using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] The HUD's pictures, config and fonts, all from the player's install: the files of <see cref="AcHudArtList"/> (content/gui,
    /// content/texture), <see cref="AcHudConfig"/> (cfg/gameplay.ini + Documents override, session_info.ini, skin.ini, locale strings) and the
    /// fonts (Segoe UI TrueType from content/fonts through <see cref="AcHudFonts"/>, bitmap "arial" as the fallback renderer). The disk reads
    /// run on a worker; textures are made on the main thread a few per frame (<see cref="Pump"/>). Nothing ships with the world.
    /// </summary>
    public static class AcHudArt
    {
        /// <summary>Everything is loaded (sprites may still be individually missing: <see cref="Get"/> returns null then).</summary>
        public static bool Ready { get; private set; }
        public static bool Started { get; private set; }
        public static AcHudConfig Config { get; private set; } = new AcHudConfig();
        /// <summary>Fallback bitmap font + texture (null if even that failed).</summary>
        public static AcBitmapFont Bitmap { get; private set; }
        public static Texture2D BitmapTexture { get; private set; }
        public static int Loaded { get; private set; }
        public static int Missing { get; private set; }

        /// <summary>Bitmap font used when no TrueType font exists (harness bmfonts: 100 % clean cuts; bmfont arial_big picture checked)</summary>
        public const string FallbackBitmapFont = "arial_big";   // ours choice: 4096x64, all 94 glyphs inside the image (arial.png loses x..~ past U 1), grey AA

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Texture2D> Textures = new List<Texture2D>();
        private static byte[][] _bytes;
        private static byte[] _bitmapBytes;
        private static AcBitmapFont _bitmapPending;
        private static AcHudConfig _configPending;
        private static volatile bool _readDone;
        private static int _next;
        private static string _root;
        private static AcInstall _install;
        private static string _fontsDir;

        /// <summary>Starts the worker read (idempotent). <paramref name="install"/> = AcGame.Current.Install.</summary>
        public static void Begin(AcInstall install)
        {
            if (Started || install == null) return;
            Started = true;
            _root = install.Root;
            _install = install;
            string docs = null;
            try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch (Exception) { docs = null; }
            string fonts = install.FontsDir;
            _fontsDir = fonts;
            string root = _root;
            var t = new Thread(() => ReadAll(root, fonts, docs)) { IsBackground = true, Name = "Assetto HUD art" };
            t.Start();
        }

        private static void ReadAll(string root, string fontsDir, string docs)
        {
            try
            {
                var files = AcHudArtList.All;
                var bytes = new byte[files.Length][];
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        string p = Path.Combine(root, files[i].Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(p)) bytes[i] = File.ReadAllBytes(p);
                    }
                    catch (Exception) { bytes[i] = null; }
                }
                _bytes = bytes;
                _configPending = AcHudConfig.Load(root, docs);
                try { if (_install != null) AcDamageRules.Load(_install); }      // damage displayer values (P's reader, cached per install)
                catch (Exception e) { AcLog.Warn("Hud: damage rules: " + e.Message); }
                string problem;
                _bitmapPending = AcBitmapFont.Load(fontsDir, FallbackBitmapFont, out problem);
                if (_bitmapPending != null) _bitmapBytes = File.ReadAllBytes(_bitmapPending.ImagePath);
            }
            catch (Exception e)
            {
                AcLog.Warn("Hud: reading the HUD art failed: " + e.Message);
            }
            _readDone = true;
        }

        /// <summary>Main thread, once a frame until <see cref="Ready"/>: uploads up to <paramref name="perFrame"/> pictures. Returns Ready.</summary>
        public static bool Pump(int perFrame)
        {
            if (Ready) return true;
            if (!Started || !_readDone) return false;
            var files = AcHudArtList.All;
            int done = 0;
            while (_next < files.Length && done < perFrame)
            {
                int i = _next++;
                done++;
                byte[] b = _bytes != null ? _bytes[i] : null;
                if (b == null) { Missing++; AcLog.WarnOnce("Hud: missing " + files[i]); continue; }
                try
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (!ImageConversion.LoadImage(tex, b, false)) { UnityEngine.Object.Destroy(tex); Missing++; AcLog.WarnOnce("Hud: undecodable " + files[i]); continue; }
                    tex.name = "Ac HUD " + Path.GetFileNameWithoutExtension(files[i]);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Trilinear;
                    tex.anisoLevel = 2;
                    // the 14 damage layers are 512x512 white-on-alpha: DXT5 in memory (~0.35 MB each instead of ~1.4 MB with mips) // ours
                    bool dxt = AcHudArtList.IsDamage(files[i]) && (tex.width & 3) == 0 && (tex.height & 3) == 0;
                    if (dxt) { tex.Apply(true, false); tex.Compress(true); }     // mips first (still readable), then DXT5 of every level
                    tex.Apply(!dxt, true);       // mipmaps (the VR panel sees them small), then drop the CPU copy
                    Textures.Add(tex);
                    Vector4 border = Vector4.zero;
                    // currentLapBg (476x26) keeps its red tip when stretched to a row: 9-slice with a 12 px left border // ours
                    if (files[i] == AcHudArtList.CurrentLapBg) border = new Vector4(12f, 0f, 2f, 0f);
                    // white_line (265x40) rows: 6 px ends // ours
                    if (files[i] == AcHudArtList.WhiteLine) border = new Vector4(6f, 0f, 6f, 0f);
                    var rect = new Rect(0f, 0f, tex.width, tex.height);
                    if (AcHudArtList.IsDamage(files[i]) && tex.width == tex.height)
                    {
                        float k = tex.width / AcHudArtList.CropOf;      // the inked middle of the 512 layer (AcHudArtList.Crop*), same for every layer
                        rect = new Rect(AcHudArtList.CropX * k, AcHudArtList.CropY * k, AcHudArtList.CropW * k, AcHudArtList.CropH * k);
                    }
                    var sp = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
                    sp.name = tex.name;
                    Sprites[files[i]] = sp;
                    Loaded++;
                }
                catch (Exception e) { Missing++; AcLog.WarnOnce("Hud: " + files[i] + ": " + e.Message); }
            }
            if (_next < files.Length) return false;

            Config = _configPending ?? new AcHudConfig();
            LoadBitmapFont();
            AcHudFonts.Build(_install, _fontsDir);          // the same AcInstall.FontsDir the worker read the bitmap font from
            _bytes = null;
            Ready = true;
            AcLog.Info("Hud: art loaded (" + Loaded + " images" + (Missing > 0 ? ", " + Missing + " missing" : "") + ", fonts: " + AcHudFonts.Describe()
                + ", bitmap " + (Bitmap != null ? Bitmap.ToString() : "none") + "; config: " + Config.Read.Count + " files, language " + Config.Language
                + " (" + Config.StringCount + " strings), USE_MPH " + (Config.UseMph ? 1 : 0) + ", TIME_DIFFERENCE " + (Config.TimeDifference ? 1 : 0) + ")");
            return true;
        }

        private static void LoadBitmapFont()
        {
            if (_bitmapPending == null || _bitmapBytes == null) return;
            try
            {
                Texture2D tex;
                if (_bitmapPending.ImageIsBmp)
                {
                    int w, h; string problem;
                    byte[] rgba = AcBitmapFont.DecodeBmp(_bitmapBytes, out w, out h, out problem);
                    if (rgba == null) return;
                    _bitmapPending.MeasureInk(rgba, w, h);
                    tex = Upload(rgba, w, h);
                }
                else
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(tex, _bitmapBytes, false)) { UnityEngine.Object.Destroy(tex); return; }
                    // AC font images are light glyphs on black: alpha from brightness (AcBitmapFont.LuminanceToAlpha, harness-verified on 58 fonts)
                    Color32[] px = tex.GetPixels32();
                    var rgba = new byte[px.Length * 4];
                    for (int i = 0; i < px.Length; i++) { rgba[i * 4] = px[i].r; rgba[i * 4 + 1] = px[i].g; rgba[i * 4 + 2] = px[i].b; rgba[i * 4 + 3] = px[i].a; }
                    AcBitmapFont.LuminanceToAlpha(rgba);
                    _bitmapPending.MeasureInk(rgba, tex.width, tex.height, true);   // GetPixels32 rows are bottom-up
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
                    tex.SetPixels32(px);
                    tex.Apply(false, true);
                }
                tex.name = "Ac HUD font " + _bitmapPending.Name;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                Textures.Add(tex);
                Bitmap = _bitmapPending;
                BitmapTexture = tex;
            }
            catch (Exception e) { AcLog.Warn("Hud: bitmap font " + FallbackBitmapFont + " failed: " + e.Message); }
            _bitmapBytes = null;
        }

        /// <summary>RGBA rows top-down -> a texture (Unity rows are bottom-up).</summary>
        public static Texture2D Upload(byte[] rgba, int w, int h)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w * 4, dst = y * w;
                for (int x = 0; x < w; x++, src += 4) px[dst + x] = new Color32(rgba[src], rgba[src + 1], rgba[src + 2], rgba[src + 3]);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The sprite of an <see cref="AcHudArtList"/> file, or null (missing / not loaded yet).</summary>
        public static Sprite Get(string rel)
        {
            Sprite s;
            return rel != null && Sprites.TryGetValue(rel, out s) ? s : null;
        }

        /// <summary>Frees every texture (world closing).</summary>
        public static void Release()
        {
            foreach (var s in Sprites.Values) if (s != null) UnityEngine.Object.Destroy(s);
            foreach (var t in Textures) if (t != null) UnityEngine.Object.Destroy(t);
            Sprites.Clear(); Textures.Clear();
            if (BitmapTexture != null) BitmapTexture = null;
            Bitmap = null;
            AcHudFonts.Release();
            Ready = false; Started = false; _readDone = false; _next = 0; _bytes = null; Loaded = 0; Missing = 0;
        }
    }
}
