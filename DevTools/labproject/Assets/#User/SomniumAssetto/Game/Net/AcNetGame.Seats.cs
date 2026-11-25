using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] Passengers (part of <see cref="AcNetGame"/>; ETS EtsNet.RequestSeat flow): S asks before seating the local player in ANOTHER player's
    /// car - <see cref="RequestSeat"/> writes "requesting the passenger seat of P's car" into my record, P's client grants it within ~0.25 s when the
    /// seat is free (AcNet.ServeSeats), and <c>granted</c> runs here; <c>refused</c> runs with the reason (taken, no passenger seat, no answer in 3 s,
    /// the car is gone). Once seated, my rider record (AcNetGame.Own) keeps naming the seat; when it stops (S got out) the owner frees the seat
    /// within 2 s. The driver seat of a remote car is never requestable (it is its owner's). My own car: granted at once.
    /// </summary>
    public static partial class AcNetGame
    {
        private static Action _seatGranted;
        private static Action<string> _seatRefused;
        private static string _seatOwnerName;

        /// <summary>
        /// Asks for <paramref name="seat"/> of <paramref name="car"/> (a remote player's car drawn here, or my own). One request at a time (a new one
        /// supersedes the old: its <c>refused</c> runs with "Another request took its place."). Callbacks run on the main thread from NetTick.
        /// The words are ours (AC has no passengers, so no game string exists).
        /// </summary>
        public static void RequestSeat(AcCarRig car, AcSeatKind seat, Action granted, Action<string> refused)
        {
            string reason = SeatRefusal(car, seat);
            if (reason != null)
            {
                AcLog.Info("Net: seat request refused at once - " + reason);
                Call(refused, reason);
                return;
            }

            if (!Up || !Net.Online || ReferenceEquals(car, AcLocal.Car))
            {
                Call(granted);
                return;
            }

            if (Net.RequestActive)
            {
                Action<string> superseded = _seatRefused;
                Net.EndSeatRequest();
                _seatGranted = null;
                _seatRefused = null;
                Call(superseded, "Another request took its place.");
            }

            int owner = OwnerOfCar(car);
            _seatGranted = granted;
            _seatRefused = refused;
            _seatOwnerName = NameOf(owner);
            Net.BeginSeatRequest(owner, (int)seat);
            AcLog.Info("Net: asked " + _seatOwnerName + " for the passenger seat");
        }

        // ------------------------------------------------------------------ S's hooks (Game/Seat/AcRiders: Asker, CanGrantPassenger, SetRemoteSeatOffset)

        private static AcRiders.SeatAsk _asker;
        private static Func<int, bool> _canGrant;

        /// <summary>Plugs N into S's AcRiders: its ride requests go through <see cref="RequestSeat"/>, the owner's grant asks S's CanGrantPassenger.</summary>
        private static void HookSeat(bool on)
        {
            try
            {
                if (on)
                {
                    if (_asker == null) _asker = AskForS;
                    if (_canGrant == null) _canGrant = CanGrantForS;
                    AcRiders.Asker = _asker;
                    if (Net != null) Net.CanGrant = _canGrant;
                }
                else
                {
                    if (AcRiders.Asker == _asker) AcRiders.Asker = null;
                    if (Net != null) Net.CanGrant = null;
                }
            }
            catch (Exception e)
            {
                AcLog.Warn("Net: hooking S's riders failed: " + e.Message);
            }
        }

        /// <summary>S's AcRiders.Asker: (owner photon id, seat, granted, refused).</summary>
        private static void AskForS(int ownerPhotonId, AcSeatKind seat, Action granted, Action<string> refused)
        {
            AcCarRig car = null;
            AcNetPlayer p = FindByPhotonId(ownerPhotonId);
            if (p != null && p.Car != null) car = p.Car.Rig;
            if (car == null && Up && Net.Online && ownerPhotonId == Net.LocalId) car = AcLocal.Car;
            if (car == null) { Call(refused, "That car is not in this session any more."); return; }
            RequestSeat(car, seat, granted, refused);
        }

        /// <summary>The owner's side: S decides (stopped car, a passenger seat, nobody in it).</summary>
        private static bool CanGrantForS(int requester)
        {
            string reason = null;
            bool ok;
            try { ok = AcRiders.CanGrantPassenger(requester, out reason); }
            catch (Exception e)
            {
                AcLog.WarnOnce("Net: S's CanGrantPassenger threw (seat requests are refused): " + e.Message);
                ok = false;
                reason = "the seat check failed";
            }

            if (!ok) AcLog.Info("Net: passenger seat request of " + NameOf(requester) + " refused: " + (reason ?? "?"));
            return ok;
        }

        /// <summary>Every seated remote's body offset in their car's frame (their rider record) handed to S's AcRiders, which places the bodies.</summary>
        private static void FeedRiders()
        {
            IReadOnlyList<AcNet.Remote> listed = Net.Listed;
            for (int i = 0; i < listed.Count; i++)
            {
                AcNet.Remote r = listed[i];
                if (!r.Fresh) continue;
                AcNetRider rider = r.State.Rider;
                if (rider.Target == 0 || rider.Requesting || !r.State.Seated) continue;
                try { AcRiders.SetRemoteSeatOffset(r.PlayerId, V(rider.Offset), rider.YawLocal); }
                catch (Exception e) { AcLog.WarnOnce("Net: feeding S's riders failed: " + e.Message); return; }
            }
        }

        /// <summary>Withdraws a pending request (its <c>refused</c> runs with "Cancelled."). Getting out needs nothing: my rider record stops naming
        /// the seat and the owner frees it.</summary>
        public static void LeaveSeat()
        {
            if (!Up || !Net.RequestActive) return;
            Action<string> refused = _seatRefused;
            Net.EndSeatRequest();
            _seatGranted = null;
            _seatRefused = null;
            Call(refused, "Cancelled.");
        }

        /// <summary>A seat request is waiting for the owner's answer.</summary>
        public static bool SeatRequestPending { get { return Up && Net.RequestActive; } }

        /// <summary>Who sits in <paramref name="car"/>'s passenger seat (Photon id; -1 none): for my car my own seat table, for a remote car its owner's.</summary>
        public static int PassengerOf(AcCarRig car)
        {
            if (!Up || car == null) return -1;
            if (ReferenceEquals(car, AcLocal.Car)) return Net.SeatOccupant;
            AcNetPlayer p = PlayerOfCar(car);
            return p != null ? p.PassengerPhotonId : -1;
        }

        /// <summary>The Photon id of the player whose car this is (mine: my id, or -1 offline; AI: -(100 + index); unknown: int.MinValue).</summary>
        public static int OwnerOfCar(AcCarRig car)
        {
            if (car == null) return int.MinValue;
            if (ReferenceEquals(car, AcLocal.Car)) return Up && Net.Online ? Net.LocalId : -1;
            AcNetPlayer p = PlayerOfCar(car);
            return p != null ? p.PhotonId : int.MinValue;
        }

        /// <summary>The remote player (or remote AI) whose drawn car this is, null for my own / unknown rigs.</summary>
        public static AcNetPlayer PlayerOfCar(AcCarRig car)
        {
            if (car == null) return null;
            for (int i = 0; i < CarList.Count; i++) if (ReferenceEquals(CarList[i].Rig, car)) return CarList[i].Player;
            return null;
        }

        /// <summary>The remote car drawn here for a rig (null for my own / AI rigs of this master / unknown).</summary>
        public static AcRemoteCar RemoteCarOf(AcCarRig car)
        {
            if (car == null) return null;
            for (int i = 0; i < CarList.Count; i++) if (ReferenceEquals(CarList[i].Rig, car)) return CarList[i];
            return null;
        }

        private static string SeatRefusal(AcCarRig car, AcSeatKind seat)
        {
            if (!Up) return null;
            if (car == null) return "There is no car.";
            if (ReferenceEquals(car, AcLocal.Car)) return null;
            AcNetPlayer p = PlayerOfCar(car);
            if (p == null) return "That car is not in this session any more.";
            if (p.IsAi) return "AI cars take no passengers.";
            if (seat == AcSeatKind.Driver) return p.Name + " drives this car.";
            if (!Net.Claimed) return "Still joining the session; try again in a moment.";
            if (!p.HasPassengerSeat) return "This car has no passenger seat.";
            int who = p.PassengerPhotonId;
            if (who >= 0 && who != Net.LocalId) return "The seat is taken.";
            return null;
        }

        private static void StepSeats()
        {
            AcNet.SeatAnswer a = Net.TakeSeatAnswer();
            if (a == AcNet.SeatAnswer.None || a == AcNet.SeatAnswer.Waiting) return;
            Action granted = _seatGranted;
            Action<string> refused = _seatRefused;
            _seatGranted = null;
            _seatRefused = null;
            string who = _seatOwnerName ?? "The driver";
            switch (a)
            {
                case AcNet.SeatAnswer.Granted:
                    AcLog.Info("Net: " + who + " granted the passenger seat");
                    Call(granted);
                    return;
                case AcNet.SeatAnswer.Taken: Call(refused, "The seat is taken."); break;
                case AcNet.SeatAnswer.NoSeat: Call(refused, "This car has no passenger seat."); break;
                case AcNet.SeatAnswer.Refused: Call(refused, who + " takes no passengers now."); break;
                case AcNet.SeatAnswer.NoAnswer: Call(refused, who + " did not answer."); break;
                default: Call(refused, who + "'s car is gone."); break;
            }

            AcLog.Info("Net: passenger seat of " + who + "'s car: " + a);
        }

        private static void Call(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { AcLog.Error("Net: a seat callback failed: " + e); }
        }

        private static void Call(Action<string> a, string reason)
        {
            if (a == null) return;
            try { a(reason); }
            catch (Exception e) { AcLog.Error("Net: a seat callback failed: " + e); }
        }
    }
}
