using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>How <see cref="AcKn5Builder"/> builds a kn5 node tree.</summary>
    public sealed class AcKn5BuildOptions
    {
        /// <summary>Layer for renderers (default World 0).</summary>
        public int Layer = AcLayers.World;
        /// <summary>Skin folder whose files override kn5 textures by name (car skins; null = none).</summary>
        public string SkinDir;
        /// <summary>Keep meshes CPU-readable (S measures the steering rim from the mesh; colliders need it). Default false = UploadMeshData(true).</summary>
        public bool Readable;
        /// <summary>Build MeshColliders for physics meshes (track: digit-prefixed names, tagged with AcSurfaceTag) on <see cref="ColliderLayer"/>.</summary>
        public bool PhysicsColliders;
        public int ColliderLayer = AcLayers.Track;
        /// <summary>The layout's surfaces (needed with <see cref="PhysicsColliders"/>).</summary>
        public AcSurfaces Surfaces;
        /// <summary>Hide AC_ marker cubes and isRenderable=0 meshes (track).</summary>
        public bool HideMarkers = true;
        /// <summary>Main-thread budget per frame for uploads in <see cref="AcKn5Builder.BuildAsync"/> (ms). // ours</summary>
        public float SliceMs = 6f;

        // ---- added by W
        /// <summary>Textures missing from the kn5 are looked up here (LOD_1..3 / collider kn5 carry 0 textures: pass the LOD_0 file).</summary>
        public Kn5File TextureSource;
        /// <summary>The same as <see cref="TextureSource"/> (both names are used by callers).</summary>
        public Kn5File TextureFallback { get { return TextureSource; } set { TextureSource = value; } }
        /// <summary>Every DRAWN mesh also gets a MeshCollider on <see cref="ColliderLayer"/> (a showroom floor the avatar stands on).</summary>
        public bool CollideRendered;
        /// <summary>Make mips for textures stored with one level (on the worker; default on).</summary>
        public bool MakeMips = true;
        /// <summary>Renderers cast shadows when the kn5 says so (castShadows flag); false = never.</summary>
        public bool CastShadows = true;
        /// <summary>Material scope key: builds with the same key share textures and materials (default: texture source path + skin).</summary>
        public string ScopeKey;
        /// <summary>Nodes not built, with their subtree (C: COCKPIT_HR of remote / AI cars). Evaluated on the main thread before the worker starts.</summary>
        public Predicate<Kn5Node> Skip;
        /// <summary>Meshes kept CPU-readable regardless of <see cref="Readable"/> (STEER_HR's rim for S's grab).</summary>
        public Predicate<Kn5Node> ReadableFilter;
        /// <summary>Iteration 2 (DAMAGE_DESIGN 2.1): read the damage paint slots (txDamage, txDamageMask) and give ksBrokenGlass the install's
        /// crack map. C passes AcCarDamageGate.TexturesWanted at the car's spawn; a build with it on uses its own material scope ("|damage").
        /// False (default; tracks, showroom props, DAMAGE 0) = exactly iteration 1's textures.</summary>
        public bool DamageSlots;
    }

    /// <summary>A built kn5: the GameObject tree plus lookups by node name.</summary>
    public sealed class AcKn5Model
    {
        /// <summary>Root of the built tree (child of the parent given to the builder).</summary>
        public Transform Root;
        /// <summary>Every node's transform by kn5 node name (first one wins on duplicates).</summary>
        public readonly Dictionary<string, Transform> Nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
        /// <summary>All renderers built.</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();
        /// <summary>Physics colliders built (with <see cref="AcKn5BuildOptions.PhysicsColliders"/>).</summary>
        public readonly List<Collider> Colliders = new List<Collider>();
        /// <summary>Bounds of the renderers (world space at build time).</summary>
        public Bounds Bounds;

        // ---- added by W
        /// <summary>The kn5 this was built from.</summary>
        public Kn5File Kn5;
        /// <summary>Meshes created for this model (render + collision); destroyed by <see cref="Dispose"/>.</summary>
        public readonly List<Mesh> Meshes = new List<Mesh>();
        /// <summary>The renderer of a mesh node by kn5 node name (first wins).</summary>
        public readonly Dictionary<string, Renderer> RendererByNode = new Dictionary<string, Renderer>(StringComparer.Ordinal);
        /// <summary>The kn5 node of every built transform (reverse lookup).</summary>
        public readonly Dictionary<Transform, Kn5Node> NodeOf = new Dictionary<Transform, Kn5Node>();
        /// <summary>Shared textures/materials (released by <see cref="Dispose"/>).</summary>
        public AcMatScope Scope;
        /// <summary>GPU bytes of the textures of this model's scope (shared with other builds of the same kn5 + skin).</summary>
        public long TextureBytes;
        public int Triangles, SkinOverrides, MipsMade, TextureFailures;
        public long PrepareMs, UploadMs;

        /// <summary>Node transform by name (null when absent). Case-sensitive like AC (WHEEL_LF; the miata has lowercase wiper_L).</summary>
        public Transform Find(string name) { Transform t; return name != null && Nodes.TryGetValue(name, out t) ? t : null; }

        /// <summary>Destroys the tree, its meshes, materials and textures that only it uses.</summary>
        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            for (int i = 0; i < Meshes.Count; i++) if (Meshes[i] != null) UnityEngine.Object.Destroy(Meshes[i]);
            Meshes.Clear();
            if (Scope != null) { AcMaterials.Release(Scope); Scope = null; }
            Root = null; Nodes.Clear(); Renderers.Clear(); Colliders.Clear(); RendererByNode.Clear(); NodeOf.Clear();
        }
    }

    /// <summary>
    /// [W, World] The generic kn5 -> GameObject builder (used by W for tracks and the hub showroom, by C for cars): node matrices via
    /// AcMat4.ToUnity (U = S * transpose(M) * S), X mirror + winding flip (AcMeshPrepare), materials -> the World shaders (AcMaterials),
    /// textures raw DDS upload (AcTextures, uv = (u, v_stored)), skin overrides by file name, LOD in/out (AcLodDriver), inactive nodes hidden,
    /// skinned meshes (shifter boots, drivers) as SkinnedMeshRenderers with the kn5's stored bind matrices. The heavy part (reading, converting,
    /// tangents, texture decode / mips) runs on a worker in <see cref="BuildAsync"/> (<see cref="AcModelJob"/>); the main thread only uploads.
    /// </summary>
    public static class AcKn5Builder
    {
        /// <summary>Builds the whole file at once (lab / small files). <paramref name="kn5"/> may be read Full, NoTextures or HeaderOnly:
        /// whatever is missing is read by file offset. Meshes must NOT be pre-converted (ConvertToUnity false).</summary>
        public static AcKn5Model Build(Kn5File kn5, Transform parent, AcKn5BuildOptions options)
        {
            if (kn5 == null) return null;
            if (options == null) options = new AcKn5BuildOptions();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scope = AcMaterials.Acquire(ScopeKey(kn5, options));
            var job = MakeJob(kn5, options, scope);
            job.Run();
            if (job.Failed) { AcLog.Warn("Kn5: " + System.IO.Path.GetFileName(kn5.Path) + " failed: " + job.Error); AcMaterials.Release(scope); return null; }
            var model = NewModel(kn5, parent, options, scope);
            model.PrepareMs = job.Ms;
            var b = new Upload(model, job, options);
            b.Nodes(int.MaxValue);
            while (!b.Textures(float.MaxValue)) { }
            while (!b.MeshesStep(float.MaxValue)) { }
            b.Finish(true);
            model.UploadMs = sw.ElapsedMilliseconds - job.Ms;
            Report(model, job);
            return model;
        }

        /// <summary>Builds time-sliced (<see cref="AcKn5BuildOptions.SliceMs"/> per frame); <paramref name="done"/> gets the model (null on failure).
        /// Run with StartCoroutine. Reports progress 0..1.</summary>
        public static IEnumerator BuildAsync(Kn5File kn5, Transform parent, AcKn5BuildOptions options, Action<float> progress, Action<AcKn5Model> done)
        {
            if (kn5 == null) { Done(done, null); yield break; }
            if (options == null) options = new AcKn5BuildOptions();
            var scope = AcMaterials.Acquire(ScopeKey(kn5, options));
            var job = MakeJob(kn5, options, scope);
            ThreadPool.QueueUserWorkItem(RunJob, job);
            while (!job.Done)
            {
                Progress(progress, 0.05f);
                yield return null;
            }
            if (job.Failed)
            {
                AcLog.Warn("Kn5: " + System.IO.Path.GetFileName(kn5.Path) + " failed: " + job.Error);
                AcMaterials.Release(scope);
                Done(done, null);
                yield break;
            }
            AcKn5Model model = null;
            Upload b = null;
            try
            {
                model = NewModel(kn5, parent, options, scope);
                model.PrepareMs = job.Ms;
                b = new Upload(model, job, options);
            }
            catch (Exception e) { AcLog.Warn("Kn5: " + System.IO.Path.GetFileName(kn5.Path) + " build failed: " + e.Message); AcMaterials.Release(scope); Done(done, null); yield break; }
            float slice = Mathf.Max(1f, options.SliceMs);
            int stage = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (stage < 4)
            {
                if (model.Root == null) { model.Dispose(); Done(done, null); yield break; }   // parent destroyed meanwhile
                float t0 = Time.realtimeSinceStartup * 1000f;
                bool more = true;
                try
                {
                    if (stage == 0) { if (b.Nodes(200)) stage = 1; }                         // ours: nodes per step
                    else if (stage == 1) { if (b.Textures(slice)) stage = 2; }
                    else if (stage == 2) { if (b.MeshesStep(slice)) stage = 3; }
                    else { if (b.Finish(false)) { stage = 4; more = false; } }
                }
                catch (Exception e)
                {
                    AcLog.Warn("Kn5: " + System.IO.Path.GetFileName(kn5.Path) + " upload failed: " + e.Message);
                    model.Dispose();
                    Done(done, null);
                    yield break;
                }
                Progress(progress, 0.1f + 0.9f * b.Fraction);
                if (more && Time.realtimeSinceStartup * 1000f - t0 >= 0f) yield return null;
            }
            model.UploadMs = sw.ElapsedMilliseconds;
            Report(model, job);
            Progress(progress, 1f);
            Done(done, model);
        }

        static void RunJob(object o) { ((AcModelJob)o).Run(); }
        static void Progress(Action<float> p, float f) { if (p == null) return; try { p(f); } catch (Exception e) { AcLog.Warn("Kn5: progress callback: " + e.Message); } }
        static void Done(Action<AcKn5Model> d, AcKn5Model m) { if (d == null) return; try { d(m); } catch (Exception e) { AcLog.Warn("Kn5: done callback: " + e.Message); } }

        static string ScopeKey(Kn5File kn5, AcKn5BuildOptions o)
        {
            if (!string.IsNullOrEmpty(o.ScopeKey)) return o.DamageSlots ? o.ScopeKey + "|damage" : o.ScopeKey;
            string src = o.TextureSource != null ? o.TextureSource.Path : kn5.Path;
            return "model|" + (src ?? "").ToLowerInvariant() + "|" + (o.SkinDir ?? "").ToLowerInvariant() + (o.DamageSlots ? "|damage" : "");
        }

        static AcModelJob MakeJob(Kn5File kn5, AcKn5BuildOptions o, AcMatScope scope)
        {
            var skip = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in scope.Textures) skip.Add(kv.Key);
            bool[] skipped = null;
            if (o.Skip != null)
            {
                skipped = new bool[kn5.Nodes.Count];
                for (int i = 0; i < kn5.Nodes.Count; i++)
                {
                    var n = kn5.Nodes[i];
                    bool s = n.Parent != null && skipped[n.Parent.Index];
                    if (!s) { try { s = o.Skip(n); } catch (Exception) { s = false; } }
                    skipped[i] = s;
                }
            }
            return new AcModelJob
            {
                Kn5 = kn5, SkinDir = o.SkinDir, TextureFallback = o.TextureSource, PhysicsColliders = o.PhysicsColliders, CollideRendered = o.CollideRendered,
                HideMarkers = o.HideMarkers, MakeMips = o.MakeMips, Readable = o.Readable, Surfaces = o.Surfaces, SkipTextures = skip, Skipped = skipped,
                DamageSlots = o.DamageSlots,
            };
        }

        static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static AcKn5Model NewModel(Kn5File kn5, Transform parent, AcKn5BuildOptions o, AcMatScope scope)
        {
            var go = new GameObject(System.IO.Path.GetFileName(kn5.Path) ?? "kn5");
            go.layer = o.Layer;
            go.transform.SetParent(parent, false);
            return new AcKn5Model { Root = go.transform, Kn5 = kn5, Scope = scope };
        }

        static void Report(AcKn5Model m, AcModelJob job)
        {
            m.SkinOverrides = job.SkinOverrides;
            for (int i = 0; i < job.Textures.Count; i++) if (job.Textures[i].MipsMade) m.MipsMade++;
            if (m.Scope != null) m.TextureBytes = m.Scope.TextureBytes;
            // one line per kn5 file and visit (a car is built again for every spawn)
            if (!Reported.Add((m.Kn5.Path ?? "") + "|" + (m.Scope != null ? m.Scope.Key : ""))) return;
            AcLog.Info("Kn5: built " + System.IO.Path.GetFileName(m.Kn5.Path) + ": " + m.Renderers.Count + " renderers, " + m.Triangles + " tris, " + m.Colliders.Count + " colliders, " +
                       job.Textures.Count + " textures read (" + m.SkinOverrides + " from the skin, mips made " + m.MipsMade + ", failed " + m.TextureFailures + "), prepare " + m.PrepareMs +
                       " ms, upload " + m.UploadMs + " ms" + (job.Problems.Count > 0 ? "; problems: " + string.Join("; ", job.Problems.GetRange(0, Math.Min(5, job.Problems.Count)).ToArray()) : ""));
        }

        // ------------------------------------------------------------------ the main-thread upload of one model

        sealed class Upload
        {
            readonly AcKn5Model _m;
            readonly AcModelJob _job;
            readonly AcKn5BuildOptions _o;
            readonly Transform[] _nodeT;
            int _nodeAt, _texAt, _meshAt;
            readonly List<KeyValuePair<MeshCollider, AcCooker.Ticket>> _cooking = new List<KeyValuePair<MeshCollider, AcCooker.Ticket>>();
            readonly List<KeyValuePair<MeshCollider, Mesh>> _cookMeshes = new List<KeyValuePair<MeshCollider, Mesh>>();
            readonly List<KeyValuePair<AcMeshPrep, SkinnedMeshRenderer>> _skinned = new List<KeyValuePair<AcMeshPrep, SkinnedMeshRenderer>>();
            AcLodDriver _lod;
            bool _boundsSet;

            public Upload(AcKn5Model m, AcModelJob job, AcKn5BuildOptions o)
            {
                _m = m; _job = job; _o = o;
                _nodeT = new Transform[m.Kn5.Nodes.Count];
            }

            public float Fraction
            {
                get
                {
                    float total = _nodeT.Length + _job.Textures.Count * 4 + _job.Meshes.Count * 2 + 1;
                    return Mathf.Clamp01((_nodeAt + _texAt * 4 + _meshAt * 2) / total);
                }
            }

            /// <summary>Creates up to <paramref name="max"/> node GameObjects (depth-first; parents first). True when all exist.</summary>
            public bool Nodes(int max)
            {
                var k = _m.Kn5;
                int n = 0;
                while (_nodeAt < k.Nodes.Count && n < max)
                {
                    var node = k.Nodes[_nodeAt];
                    Transform parent = node.Parent != null ? _nodeT[node.Parent.Index] : _m.Root;
                    if (parent == null) parent = _m.Root;
                    bool skip = (_o.HideMarkers && node.Mesh != null && AcTrackMarkers.IsMarkerCube(node)) || (_job.Skipped != null && _job.Skipped[_nodeAt]);
                    if (!skip)
                    {
                        var go = new GameObject(node.Name);
                        go.layer = _o.Layer;
                        var t = go.transform;
                        t.SetParent(parent, false);
                        if (node.IsDummy) AcKn5Upload.SetLocal(t, node.Local);
                        if (!node.Active) go.SetActive(false);
                        _nodeT[_nodeAt] = t;
                        if (!_m.Nodes.ContainsKey(node.Name)) _m.Nodes[node.Name] = t;
                        _m.NodeOf[t] = node;
                    }
                    _nodeAt++; n++;
                }
                return _nodeAt >= k.Nodes.Count;
            }

            public bool Textures(float budgetMs)
            {
                float t0 = Time.realtimeSinceStartup * 1000f;
                var scope = _m.Scope;
                while (_texAt < _job.Textures.Count)
                {
                    var p = _job.Textures[_texAt++];
                    string key = (p.Name ?? "").ToLowerInvariant();
                    if (scope.Textures.ContainsKey(key)) continue;
                    var tex = AcTextures.Upload(p, System.IO.Path.GetFileName(_m.Kn5.Path));
                    scope.Textures[key] = tex;
                    scope.TextureCount++;
                    if (tex == null) { scope.TextureFailures++; _m.TextureFailures++; }
                    else scope.TextureBytes += AcTextures.BytesOf(p);
                    p.Data = null;   // let the GC have it
                    if (Time.realtimeSinceStartup * 1000f - t0 > budgetMs) return _texAt >= _job.Textures.Count;
                }
                return true;
            }

            Texture2D Tex(string file)
            {
                return file == null ? null : _m.Scope.Tex(file.ToLowerInvariant());
            }

            Material MaterialFor(AcMeshPrep p)
            {
                var k = _m.Kn5;
                int id = p.Node.Mesh.MaterialId;
                string key = (k.Path ?? "").ToLowerInvariant() + "|" + id;
                Material mat;
                if (_m.Scope.Materials.TryGetValue(key, out mat) && mat != null) return mat;
                var sh = id >= 0 && id < _job.Shading.Length ? _job.Shading[id] : null;
                mat = AcMaterials.Create(p.Material, sh, Tex);
                _m.Scope.Materials[key] = mat;
                return mat;
            }

            public bool MeshesStep(float budgetMs)
            {
                float t0 = Time.realtimeSinceStartup * 1000f;
                while (_meshAt < _job.Meshes.Count)
                {
                    var p = _job.Meshes[_meshAt++];
                    var t = _nodeT[p.Node.Index];
                    if (t != null) BuildMesh(p, t);
                    p.Vertices = null; p.ColliderPositions = null; p.Indices = null;
                    if (Time.realtimeSinceStartup * 1000f - t0 > budgetMs) return _meshAt >= _job.Meshes.Count;
                }
                return true;
            }

            void BuildMesh(AcMeshPrep p, Transform t)
            {
                if (p.Render)
                {
                    var mat = MaterialFor(p);
                    Renderer r;
                    if (p.Skinned)
                    {
                        var mesh = AcKn5Upload.SkinnedMesh(p, true);
                        _m.Meshes.Add(mesh);
                        // skinned vertices are in the kn5 ROOT space with the stored bind matrices: the renderer sits on the model root
                        var go = new GameObject(p.Node.Name + " (skinned)");
                        go.layer = _o.Layer;
                        go.transform.SetParent(_m.Root, false);
                        var smr = go.AddComponent<SkinnedMeshRenderer>();
                        smr.sharedMesh = mesh;
                        smr.updateWhenOffscreen = false;
                        r = smr;
                        _skinned.Add(new KeyValuePair<AcMeshPrep, SkinnedMeshRenderer>(p, smr));
                        if (!t.gameObject.activeInHierarchy) go.SetActive(false);
                    }
                    else
                    {
                        bool readable = _o.Readable;
                        if (!readable && _o.ReadableFilter != null) { try { readable = _o.ReadableFilter(p.Node); } catch (Exception) { readable = false; } }
                        var mesh = AcKn5Upload.RenderMesh(p, readable);
                        _m.Meshes.Add(mesh);
                        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                        r = t.gameObject.AddComponent<MeshRenderer>();
                    }
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = _o.CastShadows && p.Node.Mesh.CastShadows && !(mat != null && mat.renderQueue >= AcMaterials.QueueTransparent) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    _m.Renderers.Add(r);
                    if (!_m.RendererByNode.ContainsKey(p.Node.Name)) _m.RendererByNode[p.Node.Name] = r;
                    _m.Triangles += p.IndexCount / 3;
                    var km = p.Node.Mesh;
                    if (km.LodOut > 0f)
                    {
                        if (_lod == null) _lod = _m.Root.gameObject.AddComponent<AcLodDriver>();
                        _lod.Add(r, new Vector3((p.MinX + p.MaxX) * 0.5f, (p.MinY + p.MaxY) * 0.5f, (p.MinZ + p.MaxZ) * 0.5f), km.LodIn, km.LodOut);
                    }
                }
                if (p.Collide && p.ColliderPositions != null)
                {
                    var cm = AcKn5Upload.ColliderMesh(p);
                    _m.Meshes.Add(cm);
                    var cgo = new GameObject(p.Node.Name + " (collider)");
                    cgo.layer = _o.ColliderLayer;
                    cgo.transform.SetParent(t, false);
                    var mc = cgo.AddComponent<MeshCollider>();
                    _m.Colliders.Add(mc);
                    if (_o.PhysicsColliders && AcSurfaces.IsPhysicsName(p.Node.Name)) AcSurfaceTag.Register(mc, p.Surface ?? (_o.Surfaces != null ? _o.Surfaces.Default : null), p.Wall);
                    var ticket = AcCooker.Queue(cm);
                    if (ticket.Done) mc.sharedMesh = cm;
                    else { _cooking.Add(new KeyValuePair<MeshCollider, AcCooker.Ticket>(mc, ticket)); _cookMeshes.Add(new KeyValuePair<MeshCollider, Mesh>(mc, cm)); }
                }
            }

            /// <summary>Bones, colliders whose cooking finished, bounds. True when everything is done (<paramref name="wait"/>: block until cooked).</summary>
            public bool Finish(bool wait)
            {
                for (int i = 0; i < _skinned.Count; i++) Bind(_skinned[i].Key, _skinned[i].Value);
                _skinned.Clear();
                for (int i = _cooking.Count - 1; i >= 0; i--)
                {
                    var tk = _cooking[i].Value;
                    if (!tk.Done && !wait) continue;
                    while (!tk.Done) Thread.Sleep(1);
                    var mc = _cooking[i].Key;
                    if (mc != null) mc.sharedMesh = _cookMeshes[i].Value;
                    _cooking.RemoveAt(i); _cookMeshes.RemoveAt(i);
                }
                if (_cooking.Count > 0) return false;
                if (!_boundsSet)
                {
                    _boundsSet = true;
                    bool any = false;
                    var bb = new Bounds();
                    for (int i = 0; i < _m.Renderers.Count; i++)
                    {
                        var r = _m.Renderers[i];
                        if (r == null) continue;
                        if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                    }
                    _m.Bounds = bb;
                    if (_lod != null) _lod.SweepAll();
                }
                return true;
            }

            void Bind(AcMeshPrep p, SkinnedMeshRenderer smr)
            {
                var bones = p.Node.Mesh.Bones;
                if (bones == null) return;
                var bt = new Transform[bones.Length];
                var bp = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    bt[i] = _m.Find(bones[i].Name);
                    if (bt[i] == null) bt[i] = _m.Root;
                    bp[i] = AcKn5Upload.ToMatrix(bones[i].InverseBind);
                }
                smr.sharedMesh.bindposes = bp;
                smr.bones = bt;
                smr.rootBone = _m.Root;
                smr.localBounds = new Bounds(new Vector3((p.MinX + p.MaxX) * 0.5f, (p.MinY + p.MaxY) * 0.5f, (p.MinZ + p.MaxZ) * 0.5f),
                    new Vector3(p.MaxX - p.MinX + 0.5f, p.MaxY - p.MinY + 0.5f, p.MaxZ - p.MinZ + 0.5f));
            }
        }
    }

    /// <summary>Shared main-thread pieces of the kn5 upload (models and tracks).</summary>
    public static class AcKn5Upload
    {
        static readonly VertexAttributeDescriptor[] LayoutPlain =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
        static readonly VertexAttributeDescriptor[] LayoutTangent =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
        static readonly VertexAttributeDescriptor[] LayoutPosition =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        };
        const MeshUpdateFlags Fast = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;

        public static Bounds BoundsOf(AcMeshPrep p)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(p.MinX, p.MinY, p.MinZ), new Vector3(p.MaxX, p.MaxY, p.MaxZ));
            return b;
        }

        /// <summary>The render mesh of a prepared kn5 mesh (non-readable unless <paramref name="readable"/>).</summary>
        public static Mesh RenderMesh(AcMeshPrep p, bool readable)
        {
            var mesh = new Mesh { name = p.Node.Name };
            mesh.SetVertexBufferParams(p.VertexCount, p.HasTangents ? LayoutTangent : LayoutPlain);
            mesh.SetVertexBufferData(p.Vertices, 0, 0, p.VertexCount * p.Stride, 0, Fast);
            SetIndices(mesh, p);
            mesh.UploadMeshData(!readable);
            return mesh;
        }

        /// <summary>A position-only readable mesh for a MeshCollider.</summary>
        public static Mesh ColliderMesh(AcMeshPrep p)
        {
            var mesh = new Mesh { name = p.Node.Name + " (collision)" };
            mesh.SetVertexBufferParams(p.VertexCount, LayoutPosition);
            mesh.SetVertexBufferData(p.ColliderPositions, 0, 0, p.VertexCount * 3, 0, Fast);
            SetIndices(mesh, p);
            return mesh;
        }

        static void SetIndices(Mesh mesh, AcMeshPrep p)
        {
            var b = BoundsOf(p);
            mesh.SetIndexBufferParams(p.IndexCount, IndexFormat.UInt16);
            mesh.SetIndexBufferData(p.Indices, 0, 0, p.IndexCount, Fast);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, p.IndexCount, MeshTopology.Triangles) { firstVertex = 0, vertexCount = p.VertexCount, bounds = b }, Fast);
            mesh.bounds = b;
        }

        /// <summary>A skinned mesh (classic API; rare and small: shifter boots, drivers). Bind poses are set when the bones exist.</summary>
        public static Mesh SkinnedMesh(AcMeshPrep p, bool readable)
        {
            int vc = p.VertexCount, st = p.Stride;
            var pos = new Vector3[vc]; var nrm = new Vector3[vc]; var uv = new Vector2[vc]; var bw = new BoneWeight[vc];
            int uvAt = p.HasTangents ? 10 : 6;
            for (int i = 0; i < vc; i++)
            {
                int o = i * st;
                pos[i] = new Vector3(p.Vertices[o], p.Vertices[o + 1], p.Vertices[o + 2]);
                nrm[i] = new Vector3(p.Vertices[o + 3], p.Vertices[o + 4], p.Vertices[o + 5]);
                uv[i] = new Vector2(p.Vertices[o + uvAt], p.Vertices[o + uvAt + 1]);
                var w = new BoneWeight();
                if (p.SkinWeights != null)
                {
                    int s = i * 4;
                    w.boneIndex0 = Mathf.Max(0, p.SkinBones[s]); w.weight0 = p.SkinWeights[s];
                    w.boneIndex1 = Mathf.Max(0, p.SkinBones[s + 1]); w.weight1 = p.SkinWeights[s + 1];
                    w.boneIndex2 = Mathf.Max(0, p.SkinBones[s + 2]); w.weight2 = p.SkinWeights[s + 2];
                    w.boneIndex3 = Mathf.Max(0, p.SkinBones[s + 3]); w.weight3 = p.SkinWeights[s + 3];
                    float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                    if (sum > 1e-5f) { w.weight0 /= sum; w.weight1 /= sum; w.weight2 /= sum; w.weight3 /= sum; } else { w.boneIndex0 = 0; w.weight0 = 1f; }
                }
                else { w.boneIndex0 = 0; w.weight0 = 1f; }
                bw[i] = w;
            }
            var tri = new int[p.IndexCount];
            for (int i = 0; i < p.IndexCount; i++) tri[i] = p.Indices[i];
            var mesh = new Mesh { name = p.Node.Name };
            mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv;
            mesh.triangles = tri;
            mesh.boneWeights = bw;
            mesh.RecalculateTangents();   // normal-mapped skinned materials (driver suits): Unity's tangents, cross(N, T) * w = dP/dv like ours
            mesh.bounds = BoundsOf(p);
            return mesh;
        }

        /// <summary>AC row-vector matrix -> Unity column-vector Matrix4x4 (U = S * transpose(M) * S).</summary>
        public static Matrix4x4 ToMatrix(AcMat4 m)
        {
            var a = m.ToUnity();
            var u = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) u[r, c] = a[r * 4 + c];
            return u;
        }

        static int _mirroredLogged;

        /// <summary>Sets a transform's local position / rotation / scale from an AC local matrix (mirrored dummies: negative x scale).</summary>
        public static void SetLocal(Transform t, AcMat4 m)
        {
            var a = m.ToUnity();
            Vector3 c0 = new Vector3(a[0], a[4], a[8]), c1 = new Vector3(a[1], a[5], a[9]), c2 = new Vector3(a[2], a[6], a[10]);
            Vector3 s = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
            if (m.Determinant3 < 0f)
            {
                s.x = -s.x;
                if (_mirroredLogged++ == 0) AcLog.Info("Kn5: mirrored dummy '" + t.name + "' (negative scale kept)");
            }
            t.localPosition = new Vector3(a[3], a[7], a[11]);
            if (s.y > 1e-8f && s.z > 1e-8f) t.localRotation = Quaternion.LookRotation(c2 / s.z, c1 / s.y);
            if (Mathf.Abs(s.x - 1f) > 1e-4f || Mathf.Abs(s.y - 1f) > 1e-4f || Mathf.Abs(s.z - 1f) > 1e-4f) t.localScale = s;
        }
    }
}
