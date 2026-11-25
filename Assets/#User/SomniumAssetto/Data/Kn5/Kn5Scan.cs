using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Header-only kn5 pass for big track files: textures (name/offset/size), materials, the full node tree with matrices and
    /// per-mesh counts / flags / LOD / bounding sphere / file offsets - but no texture bytes and no vertex data (seeks past them).
    /// Measured on the install: 22-600 ms for a 300-430 MB file (warm cache), a whole monza layout (7 files) in &lt; 0.6 s.
    /// Use it first on a worker (markers, surfaces, material table, loading progress), then load meshes/textures by offset with
    /// <see cref="Kn5File.LoadMeshes"/> / <see cref="Kn5File.ReadTextureBytes"/>.
    /// </summary>
    public static class Kn5Scan
    {
        public static Kn5File Scan(string path) { return Kn5File.Read(path, Kn5ReadOptions.HeaderOnly); }

        /// <summary>
        /// World matrices (kn5 space, AC row-vector convention) of every node, indexed by <see cref="Kn5Node.Index"/>, computed in
        /// one depth-first pass (parents come before children in <see cref="Kn5File.Nodes"/>). Mesh nodes get their parent's world.
        /// </summary>
        public static AcMat4[] WorldMatrices(Kn5File k)
        {
            var w = new AcMat4[k.Nodes.Count];
            for (int i = 0; i < k.Nodes.Count; i++)
            {
                var n = k.Nodes[i];
                AcMat4 parent = n.Parent != null ? w[n.Parent.Index] : AcMat4.Identity;
                w[i] = n.Class == Kn5NodeClass.Dummy ? AcMat4.Mul(n.Local, parent) : parent;
            }
            return w;
        }

        /// <summary>Every node whose name starts with <paramref name="prefix"/> (ordinal, case-sensitive), depth-first order.</summary>
        public static List<Kn5Node> WithPrefix(Kn5File k, string prefix)
        {
            var l = new List<Kn5Node>();
            for (int i = 0; i < k.Nodes.Count; i++) if (k.Nodes[i].Name.StartsWith(prefix, StringComparison.Ordinal)) l.Add(k.Nodes[i]);
            return l;
        }

        /// <summary>
        /// The set of texture file names referenced by materials of the meshes accepted by <paramref name="filter"/> (null = all):
        /// load only those (a physics-only file like monza 2.kn5 references 1 texture).
        /// </summary>
        public static HashSet<string> UsedTextures(Kn5File k, Predicate<Kn5Node> filter)
        {
            var mats = new bool[k.Materials.Count];
            for (int i = 0; i < k.Nodes.Count; i++)
            {
                var n = k.Nodes[i];
                if (n.Mesh == null || (filter != null && !filter(n))) continue;
                if (n.Mesh.MaterialId >= 0 && n.Mesh.MaterialId < mats.Length) mats[n.Mesh.MaterialId] = true;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int m = 0; m < mats.Length; m++)
            {
                if (!mats[m]) continue;
                var s = k.Materials[m].Samplers;
                for (int j = 0; j < s.Count; j++) if (!string.IsNullOrEmpty(s[j].Texture)) names.Add(s[j].Texture);
            }
            return names;
        }
    }
}
