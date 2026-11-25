# P - Physics: running notes (2026-11-01, second pass after the completeness review)

## Status
Module complete for iteration 1, compiling (build.sh: runtime / server / editor 0 errors; check.sh "all 117 proven"). Offline-verified on the
real install with the harness (the SAME Unity-free code the game runs). NOT yet run in the Unity lab (this phase forbids lab.sh) - the lab
scenarios are written and ready (below).

## Files (all P's)
- `Data/Physics/AcCarSpec.cs` - every physics ini parsed from data.acd (car, engine + power.lut + coast + turbos + throttle.lut, drivetrain,
  suspensions (+ HEAVE + ROD_LENGTH + TOE_OUT + WBTYRE_STEER z), tyres, aero, brakes, electronics (+ CURVE levels), ctrl_*.ini, colliders.ini,
  ai.ini, setup.ini). `PhysicsCgHeightM` / `StaticCgHeight` / `AxleStaticHeight` (static ride height, see "Suspension reading"). `Summary()`.
- `Data/Physics/AcCarSpecParts.cs` - part classes (+ `AcAxleSpec.SteerArmZ`, `ToeDegFor`, `HeaveRodLength`; `AcAidSpec.LevelCount`,
  `DefaultLevel`, `LimitAt`).
- `Data/Physics/AcTyreModel.cs`, `AcCarCore.cs` (+ TC/ABS levels, H-shifter grind rule, `Grinds`), `AcChassis.cs` (two-pass travel/forces,
  design-point springs + rod, tyre deflection, toe, `StaticTravel`, `StaticCgHeight`), `AcAeroModel.cs` (+ `GroundHeight`, `RideHeights`).
- `Game/Physics/AcCarPhysics.cs` - MonoBehaviour (+ visual travel offsets, per-frame steering wheel, puppet MovePosition, ReapplySetup,
  ride heights, grind sound). `Game/Physics/AcCarRig.cs` (+ `ApplyLocalSetup`, server rates at spawn). `Game/Shared/AcLocal.Physics.cs`.
- `Editor/AcLabPhysics.cs` - lab scenarios. `DevTools/harness-ac/ProbeP.cs` - probes `pgrep`, `pfiles`, `spec`, `psim`, `pchassis`, `pride`.
  `DevTools/agents/P/harness/` - P's private harness copy (same ProbeP.cs).
- Census outputs (this folder): `spec_all.txt`, `psim_all.txt`, `pchassis_all.txt`, `pchassis_rates.txt`, `pride_all.txt`.

## Second pass: what the review found and what changed
1. Straight-line drift / spins (72/86 cars): FIXED. `AcChassis.Step` updated wheel i's travel and computed its force in one loop, so the ARB /
   heave spring read the other wheel half a step apart (one-sided ARB force on every pitch). Now pass 1 integrates all four travels, pass 2
   computes the forces. pchassis without disturbance: drift 0.00 m on every car checked (458, alfa_33, bmw_1m, 595ss, xbow, f40; were up to
   2950 m). Toe added (TOE_OUT / WBTYRE_STEER z per axle, setup TOE_OUT_xx). New pchassis test: 0.05 rad/s yaw gust at 80 km/h under full
   throttle, no steering: 81/86 damp it ("STABLE"); 5 diverge: ks_abarth_595ss (+_s1/_s2, rear engine, zero rear toe), ks_alfa_33_stradale,
   shelby_cobra_427sc - plausible for those cars, open for the lab / user's judgement.
2. Drawn wheels floating / sunk (36 cars > 2 cm): FIXED in P. `AcCarPhysics.VisualTravelOffset(i)` = physics design hub - C's wheel rest hub
   (body space, measured once in Init after `Model.SetWheelState(i,0,0,0)`), added to every Telemetry TravelM (local, AI, and remote via N's
   record). AC draws wheels at the physics positions. Lab physics-flat / physics-fleet now check tyre bottom vs the plane (< 2 cm).
3. 0-100 vs ui_car.json: real numbers (psim_all.txt): n 65, mean ratio 1.28, median 1.25, 23 cars > 30 % slow. psim now splits 0-100:
   launch (clutch slipping) mean 1.03 s at 1300-1700 rpm (3500 on the Lotus 4-cyl: their MIN_RPM) - AC's [AUTOCLUTCH] MIN_RPM..MAX_RPM
   window as written ("minimum rpm for autoclutch engagement"); factory TC active > 0.5 s on 32 cars - their SLIP_RATIO_LIMIT (0.06-0.08)
   sits below our model's longitudinal peak (0.105-0.137 = tan(FRICTION_LIMIT_ANGLE)/CX_MULT, OUR reading of the brush normalisation);
   the rest power-limited with power.lut as written (Elise SC 189 Nm vs ui 242). ui values are brochure numbers (GT-R 2.1 s is bad data).
   ferrari_458 psim 4.11 s (clutch 0.66 @1334 rpm, TC 1.89, power 1.43) / pchassis 4.18 s vs brief 3.3-3.6; 1.07 g (pchassis) / 1.21 g
   (psim skidpad) vs ~1.3. NOT changed: no basis in the data to move the peak slip or the launch; judge in the lab against AC itself.
4. VR steering wheel at 50 Hz: FIXED. Update() writes Telemetry.SteerWheelDeg from the current Input every frame while driven (and
   AcLocal.Telemetry's), PushVisuals sends it to C's model.
5./6. FuelRate / TyreWearRate and SpeedLimitKmh: the review was stale - R sets all three every frame (AcRace.Flow LocalInputTick /
   ApplyRates). P also sets the rates at spawn (Local / Ai) from AcSession.Rules. `EffectiveSpeedLimitKmh` (= SpeedLimitKmh) ADDED.
7. Garage after spawn: stale too - F's TickSetupApply re-applies changed keys when stopped. P ADDED `ReapplySetup()` /
   `AcCarRig.ApplyLocalSetup(ph)` (applies only changed values; `ApplySetup` records every applied value).
8. Remote cars: ApplyPuppet stores the pose, FixedUpdate moves the kinematic body with MovePosition / MoveRotation (PhysX sees velocity),
   Interpolate for drawing; first placement and jumps > max(5 m, 0.25 s x speed) teleport. DEVIATION (agreed with N): brief item 4's
   "puppet wheels re-cast on the local road, spun from speed" is not done - N draws remote wheels from the record.
9. Pressure: comment and code agree now: AC loses |p_hot - IDEAL| * D_GAIN; with no thermal model the default cold PRESSURE_STATIC is taken
   to reach IDEAL hot, so the loss is |p_set - STATIC| * D_GAIN (either direction costs grip) - // ours.
10. Aero heights: investigated on the 45 cars with ground luts (pride_all.txt). Pickup vs wing height agree for underbody wings; the body
   wings decide: ferrari_458 WING_0 height_body_CD.lut spans 0.30..0.47 m and mp412c WING_0 peaks at 0.52 m - at the pickup ride height
   (~0.1 m) they read 0.82 / 0.25 (458 top speed rose to 348), at the wing height 1.00 / 0.95. So the luts keep the WING height; the
   critic's 458_gt2 splitter loss came from the static CG (fixed by 12): splitter / diffuser now 0.76 / 0.90 at rest (were 0.37 / 0.47).
   Pickups are used for `RideHeightFront/Rear`.
11. Lab targets: physics-flat now checks against the harness with tolerances (458: 0-100 4.18 +-0.3 s, 100-0 35.5 +-3 m, 1.07 +-0.08 g;
   abarth500 8.76 / 37.0 / 0.98), uses the harness' slip-holding test driver per fixed step, logs the brief's targets, and checks the
   straight line (< 3 m sideways, < 2 deg heading without steering).
12. Ride height: NEW suspension reading (// ours, evidence pride_all.txt): BASEY = wheel centre at the design point where the springs are at
   free length, ROD_LENGTH preloads ("positive raises ride height"; across 86 cars rod tracks the static sag: gt86 155/160 mm, 458_gt2
   15/24), tyre deflects load / RATE. Static wheel = BASEY + (sag - rod), solved with stops + heave (`AcChassis.StaticTravel`). Fuel burn,
   SPRING_RATE, ROD_LENGTH_xx and compounds now change the ride height. Static CG vs -GRAPHICS_OFFSET.y: mean +29 mm (body 3 cm lower than
   the modellers' offset on average, sd 29 mm) vs "design point = static" mean 4 mm - the physics evidence (rod ~ sag, race-car ground luts
   at ~1.0 at rest) wins; LOOK at the lab pictures (wheels in the arches). FUELTANK position not simulated (fuel mass at the CG).
13. TC / ABS levels: electronics.ini CURVE rows -> `AcAssists.TcLevel/AbsLevel` (-1 = the row nearest SLIP_RATIO_LIMIT). Not simulated:
   AcRules.DamageMultiplier (no damage model), TyreBlanketsAllowed / thermal tyres (tyres always at their performance peak = "warm").
14. H-shifter: SUPPORTS_SHIFTER cars with auto-clutch off need the clutch (> half) or revs within VALID_SHIFT_RPM_WINDOW, else the gear grinds
   (refused, A's Gear(gear, true)). Ackermann NOT done: AC's comes from the geometric steering linkage (WBCAR/WBTYRE_STEER), which P does
   not model; an invented ratio would not be the car's.
15. No invented engine: a missing / empty power.lut = zero torque + a Problems line (none of the 86 cars).
16. Telemetry.Clutch in neutral = the pedal (was 1 = pressed).
17. "driven" InfoOnce text is constant now (no current mass / inertia).
Rule check items: `AcCarModel.PhysToBody` replaced by a local copy; `Model.Hull/Bounds/Wheels` reads guarded.

## Verified (harness on E:\SteamLibrary\steamapps\common\assettocorsa, after the changes)
- `spec`: 86/86 parse; 5 cars carry 8 explained problems (unchanged).
- `pchassis all` @50 Hz: 86/86 settle at exactly the computed static CG (0 cars > 5 mm off), creep 0.0 mm/s, positive steer turns RIGHT;
  81/86 stable under the yaw gust. ferrari_458: static CG 0.425 m (-off.y 0.450), 0-100 4.18 s, top 329 (ui 325+), 100-0 35.5 m, 1.07 g;
  30 / 90 Hz: 4.16 / 4.18 s, 35.3 / 35.5 m, 1.07 g (rate-independent). abarth500: 8.76 s, 218, 37.0 m, 0.98 g.
- `psim all`: fleet line above; 458 shift1 2.83 s, 0-100 4.11, 0-200 11.5, top 335, 100-0 35.0 m, 1.21 g @100.
- `pride all`: the suspension / aero / drawn-wheel census quoted above.

## Lab pass (2026-11-01, integration lab agent; no P code changed)
- physics-flat -accar abarth500 PASS (every check): static CG 0.508 = chassis, 4 wheels, no creep, drawn tyres 0/0/0/0 mm, steering wheel in
  the frame 207/207 deg, 0-100 9.05 s (harness 8.76, ui 7.4), top 217, straight line 0.03 m / 0.01 deg, 100-0 35.3 m (harness 37.0), 0.98 g
  turns RIGHT, park / occupy / 1 s see-through / freeze revs 6457 / 60 km/h limiter + light / puppet teleport + MovePosition. Pictures: tyres on
  the plane, wheels in the arches (car purple from the hub ambient, see C notes).
- physics-458-magione PASS: pit box 0, 4 wheels on PITLANE/0.97, upright; 6 s at 60 % gas drove only 54 m ending at 15 km/h (the after-shot
  shows the car angled next to the grass strip - probably the pit lane curve / an obstacle with no steering; not investigated).
- 458 0-100 vs real 3.4 s: harness psim 4.11 / PSIM_TC=0 4.06 - TC is NOT the cause (0.05 s). Time goes to the autoclutch launch at
  1334 rpm (MIN/MAX_RPM 1100-1700 as written, 270 Nm, 0.66 s, 0.58 g in the first second) and power-limited 2nd gear (power.lut 408-444 Nm
  used as wheel torque, no losses: 0.6-0.68 g at 72-97 km/h, consistent with the lut + inertias). 0-200 11.5 s vs real 10.4. No change made:
  nothing in the data says AC launches higher or makes more torque; judge against AC itself (autoclutch launch in the game).

## NOT verified / known gaps
- No Unity run yet: rays on W's track, hull contacts, interpolation, the drawn-wheel offsets on C's real nodes (incl. ksanim suspensions),
  MovePosition puppets against the local car, A's feed, the per-frame steering in a headset.
- 0-100 slower than brochure (above); launch window and longitudinal peak slip are the open judgement calls.
- Not simulated: thermal tyres, FLEX/XMU, geometric camber / steer (Ackermann), EDL, stability control, damage, KERS, ctrl_turbo / 4ws /
  arb controllers, fuel tank position, packers / heave rods in the setup.

## Numbers we chose (// ours) the user should judge
Fuel 0.75 kg/l; air 1.2 kg/m^3; low-speed tyre damping blend 3 m/s; slip-state caps 4 / 3; tyre shape 2s-s^2 + exp falloff; speed
sensitivity = grip/(1+SS*sliding speed); BRAKE_DX_MOD as +x %; RR /1000; DAMPING drag Fz*D*min(v,20)/4; packers max(BUMP_STOP_RATE,
5 x spring); droop guard 2 cm..25 cm (>= 2 cm below static); camber roll gain 0.6 / 0.8; static tyre deflection (no dynamic tyre spring);
toe = atan(TOE_OUT / WBTYRE_STEER z) (2 cm arm guard); setup units ROD_LENGTH 0.1 mm, TOE_OUT 0.01 mm; TC default row nearest the limit;
H-shifter clutch > 0.5, 50 rpm window floor; pressure p_hot = p_set + IDEAL - STATIC; idle controller (idle-rpm)/250; TC proportional cut;
ABS release below 2 m/s off; reverse below 2 m/s; auto-shift lock 0.25 s, downshift < 95 % of UP; autoblip POINT_2 reading; virtual km;
body friction 0.3; impact >= 3 km/h, scrape 10 Hz >= 5 km/h; puppet teleport > max(5 m, 0.25 s x speed); controllers' FILTER per 333 Hz.

## Player.log lines
`Physics: <car> spec: ...` (once per car) - `Physics: <car> problems: ...` - `Physics: <car> wheels drawn at the physics hubs: travel
offsets LF/RF/LR/RR a/b/c/d mm; static CG x m vs -GRAPHICS_OFFSET.y y` - `Physics: rig <car> (<kind>) built in n ms: ...` -
`Physics: <car> local setup: n garage values applied; aids ...; rates fuel n % tyre wear n %` - `Physics: <car> driven: substeps 7 at
fixedDeltaTime 0.0200 (350 Hz), TOTALMASS n kg + fuel, inertia box ..., set by hand, centre of mass at the root, static CG height x m` -
`Physics: <car> fell through at (..) / recovered to (..)` - lab: `Physics: <car> validation: ...`, `Physics: ferrari_458 brief targets: ...`.

## Lab steps to run later (in this order; look at the pictures)
1. `lab.sh physics-flat` (-accar ferrari_458): ResetTo height = chassis static CG, 4 wheels, ride height (< 1 cm), no creep, hand-set
   inertia, drawn tyres on the plane (< 2 cm), steering wheel updated in the frame; pictures physics-flat-rest-side / -front / -rear34 /
   -frontwheel (wheels in the arches? body height vs AC); 0-100 / 100-0 / lateral vs the harness +- tolerance; straight line; park / occupy
   (excludeLayers 2|7|24, solid after 1 s) / freeze (revs) / 60 km/h limiter + PitLimiter light / puppet teleport + MovePosition.
2. `lab.sh physics-flat -accar abarth500`.
3. `lab.sh physics-458-magione -actrack magione`: pit box 0 from the floating marker, surface keys, 6 s drive.
4. `lab.sh physics-fleet`: 86 cars: CG vs chassis static, tyre gaps, wheels, hull count (one ok/BAD line each).

## Requests
Answered: F, S, R, C, A, N, I -> P (first pass); F -> P tyre compound / blankets (second pass). Filed (second pass): P -> N (TravelM offset
range, puppet MovePosition), P -> F (ReapplySetup, ROD/TOE setup units, TC/ABS levels), P -> A (grind on refused H-shifter changes).
Earlier: P -> lead/W (hulls collide with layer 14), P -> all (API list).

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3, check.sh all 117 proven; harness builds; no lab run)
- AutoShiftStep: effective upshift = min(AUTO_SHIFTER UP, limiter in force - 50 rpm (AutoShiftLimiterMarginRpm, ours)); the limiter in force
  includes the garage ENGINE_LIMITER fraction. The downshift check uses the same point.
- ApplyLocalSetup: an untouched garage row uses P's own SetupItem.Default (F's row default is rounded / clamped for display): no phantom
  FRONT_BIAS override at spawn (bmw_m3_gt2 66.5 vs 66, lotus_49 62.5 vs 62), so ctrl_ebb is not switched off by it.
- AcCarRig.Spawn: new AcCarRigOptions.Handle (AcCarSpawnHandle: Root, Cancel(), Cancelled, Finished). Root destroyed or cancelled mid-build ->
  Spawn keeps stepping C's builder until it disposed its half-built model, disposes a model that still came back, then done(null). Callers must
  never StopCoroutine a running Spawn (R, I and F use the handle now).
- Not verified in a lab.

## Polish pass 2026-11-01 (acceleration "~1.28x slow" + magione drive; build.sh 0 errors x3; NO change to the physics core)
Verdict: no systematic modelling error found; nothing was tuned (no fudge factor). Evidence (harness on the real install, P/harness):
- Energy balance, 458 2nd gear 97 km/h (psim trace): clutch 385 Nm x 898 rad/s = 346 kW in = 308 kW traction (Fx 11.4 kN x 27 m/s) + 23 kW
  tyre slip (sr 0.075) + 8 kW wheels / gearbox spin-up + 3.5 kW rolling = 343 kW. The integrator conserves energy; no hidden loss.
- power.lut is used as WHEEL torque, no drivetrain loss applied anywhere (AcCarCore: te = gas*lut*(1+boost) + (1-gas)*coast, clutch torque
  to the wheels x ratio). The data says the lut already IS wheel torque: lut peak / ui_car.json crank = 458 466/540 0.86, abarth 205/230 0.89,
  e30 211/240 0.88, gt86 184/205 0.90, miata 135/152 0.89. No double loss.
- Inertias: engine INERTIA on the crank (clutch impulse solve = reflected x ratio^2 when locked), gearbox INERTIA x total ratio^2 on the
  driven wheels, tyres.ini ANGULAR_INERTIA per wheel (rim+tyre+disc per the formula_k comment). Units kg m^2 throughout. Removing the engine
  inertia makes the fleet SLOWER (1.33: the flywheel energy helps after each upshift); wheel inertia x0.01: 1.25.
- New `pideal <car|all>` (ProbeP.cs): analytic 0-100 from the data alone (best gear lut x max boost x ratio / r, capped by peak tyre Fx
  with load transfer, drag + rolling, mass + all inertias reflected, instant shifts, traction-limited launch). Fleet 1.089 x ui (n 64);
  458 3.89 s (0-200 10.4 = the real car), abarth 7.24, e30 6.79, gt86 7.65, miata 9.56, GT-R 3.40 vs ui 2.1 (bad ui value). So even a
  perfect driver on this data is ~9 % slower than the brochure numbers; the 458 is traction-limited (rear Fx peak ~1.2 x load = 0.73 g mean).
- Decomposition (accel_decomp.txt, fleet mean vs ui): base psim 1.280 -> instant shifts 1.166 (AC's own CHANGE_UP_TIME 250-300 ms + AUTO_SHIFTER
  GAS_CUTOFF_TIME 0.30 s on the road cars, turbo boost decaying with LAG_DN during the cut) -> + manual launch (clutch slipped at 55 % of
  the limiter) 1.146 -> analytic ideal 1.089. TC (PSIM_TC=0) changes the fleet by +0.01 (458 4.11 -> 4.06).
- Launch: AC's [AUTOCLUTCH] MIN_RPM..MAX_RPM window ("Minimum/Maximum rpm for autoclutch engadgement", formula_k; changelog "Fixed Alfa
  Romeo 155 autoclutch range for 1st gear starts", "autoclutch ... antistall") = an rpm-scheduled clutch; ours = MAX_TORQUE x engagement(rpm),
  so a full-throttle start sits where lut torque = clutch capacity (458 1334 rpm / 270 Nm: that launch is ALREADY at the tyre limit, 0.75 g;
  a 4000 rpm launch is slower, 4.55 s). Costs most on AWD / traction-rich cars (GT-R base 4.27 vs manual launch 3.48). Kept: nothing in the
  files or AC's notes says the autoclutch lets the engine flare; a rate-limited autoclutch would be an invented number.
- Shift timings are the car's data (458 40 ms + 0.13 s auto cut; road cars 250-300 ms + 0.30 s). Upshift = min(UP, limiter - 50).
- Brochure 0-100 assumes launch control / ideal technique; ui_car.json stays read-only reference.
New harness hooks (psim, env, harness only): PSIM_SHIFT0, PSIM_EI, PSIM_WI, PSIM_DX, PSIM_LAUNCH, PSIM_CLUTCH, PSIM_HUMAN, PSIM_GAS.
Lab (polish pass): `physics-flat -accar abarth500` PASS, every check, numbers identical to the earlier lab (0-100 9.04 s, 100-0 35.3 m,
0.98 g, straight 0.03 m; picture rest-side: tyres on the plane, wheels in the arches). `physics-458-magione` (2 runs, deterministic, now
traced every 0.25 s + impacts + floor-box gaps + a 20 s steered stint):
- The "54 m / 15 km/h" is EXPLAINED, not a physics fault: the car drove dead straight (heading 0.0, yaw 0.00) out of pit box 0 matching
  the harness (lab 64 km/h / 32 m at 4.0 s, psim at pedal 0.6 64.8 km/h), left wheels on GRASS at 47 m / 75 km/h (the pit lane bends,
  no steering) and hit "08WALL" at 60 km/h (root-local (-0.88, -0.31, 2.05)), spun -101 deg. Check stays "ok".
- Steered stint (fast lane from grid 0, lab pure-pursuit driver, ours) FAILS (415 m, max 134 km/h, 34 impacts, off-line 15.5 m): (1) the
  colliders.ini floor box "Ac Floor 0" scrapes 01TARMACC* at the front-left corner (-0.87, -0.35, +1.4) under LIGHT braking (pedal 0.18-0.46)
  into T1: floor gap FL/FR/RL/RR 137/136/112/113 mm at 134 km/h -> 36/41/63/66 at brake 0.33 -> 3/27/43/69 at 0.46; CG height 456 -> 371-390
  mm (static 425); travel x of all FOUR wheels +30..+60 mm while the telemetry loads only shift ~1 kN front-to-rear (= I's GT86 report,
  same braking zone s ~160). Not resolved here (needs the chassis travel/force consistency checked on flat ground vs this undulating
  road); (2) understeer in the T2 hairpin at 0.84 g (front slip angle 12 deg, lab driver over-steering); (3) a spin at 97 km/h after the
  lab driver put the left wheels on GRASS/CURB: both rears airborne ("air") for one sample, landing with rear slip angles 14 -> 27 -> 54 deg.
  No exceptions in lab.log. Lab runs used: physics-458-magione 2, physics-flat 1.
- From grid at full gas the 458 spins the rears (sr 1.4-1.6 at 10 km/h): TC is off below electronics.ini MIN_SPEED_KMH 25 (AC's rule).

## Review of the polish pass 2026-11-01 (lead reviewer): suspension travel rate fixed (the hull / floor-box dive)
- ROOT CAUSE of "I -> P: hull hits the road under braking" and P's own "floor box scrapes under light braking": `AcChassis.Step` took
  the damper speed as xdot = -(pv . up). On a body pitched against the road the forward speed leaks into that (nose down 1.5 deg at
  100 km/h = 0.7 m/s of fake REBOUND on every corner): the dampers pulled the loads down, the springs compressed to make up for it
  and the whole car sank. Proof: tyre load != spring force (GT86 front X +90 mm at only +600 N with a 23000 N/m wheel rate).
- FIX (Data/Physics/AcChassis.cs, Step pass 1): xdot = -(pv . n) / (up . n) with n = the mean contact normal (the road plane under the
  car; the ray length is h / (up . n)); past ~78 deg (up . n < 0.2 // ours) the old body axis. Pure kinematics, no new constant.
- Harness `revbrake <car> <pedal>` (new DevTools/harness-ac/ProbeRev.cs, 110 km/h, flat): ks_toyota_gt86 pedal 0.65 CG 0.409 ->
  0.297 m before, 0.394-0.407 after (front X +40 mm / rear -45 mm, loads now = springs); ferrari_458 pedal 0.4 CG 0.425 -> 0.385
  before, 0.419-0.422 after.
- Fleet (pchassis all, 86 cars, vs pchassis_all.txt 03:14): 0-100 x0.999, 100-0 x0.989 (16 shorter by > 3 %, 2 longer: abarth500
  37.0 -> 38.0, ks_audi_a1s1 37.9 -> 39.0), steer-ramp g x0.987; ks_alfa_33_stradale UNSTABLE -> STABLE (top 35 -> 272); tops that
  were cut short now reach (laferrari 257 -> 361, quattro rally 159 -> 277, ruf 226 -> 322). psim all unchanged (core only, fleet 1.28).
  aichassis (lap 2): gt86 magione 1:40.490, gt86 imola 2:28.469 0 resets, 458 magione 1:31.901, abarth500 magione 1:37.153.
- Lab physics-458-magione (steered stint, same driver): braking into T1 at pedal 0.28-0.52 CG 410-423 mm (was 456 -> 371), min floor
  gap 32 mm (was 3), no road impacts in the braking zone; impacts 34 -> 10, all on GRASS / CURB / WALL after the lab driver's off-line
  excursions (the stint still FAILs on the spin after the T2 hairpin - lab-driver understeer, as before).
- Lab ai-magione (GT86): PASS, lap 1:40.634 x1.062, 0 recoveries, 0.00 m out; impacts 25+ (every braking zone) -> 2, both on a kerb at
  s 845-854. Not re-run: ai-race, physics-flat, physics-fleet, cars-*.

## Last-gaps pass 2026-11-01 (integration re-runs after the review's road-plane damper fix; build.sh 0 errors x3, check.sh all 117 proven)
- Harness pchassis AFTER the fix (re-run now): abarth500 0-100 8.76 s / 100-0 38.0 m / 0.94 g (before 8.76 / 37.0 / 0.98), static CG 0.508
  unchanged; ferrari_458 4.18 / 35.4 / 1.07 (before 4.18 / 35.5 / 1.07); bmw_m3_e92_drift settle CG 0.407 = static, unchanged.
- Lab physics-flat -accar abarth500 (1 run): PASS every check. CG 0.508 = static (pitch -0.65 deg), 4 wheels, no creep, drawn tyres
  0/0/0/0 mm, steering wheel 207/207 deg, 0-100 9.05 s (was 9.04), top 216, straight line 0.00 m / 0.00 deg (was 0.03 m), 100-0 35.9 m
  (was 35.3; harness now 38.0 -> Unity 2.1 m shorter, before 1.7 m shorter vs 37.0), lateral 0.95 g turns RIGHT (was 0.98; harness now
  0.94), park / occupy / freeze 6457 rpm / limiter 60.2 km/h + light / puppet teleport + MovePosition. Verdict on the 38.0 m: the review's
  offline number is real (+1.0 m in the harness) and Unity follows it in the same direction (+0.6 m); no regression, within +-3 m.
  Pictures LOOKED at: rest-side, rest-rear34 - tyres on the plane, wheels in the arches (white car now, no purple hub cast).
- Editor/AcLabPhysics.cs: the harness references updated to the post-fix pchassis numbers (REF_500_BRAKE 37.0 -> 38.0, REF_500_LAT 0.98 ->
  0.94, REF_458_BRAKE 35.5 -> 35.4; tolerances unchanged). Re-run with them: PASS, identical numbers ("35.9 m vs harness 38.0", "0.95 g vs 0.94");
  one run in between hung to its 1500 s timeout on another agent's transient compile error (Game/Ai/AcAiTraffic.cs AcAiDriver.Blocked, gone now).
- Lab physics-fleet (1 run, FIRST Unity run of this scenario ever; the "86/86 settle" in INTEGRATION was the harness, which has no hull):
  FAIL 85/86 ok - BAD bmw_m3_e92_drift: CG 0.418 vs static 0.407, speed 0.028 m/s after 2 s (tyre gaps 0 mm, 4 wheels). Siblings ok but
  marginal: bmw_m3_e92_s1 +9 mm / 0.001 m/s, bmw_m3_e92 +3 mm / 0.011 m/s; every other car 0 mm / 0.000.
  ROOT CAUSE (not the damper fix - the static CG is unchanged): C's convex hull from collider.kn5 reaches the ground at the static ride
  height. Scratch probe (collider.kn5 lowest vertex vs the static ground in model space = -GRAPHICS_OFFSET.y - static CG): e92_drift 0.058 vs
  0.063 (-5 mm, penetrates), e92_s1 0.058 vs 0.062 (-4 mm), e92 0.058 vs 0.053 (+5 mm, inside PhysX's default 10 mm contact offset); next
  cars +21 mm (c7) and more. PhysX pushes the body up on the hull -> CG +11 mm and a slow jitter. Kunos (sdk formula_k colliders.ini): "the
  3D collider also does collisions properly" - so collider.kn5 meeting the ground is AC behaviour too; whether AC's e92 sits 5 mm higher
  (our static CG vs AC's) is not known. NOT fixed (no basis for a number); REQUESTS "integration -> P / C".

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN.md; build.sh 0 errors x3, check.sh all 117 proven)
Files: NEW `Data/Physics/AcCarDamage.cs` (AcDamageZone, AcRepair, AcDamageChange, AcDamageAccumulate, AcDamageRules, AcDamageSpec, AcCarDamage),
NEW `Game/Physics/IAcCarDamageVisual.cs`; changed `AcCarSpec.cs` (Damage, wing zones, P0), `AcCarSpecParts.cs` (AcWingSpec zones), `AcAeroModel.cs`
(Wing overload clDiv/cdMul), `AcChassis.cs` (AcChassisWheel ToeLen/RodOffset/UpdateToe), `AcCarCore.cs` (Damage, engine life, dead engine),
`Game/Physics/AcCarPhysics.cs`, `AcCarRig.cs` (DamageRate at spawn), `Editor/AcLabPhysics.cs` (physics-crash, physics-engine); lead part:
`Game/Shared/AcEvents.cs` (CarDamaged, CarCollision + Raise*), README log lines. Harness: `DevTools/harness-ac/ProbeDmgP.cs` (dmgspec,
dmgunit, dmgsim, dmgturbo; also linked in P/harness), ProbeP.cs psim/pchassis attach `HarnessDamage` (env PDMG %, default 0).
Public contract (section 9, all present): AcCarDamage(spec, rules), Zone[5], RodOffset[4], EngineLife, EngineDead, Version, Multiplier,
AddImpact, StepEngine, ZoneByte, SuspDamage01, BodyPct/SuspPct/EnginePct, RepairSeconds, Repair, ToWire/ApplyWire, TakeChanges; AcDamageRules
Load/Current; AcDamageSpec; AcCarPhysics.Damage / DamageRate / PuppetVelocity / Repair(what) / DebugAddImpact(z, kmh). ADDED (additive):
`AcCarDamage.MechVersion` (float changes -> aero/toe refresh), `Retired` (R sets, d1 bit 31), `EngineSmoking`, `MaxSuspDamage01`, `Pristine`,
`On`, `TakeChanges(out kmh)`, `AeroFactors(wing, out clDiv, out cdMul)`, static `ToeDeg(axle, toeLen, rod)`, `PickZone(nx, ny, nz, rollover)`,
`Accumulate` (Sum/Max switch R2); `AcDamageSpec.HasRpmDamage/HasPitStop/From(...)`; `AcDamageRules.Loaded/EngineSmokeIni/Root/Source`;
AcCarPhysics `PointVelocityPreStep(p)`, `DamageBox`, `DebugAddImpact(z, kmh, corner)`, `DebugAddImpactAt(bodyPoint, kmh)`.
Deviations from the design (closest correct thing):
- `AcDamageChange` lives in Data/Physics/AcCarDamage.cs, not AcEvents.cs: TakeChanges (Unity-free) returns it and the harness compiles Data/
  without Game/. Same namespace/members.
- The visual push calls C through `IAcCarDamageVisual` (C has no SetDamage yet; P cannot call a missing method). C adds `: IAcCarDamageVisual`.
  P pushes for every rig (incl. puppets after N's ApplyWire) on a Version change; P raises CarDamaged only for cars it simulates (N for remote).
- CarCollision is raised with CarImpact (same gate / km/h, both hits and 10 Hz scrapes); CarImpact itself byte-identical.
- [FIN_n] (1 car, all zone values 0) is not simulated (no fin model exists) - zero effect either way.
- Smoke push is on/off (1/0) while EnginePct >= 100 - ENGINE_LIFE (25 % life left reading, one line in AcCarDamage.EngineSmoking).
- Wire engine % = ceil(EnginePct) but a LIVING engine is sent as at most 99 (100 = dead only), so a remote never sees a false blow-up.
How it runs: FixedUpdate caches v0/w0/com0, sets Damage.Multiplier = DamageRate, refreshes aero factors / toe only on MechVersion or
multiplier change; OnCollisionEnter/Stay -> DamageHit only when DamageRate > 0 (tag check first, then GetContacts into ContactPoint[16],
floor boxes skipped by reference, |(vA - vB) . n| with the other car's PuppetVelocity / pre-step point velocity / GetPointVelocity, gates 3 /
10 km/h + 0.25 s, zone from the damage box (hull bounds, model bounds when > 0.5 m off), nearest hub <= 1 m). AcCarCore sub-steps
StepEngine; dead -> gas 0 (no idle, no restart), Reset keeps it stopped; Repair(Engine) back to idle. Toe: rod 0 = the iteration-1 value
untouched (a corner's ToeDeg is rewritten only when its rod offset changed); damaged = setup toe + |ToeDegFor(rod)| clamped +-12.
Verified (harness on the real install, P/harness = same Data code the game runs):
- `dmgspec`: 86 cars, 291/291 wings with ZONE_ keys + 1 fin, susp 83 (not the 3 quattros), engine rpm 86 / turbo 84 (not miura_sv, 787b),
  PIT body 20 x84 / 5 x2, engine 2 x85 / 6 x1, susp 30 x86; parsed zones = ini 0 mismatches; 0 damage problems (all problems 8 on 5 cars =
  iteration 1). Rules read: GLASS_THRESHOLD 0.65, TIME 4, MAX_DAMAGE_KMH 60, smoke 25 / 15 s, IS_ACTIVE 1, DAMAGE_MULTIPLIER 100. PASS.
- `dmgunit`: 45/45 (m 0 = nothing incl. wire 0/0; sum / cap 255 / MAX switch; raw zones at m 0.5; rod GAIN*(v-MIN)*m + cap; engine loss and
  death; %; repair seconds; Repair semantics; wire round trip, 0/0, dead 100 vs living 99; aero factors incl. negative CL and Wing(...,1,1)
  bit-identical; toe identity at rod 0, always toe-out, clamp 12, 2 cm guard; zone pick; smoke reading).
- `dmgturbo`: 31 turbo cars, P0 changes 5 (quattro_rally 1.50->1.35, 1m_s3 / z4_drift 1.38->1.104, lotus_98t 4.0->2.4, ruf_yellowbird
  1.42->1.136; iteration 1 had 4 of them ABOVE their threshold), 0 above TURBO_BOOST_THRESHOLD at default; free-rev 5 s loses 0 life on all 31.
- `dmgsim all`: toe clamp needed on 326 corners at MAX_DAMAGE, 0 violations (<= 12 deg fleet-wide); 458 after F100/R40/L60/R100/C30: toe RF
  8.13 / LR 6.59 deg, worst wing clDiv 2.50 / cdMul 3.10; money shift (2nd at 95 % limiter -> 1st, 1 s, core alone): 458 peak 12327 rpm ->
  life 592; 32 cars blow up (clutch slip caps the peak on others).
- Damage 0 = identical: `pchassis all` + `psim all` before vs after the whole pass (frozen copies, PDMG unset = damage attached at m 0):
  81 cars byte-identical; only the 5 P0 cars differ (psim 0-100 / top: quattro_rally 3.59 -> 3.73 s, 1m_s3 5.99 -> 5.96, z4_drift 5.11 ->
  5.30 s / 310 -> 297, lotus_98t 3.10 / 295 -> 3.10 / 286, ruf 4.96 -> 5.12); fleet line unchanged.
Not verified: C's visuals, N's wire,
R's repair flow. // ours numbers: zone cap 255, life 1000, gates 3 / 10 km/h + 0.25 s, contacts 16, roof box 0.7, hull/model 0.5 m,
corner 1 m, toe clamp 12 deg, Sum accumulation, smoke on/off at 25 % life, pit sum, living engine <= 99 % on the wire.
Player.log: `Physics: damage rules from ...`, `Physics: <car> damage box from the hull|model bounds ...`, `Physics: <car> first damage
(multiplier n %): zones F/R/L/R/C ...` (+ "(car model has no damage visuals yet)" until C implements IAcCarDamageVisual).
Lab (2 runs, 2026-11-02): `physics-crash` PASS every check - 458 damage box from the hull (2.32 x 1.19 x 4.60); DamageRate 0: front 40 adds
nothing, 0 CarDamaged; front 20/40/60/100 -> FRONT +20.0/40.0/60.0/100.0, others 0; rear 40 -> REAR 40; left 40 -> LEFT 40; m 0.5 front 60 ->
raw 60; scrape 100 km/h 2 s -> 3.6 (one enter hit); upside-down drop -> CENTER 28.2; ResetTo keeps; Repair(All) clears + one CarDamaged
(Repaired). Pictures looked at (front60-side, roof-side): car at the wall / on its roof; no damage visuals yet (C). Observation: a flat
frontal wall hit puts the mean contact at the bumper centre, > 1 m from both front hubs, so it bends no steer rod (design's 1 m rule);
only off-centre / corner / side hits near a wheel do. `physics-engine` PASS - 458 money shift peak 10720 rpm, life lost 189.2 vs 188.8 from
the 50 Hz rpm samples; life 0 -> EngineDead, GasEffective 0 at full pedal, coasting in gear; ResetTo keeps it stopped (0 rpm);
Repair(Engine) restarts; bmw_z4_drift full throttle 15 s: peak total boost 1.104 < 1.11, life 1000 (P0 holds in Unity).

## Damage pass 2026-11-02 (integration)
- `AcCarPhysics.Impact`: returns right after `RaiseCarImpact` when nobody subscribes to `CarCollision` (`AcEvents.CarCollisionWanted`, new, lead):
  no other-car lookup at DAMAGE 0 / while A2 and sparks are not built. CarImpact itself unchanged.
- Traced and lab-proven (`damage-e2e`, ferrari_458): a PhysX wall hit 60 km/h -> FRONT 60.0, CarDamaged 1x (Zones, 60 km/h), push -> C, aero clDiv /
  cdMul 1.90 on a front-zone wing, Repair(All) restores factors 1 and toe; DAMAGE 0 -> Version unchanged, no event, wire 0 0.
- Open (user to judge): head-on wall hits still bend no rod (mean contact point > 1 m from both front hubs; rule `CornerMaxM` // ours).

## Damage pass 2026-11-02 (crash lab area)
- `Editor/AcLabPhysics.cs` `physics-crash` extended (only file changed): R's gate (`AcCarDamageGate.Refresh(DAMAGE 100)`) before the spawn so
  the pictures carry C's paint dents (was: no damage textures, and the `-front` / `-rear34` cameras sat inside the wall = grey pictures);
  `CrashShots` moves the car 50 m off the wall (ResetTo keeps damage) and shoots front34 / rear34 / left / right; a 30 cm post (PostX 70 //
  ours) for a 50 km/h side hit at the LF hub (steer rod) at DamageRate 1 and 0.5; `Perf` coast-down 250 km/h 5 s + 200-0 before / after the
  front 60 hit; 0.5 checks the aero halving (clDiv-1, cdMul-1).
- Lab run 2026-11-02 16:02 PASS, all 20 checks: F20/40/60/100 -> FRONT exact; R40, L40 exact; front 60 worst wing clDiv 1.90 / cdMul 1.90;
  coast loss 19.92 -> 24.58 km/h (drag), 200-0 133.99 -> 133.74 m (more drag outweighs the lost front downforce - shorter, not longer);
  post 50 -> LEFT 49.4, rod LF 9.4 mm = GAIN x (v - MIN) exactly, others 0, LF toe 0.00 -> 3.85 deg toe-out; at 0.5: 4.7 mm, 1.93 deg (ratio
  0.500); 0.5 aero 0.900 -> 0.450; scrape 3.6; roof -> CENTER 28.2; ResetTo keeps; Repair(All) clears + 1 event.
- Pictures looked at: front60-front34/left (front scratched + dented, bonnet edge lifted), rear40-rear34 (rear bumper scratched), left40-left
  and post50-left (left side scratched/dented, left window cracked), roof-front34 (clean: CENTER 28 is below the windscreen threshold; the
  roof is outside the paint mask). Old 15:11 pictures moved out of the lab folder.
- Damage 0 = iteration 1: `physics-flat -accar abarth500` all checks identical to 2026-11-01 except first upshift / 0-100 4.01/9.05 ->
  4.00/9.04 s (the known lab jitter, earlier passes toggled 9.04/9.05); pictures look identical. `cars-458` (gate off): 6 exterior
  pictures byte-identical to 2026-11-01, door/cockpit pictures differ by a few bytes, paint smooth (looked at doorL_0).

## Damage pass 2026-11-02 (fixer)
- AcCarPhysics contact damage: Unity calls back once per collider PAIR, so DamageHit now only gathers (max vImp of the step with its zone/corner, from the max pair's mean point) and FlushDamageHit applies ONE AddImpact at the top of the next FixedUpdate (design 1.3 step 4). Repair() drops a gathered hit.
- Contacts with the OTHER car's 'Ac Floor n' boxes are skipped (IsFloorBox is internal now; design 1.3 step 2).
- Enter gate: an Enter within StayGapS (0.25 s) of the last counted hit counts only when >= StayMinKmh AND harder than that hit (_lastDamageKmh) - flickering puppet contacts no longer add up.
- Stand-in boxes: vB = AcRemoteContact.Velocity (N writes the drawn velocity; the box's kinematic body reads 0).
- New public field PuppetGhostable (N sets it while a remote car is 'special': just placed / in the pit lane). A car that is NOT the local car (the master's AI) takes no damage from such a puppet or stand-in box (design 1.3 step 9). Not covered: the AI car itself being in the pit lane (P has no pit-lane state for AI) - deviation.
- AcCarDamage.EngineSmoking uses the quantised EnginePctWire() on every client (design 1.2), so owner and remotes switch smoke together.
- AcCarDamage.ApplyWire per field: any zone / rod / engine value going DOWN sets Repaired; Zones/Suspension only for increases (partial pit repair, BACK TO PITS of a retired car = 0 / 1<<31 reach remotes as Repaired).
- Red line (design 3.3): telemetry LimiterRpm stays the LIMITER (audio / limiter semantics); H computes the shift-light red line itself (see H NOTES).
- Verified: build.sh 0 errors; harness dmgunit 49/49 (4 new checks: smoke owner==remote at 74.3 %, body-only wire repair, retired-pits wire repair, new hit after it); lab damage-e2e (see report).
