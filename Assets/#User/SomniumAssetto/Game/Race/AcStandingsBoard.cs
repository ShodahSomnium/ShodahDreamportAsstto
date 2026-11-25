using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] The standings (AcRace.Standings): me + the other Dreamport players on the track (their records, N) + AI cars (timed here on
    /// the master). Race (after the green): finished cars by laps then total time (race time + penalties), running cars by race distance
    /// (laps + fast-lane progress, research T3) - the master's and everybody's lists agree because they sort the same published numbers.
    /// Grid / countdown: grid order. Practice / qualify / hotlap: best valid lap (no time: after, by join order). Pooled entries, insertion
    /// sort: no allocation in steady state.
    /// </summary>
    public static class AcStandingsBoard
    {
        /// <summary>Gap estimate for a running car: race-distance difference x this lap time when the leader has none yet (ms). // ours</summary>
        public const int FallbackLapMs = 90000; // ours

        private static readonly List<AcStanding> Pool = new List<AcStanding>();
        private static int _used;
        // the last known timing of every gridded player of the current race (by PlayerId): a racer who leaves mid-race stays classified (DNF with
        // his laps, or finished if he had taken the flag) instead of vanishing from the standings and the published results (AC keeps them)
        private static readonly List<AcStanding> Gone = new List<AcStanding>();
        private static int _goneCount;
        private static double _goneRace = double.NaN;

        /// <summary>Rebuilds <paramref name="into"/> (cleared and refilled with pooled entries).</summary>
        public static void Build(List<AcStanding> into, AcRaceCommand cmd, bool raceMode, bool gridOrder)
        {
            into.Clear();
            _used = 0;
            // me
            if (AcTiming.Local.Rig != null && AcSession.OnTrack)
            {
                AcStanding s = Next();
                AcRaceTimer t = AcTiming.Local.Timer;
                s.PlayerId = AcLocal.PhotonId; s.IsLocal = true; s.IsAi = false;
                s.Name = string.IsNullOrEmpty(AcLocal.Nick) ? "You" : AcLocal.Nick;
                s.CarId = AcTiming.Local.Rig.Car != null ? AcTiming.Local.Rig.Car.Id : "";
                Fill(s, t.Laps, t.BestMs, t.LastMs, t.LapMs, t.Sector, t.LapValid, t.RaceDistance, t.Finished, t.FinishedMs, t.PenaltyMs, AcLocal.InPitlane);
                s.GridSlot = GridIndex(cmd, s.PlayerId);
                s.Spectating = raceMode && s.GridSlot < 0;
                s.Retired = raceMode && AcTiming.Local.Retired;
                into.Add(s);
            }
            // other players on this track
            try
            {
                var players = AcNetGame.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    AcNetPlayer p = players[i];
                    if (p == null || !p.OnTrack) continue;
                    AcStanding s = Next();
                    s.PlayerId = p.PhotonId; s.IsLocal = false; s.IsAi = false; s.Name = p.Name ?? ""; s.CarId = p.CarId ?? "";
                    AddRemote(s, p, cmd, raceMode);
                    into.Add(s);
                    if (raceMode && !gridOrder && s.GridSlot >= 0) Remember(s);
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Race: standings could not read the players: " + e.Message); }
            // the master's AI cars as this client receives them (N's AI blocks; empty on the master, which times them itself below)
            try
            {
                if (!AcNetGame.IsMaster)
                {
                    var ai = AcNetGame.AiPlayers;
                    for (int i = 0; i < ai.Count; i++)
                    {
                        AcNetPlayer p = ai[i];
                        if (p == null || !p.OnTrack) continue;
                        AcStanding s = Next();
                        s.PlayerId = p.PhotonId; s.IsLocal = false; s.IsAi = true; s.Name = p.Name ?? ""; s.CarId = p.CarId ?? "";
                        AddRemote(s, p, cmd, raceMode);
                        if (raceMode && s.GridSlot < 0) { _used--; continue; }   // benched AI (no grid slot): not in this race
                        into.Add(s);
                    }
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Race: standings could not read the AI cars: " + e.Message); }
            // AI timed here (master)
            for (int i = 0; i < AcTiming.Ai.Count; i++)
            {
                AcCarTiming c = AcTiming.Ai[i];
                if (c.Rig == null || !c.Begun || (raceMode && c.Benched)) continue;
                AcStanding s = Next();
                AcRaceTimer t = c.Timer;
                s.PlayerId = c.Id; s.IsLocal = false; s.IsAi = true; s.Name = c.Name ?? ""; s.CarId = c.Rig.Car != null ? c.Rig.Car.Id : "";
                Fill(s, t.Laps, t.BestMs, t.LastMs, t.LapMs, t.Sector, t.LapValid, t.RaceDistance, t.Finished, t.FinishedMs, t.PenaltyMs, false);
                s.GridSlot = GridIndex(cmd, s.PlayerId);
                s.Spectating = raceMode && s.GridSlot < 0;
                s.Retired = raceMode && c.Retired;
                into.Add(s);
            }

            if (raceMode && !gridOrder) AddDeparted(into, cmd);

            // order
            for (int i = 1; i < into.Count; i++)
            {
                AcStanding x = into[i];
                int j = i - 1;
                while (j >= 0 && Before(x, into[j], raceMode, gridOrder)) { into[j + 1] = into[j]; j--; }
                into[j + 1] = x;
            }

            // positions and gaps
            AcStanding lead = into.Count > 0 ? into[0] : null;
            for (int i = 0; i < into.Count; i++)
            {
                AcStanding s = into[i];
                s.Position = i + 1;
                s.GapMs = 0; s.LapsBehind = 0;
                if (lead == null || s == lead) continue;
                if (raceMode && !gridOrder)
                {
                    if (s.Finished && lead.Finished && s.Laps == lead.Laps) s.GapMs = Math.Max(0, s.TotalMs - lead.TotalMs);
                    else
                    {
                        float behind = lead.RaceDistance - s.RaceDistance;
                        // laps behind a finished leader: from the distance for a car still running (on the lead lap it has one lap fewer counted
                        // until it takes the flag - not "+1 LAP"); from the counted laps when both finished
                        if (s.Finished && lead.Finished) behind = lead.Laps - s.Laps;
                        else if (lead.Finished) behind = lead.Laps - s.RaceDistance;
                        else if (s.Finished) behind = lead.Laps - s.Laps;
                        if (behind >= 1f) s.LapsBehind = (int)Math.Floor(behind);
                        else
                        {
                            int lapMs = lead.LastMs > 0 ? lead.LastMs : lead.BestMs > 0 ? lead.BestMs : FallbackLapMs;
                            s.GapMs = (int)Math.Max(0f, behind * lapMs);
                        }
                    }
                }
                else if (!raceMode && s.BestMs > 0 && lead.BestMs > 0) s.GapMs = s.BestMs - lead.BestMs;
            }
        }

        // a record's FinishedMs is the TOTAL incl. penalties (N publishes FinishedMs + PenaltyMs); the row keeps race time and penalty apart
        private static void AddRemote(AcStanding s, AcNetPlayer p, AcRaceCommand cmd, bool raceMode)
        {
            int pen = Math.Max(0, p.PenaltyMs);
            int raceMs = p.Finished ? Math.Max(0, p.FinishedMs - pen) : 0;
            Fill(s, p.Laps, p.BestMs, p.LastMs, p.LapMs, p.Sector, p.LapValid, p.RaceDistance, p.Finished, raceMs, pen, p.InPitlane);
            s.GridSlot = GridIndex(cmd, s.PlayerId);
            s.Spectating = raceMode && s.GridSlot < 0;
            s.Retired = raceMode && p.DamageD1 < 0;                 // iteration 2: d1 bit 31 = retired (N decodes the damage pair raw)
        }

        /// <summary>
        /// Non-master, results: the master's published final order (N's World 2-3: id, position, finished, total, best) replaces this client's
        /// own ordering, so every client shows the same table even when a finisher's record arrived late here. Rows the master lists that this
        /// client lacks are added (name from <see cref="AcRace.NameOf"/>); rows the master does not list go after, keeping their order.
        /// Runs once per results screen (may allocate).
        /// </summary>
        public static void ApplyResults(List<AcStanding> into, IReadOnlyList<AcNetResult> results, AcRaceCommand cmd)
        {
            if (results == null || results.Count == 0) return;
            var ordered = new List<AcStanding>(Math.Max(into.Count, results.Count));
            var used = new bool[into.Count];
            for (int r = 0; r < results.Count; r++)
            {
                AcNetResult res = results[r];
                AcStanding s = null;
                for (int i = 0; i < into.Count; i++)
                    if (!used[i] && into[i].PlayerId == res.Id) { s = into[i]; used[i] = true; break; }
                if (s == null)
                {
                    s = Next();
                    s.PlayerId = res.Id; s.IsLocal = res.Id == AcLocal.PhotonId; s.IsAi = res.Id <= -100; s.Name = AcRace.NameOf(res.Id); s.CarId = "";
                    s.Laps = 0; s.LastMs = 0; s.LapMs = 0; s.Sector = 0; s.LapValid = true; s.RaceDistance = 0f; s.PenaltyMs = 0; s.InPit = false;
                }
                s.Finished = res.Finished;
                if (res.Finished && res.TotalMs > 0) s.TotalMs = res.TotalMs;
                if (res.BestMs > 0) s.BestMs = res.BestMs;
                // a row the master lists races unless it has no grid slot in the command (an older master may still publish spectators)
                s.Spectating = cmd != null && cmd.Grid != null && cmd.Grid.Count > 0 && GridIndex(cmd, res.Id) < 0;
                ordered.Add(s);
            }
            for (int i = 0; i < into.Count; i++) if (!used[i]) ordered.Add(into[i]);
            into.Clear();
            into.AddRange(ordered);
            AcStanding lead = into.Count > 0 ? into[0] : null;
            for (int i = 0; i < into.Count; i++)
            {
                AcStanding s = into[i];
                s.Position = i + 1;
                if (lead != null && s != lead && s.Finished && lead.Finished && s.Laps == lead.Laps && s.TotalMs > 0) { s.GapMs = Math.Max(0, s.TotalMs - lead.TotalMs); s.LapsBehind = 0; }
            }
        }

        /// <summary>Keeps a gridded player's row (race mode) as his last known timing.</summary>
        private static void Remember(AcStanding s)
        {
            AcStanding m = null;
            for (int i = 0; i < _goneCount; i++) if (Gone[i].PlayerId == s.PlayerId) { m = Gone[i]; break; }
            if (m == null)
            {
                if (_goneCount >= Gone.Count) Gone.Add(new AcStanding());
                m = Gone[_goneCount++];
            }

            m.PlayerId = s.PlayerId; m.IsLocal = false; m.IsAi = false; m.Name = s.Name; m.CarId = s.CarId;
            m.Laps = s.Laps; m.BestMs = s.BestMs; m.LastMs = s.LastMs; m.LapMs = 0; m.Sector = 0; m.LapValid = true; m.RaceDistance = s.RaceDistance;
            m.Finished = s.Finished; m.TotalMs = s.TotalMs; m.PenaltyMs = s.PenaltyMs; m.InPit = false; m.Retired = s.Retired;
        }

        /// <summary>Gridded players no longer listed (they left mid-race): their last known row, classified (DNF unless they had finished).</summary>
        private static void AddDeparted(List<AcStanding> into, AcRaceCommand cmd)
        {
            if (cmd == null || cmd.Grid == null) return;
            if (cmd.StartUtc != _goneRace) { _goneRace = cmd.StartUtc; _goneCount = 0; }    // a new race: forget the last one's
            for (int g = 0; g < cmd.Grid.Count; g++)
            {
                int id = cmd.Grid[g];
                if (id <= -100 || id == AcLocal.PhotonId) continue;                         // AI / me
                bool listed = false;
                for (int i = 0; i < into.Count && !listed; i++) listed = into[i].PlayerId == id && !into[i].IsAi;
                if (listed) continue;
                AcStanding m = null;
                for (int i = 0; i < _goneCount; i++) if (Gone[i].PlayerId == id) { m = Gone[i]; break; }
                if (m == null) continue;                                                     // never seen racing here: nothing to classify
                AcStanding s = Next();
                s.PlayerId = m.PlayerId; s.IsLocal = false; s.IsAi = false; s.Name = m.Name; s.CarId = m.CarId;
                Fill(s, m.Laps, m.BestMs, m.LastMs, 0, 0, true, m.RaceDistance, m.Finished, m.Finished ? m.TotalMs - m.PenaltyMs : 0, m.PenaltyMs, false);
                s.GridSlot = g;
                s.Retired = m.Retired;
                s.Spectating = false;
                into.Add(s);
            }
        }

        private static void Fill(AcStanding s, int laps, int best, int last, int lapMs, int sector, bool valid, float dist, bool finished, int finishedMs, int penaltyMs, bool inPit)
        {
            s.Laps = laps; s.BestMs = best; s.LastMs = last; s.LapMs = lapMs; s.Sector = sector; s.LapValid = valid; s.RaceDistance = dist;
            s.Finished = finished; s.PenaltyMs = penaltyMs; s.TotalMs = finished ? finishedMs + penaltyMs : 0; s.InPit = inPit;
        }

        /// <summary>a ranks before b.</summary>
        private static bool Before(AcStanding a, AcStanding b, bool raceMode, bool gridOrder)
        {
            if (raceMode)
            {
                if (a.Spectating != b.Spectating) return !a.Spectating;
                if (!gridOrder && a.Retired != b.Retired) return !a.Retired;   // iteration 2: retired (DNF) after every running / finished car
                if (gridOrder)
                {
                    int ga = a.GridSlot < 0 ? int.MaxValue : a.GridSlot, gb = b.GridSlot < 0 ? int.MaxValue : b.GridSlot;
                    if (ga != gb) return ga < gb;
                    return a.PlayerId < b.PlayerId;
                }
                if (a.Laps != b.Laps && (a.Finished || b.Finished)) return a.Laps > b.Laps;
                if (a.Finished != b.Finished) return a.Finished;
                if (a.Finished) return a.TotalMs != b.TotalMs ? a.TotalMs < b.TotalMs : a.PlayerId < b.PlayerId;
                if (a.RaceDistance != b.RaceDistance) return a.RaceDistance > b.RaceDistance;
                return a.GridSlot < b.GridSlot;
            }
            bool ta = a.BestMs > 0, tb = b.BestMs > 0;
            if (ta != tb) return ta;
            if (ta && a.BestMs != b.BestMs) return a.BestMs < b.BestMs;
            if (a.IsAi != b.IsAi) return !a.IsAi;
            return JoinKey(a) < JoinKey(b);
        }

        // join order: Fusion player ids grow with the join order; AI after everyone
        private static long JoinKey(AcStanding s) { return s.IsAi ? 1000000L - s.PlayerId : s.PlayerId; }

        private static int GridIndex(AcRaceCommand cmd, int id)
        {
            if (cmd == null || cmd.Grid == null) return -1;
            for (int i = 0; i < cmd.Grid.Count; i++) if (cmd.Grid[i] == id) return i;
            return -1;
        }

        private static AcStanding Next()
        {
            if (_used >= Pool.Count) Pool.Add(new AcStanding());
            AcStanding s = Pool[_used++];
            s.Position = 0; s.GapMs = 0; s.LapsBehind = 0; s.Spectating = false; s.GridSlot = -1; s.Retired = false;
            return s;
        }
    }
}
