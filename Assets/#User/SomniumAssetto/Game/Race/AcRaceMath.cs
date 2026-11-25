using System;
using System.Globalization;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] The race rules as pure arithmetic (Unity-FREE on purpose: the R harness copy compiles this file with Data/** and checks it
    /// on every installed layout - DevTools/agents/R/harness, probe Cmd_racedist). All positions are AC track space (right-handed, +Y up,
    /// +X = LEFT of the travel direction), the same space as <see cref="AcAiSpline"/> and the AC_TIME_n_L/R markers.
    /// Sources: research ac_track_formats.md 5.2 (progress, gate crossing), Critic addendum T3 (race distance / lap-1 rule),
    /// ac_physics_sound_rules.md 3.1-3.4 (ALLOWED_TYRES_OUT, JUMP_START_PENALTY, OVER_TIME_MULT, RACE_OVER_TIME).
    /// </summary>
    public static class AcRaceMath
    {
        /// <summary>A gate-0 crossing completes a lap only when the race distance reached the next whole lap minus this (laps). Must stay
        /// well below the smallest grid-before-the-line offset to 1.0 (laguna seca pole 0.9775 -> 0.0225 is far below 1 - 0.1). // ours</summary>
        public const float LapTolerance = 0.1f; // ours
        /// <summary>The gate segment is extended by this fraction at both ends (cars on the kerb beside a marker still cross). // ours</summary>
        public const float GateMargin = 0.08f; // ours
        /// <summary>A crossing counts only when the car is within this height (m) of the gate markers (roads passing under / over). // ours</summary>
        public const float GateHeight = 8f; // ours
        /// <summary>The start-light steps: one red every second, AC style (timing lives in acs.exe code, not in a file - research 3.3). // ours</summary>
        public const int RedLights = 5; // ours
        public const double RedStepSeconds = 1.0; // ours
        /// <summary>All reds lit, then a random hold before lights out (research: "randomised 1-3 s - ours"). // ours</summary>
        public const int HoldMinMs = 1000, HoldMaxMs = 3000; // ours
        /// <summary>A car that moves more than this (m) from its grid spot before the green made a jump start (brief). // ours</summary>
        public const float JumpStartMetres = 0.5f; // ours
        /// <summary>JUMP_START_PENALTY 2 (drive-through in AC) is applied as this time penalty (no drive-through lane logic in iteration 1). // ours</summary>
        public const int JumpStartPenaltyMs = 5000; // ours

        // ------------------------------------------------------------------ gates

        /// <summary>
        /// The car moved a -> b this step: does it cross the gate L -> R (XZ segments, with <see cref="GateMargin"/>; the car within
        /// <see cref="GateHeight"/> of <paramref name="roadY"/>)? <paramref name="roadY"/> = the road height at the gate: take the fast lane's
        /// height there, NOT the markers' (spa AC_TIME_1 markers sit 9 m below the road - verified, probe racegates).
        /// <paramref name="s"/> = fraction along a -> b at the crossing (to interpolate the crossing time), <paramref name="forward"/> = the
        /// motion goes in the race direction (L is LEFT of travel, so the travel direction is (-g.z, g.x) for g = L - R in AC space).
        /// </summary>
        public static bool CrossGate(float ax, float ay, float az, float bx, float by, float bz,
                                     float lx, float lz, float rx, float rz, float roadY, out float s, out bool forward)
        {
            s = 0f; forward = false;
            float mx = bx - ax, mz = bz - az;              // motion
            float gx = rx - lx, gz = rz - lz;              // gate L -> R
            float den = Cross(mx, mz, gx, gz);
            if (den > -1e-9f && den < 1e-9f) return false; // parallel / no motion
            float qx = lx - ax, qz = lz - az;
            float t = Cross(qx, qz, gx, gz) / den;         // along the motion
            float u = Cross(qx, qz, mx, mz) / den;         // along the gate (0 = L, 1 = R)
            if (t < 0f || t > 1f || u < -GateMargin || u > 1f + GateMargin) return false;
            float carY = ay + (by - ay) * t;
            if (Math.Abs(carY - roadY) > GateHeight) return false;
            s = t;
            float fx, fz;
            GateForward(lx, lz, rx, rz, out fx, out fz);
            forward = mx * fx + mz * fz > 0f;
            return true;
        }

        /// <summary>Unit race direction across a gate in AC space: L is LEFT of travel, left = up x forward = (f.z, -f.x), so f = (-g.z, g.x), g = L - R.</summary>
        public static void GateForward(float lx, float lz, float rx, float rz, out float fx, out float fz)
        {
            float gx = lx - rx, gz = lz - rz;
            float len = (float)Math.Sqrt(gx * gx + gz * gz);
            if (len < 1e-6f) { fx = 0f; fz = 1f; return; }
            fx = -gz / len; fz = gx / len;
        }

        static float Cross(float ax, float az, float bx, float bz) { return ax * bz - az * bx; }

        /// <summary>The road height at a gate: the fast lane's height at the point nearest the gate's middle (markers may float or sit
        /// metres below the road); the markers' mean height without a line.</summary>
        public static float GateRoadY(AcAiSpline line, float lx, float ly, float lz, float rx, float ry, float rz)
        {
            float mx = (lx + rx) * 0.5f, my = (ly + ry) * 0.5f, mz = (lz + rz) * 0.5f;
            if (line == null || line.IsEmpty) return my;
            int n = line.Nearest(mx, my, mz);
            return n >= 0 ? line.Y(n) : my;
        }

        /// <summary>Fast-lane point nearest to (x, y, z) among those within <paramref name="lapWindow"/> of the line (progress 0): grid
        /// slots and the start line always lie there (research T3: 0.97..1 or 0..0.054). Needed where one stretch of tarmac is driven
        /// twice per lap (ks_monza66/full: slots 2-6 project to progress 0.575 with a plain nearest search - verified, probe racedist).</summary>
        public static int NearestNearLine(AcAiSpline line, float x, float y, float z, float lapWindow)
        {
            if (line == null || line.IsEmpty) return -1;
            if (!line.IsClosed) return line.Nearest(x, y, z);
            int w = (int)(line.Count * lapWindow);
            return line.NearestNear(x, y, z, 0, w);
        }

        // ------------------------------------------------------------------ race distance (research T3)

        /// <summary>Race distance at the green for a car at fast-lane progress <paramref name="progress"/>: a car on the grid BEFORE the line
        /// (progress &gt; 0.5) starts negative, so its first gate-0 crossing seconds after the green is the start, not a lap (T3).</summary>
        public static float InitialDistance(float progress) { return progress > 0.5f ? progress - 1f : progress; }

        /// <summary>Progress change prev -> cur on a closed lap, wrapped into [-0.5, 0.5).</summary>
        public static float WrapDelta(float prev, float cur)
        {
            float d = cur - prev;
            if (d >= 0.5f) d -= 1f; else if (d < -0.5f) d += 1f;
            return d;
        }

        /// <summary>T3 rule: a forward gate-0 crossing completes lap (lapsDone + 1) only when the race distance passed that whole lap
        /// (minus <see cref="LapTolerance"/>).</summary>
        public static bool RaceLapCounts(float distance, int lapsDone) { return distance >= lapsDone + 1 - LapTolerance; }

        /// <summary>Practice / qualify / hotlap: a timed lap counts when the car covered (almost) a whole lap since the lap started.</summary>
        public static bool TimedLapCounts(float distance, float lapStartDistance) { return distance - lapStartDistance >= 1f - LapTolerance; }

        // ------------------------------------------------------------------ cut rule (ALLOWED_TYRES_OUT)

        /// <summary>[LAP_INVALIDATOR] ALLOWED_TYRES_OUT: more than <paramref name="allowedTyresOut"/> wheels on IS_VALID_TRACK=0 surfaces cut the
        /// lap; -1 disables the rule (research 3.4, server default 2 = three or more wheels out is a cut). [BELIEF on the exact count rule]</summary>
        public static bool IsCut(int wheelsOff, int allowedTyresOut) { return allowedTyresOut >= 0 && wheelsOff > allowedTyresOut; }

        // ------------------------------------------------------------------ start lights (shared clock)

        /// <summary>The random hold (ms) between "all reds lit" and lights out, derived from the published green time itself (ms precision,
        /// as the net carries it) so every client computes the same hold without an extra field.</summary>
        public static int HoldMs(double startUtc)
        {
            long ms = (long)Math.Round(startUtc * 1000.0);
            unchecked
            {
                ulong h = (ulong)ms * 0x9E3779B97F4A7C15UL;
                h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
                return HoldMinMs + (int)(h % (ulong)(HoldMaxMs - HoldMinMs + 1));
            }
        }

        /// <summary>When the first red comes on (UTC seconds) for a green at <paramref name="startUtc"/>.</summary>
        public static double LightsStartUtc(double startUtc) { return startUtc - HoldMs(startUtc) / 1000.0 - (RedLights - 1) * RedStepSeconds; }

        /// <summary>Lit reds at <paramref name="nowUtc"/> (0 before the sequence, 1..5 during, 0 with green after <paramref name="startUtc"/>).</summary>
        public static int RedsAt(double nowUtc, double startUtc, out bool green)
        {
            green = nowUtc >= startUtc;
            if (green) return 0;
            double first = LightsStartUtc(startUtc);
            if (nowUtc < first) return 0;
            int n = 1 + (int)Math.Floor((nowUtc - first) / RedStepSeconds);
            return n > RedLights ? RedLights : n;
        }

        /// <summary>Red steps 0..5 mapped onto a track's KS_START_LIGHT count (magione has 4 lamps, monza 5): ceil(red * count / 5).</summary>
        public static int LampsFor(int reds, int lampCount)
        {
            if (reds <= 0 || lampCount <= 0) return 0;
            return Math.Min(lampCount, (reds * lampCount + RedLights - 1) / RedLights);
        }

        // ------------------------------------------------------------------ text

        /// <summary>AC's lap-time text "m:ss.fff" ("-:--.---" for none).</summary>
        public static string FormatLap(int ms)
        {
            if (ms <= 0) return "-:--.---";
            int m = ms / 60000, s = ms / 1000 % 60, f = ms % 1000;
            return m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture) + "." + f.ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>A gap "+1.234" / "+1 LAP" / "+2 LAPS".</summary>
        public static string FormatGap(int gapMs, int lapsBehind)
        {
            if (lapsBehind > 0) return "+" + lapsBehind.ToString(CultureInfo.InvariantCulture) + (lapsBehind == 1 ? " LAP" : " LAPS");
            if (gapMs <= 0) return "";
            return "+" + (gapMs / 1000).ToString(CultureInfo.InvariantCulture) + "." + (gapMs % 1000).ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>Lower-case FNV-1a hash of a name (stable across clients; for logs / seeds). </summary>
        public static int NameHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++) { h ^= char.ToLowerInvariant(s[i]); h *= 16777619; }
                return (int)h;
            }
        }
    }
}
