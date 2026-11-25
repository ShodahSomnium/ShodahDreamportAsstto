using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using SomniumSpace.Worlds.SomniumAssetto;

// W-only probes (the W harness copy also compiles Game/World/AcTrackJob*.cs).
static partial class Program
{
    // wuv <kn5> <shaderFilter>: per mesh using a matching material: name, verts, uv range, world size (AABB), metres per uv unit
    static int Cmd_wuv(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.NoTextures);
        string filt = a[2];
        int shown = 0;
        foreach (var n in k.Nodes)
        {
            if (n.Mesh == null) continue;
            var m = k.Material(n.Mesh.MaterialId);
            if (m == null || (m.Shader.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0 && m.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            var me = n.Mesh;
            float u0 = 1e9f, u1 = -1e9f, v0 = 1e9f, v1 = -1e9f, x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
            double mpuSum = 0; int mpuN = 0;
            for (int i = 0; i < me.VertexCount; i++)
            {
                float u = me.U(i), v = me.V(i); var p = me.Position(i);
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
                x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
            }
            for (int t = 0; t + 2 < me.IndexCount && t < 3000; t += 3)
            {
                int ia = me.Indices[t], ib = me.Indices[t + 1];
                var d = me.Position(ib) - me.Position(ia);
                float du = me.U(ib) - me.U(ia), dv = me.V(ib) - me.V(ia);
                float duv = (float)Math.Sqrt(du * du + dv * dv);
                if (duv > 1e-5f) { mpuSum += d.Length / duv; mpuN++; }
            }
            Console.WriteLine(n.Name + " [" + m.Name + "/" + m.Shader + "] v" + me.VertexCount + " uv u " + WF(u0) + ".." + WF(u1) + " v " + WF(v0) + ".." + WF(v1)
                + " size " + WF(x1 - x0) + "x" + WF(y1 - y0) + "x" + WF(z1 - z0) + " m/uv " + (mpuN > 0 ? WF((float)(mpuSum / mpuN)) : "?") + " render " + me.IsRenderable + " lod " + WF(me.LodIn) + "-" + WF(me.LodOut) + " layer " + me.Layer);
            if (++shown >= (a.Count > 3 ? int.Parse(a[3]) : 25)) break;
        }
        return 0;
    }
}
static partial class Program
{
    // wtexavg <kn5> <tex1> [tex2...]: format, size, mips, average RGBA of the smallest decodable mip >= 64 px
    static int Cmd_wtexavg(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        for (int i = 2; i < a.Count; i++)
        {
            var t = k.Texture(a[i]);
            if (t == null) { Console.WriteLine(a[i] + ": missing"); continue; }
            var b = k.ReadTextureBytes(t);
            var info = AcDds.Parse(b, 0, b.Length);
            int lvl = 0;
            while (lvl + 1 < info.MipCount && Math.Max(info.Width >> (lvl + 1), info.Height >> (lvl + 1)) >= 64) lvl++;
            int w, h; var px = AcDds.DecodeRgba32(b, info, lvl, out w, out h);
            if (px == null) { Console.WriteLine(a[i] + ": " + info + " not decodable"); continue; }
            double r = 0, g = 0, bl = 0, al = 0; int n = w * h;
            for (int p = 0; p < n; p++) { r += px[p * 4]; g += px[p * 4 + 1]; bl += px[p * 4 + 2]; al += px[p * 4 + 3]; }
            Console.WriteLine(a[i] + ": " + info + " avg " + WF((float)(r / n / 255)) + " " + WF((float)(g / n / 255)) + " " + WF((float)(bl / n / 255)) + " a " + WF((float)(al / n / 255)));
        }
        return 0;
    }
}
static partial class Program
{
    // wcensus: over every owned layout's kn5 (unique files): inactive nodes, isVisible=0 meshes, renderable-but-invisible, textures by
    // (format, mips<=1 & size>256, non-multiple-of-4 DXT), shaders, skinned, mirrored dummies (det<0), non-uniform scale
    static int Cmd_wcensus(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in c.Tracks) foreach (var l in t.Layouts) if (l.Owned) foreach (var f in l.Kn5Files) files.Add(f);
        int inactive = 0, invisible = 0, invisRender = 0, skinned = 0, mirrored = 0, scaled = 0, nomip = 0, nomipBig = 0, odd4 = 0, tex = 0;
        var fmts = new Dictionary<string, int>(); var shaders = new Dictionary<string, int>(); var inactNames = new List<string>(); var invisNames = new List<string>();
        long nomipBigBytes = 0;
        foreach (var f in files)
        {
            var k = Kn5File.Read(f, Kn5ReadOptions.HeaderOnly);
            foreach (var n in k.Nodes)
            {
                if (!n.Active) { inactive++; if (inactNames.Count < 15) inactNames.Add(Path.GetFileName(f) + ":" + n.Name); }
                if (n.Mesh != null)
                {
                    if (!n.Mesh.IsVisible) { invisible++; if (n.Mesh.IsRenderable) invisRender++; if (invisNames.Count < 15) invisNames.Add(Path.GetFileName(f) + ":" + n.Name); }
                    if (n.Mesh.Skinned) skinned++;
                }
                else
                {
                    if (n.Local.Determinant3 < 0) mirrored++;
                    var s = n.Local.Scale; if (Math.Abs(s.X - 1) > 0.01 || Math.Abs(s.Y - 1) > 0.01 || Math.Abs(s.Z - 1) > 0.01) scaled++;
                }
            }
            foreach (var m in k.Materials) { int v; shaders.TryGetValue(m.Shader, out v); shaders[m.Shader] = v + 1; }
            foreach (var t in k.Textures)
            {
                tex++;
                var info = k.ReadTextureHeader(t);
                string key = info.Format.ToString(); int v; fmts.TryGetValue(key, out v); fmts[key] = v + 1;
                if (info.IsDds && info.MipCount <= 1) { nomip++; if (Math.Max(info.Width, info.Height) > 256) { nomipBig++; nomipBigBytes += (long)info.Width * info.Height * 4; } }
                if (info.IsBlockCompressed && (info.Width % 4 != 0 || info.Height % 4 != 0)) odd4++;
            }
        }
        Console.WriteLine(files.Count + " files, textures " + tex + ", inactive nodes " + inactive + ", invisible meshes " + invisible + " (renderable " + invisRender + "), skinned " + skinned + ", mirrored dummies " + mirrored + ", scaled dummies " + scaled);
        Console.WriteLine("no-mip textures " + nomip + ", of them > 256 px " + nomipBig + " (" + (nomipBigBytes >> 20) + " MB as RGBA32 level 0), DXT not multiple of 4: " + odd4);
        foreach (var kv in fmts) Console.Write(kv.Key + " " + kv.Value + "  "); Console.WriteLine();
        var sl = new List<KeyValuePair<string, int>>(shaders); sl.Sort((x, y) => y.Value.CompareTo(x.Value));
        foreach (var kv in sl) Console.Write(kv.Key + " " + kv.Value + ", "); Console.WriteLine();
        Console.WriteLine("inactive e.g. " + string.Join(" ", inactNames)); Console.WriteLine("invisible e.g. " + string.Join(" ", invisNames));
        return 0;
    }
}
static partial class Program
{
    static int Cmd_wnomip(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in c.Tracks) foreach (var l in t.Layouts) if (l.Owned && (a.Count < 2 || l.Key == a[1])) foreach (var f in l.Kn5Files) files.Add(f);
        var by = new Dictionary<string, int>(); long bytes = 0;
        foreach (var f in files)
        {
            var k = Kn5File.Read(f, Kn5ReadOptions.HeaderOnly);
            var used = Kn5Scan.UsedTextures(k, n => n.Mesh != null && n.Mesh.IsRenderable);
            foreach (var t in k.Textures)
            {
                if (!used.Contains(t.Name)) continue;
                var info = k.ReadTextureHeader(t);
                if (!info.IsDds || info.MipCount > 1 || Math.Max(info.Width, info.Height) <= 256) continue;
                string key = info.Format + " " + info.Width + "x" + info.Height; int v; by.TryGetValue(key, out v); by[key] = v + 1; bytes += t.Size;
            }
        }
        foreach (var kv in by) Console.WriteLine(kv.Key + ": " + kv.Value);
        Console.WriteLine("file bytes " + (bytes >> 20) + " MB");
        return 0;
    }
}
static partial class Program
{
    static AcLayoutEntry WLayout(AcContent c, string track, string layout)
    {
        var t = c.Track(track);
        if (t == null) return null;
        if (layout != null) return t.Layout(layout);
        foreach (var l in t.Layouts) if (l.Owned) return l;
        return null;
    }

    // wjob <track> [layout] [--nograss] [--nomips]: runs the W track job (worker) like the game does, consuming items on this thread
    static int Cmd_wjob(List<string> a)
    {
        var c = AcContent.Scan(_install);
        string layout = a.Count > 2 && !a[2].StartsWith("--") ? a[2] : null;
        var l = WLayout(c, a[1], layout);
        if (l == null) { Console.WriteLine("no layout"); return 1; }
        var opt = new AcTrackJobOptions { Grass = !a.Contains("--nograss"), MakeMips = !a.Contains("--nomips"), SystemDir = _install.SystemDir, WeatherDir = Path.Combine(_install.Content, "weather"), SystemSurfacesIni = _install.SystemSurfacesIni };
        var job = new AcTrackJob(l, opt);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long gc0 = GC.GetTotalMemory(true);
        var th = new System.Threading.Thread(job.Run) { IsBackground = true };
        th.Start();
        int tex = 0, mesh = 0, texProblems = 0, tan = 0; long maxQueued = 0, vbytes = 0, tbytes = 0, cbytes = 0; long peak = 0;
        var kinds = new Dictionary<string, int>();
        double maxItemMs = 0; string maxItem = "";
        while (true)
        {
            AcJobItem it;
            if (job.TryTake(out it))
            {
                if (it.Tex != null)
                {
                    tex++; tbytes += it.Tex.Bytes;
                    if (it.Tex.Problem != null) { texProblems++; Console.WriteLine("  tex problem " + it.Tex.Name + ": " + it.Tex.Problem); }
                    string k = it.Tex.Source + "->" + it.Tex.Kind + (it.Tex.MipsMade ? "+mips" : ""); int v; kinds.TryGetValue(k, out v); kinds[k] = v + 1;
                }
                else if (it.Mesh != null)
                {
                    mesh++; if (it.Mesh.Vertices != null) vbytes += it.Mesh.Vertices.LongLength * 4 + it.Mesh.Indices.LongLength * 2;
                    if (it.Mesh.ColliderPositions != null) cbytes += it.Mesh.ColliderPositions.LongLength * 4 + it.Mesh.Indices.LongLength * 2;
                    if (it.Mesh.HasTangents) tan++;
                }
                continue;
            }
            long q = GC.GetTotalMemory(false); if (q > peak) peak = q;
            if (job.Done && job.Queued == 0) break;
            System.Threading.Thread.Sleep(1);
        }
        Console.WriteLine(l.Key + ": " + (job.Failed ? "FAILED " + job.Error : "ok") + " in " + sw.ElapsedMilliseconds + " ms (scan " + job.ScanMs + " ms, produce " + job.ProduceMs + " ms)");
        Console.WriteLine("  kn5 " + job.Files.Count + ", meshes " + job.MeshesTotal + " (render " + job.MeshesRendered + ", collide " + job.MeshesCollide + ", walls " + job.Walls + ", marker cubes " + job.MarkerCubes + ", inactive hidden " + job.InactiveHidden + ", grass skipped " + job.GrassSkipped + ")");
        Console.WriteLine("  verts " + job.VerticesRendered + ", tris " + job.TrianglesRendered + " drawn / " + job.TrianglesCollide + " collide; items tex " + tex + " mesh " + mesh + " (tangents " + tan + "), produced " + job.Produced + "/" + job.WorkTotal);
        Console.WriteLine("  textures read " + (job.TextureBytesRead >> 20) + " MB -> prepared " + (tbytes >> 20) + " MB, mips made " + job.MipsMade + ", problems " + texProblems + "; vertex+index " + (vbytes >> 20) + " MB, collider " + (cbytes >> 20) + " MB; managed peak " + (peak >> 20) + " MB (start " + (gc0 >> 20) + ")");
        foreach (var kv in kinds) Console.Write(kv.Key + " " + kv.Value + "  "); Console.WriteLine();
        Console.WriteLine("  markers: grid " + job.Markers.Starts.Count + ", pits " + job.Markers.Pits.Count + ", gates " + job.Markers.Gates.Count + ", hotlap " + job.Markers.HotlapStarts.Count + ", start lights " + job.Markers.StartLights.Count + "; fast lane " + (job.FastLane != null ? job.FastLane.Count + " pts" : "none") + ", pit lane " + (job.PitLane != null ? job.PitLane.Count + " pts" : "none"));
        Console.WriteLine("  weather " + job.WeatherUsed + ", lighting.ini " + (job.LightingIni != null) + ", startinglights.ini " + (job.StartingLightsIni != null) + "; surfaces " + job.Surfaces.Surfaces.Count + ", unmatched physics " + job.Markers.UnmatchedPhysics.Count + " " + string.Join(" ", job.Markers.UnmatchedPhysics));
        foreach (var p in job.Problems) Console.WriteLine("  problem: " + p);
        return job.Failed ? 1 : 0;
    }
}
static partial class Program
{
    // wenc <kn5> <tex> [out.png]: decode level 0, halve, encode with AcTexPrepare.EncodeDxt, decode again; PSNR vs the halved original
    static int Cmd_wenc(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        var t = k.Texture(a[2]);
        var b = k.ReadTextureBytes(t);
        var info = AcDds.Parse(b, 0, b.Length);
        int w, h; var l0 = AcDds.DecodeRgba32(b, info, 0, out w, out h);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int hw, hh; var half = AcTexPrepare.Half(l0, w, h, out hw, out hh);
        bool dxt5 = info.Format != AcTexFormat.DXT1;
        var enc = AcTexPrepare.EncodeDxt(half, hw, hh, dxt5);
        long ms = sw.ElapsedMilliseconds;
        var buf = new byte[128 + enc.Length]; Buffer.BlockCopy(enc, 0, buf, 128, enc.Length);
        var fi = new AcDdsInfo { Format = dxt5 ? AcTexFormat.DXT5 : AcTexFormat.DXT1, Width = hw, Height = hh, MipCount = 1, DataOffset = 128 };
        int dw, dh; var dec = AcDds.DecodeRgba32(buf, fi, 0, out dw, out dh);
        double se = 0, sea = 0; int n = hw * hh;
        for (int i = 0; i < n; i++) { for (int c = 0; c < 3; c++) { double d = dec[i * 4 + c] - half[i * 4 + c]; se += d * d; } double da = dec[i * 4 + 3] - half[i * 4 + 3]; sea += da * da; }
        double mse = se / (n * 3), msea = sea / n;
        Console.WriteLine(a[2] + " " + info + " -> " + hw + "x" + hh + " encoded in " + ms + " ms, PSNR rgb " + (10 * Math.Log10(255.0 * 255 / Math.Max(mse, 1e-9))).ToString("0.0") + " dB, alpha " + (10 * Math.Log10(255.0 * 255 / Math.Max(msea, 1e-9))).ToString("0.0") + " dB");
        if (a.Count > 3) Png.Write(a[3], dw, dh, dec);
        return 0;
    }
}
static partial class Program
{
    // wmodel <kn5 path> [skinDir]: runs AcModelJob (the worker half of AcKn5Builder.BuildAsync) and reports
    static int Cmd_wmodel(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        var job = new AcModelJob { Kn5 = k, SkinDir = a.Count > 2 ? a[2] : null };
        job.Run();
        long tb = 0; int made = 0, tan = 0, sk = 0; long vb = 0;
        foreach (var t in job.Textures) { tb += t.Bytes; if (t.MipsMade) made++; }
        foreach (var m in job.Meshes) { if (m.HasTangents) tan++; if (m.Skinned) sk++; if (m.Vertices != null) vb += m.Vertices.LongLength * 4; }
        Console.WriteLine(Path.GetFileName(a[1]) + ": " + (job.Failed ? "FAILED " + job.Error : "ok") + " in " + job.Ms + " ms; meshes " + job.Meshes.Count + " (tangents " + tan + ", skinned " + sk + "), vertex MB " + (vb >> 20) +
                          "; textures " + job.Textures.Count + " prepared " + (tb >> 20) + " MB, mips made " + made + ", skin overrides " + job.SkinOverrides + "; problems " + job.Problems.Count);
        foreach (var p in job.Problems) Console.WriteLine("  " + p);
        return job.Failed ? 1 : 0;
    }
}
static partial class Program
{
    // walpha <kn5> <tex>: average RGB where alpha < 0.2 and where alpha > 0.8 (detail-blend check)
    static int Cmd_walpha(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        var t = k.Texture(a[2]); var b = k.ReadTextureBytes(t); var info = AcDds.Parse(b, 0, b.Length);
        int lvl = Math.Min(info.MipCount - 1, 2), w, h; var px = AcDds.DecodeRgba32(b, info, lvl, out w, out h);
        double[] lo = new double[4], hi = new double[4];
        for (int i = 0; i < w * h; i++)
        {
            int al = px[i * 4 + 3];
            var acc = al < 51 ? lo : al > 204 ? hi : null;
            if (acc == null) continue;
            acc[0] += px[i * 4]; acc[1] += px[i * 4 + 1]; acc[2] += px[i * 4 + 2]; acc[3]++;
        }
        Console.WriteLine(a[2] + " " + info + ": alpha<0.2 n=" + lo[3] + " rgb " + (lo[3] > 0 ? WF((float)(lo[0] / lo[3] / 255)) + " " + WF((float)(lo[1] / lo[3] / 255)) + " " + WF((float)(lo[2] / lo[3] / 255)) : "-") +
            " | alpha>0.8 n=" + hi[3] + " rgb " + (hi[3] > 0 ? WF((float)(hi[0] / hi[3] / 255)) + " " + WF((float)(hi[1] / hi[3] / 255)) + " " + WF((float)(hi[2] / hi[3] / 255)) : "-"));
        return 0;
    }
}
static partial class Program
{
    // wlights <kn5>: KS_START_LIGHT nodes: class, children meshes, materials and their diffuse texture averages
    static int Cmd_wlights(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        foreach (var n in k.Nodes)
        {
            if (!n.Name.StartsWith("KS_START_LIGHT", StringComparison.Ordinal)) continue;
            Console.WriteLine(n.Name + " " + n.Class + " children " + n.Children.Count + " active " + n.Active);
            var stack = new Stack<Kn5Node>(); stack.Push(n);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                foreach (var c in x.Children) stack.Push(c);
                if (x.Mesh == null) continue;
                var m = k.Material(x.Mesh.MaterialId);
                Console.WriteLine("   mesh " + x.Name + " v" + x.Mesh.VertexCount + " mat " + (m != null ? m.Name + " [" + m.Shader + "] diffuse " + m.Texture("txDiffuse") + " ksEmissive " + WF(m.Float("ksEmissive")) + " ksDiffuse " + WF(m.Float("ksDiffuse")) + " ksAmbient " + WF(m.Float("ksAmbient")) : "-"));
            }
        }
        return 0;
    }
}
static partial class Program
{
    // wcheck <track> [layout]: (1) marker heading (AcMat4.HeadingDeg, Unity yaw) vs the fast-lane direction converted like AcTrackWorld.LineHeading;
    // (2) the Unity placement of models.ini files (file root (-x,y,z) + Euler(0, ROTATION[0], 0)) vs AcTrackMarkers.ModelMatrix for KS_START_LIGHT nodes
    static int Cmd_wcheck(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = WLayout(c, a[1], a.Count > 2 ? a[2] : null);
        var job = new AcTrackJob(l, new AcTrackJobOptions { SystemSurfacesIni = _install.SystemSurfacesIni });
        job.Scan();
        var ai = job.FastLane;
        double maxErr = 0; int n = 0;
        foreach (var m in job.Markers.Starts)
        {
            if (ai == null) break;
            var p = m.Position; int hint = -1;
            float prog = ai.Progress(p.X, p.Y, p.Z, ref hint);
            int j = hint + 1 >= ai.Count ? 0 : hint + 1;
            float dx = -(ai.X(j) - ai.X(hint)), dz = ai.Z(j) - ai.Z(hint);
            double lineYaw = Math.Atan2(dx, dz) * 180 / Math.PI;
            double err = Math.Abs(((m.HeadingDeg - lineYaw) % 360 + 540) % 360 - 180);
            maxErr = Math.Max(maxErr, err); n++;
            if (m.Index == 0) Console.WriteLine("  AC_START_0 heading " + WF(m.HeadingDeg) + " line " + WF((float)lineYaw) + " progress " + WF(prog));
        }
        Console.WriteLine(l.Key + ": grid heading vs fast lane: max diff " + maxErr.ToString("0.0") + " deg over " + n + " slots");
        for (int f = 0; f < job.Files.Count; f++)
        {
            var pl = job.Placements[f];
            if (pl.Position[0] == 0 && pl.Position[1] == 0 && pl.Position[2] == 0 && pl.Rotation[0] == 0) continue;
            var world = Kn5Scan.WorldMatrices(job.Files[f]);
            var mm = AcTrackMarkers.ModelMatrix(pl);
            double r = pl.Rotation[0] * Math.PI / 180, cs = Math.Cos(r), sn = Math.Sin(r);
            double worst = 0; int cnt = 0;
            foreach (var node in job.Files[f].Nodes)
            {
                if (!node.IsDummy) continue;
                var w = world[node.Index];
                var acw = AcMat4.Mul(w, mm).Translation;                 // AC world via the data core
                double ux = -acw.X, uy = acw.Y, uz = acw.Z;              // -> Unity
                double lx = -w.M12, ly = w.M13, lz = w.M14;              // node in the file root (Unity)
                double qx = lx * cs + lz * sn, qz = -lx * sn + lz * cs;  // Unity Euler(0, r, 0)
                double ex = qx - pl.Position[0], ey = ly + pl.Position[1], ez = qz + pl.Position[2];
                double d = Math.Sqrt((ex - ux) * (ex - ux) + (ey - uy) * (ey - uy) + (ez - uz) * (ez - uz));
                worst = Math.Max(worst, d); cnt++;
            }
            Console.WriteLine("  " + pl.File + " pos (" + WF(pl.Position[0]) + "," + WF(pl.Position[1]) + "," + WF(pl.Position[2]) + ") rot " + WF(pl.Rotation[0]) + ": Unity placement vs ModelMatrix max " + worst.ToString("0.0000") + " m over " + cnt + " dummies");
        }
        // (the old "start light 0 seen from the pole" line used the KS_START_LIGHT NODE position, which is the FBX origin on magione / monza:
        // meaningless. wlamps measures the lamp panels from their vertices.)
        return 0;
    }
}
static partial class Program
{
    // wjoball: the W track job (scan + produce, items drained) on every owned layout: failures, problems, texture problems, timings
    static int Cmd_wjoball(List<string> a)
    {
        var c = AcContent.Scan(_install);
        int ok = 0, bad = 0;
        foreach (var t in c.Tracks) foreach (var l in t.Layouts)
        {
            if (!l.Owned) continue;
            var job = new AcTrackJob(l, new AcTrackJobOptions { SystemDir = _install.SystemDir, WeatherDir = Path.Combine(_install.Content, "weather"), SystemSurfacesIni = _install.SystemSurfacesIni });
            var th = new System.Threading.Thread(job.Run) { IsBackground = true }; th.Start();
            int texProb = 0, items = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true) { AcJobItem it; if (job.TryTake(out it)) { items++; if (it.Tex != null && it.Tex.Problem != null) texProb++; continue; } if (job.Done && job.Queued == 0) break; System.Threading.Thread.Sleep(1); }
            bool good = !job.Failed && job.Produced == job.WorkTotal && texProb == 0;
            if (good) ok++; else bad++;
            Console.WriteLine((good ? "ok   " : "FAIL ") + l.Key + ": " + sw.ElapsedMilliseconds + " ms, kn5 " + job.Files.Count + ", items " + items + "/" + job.WorkTotal + ", drawn " + job.MeshesRendered + ", colliders " + job.MeshesCollide +
                " (walls " + job.Walls + "), tex " + job.TexturesTotal + " (problems " + texProb + ", mips made " + job.MipsMade + "), grid " + job.Markers.Starts.Count + " pits " + job.Markers.Pits.Count + " gates " + job.Markers.Gates.Count +
                " lights " + job.Markers.StartLights.Count + ", lighting.ini " + (job.LightingIni != null ? "y" : "n") + (job.Failed ? " ERROR " + job.Error : "") + (job.Problems.Count > 0 ? " problems: " + string.Join("; ", job.Problems) : ""));
        }
        Console.WriteLine(ok + " ok, " + bad + " failed");
        return bad == 0 ? 0 : 1;
    }
}
