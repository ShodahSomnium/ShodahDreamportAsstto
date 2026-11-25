using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Geometry of an AI spline prepared for driving (one per spline, shared by every AI car): per point the unit tangent, the SIGNED
    /// curvature (1/m, + = turning toward AC +X = car LEFT) from the point positions, and the room either side of the line.
    ///
    /// Facts used [VERIFIED by harness <c>ailine</c> on the installed fast lanes]: payload [4] radius agrees with the geometric radius
    /// (|1/R| within a few %); payload [8] direction = the turn side (+1 left, -1 right) and the apex is on the inside (the side value on
    /// the turn side is the small one) - so [5] sideLeft is the room on the car's LEFT, [6] sideRight on its right. pit_lane.ai has no
    /// radius/sides in its payload (zeros): curvature is always taken from the geometry, pit-lane sides default to <see cref="PitSide"/>.
    ///
    /// Coordinates are AC track space (x_ac = -x_unity, z_ac = z_unity). Positions along the line are arc length s in metres from point 0
    /// (wrapping on closed lines). Unity-free; no allocation after construction.
    /// </summary>
    public sealed class AcAiTrack
    {
        public readonly AcAiSpline Spline;
        public readonly int Count;
        public readonly bool IsClosed;
        /// <summary>Lap length (closed) or total length (open), m.</summary>
        public readonly float Length;
        public readonly float[] TanX, TanZ, Curv, SideL, SideR;
        /// <summary>Vertical curvature (1/m) per point: + = compression (dip), - = crest. From the point heights over
        /// +-<see cref="VCurvHalfChord"/> m, smoothed. A crest of radius R unloads the car by v^2/R (the plan uses crests only).</summary>
        public readonly float[] VCurv;
        /// <summary>True when the side values came from the payload (fast lane).</summary>
        public readonly bool HasSides;

        /// <summary>Room each side of a pit-lane line (payload has none). // ours</summary>
        public const float PitSide = 2.5f;
        /// <summary>Chord half-length used for the geometric curvature (m). // ours</summary>
        public const float CurvHalfChord = 4f;
        /// <summary>Chord half-length for the vertical curvature (m; longer than in plan view: heights are noisier). // ours</summary>
        public const float VCurvHalfChord = 12f;

        public AcAiTrack(AcAiSpline s)
        {
            Spline = s;
            Count = s != null ? s.Count : 0;
            IsClosed = s != null && s.IsClosed;
            Length = s != null ? s.LapLength : 1f;
            TanX = new float[Count]; TanZ = new float[Count]; Curv = new float[Count]; SideL = new float[Count]; SideR = new float[Count]; VCurv = new float[Count];
            if (Count < 3) return;
            // tangents: central difference
            for (int i = 0; i < Count; i++)
            {
                int a = Idx(i - 1), b = Idx(i + 1);
                float dx = s.X(b) - s.X(a), dz = s.Z(b) - s.Z(a), l = (float)Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-5f) { dx = 0f; dz = 1f; l = 1f; }
                TanX[i] = dx / l; TanZ[i] = dz / l;
            }
            // signed curvature from the heading change over +-CurvHalfChord metres (arc-length based, robust to uneven spacing)
            var raw = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                int a = Walk(i, -CurvHalfChord), b = Walk(i, CurvHalfChord);
                float ds = Dist(a, b);
                if (ds < 1f) { raw[i] = 0f; continue; }
                // heading change from tangent a to tangent b: + when b is rotated toward +X (left) of a
                float cross = TanZ[a] * TanX[b] - TanX[a] * TanZ[b]; // (a x b).y with AC axes: left of (tx,tz) is (tz,-tx)
                float dot = TanX[a] * TanX[b] + TanZ[a] * TanZ[b];
                raw[i] = (float)Math.Atan2(cross, dot) / ds;
            }
            // light smoothing (5 points)
            for (int i = 0; i < Count; i++)
                Curv[i] = (raw[Idx(i - 2)] + raw[Idx(i - 1)] * 2f + raw[i] * 3f + raw[Idx(i + 1)] * 2f + raw[Idx(i + 2)]) / 9f;
            // vertical curvature: slope after minus slope before over the mean chord (open lines: zero at the ends)
            var rawV = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                int a = Walk(i, -VCurvHalfChord), b = Walk(i, VCurvHalfChord);
                if (a == i || b == i) continue;
                float da = Dist(a, i), db = Dist(i, b);
                if (da < 3f || db < 3f) continue;
                float slopeA = (s.Y(i) - s.Y(a)) / da, slopeB = (s.Y(b) - s.Y(i)) / db;
                rawV[i] = (slopeB - slopeA) / (0.5f * (da + db));
            }
            for (int i = 0; i < Count; i++)
                VCurv[i] = (rawV[Idx(i - 2)] + rawV[Idx(i - 1)] * 2f + rawV[i] * 3f + rawV[Idx(i + 1)] * 2f + rawV[Idx(i + 2)]) / 9f;
            HasSides = s.Payload != null && s.PayloadCount == Count && HasNonZeroSides(s);
            for (int i = 0; i < Count; i++)
            {
                SideL[i] = HasSides ? Math.Max(0.3f, s.P(i, AcAiSpline.SideLeft)) : PitSide;
                SideR[i] = HasSides ? Math.Max(0.3f, s.P(i, AcAiSpline.SideRight)) : PitSide;
            }
        }

        /// <summary>
        /// Where pit_lane.ai leaves the pit lane. The installed pit lines run on the track before the pit entry and go on along the track for
        /// hundreds of metres after the exit [VERIFIED harness <c>aipitlat</c>: magione pit line 1250 m = on track 0-230 m, pit lane 230-620 m,
        /// back on the racing line 620-1250 m]. The pit section = the longest run of points more than 1 m outside the fast lane's room
        /// (payload sides); <paramref name="exitS"/> = the first point after it back inside the room (the AI joins the fast lane there and the pit
        /// limit ends), <paramref name="waitS"/> = the last point of the run still a car width + margin (2.5 m) outside (an AI waits there for
        /// fast-lane traffic), <paramref name="exitFastS"/> = the exit's arc length on the fast lane. False when the lines give no such section
        /// (no payload sides, a pit line that never rejoins - hill climb, drag strip). // ours thresholds
        /// </summary>
        public static bool PitExit(AcAiTrack pit, AcAiTrack fast, out float waitS, out float exitS, out float exitFastS)
        {
            waitS = -1f; exitS = -1f; exitFastS = -1f;
            if (pit == null || fast == null || pit.IsEmpty || fast.IsEmpty || !fast.HasSides) return false;
            int n = pit.Count;
            var beyond = new float[n];
            var fs = new float[n];
            int hint = -1;
            var sp = pit.Spline;
            for (int i = 0; i < n; i++)
            {
                float lat;
                fs[i] = fast.Project(sp.X(i), sp.Y(i), sp.Z(i), ref hint, out lat);
                int k = fast.IndexAt(fs[i]);
                beyond[i] = Math.Abs(lat) - (lat > 0f ? fast.SideL[k] : fast.SideR[k]);
            }
            int bestA = -1, bestB = -1; float bestLen = 0f;
            int a = -1;
            for (int i = 0; i <= n; i++)
            {
                bool outside = i < n && beyond[i] > 1f;
                if (outside && a < 0) a = i;
                if (!outside && a >= 0)
                {
                    float len = pit.S(i - 1) - pit.S(a);
                    if (len > bestLen) { bestLen = len; bestA = a; bestB = i - 1; }
                    a = -1;
                }
            }
            if (bestA < 0 || bestB >= n - 1 || bestLen < 30f) return false; // never back on the track (or no pit section)
            int exit = bestB + 1;
            exitS = pit.S(exit); exitFastS = fs[exit];
            int w = bestB;
            while (w > bestA && beyond[w] < 2.5f) w--;
            waitS = pit.S(w);
            return true;
        }

        static bool HasNonZeroSides(AcAiSpline s)
        {
            int n = 0;
            for (int i = 0; i < s.Count; i += 17) if (s.P(i, AcAiSpline.SideLeft) > 0.01f || s.P(i, AcAiSpline.SideRight) > 0.01f) n++;
            return n > s.Count / 34;
        }

        public bool IsEmpty { get { return Count < 3; } }

        /// <summary>Wraps a point index (closed) or clamps it (open).</summary>
        public int Idx(int i)
        {
            if (IsClosed) { i %= Count; if (i < 0) i += Count; return i; }
            return i < 0 ? 0 : i >= Count ? Count - 1 : i;
        }

        /// <summary>Arc length at point i.</summary>
        public float S(int i) { return Spline.Length[Idx(i)]; }

        /// <summary>Wraps (closed) or clamps (open) an arc length.</summary>
        public float WrapS(float s)
        {
            if (IsClosed) { s %= Length; if (s < 0f) s += Length; return s; }
            return s < 0f ? 0f : s > Length ? Length : s;
        }

        /// <summary>Signed forward distance from s0 to s1 along the line (closed: shortest way, in (-L/2, L/2]).</summary>
        public float Delta(float s0, float s1)
        {
            float d = s1 - s0;
            if (IsClosed) { if (d > Length * 0.5f) d -= Length; else if (d <= -Length * 0.5f) d += Length; }
            return d;
        }

        float Dist(int a, int b)
        {
            float d = Spline.Length[b] - Spline.Length[a];
            if (d < 0f) d += Length;
            return d;
        }

        /// <summary>The point index reached by walking <paramref name="meters"/> along the line from point i (stops at the ends of open lines).</summary>
        public int Walk(int i, float meters)
        {
            int step = meters >= 0f ? 1 : -1, j = i, guard = 0;
            float left = Math.Abs(meters);
            while (left > 0f && guard++ < Count)
            {
                int k = j + step;
                if (!IsClosed && (k < 0 || k >= Count)) break;
                k = Idx(k);
                float seg = step > 0 ? Dist(j, k) : Dist(k, j);
                left -= seg;
                j = k;
            }
            return j;
        }

        /// <summary>The point index at or before arc length s (binary search; no allocation).</summary>
        public int IndexAt(float s)
        {
            s = WrapS(s);
            float[] L = Spline.Length;
            int lo = 0, hi = Count - 1;
            if (s >= L[hi]) return hi;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (L[m] <= s) lo = m; else hi = m; }
            return lo;
        }

        /// <summary>Line sample at arc length s: position, unit tangent, curvature, sides (linear between points).</summary>
        public void Sample(float s, out float x, out float z, out float tx, out float tz, out float curv, out float sideL, out float sideR)
        {
            s = WrapS(s);
            int i = IndexAt(s), j = Idx(i + 1);
            float seg = j == i ? 0f : Dist(i, j);
            float t = seg > 1e-4f ? (s - Spline.Length[i]) / seg : 0f;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            var sp = Spline;
            x = sp.X(i) + (sp.X(j) - sp.X(i)) * t;
            z = sp.Z(i) + (sp.Z(j) - sp.Z(i)) * t;
            tx = TanX[i] + (TanX[j] - TanX[i]) * t;
            tz = TanZ[i] + (TanZ[j] - TanZ[i]) * t;
            float l = (float)Math.Sqrt(tx * tx + tz * tz); if (l > 1e-5f) { tx /= l; tz /= l; }
            curv = Curv[i] + (Curv[j] - Curv[i]) * t;
            sideL = SideL[i] + (SideL[j] - SideL[i]) * t;
            sideR = SideR[i] + (SideR[j] - SideR[i]) * t;
        }

        /// <summary>Height of the line at arc length s (AC y).</summary>
        public float HeightAt(float s)
        {
            s = WrapS(s);
            int i = IndexAt(s), j = Idx(i + 1);
            float seg = j == i ? 0f : Dist(i, j);
            float t = seg > 1e-4f ? Math.Max(0f, Math.Min(1f, (s - Spline.Length[i]) / seg)) : 0f;
            return Spline.Y(i) + (Spline.Y(j) - Spline.Y(i)) * t;
        }

        /// <summary>
        /// Projects an AC-space point on the line near point <paramref name="hint"/> (-1 = exact search): arc length s and signed
        /// lateral offset (m, + = left of travel). Updates the hint.
        /// </summary>
        public float Project(float x, float y, float z, ref int hint, out float lateral)
        {
            lateral = 0f;
            if (Count < 3) return 0f;
            int i = hint >= 0 && hint < Count ? Spline.NearestNear(x, y, z, hint, 30) : Spline.Nearest(x, y, z); // ours: window
            if (i < 0) return 0f;
            if (hint >= 0)
            {
                float dx0 = Spline.X(i) - x, dz0 = Spline.Z(i) - z;
                if (dx0 * dx0 + dz0 * dz0 > 30f * 30f) i = Spline.Nearest(x, y, z); // ours: teleport / reset re-find
            }
            hint = i;
            // choose segment (i -> i+1) or (i-1 -> i)
            int j = Idx(i + 1), h = Idx(i - 1);
            float t = SegT(i, j, x, z);
            int a = i, b = j;
            if (t < 0f && (IsClosed || i > 0)) { float th = SegT(h, i, x, z); a = h; b = i; t = th; }
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            var sp = Spline;
            float px = sp.X(a) + (sp.X(b) - sp.X(a)) * t, pz = sp.Z(a) + (sp.Z(b) - sp.Z(a)) * t;
            float seg = a == b ? 0f : Dist(a, b);
            float s = WrapS(sp.Length[a] + t * seg);
            float tx = TanX[a] + (TanX[b] - TanX[a]) * t, tz = TanZ[a] + (TanZ[b] - TanZ[a]) * t;
            float l = (float)Math.Sqrt(tx * tx + tz * tz); if (l > 1e-5f) { tx /= l; tz /= l; }
            // left of travel = (tz, -tx)
            lateral = (x - px) * tz + (z - pz) * -tx;
            return s;
        }

        float SegT(int i, int j, float x, float z)
        {
            var sp = Spline;
            float ax = sp.X(i), az = sp.Z(i), vx = sp.X(j) - ax, vz = sp.Z(j) - az, l2 = vx * vx + vz * vz;
            return l2 > 1e-9f ? ((x - ax) * vx + (z - az) * vz) / l2 : 0f;
        }
    }
}
