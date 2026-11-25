using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Offline renderer of AC events (no Unity, no FMOD): mixes looping instruments into a stereo buffer with per-block gain / pitch from
    /// <see cref="AcSndEvent.Evaluate"/>, the way the runtime player drives AudioSources (gain ramped across a block, linear-interpolated
    /// resampling). Used by the harness (engine sweeps written as WAV, levels reported) and by the audio lab to render what the runtime
    /// asked its sources to play. Not for runtime use (allocates).
    /// </summary>
    public sealed class AcOfflineMix
    {
        public const int OutRate = 44100;
        public readonly AcSndBank Bank;
        readonly Dictionary<int, float[]> _pcm = new Dictionary<int, float[]>();

        /// <summary>Apply the event's master distortion (default on; off = the pre-effects model).</summary>
        public bool ApplyEffects = true;

        public AcOfflineMix(AcSndBank bank) { Bank = bank; }

        public float[] Pcm(int sample)
        {
            float[] f;
            if (!_pcm.TryGetValue(sample, out f)) { f = Bank.ReadSampleFloats(sample); _pcm[sample] = f; }
            return f;
        }

        /// <summary>One voice: mixes <paramref name="frames"/> output frames of a sample into <paramref name="dst"/> (stereo interleaved at
        /// <paramref name="dstFrame"/>), gain ramping g0 -> g1, playback rate <paramref name="rate"/> (1 = original pitch). Returns false when
        /// a one-shot ran out.</summary>
        public static bool MixVoice(float[] dst, int dstFrame, int frames, float[] src, int srcCh, int srcRate, ref double pos, float rate, float g0, float g1, bool loop)
        {
            if (src == null || srcCh <= 0) return false;
            int srcFrames = src.Length / srcCh;
            if (srcFrames < 2) return false;
            double step = rate * (double)srcRate / OutRate;
            for (int i = 0; i < frames; i++)
            {
                if (pos >= srcFrames - 1)
                {
                    if (!loop) return false;
                    pos -= srcFrames - 1;
                }
                int a = (int)pos; float t = (float)(pos - a);
                float g = g0 + (g1 - g0) * (i / (float)frames);
                float l, r;
                if (srcCh == 1) { l = r = src[a] + (src[a + 1] - src[a]) * t; }
                else { int q = a * srcCh; l = src[q] + (src[q + srcCh] - src[q]) * t; r = src[q + 1] + (src[q + 1 + srcCh] - src[q + 1]) * t; }
                int o = (dstFrame + i) * 2;
                dst[o] += l * g; dst[o + 1] += r * g;
                pos += step;
            }
            return true;
        }

        /// <summary>
        /// Renders an event's looping sheet instruments over a parameter schedule: <paramref name="values"/>(t, array) fills the event's
        /// parameter values for time t (seconds). Blocks of <paramref name="blockFrames"/> (512 = ~11.6 ms, a frame of the runtime).
        /// Loops start at a fixed offset (40 % of the sample) so renders are repeatable. Returns stereo interleaved floats.
        /// </summary>
        public float[] Render(AcSndEvent ev, float seconds, Action<float, float[]> values, int blockFrames = 512)
        {
            int total = (int)(seconds * OutRate);
            var dst = new float[total * 2];
            int n = ev.Instruments.Length;
            var pos = new double[n];
            var lastGain = new float[n];
            var vals = ev.NewValues();
            float down = 1f;
            AcSndFx dist = null;
            if (ApplyEffects) dist = MasterDistortion(ev, out down);
            if (dist == null) down = 1f;
            for (int i = 0; i < n; i++)
            {
                var s = Bank.Fsb.Samples[ev.Instruments[i].Samples[0]];
                pos[i] = s.Frames * 0.4;
            }
            for (int f = 0; f < total; f += blockFrames)
            {
                int len = Math.Min(blockFrames, total - f);
                values(f / (float)OutRate, vals);
                for (int i = 0; i < n; i++)
                {
                    var ins = ev.Instruments[i];
                    if (!ins.Loops && !ev.TimelineLoops) continue;
                    float g, p;
                    ev.Evaluate(i, vals, out g, out p);
                    if (g <= 0f && lastGain[i] <= 0f) continue;
                    var s = Bank.Fsb.Samples[ins.Samples[0]];
                    MixVoice(dst, f, len, Pcm(ins.Samples[0]), s.Channels, s.Rate, ref pos[i], Math.Max(0.01f, p), lastGain[i], g, true);
                    lastGain[i] = g;
                }
                if (dist != null) ApplyDistortion(dst, f, len, dist.Value(0, vals, 0.5f), down);
            }
            return dst;
        }

        /// <summary>The event's master-bus FMOD Distortion (engine_int of most cars, level automated on rpm) applied to the block the way
        /// the bank orders it: on the sum before the downstream faders.</summary>
        static void ApplyDistortion(float[] dst, int frame, int frames, float level, float down)
        {
            if (level <= 0f || down <= 0f) return;
            float inv = 1f / down;
            for (int i = frame * 2, e = (frame + frames) * 2; i < e && i < dst.Length; i++) dst[i] = AcSndFx.Distort(dst[i] * inv, level) * down;
        }

        /// <summary>The event's master-bus distortion, if any (null otherwise), and the linear gain after it.</summary>
        public static AcSndFx MasterDistortion(AcSndEvent ev, out float down)
        {
            down = 1f;
            for (int i = 0; i < ev.Effects.Length; i++)
            {
                var fx = ev.Effects[i];
                if (!fx.OnMaster || !fx.IsPlugin("Distortion")) continue;
                float mixer = ev.Instruments.Length > 0 ? ev.Instruments[0].MixerDb : 0f;
                down = (float)Math.Pow(10.0, (fx.DownstreamDb + mixer) / 20.0);
                return fx;
            }
            return null;
        }

        /// <summary>RMS and peak of a stereo interleaved buffer between two times.</summary>
        public static void Levels(float[] st, float t0, float t1, out float rms, out float peak) { Levels(st, t0, t1, OutRate, out rms, out peak); }

        /// <summary>The same for a buffer at another sample rate (a capture of Unity's mixer output).</summary>
        public static void Levels(float[] st, float t0, float t1, int rate, out float rms, out float peak)
        {
            int a = Math.Max(0, (int)(t0 * rate) * 2), b = Math.Min(st.Length, (int)(t1 * rate) * 2);
            double sum = 0; float pk = 0f;
            for (int i = a; i < b; i++) { float v = st[i]; sum += v * v; float av = v < 0 ? -v : v; if (av > pk) pk = av; }
            rms = b > a ? (float)Math.Sqrt(sum / (b - a)) : 0f;
            peak = pk;
        }

        public static float ToDb(float lin) { return lin <= 1e-6f ? -120f : (float)(20.0 * Math.Log10(lin)); }

        /// <summary>A-weighted RMS (IEC 61672 A curve: poles 20.6 Hz x2, 107.7, 737.9, 12194 Hz x2, bilinear first-order sections,
        /// normalised to 0 dB at 1 kHz) of the mono sum between two times - the measure car cabin noise is quoted in (dB(A)); plain RMS
        /// is dominated by the idle's bass.</summary>
        public static float RmsA(float[] st, float t0, float t1) { return RmsA(st, t0, t1, OutRate); }

        /// <summary>The same for a buffer at another sample rate.</summary>
        public static float RmsA(float[] st, float t0, float t1, int rate)
        {
            var f = new AWeight(rate);
            int s = (int)(t0 * rate), a = Math.Max(0, s - rate / 3), b = Math.Min(st.Length / 2, (int)(t1 * rate));
            double sum = 0; int n = 0;
            for (int i = a; i < b; i++)
            {
                float y = f.Step((st[2 * i] + st[2 * i + 1]) * 0.5f);
                if (i >= s) { sum += y * y; n++; }
            }
            return n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
        }

        sealed class AWeight
        {
            readonly double[] _b0, _b1, _a1, _x1, _y1;
            readonly double _norm = 1.0;
            static readonly double[] Fc = { 20.6, 20.6, 107.7, 737.9, 12194.0, 12194.0 };
            static readonly bool[] High = { true, true, true, true, false, false };

            public AWeight(int rate)
            {
                int n = Fc.Length;
                _b0 = new double[n]; _b1 = new double[n]; _a1 = new double[n]; _x1 = new double[n]; _y1 = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double k = Math.Tan(Math.PI * Math.Min(Fc[i], rate * 0.45) / rate);
                    _a1[i] = (k - 1.0) / (k + 1.0);
                    if (High[i]) { _b0[i] = 1.0 / (1.0 + k); _b1[i] = -_b0[i]; }
                    else { _b0[i] = k / (1.0 + k); _b1[i] = _b0[i]; }
                }
                // normalise: gain of a 1 kHz sine after settling
                double peak = 0;
                for (int i = 0; i < rate / 2; i++)
                {
                    double y = StepRaw(Math.Sin(2.0 * Math.PI * 1000.0 * i / rate));
                    if (i > rate / 4 && Math.Abs(y) > peak) peak = Math.Abs(y);
                }
                for (int i = 0; i < n; i++) { _x1[i] = 0; _y1[i] = 0; }
                _norm = peak > 1e-9 ? 1.0 / peak : 1.0;
            }

            double StepRaw(double x)
            {
                for (int i = 0; i < _b0.Length; i++)
                {
                    double y = _b0[i] * x + _b1[i] * _x1[i] - _a1[i] * _y1[i];
                    _x1[i] = x; _y1[i] = y; x = y;
                }
                return x;
            }

            public float Step(float x) { return (float)(StepRaw(x) * _norm); }
        }
    }
}
