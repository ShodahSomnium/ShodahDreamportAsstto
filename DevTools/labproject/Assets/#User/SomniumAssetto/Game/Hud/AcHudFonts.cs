using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] AC's HUD typeface, built at run time from the TrueType files in the player's <c>content/fonts</c>. acs.exe loads
    /// "content/fonts/*.ttf" and names "Segoe UI" as its UI face (UTF-16 strings in acs.exe, H NOTES), so the HUD uses Segoe UI regular
    /// (segoeui.ttf), semibold (seguisb.ttf) and light (segoeuil_0.ttf). Recipe research tablet_front.md 8: TMP dynamic SDF font asset from
    /// the file; TMP makes its material with "TextMeshPro/Mobile/Distance Field" (unknown in the client build - a carrier material is
    /// requested from the lead), so we check that shader first (TMP would throw on a null shader), then switch the material to
    /// "TextMeshPro/Distance Field" (proven: ETS client builds) when it is there. Fallbacks: TMP's default font asset, then AC's bitmap font
    /// (<see cref="AcBitmapText"/>) - every label still draws. When F's tablet fonts (AcFonts) are built they are used instead (one
    /// atlas per face for the whole world).
    /// </summary>
    public static class AcHudFonts
    {
        public static TMP_FontAsset Regular { get; private set; }
        public static TMP_FontAsset Semibold { get; private set; }
        public static TMP_FontAsset Light { get; private set; }
        /// <summary>Where the fonts came from ("Segoe UI from the install", "TMP default", "none").</summary>
        public static string Source { get; private set; } = "none";
        private static bool _built, _shared;

        /// <summary>Atlas sampling size / padding / size. // ours (64 pt SDF with 6 px padding reads well from 14 to 120 px on screen)</summary>
        private const int SamplePoint = 64, Padding = 6, Atlas = 1024;

        public static void Build(AcInstall install, string fontsDir)
        {
            if (_built) return;
            _built = true;
            // F builds the same Segoe UI faces for the tablet (AcFonts, REQUESTS "F -> H"): share them so the dynamic atlases exist once.
            try
            {
                AcFonts.Build(install);                 // idempotent; F's own boot may have done it already
                if (AcFonts.Ready)
                {
                    Regular = AcFonts.Get(AcFonts.Face.Regular);
                    Semibold = AcFonts.Get(AcFonts.Face.Bold);       // F's "Bold" face is seguisb.ttf (Semibold)
                    Light = AcFonts.Get(AcFonts.Face.Light);
                    _shared = true;
                    Source = "Segoe UI shared with the tablet (" + AcFonts.Report + ")";
                    return;
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Hud: the tablet's fonts are not usable (" + e.Message + "), building the HUD's own"); }
            try
            {
                Shader mobile = AcShaders.Find("TextMeshPro/Mobile/Distance Field");
                Shader sdf = AcShaders.Find("TextMeshPro/Distance Field");
                if (mobile != null)
                {
                    Regular = Make(fontsDir, "segoeui.ttf", sdf);
                    Semibold = Make(fontsDir, "seguisb.ttf", sdf) ?? Regular;
                    Light = Make(fontsDir, "segoeuil_0.ttf", sdf) ?? Regular;
                    if (Regular != null) Source = "Segoe UI from the install (" + (sdf != null ? "Distance Field" : "Mobile Distance Field") + " shader)";
                }
                else AcLog.Warn("Hud: TMP shader 'TextMeshPro/Mobile/Distance Field' is not in this client - Segoe UI cannot be built (lead: carrier material)");
                if (Regular == null)
                {
                    // TMP's default font only helps when its own material's shader exists and runs in this client; otherwise every label would
                    // draw pink / invisible, so leave Regular null and let AcHudText use AC's bitmap font (AcBitmapText, UI/Default material)
                    TMP_FontAsset def = TMP_Settings.defaultFontAsset;
                    Material dm = def != null ? def.material : null;
                    Shader ds = dm != null ? dm.shader : null;
                    if (def != null && ds != null && ds.isSupported)
                    {
                        Regular = Semibold = Light = def;
                        Source = "TMP default font (" + def.name + ", shader " + ds.name + ")";
                    }
                    else
                    {
                        Source = "AC bitmap font (no usable TMP font: " + (def == null ? "no TMP default font" : ds == null ? "the TMP default font has no shader"
                            : "shader " + ds.name + " not supported") + ")";
                    }
                    AcLog.Info("Hud: fonts - " + Source);
                }
            }
            catch (Exception e)
            {
                AcLog.Warn("Hud: building the fonts failed: " + e.Message);
            }
        }

        private static TMP_FontAsset Make(string dir, string file, Shader sdf)
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) { AcLog.WarnOnce("Hud: font file missing: " + path); return null; }
            try
            {
                TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(path, 0, SamplePoint, Padding, GlyphRenderMode.SDFAA, Atlas, Atlas);
                if (fa == null) { AcLog.WarnOnce("Hud: TMP could not load " + file); return null; }
                fa.name = "Ac HUD " + Path.GetFileNameWithoutExtension(file);
                if (sdf != null && fa.material != null) fa.material.shader = sdf;
                return fa;
            }
            catch (Exception e)
            {
                AcLog.WarnOnce("Hud: font " + file + " failed: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /// <summary>The font of a weight (0 regular, 1 semibold, 2 light), or null (use the bitmap fallback).</summary>
        public static TMP_FontAsset Of(int weight)
        {
            return weight == 1 ? Semibold : weight == 2 ? Light : Regular;
        }

        public static string Describe() { return Source; }

        public static void Release()
        {
            if (!_shared)   // shared faces belong to F (AcFonts.Shutdown frees them)
            {
                DestroyFont(Regular);
                if (Semibold != Regular) DestroyFont(Semibold);
                if (Light != Regular && Light != Semibold) DestroyFont(Light);
            }
            Regular = Semibold = Light = null;
            Source = "none";
            _built = false; _shared = false;
        }

        private static void DestroyFont(TMP_FontAsset f)
        {
            if (f == null || f == TMP_Settings.defaultFontAsset) return;
            try
            {
                if (f.atlasTextures != null) foreach (var t in f.atlasTextures) if (t != null) UnityEngine.Object.Destroy(t);
                if (f.material != null) UnityEngine.Object.Destroy(f.material);
                UnityEngine.Object.Destroy(f);
            }
            catch (Exception) { }
        }
    }
}
