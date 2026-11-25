using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    public sealed class KnhNode
    {
        public string Name;
        public AcMat4 Local;
        public KnhNode Parent;
        public readonly List<KnhNode> Children = new List<KnhNode>();

        /// <summary>World matrix in car MODEL space (row-vector: local * parent * ...).</summary>
        public AcMat4 World
        {
            get { AcMat4 m = Local; for (var p = Parent; p != null; p = p.Parent) m = AcMat4.Mul(m, p.Local); return m; }
        }
    }

    /// <summary>
    /// <c>driver_base_pos.knh</c>: the seated driver skeleton of a car [VERIFIED: ferrari_458 6480 B = 71 nodes consumed exactly].
    /// Recursive depth-first, no header: <c>string name, float[16] local matrix, int32 childCount, children...</c>.
    /// Positions are car MODEL space (ground origin; add GRAPHICS_OFFSET for physics space). <c>DRIVER:RIG_Center</c> = seat
    /// (0.355, 0.319, 0.009 in the 458: left seat), <c>DRIVER:RIG_Head</c> is ~0.09 m behind car.ini DRIVEREYES.
    /// Use: overwrite the local matrix of driver kn5 nodes with the same name, then apply steer.ksanim on top [BELIEF, CM does it].
    /// </summary>
    public sealed class Knh
    {
        public KnhNode Root;
        public readonly List<KnhNode> Nodes = new List<KnhNode>();
        public string Path;

        public static Knh Read(string path)
        {
            using (var r = AcBinary.OpenFile(path, 1 << 14))
            {
                var k = Read(r);
                k.Path = path;
                return k;
            }
        }

        public static Knh Read(byte[] data) { using (var r = AcBinary.FromBytes(data)) return Read(r); }

        public static Knh Read(AcBinary r)
        {
            var k = new Knh();
            k.Root = ReadNode(r, k, null, 0);
            return k;
        }

        static KnhNode ReadNode(AcBinary r, Knh k, KnhNode parent, int depth)
        {
            if (depth > 256) throw new InvalidDataException("knh tree too deep");
            var n = new KnhNode { Parent = parent };
            n.Name = r.ReadString();
            var f = new float[16];
            r.ReadFloats(f, 0, 16);
            n.Local = AcMat4.FromRowMajor(f, 0);
            int kids = r.ReadInt32();
            if (kids < 0 || kids > 10000) throw new InvalidDataException("knh child count " + kids + " at " + n.Name);
            k.Nodes.Add(n);
            for (int i = 0; i < kids; i++) n.Children.Add(ReadNode(r, k, n, depth + 1));
            return n;
        }

        public KnhNode Find(string name)
        {
            for (int i = 0; i < Nodes.Count; i++) if (string.Equals(Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Nodes[i];
            return null;
        }

        /// <summary>World position (model space) of a node, or null.</summary>
        public AcVec3? WorldPosition(string name)
        {
            var n = Find(name);
            if (n == null) return null;
            return n.World.Translation;
        }
    }
}
