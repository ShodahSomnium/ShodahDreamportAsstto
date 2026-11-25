using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One car this client times (its own car; AI cars on the master): the Unity-free <see cref="AcRaceTimer"/> + where it stood.</summary>
    public sealed class AcCarTiming
    {
        public readonly AcRaceTimer Timer = new AcRaceTimer();
        public AcCarRig Rig;
        /// <summary>Photon id (local player; -1 offline) or -(100 + index) for AI. // ours convention (AcLap.PlayerId)</summary>
        public int Id = -1;
        public string Name = "";
        public bool IsLocal;
        /// <summary>Where the car was put on the grid (jump-start check).</summary>
        public Vector3 GridPosition;
        public bool OnGrid, JumpStart, Begun;
        public double LastClock = -1;
        public int GridSlot = -1;
        /// <summary>AI car with no grid slot in the running race (more cars than AC_START_n): held frozen, not timed, until practice.</summary>
        public bool Benched;
        /// <summary>Retired from the running race (engine blown, iteration 2): DNF, not timed until the next session.</summary>
        public bool Retired;
    }

    /// <summary>
    /// [R, Race] Timing on the Unity side: the gates of the loaded layout (W's AcTrackWorld.TimeGates -> AC space for the timer), stepping
    /// each timed car from its physics pose once per physics step, the wheels on IS_VALID_TRACK=0 surfaces (cut rule) and on IS_PITLANE
    /// surfaces (pit lane), the local player's AcLocal race fields, lap events (AcSession.NotifyLap + log), and laps of remote players
    /// seen in their records. No allocation per frame (laps allocate their AcLap).
    /// </summary>
    public static class AcTiming
    {
        /// <summary>In the pit lane: at least this many wheels on IS_PITLANE surfaces to enter, none for <see cref="PitLeaveSeconds"/> to leave. // ours</summary>
        public const int PitEnterWheels = 2; // ours
        public const float PitLeaveSeconds = 0.5f; // ours
        /// <summary>No IS_PITLANE surface on the layout: within this lateral distance of ai/pit_lane.ai (and inside its 3..97 %) = in the pit lane. // ours</summary>
        public const float PitLaneMetres = 4f; // ours
        /// <summary>AI cars the net carries (N's AI blocks: 9 x 2).</summary>
        public const int MaxAiCars = 18;

        private static float[] _gates = new float[0];
        private static int _gateCount;
        private static bool _layoutHasPitSurface;
        private static float _pitClearSince = -1f;
        private static int _pitHint = -1;
        private static readonly int[] RemoteLaps = new int[AcNetSlots.PlayerCount];
        private static readonly bool[] RemoteValid = new bool[AcNetSlots.PlayerCount];
        private static readonly int[] RemoteCuts = new int[AcNetSlots.PlayerCount];
        private static readonly int[] RemoteAiLaps = new int[MaxAiCars];

        /// <summary>The local player's car timing (always the same object).</summary>
        public static readonly AcCarTiming Local = new AcCarTiming { IsLocal = true };
        /// <summary>AI cars timed here (master), by AI index.</summary>
        public static readonly List<AcCarTiming> Ai = new List<AcCarTiming>();

        public static int GateCount { get { return _gateCount; } }
        /// <summary>The layout is an open course (point-to-point AC_AB_START/FINISH or drag AC_OPEN_FINISH): one run = one "lap".</summary>
        public static bool OpenCourse { get { return _openFinish != null; } }
        /// <summary>Sectors per lap on the loaded layout.</summary>
        public static int SectorCount { get { return Local.Timer.SectorCount; } }

        private static float[] _openStart, _openFinish;   // AC space, 6 floats (L xyz, R xyz); null = closed circuit

        /// <summary>After a track load: gates (contiguous AC_TIME_0..n with both markers), open-course gates, pit surfaces, timers reset.</summary>
        public static void SetupTrack()
        {
            AcTrackWorld_Gates();
            _openStart = null; _openFinish = null;
            string course = "circuit";
            AcGate ab0 = AcTrackWorld.AbStart, ab1 = AcTrackWorld.AbFinish, of = AcTrackWorld.OpenFinish;
            if (ab0.Index >= 0 && ab1.Index >= 0) { _openStart = Gate6(ab0); _openFinish = Gate6(ab1); course = "point-to-point (AC_AB_START -> AC_AB_FINISH, AC_TIME gates are splits)"; }
            else if (of.Index >= 0) { _openFinish = Gate6(of); course = "drag (standing start -> AC_OPEN_FINISH)"; }
            _layoutHasPitSurface = false;
            AcSurfaces s = AcTrackWorld.Surfaces;
            if (s != null) for (int i = 0; i < s.Surfaces.Count; i++) if (s.Surfaces[i].IsPitlane) { _layoutHasPitSurface = true; break; }
            SetupTimer(Local.Timer);
            Local.Begun = false; Local.Rig = null;
            Ai.Clear();
            for (int i = 0; i < RemoteLaps.Length; i++) { RemoteLaps[i] = -1; RemoteValid[i] = true; RemoteCuts[i] = 0; }
            for (int i = 0; i < RemoteAiLaps.Length; i++) RemoteAiLaps[i] = -1;
            _pitHint = -1; _pitClearSince = -1f;
            AcLocal.LastSectorMs = Local.Timer.LastSectorMs;
            AcAiSpline line = AcTrackWorld.AiLine;
            AcLog.Info("Race: timing " + course + ", " + _gateCount + " gate(s) (" + Local.Timer.SectorCount + " sectors), fast lane " + (line != null && !line.IsEmpty ? line.Count + " points, " + line.LapLength.ToString("0") + " m" + (line.IsClosed ? "" : " (open)") : "none (gate-only timing)")
                       + ", pit lane by " + (_layoutHasPitSurface ? "IS_PITLANE surfaces" : AcTrackWorld.PitLane != null && !AcTrackWorld.PitLane.IsEmpty ? "pit_lane.ai distance" : "nothing (no pit lane)"));
            if (_openFinish == null && line != null && !line.IsEmpty && !line.IsClosed)
                AcLog.Warn("Race: the fast lane is open but the layout has no AC_AB_START/FINISH or AC_OPEN_FINISH gates - laps cannot be timed here");
        }

        /// <summary>A timer for the loaded layout (closed circuit or open course).</summary>
        private static void SetupTimer(AcRaceTimer t)
        {
            if (_openFinish != null) t.SetupOpen(AcTrackWorld.AiLine, _gates, _gateCount, _openStart, _openFinish);
            else t.Setup(AcTrackWorld.AiLine, _gates, _gateCount);
        }

        private static float[] Gate6(AcGate g)
        {
            var r = new float[6];
            float x, y, z;
            AcTrackWorld.ToAc(g.Left, out x, out y, out z); r[0] = x; r[1] = y; r[2] = z;
            AcTrackWorld.ToAc(g.Right, out x, out y, out z); r[3] = x; r[4] = y; r[5] = z;
            return r;
        }

        private static void AcTrackWorld_Gates()
        {
            AcGate[] g = AcTrackWorld.TimeGates ?? new AcGate[0];
            int n = 0;
            while (n < g.Length && g[n].Index == n) n++;
            if (_gates.Length != n * 6) _gates = new float[n * 6];
            for (int i = 0; i < n; i++)
            {
                float x, y, z;
                AcTrackWorld.ToAc(g[i].Left, out x, out y, out z);
                _gates[i * 6] = x; _gates[i * 6 + 1] = y; _gates[i * 6 + 2] = z;
                AcTrackWorld.ToAc(g[i].Right, out x, out y, out z);
                _gates[i * 6 + 3] = x; _gates[i * 6 + 4] = y; _gates[i * 6 + 5] = z;
            }
            _gateCount = n;
            if (n < g.Length) AcLog.Warn("Race: timing uses gates 0.." + (n - 1) + " of " + g.Length + " (gate indices not contiguous from 0)");
        }

        /// <summary>Forgets everything (track unloaded).</summary>
        public static void Clear()
        {
            Local.Rig = null; Local.Begun = false; Local.OnGrid = false; Local.JumpStart = false; Local.GridSlot = -1;
            Ai.Clear();
            _gateCount = 0;
            ClearLocalFields();
        }

        /// <summary>Resets the AcLocal race fields (hub, new session before the timer starts).</summary>
        public static void ClearLocalFields()
        {
            AcLocal.Laps = 0; AcLocal.LapMs = 0; AcLocal.LastMs = 0; AcLocal.BestMs = 0; AcLocal.DeltaMs = 0; AcLocal.Sector = 0;
            AcLocal.LapValid = true; AcLocal.Position = 0; AcLocal.RaceDistance = 0f; AcLocal.InPitlane = false; AcLocal.Finished = false;
            AcLocal.FinishedMs = 0; AcLocal.PenaltyMs = 0; AcLocal.Cuts = 0; AcLocal.LapCuts = 0; AcLocal.OutLap = true; AcLocal.WrongWay = false;
            AcLocal.Progress = float.NaN; AcLocal.GridSlot = -1;
        }

        /// <summary>Starts a car's timer now from where it stands (mode Timed = out lap; Race = the green at <paramref name="clock"/>).</summary>
        public static void Begin(AcCarTiming c, AcTimerMode mode, double clock)
        {
            if (c == null || c.Rig == null) return;
            Vector3 p = AcSpawner.CarPosition(c.Rig);
            float x, y, z;
            AcTrackWorld.ToAc(p, out x, out y, out z);
            c.Timer.AllowedTyresOut = AcSession.Rules != null ? AcSession.Rules.AllowedTyresOut : 2;
            c.Timer.Begin(mode, clock, x, y, z);
            c.LastClock = clock;
            c.Begun = true;
            if (c.IsLocal) WriteLocal();
        }

        /// <summary>The car was teleported (no gate tests across the jump).</summary>
        public static void Teleported(AcCarTiming c, bool outLap)
        {
            if (c == null || c.Rig == null || !c.Begun) return;
            Vector3 p = AcSpawner.CarPosition(c.Rig);
            float x, y, z;
            AcTrackWorld.ToAc(p, out x, out y, out z);
            double t = AcSpawner.CarClock(c.Rig);
            c.Timer.Teleported(t, x, y, z, outLap);
            c.LastClock = t;
            if (c.IsLocal) WriteLocal();
        }

        /// <summary>One frame for a timed car: steps its timer when the physics moved it. Returns the timer's step bits.</summary>
        public static int Step(AcCarTiming c)
        {
            if (c == null || c.Rig == null || c.Rig.Root == null || !c.Begun) return 0;
            double t = AcSpawner.CarClock(c.Rig);
            if (t <= c.LastClock) return 0;
            c.LastClock = t;
            Vector3 p = AcSpawner.CarPosition(c.Rig);
            float x, y, z;
            AcTrackWorld.ToAc(p, out x, out y, out z);
            AcTelemetry tel = c.Rig.Physics != null ? c.Rig.Physics.Telemetry : default(AcTelemetry);
            int bits = c.Timer.Step(t, x, y, z, WheelsOff(ref tel));
            if (c.IsLocal) { PitStep(ref tel, p); WriteLocal(); }
            if ((bits & AcRaceTimer.LapDone) != 0) LapEvent(c);
            return bits;
        }

        /// <summary>Wheels touching IS_VALID_TRACK=0 surfaces (cut rule).</summary>
        public static int WheelsOff(ref AcTelemetry t)
        {
            return Off(t.LF) + Off(t.RF) + Off(t.LR) + Off(t.RR);
        }

        private static int Off(AcWheelState w) { return w.Contact && w.Surface != null && !w.Surface.IsValidTrack ? 1 : 0; }
        private static int Pit(AcWheelState w) { return w.Contact && w.Surface != null && w.Surface.IsPitlane ? 1 : 0; }

        private static void PitStep(ref AcTelemetry t, Vector3 pos)
        {
            bool inPit = AcLocal.InPitlane;
            if (_layoutHasPitSurface)
            {
                int n = Pit(t.LF) + Pit(t.RF) + Pit(t.LR) + Pit(t.RR);
                bool anyContact = t.LF.Contact || t.RF.Contact || t.LR.Contact || t.RR.Contact;
                if (n >= PitEnterWheels) { inPit = true; _pitClearSince = -1f; }
                else if (n == 0 && anyContact)
                {
                    if (_pitClearSince < 0f) _pitClearSince = Time.time;
                    if (Time.time - _pitClearSince >= PitLeaveSeconds) inPit = false;
                }
            }
            else
            {
                AcAiSpline pl = AcTrackWorld.PitLane;
                if (pl != null && !pl.IsEmpty)
                {
                    float x, y, z;
                    AcTrackWorld.ToAc(pos, out x, out y, out z);
                    int i = pl.NearestNear(x, y, z, _pitHint, 30);
                    _pitHint = i;
                    float frac = pl.PointProgress(i);
                    float lat = Math.Abs(pl.LateralOffset(x, z, i));
                    float dy = Math.Abs(pl.Y(i) - y);
                    inPit = lat < PitLaneMetres && dy < 3f && frac > 0.03f && frac < 0.97f; // ours: 3 m height, 3..97 % of the spline
                }
                else inPit = false;
            }
            if (inPit != AcLocal.InPitlane)
            {
                AcLocal.InPitlane = inPit;
                AcLog.InfoOnce("Race: pit lane " + (inPit ? "entered" : "left") + " (first time; limiter " + (AcSession.Rules != null ? AcSession.Rules.PitLimitKmh : 80f) + " km/h, ours)");
            }
        }

        /// <summary>The local timer -> AcLocal (N publishes, H shows).</summary>
        public static void WriteLocal()
        {
            AcRaceTimer t = Local.Timer;
            AcLocal.Laps = t.Laps; AcLocal.LapMs = t.LapMs; AcLocal.LastMs = t.LastMs; AcLocal.BestMs = t.BestMs; AcLocal.DeltaMs = t.DeltaMs;
            AcLocal.Sector = t.Sector; AcLocal.LapValid = t.LapValid; AcLocal.RaceDistance = t.RaceDistance; AcLocal.Finished = t.Finished;
            AcLocal.FinishedMs = t.FinishedMs; AcLocal.PenaltyMs = t.PenaltyMs; AcLocal.Cuts = t.Cuts; AcLocal.LapCuts = t.LapCuts;
            AcLocal.OutLap = t.OutLap; AcLocal.WrongWay = t.WrongWay; AcLocal.Progress = t.Progress; AcLocal.LastSectorMs = t.LastSectorMs;
            AcLocal.GridSlot = Local.GridSlot;
        }

        private static void LapEvent(AcCarTiming c)
        {
            AcRaceTimer t = c.Timer;
            int n = t.SectorCount;
            var lap = new AcLap
            {
                PlayerId = c.Id, IsLocal = c.IsLocal, Name = c.Name, Lap = t.Laps, TimeMs = t.LastMs, Valid = t.LastValid, Cuts = t.LastCuts,
                SectorMs = new int[n],
            };
            Array.Copy(t.LastSectorMs, lap.SectorMs, Math.Min(n, t.LastSectorMs.Length));
            if (c.IsLocal || t.Laps == 1)
                AcLog.Info("Race: " + (c.IsLocal ? "" : c.Name + " ") + "lap " + t.Laps + " " + AcRaceMath.FormatLap(t.LastMs) + " " + (t.LastValid ? "valid" : "cut (" + t.LastCuts + ")")
                           + " (sectors " + Sectors(lap.SectorMs) + ")" + (t.LastValid && t.LastMs == t.BestMs ? " best" : ""));
            AcSession.NotifyLap(lap);
        }

        private static string Sectors(int[] s)
        {
            string r = "";
            for (int i = 0; i < s.Length; i++) r += (i > 0 ? " " : "") + (s[i] > 0 ? (s[i] / 1000f).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "-");
            return r;
        }

        /// <summary>Laps of remote players (and, on a non-master, of the master's AI cars) appear in their records: raise
        /// AcSession.LapCompleted for them too (once per lap), with the sector times the record carries (N: up to 3).</summary>
        public static void RemoteLapsStep()
        {
            IReadOnlyList<AcNetPlayer> list;
            try { list = AcNetGame.Players; } catch (Exception) { return; }
            for (int i = 0; i < list.Count; i++)
            {
                AcNetPlayer p = list[i];
                if (p == null || p.Slot < 0 || p.Slot >= RemoteLaps.Length) continue;
                int s = p.Slot;
                if (!p.OnTrack) { RemoteLaps[s] = -1; continue; }
                if (RemoteLaps[s] < 0 || p.Laps < RemoteLaps[s]) { RemoteLaps[s] = p.Laps; RemoteValid[s] = p.LapValid; RemoteCuts[s] = p.Cuts; continue; }
                if (p.Laps > RemoteLaps[s])
                {
                    RemoteLaps[s] = p.Laps;
                    // the record's LapValid / Cuts (N: LapCuts) already belong to the NEW lap when Laps grows (the owner's timer resets them at the
                    // crossing, and the record carries both in the same write): the previous frame's values were the finished lap's. The sector
                    // times (LastSectorMs) do belong to the finished lap.
                    var lap = new AcLap { PlayerId = p.PhotonId, IsLocal = false, Name = p.Name, Lap = p.Laps, TimeMs = p.LastMs, Valid = RemoteValid[s] && RemoteCuts[s] == 0, Cuts = RemoteCuts[s] };
                    lap.SectorMs = RemoteSectors(p);
                    AcSession.NotifyLap(lap);
                }
                RemoteValid[s] = p.LapValid;
                RemoteCuts[s] = p.Cuts;
            }
            RemoteAiLapsStep();
        }

        private static int[] RemoteSectors(AcNetPlayer p)
        {
            int n = Math.Min(Local.Timer.SectorCount, p.LastSectorMs.Length);
            var r = new int[Math.Max(0, n)];
            for (int k = 0; k < n; k++) r[k] = p.LastSectorMs[k];
            return r;
        }

        // the master's AI cars as seen on a non-master (N's AcNetGame.AiPlayers carry laps / last / best, no validity)
        private static void RemoteAiLapsStep()
        {
            IReadOnlyList<AcNetPlayer> list;
            try { if (AcNetGame.IsMaster) return; list = AcNetGame.AiPlayers; } catch (Exception) { return; }
            for (int i = 0; i < list.Count; i++)
            {
                AcNetPlayer p = list[i];
                if (p == null || p.AiIndex < 0 || p.AiIndex >= RemoteAiLaps.Length) continue;
                int s = p.AiIndex;
                if (!p.OnTrack) { RemoteAiLaps[s] = -1; continue; }
                if (RemoteAiLaps[s] < 0 || p.Laps < RemoteAiLaps[s]) { RemoteAiLaps[s] = p.Laps; continue; }
                if (p.Laps > RemoteAiLaps[s])
                {
                    RemoteAiLaps[s] = p.Laps;
                    AcSession.NotifyLap(new AcLap { PlayerId = p.PhotonId, IsLocal = false, Name = p.Name, Lap = p.Laps, TimeMs = p.LastMs, Valid = true }); // AI blocks carry no validity bit

                }
            }
        }

        /// <summary>The timing of AI car <paramref name="index"/> (created on demand).</summary>
        public static AcCarTiming AiTiming(int index, bool create)
        {
            for (int i = 0; i < Ai.Count; i++) if (Ai[i].Id == -(100 + index)) return Ai[i];
            if (!create) return null;
            var c = new AcCarTiming { Id = -(100 + index), IsLocal = false };
            SetupTimer(c.Timer);
            Ai.Add(c);
            return c;
        }
    }
}
