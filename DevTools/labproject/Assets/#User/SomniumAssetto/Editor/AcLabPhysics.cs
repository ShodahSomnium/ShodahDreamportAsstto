using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [P, Physics] Lab scenarios (DevTools/lab.sh &lt;name&gt;), all with the real AcCarRig (C's model + P's physics) and Unity's fixed step:
    /// <list type="bullet">
    /// <item><c>physics-flat</c> [-accar ferrari_458]: a 4 km flat test plane (layer Track, tagged ROAD, far from the hub). Settle (ride
    /// height = the physics' static CG height, 4 wheels on the ground, no creep), pictures of the car resting on its wheels (side / front /
    /// rear 3/4 - wheels must sit in the arches: GRAPHICS_OFFSET / PITCH right), then 0-100 / first upshift / 0-200 / top speed (full
    /// throttle, factory TC), 100-0 braking (factory ABS), a constant-100 km/h steer ramp (max lateral g, turns right for positive steer),
    /// park / freeze / occupy / puppet modes. Every number is logged ("LAB:" and "Physics: &lt;car&gt; validation ...") and compared with
    /// the harness `pchassis` numbers with tolerances (ferrari_458 @50 Hz: 0-100 4.18 +-0.3 s, 100-0 35.4 +-3 m, 1.07 +-0.08 g; abarth500 8.76 /
    /// 38.0 / 0.94 - re-measured 2026-11-01 after the road-plane damper fix; same slip-holding test driver), plus: drawn tyres on the plane (< 2 cm), the held steering wheel updated in the frame,
    /// straight line without steering (< 3 m sideways, < 2 deg heading), the speed limiter (60 km/h, PitLimiter light), the puppet moved by
    /// MovePosition (interpolated).</item>
    /// <item><c>physics-458-magione</c> [-actrack magione -accar ferrari_458]: W's real track; the car placed in pit box 0 with ResetTo
    /// (raycast down from the floating marker, research T1), pictures resting, then 6 s at 60 % pedal without steering out of the pit box: surfaces
    /// under the wheels from AcSurfaceTag (keys logged), no fall-through, the car stays on its wheels; every 0.25 s a "trace" line (speed, rpm,
    /// gear, gas / clutch / TC, heading, yaw rate, per wheel surface / load / slip, travel, floor-box gaps) and every car impact is logged. Then
    /// 20 s steered along the fast lane from grid 0 (lab pure-pursuit driver, curvature speed limit): stability on the real road.</item>
    /// <item><c>physics-fleet</c>: every owned car spawned in turn on the flat plane, settled 2 s: a census line per car (CG height vs the
    /// chassis static CG, the drawn tyres touching the plane within 2 cm, wheels on the ground, hull colliders).</item>
    /// </list>
    /// Time.timeScale is raised to 4 during the long runs (the fixed step and so the physics are unchanged) and restored after.
    /// </summary>
    [InitializeOnLoad]
    static class AcLabPhysics
    {
        static AcLabPhysics()
        {
            AcLabRunner.Register("physics-flat", r => Flat(r));
            AcLabRunner.Register("physics-458-magione", r => Magione(r));
            AcLabRunner.Register("physics-fleet", r => Fleet(r));
            AcLabRunner.Register("physics-crash", r => Crash(r));   // iteration 2: damage
            AcLabRunner.Register("physics-engine", r => Engine(r)); // iteration 2: engine life
        }

        private const float PlaneY = 0f;                                     // tracks live at AcSpace.MapOrigin (y 0); the hub is at y 3000
        private static readonly Vector3 PlaneCentre = new Vector3(5000f, PlaneY, 5000f); // ours: away from any track's origin
        private static GameObject _plane;

        static IEnumerator Boot(AcLabRunner r)
        {
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 180f, "the content catalog");
        }

        static AcCarEntry PickCar(AcLabRunner r, string fallback)
        {
            string id = AcLabBoot.Arg("-accar", fallback);
            AcCarEntry car = r.Game.Content.Car(id);
            if (car == null || !car.Owned) { r.Fail("car '" + id + "' not owned / not found"); return null; }
            return car;
        }

        static void BuildPlane()
        {
            if (_plane != null) return;
            _plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _plane.name = "Ac Lab Physics Plane";
            _plane.layer = AcLayers.Track;
            _plane.transform.position = PlaneCentre + Vector3.down * 0.5f;
            _plane.transform.localScale = new Vector3(400f, 1f, 4400f);
            var col = _plane.GetComponent<Collider>();
            AcSurfaceTag.Register(col, new AcSurface { Key = "ROAD", Friction = 1f }, false); // system surfaces.ini ROAD = 1.0
            var mr = _plane.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (sh != null) mr.sharedMaterial = new Material(sh) { color = new Color(0.35f, 0.36f, 0.38f) };
            }
            var light = new GameObject("Ac Lab Physics Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            light.intensity = 1.2f;
        }

        static IEnumerator SpawnRig(AcLabRunner r, AcCarEntry car, AcCarKind kind, Action<AcCarRig> got)
        {
            AcCarRig rig = null;
            bool done = false;
            yield return AcCarRig.Spawn(car, car.Skins.Count > 0 ? car.Skins[0].Id : "", new AcCarRigOptions { Kind = kind, Audio = false }, x => { rig = x; done = true; });
            yield return r.Wait(() => done, 120f, "AcCarRig.Spawn " + car.Id);
            got(rig);
        }

        static void Shots(AcLabRunner r, AcCarRig rig, string prefix)
        {
            Transform t = rig.Root.transform;
            Vector3 c = t.position;
            Vector3 f = t.forward, rt = t.right;
            r.Aim(c + rt * 5.5f + Vector3.up * 0.3f, c + Vector3.up * -0.1f, 40f); r.Shot(prefix + "-side");
            r.Aim(c + f * 6f + Vector3.up * 0.5f, c, 40f); r.Shot(prefix + "-front");
            r.Aim(c - f * 5f + rt * 3.5f + Vector3.up * 1.4f, c, 45f); r.Shot(prefix + "-rear34");
            r.Aim(c + rt * 2.2f + f * 1.6f + Vector3.up * -0.05f, c + f * 1.5f + rt * 0.8f + Vector3.down * 0.15f, 35f); r.Shot(prefix + "-frontwheel");
        }

        static string F(float v, string fmt) { return v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>Harness `pchassis` numbers at 50 Hz for the cars the lab steps use (second pass, DevTools/agents/P/pchassis_all.txt): the
        /// Unity run must land within the tolerance of the SAME code on PhysX (0-100 +-0.3 s, 100-0 +-3 m, lateral +-0.08 g - // ours
        /// tolerances). Other cars: the brief's loose sanity ranges. The brief's targets (458: 0-100 3.3-3.6, ~1.3 g, 32-35 m) are logged.</summary>
        private const float REF_458_T100 = 4.18f, REF_458_BRAKE = 35.4f, REF_458_LAT = 1.07f; // pchassis ferrari_458 @50 Hz (after the road-plane damper fix, 2026-11-01)
        private const float REF_500_T100 = 8.76f, REF_500_BRAKE = 38.0f, REF_500_LAT = 0.94f; // pchassis abarth500 @50 Hz (after the road-plane damper fix, 2026-11-01)

        static bool Reference(string car, out float t100, out float brake, out float latG)
        {
            switch (car)
            {
                case "ferrari_458": t100 = REF_458_T100; brake = REF_458_BRAKE; latG = REF_458_LAT; return true;
                case "abarth500": t100 = REF_500_T100; brake = REF_500_BRAKE; latG = REF_500_LAT; return true;
            }
            t100 = brake = latG = float.NaN; return false;
        }

        /// <summary>Bottom of each drawn tyre (C's WHEEL_xx node centre - tyres.ini radius) above the plane (m): the wheels must touch the
        /// road (AC draws them at the physics positions; AcCarPhysics.VisualTravelOffset). Returns the worst |gap|.</summary>
        static float TyreGap(AcCarRig rig, out string detail)
        {
            detail = "";
            float worst = 0f;
            if (rig.Model == null) { detail = "no model"; return 0f; }
            for (int i = 0; i < 4; i++)
            {
                Transform w = rig.Model.Wheels[i].Wheel;
                if (w == null) { detail += (i > 0 ? "/" : "") + "-"; continue; }
                float r = rig.Physics.Chassis.Wheels[i].ContactRadius;
                float gap = w.position.y - r - PlaneY;
                detail += (i > 0 ? "/" : "") + F(gap * 1000f, "0");
                worst = Mathf.Max(worst, Mathf.Abs(gap));
            }
            detail += " mm";
            return worst;
        }

        static int WheelsDown(AcCarPhysics ph)
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (ph.Telemetry.Wheel(i).Contact) n++;
            return n;
        }

        // ------------------------------------------------------------------ physics-flat

        static IEnumerator Flat(AcLabRunner r)
        {
            yield return Boot(r);
            if (r.Failed) yield break;
            AcCarEntry car = PickCar(r, "ferrari_458");
            if (car == null) yield break;
            BuildPlane();
            AcCarRig rig = null;
            yield return SpawnRig(r, car, AcCarKind.Local, x => rig = x);
            if (!r.Check(rig != null && rig.Physics != null && rig.Spec != null, "rig with physics (" + car.Id + ", model " + (rig != null && rig.Model != null) + ")")) yield break;
            AcCarPhysics ph = rig.Physics;
            AcCarSpec s = rig.Spec;
            Vector3 start = PlaneCentre + new Vector3(0f, 0f, -2100f);
            ph.ResetTo(start + Vector3.up * 3f, Quaternion.identity);
            float cgStatic = ph.Chassis.StaticCgHeight();
            r.Check(Mathf.Abs(rig.Root.transform.position.y - (PlaneY + cgStatic + 0.01f)) < 0.02f, "ResetTo put the CG at the ground + static CG height " + F(cgStatic, "0.000") + " (" + F(rig.Root.transform.position.y, "0.000") + ")");

            // settle, parked (kinematic) then driven with the brake held
            ph.SetOccupied(true);
            ph.SetDriven(true);
            ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 3f, 30f, "settle 3 s");
            float y = rig.Root.transform.position.y - PlaneY;
            float speed = ph.Body.linearVelocity.magnitude;
            float pitch = Mathf.Asin(Mathf.Clamp(rig.Root.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            r.Check(WheelsDown(ph) == 4, "4 wheels on the ground (" + WheelsDown(ph) + ")");
            r.Check(Mathf.Abs(y - cgStatic) < 0.01f, "ride height: CG " + F(y, "0.000") + " m vs static " + F(cgStatic, "0.000") + " (-GRAPHICS_OFFSET.y " + F(s.CgHeightM, "0.000") + "), pitch " + F(pitch, "0.00") + " deg (model pitch " + F(s.GraphicsPitchDeg, "0.0") + ")");
            r.Check(speed < 0.02f, "no creep at rest (" + F(speed * 1000f, "0.0") + " mm/s)");
            r.Check(!ph.Body.automaticInertiaTensor && ph.Body.centerOfMass.sqrMagnitude < 1e-6f, "inertia set by hand " + ph.Body.inertiaTensor.ToString("0") + ", centre of mass at the root");
            string gaps;
            float gap = TyreGap(rig, out gaps);
            r.Check(gap < 0.02f, "drawn tyres on the road (bottom - plane LF/RF/LR/RR " + gaps + "; travel offsets " + F(ph.VisualTravelOffset(0) * 1000f, "0") + "/" + F(ph.VisualTravelOffset(2) * 1000f, "0") + " mm F/R)");
            Shots(r, rig, "physics-flat-rest");

            // the held wheel follows the input every frame (VR 90 Hz), not only the 50 Hz physics copy
            ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f; ph.Input.Steer = 0.5f;
            yield return null;
            r.Check(Mathf.Abs(ph.Telemetry.SteerWheelDeg - 0.5f * s.SteerLockDeg) < 0.5f, "steering wheel angle updated in the frame (" + F(ph.Telemetry.SteerWheelDeg, "0.0") + " deg of " + F(0.5f * s.SteerLockDeg, "0.0") + ")");
            ph.Input.Steer = 0f;
            yield return r.Wait(() => Time.time - t0 > 4f, 10f, "wheel back to centre");

            Time.timeScale = 4f;
            // 0-100, first upshift, 0-200, top speed
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f;
            float tStart = Time.time, t100 = float.NaN, t200 = float.NaN, tShift = float.NaN, topKmh = 0f;
            float x0 = rig.Root.transform.position.x, yaw0 = rig.Root.transform.eulerAngles.y, maxSide = 0f, maxHeading = 0f;
            int lastGear = 0;
            int di = ph.Core.Driven(2) ? 2 : 0;
            float peakSlip = ph.Core.Tyre[di].TanPeak / Mathf.Max(0.1f, ph.Core.Tyre[di].CxMult), gas = 1f;
            while (Time.time - tStart < 75f && (rig.Root.transform.position.z - PlaneCentre.z) < 2000f)
            {
                float kmh = ph.Telemetry.SpeedKmh;
                float tt = Time.time - tStart;
                if (float.IsNaN(t100) && kmh >= 100f) t100 = tt;
                if (float.IsNaN(t200) && kmh >= 200f) t200 = tt;
                if (float.IsNaN(tShift) && lastGear == 1 && ph.Telemetry.Gear == 2) tShift = tt;
                lastGear = ph.Telemetry.Gear;
                if (kmh > topKmh) topKmh = kmh;
                maxSide = Mathf.Max(maxSide, Mathf.Abs(rig.Root.transform.position.x - x0));
                maxHeading = Mathf.Max(maxHeading, Mathf.Abs(Mathf.DeltaAngle(yaw0, rig.Root.transform.eulerAngles.y)));
                if (rig.Root.transform.up.y < 0.7f) { r.Fail("the car tipped over during the acceleration run"); break; }
                // the harness' test driver (pchassis): gas held so the driven wheels stay near the tyre's peak slip, once per fixed step
                float worst = 0f;
                for (int i = 0; i < 4; i++) if (ph.Core.Driven(i) && ph.Telemetry.Wheel(i).SlipRatio > worst) worst = ph.Telemetry.Wheel(i).SlipRatio;
                gas = worst > peakSlip * 1.1f ? Mathf.Max(0.2f, gas - 0.1f) : Mathf.Min(1f, gas + 0.05f);
                ph.Input.Throttle = gas;
                yield return new WaitForFixedUpdate();
            }
            string acc = car.Id + " validation: first upshift " + F(tShift, "0.00") + " s, 0-100 " + F(t100, "0.00") + " s, 0-200 " + F(t200, "0.0") + " s, top " + F(topKmh, "0") + " km/h (ui " + F(car.Specs.Acceleration0100, "0.0") + " s / " + F(car.Specs.TopSpeedKmh, "0") + ")";
            AcLog.Info("Physics: " + acc);
            float rT100, rBrake, rLat;
            bool hasRef = Reference(car.Id, out rT100, out rBrake, out rLat);
            if (car.Id == "ferrari_458") AcLog.Info("Physics: ferrari_458 brief targets: 0-100 3.3-3.6 s, top ~325 km/h, ~1.3 g, 100-0 32-35 m (the harness gives 0-100 " + F(REF_458_T100, "0.00") + ", " + F(REF_458_LAT, "0.00") + " g, " + F(REF_458_BRAKE, "0.0") + " m)");
            r.Check(!float.IsNaN(t100) && (hasRef ? Mathf.Abs(t100 - rT100) <= 0.3f : t100 > 2f && t100 < 12f), acc + (hasRef ? " vs harness " + F(rT100, "0.00") + " +-0.3" : " (no harness reference: 2..12 s)"));
            // no driver correction: the car must keep its line (second pass: a one-sided ARB step made 72 of 86 cars drift / spin)
            r.Check(maxSide < 3f && maxHeading < 2f, car.Id + " straight line without steering: max sideways " + F(maxSide, "0.00") + " m, heading change " + F(maxHeading, "0.00") + " deg (limits 3 m / 2 deg // ours)");

            // 100-0 braking
            yield return Settle(r, ph, start, Quaternion.identity);
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f;
            yield return r.Wait(() => ph.Telemetry.SpeedKmh >= 103f, 60f, "100 km/h for the brake test");
            ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
            yield return r.Wait(() => ph.Telemetry.SpeedKmh <= 100f, 10f, "coast to 100");
            Vector3 p0 = rig.Root.transform.position;
            yield return r.Wait(() => ph.Body.linearVelocity.magnitude < 0.3f, 30f, "stopped");
            float dist = Vector3.Distance(p0, rig.Root.transform.position);
            string br = car.Id + " validation: 100-0 km/h " + F(dist, "0.0") + " m (ABS factory " + (s.Abs.Present && s.Abs.Active) + ")";
            AcLog.Info("Physics: " + br);
            r.Check(hasRef ? Mathf.Abs(dist - rBrake) <= 3f : dist > 20f && dist < 50f, br + (hasRef ? " vs harness " + F(rBrake, "0.0") + " +-3" : " (no reference: 20..50 m)"));

            // constant 100 km/h steer ramp
            yield return Settle(r, ph, start, Quaternion.identity);
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f;
            yield return r.Wait(() => ph.Telemetry.SpeedKmh >= 100f, 60f, "100 km/h for the steer ramp");
            float bestG = 0f, bestSteer = 0f, yawSign = 0f;
            float rampStart = Time.time;
            while (Time.time - rampStart < 30f)
            {
                float tt = Time.time - rampStart;
                float steer = Mathf.Clamp01(tt / 30f) * 0.6f; // 0..60 % of STEER_LOCK over 30 s // ours test ramp
                float kmh = ph.Telemetry.SpeedKmh;
                ph.Input.Steer = steer;
                ph.Input.SteerAbsolute = false;
                ph.Input.Throttle = Mathf.Clamp01((100f - kmh) * 0.15f + 0.3f);
                ph.Input.Brake = kmh > 105f ? 0.3f : 0f;
                float yaw = ph.Body.angularVelocity.y;
                if (tt > 2f && yawSign == 0f && Mathf.Abs(yaw) > 0.01f) yawSign = Mathf.Sign(yaw);
                float latG = Mathf.Abs(yaw) * ph.Body.linearVelocity.magnitude / 9.81f;
                Vector3 v = ph.Body.linearVelocity, fw = rig.Root.transform.forward;
                float slipDeg = Vector3.Angle(new Vector3(fw.x, 0f, fw.z), new Vector3(v.x, 0f, v.z));
                if (slipDeg > 12f) break;
                if (Mathf.Abs(kmh - 100f) < 4f && latG > bestG) { bestG = latG; bestSteer = ph.SteerRoadDeg; }
                yield return null;
            }
            string lat = car.Id + " validation: lateral " + F(bestG, "0.00") + " g at 100 km/h (road wheel " + F(bestSteer, "0.0") + " deg), positive steer turns " + (yawSign > 0f ? "RIGHT" : yawSign < 0f ? "LEFT" : "?");
            AcLog.Info("Physics: " + lat);
            r.Check(yawSign > 0f && (hasRef ? Mathf.Abs(bestG - rLat) <= 0.08f : bestG > 0.7f && bestG < 1.8f), lat + (hasRef ? " vs harness " + F(rLat, "0.00") + " +-0.08" : " (no reference: 0.7..1.8 g)"));
            Time.timeScale = 1f;

            // modes: park, freeze, occupy (excludeLayers), puppet
            yield return Settle(r, ph, start, Quaternion.identity);
            ph.SetDriven(false);
            r.Check(ph.Body.isKinematic && ph.Body.interpolation == RigidbodyInterpolation.None, "parked = kinematic, interpolation None");
            ph.SetOccupied(true);
            Collider[] cols = rig.Root.GetComponentsInChildren<Collider>(true);
            bool allEx = cols.Length > 0;
            for (int i = 0; i < cols.Length; i++) allEx &= (cols[i].excludeLayers & AcLayers.AvatarExcludes) == AcLayers.AvatarExcludes;
            r.Check(allEx, "occupied: every car collider (" + cols.Length + ") excludes the avatar layers 2|7|24");
            ph.SetOccupied(false);
            float tOut = Time.time;
            yield return r.Wait(() => Time.time - tOut > 1.3f, 5f, "the 1 s see-through after getting out");
            bool noneEx = true;
            for (int i = 0; i < cols.Length; i++) noneEx &= (cols[i].excludeLayers & AcLayers.AvatarExcludes) == 0;
            r.Check(noneEx, "1 s after getting out the colliders are solid to the avatar again");
            ph.SetDriven(true); ph.Freeze(true);
            r.Check(ph.Body.isKinematic, "frozen = kinematic");
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f;
            float tf = Time.time;
            yield return r.Wait(() => Time.time - tf > 1f, 5f, "rev while frozen");
            r.Check(ph.Telemetry.Rpm > s.IdleRpm + 500f, "the engine revs while frozen (" + F(ph.Telemetry.Rpm, "0") + " rpm)");
            ph.Freeze(false);
            // speed limiter (what R sets in the pit lane; P also applies AcSession.Rules.PitLimitKmh itself while AcLocal.InPitlane)
            ph.SpeedLimitKmh = 60f;
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f;
            float tl = Time.time;
            yield return r.Wait(() => Time.time - tl > 8f, 20f, "8 s full throttle with a 60 km/h limiter");
            r.Check(ph.Telemetry.SpeedKmh < 64f && (ph.Telemetry.Lights & AcLightState.PitLimiter) != 0, "speed limiter: " + F(ph.Telemetry.SpeedKmh, "0.0") + " km/h at 60, PitLimiter light " + ((ph.Telemetry.Lights & AcLightState.PitLimiter) != 0));
            ph.SpeedLimitKmh = 0f;
            yield return Settle(r, ph, start, Quaternion.identity);
            ph.SetPuppet(true);
            ph.ApplyPuppet(start + Vector3.up * 2f, Quaternion.identity, Vector3.forward * 10f, ph.Telemetry);
            r.Check(ph.Body.isKinematic && Vector3.Distance(rig.Root.transform.position, start + Vector3.up * 2f) < 0.01f, "puppet placed by ApplyPuppet (first call teleports)");
            Vector3 next = start + Vector3.up * 2f + Vector3.forward * 0.2f;
            ph.ApplyPuppet(next, Quaternion.identity, Vector3.forward * 10f, ph.Telemetry);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
            r.Check(Vector3.Distance(ph.Body.position, next) < 0.01f && ph.Body.interpolation == RigidbodyInterpolation.Interpolate, "puppet moved by MovePosition in FixedUpdate (body " + ph.Body.position.ToString("0.00") + "), interpolated");
            rig.Dispose();
        }

        static IEnumerator Settle(AcLabRunner r, AcCarPhysics ph, Vector3 at, Quaternion rot)
        {
            ph.Input = AcCarInput.Neutral;
            ph.ResetTo(at + Vector3.up * 2f, rot);
            ph.Input.Brake = 1f;
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 1.5f, 20f, "settle");
            ph.Input = AcCarInput.Neutral;
        }

        // ------------------------------------------------------------------ physics-458-magione

        static IEnumerator Magione(AcLabRunner r)
        {
            yield return Boot(r);
            if (r.Failed) yield break;
            AcCarEntry car = PickCar(r, "ferrari_458");
            if (car == null) yield break;
            string trackId = AcLabBoot.Arg("-actrack", "magione");
            AcLayoutEntry layout = r.Game.Content.Layout(trackId, AcLabBoot.Arg("-aclayout", ""));
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + " owned")) yield break;
            yield return AcTrackWorld.Load(layout, null);
            if (!r.Check(AcTrackWorld.Loaded && AcTrackWorld.PitBoxes.Length > 0, "W's track loaded (" + AcTrackWorld.PitBoxes.Length + " pit boxes, " + AcSurfaceTag.Count + " tagged colliders)")) yield break;
            AcCarRig rig = null;
            yield return SpawnRig(r, car, AcCarKind.Local, x => rig = x);
            if (!r.Check(rig != null && rig.Physics != null, "rig with physics")) yield break;
            AcCarPhysics ph = rig.Physics;
            AcSpawnPoint pit = AcTrackWorld.PitBoxes[0];
            ph.ResetTo(pit.Marker, pit.Rotation);
            ph.SetOccupied(true); ph.SetDriven(true);
            ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 3f, 30f, "settle in pit box 0");
            float drop = pit.Marker.y - rig.Root.transform.position.y;
            r.Check(WheelsDown(ph) == 4, "4 wheels on magione's road in pit 0 (marker " + F(drop, "0.00") + " m above the CG)");
            string keys = "";
            for (int i = 0; i < 4; i++) { AcSurface su = ph.WheelSurface(i); keys += (i > 0 ? "," : "") + (su != null ? su.Key + "/" + F(su.Friction, "0.00") : "-"); }
            r.Check(ph.WheelSurface(0) != null, "surfaces under the wheels: " + keys);
            Shots(r, rig, "physics-magione-pit");
            // polish pass: the 6 s drive is traced (every 0.25 s of game time) and the car's impacts are logged, so a short distance has a reason
            _traceRig = rig; _impacts = 0; _impactMaxKmh = 0f;
            AcEvents.CarImpact += OnMagioneImpact;
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 0.6f;
            float t1 = Time.time, nextTrace = 0f;
            Vector3 p0 = rig.Root.transform.position;
            float yaw0 = rig.Root.transform.eulerAngles.y;
            while (Time.time - t1 < 6f)
            {
                if (rig.Root.transform.up.y < 0.7f) break;
                if (Time.time - t1 >= nextTrace) { nextTrace += 0.25f; Trace(ph, rig, Time.time - t1, p0, yaw0, ""); }
                yield return null;
            }
            float straight = Vector3.Distance(p0, rig.Root.transform.position);
            r.Check(rig.Root.transform.up.y > 0.9f && straight > 5f,
                "drove " + F(straight, "0") + " m on the track, " + F(ph.Telemetry.SpeedKmh, "0") + " km/h, upright (no steering; harness psim at pedal 0.6 on flat ground: 77 m, 92 km/h)");
            RunnerLog("straight drive: impacts " + _impacts + " (max " + F(_impactMaxKmh, "0") + " km/h), heading change " + F(Mathf.DeltaAngle(yaw0, rig.Root.transform.eulerAngles.y), "0.0") + " deg");
            ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
            yield return r.Wait(() => ph.Body.linearVelocity.magnitude < 0.3f, 20f, "stopped");
            Shots(r, rig, "physics-magione-after");

            // steered drive on the circuit: grid slot 0, follow AC's fast lane (fast_lane.ai) with a pure-pursuit steer and a curvature speed
            // limit (lab driver only, ours: 0.9 g corners, 0.7 g braking, look-ahead max(8 m, 0.6 s)) for 20 s - stability on a real road
            AcAiSpline line = AcTrackWorld.AiLine;
            if (line != null && !line.IsEmpty && AcTrackWorld.StartGrid.Length > 0)
            {
                AcSpawnPoint g0 = AcTrackWorld.StartGrid[0];
                ph.Input = AcCarInput.Neutral;
                ph.ResetTo(g0.Marker, g0.Rotation);
                ph.Input.Brake = 1f;
                float ts = Time.time;
                yield return r.Wait(() => Time.time - ts > 2f, 30f, "settle on grid 0");
                _impacts = 0; _impactMaxKmh = 0f;
                Vector3 q0 = rig.Root.transform.position;
                float y0 = rig.Root.transform.eulerAngles.y, maxYaw = 0f, maxOff = 0f, dist = 0f, maxKmh = 0f;
                int hint = -1, offTrack = 0;
                Vector3 last = q0;
                t1 = Time.time; nextTrace = 0f;
                bool spun = false;
                while (Time.time - t1 < 20f)
                {
                    Transform tr = rig.Root.transform;
                    if (tr.up.y < 0.7f) break;
                    float lateral;
                    string why = Pursue(ph, line, ref hint, out lateral);
                    dist += Vector3.Distance(last, tr.position); last = tr.position;
                    float yawRate = Mathf.Abs(Vector3.Dot(ph.Body.angularVelocity, tr.up));
                    if (yawRate > maxYaw) maxYaw = yawRate;
                    if (Mathf.Abs(lateral) > maxOff) maxOff = Mathf.Abs(lateral);
                    if (ph.Telemetry.SpeedKmh > maxKmh) maxKmh = ph.Telemetry.SpeedKmh;
                    if (Vector3.Dot(ph.Body.linearVelocity, tr.forward) < -1f) spun = true;
                    if (Time.time - t1 >= nextTrace)
                    {
                        nextTrace += 0.25f;
                        for (int i = 0; i < 4; i++) { AcSurface su = ph.WheelSurface(i); if (su != null && !su.IsValidTrack) { offTrack++; break; } }
                        Trace(ph, rig, Time.time - t1, q0, y0, "lat " + F(lateral, "0.0") + " m " + why);
                    }
                    yield return null;
                }
                AcEvents.CarImpact -= OnMagioneImpact;
                r.Check(!spun && rig.Root.transform.up.y > 0.9f && maxOff < 6f && _impactMaxKmh < 10f && dist > 150f,
                    "steered 20 s on " + trackId + "'s fast lane from grid 0: " + F(dist, "0") + " m, max " + F(maxKmh, "0") + " km/h, max yaw rate " + F(maxYaw, "0.00") +
                    " rad/s, max off-line " + F(maxOff, "0.0") + " m, impacts " + _impacts + " (max " + F(_impactMaxKmh, "0") + " km/h), off-track samples " + offTrack + (spun ? ", SPUN" : ""));
                ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
                yield return r.Wait(() => ph.Body.linearVelocity.magnitude < 0.3f, 20f, "stopped");
                Shots(r, rig, "physics-magione-lap");
            }
            else AcEvents.CarImpact -= OnMagioneImpact;
            _traceRig = null;
            rig.Dispose();
        }

        private static AcCarRig _traceRig;
        private static int _impacts;
        private static float _impactMaxKmh;

        static void OnMagioneImpact(AcCarRig car, float kmh, Vector3 point)
        {
            if (car == null || car != _traceRig) return;
            _impacts++;
            if (kmh > _impactMaxKmh) _impactMaxKmh = kmh;
            if (_impacts > 12) return;
            string near = "";
            Collider[] cs = Physics.OverlapSphere(point, 0.3f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cs.Length && i < 5; i++) near += (i > 0 ? ", " : "") + cs[i].name + " L" + cs[i].gameObject.layer;
            RunnerLog("impact " + F(kmh, "0") + " km/h at " + (car.Root != null ? (car.Root.transform.InverseTransformPoint(point)).ToString("F2") : "?") + " (root-local); near: " + near);
        }

        /// <summary>One trace line of the lab drives: time, distance, speed, rpm / gear, effective gas / clutch / TC, heading change, yaw rate,
        /// per wheel contact + surface + load + slip ratio, the body's height above the patch average.</summary>
        static void Trace(AcCarPhysics ph, AcCarRig rig, float t, Vector3 p0, float yaw0, string extra)
        {
            Transform tr = rig.Root.transform;
            AcTelemetry tl = ph.Telemetry;
            AcCarCore c = ph.Core;
            var sb = new System.Text.StringBuilder(260);
            sb.Append("trace t ").Append(F(t, "0.00")).Append(" d ").Append(F(Vector3.Distance(p0, tr.position), "0.0")).Append(" m ")
              .Append(F(tl.SpeedKmh, "0")).Append(" km/h rpm ").Append(F(tl.Rpm, "0")).Append(" g").Append(tl.Gear)
              .Append(" gas ").Append(c != null ? F(c.GasEffective, "0.00") : "-").Append(" clutch ").Append(c != null ? F(c.ClutchEngage, "0.00") : "-")
              .Append(tl.TcActive ? " TC" : "").Append(tl.Shifting ? " SHIFT" : "").Append(" steer ").Append(F(tl.SteerWheelDeg, "0"))
              .Append(" hdg ").Append(F(Mathf.DeltaAngle(yaw0, tr.eulerAngles.y), "0.0")).Append(" yaw ").Append(F(Vector3.Dot(ph.Body.angularVelocity, tr.up), "0.00"))
              .Append(" up ").Append(F(tr.up.y, "0.000")).Append(" |");
            for (int i = 0; i < 4; i++)
            {
                AcWheelState w = tl.Wheel(i);
                AcSurface su = ph.WheelSurface(i);
                sb.Append(' ').Append(w.Contact ? (su != null ? su.Key : "?") : "air").Append(' ').Append(F(w.LoadN, "0")).Append("N sr ").Append(F(w.SlipRatio, "0.00"))
                  .Append(" sa ").Append(F(w.SlipAngleDeg, "0.0"));
            }
            sb.Append(" | brake ").Append(F(ph.Input.Brake, "0.00")).Append(" x mm");
            for (int i = 0; i < 4; i++) sb.Append(' ').Append(F(ph.Chassis.Wheels[i].X * 1000f, "0"));
            sb.Append(" | floor gap mm ").Append(FloorGaps(rig));
            if (extra.Length > 0) sb.Append(" | ").Append(extra);
            RunnerLog(sb.ToString());
        }

        /// <summary>Gap (mm) under the four bottom corners of the colliders.ini floor box "Ac Floor 0" to the track (layer Track, ray down) and the
        /// CG height above the track: tells a road that rises under the car from a body that dips.</summary>
        static string FloorGaps(AcCarRig rig)
        {
            Transform fl = rig.Root.transform.Find("Ac Floor 0");
            BoxCollider bx = fl != null ? fl.GetComponent<BoxCollider>() : null;
            string s = "";
            int mask = 1 << AcLayers.Track;
            RaycastHit hit;
            if (bx != null)
            {
                Vector3 c = bx.center, e = bx.size * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 local = new Vector3(c.x + ((k & 1) == 0 ? -e.x : e.x), c.y - e.y, c.z + (k < 2 ? e.z : -e.z));
                    Vector3 w = fl.TransformPoint(local);
                    s += (k > 0 ? "/" : "") + (Physics.Raycast(w + Vector3.up * 0.3f, Vector3.down, out hit, 1.3f, mask, QueryTriggerInteraction.Ignore) ? F((hit.distance - 0.3f) * 1000f, "0") : "-");
                }
            }
            else s = "no floor box";
            Vector3 cg = rig.Root.transform.position;
            s += " (FL/FR/RL/RR, Unity left = -x) cg " + (Physics.Raycast(cg + Vector3.up * 0.3f, Vector3.down, out hit, 2f, mask, QueryTriggerInteraction.Ignore) ? F((hit.distance - 0.3f) * 1000f, "0") : "-");
            return s;
        }

        /// <summary>Lab driver: pure pursuit on an AC AI line (look-ahead max(8 m, 0.6 s * v)) and a speed target from the line's curvature over
        /// the next 120 m (corner 0.9 g, braking 0.7 g - ours lab numbers). Writes ph.Input; returns a short reason text.</summary>
        static string Pursue(AcCarPhysics ph, AcAiSpline line, ref int hint, out float lateral)
        {
            Transform tr = ph.transform;
            float x, y, z;
            AcTrackWorld.ToAc(tr.position, out x, out y, out z);
            hint = hint < 0 ? line.Nearest(x, y, z) : line.NearestNear(x, y, z, hint, 40);
            lateral = line.LateralOffset(x, z, hint);
            float v = ph.Body.linearVelocity.magnitude;
            float look = Mathf.Max(8f, 0.6f * v);
            int n = line.Count, j = hint;
            float acc = 0f;
            for (int k = 0; k < n && acc < look; k++) { int nx = (j + 1) % n; acc += Dist(line, j, nx); j = nx; }
            Vector3 target = AcTrackWorld.ToUnity(line.X(j), line.Y(j), line.Z(j));
            Vector3 to = target - tr.position;
            to -= tr.up * Vector3.Dot(to, tr.up);
            float ang = Vector3.SignedAngle(tr.forward, to, tr.up); // + = right
            float road = Mathf.Atan2(2f * Mathf.Sin(ang * Mathf.Deg2Rad) * ph.Spec.WheelbaseM, Mathf.Max(1f, to.magnitude)) * Mathf.Rad2Deg; // pure pursuit
            ph.Input.SteerAbsolute = false;
            ph.Input.Steer = Mathf.Clamp(road * Mathf.Max(1f, ph.Spec.SteerRatio) / Mathf.Max(1f, ph.Spec.SteerLockDeg), -1f, 1f);
            // speed target from the curvature ahead
            float vt = 70f; // m/s cap // ours
            acc = 0f; j = hint;
            for (int k = 0; k < n && acc < 120f; k += 1)
            {
                int a = j, b = (j + 4) % n, c = (j + 8) % n;
                float rad = Radius(line, a, b, c);
                float vc = Mathf.Sqrt(0.9f * 9.81f * rad);
                float allowed = Mathf.Sqrt(vc * vc + 2f * 0.7f * 9.81f * acc);
                if (allowed < vt) vt = allowed;
                int nx = (j + 1) % n; acc += Dist(line, j, nx); j = nx;
            }
            float err = vt - v;
            ph.Input.Throttle = Mathf.Clamp01(err * 0.25f);
            ph.Input.Brake = Mathf.Clamp01(-err * 0.15f);
            return "target " + F(vt * 3.6f, "0") + " km/h";
        }

        static float Dist(AcAiSpline l, int a, int b)
        {
            float dx = l.X(b) - l.X(a), dz = l.Z(b) - l.Z(a);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float Radius(AcAiSpline l, int a, int b, int c)
        {
            float ax = l.X(a), az = l.Z(a), bx = l.X(b), bz = l.Z(b), cx = l.X(c), cz = l.Z(c);
            float ab = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            float bc = Mathf.Sqrt((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz));
            float ca = Mathf.Sqrt((ax - cx) * (ax - cx) + (az - cz) * (az - cz));
            float cross = Mathf.Abs((bx - ax) * (cz - az) - (bz - az) * (cx - ax));
            return cross < 1e-4f ? 1e4f : ab * bc * ca / (2f * cross);
        }

        // ------------------------------------------------------------------ physics-fleet

        static IEnumerator Fleet(AcLabRunner r)
        {
            yield return Boot(r);
            if (r.Failed) yield break;
            BuildPlane();
            int n = 0, bad = 0;
            foreach (AcCarEntry car in r.Game.Content.Cars)
            {
                if (!car.Owned) continue;
                AcCarRig rig = null;
                yield return SpawnRig(r, car, AcCarKind.Local, x => rig = x);
                n++;
                if (rig == null || rig.Physics == null || rig.Spec == null) { bad++; RunnerLog(car.Id + " no rig / physics"); if (rig != null) rig.Dispose(); continue; }
                AcCarPhysics ph = rig.Physics;
                ph.ResetTo(PlaneCentre + new Vector3((n % 10) * 6f - 30f, 2f, -2000f), Quaternion.identity);
                ph.SetDriven(true);
                ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
                float t0 = Time.time;
                yield return r.Wait(() => Time.time - t0 > 2f, 20f, "settle " + car.Id);
                float y = rig.Root.transform.position.y - PlaneY;
                int down = WheelsDown(ph);
                float cgS = ph.Chassis.StaticCgHeight();
                string gaps;
                float gap = TyreGap(rig, out gaps);
                // drawn tyres vs the road too: the CG-only check passed cars whose wheels floated / sank (second pass, 36 cars > 2 cm)
                bool ok = down == 4 && Mathf.Abs(y - cgS) < 0.01f && gap < 0.02f && ph.Body.linearVelocity.magnitude < 0.05f;
                if (!ok) bad++;
                RunnerLog((ok ? "ok   " : "BAD  ") + car.Id + ": CG " + F(y, "0.000") + " (static " + F(cgS, "0.000") + ", -off.y " + F(rig.Spec.CgHeightM, "0.000") + "), tyre gaps " + gaps + ", wheels " + down +
                    ", hull " + (rig.Model != null && rig.Model.Hull != null ? rig.Model.Hull.Length : 0) + ", speed " + F(ph.Body.linearVelocity.magnitude, "0.000"));
                rig.Dispose();
                yield return null;
            }
            r.Check(bad == 0, "fleet: " + n + " cars settled, " + bad + " bad");
        }

        static void RunnerLog(string s) { AcLabRunner.Log(s); }

        // ================================================================== iteration 2: damage (DAMAGE_DESIGN.md 10)
        //   physics-crash [-accar ferrari_458]: flat plane + an UNTAGGED box wall on layer Track; the car is pushed by setting its velocity into
        //   the wall: front 20/40/60/100, rear 40, left side 40, a 100 km/h scrape along the wall for 2 s, dropped on its roof; front 40 again
        //   at DamageRate 0 (nothing, no CarDamaged) and 0.5 (same zone, half aero / rod effect); ResetTo keeps, Repair(All) clears; toe <= 12;
        //   a 50 km/h side hit on a 30 cm post at the LF hub (rod = m x GAIN x (v - MIN), toe-out) at 1 and 0.5; coast-down + 200-0 before /
        //   after the front 60 hit (aero changes the car). Pictures with C's visuals (gate on at spawn) 50 m from the wall.
        //   physics-engine [-accar ferrari_458]: money shift (2nd at the limiter -> 1st) life loss vs the formula from the sampled rpm;
        //   bmw_z4_drift at default boost loses nothing (P0); drive to life 0 -> EngineDead, revs fall, no gas, no restart; Repair(Engine).

        private static GameObject _wall;
        private const float WallZ = 600f; // ours: plane-local z of the wall face (the plane spans -2200..2200)

        static void BuildWall()
        {
            if (_wall != null) return;
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Ac Lab Damage Wall";
            _wall.layer = AcLayers.Track; // no AcSurfaceTag: a wall (AcSurfaceTag.Of == null) -> damage
            _wall.transform.position = PlaneCentre + new Vector3(0f, 1f, WallZ + 0.5f);
            _wall.transform.localScale = new Vector3(60f, 2f, 1f);
            // a narrow post with its face on the same line, away from the wall: a side hit right at one wheel (steer-rod damage, 3.2)
            _post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _post.name = "Ac Lab Damage Post";
            _post.layer = AcLayers.Track;
            _post.transform.position = PlaneCentre + new Vector3(PostX, 1f, WallZ + 0.5f);
            _post.transform.localScale = new Vector3(0.3f, 2f, 1f); // ours: 30 cm post
        }

        private static GameObject _post;
        private const float PostX = 70f; // ours: plane-local x of the post (the wall spans -30..30)

        /// <summary>Crash pictures: the car is moved 50 m back from the wall line (ResetTo keeps the damage) so no camera sits inside the
        /// wall, settles, then front 3/4 left, rear 3/4 right, left side, right side.</summary>
        static IEnumerator CrashShots(AcLabRunner r, AcCarRig rig, string prefix)
        {
            rig.Physics.ResetTo(PlaneCentre + new Vector3(-80f, 2f, WallZ - 50f), Quaternion.identity);
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 1.0f, 10f, "settle for the pictures");
            Transform t = rig.Root.transform;
            Vector3 c = t.position + Vector3.up * 0.4f;
            Vector3 f = t.forward, rt = t.right;
            r.Aim(c + f * 5.0f - rt * 3.0f + Vector3.up * 1.3f, c, 45f); r.Shot(prefix + "-front34");
            r.Aim(c - f * 5.0f + rt * 3.0f + Vector3.up * 1.3f, c, 45f); r.Shot(prefix + "-rear34");
            r.Aim(c - rt * 6.0f + Vector3.up * 0.4f, c, 42f); r.Shot(prefix + "-left");
            r.Aim(c + rt * 6.0f + Vector3.up * 0.4f, c, 42f); r.Shot(prefix + "-right");
        }

        /// <summary>Coast-down and braking on the open plane (lane x -120, away from the wall / post): set to 250 km/h, 1 s for the wheels
        /// to spin up, then the speed lost in neutral over 5 s (drag; the wings' CD zone gains raise it), and ~200 -> 0 km/h at full brake
        /// (distance scaled to 200 km/h by v^2 = grip incl. downforce; the CL zone
        /// gains lower downforce). Same inputs every call, deterministic physics: a damaged car must differ from the pristine baseline.</summary>
        static IEnumerator Perf(AcLabRunner r, AcCarPhysics ph, float[] result)
        {
            Vector3 start = PlaneCentre + new Vector3(-120f, 0f, -2000f);
            ph.ResetTo(start + Vector3.up * 2f, Quaternion.identity);
            ph.Input = AcCarInput.Neutral; ph.Input.GearRequest = 0;
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 0.8f, 10f, "settle before the coast-down");
            ph.Body.linearVelocity = Vector3.forward * (250f / 3.6f);
            t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 1f, 10f, "wheels spun up"); // the wheels start at 0 rpm after ResetTo
            float v0 = ph.Body.linearVelocity.magnitude * 3.6f;
            t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 5f, 15f, "coast-down");
            result[0] = v0 - ph.Body.linearVelocity.magnitude * 3.6f;
            ph.ResetTo(start + Vector3.up * 2f, Quaternion.identity);
            t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 0.8f, 10f, "settle before the brake test");
            ph.Body.linearVelocity = Vector3.forward * (200f / 3.6f);
            t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 1f, 10f, "wheels spun up");
            Vector3 p0 = ph.Body.position;
            float vb = Mathf.Max(1f, ph.Body.linearVelocity.magnitude * 3.6f);
            ph.Input.Brake = 1f;
            t0 = Time.time;
            yield return r.Wait(() => ph.Body.linearVelocity.magnitude < 0.3f || Time.time - t0 > 15f, 20f, "200-0 braking");
            Vector3 dp = ph.Body.position - p0; dp.y = 0f;
            result[1] = dp.magnitude * (200f / vb) * (200f / vb); // scaled to exactly 200 km/h (v^2)
            result[2] = vb;
            ph.Input = AcCarInput.Neutral;
        }

        /// <summary>Largest (clDiv - 1) and (cdMul - 1) over the car's wings at the current damage and multiplier.</summary>
        static void WorstAero(AcCarRig rig, out float cl1, out float cd1)
        {
            cl1 = 0f; cd1 = 0f; float cl, cd;
            for (int w = 0; w < rig.Spec.Wings.Count; w++) { rig.Physics.Damage.AeroFactors(rig.Spec.Wings[w], out cl, out cd); cl1 = Mathf.Max(cl1, cl - 1f); cd1 = Mathf.Max(cd1, cd - 1f); }
        }

        /// <summary>Places the car (heading <paramref name="yaw"/>) so its hull is <paramref name="gap"/> m short of the wall at x
        /// <paramref name="x"/>, settles it, then sets the body velocity toward the wall (+z) every fixed step until the damage changed or
        /// 2 s passed. Returns via <paramref name="got"/> the zone deltas.</summary>
        static IEnumerator PushInto(AcLabRunner r, AcCarPhysics ph, float yaw, float kmh, float x, Action<float[]> got)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Bounds box = ph.DamageBox;
            float halfZ = Mathf.Abs((rot * new Vector3(box.extents.x, 0f, 0f)).z) + Mathf.Abs((rot * new Vector3(0f, 0f, box.extents.z)).z);
            float centreZ = (rot * box.center).z;
            Vector3 at = PlaneCentre + new Vector3(x, 0f, WallZ - halfZ - centreZ - 1.0f);
            ph.ResetTo(at + Vector3.up * 2f, rot);
            ph.Input = AcCarInput.Neutral;
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 0.6f, 10f, "settle before the hit");
            float[] before = new float[5];
            for (int i = 0; i < 5; i++) before[i] = ph.Damage.Zone[i];
            int v0 = ph.Damage.MechVersion;
            Vector3 vel = Vector3.forward * (kmh / 3.6f);
            float tHit = Time.time;
            while (ph.Damage.MechVersion == v0 && Time.time - tHit < 2f)
            {
                ph.Body.linearVelocity = vel; ph.Body.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
            }
            t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 0.5f, 10f, "after the hit");
            float[] d = new float[5];
            for (int i = 0; i < 5; i++) d[i] = ph.Damage.Zone[i] - before[i];
            got(d);
        }

        static string Zones(float[] d) { return F(d[0], "0.0") + "/" + F(d[1], "0.0") + "/" + F(d[2], "0.0") + "/" + F(d[3], "0.0") + "/" + F(d[4], "0.0"); }

        static bool OnlyZone(float[] d, int zone, float kmh, out string why)
        {
            bool others = true;
            for (int i = 0; i < 5; i++) if (i != zone && d[i] >= 3f) others = false;
            bool within = d[zone] >= kmh * 0.8f && d[zone] <= kmh * 1.2f; // design: +-20 % of the set normal speed
            why = "zones F/R/L/R/C +" + Zones(d);
            return others && within;
        }

        static IEnumerator Crash(AcLabRunner r)
        {
            yield return Boot(r);
            if (r.Failed) yield break;
            AcCarEntry car = PickCar(r, "ferrari_458");
            if (car == null) yield break;
            BuildPlane(); BuildWall();
            AcCarRig rig = null;
            // the pictures need C's paint dents: R's gate as a DAMAGE 100 session sets it before the spawn (W / C read it at build)
            AcCarDamageGate.Refresh(new AcRules { DamageMultiplier = 100 });
            yield return SpawnRig(r, car, AcCarKind.Local, x => rig = x);
            AcCarDamageGate.Reset();
            if (!r.Check(rig != null && rig.Physics != null && rig.Spec != null, "rig with physics (" + car.Id + ")")) yield break;
            AcCarPhysics ph = rig.Physics;
            ph.Assists.AutoClutch = true; ph.Assists.AutoShift = false; // neutral by GearRequest 0 in the coast-down
            int events = 0; AcDamageChange lastWhat = AcDamageChange.None;
            Action<AcCarRig, AcDamageChange, float> onDmg = (cr, what, k) => { if (cr == rig) { events++; lastWhat = what; } };
            AcEvents.CarDamaged += onDmg;
            try
            {
                ph.SetOccupied(true); ph.SetDriven(true);
                RunnerLog("damage box " + ph.DamageBox.center.ToString("0.00") + " size " + ph.DamageBox.size.ToString("0.00"));
                float[] d = null;

                // DamageRate 0 first: nothing at all
                ph.DamageRate = 0f;
                yield return PushInto(r, ph, 0f, 40f, -20f, x => d = x);
                yield return null;
                r.Check(ph.Damage.Pristine && events == 0, "DamageRate 0: front 40 adds nothing (" + Zones(ph.Damage.Zone) + "), CarDamaged raised " + events + "x");

                ph.DamageRate = 1f;
                // pristine baseline for the aero effect on the car's behaviour (coast-down = drag, 200-0 = grip incl. downforce)
                ph.Repair(AcRepair.All);
                float[] perf0 = new float[3], perf1 = new float[3];
                yield return Perf(r, ph, perf0);
                float cl60 = 0f, cd60 = 0f, z60 = 0f;
                float[] speeds = { 20f, 40f, 60f, 100f };
                for (int k = 0; k < speeds.Length; k++)
                {
                    ph.Repair(AcRepair.All);
                    yield return PushInto(r, ph, 0f, speeds[k], -20f + 8f * k, x => d = x);
                    string why; r.Check(OnlyZone(d, 0, speeds[k], out why), "front " + F(speeds[k], "0") + " km/h -> FRONT within 20 %, others < 3: " + why);
                    if (k == 2)
                    {
                        WorstAero(rig, out cl60, out cd60); z60 = ph.Damage.Zone[0];
                        RunnerLog("front 60: worst wing clDiv " + F(1f + cl60, "0.00") + " cdMul " + F(1f + cd60, "0.00") + ", body " + F(ph.Damage.BodyPct, "0") + " %, CarDamaged events " + events + " (last " + lastWhat + ")");
                        yield return CrashShots(r, rig, "physics-crash-front60");
                        r.Check(ph.Damage.Zone[0] == z60, "ResetTo keeps the damage (front " + F(ph.Damage.Zone[0], "0.0") + ")");
                        yield return Perf(r, ph, perf1);
                        bool slower = perf1[0] > perf0[0] + 0.05f, longer = perf1[1] > perf0[1] + 0.05f;
                        r.Check(slower || longer, "after front 60 the car behaves differently (aero): coast 250 km/h 5 s loses " + F(perf0[0], "0.00") + " -> " + F(perf1[0], "0.00") +
                            " km/h, 200-0 " + F(perf0[1], "0.00") + " -> " + F(perf1[1], "0.00") + " m (from " + F(perf0[2], "0.0") + " / " + F(perf1[2], "0.0") + " km/h, scaled to 200)");
                    }
                }
                ph.Repair(AcRepair.All);
                yield return PushInto(r, ph, 180f, 40f, 16f, x => d = x);
                { string why; r.Check(OnlyZone(d, 1, 40f, out why), "rear 40 -> REAR: " + why); }
                yield return CrashShots(r, rig, "physics-crash-rear40");
                ph.Repair(AcRepair.All);
                yield return PushInto(r, ph, 90f, 40f, 24f, x => d = x); // heading +x: the car's LEFT side faces +z
                { string why; r.Check(OnlyZone(d, 2, 40f, out why), "left side 40 at 90 deg -> LEFT: " + why); }
                yield return CrashShots(r, rig, "physics-crash-left40");

                // steer rod: left side into the 30 cm post right at the left-front hub, 50 km/h, at DamageRate 1 and 0.5
                AcDamageSpec ds = rig.Spec.Damage;
                float rod1 = 0f, toe1 = 0f;
                float hubZ = ph.WheelHub(0).z;
                float setupToe = ph.Chassis.Wheels[0].ToeDeg;
                for (int pass = 0; pass < 2; pass++)
                {
                    float m = pass == 0 ? 1f : 0.5f;
                    ph.Repair(AcRepair.All);
                    ph.DamageRate = m;
                    yield return PushInto(r, ph, 90f, 50f, PostX - hubZ, x => d = x);
                    float rod = ph.Damage.RodOffset[0];
                    float expect = ds.HasSusp && d[2] > ds.SuspMinVelocity ? Mathf.Min(ds.SuspMaxDamage, m * ds.SuspGain * (d[2] - ds.SuspMinVelocity)) : 0f;
                    string rods = F(ph.Damage.RodOffset[0] * 1000f, "0.0") + "/" + F(ph.Damage.RodOffset[1] * 1000f, "0.0") + "/" + F(ph.Damage.RodOffset[2] * 1000f, "0.0") + "/" + F(ph.Damage.RodOffset[3] * 1000f, "0.0");
                    float toe = ph.Chassis.Wheels[0].ToeDeg;
                    bool others = ph.Damage.RodOffset[1] == 0f && ph.Damage.RodOffset[2] == 0f && ph.Damage.RodOffset[3] == 0f;
                    r.Check(expect > 0f && Mathf.Abs(rod - expect) <= 0.05f * expect + 1e-5f && others && Mathf.Abs(toe) <= 12.0001f && toe > setupToe,
                        "post at the LF hub, DamageRate " + F(m, "0.0") + ": zones +" + Zones(d) + ", rods LF/RF/LR/RR " + rods + " mm (formula " + F(expect * 1000f, "0.0") +
                        " = m x GAIN x (v - MIN_VELOCITY)), LF toe " + F(setupToe, "0.00") + " -> " + F(toe, "0.00") + " deg (more toe-out, <= 12)");
                    if (pass == 0) { rod1 = rod; toe1 = toe - setupToe; yield return CrashShots(r, rig, "physics-crash-post50"); }
                    else
                        RunnerLog("rod at 0.5 / at 1: " + F(rod1 > 0f ? rod / rod1 : 0f, "0.000") + " (toe delta " + F(toe1, "0.00") + " -> " + F(toe - setupToe, "0.00") + " deg)");
                }

                // DamageRate 0.5: same zone (raw), half the aero effect
                ph.Repair(AcRepair.All);
                ph.DamageRate = 0.5f;
                yield return PushInto(r, ph, 0f, 60f, 4f, x => d = x);
                {
                    float clH, cdH; WorstAero(rig, out clH, out cdH);
                    float scale = z60 > 0f ? ph.Damage.Zone[0] / z60 : 1f; // the raw zone may differ by a fraction of a km/h
                    bool half = cl60 > 0f && Mathf.Abs(clH - 0.5f * cl60 * scale) <= 0.02f * cl60 && Mathf.Abs(cdH - 0.5f * cd60 * scale) <= 0.02f * Mathf.Max(cd60, 1e-3f);
                    string why; r.Check(OnlyZone(d, 0, 60f, out why) && half, "DamageRate 0.5: front 60 -> FRONT raw: " + why + ", clDiv-1 " + F(cl60, "0.000") + " -> " + F(clH, "0.000") +
                        ", cdMul-1 " + F(cd60, "0.000") + " -> " + F(cdH, "0.000") + " (half)");
                }
                ph.DamageRate = 1f;

                // scrape: 100 km/h along the wall (+x) with 1 m/s into it, 2 s
                ph.Repair(AcRepair.All);
                {
                    Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
                    Bounds box = ph.DamageBox;
                    Vector3 at = PlaneCentre + new Vector3(-25f, 0f, WallZ - box.extents.x - (rot * box.center).z - 0.15f);
                    ph.ResetTo(at + Vector3.up * 2f, rot);
                    float t0 = Time.time;
                    yield return r.Wait(() => Time.time - t0 > 0.6f, 10f, "settle before the scrape");
                    float tS = Time.time;
                    while (Time.time - tS < 2f)
                    {
                        ph.Body.linearVelocity = new Vector3(100f / 3.6f, ph.Body.linearVelocity.y, 1f); ph.Body.angularVelocity = Vector3.zero;
                        yield return new WaitForFixedUpdate();
                    }
                    float total = 0f; for (int i = 0; i < 5; i++) total += ph.Damage.Zone[i];
                    r.Check(total < 20f, "scrape 100 km/h along the wall 2 s adds < 20 km/h (" + Zones(ph.Damage.Zone) + ")");
                }

                // roof: dropped upside down onto the (tagged) road -> CENTER
                ph.Repair(AcRepair.All);
                {
                    Vector3 at = PlaneCentre + new Vector3(10f, 2.5f, 0f);
                    ph.ResetTo(at, Quaternion.identity);
                    ph.Body.position = at + Vector3.up * 1.5f; ph.Body.rotation = Quaternion.Euler(0f, 0f, 180f);
                    rig.Root.transform.SetPositionAndRotation(ph.Body.position, ph.Body.rotation);
                    float t0 = Time.time;
                    yield return r.Wait(() => Time.time - t0 > 2.5f, 10f, "roof drop");
                    r.Check(ph.Damage.Zone[4] >= 3f && ph.Damage.Zone[0] < 3f && ph.Damage.Zone[1] < 3f, "upside down -> CENTER (" + Zones(ph.Damage.Zone) + ")");
                    yield return CrashShots(r, rig, "physics-crash-roof");
                }

                int ev0 = events;
                ph.Repair(AcRepair.All);
                yield return null;
                r.Check(ph.Damage.Pristine && events == ev0 + 1 && (lastWhat & AcDamageChange.Repaired) != 0, "Repair(All) clears and raises CarDamaged(Repaired) once");
            }
            finally { AcEvents.CarDamaged -= onDmg; }
        }

        static IEnumerator Engine(AcLabRunner r)
        {
            yield return Boot(r);
            if (r.Failed) yield break;
            AcCarEntry car = PickCar(r, "ferrari_458");
            if (car == null) yield break;
            BuildPlane();
            AcCarRig rig = null;
            yield return SpawnRig(r, car, AcCarKind.Local, x => rig = x);
            if (!r.Check(rig != null && rig.Physics != null && rig.Spec != null, "rig with physics (" + car.Id + ")")) yield break;
            AcCarPhysics ph = rig.Physics;
            AcDamageSpec ds = rig.Spec.Damage;
            ph.SetOccupied(true); ph.SetDriven(true);
            ph.DamageRate = 1f;
            ph.Assists.AutoShift = false; ph.Assists.AutoClutch = true;
            ph.ResetTo(PlaneCentre + new Vector3(0f, 2f, -2100f), Quaternion.identity);
            Time.timeScale = 2f;
            // up to the limiter in 2nd, then 1st (money shift); the expected loss is integrated from the 50 Hz rpm samples (the core steps 333 Hz)
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f; ph.Input.GearRequest = 1;
            float t0 = Time.time;
            yield return r.Wait(() => ph.Core.Rpm > rig.Spec.LimiterRpm * 0.9f || Time.time - t0 > 20f, 30f, "1st near the limiter");
            ph.Input.GearRequest = 2;
            yield return r.Wait(() => ph.Core.Rpm > rig.Spec.LimiterRpm * 0.95f && ph.Core.Gear == 2 || Time.time - t0 > 40f, 50f, "2nd at the limiter");
            float life0 = ph.Damage.EngineLife, expected = 0f, peak = 0f;
            ph.Input.Throttle = 0f; ph.Input.GearRequest = 1;
            float tm = Time.time;
            while (Time.time - tm < 1.5f)
            {
                yield return new WaitForFixedUpdate();
                float rpm = ph.Core.Rpm; peak = Mathf.Max(peak, rpm);
                if (ds.HasRpmDamage && rpm > ds.RpmThreshold) expected += Time.fixedDeltaTime * ds.RpmDamageK * (rpm - ds.RpmThreshold);
            }
            float lost = life0 - ph.Damage.EngineLife;
            r.Check(expected <= 0f ? lost < 1f : Mathf.Abs(lost - expected) <= 0.05f * expected + 1f, "money shift: peak " + F(peak, "0") + " rpm (threshold " + F(ds.RpmThreshold, "0") + "), life lost " + F(lost, "0.0") +
                " vs formula from 50 Hz samples " + F(expected, "0.0") + " (+-5 %)");
            // drive to life 0
            ph.Input = AcCarInput.Neutral; ph.Input.GearRequest = 2;
            ph.Damage.EngineLife = 1f;
            ph.Input.GearRequest = 1;
            tm = Time.time;
            yield return r.Wait(() => ph.Damage.EngineDead || Time.time - tm > 5f, 10f, "blown engine");
            ph.Input.Throttle = 1f;
            tm = Time.time;
            yield return r.Wait(() => Time.time - tm > 4f, 10f, "coast with a blown engine");
            r.Check(ph.Damage.EngineDead && ph.Core.GasEffective == 0f, "EngineDead, no gas at full pedal (rpm " + F(ph.Core.Rpm, "0") + ", " + F(ph.SpeedKmh, "0") + " km/h coasting)");
            ph.ResetTo(PlaneCentre + new Vector3(0f, 2f, -1800f), Quaternion.identity);
            yield return new WaitForFixedUpdate();
            r.Check(ph.Damage.EngineDead && ph.Core.Rpm < 1f, "ResetTo keeps the blown engine stopped (rpm " + F(ph.Core.Rpm, "0") + ")");
            ph.Repair(AcRepair.Engine);
            tm = Time.time;
            yield return r.Wait(() => Time.time - tm > 1f, 10f, "repaired engine idles");
            r.Check(!ph.Damage.EngineDead && ph.Damage.EngineLife == AcCarDamage.LifeNew && ph.Core.Rpm > rig.Spec.IdleRpm * 0.5f, "Repair(Engine): new engine, idling at " + F(ph.Core.Rpm, "0") + " rpm");
            Time.timeScale = 1f;
            rig.Dispose();

            // P0: bmw_z4_drift at its default turbo adjustment does not over-boost (no turbo life loss at full throttle)
            AcCarEntry z4 = r.Game.Content.Car("bmw_z4_drift");
            if (z4 == null || !z4.Owned) { RunnerLog("bmw_z4_drift not owned - P0 check skipped"); yield break; }
            yield return SpawnRig(r, z4, AcCarKind.Local, x => rig = x);
            if (!r.Check(rig != null && rig.Physics != null, "rig bmw_z4_drift")) yield break;
            ph = rig.Physics;
            ph.SetOccupied(true); ph.SetDriven(true); ph.DamageRate = 1f;
            ph.ResetTo(PlaneCentre + new Vector3(20f, 2f, -2100f), Quaternion.identity);
            ph.Input = AcCarInput.Neutral; ph.Input.Throttle = 1f;
            float maxBoost = 0f; tm = Time.time;
            Time.timeScale = 2f;
            while (Time.time - tm < 15f) { yield return new WaitForFixedUpdate(); maxBoost = Mathf.Max(maxBoost, ph.Core.BoostTotal); }
            Time.timeScale = 1f;
            r.Check(ph.Damage.EngineLife == AcCarDamage.LifeNew, "bmw_z4_drift default boost: peak total " + F(maxBoost, "0.000") + " bar vs threshold " + F(rig.Spec.Damage.TurboBoostThreshold, "0.00") + ", life " + F(ph.Damage.EngineLife, "0.0"));
            rig.Dispose();
        }
    }
}
