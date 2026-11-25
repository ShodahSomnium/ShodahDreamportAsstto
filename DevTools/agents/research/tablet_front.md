# tablet_front - Somnium tablet app, hub / front, loading card, settings UI (research spec for the Assetto Corsa Dreamport)

Author: research agent "tablet_front", 2026-10-31. Read-only research; nothing in any project was changed except this file.
Scratch probe: `C:/Users/derii/AppData/Local/Temp/claude/acscratch/tablet_front/probe/Program.cs` (dotnet 8).

Legend: **[V-code]** verified by reading the code / client source at the cited line. **[V-run]** verified by running code against the real
files. **[V-client]** proven in the real Somnium client by an earlier Dreamport's Player.log or user test (cited). **[B]** believed, not verified.

Paths used below:
- `AC/` = `D:\Dreamport\Assetta Corsa\Assets\#User\SomniumHalo\` (the copy; still Halo code, identical to the Halo project's Game/Front - `diff -rq` shows no difference)
- `ETS/` = `D:\Dreamport\Euro Truck Simulator\Euro Truck Sim\Assets\#User\SomniumEuroTruck\`
- `BO2/` = `D:\Dreamport\BO2\Black Ops 2\Assets\#User\SomniumBO2\`
- `WOW/` = `D:\Dreamport\WoW\World Of Warcraft\Assets\#User\SomniumWoW\`
- `CL/` = `E:\Somnium Space\Main Build\Assets\_SomniumSpace\` (client source)
- `GAME/` = `E:\SteamLibrary\steamapps\common\assettocorsa\`

---

## 0. TL;DR / recommendation

1. **There is no tablet API.** `SomniumSpace.Bridge.xml` and `SomniumSpace.Bridge.dll` in `D:\Dreamport\Assetta Corsa\Assets\Plugins\SomniumSpace\Bridge\`
   contain the string "tablet" 0 times **[V-run: grep -c]**. Every Dreamport borrows the client's own tablet by **searching the scene for the
   client's component TYPE names** (`TabletControl`, `TabsContainer`, `HomeTab`, `TabletStatusBarView` - these VRTablet types are NOT renamed in the
   shipped client; ETS's Player.log 2026-09-23 found all four "by type" **[V-client]**), cloning ONE home-grid button, and putting its own world-space
   uGUI canvas next to the client's tab canvases.
2. **Port `ETS/EtsTabletLink.cs` (1538 lines), not Halo's/BO2's `HaloTabletLink`/`Bo2TabletLink` (1303 lines, identical to each other apart from
   names).** ETS's link is a superset that AC needs: it carries the free-standing VR tablet with the moving vehicle (`Carry`, line 1498), snaps the
   desktop tablet to the camera's `TabletPoint` before canvases are batched (`SnapDesktop`, 1439; fixes the "tablet shake while driving" the ETS user
   reported 2026-10-05), and can press the tablet's own Exit World (`ExitWorld`, 596). Strip its ETS-only parts (`EtsFeatures.NewFlow`, `LogOnly`
   probe mode, `EtsGameUi.Note`, logo icon painting from archives).
3. **Page framework: ETS's `EtsTabletPage` pattern (abstract page, lazy build per host, `Shown/Hidden/Tick` at 4 Hz, `Available/WhyNot`, toast line)
   + Halo's simple uGUI helpers (`HaloUi.Place/Text/Box/Picture`, `HaloButton`, top-left y-down coordinates) + Halo's settings registry
   (`HaloSettingsUi`) + WoW's VR-safe drag slider (`WowSlider`).** Do NOT port ETS's `EtsGameUi`/`EtsTabletApp` drawing code: it rebuilds ETS's own
   `.sii`/`.pml` UI layout files - AC has no equivalent in-game layout format (its launcher is HTML/CSS/JS in `GAME/launcher/themes/default`).
4. **One builder per page, two hosts** (Halo pattern, `HaloFront.AddPage`): the same pages draw into the tablet frame and into a hub panel (the
   fallback when no tablet links, and the "big screen" in the hub). BO2 found the room panel must stay even when the tablet links
   (`Bo2TabletApp.RoomMenuAlways = true`).
5. **Tablet buttons while driving**: port `HaloTabletGuard` (AC/Game/Vr/HaloTabletGuard.cs) and call it like `HaloVehicleDirector.Tablet` does
   (seated + 1 s around get in/out), AND port ETS's "hold the car while the tablet is up" (`EtsTabletApp.HoldInputWhileVisible`, 1635;
   `EtsTabletLayout.HoldTruck`, 509) because in VR the trigger that presses tablet buttons is the throttle. Desktop Tab is NOT covered by the guard
   (see 5.3).
6. **Fonts**: AC ships TrueType fonts in `GAME/content/fonts/` (segoeui*.ttf, Orbitron-Medium.ttf, consola*.ttf) **[V-run: find]**; the launcher's CSS
   uses "Segoe UI Light"/"Segoe UI" **[V-run: grep ui.css]**. Build TMP fonts at run time with
   `TMP_FontAsset.CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode, int atlasWidth, int atlasHeight)`
   (exists in this project's ugui package, `Library/PackageCache/com.unity.ugui@1890c14c96ff/Runtime/TMP/TMP_FontAsset.cs:511`, dynamic atlas re-reads
   the file via `m_SourceFontFilePath`, line 1188) - with the shader caveat in section 8.

---

## 1. The client's tablet (facts measured against the client source)

| Fact | Where | Status |
|---|---|---|
| The client instantiates `Tablet.XR` / `Tablet.NonXR` prefab into `WorldsHelper.SpawnParameters` (scene "MasterScene") for the LOCAL player only | `CL/VRTablet/Tablet/TabletManager.cs:55-90` (`LocalPlayerAdded`, `Instantiate(_tabletXRPrefab, WorldsHelper.SpawnParameters)`) | V-code |
| Root object name `Tablet.XR(Clone)` under `XR.Body` in MasterScene; 14 tabs, home grid 19 buttons, home 960x1080 (Small), UI layer 5, **canvas layer 9**, raycaster `TrackedDeviceGraphicRaycaster` | ETS Player.log 2026-09-23 quoted in `ETS/EtsTabletLink.cs:36-41` | V-client |
| `TabletControl` (public `Activate()`, `DeActivate()`, `IsOpened`, `SwitchActivate()`) | `CL/VRTablet/Tablet/TabletControl.cs:55,111,183,212,220` | V-code |
| HomeTab size Small = 960x1080, Large = 1920x1080 (TabletResize configs) | `CL/VRTablet/Tablet/Prefabs/Tablet.XR.HomeTab.prefab:422-434` | V-code |
| Object names used as fallbacks: `Canvases` (Tablet.XR.prefab:510), `GrabCollider` (:1018), `HomeButton` (Tablet.XR.Model.prefab:2064), `Tablet.XR.StatusBar.Exit_World` (ETS cites Tablet.XR.StatusBar.prefab:775) | client prefabs | V-code |
| Mesh Home button fires a public `UnityEvent ButtonPressed` field on `XRInteractableButton` | read by reflection in `AC/Game/Front/HaloTabletLink.cs:733-754` | V-code |
| XR open = **tap (<0.4 s) of the secondary button**: actions `RightHandTablet` (`<XRController>{RightHand}/{SecondaryButton}` = B, Vive/WMR menu) and `LeftHandTablet` (`{LeftHand}/{SecondaryButton}` = Y), `interactions: "Tap(duration=0.4)"` | `CL/Inputs/VR/XRI Player Interactions.inputactions:10,20,228-262,1022-1044` | V-code |
| XR feature holding them: `XRTabletFeature` private fields `_rightHandTabletActivate`, `_leftHandTabletActivate` (InputActionReference) | `CL/Player/Scripts/Components/Features/XR/XRTabletFeature.cs:8-14` | V-code |
| Desktop open = action `Tablet`: **Keyboard Tab**, Gamepad select, Gamepad start; held by `NonXRTabletFeature._tabletActionRef`; while open it disables an interaction type and camera moving (`SetCameraMovingActive(this, false)`) | `CL/Inputs/2D/2D Input Actions.inputactions:50, ~485-515`; `CL/Player/.../NonXR/NonXRTabletFeature.cs:9-50` | V-code |
| The client CLOSES the tablet itself when the player enters state `AFK` or `VEHICLE` (client vehicles only - a Dreamport seat is not a client vehicle) | `CL/Player/.../Common/TabletFeature.cs` `OnEnterToState` | V-code |
| Avatar / mode change destroys and re-makes the tablet (`TabletFeature.Clean -> TabletManager.Destroy`) - the link must re-search | `TabletFeature.cs` `Clean()` | V-code |
| The tablet OUTLIVES the world (lives in MasterScene): everything added must be removed in `OnDestroy`, and leftovers of an earlier visit swept on link | `HaloTabletLink.cs:170-188, 782-818` | V-code |
| Desktop tablet point: `Camera.main/TabletPoint`, local (-0.224, 0, 0.724) (CameraSystem.prefab) | `ETS/EtsTabletLink.cs:1478-1496` | V-code (ETS) |
| Remote players' tablets are "Simulator" objects (skip them) | `HaloTabletLink.cs:636-640` | V-code |
| The tablet's layer 9 is drawn after post-processing (not tone-mapped) | `ETS/README.md:2230` | V-client (ETS) |

Proven in the client: ETS (Player.log 2026-09-23 link line; 2026-10-05 desktop shake fix after a client run), BO2 (iterations 7-28 user tests:
menu on the tablet, scoreboard / class / team / map in a match), Halo (lab `front-tablet` PASSED with stubs; client run of the tablet not
documented in Halo's F/NOTES.md "NOT verified" list).

---

## 2. How a Dreamport registers an app on the tablet home screen (code walk-through)

All line numbers in `AC/Game/Front/HaloTabletLink.cs` (identical logic in `ETS/EtsTabletLink.cs`, which you should port - its line numbers in
brackets where they differ notably).

1. **Created after the gate only**: `HaloFront.Start` (`AC/Game/Front/HaloFront.cs:51-71`, called from `HaloGame.FrontStart` hook,
   `AC/Game/HaloGame.Front.cs:9`) -> `HaloTabletApp.Create(game, hub)` (`HaloTabletApp.cs:29-56`) -> `AddComponent<HaloTabletLink>()`, paints the icon
   (`HaloTabletIcon.Paint(128)`, code-drawn: nothing of the game), sets `FrameWidth/FrameHeight`. `[DefaultExecutionOrder(1000)]` on the link, 1001 on the app.
2. **Search** (`Update` 190-213): unlinked -> `TryLink()` every 0.5 s for the first 60 s, then every 5 s. One "not linked after N s" line after 20 s.
3. **Discover** (417-603):
   - `SceneManager.GetSceneByName("MasterScene")` roots first, then every other loaded scene.
   - `SearchRoots` (615-664): depth-first walk with an explicit stack; skips subtrees named `"Halo "` (the world's own objects - rename to your prefix,
     e.g. `"AC "`) and anything containing `"Simulator"`; returns the first transform with a MonoBehaviour whose `GetType().Name == "TabletControl"`
     (`FindOn` 666-684), else the first named `Tablet.XR(` / `Tablet.NonXR(`.
   - `FindTyped(root, "TabsContainer" | "HomeTab" | "TabletStatusBarView")` (686-697) with name fallbacks `Tablet.XR.Tabs`, `Tablet.XR.HomeTab`,
     `Tablet.XR.StatusBar` (`FindNamed`, depth-limited).
   - `Canvases` = Tabs' parent; its `CanvasGroup` (open/close fade). `GrabCollider`.
   - HomeTab's `Canvas` and first `GridLayoutGroup` (the home grid). Reference button = first grid child with a `Button` and a `TMP_Text` that is not
     a clone of ours or another Dreamport's (`IsOtherDreamportsClone` 606: prefixes `ETS.Tablet.`, `WoW.Tablet.`, `BO2.Tablet.`, own `Owned`). **Add
     `"Halo.Tablet."` to that list in AC** (Halo's own prefix is `Owned` there).
   - UI layer = the reference button's graphic layer (5 in the client).
   - Status bar buttons by name: contains `"Back"`, `"Resize"` (ETS also `"Exit_World"`, line 740-742).
   - `TabContents` = each tab's `ContentView` child (to detect client tab switches).
   - `Animator` with a bool parameter `IsOpen` (tablet open state), `TabletControl.DeActivate` MethodInfo (public, by reflection on the client's
     component - allowed: the type is found by name, not an obfuscated field).
   - `complete = tabs && homeCanvas && grid && reference`.
4. **RemoveLeftovers** (782-818): destroys grid / Canvases children starting with `Owned`; re-shows a Tabs container an earlier visit left hidden.
5. **Inject** (820-906):
   - Home button: `Instantiate(reference)` **under an INACTIVE staging object** (so no client script's Awake/OnEnable runs), `StripToUnityUi`
     (938-970: DestroyImmediate every MonoBehaviour whose assembly is not `UnityEngine.UI` / `Unity.TextMeshPro`, up to 4 passes), then parent into the
     grid at sibling index 0. `button.onClick = new Button.ButtonClickedEvent()` (drops the persistent call), `AddListener(OnAppButton)`. Caption TMP
     text = `AppTitle`, NoWrap + autosize. Child `"Icon"` Image gets the sprite from `AppIcon`.
   - App canvas: `new GameObject(Owned + "App")` (inactive), layer = HomeCanvas layer (9), parent = `Canvases` (sibling of the tab canvases, so the
     tablet's close fade hides it), `Canvas.renderMode = WorldSpace`, sorting layer/order + additionalShaderChannels + worldCamera copied from HomeCanvas.
     Raycasters mirrored: `TrackedDeviceGraphicRaycaster` if HomeCanvas has one (or has none), `GraphicRaycaster` if HomeCanvas has one (or has
     no tracked one). `RectMask2D` (clips + filters raycasts), opaque backdrop `Image` with `raycastTarget = true` (a miss never falls through to the
     hidden tabs).
   - `Frame` = child RectTransform, centre-anchored, `FrameWidth x FrameHeight`, on the UI layer: **the app draws everything into `Frame`**.
6. **Keep alive** (Update at 4 Hz, 215-257): re-copy `worldCamera` (HomeCanvas camera, else `Camera.main`), `KeepButtonFirst` (346-411: re-sorts to
   index 0 and logs ONCE the measured cell: "Tablet: the HALO button is cell 1 of 15 shown in the home grid ... inside the mask: yes." - the in-client
   proof), closes the app if any tab's ContentView changed (`TabsChangedSinceOpen`), re-applies the UI layer to the app subtree.
7. **FollowHomeRect** (LateUpdate, 1247-1273): app rect = HomeTab rect size/pivot/position/rotation (follows the player's own Resize); skips while
   the HomeTab is mid close-tween (lossyScale < half); `Frame.localScale = min(home.w / FrameWidth, home.h / FrameHeight)`.
8. **Open** (1111-1149): snapshot tabs, hide `Tabs`, show app, apply layers, show status-bar Back (hooked to Close), on a Small tablet press
   `Resize.onClick.Invoke()` (`ForceLarge`), log "Tablet: HALO opened (app button, size Small->Large)", raise `AppShown(true)`.
   **Close** (1151-1185): reverse; Resize pressed back only if we enlarged it and it is still Large (`IsLargeNow`, 0.35 s tween guard).
   Triggers: Back, mesh Home (`OnMeshHome`), client tab switch, `AppAvailable = false`, world unload.
9. **CloseTablet(why)** (1065-1109): closes the app then invokes `TabletControl.DeActivate` (client ignores it while the tablet is held). Halo calls it
   when a match starts loading (`HaloTabletApp.Update` 99-100) and for RESUME.
   (`TabletControl.Activate()` also exists publicly - ETS decided never to open the tablet for the player ("Q-E1: no reflection"); AC may keep that rule.)
10. **Unlink / Shutdown** (972-1023, 183-188): `HaloGame.OnDestroy -> FrontStop -> HaloFront.Stop -> HaloTabletApp.Shutdown -> link.Shutdown()`, and
    the link's own `OnDestroy`: removes button, app canvas, sprite, mesh-Home listeners, restores Tabs/Back/size.

**ETS-only additions to keep** (in `ETS/EtsTabletLink.cs`):
- `Carry()` (1498-1536): VR, "tablet follows player" OFF (tablet not child of `SomniumBridge.PlayersContainer.LocalPlayer.References.Body.Root`, read at
  4 Hz) and not grabbed (`XRGrabInteractable.isSelected`): applies the riding platform's per-frame delta (rotation + position) to the free-standing
  tablet in LateUpdate. `RidingOn` = `Func<Transform>` returning the platform (ETS: `EtsRiders.LocalPlatformFrame`; AC: the car body the local player sits in).
- `SnapDesktop()` (1439-1476) + `OnBeginCameraRendering` (1428-1437, `RenderPipelineManager.beginCameraRendering`, hooked only while linked,
  `HookRendering` 248): desktop tablet placed on `Camera.main/TabletPoint` in LateUpdate (order 1000, after the vehicle camera) AND at the first
  on-screen game camera's render. Guard `EtsTabletLayout.DesktopSnap(desktop, open, lag, 1.5f)` (`ETS/Ets/EtsTabletLayout.cs:403`): only snaps when the
  tablet is < 1.5 m from the point. **AC: 1.5 m is a frame's travel at 300 km/h (83.3 m/s) at 55 fps; raise `maxLag` to ~3-4 m or make it
  speed-based (car speed x deltaTime x 1.5) or the tablet will be left behind at high speed / low fps [B: arithmetic].**
- `ExitWorld()` (596-607): presses the client's own Exit World button (the client asks for confirmation in its pop-up).
- `TabletOpenChanged` event and `TabletOpen` re-read every frame (Animator `IsOpen`).

---

## 3. How the app's pages are drawn

- **Plain uGUI on a world-space Canvas + TextMeshProUGUI** inside the tablet's own hierarchy (no render texture, no camera of ours). Pictures are
  `RawImage` (Texture2D made at run time) or `Image` with `Sprite.Create`. **[V-code]** `HaloTabletLink.Inject` 864-900, `HaloUi` (`AC/Game/Front/HaloUi.cs`).
- Layout coordinates: Halo/BO2 use canvas units from the **top-left, y down** (`HaloUi.Place(parent, name, x, y, w, h)`: anchors (0,1), pivot (0,1),
  `anchoredPosition = (x, -y)`, HaloUi.cs:57-68). ETS uses the game's 1440x900 frame **bottom-left, y up** (`EtsGameUi.Place(parent, name, left, right,
  bottom, top)`). Recommend top-left (Halo) for AC - simpler and matches the launcher's HTML layout.
- Frame size: Halo `HaloMenuPanel.Width/Height = 1300 x 860`; ETS 1440 x 900. **Recommend 1600 x 900** for AC: Large tablet 1920x1080 -> fit
  min(1.2, 1.2) = 1.2 (fills exactly); Small 960x1080 -> 0.6 (but the app presses Resize to Large on open). **[V-code arithmetic from FollowHomeRect]**
- All widgets must be on the client's UI layer (5): the XR ray (`TrackedDeviceGraphicRaycaster.ShouldTestGraphic`) tests each graphic's layer.
  `HaloUi.Place` creates every object on layer 5; the link's `ApplyLayers()` (1280) re-applies after builds and the app calls it every 30 frames
  (`HaloTabletApp.LateUpdate` 117-120). ETS found widgets made between the 4 Hz passes were un-pressable for up to 250 ms - create them on the right
  layer at creation (`EtsTabletApp.UiLayer`, ETS/EtsTabletApp.cs:315).
- Text: `HaloUi.Text` (88-101) = TMP with `richText false`, wrap Normal, Ellipsis, `raycastTarget false`; `HaloUi.OneLine(t, minSize)` = NoWrap +
  autosize down to minSize.
- Art from game files: `HaloUi.Picture(..., bitmap, index, usedW, usedH, color)` binds asynchronously (`HaloUiArt.Bind`, worker-thread decode, main
  thread upload) and starts with alpha 0 so nothing shows until the picture is there. For AC: decode PNG/JPG with `ImageConversion.LoadImage(tex,
  bytes, false)` (ImageConversionModule) on the main thread from bytes read on a worker - **WoW shipped this** (`WOW/Game/Play/Ui/UiPortraits.cs:157`) **[V-client by WoW upload]**.

## 4. Input (VR laser, desktop mouse, layers)

- **No EventSystem of ours in the client**: the client's EventSystem + `XRUIInputModule` drives both. Halo adds one only in the editor
  (`HaloPlayer.EnsureEditorEventSystem`, AC/Game/Vr/HaloPlayer.cs:323-334, `Application.isEditor` only).
- Raycasters on each world-space canvas: `GraphicRaycaster` (desktop mouse) + `TrackedDeviceGraphicRaycaster` (VR ray and poke).
  `HaloUi.MakeCanvas` (HaloUi.cs:33-54) adds both and a transparent full-size "Pointer catcher" Image so the ray stops on the panel.
  `canvas.worldCamera = HaloPlayer.Head()` and re-assigned every LateUpdate (HaloPanelPlacer 347-350; the client's camera arrives late / is swapped).
- Physics layers for world-object clicks (not uGUI): desktop PhysicsRaycaster reaches layers 0,1,4,5,14,16; VR NearFarInteractor far ray
  0,5,6,10,14,16,23 at 5 m (DREAMPORT_GUIDE.md section 7; ETS README 1370-1380). Hub panels are uGUI so only layer 5 matters.
- Buttons: `HaloButton` (HaloUi.cs:147-310) implements `IPointerEnter/Exit/Down/Up/ClickHandler` itself (no `Selectable` navigation), 0.3 s
  double-click debounce, `Interactable` / `Selected` / `Refused` callback (plays a fail sound and says why), logs "UI: 'X' pressed (mouse | pointer N)",
  every handler in try/catch. ETS uses `UnityEngine.UI.Button` with a hover plate (`EtsTabletApp.Clickable`, 1865) and greys with a CanvasGroup
  (`SetUsable(control, usable, pressable)`, 1938: alpha 64/255 but still pressable so the page can say why).
- Sliders: no uGUI `Slider` anywhere in Halo/BO2/ETS. **WoW's `WowSlider`** (`WOW/Game/Front/WowControls.cs:112-246`, + `WowSliderTrack` 249) handles
  pointer down + drag, using `e.pointerCurrentRaycast.worldPosition` (VR ray hit on the canvas plane) else `ScreenPointToLocalPointInRectangle`, snaps
  to step, plus "-"/"+" buttons for a steady hand in a headset. Port it for volume / FFB-like sliders. Halo's settings use `<` `>` stepper buttons only.
- Text entry: Halo uses a TMP_InputField for the install folder (HaloFront.cs:276-302) - works on desktop; in VR the Bridge has
  `SomniumSpace.Bridge.Components.SomniumKeyboard` (`ISomniumKeyboard.Initialize(TMP_InputField, SomniumKeyboardSettings)` in the Bridge XML) but no
  Dreamport has used it in code and it is not in `DevTools/fusionapi/known_good.txt` **[V-run: grep]** - unproven on the upload server; avoid or run
  the API guard.
- Lists: no ScrollRect in Halo/BO2/ETS. They use fixed grids (Halo maps 4 x 5 `HaloFront.BuildMatchMaps` 363-376) and paging (ETS
  `EtsTabletLayout.ConvoyPages/ConvoyPageRows`, 696-713). **Recommend paged grids with arrow buttons** (VR ray drag-scrolling a ScrollRect is
  untested in the client) - AC needs it: 86 owned cars, up to ~10+ skins each, 17 owned tracks with up to 5 layouts.

## 5. Opening / closing the tablet and holding tablet buttons off

### 5.1 App availability / closing
- `HaloTabletApp.Update` (AC/Game/Front/HaloTabletApp.cs:92-105): `link.AppAvailable = !loading` (hides the home button while a session loads),
  `CloseTablet("a match is starting")` on the Loading edge, `HaloCombat.TabletOpen = linked && link.TabletOpen` (no firing while open).
- BO2 equivalent: `Bo2TabletApp` (BO2/Game/Front/Bo2TabletApp.cs, 427 lines) adds a room "hint card" mode and `WaitForLinkSeconds` (5 s in the client
  before the room panel shows, so it does not flash); `RoomMenuAlways = true` became the rule (room panel always, tablet has its copy).

### 5.2 HaloTabletGuard (B/Y held off) - `AC/Game/Vr/HaloTabletGuard.cs` (140 lines)
- `Update(bool holding)` each frame in VR; block = holding || last hold < 0.8 s ago (`ReleaseAfter`, a tap ends within 0.4 s).
- `Search()` (84-111): at most 5 tries (3 s then 15 s apart), `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, None)`, keeps types whose
  class chain contains a type named `"XRTabletFeature"`; walks declared fields of every class in the chain for `FieldType.Name == "InputActionReference"`
  with "Tablet" in the field name (`_rightHandTabletActivate`, `_leftHandTabletActivate`), reads `.action` by reflection and binds
  `Disable()/Enable()/enabled` delegates (no Input System type referenced at compile time).
- Disables only actions it found enabled, re-enables only what it disabled (`WeDisabled`); `Release()` restores. Any exception -> `_failed`, release,
  one warn line, tablet works as before.
- **Vehicle use** (`D:\Dreamport\Halo\Halo Dreamport\Assets\#User\SomniumHalo\Game\Vehicles\HaloVehicleDirector.cs:28-36`):
  `if (seated) _tabletUntil = Time.time + 1f; TabletHeld = vr && (seated || Time.time < _tabletUntil); if (TabletHeld) HaloTabletGuard.Update(true);`
  called every frame from the vehicle director's Update (order -90). Log once: "Vehicles: the Somnium tablet buttons (B / Y) are held off while
  seated and 1 s around getting in / out." (B was the get-out button). Released by `HaloHandling` (`HaloTabletGuard.Release()` on match leave).
- Guard log lines: "Tablet: found the client's tablet feature (2 button action(s))." / "Tablet: the client's tablet buttons are switched off while a
  gun is held (...)".

### 5.3 Gaps for AC
- **Desktop**: `IsTabletFeature` matches only `XRTabletFeature`; `NonXRTabletFeature._tabletActionRef` (Tab key / gamepad select+start) is never
  disabled. ETS's rule instead: Tab stays the client's; the world never binds Tab. For AC desktop driving, do not bind Tab / gamepad Select / Start to
  car functions. If a desktop guard is wanted, extend `IsTabletFeature` to also accept `"NonXRTabletFeature"` (field name `_tabletActionRef` contains
  "tablet" case-insensitively - the existing `IndexOf("Tablet", OrdinalIgnoreCase)` already matches) **[B: not run]**.
- **In VR the tablet's buttons are pressed with the trigger = throttle**: ETS holds truck input while the app is visible / the tablet is open
  (`EtsTabletApp.HoldInputWhileVisible`, 1635-1700; rule `EtsTabletLayout.HoldTruck(driving, appVisible, vr, tabletOpen)`, 509) and on release calls
  `DisarmVrPedals()` so pedals wait until both triggers are let go; HUD toast "Your tablet is open, so the car waits - tap B or Y to close it and drive".
- **Recommended AC policy** (ours, to confirm with the user): seated as driver -> guard B/Y while the car moves (> ~5 km/h) or a race session is live
  (B/Y may be mapped to car functions); stationary in the pits the tablet opens normally so Garage/Setup and the race lobby are reachable from the
  seat; whenever the tablet is open while seated, hold the car (brake held / throttle 0 in practice; in a live race maybe just throttle 0) and disarm
  pedals until triggers are released. Use `CloseTablet()` at race start (countdown) like Halo does on Loading.

## 6. Page frameworks compared

| | Halo `HaloMenuPanel` + `HaloFront` | BO2 `Bo2MenuPanel` + `Bo2Game.*` | ETS `EtsTabletPage` + `EtsTabletApp` |
|---|---|---|---|
| Page unit | fixed tab roots (`Tab` enum 0..7, `SetTab`) in one panel; content area 950x760 at (330,24) | same pattern (Halo is its port) | abstract class per page (`Caption, Icon, Id, Category, Available, WhyNot, Build(), Shown(), Hidden(), Tick()`), `ETS/EtsTabletApp.cs:209-250` |
| Build | eager at AddPage; lobby filled when ui.map read; settings rebuilt when `HaloSettingsUi.Version` changes | eager | lazy: built on first show (`ShowSlot` 2050 -> `BuildSlot`), rebuilt from scratch when the host changes (`Rehost` 1339) |
| Hosts | hub panel + tablet page, each a `HaloMenuPanel`, `HaloFront.AddPage(panel, tablet)` fills both with the same builders | room panel + tablet | tablet frame, or own fallback pad (`EtsVrPanel` in VR, overlay on monitor) after `FallbackAfter = 20 s` with no tablet |
| Refresh | `panel.AddRefresh(Action)` + `HaloMenuPanelHook.OnEnable -> RefreshAll()`; per-page 0.5 s ticks in `HaloFront.Tick` | | `Tick()` at 4 Hz for the shown page only, failures logged once (`Slot.TickFailed`) |
| Nav | left column of the game's caption pictures, game-mode switch (SCORES/TEAM/MATCH in a match) | | home "desktop" + game's bottom menu bar categories, popup links, `Show(pageId)`, `Toast(text)` 10 s |
| Settings | `HaloSettingsUi` registry: `Toggle/Stepper/Choice(key, label, ...)`, values in `HaloSettings` key=value file, 2 columns x 12 rows | `Bo2Settings` | per-page buttons, visit-only options |

**Recommended for AC (hybrid):**
- `AcTabletLink` = port of `ETS/EtsTabletLink.cs` (rename `ETS.Tablet.` -> `AC.Tablet.`, add `Halo.Tablet.` to `IsOtherDreamportsClone`, drop
  NewFlow/LogOnly, `RidingOn = () => AcCars.LocalRiddenBody`, raise DesktopSnap max lag).
- `AcTabletApp` = Halo's small `HaloTabletApp` (170 lines) structure (Create/Shutdown/Update/BuildPage) hosting an ETS-style page list:
  `abstract class AcPage { string Caption; string Id; bool Available; string WhyNot; void Build(RectTransform root); Shown(); Hidden(); Tick(); }` with a
  left nav column (Drive / Lobby / Garage / Results / Settings / About) and a status line + toast line + instance master label
  (`HaloMasterLabelView`, HaloMenuPanel.cs:268-304) + version label.
- Same page objects built into: the tablet `Frame` (1600x900) and the hub's floor panel (`HaloUi.MakeCanvas` + `HaloPanelPlacer`), i.e. Halo's
  "one builder, two hosts". Each host keeps its own widget instances; shared state lives in static models (`AcLobby`, `AcSettings`, `AcSelection`).
- Widgets: `HaloUi` helpers + `HaloButton` (restyled: AC launcher black/white with red #bd0000 accent) + `WowSlider` + a new paged grid
  `AcPagedGrid(parent, cols, rows, cellW, cellH, Func<int,cell builder>)` with page arrows and "n / m".
- Settings registry: port `HaloSettingsUi` (114 lines) + `HaloSettings` (AC/Game/HaloSettings.cs) unchanged apart from names.

## 7. Hub ("front") and loading card - how Halo does it

- Boot order (`AC/Game/HaloGame.cs`): `Awake` gate (hold log, grey floor, isolation "nobody") -> whitelisted -> `StartDreamport` (264) ->
  `SetStatus("Looking for your Halo install...")`, `Guard("front start", FrontStart)` (272) -> install found -> `FrontBootDone` (345) -> `FrontTick`
  each Update (327) -> `OnDestroy` -> `FrontStop` (353). Every hook in `Guard` (try/catch -> one log line).
- `HaloFront` (AC/Game/Front/HaloFront.cs) is the conductor: `Start` (51-71: settings load, lobby init, hub create, hub panel page, tablet app),
  `BootDone` (73-77: starts the worker load of UI art), `Tick` (79-101: settings tick, art tick, lobby tick, fill pages when the install is read,
  rebuild settings on registry change, tablet in-game tabs, music, keep rule), `Stop` (103-121), `OnPhase` (174-197: Loading -> hide panel + fade music;
  Playing -> hide hub; Hub -> show hub, re-place panel, `HaloLoading.BackToHub()`).
- `HaloHub` (453 lines): the room at `HaloSpace.RoomOrigin` (3 km above the maps): a 24 m platform disc (`FloorRadius = 12`, `KeepRadius = 14`,
  colliders + invisible fence), sky dome following the head, the game's ring model x40, own sun/ambient; `BuildPanel` (384-394) =
  `HaloUi.MakeCanvas("Halo Hub Panel", ..., 1300, 860, PanelMetresPerUnit = 0.002)` + `HaloPanelPlacer` + `DecorateForRoom` (logo above, button hints
  below). `HaloFront.TickKeep` (506-524): a player off the platform for 2 s is teleported back (`HaloPlayer.Teleport`).
- `HaloPanelPlacer` (HaloUi.cs:318-404): panel upright ON the hub floor (`FloorGap 0.2 m`), facing the player's level view direction; headset at
  `VrDistance = 2.6 m`, monitor at a distance that fits the FOV (and beyond the avatar in third person); placed 1 s after shown and again after
  looking away 4 s or walking > 9 m / < 0.8 m. Logs "Hub: panel placed on the floor X m in front of the headset ...".
- `HaloLoading` / `HaloLoadingScreen` (AC/Game/Front/HaloLoading.cs, 435 lines): API `Show(map, rules)`, `Progress(fraction, label)`, `Hide()`
  (black at once then 1 s reveal), `Leave()` (fade to black 0.35 s), `BackToHub()`. Card 1920x1080: monitor = `ScreenSpaceOverlay` canvas
  (sortingOrder -5, CanvasScaler ScaleWithScreenSize 1920x1080 match height); headset = world-space 3.4 m wide card 2.5 m ahead at eye height,
  smoothed follow (`PlaceCard` 308-330). Content: game background picture dimmed, map preview with rim, logo, map name 96 pt, mode, description, the
  game's loading line, stage text, percent, elapsed, 1680x10 bar. Fade cover = URP/Unlit transparent sphere r 0.3 m parented to the head camera,
  renderQueue 4000, ZWrite 0, Cull off (`BuildCover` 334-363). Music fades out on Show.

**AC mapping [B: proposal]:**
- Hub = one of AC's own showrooms, read at run time: `GAME/content/showroom/showroom/showroom.kn5` (92,937,937 bytes) with `showroom.bank` +
  `track.wav` ambience, `ppeffects.ini`, `colorCurves.ini`; alternatives `Hangar/hangar.kn5` (296 MB), `industrial/industrial.kn5` (210 MB),
  `beach/beach.kn5` (288 MB), `studio_white/studio_white.kn5` (15 KB) **[V-run: ls -la]**. The selected car (Drive page) stands on the showroom
  floor (like acShowroom); the floor panel (HaloPanelPlacer) beside it. kn5 reading is another agent's topic.
- Loading card: track `ui/preview.png` (355x200 for imola) or layout `ui/<layout>/preview.png`, `outline.png` (365x192 RGBA) as the track map,
  track name / city / country / length / run from `ui_track.json`, car badge + skin `preview.jpg` (1022x574), session type and laps; AC logo
  `content/gui/pauseMenuLogo.png` (256x256) or `launcher/themes/default/graphics/logo.png`. Keep Halo's cover/fade code as is.

## 8. Fonts

- Halo: `HaloFonts` (AC/Game/Front/HaloUiArt.cs:355-430) builds TMP fonts from SDF atlases decoded from the game (Alpha8 texture,
  `Shader.Find("TextMeshPro/Distance Field")`, fallback = default font's shader), texts made before the font exists are queued (`Waiting`) and switched.
  ETS `EtsFont` (ETS/EtsFont.cs, 426 lines) does the same from ETS's MSDF fonts (median of 3 channels) - also `Shader.Find("TextMeshPro/Distance Field")`
  (line 304), used in the shipped ETS client builds.
- **AC ships TTFs** in `GAME/content/fonts/`: `segoeui.ttf, segoeuib.ttf, segoeuii.ttf, segoeuil_0.ttf (Light), segoeuisl_0.ttf (Semilight),
  segoeuiz.ttf, seguibl.ttf, seguibli.ttf, seguili.ttf, seguisb.ttf, seguisbi.ttf, seguisli.ttf, Orbitron-Medium.ttf, consola.ttf, consolab.ttf,
  consolai.ttf, consolaz.ttf` **[V-run]**, plus bitmap fonts `*.png` + `*.txt` (e.g. `arial.png` 1024x64 RGBA; the .txt is a list of glyph x
  positions in 0..1 - for dashboards, not the tablet).
- Recipe [B: not yet run in Unity or the client]: `TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024)` -> dynamic atlas
  filled from the file (`FontEngine.LoadFontFace(m_SourceFontFilePath, ...)`, TMP_FontAsset.cs:1188). **Caveat:** `CreateFontAssetInstance` does
  `new Material(ShaderUtilities.ShaderRef_MobileSDF)` = `Shader.Find("TextMeshPro/Mobile/Distance Field")` (TMP_ShaderUtilities.cs:139-147); the
  client project has `Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader` but whether it is in the client BUILD is unknown -> if `Shader.Find` returns
  null, `new Material(null)` throws. Mitigations: (a) a carrier material using `TextMeshPro/Mobile/Distance Field` on the root's `_shaderCarriers`
  (DREAMPORT_GUIDE section 3) so the shader is loaded with the bundle; (b) wrap in try/catch, then set `asset.material.shader =
  Shader.Find("TextMeshPro/Distance Field")` (proven) for rendering; (c) fallback `TMP_Settings.defaultFontAsset` (Halo's `HaloFonts.Apply` pattern).
  Also check the upload server compiles `UnityEngine.TextCore.LowLevel.GlyphRenderMode` (TextCoreFontEngineModule) - the fusionapi guard only lists
  Fusion/Somnium members, so this needs a server-like build check. Verify with one lab run + one client log line ("Fonts: Segoe UI built from ...").
- Faces: titles "Segoe UI Light" (`segoeuil_0.ttf`), body "Segoe UI" (`segoeui.ttf`), bold `segoeuib.ttf`, numbers/timing can use the same (the
  launcher uses Segoe for everything).

## 9. Game data the AC pages can show (all read at run time; nothing ships)

| Item | Path | Verified facts |
|---|---|---|
| Car list | `GAME/content/cars/<id>/` | 180 folders; **86 owned (have *.kn5)**, **94 DLC stubs (only `ui/` with `dlc_ui_car.json`, `dlc_preview.png`, `badge.png`)** - show stubs locked (Halo's "not installed" pattern, HaloCampaignPage) **[V-run probe]** |
| Car info | `<car>/ui/ui_car.json` (name, brand, description, tags, class, specs bhp/torque/weight/topspeed/pwratio, ...) | **170 of 180 fail a strict JSON parse** (raw newlines / control chars inside strings); replacing control chars inside strings by spaces makes **all 180 parse** (System.Text.Json, trailing commas + comments allowed) **[V-run probe]** - the Data/ reader must be tolerant; Unity JsonUtility will not do |
| Car badge | `<car>/ui/badge.png` (128x128 RGBA) | 176 of 180 have one **[V-run]**; ignore files like `badge (Stefano Casillo's conflicted copy 2020-01-17).png` |
| Skins | `<car>/skins/<skin>/preview.jpg` (1022x574 JPEG), `livery.png` (64x64), `ui_skin.json` (skinname, drivername, country, team, number, priority) | 965 skins on owned cars, **all 965 have preview.jpg and ui_skin.json** **[V-run]**; skinname often "" -> use the folder name |
| Tracks | `GAME/content/tracks/<id>/` | 21 folders; **17 owned (kn5)**, 4 stubs (ks_barcelona, ks_brands_hatch, ks_nordschleife, ks_red_bull_ring: only `ui/`, `dlc_ui_track.json`, `dlc_preview.png`) **[V-run]** |
| Layouts | `<track>/ui/ui_track.json` (single layout) or `<track>/ui/<layout>/ui_track.json` (+ `preview.png`, `outline.png`) and `models_<layout>.ini` | 44 ui_track json files, all parse strictly. e.g. ks_drag: drag1000/200/2000/400/500; ks_highlands: layout_drift/int/long/short; ks_nurburgring: layout_gp_a/gp_b/sprint_a/sprint_b; ks_silverstone: gp/international/national; ks_vallelunga: classic/club/extended_circuit; ks_monza66: full/junior/road; ks_black_cat_county: layout_int/long/short **[V-run]** |
| Track json fields | name, description, tags, country, city, length, width, pitboxes, run | imola: length "4909", pitboxes "24", run "anti-clockwise" **[V-run]** |
| In-game strings | `GAME/system/locales/en.ini` (`[STRINGS]` key=value, 313 lines: PRACTICE, QUALIFY, Laps, GAP, BEST, Race Position, Traction Control, ...) | V-run |
| Launcher strings | `GAME/system/locales/launcher/en.ini` (179 lines: ABS, TRACTION_CONTROL, AUTOMATIC_GEARS, DEGREES_OF_ROTATION, ...) | V-run |
| Setup labels | `GAME/system/locales/setup/en.ini` (`[SETUP]` ARB_FRONT, DIFF_POWER, PRESSURE_LF, FRONT_BIAS=Brake Bias, DAMP_BUMP_LF=Bump, ...), tooltips `setup/en.tag` | V-run |
| Assists defaults | `GAME/cfg/assists.ini`: IDEAL_LINE=1, AUTO_BLIP=1, STABILITY_CONTROL=100, AUTO_BRAKE=0, AUTO_SHIFTER=1, ABS=2, TRACTION_CONTROL=2, AUTO_CLUTCH=1, VISUALDAMAGE=0, DAMAGE=0, FUEL_RATE=0, TYRE_WEAR=1, TYRE_BLANKETS=0, SLIPSTREAM=1 | V-run (ABS/TC 0=off,1=factory,2=on [B]) |
| In-game GUI art | `GAME/content/gui/`: `inGameMenu/{drive,setup,exit,next,racecontrol,telemetry,time}_{ON,OFF}.png` (97x97), `pitstop/*` (fuel, tyres, repair icons), `endRace/*` (logo 296x400, continue/exit/replay/restart icons), `icons/*_ON/OFF.png` (80 app icons), `pauseMenuLogo.png` 256x256, `logo_esc_menu.png` 100x100, `skin.ini` (ksGui colours: FORM BACK 0,0,0,128, TITLE_BACK 255,0,0,255, TEXT_INPUT FOCUS_BACK 128,8,8,255, CONTROL BACK 128,128,128,128) | V-run |
| Launcher art | `GAME/launcher/themes/default/graphics/` (logo.png, bgr_*.png, badges/badge_event_1st..nopodium.png, nopreview.png, nolivery.png, 24/32/42/48/64/100 icon sheets), `audio/{item,item1,select2,select3,negative,bighit}.wav` (UI click sounds), CSS accent `#bd0000` (38 uses), `#fff`, `#000`, `rgba(0,0,0,0.5)` | V-run |
| Showroom (hub) | `GAME/content/showroom/{showroom,Hangar,industrial,beach,studio_white}` | V-run |

## 10. AC tablet pages (proposal for the implementing agents)

Common chrome (1600x900 frame, top-left coords): left nav column 300 wide (DRIVE, RACE LOBBY, GARAGE, RESULTS, SETTINGS, ABOUT; in a live race:
TIMING, PIT/RESUME, LEAVE), header with AC logo, instance master label (crown), status line (bottom), toast line, version.

1. **DRIVE** (any player, free practice / hotlap): paged grid of owned cars (badge + name + class; brand filter chips from `brand`; locked DLC
   stubs greyed with `dlc_preview.png`) -> skin grid (preview.jpg thumbnails decoded at reduced size; skinname/folder, number, driver) -> track grid
   (preview.png, name, country, length) -> layout list (ui/<layout>/ui_track.json name + outline.png) -> session type (Practice / Hotlap, strings
   from system/locales/en.ini) -> GO (loads; `AcLoading.Show`). Remember the selection in `AcSettings` (`drive.car`, `drive.skin`, `drive.track`,
   `drive.layout`).
2. **RACE LOBBY** (port `HaloLobby` + `IHaloLobbyNet` contract, AC/Game/Front/HaloLobby.cs + HaloLobbyNet.cs): master edits, others read-only.
   State struct `AcLobbyState { Phase (Lobby/Countdown/Starting/Racing/Results), Count, RaceId, StartUtc, Version, Track, Layout, Laps, Minutes (timed),
   SessionKind (practice/qualify/race), AllowedCars (list or class), Assists rules (forced ABS/TC/auto, damage, fuel, tyre wear), GridOrder }`.
   Players list with chosen car/skin and ready flag; master "START RACE" (countdown, press twice to confirm like `HaloFront.ArmMap`), pull-in of
   everyone in the hub (Halo pull-in rule), latecomers spectate / join next. Master hand-over as in Halo iteration 5. Strings: "INSTANCE MASTER: name".
3. **GARAGE / SETUP** (seated in pits or in the hub): setup rows from the car's own `setup.ini` (data.acd - car-data agent) labelled with
   `system/locales/setup/en.ini`, steppers (`<` `>`) with min/max/step from the car file; fuel; tyre compound; save/load presets per car in AcSettings.
4. **RESULTS / TIMING**: live timing table (pos, driver, car badge, laps, best, last, gap), session clock; end-of-race podium using
   `launcher/.../graphics/badges/badge_event_1st/2nd/3rd/nopodium.png`; personal bests per car+track in AcSettings.
5. **SETTINGS** (`AcSettingsUi` registry = HaloSettingsUi port): ASSISTS (ABS off/factory/on, TC off/factory/on, stability 0-100, auto shifter, auto
   clutch, auto blip, ideal line, damage, fuel rate, tyre wear, slipstream: defaults read from `GAME/cfg/assists.ini`, the master's forced values
   override in a race), VR STEERING (wheel grab two-hand / one-hand / stick, like ETS `EtsSteeringGrab`), VR seat recentre, UNITS (km/h - mph),
   AUDIO (master, engine, effects, menu music via WowSlider), haptics, left-handed, install folder override (desktop text field), tablet options
   (ForceLarge, CarryWithVehicle, as ETS's System page).
6. **ABOUT**: how to play (VR / monitor controls), "Everything you see and hear is read from your own Assetto Corsa install".

In-race on the tablet (Halo's game-mode switch, `HaloMenuPanel.SetGameMode`): TIMING, RESUME (CloseTablet), BACK TO PITS, LEAVE RACE, master:
END RACE FOR EVERYONE / RESTART.

## 11. Lab support

- `AC/Editor/LabStubs/` already contains stand-ins found by TYPE NAME like the real ones: `TabletControl.cs` (IsOpened, Activate, DeActivate,
  DeactivateCalls, CanvasGroup), `TabsContainer.cs`, `HomeTab.cs`, `TabletStatusBarView.cs`, `SwitchTabButton.cs`, `XRInteractableButton.cs`
  (public `UnityEvent ButtonPressed`), own asmdef `SomniumHalo.LabStubs` (UNITY_EDITOR define constraint; runtime-kind so AddComponent works) - rename.
- Scenario `front-tablet` in `AC/Editor/HaloLabFront.cs:28` (link, home button first, page in frame, Small->Large, VR ray click, Back, shutdown).
  BO2's richer version: `BO2/Editor/Bo2LabTablet.cs` (678 lines).

## 12. Port checklist (file -> new name)

| From | To | Notes |
|---|---|---|
| `ETS/EtsTabletLink.cs` | `Game/Front/AcTabletLink.cs` | drop NewFlow/LogOnly/EtsGameUi.Note/archive icon; `Owned = "AC.Tablet."`; skip subtrees named `"AC "`; add `"Halo.Tablet."` to foreign prefixes; `RidingOn` -> local car body; DesktopSnap max lag speed-based |
| `AC/Game/Front/HaloTabletApp.cs` | `Game/Front/AcTabletApp.cs` | + ETS page list (`AcPage`), icon painted in code (AC-red slab; do not use the AC logo PNG as the home icon unless loaded at run time - loading `launcher/themes/default/graphics/logo.png` at run time is allowed) |
| `AC/Game/Vr/HaloTabletGuard.cs` | `Game/Vr/AcTabletGuard.cs` | unchanged; optionally accept `NonXRTabletFeature` |
| `ETS/EtsTabletApp.cs:1635-1700` + `ETS/Ets/EtsTabletLayout.cs:509` | `Game/Front/AcTabletApp.HoldCar` | hold car input while tablet open in VR; disarm pedals until triggers released |
| `AC/Game/Front/HaloUi.cs` | `Game/Front/AcUi.cs` | colours -> AC (#bd0000 accent, white text, black 50 % panels); `HaloButton` -> `AcButton` (UI sounds from launcher audio wavs) |
| `WOW/Game/Front/WowControls.cs:107-260` | `Game/Front/AcSlider.cs` | WowSlider + WowSliderTrack, textures replaced by plain Images |
| `AC/Game/Front/HaloSettingsUi.cs`, `AC/Game/HaloSettings.cs` | `AcSettingsUi.cs`, `AcSettings.cs` | rows registered by modules at start |
| `AC/Game/Front/HaloMenuPanel.cs` (MasterLabel, Crown, PanelHook) | `AcMenuPanel.cs` | nav column = AC pages |
| `AC/Game/Front/HaloFront.cs` | `AcFront.cs` | conductor; hooks `FrontStart/BootDone/Tick/Stop` |
| `AC/Game/Front/HaloLoading.cs` | `AcLoading.cs` | card content from track/car UI files |
| `AC/Game/Front/HaloLobby.cs`, `HaloLobbyNet.cs` | `AcLobby.cs`, `AcLobbyNet.cs` | race rules instead of game variants |
| `AC/Game/Front/HaloHub.cs` | `AcHub.cs` | showroom kn5 instead of the ring; keep platform/fence/keep rule/panel placer |
| `AC/Game/Front/HaloUiArt.cs` | `AcUiArt.cs` | worker-thread file reads + `ImageConversion.LoadImage` on main thread, `AcFonts` (TTF) |

## 13. Open / unverified

- TTF -> TMP at run time in the client (shader availability, server compile of TextCore types) - needs one lab run and one client log.
- The free-standing VR tablet carried with a car at racing speed (ETS proved trucks <= 90 km/h).
- Desktop TabletPoint snap at > 160 km/h (threshold 1.5 m must be raised).
- Whether B/Y should be guarded only while moving or always while seated (user decision).
- Performance of decoding 965 skin previews (1022x574 JPEG): decode on demand per visible page, downscale, cache with LRU; never all at once [B].
- `SomniumKeyboard` (VR text entry) unproven on the upload server.
