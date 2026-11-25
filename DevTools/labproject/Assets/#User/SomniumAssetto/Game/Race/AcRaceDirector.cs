using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] The instance master's decisions, made only from the newest command and the shared clock (so a new master - Halo's
    /// hand-over rule, N - simply carries on): close a race (everyone on the grid finished, or RACE_OVER_TIME after the winner, or a
    /// timed race past OVER_TIME_MULT), close a qualify (time up + QUALIFY_MAX_WAIT_PERC of the fastest lap), end the results screen
    /// (back to practice), and the grid order for START (last qualify of this layout, else best valid laps, else join order; AI after).
    /// </summary>
    public static class AcRaceDirector
    {
        private static int _actedOnSeq = int.MinValue;
        private static float _retryAt;
        private static string _qualifyLayout = "";
        private static readonly List<int> QualifyOrder = new List<int>();

        /// <summary>Once a frame on a track (every client calls it; only the master acts).</summary>
        public static void Tick(AcRaceCommand cmd, double appliedUtc)
        {
            if (cmd == null || cmd.Seq == _actedOnSeq || UnityEngine.Time.unscaledTime < _retryAt) return;
            bool master;
            try { master = AcNetGame.IsMaster && AcNetGame.MasterKnown; } catch (Exception) { master = true; }
            if (!master) return;
            AcRules r = AcSession.Rules ?? new AcRules();
            double now = AcSession.UtcNow;
            switch (AcSession.Phase)
            {
                case AcPhase.Racing:
                case AcPhase.Finished:
                {
                    if (!AnyRacerPresent(cmd)) { Act(cmd, Practice(cmd), "every racer left - back to practice"); return; }
                    double first = AcRace.FirstFinishUtc;
                    if (first > 0 && AllRacersFinished(cmd)) { Act(cmd, Results(cmd, now), "every racer finished"); return; }
                    // iteration 2: nobody can finish any more (every racer still present retired, or finished) - close without the flag
                    if (first <= 0 && AllRacersFinished(cmd)) { Act(cmd, Results(cmd, now), "every racer retired (engine blown)"); return; }
                    if (first > 0 && now >= first + Math.Max(0, r.RaceOverTimeSeconds)) { Act(cmd, Results(cmd, now), "RACE_OVER_TIME " + r.RaceOverTimeSeconds + " s after the winner"); return; }
                    if (first <= 0 && r.Laps <= 0 && r.TimeMinutes > 0 && cmd.StartUtc > 0
                        && now >= cmd.StartUtc + r.TimeMinutes * 60.0 * AcRace.Defaults.OverTimeMult + Math.Max(0, r.RaceOverTimeSeconds))
                    { Act(cmd, Results(cmd, now), "timed race past OVER_TIME_MULT " + AcRace.Defaults.OverTimeMult); return; }
                    break;
                }
                case AcPhase.Results:
                {
                    double at = cmd.PhaseUtc > 0 ? cmd.PhaseUtc : appliedUtc;
                    if (now >= at + AcRace.Defaults.ResultScreenSeconds)
                    {
                        AcRaceCommand p = Practice(cmd);
                        // after qualifying the race comes next (AC's Practice -> Qualify -> Race order); its grid uses the qualify result
                        if (p.Rules.Session == AcSessionType.Qualify) p.Rules.Session = AcSessionType.Race;
                        Act(cmd, p, "results shown " + AcRace.Defaults.ResultScreenSeconds + " s");
                    }
                    break;
                }
                case AcPhase.Practice:
                {
                    if (!AcRace.TimedSessionActive || r.TimeMinutes <= 0) break;   // qualify or a timed practice (START with TIME)
                    double end = cmd.StartUtc + r.TimeMinutes * 60.0;
                    if (now < end) break;
                    int fastest = FastestLapMs();
                    double wait = fastest > 0 ? fastest / 1000.0 * AcRace.Defaults.QualifyMaxWaitPerc / 100.0 : 0.0;
                    if (now >= end + wait) Act(cmd, Results(cmd, now), (AcRace.QualifyActive ? "qualify" : "practice") + " time up (+" + wait.ToString("0") + " s QUALIFY_MAX_WAIT_PERC)");
                    break;
                }
            }
        }

        private static void Act(AcRaceCommand cmd, AcRaceCommand next, string why)
        {
            AcLog.Info("Race: master: " + why + " -> " + next.Phase);
            if (AcRace.PublishFromDirector(next, "advance the session")) _actedOnSeq = cmd.Seq;
            else _retryAt = UnityEngine.Time.unscaledTime + 2f; // ours: retry a refused publish every 2 s
        }

        private static AcRaceCommand Results(AcRaceCommand cmd, double now)
        {
            AcRaceCommand c = cmd.Clone();
            c.Phase = AcPhase.Results;
            c.PhaseUtc = now;
            return c;
        }

        private static AcRaceCommand Practice(AcRaceCommand cmd)
        {
            AcRaceCommand c = cmd.Clone();
            c.Phase = AcPhase.Practice;
            c.Grid.Clear();
            c.StartUtc = 0;
            c.PhaseUtc = 0;
            return c;
        }

        private static bool AnyRacerPresent(AcRaceCommand cmd)
        {
            for (int i = 0; i < cmd.Grid.Count; i++) if (Present(cmd.Grid[i])) return true;
            return false;
        }

        private static bool AllRacersFinished(AcRaceCommand cmd)
        {
            for (int i = 0; i < cmd.Grid.Count; i++)
            {
                int id = cmd.Grid[i];
                if (!Present(id)) continue;
                if (!Finished(id)) return false;
            }
            return true;
        }

        private static bool Present(int id)
        {
            if (id == AcLocal.PhotonId) return AcRace.LocalCar != null;
            if (id <= -100) return AcTiming.AiTiming(-100 - id, false) != null;
            try { AcNetPlayer p = AcNetGame.FindByPhotonId(id); return p != null && p.OnTrack; } catch (Exception) { return false; }
        }

        private static bool Finished(int id)
        {
            // iteration 2: a retired racer (engine blown) is done too - it can never take the flag
            if (id == AcLocal.PhotonId) return AcTiming.Local.Timer.Finished || AcTiming.Local.Retired;
            if (id <= -100) { AcCarTiming c = AcTiming.AiTiming(-100 - id, false); return c == null || c.Timer.Finished || c.Retired; }
            try { AcNetPlayer p = AcNetGame.FindByPhotonId(id); return p == null || p.Finished || p.DamageD1 < 0; } catch (Exception) { return true; }
        }

        private static int FastestLapMs()
        {
            int best = 0;
            var s = AcRace.Standings;
            for (int i = 0; i < s.Count; i++) if (s[i].BestMs > 0 && (best == 0 || s[i].BestMs < best)) best = s[i].BestMs;
            return best;
        }

        // ------------------------------------------------------------------ grid order

        /// <summary>The result of the last qualify on <paramref name="layoutKey"/> (ids with a valid lap, position order) - the next race's grid.</summary>
        public static void RememberQualify(string layoutKey, IReadOnlyList<AcStanding> standings)
        {
            QualifyOrder.Clear();
            _qualifyLayout = layoutKey ?? "";
            for (int i = 0; i < standings.Count; i++) if (standings[i].BestMs > 0) QualifyOrder.Add(standings[i].PlayerId);
            AcLog.Info("Race: qualify order kept for the next race on " + _qualifyLayout + ": " + QualifyOrder.Count + " drivers with a time");
        }

        private struct Entry { public int Id, Best, Qual; public bool Ai; }

        /// <summary>START: the racers in grid order, pole first (me + players on the track + the AI field), capped at the layout's grid slots.</summary>
        public static void BuildGrid(List<int> grid, AcRules rules)
        {
            grid.Clear();
            var list = new List<Entry>();
            bool useQual = QualifyOrder.Count > 0 && AcSession.Layout != null && AcSession.Layout.Key == _qualifyLayout;
            if (AcRace.LocalCar != null) list.Add(Make(AcLocal.PhotonId, AcLocal.BestMs, false, useQual));
            try
            {
                var players = AcNetGame.Players;
                for (int i = 0; i < players.Count; i++)
                    if (players[i] != null && players[i].OnTrack) list.Add(Make(players[i].PhotonId, players[i].BestMs, false, useQual));
            }
            catch (Exception) { }
            int ai = rules != null ? Math.Max(0, rules.AiCount) : 0;
            try
            {
                var drivers = AcAi.Drivers;
                if (drivers.Count > 0) { for (int i = 0; i < drivers.Count && i < ai; i++) if (drivers[i] != null && drivers[i].State != AcAiState.Failed) list.Add(Make(-(100 + drivers[i].Index), 0, true, useQual)); }
                else if (ai > 0 && AcAi.Available) for (int i = 0; i < ai; i++) list.Add(Make(-(100 + i), 0, true, useQual));
            }
            catch (Exception) { }
            list.Sort(Compare);
            int cap = AcTrackWorld.StartGrid.Length;
            for (int i = 0; i < list.Count && grid.Count < cap; i++) grid.Add(list[i].Id);
            AcLog.Info("Race: grid order by " + (useQual ? "the qualify result" : "best laps, then join order") + ": " + grid.Count + " of " + list.Count + " cars (" + cap + " slots)");
        }

        private static Entry Make(int id, int best, bool ai, bool useQual)
        {
            int q = useQual ? QualifyOrder.IndexOf(id) : -1;
            return new Entry { Id = id, Best = best, Ai = ai, Qual = q < 0 ? int.MaxValue : q };
        }

        private static int Compare(Entry a, Entry b)
        {
            if (a.Qual != b.Qual) return a.Qual.CompareTo(b.Qual);
            if (a.Ai != b.Ai) return a.Ai ? 1 : -1;                        // ours: AI start behind the players
            bool ta = a.Best > 0, tb = b.Best > 0;
            if (ta != tb) return ta ? -1 : 1;
            if (ta && a.Best != b.Best) return a.Best.CompareTo(b.Best);
            return a.Ai ? b.Id.CompareTo(a.Id) : a.Id.CompareTo(b.Id);     // join order (Fusion ids grow); AI by index
        }
    }
}
