using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>An embedded texture: the raw file (DDS or PNG) with its file offset so it can be read later on any thread.</summary>
    public sealed class Kn5Texture
    {
        public string Name;
        public int Type;          // always 1 in the install
        public long Offset;       // absolute file offset of the bytes
        public int Size;
        public byte[] Bytes;      // null unless loaded (Kn5ReadOptions.Textures or Kn5File.ReadTextureBytes)
        AcDdsInfo _info;

        /// <summary>Header info (format, size, mips; PNG passthrough). Needs <see cref="Bytes"/> or call <see cref="Kn5File.ReadTextureHeader"/>.</summary>
        public AcDdsInfo Info
        {
            get { if (_info == null && Bytes != null) _info = AcDds.Parse(Bytes, 0, Bytes.Length); return _info; }
            set { _info = value; }
        }
    }

    /// <summary>One material property: AC stores 1 + 2 + 3 + 4 floats per property (only one of them is meaningful).</summary>
    public sealed class Kn5Property
    {
        public string Name;
        /// <summary>Scalar value (ksDiffuse, ksSpecularEXP, ksAlphaRef ...).</summary>
        public float A;
        /// <summary>2-vector (track multilayer multR/G/B/A, detailNMMult use B).</summary>
        public float[] B = new float[2];
        /// <summary>3-vector (ksEmissive colour lives here).</summary>
        public float[] C = new float[3];
        /// <summary>4-vector.</summary>
        public float[] D = new float[4];
    }

    /// <summary>A texture slot of a material (txDiffuse@0, txNormal@1, txMaps@2, txDetail@3 ...).</summary>
    public sealed class Kn5Sampler
    {
        public string Name;       // slot name, e.g. "txDiffuse"
        public int Slot;          // shader register
        public string Texture;    // texture file name, resolved against Kn5File.Textures + skin overrides (case-insensitive)
    }

    public sealed class Kn5Material
    {
        public string Name, Shader;
        /// <summary>0 opaque, 1 alpha blend, 2 alpha-to-coverage [BELIEF].</summary>
        public byte BlendMode;
        public bool AlphaTested;
        /// <summary>0 normal, 1 no depth write [BELIEF names]. 0 for kn5 v4.</summary>
        public int DepthMode;
        public readonly List<Kn5Property> Properties = new List<Kn5Property>();
        public readonly List<Kn5Sampler> Samplers = new List<Kn5Sampler>();

        public Kn5Property Property(string name)
        {
            for (int i = 0; i < Properties.Count; i++) if (string.Equals(Properties[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Properties[i];
            return null;
        }

        /// <summary>Scalar value A of a property, or <paramref name="def"/>.</summary>
        public float Float(string name, float def = 0f) { var p = Property(name); return p != null ? p.A : def; }

        /// <summary>Texture file name of a slot ("txDiffuse"), or null.</summary>
        public string Texture(string slot)
        {
            for (int i = 0; i < Samplers.Count; i++) if (string.Equals(Samplers[i].Name, slot, StringComparison.OrdinalIgnoreCase)) return Samplers[i].Texture;
            return null;
        }
    }

    /// <summary>A bone of a skinned mesh: the bone node's name and its inverse bind matrix (AC row-vector form; use as bindpose).</summary>
    public struct Kn5Bone
    {
        public string Name;
        public AcMat4 InverseBind;
    }

    /// <summary>
    /// Mesh data of a class 2 (static) or class 3 (skinned) node. Vertices are interleaved floats in the space of the parent dummy
    /// chain: stride 11 = pos3 normal3 uv2 tangent3; stride 19 adds weights4 + boneIndices4 (indices stored AS FLOATS, -1 = unused).
    /// Indices are a ushort triangle list. When <see cref="Pooled"/> is true the arrays come from <see cref="AcPool{T}"/> and may be
    /// longer than needed: use <see cref="VertexCount"/> / <see cref="IndexCount"/>, and call <see cref="Release"/> when done.
    /// </summary>
    public sealed class Kn5Mesh
    {
        public bool CastShadows, IsVisible, IsTransparent;
        /// <summary>False = physics-only surface (track); never draw it, still use it as a collider.</summary>
        public bool IsRenderable = true;
        public bool Skinned;
        public int Stride;                 // 11 or 19
        public int VertexCount, IndexCount;
        public long VertexOffset, IndexOffset;   // file offsets of the bulk blocks (always recorded)
        public float[] Vertices;           // null until loaded
        public ushort[] Indices;           // null until loaded
        public bool Pooled;
        /// <summary>True when the loaded arrays were converted to Unity conventions (X mirrored, winding flipped).</summary>
        public bool ConvertedToUnity;
        public int MaterialId, Layer;
        public float LodIn, LodOut;        // metres; 0/0 = always
        public AcVec3 SphereCenter;        // AC space of the parent chain
        public float SphereRadius;
        public Kn5Bone[] Bones;            // skinned only
        public byte[] SkinTail;            // skinned only: 8 unknown bytes (zero in the install)

        public bool Loaded { get { return Vertices != null && Indices != null; } }

        /// <summary>Returns pooled arrays and forgets the data (offsets stay, so it can be reloaded).</summary>
        public void Release()
        {
            if (Pooled)
            {
                AcPool<float>.Return(Vertices);
                AcPool<ushort>.Return(Indices);
            }
            Vertices = null; Indices = null; Pooled = false; ConvertedToUnity = false;
        }

        public AcVec3 Position(int i) { int o = i * Stride; return new AcVec3(Vertices[o], Vertices[o + 1], Vertices[o + 2]); }
        public AcVec3 Normal(int i) { int o = i * Stride + 3; return new AcVec3(Vertices[o], Vertices[o + 1], Vertices[o + 2]); }
        public float U(int i) { return Vertices[i * Stride + 6]; }
        public float V(int i) { return Vertices[i * Stride + 7]; }
    }

    public enum Kn5NodeClass { Dummy = 1, Mesh = 2, SkinnedMesh = 3 }

    public sealed class Kn5Node
    {
        public Kn5NodeClass Class;
        public string Name;
        /// <summary>Inactive nodes start hidden (RIM_BLUR_*, COCKPIT_LR, DAMAGE_GLASS_*).</summary>
        public bool Active;
        /// <summary>Local matrix of a dummy (identity for meshes: their vertices are in the parent's space).</summary>
        public AcMat4 Local = AcMat4.Identity;
        public Kn5Mesh Mesh;               // class 2/3
        public Kn5Node Parent;
        public readonly List<Kn5Node> Children = new List<Kn5Node>();
        /// <summary>Index in <see cref="Kn5File.Nodes"/> (depth-first order).</summary>
        public int Index;

        public bool IsDummy { get { return Class == Kn5NodeClass.Dummy; } }
        public bool IsMesh { get { return Class != Kn5NodeClass.Dummy; } }

        /// <summary>World matrix in the kn5's own space (row-vector: local * parent * ... ).</summary>
        public AcMat4 World
        {
            get
            {
                AcMat4 m = Local;
                for (var p = Parent; p != null; p = p.Parent) if (p.Class == Kn5NodeClass.Dummy) m = AcMat4.Mul(m, p.Local);
                return m;
            }
        }

        /// <summary>Depth-first search below (and including) this node, case-insensitive.</summary>
        public Kn5Node Find(string name)
        {
            if (string.Equals(Name, name, StringComparison.OrdinalIgnoreCase)) return this;
            for (int i = 0; i < Children.Count; i++) { var r = Children[i].Find(name); if (r != null) return r; }
            return null;
        }

        public override string ToString() { return Class + " " + Name; }
    }

    /// <summary>What <see cref="Kn5File.Read(string, Kn5ReadOptions)"/> loads.</summary>
    public sealed class Kn5ReadOptions
    {
        /// <summary>Load embedded texture bytes (else only name/offset/size are kept).</summary>
        public bool Textures = true;
        /// <summary>Load vertex/index arrays (else only counts and file offsets: a header-only scan).</summary>
        public bool Meshes = true;
        /// <summary>Rent mesh arrays from <see cref="AcPool{T}"/> (call <see cref="Kn5File.Release"/> / <see cref="Kn5Mesh.Release"/>).</summary>
        public bool Pooled;
        /// <summary>Convert loaded meshes to Unity conventions on the reading thread (X mirror + winding flip).</summary>
        public bool ConvertToUnity;
        /// <summary>Throw when bytes remain after the node tree (a strict format check; off by default).</summary>
        public bool StrictEnd;

        public static Kn5ReadOptions Full { get { return new Kn5ReadOptions(); } }
        public static Kn5ReadOptions HeaderOnly { get { return new Kn5ReadOptions { Textures = false, Meshes = false }; } }
        public static Kn5ReadOptions NoTextures { get { return new Kn5ReadOptions { Textures = false, Meshes = true }; } }
    }

    /// <summary>
    /// Reader for Assetto Corsa .kn5 models (cars, tracks, drivers, showrooms). Unity-free, worker-thread safe, streaming.
    ///
    /// Format [VERIFIED: 430/430 car kn5, 18/18 driver kn5, 113 track kn5 parse to the exact last byte; ac_car_formats.md 1]:
    /// <c>"sc6969"</c>, int32 version (5 or 6), int32 extra (v &gt; 5 only, 0); textures (int32 count; per texture int32 type, string
    /// name, int32 size, bytes); materials (int32 count; string name, string shader, byte blend, byte alphaTested, int32 depthMode
    /// (v &gt; 4), int32 propCount x {string, float A, float[2] B, float[3] C, float[4] D}, int32 samplerCount x {string slot, int32
    /// register, string texture}); then ONE root node, depth-first: int32 class, string name, int32 childCount, byte active, then
    /// class 1: float[16] matrix; class 2: 3 flag bytes, int32 vc, vc*44 B, int32 ic, ic*2 B, int32 material, int32 layer, float
    /// lodIn, lodOut, float[3] sphere centre, float radius, byte renderable; class 3: 3 flag bytes, int32 boneCount x {string,
    /// float[16]}, int32 vc, vc*76 B, int32 ic, ic*2 B, int32 material, int32 layer, 8 bytes.
    ///
    /// Big files (300-430 MB tracks): texture and vertex blocks are never read unless asked; with <see cref="Kn5ReadOptions.HeaderOnly"/>
    /// the reader seeks past them and records offsets (<see cref="Kn5Texture.Offset"/>, <see cref="Kn5Mesh.VertexOffset"/>), so a
    /// worker can later load single meshes/textures (<see cref="LoadMesh"/>, <see cref="ReadTextureBytes"/>) without the whole file in
    /// memory. LOD_1..3 and collider kn5 carry 0 textures: resolve names against the LOD_0 file + skin overrides.
    /// AC -> Unity conversion: see <see cref="AcMat4"/> / <see cref="AcConvert"/>.
    /// </summary>
    public sealed class Kn5File
    {
        public string Path;
        public int Version;
        public long FileLength;
        public readonly List<Kn5Texture> Textures = new List<Kn5Texture>();
        public readonly List<Kn5Material> Materials = new List<Kn5Material>();
        public Kn5Node Root;
        /// <summary>Every node in depth-first order (root first).</summary>
        public readonly List<Kn5Node> Nodes = new List<Kn5Node>();
        /// <summary>Bytes left after the node tree (0 in every installed file).</summary>
        public long TrailingBytes;

        const int MaxDepth = 512;              // ours: corrupt-file guard (real trees are < 20 deep)
        const int MaxCount = 50000000;         // ours: sanity limit for counts

        public int MeshCount { get { int n = 0; for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Mesh != null) n++; return n; } }

        public static Kn5File Read(string path) { return Read(path, Kn5ReadOptions.Full); }

        public static Kn5File Read(string path, Kn5ReadOptions opt)
        {
            if (opt == null) opt = Kn5ReadOptions.Full;
            using (var r = AcBinary.OpenFile(path, 1 << 16))
            {
                var k = Read(r, opt);
                k.Path = path;
                return k;
            }
        }

        /// <summary>Reads from any stream positioned at the "sc6969" magic (offsets are stream positions).</summary>
        public static Kn5File Read(AcBinary r, Kn5ReadOptions opt)
        {
            var k = new Kn5File();
            k.FileLength = r.Length;
            string magic = r.ReadAscii(6);
            if (magic != "sc6969") throw new InvalidDataException("not a kn5 file (magic '" + magic + "')");
            k.Version = r.ReadInt32();
            if (k.Version < 1 || k.Version > 64) throw new InvalidDataException("kn5 version " + k.Version);
            if (k.Version > 5) r.ReadInt32();

            int tc = Count(r.ReadInt32(), "texture");
            for (int i = 0; i < tc; i++)
            {
                var t = new Kn5Texture();
                t.Type = r.ReadInt32();
                t.Name = r.ReadString();
                t.Size = r.ReadInt32();
                if (t.Size < 0 || t.Size > r.Remaining) throw new InvalidDataException("kn5 texture '" + t.Name + "' size " + t.Size);
                t.Offset = r.Position;
                if (opt.Textures) t.Bytes = r.ReadBytes(t.Size);
                else r.Skip(t.Size);
                k.Textures.Add(t);
            }

            int mc = Count(r.ReadInt32(), "material");
            for (int i = 0; i < mc; i++) k.Materials.Add(ReadMaterial(r, k.Version));

            k.Root = ReadNode(r, k, null, opt, 0);
            k.TrailingBytes = r.Length - r.Position;
            if (opt.StrictEnd && k.TrailingBytes != 0) throw new InvalidDataException("kn5 has " + k.TrailingBytes + " trailing bytes");
            return k;
        }

        static int Count(int n, string what)
        {
            if (n < 0 || n > MaxCount) throw new InvalidDataException("kn5 " + what + " count " + n);
            return n;
        }

        static Kn5Material ReadMaterial(AcBinary r, int version)
        {
            var m = new Kn5Material();
            m.Name = r.ReadString();
            m.Shader = r.ReadString();
            m.BlendMode = r.ReadByte();
            m.AlphaTested = r.ReadBool();
            if (version > 4) m.DepthMode = r.ReadInt32();
            int pc = Count(r.ReadInt32(), "property");
            for (int p = 0; p < pc; p++)
            {
                var pr = new Kn5Property();
                pr.Name = r.ReadString();
                pr.A = r.ReadSingle();
                r.ReadFloats(pr.B, 0, 2);
                r.ReadFloats(pr.C, 0, 3);
                r.ReadFloats(pr.D, 0, 4);
                m.Properties.Add(pr);
            }
            int sc = Count(r.ReadInt32(), "sampler");
            for (int s = 0; s < sc; s++)
            {
                var sm = new Kn5Sampler();
                sm.Name = r.ReadString();
                sm.Slot = r.ReadInt32();
                sm.Texture = r.ReadString();
                m.Samplers.Add(sm);
            }
            return m;
        }

        static Kn5Node ReadNode(AcBinary r, Kn5File k, Kn5Node parent, Kn5ReadOptions opt, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("kn5 node tree too deep");
            var n = new Kn5Node();
            n.Parent = parent;
            n.Index = k.Nodes.Count;
            k.Nodes.Add(n);
            int cls = r.ReadInt32();
            n.Name = r.ReadString();
            int kids = Count(r.ReadInt32(), "child");
            n.Active = r.ReadBool();
            if (cls == 1)
            {
                n.Class = Kn5NodeClass.Dummy;
                var f = new float[16];
                r.ReadFloats(f, 0, 16);
                n.Local = AcMat4.FromRowMajor(f, 0);
            }
            else if (cls == 2 || cls == 3)
            {
                n.Class = cls == 2 ? Kn5NodeClass.Mesh : Kn5NodeClass.SkinnedMesh;
                var m = new Kn5Mesh();
                n.Mesh = m;
                m.Skinned = cls == 3;
                m.Stride = cls == 2 ? 11 : 19;
                m.CastShadows = r.ReadBool();
                m.IsVisible = r.ReadBool();
                m.IsTransparent = r.ReadBool();
                if (cls == 3)
                {
                    int bc = Count(r.ReadInt32(), "bone");
                    m.Bones = new Kn5Bone[bc];
                    var f = new float[16];
                    for (int b = 0; b < bc; b++)
                    {
                        m.Bones[b].Name = r.ReadString();
                        r.ReadFloats(f, 0, 16);
                        m.Bones[b].InverseBind = AcMat4.FromRowMajor(f, 0);
                    }
                }
                m.VertexCount = Count(r.ReadInt32(), "vertex");
                m.VertexOffset = r.Position;
                long vbytes = (long)m.VertexCount * m.Stride * 4;
                if (vbytes > r.Remaining) throw new InvalidDataException("kn5 mesh '" + n.Name + "' vertex block past end");
                if (opt.Meshes)
                {
                    int nf = m.VertexCount * m.Stride;
                    m.Vertices = opt.Pooled ? AcPool<float>.Rent(nf) : new float[nf];
                    r.ReadFloats(m.Vertices, 0, nf);
                }
                else r.Skip(vbytes);
                m.IndexCount = Count(r.ReadInt32(), "index");
                m.IndexOffset = r.Position;
                long ibytes = (long)m.IndexCount * 2;
                if (ibytes > r.Remaining) throw new InvalidDataException("kn5 mesh '" + n.Name + "' index block past end");
                if (opt.Meshes)
                {
                    m.Indices = opt.Pooled ? AcPool<ushort>.Rent(Math.Max(1, m.IndexCount)) : new ushort[m.IndexCount];
                    r.ReadUShorts(m.Indices, 0, m.IndexCount);
                    m.Pooled = opt.Pooled;
                    if (opt.ConvertToUnity) Convert(m);
                }
                else r.Skip(ibytes);
                m.MaterialId = r.ReadInt32();
                m.Layer = r.ReadInt32();
                if (cls == 2)
                {
                    m.LodIn = r.ReadSingle();
                    m.LodOut = r.ReadSingle();
                    m.SphereCenter = new AcVec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    m.SphereRadius = r.ReadSingle();
                    m.IsRenderable = r.ReadBool();
                }
                else
                {
                    m.SkinTail = r.ReadBytes(8);
                    m.IsRenderable = true;
                }
            }
            else throw new InvalidDataException("kn5 node class " + cls + " ('" + n.Name + "' at " + r.Position + ")");

            for (int i = 0; i < kids; i++) n.Children.Add(ReadNode(r, k, n, opt, depth + 1));
            return n;
        }

        static void Convert(Kn5Mesh m)
        {
            AcConvert.MirrorVerticesX(m.Vertices, m.VertexCount, m.Stride);
            AcConvert.FlipWinding(m.Indices, m.IndexCount);
            m.ConvertedToUnity = true;
        }

        // ---------------- lazy loading by offset ----------------

        /// <summary>
        /// Loads one mesh's vertex/index arrays from the file (after a header-only read). Thread-safe per call (own stream).
        /// For many meshes prefer <see cref="LoadMeshes"/> (one stream, ascending offsets).
        /// </summary>
        public void LoadMesh(Kn5Mesh m, bool pooled = false, bool convertToUnity = false)
        {
            if (m == null || m.Loaded) return;
            using (var r = AcBinary.OpenFile(Path, 1 << 16)) LoadMesh(r, m, pooled, convertToUnity);
        }

        /// <summary>Loads every not-yet-loaded mesh accepted by <paramref name="filter"/> (null = all) in file order.</summary>
        public void LoadMeshes(Predicate<Kn5Node> filter, bool pooled = false, bool convertToUnity = false)
        {
            using (var r = AcBinary.OpenFile(Path, 1 << 18))
            {
                for (int i = 0; i < Nodes.Count; i++)
                {
                    var n = Nodes[i];
                    if (n.Mesh == null || n.Mesh.Loaded) continue;
                    if (filter != null && !filter(n)) continue;
                    LoadMesh(r, n.Mesh, pooled, convertToUnity);
                }
            }
        }

        static void LoadMesh(AcBinary r, Kn5Mesh m, bool pooled, bool convertToUnity)
        {
            int nf = m.VertexCount * m.Stride;
            m.Vertices = pooled ? AcPool<float>.Rent(nf) : new float[nf];
            m.Indices = pooled ? AcPool<ushort>.Rent(Math.Max(1, m.IndexCount)) : new ushort[m.IndexCount];
            m.Pooled = pooled;
            r.Position = m.VertexOffset;
            r.ReadFloats(m.Vertices, 0, nf);
            r.Position = m.IndexOffset;
            r.ReadUShorts(m.Indices, 0, m.IndexCount);
            if (convertToUnity) Convert(m);
        }

        /// <summary>Reads a texture's raw bytes (DDS/PNG) by offset; caches nothing. Thread-safe per call.</summary>
        public byte[] ReadTextureBytes(Kn5Texture t)
        {
            if (t.Bytes != null) return t.Bytes;
            using (var r = AcBinary.OpenFile(Path, 1 << 12))
            {
                r.Position = t.Offset;
                return r.ReadBytes(t.Size);
            }
        }

        /// <summary>Reads only the first bytes of a texture and parses its header (format/size/mips), no pixel data.</summary>
        public AcDdsInfo ReadTextureHeader(Kn5Texture t)
        {
            if (t.Info != null) return t.Info;
            byte[] head;
            if (t.Bytes != null) head = t.Bytes;
            else
            {
                using (var r = AcBinary.OpenFile(Path, 1 << 12))
                {
                    r.Position = t.Offset;
                    head = r.ReadBytes(Math.Min(t.Size, 148));
                }
            }
            t.Info = AcDds.Parse(head, 0, head.Length, t.Size);
            return t.Info;
        }

        /// <summary>Returns pooled mesh arrays of every node (see <see cref="Kn5ReadOptions.Pooled"/>).</summary>
        public void Release()
        {
            for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Mesh != null) Nodes[i].Mesh.Release();
        }

        // ---------------- queries ----------------

        /// <summary>First node with this name (case-insensitive), depth-first; null if absent.</summary>
        public Kn5Node Find(string name)
        {
            for (int i = 0; i < Nodes.Count; i++) if (string.Equals(Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Nodes[i];
            return null;
        }

        /// <summary>Texture by file name (case-insensitive), or null.</summary>
        public Kn5Texture Texture(string name)
        {
            if (name == null) return null;
            for (int i = 0; i < Textures.Count; i++) if (string.Equals(Textures[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Textures[i];
            return null;
        }

        public Kn5Material Material(int id) { return id >= 0 && id < Materials.Count ? Materials[id] : null; }

        /// <summary>Totals for logs: dummies, meshes, skinned, vertices, indices.</summary>
        public void Totals(out int dummies, out int meshes, out int skinned, out long vertices, out long indices)
        {
            dummies = meshes = skinned = 0; vertices = indices = 0;
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (n.Mesh == null) { dummies++; continue; }
                meshes++;
                if (n.Mesh.Skinned) skinned++;
                vertices += n.Mesh.VertexCount;
                indices += n.Mesh.IndexCount;
            }
        }
    }
}
