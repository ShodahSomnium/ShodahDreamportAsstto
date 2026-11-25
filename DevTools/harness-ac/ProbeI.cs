using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumAssetto;

// Module I (AI drivers) probes. Run: dotnet run -c Release --no-build -- <cmd> ...
//   aiini                              ai.ini census of every owned car
//   aiprof <track> [layout] [step]     fast_lane payload rows + lap time at the payload speed
//   aicar [car|all]                    AcAiCar digest (grip, aero, power, brakes, top speed) of one / every owned car
//   ailine <track|all> [layout]        line geometry checks (curvature vs payload radius, turn side vs sides), payload hint sanity,
//                                      preview geometry, planned lap times of ferrari_458 / abarth500 at level 100 / 90
//   aisim <car> <track> [layout] [laps] [level] [png]   AcAiBrain driving a bicycle-model car (tyres, gearbox, aero) for N laps
//   aitimer                            AcAiLapTimer unit cases (T3 grid before / after the line, practice, reversing)
static partial class Program
{
    static int Cmd_aiprof(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = FindLayout(c, a.Count > 1 ? a[1] : "magione", a.Count > 2 ? a[2] : "");
        if (l == null) { Console.WriteLine("no layout"); return 1; }
        var s = AcAiSpline.Read(Path.Combine(l.AiDir, "fast_lane.ai"));
        int step = a.Count > 3 ? int.Parse(a[3]) : 50;
        Console.WriteLine(l.Key + ": " + s.Count + " pts lap " + F(s.LapLength) + " closed " + s.IsClosed + " laptime field " + s.LapTime);
        Console.WriteLine("  i   len   speed  gas  brake latG  radius  sideL sideR camber dir  tag  grade");
        for (int i = 0; i < s.Count; i += step)
            Console.WriteLine(string.Format(Inv, "{0,5} {1,7:0} {2,6:0.0} {3,4:0.00} {4,4:0.00} {5,5:0.00} {6,8:0.0} {7,5:0.0} {8,5:0.0} {9,6:0.000} {10,3:0} {11,4:0} {12,6:0.000}",
                i, s.Length[i], s.P(i, 0), s.P(i, 1), s.P(i, 2), s.P(i, 3), s.P(i, 4), s.P(i, 5), s.P(i, 6), s.P(i, 7), s.P(i, 8), s.P(i, 16), s.P(i, 17)));
        Console.WriteLine("lap time at payload speed: " + PayloadLapTime(s).ToString("0.0", Inv) + " s");
        return 0;
    }

    static double PayloadLapTime(AcAiSpline s)
    {
        double t = 0; for (int i = 0; i + 1 < s.Count; i++) { float v = s.P(i, 0); if (v > 0.5f) t += (s.Length[i + 1] - s.Length[i]) / v; }
        return t;
    }

    static int Cmd_aiini(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var keys = new SortedDictionary<string, int>();
        foreach (var car in c.Cars)
        {
            if (!car.Owned) continue;
            try
            {
                var acd = AcdArchive.Open(car.Dir);
                var ini = acd.ReadIniOrEmpty("ai.ini");
                var sb = new StringBuilder(car.Id.PadRight(28));
                foreach (var sec in ini.Order)
                    foreach (var kk in sec.Keys)
                    {
                        string k = sec.Name + "." + kk; int n; keys.TryGetValue(k, out n); keys[k] = n + 1;
                        sb.Append(' ').Append(kk).Append('=').Append(sec.Get(kk));
                    }
                Console.WriteLine(sb.ToString());
            }
            catch (Exception e) { Console.WriteLine(car.Id + " FAIL " + e.Message); }
        }
        foreach (var kv in keys) Console.WriteLine(kv.Key + " " + kv.Value);
        return 0;
    }

    static AcAiCar AiCar(AcContent c, string id)
    {
        var e = c.Car(id);
        if (e == null || !e.Owned) return null;
        return AcAiCar.Load(e.Dir, e.Id);
    }

    static int Cmd_aicar(List<string> a)
    {
        var c = AcContent.Scan(_install);
        string which = a.Count > 1 ? a[1] : "all";
        int n = 0, bad = 0;
        foreach (var car in c.Cars)
        {
            if (!car.Owned || (which != "all" && car.Id != which)) continue;
            var ac = AcAiCar.Load(car.Dir, car.Id);
            n++; if (ac.Problems.Count > 0) bad++;
            Console.WriteLine(ac + " | ai " + ac.Ai);
        }
        Console.WriteLine(n + " cars, " + bad + " with problems");
        return 0;
    }

    static float RoadGrip(AcLayoutEntry l)
    {
        try
        {
            var s = AcSurfaces.Load(l.SurfacesIni, _install.SystemSurfacesIni);
            float best = 0f;
            foreach (var x in s.Surfaces) if (x.IsValidTrack && !x.IsPitlane && x.Friction > best && x.Friction < 1.3f) best = x.Friction;
            return best > 0.5f ? best : 1f;
        }
        catch { return 1f; }
    }

    static int Cmd_ailine(List<string> a)
    {
        var c = AcContent.Scan(_install);
        string track = a.Count > 1 ? a[1] : "magione";
        var layouts = new List<AcLayoutEntry>();
        if (track == "all") { foreach (var t in c.Tracks) foreach (var l in t.Layouts) if (l.Owned) layouts.Add(l); }
        else { var l = FindLayout(c, track, a.Count > 2 ? a[2] : ""); if (l == null) { Console.WriteLine("no layout"); return 1; } layouts.Add(l); }
        var f458 = AiCar(c, "ferrari_458"); var abarth = AiCar(c, "abarth500");
        Console.WriteLine("cars: " + f458); Console.WriteLine("      " + abarth);
        foreach (var l in layouts)
        {
            if (l.FastLanePath == null) { Console.WriteLine(l.Key + ": no fast_lane.ai"); continue; }
            var s = AcAiSpline.Read(l.FastLanePath);
            if (s.IsEmpty) { Console.WriteLine(l.Key + ": empty fast lane (v" + s.Version + ") -> no AI"); continue; }
            var tr = new AcAiTrack(s);
            // 1) curvature vs payload radius, direction sign, apex inside
            int nr = 0, signOk = 0, signN = 0, apexOk = 0, apexN = 0; var rel = new List<float>();
            for (int i = 0; i < s.Count; i++)
            {
                float R = s.P(i, AcAiSpline.Radius), k = tr.Curv[i], dir = s.P(i, AcAiSpline.Direction);
                if (R > 5f && R < 300f && Math.Abs(k) > 1e-4f) { rel.Add(Math.Abs(Math.Abs(k) * R - 1f)); nr++; }
                if (R > 5f && R < 300f && dir != 0f) { signN++; if (Math.Sign(dir) == Math.Sign(k)) signOk++; }
                // local curvature maxima (apexes) with R < 150
                if (Math.Abs(k) > 1f / 150f && Math.Abs(k) >= Math.Abs(tr.Curv[tr.Idx(i - 1)]) && Math.Abs(k) >= Math.Abs(tr.Curv[tr.Idx(i + 1)]))
                {
                    apexN++;
                    float inside = k > 0 ? tr.SideL[i] : tr.SideR[i], outside = k > 0 ? tr.SideR[i] : tr.SideL[i];
                    if (inside < outside) apexOk++;
                }
            }
            rel.Sort();
            float med = rel.Count > 0 ? rel[rel.Count / 2] : float.NaN, p90 = rel.Count > 0 ? rel[rel.Count * 9 / 10] : float.NaN;
            // 2) payload hints
            int gasN = 0, brkN = 0, latOk = 0, latN = 0;
            for (int i = 0; i < s.Count; i++)
            {
                if (s.P(i, AcAiSpline.Gas) > 0.5f) gasN++;
                if (s.P(i, AcAiSpline.Brake) > 0.01f) brkN++;
                float v = s.P(i, AcAiSpline.Speed), R = s.P(i, AcAiSpline.Radius), lg = s.P(i, AcAiSpline.ObsoleteLatG);
                if (R > 5f && R < 300f && v > 5f) { latN++; float pred = v * v / (R * 9.81f); if (Math.Abs(Math.Abs(lg) - pred) < 0.1f + 0.1f * pred) latOk++; }
            }
            float grip = RoadGrip(l);
            var p1 = new AcAiPlan(tr, f458, 100, grip); var p2 = new AcAiPlan(tr, f458, 90, grip);
            var p3 = new AcAiPlan(tr, abarth, 100, grip); var p4 = new AcAiPlan(tr, abarth, 90, grip);
            float vmin = float.MaxValue, vmax = 0; foreach (var v in p1.V) { if (v < vmin) vmin = v; if (v > vmax) vmax = v; }
            Console.WriteLine(string.Format(Inv, "{0}: {1} pts {2:0} m, road grip {3:0.000}; |k|R-1 median {4:0.000} p90 {5:0.000} (n {6}); dir sign ok {7}/{8}; apex inside {9}/{10}; sides L/R from payload {11}",
                l.Key, s.Count, tr.Length, grip, med, p90, nr, signOk, signN, apexOk, apexN, tr.HasSides));
            Console.WriteLine(string.Format(Inv, "   payload: lap {0:0.0} s, gas>0.5 {1:0}%, brake>0 {2:0}%, latG == v^2/(R g) {3}/{4}",
                PayloadLapTime(s), 100.0 * gasN / s.Count, 100.0 * brkN / s.Count, latOk, latN));
            Console.WriteLine(string.Format(Inv, "   plan 458: L100 {0} L90 {1} (v {2:0}-{3:0} km/h) | abarth500: L100 {4} L90 {5}",
                Lap(p1.LapTime), Lap(p2.LapTime), vmin * 3.6f, vmax * 3.6f, Lap(p3.LapTime), Lap(p4.LapTime)));
            if (layouts.Count == 1)
            {
                var br = new AcAiBrain(f458, tr, p1);
                Console.Write("   preview 458 (m): ");
                foreach (float v in new[] { 5f, 15f, 30f, 45f, 60f, 80f }) Console.Write((v * 3.6f).ToString("0") + "km/h " + br.PreviewDistance(v).ToString("0.0") + "  ");
                Console.WriteLine();
                // chord check: on the plan, the pursuit point's lateral distance from the line tangent -> curvature pure pursuit asks vs line curvature
                double worst = 0; int wi = 0;
                for (int i = 0; i < s.Count; i += 3)
                {
                    float S0 = s.Length[i], v = p1.V[i], D = br.PreviewDistance(v);
                    float x0, z0, tx, tz, k, sl, sr, x1, z1, tx1, tz1, k1, sl1, sr1;
                    tr.Sample(S0, out x0, out z0, out tx, out tz, out k, out sl, out sr);
                    tr.Sample(S0 + D, out x1, out z1, out tx1, out tz1, out k1, out sl1, out sr1);
                    float dx = x1 - x0, dz = z1 - z0, left = dx * tz + dz * -tx, l2 = dx * dx + dz * dz;
                    float kpp = 2f * left / l2;
                    double cut = Math.Abs(kpp - k) * 4.0; // curvature excess over 2 m ~ lateral metres of early turn-in (rough)
                    if (Math.Abs(kpp - k) > worst) { worst = Math.Abs(kpp - k); wi = i; }
                }
                Console.WriteLine(string.Format(Inv, "   largest pure-pursuit vs line curvature difference {0:0.0000} 1/m at point {1} (line k {2:0.0000}, v {3:0} km/h)",
                    worst, wi, tr.Curv[wi], p1.V[wi] * 3.6f));
                Console.Write("   458 L100 plan km/h every 100 m: ");
                for (float x = 0; x < tr.Length; x += 100f) Console.Write((p1.SpeedAt(x) * 3.6f).ToString("0") + " ");
                Console.WriteLine();
            }
        }
        return 0;
    }

    static string Lap(double s) { int ms = (int)Math.Round(s * 1000); return (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00") + "." + (ms % 1000).ToString("000"); }

    // ------------------------------------------------------------------ bicycle-model simulation (probe only; P's physics is the real one)

    sealed class SimCar
    {
        public AcAiCar C;
        public double X, Z, Psi;          // AC space, heading angle (fwd = (sin psi, cos psi))
        public double Vx, Vy, R;          // car frame: forward, left; yaw rate (+ left)
        public int Gear = 0; public double ShiftT; public double Rpm;
        public double DrivenSlip;
        public float AlphaPeakF = 7.5f, AlphaPeakR = 7f;
        public double Fy0, Fy1;
        public double MaxLatG, Ax;
    }

    static double LoadTransfer = 0.6; // probe: 60 % of the rigid-body pitch transfer (suspension anti-dive / compliance not modelled)
    static float Brush(double s) { double a = Math.Abs(s); double f = a <= 1 ? 2 * a - a * a : 1 - (1 - 0.86) * (1 - Math.Exp(-(a - 1) * 2)); return (float)(Math.Sign(s) * f); }

    static void SimStep(SimCar k, AcAiControls ctl, double dt, float grip)
    {
        var c = k.C;
        double m = c.MassKg, wb = c.WheelbaseM, a = wb * (1 - c.CgFront), b = wb * c.CgFront, Iz = m * a * b * 1.1; // ours (probe)
        double v = Math.Sqrt(k.Vx * k.Vx + k.Vy * k.Vy);
        double down = c.Downforce((float)v);
        double dTr = LoadTransfer * m * k.Ax * c.CgHeightM / wb; // longitudinal load transfer (last step's acceleration)
        double fzF = Math.Max(100, (m * 9.81 + down) * c.CgFront - dTr), fzR = Math.Max(100, (m * 9.81 + down) * (1 - c.CgFront) + dTr);
        double muF = c.MuLatFront * grip, muR = c.MuLatRear * grip;
        double delta = -ctl.RoadAngleDeg * Math.PI / 180.0;   // left positive
        double vxs = Math.Max(1.0, Math.Abs(k.Vx));
        double alphaF = Math.Atan2(k.Vy + a * k.R, vxs) - delta, alphaR = Math.Atan2(k.Vy - b * k.R, vxs);
        // cornering stiffness grows slower than the load (Fz^0.85 // probe): the peak slip angle scales with (Fz/Fz0)^-0.15
        double fz0F = m * 9.81 * c.CgFront, fz0R = m * 9.81 * (1 - c.CgFront);
        double apF = k.AlphaPeakF * Math.Pow(Math.Max(0.2, fzF / fz0F), 0.15), apR = k.AlphaPeakR * Math.Pow(Math.Max(0.2, fzR / fz0R), 0.15);
        // AC load sensitivity: mu ~ (Fz/Fz0)^(LS_EXP-1), ~-0.18 for these tyres (research 1.10)
        muF *= Math.Pow(Math.Max(0.2, fzF / fz0F), -0.18); muR *= Math.Pow(Math.Max(0.2, fzR / fz0R), -0.18);
        double FyF = -muF * fzF * Brush(alphaF * 180 / Math.PI / apF), FyR = -muR * fzR * Brush(alphaR * 180 / Math.PI / apR);
        // gearbox / engine
        if (k.ShiftT > 0) k.ShiftT -= dt;
        double drive = 0;
        if (k.Gear >= 1 && k.ShiftT <= 0)
        {
            double rpm = c.RpmAt((float)Math.Max(0, k.Vx), k.Gear);
            if (k.Gear == 1) rpm = Math.Max(rpm, 0.6 * Math.Min(c.Ai.UpRpm, c.LimiterRpm));
            rpm = Math.Max(rpm, c.IdleRpm);
            k.Rpm = rpm;
            if (rpm < c.LimiterRpm) drive = ctl.Throttle * c.WheelTorqueAt((float)rpm) * c.Gears[k.Gear - 1] * c.FinalRatio / c.DrivenRadius;
        }
        else if (k.Gear == -1 && k.ShiftT <= 0) { drive = -ctl.Throttle * c.WheelTorqueAt(2500) * Math.Abs(c.ReverseRatio) * c.FinalRatio / c.DrivenRadius; k.Rpm = 2500; }
        else k.Rpm = c.IdleRpm + (k.Gear >= 1 ? c.RpmAt((float)Math.Max(0, k.Vx), Math.Max(1, k.Gear)) : 0);
        double brakeF = ctl.Brake * c.BrakeDecelMax * m * Math.Sign(k.Vx);
        // friction ellipse per axle (driven: rear unless FWD; AWD split)
        double fr = c.Traction == "FWD" ? 0 : c.Traction == "AWD" ? 0.5 : 1;
        double FxR = drive * fr - brakeF * (1 - c.BrakeFrontShare), FxF = drive * (1 - fr) - brakeF * c.BrakeFrontShare;
        double capR = c.MuLongRear * grip * fzR, capF = c.MuLongFront * grip * fzF;
        double availR = Math.Sqrt(Math.Max(0, capR * capR - FyR * FyR)), availF = Math.Sqrt(Math.Max(0, capF * capF - FyF * FyF));
        k.DrivenSlip = (fr > 0 && Math.Abs(FxR) > availR && FxR > 0) || (fr < 1 && FxF > availF) ? 0.2 : 0.0;
        FxR = Math.Max(-availR, Math.Min(availR, FxR)); FxF = Math.Max(-availF, Math.Min(availF, FxF));
        if (Math.Abs(k.Vx) < 0.3 && ctl.Brake > 0.1) { FxR = 0; FxF = 0; k.Vx *= 0.9; }
        double drag = c.Drag((float)v) + c.RollingN * (v > 0.2 ? 1 : 0);
        double Fx = FxR + FxF * Math.Cos(delta) - FyF * Math.Sin(delta) - drag * Math.Sign(k.Vx);
        double Fy = FyR + FyF * Math.Cos(delta) + FxF * Math.Sin(delta);
        double Mz = a * (FyF * Math.Cos(delta) + FxF * Math.Sin(delta)) - b * FyR;
        if (v < 2.0)
        {   // low speed: kinematic bicycle (no tyre slip)
            k.Vx += (Fx / m) * dt; k.Vy = 0; k.R = k.Vx * Math.Tan(delta) / wb;
        }
        else
        {
            k.Ax += (Fx / m - k.Ax) * Math.Min(1.0, dt / 0.12); // suspension pitch lag ~0.12 s
            k.Vx += (Fx / m + k.R * k.Vy) * dt;
            k.Vy += (Fy / m - k.R * k.Vx) * dt;
            k.R += (Mz / Iz) * dt;
        }
        double lat = Math.Abs(Fy / m / 9.81); if (v > 10 && lat > k.MaxLatG) k.MaxLatG = lat;
        k.Psi += k.R * dt;
        double sx = Math.Sin(k.Psi), cz = Math.Cos(k.Psi);
        // fwd = (sin, cos), left = (cos, -sin)
        k.X += (k.Vx * sx + k.Vy * cz) * dt;
        k.Z += (k.Vx * cz - k.Vy * sx) * dt;
    }

    static int Cmd_aisim(List<string> a)
    {
        var c = AcContent.Scan(_install);
        string carId = a.Count > 1 ? a[1] : "ferrari_458", track = a.Count > 2 ? a[2] : "magione", layout = a.Count > 3 ? a[3] : "";
        int laps = a.Count > 4 ? int.Parse(a[4]) : 3, level = a.Count > 5 ? int.Parse(a[5]) : 100;
        string png = a.Count > 6 && a[6] != "-" ? a[6] : null;
        string gv = Environment.GetEnvironmentVariable("AI_GRIP"); if (!string.IsNullOrEmpty(gv)) AcAiPlan.GripAtLevel100 = float.Parse(gv, Inv);
        string ltv = Environment.GetEnvironmentVariable("AI_LT"); if (!string.IsNullOrEmpty(ltv)) LoadTransfer = double.Parse(ltv, Inv);
        string tune = Environment.GetEnvironmentVariable("AI_TUNE");
        if (!string.IsNullOrEmpty(tune)) { var tp = tune.Split(','); AcAiBrain.YawP = float.Parse(tp[0], Inv); AcAiBrain.YawI = float.Parse(tp[1], Inv); AcAiBrain.UseVelocityFrame = tp[2] == "1"; }
        double traceA = a.Count > 7 ? double.Parse(a[7], Inv) : -1, traceB = a.Count > 8 ? double.Parse(a[8], Inv) : -1;
        var car = AiCar(c, carId); var l = FindLayout(c, track, layout);
        if (car == null || l == null || l.FastLanePath == null) { Console.WriteLine("no car / layout / fast lane"); return 1; }
        var s = AcAiSpline.Read(l.FastLanePath);
        var tr = new AcAiTrack(s);
        float grip = RoadGrip(l);
        AcAiHints hints = Environment.GetEnvironmentVariable("AI_NOHINTS") == "1" ? null : AcAiHints.Load(l.DataDir);
        if (hints != null) Console.WriteLine("   ai_hints.ini: " + hints + ", danger points " + hints.ApplyDanger(tr));
        var plan = new AcAiPlan(tr, car, level, grip, float.PositiveInfinity, hints);
        if (hints != null || plan.CrestPoints > 0) Console.WriteLine(string.Format(Inv, "   plan: {0} hinted points, {1} crest points, fuel {2:0.000} l/lap", plan.HintedPoints, plan.CrestPoints, plan.FuelPerLap));
        var brain = new AcAiBrain(car, tr, plan) { HalfWidth = 0.95f };
        var k = new SimCar { C = car };
        // start 60 m before the line, standing, on the line heading along it
        float sx, sz, tx, tz, kk, sl, sr; float s0 = tr.WrapS(-60f);
        tr.Sample(s0, out sx, out sz, out tx, out tz, out kk, out sl, out sr);
        k.X = sx; k.Z = sz; k.Psi = Math.Atan2(tx, tz);
        var timer = new AcAiLapTimer(); timer.Begin(0, s0 / tr.Length, true);
        double t = 0, dtFrame = 1.0 / 50.0; int sub = 6;
        int lapsDone = 0; var lapTimes = new List<int>();
        double maxOut = 0; int outFrames = 0, frames = 0, shifts = 0; double sumAbsLat = 0;
        var path = new List<float>();
        double maxT = laps * plan.LapTime * 2.5 + 60;
        int lastGear = 0, excursions = 0; double lastExc = -100;
        while (lapsDone < laps && t < maxT)
        {
            double sxn = Math.Sin(k.Psi), czn = Math.Cos(k.Psi);
            var e = new AcAiSense { X = (float)k.X, Y = s.Y(Math.Max(0, brain.Hint)), Z = (float)k.Z, FwdX = (float)sxn, FwdZ = (float)czn, VelX = (float)(k.Vx * sxn + k.Vy * czn), VelZ = (float)(k.Vx * czn - k.Vy * sxn), Speed = (float)k.Vx, YawRate = (float)k.R,
                Gear = k.Gear, GearCount = car.GearCount, Rpm = (float)k.Rpm, Shifting = k.ShiftT > 0, DrivenSlip = (float)k.DrivenSlip, Dt = (float)dtFrame };
            var ctl = brain.Step(ref e);
            if (ctl.ShiftUp && k.Gear < car.GearCount) { k.Gear = k.Gear == -1 ? 0 : k.Gear + 1; k.ShiftT = k.Gear <= 1 ? 0.15 : 0.06; shifts++; }
            if (ctl.ShiftDown && k.Gear > -1) { k.Gear = k.Gear - 1; k.ShiftT = 0.12; shifts++; }
            for (int i = 0; i < sub; i++) SimStep(k, ctl, dtFrame / sub, grip);
            t += dtFrame; frames++;
            float lat = brain.Lateral;
            float room = lat > 0 ? tr.SideL[Math.Max(0, brain.Hint)] : tr.SideR[Math.Max(0, brain.Hint)];
            double over = Math.Abs(lat) + 0.95 - room; // car edge beyond the line's room
            if ((Math.Abs(k.Vy) > 2.0 || Math.Abs(brain.HeadingError) > 0.35) && excursions < 6 && t > lastExc + 5)
            {
                excursions++; lastExc = t;
                Console.WriteLine(string.Format(Inv, "   excursion: t {0:0.0} s {1:0} (pt {2}) v {3:0} km/h target {4:0} plan {5:0} k {6:0.0000} lat {7:0.0} steer {8:0.0} vy {9:0.0} r {10:0.00} thr {11:0.00} brk {12:0.00} gear {13} he {14:0.00}",
                    t, brain.S, brain.Hint, k.Vx * 3.6, brain.TargetSpeed * 3.6, plan.SpeedAt(brain.S) * 3.6, tr.Curv[Math.Max(0, brain.Hint)], lat, ctl.RoadAngleDeg, k.Vy, k.R, ctl.Throttle, ctl.Brake, k.Gear, brain.HeadingError));
            }
            if (t >= traceA && t <= traceB && frames % 5 == 0)
                Console.WriteLine(string.Format(Inv, "   t {0:0.0} s {1:0} v {2:0} tgt {3:0} k {4:0.0000} lat {5:0.00} off {6:0.0} steer {7:0.0} vy {8:0.00} r {9:0.00} thr {10:0.00} brk {11:0.00} g {12} he {13:0.00}",
                    t, brain.S, k.Vx * 3.6, brain.TargetSpeed * 3.6, tr.Curv[Math.Max(0, brain.Hint)], lat, brain.Offset, ctl.RoadAngleDeg, k.Vy, k.R, ctl.Throttle, ctl.Brake, k.Gear, brain.HeadingError));
            if (over > maxOut) maxOut = over;
            if (over > 0.5) outFrames++;
            sumAbsLat += Math.Abs(lat);
            if (frames % 10 == 0) { path.Add((float)k.X); path.Add((float)k.Z); }
            if (timer.Feed(t, brain.S / tr.Length)) { lapsDone++; lapTimes.Add(timer.LastMs); Console.WriteLine("   lap " + lapsDone + " " + Lap(timer.LastMs / 1000.0) + " | " + brain.DiagText()); brain.DiagReset(); }
            if (k.Gear != lastGear) lastGear = k.Gear;
        }
        var sb = new StringBuilder();
        foreach (var ms in lapTimes) sb.Append(Lap(ms / 1000.0)).Append(' ');
        Console.WriteLine(string.Format(Inv, "{0} on {1} level {2}: plan {3}, laps {4}; time {5:0.0} s; max edge beyond the room {6:0.00} m, frames > 0.5 m out {7} of {8}; mean |lateral| {9:0.00} m; shifts {10}; max lat {11:0.00} g",
            carId, l.Key, level, Lap(plan.LapTime), sb.ToString(), t, maxOut, outFrames, frames, sumAbsLat / Math.Max(1, frames), shifts, k.MaxLatG));
        if (png != null) DrawSim(png, tr, path);
        return lapsDone >= laps ? 0 : 1;
    }

    static void DrawSim(string pngPath, AcAiTrack tr, List<float> path)
    {
        var s = tr.Spline;
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < s.Count; i++) { minX = Math.Min(minX, s.X(i)); maxX = Math.Max(maxX, s.X(i)); minZ = Math.Min(minZ, s.Z(i)); maxZ = Math.Max(maxZ, s.Z(i)); }
        int W = 1600, H = 1600; float pad = 20f;
        float sc = Math.Min((W - 2 * pad) / (maxX - minX), (H - 2 * pad) / (maxZ - minZ));
        var px = new byte[W * H * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 20; px[i + 1] = 22; px[i + 2] = 28; px[i + 3] = 255; }
        Action<float, float, byte, byte, byte> dot = (x, z, r, g, b) =>
        {
            int u = (int)(W - pad - (x - minX) * sc), v = (int)(H - pad - (z - minZ) * sc); // AC +X = left: mirror so the map reads as seen from above
            if (u < 0 || v < 0 || u >= W || v >= H) return;
            int o = (v * W + u) * 4; px[o] = r; px[o + 1] = g; px[o + 2] = b;
        };
        for (int i = 0; i < s.Count; i++)
        {
            float x = s.X(i), z = s.Z(i), tx = tr.TanX[i], tz = tr.TanZ[i];
            dot(x + tz * tr.SideL[i], z - tx * tr.SideL[i], 200, 200, 200);
            dot(x - tz * tr.SideR[i], z + tx * tr.SideR[i], 200, 200, 200);
            dot(x, z, 60, 90, 160);
        }
        for (int i = 0; i + 1 < path.Count; i += 2) dot(path[i], path[i + 1], 255, 80, 40);
        Png.Write(pngPath, W, H, px);
        Console.WriteLine("picture " + pngPath);
    }

    static int Cmd_aitimer(List<string> a)
    {
        int fails = 0;
        Func<bool, string, int> check = (ok, what) => { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); return ok ? 0 : 1; };
        // race, grid BEFORE the line (magione-like, progress 0.998): first crossing is the start, lap 1 timed from the green
        var t = new AcAiLapTimer(); t.SetGates(new[] { 0f, 0.5f });
        t.Begin(0, 0.998f, true);
        double now = 0; float p = 0.998f; int laps = 0;
        for (int i = 0; i < 20000 && laps < 2; i++) { now += 0.02; p += 0.0002f; if (p >= 1f) p -= 1f; if (t.Feed(now, p)) laps++; }
        fails += check(t.Laps == 2 && Math.Abs(t.LastMs - 100000) < 40 && Math.Abs(t.BestMs - 100000) < 40, "grid before the line: laps " + t.Laps + " last " + t.LastMs + " best " + t.BestMs + " (lap 1 from the green = 100.2 s incl. the 0.2 s to the line, lap 2 = 100 s)");
        fails += check(t.LastSectorMs.Length == 2 && Math.Abs(t.LastSectorMs[0] - 50000) < 40 && Math.Abs(t.LastSectorMs[1] - 50000) < 40, "sectors " + string.Join(",", t.LastSectorMs));
        // race, grid AFTER the line (monza-like, progress 0.0535): first crossing = lap 1
        t = new AcAiLapTimer(); t.Begin(0, 0.0535f, true); now = 0; p = 0.0535f; laps = 0;
        for (int i = 0; i < 10000 && laps < 1; i++) { now += 0.02; p += 0.0002f; if (p >= 1f) p -= 1f; if (t.Feed(now, p)) laps++; }
        fails += check(t.Laps == 1 && Math.Abs(t.LastMs - 94650) < 30, "grid after the line: lap 1 = " + t.LastMs + " ms (expected 94650)");
        fails += check(Math.Abs(t.RaceDistance - 1f) < 0.01f, "race distance at the line " + t.RaceDistance.ToString("0.000", Inv));
        // practice: out lap not counted, reversing over the line takes the crossing back
        t = new AcAiLapTimer(); t.Begin(0, 0.6f, false); now = 0; p = 0.6f; laps = 0;
        for (int i = 0; i < 10000 && p < 0.99f; i++) { now += 0.02; p += 0.0002f; t.Feed(now, p); }
        now += 0.02; p = 0.001f; t.Feed(now, p);  // cross forward: starts timing
        now += 0.02; p = 0.999f; t.Feed(now, p);  // back over the line
        now += 0.02; p = 0.002f; bool l1 = t.Feed(now, p); // forward again: must NOT be a lap
        fails += check(!l1 && t.Laps == 0 && t.Timing, "practice: out lap + reverse wiggle -> no lap (laps " + t.Laps + ")");
        Console.WriteLine(fails == 0 ? "aitimer: all passed" : "aitimer: " + fails + " failed");
        return fails;
    }
}

static partial class Program
{
    // aistab <car> <kmh> <steerRightDeg>: open-loop check of the probe's bicycle model (constant speed, constant steer)
    static int Cmd_aistab(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var car = AiCar(c, a.Count > 1 ? a[1] : "ferrari_458");
        double v = (a.Count > 2 ? double.Parse(a[2], Inv) : 200) / 3.6; float st = a.Count > 3 ? float.Parse(a[3], Inv) : 0.5f;
        var k = new SimCar { C = car, Vx = v, Gear = 5 };
        var ctl = new AcAiControls { RoadAngleDeg = st };
        for (int i = 0; i < 3000; i++)
        {
            SimStep(k, ctl, 1.0 / 300, 1f);
            k.Vx = v;
            if (i % 150 == 0) Console.WriteLine(string.Format(Inv, "t {0:0.0} vy {1:0.00} r {2:0.000} latG {3:0.00}", i / 300.0, k.Vy, k.R, Math.Abs(k.R * v) / 9.81));
        }
        return 0;
    }
}

static partial class Program
{
    // aipit <car> <track> [layout]: from beside the pit line (a pit box 7 m off it) along pit_lane.ai at the 80 km/h limit, onto fast_lane.ai
    // at the pit line's end (as Game/Ai does), 600 m on: max edge beyond the room on the fast lane, top speed in the pit lane
    static int Cmd_aipit(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var car = AiCar(c, a.Count > 1 ? a[1] : "ferrari_458"); var l = FindLayout(c, a.Count > 2 ? a[2] : "magione", a.Count > 3 ? a[3] : "");
        if (car == null || l == null || l.FastLanePath == null || l.PitLanePath == null) { Console.WriteLine("no car / layout / lanes"); return 1; }
        var fast = new AcAiTrack(AcAiSpline.Read(l.FastLanePath)); var pit = new AcAiTrack(AcAiSpline.Read(l.PitLanePath));
        float grip = RoadGrip(l);
        var fp = new AcAiPlan(fast, car, 100, grip); var pp = new AcAiPlan(pit, car, 100, grip, 80f / 3.6f);
        var br = new AcAiBrain(car, pit, pp);
        float waitS, exitS, exitFast; bool exitOk = AcAiTrack.PitExit(pit, fast, out waitS, out exitS, out exitFast);
        Console.WriteLine(exitOk ? string.Format(Inv, "   pit exit: wait at {0:0} m, join at {1:0} m of {2:0} m", waitS, exitS, pit.Length) : "   no pit exit found - leaving 6 m before the line's end");
        float x, z, tx, tz, k, sl, sr; float s0 = exitOk ? Math.Max(0f, waitS - 150f) : pit.Length * 0.45f; // a pit box 150 m before the exit
        pit.Sample(s0, out x, out z, out tx, out tz, out k, out sl, out sr);
        var sc = new SimCar { C = car };
        sc.X = x + tz * 7f; sc.Z = z - tx * 7f; sc.Psi = Math.Atan2(tx, tz); // 7 m to the left of the pit line, parallel
        double t = 0; bool onPit = true; double maxPitV = 0, maxOut = 0, fastFrom = -1; int frames = 0;
        while (t < 200)
        {
            double sn = Math.Sin(sc.Psi), cn = Math.Cos(sc.Psi);
            var e = new AcAiSense { X = (float)sc.X, Z = (float)sc.Z, Y = 0, FwdX = (float)sn, FwdZ = (float)cn, VelX = (float)(sc.Vx * sn + sc.Vy * cn), VelZ = (float)(sc.Vx * cn - sc.Vy * sn),
                Speed = (float)sc.Vx, YawRate = (float)sc.R, Gear = sc.Gear, GearCount = car.GearCount, Rpm = (float)sc.Rpm, Shifting = sc.ShiftT > 0, Dt = 0.02f };
            e.Y = (onPit ? pit : fast).Spline.Y(Math.Max(0, br.Hint));
            if (onPit) br.SessionCap = 80f / 3.6f * 0.95f; else br.SessionCap = float.PositiveInfinity;
            var ctl = br.Step(ref e);
            if (ctl.ShiftUp && sc.Gear < car.GearCount) { sc.Gear++; sc.ShiftT = 0.08; }
            if (ctl.ShiftDown && sc.Gear > -1) { sc.Gear--; sc.ShiftT = 0.12; }
            for (int i = 0; i < 6; i++) SimStep(sc, ctl, 0.02 / 6, grip);
            t += 0.02; frames++;
            if (onPit) { if (sc.Vx > maxPitV) maxPitV = sc.Vx; if (exitOk ? br.S >= exitS && br.S < exitS + 50f : pit.Length - br.S < 6f) { onPit = false; br.SetLine(fast, fp); fastFrom = -1; Console.WriteLine(string.Format(Inv, "   left the pit line at t {0:0.0} s, {1:0} km/h", t, sc.Vx * 3.6)); } }
            else
            {
                if (fastFrom < 0) fastFrom = br.S;
                float room = br.Lateral > 0 ? fast.SideL[Math.Max(0, br.Hint)] : fast.SideR[Math.Max(0, br.Hint)];
                double over = Math.Abs(br.Lateral) + 0.95 - room; if (over > maxOut) maxOut = over;
                if (fast.Delta((float)fastFrom, br.S) > 600f) break;
            }
        }
        Console.WriteLine(string.Format(Inv, "{0} pit exit on {1}: pit line {2:0} m, max pit speed {3:0.0} km/h (limit 80), then on the fast lane max edge beyond the room {4:0.00} m, t {5:0.0} s{6}",
            car.CarId, l.Key, pit.Length, maxPitV * 3.6, maxOut, t, onPit ? " - NEVER LEFT THE PIT LINE" : ""));
        return onPit ? 1 : 0;
    }
}

static partial class Program
{
    // aihints [car]: every owned layout - ai_hints.ini contents, points it changes, crest points, planned lap with / without hints and crests,
    // fuel per lap and the race fuel load (5 laps, FUEL_RATE 100) for <car> (default ferrari_458)
    static int Cmd_aihints(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var car = AiCar(c, a.Count > 1 ? a[1] : "ferrari_458");
        if (car == null) { Console.WriteLine("no car"); return 1; }
        Console.WriteLine(string.Format(Inv, "{0}: FUEL {1} l, MAX_FUEL {2} l, CONSUMPTION {3}, FUEL_LIGHT_MIN_LITERS {4}", car.CarId, car.FuelStart, car.FuelMax, car.FuelConsumption, car.FuelLightMin));
        int fails = 0;
        var owned = new List<AcLayoutEntry>(); foreach (var t in c.Tracks) foreach (var l0 in t.Layouts) if (l0.Owned) owned.Add(l0);
        foreach (var l in owned)
        {
            if (l.FastLanePath == null || !File.Exists(l.FastLanePath)) continue;
            var s = AcAiSpline.Read(l.FastLanePath);
            if (s.Count < 3) continue;
            float grip = RoadGrip(l);
            var flatTr = new AcAiTrack(s);
            // the plan without crests: zero the vertical curvature of a private copy
            var noCrest = new AcAiTrack(s); for (int i = 0; i < noCrest.Count; i++) noCrest.VCurv[i] = 0f;
            var p0 = new AcAiPlan(noCrest, car, 100, grip);
            var p1 = new AcAiPlan(flatTr, car, 100, grip);
            AcAiHints h = AcAiHints.Load(l.DataDir);
            var trH = new AcAiTrack(s);
            int danger = h != null ? h.ApplyDanger(trH) : 0;
            var p2 = new AcAiPlan(trH, car, 100, grip, float.PositiveInfinity, h);
            float race = AcAiPlan.FuelLoad(car, p2.FuelPerLap, p2.LapTime, true, 5, 0, 1f);
            float minKv = 0f; for (int i = 0; i < flatTr.Count; i++) minKv = Math.Min(minKv, flatTr.VCurv[i]);
            Console.WriteLine(string.Format(Inv, "{0,-36} lap {1} | crests {2,4} pts (min R {3,6:0} m) -> {4} | hints {5} pts, danger {6} pts -> {7} | fuel {8:0.000} l/lap, 5-lap race load {9:0.0} l{10}",
                l.Key, Lap(p0.LapTime), p1.CrestPoints, minKv < 0 ? -1f / minKv : 0f, Lap(p1.LapTime), p2.HintedPoints, danger, Lap(p2.LapTime), p2.FuelPerLap, race,
                h != null ? "  [" + h + "]" : ""));
            if (h != null && h.Problems.Count > 0) fails++;
            if (p2.LapTime <= 1f || float.IsNaN(p2.LapTime)) fails++;
        }
        Console.WriteLine(fails == 0 ? "aihints: OK" : "aihints: " + fails + " problem(s)");
        return fails == 0 ? 0 : 1;
    }
}

static partial class Program
{
    // airef: Kunos' hotlap special events (content/specialevents) with an owned car on an owned layout - the external lap references
    // (gold = hardest TIME objective). Prints "REF <car> <track> <layout|-> <gold ms>" lines for a sim sweep.
    static int Cmd_airef(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var all = AcAiReference.All(_install.Root);
        int owned = 0;
        foreach (var r in all)
        {
            var car = c.Car(r.Car); var l = c.Layout(r.Track, r.Layout);
            bool ok = car != null && car.Owned && !car.IsDlcStub && l != null && l.Owned && l.FastLanePath != null;
            Console.WriteLine((ok ? "REF " + r.Car + " " + r.Track + " " + (r.Layout.Length > 0 ? r.Layout : "-") + " " + r.GoldMs + "   " : "    (not owned) ") + r);
            if (ok) owned++;
        }
        Console.WriteLine(all.Count + " hotlap events with time objectives, " + owned + " with an owned car on an owned layout");
        return 0;
    }
}

static partial class Program
{
    // aimerge: AcAiTrack.PitExit (what Game/Ai uses) on every owned layout: the pit section of pit_lane.ai, where the AI waits for traffic
    // and where it joins the fast lane (and the pit limit ends), against the pit line's length
    static int Cmd_aimerge(List<string> a)
    {
        var c = AcContent.Scan(_install);
        int none = 0;
        foreach (var t in c.Tracks) foreach (var l in t.Layouts)
        {
            if (!l.Owned || l.FastLanePath == null || l.PitLanePath == null || !File.Exists(l.PitLanePath)) continue;
            var fast = new AcAiTrack(AcAiSpline.Read(l.FastLanePath)); var pit = new AcAiTrack(AcAiSpline.Read(l.PitLanePath));
            if (fast.IsEmpty || pit.IsEmpty || !fast.HasSides) { Console.WriteLine(string.Format("{0,-36} no sides / lines", l.Key)); continue; }
            float waitS, exitS, exitFast;
            bool ok = AcAiTrack.PitExit(pit, fast, out waitS, out exitS, out exitFast);
            if (!ok) none++;
            Console.WriteLine(ok ? string.Format(Inv, "{0,-36} pit line {1,5:0} m: wait at {2,5:0} m, joins the fast lane at {3,5:0} m (fast s {4:0}), {5:0} m of the line after it",
                                       l.Key, pit.Length, waitS, exitS, exitFast, pit.Length - exitS)
                                 : string.Format("{0,-36} pit line {1,5:0} m: no pit exit found (old rule: leave 6 m before the end)", l.Key, pit.Length));
        }
        Console.WriteLine("aimerge: " + none + " layout(s) without a pit exit");
        return 0;
    }
}

static partial class Program
{
    // aipitlat <track> [layout]: lateral distance of pit_lane.ai from fast_lane.ai every 25 m (and beyond the fast lane's room)
    static int Cmd_aipitlat(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = FindLayout(c, a.Count > 1 ? a[1] : "magione", a.Count > 2 ? a[2] : "");
        var fast = new AcAiTrack(AcAiSpline.Read(l.FastLanePath)); var pit = new AcAiTrack(AcAiSpline.Read(l.PitLanePath));
        var sp = pit.Spline; int hint = -1; float next = 0;
        for (int i = 0; i < pit.Count; i++)
        {
            if (pit.S(i) < next) continue; next = pit.S(i) + 25f;
            float lat; float sf = fast.Project(sp.X(i), sp.Y(i), sp.Z(i), ref hint, out lat);
            int k = fast.IndexAt(sf); float room = lat > 0 ? fast.SideL[k] : fast.SideR[k];
            Console.WriteLine(string.Format(Inv, "pit s {0,6:0}  fast s {1,6:0}  lat {2,7:0.0}  beyond room {3,6:0.0}", pit.S(i), sf, lat, Math.Abs(lat) - room));
        }
        return 0;
    }
}

static partial class Program
{
    // aiopen [car]: open-line plans as Game/Ai builds them - trento-bondone run-through (end speed AcAiPlan.RunOutSpeed) and ks_drag stop
    // 40 m past a finish at 0.81 of the strip (R: AC_OPEN_FINISH 0.08..0.81): speed at the end / at the stop point, planned run time
    static int Cmd_aiopen(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var car = AiCar(c, a.Count > 1 ? a[1] : "ferrari_458");
        var t = FindLayout(c, "trento-bondone", ""); var d = FindLayout(c, "ks_drag", "drag1000");
        int fails = 0;
        if (t != null && t.FastLanePath != null)
        {
            var tr = new AcAiTrack(AcAiSpline.Read(t.FastLanePath));
            var p0 = new AcAiPlan(tr, car, 100, RoadGrip(t));
            var p1 = new AcAiPlan(tr, car, 100, RoadGrip(t), float.PositiveInfinity, null, -1f, AcAiPlan.RunOutSpeed);
            Console.WriteLine(string.Format(Inv, "trento-bondone {0:0} m: stop-at-end plan {1} (end {2:0.0} m/s, 30 m before {3:0.0}), run-through plan {4} (end {5:0.0} m/s, 30 m before {6:0.0})",
                tr.Length, Lap(p0.LapTime), p0.V[tr.Count - 1], p0.SpeedAt(tr.Length - 30f), Lap(p1.LapTime), p1.V[tr.Count - 1], p1.SpeedAt(tr.Length - 30f)));
            if (p1.V[tr.Count - 1] < 5f) fails++;
        }
        if (d != null && d.FastLanePath != null)
        {
            var tr = new AcAiTrack(AcAiSpline.Read(d.FastLanePath));
            // what Game/Ai does on ks_drag: stop at the strip's end (a stop 40 m past the finish would brake before the finish - shown too)
            float fin = tr.Length * 0.81f, stop = fin + 40f;
            var p = new AcAiPlan(tr, car, 100, RoadGrip(d), float.PositiveInfinity, null);
            var q = new AcAiPlan(tr, car, 100, RoadGrip(d), float.PositiveInfinity, null, stop, 0f);
            Console.WriteLine(string.Format(Inv, "ks_drag {0:0} m: finish {1:0} m crossed at {2:0.0} km/h (stop at the strip's end; a stop 40 m past the finish would cross at {3:0.0}); 30 m before the end {4:0.0} km/h; run {5}",
                tr.Length, fin, p.SpeedAt(fin) * 3.6f, q.SpeedAt(fin) * 3.6f, p.SpeedAt(tr.Length - 30f) * 3.6f, Lap(p.LapTime)));
            if (p.SpeedAt(fin) < 40f) fails++;
        }
        Console.WriteLine(fails == 0 ? "aiopen: OK" : "aiopen: " + fails + " problem(s)");
        return fails;
    }
}
