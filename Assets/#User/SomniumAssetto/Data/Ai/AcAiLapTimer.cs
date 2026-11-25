using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Lap timing of one AI car from its fast-lane progress (0..1, 0 = the AC_TIME_0 line; research 5.2), with the research T3 race rule:
    /// race distance = laps + progress, initialised at the green from the grid progress (grid BEFORE the line, progress &gt; 0.5, starts at
    /// progress - 1, so the first crossing just after the green is not a lap). Race lap 1 is timed from the green; in practice the first
    /// crossing starts the first timed lap (the out lap is not a lap). A backwards crossing takes the crossing back (no lap by reversing
    /// over the line). Sector splits at the timing gates' progress values. Crossing times are interpolated inside the frame.
    /// Unity-free; no allocation per frame.
    /// </summary>
    public sealed class AcAiLapTimer
    {
        /// <summary>Completed laps (race: since the green).</summary>
        public int Laps;
        /// <summary>Race distance in laps (laps-since-green + progress, T3) - positions sort by it.</summary>
        public float RaceDistance;
        public float Progress;
        /// <summary>Current lap so far, last, best (ms; 0 = none).</summary>
        public int LapMs, LastMs, BestMs;
        /// <summary>Total race time when the car took the flag (ms; 0 = not finished).</summary>
        public int TotalMs;
        /// <summary>Current sector (0-based).</summary>
        public int Sector;
        /// <summary>Last completed lap's sector times (ms): index n = sector n (ended by gate n+1; the last ends at gate 0).</summary>
        public int[] LastSectorMs = new int[1];
        public bool Running { get; private set; }
        /// <summary>A timed lap is in progress (practice: after the first crossing; race: from the green).</summary>
        public bool Timing { get; private set; }
        public bool IsRace { get { return _race; } }

        float[] _gates = new float[0];
        int[] _cur = new int[1];
        double _lapStart, _sectorStart, _raceStart, _prevNow;
        int _crossings, _maxCrossings, _base;
        bool _race;

        /// <summary>Timing gate progress values (index = gate number; gate 0 = 0), e.g. AcTrackWorld.TimeGates projected on the line.</summary>
        public void SetGates(float[] gateProgress)
        {
            _gates = gateProgress ?? new float[0];
            int n = Math.Max(1, _gates.Length);
            _cur = new int[n];
            LastSectorMs = new int[n];
        }

        /// <summary>Starts timing at <paramref name="now"/> (s): a race at the green (with the car's grid progress) or practice.</summary>
        public void Begin(double now, float progress, bool race)
        {
            _race = race;
            Running = true;
            Laps = 0; LapMs = 0; LastMs = 0; TotalMs = 0; _crossings = 0; _maxCrossings = 0; Sector = 0;
            if (race) BestMs = 0;
            Progress = progress;
            _base = race && progress > 0.5f ? -1 : 0;
            RaceDistance = _base + progress;
            _raceStart = now; _lapStart = now; _sectorStart = now; _prevNow = now;
            Timing = race;
            for (int i = 0; i < _cur.Length; i++) _cur[i] = 0;
        }

        public void Stop() { Running = false; Timing = false; }

        /// <summary>Feeds this frame's progress at time <paramref name="now"/> (s). Returns true when a lap was completed (<see cref="LastMs"/>).</summary>
        public bool Feed(double now, float progress)
        {
            if (!Running) return false;
            float last = Progress;
            double prev = _prevNow;
            Progress = progress; _prevNow = now;
            bool lap = false;
            if (last > 0.75f && progress < 0.25f) // forward over the line // ours: wrap window
            {
                float before = 1f - last, total = before + progress;
                double t = total > 1e-6f ? prev + (now - prev) * (before / total) : now;
                _crossings++;
                if (_crossings <= _maxCrossings) { } // re-crossing after reversing over the line: nothing
                else if (_race)
                {
                    int completed = _crossings + _base;
                    if (completed >= 1 && completed > Laps) { CompleteLap(t); Laps = completed; lap = true; }
                    else Sector = 0; // the start crossing (grid before the line): lap 1 keeps timing from the green
                }
                else if (!Timing) { Timing = true; _lapStart = t; _sectorStart = t; Sector = 0; }
                else { CompleteLap(t); Laps++; lap = true; }
                if (_crossings > _maxCrossings) _maxCrossings = _crossings;
            }
            else if (last < 0.25f && progress > 0.75f) _crossings--; // backwards over the line
            else if (Timing && _gates.Length > 1 && progress > last && progress - last < 0.25f)
            {
                for (int g = 1; g < _gates.Length; g++)
                    if (last < _gates[g] && progress >= _gates[g])
                    {
                        double t = prev + (now - prev) * ((_gates[g] - last) / Math.Max(1e-6f, progress - last));
                        int idx = g - 1;
                        if (idx < _cur.Length) _cur[idx] = (int)Math.Round((t - _sectorStart) * 1000.0);
                        _sectorStart = t; Sector = g;
                    }
            }
            RaceDistance = _crossings + _base + progress;
            LapMs = Timing ? (int)((now - _lapStart) * 1000.0) : 0;
            return lap;
        }

        void CompleteLap(double t)
        {
            LastMs = (int)Math.Round((t - _lapStart) * 1000.0);
            if (BestMs == 0 || LastMs < BestMs) BestMs = LastMs;
            int lastIdx = _cur.Length - 1;
            _cur[lastIdx] = (int)Math.Round((t - _sectorStart) * 1000.0);
            for (int i = 0; i < _cur.Length; i++) { LastSectorMs[i] = _cur[i]; _cur[i] = 0; }
            _lapStart = t; _sectorStart = t; Sector = 0;
        }

        /// <summary>Marks the race finished now (the flag): total time from the green.</summary>
        public void Finish(double now) { TotalMs = (int)Math.Round((now - _raceStart) * 1000.0); }
    }
}
