using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// What the AI needs to know about a car to plan its speeds and drive it: its own ai.ini (<see cref="Ai"/>) plus a digest of the
    /// physics inis from data.acd - mass, tyre grip at static load (tyres.ini DY_REF/DX_REF with LS_EXPY/LS_EXPX load sensitivity at FZ0),
    /// downforce and drag areas (aero.ini wings: CL/CD lut at ANGLE x GAIN x CHORD x SPAN), wheel force per gear (power.lut x turbo boost
    /// x GEAR_n x FINAL / driven tyre RADIUS), brake torque, wheelbase / CG, steer lock / ratio.
    ///
    /// Read straight from the archive (independent of P's AcCarSpec, so the AI plans with the same files whatever P's parser does).
    /// Worker-thread safe, Unity-free, never throws (problems in <see cref="Problems"/>).
    ///
    /// power.lut is treated as torque AT THE WHEELS (no extra driveline loss): ferrari_458 power.lut peaks at 466 Nm against ui_car.json
    /// "540Nm" (86 %) [VERIFIED numbers; reading = BELIEF]. Turbo: T x (1 + boost), boost = min(MAX_BOOST, WASTEGATE) x
    /// min(1, rpm / REFERENCE_RPM)^GAMMA per turbo (engine.ini comment: "multiply the torque like T=T*(1.0 + boost)") [READ].
    /// </summary>
    public sealed class AcAiCar
    {
        public const float G = 9.81f;
        public const float AirDensity = 1.2f; // ours (research 1.10 uses the same)

        public string CarId = "";
        public AcAiIni Ai = new AcAiIni();
        public readonly List<string> Problems = new List<string>();

        /// <summary>car.ini TOTALMASS + [FUEL] FUEL x 0.75 kg/l (// ours density, as P's research 1.10).</summary>
        public float MassKg = 1300f; // ours default
        /// <summary>car.ini [BASIC] TOTALMASS (kg, with driver, no fuel).</summary>
        public float TotalMassKg = 1300f; // ours default
        /// <summary>car.ini [FUEL] FUEL (start litres), MAX_FUEL, CONSUMPTION (litres per second = rpm x gas x CONSUMPTION / 1000), and
        /// [GRAPHICS] FUEL_LIGHT_MIN_LITERS (the fuel warning light).</summary>
        public float FuelStart = 30f, FuelMax = 60f, FuelConsumption, FuelLightMin = 3f; // ours defaults until read
        /// <summary>Fuel the planning mass assumes (litres).</summary>
        public float FuelLitres;
        /// <summary>kg per litre of fuel (P's AcCarPhysics.FuelKgPerLitre). // ours</summary>
        public const float FuelKgPerLitre = 0.75f;

        /// <summary>Sets the fuel the plan assumes (mass = TOTALMASS + fuel x 0.75 kg/l, as P's physics).</summary>
        public void SetFuel(float litres)
        {
            FuelLitres = Math.Max(0f, Math.Min(litres, FuelMax));
            MassKg = TotalMassKg + FuelLitres * FuelKgPerLitre;
        }
        /// <summary>suspensions.ini [BASIC] CG_LOCATION (front weight fraction) and WHEELBASE (m).</summary>
        public float CgFront = 0.5f, WheelbaseM = 2.6f; // ours defaults
        /// <summary>CG height above the ground (m) = -car.ini GRAPHICS_OFFSET.y (research critic addendum: CG height = -GRAPHICS_OFFSET.y).</summary>
        public float CgHeightM = 0.45f; // ours default
        /// <summary>car.ini [CONTROLS] STEER_LOCK (steering-wheel deg) and STEER_RATIO.</summary>
        public float SteerLockDeg = 360f, SteerRatio = 14f; // ours defaults
        /// <summary>Peak friction coefficients at static load (front / rear, lateral / longitudinal), times ai.ini TYRES HINT.</summary>
        public float MuLatFront = 1.1f, MuLatRear = 1.1f, MuLongFront = 1.1f, MuLongRear = 1.1f; // ours defaults
        public string FrontCompound = "", RearCompound = "";
        /// <summary>tyres.ini front FRICTION_LIMIT_ANGLE (deg): the slip angle of peak grip.</summary>
        public float FrictionLimitAngleFront = 7.5f; // ours default
        /// <summary>Sum of CL x area (m^2) over the wings (positive = downforce), times ai.ini AERO_HINT.</summary>
        public float ClA;
        /// <summary>Sum of CD x area (m^2).</summary>
        public float CdA = 0.7f; // ours default
        /// <summary>Wheel torque curve (power.lut).</summary>
        public AcLut Torque = new AcLut();
        public float IdleRpm = 900f, LimiterRpm = 7000f; // ours defaults
        /// <summary>Turbos: total max boost and the curve shape of the first turbo.</summary>
        public float BoostMax, BoostRefRpm = 1f, BoostGamma = 1f;
        public float[] Gears = new float[0];
        public float FinalRatio = 4f; // ours default
        public float ReverseRatio = -3f; // ours default
        /// <summary>"RWD", "FWD", "AWD".</summary>
        public string Traction = "RWD";
        public float WheelRadiusFront = 0.32f, WheelRadiusRear = 0.32f; // ours defaults
        /// <summary>brakes.ini MAX_TORQUE and FRONT_SHARE (torque per wheel reading, research 1.8 [BELIEF]).</summary>
        public float BrakeTorque = 2500f, BrakeFrontShare = 0.6f; // ours defaults
        /// <summary>Rolling resistance force (N) at rest: tyres.ini ROLLING_RESISTANCE_0 x 4 (per wheel [BELIEF]).</summary>
        public float RollingN = 40f; // ours default
        /// <summary>Rotating inertias (kg m^2) the engine has to spin up with the car: engine.ini [ENGINE_DATA] INERTIA, drivetrain.ini [GEARBOX]
        /// INERTIA (both on the engine side of the gear, as P's AcCarCore turns them) and tyres.ini ANGULAR_INERTIA per front / rear wheel; 0 =
        /// not in the data (no effect). Polish pass 2026-11-01: without them the plan's 2nd-gear acceleration was ~15 % above P's car.</summary>
        public float EngineInertia, GearboxInertia, WheelInertiaFront, WheelInertiaRear;

        public float DrivenRadius { get { return Traction == "FWD" ? WheelRadiusFront : WheelRadiusRear; } }
        /// <summary>Fraction of the car's weight on the driven wheels.</summary>
        public float DrivenWeightShare { get { return Traction == "FWD" ? CgFront : Traction == "AWD" ? 1f : 1f - CgFront; } }
        /// <summary>Weight-averaged lateral grip coefficient.</summary>
        public float MuLat { get { return MuLatFront * CgFront + MuLatRear * (1f - CgFront); } }
        public float MuLong { get { return MuLongFront * CgFront + MuLongRear * (1f - CgFront); } }
        /// <summary>Largest road-wheel angle (deg) = STEER_LOCK / STEER_RATIO.</summary>
        public float MaxRoadAngleDeg { get { return SteerRatio > 0.1f ? SteerLockDeg / SteerRatio : 30f; } }
        public int GearCount { get { return Gears.Length; } }

        /// <summary>Loads from an opened archive (data.acd or data/ folder). Never null.</summary>
        public static AcAiCar Load(AcdArchive acd, string carId)
        {
            var c = new AcAiCar { CarId = carId ?? "" };
            if (acd == null) { c.Problems.Add("no data.acd"); return c; }
            try { c.Read(acd); }
            catch (Exception e) { c.Problems.Add("read failed: " + e.Message); }
            return c;
        }

        /// <summary>Loads a car folder (opens data.acd / data). Never null, never throws.</summary>
        public static AcAiCar Load(string carDir, string carId)
        {
            AcdArchive acd = null;
            try { acd = AcdArchive.Open(carDir); }
            catch (Exception e) { var c = new AcAiCar { CarId = carId ?? "" }; c.Problems.Add("data.acd: " + e.Message); return c; }
            return Load(acd, carId);
        }

        void Read(AcdArchive acd)
        {
            AcIni ai = acd.ReadIni("ai.ini");
            if (ai == null) Problems.Add("no ai.ini (defaults)");
            Ai = AcAiIni.FromIni(ai);

            AcIni car = acd.ReadIniOrEmpty("car.ini");
            float total = car.GetFloat("BASIC", "TOTALMASS", 0f);
            if (total <= 0f) { Problems.Add("car.ini TOTALMASS missing"); total = 1300f; } // ours
            TotalMassKg = total;
            FuelMax = car.GetFloat("FUEL", "MAX_FUEL", FuelMax);
            FuelStart = car.GetFloat("FUEL", "FUEL", 0f);
            FuelConsumption = car.GetFloat("FUEL", "CONSUMPTION", 0f);
            FuelLightMin = car.GetFloat("GRAPHICS", "FUEL_LIGHT_MIN_LITERS", FuelLightMin);
            if (FuelMax < 1f) FuelMax = Math.Max(FuelStart, 60f); // ours
            SetFuel(FuelStart);
            SteerLockDeg = car.GetFloat("CONTROLS", "STEER_LOCK", SteerLockDeg);
            SteerRatio = car.GetFloat("CONTROLS", "STEER_RATIO", SteerRatio);
            if (SteerRatio < 0f) SteerRatio = -SteerRatio; // 16 cars have it negative: P (AcCarSpec) uses |STEER_RATIO| too
            if (SteerRatio < 1f) SteerRatio = 14f; // ours
            if (SteerLockDeg < 30f) SteerLockDeg = 360f; // ours
            float[] go = car.GetFloats("BASIC", "GRAPHICS_OFFSET", null);
            if (go != null && go.Length >= 2 && go[1] < -0.1f && go[1] > -1.5f) CgHeightM = -go[1];

            AcIni susp = acd.ReadIniOrEmpty("suspensions.ini");
            CgFront = Clamp(susp.GetFloat("BASIC", "CG_LOCATION", CgFront), 0.2f, 0.8f);
            WheelbaseM = Clamp(susp.GetFloat("BASIC", "WHEELBASE", WheelbaseM), 1.5f, 4f);

            ReadTyres(acd.ReadIniOrEmpty("tyres.ini"));
            ReadAero(acd, acd.ReadIniOrEmpty("aero.ini"));
            ReadEngine(acd, acd.ReadIniOrEmpty("engine.ini"));
            ReadDrivetrain(acd.ReadIniOrEmpty("drivetrain.ini"));

            AcIni brakes = acd.ReadIniOrEmpty("brakes.ini");
            BrakeTorque = brakes.GetFloat("DATA", "MAX_TORQUE", BrakeTorque);
            BrakeFrontShare = Clamp(brakes.GetFloat("DATA", "FRONT_SHARE", BrakeFrontShare), 0.3f, 0.9f);
        }

        void ReadTyres(AcIni t)
        {
            int idx = t.GetInt("COMPOUND_DEFAULT", "INDEX", 0);
            string fs = idx > 0 && t.Has("FRONT_" + idx) ? "FRONT_" + idx : "FRONT";
            string rs = idx > 0 && t.Has("REAR_" + idx) ? "REAR_" + idx : "REAR";
            if (!t.Has(fs) || !t.Has(rs)) { Problems.Add("tyres.ini has no " + fs + "/" + rs); return; }
            float wFront = MassKg * G * CgFront * 0.5f, wRear = MassKg * G * (1f - CgFront) * 0.5f;
            FrontCompound = t.Get(fs, "NAME", "");
            RearCompound = t.Get(rs, "NAME", "");
            WheelRadiusFront = t.GetFloat(fs, "RADIUS", WheelRadiusFront);
            WheelRadiusRear = t.GetFloat(rs, "RADIUS", WheelRadiusRear);
            FrictionLimitAngleFront = Math.Max(2f, Math.Min(20f, t.GetFloat(fs, "FRICTION_LIMIT_ANGLE", FrictionLimitAngleFront)));
            MuLatFront = Mu(t, fs, "DY_REF", "DY0", "LS_EXPY", wFront);
            MuLatRear = Mu(t, rs, "DY_REF", "DY0", "LS_EXPY", wRear);
            MuLongFront = Mu(t, fs, "DX_REF", "DX0", "LS_EXPX", wFront);
            MuLongRear = Mu(t, rs, "DX_REF", "DX0", "LS_EXPX", wRear);
            float hint = Ai.TyresHint;
            MuLatFront *= hint; MuLatRear *= hint; MuLongFront *= hint; MuLongRear *= hint;
            RollingN = 2f * (t.GetFloat(fs, "ROLLING_RESISTANCE_0", 10f) + t.GetFloat(rs, "ROLLING_RESISTANCE_0", 10f)); // ours default 10 N
            WheelInertiaFront = Math.Max(0f, t.GetFloat(fs, "ANGULAR_INERTIA", 0f));
            WheelInertiaRear = Math.Max(0f, t.GetFloat(rs, "ANGULAR_INERTIA", 0f));
        }

        float Mu(AcIni t, string sec, string refKey, string oldKey, string expKey, float load)
        {
            float d = t.GetFloat(sec, refKey, 0f);
            if (d <= 0f) d = t.GetFloat(sec, oldKey, 0f);
            if (d <= 0f) { Problems.Add(sec + " " + refKey + " missing"); return 1.1f; } // ours
            float fz0 = t.GetFloat(sec, "FZ0", 0f), e = t.GetFloat(sec, expKey, 1f);
            if (fz0 > 0f && load > 0f && e > 0f) d *= (float)Math.Pow(load / fz0, e - 1f); // AC load sensitivity: D = DREF * (Fz/FZ0)^(LS_EXP-1) (research 1.10)
            return Clamp(d, 0.3f, 3f);
        }

        void ReadAero(AcdArchive acd, AcIni a)
        {
            float cl = 0f, cd = 0f; int wings = 0;
            for (int n = 0; n < 32; n++)
            {
                var s = a["WING_" + n];
                if (s == null) { if (n > 2) break; continue; }
                wings++;
                float area = s.GetFloat("CHORD", 0f) * s.GetFloat("SPAN", 0f);
                float angle = s.GetFloat("ANGLE", 0f);
                AcLut lcl = acd.ReadLut(s.Get("LUT_AOA_CL", null)), lcd = acd.ReadLut(s.Get("LUT_AOA_CD", null));
                if (lcl != null && !lcl.IsEmpty) cl += lcl.Eval(angle) * s.GetFloat("CL_GAIN", 0f) * area;
                if (lcd != null && !lcd.IsEmpty) cd += lcd.Eval(angle) * s.GetFloat("CD_GAIN", 0f) * area;
            }
            if (wings == 0) Problems.Add("aero.ini has no wings (default drag)");
            else { ClA = Math.Max(0f, cl) * Ai.AeroHint; if (cd > 0.05f) CdA = cd; }
        }

        void ReadEngine(AcdArchive acd, AcIni e)
        {
            IdleRpm = e.GetFloat("ENGINE_DATA", "MINIMUM", IdleRpm);
            EngineInertia = Math.Max(0f, e.GetFloat("ENGINE_DATA", "INERTIA", 0f));
            float lim = e.GetFloat("ENGINE_DATA", "LIMITER", 0f);
            AcLut pl = acd.ReadLut(e.Get("HEADER", "POWER_CURVE", "power.lut"));
            if (pl == null || pl.IsEmpty) { Problems.Add("no power curve"); pl = new AcLut(new[] { 1000f, 7000f }, new[] { 150f, 150f }); } // ours
            Torque = pl;
            LimiterRpm = lim > 0f ? lim : pl.MaxX;
            for (int n = 0; n < 8; n++)
            {
                var s = e["TURBO_" + n];
                if (s == null) continue;
                float b = s.GetFloat("MAX_BOOST", 0f), wg = s.GetFloat("WASTEGATE", 0f);
                if (wg > 0f && wg < b) b = wg;
                if (n == 0 || BoostMax == 0f) { BoostRefRpm = Math.Max(1f, s.GetFloat("REFERENCE_RPM", 3000f)); BoostGamma = s.GetFloat("GAMMA", 1f); }
                BoostMax += b;
            }
        }

        void ReadDrivetrain(AcIni d)
        {
            Traction = (d.Get("TRACTION", "TYPE", "RWD") ?? "RWD").Trim().ToUpperInvariant();
            if (Traction != "FWD" && Traction != "AWD" && Traction != "AWD2") Traction = "RWD";
            if (Traction == "AWD2") Traction = "AWD";
            int count = d.GetInt("GEARS", "COUNT", 0);
            var g = new List<float>();
            for (int i = 1; i <= Math.Max(count, 1) && i < 16; i++)
            {
                float r = d.GetFloat("GEARS", "GEAR_" + i, 0f);
                if (r <= 0f) break;
                g.Add(r);
            }
            if (g.Count == 0) { Problems.Add("drivetrain.ini has no gears"); g.Add(3f); g.Add(2f); g.Add(1.4f); g.Add(1.1f); g.Add(0.9f); } // ours
            Gears = g.ToArray();
            FinalRatio = d.GetFloat("GEARS", "FINAL", FinalRatio);
            GearboxInertia = Math.Max(0f, d.GetFloat("GEARBOX", "INERTIA", 0f));
            ReverseRatio = d.GetFloat("GEARS", "GEAR_R", ReverseRatio);
        }

        // ---------------------------------------------------------------- capability queries (planning)

        /// <summary>Wheel torque (Nm) at <paramref name="rpm"/>, full throttle, boost included.</summary>
        public float WheelTorqueAt(float rpm)
        {
            float t = Torque.Eval(rpm);
            if (BoostMax > 0f) t *= 1f + BoostMax * (float)Math.Pow(Math.Min(1f, Math.Max(0f, rpm) / BoostRefRpm), BoostGamma);
            return t;
        }

        /// <summary>Engine rpm in gear <paramref name="gear"/> (1-based) at road speed <paramref name="v"/> m/s.</summary>
        public float RpmAt(float v, int gear)
        {
            if (gear < 1 || gear > Gears.Length) return 0f;
            return v / DrivenRadius * Gears[gear - 1] * FinalRatio * 60f / (2f * (float)Math.PI);
        }

        /// <summary>Best tractive force (N) over the gears at speed <paramref name="v"/>, shifting at ai.ini UP (or the limiter).
        /// Below the first gear's idle speed the clutch slips: torque at max(rpm, 0.6 x UP) // ours launch rpm.</summary>
        public float DriveForce(float v)
        {
            float best = 0f, shift = Math.Min(Ai.UpRpm, LimiterRpm);
            if (shift < IdleRpm + 500f) shift = LimiterRpm;
            for (int g = 1; g <= Gears.Length; g++)
            {
                float rpm = RpmAt(v, g);
                if (rpm > shift && g < Gears.Length) continue;
                if (rpm > LimiterRpm) continue;
                if (g == 1) rpm = Math.Max(rpm, 0.6f * shift); // ours: launch with clutch slip
                else if (rpm < IdleRpm) continue;
                float f = WheelTorqueAt(rpm) * Gears[g - 1] * FinalRatio / DrivenRadius;
                if (f > best) best = f;
            }
            return best;
        }

        /// <summary>Mass (kg) the drive force accelerates in gear <paramref name="gear"/>: the car plus the engine + gearbox inertia through the
        /// gear and the four wheels' inertia, each over its radius squared (a = F / m_eq).</summary>
        public float EquivalentMassKg(int gear)
        {
            float m = MassKg;
            float rf = Math.Max(0.1f, WheelRadiusFront), rr = Math.Max(0.1f, WheelRadiusRear), rd = Math.Max(0.1f, DrivenRadius);
            m += 2f * (WheelInertiaFront / (rf * rf) + WheelInertiaRear / (rr * rr));
            if (gear >= 1 && gear <= Gears.Length)
            {
                float ratio = Gears[gear - 1] * FinalRatio;
                m += (EngineInertia + GearboxInertia) * ratio * ratio / (rd * rd);
            }
            return m;
        }

        /// <summary>Best forward acceleration (m/s^2, before drag) over the gears at speed v: <see cref="DriveForce"/>'s rules, each gear's force
        /// over <see cref="EquivalentMassKg"/> (a low gear pulls harder but also spins the engine up faster).</summary>
        public float DriveAccel(float v)
        {
            float best = 0f, shift = Math.Min(Ai.UpRpm, LimiterRpm);
            if (shift < IdleRpm + 500f) shift = LimiterRpm;
            for (int g = 1; g <= Gears.Length; g++)
            {
                float rpm = RpmAt(v, g);
                if (rpm > shift && g < Gears.Length) continue;
                if (rpm > LimiterRpm) continue;
                if (g == 1) rpm = Math.Max(rpm, 0.6f * shift); // ours: launch with clutch slip (as DriveForce)
                else if (rpm < IdleRpm) continue;
                float a = WheelTorqueAt(rpm) * Gears[g - 1] * FinalRatio / DrivenRadius / EquivalentMassKg(g);
                if (a > best) best = a;
            }
            return best;
        }

        /// <summary>Engine rpm the AI holds at speed <paramref name="v"/>: the lowest gear under its UP rpm (ai.ini; else the limiter), idle at least.</summary>
        public float HoldRpm(float v)
        {
            float shift = Math.Min(Ai.UpRpm, LimiterRpm);
            if (shift < IdleRpm + 500f) shift = LimiterRpm;
            for (int g = 1; g <= Gears.Length; g++)
            {
                float rpm = RpmAt(v, g);
                if (rpm <= shift || g == Gears.Length) return Math.Max(IdleRpm, Math.Min(rpm, LimiterRpm));
            }
            return IdleRpm;
        }

        /// <summary>Aerodynamic downforce (N) at <paramref name="v"/>.</summary>
        public float Downforce(float v) { return 0.5f * AirDensity * ClA * v * v; }

        /// <summary>Aerodynamic drag (N) at <paramref name="v"/>.</summary>
        public float Drag(float v) { return 0.5f * AirDensity * CdA * v * v; }

        /// <summary>Largest deceleration (m/s^2) the brakes can make, ignoring grip (MAX_TORQUE per wheel [BELIEF]).</summary>
        public float BrakeDecelMax { get { return 2f * BrakeTorque * (1f / WheelRadiusFront * BrakeFrontShare + 1f / WheelRadiusRear * (1f - BrakeFrontShare)) / Math.Max(100f, MassKg); } }

        /// <summary>Top speed (m/s) where tractive force = drag + rolling (bisection).</summary>
        public float TopSpeed()
        {
            float lo = 1f, hi = 150f;
            for (int i = 0; i < 40; i++)
            {
                float m = 0.5f * (lo + hi);
                if (DriveForce(m) > Drag(m) + RollingN) lo = m; else hi = m;
            }
            return lo;
        }

        static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        public override string ToString()
        {
            return CarId + ": " + MassKg.ToString("0") + " kg, mu lat " + MuLatFront.ToString("0.00") + "/" + MuLatRear.ToString("0.00")
                + " long " + MuLongFront.ToString("0.00") + "/" + MuLongRear.ToString("0.00") + " (" + RearCompound + "), ClA " + ClA.ToString("0.00")
                + " CdA " + CdA.ToString("0.00") + ", " + Traction + " " + Gears.Length + " gears, limiter " + LimiterRpm + ", boost " + BoostMax.ToString("0.00")
                + ", top " + (TopSpeed() * 3.6f).ToString("0") + " km/h, brakes " + (BrakeDecelMax / G).ToString("0.00") + " g, wb " + WheelbaseM
                + ", steer " + SteerLockDeg + "/" + SteerRatio + (Problems.Count > 0 ? ", problems: " + string.Join("; ", Problems) : "");
        }
    }
}
