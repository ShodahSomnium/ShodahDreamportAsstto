using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>How a car model is built.</summary>
    public sealed class AcCarModelOptions
    {
        /// <summary>LOD to build (0 = lods.ini LOD_0, the full model; remote / AI cars far away may use 1..).</summary>
        public int Lod = 0;
        /// <summary>Build the high-res cockpit (COCKPIT_HR) - the local driver / passenger and close cars. False = COCKPIT_LR only.</summary>
        public bool Interior = true;
        /// <summary>Build the AC driver model (content/driver, driver3d.ini) - for cars driven by AI or seen from outside without an avatar. // ours option</summary>
        public bool Driver = false;
        /// <summary>Build collider.kn5 hull colliders on <see cref="AcLayers.Car"/> (model space; sanity-checked against the LOD_0 bounds, C1).</summary>
        public bool Colliders = true;
        /// <summary>Keep the steering-wheel mesh readable (S measures the rim radius for the VR grab).</summary>
        public bool ReadableSteer = true;

        // ---- added by C
        /// <summary>Highest LOD to build too (-1 = only <see cref="Lod"/>). With more than one LOD the model switches by the viewer's distance
        /// using lods.ini IN/OUT (metres) - remote / AI cars.</summary>
        public int MaxLod = -1;
        /// <summary>Bind analog needles, LEDs and display emissives (needs the interior).</summary>
        public bool Instruments = true;
        /// <summary>Draw digital_instruments.ini text items with the bitmap fonts.</summary>
        public bool Digital = true;
        /// <summary>Renderer layer (colliders always go on <see cref="AcLayers.Car"/>).</summary>
        public int Layer = AcLayers.World;
        /// <summary>Main-thread budget per frame for the kn5 upload (ms). // ours</summary>
        public float SliceMs = 6f;
        /// <summary>Iteration 2: load the damage paint textures (txDamage / txDamageMask, the install's DAMAGE_GLASS crack map). Null = R's
        /// AcCarDamageGate.TexturesWanted at build start (DAMAGE &gt; 0 and cars.visualdamage on); the showroom never takes damage.</summary>
        public bool? DamageTextures;

        /// <summary>The defaults per use: Local = LOD_0 + interior + hull; Remote = LOD_0..3 by distance, no interior / digital, hull;
        /// Ai = Remote + the AC driver; Showroom = LOD_0 + interior, no hull. // ours choices</summary>
        public static AcCarModelOptions ForKind(AcCarKind kind)
        {
            switch (kind)
            {
                case AcCarKind.Remote: return new AcCarModelOptions { Lod = 0, MaxLod = 3, Interior = false, Instruments = false, Digital = false, ReadableSteer = false };
                case AcCarKind.Ai: return new AcCarModelOptions { Lod = 0, MaxLod = 3, Interior = false, Instruments = false, Digital = false, ReadableSteer = false, Driver = true };
                case AcCarKind.Showroom: return new AcCarModelOptions { Lod = 0, Interior = true, Colliders = false, ReadableSteer = false, DamageTextures = false };
                default: return new AcCarModelOptions();
            }
        }
    }

    /// <summary>One wheel's animated parts (any may be null when the car lacks the node).</summary>
    public struct AcWheelParts
    {
        /// <summary>WHEEL_xx (spins + steers; carries TYRE_xx, RIM_xx, RIM_BLUR_xx).</summary>
        public Transform Wheel;
        /// <summary>SUSP_xx (steers + travels, no spin; calipers).</summary>
        public Transform Susp;
        /// <summary>DISC_xx.</summary>
        public Transform Disc;
        public Transform Rim, RimBlur;
        /// <summary>tyres.ini RADIUS (m).</summary>
        public float Radius;
        /// <summary>Rest local position of <see cref="Wheel"/> (travel is applied relative to it).</summary>
        public Vector3 RestLocal;
    }

    /// <summary>
    /// [C, Car model] One built car: kn5 LOD + skin overrides, named animated parts, lights, instruments, door / wiper / shift ksanim
    /// players, collider hull. ONE API for the local, remote, AI and showroom cars. Model space = AC model space mirrored (Unity x = -AC x,
    /// origin on the ground under the car centre - ac_car_formats.md C1); P places <see cref="Root"/> under the Rigidbody at
    /// (-GRAPHICS_OFFSET.x, GRAPHICS_OFFSET.y, GRAPHICS_OFFSET.z) with Euler(GRAPHICS_PITCH_ROTATION, 0, 0).
    /// Build: a worker reads data.acd / knh / ksanims / kn5 / collider.kn5 (<see cref="AcCarJob"/>), W's AcKn5Builder uploads time-sliced,
    /// then the parts are bound by name (<see cref="AcCarPartSet"/>). Per frame: the Set* calls write transforms / emissives only on change
    /// and allocate nothing; an <see cref="AcCarModelDriver"/> on the root runs the time-based bits (cockpit HR/LR + LOD by distance, rim
    /// blur, wipers, shift paddles, pop-up lights, LED blink).
    /// </summary>
    public sealed class AcCarModel : IAcCarDamageVisual
    {
        public AcCarEntry Car { get; private set; }
        public string SkinId { get; private set; }
        /// <summary>The model root (model space origin).</summary>
        public Transform Root { get; private set; }
        /// <summary>0 LF, 1 RF, 2 LR, 3 RR. Like <see cref="SteeringWheel"/> and <see cref="DoorL"/>/<see cref="DoorR"/> these are the PRIMARY
        /// (first built, LOD_0) set's transforms: on a remote / AI car that shows LOD_1..3 they are hidden and not posed - drive the car through
        /// the Set* calls (which pose the shown LOD) and read poses only while <see cref="ActiveLod"/> == <see cref="Lod"/>.</summary>
        public readonly AcWheelParts[] Wheels = new AcWheelParts[4];
        /// <summary>STEER_HR (or STEER_LR when the cockpit is low-res). Rotation: restLocal * Euler(0, 0, -wheelDeg) (C3).</summary>
        public Transform SteeringWheel { get; private set; }
        /// <summary>Rest local rotation of <see cref="SteeringWheel"/>.</summary>
        public Quaternion SteeringRest { get; private set; } = Quaternion.identity;
        /// <summary>Driver eye in MODEL space (Unity axes): car.ini DRIVEREYES mirrored, fallback knh RIG_Head + 9 cm forward.</summary>
        public Vector3 DriverEye { get; private set; }
        /// <summary>Driver hip point (knh RIG_Hips) in model space - the seat point.</summary>
        public Vector3 DriverHips { get; private set; }
        /// <summary>knh RIG_HAND_L / RIG_HAND_R in model space (hands-on-wheel targets at wheel angle 0).</summary>
        public Vector3 HandL { get; private set; }
        public Vector3 HandR { get; private set; }
        /// <summary>Passenger seat in model space (driver hips mirrored across the centre line; null-equivalent: <see cref="HasPassengerSeat"/> false).</summary>
        public Vector3 PassengerHips { get; private set; }
        public bool HasPassengerSeat { get; private set; }
        /// <summary>DOOR_L / DOOR_R (null when the car has no opening door).</summary>
        public Transform DoorL { get; private set; }
        public Transform DoorR { get; private set; }
        /// <summary>The hull colliders (collider.kn5) on <see cref="AcLayers.Car"/>; P adds them to the Rigidbody's compound body.</summary>
        public Collider[] Hull { get; private set; } = new Collider[0];
        /// <summary>Bounds of the LOD_0 renderers in model space.</summary>
        public Bounds Bounds { get; private set; }
        /// <summary>The underlying built kn5 (W's builder) for anything not exposed here.</summary>
        public AcKn5Model Kn5 { get; private set; }

        // ---------------- added by C (additive members)
        /// <summary>The options it was built with.</summary>
        public AcCarModelOptions Options { get; private set; }
        /// <summary>The visual data (lods, lights, instruments, driver3d, knh, car.ini GRAPHICS_OFFSET / PITCH / DRIVEREYES, tyre radii).</summary>
        public AcCarVisual Visual { get; private set; }
        /// <summary>The LOD of the primary (first built) set, and the one shown now.</summary>
        public int Lod { get; private set; }
        public int ActiveLod { get { return _active != null ? _active.Lod : -1; } }
        /// <summary>The first built part set (LOD_0 for every kind) and the one shown now (read-only views: lab, tools).</summary>
        public AcCarPartSet PrimarySet { get { return _primary; } }
        public AcCarPartSet ActiveSet { get { return _active; } }
        /// <summary>Outer rim radius of the steering wheel (m, measured from the mesh around STEER_HR's local Z; 0 when unknown).</summary>
        public float SteeringRimRadius { get; private set; }
        /// <summary>Steering wheel hub (model space) and column axis (model space, STEER_HR local +Z: forward-down, away from the driver).</summary>
        public Vector3 SteeringCenter { get; private set; }
        public Vector3 SteeringAxis { get; private set; } = Vector3.forward;
        /// <summary>The AC driver model when built (<see cref="AcCarModelOptions.Driver"/>).</summary>
        public AcCarDriverModel DriverModel { get; private set; }
        /// <summary>The car has an opening door on that side (DOOR_x node + car_DOOR_x.ksanim).</summary>
        public bool HasDoor(AcSide side) { var s = _primary; return s != null && (side == AcSide.Left ? s.DoorAnimL != null : s.DoorAnimR != null); }
        /// <summary>Outside door-handle point in model space (rear, outer, mid-height of the door's meshes // ours placement) - S's grab /
        /// "get in" target. Falls back to the side of the car beside the driver when there is no door.</summary>
        public Vector3 DoorHandle(AcSide side) { return side == AcSide.Left ? _handleL : _handleR; }
        /// <summary>Current door opening 0..1.</summary>
        public float DoorOpen(AcSide side) { return side == AcSide.Left ? _doorT[0] : _doorT[1]; }
        /// <summary>Build time (worker + upload + bind), ms.</summary>
        public long BuildMs { get; private set; }
        /// <summary>Cars alive (built and not disposed) - the lab's leak check.</summary>
        public static int LiveCount { get; private set; }
        /// <summary>The viewer for the cockpit HR/LR and LOD distance checks (lab camera; null = the player's camera, AcPlayer.Head).</summary>
        public static Transform ViewerOverride;
        public bool Disposed { get; private set; }

        readonly List<AcCarPartSet> _sets = new List<AcCarPartSet>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        AcCarPartSet _primary, _active;
        Vector3 _handleL, _handleR;

        // state (re-applied on a LOD switch)
        float _steerDeg;
        readonly float[] _spin = new float[4], _travel = new float[4], _wheelSteer = new float[4], _wheelKmh = new float[4];
        int _wheelKmhFrame = -10;
        readonly float[] _doorT = new float[2];
        AcLightState _lights;
        bool _lightsSet;
        AcInstruments _ins;
        AcCarDisplayExtras _extras;
        bool _viewerInside;
        bool _wipers;
        float _wiperT;
        AcKsanimPlayer _shiftAnim;
        float _shiftT, _shiftDur;
        int _lastGear = int.MinValue;
        bool _tickFailed;
        bool _interior, _interiorBusy;
        string _skinDir;
        bool _wasLocal, _pressureDirty = true, _subscribed;
        Action<string> _onSetting;

        /// <summary>The high-res cockpit (COCKPIT_HR, instruments) is built: from the options, or later by <see cref="EnsureInterior"/>.</summary>
        public bool HasInterior { get { return _interior; } }

        const float WiperPeriodS = 1.4f; // ours: one car_wiper.ksanim cycle (out and back); AC's wiper speed is not in the car data
        const float SwitchHysteresisM = 0.5f; // ours: no LOD / cockpit flicker when the viewer stands on a lods.ini boundary

        // ================================================================ build

        /// <summary>Builds a car (reads kn5 + data.acd on a worker, uploads time-sliced); run with StartCoroutine. <paramref name="done"/> gets
        /// the model, or null on failure (logged).</summary>
        public static IEnumerator BuildAsync(AcCarEntry car, string skinId, AcCarModelOptions options, Transform parent, Action<AcCarModel> done)
        {
            var sw = Stopwatch.StartNew();
            if (options == null) options = new AcCarModelOptions();
            AcCarModel m = null;
            AcCarJob job = null;
            string fail = null;
            try
            {
                if (car == null) fail = "no car";
                else if (!car.Owned) fail = car.Id + " is not installed (DLC stub)";
                else
                {
                    m = Begin(car, skinId, options, parent);
                    job = new AcCarJob(car, options, DriverDir());
                    job.SkinDir = SkinDir(car, ref skinId);
                    m.SkinId = skinId;
                    m._skinDir = job.SkinDir;
                    job.Start();
                }
            }
            catch (Exception e) { fail = e.Message; }
            if (fail != null) { Fail(car, skinId, fail, m); if (done != null) done(null); yield break; }

            while (!job.Done) yield return null;
            if (m.Disposed || m.Root == null) { m.Dispose(); if (done != null) done(null); yield break; }   // (Dispose: idempotent - its sets, scopes, LiveCount)
            if (job.Error != null) { Fail(car, skinId, job.Error, m); if (done != null) done(null); yield break; }

            // upload every LOD with W's builder (time-sliced). A Dispose() meanwhile destroys Root; the builder notices its root is gone
            // and disposes its half-built model itself, and a model that finishes anyway is disposed below - so the loop keeps stepping it.
            string skinDir = job.SkinDir;
            for (int i = 0; i < job.Lods.Count; i++)
            {
                var lod = job.Lods[i];
                AcKn5Model built = null;
                IEnumerator e = null;
                try
                {
                    var bo = new AcKn5BuildOptions
                    {
                        Layer = options.Layer,
                        SkinDir = skinDir,
                        Readable = false,
                        SliceMs = options.SliceMs,
                        DamageSlots = m._damageSlots,
                    };
                    if (i == 0 && options.ReadableSteer) bo.ReadableFilter = UnderSteer;           // only the rim stays CPU-readable (S measures it)
                    if (i > 0 || !options.Interior) bo.Skip = IsCockpitHr;                         // remote / AI / far LODs: no high-res cockpit
                    if (job.TextureSource != null) bo.TextureSource = job.TextureSource; // LOD_1..3 carry no textures (86/86 cars); same scope key -> shared textures
                    e = AcKn5Builder.BuildAsync(lod.Kn5, m.Root, bo, null, r => built = r);
                }
                catch (Exception ex) { AcLog.Warn("Car: " + car.Id + " LOD_" + lod.Index + " build start failed: " + ex.Message); }
                while (e != null)
                {
                    object cur;
                    try { if (!e.MoveNext()) break; cur = e.Current; }
                    catch (Exception ex) { AcLog.Warn("Car: " + car.Id + " LOD_" + lod.Index + " build failed: " + ex.Message); built = null; break; }
                    yield return cur;
                }
                if (m.Disposed || m.Root == null) { if (built != null) built.Dispose(); m.Dispose(); if (done != null) done(null); yield break; }   // the LODs built so far go too
                if (built == null || built.Root == null)
                {
                    if (i == 0) { Fail(car, skinId, "kn5 build of LOD_" + lod.Index + " failed", m); if (done != null) done(null); yield break; }
                    continue;
                }
                built.Root.name = "LOD_" + lod.Index;
                m._sets.Add(new AcCarPartSet(lod.Index, built, m.Root, lod.In, lod.Out));
            }

            try { m.Finish(job); }
            catch (Exception ex) { Fail(car, skinId, "bind: " + ex, m); if (done != null) done(null); yield break; }

            if (options.Driver && job.DriverKn5 != null)
            {
                AcCarDriverModel dm = null;
                var de = AcCarDriverModel.Build(job, m.Root, options, r => dm = r);
                while (true)
                {
                    object cur;
                    try { if (!de.MoveNext()) break; cur = de.Current; }
                    catch (Exception ex) { AcLog.Warn("Car: " + car.Id + " driver failed: " + ex.Message); break; }
                    yield return cur;
                }
                if (m.Disposed || m.Root == null) { if (dm != null) dm.Dispose(); m.Dispose(); if (done != null) done(null); yield break; }
                m.DriverModel = dm;
                if (dm != null) dm.SetSteer(m._steerDeg);
            }

            m.BuildMs = sw.ElapsedMilliseconds;
            m.LogBuilt(job);
            // the uploaded meshes live on the GPU now: drop the managed vertex arrays (the node trees stay for W's NodeOf lookups)
            for (int i = 0; i < job.Lods.Count; i++) { try { job.Lods[i].Kn5.Release(); } catch (Exception) { } }
            if (job.DriverKn5 != null) { try { job.DriverKn5.Release(); } catch (Exception) { } }
            if (done != null) done(m);
        }

        /// <summary>Builds a car synchronously (lab / tools; stalls the frame). Uses the same steps as <see cref="BuildAsync"/>, run to the end.</summary>
        public static AcCarModel Build(AcCarEntry car, string skinId, AcCarModelOptions options, Transform parent)
        {
            AcCarModel result = null;
            var e = BuildAsync(car, skinId, options, parent, r => result = r);
            var stack = new Stack<IEnumerator>();
            stack.Push(e);
            int guard = 0;
            while (stack.Count > 0 && guard++ < 10000000)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                var nested = top.Current as IEnumerator;
                if (nested != null) stack.Push(nested);
                else if (top.Current == null) System.Threading.Thread.Sleep(0); // worker wait
            }
            return result;
        }

        static bool UnderSteer(Kn5Node n)
        {
            for (var p = n; p != null; p = p.Parent) if (string.Equals(p.Name, "STEER_HR", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, "STEER_LR", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsCockpitHr(Kn5Node n) { return n != null && string.Equals(n.Name, "COCKPIT_HR", StringComparison.Ordinal); }

        static AcCarModel Begin(AcCarEntry car, string skinId, AcCarModelOptions options, Transform parent)
        {
            var go = new GameObject("Ac Car " + car.Id);
            go.layer = options.Layer;
            go.transform.SetParent(parent, false);
            LiveCount++;
            var m = new AcCarModel { Car = car, SkinId = skinId ?? "", Root = go.transform, Options = options };
            // iteration 2: one damage-texture decision per car (every LOD and a later interior agree): the option, else R's gate now
            bool slots = false;
            if (options.DamageTextures.HasValue) slots = options.DamageTextures.Value;
            // integration 2026-11-02: VISUAL DAMAGE switched off after R's last Refresh skips the textures too (R12)
            else { try { slots = AcCarDamageGate.TexturesWanted && AcCarDamageGate.VisualOn; } catch (Exception e) { AcLog.WarnOnce("Car: AcCarDamageGate failed: " + e.Message); } }
            m._damageSlots = slots;
            return m;
        }

        static void Fail(AcCarEntry car, string skinId, string why, AcCarModel m)
        {
            AcLog.Warn("Car: build of " + (car != null ? car.Id : "?") + "/" + (skinId ?? "") + " failed: " + why);
            if (m != null) m.Dispose();
        }

        static string DriverDir()
        {
            try
            {
                var g = AcGame.Current;
                if (g != null && g.Install != null) return g.Install.DriverDir;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The skin folder: the asked skin, else the first one (AC's default order). Updates <paramref name="skinId"/>.</summary>
        static string SkinDir(AcCarEntry car, ref string skinId)
        {
            if (car == null || car.Skins == null || car.Skins.Count == 0) return null;
            var s = !string.IsNullOrEmpty(skinId) ? car.Skin(skinId) : null;
            if (s == null)
            {
                if (!string.IsNullOrEmpty(skinId)) AcLog.InfoOnce("Car: " + car.Id + " has no skin '" + skinId + "', using " + car.Skins[0].Id);
                s = car.Skins[0];
            }
            skinId = s.Id;
            return s.Dir;
        }

        Kn5File _texSource;
        int _flatDamage;

        // iteration 2: damage visuals (DAMAGE_DESIGN 2.1-2.4)
        bool _damageSlots, _damageDirty;
        AcCarDamageVisual _dmg;
        AcCarSmoke _smoke;
        float _smokeRate;
        bool _smokePointKnown;
        Vector3 _smokePoint;

        void Finish(AcCarJob job)
        {
            Visual = job.Visual;
            _primary = _sets[0];
            Lod = _primary.Lod;
            Kn5 = _primary.Kn5;
            _texSource = job.TextureSource;
            _interior = Options.Interior;
            Func<string, Texture2D> tex = KnTexture;
            for (int i = 0; i < _sets.Count; i++)
            {
                var s = _sets[i];
                bool first = i == 0;
                s.Bind(Visual, job, first && Options.Interior, first && Options.Interior && Options.Instruments, Options.Digital, tex);
                if (!_damageSlots) _flatDamage += AcCarShading.FlattenDamageNormals(s.Kn5);   // iteration 2: only when built without damage textures (DAMAGE 0 = iteration 1)
            }
            _dmg = new AcCarDamageVisual(Visual.Damage, Car.Id);
            for (int i = 0; i < 4; i++)
            {
                var w = _primary.Wheels[i];
                Wheels[i] = new AcWheelParts
                {
                    Wheel = w.Wheel, Susp = w.Susp, Disc = w.Disc, Rim = w.Rim, RimBlur = w.RimBlur,
                    Radius = Visual.Radius(i), RestLocal = w.WheelPos,
                };
            }
            var steer = Options.Interior ? (_primary.SteerHr ?? _primary.SteerLr) : (_primary.SteerLr ?? _primary.SteerHr);
            SteeringWheel = steer;
            if (steer != null)
            {
                SteeringRest = steer == _primary.SteerHr ? _primary.SteerHrRest : _primary.SteerLrRest;
                SteeringCenter = Root.InverseTransformPoint(steer.position);
                SteeringAxis = Root.InverseTransformDirection(steer.forward);
            }
            SteeringRimRadius = job.SteerRimRadius;
            DoorL = _primary.DoorL; DoorR = _primary.DoorR;
            Bounds = job.HasModelBounds ? job.ModelBounds : new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(1.9f, 1.2f, 4.4f)); // ours fallback size

            // seat points (model space, Unity axes)
            var v = Visual;
            Vector3 eye;
            if (v.HasDriverEyes) eye = AcCarMath.ToUnity(v.DriverEyes);
            else if (v.HasKnh && v.KnhHead.Y != 0f) eye = AcCarMath.ToUnity(v.KnhHead) + new Vector3(0f, 0f, 0.09f); // C1: eyes ~9 cm ahead of RIG_Head
            else eye = SteeringCenter + new Vector3(0f, 0.25f, -0.5f); // ours
            DriverEye = eye;
            bool knhHips = v.HasKnh && v.KnhHips.Y != 0f;
            DriverHips = knhHips ? AcCarMath.ToUnity(v.KnhHips) : eye + new Vector3(0f, -0.55f, -0.05f); // ours fallback (2 cars have an empty knh)
            if (v.HasKnh && v.KnhHandL.Y != 0f && v.KnhHandR.Y != 0f) { HandL = AcCarMath.ToUnity(v.KnhHandL); HandR = AcCarMath.ToUnity(v.KnhHandR); }
            else if (steer != null)
            {
                float r = SteeringRimRadius > 0.05f ? SteeringRimRadius * 0.93f : 0.18f; // ours: grip on the rim tube
                HandL = Root.InverseTransformPoint(steer.TransformPoint(new Vector3(-r, 0f, 0f)));
                HandR = Root.InverseTransformPoint(steer.TransformPoint(new Vector3(r, 0f, 0f)));
            }
            else { HandL = eye + new Vector3(-0.2f, -0.25f, 0.45f); HandR = eye + new Vector3(0.2f, -0.25f, 0.45f); } // ours
            // ours: an off-centre driver means a 2-seat cockpit (single-seaters sit at x ~ 0) - except ui_car.json class "race": GT / touring
            // race cars sit off-centre in a cage with no passenger seat (harness seats: 78 offset drivers, 13 of them race class; this also
            // drops shelby_cobra_427sc and ks_audi_sport_quattro_rally, whose ui json says race although a second seat exists)
            bool raceClass = Car != null && Car.Class != null && Car.Class.Trim().Equals("race", StringComparison.OrdinalIgnoreCase);
            HasPassengerSeat = Mathf.Abs(DriverHips.x) > 0.2f && !raceClass;
            PassengerHips = new Vector3(-DriverHips.x, DriverHips.y, DriverHips.z);
            _extras.PressureLF = _extras.PressureRF = v.PressureFront;
            _extras.PressureLR = _extras.PressureRR = v.PressureRear;

            ComputeHandles();

            if (Options.Colliders)
            {
                float rejected;
                Hull = AcCarHull.Build(job, Root, _owned, out rejected);
                if (rejected > 0f) AcLog.Info("Car: " + Car.Id + " hull rejected (off by " + rejected.ToString("0.00") + " m), using the LOD_0 box");
            }

            // show the primary set, hide the others until the distance check picks one
            _active = _primary;
            for (int i = 1; i < _sets.Count; i++) if (_sets[i].Root != null) _sets[i].Root.SetActive(false);
            _primary.SetCockpitHr(Options.Interior);
            ApplyAll(_primary);

            var drv = Root.gameObject.AddComponent<AcCarModelDriver>();
            drv.Model = this;

            // the player's garage tyre pressures (F) apply to the local car: re-read when a setup.* setting changes
            try { _onSetting = OnSetting; AcSettings.Changed += _onSetting; _subscribed = true; }
            catch (Exception e) { AcLog.WarnOnce("Car: AcSettings.Changed subscribe failed: " + e.Message); }
        }

        void OnSetting(string key)
        {
            if (key != null && key.StartsWith("setup.", StringComparison.Ordinal)) _pressureDirty = true;
            if (key == AcCarDamageGate.VisualSettingKey) _damageDirty = true;
        }

        /// <summary>A digital-display texture by file name (TEXTURE_BASE / TEXTURE_TOP): the car's texture/ folder (where all 26 of the install
        /// live - harness digitems), else the LOD_0 kn5, else the car folder. PNG or DDS through W's preparer / uploader (image top at v = 0).
        /// The caller owns the texture.</summary>
        Texture2D KnTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                byte[] bytes = null;
                if (Car != null && Car.Dir != null)
                {
                    string p = System.IO.Path.Combine(System.IO.Path.Combine(Car.Dir, "texture"), name);
                    if (System.IO.File.Exists(p)) bytes = System.IO.File.ReadAllBytes(p);
                }
                if (bytes == null)
                {
                    var src = _texSource;
                    var t = src != null ? src.Texture(name) : null;
                    if (t != null) bytes = src.ReadTextureBytes(t);
                }
                if (bytes == null && Car != null && Car.Dir != null)
                {
                    string p = System.IO.Path.Combine(Car.Dir, name);
                    if (System.IO.File.Exists(p)) bytes = System.IO.File.ReadAllBytes(p);
                }
                if (bytes == null) return null;
                var prep = AcTexPrepare.Prepare(name, bytes, false);
                var tex = AcTextures.Upload(prep, Car != null ? Car.Id + " display" : "display");
                if (tex != null) tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception e) { AcLog.InfoOnce("Car: texture " + name + " failed: " + e.Message); return null; }
        }

        void ComputeHandles()
        {
            _handleL = Handle(_primary.DoorL, -1f);
            _handleR = Handle(_primary.DoorR, 1f);
        }

        /// <summary>Door handle: rear (-z) end, outer side, 55 % up the door's mesh bounds in model space. // ours placement</summary>
        Vector3 Handle(Transform door, float side)
        {
            var fallback = new Vector3(side * (Bounds.extents.x + 0.05f), DriverHips.y + 0.35f, DriverHips.z + 0.25f); // ours
            if (door == null) return fallback;
            bool any = false;
            Vector3 min = Vector3.one * 1e9f, max = -min;
            var mfs = door.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                var mesh = mfs[i].sharedMesh;
                if (mesh == null) continue;
                var b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) != 0 ? 1 : -1, (c & 2) != 0 ? 1 : -1, (c & 4) != 0 ? 1 : -1));
                    var p = Root.InverseTransformPoint(mfs[i].transform.TransformPoint(corner));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    any = true;
                }
            }
            if (!any) return Root.InverseTransformPoint(door.position) + new Vector3(side * 0.05f, 0f, -0.9f); // ours: hinge -> handle
            float x = side < 0f ? min.x : max.x;
            float y = min.y + (max.y - min.y) * 0.55f;
            float z = min.z + Mathf.Min(0.15f, (max.z - min.z) * 0.15f);
            return new Vector3(x, y, z);
        }

        void LogBuilt(AcCarJob job)
        {
            int dummies, meshes, skinned; long verts, idx;
            Kn5File k = job.Lods[0].Kn5;
            k.Totals(out dummies, out meshes, out skinned, out verts, out idx);
            long texBytes = 0;
            try
            {
                var used = Kn5Scan.UsedTextures(k, null);
                var src = job.TextureSource ?? k;
                foreach (var name in used) { var t = src.Texture(name); if (t != null) texBytes += t.Size; }
            }
            catch (Exception) { }
            long gpuBytes = Kn5 != null && Kn5.Scope != null ? Kn5.Scope.TextureBytes : 0;
            var p = _primary;
            AcLog.Info("Car: built " + Car.Id + "/" + SkinId + " LOD " + Lod + (_sets.Count > 1 ? "-" + _sets[_sets.Count - 1].Lod : "") + " in " + BuildMs + " ms (worker " + job.Ms
                + " ms; " + meshes + " meshes, " + (idx / 3) + " tris, textures " + (gpuBytes / 1048576f).ToString("0.0") + " MB uploaded (" + (texBytes / 1048576f).ToString("0.0") + " MB in the kn5), hull " + Hull.Length
                + " colliders, doors " + (p.DoorAnimL != null ? "L" : "-") + "/" + (p.DoorAnimR != null ? "R" : "-") + ", steer " + (SteeringWheel != null ? "yes" : "no")
                + (SteeringRimRadius > 0f ? " r " + SteeringRimRadius.ToString("0.000") : "") + ", lights " + (p.Lights != null ? p.Lights.Count : 0)
                + ", needles " + (p.Instruments != null ? p.Instruments.NeedleCount : 0) + ", leds " + (p.Instruments != null ? p.Instruments.LedCount : 0)
                + ", displays " + (p.Instruments != null ? p.Instruments.TextCount : 0) + ", blur " + p.BlurCount + ", driver " + (DriverModel != null ? "yes (posed " + DriverModel.Posed + ", skin slots " + DriverModel.SkinSlots + ")" : "no") + ", interior " + (_interior ? "yes" : "no") + ", passenger seat " + (HasPassengerSeat ? "yes" : "no")
                + ", W nodes " + (Kn5 != null ? Kn5.Nodes.Count : 0) + ", damage normals flat " + _flatDamage + ")");
            LogDamage();
            if (p.Missing.Count > 0) AcLog.InfoOnce("Car: " + Car.Id + " missing node " + string.Join(", ", p.Missing.ToArray()));
            for (int i = 0; i < job.Notes.Count; i++) AcLog.InfoOnce("Car: " + Car.Id + " " + job.Notes[i]);
            for (int i = 0; i < Visual.Problems.Count; i++) AcLog.InfoOnce("Car: " + Car.Id + " data: " + Visual.Problems[i]);
        }

        // ================================================================ damage (iteration 2, DAMAGE_DESIGN 2 / 9 C)

        /// <summary>The car's damage zones in km/h as P's ZoneByte values (whole km/h 0..255, identical on the owner and every remote client):
        /// paint scratches / dents (when built with damage textures), glass zones (g &gt;= GLASS_THRESHOLD), bent + oscillating
        /// [VISUAL_OBJECT_n] panels. Idempotent: a repeat of the same values does nothing; all zero = pristine (clones kept, vectors 0). Called
        /// by P on a damage Version change only (local, AI and remote rigs). The local setting cars.visualdamage off draws nothing.</summary>
        public void SetDamage(float frontKmh, float rearKmh, float leftKmh, float rightKmh, float centerKmh)
        {
            if (_dmg == null || Disposed) return;
            if (!_dmg.Set(frontKmh, rearKmh, leftKmh, rightKmh, centerKmh, DamageVisualsOn)) return;
            ApplyDamage();
        }

        /// <summary>= SetDamage(0, 0, 0, 0, 0).</summary>
        public void ClearDamage() { SetDamage(0f, 0f, 0f, 0f, 0f); }

        /// <summary>Engine smoke 0 = off .. 1 (engine_smoke.ini particles at the bonnet / engine end). Created on the first call above 0.</summary>
        public void SetEngineSmoke(float rate01)
        {
            if (Disposed || Root == null) return;
            rate01 = Mathf.Clamp01(rate01);
            if (rate01 == _smokeRate && (_smoke != null || rate01 <= 0f)) return;
            _smokeRate = rate01;
            if (rate01 <= 0f && _smoke == null) return;
            if (_smoke == null) _smoke = new AcCarSmoke();
            try { _smoke.Set(rate01, Root, SmokePoint()); }
            catch (Exception e) { AcLog.WarnOnce("Car: engine smoke failed: " + e.Message); }
        }

        /// <summary>False when the local setting cars.visualdamage is off (this client then draws no damage on any car; zones still count).
        /// Paint dents / scratches additionally need <see cref="DamageTexturesBuilt"/>; glass and panels work either way (DAMAGE_DESIGN 1.4: a
        /// 0 -> &gt;0 change mid-session shows them at once).</summary>
        public bool DamageVisualsOn
        {
            get { try { return AcCarDamageGate.VisualOn; } catch (Exception) { return true; } }
        }

        /// <summary>The car was built with the damage paint textures (AcCarDamageGate on at its build, or the option).</summary>
        public bool DamageTexturesBuilt { get { return _damageSlots; } }
        /// <summary>Read-only view of the damage visuals (lab / tools).</summary>
        public AcCarDamageVisual DamageVisual { get { return _dmg; } }
        public AcCarSmoke Smoke { get { return _smoke; } }

        void ApplyDamage()
        {
            try { _dmg.Apply(_sets, _active); }
            catch (Exception e) { AcLog.WarnOnce("Car: " + (Car != null ? Car.Id : "?") + " damage visuals failed: " + e.Message); }
        }

        /// <summary>Engine smoke emitter (model space): above the MOTORHOOD meshes when the car has that node (front or rear engine alike),
        /// else the front of the LOD_0 bounds. // ours placement</summary>
        Vector3 SmokePoint()
        {
            if (_smokePointKnown) return _smokePoint;
            _smokePointKnown = true;
            var b = Bounds;
            _smokePoint = new Vector3(b.center.x, b.center.y + b.extents.y * 0.4f, b.center.z + b.extents.z * 0.7f); // ours fallback
            var hood = _primary != null ? _primary.Find("MOTORHOOD") : null;
            if (hood == null) return _smokePoint;
            var rs = hood.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            var hb = new Bounds();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                var wb = rs[i].bounds;
                var c = Root.InverseTransformPoint(wb.center);
                if (!any) { hb = new Bounds(c, Vector3.zero); any = true; }
                hb.Encapsulate(Root.InverseTransformPoint(wb.min));
                hb.Encapsulate(Root.InverseTransformPoint(wb.max));
            }
            if (any) _smokePoint = new Vector3(hb.center.x, hb.max.y, hb.center.z);
            return _smokePoint;
        }

        void LogDamage()
        {
            var d = Visual != null ? Visual.Damage : null;
            if (d == null) return;
            var s = _primary != null ? _primary.Damage : null;
            int g0 = 0, g1 = 0, g2 = 0, g3 = 0, g4 = 0, stray = 0, activeHidden = 0, paint = 0, found = 0;
            for (int i = 0; i < _sets.Count; i++)
            {
                var ds = _sets[i].Damage;
                if (ds == null) continue;
                stray += ds.StrayGlass; activeHidden += ds.ActiveGlassHidden;
                if (ds.LooseGlassNames.Count > 0) AcLog.InfoOnce("Car: " + Car.Id + " LOD_" + _sets[i].Lod + " node(s) " + string.Join(", ", ds.LooseGlassNames.ToArray()) + " named DAMAGE_GLASS_ without a zone - left alone");
                for (int k = 0; k < ds.MissingPanels.Count; k++) AcLog.InfoOnce("Car: " + Car.Id + " damage panel " + ds.MissingPanels[k] + " not found (LOD_" + _sets[i].Lod + ")");
            }
            if (s != null)
            {
                g0 = s.Glass[0].Count; g1 = s.Glass[1].Count; g2 = s.Glass[2].Count; g3 = s.Glass[3].Count; g4 = s.Glass[4].Count;
                paint = s.PaintRenderers.Count;
                var seen = new HashSet<int>();
                for (int i = 0; i < s.Panels.Count; i++) seen.Add(s.Panels[i].Def);
                found = seen.Count;
            }
            AcLog.Info("Car: " + Car.Id + " damage visuals: damage.ini " + (d.Present ? "yes" : "no") + ", textures " + (_damageSlots ? "ON" : "off (normals flat " + _flatDamage + ")")
                + ", glass zones F/R/L/R/C " + g0 + "/" + g1 + "/" + g2 + "/" + g3 + "/" + g4 + " nodes (LOD_" + Lod + "), broken-glass meshes hidden: " + activeHidden + " active zone node(s) + " + stray
                + " stray mesh(es) outside a zone, panels " + found + " of " + d.Panels.Count + " found (LOD_" + Lod + "), paint renderers " + paint
                + ", oscillations " + (d.Oscillations ? "on" : "off") + ", initial level " + d.InitialLevel + ", scratches " + d.ScratchMin + "-" + d.ScratchMax + " km/h");
        }

        // ================================================================ per-frame API (no allocation)

        /// <summary>Opens a door: 0 closed .. 1 open (plays car_DOOR_L/R.ksanim at that time).</summary>
        public void SetDoor(AcSide side, float t)
        {
            t = Mathf.Clamp01(t);
            _doorT[side == AcSide.Left ? 0 : 1] = t;
            if (_active != null) _active.SetDoor(side, t);
        }

        /// <summary>Turns the steering wheel (degrees, right positive) and the driver's steer.ksanim if built.</summary>
        public void SetSteer(float wheelDeg)
        {
            _steerDeg = wheelDeg;
            if (_active != null) _active.SetSteer(wheelDeg);
            if (DriverModel != null) DriverModel.SetSteer(wheelDeg);
        }

        /// <summary>Poses wheel <paramref name="i"/> (0 LF, 1 RF, 2 LR, 3 RR): spin angle (deg, increasing when rolling forward), suspension travel
        /// (m from static, + = compressed / wheel up), road steer angle (deg, right positive).</summary>
        public void SetWheelState(int i, float spinDeg, float travelM, float steerDeg)
        {
            if (i < 0 || i > 3) return;
            _spin[i] = spinDeg; _travel[i] = travelM; _wheelSteer[i] = steerDeg;
            if (_active != null) _active.SetWheel(i, spinDeg, travelM, steerDeg);
        }

        /// <summary>Wheel surface speed (km/h) for the rim blur swap (blurred_objects.ini). Without it the car speed from
        /// <see cref="SetInstruments"/> is used for all four.</summary>
        public void SetWheelSpeed(int i, float kmh)
        {
            if (i < 0 || i > 3) return;
            _wheelKmh[i] = kmh;
            _wheelKmhFrame = Time.frameCount;
        }

        /// <summary>Switches light groups (lights.ini: headlights, brake, reverse...) and emissive materials.</summary>
        public void SetLights(AcLightState lights)
        {
            if (_lightsSet && lights == _lights) return;
            _lights = lights; _lightsSet = true;
            if (_active != null && _active.Lights != null) _active.Lights.Set(lights, false);
        }

        /// <summary>Drives analog needles and digital displays / LEDs (analog_instruments.ini, digital_instruments.ini); a gear change plays
        /// the paddle / lever clip (car_shift_up/dw or car_shift).</summary>
        public void SetInstruments(AcInstruments values)
        {
            _ins = values;
            if (_lastGear != int.MinValue && values.Gear != _lastGear)
            {
                bool up = values.Gear > _lastGear;
                StartShift(up);
                if (DriverModel != null) DriverModel.Shift(up);
            }
            _lastGear = values.Gear;
            // the needles / displays are written once per frame by Tick (LateUpdate), after every producer of this frame
        }

        /// <summary>Last / best lap for the dash displays (LAST_LAP / BEST_LAP items); 0 = none.</summary>
        public void SetLapTimes(int lastMs, int bestMs)
        {
            _extras.LastLapMs = lastMs; _extras.BestLapMs = bestMs;
        }

        /// <summary>Current / last / best lap (ms; 0 = none) for LAPTIME / CURRENT_LAP / LAST_LAP / BEST_LAP items. The LOCAL car fills these
        /// itself every frame from AcLocal (R); N calls it for remote cars (AcNetPlayer LapMs / LastMs / BestMs). No allocation.</summary>
        public void SetLapTimes(int lapMs, int lastMs, int bestMs)
        {
            _extras.LapMs = lapMs; _extras.LastLapMs = lastMs; _extras.BestLapMs = bestMs;
        }

        /// <summary>Live delta to the best lap (ms, negative = faster) for PERF items; the local car reads AcLocal.DeltaMs itself.</summary>
        public void SetDelta(int deltaMs) { _extras.DeltaMs = deltaMs; }

        /// <summary>TC / ABS levels for TC_LEVEL / ABS_LEVEL items (0 = off). The local car reads its AcAssists itself (P).</summary>
        public void SetAssistLevels(int tcLevel, int absLevel)
        {
            _extras.TcLevel = tcLevel; _extras.AbsLevel = absLevel; _extras.AssistsKnown = true;
        }

        /// <summary>Tyre pressures (psi) for PRESSURE items (default: tyres.ini PRESSURE_STATIC; the local car: the player's garage setup).</summary>
        public void SetTyrePressures(float lf, float rf, float lr, float rr)
        {
            _extras.PressureLF = lf; _extras.PressureRF = rf; _extras.PressureLR = lr; _extras.PressureRR = rr;
        }

        /// <summary>Wipers on / off (car_wiper.ksanim cycles while on, parks when off). NOTE: nothing calls it yet - AcCarInput has no wiper
        /// control (AC binds wipers to a key); the lab drives it directly.</summary>
        public void SetWipers(bool on) { _wipers = on; }

        /// <summary>Everything from one telemetry frame (P for local / AI cars, N for remote): steer, wheels (+ blur speed from AngularVel *
        /// tyre radius), lights, instruments. No allocation.</summary>
        public void ApplyTelemetry(ref AcTelemetry t)
        {
            SetSteer(t.SteerWheelDeg);
            for (int i = 0; i < 4; i++)
            {
                var w = t.Wheel(i);
                SetWheelState(i, w.SpinDeg, w.TravelM, w.SteerDeg);
                float r = Visual != null ? Visual.Radius(i) : 0.33f;
                SetWheelSpeed(i, Mathf.Abs(w.AngularVel) * r * 3.6f);
            }
            SetLights(t.Lights);
            SetInstruments(t.Instruments);
            _extras.LatG = t.LatG; _extras.LongG = t.LongG;
        }

        /// <summary>The seated viewer's view is inside the cockpit: forces COCKPIT_HR (when built - see <see cref="EnsureInterior"/>). The AC
        /// driver model hides whenever the LOCAL player sits in this car's driver seat (checked every frame from AcLocal.Seated / Seat /
        /// SeatCar, so it also comes back when they leave); a passenger keeps seeing the AI driver beside them, helmet included.</summary>
        public void SetViewer(bool insideCockpit)
        {
            _viewerInside = insideCockpit;
            if (_active != null && insideCockpit && _interior) _active.SetCockpitHr(true);
        }

        bool LocalDriverSeat()
        {
            var sc = AcLocal.SeatCar;
            return AcLocal.Seated && AcLocal.Seat == AcSeatKind.Driver && sc != null && ReferenceEquals(sc.Model, this);
        }

        bool IsLocalCar()
        {
            var c = AcLocal.Car;
            return c != null && ReferenceEquals(c.Model, this);
        }

        /// <summary>The local car's display extras from their owners: lap / last / best / delta (R's AcLocal), TC / ABS levels (P's AcAssists:
        /// 0 off, else level 1 // ours - the in-car level steps are not modelled), garage tyre pressures (F's SetupValue, on a setup change).</summary>
        void FillLocalExtras()
        {
            _extras.LapMs = AcLocal.LapMs; _extras.LastLapMs = AcLocal.LastMs; _extras.BestLapMs = AcLocal.BestMs;
            _extras.DeltaMs = AcLocal.DeltaMs;
            var rig = AcLocal.Car;
            var a = rig != null && rig.Physics != null ? rig.Physics.Assists : null;
            if (a != null) { _extras.TcLevel = a.Tc > 0 ? 1 : 0; _extras.AbsLevel = a.Abs > 0 ? 1 : 0; _extras.AssistsKnown = true; }
            if (_pressureDirty || !_wasLocal)
            {
                _pressureDirty = false;
                _extras.PressureLF = Setup("PRESSURE_LF", Visual.PressureFront);
                _extras.PressureRF = Setup("PRESSURE_RF", Visual.PressureFront);
                _extras.PressureLR = Setup("PRESSURE_LR", Visual.PressureRear);
                _extras.PressureRR = Setup("PRESSURE_RR", Visual.PressureRear);
            }
        }

        float Setup(string key, float fallback)
        {
            try
            {
                float v;
                AcFront.SetupValue(Car.Id, key, out v);
                return float.IsNaN(v) || v <= 0f ? fallback : v;
            }
            catch (Exception e) { AcLog.WarnOnce("Car: AcFront.SetupValue threw: " + e.Message); return fallback; }
        }

        void StartShift(bool up)
        {
            if (_active == null) return;
            var p = up ? (_active.ShiftUp ?? _active.Shift) : (_active.ShiftDown ?? _active.Shift);
            if (p == null) return;
            if (_shiftAnim != null && _shiftAnim != p) _shiftAnim.Sample(0f);
            _shiftAnim = p;
            _shiftT = 0f;
            var d = Visual != null ? Visual.Driver : null;
            float ms = d != null ? d.PositiveMs + d.StaticMs + d.NegativeMs : 0f;
            _shiftDur = ms > 20f ? ms * 0.001f : 0.3f; // driver3d.ini [SHIFT_ANIMATION] times [BELIEF: the paddle clip shares them]; 0.3 s ours
        }

        void ApplyAll(AcCarPartSet s)
        {
            s.Invalidate();
            if (_dmg != null) _dmg.ApplySet(s);   // iteration 2: glass zones + bent panels on the newly shown set
            s.SetSteer(_steerDeg);
            for (int i = 0; i < 4; i++) s.SetWheel(i, _spin[i], _travel[i], _wheelSteer[i]);
            s.SetDoor(AcSide.Left, _doorT[0]);
            s.SetDoor(AcSide.Right, _doorT[1]);
            if (s.Lights != null) s.Lights.Set(_lights, true);
            if (s.Instruments != null) s.Instruments.Apply(ref _ins, ref _extras, Time.time);
        }

        /// <summary>Time-based work, once per frame (called by <see cref="AcCarModelDriver"/> in LateUpdate). No allocation.</summary>
        public void Tick(float dt)
        {
            if (_tickFailed || Disposed || _active == null || Root == null) return;
            try
            {
                float dist = 0f;
                Transform cam = ViewerOverride;
                if (cam == null) { var c = AcPlayer.Head(); if (c != null) cam = c.transform; }
                if (cam != null) dist = Vector3.Distance(cam.position, Root.position);

                // LOD by distance (lods.ini IN/OUT)
                if (_sets.Count > 1 && cam != null)
                {
                    AcCarPartSet want = _active;
                    if (dist < _active.LodIn - SwitchHysteresisM || dist >= _active.LodOut + SwitchHysteresisM)
                    {
                        for (int i = 0; i < _sets.Count; i++) if (dist >= _sets[i].LodIn && dist < _sets[i].LodOut) { want = _sets[i]; break; }
                        if (dist >= _sets[_sets.Count - 1].LodOut) want = _sets[_sets.Count - 1];
                    }
                    if (want != _active && want.Root != null)
                    {
                        want.Root.SetActive(true);
                        if (_active.Root != null) _active.Root.SetActive(false);
                        _active = want;
                        ApplyAll(_active);
                    }
                }

                // cockpit HR / LR (lods.ini [COCKPIT_HR] DISTANCE_SWITCH)
                if (_interior && _active == _primary)
                {
                    float sw = (Visual != null ? Visual.CockpitHrSwitch : 7f) + (_active.CockpitIsHr ? SwitchHysteresisM : -SwitchHysteresisM);
                    _active.SetCockpitHr(_viewerInside || cam == null || dist < sw);
                }

                // rim blur
                if (Time.frameCount - _wheelKmhFrame > 2)
                    for (int i = 0; i < 4; i++) _wheelKmh[i] = _ins.SpeedKmh;
                _active.SetBlur(_wheelKmh);

                // wipers: cycle while on, finish the sweep and park when off
                if (_active.Wiper != null && (_wipers || _wiperT > 0f))
                {
                    _wiperT += dt / WiperPeriodS;
                    if (_wiperT >= 1f) _wiperT = _wipers ? _wiperT - 1f : 0f;
                    _active.Wiper.Sample(_wiperT);
                }

                // shift paddle / lever clip (one cycle 0..1)
                if (_shiftAnim != null)
                {
                    _shiftT += dt / _shiftDur;
                    if (_shiftT >= 1f) { _shiftAnim.Sample(0f); _shiftAnim = null; }
                    else _shiftAnim.Sample(_shiftT);
                }

                if (_active.Lights != null) _active.Lights.Tick(dt);

                // iteration 2: cars.visualdamage toggled -> re-apply the stored zones; bent panels wobble (only while damaged and near)
                if (_dmg != null)
                {
                    if (_damageDirty)
                    {
                        _damageDirty = false;
                        if (_dmg.Set(_dmg.Zone(0), _dmg.Zone(1), _dmg.Zone(2), _dmg.Zone(3), _dmg.Zone(4), DamageVisualsOn)) ApplyDamage();
                    }
                    _dmg.Tick(dt, Root, _active, cam != null ? dist : 0f);
                }

                // the local car's display extras come from their owners (R / P / F); others get them through the setters (N)
                bool local = IsLocalCar();
                if (local) FillLocalExtras();
                _wasLocal = local;
                if (_active.Instruments != null) _active.Instruments.Apply(ref _ins, ref _extras, Time.time); // once per frame: values, LED blink, clock

                // AC driver: hidden for the local player in the driver seat, and from LOD_2 on (lods.ini LOD_2 IN is 45-60 m on the
                // install; AC switches to a low driver instead - [DRIVER_HR] DISTANCE_SWITCH 25 m on 13 cars - which we do not build) // ours
                if (DriverModel != null)
                {
                    bool show = !LocalDriverSeat() && (_sets.Count < 2 || _active.Lod < 2);
                    DriverModel.SetVisible(show);
                    DriverModel.Tick(dt);
                }
            }
            catch (Exception e)
            {
                _tickFailed = true;
                AcLog.Warn("Car: " + (Car != null ? Car.Id : "?") + " tick failed, animations stopped: " + e);
            }
        }

        /// <summary>Destroys the model, its meshes, materials and textures.</summary>
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            LiveCount--;
            if (_subscribed) { try { AcSettings.Changed -= _onSetting; } catch (Exception) { } _subscribed = false; }
            for (int i = 0; i < _sets.Count; i++)
            {
                try
                {
                    if (_sets[i].Instruments != null) _sets[i].Instruments.Destroy();
                    _sets[i].DisposeExtra();
                    if (_sets[i].Kn5 != null) _sets[i].Kn5.Dispose();
                }
                catch (Exception e) { AcLog.Warn("Car: dispose LOD failed: " + e.Message); }
            }
            _sets.Clear();
            if (_dmg != null) { try { _dmg.Dispose(); } catch (Exception) { } _dmg = null; }
            if (_smoke != null) { try { _smoke.Destroy(); } catch (Exception) { } _smoke = null; }
            if (DriverModel != null) { try { DriverModel.Dispose(); } catch (Exception) { } DriverModel = null; }
            for (int i = 0; i < _owned.Count; i++) if (_owned[i] != null) UnityEngine.Object.Destroy(_owned[i]);
            _owned.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Root = null; Kn5 = null; _primary = null; _active = null; Hull = new Collider[0]; _texSource = null;
            // the cockpit fonts are shared by every car: free them with the last one (a later car reloads them; failed names retry)
            if (LiveCount <= 0) { try { AcCarFonts.Clear(); } catch (Exception e) { AcLog.Warn("Car: font cache clear failed: " + e.Message); } }
        }

        // ================================================================ interior on demand (a passenger boards a remote / AI car)

        /// <summary>
        /// Builds the high-res cockpit later for a car built without it (Remote / Ai options): re-reads the LOD_0 kn5 on a worker, builds ONLY
        /// COCKPIT_HR (its subtree + the dummies above it; everything else skipped) with W's builder in the same material scope (textures
        /// shared with the car), grafts it under the primary set's same-named parent and binds the dash lights, needles, LEDs and digital
        /// displays. Afterwards the car behaves like one built with Interior = true (HR / LR by distance, <see cref="SetViewer"/> forces HR).
        /// Run with StartCoroutine; <paramref name="done"/> gets true when the interior is there (already, or now), false when it cannot be
        /// built (logged). A second call while one runs waits for it. The interior stays until the car is disposed.
        /// </summary>
        public IEnumerator EnsureInterior(Action<bool> done)
        {
            if (Disposed || _primary == null || Visual == null) { Done(done, false); yield break; }
            while (_interiorBusy) { yield return null; if (Disposed) { Done(done, false); yield break; } }
            if (_interior) { Done(done, true); yield break; }
            var l0 = Visual.Lod(0);
            if (_primary.Lod != 0 || l0 == null || l0.Path == null) { AcLog.InfoOnce("Car: " + Car.Id + " interior on demand: no LOD_0 set"); Done(done, false); yield break; }
            _interiorBusy = true;
            var sw = Stopwatch.StartNew();
            var reread = new AcCarJob.Reread { Path = l0.Path };
            reread.Start();
            while (!reread.Done) yield return null;
            AcKn5Model built = null;
            IEnumerator e = null;
            if (!Disposed && reread.Error == null && reread.Kn5 != null)
            {
                try
                {
                    Kn5Node hr = null;
                    for (int i = 0; i < reread.Kn5.Nodes.Count; i++) if (string.Equals(reread.Kn5.Nodes[i].Name, "COCKPIT_HR", StringComparison.Ordinal)) { hr = reread.Kn5.Nodes[i]; break; }
                    if (hr != null)
                    {
                        var keep = new HashSet<Kn5Node>();
                        for (var p = hr; p != null; p = p.Parent) keep.Add(p);
                        var bo = new AcKn5BuildOptions { Layer = Options.Layer, SkinDir = _skinDir, Readable = false, SliceMs = Options.SliceMs, Skip = InteriorSkip(keep, hr), DamageSlots = _damageSlots };
                        e = AcKn5Builder.BuildAsync(reread.Kn5, Root, bo, null, r => built = r);
                    }
                    else AcLog.InfoOnce("Car: " + Car.Id + " interior on demand: LOD_0 has no COCKPIT_HR");
                }
                catch (Exception ex) { AcLog.Warn("Car: " + Car.Id + " interior build start failed: " + ex.Message); e = null; }
            }
            else if (reread.Error != null) AcLog.Warn("Car: " + Car.Id + " interior on demand: " + reread.Error);
            while (e != null)
            {
                object cur;
                try { if (!e.MoveNext()) break; cur = e.Current; }
                catch (Exception ex) { AcLog.Warn("Car: " + Car.Id + " interior build failed: " + ex.Message); built = null; break; }
                yield return cur;
            }
            bool ok = false;
            if (built != null && !Disposed && _primary != null)
            {
                try
                {
                    ok = _primary.GraftInterior(built, Visual, KnTexture, true);
                    if (ok && !_damageSlots) _flatDamage += AcCarShading.FlattenDamageNormals(built);
                    if (ok && _dmg != null) _dmg.OnGraft(_primary);
                    if (ok)
                    {
                        _interior = true;
                        if (_active == _primary) ApplyAll(_primary);
                        var ins = _primary.Instruments;
                        AcLog.Info("Car: " + Car.Id + " interior (COCKPIT_HR) built on demand in " + sw.ElapsedMilliseconds + " ms (needles " + (ins != null ? ins.NeedleCount : 0)
                            + ", leds " + (ins != null ? ins.LedCount : 0) + ", displays " + (ins != null ? ins.TextCount : 0) + ", lights " + (_primary.Lights != null ? _primary.Lights.Count : 0) + ")");
                    }
                    else AcLog.Warn("Car: " + Car.Id + " interior on demand: COCKPIT_HR could not be grafted");
                }
                catch (Exception ex) { ok = false; AcLog.Warn("Car: " + Car.Id + " interior graft failed: " + ex.Message); }
            }
            if (!ok && built != null) { try { built.Dispose(); } catch (Exception) { } }
            if (reread.Kn5 != null) { try { reread.Kn5.Release(); } catch (Exception) { } }
            _interiorBusy = false;
            Done(done, ok);
        }

        static Predicate<Kn5Node> InteriorSkip(HashSet<Kn5Node> keep, Kn5Node hr)
        {
            return n =>
            {
                if (keep.Contains(n)) return false;
                for (var p = n.Parent; p != null; p = p.Parent) if (p == hr) return false;
                return true;
            };
        }

        static void Done(Action<bool> done, bool ok)
        {
            if (done == null) return;
            try { done(ok); } catch (Exception e) { AcLog.Warn("Car: EnsureInterior callback threw: " + e.Message); }
        }

        /// <summary>AC model-space point (kn5 / knh / DRIVEREYES / collider.kn5) -> body-local Unity point (ac_car_formats.md C1; pitch not applied).</summary>
        public static Vector3 ModelToBody(Vector3 acModelPoint, Vector3 acGraphicsOffset)
        {
            return new Vector3(-(acModelPoint.x + acGraphicsOffset.x), acModelPoint.y + acGraphicsOffset.y, acModelPoint.z + acGraphicsOffset.z);
        }

        /// <summary>AC physics-space point (suspensions / colliders.ini / aero) -> body-local Unity point.</summary>
        public static Vector3 PhysToBody(Vector3 acPhysicsPoint) { return new Vector3(-acPhysicsPoint.x, acPhysicsPoint.y, acPhysicsPoint.z); }
    }
}
