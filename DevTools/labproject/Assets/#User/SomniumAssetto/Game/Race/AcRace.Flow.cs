using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[R, Race] The session phases on every client: practice / qualify / hotlap, the race (grid, start lights on the shared clock,
    /// jump start, green, laps, finish) and the results.</summary>
    public static partial class AcRace
    {
        /// <summary>The green light stays up this long before the start-light panel hides (s). // ours</summary>
        public const float GreenShowSeconds = 3f; // ours
        /// <summary>A client that finished loading less than this after the green still starts from its grid slot (s), else from the pit box. // ours</summary>
        public const float LateGridSeconds = 5f; // ours

        /// <summary>One reset to the track per this many seconds (R key, VR tablet button). // ours</summary>
        public const float ResetRateSeconds = 1.5f; // ours
        /// <summary>A non-master waits this long on the results screen for the master's published order before showing its own (s). // ours</summary>
        public const float ResultsWaitSeconds = 3f; // ours

        private static string _sessionKey;
        private static double _firstFinishUtc;
        private static bool _raceStarted, _resultsNotified, _resultsFrozen, _netResultsApplied, _spectateTold, _seatLogged;
        private static float _greenHideAt = -1f, _nextSeatTry, _nextReset, _resultsWaitUntil;
        // cached delegates: a method group passed to AcGame.Guard allocates a new delegate on every call
        private static readonly Action StartBeepCall = AcSfx.StartBeep;
        private static readonly Action GoBeepCall = AcSfx.GoBeep;

        /// <summary>When this client saw the first finisher (UTC s; 0 = nobody yet).</summary>
        public static double FirstFinishUtc { get { return _firstFinishUtc; } }

        private static void EnterCommandPhase(AcRaceCommand prev)
        {
            if (_cmd == null) return;
            AcSession.SetRules(_cmd.Rules.Clone());
            AcCarDamageGate.Refresh(AcSession.Rules);         // iteration 2: damage textures for cars built from now on
            EnforceAllowedCar();
            switch (_cmd.Phase)
            {
                case AcPhase.Grid:
                case AcPhase.Countdown:
                case AcPhase.Racing:
                case AcPhase.Finished:
                    EnterRace();
                    break;
                case AcPhase.Results:
                    EnterResults();
                    break;
                default:
                    EnterPractice();
                    break;
            }
        }

        /// <summary>The rules (a START or RequestRules) may narrow AllowedCars: a player in a car the session no longer allows gets an allowed one.</summary>
        private static void EnforceAllowedCar()
        {
            AcCarRig rig = LocalCar;
            if (rig == null || rig.Car == null || _spawning != null || CarAllowed(rig.Car.Id)) return;
            if (AcSession.Phase >= AcPhase.Racing && AcSession.Phase <= AcPhase.Finished) return;   // never mid-race (rules cannot change there)
            AcLog.Info("Race: my car " + rig.Car.Id + " is not allowed by the session's rules (" + AcSession.Rules.AllowedCars.Count + " car(s) allowed) - switching");
            Toast(rig.Car.Name + " is not allowed in this session - you get an allowed car.");
            RespawnLocalCar("car not allowed by the rules");
        }

        // ------------------------------------------------------------------ practice / qualify / hotlap

        private static void EnterPractice()
        {
            AcRules r = AcSession.Rules;
            string key = "P|" + r.Session + "|" + _cmd.StartUtc.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            bool newSession = key != _sessionKey;
            bool fromRace = AcSession.Phase >= AcPhase.Grid;
            _sessionKey = key;
            AcSession.StartUtc = _cmd.StartUtc;
            Spectating = false; _spectateTold = false;
            _resultsNotified = false; _resultsFrozen = false;
            AcTiming.Local.GridSlot = -1; AcLocal.GridSlot = -1;
            for (int i = 0; i < AcTiming.Ai.Count; i++) AcTiming.Ai[i].Benched = false;
            LightsOff();
            FreezeAll(false);
            if (fromRace || newSession) ClearRetired();
            if (LocalCar != null)
            {
                if (fromRace) PlaceInPits(LocalCar, "back to practice");
                else if (newSession && (r.Session == AcSessionType.Hotlap || (r.Session == AcSessionType.Qualify && _cmd.StartUtc > 0)) && AcSession.Phase == AcPhase.Practice)
                    PlaceInPits(LocalCar, r.Session == AcSessionType.Hotlap ? "hotlap session" : "qualifying starts from the pits");
                // iteration 2 (DAMAGE_DESIGN 1.6): every session starts clean (the repair is here, never inside PlaceInPits)
                if (fromRace || newSession) RepairCar(LocalCar, fromRace ? "back to practice" : "new session");
                if (newSession || !AcTiming.Local.Begun) AcTiming.Begin(AcTiming.Local, AcTimerMode.Timed, AcSpawner.CarClock(LocalCar));
            }
            if (fromRace || newSession) RepairAiCars(fromRace ? "back to practice" : "new session");
            if (newSession) for (int i = 0; i < AcTiming.Ai.Count; i++) AcTiming.Ai[i].Begun = false;
            if (fromRace) AcLog.Info("Race: back to practice");
            EnsureAi(AcSession.Rules);
            AcSession.SetPhase(AcPhase.Practice);
            if (newSession && QualifyActive)
            {
                AcLog.Info("Race: qualify started (" + r.TimeMinutes + " min, QUALIFY_MAX_WAIT_PERC " + Defaults.QualifyMaxWaitPerc + " %)");
                Message("QUALIFY - " + r.TimeMinutes + " MIN", 4f);
            }
            else if (newSession && TimedPracticeActive)
            {
                AcLog.Info("Race: timed practice started (" + r.TimeMinutes + " min)");
                Message("PRACTICE - " + r.TimeMinutes + " MIN", 4f);
            }
            else if (newSession && r.Session == AcSessionType.Hotlap) Message("HOTLAP", 3f);
        }

        private static void PracticeTick()
        {
            AcCarTiming me = AcTiming.Local;
            if (LocalCar != null)
            {
                if (!me.Begun) AcTiming.Begin(me, AcTimerMode.Timed, AcSpawner.CarClock(LocalCar));
                me.Timer.CutoffTime = TimedSessionActive ? ClockAt(AcSession.StartUtc + AcSession.Rules.TimeMinutes * 60.0, LocalCar) : double.PositiveInfinity;
                AcTiming.Step(me);
            }
            AiTimingTick(AcTimerMode.Timed);
        }

        /// <summary>A UTC instant on a car's timing clock (physics time).</summary>
        private static double ClockAt(double utc, AcCarRig rig) { return AcSpawner.CarClock(rig) + (utc - AcSession.UtcNow); }

        // ------------------------------------------------------------------ race

        private static void EnterRace()
        {
            AcRules r = AcSession.Rules;
            string key = "R|" + _cmd.StartUtc.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            bool newRace = key != _sessionKey;
            if (!newRace && AcSession.InRace) return;      // the same race again (master hand-over re-publish, rules echo): keep racing
            _sessionKey = key;
            AcSession.StartUtc = _cmd.StartUtc;
            int idx = _cmd.Grid.IndexOf(AcLocal.PhotonId);
            int slot = idx >= 0 && idx < AcTrackWorld.StartGrid.Length ? idx : -1;
            Spectating = slot < 0;
            AcTiming.Local.GridSlot = slot;
            AcLocal.GridSlot = slot;
            double now = AcSession.UtcNow, start = _cmd.StartUtc, lightsAt = AcRaceMath.LightsStartUtc(start);
            if (newRace)
            {
                _raceStarted = false; _resultsNotified = false; _resultsFrozen = false; _firstFinishUtc = 0; _greenHideAt = -1f; _seatLogged = false;
                ClearRetired();                                // iteration 2: the cars are repaired on their grid slot (GridPlace)
                LightsReds = 0; LightsGreen = false;
                AcTiming.Local.JumpStart = false; AcTiming.Local.OnGrid = false; AcTiming.Local.Begun = false;
                try { if (AcTrackWorld.StartLights != null) AcTrackWorld.StartLights.SetJumpStart(false); } catch (Exception) { }   // lead (W -> R)
                for (int i = 0; i < AcTiming.Ai.Count; i++) { AcTiming.Ai[i].OnGrid = false; AcTiming.Ai[i].Begun = false; AcTiming.Ai[i].JumpStart = false; AcTiming.Ai[i].Benched = false; ClearFinish(AcTiming.Ai[i].Timer); }
                ClearFinish(AcTiming.Local.Timer);
                AcTiming.ClearLocalFields();
                AcLocal.GridSlot = slot;
                AcLog.Info("Race: grid: " + _cmd.Grid.Count + " cars, pole " + NameOf(_cmd.Grid.Count > 0 ? _cmd.Grid[0] : int.MinValue) + ", start at UTC "
                           + UtcText(start) + " (lights " + (start - lightsAt).ToString("0.0") + " s before), my slot " + (slot >= 0 ? slot.ToString() : "none (spectating)")
                           + ", " + (r.Laps > 0 ? r.Laps + " laps" : r.TimeMinutes + " min") + ", jump start penalty " + r.JumpStartPenalty);
            }
            EnsureAi(AcSession.Rules);
            if (Spectating)
            {
                if (LocalCar != null) { PlaceInPits(LocalCar, null); RepairCar(LocalCar, "spectating the race"); try { LocalCar.Physics.Freeze(true); } catch (Exception) { } }
                if (!_spectateTold) { _spectateTold = true; Message("Race in progress - you join the next session", 5f); AcLog.Info("Race: spectating this race (not on its grid)"); }
            }
            else if (LocalCar != null)
            {
                if (now < start || now - start < LateGridSeconds) { GridPlace(AcTiming.Local, slot); SeatDriver(); }
                else
                {
                    PlaceInPits(LocalCar, "late for the start");
                    if (newRace) RepairCar(LocalCar, "new race (late start)"); // design 1.6: every session starts clean; PlaceInPits itself never repairs
                }
            }
            if (now >= start)
            {
                AcSession.SetPhase(AcPhase.Racing);
                if (!_raceStarted) Green(start, true);
            }
            else if (now >= lightsAt) { AcSession.SetPhase(AcPhase.Grid); EnterCountdown(); }
            else AcSession.SetPhase(AcPhase.Grid);
        }

        private static void GridPlace(AcCarTiming c, int slot)
        {
            if (c == null || c.Rig == null || slot < 0 || slot >= AcTrackWorld.StartGrid.Length) return;
            AcSpawner.PlaceAt(c.Rig, AcTrackWorld.StartGrid[slot]);
            RepairCar(c.Rig, "grid");                          // iteration 2 (DAMAGE_DESIGN 1.6): the race starts clean
            c.Retired = false;
            try { if (c.Rig.Physics != null) c.Rig.Physics.Freeze(true); } catch (Exception e) { AcLog.Warn("Race: Freeze: " + e.Message); }
            c.GridPosition = AcSpawner.CarPosition(c.Rig);
            c.OnGrid = true;
            c.GridSlot = slot;
            if (c.Begun) AcTiming.Teleported(c, false);
        }

        private static void SeatDriver()
        {
            AcCarRig rig = LocalCar;
            if (rig == null) return;
            try
            {
                if (AcSeat.Seated && AcSeat.SeatedCar == rig && AcSeat.IsDriver) return;
                if (AcSeat.Seated) AcSeat.ForceExit();
                bool ok = AcSeat.Enter(rig, AcSeatKind.Driver);
                if (!_seatLogged) { _seatLogged = true; AcLog.Info("Race: seating the driver on the grid " + (ok ? "ok" : "refused (retrying each second)")); }
            }
            catch (Exception e) { AcLog.Error("Race: seating on the grid failed: " + e); }
        }

        private static void RaceTick()
        {
            double now = AcSession.UtcNow, start = AcSession.StartUtc;
            AcPhase phase = AcSession.Phase;
            if (phase == AcPhase.Grid || phase == AcPhase.Countdown)
            {
                // my car may have been built after the grid command; AI cars appear one by one
                if (!Spectating && LocalCar != null && !AcTiming.Local.OnGrid) { GridPlace(AcTiming.Local, AcTiming.Local.GridSlot); }
                if (!Spectating && LocalCar != null && Time.unscaledTime >= _nextSeatTry) { _nextSeatTry = Time.unscaledTime + 1f; SeatDriver(); } // ours: 1 s retry
                AiGridTick();
                if (phase == AcPhase.Grid && now >= AcRaceMath.LightsStartUtc(start)) { EnterCountdown(); phase = AcPhase.Countdown; }
                if (phase == AcPhase.Countdown)
                {
                    bool green;
                    int reds = AcRaceMath.RedsAt(now, start, out green);
                    if (reds != LightsReds && !green)
                    {
                        LightsReds = reds;
                        SetLights(reds, false, true);
                        if (reds > 0) AcGame.Guard("start beep", StartBeepCall);
                    }
                    JumpWatch(AcTiming.Local);
                    if (green) Green(start, false);
                }
                return;
            }

            // racing / finished
            if (_greenHideAt > 0f && Time.unscaledTime >= _greenHideAt) { _greenHideAt = -1f; LightsGreen = false; SetLights(0, false, false); }
            AcCarTiming me = AcTiming.Local;
            if (!Spectating && LocalCar != null && !me.Retired)   // a retired car (iteration 2) is no longer timed: DNF
            {
                if (!me.Begun) AcTiming.Begin(me, AcTimerMode.Race, AcSpawner.CarClock(LocalCar));
                me.Timer.TargetLaps = AcSession.Rules.Laps;
                int bits = AcTiming.Step(me);
                if ((bits & AcRaceTimer.FinishedNow) != 0) LocalFinished();
            }
            AiTimingTick(AcTimerMode.Race);

            // the chequered flag: a lap race ends for everyone once the leader has done LAPS (each car finishes at its next crossing).
            // A timed race (LAPS 0): when the time is up only the LEADER finishes at its next crossing, everybody else at their first
            // crossing after that (research 3.1) - a lapped car crossing before the leader does not finish a lap early. RACE_EXTRA_LAP
            // (acServer key, no value in the install) is 0: no extra lap.
            bool timeUp = AcSession.Rules.Laps <= 0 && AcSession.Rules.TimeMinutes > 0 && now >= start + AcSession.Rules.TimeMinutes * 60.0;
            bool anyFinished = AnyFinished();
            if (anyFinished)
            {
                me.Timer.FinishOnNextLap = true;
                for (int i = 0; i < AcTiming.Ai.Count; i++) AcTiming.Ai[i].Timer.FinishOnNextLap = true;
            }
            else if (timeUp)
            {
                int leader = LeaderId();
                if (leader == AcLocal.PhotonId && !Spectating) me.Timer.FinishOnNextLap = true;
                else if (leader <= -100) { AcCarTiming lc = AcTiming.AiTiming(-100 - leader, false); if (lc != null) lc.Timer.FinishOnNextLap = true; }
            }
            if (anyFinished && phase == AcPhase.Racing)
            {
                _firstFinishUtc = now;
                AcLog.Info("Race: chequered flag - the leader finished (" + LeaderFinishedName() + ")" + (timeUp ? " after the time was up" : ""));
                AcSession.SetPhase(AcPhase.Finished);
            }
        }

        /// <summary>The race leader's id from the standings (10 Hz, race-distance order); int.MinValue when unknown.</summary>
        private static int LeaderId()
        {
            for (int i = 0; i < StandingList.Count; i++) if (!StandingList[i].Spectating) return StandingList[i].PlayerId;
            return int.MinValue;
        }

        private static void EnterCountdown()
        {
            AcRules r = AcSession.Rules;
            AcSession.SetPhase(AcPhase.Countdown);
            AcTiming.Local.GridPosition = LocalCar != null ? AcSpawner.CarPosition(LocalCar) : Vector3.zero;
            for (int i = 0; i < AcTiming.Ai.Count; i++) if (AcTiming.Ai[i].Rig != null) AcTiming.Ai[i].GridPosition = AcSpawner.CarPosition(AcTiming.Ai[i].Rig);
            // JUMP_START_PENALTY 0 = none: the car stays locked until the green (server START_RULE 0); otherwise it is free and watched
            if (r.JumpStartPenalty != 0 && LocalCar != null && !Spectating) { try { LocalCar.Physics.Freeze(false); } catch (Exception) { } }
            LightsReds = 0;
            SetLights(0, false, true);
            AcLog.Info("Race: start lights: " + AcRaceMath.RedLights + " reds " + AcRaceMath.RedStepSeconds.ToString("0.0") + " s apart, hold " + AcRaceMath.HoldMs(AcSession.StartUtc)
                       + " ms, then lights out (cars " + (r.JumpStartPenalty != 0 ? "free, jump starts watched" : "locked") + ")");
        }

        private static void JumpWatch(AcCarTiming c)
        {
            if (c == null || c.Rig == null || c.JumpStart || Spectating) return;
            AcCarPhysics ph = c.Rig.Physics;
            if (ph == null || ph.Frozen) return;
            Vector3 d = AcSpawner.CarPosition(c.Rig) - c.GridPosition; d.y = 0f;
            if (d.magnitude > AcRaceMath.JumpStartMetres)
            {
                c.JumpStart = true;
                AcLog.Info("Race: jump start (moved " + d.magnitude.ToString("0.00") + " m before the green)");
                Message("FALSE START!", 4f);   // acs.exe's own text (research 3.1)
                // lead (W -> R): a drag / A2B tree lights its DISQUALIFIED lamps (no-op on circuit gantries); cleared at the next race
                try { if (AcTrackWorld.StartLights != null) AcTrackWorld.StartLights.SetJumpStart(true); } catch (Exception) { }
            }
        }

        /// <summary>Lights out: release the cars, start every timer at the green, apply jump-start penalties, NotifyRaceStarted.</summary>
        private static void Green(double start, bool late)
        {
            if (_raceStarted) return;
            _raceStarted = true;
            AcRules r = AcSession.Rules;
            LightsReds = 0; LightsGreen = true;
            SetLights(0, true, true);
            AcGame.Guard("go beep", GoBeepCall);
            _greenHideAt = Time.unscaledTime + GreenShowSeconds;
            FreezeAll(false);
            if (Spectating && LocalCar != null) { try { LocalCar.Physics.Freeze(true); } catch (Exception) { } }
            int jumps = 0;
            if (!Spectating && LocalCar != null)
            {
                AcTiming.Begin(AcTiming.Local, AcTimerMode.Race, ClockAt(start, LocalCar));
                AcTiming.Local.Timer.TargetLaps = r.Laps;
                if (AcTiming.Local.JumpStart)
                {
                    jumps++;
                    if (r.JumpStartPenalty == 1) { PlaceInPits(LocalCar, "jump start: teleported to the pits (JUMP_START_PENALTY 1)"); Message("Penalty: start from the pits", 5f); }
                    else if (r.JumpStartPenalty == 2) { AcTiming.Local.Timer.PenaltyMs += AcRaceMath.JumpStartPenaltyMs; AcTiming.WriteLocal(); Message("Penalty: +" + AcRaceMath.JumpStartPenaltyMs / 1000 + " s (false start)", 5f); }
                }
            }
            for (int i = 0; i < AcTiming.Ai.Count; i++)
            {
                AcCarTiming c = AcTiming.Ai[i];
                if (c.Rig == null || c.Benched) continue;
                AcTiming.Begin(c, AcTimerMode.Race, ClockAt(start, c.Rig));
                c.Timer.TargetLaps = r.Laps;
                if (late) SeedAiTimer(c, -100 - c.Id);
            }
            AcSession.SetPhase(AcPhase.Racing);
            AcSession.NotifyRaceStarted();
            AcLog.Info("Race: lights out" + (late ? " (joined after the green)" : "") + ", jump starts " + jumps);
        }

        private static void LocalFinished()
        {
            AcRaceTimer t = AcTiming.Local.Timer;
            UpdateStandings();
            int total = t.FinishedMs + t.PenaltyMs;
            AcLog.Info("Race: finished P" + AcLocal.Position + " " + AcRaceMath.FormatLap(total) + " (" + t.Laps + " laps, best " + AcRaceMath.FormatLap(t.BestMs)
                       + (t.PenaltyMs > 0 ? ", penalty " + t.PenaltyMs / 1000 + " s" : "") + ")");
            Message("FINISHED  P" + AcLocal.Position, 6f);
        }

        /// <summary>No lap is shorter than this: a remote "finished" flag earlier after the green is stale (s). // ours</summary>
        public const double StaleFinishSeconds = 15.0; // ours

        /// <summary>A timer's finish flags of a past race cleared (a new race is entered / the car is benched); the rest stays until Begin.</summary>
        private static void ClearFinish(AcRaceTimer t)
        {
            if (t == null) return;
            t.Finished = false;
            t.FinishOnNextLap = false;
        }

        private static bool AnyFinished()
        {
            double now = AcSession.UtcNow;
            // only timers begun for THIS race count (a stale Finished from the last race - a car benched or not built at this green - must not
            // throw the chequered flag on the first Racing frame)
            if (AcTiming.Local.Timer.Finished && !Spectating && AcTiming.Local.Begun && AcTiming.Local.Timer.Mode == AcTimerMode.Race) return true;
            for (int i = 0; i < AcTiming.Ai.Count; i++)
            {
                AcCarTiming c = AcTiming.Ai[i];
                if (c.Begun && !c.Benched && c.Timer.Mode == AcTimerMode.Race && c.Timer.Finished) return true;
            }
            try
            {
                if (RecordFinished(AcNetGame.Players, now)) return true;
                if (!AcNetGame.IsMaster && RecordFinished(AcNetGame.AiPlayers, now)) return true;   // the master's AI cars, as received here
            }
            catch (Exception) { }
            return false;
        }

        private static bool RecordFinished(System.Collections.Generic.IReadOnlyList<AcNetPlayer> list, double now)
        {
            for (int i = 0; i < list.Count; i++)
            {
                AcNetPlayer p = list[i];
                // a record may still carry the previous race's finish for a moment after the green: count it only when it can be real
                if (p != null && p.Finished && p.OnTrack && _cmd != null && _cmd.Grid.Contains(p.PhotonId) && now - AcSession.StartUtc > StaleFinishSeconds
                    && (AcSession.Rules.Laps <= 0 || p.Laps >= AcSession.Rules.Laps || AcTiming.OpenCourse)) return true;
            }
            return false;
        }

        private static string LeaderFinishedName()
        {
            for (int i = 0; i < StandingList.Count; i++) if (StandingList[i].Finished) return StandingList[i].Name;
            return AcTiming.Local.Timer.Finished ? "you" : "?";
        }

        // ------------------------------------------------------------------ results

        private static void EnterResults()
        {
            bool race = _cmd.Grid.Count > 0;
            string key = "S|" + _cmd.StartUtc.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + race;
            bool fresh = key != _sessionKey;
            if (fresh) { _resultsNotified = false; _resultsFrozen = false; _netResultsApplied = false; }
            _sessionKey = key;
            LightsOff();
            FreezeAll(false);
            AcSession.SetPhase(AcPhase.Results);
            if (_resultsNotified) return;                       // the same results again (re-applied command): already final
            _resultsFrozen = false;
            UpdateStandings();
            _resultsFrozen = true;                              // the table stands still from here (the cars may drive a cool-down lap)
            bool master;
            try { master = AcNetGame.IsMaster || !AcNetGame.Up; } catch (Exception) { master = true; }
            if (master)
            {
                // everyone shows the master's order (a finisher's record can reach another client ~100 ms late, AI rows exist only here)
                try { AcNetGame.PublishResults(StandingList); }
                catch (Exception e) { AcLog.Warn("Race: publishing the results failed: " + e.Message); }
                FinalizeResults(race, "mine, published");
            }
            else
            {
                _resultsWaitUntil = Time.unscaledTime + ResultsWaitSeconds;
                ResultsTick();
            }
        }

        private static void ResultsTick()
        {
            if (_cmd == null || _netResultsApplied) return;
            bool race = _cmd.Grid.Count > 0;
            bool have = false;
            try
            {
                if (!AcNetGame.IsMaster && AcNetGame.ResultsSeq == _cmd.Seq && AcNetGame.Results.Count > 0)
                {
                    AcStandingsBoard.ApplyResults(StandingList, AcNetGame.Results, _cmd);
                    have = true;
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Race: reading the master's results failed: " + e.Message); }
            if (have)
            {
                _netResultsApplied = true;
                int pos = 0;
                for (int i = 0; i < StandingList.Count; i++) if (StandingList[i].IsLocal) { pos = StandingList[i].Spectating ? 0 : StandingList[i].Position; break; }
                AcLocal.Position = pos;
                if (!_resultsNotified) FinalizeResults(race, "the master's order");
                else AcLog.Info("Race: the master's results arrived late - table re-ordered: " + ResultsText(race));
                return;
            }
            if (!_resultsNotified && Time.unscaledTime >= _resultsWaitUntil) FinalizeResults(race, "mine - the master published none in " + ResultsWaitSeconds + " s");
        }

        private static void FinalizeResults(bool race, string source)
        {
            _resultsNotified = true;
            if (!race && (AcSession.Rules == null || AcSession.Rules.Session != AcSessionType.Practice)) AcRaceDirector.RememberQualify(   // (a timed practice's order is not a qualify result)
                AcSession.Layout != null ? AcSession.Layout.Key : "", StandingList);
            AcLog.Info("Race: results (" + (race ? "race" : AcSession.Rules.Session.ToString().ToLowerInvariant()) + ", " + source + "): " + ResultsText(race));
            AcSession.NotifyRaceFinished();
            Message(race ? "RACE RESULTS" : "SESSION RESULTS", 4f);
        }

        private static string ResultsText(bool race)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < StandingList.Count && i < 24; i++)
            {
                AcStanding s = StandingList[i];
                if (s.Spectating) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append('P').Append(s.Position).Append(' ').Append(s.Name).Append(' ');
                if (race) sb.Append(s.Finished ? AcRaceMath.FormatLap(s.TotalMs) : "DNF " + s.Laps + " laps" + (s.Retired ? " (retired)" : "")).Append(s.PenaltyMs > 0 ? " (+" + s.PenaltyMs / 1000 + "s)" : "");
                else sb.Append(AcRaceMath.FormatLap(s.BestMs));
            }
            return sb.Length > 0 ? sb.ToString() : "nobody";
        }

        // ------------------------------------------------------------------ shared bits

        /// <summary>Start lights: W's track meshes (lamp count scaled from the 5 steps) + H's panel.</summary>
        private static void SetLights(int reds, bool green, bool visible)
        {
            try { AcTrackWorld.StartLights.SetLights(AcRaceMath.LampsFor(reds, AcTrackWorld.StartLights.Count), green); }
            catch (Exception e) { AcLog.WarnOnce("Race: track start lights failed: " + e.Message); }
            try { AcHud.StartLights(reds, green, visible); }
            catch (Exception e) { AcLog.WarnOnce("Race: HUD start lights failed: " + e.Message); }
        }

        private static void LightsOff()
        {
            if (LightsReds != 0 || LightsGreen || _greenHideAt > 0f) SetLights(0, false, false);
            LightsReds = 0; LightsGreen = false; _greenHideAt = -1f;
        }

        private static void FreezeAll(bool frozen)
        {
            try { if (LocalCar != null && LocalCar.Physics != null) LocalCar.Physics.Freeze(frozen); } catch (Exception e) { AcLog.WarnOnce("Race: Freeze failed: " + e.Message); }
            for (int i = 0; i < AcTiming.Ai.Count; i++)
            {
                AcCarTiming c = AcTiming.Ai[i];
                if (!frozen && c.Benched && AcSession.InRace) continue;   // no grid slot: stays held until practice
                AcCarRig r = c.Rig;
                try { if (r != null && r.Physics != null) r.Physics.Freeze(frozen); } catch (Exception) { }
            }
        }

        /// <summary>Session rules a car's physics plays: FUEL_RATE / TYRE_WEAR_RATE (% -> P's 1 = 100 %). Cheap (two field writes).</summary>
        private static void ApplyRates(AcCarRig rig)
        {
            AcRules r = AcSession.Rules;
            if (rig == null || r == null) return;
            try
            {
                AcCarPhysics ph = rig.Physics;
                if (ph == null) return;
                ph.FuelRate = Mathf.Max(0, r.FuelRate) / 100f;
                ph.TyreWearRate = Mathf.Max(0, r.TyreWearRate) / 100f;
                ApplyDamageRate(ph, r);                       // iteration 2: DAMAGE_MULTIPLIER (AcRace.Damage.cs)
            }
            catch (Exception e) { AcLog.WarnOnce("Race: setting FUEL_RATE / TYRE_WEAR_RATE / DAMAGE_MULTIPLIER failed: " + e.Message); }
        }

        private static float _limiterLogged = -1f;

        /// <summary>Reset to the track (R key: AcCarInput.ResetRequest; VR tablet: <see cref="ResetCar"/>), the pit limiter (P's
        /// SpeedLimitKmh while in the pit lane: gas cut + the PitLimiter light) and the session's fuel / tyre-wear rates, once a frame on a track.</summary>
        private static void LocalInputTick()
        {
            AcCarRig rig = LocalCar;
            if (rig == null || rig.Physics == null) return;
            AcCarPhysics ph = rig.Physics;
            if (ph.Input.ResetRequest)
            {
                ph.Input.ResetRequest = false;              // R consumes this edge (AcCarInput doc: "the in-place reset")
                ResetCar();
            }
            // pit limiter: AC limits the car in the pit lane (no limit key exists in the install -> Rules.PitLimitKmh 80, ours)
            AcRules r = AcSession.Rules;
            float limit = AcLocal.InPitlane && r != null && r.PitLimitKmh > 0f ? r.PitLimitKmh : 0f;
            try
            {
                if (ph.SpeedLimitKmh != limit)
                {
                    ph.SpeedLimitKmh = limit;
                    if (limit > 0f && _limiterLogged != limit) { _limiterLogged = limit; AcLog.Info("Race: pit limiter on (" + limit.ToString("0") + " km/h, ours - AC has no limit key)"); }
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Race: pit limiter (AcCarPhysics.SpeedLimitKmh) failed: " + e.Message); }
            ApplyRates(rig);
        }

        private static void ResetToTrack(AcCarRig rig)
        {
            Vector3 p, ground;
            float heading;
            if (!AcSpawner.NearestOnLine(AcSpawner.CarPosition(rig), out p, out heading))
            {
                if (!AcSession.InRace) PlaceInPits(rig, "reset (no racing line) -> pits");
                return;
            }
            bool hit;
            ground = AcSpawner.GroundUnder(p + Vector3.up * 0.5f, out hit);
            AcSpawner.Place(rig, ground, heading);
            AcTiming.Teleported(AcTiming.Local, false);
            AcLog.InfoOnce("Race: reset to the track (first time; the lap is invalidated)");
        }

        // ------------------------------------------------------------------ AI (module I; the master runs them)

        /// <summary>Master: the AI field of <paramref name="rules"/> (module I). Called on practice / race entry, before a START builds its grid,
        /// and once a second on a track (a client that just became master starts driving - and adopting - the AI cars).</summary>
        private static void EnsureAi(AcRules rules)
        {
            bool master;
            try { master = AcNetGame.IsMaster; } catch (Exception) { master = true; }
            if (!master || rules == null) return;
            int want = Math.Max(0, rules.AiCount);
            int have;
            bool available;
            try { have = AcAi.Drivers.Count; available = AcAi.Available; } catch (Exception) { return; }
            if (want == have) return;
            if (want > 0 && !available) { AcLog.InfoOnce("Race: " + want + " AI cars requested but the AI module is not available"); return; }
            AcGame.Guard("ai clear", AcAi.Clear);
            AcTiming.Ai.Clear();
            if (want > 0) SpawnAi(want, rules);
            AcLog.Info("Race: AI field " + want + " car(s) (level " + rules.AiLevel + ")");
        }

        // separate method: the lambda's closure is allocated at method entry
        private static void SpawnAi(int want, AcRules rules) { AcGame.Guard("ai spawn", () => AcAi.Spawn(want, rules.Clone())); }

        private static void AiTimingTick(AcTimerMode mode)
        {
            System.Collections.Generic.IReadOnlyList<AcAiDriver> drivers;
            try { drivers = AcAi.Drivers; } catch (Exception) { return; }
            bool race = mode == AcTimerMode.Race;
            double cutoffUtc = !race && TimedSessionActive ? AcSession.StartUtc + AcSession.Rules.TimeMinutes * 60.0 : 0.0;
            for (int i = 0; i < drivers.Count; i++)
            {
                AcAiDriver d = drivers[i];
                if (d == null || d.State == AcAiState.Failed) continue;   // lead (I -> R): a driver whose car could not be built gets no timing row
                AcCarTiming c = AcTiming.AiTiming(d.Index, true);
                c.Rig = d.Rig; c.Name = d.Name;
                if (c.Rig == null || (race && c.Benched)) continue;
                ApplyRates(c.Rig);
                if (race && c.Retired) continue;              // iteration 2: a retired AI car (engine blown) is DNF, no longer timed
                if (!c.Begun)
                {
                    // a race timer always runs from the green (an AI car built / adopted after it); a new master carries the laps over
                    bool afterGreen = race && AcSession.StartUtc > 0 && AcSession.UtcNow > AcSession.StartUtc;
                    AcTiming.Begin(c, mode, afterGreen ? ClockAt(AcSession.StartUtc, c.Rig) : AcSpawner.CarClock(c.Rig));
                    if (afterGreen) SeedAiTimer(c, d.Index);
                }
                if (race) c.Timer.TargetLaps = AcSession.Rules.Laps;
                else c.Timer.CutoffTime = cutoffUtc > 0 ? ClockAt(cutoffUtc, c.Rig) : double.PositiveInfinity;
                AcTiming.Step(c);
            }
        }

        private static void AiGridTick()
        {
            if (_cmd == null) return;
            System.Collections.Generic.IReadOnlyList<AcAiDriver> drivers;
            try { drivers = AcAi.Drivers; } catch (Exception) { return; }
            for (int i = 0; i < drivers.Count; i++)
            {
                AcAiDriver d = drivers[i];
                if (d == null || d.Rig == null) continue;
                AcCarTiming c = AcTiming.AiTiming(d.Index, true);
                c.Rig = d.Rig; c.Name = d.Name;
                ApplyRates(c.Rig);
                if (c.OnGrid) continue;
                int slot = _cmd.Grid.IndexOf(c.Id);
                if (slot >= 0 && slot < AcTrackWorld.StartGrid.Length) { c.Benched = false; GridPlace(c, slot); }
                else
                {
                    // not on this grid (more cars than AC_START_n slots): held where it stands, not timed, out of the standings until practice
                    c.OnGrid = true; c.Benched = true;
                    ClearFinish(c.Timer);
                    try { if (c.Rig.Physics != null) c.Rig.Physics.Freeze(true); } catch (Exception) { }
                    AcLog.Info("Race: AI " + c.Name + " has no grid slot (" + AcTrackWorld.StartGrid.Length + " slots) - sits this race out");
                }
            }
        }

        // ------------------------------------------------------------------ names

        /// <summary>Display name of a grid / lap id: me, a Dreamport player, an AI driver.</summary>
        public static string NameOf(int id)
        {
            if (id == int.MinValue) return "-";
            if (id == AcLocal.PhotonId) return string.IsNullOrEmpty(AcLocal.Nick) ? "You" : AcLocal.Nick;
            if (id <= -100)
            {
                AcCarTiming c = AcTiming.AiTiming(-100 - id, false);
                if (c != null && !string.IsNullOrEmpty(c.Name)) return c.Name;
                try { var d = AcAi.Drivers; for (int i = 0; i < d.Count; i++) if (d[i] != null && d[i].Index == -100 - id && !string.IsNullOrEmpty(d[i].Name)) return d[i].Name; } catch (Exception) { }
                try { var a = AcNetGame.AiPlayers; for (int i = 0; i < a.Count; i++) if (a[i] != null && a[i].PhotonId == id && !string.IsNullOrEmpty(a[i].Name)) return a[i].Name; } catch (Exception) { }
                return "AI " + (-100 - id + 1);
            }
            try { AcNetPlayer p = AcNetGame.FindByPhotonId(id); if (p != null) return p.Name; } catch (Exception) { }
            return "player " + id;
        }

        private static string UtcText(double utc)
        {
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(utc).ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return utc.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }
        }
    }
}
