using System;
using System.Globalization;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] One of AC's bitmap fonts: <c>content/fonts/&lt;name&gt;.png</c> (or <c>.bmp</c>) + <c>&lt;name&gt;.txt</c>. The image is ONE row of
    /// glyphs at full height; the txt is a list of normalised U positions (one per line) for consecutive characters from 32 (space):
    /// glyph i = char (32 + i) spans [U[i], U[i+1]) of the image width, full height (research ac_physics_sound_rules.md 4). Most fonts have
    /// 96 lines = 95 glyphs (32..126) + the end of the last one (digital_toyota.txt has 6 tables back to back, each restarting at 0: the LAST one matches the image - harness bmtables: tables 2, 3, 5 cut 100 % clean, 0/1/4 do not). Values past 1 (arial ends at 1.1016) are glyphs that
    /// fall off the image: they are clamped to 1 and a glyph with no width left is "missing". These are the fonts
    /// digital_instruments.ini <c>FONT=</c> refers to (C's cockpit displays) and the HUD's fallback text renderer.
    /// Unity-free: the image is not decoded here (Unity's ImageConversion does PNG on the main thread; <see cref="DecodeBmp"/> handles the
    /// three .bmp fonts). Thread-safe after construction.
    /// </summary>
    public sealed class AcBitmapFont
    {
        /// <summary>First character of the table (AC: space).</summary>
        public const int FirstChar = 32;

        /// <summary>File stem (e.g. "digital_big").</summary>
        public string Name { get; private set; }
        /// <summary>Full path of the image (.png or .bmp); null when built from text only.</summary>
        public string ImagePath { get; private set; }
        public string TxtPath { get; private set; }
        public bool ImageIsBmp { get; private set; }
        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }
        /// <summary>Number of glyphs (lines - 1).</summary>
        public int Count { get; private set; }
        /// <summary>Largest U value in the txt before clamping (arial 1.1016).</summary>
        public float MaxRawU { get; private set; }
        /// <summary>Glyphs whose span was cut by the clamp (at least partly off the image).</summary>
        public int ClampedGlyphs { get; private set; }

        private float[] _u;   // Count + 1 boundaries, clamped to [0, 1], non-decreasing

        private AcBitmapFont() { }

        /// <summary>Loads <paramref name="name"/> from <paramref name="fontsDir"/> (AcInstall.FontsDir): the txt and the image header (size).
        /// Returns null (with <paramref name="problem"/>) when a file is missing or unreadable.</summary>
        public static AcBitmapFont Load(string fontsDir, string name, out string problem)
        {
            problem = null;
            try
            {
                if (string.IsNullOrEmpty(fontsDir) || string.IsNullOrEmpty(name)) { problem = "no fonts folder / name"; return null; }
                string stem = name;
                if (stem.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || stem.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 4);
                string txt = Path.Combine(fontsDir, stem + ".txt");
                if (!File.Exists(txt)) { problem = "no " + stem + ".txt"; return null; }
                string img = Path.Combine(fontsDir, stem + ".png");
                bool bmp = false;
                if (!File.Exists(img)) { img = Path.Combine(fontsDir, stem + ".bmp"); bmp = true; }
                if (!File.Exists(img)) { problem = "no " + stem + ".png/.bmp"; return null; }
                int w, h;
                if (!ReadImageSize(img, out w, out h, out problem)) return null;
                var f = Parse(stem, File.ReadAllText(txt), w, h, out problem);
                if (f == null) return null;
                f.ImagePath = img; f.TxtPath = txt; f.ImageIsBmp = bmp;
                return f;
            }
            catch (Exception e)
            {
                problem = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        /// <summary>Builds the glyph table from the txt contents and the image size.</summary>
        public static AcBitmapFont Parse(string name, string txt, int imageWidth, int imageHeight, out string problem)
        {
            problem = null;
            if (txt == null) { problem = "empty txt"; return null; }
            var vals = new System.Collections.Generic.List<float>(100);
            int i = 0, n = txt.Length;
            while (i < n)
            {
                while (i < n && (txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n' || txt[i] == '﻿')) i++;
                int s = i;
                while (i < n && !(txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n')) i++;
                if (i > s)
                {
                    float v;
                    string tok = txt.Substring(s, i - s).Replace(',', '.');
                    if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    {
                        // digital_toyota.txt holds six tables back to back (each restarts at 0): keep the LAST (the latest export,
                        // the one that matches the image).
                        if (vals.Count > 1 && v < vals[vals.Count - 1]) vals.Clear();
                        vals.Add(v);
                    }
                }
            }
            if (vals.Count < 2) { problem = "txt has " + vals.Count + " values"; return null; }
            if (imageWidth <= 0 || imageHeight <= 0) { problem = "bad image size " + imageWidth + "x" + imageHeight; return null; }
            var f = new AcBitmapFont { Name = name, ImageWidth = imageWidth, ImageHeight = imageHeight, Count = vals.Count - 1 };
            f._u = new float[vals.Count];
            float max = 0f, prev = 0f;
            int clamped = 0;
            for (int k = 0; k < vals.Count; k++)
            {
                float v = vals[k];
                if (v > max) max = v;
                if (v < prev) v = prev;                 // never go backwards
                float c = v > 1f ? 1f : (v < 0f ? 0f : v);
                if (v > 1f && k > 0 && vals[k - 1] < v) clamped++;
                f._u[k] = c;
                prev = v;
            }
            f.MaxRawU = max;
            f.ClampedGlyphs = clamped;
            return f;
        }

        /// <summary>The glyph of <paramref name="c"/>: U span (normalised, clamped). False when the font has no such glyph or it has no width.</summary>
        public bool Glyph(char c, out float u0, out float u1)
        {
            int i = c - FirstChar;
            if (i < 0 || i >= Count) { u0 = u1 = 0f; return false; }
            u0 = _u[i]; u1 = _u[i + 1];
            return u1 > u0;
        }

        /// <summary>U boundary k (0..Count), clamped.</summary>
        public float Boundary(int k) { return _u[k < 0 ? 0 : (k > Count ? Count : k)]; }

        /// <summary>Glyph pixel span in the image (x0 inclusive, x1 exclusive).</summary>
        public bool GlyphPixels(char c, out int x0, out int x1)
        {
            float u0, u1;
            bool ok = Glyph(c, out u0, out u1);
            x0 = (int)Math.Round(u0 * ImageWidth); x1 = (int)Math.Round(u1 * ImageWidth);
            return ok && x1 > x0;
        }

        /// <summary>Ink rows of the image (top-down, inclusive): the glyphs of some fonts fill only part of the strip (arial: about a
        /// quarter of its 64 rows). Full height until <see cref="MeasureInk"/> ran on the decoded image.</summary>
        public int InkTop { get; private set; }
        public int InkBottom { get; private set; } = -1;
        /// <summary>Height in image pixels a glyph quad covers (the ink band, or the full image).</summary>
        public int GlyphHeightPx { get { return InkBottom >= InkTop && InkBottom >= 0 ? InkBottom - InkTop + 1 : ImageHeight; } }

        /// <summary>Finds the rows that hold ink (alpha &gt; 40 after <see cref="LuminanceToAlpha"/>) so text can be drawn by its glyph
        /// height instead of the strip height. Call with the decoded RGBA (top-down rows; <paramref name="bottomUp"/> for Unity GetPixels32 order).</summary>
        public void MeasureInk(byte[] rgba, int w, int h, bool bottomUp = false)
        {
            if (rgba == null || w <= 0 || h <= 0 || rgba.Length < w * h * 4) return;
            int top = -1, bottom = -1;
            for (int y = 0; y < h; y++)
            {
                bool ink = false;
                int o = y * w * 4 + 3;
                for (int x = 0; x < w; x++, o += 4) if (rgba[o] > 40) { ink = true; break; }
                if (ink) { if (top < 0) top = y; bottom = y; }
            }
            if (top < 0) return;
            if (bottomUp) { int t = h - 1 - bottom; bottom = h - 1 - top; top = t; }
            InkTop = Math.Max(0, top - 1); InkBottom = Math.Min(h - 1, bottom + 1);   // one row of edge
        }

        /// <summary>Advance of <paramref name="c"/> in units of the text height (glyph width / image height). Missing glyphs advance like '0'
        /// (digits are what the displays draw) or 0.5 // ours.</summary>
        public float Advance(char c)
        {
            float u0, u1;
            if (Glyph(c, out u0, out u1)) return (u1 - u0) * ImageWidth / GlyphHeightPx;
            if (c != '0' && Glyph('0', out u0, out u1)) return (u1 - u0) * ImageWidth / GlyphHeightPx;
            return 0.5f;   // ours
        }

        /// <summary>Width of the text in units of height (no allocation).</summary>
        public float Measure(char[] text, int start, int length)
        {
            float w = 0f;
            for (int k = 0; k < length; k++) w += Advance(text[start + k]);
            return w;
        }

        public float Measure(string text)
        {
            if (text == null) return 0f;
            float w = 0f;
            for (int k = 0; k < text.Length; k++) w += Advance(text[k]);
            return w;
        }

        /// <summary>
        /// Lays the text out as quads: for each drawn glyph 8 floats are written to <paramref name="quads"/> (x0, y0, x1, y1, u0, u1, v0, v1)
        /// starting at (x, y) = the text's bottom-left, glyphs <paramref name="height"/> tall, widths by their own aspect (times
        /// <paramref name="widthScale"/>), plus <paramref name="spacing"/> (units of height) between glyphs. V is in Unity's convention
        /// (0 = bottom row of the image, 1 = top row) and covers the ink band (<see cref="MeasureInk"/>) or the whole strip. Spaces and
        /// missing glyphs advance without a quad. Returns the number of quads written (at most quads.Length / 8). No allocation.
        /// </summary>
        public int Layout(char[] text, int start, int length, float x, float y, float height, float widthScale, float spacing, float[] quads)
        {
            int max = quads != null ? quads.Length / 8 : 0;
            int q = 0;
            float cx = x;
            float v0 = 0f, v1 = 1f;
            if (InkBottom >= InkTop && InkBottom >= 0 && ImageHeight > 0)
            {
                v0 = 1f - (InkBottom + 1) / (float)ImageHeight;
                v1 = 1f - InkTop / (float)ImageHeight;
            }
            for (int k = 0; k < length; k++)
            {
                char c = text[start + k];
                float adv = Advance(c) * height * widthScale;
                float u0, u1;
                if (c != ' ' && Glyph(c, out u0, out u1) && q < max)
                {
                    int o = q * 8;
                    quads[o] = cx; quads[o + 1] = y; quads[o + 2] = cx + adv; quads[o + 3] = y + height;
                    quads[o + 4] = u0; quads[o + 5] = u1; quads[o + 6] = v0; quads[o + 7] = v1;
                    q++;
                }
                cx += adv + spacing * height;
            }
            return q;
        }

        // ------------------------------------------------------------------ image helpers (Unity-free)

        /// <summary>Reads width/height from a PNG (IHDR) or BMP (BITMAPINFOHEADER) header without decoding.</summary>
        public static bool ReadImageSize(string path, out int width, out int height, out string problem)
        {
            width = height = 0; problem = null;
            byte[] h = new byte[32];
            int got;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                got = fs.Read(h, 0, h.Length);
            return ReadImageSize(h, got, out width, out height, out problem);
        }

        /// <summary>Width/height from the first bytes of a PNG or BMP.</summary>
        public static bool ReadImageSize(byte[] h, int length, out int width, out int height, out string problem)
        {
            width = height = 0; problem = null;
            if (length >= 24 && h[0] == 0x89 && h[1] == (byte)'P' && h[2] == (byte)'N' && h[3] == (byte)'G')
            {
                width = (h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19];
                height = (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23];
                return width > 0 && height > 0;
            }
            if (length >= 26 && h[0] == (byte)'B' && h[1] == (byte)'M')
            {
                width = BitConverter.ToInt32(h, 18);
                height = Math.Abs(BitConverter.ToInt32(h, 22));
                return width > 0 && height > 0;
            }
            problem = "not a PNG/BMP";
            return false;
        }

        /// <summary>
        /// Decodes an uncompressed 24/32-bit BMP (AC's comic/console/default_big fonts are 24-bit, white glyphs on black) to RGBA, rows
        /// TOP-DOWN. 24-bit images have no alpha: alpha = the brightest channel, colour = white (so the glyph tints like a PNG font).
        /// Returns null on an unsupported file.
        /// </summary>
        public static byte[] DecodeBmp(byte[] b, out int width, out int height, out string problem)
        {
            width = height = 0; problem = null;
            if (b == null || b.Length < 54 || b[0] != (byte)'B' || b[1] != (byte)'M') { problem = "not a BMP"; return null; }
            int off = BitConverter.ToInt32(b, 10);
            int w = BitConverter.ToInt32(b, 18);
            int hRaw = BitConverter.ToInt32(b, 22);
            int bpp = BitConverter.ToInt16(b, 28);
            int comp = BitConverter.ToInt32(b, 30);
            if (w <= 0 || hRaw == 0 || (bpp != 24 && bpp != 32) || (comp != 0 && comp != 3)) { problem = "unsupported BMP " + bpp + " bpp, compression " + comp; return null; }
            bool bottomUp = hRaw > 0;
            int h = Math.Abs(hRaw);
            int bytesPer = bpp / 8;
            int stride = ((w * bytesPer) + 3) & ~3;
            if (off + (long)stride * h > b.Length) { problem = "truncated BMP"; return null; }
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int src = off + (bottomUp ? (h - 1 - y) : y) * stride;
                int dst = y * w * 4;
                for (int x = 0; x < w; x++, src += bytesPer, dst += 4)
                {
                    byte bl = b[src], g = b[src + 1], r = b[src + 2];
                    if (bpp == 32)
                    {
                        rgba[dst] = r; rgba[dst + 1] = g; rgba[dst + 2] = bl; rgba[dst + 3] = b[src + 3];
                    }
                    else
                    {
                        byte a = r > g ? (r > bl ? r : bl) : (g > bl ? g : bl);
                        rgba[dst] = 255; rgba[dst + 1] = 255; rgba[dst + 2] = 255; rgba[dst + 3] = a;
                    }
                }
            }
            width = w; height = h;
            return rgba;
        }

        /// <summary>
        /// Makes a font image usable with ordinary alpha blending, in place (RGBA bytes). AC's font images are light glyphs on BLACK: most have
        /// no alpha (all 255), some (f2004, pagani, ttcup...) carry an alpha that is opaque over the black background too. The ink is the
        /// brightness, so: alpha' = alpha * max(r,g,b) / 255 and the colour is un-premultiplied (white glyphs stay white, coloured LED fonts
        /// keep their colour). Fonts with a real alpha over white/transparent ink are unchanged (max = 255). Returns true when the image had
        /// no alpha at all (diagnostic).
        /// </summary>
        public static bool LuminanceToAlpha(byte[] rgba)
        {
            bool allOpaque = true;
            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                if (rgba[i + 3] != 255) allOpaque = false;
                int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                int m = r > g ? (r > b ? r : b) : (g > b ? g : b);
                if (m == 255) continue;
                rgba[i + 3] = (byte)(rgba[i + 3] * m / 255);
                if (m > 0) { rgba[i] = (byte)(r * 255 / m); rgba[i + 1] = (byte)(g * 255 / m); rgba[i + 2] = (byte)(b * 255 / m); }
            }
            return allOpaque;
        }

        public override string ToString()
        {
            return Name + " (" + Count + " glyphs, " + ImageWidth + "x" + ImageHeight + (ImageIsBmp ? " bmp" : "") + ", max U " + MaxRawU.ToString("0.###", CultureInfo.InvariantCulture)
                + (ClampedGlyphs > 0 ? ", " + ClampedGlyphs + " clamped" : "") + ")";
        }
    }
}
