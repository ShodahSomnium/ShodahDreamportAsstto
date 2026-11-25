using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// Module C (car model) probes. Data-only commands compile in the shared harness; the ones using C's Unity-free
// Game/Cars/AcCarData.cs need AC_CARDATA (C's private copy: DevTools/agents/C/harness, which compiles that file too).
//   carparts [car]   node-presence census over every owned car (LOD_0 header scan): wheels, steer, cockpit, doors, lights.ini /
//                    blurred_objects / analog / digital names found, LOD files + their wheel dummies, collider.kn5 vs LOD_0 bounds.
//   cardata [car]    (AC_CARDATA) AcCarVisual.Load on every owned car: summary + problems + every bound name resolved.
//   needles [car]    (AC_CARDATA) needle node axes and rest direction vs the gauge zero (rotation sign check).
static partial class Program
{
    static readonly string[] WheelSuffix = { "LF", "RF", "LR", "RR" };

    sealed class CarNodes
    {
        public Kn5File Kn5;
        public HashSet<string> Exact = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Ci = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool Has(string n) { return n != null && Exact.Contains(n); }
        public bool HasCi(string n) { return n != null && Ci.Contains(n); }
    }

    static CarNodes ScanNodes(string path)
    {
        var c = new CarNodes { Kn5 = Kn5File.Read(path, Kn5ReadOptions.HeaderOnly) };
        foreach (var n in c.Kn5.Nodes) { c.Exact.Add(n.Name); c.Ci.Add(n.Name); }
        return c;
    }

    static string Lod0Path(AcCarEntry car, AcdArchive acd, out AcIni lods)
    {
        lods = acd != null ? acd.ReadIni("lods.ini") : null;
        string f = lods != null ? lods.Get("LOD_0", "FILE") : null;
        if (f != null) { string p = Path.Combine(car.Dir, f.Trim()); if (File.Exists(p)) return p; }
        return car.Kn5Path;
    }

    /// <summary>Approximate model-space AABB of a kn5 from its mesh bounding spheres (AC coords).</summary>
    static void SphereBounds(Kn5File k, out AcVec3 min, out AcVec3 max)
    {
        var w = Kn5Scan.WorldMatrices(k);
        min = new AcVec3(1e9f, 1e9f, 1e9f); max = new AcVec3(-1e9f, -1e9f, -1e9f);
        foreach (var n in k.Nodes)
        {
            if (n.Mesh == null || !n.Mesh.IsRenderable) continue;
            var c = w[n.Index].TransformPoint(n.Mesh.SphereCenter);
            float r = n.Mesh.SphereRadius;
            if (r > 20f) continue; // ours: skip shadow planes / sky junk
            min = new AcVec3(Math.Min(min.X, c.X - r), Math.Min(min.Y, c.Y - r), Math.Min(min.Z, c.Z - r));
            max = new AcVec3(Math.Max(max.X, c.X + r), Math.Max(max.Y, c.Y + r), Math.Max(max.Z, c.Z + r));
        }
    }

    /// <summary>Exact AABB of the vertices of a (small) kn5, e.g. collider.kn5 (AC coords).</summary>
    static bool VertexBounds(Kn5File k, out AcVec3 min, out AcVec3 max)
    {
        var w = Kn5Scan.WorldMatrices(k);
        min = new AcVec3(1e9f, 1e9f, 1e9f); max = new AcVec3(-1e9f, -1e9f, -1e9f);
        bool any = false;
        foreach (var n in k.Nodes)
        {
            if (n.Mesh == null) continue;
            if (!n.Mesh.Loaded) k.LoadMesh(n.Mesh);
            for (int i = 0; i < n.Mesh.VertexCount; i++)
            {
                var p = w[n.Index].TransformPoint(n.Mesh.Position(i));
                min = new AcVec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new AcVec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                any = true;
            }
        }
        return any;
    }

    static int Cmd_carparts(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        int cars = 0;
        var tally = new SortedDictionary<string, int>();
        Action<string> T = k => { tally[k] = tally.TryGetValue(k, out var n) ? n + 1 : 1; };
        int lightsFound = 0, lightsCi = 0, lightsMissing = 0, blurFound = 0, blurMissing = 0, needleFound = 0, needleMissing = 0;
        int parentFound = 0, parentMissing = 0, ledFound = 0, ledMissing = 0;
        var notes = new List<string>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            cars++;
            AcdArchive acd = null;
            try { acd = AcdArchive.Open(car.Dir); } catch (Exception e) { notes.Add(car.Id + ": acd " + e.Message); }
            AcIni lods;
            string lod0 = Lod0Path(car, acd, out lods);
            var nodes = ScanNodes(lod0);
            var miss = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                string s = WheelSuffix[i];
                foreach (var pre in new[] { "WHEEL_", "SUSP_", "DISC_", "RIM_", "RIM_BLUR_", "TYRE_" })
                {
                    if (nodes.Has(pre + s)) T(pre + "xx");
                    else if (nodes.HasCi(pre + s)) { T(pre + "xx(ci)"); }
                    else if (pre == "WHEEL_" || pre == "SUSP_") miss.Add(pre + s);
                }
            }
            foreach (var n in new[] { "STEER_HR", "STEER_LR", "COCKPIT_HR", "COCKPIT_LR", "DOOR_L", "DOOR_R", "SHIFT_HR", "WIPER_L", "DISPLAY_DATA" })
            {
                if (nodes.Has(n)) T(n); else if (nodes.HasCi(n)) T(n + "(ci)");
            }
            if (!nodes.HasCi("STEER_HR")) miss.Add("STEER_HR");
            // lights.ini
            var li = acd != null ? acd.ReadIni("lights.ini") : null;
            if (li != null)
                foreach (var s in li.Order)
                {
                    if (!(s.Name.StartsWith("LIGHT_") || s.Name.StartsWith("BRAKE_")) || s.Name.StartsWith("LIGHT_ANIMATION")) continue;
                    string nm = (s.Get("NAME") ?? "").Trim();
                    if (nm.Length == 0 || nm.Equals("NULL", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nodes.Has(nm)) lightsFound++; else if (nodes.HasCi(nm)) lightsCi++; else { lightsMissing++; miss.Add("light " + nm); }
                }
            var bl = acd != null ? acd.ReadIni("blurred_objects.ini") : null;
            if (bl != null)
                foreach (var s in bl.WithPrefix("OBJECT_"))
                {
                    string nm = (s.Get("NAME") ?? "").Trim(); if (nm.Length == 0) continue;
                    if (nodes.HasCi(nm)) blurFound++; else { blurMissing++; miss.Add("blur " + nm); }
                }
            var an = acd != null ? acd.ReadIni("analog_instruments.ini") : null;
            if (an != null)
                foreach (var s in an.Order)
                {
                    string nm = (s.Get("OBJECT_NAME") ?? "").Trim();
                    if (nm.Length == 0)
                    {
                        string def = s.Name.StartsWith("RPM") ? "ARROW_RPM" : s.Name.StartsWith("SPEED") ? "ARROW_SPEED" : s.Name.StartsWith("FUEL_IND") ? "ARROW_FUEL" : s.Name.StartsWith("TURBO") ? "ARROW_TURBO" : s.Name.StartsWith("WATER") ? "ARROW_WATER" : null;
                        T("analog blank " + s.Name + (def != null && nodes.HasCi(def) ? " -> " + def + " exists" : " -> no default node"));
                        continue;
                    }
                    if (nodes.HasCi(nm)) needleFound++; else { needleMissing++; miss.Add("needle " + nm); }
                }
            var di = acd != null ? acd.ReadIni("digital_instruments.ini") : null;
            if (di != null)
                foreach (var s in di.Order)
                {
                    string p = (s.Get("PARENT") ?? "").Trim();
                    if (p.Length > 0) { if (nodes.HasCi(p)) parentFound++; else { parentMissing++; miss.Add("digital parent " + p); } }
                    string o = (s.Get("OBJECT_NAME") ?? "").Trim();
                    if (o.Length > 0) { if (nodes.HasCi(o)) ledFound++; else { ledMissing++; miss.Add("led " + o); } }
                }
            // LODs
            if (lods != null)
                foreach (var s in lods.WithPrefix("LOD_"))
                {
                    string f = (s.Get("FILE") ?? "").Trim(); string p = Path.Combine(car.Dir, f);
                    if (!File.Exists(p)) { miss.Add(s.Name + " file " + f); continue; }
                    if (s.Name == "LOD_0") continue;
                    var ln = ScanNodes(p);
                    int w = 0; for (int i = 0; i < 4; i++) if (ln.HasCi("WHEEL_" + WheelSuffix[i])) w++;
                    T(s.Name + " wheels " + w + "/4");
                    T(s.Name + " textures " + (ln.Kn5.Textures.Count == 0 ? "0" : ">0"));
                }
            // collider vs LOD_0
            if (car.ColliderPath != null)
            {
                var col = Kn5File.Read(car.ColliderPath, Kn5ReadOptions.NoTextures);
                AcVec3 cmin, cmax, mmin, mmax;
                VertexBounds(col, out cmin, out cmax);
                VertexBounds(Kn5File.Read(lod0, Kn5ReadOptions.NoTextures), out mmin, out mmax);
                var cc = (cmin + cmax) * 0.5f; var mc = (mmin + mmax) * 0.5f;
                float dx = cc.X - mc.X, dz = cc.Z - mc.Z;
                float off = (float)Math.Sqrt(dx * dx + dz * dz);
                int tris = 0; foreach (var n in col.Nodes) if (n.Mesh != null) tris += n.Mesh.IndexCount / 3;
                if (off > 0.5f) notes.Add(car.Id + ": collider.kn5 centre off by " + F(off) + " m in xz (hull " + cc + " model " + mc + ") -> box fallback");
                T(off > 0.5f ? "hull rejected" : "hull ok");
                if (tris > 255) notes.Add(car.Id + ": collider has " + tris + " triangles (> 255: convex hull limit)");
            }
            else T("no collider.kn5");
            if (miss.Count > 0) notes.Add(car.Id + " missing: " + string.Join(", ", miss));
        }
        Console.WriteLine("cars " + cars);
        foreach (var kv in tally) Console.WriteLine("  " + kv.Key + ": " + kv.Value);
        Console.WriteLine("lights.ini names: exact " + lightsFound + ", case-insensitive " + lightsCi + ", missing " + lightsMissing);
        Console.WriteLine("blurred names: found " + blurFound + ", missing " + blurMissing);
        Console.WriteLine("analog needles: found " + needleFound + ", missing " + needleMissing);
        Console.WriteLine("digital parents: found " + parentFound + ", missing " + parentMissing + "; LED/fuel objects found " + ledFound + ", missing " + ledMissing);
        foreach (var n in notes) Console.WriteLine("  " + n);
        return 0;
    }

#if AC_CARDATA
    static int Cmd_cardata(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        int cars = 0, ok = 0, problems = 0, unresolved = 0;
        long ms = 0;
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            cars++;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var v = AcCarVisual.Load(car);
            ms += sw.ElapsedMilliseconds;
            var l0 = v.Lod(0);
            var nodes = l0 != null && l0.Path != null ? ScanNodes(l0.Path) : null;
            var miss = new List<string>();
            if (nodes != null)
            {
                foreach (var d in v.Lights) if (!nodes.HasCi(d.Name)) miss.Add("light " + d.Name);
                foreach (var d in v.Blur) if (!nodes.HasCi(d.Name)) miss.Add("blur " + d.Name);
                foreach (var d in v.Needles) if (!nodes.HasCi(d.ObjectName)) miss.Add((d.NameDefaulted ? "needle(default) " : "needle ") + d.ObjectName);
                foreach (var d in v.Items) if (!nodes.HasCi(d.Parent)) miss.Add("item parent " + d.Parent);
                foreach (var d in v.Leds) if (!nodes.HasCi(d.ObjectName)) miss.Add("led " + d.ObjectName);
                foreach (var d in v.FuelLeds) if (!nodes.HasCi(d.ObjectName)) miss.Add("fuel led " + d.ObjectName);
                foreach (var d in v.Displays) if (!nodes.HasCi(d.Name)) miss.Add("display " + d.Name);
                foreach (var d in v.Series)
                {
                    int f = 0, n = 0;
                    for (int i = d.StartIndex; i <= d.EndIndex; i++) { n++; if (nodes.HasCi(d.Prefix + i) || (d.CountBased && nodes.HasCi(d.Prefix + (i + 1)))) f++; }
                    if (f < n) miss.Add("series " + d.Prefix + " " + f + "/" + n);
                }
                AnimCheck(v.DoorL, nodes, "door L", miss); AnimCheck(v.DoorR, nodes, "door R", miss); AnimCheck(v.Wiper, nodes, "wiper", miss);
                AnimCheck(v.ShiftUp, nodes, "shift up", miss); AnimCheck(v.ShiftDown, nodes, "shift dw", miss); AnimCheck(v.Shift, nodes, "shift", miss);
                for (int i = 0; i < 4; i++) AnimCheck(v.Susp[i], nodes, "susp " + i, miss);
                foreach (var la in v.LightAnims) AnimCheck(la.Anim, nodes, "light anim " + la.File, miss);
            }
            else miss.Add("NO LOD_0");
            bool eyeOk = v.HasDriverEyes, radOk = v.HasRadius;
            if (v.Problems.Count == 0 && miss.Count == 0 && eyeOk && radOk) ok++;
            if (v.Problems.Count > 0) problems++;
            unresolved += miss.Count;
            Console.WriteLine(v.Summary() + " | eye " + (eyeOk ? v.DriverEyes.ToString() : "MISSING") + " hips " + v.KnhHips + " r " + F(v.RadiusFront) + "/" + F(v.RadiusRear)
                + " lodsw " + F(v.CockpitHrSwitch));
            foreach (var p in v.Problems) Console.WriteLine("    problem: " + p);
            if (miss.Count > 0) Console.WriteLine("    unresolved: " + string.Join(", ", miss));
        }
        Console.WriteLine("cardata: " + cars + " cars, " + ok + " clean, " + problems + " with problems, " + unresolved + " unresolved names, parse " + ms + " ms total");
        return 0;
    }

    static void AnimCheck(Ksanim k, CarNodes nodes, string what, List<string> miss)
    {
        if (k == null) return;
        foreach (var n in k.Nodes) if (!nodes.HasCi(n.Name)) miss.Add(what + " node " + n.Name);
    }

    /// <summary>Needle rest geometry: node world axes and the needle mesh direction in node space (AC coords, x = driver's left).</summary>
    static int Cmd_needles(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        int fwd = 0, back = 0, other = 0;
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            var v = AcCarVisual.Load(car, false, false);
            var l0 = v.Lod(0); if (l0 == null || l0.Path == null || v.Needles.Count == 0) continue;
            var k = Kn5File.Read(l0.Path, Kn5ReadOptions.HeaderOnly);
            var w = Kn5Scan.WorldMatrices(k);
            foreach (var d in v.Needles)
            {
                var node = k.Find(d.ObjectName); if (node == null) continue;
                var W = w[node.Index];
                var z = W.AxisZ.Normalized;
                if (z.Z > 0.7f) fwd++; else if (z.Z < -0.7f) back++; else other++;
                // needle direction: centroid of all mesh vertices below the node, in node-local space
                AcVec3 sum = new AcVec3(0, 0, 0); int cnt = 0; float maxR = 0; AcVec3 far = new AcVec3(0, 0, 0);
                var inv = W.InverseAffine();
                foreach (var n in k.Nodes)
                {
                    bool under = false; for (var p = n.Parent; p != null; p = p.Parent) if (p == node) { under = true; break; }
                    if (!under || n.Mesh == null) continue;
                    k.LoadMesh(n.Mesh);
                    for (int i = 0; i < n.Mesh.VertexCount; i++)
                    {
                        var pl = inv.TransformPoint(w[n.Index].TransformPoint(n.Mesh.Position(i)));
                        sum = sum + pl; cnt++;
                        float r = (float)Math.Sqrt(pl.X * pl.X + pl.Y * pl.Y);
                        if (r > maxR) { maxR = r; far = pl; }
                    }
                }
                var c = cnt > 0 ? sum * (1f / cnt) : sum;
                // angle of the far tip as seen by the driver (looking +Z): 0 = up, positive = clockwise (towards the driver's right = AC -X)
                double tipDeg = Math.Atan2(-far.X, far.Y) * 180.0 / Math.PI;
                Console.WriteLine(car.Id + " " + d.Section + " " + d.ObjectName + (d.NameDefaulted ? "(def)" : "") + " axisZ " + z + " axisY " + W.AxisY.Normalized
                    + " tip " + far + " tipDeg(cw from up) " + tipDeg.ToString("0") + " zero " + F(d.Zero) + " step " + F(d.Step) + " min " + F(d.MinValue)
                    + (d.Lut != null ? " lut[" + F(d.Lut.MinX) + "]=" + F(d.Lut.Eval(d.Lut.MinX)) + " lut[" + F(d.Lut.MaxX) + "]=" + F(d.Lut.Eval(d.Lut.MaxX)) + " step*(max-min)=" + F(d.Step * (d.Lut.MaxX - d.MinValue)) : ""));
            }
        }
        Console.WriteLine("needle node local Z: forward " + fwd + ", backward " + back + ", other " + other);
        return 0;
    }
#endif
}

static partial class Program
{
    // rimsusp [car]: the same math as Game/Cars/AcCarJob (ComputeSteerRim, ComputeSusp) on every owned car - steering rim radius and
    // the car_susp_xx rest time / travel range (data only, no Unity).
    static int Cmd_rimsusp(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        int n = 0, rimOk = 0;
        float rmin = 9, rmax = 0;
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            AcIni lods; var acd = AcdArchive.Open(car.Dir);
            string p = Lod0Path(car, acd, out lods);
            var k = Kn5File.Read(p, Kn5ReadOptions.NoTextures);
            var world = Kn5Scan.WorldMatrices(k);
            n++;
            var steer = k.Find("STEER_HR") ?? k.Find("STEER_LR");
            float best = 0f;
            if (steer != null)
            {
                var inv = world[steer.Index].InverseAffine();
                for (int i = 0; i < k.Nodes.Count; i++)
                {
                    var nd = k.Nodes[i]; var m = nd.Mesh; if (m == null || !m.Loaded) continue;
                    bool under = false, skip = false;
                    for (var q = nd.Parent; q != null; q = q.Parent)
                    {
                        string nm = q.Name ?? "";
                        if (q != steer && (nm.IndexOf("SHIFT", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("PAD", StringComparison.OrdinalIgnoreCase) >= 0)) skip = true;
                        if (q == steer) { under = true; break; }
                    }
                    if (!under || skip) continue;
                    for (int v = 0; v < m.VertexCount; v++)
                    {
                        var lp = inv.TransformPoint(world[i].TransformPoint(m.Position(v)));
                        float r = (float)Math.Sqrt(lp.X * lp.X + lp.Y * lp.Y);
                        if (r > best && r < 0.4f) best = r;
                    }
                }
            }
            if (best > 0.1f && best < 0.25f) rimOk++;
            rmin = Math.Min(rmin, best); rmax = Math.Max(rmax, best);
            string susp = "";
            string[] wn = { "WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR" }, an = { "car_susp_lf.ksanim", "car_susp_rf.ksanim", "car_susp_lr.ksanim", "car_susp_rr.ksanim" };
            for (int w = 0; w < 4 && car.AnimationsDir != null; w++)
            {
                string f = Path.Combine(car.AnimationsDir, an[w]);
                if (!File.Exists(f)) continue;
                var anim = Ksanim.Read(f);
                var node = k.Find(wn[w]); if (node == null) { susp += " " + wn[w] + "?"; continue; }
                int fc = anim.FrameCount; float rest = node.World.Translation.Y, y0 = 0, yN = 0, be = 9; int bf = 0;
                for (int fr = 0; fr < fc; fr++)
                {
                    AcMat4 mm = AcMat4.Identity;
                    for (var q = node; q != null; q = q.Parent) { if (q.Class != Kn5NodeClass.Dummy) continue; var x = anim.Find(q.Name); mm = AcMat4.Mul(mm, x != null && x.Frames.Length == fc ? x.Frames[fr] : q.Local); }
                    float y = mm.Translation.Y; if (fr == 0) y0 = y; if (fr == fc - 1) yN = y; float e = Math.Abs(y - rest); if (e < be) { be = e; bf = fr; }
                }
                susp += " " + wn[w].Substring(6) + " range " + F(yN - y0) + " restT " + F(bf / (float)(fc - 1)) + " err " + F(be);
            }
            Console.WriteLine(car.Id + ": rim " + F(best) + (susp.Length > 0 ? " susp" + susp : ""));
        }
        Console.WriteLine("rim radius in 0.10..0.25 m: " + rimOk + "/" + n + " (min " + F(rmin) + ", max " + F(rmax) + ")");
        return 0;
    }
}
