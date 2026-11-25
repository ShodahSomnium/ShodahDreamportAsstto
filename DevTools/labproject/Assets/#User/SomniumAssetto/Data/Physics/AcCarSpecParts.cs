using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    // [P, Physics] The parts of AcCarSpec, one class per data.acd ini (Unity-FREE, compiled by the harness). Field names follow the ini keys;
    // units are the ini's (SI unless noted). Defaults used when a key is missing are marked // ours (and the loader logs a Problem for
    // keys a car must have). Semantics: research/ac_physics_sound_rules.md 1.2-1.8 + the SDK formula_k comments.

    /// <summary>Drive layout (drivetrain.ini [TRACTION] TYPE). Census of the 86 owned cars: RWD 69, FWD 5, AWD 5, AWD2 7.</summary>
    public enum AcTraction { RWD = 0, FWD = 1, AWD = 2, AWD2 = 3 }

    /// <summary>engine.ini [TURBO_n].</summary>
    public sealed class AcTurboSpec
    {
        /// <summary>Per-step (AC 333 Hz) interpolation factors spinning down / up (boost += (target - boost) * (1 - LAG) each step).</summary>
        public float LagDn = 0.99f, LagUp = 0.99f; // ours defaults until parsed
        /// <summary>Boost is never above this; torque = torque * (1 + boost) [READ formula_k].</summary>
        public float MaxBoost;
        /// <summary>Boost cap of the wastegate (0 = none).</summary>
        public float Wastegate;
        public float DisplayMaxBoost;
        /// <summary>Rpm where full boost is reached at full gas.</summary>
        public float ReferenceRpm = 3000f; // ours default until parsed
        /// <summary>Boost sensitivity to the pedal (target ~ gas^GAMMA).</summary>
        public float Gamma = 1f; // ours default until parsed
        public bool CockpitAdjustable;
        /// <summary>DEFAULT_TURBO_ADJUSTMENT (0..1 of the wastegate) when the turbo is cockpit adjustable; 1 when absent.</summary>
        public float DefaultAdjustment = 1f; // ours default (no adjustment)
    }

    /// <summary>engine.ini.</summary>
    public sealed class AcEngineSpec
    {
        /// <summary>[ENGINE_DATA] INERTIA (kg m^2).</summary>
        public float Inertia = 0.12f; // ours default until parsed
        /// <summary>[ENGINE_DATA] LIMITER (rpm, 0 = none), LIMITER_HZ (cut frequency), MINIMUM (idle rpm).</summary>
        public float Limiter = 7000f, LimiterHz = 40f, Minimum = 900f; // ours defaults until parsed
        public float AltitudeSensitivity;
        /// <summary>power.lut: rpm -> torque Nm at full throttle (AC's numbers are "at the wheels" already [BELIEF]; applied as is).</summary>
        public AcLut Power = new AcLut();
        /// <summary>[COAST_REF] RPM, TORQUE (engine-brake Nm at RPM), NON_LINEARITY (0 linear .. 1 fully exponential).</summary>
        public float CoastRpm = 6000f, CoastTorque = 50f, CoastNonLinearity; // ours defaults until parsed
        /// <summary>COAST_CURVE=FROM_COAST_REF (the only form on the 86 cars) or a lut file (then <see cref="CoastLut"/>).</summary>
        public AcLut CoastLut;
        /// <summary>throttle.lut (pedal % -> throttle %), present on most cars; null = linear.</summary>
        public AcLut ThrottleLut;
        public readonly List<AcTurboSpec> Turbos = new List<AcTurboSpec>();
        /// <summary>[DAMAGE] RPM_THRESHOLD (over-rev damage starts) - informative (no engine damage in iteration 1).</summary>
        public float DamageRpmThreshold;

        /// <summary>Full-throttle torque (Nm) at rpm without boost.</summary>
        public float TorqueAt(float rpm) { return Power.Eval(rpm); }

        /// <summary>Engine-brake torque (Nm, negative) at rpm, gas off (COAST_REF: linear (0) .. exponential (1) from 0 to TORQUE at RPM).</summary>
        public float CoastAt(float rpm)
        {
            if (rpm <= 0f) return 0f;
            if (CoastLut != null && !CoastLut.IsEmpty) return -Math.Abs(CoastLut.Eval(rpm));
            float r = CoastRpm > 1f ? rpm / CoastRpm : 0f;
            float shaped = (float)Math.Pow(r, 1f + Math.Max(0f, Math.Min(1f, CoastNonLinearity))); // 0 -> r, 1 -> r^2 // ours shape for "exponential"
            return -CoastTorque * shaped;
        }
    }

    /// <summary>One differential's lock (drivetrain.ini [DIFFERENTIAL] or the FRONT_/CENTRE_/REAR_DIFF_* keys of [AWD]/[AWD2]).</summary>
    public struct AcDiffSpec
    {
        /// <summary>Lock under power / coast (1.0 = 100 %), preload torque (Nm).</summary>
        public float Power, Coast, Preload;
        public override string ToString() { return "P" + Power + "/C" + Coast + "/pre" + Preload; }
    }

    /// <summary>drivetrain.ini [AUTOCLUTCH] profile (ms): clutch fully pressed at P0, release starts at P1, fully released at P2.</summary>
    public struct AcClutchProfile
    {
        public bool Present;
        public float P0, P1, P2;
    }

    /// <summary>drivetrain.ini.</summary>
    public sealed class AcDrivetrainSpec
    {
        public AcTraction Traction = AcTraction.RWD;
        /// <summary>[GEARS] forward ratios (GEAR_1..COUNT), GEAR_R (negative), FINAL.</summary>
        public float[] Gears = new float[0];
        public float Reverse = -3f, Final = 4f; // ours defaults until parsed
        /// <summary>[DIFFERENTIAL] (2WD cars; the driven axle).</summary>
        public AcDiffSpec Diff;
        /// <summary>[AWD] FRONT_SHARE (percent of torque to the front, as written), front / centre / rear diffs.</summary>
        public float AwdFrontSharePct = 50f; // ours default until parsed
        public AcDiffSpec FrontDiff, CentreDiff, RearDiff;
        /// <summary>[AWD2] CENTRE_RAMP_TORQUE, CENTRE_MAX_TORQUE (viscous / haldex-like centre coupling; driven by ctrl_awd2.ini when present).</summary>
        public float CentreRampTorque, CentreMaxTorque;
        /// <summary>[GEARBOX] CHANGE_UP_TIME / CHANGE_DN_TIME / AUTO_CUTOFF_TIME (ms), SUPPORTS_SHIFTER, VALID_SHIFT_RPM_WINDOW, CONTROLS_WINDOW_GAIN, INERTIA.</summary>
        public float ChangeUpMs = 150f, ChangeDnMs = 200f, AutoCutoffMs; // ours defaults until parsed
        public bool SupportsShifter;
        public float ValidShiftRpmWindow = 800f, ControlsWindowGain = 0.4f; // ours defaults until parsed
        public float GearboxInertia = 0.02f; // formula_k comment: "default values to 0.02 if not set"
        /// <summary>[CLUTCH] MAX_TORQUE (Nm).</summary>
        public float ClutchMaxTorque = 500f; // ours default until parsed
        /// <summary>[AUTOCLUTCH].</summary>
        public AcClutchProfile UpshiftProfile, DownshiftProfile;
        public bool AutoClutchOnChanges, AutoClutchForcedOn;
        public float AutoClutchMinRpm = 1500f, AutoClutchMaxRpm = 2500f; // ours defaults until parsed
        /// <summary>[AUTOBLIP] ELECTRONIC (a feature of the car), POINT_0..2 (ms), LEVEL (gas).</summary>
        public bool AutoBlipElectronic;
        public float BlipP0 = 20f, BlipP1 = 120f, BlipP2 = 60f, BlipLevel = 0.6f; // ours defaults until parsed
        /// <summary>[AUTO_SHIFTER] UP / DOWN (rpm), SLIP_THRESHOLD, GAS_CUTOFF_TIME (s).</summary>
        public float AutoShiftUp, AutoShiftDown, AutoShiftSlipThreshold = 0.95f, AutoShiftGasCutoffS = 0.1f; // ours defaults until parsed

        public int GearCount { get { return Gears.Length; } }

        /// <summary>Ratio of a gear index (-1 R, 0 N, 1..n) times FINAL; 0 for neutral / unknown.</summary>
        public float TotalRatio(int gear)
        {
            if (gear == 0) return 0f;
            if (gear < 0) return Reverse * Final;
            return gear <= Gears.Length ? Gears[gear - 1] * Final : 0f;
        }
    }

    /// <summary>suspensions.ini [FRONT] / [REAR] (+ [ARB]). Positions AC physics space (CG origin, +X = car left).</summary>
    public sealed class AcAxleSpec
    {
        public string Type = "DWB";
        /// <summary>Wheel centre height relative to the CG (negative = below) - VERIFIED 86/86 (critic C1).</summary>
        public float BaseY = -0.1f; // ours default until parsed
        public float Track = 1.6f; // ours default until parsed
        public float RodLength, HubMass = 40f; // ours default hub mass until parsed
        public float StaticCamberDeg, ToeOut;
        /// <summary>WBTYRE_STEER z (m, tyre-side steering-rod point ahead (+) / behind (-) the hub): the steering-arm lever that turns TOE_OUT
        /// (a rod length change, m) into a toe angle: toe-out rad = TOE_OUT / SteerArmZ (// ours small-angle reading; 0 when missing).</summary>
        public float SteerArmZ;
        /// <summary>Toe-out angle (deg, + = the wheel front points outward) of a TOE_OUT rod length (m) on this axle.</summary>
        public float ToeDegFor(float toeOutM) { return System.Math.Abs(SteerArmZ) > 0.02f ? (float)(System.Math.Atan(toeOutM / SteerArmZ) * 57.29578) : 0f; } // 2 cm guard // ours
        /// <summary>SPRING_RATE (wheel rate N/m), PROGRESSIVE_SPRING_RATE (N/m/m), BUMP_STOP_RATE (N/m).</summary>
        public float SpringRate = 50000f, ProgressiveRate, BumpStopRate = 50000f; // ours defaults until parsed
        /// <summary>BUMPSTOP_UP / BUMPSTOP_DN (m from the design point), PACKER_RANGE (m, 0 = none).</summary>
        public float BumpStopUp = 0.1f, BumpStopDn = 0.1f, PackerRange; // ours defaults until parsed
        /// <summary>Damper wheel rates (N s/m) and the slow/fast thresholds (damper speed, m/s).</summary>
        public float DampBump = 3000f, DampFastBump, DampFastBumpThreshold, DampRebound = 4000f, DampFastRebound, DampFastReboundThreshold; // ours defaults until parsed
        /// <summary>[ARB] FRONT or REAR (N/m).</summary>
        public float ArbRate;
        /// <summary>Axle position along +Z from the CG (front +WHEELBASE*(1-CG_LOCATION), rear -WHEELBASE*CG_LOCATION; VERIFIED 85/86).</summary>
        public float AxleZ;
        /// <summary>[GRAPHICS_OFFSETS] WHEEL_xL / WHEEL_xR (visual x shifts, + = left).</summary>
        public float GraphicsOffsetL, GraphicsOffsetR;
        /// <summary>[HEAVE_FRONT] / [HEAVE_REAR] third (heave) spring of the axle (formula cars; tatuusfa1): acts on the mean travel of the two
        /// wheels, half its force on each. Same keys as a corner.</summary>
        public bool HasHeave;
        /// <summary>[HEAVE_*] ROD_LENGTH (m; positive raises the ride height, as the corners').</summary>
        public float HeaveRodLength;
        public float HeaveRate, HeaveProgressive, HeaveBumpStopRate, HeaveBumpStopUp, HeaveBumpStopDn, HeavePackerRange;
        public float HeaveDampBump, HeaveDampFastBump, HeaveDampFastBumpThreshold, HeaveDampRebound, HeaveDampFastRebound, HeaveDampFastReboundThreshold;
    }

    /// <summary>One tyre of a compound (tyres.ini [FRONT]/[REAR] or [FRONT_n]/[REAR_n], + [THERMAL_*] PERFORMANCE_CURVE).</summary>
    public sealed class AcTyreSpec
    {
        public string Name = "", ShortName = "";
        public float Width = 0.22f, Radius = 0.32f, RimRadius = 0.23f; // ours defaults until parsed
        public float AngularInertia = 1.2f; // ours default until parsed
        public float Damp = 500f, Rate = 250000f; // ours defaults until parsed
        /// <summary>Peak friction long./lat. at load FZ0 and the load-sensitivity exponents: mu(Fz) = D_REF * (Fz/FZ0)^(LS_EXP-1).</summary>
        public float DxRef = 1.2f, DyRef = 1.2f, Fz0 = 3000f, LsExpX = 0.85f, LsExpY = 0.85f; // ours defaults until parsed
        /// <summary>Slip angle (deg) of the peak lateral force.</summary>
        public float FrictionLimitAngle = 8f; // ours default until parsed
        public float CxMult = 1f, FalloffLevel = 0.8f, FalloffSpeed = 2f; // ours defaults until parsed
        public float SpeedSensitivity, RelaxationLength = 0.07f; // ours default relaxation until parsed
        public float RollingResistance0, RollingResistance1, RollingResistanceSlip;
        public float Flex, FlexGain, CamberGain, DCamber0, DCamber1, Xmu;
        public float PressureStatic = 26f, PressureIdeal = 26f, PressureSpringGain, PressureFlexGain, PressureRrGain, PressureDGain; // ours defaults until parsed
        /// <summary>Radius growth in MILLIMETRES = K * omega (rad/s) [READ].</summary>
        public float RadiusAngularK;
        public float BrakeDxMod;
        /// <summary>WEAR_CURVE: virtual km -> grip % (null when missing).</summary>
        public AcLut WearCurve;
        /// <summary>[THERMAL_*] PERFORMANCE_CURVE: tyre temperature C -> grip multiplier (null when missing).</summary>
        public AcLut PerformanceCurve;
        /// <summary>The grip multiplier the physics uses for the thermal curve: its peak (tyres at working temperature; no thermal model in
        /// iteration 1). 1 when the curve is missing.</summary>
        public float PerformancePeak = 1f;
        /// <summary>Legacy (pre-VERSION 10) DX0/DY0 - used only when DX_REF/DY_REF are absent.</summary>
        public float Dx0, Dy0, Dx1, Dy1;
    }

    /// <summary>tyres.ini compound n: front + rear.</summary>
    public sealed class AcTyreCompound
    {
        public int Index;
        public string Name = "", ShortName = "";
        public AcTyreSpec Front = new AcTyreSpec(), Rear = new AcTyreSpec();
        public override string ToString() { return Index + " " + Name + " (" + ShortName + ")"; }
    }

    /// <summary>Generic AC controller input names (ctrl_*.ini and aero.ini [DYNAMIC_CONTROLLER_n] INPUT=).</summary>
    public enum AcCtrlInput
    {
        Unknown = 0, Gear, OversteerFactor, RearSpeedRatio, SlipAngleFrontAverage, SlipAngleFrontMax, SlipAngleRearAverage, SlipAngleRearMax,
        SpeedKmh, Brake, Gas, LatG, LongG, Steer, Rpms, LoadSpreadLF, Count
    }

    /// <summary>One [CONTROLLER_n] / [DYNAMIC_CONTROLLER_n]: value = LUT(input), filtered (FILTER per 333 Hz step), clamped
    /// [DOWN_LIMIT, UP_LIMIT], combined into the chain's result by ADD or MULT.</summary>
    public sealed class AcController
    {
        public AcCtrlInput Input;
        public string InputName = "";
        public bool Mult;
        public AcLut Lut = new AcLut();
        public float Filter;
        public float UpLimit = float.MaxValue, DownLimit = float.MinValue;
        /// <summary>aero.ini DYNAMIC_CONTROLLER: the wing index it drives (-1 for ctrl_*.ini).</summary>
        public int Wing = -1;

        public static AcCtrlInput ParseInput(string s)
        {
            if (string.IsNullOrEmpty(s)) return AcCtrlInput.Unknown;
            switch (s.Trim().ToUpperInvariant())
            {
                case "GEAR": return AcCtrlInput.Gear;
                case "OVERSTEER_FACTOR": return AcCtrlInput.OversteerFactor;
                case "REAR_SPEED_RATIO": return AcCtrlInput.RearSpeedRatio;
                case "SLIPANGLE_FRONT_AVERAGE": return AcCtrlInput.SlipAngleFrontAverage;
                case "SLIPANGLE_FRONT_MAX": return AcCtrlInput.SlipAngleFrontMax;
                case "SLIPANGLE_REAR_AVERAGE": return AcCtrlInput.SlipAngleRearAverage;
                case "SLIPANGLE_REAR_MAX": return AcCtrlInput.SlipAngleRearMax;
                case "SPEED_KMH": case "SPEED": return AcCtrlInput.SpeedKmh;
                case "BRAKE": return AcCtrlInput.Brake;
                case "GAS": return AcCtrlInput.Gas;
                case "LATG": return AcCtrlInput.LatG;
                case "LONG": case "LONGG": case "LONG_G": return AcCtrlInput.LongG;
                case "STEER": return AcCtrlInput.Steer;
                case "RPMS": case "RPM": return AcCtrlInput.Rpms;
                case "LOAD_SPREAD_LF": return AcCtrlInput.LoadSpreadLF;
                default: return AcCtrlInput.Unknown;
            }
        }
    }

    /// <summary>A chain of controllers (one ctrl_*.ini, or the DYNAMIC_CONTROLLERs of one wing). Holds its own filter state; one instance per
    /// car (see <see cref="Clone"/>). <see cref="Eval"/> allocates nothing.</summary>
    public sealed class AcControllerChain
    {
        public readonly List<AcController> Items = new List<AcController>();
        public string Source = "";
        private float[] _state;
        private bool _primed;

        public int Count { get { return Items.Count; } }

        /// <summary>A copy with fresh filter state (the parsed items are shared, read-only).</summary>
        public AcControllerChain Clone()
        {
            var c = new AcControllerChain { Source = Source };
            c.Items.AddRange(Items);
            return c;
        }

        /// <summary>Evaluates the chain: start 0, each item ADDs or MULTiplies its filtered, clamped LUT value. <paramref name="inputs"/> is indexed
        /// by <see cref="AcCtrlInput"/>; <paramref name="dt"/> seconds since the last call (FILTER is per AC 333 Hz step).</summary>
        public float Eval(float[] inputs, float dt)
        {
            int n = Items.Count;
            if (n == 0) return 0f;
            if (_state == null || _state.Length != n) { _state = new float[n]; _primed = false; }
            float steps = dt * 333f; // AC physics rate [BELIEF]
            float result = 0f;
            for (int i = 0; i < n; i++)
            {
                AcController c = Items[i];
                float x = (int)c.Input < inputs.Length ? inputs[(int)c.Input] : 0f;
                float v = c.Lut.Eval(x);
                if (_primed && c.Filter > 0f && c.Filter < 1f)
                {
                    float keep = (float)Math.Pow(c.Filter, steps);
                    v = _state[i] * keep + v * (1f - keep);
                }
                if (v > c.UpLimit) v = c.UpLimit;
                if (v < c.DownLimit) v = c.DownLimit;
                _state[i] = v;
                result = c.Mult ? result * v : result + v;
            }
            _primed = true;
            return result;
        }

        /// <summary>Parses [CONTROLLER_n] sections (ctrl_*.ini) or [DYNAMIC_CONTROLLER_n] sections of one wing (aero.ini).</summary>
        public static AcControllerChain Parse(AcIni ini, string prefix, int wing, AcdArchive acd, List<string> problems, string source)
        {
            var chain = new AcControllerChain { Source = source };
            if (ini == null) return chain;
            foreach (AcIniSection s in ini.WithPrefix(prefix))
            {
                int w = s.GetInt("WING", -1);
                if (wing >= 0 && w != wing) continue;
                var c = new AcController
                {
                    InputName = (s.Get("INPUT", "") ?? "").Trim(),
                    Mult = string.Equals((s.Get("COMBINATOR", "ADD") ?? "ADD").Trim(), "MULT", StringComparison.OrdinalIgnoreCase),
                    Filter = s.GetFloat("FILTER", 0f),
                    UpLimit = s.GetFloat("UP_LIMIT", float.MaxValue),
                    DownLimit = s.GetFloat("DOWN_LIMIT", float.MinValue),
                    Wing = w
                };
                c.Input = AcController.ParseInput(c.InputName);
                if (c.Input == AcCtrlInput.Unknown && problems != null) problems.Add(source + " [" + s.Name + "] unknown INPUT '" + c.InputName + "' (reads 0)");
                string lut = s.Get("LUT", null);
                AcLut l = null;
                try { l = acd != null ? acd.ReadLut(lut) : AcLut.ParseInline(lut); } catch (Exception) { l = null; }
                if (l == null || l.IsEmpty) { if (problems != null) problems.Add(source + " [" + s.Name + "] LUT '" + lut + "' missing"); l = new AcLut(new float[] { 0f }, new float[] { c.Mult ? 1f : 0f }); }
                c.Lut = l;
                chain.Items.Add(c);
            }
            return chain;
        }
    }

    /// <summary>aero.ini [WING_n]. POSITION in physics space (from the CoG, AC axes).</summary>
    public sealed class AcWingSpec
    {
        public int Index;
        public string Name = "";
        public float Chord = 1f, Span = 1f; // ours defaults until parsed
        public AcVec3 Position;
        public AcLut AoaCl, GhCl, AoaCd, GhCd;
        public float ClGain = 1f, CdGain = 1f, Angle, YawClGain; // ours gain defaults until parsed
        /// <summary>aero.ini ZONE_{FRONT,REAR,LEFT,RIGHT}_{CL,CD} (damage: CL /= 1 + sum ZONE_x_CL * zone km/h, CD *= 1 + sum ZONE_x_CD * zone; 0 when absent).</summary>
        public float ZoneFrontCl, ZoneFrontCd, ZoneRearCl, ZoneRearCd, ZoneLeftCl, ZoneLeftCd, ZoneRightCl, ZoneRightCd;
        /// <summary>The [DYNAMIC_CONTROLLER_n] chain driving this wing's angle (null when none).</summary>
        public AcControllerChain Controller;
        public float Area { get { return Chord * Span; } }
    }

    /// <summary>brakes.ini [DATA].</summary>
    public sealed class AcBrakeSpec
    {
        /// <summary>MAX_TORQUE (Nm) - applied PER WHEEL as front = MAX_TORQUE*FRONT_SHARE, rear = MAX_TORQUE*(1-FRONT_SHARE) (research 1.8 reading).</summary>
        public float MaxTorque = 2000f, FrontShare = 0.6f, HandbrakeTorque; // ours defaults until parsed
        public bool CockpitAdjustable;
        public float AdjustStep = 0.5f;
    }

    /// <summary>electronics.ini [ABS] / [TRACTION_CONTROL].</summary>
    public sealed class AcAidSpec
    {
        public bool Present, Active;
        public float SlipRatioLimit = 0.1f; // ours default until parsed
        /// <summary>CURVE (level -> slip ratio limit) or null for a single level.</summary>
        public AcLut Curve;
        /// <summary>Number of selectable levels (CURVE rows; 1 without a curve).</summary>
        public int LevelCount { get { return Curve != null && Curve.Count > 0 ? Curve.Count : 1; } }
        /// <summary>The level the car starts on: the CURVE row closest to SLIP_RATIO_LIMIT (// ours reading: 458 limit 0.08 = row 0 of
        /// 0.08/0.15/0.25/0.35, 458_gt2 0.10 = row 2 of 0.08..0.19).</summary>
        public int DefaultLevel
        {
            get
            {
                if (Curve == null || Curve.Count == 0) return 0;
                int best = 0;
                for (int i = 1; i < Curve.Count; i++) if (Math.Abs(Curve.Y[i] - SlipRatioLimit) < Math.Abs(Curve.Y[best] - SlipRatioLimit)) best = i;
                return best;
            }
        }
        /// <summary>Slip ratio limit of a level (row index into CURVE; out of range / no curve = SLIP_RATIO_LIMIT).</summary>
        public float LimitAt(int level)
        {
            if (Curve == null || level < 0 || level >= Curve.Count) return SlipRatioLimit;
            float v = Curve.Y[level];
            return v > 0.005f ? v : SlipRatioLimit;
        }
        public float RateHz = 100f; // ours default until parsed
        /// <summary>TC only: off below this speed.</summary>
        public float MinSpeedKmh;
    }

    /// <summary>colliders.ini [COLLIDER_n] (physics space): the car-floor box.</summary>
    public struct AcColliderBox
    {
        public AcVec3 Centre, Size;
        public bool GroundEnable;
    }

    /// <summary>One adjustable setup.ini item (for F's garage page). Values in setup.ini's own units (as AC shows them).</summary>
    public sealed class AcSetupItem
    {
        /// <summary>The section name = the item key (PRESSURE_LF, CAMBER_LF, FUEL, BRAKE_POWER_MULT, WING_1, FINAL_GEAR_RATIO, ...).</summary>
        public string Key = "";
        public string Tab = "", Name = "", Help = "";
        public float Min, Max, Step = 1f;
        public int ShowClicks;
        /// <summary>Gear/final choices from RATIOS=xx.rto (label, ratio); null for numeric items.</summary>
        public List<AcLut.RtoEntry> Ratios;
        /// <summary>The car's own value of this item in setup units (from the physics inis; NaN when unknown) - the "default" column.</summary>
        public float Default = float.NaN;
        public override string ToString() { return Key + " [" + Min + ".." + Max + " step " + Step + "] default " + Default; }
    }

    /// <summary>setup.ini [GEAR_SET_n] (USE_GEARSET=1 cars): named complete gear sets.</summary>
    public sealed class AcGearSet
    {
        public string Key = "", Name = "";
        public float[] Gears = new float[0];
    }
}
