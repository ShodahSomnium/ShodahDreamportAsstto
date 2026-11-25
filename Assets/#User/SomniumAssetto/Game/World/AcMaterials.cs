using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The textures and materials one build owns (a track load, or one car kn5 + skin shared by every instance of that car). Reference
    /// counted: <see cref="AcMaterials.Acquire"/> / <see cref="AcMaterials.Release"/>; the last release destroys everything in it.
    /// </summary>
    public sealed class AcMatScope
    {
        public readonly string Key;
        internal int Refs;
        /// <summary>Uploaded textures by key (track: "file|name", model: lower-case name). Null values = failed (fallback used).</summary>
        public readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        /// <summary>Materials by key (track: "file|materialIndex", model: "kn5path|materialIndex").</summary>
        public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        public long TextureBytes;
        public int TextureCount, TextureFailures;
        public AcMatScope(string key) { Key = key; }

        public Texture2D Tex(string key) { Texture2D t; return key != null && Textures.TryGetValue(key, out t) ? t : null; }

        /// <summary>Destroys every texture and material of the scope.</summary>
        public void DestroyAll()
        {
            foreach (var kv in Materials) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            foreach (var kv in Textures) if (kv.Value != null && !AcTextures.IsShared(kv.Value)) UnityEngine.Object.Destroy(kv.Value);
            Materials.Clear(); Textures.Clear(); TextureBytes = 0;
        }
    }

    /// <summary>
    /// kn5 materials -> Unity materials drawn by the World module's own shaders (Shaders/, carried by the Resources/Ac*.mat carrier materials
    /// on "Ac Game"): <c>SomniumAssetto/PerPixel</c> for the ksPerPixel family (ksPerPixel, NM, AT, AT_NM, MultiMap(_AT/_NMDetail/SimpleRefl),
    /// Reflection, NM_UVMult, ksTree, ksGrass, ksFlags, car shaders ksPerPixelMultiMap_damage_dirt, ksTyres, ksBrakeDisc, ksWindscreen ...,
    /// and GL unlit), <c>SomniumAssetto/Multilayer</c> for the road/terrain ksMultilayer, _fresnel_nm, _objsp. Every number is the material's
    /// own (ksAmbient, ksDiffuse, ksSpecular, ksSpecularEXP, ksEmissive, ksAlphaRef, fresnelC/EXP/MaxLevel, isAdditive, useDetail,
    /// detailUVMultiplier, detailNormalBlend, multR..A, magicMult, tarmacSpecularMultiplier, detailNMMult, normalMult, diffuseMult, alpha,
    /// frequency, distortion). Light comes from shader globals set by <see cref="AcTrackLook"/> from the track's lighting.ini and the weather's
    /// colorCurves.ini (<see cref="SetLight"/>). Missing custom shader -> URP Lit with the diffuse map (logged once).
    /// </summary>
    public static class AcMaterials
    {
        public const string PerPixelShader = "SomniumAssetto/PerPixel", MultilayerShader = "SomniumAssetto/Multilayer", SkyShader = "SomniumAssetto/Sky";
        public const int QueueOpaque = 2000, QueueCutout = 2450, QueueTransparent = 3000;
        /// <summary>The sky draws after the opaque range (URP depth priming: DREAMPORT_GUIDE section 7).</summary>
        public const int QueueSky = 2501;

        static readonly Dictionary<string, AcMatScope> Scopes = new Dictionary<string, AcMatScope>(StringComparer.Ordinal);
        static bool _globalsSet;

        public static readonly int IdMainTex = Shader.PropertyToID("_MainTex"), IdNormalTex = Shader.PropertyToID("_NormalTex"),
            IdMapsTex = Shader.PropertyToID("_MapsTex"), IdDetailTex = Shader.PropertyToID("_DetailTex"), IdDetailNormalTex = Shader.PropertyToID("_DetailNormalTex"),
            IdMaskTex = Shader.PropertyToID("_MaskTex"), IdDetailR = Shader.PropertyToID("_DetailR"), IdDetailG = Shader.PropertyToID("_DetailG"),
            IdDetailB = Shader.PropertyToID("_DetailB"), IdDetailA = Shader.PropertyToID("_DetailA"), IdDetailNM = Shader.PropertyToID("_DetailNM"),
            IdLight = Shader.PropertyToID("_Light"), IdEmissive = Shader.PropertyToID("_Emissive"), IdFresnel = Shader.PropertyToID("_Fresnel"),
            IdDetailSet = Shader.PropertyToID("_DetailSet"), IdFlags = Shader.PropertyToID("_Flags"), IdExtra = Shader.PropertyToID("_Extra"),
            IdWave = Shader.PropertyToID("_Wave"), IdMultU = Shader.PropertyToID("_MultU"), IdMultV = Shader.PropertyToID("_MultV"),
            IdLayer = Shader.PropertyToID("_Layer"), IdSrcBlend = Shader.PropertyToID("_SrcBlend"), IdDstBlend = Shader.PropertyToID("_DstBlend"),
            IdZWrite = Shader.PropertyToID("_ZWrite"), IdCull = Shader.PropertyToID("_Cull"),
            IdSunDir = Shader.PropertyToID("_AcSunDir"), IdSunColor = Shader.PropertyToID("_AcSunColor"), IdAmbient = Shader.PropertyToID("_AcAmbient"),
            IdSkyZenith = Shader.PropertyToID("_AcSkyZenith"), IdSkyHorizon = Shader.PropertyToID("_AcSkyHorizon"), IdFogColor = Shader.PropertyToID("_AcFogColor"),
            IdFogParams = Shader.PropertyToID("_AcFogParams"), IdBaseMap = Shader.PropertyToID("_BaseMap"), IdBaseColor = Shader.PropertyToID("_BaseColor"),
            IdEmissiveOverride = Shader.PropertyToID("_EmissiveOverride"),
            // iteration 2 (DAMAGE_DESIGN 2.1 / 2.2): C writes the vectors on its per-car clones with these ids
            IdDamageTex = Shader.PropertyToID("_DamageTex"), IdDamageMaskTex = Shader.PropertyToID("_DamageMaskTex"),
            IdDamageScratch = Shader.PropertyToID("_DamageScratch"), IdDamageDent = Shader.PropertyToID("_DamageDent"),
            IdDamageSet = Shader.PropertyToID("_DamageSet"), IdGlassSet = Shader.PropertyToID("_GlassSet");

        static Texture2D _damageGlass;
        // fixer 2026-11-02: only a DEFINITIVE result latches (the file is not in the install); no install yet = try again later; a failed read /
        // decode is retried up to DamageGlassMaxFails times. The read + decode run on a worker (WarmDamageGlass); the getter only uploads.
        static bool _damageGlassMissing;
        static int _damageGlassFails;
        const int DamageGlassMaxFails = 3;   // ours
        static bool _damageGlassJobStarted;
        static readonly object DamageGlassLock = new object();
        static bool _damageGlassJobDone;     // under DamageGlassLock
        static AcTexPrep _damageGlassPrep;   // under DamageGlassLock
        static string _damageGlassError;     // under DamageGlassLock

        /// <summary>The crack map will not come (the file is missing, or every retry failed) - fixer 2026-11-02.</summary>
        public static bool DamageGlassGone { get { return _damageGlassMissing || _damageGlassFails >= DamageGlassMaxFails; } }

        static string DamageGlassPath()
        {
            var g = AcGame.Current;
            if (g == null || g.Install == null || string.IsNullOrEmpty(g.Install.Root)) return null;
            return System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(g.Install.Root, "content"), "texture"), "DAMAGE_GLASS.dds");
        }

        /// <summary>Main thread: starts reading and decoding DAMAGE_GLASS.dds on a worker (once; nothing when it is loaded, missing or the install
        /// is not known yet). R's AcCarDamageGate.Refresh calls it whenever damage textures are wanted (session / rules change, before a spawn),
        /// so the first glass break while driving only uploads.</summary>
        public static void WarmDamageGlass()
        {
            if (_damageGlass != null || _damageGlassMissing || _damageGlassJobStarted || _damageGlassFails >= DamageGlassMaxFails) return;
            string path;
            try { path = DamageGlassPath(); } catch (Exception) { path = null; }
            if (path == null) return;                                       // the install is not known yet: not latched
            if (!System.IO.File.Exists(path)) { _damageGlassMissing = true; AcLog.Warn("Shaders: content/texture/DAMAGE_GLASS.dds not found - broken glass stays invisible"); return; }
            lock (DamageGlassLock) { _damageGlassJobDone = false; _damageGlassPrep = null; _damageGlassError = null; }
            _damageGlassJobStarted = true;
            System.Threading.ThreadPool.QueueUserWorkItem(DamageGlassJob, path);
        }

        static void DamageGlassJob(object state)
        {
            AcTexPrep prep = null; string err = null;
            try { prep = AcTexPrepare.Prepare("DAMAGE_GLASS.dds", System.IO.File.ReadAllBytes((string)state), true); if (prep == null) err = "not decodable"; }
            catch (Exception e) { err = e.Message; }
            lock (DamageGlassLock) { _damageGlassPrep = prep; _damageGlassError = err; _damageGlassJobDone = true; System.Threading.Monitor.PulseAll(DamageGlassLock); }
        }

        /// <summary>
        /// The install's <c>content/texture/DAMAGE_GLASS.dds</c> (1024 BGRA32; alpha = crack visibility, at most 0.39 [V]) - the common crack map
        /// AC loads for every ksBrokenGlass (the kn5 holds only a 64 px proxy). Decoded on a worker (<see cref="WarmDamageGlass"/>; started here
        /// and waited for when nobody warmed it) and uploaded once on the main thread (~4 MB GPU), shared by every car, never destroyed by a scope.
        /// Null when the file is missing (logged once), the install is not known yet, or the read failed (retried later): broken glass then
        /// stays invisible.
        /// </summary>
        public static Texture2D DamageGlassTexture
        {
            get
            {
                if (_damageGlass != null || _damageGlassMissing || _damageGlassFails >= DamageGlassMaxFails) return _damageGlass;
                if (!_damageGlassJobStarted) WarmDamageGlass();
                if (!_damageGlassJobStarted) return null;
                AcTexPrep prep; string err;
                lock (DamageGlassLock)
                {
                    while (!_damageGlassJobDone) System.Threading.Monitor.Wait(DamageGlassLock);   // only when not warmed in time
                    prep = _damageGlassPrep; err = _damageGlassError; _damageGlassPrep = null;
                }
                _damageGlassJobStarted = false;
                try
                {
                    if (prep != null) _damageGlass = AcTextures.Upload(prep, "content/texture");
                    if (_damageGlass != null) { _damageGlass.name = "Ac DAMAGE_GLASS (install)"; AcLog.Info("Shaders: broken-glass crack map loaded from the install (" + _damageGlass.width + "x" + _damageGlass.height + ")"); }
                    else { _damageGlassFails++; AcLog.Warn("Shaders: DAMAGE_GLASS.dds failed (" + (err ?? "upload") + "), try " + _damageGlassFails + " of " + DamageGlassMaxFails); }
                }
                catch (Exception e) { _damageGlassFails++; AcLog.Warn("Shaders: DAMAGE_GLASS.dds failed: " + e.Message); _damageGlass = null; }
                return _damageGlass;
            }
        }

        // ------------------------------------------------------------------ scopes

        /// <summary>The scope for <paramref name="key"/> (created on first use), reference count +1.</summary>
        public static AcMatScope Acquire(string key)
        {
            AcMatScope s;
            if (!Scopes.TryGetValue(key, out s)) { s = new AcMatScope(key); Scopes[key] = s; }
            s.Refs++;
            return s;
        }

        /// <summary>Reference count -1; the last one destroys the scope's textures and materials.</summary>
        public static void Release(AcMatScope s)
        {
            if (s == null) return;
            if (--s.Refs > 0) return;
            s.DestroyAll();
            AcMatScope cur;
            if (Scopes.TryGetValue(s.Key, out cur) && ReferenceEquals(cur, s)) Scopes.Remove(s.Key);
        }

        public static int ScopeCount { get { return Scopes.Count; } }

        // ------------------------------------------------------------------ light globals

        /// <summary>
        /// The AC light of the scene, in AC's own gamma-space HDR units already multiplied by the weather's HDR_OFF_MULT: sun direction (Unity world,
        /// TOWARDS the sun) and colour, ambient, sky zenith / horizon colours, fog colour, the weather's fog BLEND and DISTANCE (m; 0 = no fog).
        /// </summary>
        public static void SetLight(Vector3 sunDir, Color sun, Color ambient, Color zenith, Color horizon, Color fog, float fogBlend, float fogDistance)
        {
            _globalsSet = true;
            Shader.SetGlobalVector(IdSunDir, sunDir.normalized);
            Shader.SetGlobalVector(IdSunColor, new Vector4(sun.r, sun.g, sun.b, 1f));
            Shader.SetGlobalVector(IdAmbient, new Vector4(ambient.r, ambient.g, ambient.b, 1f));
            Shader.SetGlobalVector(IdSkyZenith, new Vector4(zenith.r, zenith.g, zenith.b, 1f));
            Shader.SetGlobalVector(IdSkyHorizon, new Vector4(horizon.r, horizon.g, horizon.b, 1f));
            Shader.SetGlobalVector(IdFogColor, new Vector4(fog.r, fog.g, fog.b, 1f));
            // fog amount = (distance / DISTANCE) ^ BLEND [BELIEF: BLEND read as an exponent - 0.75 clear, 1.0 heavy fog, 5 system default]
            Shader.SetGlobalVector(IdFogParams, new Vector4(fogDistance > 1f ? 1f / fogDistance : 0f, Mathf.Max(0.05f, fogBlend), 1f, 0f));
        }

        public static bool GlobalsSet { get { return _globalsSet; } }

        /// <summary>Neutral light until a track / hub look is applied (so a car built in the hub is never black). // ours: all values</summary>
        public static void EnsureGlobals()
        {
            if (_globalsSet) return;
            if (AcTrackLook.TryApplyDefault()) return;
            SetLight(new Vector3(-0.4f, 0.75f, -0.5f), new Color(2.6f, 2.5f, 2.3f), new Color(1.2f, 1.25f, 1.35f), new Color(0.12f, 0.27f, 0.6f),
                new Color(0.62f, 0.7f, 0.9f), new Color(0.57f, 0.73f, 1f), 0f, 0f);
        }

        static MaterialPropertyBlock _mpb;

        /// <summary>
        /// Overrides the emissive colour of one renderer's World-shader material, in AC units (the same scale as a kn5 ksEmissive, which the
        /// shaders add as emissive x txDiffuse): lights.ini / digital_instruments.ini EMISSIVE, start lights. Per-renderer property block
        /// (the shared material stays); no allocation after the first call. <see cref="ClearEmissive"/> restores the material's own value.
        /// </summary>
        public static void SetEmissive(Renderer r, float acR, float acG, float acB)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(IdEmissive, new Color(acR, acG, acB, 1f));
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Removes the emissive override of <see cref="SetEmissive"/> (the material's own ksEmissive shows again). Only `_Emissive` is
        /// touched: other values a caller put in the renderer's property block stay (Unity cannot remove one property from a block, so the
        /// material's own value is written back).</summary>
        public static void ClearEmissive(Renderer r)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            var m = r.sharedMaterial;
            _mpb.SetColor(IdEmissive, m != null && m.HasProperty(IdEmissive) ? m.GetColor(IdEmissive) : Color.clear);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// A start-light lamp lit (<paramref name="lit"/>) or dark: lit = emissive <paramref name="emissive"/> (AC units) and ksDiffuse replaced by
        /// <paramref name="diffuse"/> (startinglights.ini DIFFUSE; NaN = keep the material's) [BELIEF: DIFFUSE is the lamp's ksDiffuse while lit, so
        /// the sun does not wash the lit lens out]; dark = the material's own ksEmissive and ksDiffuse. Only `_Emissive` and `_Light` are written.
        /// </summary>
        public static void SetLamp(Renderer r, bool lit, Color emissive, float diffuse)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            var m = r.sharedMaterial;
            Vector4 light = m != null && m.HasProperty(IdLight) ? m.GetVector(IdLight) : new Vector4(0.4f, 0.4f, 0f, 10f);
            if (lit)
            {
                _mpb.SetColor(IdEmissive, new Color(emissive.r, emissive.g, emissive.b, 1f));
                if (!float.IsNaN(diffuse)) light.y = diffuse;
            }
            else _mpb.SetColor(IdEmissive, m != null && m.HasProperty(IdEmissive) ? m.GetColor(IdEmissive) : Color.clear);
            _mpb.SetVector(IdLight, light);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Re-applies the default (hub) light after a track look was removed.</summary>
        public static void EnsureDefaultAgain() { _globalsSet = false; EnsureGlobals(); }

        // ------------------------------------------------------------------ materials

        static readonly HashSet<string> MissingLogged = new HashSet<string>();

        /// <summary>
        /// The Unity material of a kn5 material. <paramref name="tex"/> resolves a texture slot's FILE name to an uploaded texture (null = missing:
        /// a neutral texture is used).
        /// </summary>
        public static Material Create(Kn5Material m, AcShading s, Func<string, Texture2D> tex)
        {
            EnsureGlobals();
            if (s == null) s = AcShading.Of(m);
            string shaderName = s.Kind == AcShaderKind.Multilayer ? MultilayerShader : PerPixelShader;
            Shader sh = AcShaders.Find(shaderName);
            if (sh == null) return Fallback(m, tex, shaderName);
            var mat = new Material(sh) { name = m != null ? m.Name : "kn5 material" };
            if (m == null) { mat.SetTexture(IdMainTex, AcTextures.White); return mat; }

            mat.SetVector(IdLight, new Vector4(m.Float("ksAmbient", 0.4f), m.Float("ksDiffuse", 0.4f), m.Float("ksSpecular", 0f), Mathf.Max(1f, m.Float("ksSpecularEXP", 10f))));
            var em = m.Property("ksEmissive");
            Color emissive = Color.black;
            if (em != null) emissive = (em.C[0] != 0f || em.C[1] != 0f || em.C[2] != 0f) ? new Color(em.C[0], em.C[1], em.C[2], 1f) : new Color(em.A, em.A, em.A, 1f);
            mat.SetColor(IdEmissive, emissive);
            float fresnelMode = !s.Reflection ? 0f : s.ReflectionAdd ? 2f : 1f;
            mat.SetVector(IdFresnel, new Vector4(m.Float("fresnelC", 0f), Mathf.Max(0.01f, m.Float("fresnelEXP", 1f)), m.Float("fresnelMaxLevel", 0f), fresnelMode));
            mat.SetTexture(IdMainTex, Get(tex, m.Texture("txDiffuse"), AcTextures.White));

            if (s.Kind == AcShaderKind.Multilayer)
            {
                mat.SetTexture(IdMaskTex, Get(tex, m.Texture("txMask"), AcTextures.Black));
                mat.SetTexture(IdDetailR, Get(tex, m.Texture("txDetailR"), AcTextures.White));
                mat.SetTexture(IdDetailG, Get(tex, m.Texture("txDetailG"), AcTextures.White));
                mat.SetTexture(IdDetailB, Get(tex, m.Texture("txDetailB"), AcTextures.White));
                mat.SetTexture(IdDetailA, Get(tex, m.Texture("txDetailA"), AcTextures.White));
                mat.SetTexture(IdDetailNM, s.LayerNormal ? Get(tex, m.Texture("txDetailNM"), AcTextures.FlatNormal) : AcTextures.FlatNormal);
                mat.SetTexture(IdNormalTex, s.NormalMap ? Get(tex, m.Texture("txNormal"), AcTextures.FlatNormal) : AcTextures.FlatNormal);
                // ksMultilayer: the details are projected from above in world metres (the shader: x = -Unity x, z), detail = w * multX (value A,
                // 0.02..6 measured: 0.8 = 1.25 m asphalt tiles); _objsp keeps 0 there and two numbers in B (13.05,30 ...): the u / v scales of
                // the detail on the MESH uv (_Flags.x = 1) [BELIEF - shader not documented; lab pictures 2026-11-01]
                Vector4 mu, mv;
                if (s.ObjectSpaceLayers)
                {
                    mu = new Vector4(B(m, "multR", 0), B(m, "multG", 0), B(m, "multB", 0), B(m, "multA", 0));
                    mv = new Vector4(B(m, "multR", 1), B(m, "multG", 1), B(m, "multB", 1), B(m, "multA", 1));
                }
                else
                {
                    mu = new Vector4(m.Float("multR", 1f), m.Float("multG", 1f), m.Float("multB", 1f), m.Float("multA", 1f));
                    mv = mu;
                }
                mat.SetVector(IdMultU, mu);
                mat.SetVector(IdMultV, mv);
                float magic = m.Float("magicMult", 1f);
                mat.SetVector(IdLayer, new Vector4(magic > 0f ? magic : 1f, m.Float("tarmacSpecularMultiplier", 1f), m.Float("detailNMMult", 0f), s.LayerNormal ? 1f : 0f));
                mat.SetVector(IdFlags, new Vector4(s.ObjectSpaceLayers ? 1f : 0f, s.NormalMap ? 1f : 0f, 0f, 0f));
                SetBlend(mat, s, 2f);
                return mat;
            }

            bool normalMap = s.NormalMap;
            if (s.BrokenGlass)
            {
                // ksBrokenGlass (G0, always): broken-glass mode, glassDamage 0 on this SHARED material = invisible; C's per-car clone sets 1.
                // The crack map is the install's common texture, only when the car was built with damage textures (else no normal, still invisible).
                Texture2D crack = normalMap ? DamageGlassTexture : null;
                normalMap = crack != null;
                mat.SetTexture(IdNormalTex, crack != null ? crack : AcTextures.FlatNormal);
            }
            else mat.SetTexture(IdNormalTex, normalMap ? Get(tex, m.Texture("txNormal"), AcTextures.FlatNormal) : AcTextures.FlatNormal);
            Vector4 dset = Vector4.zero;
            if (s.DamagePaint)
            {
                // damage paint slots (resolved by SLOT; a missing file - lotus_elise_sc mask, NULL.dds - is black = no damage there)
                mat.SetTexture(IdDamageTex, Get(tex, m.Texture("txDamage"), AcTextures.Black));
                mat.SetTexture(IdDamageMaskTex, s.DamageScalar ? AcTextures.White : Get(tex, m.Texture("txDamageMask"), AcTextures.Black));
                dset.x = 1f;
            }
            if (s.BrokenGlass) dset.w = 1f;
            mat.SetVector(IdDamageSet, dset);
            mat.SetVector(IdDamageScratch, Vector4.zero);
            mat.SetVector(IdDamageDent, Vector4.zero);
            mat.SetVector(IdGlassSet, Vector4.zero);
            mat.SetTexture(IdMapsTex, s.Maps ? Get(tex, m.Texture("txMaps"), AcTextures.White) : AcTextures.White);
            mat.SetTexture(IdDetailTex, s.Detail ? Get(tex, m.Texture("txDetail"), AcTextures.White) : AcTextures.White);
            mat.SetTexture(IdDetailNormalTex, s.DetailNormal ? Get(tex, m.Texture("txNormalDetail"), AcTextures.FlatNormal) : AcTextures.FlatNormal);
            mat.SetVector(IdDetailSet, new Vector4(s.Detail ? 1f : 0f, NonZero(m.Float("detailUVMultiplier", 1f)), s.DetailNormal ? m.Float("detailNormalBlend", 1f) : 0f, 0f));
            mat.SetVector(IdFlags, new Vector4(s.AlphaTest ? s.AlphaRef : 0f, normalMap ? 1f : 0f, s.Maps ? 1f : 0f, s.Kind == AcShaderKind.Unlit ? 1f : 0f));
            var alphaProp = m.Property("alpha");
            float alpha = alphaProp != null && alphaProp.A > 0f ? alphaProp.A : 1f;
            mat.SetVector(IdExtra, new Vector4(alpha, NonZero(m.Float("normalMult", 1f)), NonZero(m.Float("diffuseMult", 1f)), 0f));
            mat.SetVector(IdWave, new Vector4(s.Flag ? m.Float("frequency", 0f) : 0f, s.Flag ? m.Float("distortion", 0f) : 0f, m.Float("sunSpecular", 0f), m.Float("sunSpecularEXP", 0f)));
            // foliage, grass and flags are seen from both sides // ours (AC models most of them double-sided; Cull Off is safe for them)
            SetBlend(mat, s, s.Grass || s.Tree || s.Flag ? 0f : 2f);
            return mat;
        }

        static float B(Kn5Material m, string name, int i) { var p = m.Property(name); return p != null ? p.B[i] : 0f; }
        static float NonZero(float v) { return v > 0f ? v : 1f; }

        static Texture2D Get(Func<string, Texture2D> tex, string file, Texture2D fallback)
        {
            if (string.IsNullOrEmpty(file) || tex == null) return fallback;
            Texture2D t = null;
            try { t = tex(file); } catch (Exception) { t = null; }
            return t != null ? t : fallback;
        }

        static void SetBlend(Material mat, AcShading s, float cull)
        {
            mat.SetFloat(IdCull, cull);
            if (s.Blend)
            {
                mat.SetFloat(IdSrcBlend, (float)BlendMode.SrcAlpha);
                mat.SetFloat(IdDstBlend, (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat(IdZWrite, s.NoDepthWrite ? 0f : 1f);
                mat.renderQueue = QueueTransparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                // the depth passes must not draw a blended surface
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("DepthNormals", false);
                mat.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                mat.SetFloat(IdSrcBlend, (float)BlendMode.One);
                mat.SetFloat(IdDstBlend, (float)BlendMode.Zero);
                mat.SetFloat(IdZWrite, 1f);
                mat.renderQueue = s.AlphaTest ? QueueCutout : QueueOpaque;
                mat.SetOverrideTag("RenderType", s.AlphaTest ? "TransparentCutout" : "Opaque");
            }
        }

        static Material Fallback(Kn5Material m, Func<string, Texture2D> tex, string wanted)
        {
            if (MissingLogged.Add(wanted)) AcLog.Warn("Shaders: '" + wanted + "' is not carried - kn5 materials fall back to URP Lit (colours off, no AC shading)");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null) return null;
            var mat = new Material(lit) { name = (m != null ? m.Name : "kn5") + " (fallback)" };
            if (m != null) mat.SetTexture(IdBaseMap, Get(tex, m.Texture("txDiffuse"), AcTextures.White));
            if (mat.HasProperty(IdBaseColor)) mat.SetColor(IdBaseColor, Color.white);
            return mat;
        }
    }
}
