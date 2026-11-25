using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] One HUD label: a TextMeshPro text in AC's Segoe UI when the font could be built, else an <see cref="AcBitmapText"/> with AC's
    /// bitmap font. Set from a char buffer (AcHudFormat) or a string; pushes to the renderer only when the characters / colour change, so
    /// per-frame updates allocate nothing. Rects use a top-left origin inside the parent group (x right, y down), in reference pixels.
    /// </summary>
    public sealed class AcHudText
    {
        public RectTransform Rect { get; private set; }
        public GameObject Go { get; private set; }
        private TextMeshProUGUI _tmp;
        private AcBitmapText _bmp;
        private char[] _cur = new char[48];
        private int _curLen = -1;
        private Color _color;
        private char[] _scratch = new char[64];

        /// <summary>Makes a label. <paramref name="weight"/> 0 regular, 1 semibold, 2 light. <paramref name="overlay"/>: a material that
        /// draws over the car's cockpit (VR panel).</summary>
        public static AcHudText Create(Transform parent, string name, float x, float y, float w, float h, float size, TextAnchor align, Color color,
            int weight, Material overlayImageMat, bool overlay)
        {
            var t = new AcHudText();
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            t.Go = go;
            t.Rect = (RectTransform)go.transform;
            AcHudView.Place(t.Rect, x, y, w, h);
            t._color = color;
            TMP_FontAsset font = AcHudFonts.Of(weight);
            if (font != null)
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                if (overlay) tmp.fontSharedMaterial = AcHudView.OverlayFontMaterial(font);
                tmp.fontSize = size;
                tmp.color = color;
                tmp.richText = false;
                tmp.raycastTarget = false;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = ToTmp(align);
                tmp.margin = Vector4.zero;
                tmp.SetText(string.Empty);
                t._tmp = tmp;
            }
            else
            {
                var b = go.AddComponent<AcBitmapText>();
                b.SetFont(AcHudArt.Bitmap, AcHudArt.BitmapTexture);
                b.raycastTarget = false;
                b.color = color;
                b.Alignment = align;
                if (overlayImageMat != null) b.material = overlayImageMat;
                // the bitmap glyph image is the full line height: draw the rect at the TMP point size (cap height ~ 0.7 em) // ours
                AcHudView.Place(t.Rect, x, y + (h - size * 0.9f) * 0.5f, w, size * 0.9f);   // ours: the ink band ~ 0.9 em
                t._bmp = b;
            }
            return t;
        }

        private static TextAlignmentOptions ToTmp(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.BottomRight;
            }
        }

        /// <summary>Sets the text from a buffer (no allocation; only pushed when it changed).</summary>
        public void Set(char[] b, int len)
        {
            if (len < 0) len = 0;
            if (len == _curLen)
            {
                bool same = true;
                for (int i = 0; i < len; i++) if (_cur[i] != b[i]) { same = false; break; }
                if (same) return;
            }
            if (len > _cur.Length) _cur = new char[len + 16];
            System.Array.Copy(b, _cur, len);
            _curLen = len;
            if (_tmp != null) _tmp.SetCharArray(_cur, 0, len);
            else if (_bmp != null) _bmp.SetChars(_cur, len);
        }

        /// <summary>Sets the text from a string (copied into a scratch buffer: no allocation when unchanged).</summary>
        public void Set(string s)
        {
            if (s == null) s = string.Empty;
            if (s.Length > _scratch.Length) _scratch = new char[s.Length + 16];
            for (int i = 0; i < s.Length; i++) _scratch[i] = s[i];
            Set(_scratch, s.Length);
        }

        public Color Color
        {
            get { return _color; }
            set
            {
                if (_color == value) return;
                _color = value;
                if (_tmp != null) _tmp.color = value; else if (_bmp != null) _bmp.color = value;
            }
        }

        /// <summary>The characters shown now (allocates: lab / tests only).</summary>
        public string Text { get { return _curLen > 0 ? new string(_cur, 0, _curLen) : ""; } }

        public void Show(bool on) { if (Go.activeSelf != on) Go.SetActive(on); }
    }
}
