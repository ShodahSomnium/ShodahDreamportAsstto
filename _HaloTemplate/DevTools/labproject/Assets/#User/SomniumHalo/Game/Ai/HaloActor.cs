using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] One AI actor: the brain driving one HaloUnit (Characters) through its control members, from its actor variant (actv)
    /// and actor (actr) data: perception (vision cones, sight lines, perception times, hearing shots, feeling hits), the states Halo plays
    /// (sleeping / guarding / patrolling move positions / searching / combat / fleeing and cowering / hiding with low shields / berserk /
    /// following the players), firing positions from its squad's groups, bursts, grenades, melee, crouching, evading, panicking when a leader
    /// or friends die, and the scripted command lists. Ticked by HaloAi at 30 Hz; every frame the unit moves itself from MoveVelocity.
    /// Split: HaloActor.cs (core, perception, movement), HaloActor.Combat.cs (combat behaviours), HaloActor.Commands.cs (command lists).
    /// </summary>
    public sealed partial class HaloActor
    {
        public enum Mode { Idle, Patrol, Search, Combat, Flee, Cower, Hide, Berserk, Dead }

        public readonly int Id;
        public HaloUnit Unit;
        public HaloAiBody Body;
        public HaloActorVariantDef V;
        public HaloActorDef A;
        public HaloAiEncounter Enc;
        public int Squad = -1, Platoon = -1;
        public HaloAiState InitialState, ReturnState;
        public Mode State = Mode.Idle;
        public bool Braindead, Blind, Deaf, AllowCharge = true, Berserking, ForceActive;
        /// <summary>Scripts' object_set_melee_attack_inhibited / object_set_ranged_attack_inhibited.</summary>
        public bool MeleeInhibited, RangedInhibited;
        public bool Free;                         // ai_free / ai_attach_free: no encounter
        public Vector3 Home; public float HomeYaw;
        public HaloAiScenario.StartingLocation Start;

        // target
        public HaloAiBody Target;
        public Vector3 TargetLastSeen;
        public float TargetSeenAt = -100f, TargetSince = -100f;
        public bool TargetVisible;
        /// <summary>Script preferences: ai_try_to_fight (an ai name), ai_try_to_fight_player.</summary>
        public string FightAi; public bool FightPlayer;
        public HaloAiBody MagicTarget;
        float _sightAccum; HaloAiBody _sightCandidate; int _scanCursor;
        // scripted look
        public GameObject LookAtObject;

        // movement
        Vector3 _goal; bool _hasGoal, _arrived; float _speedFrac = 1f;
        readonly List<Vector3> _path = new List<Vector3>(16);
        int _pathIdx; float _repathAt; Vector3 _stuckRef; float _stuckCheckAt; int _stuckCount;
        float _acceptRadius = 0.45f;
        Vector3 _override; float _overrideUntil = -1f;
        /// <summary>Moves the unit at a fixed velocity for a while (evades, dives, leaps), whatever the path says.</summary>
        public void Push(Vector3 velocity, float seconds) { _override = velocity; _overrideUntil = Now + seconds; }
        static readonly List<HVec3> PathTmp = new List<HVec3>(32);

        // state timers
        float _stateUntil, _stateStarted; Vector3 _threat;
        float _nextIdleLook; Vector3 _idleLook;
        int _patrolIndex = -1, _patrolDir = 1; float _patrolWaitUntil = -1f;

        // damage
        float _lastHitTime = -100f, _lastVitality = 1f, _danger; float _damagedAt = -100f;
        int _friendsKilled;

        internal int TickPhase;
        public float Now => HaloAi.Time;
        public bool Alive => Unit != null && Unit.Alive && State != Mode.Dead;
        /// <summary>Body + shield strength 0..1 (for platoon rules and ai_strength).</summary>
        public float Strength => Unit != null && Unit.Alive ? Mathf.Clamp01(Unit.Body) : 0f;
        public HaloTeam Team => Unit != null ? Unit.Team : HaloTeam.None;
        public Vector3 Position => Unit != null ? Unit.transform.position : Home;
        public bool Flying => (A != null && A.IsFlying) || (Unit != null && Unit.Def != null && Unit.Def.Flying);
        public string Label => (Enc != null ? Enc.Name + (Squad >= 0 && Squad < Enc.Data.Squads.Count ? "/" + Enc.Data.Squads[Squad].Name : "") : "free") + "#" + Id;

        public HaloActor(int id) { Id = id; TickPhase = id % 3; }

        // ------------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            if (State == Mode.Dead) return;
            if (Unit == null || !Unit.Alive) { Died(); return; }
            if (Braindead || (Enc != null && Enc.Braindead) || !HaloAi.Enabled)
            {
                Unit.MoveVelocity = Vector3.zero; Unit.Firing = false; Unit.Frozen = Braindead || (Enc != null && Enc.Braindead);
                return;
            }
            Unit.Frozen = false;
            Unit.Firing = false;

            SenseDamage();
            if ((HaloAi.TickCount + TickPhase) % 3 == 1 && HaloAi.LiveGrenades.Count > 0) DodgeGrenades();
            if ((HaloAi.TickCount + TickPhase) % 3 == 0) Perceive();
            UpdateTarget();

            // vehicles: riding (drive / gun) and boarding take the tick
            if (Unit.Seat != null && Vehicle != null)
            {
                if (Commands != null && Commands.Active) Commands.Step(this, dt);
                TickRiding();
                return;
            }
            if (TickBoarding()) return;

            if (Commands != null && Commands.Active)
            {
                bool interrupt = Commands.List.AllowInitiative && Target != null && TargetVisible;
                if (!interrupt)
                {
                    Commands.Step(this, dt);
                    if (Commands.List.AllowTargeting && Target != null && TargetVisible) Shoot(dt, true);
                    Steer(dt);
                    return;
                }
            }

            switch (State)
            {
                case Mode.Idle: TickIdle(dt); break;
                case Mode.Patrol: TickPatrol(dt); break;
                case Mode.Search: TickSearch(dt); break;
                case Mode.Combat: TickCombat(dt); break;
                case Mode.Flee: TickFlee(dt); break;
                case Mode.Cower: TickCower(dt); break;
                case Mode.Hide: TickHide(dt); break;
                case Mode.Berserk: TickBerserk(dt); break;
            }
            Steer(dt);
        }

        public void SetState(Mode m, float duration = 0f)
        {
            if (State == Mode.Dead) return;
            if (State != m)
            {
                if (HaloAi.Verbose) HaloLog.Info("AI: " + Label + " " + State + " -> " + m + (Target != null ? " (target " + Target + ")" : ""));
                HaloAi.NoteState(m);
                if (m == Mode.Search && Target != null) HaloAiVoice.Say(this, HaloAiVoice.SearchStart, 0.5f);
                else if (m == Mode.Hide) HaloAiVoice.Say(this, HaloAiVoice.Cover, 0.6f);
                State = m;
                _stateStarted = Now;
                ReleasePosition();
            }
            _stateUntil = duration > 0f ? Now + duration : -1f;
            if (Unit != null) Unit.Stance = m == Mode.Flee ? "flee" : (m == Mode.Combat || m == Mode.Search || m == Mode.Hide || m == Mode.Berserk ? null : IdleStance());
        }

        string IdleStance()
        {
            var s = Start != null && Start.InitialState != HaloAiState.None ? Start.InitialState : InitialState;
            if (State == Mode.Idle && s == HaloAiState.Sleeping) return "asleep";
            if (State == Mode.Idle && s == HaloAiState.Alert) return "alert";
            return null;
        }

        /// <summary>The state an actor settles in when it has nothing to do (squad / starting location return state).</summary>
        public void ReturnToRest()
        {
            Target = null; TargetVisible = false; Berserking = false;
            var s = ReturnState != HaloAiState.None ? ReturnState : InitialState;
            EnterAiState(s);
        }

        /// <summary>Enters one of the scenario's / scripts' AI states (ai_set_current_state, a squad's initial state).</summary>
        public void EnterAiState(HaloAiState s)
        {
            _patrolIndex = -1;
            switch (s)
            {
                case HaloAiState.MovingRepeatSamePosition:
                case HaloAiState.MovingLoop:
                case HaloAiState.MovingLoopBackAndForth:
                case HaloAiState.MovingLoopRandomly:
                case HaloAiState.MovingRandomly:
                    SetState(Mode.Patrol); break;
                case HaloAiState.Searching:
                    SetState(Mode.Search, SearchTime() * 2f); break;
                case HaloAiState.Fleeing:
                    Panic(null, 1f); break;
                default:
                    SetState(Mode.Idle); break;
            }
            InitialState = s;
            if (Unit != null) Unit.Stance = IdleStance();
        }

        // ------------------------------------------------------------------ perception

        float PerceptionTime()
        {
            if (A == null) return 0.5f;
            if (State == Mode.Combat || State == Mode.Search || State == Mode.Berserk || State == Mode.Hide) return Mathf.Max(0f, A.CombatPerceptionTime);
            var s = InitialState;
            if (s == HaloAiState.Alert || s == HaloAiState.Guarding || s == HaloAiState.GuardingAtGuardPosition) return Mathf.Max(0f, A.GuardPerceptionTime);
            return Mathf.Max(0f, A.NonCombatPerceptionTime);
        }

        bool Sleeping => State == Mode.Idle && InitialState == HaloAiState.Sleeping;

        /// <summary>Looks for enemies (every third tick): the current target first, then one other candidate per look (round robin).</summary>
        void Perceive()
        {
            if (A == null || Unit == null) return;
            bool blind = Blind || (Enc != null && Enc.Blind) || HaloAi.DebugBlind;
            Vector3 eye = Unit.Eye;
            Vector3 fwd = Unit.transform.forward;
            float range = (V != null && V.ModifiedVisionRange > 0f ? V.ModifiedVisionRange : A.MaxVisionDistance) * HaloSpace.Unit;
            if (range <= 0f) range = 40f * HaloSpace.Unit;
            float cosMax = Mathf.Cos(Mathf.Clamp(A.MaxVisionAngle > 0f ? A.MaxVisionAngle : 1.2f, 0.1f, Mathf.PI));
            float cosPeri = Mathf.Cos(Mathf.Clamp(A.PeripheralVisionAngle, 0f, Mathf.PI));
            float periDist = A.PeripheralDistance * HaloSpace.Unit;
            if (Sleeping) { range = Mathf.Min(range, 2f * HaloSpace.Unit); cosMax = -1f; }
            const float period = 3f / 30f;

            // 1) keep the current target in sight
            if (Target != null && Target.Alive)
            {
                bool vis = !blind && CanSee(eye, fwd, Target, range * 1.25f, -1f, 0f);    // once engaged, any angle (the body turns to it)
                if (vis) { TargetSeenAt = Now; TargetLastSeen = Target.Feet; }
            }

            // 2) one other candidate
            if (blind) return;
            var all = HaloAiBodies.All;
            int n = all.Count;
            if (n == 0) return;
            HaloAiBody best = null; float bestScore = float.MinValue;
            for (int k = 0; k < n; k++)
            {
                var b = all[(k + _scanCursor) % n];
                if (!IsEnemy(b)) continue;
                if (b == Target && TargetVisible) continue;
                float d = (b.Center - eye).magnitude;
                if (d > range) continue;
                float score = -d + (b.Preferred ? 1000f : 0f) + (b.IsPlayer ? 3f : 0f) + (b == _sightCandidate ? 5f : 0f);
                if (FightPlayer && b.IsPlayer) score += 500f;
                if (FightAi != null && b.Actor != null && HaloAi.InSelection(b.Actor, FightAi)) score += 500f;
                if (score > bestScore) { bestScore = score; best = b; }
            }
            _scanCursor++;
            if (best == null) { _sightAccum = 0f; _sightCandidate = null; return; }
            if (best != _sightCandidate) { _sightCandidate = best; _sightAccum = 0f; }
            if (CanSee(eye, fwd, best, range, cosMax, periDist > 0f ? periDist : 0f, cosPeri))
            {
                _sightAccum += period;
                // surprise: an enemy right in front of us
                if (A.SurpriseDistance > 0f && (best.Feet - Unit.transform.position).magnitude < A.SurpriseDistance * HaloSpace.Unit && State != Mode.Combat && A.Has(HaloActorDef.PanicWhenSurprised))
                    Panic(best, 0.6f);
                if (_sightAccum >= PerceptionTime() || Target == null || ShouldSwitch(best))
                {
                    if (Target == null || ShouldSwitch(best)) Acquire(best, "sight");
                    if (best == Target) { TargetSeenAt = Now; TargetLastSeen = best.Feet; }
                }
            }
            else _sightAccum = Mathf.Max(0f, _sightAccum - period);
        }

        bool ShouldSwitch(HaloAiBody b)
        {
            if (Target == null || !Target.Alive) return true;
            if (b == Target) return false;
            if (b.Preferred && !Target.Preferred) return true;
            if (!TargetVisible && Now - TargetSeenAt > 1.5f) return true;
            // much closer
            float dNew = (b.Feet - Position).sqrMagnitude, dOld = (Target.Feet - Position).sqrMagnitude;
            return dNew < dOld * 0.25f;
        }

        bool CanSee(Vector3 eye, Vector3 fwd, HaloAiBody b, float range, float cosMax, float periDist, float cosPeri = -1f)
        {
            Vector3 to = b.Center - eye;
            float d = to.magnitude;
            if (d > range) return false;
            if (cosMax > -1f && d > 0.5f)
            {
                float c = Vector3.Dot(fwd, to / d);
                bool inCone = c >= cosMax || (d <= periDist && c >= cosPeri);
                if (!inCone) return false;
            }
            // head first, then centre (half the budget each time)
            if (HaloAiBodies.Clear(eye, b.Head, false)) return true;
            return HaloAiBodies.Clear(eye, b.Center, false);
        }

        public bool IsEnemy(HaloAiBody b)
        {
            if (b == null || !b.Alive || b == Body || b.Disregarded) return false;
            if (Enc != null && Enc.Playfight) return false;
            return HaloAllegiance.AreEnemies(Team, b.Team);
        }

        void Acquire(HaloAiBody b, string how)
        {
            if (b == null) return;
            bool fresh = Target != b;
            Target = b;
            TargetLastSeen = b.Feet;
            TargetSeenAt = Now;
            if (fresh)
            {
                TargetSince = Now;
                _sightAccum = 0f;
                OnNewTarget();
                if (how == "damage" && V != null && V.SurpriseDelayTime > 0f) _nextBurst = Mathf.Max(_nextBurst, Now + V.SurpriseDelayTime);     // actv surprise delay
                HaloAiVoice.Say(this, how == "damage" ? HaloAiVoice.UnexpectedEnemy : (State == Mode.Combat ? HaloAiVoice.NewEnemyRecentCombat : HaloAiVoice.NewCombatAlone));
                HaloAi.NoteAcquire(this, b, how);
            }
            if (State == Mode.Idle || State == Mode.Patrol || State == Mode.Search)
                if (Released) SetState(Mode.Combat);
        }

        /// <summary>The squad's delay timer let it fight (else it holds until ai_timer_expire / the timer runs out).</summary>
        bool Released => Enc == null || Enc.SquadReleased(Squad);

        void UpdateTarget()
        {
            ResolveAdoptedTarget();
            if (Target != null && (!Target.Alive || Target.Disregarded || !HaloAllegiance.AreEnemies(Team, Target.Team)))
            {
                var was = Target;
                Target = null; TargetVisible = false;
                if (was != null && !was.Alive) HaloAiVoice.Say(this, was.IsPlayer ? HaloAiVoice.KilledEnemyPlayer : HaloAiVoice.KilledEnemy, 0.6f);
                if (was != null && !was.Alive && (State == Mode.Combat || State == Mode.Berserk)) { _stateUntil = Now + 1.5f; }
            }
            TargetVisible = Target != null && Now - TargetSeenAt < 0.35f;
            if (MagicTarget != null) { if (MagicTarget.Alive && IsEnemy(MagicTarget)) Acquire(MagicTarget, "magic"); MagicTarget = null; }
            if (Target == null && (State == Mode.Combat || State == Mode.Berserk) && (_stateUntil < 0f || Now >= _stateUntil))
                ReturnToRest();
            // a squad held by its timer starts the timer on first contact
            if (Target != null && Enc != null && Squad >= 0 && !Enc.Squads[Squad].TimerStarted)
                Enc.StartTimer(Squad, Now);
            if (Target != null && State != Mode.Combat && (State == Mode.Idle || State == Mode.Patrol) && Released) SetState(Mode.Combat);
        }

        /// <summary>Hits on the unit: who hit us becomes the target (a player by its id, else the enemy we were hit from), panic and berserk checks.</summary>
        void SenseDamage()
        {
            float vit = Unit.Body + Unit.Shield;
            float lost = _lastVitality - vit;
            _lastVitality = vit;
            _danger = Mathf.Max(0f, _danger - 1f / 30f);
            if (Unit.LastHitTime > _lastHitTime + 1e-4f)
            {
                _lastHitTime = Unit.LastHitTime;
                _damagedAt = Now;
                var hit = Unit.LastHit;
                float frac = Mathf.Max(0f, lost);
                _danger += 0.5f + frac * 4f;
                // who?
                HaloAiBody from = null;
                if (hit.SourcePlayer >= 0 || hit.SourcePlayer == -3) from = PlayerBody(hit.SourcePlayer);
                if (from == null) from = EnemyFrom(hit.Point - hit.Direction * 5f, hit.Direction);
                if (from != null && IsEnemy(from) && (Target == null || !TargetVisible)) Acquire(from, "damage");
                if (Sleeping || State == Mode.Idle || State == Mode.Patrol) { InitialState = HaloAiState.Alert; if (from != null && Released) SetState(Mode.Combat); }
                // panic from a big hit
                // ours: a chance, and only once hurt (a panic on every hit is not what the game plays)
                if (A != null && A.PanicDamageThreshold > 0f && frac >= A.PanicDamageThreshold && Unit.Body < 0.75f && State != Mode.Flee && !Berserking && HaloAi.Chance(0.5f))
                { HaloAi.NotePanic(this, "damage"); Panic(from, 1f); }
                // berserk from damage
                if (A != null && A.BerserkDamageThreshold > 0f && frac >= A.BerserkDamageThreshold && !Berserking && State != Mode.Flee && (Unit.MaxShield <= 0f || Unit.Shield <= 0.01f))
                    GoBerserk("damage");
                if (A != null && A.DiveIntoCoverChance > 0f && State == Mode.Combat && HaloAi.Chance(A.DiveIntoCoverChance * 0.5f)) WantCover();
            }
        }

        HaloAiBody PlayerBody(int sourcePlayer)
        {
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b.Kind == HaloAiBody.Kinds.LocalPlayer && (sourcePlayer == HaloLocal.NetId || sourcePlayer == -1)) return b;
                if (b.Kind == HaloAiBody.Kinds.StandIn && sourcePlayer == -3) return b;
                if (b.Kind == HaloAiBody.Kinds.RemotePlayer && b.Remote != null && b.Remote.Slot == sourcePlayer) return b;
            }
            return HaloAiBodies.LocalPlayer;
        }

        /// <summary>The enemy nearest to the line the hit came along (a guess for hits from AI units).</summary>
        HaloAiBody EnemyFrom(Vector3 origin, Vector3 dir)
        {
            var all = HaloAiBodies.All;
            HaloAiBody best = null; float bd = float.MaxValue;
            Vector3 me = Position;
            Vector3 back = dir.sqrMagnitude > 1e-6f ? -dir.normalized : Vector3.zero;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (!IsEnemy(b)) continue;
                Vector3 to = b.Center - me;
                float d = to.magnitude;
                if (d > 80f) continue;
                float off = back != Vector3.zero && d > 0.1f ? 1f - Vector3.Dot(back, to / d) : 0.5f;
                float score = d * 0.1f + off * 20f;
                if (score < bd) { bd = score; best = b; }
            }
            return best;
        }

        /// <summary>A shot was heard at a point (players' and AI shots).</summary>
        public void Hear(Vector3 at, HaloAiBody shooter)
        {
            if (Deaf || (Enc != null && Enc.Deaf) || A == null || HaloAi.DebugDeaf) return;
            if (State == Mode.Flee || State == Mode.Cower) return;
            float range = A.HearingDistance * HaloSpace.Unit;
            if ((at - Position).sqrMagnitude > range * range) return;
            if (shooter != null && IsEnemy(shooter))
            {
                if (Target == null || (!TargetVisible && Now - TargetSeenAt > 2f))
                {
                    Target = shooter; TargetLastSeen = at; TargetSeenAt = Now - 1f; TargetSince = Now;
                    if (Sleeping) InitialState = HaloAiState.Alert;
                    if ((State == Mode.Idle || State == Mode.Patrol) && Released) SetState(Mode.Search, SearchTime());
                }
            }
            else if (State == Mode.Idle && Sleeping) InitialState = HaloAiState.Alert;
        }

        /// <summary>Script ai_magically_see_*: aware of the body now, wherever it is.</summary>
        public void MagicallySee(HaloAiBody b) { if (b != null) MagicTarget = b; }

        public void MagicallySeePlayers()
        {
            var p = HaloAiBodies.NearestPlayer(Position);
            if (p != null) MagicallySee(p);
        }

        public float SearchTime()
        {
            float t = A != null ? A.TargetSearchTime.Pick(HaloAi.Rng) : 5f;
            if (t <= 0f) t = 5f;
            if (Enc != null && Enc.Data.SearchBehavior == 2) t *= 3f;          // tenacious
            return t;
        }

        // ------------------------------------------------------------------ death

        void Died()
        {
            if (State == Mode.Dead) return;
            var killer = Unit != null ? Unit.LastHit : default(HaloHit);
            State = Mode.Dead;
            ReleasePosition();
            if (Unit != null) { Unit.MoveVelocity = Vector3.zero; Unit.Firing = false; }
            if (Commands != null) Commands.Stop();
            HaloAi.OnActorDied(this, killer);
        }

        /// <summary>A friend died near us (same encounter or within sight): panic checks (actr friends killed / leader killed).</summary>
        public void FriendDied(HaloActor dead)
        {
            if (!Alive || A == null || dead == this) return;
            // hunters come in bonded pairs: the partner's death sends the other berserk (Halo)
            if (A.Type == HaloActorType.Hunter && dead.A != null && dead.A.Type == HaloActorType.Hunter && dead.Enc == Enc && dead.Squad == Squad && !Berserking)
            { if (dead.Target != null && Target == null) Acquire(dead.Target, "partner"); if (Target != null) GoBerserk("partner died"); return; }
            if (State == Mode.Flee || State == Mode.Cower || Berserking) return;
            float d = (dead.Position - Position).magnitude / HaloSpace.Unit;
            // ours: it must notice the death - close by (5 wu), or in the fight and within 12 wu with a sight line
            bool fighting = Target != null && (State == Mode.Combat || State == Mode.Search || State == Mode.Hide);
            if (d > 12f || (d > 5f && !fighting)) return;
            if (d > 5f && !HaloAiBodies.Clear(Unit.Eye, dead.Position + Vector3.up, true)) return;
            _friendsKilled++;
            HaloAiVoice.Say(this, dead.Body != null && dead.Body.IsPlayer ? HaloAiVoice.FriendPlayerDied : HaloAiVoice.FriendDied, 0.4f);
            bool leader = dead.A != null && dead.A.Type == A.LeaderType && A.LeaderType != A.Type && A.LeaderKilledPanicChance > 0f;
            if (leader && HaloAi.Chance(A.LeaderKilledPanicChance)) { HaloAiVoice.Say(this, HaloAiVoice.FleeLeaderDied); HaloAi.NotePanic(this, "leader killed"); Panic(dead.Target, 1f); return; }
            if (A.FriendsKilledTrigger > 0 && _friendsKilled >= A.FriendsKilledTrigger && HaloAi.Chance(A.FriendKilledPanicChance)) { HaloAi.NotePanic(this, "friends killed"); Panic(dead.Target, 1f); }
        }

        // ------------------------------------------------------------------ squad changes

        public void MoveToSquad(HaloAiEncounter enc, int squad)
        {
            if (enc != Enc)
            {
                if (Enc != null) Enc.Actors.Remove(this);
                ReleasePosition();
                Enc = enc;
                if (enc != null && !enc.Actors.Contains(this)) enc.Actors.Add(this);
            }
            Squad = squad;
            Platoon = enc != null && squad >= 0 && squad < enc.Data.Squads.Count ? enc.Data.Squads[squad].Platoon : -1;
            ReleasePosition();
            _fposUntil = 0f;
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Head for a point (Unity space) at a fraction of the unit's run speed; repaths when the goal moves.</summary>
        public void MoveTo(Vector3 goal, float speedFrac, float accept = 0.45f)
        {
            _speedFrac = speedFrac;
            _acceptRadius = accept;
            if (_hasGoal && (goal - _goal).sqrMagnitude < 0.36f && Now < _repathAt) return;
            if (_hasGoal && _arrived && (goal - _goal).sqrMagnitude < 0.36f) return;
            _goal = goal; _hasGoal = true; _arrived = false;
            Repath();
        }

        public void Stop() { _hasGoal = false; _path.Clear(); _arrived = true; if (Unit != null) Unit.MoveVelocity = Vector3.zero; }

        public bool Arrived => !_hasGoal || _arrived;
        public bool Moving => _hasGoal && !_arrived;
        public Vector3 Goal => _goal;

        void Repath()
        {
            _path.Clear(); _pathIdx = 0;
            _repathAt = Now + 2.5f;
            _stuckRef = Position; _stuckCheckAt = Now + 1.5f;
            var nav = HaloAi.Nav;
            if (nav != null && !Flying)
            {
                Vector3 from = HaloSpace.ToHalo(Position), to = HaloSpace.ToHalo(_goal);
                float r = A != null && A.PathfindingRadius > 0f ? A.PathfindingRadius : 0.1f;
                bool ok = nav.FindPath(new HVec3(from.x, from.y, from.z + 0.05f), new HVec3(to.x, to.y, to.z + 0.05f), r, PathTmp);
                HaloAi.NotePath(nav.LastExpanded);
                bool exact = ok;
                if (!ok)
                {
                    // the goal is on another island (a ledge, a cliff, a crate, behind a lip) or off the navigation: go to the nearest
                    // reachable point of our own island instead of walking off an edge (Halo's actors stop at the closest spot they can reach)
                    int ca = HaloAiEncounter.ComponentAt(Position), cb = HaloAiEncounter.ComponentAt(_goal);
                    if (ca >= 0 && cb == ca)
                    {
                        // same island: narrow portals or the search budget - once more without the radius and a bigger budget
                        ok = exact = nav.FindPath(new HVec3(from.x, from.y, from.z + 0.05f), new HVec3(to.x, to.y, to.z + 0.05f), 0f, PathTmp, 9000);
                    }
                    else if (ca >= 0 && cb != ca)
                    {
                        HVec3 near;
                        if (nav.NearestInComponent(ca, to.x, to.y, to.z, 30f, out near) && nav.FindPath(new HVec3(from.x, from.y, from.z + 0.05f), new HVec3(near.X, near.Y, near.Z + 0.05f), r, PathTmp))
                            HaloAi.NoteDetour();
                        else { _arrived = true; HaloAi.NoteUnreachable(); return; }
                    }
                }
                for (int i = 0; i < PathTmp.Count; i++) _path.Add(HaloSpace.Pos(PathTmp[i]));
                if (_path.Count > 0 && exact) _path[_path.Count - 1] = _goal;
            }
            else _path.Add(_goal);
        }

        /// <summary>Writes MoveVelocity toward the next waypoint (with a little push away from friends standing in the way).</summary>
        void Steer(float dt)
        {
            if (Unit == null) return;
            if (_overrideUntil > Now) { Unit.MoveVelocity = _override; return; }
            if (!_hasGoal || _arrived || _path.Count == 0) { Unit.MoveVelocity = Separation() * 0.5f; return; }
            Vector3 pos = Position;
            Vector3 next = _path[_pathIdx];
            Vector3 d = next - pos;
            if (!Flying) d.y = 0f;
            bool last = _pathIdx >= _path.Count - 1;
            float accept = last ? _acceptRadius : 0.35f;
            while (d.magnitude < accept)
            {
                if (last) { _arrived = true; Unit.MoveVelocity = Vector3.zero; return; }
                _pathIdx++;
                next = _path[_pathIdx]; d = next - pos; if (!Flying) d.y = 0f;
                last = _pathIdx >= _path.Count - 1;
                accept = last ? _acceptRadius : 0.35f;
            }
            float speed = RunSpeed() * _speedFrac;
            // slow down into the goal
            if (last) speed = Mathf.Min(speed, Mathf.Max(0.6f, d.magnitude * 2.5f));
            Vector3 v = d.normalized * speed + Separation();
            if (!Flying) v.y = 0f;
            Unit.MoveVelocity = v;
            // stuck?
            if (Now >= _stuckCheckAt)
            {
                if ((pos - _stuckRef).sqrMagnitude < 0.25f * 0.25f)
                {
                    _stuckCount++;
                    if (_stuckCount >= 2) { _arrived = true; _stuckCount = 0; _fposUntil = 0f; Unit.MoveVelocity = Vector3.zero; HaloAi.NoteStuck(); }
                    else Repath();
                }
                else _stuckCount = 0;
                _stuckRef = pos; _stuckCheckAt = Now + 1.5f;
            }
        }

        public float RunSpeed()
        {
            float s = Unit != null ? Unit.NaturalSpeed : 3f;
            if (s < 0.5f || s > 12f) s = 3f;
            return s;
        }

        Vector3 _sep; int _sepTick = -10;
        Vector3 Separation()
        {
            if (HaloAi.TickCount - _sepTick < 3) return _sep;
            _sepTick = HaloAi.TickCount;
            Vector3 push = Vector3.zero;
            var list = HaloAi.Actors;
            Vector3 me = Position;
            float r = A != null && A.FriendAvoidDist > 0f ? Mathf.Clamp(A.FriendAvoidDist * HaloSpace.Unit, 0.6f, 1.5f) : 0.8f;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == this || !o.Alive) continue;
                Vector3 d = me - o.Position; d.y = 0f;
                float m = d.magnitude;
                if (m < r && m > 1e-3f) push += d / m * (r - m) * 2f;
            }
            _sep = push;
            return push;
        }

        /// <summary>Turns the facing toward a point; AimPoint only when there is something to aim at.</summary>
        void Face(Vector3 look, bool aim)
        {
            if (Unit == null) return;
            Unit.LookPoint = look;
            Unit.AimPoint = aim ? look : Vector3.zero;
        }

        /// <summary>A point on the navigation near a Unity point (null-safe: the point itself without a nav).</summary>
        public static Vector3 OnNav(Vector3 p)
        {
            var nav = HaloAi.Nav;
            if (nav == null) return p;
            Vector3 h = HaloSpace.ToHalo(p);
            int poly = nav.Locate(h.x, h.y, h.z + 0.1f, 0.5f, 2.5f, 2f);
            if (poly < 0) return p;
            var c = nav.ClosestPoint(poly, h.x, h.y);
            return HaloSpace.Pos(c);
        }

        // ------------------------------------------------------------------ idle states

        void TickIdle(float dt)
        {
            // guarding at the guard position: the squad's guard firing positions, else home
            bool atGuard = InitialState == HaloAiState.GuardingAtGuardPosition || ReturnState == HaloAiState.GuardingAtGuardPosition;
            if (Enc != null && FollowLeader(out var lead))
            {
                FollowIdle(lead);
            }
            else if (atGuard && Enc != null)
            {
                if (_fpos < 0 || Now >= _fposUntil)
                {
                    uint mask = GuardMask();
                    int p = PickPosition(mask, null, false);
                    TakePosition(p, A != null ? A.GuardPositionTime.Pick(HaloAi.Rng) : 5f);
                }
                if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 0.5f); else if ((Position - Home).sqrMagnitude > 1f) MoveTo(Home, 0.5f);
            }
            else if ((Position - Home).sqrMagnitude > 4f && State == Mode.Idle && InitialState != HaloAiState.None && !Sleeping)
                MoveTo(Home, 0.5f);
            else if (!Moving) Stop();

            IdleLook();
            bool crouch = A != null && (A.Has(HaloActorDef.CrouchWhenNotInCombat) || (A.Has(HaloActorDef.CrouchWhenGuarding) && (InitialState == HaloAiState.Guarding || atGuard))) && !Moving;
            Unit.Crouching = crouch || (V != null && V.MovementType == 1);
        }

        void IdleLook()
        {
            if (LookAtObject != null) { Face(LookAtObject.transform.position + Vector3.up * 1.5f, false); return; }
            if (Now >= _nextIdleLook || _idleLook == Vector3.zero)
            {
                float t = A != null ? A.NoncombatIdleFacing.Pick(HaloAi.Rng) : 4f;
                _nextIdleLook = Now + Mathf.Max(1.5f, t);
                float yaw = HomeYaw + HaloAi.Rand(-50f, 50f);
                if (Moving) yaw = Unit.Yaw;
                _idleLook = Unit.Eye + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 10f;
            }
            Face(Moving ? Unit.Eye + Unit.MoveVelocity.normalized * 10f : _idleLook, false);
        }

        void TickPatrol(float dt)
        {
            var sq = Enc != null && Squad >= 0 && Squad < Enc.Data.Squads.Count ? Enc.Data.Squads[Squad] : null;
            if (sq == null || sq.MovePositions.Count == 0) { SetState(Mode.Idle); return; }
            var mps = sq.MovePositions;
            if (_patrolIndex < 0) { _patrolIndex = NearestMove(mps); _patrolWaitUntil = -1f; }
            var mp = mps[_patrolIndex];
            Vector3 at = HaloSpace.Pos(mp.Position);
            if (_patrolWaitUntil < 0f)
            {
                MoveTo(at, 0.45f, 0.4f);
                if (Arrived) _patrolWaitUntil = Now + Mathf.Max(0.5f, mp.Time.Pick(HaloAi.Rng));
            }
            else
            {
                Face(Unit.Eye + HaloSpace.Yaw(mp.Facing) * Vector3.forward * 10f, false);
                if (Now >= _patrolWaitUntil)
                {
                    _patrolWaitUntil = -1f;
                    _patrolIndex = NextMove(mps, _patrolIndex);
                }
            }
            if (Moving) Face(Unit.Eye + (at - Position).normalized * 10f, false);
            Unit.Crouching = V != null && V.MovementType == 1;
        }

        int NearestMove(List<HaloAiScenario.MovePosition> mps)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < mps.Count; i++) { float d = (HaloSpace.Pos(mps[i].Position) - Position).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        int NextMove(List<HaloAiScenario.MovePosition> mps, int i)
        {
            int n = mps.Count;
            if (n <= 1) return 0;
            switch (InitialState)
            {
                case HaloAiState.MovingRepeatSamePosition: return i;
                case HaloAiState.MovingLoopBackAndForth:
                    if (i + _patrolDir >= n || i + _patrolDir < 0) _patrolDir = -_patrolDir;
                    return i + _patrolDir;
                case HaloAiState.MovingLoopRandomly:
                case HaloAiState.MovingRandomly:
                    {
                        // weighted pick (move position weights)
                        float total = 0f; for (int k = 0; k < n; k++) if (k != i) total += Mathf.Max(0.01f, mps[k].Weight);
                        float r = (float)HaloAi.Rng.NextDouble() * total;
                        for (int k = 0; k < n; k++) { if (k == i) continue; r -= Mathf.Max(0.01f, mps[k].Weight); if (r <= 0f) return k; }
                        return (i + 1) % n;
                    }
                default: return (i + 1) % n;
            }
        }
    }
}
