using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>What an AI driver is doing (for the HUD / tablet / lab).</summary>
    public enum AcAiState { Building, Pits, Driving, Reversing, Finished, Rails, Failed, Retired }

    /// <summary>
    /// [I, AI drivers] One AI car: an AcCarInput producer following fast_lane.ai (pit_lane.ai out of the pits) with the car's own ai.ini and a
    /// speed plan for THIS car (Data/Ai: AcAiBrain, AcAiPlan), overtaking offsets from <see cref="AcAiTraffic"/>, recovery when stuck / off /
    /// wrong way (reverse, then ResetTo the line), driving P's physics on C's model (an AcCarRig of Kind Ai). Run by the instance master;
    /// other clients draw AI cars from N's "Ac Net Ai" blocks. Without a Rigidbody (P missing) the car runs "on rails" along the line.
    /// </summary>
    public sealed class AcAiDriver
    {
        /// <summary>0-based AI index (grid id = -(100 + Index)).</summary>
        public int Index;
        public string Name = "";
        /// <summary>The AI's car (null while building).</summary>
        public AcCarRig Rig;
        /// <summary>AI_LEVEL 0..100.</summary>
        public int Level = 90;

        // ---- added by I (read-only for others)
        /// <summary>The car folder / skin folder this AI drives.</summary>
        public string CarId = "", SkinId = "";
        /// <summary>What it is doing now.</summary>
        public AcAiState State = AcAiState.Building;
        /// <summary>Arc length on the line it follows (m) and the lateral offset from it (m, + = left).</summary>
        public float LineS, Lateral;
        /// <summary>Following the pit lane (leaving the pits) instead of the fast lane.</summary>
        public bool OnPitLine;
        /// <summary>Target speed this frame (km/h) and the planned lap time at this level (s).</summary>
        public float TargetKmh, PlannedLapS;
        /// <summary>Times it was put back on the track.</summary>
        public int Recoveries;
        /// <summary>The ai.ini / physics digest and the controller (null until built).</summary>
        public AcAiCar CarData;
        public AcAiBrain Brain;
        /// <summary>Litres this car started with (race distance or car.ini FUEL - AcAiPlan.FuelLoad) and times it was topped up (no AI pit stops).</summary>
        public float FuelLoad;
        public int FuelTopUps;
        /// <summary>True once the car exists and drives (false while building and for drivers whose car could not be built - R: skip those
        /// in grids / standings once <see cref="AcAi.Ready"/>).</summary>
        public bool Live { get { return Rig != null && Rig.Root != null && State != AcAiState.Failed; } }
        internal AcAiPlan FastPlan, PitPlan;
        /// <summary>The fast-lane speed plan this driver follows (lap time, fuel per lap; null until built).</summary>
        public AcAiPlan FastLanePlan { get { return FastPlan; } }
        internal float FuelCheckAt, BrakeEase = 1f, MergeWait;
        internal AcAiTrack FastTrack, PitTrack;
        internal bool LapLogged;
        /// <summary>Laps logged with the brain's diagnostics (the first <see cref="AcAi.DiagLaps"/> laps of each driver).</summary>
        internal int LapsLogged;
        internal float HalfLength = 2.2f; // ours default until the model bounds are known
        internal float StuckTime, ReverseTime, OffTime, WrongTime;
        internal float SteerRatioEff;
        internal Vector3 RailsPos;
        internal float RailsSpeed;
        internal bool WasFrozen;
        public int PassSide; // +1 left, -1 right, 0 none (public for the lab)
        internal int LastPlacements;
        internal float PassUntil;
        /// <summary>This frame: standing behind a standing car with no room to creep on (AcAiTraffic) - Recover counts it as stuck.</summary>
        public bool Blocked;

        // ---- damage (iteration 2, DAMAGE_DESIGN 8): a blown engine parks the car at the track side
        /// <summary>Time (Time.time) the engine was found dead; -1 = not retired.</summary>
        public float RetiredAt = -1f;
        /// <summary>The side it parks on (+1 left, -1 right; AC lateral sign) and whether I froze it there (I unfreeze it after a repair).</summary>
        internal int RetireSide;
        internal bool RetireFrozen;
        internal int RetirePlacements;
        /// <summary>The room from the line to its edge on the parking side when it retired (m; lab / HUD read-out).</summary>
        public float RetireRoom;
        /// <summary>The lateral it parks at (m from the line, + = left).</summary>
        public float RetireTarget;
        /// <summary>Seconds to steer over to the side before the full brake (AC 0.21.6 "try to park on the side"). // ours</summary>
        public const float RetireSteerSeconds = 3f;
        /// <summary>Park with the car's centre this far beyond the line's edge (0 = on the edge, half the car on the verge). // ours</summary>
        public const float RetireBeyondSide = 0f;

        /// <summary>Stuck this long (s) wanting to move -> reverse; still stuck after this long in total -> reset. // ours (brief: 5 s)</summary>
        public const float ReverseAfter = 2f, ResetAfter = 5f;
        /// <summary>Reverse for at most this long (s). // ours</summary>
        public const float ReverseFor = 2.5f;
        /// <summary>Far off the line (m beyond the side) for this long (s) -> reset. // ours</summary>
        public const float FarOffMetres = 8f, FarOffSeconds = 4f;
        /// <summary>Pointing backwards (> 115 deg) while rolling for this long (s) -> reset. // ours</summary>
        public const float WrongWaySeconds = 3f;

        /// <summary>Computes this frame's controls into Rig.Physics.Input (or moves the car on rails without physics).</summary>
        public void Produce()
        {
            if (Rig == null || Rig.Root == null || Brain == null) return;
            AcCarPhysics ph = Rig.Physics;
            if (ph == null || ph.Body == null) { Rails(); return; }
            float dt = Time.deltaTime;
            AcPhase phase = AcSession.Phase;
            // ---- damage: repaired (R: new session / grid) after a blown engine -> drive on (my parking freeze released)
            AcCarDamage dmg = ph.Damage;
            bool dead = dmg != null && dmg.EngineDead;
            if (RetiredAt >= 0f && !dead) Unretire(ph);
            if (ph.Frozen || phase < AcPhase.Practice || phase == AcPhase.Grid || phase == AcPhase.Countdown)
            {
                // on the grid / frozen: foot on the brake, engine idling (R releases at the green)
                Hold(ph);
                WasFrozen = true;
                StuckTime = 0f; OffTime = 0f; WrongTime = 0f;
                return;
            }
            int placements = 0;
            try { placements = ph.Placements; } catch (Exception) { }
            if (WasFrozen || placements != LastPlacements)
            {
                // released from the grid, or somebody (R: grid / pits) moved the car: follow whichever line it now stands on
                WasFrozen = false; LastPlacements = placements;
                ChooseLine(ph.Body.position);
            }
            if (!ph.Driven) { try { ph.SetDriven(true); } catch (Exception ex) { AcLog.WarnOnce("Ai: SetDriven failed: " + ex.Message); } }

            // ---- sense (Unity -> AC track space)
            Rigidbody b = ph.Body;
            Vector3 pos = b.position, fwd = b.rotation * Vector3.forward, vel = b.linearVelocity;
            float ax, ay, az;
            AcTrackWorld.ToAc(pos, out ax, out ay, out az);
            var e = new AcAiSense();
            e.X = ax; e.Y = ay; e.Z = az;
            float fx = -fwd.x, fz = fwd.z, fl = Mathf.Sqrt(fx * fx + fz * fz);
            if (fl > 1e-4f) { fx /= fl; fz /= fl; } else { fx = 0f; fz = 1f; }
            e.FwdX = fx; e.FwdZ = fz;
            e.VelX = -vel.x; e.VelZ = vel.z;
            e.Speed = e.VelX * fx + e.VelZ * fz;
            e.YawRate = -b.angularVelocity.y; // Unity +y yaw turns right; AC + = left
            AcTelemetry t = ph.Telemetry;
            e.Gear = t.Gear; e.GearCount = t.GearCount; e.Rpm = t.Rpm; e.Shifting = t.Shifting;
            e.DrivenSlip = DrivenSlip(ref t, CarData != null ? CarData.Traction : "RWD");
            e.Dt = dt;

            // ---- blown engine (damage on): steer to the side, brake, hold (DAMAGE_DESIGN 8; R marks it DNF in a race)
            if (dead) { Retire(ph, ref e); return; }

            // ---- fuel (no AI pit stops): top up when less than a lap is left
            if (Time.time >= FuelCheckAt) { FuelCheckAt = Time.time + 1f; CheckFuel(ph); } // ours: once a second

            // ---- pit lane -> fast lane (wait at the pit exit while fast-lane traffic is coming)
            float mergeCap = float.PositiveInfinity;
            if (OnPitLine && PitTrack != null && Brain.Track == PitTrack)
            {
                mergeCap = MergeCap(dt);
                if (PitLineDone(Brain.S)) LeavePitLine();
            }

            // ---- the end of an open line (drag strip / hill climb): through the finish, then stop (AcAi.FindFinish)
            if (!OnPitLine && FastTrack != null && !FastTrack.IsClosed && Brain.Track == FastTrack && Brain.Hint >= 0)
            {
                if (AcAi.OpenRunOut)
                {
                    if (FastTrack.Length - Brain.S < 10f || State == AcAiState.Finished) { RunOut(ph, ref e); return; } // ours: last 10 m
                }
                else
                {
                    float stop = AcAi.OpenStopS > 0f ? AcAi.OpenStopS : FastTrack.Length;
                    if (stop - Brain.S < 15f) { Hold(ph); State = AcAiState.Finished; StuckTime = 0f; return; } // ours: 15 m
                }
            }

            // ---- session caps
            float cap = float.PositiveInfinity;
            if (OnPitLine) cap = Math.Max(5f, SafeRules().PitLimitKmh / 3.6f * 0.95f); // ours: 5 % under the limit
            if (mergeCap < cap) cap = mergeCap;
            bool finished = IsFinished();
            Brain.SpeedScale = finished ? 0.72f : 1f; // ours: cool-down lap after the flag
            Brain.SessionCap = cap;
            Brain.OwnShifting = e.GearCount > 0;
            if (ph.Assists != null && ph.Assists.AutoShift == Brain.OwnShifting) ph.Assists.AutoShift = !Brain.OwnShifting; // the car shifts itself until P reports its gearbox
            State = finished ? AcAiState.Finished : OnPitLine ? AcAiState.Pits : Brain.Reverse ? AcAiState.Reversing : AcAiState.Driving;

            // ---- traffic (offset / follow speed / emergency) computed by AcAiTraffic for this frame
            AcAiTraffic.Decide(this, pos, vel);

            AcAiControls c = Brain.Step(ref e);
            LineS = Brain.S; Lateral = Brain.Lateral; TargetKmh = Brain.TargetSpeed * 3.6f;

            // ---- recovery
            if (Recover(ph, dt, ref e, pos)) return;

            // ---- out
            Write(ph, ref c);
        }

        /// <summary>
        /// A finish at the very end of an open line (trento-bondone's AC_AB_FINISH at progress 1.000): wheels straight along the line's last
        /// tangent (a small heading correction), roll on to cross the end (throttle 0.3 below 8 m/s // ours), full brake once past it, then hold.
        /// </summary>
        private void RunOut(AcCarPhysics ph, ref AcAiSense e)
        {
            float x, z, tx, tz, k, sl, sr;
            FastTrack.Sample(FastTrack.Length, out x, out z, out tx, out tz, out k, out sl, out sr);
            float along = (e.X - x) * tx + (e.Z - z) * tz;
            State = AcAiState.Finished; StuckTime = 0f; OffTime = 0f; WrongTime = 0f;
            LineS = FastTrack.Length; TargetKmh = 0f;
            if (along > 0f && Math.Abs(e.Speed) < 0.5f) { Hold(ph); return; }
            if (along > 80f) { Hold(ph); return; } // ours: never further than 80 m past the end
            // heading error to the last tangent: + = the tangent is to the car's left (AC +X = left; left of (fx,fz) is (fz,-fx))
            float cross = e.FwdZ * tx - e.FwdX * tz, dot = e.FwdX * tx + e.FwdZ * tz;
            float errDeg = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            var c = new AcAiControls();
            c.RoadAngleDeg = -Mathf.Clamp(errDeg * 0.5f, -3f, 3f); // RIGHT positive: a tangent to the left steers left // ours: gentle, at most 3 deg
            c.Throttle = along < 0f && e.Speed < 8f ? 0.3f : 0f;
            c.Brake = along >= 0f ? 1f : 0f;
            Write(ph, ref c);
        }

        /// <summary>
        /// Blown engine (P: Damage.EngineDead, only with damage on): AC 0.21.6 "try to park on the side". The car coasts (P gives a dead engine
        /// no throttle), its own pure pursuit steers it to the edge of the line's side it is on (or holds it where it is when it already runs wider) -
        /// braking lightly for <see cref="RetireSteerSeconds"/>, then fully; below 1 km/h (R's RetireStopKmh) it is frozen there. No recovery
        /// teleport and no traffic logic while retired; R classifies it DNF in a race. Repair (R: grid / new session) -> <see cref="Unretire"/>.
        /// </summary>
        private void Retire(AcCarPhysics ph, ref AcAiSense e)
        {
            float now = Time.time;
            if (RetiredAt < 0f)
            {
                RetiredAt = now;
                RetireSide = Brain.Lateral >= 0f ? 1 : -1;      // the side of the line it already is on (+ = left)
                RetireRoom = SideAt(RetireSide > 0);
                RetireTarget = RetireSide * Mathf.Max(Mathf.Abs(Brain.Lateral), RetireRoom + RetireBeyondSide);   // never back toward the line
                RetireFrozen = false;
                AcLog.Info("Ai: " + Name + " engine blown - parking at the " + (RetireSide > 0 ? "left" : "right") + " side (" + AcSession.Phase + ")");
            }
            State = AcAiState.Retired;
            StuckTime = 0f; ReverseTime = 0f; OffTime = 0f; WrongTime = 0f; TargetKmh = 0f;
            Brain.Reverse = false; Brain.ReverseNose = 0;
            float speed = ph.SpeedKmh;
            if (speed < AcRace.RetireStopKmh && now - RetiredAt > 0.5f)    // ours: 0.5 s after the blow-up (a car that stands already is parked where it stands)
            {
                Hold(ph);
                try { ph.Freeze(true); RetireFrozen = true; RetirePlacements = ph.Placements; }
                catch (Exception ex) { AcLog.WarnOnce("Ai: parking freeze failed: " + ex.Message); }
                return;
            }
            AcAiTrack tr = Brain.Track;
            if (tr == null || tr.IsEmpty) { Hold(ph); return; }
            float lat;
            float s = tr.Project(e.X, e.Y, e.Z, ref Brain.Hint, out lat);
            Brain.S = s; LineS = s; Lateral = lat;
            // own pure pursuit to a point on the line ahead shifted to the parking lateral (the brain would clamp it into the line's room)
            float v = Math.Abs(e.Speed);
            float d = Math.Max(6f, v * 1.0f);                // ours: preview 1 s, at least 6 m
            float x, z, tx, tz, k, sl, sr;
            tr.Sample(tr.WrapS(s + d), out x, out z, out tx, out tz, out k, out sl, out sr);
            float px = x + tz * RetireTarget, pz = z - tx * RetireTarget;   // left normal (tz, -tx), as AcAiBrain
            float dx = px - e.X, dz = pz - e.Z;
            float left = dx * e.FwdZ - dz * e.FwdX, l2 = dx * dx + dz * dz;
            float kappa = l2 > 0.01f ? 2f * left / l2 : 0f;
            float wb = ph.Spec != null && ph.Spec.WheelbaseM > 1f ? ph.Spec.WheelbaseM : 2.6f;
            var c = new AcAiControls();
            c.RoadAngleDeg = -Mathf.Atan(wb * kappa) * Mathf.Rad2Deg;  // RIGHT positive (as RunOut): a target to the left steers left
            c.Throttle = 0f;
            c.Brake = now - RetiredAt < RetireSteerSeconds ? 0.15f : 1f;     // ours: a light brake while moving over, then stop
            Write(ph, ref c);
        }

        /// <summary>The engine works again (R repaired the car): my parking freeze is released unless somebody placed the car since (R's grid
        /// then owns the freeze), and the driver picks its line again.</summary>
        private void Unretire(AcCarPhysics ph)
        {
            bool moved = ph.Placements != RetirePlacements;
            if (RetireFrozen && ph.Frozen && !moved)
            {
                try { ph.Freeze(false); } catch (Exception ex) { AcLog.WarnOnce("Ai: unfreeze after repair failed: " + ex.Message); }
            }
            AcLog.Info("Ai: " + Name + " repaired - driving again" + (moved ? " (placed by the race)" : ""));
            RetiredAt = -1f; RetireFrozen = false; RetireSide = 0;
            if (State == AcAiState.Retired) State = AcAiState.Driving;
            if (ph.Body != null && !ph.Frozen) ChooseLine(ph.Body.position);
        }

        private void Hold(AcCarPhysics ph)
        {
            AcCarInput inp = AcCarInput.Neutral;
            inp.Brake = 1f; inp.Handbrake = 1f;
            inp.SteerAbsolute = true; inp.SteerWheelDeg = 0f;
            inp.ShiftUp = ph.Input.ShiftUp; inp.ShiftDown = ph.Input.ShiftDown; // never drop an edge P has not consumed yet
            ph.Input = inp;
        }

        private void Write(AcCarPhysics ph, ref AcAiControls c)
        {
            float ratio = EffectiveSteerRatio(ph);
            float lockDeg = ph.Spec != null && ph.Spec.SteerLockDeg > 1f ? ph.Spec.SteerLockDeg : (CarData != null ? CarData.SteerLockDeg : 360f);
            float wheel = Mathf.Clamp(c.RoadAngleDeg * ratio, -lockDeg, lockDeg);
            AcCarInput inp = AcCarInput.Neutral;
            inp.SteerAbsolute = true;
            inp.SteerWheelDeg = wheel;
            inp.Steer = lockDeg > 0f ? wheel / lockDeg : 0f;
            inp.Throttle = c.Throttle;
            inp.Brake = c.Brake * LockEase(ph, c.Brake);
            inp.Handbrake = c.Handbrake;
            inp.ShiftUp = c.ShiftUp || ph.Input.ShiftUp;
            inp.ShiftDown = c.ShiftDown || ph.Input.ShiftDown;
            ph.Input = inp;
        }

        /// <summary>
        /// Steering-wheel degrees per road-wheel degree exactly as P's physics applies it (AcCarPhysics.ControlsToCore: road = wheel /
        /// max(1, Spec.SteerRatio); AcCarSpec makes STEER_RATIO positive, the 16 negative cars too), so the road angle is the one asked.
        /// Polish pass 2026-11-01: this USED to be measured every frame as Telemetry.SteerWheelDeg / SteerRoadDeg with a 0.2 per-frame
        /// filter, but P refreshes Telemetry.SteerWheelDeg every FRAME (Update) and SteerRoadDeg only every 50 Hz STEP, so between two
        /// steps the "ratio" was (this frame's wheel) / (the last step's road) and the next wheel = wanted road x that ratio: a positive
        /// feedback loop that ran away within one step at ~40 frames per step (batch lab ~2000 fps; still present at 2-3 frames per step).
        /// Lab ai-magione 05:32: only 21 of 135 traced samples (|wheel| > 20) had wheel/road within 10..18 of STEER_RATIO 14 (38 under 10,
        /// 76 over 18; wheel 450 -> road 34.6 deg at 34 km/h): the road angle was noise, the car left the track 5 times, lap x1.74.
        /// </summary>
        private float EffectiveSteerRatio(AcCarPhysics ph)
        {
            if (SteerRatioEff <= 0f)
            {
                float r = ph.Spec != null ? Mathf.Abs(ph.Spec.SteerRatio) : 0f;
                if (r < 1f && CarData != null) r = Mathf.Abs(CarData.SteerRatio);
                SteerRatioEff = r >= 1f ? r : 14f; // ours fallback (AcCarSpec's own default)
            }
            return SteerRatioEff;
        }

        private static float DrivenSlip(ref AcTelemetry t, string traction)
        {
            float f = Math.Max(t.LF.SlipRatio, t.RF.SlipRatio), r = Math.Max(t.LR.SlipRatio, t.RR.SlipRatio);
            return traction == "FWD" ? f : traction == "AWD" ? Math.Max(f, r) : r;
        }

        private bool IsFinished()
        {
            if (AcSession.Phase < AcPhase.Racing) return false;
            try
            {
                var st = AcRace.Standings;
                int id = -(100 + Index);
                for (int i = 0; i < st.Count; i++) if (st[i] != null && st[i].IsAi && st[i].PlayerId == id) return st[i].Finished;
            }
            catch (Exception) { }
            return false;
        }

        private static readonly AcRules DefaultRules = new AcRules();
        private static AcRules SafeRules() { return AcSession.Rules ?? DefaultRules; }

        /// <summary>Wheel slip ratio under which a braking wheel counts as locking (P: negative = locking). // ours</summary>
        public const float LockSlip = 0.15f;

        /// <summary>
        /// Brake pedal factor for a car whose data has no (active) ABS (the AI runs the factory setting): a driver eases the pedal when a wheel
        /// locks - down 4/s while any wheel is past <see cref="LockSlip"/>, back up 2/s, never under 0.5 (// ours rates). 1 with ABS.
        /// </summary>
        private float LockEase(AcCarPhysics ph, float brake)
        {
            bool abs = false;
            try { abs = ph.Spec != null && ph.Spec.Abs != null && ph.Spec.Abs.Present && ph.Spec.Abs.Active; } catch (Exception) { }
            float dt = Time.deltaTime;
            if (abs || brake < 0.05f) { BrakeEase = Math.Min(1f, BrakeEase + 2f * dt); return abs ? 1f : BrakeEase; }
            AcTelemetry t = ph.Telemetry;
            float worst = Math.Min(Math.Min(t.LF.SlipRatio, t.RF.SlipRatio), Math.Min(t.LR.SlipRatio, t.RR.SlipRatio));
            if (worst < -LockSlip && ph.SpeedKmh > 10f) BrakeEase = Math.Max(0.5f, BrakeEase - 4f * dt);
            else BrakeEase = Math.Min(1f, BrakeEase + 2f * dt);
            return BrakeEase;
        }

        /// <summary>No AI pit stops: a car with less than one planned lap of fuel (+1 l) is topped up to its start load (logged, counted).</summary>
        private void CheckFuel(AcCarPhysics ph)
        {
            if (CarData == null || FastPlan == null || FuelLoad <= 0f) return;
            float litres = ph.Telemetry.FuelLitres, rate = ph.FuelRate;
            if (!AcAiPlan.NeedsTopUp(CarData, litres, FastPlan.FuelPerLap * rate)) return;
            try
            {
                if (!ph.ApplySetup("FUEL", FuelLoad)) return;
                FuelTopUps++;
                if (FuelTopUps <= 2) AcLog.Info("Ai: " + Name + " topped up to " + FuelLoad.ToString("0.0") + " l at " + litres.ToString("0.0") + " l left (AI pit stops are not done) x" + FuelTopUps);
            }
            catch (Exception e) { AcLog.WarnOnce("Ai: fuel top-up failed: " + e.Message); }
        }

        /// <summary>Pit exit: wait at the waiting point (AcAi.PitMergeS) while fast-lane traffic would reach the merge within 2 s (// ours); after
        /// 10 s of waiting go anyway (// ours). Returns the speed cap (m/s) for this frame.</summary>
        private float MergeCap(float dt)
        {
            float wait = AcAi.PitMergeS;
            if (wait < 0f || PitTrack == null) return float.PositiveInfinity;
            float before = wait - Brain.S;
            if (before < -1f || before > 80f) { if (before < -1f) MergeWait = 0f; return float.PositiveInfinity; } // committed / far // ours: 80 m
            if (MergeWait > 10f || AcAiTraffic.MergeClear(Rig, AcAi.PitMergeFastS, 2f)) return float.PositiveInfinity;
            MergeWait += dt;
            float free = Math.Max(0f, before - 2f); // ours: stop 2 m short
            return (float)Math.Sqrt(2f * 4f * free); // ours: 4 m/s^2
        }

        // ------------------------------------------------------------------ pit lane

        /// <summary>Picks the pit line when the car stands on / beside it (a pit box, the pit lane), else the fast lane; resets the controller.</summary>
        internal void ChooseLine(Vector3 pos)
        {
            if (FastTrack == null || FastPlan == null) return;
            float ax, ay, az;
            AcTrackWorld.ToAc(pos, out ax, out ay, out az);
            int hf = -1; float latF;
            FastTrack.Project(ax, ay, az, ref hf, out latF);
            bool pit = false;
            if (PitTrack != null && PitPlan != null)
            {
                int hp = -1; float latP;
                float sp = PitTrack.Project(ax, ay, az, ref hp, out latP);
                pit = Math.Abs(latP) < 12f && Math.Abs(latP) + 2f < Math.Abs(latF) && sp > PitTrack.Length * 0.02f && sp < (AcAi.PitExitS > 0f ? AcAi.PitExitS : PitTrack.Length - 10f); // ours
            }
            Brain.ResetState();
            if (pit) Brain.SetLine(PitTrack, PitPlan); else Brain.SetLine(FastTrack, FastPlan);
            OnPitLine = pit;
        }

        /// <summary>Time to join the fast lane: at the pit exit (AcAi.PitExitS, where pit_lane.ai rejoins the track - the line goes on along the
        /// track after it), else 6 m before the pit line's end (// ours).</summary>
        private bool PitLineDone(float s)
        {
            if (PitTrack == null) return true;
            float exit = AcAi.PitExitS;
            return (exit > 0f && s >= exit) || PitTrack.Length - s < 6f;
        }

        internal void LeavePitLine()
        {
            OnPitLine = false;
            MergeWait = 0f;
            if (FastTrack != null && FastPlan != null) Brain.SetLine(FastTrack, FastPlan);
        }

        // ------------------------------------------------------------------ recovery

        private bool Recover(AcCarPhysics ph, float dt, ref AcAiSense e, Vector3 pos)
        {
            float av = Math.Abs(e.Speed);
            bool wants = Brain.TargetSpeed > 3f || Blocked; // ours; Blocked: held by a standing car ahead (the grid) - back off and go round
            // stuck: wants to go, does not move
            if (wants && av < 1f) StuckTime += dt; // ours: 3.6 km/h
            else if (av > 3f) { StuckTime = 0f; if (!Brain.Reverse) ReverseTime = 0f; }
            if (Brain.Reverse)
            {
                ReverseTime += dt;
                if (ReverseTime > ReverseFor || (ReverseTime > 0.8f && Math.Abs(Brain.HeadingError) < 0.6f && av > 1.5f)) { Brain.Reverse = false; Brain.ReverseNose = 0; StuckTime = ReverseAfter * 0.5f; }
            }
            else if (StuckTime > ReverseAfter && StuckTime < ResetAfter && ReverseTime == 0f)
            {
                // blocked by a standing car: back off with the nose swinging to the pass side, unless a car stands right behind (then
                // the reset at ResetAfter puts it back on the line)
                bool behindFree = true;
                if (Blocked)
                {
                    Vector3 back = pos - Rig.Root.transform.forward * (HalfLength + 3f); // ours: 3 m behind the tail
                    behindFree = !AcAiTraffic.Occupied(back, Rig, 2.5f); // ours: 2.5 m
                }
                if (behindFree)
                {
                    Brain.Reverse = true; ReverseTime = 0f;
                    Brain.ReverseNose = Blocked ? PassSide : 0;
                }
            }
            // far off the line / pointing backwards
            float side = Brain.Lateral > 0f ? SideAt(true) : SideAt(false);
            if (Math.Abs(Brain.Lateral) > side + FarOffMetres) OffTime += dt; else OffTime = 0f;
            if (Math.Abs(Brain.HeadingError) > 2.0f && av > 2f && !Brain.Reverse) WrongTime += dt; else if (Math.Abs(Brain.HeadingError) < 1.5f) WrongTime = 0f; // ours: 115 deg
            float lineY = Brain.Track != null ? Brain.Track.HeightAt(Brain.S) : e.Y;
            bool fell = e.Y < lineY - 15f; // ours
            string why = null;
            if (StuckTime >= ResetAfter) why = "stuck";
            else if (OffTime >= FarOffSeconds) why = "off the track";
            else if (WrongTime >= WrongWaySeconds) why = "wrong way";
            else if (fell) why = "fell";
            if (why == null) return false;
            ResetToLine(ph, why);
            return true;
        }

        private float SideAt(bool left)
        {
            var tr = Brain.Track;
            if (tr == null || tr.IsEmpty) return 5f;
            int i = tr.IndexAt(Brain.S);
            return left ? tr.SideL[i] : tr.SideR[i];
        }

        /// <summary>
        /// Puts the car back on its line, stopped, pointing along it, free of other cars and of fast-lane traffic arriving within 2.5 s (// ours).
        /// In a race it never gains: places at or behind where it was (Brain.S - 2 m, then 15 m steps back // ours); in practice a little ahead
        /// (10 m, then 15 m steps // ours). R's timer is told (AcTiming.Teleported) so no gate is tested across the jump.
        /// </summary>
        internal void ResetToLine(AcCarPhysics ph, string why)
        {
            var tr = Brain.Track;
            if (tr == null || tr.IsEmpty) return;
            bool race = AcSession.Phase == AcPhase.Racing || AcSession.Phase == AcPhase.Finished;
            float step = race ? -15f : 15f, s0 = race ? Brain.S - 2f : Brain.S + 10f;
            if (!tr.IsClosed) s0 = tr.WrapS(s0);
            Vector3 p = Vector3.zero, first = Vector3.zero; float yaw = 0f, firstYaw = 0f;
            bool found = false, roomy = false;
            float s = s0;
            for (int k = 0; k < 10; k++, s += step) // ours: 10 places
            {
                float sw = tr.WrapS(s);
                LinePose(tr, sw, out p, out yaw);
                if (AcAiTraffic.Occupied(p, Rig, 7f)) continue; // ours: 7 m clear
                if (!roomy) { roomy = true; first = p; firstYaw = yaw; }
                if (tr != FastTrack || AcAiTraffic.MergeClear(Rig, sw, 2.5f)) { found = true; break; }
            }
            if (!found)
            {
                if (roomy) { p = first; yaw = firstYaw; }
                else LinePose(tr, tr.WrapS(s0), out p, out yaw);
            }
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            try { ph.ResetTo(p, rot); }
            catch (Exception ex) { AcLog.Warn("Ai: ResetTo failed: " + ex.Message); Rig.Root.transform.SetPositionAndRotation(p, rot); }
            Brain.ResetState();
            StuckTime = 0f; ReverseTime = 0f; OffTime = 0f; WrongTime = 0f;
            Recoveries++;
            try { LastPlacements = ph.Placements; } catch (Exception) { }
            // R's lap timer: a jump, not a drive (no finish / sector crossing across it)
            try { AcTiming.Teleported(AcTiming.AiTiming(Index, false), false); }
            catch (Exception ex) { AcLog.WarnOnce("Ai: telling R's timer about a recovery failed: " + ex.Message); }
            AcAi.NoteRecovery(this, why);
        }

        /// <summary>A Unity pose on a line at arc length s: point on the line, yaw along it.</summary>
        internal static void LinePose(AcAiTrack tr, float s, out Vector3 p, out float yawDeg)
        {
            float x, z, tx, tz, k, sl, sr;
            tr.Sample(s, out x, out z, out tx, out tz, out k, out sl, out sr);
            float y = tr.HeightAt(s);
            p = AcTrackWorld.ToUnity(x, y, z);
            // AC tangent (tx, tz) -> Unity direction (-tx, tz)
            yawDeg = Mathf.Atan2(-tx, tz) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ no physics: on rails

        /// <summary>Without P's Rigidbody the car still runs: moved along the line at the planned speed (traffic respected), on the ground.</summary>
        private void Rails()
        {
            if (State != AcAiState.Rails) { State = AcAiState.Rails; AcLog.InfoOnce("Ai: no physics body on the AI cars - driving them on rails along the line (P's AcCarPhysics missing)"); }
            AcPhase phase = AcSession.Phase;
            bool hold = phase < AcPhase.Practice || phase == AcPhase.Grid || phase == AcPhase.Countdown;
            if (Rig.Physics != null && Rig.Physics.Frozen) hold = true;
            var tr = Brain.Track;
            if (tr == null || tr.IsEmpty) return;
            Transform root = Rig.Root.transform;
            float dt = Time.deltaTime;
            if (RailsPos == Vector3.zero)
            {
                float ax, ay, az; AcTrackWorld.ToAc(root.position, out ax, out ay, out az);
                float lat; Brain.S = tr.Project(ax, ay, az, ref Brain.Hint, out lat);
                RailsPos = root.position;
            }
            float want = hold ? 0f : Brain.Plan.SpeedAt(Brain.S + 5f) * (IsFinished() ? 0.72f : 1f);
            if (OnPitLine) want = Math.Min(want, SafeRules().PitLimitKmh / 3.6f);
            AcAiTraffic.Decide(this, root.position, root.forward * RailsSpeed);
            if (Brain.FollowCap < want) want = Brain.FollowCap;
            float acc = want > RailsSpeed ? 6f : 12f; // ours: rails accel / brake m/s^2
            RailsSpeed = Mathf.MoveTowards(RailsSpeed, want, acc * dt);
            Brain.S = tr.WrapS(Brain.S + RailsSpeed * dt);
            if (OnPitLine && !tr.IsClosed && PitLineDone(Brain.S)) { LeavePitLine(); float ax, ay, az; AcTrackWorld.ToAc(root.position, out ax, out ay, out az); float l2; Brain.S = Brain.Track.Project(ax, ay, az, ref Brain.Hint, out l2); return; }
            Brain.Offset = Mathf.MoveTowards(Brain.Offset, Brain.OffsetTarget, AcAiBrain.OffsetRate * dt);
            Vector3 p; float yaw;
            LinePose(tr, Brain.S, out p, out yaw);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            p += rot * Vector3.left * Brain.Offset; // offset + = left
            Vector3 g, n;
            if (AcTrackWorld.TryGround(p + Vector3.up * 2f, out g, out n)) p = g + Vector3.up * 0.5f; // ours: rails ride height
            root.SetPositionAndRotation(p, rot);
            LineS = Brain.S; Lateral = Brain.Offset; TargetKmh = want * 3.6f;
        }
    }
}
