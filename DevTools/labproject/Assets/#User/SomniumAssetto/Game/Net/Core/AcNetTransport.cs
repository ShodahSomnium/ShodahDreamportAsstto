using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The scene's networked int records as AcNet sees them (IHaloSlotTransport ported; record numbers = <see cref="AcNetSlots"/>): 0..49 the player
    /// slots ("Ac Net Player k"), 50..53 the master's race-control blocks ("Ac Net World b"), 54..62 the AI blocks, 63..64 the race-state blocks,
    /// 65..68 the host blocks. Every client can read every record; only the client holding a record's state authority can write it.
    /// AcFusionTransport (Game/Net) is the real thing over the world's Photon Fusion session; <see cref="AcLoopbackTransport"/> an in-memory stand-in
    /// with Fusion's rules for the harness and the lab. Pure C#.
    /// </summary>
    public interface IAcSlotTransport
    {
        /// <summary>In a room with a runner and the slot objects found.</summary>
        bool Online { get; }

        /// <summary>This client's player id (PlayerRef.PlayerId = ISomniumPlayer.Properties.PhotonId), -1 when not in a room.</summary>
        int LocalId { get; }

        /// <summary>Usable player slot objects, 0..50 (records 0..SlotCount-1, counted from 0 without a gap).</summary>
        int SlotCount { get; }

        /// <summary>Usable blocks of each master kind (counted from block 0 without a gap).</summary>
        int WorldCount { get; }
        int AiCount { get; }
        int RaceCount { get; }
        int HostCount { get; }

        /// <summary>The room's shared-mode master client (Fusion's notion; the instance master is decided from the records, see AcNet.Master).</summary>
        bool IsMaster { get; }

        /// <summary>The ids of the players in the room (refreshed about once a second and on joins / leaves).</summary>
        IReadOnlyList<int> Present { get; }

        /// <summary>Once per frame, before any read: finds the objects, refreshes the player list, delivers what the network brought.</summary>
        void Update(double now);

        bool IsValid(int record);

        bool HasAuthority(int record);

        /// <summary>Copies the record's ints (up to <paramref name="into"/>.Length) into <paramref name="into"/>; false when the record is not attached.</summary>
        bool Read(int record, int[] into);

        /// <summary>Sets the ints that differ (nothing without the record's authority); returns how many changed.</summary>
        int Write(int record, int[] values);

        void RequestAuthority(int record);

        /// <summary>A line for the log: the session, who is in it.</summary>
        string Describe();

        void Dispose();
    }

    /// <summary>
    /// The shared memory behind <see cref="AcLoopbackTransport"/>s: the 69 records, their authority holders and the room's player list
    /// (HaloLoopbackWorld ported). Fusion's rules, reduced: a state authority request is granted after <see cref="RequestDelay"/> (the last request
    /// wins), writes by the authority holder reach the other readers at the next <see cref="SendEvery"/> tick (changes between two ticks arrive
    /// together), a leaving player's records lose their authority and the room's master takes them <see cref="OrphanDelay"/> later
    /// (SceneNetworking's ReassignNullObjectsAuthority). The block counts can be lowered to simulate a scene without some objects.
    /// </summary>
    public sealed class AcLoopbackWorld
    {
        public const int Records = AcNetSlots.Total;

        public double RequestDelay = 0.12, SendEvery = 1.0 / 16.0, OrphanDelay = 0.4;
        public int SlotBlocks = AcNetSlots.PlayerCount, WorldBlocks = AcNetSlots.WorldCount, AiBlocks = AcNetSlots.AiCount, RaceBlocks = AcNetSlots.RaceCount,
            HostBlocks = AcNetSlots.HostCount;

        private readonly int[][] _live = new int[Records][];
        private readonly int[][] _sent = new int[Records][];
        private readonly int[] _authority = new int[Records];
        private readonly int[] _pendingBy = new int[Records];
        private readonly double[] _pendingAt = new double[Records];
        private readonly double[] _orphanAt = new double[Records];
        private readonly List<int> _present = new List<int>();
        private double _nextSend;
        private double _lastStep = double.NegativeInfinity;

        /// <summary>Counters for tests.</summary>
        public int Sends, WritesChanged;

        /// <summary>Test hook: the room's shared-mode master (-1 = the lowest id present, Fusion's oldest client). Set it to a visitor's id to
        /// test the "the room's master is not a Dreamport player" case.</summary>
        public int MasterOverride = -1;

        public AcLoopbackWorld()
        {
            for (int k = 0; k < Records; k++)
            {
                _live[k] = new int[AcNetSlots.Ints];
                _sent[k] = new int[AcNetSlots.Ints];
                _authority[k] = -1;
                _pendingBy[k] = -1;
                _orphanAt[k] = -1.0;
            }
        }

        public IReadOnlyList<int> Present { get { return _present; } }

        /// <summary>The room's master client: the lowest id present (Fusion's shared-mode master is the oldest client).</summary>
        public int Master
        {
            get
            {
                if (MasterOverride >= 0 && _present.Contains(MasterOverride)) return MasterOverride;
                int m = int.MaxValue;
                for (int i = 0; i < _present.Count; i++) if (_present[i] < m) m = _present[i];
                return m == int.MaxValue ? -1 : m;
            }
        }

        /// <summary>A player enters the room and gets a view of the records.</summary>
        public AcLoopbackTransport Join(int playerId, Func<double> clock)
        {
            if (!_present.Contains(playerId)) _present.Add(playerId);
            return new AcLoopbackTransport(this, playerId, clock);
        }

        /// <summary>A player who is in the room without running the Dreamport (a visitor): present, never writes.</summary>
        public void JoinVisitor(int playerId)
        {
            if (!_present.Contains(playerId)) _present.Add(playerId);
        }

        /// <summary>A player leaves (or is cut off): its records become authority-less and the master takes them over a moment later.</summary>
        public void Leave(int playerId)
        {
            _present.Remove(playerId);
            for (int k = 0; k < Records; k++)
            {
                if (_authority[k] == playerId) { _authority[k] = -1; _orphanAt[k] = double.NaN; }
                if (_pendingBy[k] == playerId) _pendingBy[k] = -1;
            }
        }

        public int AuthorityOf(int record) { return _authority[record]; }

        public int[] LiveOf(int record) { return _live[record]; }

        public bool InScene(int record)
        {
            if (record < 0 || record >= Records) return false;
            if (record < AcNetSlots.FirstWorld) return record - AcNetSlots.FirstPlayer < SlotBlocks;
            if (record < AcNetSlots.FirstAi) return record - AcNetSlots.FirstWorld < WorldBlocks;
            if (record < AcNetSlots.FirstRace) return record - AcNetSlots.FirstAi < AiBlocks;
            if (record < AcNetSlots.FirstHost) return record - AcNetSlots.FirstRace < RaceBlocks;
            return record - AcNetSlots.FirstHost < HostBlocks;
        }

        internal void Step(double now)
        {
            if (now <= _lastStep) return;      // several clients share the world: step it once per moment
            _lastStep = now;
            for (int k = 0; k < Records; k++)
            {
                if (_pendingBy[k] >= 0 && now >= _pendingAt[k])
                {
                    if (_present.Contains(_pendingBy[k])) _authority[k] = _pendingBy[k];
                    _pendingBy[k] = -1;
                }

                if (_authority[k] < 0 && _present.Count > 0)
                {
                    if (double.IsNaN(_orphanAt[k]) || _orphanAt[k] < 0.0) _orphanAt[k] = now + OrphanDelay;
                    else if (now >= _orphanAt[k]) { _authority[k] = Master; _orphanAt[k] = -1.0; }
                }
                else if (_authority[k] >= 0) _orphanAt[k] = -1.0;
            }

            if (now >= _nextSend)
            {
                _nextSend = now + SendEvery;
                Sends++;
                for (int k = 0; k < Records; k++) Array.Copy(_live[k], _sent[k], AcNetSlots.Ints);
            }
        }

        internal void Request(int record, int playerId, double now)
        {
            _pendingBy[record] = playerId;
            _pendingAt[record] = now + RequestDelay;
        }

        internal int[] SentOf(int record) { return _sent[record]; }

        internal int Write(int record, int[] values)
        {
            int[] live = _live[record];
            int n = Math.Min(AcNetSlots.Ints, values.Length);
            int changed = 0;
            for (int i = 0; i < n; i++)
                if (live[i] != values[i]) { live[i] = values[i]; changed++; }
            WritesChanged += changed;
            return changed;
        }
    }

    /// <summary>One player's view of an <see cref="AcLoopbackWorld"/>: its own records live, everyone else's as last "sent".</summary>
    public sealed class AcLoopbackTransport : IAcSlotTransport
    {
        private readonly AcLoopbackWorld _world;
        private readonly int _id;
        private readonly Func<double> _clock;
        private bool _gone;

        /// <summary>Test hook: false = this client's network is down (nothing read, nothing written, no authority).</summary>
        public bool Connected = true;

        internal AcLoopbackTransport(AcLoopbackWorld world, int id, Func<double> clock)
        {
            _world = world;
            _id = id;
            _clock = clock;
        }

        public bool Online { get { return Connected && !_gone; } }

        public int LocalId { get { return Online ? _id : -1; } }

        public int SlotCount { get { return Online ? _world.SlotBlocks : 0; } }
        public int WorldCount { get { return Online ? _world.WorldBlocks : 0; } }
        public int AiCount { get { return Online ? _world.AiBlocks : 0; } }
        public int RaceCount { get { return Online ? _world.RaceBlocks : 0; } }
        public int HostCount { get { return Online ? _world.HostBlocks : 0; } }

        public bool IsMaster { get { return _world.Master == _id; } }

        public IReadOnlyList<int> Present { get { return _world.Present; } }

        public void Update(double now)
        {
            if (!Online) return;
            _world.Step(now);
        }

        public bool IsValid(int record) { return Online && _world.InScene(record); }

        public bool HasAuthority(int record) { return IsValid(record) && _world.AuthorityOf(record) == _id; }

        public bool Read(int record, int[] into)
        {
            if (!IsValid(record)) return false;
            int[] from = _world.AuthorityOf(record) == _id ? _world.LiveOf(record) : _world.SentOf(record);
            Array.Copy(from, into, Math.Min(into.Length, AcNetSlots.Ints));
            return true;
        }

        public int Write(int record, int[] values)
        {
            if (!HasAuthority(record)) return 0;
            return _world.Write(record, values);
        }

        public void RequestAuthority(int record)
        {
            if (IsValid(record)) _world.Request(record, _id, _clock());
        }

        public string Describe()
        {
            return "loopback world, " + _world.Present.Count + " player(s) in it, this client " + _id + ", blocks " + _world.SlotBlocks + "/" + _world.WorldBlocks + "/"
                   + _world.AiBlocks + "/" + _world.RaceBlocks + "/" + _world.HostBlocks;
        }

        /// <summary>The client leaves the room.</summary>
        public void Dispose()
        {
            if (_gone) return;
            _gone = true;
            _world.Leave(_id);
        }
    }
}
