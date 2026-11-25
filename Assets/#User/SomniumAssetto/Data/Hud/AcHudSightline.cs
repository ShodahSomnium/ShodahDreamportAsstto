using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] Where the driver's view of the road ends at the bottom: the "dash line" of a car, measured from the car's own kn5 (the
    /// same file C draws) as seen from the seat's eye. Every opaque, visible triangle of the model within <see cref="MaxDist"/> of the eye
    /// (dash, cowl, binnacle, steering wheel, A-pillar foot) is projected into an angular grid around the eye (yaw x pitch); per yaw
    /// column the line is the top of the covered band that reaches down to the bottom of the grid (so a mirror or a sun visor above a gap
    /// of road does not count). The VR panel's top edge is fitted below this line (<see cref="FitCentre"/>) so the HUD never covers the
    /// road seen through the windscreen. Unity-free: the harness (<c>vrhud</c>) runs it on every owned car; Game/Hud/AcHudVr runs it once
    /// per car on a worker. Angles in degrees: yaw positive to the driver's right, pitch positive DOWN.
    /// </summary>
    public sealed class AcHudSightline
    {
        public const float YawHalfDeg = 64f, YawStepDeg = 1f;            // ours: covers the VR panel beside the wheel (its far edge ~60 deg toward the car centre)
        public const float PitchMinDeg = -6f, PitchMaxDeg = 50f, PitchStepDeg = 0.5f;   // ours
        /// <summary>Interior only: the dash and wheel are within ~1.3 m of the eye; the bonnet seen over the dash is further. // ours</summary>
        public const float MaxDist = 1.5f;
        /// <summary>Triangles are cut until no edge spans more than this fraction of its distance (~3 degrees) before the cells are tested. // ours</summary>
        public const float MaxEdgeRatio = 0.05f;

        public readonly int Columns = (int)(2f * YawHalfDeg / YawStepDeg) + 1;
        public readonly int Rows = (int)((PitchMaxDeg - PitchMinDeg) / PitchStepDeg) + 1;
        /// <summary>Per yaw column: the dash line (deg below the horizon); <see cref="PitchMaxDeg"/> when nothing covers that column.</summary>
        public readonly float[] Line;
        public int Triangles, Meshes;
        public long Ms;
        public string Problem;
        private readonly bool[] _cov;

        public AcHudSightline()
        {
            Line = new float[Columns];
            _cov = new bool[Columns * Rows];
        }

        public float YawOf(int c) { return -YawHalfDeg + c * YawStepDeg; }

        /// <summary>The line at <paramref name="yawDeg"/> (linear between columns; outside the grid = the edge column).</summary>
        public float LineAt(float yawDeg)
        {
            float f = (yawDeg + YawHalfDeg) / YawStepDeg;
            if (f <= 0f) return Line[0];
            if (f >= Columns - 1) return Line[Columns - 1];
            int i = (int)f; float t = f - i;
            return Line[i] * (1f - t) + Line[i + 1] * t;
        }

        /// <summary>The highest (smallest) line value over a yaw span.</summary>
        public float Highest(float yawFrom, float yawTo)
        {
            float m = PitchMaxDeg;
            for (int c = 0; c < Columns; c++) { float y = YawOf(c); if (y >= yawFrom && y <= yawTo && Line[c] < m) m = Line[c]; }
            return m;
        }

        /// <summary>
        /// Measures the car <paramref name="kn5Path"/> from <paramref name="eyeAc"/> (kn5 model space, AC axes: +X = car left, +Y up, +Z
        /// forward). Worker-safe (own file streams, no Unity). Returns null on failure (<paramref name="problem"/> says why).
        /// </summary>
        public static AcHudSightline Measure(string kn5Path, AcVec3 eyeAc, out string problem)
        {
            problem = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s = new AcHudSightline();
            try
            {
                Kn5File f = Kn5File.Read(kn5Path, Kn5ReadOptions.HeaderOnly);
                for (int i = 0; i < f.Nodes.Count; i++)
                {
                    Kn5Node n = f.Nodes[i];
                    Kn5Mesh m = n.Mesh;
                    if (m == null || !m.IsVisible || !m.IsRenderable || m.IsTransparent || m.LodIn > 0.5f) continue;
                    if (!ActiveChain(n)) continue;
                    Kn5Material mat = f.Material(m.MaterialId);
                    if (mat != null && (mat.BlendMode == 1 || Glass(mat.Shader) || Glass(mat.Name))) continue;
                    // cheap reject: the mesh's bounding sphere (parent-chain space ~ model space for car parts) far from the eye
                    AcMat4 w = n.World;
                    AcVec3 sc = w.TransformPoint(m.SphereCenter);
                    if ((sc - eyeAc).Length - m.SphereRadius > MaxDist) continue;
                    f.LoadMesh(m, true);           // pooled (AcPool is thread-safe): little garbage from the worker
                    try { s.AddMesh(m, w, eyeAc); }
                    finally { m.Release(); }
                    s.Meshes++;
                }
                f.Release();
            }
            catch (Exception e) { problem = e.GetType().Name + ": " + e.Message; return null; }
            s.Finish();
            s.Ms = sw.ElapsedMilliseconds;
            return s;
        }

        private static bool ActiveChain(Kn5Node n)
        {
            for (Kn5Node p = n; p != null; p = p.Parent) if (!p.Active) return false;
            return true;
        }

        private static bool Glass(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("windscreen", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("cristal", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("vetri", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // the split work list: 9 floats per triangle in one reused array (no allocation per triangle - the worker's garbage would make the
        // main thread's GC stutter while the player sits down)
        private float[] _stack = new float[9 * 256];
        private int _top;
        private readonly float[] _v = new float[9];

        private void Push(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
        {
            if ((_top + 1) * 9 > _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
            int o = _top * 9;
            _stack[o] = ax; _stack[o + 1] = ay; _stack[o + 2] = az; _stack[o + 3] = bx; _stack[o + 4] = by; _stack[o + 5] = bz;
            _stack[o + 6] = cx; _stack[o + 7] = cy; _stack[o + 8] = cz;
            _top++;
        }

        /// <summary>Projects one mesh (positions transformed by <paramref name="world"/>) - public for tests.</summary>
        public void AddMesh(Kn5Mesh m, AcMat4 world, AcVec3 eye)
        {
            for (int t = 0; t + 2 < m.IndexCount; t += 3)
            {
                AcVec3 a = world.TransformPoint(m.Position(m.Indices[t])) - eye;
                AcVec3 b = world.TransformPoint(m.Position(m.Indices[t + 1])) - eye;
                AcVec3 c = world.TransformPoint(m.Position(m.Indices[t + 2])) - eye;
                // eye frame: x = driver's right (= -AC X), y up, z forward
                AddTriangle(-a.X, a.Y, a.Z, -b.X, b.Y, b.Z, -c.X, c.Y, c.Z);
            }
        }

        /// <summary>One triangle in the eye frame (x right, y up, z forward; metres from the eye).</summary>
        public void AddTriangle(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
        {
            // whole triangle behind the eye or far beyond reach: nothing to see
            if (az <= 0.02f && bz <= 0.02f && cz <= 0.02f) return;
            float far = MaxDist * MaxDist;
            float da = ax * ax + ay * ay + az * az, db = bx * bx + by * by + bz * bz, dc = cx * cx + cy * cy + cz * cz;
            float ab = (ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz);
            float bc = (bx - cx) * (bx - cx) + (by - cy) * (by - cy) + (bz - cz) * (bz - cz);
            float ca = (cx - ax) * (cx - ax) + (cy - ay) * (cy - ay) + (cz - az) * (cz - az);
            float reach = MaxDist + (float)Math.Sqrt(Math.Max(ab, Math.Max(bc, ca)));
            if (da > reach * reach && db > reach * reach && dc > reach * reach) return;
            Triangles++;
            _top = 0;
            Push(ax, ay, az, bx, by, bz, cx, cy, cz);
            int guard = 0;
            float[] v = _v;
            while (_top > 0 && guard++ < 400000)
            {
                _top--;
                Array.Copy(_stack, _top * 9, v, 0, 9);
                float e0 = D2(v, 0, 3), e1 = D2(v, 3, 6), e2 = D2(v, 6, 0);
                float emax = Math.Max(e0, Math.Max(e1, e2));
                float dmin = Math.Min(v[0] * v[0] + v[1] * v[1] + v[2] * v[2], Math.Min(v[3] * v[3] + v[4] * v[4] + v[5] * v[5], v[6] * v[6] + v[7] * v[7] + v[8] * v[8]));
                if (emax > MaxEdgeRatio * MaxEdgeRatio * Math.Max(dmin, 0.0025f))
                {
                    float edge = (float)Math.Sqrt(emax);
                    if (dmin > (MaxDist + 2f * edge) * (MaxDist + 2f * edge)) continue;   // all beyond reach
                    // split the longest edge
                    int i0 = e0 >= e1 && e0 >= e2 ? 0 : (e1 >= e2 ? 3 : 6);
                    int i1 = i0 == 0 ? 3 : (i0 == 3 ? 6 : 0), i2 = i0 == 0 ? 6 : (i0 == 3 ? 0 : 3);
                    float mx = (v[i0] + v[i1]) * 0.5f, my = (v[i0 + 1] + v[i1 + 1]) * 0.5f, mz = (v[i0 + 2] + v[i1 + 2]) * 0.5f;
                    float x0 = v[i0], y0 = v[i0 + 1], z0 = v[i0 + 2], x1 = v[i1], y1 = v[i1 + 1], z1 = v[i1 + 2], x2 = v[i2], y2 = v[i2 + 1], z2 = v[i2 + 2];
                    Push(x0, y0, z0, mx, my, mz, x2, y2, z2);
                    Push(mx, my, mz, x1, y1, z1, x2, y2, z2);
                    continue;
                }
                if (dmin > far) continue;
                Raster(v);
            }
            _top = 0;
        }

        private static float D2(float[] v, int i, int j)
        {
            float x = v[i] - v[j], y = v[i + 1] - v[j + 1], z = v[i + 2] - v[j + 2];
            return x * x + y * y + z * z;
        }

        private const float Rad = (float)(180.0 / Math.PI);

        /// <summary>A small triangle (a few degrees): every grid cell whose centre lies inside its angular projection, plus the cell of its centre
        /// (so thin slivers - a rim seen edge-on - still count).</summary>
        private void Raster(float[] v)
        {
            if (v[2] <= 0.01f || v[5] <= 0.01f || v[8] <= 0.01f) return;     // touches the eye plane: outside the yaw grid anyway
            float y0, p0, y1, p1, y2, p2;
            Angles(v[0], v[1], v[2], out y0, out p0);
            Angles(v[3], v[4], v[5], out y1, out p1);
            Angles(v[6], v[7], v[8], out y2, out p2);
            Mark((y0 + y1 + y2) / 3f, (p0 + p1 + p2) / 3f);
            float yMin = Math.Min(y0, Math.Min(y1, y2)), yMax = Math.Max(y0, Math.Max(y1, y2));
            float pMin = Math.Min(p0, Math.Min(p1, p2)), pMax = Math.Max(p0, Math.Max(p1, p2));
            int c0 = Math.Max(0, (int)Math.Ceiling((yMin + YawHalfDeg) / YawStepDeg));
            int c1 = Math.Min(Columns - 1, (int)Math.Floor((yMax + YawHalfDeg) / YawStepDeg));
            int r0 = Math.Max(0, (int)Math.Ceiling((pMin - PitchMinDeg) / PitchStepDeg));
            int r1 = Math.Min(Rows - 1, (int)Math.Floor((pMax - PitchMinDeg) / PitchStepDeg));
            float area = (y1 - y0) * (p2 - p0) - (y2 - y0) * (p1 - p0);
            if (Math.Abs(area) < 1e-9f) return;
            for (int c = c0; c <= c1; c++)
            {
                float yy = YawOf(c);
                for (int r = r0; r <= r1; r++)
                {
                    float pp = PitchMinDeg + r * PitchStepDeg;
                    float w0 = (y1 - yy) * (p2 - pp) - (y2 - yy) * (p1 - pp);
                    float w1 = (y2 - yy) * (p0 - pp) - (y0 - yy) * (p2 - pp);
                    float w2 = (y0 - yy) * (p1 - pp) - (y1 - yy) * (p0 - pp);
                    bool inside = area > 0 ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0);
                    if (inside) _cov[r * Columns + c] = true;
                }
            }
        }

        private void Mark(float yaw, float pitch)
        {
            int c = (int)Math.Round((yaw + YawHalfDeg) / YawStepDeg), r = (int)Math.Round((pitch - PitchMinDeg) / PitchStepDeg);
            if (c >= 0 && c < Columns && r >= 0 && r < Rows) _cov[r * Columns + c] = true;
        }

        private static void Angles(float x, float y, float z, out float yaw, out float pitch)
        {
            yaw = (float)Math.Atan2(x, z) * Rad;
            pitch = (float)Math.Atan2(-y, Math.Sqrt(x * x + z * z)) * Rad;
        }

        /// <summary>
        /// Per column: scan DOWN from the horizon through the open view (windscreen / bonnet beyond
        /// <see cref="MaxDist"/>) - to the first covered row: the top of the dash. (The earlier bottom-up scan stopped at any open cell under
        /// the dash: beside the wheel the passenger footwell floor is beyond <see cref="MaxDist"/>, which put the line at ~33 deg there - lab
        /// hud-vr 2026-11-01.) A column already covered at the horizon (pillar, binnacle) shows no road: its line is the horizon (the least restrictive value).
        /// </summary>
        public void Finish()
        {
            int r0 = (int)Math.Round((0f - PitchMinDeg) / PitchStepDeg);
            if (r0 < 0) r0 = 0;
            if (r0 > Rows - 1) r0 = Rows - 1;
            for (int c = 0; c < Columns; c++)
            {
                int r = r0;
                if (_cov[r * Columns + c]) { Line[c] = 0f; continue; }   // covered at the horizon (pillar, binnacle): no road in this column
                while (r < Rows && !_cov[r * Columns + c]) r++;      // the open view
                if (r >= Rows) { Line[c] = PitchMaxDeg; continue; }
                Line[c] = Math.Max(0f, PitchMinDeg + r * PitchStepDeg - PitchStepDeg * 0.5f);
            }
        }

        // ------------------------------------------------------------------ panel fit

        /// <summary>
        /// The VR panel (flat, facing the eye; width <paramref name="widthM"/>, height <paramref name="heightM"/>, its centre
        /// <paramref name="aheadM"/> ahead of the eye) is lowered until its whole top edge is at least <paramref name="marginDeg"/> below
        /// <paramref name="line"/> (and at least <paramref name="minTopDeg"/> below the horizon). Returns the centre's drop below the eye
        /// (metres, positive down); <paramref name="topDeg"/> = the top edge's highest point (deg below the horizon) after the fit.
        /// <paramref name="line"/> null = a flat line at <paramref name="flatLineDeg"/>.
        /// </summary>
        public static float FitCentre(AcHudSightline line, float flatLineDeg, float aheadM, float widthM, float heightM, float marginDeg, float minTopDeg,
            out float topDeg)
        {
            float lo = 0f, hi = 60f;      // centre pitch, degrees
            for (int it = 0; it < 30; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Clears(line, flatLineDeg, mid, aheadM, widthM, heightM, marginDeg, minTopDeg)) hi = mid; else lo = mid;
            }
            float th = hi / Rad;
            topDeg = TopEdge(hi, aheadM, widthM, heightM, line, flatLineDeg, out _);
            return aheadM * (float)Math.Tan(th);
        }

        /// <summary>The panel's top edge for a centre pitch: the smallest pitch along it (deg) and the worst clearance below the line.</summary>
        public static float TopEdge(float centrePitchDeg, float aheadM, float widthM, float heightM, AcHudSightline line, float flatLineDeg, out float worstClear)
        {
            float th = centrePitchDeg / Rad;
            float L = aheadM / (float)Math.Cos(th);
            float hh = heightM * 0.5f, hw = widthM * 0.5f;
            float top = 999f; worstClear = 999f;
            for (int i = 0; i <= 16; i++)
            {
                float sx = -hw + widthM * i / 16f;
                // centre direction d = (0, -sin, cos); panel up u = (0, cos, sin)
                float x = sx, y = -L * (float)Math.Sin(th) + hh * (float)Math.Cos(th), z = L * (float)Math.Cos(th) + hh * (float)Math.Sin(th);
                float yaw, pitch;
                Angles(x, y, z, out yaw, out pitch);
                if (pitch < top) top = pitch;
                float ln = line != null ? line.LineAt(yaw) : flatLineDeg;
                float clear = pitch - ln;
                if (clear < worstClear) worstClear = clear;
            }
            return top;
        }

        // ------------------------------------------------------------------ panel fit at a yaw (the panel beside the steering wheel)

        /// <summary>
        /// A flat panel facing the eye, its centre <paramref name="distM"/> from the eye at <paramref name="yawDeg"/> (positive = driver's right)
        /// and a centre pitch (positive down): lowered until its whole top edge is at least <paramref name="marginDeg"/> under the line and
        /// at least <paramref name="minTopDeg"/> under the horizon. Returns the centre pitch (deg); <paramref name="topDeg"/> = the top edge's
        /// highest point after the fit.
        /// </summary>
        public static float FitAt(AcHudSightline line, float flatLineDeg, float distM, float yawDeg, float widthM, float heightM, float marginDeg, float minTopDeg,
            out float topDeg)
        {
            float lo = -20f, hi = 70f;      // centre pitch, degrees
            for (int it = 0; it < 30; it++)
            {
                float mid = (lo + hi) * 0.5f;
                float worst;
                float top = TopEdgeAt(mid, yawDeg, distM, widthM, heightM, line, flatLineDeg, out worst);
                if (worst >= marginDeg && top >= minTopDeg) hi = mid; else lo = mid;
            }
            topDeg = TopEdgeAt(hi, yawDeg, distM, widthM, heightM, line, flatLineDeg, out _);
            return hi;
        }

        /// <summary>A point of the panel (sx right of the centre, sy above it, metres) in the eye frame (x right, y up, z forward).</summary>
        public static void PanelPoint(float centrePitchDeg, float yawDeg, float distM, float sx, float sy, out float x, out float y, out float z)
        {
            float th = centrePitchDeg / Rad, ps = yawDeg / Rad;
            float ct = (float)Math.Cos(th), st = (float)Math.Sin(th), cp = (float)Math.Cos(ps), sp = (float)Math.Sin(ps);
            float dx = sp * ct, dy = -st, dz = cp * ct;               // centre direction (pitch positive down)
            float rx = cp, rz = -sp;                                  // right = up x forward (horizontal)
            float ux = dy * rz, uy = dz * rx - dx * rz, uz = -dy * rx;  // up = forward x right
            x = distM * dx + ux * sy + rx * sx;
            y = distM * dy + uy * sy;
            z = distM * dz + uz * sy + rz * sx;
        }

        /// <summary>The top edge of a panel at a yaw: its smallest pitch (deg) and the worst clearance below the line.</summary>
        public static float TopEdgeAt(float centrePitchDeg, float yawDeg, float distM, float widthM, float heightM, AcHudSightline line, float flatLineDeg, out float worstClear)
        {
            float top = 999f; worstClear = 999f;
            for (int i = 0; i <= 16; i++)
            {
                float x, y, z;
                PanelPoint(centrePitchDeg, yawDeg, distM, -widthM * 0.5f + widthM * i / 16f, heightM * 0.5f, out x, out y, out z);
                float yaw, pitch;
                Angles(x, y, z, out yaw, out pitch);
                if (pitch < top) top = pitch;
                float ln = line != null ? line.LineAt(yaw) : flatLineDeg;
                if (pitch - ln < worstClear) worstClear = pitch - ln;
            }
            return top;
        }

        /// <summary>The yaw of the panel's edge nearest the straight-ahead view (deg; for a panel right of ahead its smallest yaw over the
        /// left edge, for one left of ahead minus its largest yaw over the right edge - so positive = clear of ahead by that much).</summary>
        public static float InnerEdgeYaw(float centrePitchDeg, float yawDeg, float distM, float widthM, float heightM)
        {
            float side = yawDeg >= 0f ? 1f : -1f, best = 999f;
            for (int i = 0; i <= 8; i++)
            {
                float x, y, z, yaw, pitch;
                PanelPoint(centrePitchDeg, yawDeg, distM, -side * widthM * 0.5f, -heightM * 0.5f + heightM * i / 8f, out x, out y, out z);
                Angles(x, y, z, out yaw, out pitch);
                if (side * yaw < best) best = side * yaw;
            }
            return best;
        }

        private static bool Clears(AcHudSightline line, float flat, float centrePitchDeg, float aheadM, float widthM, float heightM, float marginDeg, float minTopDeg)
        {
            float worst;
            float top = TopEdge(centrePitchDeg, aheadM, widthM, heightM, line, flat, out worst);
            return worst >= marginDeg && top >= minTopDeg;
        }
    }
}
