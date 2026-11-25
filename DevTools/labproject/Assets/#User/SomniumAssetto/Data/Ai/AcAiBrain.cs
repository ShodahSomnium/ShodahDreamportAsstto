using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>What an AI driver senses this frame (AC track space: x_ac = -x_unity, z_ac = z_unity, +X = left).</summary>
    public struct AcAiSense
    {
        /// <summary>Car position (CG), AC space.</summary>
        public float X, Y, Z;
        /// <summary>Unit heading on the ground plane, AC space.</summary>
        public float FwdX, FwdZ;
        /// <summary>Ground velocity (m/s), AC space (x, z). Zero when unknown: the heading is used instead.</summary>
        public float VelX, VelZ;
        /// <summary>Speed along the heading (m/s, negative = rolling backwards).</summary>
        public float Speed;
        /// <summary>Yaw rate (rad/s, + = turning LEFT).</summary>
        public float YawRate;
        /// <summary>Gearbox: -1 R, 0 N, 1..; GearCount 0 = unknown (then the car's auto-shift is used).</summary>
        public int Gear, GearCount;
        public float Rpm;
        public bool Shifting;
        /// <summary>Largest slip ratio of the driven wheels (positive = spinning up), 0 when unknown.</summary>
        public float DrivenSlip;
        /// <summary>Frame time (s).</summary>
        public float Dt;
    }

    /// <summary>What an AI driver does this frame.</summary>
    public struct AcAiControls
    {
        /// <summary>Road-wheel angle in degrees, RIGHT positive (AcCarInput convention).</summary>
        public float RoadAngleDeg;
        public float Throttle, Brake, Handbrake;
        public bool ShiftUp, ShiftDown;
    }

    /// <summary>
    /// One AI driver's per-frame decisions, Unity-free (the harness drives it on a simulated car; Game/Ai feeds it P's physics):
    /// - Steering: pure pursuit to a point on the line <see cref="PreviewDistance"/> ahead (from ai.ini [LOOKAHEAD] BASE / SPEED_GAIN),
    ///   shifted by the overtaking offset; feedback scaled by ai.ini STEER_GAIN / 1.7; road angle = atan(wheelbase x curvature),
    ///   limited to STEER_LOCK / STEER_RATIO and a steering rate.
    /// - Speed: the car's own <see cref="AcAiPlan"/> (lowest target over the next GAS_BRAKE_LOOKAHEAD + 0.15 s x v metres), throttle
    ///   P-controller with ai.ini GASGAIN plus a drag feed-forward, brake from the planned deceleration plus the speed error; our traction
    ///   limit from ai.ini SLIP_THRESHOLD; throttle lifted GAS_CUTOFF_TIME after an upshift.
    /// - Gears (when <see cref="OwnShifting"/>): ai.ini UP / DOWN (v3 rpm, v4 percent of UP for the lower gear).
    /// - Reverse mode for recovery (the Unity side decides when).
    /// No allocation per frame.
    /// </summary>
    public sealed class AcAiBrain
    {
        public AcAiTrack Track;
        public AcAiPlan Plan;
        public readonly AcAiCar Car;

        /// <summary>Arc length of the car on the line (m) and its lateral offset (m, + left), updated by <see cref="Step"/>.</summary>
        public float S, Lateral;
        /// <summary>Line point index hint (-1 = search).</summary>
        public int Hint = -1;
        /// <summary>Desired lateral offset from the line (m, + left) set by the traffic logic; the brain slews to it and clamps it.</summary>
        public float OffsetTarget;
        /// <summary>The offset in use now.</summary>
        public float Offset;
        /// <summary>Speed cap from traffic (m/s, +inf = none).</summary>
        public float FollowCap = float.PositiveInfinity;
        /// <summary>Deceleration (m/s^2) the traffic logic needs to stop short of a car in the way this frame (0 = none): above
        /// <see cref="FollowDecelMin"/> the brain brakes at least that hard, off the throttle - the speed error alone brakes far too
        /// softly at low speed (0.12 per m/s: 24 % at 7 km/h) for a car standing a few metres ahead on the grid.</summary>
        public float FollowDecel;
        /// <summary>Below this the follow cap alone does it (m/s^2). // ours</summary>
        public const float FollowDecelMin = 1.5f;
        /// <summary>Speed cap from the session (cool-down lap after the flag, pit limiter) m/s, +inf = none.</summary>
        public float SessionCap = float.PositiveInfinity;
        /// <summary>Multiplier on the plan speed (1 = race pace).</summary>
        public float SpeedScale = 1f;
        /// <summary>Drive backwards to get free (recovery).</summary>
        public bool Reverse;
        /// <summary>While reversing: swing the nose to this side (+1 left, -1 right; 0 = toward the line) - backing off a car ahead to pass it.</summary>
        public int ReverseNose;
        /// <summary>Shift with ai.ini UP/DOWN (true) or leave it to the car's auto-shift (false).</summary>
        public bool OwnShifting = true;
        /// <summary>Half the car's width (m) for the corridor clamp.</summary>
        public float HalfWidth = 0.95f; // ours default (Unity side sets it from the model bounds)

        /// <summary>Last target speed (m/s), target point (AC space) - for the HUD / probes.</summary>
        public float TargetSpeed, TargetX, TargetZ;
        /// <summary>The car is outside the line's room (|lateral| beyond side + 1 m) this frame.</summary>
        public bool OffTrack;
        /// <summary>The steering asked for more than the tyres give this frame (the AI lifts).</summary>
        public bool Saturated;
        /// <summary>The car rotates more than asked (the pedals are eased this frame).</summary>
        public bool Oversteer;
        /// <summary>Catching a slide this frame (countersteer).</summary>
        public bool Countersteer;
        /// <summary>Heading error to the line tangent (rad, + = line points left of the car).</summary>
        public float HeadingError;

        float _steerDeg, _cutTimer, _shiftCooldown, _kInt, _kcmd, _sinceUp = 10f;
        int _lastGear = int.MinValue;

        /// <summary>Per-lap diagnostics (seconds; the lab's first Unity run showed the AI 1.74x slower than its plan with P's physics and
        /// the harness sim cannot show why): time driven, time > 2 m/s under the target speed, time the throttle was capped by the friction
        /// circle / our TC / the post-shift cut / the oversteer easing, time saturated / off the track, throttle x time, |steer| x time.
        /// Plain float sums: no allocation; <see cref="DiagText"/> formats them once a lap.</summary>
        public float DiagT, DiagUnder, DiagFc, DiagTc, DiagCut, DiagOver, DiagSat, DiagOff, DiagThrottle, DiagSteer;

        public void DiagReset() { DiagT = DiagUnder = DiagFc = DiagTc = DiagCut = DiagOver = DiagSat = DiagOff = DiagThrottle = DiagSteer = 0f; }

        /// <summary>One line of the per-lap diagnostics (allocates: once a lap).</summary>
        public string DiagText()
        {
            float t = Math.Max(0.001f, DiagT);
            System.Globalization.CultureInfo ic = System.Globalization.CultureInfo.InvariantCulture;
            return "under target " + (DiagUnder / t * 100f).ToString("0", ic) + " %, throttle capped by grip circle " + (DiagFc / t * 100f).ToString("0", ic)
                 + " % / TC " + (DiagTc / t * 100f).ToString("0", ic) + " % / shift cut " + (DiagCut / t * 100f).ToString("0", ic) + " % / oversteer " + (DiagOver / t * 100f).ToString("0", ic)
                 + " %, saturated " + (DiagSat / t * 100f).ToString("0", ic) + " %, off track " + (DiagOff / t * 100f).ToString("0", ic) + " %, mean throttle "
                 + (DiagThrottle / t).ToString("0.00", ic) + ", mean |road steer| " + (DiagSteer / t).ToString("0.0", ic) + " deg";
        }

        /// <summary>Extra corridor margin from the side limits (m). // ours</summary>
        public const float SideMargin = 0.35f;
        /// <summary>Offset slew rate (m/s). // ours</summary>
        public const float OffsetRate = 1.6f;
        /// <summary>Steering rate limit (road-wheel deg/s). // ours</summary>
        public const float SteerRateDeg = 70f;
        /// <summary>Traction slip limit at SLIP_THRESHOLD 1 (slip ratio). // ours</summary>
        public const float TractionSlip = 0.12f;
        /// <summary>Yaw-rate tracking gains (P, I per second) and the travel-direction aiming switch (tuned with harness aisim). // ours</summary>
        public static float YawP = 0.5f, YawI = 2.5f;
        public static bool UseVelocityFrame = false;

        public AcAiBrain(AcAiCar car, AcAiTrack track, AcAiPlan plan)
        {
            Car = car; Track = track; Plan = plan;
        }

        /// <summary>Switch to another line (pit lane -> fast lane); the position is re-found.</summary>
        public void SetLine(AcAiTrack track, AcAiPlan plan)
        {
            Track = track; Plan = plan; Hint = -1;
        }

        /// <summary>Preview distance (m) at speed v: 0.35 x BASE + SPEED_GAIN x v + 0.3 s x v, 5..60 m. // ours formula on ai.ini values</summary>
        public float PreviewDistance(float v)
        {
            v = Math.Abs(v);
            float d = 0.35f * Car.Ai.LookaheadBase + Car.Ai.SpeedGain * v + 0.6f * v; // ours
            return d < 5f ? 5f : d > 80f ? 80f : d; // ours
        }

        /// <summary>Runs one frame. Returns the controls; updates <see cref="S"/>, <see cref="Lateral"/>, <see cref="Offset"/>.</summary>
        public AcAiControls Step(ref AcAiSense e)
        {
            var c = new AcAiControls();
            if (Track == null || Track.IsEmpty || Plan == null) { c.Brake = 1f; return c; }
            float dt = e.Dt > 0f ? (e.Dt < 0.1f ? e.Dt : 0.1f) : 0.02f;
            float v = e.Speed, av = Math.Abs(v);

            S = Track.Project(e.X, e.Y, e.Z, ref Hint, out Lateral);

            // ---- corridor + offset
            float tx0, tz0, x0, z0, k0, sl0, sr0;
            Track.Sample(S, out x0, out z0, out tx0, out tz0, out k0, out sl0, out sr0);
            OffTrack = Lateral > sl0 + 1f || -Lateral > sr0 + 1f; // ours: 1 m beyond the line's room
            float dOff = OffsetTarget - Offset, maxStep = OffsetRate * dt;
            Offset += dOff > maxStep ? maxStep : dOff < -maxStep ? -maxStep : dOff;

            // ---- steering: pure pursuit to the (offset) line point ahead
            float D = PreviewDistance(v);
            float px, pz, ptx, ptz, pk, psl, psr;
            Track.Sample(S + D, out px, out pz, out ptx, out ptz, out pk, out psl, out psr);
            float lim = psl - HalfWidth - SideMargin, rim = psr - HalfWidth - SideMargin;
            float off = Offset;
            if (lim < 0f) lim = 0f; if (rim < 0f) rim = 0f;
            if (off > lim) off = lim; else if (off < -rim) off = -rim;
            px += ptz * off; pz += -ptx * off; // left normal (tz, -tx)
            TargetX = px; TargetZ = pz;
            float dx = px - e.X, dz = pz - e.Z;
            // aim in the frame of the TRAVEL direction once moving (a sliding car points elsewhere than it goes) // ours
            float hx = e.FwdX, hz = e.FwdZ, vel = (float)Math.Sqrt(e.VelX * e.VelX + e.VelZ * e.VelZ);
            if (UseVelocityFrame && vel > 8f && !Reverse && (e.VelX * e.FwdX + e.VelZ * e.FwdZ) > 0.8f * vel) { hx = e.VelX / vel; hz = e.VelZ / vel; }
            float fwd = dx * hx + dz * hz;                 // along the travel direction
            float left = dx * hz + dz * -hx;              // to its left
            float ld2 = dx * dx + dz * dz;
            float kpp = ld2 > 0.01f ? 2f * left / ld2 : 0f;
            if (fwd < 0f) kpp = left >= 0f ? 2f / D : -2f / D;   // target behind: full turn toward it
            HeadingError = (float)Math.Atan2(e.FwdZ * tx0 - e.FwdX * tz0, e.FwdX * tx0 + e.FwdZ * tz0);
            // STEER_GAIN shapes how hard the AI corrects (pure pursuit itself is gain-free); curvature feed-forward stays exact
            float gain = Car.Ai.SteerGain / 1.7f; // ours: 1.7 = ferrari_458 reference
            gain = gain < 0.7f ? 0.7f : gain > 1.4f ? 1.4f : gain;
            float kff = k0;
            float kcmd = kff + (kpp - kff) * gain;
            // never ask for more turn than the tyres can give at this speed: a sliding car is slowed, not steered harder // ours
            bool saturated = false;
            if (av > 8f)
            {
                float kcap = 1.05f * Plan.LatCap(av) / Math.Max(0.5f, Plan.GripUse) / (av * av); // ours: 5 % over the full grip
                if (kcmd > kcap) { kcmd = kcap; saturated = true; } else if (kcmd < -kcap) { kcmd = -kcap; saturated = true; }
            }
            Saturated = saturated;
            _kcmd = kcmd;
            // the tyres need slip angles: track the asked curvature with the measured yaw rate (P + I on the yaw error); the
            // kinematic angle atan(L k) alone understeers at speed // ours gains
            float maxRoad = Car.MaxRoadAngleDeg;
            float ksteer = kcmd;
            if (av > 5f && !Reverse)
            {
                float ey = kcmd - e.YawRate / av;
                bool atLock = Math.Abs(_steerDeg) >= maxRoad * 0.98f;
                if (!atLock || (ey > 0f) != (_kInt > 0f)) _kInt += ey * dt * YawI;
                float kiMax = 0.6f * (float)Math.Tan(maxRoad * Math.PI / 180.0) / Car.WheelbaseM;
                if (_kInt > kiMax) _kInt = kiMax; else if (_kInt < -kiMax) _kInt = -kiMax;
                ksteer = kcmd + YawP * ey + _kInt;
            }
            else _kInt *= 0.9f;
            float leftDeg = (float)(Math.Atan(Car.WheelbaseM * ksteer) * 180.0 / Math.PI);
            if (Reverse) leftDeg = ReverseNose != 0 ? -ReverseNose * maxRoad * 0.8f // backing off a car ahead: the nose swings to the pass side
                                 : HeadingError > 0f ? -maxRoad * 0.8f : maxRoad * 0.8f; // reversing: rear swings, nose turns to the line
            // at speed never more lock than the grip-limited turn plus the front tyres' peak slip angle (no snap oversteer in chicanes) // ours
            if (av > 8f && !Reverse)
            {
                float kcapS = Plan.LatCap(av) / Math.Max(0.5f, Plan.GripUse) / (av * av);
                float lockLim = (float)(Math.Atan(Car.WheelbaseM * kcapS) * 180.0 / Math.PI) + 0.6f * Car.FrictionLimitAngleFront;
                if (lockLim < maxRoad) { if (leftDeg > lockLim) leftDeg = lockLim; else if (leftDeg < -lockLim) leftDeg = -lockLim; }
            }
            // the rear steps out (the car points further into the turn than it travels): catch it - front wheels toward the travel
            // direction, whatever the line wants; full catch from 7 deg of body slip // ours thresholds
            if (av > 6f && !Reverse && vel > 6f)
            {
                float vFwd = e.VelX * e.FwdX + e.VelZ * e.FwdZ, vLeft = e.VelX * e.FwdZ - e.VelZ * e.FwdX;
                float beta = (float)(Math.Atan2(vLeft, Math.Max(1f, vFwd)) * 180.0 / Math.PI);
                if (beta * e.YawRate < 0f && Math.Abs(beta) > 3f)
                {
                    float w = Math.Min(1f, (Math.Abs(beta) - 3f) / 4f);
                    leftDeg = leftDeg + (beta * 1.1f - leftDeg) * w;
                    Countersteer = true;
                }
                else Countersteer = false;
            }
            if (leftDeg > maxRoad) leftDeg = maxRoad; else if (leftDeg < -maxRoad) leftDeg = -maxRoad;
            // steering speed falls with road speed (70 deg/s road angle at 50 km/h -> 25 deg/s at 250 km/h) // ours
            float rateDeg = av < 14f ? SteerRateDeg : Math.Max(25f, SteerRateDeg - (av - 14f) * 0.81f);
            float wantRight = -leftDeg, rate = rateDeg * dt * (av < 3f ? 2f : 1f);
            float ds = wantRight - _steerDeg;
            _steerDeg += ds > rate ? rate : ds < -rate ? -rate : ds;
            c.RoadAngleDeg = _steerDeg;

            // ---- speed
            float look = Car.Ai.GasBrakeLookahead + 0.15f * av + 2f; // ours: reaction distance
            float vt = Plan.MinSpeedAhead(S, look) * SpeedScale;
            if (FollowCap < vt) vt = FollowCap;
            if (SessionCap < vt) vt = SessionCap;
            float absHe = Math.Abs(HeadingError);
            if (absHe > 1.0f) vt = Math.Min(vt, 8f); // ours: pointing far off the line (spun): crawl back
            else if (OffTrack) vt = Math.Min(vt, Math.Max(12f, av * 0.9f)); // ours: off the track: no flat out
            if (saturated && vt > av) vt = av; // ours: at the grip limit: no more throttle (lift, never brake for it)
            TargetSpeed = vt;

            if (_cutTimer > 0f) _cutTimer -= dt;
            if (_shiftCooldown > 0f) _shiftCooldown -= dt;

            if (Reverse)
            {
                if (e.GearCount > 0 && e.Gear >= 0) { c.Throttle = 0f; c.Brake = 1f; } // not in reverse yet: stand still, then shift
                else { c.Throttle = v > -4f ? 0.55f : 0.2f; c.Brake = 0f; } // ours: reverse gently
                Gearbox(ref e, ref c, true);
                return c;
            }

            float err = vt - v;
            float drive = Car.DriveForce(Math.Max(av, 1f));
            float ff = drive > 1f ? (Car.Drag(av) + Car.RollingN) / drive : 0f;
            if (err >= 0f)
            {
                c.Throttle = Clamp01(ff + 0.5f * Car.Ai.GasGain * err); // ours: 0.5 per m/s at GASGAIN 1
            }
            else
            {
                float vNext = Plan.SpeedAt(S + Math.Max(5f, av * 0.25f)) * SpeedScale; // ours: 0.25 s ahead
                float dsAhead = Math.Max(5f, av * 0.25f);
                float aPlan = (v * v - vNext * vNext) / (2f * dsAhead);
                float bmax = Math.Max(1f, Car.BrakeDecelMax);
                float brake = (aPlan > 0f ? aPlan / bmax : 0f) + (-err) * 0.12f; // ours: 0.12 per m/s over
                if (err > -0.6f) { c.Throttle = Clamp01(ff * (1f + err / 0.6f)); brake = 0f; } // ours: coast band
                c.Brake = Clamp01(brake);
            }
            // a car in the way (AcAiTraffic): brake at least as hard as stopping short of it needs
            if (FollowDecel > FollowDecelMin && v > 0.2f)
            {
                float bf = FollowDecel / Math.Max(1f, Car.BrakeDecelMax);
                if (bf > c.Brake) c.Brake = Clamp01(bf);
                c.Throttle = 0f;
            }
            // friction circle: while cornering only the grip left over may drive the wheels (no power oversteer / understeer) // ours
            bool fcCapped = false;
            if (av > 3f && drive > 1f)
            {
                float latCap = Plan.LatCap(av) / Math.Max(0.5f, Plan.GripUse); // the tyres' full lateral capacity
                float use = Math.Min(1f, Math.Abs(e.YawRate * av) / Math.Max(1f, latCap));
                float longCap = Car.MuLong * Plan.SurfaceGrip * (AcAiCar.G + Car.Downforce(av) / Car.MassKg) * Car.DrivenWeightShare * Car.MassKg;
                float avail = longCap * (float)Math.Sqrt(Math.Max(0f, 1f - use * use)) + Car.Drag(av) + Car.RollingN;
                float tmax = Math.Max(0.12f, avail / drive); // ours: never below 12 %
                if (c.Throttle > tmax) { c.Throttle = tmax; fcCapped = true; }
            }
            // traction limit (our TC on top of the car's): ai.ini SLIP_THRESHOLD x 0.12
            float slipLim = TractionSlip * Car.Ai.SlipThreshold;
            if (e.DrivenSlip > slipLim) c.Throttle *= Math.Max(0.15f, 1f - (e.DrivenSlip - slipLim) * 6f); // ours
            if (_cutTimer > 0f) c.Throttle = 0f;
            // stability: the car rotates clearly more than asked (oversteer, rear stepping out on the brakes or a lift): ease off
            // both pedals so the rear grips again - what a driver does // ours thresholds
            if (av > 8f)
            {
                float asked = av * _kcmd, yaw = e.YawRate;
                float over = Math.Abs(yaw) - Math.Abs(asked);
                if (over > 0.08f && (asked == 0f || (yaw > 0f) == (asked > 0f) || Math.Abs(asked) < 0.05f))
                {
                    float f = Math.Max(0.2f, 1f - (over - 0.08f) * 6f);
                    c.Brake *= f; c.Throttle *= Math.Max(0.5f, f);
                    Oversteer = true;
                }
                else Oversteer = false;
            }
            // per-lap diagnostics (see DiagT)
            DiagT += dt;
            if (err > 2f) DiagUnder += dt; // ours: 2 m/s
            if (c.Throttle < 0.999f && err > 0.5f) // wanted more throttle than it got: which cap held it
            {
                if (_cutTimer > 0f) DiagCut += dt;
                else if (e.DrivenSlip > slipLim) DiagTc += dt;
                else if (Oversteer) DiagOver += dt;
                else if (fcCapped) DiagFc += dt;
            }
            if (Saturated) DiagSat += dt;
            if (OffTrack) DiagOff += dt;
            DiagThrottle += c.Throttle * dt;
            DiagSteer += Math.Abs(c.RoadAngleDeg) * dt;
            Gearbox(ref e, ref c, false);
            return c;
        }

        void Gearbox(ref AcAiSense e, ref AcAiControls c, bool reverse)
        {
            if (_sinceUp < 10f) _sinceUp += e.Dt > 0f ? (e.Dt < 0.1f ? e.Dt : 0.1f) : 0.02f;
            if (e.Gear != _lastGear) { if (_lastGear != int.MinValue && e.Gear > _lastGear && e.Gear > 1) _cutTimer = Car.Ai.GasCutoffTime; _lastGear = e.Gear; }
            if (!OwnShifting || e.GearCount <= 0 || e.Shifting || _shiftCooldown > 0f) return;
            int g = e.Gear;
            if (reverse)
            {
                if (g > -1 && Math.Abs(e.Speed) < 1.5f) { c.ShiftDown = true; c.Throttle = 0f; c.Brake = 1f; _shiftCooldown = 0.35f; } // ours
                return;
            }
            if (g <= 0)
            {
                if (e.Speed < -1f) { c.Throttle = 0f; c.Brake = 1f; return; } // stop rolling back first
                c.ShiftUp = true; _shiftCooldown = 0.35f; // ours: R -> N -> 1
                return;
            }
            // Shift points from ai.ini UP / DOWN, judged on the ROAD rpm (ground speed through the gear) - not the engine rpm, which in P's
            // physics carries the driven wheels' slip (4-6 % under power) and falls while the clutch is open during the change (GT86 0.25 s).
            // Polish pass 2026-11-01, lab + harness aichassis: GT86 v3 UP 7200 / DOWN 5000 - the 2->3 change at 7200 engine rpm with 4 % slip
            // lands 3rd at 4850 rpm < DOWN -> straight back to 2nd -> 3rd ... (the car hunted 2-3-2 at 91 km/h for whole straights).
            // The same at the 1-2 change in a hairpin: 2nd at 55 km/h = 4180 < DOWN and 1st would run 6930 < 0.97 x UP -> down; 1st reaches UP
            // at 57 km/h -> up -> 2nd 4340 < DOWN -> down ... and the engine rpm right after a change is the autoblip / free revving.
            // Rules now: upshift on the driven wheels' rpm (road rpm x (1 + slip), never the engine's transient), only when the next gear
            // stays above the downshift point (or the wheels reach the limiter); v3 downshift only when the lower gear runs under 90 % of UP
            // (v4's DOWN % of UP is a band already); no downshift in the first second after an upshift unless braking. // ours rules
            float up = Math.Min(Car.Ai.UpRpm, Car.LimiterRpm - 50f);
            float av = Math.Abs(e.Speed);
            float road = Car.RpmAt(av, g);
            float wheels = road * (1f + (e.DrivenSlip > 0f ? Math.Min(e.DrivenSlip, 0.15f) : 0f)); // ours: slip counted up to 15 %
            if (road <= 0f) road = wheels = e.Rpm; // no gear table: the engine rpm
            float downRpm = Car.Ai.DownIsPercent ? Car.Ai.DownshiftRpm : Car.Ai.DownRaw;
            if (g < e.GearCount && wheels >= up && c.Throttle > 0.3f)
            {
                float next = Car.RpmAt(av, g + 1);
                // v3: next gear's rpm must stay above DOWN; v4 (DOWN = lower-gear rule): coming back from g+1 needs road < DownshiftRpm in g
                bool holds = next <= 0f || (Car.Ai.DownIsPercent ? road >= downRpm : next > downRpm) || wheels >= Car.LimiterRpm - 50f;
                if (holds) { c.ShiftUp = true; _shiftCooldown = 0.3f; _sinceUp = 0f; return; } // ours
            }
            if (g > 1 && g - 2 < Car.Gears.Length && g - 1 < Car.Gears.Length)
            {
                float lower = road * Car.Gears[g - 2] / Car.Gears[g - 1];
                bool down = Car.Ai.DownIsPercent ? lower < downRpm : road < downRpm && lower < up * 0.9f; // ours: 90 % band (was 97 %: hunting)
                if (down && _sinceUp < 1f && c.Brake < 0.1f && road > 0.8f * downRpm) down = false; // ours: 1 s / 80 % hysteresis
                if (down) { c.ShiftDown = true; _shiftCooldown = 0.25f; } // ours
            }
        }

        /// <summary>Forget controller state (after a reset / teleport).</summary>
        public void ResetState()
        {
            _steerDeg = 0f; _kInt = 0f; _cutTimer = 0f; _shiftCooldown = 0f; _sinceUp = 10f; Hint = -1; Offset = 0f; OffsetTarget = 0f; Reverse = false; ReverseNose = 0;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    }
}
