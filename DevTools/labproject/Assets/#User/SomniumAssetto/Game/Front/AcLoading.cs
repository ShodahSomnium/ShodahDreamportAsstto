using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The loading card between the hub and a track (Halo's HaloLoading recipe, AC content): the track's own ui/preview.png large, its
    /// outline.png as the map, the layout's name, city / country, length, direction (ui_track.json "run") and pit boxes, the session and rules,
    /// your car's badge and skin preview, AC's pause-menu logo, the stage text and a bar following AcSession.TrackLoading. A monitor sees a screen
    /// overlay; a headset a 3.4 m card 2.5 m ahead at eye height, following the view smoothly by heading only. When the track is ready (Hide, or the
    /// phase leaving Loading) the view is black at once and fades into the track over a second; leaving the track (Leave) fades to black, and the
    /// hub fades in when the player stands in it (BackToHub). Everything is made in code; a cover that cannot be made leaves the plain cut.
    /// AcFront drives it from AcSession.PhaseChanged / TrackLoading; R may call it too (idempotent). Log lines "Loading: ...".
    /// </summary>
    public static class AcLoading
    {
        /// <summary>The card is up.</summary>
        public static bool Showing { get; private set; }
        /// <summary>The layout the card shows (null when none).</summary>
        public static AcLayoutEntry Layout { get; private set; }

        /// <summary>Shows the card for <paramref name="layout"/> at once (no fade-in: the opaque card replaces the view in the same frame; a black cover
        /// still fading from Leave is carried over and fades out under it). A second call for the same layout returns at once.</summary>
        public static void Show(AcLayoutEntry layout, AcRules rules)
        {
            if (Showing && layout != null && Layout == layout && AcLoadingScreen.Current != null && AcLoadingScreen.Current.CardShown) return;
            Showing = true;
            Layout = layout;
            try
            {
                AcLoadingScreen.Begin(layout, rules);
                AcLog.Info("Loading: " + (layout != null ? layout.Key + " (" + layout.Name + ")" : "?") + " - " + (rules != null ? rules.ToString() : "no rules"));
            }
            catch (Exception e) { AcLog.Warn("Loading: the card could not start: " + e.Message); }
        }

        /// <summary>Progress 0..1 with a stage text.</summary>
        public static void Progress(float fraction, string label)
        {
            AcLoadingScreen s = AcLoadingScreen.Current;
            if (s != null) s.SetProgress(fraction, label);
        }

        /// <summary>Hides the card (black, then a ~1 s reveal of the track).</summary>
        public static void Hide()
        {
            bool was = Showing;
            Showing = false;
            AcLoadingScreen s = AcLoadingScreen.Current;
            if (s != null) { s.Cover(); s.Reveal(1.0f); }
            if (was) AcLog.Info("Loading: done - fading into " + (Layout != null ? Layout.Key : "the track") + ".");
        }

        /// <summary>Fade to black before leaving the track (back to the hub).</summary>
        public static void Leave()
        {
            AcLoadingScreen s = AcLoadingScreen.CoverNow();
            if (s != null) s.FadeToBlack(0.35f);
            AcLog.Info("Loading: leaving the track - black until the hub is back.");
        }

        /// <summary>Black now; the hub fades in when the player stands in it (or after 4 s).</summary>
        public static void BackToHub()
        {
            Showing = false;
            AcLoadingScreen s = AcLoadingScreen.CoverNow();
            if (s != null) s.RevealInHub();
        }

        /// <summary>World closing: everything of the card and the cover goes.</summary>
        public static void Shutdown()
        {
            Showing = false;
            Layout = null;
            AcLoadingScreen s = AcLoadingScreen.Current;
            if (s != null) UnityEngine.Object.Destroy(s.gameObject);
        }
    }

    /// <summary>The card and the black cover round the head (one at a time; see <see cref="AcLoading"/>).</summary>
    public sealed class AcLoadingScreen : MonoBehaviour
    {
        public static AcLoadingScreen Current { get; private set; }

        private const float CardWidth = 1920f, CardHeight = 1080f, CardMetresPerUnit = 3.4f / 1920f; // ours: 3.4 m wide in VR
        private const float BarX = 120f, BarY = 1000f, BarW = 1680f, BarH = 8f;

        /// <summary>Lab: build the headset's world-space card on a monitor too.</summary>
        public static bool ForceWorld;

        private Canvas _card;
        private CanvasGroup _group;
        private TextMeshProUGUI _stage, _percent, _time;
        private RectTransform _fill;
        private AcLayoutEntry _layout;
        private AcRules _rules;
        private float _startedAt, _shown, _target, _cardAlpha;
        private int _shownPercent = -1, _shownSeconds = -1;
        private bool _overlay, _placed, _cardGone, _hubWait;
        private Vector3 _cardPos;
        private Quaternion _cardRot = Quaternion.identity;
        private float _hubWaitUntil;

        private MeshRenderer _cover;
        private Material _coverMat;
        private float _alpha, _fadeFrom, _fadeTo, _fadeElapsed, _fadeLength;
        private bool _fading, _destroyWhenClear;

        public bool Covered { get { return _alpha >= 0.999f && !_fading; } }
        public bool CardShown { get { return _card != null && !_cardGone; } }
        public float CoverAlpha { get { return _alpha; } }
        public float Shown { get { return _shown; } }
        public bool Overlay { get { return _overlay; } }
        public Canvas Card { get { return _card; } }

        public static AcLoadingScreen Begin(AcLayoutEntry layout, AcRules rules)
        {
            float carried = 0f;
            if (Current != null) { carried = Current._alpha; Current.Dispose(); }
            AcLoadingScreen s = Make();
            s._layout = layout;
            s._rules = rules;
            s._startedAt = Time.realtimeSinceStartup;
            try { s.BuildCard(); }
            catch (Exception e) { AcLog.Warn("Loading: the card could not be built: " + e.Message); }
            if (carried > 0.01f) { s.SetAlpha(carried); s.FadeCover(0.5f); }
            return s;
        }

        private static AcLoadingScreen Make()
        {
            var go = new GameObject("Ac Loading");
            if (AcGame.Current != null) go.transform.SetParent(AcGame.Current.transform, false);
            var s = go.AddComponent<AcLoadingScreen>();
            Current = s;
            return s;
        }

        public static AcLoadingScreen CoverNow()
        {
            try { return Current != null ? Current : Make(); }
            catch (Exception e)
            {
                AcLog.Warn("Loading: the cover could not start: " + e.Message);
                return null;
            }
        }

        public void SetProgress(float f, string label)
        {
            if (f > _target) _target = Mathf.Clamp01(f);
            if (_stage != null && !string.IsNullOrEmpty(label) && _stage.text != label) _stage.text = label;
        }

        public void Cover()
        {
            _cardGone = true;
            DestroyCard();
            SetAlpha(1f);
            _fading = false;
            _hubWait = false;
        }

        public void Reveal(float seconds) { StartFade(0f, seconds, true); }

        public void FadeToBlack(float seconds)
        {
            _cardGone = true;
            DestroyCard();
            StartFade(1f, seconds, false);
        }

        public void RevealInHub()
        {
            Cover();
            _hubWait = true;
            _hubWaitUntil = Time.realtimeSinceStartup + 4f; // ours
        }

        private void FadeCover(float seconds) { StartFade(0f, seconds, false); }

        private void StartFade(float to, float seconds, bool destroyWhenClear)
        {
            _fadeFrom = _alpha;
            _fadeTo = to;
            _fadeElapsed = 0f;
            _fadeLength = Mathf.Max(0.05f, seconds);
            _fading = true;
            _destroyWhenClear = destroyWhenClear;
        }

        private void Dispose()
        {
            if (Current == this) Current = null;
            if (_coverMat != null) Destroy(_coverMat);
            if (_cover != null) Destroy(_cover.gameObject);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_cover != null) Destroy(_cover.gameObject);
            if (_coverMat != null) Destroy(_coverMat);
        }

        private void DestroyCard()
        {
            if (_card != null) Destroy(_card.gameObject);
            _card = null;
            _group = null;
        }

        // ------------------------------------------------------------------ the card

        private void BuildCard()
        {
            _overlay = !AcPlayer.XrPresent && !ForceWorld;
            RectTransform frame;
            if (_overlay)
            {
                var go = new GameObject("Ac Loading Card");
                go.layer = AcUi.UiLayer;
                go.transform.SetParent(transform, false);
                _card = go.AddComponent<Canvas>();
                _card.renderMode = RenderMode.ScreenSpaceOverlay;
                _card.sortingOrder = -5;
                var sc = go.AddComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(CardWidth, CardHeight);
                sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                sc.matchWidthOrHeight = 1f;
                Full(go.transform, "Black", Color.black);
                frame = (RectTransform)new GameObject("Frame", typeof(RectTransform)).transform;
                frame.gameObject.layer = AcUi.UiLayer;
                frame.SetParent(go.transform, false);
                frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
                frame.sizeDelta = new Vector2(CardWidth, CardHeight);
            }
            else
            {
                _card = AcUi.MakeCanvas("Ac Loading Card", transform, CardWidth, CardHeight, CardMetresPerUnit);
                foreach (GraphicRaycaster r in _card.GetComponents<GraphicRaycaster>()) Destroy(r);
                foreach (Component c in _card.GetComponents<Component>()) if (c != null && c.GetType().Name == "TrackedDeviceGraphicRaycaster") Destroy(c);
                Transform catcher = _card.transform.Find("Pointer catcher");
                if (catcher != null) Destroy(catcher.gameObject);
                frame = AcUi.Place(_card.transform, "Frame", 0f, 0f, CardWidth, CardHeight);
                AcUi.Box(frame, "Black", 0f, 0f, CardWidth, CardHeight, Color.black);
                _card.worldCamera = AcPlayer.Head();
            }

            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            AcLayoutEntry l = _layout;
            AcInstall inst = AcGame.Current != null ? AcGame.Current.Install : null;
            // the track's preview large (355 x 200 in the install, drawn 1120 x 630), AC's red title bar above, the outline as the map
            AcUi.Box(frame, "Title band", 0f, 0f, CardWidth, 96f, AcUi.TitleBack);
            AcUi.OneLine(AcUi.Label(frame, "Title", (l != null ? l.Name : "").ToUpperInvariant(), 58f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 120f, 0f, 1300f, 96f, AcFonts.Face.Bold), 26f);
            if (inst != null) AcUi.Picture(frame, "Logo", 1700f, 6f, 84f, 84f, System.IO.Path.Combine(System.IO.Path.Combine(inst.Content, "gui"), "pauseMenuLogo.png"), 256, Color.white);
            AcUi.Box(frame, "Preview back", 120f, 150f, 1120f, 630f, new Color(0.08f, 0.08f, 0.09f, 1f));
            if (l != null) AcUi.Picture(frame, "Preview", 120f, 150f, 1120f, 630f, l.PreviewPath, 1024, Color.white);
            AcUi.Box(frame, "Map back", 1300f, 150f, 500f, 300f, new Color(0f, 0f, 0f, 0.6f));
            if (l != null) AcUi.Picture(frame, "Outline", 1310f, 160f, 480f, 280f, l.OutlinePath, 512, Color.white);

            string place = l != null ? Join(l.City, l.Country) : "";
            float y = 470f;
            Info(frame, ref y, place);
            if (l != null)
            {
                Info(frame, ref y, AcUiFormat.Length(l.LengthM, AcFront.UseMph()) + (string.IsNullOrEmpty(l.Run) ? "" : "   -   " + l.Run));
                if (l.Pitboxes > 0) Info(frame, ref y, l.Pitboxes + " pit boxes");
            }

            string session = _rules != null ? AcLocale.Session((int)_rules.Session) + (_rules.Session == AcSessionType.Race ? (_rules.Laps > 0 ? "   " + _rules.Laps + " " + AcLocale.S("Laps", "LAPS") : "   " + _rules.TimeMinutes + " min") : _rules.TimeMinutes > 0 ? "   " + _rules.TimeMinutes + " min" : "") : "";
            Info(frame, ref y, session);

            // your car: badge + skin preview + name
            AcCarEntry car = AcGame.Current != null && AcGame.Current.Content != null && !string.IsNullOrEmpty(AcLocal.CarId) ? AcGame.Current.Content.Car(AcLocal.CarId) : null;
            if (car != null)
            {
                AcSkinEntry skin = car.Skin(AcLocal.SkinId);
                if (skin == null && car.Skins.Count > 0) skin = car.Skins[0];
                // below the last info line (4 lines end at 646: the badge at a fixed 630 overlapped the session line, lab front-loading) // ours
                float carY = Mathf.Max(630f, y + 8f);
                if (skin != null) AcUi.Picture(frame, "Car preview", 1300f, carY + 60f, 500f, 140f, skin.PreviewPath, 512, Color.white);
                AcUi.Picture(frame, "Badge", 1300f, carY, 52f, 52f, car.BadgePath, 128, Color.white);
                AcUi.OneLine(AcUi.Label(frame, "Car", car.Name, 28f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 1364f, carY, 436f, 52f), 14f);
            }

            if (l != null && !string.IsNullOrEmpty(l.Description))
            {
                var d = AcUi.Label(frame, "Description", l.Description, 22f, AcUi.Grey, TextAlignmentOptions.TopLeft, 120f, 800f, 1120f, 120f, AcFonts.Face.Light);
                d.overflowMode = TextOverflowModes.Ellipsis;
            }

            _stage = AcUi.OneLine(AcUi.Label(frame, "Stage", "Starting...", 26f, AcUi.Text, TextAlignmentOptions.Left, 120f, 944f, 1300f, 40f, AcFonts.Face.Light), 12f);
            _percent = AcUi.OneLine(AcUi.Label(frame, "Percent", "0 %", 34f, AcUi.Text, TextAlignmentOptions.Right, 1500f, 940f, 300f, 46f, AcFonts.Face.Bold));
            _time = AcUi.OneLine(AcUi.Label(frame, "Time", "", 22f, AcUi.Grey, TextAlignmentOptions.Right, 1500f, 896f, 300f, 34f));
            AcUi.Box(frame, "Bar track", BarX, BarY, BarW, BarH, AcUi.ControlBack);
            _fill = AcUi.Box(frame, "Bar fill", BarX, BarY, 0f, BarH, AcUi.Accent).rectTransform;
            _shownPercent = _shownSeconds = -1;
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + ", " + b;
        }

        private static void Info(RectTransform frame, ref float y, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            AcUi.OneLine(AcUi.Label(frame, "Info", text, 30f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 1300f, y, 500f, 40f, AcFonts.Face.Light), 14f);
            y += 44f;
        }

        private static void Full(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = AcUi.UiLayer;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        private void PlaceCard(Camera head)
        {
            bool vr = AcPlayer.XrPresent;
            Vector3 eye = head.transform.position, fwd = head.transform.forward;
            float tan = Mathf.Tan(Mathf.Clamp(head.fieldOfView, 20f, 120f) * 0.5f * Mathf.Deg2Rad);
            float height = CardHeight * CardMetresPerUnit, width = CardWidth * CardMetresPerUnit;
            float distance = vr ? 2.5f : Mathf.Clamp(Mathf.Min(height / (2f * tan), width / (2f * tan * Mathf.Max(0.5f, head.aspect))) * 0.98f, 0.5f, 8f);
            fwd.y = vr ? 0f : fwd.y;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 target = eye + fwd * distance;
            if (vr) target.y = eye.y;
            Quaternion look = vr ? Quaternion.LookRotation(target - eye, Vector3.up) : head.transform.rotation;
            if (!_placed || !vr) { _cardPos = target; _cardRot = look; _placed = true; }
            else
            {
                float k = 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime); // ours: smoothing
                _cardPos = Vector3.Lerp(_cardPos, target, k);
                _cardRot = Quaternion.Slerp(_cardRot, look, k);
            }

            _card.transform.SetPositionAndRotation(_cardPos, _cardRot);
        }

        // ------------------------------------------------------------------ the cover

        private void BuildCover()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ac Loading Cover";
            Destroy(go.GetComponent<Collider>());
            Camera head = AcPlayer.Head();
            go.transform.SetParent(head != null ? head.transform : transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.6f;
            _cover = go.GetComponent<MeshRenderer>();
            Shader sh = AcShaders.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            _coverMat = new Material(sh) { name = "Ac loading cover" };
            _coverMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            _coverMat.SetFloat("_Surface", 1f);
            _coverMat.SetFloat("_Blend", 0f);
            _coverMat.SetOverrideTag("RenderType", "Transparent");
            _coverMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _coverMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _coverMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _coverMat.SetFloat("_ZWrite", 0f);
            _coverMat.SetFloat("_Cull", 0f);
            _coverMat.renderQueue = 4000;
            _cover.sharedMaterial = _coverMat;
            _cover.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cover.receiveShadows = false;
            go.SetActive(false);
        }

        private void SetAlpha(float a)
        {
            _alpha = Mathf.Clamp01(a);
            if (_cover == null && _alpha > 0.001f)
            {
                try { BuildCover(); }
                catch (Exception e) { AcLog.Warn("Loading: the cover could not be made: " + e.Message); }
            }

            if (_cover == null) return;
            bool on = _alpha > 0.002f;
            if (_cover.gameObject.activeSelf != on) _cover.gameObject.SetActive(on);
            if (on) _coverMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, _alpha));
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            Camera head = AcPlayer.Head();
            if (_cover != null && head != null && _cover.transform.parent != head.transform)
            {
                _cover.transform.SetParent(head.transform, false);
                _cover.transform.localPosition = Vector3.zero;
                _cover.transform.localRotation = Quaternion.identity;
            }

            if (_hubWait)
            {
                Vector3 feet;
                bool there = AcPlayer.TryFeet(out feet) && !AcHub.IsAway(feet);
                if (there || Time.realtimeSinceStartup > _hubWaitUntil)
                {
                    _hubWait = false;
                    AcLog.Info("Loading: back in the hub (" + (there ? "the player stands in the showroom" : "waited 4 s") + ") - fading in.");
                    Reveal(0.8f);
                }
            }

            if (_fading)
            {
                _fadeElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float u = Mathf.Clamp01(_fadeElapsed / _fadeLength);
                u = u * u * (3f - 2f * u);
                SetAlpha(Mathf.Lerp(_fadeFrom, _fadeTo, u));
                if (u >= 1f)
                {
                    _fading = false;
                    if (_destroyWhenClear && _alpha <= 0.002f) { Dispose(); return; }
                }
            }

            if (_card == null) return;
            _cardAlpha = Mathf.MoveTowards(_cardAlpha, 1f, Time.unscaledDeltaTime * 2.5f);
            if (_group != null) _group.alpha = _cardAlpha;
            if (head != null && !_overlay)
            {
                _card.worldCamera = head;
                PlaceCard(head);
            }

            float now = Time.realtimeSinceStartup;
            // The bar creeps on its own between reports (never past the last report + 5 %) and catches up quickly. // ours
            float ceiling = Mathf.Min(0.985f, _target + 0.05f);
            float want = Mathf.Max(_shown, Mathf.Min(ceiling, _shown + Time.unscaledDeltaTime * 0.012f));
            if (_target > want) want = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * 0.5f);
            _shown = Mathf.Clamp01(want);
            if (_fill != null) _fill.sizeDelta = new Vector2(BarW * _shown, BarH);
            int percent = Mathf.RoundToInt(_shown * 100f), seconds = (int)(now - _startedAt);
            if (_percent != null && percent != _shownPercent) { _shownPercent = percent; _percent.text = percent + " %"; }
            if (_time != null && seconds != _shownSeconds) { _shownSeconds = seconds; _time.text = seconds + " s"; }
        }
    }
}
