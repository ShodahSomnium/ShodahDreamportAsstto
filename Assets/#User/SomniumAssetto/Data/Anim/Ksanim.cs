using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One animated node of a ksanim: its local transform per frame (replaces the node's kn5 matrix).</summary>
    public sealed class KsanimNode
    {
        public string Name;
        /// <summary>Per frame, AC row-vector local matrix (v1 stored as-is, v2 rebuilt from quaternion/translation/scale).</summary>
        public AcMat4[] Frames;
        /// <summary>v2 only: rotation (Hamilton x,y,z,w), translation and scale per frame as stored; null for v1.</summary>
        public AcQuat[] Rotations;
        public AcVec3[] Translations, Scales;

        public int FrameCount { get { return Frames.Length; } }

        /// <summary>
        /// Matrix at normalised time <paramref name="t"/> 0..1 (frame = t * (frames-1)), nearest frame. Allocation-free; for smooth
        /// playback interpolate <see cref="Rotations"/>/<see cref="Translations"/> (v2) or decompose on the Unity side.
        /// </summary>
        public AcMat4 At(float t)
        {
            int n = Frames.Length;
            if (n == 0) return AcMat4.Identity;
            if (t <= 0f) return Frames[0];
            if (t >= 1f) return Frames[n - 1];
            int f = (int)Math.Round(t * (n - 1));
            return Frames[f];
        }
    }

    /// <summary>
    /// Assetto Corsa node animation (<c>animations/*.ksanim</c>: doors, wipers, paddles, steer/shift driver anims, pop-up lights,
    /// lotus_49 suspension; also pit crew in content/objects3D).
    ///
    /// Format [VERIFIED: 654/654 files in content parse to the exact byte: v1 x195, v2 x459; ac_car_formats.md 3]:
    /// <c>int32 version (1 or 2), int32 nodeCount</c>; per node: <c>string name, int32 frameCount</c>, frames:
    /// v1 = float[16] local matrix (kn5 dummy convention), v2 = float[10] = quaternion (x,y,z,w), translation (x,y,z), scale (x,y,z).
    /// Frames are LOCAL transforms replacing the node's kn5 matrix (frame 0 of car_DOOR_L DOOR_L == the kn5 DOOR_L matrix).
    /// Playback [BELIEF, AC behaviour]: car_* anims map time 0..1 to frames 0..N-1; steer.ksanim frame =
    /// (steerDeg / LOCK * 0.5 + 0.5) * (N-1) with driver3d.ini [STEER_ANIMATION] LOCK. Unknown versions throw.
    /// </summary>
    public sealed class Ksanim
    {
        public int Version;
        public readonly List<KsanimNode> Nodes = new List<KsanimNode>();
        public string Path;

        public int FrameCount { get { return Nodes.Count > 0 ? Nodes[0].FrameCount : 0; } }

        public KsanimNode Find(string name)
        {
            for (int i = 0; i < Nodes.Count; i++) if (string.Equals(Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Nodes[i];
            return null;
        }

        public static Ksanim Read(string path)
        {
            using (var r = AcBinary.OpenFile(path, 1 << 14))
            {
                var a = Read(r);
                a.Path = path;
                return a;
            }
        }

        public static Ksanim Read(byte[] data)
        {
            using (var r = AcBinary.FromBytes(data)) return Read(r);
        }

        public static Ksanim Read(AcBinary r)
        {
            var a = new Ksanim();
            a.Version = r.ReadInt32();
            if (a.Version != 1 && a.Version != 2) throw new InvalidDataException("ksanim version " + a.Version + " not supported");
            int nc = r.ReadInt32();
            if (nc < 0 || nc > 100000) throw new InvalidDataException("ksanim node count " + nc);
            int per = a.Version == 1 ? 16 : 10;
            var f = new float[16];
            for (int i = 0; i < nc; i++)
            {
                var node = new KsanimNode();
                node.Name = r.ReadString();
                int fc = r.ReadInt32();
                if (fc < 0 || (long)fc * per * 4 > r.Remaining) throw new InvalidDataException("ksanim frame count " + fc + " for " + node.Name);
                node.Frames = new AcMat4[fc];
                if (a.Version == 2)
                {
                    node.Rotations = new AcQuat[fc];
                    node.Translations = new AcVec3[fc];
                    node.Scales = new AcVec3[fc];
                }
                for (int k = 0; k < fc; k++)
                {
                    r.ReadFloats(f, 0, per);
                    if (a.Version == 1) node.Frames[k] = AcMat4.FromRowMajor(f, 0);
                    else
                    {
                        var q = new AcQuat(f[0], f[1], f[2], f[3]);
                        var t = new AcVec3(f[4], f[5], f[6]);
                        var s = new AcVec3(f[7], f[8], f[9]);
                        node.Rotations[k] = q; node.Translations[k] = t; node.Scales[k] = s;
                        node.Frames[k] = AcMat4.FromTrs(q, t, s);
                    }
                }
                a.Nodes.Add(node);
            }
            return a;
        }
    }
}
