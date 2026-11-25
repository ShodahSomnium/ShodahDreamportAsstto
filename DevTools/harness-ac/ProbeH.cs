using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SomniumSpace.Worlds.SomniumAssetto;

// Module H (HUD) probes. Run: dotnet run -c Release --no-build -- <cmd>
//   bmfonts                         every content/fonts/*.txt: glyph count, image size, clamp, boundary alignment score
//   bmfont <font> [text] [out.png]  renders a sample string with the bitmap font (look at it: glyphs in the right order)
//   hudart [out.png]                checks every HUD image AcHudArt loads, composes them on a dark sheet
//   hudstrings                      the system/locales/en.ini strings the HUD uses
//   hudfmt                          AcHudFormat self-test (lap times, deltas, speeds) + allocation count
//   bmtables <font>                 several tables in one txt (digital_toyota): clean-cut score per table
//   arc <png> / redcols <png>       ring arc extent (rpm_on, turbo_on) / ruler red zone (rpmBar, fuelBar)
//   hudmock [out.png] [rpm] [vr]    1920x1080 (or VR 1400x760) MOCK of the HUD layout with the install art + bitmap font
static partial class Program
{
    // ---------------------------------------------------------------- PNG decoder (harness only; Unity uses ImageConversion)
    static byte[] PngDecode(byte[] b, out int w, out int h)
    {
        w = h = 0;
        if (b.Length < 8 || b[0] != 0x89 || b[1] != 'P') return null;
        int p = 8, bitDepth = 0, colorType = 0, interlace = 0;
        var idat = new MemoryStream();
        byte[] palette = null, trns = null;
        while (p + 8 <= b.Length)
        {
            int len = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
            string type = Encoding.ASCII.GetString(b, p + 4, 4);
            int d = p + 8;
            if (type == "IHDR")
            {
                w = (b[d] << 24) | (b[d + 1] << 16) | (b[d + 2] << 8) | b[d + 3];
                h = (b[d + 4] << 24) | (b[d + 5] << 16) | (b[d + 6] << 8) | b[d + 7];
                bitDepth = b[d + 8]; colorType = b[d + 9]; interlace = b[d + 12];
            }
            else if (type == "PLTE") { palette = new byte[len]; Array.Copy(b, d, palette, 0, len); }
            else if (type == "tRNS") { trns = new byte[len]; Array.Copy(b, d, trns, 0, len); }
            else if (type == "IDAT") idat.Write(b, d, len);
            else if (type == "IEND") break;
            p = d + len + 4;
        }
        if (bitDepth != 8 || interlace != 0) { Console.WriteLine("  png: unsupported depth " + bitDepth + " interlace " + interlace); return null; }
        int ch = colorType == 6 ? 4 : colorType == 2 ? 3 : colorType == 4 ? 2 : 1;
        idat.Position = 0;
        byte[] raw;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) using (var ms = new MemoryStream()) { z.CopyTo(ms); raw = ms.ToArray(); }
        int stride = w * ch;
        var cur = new byte[stride]; var prev = new byte[stride];
        var rgba = new byte[w * h * 4];
        int r = 0;
        for (int y = 0; y < h; y++)
        {
            int f = raw[r++];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= ch ? cur[x - ch] : 0, up = prev[x], c = x >= ch ? prev[x - ch] : 0, v = raw[r++];
                switch (f)
                {
                    case 1: v += a; break;
                    case 2: v += up; break;
                    case 3: v += (a + up) >> 1; break;
                    case 4: { int pp = a + up - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - up), pc = Math.Abs(pp - c); v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? up : c); break; }
                }
                cur[x] = (byte)v;
            }
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                switch (colorType)
                {
                    case 6: rgba[o] = cur[x * 4]; rgba[o + 1] = cur[x * 4 + 1]; rgba[o + 2] = cur[x * 4 + 2]; rgba[o + 3] = cur[x * 4 + 3]; break;
                    case 2: rgba[o] = cur[x * 3]; rgba[o + 1] = cur[x * 3 + 1]; rgba[o + 2] = cur[x * 3 + 2]; rgba[o + 3] = 255; break;
                    case 4: rgba[o] = rgba[o + 1] = rgba[o + 2] = cur[x * 2]; rgba[o + 3] = cur[x * 2 + 1]; break;
                    case 0: rgba[o] = rgba[o + 1] = rgba[o + 2] = cur[x]; rgba[o + 3] = 255; break;
                    case 3:
                    {
                        int i = cur[x];
                        rgba[o] = palette[i * 3]; rgba[o + 1] = palette[i * 3 + 1]; rgba[o + 2] = palette[i * 3 + 2];
                        rgba[o + 3] = trns != null && i < trns.Length ? trns[i] : (byte)255; break;
                    }
                }
            }
            var t = prev; prev = cur; cur = t;
        }
        return rgba;
    }

    /// <summary>Decoded font image as AcHudArt / C would see it: RGBA top-down; RGB (no alpha) PNG fonts get alpha = brightest channel
    /// (they are white glyphs on black, like the .bmp ones).</summary>
    static byte[] FontImage(AcBitmapFont f, out int w, out int h, out bool opaque)
    {
        byte[] bytes = File.ReadAllBytes(f.ImagePath);
        string problem;
        byte[] rgba = f.ImageIsBmp ? AcBitmapFont.DecodeBmp(bytes, out w, out h, out problem) : PngDecode(bytes, out w, out h);
        opaque = false;
        if (rgba == null) return null;
        opaque = AcBitmapFont.LuminanceToAlpha(rgba);
        f.MeasureInk(rgba, w, h);
        return rgba;
    }

    static float[] ColumnAlpha(byte[] rgba, int w, int h)
    {
        var col = new float[w];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) col[x] += rgba[(y * w + x) * 4 + 3] / 255f;
        return col;
    }

    // Alignment score: at each inner boundary the ink on the column there should be (near) zero (glyphs do not touch the cut) while
    // the glyph spans carry ink. Returns the fraction of boundaries whose column (+-1) holds < 15 % of a mean glyph column.
    static float BoundaryScore(AcBitmapFont f, float[] col, int w, float scale, out float inkMean)
    {
        double ink = 0; int inkN = 0;
        for (int x = 0; x < w; x++) if (col[x] > 0.01f) { ink += col[x]; inkN++; }
        inkMean = inkN > 0 ? (float)(ink / inkN) : 0f;
        int good = 0, n = 0;
        for (int k = 1; k < f.Count; k++)
        {
            float u = f.Boundary(k) * scale;
            if (u >= 1f) break;
            int x = (int)Math.Round(u * w);
            float m = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++) { int xx = x + dx; if (xx >= 0 && xx < w) m = Math.Min(m, col[xx]); }
            n++;
            if (m < 0.15f * inkMean) good++;
        }
        return n > 0 ? (float)good / n : 0f;
    }

    static int Cmd_bmfonts(List<string> a)
    {
        string dir = _install.FontsDir;
        int ok = 0, bad = 0;
        foreach (string txt in Directory.GetFiles(dir, "*.txt"))
        {
            string name = Path.GetFileNameWithoutExtension(txt);
            string problem;
            var f = AcBitmapFont.Load(dir, name, out problem);
            if (f == null) { Console.WriteLine("  " + name + ": FAIL " + problem); bad++; continue; }
            int w, h; bool opaque;
            byte[] img = FontImage(f, out w, out h, out opaque);
            string score = "-";
            if (img != null)
            {
                float[] col = ColumnAlpha(img, w, h);
                float inkMean;
                float s1 = BoundaryScore(f, col, w, 1f, out inkMean);
                float s2 = f.MaxRawU > 1f ? BoundaryScore(f, col, w, 1f / f.MaxRawU, out inkMean) : s1;
                score = "clean cuts " + (s1 * 100f).ToString("0") + "%" + (f.MaxRawU > 1f ? " (if rescaled by 1/maxU: " + (s2 * 100f).ToString("0") + "%)" : "") + (opaque ? " [opaque->alpha=lum]" : "");
                // missing-glyph check: digits must have ink
                int emptyDigits = 0;
                for (char c = '0'; c <= '9'; c++)
                {
                    int x0, x1; if (!f.GlyphPixels(c, out x0, out x1)) { emptyDigits++; continue; }
                    float s = 0; for (int x = x0; x < x1 && x < w; x++) s += col[x];
                    if (s < 1f) emptyDigits++;
                }
                if (emptyDigits > 0) score += " EMPTY DIGITS " + emptyDigits;
            }
            Console.WriteLine("  " + f + "  " + score);
            ok++;
        }
        Console.WriteLine("bmfonts: " + ok + " parsed, " + bad + " failed");
        return bad == 0 ? 0 : 1;
    }

    static int Cmd_bmfont(List<string> a)
    {
        if (a.Count < 2) { Console.WriteLine("bmfont <font> [text] [out.png]"); return 1; }
        string problem;
        var f = AcBitmapFont.Load(_install.FontsDir, a[1], out problem);
        if (f == null) { Console.WriteLine("FAIL " + problem); return 1; }
        string text = a.Count > 2 ? a[2] : "0123456789 1:23.456 -0.42 +1.07 ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz !?%/()[]";
        string outPath = a.Count > 3 ? a[3] : Path.Combine(Path.GetTempPath(), "bmfont_" + f.Name + ".png");
        int w, h; bool opaque;
        byte[] img = FontImage(f, out w, out h, out opaque);
        if (img == null) { Console.WriteLine("image decode failed"); return 1; }
        Console.WriteLine(f + (opaque ? " (opaque image: alpha = brightest channel)" : ""));
        int lineH = Math.Max(32, Math.Min(96, h));   // draw height in pixels
        char[] chars = text.ToCharArray();
        var quads = new float[chars.Length * 8];
        float totalW = f.Measure(chars, 0, chars.Length) * lineH + 4 * chars.Length;
        int outW = Math.Min(4000, (int)totalW + 40), outH = lineH * 2 + 40;
        var o = new byte[outW * outH * 4];
        for (int i = 0; i < o.Length; i += 4) { o[i] = 30; o[i + 1] = 34; o[i + 2] = 40; o[i + 3] = 255; }
        int nq = f.Layout(chars, 0, chars.Length, 20f, 20f, lineH, 1f, 0.03f, quads);
        // draw each quad: sample the font image (nearest) and blend white * alpha; alternate a faint box per glyph to show the spans
        for (int q = 0; q < nq; q++)
        {
            float x0 = quads[q * 8], y0 = quads[q * 8 + 1], x1 = quads[q * 8 + 2], y1 = quads[q * 8 + 3], u0 = quads[q * 8 + 4], u1 = quads[q * 8 + 5];
            for (int py = (int)y0; py < (int)y1; py++)
                for (int px = (int)x0; px < (int)x1 && px < outW; px++)
                {
                    float u = u0 + (u1 - u0) * ((px - x0 + 0.5f) / Math.Max(1f, x1 - x0));
                    float vv1 = quads[q * 8 + 7], vv0 = quads[q * 8 + 6]; float vu = vv1 - (vv1 - vv0) * ((py - y0 + 0.5f) / (y1 - y0)); float v = 1f - vu;   // Unity v -> top-down image row fraction
                    int sx = Math.Min(w - 1, (int)(u * w)), sy = Math.Min(h - 1, (int)(v * h));
                    int si = (sy * w + sx) * 4;
                    float al = img[si + 3] / 255f;
                    int oi = (py * outW + px) * 4;
                    byte tint = (byte)(q % 2 == 0 ? 46 : 38);
                    float br = img[si], bg = img[si + 1], bb = img[si + 2];
                    o[oi] = (byte)(tint * (1 - al) + br * al); o[oi + 1] = (byte)(tint * (1 - al) + bg * al); o[oi + 2] = (byte)((tint + 8) * (1 - al) + bb * al);
                }
        }
        // second row: the raw image strip (first 4000 px), to compare
        int rowY = lineH + 30;
        for (int py = 0; py < Math.Min(lineH, outH - rowY); py++)
            for (int px = 0; px < outW - 20; px++)
            {
                int sx = (int)((long)px * h / lineH); if (sx >= w) break;
                int sy = Math.Min(h - 1, py * h / lineH);
                int si = (sy * w + sx) * 4; float al = img[si + 3] / 255f;
                int oi = ((rowY + py) * outW + px + 20) * 4;
                o[oi] = (byte)(30 * (1 - al) + 255 * al); o[oi + 1] = (byte)(34 * (1 - al) + 255 * al); o[oi + 2] = (byte)(40 * (1 - al) + 255 * al);
            }
        Png.Write(outPath, outW, outH, o);
        Console.WriteLine("wrote " + outPath + " (" + outW + "x" + outH + ", " + nq + " glyph quads for \"" + text + "\")");
        return 0;
    }
}

static partial class Program
{
    // bmtables <font>: a txt with several tables back to back (digital_toyota): clean-cut score of each table and of shifted starts.
    static int Cmd_bmtables(List<string> a)
    {
        string dir = _install.FontsDir, name = a[1];
        string problem;
        var f0 = AcBitmapFont.Load(dir, name, out problem);
        int w, h; bool opaque;
        byte[] img = FontImage(f0, out w, out h, out opaque);
        float[] col = ColumnAlpha(img, w, h);
        string[] lines = File.ReadAllText(Path.Combine(dir, name + ".txt")).Split(new[] { '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var tables = new List<List<string>>();
        foreach (string s in lines)
        {
            float v = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            if (tables.Count == 0 || (v == 0f && tables[tables.Count - 1].Count > 0)) tables.Add(new List<string>());
            tables[tables.Count - 1].Add(s);
        }
        for (int t = 0; t < tables.Count; t++)
        {
            var ft = AcBitmapFont.Parse(name, string.Join("\n", tables[t]), w, h, out problem);
            float ink; float sc = BoundaryScore(ft, col, w, 1f, out ink);
            Console.WriteLine("  table " + t + ": " + tables[t].Count + " values, last " + tables[t][tables[t].Count - 1] + ", clean cuts " + (sc * 100).ToString("0") + "%");
        }
        // where is the ink? first/last inked column
        int first = -1, last = -1;
        for (int x = 0; x < w; x++) if (col[x] > 0.5f) { if (first < 0) first = x; last = x; }
        Console.WriteLine("  ink from x " + first + " to " + last + " (u " + ((float)first / w).ToString("0.000") + " .. " + ((float)last / w).ToString("0.000") + ")");
        return 0;
    }
}

static partial class Program
{
    // hudart [out.png]: every image AcHudArt loads (AcHudArtList), decoded and composed on a dark sheet (checker under alpha) at 2x.
    static int Cmd_hudart(List<string> a)
    {
        string outPath = a.Count > 1 ? a[1] : Path.Combine(Path.GetTempPath(), "hudart.png");
        int sheetW = 1800, x = 10, y = 10, rowH = 0, missing = 0, n = 0;
        var placed = new List<(byte[] img, int w, int h, int x, int y, int s)>();
        foreach (string rel in AcHudArtList.Files)
        {
            string p = Path.Combine(_install.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) { Console.WriteLine("  MISSING " + rel); missing++; continue; }
            int w, h; byte[] img = PngDecode(File.ReadAllBytes(p), out w, out h);
            if (img == null) { Console.WriteLine("  UNDECODABLE " + rel); missing++; continue; }
            int s = Math.Max(w, h) > 300 ? 1 : 2;
            if (Math.Max(w, h) > 600) { s = 1; }
            int dw = Math.Min(w * s, 700), dh = h * s * dw / Math.Max(1, w * s);
            if (x + dw + 10 > sheetW) { x = 10; y += rowH + 24; rowH = 0; }
            placed.Add((img, w, h, x, y, s));
            long sumA = 0; for (int i = 3; i < img.Length; i += 4) sumA += img[i];
            Console.WriteLine("  " + rel + "  " + w + "x" + h + "  mean alpha " + (sumA / (double)(w * h) / 255.0).ToString("0.00") + "  at " + x + "," + y);
            x += dw + 14; rowH = Math.Max(rowH, dh); n++;
        }
        int sheetH = y + rowH + 20;
        var o = new byte[sheetW * sheetH * 4];
        for (int py = 0; py < sheetH; py++) for (int px = 0; px < sheetW; px++)
        {
            int oi = (py * sheetW + px) * 4; byte c = (byte)((((px >> 3) + (py >> 3)) & 1) == 0 ? 52 : 70);
            o[oi] = c; o[oi + 1] = c; o[oi + 2] = (byte)(c + 10); o[oi + 3] = 255;
        }
        foreach (var e in placed)
        {
            int dw = Math.Min(e.w * e.s, 700), dh = e.h * e.s * dw / Math.Max(1, e.w * e.s);
            for (int py = 0; py < dh && e.y + py < sheetH; py++) for (int px = 0; px < dw; px++)
            {
                int sx = Math.Min(e.w - 1, px * e.w / dw), sy = Math.Min(e.h - 1, py * e.h / dh);
                int si = (sy * e.w + sx) * 4; float al = e.img[si + 3] / 255f;
                int oi = ((e.y + py) * sheetW + e.x + px) * 4;
                for (int k = 0; k < 3; k++) o[oi + k] = (byte)(o[oi + k] * (1 - al) + e.img[si + k] * al);
            }
        }
        Png.Write(outPath, sheetW, sheetH, o);
        Console.WriteLine("hudart: " + n + " images, " + missing + " missing; wrote " + outPath);
        return missing == 0 ? 0 : 1;
    }
}

static partial class Program
{
    // arc <png>: angular extent of the ink of a ring image (rpm_on/turbo_on): angles measured clockwise from straight DOWN (Unity
    // Image.Radial360 fillOrigin Bottom, clockwise), so the HUD maps a value onto exactly the drawn arc. Also inner/outer radius.
    static int Cmd_arc(List<string> a)
    {
        string p = Path.Combine(_install.Root, a[1].Replace('/', Path.DirectorySeparatorChar));
        int w, h; byte[] img = PngDecode(File.ReadAllBytes(p), out w, out h);
        var hist = new float[360]; float rMin = 1e9f, rMax = 0; var red = new float[360];
        double cx = w / 2.0, cy = h / 2.0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4; if (img[i + 3] < 128) continue;
            double dx = x + 0.5 - cx, dy = y + 0.5 - cy;   // image rows go down
            double ang = Math.Atan2(-dx, dy) * 180 / Math.PI; // 0 = down, positive = towards the left (clockwise on screen: down->left->up)
            if (ang < 0) ang += 360;
            hist[(int)ang % 360] += 1; float r = (float)Math.Sqrt(dx * dx + dy * dy); rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            if (img[i] > 150 && img[i + 1] < 80) red[(int)ang % 360] += 1;
        }
        int first = -1, last = -1, redFirst = -1;
        for (int d = 0; d < 360; d++) if (hist[d] > 2) { if (first < 0) first = d; last = d; }
        for (int d = 0; d < 360; d++) if (red[d] > 2) { redFirst = d; break; }
        Console.WriteLine(a[1] + " " + w + "x" + h + ": ink from " + first + " to " + last + " deg (clockwise from down), red from " + redFirst + " deg, radius " + rMin.ToString("0.0") + ".." + rMax.ToString("0.0"));
        return 0;
    }
}

static partial class Program
{
    // hudstrings: AcHudConfig as the HUD sees it (gameplay.ini + user cfg, session_info, skin, locale strings the HUD uses)
    static int Cmd_hudstrings(List<string> a)
    {
        var c = AcHudConfig.Load(_install.Root, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Console.WriteLine("read: " + string.Join(" | ", c.Read));
        Console.WriteLine("USE_MPH " + c.UseMph + ", TIME_DIFFERENCE " + c.TimeDifference + " POS_Y " + c.TimeDifferencePosY + ", SHOW_SECTORS " + c.ShowSectors + ", OVERLAY_LEADERBOARD " + c.OverlayLeaderboard);
        Console.WriteLine("session back " + string.Join(",", c.SessionBack) + " fore " + string.Join(",", c.SessionFore) + "; form back " + string.Join(",", c.FormBack) + " title " + string.Join(",", c.TitleBack));
        Console.WriteLine("session box visible practice/qualify/race " + c.PracticeVisible + "/" + c.QualifyVisible + "/" + c.RaceVisible + " (hotlap -> " + c.SessionVisible(4) + "); messages.ini joined " + c.MsgPlayerJoined + " left " + c.MsgPlayerLeft + "; standings default " + c.LeaderboardDefault);
        Console.WriteLine("language " + c.Language + ", " + c.StringCount + " strings");
        foreach (string k in new[] { "PRACTICE", "QUALIFY", "RACE", "LAP", "LAPS", "BEST", "LAST", "GAP", "POS.", "DELTA", "FINISH", "SESSION", "TOTAL", "Session Over", "Hotlap", "HOTLAP", "LAPTIME", "GEAR", "SPEED", "FUEL" })
            Console.WriteLine("  " + k + " = " + c.S(k, "<missing>"));
        return 0;
    }

    // hudfmt: AcHudFormat cases
    static int Cmd_hudfmt(List<string> a)
    {
        var b = new char[40]; int bad = 0;
        void Check(string what, int n, string want) { string got = new string(b, 0, n); bool ok = got == want; if (!ok) bad++; Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what + " -> \"" + got + "\"" + (ok ? "" : " want \"" + want + "\"")); }
        Check("LapTime(83456)", AcHudFormat.LapTime(b, 0, 83456), "1:23.456");
        Check("LapTime(5009)", AcHudFormat.LapTime(b, 0, 5009), "0:05.009");
        Check("LapTime(0)", AcHudFormat.LapTime(b, 0, 0), "-:--.---");
        Check("LapTime(3723004)", AcHudFormat.LapTime(b, 0, 3723004), "1:02:03.004");
        Check("Delta(-234,2)", AcHudFormat.Delta(b, 0, -234, 2), "-0.23");
        Check("Delta(1055,2)", AcHudFormat.Delta(b, 0, 1055, 2), "+1.06");
        Check("Delta(0,3)", AcHudFormat.Delta(b, 0, 0, 3), "+0.000");
        Check("Delta(-5,1)", AcHudFormat.Delta(b, 0, -5, 1), "-0.0");
        Check("Gap(12345,0)", AcHudFormat.Gap(b, 0, 12345, 0), "+12.345");
        Check("Gap(72005,0)", AcHudFormat.Gap(b, 0, 72005, 0), "+1:12.005");
        Check("Gap(0,2)", AcHudFormat.Gap(b, 0, 0, 2), "+2 L");
        Check("Gear(-1)", AcHudFormat.Gear(b, 0, -1), "R");
        Check("Gear(0)", AcHudFormat.Gear(b, 0, 0), "N");
        Check("Gear(6)", AcHudFormat.Gear(b, 0, 6), "6");
        Check("Clock(599)", AcHudFormat.Clock(b, 0, 599), "09:59");
        Check("Clock(3725)", AcHudFormat.Clock(b, 0, 3725), "1:02:05");
        Check("Int(-42)", AcHudFormat.Int(b, 0, -42), "-42");
        Check("OfTotal(2,6)", AcHudFormat.OfTotal(b, 0, 2, 6), "2/6");
        long before = GC.GetAllocatedBytesForCurrentThread();
        int n = 0; for (int i = 0; i < 100000; i++) { n = AcHudFormat.LapTime(b, 0, i * 7); n = AcHudFormat.Delta(b, 0, i - 50000, 2); n = AcHudFormat.Gear(b, 0, i % 8 - 1); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine("  300000 formats allocated " + alloc + " bytes");
        if (alloc > 0) bad++;
        Console.WriteLine("hudfmt: " + (bad == 0 ? "all ok" : bad + " FAILED"));
        return bad == 0 ? 0 : 1;
    }
}

static partial class Program
{
    // redcols <png>: which columns of a ruler image (rpmBar / fuelBar) are red, and where the white ticks are (fractions of the width)
    static int Cmd_redcols(List<string> a)
    {
        string p = Path.Combine(_install.Root, a[1].Replace('/', Path.DirectorySeparatorChar));
        int w, h; byte[] img = PngDecode(File.ReadAllBytes(p), out w, out h);
        var red = new StringBuilder(); var ticks = new StringBuilder(); int r0 = -1, r1 = -1;
        for (int x = 0; x < w; x++)
        {
            int nRed = 0, nWhite = 0;
            for (int y = 0; y < h; y++) { int i = (y * w + x) * 4; if (img[i + 3] < 100) continue; if (img[i] > 150 && img[i + 1] < 90) nRed++; else if (img[i + 1] > 150) nWhite++; }
            if (nRed > 0) { if (r0 < 0) r0 = x; r1 = x; }
            if (nWhite > h / 3) ticks.Append(x).Append(' ');
        }
        Console.WriteLine(a[1] + " " + w + "x" + h + ": red columns " + r0 + ".." + r1 + " (" + (r0 / (float)w).ToString("0.000") + ".." + (r1 / (float)w).ToString("0.000") + "), tall white columns: " + ticks);
        return 0;
    }
}

static partial class Program
{
    // hudmock [out.png] [rpm]: a 1920x1080 MOCK of the monitor HUD (AcHudDesk.Layout + AcHudView.Build numbers copied here) with the
    // install's art and the bitmap fallback font (the game uses Segoe UI through TMP) - to judge composition offline before the lab.
    sealed class Mock
    {
        public int W = 1920, H = 1080; public byte[] Px;
        public Dictionary<string, (byte[] img, int w, int h)> Art = new Dictionary<string, (byte[], int, int)>();
        public AcBitmapFont Font; public byte[] FontImg; public int FW, FH;
        public Mock() { Px = new byte[W * H * 4]; }
        public void Back()
        {
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                int o = (y * W + x) * 4; float t = y / (float)H;
                byte r, g, b;
                if (t < 0.45f) { r = (byte)(120 + 60 * t); g = (byte)(160 + 50 * t); b = (byte)(210 + 20 * t); }
                else { r = (byte)(70 + 20 * t); g = (byte)(72 + 20 * t); b = (byte)(76 + 20 * t); }
                Px[o] = r; Px[o + 1] = g; Px[o + 2] = b; Px[o + 3] = 255;
            }
        }
        void Blend(int x, int y, float r, float g, float b, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || a <= 0f) return;
            int o = (y * W + x) * 4;
            Px[o] = (byte)(Px[o] * (1 - a) + r * a); Px[o + 1] = (byte)(Px[o + 1] * (1 - a) + g * a); Px[o + 2] = (byte)(Px[o + 2] * (1 - a) + b * a);
        }
        public void Rect(float x, float y, float w, float h, float r, float g, float b, float a)
        {
            for (int py = (int)y; py < (int)(y + h); py++) for (int px = (int)x; px < (int)(x + w); px++) Blend(px, py, r * 255, g * 255, b * 255, a);
        }
        // mode 0 simple, 1 radial360 bottom clockwise (fill), 2 horizontal fill, 3 sliced (left/right border px)
        public void Img(string rel, float x, float y, float w, float h, float alpha = 1f, int mode = 0, float fill = 1f, int bl = 0, int br = 0)
        {
            if (!Art.TryGetValue(rel, out var a)) return;
            for (int py = 0; py < (int)h; py++) for (int px = 0; px < (int)w; px++)
            {
                float u = (px + 0.5f) / w, v = (py + 0.5f) / h;
                if (mode == 1)
                {
                    double dx = u - 0.5, dy = v - 0.5; double ang = Math.Atan2(-dx, dy) * 180 / Math.PI; if (ang < 0) ang += 360;
                    if (ang / 360.0 > fill) continue;
                }
                if (mode == 2 && u > fill) continue;
                int sx;
                if (mode == 3)
                {
                    if (px < bl) sx = px; else if (px >= w - br) sx = a.w - (int)(w - px); else sx = bl + (int)((px - bl) / (w - bl - br) * (a.w - bl - br));
                }
                else sx = (int)(u * a.w);
                sx = Math.Clamp(sx, 0, a.w - 1); int sy = Math.Clamp((int)(v * a.h), 0, a.h - 1);
                int si = (sy * a.w + sx) * 4;
                Blend((int)x + px, (int)y + py, a.img[si], a.img[si + 1], a.img[si + 2], a.img[si + 3] / 255f * alpha);
            }
        }
        // align: 0 left, 1 centre, 2 right; size = text height in px (approximates the TMP point size)
        public void Text(string s, float x, float y, float w, float h, float size, int align, float r, float g, float b, float a = 1f)
        {
            char[] c = s.ToCharArray(); var q = new float[c.Length * 8];
            float tw = Font.Measure(c, 0, c.Length) * size;
            float sx = align == 0 ? x : align == 1 ? x + (w - tw) / 2 : x + w - tw;
            float sy = y + (h - size) / 2;
            int n = Font.Layout(c, 0, c.Length, sx, sy, size, 1f, 0f, q);
            for (int k = 0; k < n; k++)
            {
                float x0 = q[k * 8], y0 = q[k * 8 + 1], x1 = q[k * 8 + 2], y1 = q[k * 8 + 3], u0 = q[k * 8 + 4], u1 = q[k * 8 + 5];
                for (int py = (int)y0; py < (int)y1; py++) for (int px = (int)x0; px < (int)x1; px++)
                {
                    float u = u0 + (u1 - u0) * ((px - x0 + 0.5f) / Math.Max(1f, x1 - x0)); float vu = q[k * 8 + 7] - (q[k * 8 + 7] - q[k * 8 + 6]) * ((py - y0 + 0.5f) / (y1 - y0)); float v = 1f - vu;
                    int fx = Math.Clamp((int)(u * FW), 0, FW - 1), fy = Math.Clamp((int)(v * FH), 0, FH - 1);
                    Blend(px, py, r * 255, g * 255, b * 255, FontImg[(fy * FW + fx) * 4 + 3] / 255f * a);
                }
            }
        }
    }

    static int Cmd_hudmock(List<string> a)
    {
        string outPath = a.Count > 1 ? a[1] : Path.Combine(Path.GetTempPath(), "hudmock.png");
        float rpm = a.Count > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 6100f, lim = 7500f;
        var m = new Mock();
        foreach (string rel in AcHudArtList.Files)
        {
            string p = Path.Combine(_install.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            int w, h; var img = PngDecode(File.ReadAllBytes(p), out w, out h); if (img != null) m.Art[rel] = (img, w, h);
        }
        string problem; m.Font = AcBitmapFont.Load(_install.FontsDir, "arial_big", out problem); bool op;
        m.FontImg = FontImage(m.Font, out m.FW, out m.FH, out op);
        var cfg = AcHudConfig.Load(_install.Root, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        bool vr = a.Contains("vr");
        if (vr) { m.W = 1400; m.H = 760; m.Px = new byte[m.W * m.H * 4]; }
        m.Back();
        float[] sb = cfg.SessionBack, fb = cfg.FormBack, tb = cfg.TitleBack;
        // group top-left corners: monitor = AcHudDesk.Layout on 1920x1080; vr = AcHudVr.Layout on its 1400x760 canvas
        float tX = 24, tY = 24, wX = 24, wY = 236, lX = 759, lY = 73, dX = 800, dY = 173, fX = 1215, fY = 70, mX = 606, mY = 290, pX = 790, pY = 760, gX = 750, gY = 812;
        if (vr) { tX = 220; tY = 526; wX = 915; wY = 468; lX = 499; lY = 43; dX = 540; dY = 153; fX = 955; fY = 40; mX = 346; mY = 284; pX = 530; pY = 454; gX = 490; gY = 500; }

        float X = tX, Y = tY, tw = 265, rh = 40, y0 = 34;
        m.Rect(X, Y, tw, 30, sb[0], sb[1], sb[2], sb[3]);
        m.Text(cfg.S("RACE", "RACE"), X + 10, Y, 150, 30, 17, 0, 1, 1, 1);
        m.Text("LAP 4/5", X + 120, Y, tw - 130, 30, 17, 2, 1, 1, 1);
        for (int i = 0; i < 4; i++) { m.Rect(X, Y + y0 + i * rh, tw, rh, sb[0], sb[1], sb[2], sb[3]); m.Img(AcHudArtList.WhiteLine, X, Y + y0 + i * rh, tw, rh, 1, 3, 1, 6, 6); if (i > 0) m.Img(AcHudArtList.Separator, X, Y + y0 + i * rh, tw, 1); }
        m.Text("LAP 4  S2", X + 10, Y + y0, 110, rh, 15, 0, 1, 1, 1, 0.6f);
        m.Text("1:01.234", X + 100, Y + y0, tw - 110, rh, 27, 2, 1, 1, 1);
        m.Img(AcHudArtList.Best, X + 10, Y + y0 + rh + 4.5f, 26, 31); m.Text("1:37.531", X + 60, Y + y0 + rh, tw - 70, rh, 22, 2, 1, 1, 1);
        m.Img(AcHudArtList.Last, X + 10, Y + y0 + 2 * rh + 4.5f, 27, 31); m.Text("1:38.765", X + 60, Y + y0 + 2 * rh, tw - 70, rh, 22, 2, 1, 1, 1);
        m.Text(cfg.S("POS.", "POS."), X + 10, Y + y0 + 3 * rh, 120, rh, 15, 0, 1, 1, 1, 0.6f); m.Text("2/6", X + 100, Y + y0 + 3 * rh, tw - 110, rh, 24, 2, 1, 1, 1);

        X = wX; Y = wY;
        string[] names = { "Shodah", "Wildy", "Meloz", "denevraut", "AI Rossi", "AI Lauda" };
        string[] vals = { "1:37.531", "+1.834", "+4.210", "+9.055", "+15.020", "+1 L" };
        for (int i = 0; i < names.Length; i++)
        {
            float ry = Y + i * 26;
            if (i == 1) m.Img(AcHudArtList.CurrentLapBg, X, ry, 320, 24, 1, 3, 1, 12, 2); else m.Rect(X, ry, 320, 24, fb[0], fb[1], fb[2], fb[3]);
            m.Text((i + 1).ToString(), X + 6, ry, 30, 24, 16, 2, 1, 1, 1);
            float dim = i == 4 ? 0.6f : 1f;
            m.Text(names[i], X + 46, ry, 170, 24, 16, 0, 1, 1, 1, dim);
            m.Text(vals[i], X + 210, ry, 104, 24, 16, 2, 1, 1, 1, dim);
        }

        for (int i = 0; i < 5; i++) m.Img(i < 3 ? AcHudArtList.LightRed : AcHudArtList.LightOff, lX + i * 82, lY, 74, 74);
        m.Img(AcHudArtList.Up, dX + 52, dY + 18, 18, 17); m.Text("-0.21", dX + 40, dY, 240, 54, 40, 1, 0.30f, 0.92f, 0.36f);
        m.Img(AcHudArtList.FlagBlue, fX, fY, 150, 80);
        m.Text("FALSE START!", mX, mY, 708, 40, 24, 1, 1, 1, 1); m.Img(AcHudArtList.MessageBar, mX, mY + 40, 708, 1);
        m.Rect(pX, pY, 340, 38, tb[0], tb[1], tb[2], tb[3] * 0.85f); m.Text("PIT LIMITER  80 Km/h", pX, pY, 340, 38, 20, 1, 1, 1, 1);

        X = gX; Y = gY; float cx = X + 93, cy = Y + 18, D = 234, mid = 18 + 117;
        float frac = Math.Clamp(rpm / lim, 0, 1);
        m.Img(AcHudArtList.RpmOff, cx, cy, D, D); m.Img(AcHudArtList.TurboOff, cx, cy, D, D);
        m.Img(AcHudArtList.TurboOn, cx, cy, D, D, 1, 1, (30 + 0.6f * 298) / 360f);
        m.Img(AcHudArtList.RpmOn, cx, cy, D, D, 1, 1, (30 + frac * 298) / 360f);
        m.Text("3", X + 110, Y + mid - 62, 200, 100, 84, 1, 1, 1, 1);
        m.Text("187", X + 110, Y + mid + 26, 200, 40, 30, 1, 1, 1, 1);
        m.Text("Km/h", X + 110, Y + mid + 62, 200, 24, 16, 1, 1, 1, 1, 0.6f);
        m.Img(AcHudArtList.Abs, X + 18, Y + mid + 34, 46, 46);
        m.Img(AcHudArtList.FuelBar, X + 420 - 18 - 81, Y + mid + 92, 81, 25);
        m.Rect(X + 420 - 18 - 81, Y + mid + 86, 81 * 0.52f, 5, 1, 1, 1, 1);
        Png.Write(outPath, m.W, m.H, m.Px);
        Console.WriteLine("wrote " + outPath + " (MOCK of the " + (vr ? "VR panel canvas" : "monitor HUD") + ": bitmap arial_big stands in for Segoe UI)");
        return 0;
    }
}

static partial class Program
{
    // exestr <text>...: printable ASCII and UTF-16LE strings in acs.exe containing any of the texts (case-insensitive) - AC's own UI words
    static int Cmd_exestr(List<string> a)
    {
        byte[] d = File.ReadAllBytes(Path.Combine(_install.Root, "acs.exe"));
        var want = new List<string>(); for (int i = 1; i < a.Count; i++) want.Add(a[i]);
        var seen = new HashSet<string>();
        void Test(string s, string kind) { foreach (string w in want) if (s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 && seen.Add(kind + s)) Console.WriteLine("  " + kind + " \"" + s + "\""); }
        var sb = new StringBuilder();
        for (int i = 0; i < d.Length; i++)
        {
            byte c = d[i];
            if (c >= 0x20 && c < 0x7f) sb.Append((char)c); else { if (sb.Length >= 4) Test(sb.ToString(), "A"); sb.Clear(); }
        }
        for (int phase = 0; phase < 2; phase++)
        {
            sb.Clear();
            for (int i = phase; i + 1 < d.Length; i += 2)
            {
                byte c = d[i], h = d[i + 1];
                if (h == 0 && c >= 0x20 && c < 0x7f) sb.Append((char)c); else { if (sb.Length >= 4) Test(sb.ToString(), "U"); sb.Clear(); }
            }
        }
        return 0;
    }

    // vrhud [car]: the VR panel's dash line per owned car (AcHudSightline on the car's kn5, from the VR eye S uses: vr.ini DRIVEREYES, else
    // car.ini DRIVEREYES), the steering-rim top line for comparison, and where AcHudVr's fit puts the panel (default size / distance).
    static int Cmd_vrhud(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        AcContent content = AcContent.Scan(_install);
        AcIni vr = null;
        string vrPath = Path.Combine(Path.Combine(_install.SystemDir, "cfg"), "vr.ini");
        if (File.Exists(vrPath)) vr = AcIni.Load(vrPath);
        int cars = 0, measured = 0, rimAboveLine = 0, failed = 0;
        float lineMin = 99f, lineMax = -99f, topMin = 99f, topMax = -99f, botMax = 0f, rimMin = 99f, rimMax = -99f; long msMax = 0, msSum = 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string G(float v) { return v.ToString("0.0", inv); }
        const float ahead = 0.62f, width = 0.57f, heightM = 0.57f * 520f / 1400f;   // AcHudVr defaults (VR canvas 1400 x 520)
        foreach (AcCarEntry car in content.Cars)
        {
            if (!car.Owned) continue;
            if (only != null && !string.Equals(car.Id, only, StringComparison.OrdinalIgnoreCase)) continue;
            cars++;
            string line = car.Id + ":";
            try
            {
                AcdArchive acd = AcdArchive.Open(CarPath(car.Id));
                AcIni ci = acd.ReadIniOrEmpty("car.ini");
                float[] eyes = ci.GetFloats("GRAPHICS", "DRIVEREYES");
                float[] vrEyes = vr != null ? vr.GetFloats(car.Id, "DRIVEREYES") : null;
                bool fromVr = vrEyes != null && vrEyes.Length >= 3;
                float[] e = fromVr ? vrEyes : eyes;
                if (e == null || e.Length < 3) { Console.WriteLine(line + " no DRIVEREYES"); failed++; continue; }
                var eye = new AcVec3(e[0], e[1], e[2]);
                string problem;
                AcHudSightline s = AcHudSightline.Measure(car.Kn5Path, eye, out problem);
                if (s == null) { Console.WriteLine(line + " FAILED " + problem); failed++; continue; }
                measured++;
                msMax = Math.Max(msMax, s.Ms); msSum += s.Ms;
                float half = (float)(Math.Atan(width * 0.5 / ahead) * 180 / Math.PI);
                float hi = s.Highest(-half, half), mid = s.LineAt(0f);
                lineMin = Math.Min(lineMin, hi); lineMax = Math.Max(lineMax, hi);
                float top;
                float drop = AcHudSightline.FitCentre(s, 0f, ahead, width, heightM, 1f, 6f, out top);
                topMin = Math.Min(topMin, top); topMax = Math.Max(topMax, top);
                float bottom = (float)(Math.Atan((drop + heightM * 0.5) / ahead) * 180 / Math.PI);
                botMax = Math.Max(botMax, bottom);
                // rim top line (STEER_HR hub + outer radius along the rim plane's up)
                string rimTxt = "-";
                Kn5File f = Kn5File.Read(car.Kn5Path, Kn5ReadOptions.HeaderOnly);
                Kn5Node steer = f.Find("STEER_HR") ?? f.Find("STEER_LR");
                if (steer != null)
                {
                    AcMat4 sw = steer.World; AcMat4 sinv = sw.InverseAffine();
                    float outer = 0f; var meshes = new List<Kn5Node>(); HCollect(steer, meshes);
                    foreach (Kn5Node m in meshes)
                    {
                        f.LoadMesh(m.Mesh); AcMat4 to = AcMat4.Mul(m.World, sinv);
                        for (int i = 0; i < m.Mesh.VertexCount; i++) { AcVec3 p = to.TransformPoint(m.Mesh.Position(i)); outer = Math.Max(outer, (float)Math.Sqrt(p.X * p.X + p.Y * p.Y)); }
                        m.Mesh.Release();
                    }
                    AcVec3 hub = sw.Translation, ax = sw.AxisZ.Normalized, up = new AcVec3(0, 1, 0);
                    AcVec3 u = (up - ax * AcVec3.Dot(up, ax)).Normalized;
                    AcVec3 rt = hub + u * outer - eye;
                    float rimDeg = (float)(Math.Atan2(-rt.Y, Math.Sqrt(rt.X * rt.X + rt.Z * rt.Z)) * 180 / Math.PI);
                    rimMin = Math.Min(rimMin, rimDeg); rimMax = Math.Max(rimMax, rimDeg);
                    if (rimDeg < mid - 0.5f) rimAboveLine++;
                    rimTxt = G(rimDeg) + " (r " + outer.ToString("0.000", inv) + ")";
                }
                var cols = new StringBuilder();
                for (int c = 0; c < s.Columns; c += 4) cols.Append(G(s.Line[c])).Append(' ');
                line += " eye " + (fromVr ? "vr.ini " : "car.ini ") + eye + " line@0 " + G(mid) + " highest(+-" + G(half) + ") " + G(hi) + " rimTop " + rimTxt
                        + " | fit: centre drop " + (drop * 100f).ToString("0.0", inv) + " cm, top " + G(top) + " deg, bottom " + G(bottom) + " deg | " + s.Meshes + " meshes " + s.Triangles + " tris " + s.Ms + " ms | cols(-34..34 /4): " + cols;
            }
            catch (Exception ex) { line += " ERROR " + ex.GetType().Name + ": " + ex.Message; failed++; }
            Console.WriteLine(line);
        }
        Console.WriteLine("CENSUS vrhud: " + cars + " cars, " + measured + " measured, " + failed + " failed; dash line (highest over the panel) " + G(lineMin) + ".." + G(lineMax)
            + " deg; fitted top edge " + G(topMin) + ".." + G(topMax) + " deg, bottom down to " + G(botMax) + " deg; rim top " + G(rimMin) + ".." + G(rimMax) + " deg, rim top ABOVE the centre line on " + rimAboveLine
            + " cars; measure " + (measured > 0 ? msSum / measured : 0) + " ms avg, " + msMax + " ms max");
        return failed == 0 ? 0 : 1;
    }
}

static partial class Program
{
    // sightself: AcHudSightline on synthetic geometry (a dash wall 0.5 m ahead whose top is 0.1 m under the eye -> 11.3 deg; a mirror
    // above a gap of road must not count) + the panel fit against a flat line
    static int Cmd_sightself(List<string> a)
    {
        int bad = 0;
        var s = new AcHudSightline();
        // dash: a vertical wall z = 0.5, x -0.6..0.6, y -0.8..-0.1 (eye frame, x right, y up)
        s.AddTriangle(-0.6f, -0.8f, 0.5f, 0.6f, -0.8f, 0.5f, 0.6f, -0.1f, 0.5f);
        s.AddTriangle(-0.6f, -0.8f, 0.5f, 0.6f, -0.1f, 0.5f, -0.6f, -0.1f, 0.5f);
        // a "mirror" floating at y -0.02..0.02, z 0.4 (below the horizon, above a gap): must be ignored
        s.AddTriangle(-0.1f, -0.02f, 0.4f, 0.1f, -0.02f, 0.4f, 0.1f, 0.02f, 0.4f);
        // far bonnet beyond MaxDist: ignored
        s.AddTriangle(-1f, -0.5f, 2.5f, 1f, -0.5f, 2.5f, 0f, -0.3f, 2.5f);
        s.Finish();
        float want = (float)(Math.Atan2(0.1, 0.5) * 180 / Math.PI);
        float got = s.LineAt(0f);
        // at yaw 30 deg the wall is farther (0.5 / cos 30) so its top is lower in angle
        float want30 = (float)(Math.Atan2(0.1, 0.5 / Math.Cos(30 * Math.PI / 180)) * 180 / Math.PI);
        Console.WriteLine("  line at yaw 0: " + got.ToString("0.00") + " (want " + want.ToString("0.00") + " +- 0.5), yaw 30: " + s.LineAt(30f).ToString("0.00") + " (want " + want30.ToString("0.00") + ")");
        if (Math.Abs(got - want) > 0.5f) bad++;
        if (Math.Abs(s.LineAt(30f) - want30) > 0.6f) bad++;
        float top;
        float drop = AcHudSightline.FitCentre(s, 0f, 0.62f, 0.57f, 0.2117f, 1f, 6f, out top);
        float worst;
        float centreDeg = (float)(Math.Atan2(drop, 0.62) * 180 / Math.PI);
        AcHudSightline.TopEdge(centreDeg, 0.62f, 0.57f, 0.2117f, s, 0f, out worst);
        Console.WriteLine("  fit: centre " + (drop * 100).ToString("0.0") + " cm under the eye, top edge " + top.ToString("0.00") + " deg, worst clearance " + worst.ToString("0.00") + " deg (want >= 1, < 1.3)");
        if (worst < 0.99f || worst > 1.3f) bad++;
        float topFlat;
        AcHudSightline.FitCentre(null, 3f, 0.62f, 0.57f, 0.2117f, 1f, 6f, out topFlat);
        Console.WriteLine("  flat 3 deg line + 6 deg floor: top " + topFlat.ToString("0.00") + " (want 6.0)");
        if (Math.Abs(topFlat - 6f) > 0.05f) bad++;
        Console.WriteLine("sightself: " + (bad == 0 ? "all ok" : bad + " FAILED"));
        return bad == 0 ? 0 : 1;
    }
}

static partial class Program
{
    static void HCollect(Kn5Node n, List<Kn5Node> into)
    {
        if (n.Mesh != null) into.Add(n);
        foreach (Kn5Node c in n.Children) HCollect(c, into);
    }
}
