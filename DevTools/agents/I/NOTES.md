# I - AI drivers: notes (2026-11-01, second pass)

## What exists
| File | What |
|---|---|
| `Data/Ai/AcAiIni.cs` | ai.ini of a car (census: all 86 owned cars have it; v4 DOWN = % of UP) + how each key is used (BELIEF where no comment) |
| `Data/Ai/AcAiCar.cs` | the car digest the AI plans with, from data.acd: TOTALMASS + fuel (SetFuel), [FUEL] FUEL/MAX_FUEL/CONSUMPTION, FUEL_LIGHT_MIN_LITERS, tyre DY/DX_REF at static load, aero ClA/CdA, wheel force per gear, HoldRpm, brakes, wheelbase/CG, steer lock/ratio |
| `Data/Ai/AcAiTrack.cs` | a spline prepared for driving: tangents, SIGNED curvature, VERTICAL curvature (crests), side room, projection, sampling; `PitExit` (where pit_lane.ai rejoins the track) |
| `Data/Ai/AcAiHints.cs` | NEW: layout `data/ai_hints.ini` (HINT / BRAKEHINT / DANGER over lap-progress ranges; overlaps -> smallest value) [BELIEF semantics] |
| `Data/Ai/AcAiPlan.cs` | per car + line + level: corner speed (curvature x grip x downforce, crests unload, x HINT), braking pass (BRAKE_HINT x BRAKEHINT, TRAIL_HINT), traction pass, lap time, FuelPerLap; open-line end rules (stop / run through at 20 m/s); static FuelLoad / NeedsTopUp |
| `Data/Ai/AcAiReference.cs` | NEW: Kunos hotlap special events (content/specialevents, TYPE=4, TIME objectives) = external lap references |
| `Data/Ai/AcAiBrain.cs` | per-frame decisions (pure pursuit + curvature feed-forward, yaw P+I, speed control, TC, gears by ai.ini, reverse) |
| `Data/Ai/AcAiLapTimer.cs` | T3 lap timer for the harness sim (R times AI in the game) |
| `Game/Ai/AcAi.cs` | public face `Spawn/Tick/Clear/Drivers/Available` + ADDED `MaxDrivers`, `Ready`, `FastTrack`, `PitTrack`, `Hints`, `Observe()`, `HandOverAfter`; master-loss hand-over, hotlap refusal, ai_hints load, pit exit + open-line finish search, race refuel at Grid |
| `Game/Ai/AcAiDriver.cs` | one AI car (`Produce()`); ADDED `CarId, SkinId, State, LineS, Lateral, OnPitLine, TargetKmh, PlannedLapS, Recoveries, CarData, Brain, FuelLoad, FuelTopUps, Live, FastLanePlan, LockSlip` |
| `Game/Ai/AcAiField.cs` | car/skin/name choice, build coroutine (worker: digest + fuel load + plans, cached per car/level/fuel case), placement, adoption, `Refuel` at the race grid, `ApplyFuel` |
| `Game/Ai/AcAiTraffic.cs` | all cars projected once a frame (N's stand-in boxes too); overtaking / follow / alongside; emergency cast on layer 28; NEW people on foot; `MergeClear` (pit exit, reset spot); hint-map pruning |
| `Game/Ai/AcAiWatch.cs` | NEW: MonoBehaviour on each AI rig root (added in Setup, master only): calls `AcAi.Watch()` -> hand-over when not master for 1 s |
| `Editor/AcLabAi.cs` | lab scenarios `ai-magione`, `ai-race` (not run - lab phase later) |
| `DevTools/harness-ac/ProbeI.cs` | probes `aiini aiprof aicar ailine aisim aipit aistab aitimer` + NEW `aihints airef aimerge aipitlat aiopen` |

## Second pass (critic findings) - what changed
1. Master loss: `AcAiWatch` on every AI rig; `Tick`/`Watch`/`Observe` drive through a < 1 s flap, then `HandOver()` (rigs removed, `_droveHere` reset so a
   returning master adopts). R now calls `Observe()` on non-masters and seeds AI timing on take-over (DONE R).
2. ai_hints.ini: read per layout (12 layouts); DANGER shrinks the side room, HINT x corner speed, BRAKEHINT x braking. Log `Ai: ai_hints.ini of ...`.
3. Fuel: race = (laps or minutes/plan lap + 1) + 1 lap x plan fuel x 1.25 + 1 l (cap MAX_FUEL) at FUEL_RATE; practice = car.ini FUEL; plans re-made for
   that mass; refuel + re-plan at the race Grid; `ph.FuelRate` = FUEL_RATE/100; top-up to the start load below one lap of fuel (no AI pit stops).
4. Recovery: `AcTiming.Teleported(AcTiming.AiTiming(Index,false), false)` after ResetTo; in Racing/Finished the spot is at/behind the car; spots
   need 7 m clear + no fast-lane car arriving within 2.5 s.
5. `AcAiDriver.Live` added; R asked to skip `State == Failed` drivers (REQUESTS "I -> R (second pass)").
6. N asked to keep inherited AI blocks until my driver of that index has a rig (REQUESTS "I -> N: keep an inherited AI block").
7. People on foot: local player (not seated, AcPlayer.TryFeet) and remote Dreamport players on the track not in a car (AcRemotes + AcNetPlayer)
   -> an AI stops 3 m short (also when pulling out of a box).
8. Hotlap: `Available` false, `Spawn` refuses with one line.
9. Lab lap from R's LapCompleted only (the lap after the first event); Kunos gold check when a reference exists (gt86 magione).
11-14. Hint map pruned every 600 frames; static default AcRules; pit-exit wait for fast-lane traffic (2 s, max 10 s wait); ABS = factory (1) +
   the AI eases its pedal on a locking wheel (slip < -0.15) on cars without ABS.
15. Crests: vertical curvature in the plan (corners + braking + traction on a crest). The 2-D harness sim cannot show its effect.
EXTRA (found while doing 13): every installed pit_lane.ai runs on along the track for 218-1775 m after the pit exit (harness `aimerge`, 29 circuit
layouts; magione: pit lane 230-620 m of a 1250 m line). The old code followed it to its end at the pit limit (~630 m at 76 km/h on the racing line at
magione). Now the AI joins the fast lane at `AcAiTrack.PitExit` (first point back inside the room after the longest outside run).
EXTRA (REQUESTS "R -> I: open courses"): trento-bondone (finish at the line's end): the plan arrives at 20 m/s and the driver runs straight through,
brakes after it, holds; ks_drag unchanged (stop at the strip's end; harness `aiopen`).

## Verified (offline: harness on the real install, private copy `agents/I/harness`; build.sh x3 0 errors in my files; check.sh all 117 proven)
- `aihints`: 35 owned layouts plan fine; hinted points: magione 39, laguna 140, black_cat long 1334, imola 176 (+111 danger), monza 114 danger.
  Crests cost 0.1-0.4 % lap time on flat tracks, 1.2-1.5 % on highlands / black_cat. ferrari_458 fuel 0.89 l/lap magione, 2.1 monza, 4.2 highlands long.
- `aisim` sweep 4 cars x 28 layouts (with hints + crests): 112/112 finish 2 laps; 100/112 never more than 3.5 m beyond the room (was 97); worst
  458 highlands long 10.8 m (unchanged), sls_gt3 black_cat long 9.0 -> 1.5 m with the hints; lap 2 / plan mean 1.029 (max 1.081).
- External reference `airef`: 26 Kunos hotlap events with an owned car on an owned layout; sim lap 2 vs gold: median x1.057, 21/26 within 10 %,
  ks_toyota_gt86 magione 1:32.843 vs gold 1:32.000 (x1.009), nissan_gtr silverstone intl x1.014, m3_e92_drift magione x1.002; slowest laguna seca
  (huracan_performante x1.165, huayra_bc x1.169 - the corkscrew HINT 0.7 / BRAKEHINT 0.6 cost 3 %), alfa_33 vallelunga classic x1.13.
- `aimerge`: pit exit found on 29/29 circuit layouts (none on drag x5 / trento - open lines keep the old end rule); `aipit` 11 tracks: pit limit
  76 km/h, joins at the exit, max 2.7 m beyond the room right after joining (monza; it joins from the exit lane).
- `aiopen`: trento run-through plan ends at 20 m/s (12:04.1); ks_drag crosses a 2000 m finish at full speed. `aitimer` all pass.

## Not verified / open
- Nothing ran in Unity (lab phase later). P's `AcCarRig.Spawn` builds Kind Ai with physics + audio now, so AI cars should drive with P's physics
  (state Driving); the "on rails" path is only a fallback when a car has no Rigidbody (log line).
- Real lap times / stability with P's tyre model; AI_LEVEL scaling grip use = 0.87 x (0.65 + 0.35 x level/100) (// ours).
- ai_hints semantics (speed multiplier vs grip multiplier) are BELIEF; if the lab shows AI too slow at laguna / black_cat, try VALUE as a
  lateral-grip factor (speed x sqrt(VALUE)).
- Master hand-over, people-on-foot braking, pit-exit wait, recovery behind, race refuel: Unity-only, not simulated.
- AI never pits (top-up instead), no blue flags, no AI_AGGRESSION. Abs=1: cars without ABS rely on the AI's pedal easing (lab: watch for lock-ups).

## Player.log lines
`Ai: fast lane ready: ...` · `Ai: pit lane ready: ...` · `Ai: ai_hints.ini of <layout>: n hint(s), n brake hint(s), n danger zone(s) (n fast-lane points with less room)` ·
`Ai: pit exit at s m of the L m pit line (fast lane s ..), waiting point s m` · `Ai: open line: finish at s of L m - AI cars ...` ·
`Ai: spawning n AI car(s) (level L)` · `Ai: <car> ai.ini ... | planned lap m:ss.fff at level L (grip use, road, ai_hints on n points, slower on n crest points) | fuel x l (race a/b/r | practice r, y l/lap planned, tank z l)` ·
`Ai: spawned n AI cars (level L): ...` · `Ai: race fuel for n AI car(s) (race ...)` · `Ai: <name> lap m:ss.fff (...)` · `Ai: <name> recovered (why) xN` ·
`Ai: <name> topped up to x l at y l left (AI pit stops are not done) xN` · `Ai: hotlap session - AC has no AI cars in a hotlap, none spawned` ·
`Ai: no longer the instance master - n AI car(s) handed over` · `Ai: cleared (n car(s) removed)` · `Ai: n AI car(s) of the previous master found - continuing them where they are`.

## Lab steps (later)
1. `bash DevTools/lab.sh ai-magione -actrack magione -accar ks_toyota_gt86` (Kunos gold 1:32.000 check) then `-accar ferrari_458`, `-accar abarth500`;
   `-actrack monza` both. Look at ai-pitbox / ai-lap25/50/75 (car on the road, on the line); read the plan / gold / room / fuel checks; Player.log
   should show the pit exit line and the AI leaving the pit line at the exit (not 600 m later).
2. `bash DevTools/lab.sh ai-race -actrack magione -acai 3` (0 contacts with my standing car, all finish 2 laps, ai-traffic picture).
3. `bash DevTools/lab.sh ai-race -actrack trento-bondone -acai 2` (one run; every AI reaches the run-out past AC_AB_FINISH).
4. If cars spin / run wide in P's physics: lower `AcAiPlan.GripAtLevel100` (0.87) / `AcAiBrain.YawP/YawI`; if too slow raise it.

## Lab phase (2026-11-01, run by the R agent) - ai-magione FAIL (AI 1.74x slower than its plan with P's physics)
- `ai-magione -accar ks_toyota_gt86` (3 runs; runs 1-2 lost: F's lobby re-seed published "AI 0" and cleared the AI car - lab now syncs the lobby).
  Run 3: built 1/1 with P's physics, starts on pit_lane.ai (ai-pitbox.png: GT86 + driver in the pit lane by box 1), left the pits after 13.4 s,
  **3 recoveries "off the track"** on the out lap, out lap 2:39.707 (14 cuts), timed lap **2:37.055 vs plan 1:30.262 (x1.74) / gold 1:32.000 (x1.71)**,
  2 recoveries, max 16.4 m outside the room. Samples: 25 % 86 km/h (target 105, gear 2), 50 % 72 km/h lat -7.0 m (ai-lap50.png: car on the grass
  inside a left-hander), 75 % 49 km/h (target 82) on the straight. Fuel OK (27.4 l after ~2 laps, plan 0.83 l/lap). Harness aisim same car/track: 1:32.843.
- Cause NOT found (no lab run left this pass). ADDED per-lap brain diagnostics (no allocation per frame): `AcAiBrain.Diag*` + `DiagText()`; the first
  3 laps of each driver log `Ai: <name> lap ... | under target x %, throttle capped by grip circle / TC / shift cut / oversteer, saturated, off track,
  mean throttle, mean |road steer|`. Harness baseline (aisim gt86 magione lap 2): under target 0 %, caps 0/0/3/0 %, mean throttle 0.51, steer 2.1 deg.
  Next run: compare with that line to see which limiter (or the steering) holds the car back in P's physics.

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3; no lab run)
- AcAi.Clear no longer StopCoroutine's BuildAll: the generation bump makes BuildAll cancel the car it is building (AcCarSpawnHandle) and keep
  stepping P's Spawn until C disposed the half-built model (no orphan "Ac Car" left in the world, no leaked scope). Not verified in a lab.

## Polish pass 2026-11-01 (AI pace and line) - ai-magione PASS (lab run 3 of 3)
ROOT CAUSE of the x1.74 lap / off-track recoveries: `AcAiDriver.EffectiveSteerRatio` measured the steering ratio EVERY FRAME as
`Telemetry.SteerWheelDeg / SteerRoadDeg` (0.2 per-frame filter), but P writes Telemetry.SteerWheelDeg every frame (Update) and SteerRoadDeg only
every 50 Hz step, so between steps "ratio" = this frame's wheel / last step's road and the next wheel = wanted road x that ratio: a positive
feedback loop that ran away within one step at ~40 frames per step (batch ~2000 fps; still present at 2-3 frames per step in a client).
Evidence (lab 05:32 TRACE): only 21 of 135 samples with |wheel| > 20 deg had wheel/road within 10..18 of STEER_RATIO (38 under 10, 76 over 18;
wheel 450 -> road 34.6 deg at 34 km/h; mean |road steer| 10.2 deg vs 2-3 in the harness). FIX: the ratio is P's own formula (|Spec.SteerRatio|,
AcCarSpec makes it positive) - no measurement, no flip guess. Also `AcAiCar` took a NEGATIVE STEER_RATIO as "< 1 -> 14": now |ratio| like P.
SECOND cause (plan vs physics grip): `AcAiPlan.GripAtLevel100` 0.87 -> 0.78 // ours. P's chassis gives 0.78-0.85 x the tyre mu the plan
reads (harness `pchassis` steer ramp @100 km/h: gt86 0.96 g / mu 1.22, 458 1.07/1.26, abarth500 0.98/1.26, e30 0.98/1.24, r8_plus 1.01/1.24),
so 0.87 planned corners faster than P's car can turn at all (understeer wide, grip-circle caps). Harness `aichassis` (NEW probe
`DevTools/harness-ac/ProbeI2.cs`: the brain on P's REAL AcChassis + AcCarCore, flat ground; `framesPerStep` arg reproduces the stale-sense
batch pace - 1 vs 40 identical) lap 2 at 0.87 / 0.78: gt86 magione 1:39.4 x1.092 / 1:40.5 x1.060 (max out 1.8 / 0.9 m), gt86 imola 2:31.7
x1.124 2 resets / 2:27.9 x1.061 0 resets, abarth500 magione 1:37.0 / 1:36.5, 458 magione 1:30.9 / 1:31.9; at 0.78: gt86 monza x1.064,
458 monza x1.042, gt86 laguna x1.044. The real lap barely moves (the car was already at P's limit); the plan now matches what P's car can do.
Gearbox (earlier part of this pass, kept): up/downshift on road rpm with hysteresis (the GT86 hunted 2-3-2 on the straights).
LAB ai-magione -accar ks_toyota_gt86 (3 runs): run 1 (steer fix only) 2:37 -> timed 1:44.221 x1.145, 0 recoveries, max 3.77 m (spin at T1);
run 2 (+grip 0.78) 1:45.155 x1.110, spin at T1 again (max 3.62 m); run 3 PASS: timed lap 1:40.493 vs plan 1:34.743 (x1.061; harness
aichassis 1:40.465), gold 1:32.000 x1.092, 0 recoveries, max 0.00 m outside the room, fuel ok, 0 exceptions. Per-lap diag lap 2: under target
27 % (was 59 %), grip circle 4 %, TC 0 %, saturated 0 %, off track 0 %, mean |road steer| 2.8 deg (was 10.2). Pictures (looked at): ai-pitbox
(GT86 in the pit lane by box 1), ai-lap25/50/75 (car on the road, on the line, straights + a right-hander).
OPEN (P's, filed "I -> P: the car's hull / floor box hits the road under ~1 g braking"): every braking zone scrapes the Hull / "Ac Floor 0"
on the road (contact 0.31-0.33 m under the CG, front TravelM 10 -> 146 mm with loads only +700..1300 N); twice (runs 1-2, magione T1, ~100 km/h)
the contact kicked the car to yaw 3.9-4.4 rad/s + 2.6 m/s up within one 20 ms step (no tyre-force change) = the spins. Run 3 scraped without a
spin. NEW lab instrumentation (Editor/AcLabAi.cs): every AI impact with the colliders round the contact point; a 50 Hz ring of the car state
dumped at an anomaly (|yaw| > 1.5, > 12 km/h lost in 0.1 s, all wheels in the air).
Also open: abarth500 imola resets twice at s ~1440 in aichassis at 0.87 AND 0.78 (not a regression; flat 2-D sim of that chicane).
The offline aisim / airef numbers above (x1.009 vs gold) were made at 0.87 on the harness' simple car; the plans are ~4 % slower now (gt86 magione
plan 1:34.7 vs gold 1:32.0) because P's car is that much less grippy than AC's. If P's tyres move, re-measure with pchassis and move the one number.
LAB ai-race -accar ks_toyota_gt86 -acai 3 (one run, after ai-magione passed): 3/3 built, frozen on the grid, lights out (0 jump starts), 3/3
complete 2 laps; lap 2: 1:40.348 (x1.060 of plan 1:34.626, 0 recoveries) / 1:45.080 / 1:45.730; 13 order changes; 0 exceptions. FAIL: 2
"contacts" with my standing car at 6 km/h right after the green, then "Gian Sauvageau recovered (stuck) x1". The old check counted ANY impact
of an AI car within 4 m of my car - P's hull road scrapes included - so it may be a real nudge or a scrape: `OnImpact` now counts a contact
only when one of MY car's colliders is at the contact point (else logs "a road scrape"). Not re-run (one ai-race run allowed). Pictures
(looked at): ai-grid (my orange GT86 on pole, white AI behind), ai-start / ai-traffic (AI cars going past my car on the left lane), ai-finish.

## Review of the polish pass 2026-11-01 (lead reviewer)
- Checked: EffectiveSteerRatio now = |Spec.SteerRatio| exactly as P's ControlsToCore (road = wheel / max(1, ratio)) - correct; AcAiCar
  |STEER_RATIO| matches AcCarSpec; GripAtLevel100 0.78 documented from pchassis (pchassis unchanged for gt86/458/r8 after the review's
  chassis fix). The hull dive (your I -> P request) was P's damper-speed bug, fixed in AcChassis (see P/NOTES.md); ai-magione re-run
  PASS 1:40.634 x1.062, braking-zone impacts gone (2 kerb impacts left).
- ai-race re-run: still FAIL, 2 contacts at 7 km/h from an AI 458 into my standing pole car after the green - the stricter check
  confirms they are real car-car contacts, not road scrapes (REQUESTS "lead-review -> I").

## Last-gaps pass 2026-11-01 (REQUESTS "lead-review -> I": AI drives into the standing pole car after the green) - ai-race PASS (runs 2+3 of 3)
ROOT CAUSES (AcAiTraffic.Lanes / Emergency, AcAiBrain): (1) a standing car ahead made the AI PASS it (faster -> commit), and a pass set NO
follow cap at all - the lateral move (offset slew 1.6 m/s, the car lagging far behind it at low speed) was never checked against the gap, and
a pass target clamped back into the car by the alongside band still counted as a pass; (2) only ONE "front" car was looked at: once the pass
target pointed at the moving P2 car beside, the standing pole car right ahead was not considered any more; (3) the follow gap was 4 m CENTRE
to centre (= touching for two 4.5 m cars); (4) the emergency sphere cast was skipped under 2 m/s (the whole launch); (5) the brain brakes for
a traffic cap only by speed error x 0.12 (24 % brake at 7 km/h over) - far too soft at grid distances. FIX: `LaneGuard` - every car ahead in
my CURRENT lane (|dLat| < half widths + 0.25 m): approach no faster than stopping 1 m short of it at 5 m/s^2; when my target lane clears it,
no faster than lets the move finish (shift / 1.6 m/s + 0.3 s + 0.5 x preview / speed), crawl 2 m/s over its speed while the bumper gap is
> 1.3 m; a pass whose clamped target does not clear the car is a follow; follow gaps bumper to bumper (2 m + 0.45 s); a standing / slow
(< 3 m/s) car ahead is always passed when there is room (was: only inside 10 m from a standstill); the cast runs at any speed (along the
nose when slow), stops 1 m short; NEW `AcAiBrain.FollowDecel` (traffic sets the deceleration stopping short needs; > 1.5 m/s^2 the brain
brakes at least that hard, off the throttle); NEW `AcAiDriver.Blocked` (standing behind a standing car, no room to creep) counts as stuck
in Recover -> reverse with the nose swinging to the pass side (`AcAiBrain.ReverseNose`) if nothing stands 3 m behind, else the 5 s reset.
All numbers // ours. `AcAiDriver.PassSide` public (lab). Lab: new log lines (6 s after the green: per-AI traffic state; flying-pass slowest
speed before my car; closest approach) and shot ai-pass.
LAB ai-race -acai 3 (458, magione): run 1 (no move timing, no Blocked): 0 contacts (was 2 at 7 km/h), closest 3.47 m, but FAIL 2/3 finished:
P3 stood behind my car for the whole race (target 0 = not "wanting" -> never recovered). Run 2: PASS 0 contacts, 3/3 finished, closest
2.66 m; P3 stopped behind my car (blocked, offset -1.3 m but car lat only 0.5 m vs mine ~1.1 m) and was reset ("stuck") -> lap 1 2:00.8.
Run 3 (final code): PASS 0 contacts, 3/3 finished, 0 exceptions; same P3 stop + reset (lap 1 2:02.2); Eino Ash stopped 19 m before my car
on its flying lap 2 (after the other reset) and was reset too (2:06.3); Marion Hardy's flying pass slowed to 67 km/h 9 m before my car.
Lap 2 (traffic): run 2 1:34.2 / 1:34.3 / 1:36.4, run 3 1:35.0 / 1:36.4 (x1.097..1.113 of plan 1:26.59; the lead's run before the fix:
1:32.0-1:32.7). Pictures looked at: ai-grid (my car on pole, AI behind), ai-pass (P2 pulling away beside my car), ai-finish (AI on the road).
LAB ai-magione gt86: run 1 1:40.460 x1.060, run 2 (after Blocked/ReverseNose) 1:40.607 x1.062 - PASS, solo pace unchanged.
STILL WRONG: (a) from the grid the AI right behind a standing car does not get its car sideways in the ~7-10 m it has (offset reaches the
target, the car's lateral barely moves at crawl speed) - it stops short and is reset after 5 s; the Blocked reverse did not visibly free it
(no lab run left to see why: maybe PassSide/behind check). Next: steer to the offset with a short preview at crawl speed, or put the pass
offset into the preview point directly. (b) passing a STANDING car at speed costs time (67 km/h, one stop + reset) - likely the forward
cast (1.1 s reach, 0.75 m radius) braking while the lane change is still under way, now harder with FollowDecel; measure before tuning.

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 8)
Files: `Game/Ai/AcAiDriver.cs` (state `Retired`), `Game/Ai/AcAiField.cs` (DamageRate), `Editor/AcLabAi.cs` (scenario `ai-retire`).
- AI cars take damage through P's physics on the master (dynamic AcCarPhysics); `ApplyFuel` also sets `ph.DamageRate = DAMAGE_MULTIPLIER / 100`
  (AcCarRig sets it at spawn, R keeps it current).
- `AcAiState.Retired` (appended to the enum): `Damage.EngineDead` (any phase with physics) -> the car coasts (P: no throttle), I steer it with
  an own pure pursuit (preview max(6 m, 1 s)) to lateral `RetireTarget` = side * max(|lateral now|, line room + RetireBeyondSide 0) on the
  side it already is on (never back toward the line; the brain's own offset is clamped into the room, so it is not used), brake 0.15 for
  3 s then 1.0 // ours, below R's RetireStopKmh (1) after 0.5 s: Hold + `Freeze(true)`. No traffic logic, no recovery teleport, no fuel
  check while retired. R's RetireTick marks the DNF in a race (I do not set Damage.Retired). Engine working again (R: Repair(All) at grid /
  new session) -> `Unretire`: my freeze released only if `Placements` is unchanged since (else the grid owns the freeze), ChooseLine.
  Public: `RetiredAt`, `RetireRoom`, `RetireTarget`, consts `RetireSteerSeconds` 3, `RetireBeyondSide` 0.
- Hand-over: N seeds an adopted AI rig's damage itself (AcNetGame.SeedAiDamage) - FromNet unchanged. Optional I2 (corner speeds from the
  damaged wings' CL) NOT done.
- Verified: build.sh 0 errors x3, check.sh 117 proven. Lab `ai-retire` (magione practice, 1 AI 458, DAMAGE 100): run 1 FAIL - the brain's
  offset started at 0 and steered the car (5.4 m left, beyond the 4.0 m room) back to 1.2 m; run 2 FAIL (brain clamps into the room);
  run 3 (own pursuit) PASS: blown at 76 km/h, Retired, parked + frozen after 4.8 s at 5.5 m (target 5.4), no recovery, after Repair(All)
  unfrozen and moving again. Picture ai-retire-parked LOOKED at (car stopped left of the track on the run-off, smoke from C).
- Not verified: a car blowing up mid-lane (the lab case already ran wide - only the "hold where it is" branch was exercised), a race DNF
  with R's standings, AI-vs-AI damage contacts in ai-race at DAMAGE 100.

## Damage pass 2026-11-02 - flow labs (lab/flow agent)
- New scenario `ai-retire-race` (Editor/AcLabAi.cs, `RetireRace`): 1-lap race at DAMAGE 100 (lobby default), 2 AI, my car on pole. AI 0's engine
  blown after the green (P's StepEngine) -> parked + frozen in 3.9 s, standings Retired / not finished, retired bit set; my engine blown ->
  my row Retired; AI 1 finishes (2:16.437, 2 cuts, 1 stuck recovery - same class as iteration-1 ai-race first laps 1:39-2:02), results:
  1 AI 1, 2 AI 0 DNF (retired), 3 me DNF (retired); 0 contacts with my car; both AI cars' zones 0 at the end (no hull-scrape damage at 100);
  back in practice AI 0 repaired and Driving. PASSED. Picture ai-retire-race-parked: the white 458 stopped on the right edge of the road
  (half on the edge line), faint smoke.
