# S - Seat, cockpit and driving input (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/`, `MB/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
Everything between the avatar and the car: walking up to your car and opening its door (door handle on layer 16, the car's own door
ksanim + sound), sitting in (Bridge seat recipe), the carried body root every frame (heading-only VR view, `vr.horizonlock` default on),
the passenger seat (riders, ETS pattern), VR steering wheel grab with one or two hands, VR pedals / paddles, monitor keys + gamepad,
monitor cameras (cockpit DRIVEREYES, hood/dash, chase near/far, orbit) with the client camera hooks, own head hide, hands on the wheel
(monitor avatars, VR when not gripping), tablet guard while seated, getting out beside the car. You produce the local car's AcCarInput.

## You own
`Game/Seat/` (stub `AcSeat.cs` with `AcSeat` + `AcDriveInput` - fill it; add `AcDoor.cs`, `AcWheelGrab.cs`, `AcVrDrive.cs`, `AcCarCamera.cs`,
`AcSeatPoses.cs`, `AcRiders.cs`, ...), new files in `Game/Desk/` (monitor avatar bits), `Game/Shared/AcLocal.Seat.cs`, `Editor/AcLabSeat.cs`.
Existing lead files you USE (do not edit; ask in REQUESTS.md): `Game/Vr/AcPlayer.cs`, `AcXr.cs`, `AcTabletGuard.cs`, `AcLaserAim.cs`,
`AcBody.cs`, `AcFirstPerson.cs` (e.g. adding "Move", "Jump", "Sprint", "Gamepad Move" to BlockedActions while driving - request it),
`Game/Desk/AcAvatarFind.cs`, `AcAvatarCopies.cs`, `AcHeadHide.cs`.

## Port from (rename Ets/Halo -> Ac)
- `ETS/EtsTruckSeat.cs` (1329): TrySeat l.311 (order matters), KeepSitting l.926 (SetPose every seated frame), UndoPoseRecentre l.975 +
  UndoHandRecentre l.994, LateUpdate carry l.795, PlaceHead l.1063, GetOut l.664 + StandBeside l.701, Vanished l.1154, Release l.1255.
- `ETS/EtsTruckDoor.cs` (436): handle on layer 16, solid BoxCollider, XRSimpleInteractable + IPointerClickHandler + legacy mouse fallback.
- `ETS/EtsSteeringGrab.cs` (540): rim grab (AfterRigPlaced l.239, HandInRig l.442, MeasureRim l.477); replace WheelDegreesPerUnit by
  car.ini STEER_LOCK (AcCarSpec.SteerLockDeg); output `Input.SteerAbsolute = true; SteerWheelDeg`.
- `ETS/EtsVr.cs` (437): XR input, pedal arming, trigger capture, haptics. `ETS/EtsWheelHaptics.cs` (244): rim vibration from surfaces
  (VIBRATION_GAIN) + slip.
- `ETS/EtsTruckCamera.cs` (427): monitor camera + CinemachineBrain hold by name; `HALO/Game/Vehicles/HaloVehicleCamera.cs` (165).
- `ETS/EtsAvatarSync.cs` (533): HeadHide l.200, Remember/Carry l.298/312 (order 9990). `HALO/Game/Vehicles/HaloVehicleDirector.cs`
  (early -90 / late 10000 seat writes), `HaloVehicleSeat.cs` (Bridge calls l.87-93, 160-172, 347), `HaloVehicleInput.cs`, `HaloVehicleEntry.cs`.
- `HALO/Game/Vehicles/HaloSeatPoses.cs` + `HALO/Game/Desk/HaloPuppetPoser.cs` (ApplySeated l.351): hands on the wheel, targets = knh
  RIG_HAND_L/R rotated with the steering wheel (C's `AcCarModel.HandL/HandR`, `SteeringWheel`).
- `ETS/EtsRiders.cs` (1282) + `ETS/Ets/EtsRiderMath.cs` (358): passenger seat (seat request flow with N; passenger exit refused > 10 km/h).

## Spec sections
`research/ets_vehicles.md` 3-7, 8.4, 11, 12 (all lessons), 13, 14 + Critic addendum (seat/eye in body space, rim radius ~0.19 m, steer sign).
`research/ac_car_formats.md` 4.1 (knh), C1 (DRIVEREYES model space -> body), C3 (steering wheel axis/sign). `research/tablet_front.md` 5
(tablet guard, hold car input while the tablet is up). `research/halo_skeleton.md` 2.2 (HaloVehicleSeat recipe), 5.2-5.3.
DREAMPORT_GUIDE section 7 (grip pose = Euler(90,0,0), layers, tablet B/Y, monitor first person, avatar not under the Bridge body).

## Must do (acceptance)
1. `AcSeat.Start(game)` (R calls it), `Enter(rig, seat)`, `Exit()`, `ForceExit()`, `Stop()`, `Seated/SeatedCar/IsDriver`, events +
   `AcEvents.SeatChanged`; write `AcLocal.Seated/Driving/SeatCar/Seat/CameraMode/WorldPosition`.
2. Getting in: door handle target (layer 16) per side, usable on foot near the parked own car (or a free passenger seat of another
   player's car via N); desktop click / E key, VR trigger or grip; door opens (`Model.SetDoor`, `Audio.Door`), seat recipe, door closes.
   Call `rig.Physics.SetOccupied(true)` BEFORE the avatar arrives and `SetDriven(true)` for the driver (ETS order).
3. Carry: seat point = `ModelToBody(DriverHips)`; VR eye = `ModelToBody(DriverEye)`; root rotation = heading only; write early (-90) and
   late (10000); VR head placed at the eye with lean limit 0.45 m; monitor avatar carry (9990). No pitch/roll in VR ever (`vr.horizonlock`
   off may allow the car's pitch/roll for monitor only - VR stays heading-only; mark the option text accordingly).
4. Input producer `AcDriveInput.Produce(rig)` each frame while driving: VR right trigger throttle / left trigger brake (setting to swap or
   use the stick), paddles A = up, X or grip-click = down, wheel grab absolute angle; pedals armed only after both triggers released and
   disarmed while the tablet is up; monitor W/S or arrows, A/D, Q/E or Shift/Ctrl, Space handbrake, H lights, R reset, C camera, F/E exit
   (F is the client's Interact key - only while seated, document it); gamepad via the Input System by reflection (BO2 Bo2FirstPerson).
5. Monitor cameras: cockpit at DriverEye (near plane below the client's 35 cm, Halo's beginCameraRendering trick), hood, chase near/far
   (system/cfg/chase_cam.ini values), orbit; CinemachineBrain paused by name while you hold the camera; restored on exit.
6. Own head hidden in the cockpit (AcHeadHide); hands on the wheel for monitor drivers and remote viewers; VR hands free when gripping.
7. `AcTabletGuard.Update(true)` while seated (+1 s around in/out) so B/Y do not open the client tablet if you need them - or keep the
   tablet usable and disarm pedals (tablet_front 5.3 - pick one, explain in NOTES.md).
8. Getting out: only stopped (or < 10 km/h passenger), stand 1.6 m beside the door on the ground (`AcPlayer.Teleport`), release every
   Bridge lock (speed/gravity/jump back to 1, pose None), `SetOccupied(false)` after 1 s.
9. Settings rows via `AcSettingsUi` (group "Driving"): `seat.vrsteer` (two hands / one hand / stick), `vr.horizonlock`, `seat.pedals` swap,
   `seat.camera` default.

## Player.log lines to add
`Seat: entered <car> as driver|passenger (pose entered, rig recentred, head hidden)` · `Seat: wheel ready (rim radius X m, lock Y deg)` ·
`Seat: grip report L/R` (once) · `Seat: camera <mode> (brain held)` · `Seat: got out at <pos>` · refusals with reasons.

## Verify
build.sh x3, check.sh; lab `seat-enter` (simulated: AcXr.Simulated / editor fallbacks of AcPlayer) - shots from the cockpit eye
(steering wheel ahead at the right height), chase camera, door open; `r.Check` that the root rotation has zero pitch/roll while the car
is pitched. Most of S is client-only (Bridge): state clearly what only a client test can prove and list the exact steps for the user.

## Consumes / provides
Consumes: AcCarRig/AcCarPhysics/AcCarSpec (P), AcCarModel (C: eye, hips, hands, steering wheel, doors), AcCarAudio.Door (A),
N's seat request + riders data (passengers), AcPlayer/AcXr/AcTabletGuard (lead). Provides: AcSeat, AcDriveInput (the local car's
AcCarInput), AcLocal.Seat fields, AcEvents.SeatChanged (H shows/hides, A interior switch, N publishes occupancy).
