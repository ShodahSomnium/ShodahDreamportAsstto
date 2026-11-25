# P - Physics (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
Make every owned car drive like AC from its own data.acd numbers: parse the physics inis (Unity-free `AcCarSpec`), and run an
AC-like vehicle model in Unity (`AcCarPhysics`: Rigidbody at the CG, raycast/spherecast wheels with tyre sub-steps, AC tyre brush
model, drivetrain + clutch + gearbox (sequential, auto-shift, auto-clutch, autoblip), LSD, aero, brakes, ABS/TC, fuel, surfaces), plus
the ONE car factory `AcCarRig` (root + C's model + your physics + A's audio) used by R, I, N and F.

## You own
`Data/Physics/` (stub `AcCarSpec.cs`), `Game/Physics/` (stubs `AcCarPhysics.cs`, `AcCarRig.cs`; add `AcTyre.cs`, `AcDrivetrain.cs`,
`AcAero.cs`, `AcWheel.cs`...), `Game/Shared/AcLocal.Physics.cs`, `Editor/AcLabPhysics.cs`, harness probe `DevTools/harness-ac/ProbeP.cs`.

## Port from
- `ETS/EtsTruckController.cs` (2387): keep the SHELL (Rigidbody, body box excludes, Driven/Puppet/PlaceOnGround/ApplyPuppet,
  fall-through recovery, wheel ray 12 cm above the hub, inertia set ONCE after all colliders, Interpolate while driven, puppet
  interpolation None); REPLACE its truck physics. `ETS/EtsTruckRig.cs` (740) + `EtsLocalTruck` (l.672) for the rig / local-car pattern.
- `HALO/Game/Vehicles/HaloVehicle.Drive.cs` for another proven Rigidbody vehicle step.
- Data: `AcdArchive.Open(car.Dir)`, `ReadIni`, `ReadLut`, `AcLut.Eval`, `AcIni.GetInlineLut`.

## Spec sections
`research/ac_physics_sound_rules.md` 1.1-1.10 (every ini, the recommended model with formulas and validation targets) + Critic addendum
(axle positions, CG height, DRIVEREYES model space, brakes per-wheel, no pit limit key). `research/ets_vehicles.md` 2 (layers), 9 (all),
12 (lessons 1-5, 12, 17). `research/ac_car_formats.md` C1 (spaces / GRAPHICS_OFFSET / PITCH), 5 (collider.kn5 / colliders.ini).
`research/ac_track_formats.md` 3.4 + 6.1 (surfaces: FRICTION, DAMPING, SIN_HEIGHT), T1 (spawn raycast).

## Must do (acceptance)
1. `AcCarSpec.Load(car)` parses car.ini, engine.ini + power.lut (+ turbo, coast), drivetrain.ini (gears, final, diff, clutch, auto-shifter,
   autoblip, cutoff), suspensions.ini, tyres.ini (VERSION=10, front/rear compounds, the default compound), aero.ini (wings + LUTs),
   brakes.ini, electronics.ini (ABS/TC luts), fuel, setup.ini ranges (for F's garage). For all 86 owned cars with `Problems` empty or
   explained (harness `Cmd_spec` census: print mass, limiter, gears, steer lock/ratio, tyre radius per car).
2. `AcCarRig.Spawn(car, skin, options, done)`: root GameObject on layer 28 at the CG, `AcCarModel` (C) under it at
   (-off.x, off.y, off.z) with Euler(GRAPHICS_PITCH_ROTATION, 0, 0), hull colliders, `AcCarPhysics.Init`, `AcCarAudio.Attach` (A) when
   `options.Audio`. Kind Remote = puppet (kinematic, no wheel forces), Kind Showroom = no physics, Kind Ai = like Local.
3. `AcCarPhysics`: mass TOTALMASS + fuel*0.75 (// ours), inertia from INERTIA box, sub-stepped tyres (~333 Hz, never change
   `Time.fixedDeltaTime`), AC brush tyre with DX/DY refs, load sensitivity, relaxation, falloff; drivetrain with clutch/engine inertia,
   rev limiter (LIMITER_HZ), idle; sequential + auto-shift (AUTO_SHIFTER UP/DOWN), CHANGE_UP/DN_TIME cut, autoblip; diff
   (PRELOAD/POWER/COAST); aero at wing positions; brakes (MAX_TORQUE, FRONT_SHARE, handbrake); ABS/TC per electronics.ini and `Assists`;
   surfaces from `AcSurfaceTag.Of(hit.collider)` (FRICTION x grip, DAMPING drag, SIN_HEIGHT bumps); steering = input * STEER_LOCK /
   STEER_RATIO (absolute VR wheel angle when `Input.SteerAbsolute`), speed-sensitive rate for keys (ETS 4).
4. Modes and occupancy: `SetDriven`, `SetOccupied` (excludeLayers |= AvatarExcludes on every car collider while somebody sits in / rides;
   keep see-through 1 s after getting out - ETS lesson), parked = kinematic + solid + parking brake, `SetPuppet` + `ApplyPuppet` (wheels
   re-cast on the local road, spun from speed), `ResetTo(pos, rot)` (raycast down, T1; CG at hit + up * -GRAPHICS_OFFSET.y), `Freeze`.
5. Telemetry every FixedUpdate (rpm, gear, speed, pedals, steer wheel deg, boost, fuel, limiter, abs/tc, LatG/LongG, per-wheel spin,
   travel, slip, load, skid, surface); for local + AI cars push it each frame to `Model.SetSteer/SetWheelState/SetLights/SetInstruments` and
   `Audio.Feed`; copy the local car's into `AcLocal.Telemetry`. Raise `AcEvents.CarImpact` (OnCollisionEnter, km/h) and `GearChanged`.
6. Validation targets (log once per car in the lab): ferrari_458 full throttle in 1st reaches the shift ~2 s, 0-100 km/h 3.3-3.6 s,
   top ~325 km/h, ~1.3 g lateral, 100-0 km/h ~32-35 m; a 1.6-litre road car (abarth500) believable. Report the measured numbers.

## Player.log lines to add
`Physics: <car> spec: mass, inertia, CG height, gears, final, limiter, steer lock/ratio, tyre <compound> DX/DY, aero wings n, problems k`
(once per car) · `Physics: <car> driven: substeps n at fixedDeltaTime x` · `Physics: <car> fell through / recovered to <pos>` · validation lines in labs.

## Verify
build.sh x3, check.sh; harness `Cmd_spec` (all 86 cars); lab `physics-458-magione` (needs W's track - until then a flat test plane
built in your lab scenario): accel/brake/top speed/lateral numbers via `r.Check`, a shot of the car resting on its wheels at ride
height (wheels in the arches = C1 offsets right).

## Consumes / provides
Consumes: AcCarModel (C), AcSurfaceTag/AcTrackWorld (W), AcCarAudio (A), AcCarInput from S/I. Provides: AcCarSpec (S: steer lock;
A: limiter; F: setup ranges; I: ai.ini), AcCarPhysics/AcCarRig (R, S, N, I, F), AcTelemetry (A, H, N, C via instruments), AcLocal.Car/Telemetry.
