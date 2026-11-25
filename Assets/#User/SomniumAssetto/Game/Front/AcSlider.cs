using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] A VR-safe slider (port of WoW's WowSlider, shipped): the label above left, the value above right; pointer down / drag on the
    /// track sets the value (the VR ray's hit point on the canvas plane, else the mouse's screen point), "-" and "+" buttons step it for a steady
    /// hand in a headset. Plain images in the game's colours (CONTROL BACK track, the launcher accent fill). The value is snapped to the step.
    /// </summary>
    public sealed class AcSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _track;
        private Image _thumb, _fill;
        private TextMeshProUGUI _label, _valueText;
        private AcButton _minus, _plus;
        private float _min, _max, _step, _value;
        private Func<float, string> _format;
        private Action<float> _changed;
        private bool _interactable = true;
        private float _trackWidth;

        public static AcSlider Create(Transform parent, string label, float x, float y, float w, float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
        {
            RectTransform r = AcUi.Place(parent, "Slider " + label, x, y, w, 56f);
            var s = r.gameObject.AddComponent<AcSlider>();
            s.Build(label, w, min, max, step, value, format, changed);
            return s;
        }

        private static string Percent(float v) { return Mathf.RoundToInt(v * 100f) + " %"; }

        private void Build(string label, float w, float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
        {
            _min = min;
            _max = max;
            _step = step;
            _format = format ?? Percent;
            _changed = changed;
            _label = AcUi.OneLine(AcUi.Label(transform, "Label", label, 20f, AcUi.Text, TextAlignmentOptions.Left, 0f, 0f, w - 110f, 24f), 12f);
            _valueText = AcUi.OneLine(AcUi.Label(transform, "Value", "", 20f, AcUi.Text, TextAlignmentOptions.Right, w - 110f, 0f, 110f, 24f));

            const float Btn = 42f;
            _minus = AcUi.Button(transform, "-", 0f, 24f, Btn, 32f, 24f, StepDown);
            _plus = AcUi.Button(transform, "+", w - Btn, 24f, Btn, 32f, 24f, StepUp);
            _trackWidth = w - 2f * Btn - 28f;
            _track = AcUi.Place(transform, "Track", Btn + 14f, 30f, _trackWidth, 20f);
            var groove = _track.gameObject.AddComponent<Image>();
            groove.color = AcUi.ControlBack;
            groove.raycastTarget = true;
            _fill = AcUi.Box(_track, "Fill", 0f, 4f, 0f, 12f, AcUi.Accent);
            var fwd = _track.gameObject.AddComponent<AcSliderTrack>();
            fwd.Owner = this;
            _thumb = AcUi.Box(_track, "Thumb", 0f, -6f, 12f, 32f, AcUi.Text);
            SetValue(value, false);
        }

        public float Value { get { return _value; } }

        public bool Interactable
        {
            get { return _interactable; }
            set
            {
                _interactable = value;
                if (_minus != null) _minus.Interactable = value;
                if (_plus != null) _plus.Interactable = value;
                if (_label != null) _label.color = value ? AcUi.Text : AcUi.Dim;
            }
        }

        /// <summary>Sets the shown value (clamped and snapped to the step); notify = call the changed callback.</summary>
        public void SetValue(float v, bool notify)
        {
            if (float.IsNaN(v)) return;
            float s = _step > 0f ? _min + Mathf.Round((v - _min) / _step) * _step : v;
            s = Mathf.Clamp(s, _min, _max);
            bool changed = Mathf.Abs(s - _value) > 1e-5f;
            _value = s;
            float t = _max > _min ? (s - _min) / (_max - _min) : 0f;
            if (_thumb != null) _thumb.rectTransform.anchoredPosition = new Vector2(t * _trackWidth - 6f, _thumb.rectTransform.anchoredPosition.y);
            if (_fill != null) _fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, t * _trackWidth), 12f);
            if (_valueText != null) _valueText.text = _format(s);

            if (notify && changed && _changed != null)
            {
                try { _changed(s); }
                catch (Exception ex) { AcLog.Error("UI: slider '" + (_label != null ? _label.text : "?") + "' failed: " + ex); }
            }
        }

        private void StepDown() { Step(-1); }
        private void StepUp() { Step(1); }

        private void Step(int dir)
        {
            if (!_interactable) return;
            SetValue(_value + dir * (_step > 0f ? _step : (_max - _min) / 20f), true);
        }

        internal void Pointer(PointerEventData e)
        {
            if (!_interactable || e == null || _track == null) return;
            Vector2 local = Vector2.zero;
            bool ok;
            // A VR ray (and uGUI's GraphicRaycaster on a world-space canvas) reports where it hit the canvas plane.
            if (e.pointerCurrentRaycast.gameObject != null && e.pointerCurrentRaycast.worldPosition != Vector3.zero)
            {
                local = _track.InverseTransformPoint(e.pointerCurrentRaycast.worldPosition);
                ok = true;
            }
            else
            {
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : e.enterEventCamera;
                ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, e.position, cam, out local);
            }

            if (!ok) return;
            Rect r = _track.rect;
            float t = r.width > 0f ? Mathf.Clamp01((local.x - r.xMin) / r.width) : 0f;
            SetValue(_min + t * (_max - _min), true);
        }

        public void OnPointerDown(PointerEventData e) { Pointer(e); }
        public void OnDrag(PointerEventData e) { Pointer(e); }
        public void OnPointerUp(PointerEventData e) { }
    }

    /// <summary>Forwards pointer down / drag on a slider's track to its AcSlider.</summary>
    public sealed class AcSliderTrack : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public AcSlider Owner;

        public void OnPointerDown(PointerEventData e) { if (Owner != null) Owner.Pointer(e); }
        public void OnDrag(PointerEventData e) { if (Owner != null) Owner.Pointer(e); }
    }
}
