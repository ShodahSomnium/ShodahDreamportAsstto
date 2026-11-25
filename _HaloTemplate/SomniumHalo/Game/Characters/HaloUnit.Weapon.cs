using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The weapon in the unit's hand: the weapon's gbxmodel on the hand marker its animation class names ("right hand", "right hand elite",
    /// "left hand jackal"), Halo's own weapon part animations on it (Desk's HaloWeaponAnim: fire, reload), firing while <see cref="Firing"/>
    /// at the actor variant's rate of fire (the weapon's trigger rate without one) through Combat's projectiles (HaloCombat.FireProjectile:
    /// damage, impacts, material responses) from the "primary trigger" marker toward <see cref="AimPoint"/> within the variant's projectile
    /// error, the trigger's firing effect at the muzzle, the magazine (reload-1 when empty), melee (the unit's / weapon's melee damage at the
    /// animation's key frame), grenades thrown at the throw-grenade key frame (the globals' grenade of the variant's grenade type, along a lob
    /// to the aim point), and the weapon dropped where the unit dies (Combat's pickups).
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>The shooter id of AI shots (Combat / Net: -2 = "AI"; players are 0 and up, -1 the world).</summary>
        public const int AiShooter = -2;

        /// <summary>The weapon in hand (weap), null = unarmed.</summary>
        public HaloTag Weapon { get; private set; }
        /// <summary>The weapon's label ("ar", "pr" ...) - picks the animation class.</summary>
        public string WeaponLabel { get; private set; }
        /// <summary>Where shots leave the weapon (the weapon model's "primary trigger" marker; the unit's own for built-in weapons). Barrel = its +X (Unity right).</summary>
        public Transform Muzzle { get; private set; }
        /// <summary>Rounds in the magazine (-1 = none: heat / battery weapons fire on).</summary>
        public int Rounds = -1;
        public int Grenades;
        public int GrenadeType;
        /// <summary>Shots fired since spawn (labs, AI statistics).</summary>
        public int ShotsFired { get; private set; }

        private HaloModelBuilder.Built _weaponBuilt;
        private HaloWeaponAnim _weaponAnim;
        private HaloWeaponDef _wdef;
        private float _nextShot;
        private int _burstShot;
        private bool _wasFiring;

        private void InitWeapon()
        {
            GrenadeType = Def.VariantGrenadeType >= 0 && Def.Variant != null ? Def.VariantGrenadeType : Def.GrenadeType;
            Grenades = Def.Variant != null ? Rnd(Def.GrenadeCountMin, Def.GrenadeCountMax) : Mathf.Max(0, Def.GrenadeCount);
            Give(Def.Weapon);
        }

        private int Rnd(int a, int b) { if (b < a) b = a; return a + _rng.Next(b - a + 1); }

        /// <summary>Puts a weapon in the unit's hand (null = unarmed): its model on the class's hand marker, a full magazine.</summary>
        public void Give(HaloTag weapon)
        {
            ReleaseWeapon();
            Weapon = weapon != null && weapon.Is("weap") ? weapon : null;
            WeaponLabel = Weapon != null ? Weapon.Root.Str32(780) : null;
            var cat = HaloCombat.Catalog;
            _wdef = Weapon != null && cat != null ? cat.Weapon(Weapon) : null;
            Rounds = _wdef != null && _wdef.HasMagazine ? _wdef.PrimaryMagazine.RoundsLoadedMaximum : -1;
            if (Weapon != null)
            {
                HaloTag model = Weapon.Root.Ref(40);
                if (model != null)
                {
                    var holder = new GameObject("weapon");
                    holder.transform.SetParent(transform, false);
                    _weaponBuilt = HaloModelBuilder.Build(model, holder.transform, new HaloModelBuilder.Options());
                    if (_weaponBuilt == null) Destroy(holder);
                }
            }

            if (_weaponBuilt != null) AttachWeaponModel();
            else Muzzle = Marker("primary trigger");      // built-in weapons (sentinel beam, hunter cannon): the unit's own marker
            if (_weaponBuilt != null && Weapon != null)
            {
                try { _weaponAnim = HaloWeaponAnim.Attach(_weaponBuilt, Weapon); } catch (System.Exception) { _weaponAnim = null; }
            }

            _heat = 0f; _overheated = false; _charging = false;
            // the weapon tag's attachments (idle hum, glow, lights) and its ready effect when it is given after the spawn
            if (Weapon != null && _weaponBuilt != null) { try { _weaponFx = HaloFx.Attach(Weapon, _weaponBuilt.Root.transform); } catch (System.Exception) { _weaponFx = null; } }
            if (_frames > 0) PlayReady();
        }

        /// <summary>The weapon model onto the hand marker the animation class names (the class is known after the first animation step).</summary>
        private void AttachWeaponModel()
        {
            if (_weaponBuilt == null || _weaponBuilt.Root == null) return;
            string hand = null;
            HaloAnimGraph g = Animator != null ? Animator.Graph : null;
            if (g != null)
            {
                HaloAnimGraph.Seat seat = g.FindSeat("stand") ?? (g.Seats.Count > 0 ? g.Seats[0] : null);
                HaloAnimGraph.WeaponType t;
                HaloAnimGraph.WeaponClass c = seat != null ? g.ClassFor(seat, WeaponLabel, out t) : null;
                if (c != null && !string.IsNullOrEmpty(c.HandMarker)) hand = c.HandMarker;
            }

            Transform m = (hand != null ? Marker(hand) : null) ?? Marker("right hand") ?? Marker("right hand elite") ?? (_nodes != null ? Built.Node("bip01 r hand") : null);
            Transform holder = _weaponBuilt.Root.transform.parent;
            holder.SetParent(m != null ? m : transform, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            Muzzle = _weaponBuilt.Marker("primary trigger") ?? _weaponBuilt.Root.transform;
        }

        private void ReleaseWeapon()
        {
            if (_weaponBuilt != null && _weaponBuilt.Root != null)
            {
                Transform holder = _weaponBuilt.Root.transform.parent;
                Destroy(holder != null && holder != transform ? holder.gameObject : _weaponBuilt.Root);
            }

            if (_weaponFx != null) { try { HaloFx.Detach(_weaponFx); } catch (System.Exception) { } _weaponFx = null; }
            _weaponBuilt = null;
            _weaponAnim = null;
            Muzzle = null;
        }

        // ------------------------------------------------------------------ every frame

        private void TickWeapon(float dt)
        {
            HaloUnitAnimator a = Animator;
            HaloUnitAnimator.ActionKind act = a != null ? a.Action : HaloUnitAnimator.ActionKind.None;
            bool busy = act == HaloUnitAnimator.ActionKind.Reload || act == HaloUnitAnimator.ActionKind.Melee || act == HaloUnitAnimator.ActionKind.Throw || act == HaloUnitAnimator.ActionKind.Dive;
            if (ThrowGrenade)
            {
                ThrowGrenade = false;
                if (!busy && Grenades > 0 && a != null && a.Throw()) busy = true;
            }

            if (Melee)
            {
                Melee = false;
                if (!busy && a != null && a.Melee()) busy = true;
                else if (!busy && a == null) MeleeHit();
            }

            TickHeat(dt);
            if (_charging && TickCharge(dt)) return;
            if (!Firing) { _wasFiring = false; _burstShot = 0; return; }
            if (busy || Seat != null && Weapon == null) return;
            if (Weapon == null && Muzzle == null) return;
            if (_overheated) return;
            if (Rounds == 0)
            {
                if (a != null && a.Reload())
                {
                    if (_weaponAnim != null) { try { _weaponAnim.Play("reload-empty"); } catch (System.Exception) { } }
                    HaloWeaponDef.Magazine mag = _wdef != null ? _wdef.PrimaryMagazine : null;
                    if (mag != null && mag.ReloadingEffect != null) { SfxReload++; HaloCombatFx.OnModel(mag.ReloadingEffect, WeaponRoot, false); }
                }
                Rounds = _wdef != null && _wdef.HasMagazine ? _wdef.PrimaryMagazine.RoundsLoadedMaximum : -1;      // the AI's reserve never runs out (Halo's AI: infinite ammo)
                return;
            }

            if (!_wasFiring && StartCharge()) { _wasFiring = true; return; }
            if (!_wasFiring) { _wasFiring = true; if (Time.time > _nextShot) _nextShot = Time.time; }
            if (Time.time < _nextShot) return;
            FireOnce();
            float rate = Def.RateOfFire > 0.01f ? Def.RateOfFire : (_wdef != null && _wdef.PrimaryTrigger != null ? Mathf.Max(_wdef.PrimaryTrigger.RateMax, _wdef.PrimaryTrigger.RateMin) : 2f);
            if (rate <= 0.01f) rate = 1f;
            _nextShot += 1f / rate;
            if (_nextShot < Time.time - 0.2f) _nextShot = Time.time;
        }

        /// <summary>One shot now (the AI may call it for single aimed shots): projectiles from the muzzle toward the aim, the firing effect, the recoil overlay.</summary>
        public void FireOnce() { FireOnce(false); }

        /// <summary>One volley; <paramref name="visualOnly"/> = a co-op puppet replaying the host's shot (no damage).</summary>
        public void FireOnce(bool visualOnly)
        {
            if (!Alive) return;
            Transform mz = Muzzle != null ? Muzzle : transform;
            Vector3 from = mz.position;
            Vector3 dir = AimPoint != Vector3.zero ? (AimPoint - from) : (Muzzle != null ? mz.right : transform.forward);
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            dir.Normalize();
            ShotsFired++;
            _burstShot++;
            if (Rounds > 0) Rounds--;
            if (Animator != null) Animator.Fire();
            if (_weaponAnim != null) { try { _weaponAnim.Play("fire"); } catch (System.Exception) { } }
            var cat = HaloCombat.Catalog;
            HaloWeaponDef.Trigger t = _wdef != null && _wdef.Triggers.Count > _trigger ? _wdef.Triggers[_trigger] : null;
            if (t == null || cat == null) return;
            // the trigger's own firing effect (the weapon's fire sound, flash, shell) and, as the magazine runs dry, its empty click
            HaloWeaponDef.FiringEffect fe = t.EffectFor(_burstShot, null);
            if (fe != null && fe.Firing != null) { SfxFire++; HaloCombatFx.Play(fe.Firing, from, Quaternion.LookRotation(dir), mz); }
            if (Rounds == 0 && fe != null && fe.Empty != null) { SfxEmpty++; HaloCombatFx.OnModel(fe.Empty, WeaponRoot, false); }
            // heat (plasma weapons): past the overheated threshold the weapon vents with its overheated effect until it cools
            if (t.HeatPerRound > 0f && !visualOnly)
            {
                _heat = Mathf.Min(1f, _heat + t.HeatPerRound);
                if (_wdef.OverheatedThreshold > 0f && _heat >= _wdef.OverheatedThreshold && !_overheated)
                {
                    _overheated = true;
                    if (_wdef.OverheatedEffect != null) { SfxOverheat++; HaloCombatFx.OnModel(_wdef.OverheatedEffect, WeaponRoot, false); }
                    if (_weaponAnim != null) { try { _weaponAnim.Play("overheating"); _weaponAnim.SetState("overheated", true); } catch (System.Exception) { } }
                }
            }
            int n = Mathf.Max(1, t.ProjectilesPerShot);
            float err = Def.ProjectileError + (n > 1 ? t.ErrorAngleMax : t.ErrorAngleMin + t.MinimumError);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = HaloHandling.Cone(dir, err);
                try { HaloCombat.FireProjectile(t.Projectile, from, Vector3.zero, AiShooter, Weapon, transform, d, visualOnly); }
                catch (System.Exception e) { if (!_fireFailed) { _fireFailed = true; HaloLog.Warn("Characters: " + Def.Name + " firing failed: " + e.Message); } }
            }

            HaloBipeds.RaiseFired(this, Weapon, from, dir);
        }

        private bool _fireFailed;

        // ------------------------------------------------------------------ the weapon's other sounds: ready, heat, overcharge

        /// <summary>Counts of the weapon sounds AI units played (labs / Player.log).</summary>
        public static int SfxFire, SfxReload, SfxEmpty, SfxOverheat, SfxReady, SfxCharge, SfxBodyImpact;

        private HaloFx.Attachments _weaponFx;
        private float _heat, _chargeT;
        private bool _overheated, _charging;
        private int _trigger;

        private Transform WeaponRoot => _weaponBuilt != null && _weaponBuilt.Root != null ? _weaponBuilt.Root.transform : (Muzzle != null ? Muzzle : transform);

        /// <summary>The weapon tag's ready effect (drawing it).</summary>
        private void PlayReady()
        {
            if (_wdef == null || _wdef.ReadyEffect == null || !Alive) return;
            SfxReady++;
            HaloCombatFx.OnModel(_wdef.ReadyEffect, WeaponRoot, false);
        }

        /// <summary>Heat bleeds off at the weapon's heat loss rate (a second) while not firing or while venting; vented below the recovery threshold.</summary>
        private void TickHeat(float dt)
        {
            if (_wdef == null || _heat <= 0f) return;
            if (!Firing || _overheated)
            {
                float loss = _wdef.HeatLossRate > 0f ? _wdef.HeatLossRate : 0.25f;
                _heat = Mathf.Max(0f, _heat - loss * dt);
            }

            if (_overheated && _heat <= _wdef.HeatRecoveryThreshold)
            {
                _overheated = false;
                if (_weaponAnim != null) { try { _weaponAnim.SetState("overheated", false); _weaponAnim.Play("overheat-exit"); } catch (System.Exception) { } }
            }
        }

        /// <summary>
        /// The actor variant's special fire "overcharge" (plasma pistol grunts / jackals): at a burst's start, by the variant's special fire
        /// chance, the trigger charges (its charging effect: the charge hum) for its charging time, then the weapon's second trigger fires the
        /// charged bolt. False when the burst fires normally.
        /// </summary>
        private bool StartCharge()
        {
            if (Def.SpecialFireMode != 1 || _wdef == null || _wdef.Triggers.Count < 2 || _overheated) return false;
            HaloWeaponDef.Trigger t = _wdef.PrimaryTrigger;
            if (t == null || t.ChargingTime <= 0f || _rng.NextDouble() >= Def.SpecialFireChance) return false;
            _charging = true;
            _chargeT = 0f;
            if (t.ChargingEffect != null) { SfxCharge++; HaloCombatFx.OnModel(t.ChargingEffect, WeaponRoot, false); }
            return true;
        }

        /// <summary>Charging: true while it holds the shot; the charged bolt leaves when charged (or when the burst ends after charging).</summary>
        private bool TickCharge(float dt)
        {
            HaloWeaponDef.Trigger t = _wdef != null ? _wdef.PrimaryTrigger : null;
            if (t == null) { _charging = false; return false; }
            _chargeT += dt;
            if (_chargeT < t.ChargingTime && Firing) return true;
            _charging = false;
            if (_chargeT >= t.ChargingTime * 0.5f)
            {
                _trigger = 1;
                FireOnce(false);
                _trigger = 0;
                _nextShot = Time.time + 1f;
            }

            return true;
        }

        // ------------------------------------------------------------------ the animation's key frames

        private void ActionKeyFrame(HaloUnitAnimator.ActionKind kind)
        {
            if (!Alive) return;
            if (kind == HaloUnitAnimator.ActionKind.Throw) ReleaseGrenade();
            else if (kind == HaloUnitAnimator.ActionKind.Melee) MeleeHit();
            else if (kind == HaloUnitAnimator.ActionKind.Reload && _weaponAnim != null) { }
        }

        /// <summary>The grenade leaves the hand: the globals' grenade of the unit's type, lobbed to land on the aim point.</summary>
        private void ReleaseGrenade()
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || Grenades <= 0) return;
            int type = Mathf.Clamp(GrenadeType, 0, Mathf.Max(0, cat.Grenades.Count - 1));
            if (type >= cat.Grenades.Count) return;
            HaloTag proj = cat.Grenades[type].Projectile;
            if (proj == null) return;
            Grenades--;
            Transform hand = Marker("left hand") ?? Marker("right hand") ?? Marker("left hand elite") ?? transform;
            Vector3 from = hand.position;
            float speed = (Def.VariantGrenadeVelocity > 0.01f ? Def.VariantGrenadeVelocity : Def.GrenadeVelocity > 0.01f ? Def.GrenadeVelocity : 6f) * HaloSpace.Unit;      // world units a second
            Vector3 target = AimPoint != Vector3.zero ? AimPoint : transform.position + transform.forward * 10f;
            Vector3 vel = Lob(from, target, speed);
            HaloCombat.FireProjectile(proj, from, vel, AiShooter, proj, transform, vel.normalized);
            HaloLog.Info("Characters: " + Def.Name + " threw a " + proj.Name + " (" + speed.ToString("0.0") + " m/s)");
        }

        /// <summary>A launch velocity of a given speed that lands on the target (the flatter of the two arcs; 45 degrees when out of reach).</summary>
        private static Vector3 Lob(Vector3 from, Vector3 to, float speed)
        {
            Vector3 d = to - from;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float x = flat.magnitude, y = d.y, g = Gravity, v2 = speed * speed;
            if (x < 0.1f) return Vector3.up * speed;
            float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
            float ang = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x)) : Mathf.PI / 4f;
            Vector3 h = flat / x;
            return (h * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang)) * speed;
        }

        /// <summary>The melee blow: the unit's melee damage (else the weapon's) to what is in front within reach (hit boxes and the local player).</summary>
        private void MeleeHit()
        {
            var cat = HaloCombat.Catalog;
            HaloTag jpt = Def.MeleeDamage ?? (_wdef != null ? _wdef.MeleeDamage : null);
            HaloDamageDef dd = jpt != null && cat != null ? cat.Damage(jpt) : null;
            if (dd == null) return;
            float reach = Mathf.Max(1.2f, (Def.MeleeRange > 0.01f ? Def.MeleeRange : 0.4f) * HaloSpace.Unit + _radius);
            Vector3 c = Center + transform.forward * (reach * 0.5f);
            float amount = dd.DirectAmount(_rng, 0f, 0f, 0f);
            int n = Physics.OverlapSphereNonAlloc(c, reach * 0.6f, Overlap, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide);
            IHaloDamageable done = null;
            for (int i = 0; i < n; i++)
            {
                Collider col = Overlap[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                IHaloDamageable v = col.GetComponentInParent<IHaloDamageable>();
                if (v == null || v == done) continue;
                done = v;
                HaloProjectiles.Deliver(v, new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = AiShooter, Cause = jpt, Point = col.ClosestPoint(c), Direction = transform.forward, Melee = true });
                break;
            }

            // the local player (no hit box of its own)
            Vector3 me = HaloCombat.LocalChest;
            if (HaloLocal.Alive && me != Vector3.zero && (me - c).magnitude < reach * 0.8f && HaloAllegiance.AreEnemies(Team, HaloTeam.Player))
                HaloCombat.TakeHit(new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = AiShooter, Cause = jpt, Point = me, Direction = transform.forward, Melee = true });
        }

        private static readonly Collider[] Overlap = new Collider[32];

        // ------------------------------------------------------------------ death: the weapon falls

        private void DropWeapon()
        {
            if (Weapon == null || _wdef == null) { ReleaseWeapon(); return; }
            Vector3 at = Muzzle != null && _weaponBuilt != null ? _weaponBuilt.Root.transform.position : Center;
            Quaternion rot = _weaponBuilt != null && _weaponBuilt.Root != null ? _weaponBuilt.Root.transform.rotation : transform.rotation;
            HaloTag w = Weapon;
            ReleaseWeapon();
            Weapon = null;
            WeaponLabel = null;
            try
            {
                HaloGun g = HaloGun.Create(_wdef, HaloPickups.Root);
                if (g == null) return;
                g.SetScale(1f);
                g.transform.position = at;
                g.transform.rotation = rot * Quaternion.Euler(0f, 90f, 0f);
                HaloPickups.Item it = HaloPickups.DropWeapon(g, _velocity + Vector3.up * 1f, false, at);
                if (it != null) HaloLog.Info("Characters: " + Def.Name + " dropped its " + w.Name);
            }
            catch (System.Exception e) { HaloLog.Warn("Characters: dropping " + w.Name + " failed: " + e.Message); }
        }
    }
}
