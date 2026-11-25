using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] The own record (part of <see cref="AcNetGame"/>): composed once a frame from AcLocal (every owner's fields), the local car's
    /// Rigidbody pose with a fixed-time stamp (ETS PackOwnMotion: body.position / rotation / linearVelocity and Time.fixedTimeAsDouble, so pose and
    /// clock agree), P's telemetry, S's seat state, R's timing, and the rider record (where my body sits in which car, in that car's frame as I
    /// draw it). The session writes it at most 30 Hz while the car moves, 10 Hz otherwise, only the ints that changed.
    /// </summary>
    public static partial class AcNetGame
    {
        private static AcCarRig _ownRig;
        private static Vector3 _ownLastPos;
        private static float _ownLastAt;
        private static int _ownEpoch, _ownPlacements = -1;
        private static bool _placementNoted;
        private static int _layoutHash;
        private static AcLayoutEntry _layoutHashFor;
        private static readonly int[] LastSectors = new int[3];
        private static int _riderEpoch;
        private static int _riderTarget, _riderSeat = -1;
        private static AcSurfaces _surfaceIndexOf;
        private static readonly Dictionary<AcSurface, int> SurfaceIndex = new Dictionary<AcSurface, int>(32);
        private static int _appliedSeq;

        /// <summary>A jump this far beyond what the velocity explains between two frames is a teleport (placement epoch + 1). // ours</summary>
        private const float TeleportMetres = 3f;

        /// <summary>
        /// The local car was teleported (spawn, back to pits, grid, reset): the next record carries a new placement epoch, so every other client snaps
        /// my car instead of sliding it. R / P may call it; a jump is also detected on its own (and a new rig always counts).
        /// </summary>
        public static void NotePlacement() { _placementNoted = true; }

        /// <summary>The NameHash of the current layout key (0 in the hub).</summary>
        public static int LayoutHash
        {
            get
            {
                AcLayoutEntry l = AcSession.Layout;
                if (!ReferenceEquals(l, _layoutHashFor))
                {
                    _layoutHashFor = l;
                    _layoutHash = l != null ? AcNetWire.NameHash(l.Key) : 0;
                }

                return AcSession.OnTrack || AcSession.Phase == AcPhase.Loading ? _layoutHash : 0;
            }
        }

        private static void ComposeOwn()
        {
            AcNet net = Net;
            AcNetState s = net.Own;
            bool onTrack = AcSession.OnTrack;
            s.Status = onTrack ? AcNetStatus.Playing : AcNetStatus.Present;
            s.Vr = XrSafe();
            s.OnTrack = onTrack;
            s.Seated = AcLocal.Seated;
            s.Driving = AcLocal.Seated && AcLocal.Driving;
            s.Passenger = AcLocal.Seated && !AcLocal.Driving;
            s.Ready = Ready || AcLocal.Ready;      // F writes AcLocal.Ready (REQUESTS F -> N)
            s.Spectating = Spectating;
            s.AppliedSeq = Math.Max(_appliedSeq, AcLocal.CommandSeq);      // R's own "applied" number when it has one
            int[] rs = AcLocal.LastSectorMs;                                 // R's reused array (one per gate); else the LapCompleted copy
            bool fromR = rs != null && rs.Length > 0;
            s.Sector0Ms = fromR ? rs[0] : LastSectors[0];
            s.Sector1Ms = fromR ? (rs.Length > 1 ? rs[1] : 0) : LastSectors[1];
            s.Sector2Ms = fromR ? (rs.Length > 2 ? rs[2] : 0) : LastSectors[2];

            AcCarRig rig = AcLocal.Car;
            bool haveCar = onTrack && rig != null && rig.Root != null;
            AcNetCar car = s.Car;
            int stamp = -1;
            if (haveCar)
            {
                // P's / C's members inside one guard: a throw there drops the car from this frame's record (logged), never the record itself
                try
                {
                    if (!ReferenceEquals(rig, _ownRig)) { _ownRig = rig; _placementNoted = true; }
                    int placements = rig.Physics != null ? rig.Physics.Placements : 0;     // P counts every ResetTo (spawn, pits, grid, reset)
                    if (placements != _ownPlacements) { _ownPlacements = placements; _placementNoted = true; }
                    FillCar(rig, ref car, out stamp);
                }
                catch (Exception e)
                {
                    haveCar = false;
                    AcLog.WarnOnce("Net: reading the own car for the record failed (later ones not logged): " + e);
                }
            }

            if (haveCar)
            {
                car.LayoutHash = LayoutHash;
                if (string.IsNullOrEmpty(rig.SkinId) && !string.IsNullOrEmpty(AcLocal.SkinId)) car.SkinHash = AcNetWire.NameHash(AcLocal.SkinId);
                // placement epoch: a new rig, a noted teleport or a jump the velocity does not explain
                Vector3 pos = V(car.Pos);
                float now = Time.unscaledTime;
                float dt = Mathf.Max(now - _ownLastAt, 0.001f);
                float explained = V(car.Vel).magnitude * dt * 1.5f + TeleportMetres;
                if (_placementNoted || (_ownLastAt > 0f && (pos - _ownLastPos).magnitude > explained))
                {
                    _ownEpoch = (_ownEpoch + 1) & 0xFF;
                    _placementNoted = false;
                }

                _ownLastPos = pos;
                _ownLastAt = now;
                car.Epoch = _ownEpoch;
                car.Laps = AcLocal.Laps;
                car.Sector = AcLocal.Sector;
                car.LapMs = AcLocal.LapMs;
                car.LastMs = AcLocal.LastMs;
                car.BestMs = AcLocal.BestMs;
                car.RaceDistance = AcLocal.RaceDistance;
                car.LapValid = AcLocal.LapValid;
                car.InPitlane = AcLocal.InPitlane;
                car.Finished = AcLocal.Finished;
                car.Position = AcLocal.Position;
                car.Cuts = AcLocal.LapCuts;
                LocalStanding(ref car);
                // R's own numbers when it has them: total = race time + penalties (R reads AcNetPlayer.FinishedMs as the total)
                if (AcLocal.Finished && AcLocal.FinishedMs > 0) car.FinishMs = AcLocal.FinishedMs + Math.Max(0, AcLocal.PenaltyMs);
                if (AcLocal.PenaltyMs > 0) car.PenaltyMs = AcLocal.PenaltyMs;
                s.Car = car;
                net.OwnStampMs = stamp;
                bool noSeat = true;
                try { noSeat = rig.Model == null || !rig.Model.HasPassengerSeat; }
                catch (Exception e) { AcLog.WarnOnce("Net: AcCarModel.HasPassengerSeat threw: " + e.Message); }
                s.NoPassengerSeat = noSeat;
            }
            else
            {
                if (s.Car.Present) { s.Car = AcNetCar.Empty(); s.Car.Epoch = _ownEpoch; }
                s.Car.LayoutHash = LayoutHash;
                s.Car.Present = false;
                s.NoPassengerSeat = true;
                net.OwnStampMs = -1;
                _ownRig = null;
            }

            s.RefuseRiders = false;   // // ours: a passenger may ride along in every phase (AC itself has no passengers; nothing to copy)
            ComposeRider(ref s);
            net.Own = s;
        }

        /// <summary>The finish time and penalty of the local player from R's standings (AcLocal has no such fields).</summary>
        private static void LocalStanding(ref AcNetCar car)
        {
            car.FinishMs = 0;
            car.PenaltyMs = 0;
            IReadOnlyList<AcStanding> st = StandingsSafe();
            if (st == null) return;
            for (int i = 0; i < st.Count; i++)
            {
                AcStanding x = st[i];
                if (x == null || !x.IsLocal) continue;
                car.FinishMs = x.Finished ? x.TotalMs : 0;
                car.PenaltyMs = x.PenaltyMs;
                break;
            }
        }

        /// <summary>A car (the own one or an AI car on the master) into its wire state: identity, the Rigidbody pose, P's telemetry. Returns the
        /// pose's physics stamp (ms).</summary>
        internal static void FillCar(AcCarRig rig, ref AcNetCar car, out int stampMs)
        {
            car.Present = true;
            car.CarHash = rig.Car != null ? AcNetWire.NameHash(rig.Car.Id) : 0;
            car.SkinHash = AcNetWire.NameHash(rig.SkinId);
            Transform root = rig.Root.transform;
            AcCarPhysics ph = rig.Physics;
            Rigidbody body = ph != null ? ph.Body : null;
            Vector3 pos, vel = Vector3.zero, angular = Vector3.zero;
            Quaternion rot;
            if (body != null)
            {
                pos = body.position;
                rot = body.rotation;
                if (!body.isKinematic)
                {
                    vel = body.linearVelocity;
                    angular = body.angularVelocity;
                }
            }
            else
            {
                pos = root.position;
                rot = root.rotation;
            }

            stampMs = (int)(Time.fixedTimeAsDouble * 1000.0);
            car.Pos = W(pos);
            car.Rot = W(rot);
            car.Vel = W(vel);
            car.YawRateDeg = angular.y * Mathf.Rad2Deg;
            AcTelemetry t = ph != null ? ph.Telemetry : default(AcTelemetry);
            car.SteerWheelDeg = t.SteerWheelDeg;
            car.SteerLF = t.LF.SteerDeg;
            car.SteerRF = t.RF.SteerDeg;
            car.Rpm = t.Rpm;
            car.Throttle = t.Throttle;
            car.Brake = t.Brake;
            car.Clutch = t.Clutch;
            car.Handbrake = ph != null ? ph.Input.Handbrake : 0f;
            car.BoostBar = t.BoostBar;
            car.Gear = t.Gear;
            car.Lights = (int)t.Lights;
            car.Limiter = t.Limiter;
            car.Abs = t.AbsActive;
            car.Tc = t.TcActive;
            car.Shifting = t.Shifting;
            car.Frozen = ph != null && ph.Frozen;
            AcCarModel model = rig.Model;
            car.DoorL = model != null ? model.DoorOpen(AcSide.Left) : 0f;      // C: the last SetDoor value (S animates the doors)
            car.DoorR = model != null ? model.DoorOpen(AcSide.Right) : 0f;
            for (int i = 0; i < 4; i++)
            {
                AcWheelState w = t.Wheel(i);
                car.SetWheel(i, new AcNetWheel { TravelM = w.TravelM, Skid = w.Skid, Surface = w.Contact ? IndexOfSurface(w.Surface) : -1, AngularVel = w.AngularVel });
            }

            // player records also carry the wheels' own angular velocity (lock-ups, wheelspin) and the fuel (the remote cockpit's gauge)
            car.HasWheelSpeed = ph != null;
            car.FuelLitres = t.FuelLitres;
            // iteration 2 (protocol 3, DAMAGE_DESIGN 7): the owner-authoritative damage pair (all zero at DAMAGE 0); AI cars go to "Ac Net Host 0"
            car.DamageD0 = 0; car.DamageD1 = 0;
            if (ph != null)
            {
                try { AcCarDamage dmg = ph.Damage; if (dmg != null) dmg.ToWire(out car.DamageD0, out car.DamageD1); }
                catch (Exception e) { AcLog.WarnOnce("Net: reading the car damage failed: " + e.Message); }
            }
        }

        private static bool XrSafe()
        {
            try { return AcPlayer.XrPresent; }
            catch (Exception e) { AcLog.WarnOnce("Net: AcPlayer.XrPresent threw (published as monitor): " + e.Message); return false; }
        }

        private static AcSurfaces SurfacesSafe()
        {
            try { return AcTrackWorld.Surfaces; }
            catch (Exception e) { AcLog.WarnOnce("Net: AcTrackWorld.Surfaces threw (wheel surfaces not sent): " + e.Message); return null; }
        }

        /// <summary>The surface's index in the loaded layout's surfaces list (the same file, so the same order on every client); -1 none.</summary>
        private static int IndexOfSurface(AcSurface s)
        {
            if (s == null) return -1;
            AcSurfaces all = SurfacesSafe();
            if (all == null) return -1;
            if (!ReferenceEquals(all, _surfaceIndexOf))
            {
                _surfaceIndexOf = all;
                SurfaceIndex.Clear();
                for (int i = 0; i < all.Surfaces.Count; i++) if (all.Surfaces[i] != null && !SurfaceIndex.ContainsKey(all.Surfaces[i])) SurfaceIndex.Add(all.Surfaces[i], i);
            }

            int k;
            return SurfaceIndex.TryGetValue(s, out k) ? k : -1;
        }

        /// <summary>The surface at a wire index on this client's loaded layout (null when out of range / none).</summary>
        internal static AcSurface SurfaceAt(int index)
        {
            AcSurfaces all = SurfacesSafe();
            if (all == null || index < 0 || index >= all.Surfaces.Count) return null;
            return all.Surfaces[index];
        }

        /// <summary>
        /// The rider record: the car my body sits in (my own, a remote player's, or an AI car) and the body root's pose in that car's root frame as
        /// THIS client draws it (EtsRiders.Publish). A pending seat request is written by the session (requesting bit, the owner's car).
        /// </summary>
        private static void ComposeRider(ref AcNetState s)
        {
            var r = default(AcNetRider);
            AcCarRig car = AcLocal.Seated ? AcLocal.SeatCar : null;
            Transform body = null;
            try { body = AcPlayer.Root; }
            catch (Exception) { body = null; }
            int target = car != null ? TargetOf(car) : 0;
            if (target != 0 && car.Root != null)
            {
                r.Target = target;
                r.Seat = (int)AcLocal.Seat;
                r.Xr = s.Vr;
                if (AcLocal.SeatRootLocal.sqrMagnitude > 1e-6f)
                {
                    // S's own number: the seated body root in the car's Rigidbody frame as S placed it (AcLocal.SeatRootLocal / SeatYawLocal)
                    r.Offset = W(AcLocal.SeatRootLocal);
                    r.YawLocal = AcLocal.SeatYawLocal;
                }
                else if (body != null)
                {
                    Transform frame = car.Root.transform;
                    r.Offset = W(frame.InverseTransformPoint(body.position));
                    r.YawLocal = Mathf.DeltaAngle(YawOf(frame.rotation), YawOf(body.rotation));
                }
            }

            if (r.Target != _riderTarget || r.Seat != _riderSeat)
            {
                _riderTarget = r.Target;
                _riderSeat = r.Seat;
                _riderEpoch = (_riderEpoch + 1) & 0xF;
            }

            r.Epoch = _riderEpoch;
            s.Rider = r;
        }

        /// <summary>The rider / grid code of a car drawn here: mine, a remote player's, or an AI car (master: its own AI rigs; others: remote AI cars).</summary>
        internal static int TargetOf(AcCarRig car)
        {
            if (car == null || !Up) return 0;
            int me = Net.Online ? Net.LocalId : 0;
            if (ReferenceEquals(car, AcLocal.Car)) return AcNetWire.TargetPlayer(me);
            for (int i = 0; i < CarList.Count; i++)
            {
                AcRemoteCar rc = CarList[i];
                if (!ReferenceEquals(rc.Rig, car) || rc.Player == null) continue;
                return rc.Player.IsAi ? AcNetWire.TargetAi(rc.Player.AiIndex) : AcNetWire.TargetPlayer(rc.Player.PhotonId);
            }

            IReadOnlyList<AcAiDriver> ai = AiDriversSafe();
            if (ai != null)
                for (int i = 0; i < ai.Count; i++)
                    if (ai[i] != null && ReferenceEquals(ai[i].Rig, car)) return AcNetWire.TargetAi(ai[i].Index);
            return 0;
        }

        /// <summary>R's lap event: keep my last lap's sector times for the record.</summary>
        private static void OnLap(AcLap lap)
        {
            if (lap == null || !lap.IsLocal) return;
            for (int i = 0; i < 3; i++) LastSectors[i] = lap.SectorMs != null && i < lap.SectorMs.Length ? lap.SectorMs[i] : 0;
        }

        /// <summary>A command became current here (applied by R through Changed): published back as "applied Seq".</summary>
        internal static void NoteApplied(AcRaceCommand c, bool own)
        {
            if (c == null) return;
            _appliedSeq = c.Seq;
            AcLog.Info("Net: race command seq " + c.Seq + " applied (" + c.Phase + " " + (string.IsNullOrEmpty(c.TrackId) ? "-" : c.TrackId + (string.IsNullOrEmpty(c.LayoutId) ? "" : "/" + c.LayoutId))
                       + (own ? ", published by me" : ", from " + NameOf(c.MasterId)) + ")");
        }

        // ------------------------------------------------------------------ conversions (Unity <-> wire)

        internal static AcNetV3 W(Vector3 v) { return new AcNetV3(v.x, v.y, v.z); }
        internal static AcNetQ W(Quaternion q) { return new AcNetQ(q.x, q.y, q.z, q.w); }
        internal static Vector3 V(AcNetV3 v) { return new Vector3(v.X, v.Y, v.Z); }
        internal static Quaternion Q(AcNetQ q) { return new Quaternion(q.X, q.Y, q.Z, q.W); }

        internal static float YawOf(Quaternion q)
        {
            Vector3 f = q * Vector3.forward;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}
