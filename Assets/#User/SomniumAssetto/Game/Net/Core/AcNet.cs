using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Everyone's presence over the world's Photon Fusion session - HALO/Game/Net/Core/HaloNet.cs ported for racing (TickCore, claim, housekeeping,
    /// remotes, master, own write, HasRecord, Stop kept; the shooter parts dropped). Networked objects are placed in the scene by hand ("Ac Net Player
    /// 0..49" + the master's blocks), none can be made at run time. Each client claims one free slot (<see cref="AcSlotClaim"/>, verbatim), writes its
    /// own record there (<see cref="AcNetWire"/>, ~10 Hz idle, up to 30 Hz with a moving car) and reads everyone else's. Leaving sends nothing: the
    /// slot's owner is no longer in the room, every client stops counting it, and whoever holds its authority writes it free. The instance master also
    /// writes the race-control, AI and race-state blocks (<see cref="AcNetBlocks"/>, AcNet.Master.cs / AcNet.Blocks.cs).
    ///
    /// Pure C# over an <see cref="IAcSlotTransport"/> (the real one is AcFusionTransport, the test one a loopback), so two logical players can run in one
    /// process (harness, lab). The game layer (AcNetGame) fills <see cref="Own"/> each frame, calls <see cref="Tick"/>, and reads <see cref="Listed"/>.
    /// Never writes a record it does not hold, never throws into the caller.
    /// </summary>
    public sealed partial class AcNet
    {
        public const int MaxSlots = AcNetWire.MaxSlots;
        public const int Records = AcNetSlots.Total;

        /// <summary>Own-record writes a second: idle (Halo WriteEvery) and with a moving car (ETS WriteEvery 1/30; Fusion itself sends ~16 Hz).</summary>
        public double WriteEvery = 0.1, WriteEveryMoving = 1.0 / 30.0;
        /// <summary>Seq + stamp advance at least this often even when nothing moves (a live record is told from one left behind). // ours</summary>
        public const double KeepAlive = 1.0;
        public const double ServeEvery = 0.25;

        /// <summary>Another player's slot (in the Dreamport, maybe on my track).</summary>
        public sealed class Remote
        {
            public int Slot, PlayerId;
            public AcNetState State;
            /// <summary>The record's seq and stamp as last read (seq advances with each motion sample).</summary>
            public int Seq = -1, StampMs;
            /// <summary>Any int of the record changed this tick.</summary>
            public bool Fresh;
            /// <summary>A new motion sample arrived this tick (seq changed).</summary>
            public bool NewSample;
            public bool Decoded;
            /// <summary>The car's samples (fed by the session when the record carries a car).</summary>
            public readonly AcNetTimeline Timeline = new AcNetTimeline();
            /// <summary>The Somnium nickname (set by the game layer).</summary>
            public string Name;
            /// <summary>The game layer's own object for this player.</summary>
            public object Tag;
            public double JoinedAt, ChangedAt;

            internal int Generation = -1;
            internal bool ForeignLogged;
            internal readonly int[] Last = new int[AcNetSlots.Ints];
        }

        private readonly IAcSlotTransport _t;
        private readonly Func<double> _clock;
        private readonly string _label;
        private readonly int[][] _slots = new int[Records][];
        private readonly bool[] _valid = new bool[Records];
        private readonly bool[] _authority = new bool[Records];
        private readonly Remote[] _remoteBySlot = new Remote[MaxSlots];
        private readonly List<Remote> _listed = new List<Remote>(MaxSlots);
        private readonly AcSlotClaim _claim = new AcSlotClaim();
        private readonly int[] _own = new int[AcNetSlots.Ints];
        private readonly int[] _free = new int[AcNetSlots.Ints];
        private readonly double _t0;
        private int _slotCount, _worldCount, _aiCount, _raceCount, _hostCount;
        private double _nextServe, _nextWrite, _lastSeqAt = double.NegativeInfinity, _ownedAt = -1.0;
        private readonly int[] _seqSeen = new int[MaxSlots];
        private readonly double[] _changedAt = new double[MaxSlots];
        private bool _forceWrite, _wasOnline;
        private int _writes, _reads, _errors, _logs;
        private bool _disposed;

        /// <summary>What this player says about itself: the game layer sets it before every <see cref="Tick"/> (the session overrides Status
        /// when Free, Master and PassengerId).</summary>
        public AcNetState Own = AcNetState.Empty();

        /// <summary>The time of the pose in <see cref="Own"/> on the writer's physics clock, ms (Time.fixedTimeAsDouble): pose and stamp agree
        /// (ETS PackOwnMotion). Negative = use the session clock.</summary>
        public int OwnStampMs = -1;

        /// <summary>Players who appeared in / disappeared from <see cref="Listed"/> during the last tick.</summary>
        public readonly List<Remote> Joined = new List<Remote>(8), Left = new List<Remote>(8);

        /// <summary>Optional log sink (AcLog.Info by default).</summary>
        public static Action<string> LogSink;

        public AcNet(IAcSlotTransport transport, Func<double> clock, string label)
        {
            _t = transport;
            _clock = clock;
            _label = label ?? "";
            _t0 = clock();
            for (int k = 0; k < Records; k++) _slots[k] = new int[AcNetSlots.Ints];
            InitBlocks();
        }

        public IAcSlotTransport Transport { get { return _t; } }

        /// <summary>In a room with the slots found. False alone in the editor, before the room is joined, or without slot objects.</summary>
        public bool Online { get { return _t.Online && _t.SlotCount > 0; } }

        public int LocalId { get { return _t.LocalId; } }

        /// <summary>This client's slot, -1 until claimed.</summary>
        public int MySlot { get { return _claim.Holding ? _claim.Slot : -1; } }

        /// <summary>The claim is complete (the record is held and confirmed).</summary>
        public bool Claimed { get { return _claim.Current == AcSlotClaim.Stage.Owned; } }

        public int SlotCount { get { return _slotCount; } }
        public int WorldCount { get { return _worldCount; } }
        public int AiBlockCount { get { return _aiCount; } }
        public int RaceCount { get { return _raceCount; } }
        public int HostCount { get { return _hostCount; } }

        /// <summary>Every other Dreamport player in the instance (decoded, not free), in slot order.</summary>
        public IReadOnlyList<Remote> Listed { get { return _listed; } }

        public Remote RemoteAt(int slot) { return slot >= 0 && slot < MaxSlots ? _remoteBySlot[slot] : null; }

        /// <summary>The record of another player by player id, null if none.</summary>
        public Remote RemoteOfPlayer(int playerId)
        {
            if (playerId < 0) return null;
            for (int k = 0; k < MaxSlots; k++)
            {
                Remote r = _remoteBySlot[k];
                if (r != null && r.PlayerId == playerId) return r;
            }

            return null;
        }

        public double Now { get { return _clock(); } }

        public int StampMs() { return (int)((_clock() - _t0) * 1000.0); }

        /// <summary>Ints written (changed) since the start; for the stats line.</summary>
        public long IntsWrittenTotal { get; private set; }

        // ------------------------------------------------------------------ the frame

        /// <summary>Once per frame: read every record, step the claim, keep house, find the others, the master and its blocks, write the own record.
        /// Never throws.</summary>
        public void Tick()
        {
            Joined.Clear();
            Left.Clear();
            if (_disposed) return;
            try
            {
                TickCore(_clock());
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) Warn("Net" + _label + ": the tick threw (" + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e);
            }
        }

        private void TickCore(double now)
        {
            _t.Update(now);
            bool online = Online;
            if (online != _wasOnline)
            {
                _wasOnline = online;
                if (online) Log("room joined (player id " + LocalId + ", room master " + _t.IsMaster + ", " + _t.SlotCount + " slots, world " + _t.WorldCount + ", ai "
                                + _t.AiCount + ", race " + _t.RaceCount + ", host " + _t.HostCount + ") - " + _t.Describe());
                else Log("offline (no room: nobody else is shown)");
                if (online) CommandsWentOnline();
            }

            _slotCount = online ? Math.Min(_t.SlotCount, MaxSlots) : 0;
            _worldCount = online ? Math.Min(_t.WorldCount, AcNetSlots.WorldCount) : 0;
            _aiCount = online ? Math.Min(_t.AiCount, AcNetSlots.AiCount) : 0;
            _raceCount = online ? Math.Min(_t.RaceCount, AcNetSlots.RaceCount) : 0;
            _hostCount = online ? Math.Min(_t.HostCount, AcNetSlots.HostCount) : 0;
            IReadOnlyList<int> present = _t.Present;
            int localId = LocalId;
            for (int k = 0; k < Records; k++)
            {
                bool valid = InRange(k) && _t.IsValid(k);
                _valid[k] = valid;
                _authority[k] = valid && _t.HasAuthority(k);
                if (!valid)
                {
                    if (_slots[k][0] != 0) Array.Clear(_slots[k], 0, _slots[k].Length);
                    continue;
                }

                if (!_t.Read(k, _slots[k])) _valid[k] = false;
                else
                {
                    _reads++;
                    if (k < MaxSlots && _slots[k][AcNetWire.AtSeq] != _seqSeen[k]) { _seqSeen[k] = _slots[k][AcNetWire.AtSeq]; _changedAt[k] = now; }
                }
            }

            StepClaim(online, localId, present, now);
            if (now >= _nextServe)
            {
                _nextServe = now + ServeEvery;
                Housekeeping(localId, present);
                ServeSeats(now, present);
            }

            RefreshRemotes(now, localId, present);
            StepMaster(now, localId);
            StepBlocks(now, localId);
            StepSeatRequest(now, localId);
            WriteOwn(now, localId);
        }

        private bool InRange(int k)
        {
            if (k < AcNetSlots.FirstWorld) return k < _slotCount;
            if (k < AcNetSlots.FirstAi) return k - AcNetSlots.FirstWorld < _worldCount;
            if (k < AcNetSlots.FirstRace) return k - AcNetSlots.FirstAi < _aiCount;
            if (k < AcNetSlots.FirstHost) return k - AcNetSlots.FirstRace < _raceCount;
            return k - AcNetSlots.FirstHost < _hostCount;
        }

        // ------------------------------------------------------------------ the claim and the house (HaloNet verbatim)

        private void StepClaim(bool online, int localId, IReadOnlyList<int> present, double now)
        {
            var input = new AcSlotClaim.Inputs { Now = now, LocalId = localId, Allowed = online && _slotCount > 0, SlotCount = _slotCount };
            _claim.Step(input, _slots, _valid, _authority, present);
            if (_claim.AskAuthority >= 0) _t.RequestAuthority(_claim.AskAuthority);
            if (_claim.WriteClaim && _claim.Slot >= 0)
            {
                AcNetWire.WriteClaim(_own, localId, _claim.Generation, StampMs());
                _forceWrite = true;
            }

            if (_claim.Current == AcSlotClaim.Stage.Owned) { if (_ownedAt < 0.0) _ownedAt = now; }
            else _ownedAt = -1.0;
            string note = _claim.TakeNote();
            if (note != null) Log(note + ".");
        }

        /// <summary>When the claim completed (session clock), -1 while not owned.</summary>
        public double OwnedAt { get { return _ownedAt; } }

        private void Housekeeping(int localId, IReadOnlyList<int> present)
        {
            for (int k = 0; k < _slotCount; k++)
            {
                if (!_valid[k] || !AcSlotRules.ToFree(_slots[k], _authority[k], k == _claim.Slot, localId, present)) continue;
                int gone = AcNetWire.OwnerOf(_slots[k]);
                Array.Copy(_slots[k], _free, _free.Length);
                AcNetWire.WriteFree(_free, StampMs());
                Push(k, _free);
                Log("slot " + k + " written free (" + (gone == localId ? "this client's own record, left behind by a lost claim" : "player " + gone + " left") + ").");
            }
        }

        private void Push(int record, int[] values)
        {
            if (record < 0 || record >= Records || !_valid[record] || !_authority[record]) return;
            try
            {
                int n = _t.Write(record, values);
                _writes += n;
                IntsWrittenTotal += n;
            }
            catch (Exception e) { _errors++; if (_errors <= 3) Warn("Net" + _label + ": writing record " + record + " failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ the others

        private void RefreshRemotes(double now, int localId, IReadOnlyList<int> present)
        {
            int mySlot = MySlot;
            _listed.Clear();
            for (int k = 0; k < MaxSlots; k++)
            {
                Remote r = _remoteBySlot[k];
                int[] slot = _slots[k];
                bool listed = k < _slotCount && _valid[k] && k != mySlot && AcSlotRules.Listed(slot, localId, present) && AcNetWire.StatusOf(slot) != AcNetStatus.Free && !Shadowed(k);
                if (!listed)
                {
                    if (r != null) DropRemote(k, r);
                    continue;
                }

                int owner = AcNetWire.OwnerOf(slot);
                int generation = AcNetWire.GenerationOf(slot);
                if (r != null && (r.PlayerId != owner || r.Generation != generation))
                {
                    DropRemote(k, r);
                    r = null;
                }

                if (r == null)
                {
                    r = new Remote { Slot = k, PlayerId = owner, Generation = generation, JoinedAt = now, Name = "Player " + owner };
                    _remoteBySlot[k] = r;
                    Joined.Add(r);
                }

                r.Fresh = false;
                r.NewSample = false;
                if (!r.Decoded || Differs(slot, r.Last))
                {
                    Array.Copy(slot, r.Last, AcNetSlots.Ints);
                    r.Decoded = AcNetWire.Unpack(slot, out r.State);
                    if (!r.Decoded && !r.ForeignLogged)
                    {
                        r.ForeignLogged = true;
                        Log("player " + owner + " (slot " + k + ") runs net protocol " + AcNetWire.ProtocolOf(slot) + ", this build " + AcNetWire.Protocol
                            + ": refused (not listed, no car, no seat; its slot stays taken and its master bit still counts) - both need the same Dreamport build.");
                    }
                    r.Fresh = r.Decoded;
                    r.ChangedAt = now;
                    int seq = slot[AcNetWire.AtSeq];
                    if (r.Decoded && seq != r.Seq)
                    {
                        r.Seq = seq;
                        r.StampMs = slot[AcNetWire.AtStamp];
                        r.NewSample = true;
                        if (r.State.Car.Present) r.Timeline.Add(in r.State.Car, r.Seq, r.StampMs, now);
                    }
                }

                if (r.Decoded) _listed.Add(r);
            }
        }

        private static bool Differs(int[] a, int[] b)
        {
            for (int i = 0; i < AcNetSlots.Ints; i++) if (a[i] != b[i]) return true;
            return false;
        }

        private void DropRemote(int k, Remote r)
        {
            _remoteBySlot[k] = null;
            Left.Add(r);
        }

        /// <summary>
        /// Two written slots naming the same owner: a client whose claim was lost and who claimed another slot leaves the old record behind until it is
        /// freed. Only the record still being written counts (HaloNet verbatim).
        /// </summary>
        private bool Shadowed(int k)
        {
            int owner = AcNetWire.OwnerOf(_slots[k]);
            for (int j = 0; j < _slotCount; j++)
            {
                if (j == k || !_valid[j] || AcNetWire.OwnerOf(_slots[j]) != owner || AcNetWire.StatusOf(_slots[j]) == AcNetStatus.Free) continue;
                if (_changedAt[j] > _changedAt[k] + 0.35 || (Math.Abs(_changedAt[j] - _changedAt[k]) <= 0.35 && j > k)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ own record

        private void WriteOwn(double now, int localId)
        {
            int k = MySlot;
            if (k < 0 || k >= _slotCount || !_valid[k] || !_authority[k]) return;
            if (AcNetWire.OwnerOf(_own) != localId) return;   // the claim record is written first (StepClaim)
            if (_forceWrite)
            {
                _forceWrite = false;
                _nextWrite = now + WriteEvery;
                _own[AcNetWire.AtSeq]++;
                _own[AcNetWire.AtStamp] = StampMs();
                _lastSeqAt = now;
                Push(k, _own);
                return;
            }

            if (now < _nextWrite) return;
            AcNetState own = Own;
            bool moving = own.Car.Present && (own.Driving || own.Car.Vel.SqrLength > 0.01f);
            _nextWrite = now + (moving ? WriteEveryMoving : WriteEvery);
            if (own.Status == AcNetStatus.Free) own.Status = AcNetStatus.Present;
            own.Master = Online && _t.IsMaster;
            own.PassengerId = SeatOccupant;
            if (RefusingNow) own.RefuseRiders = true;
            if (_request.Active)
            {
                own.Rider.Target = AcNetWire.TargetPlayer(_request.OwnerId);
                own.Rider.Seat = _request.Seat;
                own.Rider.Requesting = true;
            }

            bool any;
            bool motion = AcNetWire.Pack(in own, _own, out any);
            if (motion || now - _lastSeqAt >= KeepAlive)
            {
                _own[AcNetWire.AtSeq]++;
                _own[AcNetWire.AtStamp] = OwnStampMs >= 0 ? OwnStampMs : StampMs();
                _lastSeqAt = now;
                any = true;
            }

            if (any) Push(k, _own);
        }

        /// <summary>The own record as last written (tests).</summary>
        public int[] OwnRecord { get { return _own; } }

        /// <summary>The own record is written free (this client leaves the world). Idempotent.</summary>
        public void Stop()
        {
            int k = MySlot;
            if (k >= 0 && k < _slotCount && _valid[k] && _authority[k])
            {
                AcNetWire.WriteFree(_own, StampMs());
                Push(k, _own);
                Log("slot " + k + " released.");
            }

            _claim.Reset();
            _listed.Clear();
            for (int j = 0; j < MaxSlots; j++) _remoteBySlot[j] = null;
            _request.End();
        }

        public void Dispose()
        {
            if (_disposed) return;
            try { Stop(); }
            catch (Exception e) { Warn("Net" + _label + ": stopping failed: " + e.Message); }
            _disposed = true;
            try { _t.Dispose(); }
            catch (Exception) { /* the world is going away */ }
        }

        // ------------------------------------------------------------------ for the isolation and the log

        /// <summary>
        /// Access isolation (Game/Access/AcGear): true while that player (Fusion PlayerId = Bridge PhotonId) holds a written slot here - claimed, in the
        /// room and not free. A client that never started the Dreamport (a visitor) never has one (HaloNet.HasRecord verbatim).
        /// </summary>
        public bool HasRecord(int playerId)
        {
            if (playerId < 0 || !Online) return false;
            int localId = LocalId;
            IReadOnlyList<int> present = _t.Present;
            if (playerId == localId || !AcSlotRules.Contains(present, playerId)) return false;
            for (int k = 0; k < _slotCount; k++)
            {
                int[] slot = _slots[k];
                if (_valid[k] && AcNetWire.OwnerOf(slot) == playerId && AcNetWire.StatusOf(slot) != AcNetStatus.Free) return true;
            }

            return false;
        }

        /// <summary>For the log: slots, the own slot, players, ints read / written since the last call.</summary>
        public string StatsLine(double seconds)
        {
            double s = seconds > 0.0 ? seconds : 1.0;
            string line = "Net" + _label + ": stats: " + _slotCount + " slots, mine " + MySlot + ", " + _listed.Count + " other Dreamport player(s), master "
                          + (MasterId < 0 ? "unknown" : MasterId == LocalId ? "me" : "player " + MasterId) + ", " + Math.Round(_writes * 4 / s) + " bytes/s written ("
                          + _writes + " ints in " + Math.Round(s) + " s), " + _reads + " record reads";
            _reads = 0;
            _writes = 0;
            return line;
        }

        internal void Log(string text)
        {
            if (_logs >= 300) return;
            _logs++;
            string line = "Net" + _label + ": " + text + (_logs == 300 ? " (later lines not logged)" : "");
            Action<string> sink = LogSink;
            if (sink != null) sink(line); else AcLog.Info(line);
        }

        private static void Warn(string text) { AcLog.Warn(text); }
    }
}
