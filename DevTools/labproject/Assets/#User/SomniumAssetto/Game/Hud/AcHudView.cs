using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] The HUD's widgets, built once under a host's root (monitor overlay <see cref="AcHudDesk"/> or VR dash panel
    /// <see cref="AcHudVr"/>) and updated every frame from an <see cref="AcHudFeed"/> without allocating. Pictures and fonts are AC's own
    /// (<see cref="AcHudArt"/>); positions are ours, matched to AC's default HUD (H NOTES.md "Layout"):
    /// <list type="bullet">
    /// <item>Dial (bottom centre): content/texture rpm_off + rpm_on rings (rpm_on filled over its own arc 30..328 degrees from straight
    /// down, red zone in the art from 297 degrees, flashing at the limiter), turbo_off/on inner ring for turbo cars, gear, speed (Km/h | mph),
    /// ABS / TC icons when they act, fuel ruler (gui/essential/fuelBar) + fuel warning / out-of-fuel icon. Alternative "Essentials" style:
    /// horizontal rpm bar over gui/essential/rpmBar (red zone 0.917..0.992 of its width), gear, speed, fuel. The straight bars are drawn by
    /// sizing their rect (a sprite-less Image ignores Type.Filled and would always draw full).</item>
    /// <item>Lap times (top left, the "PlayerCurrentTime" app art): session box in session_info.ini colours (PRACTICE / QUALIFY / RACE /
    /// HOTLAP, clock, infinity_icon when untimed, or LAP n/N; hidden per session type by session_info.ini *_VISIBLE), white_line rows:
    /// current lap (OUT LAP, FINISH + race time, red when cut), best (best.png), last (last.png), POS. p/n (racers only).</item>
    /// <item>Time difference (top centre at gameplay.ini [TIME_DIFFERENCE] POS_Y): live delta to the best lap, green faster / red slower
    /// with the up / down arrows; under it the last sector split (time + delta to the best lap's sector) for 5 s.</item>
    /// <item>Standings tower (left): top 10 + me, own row on sessionRanking/currentLapBg, gaps or best laps; spectators (late joiners) greyed
    /// with no position after the racers.</item>
    /// <item>Start lights (top centre, texture_trafficlight_off/red/green x5, gui/drag/false_start.png with "FALSE START!"), flags (gui/flags:
    /// chequered from the leader's finish), wrong way (texture/wrongway.png), pit limiter banner, messages (gui/systemMessage/bar.png).</item>
    /// </list>
    /// VR (<see cref="Vr"/>): every text is at least <see cref="VrMinText"/> units, the tower shows <see cref="VrTowerRows"/> rows.
    /// </summary>
    public sealed class AcHudView
    {
        // ---------------------------------------------------------------- shared helpers

        public static readonly Color White = new Color(1f, 1f, 1f, 1f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        public static readonly Color Green = new Color(0.30f, 0.92f, 0.36f, 1f);   // ours: delta faster (the up.png arrow is this green)
        public static readonly Color Red = new Color(1f, 0.27f, 0.22f, 1f);        // ours: delta slower / cut (the down.png arrow red)
        public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        /// <summary>Dial ring arc (rpm_on / turbo_on): ink from 30 to 328 degrees clockwise from straight down (harness `arc`, VERIFIED).</summary>
        public const float ArcStartDeg = 30f, ArcEndDeg = 328f;
        /// <summary>The dial is drawn at 1.5x the 156 px texture on a 1080-high screen. // ours</summary>
        public const float DialSize = 234f;
        /// <summary>Start lights on the HUD: 5 (the tracks' KS_START_LIGHT_0..4, gui/drag/false_start.png shows 5). // ours count</summary>
        public const int LightCount = 5;
        public const int TowerRows = 11;   // top 10 + me // ours (brief)
        /// <summary>VR tower: top 6 + me, rows tall enough for <see cref="VrMinText"/>. // ours</summary>
        public const int VrTowerRows = 7;
        /// <summary>VR: no text smaller than this (canvas units). At the default panel (0.57 m for 1400 units; its centre ~0.66 m from the eye
        /// once lowered under the dash line) 30 units = 1.22 cm = 1.06 degree em height - above the ~1 degree floor for comfortable VR text. // ours</summary>
        public const float VrMinText = 30f;
        /// <summary>Seconds a sector split stays under the time difference. // ours</summary>
        public const float SplitSeconds = 5f;
        /// <summary>R's false-start message text (REQUESTS "R -> H"); acs.exe has the same "FALSE START!".</summary>
        public const string FalseStartText = "FALSE START!";

        /// <summary>Top-left origin placement in the parent (x right, y down), reference pixels.</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Material _overlayImage;
        private static readonly Dictionary<TMP_FontAsset, Material> OverlayFonts = new Dictionary<TMP_FontAsset, Material>();

        /// <summary>UI/Default with ZTest Always: the VR panel draws over the cockpit (the dash would otherwise cut it). // ours</summary>
        public static Material OverlayImageMaterial()
        {
            if (_overlayImage != null) return _overlayImage;
            Material baseMat = Canvas.GetDefaultCanvasMaterial();
            if (baseMat == null) return null;
            _overlayImage = new Material(baseMat) { name = "Ac HUD overlay (UI)" };
            _overlayImage.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            _overlayImage.renderQueue = 4000;
            return _overlayImage;
        }

        /// <summary>A copy of the font's material with ZTest Always (VR panel).</summary>
        public static Material OverlayFontMaterial(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            Material m;
            if (OverlayFonts.TryGetValue(font, out m) && m != null) return m;
            m = new Material(font.material) { name = font.name + " overlay" };
            m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            m.renderQueue = 4000;
            OverlayFonts[font] = m;
            return m;
        }

        public static void ReleaseMaterials()
        {
            if (_overlayImage != null) Object.Destroy(_overlayImage);
            _overlayImage = null;
            foreach (var m in OverlayFonts.Values) if (m != null) Object.Destroy(m);
            OverlayFonts.Clear();
        }

        // ---------------------------------------------------------------- state

        public RectTransform Root { get; private set; }
        public readonly bool Vr;
        private readonly Material _imgMat;
        private readonly AcHudConfig _cfg;
        private readonly char[] _b = new char[64];

        // groups
        public RectTransform GTimes, GTower, GLights, GDelta, GFlag, GWrong, GMsg, GPit, GDial, GEss;
        /// <summary>Sector split (under the time difference).</summary>
        public RectTransform GSplit;
        /// <summary>Damage displayer (iteration 2): the stacked content/texture/damage layers + optional values and the repair countdown.</summary>
        public RectTransform GDamage;

        // dial
        private Image _rpmOff, _rpmOn, _turboOff, _turboOn, _abs, _tc, _fuelRuler, _fuelFill, _fuelIcon;
        private AcHudText _gear, _speed, _unit;
        private float _fuelW;
        // essentials
        private Image _eBar, _eBarBack, _eRuler, _eFuelRuler, _eFuelFill;
        private AcHudText _eGear, _eSpeed, _eUnit;
        private float _eBarW, _eFuelW;
        // times
        private Image _sessionBack, _sessionInf;
        private AcHudText _sessionName, _sessionRight, _lapLabel, _lapTime, _bestTime, _lastTime, _posLabel, _posValue;
        private GameObject _posRow;
        // delta + split
        private AcHudText _delta, _splitTime, _splitDelta;
        private Image _deltaUp, _deltaDown;
        // tower
        private sealed class Row { public GameObject Go; public Image Back, Finish; public AcHudText Pos, Name, Value; }
        private readonly Row[] _rows;
        private readonly int _towerRows;
        private Sprite _ownRowSprite;
        private int[] _racers = new int[64], _spectators = new int[64];
        // lights, flags, wrong way, pit, messages
        private readonly Image[] _lights = new Image[LightCount];
        private Image _flag, _wrong, _falseStart;
        private AcHudText _pitText;
        private readonly AcHudText[] _msg = new AcHudText[3];
        private readonly Image[] _msgBar = new Image[3];

        private int _lightReds; private bool _lightGreen, _lightsOn;
        private float _falseStartUntil = -1f;
        private readonly string[] _msgText = new string[3];
        private readonly float[] _msgUntil = new float[3];
        private int _style = -1;
        // damage displayer
        private Image[] _dmgImg = new Image[0];
        private float[] _dmgLast = new float[0];
        private readonly AcHudText[] _dmgVal = new AcHudText[6];
        private readonly float[] _dmgValLast = new float[6];
        private AcHudText _dmgRepair;
        private int _dmgRepairLast = -1;
        private float _dmgW, _dmgH;

        /// <summary>The smallest text size used (canvas units; lab: VR readability).</summary>
        public float MinTextSize { get; private set; } = 999f;

        public AcHudView(RectTransform root, bool vr)
        {
            Root = root;
            Vr = vr;
            _imgMat = vr ? OverlayImageMaterial() : null;
            _cfg = AcHudArt.Config ?? new AcHudConfig();
            _towerRows = vr ? VrTowerRows : TowerRows;
            _rows = new Row[_towerRows];
            Build();
        }

        private Color Cfg(float[] c) { return c != null && c.Length >= 4 ? GammaAlpha(new Color(c[0], c[1], c[2], c[3])) : White; }

        private static int _linear = -1;
        private static bool LinearSpace { get { if (_linear < 0) _linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0; return _linear == 1; } }

        /// <summary>
        /// AC blends its HUD in gamma space; this project (and the client) is LINEAR, where a translucent backdrop blends far lighter: AC's
        /// black 0.3 rows + the 20 % white_line veil came out light grey and the white labels on them unreadable (lab hud-desk 2026-11-01).
        /// The alpha is converted so the result matches AC's gamma blend: exactly for a dark backdrop (1 - a' = (1 - a)^2.2, any background),
        /// for a light one over a dark background (a' = a^2.2). Opaque colours are unchanged.
        /// </summary>
        public static Color GammaAlpha(Color c)
        {
            if (!LinearSpace || c.a >= 0.999f || c.a <= 0.001f) return c;
            float luma = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            c.a = luma < 0.5f ? 1f - Mathf.Pow(1f - c.a, 2.2f) : Mathf.Pow(c.a, 2.2f);
            return c;
        }

        /// <summary>The tint for a white picture whose own alpha is <paramref name="texAlpha"/> so that it blends like AC's (see <see cref="GammaAlpha"/>).</summary>
        public static Color VeilTint(float texAlpha)
        {
            if (!LinearSpace || texAlpha <= 0.001f) return White;
            return new Color(1f, 1f, 1f, Mathf.Pow(texAlpha, 2.2f) / texAlpha);
        }

        private float Sz(float s) { return Vr ? Mathf.Max(s, VrMinText) : s; }

        private RectTransform Group(string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = Root.gameObject.layer;
            go.transform.SetParent(Root, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private Image Img(Transform parent, string name, string art, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            if (_imgMat != null) img.material = _imgMat;
            Sprite s = art != null ? AcHudArt.Get(art) : null;
            img.sprite = s;
            if (s != null && s.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
            img.color = (art != null && s == null) ? Clear : color;     // a missing picture draws nothing (logged once by AcHudArt)
            return img;
        }

        private AcHudText Txt(Transform parent, string name, float x, float y, float w, float h, float size, TextAnchor a, Color c, int weight)
        {
            size = Sz(size);
            if (size < MinTextSize) MinTextSize = size;
            if (h < size) { y -= (size - h) * 0.5f; h = size; }       // the VR floor may outgrow a monitor row: keep it centred on the row
            return AcHudText.Create(parent, name, x, y, w, h, size, a, c, weight, _imgMat, Vr);
        }

        private static void Radial(Image img)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Bottom;
            img.fillClockwise = true;
            img.fillAmount = 0f;
        }

        /// <summary>A straight bar from the left: its rect width = <paramref name="full"/> x <paramref name="frac"/> (no sprite needed).</summary>
        private static void SetWidth(Image img, float full, float frac)
        {
            var rt = img.rectTransform;
            float w = full * Mathf.Clamp01(frac);
            Vector2 sd = rt.sizeDelta;
            if (Mathf.Abs(sd.x - w) > 0.05f) rt.sizeDelta = new Vector2(w, sd.y);
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            Color fore = Cfg(_cfg.FormFore);

            // --- Dial (AC rings)
            GDial = Group("Dial", 420f, 260f);
            float cx = 210f - DialSize * 0.5f, cy = 260f - DialSize - 8f;
            _rpmOff = Img(GDial, "Rpm off", AcHudArtList.RpmOff, cx, cy, DialSize, DialSize, White);
            _turboOff = Img(GDial, "Turbo off", AcHudArtList.TurboOff, cx, cy, DialSize, DialSize, White);
            _turboOn = Img(GDial, "Turbo on", AcHudArtList.TurboOn, cx, cy, DialSize, DialSize, White); Radial(_turboOn);
            _rpmOn = Img(GDial, "Rpm on", AcHudArtList.RpmOn, cx, cy, DialSize, DialSize, White); Radial(_rpmOn);
            float mid = cy + DialSize * 0.5f;
            _gear = Txt(GDial, "Gear", 110f, mid - 62f, 200f, 100f, 84f, TextAnchor.MiddleCenter, fore, 1);
            _speed = Txt(GDial, "Speed", 110f, mid + 26f, 200f, 40f, 30f, TextAnchor.MiddleCenter, fore, 0);
            _unit = Txt(GDial, "Unit", 110f, mid + (Vr ? 70f : 62f), 200f, 24f, 16f, TextAnchor.MiddleCenter, Dim, 2);
            _abs = Img(GDial, "ABS", AcHudArtList.Abs, 18f, mid + 34f, 46f, 46f, White);
            _tc = Img(GDial, "TC", AcHudArtList.Tc, 420f - 18f - 46f, mid + 34f, 46f, 46f, White);
            _fuelRuler = Img(GDial, "Fuel ruler", AcHudArtList.FuelBar, 420f - 18f - 81f, mid + 92f, 81f, 25f, White);
            _fuelW = 81f;
            _fuelFill = Img(GDial, "Fuel", null, 420f - 18f - 81f, mid + 86f, _fuelW, 5f, White);
            _fuelIcon = Img(GDial, "Fuel low", AcHudArtList.FuelLow, 18f, mid + 86f, 34f, 34f, White);

            // --- Essentials (alternative gauge)
            GEss = Group("Essentials", 300f, 132f);
            Img(GEss, "Back", null, 0f, 0f, 300f, 132f, Cfg(_cfg.FormBack));
            _eBarW = 264f;
            _eBarBack = Img(GEss, "Rpm back", null, 18f, 12f, _eBarW, 12f, new Color(1f, 1f, 1f, 0.12f));
            _eBar = Img(GEss, "Rpm", null, 18f, 12f, _eBarW, 12f, White);
            _eRuler = Img(GEss, "Rpm ruler", AcHudArtList.RpmBar, 18f, 24f, 264f, 46f, White);
            _eGear = Txt(GEss, "Gear", 14f, 66f, 80f, 64f, 60f, TextAnchor.MiddleCenter, fore, 1);
            _eSpeed = Txt(GEss, "Speed", 110f, 70f, 120f, 40f, 38f, TextAnchor.MiddleRight, fore, 1);
            _eUnit = Txt(GEss, "Unit", 234f, 82f, 60f, 24f, 16f, TextAnchor.MiddleLeft, Dim, 2);
            _eFuelRuler = Img(GEss, "Fuel ruler", AcHudArtList.FuelBar, 110f, 108f, 81f, 25f * 0.8f, White);
            _eFuelW = 81f;
            _eFuelFill = Img(GEss, "Fuel", null, 110f, 104f, _eFuelW, 4f, White);

            // --- Lap times (PlayerCurrentTime)
            float tw = Vr ? 320f : 265f;
            const float rh = 40f;
            float lw = Vr ? 150f : 110f, vx = Vr ? 120f : 100f;     // label width / value x // ours
            GTimes = Group("Times", tw, 30f + rh * 4f + 4f);
            _sessionBack = Img(GTimes, "Session back", null, 0f, 0f, tw, 30f, Cfg(_cfg.SessionBack));
            Color sFore = Cfg(_cfg.SessionFore);
            _sessionName = Txt(GTimes, "Session", 10f, 0f, 150f, 30f, 17f, TextAnchor.MiddleLeft, sFore, 1);
            _sessionRight = Txt(GTimes, "Session clock", 120f, 0f, tw - 130f, 30f, 17f, TextAnchor.MiddleRight, sFore, 0);
            float inf = Vr ? 1.5f : 1f;                                // infinity_icon.png (29x17) 1:1, 1.5x on the VR panel // ours
            _sessionInf = Img(GTimes, "Untimed", AcHudArtList.Infinity, tw - 10f - 29f * inf, 15f - 8.5f * inf, 29f * inf, 17f * inf, sFore);
            float y0 = 34f;
            for (int i = 0; i < 4; i++)
            {
                // session_info.ini's dark backdrop under AC's white_line veil keeps white digits readable over a bright sky // ours
                var back = Img(GTimes, "Row " + i, null, 0f, y0 + i * rh, tw, rh, Cfg(_cfg.SessionBack));
                Img(back.transform, "Line", AcHudArtList.WhiteLine, 0f, 0f, tw, rh, VeilTint(51f / 255f));     // white_line.png: white, alpha 51 everywhere (read from the install)
                if (i == 3) _posRow = back.gameObject;
                if (i > 0) Img(back.transform, "Separator", AcHudArtList.Separator, 0f, 0f, tw, 1f, White);
            }
            _lapLabel = Txt(GTimes, "Lap", 10f, y0, lw, rh, 15f, TextAnchor.MiddleLeft, Dim, 0);
            _lapTime = Txt(GTimes, "Lap time", vx, y0, tw - vx - 10f, rh, 27f, TextAnchor.MiddleRight, fore, 1);
            Img(GTimes, "Best icon", AcHudArtList.Best, 10f, y0 + rh + 4.5f, 26f, 31f, White);
            _bestTime = Txt(GTimes, "Best", 60f, y0 + rh, tw - 70f, rh, 22f, TextAnchor.MiddleRight, fore, 0);
            Img(GTimes, "Last icon", AcHudArtList.Last, 10f, y0 + rh * 2f + 4.5f, 27f, 31f, White);
            _lastTime = Txt(GTimes, "Last", 60f, y0 + rh * 2f, tw - 70f, rh, 22f, TextAnchor.MiddleRight, fore, 0);
            _posLabel = Txt(_posRow.transform, "Pos label", 10f, 0f, lw, rh, 15f, TextAnchor.MiddleLeft, Dim, 0);
            _posValue = Txt(_posRow.transform, "Pos", vx, 0f, tw - vx - 10f, rh, 24f, TextAnchor.MiddleRight, fore, 1);
            _lapLabel.Set(_cfg.S("LAP", "LAP"));
            _posLabel.Set(_cfg.S("POS.", "POS."));

            // --- Time difference + sector split
            GDelta = Group("Time difference", 320f, 54f);
            _deltaUp = Img(GDelta, "Faster", AcHudArtList.Up, 52f, 18f, 18f, 17f, White);
            _deltaDown = Img(GDelta, "Slower", AcHudArtList.Down, 52f, 18f, 18f, 17f, White);
            _delta = Txt(GDelta, "Delta", 40f, 0f, 240f, 54f, 40f, TextAnchor.MiddleCenter, Green, 1);
            GSplit = Group("Sector split", 360f, 36f);
            Img(GSplit, "Back", null, 0f, 0f, 360f, 36f, Cfg(_cfg.SessionBack));
            _splitTime = Txt(GSplit, "Split", 10f, 0f, 200f, 36f, 22f, TextAnchor.MiddleLeft, fore, 1);
            _splitDelta = Txt(GSplit, "Split delta", 200f, 0f, 150f, 36f, 22f, TextAnchor.MiddleRight, Green, 1);

            // --- Standings tower
            float rw = Vr ? 400f : 320f, rr = Vr ? 34f : 26f;
            GTower = Group("Standings", rw, rr * _towerRows + 6f);
            _ownRowSprite = AcHudArt.Get(AcHudArtList.CurrentLapBg);
            float nameW = Vr ? rw - 50f - 150f : 170f, valX = Vr ? rw - 150f : 210f, valW = Vr ? 144f : 104f;
            for (int i = 0; i < _towerRows; i++)
            {
                var row = new Row();
                var go = new GameObject("Row " + i, typeof(RectTransform));
                go.layer = GTower.gameObject.layer;
                go.transform.SetParent(GTower, false);
                float ry = i * rr + (i == _towerRows - 1 ? 6f : 0f);   // the "me" row sits a little apart
                Place((RectTransform)go.transform, 0f, ry, rw, rr - 2f);
                row.Go = go;
                row.Back = Img(go.transform, "Back", null, 0f, 0f, rw, rr - 2f, Cfg(_cfg.FormBack));
                row.Pos = Txt(go.transform, "Pos", 6f, 0f, Vr ? 36f : 30f, rr - 2f, 16f, TextAnchor.MiddleRight, fore, 1);
                row.Name = Txt(go.transform, "Name", Vr ? 50f : 46f, 0f, nameW, rr - 2f, 16f, TextAnchor.MiddleLeft, fore, 0);
                row.Value = Txt(go.transform, "Value", valX, 0f, valW, rr - 2f, 16f, TextAnchor.MiddleRight, fore, 0);
                float fs = Vr ? 24f : 18f;
                row.Finish = Img(go.transform, "Finished", AcHudArtList.MiniFinish, valX - fs - 4f, (rr - 2f - fs) * 0.5f, fs, fs, White);
                go.SetActive(false);
                _rows[i] = row;
            }

            // --- Start lights (+ AC's drag false-start bar under them)
            GLights = Group("Start lights", LightCount * 82f - 8f, 74f);
            for (int i = 0; i < LightCount; i++) _lights[i] = Img(GLights, "Light " + i, AcHudArtList.LightOff, i * 82f, 0f, 74f, 74f, White);
            float fw = LightCount * 82f - 8f;
            _falseStart = Img(GLights, "False start", AcHudArtList.FalseStart, 0f, 80f, fw, fw * 33f / 708f, White);   // 708x33 kept in proportion

            // --- Flag, wrong way, pit, messages
            GFlag = Group("Flag", 150f, 80f);
            _flag = Img(GFlag, "Flag", AcHudArtList.FlagFinish, 0f, 0f, 150f, 80f, White);
            GWrong = Group("Wrong way", 128f, 128f);
            _wrong = Img(GWrong, "Wrong way", AcHudArtList.WrongWay, 0f, 0f, 128f, 128f, White);
            GPit = Group("Pit limiter", 340f, 38f);
            Color pitBack = Cfg(_cfg.TitleBack); pitBack.a *= 0.85f;          // skin.ini TITLE_BACK, a touch see-through // ours
            Img(GPit, "Back", null, 0f, 0f, 340f, 38f, pitBack);
            _pitText = Txt(GPit, "Text", 0f, 0f, 340f, 38f, 20f, TextAnchor.MiddleCenter, fore, 1);
            float mh = Vr ? 40f : 44f;
            GMsg = Group("Messages", 708f, mh * 3f);
            for (int i = 0; i < 3; i++)
            {
                _msg[i] = Txt(GMsg, "Message " + i, 0f, i * mh, 708f, mh - 4f, 24f, TextAnchor.MiddleCenter, fore, 1);
                _msgBar[i] = Img(GMsg, "Bar " + i, AcHudArtList.MessageBar, 0f, i * mh + mh - 4f, 708f, 1f, White);
            }

            BuildDamage(fore);

            SetAllInactive();
        }

        private void SetAllInactive()
        {
            Act(GTimes, false); Act(GTower, false); Act(GLights, false); Act(GDelta, false); Act(GFlag, false); Act(GSplit, false);
            Act(GWrong, false); Act(GMsg, false); Act(GPit, false); Act(GDial, false); Act(GEss, false);
            Act(GDamage, false);
        }

        private static void Act(Component c, bool on) { if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

        // ---------------------------------------------------------------- inputs from AcHud

        public void SetLights(int reds, bool green, bool visible) { _lightReds = reds; _lightGreen = green; _lightsOn = visible; }

        public void PushMessage(string text, float until)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (text == FalseStartText) _falseStartUntil = until;
            // newest at the bottom; a repeat of a showing line only extends it
            for (int i = 0; i < 3; i++) if (_msgText[i] == text && _msgUntil[i] > 0f) { _msgUntil[i] = until; return; }
            int slot = -1;
            for (int i = 0; i < 3; i++) if (_msgText[i] == null) { slot = i; break; }
            if (slot < 0)
            {
                for (int i = 0; i < 2; i++) { _msgText[i] = _msgText[i + 1]; _msgUntil[i] = _msgUntil[i + 1]; }
                slot = 2;
            }
            _msgText[slot] = text; _msgUntil[slot] = until;
        }

        /// <summary>A message is showing (or waiting to show) with this text (lab / tests).</summary>
        public bool HasMessage(string text)
        {
            for (int i = 0; i < 3; i++) if (_msgText[i] == text) return true;
            return false;
        }

        // ---------------------------------------------------------------- lab read-outs (allocate; never per frame in the world)

        public string LabSession { get { return _sessionName.Text; } }
        public string LabSessionRight { get { return _sessionRight.Text; } }
        public string LabLapLabel { get { return _lapLabel.Text; } }
        public string LabLapTime { get { return _lapTime.Text; } }
        public string LabPos { get { return _posRow.activeSelf ? _posValue.Text : ""; } }
        public string LabSplit { get { return GSplit.gameObject.activeSelf ? _splitTime.Text + " " + _splitDelta.Text : ""; } }
        public bool LabUntimedIcon { get { return _sessionInf.gameObject.activeSelf; } }
        public bool LabFalseStartBar { get { return _falseStart.gameObject.activeInHierarchy; } }
        public bool LabLampsShown { get { return _lights[0].gameObject.activeInHierarchy; } }
        public Sprite LabFlag { get { return GFlag.gameObject.activeSelf ? _flag.sprite : null; } }
        public Sprite LabFuelIcon { get { return _fuelIcon.gameObject.activeInHierarchy ? _fuelIcon.sprite : null; } }
        /// <summary>Drawn width / full width of the straight bars: Essentials rpm, Essentials fuel, dial fuel.</summary>
        public float LabEssRpmFrac { get { return _eBar.rectTransform.rect.width / _eBarW; } }
        public float LabEssFuelFrac { get { return _eFuelFill.rectTransform.rect.width / _eFuelW; } }
        public float LabDialFuelFrac { get { return _fuelFill.rectTransform.rect.width / _fuelW; } }
        public float LabRpmFill { get { return _rpmOn.fillAmount; } }
        public RectTransform LabRpmRing { get { return _rpmOn.rectTransform; } }
        public int LabTowerRows { get { return _towerRows; } }
        /// <summary>Tower row <paramref name="r"/> as "pos|name|value" ("" when hidden).</summary>
        public string LabTowerRow(int r)
        {
            if (r < 0 || r >= _towerRows || !_rows[r].Go.activeSelf) return "";
            return _rows[r].Pos.Text + "|" + _rows[r].Name.Text + "|" + _rows[r].Value.Text;
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Draws one frame. <paramref name="style"/> 0 = AC dial, 1 = Essentials; the flags say which groups the player wants.</summary>
        public void Tick(ref AcHudFeed f, IReadOnlyList<AcStanding> standings, float now, bool showDriving, bool showTower, bool showDelta, int style)
        {
            bool onTrack = f.Phase >= AcPhase.Practice;
            bool driving = showDriving && f.Car;
            _style = style;

            // gauges
            Act(GDial, driving && style == 0);
            Act(GEss, driving && style == 1);
            if (driving) { if (style == 0) TickDial(ref f, now); else TickEss(ref f, now); }

            // times
            // flow pass 2026-11-02: on the VR panel the damage displayer takes the times box's place while it shows (AcHudVr.PlaceDamage)
            bool times = driving && f.Driver && onTrack && !(Vr && f.Damage);
            Act(GTimes, times);
            if (times) TickTimes(ref f);

            // delta
            bool delta = times && showDelta && f.BestMs > 0 && f.LapMs > 0 && !f.OutLap && !f.Finished && f.Phase != AcPhase.Results;
            Act(GDelta, delta);
            if (delta)
            {
                int n = AcHudFormat.Delta(_b, 0, f.DeltaMs, 2);
                _delta.Set(_b, n);
                bool faster = f.DeltaMs < 0;
                _delta.Color = faster ? Green : Red;
                Act(_deltaUp, faster); Act(_deltaDown, !faster);
            }

            // sector split (gameplay.ini [GUI] SHOW_SECTORS)
            bool split = times && _cfg.ShowSectors && f.SplitSector > 0 && f.SplitMs > 0;
            Act(GSplit, split);
            if (split)
            {
                int n = AcHudFormat.Char(_b, 0, 'S');
                n = AcHudFormat.Int(_b, n, f.SplitSector);
                n = AcHudFormat.Str(_b, n, "  ");
                n = AcHudFormat.LapTime(_b, n, f.SplitMs);
                _splitTime.Set(_b, n);
                bool hasBest = f.SplitDeltaMs != int.MinValue;
                n = hasBest ? AcHudFormat.Delta(_b, 0, f.SplitDeltaMs, 3) : 0;
                _splitDelta.Set(_b, n);
                if (hasBest) _splitDelta.Color = f.SplitDeltaMs < 0 ? Green : Red;
            }

            // tower
            int count = standings != null ? standings.Count : 0;
            bool tower = showTower && onTrack && count > 1 && (f.Car || f.Phase >= AcPhase.Grid);
            Act(GTower, tower);
            if (tower) TickTower(ref f, standings, count);

            // lights (+ the false-start bar while R's "FALSE START!" shows)
            bool falseStart = now < _falseStartUntil;
            Act(GLights, _lightsOn || falseStart);
            if (_lightsOn || falseStart)
            {
                for (int i = 0; i < LightCount; i++)
                {
                    string art = _lightGreen ? AcHudArtList.LightGreen : (i < _lightReds ? AcHudArtList.LightRed : AcHudArtList.LightOff);
                    Sprite s = AcHudArt.Get(art);
                    if (_lights[i].sprite != s) _lights[i].sprite = s;
                    Act(_lights[i], _lightsOn);
                }
                Act(_falseStart, falseStart);
            }

            // flag (priority: finish > penalty > blue > yellow); chequered for everyone from the moment the leader finishes (AC)
            string flagArt = null;
            bool raceRun = f.Phase >= AcPhase.Racing && f.Phase <= AcPhase.Results;
            if (driving && (f.Finished || (raceRun && f.LeaderFinished))) flagArt = AcHudArtList.FlagFinish;
            else if (driving && f.PenaltyFlag) flagArt = AcHudArtList.FlagPenalty;
            else if (driving && f.BlueFlag) flagArt = AcHudArtList.FlagBlue;
            else if (driving && f.YellowFlag) flagArt = AcHudArtList.FlagYellow;
            Act(GFlag, flagArt != null);
            if (flagArt != null) { Sprite s = AcHudArt.Get(flagArt); if (_flag.sprite != s) _flag.sprite = s; }

            // wrong way: pulsing // ours 1.25 Hz
            bool wrong = driving && f.Driver && f.WrongWay;
            Act(GWrong, wrong);
            if (wrong) _wrong.color = new Color(1f, 1f, 1f, 0.65f + 0.35f * Mathf.Sin(now * 7.85f));

            // pit limiter
            bool pit = driving && f.InPitlane;
            Act(GPit, pit);
            if (pit)
            {
                int n = AcHudFormat.Str(_b, 0, "PIT LIMITER  ");       // ours text (acs.exe has "LIMITER"; no locale key)
                float lim = f.Mph ? f.PitLimitKmh * 0.621371f : f.PitLimitKmh;
                n = AcHudFormat.Int(_b, n, Mathf.RoundToInt(lim));
                n = AcHudFormat.Char(_b, n, ' ');
                n = AcHudFormat.Str(_b, n, f.Mph ? "mph" : "Km/h");
                _pitText.Set(_b, n);
                bool over = f.SpeedKmh > f.PitLimitKmh + 1f;
                _pitText.Color = over && ((int)(now * 6f) & 1) == 0 ? new Color(1f, 1f, 1f, 0.35f) : White;
            }

            // damage displayer (DAMAGE_DESIGN 5: TIME s after a hit on my car, always while a repair runs; AcHud decides f.Damage)
            bool dmg = driving && f.Damage;
            Act(GDamage, dmg);
            if (dmg) TickDamage(ref f);

            // messages
            bool any = false;
            for (int i = 0; i < 3; i++)
            {
                if (_msgText[i] != null && now > _msgUntil[i]) { _msgText[i] = null; }
            }
            // compact: keep order, fill from the top
            int w = 0;
            for (int i = 0; i < 3; i++) if (_msgText[i] != null) { if (w != i) { _msgText[w] = _msgText[i]; _msgUntil[w] = _msgUntil[i]; _msgText[i] = null; } w++; }
            for (int i = 0; i < 3; i++)
            {
                bool on = _msgText[i] != null;
                _msg[i].Show(on); Act(_msgBar[i], on);
                if (on)
                {
                    any = true;
                    _msg[i].Set(_msgText[i]);
                    float left = _msgUntil[i] - now;
                    float a = left < 0.4f ? Mathf.Clamp01(left / 0.4f) : 1f;    // ours: 0.4 s fade
                    _msg[i].Color = new Color(1f, 1f, 1f, a);
                }
            }
            Act(GMsg, any);
        }

        // ---------------------------------------------------------------- damage displayer (iteration 2, DAMAGE_DESIGN 5)

        /// <summary>Damage tint (AC's colours are not in any file): white at 0, yellow at 0.5, red at 1, alpha 0.85. // ours</summary>
        public static Color DamageTint(float v)
        {
            v = Mathf.Clamp01(v);
            Color c = v < 0.5f ? Color.Lerp(White, new Color(1f, 1f, 0f, 1f), v * 2f) : Color.Lerp(new Color(1f, 1f, 0f, 1f), new Color(1f, 0f, 0f, 1f), (v - 0.5f) * 2f);
            c.a = DamageAlpha;
            return c;
        }
        public const float DamageAlpha = 0.85f;  // ours
        /// <summary>Height of the stacked car (reference px on the monitor, units on the VR panel). // ours</summary>
        public const float DamageCarH = 260f, VrDamageCarH = 220f;   // VR: the cell (car + countdown line = 262) fits the times column under the messages (flow pass)

        /// <summary>Labels of the printed values (PRINT_VALUES=1; AC's own labels are exe strings - these are ours).</summary>
        private static readonly string[] DamageLabels = { "FRONT", "REAR", "LEFT", "RIGHT", "ENGINE", "SUSP." };

        private void BuildDamage(Color fore)
        {
            _dmgH = Vr ? VrDamageCarH : DamageCarH;
            _dmgW = _dmgH * AcHudArtList.CropW / AcHudArtList.CropH;
            float valW = Vr ? 230f : 170f, line = Vr ? 36f : 26f, valSize = Vr ? VrMinText : 18f;
            GDamage = Group("Damage", _dmgW + 12f + valW, _dmgH + line + 6f);
            string[] files = AcHudArtList.DamageFiles;
            _dmgImg = new Image[files.Length];
            _dmgLast = new float[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                _dmgImg[i] = Img(GDamage, "Layer " + System.IO.Path.GetFileNameWithoutExtension(files[i]), files[i], 0f, 0f, _dmgW, _dmgH, DamageTint(0f));
                _dmgLast[i] = -1f;
            }
            float top = (_dmgH - line * DamageLabels.Length) * 0.5f;
            for (int i = 0; i < _dmgVal.Length; i++)
            {
                _dmgVal[i] = Txt(GDamage, "Value " + i, _dmgW + 12f, top + i * line, valW, line, valSize, TextAnchor.MiddleLeft, fore, 1);
                _dmgValLast[i] = -1f;
            }
            _dmgRepair = Txt(GDamage, "Repair", 0f, _dmgH + 4f, _dmgW + 12f + valW, line, Vr ? VrMinText : 20f, TextAnchor.MiddleLeft, fore, 1);
        }

        private float DamageValue(int layer, ref AcHudFeed f)
        {
            string a = AcHudArtList.DamageFiles[layer];
            if (a == AcHudArtList.DmgFront) return f.DmgFront;
            if (a == AcHudArtList.DmgRear) return f.DmgRear;
            if (a == AcHudArtList.DmgLeft) return f.DmgLeft;
            if (a == AcHudArtList.DmgRight) return f.DmgRight;
            if (a == AcHudArtList.DmgEngine) return f.DmgEngine;
            if (a == AcHudArtList.DmgSusFl) return f.DmgSusFl;
            if (a == AcHudArtList.DmgSusFr) return f.DmgSusFr;
            if (a == AcHudArtList.DmgSusRl) return f.DmgSusRl;
            if (a == AcHudArtList.DmgSusRr) return f.DmgSusRr;
            return 0f;      // gearbox, tyres: not simulated (drawn clean)
        }

        private void TickDamage(ref AcHudFeed f)
        {
            for (int i = 0; i < _dmgImg.Length; i++)
            {
                float v = Mathf.Clamp01(DamageValue(i, ref f));
                if (Mathf.Abs(v - _dmgLast[i]) < 0.002f) continue;
                _dmgLast[i] = v;
                if (_dmgImg[i].sprite != null) _dmgImg[i].color = DamageTint(v);
            }
            for (int i = 0; i < _dmgVal.Length; i++)
            {
                _dmgVal[i].Show(f.DmgPrint);
                if (!f.DmgPrint) continue;
                float v = i == 0 ? f.DmgFront : i == 1 ? f.DmgRear : i == 2 ? f.DmgLeft : i == 3 ? f.DmgRight : i == 4 ? f.DmgEngine
                        : Mathf.Max(Mathf.Max(f.DmgSusFl, f.DmgSusFr), Mathf.Max(f.DmgSusRl, f.DmgSusRr));
                v = Mathf.Clamp01(v);
                if (Mathf.Abs(v - _dmgValLast[i]) < 0.002f) continue;
                _dmgValLast[i] = v;
                int n = AcHudFormat.Str(_b, 0, DamageLabels[i]);
                n = AcHudFormat.Char(_b, n, ' ');
                n = AcHudFormat.Int(_b, n, Mathf.RoundToInt(v * 100f));
                n = AcHudFormat.Str(_b, n, " %");
                _dmgVal[i].Set(_b, n);
                Color c = DamageTint(v); c.a = 1f; _dmgVal[i].Color = c;
            }
            bool rep = f.RepairTotal > 0f;
            _dmgRepair.Show(rep);
            if (rep)
            {
                int s = Mathf.CeilToInt(Mathf.Max(0f, f.RepairLeft));
                if (s != _dmgRepairLast)
                {
                    _dmgRepairLast = s;
                    int n = AcHudFormat.Str(_b, 0, "REPAIRING  ");      // ours text (R's message wording)
                    n = AcHudFormat.Int(_b, n, s);
                    n = AcHudFormat.Str(_b, n, " S");
                    _dmgRepair.Set(_b, n);
                }
            }
            else _dmgRepairLast = -1;
        }

        /// <summary>Lab read-out: the tint of damage layer <paramref name="rel"/> (an AcHudArtList.Dmg* path); clear when not drawn.</summary>
        public Color LabDamageColor(string rel)
        {
            string[] files = AcHudArtList.DamageFiles;
            for (int i = 0; i < files.Length && i < _dmgImg.Length; i++) if (files[i] == rel) return _dmgImg[i].gameObject.activeInHierarchy ? _dmgImg[i].color : Clear;
            return Clear;
        }
        public bool LabDamageShown { get { return GDamage != null && GDamage.gameObject.activeSelf; } }
        public string LabDamageRepair { get { return _dmgRepair != null && GDamage.gameObject.activeSelf ? _dmgRepair.Text : ""; } }
        public RectTransform LabDamageRect { get { return GDamage; } }

        private float RpmFrac(ref AcHudFeed f)
        {
            float lim = f.LimiterRpm > 100f ? f.LimiterRpm : 8000f;   // ours fallback when the car has no LIMITER
            return Mathf.Clamp01(f.Rpm / lim);
        }

        private bool LimiterFlash(ref AcHudFeed f, float now)
        {
            float red = f.RedLineRpm > 100f && (f.LimiterRpm <= 100f || f.RedLineRpm < f.LimiterRpm) ? f.RedLineRpm : f.LimiterRpm; // fixer 2026-11-02: design 3.3 red line (0 = the limiter)
            bool at = f.Limiter || (red > 100f && f.Rpm >= red * 0.995f);
            return at && ((int)(now * 16f) & 1) == 1;     // ours: 8 Hz flash
        }

        private int SpeedValue(ref AcHudFeed f) { return Mathf.RoundToInt(Mathf.Abs(f.Mph ? f.SpeedKmh * 0.621371f : f.SpeedKmh)); }

        /// <summary>rpm_on / turbo_on fill amount for a 0..1 value: the ring's ink spans 30..328 degrees clockwise from straight down.</summary>
        public static float RingFill(float frac) { return (ArcStartDeg + Mathf.Clamp01(frac) * (ArcEndDeg - ArcStartDeg)) / 360f; }

        private void TickDial(ref AcHudFeed f, float now)
        {
            float frac = RpmFrac(ref f);
            _rpmOn.fillAmount = RingFill(frac);
            _rpmOn.color = LimiterFlash(ref f, now) ? new Color(1f, 1f, 1f, 0.3f) : White;
            bool turbo = f.MaxBoost > 0.01f;
            Act(_turboOff, turbo); Act(_turboOn, turbo);
            if (turbo) _turboOn.fillAmount = RingFill(f.Boost / f.MaxBoost);
            int n = AcHudFormat.Gear(_b, 0, f.Gear); _gear.Set(_b, n);
            n = AcHudFormat.Int(_b, 0, SpeedValue(ref f)); _speed.Set(_b, n);
            _unit.Set(f.Mph ? "mph" : "Km/h");                  // AC's own unit strings (acs.exe)
            Act(_abs, f.Abs); Act(_tc, f.Tc);
            bool fuel = f.MaxFuel > 0.1f;
            Act(_fuelRuler, fuel); Act(_fuelFill, fuel);
            float ff = fuel ? Mathf.Clamp01(f.Fuel / f.MaxFuel) : 0f;
            if (fuel) { SetWidth(_fuelFill, _fuelW, ff); _fuelFill.color = ff < 0.099f ? Red : White; }    // fuelBar red zone 0..0.099 (VERIFIED)
            bool empty = fuel && f.Fuel <= 0.05f;               // ours: 50 ml = dry
            Act(_fuelIcon, fuel && ff < 0.05f);                 // ours: warning under 5 %
            if (fuel && ff < 0.05f)
            {
                Sprite want = AcHudArt.Get(empty ? AcHudArtList.FuelOut : AcHudArtList.FuelLow);
                if (_fuelIcon.sprite != want) { _fuelIcon.sprite = want; _fuelIcon.color = want != null ? White : Clear; }
            }
        }

        private void TickEss(ref AcHudFeed f, float now)
        {
            float frac = RpmFrac(ref f);
            SetWidth(_eBar, _eBarW, frac);
            bool red = frac >= 0.917f;                          // rpmBar.png red zone starts at 0.917 of its width (VERIFIED)
            _eBar.color = LimiterFlash(ref f, now) ? new Color(1f, 0.27f, 0.22f, 0.3f) : (red ? Red : White);
            int n = AcHudFormat.Gear(_b, 0, f.Gear); _eGear.Set(_b, n);
            n = AcHudFormat.Int(_b, 0, SpeedValue(ref f)); _eSpeed.Set(_b, n);
            _eUnit.Set(f.Mph ? "mph" : "Km/h");
            bool fuel = f.MaxFuel > 0.1f;
            Act(_eFuelRuler, fuel); Act(_eFuelFill, fuel);
            if (fuel) { float ff = Mathf.Clamp01(f.Fuel / f.MaxFuel); SetWidth(_eFuelFill, _eFuelW, ff); _eFuelFill.color = ff < 0.099f ? Red : White; }
        }

        /// <summary>The session the box names: the feed's <see cref="AcHudFeed.ShownSession"/> (AcHud works it out from R's state), or for an
        /// older / fake feed without it: from the grid on the session the master picked, before it practice.</summary>
        public static AcSessionType SessionOf(ref AcHudFeed f)
        {
            if (f.ShownSession != 0) return f.ShownSession;
            return f.Phase >= AcPhase.Grid ? f.Session : AcSessionType.Practice;
        }

        private void TickTimes(ref AcHudFeed f)
        {
            // session box (session_info.ini PRATICE_VISIBLE / QUALIFY_VISIBLE / RACE_VISIBLE)
            int n = 0;
            AcSessionType st = SessionOf(ref f);
            bool race = st == AcSessionType.Race;
            bool lapRace = race && f.TotalLaps > 0;
            bool box = _cfg.SessionVisible((int)st);
            Act(_sessionBack, box); _sessionName.Show(box); _sessionRight.Show(box);
            bool untimed = box && !race && f.SecondsLeft < 0;            // untimed practice / hotlap / qualify not started: infinity_icon
            Act(_sessionInf, untimed);
            if (box)
            {
                string name = race ? _cfg.S("RACE", "RACE") : st == AcSessionType.Qualify ? _cfg.S("QUALIFY", "QUALIFY")
                    : st == AcSessionType.Hotlap ? _cfg.S("HOTLAP", "HOTLAP") : _cfg.S("PRACTICE", "PRACTICE");
                _sessionName.Set(name);
                if (lapRace)
                {
                    if (f.Finished) n = AcHudFormat.Str(_b, 0, _cfg.S("FINISH", "FINISH"));
                    else
                    {
                        n = AcHudFormat.Str(_b, 0, _cfg.S("LAP", "LAP"));
                        n = AcHudFormat.Char(_b, n, ' ');
                        int cur = f.Phase >= AcPhase.Racing ? Mathf.Min(f.Laps + 1, f.TotalLaps) : 0;
                        n = AcHudFormat.OfTotal(_b, n, cur, f.TotalLaps);
                    }
                }
                else if (f.SecondsLeft >= 0) n = AcHudFormat.Clock(_b, 0, f.SecondsLeft);
                _sessionRight.Set(_b, n);
            }

            // current lap: FINISH + race time / OUT LAP / LAP n [Sx]
            if (race && f.Finished)
            {
                n = AcHudFormat.Str(_b, 0, _cfg.S("FINISH", "FINISH"));
                _lapLabel.Set(_b, n);
                n = AcHudFormat.LapTime(_b, 0, f.FinishedMs);
                _lapTime.Set(_b, n);
                _lapTime.Color = White;
            }
            else
            {
                if (f.OutLap && !race) n = AcHudFormat.Str(_b, 0, "OUT LAP");     // ours text (no locale key / acs.exe string)
                else if (f.OpenCourse) n = AcHudFormat.Str(_b, 0, _cfg.S("LAPTIME", "LAPTIME"));   // one run, no lap number (R)
                else
                {
                    n = AcHudFormat.Str(_b, 0, _cfg.S("LAP", "LAP"));
                    n = AcHudFormat.Char(_b, n, ' ');
                    n = AcHudFormat.Int(_b, n, lapRace ? Mathf.Min(f.Laps + 1, f.TotalLaps) : f.Laps + 1);
                    if (_cfg.ShowSectors && f.LapMs > 0 && !Vr)
                    {
                        n = AcHudFormat.Str(_b, n, "  S");
                        n = AcHudFormat.Int(_b, n, f.Sector + 1);
                    }
                }
                _lapLabel.Set(_b, n);
                n = AcHudFormat.LapTime(_b, 0, f.LapMs);
                _lapTime.Set(_b, n);
                _lapTime.Color = f.LapValid ? White : Red;
            }
            n = AcHudFormat.LapTime(_b, 0, f.BestMs); _bestTime.Set(_b, n);
            n = AcHudFormat.LapTime(_b, 0, f.LastMs); _lastTime.Set(_b, n);

            bool pos = f.Position > 0 && f.Cars > 1;
            if (_posRow.activeSelf != pos) _posRow.SetActive(pos);
            if (pos) { n = AcHudFormat.OfTotal(_b, 0, f.Position, f.Cars); _posValue.Set(_b, n); }
        }

        private void TickTower(ref AcHudFeed f, IReadOnlyList<AcStanding> st, int count)
        {
            // racers in standings order, then spectators (late joiners: R's AcStanding.Spectating, Position 0) greyed without a position
            if (_racers.Length < count) { _racers = new int[count + 16]; _spectators = new int[count + 16]; }
            int nr = 0, ns = 0, meRank = -1, meIdx = -1;
            for (int i = 0; i < count; i++)
            {
                var s = st[i];
                if (s == null) continue;
                if (s.IsLocal) meIdx = i;
                if (s.Spectating) { _spectators[ns++] = i; continue; }
                if (s.IsLocal) meRank = nr;
                _racers[nr++] = i;
            }
            bool race = f.Phase >= AcPhase.Grid && f.Phase <= AcPhase.Results && f.Session == AcSessionType.Race;
            int top = _towerRows - 1;
            bool meBelow = meIdx >= 0 && (meRank >= top || (meRank < 0 && nr + ns > top));
            int k = 0;      // next list entry (racers, then spectators)
            for (int r = 0; r < _towerRows; r++)
            {
                int idx = -1, rank = -1;
                if (r < top)
                {
                    while (k < nr + ns)
                    {
                        int cand = k < nr ? _racers[k] : _spectators[k - nr];
                        int candRank = k < nr ? k : -1;
                        k++;
                        if (meBelow && cand == meIdx) continue;    // shown in the last row instead
                        idx = cand; rank = candRank; break;
                    }
                }
                else if (meBelow) { idx = meIdx; rank = meRank; }
                else if (k < nr + ns) { idx = k < nr ? _racers[k] : _spectators[k - nr]; rank = k < nr ? k : -1; k++; }
                Row row = _rows[r];
                AcStanding s = idx >= 0 ? st[idx] : null;
                if (row.Go.activeSelf != (s != null)) row.Go.SetActive(s != null);
                if (s == null) continue;
                bool spec = s.Spectating;
                int n;
                if (spec) n = AcHudFormat.Char(_b, 0, '-');
                else n = AcHudFormat.Int(_b, 0, s.Position > 0 ? s.Position : rank + 1);
                row.Pos.Set(_b, n);
                row.Name.Set(s.Name);
                if (spec) n = 0;
                else if (s.Retired && !s.Finished) n = AcHudFormat.Str(_b, 0, "DNF");      // iteration 2: engine blown (R)
                else if (race && rank > 0) n = AcHudFormat.Gap(_b, 0, s.GapMs, s.LapsBehind);
                else n = AcHudFormat.LapTime(_b, 0, s.BestMs);
                row.Value.Set(_b, n);
                bool own = s.IsLocal;
                Sprite want = own ? _ownRowSprite : null;
                if (row.Back.sprite != want)
                {
                    row.Back.sprite = want;
                    row.Back.type = want != null ? Image.Type.Sliced : Image.Type.Simple;
                    row.Back.color = want != null ? White : Cfg(_cfg.FormBack);
                }
                Color tc = spec || s.InPit || s.Retired ? Dim : White;
                row.Name.Color = tc; row.Value.Color = tc; row.Pos.Color = spec ? Dim : White;
                Act(row.Finish, s.Finished && !spec);
            }
        }
    }
}
