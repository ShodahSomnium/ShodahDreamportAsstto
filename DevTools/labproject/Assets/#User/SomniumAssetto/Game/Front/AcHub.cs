using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The hub at AcSpace.RoomOrigin (3 km above the tracks): Assetto Corsa's own showroom (content/showroom/showroom/showroom.kn5 -
    /// the room acShowroom opens in; measured with the harness `fbounds`: floor "Pavimento" 28 x 28 m at y 0, walls "Pareti" at +-13.95 m,
    /// ceiling at 5.0-5.5 m) built at run time by W's AcKn5Builder, with our own invisible floor and wall colliders on those measured planes (the
    /// kn5 has no physics meshes). Until the showroom is built (or if it cannot be) a plain grey floor of the same size stands there. The chosen
    /// car (Drive page) stands on the showroom floor (P's AcCarRig, Kind Showroom = no physics). The menu panel (the hub's copy of the tablet app,
    /// always there - BO2's rule) is placed in front of the player (AcPanelPlacer). The hub hides while a track loads / runs and comes back with
    /// the Hub phase. Keep rule (AcFront): a player outside the room for 2 s is put back. Light: the showroom's own colorCurves.ini (ApplyShowroomLight;
    /// RenderSettings are W's). The showroom car exists only while the hub is shown (ClearCar on Loading). With no linked tablet on a track the
    /// panel leaves the hidden hub and stands in front of the player there (DetachPanel / AttachPanel).
    /// </summary>
    public sealed class AcHub : MonoBehaviour
    {
        /// <summary>The showroom's floor half-size and wall height (showroom.kn5 measured, see the summary).</summary>
        public const float HalfSize = 14f, WallHeight = 5.5f;
        /// <summary>Beyond this from the centre (or 3 m below the floor) the player is off the hub (keep rule). // ours</summary>
        public const float KeepHalfSize = 15.5f;
        /// <summary>The showroom car's place on the floor: 4 m ahead of the spawn point, nose towards it. // ours</summary>
        public static readonly Vector3 CarSpot = new Vector3(0f, 0f, 4.5f);
        public const float CarYaw = 180f; // ours
        public const float PanelMetresPerUnit = 0.0016f; // ours: the 1600-unit menu is 2.56 m wide

        public static AcHub Current { get; private set; }

        private GameObject _plainFloor, _sunGo;
        private Material _floorMat;
        private Light _sun;
        private Canvas _panel;
        private AcPanelPlacer _placer;
        private AcKn5Model _showroom;
        private AcCarRig _car;
        private string _carKey;
        private int _carGeneration;
        // one showroom build at a time: a newer pick cancels the running build (root destroyed, P's Spawn drains C's builder) and waits as the
        // single pending request, started when the running one has ended - never a pile of parallel full car builds
        private AcCarSpawnHandle _carBuild;
        private AcCarEntry _pendingCar;
        private string _pendingSkin;
        private bool _hasPending;
        private bool _shown = true, _showroomStarted, _panelDetached;
        private int _lightLogs;

        public Canvas Panel { get { return _panel; } }
        public AcPanelPlacer Placer { get { return _placer; } }
        public bool Shown { get { return _shown; } }
        public bool ShowroomBuilt { get; private set; }
        public AcKn5Model Showroom { get { return _showroom; } }
        public AcCarRig Car { get { return _car; } }
        public string ShowroomReport { get; private set; } = "not started";

        public static AcHub Create(Transform parent)
        {
            var go = new GameObject("Ac Hub");
            go.transform.SetParent(parent, false);
            go.transform.position = AcSpace.RoomOrigin;
            go.transform.rotation = Quaternion.identity;
            var hub = go.AddComponent<AcHub>();
            Current = hub;
            hub.BuildBase();
            return hub;
        }

        private void OnDestroy()
        {
            if (_panelDetached && _panel != null) Destroy(_panel.gameObject); // a panel standing on a track is not under the hub any more
            DisposeCar();
            CancelCarBuild();
            if (_showroom != null) { try { _showroom.Dispose(); } catch (Exception) { } _showroom = null; }
            if (_floorMat != null) Destroy(_floorMat);
            if (Current == this) Current = null;
        }

        // ------------------------------------------------------------------ the room's solid parts and the plain floor

        private void BuildBase()
        {
            // floor: a 0.4 m slab whose top is the showroom's floor (y 0)
            var floor = new GameObject("Ac Hub Floor Collider");
            floor.layer = AcLayers.World;
            floor.transform.SetParent(transform, false);
            var fb = floor.AddComponent<BoxCollider>();
            fb.center = new Vector3(0f, -0.2f, 0f);
            fb.size = new Vector3(HalfSize * 2f + 1f, 0.4f, HalfSize * 2f + 1f);

            // walls on the showroom's wall planes (+-13.95 m), 0.5 m thick outward
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Ac Hub Wall " + i);
                w.layer = AcLayers.World;
                w.transform.SetParent(transform, false);
                var b = w.AddComponent<BoxCollider>();
                bool x = i < 2;
                float s = (i % 2 == 0) ? 1f : -1f;
                float off = HalfSize + 0.2f;
                b.center = x ? new Vector3(s * off, WallHeight * 0.5f, 0f) : new Vector3(0f, WallHeight * 0.5f, s * off);
                b.size = x ? new Vector3(0.5f, WallHeight, HalfSize * 2f + 1f) : new Vector3(HalfSize * 2f + 1f, WallHeight, 0.5f);
            }

            _plainFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plainFloor.name = "Ac Hub Plain Floor";
            Destroy(_plainFloor.GetComponent<Collider>());
            _plainFloor.transform.SetParent(transform, false);
            _plainFloor.transform.localScale = new Vector3(HalfSize * 0.2f, 1f, HalfSize * 0.2f); // a Unity plane is 10 m
            Shader sh = AcShaders.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                _floorMat = new Material(sh) { name = "Ac hub floor" };
                if (_floorMat.HasProperty("_BaseColor")) _floorMat.SetColor("_BaseColor", new Color(0.36f, 0.36f, 0.37f, 1f)); // ours
                if (_floorMat.HasProperty("_Smoothness")) _floorMat.SetFloat("_Smoothness", 0.35f);
                _plainFloor.GetComponent<MeshRenderer>().sharedMaterial = _floorMat;
            }

            // the hub's directional light for Unity URP materials (avatars, the plain floor) - a child of the hub, so it is off with the hub while
            // a track runs; colour / direction / intensity come from the showroom's own colorCurves.ini (ApplyShowroomLight) once the install
            // is read. RenderSettings (ambient, fog) are W's (AcTrackLook): the hub never writes them.
            _sunGo = new GameObject("Ac Hub Sun");
            _sunGo.transform.SetParent(transform, false);
            _sun = _sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _sun.intensity = 0f; // dark until the showroom's light is read (no invented light)
        }

        /// <summary>
        /// The showroom light as acShowroom.exe lights it: its string table names only "system/cfg/colorCurves.ini" (HDR_OFF_MULT, ANGLE_GAMMA,
        /// SUN / AMBIENT / SKY / HORIZON) - the showroom folder's own colorCurves.ini (purple AMBIENT 114,0,147, red HORIZON) is a leftover the
        /// exe never opens (it reads only settings.ini [LIGHT] LOCK_SUN / SUN_DIRECTION there, absent for "showroom"). That file is the fallback
        /// when system/cfg/colorCurves.ini is missing. Curves through W's AcTrackLook.Compute (the same maths as a track; the showroom has no lighting.ini, so
        /// W's default sun path and time of day) into the World shaders' globals (AcMaterials.SetLight, no fog indoors) and the hub's directional
        /// light (W's scaling for URP materials). Called at boot and every time the hub comes back (W's track weather would stay otherwise).
        /// </summary>
        public void ApplyShowroomLight(AcInstall install, string why)
        {
            if (install == null) return;
            try
            {
                string path = Path.Combine(Path.Combine(install.SystemDir, "cfg"), "colorCurves.ini");
                string source = "system/cfg/colorCurves.ini (as acShowroom.exe)";
                AcIni curves = File.Exists(path) ? AcIni.Load(path) : null;
                if (curves == null)
                {
                    path = Path.Combine(Path.Combine(install.ShowroomDir, "showroom"), "colorCurves.ini");
                    source = "content/showroom/showroom/colorCurves.ini (system/cfg one missing)";
                    curves = File.Exists(path) ? AcIni.Load(path) : null;
                }
                if (curves == null)
                {
                    if (_lightLogs++ < 2) AcLog.Warn("Front: no colorCurves.ini for the showroom (system/cfg nor " + path + ") - W's default light stays.");
                    return;
                }

                AcTrackLook.Values v = AcTrackLook.Compute(null, curves, null, AcTrackLook.DefaultSunAngle, "showroom");
                AcMaterials.SetLight(v.SunDir, v.Sun, v.Ambient, v.Zenith, v.Horizon, v.Fog, v.FogBlend, 0f);
                if (_sun != null)
                {
                    float peak = Mathf.Max(0.001f, Mathf.Max(v.Sun.r, Mathf.Max(v.Sun.g, v.Sun.b)));
                    _sun.color = new Color(v.Sun.r / peak, v.Sun.g / peak, v.Sun.b / peak, 1f).linear;
                    _sun.intensity = Mathf.Clamp(peak * 0.4f, 0f, 3f); // W's AcTrackLook scaling for URP materials (ours there)
                    _sunGo.transform.rotation = Quaternion.LookRotation(-v.SunDir, Vector3.up);
                }

                if (_lightLogs++ < 3)
                    AcLog.Info("Front: hub light from " + source + " (" + why + "): sun " + Fmt(v.Sun) + ", ambient " + Fmt(v.Ambient) + ", sky " + Fmt(v.Zenith) +
                               "/" + Fmt(v.Horizon) + ", elevation " + v.SunElevationDeg.ToString("0") + " deg.");
            }
            catch (Exception e) { AcLog.Warn("Front: the showroom light failed (" + e.GetType().Name + ": " + e.Message + ") - W's default light stays."); }
        }

        private static string Fmt(Color c) { return c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00"); }

        // ------------------------------------------------------------------ the showroom kn5 (W's builder)

        /// <summary>Reads showroom.kn5 on a worker and builds it time-sliced (once). Logs "Front: showroom built ...".</summary>
        public void BuildShowroom(AcInstall install)
        {
            if (_showroomStarted || install == null) return;
            _showroomStarted = true;
            string path = Path.Combine(Path.Combine(install.ShowroomDir, "showroom"), "showroom.kn5");
            if (!File.Exists(path)) { ShowroomReport = "no " + path; AcLog.Warn("Front: showroom not built - " + ShowroomReport + " (the plain floor stays)."); return; }
            Runner().StartCoroutine(ShowroomRoutine(path));
        }

        /// <summary>Coroutines run on the scene root (they survive the hub being hidden while a track loads).</summary>
        private MonoBehaviour Runner()
        {
            AcGame g = AcGame.Current;
            return g != null ? (MonoBehaviour)g : this;
        }

        private IEnumerator ShowroomRoutine(string path)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Kn5File kn5 = null;
            string error = null;
            bool done = false;
            var t = new Thread(() =>
            {
                try { kn5 = Kn5File.Read(path, Kn5ReadOptions.Full); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                done = true;
            }) { IsBackground = true, Name = "Assetto showroom" };
            t.Start();
            while (!done) yield return null;
            long readMs = watch.ElapsedMilliseconds;
            if (kn5 == null)
            {
                ShowroomReport = "reading " + path + " failed: " + error;
                AcLog.Warn("Front: showroom not built - " + ShowroomReport + " (the plain floor stays).");
                yield break;
            }

            AcKn5Model model = null;
            var options = new AcKn5BuildOptions { Layer = AcLayers.World, PhysicsColliders = false, HideMarkers = true };
            IEnumerator build = null;
            try { build = AcKn5Builder.BuildAsync(kn5, transform, options, null, m => model = m); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            if (build != null)
            {
                while (true)
                {
                    bool more;
                    try { more = build.MoveNext(); }
                    catch (Exception e) { error = e.GetType().Name + ": " + e.Message; break; }
                    if (!more) break;
                    yield return build.Current;
                }
            }

            try { kn5.Release(); } catch (Exception) { }
            if (this == null) yield break;
            _showroom = model;
            int renderers = model != null ? model.Renderers.Count : 0;
            if (model != null && model.Root != null)
            {
                model.Root.localPosition = Vector3.zero;
                model.Root.localRotation = Quaternion.identity;
                AcUi.SetLayerRecursive(model.Root, AcLayers.World);
            }

            ShowroomBuilt = renderers > 0;
            if (ShowroomBuilt && _plainFloor != null) _plainFloor.SetActive(false);
            ShowroomReport = model == null ? "the builder gave nothing" + (error != null ? " (" + error + ")" : "") : renderers + " renderers, " + kn5.Textures.Count + " textures, read " + readMs + " ms, built " + (watch.ElapsedMilliseconds - readMs) + " ms";
            if (ShowroomBuilt) AcLog.Info("Front: showroom built (content/showroom/showroom/showroom.kn5: " + ShowroomReport + ").");
            else AcLog.Warn("Front: showroom not built - " + ShowroomReport + " (W's AcKn5Builder; the plain floor stays).");
        }

        // ------------------------------------------------------------------ the chosen car on the floor

        /// <summary>Puts <paramref name="car"/> / <paramref name="skinId"/> on the showroom floor (P's AcCarRig Kind Showroom); null removes it.</summary>
        public void SetCar(AcCarEntry car, string skinId)
        {
            string key = car != null ? car.Id + "/" + (skinId ?? "") : null;
            if (key == _carKey) return;
            _carKey = key;
            DisposeCar();
            ++_carGeneration;
            _hasPending = false;
            if (_carBuild != null && !_carBuild.Finished)
            {
                _carBuild.Cancel();
                if (car != null && car.Owned) { _pendingCar = car; _pendingSkin = skinId; _hasPending = true; }
                return;
            }

            StartCarBuild(car, skinId);
        }

        private void StartCarBuild(AcCarEntry car, string skinId)
        {
            if (car == null || !car.Owned) return;
            int gen = _carGeneration;
            var handle = new AcCarSpawnHandle();
            var options = new AcCarRigOptions { Kind = AcCarKind.Showroom, Audio = false, Parent = transform, Handle = handle };
            IEnumerator spawn;
            try { spawn = AcCarRig.Spawn(car, skinId, options, rig => OnCar(gen, rig)); }
            catch (Exception e) { AcLog.Warn("Front: the showroom car could not start (" + e.GetType().Name + ": " + e.Message + ")."); return; }
            _carBuild = handle;
            Runner().StartCoroutine(RunCarBuild(Guarded(spawn, "showroom car " + car.Id), handle));
        }

        /// <summary>One showroom build to its end, then the pending pick (if any).</summary>
        private IEnumerator RunCarBuild(IEnumerator body, AcCarSpawnHandle handle)
        {
            while (body.MoveNext()) yield return body.Current;
            if (ReferenceEquals(_carBuild, handle)) _carBuild = null;
            if (this == null || !_hasPending || _carBuild != null) yield break;
            _hasPending = false;
            AcCarEntry car = _pendingCar;
            string skin = _pendingSkin;
            _pendingCar = null; _pendingSkin = null;
            StartCarBuild(car, skin);
        }

        private void CancelCarBuild()
        {
            _hasPending = false;
            _pendingCar = null; _pendingSkin = null;
            if (_carBuild != null && !_carBuild.Finished) _carBuild.Cancel();
        }

        private void OnCar(int gen, AcCarRig rig)
        {
            if (rig == null) return;
            if (gen != _carGeneration || this == null) { try { rig.Dispose(); } catch (Exception) { } return; }
            _car = rig;
            try
            {
                if (rig.Root != null)
                {
                    rig.Root.transform.SetParent(transform, false);
                    rig.Root.transform.localPosition = CarSpot;
                    rig.Root.transform.localRotation = Quaternion.Euler(0f, CarYaw, 0f);
                }

                AcLog.Info("Front: showroom car " + (rig.Car != null ? rig.Car.Id : "?") + "/" + rig.SkinId + " on the floor.");
            }
            catch (Exception e) { AcLog.Warn("Front: placing the showroom car failed: " + e.Message); }
        }

        private static IEnumerator Guarded(IEnumerator body, string what)
        {
            while (true)
            {
                bool more;
                try { more = body.MoveNext(); }
                catch (Exception e) { AcLog.Warn("Front: " + what + " failed: " + e.GetType().Name + ": " + e.Message); yield break; }
                if (!more) yield break;
                yield return body.Current;
            }
        }

        private void DisposeCar()
        {
            if (_car == null) return;
            try { _car.Dispose(); } catch (Exception e) { AcLog.Warn("Front: removing the showroom car failed: " + e.Message); }
            _car = null;
        }

        // ------------------------------------------------------------------ the panel

        /// <summary>The hub's menu canvas (1600 x 900 units, 2.56 m wide) with its placer; AcFront builds the menu into it.</summary>
        public Canvas BuildPanel(float width, float height)
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = AcUi.MakeCanvas("Ac Hub Panel", transform, width, height, PanelMetresPerUnit);
            _panel.transform.localPosition = new Vector3(0f, 1.6f, 2.4f);
            _panel.transform.localRotation = Quaternion.identity;
            _placer = _panel.gameObject.AddComponent<AcPanelPlacer>();
            _placer.HeightMetres = height * PanelMetresPerUnit;
            _placer.What = "hub menu";
            return _panel;
        }

        public void SetPanelShown(bool shown)
        {
            if (_panel != null && _panel.gameObject.activeSelf != shown) _panel.gameObject.SetActive(shown);
        }

        /// <summary>The hub on / off (a track loads / runs: off).</summary>
        public void SetShown(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            gameObject.SetActive(shown);
            AcLog.Info("Hub: " + (shown ? "shown" : "hidden") + ".");
        }

        /// <summary>No tablet on a track: the menu panel leaves the (hidden) hub and stands in front of the player there (AcPanelPlacer TrackMode).</summary>
        public void DetachPanel(Transform parent)
        {
            if (_panel == null || parent == null) return;
            if (!_panelDetached) _panel.transform.SetParent(parent, true);
            _panelDetached = true;
            if (_placer != null) { _placer.TrackMode = true; _placer.Replace(); }
        }

        /// <summary>The menu panel back under the hub (the Hub phase, or a track loading).</summary>
        public void AttachPanel()
        {
            if (_panel == null || !_panelDetached) return;
            _panelDetached = false;
            _panel.transform.SetParent(transform, true);
            if (_placer != null) { _placer.TrackMode = false; _placer.Replace(); }
        }

        public bool PanelOnTrack { get { return _panelDetached; } }

        /// <summary>Removes the showroom car now (a track loads: no second car model in memory while driving).</summary>
        public void ClearCar()
        {
            _carKey = null;
            _carGeneration++;
            DisposeCar();
            CancelCarBuild();
        }

        private void LateUpdate()
        {
            if (_panel != null && _panel.gameObject.activeInHierarchy)
            {
                Camera head = AcPlayer.Head();
                if (_panel.worldCamera != head) _panel.worldCamera = head;
            }
        }

        /// <summary>True when feet are outside the room or far below / above it (the keep rule).</summary>
        public static bool IsAway(Vector3 feet)
        {
            Vector3 d = feet - AcSpace.RoomOrigin;
            return Mathf.Abs(d.x) > KeepHalfSize || Mathf.Abs(d.z) > KeepHalfSize || d.y < -3f || d.y > 25f;
        }
    }
}
