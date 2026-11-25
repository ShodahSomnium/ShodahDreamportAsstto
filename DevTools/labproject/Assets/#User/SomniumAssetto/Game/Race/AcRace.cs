using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] The session conductor (AcGame.Race.cs calls Tick / Stop): AcSession flow Hub -> Loading -> Practice -> Grid -> Countdown ->
    /// Racing -> Finished -> Results -> Practice; track loads through W, the local car through P's AcCarRig (pit box / grid / hotlap start,
    /// raycast down - research T1/T2), start lights (W meshes + H panel + A beeps), timing (AC_TIME gates, sectors, validity, T3 lap rule),
    /// positions, pit lane + limiter, results. On its first Tick it starts the race-side modules (AcSeat.Start, AcHud.Start).
    /// Master-only actions go through N (AcNetGame.RaceControl); every client applies commands in <see cref="Apply"/>.
    ///
    /// The command model (Halo HaloNet.Master semantics): the instance master publishes an <see cref="AcRaceCommand"/>; every client - late
    /// joiners too - applies the newest Seq. Phases the master publishes: Practice (track load / back to practice / a qualify or hotlap
    /// session start: <see cref="AcRaceCommand.StartUtc"/> = session start), Grid (race START: grid order + StartUtc = lights out on the
    /// shared UTC clock; Countdown and Racing follow from the clock on every client), Results, Hub. Finished is local (the leader's finish
    /// is seen in the records). Timing is owner-authoritative (each client times its own car; the master its AI). A new master keeps
    /// publishing from the newest command it read (N raises Seq above any seen; R's master decisions only look at the command and the
    /// clock, so they carry over a hand-over).
    /// Files: AcRace.cs (face, tick, apply), AcRace.Load.cs (load, spawn, teardown), AcRace.Flow.cs (practice, grid, lights, race, results),
    /// AcRaceDirector.cs (master decisions), AcTiming.cs, AcStandingsBoard.cs, AcSpawner.cs, AcRaceMath.cs / AcRaceTimer.cs (Unity-free).
    /// </summary>
    public static partial class AcRace
    {
        private static bool _started;
        private static readonly List<AcStanding> StandingList = new List<AcStanding>();
        private static AcGame _game;
        private static AcRaceDefaults _defaults;
        private static AcRaceCommand _cmd;          // the command applied last (a clone)
        private static int _appliedSeq = int.MinValue;
        /// <summary>The MasterId of the applied command: (Seq, MasterId) is a command's identity (as N's StepCommands) - an offline seq 1 (master -1)
        /// must not shadow the room's seq 1.</summary>
        private static int _appliedMaster = int.MinValue;
        private static double _cmdAppliedUtc;
        private static float _nextStandings;

        /// <summary>The local player's own car (null in the hub / before spawn). Same object as AcLocal.Car.</summary>
        public static AcCarRig LocalCar { get; private set; }

        /// <summary>Current standings, position order (practice / qualify: by best lap; race: by race distance). Do not keep the list.</summary>
        public static IReadOnlyList<AcStanding> Standings { get { return StandingList; } }

        // ---- added by R (read-only helpers for F / H / N) --------------------------------------------------------------------

        /// <summary>The command applied last (null before any). Do not modify.</summary>
        public static AcRaceCommand Command { get { return _cmd; } }
        /// <summary>My pit box (AC_PIT_n index; -1 none).</summary>
        public static int PitBox { get; private set; } = -1;
        /// <summary>A race runs and I am not in its grid (late joiner): my car waits in the pit box.</summary>
        public static bool Spectating { get; private set; }
        /// <summary>Lit start lights now (0..5) and green (HUD / lab).</summary>
        public static int LightsReds { get; private set; }
        public static bool LightsGreen { get; private set; }
        /// <summary>Seconds left in the timed session (qualify, timed race), -1 when untimed.</summary>
        public static float SessionTimeLeft
        {
            get
            {
                if (_cmd == null || AcSession.Rules == null || AcSession.StartUtc <= 0) return -1f;
                AcRules r = AcSession.Rules;
                bool timed = (TimedSessionActive && r.TimeMinutes > 0) || (AcSession.InRace && r.Laps <= 0 && r.TimeMinutes > 0 && AcSession.Phase >= AcPhase.Racing);
                if (!timed) return -1f;
                return Mathf.Max(0f, (float)(AcSession.StartUtc + r.TimeMinutes * 60.0 - AcSession.UtcNow));
            }
        }
        /// <summary>Seconds since the green (racing), 0 otherwise.</summary>
        public static float RaceElapsed { get { return AcSession.Phase >= AcPhase.Racing && AcSession.Phase <= AcPhase.Results && AcSession.StartUtc > 0 ? Mathf.Max(0f, (float)(AcSession.UtcNow - AcSession.StartUtc)) : 0f; } }
        /// <summary>A qualifying session is running (its clock started).</summary>
        public static bool QualifyActive { get { return _cmd != null && _cmd.Phase == AcPhase.Practice && AcSession.Rules != null && AcSession.Rules.Session == AcSessionType.Qualify && _cmd.StartUtc > 0; } }
        /// <summary>A timed practice is running: START with Session Practice and TIME (AcRaceDefaults) - its clock, lap cutoff and end run like qualify.</summary>
        public static bool TimedPracticeActive { get { return _cmd != null && _cmd.Phase == AcPhase.Practice && AcSession.Rules != null && AcSession.Rules.Session == AcSessionType.Practice && _cmd.StartUtc > 0 && AcSession.Rules.TimeMinutes > 0; } }
        /// <summary>A timed non-race session (qualify, timed practice) is running.</summary>
        public static bool TimedSessionActive { get { return QualifyActive || TimedPracticeActive; } }
        /// <summary>This client may command the session (alone, or the instance master once known) - F greys the lobby otherwise.</summary>
        public static bool CanCommand { get { try { return AcNetGame.IsMaster && AcNetGame.MasterKnown; } catch (Exception) { return true; } } }
        /// <summary>The lobby defaults read from the install (server_cfg.ini names and values), a fresh copy.</summary>
        public static AcRules DefaultRules() { return _defaults != null ? _defaults.Rules() : new AcRules(); }
        /// <summary>The install defaults object (OVER_TIME_MULT, QUALIFY_MAX_WAIT_PERC, result-screen and grid times).</summary>
        public static AcRaceDefaults Defaults { get { return _defaults ?? (_defaults = new AcRaceDefaults()); } }
        /// <summary>Timing of AI car <paramref name="index"/> on the master (N packs it into the AI blocks); null when not timed here.</summary>
        public static AcRaceTimer AiTimer(int index) { AcCarTiming c = AcTiming.AiTiming(index, false); return c != null ? c.Timer : null; }

        /// <summary>Once a frame (from AcGame.RaceTick). Starts the race-side modules on the first call.</summary>
        public static void Tick(AcGame game)
        {
            if (!_started) StartModules(game);
            _game = game;
            if (_defaults == null && game.Install != null) LoadDefaults(game.Install.Root);
            try { AcRaceClock.Tick(); } catch (Exception e) { if (_clockErrors++ < 3) AcLog.Error("Race: clock: " + e); }
            PollCommand();
            switch (AcSession.Phase)
            {
                case AcPhase.Hub:
                case AcPhase.Loading:
                    break;
                case AcPhase.Practice:
                    PracticeTick();
                    break;
                case AcPhase.Grid:
                case AcPhase.Countdown:
                case AcPhase.Racing:
                case AcPhase.Finished:
                    RaceTick();
                    break;
                case AcPhase.Results:
                    ResultsTick();
                    break;
            }
            if (AcSession.OnTrack)
            {
                LocalInputTick();
                DamageTick();                                  // iteration 2: pit-box repair timer, retired cars (AcRace.Damage.cs)
                AcTiming.RemoteLapsStep();
                if (Time.unscaledTime >= _nextStandings) { _nextStandings = Time.unscaledTime + 0.1f; UpdateStandings(); } // ours: 10 Hz
                AcRaceDirector.Tick(_cmd, _cmdAppliedUtc);
                AiTick();
            }
        }

        // Separate method: a lambda capturing a parameter allocates its closure at METHOD ENTRY, so it must not live in Tick.
        private static void StartModules(AcGame game)
        {
            _started = true;
            _game = game;
            AcLog.Info("Race: conductor up (" + AcVersion.Label + ")");
            try { AcNetGame.RaceControl.Changed += OnRaceCommand; } catch (Exception e) { AcLog.Warn("Race: cannot follow race control: " + e.Message); }
            AcGame.Guard("seat start", () => AcSeat.Start(game));
            AcGame.Guard("hud start", () => AcHud.Start(game));
        }

        private static int _aiErrors, _clockErrors;
        private static bool _aiMaster;

        /// <summary>The loaded layout is an open course (point-to-point / drag): one run start -> finish is the "lap"; a race is one run. Added by R.</summary>
        public static bool OpenCourse { get { return AcTiming.OpenCourse; } }

        /// <summary>The master drives the AI field (module I) once a frame on a track; every other client lets I observe the master's AI cars
        /// (so a hand-over continues them where they are) and keeps their timing for seeding (<see cref="SeedAiTimer"/>).</summary>
        private static void AiTick()
        {
            bool master;
            try { master = AcNetGame.IsMaster; } catch (Exception) { master = true; }
            if (!master)
            {
                _aiMaster = false;
                try { AcAi.Observe(); }
                catch (Exception e) { if (_aiErrors++ < 3) AcLog.Error("Race: AcAi.Observe failed: " + e); }
                RememberRemoteAi();
                return;
            }
            if (!_aiMaster)
            {
                // just became the master on a running session (hand-over): start driving the field (I adopts the old master's cars)
                _aiMaster = true;
                if (AcSession.Phase != AcPhase.Loading && _cmd != null) EnsureAi(AcSession.Rules);
            }
            try { AcAi.Tick(); }
            catch (Exception e) { if (_aiErrors++ < 3) AcLog.Error("Race: AcAi.Tick failed: " + e); }
        }

        // ---- the master's AI timing as seen here (N's AiPlayers is empty on the master itself, so a new master must have kept it)

        private struct AiSeen { public bool Known; public int Laps, BestMs, LastMs, FinishedMs, PenaltyMs; public float RaceDistance; public bool Finished; public double StartUtc; }
        private static readonly AiSeen[] RemoteAi = new AiSeen[AcTiming.MaxAiCars];

        private static void RememberRemoteAi()
        {
            System.Collections.Generic.IReadOnlyList<AcNetPlayer> ai;
            try { ai = AcNetGame.AiPlayers; } catch (Exception) { return; }
            double start = AcSession.InRace ? AcSession.StartUtc : 0;
            for (int i = 0; i < ai.Count; i++)
            {
                AcNetPlayer p = ai[i];
                if (p == null || p.AiIndex < 0 || p.AiIndex >= RemoteAi.Length || !p.OnTrack) continue;
                RemoteAi[p.AiIndex] = new AiSeen
                {
                    Known = true, Laps = p.Laps, BestMs = p.BestMs, LastMs = p.LastMs, RaceDistance = p.RaceDistance, Finished = p.Finished,
                    PenaltyMs = Math.Max(0, p.PenaltyMs), FinishedMs = p.Finished ? Math.Max(0, p.FinishedMs - Math.Max(0, p.PenaltyMs)) : 0, StartUtc = start,
                };
            }
        }

        private static void ForgetRemoteAi() { for (int i = 0; i < RemoteAi.Length; i++) RemoteAi[i].Known = false; }

        /// <summary>A race timer just begun for AI car <paramref name="index"/> after the green: carry over the laps the previous master
        /// published (same race only). Returns true when seeded.</summary>
        private static bool SeedAiTimer(AcCarTiming c, int index)
        {
            if (index < 0 || index >= RemoteAi.Length) return false;
            AiSeen s = RemoteAi[index];
            if (!s.Known || s.StartUtc <= 0 || Math.Abs(s.StartUtc - AcSession.StartUtc) > 0.001) return false;
            c.Timer.Seed(s.Laps, s.RaceDistance, s.BestMs, s.LastMs, s.Finished, s.FinishedMs, s.PenaltyMs);
            AcLog.Info("Race: AI " + (string.IsNullOrEmpty(c.Name) ? "#" + index : c.Name) + " timing carried over from the previous master (lap " + s.Laps
                       + ", distance " + s.RaceDistance.ToString("0.00") + (s.Finished ? ", finished" : "") + ")");
            return true;
        }

        private static void LoadDefaults(string root)
        {
            _defaults = AcRaceDefaults.Load(root);
            if (AcSession.Phase == AcPhase.Hub) AcSession.SetRules(_defaults.Rules());
            AcLog.Info("Race: defaults " + _defaults);
        }

        // N fires RaceControl.Changed once per new command (master publish, network, late join); the per-frame poll is the backstop.
        private static void OnRaceCommand(AcRaceCommand c) { Apply(c); }

        /// <summary>World closing: get out, unload the track, forget the session.</summary>
        public static void Stop()
        {
            try { AcNetGame.RaceControl.Changed -= OnRaceCommand; } catch (Exception) { }
            try { Teardown("the world closes", false); }
            catch (Exception e) { AcLog.Error("Race: stop: " + e); }
            AcGame.Guard("seat stop", AcSeat.Stop);
            AcGame.Guard("hud stop", AcHud.Stop);
            _started = false;
            _cmd = null; _appliedSeq = int.MinValue; _appliedMaster = int.MinValue; _game = null;
            _optedOutKey = null; _defaults = null; _aiMaster = false;
            CancelRepair("the world closes");
            AcCarDamageGate.Reset();
            ForgetRemoteAi();
            AcSession.SetClockOffset(0.0);
            StandingList.Clear();
        }

        // ------------------------------------------------------------------ commands

        private static void PollCommand()
        {
            AcRaceCommand cur = null;
            try { cur = AcNetGame.RaceControl != null ? AcNetGame.RaceControl.Current : null; }
            catch (Exception e) { AcLog.WarnOnce("Race: reading race control failed: " + e.Message); }
            if (cur != null && (cur.Seq != _appliedSeq || cur.MasterId != _appliedMaster)) Apply(cur);
        }

        /// <summary>Applies the master's race command (every client; late joiners get the current one): load the track, grid, start time...</summary>
        public static void Apply(AcRaceCommand command)
        {
            if (command == null) return;
            if (command.Seq == _appliedSeq && command.MasterId == _appliedMaster && _cmd != null) return;   // N's Changed and our poll both deliver it
            AcGame g = _game ?? AcGame.Current;
            if (command.Phase != AcPhase.Hub && (g == null || g.Content == null)) { AcLog.InfoOnce("Race: a race command waits for the content catalog"); return; } // the poll retries
            AcRaceCommand c = command.Clone();
            AcRaceCommand prev = _cmd;
            _cmd = c;
            _appliedSeq = c.Seq;
            _appliedMaster = c.MasterId;
            _cmdAppliedUtc = AcSession.UtcNow;
            AcLocal.CommandSeq = c.Seq;
            if (c.Rules == null) c.Rules = DefaultRules();
            AcLog.Info("Race: command " + c + (prev != null && prev.MasterId != c.MasterId ? " (new master)" : ""));

            if (c.Phase == AcPhase.Hub) { _optedOutKey = null; GoHub(); return; }

            AcLayoutEntry layout = ResolveLayout(c.TrackId, c.LayoutId);
            if (layout != null && _optedOutKey != null)
            {
                if (_optedOutKey == layout.Key) { AcLog.Info("Race: staying in the hub (left this session; JoinSession to come back)"); return; }
                _optedOutKey = null;                                   // the master moved on to another track: follow again
            }
            if (layout == null)
            {
                string what = string.IsNullOrEmpty(c.LayoutId) ? c.TrackId : c.TrackId + "/" + c.LayoutId;
                AcLog.Warn("Race: the track " + what + " of the command is not in this install (or not owned) - staying in the hub");
                Toast("The instance plays " + what + ", which is not installed here.");
                if (AcSession.Phase != AcPhase.Hub) Teardown("track not installed", true);
                return;
            }

            if (AcSession.Phase == AcPhase.Hub || AcSession.Layout == null || AcSession.Layout.Key != layout.Key)
            {
                StartLoad(layout, c, _selfPublished ? "me" : "master " + MasterNameSafe());
                return;
            }
            if (AcSession.Phase == AcPhase.Loading) return;          // the load enters the newest command when it is done
            EnterCommandPhase(prev);
        }

        private static bool _selfPublished;

        private static string MasterNameSafe() { try { return AcNetGame.MasterName; } catch (Exception) { return "?"; } }

        private static AcLayoutEntry ResolveLayout(string trackId, string layoutId)
        {
            AcContent content = _game != null ? _game.Content : (AcGame.Current != null ? AcGame.Current.Content : null);
            if (content == null || string.IsNullOrEmpty(trackId)) return null;
            AcLayoutEntry l = content.Layout(trackId, layoutId ?? "");
            if (l == null && string.IsNullOrEmpty(layoutId))
            {
                AcTrackEntry t = content.Track(trackId);
                if (t != null && t.Layouts.Count > 0) l = t.Layouts[0];
            }
            return l != null && l.Owned ? l : null;
        }

        /// <summary>Publishes a command as master (alone: applied locally by N's offline Publish). False + toast when not allowed.</summary>
        private static bool Publish(AcRaceCommand c, string what)
        {
            if (!CanCommand)
            {
                string m;
                try { m = AcNetGame.MasterName; } catch (Exception) { m = "the instance master"; }
                Toast("Only the instance master (" + m + ") can " + what + ".");
                AcLog.Info("Race: refused to " + what + " (not the instance master)");
                return false;
            }
            c.MasterId = AcLocal.PhotonId;
            if (c.PhaseUtc <= 0) c.PhaseUtc = AcSession.UtcNow;
            bool ok = false;
            _selfPublished = true;     // N may call Apply synchronously from Publish (RaceControl.Changed)
            try
            {
                ok = AcNetGame.RaceControl.Publish(c);
                AcRaceCommand cur = ok ? AcNetGame.RaceControl.Current : null;
                if (cur != null) Apply(cur);
            }
            catch (Exception e) { AcLog.Error("Race: publishing failed: " + e); }
            finally { _selfPublished = false; }
            if (!ok) { Toast("Could not " + what + " (race control refused)."); return false; }
            return true;
        }

        /// <summary>The master director's publish (same refusal rules as the tablet requests).</summary>
        internal static bool PublishFromDirector(AcRaceCommand c, string what) { return Publish(c, what); }

        private static string _optedOutKey;

        /// <summary>Back into the instance's running session after leaving it alone (re-applies the current command). Added by R.</summary>
        public static bool JoinSession()
        {
            _optedOutKey = null;
            AcRaceCommand cur = null;
            try { cur = AcNetGame.RaceControl.Current; } catch (Exception) { }
            if (cur == null || cur.Phase == AcPhase.Hub) return false;
            _appliedSeq = int.MinValue;
            Apply(cur);
            return true;
        }

        private static bool IsInstanceLayout(AcLayoutEntry layout)
        {
            AcRaceCommand cur = null;
            try { cur = AcNetGame.RaceControl.Current; } catch (Exception) { }
            if (cur == null || cur.Phase == AcPhase.Hub) return false;
            AcLayoutEntry l = ResolveLayout(cur.TrackId, cur.LayoutId);
            return l != null && l.Key == layout.Key;
        }

        private static AcRaceCommand NewCommand(AcPhase phase)
        {
            var c = new AcRaceCommand { Phase = phase };
            if (_cmd != null) { c.TrackId = _cmd.TrackId; c.LayoutId = _cmd.LayoutId; c.Rules = (_cmd.Rules ?? DefaultRules()).Clone(); }
            else if (AcSession.Layout != null) { c.TrackId = AcSession.Layout.TrackId; c.LayoutId = AcSession.Layout.Id; c.Rules = AcSession.Rules.Clone(); }
            return c;
        }

        /// <summary>Tablet "Drive" / track pick: alone or as master -> publish (N) and load; otherwise refused (false) with a toast.</summary>
        public static bool RequestLoad(AcLayoutEntry layout, AcRules rules)
        {
            if (layout == null) return false;
            if (!layout.Owned) { Toast(layout.Name + " is not installed."); return false; }
            if (!CanCommand && IsInstanceLayout(layout))
            {
                // a non-master picking the track the instance plays = going (back) to it - also after leaving it alone (RequestHub)
                if (AcSession.OnTrack && AcSession.Layout != null && AcSession.Layout.Key == layout.Key && _optedOutKey == null) return true;
                AcLog.Info("Race: joining the instance's session on " + layout.Key + " (track pick by a non-master)");
                return JoinSession();
            }
            var c = new AcRaceCommand
            {
                Phase = AcPhase.Practice, TrackId = layout.TrackId, LayoutId = layout.Id ?? "",
                Rules = (rules ?? AcSession.Rules ?? DefaultRules()).Clone(),
                StartUtc = 0,      // a qualify / race waits for START once everybody has loaded
            };
            return Publish(c, "choose the track");
        }

        /// <summary>Master: change the lobby rules for everyone (practice keeps running; the next START uses them). Added by R.</summary>
        public static bool RequestRules(AcRules rules)
        {
            if (rules == null || _cmd == null || !AcSession.OnTrack) return false;
            if (AcSession.InRace) { Toast("The rules can change after the race."); return false; }
            AcRaceCommand c = NewCommand(AcPhase.Practice);
            c.Rules = rules.Clone();
            c.StartUtc = 0;
            return Publish(c, "change the rules");
        }

        /// <summary>Master "START": grid the racers, lights, race (false when not master / no track).</summary>
        public static bool RequestStart()
        {
            AcRules lobby = null;
            try { lobby = AcFront.LobbyRules; } catch (Exception e) { AcLog.Warn("Race: AcFront.LobbyRules failed: " + e.Message); }   // F: the master's edited rules
            return RequestStart(lobby);
        }

        /// <summary>Master "START" with these rules (null = the current ones). Race: grid + lights; Qualify / Hotlap / Practice: that session
        /// starts now for everyone (timing reset). Added by R.</summary>
        public static bool RequestStart(AcRules rules)
        {
            if (!AcSession.OnTrack || _cmd == null) { Toast("Load a track first."); return false; }
            if (AcSession.InRace) { Toast("A race is already running."); return false; }
            if (!CanCommand) return Publish(new AcRaceCommand(), "start the session");
            AcRules r = (rules ?? AcSession.Rules ?? DefaultRules()).Clone();
            if (r.Session == AcSessionType.Race)
            {
                AcRaceCommand c = NewCommand(AcPhase.Grid);
                c.Rules = r;
                // the AI field of the NEW rules first (I lists its drivers at once, cars build after), so the grid holds their indices
                EnsureAi(r);
                AcRaceDirector.BuildGrid(c.Grid, r);
                if (c.Grid.Count == 0) { Toast("Nobody is on the track to race."); return false; }
                // green = now + grid time + 4 red steps + the mid hold (2 s); the real hold is a pure function of StartUtc's ms value
                // (AcRaceMath.HoldMs, same on every client), so the first red comes 9..11 s after START
                double now = AcSession.UtcNow;
                double lightsAt = now + Defaults.GridSeconds;
                double start = Math.Round((lightsAt + (AcRaceMath.RedLights - 1) * AcRaceMath.RedStepSeconds + 2.0) * 1000.0) / 1000.0;
                c.StartUtc = start;
                return Publish(c, "start the race");
            }
            AcRaceCommand s = NewCommand(AcPhase.Practice);
            s.Rules = r;
            s.StartUtc = Math.Round(AcSession.UtcNow * 1000.0) / 1000.0;
            return Publish(s, "start the " + r.Session.ToString().ToLowerInvariant() + " session");
        }

        /// <summary>Master "END RACE FOR EVERYONE": back to practice.</summary>
        public static bool RequestStop()
        {
            if (!AcSession.OnTrack || _cmd == null) return false;
            AcRaceCommand c = NewCommand(AcPhase.Practice);
            c.StartUtc = 0;
            return Publish(c, "end the session");
        }

        /// <summary>Master: leave the track, everyone back to the hub. Others (added by R): leave the session alone (back to the hub; the
        /// master's session goes on - <see cref="JoinSession"/> or the master's next track brings this client back).</summary>
        public static bool RequestHub()
        {
            if (!CanCommand)
            {
                if (AcSession.Phase == AcPhase.Hub) return false;
                _optedOutKey = AcSession.Layout != null ? AcSession.Layout.Key : "";
                AcLog.Info("Race: left the session alone (the master's session goes on)");
                GoHub();
                return true;
            }
            AcRaceCommand c = NewCommand(AcPhase.Hub);
            c.TrackId = ""; c.LayoutId = "";
            return Publish(c, "leave the track");
        }

        /// <summary>Local: put my car back in my pit box (tablet "Back to pits"; refused in a race unless finished / retired). Iteration 2:
        /// BACK TO PITS is a full instant repair (DAMAGE_DESIGN 1.6) and cancels a running pit-box repair.</summary>
        public static bool BackToPits()
        {
            if (!AcSession.OnTrack) return false;
            if (AcSession.Phase >= AcPhase.Grid && AcSession.Phase <= AcPhase.Racing && !Spectating && !AcTiming.Local.Timer.Finished && !AcTiming.Local.Retired)
            {
                Toast("Back to the pits is not possible during the race.");
                return false;
            }
            if (LocalCar == null) { Toast("Your car is still being built."); return false; }
            CancelRepair("back to pits");
            PlaceInPits(LocalCar, "back to pits");
            RepairCar(LocalCar, "back to pits");
            if (AcTiming.Local.Retired && LocalCar.Physics != null) LocalCar.Physics.Damage.Retired = true;   // still DNF in this race
            return true;
        }

        /// <summary>Reset to the track (AC's "reset car"): my car back onto the racing line at the nearest point, heading along it, the lap
        /// invalidated. Same rules as the R key (AcCarInput.ResetRequest): not on the grid / during the lights, not while spectating, once per
        /// 1.5 s. For the VR tablet / any button (S produces ResetRequest from the keyboard only). Added by R.</summary>
        public static bool ResetCar()
        {
            if (!AcSession.OnTrack) return false;
            AcCarRig rig = LocalCar;
            if (rig == null || rig.Physics == null) { Toast("Your car is still being built."); return false; }
            if (AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown) { Toast("No reset on the grid."); return false; }
            if (Spectating) return false;
            if (Time.unscaledTime < _nextReset) return false;
            _nextReset = Time.unscaledTime + ResetRateSeconds;
            ResetToTrack(rig);
            return true;
        }

        /// <summary>Local: switch to another car / skin (respawns my car in my pit box when on a track).</summary>
        public static bool ChooseCar(AcCarEntry car, string skinId)
        {
            if (car == null) return false;
            if (!car.Owned || car.IsDlcStub) { Toast(car.Name + " is not installed."); return false; }
            if (!CarAllowed(car.Id)) { Toast(car.Name + " is not allowed in this session."); return false; }
            _wantCar = car.Id; _wantSkin = skinId ?? "";
            if (!AcSession.OnTrack) return true;                      // used at the next load
            if (AcSession.InRace && !Spectating) { Toast("You can change the car after the race."); return false; }
            if (LocalCar != null && LocalCar.Car == car && LocalCar.SkinId == (skinId ?? "")) return true;
            RespawnLocalCar("car change");
            return true;
        }

        private static void Toast(string text)
        {
            AcGame.Guard("front toast", () => AcFront.Toast(text));
        }

        private static void Message(string text, float seconds)
        {
            AcGame.Guard("race message", () => AcEvents.RaiseMessage(text, seconds));
        }

        private static void UpdateStandings()
        {
            if (AcSession.Phase == AcPhase.Results && _resultsFrozen) return;   // final: built in EnterResults (+ the master's order)
            bool race = AcSession.Phase >= AcPhase.Grid && AcSession.Phase <= AcPhase.Results && _cmd != null && _cmd.Grid.Count > 0;
            bool gridOrder = AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown;
            try { AcStandingsBoard.Build(StandingList, _cmd, race, gridOrder); }
            catch (Exception e) { AcLog.WarnOnce("Race: standings failed: " + e); return; }
            int pos = 0;
            for (int i = 0; i < StandingList.Count; i++) if (StandingList[i].IsLocal) { pos = StandingList[i].Spectating ? 0 : StandingList[i].Position; break; }
            AcLocal.Position = pos;
        }
    }
}
