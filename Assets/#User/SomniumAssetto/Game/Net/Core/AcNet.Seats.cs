using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Passengers (part of <see cref="AcNet"/>): ETS/EtsPlayerWire.cs EtsSeatTable + EtsSeatRequest ported for AC's one passenger seat (AC road cars
    /// have a driver and at most one passenger; the driver seat is always the car owner's). State-based on purpose, no RPC to lose:
    ///  - the passenger's record says "requesting seat 1 of player P's car" (rider record, requesting bit);
    ///  - the OWNER's client, every <see cref="ServeEvery"/>, grants the seat to a request naming its car when the seat is free (writes the passenger's
    ///    id in its record int 31), and frees it when the passenger's record has not named the car for <see cref="SeatStaleAfter"/>, the passenger left,
    ///    or the owner has no car out;
    ///  - the passenger sees itself named there: Granted (within ~0.25 s + the net delay); somebody else: Taken; the "no passenger seat" / "refusing"
    ///    bits: NoSeat / Refused; nothing within <see cref="SeatTimeout"/>: NoAnswer; the owner's car gone: Gone.
    /// </summary>
    public sealed partial class AcNet
    {
        /// <summary>ETS values: the owner frees a seat whose passenger has not named it for 2 s; a request waits 3 s for an answer.</summary>
        public const double SeatStaleAfter = 2.0, SeatTimeout = 3.0;

        public enum SeatAnswer { None, Waiting, Granted, Taken, NoSeat, Refused, NoAnswer, Gone }

        private int _seatOccupant = -1;
        private double _refuseUntil = double.NegativeInfinity;

        /// <summary>How long a refusal (the owner's game said no) stays visible as the "refusing riders" bit, so the requester reads Refused. // ours</summary>
        public const double RefuseShowFor = 1.0;

        /// <summary>The owner's game decides whether a request for my passenger seat may be granted (S: car stopped, a passenger seat, ...); null = always.</summary>
        public Func<int, bool> CanGrant;

        /// <summary>The "refusing riders" bit is up now (a request was just refused by the owner's game).</summary>
        public bool RefusingNow { get { return Now < _refuseUntil; } }

        private bool AskCanGrant(int requester)
        {
            Func<int, bool> f = CanGrant;
            if (f == null) return true;
            try { return f(requester); }
            catch (Exception e) { Log("the seat grant check threw " + e.GetType().Name + ": " + e.Message); return false; }
        }
        private double _seatStaleSince = -1.0;
        private string _seatNote;

        /// <summary>Who sits in my car's passenger seat (PlayerId, -1 none) - the owner's seat table.</summary>
        public int SeatOccupant { get { return _seatOccupant; } }

        /// <summary>The seat table's last change, for the log (null when none since the last call).</summary>
        public string TakeSeatNote()
        {
            string n = _seatNote;
            _seatNote = null;
            return n;
        }

        private void ServeSeats(double now, IReadOnlyList<int> present)
        {
            int localId = LocalId;
            bool haveCar = Own.Car.Present && !Own.NoPassengerSeat;
            int me = AcNetWire.TargetPlayer(localId);
            bool seen = false;
            for (int i = 0; i < _listed.Count && me != 0; i++)
            {
                Remote r = _listed[i];
                AcNetRider rider = r.State.Rider;
                if (rider.Target != me || rider.Seat != (int)AcSeatKindWire.Passenger) continue;
                if (_seatOccupant == r.PlayerId) { seen = true; continue; }
                if (!rider.Requesting || _seatOccupant >= 0 || !haveCar || Own.RefuseRiders) continue;
                if (!AskCanGrant(r.PlayerId))
                {
                    // the owner's game says no (moving, ...): show the "refusing riders" bit for a moment so the requester reads Refused
                    if (!RefusingNow) _seatNote = "passenger seat request of player " + r.PlayerId + " refused by the game";
                    _refuseUntil = now + RefuseShowFor;
                    continue;
                }

                _seatOccupant = r.PlayerId;
                _seatStaleSince = -1.0;
                seen = true;
                _seatNote = "passenger seat granted to player " + r.PlayerId;
            }

            if (_seatOccupant < 0) return;
            if (!haveCar || !AcSlotRules.Contains(present, _seatOccupant))
            {
                _seatNote = "passenger seat freed: " + (!haveCar ? "my car is gone" : "player " + _seatOccupant + " left");
                _seatOccupant = -1;
                _seatStaleSince = -1.0;
                return;
            }

            if (seen) { _seatStaleSince = -1.0; return; }
            if (_seatStaleSince < 0.0) _seatStaleSince = now;
            else if (now - _seatStaleSince >= SeatStaleAfter)
            {
                _seatNote = "passenger seat freed: player " + _seatOccupant + "'s record has not named it for " + SeatStaleAfter + " s";
                _seatOccupant = -1;
                _seatStaleSince = -1.0;
            }
        }

        /// <summary>Offline / tests: the owner frees its passenger seat now.</summary>
        public void FreeSeat() { _seatOccupant = -1; _seatStaleSince = -1.0; }

        // ------------------------------------------------------------------ the passenger's side

        private struct Request
        {
            public bool Active;
            public int OwnerId, Seat;
            public double Since;
            public void End() { Active = false; }
        }

        private Request _request;
        private SeatAnswer _answer;

        /// <summary>A seat request is waiting for the owner's answer.</summary>
        public bool RequestActive { get { return _request.Active; } }
        public int RequestOwner { get { return _request.Active ? _request.OwnerId : -1; } }
        public double RequestSince { get { return _request.Since; } }

        /// <summary>Asks player <paramref name="ownerId"/> for seat <paramref name="seat"/> (1 = passenger) of their car. One request at a time.</summary>
        public void BeginSeatRequest(int ownerId, int seat)
        {
            _request = new Request { Active = true, OwnerId = ownerId, Seat = seat, Since = Now };
            _answer = SeatAnswer.Waiting;
            Log("seat " + seat + " of player " + ownerId + "'s car: asked.");
        }

        public void EndSeatRequest() { _request.End(); _answer = SeatAnswer.None; }

        /// <summary>The answer reached this tick (Granted / Taken / ... once, then None); Waiting while it waits.</summary>
        public SeatAnswer TakeSeatAnswer()
        {
            SeatAnswer a = _answer;
            if (a != SeatAnswer.Waiting) _answer = SeatAnswer.None;
            return a;
        }

        private void StepSeatRequest(double now, int localId)
        {
            if (!_request.Active) return;
            Remote owner = RemoteOfPlayer(_request.OwnerId);
            SeatAnswer a;
            if (owner == null || !owner.Decoded || !owner.State.Car.Present) a = SeatAnswer.Gone;
            else if (owner.State.PassengerId == localId && localId >= 0) a = SeatAnswer.Granted;
            else if (owner.State.PassengerId >= 0) a = SeatAnswer.Taken;
            else if (owner.State.NoPassengerSeat) a = SeatAnswer.NoSeat;
            else if (owner.State.RefuseRiders) a = SeatAnswer.Refused;
            else if (now - _request.Since >= SeatTimeout) a = SeatAnswer.NoAnswer;
            else a = SeatAnswer.Waiting;
            if (a == SeatAnswer.Waiting) return;
            int ms = (int)Math.Round((now - _request.Since) * 1000.0);
            Log("seat " + _request.Seat + " of player " + _request.OwnerId + "'s car: " + a + " in " + ms + " ms.");
            _request.End();
            _answer = a;
        }
    }

    /// <summary>The seat numbers on the wire (= AcSeatKind values, kept here so the pure core does not need the Unity-side enum file).</summary>
    public enum AcSeatKindWire { Driver = 0, Passenger = 1 }
}
