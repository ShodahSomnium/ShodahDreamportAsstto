using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Characters module, C] One campaign biped (marine, crewman, Keyes, grunt, elite, jackal, hunter, flood forms, sentinels, the monitor):
    /// its model (gbxmodel with its region permutations and change colour), its animation graph played by what it does (HaloUnitAnimator:
    /// idle / move blended by velocity / turn / aim / look / fire / reload / melee / throw / dive / pings / deaths ...), its weapon in hand
    /// (fires through Combat's projectiles while <see cref="Firing"/>), its body / shield vitality from its collision model and the actor
    /// variant, hit boxes on its nodes (HaloLayers.Hitbox) so shots hit it, death with the game's death animations, the dropped weapon and a
    /// body that stays a while. The AI module drives it through the control members (written every tick, read every frame); scripts reach it
    /// through HaloObjects / HaloAi. Split over HaloUnit.*.cs (Body, Move, Weapon, Damage).
    /// </summary>
    public partial class HaloUnit : MonoBehaviour, IHaloDamageable
    {
        /// <summary>The unit tag (bipd) and the actor variant (actv) it was spawned from (null for a scripted unit without AI).</summary>
        public HaloTag UnitTag, ActorVariant;
        public HaloTeam Team = HaloTeam.None;
        public string ObjectName = "";
        /// <summary>Vitality as Halo keeps it: fractions of the maximum (<see cref="MaxBody"/> / <see cref="MaxShield"/> points). Shield above 1 = overshield.</summary>
        public float Body = 1f, Shield = 0f;
        public bool Alive = true;

        // ---- control (the AI module writes these every frame; Characters plays them)
        /// <summary>Wanted movement in world space (metres / second; horizontal for walkers, 3D for flying units). The body faces
        /// <see cref="LookPoint"/> / <see cref="AimPoint"/> when set and strafes, else it faces where it goes.</summary>
        public Vector3 MoveVelocity;
        /// <summary>Where the head looks and where the weapon aims (world points; Vector3.zero = not set).</summary>
        public Vector3 LookPoint, AimPoint;
        /// <summary>Firing: the weapon fires at its rate (the actor variant's rate of fire) toward AimPoint. Crouching: the crouch seat.
        /// Melee / ThrowGrenade / Jump: one-shot requests (the unit clears them when it starts the action).</summary>
        public bool Firing, Crouching, Melee, ThrowGrenade, Jump;

        /// <summary>The seat the animation graph plays when not crouching (null = "stand"; AI may set "alert", "flee", "asleep", "flaming").</summary>
        public string Stance;
        /// <summary>The AI holds this unit still (scripts: ai_braindead / a cutscene): no movement, the animation keeps playing.</summary>
        public bool Frozen;

        public HaloCharacterDef Def { get; private set; }
        public float MaxBody => _maxBodyOverride >= 0f ? _maxBodyOverride : Def != null ? Def.MaxBody : 0f;
        public float MaxShield => _maxShieldOverride >= 0f ? _maxShieldOverride : Def != null ? Def.MaxShield : 0f;
        /// <summary>Seconds since the unit died (0 alive).</summary>
        public float DeadFor { get; private set; }
        /// <summary>The last hit that hurt it (for the AI: who / where from).</summary>
        public HaloHit LastHit;
        public float LastHitTime = -100f;

        private bool _ready;
        private System.Random _rng;
        private static int _seed = 17;

        /// <summary>The unit's eye point (for sight lines and the motion tracker).</summary>
        public virtual Vector3 Eye
        {
            get
            {
                if (_headNode != null) return _headNode.position;
                float h = Def != null ? (Crouching ? Def.CrouchingCamera : Def.StandingCamera) * HaloSpace.Unit : 1.6f;
                return transform.position + Vector3.up * (h > 0.05f ? h : 1.6f);
            }
        }

        /// <summary>The body centre (aim point for enemies shooting at it).</summary>
        public Vector3 Center
        {
            get
            {
                if (_pelvisNode != null) return _pelvisNode.position;
                return transform.position + Vector3.up * (Def != null && !Def.Flying ? Mathf.Max(0.3f, Def.StandingHeight * HaloSpace.Unit * 0.5f) : 0f);
            }
        }

        /// <summary>Builds everything from a character definition (HaloBipeds.Spawn calls it).</summary>
        public void Init(HaloCharacterDef def, HaloTeam team)
        {
            Def = def;
            UnitTag = def.Unit;
            ActorVariant = def.Variant;
            Team = team != HaloTeam.None ? team : (HaloTeam)Mathf.Clamp(def.Team, 0, 5);
            _rng = new System.Random(_seed += 7919);
            Body = 1f;
            Shield = def.MaxShield > 0f ? 1f : 0f;
            Alive = true;
            _yaw = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            BuildBody();
            InitWeapon();
            InitMove();
            _ready = true;
        }

        /// <summary>Names the unit for scripts (HaloObjects) - K / AI call it for named scenario bipeds.</summary>
        public void SetName(string name)
        {
            if (!string.IsNullOrEmpty(ObjectName)) HaloObjects.Unregister(ObjectName);
            ObjectName = name ?? "";
            if (!string.IsNullOrEmpty(ObjectName)) HaloObjects.Register(ObjectName, gameObject);
        }

        private void Update()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (dt > 0.1f) dt = 0.1f;
            _frames++;
            try
            {
                if (Puppet) { TickPuppet(dt); if (!Alive) DeadFor += dt; }
                else if (Alive)
                {
                    TickShield(dt);
                    TickMove(dt);
                    TickWeapon(dt);
                }
                else
                {
                    DeadFor += dt;
                    TickDeadMove(dt);
                }

                TickAnimation(dt);
            }
            catch (System.Exception e)
            {
                if (!_tickFailed) { _tickFailed = true; HaloLog.Error("Characters: " + name + " tick failed: " + e); }
            }
        }

        private bool _tickFailed;
        private int _frames;

        private void OnDestroy()
        {
            HaloBipeds.Forget(this);
            if (!string.IsNullOrEmpty(ObjectName) && HaloObjects.Find(ObjectName) == gameObject) HaloObjects.Unregister(ObjectName);
            ReleaseWeapon();
        }

        private float Rand(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }
    }
}
