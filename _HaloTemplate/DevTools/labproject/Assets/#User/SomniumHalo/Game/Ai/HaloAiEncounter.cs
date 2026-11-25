using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] One scenario encounter at run time: its actors, the squads' delay timers and placement counts, the platoons'
    /// attacking / defending state and their change / maneuver rules, the firing positions (with who holds which), the follow target,
    /// script switches (blind, deaf, braindead, respawn, force active) and the team (encounter team, else each unit's own).
    /// </summary>
    public sealed class HaloAiEncounter
    {
        public sealed class SquadState
        {
            public int Placed, Killed;
            public bool TimerStarted, TimerExpired;
            public float TimerEnds = -1f;
            public bool AutomaticMigrationTarget = true;
            public int RespawnsLeft;
            public float NextRespawn = -1f;
        }

        public sealed class PlatoonState
        {
            public bool Defending;
            public bool Changed, Maneuvered;
            public bool ManeuverEnabled = true;
        }

        public readonly HaloAiScenario.Encounter Data;
        public readonly int Index;
        public readonly SquadState[] Squads;
        public readonly PlatoonState[] Platoons;
        public readonly List<HaloActor> Actors = new List<HaloActor>(16);
        /// <summary>Firing positions in Unity space (feet) and how many actors hold / head for each.</summary>
        public readonly Vector3[] Positions;
        public readonly int[] Claims;
        public HaloTeam Team;
        public bool Blind, Deaf, Braindead, Respawn, ForceActive, Playfight, ManeuverEnabled = true, AllowCharge = true, AllowDormant = true;
        // following
        public bool FollowPlayers; public HaloUnit FollowUnit; public string FollowAi; public float FollowDistance = 2.5f;   // world units (ours: ai_follow_distance default)
        public float LastGrenade = -100f;
        public int TotalPlaced, TotalKilled;
        /// <summary>The encounter has been placed at least once (scripts' ai_place or "not initially created" unset at mission start).</summary>
        public bool Created;
        public HaloAiEncounter LinkedTo;

        public HaloAiEncounter(HaloAiScenario.Encounter data)
        {
            Data = data; Index = data.Index;
            Squads = new SquadState[data.Squads.Count];
            for (int i = 0; i < Squads.Length; i++) Squads[i] = new SquadState { RespawnsLeft = data.Squads[i].RespawnTotal };
            Platoons = new PlatoonState[data.Platoons.Count];
            for (int i = 0; i < Platoons.Length; i++) Platoons[i] = new PlatoonState { Defending = (data.Platoons[i].Flags & HaloAiScenario.PlStartInDefendingState) != 0 };
            Positions = new Vector3[data.FiringPositions.Count];
            for (int i = 0; i < Positions.Length; i++) Positions[i] = HaloSpace.Pos(data.FiringPositions[i].Position);
            Claims = new int[Positions.Length];
            Team = data.Team > 0 && data.Team <= 5 ? (HaloTeam)data.Team : HaloTeam.None;
            Blind = data.Has(HaloAiScenario.EncInitiallyBlind);
            Deaf = data.Has(HaloAiScenario.EncInitiallyDeaf);
            Braindead = data.Has(HaloAiScenario.EncInitiallyBraindead);
            Respawn = data.Has(HaloAiScenario.EncRespawnEnabled);
        }

        public string Name => Data.Name;

        /// <summary>Does an actor belong to the selection (squad / platoon -1 = any)?</summary>
        public static bool Matches(HaloActor a, int squad, int platoon)
        {
            if (squad >= 0 && a.Squad != squad) return false;
            if (platoon >= 0 && a.Platoon != platoon) return false;
            return true;
        }

        public int Living(int squad, int platoon)
        {
            int n = 0;
            for (int i = 0; i < Actors.Count; i++) { var a = Actors[i]; if (a.Alive && Matches(a, squad, platoon)) n++; }
            return n;
        }

        public bool IsPlatoonDefending(int platoon) { return platoon >= 0 && platoon < Platoons.Length && Platoons[platoon].Defending; }

        /// <summary>The platoon's strength and dead fraction (members placed so far).</summary>
        public void PlatoonStats(int platoon, out float strength, out int dead, out int total)
        {
            float vit = 0f; dead = 0; total = 0;
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (a.Platoon != platoon) continue;
                total++;
                if (!a.Alive) { dead++; continue; }
                vit += a.Strength;
            }
            strength = total > 0 ? vit / total : 1f;
        }

        /// <summary>ScenarioChangeAttackingDefendingStateWhen: 0 never, 1 75% strength, 2 50% strength, 3 25% strength, 4 anybody dead,
        /// 5 25% dead, 6 50% dead, 7 75% dead, 8 all but one dead, 9 all dead.</summary>
        public bool Condition(int when, int platoon)
        {
            if (when <= 0 || platoon < 0 || platoon >= Platoons.Length) return false;
            float s; int dead, total;
            PlatoonStats(platoon, out s, out dead, out total);
            if (total == 0) return false;
            float df = (float)dead / total;
            switch (when)
            {
                case 1: return s <= 0.75f;
                case 2: return s <= 0.5f;
                case 3: return s <= 0.25f;
                case 4: return dead > 0;
                case 5: return df >= 0.25f;
                case 6: return df >= 0.5f;
                case 7: return df >= 0.75f;
                case 8: return total - dead <= 1;
                case 9: return dead >= total;
            }
            return false;
        }

        /// <summary>Platoon rules, squad timers, respawns (a few times a second).</summary>
        public void Think(float now)
        {
            for (int p = 0; p < Platoons.Length; p++)
            {
                var pd = Data.Platoons[p];
                var ps = Platoons[p];
                if (!ps.Changed && pd.ChangeWhen > 0 && Condition(pd.ChangeWhen, pd.ChangeHappensTo >= 0 ? pd.ChangeHappensTo : p))
                {
                    ps.Changed = true;
                    ps.Defending = !ps.Defending;
                    if (HaloAi.Verbose) HaloLog.Info("AI: " + Name + "/" + pd.Name + " now " + (ps.Defending ? "defending" : "attacking"));
                }
                if (!ps.Maneuvered && pd.ManeuverWhen > 0 && ManeuverEnabled && ps.ManeuverEnabled && Condition(pd.ManeuverWhen, pd.ManeuverHappensTo >= 0 ? pd.ManeuverHappensTo : p))
                {
                    ps.Maneuvered = true;
                    Maneuver(p, (pd.Flags & HaloAiScenario.PlFleeWhenManeuvering) != 0);
                }
            }

            for (int s = 0; s < Squads.Length; s++)
            {
                var ss = Squads[s];
                if (ss.TimerStarted && !ss.TimerExpired && ss.TimerEnds >= 0f && now >= ss.TimerEnds) ExpireTimer(s);
                // respawn (encounter "respawn enabled" + squad respawn counts)
                var sd = Data.Squads[s];
                if (Respawn && ss.RespawnsLeft > 0 && sd.RespawnMax > 0)
                {
                    int living = Living(s, -1);
                    if (living < sd.RespawnMin || (living < sd.RespawnMax && ss.NextRespawn >= 0f))
                    {
                        if (ss.NextRespawn < 0f) ss.NextRespawn = now + Mathf.Max(0.5f, sd.RespawnDelay.Max > 0f ? HaloAi.Rand(sd.RespawnDelay.Min, sd.RespawnDelay.Max) : HaloAi.Rand(Data.RespawnDelay.Min, Data.RespawnDelay.Max));
                        else if (now >= ss.NextRespawn)
                        {
                            ss.NextRespawn = -1f;
                            ss.RespawnsLeft--;
                            HaloAi.PlaceSquad(this, s, 1, false);
                        }
                    }
                }
            }
        }

        public void StartTimer(int squad, float now)
        {
            if (squad < 0 || squad >= Squads.Length) return;
            var ss = Squads[squad];
            if (ss.TimerStarted) return;
            ss.TimerStarted = true;
            float d = Data.Squads[squad].SquadDelayTime;
            ss.TimerEnds = now + Mathf.Max(0f, d);
            if (d <= 0f) ExpireTimer(squad);
        }

        public void ExpireTimer(int squad)
        {
            if (squad < 0 || squad >= Squads.Length) return;
            var ss = Squads[squad];
            ss.TimerStarted = true; ss.TimerExpired = true;
            if (Data.Squads[squad].Has(HaloAiScenario.SqMagicSightAfterTimer))
                for (int i = 0; i < Actors.Count; i++) { var a = Actors[i]; if (a.Alive && a.Squad == squad) a.MagicallySeePlayers(); }
        }

        /// <summary>Whether the squad may fight yet (its delay timer).</summary>
        public bool SquadReleased(int squad)
        {
            if (squad < 0 || squad >= Squads.Length) return true;
            var sd = Data.Squads[squad];
            if (sd.SquadDelayTime <= 0f && !sd.Has(HaloAiScenario.SqNoTimerDelayForever)) return true;
            return Squads[squad].TimerExpired;
        }

        /// <summary>The platoon's squads move into their "maneuver to" squads (taking that squad's firing position groups).</summary>
        public void Maneuver(int platoon, bool flee)
        {
            int moved = 0;
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (!a.Alive || a.Platoon != platoon) continue;
                int to = a.Squad >= 0 && a.Squad < Data.Squads.Count ? Data.Squads[a.Squad].ManeuverToSquad : -1;
                if (to < 0 || to >= Data.Squads.Count) continue;
                a.MoveToSquad(this, to);
                if (flee) a.Panic(null, 0.5f);
                moved++;
            }
            if (moved > 0 && HaloAi.Verbose) HaloLog.Info("AI: " + Name + "/" + Data.Platoons[platoon].Name + " maneuvers (" + moved + " actors)" + (flee ? " fleeing" : ""));
        }

        // ------------------------------------------------------------------ firing positions

        public bool PositionInGroups(int index, uint mask)
        {
            int g = Data.FiringPositions[index].Group;
            return g >= 0 && g < 26 && (mask & (1u << g)) != 0;
        }

        public bool HasPositions(uint mask)
        {
            if (mask == 0) return false;
            for (int i = 0; i < Positions.Length; i++) if (PositionInGroups(i, mask)) return true;
            return false;
        }

        int[] _posComp; HaloAiNav _compNav;

        /// <summary>The navigation island of a firing position (-1 off the navigation / no navigation), cached per BSP.</summary>
        public int PositionComponent(int index)
        {
            var nav = HaloAi.Nav;
            if (nav == null) return -1;
            if (_posComp == null || _compNav != nav)
            {
                _compNav = nav;
                _posComp = new int[Positions.Length];
                for (int i = 0; i < Positions.Length; i++) _posComp[i] = -2;
            }
            if (_posComp[index] == -2) _posComp[index] = ComponentAt(Positions[index]);
            return _posComp[index];
        }

        /// <summary>The navigation island under a Unity point (-1 none).</summary>
        public static int ComponentAt(Vector3 p)
        {
            var nav = HaloAi.Nav;
            if (nav == null) return -1;
            Vector3 h = HaloSpace.ToHalo(p);
            int poly = nav.Locate(h.x, h.y, h.z + 0.1f, 0.4f, 2f, 1f);
            return poly >= 0 ? nav.Component(poly) : -1;
        }

        public void Claim(int index) { if (index >= 0 && index < Claims.Length) Claims[index]++; }
        public void Release(int index) { if (index >= 0 && index < Claims.Length && Claims[index] > 0) Claims[index]--; }
    }
}
