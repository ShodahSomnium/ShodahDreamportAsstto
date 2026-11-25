using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] A UI text drawn with one of AC's bitmap fonts (content/fonts, <see cref="AcBitmapFont"/>): one quad per glyph from the font's
    /// single-row image, UI/Default shader (always in the client), so it renders on a ScreenSpaceOverlay and on a world-space canvas alike.
    /// The HUD's fallback when no TrueType font can be built (TMP shader missing) and usable by C for digital displays on a canvas.
    /// Text is set from a char buffer; the mesh is rebuilt only when the text changes (no allocation in steady state).
    /// </summary>
    public sealed class AcBitmapText : MaskableGraphic
    {
        private AcBitmapFont _font;
        private Texture _tex;
        private char[] _chars = new char[48];
        private int _len;
        private float[] _quads = new float[48 * 8];
        private TextAnchor _align = TextAnchor.MiddleLeft;
        private float _spacing;

        /// <summary>The font and its uploaded texture (AcHudArt.LoadBitmapFont).</summary>
        public void SetFont(AcBitmapFont font, Texture texture)
        {
            _font = font; _tex = texture;
            SetMaterialDirty(); SetVerticesDirty();
        }

        public TextAnchor Alignment { get { return _align; } set { if (_align != value) { _align = value; SetVerticesDirty(); } } }
        /// <summary>Extra space between glyphs in units of the text height.</summary>
        public float Spacing { get { return _spacing; } set { if (_spacing != value) { _spacing = value; SetVerticesDirty(); } } }

        public override Texture mainTexture { get { return _tex != null ? _tex : s_WhiteTexture; } }

        /// <summary>Sets the text (copied); rebuilds only when it differs.</summary>
        public void SetChars(char[] text, int length)
        {
            if (length < 0) length = 0;
            if (length == _len)
            {
                bool same = true;
                for (int i = 0; i < length; i++) if (_chars[i] != text[i]) { same = false; break; }
                if (same) return;
            }
            if (length > _chars.Length)
            {
                _chars = new char[length + 16];
                _quads = new float[(length + 16) * 8];
            }
            System.Array.Copy(text, _chars, length);
            _len = length;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_font == null || _len == 0) return;
            Rect r = GetPixelAdjustedRect();
            float h = r.height;
            float w = (_font.Measure(_chars, 0, _len) + _spacing * (_len - 1)) * h;
            if (w > r.width && w > 0f) { h *= r.width / w; w = r.width; }      // shrink to fit, keep the aspect
            float x;
            switch (_align)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x = r.xMin + (r.width - w) * 0.5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x = r.xMax - w; break;
                default: x = r.xMin; break;
            }
            float y;
            switch (_align)
            {
                case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: y = r.yMax - h; break;
                case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: y = r.yMin; break;
                default: y = r.yMin + (r.height - h) * 0.5f; break;
            }
            int n = _font.Layout(_chars, 0, _len, x, y, h, 1f, _spacing, _quads);
            Color32 c = color;
            for (int q = 0; q < n; q++)
            {
                int o = q * 8;
                float x0 = _quads[o], y0 = _quads[o + 1], x1 = _quads[o + 2], y1 = _quads[o + 3], u0 = _quads[o + 4], u1 = _quads[o + 5];
                float v0 = _quads[o + 6], v1 = _quads[o + 7];
                int v = vh.currentVertCount;
                // Layout gives v in Unity convention (LoadImage puts the PNG top row at v = 1), cropped to the ink band
                vh.AddVert(new Vector3(x0, y0), c, new Vector4(u0, v0, 0f, 0f));
                vh.AddVert(new Vector3(x0, y1), c, new Vector4(u0, v1, 0f, 0f));
                vh.AddVert(new Vector3(x1, y1), c, new Vector4(u1, v1, 0f, 0f));
                vh.AddVert(new Vector3(x1, y0), c, new Vector4(u1, v0, 0f, 0f));
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v + 2, v + 3, v);
            }
        }
    }
}
