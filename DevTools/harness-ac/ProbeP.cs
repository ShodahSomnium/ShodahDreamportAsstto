using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumAssetto;

// Module P (physics) probes. Run: dotnet run -c Release --no-build -- <cmd> ...
//   pgrep <entry> <regex>     every owned car: lines of data.acd <entry> matching the regex (census of keys / values)
//   pfiles [prefix]           distinct data.acd entry names over the owned cars
//   spec [car]                AcCarSpec census over every owned car (or one car in detail): mass, limiter, gears, steer, tyres, problems
//   psim [car|all]            straight-line + skidpad validation with the real AcCarCore: shift in 1st, 0-100, top speed, 100-0, lateral g
static partial class Program
{
    static readonly CultureInfo PInv = CultureInfo.InvariantCulture;

    static int Cmd_pgrep(List<string> a)
    {
        if (a.Count < 3) { Console.WriteLine("pgrep <entry> <regex>"); return 1; }
        var rx = new System.Text.RegularExpressions.Regex(a[2], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var c = AcContent.Scan(_install);
        foreach (var car in c.Cars)
        {
            if (!car.Owned) continue;
            AcdArchive acd;
            try { acd = AcdArchive.Open(car.Dir); } catch (Exception e) { Console.WriteLine(car.Id + ": open failed " + e.Message); continue; }
            if (acd == null) { Console.WriteLine(car.Id + ": no data"); continue; }
            string t = acd.ReadText(a[1]);
            if (t == null) { Console.WriteLine(car.Id + ": (no " + a[1] + ")"); continue; }
            string section = "";
            foreach (var raw in t.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string tl = line.Trim();
                if (tl.StartsWith("[")) section = tl;
                if (rx.IsMatch(line)) Console.WriteLine(car.Id + " " + section + " " + tl);
            }
        }
        return 0;
    }

    static int Cmd_pfiles(List<string> a)
    {
        string prefix = a.Count > 1 ? a[1] : "";
        var counts = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var c = AcContent.Scan(_install);
        foreach (var car in c.Cars)
        {
            if (!car.Owned) continue;
            var acd = AcdArchive.Open(car.Dir);
            if (acd == null) continue;
            foreach (var n in acd.Names)
            {
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                List<string> l; if (!counts.TryGetValue(n, out l)) counts[n] = l = new List<string>();
                l.Add(car.Id);
            }
        }
        foreach (var kv in counts) Console.WriteLine(kv.Value.Count.ToString().PadLeft(3) + " " + kv.Key + (kv.Value.Count <= 8 ? "  (" + string.Join(" ", kv.Value) + ")" : ""));
        return 0;
    }

    static string F(float v, string fmt = "0.##") { return float.IsNaN(v) ? "-" : v.ToString(fmt, PInv); }

    static int Cmd_spec(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string only = a.Count > 1 ? a[1] : null;
        int n = 0, withProblems = 0, problemTotal = 0;
        var problemKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (only != null && !car.Id.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) { Console.WriteLine(car.Id.PadRight(36) + " NO DATA"); continue; }
            n++;
            AcTyreCompound c = s.Compound(s.DefaultCompound);
            float boostMax = 0f;
            foreach (var t in s.Engine.Turbos) boostMax += t.Wastegate > 0f ? Math.Min(t.Wastegate, t.MaxBoost) : t.MaxBoost;
            float peakT = s.Engine.Power.MaxY * (1f + boostMax);
            Console.WriteLine(car.Id.PadRight(36) + " " + F(s.TotalMassKg, "0").PadLeft(5) + "kg lim " + F(s.LimiterRpm, "0").PadLeft(5) +
                " " + s.Traction.PadRight(4) + " g" + s.GearCount + " fin " + F(s.FinalRatio, "0.00") + " steer " + F(s.SteerLockDeg, "0") + "/" + F(s.SteerRatio, "0.0") +
                " R " + F(c.Front.Radius, "0.000") + "/" + F(c.Rear.Radius, "0.000") + " " + (c.ShortName.Length > 0 ? c.ShortName : c.Name).PadRight(4) +
                " DX/DY " + F(c.Front.DxRef, "0.00") + "/" + F(c.Front.DyRef, "0.00") + " Tpk " + F(peakT, "0") + (boostMax > 0 ? "(t" + s.Engine.Turbos.Count + ")" : "") +
                " ui " + F(car.Specs.TorqueNm, "0") + " wings " + s.Wings.Count + " cmp " + s.Compounds.Count + " setup " + s.Setup.Count + " P" + s.Problems.Count);
            if (s.Problems.Count > 0) withProblems++;
            problemTotal += s.Problems.Count;
            foreach (string p in s.Problems)
            {
                string kind = System.Text.RegularExpressions.Regex.Replace(p, @"\[[^\]]*\]|'[^']*'|[-0-9.]+", "#");
                int k; problemKinds.TryGetValue(kind, out k); problemKinds[kind] = k + 1;
                if (only != null) Console.WriteLine("   problem: " + p);
            }
            if (only != null)
            {
                Console.WriteLine("   " + s.Summary());
                Console.WriteLine("   engine inertia " + F(s.Engine.Inertia, "0.000") + " limiterHz " + F(s.Engine.LimiterHz) + " coast " + F(s.Engine.CoastTorque) + "@" + F(s.Engine.CoastRpm) + " throttle.lut " + (s.Engine.ThrottleLut != null));
                foreach (var t in s.Engine.Turbos) Console.WriteLine("   turbo max " + F(t.MaxBoost) + " wg " + F(t.Wastegate) + " ref " + F(t.ReferenceRpm) + " gamma " + F(t.Gamma) + " lag " + F(t.LagUp, "0.000") + "/" + F(t.LagDn, "0.000"));
                var d = s.Drivetrain;
                Console.WriteLine("   diff " + d.Diff + " awd front " + F(d.AwdFrontSharePct) + "% F" + d.FrontDiff + " C" + d.CentreDiff + " R" + d.RearDiff + " awd2 ramp " + F(d.CentreRampTorque) + " max " + F(d.CentreMaxTorque));
                Console.WriteLine("   gearbox up/dn " + F(d.ChangeUpMs) + "/" + F(d.ChangeDnMs) + " ms cutoff " + F(d.AutoCutoffMs) + " clutch " + F(d.ClutchMaxTorque) + " autoclutch " + F(d.AutoClutchMinRpm) + "-" + F(d.AutoClutchMaxRpm) +
                    " forced " + d.AutoClutchForcedOn + " blip " + d.AutoBlipElectronic + " " + F(d.BlipP0) + "/" + F(d.BlipP1) + "/" + F(d.BlipP2) + " L" + F(d.BlipLevel) + " auto " + F(d.AutoShiftUp) + "/" + F(d.AutoShiftDown));
                foreach (var ax in new[] { s.Front, s.Rear })
                    Console.WriteLine("   axle z " + F(ax.AxleZ, "0.000") + " basey " + F(ax.BaseY, "0.000") + " track " + F(ax.Track, "0.000") + " k " + F(ax.SpringRate, "0") + " prog " + F(ax.ProgressiveRate, "0") + " arb " + F(ax.ArbRate, "0") +
                        " bump " + F(ax.DampBump, "0") + "/" + F(ax.DampFastBump, "0") + " reb " + F(ax.DampRebound, "0") + "/" + F(ax.DampFastRebound, "0") + " stops " + F(ax.BumpStopUp, "0.000") + "/" + F(ax.BumpStopDn, "0.000") + " packer " + F(ax.PackerRange, "0.000") + " camber " + F(ax.StaticCamberDeg));
                foreach (var w in s.Wings)
                    Console.WriteLine("   wing " + w.Index + " " + w.Name + " pos " + w.Position + " area " + F(w.Area, "0.000") + " cl(" + F(w.Angle) + ")=" + (w.AoaCl != null ? F(w.AoaCl.Eval(w.Angle) * w.ClGain, "0.000") : "-") +
                        " cd=" + (w.AoaCd != null ? F(w.AoaCd.Eval(w.Angle) * w.CdGain, "0.000") : "-") + (w.Controller != null ? " ctrl " + w.Controller.Count : ""));
                Console.WriteLine("   brakes " + F(s.Brakes.MaxTorque) + " front " + F(s.Brakes.FrontShare) + " hand " + F(s.Brakes.HandbrakeTorque) + " | ABS " + s.Abs.Present + "/" + s.Abs.Active + " " + F(s.Abs.SlipRatioLimit) + "@" + F(s.Abs.RateHz) +
                    " | TC " + s.Tc.Present + "/" + s.Tc.Active + " " + F(s.Tc.SlipRatioLimit) + "@" + F(s.Tc.RateHz) + " min " + F(s.Tc.MinSpeedKmh) + " | ctrl lock " + (s.CtrlSingleLock != null) + " awd2 " + (s.CtrlAwd2 != null) + " ebb " + (s.CtrlEbb != null));
                foreach (var cp in s.Compounds) Console.WriteLine("   compound " + cp + " F: R " + F(cp.Front.Radius, "0.000") + " I " + F(cp.Front.AngularInertia) + " DX " + F(cp.Front.DxRef) + " DY " + F(cp.Front.DyRef) + " FZ0 " + F(cp.Front.Fz0, "0") + " LS " + F(cp.Front.LsExpX, "0.000") + "/" + F(cp.Front.LsExpY, "0.000") +
                    " peak " + F(cp.Front.FrictionLimitAngle) + " falloff " + F(cp.Front.FalloffLevel) + "/" + F(cp.Front.FalloffSpeed) + " relax " + F(cp.Front.RelaxationLength, "0.000") + " perf " + F(cp.Front.PerformancePeak) + " wear " + (cp.Front.WearCurve != null));
                foreach (var it in s.Setup) Console.WriteLine("   setup " + it + (it.Ratios != null ? " ratios " + it.Ratios.Count : ""));
                foreach (var cb in s.Colliders) Console.WriteLine("   collider " + cb.Centre + " size " + cb.Size + " ground " + cb.GroundEnable);
            }
        }
        Console.WriteLine("cars " + n + ", with problems " + withProblems + ", problems " + problemTotal + ", " + sw.ElapsedMilliseconds + " ms");
        foreach (var kv in problemKinds) Console.WriteLine("  " + kv.Value.ToString().PadLeft(3) + "x " + kv.Key);
        return 0;
    }

    // ------------------------------------------------------------------ validation sim (straight line + skidpad), the real AcCarCore

    sealed class SimResult
    {
        public float Shift1 = float.NaN, T100 = float.NaN, T200 = float.NaN, Top = float.NaN, Brake100 = float.NaN, LatG = float.NaN, LatG200 = float.NaN;
        /// <summary>0-100 split (s): clutch slipping (launch), gear change (cut / disconnected), TC cutting, traction-limited (driven slip past
        /// 85 % of the tyre peak), power-limited (the rest); launch rpm (engine rpm while the clutch slipped, mean).</summary>
        public float TClutch, TShift, TTc, TTraction, TPower, LaunchRpm, PeakSlip;
    }

    static void SimAero(AcCarSpec s, float v, float cgH, out float dragN, out float downF, out float downR)
    {
        dragN = 0f; downF = 0f; downR = 0f;
        float L = s.WheelbaseM, zr = s.Rear.AxleZ;
        foreach (var w in s.Wings)
        {
            float df, dr;
            AcAeroModel.Wing(w, v, 0f, AcAeroModel.GroundHeight(s, w, cgH, cgH), 0f, w.Angle, out df, out dr);
            dragN += dr;
            float frontShare = Math.Max(-0.5f, Math.Min(1.5f, (w.Position.Z - zr) / L));
            downF += df * frontShare; downR += df * (1f - frontShare);
        }
    }

    static SimResult Simulate(AcCarSpec s, bool verbose)
    {
        var r = new SimResult();
        const float g = 9.81f, h = 1f / 333f;
        // polish-pass test hooks (env, harness only): PSIM_SHIFT0=1 instant shifts, PSIM_EI=<scale> engine + gearbox inertia,
        // PSIM_WI=<scale> wheel inertia, PSIM_LAUNCH=<rpm> autoclutch window MIN=MAX-1=rpm, PSIM_DX=<scale> tyre DX_REF, PSIM_CLUTCH=<Nm>
        if (Environment.GetEnvironmentVariable("PSIM_SHIFT0") == "1") { s.Drivetrain.ChangeUpMs = 10f; s.Drivetrain.AutoCutoffMs = 0f; s.Drivetrain.AutoShiftGasCutoffS = 0f; }
        string ev = Environment.GetEnvironmentVariable("PSIM_EI");
        if (!string.IsNullOrEmpty(ev)) { float k = float.Parse(ev, PInv); s.Engine.Inertia *= k; s.Drivetrain.GearboxInertia *= k; }
        ev = Environment.GetEnvironmentVariable("PSIM_LAUNCH");
        if (!string.IsNullOrEmpty(ev)) { float k = float.Parse(ev, PInv); s.Drivetrain.AutoClutchMinRpm = k - 1f; s.Drivetrain.AutoClutchMaxRpm = k; }
        ev = Environment.GetEnvironmentVariable("PSIM_CLUTCH");
        if (!string.IsNullOrEmpty(ev)) s.Drivetrain.ClutchMaxTorque = float.Parse(ev, PInv);
        ev = Environment.GetEnvironmentVariable("PSIM_WI");
        float wiScale = string.IsNullOrEmpty(ev) ? 1f : float.Parse(ev, PInv);
        ev = Environment.GetEnvironmentVariable("PSIM_DX");
        float dxScale = string.IsNullOrEmpty(ev) ? 1f : float.Parse(ev, PInv);
        float m = s.TotalMassKg + s.FuelLitres * 0.75f; // 0.75 kg/l // ours
        float cgH = s.PhysicsCgHeightM, L = s.WheelbaseM;
        var core = new AcCarCore(s); core.Damage = HarnessDamage(s); // iteration 2: damage state at PDMG % (default 0 = off)
        core.AutoShift = true; core.AutoClutch = true; core.AbsMode = 1; core.TcMode = Environment.GetEnvironmentVariable("PSIM_TC") == "0" ? 0 : 1;
        for (int i = 0; i < 4; i++) { core.Tyre[i].Inertia *= wiScale; core.Tyre[i].DxRef *= dxScale; }
        bool human = Environment.GetEnvironmentVariable("PSIM_HUMAN") == "1", humanDone = false;
        ev = Environment.GetEnvironmentVariable("PSIM_GAS"); // pedal cap (lab physics-458-magione drives at 0.6)
        float gasCap = string.IsNullOrEmpty(ev) ? 1f : float.Parse(ev, PInv); float x6 = float.NaN, v6 = 0f, dist6 = 0f;
        float humanClutch = 1f, humanRpm = s.Engine.Limiter * 0.55f; // harness test driver only // ours
        if (human) { core.AutoClutch = false; s.Drivetrain.AutoClutchForcedOn = false; }
        float cam0 = Math.Abs(s.Front.StaticCamberDeg) * AcTyreModel.Deg2Rad, cam1 = Math.Abs(s.Rear.StaticCamberDeg) * AcTyreModel.Deg2Rad;
        float v = 0f, a = 0f, t = 0f, gas = 1f;
        int lastGear = 0;
        bool shifted = false;
        int di = core.Driven(2) ? 2 : 0;
        float peakSlip = core.Tyre[di].TanPeak / Math.Max(0.1f, core.Tyre[di].CxMult);
        for (int step = 0; step < 333 * 150; step++)
        {
            float drag, dF, dR;
            SimAero(s, v, cgH, out drag, out dF, out dR);
            float transfer = m * a * cgH / L;
            float fl = (m * g * s.CgLocation + dF - transfer) * 0.5f, rl = (m * g * (1f - s.CgLocation) + dR + transfer) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                core.Load[i] = Math.Max(0f, i < 2 ? fl : rl);
                core.VLong[i] = v; core.VLat[i] = 0f; core.Grip[i] = 1f;
                float cam = i < 2 ? cam0 : cam1;
                core.Camber[i] = (i & 1) == 0 ? cam : -cam;
            }
            core.Speed = v; core.LongG = a / g;
            // the test driver: full gas, but holds the driven-wheel slip near the tyre's peak (a good launch; TC is off below its MIN_SPEED)
            float worst = 0f;
            for (int i = 0; i < 4; i++) if (core.Driven(i) && core.SlipRatio[i] > worst) worst = core.SlipRatio[i];
            if (worst > peakSlip * 1.1f) gas = Math.Max(0.2f, gas - 0.02f); else gas = Math.Min(1f, gas + 0.01f);
            if (human)
            {
                // PSIM_HUMAN=1 (harness only): a manual launch - full gas, the clutch pedal slipped so the engine holds launchRpm and the driven
                // slip stays under the tyre peak, then released for good once the clutch is (nearly) locked
                if (!humanDone && core.Gear == 1)
                {
                    float wheelRpm = 0f; int nd = 0;
                    for (int i = 0; i < 4; i++) if (core.Driven(i)) { wheelRpm += core.Omega[i]; nd++; }
                    wheelRpm = wheelRpm / Math.Max(1, nd) * core.TotalRatio(1) * 9.549297f;
                    if (worst > peakSlip) humanClutch += 0.01f;
                    else if (core.Rpm < humanRpm) humanClutch += 0.004f;
                    else humanClutch -= 0.01f;
                    humanClutch = Math.Max(0f, Math.Min(1f, humanClutch));
                    if (wheelRpm > core.Rpm * 0.97f && v > 2f) { humanDone = true; humanClutch = 0f; }
                    gas = Math.Max(gas, worst > peakSlip * 1.1f ? 0.2f : 1f);
                }
                core.Clutch = humanDone ? 0f : humanClutch;
            }
            core.Gas = Math.Min(gas, gasCap); core.Brake = 0f;
            core.Step(h);
            float fx = core.Fx[0] + core.Fx[1] + core.Fx[2] + core.Fx[3];
            a = (fx - drag) / m;
            v += a * h; if (v < 0f) v = 0f;
            t += h;
            if (!shifted && lastGear == 1 && core.Gear == 2) { shifted = true; r.Shift1 = t; }
            if (float.IsNaN(r.T100))
            {
                if (core.Shifting) r.TShift += h;
                else if (core.ClutchEngage < 0.98f) { r.TClutch += h; r.LaunchRpm += core.Rpm * h; }
                else if (core.TcActive) r.TTc += h;
                else if (worst > peakSlip * 0.85f) r.TTraction += h;
                else r.TPower += h;
            }
            lastGear = core.Gear;
            if (float.IsNaN(r.T100) && v >= 100f / 3.6f) r.T100 = t;
            dist6 += v * h; if (float.IsNaN(x6) && t >= 6f) { x6 = dist6; v6 = v; if (gasCap < 1f) Console.WriteLine("   6 s at pedal " + F(gasCap) + ": " + F(x6, "0") + " m, " + F(v6 * 3.6f, "0") + " km/h, gear " + core.Gear); }
            if (float.IsNaN(r.T200) && v >= 200f / 3.6f) r.T200 = t;
            if (verbose && (step % 333 == 0 && t < 12f || step % 3330 == 0 || (Environment.GetEnvironmentVariable("PSIM_FINE") == "1" && step % 17 == 0 && t < 4.2f)))
                Console.WriteLine("   t " + F(t, "0.0") + " v " + F(v * 3.6f, "0.0") + " gear " + core.Gear + " rpm " + F(core.Rpm, "0") + " gas " + F(core.GasEffective) + " boost " + F(core.BoostTotal) +
                    " slipR " + F(core.SlipRatio[di], "0.000") + " Fx " + F(fx, "0") + " drag " + F(drag, "0") + " clutch " + F(core.ClutchEngage) + " tc " + core.TcActive + " lim " + core.Limiter + " Te " + F(core.EngineTorque, "0") + " Tc " + F(core.ClutchTorque, "0") + " Td " + F(core.DriveTorque[di], "0") + " FxD " + F(core.Fx[di], "0") + " FzD " + F(core.Load[di], "0") + " FxF " + F(core.Fx[0], "0"));
        }
        r.Top = v * 3.6f;
        r.LaunchRpm = r.TClutch > 0f ? r.LaunchRpm / r.TClutch : 0f; r.PeakSlip = peakSlip;

        // braking from 100 km/h, ABS factory
        core = new AcCarCore(s); core.Damage = HarnessDamage(s); core.AbsMode = 1; core.AutoShift = true; core.AutoClutch = true;
        v = 100f / 3.6f; a = 0f; float x = 0f;
        for (int i = 0; i < 4; i++) core.Omega[i] = v / core.Tyre[i].Radius;
        for (int step = 0; step < 333 * 20 && v > 0.3f; step++)
        {
            float drag, dF, dR;
            SimAero(s, v, cgH, out drag, out dF, out dR);
            float transfer = m * a * cgH / L;
            float fl = (m * g * s.CgLocation + dF - transfer) * 0.5f, rl = (m * g * (1f - s.CgLocation) + dR + transfer) * 0.5f;
            for (int i = 0; i < 4; i++) { core.Load[i] = Math.Max(0f, i < 2 ? fl : rl); core.VLong[i] = v; core.VLat[i] = 0f; }
            core.Speed = v; core.Gas = 0f; core.Brake = 1f;
            core.Step(h);
            float fx = core.Fx[0] + core.Fx[1] + core.Fx[2] + core.Fx[3];
            a = (fx - drag) / m;
            v += a * h; x += v * h;
            if (verbose && step % 100 == 0) Console.WriteLine("   brake t " + F(step * h, "0.00") + " v " + F(v * 3.6f, "0.0") + " a " + F(a / g) + "g slip " + F(core.SlipRatio[0], "0.000") + "/" + F(core.SlipRatio[2], "0.000") + " abs " + core.AbsActive);
        }
        r.Brake100 = x;
        r.LatG = Skidpad(s, m, 100f / 3.6f);
        r.LatG200 = Skidpad(s, m, 200f / 3.6f);
        return r;
    }

    /// <summary>Steady-state lateral limit (g) at a speed: per axle peak Fy of both tyres with lateral load transfer split by roll stiffness
    /// (springs T^2/2 + ARB T^2), aero downforce; the axle that saturates first sets the limit.</summary>
    static float Skidpad(AcCarSpec s, float m, float v)
    {
        const float g = 9.81f;
        var core = new AcCarCore(s);
        float drag, dF, dR;
        SimAero(s, v, s.PhysicsCgHeightM, out drag, out dF, out dR);
        float kf = s.Front.SpringRate * s.Front.Track * s.Front.Track * 0.5f + s.Front.ArbRate * s.Front.Track * s.Front.Track;
        float kr = s.Rear.SpringRate * s.Rear.Track * s.Rear.Track * 0.5f + s.Rear.ArbRate * s.Rear.Track * s.Rear.Track;
        float fShare = kf / Math.Max(1f, kf + kr);
        float best = 0f;
        for (float ay = 0.3f; ay < 3.5f; ay += 0.005f)
        {
            float moment = m * ay * g * s.PhysicsCgHeightM;
            float dFzF = moment * fShare / s.Front.Track, dFzR = moment * (1f - fShare) / s.Rear.Track;
            float sf = (m * g * s.CgLocation + dF) * 0.5f, sr = (m * g * (1f - s.CgLocation) + dR) * 0.5f;
            float capF = PeakFy(core, 0, sf + dFzF) + PeakFy(core, 1, Math.Max(0f, sf - dFzF));
            float capR = PeakFy(core, 2, sr + dFzR) + PeakFy(core, 3, Math.Max(0f, sr - dFzR));
            float needF = m * ay * g * s.CgLocation, needR = m * ay * g * (1f - s.CgLocation);
            if (capF < needF || capR < needR) break;
            best = ay;
        }
        return best;
    }

    static float PeakFy(AcCarCore core, int i, float load)
    {
        if (load <= 0f) return 0f;
        float fx, fy, kx, ky, comb;
        AcTyreModel.Forces(ref core.Tyre[i], load, 0f, core.Tyre[i].TanPeak, 1f, 0f, 0f, out fx, out fy, out kx, out ky, out comb);
        return Math.Abs(fy);
    }

    /// <summary>Polish pass: the physics-ideal 0-100 from the car's data alone (no core model): at each speed the best gear's wheel force
    /// power.lut(rpm) * (1 + max boost) * ratio / r (lut = wheel torque, no extra loss), capped by the driven tyres' peak Fx (DX_REF, LS_EXPX,
    /// the load with longitudinal transfer, camber off), minus aero drag and rolling resistance, over the mass + every rotating inertia
    /// reflected (wheels, gearbox, engine * ratio^2) / r^2; launch at the traction limit, instant shifts. pideal &lt;car|all&gt;.</summary>
    static float IdealT100(AcCarSpec s, out float t200, out float meanG100)
    {
        const float g = 9.81f, dv = 0.02f;
        float m = s.TotalMassKg + s.FuelLitres * 0.75f, cgH = s.PhysicsCgHeightM, L = s.WheelbaseM;
        var core = new AcCarCore(s);
        AcDrivetrainSpec d = s.Drivetrain;
        float boostMax = 0f;
        foreach (var tb in s.Engine.Turbos) boostMax += Math.Min(tb.MaxBoost, tb.Wastegate > 0f ? tb.Wastegate : tb.MaxBoost);
        float rD = core.Driven(2) ? core.Tyre[2].Radius : core.Tyre[0].Radius;
        float iw = core.Tyre[0].Inertia * 2f / (core.Tyre[0].Radius * core.Tyre[0].Radius) + core.Tyre[2].Inertia * 2f / (core.Tyre[2].Radius * core.Tyre[2].Radius);
        float lim = s.Engine.Limiter > 0f ? s.Engine.Limiter : 99999f;
        float t = 0f, v = 0f, a = 0f; t200 = float.NaN; float t100 = float.NaN;
        while (v < 200f / 3.6f && t < 120f)
        {
            float drag, dF, dR;
            SimAero(s, v, cgH, out drag, out dF, out dR);
            float transfer = m * a * cgH / L;
            float fl = (m * g * s.CgLocation + dF - transfer) * 0.5f, rl = (m * g * (1f - s.CgLocation) + dR + transfer) * 0.5f;
            float cap = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (!core.Driven(i)) continue;
                float load = Math.Max(0f, i < 2 ? fl : rl);
                var tt = core.Tyre[i];
                cap += tt.DxRef * (float)Math.Pow(Math.Max(0.05f, load / tt.Fz0), tt.LsX1) * tt.Perf * load;
            }
            float rr = 0f;
            for (int i = 0; i < 4; i++) rr += AcTyreModel.RollingResistance(ref core.Tyre[i], Math.Max(0f, i < 2 ? fl : rl), v, 0f);
            float best = -1e9f, bestM = m;
            for (int gi = 1; gi <= d.Gears.Length; gi++)
            {
                float ratio = core.TotalRatio(gi);
                float rpm = Math.Max(v / rD * ratio * 9.549297f, d.AutoClutchMaxRpm);
                if (rpm > lim) continue;
                float fw = Math.Min(cap, s.Engine.TorqueAt(rpm) * (1f + boostMax) * ratio / rD);
                float me = m + iw + (s.Engine.Inertia + d.GearboxInertia) * ratio * ratio / (rD * rD);
                float acc = (fw - drag - rr) / me;
                if (acc > best) { best = acc; bestM = me; }
            }
            if (best <= 0.01f) break;
            a = best;
            t += dv / a; v += dv;
            if (float.IsNaN(t100) && v >= 100f / 3.6f) t100 = t;
        }
        if (v >= 200f / 3.6f) t200 = t;
        meanG100 = float.IsNaN(t100) ? 0f : 100f / 3.6f / t100 / g;
        return t100;
    }

    static int Cmd_pideal(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string only = a.Count > 1 ? a[1] : "ferrari_458";
        var ratios = new List<float>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (only != "all" && !car.Id.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) continue;
            float t200, mg;
            float t100 = IdealT100(s, out t200, out mg);
            float ui = car.Specs.Acceleration0100;
            Console.WriteLine(car.Id.PadRight(36) + " ideal 0-100 " + F(t100, "0.00").PadLeft(5) + "s (ui " + F(ui, "0.0").PadLeft(4) + ", ratio " + (ui > 1f ? F(t100 / ui, "0.00") : "-") + ")  0-200 " + F(t200, "0.0") + "s  mean " + F(mg, "0.00") + " g");
            if (ui > 1f && !float.IsNaN(t100)) ratios.Add(t100 / ui);
        }
        if (ratios.Count > 1)
        {
            float sum = 0f; foreach (float x in ratios) sum += x;
            ratios.Sort();
            Console.WriteLine("fleet ideal 0-100 vs ui_car.json: n " + ratios.Count + ", mean ratio " + F(sum / ratios.Count, "0.00") + ", median " + F(ratios[ratios.Count / 2], "0.00"));
        }
        return 0;
    }

    static int Cmd_psim(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string only = a.Count > 1 ? a[1] : "ferrari_458";
        bool all = only == "all";
        var ratios = new List<float>(); var slow = new List<string>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (!all && !car.Id.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) continue;
            SimResult r;
            try { r = Simulate(s, !all); }
            catch (Exception e) { Console.WriteLine(car.Id + " SIM FAILED " + e); continue; }
            Console.WriteLine(car.Id.PadRight(36) + " shift1 " + F(r.Shift1, "0.00").PadLeft(5) + "s  0-100 " + F(r.T100, "0.00").PadLeft(5) + "s (ui " + F(car.Specs.Acceleration0100, "0.0").PadLeft(4) + ")  0-200 " + F(r.T200, "0.0").PadLeft(5) +
                "s  top " + F(r.Top, "0").PadLeft(4) + " (ui " + F(car.Specs.TopSpeedKmh, "0").PadLeft(4) + ")  100-0 " + F(r.Brake100, "0.0").PadLeft(5) + "m  lat " + F(r.LatG, "0.00") + "g @100, " + F(r.LatG200, "0.00") + "g @200" +
                "  0-100 split: clutch " + F(r.TClutch, "0.00") + " (launch " + F(r.LaunchRpm, "0") + " rpm) shift " + F(r.TShift, "0.00") + " tc " + F(r.TTc, "0.00") + " traction " + F(r.TTraction, "0.00") + " power " + F(r.TPower, "0.00") +
                " ; tyre peak slip " + F(r.PeakSlip, "0.000") + " TC limit " + F(s.Tc.SlipRatioLimit, "0.00") + (s.Tc.Present && s.Tc.Active ? " (factory on)" : ""));
            float ui = car.Specs.Acceleration0100;
            if (ui > 1f && !float.IsNaN(r.T100)) { ratios.Add(r.T100 / ui); if (r.T100 / ui > 1.3f) slow.Add(car.Id + " " + F(r.T100, "0.0") + "/" + F(ui, "0.0")); }
        }
        if (ratios.Count > 1)
        {
            float sum = 0f; foreach (float x in ratios) sum += x;
            ratios.Sort();
            Console.WriteLine("fleet 0-100 vs ui_car.json: n " + ratios.Count + ", mean ratio " + F(sum / ratios.Count, "0.00") + ", median " + F(ratios[ratios.Count / 2], "0.00") + ", > 30 % slow: " + slow.Count + " (" + string.Join(", ", slow) + ")");
        }
        return 0;
    }

    // ------------------------------------------------------------------ pchassis: the Unity-side chassis (AcChassis + AcCarCore) on a 3D rigid body
    // Same math as AcCarPhysics.Step (rays replaced by a flat ground y = 0, PhysX's semi-implicit integration re-done here):
    //   pchassis <car|all> [dt]   settle (ride height, creep), 0-100 / top / 100-0 on the full chassis, constant-speed steer ramp (max
    //                             lateral g, turn direction), at the given fixed step (default 0.02 = Somnium's TimeManager).
    sealed class Rb
    {
        public AcVec3 P, V, W; public float Qx, Qy, Qz, Qw = 1f; public float M; public AcVec3 I;
        public AcVec3 Up { get { return AcChassis.Rotate(Qx, Qy, Qz, Qw, new AcVec3(0, 1, 0)); } }
        public AcVec3 Fwd { get { return AcChassis.Rotate(Qx, Qy, Qz, Qw, new AcVec3(0, 0, 1)); } }
        public AcVec3 Right { get { return AcChassis.Rotate(Qx, Qy, Qz, Qw, new AcVec3(1, 0, 0)); } }
        public void Integrate(AcVec3 f, AcVec3 t, AcVec3 g, float dt)
        {
            V = V + (f * (1f / M) + g) * dt;
            AcVec3 tl = AcChassis.Rotate(-Qx, -Qy, -Qz, Qw, t);
            W = W + AcChassis.Rotate(Qx, Qy, Qz, Qw, new AcVec3(tl.X / I.X, tl.Y / I.Y, tl.Z / I.Z)) * dt;
            W = W * (1f / (1f + 0.05f * dt)); // Unity's default angular damping 0.05
            P = P + V * dt;
            float wx = W.X, wy = W.Y, wz = W.Z; // q += 0.5 * (0, w) * q * dt
            float dx = 0.5f * dt * (wx * Qw + wy * Qz - wz * Qy);
            float dy = 0.5f * dt * (wy * Qw + wz * Qx - wx * Qz);
            float dz = 0.5f * dt * (wz * Qw + wx * Qy - wy * Qx);
            float dw = 0.5f * dt * (-wx * Qx - wy * Qy - wz * Qz);
            Qx += dx; Qy += dy; Qz += dz; Qw += dw;
            float n = (float)Math.Sqrt(Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw);
            Qx /= n; Qy /= n; Qz /= n; Qw /= n;
        }
    }

    sealed class ChassisSim
    {
        public AcCarSpec S; public AcCarCore Core; public AcChassis Ch; public Rb B = new Rb(); public float Dt; public int N;
        public AcVec3 G = new AcVec3(0, -9.81f, 0);
        public float Steer; // road deg
        public ChassisSim(AcCarSpec s, float dt)
        {
            S = s; Dt = dt; N = Math.Max(1, (int)Math.Ceiling(dt * 333f));
            Core = new AcCarCore(s); Core.Damage = HarnessDamage(s);
            B.M = s.TotalMassKg + s.FuelLitres * 0.75f;
            float w = s.InertiaBox[0], h = s.InertiaBox[1], l = s.InertiaBox[2], m = B.M;
            B.I = new AcVec3(m * (h * h + l * l) / 12f, m * (w * w + l * l) / 12f, m * (w * w + h * h) / 12f);
            Ch = new AcChassis(s, Core, B.M, 9.81f);
            string toeEnv = Environment.GetEnvironmentVariable("PCH_TOE"); // test hook: scale every toe angle (0 = none, -1 = flipped)
            if (!string.IsNullOrEmpty(toeEnv)) for (int i = 0; i < 4; i++) Ch.Wheels[i].ToeDeg *= float.Parse(toeEnv, PInv);
            B.P = new AcVec3(0, s.PhysicsCgHeightM + 0.01f, 0);
        }
        public void Step()
        {
            AcVec3 up = B.Up, fwd = B.Fwd;
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel wh = Ch.Wheels[i];
                AcVec3 hub = B.P + AcChassis.Rotate(B.Qx, B.Qy, B.Qz, B.Qw, wh.Hub);
                wh.Contact = false;
                if (up.Y > 0.1f)
                {
                    AcVec3 o = hub + up * 0.12f;
                    float t = o.Y / up.Y;
                    float dist = t - 0.12f;
                    if (t >= 0f && dist <= wh.Radius + wh.Droop)
                    {
                        wh.Contact = true; wh.Dist = dist; wh.Point = hub - up * dist; wh.Normal = new AcVec3(0, 1, 0); wh.Grip = 1f; wh.Damping = 0f;
                    }
                }
            }
            Ch.SteerRoadDeg = Steer;
            Ch.Prepare(up, fwd);
            AcVec3 af = new AcVec3(0, 0, 0), at = new AcVec3(0, 0, 0);
            float speed = B.V.Length;
            if (speed > 1f)
            {
                AcVec3 dir = B.V * (1f / speed);
                foreach (var wg in S.Wings)
                {
                    float down, drag;
                    AcAeroModel.Wing(wg, speed, 0f, AcAeroModel.GroundHeight(S, wg, CgAbove(true), CgAbove(false)), 0f, wg.Angle, out down, out drag);
                    AcVec3 F = up * (-down) + dir * (-drag);
                    AcVec3 pos = B.P + AcChassis.Rotate(B.Qx, B.Qy, B.Qz, B.Qw, new AcVec3(-wg.Position.X, wg.Position.Y, wg.Position.Z));
                    af = af + F; at = at + AcVec3.Cross(pos - B.P, F);
                }
            }
            var bs = new AcBodyState { Up = up, Fwd = fwd, Right = B.Right, Com = B.P, V = B.V, W = B.W, Gravity = G, Mass = B.M, Inertia = B.I, Qx = B.Qx, Qy = B.Qy, Qz = B.Qz, Qw = B.Qw };
            AcVec3 f, tq;
            Ch.Step(Dt, N, ref bs, af, at, out f, out tq);
            B.Integrate(f, tq, G, Dt);
        }
        public float Kmh { get { return B.V.Length * 3.6f; } }
        /// <summary>CG height above the front / rear tyre patches (as AcCarPhysics.Aero).</summary>
        public float CgAbove(bool front)
        {
            float h = 0f; int n = 0; AcVec3 up = B.Up;
            for (int i = 0; i < 4; i++) { var w = Ch.Wheels[i]; if (w.Front != front || !w.Contact) continue; h += AcVec3.Dot(B.P - w.Point, up); n++; }
            return n > 0 ? h / n : S.PhysicsCgHeightM + 1f;
        }
    }

    static float Heading(Rb b) { AcVec3 f = b.Fwd; return (float)(Math.Atan2(f.X, f.Z) * 57.29578); }

    static int Cmd_pchassis(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string id = a.Count > 1 ? a[1] : "ferrari_458";
        float dt = a.Count > 2 ? float.Parse(a[2], PInv) : 0.02f;
        bool all = id == "all";
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (!all && !car.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) continue;
            try { ChassisReport(s, dt, !all); } catch (Exception e) { Console.WriteLine(car.Id + " FAILED " + e); }
        }
        return 0;
    }

    static void ChassisReport(AcCarSpec s, float dt, bool verbose)
    {
        int stepsPerS = (int)Math.Round(1f / dt);
        // 1. settle with the brake on
        var c = new ChassisSim(s, dt);
        c.Core.Brake = 1f;
        float maxV = 0f;
        for (int k = 0; k < stepsPerS * 4; k++) { c.Step(); if (k > stepsPerS * 3) maxV = Math.Max(maxV, c.B.V.Length); }
        float pitch = (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, c.B.Fwd.Y))) * 57.2958), roll = (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, c.B.Right.Y))) * 57.2958);
        string settle = "settle: CG " + F(c.B.P.Y, "0.000") + " m (static " + F(s.PhysicsCgHeightM, "0.000") + ", -off.y " + F(s.CgHeightM, "0.000") + ") pitch " + F(pitch, "0.00") + " roll " + F(roll, "0.00") + " deg, creep " + F(maxV * 1000f, "0.0") + " mm/s, travel F/R " +
            F(c.Ch.Wheels[0].X * 1000f, "0.0") + "/" + F(c.Ch.Wheels[2].X * 1000f, "0.0") + " mm";

        // 2. acceleration (test driver as psim), top speed
        c.Core.Brake = 0f;
        float t = 0f, t100 = float.NaN, gas = 1f;
        int di = c.Core.Driven(2) ? 2 : 0;
        float peak = c.Core.Tyre[di].TanPeak / Math.Max(0.1f, c.Core.Tyre[di].CxMult);
        float maxYaw = 0f, maxLat = 0f, maxYawKick = 0f, kickHeading = 0f; int kickAt = -1;
        for (int k = 0; k < stepsPerS * 90; k++)
        {
            float worst = 0f;
            for (int i = 0; i < 4; i++) if (c.Core.Driven(i) && c.Core.SlipRatio[i] > worst) worst = c.Core.SlipRatio[i];
            if (worst > peak * 1.1f) gas = Math.Max(0.2f, gas - 0.1f); else gas = Math.Min(1f, gas + 0.05f);
            c.Core.Gas = gas;
            c.Step(); t += dt;
            maxYaw = Math.Max(maxYaw, Math.Abs(c.B.W.Y)); maxLat = Math.Max(maxLat, Math.Abs(c.B.P.X));
            // a yaw "gust" of 0.05 rad/s at 80 km/h (// ours test): a stable car damps it (AC cars carry rear toe-in for that), an unstable one spins up
            if (kickAt < 0 && c.Kmh >= 80f) { kickAt = k; c.B.W = c.B.W + c.B.Up * 0.05f; kickHeading = Heading(c.B); }
            if (kickAt >= 0 && k > kickAt) maxYawKick = Math.Max(maxYawKick, Math.Abs(c.B.W.Y));
            if (float.IsNaN(t100) && c.Kmh >= 100f) t100 = t;
            if (verbose && k % stepsPerS == 0 && t < 8f) Console.WriteLine("   t " + F(t, "0.0") + " " + F(c.Kmh, "0.0") + " km/h gear " + c.Core.Gear + " rpm " + F(c.Core.Rpm, "0") + " CG " + F(c.B.P.Y, "0.000") + " pitch " + F((float)(Math.Asin(c.B.Fwd.Y) * 57.3), "0.00") + " loads F/R " + F(c.Core.Load[0], "0") + "/" + F(c.Core.Load[2], "0") + " Fy " + F(c.Core.Fy[0], "0.0") + "/" + F(c.Core.Fy[1], "0.0") + "/" + F(c.Core.Fy[2], "0.0") + "/" + F(c.Core.Fy[3], "0.0") + " vx " + F(c.B.V.X, "0.000") + " w " + c.B.W + " cam " + F(c.Core.Camber[0], "0.0000") + "/" + F(c.Core.Camber[1], "0.0000") + " LR " + F(c.Core.Load[0], "0.0") + "/" + F(c.Core.Load[1], "0.0"));
        }
        float top = c.Kmh;
        float headingChange = Heading(c.B) - kickHeading, yawEnd = Math.Abs(c.B.W.Y);
        bool stable = yawEnd < 0.01f && maxYawKick < 0.1f && c.B.Up.Y > 0.9f;
        string accel = "0-100 " + F(t100, "0.00") + " s, top " + F(top, "0") + ", drift x " + F(maxLat, "0.00") + " m, max yaw " + F(maxYaw, "0.000") + " rad/s, yaw kick 0.05 at 80 km/h: max " + F(maxYawKick, "0.000") + " end " + F(yawEnd, "0.0000") + " heading " + F(headingChange, "0.0") + " deg " + (stable ? "STABLE" : "UNSTABLE");

        // 3. braking 100-0 (ABS factory)
        c = new ChassisSim(s, dt);
        c.Core.Brake = 1f; for (int k = 0; k < stepsPerS * 2; k++) c.Step();
        c.B.V = c.B.Fwd * (100f / 3.6f);
        for (int i = 0; i < 4; i++) c.Core.Omega[i] = 100f / 3.6f / c.Core.Tyre[i].Radius;
        float z0 = c.B.P.Z; int steps = 0;
        while (c.Kmh > 1f && steps++ < stepsPerS * 15) c.Step();
        string brake = "100-0 " + F(c.B.P.Z - z0, "0.0") + " m";

        // 4. constant-speed steer ramp at 100 km/h: lateral g, direction
        c = new ChassisSim(s, dt);
        c.Core.Brake = 1f; for (int k = 0; k < stepsPerS * 2; k++) c.Step();
        c.Core.Brake = 0f;
        float vt = 100f / 3.6f;
        c.B.V = c.B.Fwd * vt;
        for (int i = 0; i < 4; i++) c.Core.Omega[i] = vt / c.Core.Tyre[i].Radius;
        c.Core.RequestGear(3);
        float best = 0f, bestSteer = 0f, yawSign = 0f, spunAt = float.NaN;
        for (int k = 0; k < stepsPerS * 40; k++)
        {
            float ramp = k * dt * 0.25f; // 0.25 deg/s of road angle // ours test ramp
            c.Steer = ramp;
            float sp = AcVec3.Dot(c.B.V, c.B.Fwd);
            float err = vt - sp;
            c.Core.Gas = Math.Max(0f, Math.Min(1f, err * 0.5f + 0.3f));
            c.Core.Brake = err < -2f ? Math.Min(1f, -err * 0.1f) : 0f;
            c.Step();
            float lat = Math.Abs(c.B.W.Y) * c.B.V.Length / 9.81f;
            float slip = (float)(Math.Atan2(AcVec3.Dot(c.B.V, c.B.Right), Math.Max(0.1f, AcVec3.Dot(c.B.V, c.B.Fwd))) * 57.2958);
            if (k == stepsPerS * 4) yawSign = Math.Sign(c.B.W.Y);
            if (Math.Abs(slip) > 12f || c.B.Up.Y < 0.7f) { spunAt = ramp; break; }
            if (lat > best && Math.Abs(sp - vt) < 3f) { best = lat; bestSteer = ramp; }
        }
        string corner = "steer ramp @100: max " + F(best, "0.00") + " g at road " + F(bestSteer, "0.0") + " deg, turns " + (yawSign > 0 ? "RIGHT (yaw +)" : yawSign < 0 ? "LEFT (yaw -)" : "none") +
            (float.IsNaN(spunAt) ? "" : ", lost it at " + F(spunAt, "0.0") + " deg");
        Console.WriteLine(s.CarId.PadRight(34) + " dt " + F(dt, "0.0000") + " n " + c.N + " | " + settle + " | " + accel + " | " + brake + " | " + corner);
    }

    // ------------------------------------------------------------------ pride: which static ride height did Kunos mean? (second pass)
    //   pride [car|all]   per car: -GRAPHICS_OFFSET.y vs the CG height of (A) wheels at BASEY (design point = static) and (B) BASEY at the
    //                     springs' free length (static = design - spring sag - tyre deflection + rod), the kn5 WHEEL_LF/LR rest height vs the
    //                     physics hub, and the aero ground-height lut multipliers at the pickup ride heights under A and B.
    static int Cmd_pride(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string id = a.Count > 1 ? a[1] : "all";
        var dA = new List<float>(); var dB = new List<float>(); var dB2 = new List<float>(); var dW = new List<float>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (id != "all" && !car.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) continue;
            float m = s.TotalMassKg + s.FuelLitres * 0.75f, g = 9.81f;
            AcTyreCompound c = s.Compound(s.DefaultCompound);
            float lf = m * g * s.CgLocation * 0.5f, lr = m * g * (1f - s.CgLocation) * 0.5f;
            float hdF = c.Front.Radius - s.Front.BaseY, hdR = c.Rear.Radius - s.Rear.BaseY;
            float sagF = AcChassis.Sag(s.Front, lf), sagR = AcChassis.Sag(s.Rear, lr);
            float tyF = lf / Math.Max(1e4f, c.Front.Rate), tyR = lr / Math.Max(1e4f, c.Rear.Rate);
            float k = 1f - s.CgLocation;
            float cgA = hdF + (hdR - hdF) * k;
            float hbF = hdF - sagF - tyF + s.Front.RodLength, hbR = hdR - sagR - tyR + s.Rear.RodLength; // ROD_LENGTH raises the ride height (Kunos: "positive raises ride height")
            float cgB2 = (hdF - sagF + s.Front.RodLength) + ((hdR - sagR + s.Rear.RodLength) - (hdF - sagF + s.Front.RodLength)) * k; dB2.Add(-s.GraphicsOffset.Y - cgB2);
            float cgB = hbF + (hbR - hbF) * k;
            float off = -s.GraphicsOffset.Y;
            dA.Add(off - cgA); dB.Add(off - cgB);
            // kn5 wheel rest height (model space, ground origin) -> body: off.y + y (pitch ignored) vs the physics hub BASEY
            string wheel = "";
            try
            {
                var kn = Kn5File.Read(car.Kn5Path, Kn5ReadOptions.HeaderOnly);
                var nf = kn.Find("WHEEL_LF"); var nr = kn.Find("WHEEL_LR");
                if (nf != null && nr != null)
                {
                    float yf = nf.World.Translation.Y, yr = nr.World.Translation.Y;
                    float visF = s.GraphicsOffset.Y + yf, visR = s.GraphicsOffset.Y + yr; // body-space hub height of the drawn wheel at rest
                    wheel = " | kn5 wheel y F/R " + F(yf, "0.000") + "/" + F(yr, "0.000") + " (R " + F(c.Front.Radius, "0.000") + "/" + F(c.Rear.Radius, "0.000") + ") hub-vs-drawn F/R " +
                        F((s.Front.BaseY - visF) * 1000f, "0") + "/" + F((s.Rear.BaseY - visR) * 1000f, "0") + " mm";
                    dW.Add(Math.Max(Math.Abs(s.Front.BaseY - visF), Math.Abs(s.Rear.BaseY - visR)));
                }
            }
            catch (Exception e) { wheel = " | kn5 " + e.GetType().Name; }
            string aero = "";
            foreach (var w in s.Wings)
            {
                if (w.GhCl == null && w.GhCd == null) continue;
                if (w.GhCl == null) { float t0 = Math.Max(0f, Math.Min(1f, (w.Position.Z - s.Rear.AxleZ) / Math.Max(0.5f, s.WheelbaseM))); float pB0 = cgB + (s.PickupRearHeight + (s.PickupFrontHeight - s.PickupRearHeight) * t0); aero += " | wing" + w.Index + " CDonly CD(posB " + F(cgB + w.Position.Y, "0.000") + ")=" + F(w.GhCd.Eval(cgB + w.Position.Y), "0.00") + " CD(pickupB " + F(pB0, "0.000") + ")=" + F(w.GhCd.Eval(pB0), "0.00") + " CD(cgB)=" + F(w.GhCd.Eval(cgB), "0.00"); continue; }
                float t = Math.Max(0f, Math.Min(1f, (w.Position.Z - s.Rear.AxleZ) / Math.Max(0.5f, s.WheelbaseM)));
                float pA = cgA + (s.PickupRearHeight + (s.PickupFrontHeight - s.PickupRearHeight) * t);
                float pB = cgB + (s.PickupRearHeight + (s.PickupFrontHeight - s.PickupRearHeight) * t);
                float wA = cgA + w.Position.Y, wB = cgB + w.Position.Y;
                aero += " | wing" + w.Index + " GH(pickupA " + F(pA, "0.000") + ")=" + F(w.GhCl.Eval(pA), "0.00") + " GH(pickupB " + F(pB, "0.000") + ")=" + F(w.GhCl.Eval(pB), "0.00") + " GH(posA " + F(wA, "0.000") + ")=" + F(w.GhCl.Eval(wA), "0.00") + " GH(posB " + F(wB, "0.000") + ")=" + F(w.GhCl.Eval(wB), "0.00") + (w.GhCd != null ? " CD(posB)=" + F(w.GhCd.Eval(wB), "0.00") + " CD(pickupB)=" + F(w.GhCd.Eval(pB), "0.00") : "") + " argmax " + F(w.GhCl.ArgMaxY, "0.000");
            }
            Console.WriteLine(car.Id.PadRight(34) + " -off.y " + F(off, "0.000") + " A(design) " + F(cgA, "0.000") + " d " + F((off - cgA) * 1000f, "0").PadLeft(4) + "mm  B(free) " + F(cgB, "0.000") + " d " + F((off - cgB) * 1000f, "0").PadLeft(4) +
                "mm  sag F/R " + F(sagF * 1000f, "0") + "/" + F(sagR * 1000f, "0") + " tyre " + F(tyF * 1000f, "0") + "/" + F(tyR * 1000f, "0") + " rod " + F(s.Front.RodLength * 1000f, "0") + "/" + F(s.Rear.RodLength * 1000f, "0") + wheel + aero);
        }
        Stat("A design point = static: -off.y - CG", dA); Stat("B BASEY at free length: -off.y - CG", dB); Stat("B2 free length + rod, no tyre deflection", dB2); Stat("max |physics hub - drawn kn5 wheel| (m)", dW);
        return 0;
    }

    static void Stat(string what, List<float> v)
    {
        if (v.Count == 0) return;
        double sum = 0, sq = 0, abs = 0; int over2 = 0;
        foreach (float x in v) { sum += x; sq += x * x; abs += Math.Abs(x); if (Math.Abs(x) > 0.02f) over2++; }
        double mean = sum / v.Count, sd = Math.Sqrt(Math.Max(0, sq / v.Count - mean * mean));
        Console.WriteLine(what + ": n " + v.Count + " mean " + (mean * 1000).ToString("0.0", PInv) + " mm, sd " + (sd * 1000).ToString("0.0", PInv) + " mm, mean |d| " + (abs / v.Count * 1000).ToString("0.0", PInv) + " mm, > 2 cm: " + over2);
    }
}
