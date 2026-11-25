using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumAssetto;

static partial class Program
{
    // wlamps <track> [layout]: where the KS_START_LIGHT_n lamps really are. On magione / monza the lamp meshes hang under an FBX node at the
    // origin, so the node position is NOT the lamp: the vertices are (AC world = vertex x node world x models.ini ModelMatrix). One lamp mesh
    // may hold several panels (monza: the gantry ahead of the grid + a repeater half way down), so vertices are clustered (3 m) into panels;
    // each panel: distance / direction from the pole slot and whether its mean normal faces the pole (T5: models.ini ROTATION check).
    static int Cmd_wlamps(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = WLayout(c, a[1], a.Count > 2 ? a[2] : null);
        if (l == null) { Console.WriteLine(a[1] + ": no owned layout"); return 1; }
        var job = new AcTrackJob(l, new AcTrackJobOptions { SystemSurfacesIni = _install.SystemSurfacesIni });
        job.Scan();
        var mk = job.Markers;
        Console.WriteLine(l.Key + ": " + mk.StartLights.Count + " start lights, " + mk.Starts.Count + " grid slots");
        if (mk.Starts.Count == 0) return 0;
        var s0 = mk.Starts[0].Position;
        double yaw = mk.Starts[0].HeadingDeg * Math.PI / 180;      // Unity yaw
        double fux = Math.Sin(yaw), fuz = Math.Cos(yaw);            // Unity forward
        foreach (var lm in mk.StartLights)
        {
            int f = -1;
            for (int i = 0; i < job.Files.Count; i++) if (string.Equals(job.Files[i].Path, lm.Kn5File, StringComparison.OrdinalIgnoreCase)) f = i;
            if (f < 0) { Console.WriteLine("  " + lm.Name + ": file not found"); continue; }
            var file = job.Files[f];
            var world = Kn5Scan.WorldMatrices(file);
            var mm = AcTrackMarkers.ModelMatrix(job.Placements[f]);
            var stack = new Stack<Kn5Node>(); stack.Push(lm.Node);
            var cs = new List<double[]>();   // sx, sy, sz, n, nx, nz
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                foreach (var ch in n.Children) stack.Push(ch);
                if (n.Mesh == null) continue;
                file.LoadMesh(n.Mesh);
                var M = AcMat4.Mul(world[n.Index], mm);
                var v = n.Mesh.Vertices; int st = n.Mesh.Stride;
                for (int i = 0; i + st <= v.Length; i += st)
                {
                    double x = v[i], y = v[i + 1], z = v[i + 2];
                    double wx = x * M.M0 + y * M.M4 + z * M.M8 + M.M12;
                    double wy = x * M.M1 + y * M.M5 + z * M.M9 + M.M13;
                    double wz = x * M.M2 + y * M.M6 + z * M.M10 + M.M14;
                    double a0 = v[i + 3], a1 = v[i + 4], a2 = v[i + 5];
                    double nx = a0 * M.M0 + a1 * M.M4 + a2 * M.M8, nz = a0 * M.M2 + a1 * M.M6 + a2 * M.M10;
                    double[] hit = null;
                    foreach (var q in cs)
                    {
                        double dx = q[0] / q[3] - wx, dy = q[1] / q[3] - wy, dz = q[2] / q[3] - wz;
                        if (dx * dx + dy * dy + dz * dz < 9) { hit = q; break; }
                    }
                    if (hit == null) cs.Add(hit = new double[6]);
                    hit[0] += wx; hit[1] += wy; hit[2] += wz; hit[3]++; hit[4] += nx; hit[5] += nz;
                }
            }
            if (cs.Count == 0) { Console.WriteLine("  " + lm.Name + ": no vertices"); continue; }
            Console.WriteLine("  " + lm.Name + " node pos (" + WF(lm.Position.X) + "," + WF(lm.Position.Y) + "," + WF(lm.Position.Z) + "), " + cs.Count + " panel(s), file rot " + WF(job.Placements[f].Rotation[0]) + ":");
            foreach (var q in cs)
            {
                double cx = q[0] / q[3], cy = q[1] / q[3], cz = q[2] / q[3];
                double vx = -(cx - s0.X), vy = cy - s0.Y, vz = cz - s0.Z, vh = Math.Sqrt(vx * vx + vz * vz);   // Unity offset from the pole
                double dotFwd = (vx * fux + vz * fuz) / Math.Max(1e-6, vh);
                double unx = -q[4], unz = q[5], nl = Math.Sqrt(unx * unx + unz * unz);
                double facing = nl > 1e-6 ? (-vx * unx + -vz * unz) / (vh * nl) : double.NaN;   // panel normal . (panel -> pole)
                Console.WriteLine("     panel AC (" + cx.ToString("0.0") + "," + cy.ToString("0.0") + "," + cz.ToString("0.0") + ") " + q[3] + " verts: " + vh.ToString("0.0") + " m from the pole, ahead-dot " +
                    dotFwd.ToString("0.00") + ", " + vy.ToString("0.0") + " m above it, normal faces the pole " + facing.ToString("0.00"));
            }
        }
        return 0;
    }
}
static partial class Program
{
    // wlamptex <kn5> [name prefix]: the txDiffuse colour under each KS_START_LIGHT mesh's UVs (what "ksEmissive x txDiffuse" multiplies)
    static int Cmd_wlamptex(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        foreach (var n in k.Nodes)
        {
            if (n.Mesh == null || !n.Name.StartsWith(a.Count > 2 ? a[2] : "KS_START_LIGHT", StringComparison.Ordinal)) continue;
            var m = k.Material(n.Mesh.MaterialId);
            string tn = m != null ? m.Texture("txDiffuse") : null;
            var t = tn != null ? k.Texture(tn) : null;
            if (t == null) { Console.WriteLine(n.Name + ": no txDiffuse"); continue; }
            var bytes = k.ReadTextureBytes(t);
            int w, h; byte[] px;
            if (tn.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) { var info = AcDds.Parse(bytes, 0, bytes.Length); px = AcDds.DecodeRgba32(bytes, info, 0, out w, out h); }
            else { Console.WriteLine(n.Name + ": " + tn + " is not DDS (PNG) - skipped"); continue; }
            k.LoadMesh(n.Mesh);
            var v = n.Mesh.Vertices; int st = n.Mesh.Stride;
            double r = 0, g = 0, b = 0, al = 0; int c = 0;
            double u0 = 1e9, u1 = -1e9, v0 = 1e9, v1 = -1e9;
            for (int i = 0; i + st <= v.Length; i += st)
            {
                double u = v[i + 6], vv = v[i + 7];
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, vv); v1 = Math.Max(v1, vv);
            }
            // sample a 8x8 grid inside the UV rectangle (raw DDS orientation: row = v * h)
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                double u = u0 + (u1 - u0) * (x + 0.5) / 8, vv = v0 + (v1 - v0) * (y + 0.5) / 8;
                int px_ = (int)(((u % 1) + 1) % 1 * w), py = (int)(((vv % 1) + 1) % 1 * h);
                int o = (py * w + px_) * 4;
                r += px[o]; g += px[o + 1]; b += px[o + 2]; al += px[o + 3]; c++;
            }
            Console.WriteLine(n.Name + " " + tn + " " + w + "x" + h + " uv [" + u0.ToString("0.000") + ".." + u1.ToString("0.000") + "] x [" + v0.ToString("0.000") + ".." + v1.ToString("0.000") + "]: mean rgba " +
                (r / c / 255).ToString("0.00") + " " + (g / c / 255).ToString("0.00") + " " + (b / c / 255).ToString("0.00") + " " + (al / c / 255).ToString("0.00"));
        }
        return 0;
    }
}
static partial class Program
{
    // wsem <track> [layout]: data/semaphore.ini objects -> AC_SEMAPHORE nodes; their lamp meshes must now be drawn (not hidden as marker cubes)
    static int Cmd_wsem(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = WLayout(c, a[1], a.Count > 2 ? a[2] : null);
        var job = new AcTrackJob(l, new AcTrackJobOptions { SystemSurfacesIni = _install.SystemSurfacesIni });
        job.Scan();
        var ini = job.SemaphoreIni;
        Console.WriteLine(l.Key + ": semaphore.ini " + (ini != null ? "yes" : "no") + ", KS_START_LIGHT " + job.Markers.StartLights.Count + ", marker cubes hidden " + job.MarkerCubes + ", drawn meshes " + job.MeshesRendered);
        if (ini == null) return 0;
        Console.WriteLine("  [GROUPS] DISQUALIFIED " + ini.GetInt("GROUPS", "DISQUALIFIED", -1) + " READY " + ini.GetInt("GROUPS", "READY", -1) + " START " + ini.GetInt("GROUPS", "START", -1) + ", [COLOR] INTENSITY " + WF(ini.GetFloat("COLOR", "INTENSITY", -1)));
        foreach (var o in ini.WithPrefix("OBJECT_"))
        {
            string name = (o.Get("NAME", "") ?? "").Trim();
            Kn5Node node = null; Kn5File file = null;
            foreach (var f in job.Files) { var n = f.Find(name); if (n != null) { node = n; file = f; break; } }
            int meshes = 0, kept = 0;
            if (node != null)
                foreach (var ch in node.Children)
                    if (ch.Mesh != null) { meshes++; if (!(AcTrackMarkers.IsMarkerCube(ch) && AcTrackMarkers.Classify(ch.Name) != AcMarkerKind.Semaphore) && ch.Mesh.IsRenderable && ch.Mesh.IsVisible) kept++; }
            var mat = node != null && meshes > 0 ? file.Material(node.Children[0].Mesh.MaterialId) : null;
            Console.WriteLine("  " + name + " TYPE " + o.GetInt("TYPE", -1) + " ORDER " + o.GetInt("ORDER", -1) + " INTENSITY " + WF(o.GetFloat("INTENSITY", float.NaN)) + ": node " + (node != null ? "yes" : "NO") + ", meshes " + meshes + " drawn " + kept +
                (mat != null ? ", mat " + mat.Name + " [" + mat.Shader + "] " + mat.Texture("txDiffuse") + " ksEmissive " + WF(mat.Float("ksEmissive")) : ""));
        }
        return 0;
    }
}
static partial class Program
{
    // wshadow <track> [layout]: how many drawn meshes / triangles have the kn5 castShadows flag (what AcTrackWorld honours)
    static int Cmd_wshadow(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = WLayout(c, a[1], a.Count > 2 ? a[2] : null);
        var job = new AcTrackJob(l, new AcTrackJobOptions { SystemSurfacesIni = _install.SystemSurfacesIni });
        job.Scan();
        long n = 0, ns = 0, t = 0, ts = 0;
        foreach (var f in job.Files)
            foreach (var node in f.Nodes)
            {
                var m = node.Mesh;
                if (m == null || !m.IsRenderable || !m.IsVisible || AcTrackMarkers.IsMarkerCube(node)) continue;
                n++; t += m.IndexCount / 3;
                if (m.CastShadows) { ns++; ts += m.IndexCount / 3; }
            }
        Console.WriteLine(l.Key + ": drawn meshes " + n + ", castShadows " + ns + " (" + (100.0 * ns / Math.Max(1, n)).ToString("0") + " %), triangles " + t + ", shadow triangles " + ts + " (" + (100.0 * ts / Math.Max(1, t)).ToString("0") + " %)");
        return 0;
    }
}
