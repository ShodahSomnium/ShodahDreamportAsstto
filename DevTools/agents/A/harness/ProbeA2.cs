using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// A's private probe (polish pass): octave-band levels and discontinuity counts of a WAV window, to compare the lab's render with the harness's.
static partial class Program
{
    // wavspec <file.wav> <t0> <t1>
    static int Cmd_wavspec(List<string> a)
    {
        string path = a[1]; float t0 = float.Parse(a[2], Iv), t1 = float.Parse(a[3], Iv);
        byte[] b = File.ReadAllBytes(path);
        int ch = BitConverter.ToInt16(b, 22), rate = BitConverter.ToInt32(b, 24);
        int data = 12;
        while (data < b.Length - 8 && !(b[data] == 'd' && b[data + 1] == 'a' && b[data + 2] == 't' && b[data + 3] == 'a')) data += 8 + BitConverter.ToInt32(b, data + 4);
        int start = data + 8, frames = (b.Length - start) / (2 * ch);
        var st = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float l = BitConverter.ToInt16(b, start + i * 2 * ch) / 32768f;
            float r = ch > 1 ? BitConverter.ToInt16(b, start + i * 2 * ch + 2) / 32768f : l;
            st[2 * i] = l; st[2 * i + 1] = r;
        }
        Spectrum(st, rate, t0, t1);
        return 0;
    }

    static void Spectrum(float[] st, int rate, float t0, float t1)
    {
        int n = 32768, s0 = (int)(t0 * rate);
        int avail = (int)((t1 - t0) * rate);
        while (n > avail && n > 1024) n >>= 1;
        var re = new double[n]; var im = new double[n];
        int jumps = 0; float maxJump = 0f;
        for (int i = 0; i < n; i++)
        {
            int k = s0 + i; if (2 * k + 1 >= st.Length) break;
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            re[i] = (st[2 * k] + st[2 * k + 1]) * 0.5 * w;
            if (k > 0) { float d = Math.Abs(st[2 * k] - st[2 * k - 2]); if (d > maxJump) maxJump = d; if (d > 0.05f) jumps++; }
        }
        Fft(re, im);
        double[] edges = { 31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000, 22050 };
        var line = new System.Text.StringBuilder();
        for (int e = 0; e + 1 < edges.Length; e++)
        {
            double sum = 0;
            for (int k = 1; k < n / 2; k++) { double f = k * (double)rate / n; if (f >= edges[e] && f < edges[e + 1]) sum += re[k] * re[k] + im[k] * im[k]; }
            line.Append(((int)edges[e]).ToString().PadLeft(6)).Append(":").Append((sum > 0 ? 10 * Math.Log10(sum) : -999).ToString("0.0", Iv).PadLeft(7));
        }
        float rms, pk; AcOfflineMix.Levels(st, t0, t1, out rms, out pk);
        Console.WriteLine("t " + t0 + "-" + t1 + " rms " + AcOfflineMix.ToDb(rms).ToString("0.0", Iv) + " dBFS, " + AcOfflineMix.ToDb(AcOfflineMix.RmsA(st, t0, t1)).ToString("0.0", Iv)
            + " dB(A)FS, sample jumps > 0.05: " + jumps + " (max " + maxJump.ToString("0.000", Iv) + ")");
        Console.WriteLine("  octave bands (dB, relative):" + line);
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int p = i + k, q = p + len / 2;
                    double xr = re[q] * cr - im[q] * ci, xi = re[q] * ci + im[q] * cr;
                    re[q] = re[p] - xr; im[q] = im[p] - xi; re[p] += xr; im[p] += xi;
                    double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                }
            }
        }
    }
}
