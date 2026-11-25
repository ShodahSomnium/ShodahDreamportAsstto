using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] Plays one ksanim (car_DOOR_L/R, car_wiper, car_shift(_up/_dw), lights, car_susp_xx, the driver's steer.ksanim) on a
    /// built node tree: frames are LOCAL transforms that replace the node's kn5 matrix (ac_car_formats.md 3; frame 0 of car_DOOR_L DOOR_L ==
    /// the kn5 DOOR_L matrix). Time 0..1 maps to frames 0..N-1 with interpolation (lerp position/scale, slerp rotation).
    /// Frames are converted to Unity TRS once at bind time; <see cref="Sample"/> allocates nothing.
    /// Nodes absent from the model are skipped (the census found export junk like "g_SUSP_RR_Dump" in some files), and nodes whose frames
    /// never change are skipped too (car_DOOR_L carries a constant COCKPIT_HR; a wiper file may list rims) so two players never fight.
    /// </summary>
    public sealed class AcKsanimPlayer
    {
        public readonly Ksanim Anim;
        public readonly int FrameCount;
        Transform[] _targets = new Transform[0];
        Vector3[][] _pos;
        Quaternion[][] _rot;
        Vector3[][] _scale;
        float _last = -1f;

        /// <summary>Number of nodes driven.</summary>
        public int Bound { get { return _targets.Length; } }
        /// <summary>Last sampled time (-1 before the first sample).</summary>
        public float Time { get { return _last; } }
        /// <summary>The driven transforms (read-only use).</summary>
        public Transform[] Targets { get { return _targets; } }

        AcKsanimPlayer(Ksanim anim) { Anim = anim; FrameCount = anim != null ? anim.FrameCount : 0; }

        /// <summary>Binds <paramref name="anim"/> to the nodes <paramref name="find"/> returns (null = absent). Null when nothing animates.</summary>
        public static AcKsanimPlayer Bind(Ksanim anim, Func<string, Transform> find)
        {
            if (anim == null || find == null || anim.FrameCount < 1) return null;
            var p = new AcKsanimPlayer(anim);
            var targets = new List<Transform>();
            var pos = new List<Vector3[]>();
            var rot = new List<Quaternion[]>();
            var scale = new List<Vector3[]>();
            for (int i = 0; i < anim.Nodes.Count; i++)
            {
                var n = anim.Nodes[i];
                if (n.Frames == null || n.Frames.Length == 0 || IsConstant(n)) continue;
                var t = find(n.Name);
                if (t == null || targets.Contains(t)) continue;
                int fc = n.Frames.Length;
                var ps = new Vector3[fc]; var rs = new Quaternion[fc]; var ss = new Vector3[fc];
                for (int f = 0; f < fc; f++) AcCarMath.ToTrs(n.Frames[f], out ps[f], out rs[f], out ss[f]);
                for (int f = 1; f < fc; f++) if (Quaternion.Dot(rs[f - 1], rs[f]) < 0f) rs[f] = new Quaternion(-rs[f].x, -rs[f].y, -rs[f].z, -rs[f].w);
                targets.Add(t); pos.Add(ps); rot.Add(rs); scale.Add(ss);
            }
            if (targets.Count == 0) return null;
            p._targets = targets.ToArray();
            p._pos = pos.ToArray(); p._rot = rot.ToArray(); p._scale = scale.ToArray();
            return p;
        }

        static bool IsConstant(KsanimNode n)
        {
            var a = n.Frames[0];
            for (int f = 1; f < n.Frames.Length; f++)
            {
                var b = n.Frames[f];
                for (int k = 0; k < 16; k++) if (Math.Abs(a[k] - b[k]) > 1e-5f) return false;
            }
            return true;
        }

        /// <summary>Poses every bound node at normalised time <paramref name="t"/> (clamped 0..1). Skips the work when t did not change.</summary>
        public void Sample(float t)
        {
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            if (t == _last) return;
            _last = t;
            for (int i = 0; i < _targets.Length; i++)
            {
                var tr = _targets[i];
                if (tr == null) continue;
                var ps = _pos[i];
                int n = ps.Length;
                float f = t * (n - 1);
                int f0 = (int)f;
                if (f0 >= n - 1) { f0 = n - 1; }
                int f1 = f0 < n - 1 ? f0 + 1 : f0;
                float w = f - f0;
                tr.localPosition = Vector3.LerpUnclamped(ps[f0], ps[f1], w);
                tr.localRotation = Quaternion.SlerpUnclamped(_rot[i][f0], _rot[i][f1], w);
                tr.localScale = Vector3.LerpUnclamped(_scale[i][f0], _scale[i][f1], w);
            }
        }

        /// <summary>Index of a driven transform (-1 when this clip does not move it).</summary>
        public int IndexOf(Transform t)
        {
            for (int i = 0; i < _targets.Length; i++) if (_targets[i] == t) return i;
            return -1;
        }

        /// <summary>The clip's local pose of target <paramref name="index"/> at time <paramref name="t"/> without touching the transform. No allocation.</summary>
        public void Evaluate(int index, float t, out Vector3 pos, out Quaternion rot)
        {
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            var ps = _pos[index];
            int n = ps.Length;
            float f = t * (n - 1);
            int f0 = (int)f;
            if (f0 >= n - 1) f0 = n - 1;
            int f1 = f0 < n - 1 ? f0 + 1 : f0;
            float w = f - f0;
            pos = Vector3.LerpUnclamped(ps[f0], ps[f1], w);
            rot = Quaternion.SlerpUnclamped(_rot[index][f0], _rot[index][f1], w);
        }

        /// <summary>Forces the next <see cref="Sample"/> to write (after something else moved the nodes, e.g. a LOD switch).</summary>
        public void Invalidate() { _last = -1f; }
    }
}
