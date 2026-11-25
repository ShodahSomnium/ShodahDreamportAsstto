using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] The bitmap fonts of content/fonts as Unity textures + display materials, shared by every car (H's Unity-free
    /// <see cref="AcBitmapFont"/> reads the glyph table). Images are light glyphs on black: <see cref="AcBitmapFont.LuminanceToAlpha"/> turns
    /// them into alpha-blendable ink. Rows end up bottom-up (Unity convention, v = 1 at the image top) for PNG and BMP alike.
    /// Kept for the session (a font is 0.2-1 MB; 26 fonts are referenced by the 86 cars).
    /// </summary>
    public static class AcCarFonts
    {
        public sealed class Entry
        {
            public AcBitmapFont Font;
            public Texture2D Texture;
            public Material Material;
        }

        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Frees every font texture / material and forgets failed names (C calls it when the last car is disposed).</summary>
        public static void Clear()
        {
            foreach (var kv in Cache)
            {
                var e = kv.Value;
                if (e == null) continue;
                if (e.Material != null) UnityEngine.Object.Destroy(e.Material);
                if (e.Texture != null) UnityEngine.Object.Destroy(e.Texture);
            }
            Cache.Clear();
        }

        public static int Count { get { return Cache.Count; } }

        /// <summary>The font by digital_instruments.ini FONT= name, or null (logged once per name).</summary>
        public static Entry Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Entry e;
            if (Cache.TryGetValue(name, out e)) return e;
            e = Load(name);
            Cache[name] = e;
            return e;
        }

        static Entry Load(string name)
        {
            string dir = FontsDir();
            string problem;
            var font = AcBitmapFont.Load(dir, name, out problem);
            if (font == null) { AcLog.InfoOnce("Car: font " + name + " not loaded (" + problem + ")"); return null; }
            try
            {
                byte[] bytes = File.ReadAllBytes(font.ImagePath);
                byte[] rgba;
                int w, h;
                if (font.ImageIsBmp)
                {
                    rgba = AcBitmapFont.DecodeBmp(bytes, out w, out h, out problem);
                    if (rgba == null) { AcLog.InfoOnce("Car: font " + name + " image: " + problem); return null; }
                    FlipRows(rgba, w, h); // top-down -> bottom-up
                }
                else
                {
                    var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(tmp, bytes, false)) { UnityEngine.Object.Destroy(tmp); AcLog.InfoOnce("Car: font " + name + " png not decoded"); return null; }
                    w = tmp.width; h = tmp.height;
                    var px = tmp.GetPixels32();
                    UnityEngine.Object.Destroy(tmp);
                    rgba = new byte[w * h * 4];
                    for (int i = 0; i < px.Length; i++) { rgba[i * 4] = px[i].r; rgba[i * 4 + 1] = px[i].g; rgba[i * 4 + 2] = px[i].b; rgba[i * 4 + 3] = px[i].a; }
                }
                AcBitmapFont.LuminanceToAlpha(rgba);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Ac font " + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
                var pix = new Color32[w * h]; // SetPixels32 + Apply(mips): LoadRawTextureData would want the whole mip chain
                for (int i = 0; i < pix.Length; i++) pix[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
                tex.SetPixels32(pix);
                tex.Apply(true, true);
                var mat = AcCarShading.CreateUnlit(tex);
                if (mat == null) { UnityEngine.Object.Destroy(tex); return null; }   // (Get caches the null: nothing would ever free the atlas)
                AcLog.Info("Car: font " + font + " loaded for cockpit displays");
                return new Entry { Font = font, Texture = tex, Material = mat };
            }
            catch (Exception e)
            {
                AcLog.InfoOnce("Car: font " + name + " failed: " + e.Message);
                return null;
            }
        }

        static void FlipRows(byte[] rgba, int w, int h)
        {
            int stride = w * 4;
            var row = new byte[stride];
            for (int y = 0; y < h / 2; y++)
            {
                int a = y * stride, b = (h - 1 - y) * stride;
                Buffer.BlockCopy(rgba, a, row, 0, stride);
                Buffer.BlockCopy(rgba, b, rgba, a, stride);
                Buffer.BlockCopy(row, 0, rgba, b, stride);
            }
        }

        static string FontsDir()
        {
            try
            {
                var g = AcGame.Current;
                if (g != null && g.Content != null && g.Content.Install != null) return g.Content.Install.FontsDir;
            }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>What the digital displays show besides <see cref="AcInstruments"/> (lap times etc.). Filled by <see cref="AcCarModel"/>: from
    /// AcLocal (R) / the car's AcAssists (P) for the local car, from <see cref="AcCarModel.SetLapTimes(int,int,int)"/> (N) for others.</summary>
    public struct AcCarDisplayExtras
    {
        public int LastLapMs, BestLapMs;
        public int LapCount;
        // ---- added by C (second pass)
        /// <summary>Current lap so far (ms; 0 = none - then AcInstruments.LapTimeS is used).</summary>
        public int LapMs;
        /// <summary>Live delta to the best lap (ms, negative = faster; AcLocal.DeltaMs) for PERF items.</summary>
        public int DeltaMs;
        /// <summary>Lateral / longitudinal g from the telemetry (P: LatG + = acceleration towards the car's right, LongG + = accelerating).</summary>
        public float LatG, LongG;
        /// <summary>Tyre pressures (psi) for PRESSURE items: tyres.ini PRESSURE_STATIC, or the player's garage setup for the local car.</summary>
        public float PressureLF, PressureRF, PressureLR, PressureRR;
        /// <summary>TC / ABS levels shown by TC_LEVEL / ABS_LEVEL (valid when <see cref="AssistsKnown"/>).</summary>
        public int TcLevel, AbsLevel;
        public bool AssistsKnown;

        public float Pressure(int tyre)
        {
            switch (tyre) { case 0: return PressureLF; case 1: return PressureRF; case 2: return PressureLR; default: return PressureRR; }
        }
    }

    /// <summary>UNITS=SYSTEM on cockpit displays: the player's HUD choice (H's <c>AcHud.UseMph</c>: hud.units, else AC's gameplay.ini USE_MPH),
    /// re-read once a second. No allocation.</summary>
    public static class AcCarUnits
    {
        static float _next = -1f;
        static bool _mph, _failed;

        public static bool SystemMph
        {
            get
            {
                float now = Time.unscaledTime;
                if (_failed || now < _next) return _mph;
                _next = now + 1f; // ours: re-check period
                try { _mph = AcHud.UseMph(); }
                catch (Exception e) { _failed = true; _mph = false; AcLog.WarnOnce("Car: AcHud.UseMph threw (displays stay km/h): " + e.Message); }
                return _mph;
            }
        }
    }

    /// <summary>
    /// [C, Car model] One digital_instruments.ini [ITEM_n]: text drawn with a bitmap font as quads at POSITION in PARENT's local space
    /// (AC coords, X mirrored), glyph height SIZE metres, ALIGN LEFT/RIGHT/CENTER, colour COLOR / 255 x INTENSITY (the emissive-style scale:
    /// 72 of 392 items are DARK ink on a lit LCD, e.g. ferrari_458 COLOR=10,10,10 INTENSITY=0.5), facing the driver (the parent's local -Z
    /// side). Rebuilds its mesh only when the text changes; no allocation per frame.
    /// [BELIEF: the text plane is the parent's local XY with reading direction = the driver's right; verify in the lab cockpit shot.]
    /// </summary>
    public sealed class AcCarDisplayText
    {
        const int MaxChars = 16;
        public readonly AcCarDigitalItemDef Def;
        readonly AcCarFonts.Entry _font;
        readonly Mesh _mesh;
        readonly GameObject _go;
        readonly Vector3[] _verts = new Vector3[MaxChars * 4];
        readonly Vector2[] _uvs = new Vector2[MaxChars * 4];
        readonly float[] _quads = new float[MaxChars * 8];
        readonly char[] _text = new char[MaxChars];
        readonly char[] _shown = new char[MaxChars];
        int _shownLen = -1;
        readonly Vector3 _origin;
        float _maxSpeed, _maxRpm;

        public GameObject GameObject { get { return _go; } }
        public Mesh Mesh { get { return _mesh; } }
        /// <summary>The text on the display now (lab only: allocates).</summary>
        public string ShownText() { return _shownLen > 0 ? new string(_shown, 0, _shownLen) : ""; }

        AcCarDisplayText(AcCarDigitalItemDef d, AcCarFonts.Entry font, Transform parent)
        {
            Def = d; _font = font;
            _go = new GameObject("DI " + d.Section + " " + d.Type);
            _go.layer = parent.gameObject.layer;
            _go.transform.SetParent(parent, false);
            _origin = AcCarMath.ToUnity(d.Position) + new Vector3(0f, 0f, -0.0005f); // ours: 0.5 mm towards the driver against z-fighting
            _mesh = new Mesh { name = "Ac display " + d.Type };
            _mesh.MarkDynamic();
            var colors = new Color[MaxChars * 4];
            var c = AcCarDisplayColor.Of(d);
            for (int i = 0; i < colors.Length; i++) colors[i] = c;
            var tris = new int[MaxChars * 6];
            for (int q = 0; q < MaxChars; q++)
            {
                int v = q * 4, t = q * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.colors = colors;
            _mesh.triangles = tris;
            float w = d.Size * 12f; // ours: bounds of a 12-glyph line
            _mesh.bounds = new Bounds(_origin, new Vector3(w * 2f, d.Size * 2f, 0.01f));
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = font.Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>Creates the item, or null when its type is not drawn, the parent is missing or the font is unavailable.</summary>
        public static AcCarDisplayText Create(AcCarDigitalItemDef d, Transform parent)
        {
            if (d == null || parent == null || !Supported(d.Type)) return null;
            var f = AcCarFonts.Get(d.Font);
            if (f == null) return null;
            return new AcCarDisplayText(d, f, parent);
        }

        static bool Supported(string type)
        {
            switch (type)
            {
                case "GEAR": case "SPEED": case "RPM": case "FUEL": case "CLOCK": case "LAPTIME": case "CURRENT_LAP":
                case "LAST_LAP": case "BEST_LAP": case "AMBIENT_TEMP": case "WATER_TEMP": case "TURBO_BOOST": case "TURBO_LEVEL": case "TC_LEVEL":
                case "ABS_LEVEL": case "PRESSURE": case "PERF": case "GFORCES": case "MAX_SPEED": case "MAX_RPM": case "FUEL_CONS": case "PLACE_HOLDER":
                    return true;
                default: return false; // RPM_GRAPH is AcCarRpmGraph, GEAR_TX is AcCarTextureItem
            }
        }

        int _rawGear = int.MinValue, _dispGear;
        float _rawSince;

        /// <summary>Formats and (when changed) redraws. No allocation.</summary>
        public void Update(ref AcInstruments v, ref AcCarDisplayExtras x)
        {
            int n = 0;
            n = Put(Def.Prefix, n);
            var d = Def;
            switch (d.Type)
            {
                case "GEAR":
                case "GEAR_TX":
                {
                    // N_TIME: a neutral shorter than N_TIME (the shift itself) keeps the previous gear on the display [BELIEF]
                    int raw = v.Gear;
                    float now = Time.time;
                    if (raw != _rawGear) { _rawGear = raw; _rawSince = now; }
                    int g = raw;
                    if (raw == 0 && d.NTime > 0f && _dispGear != 0 && now - _rawSince < d.NTime) g = _dispGear;
                    _dispGear = g;
                    if (n < MaxChars) _text[n++] = g < 0 ? 'R' : g == 0 ? 'N' : (char)('0' + Math.Min(9, g));
                    break;
                }
                case "SPEED": n = PutInt(Mph(d) ? (int)(v.SpeedKmh * 0.621371f + 0.5f) : (int)(v.SpeedKmh + 0.5f), n); break;
                case "RPM": n = PutInt((int)(v.Rpm + 0.5f), n); break;
                case "FUEL": n = PutFixed(v.FuelLitres, d.Decimals < 0 ? 0 : d.Decimals, n); break;
                case "CLOCK":
                {
                    var now = DateTime.Now; // ours: the local clock (AC shows the session's time of day)
                    n = Put2(now.Hour, n); if (n < MaxChars) _text[n++] = ':'; n = Put2(now.Minute, n);
                    break;
                }
                case "LAPTIME": case "CURRENT_LAP": n = PutTime(x.LapMs > 0 ? x.LapMs : (int)(v.LapTimeS * 1000f), d.Decimals, n); break;
                case "LAST_LAP": n = PutTime(x.LastLapMs, d.Decimals, n); break;
                case "BEST_LAP": n = PutTime(x.BestLapMs, d.Decimals, n); break;
                case "AMBIENT_TEMP": n = PutInt(26, n); break; // ours: AC's default ambient (race.ini TEMPERATURE 26); the Dreamport has no weather
                case "WATER_TEMP": n = PutInt((int)(v.WaterTempC > 0f ? v.WaterTempC : 90f), n); break; // ours: 90 C - P has no water temperature model
                case "TURBO_BOOST": n = PutFixed(v.TurboBar, d.Decimals < 0 ? 1 : d.Decimals, n); break;
                case "TURBO_LEVEL": n = PutInt(1, n); break; // ours: lotus_98t only; no adjustable boost in the physics
                case "TC_LEVEL": n = PutInt(x.AssistsKnown ? x.TcLevel : 1, n); break;
                case "ABS_LEVEL": n = PutInt(x.AssistsKnown ? x.AbsLevel : 1, n); break;
                case "PRESSURE": n = PutFixed(x.Pressure(d.TyreNumber), d.Decimals < 0 ? 1 : d.Decimals, n); break; // psi, cold (no tyre heat model)
                case "PERF": n = PutDelta(x.DeltaMs, d.Decimals < 0 ? 2 : d.Decimals, n); break;
                case "GFORCES":
                {
                    // AC axes: X = the car's left, Z = forward [BELIEF: AC's sign of the g readout]; MIN/MAX clamp; ADDSIGN 0 = magnitude
                    float g = d.Direction == 'Z' ? x.LongG : -x.LatG;
                    if (!float.IsNaN(d.Min) && g < d.Min) g = d.Min;
                    if (!float.IsNaN(d.Max) && g > d.Max) g = d.Max;
                    if (d.AddSign && n < MaxChars) _text[n++] = g < 0f ? '-' : '+';
                    n = PutFixed(Mathf.Abs(g), d.Decimals < 0 ? 1 : d.Decimals, n);
                    break;
                }
                case "MAX_SPEED": if (v.SpeedKmh > _maxSpeed) _maxSpeed = v.SpeedKmh; n = PutInt((int)(Mph(d) ? _maxSpeed * 0.621371f : _maxSpeed), n); break;
                case "MAX_RPM": if (v.Rpm > _maxRpm) _maxRpm = v.Rpm; n = PutInt((int)_maxRpm, n); break;
                case "FUEL_CONS": n = Put("--", n); break; // ours
                case "PLACE_HOLDER": n = Put(d.Text, n); break;
            }
            n = Put(Def.Postfix, n);
            if (n == _shownLen)
            {
                bool same = true;
                for (int i = 0; i < n; i++) if (_shown[i] != _text[i]) { same = false; break; }
                if (same) return;
            }
            for (int i = 0; i < n; i++) _shown[i] = _text[i];
            _shownLen = n;
            Redraw(n);
        }

        /// <summary>UNITS=MPH, or UNITS=SYSTEM (26 items) with the player's mph choice; KMH / absent = km/h.</summary>
        static bool Mph(AcCarDigitalItemDef d)
        {
            if (d.Units == null) return false;
            if (d.Units.Equals("MPH", StringComparison.OrdinalIgnoreCase)) return true;
            return d.Units.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) && AcCarUnits.SystemMph;
        }

        /// <summary>+s.ff / -s.ff (seconds). // ours format (AC's PERF look is not documented)</summary>
        int PutDelta(int ms, int decimals, int n)
        {
            if (n < MaxChars) _text[n++] = ms < 0 ? '-' : '+';
            return PutFixed(Math.Abs(ms) * 0.001f, decimals, n);
        }

        void Redraw(int n)
        {
            var f = _font.Font;
            float h = Def.Size;
            float width = f.Measure(_text, 0, n) * h;
            float x0 = _origin.x;
            if (Def.Align == "RIGHT") x0 -= width;
            else if (Def.Align == "CENTER") x0 -= width * 0.5f;
            int q = f.Layout(_text, 0, n, x0, _origin.y, h, 1f, 0f, _quads);
            float z = _origin.z;
            for (int i = 0; i < MaxChars; i++)
            {
                int v = i * 4;
                if (i < q)
                {
                    int o = i * 8;
                    float qx0 = _quads[o], qy0 = _quads[o + 1], qx1 = _quads[o + 2], qy1 = _quads[o + 3], u0 = _quads[o + 4], u1 = _quads[o + 5];
                    _verts[v] = new Vector3(qx0, qy0, z); _verts[v + 1] = new Vector3(qx0, qy1, z);
                    _verts[v + 2] = new Vector3(qx1, qy1, z); _verts[v + 3] = new Vector3(qx1, qy0, z);
                    _uvs[v] = new Vector2(u0, 0f); _uvs[v + 1] = new Vector2(u0, 1f); _uvs[v + 2] = new Vector2(u1, 1f); _uvs[v + 3] = new Vector2(u1, 0f);
                }
                else
                {
                    _verts[v] = _verts[v + 1] = _verts[v + 2] = _verts[v + 3] = Vector3.zero;
                }
            }
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
        }

        int Put(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return n;
            for (int i = 0; i < s.Length && n < MaxChars; i++) _text[n++] = s[i];
            return n;
        }

        int Put2(int v, int n)
        {
            if (n < MaxChars) _text[n++] = (char)('0' + (v / 10) % 10);
            if (n < MaxChars) _text[n++] = (char)('0' + v % 10);
            return n;
        }

        int PutInt(int v, int n)
        {
            if (v < 0) { if (n < MaxChars) _text[n++] = '-'; v = -v; }
            int start = n;
            do { if (n < MaxChars) _text[n++] = (char)('0' + v % 10); v /= 10; } while (v > 0 && n < MaxChars);
            for (int a = start, b = n - 1; a < b; a++, b--) { char t = _text[a]; _text[a] = _text[b]; _text[b] = t; }
            return n;
        }

        int PutFixed(float v, int decimals, int n)
        {
            if (decimals <= 0) return PutInt((int)Math.Round(v), n);
            if (decimals > 3) decimals = 3;
            int scale = decimals == 1 ? 10 : decimals == 2 ? 100 : 1000;
            int iv = (int)Math.Round(Math.Abs(v) * scale);
            if (v < 0f && iv != 0 && n < MaxChars) _text[n++] = '-';
            n = PutInt(iv / scale, n);
            if (n < MaxChars) _text[n++] = '.';
            int frac = iv % scale;
            for (int k = scale / 10; k >= 1 && n < MaxChars; k /= 10) { _text[n++] = (char)('0' + (frac / k) % 10); }
            return n;
        }

        /// <summary>m:ss.fff (decimals 0..3, default 3); 0 or less = "-:--.---" (no time yet). // ours format</summary>
        int PutTime(int ms, int decimals, int n)
        {
            if (decimals < 0 || decimals > 3) decimals = 3;
            if (ms <= 0)
            {
                n = Put("-:--", n);
                if (decimals > 0) { if (n < MaxChars) _text[n++] = '.'; for (int k = 0; k < decimals && n < MaxChars; k++) _text[n++] = '-'; }
                return n;
            }
            int min = ms / 60000, sec = (ms / 1000) % 60, frac = ms % 1000;
            n = PutInt(min, n);
            if (n < MaxChars) _text[n++] = ':';
            n = Put2(sec, n);
            if (decimals > 0)
            {
                if (n < MaxChars) _text[n++] = '.';
                int div = 100;
                for (int k = 0; k < decimals && n < MaxChars; k++, div /= 10) _text[n++] = (char)('0' + (frac / div) % 10);
            }
            return n;
        }

        public void Destroy()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }

    /// <summary>
    /// [C, Car model] digital_instruments.ini TYPE=RPM_GRAPH (4 items): TEXTURE_BASE drawn WIDTH x HEIGHT at POSITION, TEXTURE_TOP over it
    /// cut at (rpm - RPM_MIN) / (RPM_MAX - RPM_MIN) of the width. Textures by name from the car's texture/ folder (where all 26 digital
    /// textures of the install live, harness digitems), else its kn5 / folder; PNG or DDS through W's uploader; skipped (logged) when absent.
    /// </summary>
    public sealed class AcCarRpmGraph
    {
        readonly AcCarDigitalItemDef _def;
        readonly Mesh _top;
        readonly Vector3[] _v = new Vector3[4];
        readonly Vector2[] _uv = new Vector2[4];
        readonly GameObject _root;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        float _shown = -1f;
        readonly Vector3 _o;

        AcCarRpmGraph(AcCarDigitalItemDef d, Transform parent, Texture2D baseTex, Texture2D topTex)
        {
            _def = d;
            _root = new GameObject("DI " + d.Section + " RPM_GRAPH");
            _root.layer = parent.gameObject.layer;
            _root.transform.SetParent(parent, false);
            _o = AcCarMath.ToUnity(d.Position) + new Vector3(0f, 0f, -0.0005f); // ours: against z-fighting
            if (baseTex != null) MakeQuad("base", baseTex, 1f, -0.0001f);
            _top = topTex != null ? MakeQuad("top", topTex, 0f, -0.0002f) : null;
        }

        Mesh MakeQuad(string name, Texture2D tex, float frac, float dz)
        {
            var go = new GameObject(name);
            go.layer = _root.layer;
            go.transform.SetParent(_root.transform, false);
            var m = new Mesh { name = "Ac rpm graph " + name };
            m.MarkDynamic();
            Fill(m, frac, dz);
            var col = AcCarDisplayColor.Of(_def); // COLOR x INTENSITY like the texts [BELIEF: tatuusfa1 / ktm graphs are white segments tinted dark (10,10,10 x 3)]
            m.colors = new[] { col, col, col, col };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.bounds = new Bounds(_o, new Vector3(Mathf.Max(0.01f, _def.Width * 2f), Mathf.Max(0.01f, _def.Height * 2f), 0.01f));
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            var mat = AcCarShading.CreateUnlit(tex);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _owned.Add(m); if (mat != null) _owned.Add(mat); _owned.Add(tex);
            return m;
        }

        void Fill(Mesh m, float frac, float dz)
        {
            float x0 = _o.x, y0 = _o.y, w = _def.Width * frac, h = _def.Height, z = _o.z + dz;
            _v[0] = new Vector3(x0, y0, z); _v[1] = new Vector3(x0, y0 + h, z); _v[2] = new Vector3(x0 + w, y0 + h, z); _v[3] = new Vector3(x0 + w, y0, z);
            // W's uploader keeps the image top at v = 0 (DDS raw and PNG flipped alike): bottom vertices v = 1
            _uv[0] = new Vector2(0f, 1f); _uv[1] = new Vector2(0f, 0f); _uv[2] = new Vector2(frac, 0f); _uv[3] = new Vector2(frac, 1f);
            m.vertices = _v;
            m.uv = _uv;
        }

        public static AcCarRpmGraph Create(AcCarDigitalItemDef d, Transform parent, Func<string, Texture2D> texture)
        {
            if (d == null || parent == null || texture == null || d.Width <= 0f || d.Height <= 0f) return null;
            var b = d.TextureBase != null ? texture(d.TextureBase) : null;
            var t = d.TextureTop != null ? texture(d.TextureTop) : null;
            if (b == null && t == null) return null;
            return new AcCarRpmGraph(d, parent, b, t);
        }

        public void Update(float rpm)
        {
            if (_top == null) return;
            float span = _def.RpmMax - _def.RpmMin;
            float f = span > 1f ? Mathf.Clamp01((rpm - _def.RpmMin) / span) : 0f;
            f = Mathf.Round(f * 200f) / 200f; // ours: 0.5 % steps - redraw only on visible change
            if (f == _shown) return;
            _shown = f;
            Fill(_top, f, -0.0002f);
        }

        public void Destroy()
        {
            for (int i = 0; i < _owned.Count; i++) if (_owned[i] != null) UnityEngine.Object.Destroy(_owned[i]);
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }

    /// <summary>digital_instruments.ini item colour: COLOR / 255 x INTENSITY, alpha COLOR[3] / 255 (vertex colour of the unlit display
    /// material; values above 1 are HDR, like an emissive in AC units).</summary>
    public static class AcCarDisplayColor
    {
        public static Color Of(AcCarDigitalItemDef d)
        {
            float k = Mathf.Max(0f, d.Intensity) / 255f;
            return new Color(d.R * k, d.G * k, d.B * k, Mathf.Clamp01(d.A / 255f));
        }
    }

    /// <summary>
    /// [C, Car model] digital_instruments.ini TYPE=GEAR_TX (18 items, ks_lamborghini_huracan_performante + ks_pagani_huayra_bc): a quad WIDTH x
    /// HEIGHT at POSITION showing TEXTURE_BASE (gear_r.dds ... gear_7.dds from the car's texture/ folder) while the gear index equals TRIGGER
    /// (0 R, 1 N, 2 first ...). ITEM_REF (always 0) is ignored. POSITION = the quad's lower left corner seen by the driver [BELIEF: the
    /// huracan's quad then centres on x = 0 like its CENTER-aligned GFORCES texts]. No allocation per frame.
    /// </summary>
    public sealed class AcCarTextureItem
    {
        readonly AcCarDigitalItemDef _def;
        readonly GameObject _go;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        int _shown = -1;

        AcCarTextureItem(AcCarDigitalItemDef d, Transform parent, Texture2D tex)
        {
            _def = d;
            _go = new GameObject("DI " + d.Section + " " + d.Type);
            _go.layer = parent.gameObject.layer;
            _go.transform.SetParent(parent, false);
            var o = AcCarMath.ToUnity(d.Position) + new Vector3(0f, 0f, -0.0005f); // ours: 0.5 mm towards the driver against z-fighting
            float w = d.Width, h = d.Height;
            var m = new Mesh { name = "Ac display " + d.Type };
            m.vertices = new[] { o, o + new Vector3(0f, h, 0f), o + new Vector3(w, h, 0f), o + new Vector3(w, 0f, 0f) };
            m.uv = new[] { new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f) }; // W's textures: image top at v = 0
            var c = AcCarDisplayColor.Of(d);
            m.colors = new[] { c, c, c, c };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            _go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = _go.AddComponent<MeshRenderer>();
            var mat = AcCarShading.CreateUnlit(tex);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _owned.Add(m); if (mat != null) _owned.Add(mat); _owned.Add(tex);
            _go.SetActive(false);
        }

        public static AcCarTextureItem Create(AcCarDigitalItemDef d, Transform parent, Func<string, Texture2D> texture)
        {
            if (d == null || parent == null || texture == null || d.Width <= 0f || d.Height <= 0f || d.TextureBase == null) return null;
            var t = texture(d.TextureBase);
            if (t == null) return null;
            return new AcCarTextureItem(d, parent, t);
        }

        public void Update(ref AcInstruments v)
        {
            int idx = v.Gear < 0 ? 0 : v.Gear + 1;
            int on = idx == _def.Trigger ? 1 : 0;
            if (on == _shown) return;
            _shown = on;
            if (_go != null) _go.SetActive(on == 1);
        }

        public void Invalidate() { _shown = -1; }

        public bool Shown { get { return _shown == 1; } }

        public void Destroy()
        {
            for (int i = 0; i < _owned.Count; i++) if (_owned[i] != null) UnityEngine.Object.Destroy(_owned[i]);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
