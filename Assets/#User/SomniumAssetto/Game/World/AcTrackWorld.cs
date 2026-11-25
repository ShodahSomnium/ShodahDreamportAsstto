using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>A spawn marker of the track (AC_START_n, AC_PIT_n, AC_HOTLAP_START_0) in Unity world space.</summary>
    public struct AcSpawnPoint
    {
        /// <summary>The n of AC_START_n / AC_PIT_n (0 = pole / first box).</summary>
        public int Index;
        /// <summary>The marker node position (Unity world). It FLOATS 0-7.7 m above the road (research T1): raycast down before placing a car.</summary>
        public Vector3 Marker;
        /// <summary>Travel heading in degrees (Unity yaw, from the HORIZONTAL part of node +Z only - research T2).</summary>
        public float HeadingDeg;
        /// <summary>Yaw-only rotation from <see cref="HeadingDeg"/>.</summary>
        public Quaternion Rotation { get { return Quaternion.Euler(0f, HeadingDeg, 0f); } }
        /// <summary>Normalised fast-lane progress of the marker (0..1; research T3 grid-before/after-the-line rule). NaN without an AI line.</summary>
        public float Progress;
    }

    /// <summary>A timing gate AC_TIME_n_L/R (gate 0 = start/finish; 1.. = sector splits) in Unity world space.</summary>
    public struct AcGate
    {
        public int Index;
        public Vector3 Left, Right;
        /// <summary>Unit vector of the race direction across the gate (from the AI line; zero when unknown).</summary>
        public Vector3 Forward;
    }

    /// <summary>A named corner / section of data/sections.ini (normalised fast-lane progress; may wrap past 1).</summary>
    public struct AcTrackSection
    {
        public float In, Out;
        public string Text;
    }

    /// <summary>The track's start-light meshes (KS_START_LIGHT_0..n, emissive per data/startinglights.ini). R drives them; W builds them.</summary>
    public sealed class AcStartLights
    {
        /// <summary>Number of lamps found (0 = the track has none; R then shows only the HUD lights). Lamps are numbered 0..Count-1 in the
        /// order of their KS_START_LIGHT_n index (a track numbering from 1 or with gaps still gets lamp 0 = its lowest n).</summary>
        public int Count;

        /// <summary>The lamps' renderers, lamp i = the i-th lowest KS_START_LIGHT_n (W fills them when a track is built).</summary>
        internal Renderer[][] Lamps = new Renderer[0][];
        /// <summary>The kn5 n of each lamp (KS_START_LIGHT_n).</summary>
        internal int[] Kn5Index = new int[0];
        /// <summary>Per lamp: the lamp panels' centres / mean normals in Unity world. One KS_START_LIGHT mesh may hold several panels (monza:
        /// the gantry 42 m ahead of the pole + a repeater 70 m behind it, facing the back of the grid); the mesh NODE sits at the origin of its
        /// FBX (magione / monza 100.kn5), so only the vertices tell where a lamp is.</summary>
        internal Vector3[][] PanelCentres = new Vector3[0][], PanelNormals = new Vector3[0][];
        /// <summary>startinglights.ini EMISSIVE (r, g, b 0-255) x INTENSITY, in AC emissive units.</summary>
        internal Color Red = new Color(2f, 0f, 0f, 1f);
        /// <summary>The go colour: the same intensity in green. // ours (startinglights.ini has only the red EMISSIVE; not used: lights go out)</summary>
        internal Color Green = new Color(0f, 2f, 0f, 1f);
        /// <summary>startinglights.ini DIFFUSE (0.3 on all 28 files): the lit lamp's ksDiffuse [BELIEF]. NaN = keep the material's.</summary>
        internal float Diffuse = float.NaN;
        int _reds = -1; bool _green;

        /// <summary>The state last set (for logs / the HUD).</summary>
        public int Reds { get { return Math.Max(0, _reds); } }
        public bool IsGreen { get { return _green; } }

        /// <summary>The panels of lamp <paramref name="lamp"/> (Unity world centres; empty when unknown). Allocates: not per frame.</summary>
        public Vector3[] Panels(int lamp) { return lamp >= 0 && lamp < PanelCentres.Length && PanelCentres[lamp] != null ? (Vector3[])PanelCentres[lamp].Clone() : new Vector3[0]; }

        /// <summary>The panel of lamp <paramref name="lamp"/> best seen from <paramref name="eye"/> (its normal facing the eye, then nearest):
        /// true with its centre and outward normal (Unity world).</summary>
        public bool PanelSeenFrom(int lamp, Vector3 eye, out Vector3 centre, out Vector3 normal)
        {
            centre = Vector3.zero; normal = Vector3.zero;
            if (lamp < 0 || lamp >= PanelCentres.Length || PanelCentres[lamp] == null || PanelCentres[lamp].Length == 0) return false;
            var cs = PanelCentres[lamp]; var ns = PanelNormals[lamp];
            float best = float.MaxValue;
            for (int i = 0; i < cs.Length; i++)
            {
                Vector3 to = eye - cs[i];
                float facing = ns[i].sqrMagnitude > 1e-8f ? Vector3.Dot(ns[i].normalized, to.normalized) : 0f;
                float score = to.magnitude * (facing > 0.3f ? 1f : 100f);   // // ours: facing panels first
                if (score < best) { best = score; centre = cs[i]; normal = ns[i]; }
            }
            return true;
        }

        /// <summary>Lights the first <paramref name="redCount"/> lamps red (0 = all off); <paramref name="green"/> = lights out (AC's circuit lamps go
        /// dark at the start - agreed with R; the KS_START_LIGHT meshes carry only the red EMISSIVE of startinglights.ini).
        /// On a drag / A2B start tree (<see cref="IsSemaphore"/>): lamp i = READY step i (semaphore.ini ORDER i, yellow), lit while i &lt; redCount;
        /// <paramref name="green"/> = the START lamps (green) on and the READY lamps off.</summary>
        public void SetLights(int redCount, bool green)
        {
            if (redCount == _reds && green == _green) return;
            _reds = redCount; _green = green;
            if (IsSemaphore) { ApplySemaphore(); return; }
            for (int i = 0; i < Lamps.Length; i++)
            {
                var rs = Lamps[i];
                if (rs == null) continue;
                bool on = !green && i < redCount;
                for (int k = 0; k < rs.Length; k++)
                    if (rs[k] != null) AcMaterials.SetLamp(rs[k], on, Red, Diffuse);
            }
        }

        // ---- drag / A2B start tree (data/semaphore.ini: [GROUPS] DISQUALIFIED / READY / START, [OBJECT_n] TYPE 0|1|2, NAME AC_SEMAPHORE_*, ORDER)
        internal sealed class SemLamp { public Renderer[] Rs; public Color C; public int Type, Order; public string Name; }
        internal List<SemLamp> Sem = new List<SemLamp>();
        bool _jump;

        /// <summary>The track's start lights are a drag / A2B tree (AC_SEMAPHORE_* lamps of data/semaphore.ini; ks_drag, trento-bondone):
        /// <see cref="Count"/> = the READY steps (3), driven by <see cref="SetLights"/> like circuit lamps.</summary>
        public bool IsSemaphore { get; internal set; }
        /// <summary>The DISQUALIFIED (red) lamps are lit (jump start).</summary>
        public bool JumpStart { get { return _jump; } }

        /// <summary>Lights / clears the tree's DISQUALIFIED lamps (semaphore.ini TYPE 0, red) - R calls it on a jump start. No-op on circuit lamps
        /// (AC's KS_START_LIGHT gantries have no jump-start lamp).</summary>
        public void SetJumpStart(bool on)
        {
            if (on == _jump) return;
            _jump = on;
            if (IsSemaphore) ApplySemaphore();
        }

        void ApplySemaphore()
        {
            for (int i = 0; i < Sem.Count; i++)
            {
                var l = Sem[i];
                bool on = l.Type == 0 ? _jump : l.Type == 2 ? _green : !_green && l.Order < _reds;
                for (int k = 0; k < l.Rs.Length; k++) if (l.Rs[k] != null) AcMaterials.SetLamp(l.Rs[k], on, l.C, float.NaN);
            }
        }

        internal void Reset()
        {
            Count = 0; Lamps = new Renderer[0][]; Kn5Index = new int[0]; PanelCentres = new Vector3[0][]; PanelNormals = new Vector3[0][];
            Diffuse = float.NaN; _reds = -1; _green = false; _jump = false; IsSemaphore = false; Sem.Clear();
        }
    }

    /// <summary>
    /// [W, World] The loaded track: builds a layout from the player's install (models.ini kn5 list, LODs, materials, collision meshes tagged
    /// with <see cref="AcSurfaceTag"/> on <see cref="AcLayers.Track"/>, markers hidden, lighting.ini sun), and answers what the race needs
    /// (spawn points, gates, AI line, surfaces, start lights). Main-thread API; the heavy reading runs on a worker inside <see cref="Load"/>
    /// (<see cref="AcTrackJob"/>: header scan, vertex conversion, tangents, texture preparation), the main thread uploads time-sliced
    /// (setting "world.loadslice" ms per frame) and MeshColliders are cooked on a background thread (<see cref="AcCooker"/>).
    /// </summary>
    public static class AcTrackWorld
    {
        /// <summary>A layout is built and drivable.</summary>
        public static bool Loaded { get; private set; }
        /// <summary>The layout being loaded / loaded (null in the hub).</summary>
        public static AcLayoutEntry Layout { get; private set; }
        /// <summary>Everything of the track hangs under this (null when nothing is loaded).</summary>
        public static Transform Root { get; private set; }
        /// <summary>Pit boxes AC_PIT_0..n (count == ui_track.json pitboxes).</summary>
        public static AcSpawnPoint[] PitBoxes { get; private set; } = new AcSpawnPoint[0];
        /// <summary>Grid slots AC_START_0..n (0 = pole, most advanced).</summary>
        public static AcSpawnPoint[] StartGrid { get; private set; } = new AcSpawnPoint[0];
        /// <summary>AC_HOTLAP_START_0 (Index -1 when the layout has none).</summary>
        public static AcSpawnPoint HotlapStart { get; private set; }
        /// <summary>Timing gates, sorted by index (gate 0 = start/finish).</summary>
        public static AcGate[] TimeGates { get; private set; } = new AcGate[0];
        /// <summary>ai/fast_lane.ai (racing line: lap progress, positions, AI driving). Null when missing (drift layouts). AC coordinates -
        /// convert with <see cref="ToUnity"/>/<see cref="ToAc"/>.</summary>
        public static AcAiSpline AiLine { get; private set; }
        /// <summary>ai/pit_lane.ai (null when missing).</summary>
        public static AcAiSpline PitLane { get; private set; }
        /// <summary>The layout's surfaces (layout surfaces.ini merged over system/data/surfaces.ini).</summary>
        public static AcSurfaces Surfaces { get; private set; }
        /// <summary>The start-light meshes (never null; Count 0 when the track has none).</summary>
        public static AcStartLights StartLights { get; private set; } = new AcStartLights();
        /// <summary>Physics layer of every track collider (road, kerbs, grass, walls).</summary>
        public const int CollisionLayer = AcLayers.Track;

        // ---- added by W
        /// <summary>A load is running.</summary>
        public static bool Loading { get; private set; }
        /// <summary>Progress 0..1 and stage text of the running / last load.</summary>
        public static float LoadProgress { get; private set; }
        public static string LoadStage { get; private set; } = "";
        /// <summary>Why the last load failed (null = it did not).</summary>
        public static string LoadError { get; private set; }
        /// <summary>Every classified marker of the layout (AC space; research 3.2).</summary>
        public static AcTrackMarkers Markers { get; private set; }
        /// <summary>Drag strip finish gate AC_OPEN_FINISH_L/R (Index -1 when none).</summary>
        public static AcGate OpenFinish { get; private set; }
        /// <summary>Point-to-point start / finish gates AC_AB_START_L/R, AC_AB_FINISH_L/R (trento-bondone; Index -1 when none).</summary>
        public static AcGate AbStart { get; private set; }
        public static AcGate AbFinish { get; private set; }
        /// <summary>data/sections.ini corner names (empty when the layout has none).</summary>
        public static AcTrackSection[] Sections { get; private set; } = new AcTrackSection[0];
        /// <summary>Lap length in metres (fast lane, else ui_track.json length; NaN when unknown).</summary>
        public static float LengthM { get; private set; } = float.NaN;
        /// <summary>Most cars a race can hold: min(#AC_START, #AC_PIT).</summary>
        public static int MaxCars { get { return Math.Min(StartGrid.Length, PitBoxes.Length); } }
        /// <summary>Bounds of everything drawn (Unity world).</summary>
        public static Bounds DrawnBounds { get; private set; }

        // ---- load state
        static int _gen;
        static AcTrackJob _job;
        static AcMatScope _scope;
        static readonly List<Mesh> _meshes = new List<Mesh>();
        static readonly List<Transform> _fileRoots = new List<Transform>();
        static readonly List<Dictionary<Kn5Node, Transform>> _nodeMaps = new List<Dictionary<Kn5Node, Transform>>();
        static readonly Dictionary<string, Transform> _nodesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        static readonly List<MeshCollider> _cookColliders = new List<MeshCollider>();
        static readonly List<Mesh> _cookMeshes = new List<Mesh>();
        static readonly List<AcCooker.Ticket> _cookTickets = new List<AcCooker.Ticket>();
        static readonly List<Renderer> _renderers = new List<Renderer>();
        static readonly Dictionary<Renderer, Vector3[]> _lampPanels = new Dictionary<Renderer, Vector3[]>();
        static AcLodDriver _lod;
        static int _consumed, _texUploaded, _texFailed, _colliders, _walls, _tagged, _untaggedPhysics;
        static long _tris, _texBytes, _meshBytes;
        static bool _settingsRegistered, _weatherRegistered, _shadows = true;

        // ------------------------------------------------------------------ settings

        /// <summary>Main-thread upload budget per frame while a track loads (ms). // ours</summary>
        public static float SliceMs { get { return Mathf.Clamp(AcSettings.Num("world.loadslice", 8f), 2f, 40f); } }
        /// <summary>3D grass tufts (ksGrass meshes, e.g. monza 1.kn5 1.9 M vertices); off helps VR frame rates. Applied at the next load.
        /// Default: on for monitor players, OFF in VR (brief #8 optional item).</summary>
        public static bool Grass { get { return AcSettings.Flag("world.grass", GrassDefault); } }
        static bool GrassDefault { get { bool xr = false; try { xr = AcPlayer.XrPresent; } catch (Exception) { } return !xr; } }
        /// <summary>Track meshes cast sun shadows where the kn5 says so (castShadows: monza 777 of 1027 drawn meshes, 1.7 M of 2.9 M triangles -
        /// harness wshadow); off saves the shadow pass (VR frame time). Applied at the next load.</summary>
        public static bool Shadows { get { return AcSettings.Flag("world.shadows", true); } }

        static readonly string[] TimeChoices =
        {
            "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "12:00", "12:30", "13:00",
            "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "18:00",
        };
        static string[] _weatherIds;
        static bool _weatherListening, _defaultsRead;
        static float _defaultSunAngle = AcTrackLook.DefaultSunAngle;
        static string _defaultWeather = AcTrackLook.DefaultWeather, _defaultsFrom = "ours";

        /// <summary>
        /// Adds W's rows to the tablet's Settings page (F's registry): 3D grass, time of day, weather. Call once the install is known
        /// (F's FrontBootDone; Load calls it too). Safe to call again (rows are replaced). The rows' defaults are the player's own last AC
        /// launcher choice (Documents/Assetto Corsa/cfg/race.ini SUN_ANGLE, [WEATHER] NAME), else the install's cfg/race.ini.
        /// </summary>
        public static void RegisterSettings()
        {
            try
            {
                ReadInstallDefaults();
                if (!_settingsRegistered)
                {
                    _settingsRegistered = true;
                    AcSettingsUi.Toggle("TRACK", "world.grass", "3D GRASS (NEXT TRACK LOAD)", GrassDefault);
                    AcSettingsUi.Toggle("TRACK", "world.shadows", "TRACK SHADOWS (NEXT TRACK LOAD)", true);
                    AcSettingsUi.Choice("TRACK", "world.time", "TIME OF DAY", TimeChoices, TimeIndex(_defaultSunAngle));
                }
                if (!_weatherRegistered)
                {
                    var ids = WeatherIds();
                    if (ids.Length > 0)
                    {
                        _weatherRegistered = true;
                        var names = new string[ids.Length];
                        var inst = AcGame.Current != null ? AcGame.Current.Install : null;
                        for (int i = 0; i < ids.Length; i++)
                        {
                            var ini = inst != null ? AcIni.Load(Path.Combine(Path.Combine(Path.Combine(inst.Content, "weather"), ids[i]), "weather.ini")) : null;
                            string n = ini != null ? ini.Get("LAUNCHER", "NAME", null) : null;
                            names[i] = string.IsNullOrEmpty(n) ? ids[i] : n.ToUpperInvariant();
                        }
                        // the choice row stores an index; the folder id ("world.weather.id") is the truth, so a weather mod added later does not
                        // shift the player's pick: put the index back where that folder is now
                        string id = AcSettings.Text("world.weather.id", null);
                        int at = IndexOf(ids, id);
                        if (at >= 0 && AcSettings.Int("world.weather", -1) != at) AcSettings.SetInt("world.weather", at);
                        AcSettingsUi.Choice("TRACK", "world.weather", "WEATHER", names, Math.Max(0, IndexOf(ids, _defaultWeather)));
                        if (!_weatherListening) { _weatherListening = true; AcSettings.Changed += OnWeatherIndex; }
                    }
                }
            }
            catch (Exception e) { AcLog.Warn("Track: settings rows failed: " + e.Message); }
        }

        static void OnWeatherIndex(string key)
        {
            if (key == "world.weather") SyncWeatherId();
        }

        /// <summary>Writes the folder id of the weather row's current index to "world.weather.id" (the row changed). AcTrackLook calls it too
        /// before it reads <see cref="Weather"/> on a change, whatever the order of the Changed handlers.</summary>
        internal static void SyncWeatherId()
        {
            var ids = WeatherIds();
            int i = AcSettings.Int("world.weather", -1);
            if (i < 0 || i >= ids.Length) return;
            if (!string.Equals(AcSettings.Text("world.weather.id", null), ids[i], StringComparison.Ordinal)) AcSettings.SetText("world.weather.id", ids[i]);
        }

        static int IndexOf(string[] ids, string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < ids.Length; i++) if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static int TimeIndex(float sunAngle)
        {
            float hour = 13f + sunAngle / 16f;
            return Mathf.Clamp(Mathf.RoundToInt((hour - 8f) * 2f), 0, TimeChoices.Length - 1);
        }

        /// <summary>The time / weather AC itself would use: the player's own last launcher race (Documents/Assetto Corsa/cfg/race.ini), else the
        /// install's cfg/race.ini (SUN_ANGLE=-48 = 10:00, [WEATHER] NAME=4_mid_clear here), else ours (14:00, 4_mid_clear).</summary>
        static void ReadInstallDefaults()
        {
            if (_defaultsRead) return;
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst == null) return;
            _defaultsRead = true;
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string[] paths =
                {
                    string.IsNullOrEmpty(docs) ? null : Path.Combine(Path.Combine(Path.Combine(docs, "Assetto Corsa"), "cfg"), "race.ini"),
                    Path.Combine(Path.Combine(inst.Root, "cfg"), "race.ini"),
                };
                string[] from = { "Documents/Assetto Corsa/cfg/race.ini", "install cfg/race.ini" };
                for (int i = 0; i < paths.Length; i++)
                {
                    if (paths[i] == null || !File.Exists(paths[i])) continue;
                    var ini = AcIni.Load(paths[i]);
                    if (ini == null) continue;
                    float a = ini.GetFloat("LIGHTING", "SUN_ANGLE", float.NaN);
                    string w = ini.Get("WEATHER", "NAME", null);
                    if (float.IsNaN(a) && string.IsNullOrEmpty(w)) continue;
                    if (!float.IsNaN(a)) _defaultSunAngle = Mathf.Clamp(a, -80f, 80f);
                    if (!string.IsNullOrEmpty(w)) _defaultWeather = w.Trim();
                    _defaultsFrom = from[i];
                    break;
                }
            }
            catch (Exception e) { AcLog.Warn("Track: reading race.ini defaults failed: " + e.Message); }
            AcLog.InfoOnce("Track: time / weather defaults from " + _defaultsFrom + ": SUN_ANGLE " + _defaultSunAngle.ToString("0") + " (" + TimeChoices[TimeIndex(_defaultSunAngle)] + "), weather " + _defaultWeather);
        }

        /// <summary>content/weather folder names, sorted (the weather row's choices; the setting itself is the folder id).</summary>
        public static string[] WeatherIds()
        {
            if (_weatherIds != null && _weatherIds.Length > 0) return _weatherIds;
            try
            {
                var inst = AcGame.Current != null ? AcGame.Current.Install : null;
                string dir = inst != null ? Path.Combine(inst.Content, "weather") : null;
                if (dir == null || !Directory.Exists(dir)) return new string[0];
                var l = new List<string>();
                foreach (var d in Directory.GetDirectories(dir)) if (File.Exists(Path.Combine(d, "colorCurves.ini"))) l.Add(Path.GetFileName(d));
                l.Sort(StringComparer.OrdinalIgnoreCase);
                _weatherIds = l.ToArray();
            }
            catch (Exception) { _weatherIds = new string[0]; }
            return _weatherIds;
        }

        // ---- session look (multiplayer: the server sets time and weather for everyone, like AC's server_cfg SUN_ANGLE / [WEATHER_n] GRAPHICS)
        /// <summary>SUN_ANGLE of the race session (NaN = none: the local choice applies). Set through <see cref="SetSessionLook"/>.</summary>
        public static float SessionSunAngle { get; private set; } = float.NaN;
        /// <summary>Weather folder of the race session (null = none). Set through <see cref="SetSessionLook"/>.</summary>
        public static string SessionWeather { get; private set; }

        /// <summary>
        /// R (session conductor) passes the race session's time and weather (server_cfg SUN_ANGLE and [WEATHER_0] GRAPHICS, carried in the
        /// race command so every client draws the same sky); NaN / null = no session value (the player's own choice applies). Applies live on a
        /// loaded track. Main thread.
        /// </summary>
        public static void SetSessionLook(float sunAngle, string weather)
        {
            float a = float.IsNaN(sunAngle) || float.IsInfinity(sunAngle) ? float.NaN : Mathf.Clamp(sunAngle, -80f, 80f);
            string w = string.IsNullOrEmpty(weather) ? null : weather.Trim();
            if (w != null && WeatherIds().Length > 0 && IndexOf(WeatherIds(), w) < 0)
            {
                AcLog.WarnOnce("Track: session weather '" + w + "' is not in content/weather here - the local choice is used");
                w = null;
            }
            bool same = (float.IsNaN(a) ? float.IsNaN(SessionSunAngle) : a == SessionSunAngle) && string.Equals(w, SessionWeather, StringComparison.OrdinalIgnoreCase);
            if (same) return;
            SessionSunAngle = a; SessionWeather = w;
            AcLog.Info("Track: session look: SUN_ANGLE " + (float.IsNaN(a) ? "local" : a.ToString("0")) + ", weather " + (w ?? "local"));
            try { AcTrackLook.Refresh(true); } catch (Exception e) { AcLog.Warn("Track: session look update failed: " + e.Message); }
        }

        /// <summary>The weather folder in effect: the session's, else the player's choice ("world.weather.id"), else race.ini's default.</summary>
        public static string Weather
        {
            get
            {
                if (SessionWeather != null) return SessionWeather;
                ReadInstallDefaults();
                var ids = WeatherIds();
                if (ids.Length == 0) return _defaultWeather;
                int at = IndexOf(ids, AcSettings.Text("world.weather.id", null));
                if (at >= 0) return ids[at];
                int i = AcSettings.Int("world.weather", -1);
                if (i >= 0 && i < ids.Length) return ids[i];
                int d = IndexOf(ids, _defaultWeather);
                return d >= 0 ? ids[d] : ids[0];
            }
        }

        /// <summary>The time of day in effect as AC's SUN_ANGLE (16 degrees per hour, 0 = 13:00): the session's, else the player's choice
        /// ("world.time"), else race.ini's SUN_ANGLE.</summary>
        public static float SunAngle
        {
            get
            {
                if (!float.IsNaN(SessionSunAngle)) return SessionSunAngle;
                ReadInstallDefaults();
                int i = AcSettings.Int("world.time", -1);
                if (i < 0 || i >= TimeChoices.Length) return _defaultSunAngle;
                float hour = 8f + i * 0.5f;
                return (hour - 13f) * 16f;
            }
        }

        /// <summary>Where the time / weather in effect come from ("session", "player", or the race.ini default) - for the look log line.</summary>
        public static string LookSource
        {
            get
            {
                string t = !float.IsNaN(SessionSunAngle) ? "session" : AcSettings.Int("world.time", -1) >= 0 ? "player" : _defaultsFrom;
                string w = SessionWeather != null ? "session" : AcSettings.Text("world.weather.id", null) != null || AcSettings.Int("world.weather", -1) >= 0 ? "player" : _defaultsFrom;
                return "time " + t + ", weather " + w;
            }
        }

        // ------------------------------------------------------------------ load

        /// <summary>Builds <paramref name="layout"/> (unloading any previous track first); run with AcGame.StartCoroutine. Reports
        /// progress (0..1, stage text) through <paramref name="progress"/>; ends with <see cref="Loaded"/> true or a logged failure.</summary>
        public static IEnumerator Load(AcLayoutEntry layout, Action<float, string> progress)
        {
            bool unloaded = false;
            if (Loaded || Loading || Root != null) { Unload(); unloaded = true; }
            int gen = ++_gen;
            LoadError = null;
            if (layout == null) { Fail("no layout given", progress); yield break; }
            Layout = layout;
            Loading = true;
            RegisterSettings();
            float t0 = Time.realtimeSinceStartup;
            _loadT0 = t0;
            WorstFrameMs = 0f; WorstFrameStage = "";
            long heapBefore = GC.GetTotalMemory(false);
            if (unloaded) for (int w = 0; w < 3 && gen == _gen; w++) yield return null;   // the previous track's textures / meshes are freed at the end of a frame
            if (gen != _gen) yield break;
            MemoryBeforeLoad = NativeMemory(); MemoryBuilt = 0;
            Report(progress, 0f, "Reading " + Name(layout));

            AcTrackJob job = null;
            string err;
            try { err = StartJob(layout, out job); }
            catch (Exception e) { err = "starting the reader: " + e; }
            if (err != null) { Fail(err, progress); yield break; }
            _job = job;
            while (!job.ScanDone)
            {
                if (gen != _gen) { job.Cancel(); yield break; }
                Report(progress, 0.03f, "Reading track files");
                yield return null;
                SampleFrame("reading");
            }
            if (gen != _gen) { job.Cancel(); yield break; }
            if (job.Failed) { Fail(job.Error ?? "reading failed", progress); yield break; }
            try { err = Begin(job); }
            catch (Exception e) { err = "setting up: " + e; }
            if (err != null) { job.Cancel(); Fail(err, progress); yield break; }
            yield return null;
            SampleFrame("setup (look, markers)");

            float budget = SliceMs;
            _shadows = Shadows;
            while (true)
            {
                if (gen != _gen) { job.Cancel(); yield break; }
                bool finished;
                try { finished = Consume(job, budget); }
                catch (Exception e) { job.Cancel(); Fail("building: " + e, progress); yield break; }
                float f = job.WorkTotal > 0 ? (float)_consumed / job.WorkTotal : 1f;
                Report(progress, 0.05f + 0.85f * f, _texUploaded < job.TexturesTotal ? "Loading textures " + _texUploaded + "/" + job.TexturesTotal : "Building the track " + (_consumed - _texUploaded - _texFailed) + "/" + (job.WorkTotal - job.TexturesTotal));
                if (finished) break;
                yield return null;
                SampleFrame(_texUploaded < job.TexturesTotal ? "textures" : "meshes");
            }
            if (job.Failed) { Fail(job.Error ?? "reading failed", progress); yield break; }

            while (true)
            {
                if (gen != _gen) yield break;
                bool cooked;
                try { cooked = CookStep(budget); }
                catch (Exception e) { Fail("collision: " + e, progress); yield break; }
                if (cooked) break;
                Report(progress, 0.92f, "Building collision " + (_colliders - _cookTickets.Count) + "/" + _colliders);
                yield return null;
                SampleFrame("collision");
            }
            try { Finish(job, Time.realtimeSinceStartup - t0, heapBefore); }
            catch (Exception e) { Fail("finishing: " + e, progress); yield break; }
            // the frame that ran Finish is part of the load too (brief #8): measure it before reporting Ready
            yield return null;
            if (gen != _gen) yield break;
            SampleFrame("finish");
            MemoryBuilt = NativeMemory();
            AcLog.Info("Track: ready " + layout.Key + ": worst frame after 1 s " + WorstFrameMs.ToString("0") + " ms (" + WorstFrameStage + "), allocated " + MemMb(MemoryBeforeLoad) + " -> " + MemMb(MemoryBuilt) +
                       " MB, graphics driver " + MemMb(GfxMemory()) + " MB" + FarPlaneFact());
            Loading = false;
            Loaded = true;
            _job = null;
            Report(progress, 1f, "Ready");
        }

        // ---- frame time and memory of a load (brief #6 / #8)
        static float _loadT0;
        /// <summary>The longest frame (ms) of the running / last load after its first second, and the load stage it happened in (brief #8:
        /// monza must stay at or under 50 ms). Sampled after every frame of <see cref="Load"/>, including setup, collision and finish.</summary>
        public static float WorstFrameMs { get; private set; }
        public static string WorstFrameStage { get; private set; } = "";
        /// <summary>Unity's total allocated memory (bytes, Profiler.GetTotalAllocatedMemoryLong: native + managed, textures and meshes
        /// included) right before the last load, when it was built, and a few frames after the last unload (0 = not measured yet).</summary>
        public static long MemoryBeforeLoad { get; private set; }
        public static long MemoryBuilt { get; private set; }
        public static long MemoryAfterUnload { get; private set; }

        static void SampleFrame(string stage)
        {
            if (Time.realtimeSinceStartup - _loadT0 <= 1f) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > WorstFrameMs) { WorstFrameMs = ms; WorstFrameStage = stage; }
        }

        static long NativeMemory() { try { return UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(); } catch (Exception) { return 0; } }
        /// <summary>Graphics-driver memory (0 in non-development players: Unity reports it only in the editor / development builds).</summary>
        static long GfxMemory() { try { return UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver(); } catch (Exception) { return 0; } }
        static string MemMb(long b) { return (b >> 20).ToString(); }

        /// <summary>The far plane the track needs (m; 0 = nothing loaded) - AcLodDriver keeps the player's camera at least this far.</summary>
        public static float WantFar { get { return _lod != null ? _lod.WantFar : 0f; } }

        static string FarPlaneFact()
        {
            if (_lod == null || _lod.WantFar <= 0f) return "";
            Camera cam = null;
            try { _lod.KeepFar(); cam = AcPlayer.Head(); } catch (Exception) { }
            return ", track needs far plane " + _lod.WantFar.ToString("0") + " m, player camera " + (cam != null ? "far " + AcLodDriver.ClientFarSeen.ToString("0") + " -> " + cam.farClipPlane.ToString("0") + " m" : "not found yet");
        }

        static IEnumerator LogAfterUnload(string key, long before, long gfxBefore, long heap0, long beforeLoad)
        {
            // Destroy() frees textures / meshes at the end of the frame: measure a few frames later
            for (int i = 0; i < 3; i++) yield return null;
            MemoryAfterUnload = NativeMemory();
            AcLog.Info("Track: memory after unloading " + key + ": allocated " + MemMb(before) + " -> " + MemMb(MemoryAfterUnload) + " MB (before the load " + MemMb(beforeLoad) +
                       " MB), graphics driver " + MemMb(gfxBefore) + " -> " + MemMb(GfxMemory()) + " MB, managed heap " + (heap0 >> 20) + " -> " + (GC.GetTotalMemory(false) >> 20) + " MB");
        }

        static string Name(AcLayoutEntry l) { return string.IsNullOrEmpty(l.Name) ? l.Key : l.Name; }

        static void Report(Action<float, string> progress, float f, string stage)
        {
            LoadProgress = f; LoadStage = stage;
            if (progress == null) return;
            try { progress(f, stage); } catch (Exception e) { AcLog.Warn("Track: progress callback: " + e.Message); }
        }

        static void Fail(string why, Action<float, string> progress)
        {
            string key = Layout != null ? Layout.Key : "?";
            AcLog.Warn("Track: loading " + key + " failed: " + why);
            Cleanup();
            LoadError = why;
            Loading = false;
            Loaded = false;
            Report(progress, 1f, "Failed: " + why);
        }

        static string StartJob(AcLayoutEntry layout, out AcTrackJob job)
        {
            job = null;
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (!layout.Owned) return "layout " + layout.Key + " is not installed (DLC stub or missing kn5)";
            var opt = new AcTrackJobOptions
            {
                Grass = Grass,
                Weather = Weather,
                SystemDir = inst != null ? inst.SystemDir : null,
                WeatherDir = inst != null ? Path.Combine(inst.Content, "weather") : null,
                SystemSurfacesIni = inst != null ? inst.SystemSurfacesIni : null,
            };
            job = new AcTrackJob(layout, opt);
            var th = new Thread(job.Run) { IsBackground = true, Name = "Assetto track " + layout.Key };
            th.Start();
            return null;
        }

        /// <summary>After the scan: root, look, markers, spawn points, gates, start lights' colours, log line.</summary>
        static string Begin(AcTrackJob job)
        {
            var L = job.Layout;
            var go = new GameObject("Ac Track " + L.Key);
            go.transform.position = AcSpace.MapOrigin;
            Root = go.transform;
            _scope = AcMaterials.Acquire("track|" + L.Key + "|" + _gen);
            for (int f = 0; f < job.Files.Count; f++)
            {
                var m = job.Placements[f];
                var fr = new GameObject(Path.GetFileName(job.Files[f].Path)).transform;
                fr.SetParent(Root, false);
                fr.localPosition = new Vector3(-m.Position[0], m.Position[1], m.Position[2]);
                fr.localRotation = Quaternion.Euler(0f, m.Rotation[0], 0f);   // models.ini ROTATION[0] = yaw (research 1.3 / T5)
                _fileRoots.Add(fr);
                _nodeMaps.Add(new Dictionary<Kn5Node, Transform>());
            }
            _lod = go.AddComponent<AcLodDriver>();

            Surfaces = job.Surfaces;
            AiLine = job.FastLane;
            PitLane = job.PitLane;
            Markers = job.Markers;
            LengthM = AiLine != null && AiLine.LapLength > 1f ? AiLine.LapLength : L.LengthM;
            BuildMarkers(job);
            Sections = ReadSections(L);

            try { AcTrackLook.Apply(job.LightingIni, job.ColorCurvesIni, job.WeatherIni, job.WeatherUsed, Root); }
            catch (Exception e) { AcLog.Warn("Track: look failed: " + e.Message); }

            var sl = job.StartingLightsIni;
            var em = sl != null ? sl.GetFloats("SETTINGS", "EMISSIVE") : null;
            float intensity = sl != null ? sl.GetFloat("SETTINGS", "INTENSITY", 1f) : 1f;
            if (em != null && em.Length >= 3) StartLights.Red = new Color(em[0] / 255f * intensity, em[1] / 255f * intensity, em[2] / 255f * intensity, 1f);
            float dif = sl != null ? sl.GetFloat("SETTINGS", "DIFFUSE", float.NaN) : float.NaN;
            StartLights.Diffuse = dif >= 0f ? dif : float.NaN;
            float peak = Mathf.Max(StartLights.Red.r, Mathf.Max(StartLights.Red.g, StartLights.Red.b));
            StartLights.Green = new Color(0f, peak > 0f ? peak : intensity, 0f, 1f);

            AcLog.Info("Track: loading " + L.Key + ": " + job.Files.Count + " kn5, " + job.MeshesTotal + " meshes (" + job.MeshesRendered + " drawn, " + job.MeshesCollide +
                       " colliders, " + job.MarkerCubes + " marker cubes hidden, " + job.InactiveHidden + " inactive" + (job.GrassSkipped > 0 ? ", " + job.GrassSkipped + " grass skipped" : "") +
                       "), " + job.TexturesTotal + " textures, " + job.VerticesRendered + " vertices; scan " + job.ScanMs + " ms" +
                       (job.Problems.Count > 0 ? "; problems: " + string.Join("; ", job.Problems.GetRange(0, Math.Min(6, job.Problems.Count)).ToArray()) : ""));
            AcLog.Info("Track: markers: " + StartGrid.Length + " grid, " + PitBoxes.Length + " pits, gates " + TimeGates.Length + ", hotlap " + (HotlapStart.Index >= 0 ? "yes" : "no") +
                       ", start lights " + job.Markers.StartLights.Count + (OpenFinish.Index >= 0 ? ", open finish" : "") + (AbStart.Index >= 0 ? ", A-B start/finish" : "") +
                       ", fast lane " + (AiLine != null ? AiLine.Count + " points " + AiLine.LapLength.ToString("0") + " m" : "none") + ", pit lane " + (PitLane != null ? PitLane.Count + " points" : "none") +
                       ", pole progress " + (StartGrid.Length > 0 && !float.IsNaN(StartGrid[0].Progress) ? StartGrid[0].Progress.ToString("0.0000") : "?"));
            return null;
        }

        // ------------------------------------------------------------------ markers

        static void BuildMarkers(AcTrackJob job)
        {
            var mk = job.Markers;
            StartGrid = Spawns(mk.Starts);
            PitBoxes = Spawns(mk.Pits);
            HotlapStart = mk.HotlapStarts.Count > 0 ? Spawn(mk.HotlapStarts[0]) : new AcSpawnPoint { Index = -1, Progress = float.NaN };
            var gates = new List<AcGate>();
            for (int i = 0; i < mk.Gates.Count; i++) if (mk.Gates[i].Complete) gates.Add(Gate(mk.Gates[i], mk.Gates[i].Index));
            TimeGates = gates.ToArray();
            OpenFinish = mk.OpenFinish != null && mk.OpenFinish.Complete ? Gate(mk.OpenFinish, 0) : new AcGate { Index = -1 };
            AbStart = mk.AbStart != null && mk.AbStart.Complete ? Gate(mk.AbStart, 0) : new AcGate { Index = -1 };
            AbFinish = mk.AbFinish != null && mk.AbFinish.Complete ? Gate(mk.AbFinish, 0) : new AcGate { Index = -1 };
        }

        static AcSpawnPoint[] Spawns(List<AcMarker> l)
        {
            var a = new AcSpawnPoint[l.Count];
            for (int i = 0; i < l.Count; i++) a[i] = Spawn(l[i]);
            return a;
        }

        static AcSpawnPoint Spawn(AcMarker m)
        {
            var p = m.Position;
            float heading = m.HeadingDeg;
            float progress = float.NaN;
            if (AiLine != null)
            {
                int hint = -1;
                progress = AiLine.Progress(p.X, p.Y, p.Z, ref hint);
                if (float.IsNaN(heading) && hint >= 0) heading = LineHeading(hint);
            }
            if (float.IsNaN(heading)) heading = 0f;
            return new AcSpawnPoint { Index = m.Index, Marker = ToUnity(p.X, p.Y, p.Z), HeadingDeg = heading, Progress = progress };
        }

        /// <summary>Unity yaw of the fast lane at point <paramref name="i"/>.</summary>
        static float LineHeading(int i)
        {
            int j = i + 1 >= AiLine.Count ? 0 : i + 1;
            float dx = -(AiLine.X(j) - AiLine.X(i)), dz = AiLine.Z(j) - AiLine.Z(i);
            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        static AcGate Gate(AcTimeGate g, int index)
        {
            var l = g.Left.Position; var r = g.Right.Position;
            var gate = new AcGate { Index = index, Left = ToUnity(l.X, l.Y, l.Z), Right = ToUnity(r.X, r.Y, r.Z) };
            Vector3 fwd = Vector3.zero;
            if (AiLine != null)
            {
                int i = AiLine.Nearest((l.X + r.X) * 0.5f, (l.Y + r.Y) * 0.5f, (l.Z + r.Z) * 0.5f);
                if (i >= 0)
                {
                    int j = i + 1, k = i;
                    if (j >= AiLine.Count) { if (AiLine.IsClosed) j = 0; else { j = i; k = Math.Max(0, i - 1); } }
                    fwd = new Vector3(-(AiLine.X(j) - AiLine.X(k)), 0f, AiLine.Z(j) - AiLine.Z(k));
                }
            }
            if (fwd.sqrMagnitude < 1e-6f)
            {
                // L is LEFT of travel (research 3.2): forward = (R - L) x up in Unity's left-handed frame
                Vector3 across = gate.Right - gate.Left; across.y = 0f;
                fwd = Vector3.Cross(across, Vector3.up);
            }
            fwd.y = 0f;
            gate.Forward = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.zero;
            return gate;
        }

        static AcTrackSection[] ReadSections(AcLayoutEntry L)
        {
            try
            {
                var ini = AcIni.Load(Path.Combine(L.DataDir ?? "", "sections.ini"));
                if (ini == null) return new AcTrackSection[0];
                var secs = ini.WithPrefix("SECTION_");
                var a = new List<AcTrackSection>();
                for (int i = 0; i < secs.Count; i++)
                    a.Add(new AcTrackSection { In = secs[i].GetFloat("IN", 0f), Out = secs[i].GetFloat("OUT", 0f), Text = secs[i].Get("TEXT", "") });
                return a.ToArray();
            }
            catch (Exception) { return new AcTrackSection[0]; }
        }

        // ------------------------------------------------------------------ building (main thread)

        /// <summary>Uploads queued items for up to <paramref name="budgetMs"/>. True when the worker is done and the queue empty.</summary>
        static bool Consume(AcTrackJob job, float budgetMs)
        {
            float start = Time.realtimeSinceStartup * 1000f;
            AcJobItem it;
            while (Time.realtimeSinceStartup * 1000f - start < budgetMs)
            {
                bool done = job.Done;   // read BEFORE taking: everything produced before Done is in the queue
                if (!job.TryTake(out it)) return done;
                if (it.Tex != null) UploadTexture(it);
                else if (it.Mesh != null) BuildMesh(job, it.Mesh);
                _consumed++;
            }
            return false;
        }

        static void UploadTexture(AcJobItem it)
        {
            var tex = AcTextures.Upload(it.Tex, Path.GetFileName(_job != null && it.TexRef.File < _job.Files.Count ? _job.Files[it.TexRef.File].Path : null));
            _scope.Textures[it.TexRef.Key] = tex;
            if (tex != null) { _texUploaded++; long b = AcTextures.BytesOf(it.Tex); _texBytes += b; _scope.TextureBytes += b; }
            else _texFailed++;
            it.Tex.Data = null;
        }

        static Transform EnsureNode(int file, Kn5Node n)
        {
            if (n == null) return _fileRoots[file];
            var map = _nodeMaps[file];
            Transform t;
            if (map.TryGetValue(n, out t)) return t;
            var parent = EnsureNode(file, n.Parent);
            var go = new GameObject(n.Name);
            t = go.transform;
            t.SetParent(parent, false);
            if (n.IsDummy) AcKn5Upload.SetLocal(t, n.Local);
            map[n] = t;
            if (!_nodesByName.ContainsKey(n.Name)) _nodesByName[n.Name] = t;
            return t;
        }

        static Material MaterialFor(AcTrackJob job, int file, Kn5Material m, int id)
        {
            string key = file + "|" + id;
            Material mat;
            if (_scope.Materials.TryGetValue(key, out mat)) return mat;
            var shading = job.Shading[file];
            var sh = id >= 0 && id < shading.Length ? shading[id] : null;
            mat = AcMaterials.Create(m, sh, new TexLookup(file, job.Files.Count).Get);
            _scope.Materials[key] = mat;
            return mat;
        }

        /// <summary>A material's texture: its own kn5 first, then the layout's other files (the job read it from where it was found).</summary>
        sealed class TexLookup
        {
            readonly int _file, _files;
            public TexLookup(int file, int files) { _file = file; _files = files; }
            public Texture2D Get(string name)
            {
                var t = _scope.Tex(AcTrackJob.TexKey(_file, name));
                for (int f = 0; t == null && f < _files; f++) if (f != _file) t = _scope.Tex(AcTrackJob.TexKey(f, name));
                return t;
            }
        }

        static void BuildMesh(AcTrackJob job, AcMeshPrep p)
        {
            var parent = EnsureNode(p.File, p.Node.Parent);
            if (p.Render)
            {
                var mesh = AcKn5Upload.RenderMesh(p, false);
                _meshes.Add(mesh);
                _meshBytes += (long)p.VertexCount * p.Stride * 4 + p.IndexCount * 2;
                var go = new GameObject(p.Node.Name);
                go.layer = AcLayers.World;
                var t = go.transform;
                t.SetParent(parent, false);
                Transform existing;
                if (!_nodeMaps[p.File].TryGetValue(p.Node, out existing)) _nodeMaps[p.File][p.Node] = t;
                if (!_nodesByName.ContainsKey(p.Node.Name)) _nodesByName[p.Node.Name] = t;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var mat = MaterialFor(job, p.File, p.Material, p.Node.Mesh.MaterialId);
                r.sharedMaterial = mat;
                bool transparent = mat != null && mat.renderQueue >= AcMaterials.QueueTransparent;
                r.shadowCastingMode = _shadows && p.Node.Mesh.CastShadows && !transparent ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _renderers.Add(r);
                if (IsLampNode(p.Node)) _lampPanels[r] = LampPanels(p);
                _tris += p.IndexCount / 3;
                var km = p.Node.Mesh;
                if (km.LodOut > 0f && _lod != null)
                    _lod.Add(r, new Vector3((p.MinX + p.MaxX) * 0.5f, (p.MinY + p.MaxY) * 0.5f, (p.MinZ + p.MaxZ) * 0.5f), km.LodIn, km.LodOut);
            }
            if (p.Collide && p.ColliderPositions != null)
            {
                var cm = AcKn5Upload.ColliderMesh(p);
                _meshes.Add(cm);
                _meshBytes += (long)p.VertexCount * 12 + p.IndexCount * 2;
                var cgo = new GameObject(p.Node.Name + " (collider)");
                cgo.layer = CollisionLayer;
                cgo.transform.SetParent(parent, false);
                var mc = cgo.AddComponent<MeshCollider>();
                _colliders++;
                if (p.Wall) { _walls++; AcSurfaceTag.Register(mc, null, true); }
                else
                {
                    var s = p.Surface ?? (Surfaces != null ? Surfaces.Default : null);
                    if (s != null) { AcSurfaceTag.Register(mc, s, false); _tagged++; } else _untaggedPhysics++;
                }
                var ticket = AcCooker.Queue(cm);
                if (ticket.Done) mc.sharedMesh = cm;
                else { _cookColliders.Add(mc); _cookMeshes.Add(cm); _cookTickets.Add(ticket); }
            }
            p.Vertices = null; p.ColliderPositions = null; p.Indices = null;
        }

        static bool IsLampNode(Kn5Node n)
        {
            for (; n != null; n = n.Parent) if (n.Name != null && n.Name.StartsWith("KS_START_LIGHT", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>A lamp mesh's vertices clustered into panels (3 m // ours): centres then mean normals, in the mesh's local (Unity) space.</summary>
        static Vector3[] LampPanels(AcMeshPrep p)
        {
            var c = new List<Vector3>(); var nsum = new List<Vector3>(); var cnt = new List<int>();
            var v = p.Vertices; int st = p.Stride;
            if (v == null || st < 6) return new Vector3[0];
            for (int i = 0; i + st <= v.Length && i / st < p.VertexCount; i += st)
            {
                var pos = new Vector3(v[i], v[i + 1], v[i + 2]);
                int k = -1;
                for (int j = 0; j < c.Count && k < 0; j++) if ((c[j] / cnt[j] - pos).sqrMagnitude < 9f) k = j;
                if (k < 0) { c.Add(Vector3.zero); nsum.Add(Vector3.zero); cnt.Add(0); k = c.Count - 1; }
                c[k] += pos; nsum[k] += new Vector3(v[i + 3], v[i + 4], v[i + 5]); cnt[k]++;
            }
            var res = new Vector3[c.Count * 2];
            for (int j = 0; j < c.Count; j++) { res[j] = c[j] / cnt[j]; res[c.Count + j] = nsum[j]; }
            return res;
        }

        /// <summary>Assigns colliders whose cooking finished, for up to <paramref name="budgetMs"/> (an assignment cooks inline when the bake
        /// cache misses). True when none is left.</summary>
        static bool CookStep(float budgetMs)
        {
            float start = Time.realtimeSinceStartup * 1000f;
            for (int i = _cookTickets.Count - 1; i >= 0; i--)
            {
                if (!_cookTickets[i].Done) continue;
                if (_cookColliders[i] != null) _cookColliders[i].sharedMesh = _cookMeshes[i];
                _cookTickets.RemoveAt(i); _cookColliders.RemoveAt(i); _cookMeshes.RemoveAt(i);
                if (Time.realtimeSinceStartup * 1000f - start >= budgetMs) break;
            }
            return _cookTickets.Count == 0;
        }

        /// <summary>Start lights: the renderers under each KS_START_LIGHT_n node, compacted in n order (no holes), with their panels in world space.</summary>
        static void BuildLamps(AcTrackJob job)
        {
            var lamps = new List<Renderer[]>(); var idx = new List<int>(); var cen = new List<Vector3[]>(); var nor = new List<Vector3[]>();
            var mk = job.Markers.StartLights;   // sorted by Index (data core)
            for (int i = 0; i < mk.Count; i++)
            {
                if (idx.Count > 0 && idx[idx.Count - 1] == mk[i].Index) continue;   // the same n twice: the first wins
                Transform t = null;
                for (int f = 0; f < job.Files.Count && t == null; f++)
                    if (string.Equals(job.Files[f].Path, mk[i].Kn5File, StringComparison.OrdinalIgnoreCase)) _nodeMaps[f].TryGetValue(mk[i].Node, out t);
                if (t == null) _nodesByName.TryGetValue(mk[i].Name, out t);
                var rs = t != null ? t.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                var pc = new List<Vector3>(); var pn = new List<Vector3>();
                for (int k = 0; k < rs.Length; k++)
                {
                    Vector3[] loc;
                    if (!_lampPanels.TryGetValue(rs[k], out loc)) continue;
                    int n = loc.Length / 2;
                    for (int j = 0; j < n; j++) { pc.Add(rs[k].transform.TransformPoint(loc[j])); pn.Add(rs[k].transform.TransformDirection(loc[n + j])); }
                }
                lamps.Add(rs); idx.Add(mk[i].Index); cen.Add(pc.ToArray()); nor.Add(pn.ToArray());
            }
            _lampPanels.Clear();
            if (lamps.Count == 0 && job.SemaphoreIni != null) { BuildSemaphore(job.SemaphoreIni); return; }
            StartLights.Lamps = lamps.ToArray();
            StartLights.Kn5Index = idx.ToArray();
            StartLights.PanelCentres = cen.ToArray();
            StartLights.PanelNormals = nor.ToArray();
            StartLights.Count = lamps.Count;
            StartLights.SetLights(0, false);
            if (lamps.Count > 0)
            {
                // one fact for the client log: where lamp 0 is seen from the pole (T5: models.ini ROTATION; the panel must face the grid)
                string where = "";
                Vector3 c, n;
                if (StartGrid.Length > 0 && StartLights.PanelSeenFrom(0, StartGrid[0].Marker, out c, out n))
                {
                    Vector3 to = c - StartGrid[0].Marker; Vector3 toH = new Vector3(to.x, 0f, to.z);
                    where = ", lamp 0 (" + cen[0].Length + " panel(s)) " + toH.magnitude.ToString("0") + " m from the pole, ahead-dot " +
                            Vector3.Dot(toH.normalized, StartGrid[0].Rotation * Vector3.forward).ToString("0.00") + ", faces the pole " +
                            (n.sqrMagnitude > 1e-8f ? Vector3.Dot(n.normalized, -to.normalized).ToString("0.00") : "?");
                }
                AcLog.Info("Track: start lights " + lamps.Count + " (KS_START_LIGHT_" + idx[0] + ".." + idx[idx.Count - 1] + "), emissive " + StartLights.Red.r.ToString("0.##") + "," +
                           StartLights.Red.g.ToString("0.##") + "," + StartLights.Red.b.ToString("0.##") + " diffuse " + (float.IsNaN(StartLights.Diffuse) ? "material" : StartLights.Diffuse.ToString("0.##")) + where);
            }
        }

        /// <summary>A drag / A2B start tree from data/semaphore.ini (no KS_START_LIGHT lamps on these layouts): the AC_SEMAPHORE_* lamp meshes
        /// (drawn: AcTrackJob keeps them although they are marker children), emissive white x INTENSITY (per object or [COLOR]; the texture is the colour).</summary>
        static void BuildSemaphore(AcIni ini)
        {
            float baseInt = ini.GetFloat("COLOR", "INTENSITY", 1f);
            var objs = ini.WithPrefix("OBJECT_");
            int steps = 0, found = 0, missing = 0;
            var sl = StartLights;
            for (int i = 0; i < objs.Count; i++)
            {
                string name = objs[i].Get("NAME", "");
                if (string.IsNullOrEmpty(name)) continue;
                name = name.Trim();
                Transform t = FindNode(name);
                if (t == null) { missing++; continue; }
                var l = new AcStartLights.SemLamp { Rs = t.GetComponentsInChildren<Renderer>(true), Type = objs[i].GetInt("TYPE", 1), Order = Math.Max(0, objs[i].GetInt("ORDER", 0)), Name = name };
                float k = objs[i].GetFloat("INTENSITY", baseInt);
                // semaphore.ini has no colour, only INTENSITY: the lamp textures carry it (harness wlamptex: ks_drag lights.dds under the
                // yellow lamps 0.63,0.47,0.03, red 0.63,0.06,0.04, green 0.03,0.63,0.14; trento LIGHT_yellow/green/red.png), and the shaders add
                // emissive x txDiffuse - so the emissive is white x INTENSITY
                l.C = new Color(k, k, k, 1f);
                sl.Sem.Add(l); found++;
                if (l.Type == 1) steps = Math.Max(steps, l.Order + 1);
            }
            if (found == 0) { AcLog.Info("Track: semaphore.ini names no built AC_SEMAPHORE lamp (" + missing + " missing) - no start lights"); return; }
            int groupReady = ini.GetInt("GROUPS", "READY", steps);
            if (groupReady > 0 && groupReady != steps) AcLog.Info("Track: semaphore.ini [GROUPS] READY=" + groupReady + " but objects give " + steps + " steps - using the objects");
            var cen = new Vector3[steps][]; var nor = new Vector3[steps][];
            for (int s = 0; s < steps; s++)
            {
                var pc = new List<Vector3>();
                for (int i = 0; i < sl.Sem.Count; i++)
                    if (sl.Sem[i].Type == 1 && sl.Sem[i].Order == s)
                        for (int r = 0; r < sl.Sem[i].Rs.Length; r++) if (sl.Sem[i].Rs[r] != null) pc.Add(sl.Sem[i].Rs[r].bounds.center);
                cen[s] = pc.ToArray(); nor[s] = new Vector3[pc.Count];
            }
            sl.IsSemaphore = true;
            sl.Lamps = new Renderer[0][];
            sl.PanelCentres = cen; sl.PanelNormals = nor;
            sl.Count = steps;
            sl.SetLights(0, false);
            AcLog.Info("Track: start tree (semaphore.ini): " + found + " lamps (" + steps + " READY steps" + (missing > 0 ? ", " + missing + " names not built" : "") + "), intensity " + baseInt.ToString("0.##"));
        }

        static void Finish(AcTrackJob job, float seconds, long heapBefore)
        {
            Physics.SyncTransforms();
            BuildLamps(job);

            bool any = false;
            var bb = new Bounds();
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
            DrawnBounds = bb;
            if (_lod != null)
            {
                _lod.SweepAll();
                // the player's camera must see across the track (tracks span km; Unity's default far plane is 1000 m) // ours: 1500..6000 m
                Vector3 sz = bb.size;
                _lod.WantFar = any ? Mathf.Clamp(Mathf.Sqrt(sz.x * sz.x + sz.z * sz.z), 1500f, 6000f) : 0f;
            }

            int unmatched = job.Markers.UnmatchedPhysics.Count;
            AcLog.Info("Track: built " + job.Layout.Key + " in " + seconds.ToString("0.0") + " s (" + _renderers.Count + " meshes, " + _tris + " tris, " + _texUploaded + " textures " +
                       (_texBytes >> 20) + " MB" + (_texFailed > 0 ? " (" + _texFailed + " failed)" : "") + ", mesh data " + (_meshBytes >> 20) + " MB, " + _colliders + " colliders, " + _walls +
                       " walls, surfaces matched " + (_tagged - unmatched) + "/unmatched " + unmatched + (_untaggedPhysics > 0 ? "/untagged " + _untaggedPhysics : "") + ", LOD-managed " +
                       (_lod != null ? _lod.Count : 0) + ", mips made " + job.MipsMade + ", shadows " + (_shadows ? "kn5 castShadows" : "off (setting)") + "; worker " + (job.ScanMs + job.ProduceMs) + " ms, cooking " + AcCooker.Summary + ", worst frame so far " +
                       WorstFrameMs.ToString("0") + " ms, managed heap " + (heapBefore >> 20) + " -> " + (GC.GetTotalMemory(false) >> 20) + " MB)");
            if (unmatched > 0) AcLog.Info("Track: physics meshes without an exact surface key (contains/ROAD fallback): " + string.Join(" ", job.Markers.UnmatchedPhysics.ToArray()));
        }

        // ------------------------------------------------------------------ unload

        /// <summary>Destroys the track (meshes, textures, colliders, surface tags) and frees its memory.</summary>
        public static void Unload()
        {
            string key = Layout != null ? Layout.Key : null;
            bool had = Root != null || Loaded || Loading;
            long tex = _texBytes, mesh = _meshBytes;
            long heap0 = GC.GetTotalMemory(false);
            long mem0 = had ? NativeMemory() : 0, gfx0 = had ? GfxMemory() : 0;
            _gen++;
            Cleanup();
            Loaded = false; Loading = false; Layout = null;
            if (had)
            {
                AcPool<float>.Clear(); AcPool<ushort>.Clear();
                GC.Collect();
                AcLog.Info("Track: unloaded " + (key ?? "?") + " (freed ~" + (tex >> 20) + " MB textures, " + (mesh >> 20) + " MB meshes; managed heap " + (heap0 >> 20) + " -> " +
                           (GC.GetTotalMemory(false) >> 20) + " MB, material scopes left " + AcMaterials.ScopeCount + ")");
                MemoryAfterUnload = 0;
                try
                {
                    if (AcGame.Current != null && AcGame.Current.isActiveAndEnabled) AcGame.Current.StartCoroutine(LogAfterUnload(key ?? "?", mem0, gfx0, heap0, MemoryBeforeLoad));
                }
                catch (Exception e) { AcLog.Warn("Track: memory check after unload: " + e.Message); }
            }
        }

        /// <summary>Everything of a (partial) build destroyed; public state back to "nothing loaded".</summary>
        static void Cleanup()
        {
            if (_job != null) { _job.Cancel(); _job.Drain(); _job = null; }
            // never destroy a mesh the cooking thread may still be baking (an unload during "Building collision"): wait for it (one-time,
            // bounded) // ours: 3 s
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < _cookTickets.Count; i++) while (!_cookTickets[i].Done && sw.ElapsedMilliseconds < 3000) Thread.Sleep(1);
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Root = null;
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) UnityEngine.Object.Destroy(_meshes[i]);
            _meshes.Clear();
            if (_scope != null) { AcMaterials.Release(_scope); _scope = null; }
            AcSurfaceTag.Clear();
            try { AcTrackLook.Restore(); } catch (Exception e) { AcLog.Warn("Track: restoring the look failed: " + e.Message); }
            StartLights.Reset();
            _fileRoots.Clear(); _nodeMaps.Clear(); _nodesByName.Clear(); _renderers.Clear(); _lampPanels.Clear();
            _cookColliders.Clear(); _cookMeshes.Clear(); _cookTickets.Clear();
            _lod = null;
            _consumed = _texUploaded = _texFailed = _colliders = _walls = _tagged = _untaggedPhysics = 0;
            _tris = _texBytes = _meshBytes = 0;
            AiLine = null; PitLane = null; Surfaces = null; Markers = null;
            PitBoxes = new AcSpawnPoint[0]; StartGrid = new AcSpawnPoint[0]; TimeGates = new AcGate[0]; Sections = new AcTrackSection[0];
            HotlapStart = new AcSpawnPoint { Index = -1, Progress = float.NaN };
            OpenFinish = AbStart = AbFinish = new AcGate { Index = -1 };
            LengthM = float.NaN;
            DrawnBounds = new Bounds();
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The surface of a track collider (null for walls, cars, untagged colliders).</summary>
        public static AcSurface SurfaceAt(Collider c) { return AcSurfaceTag.Of(c); }

        /// <summary>The surface under a raycast hit (null when not a tagged track surface).</summary>
        public static AcSurface SurfaceAt(RaycastHit hit) { return AcSurfaceTag.Of(hit.collider); }

        /// <summary>Raycasts down onto the track/hub ground (<see cref="AcLayers.GroundMask"/>) from <paramref name="from"/> + 1 m (research
        /// T1: markers float above the road). True with the ground point and normal.</summary>
        public static bool TryGround(Vector3 from, out Vector3 point, out Vector3 normal)
        {
            RaycastHit hit;
            if (Physics.Raycast(from + Vector3.up, Vector3.down, out hit, 20f, AcLayers.GroundMask, QueryTriggerInteraction.Ignore)) // ours: 20 m reach
            {
                point = hit.point; normal = hit.normal; return true;
            }
            point = from; normal = Vector3.up; return false;
        }

        /// <summary>A built node of the track by kn5 name (first found; null when absent or not built - only nodes with drawn/collision meshes
        /// below them are built).</summary>
        public static Transform FindNode(string name)
        {
            Transform t;
            return name != null && _nodesByName.TryGetValue(name, out t) ? t : null;
        }

        /// <summary>The name of the section (corner) at a normalised progress, or null.</summary>
        public static string SectionAt(float progress)
        {
            var s = Sections;
            for (int i = 0; i < s.Length; i++)
            {
                float a = s[i].In, b = s[i].Out;
                bool inside = a <= b ? progress >= a && progress < b : progress >= a || progress < b;
                if (inside) return s[i].Text;
            }
            return null;
        }

        /// <summary>AC track coordinates -> Unity world (X mirror + <see cref="AcSpace.MapOrigin"/>).</summary>
        public static Vector3 ToUnity(float x, float y, float z) { return AcSpace.MapOrigin + new Vector3(-x, y, z); }

        /// <summary>Unity world -> AC track coordinates (for AcAiSpline.Progress / Nearest).</summary>
        public static void ToAc(Vector3 p, out float x, out float y, out float z)
        {
            Vector3 q = p - AcSpace.MapOrigin; x = -q.x; y = q.y; z = q.z;
        }
    }
}
