using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Everything about one campaign character the Characters module plays, read from its tags: the unit (bipd: model, animation graph,
    /// collision model, default team, biped flags (flying, spherical ...), turning, jump, heights, ping / death thresholds, grenades, the
    /// melee damage, footsteps, the unit's weapons, its object change colours) and, when spawned from an actor variant (actv), the variant's
    /// weapon, vitality overrides (body / shield vitality, absolute points: 0 = the collision model's), firing numbers for the AI (rate of
    /// fire, projectile error, burst), grenades, the dropped weapon's ammo and its change colours. Layouts: DevTools/reference/layouts.txt
    /// (Biped : Unit : Object, ActorVariant). Speeds are as the tags keep them (world units, per second / per tick as noted). Unity-free.
    /// </summary>
    public sealed class HaloCharacterDef
    {
        public struct ColorRange { public float R0, G0, B0, R1, G1, B1; }

        public sealed class ChangeColor
        {
            public ColorRange Range;
            public readonly List<float> Weights = new List<float>();
            public readonly List<ColorRange> Permutations = new List<ColorRange>();
        }

        public readonly HaloTag Unit, Variant;
        public readonly HaloTag Model, Graph, Collision, ModifierShader, CreationEffect, MeleeDamage, Footsteps, SpawnedActor;
        public readonly HaloTag Weapon;                 // the variant's weapon, else the unit's first weapon (null = unarmed)
        public readonly List<HaloTag> UnitWeapons = new List<HaloTag>();
        /// <summary>The unit's default team (UnitDefaultTeam: 0 none, 1 player, 2 human, 3 covenant, 4 flood, 5 sentinel; = HaloTeam).</summary>
        public readonly int Team;
        public readonly uint UnitFlags, BipedFlags;
        public readonly float MovingTurningSpeed, StationaryTurningThreshold, AngularVelocityMaximum, AngularAccelerationMaximum;
        public readonly float MaxVelocity, MaxSidestepVelocity, Acceleration, Deceleration, CrouchVelocityModifier, JumpVelocity;
        public readonly float StandingHeight, CrouchingHeight, Radius, StandingCamera, CrouchingCamera, CrouchTransitionTime;
        public readonly float SoftPingThreshold, SoftPingInterruptTime, HardPingThreshold, HardPingInterruptTime, HardDeathThreshold;
        public readonly float FeignDeathThreshold, FeignDeathTime, FeignDeathChance, EvadeDistance, DiveDistance;
        public readonly float AimingVelocityMaximum, LookingVelocityMaximum;
        public readonly float GrenadeVelocity; public readonly int GrenadeType, GrenadeCount;
        public readonly float MinSoftLandingVelocity, MinHardLandingVelocity, MaxHardLandingVelocity, DeathHardLandingVelocity;
        public readonly int PelvisNode, HeadNode;
        public readonly List<ChangeColor> ChangeColors = new List<ChangeColor>();
        public readonly List<string> SeatLabels = new List<string>();
        public readonly List<string> SeatMarkers = new List<string>();

        // ---- the actor variant (zero / null without one)
        public readonly float BodyVitality, ShieldVitality;
        public readonly uint VariantFlags;
        public readonly int MovementType;
        public readonly float RateOfFire, ProjectileError, WeaponDamageModifier, MaximumFiringDistance;
        public readonly float BurstDurationMin, BurstDurationMax, BurstSeparationMin, BurstSeparationMax;
        public readonly float MeleeRange, MeleeAbortRange;
        /// <summary>actv special fire mode (0 none, 1 overcharge, 2 secondary trigger) and its chance per burst.</summary>
        public readonly int SpecialFireMode;
        public readonly float SpecialFireChance;
        public readonly int VariantGrenadeType;
        public readonly float VariantGrenadeVelocity, GrenadeChance;
        public readonly int GrenadeCountMin, GrenadeCountMax;
        public readonly float DontDropGrenadesChance, DropLoadedMin, DropLoadedMax;
        public readonly int DropAmmoMin, DropAmmoMax, ForcedShaderPermutation;
        public readonly HaloTag VariantEquipment, MajorVariant;
        public readonly List<ColorRange> VariantColors = new List<ColorRange>();

        public HaloCollisionDef Coll;

        public bool Flying => (BipedFlags & 4) != 0;
        public bool Spherical => (BipedFlags & 16) != 0;
        public bool PillCenteredAtOrigin => (BipedFlags & 8) != 0;
        public bool PassesThroughBipeds => (BipedFlags & 32) != 0;
        public bool ImmuneToFallingDamage => (BipedFlags & 128) != 0;
        public bool HasNoDyingAirborne => (BipedFlags & 1024) != 0;
        public bool TurnsWithoutAnimating => (BipedFlags & 1) != 0;

        /// <summary>The body vitality points this character has (the variant's when it sets one, else the collision model's).</summary>
        public float MaxBody => BodyVitality > 0f ? BodyVitality : Coll != null ? Coll.MaximumBody : 0f;
        /// <summary>The shield vitality points (variant's override, else the collision model's; 0 = no shield).</summary>
        public float MaxShield => ShieldVitality > 0f ? ShieldVitality : Coll != null ? Coll.MaximumShield : 0f;

        public string Name => (Variant ?? Unit).Name;

        /// <summary>A character from a unit (bipd) or actor variant (actv) tag; null for anything else (or a variant without a unit).</summary>
        public static HaloCharacterDef From(HaloTag unitOrVariant)
        {
            if (unitOrVariant == null) return null;
            if (unitOrVariant.Is("actv"))
            {
                HaloTag u = unitOrVariant.Root.Ref(20);
                if (u == null || !u.Is("bipd")) return null;
                return new HaloCharacterDef(u, unitOrVariant);
            }

            return unitOrVariant.Is("bipd") ? new HaloCharacterDef(unitOrVariant, null) : null;
        }

        private HaloCharacterDef(HaloTag unit, HaloTag variant)
        {
            Unit = unit;
            Variant = variant;
            TagReader r = unit.Root;
            Model = r.Ref(40);
            Graph = r.Ref(56);
            Collision = r.Ref(112);
            ModifierShader = r.Ref(144);
            CreationEffect = r.Ref(160);
            foreach (TagReader c in r.Block(356, 44))
            {
                var cc = new ChangeColor { Range = Range(c, 8) };
                foreach (TagReader p in c.Block(32, 28)) { cc.Weights.Add(p.F32(0)); cc.Permutations.Add(Range(p, 4)); }
                ChangeColors.Add(cc);
            }

            UnitFlags = r.U32(380);
            int team = r.U16(384);
            Team = team >= 0 && team <= 9 ? team : 0;
            SoftPingThreshold = r.F32(536); SoftPingInterruptTime = r.F32(540);
            HardPingThreshold = r.F32(544); HardPingInterruptTime = r.F32(548);
            HardDeathThreshold = r.F32(552);
            FeignDeathThreshold = r.F32(556); FeignDeathTime = r.F32(560);
            EvadeDistance = r.F32(564); DiveDistance = r.F32(568);
            FeignDeathChance = r.F32(580);
            SpawnedActor = r.Ref(588);
            AimingVelocityMaximum = r.F32(612);
            LookingVelocityMaximum = r.F32(624);
            MeleeDamage = r.Ref(648);
            GrenadeVelocity = r.F32(704);
            GrenadeType = r.U16(708);
            GrenadeCount = r.I16(710);
            foreach (TagReader w in r.Block(728, 36)) { HaloTag t = w.Ref(0); if (t != null) UnitWeapons.Add(t); }
            foreach (TagReader s in r.Block(740, 284)) { SeatLabels.Add(s.Str32(4)); SeatMarkers.Add(s.Str32(36)); }
            MovingTurningSpeed = r.F32(752);
            BipedFlags = r.U32(756);
            StationaryTurningThreshold = r.F32(760);
            MaxVelocity = r.F32(820); MaxSidestepVelocity = r.F32(824);
            Acceleration = r.F32(828); Deceleration = r.F32(832);
            AngularVelocityMaximum = r.F32(836); AngularAccelerationMaximum = r.F32(840);
            CrouchVelocityModifier = r.F32(844);
            Footsteps = r.Ref(908);
            JumpVelocity = r.F32(948);
            MinSoftLandingVelocity = r.F32(988); MinHardLandingVelocity = r.F32(992); MaxHardLandingVelocity = r.F32(996); DeathHardLandingVelocity = r.F32(1000);
            StandingCamera = r.F32(1024); CrouchingCamera = r.F32(1028); CrouchTransitionTime = r.F32(1032);
            StandingHeight = r.F32(1060); CrouchingHeight = r.F32(1064); Radius = r.F32(1068);
            PelvisNode = r.I16(1252); HeadNode = r.I16(1254);
            Weapon = UnitWeapons.Count > 0 ? UnitWeapons[0] : null;
            VariantGrenadeType = -1;
            ForcedShaderPermutation = -1;

            if (variant != null)
            {
                TagReader v = variant.Root;
                VariantFlags = v.U32(0);
                MajorVariant = v.Ref(36);
                MovementType = v.U16(76);
                HaloTag w = v.Ref(100);
                if (w != null) Weapon = w;
                MaximumFiringDistance = v.F32(116);
                RateOfFire = v.F32(120);
                ProjectileError = v.F32(124);
                WeaponDamageModifier = v.F32(196);
                BurstDurationMin = v.F32(224); BurstDurationMax = v.F32(228);
                BurstSeparationMin = v.F32(232); BurstSeparationMax = v.F32(236);
                MeleeRange = v.F32(352); MeleeAbortRange = v.F32(356);
                SpecialFireMode = v.U16(340); SpecialFireChance = v.F32(344);
                VariantGrenadeType = v.U16(384);
                VariantGrenadeVelocity = v.F32(400);
                GrenadeChance = v.F32(416);
                VariantEquipment = v.Ref(448);
                GrenadeCountMin = v.I16(464); GrenadeCountMax = v.I16(466);
                DontDropGrenadesChance = v.F32(468);
                DropLoadedMin = v.F32(472); DropLoadedMax = v.F32(476);
                DropAmmoMin = v.I16(480); DropAmmoMax = v.I16(482);
                BodyVitality = v.F32(512);
                ShieldVitality = v.F32(516);
                ForcedShaderPermutation = v.I16(524);
                foreach (TagReader c in v.Block(556, 32)) VariantColors.Add(Range(c, 0));
            }

            if (Collision != null) Coll = new HaloCollisionDef(Collision);
        }

        private static ColorRange Range(TagReader r, int at)
        {
            return new ColorRange { R0 = r.F32(at), G0 = r.F32(at + 4), B0 = r.F32(at + 8), R1 = r.F32(at + 12), G1 = r.F32(at + 16), B1 = r.F32(at + 20) };
        }

        /// <summary>The change colour A (armour / rank colour) for a random draw: the variant's range when it has one, else the unit's (a weighted
        /// permutation when it lists some). Values 0..1 (r, g, b); false when neither has one.</summary>
        public bool ColorA(Random rng, out float r, out float g, out float b)
        {
            r = g = b = 1f;
            ColorRange c;
            if (VariantColors.Count > 0) c = VariantColors[0];
            else if (ChangeColors.Count > 0)
            {
                ChangeColor cc = ChangeColors[0];
                c = cc.Range;
                if (cc.Permutations.Count > 0)
                {
                    float sum = 0f;
                    for (int i = 0; i < cc.Weights.Count; i++) sum += Math.Max(0f, cc.Weights[i]);
                    float x = (float)rng.NextDouble() * sum;
                    c = cc.Permutations[cc.Permutations.Count - 1];
                    for (int i = 0; i < cc.Weights.Count; i++) { x -= Math.Max(0f, cc.Weights[i]); if (x <= 0f) { c = cc.Permutations[i]; break; } }
                }
            }
            else return false;

            float u = (float)rng.NextDouble();
            r = c.R0 + (c.R1 - c.R0) * u; g = c.G0 + (c.G1 - c.G0) * u; b = c.B0 + (c.B1 - c.B0) * u;
            return true;
        }

        public override string ToString() { return Name; }
    }
}
