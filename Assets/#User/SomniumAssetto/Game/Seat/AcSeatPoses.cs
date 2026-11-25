using System;
using System.Collections.Generic;
using SomniumSpace.Bridge.Player;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Seated avatars' hands on the steering wheel and the own head hidden in the cockpit (LateUpdate 9997, after the client's Animator):
    /// - monitor drivers (the own avatar on a monitor, and every remote desktop driver drawn here): both wrists IK'd to the car's own driver
    ///   wrist points (driver_base_pos.knh RIG_HAND_L / RIG_HAND_R, the 9-and-3 grip AC's driver uses: 79 / 84 knh files put them on the rim)
    ///   turning WITH the steering wheel up to +-HandFollowDeg (the points ride in STEER_HR's rest frame turned by the wheel angle), hands in a rim grip, fingers
    ///   curled (Halo HaloSeatPoses / HaloPuppetPoser idea; <see cref="AcSeatedArms"/>). Copied onto the mirror / shadow copies (AcAvatarCopies).
    ///   VR avatars keep their tracked hands, gripping or not (decision, S/NOTES.md: VRIK places them at 9998 from the controllers, and a hand
    ///   drawn on the rim while the real one is elsewhere would break the player's own hand-eye match; others see the hands where they are).
    /// - the own head (main copy only, this screen only): shrunk while the view is inside the car (cockpit / dash camera, or VR when the
    ///   client has not hidden it already) through <see cref="AcHeadHide"/>, given back after every camera drew.
    /// </summary>
    public static class AcSeatPoses
    {
        private const float BlendIn = 0.3f;      // ours
        private sealed class Posed
        {
            public int Key;                     // -1 own, else photon id
            public Animator Anim;
            public AcSeatedArms Arms = new AcSeatedArms();
            public AcAvatarCopies Copies;
            public float Weight, NextSearch, NextCopies;
            public AcCarRig Car;
            public AcSeatGeometry Geo;
            public bool HaveHands;
            public Vector3 LocalL, LocalR;      // wrist points in the steering wheel's frame (at angle 0)
            public float WheelDeg, LastWrapped; // the wheel's angle unwrapped across frames (STEER_LOCK reaches 585 deg)
            public bool HaveAngle;
            public bool Seen;
            public bool Said;
        }

        private static readonly Dictionary<int, Posed> All = new Dictionary<int, Posed>();
        private static readonly List<int> Gone = new List<int>();
        private static bool _ownHeadHidden, _saidHead;

        /// <summary>The own head is hidden this frame (the entry report).</summary>
        public static bool OwnHeadHidden { get { return _ownHeadHidden || AcHeadHide.Hidden; } }

        /// <summary>Lab: an Animator to pose as the own avatar (the editor has no client avatar).</summary>
        public static Animator LabAvatar;

        internal static void Late()
        {
            foreach (KeyValuePair<int, Posed> kv in All) kv.Value.Seen = false;
            _ownHeadHidden = false;

            if (AcSeat.Seated && AcSeat.SeatedCar != null)
            {
                Posed own = Get(-1);
                own.Seen = true;
                Animator a = OwnAvatar(own);
                if (a != null)
                {
                    if (!AcSeat.Vr && AcSeat.IsDriver) Pose(own, a, AcSeat.SeatedCar);
                    HeadHide(a);
                }
            }

            int n = AcRiders.Count;
            for (int i = 0; i < n; i++)
            {
                ISomniumPlayer player;
                AcCarRig car;
                AcSeatKind seat;
                int id;
                if (!AcRiders.Get(i, out player, out car, out seat, out id) || seat != AcSeatKind.Driver) continue;
                AcNetPlayer np = AcRiders.Find(id);
                if (np == null || np.Vr) continue;
                Posed p = Get(id);
                p.Seen = true;
                Animator a = RemoteAvatar(p, player);
                if (a != null) Pose(p, a, car);
            }

            Gone.Clear();
            foreach (KeyValuePair<int, Posed> kv in All) if (!kv.Value.Seen) Gone.Add(kv.Key);
            for (int i = 0; i < Gone.Count; i++) All.Remove(Gone[i]);
        }

        private static Posed Get(int key)
        {
            Posed p;
            if (!All.TryGetValue(key, out p)) { p = new Posed { Key = key }; All.Add(key, p); }
            return p;
        }

        private static Animator OwnAvatar(Posed p)
        {
            if (LabAvatar != null) return LabAvatar;
            if (p.Anim != null && p.Anim.gameObject.activeInHierarchy) return p.Anim;
            if (Time.unscaledTime < p.NextSearch) return null;
            p.NextSearch = Time.unscaledTime + 1f;
            Transform head = AcPlayer.HeadBone;
            if (head == null) return null;
            string kind;
            p.Anim = AcAvatarFind.ByHead(head.position, out kind);
            if (p.Anim != null) AcLog.InfoOnce("Seat: own avatar found for posing (" + kind + " '" + p.Anim.name + "').");
            return p.Anim;
        }

        private static Animator RemoteAvatar(Posed p, ISomniumPlayer player)
        {
            if (p.Anim != null && p.Anim.gameObject.activeInHierarchy) return p.Anim;
            if (Time.unscaledTime < p.NextSearch) return null;
            p.NextSearch = Time.unscaledTime + 1f;
            Transform head = null;
            try { head = player.References != null && player.References.Body != null ? player.References.Body.Head : null; } catch (Exception) { head = null; }
            if (head == null) return null;
            string kind;
            p.Anim = AcAvatarFind.ByHead(head.position, out kind);
            return p.Anim;
        }

        private static void Pose(Posed p, Animator a, AcCarRig car)
        {
            if (!p.Arms.Bound(a) && !p.Arms.Bind(a)) return;
            if (!ReferenceEquals(p.Car, car) || p.Geo == null) { p.Car = car; p.HaveHands = false; p.HaveAngle = false; p.Weight = 0f; p.Geo = AcSeatGeometry.Make(car); }
            AcSeatGeometry g = p.Geo;
            Transform steer = g.SteeringWheel;
            if (steer == null) return;
            if (!p.HaveHands && !Hands(p, g)) { p.HaveAngle = false; return; }
            p.Weight = Mathf.MoveTowards(p.Weight, 1f, Time.deltaTime / BlendIn);
            Quaternion grip = HandFrame(p, g, steer);
            Vector3 wl = steer.position + grip * p.LocalL;
            Vector3 wr = steer.position + grip * p.LocalR;
            Transform body = g.Body;
            Vector3 up = body.up, right = body.right;
            Quaternion gl = GripRotation(grip, p.LocalL, false);
            Quaternion gr = GripRotation(grip, p.LocalR, true);
            // elbows down and out (// ours)
            p.Arms.Apply(wl, gl, -right * 0.6f - up, wr, gr, right * 0.6f - up, p.Weight);
            if (p.Copies == null || p.Copies.Main != a || (p.Copies.Copies == 0 && Time.unscaledTime >= p.NextCopies))
            {
                p.NextCopies = Time.unscaledTime + 5f;
                p.Copies = AcAvatarCopies.Bind(a);
            }

            p.Copies.Copy();
            if (!p.Said)
            {
                p.Said = true;
                AcLog.Info("Seat: hands on the wheel for " + (p.Key == -1 ? "my monitor avatar" : "remote driver " + p.Key) + " (knh wrists " + (g.Data != null && g.Data.HasKnh ? "yes" : "no - rim fallback") +
                           ", fingers " + (p.Arms.HasFingers ? "yes" : "no") + ", copies " + p.Copies.Copies + ").");
            }
        }

        /// <summary>The wrist points in STEER_HR's frame at angle 0: C's HandL / HandR, else the knh, else 9 and 3 o'clock on the rim.</summary>
        private static bool Hands(Posed p, AcSeatGeometry g)
        {
            Vector3 hub;
            Quaternion rest;
            if (!g.WheelRestFrame(out hub, out rest)) return false;
            Quaternion inv = Quaternion.Inverse(rest);
            AcCarModel m = g.Rig.Model;
            Vector3 l, r;
            if (m != null && m.HandL.sqrMagnitude > 1e-6f && m.HandR.sqrMagnitude > 1e-6f) { l = m.HandL; r = m.HandR; }
            else if (g.Data != null && g.Data.Ready && g.Data.HasKnh) { l = AcSeatData.ToUnity(g.Data.HandL); r = AcSeatData.ToUnity(g.Data.HandR); }
            else
            {
                if (g.Data != null && !g.Data.Ready) return false;
                float rim = AcWheelGrab.RimRadius > 0.1f ? AcWheelGrab.RimRadius : 0.18f;
                p.LocalL = new Vector3(-rim, 0f, -0.06f); // ours: 9 o'clock, wrist 6 cm toward the driver
                p.LocalR = new Vector3(rim, 0f, -0.06f);
                p.HaveHands = true;
                return true;
            }

            p.LocalL = inv * (g.ModelToWorld(l) - hub);
            p.LocalR = inv * (g.ModelToWorld(r) - hub);
            // 5 of 84 knh files hold one wrist off the rim (bmw_m3_gt2, lotus_evora_s: 0.35 m vs 0.17-0.20 m - a hand on the shifter):
            // that hand goes onto the rim at its own clock angle, at the other hand's radius
            float rl = new Vector2(p.LocalL.x, p.LocalL.y).magnitude, rr = new Vector2(p.LocalR.x, p.LocalR.y).magnitude;
            if (Mathf.Abs(rl - rr) > 0.1f && Mathf.Min(rl, rr) > 0.05f)
            {
                float want = Mathf.Min(rl, rr);
                if (rl > rr) p.LocalL = new Vector3(p.LocalL.x * want / rl, p.LocalL.y * want / rl, p.LocalL.z);
                else p.LocalR = new Vector3(p.LocalR.x * want / rr, p.LocalR.y * want / rr, p.LocalR.z);
            }

            p.HaveHands = true;
            return true;
        }

        /// <summary>How far the posed hands follow the wheel (deg each way); beyond it they stay put while the wheel turns on under them.
        /// AC's driver plays driver3d.ini [STEER_ANIMATION] steer.ksanim (hand-over-hand over +-LOCK, 360 typical) - a clip two-bone IK cannot
        /// reproduce, so the arms stop at a comfortable angle instead. // ours</summary>
        public const float HandFollowDeg = 110f; // ours

        /// <summary>The frame the wrist points ride in: the steering wheel's rest frame turned by the wheel's angle, clamped to
        /// +-<see cref="HandFollowDeg"/>. The angle is unwrapped frame to frame (C turns STEER_HR as rest * Euler(0, 0, -deg), C3).</summary>
        private static Quaternion HandFrame(Posed p, AcSeatGeometry g, Transform steer)
        {
            Vector3 hub;
            Quaternion rest;
            if (!g.WheelRestFrame(out hub, out rest)) return steer.rotation;
            float wrapped = -Mathf.DeltaAngle(0f, (Quaternion.Inverse(rest) * steer.rotation).eulerAngles.z);
            if (!p.HaveAngle) { p.HaveAngle = true; p.WheelDeg = wrapped; }
            else p.WheelDeg += Mathf.DeltaAngle(p.LastWrapped, wrapped);
            p.LastWrapped = wrapped;
            float deg = Mathf.Clamp(p.WheelDeg, -HandFollowDeg, HandFollowDeg);
            return rest * Quaternion.Euler(0f, 0f, -deg);
        }

        /// <summary>A hand's grip on the rim: knuckle line along the rim's tangent toward twelve o'clock, back of the hand toward the driver.</summary>
        private static Quaternion GripRotation(Quaternion frame, Vector3 local, bool right)
        {
            float th = Mathf.Atan2(local.x, local.y);
            Vector3 tangent = right ? new Vector3(-Mathf.Cos(th), Mathf.Sin(th), 0f) : new Vector3(Mathf.Cos(th), -Mathf.Sin(th), 0f);
            Vector3 fwd = frame * tangent;
            Vector3 up = frame * Vector3.back;
            return Quaternion.LookRotation(fwd, up);
        }

        private static void HeadHide(Animator a)
        {
            if (!AcLocal.ViewInside) { AcHeadHide.Restore(); return; }
            Transform head = a.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            Vector3 s = head.localScale;
            if (!AcHeadHide.Hidden && Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))) < 0.001f)
            {
                _ownHeadHidden = true;      // the client hides a VR player's own head itself
                if (!_saidHead) { _saidHead = true; AcLog.Info("Seat: head hidden by the client already (VR) - left alone."); }
                return;
            }

            AcHeadHide.Hide(head);
            _ownHeadHidden = true;
            if (!_saidHead) { _saidHead = true; AcLog.Info("Seat: own head '" + head.name + "' hidden on this screen while the view is inside the car (others still see it)."); }
        }

        /// <summary>The local player got out: own pose state forgotten.</summary>
        internal static void OwnReleased()
        {
            All.Remove(-1);
            AcHeadHide.Restore();
        }

        internal static void Stop()
        {
            All.Clear();
            AcHeadHide.Unhook();
        }
    }
}
