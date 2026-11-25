# Vehicles: how ETS2 (and Halo) do them, and what Assetto Corsa should take

Research spec for the Assetto Corsa Dreamport (project `D:\Dreamport\Assetta Corsa`, code to be `Assets/#User/SomniumAC`-ish).
Sources, all read-only: ETS code `D:\Dreamport\Euro Truck Simulator\Euro Truck Sim\Assets\#User\SomniumEuroTruck` (below: `ETS/`),
Halo `D:\Dreamport\Halo\Halo Dreamport\Assets\#User\SomniumHalo` (`HALO/`), Somnium client `E:\Somnium Space\Main Build\Assets\_SomniumSpace`
(`MB/`), AC install `E:\SteamLibrary\steamapps\common\assettocorsa` (`AC/`).

Legend: **[V-code]** = verified by reading the code at the given line. **[V-run]** = verified by running a probe against real files
(probes in `C:/Users/derii/AppData/Local/Temp/claude/acscratch/ets_vehicles/`). **[belief]** = my inference, not tested.
Line numbers are as of 2026-10-31.

---

## 0. One-paragraph summary

ETS has a complete, client-proven vehicle stack: a raycast-wheel Rigidbody controller (`EtsTruckController`), a seat that takes the
Somnium avatar over through the Bridge and carries the body root every LateUpdate (`EtsTruckSeat`), a door handle the client's pointers
can hit (`EtsTruckDoor`), VR hands-on-the-rim steering (`EtsSteeringGrab`), trigger pedals with capture (`EtsVr`), a monitor camera that
pauses the client's CinemachineBrain (`EtsTruckCamera`), head hiding + elevator-style avatar carry (`EtsAvatarSync`), passengers and
standing riders drawn rigidly on every client's own copy of the vehicle (`EtsRiders` + `Ets/EtsRiderMath`), and networking of the driven
vehicle through the player's own 64-int slot drawn on a stamped Hermite timeline (`EtsNet` / `EtsPlayerWire` / `EtsNetVehicle` /
`EtsNetTimeline`). The seat/door/riders/net/camera/VR-input pieces are ~90 % generic and should be ported nearly verbatim (rename
Ets->Ac). The **physics core is truck-grade** (no wheel spin state, linear tyre, automatic 3-rpm-threshold gearbox, 50 Hz) and must be
replaced for 300 km/h racing cars. Halo adds: an early(-90)/late(10000) frame director, avatar hands on the wheel grips by bone posing
(`HaloSeatPoses` + Desk `HaloPuppetPoser.ApplySeated`), an optional VR third-person chase view, and the tablet-button guard.

---

## 1. File map (what to port, what to rewrite)

| ETS file (lines) | Role | Generic? | AC action |
|---|---|---|---|
| `EtsTruckController.cs` (2387) | Rigidbody + raycast wheels, engine, gearbox, steering, puppet mode, fall recovery | Structure generic; physics truck-only | **Rewrite physics** (sec. 9), keep Driven/Puppet/PlaceOnGround/HoldWheel/ApplyPuppet contract |
| `EtsTruckSeat.cs` (1329) | Seat: Bridge takeover, pose, carry body root, VR head placement, get in/out, passenger, vanish | ~95 % generic | **Port** (seat points from AC driver rig, sec. 3.6) |
| `EtsTruckDoor.cs` (436) | Click/trigger/grip handle on layer 16 | Generic | **Port** |
| `EtsSteeringGrab.cs` (540) | VR rim grab, 1 or 2 hands | Generic (rim measured from mesh) | **Port**, lock from `car.ini STEER_LOCK` |
| `EtsVr.cs` (437) | XR input (UnityEngine.XR), pedals, capture, haptics, Teleport | Generic | **Port**, remap buttons for gears |
| `EtsCabControls.cs` (575) | Free-hand trigger on stalk/hazard/park lever | Truck cab | Optional (AC: maybe paddles/shifter) |
| `EtsWheelHaptics.cs` (244) | Rim vibration | Generic idea | **Port**, drive from AC surfaces.ini VIBRATION_* + slip |
| `EtsTruckCamera.cs` (427) | Monitor interior/chase camera, brain hold | Generic | **Port**, AC camera data |
| `EtsAvatarSync.cs` (533) | Own head hide; monitor avatar carry in LateUpdate | Generic | **Port** |
| `EtsRiders.cs` (1282) + `Ets/EtsRiderMath.cs` | Riders, rider record, rigid correction of remote bodies | Generic | **Port** (passenger seat in 2-seat cars) |
| `EtsNet.cs` (2853), `EtsPlayerWire.cs` (1788) | Slot claim/write/read, frame order, seat requests | Generic pattern, truck fields | **Port pattern**, new int layout (sec. 8) |
| `EtsNetVehicle.cs` (587) | One remote vehicle (build sliced, stand-in box, draw) | Generic | **Port** |
| `EtsNetWire.cs` (493): `EtsNetTimeline` (l.221) | Stamped Hermite interpolation | Generic, pure | **Port verbatim** |
| `EtsTruckAudio.cs` (2173), `EtsGameSound.cs` (1376), `Ets/EtsFmodBank.cs`, `Ets/EtsFmodStudio.cs` | Game FMOD banks -> AudioSources | Bank reader generic | **Port bank reader + add PCM16**; re-measure Studio chunk offsets (sec. 10) |
| `EtsTruckRig.cs` (740) | One built vehicle; `EtsLocalTruck` (Own/Occupied statics, l.672) | Generic | **Port** (`AcLocalCar`) |
| `Ets/EtsLayers.cs` | Layer constants + masks | Generic (client's matrix) | **Port verbatim** |

Halo equivalents (`HALO/Game/Vehicles/`): `HaloVehicleSeat.cs` (ETS seat port), `HaloVehicleDirector.cs` (frame order), `HaloSeatPoses.cs`
(seat animation + hand IK), `HaloVehicleRiders.cs`, `HaloVehicleEntry.cs`, `HaloVehicleCamera.cs`, `HaloVehicleNet.cs`, `HaloVehicleInput.cs`.
Notes: `D:\Dreamport\Halo\Halo Dreamport\DevTools\agents\V\NOTES.md`.

---

## 2. Layers and collision (copy exactly) [V-code `ETS/Ets/EtsLayers.cs`]

- Constants: Default 0, IgnoreRaycast 2 (avatar CharacterController), Ui 5, PhysicalObject 7 (VR hands' 1 kg boxes), Terrain 14,
  Environment 23, Player 24, Vehicle 28. The collision matrix string is identical in client and SDK `DynamicsManager.asset`.
- `GroundMask = 1<<0 | 1<<14` (wheel rays, spawn rays). `BodyExcludes = GroundMask` (the body box never touches the drive surface; wheels
  hold the car up). `SeatedBodyExcludes = GroundMask | 1<<2 | 1<<7 | 1<<24` (while someone local sits in it). `SeatedCapsuleExcludes = 1<<28`
  (a passenger's own capsule ignores vehicles). `PlatformMask = 1<<28` (rider step-on ray).
- **Lesson (README "The truck fell through the world", l.1333):** Bridge `SetCollisionDisableState(true)` only ORs layer 2 into the
  capsule's excludeLayers (MB MotionFeature). The capsule (kinematic RB) stays solid against layer 28; written into the cab each frame it
  pushed the truck at the 10 m/s depenetration speed -> 297 then 434 km/h falling. Fix = the vehicle box excludes 2|7|24 while driven;
  parked vehicles are **kinematic and solid** (`SetDriven(false)`); after getting out the box stays see-through 1 s (`_solidAgainAt`).
- Track/world: drive surface on 0 (or 14), walls/buildings on 23 (the body box collides with 23).
- Halo's extra: contact shapes on layer 31 (`HaloVehicle.ShapeLayer`, collides with everything, not shot); always exclude Hitbox 24.

---

## 3. The seat: entering, sitting, carrying the avatar [V-code `ETS/EtsTruckSeat.cs`]

### 3.1 Entering (`EtsTruckDoor`, [V-code])
- `Build()` l.132: child GameObject "Ets get in (click the truck)" on **layer 16** (`HandleLayer`, the only layer both client pointers hit
  that is not ground/UI), a **solid** BoxCollider (not trigger: the VR ray and XRI skip triggers) sized from the body box down to 0.3 m,
  `excludeLayers = ~0`, `providesContacts = false`; `XRSimpleInteractable` on interaction layer 4 (`1<<4`, 'XRInteractable'),
  `activated` -> `Use("trigger on the truck")`, `selectEntered` -> `Use("grip ...")`; plus `EtsTruckDoorHandle : IPointerClickHandler`
  for desktop clicks. Built inactive; `Update()` l.364 shows it only while `Usable`.
- Desktop pointer facts: XRUIInputModule + PhysicsRaycaster, layers 0,1,4,5,14,16. VR: NearFarInteractor far ray 0,5,6,10,14,16,23, 5 m,
  interaction layers 144. (Doc block l.9-31.)
- Belt and braces l.409: legacy `Input.GetMouseButtonDown(0)` raycast against the handle when no EventSystem object is under the pointer.
- **Lesson (l.139-147):** never read/set `inertiaTensor` around adding colliders: Unity computes mass properties lazily; setting the
  tensor turns automatic inertia off for good -> "as soon as I enter the truck it spins" (thrown 34 m). The handle only exists while the
  vehicle is kinematic, so it never joins the dynamic compound body.
- `Usable` rules: player on foot, has a Bridge body, menu/tablet closed, own vehicle parked (`!Truck.Driven`) or any puppet.
- Puppet click -> `EtsNet.RequestSeat(ownerSlot, 1, granted, refused)` (owner grants within 0.25 s, refusal or 3 s timeout), riders hold
  publishing meanwhile (`UseRig` l.263).
- Halo variant `HALO/Game/Vehicles/HaloVehicleEntry.cs`: one shared handle moved to the best seat (`Choose`), prompt text, E on monitor.

### 3.2 Taking the avatar over (`TrySeat` l.311)
Order matters: (1) `Truck.SetDriven(true)` first (box already ignores the avatar when it arrives); passenger: `ExcludeVehicles(true)`.
(2) Bridge `Motion`: `SetCollisionDisableState(true)`, `SetFlyModeDisableState(true)`, `SetTeleportationDisableState(true)`,
`SetMovementSpeed(0,0,0)`, `SetGravity(0,0)`, `SetJump(0,0)`. (3) `Animation.SetFallingAnimationDisabled(true)`,
`Avatar.SetVRIKLegMovementDisabled(true)`. `Release()` l.1255 undoes all (speed/gravity/jump back to 1), `Poses.SetPose(None)` first,
then `SetDriven(false)` for the driver only.
Bridge members available (from `SomniumSpace.Bridge.xml`): Poses.SetPose(PoseType), Motion.DoTeleportToPoint(pos, euler, Action),
Set* above, Avatar.SetVRIKLegMovementDisabled, Animation.SetFallingAnimationDisabled. **There is no IK-target API** (the client's own
cars use internal `SetNewIKTargets`, MB `Player/.../VehicleFeature.cs:42-64`) -> hands on the wheel must be done by bone posing (sec. 6).

### 3.3 Forced sitting pose (`KeepSitting` l.926)
- `Poses.SetPose(PoseType.SittingStraight)` **every seated frame** in LateUpdate (cheap when already in it; re-enters the same frame when
  the client drops it on its cancel action: left X in VR, Escape on monitor, avatar reload, scale change). Throw -> `ForceSitting=false`.
- VR: entering a pose turns `HeadCustomPoseOffset` and slides the rig under the head; `UndoPoseRecentre` l.975 zeroes its local rotation
  and re-centres it, and `UndoHandRecentre` l.994 does the same to `Left/RightHandCustomPoseOffset` (else hands are drawn/read 1/3 m off
  after a 40 degree entry - this breaks the wheel grab).
- The client networks the pose, so everyone sees the driver sit.

### 3.4 Carrying the body root (`LateUpdate` l.795)
```
_root.position = truck.TransformPoint(SeatLocalNow);
_root.rotation = Quaternion.Euler(0, truck.rotation.eulerAngles.y, 0);   // SeatRotation l.868: HEADING ONLY
VR:      PlaceHead(truck.TransformPoint(EyeLocalNow)); then Wheel.AfterRigPlaced() (driver)
Monitor: if pose lowered head (HeadCustomPoseOffset.localPosition.y < -0.05) _root.position += eye - _head.position
```
- `PlaceHead` l.1063: rig = body root if the camera is its child (usual), else camera root; `inRig = rig.InverseTransformPoint(head)`;
  rest taken on first frame or when the head moves > 0.7 m from it; lean clamped to `LeanLimit = 0.45 m`;
  `rig.position += (eye + rig.TransformVector(lean)) - head.position`.
- **Heading only, never pitch/roll** (rule 4 of the guide). ETS tried cab-tilt follow on 2026-09-23 and removed it on 2026-09-25:
  user found it "VERY disorientating" (class doc l.30-34). Note: the client's own `FirstPersonViewState` copies the full vehicle rotation
  (MB `VehicleSystem/Scripts/Parts/Additional/Camera/States/FirstPersonViewState.cs` Process) - we deliberately differ.
- The truck uses `RigidbodyInterpolation.Interpolate` while driven (`Configure` l.1309), so `truck.transform` in LateUpdate is the smooth
  interpolated pose -> no seat jitter. Keep this for AC.
- Halo improvement: write the seat **twice**: early in Update at order -90 (`HaloVehicleDirector`, before the client's order-0 avatar
  placement, so a monitor avatar does not trail by a frame) and late in LateUpdate at order 10000 (`HaloVehicleLate`) [V-code
  `HALO/Game/Vehicles/HaloVehicleDirector.cs`]. ETS achieves the monitor part with `EtsAvatarSync` instead (sec. 6.2). At 300 km/h one
  frame = 1.4 m at 60 fps, so AC needs both.

### 3.5 Getting out
- `GetOut()` l.664 -> door sound, `Release()`, `StandBeside()` l.701: 1.6 m outside the seat's side (side from sign of seat x), ray down
  from 3 m on GroundMask|Environment, `EtsVr.Teleport(pos, yaw)` = `Motion.DoTeleportToPoint(pos, (0,yaw,0), null)` (never set the
  transform: the client's controller drags you back).
- Passenger exit refused above `EtsRiders.PassengerExitMaxKmh = 10`.
- Vehicle destroyed/vanished under a seated player: `Vanished` l.1154 -> `LandBeside` coroutine waits up to 10 s for ground, then the
  arrival point. Setting a parked vehicle down: if tipped (`up.y < 0.8`) or airborne, `PlaceOnGround(..., remember:false)` (lesson: got out
  mid-tumble, truck froze 9 m up).
- AC: getting out on track mid-race should be menu-only (tablet) and only below a speed; teleport to the pit wall/track side (sec. 11).

### 3.6 Seat/eye points for AC [V-run, `knh` probe]
- ETS seat = chassis "driver*" locator; eye = interior origin + camera head offset (`DriverEyeLocal`, controller l.1212).
- **AC has the same data per car in `content/cars/<car>/driver_base_pos.knh`** (unencrypted). Format verified on abarth500 (6430/6430
  bytes consumed): recursive node = `int32 nameLen, name (ASCII), 16 x float32 matrix (row-major, translation in [12..14]), int32 childCount`.
  Abarth 500 (AC car frame, metres): `RIG_Hips` world (0.338, 0.720, -0.556); `RIG_Head` (0.337, 1.212, -0.547);
  `RIG_HAND_L` (0.526, 0.962, -0.103); `RIG_HAND_R` (0.125, 0.934, -0.090); `RIG_Foot_L/R` z +0.33. 71 nodes, bones named `RIG_*`, fingers `HAND_*`.
  The hands bracket the wheel centre at about (0.33, 0.95, -0.10), 0.40 m apart (9-and-3 grip, rim radius ~0.2 m).
- Axis convention [belief]: AC +Y up, +Z forward (feet are at +z of hips), +X = driver's LEFT (the LHD Abarth's driver sits at +0.338)
  -> Unity = (-x, y, z). Verify against a rendered kn5 before relying on it.
- Seat hip -> `RIG_Hips`; eye -> `car.ini [GRAPHICS] DRIVEREYES` (inside the encrypted `data.acd`; SDK sample `formula_k`:
  `0.0018, 0.6512, -0.0846`) or `RIG_Head` + small forward offset as fallback.
- Steering wheel node in the kn5: `STEER_HR` (+ `STEER_LR`) [V-run, string grep of abarth500.kn5]. Other nodes present: `WHEEL_LF/RF/LR/RR`,
  `SUSP_*`, `RIM_*`, `RIM_BLUR_*`, `DISC_*`, `DOOR_L/R`, `SHIFT`, `SHIFT_HR`, `COCKPIT_HR/LR`, `CINTURE_ON`, `WIPER_*`.
  Car animations: `animations/steer.ksanim` (driver arms), `shift.ksanim`, `car_door_L/R.ksanim`, `car_shift.ksanim`, `car_wiper.ksanim`.

---

## 4. VR driving input [V-code `ETS/EtsVr.cs`, `EtsTruckController.Update` l.1533]

- Read with `UnityEngine.XR.InputDevices` (Unity.InputSystem is NOT on the server's assembly list). Polled once a frame (`Poll` l.173).
- Bindings (avoid the client's: menu buttons, right B inspector, B/Y tablet tap): right trigger = throttle, left trigger = brake / reverse
  from standstill, left stick X = steer (dead zone 0.15 rescaled), right A = parking brake, right stick click = horn (only when stick
  near centre), left X = menu, left Y = get out (both retired once the app is on the client tablet: `EtsTabletLink.KeysRetired`).
- **Pedal arming:** after getting in, pedals ignored until both triggers < 0.05 (`_vrPedalsArmed`, l.1617; `DisarmVrPedals()` l.123 is
  called while/after the tablet is used) - the trigger that pointed at the door is also the throttle.
- **Trigger capture** (`Pedal` l.248): a press that starts on a hand hovering a cab control is owned by the control until released.
- Grip analog first, button as floor (`Grip` l.268; AoM lesson: runtimes report one or the other).
- Haptics: `Pulse` (one-shot, protected from being cut) and `Rumble` (continuous, re-sent overlapping impulses); `SendHapticImpulse` is
  the only haptics API on the server list.
- Keyboard (monitor): W/S throttle/brake (S reverses from stop), A/D steer, Space brake, P parking brake, keys 1/2 camera, C cruise,
  T trailer, R respawn (only without the tablet: R is the client's once linked). F is the client's Interact key - never take it.
- Steering on keys/stick is *relative*: input moves toward target at `Spec.SteerSpeedCoefficient * 0.6` per second and self-centres;
  lock shrinks with speed (physics.sii SteerSensitivityAt0/At100). With hands on the rim it is *absolute* (eased in over 0.3 s,
  `_absoluteSteering`, FixedUpdate l.1825).
- Halo (`HaloVehicleInput.cs`): same right/left trigger mapping (copied from the client's XRI Vehicle actions), right B exit, left Y view
  toggle; `HaloTabletGuard.Update(true)` while seated + 1 s after, so B/Y do not open the client tablet (director `Tablet()`).

**AC recommendation:** right trigger throttle, left trigger brake (no auto-reverse on track; reverse = R gear), paddles = A (up) / X or
grip-click (down) [belief: pick buttons the client does not bind; X is the client's short-press too - see guide sec. 7], left stick steer
fallback, keyboard W/S/A/D + arrows, Shift/Ctrl or E/Q for gears (AC default keyboard is shift up/down on keys - read `AC/system/cfg`
controls if the user wants identical keys). Analog throttle/brake from triggers is ideal for racing (keep it linear, add an optional
gamma). Keep pedal arming.

---

## 5. VR steering wheel grab [V-code `ETS/EtsSteeringGrab.cs`]

- No collider, no XRI on the wheel (the client's grip selects nothing on layer 28, so nothing fights; a collider would join the
  vehicle's compound body).
- Called by the seat at the end of its LateUpdate (`AfterRigPlaced` l.239) - hands are only in the cab after the rig is placed
  (read earlier = 0.28 m behind at 90 km/h; at 300 km/h ~1 m).
- Hand position = Bridge `Body.LeftHand/RightHand` (grip pose anchors) expressed in `Body.Root` (rig) space; fallback XR device pose
  carried through the headset (`HandInRig` l.442).
- Wheel frame from `TryGetSteeringWheelFrame` (controller l.1741): hub = wheel transform position, rest = parent rotation x rest local
  rotation; +Y axle toward driver, +Z top of rim, +X driver's right. Angle = `atan2(x, z)` (0 at 12 o'clock, clockwise positive).
- Take hold: grip crosses `GripOn 0.55` (hysteresis off at 0.35) within `Reach 0.12 m` of the rim centre line; let go if grip opens or
  hand > `LetGoDistance 0.35 m`. Two hands: delta = change of angle of the chord between the hands (if chord > 0.15 m); one hand:
  change of its angle (ignored within 5 cm of the hub). Frame deltas > 60 degrees are dropped (tracking jump).
- **Key trick:** turning is measured from how each hand moved **inside the rig** since last frame, re-expressed through *this* frame's
  rig and wheel - so the car pitching/rolling under a heading-only rig never turns the wheel by itself.
- Rim radius/height measured from the wheel mesh at run time (`MeasureRim` l.477: outermost vertex - 2 cm tube inset; valid 0.12-0.4 m;
  fallback 0.22 m). Needs `mesh.isReadable` (build meshes readable).
- Output: `Truck.HoldWheel(deg / WheelDegreesPerUnit)` where `WheelDegreesPerUnit = 450` (2.5 turns lock to lock, truck);
  past the lock -> 0.5/40 ms pulses. `ReleaseWheel()` -> autocentre (game rule, floor 0.15).
- First grab starts the engine in VR (truck-specific nicety; `RequestEngineStart`).
- **AC:** use `car.ini [CONTROLS] STEER_LOCK` (degrees from centre to right, sample 200) as the hand limit and `STEER_RATIO` (sample 12.5)
  to get road-wheel angle; i.e. replace `WheelDegreesPerUnit` with `STEER_LOCK`. Race cars ~ 270-540 degrees lock to lock - a VR rim at
  1:1 is right. Add self-aligning feedback through haptics only (no force feedback in VR).

---

## 6. Avatar: head, carry, hands on the wheel

### 6.1 Own head hidden (ETS `EtsAvatarSync.HeadHide` l.200) [V-code]
Main copy = active humanoid Animator under a client `MainRoot`/`NonXRMainRoot` (type name test) whose head bone is within 0.6 m of
Bridge `Body.Head` (`FindMain` l.434, once a second while missing). Head bone scaled to 1e-4 every frame while seated and camera within
0.45 m (out at 0.6 m); restored on exit/chase view. A head already scaled < 0.001 (client hides VR own head) is left alone. Only the local
main copy, nothing networked. README l.3417: "Untested in the client. The client's type names MainRoot/NonXRMainRoot survive obfuscation
(BO2 relies on it)." Halo `HaloSeatPoses.OwnHead` (l.431) does the reverse for its VR third-person view (shows the head).

### 6.2 Elevator carry for monitor avatars (`Remember` l.298 / `Carry` l.312, order 9990) [V-code]
In Update (after the client placed the avatar main root) store main-root pose in body-root frame; in LateUpdate (after the seat moved
the body root) put it back on the new body root. Fixes the avatar trailing the seat by one frame of travel (40 cm at 90 km/h/60 fps).
Also applied to remote monitor riders (`EtsRiders.PlacedDesktopRemotes`). VR avatars are placed by VRIK after LateUpdate - untouched.

### 6.3 Hands on the wheel
- **ETS: none.** VR hands are on the rim because the real controllers are (VRIK). A monitor driver sits in SittingStraight with default arms.
- **Halo does it** [V-code `HALO/Game/Vehicles/HaloSeatPoses.cs`, order 9997]: per rider (own + remote), samples the game's seat clip,
  then `HaloPuppetPoser.ApplySeated(sample, dt, seatMarker, legLen, armLen, hipsToo, arms, lv, lw, rv, rw, out gp, out gq)`
  (`HALO/Game/Desk/HaloPuppetPoser.cs:351`) after the client's Animator/IK. Monitor: whole body + hands IK'd to the vehicle's grip markers
  (`SeatIk` l.177, scaled by avatar arm length / game arm length, k clamped 0.4-1.6). VR first person: hips+legs+torso from the clip, arms
  IK'd to the tracked controllers, hand rotation kept. Copies to mirror/shadow avatar copies (`HaloAvatarCopies`).
- **AC recommendation:** port HaloPuppetPoser + HaloSeatPoses; targets = AC `driver_base_pos.knh` `RIG_HAND_L/R` (sec. 3.6) rotated with
  the steering wheel (`STEER_HR` local rotation) - or drive the arms from `animations/steer.ksanim` (driver steer animation; format not yet
  parsed). Monitor drivers + remote viewers then see hands on the wheel turning with the steering; VR keeps tracked hands.

---

## 7. Monitor (desktop) camera [V-code `ETS/EtsTruckCamera.cs`]

- Does not own a camera: each LateUpdate places `Camera.main` (or first enabled screen camera). Skips entirely in VR.
- **Client's CinemachineBrain** on 'Camera' (CameraSystem.prefab) runs at order 100 in LateUpdate and overwrites pose/FOV/clip: paused
  while we hold the view: `_camera.GetComponent("CinemachineBrain") as Behaviour; enabled=false` (by name - no Cinemachine assembly
  reference) and re-enabled on hand-back (`HoldBrain` l.262 / `GiveBrainBack` l.276). FOV and far clip restored.
- Interior: position = `truck.TransformPoint(EyeLocal)`, rotation = `truck.rotation * Euler(pitch, yaw, 0)` (monitor may tilt with the
  car), mouse look on RMB clamped to the game's interior camera limits; FOV from the game's camera def.
- Chase: orbit behind, 14 m back, 5 m up, FOV 60, kept 1 m above ground by a down ray.
- Keys 1 (cab) / 2 (chase); tablet "Camera: inside / behind" button (`EtsPageDrive`, `EtsTabletApp.cs:3249`).
- Halo `HaloVehicleCamera.Place` uses the game's camera tracks + collision on 0/14/16/23.
- Client facts (Halo NOTES): the client's desktop vehicle cam is its FreeLook at ~7.3 m back / 1.74 m up.
- **AC:** cameras from the game: `car.ini DRIVEREYES` (cockpit), `system/cfg/chase_cam.ini` (chase presets), `camera_onboard_free.ini`;
  per-car `data/cameras.ini` / `dash_cam.ini` (in data.acd). Racing needs cockpit, bonnet/dash, chase near/far; first-person near plane
  must go below the client's 35 cm (guide sec. 7).

### VR third person (Halo option, [V-code `HaloVehicleSeat.ChasePoint` l.290])
Client's `ThirdPersonViewState` (MB): heading-only rig at the vehicle, 4 m up, `MoveTowards(6, 10, speed/10)` m behind, heading lerps toward
the vehicle's (looks back when reversing, angle > 110 degrees). Halo reproduces it by moving the XR rig so the head lands there
(`PlaceHead(ChasePoint(), Euler(0, chaseYaw, 0), lean:false)`). Useful for AC as an option (TV-cam-like chase), default cockpit.

---

## 8. Networking a driven vehicle

### 8.1 Pattern [V-code `ETS/EtsNet.cs` header l.20-41, `Update` l.265]
- Scene carries pre-placed slots "Ets Net Player 0..49" (Fusion NetworkObject + NetworkBridgeData64 = 64 ints). A client claims one
  (RequestStateAuthority + claim record), writes only its own, reads all. Vehicle state rides **in the owner's player slot** (Phase 3
  retired the separate truck object).
- Frame order in `EtsNet` (`[DefaultExecutionOrder(-100)]`): Update = session tick -> ReadSlots -> DrawPuppets -> UpdatePlates ->
  `EtsRiders.EarlyPass` (guarded) -> `WriteOwnSlot`. LateUpdate = `EtsRiders.LatePass` (after Fusion Render, before VRIK 9998).
  Seat LateUpdate (order 0), AvatarSync 9990.
- `WriteOwnSlot` l.1400: header composed at 4 Hz; motion etc. at most 30 Hz (`WriteEvery = 1/30`, l.50); only changed ints are set
  (`Push`: Fusion sends changed ints only; Fusion's own send rate measured 16 Hz, probe 1674). `seq`+`stamp` advance only when motion
  ints (5, 9..25) change -> a parked car costs nothing.
- `PackOwnMotion` l.1514 reads **`body.position` / `body.rotation` / `body.linearVelocity`** (physics pose, not the interpolated
  transform) with stamp `StampMs() = (int)(Time.fixedTimeAsDouble*1000)` (l.1318) - pose and clock agree.

### 8.2 Slot layout (truck) [V-code `ETS/EtsPlayerWire.cs` l.47-105]
`0 magic 'ETP1' | 1 seq | 2 stamp ms | 3 owner+1, claim gen, session epoch | 4 status/flags | 5 placement epoch, serials | 6-8 vehicle
identity/paint | 9 vehicle flags (placed, driven, parking brake, horn, beams, blinkers, hazard, brake lights, reverse, engine, ...) |
10-12 position WirePos (int cm, game-origin axes, packed in double) | 13 yaw|pitch Angle16 | 14 roll Angle16|steer S16 | 15 vel x|z S16 cm/s
(+-327 m/s) | 16 vel y S16|rpm u16 | 17 gear sbyte|throttle u8|brake u8 | 18-25 trailer | 26-27 seat table (owner writes) | 28-32 job, money |
33-35 + 43 rider record | 42 map signature | 44 traffic hit | 45-63 spare`.
Measured (NetTest2 probe 1823): 535-663 B/s for a driving truck+trailer at 16 Hz, quantisation <= 8.7 mm.
Pure (no Fusion/no native Quaternion calls), so it runs in a dotnet probe - keep that property.

**AC slot (recommended):** drop trailer/job ints; add: lap count, last line-crossing stamp, current lap time, best lap, sector,
race position/state, pit flag, car index (into the sorted owned-car list + catalogue hash, like ETS int 6), skin index, wheel spin rates
or slip (for skid audio/smoke), clutch, gear, rpm, throttle, brake, steer, lights, DRS/ABS/TC flags. Velocity S16 cm/s covers 83 m/s
(300 km/h) fine; Angle16 = 0.0055 degrees.

### 8.3 Drawing remote vehicles [V-code `ETS/EtsNetWire.cs` `EtsNetTimeline` l.221-492, `EtsNetVehicle.Draw` l.228]
- Each remote vehicle has an `EtsNetTimeline`. `Add(state, now)` (l.304) dedups on seq, places samples on the **writer's** clock
  (unwrapped ms stamp), snaps on a new epoch (respawn/teleport) or > 50 m jump.
- `Sample(now)` (l.356): render lag aims at best recent arrival lag + spread + 1.1 send interval + 5 ms, clamped `MinDelay 0.03` ..
  `MaxDelay 0.4 s`, slewed at 0.05 s/s (jumps if > 0.5 s off); position = cubic Hermite with sample velocities; rotation nlerp;
  steer/rpm/throttle lerped; past the newest sample extrapolate on velocity up to `MaxExtrapolation 0.25 s`, then hold.
- `EtsNetVehicle.Draw` -> `Controller.ApplyPuppet(pos, rot, vel, steer, rpm, throttle, brake, gear, parkingBrake, dt)`
  (`EtsTruckController.cs:1145`): kinematic body, `interpolation = None` (`SetPuppet` l.1128, set by transform each frame), wheels re-cast on
  the local road, steered and spun from speed. Lights/blinkers/engine from flags. Out of `TruckDrawRadius 1500 m` -> hidden but frame posed.
  Grey stand-in box (2.5 x 3.3 x 6.1) while the real model builds sliced (1.5 ms/frame, one build at a time).
- Truck vs truck (Q-A3): puppet boxes solid below 10 km/h relative speed, see-through (excludeLayers Vehicle) above, hysteresis 9 km/h,
  never toggled while overlapping (`EtsNetVehicle.Contacts` l.461 doc; `EtsTruckRig.UpdateVehicleContacts`). Uses `excludeLayers` (survives
  deactivation) not `Physics.IgnoreCollision` (lost on deactivate - a real bug they hit).
- Client's own (MB `VehicleSystem/Scripts/Network/Driver/VehiclePhysicsDriver.cs`): sender snapshots every physics step; receiver
  replays 1.5 send intervals behind (+2 x jitter, 0.05-0.3 s), Hermite, turn-aware extrapolation 0.25 s, error correction 10/s, teleport
  at 10 m, applied in FixedUpdate. Same family as EtsNetTimeline - confirms the design.

### 8.4 Passengers / riders [V-code `ETS/EtsRiders.cs`, `Ets/EtsRiderMath.cs`]
- Each player publishes a rider record (vehicle kind/index/serial, seat 0/1/255, body-root offset S16 mm + yaw in the **vehicle's frame
  as this client draws it**), <= 20 Hz, only on change + 3 settle writes + 3 s keep-alive; epoch bump on target/seat change (receivers snap).
- Every client puts each remote rider's body on **its own** drawn copy of the vehicle: rigid correction
  `C(p,q) = (target + R(dYaw)(p - rawRoot), R(dYaw) q)` applied to the Fusion `Body/Root, Head, LeftHand, RightHand` transforms (found by
  `StateAuthority.PlayerId` + parent named "Body", via `FindObjectsByType<NetworkBehaviour>` once a second - **not**
  `runner.GetAllBehaviours<NetworkTransform>`, unproven on the server's older Fusion) **and** the Bridge transforms they drive
  (MB MobilePlatformMover.cs:167-189: writing only one jitters). Early (before client order 0) and again late (after Fusion Render).
  Remote IK legs off + falling anim off while seated or carried > 0.5 m/s.
- For AC: driver = seat 0 is implicit (the slot owner); a passenger (2-seat road cars) uses the seat request flow. Standing riders are
  irrelevant at race speed (keep the code path; AC has no reason to stand on cars).

### 8.5 AC-specific networking recommendations [belief]
- At 300 km/h a 0.1 s render lag = 8 m behind truth. For close racing, draw puppets with **less lag** (MinDelay ~0.03, aim 1 send interval)
  and consider extrapolating to "now" (dead reckoning with velocity + acceleration estimate, smoothed corrections like the client's 10/s).
  Write at 30-60 Hz while racing (Fusion's own tick caps what is sent; measure).
- Car-car contact: the local car is dynamic, remote cars kinematic boxes -> contact is one-sided (the local car bounces off a puppet that
  does not react). Acceptable; offer "ghost" (excludeLayers Vehicle) for the first lap / pit lane / spectators, like Q-A3.
- Race control (start lights, grid slots, lap counting order, results) = instance master (`IsSharedModeMasterClient`, published in the slot
  - the server Fusion has no `GetMasterClient`; ETS int 4 bit 19). Each client times its own laps from its own physics (authoritative for
  itself) and publishes count + crossing stamp; the master orders by (laps, crossing stamp). Host hand-over as Halo iteration 5.
- Seed anything random from shared data.

---

## 9. Raycast wheel physics: what ETS does, and what AC needs

### 9.1 ETS controller as built [V-code `EtsTruckController.cs`]
- `Configure` l.1236: wheels from locators named `wheel_*`; axles grouped by z within 0.5 m (front first); per wheel
  `Spring = share*9.81/Sag` (`Sag 0.10 m`), `Damper = 2*sqrt(k*m)*DampingRatio` (`0.4`), `RestLength = radius + Sag`,
  `Travel = Sag + residual`; front axle steered; mass = kerb mass; `angularDamping 0.8`, `linearDamping 0`, `ContinuousSpeculative`,
  COM = (0, 1.1, weighted z). **Inertia left automatic** (logged once on first driven step, l.1808).
- `FixedUpdate` l.1796 (Unity 50 Hz: client + SDK `TimeManager.asset Fixed Timestep: 0.02`):
  - steer lock = input x MaxSteerAngle x (speed-sensitivity lerp for relative, 1 for absolute);
  - automatic gearbox: up above 1550 rpm (with throttle), down below 950, 0.35 s torque cut (truck constants);
  - rpm from powered wheels' ground speed (no wheel spin state), lerp at 8/s, `TorqueAt(rpm)*throttle*ratio*0.9`;
  - per wheel: `CastWheel` (ray from hub+12 cm along -up, GroundMask), spring/damper along chassis up + bump stop (6x spring past travel),
    tyre frame on hit normal; longitudinal = drive - min(brake+engine brake+rolling, stopForce); lateral = `-9 * slipAngle * load`
    (`CorneringStiffness 9`), both clamped to `m_share*|v|/dt` (no overshoot) then to the friction circle `WetGrip * load`;
  - anti-roll: `0.5*k*(dCompression)` per axle; air drag `-AirResistance*|v|*v`; wheel visuals.
- Safety: fall-through recovery (no wheel grounded 3 s while falling > 15 m/s -> last good spot, kept every 1 s when level), blocked/knock
  diagnostic reports, `PlaceOnGround` ray from 3 m above first then 60 m (lesson: hall roofs on Default caught the 60 m ray -> 7.4 m up).
- Lessons in comments: ray lift 0.5 m found a second overlapping surface -> bump stop threw the truck; lift 12 cm (l.2330-2343).
  Puppets: kinematic + interpolation None.

### 9.2 Why it is not enough for AC
- No wheel angular velocity -> no slip ratio, no wheelspin, lockup, ABS/TC, launch, burnouts; drive force is applied as if tyres never slip.
- Linear lateral force to a hard friction circle -> no peak/fall-off, no drift; AC's tyre model (tyres.ini v7+) has load sensitivity,
  peak slip (`FRICTION_LIMIT_ANGLE`), fall-off, combined slip, camber, pressure, temperature, relaxation length.
- 50 Hz at 83 m/s = 1.67 m per step: kerbs/bumps shorter than that alias; tyre/wheel-spin integration at 20 ms is unstable for light,
  stiff race wheels (ANGULAR_INERTIA ~0.8-0.9 kg m^2, tyre RATE ~170-220 kN/m in the SDK sample).
- Truck gearbox thresholds; AC has per-car ratios, shift times, auto-blip, autoclutch, shifter vs paddles.

### 9.3 AC tyre/drivetrain data available [V-run: read `AC/sdk/dev/content/cars/formula_k/data/*.ini`]
Real cars keep these inside `data.acd` (encrypted, 86 of 180 car folders have one on this install = owned cars; the rest are UI-only DLC
stubs, e.g. `ks_ferrari_488_gtb` has only `ui/`). The SDK sample shows the field names:
- `tyres.ini [FRONT]/[REAR]` (VERSION=7): WIDTH, RADIUS 0.275, RIM_RADIUS, ANGULAR_INERTIA 0.8, DAMP 500, RATE 172538, DY0/DY1, DX0/DX1,
  SPEED_SENSITIVITY, RELAXATION_LENGTH 0.0439, ROLLING_RESISTANCE_0/1/SLIP, FLEX, CAMBER_GAIN, DCAMBER_0/1, FRICTION_LIMIT_ANGLE 6.6,
  XMU, PRESSURE_*, FZ0 1915, LS_EXPY/LS_EXPX, DX_REF/DY_REF 1.80, FLEX_GAIN, FALLOFF_LEVEL 0.92, FALLOFF_SPEED 4; plus `.lut` wear and
  `tcurve_*.lut` thermal curves.
- `car.ini`: TOTALMASS, INERTIA (polar), GRAPHICS_OFFSET, DRIVEREYES, STEER_LOCK, STEER_RATIO, LINEAR_STEER_ROD_RATIO, FFMULT.
- `drivetrain.ini`: [GEARS] COUNT, GEAR_R, GEAR_1..n, FINAL; [GEARBOX] CHANGE_UP_TIME 130 ms, CHANGE_DN_TIME 180 ms, AUTO_CUTOFF_TIME,
  SUPPORTS_SHIFTER, VALID_SHIFT_RPM_WINDOW; [AUTOCLUTCH], [AUTOBLIP], [AUTO_SHIFTER] UP 6300 / DOWN 4500 / GAS_CUTOFF_TIME; [DIFFERENTIAL].
- Others present: engine.ini + power.lut, suspensions.ini, aero.ini (+ wing LUTs), brakes.ini, electronics.ini (ABS/TC), setup.ini.
- Surfaces: `AC/system/data/surfaces.ini` (plain): per KEY (ROAD 1.0, GRASS 0.6, KERB 0.92, ...) FRICTION, DAMPING, WAV, WAV_PITCH,
  FF_EFFECT, DIRT_ADDITIVE, IS_VALID_TRACK, SIN_HEIGHT/SIN_LENGTH (procedural bumps), IS_PITLANE, VIBRATION_GAIN/LENGTH. The track kn5's
  physics meshes are named by these keys (e.g. `1ROAD`, `1KERB` - [belief], verify on a track kn5).

### 9.4 Recommendation for the AC car controller
1. **Keep the ETS shell**: one Rigidbody per car on layer 28, body BoxCollider (or a few boxes from `collider.kn5`, present in every car
   folder) excluding GroundMask (+2|7|24 while driven), automatic inertia (or `car.ini INERTIA` set **once, after all colliders exist**,
   at the very end of setup - never before), Interpolate while driven, kinematic + None as puppet, `Driven`/`Puppet`/`PlaceOnGround`/
   `HoldWheel`/`ApplyPuppet` API so the seat/grab/net code ports unchanged.
2. **Sub-step the tyres inside FixedUpdate** [belief, standard practice]: keep Unity's 50 Hz rigid-body step, but per FixedUpdate run
   N = 5-10 internal substeps (2-4 ms) of: wheel angular velocity integration (drive torque, brake torque, tyre longitudinal force x radius,
   / ANGULAR_INERTIA), slip ratio + slip angle (with RELAXATION_LENGTH lag), tyre forces from a Pacejka-like curve built from AC's
   DX/DY, FZ0, LS_EXP*, FRICTION_LIMIT_ANGLE, FALLOFF_*; accumulate the average force/torque per wheel and apply once with
   `AddForceAtPosition`. Body velocity during substeps can be advanced analytically with the accumulated force (semi-implicit) for
   stability. Do the suspension ray once per FixedUpdate but use a **sphere/box cast of the tyre** or 3 rays along the contact patch so kerbs
   do not alias at 1.7 m/step.
3. **Do not change `Time.fixedDeltaTime` globally** unless proven necessary: it is client-wide (doubles the client's physics + Fusion cost).
   The client snapshots quality/time settings at each world transition (`MB/Settings/SerialiseQualitySettings.cs:110` GetSettings,
   called from `WorldTransitionControl.cs:198,345`) and restores `Time.fixedDeltaTime` only on the IN_MAIN transition (l.505, `SetSettings`
   l.213). A world-to-world hop would snapshot our value - so if a world ever sets it (e.g. 0.01 while seated), it must restore 0.02 itself
   in `OnDestroy` and on getting out.
4. Collision detection: `ContinuousDynamic` (or keep Speculative and test) against track walls at 83 m/s; walls as MeshColliders on 23.
   Default max angular speed is 50 rad/s (DynamicsManager) - fine.
5. Gearbox: manual sequential (paddles/keys) with CHANGE_UP/DN_TIME torque cut and AUTO_CUTOFF_TIME, optional automatic using
   [AUTO_SHIFTER] UP/DOWN (exactly the game's numbers), autoclutch for standing starts, AUTOBLIP on downshift. `SHIFT` / `SHIFT_HR` node +
   `shift.ksanim` for the lever/paddle animation.
6. Steering: road angle from wheel angle / STEER_RATIO (AC itself uses LINEAR_STEER_ROD_RATIO with suspension geometry - approximation OK
   first), lock STEER_LOCK. Keyboard steering needs speed-sensitive rate (AC has steer speed/gamma in controls.ini - read it).
7. Aero: downforce/drag from aero.ini (wing CL/CD LUTs vs AoA/height) applied at the wing positions - matters a lot above 200 km/h.
8. Keep ETS's fall-through recovery (last good spot), blocked reports and one-time "driven: mass/inertia/COM" log line.

---

## 10. Vehicle audio

### 10.1 ETS pipeline [V-code `EtsTruckAudio.cs`, `EtsGameSound.cs`]
- `OnAudioFilterRead` is never called for world code -> all audio = `AudioClip.Create` + `SetData` built off the main thread, played by
  ordinary AudioSources (doc l.6-17).
- Banks: `Ets/EtsFmodBank.Parse` (RIFF "FEV " -> "SND " chunk -> FSB5 sample headers: names, rate, channels, frames, loop points, data
  offsets) and `Ets/EtsFmodStudio.Parse(bank, guidsText)` (event model: PRMB params, WAV/WAIB/MUIB/INST instruments with volume, pitch,
  loop, auto-pitch ref, parameter-sheet regions, CTRL/CURV automations, EVTB events). ETS2 samples are FSB5 codec 15 (Vorbis without
  headers; setup header found by CRC-32 in `EtsVorbisSetups`, decoded by vendored NVorbis `Ets/Vorbis`).
- Runtime: `EtsGameEventPlayer` (`EtsGameSound.cs:423`) = one AudioSource per instrument; `Drive(master)` l.492 evaluates gain/pitch from
  the event's parameter values (rpm, load, brake...) each frame, starts loops above -42 dB, stops below. Engine params set from
  smoothed rpm (40 ms), load (70 ms), speed (100 ms) (`Update` l.645-647).
- Spatial: in the cab 2D (`spatialBlend 0`) with outside sounds x `InteriorAttenuation 0.35` (game_data.sii); outside 3D, full within
  `NearMetres 12`, log roll-off, silent past `PuppetAudibleMetres 300`. `ListenerOutsideGain` damps ambience/traffic in the cab.
  Puppets get engine/exhaust/turbo only (`EtsTruckAudio.ForPuppet`).

### 10.2 AC banks [V-run, `fmodprobe`: ETS `EtsFmodBank.cs` + `EtsFmodStudio.cs` compiled in dotnet 8]
- `content/cars/abarth500/sfx/abarth500.bank` (30.5 MB): RIFF/FEV, chunks `FMT 8, LIST 57718, SND 30463454`, FSB5 version 1, **codec
  (mode) 2 = PCM16**, 53 samples (e.g. `500ss_on_7500` 2ch 44100 Hz 398946 frames, `ext500ss_off_7500`, `500ss_idle`, `gearup`,
  `backfireEXT_*`, `500_limiter`, `tyre_explosion`, `wind_draft_mono`). `content/sfx/common.bank` (43.8 MB): same, 29 samples
  (`car_hit_2`, `crash2`, `scrape3`, `dirt_min`, `ambient1`...). Every sample's data length >= frames x channels x 2 (difference = padding
  to the 32-byte FSB alignment), RMS 3400-17700 and zero-crossing rates 250-3600 /s = real audio, not noise.
- So: **`EtsFmodBank.Parse` reads AC's sample table unchanged**; `Decode` refuses ("codec 2 is not Vorbis") - add a trivial PCM16 path
  (little-endian int16 interleaved -> float, mono/stereo, honour `Coded` channel count). No NVorbis needed for AC.
- `EtsFmodStudio.Parse` on the AC bank: found 18 events by GUID (from `content/sfx/GUIDs.txt`: 3237 lines, 3019 `event:` paths, e.g.
  `event:/cars/abarth500/engine_ext`, `engine_int`, `gear_ext`, `gear_int`, `gear_grind`, `limiter`, `turbo`, `skid_ext`, `skid_int`,
  `wheel`, `wind`, `horn`, `door`, `bodywork`, `backfire_ext/int`, `tractioncontrol_ext/int`) but **0 parameters and 0 instruments**: the
  chunk vocabulary is the same (PRMB, WAIB, INST, CTRL, CURV, PMLB, EVTB, GBSB all present in the LIST chunk) but AC's FMOD Studio 1.x bank
  layout puts fields at different offsets -> the Studio chunk offsets must be re-measured for AC (or fall back to name-based mapping:
  sample names carry rpm, e.g. `*_on_7500`, `*_off_2500`).
- FMOD game parameters used by AC events, from the SDK FMOD project (`AC/sdk/audio/ac_fmod_sdk_1_9/Metadata/Event/*.xml`, plain XML)
  [V-run grep]: `rpms`, `throttle`, `speed`, `decay`, `state`, `impact_speed`, `impact_angle`, `boost`, `bov`, `bov_decay`,
  `drivetrain_speed`, `brake`, `susp_travel_speed`, `suspension_damage`, `inflation`, `dirtiness`, `air_pressure`. Also
  `AC/sdk/audio/AC Audio Pipeline 1.9.pdf` (not read) and per-car `sounds.ini` in data.acd.
- Recommendation: port the ETS player structure (per-instrument AudioSource, in-cab 2D with `engine_int` / outside 3D with `engine_ext`,
  puppets engine+skid only within 300 m), PCM16 decode on a worker, clips built once per car.

---

## 11. Menus in the tablet (ETS pattern to copy)

- ETS's tablet app page `EtsPageDrive` (`EtsTabletApp.cs:3212`): "Get out and walk" (greyed + toast when refused, e.g. passenger > 10 km/h),
  "Put the truck back on the road" (respawn), "Choose another truck", "Camera: inside / behind"; `EtsPageConvoys` (l.3421) "Ride along" =
  passenger seat request. Once the app is linked to the client tablet, left X / left Y / R are retired to the client (`EtsTabletLink.KeysRetired`).
- While seated in VR, the tablet opens on B/Y tap: ETS keeps it (getting out moved into the app); Halo holds the client's tablet actions
  off while B/Y mean something (`HaloTabletGuard`). For AC: keep the tablet usable while seated (it is the menu), disarm pedals while it
  is up (`DisarmVrPedals`), disable the wheel grab while the app is visible (`EtsSteeringGrab.UsableInCab` l.159).
- AC pages to plan: Drive (get out / back to pits / camera / auto vs manual gears / ABS/TC/assists), Race (lobby, track, laps, grid,
  start), Car (owned cars from `content/cars/*/data.acd` present + `ui/ui_car.json`), Setup, Results/lap times.

---

## 12. Known bugs / lessons list (from ETS README + code comments; all [V-code])

1. Avatar capsule pushes a dynamic vehicle -> exclude 2|7|24 while seated; parked = kinematic (README l.1333).
2. Setting inertiaTensor around collider creation locks a garbage tensor -> leave automatic (EtsTruckDoor l.139, README l.1396).
3. Wheel ray must start slightly above the hub (12 cm), not at it (falls through) nor a tyre radius above (finds an upper overlapping
   surface) (CastWheel l.2330).
4. Spawn rays from 60 m hit roofs on Default -> ray from 3 m above the expected height first (PlaceOnGround l.1458-1473).
5. Accelerator releases the parking brake; place vehicles with parking brake on (else free-rolls through menus).
6. VR trigger that pressed the door/tablet is the throttle -> arm pedals only after both triggers released.
7. Pose entry re-centres head AND hand offsets -> undo all three (EtsTruckSeat l.975-1007).
8. Hands read before the rig is placed are a frame behind -> grab runs at the end of the seat's LateUpdate.
9. Cab-tilt view follow = VR sickness -> heading only.
10. Client CinemachineBrain overwrites the desktop camera -> disable by name while driving, restore after.
11. F is the client's Interact key; R/left X/left Y belong to the client once the tablet app is linked.
12. `Physics.IgnoreCollision` pairs are dropped when a collider deactivates -> use `Collider.excludeLayers`.
13. Fusion API on the upload server is older: no `NetworkRunner.GetMasterClient`, avoid `GetAllBehaviours<NetworkTransform>`; run
    `DevTools/fusionapi/check.sh`.
14. A destroyed rig compares == null but must still be released (`ReferenceEquals` checks in EtsRiders.Membership l.568).
15. Monitor avatar trails the seat by a frame -> elevator carry in LateUpdate (EtsAvatarSync) / early+late seat write (Halo).
16. `EtsVr.Present` cached per frame (XRSettings.loadedDeviceName allocates a string per call).
17. Puppet Rigidbody interpolation must be None (it is placed by transform; Interpolate draws it a step behind).

---

## 13. Somnium client VehicleSystem facts (for reference) [V-code, MB paths]

- Cars: `VehicleSystem/Cars/Scripts/Controller/Car.cs` extends PG `CarController` (Perfect Games vehicle kit, Unity WheelColliders;
  `Wheels[i].WheelCollider.motorTorque/brakeTorque/steerAngle`), `DownPressureApplier.cs`, `FirstPersonCameraPoint`, `LeavePoint`.
  Inputs: `SetInputs(acceleration, brakeReverse, horizontal, handBrake, boost)`.
- Seats: `VehicleSystem/Scripts/Parts/Base/Seats/VehicleSeat.cs`: seat id 0-7, `_sitPoint` (body root + facing), `_exitPoint` (else
  `VehicleExitPositionFinder` searches), `AvatarTargets` IK targets (Root + limbs; without targets the seated avatar is hidden like a
  driver's), `_firstPersonCameraPoint` (default head target / sit point).
- XR views (`Parts/Base/VehicleCameraController.cs`): XR only (2D uses the player's own third-person camera). Starts in third person,
  re-parents the HMD camera under a rig. `FirstPersonViewState`: camera parent follows `EndPoint.position` and full `rotation`.
  `ThirdPersonViewState`: rig at the vehicle, `_minZPos -6`, `_maxZPos -10`, `_maxSpeedPos 10` (z = MoveTowards(-6, -10, speed/10)),
  heading only (`localEulerAngles = (0, y, 0)`), rotation lerp `_rotationSpeed` (script default 5; Halo's notes measured 2/s on the
  prefab), flips when moving > 110 degrees from forward (reversing). A world cannot re-parent the HMD camera; move the rig instead (Halo).
- Network: `VehiclePhysicsDriver.cs` (sec. 8.3), `VehicleSeatsNetworkObject` (MAX_SEATS 8).

---

## 14. Concrete port plan (for the implementing agents)

1. `Ac/AcLayers.cs` = EtsLayers verbatim.
2. `AcCarController` (new physics, sec. 9.4) exposing the ETS controller API: `Driven`, `SetDriven`, `Puppet`, `SetPuppet`,
   `ApplyPuppet(...)`, `PlaceOnGround(pos, yaw, remember)`, `Respawn`, `HoldWheel`, `ReleaseWheel`, `TryGetSteeringWheelFrame`,
   `SteeringWheel` (= `STEER_HR`), `SeatLocalFor(seat)`, `EyeLocalFor(seat)`, `BodyBox`, `Placements`, `SpeedKmh`, `Rpm`, `Gear`,
   `Throttle`, `Brake`, `SteerInput`, `DisarmVrPedals`.
3. Port `EtsTruckSeat` -> `AcSeat`, `EtsTruckDoor` -> `AcDoor`, `EtsSteeringGrab` -> `AcWheelGrab` (STEER_LOCK), `EtsVr` -> `AcVr` (gear
   buttons), `EtsTruckCamera` -> `AcCarCamera`, `EtsAvatarSync`, `EtsRiders` + `EtsRiderMath`, `EtsTruckRig`/`EtsLocalTruck` -> `AcCarRig`/
   `AcLocalCar`, `EtsNetTimeline` verbatim, `EtsNetVehicle` -> `AcNetCar`, slot wire with the AC layout (sec. 8.2), frame order as EtsNet
   (+ Halo's early -90 seat write).
4. Port Halo `HaloPuppetPoser` + `HaloSeatPoses` -> hands on `STEER_HR` grips from `driver_base_pos.knh` (`RIG_HAND_L/R`).
5. Audio: `EtsFmodBank` + PCM16 decode; re-measure FMOD 1.x Studio chunks or name-based rpm layers; `engine_int`/`engine_ext` split.
6. Everything started from the gated root's hooks (guide sec. 2); one-time Player.log lines for: seated (pose entered, rig, head hidden),
   wheel ready (grip report, rim radius), car driven (mass/inertia/COM), puppet built, rider bound, sounds loaded.

## 15. Probes written (scratch, not shipped)
- `C:/Users/derii/AppData/Local/Temp/claude/acscratch/ets_vehicles/fmodprobe/` - compiles ETS `EtsFmodBank.cs`, `EtsVorbisSetups.cs`,
  `EtsFmodStudio.cs`, `Vorbis/**` unchanged under dotnet 8 (C# 9); `dotnet bin/Release/net8.0/fmodprobe.dll <GUIDs.txt> <bank>...`.
- `C:/Users/derii/AppData/Local/Temp/claude/acscratch/ets_vehicles/knh/` - `.knh` hierarchy dumper: `dotnet bin/Release/net8.0/knh.dll <file.knh> [prefix,prefix]`.

---

## Critic addendum (completeness critic, 2026-10-31)

- 3.6 axis convention "[belief]" is now VERIFIED (car spec 1.5 + C1): AC +X = car left -> Unity (-x, y, z). The knh, DRIVEREYES, kn5 and
  collider.kn5 share MODEL space (origin on the ground); physics inis are CG space; `physics = model + GRAPHICS_OFFSET`. Seat/eye in the
  Rigidbody's local frame: `(-(p.x+off.x), p.y+off.y, p.z+off.z)` (`ac_car_formats.md` C1, 86/86 cars).
- 5 (wheel grab): rim = mesh under `STEER_HR`, column axis = node local Z, rim radius ~0.19 m on road cars (0.13 m formula); Unity rotation
  `rest * Euler(0,0,-wheelDeg)` for right = positive (`ac_car_formats.md` C3). STEER_LOCK of the installed cars is e.g. 360 (458), 9.3's
  "VERSION=7 / STEER_LOCK 200" numbers are the SDK sample only - all 86 installed cars use tyres.ini VERSION=10.
- 6.3: `steer.ksanim` IS parsed now (`ac_car_formats.md` section 3: v2, 60 `DRIVER:*` nodes) - usable for monitor drivers' arms.
- 10.2: superseded by `ac_physics_sound_rules.md` 2.4 (the AC 1.08 Studio chunk offsets were re-measured there, 86/86 banks).
