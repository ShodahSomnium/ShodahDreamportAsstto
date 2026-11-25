using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Lenient JSON reader for AC's ui_car.json / ui_skin.json / ui_track.json (Unity-free; System.Text.Json is not available in
    /// the world and JsonUtility cannot do dictionaries).
    ///
    /// Tolerates everything found in the 1186 ui json files of the install (171 fail a strict parser): UTF-8 BOM, invalid UTF-8
    /// (Latin-1 bytes, via <see cref="AcText.Decode(byte[])"/>), raw CR/LF/TAB inside strings (multi-line descriptions), invalid escapes
    /// (<c>\x</c> kept as backslash + char), trailing commas, missing commas, <c>//</c> and <c>/* */</c> comments, unquoted keys,
    /// space before colons, leading whitespace, truncated files (returns what was read).
    ///
    /// Result tree: <c>Dictionary&lt;string, object&gt;</c> (keys case-insensitive, last duplicate wins), <c>List&lt;object&gt;</c>,
    /// <c>string</c>, <c>bool</c>, <c>null</c>. <b>Numbers are returned as their source text (string)</b>: AC writes numbers as strings
    /// anyway ("5793", "3.602", "4655m") - use <see cref="Num"/> / <see cref="AcText.TryFloat"/> to read them.
    /// </summary>
    public static class AcJson
    {
        const int MaxDepth = 64; // ours: guard against pathological nesting

        public static object Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;
            return Value(text, ref i, 0);
        }

        public static object Parse(byte[] data) { return data == null ? null : Parse(AcText.Decode(data)); }

        /// <summary>Parses a file; null when missing / unreadable.</summary>
        public static object ParseFile(string path)
        {
            string t = AcText.ReadFile(path);
            return t == null ? null : Parse(t);
        }

        /// <summary>Parses a file whose root must be an object; null otherwise.</summary>
        public static Dictionary<string, object> ParseObjectFile(string path) { return ParseFile(path) as Dictionary<string, object>; }

        // ---------------- accessors ----------------

        /// <summary>String value of a key (numbers/bools as text); <paramref name="def"/> when missing or not a scalar.</summary>
        public static string Str(Dictionary<string, object> o, string key, string def = null)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return def;
            if (v is string) return (string)v;
            if (v is bool) return (bool)v ? "true" : "false";
            return def;
        }

        /// <summary>Trimmed string value, null/empty mapped to <paramref name="def"/>.</summary>
        public static string Text(Dictionary<string, object> o, string key, string def = null)
        {
            string s = Str(o, key, null);
            if (s == null) return def;
            s = s.Trim();
            return s.Length == 0 ? def : s;
        }

        /// <summary>Number from a string value ("26", "3.602", "4655m", "570bhp"); <paramref name="def"/> when absent.</summary>
        public static float Num(Dictionary<string, object> o, string key, float def = 0f)
        {
            string s = Str(o, key, null);
            float v;
            return s != null && AcText.TryFloat(s, out v) ? v : def;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> o, string key)
        {
            object v; return o != null && o.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(Dictionary<string, object> o, string key)
        {
            object v; return o != null && o.TryGetValue(key, out v) ? v as List<object> : null;
        }

        /// <summary>A string array ("tags": [...]); a single string becomes a one-element list. Never null.</summary>
        public static List<string> Strings(Dictionary<string, object> o, string key)
        {
            var res = new List<string>();
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return res;
            var l = v as List<object>;
            if (l == null) { if (v is string && ((string)v).Length > 0) res.Add((string)v); return res; }
            for (int i = 0; i < l.Count; i++) { var s = l[i] as string; if (!string.IsNullOrEmpty(s)) res.Add(s.Trim()); }
            return res;
        }

        /// <summary>
        /// A curve given as an array of pairs (ui_car.json torqueCurve / powerCurve: [["rpm","value"], ...], strings or numbers).
        /// Returns an empty <see cref="AcLut"/> when absent.
        /// </summary>
        public static AcLut Curve(Dictionary<string, object> o, string key)
        {
            var l = Arr(o, key);
            var xs = new List<float>(); var ys = new List<float>();
            if (l != null)
            {
                for (int i = 0; i < l.Count; i++)
                {
                    var pair = l[i] as List<object>;
                    if (pair == null || pair.Count < 2) continue;
                    float x, y;
                    if (AcText.TryFloat(pair[0] as string, out x) && AcText.TryFloat(pair[1] as string, out y)) { xs.Add(x); ys.Add(y); }
                }
            }
            return new AcLut(xs.ToArray(), ys.ToArray());
        }

        // ---------------- parser ----------------

        static void Ws(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c <= ' ' || c == '﻿' || c == ' ') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = e < 0 ? s.Length : e + 2; continue;
                }
                break;
            }
        }

        static object Value(string s, ref int i, int depth)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) return d;
                    char k0 = s[i];
                    if (k0 == '}') { i++; return d; }
                    if (k0 == ',') { i++; continue; }
                    if (k0 == ']') { i++; return d; } // mismatched bracket: give up on this object
                    string key = k0 == '"' || k0 == '\'' ? Str(s, ref i) : Bare(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && (s[i] == ':' || s[i] == '=')) i++;
                    object v = depth < MaxDepth ? Value(s, ref i, depth + 1) : null;
                    d[key.Trim()] = v;
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) return l;
                    char a = s[i];
                    if (a == ']') { i++; return l; }
                    if (a == ',') { i++; continue; }
                    if (a == '}') { i++; return l; }
                    int before = i;
                    l.Add(depth < MaxDepth ? Value(s, ref i, depth + 1) : null);
                    if (i == before) i++; // never loop forever on junk
                }
            }
            if (c == '"' || c == '\'') return Str(s, ref i);
            string bare = Bare(s, ref i);
            if (bare.Length == 0) { if (i < s.Length) i++; return null; }
            if (bare == "true") return true;
            if (bare == "false") return false;
            if (bare == "null") return null;
            return bare;
        }

        static string Bare(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && ",}]:\r\n".IndexOf(s[i]) < 0) i++;
            return s.Substring(st, i - st).Trim();
        }

        static string Str(string s, ref int i)
        {
            char q = s[i];
            var sb = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != q)
            {
                char c = s[i++];
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '"': case '\\': case '/': case '\'': sb.Append(e); break;
                        case 'u':
                            int code;
                            if (i + 4 <= s.Length && int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            { sb.Append((char)code); i += 4; }
                            else sb.Append("\\u");
                            break;
                        default: sb.Append('\\').Append(e); break; // invalid escape: keep literally
                    }
                }
                else if (c == '\r') { } // raw CR inside strings: drop (CRLF -> LF)
                else sb.Append(c);
            }
            if (i < s.Length) i++; // closing quote
            return sb.ToString();
        }
    }
}
