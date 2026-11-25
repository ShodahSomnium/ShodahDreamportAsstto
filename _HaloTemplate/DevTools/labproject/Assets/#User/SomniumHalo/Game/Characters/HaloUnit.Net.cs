using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Co-op puppets (Net) and checkpoints (Mission). A puppet is a client's copy of a host unit: Net moves its transform and writes the
    /// controls; the unit only animates (its velocity from how the transform moved), plays the events Net reports (PlayFire: the firing effect
    /// and a visual-only projectile; PlayMelee / PlayThrow: the animations) and hands hits to the host (HaloNetGame.PuppetHit) instead of
    /// taking them. Checkpoints: HaloBipeds registers the part "characters" (every unit's life, vitality, place, weapon, grenades) with
    /// HaloCheckpoints on a campaign map; a revert puts units that still exist back as they were (bodies of the dead since rise again).
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>Co-op client copy: no own movement / gravity / firing / damage (Net drives it and forwards hits to the host).</summary>
        public bool Puppet;

        /// <summary>Volleys fired (each FireOnce / PlayFire) - Net sends exact shots from it.</summary>
        public int Shots => ShotsFired;

        private Vector3 _puppetLast;
        private bool _puppetHasLast;

        /// <summary>Net: the held weapon changed.</summary>
        public void SetWeapon(HaloTag weapon) { if (weapon != Weapon) Give(weapon); }

        /// <summary>Net: the host's unit fired one volley (effects, recoil, a projectile for the eyes only).</summary>
        public void PlayFire() { FireOnce(true); }
        public void PlayMelee() { if (Animator != null) Animator.Melee(); }
        public void PlayThrow() { if (Animator != null) Animator.Throw(); }

        private void TickPuppet(float dt)
        {
            Vector3 p = transform.position;
            if (_puppetHasLast && dt > 0f)
            {
                Vector3 v = (p - _puppetLast) / dt;
                if (v.sqrMagnitude > 900f) v = Vector3.zero;      // a snap
                _velocity = Vector3.Lerp(_velocity, v, Mathf.Clamp01(dt * 10f));
            }

            _puppetLast = p;
            _puppetHasLast = true;
            _yaw = transform.eulerAngles.y;
            _grounded = true;
            AimAngles(dt);
        }

        // ------------------------------------------------------------------ checkpoints

        internal sealed class Saved
        {
            public HaloUnit Unit;
            public bool Alive;
            public float Body, Shield, Yaw;
            public Vector3 Position;
            public HaloTag Weapon;
            public int Grenades, Rounds;
            public string Stance;
        }

        internal Saved Capture()
        {
            return new Saved { Unit = this, Alive = Alive, Body = Body, Shield = Shield, Yaw = _yaw, Position = transform.position, Weapon = Weapon, Grenades = Grenades, Rounds = Rounds, Stance = Stance };
        }

        internal void Restore(Saved s)
        {
            if (s.Alive && !Alive) Revive();
            if (!s.Alive && Alive) { PlaceDeadNow(); }
            Body = s.Body;
            Shield = s.Shield;
            Grenades = s.Grenades;
            Stance = s.Stance;
            if (s.Alive && s.Weapon != Weapon) Give(s.Weapon);
            Rounds = s.Rounds;
            MoveVelocity = Vector3.zero; Firing = false; Melee = false; ThrowGrenade = false; Jump = false;
            if (Seat == null) Teleport(s.Position, s.Yaw);
        }

        /// <summary>Back to life where it lies (a checkpoint from before its death): full animation, hit boxes, its weapon.</summary>
        public void Revive()
        {
            if (Alive) return;
            Alive = true;
            DeadFor = 0f;
            _bodyDown = false;
            Body = 1f;
            Shield = MaxShield > 0f ? 1f : 0f;
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.isTrigger = false;
            if (Built != null && Built.Root != null) Built.Root.SetActive(true);
            if (Animator != null) Animator.Revive();
        }

        private void PlaceDeadNow()
        {
            int f = _frames;
            _frames = 0;
            PlaceDead();
            _frames = f;
            DeadFor = 0f;
        }
    }

    internal static class HaloBipedsCheckpoints
    {
        private static bool _registered;
        private static HaloMap _for;

        /// <summary>Registers the "characters" checkpoint part once per campaign map (HaloBipeds.Spawn calls it).</summary>
        internal static void Ensure()
        {
            if (!HaloCampaign.Active) return;
            HaloMap m = HaloSession.Map;
            if (_registered && _for == m) return;
            _registered = true;
            _for = m;
            HaloCheckpoints.Register("characters", Capture, Restore);
        }

        private static object Capture()
        {
            var l = new List<HaloUnit.Saved>();
            for (int i = 0; i < HaloBipeds.All.Count; i++) if (HaloBipeds.All[i] != null && !HaloBipeds.All[i].Puppet && !HaloAi.IsActor(HaloBipeds.All[i])) l.Add(HaloBipeds.All[i].Capture());
            return l;
        }

        private static void Restore(object state)
        {
            var l = state as List<HaloUnit.Saved>;
            if (l == null) return;
            int n = 0;
            for (int i = 0; i < l.Count; i++)
            {
                HaloUnit u = l[i].Unit;
                if (u == null) continue;
                try { u.Restore(l[i]); n++; } catch (System.Exception e) { HaloLog.Warn("Characters: restoring " + u.name + " failed: " + e.Message); }
            }

            HaloLog.Info("Characters: checkpoint restored " + n + " of " + l.Count + " units");
        }
    }
}
