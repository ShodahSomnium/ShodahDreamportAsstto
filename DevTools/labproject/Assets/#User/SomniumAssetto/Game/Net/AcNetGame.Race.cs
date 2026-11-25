using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] Race control on the wire (part of <see cref="AcNetGame"/>): AcRaceCommand (R) <-> AcNetCommand (World 0..1) through the catalog every
    /// client has (car / track / layout ids travel as NameHash of their folder names), the master's live race state, penalties and results
    /// (Race 0..1, World 2..3), and the master's AI cars (AI blocks) from module I's AcAi.Drivers with timing from R's standings.
    /// </summary>
    public static partial class AcNetGame
    {
        private static readonly AcNetCommand WireOut = new AcNetCommand();
        private static bool _commandPending;
        private static AcContent _catalogOf;
        private static readonly Dictionary<int, AcCarEntry> CarsByHash = new Dictionary<int, AcCarEntry>(256);
        private static readonly Dictionary<int, AcTrackEntry> TracksByHash = new Dictionary<int, AcTrackEntry>(64);
        private static readonly List<AcNetResult> ResultsOut = new List<AcNetResult>(AcNetBlocks.MaxResults);
        private static readonly AcNetResult[] ResultsIn = new AcNetResult[AcNetBlocks.MaxResults];
        private static readonly List<AcNetResult> ResultsList = new List<AcNetResult>(AcNetBlocks.MaxResults);
        private static int _resultsReadFrame = -1;
        private static readonly bool[] AiPublished = new bool[AcNetBlocks.MaxAi];
        private static float _aiAbsentSince = -1f;
        private static bool _hashCollisionLogged;

        // ------------------------------------------------------------------ the catalog

        private static AcContent Catalog
        {
            get
            {
                AcGame g = AcGame.Current;
                AcContent c = g != null ? g.Content : null;
                if (!ReferenceEquals(c, _catalogOf)) BuildIndex(c);
                return c;
            }
        }

        private static void BuildIndex(AcContent c)
        {
            _catalogOf = c;
            CarsByHash.Clear();
            TracksByHash.Clear();
            if (c == null) return;
            int collisions = 0;
            foreach (AcCarEntry car in c.Cars)
            {
                if (car == null || string.IsNullOrEmpty(car.Id)) continue;
                int h = AcNetWire.NameHash(car.Id);
                if (CarsByHash.ContainsKey(h)) { collisions++; if (car.Owned) CarsByHash[h] = car; }
                else CarsByHash.Add(h, car);
            }

            foreach (AcTrackEntry t in c.Tracks)
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                int h = AcNetWire.NameHash(t.Id);
                if (TracksByHash.ContainsKey(h)) collisions++;
                else TracksByHash.Add(h, t);
            }

            if (collisions > 0 && !_hashCollisionLogged)
            {
                _hashCollisionLogged = true;
                AcLog.Warn("Net: " + collisions + " car/track folder name(s) share a NameHash in this catalog - those cars may show as another one remotely");
            }
        }

        internal static AcCarEntry CarByHash(int hash)
        {
            if (hash == 0) return null;
            AcContent c = Catalog;
            if (c == null) return null;
            AcCarEntry e;
            return CarsByHash.TryGetValue(hash, out e) ? e : null;
        }

        internal static AcSkinEntry SkinByHash(AcCarEntry car, int hash)
        {
            if (car == null || car.Skins == null) return null;
            for (int i = 0; i < car.Skins.Count; i++)
                if (car.Skins[i] != null && AcNetWire.NameHash(car.Skins[i].Id) == hash) return car.Skins[i];
            return hash == 0 && car.Skins.Count > 0 ? car.Skins[0] : null;
        }

        private static AcTrackEntry TrackByHash(int hash)
        {
            AcContent c = Catalog;
            if (c == null || hash == 0) return null;
            AcTrackEntry t;
            return TracksByHash.TryGetValue(hash, out t) ? t : null;
        }

        private static AcLayoutEntry LayoutByHash(AcTrackEntry t, int hash)
        {
            if (t == null || t.Layouts == null) return null;
            for (int i = 0; i < t.Layouts.Count; i++)
                if (t.Layouts[i] != null && AcNetWire.NameHash(t.Layouts[i].Id) == hash) return t.Layouts[i];
            return null;
        }

        // ------------------------------------------------------------------ commands

        /// <summary>RaceControl.Publish: the master's command onto the wire. Returns its Seq (0 = not published: not up, not master, master unknown).</summary>
        internal static int PublishCommand(AcRaceCommand c)
        {
            if (!Up || c == null) return 0;
            ToWire(c, WireOut);
            int seq = Net.PublishCommand(WireOut);
            if (seq > 0 && Net.Online && (c.Rules != null && c.Rules.AllowedCars != null && c.Rules.AllowedCars.Count > AcNetBlocks.MaxAllowed))
                AcLog.WarnOnce("Net: the race command allows " + c.Rules.AllowedCars.Count + " cars; only the first " + AcNetBlocks.MaxAllowed + " travel over the network");
            if (seq > 0) c.MasterId = Net.Online ? Net.LocalId : -1;
            return seq;
        }

        private static void ToWire(AcRaceCommand c, AcNetCommand w)
        {
            AcRules r = c.Rules ?? new AcRules();
            w.Phase = (int)c.Phase;
            w.TrackHash = AcNetWire.NameHash(c.TrackId);
            w.LayoutHash = AcNetWire.NameHash(c.LayoutId);
            w.Session = (int)r.Session;
            w.Laps = r.Laps;
            w.Minutes = r.TimeMinutes;
            w.Abs = r.AbsAllowed;
            w.Tc = r.TcAllowed;
            w.Stability = r.StabilityAllowed;
            w.AutoClutch = r.AutoClutchAllowed;
            w.AutoShift = r.AutoShiftAllowed;
            w.Blankets = r.TyreBlanketsAllowed;
            w.TyresOut = r.AllowedTyresOut;
            w.JumpStart = r.JumpStartPenalty;
            w.FuelRate = r.FuelRate;
            w.TyreWear = r.TyreWearRate;
            w.Damage = r.DamageMultiplier;
            w.AiCount = r.AiCount;
            w.AiLevel = r.AiLevel;
            w.RaceOverSeconds = r.RaceOverTimeSeconds;
            w.PitLimitKmh = r.PitLimitKmh;
            w.StartUtc = c.StartUtc;
            w.PhaseUtc = c.PhaseUtc;
            w.MasterId = c.MasterId;
            w.Grid.Clear();
            if (c.Grid != null) for (int i = 0; i < c.Grid.Count && w.Grid.Count < AcNetBlocks.MaxGrid; i++) w.Grid.Add(c.Grid[i]);
            w.Allowed.Clear();
            if (r.AllowedCars != null)
                for (int i = 0; i < r.AllowedCars.Count && w.Allowed.Count < AcNetBlocks.MaxAllowed; i++) w.Allowed.Add(AcNetWire.NameHash(r.AllowedCars[i]));
        }

        /// <summary>A wire command into R's AcRaceCommand (null while the catalog is not built yet - kept pending).</summary>
        private static AcRaceCommand FromWire(AcNetCommand w)
        {
            if (Catalog == null) return null;
            var c = new AcRaceCommand { Seq = w.Seq, Phase = (AcPhase)Mathf.Clamp(w.Phase, 0, (int)AcPhase.Results), StartUtc = w.StartUtc, PhaseUtc = w.PhaseUtc, MasterId = w.MasterId };
            AcTrackEntry t = TrackByHash(w.TrackHash);
            AcLayoutEntry l = LayoutByHash(t, w.LayoutHash);
            c.TrackId = t != null ? t.Id : w.TrackHash == 0 ? "" : "#" + w.TrackHash.ToString("x8");
            // a layout this catalog lacks travels on as "#hash" (like the track): R then says "not installed" instead of loading the default layout,
            // whose other layout hash would hide every remote car and put the grid elsewhere
            c.LayoutId = l != null ? l.Id : w.LayoutHash == 0 ? "" : "#" + w.LayoutHash.ToString("x8");
            if (w.TrackHash != 0 && t == null) AcLog.WarnOnce("Net: the master's track " + c.TrackId + " is not in this install's catalog");
            else if (t != null && l == null && w.LayoutHash != 0) AcLog.WarnOnce("Net: the master's layout " + c.LayoutId + " of " + t.Id + " is not in this install's catalog - shown as not installed");
            var r = new AcRules
            {
                Session = (AcSessionType)Mathf.Clamp(w.Session, 1, 4),
                Laps = w.Laps,
                TimeMinutes = w.Minutes,
                AbsAllowed = w.Abs,
                TcAllowed = w.Tc,
                StabilityAllowed = w.Stability,
                AutoClutchAllowed = w.AutoClutch,
                AutoShiftAllowed = w.AutoShift,
                TyreBlanketsAllowed = w.Blankets,
                AllowedTyresOut = w.TyresOut,
                JumpStartPenalty = w.JumpStart,
                FuelRate = w.FuelRate,
                TyreWearRate = w.TyreWear,
                DamageMultiplier = w.Damage,
                AiCount = w.AiCount,
                AiLevel = w.AiLevel,
                RaceOverTimeSeconds = w.RaceOverSeconds,
                PitLimitKmh = w.PitLimitKmh,
            };
            for (int i = 0; i < w.Allowed.Count; i++)
            {
                AcCarEntry car = CarByHash(w.Allowed[i]);
                r.AllowedCars.Add(car != null ? car.Id : "#" + w.Allowed[i].ToString("x8"));
            }

            c.Rules = r;
            c.Grid.AddRange(w.Grid);
            return c;
        }

        private static void StepCommands()
        {
            AcNetCommand w;
            if (Net.Republished && Net.TryGetCommand(out w) && Control.Current != null)
            {
                // my offline command went into the room under a room Seq (I am its master): the same command, only its number changed - no re-apply
                Control.Current.Seq = w.Seq;
                Control.Current.MasterId = w.MasterId;
                _appliedSeq = w.Seq;
                AcLog.Info("Net: the race command published offline is now seq " + w.Seq + " in the room (I am the master)");
            }

            if (Net.NewCommand) _commandPending = true;
            if (!_commandPending) return;
            if (!Net.TryGetCommand(out w)) { _commandPending = false; return; }
            // the same Seq from the same master is the command I hold; an offline command (MasterId -1) never matches a room's command of equal Seq
            if (Control.Current != null && Control.Current.Seq == w.Seq && Control.Current.MasterId == w.MasterId) { _commandPending = false; return; }
            AcRaceCommand c = FromWire(w);
            if (c == null) return;                  // the catalog is not built yet: apply once it is
            _commandPending = false;
            Control.Set(c, false);
        }

        // ------------------------------------------------------------------ live race state, penalties, results

        /// <summary>Master: the live race state (start lights on the shared UTC clock, session clocks) for everyone. No-op for others.</summary>
        public static void PublishRaceState(AcNetRaceState state)
        {
            if (Up) Net.PublishRaceState(in state);
        }

        /// <summary>The master's live race state (false when none was published); <paramref name="commandSeq"/> = the command it was published under.</summary>
        public static bool TryGetRaceState(out AcNetRaceState state, out int commandSeq)
        {
            state = default(AcNetRaceState);
            commandSeq = 0;
            if (!Up) return false;
            commandSeq = Net.RaceStateSeq;
            return Net.TryGetRaceState(out state);
        }

        /// <summary>Master: a penalty for a player (Photon id) or an AI car (-(100 + index)): seconds 1..254, <see cref="AcNetBlocks.DriveThrough"/>, 0 = none.</summary>
        public static void SetPenalty(int playerId, int value)
        {
            if (!Up) return;
            int entry = PenaltyEntry(playerId);
            if (entry >= 0) Net.SetPenalty(entry, value);
        }

        /// <summary>The penalty the master set for a player / AI car (0 none).</summary>
        public static int PenaltyOf(int playerId)
        {
            if (!Up) return 0;
            int entry = PenaltyEntry(playerId);
            return entry >= 0 ? Net.Penalty(entry) : 0;
        }

        private static int PenaltyEntry(int playerId)
        {
            if (playerId <= -100) { int ai = -100 - playerId; return ai < AcNetBlocks.MaxAi ? AcNetWire.MaxSlots + ai : -1; }
            if (Net.Online && playerId == Net.LocalId) return Net.MySlot;
            AcNetPlayer p = FindByPhotonId(playerId);
            return p != null ? p.Slot : -1;
        }

        /// <summary>Master: the results (R's standings, position order) for everyone (up to 56 lines).</summary>
        public static void PublishResults(IReadOnlyList<AcStanding> standings)
        {
            if (!Up || standings == null) return;
            ResultsOut.Clear();
            int me = Net.Online ? Net.LocalId : -1;
            for (int i = 0; i < standings.Count && ResultsOut.Count < AcNetBlocks.MaxResults; i++)
            {
                AcStanding s = standings[i];
                if (s == null || s.Spectating) continue;                 // late joiners without a grid slot are not classified
                int id = s.IsLocal ? me : s.PlayerId;
                ResultsOut.Add(new AcNetResult { Id = id, Position = s.Position, Finished = s.Finished, TotalMs = s.TotalMs, BestMs = s.BestMs });
            }

            Net.PublishResults(ResultsOut, ResultsOut.Count);
        }

        /// <summary>The master's results (position order; ids = Photon id / -(100 + AI index)); <see cref="ResultsSeq"/> = the command they belong to.</summary>
        public static IReadOnlyList<AcNetResult> Results
        {
            get
            {
                if (!Up) { ResultsList.Clear(); return ResultsList; }
                if (_resultsReadFrame != Time.frameCount)
                {
                    _resultsReadFrame = Time.frameCount;
                    int n = Net.Results(ResultsIn);
                    ResultsList.Clear();
                    for (int i = 0; i < n; i++) ResultsList.Add(ResultsIn[i]);
                }

                return ResultsList;
            }
        }

        public static int ResultsSeq { get { return Up ? Net.ResultsSeq : 0; } }

        // ------------------------------------------------------------------ the master's AI cars

        /// <summary>Master: module I's AI cars into the AI blocks (pose + controls from their rigs, timing from R's standings). A master that just took
        /// over keeps writing the AI cars it read until its own AI module drives (then I's cars replace them); cleared when nobody drives AI for 10 s.</summary>
        private static void PublishAiCars()
        {
            if (!Net.IsInstanceMaster)
            {
                Array.Clear(AiPublished, 0, AiPublished.Length);
                _aiAbsentSince = -1f;
                Array.Clear(AiEpochRig, 0, AiEpochRig.Length);
                Array.Clear(AiDamageRig, 0, AiDamageRig.Length);
                for (int i = 0; i < AiPendingSince.Length; i++) AiPendingSince[i] = -1f;
                return;
            }

            IReadOnlyList<AcAiDriver> drivers = AiDriversSafe();
            int count = drivers != null ? drivers.Count : 0;
            bool any = false;
            for (int i = 0; i < AcNetBlocks.MaxAi; i++) { AiSeen[i] = false; AiPending[i] = false; }
            for (int d = 0; d < count; d++)
            {
                AcAiDriver ai = drivers[d];
                if (ai == null || ai.Index < 0 || ai.Index >= AcNetBlocks.MaxAi) continue;
                if (ai.Rig == null || ai.Rig.Root == null) { AiPending[ai.Index] = true; continue; }     // I lists every driver at once and builds them one by one
                try
                {
                    if (!ReferenceEquals(AiDamageRig[ai.Index], ai.Rig)) { AiDamageRig[ai.Index] = ai.Rig; SeedAiDamage(ai.Index, ai.Rig); }   // iteration 2
                    AcNetCar car = AcNetCar.Empty();
                    int stamp;
                    FillCar(ai.Rig, ref car, out stamp);
                    car.LayoutHash = LayoutHash;
                    AiTiming(ai.Index, ref car);
                    car.Epoch = AiEpochOf(ai.Index, ai.Rig);
                    string name = ai.Name ?? "";
                    int line = DriverNameLine(ai.Index, name);
                    AcSkinEntry skin = ai.Rig.Car != null ? ai.Rig.Car.Skin(ai.Rig.SkinId) : null;
                    if (skin != null && string.Equals(skin.Driver, name, StringComparison.Ordinal)) name = "";   // receivers take the full skin driver name
                    Net.SetAi(ai.Index, in car, ai.Level, name, line);
                    if (Net.OwnStampMs < 0) Net.OwnStampMs = stamp;
                    AiSeen[ai.Index] = true;
                    AiPublished[ai.Index] = true;
                    any = true;
                }
                catch (Exception e)
                {
                    AcLog.WarnOnce("Net: publishing an AI car failed (later ones not logged): " + e.Message);
                }
            }

            if (any) _aiAbsentSince = -1f;
            float now = Time.unscaledTime;
            for (int i = 0; i < AcNetBlocks.MaxAi; i++)
            {
                if (AiSeen[i]) { AiPendingSince[i] = -1f; continue; }
                AcNet.AiCar a = Net.Ai(i);
                bool present = a != null && a.Present;
                if (AiPending[i] && present)
                {
                    // the driver of this index is still being built (or rebuilt): keep its block - the car stays drawn where it is on every other client
                    // (inherited cars too, so they do not vanish and come back one by one); a build that never ends is cleared after 60 s (// ours)
                    if (AiPendingSince[i] < 0f) AiPendingSince[i] = now;
                    if (now - AiPendingSince[i] <= 60f) continue;
                }

                AiPendingSince[i] = -1f;
                if (AiPublished[i]) { Net.ClearAi(i); AiPublished[i] = false; continue; }
                // inherited from a former master: keep until nobody drives AI for 10 s (// ours), then clear
                if (!present) continue;
                if (count > 0) { Net.ClearAi(i); continue; }                 // my AI module drives other indices: this one is not continued
                if (_aiAbsentSince < 0f) _aiAbsentSince = now;
                else if (now - _aiAbsentSince > 10f) { Net.ClearAi(i); AcLog.InfoOnce("Net: inherited AI cars cleared (no AI driver on this master)"); }
            }
        }

        // the AI cars' placement epoch (as the own car's _ownEpoch): bumped for a new rig of an index and for every placement (grid, pits, reset),
        // so receivers snap instead of sliding, and a new master's rigs never share the old master's epoch (its stamps are on another clock)
        private static readonly AcCarRig[] AiEpochRig = new AcCarRig[AcNetBlocks.MaxAi];
        // iteration 2: the rig of each AI index whose inherited damage was considered (a new master seeds its own rig once)
        private static readonly AcCarRig[] AiDamageRig = new AcCarRig[AcNetBlocks.MaxAi];

        /// <summary>Iteration 2 (DAMAGE_DESIGN 7 hand-over): a new master's first own rig of AI index <paramref name="index"/> takes the damage the
        /// old master published for it in "Ac Net Host 0" (quantised: 1 km/h zones, 1/15 rods, 1 % engine; the retired bit too). Only a pristine
        /// rig is seeded (I may have seeded it already), and only once per take-over.</summary>
        private static void SeedAiDamage(int index, AcCarRig rig)
        {
            int d0, d1;
            int carHash = rig.Car != null ? AcNetWire.NameHash(rig.Car.Id) : 0;   // fixer 2026-11-02: the same car as the inherited one only
            if (!Net.TakeInheritedAiDamage(index, carHash, out d0, out d1)) return;
            try
            {
                AcCarPhysics ph = rig.Physics;
                if (ph == null || ph.Damage == null || !ph.Damage.Pristine) return;
                if (ph.Damage.ApplyWire(d0, d1))
                    AcLog.Info("Net: AI car " + (index + 1) + " (" + (rig.Car != null ? rig.Car.Id : "?") + ") keeps its damage from the previous master (zones F/R/L/R/C "
                               + ph.Damage.ZoneByte(0) + "/" + ph.Damage.ZoneByte(1) + "/" + ph.Damage.ZoneByte(2) + "/" + ph.Damage.ZoneByte(3) + "/" + ph.Damage.ZoneByte(4)
                               + " km/h, engine " + ph.Damage.EnginePct.ToString("0") + " %" + (ph.Damage.Retired ? ", retired" : "") + ")");
            }
            catch (Exception e) { AcLog.WarnOnce("Net: seeding an AI car's damage failed: " + e.Message); }
        }
        private static readonly int[] AiEpochPlacements = new int[AcNetBlocks.MaxAi];
        private static readonly int[] AiEpoch = new int[AcNetBlocks.MaxAi];

        private static int AiEpochOf(int index, AcCarRig rig)
        {
            int placements = rig.Physics != null ? rig.Physics.Placements : 0;
            if (!ReferenceEquals(rig, AiEpochRig[index]))
            {
                AcNet.AiCar a = Net.Ai(index);
                int last = a != null && a.Present ? a.Car.Epoch : AiEpoch[index];   // above whatever this index last carried (an inherited car too)
                AiEpoch[index] = (last + 1) & 0xFF;
                AiEpochRig[index] = rig;
                AiEpochPlacements[index] = placements;
            }
            else if (placements != AiEpochPlacements[index])
            {
                AiEpochPlacements[index] = placements;
                AiEpoch[index] = (AiEpoch[index] + 1) & 0xFF;
            }

            return AiEpoch[index];
        }

        private static readonly bool[] AiSeen = new bool[AcNetBlocks.MaxAi];
        private static readonly bool[] AiPending = new bool[AcNetBlocks.MaxAi];
        private static readonly float[] AiPendingSince = NewPendingSince();

        private static float[] NewPendingSince()
        {
            var a = new float[AcNetBlocks.MaxAi];
            for (int i = 0; i < a.Length; i++) a[i] = -1f;
            return a;
        }

        /// <summary>I's AI drivers (guarded: null when I is missing or throws).</summary>
        private static IReadOnlyList<AcAiDriver> AiDriversSafe()
        {
            try { return AcAi.Drivers; }
            catch (Exception e) { AcLog.WarnOnce("Net: AcAi.Drivers threw (AI cars are not published): " + e.Message); return null; }
        }

        /// <summary>R's standings (guarded: null when R is missing or throws).</summary>
        private static IReadOnlyList<AcStanding> StandingsSafe()
        {
            try { return AcRace.Standings; }
            catch (Exception e) { AcLog.WarnOnce("Net: AcRace.Standings threw (timing fields stay 0): " + e.Message); return null; }
        }

        // ------------------------------------------------------------------ AI driver names (content/career/names.txt: the same file on every client)

        private static List<string> _driverNames;
        private static Dictionary<string, int> _driverNameLine;
        private static string _driverNamesRoot;
        private static readonly string[] AiNameFor = new string[AcNetBlocks.MaxAi];
        private static readonly int[] AiNameLineFor = new int[AcNetBlocks.MaxAi];
        private static readonly int[] PackA = new int[3], PackB = new int[3];

        /// <summary>The non-empty lines of content/career/names.txt (trimmed, BOM dropped - read as I's AcAiField reads them); empty when missing.</summary>
        private static List<string> DriverNames()
        {
            AcInstall inst = AcGame.Current != null ? AcGame.Current.Install : null;
            string root = inst != null ? inst.Root : null;
            if (_driverNames != null && string.Equals(root, _driverNamesRoot, StringComparison.Ordinal)) return _driverNames;
            _driverNamesRoot = root;
            _driverNames = new List<string>(64);
            _driverNameLine = new Dictionary<string, int>(64, StringComparer.Ordinal);
            try
            {
                string path = root != null ? System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(root, "content"), "career"), "names.txt") : null;
                if (path != null && System.IO.File.Exists(path))
                {
                    string[] lines = AcText.ReadFile(path).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string s = lines[i].Trim().Trim('﻿');
                        if (s.Length == 0) continue;
                        if (!_driverNameLine.ContainsKey(s)) _driverNameLine.Add(s, _driverNames.Count);
                        _driverNames.Add(s);
                    }
                }

                AcLog.InfoOnce("Net: " + _driverNames.Count + " AI driver names from content/career/names.txt travel as their line number");
            }
            catch (Exception e)
            {
                AcLog.WarnOnce("Net: content/career/names.txt could not be read (AI names travel as 12 characters): " + e.Message);
            }

            return _driverNames;
        }

        /// <summary>1 + the names.txt line of an AI driver's name (0 = not from that file or beyond line 255); cached per AI index.</summary>
        private static int DriverNameLine(int index, string name)
        {
            if (ReferenceEquals(AiNameFor[index], name)) return AiNameLineFor[index];
            AiNameFor[index] = name;
            int line = 0;
            if (!string.IsNullOrEmpty(name))
            {
                DriverNames();
                int k;
                if (_driverNameLine != null && _driverNameLine.TryGetValue(name, out k) && k < 255) line = k + 1;
            }

            AiNameLineFor[index] = line;
            return line;
        }

        /// <summary>Line <paramref name="line"/> (0-based) of content/career/names.txt, null when out of range.</summary>
        public static string DriverNameAt(int line)
        {
            List<string> names = DriverNames();
            return line >= 0 && line < names.Count ? names[line] : null;
        }

        /// <summary>The 0-based names.txt line of a name (-1 when it is not in the file).</summary>
        public static int DriverNameIndex(string name)
        {
            DriverNames();
            int k;
            return name != null && _driverNameLine != null && _driverNameLine.TryGetValue(name, out k) ? k : -1;
        }

        /// <summary>The full name of an AI car as received: the names.txt line when it agrees with the 12 characters that came with it (a client
        /// with another names.txt falls back to the text), else the text, else the skin's driver name, else "AI n".</summary>
        private static string ReceivedAiName(AcNet.AiCar a, int carHash, int skinHash)
        {
            string text = a.Name ?? "";
            if (a.NameLine > 0)
            {
                string line = DriverNameAt(a.NameLine - 1);
                if (line != null && (text.Length == 0 || SamePacked(line, text))) return line;
            }

            if (text.Length > 0) return text;
            // the master sends no name when it is the skin's own driver name (ui_skin.json "drivername"): the full name from my catalog
            AcSkinEntry skin = SkinByHash(CarByHash(carHash), skinHash);
            return skin != null && !string.IsNullOrEmpty(skin.Driver) ? skin.Driver : "AI " + (a.Index + 1);
        }

        private static bool SamePacked(string a, string b)
        {
            bool any = false;
            AcNetWire.PackText(a, PackA, 0, 3, ref any);
            AcNetWire.PackText(b, PackB, 0, 3, ref any);
            return PackA[0] == PackB[0] && PackA[1] == PackB[1] && PackA[2] == PackB[2];
        }

        private static void AiTiming(int index, ref AcNetCar car)
        {
            int id = -100 - index;
            IReadOnlyList<AcStanding> st = StandingsSafe();
            if (st == null) return;
            for (int i = 0; i < st.Count; i++)
            {
                AcStanding s = st[i];
                if (s == null || !s.IsAi || s.PlayerId != id) continue;
                car.Laps = s.Laps;
                car.BestMs = s.BestMs;
                car.LastMs = s.LastMs;
                car.RaceDistance = s.RaceDistance;
                car.Finished = s.Finished;
                car.FinishMs = s.Finished ? s.TotalMs : 0;
                car.PenaltyMs = s.PenaltyMs;
                car.InPitlane = s.InPit;
                car.Position = s.Position;
                return;
            }
        }
    }
}
