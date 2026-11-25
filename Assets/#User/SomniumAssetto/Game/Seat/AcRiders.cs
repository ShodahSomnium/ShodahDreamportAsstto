using System;
using System.Collections.Generic;
using Fusion;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Riders: every seated remote player's body put on THIS client's drawn copy of the car they sit in, and the passenger-seat
    /// request flow - the Euro Truck Sim Dreamport's EtsRiders + EtsRiderMath (the Main Build's elevator method for vehicles), for AC cars.
    ///
    /// 1. Remote bodies (drivers in their own remote car, passengers in a remote car or in MY car): at 300 km/h the avatar's own network pose
    ///    and N's interpolated car disagree by metres, so each seated remote body is moved rigidly onto its seat on the car as drawn here:
    ///    <c>C(p, q) = (target + R(dYaw)(p - rawRoot), R(dYaw) q)</c> (ETS EtsRiderMath.Correct), written to BOTH the client's Fusion transforms
    ///    (Body/Root, Head, LeftHand, RightHand under "Body", state authority = the player; found by a scene search once a second - never
    ///    <c>GetAllBehaviours&lt;NetworkTransform&gt;</c>, unproven on the server) AND the Bridge transforms they drive (MobilePlatformMover: writing one
    ///    jitters). Early (order -90, before the client places desktop avatars at order 0) and late (order 9000, after Fusion's Render, before
    ///    VRIK at 9998). The target is the body root offset N decodes from the player's record (<see cref="SetRemoteSeatOffset"/>: the root in the
    ///    car's frame as THEIR client placed it = AcLocal.SeatRootLocal), else the seat's hip point with the car's heading. Remote VRIK leg
    ///    stepping and falling animation are off while seated (local switches for a remote player).
    /// 2. Passenger seat: <see cref="AskForRide"/> asks the car's owner through N's <see cref="Asker"/> (ETS RequestSeat: granted within 0.25 s,
    ///    refused, or no answer); the owner's N asks <see cref="CanGrantPassenger"/>. Passenger exit is refused above 10 km/h (AcSeat).
    /// Data from N only through its public face (AcNetGame.Players / RemoteCars / FindByPhotonId) and the two hooks above (no compile-time
    /// dependency on N's internals). Allocation-free per frame once riders are bound.
    /// </summary>
    public static class AcRiders
    {
        /// <summary>N's seat request: (owner photon id, seat, granted, refused(reason)). Set by N at its Start; null = no net layer.</summary>
        public delegate void SeatAsk(int ownerPhotonId, AcSeatKind seat, Action granted, Action<string> refused);

        /// <summary>Set by N: sends a seat request to a car owner and calls back once (grant / refusal / timeout).</summary>
        public static SeatAsk Asker;

        /// <summary>Lab / offline: grant ride requests at once when there is no <see cref="Asker"/>.</summary>
        public static bool GrantLocally;

        private const float AskTimeout = 5f; // ours (ETS: the owner answers within 3 s or the request is "not answered")
        private const int BindFastTries = 5, BindMaxTries = 15; // ours

        private sealed class Remote
        {
            public int PlayerId;
            public ISomniumPlayer Player;
            public int Seen;
            public AcCarRig Car;
            public AcSeatKind Seat;
            public Vector3 Target;
            public float TargetYaw;
            public Transform NtRoot, NtHead, NtLeft, NtRight;
            public Pose RawRoot, RawHead, RawLeft, RawRight;
            public bool HaveRaw, BindReported, LegsOff, FallingOff, GaveUp;
            public int BindTries;
            public float NextBind, WorstBefore, WorstAfter;
            public AcSeatGeometry Geo;
        }

        private struct Offset { public Vector3 Root; public float Yaw; public float At; }

        private static readonly Dictionary<int, Remote> Remotes = new Dictionary<int, Remote>();
        private static readonly Dictionary<int, Offset> Offsets = new Dictionary<int, Offset>();
        private static readonly List<int> Stale = new List<int>();
        private static int _pass;
        private static NetworkBehaviour[] _found;
        private static float _nextSearch, _nextReport;

        private static int _askToken;
        private static AcCarRig _askCar;
        private static float _askUntil;

        // ================================================================ the passenger request

        /// <summary>Asks the remote car's owner for its passenger seat; the seat is taken when granted. True = asked (pending).</summary>
        public static bool AskForRide(AcCarRig car)
        {
            if (car == null || car.Root == null) return AcSeat.Refuse("there is no car to get into", 2f);
            if (!PassengerSeatFree(car)) return AcSeat.Refuse("somebody already rides in that car", 3f);
            int owner = car.OwnerId;
            string name = OwnerName(owner);
            // lab / offline flag first: N installs its Asker even when offline, and that Asker refuses a car with no owner record
            if (GrantLocally) { AcLog.Info("Seat: ride in " + name + "'s car granted locally (lab flag, no owner asked)."); return AcSeat.EnterGranted(car); }
            if (Asker == null) return AcSeat.Refuse("riding along needs the network (not connected)", 3f);

            int token = ++_askToken;
            _askCar = car;
            _askUntil = Time.unscaledTime + AskTimeout;
            AcSeat.SetPending(true, AcSeatKind.Passenger);
            AcLog.Info("Seat: asking " + name + " for the passenger seat.");
            AcEvents.RaiseMessage("Asking " + name + " for the passenger seat...", 3f);
            try
            {
                Asker(owner, AcSeatKind.Passenger, new Granted(token, car).Call, new Refused(token).Call);
            }
            catch (Exception e)
            {
                AcSeat.SetPending(false, AcSeatKind.Passenger);
                _askCar = null;
                AcLog.Error("Seat: N's seat request threw: " + e);
                return false;
            }

            return true;
        }

        private sealed class Granted
        {
            private readonly int _token;
            private readonly AcCarRig _car;
            public Granted(int token, AcCarRig car) { _token = token; _car = car; }
            public void Call()
            {
                if (_token != _askToken) return;
                _askCar = null;
                AcSeat.SetPending(false, AcSeatKind.Passenger);
                if (_car == null || _car.Root == null) { AcLog.Info("Seat: the ride was granted but the car is gone."); return; }
                AcLog.Info("Seat: ride granted by " + OwnerName(_car.OwnerId) + ".");
                AcSeat.EnterGranted(_car);
            }
        }

        private sealed class Refused
        {
            private readonly int _token;
            public Refused(int token) { _token = token; }
            public void Call(string reason)
            {
                if (_token != _askToken) return;
                _askCar = null;
                AcSeat.SetPending(false, AcSeatKind.Passenger);
                AcSeat.Refuse(string.IsNullOrEmpty(reason) ? "the ride was refused" : reason, 3f);
            }
        }

        /// <summary>Forgets an open request (getting in elsewhere, unload).</summary>
        internal static void CancelAsk()
        {
            _askToken++;
            _askCar = null;
        }

        /// <summary>Owner side (N calls it when a request for MY car's passenger seat arrives): may I grant it?</summary>
        public static bool CanGrantPassenger(int requesterPhotonId, out string reason)
        {
            reason = null;
            AcCarRig own = AcLocal.Car;
            if (own == null || own.Root == null) { reason = "they have no car out"; return false; }
            AcSeatGeometry g = AcSeatGeometry.Make(own);
            if (own.Model != null && own.Model.Root != null && own.Model.Bounds.size.x > 0.5f && !g.HasPassengerSeat) { reason = "their car has no passenger seat"; return false; }
            if (g.SpeedKmh > AcSeat.PassengerExitKmh) { reason = "their car is moving"; return false; }
            if (AcSeat.Seated && ReferenceEquals(AcSeat.SeatedCar, own) && AcSeat.Kind == AcSeatKind.Passenger) { reason = "the passenger seat is taken"; return false; }
            IReadOnlyList<AcNetPlayer> players = Players();
            for (int i = 0; players != null && i < players.Count; i++)
            {
                AcNetPlayer p = players[i];
                if (p != null && p.Seated && p.RideInPhotonId == AcLocal.PhotonId && p.PhotonId != requesterPhotonId) { reason = "the passenger seat is taken"; return false; }
            }

            return true;
        }

        // ---- other modules' public faces, guarded (per-frame callers: a throwing member costs one log line and a 5 s pause, never the
        // seat's frame; no exception storm every frame)
        private const float RetryAfterFailure = 5f; // ours
        private static float _playersRetry, _findRetry, _remotesRetry;

        /// <summary>AcNetGame.Players, or null when N is missing / throws (logged once, retried after 5 s).</summary>
        internal static IReadOnlyList<AcNetPlayer> Players()
        {
            if (Time.unscaledTime < _playersRetry) return null;
            try { return AcNetGame.Players; }
            catch (Exception e) { _playersRetry = Time.unscaledTime + RetryAfterFailure; AcLog.WarnOnce("Seat: reading AcNetGame.Players failed (" + e.GetType().Name + ": " + e.Message + "); riders / passenger seats pause."); return null; }
        }

        /// <summary>AcNetGame.FindByPhotonId, or null when N is missing / throws (logged once, retried after 5 s).</summary>
        internal static AcNetPlayer Find(int photonId)
        {
            if (Time.unscaledTime < _findRetry) return null;
            try { return AcNetGame.FindByPhotonId(photonId); }
            catch (Exception e) { _findRetry = Time.unscaledTime + RetryAfterFailure; AcLog.WarnOnce("Seat: AcNetGame.FindByPhotonId failed (" + e.GetType().Name + ": " + e.Message + "); remote names / ride owners pause."); return null; }
        }

        private static IReadOnlyList<AcRemotes.Remote> RemotesAll()
        {
            if (Time.unscaledTime < _remotesRetry) return null;
            try { return AcRemotes.All; }
            catch (Exception e) { _remotesRetry = Time.unscaledTime + RetryAfterFailure; AcLog.WarnOnce("Seat: reading AcRemotes.All failed (" + e.GetType().Name + ": " + e.Message + ")."); return null; }
        }

        /// <summary>A remote car's passenger seat is free (nobody's record rides in it, and not me).</summary>
        public static bool PassengerSeatFree(AcCarRig car)
        {
            if (car == null) return false;
            if (AcSeat.Seated && ReferenceEquals(AcSeat.SeatedCar, car) && AcSeat.Kind == AcSeatKind.Passenger) return false;
            if (car.Model != null && car.Model.Root != null && car.Model.Bounds.size.x > 0.5f && !car.Model.HasPassengerSeat) return false;
            IReadOnlyList<AcNetPlayer> players = Players();
            for (int i = 0; players != null && i < players.Count; i++)
            {
                AcNetPlayer p = players[i];
                if (p != null && p.Seated && p.RideInPhotonId >= 0 && p.RideInPhotonId == car.OwnerId) return false;
            }

            return true;
        }

        /// <summary>The local passenger left a remote car (N stops publishing the ride from AcLocal).</summary>
        internal static void LeftRide() { CancelAsk(); }

        /// <summary>N: a remote player's seated body root in the car's frame (their AcLocal.SeatRootLocal / SeatYawLocal from the record).</summary>
        public static void SetRemoteSeatOffset(int photonId, Vector3 rootLocal, float yawLocal)
        {
            Offsets[photonId] = new Offset { Root = rootLocal, Yaw = yawLocal, At = Time.unscaledTime };
        }

        private static string OwnerName(int photonId)
        {
            AcNetPlayer p = Find(photonId);
            return p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "the owner";
        }

        // ================================================================ remote bodies

        /// <summary>Early pass (order -90): who rides what, targets, the correction before the client's order-0 placement.</summary>
        internal static void Early()
        {
            if (_askCar != null && Time.unscaledTime > _askUntil)
            {
                CancelAsk();
                AcSeat.SetPending(false, AcSeatKind.Passenger);
                AcSeat.Refuse("the owner did not answer", 3f);
            }

            _pass++;
            IReadOnlyList<AcNetPlayer> players = null;
            players = Players();
            for (int i = 0; players != null && i < players.Count; i++)
            {
                AcNetPlayer p = players[i];
                if (p == null || !p.Seated || p.PhotonId < 0) continue;
                AcSeatKind seat;
                AcCarRig car = CarOf(p, out seat);
                if (car == null || car.Root == null) continue;
                Remote r;
                if (!Remotes.TryGetValue(p.PhotonId, out r))
                {
                    r = new Remote { PlayerId = p.PhotonId };
                    Remotes.Add(p.PhotonId, r);
                }

                ISomniumPlayer player = PlayerOf(p.PhotonId);
                if (player == null) continue;
                if (!ReferenceEquals(r.Player, player))
                {
                    r.NtRoot = r.NtHead = r.NtLeft = r.NtRight = null;
                    r.HaveRaw = false;
                    r.NextBind = 0f;
                    r.BindTries = 0;
                    r.GaveUp = false;
                    r.LegsOff = r.FallingOff = false;
                }

                if (!ReferenceEquals(r.Car, car)) r.Geo = AcSeatGeometry.Make(car);
                r.Player = player;
                r.Car = car;
                r.Seat = seat;
                r.Seen = _pass;
                Target(r);
                Bind(r);
                if (!r.HaveRaw) CacheRaw(r);
                r.WorstBefore = Mathf.Max(r.WorstBefore, Vector3.Distance(r.RawRoot.position, r.Target));
                Apply(r);
                Limbs(r);
            }

            Stale.Clear();
            foreach (KeyValuePair<int, Remote> kv in Remotes) if (kv.Value.Seen != _pass) Stale.Add(kv.Key);
            for (int i = 0; i < Stale.Count; i++)
            {
                Restore(Remotes[Stale[i]]);
                Remotes.Remove(Stale[i]);
            }

            if (Remotes.Count > 0 && Time.unscaledTime >= _nextReport) Report();
        }

        /// <summary>Late pass (order 9000): the car as it stands now and the poses Fusion just rendered.</summary>
        internal static void Late()
        {
            foreach (KeyValuePair<int, Remote> kv in Remotes)
            {
                Remote r = kv.Value;
                if (r.Seen != _pass || r.Player == null || r.Car == null || r.Car.Root == null) continue;
                Transform bodyRoot = BodyRoot(r.Player);
                if (bodyRoot != null) r.WorstAfter = Mathf.Max(r.WorstAfter, Vector3.Distance(bodyRoot.position, r.Target));
                Target(r);
                CacheRaw(r);
                Apply(r);
            }
        }

        /// <summary>The seated remote players drawn here (AcSeatPoses poses desktop drivers' hands on the wheel).</summary>
        internal static int Count { get { return Remotes.Count; } }

        /// <summary>Remote rider i's player, car, seat (enumeration for the poser; allocation-free).</summary>
        internal static bool Get(int index, out ISomniumPlayer player, out AcCarRig car, out AcSeatKind seat, out int photonId)
        {
            int k = 0;
            foreach (KeyValuePair<int, Remote> kv in Remotes)
            {
                if (k++ != index) continue;
                player = kv.Value.Player;
                car = kv.Value.Car;
                seat = kv.Value.Seat;
                photonId = kv.Key;
                return player != null && car != null && car.Root != null;
            }

            player = null; car = null; seat = AcSeatKind.Driver; photonId = -1;
            return false;
        }

        private static AcCarRig CarOf(AcNetPlayer p, out AcSeatKind seat)
        {
            if (p.RideInPhotonId >= 0)
            {
                seat = AcSeatKind.Passenger;
                if (p.RideInPhotonId == AcLocal.PhotonId) return AcLocal.Car;
                AcNetPlayer owner = Find(p.RideInPhotonId);
                return owner != null && owner.Car != null ? owner.Car.Rig : null;
            }

            seat = AcSeatKind.Driver;
            return p.Car != null ? p.Car.Rig : null;
        }

        private static void Target(Remote r)
        {
            Transform car = r.Car.Root.transform;
            Offset o;
            if (Offsets.TryGetValue(r.PlayerId, out o) && o.Root.sqrMagnitude > 1e-6f)
            {
                r.Target = car.TransformPoint(o.Root);
                r.TargetYaw = r.Geo.Yaw + o.Yaw;
                return;
            }

            Vector3 seat;
            if (!r.Geo.SeatModel(r.Seat, out seat)) seat = Vector3.zero;
            r.Target = r.Geo.ModelToWorld(seat);
            r.TargetYaw = r.Geo.Yaw;
        }

        private static ISomniumPlayer PlayerOf(int photonId)
        {
            IReadOnlyList<AcRemotes.Remote> all = RemotesAll();
            for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null && all[i].Id == photonId) return all[i].Player;
            return null;
        }

        private static Transform BodyRoot(ISomniumPlayer p)
        {
            try { return p.References != null && p.References.Body != null ? p.References.Body.Root : null; } catch (Exception) { return null; }
        }

        /// <summary>ETS Bind: the player's Fusion transforms (Root/Head/LeftHand/RightHand under "Body", state authority = the player).</summary>
        private static void Bind(Remote r)
        {
            if (r.NtRoot != null || r.GaveUp || Time.unscaledTime < r.NextBind) return;
            if (SceneNetworking.NetworkRunnerRef == null) { r.NextBind = Time.unscaledTime + 1f; return; }
            // back-off (the scene search allocates its array): every second for the first 5 tries, then every 5 s, give up after 15 tries
            // (~55 s; the names may differ in this client build) // ours
            r.BindTries++;
            r.NextBind = Time.unscaledTime + (r.BindTries < BindFastTries ? 1f : 5f);
            r.NtHead = r.NtLeft = r.NtRight = null;
            try
            {
                if (_found == null || Time.unscaledTime >= _nextSearch)
                {
                    _nextSearch = Time.unscaledTime + 1f;
                    _found = UnityEngine.Object.FindObjectsByType<NetworkBehaviour>(FindObjectsSortMode.None);
                }

                for (int i = 0; i < _found.Length; i++)
                {
                    NetworkBehaviour nt = _found[i];
                    if (nt == null || nt.Object == null || nt.Object.StateAuthority.PlayerId != r.PlayerId) continue;
                    Transform t = nt.transform;
                    if (t.parent == null || t.parent.name != "Body") continue;
                    switch (t.name)
                    {
                        case "Root": r.NtRoot = t; break;
                        case "Head": r.NtHead = t; break;
                        case "LeftHand": r.NtLeft = t; break;
                        case "RightHand": r.NtRight = t; break;
                    }
                }
            }
            catch (Exception e)
            {
                AcLog.WarnOnce("Seat: finding a remote network body threw " + e.GetType().Name + ": " + e.Message);
            }

            r.HaveRaw = false;
            if (r.NtRoot != null) r.BindTries = 0;      // a later loss of the transforms (avatar reload) starts a fresh back-off
            else if (r.BindTries >= BindMaxTries)
            {
                r.GaveUp = true;
                AcLog.Info("Seat: rider " + OwnerName(r.PlayerId) + " (" + r.PlayerId + ") still unbound after " + r.BindTries + " searches - stopped searching; only the body root is moved.");
            }

            if (!r.BindReported || r.NtRoot != null)
            {
                r.BindReported = true;
                AcLog.Info("Seat: rider " + OwnerName(r.PlayerId) + " (" + r.PlayerId + ") " + (r.NtRoot != null
                    ? "bound: Root" + (r.NtHead != null ? ",Head" : "") + (r.NtLeft != null ? ",LeftHand" : "") + (r.NtRight != null ? ",RightHand" : "")
                    : "unbound: only the body root is moved (a desktop avatar may lag the car)") + ".");
            }
        }

        private static void CacheRaw(Remote r)
        {
            if (r.NtRoot != null)
            {
                r.RawRoot = new Pose(r.NtRoot.position, r.NtRoot.rotation);
                if (r.NtHead != null) r.RawHead = new Pose(r.NtHead.position, r.NtHead.rotation);
                if (r.NtLeft != null) r.RawLeft = new Pose(r.NtLeft.position, r.NtLeft.rotation);
                if (r.NtRight != null) r.RawRight = new Pose(r.NtRight.position, r.NtRight.rotation);
                r.HaveRaw = true;
                return;
            }

            Transform root = BodyRoot(r.Player);
            if (root != null) { r.RawRoot = new Pose(root.position, root.rotation); r.HaveRaw = true; }
        }

        /// <summary>ETS EtsRiderMath.Correct: the rigid move of a raw pose that takes rawRoot (yaw rawYaw) to target (yaw targetYaw).</summary>
        public static Pose Correct(Pose raw, Vector3 rawRoot, float rawYaw, Vector3 target, float targetYaw)
        {
            Quaternion turn = Quaternion.Euler(0f, Mathf.DeltaAngle(rawYaw, targetYaw), 0f);
            Quaternion q = turn * raw.rotation;
            q.Normalize();
            return new Pose(target + turn * (raw.position - rawRoot), q);
        }

        public static float YawOf(Quaternion q)
        {
            Vector3 f = q * Vector3.forward;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        private static void Apply(Remote r)
        {
            if (!r.HaveRaw) return;
            ISomniumPlayerBody body = null;
            try { body = r.Player.References != null ? r.Player.References.Body : null; } catch (Exception) { body = null; }
            Vector3 rawRoot = r.RawRoot.position;
            float rawYaw = YawOf(r.RawRoot.rotation);
            Pose root = Correct(r.RawRoot, rawRoot, rawYaw, r.Target, r.TargetYaw);
            if (r.NtRoot != null) r.NtRoot.SetPositionAndRotation(root.position, root.rotation);
            Transform bodyRoot = body != null ? body.Root : null;
            if (bodyRoot != null) bodyRoot.SetPositionAndRotation(root.position, root.rotation);
            if (r.NtRoot == null) return;
            Place(r.NtHead, body != null ? body.Head : null, r.RawHead, rawRoot, rawYaw, r);
            Place(r.NtLeft, body != null ? body.LeftHand : null, r.RawLeft, rawRoot, rawYaw, r);
            Place(r.NtRight, body != null ? body.RightHand : null, r.RawRight, rawRoot, rawYaw, r);
        }

        private static void Place(Transform network, Transform driven, Pose raw, Vector3 rawRoot, float rawYaw, Remote r)
        {
            if (network == null) return;
            Pose part = Correct(raw, rawRoot, rawYaw, r.Target, r.TargetYaw);
            network.SetPositionAndRotation(part.position, part.rotation);
            if (driven != null) driven.SetPositionAndRotation(part.position, part.rotation);
        }

        private static void Limbs(Remote r)
        {
            ISomniumPlayerFeatures f = null;
            try { f = r.Player.Features; } catch (Exception) { f = null; }
            if (f == null) return;
            try
            {
                if (!r.LegsOff) { r.LegsOff = true; if (f.Avatar != null) f.Avatar.SetVRIKLegMovementDisabled(true); }
                if (!r.FallingOff) { r.FallingOff = true; if (f.Animation != null) f.Animation.SetFallingAnimationDisabled(true); }
            }
            catch (Exception e) { AcLog.WarnOnce("Seat: a remote rider's avatar switches threw: " + e.Message); }
        }

        private static void Restore(Remote r)
        {
            try
            {
                ISomniumPlayerFeatures f = r.Player != null ? r.Player.Features : null;
                if (f != null && r.LegsOff && f.Avatar != null) f.Avatar.SetVRIKLegMovementDisabled(false);
                if (f != null && r.FallingOff && f.Animation != null) f.Animation.SetFallingAnimationDisabled(false);
            }
            catch (Exception) { /* they have left */ }

            r.LegsOff = r.FallingOff = false;
        }

        private static void Report()
        {
            _nextReport = Time.unscaledTime + 60f;
            foreach (KeyValuePair<int, Remote> kv in Remotes)
            {
                Remote r = kv.Value;
                AcLog.Info("Seat: rider " + OwnerName(r.PlayerId) + " in " + (r.Car != null && r.Car.Car != null ? r.Car.Car.Id : "?") + " (" + (r.Seat == AcSeatKind.Driver ? "driver" : "passenger") +
                           "): network body up to " + r.WorstBefore.ToString("0.00") + " m from its seat before correction, " + r.WorstAfter.ToString("0.000") + " m after; " +
                           (Offsets.ContainsKey(r.PlayerId) ? "record offset" : "seat point") + ", " + (r.NtRoot != null ? "bound" : "unbound") + ".");
                r.WorstBefore = r.WorstAfter = 0f;
            }
        }

        /// <summary>Module stop: every remote avatar back to the client.</summary>
        internal static void Stop()
        {
            foreach (KeyValuePair<int, Remote> kv in Remotes) Restore(kv.Value);
            Remotes.Clear();
            Offsets.Clear();
            CancelAsk();
        }
    }
}
