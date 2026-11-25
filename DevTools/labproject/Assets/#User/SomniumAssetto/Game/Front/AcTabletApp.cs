using System;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The "ASSETTO CORSA" app on the Somnium tablet (Halo's HaloTabletApp structure, ETS's link): the same menu the hub panel shows
    /// (AcMenu, built by AcFront) on the home screen of the client's own tablet, VR and desktop (AcTabletLink finds and borrows it). The hub panel
    /// stays too (BO2's rule). The app is not offered while a track loads; the client's tablet is closed when a race goes to the grid.
    /// AcFront.Stop -> <see cref="Shutdown"/> takes everything off the client's tablet (it outlives the world). Steady state: a few reads a frame.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    public sealed class AcTabletApp : MonoBehaviour
    {
        public static AcTabletApp Instance { get; private set; }

        public AcTabletLink Link { get { return _link; } }
        public AcMenu Menu { get { return _menu; } }
        /// <summary>The icon texture now (the launcher logo after the boot) and whether the logo was painted on it.</summary>
        public Texture2D Icon { get { return _icon; } }
        public bool IconFromInstall { get { return _iconFromInstall; } }

        private AcGame _game;
        private AcTabletLink _link;
        private AcMenu _menu;
        private Texture2D _icon;
        private bool _tornDown, _iconFromInstall;
        private AcPhase _lastPhase = AcPhase.Hub;

        public static AcTabletApp Create(AcGame game)
        {
            if (Instance != null) Shutdown();
            AcTabletApp app = game.gameObject.AddComponent<AcTabletApp>();
            app._game = game;
            app.Setup();
            return app;
        }

        public static void Shutdown()
        {
            AcTabletApp app = Instance;
            if (app != null) app.Teardown();
        }

        private void Awake() { Instance = this; }

        private void Setup()
        {
            _icon = AcTabletIcon.Paint(128, null);
            _link = _game.gameObject.AddComponent<AcTabletLink>();
            _link.AppIcon = _icon;
            _link.FrameWidth = AcMenu.Width;
            _link.FrameHeight = AcMenu.Height;
            _link.LinkChanged += OnLinkChanged;
            AcLog.Info("Tablet: searching for the Somnium tablet (the \"" + _link.AppTitle + "\" app goes on its home screen; the hub panel stays the menu too).");
        }

        /// <summary>The launcher's own logo (launcher/themes/default/graphics/logo.png, read now) on the icon's red slab.</summary>
        public void PaintIconFromInstall(AcInstall install)
        {
            if (_iconFromInstall || install == null || _link == null) return;
            _iconFromInstall = true;
            try
            {
                string path = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(install.Root, "launcher"), "themes"), "default"), "graphics"), "logo.png");
                if (!File.Exists(path)) return;
                var logo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(logo, File.ReadAllBytes(path), false)) { Destroy(logo); return; }
                Texture2D painted = AcTabletIcon.Paint(128, logo);
                Destroy(logo);
                Texture2D old = _icon;
                _icon = painted;
                _link.AppIcon = painted;
                _link.RefreshIcon(); // the button's sprite moves to the new texture before the old one goes
                if (old != null) Destroy(old);
                AcLog.Info("Tablet: the app icon carries the launcher's logo (" + path + ").");
            }
            catch (Exception e) { AcLog.Warn("Tablet: the logo for the app icon could not be read (" + e.Message + ") - the plain icon stays."); }
        }

        private void OnDestroy()
        {
            Teardown();
            if (Instance == this) Instance = null;
        }

        private void Teardown()
        {
            if (_tornDown) return;
            _tornDown = true;
            enabled = false;
            try
            {
                if (_link != null)
                {
                    _link.LinkChanged -= OnLinkChanged;
                    _link.Shutdown();
                    Destroy(_link);
                }

                if (_icon != null) Destroy(_icon);
            }
            catch (Exception e)
            {
                AcLog.Warn("Tablet: shutting the app down failed: " + e.GetType().Name + ": " + e.Message);
            }

            _link = null;
            _icon = null;
            _menu = null;
            if (Instance == this) Instance = null;
            if (this != null) Destroy(this);
        }

        private void Update()
        {
            if (_game == null || _link == null) return;
            bool linked = _link.IsLinked;
            if (_menu != null && _menu.Root == null) _menu = null;
            if (linked && _menu == null && _link.Frame != null) BuildMenu();
            _link.ForceLarge = AcSettings.Flag("front.large", true);
            _link.CarryWithVehicle = AcSettings.Flag("front.carry", true);

            AcPhase phase = AcSession.Phase;
            if (phase != _lastPhase)
            {
                if (linked && (phase == AcPhase.Loading || phase == AcPhase.Grid || phase == AcPhase.Countdown))
                    _link.CloseTablet(phase == AcPhase.Loading ? "a track is loading" : "the race goes to the grid");
                if (_menu != null && phase == AcPhase.Results) _menu.Show("timing");
                _lastPhase = phase;
            }

            _link.AppAvailable = phase != AcPhase.Loading;
        }

        private void BuildMenu()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _menu = AcFront.BuildMenu(_link.Frame, true);
            if (_menu != null) _menu.ApplyLayersHook = _link.ApplyLayers;
            _link.ApplyLayers();
            AcLog.Info("Tablet: the menu was built into the app frame in " + watch.ElapsedMilliseconds + " ms.");
        }

        /// <summary>The install was read: the menu is built again with the game's art, words and colours.</summary>
        public void RebuildMenu()
        {
            if (_menu == null || _menu.Root == null) return;
            string page = _menu.Current != null ? _menu.Current.Id : null;
            AcFront.ForgetMenu(_menu);
            Destroy(_menu.Root.gameObject);
            _menu = null;
            if (_link != null && _link.IsLinked && _link.Frame != null)
            {
                BuildMenu();
                if (_menu != null && page != null) _menu.Show(page);
            }
        }

        /// <summary>Widgets added later go onto the tablet's UI layer (the link also re-applies it at 4 Hz while the app is open).</summary>
        private void LateUpdate()
        {
            if (_link != null && _menu != null && Time.frameCount % 30 == 0) _link.ApplyLayers();
        }

        private void OnLinkChanged()
        {
            if (_link == null || !_link.IsLinked)
            {
                if (_menu != null) AcFront.ForgetMenu(_menu);
                _menu = null;
            }
        }
    }

    /// <summary>The app's home-screen icon, drawn here: a rounded slab in AC's red (skin.ini TITLE_BACK / the launcher accent) with the
    /// launcher's own logo on it when the install is read (nothing of the game is shipped: the logo is read at run time).</summary>
    public static class AcTabletIcon
    {
        public static Texture2D Paint(int n, Texture2D logo)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "AC.Tablet.IconTexture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[n * n];
            Color slab = Color.Lerp(AcUi.Accent, Color.black, 0.15f), rim = AcUi.Accent;
            Color32[] lp = null;
            int lw = 0, lh = 0;
            float scale = 0f, ox = 0f, oy = 0f;
            if (logo != null)
            {
                try { lp = logo.GetPixels32(); lw = logo.width; lh = logo.height; } catch (Exception) { lp = null; }
                if (lp != null)
                {
                    scale = Mathf.Min(n * 0.72f / lw, n * 0.72f / lh);
                    ox = (n - lw * scale) * 0.5f; oy = (n - lh * scale) * 0.5f;
                }
            }

            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x - c) / n, v = (y - c) / n;
                    float box = RoundBox(u, v, 0.47f, 0.47f, 0.17f);
                    Color col = Color.Lerp(slab, rim, Cover(Mathf.Abs(box + 0.025f) - 0.012f, n));
                    if (lp != null)
                    {
                        int sx = (int)((x - ox) / scale), sy = (int)((y - oy) / scale);
                        if (sx >= 0 && sy >= 0 && sx < lw && sy < lh)
                        {
                            Color32 l = lp[sy * lw + sx];
                            col = Color.Lerp(col, new Color(l.r / 255f, l.g / 255f, l.b / 255f, 1f), l.a / 255f);
                        }
                    }
                    else
                    {
                        // no logo yet: a white chequered-flag band (ours)
                        if (Mathf.Abs(v) < 0.12f && Mathf.Abs(u) < 0.32f && ((int)((u + 1f) * 12f) + (int)((v + 1f) * 12f)) % 2 == 0) col = Color.white;
                    }

                    col.a = Cover(box, n);
                    pixels[y * n + x] = col;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static float Cover(float d, int n) { return Mathf.Clamp01(0.5f - d * n); }

        private static float RoundBox(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - hw + r, qy = Mathf.Abs(y) - hh + r;
            float o = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return o + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
    }
}
