using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The light, sky and fog of a track, from the game's own files:
    /// <list type="bullet">
    /// <item>sun path from the layout's <c>data/lighting.ini</c> (SUN_PITCH_ANGLE, SUN_HEADING_ANGLE; missing on ks_black_cat_county /
    /// ks_highlands: 35 / 45, the most common values) and the time of day as AC's race.ini SUN_ANGLE (setting "world.time" 08:00-18:00, degrees,
    /// 16 per hour, 0 = 13:00 [BELIEF]);</item>
    /// <item>colours from the weather's <c>colorCurves.ini</c> (HORIZON, SKY, SUN, AMBIENT, each LOW/HIGH = r,g,b (0-255), multiplier;
    /// interpolated by the sun height with ANGLE_GAMMA [BELIEF on the curve]) times HDR_OFF_MULT (AC's non-HDR exposure);</item>
    /// <item>fog from the weather's <c>weather.ini [FOG]</c> COLOR (x HDR_OFF_MULT), BLEND, DISTANCE (setting "world.weather", default
    /// 4_mid_clear = AC's default race.ini weather).</item>
    /// </list>
    /// Writes the World shaders' globals (<see cref="AcMaterials.SetLight"/>), RenderSettings (fog kept ON + Linear, the ambient probe set
    /// directly - DREAMPORT_GUIDE section 7), one directional "Ac Sun" with soft shadows for URP shadow maps and other modules' URP materials,
    /// and a procedural sky dome (AC has no sky textures: it draws its sky from these colours). <see cref="Restore"/> puts the previous
    /// RenderSettings back.
    /// </summary>
    public static class AcTrackLook
    {
        sealed class Saved
        {
            public bool Fog; public FogMode Mode; public Color FogColor; public float Start, End, Density;
            public AmbientMode Ambient; public Color AmbientLight; public SphericalHarmonicsL2 Probe; public Light Sun; public Material Skybox;
        }

        public struct Values
        {
            public Vector3 SunDir;          // Unity world, towards the sun
            public float SunElevationDeg;
            public Color Sun, Ambient, Zenith, Horizon, Fog;
            public float FogBlend, FogDistance, HdrOffMult;
            public float SunPitch, SunHeading, SunAngle;
            public string Weather;
        }

        static Saved _saved;
        static GameObject _sunGo, _skyGo;
        static Mesh _skyMesh;
        static Material _skyMat;
        static AcIni _lighting, _curves, _weather;
        static string _weatherName;
        static bool _listening;

        public static Values Current { get; private set; }
        public static bool Applied { get { return _sunGo != null; } }

        /// <summary>Default sun pitch / heading when a layout has no lighting.ini (the most common values in the install). // ours</summary>
        public const float DefaultPitch = 35f, DefaultHeading = 45f;
        /// <summary>Time of day as AC's SUN_ANGLE when neither a session, the player nor a race.ini says (16 = 14:00). // ours fallback</summary>
        public const float DefaultSunAngle = 16f;
        public const string DefaultWeather = "4_mid_clear";

        /// <summary>The time of day in effect as SUN_ANGLE (AcTrackWorld.SunAngle: session, else the player's "world.time", else race.ini).</summary>
        public static float SunAngleSetting { get { return Mathf.Clamp(AcTrackWorld.SunAngle, -80f, 80f); } }
        /// <summary>The weather folder in effect (AcTrackWorld.Weather: session, else the player's "world.weather.id", else race.ini).</summary>
        public static string WeatherSetting { get { string w = AcTrackWorld.Weather; return string.IsNullOrEmpty(w) ? DefaultWeather : w; } }

        // ------------------------------------------------------------------ the maths (pure)

        /// <summary>Colour of a colorCurves.ini key: LOW/HIGH "r,g,b,mult" interpolated by <paramref name="f"/> (0 = sun on the horizon, 1 = high).</summary>
        static Color Curve(AcIni curves, string section, float f, float hdr, Color fallback)
        {
            var lo = curves != null ? curves.GetFloats(section, "LOW") : null;
            var hi = curves != null ? curves.GetFloats(section, "HIGH") : null;
            if (lo == null || lo.Length < 4 || hi == null || hi.Length < 4) return fallback;
            Color a = new Color(lo[0] / 255f * lo[3], lo[1] / 255f * lo[3], lo[2] / 255f * lo[3], 1f);
            Color b = new Color(hi[0] / 255f * hi[3], hi[1] / 255f * hi[3], hi[2] / 255f * hi[3], 1f);
            Color c = Color.Lerp(a, b, f) * hdr;
            c.a = 1f;
            return c;
        }

        /// <summary>Sun direction (Unity world, towards the sun) for AC's sun path [BELIEF on the axes and signs: noon elevation = 90 - pitch,
        /// rising in the east (AC +X) for negative angles, the path turned by the heading about the vertical].</summary>
        public static Vector3 SunDirection(float pitchDeg, float headingDeg, float sunAngleDeg)
        {
            float p = pitchDeg * Mathf.Deg2Rad, t = sunAngleDeg * Mathf.Deg2Rad, h = headingDeg * Mathf.Deg2Rad;
            // AC space: east +X, south +Z (map drawn x right / z down = north up, research 4.1)
            Vector3 noon = new Vector3(0f, Mathf.Cos(p), Mathf.Sin(p));
            Vector3 east = new Vector3(1f, 0f, 0f);
            Vector3 d = noon * Mathf.Cos(t) - east * Mathf.Sin(t);
            float c = Mathf.Cos(h), s = Mathf.Sin(h);
            Vector3 r = new Vector3(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
            return new Vector3(-r.x, r.y, r.z).normalized;   // AC -> Unity: mirror X
        }

        public static Values Compute(AcIni lighting, AcIni curves, AcIni weather, float sunAngle, string weatherName)
        {
            var v = new Values();
            v.SunPitch = lighting != null ? lighting.GetFloat("LIGHTING", "SUN_PITCH_ANGLE", DefaultPitch) : DefaultPitch;
            v.SunHeading = lighting != null ? lighting.GetFloat("LIGHTING", "SUN_HEADING_ANGLE", DefaultHeading) : DefaultHeading;
            v.SunAngle = sunAngle;
            v.Weather = weatherName;
            v.SunDir = SunDirection(v.SunPitch, v.SunHeading, sunAngle);
            v.SunElevationDeg = Mathf.Asin(Mathf.Clamp(v.SunDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            v.HdrOffMult = curves != null ? curves.GetFloat("HEADER", "HDR_OFF_MULT", 0.3f) : 0.3f;   // 0.3: the clear weathers' value if the file lacks it
            float gamma = curves != null ? curves.GetFloat("HEADER", "ANGLE_GAMMA", 2.4f) : 2.4f;
            float f = Mathf.Pow(Mathf.Clamp01(v.SunDir.y), 1f / Mathf.Max(0.1f, gamma));
            float hdr = v.HdrOffMult;
            v.Sun = Curve(curves, "SUN", f, hdr, new Color(2.6f, 2.5f, 2.3f));           // fallbacks: ours
            v.Ambient = Curve(curves, "AMBIENT", f, hdr, new Color(1.2f, 1.25f, 1.35f));
            v.Zenith = Curve(curves, "SKY", f, hdr, new Color(0.12f, 0.27f, 0.6f));
            v.Horizon = Curve(curves, "HORIZON", f, hdr, new Color(0.62f, 0.7f, 0.9f));
            if (v.SunDir.y < 0.02f)
            {
                // the sun at / below the horizon: no direct light (AC's time range stays above it) // ours
                float k = Mathf.Clamp01((v.SunDir.y + 0.05f) / 0.07f);
                v.Sun *= k;
            }
            var fc = weather != null ? weather.GetFloats("FOG", "COLOR") : null;
            v.Fog = fc != null && fc.Length >= 3 ? new Color(fc[0] * hdr, fc[1] * hdr, fc[2] * hdr, 1f) : v.Horizon;
            v.FogBlend = weather != null ? weather.GetFloat("FOG", "BLEND", 1f) : 1f;
            v.FogDistance = weather != null ? weather.GetFloat("FOG", "DISTANCE", 0f) : 0f;
            return v;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Applies a track's look (lighting.ini of the layout + the chosen weather) under <paramref name="root"/>. Main thread.</summary>
        public static void Apply(AcIni lighting, AcIni curves, AcIni weather, string weatherName, Transform root)
        {
            Save();
            _lighting = lighting; _curves = curves; _weather = weather; _weatherName = weatherName;
            var v = Compute(lighting, curves, weather, SunAngleSetting, weatherName);
            Write(v, root);
            if (!_listening) { AcSettings.Changed += OnSetting; _listening = true; }
            AcLog.Info("Track: look: weather " + (weatherName ?? "?") + ", sun pitch " + v.SunPitch.ToString("0") + " heading " + v.SunHeading.ToString("0") + " angle " +
                       v.SunAngle.ToString("0") + " -> elevation " + v.SunElevationDeg.ToString("0") + " deg, sun " + Fmt(v.Sun) + ", ambient " + Fmt(v.Ambient) +
                       ", sky " + Fmt(v.Zenith) + "/" + Fmt(v.Horizon) + ", fog " + Fmt(v.Fog) + " blend " + v.FogBlend.ToString("0.##") + " distance " + v.FogDistance.ToString("0") + " m (" + AcTrackWorld.LookSource + ")");
        }

        static string Fmt(Color c) { return "(" + c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00") + ")"; }

        static void OnSetting(string key)
        {
            if (key != "world.time" && key != "world.weather") return;
            if (_sunGo == null) return;
            try
            {
                if (key == "world.weather") AcTrackWorld.SyncWeatherId();   // the row stores an index; the folder id is the truth
                Refresh(key == "world.weather");
            }
            catch (Exception e) { AcLog.Warn("Track: look update failed: " + e.Message); }
        }

        /// <summary>Re-applies the look with the time / weather in effect (session value, else the player's choice, else race.ini): live on a
        /// loaded track (nothing in the hub). <paramref name="weatherToo"/> re-reads the weather files when it changed.</summary>
        public static void Refresh(bool weatherToo)
        {
            if (_sunGo == null) return;   // the hub keeps its light (F may set its own); the next track load reads the values in effect
            string w = WeatherSetting;
            if (weatherToo && !string.Equals(w, _weatherName, StringComparison.OrdinalIgnoreCase)) ReadWeather(w);
            var v = Compute(_lighting, _curves, _weather, SunAngleSetting, _weatherName);
            Write(v, _sunGo.transform.parent);
            AcLog.Info("Track: look changed: weather " + (_weatherName ?? "?") + ", sun angle " + v.SunAngle.ToString("0") + " -> elevation " + v.SunElevationDeg.ToString("0") + " deg (" + AcTrackWorld.LookSource + ")");
        }

        static void ReadWeather(string name)
        {
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst == null) return;
            string dir = Path.Combine(Path.Combine(inst.Content, "weather"), name);
            if (!Directory.Exists(dir)) return;
            _curves = AcIni.Load(Path.Combine(dir, "colorCurves.ini")) ?? _curves;
            _weather = AcIni.Load(Path.Combine(dir, "weather.ini")) ?? _weather;
            _weatherName = name;
        }

        /// <summary>The hub's light (no track): system/cfg colorCurves.ini + weather.ini of the chosen weather, default sun path. False without an install.</summary>
        public static bool TryApplyDefault()
        {
            try
            {
                var inst = AcGame.Current != null ? AcGame.Current.Install : null;
                if (inst == null) return false;
                string w = WeatherSetting;
                string dir = Path.Combine(Path.Combine(inst.Content, "weather"), w);
                AcIni curves = AcIni.Load(Path.Combine(dir, "colorCurves.ini")) ?? AcIni.Load(Path.Combine(Path.Combine(inst.SystemDir, "cfg"), "colorCurves.ini"));
                AcIni weather = AcIni.Load(Path.Combine(dir, "weather.ini")) ?? AcIni.Load(Path.Combine(Path.Combine(inst.SystemDir, "cfg"), "weather.ini"));
                if (curves == null) return false;
                var v = Compute(null, curves, weather, SunAngleSetting, w);
                AcMaterials.SetLight(v.SunDir, v.Sun, v.Ambient, v.Zenith, v.Horizon, v.Fog, v.FogBlend, 0f);   // no fog in the hub room
                AcLog.InfoOnce("World: default light from weather " + w + " (sun " + Fmt(v.Sun) + ", ambient " + Fmt(v.Ambient) + ")");
                return true;
            }
            catch (Exception e) { AcLog.Warn("World: default light failed: " + e.Message); return false; }
        }

        static void Write(Values v, Transform root)
        {
            Current = v;
            AcMaterials.SetLight(v.SunDir, v.Sun, v.Ambient, v.Zenith, v.Horizon, v.Fog, v.FogBlend, v.FogDistance);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            Color fogLin = Clamp01(v.Fog).linear;
            RenderSettings.fogColor = fogLin;
            RenderSettings.fogStartDistance = 0f;
            RenderSettings.fogEndDistance = v.FogDistance > 1f ? v.FogDistance : 100000f;

            // other modules' URP materials: ambient / sun scaled by AC's typical ksAmbient / ksDiffuse (0.4) // ours
            Color amb = Clamp01(v.Ambient * 0.4f).linear;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(amb);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
            RenderSettings.ambientLight = amb;

            if (_sunGo == null)
            {
                _sunGo = new GameObject("Ac Sun");
                _sunGo.AddComponent<Light>();
                MuteSceneSuns();
            }
            if (root != null && _sunGo.transform.parent != root) _sunGo.transform.SetParent(root, false);
            _sunGo.transform.rotation = Quaternion.LookRotation(-v.SunDir, Vector3.up);
            var light = _sunGo.GetComponent<Light>();
            light.type = LightType.Directional;
            float peak = Mathf.Max(0.001f, Mathf.Max(v.Sun.r, Mathf.Max(v.Sun.g, v.Sun.b)));
            light.color = new Color(v.Sun.r / peak, v.Sun.g / peak, v.Sun.b / peak, 1f).linear;
            light.intensity = Mathf.Clamp(peak * 0.4f, 0f, 3f);   // ours (URP materials only; our shaders read the globals)
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            RenderSettings.sun = light;

            if (_skyGo == null) BuildSky(root);
        }

        static Color Clamp01(Color c) { return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f); }

        static readonly System.Collections.Generic.List<Light> _muted = new System.Collections.Generic.List<Light>();

        /// <summary>
        /// The world scene's own directional light(s) (the hub's "Directional Light", intensity 1, soft shadows) are switched off while a track
        /// is loaded, else URP materials (avatars, other modules' Lit materials) would be lit twice (URP takes RenderSettings.sun = Ac Sun as the
        /// main light and the scene light as an additional one). Only enabled directional lights in AcGame's own scene are touched (never the
        /// client's); <see cref="Restore"/> switches exactly those back on.
        /// </summary>
        static void MuteSceneSuns()
        {
            try
            {
                var game = AcGame.Current;
                if (game == null) return;
                var scene = game.gameObject.scene;
                int n = 0;
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (l == null || !l.enabled || l.type != LightType.Directional || l.gameObject.scene != scene) continue;
                    if (_sunGo != null && l.gameObject == _sunGo) continue;
                    l.enabled = false; _muted.Add(l); n++;
                }
                if (n > 0) AcLog.InfoOnce("Track: the scene's directional light(s) switched off while a track is loaded (" + n + ", first '" + _muted[0].name + "'); Ac Sun lights the track");
            }
            catch (Exception e) { AcLog.Warn("Track: muting the scene light failed: " + e.Message); }
        }

        static void UnmuteSceneSuns()
        {
            for (int i = 0; i < _muted.Count; i++) if (_muted[i] != null) _muted[i].enabled = true;
            _muted.Clear();
        }

        /// <summary>A dome drawn around the eye at the far plane by the Sky shader (queue 2501+). // ours: the mesh</summary>
        static void BuildSky(Transform root)
        {
            Shader s = AcShaders.Find(AcMaterials.SkyShader);
            if (s == null) { AcLog.InfoOnce("Track: no sky shader carried - the camera's clear colour shows"); return; }
            _skyMesh = SphereMesh(24, 12);
            _skyMat = new Material(s) { name = "Ac Sky", renderQueue = AcMaterials.QueueSky };
            _skyGo = new GameObject("Ac Sky");
            if (root != null) _skyGo.transform.SetParent(root, false);
            _skyGo.AddComponent<MeshFilter>().sharedMesh = _skyMesh;
            var r = _skyGo.AddComponent<MeshRenderer>();
            r.sharedMaterial = _skyMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Mesh SphereMesh(int seg, int rings)
        {
            var v = new Vector3[(seg + 1) * (rings + 1)];
            int k = 0;
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * r / rings - Mathf.PI * 0.5f;
                for (int s = 0; s <= seg; s++)
                {
                    float lon = 2f * Mathf.PI * s / seg;
                    v[k++] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                }
            }
            var t = new int[seg * rings * 6];
            k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    t[k++] = a; t[k++] = b; t[k++] = a + 1;
                    t[k++] = a + 1; t[k++] = b; t[k++] = b + 1;
                }
            var m = new Mesh { name = "Ac Sky Dome" };
            m.vertices = v;
            m.triangles = t;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2e6f);   // drawn around the eye: never culled
            return m;
        }

        static void Save()
        {
            if (_saved != null) return;
            _saved = new Saved
            {
                Fog = RenderSettings.fog, Mode = RenderSettings.fogMode, FogColor = RenderSettings.fogColor, Start = RenderSettings.fogStartDistance,
                End = RenderSettings.fogEndDistance, Density = RenderSettings.fogDensity, Ambient = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight, Probe = RenderSettings.ambientProbe, Sun = RenderSettings.sun, Skybox = RenderSettings.skybox,
            };
        }

        /// <summary>Removes the sun and sky and puts the hub's RenderSettings back (track unload).</summary>
        public static void Restore()
        {
            if (_listening) { AcSettings.Changed -= OnSetting; _listening = false; }
            if (_sunGo != null) UnityEngine.Object.Destroy(_sunGo);
            UnmuteSceneSuns();
            if (_skyGo != null) UnityEngine.Object.Destroy(_skyGo);
            if (_skyMesh != null) UnityEngine.Object.Destroy(_skyMesh);
            if (_skyMat != null) UnityEngine.Object.Destroy(_skyMat);
            _sunGo = null; _skyGo = null; _skyMesh = null; _skyMat = null;
            _lighting = _curves = _weather = null;
            if (_saved == null) return;
            RenderSettings.fog = _saved.Fog; RenderSettings.fogMode = _saved.Mode; RenderSettings.fogColor = _saved.FogColor;
            RenderSettings.fogStartDistance = _saved.Start; RenderSettings.fogEndDistance = _saved.End; RenderSettings.fogDensity = _saved.Density;
            RenderSettings.ambientMode = _saved.Ambient; RenderSettings.ambientLight = _saved.AmbientLight; RenderSettings.ambientProbe = _saved.Probe;
            RenderSettings.sun = _saved.Sun; RenderSettings.skybox = _saved.Skybox;
            _saved = null;
            // the hub look again for kn5 materials built there (cars in the hub)
            AcMaterials.EnsureDefaultAgain();
        }
    }
}
