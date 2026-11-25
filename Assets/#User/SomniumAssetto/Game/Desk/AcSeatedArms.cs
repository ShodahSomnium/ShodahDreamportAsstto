using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Desk] A seated avatar's arms on a steering wheel: two-bone IK of upper arm / forearm to a wrist target, the hand turned into a grip
    /// frame and the fingers curled round the rim - Halo's HaloPuppetPoser (TwoBone, GripNow / Grip, Curl) cut down to the arms. Applied after
    /// the client's Animator (LateUpdate 9997, before the copies are synced), on monitor avatars only: a VR avatar's arms follow its tracked
    /// controllers (VRIK). One instance per posed avatar; no allocation per frame.
    /// </summary>
    public sealed class AcSeatedArms
    {
        private const int LUpper = 0, LLower = 1, LHand = 2, RUpper = 3, RLower = 4, RHand = 5, LFing = 6, RFing = 18, Count = 30;
        private readonly Transform[] _b = new Transform[Count];
        private Animator _anim;
        private Avatar _avatar;

        public Animator Anim { get { return _anim; } }
        public bool HasFingers { get; private set; }

        public bool Bound(Animator a)
        {
            return a != null && a == _anim && a.avatar == _avatar && _b[LHand] != null && _b[RHand] != null;
        }

        public bool Bind(Animator a)
        {
            _anim = a;
            _avatar = a != null ? a.avatar : null;
            for (int i = 0; i < Count; i++) _b[i] = null;
            if (a == null || !a.isHuman) return false;
            _b[LUpper] = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _b[LLower] = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _b[LHand] = a.GetBoneTransform(HumanBodyBones.LeftHand);
            _b[RUpper] = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _b[RLower] = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _b[RHand] = a.GetBoneTransform(HumanBodyBones.RightHand);
            for (int i = 0; i < 12; i++)
            {
                _b[LFing + i] = a.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.LeftIndexProximal + i));
                _b[RFing + i] = a.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.RightIndexProximal + i));
            }

            HasFingers = _b[LFing] != null && _b[LFing + 9] != null && _b[RFing] != null && _b[RFing + 9] != null;
            return _b[LUpper] != null && _b[LLower] != null && _b[LHand] != null && _b[RUpper] != null && _b[RLower] != null && _b[RHand] != null;
        }

        /// <summary>The avatar's arm length (shoulder to wrist, metres; 0.55 when unknown // ours).</summary>
        public float ArmLength
        {
            get
            {
                if (_b[LUpper] == null || _b[LLower] == null || _b[LHand] == null) return 0.55f;
                float a = (_b[LLower].position - _b[LUpper].position).magnitude + (_b[LHand].position - _b[LLower].position).magnitude;
                return a < 0.2f ? 0.55f : a;
            }
        }

        /// <summary>Both hands to their wrist targets with grip rotations (forward = little -> index knuckle line, up = back toward the wrist),
        /// elbows toward the poles, fingers curled; blended from the animated pose by <paramref name="weight"/>.</summary>
        public void Apply(Vector3 leftWrist, Quaternion leftGrip, Vector3 poleL, Vector3 rightWrist, Quaternion rightGrip, Vector3 poleR, float weight)
        {
            if (weight < 0.01f || _anim == null) return;
            Vector3 side = _anim.transform.right;
            TwoBone(_b[LUpper], _b[LLower], _b[LHand], leftWrist, poleL, weight, -side);
            TwoBone(_b[RUpper], _b[RLower], _b[RHand], rightWrist, poleR, weight, side);
            Grip(LHand, LFing, leftGrip, weight);
            Grip(RHand, RFing, rightGrip, weight);
            Curl(LFing, false, weight);
            Curl(RFing, true, weight);
        }

        /// <summary>Two-bone IK (upper, lower, end) to a world target with a pole direction, blended from the animated pose (Halo TwoBone).</summary>
        public static void TwoBone(Transform u, Transform l, Transform h, Vector3 target, Vector3 pole, float weight, Vector3 sideFallback)
        {
            if (u == null || l == null || h == null) return;
            Quaternion uBase = u.localRotation, lBase = l.localRotation;
            Vector3 s = u.position, e = l.position, w = h.position;
            float a = (e - s).magnitude, b = (w - e).magnitude;
            if (a < 1e-4f || b < 1e-4f) return;
            Vector3 toT = target - s;
            float dist = toT.magnitude;
            if (dist < 1e-4f) return;
            Vector3 dir = toT / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.999f);
            pole -= Vector3.Dot(pole, dir) * dir;
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.Cross(dir, sideFallback).normalized;
            else pole.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = s + dir * (a * cosA) + pole * (a * sinA);
            u.rotation = Quaternion.FromToRotation(e - s, elbow - s) * u.rotation;
            Vector3 e1 = l.position, w1 = h.position;
            Vector3 want = s + dir * d - e1;
            if ((w1 - e1).sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f) l.rotation = Quaternion.FromToRotation(w1 - e1, want) * l.rotation;
            if (weight < 0.999f)
            {
                Quaternion uIk = u.localRotation, lIk = l.localRotation;
                u.localRotation = Quaternion.Slerp(uBase, uIk, weight);
                l.localRotation = Quaternion.Slerp(lBase, lIk, weight);
            }
        }

        /// <summary>The hand's grip frame now (forward = little -> index knuckle, up = -(wrist -> middle knuckle)); false without fingers.</summary>
        private bool GripNow(int hand, int fing, out Quaternion grip)
        {
            grip = Quaternion.identity;
            Transform h = _b[hand], index = _b[fing], middle = _b[fing + 3], little = _b[fing + 9];
            if (h == null || index == null || little == null || middle == null) return false;
            Vector3 k = middle.position - h.position;
            if (k.sqrMagnitude < 1e-8f) return false;
            k.Normalize();
            Vector3 f = index.position - little.position;
            f -= Vector3.Dot(f, k) * k;
            if (f.sqrMagnitude < 1e-8f) return false;
            grip = Quaternion.LookRotation(f.normalized, -k);
            return true;
        }

        private void Grip(int hand, int fing, Quaternion want, float weight)
        {
            Quaternion now;
            Transform h = _b[hand];
            if (h == null || !GripNow(hand, fing, out now)) return;
            Quaternion turned = want * Quaternion.Inverse(now) * h.rotation;
            h.rotation = Quaternion.Slerp(h.rotation, turned, Mathf.Clamp01(weight));
        }

        /// <summary>The fingers round the rim: each joint bends toward the palm about the knuckles' line (Halo Curl angles).</summary>
        private void Curl(int fing, bool right, float weight)
        {
            Transform index = _b[fing], little = _b[fing + 9];
            if (index == null || little == null) return;
            Vector3 f = index.position - little.position;
            if (f.sqrMagnitude < 1e-8f) return;
            Vector3 axis = (right ? -f : f).normalized;
            for (int fi = 0; fi < 4; fi++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Transform b = _b[fing + fi * 3 + j];
                    if (b == null) continue;
                    float ang = j == 0 ? 60f : j == 1 ? 70f : 45f; // ours (Halo)
                    b.rotation = Quaternion.AngleAxis(ang * weight, axis) * b.rotation;
                }
            }
        }
    }
}
