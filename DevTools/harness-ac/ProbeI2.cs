using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumAssetto;

// Module I probe (polish pass 2026-11-01): the AI brain driving P's REAL chassis (AcChassis + AcCarCore on ProbeP's rigid body, flat
// ground y = 0) along a layout's fast lane in 2-D - the same physics the Unity AI car runs, without PhysX / the track mesh.
//   aichassis <car> <track> [layout] [laps] [framesPerStep] [trace from-to s]
//   framesPerStep: how many brain frames run per 50 Hz physics step with the same (stale) sense - 1 = the harness pace, 40 = the batch lab
//   (~2000 fps), 2 = a 100 Hz client.
static partial class Program
{
    static int TraceEvery = 10;
    static int Cmd_aichassis(List<string> a)
    {
        var c = AcContent.Scan(_install);
        string carId = a.Count > 1 ? a[1] : "ks_toyota_gt86", track = a.Count > 2 ? a[2] : "magione", layout = a.Count > 3 && a[3] != "-" ? a[3] : "";
        int laps = a.Count > 4 ? int.Parse(a[4]) : 2, fps = a.Count > 5 ? int.Parse(a[5]) : 1;
        string te0 = Environment.GetEnvironmentVariable("AI_TRACE_EVERY"); if (!string.IsNullOrEmpty(te0)) TraceEvery = int.Parse(te0);
        float trA = a.Count > 6 ? float.Parse(a[6], CultureInfo.InvariantCulture) : -1f, trB = a.Count > 7 ? float.Parse(a[7], CultureInfo.InvariantCulture) : -1f;
        var entry = c.Car(carId);
        var car = AiCar(c, carId); var l = FindLayout(c, track, layout);
        if (entry == null || car == null || l == null || l.FastLanePath == null) { Console.WriteLine("no car / layout / fast lane"); return 1; }
        AcCarSpec spec = AcCarSpec.Load(entry);
        var s = AcAiSpline.Read(l.FastLanePath);
        var tr = new AcAiTrack(s);
        float grip = RoadGrip(l);
        AcAiHints hints = AcAiHints.Load(l.DataDir);
        if (hints != null) hints.ApplyDanger(tr);
        string gv = Environment.GetEnvironmentVariable("AI_GRIP"); if (!string.IsNullOrEmpty(gv)) AcAiPlan.GripAtLevel100 = float.Parse(gv, CultureInfo.InvariantCulture);
        var plan = new AcAiPlan(tr, car, 100, grip, float.PositiveInfinity, hints);
        var brain = new AcAiBrain(car, tr, plan) { HalfWidth = 0.95f };
        string scv = Environment.GetEnvironmentVariable("AI_SCALE"); if (!string.IsNullOrEmpty(scv)) brain.SpeedScale = float.Parse(scv, CultureInfo.InvariantCulture);
        const float dt = 0.02f;
        var sim = new ChassisSim(spec, dt);
        sim.Core.AutoShift = false; sim.Core.AbsMode = 1; sim.Core.TcMode = 1;
        for (int i = 0; i < 4; i++) sim.Ch.Wheels[i].Grip = grip;
        // place 60 m before point 0, on the line, heading along it (Unity space: x = -x_ac)
        float sx, sz, tx, tz, kk, sl, sr; float s0 = tr.WrapS(-60f);
        tr.Sample(s0, out sx, out sz, out tx, out tz, out kk, out sl, out sr);
        float yaw = (float)Math.Atan2(-tx, tz);
        sim.B.P = new AcVec3(-sx, sim.B.P.Y, sz);
        sim.B.Qy = (float)Math.Sin(yaw * 0.5); sim.B.Qw = (float)Math.Cos(yaw * 0.5);
        sim.Core.Brake = 1f; for (int k = 0; k < 50; k++) sim.Step();
        var timer = new AcAiLapTimer(); timer.Begin(0, s0 / tr.Length, true);
        double t = 0; int lapsDone = 0; double maxOut = 0, maxT = laps * plan.LapTime * 3 + 60; int resets = 0;
        float steer = 0f;
        Console.WriteLine(string.Format(Inv, "aichassis {0} on {1}: plan {2}, grip {3:0.00}, brain frames per 50 Hz step {4}, chassis substeps {5}", carId, l.Key, Lap(plan.LapTime), grip, fps, sim.N));
        while (lapsDone < laps && t < maxT)
        {
            AcVec3 f = sim.B.Fwd, v = sim.B.V;
            float fl = (float)Math.Sqrt(f.X * f.X + f.Z * f.Z);
            var e = new AcAiSense { X = -sim.B.P.X, Y = s.Y(Math.Max(0, brain.Hint)), Z = sim.B.P.Z, FwdX = -f.X / fl, FwdZ = f.Z / fl, VelX = -v.X, VelZ = v.Z, YawRate = -sim.B.W.Y,
                Gear = sim.Core.Gear, GearCount = sim.Core.GearCount, Rpm = sim.Core.Rpm, Shifting = sim.Core.Shifting, Dt = dt / fps };
            e.Speed = e.VelX * e.FwdX + e.VelZ * e.FwdZ;
            float dr = 0f;
            for (int i = 0; i < 4; i++) if (sim.Core.Driven(i) && sim.Core.SlipRatio[i] > dr) dr = sim.Core.SlipRatio[i];
            e.DrivenSlip = dr;
            AcAiControls ctl = default;
            bool up = false, down = false;
            for (int k = 0; k < fps; k++) { ctl = brain.Step(ref e); up |= ctl.ShiftUp; down |= ctl.ShiftDown; }
            if (up) sim.Core.ShiftUp();
            if (down) sim.Core.ShiftDown();
            sim.Core.Gas = ctl.Throttle; sim.Core.Brake = ctl.Brake; sim.Core.Handbrake = ctl.Handbrake;
            steer = ctl.RoadAngleDeg;
            sim.Steer = steer; sim.Core.SteerNorm = steer * spec.SteerRatio / Math.Max(1f, spec.SteerLockDeg);
            sim.Step(); t += dt;
            float lat = brain.Lateral;
            int hi = Math.Max(0, brain.Hint);
            double over = Math.Abs(lat) + 0.95 - (lat > 0 ? tr.SideL[hi] : tr.SideR[hi]);
            if (over > maxOut) maxOut = over;
            if (t >= trA && t <= trB && ((int)Math.Round(t / dt)) % TraceEvery == 0)
                Console.WriteLine(string.Format(Inv, "   t {0:0.00} s {1:0} v {2:0} tgt {3:0} plan {4:0} lat {5:0.0} steer {6:0.0} r {7:0.00} thr {8:0.00} brk {9:0.00} g {10} rpm {11:0} he {12:0.00} slip {13:0.00} sat {14} ov {15} | ce {16:0.00} ge {17:0.00} te {18:0} dt2 {19:0} wv {20:0.0} sh {21} up {22} dn {23}",
                    t, brain.S, e.Speed * 3.6f, brain.TargetSpeed * 3.6f, plan.SpeedAt(brain.S) * 3.6f, lat, steer, e.YawRate, ctl.Throttle, ctl.Brake, sim.Core.Gear, sim.Core.Rpm, brain.HeadingError, dr, brain.Saturated ? 1 : 0, brain.Oversteer ? 1 : 0,
                    sim.Core.ClutchEngage, sim.Core.GasEffective, sim.Core.EngineTorque, sim.Core.DriveTorque[2], sim.Core.Omega[2] * spec.WheelRadius[2] * 3.6f, sim.Core.Shifting ? 1 : 0, up ? 1 : 0, down ? 1 : 0));
            if (over > 10 || sim.B.Up.Y < 0.5f)
            {
                resets++;
                Console.WriteLine(string.Format(Inv, "   RESET {0}: t {1:0.0} s {2:0} v {3:0} lat {4:0.0}", resets, t, brain.S, e.Speed * 3.6f, lat));
                if (resets > 8) break;
                float rs = brain.S + 10f, x2, z2, t2x, t2z, k2, l2, r2;
                tr.Sample(rs, out x2, out z2, out t2x, out t2z, out k2, out l2, out r2);
                float y2 = (float)Math.Atan2(-t2x, t2z);
                sim.B.P = new AcVec3(-x2, spec.PhysicsCgHeightM + 0.01f, z2); sim.B.V = new AcVec3(0, 0, 0); sim.B.W = new AcVec3(0, 0, 0);
                sim.B.Qx = 0; sim.B.Qz = 0; sim.B.Qy = (float)Math.Sin(y2 * 0.5); sim.B.Qw = (float)Math.Cos(y2 * 0.5);
                brain.ResetState();
            }
            if (timer.Feed(t, brain.S / tr.Length)) { lapsDone++; Console.WriteLine("   lap " + lapsDone + " " + Lap(timer.LastMs / 1000.0) + " (x" + (timer.LastMs / 1000.0 / plan.LapTime).ToString("0.000", Inv) + ") | " + brain.DiagText()); brain.DiagReset(); }
        }
        Console.WriteLine(string.Format(Inv, "done: laps {0}, t {1:0.0} s, max edge beyond the room {2:0.0} m, resets {3}", lapsDone, t, maxOut, resets));
        return 0;
    }
}
