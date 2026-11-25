using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// [H] iteration 2 probe (DAMAGE_DESIGN 10):
//   dmghud [out.png]  the 18 acs.exe damage-displayer paths exist and decode (14 drawn + 4 flatspots not drawn), center.png not in the list,
//                     the crop (AcHudArtList.Crop*) holds every layer's ink, the damage rules the HUD reads (AcDamageRules), and a composite
//                     of the drawn layers with the HUD's tint ramp (white 0 -> yellow 0.5 -> red 1, alpha 0.85) in four panels:
//                     all 0 | all 0.5 | all 1 | the lab's fake damage (front 0, rear 30, left 60, right 120 km/h, susp 0/0.5/1/0, engine 50 %).
static partial class Program
{
    static int Cmd_dmghud(List<string> a)
    {
        string outPath = a.Count > 1 ? a[1] : "dmghud.png";
        int fails = 0;
        var all = new List<string>(AcHudArtList.DamageFiles);
        all.AddRange(AcHudArtList.DamageFlatspots);
        Console.WriteLine("damage displayer paths: " + all.Count + " (" + AcHudArtList.DamageFiles.Length + " drawn, " + AcHudArtList.DamageFlatspots.Length + " flatspots not drawn)");
        foreach (string s in AcHudArtList.All) if (s.EndsWith("center.png", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("FAIL center.png is loaded"); fails++; }
        int cx0 = (int)AcHudArtList.CropX, cw = (int)AcHudArtList.CropW, ch = (int)AcHudArtList.CropH;
        int cyTop = (int)(AcHudArtList.CropOf - AcHudArtList.CropY - AcHudArtList.CropH);   // crop rows top-down
        var px = new Dictionary<string, byte[]>();
        foreach (string rel in all)
        {
            string p = Path.Combine(_install.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) { Console.WriteLine("FAIL missing " + rel); fails++; continue; }
            int w, h;
            byte[] img = PngDecode(File.ReadAllBytes(p), out w, out h);
            if (img == null) { Console.WriteLine("FAIL undecodable " + rel); fails++; continue; }
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (img[(y * w + x) * 4 + 3] > 0) { if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
            bool inCrop = w == 512 && h == 512 && x0 >= cx0 && x1 < cx0 + cw && y0 >= cyTop && y1 < cyTop + ch;
            Console.WriteLine("  " + rel.PadRight(42) + w + "x" + h + " ink " + x0 + "," + y0 + ".." + x1 + "," + y1 + (inCrop ? " in crop" : " OUTSIDE CROP"));
            if (!inCrop) fails++;
            if (w == 512 && h == 512) px[rel] = img;
        }
        AcDamageRules dr = AcDamageRules.Load(_install);
        Console.WriteLine("rules (" + dr.Source + "): TIME " + dr.DisplayTimeS + " MAX_DAMAGE_KMH " + dr.MaxDamageKmh + " POSITION_X " + dr.PosX + " DISTANCE_FROM_CENTER_Y " + dr.CenterY
                          + " PRINT_VALUES " + dr.PrintValues + " IS_ACTIVE " + dr.DisplayerDefaultOn + " DAMAGE_MULTIPLIER " + dr.DefaultMultiplier);

        // composite: 4 panels of the crop
        int W = cw * 4, H = ch;
        var o = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { o[i * 4] = 30; o[i * 4 + 1] = 34; o[i * 4 + 2] = 40; o[i * 4 + 3] = 255; }
        float max = dr.MaxDamageKmh;
        for (int panel = 0; panel < 4; panel++)
        {
            foreach (string rel in AcHudArtList.DamageFiles)
            {
                byte[] img;
                if (!px.TryGetValue(rel, out img)) continue;
                float v = panel == 0 ? 0f : panel == 1 ? 0.5f : panel == 2 ? 1f : Fake(rel, max);
                if (rel.Contains("gearbox") || rel.Contains("tyre_")) v = 0f;     // not simulated: drawn clean
                float r, g, b; Tint(v, out r, out g, out b);
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        int s = ((cyTop + y) * 512 + cx0 + x) * 4;
                        float al = img[s + 3] / 255f * 0.85f;
                        if (al <= 0f) continue;
                        int d = (y * W + panel * cw + x) * 4;
                        o[d] = (byte)(o[d] * (1f - al) + img[s] * r * al);
                        o[d + 1] = (byte)(o[d + 1] * (1f - al) + img[s + 1] * g * al);
                        o[d + 2] = (byte)(o[d + 2] * (1f - al) + img[s + 2] * b * al);
                    }
            }
        }
        Png.Write(outPath, W, H, o);
        Console.WriteLine("wrote " + outPath + " (" + W + "x" + H + ": all 0 | all 0.5 | all 1 | lab fake)");
        Console.WriteLine(fails == 0 ? "dmghud PASS" : "dmghud FAIL " + fails);
        return fails == 0 ? 0 : 1;
    }

    static float Fake(string rel, float max)
    {
        if (rel.EndsWith("/front.png")) return 0f;
        if (rel.EndsWith("/rear.png")) return Math.Min(1f, 30f / max);
        if (rel.EndsWith("/left.png")) return Math.Min(1f, 60f / max);
        if (rel.EndsWith("/right.png")) return Math.Min(1f, 120f / max);
        if (rel.EndsWith("/engine.png")) return 0.5f;
        if (rel.EndsWith("sus_fr.png")) return 0.5f;
        if (rel.EndsWith("sus_rl.png")) return 1f;
        return 0f;
    }

    /// <summary>The HUD's ramp (AcHudView.DamageTint): white -> yellow at 0.5 -> red at 1.</summary>
    static void Tint(float v, out float r, out float g, out float b)
    {
        v = Math.Max(0f, Math.Min(1f, v));
        if (v < 0.5f) { r = 1f; g = 1f; b = 1f - v * 2f; }
        else { r = 1f; g = 1f - (v - 0.5f) * 2f; b = 0f; }
    }
}
