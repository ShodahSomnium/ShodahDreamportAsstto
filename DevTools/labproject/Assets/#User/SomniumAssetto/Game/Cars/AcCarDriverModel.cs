using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] The optional AC driver (content/driver/&lt;driver3d.ini [MODEL] NAME&gt;.kn5) for cars without a Somnium avatar in the seat
    /// (AI cars, an empty showroom car): built with W's kn5 builder (skinned suit / gloves), then every driver node named like a
    /// driver_base_pos.knh node gets the knh local matrix (seats the generic driver in this car - ac_car_formats.md 4.1). VERIFIED offline
    /// (harness <c>driverpose</c>): with the driver kn5's own FBX wrapper kept, the posed RIG_Hips / RIG_Head / RIG_HAND_L/R land exactly
    /// (0 mm) on the knh's world positions on all 84 cars with a full knh (2 cars have an empty knh: ks_alfa_giulia_qv, ks_mazda_787b).
    /// steer.ksanim drives the arms: time = wheelDeg / LOCK * 0.5 + 0.5 (driver3d.ini [STEER_ANIMATION] LOCK); a gear change plays
    /// shift_up / shift_dw / shift.ksanim (a full hand-to-lever-and-back cycle, frame 0 = frame N) over the [SHIFT_ANIMATION] times on top.
    /// skin.ini [&lt;model&gt;] SUIT / GLOVES / HELMET textures (content/texture/driver_*) replace the kn5's textures of the same name per renderer
    /// (MaterialPropertyBlock; W's materials stay shared); each file is uploaded once and shared by every car (reference counted).
    /// [MODEL] POSITION is NOT applied: the knh is already in car model space (ks_mazda_miata: POSITION 0,0,0.5 but knh hips z -0.379 sit
    /// 7 cm behind DRIVEREYES z -0.447, consistent without it). [HIDE_OBJECT_n] (helmet, face) hide for a first-person viewer.
    /// </summary>
    public sealed class AcCarDriverModel
    {
        public AcKn5Model Kn5 { get; private set; }
        AcKsanimPlayer _steer, _shiftUp, _shiftDown, _shift, _shiftPlaying;
        float _lock = 360f, _steerDeg, _shiftT, _shiftDur = 0.3f;
        readonly List<GameObject> _hide = new List<GameObject>();
        readonly List<string> _skinRefs = new List<string>();
        bool _visible = true;
        public int Posed { get; private set; }
        /// <summary>Renderer texture slots the car skin replaced (log line).</summary>
        public int SkinSlots { get; private set; }
        public bool Visible { get { return _visible; } }
        /// <summary>A shift clip is playing (lab check).</summary>
        public bool Shifting { get { return _shiftPlaying != null; } }

        /// <summary>Builds the driver under <paramref name="modelRoot"/>; <paramref name="done"/> gets it or null.</summary>
        public static IEnumerator Build(AcCarJob job, Transform modelRoot, AcCarModelOptions options, Action<AcCarDriverModel> done)
        {
            if (job == null || job.DriverKn5 == null || modelRoot == null) { if (done != null) done(null); yield break; }
            AcKn5Model built = null;
            var bo = new AcKn5BuildOptions { Layer = options.Layer, SliceMs = options.SliceMs };
            var e = AcKn5Builder.BuildAsync(job.DriverKn5, modelRoot, bo, null, r => built = r);
            while (true)
            {
                object cur;
                try { if (!e.MoveNext()) break; cur = e.Current; }
                catch (Exception ex) { AcLog.Warn("Car: driver model build failed: " + ex.Message); built = null; break; }
                yield return cur;
            }
            if (built == null || built.Root == null) { if (done != null) done(null); yield break; }
            var d = new AcCarDriverModel { Kn5 = built };
            try { d.Pose(job.Visual); }
            catch (Exception ex) { AcLog.Warn("Car: driver pose failed: " + ex.Message); }
            try { d.ApplySkin(job); }
            catch (Exception ex) { AcLog.Warn("Car: driver skin failed: " + ex.Message); }
            if (done != null) done(d);
        }

        Transform Find(string name)
        {
            if (Kn5 == null || string.IsNullOrEmpty(name)) return null;
            var t = Kn5.Find(name);
            if (t != null) return t;
            foreach (var kv in Kn5.Nodes) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        void Pose(AcCarVisual v)
        {
            if (v == null) return;
            if (v.HasKnh && v.Knh != null)
            {
                for (int i = 0; i < v.Knh.Nodes.Count; i++)
                {
                    var n = v.Knh.Nodes[i];
                    if (n.Name == null || !n.Name.StartsWith("DRIVER:", StringComparison.Ordinal)) continue; // the knh's SCENE_ROOT/MODEL/FBX wrappers stay the kn5's
                    var t = Find(n.Name);
                    if (t == null) continue;
                    Vector3 p; Quaternion q; Vector3 s;
                    AcCarMath.ToTrs(n.Local, out p, out q, out s);
                    t.localPosition = p; t.localRotation = q; t.localScale = s;
                    Posed++;
                }
            }
            _lock = v.Driver.SteerLock > 1f ? v.Driver.SteerLock : 360f;
            _steer = AcKsanimPlayer.Bind(v.DriverSteer, Find);
            _shiftUp = AcKsanimPlayer.Bind(v.DriverShiftUp, Find);
            _shiftDown = AcKsanimPlayer.Bind(v.DriverShiftDown, Find);
            _shift = AcKsanimPlayer.Bind(v.DriverShift, Find);
            float ms = v.Driver.PositiveMs + v.Driver.StaticMs + v.Driver.NegativeMs;
            _shiftDur = ms > 20f ? ms * 0.001f : 0.3f; // driver3d.ini [SHIFT_ANIMATION] POSITIVE + STATIC + NEGATIVE; 0.3 s ours
            for (int i = 0; i < v.Driver.HideObjects.Count; i++)
            {
                var t = Find(v.Driver.HideObjects[i]);
                if (t != null) _hide.Add(t.gameObject);
            }
            SetSteer(0f);
        }

        // ---------------------------------------------------------------- driver skins (shared uploads)

        sealed class SkinTex { public Texture2D Tex; public int Refs; }
        static readonly Dictionary<string, SkinTex> SkinCache = new Dictionary<string, SkinTex>(StringComparer.OrdinalIgnoreCase);
        static readonly object SkinGate = new object();
        static MaterialPropertyBlock _mpb;
        static readonly int[] TexProps = { AcMaterials.IdMainTex, AcMaterials.IdNormalTex, AcMaterials.IdMapsTex, AcMaterials.IdDetailTex, AcMaterials.IdDetailNormalTex, AcMaterials.IdMaskTex, AcMaterials.IdBaseMap };

        /// <summary>True when that skin file is uploaded already (the worker then skips preparing it). Thread-safe.</summary>
        public static bool HasSkinTexture(string path)
        {
            lock (SkinGate) return path != null && SkinCache.ContainsKey(path);
        }

        /// <summary>Skin textures alive (the lab's leak check: 0 when no AI car is left).</summary>
        public static int SkinTextureCount { get { lock (SkinGate) return SkinCache.Count; } }

        void ApplySkin(AcCarJob job)
        {
            if (job.DriverSkinPaths.Count == 0 || Kn5 == null) return;
            var byName = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < job.DriverSkinPaths.Count; i++)
            {
                string path = job.DriverSkinPaths[i];
                SkinTex st;
                lock (SkinGate) SkinCache.TryGetValue(path, out st);
                if (st == null)
                {
                    var prep = i < job.DriverSkin.Count ? job.DriverSkin[i] : null;
                    // the worker skipped it as cached, but the last car holding it was disposed since: read and prepare it here (rare; one-off
                    // stall) rather than leaving this driver in the kn5's default suit / helmet
                    if (prep == null) prep = PrepareNow(path);
                    var tex = prep != null ? AcTextures.Upload(prep, "driver skin") : null;
                    if (tex == null) continue;
                    st = new SkinTex { Tex = tex };
                    lock (SkinGate) SkinCache[path] = st;
                }
                st.Refs++;
                _skinRefs.Add(path);
                byName[System.IO.Path.GetFileName(path)] = st.Tex;
            }
            if (byName.Count == 0) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int r = 0; r < Kn5.Renderers.Count; r++)
            {
                var rend = Kn5.Renderers[r];
                if (rend == null) continue;
                var mats = rend.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    bool any = false;
                    _mpb.Clear();
                    rend.GetPropertyBlock(_mpb, m);
                    for (int p = 0; p < TexProps.Length; p++)
                    {
                        if (!mat.HasProperty(TexProps[p])) continue;
                        var cur = mat.GetTexture(TexProps[p]);
                        Texture2D rep;
                        if (cur == null || !byName.TryGetValue(cur.name, out rep)) continue;
                        _mpb.SetTexture(TexProps[p], rep);
                        any = true;
                        SkinSlots++;
                    }
                    if (any) rend.SetPropertyBlock(_mpb, m);
                }
            }
        }

        static bool _saidLateSkin;

        static AcTexPrep PrepareNow(string path)
        {
            try
            {
                if (!_saidLateSkin) { _saidLateSkin = true; AcLog.Info("Car: a shared driver skin texture was released before this car used it - re-read on the main thread (" + System.IO.Path.GetFileName(path) + ")."); }
                return AcTexPrepare.Prepare(System.IO.Path.GetFileName(path), System.IO.File.ReadAllBytes(path), true);
            }
            catch (Exception e) { AcLog.WarnOnce("Car: re-reading a driver skin failed: " + e.Message); return null; }
        }

        static void ReleaseSkin(List<string> refs)
        {
            for (int i = 0; i < refs.Count; i++)
            {
                SkinTex st;
                lock (SkinGate)
                {
                    if (!SkinCache.TryGetValue(refs[i], out st)) continue;
                    if (--st.Refs > 0) continue;
                    SkinCache.Remove(refs[i]);
                }
                if (st.Tex != null) UnityEngine.Object.Destroy(st.Tex);
            }
            refs.Clear();
        }

        // ---------------------------------------------------------------- per frame (no allocation)

        /// <summary>Steering-wheel angle (deg, right positive) -> steer.ksanim time. No allocation.</summary>
        public void SetSteer(float wheelDeg)
        {
            _steerDeg = wheelDeg;
            if (!_visible || _shiftPlaying != null) return; // the shift clip owns the arms while it plays
            if (_steer != null) _steer.Sample(wheelDeg / _lock * 0.5f + 0.5f);
        }

        /// <summary>Starts the hand-to-lever clip (shift_up / shift_dw, else shift.ksanim).</summary>
        public void Shift(bool up)
        {
            var p = up ? (_shiftUp ?? _shift) : (_shiftDown ?? _shift);
            if (p == null || !_visible) return;
            _shiftPlaying = p;
            _shiftT = 0f;
            p.Invalidate();
            p.Sample(0f);
        }

        /// <summary>Advances the shift clip. Call once per frame.</summary>
        public void Tick(float dt)
        {
            if (_shiftPlaying == null) return;
            _shiftT += dt / _shiftDur;
            if (_shiftT < 1f)
            {
                // steer first (the arm the clip leaves alone keeps steering), the shift clip on top; ~25-60 nodes for 0.3 s
                if (_steer != null) { _steer.Invalidate(); _steer.Sample(_steerDeg / _lock * 0.5f + 0.5f); }
                _shiftPlaying.Invalidate();
                _shiftPlaying.Sample(_shiftT);
                return;
            }
            _shiftPlaying = null;
            if (_steer != null) { _steer.Invalidate(); _steer.Sample(_steerDeg / _lock * 0.5f + 0.5f); } // hands back on the wheel
        }

        /// <summary>Shows / hides the whole driver (hidden: the local player sits in the driver seat, or the car is far away).</summary>
        public void SetVisible(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            if (Kn5 != null && Kn5.Root != null) Kn5.Root.gameObject.SetActive(on);
            if (!on) _shiftPlaying = null;
            else if (_steer != null) { _steer.Invalidate(); _steer.Sample(_steerDeg / _lock * 0.5f + 0.5f); }
        }

        /// <summary>First-person viewer: hide the HIDE_OBJECT nodes (helmet, face).</summary>
        public void SetFirstPerson(bool on)
        {
            for (int i = 0; i < _hide.Count; i++) if (_hide[i] != null) _hide[i].SetActive(!on);
        }

        public void Dispose()
        {
            if (Kn5 != null) Kn5.Dispose();
            Kn5 = null;
            ReleaseSkin(_skinRefs);
        }
    }
}
