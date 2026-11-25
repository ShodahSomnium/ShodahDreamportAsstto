using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Worker-side preparation of ONE kn5 for <c>AcKn5Builder</c> (cars, showrooms, props; Unity-free). Reads whatever the
    /// <see cref="Kn5File"/> does not already hold (meshes / texture bytes by file offset), applies skin overrides (every file in
    /// <see cref="SkinDir"/> replaces the kn5 texture of the same name, case-insensitive - research 6.2) and a texture fallback file (LOD_1..3
    /// and collider kn5 carry 0 textures: their names resolve against the LOD_0 file).
    /// </summary>
    public sealed class AcModelJob
    {
        public Kn5File Kn5;
        public string SkinDir;
        public Kn5File TextureFallback;
        public bool PhysicsColliders, CollideRendered, HideMarkers = true, MakeMips = true, Readable;
        /// <summary>Iteration 2: read the damage texture slots (AcShading.Of(m, true)) - a car built while damage textures are wanted.</summary>
        public bool DamageSlots;
        public AcSurfaces Surfaces;
        /// <summary>Texture names (lower case) already uploaded in the material scope: not read again.</summary>
        public HashSet<string> SkipTextures;
        /// <summary>Per node: not built (with its subtree), decided on the main thread (AcKn5BuildOptions.Skip). Null = none.</summary>
        public bool[] Skipped;

        public AcShading[] Shading;
        public bool[] Active;
        public readonly List<AcTexPrep> Textures = new List<AcTexPrep>();
        /// <summary>Lower-case texture name -> where it came from ("kn5", "skin", "fallback"), for logs.</summary>
        public readonly Dictionary<string, string> TextureSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<AcMeshPrep> Meshes = new List<AcMeshPrep>();
        public readonly List<string> Problems = new List<string>();
        public int SkinOverrides, MarkerCubes;
        public volatile bool Done, Failed;
        public volatile string Error;
        public long Ms;

        public void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Prepare(); }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; Failed = true; }
            finally { Ms = sw.ElapsedMilliseconds; Done = true; }
        }

        void Prepare()
        {
            var k = Kn5;
            Shading = new AcShading[k.Materials.Count];
            for (int i = 0; i < Shading.Length; i++) Shading[i] = AcShading.Of(k.Materials[i], DamageSlots);
            Active = new bool[k.Nodes.Count];
            for (int i = 0; i < k.Nodes.Count; i++) { var n = k.Nodes[i]; Active[i] = n.Active && (n.Parent == null || Active[n.Parent.Index]); }

            // skin folder files by lower-case name
            var skin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(SkinDir) && Directory.Exists(SkinDir))
                foreach (var f in Directory.GetFiles(SkinDir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".dds" || ext == ".png" || ext == ".jpg") skin[Path.GetFileName(f)] = f;
                }

            // meshes
            var usedMats = new bool[k.Materials.Count];
            AcBinary r = null;
            try
            {
                for (int i = 0; i < k.Nodes.Count; i++)
                {
                    var n = k.Nodes[i];
                    var m = n.Mesh;
                    if (m == null) continue;
                    if (Skipped != null && Skipped[i]) continue;
                    if (HideMarkers && AcTrackMarkers.IsMarkerCube(n)) { MarkerCubes++; continue; }
                    var p = new AcMeshPrep { Node = n, Material = k.Material(m.MaterialId) };
                    // inactive nodes are built too (hidden): cars switch RIM_BLUR / COCKPIT_LR / DAMAGE_GLASS on and off
                    p.Render = m.IsRenderable && m.VertexCount > 0 && m.IndexCount >= 3;
                    bool physics = PhysicsColliders && AcSurfaces.IsPhysicsName(n.Name);
                    p.Collide = physics || (CollideRendered && p.Render);
                    if (physics && Surfaces != null) { bool wall, exact; p.Surface = Surfaces.Match(n.Name, out wall, out exact); p.Wall = wall; }
                    if (!p.Render && !p.Collide) continue;
                    var sh = m.MaterialId >= 0 && m.MaterialId < Shading.Length ? Shading[m.MaterialId] : null;
                    if (p.Render && p.Material != null) usedMats[m.MaterialId] = true;
                    if (m.Loaded)
                    {
                        if (m.ConvertedToUnity) { Problems.Add(n.Name + ": mesh already converted (read without ConvertToUnity)"); continue; }
                        AcMeshPrepare.Fill(p, m, m.Vertices, m.Indices, sh != null && sh.NeedsTangents);
                    }
                    else
                    {
                        if (r == null) r = AcBinary.OpenFile(k.Path, 1 << 16);
                        int nf = m.VertexCount * m.Stride;
                        var raw = AcPool<float>.Rent(nf);
                        var idx = AcPool<ushort>.Rent(Math.Max(1, m.IndexCount));
                        try
                        {
                            r.Position = m.VertexOffset; r.ReadFloats(raw, 0, nf);
                            r.Position = m.IndexOffset; r.ReadUShorts(idx, 0, m.IndexCount);
                            AcMeshPrepare.Fill(p, m, raw, idx, sh != null && sh.NeedsTangents);
                        }
                        finally { AcPool<float>.Return(raw); AcPool<ushort>.Return(idx); }
                    }
                    Meshes.Add(p);
                }
            }
            finally { if (r != null) r.Dispose(); }

            // textures used by the drawn materials
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int mi = 0; mi < usedMats.Length; mi++)
            {
                if (!usedMats[mi]) continue;
                var sl = Shading[mi].Slots;
                for (int s = 0; s < sl.Count; s++) { string tn = k.Materials[mi].Texture(sl[s]); if (!string.IsNullOrEmpty(tn)) wanted.Add(tn); }
            }
            foreach (var tn in wanted)
            {
                if (SkipTextures != null && SkipTextures.Contains(tn.ToLowerInvariant())) continue;
                byte[] bytes = null; string src = null;
                try
                {
                    string sp;
                    if (skin.TryGetValue(tn, out sp)) { bytes = File.ReadAllBytes(sp); src = "skin"; SkinOverrides++; }
                    else
                    {
                        var t = k.Texture(tn);
                        if (t != null) { bytes = k.ReadTextureBytes(t); src = "kn5"; }
                        else if (TextureFallback != null && (t = TextureFallback.Texture(tn)) != null) { bytes = TextureFallback.ReadTextureBytes(t); src = "fallback"; }
                    }
                }
                catch (Exception e) { Problems.Add("texture " + tn + ": " + e.Message); }
                if (bytes == null) { if (src == null) Problems.Add("texture " + tn + " not found"); continue; }
                var p = AcTexPrepare.Prepare(tn, bytes, MakeMips);
                if (p.Problem != null) Problems.Add("texture " + tn + ": " + p.Problem);
                Textures.Add(p);
                TextureSource[tn] = src;
            }
        }
    }
}
