using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Driving aids of one car (player settings within the master's AcRules; AI uses its own). Values as AC's assists.ini.</summary>
    public sealed class AcAssists
    {
        /// <summary>0 off, 1 car's factory setting, 2 on (assists.ini ABS).</summary>
        public int Abs = 1;
        /// <summary>0 off, 1 factory, 2 on (assists.ini TRACTION_CONTROL).</summary>
        public int Tc = 1;
        /// <summary>0..100 (assists.ini STABILITY_CONTROL). Not simulated in iteration 1.</summary>
        public int Stability = 0;
        public bool AutoShift = true;
        public bool AutoClutch = true;
        public bool AutoBlip = true;
        /// <summary>TC / ABS level = row of electronics.ini [TRACTION_CONTROL] / [ABS] CURVE (AC's ctrl+T / ctrl+A levels); -1 = the car's
        /// own starting row (the one nearest SLIP_RATIO_LIMIT). Levels: AcCarSpec.Tc.LevelCount / LimitAt(i).</summary>
        public int TcLevel = -1;
        public int AbsLevel = -1;
    }

    /// <summary>
    /// [P, Physics] The car's driving physics: a MonoBehaviour on the car root = the Rigidbody at the CG (layer <see cref="AcLayers.Car"/>),
    /// four 3-ray wheels (kerbs do not alias at speed), and the Unity-free <see cref="AcChassis"/> + <see cref="AcCarCore"/> sub-stepped at
    /// ~333 Hz inside each FixedUpdate (suspensions.ini springs / dampers / ARBs, tyres.ini forces, the drivetrain, ABS/TC) - the harness
    /// `psim` / `pchassis` runs the same code. Aero per aero.ini (wings, ground-height luts, dynamic controllers); surfaces from
    /// <see cref="AcSurfaceTag"/> (FRICTION, DAMPING, SIN_HEIGHT bumps). Never changes Time.fixedDeltaTime.
    /// Producers write <see cref="Input"/> every frame (S: local driver, I: AI); it publishes <see cref="Telemetry"/> every FixedUpdate and pushes
    /// it to the model (steer, wheels, lights, instruments) and the audio every frame for local / AI cars (N does that for remote cars).
    /// Modes: driven (dynamic, Interpolate), parked (kinematic + solid, parking brake), frozen (kinematic hold, engine revs free) and puppet
    /// (remote car: kinematic, interpolation None, placed by N through <see cref="ApplyPuppet"/>). Every car collider excludes
    /// <see cref="AcLayers.AvatarExcludes"/> while somebody sits in or rides the car, and for 1 s after (ETS lesson).
    /// </summary>
    public sealed class AcCarPhysics : MonoBehaviour
    {
        /// <summary>The controls for this car (set every frame by the producer; edges are cleared by the physics).</summary>
        public AcCarInput Input = AcCarInput.Neutral;
        /// <summary>The car's state, written every FixedUpdate.</summary>
        public AcTelemetry Telemetry;
        /// <summary>Driving aids in effect.</summary>
        public AcAssists Assists = new AcAssists();

        public AcCarSpec Spec { get; private set; }
        public AcCarModel Model { get; private set; }
        public Rigidbody Body { get; private set; }
        /// <summary>Somebody drives it (dynamic; colliders exclude <see cref="AcLayers.AvatarExcludes"/> while seated).</summary>
        public bool Driven { get; private set; }
        /// <summary>A remote car's kinematic stand-in.</summary>
        public bool Puppet { get; private set; }
        /// <summary>Frozen in place (grid before the green, loading, menus).</summary>
        public bool Frozen { get; private set; }
        public float SpeedKmh { get { return Telemetry.SpeedKmh; } }

        // ------------------------------------------------------------------ additions (P)
        /// <summary>The rig this physics belongs to (set by AcCarRig; events carry it).</summary>
        public AcCarRig Rig { get; internal set; }
        /// <summary>The inner model (engine, gearbox, tyres) - Unity-free; null before Init.</summary>
        public AcCarCore Core { get; private set; }
        /// <summary>The sub-stepped chassis (suspension, wheel frames) - Unity-free; null before Init.</summary>
        public AcChassis Chassis { get; private set; }
        /// <summary>Somebody sits in / rides the car (avatar layers excluded on its colliders).</summary>
        public bool Occupied { get; private set; }
        /// <summary>Speed limiter (km/h, 0 = off) - REQUESTS R -> P: R sets AcSession.Rules.PitLimitKmh while AcLocal.InPitlane. AC style:
        /// gas cut above it (the throttle comes back below), Telemetry.Lights |= PitLimiter while set.</summary>
        public float SpeedLimitKmh;
        /// <summary>Alias of <see cref="SpeedLimitKmh"/>.</summary>
        public float PitLimiterKmh { get { return SpeedLimitKmh; } set { SpeedLimitKmh = value; } }
        /// <summary>Server-style rates (AcRules FUEL_RATE / TYRE_WEAR_RATE / 100): 1 = AC's normal consumption / wear.</summary>
        public float FuelRate = 1f, TyreWearRate = 1f;
        /// <summary>How many times the car was set down (spawn, reset, recovery).</summary>
        public int Placements { get; private set; }
        /// <summary>Road-wheel steer angle of the fronts (deg, right positive).</summary>
        public float SteerRoadDeg { get; private set; }
        /// <summary>Sub-steps per FixedUpdate in use (ceil(fixedDeltaTime * 333)).</summary>
        public int Substeps { get; private set; }
        /// <summary>The surface under wheel i (null in the air / untagged ground).</summary>
        public AcSurface WheelSurface(int i) { return _surface[i & 3]; }

        /// <summary>The speed limit in force (km/h, 0 = none) = <see cref="SpeedLimitKmh"/>, which R sets every frame from AcLocal.InPitlane and
        /// AcSession.Rules.PitLimitKmh (AcRace.LocalInputTick; the critic's "unused" note was stale). Gas cut above it, PitLimiter light on.</summary>
        public float EffectiveSpeedLimitKmh { get { return SpeedLimitKmh; } }

        /// <summary>Constant added to the physics travel so C's wheels (posed from their kn5 rest + TravelM) are drawn where the physics hub
        /// is (m, + = up; per wheel; measured once in Init from the model's rest pose). AC draws the wheels at the physics positions.</summary>
        public float VisualTravelOffset(int i) { return _visualOffset[i & 3]; }

        /// <summary>Ride height (m) at car.ini [RIDE] PICKUP_FRONT_HEIGHT / PICKUP_REAR_HEIGHT, updated every FixedUpdate while driven (setup / HUD).</summary>
        public float RideHeightFront { get; private set; }
        public float RideHeightRear { get; private set; }

        /// <summary>Re-reads the player's garage setup (F: AcFront.SetupValue per setup.ini key) and the aids (AcFront.Assists) for this car;
        /// applies only the values that changed since the last apply (fuel is not refilled by an unrelated change). F calls it when a garage
        /// row changes while the car exists (REQUESTS P -> F). Guarded; returns the number of values applied.</summary>
        public int ReapplySetup() { return AcCarRig.ApplyLocalSetup(this); }

        // ------------------------------------------------------------------ iteration 2: damage (DAMAGE_DESIGN.md 1.3, 1.7, 3, 9)
        /// <summary>This car's damage state - exists for every rig (local / AI: written by this physics from its contacts and engine; remote:
        /// written by N through <see cref="AcCarDamage.ApplyWire"/>). Never null.</summary>
        public AcCarDamage Damage { get { return _damage; } }
        /// <summary>Damage multiplier 0..1 (AcRules.DamageMultiplier / 100), written by R / I like <see cref="FuelRate"/> (and at spawn by
        /// AcCarRig). 0 = damage OFF = iteration 1 exactly: no accumulation, no CarDamaged, no extra work in the contact callbacks.</summary>
        public float DamageRate;
        /// <summary>Velocity last given to <see cref="ApplyPuppet"/> (remote cars; the other car's side of an impact speed). Zero otherwise.</summary>
        public Vector3 PuppetVelocity { get { return _puppetVel; } }
        /// <summary>Remote cars: N sets it while the ghost rule calls this car 'special' (just placed, or in the pit lane). Cars other than the
        /// local one (the master's AI) then take no damage from it (fixer 2026-11-02; DAMAGE_DESIGN 1.3 step 9).</summary>
        public bool PuppetGhostable;
        /// <summary>Velocity (world) of a point of this body at the START of the current fixed step (before PhysX resolved the contacts):
        /// v0 + w0 x (p - com0). The impact-speed side of a dynamic car (DAMAGE_DESIGN 1.3 step 3).</summary>
        public Vector3 PointVelocityPreStep(Vector3 worldPoint) { return _v0 + Vector3.Cross(_w0, worldPoint - _com0); }
        /// <summary>Body-space box (Unity axes) the zone pick normalises contact points with: collider.kn5 hull bounds, or the model bounds when
        /// the hull centre is > 0.5 m off them (ks_ford_mustang_2015). Lab / debugging.</summary>
        public Bounds DamageBox { get { return new Bounds(_hullCentre, _hullHalf * 2f); } }

        /// <summary>Repairs the selected parts (AcRepair; Suspension implies Body): Damage.Repair, then the aero factors and the toe at once, a
        /// blown engine back to idle, and <see cref="AcEvents.CarDamaged"/>(Repaired) once when anything changed. R calls it (back to pits,
        /// grid, session change, pit-box repair, multiplier set to 0). Never called by <see cref="ResetTo"/>.</summary>
        public void Repair(AcRepair what)
        {
            bool wasDead = _damage.EngineDead;
            _pendHit = false;                                  // a hit gathered before the repair does not land after it
            _damage.Repair(what);
            RefreshDamageEffects();
            if (wasDead && !_damage.EngineDead && Core != null && Spec != null) Core.EngineOmega = Spec.Engine.Minimum / 9.549297f;
            if (_damage.Version != _pushedVersion) PushDamage(true);
        }

        /// <summary>Lab / harness only: a hit of <paramref name="kmh"/> on the centre of a zone's face of the damage box - the same zone /
        /// corner / accumulation path as a real contact (needs DamageRate > 0). Returns true when it counted.</summary>
        public bool DebugAddImpact(AcDamageZone z, float kmh)
        {
            Vector3 c = _hullCentre, h = _hullHalf;
            Vector3 p;
            switch (z)
            {
                case AcDamageZone.Front: p = new Vector3(c.x, c.y, c.z + h.z); break;
                case AcDamageZone.Rear: p = new Vector3(c.x, c.y, c.z - h.z); break;
                case AcDamageZone.Left: p = new Vector3(c.x - h.x, c.y, c.z); break;
                case AcDamageZone.Right: p = new Vector3(c.x + h.x, c.y, c.z); break;
                default: p = new Vector3(c.x, c.y + h.y, c.z); break;
            }
            return DebugAddImpactAt(p, kmh);
        }

        /// <summary>Lab / harness only: a hit on a zone that also bends the steer rod of <paramref name="corner"/> (0 LF, 1 RF, 2 LR, 3 RR; -1 none).</summary>
        public bool DebugAddImpact(AcDamageZone z, float kmh, int corner)
        {
            _damage.Multiplier = ClampRate(DamageRate);
            bool ok = _damage.AddImpact((int)z, kmh, corner);
            if (ok) RefreshDamageEffects();
            return ok;
        }

        /// <summary>Lab / harness only: a hit at a body-space point (Unity axes, root-relative) - zone pick and nearest-hub corner as a real hit.</summary>
        public bool DebugAddImpactAt(Vector3 bodyPoint, float kmh)
        {
            _damage.Multiplier = ClampRate(DamageRate);
            bool ok = _damage.AddImpact(ZoneAt(bodyPoint, transform.up.y < 0f), kmh, CornerAt(bodyPoint));
            if (ok) RefreshDamageEffects();
            return ok;
        }

        /// <summary>AC's physics rate the sub-steps aim for (Hz) [BELIEF: public knowledge].</summary>
        public const float InnerRate = 333f;
        /// <summary>Fuel density (kg per litre). // ours</summary>
        public const float FuelKgPerLitre = 0.75f;

        private const float RayLift = 0.12f;          // ETS lesson 3: the wheel ray starts 12 cm above the hub
        private const float SeeThroughSeconds = 1f;    // ETS lesson 1: colliders stay see-through 1 s after getting out
        private const float FallSeconds = 3f, FallSpeed = 15f; // ETS fall-through rule

        private readonly AcSurface[] _surface = new AcSurface[4];
        private readonly float[] _spinDeg = new float[4];
        private AcSurfaces _surfaces;
        private Collider[] _colliders = new Collider[0];
        private int[] _baseExcludes = new int[0];
        private float _solidAgainAt = -1f;
        private bool _ready, _reportDriven, _hornOn;
        private float _airborne, _goodTimer;
        private Vector3 _lastGoodPos; private float _lastGoodYaw; private bool _haveLastGood;
        private Vector3 _prevVel;
        private float _latG, _longG;
        private int _pendingUp, _pendingDown, _lastGearChanges;
        private float _fuelMassTimer;
        private float[] _wingAngle = new float[0];
        private AcControllerChain[] _wingCtrl = new AcControllerChain[0];
        private readonly float[] _ctrlIn = new float[(int)AcCtrlInput.Count];
        private static PhysicsMaterial _bodyMaterial;
        private readonly float[] _visualOffset = new float[4];
        private int _refusedGear = int.MinValue;
        // puppet: the pose N asked for, moved to in FixedUpdate with MovePosition (PhysX sees the kinematic velocity)
        private Vector3 _puppetPos; private Quaternion _puppetRot = Quaternion.identity;
        private bool _puppetTarget, _puppetPlaced;
        // ---- damage (iteration 2)
        private AcCarDamage _damage = new AcCarDamage(null, null);
        private Vector3 _v0, _w0, _com0, _puppetVel;              // pre-step cache (top of FixedUpdate) / puppet velocity
        private readonly ContactPoint[] _contacts = new ContactPoint[16]; // ours: 16 > any convex-hull pair seen
        private Collider[] _floorBoxes = new Collider[0];
        private Vector3 _hullCentre = new Vector3(0f, 0.2f, 0f), _hullHalf = new Vector3(0.9f, 0.6f, 2.2f); // ours fallback until Init
        private float[] _clDiv = new float[0], _cdMul = new float[0];
        private int _effectsMechVersion = int.MinValue, _pushedVersion;
        private float _effectsMult = -1f, _lastDamageAt = -10f, _lastDamageKmh;
        private bool _pendHit; private float _pendKmh; private int _pendZone, _pendCorner; // the hit gathered over one fixed step (fixer 2026-11-02)
        private bool _loggedDamage;
        /// <summary>Contact-damage gates, // ours (DAMAGE_DESIGN 1.3 step 5): a hit counts from 3 km/h (as CarImpact); a stay contact only
        /// from 10 km/h and 0.25 s after this car's last counted hit; the steer rod only for a hub within 1 m of the contact.</summary>
        private const float HitMinKmh = 3f, StayMinKmh = 10f, StayGapS = 0.25f, CornerMaxM = 1f;
        /// <summary>Setup values applied so far (key -> setup units); ReapplySetup applies only changes. Built at setup time, not per frame.</summary>
        internal readonly System.Collections.Generic.Dictionary<string, float> AppliedSetup = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sets the car up: Rigidbody mass/inertia from the spec (inertia set ONCE after all colliders exist - ETS lesson), wheels from
        /// suspensions.ini, surfaces for grip. <paramref name="surfaces"/> may be null (hub: default grip).</summary>
        public void Init(AcCarSpec spec, AcCarModel model, AcSurfaces surfaces)
        {
            Spec = spec; Model = model; _surfaces = surfaces;
            if (spec == null) { AcLog.Warn("Physics: Init without a spec on " + name + " - the car stays parked"); return; }
            gameObject.layer = AcLayers.Car;
            Core = new AcCarCore(spec);
            _damage = new AcCarDamage(spec.Damage, LoadDamageRules());
            Core.Damage = _damage;
            Body = GetComponent<Rigidbody>();
            if (Body == null) Body = gameObject.AddComponent<Rigidbody>();
            Body.isKinematic = true;
            Body.useGravity = true;
            Body.interpolation = RigidbodyInterpolation.None;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.linearDamping = 0f;
            Body.angularDamping = 0.05f;   // ours (Unity's default; AC has no body damping)
            Body.maxAngularVelocity = 50f; // the project's DynamicsManager default
            Body.mass = Mass();
            Chassis = new AcChassis(spec, Core, Body.mass, Gravity());
            BuildColliders();
            // ETS lesson 2: the inertia tensor is set ONCE, after every collider of the compound exists, with the CG at the origin.
            ApplyInertia();
            _wingAngle = new float[spec.Wings.Count];
            _wingCtrl = new AcControllerChain[spec.Wings.Count];
            for (int i = 0; i < spec.Wings.Count; i++) { _wingAngle[i] = spec.Wings[i].Angle; _wingCtrl[i] = spec.Wings[i].Controller != null ? spec.Wings[i].Controller.Clone() : null; }
            _clDiv = new float[spec.Wings.Count]; _cdMul = new float[spec.Wings.Count];
            for (int i = 0; i < _clDiv.Length; i++) { _clDiv[i] = 1f; _cdMul[i] = 1f; }
            ComputeDamageBox();
            MeasureVisualOffsets();
            Telemetry = BuildTelemetry(Vector3.zero);
            _ready = true;
            AcLog.InfoOnce("Physics: " + spec.CarId + " spec: " + spec.Summary());
            if (spec.Problems.Count > 0) AcLog.InfoOnce("Physics: " + spec.CarId + " problems: " + string.Join("; ", spec.Problems.ToArray()));
        }

        private static float Gravity() { float g = -Physics.gravity.y; return g > 1f ? g : 9.81f; }

        private float Mass() { return Spec.TotalMassKg + (Core != null ? Core.FuelLitres : Spec.FuelLitres) * FuelKgPerLitre; }

        private void ApplyInertia()
        {
            float m = Body.mass;
            float w = Spec.InertiaBox[0], h = Spec.InertiaBox[1], l = Spec.InertiaBox[2];
            Body.centerOfMass = Vector3.zero;
            // box of the car's mass with car.ini INERTIA = width, height, length (Unity x right, y up, z forward)
            Body.inertiaTensor = new Vector3(m * (h * h + l * l) / 12f, m * (w * w + l * l) / 12f, m * (w * w + h * h) / 12f);
            Body.inertiaTensorRotation = Quaternion.identity;
        }

        private void BuildColliders()
        {
            if (_bodyMaterial == null)
                _bodyMaterial = new PhysicsMaterial("Ac Car Body") { dynamicFriction = 0.3f, staticFriction = 0.3f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum }; // ours (body scraping walls / road)
            int hull = 0;
            Collider[] hullCols = null; Bounds modelBounds = new Bounds();
            try { if (Model != null) { hullCols = Model.Hull; modelBounds = Model.Bounds; } } // C's properties, guarded
            catch (Exception e) { AcLog.WarnOnce("Physics: reading the car model's hull failed (" + e.GetType().Name + ") - body box"); }
            if (hullCols != null)
            {
                for (int i = 0; i < hullCols.Length; i++)
                {
                    Collider c = hullCols[i];
                    if (c == null) continue;
                    var mc = c as MeshCollider;
                    if (mc != null && !mc.convex) mc.convex = true; // a dynamic body needs convex shapes
                    c.gameObject.layer = AcLayers.Car;
                    c.sharedMaterial = _bodyMaterial;
                    hull++;
                }
            }
            if (hull == 0)
            {
                // No collider.kn5 hull from C: the model's bounds (or a car-sized box) as the body - // ours fallback
                var go = new GameObject("Ac Body Box");
                go.layer = AcLayers.Car;
                go.transform.SetParent(transform, false);
                var box = go.AddComponent<BoxCollider>();
                Bounds b = modelBounds;
                Vector3 off = ModelOffset();
                if (b.size.sqrMagnitude > 0.5f)
                {
                    float bottom = b.min.y + 0.15f; // ours: sill height
                    box.center = new Vector3(b.center.x + off.x, (bottom + b.max.y) * 0.5f + off.y, b.center.z + off.z);
                    box.size = new Vector3(b.size.x, Mathf.Max(0.3f, b.max.y - bottom), b.size.z);
                }
                else { box.center = new Vector3(0f, 0.2f, 0f); box.size = new Vector3(Spec.InertiaBox[0], 0.8f, Spec.InertiaBox[2]); }
                box.sharedMaterial = _bodyMaterial;
                AcLog.InfoOnce("Physics: " + Spec.CarId + " has no hull colliders from the model - body box " + box.size.ToString("0.00"));
            }
            // colliders.ini floor boxes (physics space; GROUND_ENABLE): the car's floor meets the road when it bottoms out
            var floors = new System.Collections.Generic.List<Collider>();
            for (int i = 0; i < Spec.Colliders.Count; i++)
            {
                AcColliderBox cb = Spec.Colliders[i];
                if (!cb.GroundEnable) continue;
                var go = new GameObject("Ac Floor " + i);
                go.layer = AcLayers.Car;
                go.transform.SetParent(transform, false);
                var box = go.AddComponent<BoxCollider>();
                box.center = PhysToBody(cb.Centre);
                box.size = new Vector3(cb.Size.X, cb.Size.Y, cb.Size.Z);
                box.sharedMaterial = _bodyMaterial;
                floors.Add(box);
            }
            _floorBoxes = floors.ToArray(); // damage: floor boxes are ground-only (formula_k colliders.ini) - their contacts never damage
            _colliders = GetComponentsInChildren<Collider>(true);
            _baseExcludes = new int[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++) _baseExcludes[i] = _colliders[i].excludeLayers;
            ApplyExcludes();
        }

        /// <summary>Item: the drawn wheels must sit where the physics wheels are (AC renders them at the physics positions). C poses each wheel
        /// from its rest pose + TravelM along its up axis; the physics hub at travel X is Hub + up * X. So offset = physics design hub y - the
        /// drawn rest hub y (body space), read once here after C posed the wheel at travel 0. Without a wheel node: model wheel assumed at
        /// GRAPHICS_OFFSET.y + RADIUS (the kn5 convention, pride census). Logged once per car.</summary>
        private void MeasureVisualOffsets()
        {
            float maxAbs = 0f;
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel wh = Chassis.Wheels[i];
                float drawn = ModelOffset().y + wh.Radius; // fallback
                try
                {
                    if (Model != null)
                    {
                        Transform wt = Model.Wheels[i].Wheel;
                        if (wt != null)
                        {
                            Model.SetWheelState(i, 0f, 0f, 0f); // rest pose (a car_susp ksanim samples its rest frame)
                            drawn = transform.InverseTransformPoint(wt.position).y;
                        }
                    }
                }
                catch (Exception e) { AcLog.WarnOnce("Physics: reading the car model's wheel " + i + " failed (" + e.GetType().Name + ") - wheel assumed at GRAPHICS_OFFSET.y + radius"); }
                _visualOffset[i] = wh.Hub.Y - drawn;
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(_visualOffset[i]));
            }
            AcLog.InfoOnce("Physics: " + Spec.CarId + " wheels drawn at the physics hubs: travel offsets LF/RF/LR/RR " + (_visualOffset[0] * 1000f).ToString("0") + "/" + (_visualOffset[1] * 1000f).ToString("0") + "/" +
                (_visualOffset[2] * 1000f).ToString("0") + "/" + (_visualOffset[3] * 1000f).ToString("0") + " mm; static CG " + Spec.PhysicsCgHeightM.ToString("0.000") + " m vs -GRAPHICS_OFFSET.y " + Spec.CgHeightM.ToString("0.000"));
        }

        /// <summary>Body-local position of the model root: (-GRAPHICS_OFFSET.x, GRAPHICS_OFFSET.y, GRAPHICS_OFFSET.z) (ac_car_formats.md C1).</summary>
        public Vector3 ModelOffset()
        {
            if (Spec == null) return Vector3.zero;
            return new Vector3(-Spec.GraphicsOffset.X, Spec.GraphicsOffset.Y, Spec.GraphicsOffset.Z);
        }

        /// <summary>Body-local hub of wheel i at static ride height (Unity axes).</summary>
        public Vector3 WheelHub(int i)
        {
            if (Chassis == null) return Vector3.zero;
            AcVec3 h = Chassis.Wheels[i & 3].Hub;
            return new Vector3(h.X, h.Y, h.Z);
        }

        // ------------------------------------------------------------------ modes

        /// <summary>Driven on/off: dynamic with interpolation vs parked kinematic and solid (parking brake on).</summary>
        public void SetDriven(bool driven)
        {
            if (driven && Puppet) SetPuppet(false);
            Driven = driven;
            if (Body == null) return;
            if (driven)
            {
                _reportDriven = true;
                if (!Frozen) MakeDynamic();
                return;
            }
            Input = AcCarInput.Neutral;
            MakeKinematic();
            // parked means standing on its wheels: left tipped or in the air, set it down upright where it is (ETS: got out mid-tumble)
            if (transform.up.y < 0.8f || !AnyContact()) ResetTo(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }

        /// <summary>Somebody sits in / rides the car: exclude (or stop excluding) the avatar layers 2|7|24 on every car collider.</summary>
        public void SetOccupied(bool occupied)
        {
            Occupied = occupied;
            _solidAgainAt = occupied ? -1f : Time.time + SeeThroughSeconds;
            ApplyExcludes();
        }

        private void ApplyExcludes()
        {
            bool seeThrough = Occupied || (_solidAgainAt > 0f && Time.time < _solidAgainAt);
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider c = _colliders[i];
                if (c == null) continue;
                // only the avatar bits change: N toggles the Car bit (28) on remote rigs for ghosting (REQUESTS N -> P)
                int cur = c.excludeLayers;
                c.excludeLayers = seeThrough ? (cur | AcLayers.AvatarExcludes) : ((cur & ~AcLayers.AvatarExcludes) | (_baseExcludes[i] & AcLayers.AvatarExcludes));
            }
        }

        /// <summary>Puppet mode for remote cars: kinematic, moved by <see cref="ApplyPuppet"/> through Rigidbody.MovePosition / MoveRotation in
        /// FixedUpdate (so PhysX sees the kinematic velocity: a hit on the local car is a proper contact with the right relative velocity,
        /// not a positional shove) and drawn with Interpolate (smooth between fixed steps, up to one fixed step behind N's timeline - a
        /// deliberate change from ETS lesson 17's transform placement, which gave the body no velocity).</summary>
        public void SetPuppet(bool puppet)
        {
            Puppet = puppet;
            _puppetTarget = false; _puppetPlaced = false;
            if (Body == null) return;
            if (puppet)
            {
                Driven = false;
                MakeKinematic();
                Body.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        /// <summary>Places a puppet (remote car): the pose it moves to in the next FixedUpdate (the first call, and jumps farther than
        /// max(5 m, 0.25 s at its speed) // ours - respawns, placement epochs - teleport at once), its velocity, and the shown state (steer,
        /// rpm, lights...) kept for readers. N drives the model and the audio of remote cars itself (AcNetGame.Cars.Draw).</summary>
        public void ApplyPuppet(Vector3 position, Quaternion rotation, Vector3 velocity, AcTelemetry shown)
        {
            Telemetry = shown;
            if (!Puppet) return;
            _puppetPos = position; _puppetRot = rotation; _puppetTarget = true;
            _prevVel = velocity;
            _puppetVel = velocity;
            Vector3 from = Body != null ? Body.position : transform.position;
            float jump = Mathf.Max(5f, velocity.magnitude * 0.25f); // ours
            if (!_puppetPlaced || (position - from).sqrMagnitude > jump * jump || Body == null || !Body.isKinematic)
            {
                _puppetPlaced = true;
                transform.SetPositionAndRotation(position, rotation);
                if (Body != null) { Body.position = position; Body.rotation = rotation; }
            }
        }

        private void MovePuppet()
        {
            if (!_puppetTarget || Body == null || !Body.isKinematic) return;
            Body.MovePosition(_puppetPos);
            Body.MoveRotation(_puppetRot);
        }

        /// <summary>Teleports the car (stopped) to a pose: wheels on the ground under <paramref name="position"/> (raycast down from 1 m above,
        /// then from 60 m - research T1, ETS lesson 4), heading from <paramref name="rotation"/>'s yaw, CG at hit + up * CG height
        /// (-GRAPHICS_OFFSET.y). Used for spawns, grid, back-to-pits and "reset to track".</summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            Vector3 fwd = rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Vector3 p = position;
            RaycastHit hit;
            bool ground = Physics.Raycast(position + Vector3.up * 1f, Vector3.down, out hit, 50f, AcLayers.GroundMask, QueryTriggerInteraction.Ignore)
                       || Physics.Raycast(position + Vector3.up * 60f, Vector3.down, out hit, 300f, AcLayers.GroundMask, QueryTriggerInteraction.Ignore);
            float cg = Chassis != null ? Chassis.StaticCgHeight() : Spec != null ? Spec.PhysicsCgHeightM : 0.45f; // 0.45 ours fallback (no spec)
            if (ground) p = hit.point + Vector3.up * (cg + 0.01f); // 1 cm // ours: settles onto the springs
            else AcLog.WarnOnce("Physics: ResetTo found no ground (layers 0|14) under " + position.ToString("0.0") + " - placed as asked");
            bool dynamic = Body != null && !Body.isKinematic;
            if (dynamic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            transform.SetPositionAndRotation(p, rot);
            if (Body != null) { Body.position = p; Body.rotation = rot; }
            if (dynamic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            if (Core != null) Core.Reset(false);
            if (Chassis != null) for (int i = 0; i < 4; i++) { Chassis.Wheels[i].X = Chassis.Wheels[i].StaticX; Chassis.Wheels[i].XDot = 0f; }
            _prevVel = Vector3.zero;
            _airborne = 0f;
            _lastGoodPos = p; _lastGoodYaw = rot.eulerAngles.y; _haveLastGood = ground;
            Placements++;
            if (Driven && !Frozen && !Puppet) MakeDynamic();
        }

        /// <summary>Freezes the car in place (grid lock, loading) or releases it. The engine still revs while frozen (the wheels are held).</summary>
        public void Freeze(bool frozen)
        {
            Frozen = frozen;
            if (Body == null) return;
            if (frozen) MakeKinematic();
            else if (Driven && !Puppet) MakeDynamic();
        }

        private void MakeDynamic()
        {
            if (Body == null || Spec == null) return;
            if (Body.isKinematic)
            {
                Body.isKinematic = false;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // walls at 83 m/s (research ets 9.4.4)
                Body.interpolation = RigidbodyInterpolation.Interpolate;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                ApplyInertia();
            }
        }

        private void MakeKinematic()
        {
            if (Body == null) return;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // kinematic bodies support only speculative CCD
                Body.isKinematic = true;
            }
            if (!Puppet) Body.interpolation = RigidbodyInterpolation.None;
        }

        private bool AnyContact()
        {
            if (Chassis == null) return false;
            for (int i = 0; i < 4; i++) if (Chassis.Wheels[i].Contact) return true;
            return false;
        }

        // ------------------------------------------------------------------ setup / tyres (F's garage)

        /// <summary>Switches tyre compound (tyres.ini index). Resets wear and pressures.</summary>
        public void SetCompound(int index)
        {
            if (Core == null) return;
            Core.SetCompound(index);
            Chassis.SetMass(Mass(), Gravity()); // radii, tyre deflection (RATE), static travels
            AppliedSetup.Remove("PRESSURE_LF"); AppliedSetup.Remove("PRESSURE_RF"); AppliedSetup.Remove("PRESSURE_LR"); AppliedSetup.Remove("PRESSURE_RR"); // reset by the compound
        }

        /// <summary>Applies one setup.ini item in setup units (AcSetupItem.Key / value as AC's setup screen): PRESSURE_xx (psi), CAMBER_xx (deg),
        /// FUEL (l), BRAKE_POWER_MULT / FRONT_BIAS / ENGINE_LIMITER / DIFF_POWER / DIFF_COAST (%), DIFF_PRELOAD (Nm), ARB_FRONT/REAR (N/m),
        /// SPRING_RATE_xx (N/m; values under 2000 read as kN/m // ours), DAMP_[FAST_]BUMP/REBOUND_xx (N s/m), BUMP_STOP_RATE_xx, WING_n (angle),
        /// GEAR_n / FINAL_GEAR_RATIO (ratio). Per-wheel suspension items set the axle. ROD_LENGTH_xx (0.1 mm), TOE_OUT_xx (0.01 mm). Returns false for keys not simulated (PACKER_RANGE, heave rods...).</summary>
        public bool ApplySetup(string key, float value)
        {
            bool ok = ApplySetupItem(key, value);
            if (ok) AppliedSetup[key] = value; // remembered for ReapplySetup (whoever applied it: P at spawn, F's garage, ReapplySetup)
            return ok;
        }

        private bool ApplySetupItem(string key, float value)
        {
            if (Core == null || string.IsNullOrEmpty(key)) return false;
            string k = key.ToUpperInvariant();
            int wi = k.EndsWith("_LF") ? 0 : k.EndsWith("_RF") ? 1 : k.EndsWith("_LR") ? 2 : k.EndsWith("_RR") ? 3 : -1;
            switch (k)
            {
                case "FUEL": Core.FuelLitres = Mathf.Clamp(value, 0f, Spec.MaxFuelLitres); if (Body != null) Body.mass = Mass(); Chassis.SetMass(Mass(), Gravity()); return true;
                case "BRAKE_POWER_MULT": Core.BrakePowerMult = value * 0.01f; return true;
                case "FRONT_BIAS": Core.FrontShareOverride = Mathf.Clamp01(value * 0.01f); return true;
                case "ENGINE_LIMITER": Core.EngineLimiterFraction = value * 0.01f; return true;
                case "DIFF_POWER": { AcDiffSpec d = Core.AxleDiff; d.Power = value * 0.01f; Core.AxleDiff = d; return true; }
                case "DIFF_COAST": { AcDiffSpec d = Core.AxleDiff; d.Coast = value * 0.01f; Core.AxleDiff = d; return true; }
                case "DIFF_PRELOAD": { AcDiffSpec d = Core.AxleDiff; d.Preload = value; Core.AxleDiff = d; return true; }
                case "ARB_FRONT": Spec.Front.ArbRate = value; return true;
                case "ARB_REAR": Spec.Rear.ArbRate = value; return true;
                case "FINAL_GEAR_RATIO": if (value > 0.5f) { Core.Final = value; return true; } return false;
            }
            if (k.StartsWith("PRESSURE_") && wi >= 0) { Core.Pressure[wi] = value; return true; }
            if (k.StartsWith("CAMBER_") && wi >= 0) { Chassis.Wheels[wi].CamberDeg = value; return true; }
            // ROD_LENGTH_xx in 0.1 mm clicks, TOE_OUT_xx in 0.01 mm (AcCarSpec.SetupDefault readings, // ours): ride height / toe of that corner
            if (k.StartsWith("ROD_LENGTH_") && wi >= 0) { Chassis.Wheels[wi].Rod = value * 0.0001f; Chassis.SetMass(Mass(), Gravity()); return true; }
            if (k.StartsWith("TOE_OUT_") && wi >= 0) { AcChassisWheel w0 = Chassis.Wheels[wi]; w0.ToeLen = value * 0.00001f; w0.UpdateToe(); return true; } // + steer-rod damage (iteration 2)
            if (k.StartsWith("GEAR_") && !k.StartsWith("GEAR_SET"))
            {
                int g;
                if (int.TryParse(k.Substring(5), out g) && g >= 1 && g <= Core.Gears.Length && value > 0.1f) { Core.Gears[g - 1] = value; return true; }
                return false;
            }
            if (k.StartsWith("WING_"))
            {
                int w;
                if (int.TryParse(k.Substring(5), out w)) for (int i = 0; i < Spec.Wings.Count; i++) if (Spec.Wings[i].Index == w) { _wingAngle[i] = value; return true; }
                return false;
            }
            if (wi >= 0)
            {
                AcAxleSpec ax = wi < 2 ? Spec.Front : Spec.Rear;
                if (k.StartsWith("SPRING_RATE_")) { ax.SpringRate = value < 2000f ? value * 1000f : value; Chassis.SetMass(Mass(), Gravity()); return true; }
                if (k.StartsWith("DAMP_FAST_BUMP_")) { ax.DampFastBump = value; return true; }
                if (k.StartsWith("DAMP_FAST_REBOUND_")) { ax.DampFastRebound = value; return true; }
                if (k.StartsWith("DAMP_BUMP_")) { ax.DampBump = value; return true; }
                if (k.StartsWith("DAMP_REBOUND_")) { ax.DampRebound = value; return true; }
                if (k.StartsWith("BUMP_STOP_RATE_")) { ax.BumpStopRate = value; return true; }
            }
            return false;
        }

        /// <summary>Applies a setup.ini [GEAR_SET_n] (USE_GEARSET=1 cars).</summary>
        public bool ApplyGearSet(int index)
        {
            if (Core == null || index < 0 || index >= Spec.GearSets.Count) return false;
            float[] g = Spec.GearSets[index].Gears;
            for (int i = 0; i < g.Length && i < Core.Gears.Length; i++) if (g[i] > 0.1f) Core.Gears[i] = g[i];
            return true;
        }

        // ------------------------------------------------------------------ the step

        private void Update()
        {
            if (!_ready) return;
            LatchEdges();
            if (_solidAgainAt > 0f && Time.time >= _solidAgainAt) { _solidAgainAt = -1f; ApplyExcludes(); }
            if (_damage.Version != _pushedVersion) PushDamage(!Puppet); // iteration 2: visuals on a change only (N raises CarDamaged for remote cars)
            if (Puppet) return; // N poses remote models and feeds their audio
            if (Driven)
            {
                // the held steering wheel follows the input EVERY frame (90 Hz in a headset), not the 50 Hz FixedUpdate copy: S's hands ride
                // on STEER_HR. The physics reads the same input at its next step.
                Telemetry.SteerWheelDeg = WheelDegFromInput();
                AcCarRig rig = Rig;
                if (rig != null && ReferenceEquals(AcLocal.Car, rig)) AcLocal.Telemetry.SteerWheelDeg = Telemetry.SteerWheelDeg;
            }
            PushVisuals();
        }

        /// <summary>Steering-wheel angle (deg, right +) from the current input: absolute (VR wheel / wheel device) clamped to STEER_LOCK, or
        /// Steer -1..1 * STEER_LOCK.</summary>
        private float WheelDegFromInput()
        {
            float lockDeg = Mathf.Max(1f, Spec.SteerLockDeg);
            return Input.SteerAbsolute ? Mathf.Clamp(Input.SteerWheelDeg, -lockDeg, lockDeg) : Mathf.Clamp(Input.Steer, -1f, 1f) * lockDeg;
        }

        /// <summary>A refused H-shifter change: A's grind sound (guarded).</summary>
        private void GrindAudio()
        {
            AcCarAudio a = Rig != null ? Rig.Audio : null;
            if (a == null) return;
            try { a.Gear(Core.Gear, true); }
            catch (Exception e) { AcLog.WarnOnce("Physics: the gear grind sound failed: " + e.GetType().Name + " " + e.Message); }
        }

        private void LatchEdges()
        {
            if (Input.ShiftUp) { _pendingUp++; Input.ShiftUp = false; }
            if (Input.ShiftDown) { _pendingDown++; Input.ShiftDown = false; }
        }

        private void FixedUpdate()
        {
            if (!_ready) return;
            if (Puppet) { MovePuppet(); return; }
            FlushDamageHit();                                  // fixer 2026-11-02: the last step's contact pairs -> one hit
            // iteration 2: pre-step cache for the impact speed (the contact callbacks run after the PhysX step), multiplier, damage effects
            if (Body != null) { _v0 = Body.linearVelocity; _w0 = Body.angularVelocity; _com0 = Body.worldCenterOfMass; }
            _damage.Multiplier = ClampRate(DamageRate);
            if (_damage.MechVersion != _effectsMechVersion || _damage.Multiplier != _effectsMult) RefreshDamageEffects();
            LatchEdges();
            float dt = Time.fixedDeltaTime;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt * InnerRate));
            Substeps = n;
            ControlsToCore();
            bool dynamic = Body != null && !Body.isKinematic;
            if (!dynamic)
            {
                // parked / frozen: the engine runs (revs on the grid), the wheels are held
                Core.Disconnected = true;
                for (int i = 0; i < 4; i++) { Core.Load[i] = 0f; Core.VLong[i] = 0f; Core.VLat[i] = 0f; }
                for (int s = 0; s < n; s++) Core.Step(dt / n);
                Telemetry = BuildTelemetry(Vector3.zero);
                PublishLocal();
                return;
            }
            Core.Disconnected = false;
            if (_reportDriven)
            {
                _reportDriven = false;
                // only constant facts (InfoOnce de-duplicates by text): no current mass / inertia, which change as fuel burns
                AcLog.InfoOnce("Physics: " + Spec.CarId + " driven: substeps " + n + " at fixedDeltaTime " + dt.ToString("0.0000") + " (" + (n / dt).ToString("0") + " Hz), TOTALMASS " +
                    Spec.TotalMassKg.ToString("0") + " kg + fuel, inertia box " + Spec.InertiaBox[0].ToString("0.00") + "x" + Spec.InertiaBox[1].ToString("0.00") + "x" + Spec.InertiaBox[2].ToString("0.00") +
                    (Body.automaticInertiaTensor ? " AUTOMATIC" : " set by hand") + ", centre of mass at the root, static CG height " + Spec.PhysicsCgHeightM.ToString("0.000") + " m");
            }
            Step(dt, n);
        }

        private void ControlsToCore()
        {
            AcCarCore c = Core;
            AcAssists a = Assists ?? new AcAssists();
            c.AbsMode = a.Abs; c.TcMode = a.Tc; c.AutoShift = a.AutoShift; c.AutoClutch = a.AutoClutch; c.AutoBlip = a.AutoBlip;
            c.TcLevel = a.TcLevel; c.AbsLevel = a.AbsLevel;
            c.FuelRate = FuelRate; c.TyreWearRate = TyreWearRate;
            float gas = Mathf.Clamp01(Input.Throttle);
            float limit = EffectiveSpeedLimitKmh;
            if (limit > 0f && Telemetry.SpeedKmh > limit) gas = 0f;
            c.Gas = Driven ? gas : 0f;
            c.Brake = Driven ? Mathf.Clamp01(Input.Brake) : 0f;
            c.Clutch = Mathf.Clamp01(Input.Clutch);
            // parked: the parking brake (ETS lesson 5); getting in (Driven) releases it
            c.Handbrake = Driven ? Mathf.Clamp01(Input.Handbrake) : 1f;
            while (_pendingUp > 0) { _pendingUp--; if (Driven) c.ShiftUp(); }
            while (_pendingDown > 0) { _pendingDown--; if (Driven) c.ShiftDown(); }
            int req = Input.GearRequest;
            if (req != AcCarInput.NoGearRequest && req != c.Gear && Driven)
            {
                // H-shifter: retried every step (the clutch may come down while the lever is held); the grind sounds once per refused request
                int g0 = c.Grinds;
                if (!c.RequestGear(req) && c.Grinds != g0) { if (_refusedGear != req) { _refusedGear = req; GrindAudio(); } }
                else if (c.Gear == req) _refusedGear = int.MinValue;
            }
            else _refusedGear = int.MinValue;
            // steering: relative input is already filtered by the producer (S: AC controls.ini keyboard / pad filters; I: its own)
            float lockDeg = Mathf.Max(1f, Spec.SteerLockDeg);
            float wheelDeg = WheelDegFromInput();
            if (!Driven) wheelDeg = Telemetry.SteerWheelDeg; // parked: the wheel stays where it was left
            Telemetry.SteerWheelDeg = wheelDeg;
            SteerRoadDeg = wheelDeg / Mathf.Max(1f, Spec.SteerRatio);
            c.SteerNorm = wheelDeg / lockDeg;
        }

        private static AcVec3 A(Vector3 v) { return new AcVec3(v.x, v.y, v.z); }
        /// <summary>AC physics space -> body (Unity) space: x mirrored (AC +X = car left) - the same math as AcCarModel.PhysToBody, kept local
        /// so P's per-step code never calls into another module.</summary>
        private static Vector3 PhysToBody(AcVec3 p) { return new Vector3(-p.X, p.Y, p.Z); }
        private static Vector3 U(AcVec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        private void Step(float dt, int n)
        {
            Transform tr = transform;
            Vector3 up = tr.up, fwd = tr.forward, right = tr.right;
            Vector3 com = Body.worldCenterOfMass;

            // ---- wheel rays (once per FixedUpdate)
            bool anyContact = false;
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel wh = Chassis.Wheels[i];
                CastWheel(i, wh, tr.TransformPoint(U(wh.Hub)), up, fwd);
                anyContact |= wh.Contact;
            }
            Chassis.SteerRoadDeg = SteerRoadDeg;
            Chassis.Prepare(A(up), A(fwd));

            // ---- aero (once per FixedUpdate, acts in every sub-step)
            Vector3 v = Body.linearVelocity;
            Vector3 aeroF = Vector3.zero, aeroT = Vector3.zero;
            Aero(dt, v, up, fwd, right, com, ref aeroF, ref aeroT);

            // ---- the sub-stepped chassis
            Quaternion ir = tr.rotation * Body.inertiaTensorRotation;
            var b = new AcBodyState
            {
                Up = A(up), Fwd = A(fwd), Right = A(right), Com = A(com), V = A(v), W = A(Body.angularVelocity), Gravity = A(Physics.gravity),
                Mass = Body.mass, Inertia = A(Body.inertiaTensor), Qx = ir.x, Qy = ir.y, Qz = ir.z, Qw = ir.w
            };
            Core.LatG = _latG; Core.LongG = _longG;
            AcVec3 f, t;
            Chassis.Step(dt, n, ref b, A(aeroF), A(aeroT), out f, out t);
            Body.AddForce(U(f), ForceMode.Force);
            Body.AddTorque(U(t), ForceMode.Force);

            // ---- g, fuel mass, events, telemetry, recovery
            Vector3 vel = Body.linearVelocity;
            Vector3 acc = (vel - _prevVel) / Mathf.Max(1e-4f, dt);
            _prevVel = vel;
            _latG = Mathf.Lerp(_latG, Vector3.Dot(acc, right) / 9.81f, 0.3f);  // 0.3 // ours smoothing
            _longG = Mathf.Lerp(_longG, Vector3.Dot(acc, fwd) / 9.81f, 0.3f);
            if ((_fuelMassTimer += dt) > 1f) { _fuelMassTimer = 0f; Body.mass = Mass(); Chassis.SetMass(Body.mass, Gravity()); }
            for (int i = 0; i < 4; i++) _spinDeg[i] = Core.SpinRad[i] * Mathf.Rad2Deg;
            if (Core.GearChanges != _lastGearChanges)
            {
                _lastGearChanges = Core.GearChanges;
                AcCarRig rig = Rig;
                if (rig != null) AcEvents.RaiseGearChanged(rig, Core.Gear);
            }
            Telemetry = BuildTelemetry(vel);
            PublishLocal();
            Recovery(dt, anyContact, vel);
        }

        /// <summary>Three rays per wheel (centre and +-R/2 along the car's heading; the tyre circle's height at each offset) from 12 cm above the
        /// hub along -up onto layers 0|14: the highest ground wins - a kerb edge lifts the wheel before the hub passes it. Applies the surface's
        /// SIN_HEIGHT bumps; the patch is put right under the hub.</summary>
        private void CastWheel(int i, AcChassisWheel wh, Vector3 hub, Vector3 up, Vector3 fwd)
        {
            float len = wh.Radius + wh.Droop + RayLift;
            float best = float.MinValue;
            Collider col = null;
            Vector3 normal = up;
            wh.Contact = false;
            for (int k = 0; k < 3; k++)
            {
                float off = k == 0 ? 0f : (k == 1 ? 0.5f : -0.5f) * wh.Radius;
                RaycastHit hit;
                if (!Physics.Raycast(hub + fwd * off + up * RayLift, -up, out hit, len, AcLayers.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider != null && hit.collider.attachedRigidbody == Body) continue;
                float dist = hit.distance - RayLift;
                float circle = Mathf.Sqrt(Mathf.Max(0f, wh.Radius * wh.Radius - off * off));
                float pen = circle - dist; // how far the tyre circle reaches below this ground point
                if (pen <= best) continue;
                best = pen;
                wh.Contact = true;
                wh.Dist = wh.Radius - pen;
                normal = hit.normal;
                col = hit.collider;
            }
            AcSurface s = wh.Contact ? AcSurfaceTag.Of(col) : null; // null on walls / untagged ground (hub floor): default grip
            _surface[i] = s;
            if (!wh.Contact) { wh.Grip = 1f; wh.Damping = 0f; return; }
            wh.Grip = s != null ? s.Friction : 1f;
            wh.Damping = s != null ? s.Damping : 0f;
            if (s != null && s.SinHeight > 0f && s.SinLength > 0.01f)
            {
                // procedural bumps (surfaces.ini SIN_HEIGHT / SIN_LENGTH; grass 0.03 / 0.5) - // ours shape over world x/z
                float k2 = 2f * Mathf.PI / s.SinLength;
                wh.Dist -= s.SinHeight * 0.5f * (Mathf.Sin(hub.x * k2) + Mathf.Sin(hub.z * k2));
            }
            wh.Point = A(hub - up * wh.Dist);
            wh.Normal = A(normal);
        }

        private void Aero(float dt, Vector3 v, Vector3 up, Vector3 fwd, Vector3 right, Vector3 com, ref Vector3 force, ref Vector3 torque)
        {
            // ground under the car from the wheel patches: CG height above them, front and rear
            float hf = 0f, hr = 0f; int nf = 0, nr = 0;
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel wh = Chassis.Wheels[i];
                if (!wh.Contact) continue;
                float hgt = Vector3.Dot(com - U(wh.Point), up);
                if (wh.Front) { hf += hgt; nf++; } else { hr += hgt; nr++; }
            }
            float cg = Spec.PhysicsCgHeightM;
            hf = nf > 0 ? hf / nf : cg + 1f; hr = nr > 0 ? hr / nr : cg + 1f; // in the air: high (ground luts at their far end)
            float rf, rr;
            AcAeroModel.RideHeights(Spec, hf, hr, out rf, out rr);
            RideHeightFront = rf; RideHeightRear = rr;
            if (Spec.Wings.Count == 0) return;
            float speed = v.magnitude;
            if (speed < 1f) return;
            Vector3 dir = v / speed;
            float vz = Vector3.Dot(v, fwd), vy = Vector3.Dot(v, up), vx = Vector3.Dot(v, right);
            float flowPitch = Mathf.Atan2(-vy, Mathf.Max(0.1f, Mathf.Abs(vz))) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(vx, Mathf.Max(0.1f, Mathf.Abs(vz))) * Mathf.Rad2Deg;
            FillWingInputs();
            for (int i = 0; i < Spec.Wings.Count; i++)
            {
                AcWingSpec wg = Spec.Wings[i];
                float angle = _wingCtrl[i] != null ? _wingCtrl[i].Eval(_ctrlIn, dt) : _wingAngle[i];
                float height = AcAeroModel.GroundHeight(Spec, wg, hf, hr); // the wing's height above the ground (aero ground luts)
                float down, drag;
                AcAeroModel.Wing(wg, speed, flowPitch, height, yaw, angle, _clDiv[i], _cdMul[i], out down, out drag); // damage factors (1 / 1 undamaged)
                Vector3 at = transform.TransformPoint(PhysToBody(wg.Position));
                Vector3 F = -up * down - dir * drag;
                force += F;
                torque += Vector3.Cross(at - com, F);
            }
        }

        private void FillWingInputs()
        {
            float[] c = _ctrlIn;
            c[(int)AcCtrlInput.SpeedKmh] = Telemetry.SpeedKmh;
            c[(int)AcCtrlInput.Brake] = Core.Brake;
            c[(int)AcCtrlInput.Gas] = Core.Gas;
            c[(int)AcCtrlInput.LatG] = Mathf.Abs(_latG);
            c[(int)AcCtrlInput.LongG] = _longG;
            c[(int)AcCtrlInput.Steer] = Core.SteerNorm;
            c[(int)AcCtrlInput.Gear] = Core.Gear;
            c[(int)AcCtrlInput.Rpms] = Core.Rpm;
        }

        private AcTelemetry BuildTelemetry(Vector3 vel)
        {
            var t = new AcTelemetry();
            if (Core == null) return t;
            t.Rpm = Core.Rpm;
            t.LimiterRpm = Spec.LimiterRpm;
            t.IdleRpm = Spec.IdleRpm;
            t.Gear = Core.Gear;
            t.GearCount = Core.GearCount;
            t.SpeedKmh = vel.magnitude * 3.6f;
            t.Throttle = Core.GasEffective;
            t.Brake = Core.Brake;
            t.Clutch = Core.Gear == 0 ? Mathf.Clamp01(Core.Clutch) : 1f - Core.ClutchEngage; // neutral: the pedal (the box is open anyway)
            t.SteerWheelDeg = Telemetry.SteerWheelDeg;
            t.BoostBar = Core.BoostTotal;
            t.FuelLitres = Core.FuelLitres;
            t.Limiter = Core.Limiter;
            t.AbsActive = Core.AbsActive;
            t.TcActive = Core.TcActive;
            t.Shifting = Core.Shifting;
            t.LatG = _latG; t.LongG = _longG;
            AcLightState l = AcLightState.None;
            if (Input.Lights) l |= AcLightState.Headlights;
            if (Core.Brake > 0.05f) l |= AcLightState.Brake;
            if (Core.Gear < 0) l |= AcLightState.Reverse;
            if (Core.Limiter || Core.Rpm >= Spec.Drivetrain.AutoShiftUp) l |= AcLightState.Limiter;
            if (EffectiveSpeedLimitKmh > 0f) l |= AcLightState.PitLimiter;
            if (Input.Horn && Driven) l |= AcLightState.Horn;
            t.Lights = l;
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel wh = Chassis.Wheels[i];
                bool contact = wh.Contact && Core.Load[i] > 0f;
                t.SetWheel(i, new AcWheelState
                {
                    SpinDeg = Mathf.Repeat(_spinDeg[i], 360f),
                    AngularVel = Core.Omega[i],
                    SteerDeg = wh.SteerDeg,
                    TravelM = (wh.Contact ? Mathf.Clamp(wh.X, -wh.Droop, 0.3f) : -wh.Droop) + _visualOffset[i], // + offset: drawn at the physics hub
                    SlipRatio = Core.SlipRatio[i],
                    SlipAngleDeg = Core.SlipAngleDeg[i],
                    LoadN = Core.Load[i],
                    Skid = Core.Skid[i],
                    Contact = contact,
                    Surface = wh.Contact ? _surface[i] : null
                });
            }
            return t;
        }

        private void PublishLocal()
        {
            AcCarRig rig = Rig;
            if (rig != null && ReferenceEquals(AcLocal.Car, rig)) AcLocal.Telemetry = Telemetry;
        }

        /// <summary>Every frame for local / AI cars: the model (steering wheel, wheels, lights, instruments) and the audio.</summary>
        private void PushVisuals()
        {
            AcTelemetry t = Telemetry;
            AcCarModel m = Model;
            if (m != null)
            {
                try
                {
                    m.ApplyTelemetry(ref t); // C: SetSteer + 4 x SetWheelState + rim blur + SetLights + SetInstruments, allocation-free
                }
                catch (Exception e) { AcLog.WarnOnce("Physics: pushing the state to the car model failed: " + e.GetType().Name + " " + e.Message); }
            }
            AcCarAudio a = Rig != null ? Rig.Audio : null;
            if (a != null)
            {
                try
                {
                    a.Feed(t);
                    bool horn = Input.Horn && Driven;
                    if (horn != _hornOn) { _hornOn = horn; a.Horn(horn); }
                }
                catch (Exception e) { AcLog.WarnOnce("Physics: feeding the car audio failed: " + e.GetType().Name + " " + e.Message); }
            }
        }

        /// <summary>ETS fall-through rule: no wheel on the ground for 3 s while falling faster than 15 m/s = through the world: back to the last
        /// spot it stood level (kept every second), logged.</summary>
        private void Recovery(float dt, bool anyContact, Vector3 vel)
        {
            _airborne = anyContact || vel.y > -FallSpeed ? 0f : _airborne + dt;
            if (anyContact && Mathf.Abs(vel.y) < 2f && transform.up.y > 0.9f && (_goodTimer += dt) > 1f)
            {
                _goodTimer = 0f;
                _lastGoodPos = transform.position;
                _lastGoodYaw = transform.eulerAngles.y;
                _haveLastGood = true;
            }
            if (_airborne < FallSeconds) return;
            _airborne = 0f;
            Vector3 at = transform.position;
            if (_haveLastGood)
            {
                ResetTo(_lastGoodPos, Quaternion.Euler(0f, _lastGoodYaw, 0f));
                AcLog.Info("Physics: " + Spec.CarId + " fell through at " + at.ToString("0") + " / recovered to " + transform.position.ToString("0.0"));
            }
            else
            {
                MakeKinematic();
                AcLog.Info("Physics: " + Spec.CarId + " fell through at " + at.ToString("0") + " / no safe spot known - held in place");
            }
        }

        private float _lastImpactAt = -10f;

        private void OnCollisionEnter(Collision collision)
        {
            Impact(collision, true);
            if (DamageRate > 0f) DamageHit(collision, true); // iteration 2; nothing at all at DAMAGE 0 (gathered per step, see FlushDamageHit)
        }

        /// <summary>REQUESTS A -> P: while scraping (a wall, the road when bottoming) the impact is raised again at 10 Hz with the sliding
        /// speed, so A turns the repeats into common.bank scrapes. Damage: a stay contact counts again only past the stay gates.</summary>
        private void OnCollisionStay(Collision collision)
        {
            if (Time.time - _lastImpactAt >= 0.1f) Impact(collision, false); // 10 Hz // ours
            if (DamageRate > 0f && Time.time - _lastDamageAt >= StayGapS) DamageHit(collision, false);
        }

        private void Impact(Collision collision, bool entered)
        {
            if (!_ready || Puppet || Body == null || Body.isKinematic) return;
            if (collision.contactCount == 0) return;
            ContactPoint cp = collision.GetContact(0);
            float kmh = (entered ? Mathf.Abs(Vector3.Dot(collision.relativeVelocity, cp.normal)) : collision.relativeVelocity.magnitude) * 3.6f;
            if (kmh < (entered ? 3f : 5f)) return; // ours: below 3 km/h (hit) / 5 km/h (scrape) nothing is raised
            _lastImpactAt = Time.time;
            AcCarRig rig = Rig;
            if (rig != null)
            {
                AcEvents.RaiseCarImpact(rig, kmh, cp.point);
                if (!AcEvents.CarCollisionWanted) return;   // integration 2026-11-02: nobody listens - the iteration-1 path stays as it was
                // iteration 2: the same contact with its normal and the other car (A's angle, C's sparks)
                AcCarPhysics op = OtherPhysics(collision);
                Collider oc = collision.collider;
                bool withCar = op != null || (oc != null && oc.gameObject.layer == AcLayers.Car);
                AcEvents.RaiseCarCollision(rig, kmh, cp.point, cp.normal, op != null ? op.Rig : null, withCar);
            }
        }

        private static AcCarPhysics OtherPhysics(Collision collision)
        {
            Rigidbody rb = collision.rigidbody;
            AcCarPhysics op = null;
            if (rb != null) rb.TryGetComponent(out op); // allocation-free
            return op;
        }

        // ------------------------------------------------------------------ damage (iteration 2)

        private static float ClampRate(float r) { return r > 0f ? (r < 1f ? r : 1f) : 0f; }

        /// <summary>DAMAGE_DESIGN 1.3: impact speed = max over this car's non-floor contacts of |(vA - vB) . n| (km/h, pre-step velocities),
        /// point = their mean; gates; surfaces add nothing (except upside down -> CENTER); zone from the damage box; steer-rod corner = the
        /// nearest hub within 1 m; AcCarDamage.AddImpact. Only for cars this client simulates (dynamic, not a puppet).</summary>
        private void DamageHit(Collision collision, bool entered)
        {
            if (!_ready || Puppet || Body == null || Body.isKinematic) return;
            bool rollover = transform.up.y < 0f;
            Collider other = collision.collider;
            if (!rollover && other != null && AcSurfaceTag.Of(other) != null) return; // road / kerb / grass / sand: no damage
            AcCarPhysics op = OtherPhysics(collision);
            // fixer 2026-11-02: the master's AI cars obey the ghost rule too - a remote car just placed / in the pit lane does no damage to a car
            // that is not the local one (AcNetGame.Contacts only ghosts relative to the local car)
            if (op != null && op.Puppet && op.PuppetGhostable && !ReferenceEquals(AcLocal.Car, Rig)) return;
            Rigidbody orb = collision.rigidbody;
            AcRemoteContact standIn = null;
            if (op == null && orb != null) orb.TryGetComponent(out standIn); // a grey stand-in box: its drawn velocity (allocation-free)
            if (standIn != null && standIn.Ghostable && !ReferenceEquals(AcLocal.Car, Rig)) return;
            int n = collision.GetContacts(_contacts);
            if (n <= 0) return;
            if (n > _contacts.Length) n = _contacts.Length;
            bool opDynamic = op != null && !op.Puppet && op.Body != null && !op.Body.isKinematic;
            float vmax = 0f; Vector3 sum = Vector3.zero; int kept = 0;
            for (int k = 0; k < n; k++)
            {
                ContactPoint c = _contacts[k];
                if (IsFloorBox(c.thisCollider)) continue;
                if (op != null && op.IsFloorBox(c.otherCollider)) continue; // the other car's floor boxes are ground-only too (design 1.3 step 2)
                Vector3 p = c.point;
                Vector3 vA = _v0 + Vector3.Cross(_w0, p - _com0);
                Vector3 vB = op != null ? (op.Puppet ? op.PuppetVelocity : opDynamic ? op.PointVelocityPreStep(p) : Vector3.zero)
                                        : standIn != null ? standIn.Velocity
                                        : orb != null ? orb.GetPointVelocity(p) : Vector3.zero;
                float v = Mathf.Abs(Vector3.Dot(vA - vB, c.normal)) * 3.6f;
                if (v > vmax) vmax = v;
                sum += p; kept++;
            }
            if (kept == 0 || vmax < (entered ? HitMinKmh : StayMinKmh)) return;
            // fixer 2026-11-02: a re-entry within the stay gap of the last counted hit (a puppet contact flickering door-to-door) counts only
            // when it is clearly harder than that hit
            if (entered && Time.time - _lastDamageAt < StayGapS && (vmax < StayMinKmh || vmax <= _lastDamageKmh)) return;
            // fixer 2026-11-02: one event per impact (design 1.3 step 4) - Unity calls back once per collider pair, so the pairs of one fixed
            // step are gathered (max vImp with its zone / corner) and applied once at the top of the next FixedUpdate
            if (_pendHit && vmax <= _pendKmh) return;
            Vector3 local = transform.InverseTransformPoint(sum / kept);
            _pendHit = true;
            _pendKmh = vmax;
            _pendZone = ZoneAt(local, rollover);
            _pendCorner = CornerAt(local);
        }

        /// <summary>Applies the hit gathered over the last fixed step's contact callbacks (one AddImpact per step). Top of FixedUpdate.</summary>
        private void FlushDamageHit()
        {
            if (!_pendHit) return;
            _pendHit = false;
            if (DamageRate <= 0f) return;
            _damage.Multiplier = ClampRate(DamageRate);
            if (_damage.AddImpact(_pendZone, _pendKmh, _pendCorner))
            {
                _lastDamageAt = Time.time;
                _lastDamageKmh = _pendKmh;
                RefreshDamageEffects();
            }
        }

        /// <summary>True when <paramref name="c"/> is one of this car's 'Ac Floor n' boxes (ground-only colliders; never a damage contact).</summary>
        internal bool IsFloorBox(Collider c)
        {
            for (int i = 0; i < _floorBoxes.Length; i++) if (ReferenceEquals(_floorBoxes[i], c)) return true;
            return false;
        }

        /// <summary>Zone of a body-space point (DAMAGE_DESIGN 1.3 step 7) normalised by the damage box: nx, nz = offset / half width / half
        /// length, ny = height above the box bottom / box height -> AcCarDamage.PickZone.</summary>
        private int ZoneAt(Vector3 local, bool rollover)
        {
            float nx = (local.x - _hullCentre.x) / _hullHalf.x;
            float nz = (local.z - _hullCentre.z) / _hullHalf.z;
            float ny = (local.y - (_hullCentre.y - _hullHalf.y)) / (2f * _hullHalf.y);
            return AcCarDamage.PickZone(nx, ny, nz, rollover);
        }

        /// <summary>The wheel hub (design point, body space) nearest to a body-space point in the horizontal plane, within 1 m // ours; -1 none.</summary>
        private int CornerAt(Vector3 local)
        {
            if (Chassis == null) return -1;
            int best = -1; float bestD = CornerMaxM * CornerMaxM;
            for (int i = 0; i < 4; i++)
            {
                AcVec3 h = Chassis.Wheels[i].Hub;
                float dx = local.x - h.X, dz = local.z - h.Z;
                float d = dx * dx + dz * dz;
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Aero factors per wing and the damaged toe per corner from the current damage (on a MechVersion / multiplier change only).
        /// A corner's toe is rewritten only when its rod offset changed, so an undamaged car keeps its iteration-1 toe untouched.</summary>
        private void RefreshDamageEffects()
        {
            _effectsMechVersion = _damage.MechVersion; _effectsMult = _damage.Multiplier;
            if (Spec != null)
                for (int i = 0; i < _clDiv.Length && i < Spec.Wings.Count; i++) _damage.AeroFactors(Spec.Wings[i], out _clDiv[i], out _cdMul[i]);
            if (Chassis != null)
                for (int i = 0; i < 4; i++)
                {
                    AcChassisWheel wh = Chassis.Wheels[i];
                    float r = _damage.RodOffset[i];
                    if (wh.RodOffset != r) { wh.RodOffset = r; wh.UpdateToe(); }
                }
        }

        /// <summary>DAMAGE_DESIGN 1.7 push (Update, on a Version change only): C's visuals (ZoneByte values, engine smoke) through
        /// <see cref="IAcCarDamageVisual"/>, then <see cref="AcEvents.CarDamaged"/> once with the changes since the last raise (not for
        /// puppets: N raises those when ApplyWire returns true).</summary>
        private void PushDamage(bool raise)
        {
            _pushedVersion = _damage.Version;
            object m = Model;
            var vis = m as IAcCarDamageVisual;
            if (vis != null)
            {
                try
                {
                    vis.SetDamage(_damage.ZoneByte(0), _damage.ZoneByte(1), _damage.ZoneByte(2), _damage.ZoneByte(3), _damage.ZoneByte(4));
                    vis.SetEngineSmoke(_damage.EngineSmoking ? 1f : 0f); // ours: on / off (C shapes it from engine_smoke.ini)
                }
                catch (Exception e) { AcLog.WarnOnce("Physics: pushing the damage to the car model failed: " + e.GetType().Name + " " + e.Message); }
            }
            if (!raise) return;
            float kmh;
            AcDamageChange what = _damage.TakeChanges(out kmh);
            if (!_loggedDamage && (what & AcDamageChange.Repaired) == 0 && Spec != null)
            {
                _loggedDamage = true;
                AcLog.Info("Physics: " + Spec.CarId + " first damage (multiplier " + (_damage.Multiplier * 100f).ToString("0") + " %): zones F/R/L/R/C " + _damage.ZoneByte(0) + "/" + _damage.ZoneByte(1) + "/" +
                    _damage.ZoneByte(2) + "/" + _damage.ZoneByte(3) + "/" + _damage.ZoneByte(4) + " km/h, suspension " + _damage.SuspPct.ToString("0") + " %, engine " + _damage.EnginePct.ToString("0") + " %" +
                    (vis != null ? "" : " (car model has no damage visuals yet)"));
            }
            AcCarRig rig = Rig;
            if (rig != null) AcEvents.RaiseCarDamaged(rig, what, kmh);
        }

        /// <summary>The body-space box the zone pick uses: the collider.kn5 hull bounds (C's Model.Hull, measured in this root's space), or the
        /// model bounds (+ the model offset) when there is no hull or its centre is > 0.5 m off them (ks_ford_mustang_2015, DAMAGE_DESIGN 1.3).</summary>
        private void ComputeDamageBox()
        {
            bool haveHull = false, haveModel = false;
            Bounds hb = new Bounds(), mb = new Bounds();
            try
            {
                if (Model != null)
                {
                    Bounds b = Model.Bounds;
                    if (b.size.sqrMagnitude > 0.5f) { mb = new Bounds(b.center + ModelOffset(), b.size); haveModel = true; }
                    Collider[] hull = Model.Hull;
                    if (hull != null)
                        for (int i = 0; i < hull.Length; i++)
                        {
                            Bounds lb;
                            if (hull[i] == null || !LocalBounds(hull[i], out lb)) continue;
                            if (!haveHull) { hb = lb; haveHull = true; } else hb.Encapsulate(lb);
                        }
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Physics: reading the car model's bounds for damage failed (" + e.GetType().Name + ") - box from the spec"); }
            string from;
            Bounds use;
            if (haveHull && (!haveModel || (hb.center - mb.center).magnitude <= 0.5f)) { use = hb; from = "hull"; } // 0.5 m // ours
            else if (haveModel) { use = mb; from = haveHull ? "model bounds (hull centre " + (hb.center - mb.center).magnitude.ToString("0.00") + " m off)" : "model bounds"; }
            else { use = new Bounds(new Vector3(0f, 0.2f, 0f), new Vector3(Spec.InertiaBox[0], 1.2f, Spec.InertiaBox[2])); from = "inertia box"; } // ours fallback
            _hullCentre = use.center;
            _hullHalf = new Vector3(Mathf.Max(0.1f, use.extents.x), Mathf.Max(0.1f, use.extents.y), Mathf.Max(0.1f, use.extents.z)); // ours guards
            AcLog.InfoOnce("Physics: " + Spec.CarId + " damage box from the " + from + ": centre " + _hullCentre.ToString("0.00") + " size " + (_hullHalf * 2f).ToString("0.00"));
        }

        private bool LocalBounds(Collider c, out Bounds b)
        {
            b = new Bounds();
            Vector3 ctr, ext; Transform t = c.transform;
            var mc = c as MeshCollider; var bc = c as BoxCollider;
            if (mc != null && mc.sharedMesh != null) { ctr = mc.sharedMesh.bounds.center; ext = mc.sharedMesh.bounds.extents; }
            else if (bc != null) { ctr = bc.center; ext = bc.size * 0.5f; }
            else
            {
                Bounds w = c.bounds; t = null; ctr = w.center; ext = w.extents;
            }
            bool first = true;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = ctr + new Vector3((k & 1) != 0 ? ext.x : -ext.x, (k & 2) != 0 ? ext.y : -ext.y, (k & 4) != 0 ? ext.z : -ext.z);
                Vector3 world = t != null ? t.TransformPoint(corner) : corner;
                Vector3 local = transform.InverseTransformPoint(world);
                if (first) { b = new Bounds(local, Vector3.zero); first = false; } else b.Encapsulate(local);
            }
            return true;
        }

        /// <summary>system/cfg damage rules of the install AcGame found (loaded once per install, cached); the fallbacks without one.</summary>
        private static AcDamageRules LoadDamageRules()
        {
            try
            {
                AcGame g = AcGame.Current;
                if (g != null && g.Install != null) return AcDamageRules.Load(g.Install);
            }
            catch (Exception e) { AcLog.WarnOnce("Physics: reading the damage rules failed (" + e.GetType().Name + ") - fallbacks"); }
            return AcDamageRules.Current;
        }

        private void OnDestroy()
        {
            AcCarRig rig = Rig;
            if (rig != null && ReferenceEquals(AcLocal.Car, rig)) AcLocal.Telemetry = default(AcTelemetry);
        }
    }
}
