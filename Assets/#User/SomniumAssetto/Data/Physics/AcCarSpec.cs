using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [P, Physics] A car's physics numbers, parsed from its data.acd (Data/Acd/AcdArchive) - car.ini, engine.ini + power.lut + turbo,
    /// drivetrain.ini, gearbox ratios, suspensions.ini, tyres.ini (VERSION=10), aero.ini, brakes.ini, electronics.ini, ctrl_*.ini, fuel,
    /// colliders.ini, setup.ini. Unity-FREE (compiled by the harness too); thread-safe to load on a worker. AC coordinates as stored
    /// (AC +X = car LEFT; physics-space points are CG space; model-space points (DRIVEREYES) are ground space - ac_car_formats.md C1).
    /// Numbers our code invents are marked // ours. Census (harness `spec`): all 86 owned cars parse.
    /// </summary>
    public sealed class AcCarSpec
    {
        public string CarId = "";
        /// <summary>The raw inis by file name ("car.ini", "engine.ini", ...) as read from data.acd - for anything not parsed yet.</summary>
        public readonly Dictionary<string, AcIni> Inis = new Dictionary<string, AcIni>(StringComparer.OrdinalIgnoreCase);
        /// <summary>What could not be read (logged once per car by the loader's caller).</summary>
        public readonly List<string> Problems = new List<string>();

        // ---- car.ini
        /// <summary>[INFO] SCREEN_NAME.</summary>
        public string ScreenName = "";
        /// <summary>[BASIC] TOTALMASS (kg, with driver, no fuel).</summary>
        public float TotalMassKg = 1200f; // ours default until parsed
        /// <summary>[BASIC] INERTIA box (width, height, length m).</summary>
        public float[] InertiaBox = { 1.8f, 1.2f, 4.4f }; // ours default until parsed
        /// <summary>[BASIC] GRAPHICS_OFFSET (AC coords): physics point = model point + offset.</summary>
        public AcVec3 GraphicsOffset;
        /// <summary>[BASIC] GRAPHICS_PITCH_ROTATION (deg; Unity model root Euler(x=this)).</summary>
        public float GraphicsPitchDeg;
        /// <summary>[GRAPHICS] DRIVEREYES (MODEL space, AC coords).</summary>
        public AcVec3 DriverEyes;
        /// <summary>[CONTROLS] STEER_LOCK (deg, centre to full right, steering-WHEEL angle).</summary>
        public float SteerLockDeg = 360f; // ours default until parsed
        /// <summary>[CONTROLS] STEER_RATIO (wheel deg per road-wheel deg).</summary>
        public float SteerRatio = 14f; // ours default until parsed
        /// <summary>[CONTROLS] LINEAR_STEER_ROD_RATIO, FFMULT (informative).</summary>
        public float LinearSteerRodRatio, FfMult;
        /// <summary>[FUEL] FUEL (start litres) and MAX_FUEL.</summary>
        public float FuelLitres = 30f, MaxFuelLitres = 60f; // ours defaults until parsed
        /// <summary>[FUEL] CONSUMPTION: litres per second = rpm * gas * CONSUMPTION / 1000 [READ].</summary>
        public float FuelConsumption;
        /// <summary>[FUELTANK] POSITION (physics space). Not simulated: the fuel mass sits at the CG (60 l = ~45 kg of ~1300; // ours simplification).</summary>
        public AcVec3 FuelTankPosition;
        /// <summary>[RIDE] PICKUP_FRONT_HEIGHT / PICKUP_REAR_HEIGHT (m, physics space y of the ride-height points): AcAeroModel.RideHeights /
        /// AcCarPhysics.RideHeightFront/Rear. The aero ground luts read the WING height instead (evidence in AcAeroModel.GroundHeight).</summary>
        public float PickupFrontHeight = -0.3f, PickupRearHeight = -0.3f; // ours defaults until parsed

        // ---- engine.ini
        /// <summary>[ENGINE_DATA] MINIMUM (idle rpm).</summary>
        public float IdleRpm = 900f; // ours default until parsed
        /// <summary>[ENGINE_DATA] LIMITER (rpm).</summary>
        public float LimiterRpm = 7000f; // ours default until parsed
        /// <summary>power.lut (rpm -> torque Nm at full throttle).</summary>
        public AcLut TorqueCurve = new AcLut();
        /// <summary>All of engine.ini (turbos, coast, inertia, limiter frequency).</summary>
        public AcEngineSpec Engine = new AcEngineSpec();

        // ---- drivetrain.ini
        /// <summary>[GEARS] GEAR_1..n ratios (forward), GEAR_R, FINAL.</summary>
        public float[] GearRatios = new float[0];
        public float ReverseRatio = -3f; // ours default until parsed
        public float FinalRatio = 4f;    // ours default until parsed
        /// <summary>[TRACTION] TYPE: "RWD", "FWD", "AWD", "AWD2".</summary>
        public string Traction = "RWD";
        /// <summary>All of drivetrain.ini.</summary>
        public AcDrivetrainSpec Drivetrain = new AcDrivetrainSpec();

        // ---- tyres.ini / suspensions.ini (per wheel 0 LF, 1 RF, 2 LR, 3 RR)
        /// <summary>tyres.ini RADIUS per wheel (m) of the default compound.</summary>
        public float[] WheelRadius = { 0.33f, 0.33f, 0.33f, 0.33f }; // ours defaults until parsed
        /// <summary>suspensions.ini [BASIC] WHEELBASE (m), CG_LOCATION (front weight fraction), [FRONT]/[REAR] TRACK (m).</summary>
        public float WheelbaseM = 2.6f, CgLocation = 0.5f, TrackFrontM = 1.6f, TrackRearM = 1.6f; // ours defaults until parsed
        /// <summary>suspensions.ini [FRONT] / [REAR] (+ ARB, axle z).</summary>
        public AcAxleSpec Front = new AcAxleSpec(), Rear = new AcAxleSpec();
        /// <summary>tyres.ini compounds (index order) and [COMPOUND_DEFAULT] INDEX.</summary>
        public readonly List<AcTyreCompound> Compounds = new List<AcTyreCompound>();
        public int DefaultCompound;

        // ---- aero / brakes / electronics / controllers
        public readonly List<AcWingSpec> Wings = new List<AcWingSpec>();
        public AcBrakeSpec Brakes = new AcBrakeSpec();
        public AcAidSpec Abs = new AcAidSpec(), Tc = new AcAidSpec();
        /// <summary>electronics.ini [EDL] PRESENT (not simulated in iteration 1; reported).</summary>
        public bool EdlPresent;
        /// <summary>ctrl_single_lock.ini (electronic diff lock, axle Nm), ctrl_awd2.ini (centre coupling Nm), ctrl_awd_center_lock.ini,
        /// ctrl_ebb.ini (electronic brake balance = front share). Null when the car has none.</summary>
        public AcControllerChain CtrlSingleLock, CtrlAwd2, CtrlAwdCentreLock, CtrlEbb;
        /// <summary>colliders.ini boxes (physics space).</summary>
        public readonly List<AcColliderBox> Colliders = new List<AcColliderBox>();
        /// <summary>setup.ini items (F's garage) and gear sets.</summary>
        public readonly List<AcSetupItem> Setup = new List<AcSetupItem>();
        public readonly List<AcGearSet> GearSets = new List<AcGearSet>();
        /// <summary>ai.ini [GEARS] UP / DOWN (rpm) for module I (0 when absent).</summary>
        public float AiShiftUp, AiShiftDown;
        /// <summary>Iteration 2: the mechanical damage keys (suspensions.ini / engine.ini [DAMAGE], car.ini [PIT_STOP]); never null.</summary>
        public AcDamageSpec Damage = new AcDamageSpec();

        /// <summary>Number of forward gears.</summary>
        public int GearCount { get { return GearRatios != null ? GearRatios.Length : 0; } }

        /// <summary>The compound the car starts on (never null after a successful load).</summary>
        public AcTyreCompound Compound(int index)
        {
            for (int i = 0; i < Compounds.Count; i++) if (Compounds[i].Index == index) return Compounds[i];
            return Compounds.Count > 0 ? Compounds[0] : null;
        }

        /// <summary>Fuel density used for the static mass (kg per litre). // ours (same as AcCarPhysics.FuelKgPerLitre)</summary>
        public const float FuelDensity = 0.75f;

        /// <summary>CG height the PHYSICS settles at on flat ground with the car's default fuel and setup. Reading of the suspension (// ours,
        /// second pass, evidence in DevTools/agents/P/pride_all.txt): BASEY is the wheel centre at the suspension's design point where the
        /// springs are at their free length, ROD_LENGTH preloads them ("positive raises ride height" - Kunos; across the 86 cars ROD_LENGTH
        /// tracks the static spring sag, e.g. gt86 155 mm rod / 160 mm sag, 458_gt2 15 / 24), and the tyre deflects by load / RATE. So the
        /// static wheel centre sits at BASEY + (sag - rod) and the CG at RADIUS - deflection - that, blended at CG_LOCATION. With it the race
        /// cars' aero ground-height luts read ~1.0 at static (z4_gt3 0.94/1.00, 458_gt2 0.80/0.93) where "design point = static" gave 0.45-0.75.</summary>
        public float PhysicsCgHeightM { get { return StaticCgHeight(TotalMassKg + FuelLitres * FuelDensity, 9.81f); } }

        /// <summary>Static CG height (m) at a total mass, default compound and the axles' own rod lengths.</summary>
        public float StaticCgHeight(float massKg, float gravity)
        {
            AcTyreCompound c = Compound(DefaultCompound);
            float hf = AxleStaticHeight(Front, c != null ? c.Front : null, massKg * gravity * CgLocation * 0.5f);
            float hr = AxleStaticHeight(Rear, c != null ? c.Rear : null, massKg * gravity * (1f - CgLocation) * 0.5f);
            return hf + (hr - hf) * (1f - CgLocation);
        }

        /// <summary>CG height above the ground at one axle under a corner load: RADIUS - load / RATE - (BASEY + static travel).</summary>
        public static float AxleStaticHeight(AcAxleSpec ax, AcTyreSpec t, float cornerLoad)
        {
            float r = t != null ? t.Radius : 0.33f, rate = t != null ? t.Rate : 250000f; // ours fallbacks (no tyre)
            float defl = cornerLoad / Math.Max(1e4f, rate);
            return r - defl - (ax.BaseY + AcChassis.StaticTravel(ax, cornerLoad, ax.RodLength));
        }

        /// <summary>-GRAPHICS_OFFSET.y: the model's ground origin sits this far under the CG (ac_car_formats.md C1). The physics static CG
        /// (<see cref="PhysicsCgHeightM"/>) differs per car (pride census: sd 3 cm); the drawn wheels follow the physics (AcCarPhysics visual offsets).</summary>
        public float CgHeightM { get { return -GraphicsOffset.Y > 0.05f ? -GraphicsOffset.Y : Math.Max(0.15f, Front.Track > 0 ? WheelRadius[0] - Front.BaseY : 0.4f); } }

        /// <summary>Wheel centre i (0 LF, 1 RF, 2 LR, 3 RR) in PHYSICS space (AC axes, CG origin): (+-TRACK/2, BASEY, axle z); AC +X = left.</summary>
        public AcVec3 WheelCentre(int i)
        {
            AcAxleSpec a = i < 2 ? Front : Rear;
            float x = (i & 1) == 0 ? a.Track * 0.5f : -a.Track * 0.5f;
            return new AcVec3(x, a.BaseY, a.AxleZ);
        }

        /// <summary>Loads the spec of an installed car (opens data.acd or the data/ folder). Returns null when the car has no data (DLC stub).
        /// Never throws: problems go to <see cref="Problems"/>.</summary>
        public static AcCarSpec Load(AcCarEntry car)
        {
            if (car == null || !car.Owned) return null;
            AcdArchive acd = null;
            try { acd = AcdArchive.Open(car.Dir); }
            catch (Exception e)
            {
                var bad = new AcCarSpec { CarId = car.Id };
                bad.Problems.Add("data.acd could not be opened: " + e.Message);
                return bad;
            }
            if (acd == null) return null;
            return FromArchive(acd, car.Id);
        }

        /// <summary>Parses a spec from an opened archive (harness / tests). Never throws.</summary>
        public static AcCarSpec FromArchive(AcdArchive acd, string carId)
        {
            var s = new AcCarSpec { CarId = carId ?? "" };
            if (acd == null) { s.Problems.Add("no archive"); return s; }
            try { s.Parse(acd); }
            catch (Exception e) { s.Problems.Add("parse failed: " + e.GetType().Name + " " + e.Message); }
            return s;
        }

        // ------------------------------------------------------------------ parsing

        private AcIni Ini(AcdArchive acd, string name, bool required)
        {
            AcIni ini = acd.ReadIni(name);
            if (ini == null)
            {
                if (required) Problems.Add(name + " missing");
                ini = new AcIni { Source = name };
            }
            Inis[name] = ini;
            return ini;
        }

        private AcLut Lut(AcdArchive acd, string nameOrInline, string what)
        {
            if (string.IsNullOrEmpty(nameOrInline)) return null;
            AcLut l = null;
            try { l = acd.ReadLut(nameOrInline); } catch (Exception) { l = null; }
            if (l == null || l.IsEmpty) { Problems.Add(what + ": lut '" + nameOrInline + "' missing or empty"); return null; }
            return l;
        }

        private static AcVec3 Vec(AcIniSection sec, string key, AcVec3 def)
        {
            if (sec == null) return def;
            float[] f = sec.GetFloats(key);
            if (f == null || f.Length < 3) return def;
            return new AcVec3(f[0], f[1], f[2]);
        }

        private float Req(AcIniSection sec, string file, string key, float def)
        {
            if (sec == null || !sec.Has(key)) { Problems.Add(file + " [" + (sec != null ? sec.Name : "?") + "] " + key + " missing (using " + def + ")"); return def; }
            return sec.GetFloat(key, def);
        }

        private void Parse(AcdArchive acd)
        {
            ParseCar(acd);
            ParseEngine(acd);
            ParseAi(acd);
            ParseDrivetrain(acd);
            ParseSuspensions(acd);
            ParseTyres(acd);
            ParseAero(acd);
            ParseBrakesAndElectronics(acd);
            ParseControllers(acd);
            ParseColliders(acd);
            ParseSetup(acd);
            ParseDamage();
        }

        private void ParseCar(AcdArchive acd)
        {
            AcIni car = Ini(acd, "car.ini", true);
            ScreenName = car.Get("INFO", "SCREEN_NAME", CarId) ?? CarId;
            AcIniSection b = car["BASIC"];
            TotalMassKg = Req(b, "car.ini", "TOTALMASS", TotalMassKg);
            float[] inertia = car.GetFloats("BASIC", "INERTIA");
            if (inertia != null && inertia.Length >= 3) InertiaBox = new[] { inertia[0], inertia[1], inertia[2] };
            else Problems.Add("car.ini [BASIC] INERTIA missing");
            GraphicsOffset = Vec(b, "GRAPHICS_OFFSET", new AcVec3(0, -0.4f, 0)); // ours default until parsed
            if (b == null || !b.Has("GRAPHICS_OFFSET")) Problems.Add("car.ini [BASIC] GRAPHICS_OFFSET missing");
            GraphicsPitchDeg = car.GetFloat("BASIC", "GRAPHICS_PITCH_ROTATION", 0f);
            DriverEyes = Vec(car["GRAPHICS"], "DRIVEREYES", new AcVec3(0.35f, 1.0f, -0.2f)); // ours default until parsed
            AcIniSection c = car["CONTROLS"];
            SteerLockDeg = Req(c, "car.ini", "STEER_LOCK", SteerLockDeg);
            SteerRatio = Req(c, "car.ini", "STEER_RATIO", SteerRatio);
            // 16 of the 86 cars write a NEGATIVE ratio (ks_audi_a1s1 -14.8): the sign is the steering rod's side in AC's geometric
            // suspension (LINEAR_STEER_ROD_RATIO), not a direction for the driver - the magnitude is the ratio
            if (SteerRatio < 0f) SteerRatio = -SteerRatio;
            if (SteerRatio < 1f) { Problems.Add("car.ini STEER_RATIO " + SteerRatio + " < 1 (using 14)"); SteerRatio = 14f; } // ours
            LinearSteerRodRatio = car.GetFloat("CONTROLS", "LINEAR_STEER_ROD_RATIO", 0f);
            FfMult = car.GetFloat("CONTROLS", "FFMULT", 1f);
            AcIniSection f = car["FUEL"];
            FuelLitres = Req(f, "car.ini", "FUEL", FuelLitres);
            MaxFuelLitres = Req(f, "car.ini", "MAX_FUEL", MaxFuelLitres);
            FuelConsumption = car.GetFloat("FUEL", "CONSUMPTION", 0f);
            FuelTankPosition = Vec(car["FUELTANK"], "POSITION", new AcVec3(0, 0, 0));
            PickupFrontHeight = car.GetFloat("RIDE", "PICKUP_FRONT_HEIGHT", PickupFrontHeight);
            PickupRearHeight = car.GetFloat("RIDE", "PICKUP_REAR_HEIGHT", PickupRearHeight);
        }

        private void ParseEngine(AcdArchive acd)
        {
            AcIni e = Ini(acd, "engine.ini", true);
            var en = Engine;
            AcIniSection d = e["ENGINE_DATA"];
            en.Inertia = Req(d, "engine.ini", "INERTIA", en.Inertia);
            en.Limiter = Req(d, "engine.ini", "LIMITER", en.Limiter);
            en.LimiterHz = d != null ? d.GetFloat("LIMITER_HZ", en.LimiterHz) : en.LimiterHz;
            en.Minimum = Req(d, "engine.ini", "MINIMUM", en.Minimum);
            en.AltitudeSensitivity = d != null ? d.GetFloat("ALTITUDE_SENSITIVITY", 0f) : 0f;
            string pc = e.Get("HEADER", "POWER_CURVE", "power.lut");
            en.Power = Lut(acd, pc, "engine.ini POWER_CURVE");
            if (en.Power == null || en.Power.IsEmpty)
            {
                // no invented engine: without the car's own power curve the engine makes no torque (it idles, the car stays where it is parked)
                Problems.Add("engine.ini POWER_CURVE '" + pc + "' missing or empty - the engine makes no torque");
                en.Power = new AcLut(new float[] { 0f, 20000f }, new float[] { 0f, 0f });
            }
            string coast = (e.Get("HEADER", "COAST_CURVE", "FROM_COAST_REF") ?? "FROM_COAST_REF").Trim();
            AcIniSection cr = e["COAST_REF"];
            if (cr != null)
            {
                en.CoastRpm = cr.GetFloat("RPM", en.CoastRpm);
                en.CoastTorque = cr.GetFloat("TORQUE", en.CoastTorque);
                en.CoastNonLinearity = cr.GetFloat("NON_LINEARITY", 0f);
            }
            if (!coast.Equals("FROM_COAST_REF", StringComparison.OrdinalIgnoreCase))
            {
                if (coast.Equals("FROM_COAST_DATA", StringComparison.OrdinalIgnoreCase)) Problems.Add("engine.ini COAST_CURVE=FROM_COAST_DATA (obsolete) - using COAST_REF");
                else if (cr == null) en.CoastLut = Lut(acd, e.Get("COAST_CURVE", "FILENAME", coast), "engine.ini coast curve");
            }
            else if (cr == null) Problems.Add("engine.ini [COAST_REF] missing (using " + en.CoastTorque + " Nm at " + en.CoastRpm + ")");
            en.ThrottleLut = acd.Has("throttle.lut") ? Lut(acd, "throttle.lut", "throttle.lut") : null;
            en.DamageRpmThreshold = e.GetFloat("DAMAGE", "RPM_THRESHOLD", 0f);
            foreach (AcIniSection t in e.Numbered("TURBO_"))
            {
                en.Turbos.Add(new AcTurboSpec
                {
                    LagDn = t.GetFloat("LAG_DN", 0.99f),
                    LagUp = t.GetFloat("LAG_UP", 0.99f),
                    MaxBoost = t.GetFloat("MAX_BOOST", 0f),
                    Wastegate = t.GetFloat("WASTEGATE", 0f),
                    DisplayMaxBoost = t.GetFloat("DISPLAY_MAX_BOOST", 0f),
                    ReferenceRpm = Math.Max(100f, t.GetFloat("REFERENCE_RPM", 3000f)),
                    Gamma = t.GetFloat("GAMMA", 1f),
                    CockpitAdjustable = t.GetBool("COCKPIT_ADJUSTABLE", false),
                    // P0 fix (iteration 2, DAMAGE_DESIGN 0): AC's files carry DEFAULT_TURBO_ADJUSTMENT in [ENGINE_DATA] (bmw_z4_drift engine.ini);
                    // read it there first, the [TURBO_n] section only as a fallback (was: [TURBO_n] only = always 1 = full wastegate)
                    DefaultAdjustment = d != null && d.Has("DEFAULT_TURBO_ADJUSTMENT") ? d.GetFloat("DEFAULT_TURBO_ADJUSTMENT", 1f)
                                      : t.Has("DEFAULT_TURBO_ADJUSTMENT") ? t.GetFloat("DEFAULT_TURBO_ADJUSTMENT", 1f) : 1f
                });
            }
            IdleRpm = en.Minimum;
            LimiterRpm = en.Limiter > 0f ? en.Limiter : Math.Max(1000f, en.Power.MaxX); // no limiter: the curve's end
            TorqueCurve = en.Power;
        }

        private static AcDiffSpec Diff(AcIniSection s, string prefix)
        {
            if (s == null) return new AcDiffSpec();
            return new AcDiffSpec { Power = s.GetFloat(prefix + "POWER", 0f), Coast = s.GetFloat(prefix + "COAST", 0f), Preload = s.GetFloat(prefix + "PRELOAD", 0f) };
        }

        private static AcClutchProfile Profile(AcIni ini, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase)) return new AcClutchProfile();
            AcIniSection s = ini[name.Trim()];
            if (s == null) return new AcClutchProfile();
            return new AcClutchProfile { Present = true, P0 = s.GetFloat("POINT_0", 0f), P1 = s.GetFloat("POINT_1", 0f), P2 = s.GetFloat("POINT_2", 0f) };
        }

        private void ParseDrivetrain(AcdArchive acd)
        {
            AcIni d = Ini(acd, "drivetrain.ini", true);
            var dt = Drivetrain;
            string type = (d.Get("TRACTION", "TYPE", "RWD") ?? "RWD").Trim();
            int sp = type.IndexOfAny(new[] { ' ', '\t' });
            if (sp > 0) type = type.Substring(0, sp);
            Traction = type.ToUpperInvariant();
            switch (Traction)
            {
                case "FWD": dt.Traction = AcTraction.FWD; break;
                case "AWD": dt.Traction = AcTraction.AWD; break;
                case "AWD2": dt.Traction = AcTraction.AWD2; break;
                case "RWD": dt.Traction = AcTraction.RWD; break;
                default: Problems.Add("drivetrain.ini TYPE '" + type + "' unknown (RWD)"); dt.Traction = AcTraction.RWD; Traction = "RWD"; break;
            }
            AcIniSection g = d["GEARS"];
            int count = g != null ? g.GetInt("COUNT", 0) : 0;
            var gears = new List<float>();
            for (int i = 1; i <= Math.Max(count, 12); i++)
            {
                if (g == null || !g.Has("GEAR_" + i)) { if (i <= count) Problems.Add("drivetrain.ini GEAR_" + i + " missing"); if (i > count) break; continue; }
                gears.Add(g.GetFloat("GEAR_" + i, 1f));
            }
            if (gears.Count == 0) { Problems.Add("drivetrain.ini has no forward gears (one 1:1 gear)"); gears.Add(1f); }
            dt.Gears = gears.ToArray();
            dt.Reverse = g != null ? g.GetFloat("GEAR_R", -3f) : -3f;
            if (dt.Reverse > 0f) dt.Reverse = -dt.Reverse;
            dt.Final = Req(g, "drivetrain.ini", "FINAL", dt.Final);
            dt.Diff = Diff(d["DIFFERENTIAL"], "");
            AcIniSection awd = d["AWD"];
            if (awd != null)
            {
                dt.AwdFrontSharePct = awd.GetFloat("FRONT_SHARE", 50f);
                dt.FrontDiff = Diff(awd, "FRONT_DIFF_");
                dt.CentreDiff = Diff(awd, "CENTRE_DIFF_");
                dt.RearDiff = Diff(awd, "REAR_DIFF_");
            }
            else if (dt.Traction == AcTraction.AWD || dt.Traction == AcTraction.AWD2) Problems.Add("drivetrain.ini [AWD] missing for " + Traction);
            AcIniSection awd2 = d["AWD2"];
            if (awd2 != null && dt.Traction == AcTraction.AWD2)
            {
                dt.FrontDiff = Diff(awd2, "FRONT_DIFF_");
                dt.RearDiff = Diff(awd2, "REAR_DIFF_");
                dt.CentreRampTorque = awd2.GetFloat("CENTRE_RAMP_TORQUE", 0f);
                dt.CentreMaxTorque = awd2.GetFloat("CENTRE_MAX_TORQUE", 0f);
            }
            AcIniSection gb = d["GEARBOX"];
            dt.ChangeUpMs = Req(gb, "drivetrain.ini", "CHANGE_UP_TIME", dt.ChangeUpMs);
            dt.ChangeDnMs = Req(gb, "drivetrain.ini", "CHANGE_DN_TIME", dt.ChangeDnMs);
            if (gb != null)
            {
                dt.AutoCutoffMs = gb.GetFloat("AUTO_CUTOFF_TIME", 0f);
                dt.SupportsShifter = gb.GetBool("SUPPORTS_SHIFTER", false);
                dt.ValidShiftRpmWindow = gb.GetFloat("VALID_SHIFT_RPM_WINDOW", dt.ValidShiftRpmWindow);
                dt.ControlsWindowGain = gb.GetFloat("CONTROLS_WINDOW_GAIN", dt.ControlsWindowGain);
                dt.GearboxInertia = gb.GetFloat("INERTIA", 0.02f);
            }
            if (dt.GearboxInertia <= 0f) dt.GearboxInertia = 0.02f;
            dt.ClutchMaxTorque = Req(d["CLUTCH"], "drivetrain.ini", "MAX_TORQUE", dt.ClutchMaxTorque);
            AcIniSection ac = d["AUTOCLUTCH"];
            if (ac != null)
            {
                dt.UpshiftProfile = Profile(d, ac.Get("UPSHIFT_PROFILE", "NONE"));
                dt.DownshiftProfile = Profile(d, ac.Get("DOWNSHIFT_PROFILE", "NONE"));
                dt.AutoClutchOnChanges = ac.GetBool("USE_ON_CHANGES", false);
                dt.AutoClutchForcedOn = ac.GetBool("FORCED_ON", false);
                dt.AutoClutchMinRpm = ac.GetFloat("MIN_RPM", dt.AutoClutchMinRpm);
                dt.AutoClutchMaxRpm = ac.GetFloat("MAX_RPM", dt.AutoClutchMaxRpm);
            }
            else Problems.Add("drivetrain.ini [AUTOCLUTCH] missing (ours " + dt.AutoClutchMinRpm + "-" + dt.AutoClutchMaxRpm + " rpm)");
            if (dt.AutoClutchMaxRpm <= dt.AutoClutchMinRpm) dt.AutoClutchMaxRpm = dt.AutoClutchMinRpm + 500f; // ours guard
            AcIniSection bl = d["AUTOBLIP"];
            if (bl != null)
            {
                dt.AutoBlipElectronic = bl.GetBool("ELECTRONIC", false);
                dt.BlipP0 = bl.GetFloat("POINT_0", dt.BlipP0);
                dt.BlipP1 = bl.GetFloat("POINT_1", dt.BlipP1);
                dt.BlipP2 = bl.GetFloat("POINT_2", dt.BlipP2);
                dt.BlipLevel = bl.GetFloat("LEVEL", dt.BlipLevel);
            }
            AcIniSection ash = d["AUTO_SHIFTER"];
            dt.AutoShiftUp = ash != null ? ash.GetFloat("UP", 0f) : 0f;
            dt.AutoShiftDown = ash != null ? ash.GetFloat("DOWN", 0f) : 0f;
            if (ash != null)
            {
                dt.AutoShiftSlipThreshold = ash.GetFloat("SLIP_THRESHOLD", dt.AutoShiftSlipThreshold);
                dt.AutoShiftGasCutoffS = ash.GetFloat("GAS_CUTOFF_TIME", dt.AutoShiftGasCutoffS);
            }
            if (dt.AutoShiftUp <= 0f && AiShiftUp > 0f) { dt.AutoShiftUp = AiShiftUp; if (dt.AutoShiftDown <= 0f) dt.AutoShiftDown = AiShiftDown; } // the car's own ai.ini [GEARS]
            if (dt.AutoShiftUp <= 0f) { dt.AutoShiftUp = LimiterRpm * 0.95f; Problems.Add("drivetrain.ini [AUTO_SHIFTER] UP missing (ours 95 % of the limiter)"); } // ours
            if (dt.AutoShiftDown <= 0f) dt.AutoShiftDown = dt.AutoShiftUp * 0.55f; // ours
            GearRatios = dt.Gears;
            ReverseRatio = dt.Reverse;
            FinalRatio = dt.Final;
        }

        private void ParseAxle(AcIni s, string name, AcAxleSpec a)
        {
            AcIniSection x = s[name];
            if (x == null) { Problems.Add("suspensions.ini [" + name + "] missing"); return; }
            a.Type = (x.Get("TYPE", "DWB") ?? "DWB").Trim();
            a.BaseY = Req(x, "suspensions.ini", "BASEY", a.BaseY);
            a.Track = Req(x, "suspensions.ini", "TRACK", a.Track);
            a.RodLength = x.GetFloat("ROD_LENGTH", 0f);
            a.HubMass = x.GetFloat("HUB_MASS", a.HubMass);
            a.StaticCamberDeg = x.GetFloat("STATIC_CAMBER", 0f);
            a.ToeOut = x.GetFloat("TOE_OUT", 0f);
            float[] steer = x.GetFloats("WBTYRE_STEER");
            a.SteerArmZ = steer != null && steer.Length >= 3 ? steer[2] : 0f;
            a.SpringRate = Req(x, "suspensions.ini", "SPRING_RATE", a.SpringRate);
            a.ProgressiveRate = x.GetFloat("PROGRESSIVE_SPRING_RATE", 0f);
            a.BumpStopRate = x.GetFloat("BUMP_STOP_RATE", a.BumpStopRate);
            a.BumpStopUp = x.GetFloat("BUMPSTOP_UP", a.BumpStopUp);
            a.BumpStopDn = x.GetFloat("BUMPSTOP_DN", a.BumpStopDn);
            a.PackerRange = x.GetFloat("PACKER_RANGE", 0f);
            a.DampBump = Req(x, "suspensions.ini", "DAMP_BUMP", a.DampBump);
            a.DampFastBump = x.GetFloat("DAMP_FAST_BUMP", a.DampBump);
            a.DampFastBumpThreshold = x.GetFloat("DAMP_FAST_BUMPTHRESHOLD", 0f);
            a.DampRebound = Req(x, "suspensions.ini", "DAMP_REBOUND", a.DampRebound);
            a.DampFastRebound = x.GetFloat("DAMP_FAST_REBOUND", a.DampRebound);
            a.DampFastReboundThreshold = x.GetFloat("DAMP_FAST_REBOUNDTHRESHOLD", 0f);
        }

        private void ParseSuspensions(AcdArchive acd)
        {
            AcIni s = Ini(acd, "suspensions.ini", true);
            AcIniSection b = s["BASIC"];
            WheelbaseM = Req(b, "suspensions.ini", "WHEELBASE", WheelbaseM);
            CgLocation = Req(b, "suspensions.ini", "CG_LOCATION", CgLocation);
            if (CgLocation <= 0f || CgLocation >= 1f) { Problems.Add("suspensions.ini CG_LOCATION " + CgLocation + " out of 0..1 (0.5)"); CgLocation = 0.5f; } // ours
            ParseAxle(s, "FRONT", Front);
            ParseAxle(s, "REAR", Rear);
            Front.ArbRate = s.GetFloat("ARB", "FRONT", 0f);
            Rear.ArbRate = s.GetFloat("ARB", "REAR", 0f);
            Front.AxleZ = WheelbaseM * (1f - CgLocation);
            Rear.AxleZ = -WheelbaseM * CgLocation;
            AcIniSection go = s["GRAPHICS_OFFSETS"];
            if (go != null)
            {
                Front.GraphicsOffsetL = go.GetFloat("WHEEL_LF", 0f); Front.GraphicsOffsetR = go.GetFloat("WHEEL_RF", 0f);
                Rear.GraphicsOffsetL = go.GetFloat("WHEEL_LR", 0f); Rear.GraphicsOffsetR = go.GetFloat("WHEEL_RR", 0f);
            }
            TrackFrontM = Front.Track;
            TrackRearM = Rear.Track;
            ParseHeave(s, "HEAVE_FRONT", Front);
            ParseHeave(s, "HEAVE_REAR", Rear);
        }


        private static void ParseHeave(AcIni s, string name, AcAxleSpec a)
        {
            AcIniSection x = s[name];
            if (x == null) return;
            a.HasHeave = true;
            a.HeaveRate = x.GetFloat("SPRING_RATE", 0f);
            a.HeaveRodLength = x.GetFloat("ROD_LENGTH", 0f);
            a.HeaveProgressive = x.GetFloat("PROGRESSIVE_SPRING_RATE", 0f);
            a.HeaveBumpStopRate = x.GetFloat("BUMP_STOP_RATE", 0f);
            a.HeaveBumpStopUp = x.GetFloat("BUMPSTOP_UP", 0f);
            a.HeaveBumpStopDn = x.GetFloat("BUMPSTOP_DN", 0f);
            a.HeavePackerRange = x.GetFloat("PACKER_RANGE", 0f);
            a.HeaveDampBump = x.GetFloat("DAMP_BUMP", 0f);
            a.HeaveDampFastBump = x.GetFloat("DAMP_FAST_BUMP", a.HeaveDampBump);
            a.HeaveDampFastBumpThreshold = x.GetFloat("DAMP_FAST_BUMPTHRESHOLD", 0f);
            a.HeaveDampRebound = x.GetFloat("DAMP_REBOUND", 0f);
            a.HeaveDampFastRebound = x.GetFloat("DAMP_FAST_REBOUND", a.HeaveDampRebound);
            a.HeaveDampFastReboundThreshold = x.GetFloat("DAMP_FAST_REBOUNDTHRESHOLD", 0f);
        }

        private void ParseTyre(AcdArchive acd, AcIni t, AcIniSection x, string thermalName, AcTyreSpec o)
        {
            string file = "tyres.ini";
            o.Name = (x.Get("NAME", "") ?? "").Trim();
            o.ShortName = (x.Get("SHORT_NAME", "") ?? "").Trim();
            o.Width = x.GetFloat("WIDTH", o.Width);
            o.Radius = Req(x, file, "RADIUS", o.Radius);
            o.RimRadius = x.GetFloat("RIM_RADIUS", o.RimRadius);
            o.AngularInertia = Req(x, file, "ANGULAR_INERTIA", o.AngularInertia);
            o.Damp = x.GetFloat("DAMP", o.Damp);
            o.Rate = x.GetFloat("RATE", o.Rate);
            o.Dx0 = x.GetFloat("DX0", 0f); o.Dy0 = x.GetFloat("DY0", 0f); o.Dx1 = x.GetFloat("DX1", 0f); o.Dy1 = x.GetFloat("DY1", 0f);
            if (x.Has("DX_REF")) o.DxRef = x.GetFloat("DX_REF", o.DxRef); else if (o.Dx0 > 0f) { o.DxRef = o.Dx0; Problems.Add(file + " [" + x.Name + "] DX_REF missing (DX0 used)"); } else Problems.Add(file + " [" + x.Name + "] DX_REF missing");
            if (x.Has("DY_REF")) o.DyRef = x.GetFloat("DY_REF", o.DyRef); else if (o.Dy0 > 0f) { o.DyRef = o.Dy0; Problems.Add(file + " [" + x.Name + "] DY_REF missing (DY0 used)"); } else Problems.Add(file + " [" + x.Name + "] DY_REF missing");
            o.Fz0 = Req(x, file, "FZ0", o.Fz0);
            o.LsExpX = x.GetFloat("LS_EXPX", o.LsExpX);
            o.LsExpY = x.GetFloat("LS_EXPY", o.LsExpY);
            o.FrictionLimitAngle = Req(x, file, "FRICTION_LIMIT_ANGLE", o.FrictionLimitAngle);
            o.CxMult = x.GetFloat("CX_MULT", 1f);
            o.FalloffLevel = x.GetFloat("FALLOFF_LEVEL", o.FalloffLevel);
            o.FalloffSpeed = x.GetFloat("FALLOFF_SPEED", o.FalloffSpeed);
            o.SpeedSensitivity = x.GetFloat("SPEED_SENSITIVITY", 0f);
            o.RelaxationLength = x.GetFloat("RELAXATION_LENGTH", o.RelaxationLength);
            o.RollingResistance0 = x.GetFloat("ROLLING_RESISTANCE_0", 0f);
            o.RollingResistance1 = x.GetFloat("ROLLING_RESISTANCE_1", 0f);
            o.RollingResistanceSlip = x.GetFloat("ROLLING_RESISTANCE_SLIP", 0f);
            o.Flex = x.GetFloat("FLEX", 0f); o.FlexGain = x.GetFloat("FLEX_GAIN", 0f);
            o.CamberGain = x.GetFloat("CAMBER_GAIN", 0f);
            o.DCamber0 = x.GetFloat("DCAMBER_0", 0f); o.DCamber1 = x.GetFloat("DCAMBER_1", 0f);
            o.Xmu = x.GetFloat("XMU", 0f);
            o.PressureStatic = x.GetFloat("PRESSURE_STATIC", o.PressureStatic);
            o.PressureIdeal = x.GetFloat("PRESSURE_IDEAL", o.PressureStatic);
            o.PressureSpringGain = x.GetFloat("PRESSURE_SPRING_GAIN", 0f);
            o.PressureFlexGain = x.GetFloat("PRESSURE_FLEX_GAIN", 0f);
            o.PressureRrGain = x.GetFloat("PRESSURE_RR_GAIN", 0f);
            o.PressureDGain = x.GetFloat("PRESSURE_D_GAIN", 0f);
            o.RadiusAngularK = x.GetFloat("RADIUS_ANGULAR_K", 0f);
            o.BrakeDxMod = x.GetFloat("BRAKE_DX_MOD", 0f);
            string wear = x.Get("WEAR_CURVE", null);
            o.WearCurve = string.IsNullOrEmpty(wear) ? null : Lut(acd, wear, file + " [" + x.Name + "] WEAR_CURVE");
            AcIniSection th = t[thermalName];
            string perf = th != null ? th.Get("PERFORMANCE_CURVE", null) : null;
            o.PerformanceCurve = string.IsNullOrEmpty(perf) ? null : Lut(acd, perf, file + " [" + thermalName + "] PERFORMANCE_CURVE");
            o.PerformancePeak = o.PerformanceCurve != null && o.PerformanceCurve.MaxY > 0.1f ? Math.Min(1.2f, o.PerformanceCurve.MaxY) : 1f;
            if (o.Radius < 0.1f || o.Radius > 0.8f) Problems.Add(file + " [" + x.Name + "] RADIUS " + o.Radius + " implausible");
        }

        private void ParseTyres(AcdArchive acd)
        {
            AcIni t = Ini(acd, "tyres.ini", true);
            int version = t.GetInt("HEADER", "VERSION", 0);
            if (version < 10) Problems.Add("tyres.ini VERSION=" + version + " (model written for 10)");
            DefaultCompound = t.GetInt("COMPOUND_DEFAULT", "INDEX", 0);
            for (int i = 0; i < 16; i++)
            {
                string fn = i == 0 ? "FRONT" : "FRONT_" + i, rn = i == 0 ? "REAR" : "REAR_" + i;
                AcIniSection fs = t[fn], rs = t[rn];
                if (fs == null || rs == null) { if (i == 0) Problems.Add("tyres.ini [FRONT]/[REAR] missing"); break; }
                var c = new AcTyreCompound { Index = i };
                ParseTyre(acd, t, fs, i == 0 ? "THERMAL_FRONT" : "THERMAL_FRONT_" + i, c.Front);
                ParseTyre(acd, t, rs, i == 0 ? "THERMAL_REAR" : "THERMAL_REAR_" + i, c.Rear);
                c.Name = c.Front.Name.Length > 0 ? c.Front.Name : "Compound " + i;
                c.ShortName = c.Front.ShortName;
                Compounds.Add(c);
            }
            AcTyreCompound d = Compound(DefaultCompound);
            if (d == null) { Problems.Add("tyres.ini has no compound"); Compounds.Add(d = new AcTyreCompound()); }
            if (d.Index != DefaultCompound) Problems.Add("tyres.ini [COMPOUND_DEFAULT] INDEX " + DefaultCompound + " not found (compound " + d.Index + ")");
            DefaultCompound = d.Index;
            WheelRadius = new[] { d.Front.Radius, d.Front.Radius, d.Rear.Radius, d.Rear.Radius };
        }

        /// <summary>A ground-height lut: a file / inline table, or a plain number (lotus_exige_s writes LUT_GH_CD=1.00) = a constant.</summary>
        private AcLut GroundLut(AcdArchive acd, string v, string what)
        {
            if (string.IsNullOrEmpty(v)) return null;
            float c;
            if (v.IndexOf('.') >= 0 && !v.EndsWith(".lut", StringComparison.OrdinalIgnoreCase) && AcText.TryFloat(v, out c) && !acd.Has(v.Trim()))
                return new AcLut(new[] { 0f }, new[] { c });
            return Lut(acd, v, what);
        }

        private void ParseAero(AcdArchive acd)
        {
            AcIni a = Ini(acd, "aero.ini", true);
            foreach (AcIniSection w in a.WithPrefix("WING_"))
            {
                int idx;
                if (!int.TryParse(w.Name.Substring(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out idx)) continue;
                var spec = new AcWingSpec
                {
                    Index = idx,
                    Name = (w.Get("NAME", "") ?? "").Trim(),
                    Chord = w.GetFloat("CHORD", 1f),
                    Span = w.GetFloat("SPAN", 1f),
                    Position = Vec(w, "POSITION", new AcVec3(0, 0, 0)),
                    ClGain = w.GetFloat("CL_GAIN", 1f),
                    CdGain = w.GetFloat("CD_GAIN", 1f),
                    Angle = w.GetFloat("ANGLE", 0f),
                    YawClGain = w.GetFloat("YAW_CL_GAIN", 0f),
                    ZoneFrontCl = w.GetFloat("ZONE_FRONT_CL", 0f), ZoneFrontCd = w.GetFloat("ZONE_FRONT_CD", 0f),
                    ZoneRearCl = w.GetFloat("ZONE_REAR_CL", 0f), ZoneRearCd = w.GetFloat("ZONE_REAR_CD", 0f),
                    ZoneLeftCl = w.GetFloat("ZONE_LEFT_CL", 0f), ZoneLeftCd = w.GetFloat("ZONE_LEFT_CD", 0f),
                    ZoneRightCl = w.GetFloat("ZONE_RIGHT_CL", 0f), ZoneRightCd = w.GetFloat("ZONE_RIGHT_CD", 0f)
                };
                string what = "aero.ini [" + w.Name + "]";
                spec.AoaCl = Lut(acd, w.Get("LUT_AOA_CL", null), what + " LUT_AOA_CL");
                spec.AoaCd = Lut(acd, w.Get("LUT_AOA_CD", null), what + " LUT_AOA_CD");
                string gh = w.Get("LUT_GH_CL", null);
                spec.GhCl = GroundLut(acd, gh, what + " LUT_GH_CL");
                gh = w.Get("LUT_GH_CD", null);
                spec.GhCd = GroundLut(acd, gh, what + " LUT_GH_CD");
                AcControllerChain ctrl = AcControllerChain.Parse(a, "DYNAMIC_CONTROLLER_", idx, acd, Problems, "aero.ini");
                spec.Controller = ctrl.Count > 0 ? ctrl : null;
                Wings.Add(spec);
            }
            Wings.Sort((x, y) => x.Index.CompareTo(y.Index));
            if (Wings.Count == 0) Problems.Add("aero.ini has no [WING_n]");
        }

        /// <summary>Iteration 2: the damage keys from the inis already read (car.ini, engine.ini, suspensions.ini). Absent keys = effect off.</summary>
        private void ParseDamage()
        {
            AcIni car, eng, sus;
            Inis.TryGetValue("car.ini", out car); Inis.TryGetValue("engine.ini", out eng); Inis.TryGetValue("suspensions.ini", out sus);
            Damage = AcDamageSpec.From(car, eng, sus);
        }

        private static AcAidSpec Aid(AcdArchive acd, AcIniSection s, AcCarSpec spec, string what)
        {
            var a = new AcAidSpec();
            if (s == null) return a;
            a.Present = s.GetBool("PRESENT", false);
            a.Active = s.GetBool("ACTIVE", false);
            a.SlipRatioLimit = s.GetFloat("SLIP_RATIO_LIMIT", a.SlipRatioLimit);
            string curve = s.Get("CURVE", null);
            a.Curve = string.IsNullOrEmpty(curve) ? null : spec.Lut(acd, curve, what + " CURVE");
            a.RateHz = s.GetFloat("RATE_HZ", a.RateHz);
            a.MinSpeedKmh = s.GetFloat("MIN_SPEED_KMH", 0f);
            if (a.SlipRatioLimit <= 0f) a.SlipRatioLimit = 0.1f; // ours guard
            if (a.RateHz <= 0f) a.RateHz = 100f; // ours guard
            return a;
        }

        private void ParseBrakesAndElectronics(AcdArchive acd)
        {
            AcIni b = Ini(acd, "brakes.ini", true);
            AcIniSection d = b["DATA"];
            Brakes.MaxTorque = Req(d, "brakes.ini", "MAX_TORQUE", Brakes.MaxTorque);
            Brakes.FrontShare = Req(d, "brakes.ini", "FRONT_SHARE", Brakes.FrontShare);
            if (d != null)
            {
                Brakes.HandbrakeTorque = d.GetFloat("HANDBRAKE_TORQUE", 0f);
                Brakes.CockpitAdjustable = d.GetBool("COCKPIT_ADJUSTABLE", false);
                Brakes.AdjustStep = d.GetFloat("ADJUST_STEP", 0.5f);
            }
            AcIni e = Ini(acd, "electronics.ini", false);
            Abs = Aid(acd, e["ABS"], this, "electronics.ini [ABS]");
            Tc = Aid(acd, e["TRACTION_CONTROL"], this, "electronics.ini [TRACTION_CONTROL]");
            EdlPresent = e.GetBool("EDL", "PRESENT", false);
        }

        private AcControllerChain Ctrl(AcdArchive acd, string name)
        {
            if (!acd.Has(name)) return null;
            AcIni ini = Ini(acd, name, false);
            AcControllerChain c = AcControllerChain.Parse(ini, "CONTROLLER_", -1, acd, Problems, name);
            return c.Count > 0 ? c : null;
        }

        private void ParseControllers(AcdArchive acd)
        {
            CtrlSingleLock = Ctrl(acd, "ctrl_single_lock.ini");
            CtrlAwd2 = Ctrl(acd, "ctrl_awd2.ini");
            CtrlAwdCentreLock = Ctrl(acd, "ctrl_awd_center_lock.ini");
            CtrlEbb = Ctrl(acd, "ctrl_ebb.ini");
            for (int i = 0; i < acd.Names.Count; i++)
            {
                string n = acd.Names[i];
                if (!n.StartsWith("ctrl_", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.Equals("ctrl_single_lock.ini", StringComparison.OrdinalIgnoreCase) || n.Equals("ctrl_awd2.ini", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("ctrl_awd_center_lock.ini", StringComparison.OrdinalIgnoreCase) || n.Equals("ctrl_ebb.ini", StringComparison.OrdinalIgnoreCase)) continue;
                Problems.Add(n + " not simulated");
            }
        }

        private void ParseColliders(AcdArchive acd)
        {
            AcIni c = Ini(acd, "colliders.ini", false);
            foreach (AcIniSection s in c.WithPrefix("COLLIDER_"))
            {
                AcVec3 size = Vec(s, "SIZE", new AcVec3(0, 0, 0));
                if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f) continue;
                Colliders.Add(new AcColliderBox { Centre = Vec(s, "CENTRE", new AcVec3(0, 0, 0)), Size = size, GroundEnable = s.GetBool("GROUND_ENABLE", true) });
            }
        }

        private void ParseAi(AcdArchive acd)
        {
            AcIni a = Ini(acd, "ai.ini", false);
            AiShiftUp = a.GetFloat("GEARS", "UP", 0f);
            AiShiftDown = a.GetFloat("GEARS", "DOWN", 0f);
        }

        private void ParseSetup(AcdArchive acd)
        {
            AcIni s = Ini(acd, "setup.ini", false);
            AcTyreCompound d = Compound(DefaultCompound);
            for (int i = 0; i < s.Order.Count; i++)
            {
                AcIniSection x = s.Order[i];
                if (x.Name.Length == 0 || x.Name.Equals("DISPLAY_METHOD", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("GEARS", StringComparison.OrdinalIgnoreCase)) continue;
                if (x.Name.StartsWith("GEAR_SET_", StringComparison.OrdinalIgnoreCase))
                {
                    var set = new AcGearSet { Key = x.Name, Name = (x.Get("NAME", x.Name) ?? x.Name).Trim() };
                    var gl = new List<float>();
                    for (int g = 1; g <= 12 && x.Has("GEAR_" + g); g++) gl.Add(x.GetFloat("GEAR_" + g, 1f));
                    set.Gears = gl.ToArray();
                    GearSets.Add(set);
                    continue;
                }
                var item = new AcSetupItem
                {
                    Key = x.Name,
                    Tab = (x.Get("TAB", "") ?? "").Trim(),
                    Name = (x.Get("NAME", x.Name) ?? x.Name).Trim(),
                    Help = (x.Get("HELP", "") ?? "").Trim(),
                    Min = x.GetFloat("MIN", 0f),
                    Max = x.GetFloat("MAX", 0f),
                    Step = x.GetFloat("STEP", 1f),
                    ShowClicks = x.GetInt("SHOW_CLICKS", 0)
                };
                string rto = x.Get("RATIOS", null);
                if (!string.IsNullOrEmpty(rto))
                {
                    string text = acd.ReadText(rto.Trim());
                    if (text == null) Problems.Add("setup.ini [" + x.Name + "] RATIOS '" + rto + "' missing");
                    else item.Ratios = AcLut.ParseRto(text);
                }
                item.Default = SetupDefault(item.Key, d);
                Setup.Add(item);
            }
        }

        /// <summary>The car's own value of a setup item in setup.ini units (NaN when not known). Units per item as AC's setup screen shows
        /// them: psi, degrees, litres, percent, N/m (ARB), N s/m (dampers), mm (rod length / packers), wing angle steps.</summary>
        private float SetupDefault(string key, AcTyreCompound d)
        {
            string k = key.ToUpperInvariant();
            AcAxleSpec ax = k.EndsWith("F") ? Front : Rear;
            switch (k)
            {
                case "FUEL": return FuelLitres;
                case "BRAKE_POWER_MULT": return 100f;
                case "FRONT_BIAS": return Brakes.FrontShare * 100f;
                case "ENGINE_LIMITER": return 100f;
                case "ARB_FRONT": return Front.ArbRate;
                case "ARB_REAR": return Rear.ArbRate;
                case "DIFF_POWER": return Drivetrain.Diff.Power * 100f;
                case "DIFF_COAST": return Drivetrain.Diff.Coast * 100f;
                case "DIFF_PRELOAD": return Drivetrain.Diff.Preload;
                case "FINAL_GEAR_RATIO": return FinalRatio;
            }
            if (d != null && k.StartsWith("PRESSURE_")) return (k.EndsWith("F") ? d.Front : d.Rear).PressureStatic;
            if (k.StartsWith("CAMBER_")) return ax.StaticCamberDeg;
            if (k.StartsWith("SPRING_RATE_")) return ax.SpringRate;
            if (k.StartsWith("DAMP_FAST_BUMP_")) return ax.DampFastBump;
            if (k.StartsWith("DAMP_FAST_REBOUND_")) return ax.DampFastRebound;
            if (k.StartsWith("DAMP_BUMP_")) return ax.DampBump;
            if (k.StartsWith("DAMP_REBOUND_")) return ax.DampRebound;
            if (k.StartsWith("ROD_LENGTH_")) return ax.RodLength * 10000f; // ours reading: setup clicks of 0.1 mm (458_gt2 0..500 step 10 around its 0.015 m)
            if (k.StartsWith("TOE_OUT_")) return ax.ToeOut * 100000f;     // ours reading: 0.01 mm (458_gt2 -100..70 step 10 around its 0.0004 m)
            if (k.StartsWith("PACKER_RANGE_")) return ax.PackerRange * 1000f;
            if (k.StartsWith("BUMP_STOP_RATE_")) return ax.BumpStopRate;
            if (k.StartsWith("WING_"))
            {
                int w;
                if (int.TryParse(k.Substring(5), out w)) for (int i = 0; i < Wings.Count; i++) if (Wings[i].Index == w) return Wings[i].Angle;
            }
            if (k.StartsWith("GEAR_"))
            {
                int g;
                if (int.TryParse(k.Substring(5), out g) && g >= 1 && g <= GearRatios.Length) return GearRatios[g - 1];
            }
            return float.NaN;
        }

        /// <summary>One line for the Player.log ("Physics: &lt;car&gt; spec: ...").</summary>
        public string Summary()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            AcTyreCompound c = Compound(DefaultCompound);
            var sb = new System.Text.StringBuilder(256);
            sb.Append("mass ").Append(TotalMassKg.ToString("0", inv)).Append(" kg + ").Append(FuelLitres.ToString("0", inv)).Append(" l fuel, inertia box ")
              .Append(InertiaBox[0].ToString("0.##", inv)).Append('x').Append(InertiaBox[1].ToString("0.##", inv)).Append('x').Append(InertiaBox[2].ToString("0.##", inv))
              .Append(", CG height ").Append(CgHeightM.ToString("0.000", inv)).Append(" m, ").Append(Traction).Append(", ").Append(GearCount).Append(" gears (");
            for (int i = 0; i < GearRatios.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(GearRatios[i].ToString("0.###", inv)); }
            sb.Append("), final ").Append(FinalRatio.ToString("0.###", inv)).Append(", limiter ").Append(LimiterRpm.ToString("0", inv))
              .Append(", steer lock ").Append(SteerLockDeg.ToString("0", inv)).Append("/ratio ").Append(SteerRatio.ToString("0.##", inv));
            if (c != null)
                sb.Append(", tyre ").Append(c.Name).Append(" DX/DY ").Append(c.Front.DxRef.ToString("0.###", inv)).Append('/').Append(c.Front.DyRef.ToString("0.###", inv))
                  .Append(" R ").Append(c.Front.Radius.ToString("0.###", inv)).Append('/').Append(c.Rear.Radius.ToString("0.###", inv));
            sb.Append(", turbos ").Append(Engine.Turbos.Count).Append(", aero wings ").Append(Wings.Count).Append(", problems ").Append(Problems.Count);
            return sb.ToString();
        }

        public override string ToString()
        {
            return CarId + ": " + TotalMassKg + " kg, limiter " + LimiterRpm + " rpm, " + GearCount + " gears, steer lock " + SteerLockDeg + " / ratio " + SteerRatio;
        }
    }
}
