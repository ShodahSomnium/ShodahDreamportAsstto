using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One sample of an FSB5 sound bank.</summary>
    public sealed class Fsb5Sample
    {
        public int Index;
        /// <summary>Name from the name table with any " {guid}" suffix cut (newer builds append it); "#i" when the bank has no names.</summary>
        public string Name;
        public int Rate, Channels;
        public long Frames;
        /// <summary>Absolute byte offset of the sample data in the file/stream the bank was read from.</summary>
        public long DataOffset;
        /// <summary>Loop points in frames from a type-3 chunk; -1 when absent (AC banks: looping is set per instrument instead).</summary>
        public long LoopStart = -1, LoopEnd = -1;

        public double Seconds { get { return Rate > 0 ? (double)Frames / Rate : 0; } }
        /// <summary>PCM16 byte length (frames * 2 * channels).</summary>
        public long Pcm16Bytes { get { return Frames * 2 * Channels; } }
        public override string ToString() { return Name + " " + Rate + "Hz " + Channels + "ch " + Frames + " frames"; }
    }

    /// <summary>
    /// FSB5 container (inside an FMOD .bank's SND chunk, or a bare .fsb) - headers, names and PCM16 sample extraction.
    ///
    /// [VERIFIED on 86/86 car banks + common.bank + sdk formula_k.bank; a sample pulled from formula_k.bank is byte-identical to the
    /// SDK source wav]: header 0x3C bytes (version 1; 0x40 for version 0): "FSB5", u32 version, u32 sampleCount, u32 headerBlockSize,
    /// u32 nameTableSize, u32 dataSize, u32 <b>mode at +0x18</b> (2 = PCM16 in every AC bank; 15 = Vorbis, 16 = FADPCM not decoded here).
    /// Sample header: u64 bit 0 = extra chunks follow, bits 1-4 rate index, 5-6 channel code, 7-33 data offset / 32, 34-63 frames;
    /// chunks u32 (bit 0 more, bits 1-24 size, 25-31 type: 1 channels, 2 rate, 3 loop start/end, others skipped).
    /// Name table: u32 offsets (relative to the table) then NUL-terminated ASCII names. Data base = header + headerBlock + names.
    /// </summary>
    public sealed class Fsb5
    {
        static readonly int[] Rates = { 4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };

        public int Version;
        public int Mode;
        public long Offset;            // absolute offset of "FSB5"
        public long DataBase;          // absolute offset of the sample data block
        public long DataSize;
        public readonly List<Fsb5Sample> Samples = new List<Fsb5Sample>();

        public bool IsPcm16 { get { return Mode == 2; } }

        /// <summary>Sample by name (case-insensitive), or null.</summary>
        public Fsb5Sample Find(string name)
        {
            for (int i = 0; i < Samples.Count; i++) if (string.Equals(Samples[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Samples[i];
            return null;
        }

        /// <summary>Reads the headers of the FSB5 at <paramref name="offset"/> (must point at "FSB5"). Sample data is not read.</summary>
        public static Fsb5 Read(AcBinary r, long offset)
        {
            r.Position = offset;
            if (r.ReadAscii(4) != "FSB5") throw new InvalidDataException("no FSB5 at " + offset);
            var f = new Fsb5 { Offset = offset };
            f.Version = r.ReadInt32();
            int num = r.ReadInt32();
            int shdr = r.ReadInt32();
            int nsz = r.ReadInt32();
            f.DataSize = r.ReadUInt32();
            f.Mode = r.ReadInt32();
            if (num < 0 || num > 100000 || shdr < 0 || nsz < 0) throw new InvalidDataException("FSB5: bad header counts");
            int hdr = f.Version == 0 ? 0x40 : 0x3C;
            long sp = offset + hdr, nameBase = sp + shdr;
            f.DataBase = nameBase + nsz;
            r.Position = sp;
            for (int i = 0; i < num; i++)
            {
                ulong m = r.ReadUInt64();
                var s = new Fsb5Sample { Index = i };
                bool more = (m & 1) != 0;
                int ri = (int)((m >> 1) & 0xF);
                s.Rate = ri < Rates.Length ? Rates[ri] : 44100;
                int cc = (int)((m >> 5) & 3);
                s.Channels = cc == 0 ? 1 : cc == 1 ? 2 : cc == 2 ? 6 : 8;
                s.DataOffset = f.DataBase + (long)(((m >> 7) & 0x7FFFFFF) << 5);
                s.Frames = (long)((m >> 34) & 0x3FFFFFFF);
                int guard = 0;
                while (more && guard++ < 64)
                {
                    uint c = r.ReadUInt32();
                    more = (c & 1) != 0;
                    int size = (int)((c >> 1) & 0xFFFFFF), type = (int)(c >> 25);
                    long next = r.Position + size;
                    if (type == 1 && size >= 1) s.Channels = r.ReadByte();
                    else if (type == 2 && size >= 4) s.Rate = r.ReadInt32();
                    else if (type == 3 && size >= 8) { s.LoopStart = r.ReadUInt32(); s.LoopEnd = r.ReadUInt32(); }
                    r.Position = next;
                }
                f.Samples.Add(s);
            }
            if (nsz > 0 && num > 0)
            {
                r.Position = nameBase;
                var offs = new int[num];
                r.ReadInts(offs, 0, num);
                byte[] names = new byte[nsz];
                r.Position = nameBase;
                r.ReadBytes(names, 0, nsz);
                for (int i = 0; i < num; i++)
                {
                    int s0 = offs[i], s1 = s0;
                    if (s0 < 0 || s0 >= nsz) { f.Samples[i].Name = "#" + i; continue; }
                    while (s1 < nsz && names[s1] != 0) s1++;
                    string n = Encoding.ASCII.GetString(names, s0, s1 - s0);
                    int g = n.IndexOf(" {", StringComparison.Ordinal);
                    f.Samples[i].Name = g >= 0 ? n.Substring(0, g) : n;
                }
            }
            else for (int i = 0; i < num; i++) f.Samples[i].Name = "#" + i;
            return f;
        }

        /// <summary>Finds the first "FSB5" at or after <paramref name="from"/> (within <paramref name="maxScan"/> bytes). -1 if absent.</summary>
        public static long FindMagic(AcBinary r, long from, long maxScan = 1 << 20)
        {
            r.Position = from;
            long end = Math.Min(r.Length - 4, from + maxScan);
            int a = -1, b = -1, c = -1;
            for (long p = from; p < end + 3; p++)
            {
                int d = r.ReadByte();
                if (a == 'F' && b == 'S' && c == 'B' && d == '5') return p - 3;
                a = b; b = c; c = d;
            }
            return -1;
        }

        /// <summary>Opens a bare .fsb or an FMOD .bank (finds the FSB5 inside) and reads the headers.</summary>
        public static Fsb5 ReadFile(string path)
        {
            using (var r = AcBinary.OpenFile(path, 1 << 16))
            {
                long at = FmodBank.FindFsb5(r);
                if (at < 0) throw new InvalidDataException("no FSB5 in " + path);
                return Read(r, at);
            }
        }

        /// <summary>Raw PCM16 bytes of a sample (little-endian, interleaved). Mode must be 2.</summary>
        public byte[] ReadPcm16Bytes(AcBinary r, Fsb5Sample s)
        {
            if (!IsPcm16) throw new NotSupportedException("FSB5 mode " + Mode + " is not PCM16");
            long n = s.Pcm16Bytes;
            if (n < 0 || n > int.MaxValue || s.DataOffset + n > r.Length) throw new InvalidDataException("FSB5 sample '" + s.Name + "' out of range");
            r.Position = s.DataOffset;
            return r.ReadBytes((int)n);
        }

        /// <summary>PCM16 as shorts (interleaved).</summary>
        public short[] ReadPcm16(AcBinary r, Fsb5Sample s)
        {
            var b = ReadPcm16Bytes(r, s);
            var o = new short[b.Length / 2];
            Buffer.BlockCopy(b, 0, o, 0, o.Length * 2);
            return o;
        }

        /// <summary>
        /// PCM16 -> float [-1, 1) interleaved, ready for AudioClip.Create(name, frames, channels, rate, false) + SetData.
        /// Worker-thread safe when each thread uses its own reader.
        /// </summary>
        public float[] ReadFloats(AcBinary r, Fsb5Sample s)
        {
            var b = ReadPcm16Bytes(r, s);
            var o = new float[b.Length / 2];
            for (int i = 0, p = 0; i < o.Length; i++, p += 2) o[i] = (short)(b[p] | (b[p + 1] << 8)) / 32768f;
            return o;
        }
    }
}
