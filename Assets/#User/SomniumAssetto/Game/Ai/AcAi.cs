using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [I, AI drivers] The AI field: R calls <see cref="Spawn"/> (master; practice / race, count + level from AcRules), <see cref="Tick"/> once a
    /// frame on the master and <see cref="Clear"/>; R times the AI cars itself (AcTiming.AiTiming), N publishes them in the "Ac Net Ai" blocks
    /// from <see cref="Drivers"/>. Each driver is built one after another (ai.ini + physics digest and the speed plans on a worker, then
    /// P's AcCarRig.Spawn of Kind Ai), placed in a free pit box (from the far end; the pit lane / grid / line behind the start when there is
    /// none) and drives out along pit_lane.ai onto fast_lane.ai. Cars, skins and names like AC's quick race: the local player's car (or the
    /// master's AllowedCars in turn), skins varied, the skin's own driver name (ui_skin.json) else content/career/names.txt.
    /// A new master adopts the previous master's AI cars where they are (N's inherited blocks, or what <see cref="Observe"/> saw on a non-master);
    /// a client that loses the master role hands its AI over after <see cref="HandOverAfter"/> s (AcAiWatch on each rig - also when nobody ticks AcAi).
    /// No AI in a hotlap session (as AC). The layout's ai_hints.ini shapes every plan; AI cars carry race fuel from the grid on.
    /// </summary>
    public static partial class AcAi
    {
        private static readonly List<AcAiDriver> List = new List<AcAiDriver>();

        /// <summary>Most AI cars (N's 9 "Ac Net Ai" blocks x 2 cars).</summary>
        public const int MaxDrivers = 18;

        /// <summary>The AI drivers of this session (master: driven here; others: empty - N draws remote AI cars).</summary>
        public static IReadOnlyList<AcAiDriver> Drivers { get { return List; } }

        /// <summary>AI is available (module implemented and a track with an AI line is loaded; never in a hotlap session - AC has no AI there).</summary>
        public static bool Available
        {
            get
            {
                try
                {
                    if (IsHotlap(AcSession.Rules)) return false;
                    return AcTrackWorld.Loaded && AcTrackWorld.AiLine != null && !AcTrackWorld.AiLine.IsEmpty;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>A hotlap session (AC's session type 4 has no AI cars).</summary>
        internal static bool IsHotlap(AcRules rules) { return rules != null && rules.Session == AcSessionType.Hotlap; }

        /// <summary>All drivers built (or failed) - false while cars are still being built.</summary>
        public static bool Ready { get { return _building == null && List.Count > 0; } }

        /// <summary>The fast lane / pit lane prepared for driving (null when the track has none).</summary>
        public static AcAiTrack FastTrack { get { return _fast; } }
        public static AcAiTrack PitTrack { get { return _pit; } }

        private static int _gen;
        private static Coroutine _building;
        private static bool _hooked;
        private static AcAiTrack _fast, _pit;
        private static AcAiSpline _fastSrc, _pitSrc;
        private static int _tickErrors, _recoveryLines;
        private static bool _droveHere;

        /// <summary>Spawns <paramref name="count"/> AI cars for the session (master; <paramref name="rules"/> gives level and allowed cars).</summary>
        public static void Spawn(int count, AcRules rules)
        {
            if (count <= 0) return;
            if (!IsMaster()) { AcLog.InfoOnce("Ai: not the instance master - the master spawns the AI cars"); return; }
            if (IsHotlap(rules ?? AcSession.Rules)) { AcLog.InfoOnce("Ai: hotlap session - AC has no AI cars in a hotlap, none spawned"); return; }
            if (!Available) { AcLog.Info("Ai: no AI line on this track (fast_lane.ai missing or empty) - no AI cars"); return; }
            AcGame game = AcGame.Current;
            if (game == null || game.Content == null) { AcLog.Warn("Ai: no game / catalog yet - no AI cars"); return; }
            Hook();
            if (List.Count > 0) Clear();
            EnsureTracks();
            rules = rules ?? new AcRules();
            int level = Mathf.Clamp(rules.AiLevel, 0, 100);
            int build = Math.Min(count, MaxDrivers);
            if (build < count) AcLog.Warn("Ai: " + count + " AI cars asked, " + MaxDrivers + " is the most the net blocks carry - building " + build);
            if (!_droveHere) AcAiField.FromNet(game.Content); // only a master that never drove AI on this track adopts (else the blocks are my own old cars)
            List<AcAiDriver> made = AcAiField.Choose(game.Content, count, build, rules, level);
            for (int i = 0; i < made.Count; i++) List.Add(made[i]);
            _gen++;
            _recoveryLines = 0; _tickErrors = 0;
            AcLog.Info("Ai: spawning " + build + " AI car(s) (level " + level + ")" + (IsAdopting() ? " - adopting the previous master's AI cars" : ""));
            _building = game.StartCoroutine(AcAiField.BuildAll(_gen, level));
        }

        /// <summary>Drives the AI cars (call once a frame on the master).</summary>
        public static void Tick()
        {
            if (List.Count == 0) return;
            if (!IsMaster()) { NotMaster(); return; }
            _notMasterSince = -1f;
            Drive();
        }

        /// <summary>Not master for longer than this (s) -> the AI cars are handed over (removed here; the new master drives its own). A shorter
        /// flap keeps them driving. // ours</summary>
        public const float HandOverAfter = 1f;

        private static int _drivenFrame = -1;
        private static float _notMasterSince = -1f;

        /// <summary>Called once a frame by every AI rig's <see cref="AcAiWatch"/>: makes the module self-sufficient when R stops ticking AI on a
        /// client that lost the master role (R's AiTick returns early there) - drives through a short flap, then hands over.</summary>
        internal static void Watch()
        {
            if (List.Count == 0) return;
            if (IsMaster()) { _notMasterSince = -1f; return; } // R drives (Tick)
            NotMaster();
        }

        private static void NotMaster()
        {
            float now = Time.unscaledTime;
            if (_notMasterSince < 0f) _notMasterSince = now;
            if (now - _notMasterSince >= HandOverAfter) { HandOver(); return; }
            Drive();
        }

        private static void Drive()
        {
            if (_drivenFrame == Time.frameCount) return; // once a frame (R's Tick and the watchers)
            _drivenFrame = Time.frameCount;
            if (AcTrackWorld.AiLine != _fastSrc) { AcLog.Info("Ai: the track changed under the AI cars - clearing them"); Clear(); return; }
            AcAiTraffic.BeginFrame(_fast, List);
            _droveHere = true;
            for (int i = 0; i < List.Count; i++)
            {
                AcAiDriver d = List[i];
                if (d == null) continue;
                try { d.Produce(); }
                catch (Exception e) { if (_tickErrors++ < 5) AcLog.Error("Ai: driver " + d.Name + " failed: " + e); }
            }
        }

        /// <summary>Removes every AI car.</summary>
        public static void Clear()
        {
            _gen++;
            // the running BuildAll is NOT stopped: it sees the new generation, cancels the car it is building (AcCarSpawnHandle) and drains it -
            // StopCoroutine would leave a half-built car in the world and leak its meshes / texture scope for the session
            _building = null;
            int n = 0;
            for (int i = 0; i < List.Count; i++)
            {
                AcAiDriver d = List[i];
                if (d == null || d.Rig == null) continue;
                try { d.Rig.Dispose(); n++; } catch (Exception e) { AcLog.Warn("Ai: disposing " + d.Name + " failed: " + e.Message); }
                d.Rig = null;
            }
            bool had = List.Count > 0;
            List.Clear();
            AcAiTraffic.Reset();
            if (had) AcLog.Info("Ai: cleared (" + n + " car(s) removed)");
        }

        /// <summary>
        /// Non-master clients: call once a frame (R) - remembers the master's AI cars (N's AcNetGame.AiPlayers: car, skin, name, level, where)
        /// so that this client, if it becomes the master, continues them where they are; and drops AI cars this client still drove when it
        /// stopped being the master. Cheap (no allocation).
        /// </summary>
        public static void Observe()
        {
            if (List.Count > 0 && !IsMaster()) NotMaster();
            if (IsMaster()) return;
            AcAiField.Snapshot();
        }

        /// <summary>Called by a driver after it was put back on the track (logs the first ones).</summary>
        internal static void NoteRecovery(AcAiDriver d, string why)
        {
            if (_recoveryLines >= 30) return; // ours: no log flood
            if (d.Recoveries <= 3 || d.Recoveries % 10 == 0)
            {
                _recoveryLines++;
                AcLog.Info("Ai: " + d.Name + " recovered (" + why + ") x" + d.Recoveries);
            }
        }

        // ------------------------------------------------------------------ internals

        internal static bool IsMaster()
        {
            try { return AcNetGame.IsMaster; } catch (Exception) { return true; }
        }

        internal static bool IsAdopting() { return AcAiField.AdoptCount() > 0; }

        internal static bool StillBuilding(int gen) { return gen == _gen; }

        internal static void BuildDone(int gen)
        {
            if (gen != _gen) return;
            _building = null;
            _droveHere = true;
        }

        private static void HandOver()
        {
            int n = List.Count;
            Clear();
            _notMasterSince = -1f;
            _droveHere = false; // the blocks now carry the new master's cars: adopt them if the role comes back
            AcLog.Info("Ai: no longer the instance master - " + n + " AI car(s) handed over (the new master drives them)");
        }

        /// <summary>Prepares the fast / pit lane geometry for the loaded layout (once per layout).</summary>
        internal static void EnsureTracks()
        {
            AcAiSpline f = AcTrackWorld.AiLine, p = AcTrackWorld.PitLane;
            bool changed = false;
            if (f != _fastSrc)
            {
                _fastSrc = f;
                _fast = f != null && !f.IsEmpty ? new AcAiTrack(f) : null;
                _hints = null;
                AcAiField.ForgetPlans();
                changed = true;
                if (_fast != null)
                {
                    AcLog.Info("Ai: fast lane ready: " + f.Count + " points, " + _fast.Length.ToString("0") + " m, room from the payload " + _fast.HasSides);
                    LoadHints();
                }
            }
            if (p != _pitSrc)
            {
                _pitSrc = p;
                _pit = p != null && !p.IsEmpty ? new AcAiTrack(p) : null;
                AcAiField.ForgetPlans();
                changed = true;
                if (_pit != null) AcLog.Info("Ai: pit lane ready: " + p.Count + " points, " + _pit.Length.ToString("0") + " m");
            }
            if (changed) { FindMerge(); FindFinish(); }
        }

        /// <summary>Open lines (REQUESTS "R -> I: open courses"): where AI cars stop. <see cref="OpenRunOut"/> = the finish gate (AC_AB_FINISH /
        /// AC_OPEN_FINISH) is within 30 m of the line's end (trento-bondone): the plan reaches the end at AcAiPlan.RunOutSpeed and the driver runs
        /// straight through and brakes after it. Else (ks_drag: AC_OPEN_FINISH at 0.08..0.81 of the strip) the AI stops at the line's end as before - stopping right after the finish would mean braking before it (harness aiopen: 110 km/h at a 2000 m finish). <see cref="OpenStopS"/> stays -1 (kept for a later layout).</summary>
        internal static float OpenStopS = -1f;
        internal static bool OpenRunOut;

        private static void FindFinish()
        {
            OpenStopS = -1f; OpenRunOut = false;
            if (_fast == null || _fast.IsClosed) return;
            AcGate g = new AcGate { Index = -1 };
            try { g = AcTrackWorld.AbFinish.Index >= 0 ? AcTrackWorld.AbFinish : AcTrackWorld.OpenFinish; } catch (Exception) { }
            if (g.Index < 0) { AcLog.Info("Ai: open line without a finish gate - AI cars stop at its end"); return; }
            float x, y, z;
            AcTrackWorld.ToAc((g.Left + g.Right) * 0.5f, out x, out y, out z);
            int h = -1; float lat;
            float s = _fast.Project(x, y, z, ref h, out lat);
            if (_fast.Length - s < 30f) OpenRunOut = true; // ours
            // else: stop at the line's end (OpenStopS -1)
            AcLog.Info("Ai: open line: finish at " + s.ToString("0") + " of " + _fast.Length.ToString("0") + " m - AI cars " + (OpenRunOut ? "run through the line's end and stop after it" : "keep going past it and stop at the line's end"));
        }

        /// <summary>The layout's ai_hints.ini (null when it has none) - used by every fast-lane plan; DANGER zones already applied to <see cref="FastTrack"/>.</summary>
        public static AcAiHints Hints { get { return _hints; } }

        /// <summary>Pit exit: arc length on the pit line of its last point outside the fast lane's room (where an AI waits for traffic) and that
        /// point's arc length on the fast lane; -1 when unknown.</summary>
        internal static float PitMergeS = -1f, PitMergeFastS = -1f;

        private static AcAiHints _hints;

        private static void LoadHints()
        {
            AcLayoutEntry l = null;
            try { l = AcTrackWorld.Layout ?? AcSession.Layout; } catch (Exception) { }
            if (l == null || _fast == null) return;
            try
            {
                _hints = AcAiHints.Load(l.DataDir);
                if (_hints == null) return;
                int n = _hints.ApplyDanger(_fast);
                AcLog.Info("Ai: ai_hints.ini of " + l.Key + ": " + _hints + " (" + n + " fast-lane points with less room)");
            }
            catch (Exception e) { _hints = null; AcLog.Warn("Ai: ai_hints.ini: " + e.Message); }
        }

        /// <summary>Where pit_lane.ai rejoins the fast lane (AcAiTrack.PitExit): the AI joins the fast lane there and its pit limit ends; -1 =
        /// unknown (then it follows the pit line to 6 m before its end, as before).</summary>
        internal static float PitExitS = -1f;

        /// <summary>The pit exit (AcAiTrack.PitExit): the installed pit lines go on along the track for 200-1800 m after the pit lane rejoins it
        /// [VERIFIED harness aimerge on 29 circuit layouts] - the AI must leave them there, not at their end.</summary>
        private static void FindMerge()
        {
            PitMergeS = -1f; PitMergeFastS = -1f; PitExitS = -1f;
            if (_pit == null || _fast == null) return;
            float wait, exit, exitFast;
            if (AcAiTrack.PitExit(_pit, _fast, out wait, out exit, out exitFast))
            {
                PitMergeS = wait; PitExitS = exit; PitMergeFastS = exitFast;
                AcLog.Info("Ai: pit exit at " + exit.ToString("0") + " m of the " + _pit.Length.ToString("0") + " m pit line (fast lane s " + exitFast.ToString("0") + " m), waiting point " + wait.ToString("0") + " m");
            }
            else AcLog.Info("Ai: no pit exit found on this layout's pit line - AI cars follow it to its end");
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            AcSession.Unloading += OnUnloading;
            AcSession.LapCompleted += OnLap;
            AcSession.PhaseChanged += OnPhase;
        }

        private static void OnUnloading()
        {
            Clear();
            _droveHere = false;
            _fastSrc = null; _pitSrc = null; _fast = null; _pit = null; _hints = null;
            PitMergeS = -1f; PitMergeFastS = -1f; PitExitS = -1f;
            OpenStopS = -1f; OpenRunOut = false;
            AcAiField.ForgetPlans();
        }

        /// <summary>Grid: the AI cars of the practice now race - fuel for the race distance (and the plans re-made for that mass).</summary>
        private static void OnPhase(AcPhase from, AcPhase to)
        {
            if (to != AcPhase.Grid || List.Count == 0 || !IsMaster()) return;
            AcGame game = AcGame.Current;
            if (game == null) return;
            try { game.StartCoroutine(AcAiField.Refuel(_gen)); }
            catch (Exception e) { AcLog.Warn("Ai: race refuel failed: " + e.Message); }
        }

        private static void OnLap(AcLap lap)
        {
            if (lap == null || lap.PlayerId > -100) return;
            int idx = -100 - lap.PlayerId;
            for (int i = 0; i < List.Count; i++)
            {
                AcAiDriver d = List[i];
                if (d == null || d.Index != idx || d.LapsLogged >= DiagLaps) { if (d != null && d.Index == idx && d.Brain != null) d.Brain.DiagReset(); continue; }
                d.LapLogged = true;
                d.LapsLogged++;
                AcLog.Info("Ai: " + d.Name + " lap " + AcAiField.LapText(lap.TimeMs) + " (" + d.CarId + ", level " + d.Level + ", planned " + AcAiField.LapText((int)(d.PlannedLapS * 1000f))
                           + ", recoveries " + d.Recoveries + (lap.Valid ? "" : ", cut") + ")" + (d.Brain != null ? " | " + d.Brain.DiagText() : ""));
                if (d.Brain != null) d.Brain.DiagReset();
            }
        }

        /// <summary>Laps per driver logged with the brain's diagnostics (lab / client evidence of why a lap is slow). // ours</summary>
        public const int DiagLaps = 3; // ours
    }
}
