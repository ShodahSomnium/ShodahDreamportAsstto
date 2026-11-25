using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>How a <see cref="AcRaceTimer"/> counts laps.</summary>
    public enum AcTimerMode
    {
        /// <summary>Practice / qualify / hotlap: an out lap until the first gate-0 crossing, then timed laps.</summary>
        Timed = 0,
        /// <summary>Race: race distance from the green (research T3), lap 1 timed from the green.</summary>
        Race = 1,
    }

    /// <summary>
    /// [R, Race] One car's timing (Unity-FREE: R's harness copy runs it on every installed layout). The owner of a car times it from its own
    /// physics poses (research: race timing is owner-authoritative): the local player's car here, AI cars on the master. Every step gets the
    /// car's AC-space position, the clock and how many wheels are off IS_VALID_TRACK; it detects AC_TIME_n gate crossings between two poses
    /// (segment vs gate, race direction from L/R), sectors, lap times, the cut rule (ALLOWED_TYRES_OUT), the T3 lap-1 rule (race distance =
    /// laps + fast-lane progress, initialised at the green), the live delta to the best lap, wrong way, the finish. No allocation per step.
    /// </summary>
    public sealed class AcRaceTimer
    {
        /// <summary>Step result bits.</summary>
        public const int LapDone = 1, SectorDone = 2, CutStarted = 4, TimingStarted = 8, FinishedNow = 16, CrossedBackwards = 32;
        /// <summary>Delta trace resolution (bins per lap). // ours</summary>
        public const int TraceBins = 1000; // ours
        /// <summary>Wrong way: moving against the line faster than this (m/s) for <see cref="WrongWaySeconds"/>. // ours</summary>
        public const float WrongWaySpeed = 3f, WrongWaySeconds = 2f; // ours
        /// <summary>No AI line (drift): a lap needs this many seconds since the last one when the layout has a single gate. // ours</summary>
        public const double MinLapSecondsWithoutLine = 20.0; // ours
        /// <summary>At the green the grid is searched only within this fraction of a lap around the line (research T3: grids lie within 0.054). // ours</summary>
        public const float StartWindow = 0.15f; // ours
        /// <summary>Open course without a start gate (drag): a timed run starts when the car has moved this far from where it was put (m). // ours</summary>
        public const float MoveOffMetres = 0.5f; // ours

        // ---- setup
        private AcAiSpline _line;
        private float[] _gates = new float[0];   // 6 floats per gate: L xyz, R xyz (AC space), index = gate number
        private int _gateCount;
        private float[] _gateY = new float[0];   // road height at each gate (fast lane), see AcRaceMath.CrossGate
        // open course (point-to-point AC_AB_START/FINISH, drag AC_OPEN_FINISH): the AC_TIME_n gates are splits, one run = one "lap"
        private bool _open;
        private float[] _startGate, _finishGate;  // 6 floats each (L xyz, R xyz); start null = drag (run starts on moving off / the green)
        private float _startY, _finishY, _openStartP, _openFinishP = 1f;
        private bool _armed;                      // drag without a start gate: the next move-off starts a run
        private float _restX, _restZ;
        private bool _partialLap;                 // seeded mid-lap (master hand-over): the lap in progress has no known start time

        // ---- state (public read)
        public AcTimerMode Mode { get; private set; }
        /// <summary>Completed (counted) laps.</summary>
        public int Laps;
        /// <summary>Current lap time so far (ms) - the out lap too (not recorded).</summary>
        public int LapMs;
        public int LastMs, BestMs, DeltaMs;
        /// <summary>The current sector (0-based) = sector gates passed this lap.</summary>
        public int Sector;
        public bool LapValid = true;
        /// <summary>Practice/qualify/hotlap: still on the out lap (no gate-0 crossing yet).</summary>
        public bool OutLap = true;
        /// <summary>Race distance in laps (laps since the green + progress; T3). Timed mode: distance since the timer began.</summary>
        public float RaceDistance;
        /// <summary>Fast-lane progress 0..1 (0 = AC_TIME_0), NaN without a line.</summary>
        public float Progress = float.NaN;
        public bool Finished;
        /// <summary>Race time at the finish (ms from the green, without penalties).</summary>
        public int FinishedMs;
        public int PenaltyMs;
        /// <summary>Cuts this session / this lap.</summary>
        public int Cuts, LapCuts;
        public bool WrongWay;
        /// <summary>The last completed lap: valid, cuts, sector times (one per gate; gate n ends sector n-1, gate 0 ends the last).</summary>
        public bool LastValid = true;
        public int LastCuts;
        public int[] LastSectorMs = new int[0];
        public int[] BestSectorMs = new int[0];
        /// <summary>Race: the car must finish at its next counted lap (the leader finished / the time is up). Set by the director.</summary>
        public bool FinishOnNextLap;
        /// <summary>Race: laps to run (0 = timed race).</summary>
        public int TargetLaps;
        private int _pendingBits;
        /// <summary>Qualify: laps that START at or after this clock time are not timed (QUALIFY_MAX_WAIT_PERC window). +inf = no cutoff.</summary>
        public double CutoffTime = double.PositiveInfinity;
        /// <summary>The session is over for this car (no new timed lap after the cutoff, or finished the race).</summary>
        public bool SessionOver;
        /// <summary>ALLOWED_TYRES_OUT for the cut rule (-1 = off).</summary>
        public int AllowedTyresOut = 2;
        /// <summary>Time of the last counted lap crossing (clock seconds) - finish order, gaps.</summary>
        public double LastCrossTime;

        private bool _started, _cutting, _backOverLine, _hasBest;
        private float _px, _py, _pz, _prevProgress, _lapStartDistance;
        private double _pt, _lapStartT, _startT, _wrongSince;
        private int _hint = -1, _lastBin = -1;
        private int[] _gateMs = new int[1];     // ms since lap start at each sector gate this lap
        private int[] _cur = new int[TraceBins], _best = new int[TraceBins];

        /// <summary>The track: its fast lane (may be null / empty: drift) and gates as 6 floats each (L xyz, R xyz in AC space), gate 0 first.</summary>
        public void Setup(AcAiSpline line, float[] gates, int gateCount)
        {
            _open = false; _startGate = null; _finishGate = null;
            _line = line != null && !line.IsEmpty ? line : null;
            _gateCount = Math.Max(0, gateCount);
            _gates = gates ?? new float[0];
            if (_gates.Length < _gateCount * 6) _gateCount = _gates.Length / 6;
            if (_gateY.Length != _gateCount) _gateY = new float[_gateCount];
            for (int g = 0; g < _gateCount; g++)
            {
                int o = g * 6;
                _gateY[g] = AcRaceMath.GateRoadY(_line, _gates[o], _gates[o + 1], _gates[o + 2], _gates[o + 3], _gates[o + 4], _gates[o + 5]);
            }
            int n = Math.Max(1, _gateCount);
            if (LastSectorMs.Length != n) { LastSectorMs = new int[n]; BestSectorMs = new int[n]; }
            if (_gateMs.Length != n + 1) _gateMs = new int[n + 1];
            _started = false;
        }

        public int GateCount { get { return _gateCount; } }
        public bool HasLine { get { return _line != null; } }
        /// <summary>Open course (point-to-point / drag): one run from the start (gate, the green, or moving off) to the finish gate is a "lap".</summary>
        public bool IsOpen { get { return _open; } }
        /// <summary>Sectors per lap: closed = one per gate (min 1); open = splits + 1.</summary>
        public int SectorCount { get { return _open ? _gateCount + 1 : Math.Max(1, _gateCount); } }

        /// <summary>Open course: <paramref name="splits"/> (AC_TIME_n, 6 floats each) are split gates; <paramref name="start"/> = AC_AB_START
        /// (null = drag: a timed run starts when the car moves off, a race at the green); <paramref name="finish"/> = AC_AB_FINISH /
        /// AC_OPEN_FINISH (6 floats, required). The line may be open (trento-bondone) or the drag strip's shared pit/fast lane.</summary>
        public void SetupOpen(AcAiSpline line, float[] splits, int splitCount, float[] start, float[] finish)
        {
            Setup(line, splits, splitCount);
            _open = finish != null && finish.Length >= 6;
            if (!_open) return;
            _startGate = start != null && start.Length >= 6 ? start : null;
            _finishGate = finish;
            _startY = _startGate != null ? AcRaceMath.GateRoadY(_line, start[0], start[1], start[2], start[3], start[4], start[5]) : 0f;
            _finishY = AcRaceMath.GateRoadY(_line, finish[0], finish[1], finish[2], finish[3], finish[4], finish[5]);
            _openStartP = _startGate != null ? GateProgress(_startGate) : 0f;
            _openFinishP = GateProgress(_finishGate);
            if (float.IsNaN(_openStartP)) _openStartP = 0f;
            if (float.IsNaN(_openFinishP) || _openFinishP <= _openStartP) _openFinishP = 1f;
            int n = SectorCount;
            if (LastSectorMs.Length != n) { LastSectorMs = new int[n]; BestSectorMs = new int[n]; }
            if (_gateMs.Length != n + 1) _gateMs = new int[n + 1];
        }

        private float GateProgress(float[] g)
        {
            if (_line == null) return float.NaN;
            int h = -1;
            return _line.Progress((g[0] + g[3]) * 0.5f, (g[1] + g[4]) * 0.5f, (g[2] + g[5]) * 0.5f, ref h);
        }

        /// <summary>Starts timing at clock <paramref name="t"/> (seconds) from the car's position: <see cref="AcTimerMode.Timed"/> = out lap,
        /// <see cref="AcTimerMode.Race"/> = the green (race distance initialised from the grid progress, T3). Clears everything.</summary>
        public void Begin(AcTimerMode mode, double t, float x, float y, float z)
        {
            Mode = mode;
            _pendingBits = 0;
            Laps = 0; LapMs = 0; LastMs = 0; BestMs = 0; DeltaMs = 0; Sector = 0; LapValid = true; OutLap = mode == AcTimerMode.Timed;
            Finished = false; FinishedMs = 0; PenaltyMs = 0; Cuts = 0; LapCuts = 0; WrongWay = false; LastValid = true; LastCuts = 0;
            FinishOnNextLap = false; SessionOver = false; CutoffTime = double.PositiveInfinity; LastCrossTime = 0;
            Array.Clear(LastSectorMs, 0, LastSectorMs.Length); Array.Clear(BestSectorMs, 0, BestSectorMs.Length);
            _hasBest = false; _cutting = false; _backOverLine = false; _wrongSince = -1; _partialLap = false;
            _hint = mode == AcTimerMode.Race && !_open ? AcRaceMath.NearestNearLine(_line, x, y, z, StartWindow) : -1;
            Progress = _line == null ? float.NaN : _hint >= 0 ? _line.ProgressAt(x, z, _hint) : ProgressOf(x, y, z);
            _prevProgress = float.IsNaN(Progress) ? 0f : Progress;
            if (_open)
            {
                // an open course has no wrap: race distance = progress along the line; a race runs from the green
                RaceDistance = mode == AcTimerMode.Race && !float.IsNaN(Progress) ? Progress : 0f;
                _armed = true; _restX = x; _restZ = z;
            }
            else RaceDistance = mode == AcTimerMode.Race && !float.IsNaN(Progress) ? AcRaceMath.InitialDistance(Progress) : 0f;
            _lapStartDistance = RaceDistance;
            _px = x; _py = y; _pz = z; _pt = t; _startT = t; _lapStartT = t;
            ClearLap();
            _started = true;
        }

        /// <summary>The car was teleported (reset to the line, back to the pits, jump-start teleport): no crossing is tested between the old and
        /// new pose. <paramref name="outLap"/> (timed mode) restarts with an out lap; otherwise the lap goes on but is invalid.</summary>
        public void Teleported(double t, float x, float y, float z, bool outLap)
        {
            if (!_started) return;
            float before = RaceDistance;
            bool lapAtTeleport = false;
            float p = ProgressOf(x, y, z);
            if (!float.IsNaN(p))
            {
                if (_open) RaceDistance = Mode == AcTimerMode.Race ? p : RaceDistance + AcRaceMath.WrapDelta(_prevProgress, p);
                else if (Mode == AcTimerMode.Race)
                {
                    // keep the whole laps, take the new fraction (T3: before the line counts as the previous lap's end)
                    float cand = (float)Math.Floor(RaceDistance) + p;
                    if (cand - RaceDistance > 0.5f) cand -= 1f; else if (RaceDistance - cand > 0.5f) cand += 1f;
                    RaceDistance = cand;
                    // the reset put the car just past the line (the nearest fast-lane point can be): the lap ends HERE, invalid - else Laps stays
                    // one behind RaceDistance for the rest of the race (no real crossing of gate 0 follows). Reported with the next Step's bits.
                    if (!Finished && _line != null && Math.Floor(cand) > Math.Floor(before) && AcRaceMath.RaceLapCounts(cand, Laps))
                    {
                        LapValid = false;
                        _backOverLine = false;
                        _pendingBits |= LineCrossed(t, true);
                        lapAtTeleport = true;
                    }
                }
                else RaceDistance += AcRaceMath.WrapDelta(_prevProgress, p);
                _prevProgress = p; Progress = p;
            }
            _px = x; _py = y; _pz = z; _pt = t;
            _backOverLine = false; _cutting = false; _wrongSince = -1; WrongWay = false;
            if (_open) { _armed = true; _restX = x; _restZ = z; }
            if (Mode == AcTimerMode.Timed && outLap)
            {
                OutLap = true; _lapStartT = t; _lapStartDistance = RaceDistance; LapMs = 0; DeltaMs = 0; ClearLap();
            }
            else if (!lapAtTeleport) Invalidate();
        }

        /// <summary>The current lap no longer counts for best laps (reset, penalty).</summary>
        public void Invalidate() { if (!OutLap) LapValid = false; }

        /// <summary>
        /// One physics step: the car is at (x, y, z) at clock <paramref name="t"/> (seconds), <paramref name="wheelsOff"/> wheels touch
        /// IS_VALID_TRACK=0 surfaces. Returns the step bits (<see cref="LapDone"/>, ...). No allocation.
        /// </summary>
        public int Step(double t, float x, float y, float z, int wheelsOff)
        {
            if (!_started) return 0;
            int bits = 0;
            if (t <= _pt) { _px = x; _py = y; _pz = z; return 0; }
            bits = _pendingBits;                           // a lap completed at a teleport (Teleported)
            _pendingBits = 0;

            // progress / race distance (clamped to what the car can physically have covered: projection glitches cannot jump it)
            float p = ProgressOf(x, y, z);
            if (!float.IsNaN(p))
            {
                float d = AcRaceMath.WrapDelta(_prevProgress, p);
                float moved = (float)Math.Sqrt((x - _px) * (x - _px) + (z - _pz) * (z - _pz));
                float lim = moved / Math.Max(1f, _line.LapLength) + 0.005f; // ours: 0.5 % of a lap slack
                if (d > lim) d = lim; else if (d < -lim) d = -lim;
                RaceDistance += d;
                _prevProgress = p; Progress = p;
                WrongWayStep(t, x, z);
            }

            // gate crossings between the last pose and this one (gate 0 last so a lap closes after its last sector)
            if (_open) bits |= OpenStep(t, x, y, z);
            else
            {
                for (int g = 1; g < _gateCount; g++) bits |= GateStep(g, t, x, y, z);
                if (_gateCount > 0) bits |= GateStep(0, t, x, y, z);
            }

            // cut rule
            if (!OutLap && !SessionOver && AcRaceMath.IsCut(wheelsOff, AllowedTyresOut))
            {
                if (!_cutting) { _cutting = true; Cuts++; LapCuts++; LapValid = false; bits |= CutStarted; }
            }
            else if (wheelsOff <= Math.Max(0, AllowedTyresOut)) _cutting = false;

            // running lap time + delta trace
            LapMs = (int)Math.Round((t - _lapStartT) * 1000.0);
            if (!OutLap && !SessionOver) TraceStep();

            _px = x; _py = y; _pz = z; _pt = t;
            return bits;
        }

        private int GateStep(int g, double t, float x, float y, float z)
        {
            int o = g * 6;
            float s; bool fwd;
            if (!AcRaceMath.CrossGate(_px, _py, _pz, x, y, z, _gates[o], _gates[o + 2], _gates[o + 3], _gates[o + 5], _gateY[g], out s, out fwd))
                return 0;
            double tc = _pt + (t - _pt) * s;
            if (g == 0) return LineCrossed(tc, fwd);
            if (!fwd || OutLap || SessionOver) return 0;
            if (g <= Sector) return 0;                    // re-crossing a split already passed this lap
            _gateMs[g] = (int)Math.Round((tc - _lapStartT) * 1000.0);
            Sector = g;
            return SectorDone;
        }

        /// <summary>Open course: start (gate / move-off), splits in order, finish gate. One run = one counted "lap".</summary>
        private int OpenStep(double t, float x, float y, float z)
        {
            int bits = 0;
            float s; bool fwd;
            if (Finished) return 0;
            if (OutLap)
            {
                if (SessionOver) return 0;
                double tc;
                if (_startGate != null)
                {
                    if (!AcRaceMath.CrossGate(_px, _py, _pz, x, y, z, _startGate[0], _startGate[2], _startGate[3], _startGate[5], _startY, out s, out fwd) || !fwd) return 0;
                    tc = _pt + (t - _pt) * s;
                }
                else
                {
                    float dx = x - _restX, dz = z - _restZ;
                    if (!_armed || dx * dx + dz * dz < MoveOffMetres * MoveOffMetres) return 0;
                    tc = _pt;                                       // standing start: the run began at the last pose at rest
                }
                if (tc >= CutoffTime) { SessionOver = true; return 0; }
                _armed = false;
                StartLap(tc); OutLap = false;
                bits |= TimingStarted;
            }
            for (int k = Sector; k < _gateCount; k++)
            {
                int o = k * 6;
                if (!AcRaceMath.CrossGate(_px, _py, _pz, x, y, z, _gates[o], _gates[o + 2], _gates[o + 3], _gates[o + 5], _gateY[k], out s, out fwd) || !fwd) continue;
                _gateMs[k + 1] = (int)Math.Round((_pt + (t - _pt) * s - _lapStartT) * 1000.0);
                Sector = k + 1;
                bits |= SectorDone;
            }
            if (!AcRaceMath.CrossGate(_px, _py, _pz, x, y, z, _finishGate[0], _finishGate[2], _finishGate[3], _finishGate[5], _finishY, out s, out fwd) || !fwd) return bits;
            double tf = _pt + (t - _pt) * s;
            if (Mode == AcTimerMode.Timed && _lapStartT >= CutoffTime) { SessionOver = true; OutLap = true; return bits; }
            CompleteLap(tf);
            bits |= LapDone;
            if (Mode == AcTimerMode.Race)
            {
                Finished = true; SessionOver = true;
                FinishedMs = (int)Math.Round((tf - _startT) * 1000.0);
                bits |= FinishedNow;
            }
            else
            {
                OutLap = true;                                      // the next run needs the start again (gate, or put back + move off)
                if (tf >= CutoffTime) SessionOver = true;
            }
            LapValid = true; LapCuts = 0; Sector = 0; _cutting = false;
            return bits;
        }

        /// <summary>
        /// Race mode, after <see cref="Begin"/> at the green: carries over what another timer already counted for this car (a new instance
        /// master taking over AI cars mid-race, an AI car rebuilt after the green). <paramref name="raceDistance"/> picks the lap the current
        /// progress belongs to; the lap in progress has no known start here, so its time is not recorded (LastMs keeps <paramref name="lastMs"/>).
        /// </summary>
        public void Seed(int laps, float raceDistance, int bestMs, int lastMs, bool finished, int finishedMs, int penaltyMs)
        {
            if (!_started || Mode != AcTimerMode.Race) return;
            Laps = Math.Max(0, laps);
            BestMs = Math.Max(0, bestMs); LastMs = Math.Max(0, lastMs); PenaltyMs = Math.Max(0, penaltyMs);
            if (_line != null)
            {
                // Begin's race search looks only near the start line (grid); a car seeded mid-lap needs the whole line
                _hint = -1;
                Progress = ProgressOf(_px, _py, _pz);
                _prevProgress = float.IsNaN(Progress) ? 0f : Progress;
            }
            if (_open || float.IsNaN(Progress)) RaceDistance = Math.Max(RaceDistance, raceDistance);
            else
            {
                float cand = (float)Math.Floor(raceDistance) + Progress;
                if (cand - raceDistance > 0.5f) cand -= 1f; else if (raceDistance - cand > 0.5f) cand += 1f;
                RaceDistance = cand;
            }
            _lapStartDistance = Laps > 0 ? RaceDistance : Math.Min(0f, _lapStartDistance);
            if (finished) { Finished = true; SessionOver = true; FinishedMs = Math.Max(0, finishedMs); }
            else if (Laps > 0) { _partialLap = true; LapValid = false; }  // the lap in progress began before this timer: not timed here
            // (lap 1 began at the green = this timer's start: its time stays exact)
        }

        private int LineCrossed(double tc, bool fwd)
        {
            if (!fwd) { _backOverLine = true; return CrossedBackwards; }
            if (_backOverLine) { _backOverLine = false; return 0; } // came back over the line it reversed across: no lap
            int bits = 0;
            if (Mode == AcTimerMode.Timed)
            {
                if (OutLap)
                {
                    if (tc >= CutoffTime) { SessionOver = true; return 0; }
                    StartLap(tc); OutLap = false; return TimingStarted;
                }
                if (!LapCovered(tc, _lapStartDistance)) return 0;
                bool timed = _lapStartT < CutoffTime && !SessionOver;
                if (timed) { CompleteLap(tc); bits |= LapDone; }
                if (tc >= CutoffTime) { SessionOver = true; OutLap = false; LapValid = true; return bits; }
                StartLap(tc);
                return bits;
            }
            // race
            if (Finished) return 0;
            if (_line != null ? !AcRaceMath.RaceLapCounts(RaceDistance, Laps) : !LapCovered(tc, 0f)) return 0;
            CompleteLap(tc); bits |= LapDone;
            if ((TargetLaps > 0 && Laps >= TargetLaps) || FinishOnNextLap)
            {
                Finished = true; SessionOver = true;
                FinishedMs = (int)Math.Round((tc - _startT) * 1000.0);
                bits |= FinishedNow;
            }
            StartLap(tc);
            return bits;
        }

        /// <summary>Timed mode / no line: the car covered (almost) a whole lap since the lap began.</summary>
        private bool LapCovered(double tc, float lapStartDistance)
        {
            if (_line != null) return AcRaceMath.TimedLapCounts(RaceDistance, lapStartDistance);
            // no fast lane (drift): every split passed this lap, or a single gate and enough time
            if (_gateCount > 1) return Sector == _gateCount - 1;
            return tc - _lapStartT >= MinLapSecondsWithoutLine;
        }

        private void CompleteLap(double tc)
        {
            int ms = (int)Math.Round((tc - _lapStartT) * 1000.0);
            Laps++;
            LastCrossTime = tc;
            if (_line == null && !_open) RaceDistance = Laps;
            if (_partialLap)
            {
                // seeded mid-lap: the lap counts, its time is unknown here (LastMs / sectors keep the seeded values)
                _partialLap = false; LastValid = false; LastCuts = LapCuts;
                return;
            }
            LastMs = ms; LastValid = LapValid; LastCuts = LapCuts;
            // sector times: gate n ends sector n-1; gate 0 (the line) ends the last sector (open course: split k ends sector k, the finish the last)
            int n = SectorCount;
            _gateMs[0] = 0; _gateMs[n] = ms;
            for (int k = 0; k < n; k++)
            {
                int a = _gateMs[k], b = _gateMs[k + 1];
                LastSectorMs[k] = a >= 0 && b > a && (k == 0 || a > 0) ? b - a : 0;
            }
            if (LapValid && (BestMs <= 0 || ms < BestMs))
            {
                BestMs = ms;
                Array.Copy(LastSectorMs, BestSectorMs, n);
                int[] tmp = _best; _best = _cur; _cur = tmp; _hasBest = true;
                // the trace of the new best ends at the line: fill its tail
                int last = _best[TraceBins - 1] > 0 ? _best[TraceBins - 1] : ms;
                for (int i = TraceBins - 1; i >= 0 && _best[i] <= 0; i--) _best[i] = last;
            }
        }

        private void StartLap(double tc)
        {
            _lapStartT = tc; _lapStartDistance = RaceDistance;
            LapValid = true; LapCuts = 0; Sector = 0; DeltaMs = 0; _cutting = false;
            ClearLap();
        }

        private void ClearLap()
        {
            for (int i = 0; i < _gateMs.Length; i++) _gateMs[i] = 0;
            Array.Clear(_cur, 0, TraceBins);
            _lastBin = -1;
        }

        private void TraceStep()
        {
            float frac;
            if (_open) frac = _line != null && !float.IsNaN(Progress) ? (Progress - _openStartP) / (_openFinishP - _openStartP) : (float)Sector / SectorCount;
            else if (_line != null) frac = RaceDistance - _lapStartDistance;
            else if (_gateCount > 1) frac = (float)Sector / _gateCount;
            else return;
            if (frac < 0f) frac = 0f;
            int bin = (int)(frac * TraceBins);
            if (bin >= TraceBins) bin = TraceBins - 1;
            for (int i = _lastBin + 1; i <= bin; i++) _cur[i] = Math.Max(1, LapMs);
            if (bin > _lastBin) _lastBin = bin;
            DeltaMs = _hasBest && _best[bin] > 0 ? LapMs - _best[bin] : 0;
        }

        private void WrongWayStep(double t, float x, float z)
        {
            if (_line == null || _hint < 0) { WrongWay = false; return; }
            int i = _hint, j = i + 1 >= _line.Count ? 0 : i + 1;
            float fx = _line.X(j) - _line.X(i), fz = _line.Z(j) - _line.Z(i);
            float dt = (float)(t - _pt);
            float vx = (x - _px) / Math.Max(1e-4f, dt), vz = (z - _pz) / Math.Max(1e-4f, dt);
            float fl = (float)Math.Sqrt(fx * fx + fz * fz);
            float along = fl > 1e-5f ? (vx * fx + vz * fz) / fl : 0f;
            if (along < -WrongWaySpeed) { if (_wrongSince < 0) _wrongSince = t; WrongWay = t - _wrongSince >= WrongWaySeconds; }
            else if (along > 0f) { _wrongSince = -1; WrongWay = false; }
        }

        private float ProgressOf(float x, float y, float z)
        {
            if (_line == null) return float.NaN;
            return _line.Progress(x, y, z, ref _hint);
        }
    }
}
