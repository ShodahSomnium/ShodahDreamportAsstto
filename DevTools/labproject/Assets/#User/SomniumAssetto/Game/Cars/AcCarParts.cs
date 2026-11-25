using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] The named animated parts of ONE built LOD (ETS EtsTruckRig pattern: one built vehicle, parts by name). The hard-coded
    /// AC convention names (WHEEL_xx, SUSP_xx, DISC_xx, RIM_xx, RIM_BLUR_xx, STEER_HR/LR, COCKPIT_HR/LR, DOOR_L/R) were found on all 86
    /// owned cars by the harness census (<c>carparts</c>: WHEEL/SUSP/RIM/RIM_BLUR 344/344, STEER_HR/LR + COCKPIT_HR/LR 86/86, DOOR_L 77,
    /// DOOR_R 74; DISC 312, LOD_1/2 keep the wheels, 14 LOD_3 files have none). Data-named parts (lights.ini, blurred_objects.ini,
    /// instruments, car_*.ksanim) are bound through <see cref="Find"/> (exact name first, then case-insensitive).
    /// Pose rules (Unity, model space: x right, y up, z forward):
    /// <list type="bullet">
    /// <item>wheels: localRotation = AngleAxis(steer, up) * AngleAxis(spin, right) * rest (axes in the parent's space, so a wheel whose rest
    /// pose is turned 180 degrees still rolls forward for a positive spin); localPosition = rest + up * travel. SUSP: steer + travel, no spin;
    /// DISC: steer + spin + travel. Cars with car_susp_xx.ksanim (14) move the hub by the clip instead (time = rest + travel / range).</item>
    /// <item>steering wheel: rest * Euler(0, 0, -wheelDeg) on STEER_HR and STEER_LR (ac_car_formats.md C3, right = positive).</item>
    /// </list>
    /// </summary>
    public sealed class AcCarPartSet
    {
        public sealed class WheelBind
        {
            public Transform Wheel, Susp, Disc, Rim, RimBlur;
            public Vector3 WheelPos, SuspPos, DiscPos;
            public Quaternion WheelRot, SuspRot, DiscRot;
            public Vector3 WheelRight, DiscRight, SuspUpAxis, WheelUpAxis, DiscUpAxis;
            public AcKsanimPlayer SuspAnim;
            public float SuspRestT, SuspRange;
            public int WheelAnimIdx = -1, SuspAnimIdx = -1, DiscAnimIdx = -1;
        }

        sealed class BlurBind
        {
            public GameObject Go;
            public int Wheel;
            public float Min, Max;
            public int Shown = -1;
        }

        public readonly int Lod;
        public readonly AcKn5Model Kn5;
        public readonly float LodIn, LodOut;
        public readonly WheelBind[] Wheels = new WheelBind[4];
        public Transform SteerHr, SteerLr;
        public Quaternion SteerHrRest = Quaternion.identity, SteerLrRest = Quaternion.identity;
        public GameObject CockpitHr, CockpitLr;
        public Transform DoorL, DoorR;
        public AcKsanimPlayer DoorAnimL, DoorAnimR, Wiper, ShiftUp, ShiftDown, Shift;
        public AcCarLights Lights;
        public AcCarInstruments Instruments;
        /// <summary>Iteration 2: this set's damage nodes (glass zone groups, damage-paint / broken-glass renderers, VISUAL_OBJECT panels).</summary>
        public AcCarDamageSet Damage;
        readonly List<BlurBind> _blur = new List<BlurBind>();
        readonly Dictionary<string, Transform> _ci = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        readonly Transform _modelRoot;
        public readonly List<string> Missing = new List<string>();
        bool _hr = true, _hrKnown;

        static readonly string[] Suffix = { "LF", "RF", "LR", "RR" };

        public AcCarPartSet(int lod, AcKn5Model kn5, Transform modelRoot, float lodIn, float lodOut)
        {
            Lod = lod; Kn5 = kn5; _modelRoot = modelRoot; LodIn = lodIn; LodOut = lodOut;
            if (kn5 != null) foreach (var kv in kn5.Nodes) if (kv.Value != null && !_ci.ContainsKey(kv.Key)) _ci[kv.Key] = kv.Value;
        }

        public GameObject Root { get { return Kn5 != null && Kn5.Root != null ? Kn5.Root.gameObject : null; } }

        /// <summary>Node by name: exact (AC is case-sensitive, the miata has lowercase wiper_L), else case-insensitive (Windows file names
        /// in ksanims: car_DOOR_L vs DOOR_L). Null when absent.</summary>
        public Transform Find(string name)
        {
            if (string.IsNullOrEmpty(name) || Kn5 == null) return null;
            var t = Kn5.Find(name);
            if (t != null) return t;
            for (int i = 0; i < _extra.Count; i++) { t = _extra[i].Find(name); if (t != null) return t; }
            _ci.TryGetValue(name, out t);
            return t;
        }

        readonly List<AcKn5Model> _extra = new List<AcKn5Model>();

        /// <summary>Extra kn5 builds grafted onto this set (the COCKPIT_HR built later by <see cref="AcCarModel.EnsureInterior"/>); disposed with the car.</summary>
        public List<AcKn5Model> Extra { get { return _extra; } }

        /// <summary>Grafts a later-built COCKPIT_HR (its own kn5 build of the same LOD file with everything else skipped) under this set's
        /// parent node of the same name, then binds what lives in it: lights (dash lamps), instruments (needles, LEDs, displays) and the
        /// skinned parts under it. Returns false when the graft is impossible (no COCKPIT_HR in the build, no matching parent).</summary>
        public bool GraftInterior(AcKn5Model built, AcCarVisual v, Func<string, Texture2D> texture, bool digital)
        {
            if (built == null) return false;
            var hr = built.Find("COCKPIT_HR");
            Kn5Node hrNode = null;
            if (hr != null) built.NodeOf.TryGetValue(hr, out hrNode);
            string parentName = hrNode != null && hrNode.Parent != null ? hrNode.Parent.Name : null;
            Transform parent = parentName != null ? Find(parentName) : null;
            if (hr == null || parent == null) return false;
            hr.SetParent(parent, false); // same kn5 local matrix under the same-named parent
            _extra.Add(built);
            foreach (var kv in built.Nodes) if (kv.Value != null && !_ci.ContainsKey(kv.Key)) _ci[kv.Key] = kv.Value;
            CockpitHr = hr.gameObject;
            if (SteerHr == null) { SteerHr = Find("STEER_HR"); if (SteerHr != null) SteerHrRest = SteerHr.localRotation; } // when STEER_HR lives under COCKPIT_HR
            foreach (var kv in built.RendererByNode)
            {
                if (!(kv.Value is SkinnedMeshRenderer)) continue;
                var node = built.Find(kv.Key);
                if (node != null && node.IsChildOf(hr)) _skinnedHr.Add(kv.Value.gameObject);
            }
            Func<string, Transform> find = Find;
            if (v != null)
            {
                Lights = AcCarLights.Bind(v, find, null);
                if (Instruments != null) Instruments.Destroy();
                Instruments = AcCarInstruments.Bind(v, find, texture, digital, null);
            }
            // iteration 2: glass zones / damage paint inside the grafted cockpit (panels were resolved at the set's bind)
            if (Damage == null) Damage = new AcCarDamageSet(Lod);
            Damage.Add(built, v != null ? v.Damage : null, find, false);
            _hrKnown = false;
            return true;
        }

        /// <summary>Disposes the grafted builds (the car's Dispose).</summary>
        public void DisposeExtra()
        {
            for (int i = 0; i < _extra.Count; i++) { try { _extra[i].Dispose(); } catch (Exception) { } }
            _extra.Clear();
        }

        /// <summary>Binds everything. <paramref name="interior"/> = the cockpit is shown (COCKPIT_HR), <paramref name="instruments"/> = bind needles / LEDs / displays.</summary>
        public void Bind(AcCarVisual v, AcCarJob job, bool interior, bool instruments, bool digital, Func<string, Texture2D> texture)
        {
            Func<string, Transform> find = Find;
            for (int i = 0; i < 4; i++)
            {
                var w = new WheelBind();
                w.Wheel = Find("WHEEL_" + Suffix[i]);
                w.Susp = Find("SUSP_" + Suffix[i]);
                w.Disc = Find("DISC_" + Suffix[i]);
                w.Rim = Find("RIM_" + Suffix[i]);
                w.RimBlur = Find("RIM_BLUR_" + Suffix[i]);
                if (w.Wheel == null) Missing.Add("WHEEL_" + Suffix[i]);
                Rest(w.Wheel, out w.WheelPos, out w.WheelRot, out w.WheelUpAxis, out w.WheelRight);
                Vector3 dummy;
                Rest(w.Susp, out w.SuspPos, out w.SuspRot, out w.SuspUpAxis, out dummy);
                Rest(w.Disc, out w.DiscPos, out w.DiscRot, out w.DiscUpAxis, out w.DiscRight);
                if (job != null && Lod == 0 && job.SuspRange[i] != 0f && v.Susp[i] != null)
                {
                    w.SuspAnim = AcKsanimPlayer.Bind(v.Susp[i], find);
                    if (w.SuspAnim != null)
                    {
                        w.SuspRestT = job.SuspRestT[i]; w.SuspRange = job.SuspRange[i];
                        w.WheelAnimIdx = w.SuspAnim.IndexOf(w.Wheel); w.SuspAnimIdx = w.SuspAnim.IndexOf(w.Susp); w.DiscAnimIdx = w.SuspAnim.IndexOf(w.Disc);
                    }
                }
                Wheels[i] = w;
            }
            SteerHr = Find("STEER_HR"); SteerLr = Find("STEER_LR");
            if (SteerHr != null) SteerHrRest = SteerHr.localRotation;
            if (SteerLr != null) SteerLrRest = SteerLr.localRotation;
            if (SteerHr == null && SteerLr == null) Missing.Add("STEER_HR");
            var hr = Find("COCKPIT_HR"); var lr = Find("COCKPIT_LR");
            CockpitHr = hr != null ? hr.gameObject : null; CockpitLr = lr != null ? lr.gameObject : null;
            DoorL = Find("DOOR_L"); DoorR = Find("DOOR_R");
            if (v != null)
            {
                DoorAnimL = AcKsanimPlayer.Bind(v.DoorL, find);
                DoorAnimR = AcKsanimPlayer.Bind(v.DoorR, find);
                Wiper = AcKsanimPlayer.Bind(v.Wiper, find);
                ShiftUp = AcKsanimPlayer.Bind(v.ShiftUp, find);
                ShiftDown = AcKsanimPlayer.Bind(v.ShiftDown, find);
                Shift = AcKsanimPlayer.Bind(v.Shift, find);
                for (int i = 0; i < v.Blur.Count; i++)
                {
                    var d = v.Blur[i];
                    var t = Find(d.Name);
                    if (t == null) { Missing.Add(d.Name); continue; }
                    _blur.Add(new BlurBind { Go = t.gameObject, Wheel = Mathf.Clamp(d.Wheel, 0, 3), Min = d.MinKmh, Max = d.MaxKmh });
                }
                Lights = AcCarLights.Bind(v, find, Lod == 0 && interior ? Missing : null); // without COCKPIT_HR the dash lights are absent by design
                if (instruments) Instruments = AcCarInstruments.Bind(v, find, texture, digital, Missing);
            }
            CollectSkinned();
            // iteration 2 (DAMAGE_DESIGN 2.2 / 2.3): glass zones hidden at bind regardless of the kn5 flag (G0), panels by NAME per set
            Damage = new AcCarDamageSet(Lod);
            Damage.Add(Kn5, v != null ? v.Damage : null, find, true);
            if (!interior) SetCockpitHr(false);
        }

        readonly List<GameObject> _skinnedHr = new List<GameObject>(), _skinnedLr = new List<GameObject>();

        /// <summary>W puts skinned renderers (shifter boots: abarth500, miata, cobra, lotus) on the kn5 root, not under their node, so the
        /// cockpit switch toggles the ones whose node sits under COCKPIT_HR / COCKPIT_LR itself.</summary>
        void CollectSkinned()
        {
            if (Kn5 == null) return;
            foreach (var kv in Kn5.RendererByNode)
            {
                if (!(kv.Value is SkinnedMeshRenderer)) continue;
                var node = Find(kv.Key);
                if (node == null) continue;
                if (CockpitHr != null && node.IsChildOf(CockpitHr.transform)) _skinnedHr.Add(kv.Value.gameObject);
                else if (CockpitLr != null && node.IsChildOf(CockpitLr.transform)) _skinnedLr.Add(kv.Value.gameObject);
            }
        }

        void Rest(Transform t, out Vector3 pos, out Quaternion rot, out Vector3 up, out Vector3 right)
        {
            pos = Vector3.zero; rot = Quaternion.identity; up = Vector3.up; right = Vector3.right;
            if (t == null) return;
            pos = t.localPosition; rot = t.localRotation;
            var p = t.parent;
            if (p == null || _modelRoot == null) return;
            // model axes expressed in the parent's space (parents are static dummies at build time)
            var parentToModel = Quaternion.Inverse(_modelRoot.rotation) * p.rotation;
            var inv = Quaternion.Inverse(parentToModel);
            up = inv * Vector3.up;
            right = inv * Vector3.right;
        }

        /// <summary>Poses wheel i. No allocation. With a car_susp_xx clip the hub moves by the clip; a WHEEL/SUSP/DISC node the clip itself
        /// animates takes the clip's pose as its base (steer / spin are added on top), so the two never fight.</summary>
        public void SetWheel(int i, float spinDeg, float travelM, float steerDeg)
        {
            var w = Wheels[i];
            if (w == null) return;
            bool byAnim = w.SuspAnim != null;
            float t = 0f;
            if (byAnim) { t = w.SuspRestT + travelM / w.SuspRange; w.SuspAnim.Sample(t); }
            float trav = byAnim ? 0f : travelM;
            Vector3 bp; Quaternion br;
            if (w.Wheel != null)
            {
                Base(w, w.WheelAnimIdx, t, w.WheelPos, w.WheelRot, out bp, out br);
                w.Wheel.localRotation = Quaternion.AngleAxis(steerDeg, w.WheelUpAxis) * Quaternion.AngleAxis(spinDeg, w.WheelRight) * br;
                w.Wheel.localPosition = bp + w.WheelUpAxis * trav;
            }
            if (w.Susp != null)
            {
                Base(w, w.SuspAnimIdx, t, w.SuspPos, w.SuspRot, out bp, out br);
                w.Susp.localRotation = Quaternion.AngleAxis(steerDeg, w.SuspUpAxis) * br;
                w.Susp.localPosition = bp + w.SuspUpAxis * trav;
            }
            if (w.Disc != null)
            {
                Base(w, w.DiscAnimIdx, t, w.DiscPos, w.DiscRot, out bp, out br);
                w.Disc.localRotation = Quaternion.AngleAxis(steerDeg, w.DiscUpAxis) * Quaternion.AngleAxis(spinDeg, w.DiscRight) * br;
                w.Disc.localPosition = bp + w.DiscUpAxis * trav;
            }
        }

        static void Base(WheelBind w, int idx, float t, Vector3 restPos, Quaternion restRot, out Vector3 pos, out Quaternion rot)
        {
            if (idx >= 0 && w.SuspAnim != null) w.SuspAnim.Evaluate(idx, t, out pos, out rot);
            else { pos = restPos; rot = restRot; }
        }

        public void SetSteer(float wheelDeg)
        {
            var q = Quaternion.Euler(0f, 0f, -wheelDeg);
            if (SteerHr != null) SteerHr.localRotation = SteerHrRest * q;
            if (SteerLr != null) SteerLr.localRotation = SteerLrRest * q;
        }

        public void SetDoor(AcSide side, float t)
        {
            var p = side == AcSide.Left ? DoorAnimL : DoorAnimR;
            if (p != null) p.Sample(t);
        }

        /// <summary>RIM_xx / RIM_BLUR_xx by wheel speed (blurred_objects.ini MIN/MAX_SPEED, km/h). Writes only on change.</summary>
        public void SetBlur(float[] wheelKmh)
        {
            for (int i = 0; i < _blur.Count; i++)
            {
                var b = _blur[i];
                float s = Mathf.Abs(wheelKmh[b.Wheel]);
                int shown = s >= b.Min && s <= b.Max ? 1 : 0;
                if (shown == b.Shown) continue;
                b.Shown = shown;
                if (b.Go != null) b.Go.SetActive(shown == 1);
            }
        }

        /// <summary>COCKPIT_HR (close / inside) vs COCKPIT_LR (lods.ini DISTANCE_SWITCH). Writes only on change.</summary>
        public void SetCockpitHr(bool hr)
        {
            if (_hrKnown && hr == _hr) return;
            _hr = hr; _hrKnown = true;
            if (CockpitHr != null) CockpitHr.SetActive(hr);
            bool lr = !hr || CockpitHr == null;
            if (CockpitLr != null) CockpitLr.SetActive(lr);
            for (int i = 0; i < _skinnedHr.Count; i++) if (_skinnedHr[i] != null) _skinnedHr[i].SetActive(hr);
            for (int i = 0; i < _skinnedLr.Count; i++) if (_skinnedLr[i] != null) _skinnedLr[i].SetActive(lr);
        }

        public bool CockpitIsHr { get { return _hr; } }
        public int BlurCount { get { return _blur.Count; } }

        /// <summary>Re-arms the change caches after this set was hidden (LOD switch).</summary>
        public void Invalidate()
        {
            for (int i = 0; i < _blur.Count; i++) _blur[i].Shown = -1;
            if (DoorAnimL != null) DoorAnimL.Invalidate();
            if (DoorAnimR != null) DoorAnimR.Invalidate();
            if (Wiper != null) Wiper.Invalidate();
            for (int i = 0; i < 4; i++) if (Wheels[i] != null && Wheels[i].SuspAnim != null) Wheels[i].SuspAnim.Invalidate();
            if (Instruments != null) Instruments.Invalidate();
            if (Lights != null) Lights.Refresh();
        }
    }
}
