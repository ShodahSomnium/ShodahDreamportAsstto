using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// RIFF WAVE files: read the install's UI sounds (content/sfx/gui_click.wav, launcher/themes/default/audio/*.wav) into interleaved
    /// floats for AudioClip.Create + SetData; write PCM16 mixes (harness / lab renders). PCM 8/16/24/32-bit and IEEE float 32,
    /// WAVE_FORMAT_EXTENSIBLE. Unity-free.
    /// </summary>
    public sealed class AcWav
    {
        public int Rate, Channels;
        public float[] Samples;   // interleaved
        public int Frames { get { return Channels > 0 && Samples != null ? Samples.Length / Channels : 0; } }

        public static AcWav Read(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            return Parse(b);
        }

        public static AcWav Parse(byte[] b)
        {
            if (b == null || b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E')
                throw new InvalidDataException("not a RIFF WAVE");
            int p = 12, fmt = 0, ch = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            while (p + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                int sz = BitConverter.ToInt32(b, p + 4);
                int d = p + 8;
                if (sz < 0) break;
                if (id == "fmt " && sz >= 16)
                {
                    fmt = BitConverter.ToUInt16(b, d); ch = BitConverter.ToUInt16(b, d + 2); rate = BitConverter.ToInt32(b, d + 4); bits = BitConverter.ToUInt16(b, d + 14);
                    if (fmt == 0xFFFE && sz >= 26) fmt = BitConverter.ToUInt16(b, d + 24); // extensible: sub-format GUID's first u16
                }
                else if (id == "data") { dataAt = d; dataLen = Math.Min(sz, b.Length - d); break; }
                p = d + sz + (sz & 1);
            }
            if (dataAt < 0 || ch <= 0 || rate <= 0) throw new InvalidDataException("WAVE without fmt/data");
            int bps = bits / 8;
            if (bps <= 0) throw new InvalidDataException("WAVE bits " + bits);
            int n = dataLen / bps;
            n -= n % ch;
            var o = new float[n];
            for (int i = 0, q = dataAt; i < n; i++, q += bps)
            {
                if (fmt == 3 && bps == 4) o[i] = BitConverter.ToSingle(b, q);
                else if (bps == 1) o[i] = (b[q] - 128) / 128f;
                else if (bps == 2) o[i] = (short)(b[q] | (b[q + 1] << 8)) / 32768f;
                else if (bps == 3) o[i] = ((b[q] << 8 | b[q + 1] << 16 | b[q + 2] << 24) >> 8) / 8388608f;
                else if (bps == 4) o[i] = BitConverter.ToInt32(b, q) / 2147483648f;
            }
            return new AcWav { Rate = rate, Channels = ch, Samples = o };
        }

        /// <summary>Writes interleaved floats as a PCM16 WAVE (clipped to [-1, 1]).</summary>
        public static void WritePcm16(string path, float[] samples, int count, int channels, int rate)
        {
            using (var f = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(f))
            {
                int bytes = count * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + bytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(bytes);
                for (int i = 0; i < count; i++)
                {
                    float v = samples[i];
                    v = v < -1f ? -1f : v > 1f ? 1f : v;
                    w.Write((short)Math.Round(v * 32767f));
                }
            }
        }
    }
}
