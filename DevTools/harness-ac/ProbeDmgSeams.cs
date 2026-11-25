using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// Damage research probe (seams topic, lead-architect view). Read-only.
//   dmgsnd            census over common.bank + every owned car bank: events / instruments driven by damage-ish parameters
//                     (inflation, suspension_damage) or with damage-ish sample names (flutter, flat, explosion, glass, crash, wrench, screw)
//   dmgart [out.png]  composite of content/texture/damage/*.png (the HUD damage displayer layers), each tinted, on dark grey + bbox per layer
static partial class Program
{
    static int Cmd_dmgsnd(List<string> a)
    {
        var words = new[] { "flutter", "flat", "explo", "glass", "crash", "wrench", "screw", "damage", "blow", "puncture", "hit", "scrape", "debris" };
        var counts = new SortedDictionary<string, int>();
        int cars = 0;
        var banks = new List<KeyValuePair<string, string>>();
        banks.Add(new KeyValuePair<string, string>("common", _install.CommonBank));
        foreach (var car in AcContent.Scan(_install).Cars) if (car.Owned && car.BankPath != null) banks.Add(new KeyValuePair<string, string>(car.Id, car.BankPath));
        foreach (var kv in banks)
        {
            AcSndBank b;
            try
            {
                FmodGuids g = FmodGuids.Load(_install.GuidsTxt);
                b = AcSndBank.Load(kv.Value, g, kv.Key == "common" ? null : "/cars/" + kv.Key + "/");
            }
            catch (Exception e) { Console.WriteLine("FAIL " + kv.Key + " " + e.Message); continue; }
            if (kv.Key != "common") cars++;
            foreach (var ev in b.Events)
            {
                for (int i = 0; i < ev.Instruments.Length; i++)
                {
                    var ins = ev.Instruments[i];
                    string pn = ins.RegionParam >= 0 && ins.RegionParam < ev.Params.Length ? ev.Params[ins.RegionParam] : "";
                    bool dmgParam = pn == "inflation" || pn == "suspension_damage";
                    bool dmgName = false;
                    foreach (var s in ins.SampleNames) { string l = s.ToLowerInvariant(); foreach (var w in words) if (l.Contains(w)) dmgName = true; }
                    if (!dmgParam && !dmgName) continue;
                    string key = ev.Path + " | " + ins.Label + (ins.RegionParam >= 0 ? " | " + pn + " " + ins.RegionStart.ToString("0.###") + ".." + ins.RegionEnd.ToString("0.###") : " | timeline")
                                 + (ins.Loops ? " loop" : " oneshot");
                    if (kv.Key == "common") key = "[common] " + key; else key = "[car] " + key.Replace("/cars/" + kv.Key + "/", "/cars/*/");
                    int c; counts.TryGetValue(key, out c); counts[key] = c + 1;
                }
                // automations driven by damage params (volume curves)
                foreach (var p in ev.Params)
                    if (p == "inflation" || p == "suspension_damage")
                    {
                        string key = (kv.Key == "common" ? "[common] " : "[car] ") + ev.Path.Replace("/cars/" + kv.Key + "/", "/cars/*/") + " has param " + p;
                        int c; counts.TryGetValue(key, out c); counts[key] = c + 1;
                    }
            }
        }
        Console.WriteLine("banks: common + " + cars + " owned car banks");
        foreach (var kv in counts) Console.WriteLine(kv.Value.ToString().PadLeft(4) + "  " + kv.Key);
        return 0;
    }

    static int Cmd_dmgart(List<string> a)
    {
        string dir = Path.Combine(_install.Root, "content", "texture", "damage");
        string outPath = a.Count > 1 ? a[1] : "dmgart.png";
        int W = 512, H = 512;
        var o = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { o[i * 4] = 40; o[i * 4 + 1] = 40; o[i * 4 + 2] = 40; o[i * 4 + 3] = 255; }
        var tint = new Dictionary<string, int[]>
        {
            { "front", new[] { 255, 60, 60 } }, { "rear", new[] { 60, 255, 60 } }, { "left", new[] { 80, 160, 255 } }, { "right", new[] { 255, 255, 60 } },
            { "center", new[] { 255, 80, 255 } }, { "engine", new[] { 255, 150, 0 } }, { "gearbox", new[] { 0, 255, 255 } },
        };
        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            int w, h;
            byte[] px = PngDecode(File.ReadAllBytes(f), out w, out h);
            if (px == null) { Console.WriteLine(Path.GetFileName(f) + " undecodable"); continue; }
            string k = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            int[] c; if (!tint.TryGetValue(k, out c)) c = k.StartsWith("tyre") ? new[] { 255, 255, 255 } : k.StartsWith("flat") ? new[] { 255, 0, 0 } : new[] { 170, 170, 170 };
            int x0 = w, y0 = h, x1 = -1, y1 = -1; long aSum = 0; int maxA = 0; int rgbMin = 255;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = (y * w + x) * 4, al = px[s + 3];
                    if (al == 0) continue;
                    if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y;
                    aSum += al; if (al > maxA) maxA = al; rgbMin = Math.Min(rgbMin, Math.Min(px[s], Math.Min(px[s + 1], px[s + 2])));
                    if (x >= W || y >= H) continue;
                    int d = (y * W + x) * 4;
                    for (int ch = 0; ch < 3; ch++) o[d + ch] = (byte)((o[d + ch] * (255 - al) + c[ch] * al) / 255);
                }
            Console.WriteLine(Path.GetFileName(f).PadRight(18) + w + "x" + h + " bbox " + x0 + "," + y0 + " .. " + x1 + "," + y1 + " alpha max " + maxA + " sum " + aSum + " rgb min " + rgbMin);
        }
        Png.Write(outPath, W, H, o);
        Console.WriteLine("wrote " + outPath);
        return 0;
    }
}
