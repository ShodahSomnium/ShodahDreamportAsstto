using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Text helpers shared by every AC reader: byte decoding (UTF-8 with a Latin-1 fallback - ks_nordschleife's
    /// dlc_ui_track.json stores the degree sign as a single 0xB0 byte), BOM stripping, invariant-culture number parsing that
    /// tolerates AC's loose spellings ("3.602", "4655m", "570bhp", "325+km/h", "0 ,-0.285 ,0.3", "0,5").
    /// </summary>
    public static class AcText
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Decodes bytes as strict UTF-8; on invalid sequences maps every byte 1:1 (Latin-1). Strips a leading BOM.</summary>
        public static string Decode(byte[] b, int offset, int count)
        {
            if (b == null || count <= 0) return string.Empty;
            if (count >= 3 && b[offset] == 0xEF && b[offset + 1] == 0xBB && b[offset + 2] == 0xBF) { offset += 3; count -= 3; }
            bool ascii = true;
            for (int i = 0; i < count; i++) if (b[offset + i] >= 0x80) { ascii = false; break; }
            if (ascii) return Encoding.ASCII.GetString(b, offset, count);
            try { return StrictUtf8.GetString(b, offset, count); }
            catch (DecoderFallbackException) { return Latin1(b, offset, count); }
            catch (ArgumentException) { return Latin1(b, offset, count); }
        }

        public static string Decode(byte[] b) { return b == null ? string.Empty : Decode(b, 0, b.Length); }

        public static string Latin1(byte[] b, int offset, int count)
        {
            var c = new char[count];
            for (int i = 0; i < count; i++) c[i] = (char)b[offset + i];
            return new string(c);
        }

        /// <summary>Reads a whole text file (UTF-8 / Latin-1 fallback); null if missing or unreadable.</summary>
        public static string ReadFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return Decode(File.ReadAllBytes(path));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// Parses a float the way AC writes them: invariant culture, surrounding spaces/tabs ignored. If the strict parse fails, a
        /// lone decimal comma ("0,5") is accepted, then the leading numeric part ("4655m" -> 4655, "570bhp" -> 570).
        /// </summary>
        public static bool TryFloat(string s, out float v)
        {
            v = 0f;
            if (s == null) return false;
            s = s.Trim();
            if (s.Length == 0) return false;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return true;
            int comma = s.IndexOf(',');
            if (comma >= 0 && s.IndexOf(',', comma + 1) < 0 && s.IndexOf('.') < 0 &&
                float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return true;
            return TryLeadingNumber(s, out v);
        }

        public static float Float(string s, float def = 0f) { float v; return TryFloat(s, out v) ? v : def; }

        public static int Int(string s, int def = 0)
        {
            if (s == null) return def;
            int i;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            float f;
            return TryFloat(s, out f) ? (int)Math.Round(f) : def;
        }

        /// <summary>The first number found anywhere in the text ("325+km/h" -> 325, "3.3s 0-100" -> 3.3, "~ 1500 kg" -> 1500).</summary>
        public static bool TryLeadingNumber(string s, out float v)
        {
            v = 0f;
            if (s == null) return false;
            int i = 0;
            while (i < s.Length && !(char.IsDigit(s[i]) || ((s[i] == '-' || s[i] == '.') && i + 1 < s.Length && char.IsDigit(s[i + 1])))) i++;
            if (i >= s.Length) return false;
            int st = i;
            if (s[i] == '-') i++;
            bool dot = false;
            while (i < s.Length && (char.IsDigit(s[i]) || (s[i] == '.' && !dot)))
            {
                if (s[i] == '.') dot = true;
                i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E') && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || s[i + 1] == '-' || s[i + 1] == '+'))
            {
                int j = i + 1; if (s[j] == '-' || s[j] == '+') j++;
                if (j < s.Length && char.IsDigit(s[j])) { i = j; while (i < s.Length && char.IsDigit(s[i])) i++; }
            }
            return float.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>Splits an AC vector ("-206.718 ,-8.39364 ,410.949", "200,0,0", "1 2 3") into floats; unparsable parts = 0.</summary>
        public static float[] Floats(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];
            var parts = s.Split(VectorSeparators, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<float>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                float v;
                list.Add(TryFloat(parts[i], out v) ? v : 0f);
            }
            return list.ToArray();
        }

        static readonly char[] VectorSeparators = { ',', ' ', '\t', ';' };

        /// <summary>Trims and removes a pair of surrounding double quotes.</summary>
        public static string Unquote(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s;
        }

        /// <summary>Removes simple HTML tags (&lt;br&gt;, &lt;/br&gt;, &lt;b&gt; ...) used in ui descriptions; &lt;br&gt; becomes a newline.</summary>
        public static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '<')
                {
                    int e = s.IndexOf('>', i + 1);
                    if (e > i && e - i < 40)
                    {
                        string tag = s.Substring(i + 1, e - i - 1).Trim().ToLowerInvariant();
                        if (tag == "br" || tag == "/br" || tag == "br/" || tag == "br /" || tag == "p" || tag == "/p") sb.Append('\n');
                        i = e + 1; continue;
                    }
                }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }
    }
}
