using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One wheel of <see cref="AcChassis"/>: static data from the spec, the contact the caller found this FixedUpdate, the sub-step state.
    /// Vectors are in the caller's world frame (Unity axes: x right, y up, z forward) - Unity-free so the harness runs the same math.</summary>
    public sealed class AcChassisWheel
    {
        public int Index;
        public bool Front, Left;
        /// <summary>Hub at the suspension's DESIGN point (springs at free length), body-local (Unity axes; PhysToBody of (+-TRACK/2, BASEY,
        /// axle z)). At rest the wheel sits <see cref="StaticX"/> above it (see AcCarSpec.PhysicsCgHeightM).</summary>
        public AcVec3 Hub;
        /// <summary>Tyre radius (unloaded), the droop travel (m below the design point), the static corner load (N), static camber (deg).</summary>
        public float Radius, Droop, StaticLoad, CamberDeg;
        /// <summary>Contact radius = RADIUS - static tyre deflection (load / tyres.ini RATE) - // ours static approximation of the tyre spring.</summary>
        public float ContactRadius;
        /// <summary>Static travel from the design point (m, + = compressed): spring sag - ROD_LENGTH (with stops / heave).</summary>
        public float StaticX;
        /// <summary>This corner's ROD_LENGTH (m; setup ROD_LENGTH_xx) and toe-out angle (deg, + = the front of the wheel points outward;
        /// TOE_OUT / WBTYRE_STEER z, setup TOE_OUT_xx).</summary>
        public float Rod, ToeDeg;
        /// <summary>Iteration 2: the setup toe-out LENGTH (m; TOE_OUT / setup TOE_OUT_xx) and the steer-rod damage offset of this corner (m, >= 0,
        /// AcCarDamage.RodOffset). <see cref="ToeDeg"/> = AcCarDamage.ToeDeg(Axle, ToeLen, RodOffset) - with RodOffset 0 exactly iteration 1.</summary>
        public float ToeLen, RodOffset;
        public AcAxleSpec Axle;

        /// <summary>Recomputes <see cref="ToeDeg"/> from <see cref="ToeLen"/> and <see cref="RodOffset"/> (setup change / damage change).</summary>
        public void UpdateToe() { ToeDeg = AcCarDamage.ToeDeg(Axle, ToeLen, RodOffset); }
        // ---- contact (written by the caller's ray cast before Prepare)
        public bool Contact;
        /// <summary>Hub (design point) -> ground distance along -up (m).</summary>
        public float Dist;
        /// <summary>Patch point and ground normal (world).</summary>
        public AcVec3 Point, Normal;
        /// <summary>Surface FRICTION / DAMPING under the tyre (1 / 0 when untagged).</summary>
        public float Grip = 1f, Damping;
        // ---- state
        /// <summary>Travel from the design point (m, + = compressed / wheel up) and its rate (m/s).</summary>
        public float X, XDot;
        /// <summary>Suspension force (N) of the last sub-step.</summary>
        public float Load;
        public AcVec3 Fwd, Side;
        /// <summary>Road angle of this wheel (deg, right +): steer (fronts) + toe.</summary>
        public float SteerDeg;
    }

    /// <summary>The car body's state for one FixedUpdate (world frame): basis, centre of mass, velocities, mass properties.</summary>
    public struct AcBodyState
    {
        public AcVec3 Up, Fwd, Right, Com, V, W, Gravity;
        public float Mass;
        /// <summary>Principal moments (kg m^2) and the rotation of their frame (body rotation * inertiaTensorRotation), x y z w.</summary>
        public AcVec3 Inertia;
        public float Qx, Qy, Qz, Qw;
    }

    /// <summary>
    /// [P, Physics] The sub-stepped chassis (Unity-FREE, allocation-free per step): per FixedUpdate the caller casts the wheels and calls
    /// <see cref="Prepare"/> then <see cref="Step"/>; n sub-steps (~333 Hz) each compute the suspension (suspensions.ini: wheel rate from the
    /// free length, progressive, bump stop, packers, slow/fast dampers, ARB), the patch velocities, the <see cref="AcCarCore"/> step (tyres,
    /// drivetrain), and integrate the body's velocity LOCALLY with those forces, so stiff tyre forces stay stable at Unity's 50 Hz; the
    /// average force / torque is then applied once (Rigidbody.AddForce / AddTorque). AcCarPhysics (Unity) and the harness `pchassis`
    /// 3D test call exactly this code.
    /// </summary>
    public sealed class AcChassis
    {
        public readonly AcCarSpec Spec;
        public readonly AcCarCore Core;
        public readonly AcChassisWheel[] Wheels = new AcChassisWheel[4];
        /// <summary>Front road-wheel angle (deg, right positive) - set by the caller before <see cref="Prepare"/>.</summary>
        public float SteerRoadDeg;
        private readonly AcVec3[] _pv = new AcVec3[4];

        public AcChassis(AcCarSpec spec, AcCarCore core, float massKg, float gravity)
        {
            Spec = spec; Core = core;
            for (int i = 0; i < 4; i++)
            {
                AcAxleSpec ax = i < 2 ? spec.Front : spec.Rear;
                AcVec3 c = spec.WheelCentre(i);
                var w = new AcChassisWheel
                {
                    Index = i, Front = i < 2, Left = (i & 1) == 0, Axle = ax,
                    Hub = new AcVec3(-c.X, c.Y, c.Z), // AC +X = car left -> Unity -x
                    Radius = core.Tyre[i].Radius,
                    CamberDeg = ax.StaticCamberDeg,
                    Rod = ax.RodLength,
                    ToeLen = ax.ToeOut,
                    ToeDeg = ax.ToeDegFor(ax.ToeOut)
                };
                Wheels[i] = w;
            }
            SetMass(massKg, gravity);
        }

        /// <summary>Recomputes the static loads, static travels and tyre deflections (fuel burnt, springs / rods / tyres changed). The force law
        /// does not depend on them (springs act from their free length + ROD_LENGTH), so the car simply rises as fuel burns, like AC.</summary>
        public void SetMass(float massKg, float gravity)
        {
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel w = Wheels[i];
                w.StaticLoad = massKg * gravity * (i < 2 ? Spec.CgLocation : 1f - Spec.CgLocation) * 0.5f;
                w.StaticX = StaticTravel(w.Axle, w.StaticLoad, w.Rod);
                AcTyreSpec t = Core.Compound != null ? (i < 2 ? Core.Compound.Front : Core.Compound.Rear) : null;
                w.Radius = Core.Tyre[i].Radius;
                w.ContactRadius = w.Radius - w.StaticLoad / Math.Max(1e4f, t != null ? t.Rate : 250000f); // ours fallback rate
                // the droop stop is BUMPSTOP_DN below the design point (the wheel leaves the ground there)
                w.Droop = Math.Min(0.25f, Math.Max(0.02f, w.Axle.BumpStopDn)); // 0.25 m / 2 cm guards // ours
                if (w.Droop < 0.02f - w.StaticX) w.Droop = 0.02f - w.StaticX; // ours: at least 2 cm of droop below the static position
            }
        }

        /// <summary>CG height above flat ground at rest with the current mass / setup (m): per axle contact radius - (BASEY + static travel),
        /// blended at CG_LOCATION (equals AcCarSpec.PhysicsCgHeightM with the car's defaults).</summary>
        public float StaticCgHeight()
        {
            float hf = 0.5f * ((Wheels[0].ContactRadius - (Wheels[0].Hub.Y + Wheels[0].StaticX)) + (Wheels[1].ContactRadius - (Wheels[1].Hub.Y + Wheels[1].StaticX)));
            float hr = 0.5f * ((Wheels[2].ContactRadius - (Wheels[2].Hub.Y + Wheels[2].StaticX)) + (Wheels[3].ContactRadius - (Wheels[3].Hub.Y + Wheels[3].StaticX)));
            return hf + (hr - hf) * (1f - Spec.CgLocation);
        }

        /// <summary>Static sag (m from the springs' free length) of one corner under its static load: corner springs k*s + prog*s^2, plus
        /// half the axle's heave spring when it has one (both in parallel for heave). Kept for the probes; the chassis uses <see cref="StaticTravel"/>.</summary>
        public static float Sag(AcAxleSpec ax, float load)
        {
            float k = Math.Max(0f, ax.SpringRate) + (ax.HasHeave ? 0.5f * Math.Max(0f, ax.HeaveRate) : 0f), p = ax.ProgressiveRate;
            if (k < 1000f) k = 1000f; // ours guard (a car with no spring at all)
            if (p > 1e-3f) return (float)((-k + Math.Sqrt(k * k + 4.0 * p * load)) / (2.0 * p));
            return load / k;
        }

        /// <summary>Static travel from the design point (m, + = compressed) of one corner under <paramref name="load"/> with the corner's rod
        /// length: solves spring(x + rod) + stops(x) + half the heave spring (x + heave rod) = load (bisection; not per step).</summary>
        public static float StaticTravel(AcAxleSpec ax, float load, float rod)
        {
            if (load <= 0f) return -Math.Max(0.02f, ax.BumpStopDn);
            float lo = -0.5f, hi = 0.5f; // ours search range (m)
            if (StaticForce(ax, hi, rod) < load) return hi;
            for (int it = 0; it < 40; it++)
            {
                float mid = 0.5f * (lo + hi);
                if (StaticForce(ax, mid, rod) < load) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        private static float StaticForce(AcAxleSpec ax, float x, float rod)
        {
            float k = Math.Max(ax.SpringRate, ax.HasHeave ? 0f : 1000f); // ours guard (a car with no spring at all)
            float f = SpringForce(k, ax.ProgressiveRate, x + rod) + Stops(x, ax.BumpStopUp, ax.BumpStopRate, ax.PackerRange, ax.SpringRate);
            if (ax.HasHeave)
                f += 0.5f * (SpringForce(ax.HeaveRate, ax.HeaveProgressive, x + ax.HeaveRodLength) + Stops(x, ax.HeaveBumpStopUp, ax.HeaveBumpStopRate, ax.HeavePackerRange, ax.HeaveRate));
            return f;
        }

        private static float SpringForce(float k, float prog, float compression)
        {
            return compression > 0f && k > 0f ? k * compression + prog * compression * compression : 0f;
        }

        /// <summary>After the ray casts: wheel frames on the ground (steered fronts + toe), travel from the contact, camber relative to the road
        /// (static camber + body roll * geometry recovery // ours 0.6 DWB / 0.8 STRUT), surface grip into the core.</summary>
        public void Prepare(AcVec3 up, AcVec3 fwd)
        {
            AcVec3 right = AcVec3.Cross(up, fwd);
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel wh = Wheels[i];
                // road angle: steering on the fronts + toe (toe-out turns the left wheel's front to the left = negative)
                float deg = (wh.Front ? SteerRoadDeg : 0f) + (wh.Left ? -wh.ToeDeg : wh.ToeDeg);
                wh.SteerDeg = deg;
                AcVec3 steerFwd = fwd;
                if (deg != 0f)
                {
                    double a = deg * Math.PI / 180.0;
                    steerFwd = fwd * (float)Math.Cos(a) + right * (float)Math.Sin(a); // rotate about up by +angle = to the right (Unity convention)
                }
                AcVec3 n = wh.Contact ? wh.Normal : up;
                AcVec3 f = steerFwd - n * AcVec3.Dot(steerFwd, n);
                float fl = f.Length;
                wh.Fwd = fl > 1e-4f ? f * (1f / fl) : fwd;
                wh.Side = AcVec3.Cross(n, wh.Fwd).Normalized;
                if (wh.Contact) wh.X = wh.ContactRadius - wh.Dist;
                float roll = (float)Math.Asin(Clamp(AcVec3.Dot(up, wh.Side), -1f, 1f));
                float rollGain = string.Equals(wh.Axle.Type, "STRUT", StringComparison.OrdinalIgnoreCase) ? 0.8f : 0.6f; // ours
                float staticCamber = wh.CamberDeg * (float)(Math.PI / 180.0) * (wh.Left ? -1f : 1f); // negative camber = top leans inward
                Core.Camber[i] = staticCamber + roll * rollGain;
                Core.Grip[i] = wh.Grip;
                Core.Damping[i] = wh.Damping;
            }
        }

        /// <summary>Runs <paramref name="n"/> sub-steps over <paramref name="dt"/>. <paramref name="extraF"/>/<paramref name="extraT"/> (aero) act
        /// in every sub-step. Outputs the AVERAGE force and torque (world) to apply to the rigid body for the whole dt (gravity not included).</summary>
        public void Step(float dt, int n, ref AcBodyState b, AcVec3 extraF, AcVec3 extraT, out AcVec3 avgF, out AcVec3 avgT)
        {
            if (n < 1) n = 1;
            float h = dt / n;
            AcVec3 v = b.V, w = b.W, up = b.Up;
            // the road plane under the car: the mean contact normal (one tilted face / kerb edge under one wheel does not dominate)
            AcVec3 gn = new AcVec3(0, 0, 0);
            for (int i = 0; i < 4; i++) if (Wheels[i].Contact) gn = gn + Wheels[i].Normal;
            float gl = gn.Length;
            gn = gl > 1e-3f ? gn * (1f / gl) : up;
            float un = AcVec3.Dot(up, gn);
            AcVec3 invI = new AcVec3(b.Inertia.X > 1e-4f ? 1f / b.Inertia.X : 0f, b.Inertia.Y > 1e-4f ? 1f / b.Inertia.Y : 0f, b.Inertia.Z > 1e-4f ? 1f / b.Inertia.Z : 0f);
            float invM = b.Mass > 1e-3f ? 1f / b.Mass : 0f;
            AcVec3 sumF = new AcVec3(0, 0, 0), sumT = new AcVec3(0, 0, 0);
            for (int s = 0; s < n; s++)
            {
                AcVec3 stepF = extraF, stepT = extraT;
                // pass 1: every wheel's travel first, so the ARB / heave spring (which read the OTHER wheel of the axle) see the same
                // instant on both sides - updating and reading in one loop gave a one-sided ARB force whenever the car pitched
                for (int i = 0; i < 4; i++)
                {
                    AcChassisWheel wh = Wheels[i];
                    if (!wh.Contact) continue;
                    AcVec3 pv = v + AcVec3.Cross(w, wh.Point - b.Com);
                    // travel rate = how fast the hub closes on the GROUND along the suspension axis: the ray length is h / (up . n), so
                    // d(travel)/dt = -(pv . n) / (up . n), n = the road plane under the car. Review 2026-11-01: this was -(pv . up), which
                    // on a body pitched against the road counts the forward speed as damper speed (nose down 1.5 deg at 100 km/h = 0.7 m/s
                    // of fake rebound on every corner): the dampers pulled the loads down, the springs compressed to make up for it and the
                    // car sank 70-90 mm under braking until the hull / floor box hit the road (REQUESTS "I -> P: the car's hull ... hits
                    // the road under ~1 g braking"; harness ks_toyota_gt86 brake 0.65 from 110: CG 0.409 -> 0.30 m before, 0.39-0.41 after).
                    float xdot = un > 0.2f ? -AcVec3.Dot(pv, gn) / un : -AcVec3.Dot(pv, up); // 0.2: past ~78 deg keep the body axis // ours
                    wh.X += xdot * h;
                    wh.XDot = xdot;
                    _pv[i] = pv;
                }
                // pass 2: forces from the travels
                for (int i = 0; i < 4; i++)
                {
                    AcChassisWheel wh = Wheels[i];
                    if (!wh.Contact || wh.X < -wh.Droop) { NoLoad(i, wh); continue; }
                    AcVec3 pv = _pv[i];
                    float fs = Suspension(wh, i);
                    wh.Load = fs;
                    float cos = AcVec3.Dot(up, wh.Normal);
                    Core.Load[i] = fs * (cos > 0f ? cos : 0f);
                    Core.VLong[i] = AcVec3.Dot(pv, wh.Fwd);
                    Core.VLat[i] = AcVec3.Dot(pv, wh.Side);
                }
                Core.Speed = AcVec3.Dot(v, b.Fwd);
                Core.Step(h);
                for (int i = 0; i < 4; i++)
                {
                    AcChassisWheel wh = Wheels[i];
                    if (wh.Load <= 0f) continue;
                    AcVec3 F = up * wh.Load + wh.Fwd * Core.Fx[i] + wh.Side * Core.Fy[i];
                    stepF = stepF + F;
                    stepT = stepT + AcVec3.Cross(wh.Point - b.Com, F);
                }
                v = v + (stepF * invM + b.Gravity) * h;
                AcVec3 tl = RotateInv(b, stepT);
                w = w + Rotate(b, new AcVec3(tl.X * invI.X, tl.Y * invI.Y, tl.Z * invI.Z)) * h;
                sumF = sumF + stepF; sumT = sumT + stepT;
            }
            float inv = 1f / n;
            avgF = sumF * inv;
            avgT = sumT * inv;
        }

        private void NoLoad(int i, AcChassisWheel wh)
        {
            Core.Load[i] = 0f; Core.VLong[i] = 0f; Core.VLat[i] = 0f; wh.Load = 0f;
        }

        /// <summary>Suspension force (N) along the body up. Travel X is measured from the DESIGN point (BASEY, springs at their free length;
        /// see AcCarSpec.PhysicsCgHeightM): wheel-rate spring on X + ROD_LENGTH (+ progressive), bump stop past BUMPSTOP_UP, packers past
        /// PACKER_RANGE (rate max(BUMP_STOP_RATE, 5 x spring) // ours), droop stop at BUMPSTOP_DN (the wheel leaves the ground), slow/fast
        /// dampers by damper speed, the ARB against the other side, and half the axle's heave spring ([HEAVE_*]: same keys + its own
        /// ROD_LENGTH on the mean travel of the two wheels).</summary>
        public float Suspension(AcChassisWheel wh, int i)
        {
            AcAxleSpec ax = wh.Axle;
            float x = wh.X;
            float f = SpringForce(Math.Max(ax.SpringRate, ax.HasHeave ? 0f : 1000f), ax.ProgressiveRate, x + wh.Rod); // 1000 N/m guard // ours
            f += Stops(x, ax.BumpStopUp, ax.BumpStopRate, ax.PackerRange, ax.SpringRate);
            f += Damper(wh.XDot, ax.DampBump, ax.DampFastBump, ax.DampFastBumpThreshold, ax.DampRebound, ax.DampFastRebound, ax.DampFastReboundThreshold);
            AcChassisWheel other = Wheels[i ^ 1];
            bool otherDown = other.Contact && other.X > -other.Droop;
            float xo = otherDown ? other.X : -other.Droop;
            if (ax.ArbRate > 0f && otherDown) f += ax.ArbRate * (x - other.X);
            if (ax.HasHeave)
            {
                float xh = 0.5f * (x + xo), vh = 0.5f * (wh.XDot + (otherDown ? other.XDot : 0f));
                float fh = SpringForce(ax.HeaveRate, ax.HeaveProgressive, xh + ax.HeaveRodLength);
                fh += Stops(xh, ax.HeaveBumpStopUp, ax.HeaveBumpStopRate, ax.HeavePackerRange, ax.HeaveRate);
                fh += Damper(vh, ax.HeaveDampBump, ax.HeaveDampFastBump, ax.HeaveDampFastBumpThreshold, ax.HeaveDampRebound, ax.HeaveDampFastRebound, ax.HeaveDampFastReboundThreshold);
                f += 0.5f * fh;
            }
            return f > 0f ? f : 0f;
        }

        private static float Stops(float x, float bumpUp, float bumpRate, float packer, float spring)
        {
            float f = 0f;
            if (bumpUp > 0f && x > bumpUp) f += bumpRate * (x - bumpUp);
            if (packer > 0f && x > packer) f += Math.Max(bumpRate, 5f * spring) * (x - packer); // 5 x // ours
            return f;
        }

        private static float Damper(float v, float bump, float fastBump, float bumpTh, float rebound, float fastRebound, float reboundTh)
        {
            if (v >= 0f) return bumpTh > 0f && v > bumpTh ? bump * bumpTh + fastBump * (v - bumpTh) : bump * v;
            float s = -v;
            return -(reboundTh > 0f && s > reboundTh ? rebound * reboundTh + fastRebound * (s - reboundTh) : rebound * s);
        }

        // ---- quaternion helpers (Hamilton, the same math Unity's Quaternion * Vector3 uses)
        public static AcVec3 Rotate(float qx, float qy, float qz, float qw, AcVec3 v)
        {
            // v' = v + 2w(q x v) + 2 q x (q x v)
            AcVec3 q = new AcVec3(qx, qy, qz);
            AcVec3 t = AcVec3.Cross(q, v) * 2f;
            return v + t * qw + AcVec3.Cross(q, t);
        }

        private static AcVec3 Rotate(AcBodyState b, AcVec3 v) { return Rotate(b.Qx, b.Qy, b.Qz, b.Qw, v); }
        private static AcVec3 RotateInv(AcBodyState b, AcVec3 v) { return Rotate(-b.Qx, -b.Qy, -b.Qz, b.Qw, v); }

        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
