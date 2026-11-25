using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] AC -> Unity conversions used by the car code (ac_car_formats.md 1.5): AC +X = car LEFT -> Unity (-x, y, z); matrices
    /// U = S * transpose(M) * S. And the emissive / unlit material helpers for lights.ini, LEDs and digital displays.
    /// </summary>
    public static class AcCarMath
    {
        /// <summary>AC point / direction -> Unity (X mirrored).</summary>
        public static Vector3 ToUnity(AcVec3 p) { return new Vector3(-p.X, p.Y, p.Z); }

        /// <summary>
        /// Unity local TRS of an AC local matrix (kn5 dummy / ksanim frame / knh node). A mirrored matrix (determinant &lt; 0, not seen in
        /// car anims) keeps its rotation from the un-mirrored axes and a negative X scale.
        /// </summary>
        public static void ToTrs(AcMat4 m, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            // the same decomposition as W's AcKn5Upload.SetLocal, so ksanim frame 0 lands exactly on the built rest pose
            var a = m.ToUnity();
            Vector3 c0 = new Vector3(a[0], a[4], a[8]), c1 = new Vector3(a[1], a[5], a[9]), c2 = new Vector3(a[2], a[6], a[10]);
            scale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
            if (m.Determinant3 < 0f) scale.x = -scale.x;
            pos = new Vector3(a[3], a[7], a[11]);
            rot = scale.y > 1e-8f && scale.z > 1e-8f ? Quaternion.LookRotation(c2 / scale.z, c1 / scale.y) : Quaternion.identity;
        }

        /// <summary>Unity model-space point of an AC kn5 world-space point.</summary>
        public static Vector3 WorldToModel(AcMat4 world, AcVec3 local) { return ToUnity(world.TransformPoint(local)); }
    }

    /// <summary>
    /// Emissive switching (lights.ini, LEDs, displays) through W's <c>AcMaterials.SetEmissive</c> (AC units, the same scale as a kn5
    /// ksEmissive: the World shaders add emissive x txDiffuse), and the unlit material of cockpit digital displays.
    /// </summary>
    public static class AcCarShading
    {
        /// <summary>Kept for callers: emissives go to W's shader 1:1 in AC units now (W's AcMaterials.SetEmissive).</summary>
        public const float EmissiveScale = 1f;

        /// <summary>Bind-time hook per light / LED renderer (nothing to prepare with W's shaders; kept for a fallback material).</summary>
        public static void Prepare(Renderer r) { }

        /// <summary>Sets the emissive colour of one renderer (AC units, e.g. 200,0,0). No allocation.</summary>
        public static void SetEmissive(Renderer r, Vector3 ac)
        {
            if (r == null) return;
            AcMaterials.SetEmissive(r, ac.x, ac.y, ac.z);
        }

        static readonly int IdNormalTex = Shader.PropertyToID("_NormalTex"), IdFlags = Shader.PropertyToID("_Flags");

        /// <summary>
        /// ksPerPixelMultiMap_damage_dirt (the car paint of most cars) carries the DAMAGE normal map in txNormal (458: DAMAGE_CHASSIS_NM.dds,
        /// next to txDamage / txDamageMask): AC blends it in only where the car is damaged. We draw no damage, so the paint must use a flat
        /// normal - drawn as a plain normal map it made every body panel look crumpled (lab cars-458 / physics-flat pictures, 2026-11-01).
        /// Runs once per built kn5 (allocates; build time only); the materials are the scope's shared ones, so this is idempotent.
        /// Returns the number of materials changed.
        /// </summary>
        public static int FlattenDamageNormals(AcKn5Model k)
        {
            if (k == null || k.Kn5 == null || k.Renderers == null) return 0;
            HashSet<string> names = null;
            var mats = k.Kn5.Materials;
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                if (m == null || m.Name == null || !string.Equals(m.Shader, "ksPerPixelMultiMap_damage_dirt", StringComparison.Ordinal)) continue;
                if (names == null) names = new HashSet<string>(StringComparer.Ordinal);
                names.Add(m.Name);
            }
            if (names == null) return 0;
            int changed = 0;
            for (int r = 0; r < k.Renderers.Count; r++)
            {
                var ren = k.Renderers[r];
                if (ren == null) continue;
                var shared = ren.sharedMaterials;
                for (int j = 0; j < shared.Length; j++)
                {
                    var mat = shared[j];
                    if (mat == null || !names.Contains(mat.name) || !mat.HasProperty(IdFlags)) continue;
                    Vector4 f = mat.GetVector(IdFlags);
                    if (f.y == 0f && mat.GetTexture(IdNormalTex) == AcTextures.FlatNormal) continue;
                    f.y = 0f;
                    mat.SetVector(IdFlags, f);
                    if (mat.HasProperty(IdNormalTex)) mat.SetTexture(IdNormalTex, AcTextures.FlatNormal);
                    changed++;
                }
            }
            return changed;
        }

        static Shader _unlit;
        static bool _unlitLogged;

        /// <summary>
        /// A transparent unlit material showing <paramref name="tex"/> times the vertex colour (bitmap-font text, RPM graphs). Uses a stock
        /// shader the client ships (Sprites/Default, else UI/Default) through AcShaders (carried first). Null when none exists (logged once).
        /// </summary>
        public static Material CreateUnlit(Texture tex)
        {
            if (_unlit == null)
            {
                _unlit = AcShaders.Find("Sprites/Default");
                if (_unlit == null) _unlit = AcShaders.Find("UI/Default");
                if (_unlit == null && !_unlitLogged) { _unlitLogged = true; AcLog.Warn("Car: no unlit shader for cockpit displays (Sprites/Default, UI/Default missing) - digital displays off"); }
            }
            if (_unlit == null) return null;
            var m = new Material(_unlit) { name = "Ac display " + (tex != null ? tex.name : "") };
            m.mainTexture = tex;
            m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual); // UI/Default: never "always on top" from an overlay canvas
            m.renderQueue = 3001; // ours: after the car's own transparent glass
            return m;
        }
    }
}
