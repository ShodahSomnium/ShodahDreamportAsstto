using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] World-space UI in Assetto Corsa's look: the game's ksGui colours from content/gui/skin.ini (FORM BACK black at half alpha, TITLE_BACK
    /// red, CONTROL BACK grey at half alpha, TEXT_INPUT FOCUS_BACK dark red) and the launcher's accent (#bd0000, read from its css), white text in
    /// Segoe UI from content/fonts (AcFonts; TextMeshPro's default until built). Layouts are in canvas units from the top-left, y down (Halo's HaloUi /
    /// BO2's Bo2Ui). Every widget is created on the UI layer 5 (the VR ray tests each graphic's layer: TrackedDeviceGraphicRaycaster.ShouldTestGraphic).
    /// Canvases take the client's mouse (GraphicRaycaster) and its VR ray (TrackedDeviceGraphicRaycaster), the recipe every Dreamport uses.
    /// </summary>
    public static class AcUi
    {
        public const int UiLayer = 5;

        // The colours start as the shipped files' values (skin.ini, launcher css) and are replaced by what the player's install says (ApplySkin).
        public static Color FormBack = new Color(0f, 0f, 0f, 128f / 255f);
        public static Color FormBorder = new Color(64f / 255f, 64f / 255f, 64f / 255f, 128f / 255f);
        public static Color TitleBack = new Color(1f, 0f, 0f, 1f);
        public static Color ControlBack = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        public static Color ControlBorder = new Color(155f / 255f, 155f / 255f, 155f / 255f, 1f);
        public static Color InputFocus = new Color(128f / 255f, 8f / 255f, 8f / 255f, 1f);
        public static Color Accent = new Color(0xbd / 255f, 0f, 0f, 1f);
        public static Color Text = Color.white;
        /// <summary>Secondary text (ours: a light grey on the dark panels).</summary>
        public static readonly Color Grey = new Color(0.68f, 0.68f, 0.70f, 1f);
        /// <summary>Greyed / unavailable (ours).</summary>
        public static readonly Color Dim = new Color(0.40f, 0.40f, 0.42f, 1f);
        /// <summary>The page background (ours: near black, the launcher's body is black). Fully opaque: at 0.97 the HDR-bright showroom (floor > 1.0)
        /// bled through the world-space panel as a ghost of the car (front-hub lab, 2026-11-01).</summary>
        public static readonly Color Page = new Color(0.035f, 0.035f, 0.04f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.35f, 1f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.30f, 1f);

        public static bool SkinApplied { get; private set; }

        public static Color Of(AcRgba c) { return new Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f); }

        /// <summary>The install's own colours (worker read, applied on the main thread before the pages are built).</summary>
        public static void ApplySkin(AcGuiSkin s)
        {
            if (s == null) return;
            FormBack = Of(s.FormBack);
            FormBorder = Of(s.FormBorder);
            TitleBack = Of(s.TitleBack);
            ControlBack = Of(s.ControlBack);
            ControlBorder = Of(s.ControlBorder);
            InputFocus = Of(s.InputFocus);
            Accent = Of(s.Accent);
            Text = Of(s.FormFore);
            SkinApplied = true;
        }

        /// <summary>A world-space canvas the client's pointer can use in both modes (desktop mouse + VR ray).</summary>
        public static Canvas MakeCanvas(string name, Transform parent, float width, float height, float metresPerUnit)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = AcPlayer.Head();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * metresPerUnit;
            var catcher = Place(go.transform, "Pointer catcher", 0f, 0f, width, height).gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            return canvas;
        }

        /// <summary>A child rect at (x, y) from the parent's top-left, w x h canvas units.</summary>
        public static RectTransform Place(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color, bool raycast = false)
        {
            Image img = Place(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>A thin frame (four boxes) round a rect - the skin's border colours.</summary>
        public static void Frame(Transform parent, string name, float x, float y, float w, float h, float t, Color c)
        {
            Box(parent, name + " top", x, y, w, t, c);
            Box(parent, name + " bottom", x, y + h - t, w, t, c);
            Box(parent, name + " left", x, y, t, h, c);
            Box(parent, name + " right", x + w - t, y, t, h, c);
        }

        /// <summary>A picture read from the player's install (PNG / JPG), decoded asynchronously (AcUiArt) at most <paramref name="maxW"/> wide;
        /// invisible until it is there. <paramref name="fit"/> keeps the aspect inside the rect.</summary>
        public static RawImage Picture(Transform parent, string name, float x, float y, float w, float h, string path, int maxW, Color color, bool fit = true)
        {
            RawImage img = Place(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            img.color = new Color(color.r, color.g, color.b, 0f);
            img.raycastTarget = false;
            AcUiArt.Bind(img, path, maxW, color, fit);
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h, AcFonts.Face face = AcFonts.Face.Regular)
        {
            var t = Place(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            AcFonts.Apply(t, face);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = text ?? "";
            return t;
        }

        public static TextMeshProUGUI OneLine(TextMeshProUGUI t, float minSize = 0f)
        {
            t.textWrappingMode = TextWrappingModes.NoWrap;
            if (minSize > 0f)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = t.fontSize;
                t.fontSizeMin = minSize;
            }

            return t;
        }

        /// <summary>Wraps onto as many lines as the rect holds, shrinking down to <paramref name="minSize"/> (long row labels other modules register:
        /// OneLine shrank them to an unreadable 6-10 px in the front-tablet lab).</summary>
        public static TextMeshProUGUI Wrapped(TextMeshProUGUI t, float minSize)
        {
            t.textWrappingMode = TextWrappingModes.Normal;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = minSize;
            t.lineSpacing = -12f; // ours: tighter lines so two fit a 44-unit row
            return t;
        }

        /// <summary>A title bar in the game's ksGui look (TITLE_BACK red, white title) - AC's in-game forms.</summary>
        public static TextMeshProUGUI TitleBar(Transform parent, string title, float x, float y, float w, float h)
        {
            Box(parent, "Title bar", x, y, w, h, TitleBack);
            return OneLine(Label(parent, "Title", title, h * 0.6f, Text, TextAlignmentOptions.MidlineLeft, x + 14f, y, w - 28f, h, AcFonts.Face.Bold), 12f);
        }

        /// <summary>A panel: FORM BACK with FORM BORDER (skin.ini).</summary>
        public static Image Panel(Transform parent, string name, float x, float y, float w, float h)
        {
            Image img = Box(parent, name, x, y, w, h, FormBack);
            Frame(parent, name + " rim", x, y, w, h, 1.5f, FormBorder);
            return img;
        }

        public static AcButton Button(Transform parent, string label, float x, float y, float w, float h, float textSize, Action onClick)
        {
            RectTransform r = Place(parent, "Button " + label, x, y, w, h);
            var b = r.gameObject.AddComponent<AcButton>();
            b.Build(label, textSize, onClick);
            return b;
        }

        /// <summary>Destroys every child of <paramref name="t"/> (a page or list rebuilt).</summary>
        public static void Clear(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject g = t.GetChild(i).gameObject;
                g.SetActive(false);
                UnityEngine.Object.Destroy(g);
            }
        }

        public static void SetLayerRecursive(Transform t, int layer)
        {
            if (t == null) return;
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        /// <summary>A UI sound (A's AcSfx.Ui, mapped by A to the launcher's own wavs); never throws.</summary>
        public static void Sound(string name)
        {
            try { AcSfx.Ui(name); }
            catch (Exception e) { AcLog.WarnOnce("Front: AcSfx.Ui failed (" + e.GetType().Name + ") - the menu stays silent"); }
        }
    }

    /// <summary>
    /// An AC launcher-style button: a CONTROL BACK slab, white caption; the launcher's accent while pointed at or selected; greyed when not
    /// interactable (a press then says why through <see cref="Refused"/>). Pointer handlers itself (no Selectable navigation), a 0.3 s
    /// double-press guard, "UI: 'X' pressed (mouse | pointer n)" in the log, every handler in try/catch.
    /// </summary>
    public sealed class AcButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private Image _slab, _bar;
        private TextMeshProUGUI _label;
        private Action _onClick;
        private bool _hover, _down, _selected, _interactable = true;
        private float _lastClick;
        private Vector2 _labelPos;

        public string Label { get { return _label != null ? _label.text : name; } }
        public TextMeshProUGUI Caption { get { return _label; } }
        public Image Slab { get { return _slab; } }
        /// <summary>A press while not interactable plays the "negative" sound and calls this (the page says why).</summary>
        public Action Refused;
        /// <summary>The sound of a press ("click"; pages use "select" for a choice).</summary>
        public string PressSound = "click";
        /// <summary>Plain (no slab colour change): a picture card draws its own.</summary>
        public bool Transparent;

        public void Build(string label, float textSize, Action onClick)
        {
            _onClick = onClick;
            var rect = (RectTransform)transform;
            float w = rect.sizeDelta.x, h = rect.sizeDelta.y;
            _slab = gameObject.AddComponent<Image>();
            _slab.raycastTarget = true;
            _bar = AcUi.Box(transform, "Accent", 0f, h - 3f, w, 3f, AcUi.Accent);
            _bar.enabled = false;
            _label = AcUi.Label(transform, "Label", label, textSize, AcUi.Text, TextAlignmentOptions.Center, 8f, 0f, w - 16f, h, AcFonts.Face.Regular);
            AcUi.OneLine(_label, textSize * 0.55f);
            _labelPos = _label.rectTransform.anchoredPosition;
            Refresh();
        }

        public bool Selected { get { return _selected; } set { if (_selected == value) return; _selected = value; Refresh(); } }

        public bool Interactable
        {
            get { return _interactable; }
            set { if (_interactable == value) return; _interactable = value; if (!value) { _hover = false; _down = false; } Refresh(); }
        }

        public void SetLabel(string text)
        {
            if (_label != null && _label.text != text) _label.text = text ?? "";
        }

        /// <summary>Moves the caption inside the button (units from its top-left), e.g. under a picture.</summary>
        public void SetCaptionRect(float x, float y, float w, float h, float size, TextAlignmentOptions align)
        {
            if (_label == null) return;
            _labelPos = new Vector2(x, -y);
            _label.rectTransform.anchoredPosition = _labelPos;
            _label.rectTransform.sizeDelta = new Vector2(w, h);
            _label.fontSize = size;
            _label.fontSizeMax = size;
            _label.alignment = align;
        }

        public void Refresh()
        {
            if (_slab == null) return;
            bool lit = _interactable && (_hover || _selected);
            if (Transparent) _slab.color = !_interactable ? new Color(0f, 0f, 0f, 0.35f) : lit ? new Color(AcUi.Accent.r, AcUi.Accent.g, AcUi.Accent.b, 0.35f) : new Color(1f, 1f, 1f, 0.06f);
            else _slab.color = !_interactable ? new Color(0.12f, 0.12f, 0.13f, 0.7f) : _selected ? AcUi.Accent : _hover ? Color.Lerp(AcUi.ControlBack, AcUi.Accent, 0.55f) : AcUi.ControlBack;
            if (_bar != null) _bar.enabled = _selected && Transparent;
            if (_label != null)
            {
                _label.color = !_interactable ? AcUi.Dim : AcUi.Text;
                _label.rectTransform.anchoredPosition = _labelPos + new Vector2(_down ? 1f : 0f, _down ? -1f : 0f);
            }
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (Time.unscaledTime - _lastClick < 0.3f) return; // ours: double-press guard
            _lastClick = Time.unscaledTime;
            if (!_interactable)
            {
                AcUi.Sound("negative");
                if (Refused != null) { try { Refused(); } catch (Exception ex) { AcLog.Warn("UI: refusal of '" + Label + "' failed: " + ex.Message); } }
                return;
            }

            AcLog.Info("UI: '" + Label + "' pressed (" + (e == null ? "code" : e.pointerId < 0 ? "mouse" : "pointer " + e.pointerId) + ")");
            AcUi.Sound(PressSound);
            try
            {
                if (_onClick != null) _onClick();
            }
            catch (Exception ex)
            {
                AcLog.Error("UI: '" + Label + "' failed: " + ex);
            }
        }

        /// <summary>The lab (and code) presses the button as a click would.</summary>
        public void Press()
        {
            _lastClick = -1f;
            OnPointerClick(null);
        }
    }

    /// <summary>
    /// Keeps a hub canvas where the player can read it (Halo's HaloPanelPlacer / BO2's Bo2PanelPlacer): upright on the hub floor, facing the player
    /// along the view's level direction; a headset at <see cref="VrDistance"/>, a monitor far enough that it fits the view (and beyond the avatar in
    /// third person). Placed 1 s after it is shown and again when the player looked away for 4 s or walked away.
    /// </summary>
    public sealed class AcPanelPlacer : MonoBehaviour
    {
        public float HeightMetres = 1.5f;
        public float VrDistance = 2.4f;   // ours
        public string What = "panel";
        public float FloorGap = 0.35f;    // ours
        /// <summary>Only places while the eye is within this many metres of the hub's centre.</summary>
        public float Range = 30f;          // ours
        /// <summary>On a track (no tablet linked): places anywhere, on the ground under the player's feet instead of the hub floor.</summary>
        public bool TrackMode;

        private Canvas _canvas;
        private float _shownAt, _nextCheck, _awayFor;
        private bool _placed;
        private int _logs;

        private void OnEnable()
        {
            _canvas = GetComponent<Canvas>();
            _shownAt = Time.unscaledTime;
            _placed = false;
            _awayFor = 0f;
        }

        public void Replace()
        {
            _placed = false;
            _shownAt = Time.unscaledTime;
        }

        public bool Placed { get { return _placed; } }

        private void LateUpdate()
        {
            Camera head = AcPlayer.Head();
            if (_canvas != null && _canvas.worldCamera != head) _canvas.worldCamera = head;
            if (head == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;
            Vector3 eye = head.transform.position;
            if (!TrackMode && (eye - AcSpace.RoomOrigin).magnitude > Range) return;

            if (!_placed)
            {
                if (Time.unscaledTime - _shownAt > 1f) Place(head, "shown");
                return;
            }

            Vector3 to = transform.position - eye, fwd = head.transform.forward;
            to.y = 0f;
            fwd.y = 0f;
            if (to.magnitude > 9f || to.magnitude < 0.8f)
            {
                Place(head, to.magnitude > 9f ? "the player moved away" : "the player walked into it");
                return;
            }

            bool away = fwd.sqrMagnitude > 1e-4f && Vector3.Angle(fwd, to) > 100f;
            _awayFor = away ? _awayFor + 0.25f : 0f;
            if (_awayFor > 4f) Place(head, "the player looked away");
        }

        public void Place(Camera head, string why)
        {
            bool vr = AcPlayer.XrPresent;
            Vector3 eye = head.transform.position;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float tan = Mathf.Tan(Mathf.Clamp(head.fieldOfView, 20f, 120f) * 0.5f * Mathf.Deg2Rad);
            float distance = vr ? VrDistance : Mathf.Clamp(HeightMetres / 0.62f / (2f * tan), 1.5f, 10f);
            Transform root = AcPlayer.Root;
            if (!vr && root != null)
            {
                Vector3 toAvatar = root.position - eye;
                toAvatar.y = 0f;
                float along = Vector3.Dot(toAvatar, fwd);
                if (along > 0.5f) distance = Mathf.Max(distance, along + 1.8f);
            }

            float floor = AcSpace.RoomOrigin.y;
            if (TrackMode)
            {
                Vector3 feet;
                floor = AcPlayer.TryFeet(out feet) ? feet.y : eye.y - 1.6f; // ours: eye height when the feet are unknown
            }

            Vector3 at = eye + fwd * distance;
            at.y = floor + FloorGap + HeightMetres * 0.5f;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            transform.position = at;
            _placed = true;
            _awayFor = 0f;
            if (_logs++ < 4)
                AcLog.Info("Hub: " + What + " placed on the floor " + distance.ToString("0.00") + " m in front of the " + (vr ? "headset" : "camera (" + head.fieldOfView.ToString("0") + " degrees)") + " (" + why + ").");
        }
    }

    /// <summary>A small gold crown drawn in code (nothing of the game): the instance master's mark (Halo's HaloCrown).</summary>
    public static class AcCrown
    {
        private static Sprite _sprite;
        private static Texture2D _tex;

        private static readonly Vector2[] Shape =
        {
            new Vector2(0.04f, 0.18f), new Vector2(0.04f, 0.90f), new Vector2(0.27f, 0.52f), new Vector2(0.50f, 0.98f), new Vector2(0.73f, 0.52f), new Vector2(0.96f, 0.90f), new Vector2(0.96f, 0.18f),
        };

        public static Sprite Sprite { get { if (_sprite == null) Make(); return _sprite; } }

        private static void Make()
        {
            const int W = 64, H = 48;
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Ac.Crown", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 3; sy++) for (int sx = 0; sx < 3; sx++) if (Inside((x + (sx + 0.5f) / 3f) / W, (y + (sy + 0.5f) / 3f) / H)) hit++;
                    Color c = AcUi.Gold;
                    c.a = hit / 9f;
                    px[y * W + x] = c;
                }
            }

            _tex.SetPixels32(px);
            _tex.Apply(false, true);
            _sprite = Sprite.Create(_tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool Inside(float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = Shape.Length - 1; i < Shape.Length; j = i++)
            {
                Vector2 a = Shape[i], b = Shape[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }
    }
}
