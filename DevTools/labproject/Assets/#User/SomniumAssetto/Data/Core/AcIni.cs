using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One [SECTION] of an AC ini: keys case-insensitive, last value wins, insertion order kept.</summary>
    public sealed class AcIniSection
    {
        public readonly string Name;
        readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _keys = new List<string>();

        public AcIniSection(string name) { Name = name; }

        /// <summary>Keys in first-seen order.</summary>
        public List<string> Keys { get { return _keys; } }
        public int Count { get { return _keys.Count; } }

        public void Set(string key, string value)
        {
            if (!_map.ContainsKey(key)) _keys.Add(key);
            _map[key] = value;
        }

        public bool Has(string key) { return _map.ContainsKey(key); }

        public bool TryGet(string key, out string value) { return _map.TryGetValue(key, out value); }

        public string Get(string key, string def = null) { string v; return _map.TryGetValue(key, out v) ? v : def; }
        public float GetFloat(string key, float def = 0f) { string v; return _map.TryGetValue(key, out v) ? AcText.Float(v, def) : def; }
        public int GetInt(string key, int def = 0) { string v; return _map.TryGetValue(key, out v) ? AcText.Int(v, def) : def; }
        public bool GetBool(string key, bool def = false) { string v; return _map.TryGetValue(key, out v) ? AcIni.ParseBool(v, def) : def; }

        /// <summary>Comma/space separated vector; returns <paramref name="def"/> (may be null) when the key is missing.</summary>
        public float[] GetFloats(string key, float[] def = null) { string v; return _map.TryGetValue(key, out v) ? AcText.Floats(v) : def; }

        /// <summary>A LUT value: inline "(0=0|1000=24)" / "(|0=1|...)" or a file name resolved by the caller (see <see cref="AcLut"/>).</summary>
        public AcLut GetInlineLut(string key) { string v; return _map.TryGetValue(key, out v) ? AcLut.ParseInline(v) : null; }
    }

    /// <summary>
    /// The Assetto Corsa ini dialect (Unity-free). Verified quirks of the 86 cars' data.acd files and the track data folders:
    /// <list type="bullet">
    /// <item><c>[SECTION]</c> / <c>KEY=VALUE</c>, keys and section names case-insensitive, surrounding spaces/tabs trimmed.</item>
    /// <item>Comments: <c>;</c> anywhere (often after a value, tab padded: <c>TYPE=RWD    ; Vampire</c>), <c>//</c> at line start or after
    /// whitespace (lines of <c>/////</c> in setup.ini). <c>//</c> glued to text (a URL) is kept.</item>
    /// <item>Sections may repeat or come out of order (ai.ini has [HEADER] after [LOOKAHEAD]): repeated sections MERGE (last value wins);
    /// <see cref="Order"/> keeps the order of first appearance.</item>
    /// <item>Mixed CRLF/LF, empty files, BOM, Latin-1 bytes (decoded by <see cref="AcText"/>). Keys before any section go to section "".</item>
    /// <item>Numbers are invariant culture; vectors are comma lists with optional spaces (<c>0 ,-0.285 ,0.3</c>); a lone decimal comma
    /// (<c>0,5</c>) in a scalar is accepted by <see cref="AcText.TryFloat"/>.</item>
    /// </list>
    /// </summary>
    public sealed class AcIni
    {
        readonly Dictionary<string, AcIniSection> _sections = new Dictionary<string, AcIniSection>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sections in order of first appearance.</summary>
        public readonly List<AcIniSection> Order = new List<AcIniSection>();

        /// <summary>Source name (file path or archive entry) for messages; may be null.</summary>
        public string Source;

        public static readonly AcIni Empty = new AcIni();

        public static AcIni Parse(string text, string source = null)
        {
            var ini = new AcIni { Source = source };
            if (string.IsNullOrEmpty(text)) return ini;
            AcIniSection cur = null;
            int p = 0, n = text.Length;
            if (n > 0 && text[0] == '﻿') p = 1;
            while (p < n)
            {
                int e = p;
                while (e < n && text[e] != '\n' && text[e] != '\r') e++;
                ParseLine(ini, text, p, e, ref cur);
                p = e;
                while (p < n && (text[p] == '\n' || text[p] == '\r')) p++;
            }
            return ini;
        }

        static void ParseLine(AcIni ini, string text, int start, int end, ref AcIniSection cur)
        {
            // strip comments
            int cut = end;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == ';') { cut = i; break; }
                if (c == '/' && i + 1 < end && text[i + 1] == '/' && (i == start || text[i - 1] == ' ' || text[i - 1] == '\t' || text[i - 1] == '='))
                {
                    // "KEY=//..." would be an empty value with a comment; "a//b" (URL-like) is kept
                    cut = i; break;
                }
            }
            while (start < cut && char.IsWhiteSpace(text[start])) start++;
            while (cut > start && char.IsWhiteSpace(text[cut - 1])) cut--;
            if (cut <= start) return;
            if (text[start] == '[')
            {
                int close = text.IndexOf(']', start + 1, cut - start - 1);
                string name = (close < 0 ? text.Substring(start + 1, cut - start - 1) : text.Substring(start + 1, close - start - 1)).Trim();
                cur = ini.GetOrAdd(name);
                return;
            }
            int eq = text.IndexOf('=', start, cut - start);
            if (eq < 0) return;
            string key = text.Substring(start, eq - start).Trim();
            if (key.Length == 0) return;
            string val = text.Substring(eq + 1, cut - eq - 1).Trim();
            if (cur == null) cur = ini.GetOrAdd("");
            cur.Set(key, val);
        }

        public static AcIni Load(string path)
        {
            string t = AcText.ReadFile(path);
            return t == null ? null : Parse(t, path);
        }

        /// <summary>Loads a file or returns <see cref="Empty"/> (never null) when missing.</summary>
        public static AcIni LoadOrEmpty(string path)
        {
            return Load(path) ?? new AcIni { Source = path };
        }

        public static AcIni FromBytes(byte[] data, string source = null) { return Parse(AcText.Decode(data), source); }

        AcIniSection GetOrAdd(string name)
        {
            AcIniSection s;
            if (!_sections.TryGetValue(name, out s))
            {
                s = new AcIniSection(name);
                _sections[name] = s;
                Order.Add(s);
            }
            return s;
        }

        public bool Has(string section) { return _sections.ContainsKey(section); }
        public bool Has(string section, string key) { AcIniSection s; return _sections.TryGetValue(section, out s) && s.Has(key); }

        /// <summary>The section or null.</summary>
        public AcIniSection this[string section] { get { AcIniSection s; return _sections.TryGetValue(section, out s) ? s : null; } }

        public string Get(string section, string key, string def = null) { var s = this[section]; return s == null ? def : s.Get(key, def); }
        public float GetFloat(string section, string key, float def = 0f) { var s = this[section]; return s == null ? def : s.GetFloat(key, def); }
        public int GetInt(string section, string key, int def = 0) { var s = this[section]; return s == null ? def : s.GetInt(key, def); }
        public bool GetBool(string section, string key, bool def = false) { var s = this[section]; return s == null ? def : s.GetBool(key, def); }
        public float[] GetFloats(string section, string key, float[] def = null) { var s = this[section]; return s == null ? def : s.GetFloats(key, def); }

        /// <summary>Sections whose name starts with <paramref name="prefix"/> (e.g. "MODEL_", "SURFACE_", "LIGHT_"), in file order.</summary>
        public List<AcIniSection> WithPrefix(string prefix)
        {
            var l = new List<AcIniSection>();
            for (int i = 0; i < Order.Count; i++)
                if (Order[i].Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) l.Add(Order[i]);
            return l;
        }

        /// <summary>
        /// Numbered sections PREFIX0, PREFIX1, ... (e.g. "SURFACE_", "TURBO_") in index order, stopping at the first gap
        /// (AC's own loop does that). Use <see cref="WithPrefix"/> to get every numbered section regardless of gaps.
        /// </summary>
        public List<AcIniSection> Numbered(string prefix)
        {
            var l = new List<AcIniSection>();
            for (int i = 0; ; i++)
            {
                var s = this[prefix + i];
                if (s == null) break;
                l.Add(s);
            }
            return l;
        }

        public static bool ParseBool(string v, bool def)
        {
            if (v == null) return def;
            v = v.Trim();
            if (v.Length == 0) return def;
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("no", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            float f;
            return AcText.TryFloat(v, out f) ? f != 0f : def;
        }
    }
}
