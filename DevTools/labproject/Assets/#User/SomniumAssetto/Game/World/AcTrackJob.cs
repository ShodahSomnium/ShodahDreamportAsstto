using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    public enum AcShaderKind { PerPixel, Multilayer, Unlit }

    /// <summary>
    /// How a kn5 material is drawn (Unity-free; shared by the worker - which textures to read, which meshes need tangents - and by
    /// AcMaterials on the main thread). Shader names and property names are AC's own (ac_track_formats 3.5, ac_car_formats 1.3).
    /// </summary>
    public sealed class AcShading
    {
        public AcShaderKind Kind;
        public bool AlphaTest, Blend, NoDepthWrite, NormalMap, Maps, Detail, DetailNormal, Reflection, ReflectionAdd;
        public bool Grass, Tree, Flag, ObjectSpaceLayers, LayerNormal;
        /// <summary>Iteration 2 (DAMAGE_DESIGN 2.1): ksPerPixelMultiMap_damage(_dirt) with the damage slots txDamage / txDamageMask read
        /// (only when the car was built with damage textures wanted). <see cref="DamageScalar"/>: the older ksPerPixelMultiMap_damage (one
        /// scalar, no mask: drawn with a white mask). <see cref="BrokenGlass"/>: ksBrokenGlass (always drawn with alpha x glassDamage, G0).</summary>
        public bool DamagePaint, DamageScalar, BrokenGlass;
        public float AlphaRef;
        /// <summary>Texture slots the shader samples (only these are read from the file).</summary>
        public readonly List<string> Slots = new List<string>(6);

        /// <summary>Alpha-test threshold when ksAlphaRef is 0 on an alpha-tested material (sponsors-AT has 0). // ours [BELIEF]</summary>
        public const float DefaultAlphaRef = 0.5f;

        public static bool HasSlot(Kn5Material m, string slot) { return !string.IsNullOrEmpty(m.Texture(slot)); }

        public static AcShading Of(Kn5Material m) { return Of(m, false); }

        /// <summary>As <see cref="Of(Kn5Material)"/>; <paramref name="damageSlots"/> = the car is built with damage textures (AcCarDamageGate
        /// at the car's spawn, passed by C through <see cref="AcKn5BuildOptions.DamageSlots"/>): damage paint reads txDamage + txDamageMask
        /// (txDust is not read - dirt is out of scope) and ksBrokenGlass gets a normal map (the install's DAMAGE_GLASS.dds, bound by
        /// AcMaterials - never the kn5's 64 px proxy). Off = exactly iteration 1's slots.</summary>
        public static AcShading Of(Kn5Material m, bool damageSlots)
        {
            var s = new AcShading();
            if (m == null) { s.Slots.Add("txDiffuse"); return s; }
            string sh = m.Shader ?? "";
            s.Grass = sh == "ksGrass";
            s.Tree = sh == "ksTree";
            s.Flag = sh == "ksFlags";
            s.Blend = m.BlendMode == 1;
            s.NoDepthWrite = m.DepthMode == 1;
            s.AlphaTest = !s.Blend && (m.AlphaTested || m.BlendMode == 2);
            float aref = m.Float("ksAlphaRef", 0f);
            s.AlphaRef = aref > 0f ? aref : DefaultAlphaRef;
            if (sh.StartsWith("ksMultilayer", StringComparison.Ordinal))
            {
                s.Kind = AcShaderKind.Multilayer;
                s.ObjectSpaceLayers = sh.IndexOf("objsp", StringComparison.Ordinal) >= 0;
                s.Slots.Add("txDiffuse"); s.Slots.Add("txMask");
                s.Slots.Add("txDetailR"); s.Slots.Add("txDetailG"); s.Slots.Add("txDetailB"); s.Slots.Add("txDetailA");
                // detail normal map: ksMultilayer_fresnel_nm, scaled by detailNMMult (A; 0 = off - magione asphalt has 0)
                s.LayerNormal = HasSlot(m, "txDetailNM") && m.Float("detailNMMult", 0f) > 0f;
                if (s.LayerNormal) s.Slots.Add("txDetailNM");
                s.NormalMap = HasSlot(m, "txNormal");
                if (s.NormalMap) s.Slots.Add("txNormal");
                s.Reflection = m.Float("fresnelMaxLevel", 0f) > 0f;
                return s;
            }
            s.Kind = sh == "GL" ? AcShaderKind.Unlit : AcShaderKind.PerPixel;
            s.Slots.Add("txDiffuse");
            if (s.Kind == AcShaderKind.Unlit) return s;
            s.BrokenGlass = sh == "ksBrokenGlass";
            if (s.BrokenGlass) s.NormalMap = damageSlots;   // the crack map comes from content/texture (AcMaterials), not a kn5 slot
            else
            {
                s.NormalMap = HasSlot(m, "txNormal") && m.Float("nmObjectSpace", 0f) < 0.5f;
                if (s.NormalMap) s.Slots.Add("txNormal");
            }
            if (damageSlots && sh.StartsWith("ksPerPixelMultiMap_damage", StringComparison.Ordinal))
            {
                s.DamagePaint = true;
                s.DamageScalar = sh == "ksPerPixelMultiMap_damage";
                if (HasSlot(m, "txDamage")) s.Slots.Add("txDamage");
                if (!s.DamageScalar && HasSlot(m, "txDamageMask")) s.Slots.Add("txDamageMask");
            }
            s.Maps = HasSlot(m, "txMaps") && sh.IndexOf("MultiMap", StringComparison.Ordinal) >= 0;
            if (s.Maps) s.Slots.Add("txMaps");
            s.Detail = HasSlot(m, "txDetail") && m.Float("useDetail", 0f) > 0.5f;
            if (s.Detail) s.Slots.Add("txDetail");
            s.DetailNormal = HasSlot(m, "txNormalDetail") && s.NormalMap;
            if (s.DetailNormal) s.Slots.Add("txNormalDetail");
            s.Reflection = m.Float("fresnelMaxLevel", 0f) > 0f;
            s.ReflectionAdd = m.Float("isAdditive", 0f) > 0.5f;
            return s;
        }

        /// <summary>The mesh needs tangents (any normal map).</summary>
        public bool NeedsTangents { get { return NormalMap || DetailNormal || LayerNormal; } }
    }

    /// <summary>Options of a track job.</summary>
    public sealed class AcTrackJobOptions
    {
        /// <summary>Draw ksGrass meshes (3D grass tufts: monza 1.kn5 = 1.9 M vertices). Setting "world.grass".</summary>
        public bool Grass = true;
        /// <summary>Make mips for textures stored with one level.</summary>
        public bool MakeMips = true;
        /// <summary>Prepared bytes allowed to wait for the main thread before the worker pauses. // ours</summary>
        public long MaxQueuedBytes = 256L << 20;
        /// <summary>Weather folder name under content/weather (lighting colours, fog). Setting "world.weather". // ours default</summary>
        public string Weather = "4_mid_clear";
        public string SystemDir;
        public string WeatherDir;
        public string SystemSurfacesIni;
    }

    /// <summary>A texture to prepare: which file holds it (kn5 index or a skin file path).</summary>
    public sealed class AcTexRef
    {
        public string Key;          // "<file>|<name lower>"
        public int File;            // kn5 index of the layout (track) / 0
        public Kn5Texture Texture;  // null for a disk file
        public string DiskPath;     // skin override
        public string Name;
    }

    /// <summary>One produced item (texture or mesh), in production order per kn5: its textures first, then its meshes.</summary>
    public sealed class AcJobItem
    {
        public int File;
        public AcTexRef TexRef;
        public AcTexPrep Tex;
        public AcMeshPrep Mesh;
        public long Bytes;
    }

    /// <summary>
    /// The worker side of building a track layout (Unity-free: the dotnet harness runs it too). Phase 1 <see cref="Scan"/>: models.ini,
    /// header-only scan of every kn5 (offsets only, research section 7), surfaces (layout over system), markers, AI lines, lighting /
    /// weather / starting-lights inis, the list of meshes to draw / collide and the textures their materials use. Phase 2 <see cref="Produce"/>:
    /// per kn5 in load order, read + prepare its used textures, then read + convert its meshes (one stream per file, ascending offsets),
    /// handing items to the main thread through a bounded queue (<see cref="TryTake"/>).
    /// </summary>
    public sealed class AcTrackJob
    {
        public readonly AcLayoutEntry Layout;
        public readonly AcTrackJobOptions Options;

        // ---- phase 1 results (read-only after ScanDone)
        public AcModelsIni Models;
        public readonly List<Kn5File> Files = new List<Kn5File>();
        public readonly List<AcTrackModel> Placements = new List<AcTrackModel>();
        public AcSurfaces Surfaces;
        public AcTrackMarkers Markers;
        public AcAiSpline FastLane, PitLane;
        public AcIni LightingIni, StartingLightsIni, WeatherIni, ColorCurvesIni, GrooveIni, SemaphoreIni;
        public string WeatherUsed;
        /// <summary>Per kn5: material shading (index = material id).</summary>
        public readonly List<AcShading[]> Shading = new List<AcShading[]>();
        /// <summary>Per kn5: the effective active flag of every node (an inactive ancestor hides the subtree).</summary>
        public readonly List<bool[]> NodeActive = new List<bool[]>();
        public int MeshesTotal, MeshesRendered, MeshesCollide, Walls, MarkerCubes, GrassSkipped, InactiveHidden, TexturesTotal;
        public long VerticesRendered, TrianglesRendered, TrianglesCollide;
        public readonly List<string> Problems = new List<string>();

        // ---- work lists
        readonly List<List<Kn5Node>> _meshes = new List<List<Kn5Node>>();
        readonly List<List<AcTexRef>> _textures = new List<List<AcTexRef>>();

        // ---- state shared with the main thread
        readonly Queue<AcJobItem> _queue = new Queue<AcJobItem>();
        readonly object _lock = new object();
        long _queuedBytes;
        volatile bool _cancel;
        public volatile bool ScanDone, Done, Failed;
        public volatile string Error;
        public volatile int Produced;
        public int WorkTotal;
        public long ScanMs, ProduceMs, TextureBytesRead, PreparedTextureBytes;
        public int MipsMade;

        public AcTrackJob(AcLayoutEntry layout, AcTrackJobOptions options)
        {
            Layout = layout;
            Options = options ?? new AcTrackJobOptions();
        }

        public void Cancel() { _cancel = true; lock (_lock) Monitor.PulseAll(_lock); }
        public bool Cancelled { get { return _cancel; } }

        /// <summary>Worker entry point: scan, then produce. Never throws (Failed + Error).</summary>
        public void Run()
        {
            try
            {
                Scan();
                ScanDone = true;
                if (!_cancel && !Failed) Produce();
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                Failed = true;
            }
            finally
            {
                ScanDone = true;
                Done = true;
                lock (_lock) Monitor.PulseAll(_lock);
            }
        }

        // ------------------------------------------------------------------ phase 1

        public void Scan()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var L = Layout;
            Models = AcModelsIni.Load(L.TrackDir, L.Id);
            Surfaces = AcSurfaces.Load(L.SurfacesIni, Options.SystemSurfacesIni);
            for (int i = 0; i < Models.Models.Count; i++)
            {
                var m = Models.Models[i];
                if (!m.Exists) { Problems.Add("missing " + m.File); continue; }
                Kn5File k;
                try { k = Kn5File.Read(m.FullPath, Kn5ReadOptions.HeaderOnly); }
                catch (Exception e) { Problems.Add(m.File + ": " + e.Message); continue; }
                Files.Add(k);
                Placements.Add(m);
                if (_cancel) return;
            }
            if (Files.Count == 0) { Failed = true; Error = "no readable kn5 in " + (Models.Path ?? L.TrackDir); return; }
            Markers = AcTrackMarkers.Collect(Files, Placements, Surfaces);
            try { if (File.Exists(L.FastLanePath)) FastLane = AcAiSpline.Read(L.FastLanePath); } catch (Exception e) { Problems.Add("fast_lane.ai: " + e.Message); }
            try { if (File.Exists(L.PitLanePath)) PitLane = AcAiSpline.Read(L.PitLanePath); } catch (Exception e) { Problems.Add("pit_lane.ai: " + e.Message); }
            if (FastLane != null && FastLane.IsEmpty) FastLane = null;   // drift: v3 file with 0 points
            if (PitLane != null && PitLane.IsEmpty) PitLane = null;
            LightingIni = AcIni.Load(Path.Combine(L.DataDir ?? "", "lighting.ini"));
            StartingLightsIni = AcIni.Load(Path.Combine(L.DataDir ?? "", "startinglights.ini"));
            GrooveIni = AcIni.Load(Path.Combine(L.DataDir ?? "", "groove.ini"));
            SemaphoreIni = AcIni.Load(Path.Combine(L.DataDir ?? "", "semaphore.ini"));   // drag / A2B start tree (ks_drag, trento-bondone)
            LoadWeather();

            // what to draw / collide, which textures
            var texIndex = new Dictionary<string, AcTexRef>(StringComparer.OrdinalIgnoreCase);
            for (int f = 0; f < Files.Count; f++)
            {
                var k = Files[f];
                var shading = new AcShading[k.Materials.Count];
                for (int i = 0; i < shading.Length; i++) shading[i] = AcShading.Of(k.Materials[i]);
                Shading.Add(shading);
                var active = new bool[k.Nodes.Count];
                for (int i = 0; i < k.Nodes.Count; i++)
                {
                    var n = k.Nodes[i];
                    active[i] = n.Active && (n.Parent == null || active[n.Parent.Index]);
                }
                NodeActive.Add(active);
                var meshes = new List<Kn5Node>();
                var texs = new List<AcTexRef>();
                var usedMats = new bool[k.Materials.Count];
                for (int i = 0; i < k.Nodes.Count; i++)
                {
                    var n = k.Nodes[i];
                    if (n.Mesh == null) continue;
                    MeshesTotal++;
                    // marker cubes are never drawn - except the AC_SEMAPHORE_* lamps of a drag / A2B start tree: their meshes ARE the lamps
                    if (AcTrackMarkers.IsMarkerCube(n) && AcTrackMarkers.Classify(n.Name) != AcMarkerKind.Semaphore) { MarkerCubes++; continue; }
                    bool collide = AcSurfaces.IsPhysicsName(n.Name);
                    bool render = n.Mesh.IsRenderable && n.Mesh.IsVisible && n.Mesh.VertexCount > 0 && n.Mesh.IndexCount >= 3;
                    var mat = k.Material(n.Mesh.MaterialId);
                    var sh = n.Mesh.MaterialId >= 0 && n.Mesh.MaterialId < shading.Length ? shading[n.Mesh.MaterialId] : null;
                    if (render && !active[i]) { render = false; InactiveHidden++; }
                    if (render && sh != null && sh.Grass && !Options.Grass) { render = false; GrassSkipped++; }
                    if (!render && !collide) continue;
                    meshes.Add(n);
                    if (render)
                    {
                        MeshesRendered++;
                        VerticesRendered += n.Mesh.VertexCount;
                        TrianglesRendered += n.Mesh.IndexCount / 3;
                        if (mat != null) usedMats[n.Mesh.MaterialId] = true;
                    }
                    if (collide)
                    {
                        MeshesCollide++;
                        TrianglesCollide += n.Mesh.IndexCount / 3;
                        if (AcSurfaces.IsWallName(n.Name)) Walls++;
                    }
                }
                for (int mi = 0; mi < usedMats.Length; mi++)
                {
                    if (!usedMats[mi]) continue;
                    var m = k.Materials[mi];
                    var sl = shading[mi].Slots;
                    for (int s = 0; s < sl.Count; s++)
                    {
                        string tn = m.Texture(sl[s]);
                        if (string.IsNullOrEmpty(tn)) continue;
                        // a texture missing from its own kn5 is looked up in the layout's other files
                        int src = f;
                        var t = k.Texture(tn);
                        if (t == null)
                        {
                            for (int g = 0; g < Files.Count && t == null; g++) { t = Files[g].Texture(tn); if (t != null) src = g; }
                            if (t == null) { Problems.Add("texture " + tn + " (" + m.Name + ") not in any kn5"); continue; }
                        }
                        string key = TexKey(src, tn);
                        if (texIndex.ContainsKey(key)) continue;
                        var r = new AcTexRef { Key = key, File = src, Texture = t, Name = tn };
                        texIndex[key] = r;
                        texs.Add(r);
                    }
                }
                _meshes.Add(meshes);
                _textures.Add(texs);
                TexturesTotal += texs.Count;
                WorkTotal += meshes.Count + texs.Count;
            }
            ScanMs = sw.ElapsedMilliseconds;
        }

        public static string TexKey(int file, string name) { return file.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (name ?? "").ToLowerInvariant(); }

        void LoadWeather()
        {
            string w = string.IsNullOrEmpty(Options.Weather) ? "4_mid_clear" : Options.Weather;
            string dir = Options.WeatherDir != null ? Path.Combine(Options.WeatherDir, w) : null;
            if (dir != null && Directory.Exists(dir))
            {
                WeatherUsed = w;
                ColorCurvesIni = AcIni.Load(Path.Combine(dir, "colorCurves.ini"));
                WeatherIni = AcIni.Load(Path.Combine(dir, "weather.ini"));
            }
            string cfg = Options.SystemDir != null ? Path.Combine(Options.SystemDir, "cfg") : null;
            if (ColorCurvesIni == null && cfg != null) { ColorCurvesIni = AcIni.Load(Path.Combine(cfg, "colorCurves.ini")); WeatherUsed = "system/cfg"; }
            if (WeatherIni == null && cfg != null) WeatherIni = AcIni.Load(Path.Combine(cfg, "weather.ini"));
        }

        // ------------------------------------------------------------------ phase 2

        public void Produce()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < Files.Count && !_cancel; f++)
            {
                var k = Files[f];
                var texs = _textures[f];
                if (texs.Count > 0)
                {
                    using (var r = AcBinary.OpenFile(k.Path, 1 << 16))
                    {
                        for (int i = 0; i < texs.Count && !_cancel; i++)
                        {
                            var tr = texs[i];
                            AcTexPrep p;
                            try
                            {
                                byte[] bytes;
                                if (tr.File == f) { r.Position = tr.Texture.Offset; bytes = r.ReadBytes(tr.Texture.Size); }
                                else bytes = Files[tr.File].ReadTextureBytes(tr.Texture);
                                TextureBytesRead += bytes.Length;
                                p = AcTexPrepare.Prepare(tr.Name, bytes, Options.MakeMips);
                            }
                            catch (Exception e) { p = new AcTexPrep { Name = tr.Name, Problem = e.Message }; }
                            if (p.MipsMade) MipsMade++;
                            PreparedTextureBytes += p.Bytes;
                            Put(new AcJobItem { File = f, TexRef = tr, Tex = p, Bytes = p.Bytes });
                        }
                    }
                }
                var meshes = _meshes[f];
                var shading = Shading[f];
                using (var r = AcBinary.OpenFile(k.Path, 1 << 18))
                {
                    for (int i = 0; i < meshes.Count && !_cancel; i++)
                    {
                        var n = meshes[i];
                        var m = n.Mesh;
                        var p = new AcMeshPrep { Node = n, File = f, Material = k.Material(m.MaterialId) };
                        var sh = m.MaterialId >= 0 && m.MaterialId < shading.Length ? shading[m.MaterialId] : null;
                        p.Render = m.IsRenderable && m.IsVisible && m.VertexCount > 0 && m.IndexCount >= 3 && NodeActive[f][n.Index] && !(sh != null && sh.Grass && !Options.Grass);
                        p.Collide = AcSurfaces.IsPhysicsName(n.Name);
                        if (p.Collide)
                        {
                            bool wall, exact;
                            p.Surface = Surfaces.Match(n.Name, out wall, out exact);
                            p.Wall = wall;
                        }
                        float[] raw = null; ushort[] idx = null;
                        try
                        {
                            int nf = m.VertexCount * m.Stride;
                            raw = AcPool<float>.Rent(nf);
                            idx = AcPool<ushort>.Rent(Math.Max(1, m.IndexCount));
                            r.Position = m.VertexOffset;
                            r.ReadFloats(raw, 0, nf);
                            r.Position = m.IndexOffset;
                            r.ReadUShorts(idx, 0, m.IndexCount);
                            AcMeshPrepare.Fill(p, m, raw, idx, sh != null && sh.NeedsTangents);
                        }
                        catch (Exception e)
                        {
                            Problems.Add(Path.GetFileName(k.Path) + ":" + n.Name + ": " + e.Message);
                            p = null;
                        }
                        finally
                        {
                            AcPool<float>.Return(raw);
                            AcPool<ushort>.Return(idx);
                        }
                        if (p != null) Put(new AcJobItem { File = f, Mesh = p, Bytes = p.Bytes });
                        else Interlocked.Increment(ref Produced);
                    }
                }
            }
            ProduceMs = sw.ElapsedMilliseconds;
        }

        void Put(AcJobItem it)
        {
            lock (_lock)
            {
                while (!_cancel && _queuedBytes > Options.MaxQueuedBytes && _queue.Count > 0) Monitor.Wait(_lock, 200);
                if (_cancel) return;
                _queue.Enqueue(it);
                _queuedBytes += it.Bytes;
            }
            Interlocked.Increment(ref Produced);
        }

        /// <summary>Main thread (or the harness): the next item, if any.</summary>
        public bool TryTake(out AcJobItem it)
        {
            lock (_lock)
            {
                if (_queue.Count == 0) { it = null; return false; }
                it = _queue.Dequeue();
                _queuedBytes -= it.Bytes;
                Monitor.PulseAll(_lock);
                return true;
            }
        }

        public int Queued { get { lock (_lock) return _queue.Count; } }

        /// <summary>Drops whatever is queued (cancel / unload).</summary>
        public void Drain()
        {
            lock (_lock) { _queue.Clear(); _queuedBytes = 0; Monitor.PulseAll(_lock); }
        }
    }
}
