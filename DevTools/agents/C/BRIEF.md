# C - Car model (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
Build any owned AC car in Unity from its kn5 + skin + data.acd visual inis, with every animated part named and driven through one
API used for the local car, remote cars, AI cars and the showroom car: wheels (spin, steer, suspension travel), rim blur swap, steering
wheel, doors (ksanim), wipers / shifter / paddles (ksanim), lights groups (lights.ini, emissive), analog needles and digital displays,
cockpit HR/LR switch, collider.kn5 hull, optional AC driver model.

## You own
`Game/Cars/` (stub `AcCarModel.cs` - fill it; add `AcCarParts.cs`, `AcCarLights.cs`, `AcInstruments.cs`, `AcKsanimPlayer.cs`, ...),
`Editor/AcLabCars.cs`.

## Port from
- W's `AcKn5Builder` (Game/World) for the mesh/material/texture build - do not write a second kn5 builder; ask W in REQUESTS.md for
  what you need (until W delivers, a minimal private builder is OK if you delete it when W's lands).
- `ETS/EtsInstruments.cs` (1625) + `ETS/Ets/EtsInstrumentKit.cs` (1308): needles / digital displays pattern; `ETS/EtsVehicleLampDriver.cs`
  + `ETS/Ets/EtsVehicleLamps.cs`: light groups + emissive switching; `ETS/EtsTruckRig.cs` (740): one built vehicle, parts by name.
- `ETS/EtsSteeringGrab.cs` `MeasureRim` (l.477) - only for the readable rim mesh S needs (S owns the grab).
- Data: `Kn5File`, `AcdArchive.ReadIni("lods.ini"|"lights.ini"|"blurred_objects.ini"|"digital_instruments.ini"|"analog_instruments.ini"|
  "driver3d.ini"|"car.ini")`, `Ksanim.Read` (`car_DOOR_L/R.ksanim`, `car_wiper.ksanim`, `car_shift.ksanim`, `steer.ksanim`), `Knh.Read`.

## Spec sections
`research/ac_car_formats.md` all, especially 1.4-1.6 (node names), 3 (ksanim), 4 (driver, knh), 5 (LODs, collider), 6 (skins), 7 (recipe)
and the **Critic addendum C1 (spaces: model vs physics, GRAPHICS_OFFSET, PITCH, DRIVEREYES), C2 (UVs), C3 (STEER_HR axis + sign)**.
`research/ets_vehicles.md` 3.6, 6.3 (hands targets), 9.4 point 1 (colliders, inertia lesson).

## Must do (acceptance)
1. `AcCarModel.BuildAsync(car, skinId, options, parent, done)` works for all 86 owned cars (harness: header-level check of nodes for all;
   lab: build at least ferrari_458, abarth500, ks_mazda_miata, tatuusfa1, ks_ford_mustang_2015). Skin textures override kn5 textures by name.
2. Model space = AC model space mirrored (Unity x = -AC x), origin on the ground (C1). Expose `DriverEye` (DRIVEREYES, model space),
   `DriverHips`, `HandL/R` (knh RIG_HAND_L/R), `PassengerHips` (mirror of the driver across x = 0 when the car has 2 seats - mark // ours),
   `SteeringWheel` + `SteeringRest`, `DoorL/R`, `Wheels[4]` with radius (tyres.ini RADIUS) and rest pose.
3. `SetSteer(deg)` = restLocal * Euler(0, 0, -deg) on STEER_HR (C3); `SetWheelState(i, spin, travel, steer)` moves WHEEL_xx/SUSP_xx/DISC_xx;
   RIM_xx <-> RIM_BLUR_xx by speed per blurred_objects.ini; `SetDoor(side, t)` samples car_DOOR_x.ksanim; `SetLights` toggles lights.ini
   groups (headlights, brake, reverse); `SetInstruments` drives ARROW_* needles (analog_instruments.ini ZERO/STEP/LUT) and the digital
   display (digital_instruments.ini; bitmap fonts from content/fonts - coordinate the font reader with H, who owns Data/Hud).
4. Hull: collider.kn5 convex MeshColliders on layer 28; sanity check vs LOD_0 bounds (fallback box if the centre is > 0.5 m off - the
   mustang bug, C1). Never touch Rigidbody inertia (P does).
5. Cockpit HR/LR switch by lods.ini DISTANCE_SWITCH; `SetViewer(insideCockpit)` hides the AC driver model / shows COCKPIT_HR.
6. Remote / AI cars: LOD 1+ option and no interior; cheap per-frame update (no allocation in Set* methods).
7. `Dispose()` frees meshes/materials/textures (no leak after 20 spawn/dispose cycles - log texture memory).

## Player.log lines to add
`Car: built <id>/<skin> LOD n in X ms (meshes, tris, textures MB, hull k colliders, doors L/R, steer yes, lights n, needles n)` ·
`Car: <id> hull rejected (off by X m), using the LOD_0 box` · `Car: <id> missing node <name>` (once per car).

## Verify
build.sh x3, check.sh; harness `Cmd_carparts` over all owned cars (node presence census: WHEEL_xx, STEER_HR, DOOR_L/R, lights.ini
names found); lab `cars-458` etc.: shots front 3/4, cockpit from DriverEye, steering at -90/0/+90 deg, door at 0/0.5/1, brake lights on.
LOOK at them (paint on the right side, wheels in the arches, steering turns the right way).

## Consumes / provides
Consumes: AcKn5Builder (W), AcContent/AcdArchive/Ksanim/Knh (D), AcLightState/AcInstruments (lead). Provides: `AcCarModel` to P
(AcCarRig places it at GRAPHICS_OFFSET), S (seat/eye/hands/steering wheel/doors), N (remote cars), I (AI cars), F (showroom).
