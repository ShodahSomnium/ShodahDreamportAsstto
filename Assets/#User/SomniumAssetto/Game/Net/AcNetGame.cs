using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[N, Net] Another Dreamport player in the instance, as read from their "Ac Net Player" record (or, with <see cref="IsAi"/>, one of the
    /// master's AI cars read from the "Ac Net Ai" blocks). The object stays the same while the player is listed (no per-frame allocation).</summary>
    public sealed class AcNetPlayer
    {
        public int Slot = -1;
        /// <summary>Fusion PlayerId == Bridge Properties.PhotonId (AI: -(100 + index), the AcRaceCommand.Grid convention).</summary>
        public int PhotonId = -1;
        public string Name = "";
        public string CarId = "";
        public string SkinId = "";
        /// <summary>On the current track (not in the hub).</summary>
        public bool OnTrack;
        /// <summary>Sits in a car (driver seat of their own car, or passenger of <see cref="RideInPhotonId"/>'s car).</summary>
        public bool Seated;
        /// <summary>Photon id of the car owner they ride with as passenger (-1 = none).</summary>
        public int RideInPhotonId = -1;
        public int Laps;
        public int BestMs, LastMs, LapMs;
        public int Sector;
        public bool LapValid = true;
        /// <summary>Race distance in laps (laps + progress, research T3).</summary>
        public float RaceDistance;
        public bool InPitlane;
        public bool Finished;
        /// <summary>Total race time when finished (ms).</summary>
        public int FinishedMs;
        public bool IsMaster;
        public bool Vr;
        /// <summary>Their drawn car (null when not on my track / not built yet).</summary>
        public AcRemoteCar Car;

        // ---- added by N (additive) ----
        /// <summary>In the driver seat of their own car.</summary>
        public bool Driving;
        /// <summary>Lobby "ready" / spectating (set by their F through AcNetGame.Ready / Spectating).</summary>
        public bool Ready, Spectating;
        /// <summary>Their race position as they computed it (1-based, 0 = none), penalty ms, cuts this lap, current sector index.</summary>
        public int Position, PenaltyMs, Cuts;
        /// <summary>Iteration 2 (protocol 3): their car's raw damage pair (AcCarDamage.ToWire; 0 0 = undamaged; d1 bit 31 = retired, so
        /// <c>DamageD1 &lt; 0</c> = retired). Players: record ints 42-43; AI cars: the master's "Ac Net Host 0" table.</summary>
        public int DamageD0, DamageD1;
        /// <summary>Retired from the running race (engine blown): d1 bit 31.</summary>
        public bool Retired { get { return DamageD1 < 0; } }
        /// <summary>Sector times of their last completed lap (ms; up to 3).</summary>
        public readonly int[] LastSectorMs = new int[3];
        /// <summary>NameHash of the layout key they are on (0 = hub); <see cref="OnTrack"/> = the same as mine.</summary>
        public int LayoutHash;
        /// <summary>They have a car out (parked in their pit box or driving).</summary>
        public bool HasCar;
        /// <summary>Their car is installed here (else a grey stand-in is drawn).</summary>
        public bool CarInstalled;
        /// <summary>Who rides in THEIR passenger seat (photon id, -1 none), and whether their car has one.</summary>
        public int PassengerPhotonId = -1;
        public bool HasPassengerSeat;
        /// <summary>The race command Seq they applied last.</summary>
        public int AppliedSeq;
        /// <summary>Their car's speed (km/h) and gear as last read.</summary>
        public float SpeedKmh;
        public int Gear;
        /// <summary>One of the master's AI cars (index <see cref="AiIndex"/>), not a human.</summary>
        public bool IsAi;
        public int AiIndex = -1;
        /// <summary>Their AI level (AI only).</summary>
        public int AiLevel;

        internal int CarHashSeen = int.MinValue, SkinHashSeen = int.MinValue;
        internal AcContent CatalogSeen;
        internal string AiNameSeen;
        internal int AiNameLineSeen = -1, AiNameCarSeen = int.MinValue, AiNameSkinSeen = int.MinValue;
    }

    /// <summary>[N, Net] A remote player's (or remote AI) car drawn here: C's model via P's AcCarRig (Kind Remote = kinematic puppet, collider
    /// on Car layer - the local car feels it one-sided), A's exterior engine sound fed from the record, interpolated on AcNetTimeline.</summary>
    public sealed class AcRemoteCar
    {
        public AcNetPlayer Player;
        /// <summary>The built car (null while building; a stand-in box meanwhile).</summary>
        public AcCarRig Rig;
        /// <summary>Last decoded state shown (telemetry for audio / instruments).</summary>
        public AcTelemetry Shown;

        // ---- added by N (additive) ----
        /// <summary>The drawn pose (the car root at the Rigidbody's CG): the rig's root when built, else the stand-in's frame. Riders are put on it.</summary>
        public Transform Frame { get { return Rig != null && Rig.Root != null ? Rig.Root.transform : FrameStandIn; } }
        /// <summary>The holder object (stand-in frame + box); destroyed with the car.</summary>
        public GameObject Holder;
        internal Transform FrameStandIn;
        /// <summary>Drawn velocity (m/s) - a kinematic puppet's own Rigidbody reads zero.</summary>
        public Vector3 Velocity;
        /// <summary>Hidden beyond the draw radius (posed, not shown).</summary>
        public bool Hidden;
        /// <summary>Passing through the local car now (pit lane / just placed and fast relative to me).</summary>
        public bool Ghost;
        /// <summary>The car cannot be built here (not installed): the grey box stays.</summary>
        public bool StandInOnly;
        /// <summary>Sampled at least once.</summary>
        public bool Drawn;
        public string CarId = "", SkinId = "";
        internal AcCarEntry Entry;
        internal bool Building, Wanted, Disposed;
        internal float StandInSince;
        internal GameObject Box;
        internal AcCarAudio BoxAudio;
        /// <summary>Fixer 2026-11-02: the stand-in box's drawn velocity / ghost state for P's damage hit (null without a box).</summary>
        internal AcRemoteContact BoxContact;
        internal Collider[] Colliders;
        internal readonly float[] Spin = new float[4];
        internal float DoorL = -1f, DoorR = -1f;
        internal float SnapAt = -100f;
        internal int LastEpoch = -1;
        /// <summary>Its samples (the player's record timeline or the AI car's).</summary>
        internal AcNetTimeline Timeline;

        // ---- added by N, second pass (additive) ----
        /// <summary>The drawn rig was built with the high-res cockpit (COCKPIT_HR, instruments): rebuilt so when I walk up to the parked car to ride
        /// along (C's ForKind(Remote) has no interior), or while I ride in it when S plugs <see cref="AcNetGame.SwapSeatedCar"/>.</summary>
        public bool Interior;
        /// <summary>A stand-in or a rig is drawn and has a collider (the box is solid too, with the same ghost rule) - R / I: test THIS (and
        /// <see cref="Frame"/>) for an occupied pit box / grid slot, not <see cref="Rig"/>.</summary>
        public bool Occupies { get { return Drawn && !Hidden && !Disposed; } }
        /// <summary>Door sounds started on this car (A's Door on each open / close edge of the record) and the horn as last sent to A (lab).</summary>
        public int DoorSounds { get; internal set; }
        public bool HornOn { get; internal set; }
        internal bool WantInterior, InteriorFailed, BuildingInterior;
        internal float AudioDoorL = -1f, AudioDoorR = -1f;

        // ---- iteration 2 (damage, protocol 3) ----
        /// <summary>The damage pair last applied to <see cref="DamageRig"/> (re-applied when the pair or the rig changes: a cockpit rebuild,
        /// a stand-in replaced by the model).</summary>
        public int DamageD0 { get; internal set; }
        public int DamageD1 { get; internal set; }
        internal AcCarRig DamageRig;
        /// <summary>CarDamaged raised for this remote car (lab / diagnostics).</summary>
        public int DamageEvents { get; internal set; }

        /// <summary>One line of state for the lab / diagnostics (allocates; never per frame).</summary>
        public string Describe()
        {
            AcNetTimeline t = Timeline;
            return "car " + CarId + (Disposed ? " DISPOSED" : "") + (Rig != null ? " rig" : " no rig") + (Hidden ? " hidden" : "") + (Ghost ? " ghost" : "")
                   + " drawn " + Drawn + ", timeline " + (t != null ? t.Count + " samples, last arrival " + t.LastArrival.ToString("0.00") : "none")
                   + ", door sounds " + DoorSounds + " (audio door L " + AudioDoorL.ToString("0.00") + "), horn " + HornOn
                   + ", frame at " + (Frame != null ? Frame.position.ToString("F1") : "-") + ", snap at " + SnapAt.ToString("0.0") + " epoch " + LastEpoch;
        }
    }

    /// <summary>[N, Net] Race control through the master's "Ac Net World" blocks: the newest command everyone applies, and the master's write.</summary>
    public sealed class AcRaceControl
    {
        /// <summary>The newest command read (or written), null before any.</summary>
        public AcRaceCommand Current { get; private set; }
        /// <summary>A newer command arrived (R applies it via AcRace.Apply).</summary>
        public event Action<AcRaceCommand> Changed;

        /// <summary>Master only: publishes <paramref name="command"/> (Seq is raised above any seen). False when not master. Offline: applied locally.</summary>
        public bool Publish(AcRaceCommand command)
        {
            if (command == null || !AcNetGame.IsMaster) return false;
            AcRaceCommand c = command.Clone();
            int seq = AcNetGame.PublishCommand(c);
            if (seq <= 0)
            {
                // the net layer is not up (or refused): the stub's local behaviour
                if (AcNetGame.Up && !AcNetGame.MasterKnown) return false;
                seq = Math.Max(Current != null ? Current.Seq + 1 : 1, command.Seq);
            }

            c.Seq = seq;
            Set(c, true);
            return true;
        }

        /// <summary>A command from the network (or the master's own): becomes <see cref="Current"/> and <see cref="Changed"/> fires once.</summary>
        internal void Set(AcRaceCommand c, bool own)
        {
            Current = c;
            var h = Changed;
            if (h != null)
            {
                foreach (Delegate d in h.GetInvocationList())
                {
                    try { ((Action<AcRaceCommand>)d)(c); }
                    catch (Exception e) { AcLog.Error("Net: a RaceControl handler failed: " + e); }
                }
            }

            AcNetGame.NoteApplied(c, own);
        }

        internal void Reset() { Current = null; }
    }

    /// <summary>
    /// [N, Net] The net layer's public face: ONE session from AcGame.NetStart to the end of the visit (a whitelisted player only - a visitor
    /// never claims a slot): slot claim (HaloSlotClaim verbatim), own record (AcNetWire), other players, remote cars, the instance master
    /// (Halo's record-bit rule), race control (world blocks), AI blocks, race state, passengers / riders. Alone / offline / editor: IsMaster true,
    /// nothing sent, RaceControl.Publish applies locally, TryHasRecord known=false. Every entry point catches its own errors. Main thread only.
    /// Frame: AcNetEarlyDriver (Update -95: draw remote cars, riders early) -> AcGame.Update NetTick (compose own record, session tick, players,
    /// commands) -> ... -> AcNetDriver (LateUpdate 1000: riders late, after Fusion's render, before VRIK). The API is in DevTools/agents/N/NOTES.md.
    /// </summary>
    public static partial class AcNetGame
    {
        private static bool _up;
        private static readonly List<AcNetPlayer> PlayerList = new List<AcNetPlayer>(AcNetSlots.PlayerCount);
        private static readonly List<AcRemoteCar> CarList = new List<AcRemoteCar>(AcNetSlots.PlayerCount);
        private static readonly AcRaceControl Control = new AcRaceControl();

        /// <summary>Lab and tests: where the records come from instead of the Fusion session (null = the real one).</summary>
        public static Func<IAcSlotTransport> TransportFactory;
        /// <summary>The clock of the net layer in seconds (claim timings, write rate, timelines): real time that does not stop with the game.</summary>
        public static Func<double> Clock = DefaultClock;
        /// <summary>Lab: names of scripted players (null = the Bridge's nicknames).</summary>
        public static Func<int, string> LabNames;
        /// <summary>The session (null before the Dreamport started). For the lab and tests; modules use the statics below.</summary>
        public static AcNet Net { get; private set; }

        private static AcGame _game;
        private static int _errors;
        private static float _nextStats, _statsFrom;
        private static string _localNick;
        private static int _claimedLogged = -1, _masterLogged = int.MinValue;

        private static double DefaultClock() { return Time.unscaledTimeAsDouble; }

        /// <summary>The net layer runs (Start was called).</summary>
        public static bool Up { get { return _up && Net != null; } }
        /// <summary>In a Fusion room with the slot objects (else alone: everything local).</summary>
        public static bool Online { get { return Up && Net.Online; } }
        /// <summary>My player slot is claimed (AcLocal.NetSlot).</summary>
        public static bool Claimed { get { return Up && Net.Claimed; } }
        /// <summary>I am the instance master (true alone / offline / before the layer runs).</summary>
        public static bool IsMaster { get { return !Up || Net.IsInstanceMaster; } }
        /// <summary>False while online and the master is not known yet: grey out master controls.</summary>
        public static bool MasterKnown { get { return !Up || !Net.MasterPending; } }
        /// <summary>The instance master's name ("connecting..." while unknown).</summary>
        public static string MasterName
        {
            get
            {
                if (!Online) return LocalNick();
                if (Net.MasterPending) return "connecting...";
                if (Net.IsInstanceMaster) return LocalNick();
                AcNetPlayer p = FindByPhotonId(Net.MasterId);
                if (p != null && !string.IsNullOrEmpty(p.Name)) return p.Name;
                // F and R read this every frame: the "Player n" fallback is made once per id
                if (_masterFallbackId != Net.MasterId || _masterFallback == null) { _masterFallbackId = Net.MasterId; _masterFallback = "Player " + Net.MasterId; }
                return _masterFallback;
            }
        }

        private static int _masterFallbackId = int.MinValue;
        private static string _masterFallback;
        /// <summary>The other Dreamport players (slot order). Do not keep the list.</summary>
        public static IReadOnlyList<AcNetPlayer> Players { get { return PlayerList; } }
        /// <summary>Dreamport players in the instance, me included.</summary>
        public static int PlayerCount { get { return PlayerList.Count + 1; } }
        /// <summary>Race control (newest command; master publishes).</summary>
        public static AcRaceControl RaceControl { get { return Control; } }
        /// <summary>Remote cars drawn on my track.</summary>
        public static IReadOnlyList<AcRemoteCar> RemoteCars { get { return CarList; } }

        // ---- added by N (additive) ----
        /// <summary>The master's Photon id (-1 offline / unknown).</summary>
        public static int MasterId { get { return Online ? Net.MasterId : -1; } }
        /// <summary>My lobby "ready" flag and "spectating" flag (F sets them; published in my record, read as AcNetPlayer.Ready / Spectating).</summary>
        public static bool Ready, Spectating;
        /// <summary>A player joined / left the instance (after their name is known).</summary>
        public static event Action<AcNetPlayer> PlayerJoined, PlayerLeft;

        /// <summary>Starts the session (from AcGame.NetStart; the gate already let the player in).</summary>
        public static void Start(AcGame game)
        {
            if (_up) return;
            _game = game;
            try
            {
                IAcSlotTransport transport = TransportFactory != null ? TransportFactory() : new AcFusionTransport();
                Net = new AcNet(transport, Clock, "");
                AcNet.LogSink = null;
                _up = true;
                _statsFrom = Time.unscaledTime;
                _nextStats = _statsFrom + 60f;
                Subscribe();
                HookSeat(true);
                AcLog.Info("Net: started (" + (TransportFactory != null ? "test transport" : "Fusion session") + ", protocol " + AcNetWire.Protocol
                           + "); alone or offline everything stays local");
            }
            catch (Exception e)
            {
                _up = false;
                Net = null;
                AcLog.Error("Net: start failed: " + e);
            }
        }

        /// <summary>Once a frame, first of the hooks: compose the own record, transport update, read records, claim, master, write own record,
        /// players, race commands.</summary>
        public static void Tick()
        {
            if (!Up) return;
            // every step on its own: one failing step (often a call into another module) never stops the slot claim, the record write or the reads
            try { ComposeOwn(); } catch (Exception e) { StepFailed(0, "composing the own record", e); }
            try { PublishAiCars(); } catch (Exception e) { StepFailed(1, "publishing the AI cars", e); }
            try
            {
                Net.Tick();
                AcLocal.NetSlot = Net.Claimed ? Net.MySlot : -1;
                AcLocal.PhotonId = Net.Online ? Net.LocalId : -1;
                AcLocal.NetOnline = Net.Online;
                if (string.IsNullOrEmpty(AcLocal.Nick)) { string n = LocalNick(); if (n != "You") AcLocal.Nick = n; }
                LogState();
            }
            catch (Exception e) { StepFailed(2, "the session tick", e); }

            try { RefreshPlayers(); } catch (Exception e) { StepFailed(3, "reading the players", e); }
            try { RefreshAiPlayers(); } catch (Exception e) { StepFailed(4, "reading the AI cars", e); }
            try { StepCommands(); } catch (Exception e) { StepFailed(5, "the race commands", e); }
            try { StepSeats(); } catch (Exception e) { StepFailed(6, "the seat requests", e); }
            try { FeedRiders(); } catch (Exception e) { StepFailed(7, "feeding S's riders", e); }
            try
            {
                if (Time.unscaledTime >= _nextStats)
                {
                    float span = Time.unscaledTime - _statsFrom;
                    _statsFrom = Time.unscaledTime;
                    _nextStats = _statsFrom + 60f;
                    if (Net.Online) AcLog.Info(Net.StatsLine(span) + ", " + CarList.Count + " remote car(s) drawn");
                }
            }
            catch (Exception e) { StepFailed(8, "the stats line", e); }
        }

        private static readonly int[] StepErrors = new int[12];

        /// <summary>A frame step threw: logged 3 times per step (with the exception), then counted silently.</summary>
        private static void StepFailed(int step, string what, Exception e)
        {
            _errors++;
            int n = ++StepErrors[step];
            if (n <= 3) AcLog.Warn("Net: " + what + " failed (" + n + (n == 3 ? ", later ones not logged" : "") + "): " + e);
        }

        /// <summary>Early frame (AcNetEarlyDriver, Update -95, before S's AcRiders at -90 and the client's order-0 scripts): remote cars drawn at this frame's moment, so
        /// remote riders are put on them right after (EtsNet order: draw puppets, then riders early).</summary>
        public static void Early()
        {
            if (!Up) return;
            // remote bodies are put on these cars by S's AcRiders (order -90, right after this): N feeds it the rider offsets (FeedRiders).
            // DrawCars guards each car (and each call into P / C / A) itself.
            try { DrawCars(); } catch (Exception e) { StepFailed(9, "drawing the remote cars", e); }
        }

        /// <summary>Late frame (AcNetDriver LateUpdate 1000): draw remote cars / riders after Fusion's render, before VRIK.</summary>
        public static void Late()
        {
            if (!Up) return;
            try
            {
                // nothing late yet: S's AcRiders re-places remote bodies at 9000 on the cars drawn here at -95
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) AcLog.Warn("Net: the late frame failed (" + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e);
            }
        }

        /// <summary>World closing: write my slot free, destroy remote cars.</summary>
        public static void Stop()
        {
            if (!_up) { PlayerList.Clear(); CarList.Clear(); return; }
            _up = false;
            try
            {
                Unsubscribe();
                HookSeat(false);
                ClearCars("the world closes");
                _building = null;      // the host's coroutines end with the world
                ClearPlayers();
                if (Net != null) Net.Dispose();
            }
            catch (Exception e)
            {
                AcLog.Warn("Net: stopping failed: " + e.Message);
            }

            PlayerList.Clear();
            CarList.Clear();
            AcLocal.NetSlot = -1;
            AcLocal.PhotonId = -1;
            AcLocal.NetOnline = false;
            Control.Reset();
            Net = null;
            _claimedLogged = -1;
            _masterLogged = int.MinValue;
            AcLog.Info("Net: stopped (slot written free)");
        }

        /// <summary>Whether the player with this Photon id holds a written "Ac Net Player" slot; <paramref name="known"/> is false while
        /// the net layer cannot tell (offline, editor, no slots yet) - AcGear then falls back to "the remote is on the whitelist".</summary>
        public static bool TryHasRecord(int photonId, out bool known)
        {
            known = false;
            try
            {
                known = Up && Net.Online;
                return known && Net.HasRecord(photonId);
            }
            catch (Exception)
            {
                known = false;
                return false;
            }
        }

        /// <summary>A listed player by Photon id (null when none). AI ids -(100 + i) find the AI entries.</summary>
        public static AcNetPlayer FindByPhotonId(int photonId)
        {
            for (int i = 0; i < PlayerList.Count; i++) if (PlayerList[i].PhotonId == photonId) return PlayerList[i];
            if (photonId <= -100)
                for (int i = 0; i < AiList.Count; i++) if (AiList[i].PhotonId == photonId) return AiList[i];
            return null;
        }

        /// <summary>My Somnium nickname (Bridge: PlayersContainer.LocalPlayer.Properties.NickName); "You" when the Bridge is not up (the editor).</summary>
        public static string LocalNick()
        {
            if (!string.IsNullOrEmpty(_localNick)) return _localNick;
            try
            {
                var p = SomniumBridge.PlayersContainer != null ? SomniumBridge.PlayersContainer.LocalPlayer : null;
                string n = p != null && p.Properties != null ? p.Properties.NickName : null;
                if (!string.IsNullOrEmpty(n)) { _localNick = n; return n; }
            }
            catch (Exception) { /* no Bridge */ }

            return string.IsNullOrEmpty(AcLocal.Nick) ? "You" : AcLocal.Nick;
        }

        /// <summary>A player's name by Photon id, me included ("Player n" when unknown).</summary>
        public static string NameOf(int photonId)
        {
            if (Up && photonId >= 0 && photonId == Net.LocalId) return LocalNick();
            AcNetPlayer p = FindByPhotonId(photonId);
            return p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "Player " + photonId;
        }

        private static void LogState()
        {
            int slot = Net.Claimed ? Net.MySlot : -1;
            if (slot != _claimedLogged)
            {
                _claimedLogged = slot;
                if (slot >= 0) AcLog.Info("Net: claimed slot " + slot + " (photon id " + Net.LocalId + ")");
            }

            int m = Net.Online ? Net.MasterId : -1;
            if (m != _masterLogged && m >= 0)
            {
                string name = m == Net.LocalId ? LocalNick() : NameOf(m);
                if (m == Net.LocalId || !name.StartsWith("Player ", StringComparison.Ordinal) || Net.Now - Net.OwnedAt > 3.0)
                {
                    _masterLogged = m;
                    string how = Net.MasterReason;
                    AcLog.Info("Net: master is " + name + " (" + (how == "me" ? "me" : how) + ")" + (m == Net.LocalId ? " - this client writes race control" : ""));
                }
            }

            string note = Net.TakeSeatNote();
            if (note != null) AcLog.Info("Net: " + note);
            if (Net.BecameMaster) AcLog.Info("Net: took over as instance master (the previous master left); race command " + (Control.Current != null ? Control.Current.Seq : 0) + " stays in force");
        }

        private static void Subscribe()
        {
            AcSession.TrackBuilt += OnTrackBuilt;
            AcSession.Unloading += OnUnloading;
            AcSession.LapCompleted += OnLap;
        }

        private static void Unsubscribe()
        {
            AcSession.TrackBuilt -= OnTrackBuilt;
            AcSession.Unloading -= OnUnloading;
            AcSession.LapCompleted -= OnLap;
        }
    }

    /// <summary>[N, Net] Runs the net layer's late frame (remote cars after Fusion's render). Added by AcGame.Net.cs.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class AcNetDriver : MonoBehaviour
    {
        private int _errors;

        private void LateUpdate()
        {
            try { AcNetGame.Late(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Net: late frame failed: " + e); }
        }
    }

    /// <summary>[N, Net] Runs the net layer's early frame (remote cars drawn, remote riders placed before the client's order-0 scripts: its receive
    /// drivers and the desktop avatar's root handler - EtsRiders' EarlyPass rule). Added by AcGame.Net.cs.</summary>
    [DefaultExecutionOrder(-95)]
    public sealed class AcNetEarlyDriver : MonoBehaviour
    {
        private int _errors;

        private void Update()
        {
            try { AcNetGame.Early(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Net: early frame failed: " + e); }
        }
    }
}
