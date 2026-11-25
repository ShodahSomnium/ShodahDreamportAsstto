using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The instance master and the blocks it writes (part of <see cref="AcNet"/>, pure C#).
    ///
    /// WHO IS THE INSTANCE MASTER (HALO/Game/Net/Core/HaloNet.Master.cs StepMaster VERBATIM in its rule). Somnium's notion of "host" is the Fusion
    /// shared-mode master client (NetworkRunner.IsSharedModeMasterClient). The upload server's Fusion has no GetMasterClient, so every client publishes
    /// its own IsSharedModeMasterClient as bit 7 of its record and every client reads the same answer from the same records: 1. I am the master when my
    /// runner says so (and I hold a record); 2. else the owner of the first listed record with the bit; 3. else (the room's master is a visitor, or the
    /// bit has not arrived) the Dreamport player with the LOWEST player id (Fusion's ids grow with the join order). Not online: the local player is
    /// alone and is the master.
    ///
    /// THE BLOCKS. Only the master writes "Ac Net World 0..3" (race command, grid, results), "Ac Net Ai 0..8" (AI cars) and "Ac Net Race 0..1" (lights,
    /// clocks, penalties); it asks for their state authority once a second (SceneNetworking hands orphans to the Fusion master, who may be a visitor).
    /// A new master waits <see cref="WriterSettle"/> before its first write (the old one has surely stopped) and starts from what it last READ: the
    /// command keeps its Seq (a new command gets a Seq above every one seen - it grows across a hand-over), AI cars and race state stay as they were.
    /// </summary>
    public sealed partial class AcNet
    {
        /// <summary>A new master writes the blocks only after it has been the master this long (Halo WriterSettle).</summary>
        public const double WriterSettle = 0.5;
        /// <summary>Block write cadence: race control / race state (changes only, plus a forced rewrite) and AI cars (as the own record).</summary>
        public const double BlockEvery = 0.2, BlockForceEvery = 2.0, AiEvery = 1.0 / 30.0;

        private int _masterLogged = int.MinValue;
        private bool _wasMaster;
        private double _masterSince = -1.0;

        /// <summary>The instance master's player id; -1 while not known (not online, or my own record is not claimed yet).</summary>
        public int MasterId { get; private set; } = -1;

        /// <summary>How the master was chosen: "me" (the room's shared-mode master client), "record bit", "lowest id" (or "" while unknown).</summary>
        public string MasterReason { get; private set; } = "";

        /// <summary>This client is the instance master (or alone offline, where it decides everything).</summary>
        public bool IsInstanceMaster { get { return !Online || (MasterId >= 0 && MasterId == LocalId); } }

        /// <summary>Online, but the instance master is not known yet (the claim is not done): choose nothing yet.</summary>
        public bool MasterPending { get { return Online && MasterId < 0; } }

        /// <summary>The master writes its blocks now (online, settled).</summary>
        public bool Writing { get { return Online && IsInstanceMaster && MasterId >= 0 && _masterSince >= 0.0 && Now - _masterSince >= WriterSettle; } }

        /// <summary>Set for one tick when this client became the instance master of an instance with other players (hand-over).</summary>
        public bool BecameMaster { get; private set; }

        private void StepMaster(double now, int localId)
        {
            bool online = Online;
            bool roomMaster = online && _t.IsMaster;
            int master = -1;
            string reason = "";
            if (online && MySlot >= 0)
            {
                if (roomMaster) { master = localId; reason = "me"; }
                else
                {
                    for (int k = 0; k < MaxSlots && master < 0; k++)
                    {
                        Remote r = _remoteBySlot[k];
                        // a record of another protocol (not decoded) still counts by its header: both builds then agree on ONE master and never fight
                        // over the blocks (the other build just cannot read them)
                        if (r != null && (r.Decoded ? r.State.Master : AcNetWire.MasterBitOf(_slots[k]))) { master = r.PlayerId; reason = "record bit"; }
                    }
                }

                if (master < 0)
                {
                    master = localId;
                    reason = "lowest id";
                    for (int k = 0; k < MaxSlots; k++)
                    {
                        Remote r = _remoteBySlot[k];
                        if (r != null && r.PlayerId >= 0 && r.PlayerId < master) master = r.PlayerId;
                    }
                }
            }

            MasterReason = reason;
            if (master != MasterId)
            {
                MasterId = master;
                if (master != _masterLogged)
                {
                    _masterLogged = master;
                    Log(master < 0 ? "the instance master is not known." : "the instance master is " + (master == localId ? "this client" : "player " + master) + " (" + reason + ").");
                }
            }

            bool mine = online && master >= 0 && master == localId;
            BecameMaster = false;
            if (mine && !_wasMaster)
            {
                _masterSince = now;
                TakeOverAsMaster();
                BecameMaster = _listed.Count > 0;
            }
            else if (!mine) _masterSince = -1.0;

            _wasMaster = mine;
        }

        /// <summary>I just became the master: keep commanding from the newest command I read, keep the AI cars, race state, penalties and results.</summary>
        private void TakeOverAsMaster()
        {
            if (_haveCommand)
            {
                _cmdMine.CopyFrom(_command);
                _haveMine = true;
                AcNetBlocks.PackCommand(_cmdMine, _mine[0], _mine[1]);
                // World 0 + 1 rewritten together under my writer id (repairs a command torn by the old master leaving between the two writes)
                _blockUsed[0] = _blockUsed[1] = true;
                _blockCmd[0] = _blockCmd[1] = _command.Seq;
                Log("now the instance master: commanding race command " + _command.Seq + " further.");
            }

            for (int b = 2; b < AcNetSlots.WorldCount; b++) CopyPayload(AcNetSlots.FirstWorld + b, b);
            for (int b = 0; b < AcNetSlots.RaceCount; b++) CopyPayload(AcNetSlots.FirstRace + b, AcNetSlots.WorldCount + b);
            for (int b = 0; b < AcNetSlots.AiCount; b++)
            {
                int k = AcNetSlots.FirstAi + b;
                int[] mine = _aiMine[b];
                if (_valid[k] && AcNetBlocks.AiHeaderOk(_slots[k], b))
                {
                    if (SessionAlive(AcNetBlocks.WriterOf(_slots[k]), _haveCommand ? _command.Seq : 0))
                        Array.Copy(_slots[k], AcNetBlocks.AiHeader, mine, AcNetBlocks.AiHeader, AcNetSlots.Ints - AcNetBlocks.AiHeader);
                    else
                    {
                        // a long-gone session's AI cars: not adopted - the block is written empty (no ghost AI field)
                        Array.Clear(mine, AcNetBlocks.AiHeader, AcNetSlots.Ints - AcNetBlocks.AiHeader);
                        for (int s = 0; s < AcNetBlocks.AiPerBlock; s++) { AiCar a = _ai[b * AcNetBlocks.AiPerBlock + s]; a.Present = false; a.Timeline.Clear(); }
                    }
                }
                _aiDirty[b] = true;
            }

            // iteration 2: the AI damage table (Host 0) - kept (and remembered per index to seed my own rigs of those AI cars), or written empty
            // when it belongs to a long-gone session / another protocol
            Array.Clear(_aiDmgMine, 0, _aiDmgMine.Length);
            for (int i = 0; i < _aiDmgInherited.Length; i++) _aiDmgInherited[i] = false;
            _aiDmgInheritedSeq = _haveCommand ? _command.Seq : 0;  // fixer 2026-11-02
            int kd = AcNetSlots.FirstHost;
            bool hadTable = _hostCount > 0 && _valid[kd] && AcNetBlocks.AiDamageHeaderOk(_slots[kd], 0);
            if (hadTable
                && SessionAlive(AcNetBlocks.AiDamageWriterOf(_slots[kd]), _haveCommand ? _command.Seq : 0))
            {
                Array.Copy(_slots[kd], AcNetBlocks.AiDamageHeader, _aiDmgMine, AcNetBlocks.AiDamageHeader, AcNetBlocks.AiDamageInts - AcNetBlocks.AiDamageHeader);
                int n = 0;
                for (int i = 0; i < AcNetBlocks.MaxAi; i++)
                {
                    int d0, d1;
                    AcNetBlocks.UnpackAiDamage(_aiDmgMine, i, out d0, out d1);
                    AiCar ia = _ai[i];
                    if ((d0 != 0 || d1 != 0) && ia != null && ia.Present) { _aiDmgInherited[i] = true; _aiDmgInheritedCar[i] = ia.Car.CarHash; n++; } // fixer 2026-11-02: only a car that is there to continue
                }
                if (n > 0) Log("inherited the damage of " + n + " AI car(s) from the previous master (Host 0).");
            }
            _aiDmgDirty = hadTable;                                       // (no table = nothing to rewrite: damage 0 sends nothing new)

            for (int i = 0; i < _blockDirty.Length; i++) _blockDirty[i] = true;
        }

        private void CopyPayload(int record, int mineIndex)
        {
            if (!_valid[record] || _slots[record][0] != AcNetBlocks.MagicWorld || ((_slots[record][3] >> 24) & 0xF) != AcNetBlocks.Protocol) return;
            if (!SessionAlive(AcNetBlocks.WriterOf(_slots[record]), AcNetBlocks.CommandSeqOf(_slots[record])))
            {
                // a long-gone session's block: not inherited - overwritten empty (under command 0) so nobody reads it again
                Array.Clear(_mine[mineIndex], AcNetBlocks.Header, AcNetBlocks.PerBlock);
                _blockUsed[mineIndex] = true;
                _blockCmd[mineIndex] = 0;
                if (mineIndex == AcNetSlots.WorldCount) { _raceMine = default(AcNetRaceState); _raceState = _raceMine; _haveRaceState = false; }
                else if (mineIndex == AcNetSlots.WorldCount + 1) { Array.Clear(_penaltiesMine, 0, _penaltiesMine.Length); Array.Clear(_penalties, 0, _penalties.Length); }
                return;
            }

            Array.Copy(_slots[record], AcNetBlocks.Header, _mine[mineIndex], AcNetBlocks.Header, AcNetBlocks.PerBlock);
            _blockUsed[mineIndex] = true;
            _blockCmd[mineIndex] = AcNetBlocks.CommandSeqOf(_slots[record]);
            // what this master now holds as its own copy (it no longer reads the blocks)
            if (mineIndex == AcNetSlots.WorldCount) { _raceMine = AcNetBlocks.UnpackRaceState(_slots[record]); _raceState = _raceMine; _haveRaceState = true; }
            else if (mineIndex == AcNetSlots.WorldCount + 1) { AcNetBlocks.UnpackPenalties(_slots[record], _penaltiesMine); Array.Copy(_penaltiesMine, _penalties, _penalties.Length); }
        }
    }
}
