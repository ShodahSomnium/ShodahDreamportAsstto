using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>What a prepared texture becomes in Unity (the Unity side maps it to a TextureFormat).</summary>
    public enum AcTexKind { DXT1, DXT5, BGRA32, RGBA32, Png }

    /// <summary>
    /// A texture made ready on a worker thread (Unity-free): the bytes <c>Texture2D.LoadRawTextureData</c> takes for
    /// <see cref="Mips"/> levels, rows in FILE order (DDS row 0 = image top lands at Unity v = 0, which matches the kn5 uv (u, v_stored)
    /// with Repeat wrap - research C2/T4), or a PNG/JPG passthrough (decoded on the main thread, rows flipped there).
    /// </summary>
    public sealed class AcTexPrep
    {
        public string Name;
        public int Width, Height, Mips = 1;
        public AcTexKind Kind;
        public byte[] Data;
        /// <summary>The source format (log lines / census).</summary>
        public AcTexFormat Source;
        /// <summary>True when mips were made by us (the file had one level).</summary>
        public bool MipsMade;
        /// <summary>Why the texture could not be prepared (then Data is null and the Unity side uses a neutral texture).</summary>
        public string Problem;
        public long Bytes { get { return Data != null ? Data.LongLength : 0; } }
    }

    /// <summary>
    /// Worker-thread texture preparation for kn5 textures and skin files (Unity-free; compiled by the W harness copy too).
    /// <list type="bullet">
    /// <item>DXT1 / DXT5 with complete mip chains: uploaded as they are (Unity BC1 / BC3). Truncated chains (2048x2048 with 5 mips) keep the
    /// file's mip count. A chain shorter than the header promises is cut to what the data holds.</item>
    /// <item>DXT1 / DXT5 with ONE level (927 car textures, a few per track): level 0 kept as it is, the smaller levels made by a box filter
    /// and re-encoded to the same DXT format by <see cref="EncodeDxt"/> (AC's loader gives the game a full chain; without mips distant
    /// surfaces shimmer, worst in VR).</item>
    /// <item>B8G8R8A8 / R8G8B8A8 with mips: raw (Unity BGRA32 / RGBA32). Everything else (DXT3 - Unity has none -, B8G8R8 24-bit, 16-bit,
    /// luminance, palette, BC4/BC5) is decoded level by level to RGBA32 (<see cref="AcDds.DecodeRgba32"/>), mips made when the file has one.</item>
    /// <item>PNG / JPG: passthrough.</item>
    /// </list>
    /// </summary>
    public static class AcTexPrepare
    {
        /// <summary>Textures up to this size (largest side) get no generated mips. // ours</summary>
        public const int NoMipsBelow = 8;

        public static AcTexPrep Prepare(string name, byte[] file, bool makeMips)
        {
            var p = new AcTexPrep { Name = name };
            if (file == null || file.Length < 4) { p.Problem = "empty"; return p; }
            var info = AcDds.Parse(file, 0, file.Length, file.Length);
            p.Source = info.Format;
            if (info.IsPassthrough) { p.Kind = AcTexKind.Png; p.Data = file; return p; }
            if (!info.IsDds || info.Format == AcTexFormat.Unknown || info.Width <= 0 || info.Height <= 0) { p.Problem = "unsupported texture (" + info + ")"; return p; }
            if (info.IsCubemap || info.Depth > 1) { p.Problem = "cube / volume texture not supported (" + info + ")"; return p; }
            p.Width = info.Width; p.Height = info.Height;

            // levels the data really holds
            int mips = info.MipCount, avail = file.Length - info.DataOffset;
            while (mips > 1 && Chain(info, mips) > avail) mips--;
            if (Chain(info, 1) > avail) { p.Problem = "data shorter than one level (" + info + ")"; return p; }
            bool wantMips = makeMips && mips == 1 && Math.Max(info.Width, info.Height) > NoMipsBelow;
            // truncated chains (most track details: 1024 with 5-6 levels, smallest 32-64 px) are completed down to 1x1 from their last level:
            // clamped at 32 px, a detail tiled every 1.7 m drew moiré bands on magione's gravel trap from 100 m (lab 2026-11-01)
            bool extend = makeMips && mips > 1 && mips < FullChain(info.Width, info.Height);

            bool dxt = info.Format == AcTexFormat.DXT1 || info.Format == AcTexFormat.DXT5;
            if (dxt && info.Width % 4 == 0 && info.Height % 4 == 0)
            {
                p.Kind = info.Format == AcTexFormat.DXT1 ? AcTexKind.DXT1 : AcTexKind.DXT5;
                if (!wantMips && extend && ExtendDxtChain(p, file, info, mips)) return p;
                if (!wantMips)
                {
                    p.Mips = mips;
                    p.Data = Slice(file, info.DataOffset, Chain(info, mips));
                    return p;
                }
                MakeDxtChain(p, file, info);
                return p;
            }
            if ((info.Format == AcTexFormat.BGRA32 || info.Format == AcTexFormat.RGBA32) && !wantMips && !extend)
            {
                p.Kind = info.Format == AcTexFormat.BGRA32 ? AcTexKind.BGRA32 : AcTexKind.RGBA32;
                p.Mips = mips;
                p.Data = Slice(file, info.DataOffset, Chain(info, mips));
                return p;
            }

            // decode to RGBA32 (every level the file has, or level 0 + made mips)
            p.Kind = AcTexKind.RGBA32;
            int w0, h0;
            byte[] l0 = AcDds.DecodeRgba32(file, info, 0, out w0, out h0);
            if (l0 == null) { p.Problem = "cannot decode " + info; return p; }
            if (wantMips)
            {
                int full = FullChain(w0, h0);
                var levels = new byte[full][];
                levels[0] = l0;
                int w = w0, h = h0;
                for (int l = 1; l < full; l++) { levels[l] = Half(levels[l - 1], w, h, out w, out h); }
                p.Mips = full; p.MipsMade = true;
                p.Data = Concat(levels);
                return p;
            }
            int fullLv = FullChain(w0, h0);
            var lv = new byte[extend ? fullLv : mips][];
            lv[0] = l0;
            int got = 1, lw = w0, lh = h0;
            for (int l = 1; l < mips; l++)
            {
                int w, h;
                lv[l] = AcDds.DecodeRgba32(file, info, l, out w, out h);
                if (lv[l] == null) break;
                lw = w; lh = h;
                got++;
            }
            if (extend && got == mips)
            {
                // complete the truncated chain from the last level the file holds (see "extend")
                for (int l = got; l < fullLv; l++) { lv[l] = Half(lv[l - 1], lw, lh, out lw, out lh); got++; }
                p.MipsMade = true;
            }
            p.Mips = got;
            if (got < lv.Length) { var t = new byte[got][]; Array.Copy(lv, t, got); lv = t; }
            p.Data = Concat(lv);
            return p;
        }

        /// <summary>DXT1 / DXT5 with a truncated chain: the file's levels as they are + the missing smaller levels made from the last one
        /// (box filter, re-encoded by <see cref="EncodeDxt"/>). False when the last level cannot be decoded (the caller keeps the file's chain).</summary>
        static bool ExtendDxtChain(AcTexPrep p, byte[] file, AcDdsInfo info, int mips)
        {
            int w, h;
            byte[] rgba = AcDds.DecodeRgba32(file, info, mips - 1, out w, out h);
            if (rgba == null) return false;
            bool dxt5 = info.Format == AcTexFormat.DXT5;
            int full = FullChain(info.Width, info.Height);
            var parts = new byte[full - mips + 1][];
            parts[0] = Slice(file, info.DataOffset, Chain(info, mips));
            for (int l = 1; l < parts.Length; l++)
            {
                rgba = Half(rgba, w, h, out w, out h);
                parts[l] = EncodeDxt(rgba, w, h, dxt5);
            }
            p.Mips = full; p.MipsMade = true;
            p.Data = Concat(parts);
            return true;
        }

        static int Chain(AcDdsInfo info, int mips) { int s = 0; for (int i = 0; i < mips; i++) s += info.MipBytes(i); return s; }

        /// <summary>Levels of a full chain down to 1x1.</summary>
        public static int FullChain(int w, int h) { int n = 1; while (w > 1 || h > 1) { w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); n++; } return n; }

        static byte[] Slice(byte[] src, int off, int len)
        {
            if (off == 0 && len == src.Length) return src;
            var d = new byte[len];
            Buffer.BlockCopy(src, off, d, 0, len);
            return d;
        }

        static byte[] Concat(byte[][] parts)
        {
            int n = 0;
            for (int i = 0; i < parts.Length; i++) n += parts[i].Length;
            var d = new byte[n];
            int o = 0;
            for (int i = 0; i < parts.Length; i++) { Buffer.BlockCopy(parts[i], 0, d, o, parts[i].Length); o += parts[i].Length; }
            return d;
        }

        static void MakeDxtChain(AcTexPrep p, byte[] file, AcDdsInfo info)
        {
            bool dxt5 = info.Format == AcTexFormat.DXT5;
            int w, h;
            byte[] rgba = AcDds.DecodeRgba32(file, info, 0, out w, out h);
            int full = FullChain(info.Width, info.Height);
            var parts = new byte[full][];
            parts[0] = Slice(file, info.DataOffset, info.MipBytes(0));
            for (int l = 1; l < full; l++)
            {
                rgba = Half(rgba, w, h, out w, out h);
                parts[l] = EncodeDxt(rgba, w, h, dxt5);
            }
            p.Mips = full; p.MipsMade = true;
            p.Data = Concat(parts);
        }

        /// <summary>2x2 box filter of an RGBA32 image (odd sizes clamp); returns the half-size image.</summary>
        public static byte[] Half(byte[] src, int w, int h, out int nw, out int nh)
        {
            nw = Math.Max(1, w >> 1); nh = Math.Max(1, h >> 1);
            var d = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
            {
                int y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1);
                    int a = (y0 * w + x0) * 4, b = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, e = (y1 * w + x1) * 4, o = (y * nw + x) * 4;
                    d[o] = (byte)((src[a] + src[b] + src[c] + src[e] + 2) >> 2);
                    d[o + 1] = (byte)((src[a + 1] + src[b + 1] + src[c + 1] + src[e + 1] + 2) >> 2);
                    d[o + 2] = (byte)((src[a + 2] + src[b + 2] + src[c + 2] + src[e + 2] + 2) >> 2);
                    d[o + 3] = (byte)((src[a + 3] + src[b + 3] + src[c + 3] + src[e + 3] + 2) >> 2);
                }
            }
            return d;
        }

        // ------------------------------------------------------------------ DXT encoder (range fit; for generated mips only)

        /// <summary>
        /// Encodes an RGBA32 image to DXT1 (<paramref name="dxt5"/> false; 3-colour + transparent mode for blocks with alpha &lt; 128)
        /// or DXT5. A fast bounding-box range fit (as stb_dxt's low-quality path): plenty for mip levels &gt; 0.
        /// </summary>
        public static byte[] EncodeDxt(byte[] rgba, int w, int h, bool dxt5)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4), bb = dxt5 ? 16 : 8;
            var o = new byte[bw * bh * bb];
            var blk = new byte[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            int sx = Math.Min(w - 1, bx * 4 + x), sy = Math.Min(h - 1, by * 4 + y);
                            int s = (sy * w + sx) * 4, d = (y * 4 + x) * 4;
                            blk[d] = rgba[s]; blk[d + 1] = rgba[s + 1]; blk[d + 2] = rgba[s + 2]; blk[d + 3] = rgba[s + 3];
                        }
                    int at = (by * bw + bx) * bb;
                    if (dxt5) { AlphaBlock(blk, o, at); ColorBlock(blk, o, at + 8, false); }
                    else ColorBlock(blk, o, at, true);
                }
            return o;
        }

        static int To565(int r, int g, int b) { return ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255); }
        static void From565(int c, out int r, out int g, out int b)
        {
            r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31;
            r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
        }

        static void ColorBlock(byte[] blk, byte[] o, int at, bool dxt1)
        {
            bool punch = false;
            if (dxt1) for (int i = 0; i < 16; i++) if (blk[i * 4 + 3] < 128) { punch = true; break; }
            int r0 = 255, g0 = 255, b0 = 255, r1 = 0, g1 = 0, b1 = 0, opaque = 0;
            for (int i = 0; i < 16; i++)
            {
                if (punch && blk[i * 4 + 3] < 128) continue;
                opaque++;
                int r = blk[i * 4], g = blk[i * 4 + 1], b = blk[i * 4 + 2];
                if (r < r0) r0 = r; if (g < g0) g0 = g; if (b < b0) b0 = b;
                if (r > r1) r1 = r; if (g > g1) g1 = g; if (b > b1) b1 = b;
            }
            if (opaque == 0) { r0 = g0 = b0 = r1 = g1 = b1 = 0; }
            // inset the box by 1/16 of its range (fewer extreme colours wasted on outliers)
            int ir = (r1 - r0) >> 4, ig = (g1 - g0) >> 4, ib = (b1 - b0) >> 4;
            r0 = Math.Min(255, r0 + ir); g0 = Math.Min(255, g0 + ig); b0 = Math.Min(255, b0 + ib);
            r1 = Math.Max(0, r1 - ir); g1 = Math.Max(0, g1 - ig); b1 = Math.Max(0, b1 - ib);
            int cMax = To565(r1, g1, b1), cMin = To565(r0, g0, b0);
            int c0, c1;
            if (punch) { c0 = Math.Min(cMax, cMin); c1 = Math.Max(cMax, cMin); }   // c0 <= c1: 3 colours + transparent
            else { c0 = Math.Max(cMax, cMin); c1 = Math.Min(cMax, cMin); }         // c0 > c1: 4 colours (equal = 1 colour)
            int pr0, pg0, pb0, pr1, pg1, pb1;
            From565(c0, out pr0, out pg0, out pb0); From565(c1, out pr1, out pg1, out pb1);
            var pal = new int[12];
            pal[0] = pr0; pal[1] = pg0; pal[2] = pb0; pal[3] = pr1; pal[4] = pg1; pal[5] = pb1;
            int colours;
            if (!punch && c0 > c1)
            {
                pal[6] = (2 * pr0 + pr1) / 3; pal[7] = (2 * pg0 + pg1) / 3; pal[8] = (2 * pb0 + pb1) / 3;
                pal[9] = (pr0 + 2 * pr1) / 3; pal[10] = (pg0 + 2 * pg1) / 3; pal[11] = (pb0 + 2 * pb1) / 3;
                colours = 4;
            }
            else
            {
                pal[6] = (pr0 + pr1) / 2; pal[7] = (pg0 + pg1) / 2; pal[8] = (pb0 + pb1) / 2;
                colours = 3;
            }
            uint bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int idx;
                if (punch && blk[i * 4 + 3] < 128) idx = 3;
                else if (c0 == c1) idx = 0;
                else
                {
                    int r = blk[i * 4], g = blk[i * 4 + 1], b = blk[i * 4 + 2], best = int.MaxValue;
                    idx = 0;
                    for (int k = 0; k < colours; k++)
                    {
                        int dr = r - pal[k * 3], dg = g - pal[k * 3 + 1], db = b - pal[k * 3 + 2];
                        int dd = dr * dr * 3 + dg * dg * 4 + db * db * 2;   // ours: rough luminance weights
                        if (dd < best) { best = dd; idx = k; }
                    }
                }
                bits |= (uint)idx << (i * 2);
            }
            o[at] = (byte)c0; o[at + 1] = (byte)(c0 >> 8); o[at + 2] = (byte)c1; o[at + 3] = (byte)(c1 >> 8);
            o[at + 4] = (byte)bits; o[at + 5] = (byte)(bits >> 8); o[at + 6] = (byte)(bits >> 16); o[at + 7] = (byte)(bits >> 24);
        }

        static void AlphaBlock(byte[] blk, byte[] o, int at)
        {
            int amin = 255, amax = 0;
            for (int i = 0; i < 16; i++) { int a = blk[i * 4 + 3]; if (a < amin) amin = a; if (a > amax) amax = a; }
            o[at] = (byte)amax; o[at + 1] = (byte)amin;
            ulong bits = 0;
            if (amax > amin)
            {
                // a0 = max > a1 = min: 8-level mode. Level L (0 = max .. 7 = min) -> index 0 for L 0, 1 for L 7, L + 1 otherwise
                for (int i = 0; i < 16; i++)
                {
                    int L = ((amax - blk[i * 4 + 3]) * 7 + (amax - amin) / 2) / (amax - amin);
                    int idx = L == 0 ? 0 : L == 7 ? 1 : L + 1;
                    bits |= (ulong)idx << (i * 3);
                }
            }
            for (int k = 0; k < 6; k++) o[at + 2 + k] = (byte)(bits >> (8 * k));
        }
    }
}
