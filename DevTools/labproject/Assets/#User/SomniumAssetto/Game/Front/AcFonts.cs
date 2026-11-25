using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] Assetto Corsa's own UI fonts: the TrueType Segoe UI faces it ships in content/fonts (segoeui.ttf body, segoeuil_0.ttf Light for
    /// titles, seguisb.ttf Semibold - the launcher's css uses "Segoe UI Light" / "Segoe UI"), made into TextMeshPro font assets at run time with
    /// TMP_FontAsset.CreateFontAsset(path, ...) - a dynamic atlas filled from the file as glyphs are needed (research tablet_front.md section 8).
    /// Shader caveat: CreateFontAsset makes its material with "TextMeshPro/Mobile/Distance Field"; when the client build has no such shader the
    /// call would throw after loading the face, so it is checked first and the font falls back to TextMeshPro's default (one log line says which).
    /// Texts made before the fonts exist are remembered and switched when they are built.
    /// </summary>
    public static class AcFonts
    {
        public enum Face { Regular = 0, Light = 1, Bold = 2 }

        private static readonly string[] Files = { "segoeui.ttf", "segoeuil_0.ttf", "seguisb.ttf" };
        private static readonly string[] Fallbacks = { null, "segoeuisl_0.ttf", "segoeuib.ttf" };
        private static readonly TMP_FontAsset[] Built = new TMP_FontAsset[3];
        private static readonly List<KeyValuePair<TMP_Text, Face>> Waiting = new List<KeyValuePair<TMP_Text, Face>>();
        private static bool _tried, _failed;

        /// <summary>Lab / off switch: false keeps TextMeshPro's default font.</summary>
        public static bool UseGameFonts = true;
        /// <summary>The Segoe UI regular face is built.</summary>
        public static bool Ready { get { return Built[0] != null; } }
        public static string Report { get; private set; } = "not built yet";

        public static TMP_FontAsset Get(Face f) { return Built[(int)f] ?? Built[0]; }

        public static void Apply(TMP_Text t, Face face)
        {
            if (t == null) return;
            TMP_FontAsset f = Get(face);
            if (f != null) { t.font = f; return; }
            if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            if (!_failed && UseGameFonts && Waiting.Count < 20000) Waiting.Add(new KeyValuePair<TMP_Text, Face>(t, face));
        }

        /// <summary>Builds the faces from <paramref name="install"/>'s content/fonts (main thread, once). Logs "Fonts: Segoe UI built from ...".</summary>
        public static void Build(AcInstall install)
        {
            if (_tried) return;
            _tried = true;
            if (!UseGameFonts || install == null) { _failed = true; Waiting.Clear(); return; }
            Shader mobile = null, desktop = null;
            try { mobile = AcShaders.Find("TextMeshPro/Mobile/Distance Field"); desktop = AcShaders.Find("TextMeshPro/Distance Field"); }
            catch (Exception) { }
            if (mobile == null)
            {
                _failed = true;
                Waiting.Clear();
                Report = "the shader TextMeshPro/Mobile/Distance Field is not in this client (TextMeshPro/Distance Field " + (desktop != null ? "is" : "is not either") + ") - TextMeshPro's default font stays";
                AcLog.Warn("Fonts: Segoe UI not built: " + Report + ".");
                return;
            }

            var made = new List<string>();
            for (int i = 0; i < Files.Length; i++)
            {
                string path = Path.Combine(install.FontsDir, Files[i]);
                if (!File.Exists(path) && Fallbacks[i] != null) path = Path.Combine(install.FontsDir, Fallbacks[i]);
                if (!File.Exists(path)) continue;
                try
                {
                    // 90 pt sampling, 9 padding, SDFAA, 1024 atlas: TMP's own defaults for CreateFontAsset(Font) // ours (TMP's numbers)
                    TMP_FontAsset f = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (f == null) continue;
                    f.name = "Ac " + Path.GetFileNameWithoutExtension(path);
                    if (TMP_Settings.defaultFontAsset != null) f.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
                    Built[i] = f;
                    made.Add(Path.GetFileName(path));
                }
                catch (Exception e)
                {
                    AcLog.Warn("Fonts: " + Path.GetFileName(path) + " failed (" + e.GetType().Name + ": " + e.Message + ").");
                }
            }

            if (Built[0] == null)
            {
                _failed = true;
                Waiting.Clear();
                Report = "no face could be read from " + install.FontsDir + " - TextMeshPro's default font stays";
                AcLog.Warn("Fonts: Segoe UI not built: " + Report + ".");
                return;
            }

            int n = 0;
            for (int i = 0; i < Waiting.Count; i++)
            {
                TMP_Text t = Waiting[i].Key;
                if (t == null) continue;
                t.font = Get(Waiting[i].Value);
                n++;
            }

            Waiting.Clear();
            Material m = Built[0].material;
            Report = string.Join(", ", made.ToArray()) + " from " + install.FontsDir + " (shader " + (m != null && m.shader != null ? m.shader.name : "?") + ")";
            AcLog.Info("Fonts: Segoe UI built from " + Report + "; " + n + " texts switched to it.");
        }

        /// <summary>World closing: the font assets and their atlases go (a next visit builds them again).</summary>
        public static void Shutdown()
        {
            for (int i = 0; i < Built.Length; i++)
            {
                TMP_FontAsset f = Built[i];
                Built[i] = null;
                if (f == null) continue;
                try
                {
                    if (f.atlasTextures != null) foreach (Texture2D t in f.atlasTextures) if (t != null) UnityEngine.Object.Destroy(t);
                    if (f.material != null) UnityEngine.Object.Destroy(f.material);
                    UnityEngine.Object.Destroy(f);
                }
                catch (Exception) { }
            }

            Waiting.Clear();
            _tried = false;
            _failed = false;
            Report = "not built yet";
        }
    }
}
