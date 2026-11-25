using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using SomniumSpace.Network.Bridge;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] The records over the world's Photon Fusion session - HALO/Game/Net/HaloFusionTransport.cs ported: the Community Modules' shared-mode
    /// session every player in the instance is already in (SceneNetworking), the networked objects placed in the scene by hand - "Ac Net Player 0..49",
    /// "Ac Net World 0..3", "Ac Net Ai 0..8", "Ac Net Race 0..1", "Ac Net Host 0..3" (a NetworkObject + NetworkBridgeData64 each; none can be made at run
    /// time), found BY NAME through <see cref="AcNetSlots.RecordOf"/>. Alone, in the editor, before the room is joined or without the slot objects it is
    /// simply not <see cref="Online"/>. Every Fusion access is inside try / catch with throttled logging, and only members other worlds have passed the
    /// upload server's compile with are used (DevTools/fusionapi/check.sh): Object.IsValid / HasStateAuthority / RequestStateAuthority, IntArray
    /// Get / Set / Length, IntCount, Runner.LocalPlayer.PlayerId, Runner.ActivePlayers, SceneNetworking.IsMasterClient.
    /// </summary>
    public sealed class AcFusionTransport : IAcSlotTransport
    {
        private readonly NetworkBridgeData[] _objects = new NetworkBridgeData[AcNetSlots.Total];
        private readonly List<int> _present = new List<int>(64);
        private int _slotCount, _worldCount, _aiCount, _raceCount, _hostCount;
        private bool _complete, _subscribed, _presentRead;
        private int _foundLogged = -1, _blocksLogged = -1;
        private double _nextFind, _presentAt;
        private int _errors;
        private bool _requestRefused;

        private static NetworkRunner Runner { get { return SceneNetworking.NetworkRunnerRef; } }

        public bool Online { get { return SceneNetworking.IsNetworkReady && Runner != null && _slotCount > 0; } }

        public int LocalId
        {
            get
            {
                try { return Runner != null && SceneNetworking.IsNetworkReady ? Runner.LocalPlayer.PlayerId : -1; }
                catch (Exception e) { Warn("reading the local player id", e); return -1; }
            }
        }

        public int SlotCount { get { return _slotCount; } }
        public int WorldCount { get { return _worldCount; } }
        public int AiCount { get { return _aiCount; } }
        public int RaceCount { get { return _raceCount; } }
        public int HostCount { get { return _hostCount; } }

        public bool IsMaster { get { return SceneNetworking.IsMasterClient; } }

        public IReadOnlyList<int> Present { get { return _present; } }

        public void Update(double now)
        {
            try
            {
                bool room = SceneNetworking.IsNetworkReady && Runner != null;
                if (room && !_subscribed)
                {
                    SceneNetworking.OnOtherPlayerJoined += OnRoomPlayersChanged;
                    SceneNetworking.OnOtherPlayerLeft += OnRoomPlayersChanged;
                    _subscribed = true;
                }

                if (room) Find(now);
                RefreshPresent(room, now);
            }
            catch (Exception e)
            {
                Warn("the update", e);
            }
        }

        private void OnRoomPlayersChanged(PlayerRef player)
        {
            _presentAt = 0.0;
        }

        private static bool Valid(NetworkBehaviour behaviour)
        {
            return behaviour != null && behaviour.Object != null && behaviour.Object.IsValid;
        }

        private bool InRange(int record)
        {
            if (record < 0 || record >= AcNetSlots.Total) return false;
            if (record < AcNetSlots.FirstWorld) return record < _slotCount;
            if (record < AcNetSlots.FirstAi) return record - AcNetSlots.FirstWorld < _worldCount;
            if (record < AcNetSlots.FirstRace) return record - AcNetSlots.FirstAi < _aiCount;
            if (record < AcNetSlots.FirstHost) return record - AcNetSlots.FirstRace < _raceCount;
            return record - AcNetSlots.FirstHost < _hostCount;
        }

        public bool IsValid(int record)
        {
            return InRange(record) && Valid(_objects[record]) && SceneNetworking.IsNetworkReady && Runner != null;
        }

        public bool HasAuthority(int record)
        {
            try { return IsValid(record) && _objects[record].Object.HasStateAuthority; }
            catch (Exception e) { Warn("reading the state authority of record " + record, e); return false; }
        }

        public bool Read(int record, int[] into)
        {
            try
            {
                if (!IsValid(record)) return false;
                NetworkArray<int> ints = _objects[record].IntArray;
                int n = Math.Min(ints.Length, into.Length);
                for (int i = 0; i < n; i++) into[i] = ints.Get(i);
                return true;
            }
            catch (Exception e)
            {
                Warn("reading record " + record, e);
                return false;
            }
        }

        /// <summary>Sets the ints that differ (Fusion sends a changed int only); nothing without the object's authority.</summary>
        public int Write(int record, int[] values)
        {
            try
            {
                NetworkBridgeData data = InRange(record) ? _objects[record] : null;
                if (!Valid(data) || !data.Object.HasStateAuthority) return 0;
                NetworkArray<int> ints = data.IntArray;
                int n = Math.Min(ints.Length, values.Length);
                int changed = 0;
                for (int i = 0; i < n; i++)
                {
                    if (ints.Get(i) != values[i]) { ints.Set(i, values[i]); changed++; }
                }

                return changed;
            }
            catch (Exception e)
            {
                Warn("writing record " + record, e);
                return 0;
            }
        }

        public void RequestAuthority(int record)
        {
            try
            {
                NetworkBridgeData data = InRange(record) ? _objects[record] : null;
                if (!Valid(data)) return;
#if FUSION_2_1_OR_NEWER
                // Fusion 2.1: false = not sent (the object lacks AllowStateAuthorityOverride, or no runner) - the claim / block writer asks again in a second
                if (!data.Object.RequestStateAuthority() && !_requestRefused)
                {
                    _requestRefused = true;
                    AcLog.Warn("Net: Fusion refused a state authority request for '" + data.name + "' (record " + record + ") - does the object allow state authority override?");
                }
#else
                data.Object.RequestStateAuthority();
#endif
            }
            catch (Exception e)
            {
                Warn("requesting the state authority of record " + record, e);
            }
        }

        /// <summary>The scene objects by name, once a second until all are found, then every 5 s while some are missing.</summary>
        private void Find(double now)
        {
            if (_complete || now < _nextFind) return;
            _nextFind = now + 1.0;
            int found = 0, blocks = 0, highest = -1;
            foreach (NetworkBridgeData data in UnityEngine.Object.FindObjectsByType<NetworkBridgeData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (data == null) continue;
                string name = data.name;
                int record = AcNetSlots.RecordOf(name);
                if (record < 0) continue;
                if (data.IntCount < AcNetSlots.Ints)
                {
                    AcLog.Warn("Net: '" + name + "' holds " + data.IntCount + " ints, a record needs " + AcNetSlots.Ints + "; use a NetworkBridgeData64.");
                    continue;
                }

                _objects[record] = data;
                if (record < AcNetSlots.FirstWorld) { found++; highest = Math.Max(highest, record); }
                else blocks++;
            }

            _slotCount = Count(AcNetSlots.FirstPlayer, AcNetSlots.PlayerCount);
            _worldCount = Count(AcNetSlots.FirstWorld, AcNetSlots.WorldCount);
            _aiCount = Count(AcNetSlots.FirstAi, AcNetSlots.AiCount);
            _raceCount = Count(AcNetSlots.FirstRace, AcNetSlots.RaceCount);
            _hostCount = Count(AcNetSlots.FirstHost, AcNetSlots.HostCount);
            _complete = _slotCount == AcNetSlots.PlayerCount && _worldCount == AcNetSlots.WorldCount && _aiCount == AcNetSlots.AiCount
                        && _raceCount == AcNetSlots.RaceCount && _hostCount == AcNetSlots.HostCount;
            if (found != _foundLogged || blocks != _blocksLogged)
            {
                _foundLogged = found;
                _blocksLogged = blocks;
                AcLog.Info("Net: " + found + " player slot object(s) found ('" + AcNetSlots.PlayerPrefix + "0.." + highest + "'), " + _slotCount + " usable; world " + _worldCount
                           + ", ai " + _aiCount + ", race " + _raceCount + ", host " + _hostCount + (_complete ? "" : "; still looking every 5 s"));
            }

            if (!_complete) _nextFind = now + 5.0;
        }

        /// <summary>Usable blocks of one kind: counted from the first without a gap (a missing block ends the run, as Halo).</summary>
        private int Count(int first, int max)
        {
            int n = 0;
            while (n < max && _objects[first + n] != null) n++;
            return n;
        }

        private void RefreshPresent(bool room, double now)
        {
            if (!room)
            {
                if (_present.Count > 0) _present.Clear();
                _presentRead = false;
                return;
            }

            if (now < _presentAt) return;
            _presentAt = now + 1.0;
            _present.Clear();
            foreach (PlayerRef player in Runner.ActivePlayers) _present.Add(player.PlayerId);
            _presentRead = true;
        }

        /// <summary>"Fusion session #xxxxxxxx, region R, P of M player(s)" - NetworkRunner.SessionInfo read by reflection (not a proven member;
        /// Fusion's own public API, not the client's - HaloFusionTransport.Describe verbatim).</summary>
        public string Describe()
        {
            try
            {
                var ids = new System.Text.StringBuilder();
                for (int i = 0; i < _present.Count; i++)
                {
                    if (i > 0) ids.Append(", ");
                    ids.Append(_present[i]);
                }

                string session = "Fusion session not readable";
                object info = Prop(Runner, "SessionInfo");
                if (info != null)
                {
                    string name = Prop(info, "Name") as string;
                    object region = Prop(info, "Region"), count = Prop(info, "PlayerCount"), max = Prop(info, "MaxPlayers");
                    session = "Fusion session " + Fingerprint(name) + " (a fingerprint of its name: the same on two players' logs = the same instance), region "
                              + (region ?? "?") + ", " + (count ?? "?") + " of " + (max ?? "?") + " player(s)";
                }

                return session + "; players read " + (_presentRead ? "(ids " + ids + ")" : "not yet");
            }
            catch (Exception e)
            {
                return "Fusion session not readable (" + e.GetType().Name + ")";
            }
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            try
            {
                PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p != null ? p.GetValue(o, null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>FNV-1a (32 bit) of the text as "#xxxxxxxx" ("#none" for null / empty).</summary>
        public static string Fingerprint(string text)
        {
            if (string.IsNullOrEmpty(text)) return "#none";
            uint h = 2166136261u;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                h ^= (uint)(c & 0xFF);
                h *= 16777619u;
                h ^= (uint)(c >> 8);
                h *= 16777619u;
            }

            return "#" + h.ToString("x8");
        }

        private void Warn(string what, Exception e)
        {
            _errors++;
            if (_errors <= 3) AcLog.Warn("Net: Fusion access failed (" + what + ", " + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e.Message);
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            try
            {
                SceneNetworking.OnOtherPlayerJoined -= OnRoomPlayersChanged;
                SceneNetworking.OnOtherPlayerLeft -= OnRoomPlayersChanged;
            }
            catch (Exception) { /* the scene's networking is gone */ }

            _subscribed = false;
        }
    }
}
