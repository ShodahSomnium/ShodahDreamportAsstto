using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] The disk + math half of a car build, run on a worker thread (no Unity object API; Vector3 math only): data.acd visual
    /// inis + knh + ksanims (<see cref="AcCarVisual"/>), the kn5 of every LOD to build, collider.kn5 as Unity-ready triangles, the model
    /// bounds, the steering rim radius and the car_susp_xx travel mapping. <see cref="AcCarModel.BuildAsync"/> starts it and waits.
    /// </summary>
    public sealed class AcCarJob
    {
        public sealed class LodData
        {
            public int Index;
            public float In, Out;
            public Kn5File Kn5;
        }

        public sealed class HullMesh
        {
            public string Name;
            public Vector3[] Vertices;
            public int[] Triangles;
        }

        // inputs
        public readonly AcCarEntry Car;
        public readonly AcCarModelOptions Options;
        public readonly string DriverDir;

        // outputs
        public AcCarVisual Visual;
        public readonly List<LodData> Lods = new List<LodData>();
        /// <summary>LOD_0 kn5 (texture names for LOD_1..3, which carry none).</summary>
        public Kn5File TextureSource;
        /// <summary>Bounds of the primary LOD's visible meshes, Unity model space.</summary>
        public Bounds ModelBounds;
        public bool HasModelBounds;
        public readonly List<HullMesh> Hull = new List<HullMesh>();
        public Bounds HullBounds;
        public bool HasHullBounds;
        /// <summary>Outer radius of the steering wheel rim (max distance from the STEER_HR axis of its meshes), 0 when unknown.</summary>
        public float SteerRimRadius;
        /// <summary>car_susp_xx: time of the rest pose and metres of wheel-centre rise from frame 0 to the last frame (0 = no travel by anim).</summary>
        public readonly float[] SuspRestT = new float[4];
        public readonly float[] SuspRange = new float[4];
        public Kn5File DriverKn5;
        /// <summary>Set before <see cref="Start"/>: the car skin folder (skin.ini [driver model] SUIT / GLOVES / HELMET).</summary>
        public string SkinDir;
        /// <summary>The driver skin textures prepared on the worker (W's AcTexPrepare), by kn5 texture name; empty when the skin has none.</summary>
        public readonly List<AcTexPrep> DriverSkin = new List<AcTexPrep>();
        public readonly List<string> DriverSkinPaths = new List<string>();
        public string DriverPath;
        public long Ms;
        public string Error;
        public readonly List<string> Notes = new List<string>();

        volatile bool _done;
        public bool Done { get { return _done; } }

        public AcCarJob(AcCarEntry car, AcCarModelOptions options, string driverDir)
        {
            Car = car; Options = options ?? new AcCarModelOptions(); DriverDir = driverDir;
        }

        /// <summary>Runs on a background thread (<see cref="Start"/>) or inline (<see cref="Run"/>).</summary>
        public void Start()
        {
            var th = new Thread(Run) { IsBackground = true, Name = "Ac car " + (Car != null ? Car.Id : "?") };
            th.Start();
        }

        public void Run()
        {
            var sw = Stopwatch.StartNew();
            try { Work(); }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; }
            Ms = sw.ElapsedMilliseconds;
            _done = true;
        }

        void Work()
        {
            if (Car == null) { Error = "no car"; return; }
            if (!Car.Owned) { Error = Car.Id + " is not installed (DLC stub)"; return; }
            Visual = AcCarVisual.Load(Car, true, true);

            int first = Math.Max(0, Options.Lod);
            int last = Math.Max(first, Options.MaxLod);
            for (int i = first; i <= last; i++)
            {
                var l = Visual.Lod(i);
                if (l == null || l.Path == null)
                {
                    if (i == first && first > 0) { Notes.Add("LOD_" + i + " missing, using LOD_0"); first = 0; i = -1; Lods.Clear(); continue; }
                    if (i == first) throw new FileNotFoundException("no LOD_" + i + " kn5 (lods.ini)");
                    continue;
                }
                var opt = Kn5ReadOptions.NoTextures; // W reads texture bytes by offset itself
                Lods.Add(new LodData { Index = i, In = l.In, Out = l.Out, Kn5 = Kn5File.Read(l.Path, opt) });
            }
            if (Lods.Count == 0) throw new FileNotFoundException("no kn5 to build");
            if (Lods[0].Index == 0) TextureSource = Lods[0].Kn5;
            else
            {
                var l0 = Visual.Lod(0);
                if (l0 != null && l0.Path != null) TextureSource = Kn5File.Read(l0.Path, Kn5ReadOptions.HeaderOnly);
            }

            var primary = Lods[0].Kn5;
            var world = Kn5Scan.WorldMatrices(primary);
            ComputeBounds(primary, world);
            ComputeSteerRim(primary, world);
            if (Lods[0].Index == 0) for (int w = 0; w < 4; w++) ComputeSusp(primary, w);
            if (Options.Colliders) ReadHull();
            if (Options.Driver) ReadDriver();
        }

        static bool UnderInactive(Kn5Node n)
        {
            for (var p = n; p != null; p = p.Parent) if (!p.Active) return true;
            return false;
        }

        void ComputeBounds(Kn5File k, AcMat4[] world)
        {
            bool any = false;
            Vector3 min = new Vector3(1e9f, 1e9f, 1e9f), max = -min;
            for (int i = 0; i < k.Nodes.Count; i++)
            {
                var n = k.Nodes[i];
                var m = n.Mesh;
                if (m == null || !m.Loaded || !m.IsRenderable || UnderInactive(n)) continue;
                var w = world[i];
                for (int v = 0; v < m.VertexCount; v++)
                {
                    int o = v * m.Stride;
                    float x = m.Vertices[o], y = m.Vertices[o + 1], z = m.Vertices[o + 2];
                    if (m.ConvertedToUnity) x = -x;
                    var p = AcCarMath.WorldToModel(w, new AcVec3(x, y, z));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    any = true;
                }
            }
            if (any) { ModelBounds = new Bounds((min + max) * 0.5f, max - min); HasModelBounds = true; }
        }

        void ComputeSteerRim(Kn5File k, AcMat4[] world)
        {
            var steer = k.Find(Options.Interior ? "STEER_HR" : "STEER_LR") ?? k.Find("STEER_HR") ?? k.Find("STEER_LR");
            if (steer == null) return;
            var inv = world[steer.Index].InverseAffine();
            float best = 0f;
            for (int i = 0; i < k.Nodes.Count; i++)
            {
                var n = k.Nodes[i];
                var m = n.Mesh;
                if (m == null || !m.Loaded) continue;
                bool under = false, skip = false;
                for (var p = n.Parent; p != null; p = p.Parent)
                {
                    string nm = p.Name ?? "";
                    if (p != steer && (nm.IndexOf("SHIFT", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("PAD", StringComparison.OrdinalIgnoreCase) >= 0)) skip = true;
                    if (p == steer) { under = true; break; }
                }
                if (!under || skip) continue;
                var w = world[i];
                for (int v = 0; v < m.VertexCount; v++)
                {
                    int o = v * m.Stride;
                    float x = m.Vertices[o];
                    if (m.ConvertedToUnity) x = -x;
                    var lp = inv.TransformPoint(w.TransformPoint(new AcVec3(x, m.Vertices[o + 1], m.Vertices[o + 2])));
                    float r = (float)Math.Sqrt(lp.X * lp.X + lp.Y * lp.Y);
                    if (r > best && r < 0.4f) best = r; // ours: 0.4 m guard against stray geometry parented to the wheel
                }
            }
            SteerRimRadius = best;
        }

        /// <summary>car_susp_xx (14 cars, e.g. lotus_49: HUB_LF rises 0.109 m over the clip): world height of WHEEL_xx per frame.</summary>
        void ComputeSusp(Kn5File k, int wheel)
        {
            var anim = Visual.Susp[wheel];
            if (anim == null) return;
            string[] names = { "WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR" };
            var node = k.Find(names[wheel]);
            if (node == null) return;
            int fc = anim.FrameCount;
            if (fc < 2) return;
            float rest = node.World.Translation.Y;
            float y0 = 0f, yN = 0f, bestErr = float.MaxValue;
            int bestF = 0;
            for (int f = 0; f < fc; f++)
            {
                AcMat4 m = AcMat4.Identity;
                for (var p = node; p != null; p = p.Parent)
                {
                    if (p.Class != Kn5NodeClass.Dummy) continue;
                    var an = anim.Find(p.Name);
                    m = AcMat4.Mul(m, an != null && an.Frames.Length == fc ? an.Frames[f] : p.Local);
                }
                float y = m.Translation.Y;
                if (f == 0) y0 = y;
                if (f == fc - 1) yN = y;
                float err = Math.Abs(y - rest);
                if (err < bestErr) { bestErr = err; bestF = f; }
            }
            float range = yN - y0;
            if (Math.Abs(range) < 0.01f) return; // ours: < 1 cm = the clip does not move the wheel
            SuspRange[wheel] = range;
            SuspRestT[wheel] = bestF / (float)(fc - 1);
        }

        void ReadHull()
        {
            string path = Car.ColliderPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Notes.Add("no collider.kn5"); return; }
            var k = Kn5File.Read(path, Kn5ReadOptions.NoTextures);
            var world = Kn5Scan.WorldMatrices(k);
            Vector3 min = new Vector3(1e9f, 1e9f, 1e9f), max = -min;
            bool any = false;
            for (int i = 0; i < k.Nodes.Count; i++)
            {
                var n = k.Nodes[i];
                var m = n.Mesh;
                if (m == null || !m.Loaded || m.VertexCount < 4 || m.IndexCount < 3) continue;
                var h = new HullMesh { Name = n.Name, Vertices = new Vector3[m.VertexCount], Triangles = new int[m.IndexCount - m.IndexCount % 3] };
                var w = world[i];
                for (int v = 0; v < m.VertexCount; v++)
                {
                    int o = v * m.Stride;
                    float vx = m.ConvertedToUnity ? -m.Vertices[o] : m.Vertices[o]; // a converted mesh is already X-mirrored: undo, the matrix is AC's
                    var p = AcCarMath.WorldToModel(w, new AcVec3(vx, m.Vertices[o + 1], m.Vertices[o + 2]));
                    h.Vertices[v] = p;
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    any = true;
                }
                for (int t = 0; t + 2 < h.Triangles.Length; t += 3)
                {
                    if (m.ConvertedToUnity) { h.Triangles[t] = m.Indices[t]; h.Triangles[t + 1] = m.Indices[t + 1]; h.Triangles[t + 2] = m.Indices[t + 2]; } // already flipped
                    else { h.Triangles[t] = m.Indices[t]; h.Triangles[t + 1] = m.Indices[t + 2]; h.Triangles[t + 2] = m.Indices[t + 1]; } // X mirror flips winding
                }
                Hull.Add(h);
            }
            if (any) { HullBounds = new Bounds((min + max) * 0.5f, max - min); HasHullBounds = true; }
        }

        void ReadDriver()
        {
            string name = Visual.Driver.Model;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(DriverDir)) { Notes.Add("no driver model (driver3d.ini)"); return; }
            string p = Path.Combine(DriverDir, name + ".kn5");
            if (!File.Exists(p)) { Notes.Add("driver kn5 missing: " + name); return; }
            DriverPath = p;
            DriverKn5 = Kn5File.Read(p, Kn5ReadOptions.Full);
            ReadDriverSkin(name);
        }

        /// <summary>skin.ini [driver model] SUIT / GLOVES / HELMET folders: every .dds whose name is a texture of the driver kn5 is prepared here
        /// (decode / mips on this worker) and later uploaded once per file for all cars (<see cref="AcCarDriverModel"/>'s shared cache).</summary>
        void ReadDriverSkin(string model)
        {
            try
            {
                string content = !string.IsNullOrEmpty(DriverDir) ? Path.GetDirectoryName(DriverDir) : null;
                var dirs = AcCarVisual.DriverSkinDirs(SkinDir, model, content);
                for (int d = 0; d < dirs.Length; d++)
                {
                    if (dirs[d] == null) continue;
                    var files = Directory.GetFiles(dirs[d], "*.dds");
                    for (int f = 0; f < files.Length; f++)
                    {
                        string tn = Path.GetFileName(files[f]);
                        if (DriverKn5.Texture(tn) == null) continue;
                        DriverSkinPaths.Add(files[f]);
                        if (AcCarDriverModel.HasSkinTexture(files[f])) { DriverSkin.Add(null); continue; } // uploaded already (shared)
                        DriverSkin.Add(AcTexPrepare.Prepare(tn, File.ReadAllBytes(files[f]), true));
                    }
                }
            }
            catch (Exception e) { Notes.Add("driver skin: " + e.Message); }
        }

        /// <summary>Re-reads LOD_0 (vertices released after the first build) on a worker for <see cref="AcCarModel.EnsureInterior"/>.</summary>
        public sealed class Reread
        {
            public string Path;
            public Kn5File Kn5;
            public string Error;
            volatile bool _done;
            public bool Done { get { return _done; } }

            public void Start() { ThreadPool.QueueUserWorkItem(Run); }

            void Run(object _)
            {
                try { Kn5 = Kn5File.Read(Path, Kn5ReadOptions.NoTextures); }
                catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; }
                _done = true;
            }
        }
    }
}
