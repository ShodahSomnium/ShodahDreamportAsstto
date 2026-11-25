using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The Somnium client's own tablet, borrowed for the "ASSETTO CORSA" app (port of the Euro Truck Sim Dreamport's EtsTabletLink,
    /// proven in the client 2026-09-23 / 2026-10-05; research tablet_front.md sections 1-2).
    ///
    /// There is no tablet API for worlds (the Bridge has no tablet member). The client instantiates Tablet.XR / Tablet.NonXR into "MasterScene"
    /// for the local player; it is found by searching, by the client's component TYPE names first (TabletControl, TabsContainer, HomeTab,
    /// TabletStatusBarView - not renamed in the shipped client) with object names as the fallback.
    ///
    /// What it does, measured against the client source (ETS's notes, kept):
    ///  - never DontDestroyOnLoad: the tablet lives in MasterScene and outlives the world, so OnDestroy / <see cref="Shutdown"/> take the button and
    ///    the canvas off it and restore everything hidden or resized; objects an earlier visit left (named AC.Tablet.*) are swept at every link;
    ///  - the home button is ONE client button cloned under an INACTIVE holder and stripped to Unity UI + TMP before it is ever active (no client
    ///    script wakes on the copy), its persistent onClick dropped; it goes FIRST in the home grid; another Dreamport's clone is never the model;
    ///  - the app's canvas is created as a sibling of the tabs under "Canvases" (it fades with the tablet), on the HomeTab canvas's layer, with its
    ///    sorting, camera and raycasters mirrored; it follows the HomeTab rect every LateUpdate (the player's own Resize too);
    ///  - Resize is pressed only when the HomeTab measures Small, and pressed back on close only if we enlarged it and it is still Large;
    ///  - a client tab switch (any tab's ContentView changing) closes the app.
    /// Placement while driving (the client's tablet is only nudged):
    ///  - desktop: the open tablet is put back on Camera.main/TabletPoint in LateUpdate (order 1000, after the car camera) and at the first game
    ///    camera's render, only when it is within a frame's travel of the point - speed-based here (AC cars do 300+ km/h; ETS's fixed 1.5 m
    ///    is one frame at 160 km/h and 30 fps);
    ///  - VR, "tablet follows player" off and the tablet not held: the seated car's per-frame delta is applied to it (<see cref="RidingOn"/>).
    /// Cost: discovery walks MasterScene and the other scenes (skipping the world's own "Ac ..." subtrees and remote "Simulator" tablets) every
    /// 0.5 s for a minute, then every 5 s, only while unlinked. Linked: an Animator bool and a rect copy a frame, a few checks at 4 Hz.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class AcTabletLink : MonoBehaviour
    {
        /// <summary>The prefix of everything this puts on the client's tablet; swept on every link.</summary>
        public const string Owned = "AC.Tablet.";
        /// <summary>Home grids wider than this are the Large tablet (Small 960, Large 1920: Tablet.XR.HomeTab.prefab:424-434).</summary>
        public const float LargeAbove = 1440f;
        /// <summary>The client's home grid cell (Tablet.XR.HomeTab.prefab:273-286), for the one placement log line.</summary>
        public const float GridCellWidth = 235f, GridCellHeight = 250f;

        public static AcTabletLink Instance { get; private set; }

        [Tooltip("Seconds between searches for the tablet while it has not been found (5 s after the first minute).")]
        public float SearchInterval = 0.5f;
        [Tooltip("Caption under the app's icon on the tablet's home screen.")]
        public string AppTitle = "ASSETTO CORSA";
        [Tooltip("Press the tablet's Resize button when the app opens on a Small tablet, and again when it closes.")]
        public bool ForceLarge = true;
        [Tooltip("Desktop: put the open tablet back on the camera's TabletPoint after the car camera has moved it.")]
        public bool FixDesktopLag = true;
        [Tooltip("VR, tablet not following the player: carry it along with the car the player sits in.")]
        public bool CarryWithVehicle = true;
        /// <summary>The app's layout rectangle inside the tablet's screen (canvas units).</summary>
        public float FrameWidth = 1600f, FrameHeight = 900f;

        /// <summary>The home-screen icon (painted by AcTabletApp; nothing of the game unless read at run time).</summary>
        public Texture2D AppIcon;
        /// <summary>The platform the local player rides (the seated car's body), or null.</summary>
        public Func<Transform> RidingOn;

        public event Action<bool> AppShown;
        public event Action<bool> TabletOpenChanged;
        public event Action LinkChanged;

        public bool IsLinked { get { return _t != null && _t.Root != null; } }
        public bool IsDesktopTablet { get { return _t != null && _t.Desktop; } }
        public bool TabletOpen { get; private set; }
        public bool AppOpen { get; private set; }
        /// <summary>The rectangle the app lays itself out in, inside the tablet's screen; null unless linked.</summary>
        public RectTransform Frame { get { return IsLinked ? _frame : null; } }
        /// <summary>The app's canvas (null unless linked) - the lab's ray test uses its TrackedDeviceGraphicRaycaster.</summary>
        public Canvas AppCanvas { get { return IsLinked ? _appCanvas : null; } }
        /// <summary>The layer the client's own tablet graphics carry (5, UI), which the XR ray tests per graphic.</summary>
        public int UiLayer { get { return IsLinked ? _t.UiLayer : AcUi.UiLayer; } }
        /// <summary>The last discovery result, as logged.</summary>
        public string Report { get; private set; } = "not searched yet";
        /// <summary>The home-screen button exists and is shown.</summary>
        public bool ButtonVisible { get { return _button != null && _button.activeSelf; } }
        /// <summary>The texture the home button shows now (lab: the launcher logo icon after the boot).</summary>
        public Texture IconShown { get { return _buttonIcon != null && _buttonIcon.sprite != null ? _buttonIcon.sprite.texture : null; } }
        /// <summary>Whether the tablet's own "Exit World" button was found.</summary>
        public bool CanExitWorld { get { return IsLinked && _t.ExitWorld != null; } }

        /// <summary>False hides the home-screen button (and closes the app).</summary>
        public bool AppAvailable
        {
            get { return _available; }
            set
            {
                if (_available == value) return;
                _available = value;
                if (!value && AppOpen) Close(true, "not available now");
                if (_button != null) _button.SetActive(value);
            }
        }

        private sealed class Parts
        {
            public Transform Root;
            public Component Control;
            public MethodInfo Deactivate;
            public bool Desktop;
            public Animator Animator;
            public bool AnimatorHasIsOpen;
            public GameObject GrabCollider;
            public XRGrabInteractable Grab;
            public Transform Canvases;
            public CanvasGroup CanvasesGroup;
            public Transform Tabs;
            public Canvas HomeCanvas;
            public RectTransform Home;
            public GridLayoutGroup HomeGrid;
            public Button ReferenceButton;
            public RectTransform StatusBar;
            public Button Back;
            public Button Resize;
            public Button ExitWorld;
            public int GridShown;
            public int UiLayer = 5;
            public readonly List<GameObject> TabContents = new List<GameObject>();
            public readonly List<UnityEvent> MeshHome = new List<UnityEvent>();
        }

        private static readonly int IsOpenHash = Animator.StringToHash("IsOpen");
        private static readonly List<MonoBehaviour> Buffer = new List<MonoBehaviour>();
        private static readonly Vector3[] Corners = new Vector3[4];

        private Parts _t;
        private GameObject _button;
        private Image _buttonIcon;
        private Sprite _iconSprite;
        private GameObject _app;
        private Canvas _appCanvas;
        private RectTransform _frame;
        private float _nextSlow;
        private bool[] _tabSnapshot;
        private bool _backWasActive;
        private bool _enlarged;
        private bool _pressedToLarge; // the size our last Resize press asked for (the rect lies while its tween runs)
        private float _resizeSettleUntil;
        private Transform _desktopPoint;
        private Camera _desktopPointCamera;
        private bool _desktopPointMissingReported;
        private int _placedFrame = -1;
        private int _desktopSnaps, _desktopSkips;
        private bool _shakeFixReported;
        private Transform _lastPlatform;
        private Vector3 _platformPos;
        private Quaternion _platformRot;
        private Transform _bodyRoot;
        private float _nextBodyRead;
        private bool _available = true;
        private bool _closeFailedReported;

        private float _nextSearch;
        private int _searches;
        private double _slowestMs;
        private float _firstSearchAt = -1f;
        private bool _notLinkedReported;
        private bool _renderHooked;
        private bool _ending;
        private float _linkedAt;
        private bool _placeReported, _reorderReported;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            // Statics survive a world re-entry in the same client session: the previous visit's instance is replaced, never trusted.
            Instance = this;
            if (RidingOn == null) RidingOn = DefaultRidingOn;
        }

        private void OnEnable() { if (IsLinked) HookRendering(true); }
        private void OnDisable() { HookRendering(false); }

        private void HookRendering(bool on)
        {
            if (on == _renderHooked) return;
            _renderHooked = on;
            if (on) RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            else RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnDestroy()
        {
            // The world is unloading: leave the client's tablet as it was. No events from here.
            _ending = true;
            HookRendering(false);
            Unlink(true, "world unloading");
            if (_iconSprite != null) Destroy(_iconSprite);
            if (Instance == this) Instance = null;
        }

        /// <summary>Takes everything off the client's tablet now (AcFront.Stop) and stops searching.</summary>
        public void Shutdown()
        {
            _ending = true;
            enabled = false;
            Unlink(true, "world unloading");
        }

        /// <summary>The seated car's root (the riding platform), or null on foot.</summary>
        private static Transform DefaultRidingOn()
        {
            AcCarRig car = AcLocal.SeatCar;
            GameObject root = car != null ? car.Root : null;
            return root != null ? root.transform : null;
        }

        private void Update()
        {
            if (!IsLinked)
            {
                if (_t != null) Unlink(false, "the tablet was destroyed (avatar or mode change)"); // TabletFeature.Clean -> TabletManager.Destroy
                if (Time.unscaledTime >= _nextSearch)
                {
                    if (_firstSearchAt < 0f) _firstSearchAt = Time.unscaledTime;
                    float interval = Time.unscaledTime - _firstSearchAt < 60f ? Mathf.Max(0.1f, SearchInterval) : 5f; // ETS's numbers
                    _nextSearch = Time.unscaledTime + interval;
                    TryLink();
                }

                return;
            }

            bool open = ReadTabletOpen();
            if (open != TabletOpen)
            {
                TabletOpen = open;
                var h = TabletOpenChanged;
                if (h != null) { try { h(open); } catch (Exception e) { AcLog.Warn("Tablet: an open handler failed: " + e.Message); } }
            }

            if (Time.unscaledTime < _nextSlow) return;
            _nextSlow = Time.unscaledTime + 0.25f;

            if (_appCanvas != null)
            {
                Camera eventCamera = _t.HomeCanvas != null ? _t.HomeCanvas.worldCamera : null;
                Camera wanted = eventCamera != null ? eventCamera : Camera.main;
                if (_appCanvas.worldCamera != wanted) _appCanvas.worldCamera = wanted;
            }

            if (_buttonIcon != null && AppIcon != null && (_iconSprite == null || _iconSprite.texture != AppIcon || _buttonIcon.sprite != _iconSprite)) SetIcon(); // a new icon texture (the launcher logo after boot)
            KeepButtonFirst();

            if (AppOpen)
            {
                if (TabsChangedSinceOpen())
                {
                    Close(false, "client switched tab");
                    return;
                }

                SetLayer(_app.transform, _t.UiLayer); // rows added after the build need the ray's layer too
            }
        }

        private void LateUpdate()
        {
            if (!IsLinked) return;
            FollowHomeRect();
            if (!TabletOpen)
            {
                _lastPlatform = null;
                return;
            }

            if (!_t.Desktop && CarryWithVehicle) Carry();
            else if (_t.Desktop) SnapDesktop(false);
        }

        // ------------------------------------------------------------------ discovery

        private void TryLink()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Parts found;
            bool complete;
            string report;
            try
            {
                found = Discover(out report, out complete);
            }
            catch (Exception e)
            {
                found = null;
                complete = false;
                report = "the last search threw " + e.GetType().Name + ": " + e.Message;
            }

            watch.Stop();
            _searches++;
            _slowestMs = Math.Max(_slowestMs, watch.Elapsed.TotalMilliseconds);
            Report = report;

            if (!complete)
            {
                if (!_notLinkedReported && Time.unscaledTime - _firstSearchAt > 20f)
                {
                    _notLinkedReported = true;
                    AcLog.Info("Front: no tablet - hub panel only (not linked after " + (Time.unscaledTime - _firstSearchAt).ToString("0") + " s - " + report + "; still searching every " +
                               (Time.unscaledTime - _firstSearchAt < 60f ? SearchInterval.ToString("0.0") : "5") + " s; " + _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms).");
                }

                return;
            }

            _t = found;
            RemoveLeftovers(_t);
            try
            {
                Inject();
            }
            catch (Exception e)
            {
                AcLog.Warn("Tablet: injection failed (" + e.GetType().Name + ": " + e.Message + ") - " + report);
                Unlink(true, "injection failed");
                _nextSearch = Time.unscaledTime + 5f;
                return;
            }

            AcLog.Info("Front: tablet linked (types found: " + report + "; " + _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms).");
            _linkedAt = Time.unscaledTime;
            _placeReported = false;
            HookRendering(isActiveAndEnabled);
            var lc = LinkChanged;
            if (lc != null) { try { lc(); } catch (Exception e) { AcLog.Warn("Tablet: a link handler failed: " + e.Message); } }
        }

        /// <summary>
        /// Four times a second while linked: the button stays the grid's first child, and once (0.5 s after the link, the grid laid out) its
        /// measured cell is logged against the grid's rect (the Mask's) - the in-client proof that the button is visible.
        /// </summary>
        private void KeepButtonFirst()
        {
            if (_button == null || _t.HomeGrid == null) return;
            Transform button = _button.transform;
            if (button.parent == _t.HomeGrid.transform && button.GetSiblingIndex() != 0)
            {
                button.SetSiblingIndex(0);
                if (!_reorderReported)
                {
                    _reorderReported = true;
                    AcLog.Info("Tablet: the client re-ordered its home grid; the " + AppTitle + " button is first again.");
                }
            }

            if (_placeReported || Time.unscaledTime - _linkedAt < 0.5f || !_t.HomeGrid.gameObject.activeInHierarchy) return;
            _placeReported = true;
            var grid = (RectTransform)_t.HomeGrid.transform;
            int shown = 0, index = -1, buttons = 0;
            for (int i = 0; i < grid.childCount; i++)
            {
                Transform child = grid.GetChild(i);
                buttons += child != button && child.GetComponent<Button>() != null ? 1 : 0;
                if (!child.gameObject.activeSelf) continue;
                if (child == button) index = shown;
                shown++;
            }

            Rect view = grid.rect;
            ((RectTransform)button).GetWorldCorners(Corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = grid.InverseTransformPoint(Corners[i]);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }

            bool inside = minX >= view.xMin - 0.5f && maxX <= view.xMax + 0.5f && minY >= view.yMin - 0.5f && maxY <= view.yMax + 0.5f;
            bool masked = grid.GetComponent<Mask>() != null || grid.GetComponent<RectMask2D>() != null;
            float expectLeft, expectTop;
            HomeGridCell(view.width, view.height, shown, Mathf.Max(0, index), out expectLeft, out expectTop);
            AcLog.Info("Tablet: the " + AppTitle + " button is cell " + (index + 1) + " of " + shown + " shown in the home grid (" + buttons + " client buttons + ours), at " +
                       (minX - view.xMin).ToString("0") + ".." + (maxX - view.xMin).ToString("0") + " x " + (view.yMax - maxY).ToString("0") + ".." + (view.yMax - minY).ToString("0") +
                       " of the " + view.width.ToString("0") + " x " + view.height.ToString("0") + " view (expected " + expectLeft.ToString("0") + ".." + (expectLeft + GridCellWidth).ToString("0") +
                       " x " + expectTop.ToString("0") + ".." + (expectTop + GridCellHeight).ToString("0") + "), " + (masked ? "inside the mask: " : "no mask on the grid, inside its rect: ") + (inside ? "yes." : "NO."));
        }

        /// <summary>The client's GridLayoutGroup arithmetic (Flexible, UpperCenter) for cell <paramref name="index"/> of <paramref name="shown"/>.</summary>
        public static bool HomeGridCell(float viewWidth, float viewHeight, int shown, int index, out float left, out float top)
        {
            int columns = Math.Max(1, (int)Math.Floor((viewWidth + 0.001f) / GridCellWidth));
            int children = Math.Max(1, shown);
            int usedColumns = Math.Max(1, Math.Min(columns, children));
            float startX = (viewWidth - usedColumns * GridCellWidth) * 0.5f;
            left = startX + (index % columns) * GridCellWidth;
            top = (index / columns) * GridCellHeight;
            return index >= 0 && index < children && left >= -0.01f && left + GridCellWidth <= viewWidth + 0.01f && top + GridCellHeight <= viewHeight + 0.01f;
        }

        /// <summary>The tablet's own Exit World (the client asks the player to confirm in its pop-up). False when there is no such button.</summary>
        public bool ExitWorld()
        {
            if (!CanExitWorld) return false;
            Close(true, "the player chose to leave");
            AcLog.Info("Tablet: Exit World pressed for the player - the client asks them to confirm.");
            _t.ExitWorld.onClick.Invoke();
            return true;
        }

        private static Parts Discover(out string report, out bool complete)
        {
            complete = false;
            Scene master = SceneManager.GetSceneByName("MasterScene"); // WorldsHelper.MASTER_SCENE_NAME
            bool masterLoaded = master.IsValid() && master.isLoaded;
            var roots = new List<GameObject>();
            Transform root = null;
            bool rootByType = false;
            Component control = null;
            if (masterLoaded)
            {
                master.GetRootGameObjects(roots);
                root = SearchRoots(roots, out rootByType, out control);
            }

            int others = 0;
            for (int i = 0; root == null && i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene == master) continue;
                others++;
                scene.GetRootGameObjects(roots);
                root = SearchRoots(roots, out rootByType, out control);
            }

            if (root == null)
            {
                report = "no transform with a TabletControl component, and none named 'Tablet.XR(...' or 'Tablet.NonXR(...', in MasterScene (" +
                         (masterLoaded ? "loaded" : "NOT loaded") + ") or " + others + " other loaded scene(s); 'Simulator' objects (remote tablets) and the world's 'Ac ...' subtrees skipped";
                return null;
            }

            var t = new Parts { Root = root, Control = control };
            if (control != null) t.Deactivate = control.GetType().GetMethod("DeActivate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

            Transform tabs = FindTyped(root, "TabsContainer");
            string tabsHow = tabs != null ? "type" : null;
            if (tabs == null) { tabs = FindNamed(root, "Tablet.XR.Tabs", 3); tabsHow = tabs != null ? "name" : "-"; }
            Transform home = FindTyped(root, "HomeTab");
            string homeHow = home != null ? "type" : null;
            if (home == null) { home = FindNamed(root, "Tablet.XR.HomeTab", 4); homeHow = home != null ? "name" : "-"; }
            Transform bar = FindTyped(root, "TabletStatusBarView");
            string barHow = bar != null ? "type" : null;
            if (bar == null) { bar = FindNamed(root, "Tablet.XR.StatusBar", 3); barHow = bar != null ? "name" : "-"; }

            t.Tabs = tabs;
            t.Home = home as RectTransform;
            t.StatusBar = bar as RectTransform;
            t.Canvases = tabs != null ? tabs.parent : FindNamed(root, "Canvases", 1);
            t.CanvasesGroup = t.Canvases != null ? t.Canvases.GetComponent<CanvasGroup>() : null;
            Transform grabCollider = FindNamed(root, "GrabCollider", 1);
            t.GrabCollider = grabCollider != null ? grabCollider.gameObject : null;
            t.Grab = root.GetComponent<XRGrabInteractable>();

            Canvas homeCanvas = home != null ? home.GetComponent<Canvas>() : null;
            GridLayoutGroup grid = home != null ? home.GetComponentInChildren<GridLayoutGroup>(true) : null;
            t.HomeCanvas = homeCanvas;
            t.HomeGrid = grid;
            Button reference = null;
            int uiLayer = 5, gridButtons = 0, gridShown = 0;
            if (grid != null)
            {
                foreach (Transform child in grid.transform)
                {
                    var b = child.GetComponent<Button>();
                    if (b == null) continue;
                    gridButtons++;
                    bool foreign = IsOtherDreamportsClone(child.name);
                    gridShown += child.gameObject.activeSelf && !foreign ? 1 : 0;
                    // Never an earlier visit's clone or another Dreamport's: a copy of a copy carries no client look at all.
                    if (reference == null && !foreign && child.GetComponentInChildren<TMP_Text>(true) != null)
                    {
                        reference = b;
                        var graphic = child.GetComponent<Graphic>();
                        uiLayer = graphic != null ? graphic.gameObject.layer : child.gameObject.layer;
                    }
                }
            }

            t.ReferenceButton = reference;
            t.UiLayer = uiLayer;
            t.GridShown = gridShown;
            bool back = false, resize = false;
            if (bar != null)
            {
                foreach (Button b in bar.GetComponentsInChildren<Button>(true))
                {
                    bool isBack = b.name.IndexOf("Back", StringComparison.Ordinal) >= 0;
                    bool isResize = b.name.IndexOf("Resize", StringComparison.Ordinal) >= 0;
                    back |= isBack;
                    resize |= isResize;
                    if (isBack && t.Back == null) t.Back = b;
                    if (isResize && t.Resize == null) t.Resize = b;
                    if (t.ExitWorld == null && b.name.IndexOf("Exit_World", StringComparison.Ordinal) >= 0) t.ExitWorld = b;
                }
            }

            int tabCount = 0, contentViews = 0;
            if (tabs != null)
            {
                foreach (Transform tab in tabs)
                {
                    tabCount++;
                    Transform content = tab.Find("ContentView");
                    contentViews += content != null ? 1 : 0;
                    t.TabContents.Add(content != null ? content.gameObject : null);
                }
            }

            var animator = root.GetComponent<Animator>();
            bool isOpenParameter = false;
            if (animator != null && animator.isActiveAndEnabled)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                    isOpenParameter |= p.type == AnimatorControllerParameterType.Bool && p.name == "IsOpen";
            }

            t.Animator = animator;
            t.AnimatorHasIsOpen = isOpenParameter;
            bool meshHome = FindMeshHome(root, t.MeshHome);
            bool grab = HasType(root, "XRGrabInteractable");
            bool desktop = root.name.StartsWith("Tablet.NonXR", StringComparison.Ordinal) || !AcPlayer.XrPresent;
            t.Desktop = desktop;
            var homeRect = home as RectTransform;
            string size = (homeRect == null ? "home size unknown" :
                          "home " + homeRect.rect.width.ToString("0") + "x" + homeRect.rect.height.ToString("0") + (homeRect.rect.width > LargeAbove ? " (Large)" : " (Small)")) +
                          ", " + gridShown + " of the grid's buttons shown";
            string missing = (tabs == null ? " Tabs" : "") + (homeCanvas == null ? " HomeCanvas" : "") + (grid == null ? " HomeGrid" : "") +
                             (reference == null ? " ReferenceButton" : "") + (!back ? " Back" : "") + (!resize ? " Resize" : "") +
                             (animator == null ? " Animator" : isOpenParameter ? "" : " IsOpen") + (t.Deactivate == null ? " DeActivate" : "");
            report = "'" + root.name + "' under '" + (root.parent != null ? root.parent.name : "(scene root)") + "' in scene '" + root.gameObject.scene.name + "', " +
                     (desktop ? "desktop" : "XR") + ", root by " + (rootByType ? "type TabletControl" : "name") +
                     "; TabsContainer by " + tabsHow + ", HomeTab by " + homeHow + ", TabletStatusBarView by " + barHow +
                     "; " + tabCount + " tabs (" + contentViews + " with a ContentView), home grid " + gridButtons + " buttons, " + size +
                     ", UI layer " + uiLayer + ", canvas layer " + (homeCanvas != null ? homeCanvas.gameObject.layer.ToString() : "-") +
                     ", raycaster " + Raycaster(homeCanvas) + ", mesh Home " + (meshHome ? "yes" : "no") + ", grab " + (grab ? "yes" : "no") +
                     ", Exit World " + (t.ExitWorld != null ? "yes" : "no") + (missing.Length > 0 ? "; MISSING:" + missing : "; all parts found");
            complete = tabs != null && homeCanvas != null && grid != null && reference != null;
            return t;
        }

        /// <summary>A home-grid button an earlier visit of this world, or another Dreamport's tablet app, cloned (their names): never the model for ours.</summary>
        private static bool IsOtherDreamportsClone(string name)
        {
            return name.StartsWith(Owned, StringComparison.Ordinal) || name.StartsWith("ETS.Tablet.", StringComparison.Ordinal) || name.StartsWith("WoW.Tablet.", StringComparison.Ordinal) ||
                   name.StartsWith("BO2.Tablet.", StringComparison.Ordinal) || name.StartsWith("Halo.Tablet.", StringComparison.Ordinal);
        }

        /// <summary>The first transform carrying a TabletControl, else the first named "Tablet.XR(" / "Tablet.NonXR(". Remote tablets ("Simulator") and
        /// the world's own "Ac ..." subtrees are not entered.</summary>
        private static Transform SearchRoots(List<GameObject> roots, out bool byType, out Component control)
        {
            byType = false;
            control = null;
            var stack = new Stack<Transform>();
            foreach (GameObject go in roots) if (go != null) stack.Push(go.transform);
            Transform named = null;
            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                string name = t.name;
                if (name.StartsWith("Ac ", StringComparison.Ordinal) || name.IndexOf("Simulator", StringComparison.Ordinal) >= 0) continue;
                MonoBehaviour tc = FindOn(t, "TabletControl");
                if (tc != null)
                {
                    byType = true;
                    control = tc;
                    return t;
                }

                if (named == null && (name.StartsWith("Tablet.XR(", StringComparison.Ordinal) || name.StartsWith("Tablet.NonXR(", StringComparison.Ordinal))) named = t;
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }

            return named;
        }

        private static MonoBehaviour FindOn(Transform t, string typeName)
        {
            t.GetComponents(Buffer);
            MonoBehaviour found = null;
            foreach (MonoBehaviour mb in Buffer) if (mb != null && mb.GetType().Name == typeName) { found = mb; break; }
            Buffer.Clear();
            return found;
        }

        private static bool HasType(Transform t, string typeName) { return FindOn(t, typeName) != null; }

        private static Transform FindTyped(Transform root, string typeName)
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name == typeName) return mb.transform;
            return null;
        }

        private static Transform FindNamed(Transform root, string name, int maxDepth)
        {
            if (maxDepth < 0) return null;
            for (int i = 0; i < root.childCount; i++) if (root.GetChild(i).name == name) return root.GetChild(i);
            for (int i = 0; i < root.childCount; i++)
            {
                Transform deeper = FindNamed(root.GetChild(i), name, maxDepth - 1);
                if (deeper != null) return deeper;
            }

            return null;
        }

        /// <summary>The tablet's mesh Home button fires XRInteractableButton.ButtonPressed on a poke (a public UnityEvent field, by reflection; optional).</summary>
        private static bool FindMeshHome(Transform root, List<UnityEvent> into)
        {
            Transform meshHome = FindNamed(root, "HomeButton", 8);
            if (meshHome == null) return false;
            foreach (MonoBehaviour mb in meshHome.GetComponents<MonoBehaviour>())
            {
                FieldInfo field = mb != null ? mb.GetType().GetField("ButtonPressed", BindingFlags.Public | BindingFlags.Instance) : null;
                var pressed = field != null ? field.GetValue(mb) as UnityEvent : null;
                if (pressed != null) into.Add(pressed);
            }

            return into.Count > 0;
        }

        private static string Raycaster(Canvas canvas)
        {
            if (canvas == null) return "-";
            string found = "";
            foreach (MonoBehaviour mb in canvas.GetComponents<MonoBehaviour>())
            {
                string type = mb != null ? mb.GetType().Name : "";
                if (type.EndsWith("Raycaster", StringComparison.Ordinal)) found += (found.Length > 0 ? "+" : "") + type;
            }

            return found.Length > 0 ? found : "none";
        }

        // ------------------------------------------------------------------ injection

        private static void RemoveLeftovers(Parts t)
        {
            var doomed = new List<GameObject>();
            foreach (Transform child in t.HomeGrid.transform) if (child.name.StartsWith(Owned, StringComparison.Ordinal)) doomed.Add(child.gameObject);
            if (t.Canvases != null) foreach (Transform child in t.Canvases) if (child.name.StartsWith(Owned, StringComparison.Ordinal)) doomed.Add(child.gameObject);
            foreach (GameObject go in doomed)
            {
                go.SetActive(false); // out of the grid at once (Destroy is deferred)
                Destroy(go);
            }

            bool tabsShown = false;
            if (t.Tabs != null && !t.Tabs.gameObject.activeSelf)
            {
                t.Tabs.gameObject.SetActive(true);
                tabsShown = true;
            }

            if (doomed.Count > 0 || tabsShown)
                AcLog.Info("Tablet: removed " + doomed.Count + " objects left by an earlier visit" + (tabsShown ? "; its hidden Tabs container shown again" : "") + ".");
        }

        private void Inject()
        {
            var staging = new GameObject(Owned + "Staging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            try
            {
                _button = Instantiate(_t.ReferenceButton.gameObject, staging.transform, false);
                _button.name = Owned + "Button";
                StripToUnityUi(_button);
                _button.transform.SetParent(_t.HomeGrid.transform, false);
                _button.transform.SetSiblingIndex(0);
            }
            finally
            {
                Destroy(staging);
            }

            var button = _button.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // drops the persistent call into the destroyed client script
            button.onClick.AddListener(OnAppButton);
            button.interactable = true;
            foreach (TMP_Text caption in _button.GetComponentsInChildren<TMP_Text>(true)) caption.text = AppTitle;
            Transform iconTransform = _button.transform.Find("Icon");
            _buttonIcon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            SetIcon();
            _button.SetActive(_available);

            _app = new GameObject(Owned + "App", typeof(RectTransform));
            _app.SetActive(false);
            _app.layer = _t.HomeCanvas.gameObject.layer;
            _app.transform.SetParent(_t.Canvases != null ? _t.Canvases : _t.Tabs.parent, false);
            _appCanvas = _app.AddComponent<Canvas>();
            _appCanvas.renderMode = RenderMode.WorldSpace;
            _appCanvas.sortingLayerID = _t.HomeCanvas.sortingLayerID;
            _appCanvas.sortingOrder = _t.HomeCanvas.sortingOrder;
            _appCanvas.additionalShaderChannels = _t.HomeCanvas.additionalShaderChannels;
            _appCanvas.worldCamera = _t.HomeCanvas.worldCamera;
            bool tracked = _t.HomeCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null;
            bool mouse = _t.HomeCanvas.GetComponent<GraphicRaycaster>() != null;
            if (tracked || !mouse) _app.AddComponent<TrackedDeviceGraphicRaycaster>();
            if (mouse || !tracked) _app.AddComponent<GraphicRaycaster>();
            _app.AddComponent<RectMask2D>();
            var backdrop = _app.AddComponent<Image>();
            backdrop.color = AcUi.Page; // opaque: a miss on the app never falls through to the hidden tabs
            backdrop.raycastTarget = true;
            _frame = new GameObject(Owned + "Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            _frame.SetParent(_app.transform, false);
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            _frame.gameObject.layer = _t.UiLayer;
            FollowHomeRect();
            foreach (UnityEvent pressed in _t.MeshHome) pressed.AddListener(OnMeshHome);
        }

        /// <summary>Puts the current <see cref="AppIcon"/> on the home button now (call it after giving AppIcon a new texture and BEFORE destroying
        /// the old one, so the button never shows a destroyed texture).</summary>
        public void RefreshIcon() { SetIcon(); }

        private void SetIcon()
        {
            if (_buttonIcon == null) return;
            if (AppIcon != null)
            {
                if (_iconSprite == null || _iconSprite.texture != AppIcon)
                {
                    Sprite old = _iconSprite;
                    _iconSprite = Sprite.Create(AppIcon, new Rect(0f, 0f, AppIcon.width, AppIcon.height), new Vector2(0.5f, 0.5f), 100f);
                    _iconSprite.name = "AC.Tablet.IconSprite";
                    if (old != null) { _buttonIcon.sprite = _iconSprite; Destroy(old); }
                }

                _buttonIcon.sprite = _iconSprite;
                _buttonIcon.preserveAspect = true;
                _buttonIcon.enabled = true;
            }
            else
            {
                // Not the client's own picture under our caption: nothing until ours is painted.
                _buttonIcon.sprite = null;
                _buttonIcon.enabled = false;
            }
        }

        /// <summary>Every MonoBehaviour outside Unity UI and TextMeshPro goes, last-added first, in up to four passes (RequireComponent chains).</summary>
        private static void StripToUnityUi(GameObject root)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                int foreign = 0;
                MonoBehaviour[] all = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    MonoBehaviour mb = all[i];
                    if (mb == null) continue;
                    string assembly = mb.GetType().Assembly.GetName().Name;
                    if (assembly == "UnityEngine.UI" || assembly == "Unity.TextMeshPro") continue;
                    foreign++;
                    DestroyImmediate(mb);
                }

                if (foreign == 0) return;
            }
        }

        private void Unlink(bool restore, string why)
        {
            if (AppOpen) Close(restore, why);
            if (_t != null) foreach (UnityEvent pressed in _t.MeshHome) pressed.RemoveListener(OnMeshHome);
            if (_button != null) { _button.SetActive(false); Destroy(_button); }
            if (_app != null) Destroy(_app);
            bool was = _t != null;
            _button = null;
            _buttonIcon = null;
            _app = null;
            _appCanvas = null;
            _frame = null;
            _t = null;
            TabletOpen = false;
            _lastPlatform = null;
            HookRendering(false);
            if (was)
            {
                AcLog.Info("Tablet: unlinked (" + why + ")" + (_desktopSnaps + _desktopSkips > 0 ? "; desktop re-placed " + _desktopSnaps + " frames, left " + _desktopSkips : "") + ".");
                if (!_ending)
                {
                    var lc = LinkChanged;
                    if (lc != null) { try { lc(); } catch (Exception e) { AcLog.Warn("Tablet: a link handler failed: " + e.Message); } }
                }
            }
        }

        // ------------------------------------------------------------------ showing and hiding

        private void OnAppButton() { Open("app button"); }
        private void OnBack() { Close(true, "Back"); }
        private void OnMeshHome() { Close(true, "mesh Home"); }

        public void OpenApp() { Open("world"); }
        public void CloseApp() { Close(true, "world"); }

        /// <summary>
        /// Closes the app, then the client's tablet itself (TabletControl.DeActivate, a public method of the client's component found by type
        /// name). Used when a race starts (countdown) and for RESUME. True when the tablet was open and its close was called.
        /// </summary>
        public bool CloseTablet(string why)
        {
            if (!IsLinked) return false;
            if (AppOpen) Close(true, why);
            if (!ReadTabletOpen()) return false;
            try
            {
                if (_t.Control == null || _t.Deactivate == null)
                {
                    if (!_closeFailedReported)
                    {
                        _closeFailedReported = true;
                        AcLog.Info("Tablet: the client's tablet has no DeActivate to call - it stays open on its home screen (" + why + ").");
                    }

                    return false;
                }

                _t.Deactivate.Invoke(_t.Control, null);
                AcLog.Info("Tablet: the client's tablet closed (" + why + ").");
                return true;
            }
            catch (Exception e)
            {
                if (!_closeFailedReported)
                {
                    _closeFailedReported = true;
                    AcLog.Warn("Tablet: closing the client's tablet failed (" + e.GetType().Name + ": " + e.Message + ") - it stays open on its home screen.");
                }

                return false;
            }
        }

        private void Open(string why)
        {
            if (!IsLinked || AppOpen || _app == null || !_available) return;
            _tabSnapshot = SnapshotTabs();
            _t.Tabs.gameObject.SetActive(false);
            _app.SetActive(true);
            SetLayer(_app.transform, _t.UiLayer);
            _app.layer = _t.HomeCanvas.gameObject.layer;
            if (_t.Back != null)
            {
                _backWasActive = _t.Back.gameObject.activeSelf;
                _t.Back.gameObject.SetActive(true); // TabletStatusBarView hides it on HOME
                _t.Back.onClick.AddListener(OnBack);
            }

            float width = _t.Home != null ? _t.Home.rect.width : 0f;
            // while our own press still tweens (reopened within 0.35 s of a close) the rect lies: trust what that press asked for
            bool small = Time.unscaledTime < _resizeSettleUntil ? !_pressedToLarge : width <= LargeAbove;
            string size = small ? "Small" : "Large";
            if (ForceLarge && _t.Resize != null && small)
            {
                _t.Resize.onClick.Invoke(); // TabletResize toggles Small <-> Large
                _pressedToLarge = true;
                _enlarged = true;
                _resizeSettleUntil = Time.unscaledTime + 0.35f; // its DOTween runs 0.2 s
                size = "Small->Large";
            }

            AppOpen = true;
            AcLog.Info("Tablet: app opened (" + why + ", size " + size + ").");
            AcLog.InfoOnce("Front: app opened");
            var h = AppShown;
            if (h != null) { try { h(true); } catch (Exception e) { AcLog.Warn("Tablet: an app handler failed: " + e.Message); } }
        }

        private void Close(bool restoreSize, string why)
        {
            if (!AppOpen) return;
            AppOpen = false;
            if (_app != null) _app.SetActive(false);
            if (_t != null && _t.Tabs != null) _t.Tabs.gameObject.SetActive(true);
            if (_t != null && _t.Back != null)
            {
                _t.Back.onClick.RemoveListener(OnBack);
                _t.Back.gameObject.SetActive(_backWasActive);
            }

            bool shrunk = false;
            if (restoreSize && _enlarged && _t != null && _t.Resize != null && IsLargeNow())
            {
                _t.Resize.onClick.Invoke();
                _pressedToLarge = false;
                _resizeSettleUntil = Time.unscaledTime + 0.35f;
                shrunk = true;
            }

            _enlarged = false;
            AcLog.Info("Tablet: app closed (" + why + (shrunk ? ", size Large->Small" : "") + ").");
            if (!_ending)
            {
                var h = AppShown;
                if (h != null) { try { h(false); } catch (Exception e) { AcLog.Warn("Tablet: an app handler failed: " + e.Message); } }
            }
        }

        private bool IsLargeNow()
        {
            if (Time.unscaledTime < _resizeSettleUntil) return _pressedToLarge; // our own press is still tweening
            RectTransform measured = _t.Home != null ? _t.Home : _t.StatusBar;
            return measured != null && measured.rect.width > LargeAbove;
        }

        private bool ReadTabletOpen()
        {
            if (_t.Animator != null && _t.AnimatorHasIsOpen) return _t.Animator.GetBool(IsOpenHash); // TabletTransitionControl
            if (_t.CanvasesGroup != null) return _t.CanvasesGroup.alpha > 0.5f && _t.CanvasesGroup.blocksRaycasts; // Armature_CloseFrame fades it
            return _t.GrabCollider == null || _t.GrabCollider.activeSelf;
        }

        private bool[] SnapshotTabs()
        {
            var shot = new bool[_t.TabContents.Count];
            for (int i = 0; i < shot.Length; i++)
            {
                GameObject content = _t.TabContents[i];
                shot[i] = content != null && content.activeSelf;
            }

            return shot;
        }

        private bool TabsChangedSinceOpen()
        {
            if (_tabSnapshot == null || _tabSnapshot.Length != _t.TabContents.Count) return false;
            for (int i = 0; i < _tabSnapshot.Length; i++)
            {
                GameObject content = _t.TabContents[i];
                if ((content != null && content.activeSelf) != _tabSnapshot[i]) return true;
            }

            return false;
        }

        private void FollowHomeRect()
        {
            if (_app == null || _t.Home == null) return;
            var rect = (RectTransform)_app.transform;
            // A home canvas caught mid close-tween (TabBase.ScaleView to 0) is not copied.
            if (rect.parent == null || _t.Home.lossyScale.x < rect.parent.lossyScale.x * 0.5f) return;
            Vector2 size = _t.Home.rect.size;
            rect.localScale = Vector3.one;
            rect.pivot = _t.Home.pivot;
            rect.sizeDelta = size;
            rect.SetPositionAndRotation(_t.Home.position, _t.Home.rotation);
            if (_frame != null)
            {
                float fit = Mathf.Min(size.x / FrameWidth, size.y / FrameHeight); // Large 1920 x 1080: 1.2; Small 960 x 1080: 0.6
                _frame.localScale = new Vector3(fit, fit, 1f);
            }
        }

        /// <summary>Every graphic of the app on the client's UI layer (the XR ray tests each graphic's layer); the canvas root keeps the HomeTab canvas's.</summary>
        public void ApplyLayers()
        {
            if (IsLinked && _app != null) SetLayer(_app.transform, _t.UiLayer);
        }

        private static void SetLayer(Transform t, int layer)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child.gameObject.layer != layer) child.gameObject.layer = layer;
                SetLayer(child, layer);
            }
        }

        // ------------------------------------------------------------------ placement while riding

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_placedFrame == Time.frameCount || camera == null || camera.targetTexture != null || camera.cameraType != CameraType.Game) return;
            _placedFrame = Time.frameCount;
            SnapDesktop(true);
        }

        /// <summary>How far the desktop tablet may lag its point and still be "one frame's travel" behind: 1.5 m (ETS) or 1.5 frames at the car's
        /// speed, whichever is more (300 km/h at 30 fps = 2.8 m per frame). // ours</summary>
        public static float DesktopMaxLag(float speedKmh, float dt)
        {
            float frame = Mathf.Max(0f, speedKmh) / 3.6f * Mathf.Clamp(dt, 0.005f, 0.2f);
            return Mathf.Max(1.5f, frame * 1.5f + 0.5f);
        }

        private void SnapDesktop(bool atRender)
        {
            if (!FixDesktopLag || !IsLinked || !_t.Desktop || !TabletOpen) return;
            Transform point = DesktopPoint();
            if (point == null) return;
            float lag = Vector3.Distance(_t.Root.position, point.position);
            float maxLag = DesktopMaxLag(AcLocal.Seated ? AcLocal.Telemetry.SpeedKmh : 0f, Time.unscaledDeltaTime);
            if (lag > maxLag)
            {
                _desktopSkips++; // the client has put its tablet somewhere else on purpose
                return;
            }

            if (atRender && lag < 0.0005f) return; // LateUpdate already put it there
            if (!atRender && !_shakeFixReported)
            {
                _shakeFixReported = true;
                AcLog.Info("Tablet: desktop tablet placed in LateUpdate, before the canvases are batched (" + lag.ToString("0.000") + " m from the camera's TabletPoint).");
            }

            _desktopSnaps++;
            _t.Root.SetPositionAndRotation(point.position, point.rotation);
        }

        private Transform DesktopPoint()
        {
            Camera camera = Camera.main;
            if (camera != _desktopPointCamera)
            {
                _desktopPointCamera = camera;
                // CameraSystem/Camera/TabletPoint, local (-0.224, 0, 0.724) (CameraSystem.prefab)
                _desktopPoint = camera != null ? camera.transform.Find("TabletPoint") : null;
                if (_desktopPoint == null && camera != null && !_desktopPointMissingReported)
                {
                    _desktopPointMissingReported = true;
                    AcLog.Info("Tablet: no TabletPoint under the main camera '" + camera.name + "' - the desktop tablet is left where the client puts it.");
                }
            }

            return _desktopPoint;
        }

        private void Carry()
        {
            Transform platform = null;
            try { platform = RidingOn != null ? RidingOn() : null; }
            catch (Exception) { platform = null; }
            if (Time.unscaledTime >= _nextBodyRead)
            {
                _nextBodyRead = Time.unscaledTime + 0.25f;
                try { _bodyRoot = AcPlayer.Root; }
                catch (Exception) { _bodyRoot = null; }
            }

            bool follows = _bodyRoot != null && _t.Root.IsChildOf(_bodyRoot); // "tablet follows player": the rig carries it already
            bool held = _t.Grab != null && _t.Grab.isSelected;
            if (platform == null || follows || held)
            {
                _lastPlatform = null;
                return;
            }

            if (_lastPlatform == platform)
            {
                Quaternion turn = platform.rotation * Quaternion.Inverse(_platformRot);
                Vector3 offset = _t.Root.position - _platformPos;
                _t.Root.SetPositionAndRotation(platform.position + turn * offset, turn * _t.Root.rotation);
            }

            _lastPlatform = platform;
            _platformPos = platform.position;
            _platformRot = platform.rotation;
        }
    }
}
