using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using SomniumSpace.Worlds.SomniumAssetto;

// Module A (audio) probes. Run: dotnet run -c Release --no-build -- <cmd> ...
//   sndevents <car|common>                 module A's compiled event model (Data/Audio AcSndBank)
//   engine <car> <rpm> <throttle> [int|ext] every engine layer's gain / pitch (A's evaluator, cross-checked against D's FmodBank)
//   sndcensus                              every owned car: events, multis, loop flags, A vs D layer agreement, PCM sizes, sounds.ini
//   render <car> <int|ext> <out.wav>       offline engine sweep (idle -> limiter on throttle, lift, back down) + levels per second
//   carsounds <car>                        sounds.ini + emitter geometry (AcCarSounds)
//   sndfx <car|common|all>                 DSP effects per event (spatialiser distances, filters, EQ, plugins + automations); all = census
//   render ... [nofx]                      (render prints dBFS and dB(A); nofx = without the master distortion)
static partial class Program
{
    static readonly CultureInfo Iv = CultureInfo.InvariantCulture;
    static string G3(float v) { return v.ToString("0.###", Iv); }

    static AcSndBank _mixer;
    static AcSndBank Mixer() { if (_mixer == null) _mixer = AcSndBank.Load(_install.CommonBank, Guids(), null); return _mixer; }
    static float Mix(AcSndInstrument ins) { return (float)Math.Pow(10.0, (ins.StaticDb + ins.MixerDb) / 20.0); }

    static AcSndBank LoadSnd(string bank)
    {
        string path = BankPath(bank);
        string car = Path.GetFileNameWithoutExtension(path);
        var b = AcSndBank.Load(path, Guids(), "/cars/" + car + "/");
        if (bank != "common") b.ApplyMixer(Mixer());
        return b;
    }

    static int Cmd_sndevents(List<string> a)
    {
        var b = LoadSnd(a[1]);
        Console.WriteLine(b + ", multis " + b.Multis + ", unresolved " + b.Unresolved + " (" + b.ParseMs.ToString("0", Iv) + " ms)");
        foreach (var ev in b.Events)
        {
            Console.WriteLine("EVENT " + ev.Path + " params [" + string.Join(", ", ev.Params) + "]" + (ev.TimelineLoops ? " TIMELINE-LOOP" : ""));
            for (int i = 0; i < ev.Instruments.Length; i++)
            {
                var ins = ev.Instruments[i];
                var sb = new StringBuilder("   ");
                sb.Append(ins.Multi ? "MULTI " + string.Join("|", ins.SampleNames) : ins.SampleNames[0]);
                if (ins.RegionParam >= 0) sb.Append(" ").Append(ev.Params[ins.RegionParam]).Append(" ").Append(G3(ins.RegionStart)).Append("..").Append(G3(ins.RegionEnd));
                else sb.Append(" timeline pos ").Append(ins.TimelinePos).Append(" len ").Append(ins.TimelineLength);
                sb.Append(" static ").Append(G3(ins.StaticDb)).Append("+").Append(G3(ins.MixerDb)).Append("dB");
                sb.Append(" vol ").Append(G3(ins.VolumeDb)).Append("dB pitch ").Append(G3(ins.PitchSemis)).Append("st loop ").Append(ins.LoopCount);
                if (ins.AutoPitchParam >= 0) sb.Append(" autopitch ").Append(ev.Params[ins.AutoPitchParam]).Append(" ref ").Append(G3(ins.AutoPitchRef));
                Console.WriteLine(sb);
                foreach (var au in ins.Autos)
                    Console.WriteLine("       " + (au.OnBus ? "BUS " : "") + (au.Prop == 0 ? "dB" : au.Prop == 1 ? "pitch(st/24)" : "lin") + " on " + ev.Params[au.Param] + ": " + au.Curve);
            }
        }
        return 0;
    }

    static int Cmd_engine(List<string> a)
    {
        string car = a[1];
        float rpm = float.Parse(a[2], Iv), thr = float.Parse(a[3], Iv);
        var which = a.Count > 4 ? new[] { a[4] == "int" } : new[] { true, false };
        var b = LoadSnd(car);
        var d = FmodBank.Read(BankPath(car), Guids(), "/cars/" + car + "/");
        foreach (bool interior in which)
        {
            var ev = b.Event(interior ? "engine_int" : "engine_ext");
            if (ev == null) { Console.WriteLine("no " + (interior ? "engine_int" : "engine_ext")); continue; }
            var dl = d.EngineLayers(interior);
            var vals = ev.NewValues();
            int ri = ev.ParamIndex("rpms"), ti = ev.ParamIndex("throttle");
            if (ri < 0) for (int k = 0; k < ev.Params.Length; k++) if (ev.Params[k].IndexOf("rpm", StringComparison.OrdinalIgnoreCase) >= 0) ri = k;
            if (ri >= 0) vals[ri] = rpm; if (ti >= 0) vals[ti] = thr;
            Console.WriteLine((interior ? "engine_int" : "engine_ext") + " at " + rpm + " rpm, throttle " + thr + " (" + ev.Instruments.Length + " layers):");
            float sum2 = 0f; int maxDiff = 0;
            for (int i = 0; i < ev.Instruments.Length; i++)
            {
                var ins = ev.Instruments[i];
                float g, p; ev.Evaluate(i, vals, out g, out p);
                sum2 += g * g;
                // D's layer with the same instrument + region
                float dg = float.NaN, dp = float.NaN;
                foreach (var l in dl) if (l.Instrument.Id == ins.Id && Math.Abs(l.RpmMin - ins.RegionStart) < 0.01f) { dg = l.Gain(rpm, thr) * Mix(ins); dp = l.Pitch(rpm); }
                string flag = float.IsNaN(dg) ? " (no D layer)" : Math.Abs(DbFloor(dg) - g) > 0.002f + 0.01f * g || Math.Abs(dp - p) > 0.001f ? " DIFF vs D " + G3(dg) + "/" + G3(dp) : "";
                if (flag.Length > 0) maxDiff++;
                Console.WriteLine("  " + ins.SampleNames[0].PadRight(22) + " " + G3(ins.RegionStart).PadLeft(6) + ".." + G3(ins.RegionEnd).PadRight(6) + " ref " + G3(ins.AutoPitchRef).PadRight(5)
                    + " static " + G3(ins.StaticDb) + "+" + G3(ins.MixerDb) + "dB"+ " gain " + g.ToString("0.0000", Iv) + " (" + (g > 0 ? (20 * Math.Log10(g)).ToString("0.0", Iv) + " dB" : "off") + ") pitch " + p.ToString("0.000", Iv) + flag);
            }
            Console.WriteLine("  power sum " + (sum2 > 0 ? (10 * Math.Log10(sum2)).ToString("0.0", Iv) + " dB" : "silent") + ", layers differing from D: " + maxDiff);
        }
        return 0;
    }

    // D treats only <= -80 dB as silence; A treats the banks' -42 floor as silence. Compare with A's rule.
    static float DbFloor(float g) { return g > 0f && g <= 0.00795f ? 0f : g; }

    static int Cmd_carsounds(List<string> a)
    {
        var s = AcCarSounds.Load(CarPath(a[1]), a[1]);
        Console.WriteLine(s);
        Console.WriteLine("problems: " + string.Join("; ", s.Problems));
        float x, y, z;
        s.EngineEmitter(out x, out y, out z); Console.WriteLine("engine emitter " + G3(x) + "," + G3(y) + "," + G3(z));
        s.BackfireEmitter(out x, out y, out z); Console.WriteLine("backfire emitter " + G3(x) + "," + G3(y) + "," + G3(z));
        for (int i = 0; i < 4; i++) { s.WheelEmitter(i, out x, out y, out z); Console.WriteLine("wheel " + i + " " + G3(x) + "," + G3(y) + "," + G3(z)); }
        Console.WriteLine("wheelbase " + s.WheelbaseM + " cg " + s.CgLocation + " cgH " + s.CgHeightM + " tracks " + s.TrackFrontM + "/" + s.TrackRearM + " baseY " + s.BaseYFront + "/" + s.BaseYRear + " idle " + s.IdleRpm + " mass " + s.TotalMassKg);
        return 0;
    }

    static int Cmd_sndcensus(List<string> a)
    {
        var content = AcContent.Scan(_install);
        var evCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var loopEv = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var multiEv = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var paramsUsed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int cars = 0, fails = 0, layerMismatch = 0, unresolved = 0, noBackfireIni = 0, positions = 0;
        double maxMb = 0, sumMbAll = 0, sumMbExt = 0; string maxCar = "";
        var posCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var car in content.Cars)
        {
            if (!car.Owned || car.BankPath == null) continue;
            cars++;
            try
            {
                FmodGuids g = Guids();
                if (car.GuidsPath != null) { g = FmodGuids.Load(_install.GuidsTxt); g.AddFile(car.GuidsPath); }
                var b = AcSndBank.Load(car.BankPath, g, "/cars/" + car.Id + "/");
                    b.ApplyMixer(Mixer());
                var d = FmodBank.Read(car.BankPath, g, "/cars/" + car.Id + "/");
                unresolved += b.Unresolved;
                foreach (var ev in b.Events)
                {
                    Inc(evCount, ev.Name);
                    if (ev.TimelineLoops) Inc(loopEv, ev.Name);
                    foreach (var ins in ev.Instruments) if (ins.Multi) { Inc(multiEv, ev.Name); break; }
                    foreach (var p in ev.Params) Inc(paramsUsed, ev.Name + ":" + p);
                }
                foreach (bool interior in new[] { true, false })
                {
                    var ev = b.Event(interior ? "engine_int" : "engine_ext");
                    var dl = d.EngineLayers(interior);
                    int singles = 0; if (ev != null) foreach (var ii in ev.Instruments) if (!ii.Multi) singles++;
                        if (ev == null || singles != dl.Count) { layerMismatch++; Console.WriteLine("  " + car.Id + " " + (interior ? "int" : "ext") + " layers A " + (ev == null ? -1 : ev.Instruments.Length) + " D " + dl.Count); continue; }
                    // gains must agree at a grid of rpm/throttle (with A's -42 dB floor)
                    var vals = ev.NewValues();
                    int ri = -1, ti = ev.ParamIndex("throttle");
                    for (int k = 0; k < ev.Params.Length; k++) if (ev.Params[k].IndexOf("rpm", StringComparison.OrdinalIgnoreCase) >= 0) ri = k;
                    int bad = 0;
                    for (float rpm = 500; rpm <= 12000; rpm += 750)
                        for (float th = 0; th <= 1.001f; th += 0.25f)
                        {
                            if (ri >= 0) vals[ri] = rpm; if (ti >= 0) vals[ti] = th;
                            float sa = 0, sd = 0;
                            for (int i = 0; i < ev.Instruments.Length; i++) { if (ConeAuto(ev, ev.Instruments[i])) continue; float gg, pp; ev.Evaluate(i, vals, out gg, out pp); sa += gg; }
                            foreach (var l in dl) { float mx = 1f; bool cone = false; foreach (var ii in ev.Instruments) if (ii.Id == l.Instrument.Id) { mx = Mix(ii); cone |= ConeAuto(ev, ii); } if (!cone) sd += DbFloor(l.Gain(rpm, th)) * mx; }
                            if (Math.Abs(sa - sd) > 0.02f + 0.02f * sd) bad++;
                        }
                    if (bad > 0) { layerMismatch++; Console.WriteLine("  " + car.Id + " " + (interior ? "int" : "ext") + " gain grid differs at " + bad + " points"); }
                }
                // PCM sizes: whole bank, exterior set
                double all = 0, ext = 0;
                var extEvents = new List<AcSndEvent>();
                foreach (var n in new[] { "engine_ext", "skid_ext", "gear_ext", "backfire_ext", "limiter", "horn", "door" }) { var e = b.Event(n); if (e != null) extEvents.Add(e); }
                foreach (int si in b.SamplesOf(null)) all += b.Fsb.Samples[si].Frames * b.Fsb.Samples[si].Channels * 4.0 / 1048576.0;
                foreach (int si in b.SamplesOf(extEvents)) ext += b.Fsb.Samples[si].Frames * b.Fsb.Samples[si].Channels * 4.0 / 1048576.0;
                sumMbAll += all; sumMbExt += ext;
                if (all > maxMb) { maxMb = all; maxCar = car.Id; }
                var cs = AcCarSounds.Load(car.Dir, car.Id);
                if (!cs.HasBackfire && b.Event("backfire_ext") != null) noBackfireIni++;
                Inc(posCount, cs.EnginePosition.Length > 0 ? cs.EnginePosition.ToLowerInvariant() : "(none)");
                if (cs.Problems.Count > 0) Console.WriteLine("  " + car.Id + " sounds: " + string.Join("; ", cs.Problems));
                positions++;
            }
            catch (Exception e) { fails++; Console.WriteLine("  FAIL " + car.Id + ": " + e.Message); }
        }
        Console.WriteLine("cars " + cars + ", failures " + fails + ", engine layer/gain mismatches A vs D (multi instruments and cone-angle layers excluded: D has neither) " + layerMismatch + ", unresolved placements " + unresolved);
        Console.WriteLine("events: " + string.Join(", ", evCount.Select(kv => kv.Key + " " + kv.Value)));
        Console.WriteLine("timeline-loop events: " + string.Join(", ", loopEv.Select(kv => kv.Key + " " + kv.Value)));
        Console.WriteLine("multi-instrument events: " + string.Join(", ", multiEv.Select(kv => kv.Key + " " + kv.Value)));
        Console.WriteLine("params: " + string.Join(", ", paramsUsed.Select(kv => kv.Key + " " + kv.Value)));
        Console.WriteLine("float PCM per car: avg all " + (sumMbAll / Math.Max(1, cars)).ToString("0.0", Iv) + " MB, avg exterior set " + (sumMbExt / Math.Max(1, cars)).ToString("0.0", Iv) + " MB, max " + maxMb.ToString("0.0", Iv) + " MB (" + maxCar + ")");
        Console.WriteLine("[ENGINE] POSITION: " + string.Join(", ", posCount.Select(kv => kv.Key + " " + kv.Value)) + "; backfire event without [BACKFIRE] " + noBackfireIni);
        return fails == 0 ? 0 : 1;
    }

    // D's Gain ignores the event cone angle; A feeds it - compare only layers without cone-angle automations
    static bool ConeAuto(AcSndEvent ev, AcSndInstrument ins) { foreach (var au in ins.Autos) if (ev.Params[au.Param] == "Event Cone Angle") return true; return false; }

    static void Inc(SortedDictionary<string, int> d, string k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }

    static int Cmd_render(List<string> a)
    {
        string car = a[1]; bool interior = a[2] == "int"; string outPath = a[3];
        var b = LoadSnd(car);
        var ev = b.Event(interior ? "engine_int" : "engine_ext");
        if (ev == null) { Console.WriteLine("no engine event"); return 1; }
        var cs = AcCarSounds.Load(CarPath(car), car);
        float idle = cs.IdleRpm > 0 ? cs.IdleRpm : 1000f, lim = cs.LimiterRpm > 0 ? cs.LimiterRpm : 7000f;
        int ri = -1, ti = ev.ParamIndex("throttle");
        for (int k = 0; k < ev.Params.Length; k++) if (ev.Params[k].IndexOf("rpm", StringComparison.OrdinalIgnoreCase) >= 0) ri = k;
        // schedule: 0-2 s idle, 2-10 s full throttle sweep idle->limiter, 10-11 s hold, 11-17 s lift (throttle 0) sweep down to idle, 17-19 idle
        Action<float, float[]> sched = (t, v) =>
        {
            float rpm, th;
            if (t < 2f) { rpm = idle; th = 0f; }
            else if (t < 10f) { rpm = idle + (lim - idle) * (t - 2f) / 8f; th = 1f; }
            else if (t < 11f) { rpm = lim; th = 1f; }
            else if (t < 17f) { rpm = lim - (lim - idle) * (t - 11f) / 6f; th = 0f; }
            else { rpm = idle; th = 0f; }
            if (ri >= 0) v[ri] = rpm; if (ti >= 0) v[ti] = th;
        };
        var mix = new AcOfflineMix(b) { ApplyEffects = !(a.Count > 4 && a[4] == "nofx") };
        { float dn; var dx = AcOfflineMix.MasterDistortion(ev, out dn); Console.WriteLine(dx != null ? "master distortion " + dx.P(0, 0) + (dx.Autos.Length > 0 ? " automated on " + ev.Params[dx.Autos[0].Param] + ": " + dx.Autos[0].Curve : "") + ", downstream " + AcOfflineMix.ToDb(dn).ToString("0.0", Iv) + " dB" + (mix.ApplyEffects ? "" : " (NOT applied)") : "no master distortion"); }
        var st = mix.Render(ev, 19f, sched);
        AcWav.WritePcm16(outPath, st, st.Length, 2, AcOfflineMix.OutRate);
        Console.WriteLine("wrote " + outPath + " (" + car + " " + (interior ? "engine_int" : "engine_ext") + ", idle " + idle + ", limiter " + lim + ")");
        for (int s = 0; s < 19; s++)
        {
            float rms, pk; AcOfflineMix.Levels(st, s, s + 1, out rms, out pk);
            var v = ev.NewValues(); sched(s + 0.5f, v);
            Console.WriteLine("  t " + s.ToString().PadLeft(2) + "-" + (s + 1) + " s rpm " + (ri >= 0 ? v[ri].ToString("0", Iv) : "?").PadLeft(5) + " thr " + (ti >= 0 ? v[ti].ToString("0", Iv) : "?") + "  rms " + AcOfflineMix.ToDb(rms).ToString("0.0", Iv).PadLeft(6) + " dBFS  peak " + AcOfflineMix.ToDb(pk).ToString("0.0", Iv) + " dBFS  A-weighted " + AcOfflineMix.ToDb(AcOfflineMix.RmsA(st, s, s + 1)).ToString("0.0", Iv) + " dB(A)FS");
        }
        return 0;
    }
}

static partial class Program
{
    // chunks <bank> [ids]: chunk order with offset, size and the first 24 bytes (to see which chunk owns which)
    static int Cmd_chunks(List<string> a)
    {
        string path = BankPath(a[1]);
        var want = a.Count > 2 ? new HashSet<string>(a[2].Split(',')) : null;
        using (var r = AcBinary.OpenFile(path, 1 << 16))
        {
            r.ReadAscii(4); r.ReadInt32(); r.ReadAscii(4);
            WalkPrint(r, 12, r.Length, 0, want);
        }
        return 0;
    }

    static void WalkPrint(AcBinary r, long p, long end, int depth, HashSet<string> want)
    {
        while (p + 8 <= end)
        {
            r.Position = p;
            string id = r.ReadAscii(4);
            long sz = r.ReadUInt32();
            if (p + 8 + sz > end) return;
            if (id == "LIST") { r.Position = p + 8; string t = r.ReadAscii(4); if (want == null) Console.WriteLine(new string(' ', depth * 2) + "LIST " + t + " @" + p); WalkPrint(r, p + 12, p + 8 + sz, depth + 1, want); }
            else if (id != "SND ")
            {
                if (want == null || want.Contains(id.Trim()))
                {
                    r.Position = p + 8;
                    var b = r.ReadBytes((int)Math.Min(sz, 32));
                    var sb = new StringBuilder();
                    foreach (var x in b) sb.Append(x.ToString("X2")).Append(' ');
                    string extra = "";
                    if (id == "BUS ") { float bv; extra = AcSndBank.TryBusVolume(ReadAt(r, p + 8, (int)sz), out bv) ? " VOL " + bv.ToString("0.###", Iv) : " vol ?"; }
                    if ((id == "GBSB" || id == "MBSB" || id == "IBSB" || id == "EVTB" || id == "WAIB" || id == "MUIB") && sz >= 16) { var g = new byte[16]; Array.Copy(b, g, 16); extra = " guid " + new Guid(g) + (Guids().PathOf(new Guid(g)) is string s ? " " + s : ""); }
                    Console.WriteLine(new string(' ', depth * 2) + id + " @" + p + " size " + sz + extra + "  " + sb);
                }
            }
            p += 8 + sz + (sz & 1);
        }
    }
}

static partial class Program
{
    static byte[] ReadAt(AcBinary r, long at, int n) { long keep = r.Position; r.Position = at; var b = r.ReadBytes(n); r.Position = keep; return b; }
}

static partial class Program
{
    // sndfx <car|common|all>: the DSP effects per event (spatialiser distances, filters, EQ, plugins, their automations)
    static int Cmd_sndfx(List<string> a)
    {
        if (a[1] != "all")
        {
            var b = LoadSnd(a[1]);
            foreach (var ev in b.Events)
            {
                if (ev.Effects.Length == 0) continue;
                Console.WriteLine("EVENT " + ev.Name + (ev.Spatialiser != null ? " 3D min " + G3(ev.MinDistance(0)) + " max " + G3(ev.MaxDistance(0)) + " rolloff " + ev.Rolloff + " blend " + G3(ev.Spatialiser.P(17, 0)) : " 2D"));
                foreach (var fx in ev.Effects) Console.WriteLine("   " + FxLine(ev, fx));
            }
            return 0;
        }
        var content = AcContent.Scan(_install);
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var autoLines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int cars = 0;
        foreach (var car in content.Cars)
        {
            if (!car.Owned || car.BankPath == null) continue;
            cars++;
            FmodGuids g = Guids();
            if (car.GuidsPath != null) { g = FmodGuids.Load(_install.GuidsTxt); g.AddFile(car.GuidsPath); }
            AcSndBank b;
            try { b = AcSndBank.Load(car.BankPath, g, "/cars/" + car.Id + "/"); } catch (Exception e) { Console.WriteLine("FAIL " + car.Id + " " + e.Message); continue; }
            foreach (var ev in b.Events)
            {
                if (ev.Spatialiser == null) Inc(kinds, ev.Name + ": 2D (no spatialiser)");
                else Inc(kinds, ev.Name + ": spatialiser " + G3(ev.MinDistance(0)) + "/" + G3(ev.MaxDistance(0)) + " r" + ev.Rolloff);
                foreach (var fx in ev.Effects)
                {
                    if (fx.Dsp == AcSndFx.DspPan) continue;
                    Inc(kinds, ev.Name + ": " + fx.Kind + (fx.OnMaster ? " master" : " group") + (fx.PostFader ? " post" : " pre"));
                    foreach (var au in fx.Autos) Inc(autoLines, ev.Name + " " + fx.Kind + " p" + au.Prop + " on " + ev.Params[au.Param] + ": " + au.Curve);
                }
            }
        }
        Console.WriteLine("cars " + cars);
        foreach (var kv in kinds) Console.WriteLine("  " + kv.Value.ToString().PadLeft(3) + " " + kv.Key);
        Console.WriteLine("automations:");
        foreach (var kv in autoLines) Console.WriteLine("  " + kv.Value.ToString().PadLeft(3) + " " + kv.Key);
        return 0;
    }

    static string FxLine(AcSndEvent ev, AcSndFx fx)
    {
        var sb = new StringBuilder(fx.Kind.PadRight(16));
        sb.Append(fx.OnMaster ? " master" : " group[" + fx.Instruments.Length + ": " + ev.Instruments[fx.Instruments[0]].Label + "..]").Append(fx.PostFader ? " post" : " pre");
        sb.Append(" params");
        for (int i = 0; i < fx.Params.Length; i++) sb.Append(' ').Append(float.IsNaN(fx.Params[i]) ? "-" : G3(fx.Params[i]));
        foreach (var au in fx.Autos) sb.Append(" | p").Append(au.Prop).Append(" on ").Append(ev.Params[au.Param]).Append(": ").Append(au.Curve);
        return sb.ToString();
    }
}
