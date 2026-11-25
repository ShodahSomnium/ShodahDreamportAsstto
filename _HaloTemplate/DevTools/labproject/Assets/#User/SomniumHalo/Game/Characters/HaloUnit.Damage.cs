using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Damage as Halo's units take it: the hit lands on a hit box (a collision-model node: its region and material); the shield takes it
    /// first where the material passes damage to the shield (elites everywhere; a jackal only on its arm shield), scaled by the damage
    /// effect's modifier for the shield's material type and the material's shield multiplier (the material's shield leak passes through);
    /// the body takes the rest by the modifier for the material type and the material's body multiplier; headshots (a "head" material, a
    /// damage that can cause headshots, no shield) kill. Regions with a damage threshold are shot off (flood arms / heads: their "~damaged"
    /// permutation) and may force the death or the weapon's drop. The shield recharges after the collision model's stun time over its
    /// recharge time, with the shield effects. Hit reactions (s-ping / h-ping by the biped's thresholds), deaths (s-kill / h-kill: hard for
    /// explosions and hits past the hard death threshold) by direction and body region; a body destroyed past its threshold plays the
    /// destroyed effect and goes (infection forms pop). Death raises HaloEvents.Killed (killer = the player's id, victim -1) and
    /// HaloBipeds.Died; the weapon falls; the body stays (HaloBipeds.BodySeconds).
    /// </summary>
    public partial class HaloUnit : IHaloHitInfo
    {
        private float _shieldStunUntil;
        private bool _shieldRechargeSounded = true;
        private float _nextPing;
        private float _shieldFlash;
        private readonly Dictionary<int, float> _regionDamage = new Dictionary<int, float>();
        private static int _deathsLogged;

        /// <summary>The shield's recent hit glow 0..1 (the shield effect; fades in a quarter second).</summary>
        public float ShieldFlash => _shieldFlash;

        public virtual void TakeHit(HaloHit hit)
        {
            if (!Alive || Def == null) return;
            if (Invulnerable && !hit.Instant) return;
            if (Puppet) { try { HaloNetGame.PuppetHit(this, hit); } catch (System.Exception) { } return; }
            var cat = HaloCombat.Catalog;
            HaloDamageDef dd = hit.Effect != null && cat != null ? cat.Damage(hit.Effect) : null;
            HaloCollisionDef coll = Def.Coll;
            Hitbox hb = Nearest(hit.Point);
            HaloCollisionDef.Material mat = coll != null && hb != null ? coll.MaterialAt(hb.Material) : (coll != null && coll.Materials.Count > 0 ? coll.Materials[0] : null);
            LastHit = hit;
            LastHitTime = Time.time;
            int dir = Direction(hit.Direction);
            int region = hb != null ? hb.AnimRegion : 0;
            if (hit.Instant || (hit.Melee && hit.BackHit && MaxShield <= 0f)) { Die(hit, true, dir, region, false, 1f); return; }
            if (coll != null && coll.OnlyDamagedByExplosives && !hit.Explosion) return;
            if (MaxBody <= 0f && !hit.Instant) return;      // no body vitality at all (Cortana, the monitor): cannot be hurt
            float amount = Mathf.Max(0f, hit.Amount);
            if (amount <= 0f) return;
            if (hit.SourcePlayer >= 0 && HaloAllegiance.AreFriends(Team, HaloTeam.Player) && coll != null) amount *= Mathf.Clamp01(1f - coll.FriendlyDamageResistance);

            float maxS = MaxShield, maxB = Mathf.Max(0.01f, MaxBody);
            float shieldPts = Shield * maxS, bodyPts = Body * maxB;
            float rest = amount, shieldTaken = 0f;
            bool hadShield = shieldPts > 0.01f;
            float shieldMult = mat != null ? mat.ShieldMultiplier : 1f;
            if (maxS > 0f) { _shieldStunUntil = Time.time + (coll != null && coll.StunTime > 0f ? coll.StunTime : 1f); _shieldRechargeSounded = false; }
            if (hadShield && shieldMult > 0f && !(dd != null && dd.SkipsShields))
            {
                float mod = (dd != null ? dd.Modifier(coll != null ? coll.ShieldMaterialType : HaloDamageDef.CyborgEnergyShield) : 1f) * shieldMult;
                if (mod > 0f)
                {
                    float s = rest * mod;
                    float leak = mat != null ? Mathf.Clamp01(mat.ShieldLeak) : 0f;
                    if (s <= shieldPts) { shieldPts -= s; shieldTaken = s; rest = rest * leak; }
                    else
                    {
                        shieldTaken = shieldPts;
                        rest = (s - shieldPts) / mod;
                        shieldPts = 0f;
                        if (coll != null && coll.ShieldDepletedEffect != null) HaloCombatFx.Play(coll.ShieldDepletedEffect, Center, transform.rotation, BodyMarker());
                    }

                    _shieldFlash = 1f;
                    if (coll != null && coll.ShieldDamagedEffect != null && shieldPts > 0f) HaloCombatFx.Play(coll.ShieldDamagedEffect, hit.Point != Vector3.zero ? hit.Point : Center, transform.rotation, null);
                }
            }

            if (dd != null && dd.OnlyHurtsShields) rest = 0f;
            Shield = maxS > 0f ? shieldPts / maxS : 0f;
            bool head = hit.Headshot || (mat != null && mat.Head && dd != null && dd.CanCauseHeadshots);
            if (head && dd != null && dd.CanCauseHeadshots && shieldPts <= 0.01f && rest > 0f) { Die(hit, false, dir, 2, true, 1f); return; }

            float bodyTaken = 0f;
            if (rest > 0f)
            {
                float mod = (dd != null && mat != null ? dd.Modifier(mat.Type) : 1f) * (mat != null ? mat.BodyMultiplier : 1f);
                bodyTaken = rest * mod;
                float before = bodyPts;
                bodyPts -= bodyTaken;
                if (coll != null && coll.BodyDamagedEffect != null && before > coll.BodyDamagedThreshold * maxB && bodyPts <= coll.BodyDamagedThreshold * maxB)
                    HaloCombatFx.Play(coll.BodyDamagedEffect, Center, transform.rotation, BodyMarker());
                if (hb != null && hb.Region >= 0 && coll != null && hb.Region < coll.Regions.Count) RegionDamage(coll.Regions[hb.Region], hb.Region, bodyTaken, hit);
                if (!Alive) return;
            }

            Body = bodyPts / maxB;
            float frac = (shieldTaken + bodyTaken) / (maxB + Mathf.Max(0f, maxS));
            HaloBipeds.RaiseDamaged(this, hit);
            if (bodyPts <= 0f)
            {
                bool hard = hit.Explosion || (Def.HardDeathThreshold > 0f && bodyTaken / maxB >= Def.HardDeathThreshold);
                Die(hit, hard, dir, region, false, -bodyPts / maxB);
                return;
            }

            // hit reactions
            if (Animator != null && Time.time >= _nextPing)
            {
                bool hard = Def.HardPingThreshold > 0f && frac >= Def.HardPingThreshold || hit.Explosion && frac > 0.1f;
                bool soft = frac >= Def.SoftPingThreshold;
                if (hard || soft)
                {
                    if (Animator.Ping(hard, dir, region))
                        _nextPing = Time.time + Mathf.Max(0.25f, hard ? Def.HardPingInterruptTime : Def.SoftPingInterruptTime);
                }
            }
        }

        private void RegionDamage(HaloCollisionDef.Region reg, int index, float points, HaloHit hit)
        {
            float d;
            _regionDamage.TryGetValue(index, out d);
            d += points;
            _regionDamage[index] = d;
            if (reg.DamageThreshold <= 0f || d < reg.DamageThreshold * MaxBody || _destroyedRegions.Contains(reg.Name)) return;
            DestroyRegion(reg);
            if (reg.ForcesDropWeapon && Weapon != null) DropWeapon();
            if (reg.ForcesObjectToDie) Die(hit, false, Direction(hit.Direction), 0, false, 0f);
        }

        /// <summary>Halo's hit direction for the damage animations: 0 front, 1 left, 2 right, 3 back (where the hit came from, relative to the body).</summary>
        private int Direction(Vector3 travel)
        {
            if (travel.sqrMagnitude < 1e-6f) return 0;
            Vector3 from = -travel;
            from.y = 0f;
            float f = Vector3.Dot(from.normalized, transform.forward), r = Vector3.Dot(from.normalized, transform.right);
            if (f >= 0.7071f) return 0;
            if (f <= -0.7071f) return 3;
            return r > 0f ? 2 : 1;
        }

        private Hitbox Nearest(Vector3 p)
        {
            if (Hitboxes.Count == 0) return null;
            if (p == Vector3.zero) return Hitboxes[0];
            Hitbox best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < Hitboxes.Count; i++)
            {
                Hitbox h = Hitboxes[i];
                if (h.Box == null) continue;
                float d = (h.Box.ClosestPoint(p) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = h; }
            }

            return best;
        }

        private Hitbox Of(Collider c)
        {
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box == c) return Hitboxes[i];
            return null;
        }

        private Transform BodyMarker() { return Marker("body") ?? _pelvisNode ?? transform; }

        private void TickShield(float dt)
        {
            if (_shieldFlash > 0f) _shieldFlash = Mathf.Max(0f, _shieldFlash - dt * 2f);
            TickShieldGlow();
            if (MaxShield <= 0f || Time.time < _shieldStunUntil || Shield >= 1f) return;
            HaloCollisionDef coll = Def.Coll;
            if (!_shieldRechargeSounded)
            {
                _shieldRechargeSounded = true;
                if (coll != null && coll.ShieldRechargingEffect != null) HaloCombatFx.Play(coll.ShieldRechargingEffect, Center, transform.rotation, BodyMarker());
            }

            float secs = coll != null ? coll.FullRechargeSeconds : 4f;
            Shield = Mathf.Min(1f, Shield + dt / Mathf.Max(0.25f, secs));
        }

        /// <summary>Kills the unit (script ai_kill / unit_kill): the soft death toward the front.</summary>
        public virtual void Kill()
        {
            if (!Alive) return;
            if (_frames == 0) { PlaceDead(); return; }      // killed before it ever drew: a scenario biped placed dead
            Die(new HaloHit { SourcePlayer = -1 }, false, 0, 0, false, 0f);
        }

        /// <summary>A body lying as placed (scenario bipeds flagged dead): the end of a death animation at once, no effects, no events, the weapon kept.</summary>
        public void PlaceDead()
        {
            if (!Alive) return;
            Alive = false;
            Body = 0f; Shield = 0f;
            Firing = false;
            DeadFor = -100000f;      // (placed bodies are never swept)
            _bodyDown = true;
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.isTrigger = true;
            if (Animator == null || _nodes == null) return;
            Animator.Kill(false, _rng.Next(4), 0);
            HaloUnitAnimInput inp = AnimInput();
            Animator.Step(inp, 60f);
            WritePose();
        }

        /// <summary>Kills it without a death animation or effects (unit_kill_silent): it falls still where it is.</summary>
        public void KillSilent()
        {
            if (!Alive) return;
            Alive = false;
            Body = 0f; Shield = 0f;
            Firing = false;
            if (Animator != null) Animator.Kill(false, 0, 0);
            HaloBipeds.RaiseDied(this, new HaloHit { SourcePlayer = -1 });
        }

        private void Die(HaloHit hit, bool hard, int dir, int region, bool headshot, float overkill)
        {
            if (!Alive) return;
            Alive = false;
            Body = 0f;
            Shield = 0f;
            Firing = false;
            MoveVelocity = Vector3.zero;
            var cat = HaloCombat.Catalog;
            HaloDamageDef dd = hit.Effect != null && cat != null ? cat.Damage(hit.Effect) : null;
            HaloCollisionDef coll = Def.Coll;
            // the body destroyed past its threshold (overkill as a fraction of the body), or a unit without death animations (infection forms, sentinels)
            bool destroyed = coll != null && coll.BodyDestroyedEffect != null && (coll.BodyDestroyedThreshold > 0f && overkill >= coll.BodyDestroyedThreshold || Animator == null || !HasDeath());
            if (_shieldR != null) _shieldR.enabled = false;
            if (Animator != null) Animator.Kill(hard, dir, region);
            if (coll != null && coll.BodyDepletedEffect != null) HaloCombatFx.Play(coll.BodyDepletedEffect, Center, transform.rotation, BodyMarker());
            if (Puppet || !DropsItems) ReleaseWeapon();      // (puppets: the host drops it; unit_doesnt_drop_items: it goes)
            else { DropWeapon(); DropGrenades(); }
            // the body no longer stops shots or players (triggers: Combat's casts ignore them; it still answers "dead")
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.isTrigger = true;
            if (destroyed)
            {
                HaloCombatFx.Play(coll.BodyDestroyedEffect, Center, transform.rotation, null);
                if (Built != null && Built.Root != null) Built.Root.SetActive(false);
                Destroy(gameObject, 2f);
            }

            var k = new HaloKill
            {
                Killer = hit.SourcePlayer, Victim = -1, Cause = hit.Cause, Melee = hit.Melee, Headshot = headshot,
                Grenade = dd != null && dd.Kind == HaloDamageDef.Category.Grenade, Splatter = dd != null && dd.Kind == HaloDamageDef.Category.Vehicle,
            };
            if (_deathsLogged++ < 30)
                HaloLog.Info("Characters: " + Def.Name + " died (" + (Animator != null && Animator.DeathName != null ? Animator.DeathName : "no death animation") + (headshot ? ", headshot" : "") +
                             (destroyed ? ", body destroyed" : "") + (hit.SourcePlayer >= 0 ? ", by player " + hit.SourcePlayer : "") + (hit.Cause != null ? ", " + hit.Cause.Name : "") + ")");
            if (!Puppet) HaloEvents.RaiseKilled(k);
            HaloBipeds.RaiseDied(this, hit);
        }

        private bool HasDeath()
        {
            short[] t = Animator.Graph.UnitDamage;
            if (t == null) return false;
            for (int i = 88; i < t.Length; i++) if (t[i] >= 0) return true;
            return false;
        }

        private void DropGrenades()
        {
            if (Grenades <= 0 || Def.Variant == null) return;
            if (_rng.NextDouble() < Def.DontDropGrenadesChance) return;
            var cat = HaloCombat.Catalog;
            if (cat == null) return;
            HaloTag eq = Def.VariantEquipment;
            if (eq == null && GrenadeType >= 0 && GrenadeType < cat.Grenades.Count) eq = cat.Grenades[GrenadeType].Equipment;
            if (eq == null) return;
            try { HaloPickups.PlaceAt(eq, Center + transform.right * 0.2f, _yaw); } catch (System.Exception) { }
        }

        // ------------------------------------------------------------------ IHaloHitInfo (Combat asks the shot's target)

        public int HitSlot => -1;
        public bool HitIsHead(Collider c) { Hitbox h = Of(c); return h != null && h.Head; }
        public Vector3 HitFacing => transform.forward;
        public bool HitAlive => Alive;

        /// <summary>The material at a point: the shield's while it is up there, else the hit box's (impact effects by material).</summary>
        public int HitMaterial(Vector3 point)
        {
            HaloCollisionDef coll = Def != null ? Def.Coll : null;
            if (coll == null) return 7;
            Hitbox h = Nearest(point);
            HaloCollisionDef.Material m = h != null ? coll.MaterialAt(h.Material) : (coll.Materials.Count > 0 ? coll.Materials[0] : null);
            if (Alive && Shield > 0.01f && MaxShield > 0f && (m == null || m.ShieldMultiplier > 0f)) return coll.ShieldMaterialType;
            return m != null ? m.Type : 7;
        }
    }
}
