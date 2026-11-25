using System;
using System.Collections.Generic;
using SomniumSpace.Bridge.Player;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The cyborg's vehicle seat animations on a seat (the Desk module's puppet idea, for seats only): Halo's own clips from the
    /// cyborg's model_animations unit seat block of the seat label ("W-driver", "W-gunner", "W-passenger", "G-driver", "B-driver",
    /// "scorpion-driver", "scorpionLF", "GT-gunner"): the seat's ENTER [7] and EXIT [8] animations, the seat class's IDLE [0] with its
    /// AIM-STILL overlay [25] (the warthog driver's head / body into the steering, the warthog gunner's aim on the chain gun, the passenger's
    /// aim) laid out on the class's yaw / pitch grid (HaloPose.Aim), and the seat's acceleration overlays [2] [3] (front-back, left-right:
    /// the lurch into turns and braking). Sampled on the CPU (HaloPose, no GameObject) into the Desk module's HaloPuppetSample seat joints,
    /// in the seat marker's frame - what HaloPuppetPoser.ApplySeated lays on an avatar. Unity-side, allocation-free per frame.
    /// </summary>
    public sealed class HaloSeatPuppet
    {
        public enum Phase { Enter, Sit, Exit }

        private const int JPelvis = 0, JChest = 1, JNeck = 2, JHead = 3, JLSh = 4, JRSh = 5, JLEl = 6, JREl = 7, JLWr = 8, JRWr = 9,
            JLHip = 10, JRHip = 11, JLKnee = 12, JRKnee = 13, JLAnk = 14, JRAnk = 15, Count = 16;

        public readonly HaloCyborg Cyborg;
        private readonly HaloPose _pose;
        private readonly Vector3[] _j = new Vector3[Count];
        public HaloPuppetSample Sample;
        public float LegLength { get; private set; }
        public float ArmLength { get; private set; }
        public string Playing { get; private set; }

        private string _seatLabel;
        private HaloAnimGraph.Seat _seat;
        private HaloAnimGraph.WeaponClass _class;
        private HaloTag _weapon;

        public HaloSeatPuppet(HaloCyborg c)
        {
            Cyborg = c;
            _pose = new HaloPose(c.Skel);
            _pose.Reset();
            _pose.ToModel();
            Read();
            var b = _j;
            LegLength = 0.5f * ((b[JLKnee] - b[JLHip]).magnitude + (b[JLAnk] - b[JLKnee]).magnitude + (b[JRKnee] - b[JRHip]).magnitude + (b[JRAnk] - b[JRKnee]).magnitude);
            ArmLength = 0.5f * ((b[JLEl] - b[JLSh]).magnitude + (b[JLWr] - b[JLEl]).magnitude + (b[JREl] - b[JRSh]).magnitude + (b[JRWr] - b[JREl]).magnitude);
        }

        /// <summary>The seat block and class (the held weapon's class in a seat that allows weapons; "unarmed" / "fixed" otherwise).</summary>
        private bool Bind(string seatLabel, HaloTag weapon)
        {
            if (seatLabel == _seatLabel && weapon == _weapon && _seat != null) return true;
            _seatLabel = seatLabel; _weapon = weapon;
            _seat = Cyborg.Graph.FindSeat(seatLabel);
            _class = null;
            if (_seat == null) return false;
            if (weapon != null)
            {
                HaloCyborg.WeaponGrip g = Cyborg.Grip(weapon);
                HaloAnimGraph.WeaponType type;
                _class = Cyborg.Graph.ClassFor(_seat, g != null ? g.Label : null, out type);
            }
            if (_class == null) _class = _seat.Weapon("unarmed") ?? _seat.Weapon("fixed") ?? _seat.Weapon("rifle");
            if (_class == null || _class.Anim(HaloAnimIds.Weapon.Idle) < 0)
                for (int k = 0; k < _seat.Weapons.Count; k++) if (_seat.Weapons[k].Anim(HaloAnimIds.Weapon.Idle) >= 0) { _class = _seat.Weapons[k]; break; }
            return true;
        }

        /// <summary>The seat's enter / exit clip length in seconds (0 = the seat has none).</summary>
        public float Length(string seatLabel, Phase phase)
        {
            if (!Bind(seatLabel, _weapon)) return 0f;
            HaloAnimation a = Clip(phase);
            return a != null && a.Decode() ? a.Duration : 0f;
        }

        private HaloAnimation Clip(Phase phase)
        {
            if (_seat == null) return null;
            if (phase == Phase.Enter) return Cyborg.Graph.Get(_seat.Anim(HaloAnimIds.Seat.Enter));
            if (phase == Phase.Exit) return Cyborg.Graph.Get(_seat.Anim(HaloAnimIds.Seat.Exit));
            return _class != null ? Cyborg.Graph.Get(_class.Anim(HaloAnimIds.Weapon.Idle)) : null;
        }

        /// <summary>
        /// One frame: phase clip at time t (enter / exit held on their last frame, idle looping), then (sitting) the aim-still overlay at
        /// aimYaw / aimPitch (radians, + = left / up, relative to the seat) and the acceleration overlays (accForward / accLeft in -1..1).
        /// </summary>
        public bool Step(string seatLabel, HaloTag weapon, Phase phase, float t, float aimYaw, float aimPitch, float accForward, float accLeft)
        {
            Sample.Valid = false;
            if (!Bind(seatLabel, weapon)) return false;
            HaloAnimation a = Clip(phase);
            if (a == null && phase != Phase.Sit) { a = Clip(Phase.Sit); phase = Phase.Sit; }
            if (a == null || !a.Decode()) return false;
            Playing = a.Name;
            _pose.Reset();
            _pose.Apply(a, Cyborg.NodeMap, phase == Phase.Sit ? t : Mathf.Min(t, Mathf.Max(0f, a.Duration - 0.001f)), phase == Phase.Sit, 1f);
            if (phase == Phase.Sit)
            {
                HaloAnimation aim = _class != null ? Cyborg.Graph.Get(_class.Anim(HaloAnimIds.Weapon.AimStill)) : null;
                if (aim != null)
                    _pose.Aim(aim, Cyborg.NodeMap, aimYaw, aimPitch, _class.RightYawPerFrame, _class.LeftYawPerFrame, _class.RightFrames, _class.LeftFrames,
                              _class.DownPitchPerFrame, _class.UpPitchPerFrame, _class.DownFrames, _class.UpFrames, 1f);
                Acc(HaloAnimIds.Seat.AccFrontBack, accForward);
                Acc(HaloAnimIds.Seat.AccLeftRight, accLeft);
            }
            _pose.ToModel();
            Read();
            Fill();
            return true;
        }

        /// <summary>A 3-frame acceleration overlay: frame 1 is rest, 0 / 2 the two extremes.</summary>
        private void Acc(int id, float v)
        {
            if (Mathf.Abs(v) < 0.02f || _seat == null) return;
            HaloAnimation a = Cyborg.Graph.Get(_seat.Anim(id));
            if (a == null || !a.Decode() || a.Frames < 3) return;
            float u = Mathf.Clamp(v, -1f, 1f);
            if (u >= 0f) _pose.Overlay(a, Cyborg.NodeMap, 1, 2, u, 1f);
            else _pose.Overlay(a, Cyborg.NodeMap, 1, 0, -u, 1f);
        }

        private void Read()
        {
            HaloCyborg c = Cyborg;
            _j[JPelvis] = Pt(c.Pelvis); _j[JChest] = Pt(c.Spine1); _j[JNeck] = Pt(c.Neck); _j[JHead] = Pt(c.Head);
            _j[JLSh] = Pt(c.LUpper); _j[JRSh] = Pt(c.RUpper); _j[JLEl] = Pt(c.LFore); _j[JREl] = Pt(c.RFore);
            _j[JLWr] = Pt(c.LHand); _j[JRWr] = Pt(c.RHand); _j[JLHip] = Pt(c.LThigh); _j[JRHip] = Pt(c.RThigh);
            _j[JLKnee] = Pt(c.LCalf); _j[JRKnee] = Pt(c.RCalf); _j[JLAnk] = Pt(c.LFoot); _j[JRAnk] = Pt(c.RFoot);
        }

        private Vector3 Pt(int node)
        {
            if (node < 0) return Vector3.zero;
            float[] t = _pose.MTrans;
            return HaloCyborg.P(t[node * 3], t[node * 3 + 1], t[node * 3 + 2]);
        }

        private Quaternion HandGrip(int node)
        {
            float zx, zy, zz, xx, xy, xz;
            _pose.Axis(node, 0f, 0f, 1f, out zx, out zy, out zz);
            _pose.Axis(node, 1f, 0f, 0f, out xx, out xy, out xz);
            return Quaternion.LookRotation(HaloCyborg.D(zx, zy, zz), -HaloCyborg.D(xx, xy, xz));
        }

        /// <summary>The seat joints in the seat marker's frame (Desk's HaloPuppet.FillSeat, the same maths).</summary>
        private void Fill()
        {
            Vector3[] j = _j;
            Sample.Seated = true;
            Sample.SPelvis = S(j[JPelvis]); Sample.SChest = S(j[JChest]); Sample.SNeck = S(j[JNeck]); Sample.SHead = S(j[JHead]);
            Sample.SLSh = S(j[JLSh]); Sample.SRSh = S(j[JRSh]); Sample.SLEl = S(j[JLEl]); Sample.SREl = S(j[JREl]);
            Sample.SLWr = S(j[JLWr]); Sample.SRWr = S(j[JRWr]); Sample.SLHip = S(j[JLHip]); Sample.SRHip = S(j[JRHip]);
            Sample.SLKnee = S(j[JLKnee]); Sample.SRKnee = S(j[JRKnee]); Sample.SLAnk = S(j[JLAnk]); Sample.SRAnk = S(j[JRAnk]);
            Vector3 up = Sample.SChest - Sample.SPelvis;
            Vector3 across = Sample.SRHip - Sample.SLHip;
            Vector3 fwd = Vector3.Cross(across, up);
            Sample.SPelvisRot = up.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-8f ? Quaternion.LookRotation(Vector3.right, Vector3.up) : Quaternion.LookRotation(fwd.normalized, up.normalized);
            Sample.SLGrip = SeatRot(HandGrip(Cyborg.LHand));
            Sample.SRGrip = SeatRot(HandGrip(Cyborg.RHand));
            Sample.GunInRGrip = Quaternion.identity;
            Sample.RWristOnGun = Vector3.zero;
            Sample.AimWeight = 1f;
            Sample.Valid = true;
        }

        private static Vector3 S(Vector3 p) { return new Vector3(p.z, p.y, -p.x); }
        private static Quaternion SeatRot(Quaternion q) { return Quaternion.LookRotation(S(q * Vector3.forward), S(q * Vector3.up)); }

        /// <summary>
        /// The seat's IK points on the vehicle (the cyborg's hand markers laid onto e.g. "right hand wheel grip", "gunner left hand"): world
        /// wrist targets at the avatar's arm scale k (Desk's HaloDeskPlayer.SeatIk, the same rule).
        /// </summary>
        public void SeatIk(string seatLabel, Transform vehicle, float k, out bool lv, out Vector3 lw, out bool rv, out Vector3 rw)
        {
            lv = rv = false; lw = rw = Vector3.zero;
            HaloAnimGraph.Seat s = Cyborg.Graph.FindSeat(seatLabel);
            if (s == null) return;
            for (int i = 0; i < s.Ik.Count; i++)
            {
                HaloAnimGraph.IkPoint p = s.Ik[i];
                bool right = p.Marker.IndexOf("right hand", StringComparison.OrdinalIgnoreCase) >= 0;
                bool left = !right && p.Marker.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!right && !left) continue;
                Transform t = HaloDeskPlayer.FindMarker(vehicle, p.AttachTo);
                if (t == null) continue;
                HaloSkeleton.Marker m = right ? Cyborg.RightMarker : Cyborg.LeftMarker;
                Quaternion hand = t.rotation * Quaternion.Inverse(HaloSpace.Rot(m.Qx, m.Qy, m.Qz, m.Qw));
                Vector3 w = t.position - hand * HaloSpace.Pos(m.Tx, m.Ty, m.Tz) * k;
                if (right) { rv = true; rw = w; } else { lv = true; lw = w; }
            }
        }
    }

    /// <summary>
    /// Seated avatars, posed with Halo's own seat animations (the user's request after the first client test: "put avatar in vehicle when
    /// entering, use same animations etc as real Halo"). For every player in a seat - the own avatar and every remote rider HaloVehicleRiders
    /// places - and for the moments around it: ENTER (the seat's enter clip from the enter marker into the seat), SIT (idle + aim-still +
    /// acceleration overlays: the warthog driver leaning into the steering with his hands on the wheel grips, the gunner's body on the turning
    /// mount with the gun's pitch, a passenger's aim) and EXIT (the seat's exit clip out of the vehicle, ending where getting out stands the player).
    /// The pose goes on the avatar's humanoid bones with the Desk module's HaloPuppetPoser.ApplySeated (hips on the seat, legs, torso, arms) after
    /// the client's Animator and IK and after Desk (order 9997), and onto the avatar's mirror / shadow copies (HaloAvatarCopies).
    ///  - monitor players: the whole body; hands on the vehicle's grip markers (Halo's seat IK) or the animation's hands;
    ///  - headset players, first-person seats: hips, legs and torso from the animation, the ARMS reach the tracked controllers (the real hands stay
    ///    theirs; the warthog gunner's hands are on the chain gun's grips because the player holds them there); the hands keep the controllers' turn;
    ///  - a headset driver in the client-style third-person view: the whole body in the seat with the hands on the wheel (the player's own view is
    ///    behind the vehicle; the controllers are not near the wheel).
    /// VR comfort: the own view never follows the enter / exit clip - it cuts to the seat on getting in and to the exit point on getting out (a
    /// teleport, as every Somnium move of the player is), heading only; only the body animates, for everyone else and in mirrors.
    /// While V poses a player, Desk must not (HaloDesk.PoseSeated = false, HaloDesk.PoseOwn = false while the own avatar is V's; remote players:
    /// <see cref="Owns"/>, asked by Desk).
    /// </summary>
    [DefaultExecutionOrder(9997)]
    public sealed class HaloSeatPoses : MonoBehaviour
    {
        public sealed class Rider
        {
            public int Key;                       // -1 own, else the remote's Fusion player id
            public HaloVehicle Vehicle;
            public int Seat;
            public string Label;
            public HaloSeatPuppet.Phase Phase;
            public float T, Length;
            public HaloPuppetPoser Poser;
            public HaloSeatPuppet Puppet;
            public Animator Anim;
            public HaloAvatarCopies Copies;
            public float NextSearch, NextCopies;
            public ISomniumPlayer Player;
            public bool Vr, Seen, Said;
            public HaloTag Weapon;
        }

        private static readonly Dictionary<int, Rider> Riders = new Dictionary<int, Rider>();
        private static readonly List<int> Gone = new List<int>();
        private static bool _deskHeld, _ownHeld, _poseSeatedWas = true;
        private static HaloCyborg _cyborg;
        private static bool _saidNoCyborg;

        /// <summary>The lab's stand-in avatars (key -> Animator; null = find the client's).</summary>
        public static Func<int, Animator> LabAvatar;

        /// <summary>V poses this player now (key -1 = own): Desk leaves them alone.</summary>
        public static bool Owns(int key) { Rider r; return Riders.TryGetValue(key, out r) && r.Anim != null; }

        /// <summary>The same by net slot (Desk's HaloDeskRemote.Slot).</summary>
        public static bool OwnsSlot(int slot)
        {
            if (slot < 0) return false;
            var p = HaloNetGame.Find(slot);
            return p != null && Owns(p.PlayerId);
        }

        /// <summary>The own avatar's current phase / clip, for the log and the lab.</summary>
        public static string Describe(int key) { Rider r; return Riders.TryGetValue(key, out r) ? r.Phase + " '" + (r.Puppet != null ? r.Puppet.Playing : "-") + "' t " + r.T.ToString("0.00") + "/" + r.Length.ToString("0.00") : "none"; }

        public static Rider Get(int key) { Rider r; return Riders.TryGetValue(key, out r) ? r : null; }

        private static HaloCyborg Cyb()
        {
            HaloCyborg c = HaloCyborg.Current;
            if (c == null && !_saidNoCyborg) { _saidNoCyborg = true; HaloLog.Info("Vehicles: no cyborg animations loaded yet - seated avatars are not posed until they are."); }
            if (c != _cyborg) { _cyborg = c; foreach (var kv in Riders) kv.Value.Puppet = null; }
            return c;
        }

        // ------------------------------------------------------------------ who sits where (Update, from the seat and the riders)

        private void Update()
        {
            try { Track(); }
            catch (Exception e) { HaloLog.Error("Vehicles: seat poses failed: " + e); }
        }

        private static void Track()
        {
            foreach (var kv in Riders) kv.Value.Seen = false;
            var me = HaloVehicleSeat.Local;
            if (me.Seated && me.Vehicle != null) Note(-1, me.Vehicle, me.SeatIndex, null, HaloVehicleSeat.Vr, HaloLocal.SeatAllowsWeapons && HaloLocal.HeldWeapon != null ? HaloLocal.HeldWeapon.Tag : null);
            var list = HaloVehicleRiders.Current;
            for (int i = 0; i < list.Count; i++) Note(list[i].PlayerId, list[i].Vehicle, list[i].Seat, list[i].Player, list[i].Xr, null);
            Gone.Clear();
            foreach (var kv in Riders)
            {
                var r = kv.Value;
                if (r.Seen) continue;
                // out of the seat: play the exit clip where the seat is (unless they died, or the vehicle went)
                bool dead = r.Key == -1 ? !HaloLocal.Alive : false;
                if (r.Phase != HaloSeatPuppet.Phase.Exit && !dead && r.Vehicle != null && r.Puppet != null)
                {
                    r.Phase = HaloSeatPuppet.Phase.Exit; r.T = 0f; r.Length = r.Puppet.Length(r.Label, HaloSeatPuppet.Phase.Exit);
                    if (r.Length > 0.05f) { HaloLog.Info("Vehicles: " + Who(r) + " plays '" + r.Label + " exit' (" + r.Length.ToString("0.00") + " s)."); continue; }
                }
                if (r.Phase == HaloSeatPuppet.Phase.Exit && r.T < r.Length && !dead && r.Vehicle != null) continue;
                Gone.Add(kv.Key);
            }
            for (int i = 0; i < Gone.Count; i++) { Drop(Riders[Gone[i]]); Riders.Remove(Gone[i]); }
            HoldDesk(Riders.ContainsKey(-1));
        }

        private static string Who(Rider r) { return r.Key == -1 ? "my avatar" : "player " + r.Key; }

        private static void Note(int key, HaloVehicle v, int seat, ISomniumPlayer player, bool vr, HaloTag weapon)
        {
            var s = v.SeatOf(seat);
            if (s == null) return;
            Rider r;
            if (!Riders.TryGetValue(key, out r) || r.Vehicle != v || r.Seat != seat || r.Phase == HaloSeatPuppet.Phase.Exit)
            {
                if (r != null) Drop(r);
                r = new Rider { Key = key, Vehicle = v, Seat = seat, Label = s.Label, Phase = HaloSeatPuppet.Phase.Enter, T = 0f, Poser = new HaloPuppetPoser() };
                Riders[key] = r;
            }
            r.Seen = true; r.Player = player; r.Vr = vr; r.Weapon = weapon;
        }

        /// <summary>Desk's switches while V poses: seated avatars are V's; the own avatar entirely while V poses it.</summary>
        private static void HoldDesk(bool own)
        {
            if (!_deskHeld)
            {
                _deskHeld = true;
                _poseSeatedWas = HaloDesk.PoseSeated;
                HaloDesk.PoseSeated = false;
            }
            if (own && !_ownHeld) { _ownHeld = true; HaloDesk.PoseOwn = false; }
            else if (!own && _ownHeld) { _ownHeld = false; HaloDesk.PoseOwn = true; }
        }

        /// <summary>Gives Desk its switches back (the match ended).</summary>
        public static void Clear()
        {
            foreach (var kv in Riders) Drop(kv.Value);
            Riders.Clear();
            if (_deskHeld) { HaloDesk.PoseSeated = _poseSeatedWas; _deskHeld = false; }
            if (_ownHeld) { HaloDesk.PoseOwn = true; _ownHeld = false; }
        }

        private static void Drop(Rider r)
        {
            if (r.Key == -1) OwnHead(null, false);
            try { if (r.Poser != null) r.Poser.Release(); } catch (Exception) { }
            r.Anim = null;
        }

        // ------------------------------------------------------------------ the pose (LateUpdate 9997)

        private void LateUpdate()
        {
            try
            {
                float dt = Time.deltaTime;
                HaloCyborg c = Cyb();
                if (c == null) return;
                foreach (var kv in Riders) Pose(kv.Value, c, dt);
            }
            catch (Exception e) { HaloLog.Error("Vehicles: posing seated avatars failed: " + e); }
        }

        private static void Pose(Rider r, HaloCyborg c, float dt)
        {
            if (r.Vehicle == null) return;
            var seat = r.Vehicle.SeatOf(r.Seat);
            if (seat == null || seat.Marker == null) return;
            if (r.Puppet == null)
            {
                r.Puppet = new HaloSeatPuppet(c);
                if (r.Phase == HaloSeatPuppet.Phase.Enter) { r.Length = r.Puppet.Length(r.Label, HaloSeatPuppet.Phase.Enter); if (r.Length <= 0.05f) r.Phase = HaloSeatPuppet.Phase.Sit; }
            }
            r.T += dt;
            if (r.Phase == HaloSeatPuppet.Phase.Enter && r.T >= r.Length) { r.Phase = HaloSeatPuppet.Phase.Sit; r.T = 0f; }
            if (!FindAvatar(r)) return;

            // aim / steering / acceleration for the overlays
            float yaw = 0f, pitch = 0f, accF = 0f, accL = 0f;
            Overlays(r, seat, out yaw, out pitch, out accF, out accL);
            if (!r.Puppet.Step(r.Label, r.Weapon, r.Phase, r.T, yaw, pitch, accF, accL)) return;
            if (!r.Said)
            {
                r.Said = true;
                HaloLog.Info("Vehicles: " + Who(r) + " sits as the cyborg in '" + r.Label + "' (" + r.Phase + ", playing '" + r.Puppet.Playing + "'" +
                             (r.Vr ? ", headset: hips + legs + torso, arms to the " + (VrThird(r) ? "vehicle's grips (third-person view)" : "tracked controllers") : ", monitor: the whole body") + ").");
            }

            var poser = r.Poser;
            poser.Begin(dt);
            Transform lh = r.Anim.GetBoneTransform(HumanBodyBones.LeftHand), rh = r.Anim.GetBoneTransform(HumanBodyBones.RightHand);
            Quaternion lRot = lh != null ? lh.rotation : Quaternion.identity, rRot = rh != null ? rh.rotation : Quaternion.identity;
            float k = r.Puppet.ArmLength > 0.1f ? Mathf.Clamp(poser.ArmLength / r.Puppet.ArmLength, 0.4f, 1.6f) : 0.8f;
            bool lv = false, rv = false;
            Vector3 lw = Vector3.zero, rw = Vector3.zero;
            bool tracked = r.Vr && !VrThird(r);
            if (tracked)
            {
                // the real hands: the arms reach for the tracked controllers (Bridge hand anchors)
                Transform tl = HandAnchor(r, false), tr = HandAnchor(r, true);
                if (tl != null) { lv = true; lw = tl.position; }
                if (tr != null) { rv = true; rw = tr.position; }
            }
            else if (r.Phase == HaloSeatPuppet.Phase.Sit) r.Puppet.SeatIk(r.Label, r.Vehicle.transform, k, out lv, out lw, out rv, out rw);
            Vector3 gp; Quaternion gq;
            poser.ApplySeated(r.Puppet.Sample, dt, seat.Marker, r.Puppet.LegLength, r.Puppet.ArmLength, true, true, lv, lw, rv, rw, out gp, out gq);
            if (tracked)
            {
                // the hands keep the controllers' turn (the animation's grip is for a monitor player)
                if (lh != null && lv) lh.rotation = lRot;
                if (rh != null && rv) rh.rotation = rRot;
            }
            if (r.Copies == null || r.Copies.Main != r.Anim || (r.Copies.Copies == 0 && Time.unscaledTime >= r.NextCopies))
            {
                r.NextCopies = Time.unscaledTime + 5f;
                r.Copies = HaloAvatarCopies.Bind(r.Anim);
            }
            r.Copies.Copy();
            if (r.Key == -1) OwnHead(r.Anim, r.Vr && VrThird(r));
        }

        private static Transform _headT;
        private static Vector3 _headWas;
        private static bool _headShown, _saidHead;

        /// <summary>
        /// The own head in a headset: the client shrinks the own avatar's head on the main copy (so the eyes are not inside it) and V never
        /// hides or scales a head. In the third-person vehicle view, though, the player watches their own avatar driving from behind - there the
        /// head is given its size back on this screen (only while that view is on), and the client's scale is put back for first person, on getting
        /// out and on avatar changes. Mirrors, shadows and other players never had it hidden.
        /// </summary>
        private static void OwnHead(Animator a, bool show)
        {
            Transform head = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            if (_headT != null && (_headT != head || !show))
            {
                if (_headShown) { try { _headT.localScale = _headWas; } catch (Exception) { } }
                _headT = null; _headShown = false;
            }
            if (!show || head == null) return;
            if (_headT == null) { _headT = head; _headWas = head.localScale; _headShown = false; }
            if (head.localScale.sqrMagnitude < 1e-4f)
            {
                if (!_headShown) _headWas = head.localScale;
                head.localScale = Vector3.one;
                _headShown = true;
                if (!_saidHead) { _saidHead = true; HaloLog.Info("Vehicles: VR third-person view - my avatar's head (shrunk by the client for first person) is shown on this screen while I watch from behind; the client's scale comes back for first person."); }
            }
        }

        /// <summary>The own head's scale now (the lab checks it).</summary>
        public static Vector3 OwnHeadScale(Animator a) { Transform h = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null; return h != null ? h.localScale : Vector3.zero; }

        private static bool VrThird(Rider r)
        {
            if (r.Key == -1) return HaloVehicleSeat.Local.ThirdPerson;
            // a remote headset driver whose head is far from the seat watches from the chase view
            Transform head = null;
            try { head = r.Player != null ? r.Player.References?.Body?.Head : null; } catch (Exception) { head = null; }
            var s = r.Vehicle.SeatOf(r.Seat);
            return head != null && s != null && (head.position - s.Marker.position).sqrMagnitude > 1.6f * 1.6f;
        }

        private static Transform HandAnchor(Rider r, bool right)
        {
            if (r.Key == -1) return right ? HaloPlayer.RightHand : HaloPlayer.LeftHand;
            try { var b = r.Player != null ? r.Player.References?.Body : null; return b != null ? (right ? b.RightHand : b.LeftHand) : null; }
            catch (Exception) { return null; }
        }

        private static void Overlays(Rider r, HaloVehicle.SeatInfo seat, out float yaw, out float pitch, out float accF, out float accL)
        {
            var v = r.Vehicle;
            yaw = 0f; pitch = 0f;
            if (seat.Def.Driver && !seat.Guns) yaw = v.Steer * 0.45f;                // the warthog driver into the steering (ours: 0.45 rad at full lock)
            else if (seat.Guns) { pitch = v.AimPitch; yaw = seat.Standing ? 0f : v.AimYaw; } // the gunner's body turns with the mount; the gun's pitch
            else
            {
                // a passenger looks / aims where their head looks (relative to the seat)
                Transform head = r.Key == -1 ? HaloPlayer.HeadBone : null;
                if (r.Key != -1) { try { head = r.Player != null ? r.Player.References?.Body?.Head : null; } catch (Exception) { head = null; } }
                if (head != null && seat.Marker != null)
                {
                    Vector3 local = Quaternion.Inverse(seat.Marker.rotation) * head.forward;      // marker frame: x forward, y up, z left
                    yaw = Mathf.Atan2(local.z, local.x);
                    pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f));
                }
            }
            Vector3 a = v.Acceleration;
            accF = Mathf.Clamp(Vector3.Dot(a, v.transform.forward) / 12f, -1f, 1f);       // ours: 12 m/s^2 = the full lurch
            accL = Mathf.Clamp(-Vector3.Dot(a, v.transform.right) / 12f, -1f, 1f);
        }

        private static bool FindAvatar(Rider r)
        {
            Animator a = LabAvatar != null ? LabAvatar(r.Key) : null;
            if (a == null && r.Anim != null && r.Anim.gameObject.activeInHierarchy) a = r.Anim;
            if (a == null && LabAvatar == null && Time.unscaledTime >= r.NextSearch)
            {
                r.NextSearch = Time.unscaledTime + 1f;
                Transform head = null;
                if (r.Key == -1) head = HaloPlayer.HeadBone;
                else { try { head = r.Player != null ? r.Player.References?.Body?.Head : null; } catch (Exception) { head = null; } }
                if (r.Key != -1)
                {
                    var p = HaloNetGame.Players;
                    for (int i = 0; i < p.Count; i++) if (p[i] != null && p[i].PlayerId == r.Key && p[i].Avatar != null) { a = p[i].Avatar; break; }
                }
                string kind;
                if (a == null && head != null) a = HaloAvatarFind.ByHead(head.position, out kind);
            }
            if (a == null) return false;
            if (a != r.Anim || !r.Poser.Bound(a))
            {
                string say;
                if (!r.Poser.Bind(a, out say)) { HaloLog.Info("Vehicles: " + Who(r) + "'s avatar cannot be seated: " + say); r.Anim = null; return false; }
                r.Anim = a; r.Copies = null;
            }
            return true;
        }

        /// <summary>
        /// Where the seat's exit clip ends (the cyborg's pelvis on its last frame, at an avatar's size of 0.8 of the cyborg's), dropped to the
        /// ground: getting out stands the player there so the clip ends on the player's real spot. False when the seat has no exit clip.
        /// </summary>
        public static bool ExitPoint(HaloVehicle v, int seatIndex, out Vector3 at)
        {
            at = Vector3.zero;
            HaloCyborg c = Cyb();
            var s = v != null ? v.SeatOf(seatIndex) : null;
            if (c == null || s == null || s.Marker == null) return false;
            var p = new HaloSeatPuppet(c);
            float len = p.Length(s.Label, HaloSeatPuppet.Phase.Exit);
            if (len <= 0.05f || !p.Step(s.Label, null, HaloSeatPuppet.Phase.Exit, len, 0f, 0f, 0f, 0f)) return false;
            Vector3 pelvis = s.Marker.TransformPoint(p.Sample.SPelvis * 0.8f);
            RaycastHit hit;
            if (!Physics.Raycast(pelvis + Vector3.up * 1.5f, Vector3.down, out hit, 6f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            at = hit.point + Vector3.up * 0.05f;
            return true;
        }
    }
}
