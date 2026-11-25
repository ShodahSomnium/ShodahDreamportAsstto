# F - Front and tablet menus: notes

Status 2026-11-01 (second pass, after the completeness critique): all 22 critique points handled (one skipped with a reason, below).
The whole tree compiles x3 (build.sh into `DevTools/agents/F/buildout`: runtime 0 / server 0 / editor tools 0 errors), check.sh "all 117
Fusion/Somnium members are proven". NOT run in the Unity lab yet (this phase forbids it) - the lab scenarios are written, steps below.

## Files (all mine)
- `Data/Ui/AcUiData.cs` (Unity-free): AcLocale, AcRgba, AcGuiSkin, AcAssistsIni, AcSetupRow(s), AcUiFormat, AcPaging, AcCarFilter.
- `Game/Front/`: AcFront (conductor), AcTabletLink, AcTabletApp (+AcTabletIcon), AcMenu (+AcPage, AcGrid), AcPagesDrive (DRIVE, TRACK),
  AcPagesRace (RACE LOBBY, TIMING), AcPagesMisc (GARAGE, SETTINGS, HOW TO PLAY), AcLobby, AcHub, AcLoading, AcUi (+AcButton, AcPanelPlacer,
  AcCrown), AcSlider, AcUiArt, AcFonts, AcSettingsUi.
- `Game/Shared/AcLocal.Front.cs` (CarId, SkinId, Ready, TabletOpen), `Editor/AcLabFront.cs`, `DevTools/harness-ac/ProbeF.cs`.

## Public API others use (only ADDED members)
- `AcFront.LobbyRules` (R), `HoldCar` (S), `Assists(rules)` / `FillAssists(into, rules)`, `SetupValue(car, key, out v)` (P), `Toast`,
  `ChooseCar` (now returns false = refused, nothing kept), `RequestLoad`, `RequestStart`, `CloseTablet`, `Content`, `Results`, `LocalLaps`.
- Added this pass: `SettingsVersion`, `TyreBlankets` (for P, nobody reads it yet), `UseMph()` (guarded AcHud.UseMph), `StartLabel(rules)`,
  `LeaveOrJoin(why)` / `LeaveLabel()` / `CanJoinSession` / `LeaveOrJoinPossible`, `SetSetup(car, key, v)`, `SetupWaiting`, `SpecOf(car)` /
  `SpecReading(car)`, `CompoundChoice(car)` / `SetCompoundChoice(car, i)`. AcHub: `ApplyShowroomLight`, `ClearCar`, `DetachPanel` /
  `AttachPanel` / `PanelOnTrack`. AcTabletLink: `RefreshIcon()`, `IconShown`. AcTabletApp: `Icon`, `IconFromInstall`. AcLobby: `Sig(rules)`,
  `AllowedText`, `DefaultsSource`, launcher range constants. AcPanelPlacer: `TrackMode`.

## What changed in the second pass (critique number -> fix)
1/2 BootDone calls `AcSfx.Preload` and `AcTrackWorld.RegisterSettings` (guarded): AUDIO + TRACK rows (weather, time of day) before any load.
3 Lobby shows `AcNetPlayer.Ready`; F sets `AcNetGame.Spectating = AcRace.Spectating` (4 Hz). 4 Master rule edits in untimed practice go to
  `AcRace.RequestRules(AcLobby.Draft())` (1 s debounce; never while a qualify clock / race runs; a new master adopts the running rules);
  assists re-apply whenever `AcSession.Rules` is replaced; LEAVE TRACK (master) / LEAVE SESSION (others, R's lone leave) / JOIN SESSION
  (`AcRace.JoinSession`, when the master's session runs and I am in the hub) on RACE LOBBY and TRACK; SPECTATING shown. 6 Icon: the link
  re-sets the sprite when `AppIcon` changes, `RefreshIcon()` before the old texture is destroyed, old sprite destroyed. 7 Hub never writes
  RenderSettings. 8 Units via `AcHud.UseMph()`. 9 GARAGE values (setup.ini keys + tyre compound) go into the car on the track when it
  stands still (<= 1 km/h, ours), toast "Setup applied"; otherwise at spawn (P). 10 No linked tablet on a track: the hub panel leaves the
  hidden hub and stands in front of the player on foot (placer TrackMode: any place, floor = feet). 11 ChooseCar commits only when R says
  yes or no track is loaded. 12 Showroom car built only in the Hub phase, removed on Loading. 13 Hub light from
  content/showroom/showroom/colorCurves.ini via W's AcTrackLook.Compute + AcMaterials.SetLight (boot + every return, one frame after the Hub
  phase); the hub's directional light uses the same values (W's URP scaling). 14 Lobby defaults = `AcRace.DefaultRules()` (re-seeded 1 s
  after boot); rows DAMAGE MULTIPLIER, RACE OVER TIME added; ranges = the launcher's own sliders (laps 2-100 + TIMED, time 5-min steps to 90,
  AI level 80-100, damage 0-100 step 10, fuel Off/On = 0/100 %, tyre wear Off/1/2/3 = 0-300 %, jump start None/Pits/Drive-Through); RACE
  OVER TIME 0-600 s step 30 and AI max 18 marked ours. 15 ALLOWED CARS for non-masters from the published `AllowedCars`. 16 START button /
  toasts name the session (AcLocale). 17 GARAGE: TYRES (compound) row, TYRE BLANKETS aid. 18 TIMING paged with your row pinned, clock
  (SessionTimeLeft) or "LAP n / N"; lobby drivers paged, AI listed. 19 Reflection (DeActivate, ButtonPressed): both public in the client
  source, guarded with logged fallbacks - sign-off asked (REQUESTS F -> lead). 20 Page text rebuilt only on change (signatures) on LOBBY,
  TRACK, DRIVE, GARAGE (FillAssists into one object), SETTINGS. 21 AcLoading.Show doc fixed; VR toast no longer names buttons.
  22 Lab: see below. SKIPPED: QUALIFY_MAX_WAIT_PERC row - not an AcRules field (R's AcRaceDefaults); asked R.

## Verified (offline)
- Harness on the real install: `fui`, `fsetup`, `ffmt`, `fbounds` (first pass) and NEW `flobby`: launcher slider attributes read from
  launcher/themes/default/index.html (sliderRaceLaps 2/100/1, practiceslider 0/90/5, qualifyslider 5/90/5, ailevel 80/100/1, damageslider
  0/100/10, fuelrate Off,On, Div12 Off,1,2,3, sliderFalseStart None,Pits,Drive-Through); server_cfg LAPS 5, QUALIFY TIME 10, RACE_OVER_TIME 180,
  ABS 1, TC 1, AUTOCLUTCH 0 (R keeps 1 for VR, documented by R), DAMAGE 100 (R keeps 0: no damage model); showroom light (W's maths): HDR 0.8,
  gamma 1.6, sun elevation 52 deg, SUN 2.37,2.16,1.82, AMBIENT 0.84,0.00,1.25 (purple, as the file says), SKY 0.04,0.29,0.63; tyre
  compounds: 86 owned cars, 65 with a choice, 0 failed (bmw_m3_gt2: SS/S/M(default)/H/SH).
- Compile x3 0 errors, check.sh all proven (2026-11-01, after every change).

## NOT verified (needs the lab / the client)
- Nothing seen rendered yet: menu layout (18 lobby rows at 36 px), fonts, the showroom with its own (purple-ambient) light - JUDGE the picture,
  the panel standing on a track, the loading card, the cover fade.
- Rules sync through R + N in a real instance (offline R applies locally); spectating bit on the wire; JOIN SESSION round trip.
- Garage apply while stopped (P's ApplySetup / SetCompound on a live car); real tablet link / icon in the client; mouse + ray on pages.

## Open / requests
- F -> lead: sign-off for DeActivate / ButtonPressed reflection. F -> A: optional `AcSfx.HubAmbience(bool)` (showroom track.wav).
- F -> P: TyreBlankets (info). F -> R: QualifyMaxWaitPerc field if wanted. All earlier asks (R, S, N, P, A, H, W) are DONE and answered.
- Seated monitor player without a tablet: no menu while seated (get out with the exit key, the panel appears). Hold-car depends on S (done).

## Player.log lines
`Front: the showroom hub and its menu are up` · `Front: audio preloaded and the TRACK / AUDIO settings rows asked for at boot (n -> m rows)` ·
`Front: hub light from content/showroom/showroom/colorCurves.ini (boot|back in the hub): sun ..., ambient ...` · `Tablet: ...` link lines ·
`Tablet: the app icon carries the launcher's logo` · `Front: lobby rules seeded from ...` · `Front: drive choice <car>/<skin> (... race took it |
kept for the next track | refused by the race on this track - not kept)` · `Front: track pick <key> (master|refused...)` · `Front: start
race|qualify|practice|hotlap (master|refused, ...)` · `Front: lobby rules sent to everyone during practice (taken|refused ...; logged once)` ·
`Front: spectating - a race runs without me ...` · `Front: join session|leave track|leave session (done|refused, ...)` · `Front: no tablet on
the track - the menu panel stands in front of you ...` · `Front: <car> tyres: <compound> (garage).` · `Front: n garage value(s) put into the
car on the track (...)` · `Front: the car's pedals are held while ...` · `Loading: <key> ...` · `Hub: shown|hidden`.

## Lab steps to run later (`bash DevTools/lab.sh <scenario>`)
1. `front-tablet`: link, button first + caption, clone stripped, menu in frame, launcher-logo icon shown and alive, Small->Large, VR-ray
   DRIVE -> car -> DRIVE THIS CAR, TRACK -> layouts, SETTINGS row; desktop-mouse RACE LOBBY (START label, LEAVE grey), TIMING, GARAGE;
   HoldCar + S's TabletUp while "driving" with the app open, released on Back; reopen; Back x2 = 4 resizes; shutdown. LOOK at tablet_1..8.png.
   Ready() of every scenario also checks the TRACK + AUDIO rows exist before any load.
2. `front-hub`: showroom built, panel, room + car pictures, all 7 pages shot, art decoded; desktop-mouse click on GARAGE and an aid; setup
   rows listed; light globals set. LOOK at hub_*.png (judge the showroom light).
3. `front-loading -actrack magione`: card with the real preview / outline, bar, Hide = black then fade < 3 s. LOOK at loading_1_card.png.
4. `front-session -actrack magione` (NEW): stand-in tablet open -> LOAD: tablet DeActivate called + showroom car removed; practice: hub hidden,
   no showroom car; lobby TYRE WEAR > reaches AcSession.Rules and re-applies the aids; START says "START QUALIFY" for a qualify draft; GARAGE
   FUEL goes into the stopped car; TIMING shows; tablet gone -> the panel stands on the track (session_1_panel_on_track.png); LEAVE TRACK -> hub
   back, panel back, showroom car rebuilt (session_2_hub_again.png).
Client steps for the user: tablet -> ASSETTO CORSA icon (launcher logo) first on the home grid -> app; laser and mouse on DRIVE / TRACK /
LOBBY; as master change a rule while practising and see it on a second client; as non-master LEAVE SESSION then JOIN SESSION; in a car with
the tablet open the car must not accelerate.

## Lead integration changes (2026-11-01)
- AcPagesRace.cs: RESET CAR button (`AcRace.ResetCar`) between GET IN MY CAR and BACK TO PITS (row of three, button "Lobby Reset").

## Lab pass (2026-11-01, F agent) - front-tablet / front-hub / front-session -actrack magione
Runs: front-tablet x3 (last: PASSED 34/34), front-hub x2 (run 1 PASSED; run 2 all page/click checks ok, the one FAIL was the lab
checking the showroom car before its async build in the fresh slot-B copy - lab now waits), front-session x2 (last: PASSED, all 15).
Pictures looked at: tablet_1..8, hub_1..11, session_1/2. No exception / shader error from our code (only Unity's Search indexer).
Fixed:
- HUB LIGHT: acShowroom.exe's string table names only `system/cfg/colorCurves.ini` (+ `content/showroom/<name>/settings.ini` [LIGHT]
  LOCK_SUN / SUN_DIRECTION, absent for "showroom"); the showroom folder's own colorCurves.ini (AMBIENT 114,0,147, HORIZON 255,0,34) is never
  opened by the game - it made the hall purple. AcHub.ApplyShowroomLight now reads system/cfg/colorCurves.ini (fallback: the showroom's).
  Log: `Front: hub light from system/cfg/colorCurves.ini (as acShowroom.exe) (boot): sun 3.81,3.69,3.34, ambient 0.94,1.15,1.39 ...`.
  Picture: neutral blue-grey hangar, white tiled floor, roof-grid sun shadows. Floor/white walls clip to white (no tonemap/auto-exposure:
  AC's ppfilter + AUTO_EXPOSURE would bring it down) - W's light scaling, judge in the client.
- Page backdrop alpha 0.97 -> 1 (AcUi.Page): the HDR-bright showroom bled through the world panel as a ghost of the car.
- SETTINGS rows: long labels from S/A (horizon lock, X shifts down) were shrunk to ~6 px on one line; now `AcUi.Wrapped` (2-3 lines, min 12)
  and upper case like the AC rows; values wrap too ("Right trigger gas, left trigger brake").
- AcTabletLink: reopening the app within 0.35 s of closing (our Resize tween) left the tablet Small; now trusts the size our last press asked
  for while it tweens (`_pressedToLarge`), also in IsLargeNow.
- Lab script: name read before the grid refresh destroys the clicked car button; START label checked with the app open (the page ticks
  only when visible); panel-on-track shot from beside the car (editor has no player); hub pages get 1.5 s real time for previews.
Verified in pictures: DRIVE (brand tabs, 12 cars with badges, spec line, preview, 8 skin thumbnails, DRIVE THIS CAR), TRACK (9 previews,
NOT INSTALLED / n LAYOUTS badges, preview + outline, LOAD/LEAVE), LOBBY (18 rules, drivers, START PRACTICE, greyed buttons), GARAGE (aids),
TIMING, SETTINGS, the tablet home button with the launcher logo, panel standing in Magione's pit lane, hub + showroom car back after LEAVE TRACK.
Clicks: VR ray (TrackedDeviceGraphicRaycaster) DRIVE/car/TRACK/track/SETTINGS/row; mouse (GraphicRaycaster) LOBBY/TIMING/GARAGE/aid.
Hold: HoldCar + S's TabletUp while driving with the app open, released on Back. Loading card: only seen as "gone" in front-session
(front-loading not run this pass).
Still open: hub floor over-exposed (W light scale, no tonemap); garage setup column says "Reading..." for a while on first open (async);
slider labels touch the slider bar top (cosmetic).

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3; no lab run)
- Lobby slider ranges are read from the install's launcher/themes/default/index.html (new Data/Ui/AcLauncherSliders.cs, loaded on the boot
  worker; each slider found by the data-bind above it: RACELAPS, QUALIFYMINS / PRACTICEMINS, RACE/AI_LEVEL, DAMAGE, FUEL_RATE, TYRE_WEAR - the
  ids are not unique). Verified with a temporary harness probe on this install: 7/7 found (laps 2..100/1, practice 0..90/5, qualify 5..90/5,
  AI 80..100/1, damage 0..100/10, fuel 0,1, wear 0,1,2,3; x100 = %). AcLobby's constants became properties with NEUTRAL ours fallbacks
  (laps 1..50, minutes step 10 max 60, AI 50..100, damage step 25 max 100, fuel / wear 0,100). Log: "Front: the game's ui read - ... lobby
  sliders from <source> (...)".
- Showroom car: one build at a time - a new pick cancels the running build (AcCarSpawnHandle) and waits as the single pending request.
- Not verified in a lab.

## Polish pass 2026-11-01 (build.sh 0 errors x3, check.sh all 117 proven)
- REQUESTS "R -> F: a lobby re-seed ... publishes the lobby defaults over the session" - DONE (the code came from the cut-off attempt of this
  pass, reviewed and kept): `TickSession` sets `_rulesSentSig = DraftSig()` right after the boot re-seed (a Load is not an edit) and, if a
  track is loading / loaded then, seeds the draft from `AcSession.Rules` first (`AcLobby.SeedFrom`, writes no "lobby.*"); `TickRulesToRace`
  seeds from `AcSession.Rules` when this client becomes master (hand-over) on a track, before taking the signature. Added now: `Stop()`
  also resets `_wasMaster`, `_rulesDirtyAt`, `_lobbyReloadAt` (a re-start sees the master transition again and drops a pending re-seed).
  Logs: `Front: lobby rules seeded from the running session (AcSession.Rules) (...)`, `Front: I am the instance master on a track - the lobby
  draft starts from the session's rules (...)`. Verified by build only - no lab covers a hand-over.
- front-hub re-run: PASSED (22 ok, 0 FAIL; the showroom-car timing fix confirmed: "the chosen car stands on the showroom floor"). No
  exception from our code (only Unity's Search indexer). Pictures LOOKED at: hub_2_room (neutral grey hangar, panel in the middle, floor
  clipped white), hub_3_car (white Abarth on the floor, panel beside it), hub_page_4_drive (brand tabs, 12 cars, spec line, preview, 8 skins),
  hub_page_6_lobby (18 rules, START PRACTICE / END SESSION greyed in the hub), hub_page_11_garage_mouse (aids + setup column filled).

## Polish pass 2026-11-01 - lead re-lab (front-loading first run, front-session re-run)
- front-loading (never run before): PASS x2. Lab fix (Editor/AcLabFront.cs `Loading`): the 120-FRAME wait before the card checks is now
  1.5 s real time (batch runs ~2000 fps: 120 frames = 0.06 s, the bar catches up at 0.5/s and the card fades in at 2.5/s); the card shot is
  1920x1080 (at 1280x720 thin strokes of the Light face drop out: "c ockwise").
- Real bug fixed (Game/Front/AcLoading.cs card build): the car badge / name row sat at a fixed y 630 while 4 info lines (place, length + run,
  pit boxes, session) end at 646 -> the badge covered the "PRACTICE 10 min" line (lab picture). The car row now starts at
  `max(630, y + 8)` and the preview 60 below it (// ours). Re-shot loading_1_card.png: no overlap, "clockwise" whole at 1080p, bar 43 %.
- front-session -actrack magione: PASS (25 ok). Pictures LOOKED at: session_1_panel_on_track (lobby page on the track, 18 rules, START
  PRACTICE), session_2_hub_again (white Abarth in the hangar). Seen in the 720p panel shot: thin glyphs ("I", "l", "i") drop out at ~8 px
  ("T MING", "PRACT CE", "BACK TO P TS") - sub-pixel quads at the lab's resolution; check legibility on the real tablet / in VR.

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 4 / 9 F)
Files: `Game/Front/AcPagesMisc.cs` (GARAGE REPAIR section), `AcSettingsUi.cs` (row), `AcLobby.cs` (DAMAGE key), `AcPagesRace.cs` (DNF),
`Editor/AcLabFront.cs` (scenario `front-garage-repair`).
- GARAGE "REPAIR" (left column under the aids, y 540..742): AC's pit-app rows "REPAIR BODY", "REPAIR BODY + SUSPENSION", "REPAIR ENGINE"
  (exe strings re-typed) with the install's art content/gui/pitstop/repair_body|repair_sus|repair_engine_ON|OFF.png (ON when R's
  `AcRace.CanRepair(what)`: my car stopped in its own box, that part damaged, no repair running, not retired), R's estimate in whole seconds
  (`RepairEstimate` = car.ini [PIT_STOP] seconds per 10 %), a REPAIR button -> `AcRace.Repair(what)` (R freezes, counts down, repairs).
  Status line: "REPAIRING n s" with operating_repair.png while R's repair runs, else "ESTIMATED PIT STOP TIME n s" (full repair) + R's
  reason when refused, "Nothing to repair." / "Damage is off in this session (...)" at DAMAGE 0. Texts change only on whole-second / state
  changes (no per-frame allocation).
- Settings: `cars.visualdamage` "VISUAL DAMAGE" (group CARS, default on; R's AcCarDamageGate reads it). `hud.damage` "DAMAGE DISPLAY" is
  registered by H with the HUD rows (its default is gameplay.ini IS_ACTIVE, known once the install is read).
- Lobby DAMAGE row (unchanged range / step from the launcher slider): default now R's DefaultRules = server_cfg.ini DAMAGE_MULTIPLIER (100);
  remembered under the NEW key `lobby.dmg` (AcLobby.DamageKey) because iteration 1 wrote `lobby.damage = 0` with every lobby edit.
- TIMING page: gap column "DNF" for `AcStanding.Retired` (not finished).
- Verified: build.sh 0 errors x3, check.sh 117 proven. Lab `front-garage-repair` (magione practice, DAMAGE 100, ferrari_458) PASS (2 runs; final
  run all ok): R's DefaultRules DAMAGE = server_cfg 100; clean car -> 3 rows OFF; 100 km/h front hit -> body / body + susp ON, engine OFF,
  "200 s" / "500 s" = RepairSeconds; instant Repair(All) -> OFF and "-"; 12 km/h touch -> REPAIR BODY pressed -> R froze the car for 40.0 s
  = RepairSeconds(Body), then clean, released, rows OFF, "-". Pictures LOOKED at: garage_repair_2_damaged (AC art: red ON body/sus icons,
  white OFF engine icon, seconds, REPAIR buttons, ESTIMATED PIT STOP TIME line), _3_repairing (rows OFF, operating_repair icon, "REPAIRING
  39 s"), _4_done. The 458 behind shows C's damage paint.
- Observation (not mine to fix here): in this lab the SETUP header still names the boot car (ABARTH 500) after `AcRace.ChooseCar(458)` -
  the lab bypasses F's ChooseCar, so AcLocal.CarId (F) was not updated; the real tablet path goes through AcFront.ChooseCar.
- Not verified: the rows on the real tablet in VR (text size at 17 px), the lobby key migration with a real iteration-1 settings file.

## Damage pass 2026-11-02 - flow labs (lab/flow agent)
- `front-garage-repair` now picks the car through AcFront.ChooseCar (Editor/AcLabFront.cs): the first run used R's ChooseCar only, so
  AcLocal.CarId stayed the boot car and the GARAGE showed "SETUP - ABARTH 500 ESSEESSE" above a 458 (lab artefact, the tablet path sets both).
  Re-run PASSED; garage_repair_2_damaged shows "SETUP - FERRARI 458 ITALIA", REPAIR BODY 200 s / BODY + SUSPENSION 500 s ON, ENGINE OFF.
- Seen in the 1280x720 panel pictures (panel ~640 px wide): the letter I drops out of small labels ("REPA R", "SETT NGS"); the large tablet
  pictures (tablet_8_garage) draw it - looks like sub-pixel glyph sampling at that distance, not checked in a headset.
