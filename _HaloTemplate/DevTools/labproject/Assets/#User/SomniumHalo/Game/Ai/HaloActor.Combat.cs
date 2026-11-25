using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    public sealed partial class HaloActor
    {
        // firing positions
        int _fpos = -1; float _fposUntil; int _oldFpos = -1;
        bool _wantCover;
        float _outOfRangeSince = -1f;
        // bursts
        bool _bursting; float _burstUntil, _nextBurst, _burstStart, _burstLen; Vector3 _burstOrigin;
        // grenades / melee
        float _nextGrenadeCheck, _grenadeAimUntil = -1f; Vector3 _grenadeAim;
        float _nextMelee;
        // crouch / evade
        bool _crouch; float _crouchChangeAt;
        float _nextEvade, _evadeUntil = -1f; Vector3 _evadeDir;
        float _leapUntil = -1f;
        // search
        Vector3 _searchPoint; float _nextSearchPoint;
        // flying
        float _nextSidestep; Vector3 _flyOffset;

        bool Attacking => Enc == null || !Enc.IsPlatoonDefending(Platoon);

        HaloAiScenario.Squad SquadData => Enc != null && Squad >= 0 && Squad < Enc.Data.Squads.Count ? Enc.Data.Squads[Squad] : null;

        uint GuardMask()
        {
            var sq = SquadData; if (sq == null) return 0;
            return Attacking ? sq.AttackingGuard : sq.DefendingGuard;
        }

        uint CombatMask(bool visible)
        {
            var sq = SquadData; if (sq == null) return 0;
            if (Attacking) return visible ? sq.Attacking : sq.AttackingSearch;
            return visible ? sq.Defending : sq.DefendingSearch;
        }

        void OnNewTarget()
        {
            float d = V != null ? V.FirstBurstDelayTime.Pick(HaloAi.Rng) : 0.5f;
            _nextBurst = Mathf.Max(_nextBurst, Now + d * HaloAi.DiffScale(HaloAi.DiffTable.NewTargetDelay));
            _bursting = false;
        }

        // ------------------------------------------------------------------ positions

        void TakePosition(int index, float seconds)
        {
            if (index != _fpos) { ReleasePosition(); if (Enc != null) Enc.Claim(index); }
            _fpos = index;
            _fposUntil = Now + Mathf.Max(1f, seconds);
        }

        void ReleasePosition()
        {
            if (_fpos >= 0 && Enc != null) Enc.Release(_fpos);
            if (_fpos >= 0) _oldFpos = _fpos;
            _fpos = -1;
        }

        /// <summary>
        /// The best firing position of the encounter within the group mask: in the actv's desired combat range of the target, near us,
        /// not held by another actor, away from the one we just left, close to the leader when following; then a sight-line check on the
        /// winner (attacking wants to see the target, cover wants not to). -1 = none in the mask.
        /// </summary>
        int PickPosition(uint mask, HaloAiBody target, bool cover)
        {
            if (Enc == null || mask == 0) return -1;
            Vector3 me = Position;
            float rMin = V != null ? V.DesiredCombatRange.Min * HaloSpace.Unit : 6f, rMax = V != null ? V.DesiredCombatRange.Max * HaloSpace.Unit : 18f;
            if (rMax < rMin) rMax = rMin;
            float avoid = A != null ? A.OldPositionAvoidDist * HaloSpace.Unit : 0f;
            Vector3 tp = target != null ? TargetLastSeen : Vector3.zero;
            Vector3 lead = Vector3.zero; bool following = FollowLeader(out var lb);
            if (following) lead = lb.Feet;
            int best = -1, second = -1; float bs = float.MinValue, ss = float.MinValue;
            var pos = Enc.Positions;
            int myComp = HaloAiEncounter.ComponentAt(me);
            for (int i = 0; i < pos.Length; i++)
            {
                if (!Enc.PositionInGroups(i, mask)) continue;
                // only positions we can walk to (same navigation island)
                if (myComp >= 0 && !Flying) { int c = Enc.PositionComponent(i); if (c >= 0 && c != myComp) continue; }
                Vector3 p = pos[i];
                float s = 0f;
                if (target != null)
                {
                    float d = (p - tp).magnitude;
                    if (cover) s += Mathf.Min(d, rMax * 1.5f) * 0.15f;
                    else s -= Mathf.Max(0f, rMin - d) * 0.6f + Mathf.Max(0f, d - rMax) * 0.4f;
                }
                s -= (p - me).magnitude * 0.12f;
                int claims = Enc.Claims[i] - (i == _fpos ? 1 : 0);
                s -= claims * 12f;
                if (i == _fpos) s += 1.5f;
                if (i == _oldFpos || (avoid > 0f && _oldFpos >= 0 && _oldFpos < pos.Length && (p - pos[_oldFpos]).sqrMagnitude < avoid * avoid)) s -= 3f;
                if (following) s -= Mathf.Max(0f, Flat(p - lead) - Enc.FollowDistance * HaloSpace.Unit * 2f) * 1.5f;
                s += (float)HaloAi.Rng.NextDouble() * 1.2f;
                if (s > bs) { ss = bs; second = best; bs = s; best = i; }
                else if (s > ss) { ss = s; second = i; }
            }
            if (best >= 0 && target != null && HaloAiBodies.BudgetLeft)
            {
                Vector3 eye = pos[best] + Vector3.up * 1.5f;
                bool see = HaloAiBodies.Clear(eye, target.Head, !cover);
                if (see == cover && second >= 0) best = second;     // the runner-up (unchecked) is often as good
            }
            return best;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>No firing positions in the mask: a point at the desired range on the line to the target (on the navigation).</summary>
        Vector3 RangePoint(HaloAiBody target)
        {
            Vector3 me = Position, tp = TargetLastSeen;
            float rMin = V != null ? V.DesiredCombatRange.Min * HaloSpace.Unit : 6f, rMax = V != null ? V.DesiredCombatRange.Max * HaloSpace.Unit : 18f;
            Vector3 d = me - tp; d.y = 0f;
            float dist = d.magnitude;
            if (dist >= rMin && dist <= Mathf.Max(rMin, rMax)) return me;
            if (dist < 0.1f) d = Unit.transform.forward * -1f; else d /= dist;
            float want = Mathf.Clamp(dist, rMin, Mathf.Max(rMin, rMax));
            return OnNav(tp + d * want);
        }

        // ------------------------------------------------------------------ combat

        void TickCombat(float dt)
        {
            if (Target == null) { ReturnToRest(); return; }
            if (!Released) { TickIdle(dt); FaceTarget(false); return; }
            bool visible = TargetVisible;
            Vector3 me = Position;
            float dist = Flat(Target.Feet - me);
            float distWu = dist / HaloSpace.Unit;

            // lost it for a while: search
            float lost = Now - TargetSeenAt;
            if (!visible && lost > Mathf.Max(4f, A != null ? A.HideTargetNotVisibleTime : 4f) + 3f)
            {
                var sq = SquadData;
                bool never = (Enc != null && Enc.Data.SearchBehavior == 1) || (sq != null && sq.Has(HaloAiScenario.SqNeverSearch));
                if (never) { if (lost > 20f) ReturnToRest(); }
                else { SetState(Mode.Search, SearchTime()); return; }
            }

            // shields low: hide (elites), unless berserking
            if (A != null && A.HideShieldFraction > 0f && Unit.MaxShield > 0f && Unit.Shield < A.HideShieldFraction && !Berserking && Now - _damagedAt < 3f)
            {
                SetState(Mode.Hide, Mathf.Max(2f, A.HideBehindCoverTime.Pick(HaloAi.Rng)));
                return;
            }

            // berserk triggers
            if (A != null && !Berserking && Attacking && A.Has(HaloActorDef.AlwaysBerserkInAttackingMode) && visible) { GoBerserk("attacking"); return; }
            if (A != null && !Berserking && A.BerserkProximity > 0f && visible && distWu < A.BerserkProximity) { GoBerserk("proximity"); return; }

            // carriers / suicidal melee: walk in and go off
            if (A != null && A.Has(HaloActorDef.SuicidalMeleeAttack) && A.SuicideSensingDist > 0f && distWu < A.SuicideSensingDist) { Suicide(); return; }

            bool charge = AllowCharge && (Enc == null || Enc.AllowCharge) && A != null &&
                (A.Has(HaloActorDef.AlwaysChargeAtEnemies) || (Attacking && A.Has(HaloActorDef.AlwaysChargeInAttackingMode)) || A.IsSwarm);
            if (V != null && V.Has(HaloActorVariantDef.CannotUseRangedWeapons)) charge = true;

            if (_evadeUntil > Now) { /* evading: Steer is overridden below */ }
            else if (Flying) FlyCombat(visible);
            else if (charge) MoveTo(Target.Feet, 1f, 0.6f);
            else
            {
                // firing position
                if (_wantCover || Now >= _fposUntil)
                {
                    uint mask = CombatMask(visible);
                    int p = PickPosition(mask, Target, _wantCover);
                    if (p < 0 && mask != 0) p = PickPosition(GuardMask(), Target, _wantCover);
                    float hold = A != null ? A.CombatPositionTime.Pick(HaloAi.Rng) : 5f;
                    // following: a firing position far from the leader is no good - stay by the leader
                    if (p >= 0 && FollowLeader(out var fl) && (Enc.Positions[p] - fl.Feet).magnitude > Enc.FollowDistance * HaloSpace.Unit * 3f) p = -1;
                    if (p >= 0) TakePosition(p, hold);
                    else { ReleasePosition(); _fposUntil = Now + hold; _hasGoal = false; MoveTo(FollowLeader(out var l) ? NearLeader(l) : RangePoint(Target), 0.9f); }
                    _wantCover = false;
                }
                // seen but beyond the variant's firing distance for 2 s: close in (a range point), whatever the groups say (ours: the 2 s)
                float maxFire = V != null && V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f;
                if (visible && distWu > maxFire) { if (_outOfRangeSince < 0f) _outOfRangeSince = Now; } else _outOfRangeSince = -1f;
                if (_outOfRangeSince >= 0f && Now - _outOfRangeSince > 2f) { ReleasePosition(); _fposUntil = Now + 3f; MoveTo(RangePoint(Target), 1f); }
                else if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 1f, 0.5f);
            }

            FaceTarget(true);
            Shoot(dt, visible);
            Melee(distWu, visible);
            Grenade(distWu, visible);
            Evade(visible);
            Crouch(visible);
        }

        void FaceTarget(bool aim)
        {
            if (Target == null) return;
            if (_grenadeAimUntil > Now) { Unit.LookPoint = _grenadeAim; Unit.AimPoint = _grenadeAim; return; }
            Vector3 look = TargetVisible ? Target.Head : TargetLastSeen + Vector3.up * 1.5f;
            Unit.LookPoint = look;
            Unit.AimPoint = aim ? AimPointFor(look) : Vector3.zero;
        }

        /// <summary>Where the gun points: the target's body (its last known spot when unseen) + leading + the burst's sweep from its origin.</summary>
        Vector3 AimPointFor(Vector3 look)
        {
            if (!TargetVisible) return TargetLastSeen + Vector3.up * 1.0f;
            Vector3 p = Target.Center;
            // lead the target: actv target leading x difficulty
            float lead = V != null ? Mathf.Clamp01(V.TargetLeading + HaloAi.DiffAdd(HaloAi.DiffTable.TargetLeading)) : 0.5f;
            float speed = HaloAi.ProjectileSpeed(Unit.Weapon);
            if (speed > 1f && lead > 0f)
            {
                float t = (p - Unit.Eye).magnitude / speed;
                p += Target.Velocity * (t * lead);
            }
            // burst sweep: from the origin offset back onto the target while bursting
            if (_bursting && _burstLen > 0f)
            {
                float k = Mathf.Clamp01((Now - _burstStart) / Mathf.Max(0.05f, _burstLen));
                p += _burstOrigin * (1f - k);
            }
            return p;
        }

        void Shoot(float dt, bool visible)
        {
            if (V == null || Unit == null) return;
            bool lastKnown = !visible && A != null && A.Has(HaloActorDef.ShootAtTargetsLastLocation) && Now - TargetSeenAt < 2.5f;
            bool can = Target != null && (visible || lastKnown) && !RangedInhibited && !V.Has(HaloActorVariantDef.CannotUseRangedWeapons) && Unit.Weapon != null;
            if (can)
            {
                float d = (Target.Center - Unit.Eye).magnitude / HaloSpace.Unit;
                float maxD = V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f;
                if (Berserking && V.BerserkFiringRanges.Max > 0f) maxD = V.BerserkFiringRanges.Max;
                if (d > maxD) can = false;
            }
            if (can && A != null && A.HasMore(HaloActorDef.MustStopToFire) && Moving) can = false;
            if (_grenadeAimUntil > Now) can = false;
            // facing: do not start a burst with the target behind us (unless the actor starts firing before aligned)
            if (can && !(A != null && A.Has(HaloActorDef.StartFiringBeforeAligned)))
            {
                Vector3 to = Target.Center - Unit.transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Dot(Unit.transform.forward, to.normalized) < 0.5f) can = false;
            }

            if (_bursting)
            {
                if (Now >= _burstUntil || !can)
                {
                    _bursting = false;
                    float sep0 = V.NewTargetFiringPatternTime > 0f && Now - TargetSince < V.NewTargetFiringPatternTime && V.NewTargetBurstSeparation > 0f ? V.NewTargetBurstSeparation : -1f;
                    float sep = (Berserking && V.BerserkBurstSeparation > 0f ? V.BerserkBurstSeparation : (Moving && V.MovingBurstSeparation > 0f ? V.MovingBurstSeparation : V.BurstSeparation.Pick(HaloAi.Rng)));
                    _nextBurst = Now + Mathf.Max(0.1f, (sep0 > 0f ? sep0 : sep) * HaloAi.DiffScale(HaloAi.DiffTable.BurstSeparation));
                }
            }
            else if (can && Now >= _nextBurst)
            {
                _bursting = true;
                _burstLen = Berserking && V.BerserkBurstDuration > 0f ? V.BerserkBurstDuration : (Moving && V.MovingBurstDuration > 0f ? V.MovingBurstDuration : V.BurstDuration.Pick(HaloAi.Rng));
                // actv "new target" firing pattern for the first seconds on a new target
                if (V.NewTargetFiringPatternTime > 0f && Now - TargetSince < V.NewTargetFiringPatternTime && V.NewTargetBurstDuration > 0f) _burstLen = V.NewTargetBurstDuration;
                if (_burstLen <= 0f) _burstLen = 0.5f;
                _burstStart = Now; _burstUntil = Now + _burstLen;
                // the burst origin: off the target by the burst origin radius (x difficulty burst error), swept back during the burst
                float r = V.BurstOriginRadius * HaloSpace.Unit * HaloAi.DiffScale(HaloAi.DiffTable.BurstError);
                Vector3 side = Vector3.Cross(Vector3.up, (Target.Center - Unit.Eye).normalized);
                float a = HaloAi.Rand(0f, Mathf.PI * 2f);
                _burstOrigin = (side * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a) * 0.5f) * r;
                HaloAiVoice.Say(this, Berserking ? HaloAiVoice.ShootingBerserk : HaloAiVoice.Shooting, 0.08f);
                HaloAi.NoteShot(this);
            }
            Unit.Firing = _bursting && can;
            if (!can && Target != null) HaloAi.NoteNoShot(WhyNot(visible, lastKnown));
        }

        int WhyNot(bool visible, bool lastKnown)
        {
            if (!visible && !lastKnown) return 0;
            if (Unit.Weapon == null) return 1;
            if (RangedInhibited || V.Has(HaloActorVariantDef.CannotUseRangedWeapons)) return 2;
            float d = (Target.Center - Unit.Eye).magnitude / HaloSpace.Unit;
            if (d > (V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f)) return 3;
            if (_grenadeAimUntil > Now) return 5;
            return 4;   // moving / facing
        }

        void Melee(float distWu, bool visible)
        {
            if (V == null || A == null || Target == null || !visible || MeleeInhibited) return;
            float range = Berserking && V.BerserkMeleeRange > 0f ? V.BerserkMeleeRange : V.MeleeRange;
            if (range <= 0f) return;
            // leaping attack (hunters, flood): jump in from the leap range
            if (A.MeleeLeapChance > 0f && distWu >= A.MeleeLeapRange.Min && distWu <= A.MeleeLeapRange.Max && Now >= _nextMelee && _leapUntil < Now && HaloAi.Chance(A.MeleeLeapChance * 0.1f))
            {
                _leapUntil = Now + 0.6f;
                HaloAiVoice.Say(this, HaloAiVoice.LeapAttack);
                Unit.Jump = true;
            }
            if (_leapUntil > Now) { Vector3 d = Target.Feet - Position; d.y = 0f; Push(d.normalized * Mathf.Max(RunSpeed(), A.MeleeLeapVelocity * 30f * HaloSpace.Unit), 0.1f); }
            if (distWu > range + A.MeleeFudgeFactor) return;
            if (Now < _nextMelee) return;
            Unit.Melee = true;
            HaloAiVoice.Say(this, HaloAiVoice.MeleeAttack, 0.5f);
            float baseDelay = HaloAi.DiffAdd(HaloAi.DiffTable.MeleeDelayBase);
            _nextMelee = Now + Mathf.Max(0.8f, baseDelay + A.MeleeAttackDelay * 0.3f * HaloAi.DiffScale(HaloAi.DiffTable.MeleeDelayScale));
            HaloAi.NoteMelee(this);
        }

        void Grenade(float distWu, bool visible, bool seekingCover = false)
        {
            if (V == null || !HaloAi.Grenades || V.GrenadeStimulus == 0 || Target == null) return;
            if (Now < _nextGrenadeCheck && !seekingCover) return;
            _nextGrenadeCheck = Now + Mathf.Max(1f, V.GrenadeCheckTime > 0f ? V.GrenadeCheckTime : 2f);
            if (Unit.Grenades <= 0 && !V.Has(HaloActorVariantDef.HasUnlimitedGrenades)) return;
            if (V.GrenadeStimulus == 1 && !visible) return;
            // "seek cover": as the actor runs for cover / flees, or when the target just went out of sight
            if (V.GrenadeStimulus == 2 && !(seekingCover || _wantCover || State == Mode.Hide || State == Mode.Flee || (!visible && Now - TargetSeenAt < 3f))) return;
            if (distWu < V.GrenadeRanges.Min || (V.GrenadeRanges.Max > 0f && distWu > V.GrenadeRanges.Max)) return;
            if (Enc != null && Now - Enc.LastGrenade < V.EncounterGrenadeTimeout) return;
            // enough enemies near the target, no friends in the blast
            Vector3 at = TargetLastSeen;
            int enemies = 0, friends = 0;
            float er = Mathf.Max(0.5f, V.EnemyRadius) * HaloSpace.Unit, cr = V.CollateralDamageRadius * HaloSpace.Unit;
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (!b.Alive) continue;
                float d2 = (b.Feet - at).sqrMagnitude;
                if (IsEnemy(b)) { if (d2 <= er * er) enemies++; }
                else if (b != Body && HaloAllegiance.AreFriends(Team, b.Team) && d2 <= cr * cr) friends++;
            }
            if (enemies < Mathf.Max(1, V.MinimumEnemyCount) || friends > 0) return;
            if (!HaloAi.Chance(V.GrenadeChance * HaloAi.DiffScale(HaloAi.DiffTable.GrenadeChanceScale))) return;
            _grenadeAim = at;
            _grenadeAimUntil = Now + 0.6f;
            Unit.AimPoint = at; Unit.LookPoint = at;
            Unit.ThrowGrenade = true;
            HaloAiVoice.Say(this, HaloAiVoice.GrenadeThrowing);
            if (Enc != null) Enc.LastGrenade = Now;
            HaloAi.NoteGrenade(this);
        }

        /// <summary>Danger from hits and from enemies shooting at us: past the actor's evasion threshold an evader steps aside.</summary>
        void Evade(bool visible)
        {
            if (A == null || Unit == null || Flying) return;
            float threshold = Attacking ? A.AttackingEvasionThreshold : A.DefendingEvasionThreshold;
            if (threshold <= 0f || Now < _nextEvade || _evadeUntil > Now) return;
            if (_danger < threshold) return;
            _danger = 0f;
            _nextEvade = Now + Mathf.Max(1f, A.EvasionDelayTime > 0f ? A.EvasionDelayTime : 2f);
            if (HaloAi.Chance(A.EvasionSeekCoverChance)) { WantCover(); return; }
            Vector3 to = Target != null ? Target.Feet - Position : Unit.transform.forward; to.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * (HaloAi.Chance(0.5f) ? 1f : -1f);
            // keep on the ground we know: try the other side when this one has no floor / a wall
            if (!SideClear(side)) side = -side;
            if (!SideClear(side)) return;
            float speed = Mathf.Max(RunSpeed() * 1.4f, (Unit.Def != null && Unit.Def.EvadeDistance > 0f ? Unit.Def.EvadeDistance * HaloSpace.Unit / 0.5f : 4f));
            _evadeDir = side * Mathf.Min(speed, 7f);
            _evadeUntil = Now + 0.45f;
            // the unit's own evade animation (its root motion carries it); a plain side step when the graph has none
            bool right = Vector3.Dot(side, Unit.transform.right) > 0f;
            if (!Unit.Evade(right)) Push(_evadeDir, 0.45f);
            HaloAi.NoteEvade(this);
        }

        bool SideClear(Vector3 side)
        {
            Vector3 from = Position + Vector3.up * 0.6f;
            if (!HaloAiBodies.BudgetLeft) return true;
            if (!HaloAiBodies.Clear(from, from + side * 2.2f, true)) return false;
            var nav = HaloAi.Nav;
            if (nav == null) return true;
            Vector3 h = HaloSpace.ToHalo(Position + side * 2f);
            return nav.Locate(h.x, h.y, h.z + 0.1f, 0.3f, 0.6f, 0f) >= 0;
        }

        /// <summary>A shot was fired near / at us (a player's shot along <paramref name="dir"/>): danger for the evasion check.</summary>
        public void ShotAt(Vector3 from, Vector3 dir)
        {
            if (!Alive || A == null) return;
            Vector3 to = Unit.Center - from;
            float d = to.magnitude;
            if (d < 0.5f || d > 40f * HaloSpace.Unit) return;
            float c = Vector3.Dot(dir.normalized, to / d);
            if (c < 0.985f) return;      // ~10 degrees
            _danger += 0.6f;
            if (State == Mode.Idle || State == Mode.Patrol) { var p = PlayerNear(from); if (p != null) Acquire(p, "shot at"); }
        }

        HaloAiBody PlayerNear(Vector3 p)
        {
            var all = HaloAiBodies.All; HaloAiBody best = null; float bd = 4f;
            for (int i = 0; i < all.Count; i++) { var b = all[i]; if (!b.IsPlayer || !IsEnemy(b)) continue; float d = (b.Head - p).magnitude; if (d < bd) { bd = d; best = b; } }
            return best;
        }

        void Crouch(bool visible)
        {
            if (A == null || Unit == null) return;
            bool want = V != null && V.MovementType == 1;
            switch (A.DefensiveCrouchType)
            {
                case 1: want |= Now - _damagedAt < 2f || _danger > 0.5f; break;
                case 2: want |= Unit.MaxShield > 0f ? Unit.Shield < Mathf.Max(0.05f, A.DefendingCrouchThreshold) : Unit.Body < Mathf.Max(0.05f, A.DefendingCrouchThreshold); break;
                case 3: want |= visible && !_bursting; break;                 // jackals: behind the shield between bursts
                case 4: want |= Target != null; break;
            }
            if (!Attacking && A.DefendingCrouchThreshold > 0f && _danger > A.DefendingCrouchThreshold) want = true;
            if (Moving && (A.Has(HaloActorDef.CannotMoveWhileCrouching) || A.DefensiveCrouchType != 3)) want = V != null && V.MovementType == 1;
            if (A.Has(HaloActorDef.MustCrouchToShoot) && _bursting) want = true;
            if (A.HasMore(HaloActorDef.MustStandToFire) && _bursting) want = false;
            if (want != _crouch && Now >= _crouchChangeAt)
            {
                _crouch = want;
                _crouchChangeAt = Now + Mathf.Max(0.3f, want ? A.MinCrouchTime : A.MinStandTime);
            }
            Unit.Crouching = _crouch;
        }

        void WantCover() { _wantCover = true; }

        float _nextDive;
        /// <summary>A live grenade close by: dive away from it (actr "dive from grenade chance"), at most every 2 s. The 3.5 m danger radius is ours.</summary>
        void DodgeGrenades()
        {
            if (A == null || A.DiveFromGrenadeChance <= 0f || Now < _nextDive || Flying || Unit == null) return;
            Vector3 me = Position;
            var list = HaloAi.LiveGrenades;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 d = me - list[i];
                if (d.sqrMagnitude > 3.5f * 3.5f || Mathf.Abs(d.y) > 2.5f) continue;
                _nextDive = Now + 2f;
                if (!HaloAi.Chance(A.DiveFromGrenadeChance)) return;
                d.y = 0f;
                if (d.sqrMagnitude < 0.01f) d = -Unit.transform.forward;
                d.Normalize();
                float f = Vector3.Dot(d, Unit.transform.forward), s = Vector3.Dot(d, Unit.transform.right);
                int dir = Mathf.Abs(f) >= Mathf.Abs(s) ? (f > 0f ? 0 : 1) : (s > 0f ? 3 : 2);
                HaloAiVoice.Say(this, HaloAiVoice.GrenadeSighted);
                if (!Unit.Dive(dir)) Push(d * Mathf.Max(4f, RunSpeed() * 1.5f), 0.6f);
                HaloAi.NoteEvade(this);
                return;
            }
        }

        // ------------------------------------------------------------------ search

        void TickSearch(float dt)
        {
            if (Target != null && TargetVisible && Released) { SetState(Mode.Combat); return; }
            if (_stateUntil > 0f && Now >= _stateUntil) { ReturnToRest(); return; }
            // search positions of the squad first, else around the last known spot
            if (Now >= _nextSearchPoint || Arrived)
            {
                _nextSearchPoint = Now + HaloAi.Rand(3f, 6f);
                int p = PickPosition(CombatMask(false), Target, false);
                if (p >= 0) { TakePosition(p, 4f); _searchPoint = Enc.Positions[p]; }
                else
                {
                    Vector3 c = Target != null ? TargetLastSeen : Home;
                    _searchPoint = c;
                    var nav = HaloAi.Nav;
                    if (nav != null)
                    {
                        Vector3 h = HaloSpace.ToHalo(c);
                        HVec3 q;
                        if (nav.RandomPointNear(new HVec3(h.x, h.y, h.z + 0.1f), 4f, HaloAi.Rng, out q)) _searchPoint = HaloSpace.Pos(q);
                    }
                }
            }
            MoveTo(_searchPoint, 0.55f);
            Vector3 lookAt = Target != null && Now - TargetSeenAt < 6f ? TargetLastSeen + Vector3.up * 1.5f : Unit.Eye + Unit.transform.forward * 10f;
            Face(lookAt, Target != null);
            Unit.Crouching = false;
        }

        // ------------------------------------------------------------------ flee / cower / hide / berserk

        /// <summary>Panics: runs away from the threat (flee seat), then cowers; grunts spread it to their squad (actr "panic in groups").</summary>
        public void Panic(HaloAiBody threat, float durationScale)
        {
            if (!Alive || A == null) return;
            if (State == Mode.Flee || State == Mode.Cower || Berserking) return;
            _threat = threat != null ? threat.Feet : (Target != null ? TargetLastSeen : Position - Unit.transform.forward * 5f);
            float t = A.RetreatTime.Max > 0f ? A.RetreatTime.Pick(HaloAi.Rng) * 0.35f : 4f;    // ours: a third of the actr retreat time
            SetState(Mode.Flee, Mathf.Clamp(t * durationScale, 2f, 8f));
            Unit.Gesture(41);          // the graph's "panic" (HaloAnimIds: 41)
            HaloAiVoice.Say(this, HaloAiVoice.Flee);
            if (V != null && V.GrenadeStimulus == 2 && Target != null) Grenade((Target.Feet - Position).magnitude / HaloSpace.Unit, TargetVisible, true);
            _hasGoal = false;
            int p = FarPosition(_threat);
            if (p >= 0) TakePosition(p, 10f);
            if (A.HasMore(HaloActorDef.PanicInGroups) && Enc != null)
                for (int i = 0; i < Enc.Actors.Count; i++)
                {
                    var o = Enc.Actors[i];
                    if (o != this && o.Alive && o.Squad == Squad && (o.Position - Position).magnitude < 8f * HaloSpace.Unit) o.Panic(threat, durationScale);
                }
        }

        int FarPosition(Vector3 from)
        {
            if (Enc == null) return -1;
            int best = -1; float bs = float.MinValue;
            Vector3 me = Position;
            for (int i = 0; i < Enc.Positions.Length; i++)
            {
                Vector3 p = Enc.Positions[i];
                float dm = Flat(p - me);
                if (dm > 25f * HaloSpace.Unit) continue;
                float s = Flat(p - from) - dm * 0.3f - Enc.Claims[i] * 5f;
                if (s > bs) { bs = s; best = i; }
            }
            return best;
        }

        void TickFlee(float dt)
        {
            if (_stateUntil > 0f && Now >= _stateUntil)
            {
                float cower = A != null ? A.CoweringTime.Pick(HaloAi.Rng) : 4f;
                SetState(Mode.Cower, Mathf.Max(1f, cower));
                return;
            }
            if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 1f);
            else
            {
                Vector3 away = Position - _threat; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -Unit.transform.forward;
                MoveTo(OnNav(Position + away.normalized * 8f), 1f);
            }
            Unit.Crouching = false;
            Face(Unit.Eye + (Unit.MoveVelocity.sqrMagnitude > 0.01f ? Unit.MoveVelocity.normalized : Unit.transform.forward) * 10f, false);
        }

        void TickCower(float dt)
        {
            Stop();
            Unit.Crouching = true;
            Face(_threat + Vector3.up * 1.5f, false);
            if (_stateUntil > 0f && Now >= _stateUntil)
            {
                if (Target != null && Target.Alive) SetState(Mode.Combat); else ReturnToRest();
            }
        }

        void TickHide(float dt)
        {
            if (Target == null) { ReturnToRest(); return; }
            // shields back: return to the fight (actr attack / pursue shield fractions)
            float back = A != null && A.AttackShieldFraction > 0f ? A.AttackShieldFraction : 0.65f;
            if ((Unit.MaxShield <= 0f || Unit.Shield >= back) && (_stateUntil < 0f || Now >= _stateUntil)) { SetState(Mode.Combat); return; }
            if (_fpos < 0 || Now >= _fposUntil)
            {
                int p = PickPosition(CombatMask(false) | GuardMask(), Target, true);
                if (p >= 0) TakePosition(p, 6f);
                else
                {
                    Vector3 away = Position - TargetLastSeen; away.y = 0f;
                    MoveTo(OnNav(Position + away.normalized * 6f), 1f);
                    _fposUntil = Now + 3f;
                }
            }
            if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 1f);
            FaceTarget(true);
            Shoot(dt, TargetVisible);
            Grenade((Target.Feet - Position).magnitude / HaloSpace.Unit, TargetVisible);
            Unit.Crouching = Arrived && A != null && A.Has(HaloActorDef.CrouchWhenHidingFromUnopposable);
        }

        bool _kamikaze;

        public void GoBerserk(string why)
        {
            if (!Alive || Berserking) return;
            Berserking = true;
            // grunts' kamikaze: actr "berserk grenade chance" (higher difficulties: x the globals' grenade chance scale) - run in with a grenade
            _kamikaze = A != null && A.BerserkGrenadeChance > 0f && Unit.Grenades > 0 && HaloAi.Chance(A.BerserkGrenadeChance * HaloAi.DiffScale(HaloAi.DiffTable.GrenadeChanceScale));
            SetState(Mode.Berserk, 12f);
            Unit.Gesture(29);          // "berserk"
            HaloAiVoice.Say(this, HaloAiVoice.Berserk);
            HaloAi.NoteBerserk(this, why);
        }

        void TickBerserk(float dt)
        {
            if (Target == null || !Target.Alive || (_stateUntil > 0f && Now >= _stateUntil)) { Berserking = false; if (Target != null && Target.Alive) SetState(Mode.Combat); else ReturnToRest(); return; }
            if (A != null && A.Has(HaloActorDef.SuicidalMeleeAttack) && A.SuicideSensingDist > 0f && (Target.Feet - Position).magnitude / HaloSpace.Unit < A.SuicideSensingDist) { Suicide(); return; }
            if (_kamikaze && (Target.Feet - Position).magnitude < 2.2f * HaloSpace.Unit && Unit.Grenades > 0)
            { _kamikaze = false; Unit.AimPoint = Target.Feet; Unit.ThrowGrenade = true; HaloAi.NoteGrenade(this); }
            MoveTo(Target.Feet, 1f, 0.5f);
            Unit.Stance = A != null && A.Has(HaloActorDef.BerserkingUsesPanickedMovement) ? "flee" : null;
            Unit.Crouching = false;
            FaceTarget(true);
            Shoot(dt, TargetVisible);
            Melee((Target.Feet - Position).magnitude / HaloSpace.Unit, TargetVisible);
        }

        void Suicide()
        {
            HaloAi.NoteSuicide(this);
            Unit.Kill();
        }

        // ------------------------------------------------------------------ flying

        void FlyCombat(bool visible)
        {
            // hover in the desired range, a little above the target's head, sidestepping now and then (actr free flying sidestep)
            Vector3 me = Position, tp = TargetLastSeen + Vector3.up * 2.5f;
            float rMin = V != null ? V.DesiredCombatRange.Min * HaloSpace.Unit : 6f, rMax = V != null ? Mathf.Max(V.DesiredCombatRange.Max * HaloSpace.Unit, rMin) : 15f;
            Vector3 d = me - tp; float dist = d.magnitude;
            if (dist < 0.1f) d = Vector3.forward; else d /= dist;
            if (Now >= _nextSidestep)
            {
                _nextSidestep = Now + HaloAi.Rand(1.5f, 3.5f);
                float side = (A != null && A.FreeFlyingSidestep > 0f ? A.FreeFlyingSidestep : 1f) * HaloSpace.Unit;
                _flyOffset = Vector3.Cross(Vector3.up, d) * HaloAi.Rand(-side, side) + Vector3.up * HaloAi.Rand(-1f, 1.5f);
            }
            float want = Mathf.Clamp(dist, rMin, rMax);
            Vector3 goal = tp + d * want + _flyOffset;
            // stay out of walls: if the straight line to the goal is blocked, hold
            if (HaloAiBodies.BudgetLeft && !HaloAiBodies.Clear(me, goal, true)) goal = me + _flyOffset * 0.3f;
            MoveTo(goal, 0.8f, 0.6f);
        }

        // ------------------------------------------------------------------ following (allies)

        /// <summary>The body this actor's encounter follows (ai_follow_target_players / unit / ai), null when none.</summary>
        bool FollowLeader(out HaloAiBody leader)
        {
            leader = null;
            if (Enc == null) return false;
            if (Enc.FollowPlayers) leader = HaloAiBodies.NearestPlayer(Position);
            else if (Enc.FollowUnit != null) leader = HaloAiBodies.Of(Enc.FollowUnit);
            else if (Enc.FollowAi != null) leader = HaloAi.FirstBodyOf(Enc.FollowAi, this);
            return leader != null && leader.Alive;
        }

        Vector3 NearLeader(HaloAiBody l)
        {
            // a spot beside / behind the leader, spread by our id
            float ang = (Id * 67f) % 360f;
            Vector3 off = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * Mathf.Max(1.5f, Enc.FollowDistance * HaloSpace.Unit * 0.6f);
            return OnNav(l.Feet + off);
        }

        void FollowIdle(HaloAiBody l)
        {
            float d = Flat(l.Feet - Position);
            float far = Enc.FollowDistance * HaloSpace.Unit;
            if (d > far) MoveTo(NearLeader(l), d > far * 2f ? 1f : 0.6f, 0.8f);
            else if (Arrived) Stop();
            if (!Moving) Face(l.Head, false);
        }
    }
}
