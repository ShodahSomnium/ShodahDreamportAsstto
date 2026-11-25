# H - HUD: notes (status 2026-11-01, second pass after the critic)

## Files (all mine)
- `Data/Hud/AcBitmapFont.cs` - AC bitmap fonts (content/fonts *.png|bmp + *.txt): glyph U table, `Advance/Measure/Layout` (quads
  x0 y0 x1 y1 u0 u1 v0 v1), `LuminanceToAlpha` (glyphs are light-on-black), `DecodeBmp`, optional `MeasureInk` (crop to the ink band).
- `Data/Hud/AcHudArtList.cs` - the 29 install images the HUD draws (names only; each one is drawn somewhere). `Data/Hud/AcHudConfig.cs` -
  AC's own HUD config + locale strings. `Data/Hud/AcHudFormat.cs` - allocation-free formatting. **NEW** `Data/Hud/AcHudSightline.cs` -
  the car's dash line from its kn5 as seen from the seat eye + the VR panel fit (Unity-free; harness `vrhud`, `sightself`).
- `Game/Hud/AcHud.cs` (contract, filled) - start/show/hide/message (+ queue)/lights/stop, feed gathering (session shown, splits,
  standings), settings, hosts, player joined/left. `AcHudDriver.cs` (LateUpdate 10010), `AcHudArt.cs` (worker read + upload),
  `AcHudFonts.cs` (Segoe UI TMP / TMP default only if its shader runs / else bitmap), `AcHudText.cs`, `AcBitmapText.cs`, `AcHudView.cs`
  (widgets), `AcHudDesk.cs` (monitor overlay), `AcHudVr.cs` (VR panel), `AcHudFeed.cs`.
- `Editor/AcLabHud.cs` - lab scenarios `hud-desk`, `hud-vr`, `hud-live` (written, NOT run: lab forbidden this phase). Probes
  `DevTools/harness-ac/ProbeH.cs` (also built by my copy `DevTools/agents/H/harness`, which compiles only D's Data + Data/Hud).

## Public API (added members; contract signatures unchanged)
`AcHud.Start/Show/Hide/Message/StartLights/Stop/Shown` (contract) + `Ready`, `UseMph()`, `SetFake/ClearFake`, `ForceVr`,
`LabPedalsDisarmed`, `Desk`, `Vr`, `LastFeed`, `ApplyStandings(ref feed, standings, phase)`, `PedalHint`. AcHudFeed added: `ShownSession`,
`LeaderFinished`, `OutLap`, `FinishedMs`, `SplitSector/SplitMs/SplitDeltaMs`, `OpenCourse`. AcHudView: `GSplit`, `MinTextSize`,
`HasMessage`, `RingFill`, `SessionOf`, `Lab*` read-outs. AcHudVr: `EyeSource`, `LineSource`, `LineDeg`, `TopDeg`, `DropM`, `Sightline`,
`MeasuredTopDeg`. Settings (group "HUD"): hud.show, hud.units, hud.gauge, hud.tower (default = gameplay.ini [OVERLAY_LEADERBOARD]
ACTIVE != 0), hud.delta, hud.scale, hud.vr.distance (cm, 62), **hud.vr.raise** (cm, 0 = the fitted place; replaces hud.vr.height),
hud.vr.size (m, 0.57).

## Second pass: the critic's 15 points
1. Fills: sprite-less Filled Images drew full -> the Essentials rpm bar and both fuel fills are now drawn by sizing the rect width
   (`SetWidth`); lab `hud-desk` checks the drawn widths (rpm 5200/7500 = 0.693, fuel 31/60) and the ring fill direction by pixels.
2. Session box: `AcHud.ShownSession` = Qualify while `AcRace.QualifyActive`, Hotlap while Rules.Session Hotlap and StartUtc > 0, the
   master's session from the grid on, else Practice -> feed.ShownSession; lab qualify + hotlap (desk) and a live qualify (hud-live).
3. Spectators (`AcStanding.Spectating`): not counted (POS p/n of racers), skipped by the blue flag, listed after the racers greyed with
   "-" (`ApplyStandings`, `TickTower`); lab checks POS 2/6 with 7 rows and the tower order.
4. VR eye: S's `AcSeat.Geometry.EyeModel(seat, true, ...)` (vr.ini DRIVEREYES 42/86 cars, else cockpit eye; passenger seat), via
   `AcSeatGeometry.Make(rig)` when the seat geometry is another car / null; then C's DriverEye, P's spec, fallback. Logged once per car.
5. VR placement: compact canvas 1400x520 (was 760); the panel is lowered until its whole top edge is 1 deg under the car's **measured
   dash line** and >= 6 deg under the horizon (`AcHudSightline` on the car kn5 in a worker; until then the steering-rim top, else 12 deg).
   Harness `vrhud`, all 86 owned cars (0.57 m / 62 cm): dash line straight ahead 0-15.8 deg (5-10 typical), fitted top edge 6.0-22.2 deg,
   bottom <= 38.6 deg; rim top at/below the centre line on 82/86; 105 ms avg / 163 ms max per car (dotnet). `sightself`: synthetic wall
   11.25 vs 11.31 deg, a mirror over a gap ignored, fit clearance 1.00 deg. Lab asserts the DRAWN top edge (RectTransform corners).
6. `LogShow` string built only while `_pendingShowLog != null`. 7. VR text floor 30 units (1.22 cm, ~1.06 deg at ~0.66 m), VR tower
   7 rows x 34; lab measures MinTextSize x scale / distance >= 1 deg. 8. TMP default font only when its material shader exists and
   `isSupported`; else the bitmap font (logged). 9. "RELEASE BOTH TRIGGERS TO DRIVE" (VR, driver, `!AcLocal.PedalsArmed`; done H).
10. Sector splits (GSplit under the delta, 5 s: sector time + delta to my best lap's sector; in-lap from AcLocal.Sector steps, the last
   sector from R's LastSectorMs); OUT LAP; FINISH + FinishedMs after the finish (LAP n clamped to N); chequered flag from the moment any
   racer finished (`AcStanding.Finished`). `AcStanding.Sector` is not drawn (AC's tower shows no sectors).
11. [OVERLAY_LEADERBOARD] ACTIVE -> hud.tower default; session_info.ini PRATICE_/QUALIFY_/RACE_VISIBLE hide the session box; messages.ini
   SERVER_PLAYER_JOINED / _DISCONNECTED gate join/leave messages (AcNetGame.PlayerJoined/Left; acs.exe wording "<car> is now driven by
   <name>", "<name> disconnected from the server"; "<name> joined" without a known car - ours). messages.ini ABS/TC gate AC's aid-change
   messages (none in the Dreamport) -> not read. Images now drawn: false_start.png (with R's "FALSE START!" under the lights),
   outOfFuel.png (tank <= 0.05 l), infinity_icon.png (untimed practice / hotlap). Dropped: blackFlag, whiteFlag, rightArrow,
   white_line_for_repeat.
12. Messages before any view wait in a 4-slot queue, replayed when the overlay / panel is built; AcEvents messages are no longer toasted
   by H (F does it), only H's own (cut, joined/left) and only in VR without a panel (no closure). hud.show off: the VR panel stays with
   messages + lights only; the tower hides with hud.show off on both paths.
13. up.png = green FASTER, down.png = red SLOWER (comments fixed). 14. VR eye re-search only while a better source can appear (rank,
   max 30 tries); Hide clears the pending log; fonts dir = install.FontsDir in both places; the font deviation is put to the lead
   (REQUESTS "H -> lead: HUD typeface"). 15. Lab scenarios extended + `hud-live`.
Also (REQUESTS "R -> H" open courses): `AcRace.OpenCourse` -> no LAP n/N, lap row "LAPTIME".

## What AC data the HUD uses (VERIFIED offline on E:\SteamLibrary\steamapps\common\assettocorsa)
- Fonts: acs.exe UTF-16 "content/fonts/*.ttf", "Segoe UI" -> HUD text = TMP SDF from segoeui/seguisb/segoeuil_0 (shared with F).
- Bitmap fonts (`bmfonts`): 58/58 parse, 93-100 % clean cuts; fallback arial_big.
- Art (`hudart`): 29/29 present + decodable; infinity_icon 29x17, false_start 708x33 (drag bar, 5 red lights), outOfFuel 74x74.
- Dial rings (`arc`): ink 30..328 deg clockwise from down; fill = (30 + f*298)/360. Rulers (`redcols`): rpmBar red 0.917..0.992, fuelBar 0..0.099.
- Config (`hudstrings`): gameplay.ini (Documents override) USE_MPH 0, TIME_DIFFERENCE 1 / POS_Y 200, SHOW_SECTORS 1, OVERLAY_LEADERBOARD 3;
  session_info.ini PRATICE/QUALIFY/RACE_VISIBLE 1/1/1, colours 0,0,0,0.3 / 1,1,1,1; messages.ini SERVER_PLAYER_JOINED 1, DISCONNECTED 1;
  skin.ini; en.ini 313 strings ("HOTLAP" has no locale key -> ours; "LAPTIME" exists).
- acs.exe strings (`exestr`): UTF-16 "CUT DETECTED" (next to "LAP WILL NOT COUNT"), ASCII "FALSE START!", " disconnected from the
  server", UTF-16 " is now driven by ", "Km/h". No "OUT LAP" string -> ours.

## Layout (ours, matched to AC's default HUD from memory - the user should judge against the game)
Monitor (1920x1080 reference): dial bottom centre, times top left, tower under it, lights top centre (y 110), delta at POS_Y 200, split
under it (y 250), flags right of the lights, messages y 290, wrong way above centre, PIT LIMITER above the dial.
VR (1400x520 units, 0.57 m): bottom row times (x -535..-215) | dial | tower (215..615); row above: wrong way (-620), delta (-375, y 52) +
split (y 2), lights (centre y 58, false-start bar under), flag (405); pit banner y 117; 3 message lines on top. Placement: point 5.

## Invented numbers (// ours) to judge
Layout positions/sizes, dial 1.5x, 5 HUD lights, tower 10+me (VR 6+me), VR 0.57 m / 62 cm / 1 deg under the dash line / 6 deg floor /
12 deg pre-measure default / sightline reach 1.5 m, VR text floor 30 units, split shown 5 s, limiter flash 8 Hz, blue flag 0.97-1.0 laps,
penalty flag 6 s, fuel warning 5 %, dry tank 0.05 l, cut message 3 s, join/leave 5 s, message fade 0.4 s, message queue 4, pedal hint text,
"OUT LAP" / "HOTLAP" / "<name> joined" texts, TMP 64 pt SDF / padding 6 / 1024 atlas.

## Player.log lines
`Hud: started (waits for the install to load the HUD art)` - `Hud: art loaded (29 images, fonts: <source>, bitmap arial_big ...; config: N
files, language en (313 strings), USE_MPH 0, TIME_DIFFERENCE 1)` - `Hud: units km/h (AC gameplay.ini USE_MPH), standings default on
([OVERLAY_LEADERBOARD] ACTIVE 3), session box practice/qualify/race 1/1/1, join/leave messages 1/1` - `Hud: fonts - AC bitmap font (no
usable TMP font: ...)` (only without Segoe UI) - `Hud: VR panel on <car>[ (passenger)]: eye vr.ini DRIVEREYES (S seat eye) | S seat eye
(cockpit DRIVEREYES) | model DRIVEREYES | car.ini DRIVEREYES | fallback, dash line X deg (kn5 sightline, N tris, M ms | steering rim top |
default), top edge X deg, centre N cm below / 62 cm ahead, 0.57 m wide` (once per car / source) - `Hud: shown for <car> (VR panel, eye ...
| monitor overlay[, passenger])` - warnings `Hud: VR dash line of <car> not measured (...)`, `Hud: missing <file>`, `Hud: frame failed`.

## NOT verified yet (needs the lab / client)
- Nothing ran in Unity (lab forbidden this phase): TMP fonts from the TTF, the overlay ZTest material, the ring direction (pixel check
  written), the drawn fill widths, VR placement over a real dash (the sightline is verified in dotnet only), the worker thread in Mono,
  TMP in the client build (carrier request to the lead still open).
- The VR panel is drawn with ZTest Always: under the dash line it covers the dash / upper wheel - and the hands on the rim at 10-and-2.
- Yellow flag is never raised (no data). AC's real HUD layout is from memory, not screenshots. In-lap split times are frame-accurate
  (R publishes only completed laps' sectors). harness `hudmock vr` still draws the old 1400x760 VR layout.

## Lab steps (when allowed)
1. `lab.sh hud-desk` -> hud-desk-{2000rpm,6500rpm,limiter,essentials-mph,qualify,practice,split,finished}.png; checks queue, fills, ring
   pixels, spectators, qualify/hotlap, untimed/out lap, split, leader-finished flag, finish row, out-of-fuel. LOOK: ring grows clockwise
   from bottom-left; bars partly filled; tower 1..6 then "- LateJoiner" greyed.
2. `lab.sh hud-vr -accar ks_mazda_miata` (also a car with vr.ini eyes - `vrhud` lists "eye vr.ini" - and lotus_49, the highest line)
   -> hud-vr-eye.png: the panel must sit on the dash under the road view; checks eye source, kn5 line, top edge clearance, text >= 1 deg,
   pedal hint, hud.show off, passenger mirror.
3. `lab.sh hud-live -actrack magione -accar abarth500` -> live feed, PRACTICE / untimed / out lap, QUALIFY + clock in the Practice phase,
   VR panel from S's eye on the live car, getting out hides the HUD.
4. Client: Player.log lines above; monitor + VR; tablet HUD settings.

## Requests
Filed: H -> lead (TMP SDF carrier materials, open), H -> lead (HUD typeface vs DESIGN.md, open), H -> R, H -> C (done).
Answered: F -> H (fonts), R -> H (race fields; open courses), S -> R, H FYI (pedal hint + S's eye) - all "DONE H".

## Inputs from other modules (as of 2026-11-01)
R: AcLocal timing (Laps, LapMs, LastMs, BestMs, DeltaMs, Sector, LapValid, Position, InPitlane, Finished, FinishedMs, PenaltyMs, OutLap,
WrongWay, LastSectorMs), `AcRace.Standings` (Spectating, Finished), `QualifyActive`, `SessionTimeLeft`, `OpenCourse`, `AcHud.StartLights`.
S: `AcEvents.SeatChanged`, `AcLocal.Seated/Seat/PedalsArmed`, `AcSeat.Geometry` / `AcSeatGeometry.Make` (EyeModel, ModelToWorld, Data).
P: `AcLocal.Telemetry`, `rig.Spec`. C: `rig.Model` (Root, DriverEye, SteeringCenter/Axis/RimRadius). N: `AcNetGame.PlayerJoined/Left`.
F: `AcFonts`, `AcFront.Toast`, `AcSettingsUi`.

## Lab pass 2026-11-01 (hud-desk 1 run, hud-vr 2 runs - the last PASSED)
- VR panel PLACEMENT changed (integrator: it covered the dash and the top of the wheel). New default `hud.vr.place` 0 = BESIDE THE WHEEL,
  toward the car's centre (over the centre console): its inner edge 3 deg (RimClearDeg, ours) outside the steering rim seen from the eye
  (16 points round C's hub at rim radius + 2 cm; 20 deg default before C's model), top edge 1 deg under the dash line at every yaw it covers.
  1 = straight ahead under the dash line (old); the passenger always straight ahead. Compact canvas 900 x 660 (dial | standings bottom, times
  above the dial, split / delta / lights above the standings, flag + wrong way between, pit + messages on top); default width 0.42 m
  (hud.vr.size 0.30-0.70); text floor 30 units = 1.29 deg. New AcHudSightline.FitAt / PanelPoint / TopEdgeAt / InnerEdgeYaw; grid now +-64 deg yaw.
- Dash line now scanned DOWN from the horizon to the first covered cell (a column covered at the horizon = 0, no road). The old bottom-up scan
  stopped at the passenger footwell floor (beyond MaxDist 1.5 m) -> 33 deg beside the wheel and the panel was pushed to 50 deg down.
  Offline (agents/H/harness ProbeH2 `vrside`): miata/458/abarth/e30 yaw 46-48, top 7-13 deg, bottom ~35-41 deg; lotus_49 (open cockpit,
  road visible beside it) still goes low (top 34 deg) - judge in the client, or use place 1.
- hud-vr run 2 (miata): eye S seat eye, kn5 line 7.3 deg, panel 0.62 m away at yaw 47, top 10.5 deg (clearance 1.0), inner edge 24.1 vs rim
  21.1 deg, bottom 36.7 deg, passenger mirrored. Pictures LOOKED at: hud-vr-eye.png - wheel, both gauges and the windscreen are clear; the
  panel sits right of the wheel over the radio/console; hud-vr-panel.png - times, dial (3 / 187 km/h), 3 red lights, -0.21, 7-row tower all
  readable. NOT seen in the picture: the "CUT DETECTED" message line (not checked by the lab).
- Readability fix: the project is LINEAR colour space; AC's gamma-space translucent backdrops (session_info 0,0,0,0.3 + white_line.png alpha
  51) came out light grey with unreadable labels (hud-desk-2000rpm.png). `AcHudView.GammaAlpha` (dark: 1-a' = (1-a)^2.2; light: a' = a^2.2)
  on every config colour + `VeilTint` on white_line: rows are dark again (hud-vr-panel.png: LAP 4 / POS. readable). hud-desk not re-run.
- hud-desk run 1: 33 ok, 1 FAIL = a stale lab check (the false-start bar correctly stays with the earlier "FALSE START!" message); the check now
  tests the lamps (`LabLampsShown`). Pictures: dial ring/gear/speed, tower 1..6 + greyed LateJoiner, lights + false-start bar, delta, messages.
- Lab only: the hub showroom car / menu panel hidden in hud-vr (they overlapped the lab car).

## Polish pass 2026-11-01 (F/A agent re-ran hud-desk; no H code changed)
- hud-desk re-run: PASSED, 30 ok / 0 FAIL (the stale false-start check is fixed: "start lights + AC's false-start bar shown").
- Pictures LOOKED at (2000rpm, 6500rpm, qualify, finished): the standings tower rows are dark and readable (GammaAlpha works); the time box
  values (1:01.234 / 1:37.531 / 2/6) read, but its small grey labels ("LAP 4 S2", "POS.") and the third time are crossed by the hub's
  over-exposed white roof strips showing through AC's 0.3-alpha backdrop. The dial has no backdrop: on the white hub floor the gear digit is
  NOT visible and "187" barely; the rpm ring reads. On a track (darker background) this is likely fine - judge in hud-drive / the client.

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 5 - the damage displayer)
Files: `Data/Hud/AcHudArtList.cs` (Dmg* paths, DamageFiles (14 drawn), DamageFlatspots (4, not drawn), All, IsDamage, Crop*), `Game/Hud/AcHudArt.cs`
(loads All; the 14 damage layers DXT5-compressed after mips, sprites cropped to the inked middle 160,80 192x352 of 512; the worker also calls
P's `AcDamageRules.Load`), `AcHudFeed.cs` (Damage, Dmg* values, DmgPrint, RepairLeft/Total), `AcHudView.cs` (group GDamage, DamageTint,
TickDamage, DNF in the tower for AcStanding.Retired, lab read-outs), `AcHudDesk.cs` / `AcHudVr.cs` (placement), `AcHud.cs`, `Editor/AcLabHud.cs`
(`hud-damage`; `Ready` now checks All = 43 pictures), NEW probe `DevTools/harness-ac/ProbeDmgH.cs` (`dmghud`).
- What it draws: AC's stacked layers content/texture/damage/{tyre_*, sus_*, front, rear, left, right, gearbox, engine}.png (center.png on disk
  but not named in acs.exe -> never loaded; flatspot_* not drawn). Values: body = ZoneByte / MAX_DAMAGE_KMH, engine = EnginePct / 100, sus =
  SuspDamage01 per corner (FL FR RL RR = P's LF RF LR RR); gearbox / tyres at 0. Tint white 0 -> yellow 0.5 -> red 1, alpha 0.85 // ours.
  PRINT_VALUES=1 prints "FRONT n %" ... "SUSP. n %" (our labels). While R's pit-box repair runs: "REPAIRING n S" under it (and shown).
- When: TIME s (4) after `AcEvents.CarDamaged` on my car / the car I sit in (repair-only events do not pop it), always while R's repair runs;
  hidden at DAMAGE 0 (session rule) and with setting `hud.damage` off (registered by H in the HUD group, default = Documents gameplay.ini
  [DAMAGE_DISPLAYER] IS_ACTIVE through AcDamageRules). "ENGINE BLOWN" message once per blow (not in Racing / Finished: R says "ENGINE BLOWN -
  RETIRED" there).
- Where: monitor = anchor left-middle at (POSITION_X, DISTANCE_FROM_CENTER_Y) = (20, -200) reference px, i.e. 200 px BELOW the centre // ours
  reading (keeps it clear of the standings tower); car 260 px high. VR = a cell beside the panel on the side away from the steering wheel
  (AcHudVr.CentreSide), bottom-aligned, car 300 units high // ours. The first lab put it on the wheel side, where it covered the wheel hub.
- API: `AcHud.SetFakeDamage(float[5] zones km/h, float[4] susp01, float enginePct)`, `ClearFakeDamage`, `DamageShowLeft`, `FakeDamageMultiplier` (lab).
- Verified: build.sh 0 errors x3, check.sh 117 proven. Harness `dmghud` PASS: 18 paths exist + decode, every layer's ink is inside the crop,
  rules read (TIME 4, MAX 60, POS 20 / -200, PRINT 0, IS_ACTIVE 1); composite picture LOOKED at (all 0 / 0.5 / 1 / the lab fake: front white,
  rear yellow, both sides red, engine yellow, sus FR yellow / RL red - left layer on the image's left, front on top). Lab `hud-damage` PASS x2
  (25 ok): 43/43 pictures; every layer's tint as specified; hidden 4 s after the hit, at DAMAGE 0 and with hud.damage off. Pictures LOOKED at:
  hud-damage-desk (car outline lower left, colours right; on the white hub floor the white layers are hard to see), hud-damage-vr-cell
  (beside the standings, toward the passenger side; outside the straight-ahead view in hud-damage-vr-eye at fov 100 - a head turn).
- Not verified: the live path (a real crash -> CarDamaged -> 4 s) and the repair countdown in a lab; colours / layout against AC (ours).

## Damage pass 2026-11-02 - flow labs (lab/flow agent)
- VR damage cell MOVED onto the panel (Game/Hud/AcHudVr.cs `PlaceDamage`, Game/Hud/AcHudView.cs): it takes the lap-times box's place (left
  column above the dial; the times + delta hide while the displayer shows: TIME s after a hit / a running repair); VrDamageCarH 300 -> 220
  (// ours, so car + countdown line = 262 fits under the messages). Why: lab race-damage-live measured the old cell (beside the panel on the
  car-centre side) 77 deg right and 34 deg down of the 458 driver's view - never seen in a headset. After: yaw 33 deg, pitch -24 deg; picture
  live-dmg-repair-vr-eye shows the car + "REPAIRING 35 S" in view; hud-damage re-run PASSED, hud-damage-vr-eye (miata) shows the cell in view.
- Live path verified (race-damage-live): a real CarDamaged pops the displayer for 4 s; it shows with the countdown during a seated pit repair and
  goes when the repair ends. Desk layout: still only the hub picture (seated monitor overlay is not captured by the lab, see R notes).

## Damage pass 2026-11-02 (fixer)
- Shift-light red line (DAMAGE_DESIGN 3.3): AcHudFeed.RedLineRpm = min(LIMITER, engine.ini [DAMAGE] RPM_THRESHOLD) while DAMAGE > 0 (FakeDamageMultiplier honoured) and the car has RPM_THRESHOLD; 0 otherwise. AcHudView.LimiterFlash flashes at the red line when it is below the limiter. The rpm ring still spans 0..LIMITER. DAMAGE 0 = iteration 1 (RedLineRpm 0).
- Verified: build.sh 0 errors. Not lab-run (hud lab not re-run).
