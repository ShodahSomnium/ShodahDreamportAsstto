using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// An AC lookup table: sorted (x, y) pairs, evaluated piecewise-linearly and clamped at both ends.
    ///
    /// File form (power.lut, throttle.lut, *.lut in data.acd): one <c>x|y</c> per line, invariant floats, blank lines and
    /// <c>;</c> / <c>//</c> comments allowed (<c>-3000|100</c> ... <c>9500|0</c>). Inline form used inside inis:
    /// <c>LUT=(0=0|1000=24|3000=98)</c>, also written with a leading pipe <c>(|0=1|500=0.9|)</c>. The <c>.rto</c> gear files are
    /// <c>label|ratio</c> with a non-numeric label (<c>13//41|3.15</c>) - see <see cref="ParseRto"/>.
    /// Evaluation allocates nothing (safe per frame).
    /// </summary>
    public sealed class AcLut
    {
        public float[] X = new float[0];
        public float[] Y = new float[0];

        public int Count { get { return X.Length; } }
        public bool IsEmpty { get { return X.Length == 0; } }

        public AcLut() { }

        public AcLut(float[] x, float[] y)
        {
            if (x == null || y == null || x.Length != y.Length) throw new ArgumentException("lut x/y length mismatch");
            X = x; Y = y;
            SortIfNeeded();
        }

        /// <summary>Piecewise linear, clamped to the first/last value. 0 for an empty table.</summary>
        public float Eval(float x)
        {
            int n = X.Length;
            if (n == 0) return 0f;
            if (x <= X[0]) return Y[0];
            if (x >= X[n - 1]) return Y[n - 1];
            // binary search for the segment
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (X[mid] <= x) lo = mid; else hi = mid;
            }
            float dx = X[hi] - X[lo];
            if (dx <= 1e-9f) return Y[hi];
            return Y[lo] + (Y[hi] - Y[lo]) * (x - X[lo]) / dx;
        }

        public float MinX { get { return X.Length > 0 ? X[0] : 0f; } }
        public float MaxX { get { return X.Length > 0 ? X[X.Length - 1] : 0f; } }

        public float MaxY
        {
            get { float m = float.MinValue; for (int i = 0; i < Y.Length; i++) if (Y[i] > m) m = Y[i]; return Y.Length > 0 ? m : 0f; }
        }

        /// <summary>X at which Y is largest (e.g. peak-torque rpm of power.lut).</summary>
        public float ArgMaxY
        {
            get { int b = -1; float m = float.MinValue; for (int i = 0; i < Y.Length; i++) if (Y[i] > m) { m = Y[i]; b = i; } return b >= 0 ? X[b] : 0f; }
        }

        void SortIfNeeded()
        {
            for (int i = 1; i < X.Length; i++)
            {
                if (X[i] < X[i - 1])
                {
                    Array.Sort((float[])X.Clone(), Y); // keep Y aligned with sorted X
                    Array.Sort(X);
                    return;
                }
            }
        }

        /// <summary>Parses the text of a .lut file. Lines that do not hold two numbers are skipped. Never null.</summary>
        public static AcLut Parse(string text)
        {
            var xs = new List<float>(); var ys = new List<float>();
            if (!string.IsNullOrEmpty(text))
            {
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = StripComment(lines[i]);
                    if (l.Length == 0) continue;
                    int bar = l.IndexOf('|');
                    if (bar < 0) bar = l.IndexOf('=');
                    if (bar < 0) bar = l.IndexOf('\t');
                    if (bar <= 0) continue;
                    float x, y;
                    if (AcText.TryFloat(l.Substring(0, bar), out x) && AcText.TryFloat(l.Substring(bar + 1), out y) && IsNumberish(l.Substring(0, bar)))
                    { xs.Add(x); ys.Add(y); }
                }
            }
            return new AcLut(xs.ToArray(), ys.ToArray());
        }

        public static AcLut FromBytes(byte[] data) { return Parse(AcText.Decode(data)); }

        /// <summary>
        /// Inline LUT: "(0=0|1000=24|3000=98)", "(|0=1|500=0.9|)", also tolerates ':' instead of '='. Returns null when the text
        /// is not an inline table (then it is probably a file name - load it with <see cref="Parse"/>).
        /// </summary>
        public static AcLut ParseInline(string text)
        {
            if (text == null) return null;
            string t = text.Trim();
            if (t.Length < 2 || t[0] != '(') return null;
            int close = t.LastIndexOf(')');
            t = close > 0 ? t.Substring(1, close - 1) : t.Substring(1);
            var parts = t.Split('|');
            var xs = new List<float>(); var ys = new List<float>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq < 0) eq = p.IndexOf(':');
                if (eq <= 0) continue;
                float x, y;
                if (AcText.TryFloat(p.Substring(0, eq), out x) && AcText.TryFloat(p.Substring(eq + 1), out y)) { xs.Add(x); ys.Add(y); }
            }
            return new AcLut(xs.ToArray(), ys.ToArray());
        }

        /// <summary>One entry of a .rto gear-ratio choice file: <c>label|ratio</c>.</summary>
        public struct RtoEntry { public string Label; public float Ratio; }

        /// <summary>Parses a .rto file (<c>13//41|3.15</c> - the label may contain "//", so comments are only ';').</summary>
        public static List<RtoEntry> ParseRto(string text)
        {
            var l = new List<RtoEntry>();
            if (string.IsNullOrEmpty(text)) return l;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i];
                int sc = s.IndexOf(';'); if (sc >= 0) s = s.Substring(0, sc);
                s = s.Trim();
                int bar = s.LastIndexOf('|');
                if (bar <= 0) continue;
                float r;
                if (AcText.TryFloat(s.Substring(bar + 1), out r)) l.Add(new RtoEntry { Label = s.Substring(0, bar).Trim(), Ratio = r });
            }
            return l;
        }

        static string StripComment(string l)
        {
            int c = l.IndexOf(';'); if (c >= 0) l = l.Substring(0, c);
            c = l.IndexOf("//", StringComparison.Ordinal); if (c >= 0) l = l.Substring(0, c);
            return l.Trim();
        }

        static bool IsNumberish(string s)
        {
            s = s.Trim();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsDigit(c) || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E' || c == ',')) return false;
            }
            return s.Length > 0;
        }
    }
}
