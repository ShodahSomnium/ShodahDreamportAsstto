# H - HUD (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/`, `BO2/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
AC's driving HUD essentials, drawn with AC's own art and fonts read at run time: speed (km/h | mph), gear, rpm bar with the limiter,
lap / best / last / delta, position and lap count, start lights, flags, pit limiter, wrong way, messages, standings tower. Monitor = a
screen overlay; VR = a panel anchored to the car's dash (moves with the car, never head-locked while driving). Also the bitmap-font
reader (content/fonts *.png + *.txt) that C uses for digital cockpit displays.

## You own
`Data/Hud/` (Unity-free: bitmap font reader `AcBitmapFont` - glyph spans from the .txt U positions), `Game/Hud/` (stub `AcHud.cs` - fill
it; add `AcHudView.cs`, `AcHudDesk.cs`, `AcHudVr.cs`, `AcHudArt.cs`, `AcTower.cs`, ...), `Editor/AcLabHud.cs`, probe `DevTools/harness-ac/ProbeH.cs`.

## Port from
- `BO2/Game/Front/Bo2Hud.cs` + `Bo2DeskHud.cs` (VR + monitor HUD hosts), `BO2/Editor/Bo2LabDeskHud.cs` (lab).
- `HALO/Game/Hud/HaloHud.cs` (149), `HaloHudKit.cs` (315: quads / labels / meters), `HaloHudDigits.cs`, `HaloHudMeter.cs`.
- `ETS/EtsVrPanel.cs` (422: a VR panel attached to the vehicle), `ETS/EtsFont.cs` (426: building a TMP font at run time + shader fallback).

## Spec sections
`research/ac_physics_sound_rules.md` 4 (HUD art: content/gui/essential/rpmBar.png, fuelBar.png, PlayerCurrentTime/*, sessionRanking/*,
flags/*, content/texture/texture_trafficlight_red/green/off.png, wrongway.png, rpm_on/off.png; bitmap fonts format; system/cfg app inis;
"layouts are drawn by acs.exe - match screenshots"), 3.3 (start lights), 3.4 (pit limiter). `research/tablet_front.md` 8 (TTF -> TMP,
shader caveat), 9 (locales: system/locales/en.ini strings PRACTICE, QUALIFY, Laps, GAP, BEST...). DREAMPORT_GUIDE section 1.4 (VR comfort).

## Must do (acceptance)
1. `AcHud.Start(game)` (R calls it; subscribe `AcEvents.Message` and `AcEvents.SeatChanged`: show when driving / riding, hide on foot),
   `Show(rig)`, `Hide()`, `Message(text, s)`, `StartLights(reds, green, visible)`, `Stop()`.
2. Essentials from `AcLocal.Telemetry` (rpm bar with limiter flash from LimiterRpm, gear R/N/1.., speed, fuel) and `AcLocal` race fields
   (lap n / laps from `AcSession.Rules`, current / last / best, delta, position / cars, cut warning when !LapValid, pit limiter when
   InPitlane), standings tower from `AcRace.Standings` (top 10 + me).
3. Art and fonts from the install only (content/gui, content/texture, content/fonts; strings from system/locales/en.ini); our layout
   positions are // ours (match AC screenshots; describe what you matched in NOTES.md).
4. Monitor: ScreenSpaceOverlay canvas, scales with resolution. VR: world-space panel at the dash (C's model; a point below the
   windscreen, // ours offset) or a fixed seat-relative point; readable at 0.7-1 m; never in front of the eyes.
5. Units setting `hud.units` (km/h | mph) and `hud.show` via `AcSettingsUi` (group "HUD").
6. No allocation per frame (cache strings for numbers: integer -> string table, StringBuilder reuse).

## Player.log lines to add
`Hud: art loaded (n images, fonts: ...)` · `Hud: shown for <car> (VR panel | monitor overlay)` · `Hud: units km/h|mph`.

## Verify
build.sh x3, check.sh; harness `Cmd_bmfont <font>` renders a sample string to PNG (look at it: glyphs in the right order); lab `hud-desk`
and `hud-vr` with fake telemetry (rpm sweep, gear 3, lap times, position 2/6, red lights 3) - shots; LOOK at them against AC's HUD.

## Consumes / provides
Consumes: AcLocal (P telemetry, R race fields, S seat), AcSession (R), AcRace.Standings (R), AcEvents, AcCarModel (dash anchor, C),
AcInstall.FontsDir (D). Provides: AcHud (R drives lights/messages), the bitmap-font reader in Data/Hud (C's digital displays).
