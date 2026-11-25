using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The master's blocks (part of <see cref="AcNet"/>): race control in "Ac Net World 0..3" (HaloNetWorld's header and write discipline), AI cars in
    /// "Ac Net Ai 0..8", live race state in "Ac Net Race 0..1". The master publishes (Publish* / SetAi / SetPenalty); everybody reads (Command,
    /// RaceState, Penalty, Results, Ai). A command is complete only when every block it needs carries the same command Seq. Pure C#.
    /// </summary>
    public sealed partial class AcNet
    {
        /// <summary>An AI car of the master, as read from the AI blocks (or as this master wrote it).</summary>
        public sealed class AiCar
        {
            public int Index;
            public bool Present;
            public AcNetCar Car;
            public int Level;
            public string Name = "";
            /// <summary>1 + the content/career/names.txt line of the name (0 = none: use <see cref="Name"/>).</summary>
            public int NameLine;
            /// <summary>A new motion sample arrived this tick.</summary>
            public bool NewSample;
            public readonly AcNetTimeline Timeline = new AcNetTimeline();
            /// <summary>The game layer's own object.</summary>
            public object Tag;

            internal int Seq;
            internal readonly int[] LastMotion = new int[AcNetWire.CMotionLast - AcNetWire.CMotionFirst + 1];
            internal readonly int[] LastName = new int[3];
        }

        // world 0..3 + race 0..1 payloads this master writes (index: world b = b, race b = WorldCount + b)
        private const int MasterBlocks = AcNetSlots.WorldCount + AcNetSlots.RaceCount;
        private readonly int[][] _mine = new int[MasterBlocks][];
        private readonly bool[] _blockDirty = new bool[MasterBlocks];
        /// <summary>A block was ever published (or taken over) - unused blocks are not written; the command Seq each block was published under.</summary>
        private readonly bool[] _blockUsed = new bool[MasterBlocks];
        private readonly int[] _blockCmd = new int[MasterBlocks];
        private readonly int[][] _aiMine = new int[AcNetSlots.AiCount][];
        private readonly bool[] _aiDirty = new bool[AcNetSlots.AiCount];
        /// <summary>The writer of each AI block as last read (int.MinValue = none): a new writer's stamps are on its own process clock, so the cars'
        /// timelines restart when it changes (else a later-started master's lower stamps are all dropped and the cars freeze).</summary>
        private readonly int[] _aiWriter = new int[AcNetSlots.AiCount];
        private readonly AiCar[] _ai = new AiCar[AcNetBlocks.MaxAi];
        // iteration 2 (protocol 3): the AI damage table this master writes into "Ac Net Host 0" (AcNetBlocks.MagicAiDamage), and the pairs a new
        // master inherited from the old one (seeded once into its own rig of that index - AcNetGame.PublishAiCars)
        private readonly int[] _aiDmgMine = new int[AcNetSlots.Ints];
        private bool _aiDmgDirty;
        private readonly bool[] _aiDmgInherited = new bool[AcNetBlocks.MaxAi];
        // fixer 2026-11-02: what an inherited pair belongs to - the inherited AI block's car (NameHash) and the race command of the take-over;
        // the pair seeds only a rig of that car in that session (not a fresh car on the index, not the next session)
        private readonly int[] _aiDmgInheritedCar = new int[AcNetBlocks.MaxAi];
        private int _aiDmgInheritedSeq;

        private readonly AcNetCommand _command = new AcNetCommand();
        private readonly AcNetCommand _cmdMine = new AcNetCommand();
        private readonly AcNetCommand _cmdRead = new AcNetCommand();
        private bool _haveCommand, _haveMine;
        private int _highestSeq;
        /// <summary>The command this client holds was published while OFFLINE (editor, the first second before the slots are found, a reconnect):
        /// its Seq means nothing in the room. On joining it is parked (not current, not seeding the Seq) - re-published with a room Seq if this client
        /// turns out to be the master of a room without a command, else dropped (the room's command wins).</summary>
        private bool _mineOffline, _haveParked;
        private readonly AcNetCommand _parked = new AcNetCommand();

        /// <summary>Set for one tick when a command published offline was re-published into the room (this client is its master, the room had none);
        /// <see cref="TryGetCommand"/> holds it with its new Seq.</summary>
        public bool Republished { get; private set; }

        /// <summary>The room was joined (offline -> online): an offline command is parked, never compared with the room's Seq.</summary>
        private void CommandsWentOnline()
        {
            if (!_haveMine || !_mineOffline) return;
            _parked.CopyFrom(_cmdMine);
            _haveParked = true;
            _haveMine = false;
            _mineOffline = false;
            _haveCommand = false;
            _highestSeq = 0;
            Log("a race command published offline (seq " + _parked.Seq + ") is parked until the room's master is known.");
        }

        /// <summary>The parked offline command once the master is known: the master of a room without a command re-publishes it, anyone else drops it.</summary>
        private void StepParked(bool writer)
        {
            Republished = false;
            if (!_haveParked || !Online || MasterPending) return;
            if (!IsInstanceMaster || _haveCommand)
            {
                _haveParked = false;
                Log("the race command published offline was dropped (" + (_haveCommand ? "the room has command " + _command.Seq : "player " + MasterId + " is the master") + ").");
                return;
            }

            if (!writer) return;                                          // the master settles (and reads the blocks) first
            _haveParked = false;
            int seq = PublishCommand(_parked);
            Republished = seq > 0;
        }

        private AcNetRaceState _raceState, _raceMine;
        private bool _haveRaceState;
        private readonly byte[] _penalties = new byte[AcNetBlocks.Penalties];
        private readonly byte[] _penaltiesMine = new byte[AcNetBlocks.Penalties];
        private readonly AcNetResult[] _results = new AcNetResult[AcNetBlocks.MaxResults];
        private int _resultCount;
        private double _nextBlockWrite, _nextBlockForce, _nextBlockAsk, _nextAiWrite;

        private void InitBlocks()
        {
            for (int i = 0; i < MasterBlocks; i++) _mine[i] = new int[AcNetSlots.Ints];
            for (int i = 0; i < AcNetSlots.AiCount; i++) _aiMine[i] = new int[AcNetSlots.Ints];
            for (int i = 0; i < AcNetSlots.AiCount; i++) _aiWriter[i] = int.MinValue;
            for (int i = 0; i < _ai.Length; i++) _ai[i] = new AiCar { Index = i };
        }

        // ------------------------------------------------------------------ reading

        /// <summary>The newest race command (read, or written by this master); false before any.</summary>
        public bool TryGetCommand(out AcNetCommand command)
        {
            command = _command;
            return _haveCommand;
        }

        /// <summary>Set for one tick when a NEWER command arrived from the network (never for the master's own <see cref="PublishCommand"/>).</summary>
        public bool NewCommand { get; private set; }

        /// <summary>The highest command Seq seen anywhere (blocks, records, own).</summary>
        public int HighestSeq { get { return _highestSeq; } }

        /// <summary>The master's live race state (false when none was published). <see cref="RaceStateSeq"/> = the command Seq it was published under.</summary>
        public bool TryGetRaceState(out AcNetRaceState state)
        {
            state = _raceState;
            return _haveRaceState;
        }

        /// <summary>The command Seq current when the master published the race state / the penalties / the results (R tells stale from current).</summary>
        public int RaceStateSeq { get; private set; }
        public int PenaltiesSeq { get; private set; }
        public int ResultsSeq { get; private set; }

        private void MarkUsed(int i)
        {
            _blockDirty[i] = true;
            _blockUsed[i] = true;
            _blockCmd[i] = _haveCommand ? _command.Seq : 0;
            if (i == 2) ResultsSeq = _blockCmd[i];
            else if (i == AcNetSlots.WorldCount) RaceStateSeq = _blockCmd[i];
            else if (i == AcNetSlots.WorldCount + 1) PenaltiesSeq = _blockCmd[i];
        }

        /// <summary>A penalty: entry = player slot 0..49 or 50 + AI index (seconds 0..254, 255 drive-through).</summary>
        public int Penalty(int entry) { return entry >= 0 && entry < _penalties.Length ? _penalties[entry] : 0; }

        /// <summary>The master's results (count valid entries in <paramref name="into"/>).</summary>
        public int Results(AcNetResult[] into)
        {
            int n = Math.Min(_resultCount, into.Length);
            Array.Copy(_results, into, n);
            return n;
        }

        public int ResultCount { get { return _resultCount; } }

        public AiCar Ai(int index) { return index >= 0 && index < _ai.Length ? _ai[index] : null; }

        // ------------------------------------------------------------------ the master's API

        /// <summary>
        /// The master publishes a race command: its Seq becomes one above every Seq seen (so it grows across a hand-over). Returns the Seq, 0 when this
        /// client may not (not the master, or online with the master not known). Offline the command is only kept (nothing to send).
        /// </summary>
        public int PublishCommand(AcNetCommand c)
        {
            if (c == null || !IsInstanceMaster || MasterPending) return 0;
            int seq = Math.Max(_highestSeq, _haveCommand ? _command.Seq : 0) + 1;
            _cmdMine.CopyFrom(c);
            _cmdMine.Seq = seq;
            _cmdMine.MasterId = LocalId;
            _highestSeq = seq;
            _haveMine = true;
            _mineOffline = !Online;
            _command.CopyFrom(_cmdMine);
            _haveCommand = true;
            AcNetBlocks.PackCommand(_cmdMine, _mine[0], _mine[1]);
            _blockDirty[0] = _blockDirty[1] = true;
            _blockUsed[0] = _blockUsed[1] = true;
            _blockCmd[0] = _blockCmd[1] = seq;
            _nextBlockWrite = 0.0;
            Log("race command " + seq + " published (phase " + c.Phase + ", track " + c.TrackHash.ToString("x8") + ").");
            return seq;
        }

        /// <summary>The master's live race state (start lights on the UTC clock, session clocks).</summary>
        public void PublishRaceState(in AcNetRaceState s)
        {
            if (!IsInstanceMaster) return;
            _raceMine = s;
            _raceState = s;
            _haveRaceState = true;
            AcNetBlocks.PackRaceState(in s, _mine[AcNetSlots.WorldCount]);
            MarkUsed(AcNetSlots.WorldCount);
        }

        /// <summary>The master sets a penalty (entry = player slot or 50 + AI index; seconds, 255 = drive-through, 0 = none).</summary>
        public void SetPenalty(int entry, int value)
        {
            if (!IsInstanceMaster || entry < 0 || entry >= _penaltiesMine.Length) return;
            byte v = (byte)AcNetWire.Clamp(value, 0, 255);
            if (_penaltiesMine[entry] == v) return;
            _penaltiesMine[entry] = v;
            _penalties[entry] = v;
            AcNetBlocks.PackPenalties(_penaltiesMine, _mine[AcNetSlots.WorldCount + 1]);
            MarkUsed(AcNetSlots.WorldCount + 1);
        }

        /// <summary>The master publishes the results (finishers in position order).</summary>
        public void PublishResults(IReadOnlyList<AcNetResult> results, int count)
        {
            if (!IsInstanceMaster || results == null) return;
            int n = Math.Min(count, AcNetBlocks.MaxResults);
            for (int i = 0; i < n; i++) _results[i] = results[i];
            _resultCount = n;
            AcNetBlocks.PackResults(results, n, _mine[2], _mine[3]);
            MarkUsed(2);
            MarkUsed(3);
        }

        /// <summary>The master writes AI car <paramref name="index"/> (0..17).</summary>
        public void SetAi(int index, in AcNetCar car, int level, string name)
        {
            SetAi(index, in car, level, name, 0);
        }

        /// <summary>As above with the names.txt line of the name (1-based, 0 = none; see AcNetBlocks.PackAi).</summary>
        public void SetAi(int index, in AcNetCar car, int level, string name, int nameLine)
        {
            if (!IsInstanceMaster || index < 0 || index >= AcNetBlocks.MaxAi) return;
            int b = index / AcNetBlocks.AiPerBlock, s = index % AcNetBlocks.AiPerBlock;
            AcNetCar c = car;
            c.Present = true;
            if (AcNetBlocks.PackAi(_aiMine[b], s, in c, index, level, name, nameLine)) _aiDirty[b] = true;
            // iteration 2: the car's damage pair goes to the AI damage table (Host 0), not the AI block (full); zeros at DAMAGE 0 never dirty it
            if (AcNetBlocks.PackAiDamage(_aiDmgMine, index, c.DamageD0, c.DamageD1)) _aiDmgDirty = true;
            _aiDmgInherited[index] = false;
            AiCar a = _ai[index];
            a.Present = true; a.Car = c; a.Level = level; a.Name = name ?? ""; a.NameLine = nameLine;
        }

        /// <summary>Iteration 2: a damage pair this client inherited for AI car <paramref name="index"/> when it became the master (from the old
        /// master's "Ac Net Host 0" table) and not yet used - returned once (true), then forgotten. AcNetGame seeds its own new rig with it.
        /// Fixer 2026-11-02: only for the same car (<paramref name="carHash"/> = AcNetWire.NameHash of the rig's car id = the inherited AI
        /// block's car) in the same race command as the take-over; otherwise the pair is dropped (a fresh car / a new session starts clean).</summary>
        public bool TakeInheritedAiDamage(int index, int carHash, out int d0, out int d1)
        {
            d0 = 0; d1 = 0;
            if (index < 0 || index >= AcNetBlocks.MaxAi || !_aiDmgInherited[index]) return false;
            _aiDmgInherited[index] = false;
            if (carHash != _aiDmgInheritedCar[index] || (_haveCommand ? _command.Seq : 0) != _aiDmgInheritedSeq)
            {
                Log("AI car " + (index + 1) + ": the inherited damage is not used (another car or another session on that index).");
                return false;
            }
            AcNetBlocks.UnpackAiDamage(_aiDmgMine, index, out d0, out d1);
            return d0 != 0 || d1 != 0;
        }

        /// <summary>The AI damage table as this master holds it (lab / harness; do not modify).</summary>
        public int[] AiDamageTable { get { return _aiDmgMine; } }

        /// <summary>The master removes AI car <paramref name="index"/>.</summary>
        public void ClearAi(int index)
        {
            if (!IsInstanceMaster || index < 0 || index >= AcNetBlocks.MaxAi) return;
            int b = index / AcNetBlocks.AiPerBlock, s = index % AcNetBlocks.AiPerBlock;
            int at = AcNetBlocks.AiHeader + s * AcNetBlocks.AiCarInts;
            bool was = false;
            for (int i = 0; i < AcNetBlocks.AiCarInts; i++) was |= _aiMine[b][at + i] != 0;
            if (was) { AcNetBlocks.ClearAi(_aiMine[b], s); _aiDirty[b] = true; }
            if (AcNetBlocks.PackAiDamage(_aiDmgMine, index, 0, 0)) _aiDmgDirty = true;
            _aiDmgInherited[index] = false;
            _ai[index].Present = false;
        }

        // ------------------------------------------------------------------ the frame's block step

        private void StepBlocks(double now, int localId)
        {
            NewCommand = false;
            bool writer = Writing;
            ReadCommand(writer);
            StepParked(writer);
            ReadRaceBlocks(writer);
            ReadAi(now, writer);
            if (!Online || !IsInstanceMaster || MasterId < 0) return;

            if (now >= _nextBlockAsk)
            {
                _nextBlockAsk = now + 1.0;
                for (int k = AcNetSlots.FirstWorld; k < AcNetSlots.FirstHost; k++)
                    if (_valid[k] && !_authority[k]) _t.RequestAuthority(k);
                int kd = AcNetSlots.FirstHost;                            // iteration 2: Host 0 = the AI damage table
                if (_hostCount > 0 && _valid[kd] && !_authority[kd]) _t.RequestAuthority(kd);
            }

            if (!writer) return;
            int stamp = OwnStampMs >= 0 ? OwnStampMs : StampMs();
            if (now >= _nextAiWrite)
            {
                _nextAiWrite = now + AiEvery;
                for (int b = 0; b < _aiCount; b++)
                {
                    int k = AcNetSlots.FirstAi + b;
                    if (!_aiDirty[b]) continue;
                    if (!_valid[k] || !_authority[k]) continue;
                    _aiDirty[b] = false;
                    AcNetBlocks.WriteAiHeader(_aiMine[b], b, localId, stamp);
                    Push(k, _aiMine[b]);
                }
                int kd = AcNetSlots.FirstHost;
                if (_aiDmgDirty && _hostCount > 0 && _valid[kd] && _authority[kd])
                {
                    _aiDmgDirty = false;
                    AcNetBlocks.WriteAiDamageHeader(_aiDmgMine, 0, localId, stamp);
                    Push(kd, _aiDmgMine);
                }
            }

            if (now < _nextBlockWrite) return;
            _nextBlockWrite = now + BlockEvery;
            bool force = now >= _nextBlockForce;
            if (force) _nextBlockForce = now + BlockForceEvery;
            AcNetCommand cmd = _haveMine ? _cmdMine : null;
            for (int i = 0; i < MasterBlocks; i++)
            {
                int k = i < AcNetSlots.WorldCount ? AcNetSlots.FirstWorld + i : AcNetSlots.FirstRace + (i - AcNetSlots.WorldCount);
                if (!_valid[k] || !_authority[k]) { _blockDirty[i] = true; continue; }
                if (cmd == null && i < 2) continue;                       // no command yet: nothing to say in World 0/1
                if (!_blockUsed[i]) continue;                             // never published: leave the block alone
                bool dirty = _blockDirty[i];
                if (!dirty && !force) continue;
                _blockDirty[i] = false;
                int block = i < AcNetSlots.WorldCount ? i : i - AcNetSlots.WorldCount;
                int kind = i < AcNetSlots.WorldCount ? AcNetBlocks.KindWorld : AcNetBlocks.KindRace;
                int cmdSeq = i < 2 ? cmd.Seq : _blockCmd[i];
                AcNetBlocks.WriteHeader(_mine[i], block, kind, localId, StampMs(), cmd != null ? cmd.TrackHash : 0, cmdSeq, cmd != null ? cmd.StartSec : 0, dirty);
                Push(k, _mine[i]);
            }
        }

        private void ReadCommand(bool writer)
        {
            // the highest Seq anyone has seen (records' applied Seq, block headers) - a new master's next command goes above it
            for (int i = 0; i < _listed.Count; i++) if (_listed[i].State.AppliedSeq > _highestSeq) _highestSeq = _listed[i].State.AppliedSeq;
            if (Own.AppliedSeq > _highestSeq) _highestSeq = Own.AppliedSeq;
            int k0 = AcNetSlots.FirstWorld, k1 = AcNetSlots.FirstWorld + 1;
            if (!_valid[k0]) return;
            int[] w0 = _slots[k0];
            if (!AcNetBlocks.HeaderOk(w0, 0, AcNetBlocks.KindWorld)) return;
            int seq = AcNetBlocks.CommandSeqOf(w0);
            if (seq > _highestSeq) _highestSeq = seq;
            if (writer && IsInstanceMaster) return;                       // the master's own command is _command already
            if (seq <= 0) return;
            bool need1 = AcNetBlocks.NeedsWorld1(w0);
            bool have1 = _worldCount > 1 && _valid[k1] && AcNetBlocks.HeaderOk(_slots[k1], 1, AcNetBlocks.KindWorld) && AcNetBlocks.CommandSeqOf(_slots[k1]) == seq;
            if (need1 && !have1 && _worldCount > 1) return;              // wait for the rest of it
            if (_haveCommand && seq == _command.Seq) return;
            int writerId = AcNetBlocks.WriterOf(w0);
            if (_haveCommand && seq < _command.Seq && writerId != MasterId) return;   // an old block of a former master
            if (!SessionAlive(writerId, seq)) { NoteStale(seq); return; }   // a long-gone session (visitors kept the room)
            if (!AcNetBlocks.UnpackCommand(w0, have1 ? _slots[k1] : null, _cmdRead)) return;
            if (_cmdRead.MasterId < 0) _cmdRead.MasterId = writerId;
            _command.CopyFrom(_cmdRead);
            _haveCommand = true;
            NewCommand = true;
        }

        private bool _staleLogged;

        /// <summary>
        /// A master block still belongs to a live session: its writer is me or still in the room, or someone listed (or I) holds the command Seq it
        /// was written under. Nothing clears the blocks when the last Dreamport player leaves, and visitors can keep the room (and the blocks) alive
        /// for hours: a block with nobody behind it is a long-gone session - never adopted (command, AI field, race state, clock beacon). While the
        /// room's player list is not known yet every block counts as alive.
        /// </summary>
        private bool SessionAlive(int writer, int seq)
        {
            if (writer < 0 || writer == LocalId || !Online) return true;
            IReadOnlyList<int> present = _t.Present;
            if (present == null || present.Count == 0) return true;
            for (int i = 0; i < present.Count; i++) if (present[i] == writer) return true;
            if (seq <= 0) return false;
            if (_haveCommand && _command.Seq == seq) return true;            // I am in that session (read it while its writer was here)
            for (int i = 0; i < _listed.Count; i++) if (_listed[i].State.AppliedSeq == seq) return true;
            return false;
        }

        private bool BlockAlive(int[] rec)
        {
            if (SessionAlive(AcNetBlocks.WriterOf(rec), AcNetBlocks.CommandSeqOf(rec))) return true;
            NoteStale(AcNetBlocks.CommandSeqOf(rec));
            return false;
        }

        private void NoteStale(int seq)
        {
            if (_staleLogged) return;
            _staleLogged = true;
            Log("master blocks of race command " + seq + " are from a session nobody is in any more (their writer left) - ignored, not adopted (an instance kept alive by visitors).");
        }

        private void ReadRaceBlocks(bool writer)
        {
            // valid whenever the master wrote them; the command Seq they were published under is passed on (R tells stale from current)
            if (writer && IsInstanceMaster) return;
            int kr0 = AcNetSlots.FirstRace, kr1 = AcNetSlots.FirstRace + 1;
            if (_raceCount > 0 && _valid[kr0] && AcNetBlocks.HeaderOk(_slots[kr0], 0, AcNetBlocks.KindRace) && BlockAlive(_slots[kr0]))
            {
                _raceState = AcNetBlocks.UnpackRaceState(_slots[kr0]);
                _haveRaceState = true;
                RaceStateSeq = AcNetBlocks.CommandSeqOf(_slots[kr0]);
            }
            else _haveRaceState = false;

            if (_raceCount > 1 && _valid[kr1] && AcNetBlocks.HeaderOk(_slots[kr1], 1, AcNetBlocks.KindRace) && BlockAlive(_slots[kr1]))
            {
                AcNetBlocks.UnpackPenalties(_slots[kr1], _penalties);
                PenaltiesSeq = AcNetBlocks.CommandSeqOf(_slots[kr1]);
            }
            else Array.Clear(_penalties, 0, _penalties.Length);

            int k2 = AcNetSlots.FirstWorld + 2, k3 = AcNetSlots.FirstWorld + 3;
            if (_worldCount > 2 && _valid[k2] && AcNetBlocks.HeaderOk(_slots[k2], 2, AcNetBlocks.KindWorld) && BlockAlive(_slots[k2]))
            {
                int rseq = AcNetBlocks.CommandSeqOf(_slots[k2]);
                bool have3 = _worldCount > 3 && _valid[k3] && AcNetBlocks.HeaderOk(_slots[k3], 3, AcNetBlocks.KindWorld) && AcNetBlocks.CommandSeqOf(_slots[k3]) == rseq;
                _resultCount = AcNetBlocks.UnpackResults(_slots[k2], _slots[k3], have3, _results);
                ResultsSeq = rseq;
            }
            else _resultCount = 0;
        }

        private void ReadAi(double now, bool writer)
        {
            bool mine = writer && IsInstanceMaster;
            // iteration 2: the master's AI damage table in Host 0 (absent / another protocol / a gone session = undamaged)
            int kd = AcNetSlots.FirstHost;
            int[] dmg = !mine && _hostCount > 0 && _valid[kd] && AcNetBlocks.AiDamageHeaderOk(_slots[kd], 0)
                        && SessionAlive(AcNetBlocks.AiDamageWriterOf(_slots[kd]), _haveCommand ? _command.Seq : 0) ? _slots[kd] : null;
            for (int b = 0; b < AcNetSlots.AiCount; b++)
            {
                int k = AcNetSlots.FirstAi + b;
                int[] rec = mine ? _aiMine[b] : _slots[k];
                bool ok = mine || (b < _aiCount && _valid[k] && AcNetBlocks.AiHeaderOk(rec, b) && SessionAlive(AcNetBlocks.WriterOf(rec), _haveCommand ? _command.Seq : 0));
                int stamp = rec[2];
                if (mine) _aiWriter[b] = int.MinValue;
                else if (ok)
                {
                    int w = AcNetBlocks.WriterOf(rec);
                    if (w != _aiWriter[b])
                    {
                        if (_aiWriter[b] != int.MinValue)
                            for (int s = 0; s < AcNetBlocks.AiPerBlock; s++) _ai[b * AcNetBlocks.AiPerBlock + s].Timeline.Clear();
                        _aiWriter[b] = w;
                    }
                }

                for (int s = 0; s < AcNetBlocks.AiPerBlock; s++)
                {
                    AiCar a = _ai[b * AcNetBlocks.AiPerBlock + s];
                    a.NewSample = false;
                    int at = AcNetBlocks.AiHeader + s * AcNetBlocks.AiCarInts;
                    bool present = ok && (rec[at + AcNetWire.CDoors] >> 24 & 1) != 0;
                    if (!present)
                    {
                        if (a.Present && !mine) { a.Present = false; a.Timeline.Clear(); }
                        continue;
                    }

                    if (mine) continue;                                       // SetAi already holds it
                    AcNetWire.UnpackCar(rec, at, out a.Car);
                    AcNetBlocks.UnpackAiDamage(dmg, a.Index, out a.Car.DamageD0, out a.Car.DamageD1);   // zeros when dmg is null
                    a.Present = true;
                    a.Level = (rec[at + 26] >> 8) & 0xFF;
                    a.NameLine = (rec[at + 26] >> 16) & 0xFF;
                    bool nameChanged = false;
                    for (int i = 0; i < 3; i++) if (a.LastName[i] != rec[at + 27 + i]) { a.LastName[i] = rec[at + 27 + i]; nameChanged = true; }
                    if (nameChanged) a.Name = AcNetWire.UnpackText(rec, at + 27, 3);
                    bool moved = false;
                    for (int i = 0; i < a.LastMotion.Length; i++)
                    {
                        int v = rec[at + AcNetWire.CMotionFirst + i];
                        if (a.LastMotion[i] != v) { a.LastMotion[i] = v; moved = true; }
                    }

                    if (moved || a.Timeline.Count == 0)
                    {
                        a.Seq++;
                        a.NewSample = true;
                        a.Timeline.Add(in a.Car, a.Seq, stamp, now);
                    }
                }
            }
        }
    }
}
