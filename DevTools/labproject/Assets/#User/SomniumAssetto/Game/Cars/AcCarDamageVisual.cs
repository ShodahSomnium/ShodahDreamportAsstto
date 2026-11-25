using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] Iteration 2 (DAMAGE_DESIGN 2.2 / 2.3): the damage nodes of ONE built LOD set - glass zone groups, damage-paint and
    /// broken-glass renderers, [VISUAL_OBJECT_n] panels with their rest rotation. Bound once per set (and again for a grafted COCKPIT_HR
    /// build); allocates only at bind.
    /// <list type="bullet">
    /// <item>Glass: a node belongs to zone Z when it or an ANCESTOR is named <c>DAMAGE_GLASS_&lt;Z&gt;...</c> (exact token right after the prefix);
    /// the TOP-MOST such node is the group member (children are named <c>..._Vtr_Left_3</c>, <c>polymsh_extracted</c>, <c>..._CENTER_MESH</c>).
    /// Zone nodes are hidden at bind regardless of the kn5 active flag (G0: ktm_xbow_r LOD0 and lotus_exos_125(_s1) LOD1 build them active).
    /// A <c>DAMAGE_GLASS_</c> node without a zone token (mustang LOD1 <c>DAMAGE_GLASS_Vtr_Ant1</c>, a real window) is left alone.</item>
    /// <item>Stray ksBrokenGlass meshes outside every zone (bmw_m3_e92 family <c>R_DOOR_mesh_SUB3</c>) keep the SHARED material, whose
    /// glassDamage stays 0 (W's shader: invisible) - they never break (// ours).</item>
    /// </list>
    /// </summary>
    public sealed class AcCarDamageSet
    {
        public sealed class PanelBind
        {
            /// <summary>Index into AcCarDamageDef.Panels.</summary>
            public int Def;
            public Transform Node;
            public Quaternion Rest;
        }

        /// <summary>Top-most zone nodes per zone (FRONT 0, REAR 1, LEFT 2, RIGHT 3, CENTER 4).</summary>
        public readonly List<GameObject>[] Glass = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
        /// <summary>Renderers with a ksBrokenGlass material inside a zone group (they get the car's glass clone on the first break).</summary>
        public readonly List<Renderer> GlassRenderers = new List<Renderer>();
        /// <summary>Renderers with a damage-paint material (W's _DamageSet.x = 1: built with damage slots).</summary>
        public readonly List<Renderer> PaintRenderers = new List<Renderer>();
        /// <summary>Panels found in this set (several entries may share one node: they are adjacent and compose).</summary>
        public readonly List<PanelBind> Panels = new List<PanelBind>();
        public readonly List<string> MissingPanels = new List<string>();
        public readonly List<string> LooseGlassNames = new List<string>();
        /// <summary>ksBrokenGlass renderers outside every zone (stay invisible), and zone nodes the kn5 had ACTIVE (G0 hides them).</summary>
        public int StrayGlass, ActiveGlassHidden;
        public readonly int Lod;

        readonly bool[] _shown = new bool[5];
        bool _shownKnown;

        public AcCarDamageSet(int lod) { Lod = lod; }

        public int GlassNodeCount { get { int n = 0; for (int z = 0; z < 5; z++) n += Glass[z].Count; return n; } }

        /// <summary>W's shader switches of a material: damage paint (_DamageSet.x) / broken glass (_DamageSet.w).</summary>
        public static bool IsPaint(Material m) { return m != null && m.HasProperty(AcMaterials.IdDamageSet) && m.GetVector(AcMaterials.IdDamageSet).x > 0.5f; }
        public static bool IsBrokenGlass(Material m) { return m != null && m.HasProperty(AcMaterials.IdDamageSet) && m.GetVector(AcMaterials.IdDamageSet).w > 0.5f; }

        /// <summary>Collects the damage nodes of <paramref name="k"/> (the set's own build, or a grafted one). <paramref name="panels"/> = also
        /// resolve the [VISUAL_OBJECT_n] names through <paramref name="find"/> (the set's Find: exact, then case-insensitive).</summary>
        public void Add(AcKn5Model k, AcCarDamageDef def, Func<string, Transform> find, bool panels)
        {
            if (k == null || k.Root == null) return;
            // glass groups: top-most zone node
            foreach (var kv in k.NodeOf)
            {
                var t = kv.Key;
                if (t == null) continue;
                string name = t.name;
                if (!AcCarDamageDef.IsGlassName(name)) continue;
                int z = AcCarDamageDef.GlassZoneOfName(name);
                if (HasGlassAncestor(t, k.Root, z >= 0)) continue;
                if (z >= 0)
                {
                    Glass[z].Add(t.gameObject);
                    if (t.gameObject.activeSelf) ActiveGlassHidden++;
                    t.gameObject.SetActive(false);
                    _shownKnown = false;   // fixer 2026-11-02: a grafted node starts hidden - the next SetGlass writes every zone again

                }
                else LooseGlassNames.Add(name);
            }
            // renderers: damage paint, broken glass in a zone / stray
            for (int i = 0; i < k.Renderers.Count; i++)
            {
                var r = k.Renderers[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                bool paint = false, glass = false;
                for (int j = 0; j < mats.Length; j++) { if (IsPaint(mats[j])) paint = true; if (IsBrokenGlass(mats[j])) glass = true; }
                if (paint) PaintRenderers.Add(r);
                if (glass)
                {
                    if (ZoneOfRenderer(r.transform, k.Root) >= 0) GlassRenderers.Add(r);
                    else StrayGlass++;
                }
            }
            if (!panels || def == null || find == null) return;
            for (int i = 0; i < def.Panels.Count; i++)
            {
                var p = def.Panels[i];
                Transform node = null;
                try { node = find(p.Name); } catch (Exception) { node = null; }
                if (node == null) { MissingPanels.Add(p.Name); continue; }
                var b = new PanelBind { Def = i, Node = node, Rest = node.localRotation };
                int at = -1;
                for (int j = Panels.Count - 1; j >= 0; j--) if (Panels[j].Node == node) { at = j; break; }
                if (at >= 0) { b.Rest = Panels[at].Rest; Panels.Insert(at + 1, b); }   // two sections on one node compose
                else Panels.Add(b);
            }
        }

        /// <summary>True when an ancestor (below the kn5 root) is a glass node that wins over <paramref name="t"/>: any zone node when t is
        /// one (top-most rule); any DAMAGE_GLASS_ node when t is a token-less one (only the top-most loose name is reported).</summary>
        static bool HasGlassAncestor(Transform t, Transform root, bool zoneOnly)
        {
            for (var p = t.parent; p != null && p != root; p = p.parent)
            {
                if (zoneOnly ? AcCarDamageDef.GlassZoneOfName(p.name) >= 0 : AcCarDamageDef.IsGlassName(p.name)) return true;
            }
            return false;
        }

        /// <summary>Zone of the top-most zone node above (or at) <paramref name="t"/>, -1 when none.</summary>
        static int ZoneOfRenderer(Transform t, Transform root)
        {
            int zone = -1;
            for (var p = t; p != null && p != root; p = p.parent)
            {
                int z = AcCarDamageDef.GlassZoneOfName(p.name);
                if (z >= 0) zone = z;
            }
            return zone;
        }

        /// <summary>Shows the glass zones that are broken (writes only on change). No allocation.</summary>
        public void SetGlass(bool[] broken)
        {
            for (int z = 0; z < 5; z++)
            {
                if (_shownKnown && _shown[z] == broken[z]) continue;
                _shown[z] = broken[z];
                var g = Glass[z];
                for (int i = 0; i < g.Count; i++) if (g[i] != null) g[i].SetActive(broken[z]);
            }
            _shownKnown = true;
        }
    }

    /// <summary>
    /// [C, Car model] Iteration 2: one car's damage visuals (DAMAGE_DESIGN 2.1-2.4), driven by <see cref="AcCarModel.SetDamage"/> with
    /// ZoneByte km/h values (identical on the owner and every remote client). Work happens on a value change only, except the oscillating
    /// panels (a few float ops per damaged panel per frame, within 60 m of the camera) - no allocation after the first damage.
    /// <list type="bullet">
    /// <item>Paint: per-car material CLONES (not property blocks: those drop renderers out of the SRP Batcher and collide with the lights'
    /// blocks) made on the first non-zero zone, swapped into every LOD set of this car; the two vectors (scratch, dent) in mask order
    /// (left, rear, right, front) are written on a change only; repair writes zeros and keeps the clones; Dispose destroys them.</item>
    /// <item>Glass: zone g = saturate((z - MIN) / (FULL - MIN)) from damage.ini [DAMAGE_GLASS_&lt;Z&gt;]; shown at g &gt;= GLASS_THRESHOLD; the zone
    /// renderers get ONE per-car clone of the ksBrokenGlass material with glassDamage 1 on the first break.</item>
    /// <item>Panels: static bend rest * AngleAxis(-f * ANGLE, normalize(-ax, ay, az)) about the node's own origin, plus a damped angular
    /// spring driven by the car's acceleration (finite difference of the model root) when [OSCILLATIONS] ENABLED.</item>
    /// </list>
    /// </summary>
    public sealed class AcCarDamageVisual
    {
        const float OscHz = 6f;            // ours: panel spring frequency (no AC value)
        const float OscDamping = 0.3f;     // ours: damping ratio
        const float OscMaxDistM = 60f;     // ours: no wobble beyond this camera distance
        const float OscMaxG = 5f;          // ours: clamp of the finite-difference acceleration (teleports / resets spike it)
        const float OscDegPerG = Mathf.Rad2Deg; // ours: MULT_G x g read as radians of excitation (MULT_G 0.1 at 1 g = 5.7 deg target)

        readonly AcCarDamageDef _def;
        readonly string _carId;
        readonly float[] _zone = new float[5];
        readonly float[] _eff = new float[5];
        readonly bool[] _broken = new bool[5];
        bool _applied, _visualOn = true;

        readonly Dictionary<Material, Material> _cloneOf = new Dictionary<Material, Material>();
        readonly HashSet<Material> _isClone = new HashSet<Material>();
        readonly List<Material> _paintClones = new List<Material>(), _glassClones = new List<Material>();
        bool _paintSwapped, _glassSwapped, _glassUnavailable;
        Vector4 _scr, _dent;     // the last paint vectors written (fixer 2026-11-02: OnGraft writes them to new clones)
        Texture _crack;          // the crack texture of the glass clones

        // panels (per def; shared by every LOD set so the wobble survives a LOD switch)
        readonly float[] _f, _theta, _omega;
        readonly Quaternion[] _static;
        readonly Vector3[] _sAxis, _oAxis;
        bool _anyBent;
        Vector3 _prevPos, _prevVel;
        int _posFrames;
        bool _oscRested = true;  // theta / omega are zero and written (fixer 2026-11-02)

        public AcCarDamageVisual(AcCarDamageDef def, string carId)
        {
            _def = def ?? new AcCarDamageDef();
            _carId = carId ?? "?";
            int n = _def.Panels.Count;
            _f = new float[n]; _theta = new float[n]; _omega = new float[n];
            _static = new Quaternion[n]; _sAxis = new Vector3[n]; _oAxis = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var p = _def.Panels[i];
                _static[i] = Quaternion.identity;
                _sAxis[i] = Axis(p.StaticAxis);
                _oAxis[i] = Axis(p.OscAxis);
            }
        }

        /// <summary>AC model axis (+X = car LEFT) -> Unity, normalised (zero axis -> zero: no rotation).</summary>
        static Vector3 Axis(AcVec3 a)
        {
            var v = new Vector3(-a.X, a.Y, a.Z);
            float m = v.magnitude;
            return m > 1e-6f ? v / m : Vector3.zero;
        }

        public AcCarDamageDef Def { get { return _def; } }
        /// <summary>The last zone values given (km/h, ZoneByte).</summary>
        public float Zone(int z) { return z >= 0 && z < 5 ? _zone[z] : 0f; }
        public bool AnyDamage { get { for (int z = 0; z < 5; z++) if (_zone[z] > 0f) return true; return false; } }
        public bool GlassBroken(int z) { return z >= 0 && z < 5 && _broken[z]; }
        /// <summary>Static bend factor f of panel def <paramref name="i"/> (0..1).</summary>
        public float PanelFactor(int i) { return i >= 0 && i < _f.Length ? _f[i] : 0f; }
        /// <summary>Current wobble angle (deg) of panel def <paramref name="i"/>.</summary>
        public float PanelWobble(int i) { return i >= 0 && i < _theta.Length ? _theta[i] : 0f; }
        public int PaintClones { get { return _paintClones.Count; } }
        public int GlassClones { get { return _glassClones.Count; } }

        /// <summary>Stores the zone values; returns true when they changed (or <paramref name="visualOn"/> changed).</summary>
        public bool Set(float front, float rear, float left, float right, float center, bool visualOn)
        {
            bool changed = !_applied || visualOn != _visualOn || _zone[0] != front || _zone[1] != rear || _zone[2] != left || _zone[3] != right || _zone[4] != center;
            _zone[0] = Clean(front); _zone[1] = Clean(rear); _zone[2] = Clean(left); _zone[3] = Clean(right); _zone[4] = Clean(center);
            _visualOn = visualOn;
            return changed;
        }

        static float Clean(float v) { return float.IsNaN(v) || v < 0f ? 0f : v > 255f ? 255f : v; }

        /// <summary>Applies the stored values to every set (paint vectors, glass, static bends). Called on a change only.</summary>
        public void Apply(List<AcCarPartSet> sets, AcCarPartSet active)
        {
            _applied = true;
            bool any = false;
            for (int z = 0; z < 5; z++) { _eff[z] = _visualOn ? _zone[z] : 0f; if (_eff[z] > 0f) any = true; }
            var rules = AcDamageRules.Current;
            float threshold = rules != null ? rules.GlassThreshold : 0.65f;
            float maxKmh = rules != null ? rules.MaxDamageKmh : 60f;

            // ---- paint (only materials built with the damage slots are listed)
            if (any && !_paintSwapped) SwapPaint(sets);
            if (_paintSwapped)
            {
                // mask order: R = LEFT (2), G = REAR (1), B = RIGHT (3), A = FRONT (0)
                _scr = new Vector4(_def.Scratch01(_eff[2]), _def.Scratch01(_eff[1]), _def.Scratch01(_eff[3]), _def.Scratch01(_eff[0]));
                _dent = new Vector4(_def.Dent01(_eff[2], maxKmh), _def.Dent01(_eff[1], maxKmh), _def.Dent01(_eff[3], maxKmh), _def.Dent01(_eff[0], maxKmh));
                WritePaint();
            }

            // ---- glass (fixer 2026-11-02: a zone without its [DAMAGE_GLASS_<Z>] section never breaks, whatever GLASS_THRESHOLD says)
            bool anyBroken = false;
            for (int z = 0; z < 5; z++) { _broken[z] = _def.HasGlass[z] && _def.Glass01(z, _eff[z]) >= threshold && _eff[z] > 0f; if (_broken[z]) anyBroken = true; }
            if (anyBroken && !_glassSwapped && !_glassUnavailable) SwapGlass(sets);
            if (_glassUnavailable || !_glassSwapped) for (int z = 0; z < 5; z++) _broken[z] = false;   // never show a crack-less grey film
            for (int i = 0; i < sets.Count; i++) if (sets[i].Damage != null) sets[i].Damage.SetGlass(_broken);

            // ---- panels: static bend factor
            _anyBent = false;
            for (int i = 0; i < _f.Length; i++)
            {
                var p = _def.Panels[i];
                float f = p.Zone >= 0 && p.Zone < 5 ? p.Factor(_eff[p.Zone]) : 0f;
                _f[i] = f;
                if (f > 0f) _anyBent = true; else { _theta[i] = 0f; _omega[i] = 0f; }
                _static[i] = f > 0f && _sAxis[i] != Vector3.zero ? Quaternion.AngleAxis(-f * p.StaticAngle, _sAxis[i]) : Quaternion.identity;
            }
            for (int i = 0; i < sets.Count; i++) ApplyPanels(sets[i]);
        }

        /// <summary>Re-applies glass and panels to one set (LOD switch / interior graft). No allocation.</summary>
        public void ApplySet(AcCarPartSet s)
        {
            if (s == null || s.Damage == null || !_applied) return;
            s.Damage.SetGlass(_broken);
            ApplyPanels(s);
        }

        void ApplyPanels(AcCarPartSet s)
        {
            var d = s != null ? s.Damage : null;
            if (d == null) return;
            var list = d.Panels;
            Quaternion acc = Quaternion.identity;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b.Node == null) continue;
                bool chained = i > 0 && list[i - 1].Node == b.Node;
                Quaternion q = chained ? acc : b.Rest;
                q = q * _static[b.Def];
                float th = _theta[b.Def];
                if (th != 0f && _oAxis[b.Def] != Vector3.zero) q = q * Quaternion.AngleAxis(-th, _oAxis[b.Def]);
                acc = q;
                bool last = i == list.Count - 1 || list[i + 1].Node != b.Node;
                if (last) b.Node.localRotation = acc;
            }
        }

        /// <summary>Wobble of the bent panels (once per frame, LateUpdate). No allocation.</summary>
        public void Tick(float dt, Transform root, AcCarPartSet active, float camDist)
        {
            if (!_anyBent || !_def.Oscillations || root == null || dt <= 0f || active == null || active.Damage == null || active.Damage.Panels.Count == 0 || camDist > OscMaxDistM)
            {
                _posFrames = 0;
                if (!_oscRested)
                {
                    // fixer 2026-11-02: once when the wobble stops (far camera, LOD without panels ...): the panels rest at the static bend,
                    // so a later Apply / ApplySet does not freeze a mid-swing angle
                    _oscRested = true;
                    for (int i = 0; i < _theta.Length; i++) { _theta[i] = 0f; _omega[i] = 0f; }
                    if (active != null && active.Damage != null) ApplyPanels(active);
                }
                return;
            }
            _oscRested = false;
            Vector3 pos = root.position;
            Vector3 acc = Vector3.zero;
            if (_posFrames >= 1)
            {
                Vector3 vel = (pos - _prevPos) / dt;
                if (_posFrames >= 2) acc = (vel - _prevVel) / dt;
                _prevVel = vel;
            }
            _prevPos = pos;
            if (_posFrames < 3) _posFrames++;
            Vector3 local = root.InverseTransformDirection(acc) / 9.81f;
            if (local.sqrMagnitude > OscMaxG * OscMaxG) local = local.normalized * OscMaxG;
            var accAc = new Vector3(-local.x, local.y, local.z);   // AC axes: x lateral (+ left), y vertical, z longitudinal

            float w = 2f * Mathf.PI * OscHz;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 8);   // ours: <= 1/120 s per spring step (stable for 6 Hz)
            float h = dt / steps;
            for (int i = 0; i < _f.Length; i++)
            {
                float f = _f[i];
                if (f <= 0f) continue;
                var p = _def.Panels[i];
                float exc = p.MultG * (accAc.x * p.AllowedG.X + accAc.y * p.AllowedG.Y + accAc.z * p.AllowedG.Z) * OscDegPerG;
                float lo = Mathf.Min(p.OscMin, p.OscMax) * f, hi = Mathf.Max(p.OscMin, p.OscMax) * f;
                float th = _theta[i], om = _omega[i];
                for (int k = 0; k < steps; k++)
                {
                    om += (w * w * (exc - th) - 2f * OscDamping * w * om) * h;
                    th += om * h;
                    if (th < lo) { th = lo; if (om < 0f) om = 0f; }
                    else if (th > hi) { th = hi; if (om > 0f) om = 0f; }
                }
                _theta[i] = th; _omega[i] = om;
            }
            ApplyPanels(active);
        }

        void SwapPaint(List<AcCarPartSet> sets)
        {
            _paintSwapped = true;
            int renderers = 0;
            for (int i = 0; i < sets.Count; i++) if (sets[i].Damage != null) renderers += Swap(sets[i].Damage.PaintRenderers, true);
            AcLog.Info("Car: " + _carId + " first visual damage: paint material copies " + _paintClones.Count + " on " + renderers + " renderers" + (_paintClones.Count == 0 ? " (built without damage textures - no dents / scratches; mechanics, glass and panels still show)" : ""));
        }

        void SwapGlass(List<AcCarPartSet> sets)
        {
            var crack = AcMaterials.DamageGlassTexture;
            if (crack == null)
            {
                // fixer 2026-11-02: latched for this car only when the crack map will never come; else the next damage change tries again
                if (AcMaterials.DamageGlassGone) { _glassUnavailable = true; AcLog.InfoOnce("Car: broken glass unavailable (no content/texture/DAMAGE_GLASS.dds) - glass zones stay intact"); }
                return;
            }
            _glassSwapped = true;
            _crack = crack;
            for (int i = 0; i < sets.Count; i++) if (sets[i].Damage != null) Swap(sets[i].Damage.GlassRenderers, false);
            WriteGlass();
        }

        /// <summary>The current scratch / dent vectors to every paint clone (Apply on a change; OnGraft for clones made then).</summary>
        void WritePaint()
        {
            for (int i = 0; i < _paintClones.Count; i++)
            {
                var m = _paintClones[i];
                if (m == null) continue;
                m.SetVector(AcMaterials.IdDamageScratch, _scr);
                m.SetVector(AcMaterials.IdDamageDent, _dent);
            }
        }

        /// <summary>glassDamage 1 and the crack texture to every glass clone (SwapGlass; OnGraft for clones made then).</summary>
        void WriteGlass()
        {
            if (_crack == null) return;
            for (int i = 0; i < _glassClones.Count; i++)
            {
                var m = _glassClones[i];
                if (m == null) continue;
                m.SetVector(AcMaterials.IdGlassSet, new Vector4(1f, 0f, 0f, 0f));   // glassDamage
                if (m.GetTexture(AcMaterials.IdNormalTex) != _crack) m.SetTexture(AcMaterials.IdNormalTex, _crack);   // built without damage textures: crack alpha only
            }
        }

        /// <summary>Swaps the paint (or glass) materials of <paramref name="list"/> for this car's clones. Returns the renderers changed.</summary>
        int Swap(List<Renderer> list, bool paint)
        {
            int changed = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                for (int j = 0; j < mats.Length; j++)
                {
                    var m = mats[j];
                    if (m == null || _isClone.Contains(m)) continue;
                    if (paint ? !AcCarDamageSet.IsPaint(m) : !AcCarDamageSet.IsBrokenGlass(m)) continue;
                    Material c;
                    if (!_cloneOf.TryGetValue(m, out c) || c == null)
                    {
                        c = new Material(m) { name = m.name + " (damage " + _carId + ")" };
                        _cloneOf[m] = c;
                        _isClone.Add(c);
                        if (paint) _paintClones.Add(c); else _glassClones.Add(c);
                    }
                    mats[j] = c;
                    any = true;
                }
                if (any) { r.sharedMaterials = mats; changed++; }
            }
            return changed;
        }

        /// <summary>A grafted build (COCKPIT_HR on demand) shares the scope's materials: give its renderers the clones made so far.</summary>
        public void OnGraft(AcCarPartSet s)
        {
            if (s == null || s.Damage == null) return;
            // fixer 2026-11-02: a material only COCKPIT_HR uses gets a NEW clone here - write the current state to the clones (a few
            // SetVector calls, once per graft), else a new glass clone stays clipped (glassDamage 0) and a new paint clone shows no dents
            if (_paintSwapped) { Swap(s.Damage.PaintRenderers, true); WritePaint(); }
            if (_glassSwapped) { Swap(s.Damage.GlassRenderers, false); WriteGlass(); }
            ApplySet(s);
        }

        /// <summary>Destroys the per-car clones (the car's Dispose).</summary>
        public void Dispose()
        {
            foreach (var c in _isClone) if (c != null) UnityEngine.Object.Destroy(c);
            _isClone.Clear(); _cloneOf.Clear(); _paintClones.Clear(); _glassClones.Clear();
        }
    }
}
