# I - AI drivers (stretch) (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.
This module is a STRETCH goal: do it after the core is usable; never block others. Start with the pure parts (driver logic in the
harness) while W/P/C are being built.

## Scope
AC-style AI opponents so one player can race: an `AcCarInput` producer following `ai/fast_lane.ai` (target speed / gas / brake hints
and side limits from the spline payload, the car's `ai.ini` from data.acd), difficulty (AI_LEVEL 0..100), overtaking lateral offset,
recovery when off track or stuck; the instance master spawns N AI cars (on the grid for races, in free pit boxes for practice) using P's
physics and C's model (AcCarRig Kind Ai); other clients draw them from N's "Ac Net Ai" blocks.

## You own
`Game/Ai/` (stub `AcAi.cs` with `AcAi` + `AcAiDriver` - fill it; add `AcAiLine.cs`, `AcAiField.cs`, ...), `Editor/AcLabAi.cs`, probe
`DevTools/harness-ac/ProbeI.cs`. Pure driver math you want to run in the harness goes Unity-free into `Data/Ai/` (yours; the harness compiles Data/**).

## Port from
- `ETS/Ets/EtsTrafficSim.cs` (2308) + `ETS/EtsTraffic.cs` (2479): traffic cars following lanes with speed control, spawning / despawning,
  drawing many cars cheaply - the lane follower and spacing logic are reusable ideas.
- Data: `AcAiSpline` (`Nearest`, `NearestNear`, `Progress`, `P(i, AcAiSpline.Speed|Gas|Brake|Radius|SideLeft|SideRight|Camber...)`,
  `LateralOffset`, `IsClosed`), `AcdArchive.ReadIni("ai.ini")` (`[GEARS] UP DOWN`, `[PEDALS] GASGAIN BRAKE_HINT TRAIL_HINT`, `[STEER] STEER_GAIN`, `[LOOKAHEAD] BASE GAS_BRAKE_LOOKAHEAD`).

## Spec sections
`research/ac_track_formats.md` 5.1-5.3 (spline format, payload fields, progress), 6.12 (ai_hints.ini, ai_corners.ini), 9.2 (reader).
`research/ac_physics_sound_rules.md` 1.8 (ai.ini values), 3.1 (AI_LEVEL in race.ini). `research/ets_vehicles.md` 8.5 (AI = master).

## Must do (acceptance)
1. `AcAi.Spawn(count, rules)` (master only; R calls it at Grid / Practice; cars from `rules.AllowedCars` or owned cars, skins varied),
   `Tick()` (R calls it each frame on the master), `Clear()`, `Drivers`, `Available` (true once implemented and `AcTrackWorld.AiLine` exists).
2. `AcAiDriver.Produce()`: look-ahead point on the spline by speed, steer toward it (P's relative steer), speed target from the payload
   speed hint scaled by level (// ours scale), braking by distance to the next lower target, gear via the car's auto-shift (P), recovery
   (reverse / `ResetTo` the nearest spline point after 5 s stuck, // ours).
3. A lap of magione / monza by ferrari_458 and abarth500 AI without leaving the track, lap time within 10 % of AC's AI at level 100 (state
   your reference: the fast_lane.ai header LapTime field (`AcAiSpline.LapTime`, meaning unproven) or the player's best lap).
4. Overtaking: lateral offset within SideLeft/SideRight when a car is ahead and slower; no driving through the local player (raycast /
   proximity on layer 28).
5. Net: agree with N in REQUESTS.md on the AI block API (master publishes each AI car's pose/controls/timing; other clients draw them as
   remote cars). AI laps go into `AcSession.NotifyLap` / standings through R (ask R for the hook).

## Player.log lines to add
`Ai: spawned n AI cars (level L): <car list>` · `Ai: <name> lap <time>` (first lap only) · `Ai: <name> recovered (stuck)` (count) · `Ai: cleared`.

## Verify
build.sh x3, check.sh; harness `Cmd_ailine <track>` (speed/gas/brake hint profile sanity, look-ahead geometry); lab `ai-magione`
(one AI car laps once; shots at 3 points along the lap; lap time logged).

## Consumes / provides
Consumes: AcTrackWorld.AiLine/StartGrid/PitBoxes (W), AcCarRig/AcCarPhysics (P), AcCarModel (C), AcRules (R), N's AI blocks.
Provides: AcAi (R), AI cars in the standings (via R), AI blocks content (N).
