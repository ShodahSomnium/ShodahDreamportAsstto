# F - Front and tablet (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/`, `BO2/`, `WOW/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
All menus live in the Somnium tablet: an "ASSETTO CORSA" app on the tablet home screen (port of `EtsTabletLink`), with pages Drive
(car grid with badges, skins with previews, specs), Track (track / layout with previews, outlines, length, pit boxes), Lobby (master:
session type, laps / time, assists rules, AI count, START / STOP; everyone: joined players, ready), Garage (assists, basic setup), Settings
(install folder, VR steering mode, horizon lock, units, volumes, camera - the `AcSettingsUi` registry), Results / Timing. Plus the hub
(the spawn room: an AC showroom kn5 built with W's builder, the panel fallback when no tablet links) and the loading card.

## You own
`Data/Ui/`, `Game/Front/` (stub `AcFront.cs` with `AcFront`, `AcLoading`, `AcSettingsUi` - fill / split them; add `AcTabletLink.cs`,
`AcTabletApp.cs`, `AcPage*.cs`, `AcUi.cs`, `AcSlider.cs`, `AcHub.cs`, `AcUiArt.cs`, `AcFonts.cs`, `AcLobby.cs`, ...), `Game/AcGame.Front.cs`
(already wired), `Game/Shared/AcLocal.Front.cs` (`CarId`, `SkinId`), `Editor/AcLabFront.cs`, `Editor/LabStubs/`.

## Port from (the checklist is research/tablet_front.md section 12)
- `ETS/EtsTabletLink.cs` (1538) -> `AcTabletLink` (NOT Halo's): AppTitle "ASSETTO CORSA", Owned prefix "Ac.Tablet.", skip subtrees named
  "Ac " (our scene objects), Carry with the moving car (`RidingOn` = the seated car), SnapDesktop (raise the 1.5 m threshold for > 160 km/h),
  ExitWorld; drop NewFlow / LogOnly / EtsGameUi.Note / archive icons.
- `HALO/Game/Front/HaloTabletApp.cs` (170), `HaloFront.cs` (conductor), `HaloUi.cs` (405: raycaster recipe, Place/Text/Box/Picture,
  HaloButton, HaloPanelPlacer), `HaloMenuPanel.cs` (353), `HaloSettingsUi.cs` (114), `HaloLoading.cs` (435), `HaloHub.cs` (453),
  `HaloLobby.cs` + `HaloLobbyNet.cs`, `HaloUiArt.cs` (worker file reads + LoadImage on main thread). `ETS/EtsTabletApp.cs` page pattern
  (EtsTabletPage: lazy build, Shown/Hidden/Tick 4 Hz, Available/WhyNot, toast) and `HoldInputWhileVisible` (l.1635) +
  `ETS/Ets/EtsTabletLayout.cs` HoldTruck (l.509) -> hold the car / disarm pedals while the tablet is up (call `AcDriveInput.DisarmPedals`).
- `WOW/Game/Front/WowControls.cs` l.107-260 (VR-safe drag slider). `BO2/Editor/Bo2LabTablet.cs` (678) for the lab; LabStubs are already
  renamed in `Editor/LabStubs/`.

## Spec sections
`research/tablet_front.md` all (0 TL;DR, 1-6 tablet facts and link, 7 hub + loading, 8 fonts (TTF -> TMP at run time + shader caveat),
9 data the pages show, 10 page proposal, 11 lab, 12 checklist, 13 open). `research/ac_physics_sound_rules.md` 3.1-3.2 (rule names /
defaults for the lobby). `research/ac_car_formats.md` 6 (ui_car.json, skins). DESIGN.md "Iteration-1 user flow".

## Must do (acceptance)
1. `AcFront.Start/BootDone/Tick/Stop` conductor; settings load at Start (already in the stub - keep it before the boot reads the override).
2. Tablet app linked (client types TabletControl / TabsContainer / HomeTab / TabletStatusBarView found by type name), home button, app
   canvas in the tablet frame; works with the VR laser and the desktop mouse; hub panel fallback when no tablet (BO2: keep it always).
3. Drive page: owned cars (badge + name + class + brand filter), DLC stubs greyed "not installed"; skins with preview.jpg thumbnails decoded
   on demand at reduced size (never all 965 at once); specs from `AcCarEntry.Specs`; choose -> `AcLocal.CarId/SkinId` + AcSettings
   "drive.car"/"drive.skin" + `AcRace.ChooseCar(car, skin)`.
4. Track page: tracks / layouts (preview.png, outline.png, name, length, pitboxes, owned); pick -> `AcRace.RequestLoad(layout, rules)`
   (master or alone; others see it read-only with the master's name).
5. Lobby page: rules editor (AcRules fields with AC's names: laps / time, ABS/TC allowed, tyres out, jump start, AI count/level, allowed
   cars), START / STOP for the master (`AcRace.RequestStart/RequestStop`), players list from `AcNetGame.Players` (car, ready),
   "INSTANCE MASTER: name" (`AcNetGame.MasterName`, grey controls while `!AcNetGame.MasterKnown`).
6. Results / Timing page from `AcRace.Standings` (live) and results after `AcSession.RaceFinished`; badges from launcher art.
7. Settings page drawing `AcSettingsUi.Rows` (other modules register rows at their Start); install folder override text + "Rescan"
   (`AcGame.Current.Rescan()`).
8. Hub at `AcSpace.RoomOrigin`: showroom kn5 (content/showroom/showroom) via `AcKn5Builder` (W) - until W delivers, a plain floor; keep rule
   (teleport back after 2 s off the platform). Loading card `AcLoading` follows `AcSession.PhaseChanged/TrackLoading` (track preview,
   outline, name, length, session) - monitor overlay + VR card ahead, fade (Halo).
9. Fonts: Segoe UI from content/fonts via `TMP_FontAsset.CreateFontAsset` with the shader fallback chain (section 8); log the result.
10. UI sounds via `AcSfx.Ui`. Colours from content/gui/skin.ini + launcher accent #bd0000 (read at run time where possible).

## Player.log lines to add
`Front: tablet linked (types found: ...)` / `Front: no tablet - hub panel only` · `Front: app opened` (once) · `Fonts: Segoe UI built from
<path> (shader ...)` · `Front: showroom built` · `Front: drive choice <car>/<skin>` · `Front: track pick <key> (master|refused)`.

## Verify
build.sh x3, check.sh; lab `front-tablet` (LabStubs: link, home button, page in frame, click through Drive -> Track, settings row, close),
`front-hub` (hub + panel shot), `front-loading` (card shot with a real track preview). LOOK at the pictures. Client-only facts (real
tablet) - list the steps for the user.

## Consumes / provides
Consumes: AcContent (D), AcRace requests + Standings (R), AcNetGame players/master (N), AcSession events (R), AcKn5Builder (W),
AcSfx (A), AcDriveInput.DisarmPedals (S). Provides: the tablet app, AcLoading, AcSettingsUi (S, A, H, P register rows), AcLocal.CarId/SkinId.
