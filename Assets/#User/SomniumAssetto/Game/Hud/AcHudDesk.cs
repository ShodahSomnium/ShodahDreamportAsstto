using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] The monitor HUD: one ScreenSpaceOverlay canvas (port of BO2's Bo2DeskHud: drawn after the camera and the client's
    /// upscaling, fixed to the pixels, sorting order -10 so the client's own menus cover it, no raycaster so it never takes the mouse),
    /// scaled with the screen height from a 1920x1080 reference - AC's HUD art is drawn 1:1 at 1080p (the dial at 1.5x). The groups sit
    /// where AC puts them (H NOTES "Layout"). Setting hud.scale (0.6..1.6) shrinks / grows everything.
    /// </summary>
    public sealed class AcHudDesk
    {
        public const int SortingOrder = -10;
        public const float RefW = 1920f, RefH = 1080f;

        public Canvas Canvas { get; private set; }
        public AcHudView View { get; private set; }
        public GameObject Go { get; private set; }
        private CanvasScaler _scaler;
        private float _scale = -1f;
        private bool _lab;

        public static AcHudDesk Create(Transform parent)
        {
            var d = new AcHudDesk();
            var go = new GameObject("Ac HUD (monitor overlay)");
            go.layer = AcLayers.Ui;
            go.transform.SetParent(parent, false);
            d.Go = go;
            d.Canvas = go.AddComponent<Canvas>();
            d.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            d.Canvas.sortingOrder = SortingOrder;
            d.Canvas.pixelPerfect = false;
            d._scaler = go.AddComponent<CanvasScaler>();
            d._scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            d._scaler.referenceResolution = new Vector2(RefW, RefH);
            d._scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            d._scaler.matchWidthOrHeight = 1f;     // the height decides the size; a wider screen moves the corners out

            var root = new GameObject("Root", typeof(RectTransform));
            root.layer = AcLayers.Ui;
            root.transform.SetParent(go.transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            d.View = new AcHudView(rt, false);
            d.Layout();
            return d;
        }

        /// <summary>Applies hud.scale (reference resolution / scale keeps the corners in the corners).</summary>
        public void SetScale(float s)
        {
            s = Mathf.Clamp(s, 0.5f, 2f);
            if (Mathf.Abs(s - _scale) < 0.001f || _lab) return;
            _scale = s;
            _scaler.referenceResolution = new Vector2(RefW / s, RefH / s);
        }

        private static void At(RectTransform g, float ax, float ay, float px, float py, float x, float y)
        {
            g.anchorMin = g.anchorMax = new Vector2(ax, ay);
            g.pivot = new Vector2(px, py);
            g.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>Where each group sits on the screen (reference pixels from its anchor). // ours, matched to AC's default HUD</summary>
        public void Layout()
        {
            var v = View;
            var cfg = AcHudArt.Config ?? new AcHudConfig();
            At(v.GTimes, 0f, 1f, 0f, 1f, 24f, -24f);                          // top left: session + lap times
            At(v.GTower, 0f, 1f, 0f, 1f, 24f, -24f - 198f - 14f);              // under it: standings
            At(v.GLights, 0.5f, 1f, 0.5f, 0.5f, 0f, -110f);                    // top centre
            At(v.GDelta, 0.5f, 1f, 0.5f, 0.5f, 0f, -cfg.TimeDifferencePosY);   // gameplay.ini [TIME_DIFFERENCE] POS_Y (AC px, 1080 high)
            At(v.GSplit, 0.5f, 1f, 0.5f, 0.5f, 0f, -cfg.TimeDifferencePosY - 50f);                           // sector split under it // ours
            At(v.GFlag, 0.5f, 1f, 0.5f, 0.5f, 330f, -110f);
            At(v.GMsg, 0.5f, 1f, 0.5f, 1f, 0f, -290f);
            At(v.GWrong, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 120f);
            At(v.GPit, 0.5f, 0f, 0.5f, 0f, 0f, 282f);
            At(v.GDial, 0.5f, 0f, 0.5f, 0f, 0f, 8f);                           // bottom centre
            At(v.GEss, 0.5f, 0f, 0.5f, 0f, 0f, 24f);
            // damage displayer: damage_displayer.ini POSITION_X (px from the left) and DISTANCE_FROM_CENTER_Y (px from the screen centre, y up as
            // Unity: -200 = 200 px below the centre, clear of the standings tower) on the 1080p reference // ours reading of the two keys
            AcDamageRules dr = AcDamageRules.Current;
            At(v.GDamage, 0f, 0.5f, 0f, 0.5f, dr.PosX, dr.CenterY);
        }

        public void Show(bool on) { if (Go.activeSelf != on) Go.SetActive(on); }

        /// <summary>
        /// Lab only: a camera's Render() does not draw ScreenSpaceOverlay canvases, so the lab hangs the same canvas in world space exactly
        /// filling <paramref name="cam"/>'s view (16:9, 1080 units high) - the picture is the monitor HUD as a player sees it.
        /// </summary>
        public void LabFrame(Camera cam, float aspect)
        {
            _lab = true;
            Canvas.renderMode = RenderMode.WorldSpace;
            _scaler.enabled = false;
            var rt = (RectTransform)Go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RefH * aspect, RefH);
            const float d = 1f;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            rt.localScale = Vector3.one * (h / RefH);
            rt.position = cam.transform.position + cam.transform.forward * d;
            rt.rotation = cam.transform.rotation;
            Canvas.worldCamera = cam;
        }

        public void Destroy() { if (Go != null) Object.Destroy(Go); Go = null; }
    }
}
