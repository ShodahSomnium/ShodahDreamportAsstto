using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [P, Physics] The car's stiff inner model, Unity-FREE and allocation-free per step (the harness simulates with it, the Unity
    /// AcCarPhysics drives it ~333 times a second inside each FixedUpdate): four wheels (spin, relaxed slip ratio / slip angle, AC tyre
    /// forces), engine (power.lut, coast, turbos, limiter at LIMITER_HZ, idle), clutch (MAX_TORQUE, autoclutch MIN/MAX_RPM and the
    /// shift profiles), sequential gearbox (CHANGE_UP/DN_TIME, AUTO_CUTOFF_TIME, autoblip, AUTO_SHIFTER), differentials (PRELOAD + POWER/COAST
    /// ramps, AWD centre, AWD2 coupling, ctrl_*.ini electronic locks), brakes (per-wheel MAX_TORQUE*share, handbrake, ctrl_ebb), ABS / TC
    /// per electronics.ini at their RATE_HZ, fuel (CONSUMPTION), tyre wear (WEAR_CURVE).
    ///
    /// Per sub-step the caller writes, per wheel i (0 LF, 1 RF, 2 LR, 3 RR): <see cref="Load"/> (N), <see cref="VLong"/>/<see cref="VLat"/>
    /// (patch velocity in the wheel frame, m/s), <see cref="Grip"/> (surface FRICTION), <see cref="Damping"/> (surface DAMPING),
    /// <see cref="Camber"/> (rad, signed), and the car-level <see cref="Speed"/>, <see cref="LatG"/>, <see cref="LongG"/>, <see cref="SteerNorm"/>;
    /// then calls <see cref="Step"/>(h) and applies <see cref="Fx"/>/<see cref="Fy"/> at the patches.
    ///
    /// Wheel spin uses a semi-implicit step (the tyre's slip stiffness enters the wheel's effective inertia) and the clutch is solved as an
    /// impulse with the driveline's reflected inertia, so any sub-step size is stable. Low-speed: the relaxation-length slip states act as
    /// tyre springs (no creep on a slope) with a damping blend under 3 m/s (// ours).
    /// </summary>
    public sealed class AcCarCore
    {
        public readonly AcCarSpec Spec;
        public AcTyreCompound Compound { get; private set; }
        public readonly AcTyreModel.Derived[] Tyre = new AcTyreModel.Derived[4];

        // ---- per-step inputs (per wheel)
        public readonly float[] Load = new float[4], VLong = new float[4], VLat = new float[4], Grip = { 1f, 1f, 1f, 1f }, Damping = new float[4], Camber = new float[4];
        // ---- car-level inputs
        /// <summary>Body speed along its forward axis (m/s, signed), lateral / longitudinal acceleration (g), steering -1..1.</summary>
        public float Speed, LatG, LongG, SteerNorm;
        /// <summary>Pedals 0..1 (clutch: 1 = pressed), handbrake 0..1.</summary>
        public float Gas, Brake, Clutch, Handbrake;
        /// <summary>Assists (AcAssists values: 0 off, 1 car's factory setting, 2 on).</summary>
        public int AbsMode = 1, TcMode = 1;
        /// <summary>ABS / TC level (row of electronics.ini CURVE; -1 = the car's own default row, see AcAidSpec.DefaultLevel).</summary>
        public int AbsLevel = -1, TcLevel = -1;
        /// <summary>Counts refused H-shifter changes (no clutch and the revs outside VALID_SHIFT_RPM_WINDOW): the gears grind.</summary>
        public int Grinds { get; private set; }
        public bool AutoShift = true, AutoClutch = true, AutoBlip = true;
        /// <summary>Server-style rates (AcRules): fuel and tyre wear multipliers (1 = AC's 100 %).</summary>
        public float FuelRate = 1f, TyreWearRate = 1f;
        /// <summary>Setup: BRAKE_POWER_MULT (0..1), brake front share override (-1 = car / ctrl_ebb), ENGINE_LIMITER fraction, pressures (psi).</summary>
        public float BrakePowerMult = 1f, FrontShareOverride = -1f, EngineLimiterFraction = 1f;
        public readonly float[] Pressure = new float[4];

        // ---- wheel state
        public readonly float[] Omega = new float[4], SlipState = new float[4], AlphaState = new float[4], SpinRad = new float[4], VirtualKm = new float[4];
        // ---- per-step outputs (per wheel)
        public readonly float[] Fx = new float[4], Fy = new float[4], SlipRatio = new float[4], SlipAngleDeg = new float[4], Skid = new float[4], Combined = new float[4];
        public readonly float[] BrakeTorque = new float[4], DriveTorque = new float[4];

        // ---- engine / gearbox state
        /// <summary>Gear shown and selected (-1 R, 0 N, 1..n); the box engages it when <see cref="Shifting"/> ends.</summary>
        public int Gear { get; private set; }
        public int GearCount { get { return Gears.Length; } }
        /// <summary>The ratios in use (setup can change them): forward gears, reverse (negative), final; the driven-axle diff (2WD).</summary>
        public float[] Gears;
        public float Reverse, Final;
        public AcDiffSpec AxleDiff;
        /// <summary>True: engine and wheels disconnected and the wheels held (frozen on the grid / parked) - the engine still revs.</summary>
        public bool Disconnected;

        /// <summary>Total ratio of a gear (-1 R, 0 N, 1..n) with the setup in use.</summary>
        public float TotalRatio(int gear)
        {
            if (gear == 0) return 0f;
            if (gear < 0) return Reverse * Final;
            return gear <= Gears.Length ? Gears[gear - 1] * Final : 0f;
        }
        /// <summary>Counts gear changes (the Unity side raises AcEvents.GearChanged when it moves).</summary>
        public int GearChanges { get; private set; }
        public float EngineOmega;
        public float Rpm { get { return EngineOmega * 9.549297f; } }
        /// <summary>Effective gas the engine got this step (pedal through throttle.lut, TC, limiter, cut-off, blip, idle).</summary>
        public float GasEffective { get; private set; }
        /// <summary>Clutch engagement 0..1 (1 = closed) this step.</summary>
        public float ClutchEngage { get; private set; }
        public float BoostTotal { get; private set; }
        public float FuelLitres;
        public bool Limiter { get; private set; }
        public bool AbsActive { get; private set; }
        public bool TcActive { get; private set; }
        public bool Shifting { get { return _shiftTimer > 0f; } }
        /// <summary>Engine torque (Nm) and clutch torque (Nm, engine side) of the last step.</summary>
        public float EngineTorque { get; private set; }
        public float ClutchTorque { get; private set; }
        /// <summary>The axle diff / centre lock torques the electronic controllers asked for this step (Nm; 0 when none).</summary>
        public float CtrlLockOut, CtrlCentreOut;
        public float BrakeFrontShare { get; private set; }
        /// <summary>Iteration 2: the car's damage state (set by AcCarPhysics / the harness; null = none). Engine life is stepped here; a dead engine
        /// gets no gas. Multiplier 0 = no effect at all.</summary>
        public AcCarDamage Damage;

        // ---- private state
        private readonly float[] _phi = new float[4];          // share of the gearbox output torque per wheel (open diffs)
        private readonly float[] _ieff = new float[4], _jdl = new float[4];
        private readonly float[] _wear = { 1f, 1f, 1f, 1f };
        private readonly bool[] _absRelease = new bool[4];
        private readonly float[] _boost;
        private readonly float[] _ctrlIn = new float[(int)AcCtrlInput.Count];
        private readonly AcControllerChain _ctrlLock, _ctrlCentre, _ctrlEbb;
        private int _engaged;              // gear the box has engaged (0 while shifting)
        private float _shiftTimer, _cutTimer, _blipT = -1f, _clutchProfT = -1f, _autoLock, _limiterTimer, _absTimer, _tcTimer, _tcGas = 1f;
        private AcClutchProfile _clutchProf;
        private readonly int _tcDefaultLevel, _absDefaultLevel;
        private float _ebb;

        /// <summary>Low-speed damping blend speed (m/s). // ours</summary>
        public const float LowSpeedBlend = 3f;
        private const float SlipStateMax = 4f;    // ours: wind-up guard of the relaxed slip states
        private const float AlphaStateMax = 3f;   // ours

        public AcCarCore(AcCarSpec spec, int compound = -1)
        {
            Spec = spec;
            Gears = (float[])spec.Drivetrain.Gears.Clone();
            Reverse = spec.Drivetrain.Reverse;
            Final = spec.Drivetrain.Final;
            AxleDiff = spec.Drivetrain.Diff;
            _boost = new float[spec.Engine.Turbos.Count];
            _ctrlLock = spec.CtrlSingleLock != null ? spec.CtrlSingleLock.Clone() : null;
            AcControllerChain centre = spec.Drivetrain.Traction == AcTraction.AWD2 ? spec.CtrlAwd2 : spec.Drivetrain.Traction == AcTraction.AWD ? spec.CtrlAwdCentreLock : null;
            _ctrlCentre = centre != null ? centre.Clone() : null;
            _ctrlEbb = spec.CtrlEbb != null ? spec.CtrlEbb.Clone() : null;
            _tcDefaultLevel = spec.Tc.DefaultLevel; _absDefaultLevel = spec.Abs.DefaultLevel;
            SetCompound(compound < 0 ? spec.DefaultCompound : compound);
            FuelLitres = spec.FuelLitres;
            ComputeShares();
            Reset(false);
        }

        /// <summary>Switches tyres (compound index of tyres.ini; resets wear and pressures to the compound's static pressure).</summary>
        public void SetCompound(int index)
        {
            Compound = Spec.Compound(index) ?? new AcTyreCompound();
            for (int i = 0; i < 4; i++)
            {
                AcTyreSpec t = i < 2 ? Compound.Front : Compound.Rear;
                Tyre[i] = AcTyreModel.Derived.From(t);
                Pressure[i] = t.PressureStatic;
                VirtualKm[i] = 0f;
            }
        }

        private void ComputeShares()
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            float f;
            switch (d.Traction)
            {
                case AcTraction.FWD: f = 1f; break;
                case AcTraction.AWD: case AcTraction.AWD2: f = Math.Max(0f, Math.Min(1f, d.AwdFrontSharePct * 0.01f)); break;
                default: f = 0f; break;
            }
            _phi[0] = _phi[1] = f * 0.5f;
            _phi[2] = _phi[3] = (1f - f) * 0.5f;
        }

        /// <summary>Is wheel i driven by the engine?</summary>
        public bool Driven(int i)
        {
            AcTraction t = Spec.Drivetrain.Traction;
            if (t == AcTraction.AWD || t == AcTraction.AWD2) return true;
            return t == AcTraction.FWD ? i < 2 : i >= 2;
        }

        /// <summary>Stops everything (spawn, reset): wheels still, engine at idle, slip states zero; gear kept or neutral.</summary>
        public void Reset(bool keepGear)
        {
            for (int i = 0; i < 4; i++) { Omega[i] = 0f; SlipState[i] = 0f; AlphaState[i] = 0f; Fx[i] = 0f; Fy[i] = 0f; _absRelease[i] = false; }
            EngineOmega = Damage != null && Damage.EngineDead ? 0f : Spec.Engine.Minimum / 9.549297f; // a blown engine stays stopped through resets
            for (int i = 0; i < _boost.Length; i++) _boost[i] = 0f;
            if (!keepGear) { Gear = 0; _engaged = 0; }
            else _engaged = Gear;
            _shiftTimer = 0f; _cutTimer = 0f; _blipT = -1f; _clutchProfT = -1f; _autoLock = 0f; _limiterTimer = 0f; _tcGas = 1f;
        }

        // ------------------------------------------------------------------ gear requests

        /// <summary>One gear up (paddle / key). False when already in top gear or shifting.</summary>
        public bool ShiftUp() { return Change(Gear + 1, false); }

        /// <summary>One gear down (top -> ... -> 1 -> N -> R).</summary>
        public bool ShiftDown() { return Change(Gear - 1, false); }

        /// <summary>H-shifter / direct request (-1 R, 0 N, 1..n). On a car with drivetrain.ini SUPPORTS_SHIFTER=1 and the auto-clutch off, a
        /// gear (not neutral) only goes in with the clutch pressed (pedal past half // ours) or the engine within VALID_SHIFT_RPM_WINDOW of
        /// the rpm the new gear needs at this wheel speed; otherwise it grinds (refused, <see cref="Grinds"/>++). Neutral always goes in.</summary>
        public bool RequestGear(int gear)
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            if (gear != 0 && gear != Gear && d.SupportsShifter && !AutoClutch && !d.AutoClutchForcedOn && Clutch < 0.5f)
            {
                float need = Math.Abs(DrivenWheelOmega() * TotalRatio(gear)) * 9.549297f;
                if (Math.Abs(Rpm - need) > Math.Max(50f, d.ValidShiftRpmWindow)) { Grinds++; return false; } // 50 rpm floor // ours
            }
            return Change(gear, false);
        }

        /// <summary>Mean angular speed (rad/s) of the driven wheels.</summary>
        private float DrivenWheelOmega()
        {
            float sum = 0f; int n = 0;
            for (int i = 0; i < 4; i++) if (_phi[i] > 0f) { sum += Omega[i]; n++; }
            return n > 0 ? sum / n : 0f;
        }

        private bool Change(int to, bool automatic)
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            if (to > Gears.Length || to < -1 || to == Gear) return false;
            if (_shiftTimer > 0f) return false;
            bool up = to > Gear && Gear >= 0;
            if (to == -1 && Math.Abs(Speed) > 2f) return false; // ours: reverse only near standstill
            int from = Gear;
            Gear = to;
            _engaged = 0;
            _shiftTimer = (up ? d.ChangeUpMs : d.ChangeDnMs) * 0.001f;
            if (from == 0 || to == 0) _shiftTimer = Math.Min(_shiftTimer, d.ChangeUpMs * 0.001f);
            if (_shiftTimer < 0.01f) _shiftTimer = 0.01f; // ours: at least one sub-step disconnected
            if (up && from > 0)
            {
                float cut = automatic ? d.AutoShiftGasCutoffS : d.AutoCutoffMs * 0.001f;
                if (cut > 0f) _cutTimer = cut;
            }
            bool clutchAid = AutoClutch || d.AutoClutchOnChanges || d.AutoClutchForcedOn;
            AcClutchProfile p = up ? d.UpshiftProfile : d.DownshiftProfile;
            if (clutchAid && p.Present && from != 0 && to != 0) { _clutchProf = p; _clutchProfT = 0f; }
            if (!up && to > 0 && from > 0 && (AutoBlip || d.AutoBlipElectronic)) _blipT = 0f;
            _autoLock = _shiftTimer + 0.25f; // ours: auto shifter waits after any change
            GearChanges++;
            return true;
        }

        // ------------------------------------------------------------------ the step

        /// <summary>Advances the inner model by <paramref name="h"/> seconds (call ~333 times a second).</summary>
        public void Step(float h)
        {
            if (h <= 0f) return;
            AcDrivetrainSpec d = Spec.Drivetrain;
            AcEngineSpec en = Spec.Engine;

            // ---- controllers' inputs (shared by ctrl_*.ini chains)
            FillControllerInputs();
            CtrlLockOut = _ctrlLock != null ? _ctrlLock.Eval(_ctrlIn, h) : 0f;
            CtrlCentreOut = _ctrlCentre != null ? _ctrlCentre.Eval(_ctrlIn, h) : 0f;
            _ebb = _ctrlEbb != null ? _ctrlEbb.Eval(_ctrlIn, h) : 0f;

            // ---- gearbox timers and the auto shifter
            if (_shiftTimer > 0f) { _shiftTimer -= h; if (_shiftTimer <= 0f) { _shiftTimer = 0f; _engaged = Gear; } }
            if (_autoLock > 0f) _autoLock -= h;
            if (AutoShift && _shiftTimer <= 0f && _autoLock <= 0f) AutoShiftStep();

            // ---- gas: pedal -> throttle.lut -> TC -> cut-offs / limiter -> blip -> idle
            float pedal = Clamp01(Gas);
            float gas = en.ThrottleLut != null ? Clamp01(en.ThrottleLut.Eval(pedal * 100f) * 0.01f) : pedal;
            TcStep(h);
            gas *= _tcGas;
            if (_cutTimer > 0f) { _cutTimer -= h; gas = 0f; }
            float limiterRpm = en.Limiter > 0f ? en.Limiter * Clamp(EngineLimiterFraction, 0.5f, 1f) : 0f;
            if (_limiterTimer > 0f) _limiterTimer -= h;
            if (limiterRpm > 0f && Rpm >= limiterRpm && _limiterTimer <= 0f) _limiterTimer = 1f / Math.Max(1f, en.LimiterHz);
            Limiter = _limiterTimer > 0f;
            if (Limiter) gas = 0f;
            if (_blipT >= 0f)
            {
                float blip = BlipLevel(_blipT * 1000f);
                _blipT += h;
                if (blip < 0f) _blipT = -1f; else if (blip > gas) gas = blip;
            }
            float idle = en.Minimum;
            if (Rpm < idle) { float ig = Clamp01((idle - Rpm) / 250f); if (ig > gas) gas = ig; } // ours idle controller gain
            if (FuelLitres <= 0f && Spec.FuelConsumption > 0f) gas = 0f;
            if (Damage != null && Damage.EngineDead) gas = 0f; // blown engine: no throttle, no idle controller, no restart until repaired (coast only)
            GasEffective = gas;

            // ---- turbos
            float rpm = Rpm;
            float boost = 0f;
            for (int i = 0; i < _boost.Length; i++)
            {
                AcTurboSpec t = en.Turbos[i];
                float target = t.MaxBoost * (float)Math.Pow(gas, Math.Max(0.01f, t.Gamma)) * (rpm / t.ReferenceRpm);
                if (target > t.MaxBoost) target = t.MaxBoost;
                if (t.Wastegate > 0f) { float wg = t.Wastegate * (t.CockpitAdjustable ? Clamp01(t.DefaultAdjustment) : 1f); if (target > wg) target = wg; }
                if (target < 0f) target = 0f;
                float lag = target > _boost[i] ? t.LagUp : t.LagDn;
                float keep = (float)Math.Pow(Clamp(lag, 0f, 0.99999f), h * 333f);
                _boost[i] = target + (_boost[i] - target) * keep;
                boost += _boost[i];
            }
            BoostTotal = boost;
            // ---- engine life (iteration 2, DAMAGE_DESIGN 3.3): every sub-step, so money-shift spikes count; no-op at multiplier 0
            if (Damage != null) Damage.StepEngine(h, rpm, boost);

            // ---- engine torque
            float te = gas * en.TorqueAt(rpm) * (1f + boost) + (1f - gas) * en.CoastAt(rpm);
            EngineTorque = te;

            // ---- brakes + ABS
            float share = FrontShareOverride >= 0f ? FrontShareOverride : (_ctrlEbb != null && _ebb > 0.05f && _ebb < 0.95f ? _ebb : Spec.Brakes.FrontShare);
            BrakeFrontShare = share;
            float bp = Clamp01(Brake) * Clamp(BrakePowerMult, 0f, 1.5f);
            AbsStep(h);
            for (int i = 0; i < 4; i++)
            {
                float tb = Spec.Brakes.MaxTorque * (i < 2 ? share : 1f - share) * bp;
                if (_absRelease[i]) tb = 0f;
                if (i >= 2) tb += Clamp01(Handbrake) * Spec.Brakes.HandbrakeTorque;
                BrakeTorque[i] = tb;
            }

            // ---- tyres: relaxed slip states, forces, implicit wheel inertia, free spin update
            float ratio = TotalRatio(_engaged);
            for (int i = 0; i < 4; i++) _jdl[i] = _phi[i] * d.GearboxInertia * ratio * ratio; // the gearbox turns with the driven wheels while a gear is in
            for (int i = 0; i < 4; i++) TyreStep(i, h);

            // ---- drivetrain: clutch impulse between the engine and the driveline (open diffs), then the diff locks
            float engage = ClutchEngagement(h);
            ClutchEngage = _engaged != 0 ? engage : 0f;
            float ie = Math.Max(0.01f, en.Inertia);
            float weFree = EngineOmega + h * te / ie;
            float tc = 0f;
            for (int i = 0; i < 4; i++) DriveTorque[i] = 0f;
            if (_engaged != 0 && ratio != 0f && engage > 0f)
            {
                float invIdl = 0f, wDs = 0f;
                for (int i = 0; i < 4; i++)
                {
                    if (_phi[i] <= 0f) continue;
                    invIdl += _phi[i] * _phi[i] / _ieff[i];
                    wDs += _phi[i] * Omega[i];
                }
                invIdl *= ratio * ratio;
                wDs *= ratio;
                float idl = invIdl > 1e-9f ? 1f / invIdl : 1e9f;
                float need = (weFree - wDs) / (h * (1f / ie + 1f / idl));
                float cap = Math.Max(0f, d.ClutchMaxTorque) * engage;
                tc = Clamp(need, -cap, cap);
                float tOut = tc * ratio;
                for (int i = 0; i < 4; i++)
                {
                    if (_phi[i] <= 0f) continue;
                    float ti = _phi[i] * tOut;
                    DriveTorque[i] = ti;
                    Omega[i] += h * ti / _ieff[i];
                }
            }
            ClutchTorque = tc;
            EngineOmega = weFree - h * tc / ie;
            if (EngineOmega < 0f) EngineOmega = 0f;
            DiffLocks(h, tc * ratio);

            // ---- friction torques last (brakes, rolling resistance, surface drag): can stop a wheel, never reverse it
            for (int i = 0; i < 4; i++)
            {
                float r = Tyre[i].Radius;
                float tf = BrakeTorque[i] + r * AcTyreModel.RollingResistance(ref Tyre[i], Load[i], VLong[i], AlphaState[i]);
                if (Damping[i] > 0f && Load[i] > 0f) tf += r * Load[i] * Damping[i] * Math.Min(Math.Abs(VLong[i]), 20f) * 0.25f; // ours: DAMPING drag
                float dw = h * tf / _ieff[i];
                float w = Omega[i];
                Omega[i] = w > dw ? w - dw : w < -dw ? w + dw : 0f;
                SpinRad[i] += Omega[i] * h;
                if (SpinRad[i] > 1000f || SpinRad[i] < -1000f) SpinRad[i] %= (float)(2.0 * Math.PI);
            }

            if (Disconnected) for (int i = 0; i < 4; i++) { Omega[i] = 0f; SlipState[i] = 0f; AlphaState[i] = 0f; }

            // ---- fuel and wear
            if (Spec.FuelConsumption > 0f && FuelLitres > 0f)
            {
                FuelLitres -= Rpm * gas * Spec.FuelConsumption * 0.001f * h * FuelRate;
                if (FuelLitres < 0f) FuelLitres = 0f;
            }
            for (int i = 0; i < 4; i++)
            {
                if (Load[i] <= 0f) continue;
                AcTyreSpec ts = i < 2 ? Compound.Front : Compound.Rear;
                // virtual km from the SLIDING distance at the patch, scaled by load ([VIRTUALKM] USE_LOAD=1) - // ours reading: the 458's
                // semislick curve is 98 % at 10 and 80 % at 25 "km", which plain distance would reach in four laps
                float slide = (float)Math.Sqrt(SlipRatio[i] * SlipRatio[i] * VLong[i] * VLong[i] + VLat[i] * VLat[i]);
                VirtualKm[i] += slide * h * 0.001f * TyreWearRate * (Load[i] / Tyre[i].Fz0);
                _wear[i] = ts.WearCurve != null ? Clamp(ts.WearCurve.Eval(VirtualKm[i]) * 0.01f, 0.5f, 1.1f) : 1f;
            }
        }

        private void TyreStep(int i, float h)
        {
            ref AcTyreModel.Derived t = ref Tyre[i];
            float v = VLong[i], vl = VLat[i];
            float av = Math.Abs(v);
            float w = Omega[i];
            float r = t.Radius + t.RadiusK * Math.Abs(w) * 0.001f;
            float slipVel = w * r - v;
            if (Load[i] <= 0f)
            {
                SlipState[i] = 0f; AlphaState[i] = 0f;
                Fx[i] = 0f; Fy[i] = 0f; Skid[i] = 0f; Combined[i] = 0f;
                SlipRatio[i] = slipVel / Math.Max(av, 1f);
                SlipAngleDeg[i] = 0f;
                _ieff[i] = t.Inertia + _jdl[i];
                Omega[i] = w;
                return;
            }
            float e = (float)Math.Exp(-h * av / t.Relax);
            float g = av > 1e-3f ? (1f - e) / av : h / t.Relax;
            float ss = SlipState[i] * e + g * slipVel;
            float sa = AlphaState[i] * e + g * vl;
            ss = Clamp(ss, -SlipStateMax, SlipStateMax);
            sa = Clamp(sa, -AlphaStateMax, AlphaStateMax);
            SlipState[i] = ss; AlphaState[i] = sa;
            float blend = av < LowSpeedBlend ? 1f - av / LowSpeedBlend : 0f;
            float sigma = ss + blend * slipVel / LowSpeedBlend;
            float tanA = sa + blend * vl / LowSpeedBlend;
            // |p_hot - IDEAL| * D_GAIN with p_hot = p_set + IDEAL - STATIC (no thermal model, // ours; see AcTyreModel)
            float pressure = 1f - Math.Abs(Pressure[i] - (i < 2 ? Compound.Front : Compound.Rear).PressureStatic) * t.PressureDGain;
            float grip = Grip[i] * _wear[i] * Clamp(pressure, 0.7f, 1f);
            float slide = (float)Math.Sqrt(slipVel * slipVel + vl * vl);
            float fx, fy, kx, ky, comb;
            AcTyreModel.Forces(ref t, Load[i], sigma, tanA, grip, Camber[i], slide, out fx, out fy, out kx, out ky, out comb);
            Fx[i] = fx; Fy[i] = fy; Combined[i] = comb;
            Skid[i] = Clamp01((comb - 0.9f) * 2f) * Clamp01(slide * 0.5f); // ours: audible slide past ~the peak
            SlipRatio[i] = slipVel / Math.Max(av, 1f);
            SlipAngleDeg[i] = (float)(Math.Atan2(vl, Math.Max(av, 0.5f)) * 57.29578);
            float dSigmaDw = r * (g + blend / LowSpeedBlend);
            _ieff[i] = t.Inertia + _jdl[i] + h * kx * r * dSigmaDw;
            Omega[i] = w + h * (-r * fx) / _ieff[i];
        }

        private void DiffLocks(float h, float outputTorque)
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            bool power = outputTorque >= 0f;
            float tAbs = Math.Abs(outputTorque);
            switch (d.Traction)
            {
                case AcTraction.RWD:
                case AcTraction.FWD:
                {
                    int a = d.Traction == AcTraction.FWD ? 0 : 2;
                    float lim = AxleLimit(AxleDiff, power, tAbs);
                    if (CtrlLockOut > lim) lim = CtrlLockOut;
                    Lock(a, a + 1, lim, h);
                    break;
                }
                default:
                {
                    float f = _phi[0] * 2f;
                    float centreLim = d.Traction == AcTraction.AWD2 ? (_ctrlCentre != null ? CtrlCentreOut * Math.Max(0.01f, d.CentreMaxTorque) : d.CentreRampTorque)
                                                                   : AxleLimit(d.CentreDiff, power, tAbs);
                    if (d.Traction == AcTraction.AWD && _ctrlCentre != null && CtrlCentreOut > centreLim) centreLim = CtrlCentreOut;
                    LockAxles(centreLim, h);
                    Lock(0, 1, AxleLimit(d.FrontDiff, power, tAbs * f), h);
                    Lock(2, 3, AxleLimit(d.RearDiff, power, tAbs * (1f - f)), h);
                    break;
                }
            }
        }

        private static float AxleLimit(AcDiffSpec s, bool power, float torque)
        {
            return Math.Max(0f, s.Preload) + Math.Max(0f, power ? s.Power : s.Coast) * torque;
        }

        private void Lock(int a, int b, float limit, float h)
        {
            if (limit <= 0f) return;
            float need = (Omega[a] - Omega[b]) / (h * (1f / _ieff[a] + 1f / _ieff[b]));
            float t = Clamp(need, -limit, limit);
            Omega[a] -= h * t / _ieff[a];
            Omega[b] += h * t / _ieff[b];
        }

        private void LockAxles(float limit, float h)
        {
            if (limit <= 0f) return;
            float invF = 0.25f * (1f / _ieff[0] + 1f / _ieff[1]);
            float invR = 0.25f * (1f / _ieff[2] + 1f / _ieff[3]);
            float wf = 0.5f * (Omega[0] + Omega[1]), wr = 0.5f * (Omega[2] + Omega[3]);
            float need = (wf - wr) / (h * (invF + invR));
            float t = Clamp(need, -limit, limit);
            Omega[0] -= h * 0.5f * t / _ieff[0]; Omega[1] -= h * 0.5f * t / _ieff[1];
            Omega[2] += h * 0.5f * t / _ieff[2]; Omega[3] += h * 0.5f * t / _ieff[3];
        }

        private float ClutchEngagement(float h)
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            if (_engaged == 0 || Disconnected) return 0f;
            float e;
            if (AutoClutch || d.AutoClutchForcedOn)
            {
                // AC's window: the clutch bites from MIN_RPM and is closed at MAX_RPM (launches happen there, engine-limited, no wheelspin)
                e = Clamp01((Rpm - d.AutoClutchMinRpm) / (d.AutoClutchMaxRpm - d.AutoClutchMinRpm));
            }
            else e = 1f - Clamp01(Clutch);
            if (_clutchProfT >= 0f)
            {
                float ms = _clutchProfT * 1000f;
                _clutchProfT += h;
                AcClutchProfile p = _clutchProf;
                float pressed; // 1 = pedal fully pressed
                if (ms < p.P0) pressed = p.P0 > 0f ? ms / p.P0 : 1f;
                else if (ms < p.P1) pressed = 1f;
                else if (ms < p.P2) pressed = p.P2 > p.P1 ? 1f - (ms - p.P1) / (p.P2 - p.P1) : 0f;
                else { pressed = 0f; _clutchProfT = -1f; }
                e = Math.Min(e, 1f - pressed);
            }
            return e;
        }

        /// <summary>Autoblip gas at t ms after the downshift (POINT_0 rise, hold to POINT_1, fall over POINT_2 when POINT_2 &lt; POINT_1
        /// (458: 20/130/60) or until POINT_2 when larger (formula_k: 10/150/180) - // ours reading of the two forms); -1 when over.</summary>
        private float BlipLevel(float ms)
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            float p0 = Math.Max(1f, d.BlipP0), p1 = Math.Max(p0, d.BlipP1);
            float end = d.BlipP2 > p1 ? d.BlipP2 : p1 + Math.Max(1f, d.BlipP2);
            if (ms < p0) return d.BlipLevel * ms / p0;
            if (ms < p1) return d.BlipLevel;
            if (ms < end) return d.BlipLevel * (1f - (ms - p1) / (end - p1));
            return -1f;
        }

        /// <summary>The auto shifter upshifts this far below the rev limiter in force at the latest (rpm). // ours</summary>
        public const float AutoShiftLimiterMarginRpm = 50f; // ours

        private void AutoShiftStep()
        {
            AcDrivetrainSpec d = Spec.Drivetrain;
            if (Gear == 0)
            {
                if (Gas > 0.1f && Brake < 0.1f) Change(1, true);
                return;
            }
            if (Gear < 0) return;
            float rpm = Rpm;
            // the effective upshift point: AUTO_SHIFTER UP, but never at / above the rev limiter actually in force (garage ENGINE_LIMITER < 100 %, or a
            // car whose UP / the ai.ini fallback sits above LIMITER) - else the engine bounces on the limiter and never reaches UP
            float up = d.AutoShiftUp;
            float limiterRpm = Spec.Engine.Limiter > 0f ? Spec.Engine.Limiter * Clamp(EngineLimiterFraction, 0.5f, 1f) : 0f;
            if (limiterRpm > 0f) up = Math.Min(up, limiterRpm - AutoShiftLimiterMarginRpm);
            if (rpm >= up && Gear < Gears.Length && ClutchEngage > 0.9f) { Change(Gear + 1, true); return; }
            if (Gear > 1 && rpm <= d.AutoShiftDown)
            {
                float after = rpm * Gears[Gear - 2] / Math.Max(0.01f, Gears[Gear - 1]);
                if (after < up * 0.95f && !DrivenWheelsLocking()) Change(Gear - 1, true); // 0.95 ours margin
            }
        }

        /// <summary>SLIP_THRESHOLD (// ours reading): no auto downshift while the driven wheels turn slower than threshold * road speed.</summary>
        private bool DrivenWheelsLocking()
        {
            float v = Math.Abs(Speed);
            if (v < 3f) return false;
            float sum = 0f; int n = 0;
            for (int i = 0; i < 4; i++) if (_phi[i] > 0f) { sum += Math.Abs(Omega[i]) * Tyre[i].Radius; n++; }
            return n > 0 && sum / n < v * Spec.Drivetrain.AutoShiftSlipThreshold;
        }

        private void TcStep(float h)
        {
            AcAidSpec tc = Spec.Tc;
            bool on = TcMode == 2 || (TcMode == 1 && tc.Present && tc.Active);
            if (!on) { _tcGas = 1f; TcActive = false; return; }
            _tcTimer += h;
            float period = 1f / tc.RateHz;
            if (_tcTimer < period) return;
            _tcTimer = 0f;
            float speedKmh = Math.Abs(Speed) * 3.6f;
            if (speedKmh < tc.MinSpeedKmh) { _tcGas = 1f; TcActive = false; return; }
            float limit = tc.LimitAt(TcLevel >= 0 ? TcLevel : _tcDefaultLevel); // electronics.ini CURVE row (level) or SLIP_RATIO_LIMIT
            float worst = 0f;
            for (int i = 0; i < 4; i++) if (_phi[i] > 0f && Load[i] > 0f && SlipRatio[i] > worst) worst = SlipRatio[i];
            float target = worst > limit ? Clamp01(1f - (worst - limit) / Math.Max(0.02f, limit) * 2f) : 1f; // ours: proportional cut
            _tcGas = target < _tcGas ? target : Math.Min(1f, _tcGas + 0.2f); // ours: fast cut, quick recovery
            TcActive = _tcGas < 0.98f;
        }

        private void AbsStep(float h)
        {
            AcAidSpec abs = Spec.Abs;
            bool on = AbsMode == 2 || (AbsMode == 1 && abs.Present && abs.Active);
            if (!on) { for (int i = 0; i < 4; i++) _absRelease[i] = false; AbsActive = false; return; }
            _absTimer += h;
            if (_absTimer < 1f / abs.RateHz) return;
            _absTimer = 0f;
            bool any = false;
            float limit = abs.LimitAt(AbsLevel >= 0 ? AbsLevel : _absDefaultLevel);
            for (int i = 0; i < 4; i++)
            {
                bool rel = Brake > 0.01f && Load[i] > 0f && Math.Abs(VLong[i]) > 2f && SlipRatio[i] < -limit; // 2 m/s ours
                _absRelease[i] = rel;
                any |= rel;
            }
            AbsActive = any;
        }

        private void FillControllerInputs()
        {
            float[] c = _ctrlIn;
            c[(int)AcCtrlInput.Gear] = Gear;
            c[(int)AcCtrlInput.SpeedKmh] = Math.Abs(Speed) * 3.6f;
            c[(int)AcCtrlInput.Brake] = Clamp01(Brake);
            c[(int)AcCtrlInput.Gas] = Clamp01(Gas);
            c[(int)AcCtrlInput.LatG] = LatG;
            c[(int)AcCtrlInput.LongG] = LongG;
            c[(int)AcCtrlInput.Steer] = SteerNorm;
            c[(int)AcCtrlInput.Rpms] = Rpm;
            float af = 0.5f * (Math.Abs(SlipAngleDeg[0]) + Math.Abs(SlipAngleDeg[1]));
            float ar = 0.5f * (Math.Abs(SlipAngleDeg[2]) + Math.Abs(SlipAngleDeg[3]));
            c[(int)AcCtrlInput.SlipAngleFrontAverage] = af;
            c[(int)AcCtrlInput.SlipAngleRearAverage] = ar;
            c[(int)AcCtrlInput.SlipAngleFrontMax] = Math.Max(Math.Abs(SlipAngleDeg[0]), Math.Abs(SlipAngleDeg[1]));
            c[(int)AcCtrlInput.SlipAngleRearMax] = Math.Max(Math.Abs(SlipAngleDeg[2]), Math.Abs(SlipAngleDeg[3]));
            c[(int)AcCtrlInput.OversteerFactor] = ar - af; // ours reading: rear minus front slip angle (deg)
            float wf = 0.5f * (Math.Abs(Omega[0]) * Tyre[0].Radius + Math.Abs(Omega[1]) * Tyre[1].Radius);
            float wr = 0.5f * (Math.Abs(Omega[2]) * Tyre[2].Radius + Math.Abs(Omega[3]) * Tyre[3].Radius);
            c[(int)AcCtrlInput.RearSpeedRatio] = wf > 0.5f ? Math.Max(0f, wr / wf - 1f) : 0f; // ours reading
            float lt = Load[0] + Load[1] + Load[2] + Load[3];
            c[(int)AcCtrlInput.LoadSpreadLF] = lt > 1f ? (Load[0] + Load[1]) / lt : 0.5f; // ours reading: front load fraction
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
