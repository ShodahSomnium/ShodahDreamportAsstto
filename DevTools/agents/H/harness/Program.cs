using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumAssetto;

// AC Dreamport data-core harness (dotnet 8, compiles Assets/#User/SomniumAssetto/Data/**).
//   dotnet run -c Release -- <command> [args] [--root <acRoot>]
// commands: content | kn5 <path> [tree|mats|tex] | acd <car> [file] | ksanim <path|dir> | ai <track> [layout] | fsb <bank>
//           | fev <bank> [filter] | dds <kn5> <texName> <out.png> | all [--decode]
static partial class Program
{
    static AcInstall _install;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static int Main(string[] argv)
    {
        CultureInfo.DefaultThreadCurrentCulture = Inv;
        System.Threading.Thread.CurrentThread.CurrentCulture = Inv;
        AcLog.Sink = Console.WriteLine;   // Data/ code (and module probes) may log through AcLog
        var args = new List<string>(argv);
        string root = null;
        int ri = args.IndexOf("--root");
        if (ri >= 0 && ri + 1 < args.Count) { root = args[ri + 1]; args.RemoveRange(ri, 2); }
        if (args.Count == 0) { Console.WriteLine("commands: content | kn5 <path> [tree|mats|tex] | acd <car> [file] | ksanim <path|dir> | ai <track> [layout] | fsb <bank> | fev <bank> [filter] | dds <kn5> <tex> <out.png> | all [--decode]"); return 1; }
        var sw = Stopwatch.StartNew();
        _install = AcInstall.Find(root);
        if (_install == null) { Console.WriteLine("install not found" + (AcInstall.HintProblem != null ? ": " + AcInstall.HintProblem : "")); return 2; }
        Console.WriteLine("install: " + _install.Root + " (" + _install.FoundBy + ", " + AcInstall.LibrariesSearched + " steam libraries, " + sw.ElapsedMilliseconds + " ms)");
        try
        {
            switch (args[0])
            {
                case "content": return Content();
                case "kn5": return Kn5Cmd(args[1], args.Count > 2 ? args[2] : "");
                case "acd": return AcdCmd(args[1], args.Count > 2 ? args[2] : null);
                case "ksanim": return KsanimCmd(args[1], args.Count > 2 ? args[2] : null);
                case "ai": return AiCmd(args[1], args.Count > 2 ? args[2] : "");
                case "fsb": return FsbCmd(args[1]);
                case "fev": return FevCmd(args[1], args.Count > 2 ? args[2] : null);
                case "dds": return DdsCmd(args[1], args[2], args[3]);
                case "chunk": return ChunkCmd(args[1], args[2]);
                case "selftest": return SelfTest();
                case "knh":
                {
                    var kh = Knh.Read(File.Exists(args[1]) ? args[1] : Path.Combine(CarPath(args[1]), "driver_base_pos.knh"));
                    foreach (var n in kh.Nodes) { int d = 0; for (var p = n.Parent; p != null; p = p.Parent) d++; Console.WriteLine(new string(' ', d) + n.Name + " world " + n.World.Translation); }
                    return 0;
                }
                case "hint": { var a = AcInstall.Find(args[1]); Console.WriteLine("hint result: " + (a == null ? "null" : a.Root + " by " + a.FoundBy) + " problem: " + AcInstall.HintProblem); return 0; }
                case "all": return All(args.Contains("--decode"));
                default:
                {
                    // Module probes: a file Probe<Code>.cs in this folder with `static partial class Program { static int Cmd_<name>(List<string> a) {...} }`
                    // runs as `<name>` (a[0] = the command). No edit of this switch needed (reflection is fine here: the harness never ships).
                    var mi = typeof(Program).GetMethod("Cmd_" + args[0], System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (mi != null) return (int)mi.Invoke(null, new object[] { args });
                    Console.WriteLine("unknown command " + args[0]); return 1;
                }
            }
        }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); return 3; }
    }

    static string F(float v) { return float.IsNaN(v) ? "-" : v.ToString("0.###", Inv); }

    static string CarPath(string car) { return Directory.Exists(car) ? car : Path.Combine(_install.CarsDir, car); }

    static string BankPath(string bank)
    {
        if (File.Exists(bank)) return bank;
        if (bank == "common") return _install.CommonBank;
        return Path.Combine(Path.Combine(CarPath(bank), "sfx"), bank + ".bank");
    }

    // ------------------------------------------------------------------ content
    static int Content()
    {
        var c = AcContent.Scan(_install);
        Console.WriteLine("cars " + c.Cars.Count + " (owned " + c.OwnedCars + ", DLC stubs " + c.StubCars + "), tracks " + c.Tracks.Count +
                          ", layouts " + c.TotalLayouts + " (owned " + c.OwnedLayouts + ", DLC stubs " + c.StubLayouts + "), scan " + c.ScanMs + " ms");
        int skins = 0, previews = 0, badges = 0, banks = 0, curves = 0;
        foreach (var car in c.Cars)
        {
            if (!car.Owned) continue;
            skins += car.Skins.Count;
            foreach (var s in car.Skins) if (s.PreviewPath != null) previews++;
            if (car.BadgePath != null) badges++;
            if (car.BankPath != null) banks++;
            if (car.PowerCurve.Count > 0) curves++;
        }
        Console.WriteLine("owned cars: skins " + skins + " (with preview " + previews + "), badges " + badges + ", banks " + banks + ", power curves " + curves);
        foreach (var car in c.Cars)
            if (car.Owned)
                Console.WriteLine("  CAR " + car.Id + " | " + car.Name + " | " + car.Brand + " | " + car.Class + " | bhp " + F(car.Specs.Bhp) + " Nm " + F(car.Specs.TorqueNm) +
                                  " kg " + F(car.Specs.WeightKg) + " top " + F(car.Specs.TopSpeedKmh) + " acc " + F(car.Specs.Acceleration0100) + " pw " + F(car.Specs.PwRatio) +
                                  " | skins " + car.Skins.Count + " | kn5 " + Path.GetFileName(car.Kn5Path) + (car.Parent != null ? " | parent " + car.Parent : ""));
        int stubShown = 0;
        foreach (var car in c.Cars) if (car.IsDlcStub && stubShown++ < 3) Console.WriteLine("  STUB " + car.Id + " | " + car.Name);
        foreach (var t in c.Tracks)
        {
            Console.WriteLine("  TRACK " + t.Id + " | " + t.Name + (t.MultiLayout ? " (multi)" : ""));
            foreach (var l in t.Layouts)
                Console.WriteLine("     " + (l.Id == "" ? "(single)" : l.Id) + " | " + l.Name + " | owned " + l.Owned + (l.IsDlcStub ? " DLC" : "") + " | len " + F(l.LengthM) + " (\"" + l.LengthText + "\") | pit " + l.Pitboxes +
                                  " | " + l.Country + "/" + l.City + " | kn5 " + l.Kn5Files.Count + (l.MissingKn5.Count > 0 ? " missing " + string.Join(",", l.MissingKn5) : "") +
                                  " | preview " + (l.PreviewPath != null) + " outline " + (l.OutlinePath != null) + " map " + (l.MapPath != null) + " ai " + (l.FastLanePath != null));
        }
        foreach (var p in c.Problems) Console.WriteLine("  PROBLEM " + p);
        return 0;
    }

    // ------------------------------------------------------------------ kn5
    static int Kn5Cmd(string path, string mode)
    {
        var sw = Stopwatch.StartNew();
        var k = Kn5File.Read(path, new Kn5ReadOptions { Textures = true, Meshes = true, StrictEnd = true });
        long ms = sw.ElapsedMilliseconds;
        int d, m, s; long v, ix;
        k.Totals(out d, out m, out s, out v, out ix);
        Console.WriteLine(Path.GetFileName(path) + ": v" + k.Version + " tex " + k.Textures.Count + " mats " + k.Materials.Count + " dummies " + d + " meshes " + m + " skinned " + s +
                          " verts " + v + " indices " + ix + " trailing " + k.TrailingBytes + " (" + ms + " ms full read)");
        sw.Restart();
        var h = Kn5Scan.Scan(path);
        Console.WriteLine("header-only scan: " + sw.ElapsedMilliseconds + " ms, nodes " + h.Nodes.Count);
        var census = new Dictionary<string, int>();
        foreach (var t in k.Textures) { string f = t.Info.Format.ToString(); int c; census.TryGetValue(f, out c); census[f] = c + 1; }
        var sb = new StringBuilder();
        foreach (var kv in census) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append("  ");
        Console.WriteLine("textures: " + sb);
        if (mode == "tex") foreach (var t in k.Textures) Console.WriteLine("  " + t.Name + " " + t.Size + " B " + t.Info);
        if (mode == "mats")
            foreach (var mt in k.Materials)
            {
                var ps = new StringBuilder();
                foreach (var p in mt.Properties) ps.Append(p.Name).Append('=').Append(F(p.A)).Append(' ');
                var ts = new StringBuilder();
                foreach (var t in mt.Samplers) ts.Append(t.Name).Append('@').Append(t.Slot).Append('=').Append(t.Texture).Append(' ');
                Console.WriteLine("  " + mt.Name + " [" + mt.Shader + "] blend " + mt.BlendMode + " at " + mt.AlphaTested + " depth " + mt.DepthMode + " | " + ps + "| " + ts);
            }
        if (mode == "tree") Tree(k.Root, 0);
        var markers = new List<string>();
        foreach (var n in k.Nodes) if (AcTrackMarkers.Classify(n.Name) != AcMarkerKind.None && n.IsDummy) markers.Add(n.Name);
        if (markers.Count > 0) Console.WriteLine("markers: " + markers.Count + " e.g. " + string.Join(" ", markers.GetRange(0, Math.Min(12, markers.Count))));
        return 0;
    }

    static void Tree(Kn5Node n, int depth)
    {
        if (depth > 12) return;
        string extra = n.IsDummy ? " T" + n.Local.Translation : " v" + n.Mesh.VertexCount + " i" + n.Mesh.IndexCount + " mat" + n.Mesh.MaterialId + (n.Mesh.IsRenderable ? "" : " NOREND") + " lod " + n.Mesh.LodIn + "-" + n.Mesh.LodOut;
        Console.WriteLine(new string(' ', depth * 2) + (n.Active ? "" : "(off) ") + n.Name + extra);
        foreach (var c in n.Children) Tree(c, depth + 1);
    }

    static int DdsCmd(string kn5, string tex, string outPng)
    {
        var k = Kn5File.Read(kn5, Kn5ReadOptions.HeaderOnly);
        var t = k.Texture(tex);
        if (t == null) { Console.WriteLine("no texture " + tex); return 1; }
        var b = k.ReadTextureBytes(t);
        var info = AcDds.Parse(b, 0, b.Length);
        int w, h;
        var rgba = AcDds.DecodeRgba32(b, info, 0, out w, out h);
        Console.WriteLine(t.Name + ": " + info + " pf 0x" + info.PixelFlags.ToString("X") + " fourcc '" + info.FourCCText + "' bits " + info.BitCount + " masks " + info.RMask.ToString("X") + "/" + info.GMask.ToString("X") + "/" +
                          info.BMask.ToString("X") + "/" + info.AMask.ToString("X") + " size " + b.Length + " chain " + info.ChainBytes + (rgba == null ? " (not decodable)" : " -> " + outPng));
        if (rgba != null) Png.Write(outPng, w, h, rgba);
        return 0;
    }

    // ------------------------------------------------------------------ acd
    static int AcdCmd(string car, string file)
    {
        var sw = Stopwatch.StartNew();
        var a = AcdArchive.Open(CarPath(car));
        if (a == null) { Console.WriteLine("no data.acd / data folder"); return 1; }
        Console.WriteLine("key " + AcdArchive.Key(Path.GetFileName(CarPath(car))) + ", " + a.Count + " files, " + sw.ElapsedMilliseconds + " ms" + (a.Unpacked ? " (unpacked data/)" : ""));
        if (file != null) { Console.WriteLine(a.ReadText(file)); return 0; }
        foreach (var n in a.Names) Console.Write(n + "(" + a.ReadBytes(n).Length + ") ");
        Console.WriteLine();
        var car_ = a.ReadIni("car.ini");
        Console.WriteLine("car.ini: SCREEN_NAME=" + car_.Get("INFO", "SCREEN_NAME") + " TOTALMASS=" + F(car_.GetFloat("BASIC", "TOTALMASS")) +
                          " GRAPHICS_OFFSET=" + string.Join(",", car_.GetFloats("BASIC", "GRAPHICS_OFFSET") ?? new float[0]) + " DRIVEREYES=" + string.Join(",", car_.GetFloats("GRAPHICS", "DRIVEREYES") ?? new float[0]));
        var lods = a.ReadIniOrEmpty("lods.ini");
        foreach (var s in lods.WithPrefix("LOD_")) Console.WriteLine("  " + s.Name + " FILE=" + s.Get("FILE") + " IN=" + s.Get("IN") + " OUT=" + s.Get("OUT"));
        var eng = a.ReadIniOrEmpty("engine.ini");
        var power = a.ReadLut(eng.Get("HEADER", "POWER_CURVE"));
        if (power != null) Console.WriteLine("power.lut: " + power.Count + " points, peak torque " + F(power.MaxY) + " Nm @ " + F(power.ArgMaxY) + " rpm, at 6000 rpm " + F(power.Eval(6000)));
        return 0;
    }

    // ------------------------------------------------------------------ ksanim
    static int KsanimCmd(string path, string node)
    {
        var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.ksanim", SearchOption.AllDirectories) : new[] { path };
        int ok = 0, fail = 0, v1 = 0, v2 = 0;
        foreach (var f in files)
        {
            try
            {
                var a = Ksanim.Read(f);
                ok++; if (a.Version == 1) v1++; else v2++;
                if (files.Length == 1)
                {
                    Console.WriteLine(Path.GetFileName(f) + ": v" + a.Version + " nodes " + a.Nodes.Count + " frames " + a.FrameCount);
                    var nn = node != null ? a.Find(node) : null;
                    if (nn != null) { var m0 = nn.Frames[0]; Console.WriteLine("  " + nn.Name + " frame0 rows (" + F(m0.M0) + "," + F(m0.M1) + "," + F(m0.M2) + ")/(" + F(m0.M4) + "," + F(m0.M5) + "," + F(m0.M6) + ")/(" + F(m0.M8) + "," + F(m0.M9) + "," + F(m0.M10) + ") T" + m0.Translation); return 0; }
                    foreach (var n in a.Nodes) Console.WriteLine("  " + n.Name + " f0 T" + n.Frames[0].Translation + " fN T" + n.Frames[n.FrameCount - 1].Translation + " heading0 " + F(n.Frames[0].HeadingDeg) + " headingN " + F(n.Frames[n.FrameCount - 1].HeadingDeg));
                }
            }
            catch (Exception e) { fail++; Console.WriteLine("FAIL " + f + ": " + e.Message); }
        }
        Console.WriteLine("ksanim ok " + ok + " (v1 " + v1 + ", v2 " + v2 + ") fail " + fail);
        return fail == 0 ? 0 : 4;
    }

    // ------------------------------------------------------------------ ai / track
    static AcLayoutEntry FindLayout(AcContent c, string track, string layout)
    {
        var t = c.Track(track);
        if (t == null) return null;
        if (layout == "" && t.Layouts.Count > 0 && t.Layout("") == null) return t.Layouts[0];
        return t.Layout(layout);
    }

    static int AiCmd(string track, string layout)
    {
        var c = AcContent.Scan(_install);
        var l = FindLayout(c, track, layout);
        if (l == null) { Console.WriteLine("no layout " + track + "/" + layout); return 1; }
        var r = CheckLayout(l, true, new Dictionary<string, Kn5File>());
        return r == null ? 0 : 4;
    }

    /// <summary>Header-scans the layout's kn5 files, collects markers, reads the AI lines. Returns null when OK, else the problem.</summary>
    static string CheckLayout(AcLayoutEntry l, bool verbose, Dictionary<string, Kn5File> cache)
    {
        var sw = Stopwatch.StartNew();
        var models = AcModelsIni.Load(l.TrackDir, l.Id);
        var files = new List<Kn5File>();
        foreach (var m in models.Models)
        {
            Kn5File k;
            if (!cache.TryGetValue(m.FullPath, out k)) { k = Kn5Scan.Scan(m.FullPath); cache[m.FullPath] = k; if (k.TrailingBytes != 0) return m.File + " trailing " + k.TrailingBytes; }
            files.Add(k);
        }
        long scanMs = sw.ElapsedMilliseconds;
        var surfaces = AcSurfaces.Load(l.SurfacesIni, _install.SystemSurfacesIni);
        var mk = AcTrackMarkers.Collect(files, models.Models, surfaces);
        var fast = l.FastLanePath != null ? AcAiSpline.Read(l.FastLanePath) : null;
        var pit = l.PitLanePath != null ? AcAiSpline.Read(l.PitLanePath) : null;
        string problem = null;
        if (mk.Starts.Count == 0 && mk.AbStart == null) problem = "no AC_START";
        if (mk.Pits.Count != l.Pitboxes) problem = (problem == null ? "" : problem + "; ") + "pits " + mk.Pits.Count + " != ui pitboxes " + l.Pitboxes;
        if (fast != null && fast.TrailingBytes != 0) problem = (problem == null ? "" : problem + "; ") + "fast_lane trailing " + fast.TrailingBytes;
        if (pit != null && pit.TrailingBytes != 0) problem = (problem == null ? "" : problem + "; ") + "pit_lane trailing " + pit.TrailingBytes;
        float pole = float.NaN;
        string gateInfo = "";
        if (fast != null && !fast.IsEmpty)
        {
            if (mk.Starts.Count > 0) { var p = mk.Starts[0].Position; int hint = -1; pole = fast.Progress(p.X, p.Y, p.Z, ref hint); }
            foreach (var g in mk.Gates)
                if (g.Complete)
                {
                    var a = g.Left.Position; var b = g.Right.Position;
                    int hint = -1;
                    gateInfo += " g" + g.Index + "@" + fast.Progress((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2, ref hint).ToString("0.0000", Inv);
                }
        }
        if (verbose)
        {
            Console.WriteLine(l.Key + ": kn5 " + files.Count + " (header scan " + scanMs + " ms), starts " + mk.Starts.Count + " pits " + mk.Pits.Count + " (ui " + l.Pitboxes + ") gates " + mk.Gates.Count +
                              " hotlap " + mk.HotlapStarts.Count + " startlights " + mk.StartLights.Count + " markers " + mk.All.Count + " physics " + mk.PhysicsMeshes + " walls " + mk.WallMeshes +
                              " unmatched " + mk.UnmatchedPhysics.Count + (mk.UnmatchedPhysics.Count > 0 ? " (" + string.Join(",", mk.UnmatchedPhysics) + ")" : ""));
            Console.WriteLine("  surfaces " + surfaces.Surfaces.Count + ": " + Join(surfaces.Surfaces, 30));
            if (mk.Starts.Count > 0) Console.WriteLine("  AC_START_0 " + mk.Starts[0].Position + " heading " + F(mk.Starts[0].HeadingDeg) + " deg; pole progress " + F(pole) + gateInfo);
            if (mk.OpenFinish != null) Console.WriteLine("  open finish " + (mk.OpenFinish.Complete ? mk.OpenFinish.Left.Position + "-" + mk.OpenFinish.Right.Position : "incomplete"));
            if (mk.AbStart != null) Console.WriteLine("  A2B start/finish " + (mk.AbStart.Complete) + "/" + (mk.AbFinish != null && mk.AbFinish.Complete));
            if (fast != null) Console.WriteLine("  fast_lane: v" + fast.Version + " points " + fast.Count + " lap " + F(fast.LapLength) + " m payload " + fast.PayloadCount + " file grid " + fast.HasFileGrid + " " + fast.FileGridNx + "x" + fast.FileGridNz);
            if (pit != null) Console.WriteLine("  pit_lane: v" + pit.Version + " points " + pit.Count + " length " + F(pit.Total) + " m");
            if (fast != null && !fast.IsEmpty) NearestCheck(fast);
        }
        return problem;
    }

    static string Join(List<AcSurface> l, int max)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < l.Count && i < max; i++) sb.Append(l[i].Key).Append(l[i].FromSystem ? "(sys)" : "").Append(' ');
        return sb.ToString();
    }

    /// <summary>Our grid nearest vs brute force on random points near the line; progress tracking timing.</summary>
    static void NearestCheck(AcAiSpline s)
    {
        var rnd = new Random(1);
        int agree = 0, n = 2000;
        for (int k = 0; k < n; k++)
        {
            int i = rnd.Next(s.Count);
            float x = s.X(i) + (float)(rnd.NextDouble() * 16 - 8), y = s.Y(i) + (float)(rnd.NextDouble() * 2 - 1), z = s.Z(i) + (float)(rnd.NextDouble() * 16 - 8);
            int g = s.Nearest(x, y, z);
            int best = -1; float bd = float.MaxValue;
            for (int j = 0; j < s.Count; j++) { float dx = s.X(j) - x, dy = s.Y(j) - y, dz = s.Z(j) - z, d = dx * dx + dz * dz + 4 * dy * dy; if (d < bd) { bd = d; best = j; } }
            if (g == best) agree++;
        }
        // tracking: drive along the line
        var sw = Stopwatch.StartNew();
        int hint = -1; float last = 0; int wraps = 0, back = 0;
        for (int i = 0; i < s.Count * 4; i++)
        {
            int a = (i / 4) % s.Count, b = (a + 1) % s.Count; float t = (i % 4) / 4f;
            float x = s.X(a) + (s.X(b) - s.X(a)) * t, y = s.Y(a) + (s.Y(b) - s.Y(a)) * t, z = s.Z(a) + (s.Z(b) - s.Z(a)) * t;
            float p = s.Progress(x + 1.5f, y, z, ref hint);
            if (p < last - 0.5f) wraps++; else if (p < last - 1e-4f) back++;
            last = p;
        }
        Console.WriteLine("  nearest grid==brute " + agree + "/" + n + "; tracking " + (s.Count * 4) + " steps " + sw.Elapsed.TotalMilliseconds.ToString("0.0", Inv) + " ms, wraps " + wraps + " backwards " + back);
    }

    // ------------------------------------------------------------------ fmod
    static FmodGuids _guids;
    static FmodGuids Guids() { if (_guids == null) _guids = FmodGuids.Load(_install.GuidsTxt); return _guids; }

    static int FsbCmd(string bank)
    {
        var f = Fsb5.ReadFile(BankPath(bank));
        Console.WriteLine("FSB5 v" + f.Version + " mode " + f.Mode + " samples " + f.Samples.Count + " @" + f.Offset + " data " + f.DataSize);
        foreach (var s in f.Samples) Console.WriteLine("  " + s.Index + " " + s + (s.LoopStart >= 0 ? " loop " + s.LoopStart + "-" + s.LoopEnd : "") + " " + s.Seconds.ToString("0.00", Inv) + " s");
        return 0;
    }

    static int FevCmd(string bank, string filter)
    {
        string path = BankPath(bank);
        string car = Path.GetFileNameWithoutExtension(path);
        var sw = Stopwatch.StartNew();
        var b = FmodBank.Read(path, Guids(), "/cars/" + car + "/");
        Console.WriteLine("bank FMT 0x" + b.FormatVersion.ToString("X") + "/0x" + b.FormatSub.ToString("X") + ", events " + b.Events.Count + ", instruments " + b.Instruments.Count +
                          ", params " + b.Parameters.Count + ", unresolved entries " + b.UnresolvedEntries + ", FSB5 " + (b.Fsb != null ? b.Fsb.Samples.Count + " samples mode " + b.Fsb.Mode : "none") + " (" + sw.ElapsedMilliseconds + " ms)");
        var cc = new StringBuilder();
        foreach (var kv in b.ChunkCounts) cc.Append(kv.Key.Trim()).Append(' ').Append(kv.Value).Append(' ');
        Console.WriteLine("chunks: " + cc);
        if (b.UnresolvedIds.Count > 0)
        {
            var raw = File.ReadAllBytes(path);
            foreach (var g in b.UnresolvedIds)
            {
                var gb = g.ToByteArray();
                for (int p = 8; p + 16 <= Math.Min(raw.Length, (int)b.SndOffset); p++)
                {
                    bool eq = true;
                    for (int q = 0; q < 16 && eq; q++) if (raw[p + q] != gb[q]) eq = false;
                    if (eq) Console.WriteLine("  unresolved " + g + " found at " + p + " preceded by '" + Encoding.ASCII.GetString(raw, p - 8, 4) + "' size " + BitConverter.ToInt32(raw, p - 4));
                }
            }
        }
        foreach (var ev in b.Events)
        {
            if (filter != null && ev.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var ps = new StringBuilder();
            foreach (var p in ev.Parameters) ps.Append(p).Append(' ');
            Console.WriteLine("EVENT " + ev.Path + "  params: " + ps);
            foreach (var sh in ev.Sheets)
            {
                Console.WriteLine("  sheet " + (sh.Parameter != null ? sh.Parameter.Name : "?") + (sh.AutomationOnly ? " (automation only)" : ": " + sh.Instruments.Count + " instruments"));
                foreach (var i in sh.Instruments) PrintInst(i);
            }
            if (ev.Timeline.Count > 0) { Console.WriteLine("  timeline: " + ev.Timeline.Count + " instruments"); foreach (var i in ev.Timeline) PrintInst(i); }
        }
        foreach (bool interior in new[] { true, false })
        {
            var layers = b.EngineLayers(interior);
            Console.WriteLine((interior ? "engine_int" : "engine_ext") + " layers: " + layers.Count);
            foreach (var l in layers) Console.WriteLine("  " + l + "  gain@3000/1 " + l.Gain(3000, 1).ToString("0.00", Inv) + " @3000/0 " + l.Gain(3000, 0).ToString("0.00", Inv) + " @6000/1 " + l.Gain(6000, 1).ToString("0.00", Inv) + " pitch@6000 " + l.Pitch(6000).ToString("0.00", Inv));
        }
        return 0;
    }

    /// <summary>Hex dump of every RIFF chunk with the given id (diagnostics for the FEV decoder).</summary>
    static int ChunkCmd(string bank, string id)
    {
        var raw = File.ReadAllBytes(BankPath(bank));
        id = id.PadRight(4);
        Action<int, int> walk = null;
        walk = (p, end) =>
        {
            while (p + 8 <= end)
            {
                string cid = Encoding.ASCII.GetString(raw, p, 4); int sz = BitConverter.ToInt32(raw, p + 4);
                if (sz < 0 || p + 8 + sz > end) return;
                if (cid == "LIST") walk(p + 12, p + 8 + sz);
                else if (cid == id)
                {
                    Console.WriteLine(cid + " @" + p + " size " + sz);
                    for (int o = 0; o < sz; o += 16)
                    {
                        var sb = new StringBuilder("  " + o.ToString("D4") + ": ");
                        for (int q = 0; q < 16 && o + q < sz; q++) sb.Append(raw[p + 8 + o + q].ToString("X2")).Append(' ');
                        Console.WriteLine(sb);
                    }
                }
                p += 8 + sz + (sz & 1);
            }
        };
        walk(12, raw.Length);
        return 0;
    }

    static void PrintInst(FmodInstrument i)
    {
        Console.WriteLine("    " + (i.SampleName ?? "?").PadRight(22) + (i.RegionParam != null ? " " + i.RegionParam.Name + " " + F(i.RegionStart) + ".." + F(i.RegionEnd) : " pos " + i.TimelinePos + " len " + i.TimelineLength) +
                          (i.AutoPitchParam != null ? " autopitch " + i.AutoPitchParam.Name + " ref " + F(i.AutoPitchRef) : "") + " vol " + F(i.VolumeDb) + "dB pitch " + F(i.PitchSemis) + "st loop " + i.LoopCount);
        foreach (var a in i.Automations)
            Console.WriteLine("        " + (a.OnBus ? "BUS " : "") + (a.Property == 0 ? "vol(dB)" : a.Property == 4 ? "vol(lin)" : a.Property == 1 ? "pitch" : "prop" + a.Property) + " on " + a.ParamName + ": " + (a.Curve != null ? a.Curve.ToString() : "?"));
    }

    // ------------------------------------------------------------------ all
    sealed class Tally
    {
        public int Ok, Fail; public long Ms; public readonly List<string> Fails = new List<string>();
        public void Pass() { Ok++; }
        public void No(string what) { Fail++; if (Fails.Count < 40) Fails.Add(what); }
        public override string ToString() { return "pass " + Ok + " fail " + Fail + " (" + Ms + " ms)"; }
    }

    static int All(bool decode)
    {
        var total = Stopwatch.StartNew();
        var content = AcContent.Scan(_install);
        Console.WriteLine("content: cars " + content.Cars.Count + " owned " + content.OwnedCars + " stubs " + content.StubCars + ", layouts " + content.TotalLayouts + " owned " + content.OwnedLayouts +
                          " stubs " + content.StubLayouts + ", problems " + content.Problems.Count + " (" + content.ScanMs + " ms)");
        var kn5 = new Tally(); var tex = new Tally(); var acd = new Tally(); var lods = new Tally(); var inis = new Tally(); var bank = new Tally();
        var anim = new Tally(); var knh = new Tally(); var drv = new Tally(); var lay = new Tally(); var trk = new Tally(); var ai = new Tally(); var json = new Tally();
        var texFormats = new Dictionary<AcTexFormat, int>();
        long kn5Bytes = 0, verts = 0;
        int decoded = 0, engineLayersInt = 0, engineLayersExt = 0, fixedPitchLayers = 0, knhNoRig = 0;
        var guids = Guids();

        // ui json of every car / skin / track (parsed by the scan; count files that gave no object)
        foreach (var car in content.Cars)
        {
            if (car.UiJsonPath != null) { if (car.UiJson != null) json.Pass(); else json.No(car.UiJsonPath); }
            foreach (var s in car.Skins) { string p = Path.Combine(s.Dir, "ui_skin.json"); if (File.Exists(p)) { if (AcJson.ParseObjectFile(p) != null) json.Pass(); else json.No(p); } }
        }
        foreach (var t in content.Tracks) foreach (var l in t.Layouts) if (l.UiJsonPath != null) { if (l.UiJson != null) json.Pass(); else json.No(l.UiJsonPath); }

        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            // kn5: every model file of the car (LODs + collider), full read incl. textures
            var sw = Stopwatch.StartNew();
            var models = new List<string>(car.Kn5Files);
            if (car.ColliderPath != null) models.Add(car.ColliderPath);
            foreach (var f in models)
            {
                try
                {
                    var k = Kn5File.Read(f, new Kn5ReadOptions { Textures = true, Meshes = true, Pooled = true, ConvertToUnity = true });
                    if (k.TrailingBytes != 0) { kn5.No(f + " trailing " + k.TrailingBytes); continue; }
                    int d, m, s; long v, ix; k.Totals(out d, out m, out s, out v, out ix);
                    verts += v; kn5Bytes += k.FileLength;
                    bool idxOk = true;
                    foreach (var n in k.Nodes) if (n.Mesh != null) { for (int q = 0; q < n.Mesh.IndexCount; q++) if (n.Mesh.Indices[q] >= n.Mesh.VertexCount) { idxOk = false; break; } if (n.Mesh.MaterialId >= k.Materials.Count) idxOk = false; }
                    if (!idxOk) { kn5.No(f + " index/material out of range"); k.Release(); continue; }
                    kn5.Pass();
                    foreach (var t in k.Textures)
                    {
                        var info = t.Info;
                        int c; texFormats.TryGetValue(info.Format, out c); texFormats[info.Format] = c + 1;
                        if (info.Format == AcTexFormat.Unknown || info.Truncated) { tex.No(Path.GetFileName(f) + ":" + t.Name + " " + info + " fourcc " + info.FourCCText + " pf 0x" + info.PixelFlags.ToString("X") + " bits " + info.BitCount + (info.Truncated ? " TRUNCATED" : "")); continue; }
                        if (decode && info.IsDds)
                        {
                            int w, h; var px = AcDds.DecodeRgba32(t.Bytes, info, 0, out w, out h);
                            if (px == null && info.Format != AcTexFormat.BC7 && info.Format != AcTexFormat.BC6H) { tex.No(t.Name + " decode failed " + info); continue; }
                            decoded++;
                        }
                        tex.Pass();
                    }
                    k.Release();
                }
                catch (Exception e) { kn5.No(f + ": " + e.Message); }
            }
            kn5.Ms += sw.ElapsedMilliseconds;

            // data.acd + every ini / lut inside + lods.ini files exist
            sw.Restart();
            try
            {
                var a = AcdArchive.Open(car.Dir);
                var carIni = a.ReadIni("car.ini");
                if (carIni == null || !(carIni.Has("HEADER") || carIni.Has("BASIC")) || a.Count < 20) acd.No(car.Id + " car.ini " + (carIni == null ? "missing" : "no HEADER/BASIC") + ", files " + a.Count);
                else acd.Pass();
                foreach (var n in a.Names)
                {
                    try
                    {
                        if (n.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) { a.ReadIni(n); inis.Pass(); }
                        else if (n.EndsWith(".lut", StringComparison.OrdinalIgnoreCase)) { var lut = AcLut.FromBytes(a.ReadBytes(n)); if (lut.Count == 0 && a.ReadBytes(n).Length > 8) inis.No(car.Id + "/" + n + " empty lut"); else inis.Pass(); }
                        else if (n.EndsWith(".rto", StringComparison.OrdinalIgnoreCase)) { if (AcLut.ParseRto(a.ReadText(n)).Count > 0 || a.ReadText(n).Trim().Length == 0) inis.Pass(); else inis.No(car.Id + "/" + n + " empty rto"); }
                    }
                    catch (Exception e) { inis.No(car.Id + "/" + n + ": " + e.Message); }
                }
                var lodIni = a.ReadIniOrEmpty("lods.ini");
                var lodSecs = lodIni.WithPrefix("LOD_");
                bool lodOk = lodSecs.Count > 0;
                foreach (var s in lodSecs) if (!File.Exists(Path.Combine(car.Dir, s.Get("FILE", "")))) lodOk = false;
                if (lodOk && lodSecs.Count > 0 && !string.Equals(Path.Combine(car.Dir, lodSecs[0].Get("FILE", "")), car.Kn5Path, StringComparison.OrdinalIgnoreCase)) { lods.No(car.Id + " LOD_0 " + lodSecs[0].Get("FILE") + " != largest kn5 " + Path.GetFileName(car.Kn5Path)); }
                else if (lodOk) lods.Pass(); else lods.No(car.Id + " lods.ini files missing");
                // power lut referenced by engine.ini
                var eng = a.ReadIniOrEmpty("engine.ini");
                var pw = a.ReadLut(eng.Get("HEADER", "POWER_CURVE"));
                if (pw == null || pw.Count < 2) inis.No(car.Id + " POWER_CURVE " + eng.Get("HEADER", "POWER_CURVE"));
            }
            catch (Exception e) { acd.No(car.Id + ": " + e.Message); }
            acd.Ms += sw.ElapsedMilliseconds;

            // bank
            sw.Restart();
            if (car.BankPath == null) bank.No(car.Id + " no bank");
            else
            {
                try
                {
                    FmodGuids g = guids;
                    if (car.GuidsPath != null) { g = FmodGuids.Load(_install.GuidsTxt); g.AddFile(car.GuidsPath); }
                    var b = FmodBank.Read(car.BankPath, g, "/cars/" + car.Id + "/");
                    var li = b.EngineLayers(true); var le = b.EngineLayers(false);
                    engineLayersInt += li.Count; engineLayersExt += le.Count;
                    string why = null;
                    if (b.Fsb == null || b.Fsb.Mode != 2) why = "FSB5 missing or not PCM16";
                    else if (li.Count == 0 || le.Count == 0) why = "engine layers int " + li.Count + " ext " + le.Count;
                    else
                    {
                        foreach (var l in li) { if (l.Sample < 0) why = "int layer without sample"; if (l.PitchRefRpm <= 0) fixedPitchLayers++; }
                        foreach (var l in le) { if (l.Sample < 0) why = "ext layer without sample"; if (l.PitchRefRpm <= 0) fixedPitchLayers++; }
                        foreach (var s in b.Fsb.Samples) if (s.DataOffset + s.Pcm16Bytes > new FileInfo(car.BankPath).Length) why = "sample " + s.Name + " past EOF";
                        if (b.Event("door") == null && b.Event("engine_ext") == null) why = "events unnamed";
                        var first = b.ReadSampleFloats(li[0].Sample);
                        if (first == null || first.Length == 0) why = "sample read failed";
                    }
                    if (why == null) bank.Pass(); else bank.No(car.Id + ": " + why);
                }
                catch (Exception e) { bank.No(car.Id + ": " + e.Message); }
            }
            bank.Ms += sw.ElapsedMilliseconds;

            // animations + knh
            sw.Restart();
            if (car.AnimationsDir != null)
                foreach (var f in Directory.GetFiles(car.AnimationsDir, "*.ksanim"))
                {
                    try
                    {
                        using (var r = AcBinary.OpenFile(f))
                        {
                            var ka = Ksanim.Read(r);
                            if (!r.AtEnd) anim.No(f + " trailing " + r.Remaining); else anim.Pass();
                        }
                    }
                    catch (Exception e) { anim.No(f + ": " + e.Message); }
                }
            anim.Ms += sw.ElapsedMilliseconds;
            if (car.KnhPath != null)
            {
                try
                {
                    using (var r = AcBinary.OpenFile(car.KnhPath))
                    {
                        var kh = Knh.Read(r);
                        if (!r.AtEnd) knh.No(car.Id + " knh trailing " + r.Remaining); else knh.Pass(); if (kh.Find("DRIVER:RIG_Center") == null) knhNoRig++;
                    }
                }
                catch (Exception e) { knh.No(car.Id + ": " + e.Message); }
            }
        }

        // other ksanim in content (objects3D pit crew etc.) + driver kn5
        foreach (var dir in new[] { Path.Combine(_install.Content, "objects3D"), _install.DriverDir })
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.ksanim", SearchOption.AllDirectories))
                {
                    try { using (var r = AcBinary.OpenFile(f)) { Ksanim.Read(r); if (!r.AtEnd) anim.No(f + " trailing"); else anim.Pass(); } }
                    catch (Exception e) { anim.No(f + ": " + e.Message); }
                }
        foreach (var f in Directory.GetFiles(_install.DriverDir, "*.kn5", SearchOption.AllDirectories))
        {
            try { var k = Kn5File.Read(f, new Kn5ReadOptions { Textures = true, Meshes = true }); if (k.TrailingBytes != 0) drv.No(f + " trailing"); else drv.Pass(); }
            catch (Exception e) { drv.No(f + ": " + e.Message); }
        }

        // common.bank
        try
        {
            var cb = FmodBank.Read(_install.CommonBank, guids, null);
            if (cb.Fsb != null && cb.Fsb.Mode == 2 && cb.Event("event:/collisions/car/hit") != null) bank.Pass(); else bank.No("common.bank events/fsb");
        }
        catch (Exception e) { bank.No("common.bank: " + e.Message); }

        // tracks: markers + ai per owned layout (header scan), then a full mesh read of every distinct track kn5
        var cache = new Dictionary<string, Kn5File>(StringComparer.OrdinalIgnoreCase);
        var swT = Stopwatch.StartNew();
        foreach (var t in content.Tracks)
            foreach (var l in t.Layouts)
            {
                if (!l.Owned) continue;
                try
                {
                    string p = CheckLayout(l, false, cache);
                    if (p == null) lay.Pass(); else lay.No(l.Key + ": " + p);
                    foreach (var aiPath in new[] { l.FastLanePath, l.PitLanePath })
                    {
                        if (aiPath == null) continue;
                        var s = AcAiSpline.Read(aiPath);
                        if (s.TrailingBytes != 0) ai.No(aiPath + " trailing " + s.TrailingBytes);
                        else if (s.IsEmpty && !(l.TrackId == "drift")) ai.No(aiPath + " empty v" + s.Version);
                        else ai.Pass();
                    }
                }
                catch (Exception e) { lay.No(l.Key + ": " + e.Message); }
            }
        lay.Ms = swT.ElapsedMilliseconds;
        swT.Restart();
        long trackBytes = 0;
        foreach (var kv in cache)
        {
            try
            {
                var k = Kn5File.Read(kv.Key, new Kn5ReadOptions { Textures = false, Meshes = true, Pooled = true, ConvertToUnity = true });
                trackBytes += k.FileLength;
                int d, m, s; long v, ix; k.Totals(out d, out m, out s, out v, out ix);
                verts += v;
                bool ok = k.TrailingBytes == 0;
                foreach (var tx in k.Textures)
                {
                    var info = k.ReadTextureHeader(tx);
                    int c; texFormats.TryGetValue(info.Format, out c); texFormats[info.Format] = c + 1;
                    if (info.Format == AcTexFormat.Unknown || info.Truncated) tex.No(Path.GetFileName(kv.Key) + ":" + tx.Name + " " + info + (info.Truncated ? " TRUNCATED" : "") + " fourcc " + info.FourCCText + " pf 0x" + info.PixelFlags.ToString("X") + " bits " + info.BitCount + " r " + info.RMask.ToString("X"));
                    else tex.Pass();
                }
                k.Release();
                if (ok) trk.Pass(); else trk.No(kv.Key + " trailing " + k.TrailingBytes);
            }
            catch (Exception e) { trk.No(kv.Key + ": " + e.Message); }
        }
        trk.Ms = swT.ElapsedMilliseconds;

        Console.WriteLine("car kn5 (full read incl. textures, pooled, converted): " + kn5 + ", " + (kn5Bytes >> 20) + " MB");
        Console.WriteLine("textures (headers" + (decode ? " + mip0 RGBA decode " + decoded : "") + "): " + tex);
        var tf = new StringBuilder(); foreach (var kv in texFormats) tf.Append(kv.Key).Append(' ').Append(kv.Value).Append("  ");
        Console.WriteLine("  formats: " + tf);
        Console.WriteLine("data.acd: " + acd + "; inner ini/lut/rto: " + inis + "; lods.ini LOD files exist + LOD_0 = largest kn5: " + lods);
        Console.WriteLine("banks (car + common; engine_int layers " + engineLayersInt + ", engine_ext layers " + engineLayersExt + ", of them fixed-pitch (no auto-pitch) " + fixedPitchLayers + "): " + bank);
        Console.WriteLine("ksanim: " + anim + "; knh: " + knh + " (" + knhNoRig + " without a DRIVER:RIG_Center - root-only stubs); driver kn5: " + drv + "; ui json: " + json);
        Console.WriteLine("layouts (markers + surfaces + ai, header scan of " + cache.Count + " kn5): " + lay + "; ai files: " + ai);
        Console.WriteLine("track kn5 full mesh read (pooled, converted): " + trk + ", " + (trackBytes >> 20) + " MB");
        Console.WriteLine("vertices read total: " + verts + "; total time " + total.ElapsedMilliseconds + " ms");
        var all = new[] { kn5, tex, acd, inis, lods, bank, anim, knh, drv, json, lay, ai, trk };
        int fails = 0;
        foreach (var t in all) { fails += t.Fail; foreach (var f in t.Fails) Console.WriteLine("  FAIL " + f); }
        foreach (var p in content.Problems) Console.WriteLine("  PROBLEM " + p);
        Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
        return fails == 0 ? 0 : 5;
    }
}
