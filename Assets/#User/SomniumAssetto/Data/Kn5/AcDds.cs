using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Pixel formats found in AC textures (census of every car and track kn5 in ac_car_formats 1.7 / ac_track_formats 3.5).</summary>
    public enum AcTexFormat
    {
        Unknown = 0,
        DXT1, DXT3, DXT5,          // BC1 / BC2 / BC3 (Unity has no DXT3: decode on the CPU)
        BC4, BC5, BC6H, BC7,
        BGRA32,                    // B8G8R8A8 (pf 0x41, masks R FF0000 A FF000000)
        BGRX32,                    // B8G8R8X8 (no alpha)
        RGBA32,                    // R8G8B8A8 (R mask FF)
        RGBX32,
        BGR24,                     // B8G8R8 24-bit (R mask FF0000) - the common "RGB24" DDS
        RGB24,                     // R8G8B8 24-bit (R mask FF)
        R5G6B5, X1R5G5B5, A1R5G5B5, A4R4G4B4, X4R4G4B4,
        L8, L8A8, A8, L16,
        P8,                        // 8-bit palette-indexed (pf 0x20): 256 x RGBA palette after the header (mercedes_sls belt)
        Png, Jpg,                  // passthrough: decode with ImageConversion.LoadImage (rows upright: flip for uv = (u, v_stored))
    }

    /// <summary>Parsed header of an embedded texture file (DDS or PNG/JPG passthrough).</summary>
    public sealed class AcDdsInfo
    {
        public AcTexFormat Format;
        public int Width, Height, Depth = 1;
        /// <summary>Mip levels in the file (&gt;= 1). DXT chains are often truncated (2048x2048 with 5 mips).</summary>
        public int MipCount = 1;
        public bool IsCubemap, IsDx10;
        /// <summary>Byte offset of the first pixel (128, or 148 with a DX10 header). 0 for PNG/JPG.</summary>
        public int DataOffset;
        public uint PixelFlags, FourCC, BitCount, RMask, GMask, BMask, AMask;
        public int DxgiFormat;
        /// <summary>P8 only: byte offset of the 256 x RGBA palette (128); pixels start at DataOffset (1152).</summary>
        public int PaletteOffset;
        /// <summary>Total file size if known (to check the data is complete).</summary>
        public int FileSize;

        public bool IsDds { get { return DataOffset > 0; } }
        /// <summary>PNG/JPG embedded instead of a DDS: hand the raw bytes to ImageConversion.LoadImage.</summary>
        public bool IsPassthrough { get { return Format == AcTexFormat.Png || Format == AcTexFormat.Jpg; } }
        public bool IsBlockCompressed { get { return AcDds.BlockBytes(Format) > 0; } }
        public string FourCCText { get { return FourCC == 0 ? "" : new string(new[] { (char)(FourCC & 0xFF), (char)((FourCC >> 8) & 0xFF), (char)((FourCC >> 16) & 0xFF), (char)(FourCC >> 24) }); } }

        /// <summary>Bytes of mip level <paramref name="level"/> (one face).</summary>
        public int MipBytes(int level)
        {
            int w = Math.Max(1, Width >> level), h = Math.Max(1, Height >> level);
            int bb = AcDds.BlockBytes(Format);
            if (bb > 0) return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * bb;
            return w * h * AcDds.PixelBytes(Format);
        }

        /// <summary>Bytes of all mips of one face (what LoadRawTextureData expects for a 2D texture with MipCount mips).</summary>
        public int ChainBytes { get { int s = 0; for (int i = 0; i < MipCount; i++) s += MipBytes(i); return s; } }

        /// <summary>Data shorter than the header promises (some files: pad or drop mips). False when the file size is unknown.</summary>
        public bool Truncated { get { return IsDds && FileSize > 0 && FileSize - DataOffset < ChainBytes * (IsCubemap ? 6 : 1); } }

        public override string ToString() { return Format + " " + Width + "x" + Height + " mips " + MipCount + (IsCubemap ? " cube" : "") + (IsDx10 ? " dx10" : ""); }
    }

    /// <summary>
    /// DDS header parsing and CPU decoding to RGBA32 (Unity-free). Header: magic "DDS ", dwHeight@12, dwWidth@16, mipMapCount@28,
    /// pfFlags@80, fourCC@84, rgbBitCount@88, masks@92/96/100/104, caps2@112, data@128 (+20 with a "DX10" header).
    /// AC never ships DX10 headers or cubemaps in kn5 (both handled anyway). PNG/JPG inside a kn5 are flagged passthrough.
    /// <para>Orientation: DDS row 0 = image top; uploaded raw with LoadRawTextureData it lands at Unity v = 0, which matches the kn5
    /// stored uv (u, v_stored) with Repeat wrap [VERIFIED data side]. <see cref="DecodeRgba32"/> keeps that file row order.</para>
    /// </summary>
    public static class AcDds
    {
        const uint DDPF_ALPHAPIXELS = 0x1, DDPF_ALPHA = 0x2, DDPF_FOURCC = 0x4, DDPF_PALETTEINDEXED8 = 0x20, DDPF_RGB = 0x40, DDPF_LUMINANCE = 0x20000;

        static uint U32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
        static uint Cc(string s) { return (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24)); }

        /// <summary>Bytes per 4x4 block of a compressed format, 0 otherwise.</summary>
        public static int BlockBytes(AcTexFormat f)
        {
            switch (f)
            {
                case AcTexFormat.DXT1: case AcTexFormat.BC4: return 8;
                case AcTexFormat.DXT3: case AcTexFormat.DXT5: case AcTexFormat.BC5: case AcTexFormat.BC6H: case AcTexFormat.BC7: return 16;
                default: return 0;
            }
        }

        /// <summary>Bytes per pixel of an uncompressed format, 0 for compressed/unknown.</summary>
        public static int PixelBytes(AcTexFormat f)
        {
            switch (f)
            {
                case AcTexFormat.BGRA32: case AcTexFormat.BGRX32: case AcTexFormat.RGBA32: case AcTexFormat.RGBX32: return 4;
                case AcTexFormat.BGR24: case AcTexFormat.RGB24: return 3;
                case AcTexFormat.R5G6B5: case AcTexFormat.X1R5G5B5: case AcTexFormat.A1R5G5B5: case AcTexFormat.A4R4G4B4:
                case AcTexFormat.X4R4G4B4: case AcTexFormat.L8A8: case AcTexFormat.L16: return 2;
                case AcTexFormat.L8: case AcTexFormat.A8: case AcTexFormat.P8: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Parses a texture header. <paramref name="count"/> may be just the first 148 bytes; pass the real file size as
        /// <paramref name="fileSize"/> to get <see cref="AcDdsInfo.Truncated"/>. Never throws: unknown data gives Format Unknown.
        /// </summary>
        public static AcDdsInfo Parse(byte[] b, int offset, int count, int fileSize = -1)
        {
            var info = new AcDdsInfo { FileSize = fileSize >= 0 ? fileSize : count };
            if (b == null || count < 4) return info;
            int o = offset;
            // PNG: 89 50 4E 47; IHDR width/height big-endian at 16/20
            if (b[o] == 0x89 && b[o + 1] == 0x50 && b[o + 2] == 0x4E && b[o + 3] == 0x47)
            {
                info.Format = AcTexFormat.Png;
                if (count >= 24)
                {
                    info.Width = (b[o + 16] << 24) | (b[o + 17] << 16) | (b[o + 18] << 8) | b[o + 19];
                    info.Height = (b[o + 20] << 24) | (b[o + 21] << 16) | (b[o + 22] << 8) | b[o + 23];
                }
                return info;
            }
            if (b[o] == 0xFF && b[o + 1] == 0xD8) { info.Format = AcTexFormat.Jpg; return info; }
            if (count < 128 || b[o] != 'D' || b[o + 1] != 'D' || b[o + 2] != 'S' || b[o + 3] != ' ') return info;

            info.Height = (int)U32(b, o + 12);
            info.Width = (int)U32(b, o + 16);
            uint depth = U32(b, o + 24);
            info.Depth = depth > 1 ? (int)depth : 1;
            info.MipCount = Math.Max(1, (int)U32(b, o + 28));
            info.PixelFlags = U32(b, o + 80);
            info.FourCC = U32(b, o + 84);
            info.BitCount = U32(b, o + 88);
            info.RMask = U32(b, o + 92); info.GMask = U32(b, o + 96); info.BMask = U32(b, o + 100); info.AMask = U32(b, o + 104);
            uint caps2 = U32(b, o + 112);
            info.IsCubemap = (caps2 & 0x200) != 0;
            info.DataOffset = 128;

            uint pf = info.PixelFlags;
            if ((pf & DDPF_FOURCC) != 0)
            {
                uint cc = info.FourCC;
                if (cc == Cc("DXT1")) info.Format = AcTexFormat.DXT1;
                else if (cc == Cc("DXT2") || cc == Cc("DXT3")) info.Format = AcTexFormat.DXT3;
                else if (cc == Cc("DXT4") || cc == Cc("DXT5")) info.Format = AcTexFormat.DXT5;
                else if (cc == Cc("ATI1") || cc == Cc("BC4U") || cc == Cc("BC4S")) info.Format = AcTexFormat.BC4;
                else if (cc == Cc("ATI2") || cc == Cc("BC5U") || cc == Cc("BC5S")) info.Format = AcTexFormat.BC5;
                else if (cc == Cc("DX10") && count >= 148)
                {
                    info.IsDx10 = true;
                    info.DataOffset = 148;
                    info.DxgiFormat = (int)U32(b, o + 128);
                    uint misc = U32(b, o + 136);
                    if ((misc & 0x4) != 0) info.IsCubemap = true;
                    info.Format = FromDxgi(info.DxgiFormat);
                }
                else if (cc == 21) info.Format = AcTexFormat.BGRA32;     // D3DFMT_A8R8G8B8 as fourCC
                else if (cc == 22) info.Format = AcTexFormat.BGRX32;
                else if (cc == 20) info.Format = AcTexFormat.BGR24;
                else if (cc == 23) info.Format = AcTexFormat.R5G6B5;
                else if (cc == 25) info.Format = AcTexFormat.A1R5G5B5;
                else if (cc == 26) info.Format = AcTexFormat.A4R4G4B4;
                else if (cc == 32) info.Format = AcTexFormat.RGBA32;     // D3DFMT_A8B8G8R8
                else if (cc == 50) info.Format = AcTexFormat.L8;
                else if (cc == 51) info.Format = AcTexFormat.L8A8;
                else if (cc == 28) info.Format = AcTexFormat.A8;
            }
            else
            {
                bool alpha = (pf & DDPF_ALPHAPIXELS) != 0 && info.AMask != 0;
                uint bits = info.BitCount, r = info.RMask, g = info.GMask, bl = info.BMask, a = info.AMask;
                if ((pf & DDPF_RGB) != 0)
                {
                    if (bits == 32 && r == 0xFF0000 && g == 0xFF00 && bl == 0xFF) info.Format = alpha ? AcTexFormat.BGRA32 : AcTexFormat.BGRX32;
                    else if (bits == 32 && r == 0xFF && g == 0xFF00 && bl == 0xFF0000) info.Format = alpha ? AcTexFormat.RGBA32 : AcTexFormat.RGBX32;
                    else if (bits == 24 && r == 0xFF0000 && g == 0xFF00 && bl == 0xFF) info.Format = AcTexFormat.BGR24;
                    else if (bits == 24 && r == 0xFF && g == 0xFF00 && bl == 0xFF0000) info.Format = AcTexFormat.RGB24;
                    else if (bits == 16 && r == 0xF800 && g == 0x7E0 && bl == 0x1F) info.Format = AcTexFormat.R5G6B5;
                    else if (bits == 16 && r == 0x7C00 && g == 0x3E0 && bl == 0x1F) info.Format = alpha && a == 0x8000 ? AcTexFormat.A1R5G5B5 : AcTexFormat.X1R5G5B5;
                    else if (bits == 16 && r == 0xF00 && g == 0xF0 && bl == 0xF) info.Format = alpha && a == 0xF000 ? AcTexFormat.A4R4G4B4 : AcTexFormat.X4R4G4B4;
                }
                else if ((pf & DDPF_LUMINANCE) != 0)
                {
                    if (bits == 8) info.Format = AcTexFormat.L8;
                    else if (bits == 16 && alpha) info.Format = AcTexFormat.L8A8;
                    else if (bits == 16) info.Format = AcTexFormat.L16;
                }
                else if ((pf & DDPF_ALPHA) != 0 && bits == 8) info.Format = AcTexFormat.A8;
                else if ((pf & DDPF_PALETTEINDEXED8) != 0 && bits == 8) { info.Format = AcTexFormat.P8; info.PaletteOffset = 128; info.DataOffset = 128 + 1024; }
            }
            return info;
        }

        static AcTexFormat FromDxgi(int f)
        {
            switch (f)
            {
                case 70: case 71: case 72: return AcTexFormat.DXT1;
                case 73: case 74: case 75: return AcTexFormat.DXT3;
                case 76: case 77: case 78: return AcTexFormat.DXT5;
                case 79: case 80: case 81: return AcTexFormat.BC4;
                case 82: case 83: case 84: return AcTexFormat.BC5;
                case 94: case 95: case 96: return AcTexFormat.BC6H;
                case 97: case 98: case 99: return AcTexFormat.BC7;
                case 27: case 28: case 29: return AcTexFormat.RGBA32;
                case 87: case 90: case 91: return AcTexFormat.BGRA32;
                case 88: case 92: case 93: return AcTexFormat.BGRX32;
                case 85: return AcTexFormat.R5G6B5;
                case 86: return AcTexFormat.A1R5G5B5;
                case 115: return AcTexFormat.A4R4G4B4;
                case 61: return AcTexFormat.L8;      // R8_UNORM
                case 65: return AcTexFormat.A8;
                default: return AcTexFormat.Unknown;
            }
        }

        /// <summary>Byte offset of mip <paramref name="level"/> (face 0) inside the file.</summary>
        public static int MipOffset(AcDdsInfo info, int level)
        {
            int o = info.DataOffset;
            for (int i = 0; i < level; i++) o += info.MipBytes(i);
            return o;
        }

        /// <summary>
        /// Decodes one mip level of a DDS (any format except BC6H/BC7) to RGBA32, rows in FILE order (row 0 = image top).
        /// Returns null if the format is unsupported or the data is short. Worker-thread safe. Use it for DXT3 (no Unity
        /// format), the 16-bit / luminance formats, odd sizes, and previews; DXT1/DXT5/BGRA32 can be uploaded raw instead.
        /// </summary>
        public static byte[] DecodeRgba32(byte[] dds, AcDdsInfo info, int level, out int width, out int height)
        {
            width = height = 0;
            if (info == null || dds == null || !info.IsDds || level >= info.MipCount) return null;
            width = Math.Max(1, info.Width >> level); height = Math.Max(1, info.Height >> level);
            int src = MipOffset(info, level);
            int need = info.MipBytes(level);
            if (src + need > dds.Length) return null;
            int w = width, h = height;
            var o = new byte[w * h * 4];
            int bb = BlockBytes(info.Format);
            if (bb > 0)
            {
                if (info.Format == AcTexFormat.BC6H || info.Format == AcTexFormat.BC7) return null;
                int bw = (w + 3) / 4, bh = (h + 3) / 4;
                var block = new byte[64];
                for (int by = 0; by < bh; by++)
                    for (int bx = 0; bx < bw; bx++)
                    {
                        int p = src + (by * bw + bx) * bb;
                        DecodeBlock(info.Format, dds, p, block);
                        for (int y = 0; y < 4; y++)
                        {
                            int py = by * 4 + y; if (py >= h) break;
                            for (int x = 0; x < 4; x++)
                            {
                                int px = bx * 4 + x; if (px >= w) break;
                                int d = (py * w + px) * 4, s = (y * 4 + x) * 4;
                                o[d] = block[s]; o[d + 1] = block[s + 1]; o[d + 2] = block[s + 2]; o[d + 3] = block[s + 3];
                            }
                        }
                    }
                return o;
            }
            int pbytes = PixelBytes(info.Format);
            if (pbytes == 0) return null;
            for (int i = 0, p = src, d = 0; i < w * h; i++, p += pbytes, d += 4)
            {
                byte r, g, b, a = 255;
                switch (info.Format)
                {
                    case AcTexFormat.BGRA32: b = dds[p]; g = dds[p + 1]; r = dds[p + 2]; a = dds[p + 3]; break;
                    case AcTexFormat.BGRX32: b = dds[p]; g = dds[p + 1]; r = dds[p + 2]; break;
                    case AcTexFormat.RGBA32: r = dds[p]; g = dds[p + 1]; b = dds[p + 2]; a = dds[p + 3]; break;
                    case AcTexFormat.RGBX32: r = dds[p]; g = dds[p + 1]; b = dds[p + 2]; break;
                    case AcTexFormat.BGR24: b = dds[p]; g = dds[p + 1]; r = dds[p + 2]; break;
                    case AcTexFormat.RGB24: r = dds[p]; g = dds[p + 1]; b = dds[p + 2]; break;
                    case AcTexFormat.L8: r = g = b = dds[p]; break;
                    case AcTexFormat.A8: r = g = b = 255; a = dds[p]; break;
                    case AcTexFormat.L8A8: r = g = b = dds[p]; a = dds[p + 1]; break;
                    case AcTexFormat.L16: r = g = b = dds[p + 1]; break;
                    case AcTexFormat.P8: { int pi = info.PaletteOffset + dds[p] * 4; r = dds[pi]; g = dds[pi + 1]; b = dds[pi + 2]; break; } // D3DFMT_P8: palette alpha unused
                    default:
                    {
                        int v = dds[p] | (dds[p + 1] << 8);
                        switch (info.Format)
                        {
                            case AcTexFormat.R5G6B5: r = X5((v >> 11) & 31); g = X6((v >> 5) & 63); b = X5(v & 31); break;
                            case AcTexFormat.X1R5G5B5: r = X5((v >> 10) & 31); g = X5((v >> 5) & 31); b = X5(v & 31); break;
                            case AcTexFormat.A1R5G5B5: r = X5((v >> 10) & 31); g = X5((v >> 5) & 31); b = X5(v & 31); a = (byte)((v & 0x8000) != 0 ? 255 : 0); break;
                            case AcTexFormat.A4R4G4B4: r = (byte)(((v >> 8) & 15) * 17); g = (byte)(((v >> 4) & 15) * 17); b = (byte)((v & 15) * 17); a = (byte)(((v >> 12) & 15) * 17); break;
                            default: r = (byte)(((v >> 8) & 15) * 17); g = (byte)(((v >> 4) & 15) * 17); b = (byte)((v & 15) * 17); break; // X4R4G4B4
                        }
                        break;
                    }
                }
                o[d] = r; o[d + 1] = g; o[d + 2] = b; o[d + 3] = a;
            }
            return o;
        }

        static byte X5(int v) { return (byte)((v << 3) | (v >> 2)); }
        static byte X6(int v) { return (byte)((v << 2) | (v >> 4)); }

        /// <summary>Decodes one 4x4 block into 64 bytes RGBA (row-major 4x4).</summary>
        static void DecodeBlock(AcTexFormat f, byte[] b, int p, byte[] o)
        {
            switch (f)
            {
                case AcTexFormat.DXT1: Color565Block(b, p, o, true); break;
                case AcTexFormat.DXT3:
                    Color565Block(b, p + 8, o, false);
                    for (int i = 0; i < 16; i++) { int a4 = (b[p + (i >> 1)] >> ((i & 1) * 4)) & 15; o[i * 4 + 3] = (byte)(a4 * 17); }
                    break;
                case AcTexFormat.DXT5:
                    Color565Block(b, p + 8, o, false);
                    AlphaBlock(b, p, o, 3);
                    break;
                case AcTexFormat.BC4:
                    AlphaBlock(b, p, o, 0);
                    for (int i = 0; i < 16; i++) { o[i * 4 + 1] = o[i * 4]; o[i * 4 + 2] = o[i * 4]; o[i * 4 + 3] = 255; }
                    break;
                case AcTexFormat.BC5:
                    AlphaBlock(b, p, o, 0);
                    AlphaBlock(b, p + 8, o, 1);
                    for (int i = 0; i < 16; i++) { o[i * 4 + 2] = 0; o[i * 4 + 3] = 255; }
                    break;
            }
        }

        static void Color565Block(byte[] b, int p, byte[] o, bool dxt1)
        {
            int c0 = b[p] | (b[p + 1] << 8), c1 = b[p + 2] | (b[p + 3] << 8);
            int r0 = X5((c0 >> 11) & 31), g0 = X6((c0 >> 5) & 63), b0 = X5(c0 & 31);
            int r1 = X5((c1 >> 11) & 31), g1 = X6((c1 >> 5) & 63), b1 = X5(c1 & 31);
            int cr2, cg2, cb2, cr3, cg3, cb3, a3 = 255;
            if (c0 > c1 || !dxt1)
            {
                cr2 = (2 * r0 + r1) / 3; cg2 = (2 * g0 + g1) / 3; cb2 = (2 * b0 + b1) / 3;
                cr3 = (r0 + 2 * r1) / 3; cg3 = (g0 + 2 * g1) / 3; cb3 = (b0 + 2 * b1) / 3;
            }
            else
            {
                cr2 = (r0 + r1) / 2; cg2 = (g0 + g1) / 2; cb2 = (b0 + b1) / 2;
                cr3 = 0; cg3 = 0; cb3 = 0; a3 = 0;
            }
            uint bits = (uint)(b[p + 4] | (b[p + 5] << 8) | (b[p + 6] << 16) | (b[p + 7] << 24));
            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((bits >> (i * 2)) & 3), d = i * 4;
                switch (idx)
                {
                    case 0: o[d] = (byte)r0; o[d + 1] = (byte)g0; o[d + 2] = (byte)b0; o[d + 3] = 255; break;
                    case 1: o[d] = (byte)r1; o[d + 1] = (byte)g1; o[d + 2] = (byte)b1; o[d + 3] = 255; break;
                    case 2: o[d] = (byte)cr2; o[d + 1] = (byte)cg2; o[d + 2] = (byte)cb2; o[d + 3] = 255; break;
                    default: o[d] = (byte)cr3; o[d + 1] = (byte)cg3; o[d + 2] = (byte)cb3; o[d + 3] = (byte)a3; break;
                }
            }
        }

        /// <summary>BC3 alpha / BC4 channel block (8 bytes) into channel <paramref name="ch"/> of the 64-byte output.</summary>
        static void AlphaBlock(byte[] b, int p, byte[] o, int ch)
        {
            int a0 = b[p], a1 = b[p + 1];
            ulong bits = 0;
            for (int i = 0; i < 6; i++) bits |= (ulong)b[p + 2 + i] << (8 * i);
            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((bits >> (3 * i)) & 7), v;
                if (idx == 0) v = a0;
                else if (idx == 1) v = a1;
                else if (a0 > a1) v = ((8 - idx) * a0 + (idx - 1) * a1) / 7;
                else if (idx == 6) v = 0;
                else if (idx == 7) v = 255;
                else v = ((6 - idx) * a0 + (idx - 1) * a1) / 5;
                o[i * 4 + ch] = (byte)v;
            }
        }
    }
}
