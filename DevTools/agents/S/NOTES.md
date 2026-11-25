# S - Seat, cockpit and driving input: NOTES (2026-11-01, second pass after the critic)

## Files (all mine)
`Game/Seat/`: AcSeat.cs (state machine, Bridge seat recipe, carry, VR head, doors, exit, vanish, race hold, N's swap hook), AcSeatGeometry.cs
(seat/eye/wheel/door frames in world space from C's model or the car's files), AcSeatData.cs (worker-loaded install numbers + the ONE
fallback set `AcSeatGlobals.Fallback`), AcDriveInput.cs (the AcCarInput producer), AcKeys.cs (keyboard + AcPad gamepad by Input System
reflection + VR stick shaping), AcVrDrive.cs (VR pedals/paddles/inside door handle), AcWheelGrab.cs (VR rim grab + rim haptics),
AcCarCamera.cs (monitor cameras), AcDoors.cs (get-in DOOR targets on layer 16 + AcDoorHandle), AcRiders.cs (remote bodies on drawn cars +
passenger request API), AcSeatPoses.cs (hands on the wheel, own head hide), AcSeatSettings.cs (rows), AcSeatDrivers.cs (AcSeatEarly -90,
AcSeatLate 9000, **AcSeatCarry 9990**, AcSeatPosesDriver 9997, AcSeatFinal 10000). `Game/Desk/`: AcSeatedArms.cs (two-bone arm IK, grip,
curl), AcDeskCarry.cs (monitor avatar elevator carry). `Game/Shared/AcLocal.Seat.cs` (+ AcCameraMode). `Editor/AcLabSeat.cs`.
Probe: `DevTools/harness-ac/ProbeS.cs` (`seat`, `seatcams`); private build copy `DevTools/agents/S/harness` (output `seat.txt`).

## Public API (contract kept; ADDED members)
AcSeat: Start/Enter/Exit/ForceExit/Stop/Seated/SeatedCar/IsDriver/Entered/Exited (contract) + Kind, Pending, Vr, Started, Geometry,
CanExit(out reason), DoorOpen, PassengerExitKmh 10, DriverExitKmh 3, lab hooks LabRoot/LabHead/LabLeft/LabRight/LabVr.
AcDriveInput: Produce(car), DisarmPedals() (contract) + TabletUp (= AcFront.HoldCar (guarded) or a DisarmPedals call in the last 2 frames), ExitPressed, Last.
AcLocal (S): Seated, Driving, SeatCar, Seat, CameraMode (contract) + SeatRootLocal, SeatYawLocal, WheelHands, PedalsArmed, ViewInside.
AcCameraMode constants (0 cockpit, 1 bonnet, 2 chase near, 3 chase far, 4 orbit, 5 dash, 6 bumper; Cycle order like AC's F1 + orbit).
AcCarCamera.Cycle()/Set(mode)/Mode/HandBack(). AcWheelGrab.Holding/WheelDeg/RimRadius. AcRiders.Asker (N sets), CanGrantPassenger,
PassengerSeatFree, SetRemoteSeatOffset (N calls), GrantLocally (lab). AcSeatPoses.LabAvatar, HandFollowDeg. AcDoors.BoxOf(car), LabCar.
AcVrDrive.Read(ref r, speedKmh) (old Read(ref r) kept), LastLeftPrimaryAt. AcPad.ShapeStick/ResetStick. AcSeatData.G, ReportOnce.
AcSeatSettings.XShiftsDown (seat.vrxdown), FlushCamera. AcSeatGeometry.DoorHandleModel/DoorSkinX. Removed (unused anywhere):
AcVrDrive.ReadPassenger (empty stub), AcSeatCar.Centre (RIG_Center, never used).

## Design decisions
- Frame order (Halo + ETS + AGENTS): early -90 (riders, input, seat write before the client's order-0 avatar placement), late 9000 (pose
  re-entry, seat, VR PlaceHead, wheel grab AFTER the rig is placed, monitor camera; before VRIK 9998), **9990 monitor avatar carry**
  (ETS EtsAvatarSync; before the hands are IK'd at 9997 so the carry never moves posed hands off the rim), 9997 poses (after the
  Animator), 10000 seat re-write only if something moved the body after 9000 (then the carry runs once more; logged once).
- VR view: heading only ALWAYS. `vr.horizonlock` (default on) off only lets a MONITOR body tilt with the car (row text says so).
- Tablet (brief item 7): **kept usable while seated** (it is the menu: get out, back to pits, lobby, camera). B / Y are never ours, so no
  AcTabletGuard. Pedals disarm on any B / Y press and while F's AcFront.HoldCar is true; the wheel grab lets go meanwhile; brake held when stopped.
- Getting out: refused above 3 km/h (driver) / 10 km/h (passenger), and **in the own car while R's phase is Grid / Countdown, or Racing until
  AcLocal.Finished** (AC has no getting out in a race; the same window as R's BackToPits refusal; not when AcRace.Spectating). ForceExit is
  never refused. Every way out resets the car's viewer state (C SetViewer(false), A SetInterior(false)).
- Get-in target = **a box around the door of the seat on offer** (own car: driver's door; remote car with a free passenger seat: passenger
  door): from the seat line to 15 cm proud of the door skin (C's DoorHandle when C found the door animation, else model bounds), from C's
  handle (door rear end) - 10 cm to 0.85 m ahead of the hips, sill to roof (all // ours). Whole car until C's bounds + seat are known.
  Layer 16, own kinematic body following the car (never under its Rigidbody). The door that animates is the seat's side.
- Keys (DESIGN binding over AC defaults where they clash): W/S/Up/Down, A/D/Left/Right, E/LShift up, Q/LCtrl down, Space handbrake, H/L
  lights, N horn (ours), R reset (Input.ResetRequest), C camera, F get out (only while seated; AcFirstPerson holds the client's F/Q/E/R/X off).
  On foot: E near the door = get in. Gamepad (AC X360 defaults): RT/LT, left stick, A/X shift, B handbrake, Y horn, D-pad up lights; ours: RB
  camera, D-pad down get out. Keyboard/pad/VR-stick steering go through AC's own controls.ini filters (numbers from the file; formula ours).
- VR: right trigger gas / left brake (or swapped, or right stick), A up, **quick grip-click away from the rim = shift (right up, LEFT DOWN -
  the default downshift)**; left X shifts down only with `seat.vrxdown` (X is the client's pose-cancel action: each press drops the sitting
  pose and S re-enters it next frame; logged once "dropped the sitting pose right after left X"). Left stick X steers while no hand holds the
  rim (through the [X360] shaping, dead zone >= 0.15), left stick back = handbrake, left stick click = lights, right stick click = horn, grip
  the inside door handle (0.12 m inside C's door skin, 0.25 m above / 0.35 m ahead of the hips, ours - AC has none) = get out.
- Hands: monitor drivers (own + remote desktop) get both wrists on the knh RIG_HAND points, following the wheel up to +-110 deg (ours; AC's
  driver plays a hand-over-hand steer.ksanim over driver3d LOCK (360 typical) that two-bone IK cannot reproduce). **VR avatars are NOT posed
  onto the wheel, gripping or not** (decision): VRIK places them from the controllers at 9998, and a hand drawn on the rim while the real hand
  is elsewhere breaks the player's own hand-eye match; remote viewers see a VR driver's hands where they really are.
- Eyes: VR = system/cfg/vr.ini [car] DRIVEREYES when present (42/86 cars), else car.ini DRIVEREYES; monitor cockpit = car.ini DRIVEREYES
  pitched by ON_BOARD_PITCH_ANGLE, FOV cfg/camera_onboard.ini 56, mouse look limited to HEAD_MAX_DEGREES (60) yaw / 45 pitch (ours).
  Chase = system/cfg/chase_cam.ini CHASE_0/1 - [BELIEF] level view + PITCH (> 0 = down), distance from the car's rear.
- Steering lock = car.ini STEER_LOCK read by S from data.acd (P's spec as fallback); wheel sign = C3 (clockwise as seen = right = +).
- Passenger in a remote car: N rebuilds a parked remote car near me with COCKPIT_HR; if I boarded the outside build, N swaps the rig under me
  through `AcNetGame.SwapSeatedCar` = S's hook (seat, geometry, door, viewer state move over; SeatChanged raised; nothing released).
- Fallback numbers live once in `AcSeatGlobals` (marked "// ours (install value, used only when the file is missing)"), read via AcSeatData.G.
- Door timing 0.6 / 0.5 / 0.7 s is ours: car_DOOR_L.ksanim has 100 frames (71 cars) / 101 (6) and no frame rate (harness census).

## Verified (offline)
- build.sh 2026-11-01 (second pass): runtime / editor tools 0 errors in the whole tree; check.sh "all 117 Fusion/Somnium members are proven"
  (before N's swap hook; re-run below). Later runs: my folders 0 errors (other modules' files were mid-edit).
- Harness `seat` on the real install, 86/86 owned cars: knh joints 84/86 (ks_alfa_giulia_qv, ks_mazda_787b have no knh -> eyes fallback);
  STEER_HR/LR 86/86; rim mesh outer radius 0.13..0.358 m; knh wrists on the rim 79/84 at clock angles -97..-104 (left) / +100..+104 (right)
  - confirms the angle convention; DOOR_L 77 / DOOR_R 74, car_door_L/R.ksanim 77/77 (frames 100 x71, 101 x6); vr.ini DRIVEREYES 42;
  STEER_LOCK 180..585 (450 x25); STEER_RATIO negative on 16 cars (P: magnitude only); ON_BOARD_PITCH_ANGLE 86 all in -7.8..0 deg.
- Harness `seatcams`: chase_cam.ini (3.0/1.4/0.0349, 3.9/1.9/-0.0175), camera_onboard FOV 56 / IS_WORLD_ALIGNED 0 / HEAD_MAX 60,
  controls.ini [KEYBOARD] 1.75/2.5/0.18/1.8, [X360] gamma 3.01, filter 0.601, sens 0.5, dz 0.05, speed 0.2.
- Harness `acd`: driver3d.ini [STEER_ANIMATION] LOCK = 360 (abarth500, ferrari_458), 180 (lotus_exos_125) - why the hands stop at 110.
- By reading: R's SeatDriver (AcRace.Flow) only re-enters while Grid/Countdown and now meets a refused Exit; F's GET OUT shows CanExit's reason.

## NOT verified (needs the lab, then a client run)
- Nothing visual yet: no lab run in this phase. `seat-enter` is written (below).
- Client-only (Bridge): pose SittingStraight entry + recentre undo, the left-X pose drop + re-entry, PoseLowered head-on-eye shift on a
  monitor, DoTeleportToPoint exit, CinemachineBrain hold by name, XRSimpleInteractable / pointer click on the door box, AcFirstPerson key
  hold, remote rider binding (NetworkBehaviour search, back-off), own head hide scale, hands-on-wheel IK on real avatars, Input System pad,
  N's SwapSeatedCar (two clients).
- Unknown semantics marked // ours: chase DISTANCE from the car's rear and the PITCH model/sign [BELIEF]; keyboard STEER_GAIN formula;
  X360 filter/rate per 60 Hz frame; door timing; door box margins; inside door handle point; rim haptics levels; hand follow 110 deg.

## Lab scenario (run later: `bash DevTools/lab.sh seat-enter [-accar ferrari_458]`)
Spawns the car frozen at RoomOrigin+(0,0.6,6) (AcDoors.LabCar), stand-in body, then checks: get-in target shown, solid, layer 16, on the
LEFT door and not the whole car (centre x < -0.3, size < 1.3 x .. x 2.6 m); pressing it (AcDoorHandle.OnActivated) seats the driver; the
target hides; Occupied + Driven; body root on the knh hips (< 1 cm); driver seat left (LHD); steering wheel 0.3-0.8 m ahead / 0.1-0.45 m below
the eye; cockpit camera at DRIVEREYES; car pitched 8 / rolled 5 -> root tilt 0 and heading = car's; VR head on the eye (< 1 cm), still
level; pedals disarmed while the entry trigger is held, armed after release; left X does not shift down; rim measured 0.1-0.4 m; right hand
at 3 o'clock + grip -> holding; hand moved 30 deg clockwise -> WheelDeg +30 +-2 and Input.SteerAbsolute; C's STEER_HR at +30 moves the rim
mesh's own top vertex to the driver's right (independent sign check); grip released -> let go; CanExit true in the hub; Exit -> door
opening (waits on DoorOpen, not frames), stood > 1 m left, car parked; passenger in a second Remote rig via AcRiders.GrantLocally -> seated
as passenger on the right seat (< 1 cm), occupied not driven, Exit -> stood right; ForceExit releases + parks; the car disposed under a
seated player -> released and landed beside. Pictures to LOOK at: seat-cockpit.png (wheel ahead, dash), seat-chase.png and
seat-chase-far.png (compare with AC's F1 chase views: car placement and horizon - the PITCH belief), seat-bonnet.png, seat-wheel-30.png
(wheel turned right), seat-door.png (left door open). Not covered: hands on the wheel (no humanoid avatar ships), race-phase refusal
(AcSession setters are R's), Bridge pose.

## Client test steps for the user (Player.log lines `[SomniumAssetto] Seat: ...`)
1. Load a track, walk to your car: `Seat: a car can be got into: ...`, `Seat: get-in target = the left door of <car> (W x H x L m, ...)`.
   Click the DRIVER'S DOOR / E near it (monitor) or point at the door + trigger (VR). Also check: the VR teleport ray and desktop clicks near
   OTHER players' cars are not caught by their door boxes (layer 16) more than you would like.
2. `Seat: entered <car> as driver (pose entered, rig recentred|monitor, head hidden; eye vr.ini|car.ini DRIVEREYES, heading-only yes)`;
   `Seat: <car> seat data - knh yes, ...` (a `seat data read failed` warning means data.acd / knh did not read); `Seat: game cameras /
   controls - ...`; monitor: `Seat: camera cockpit (brain held, fov 56)`; C cycles cameras (one line per mode).
3. VR: `Seat: VR pedals armed`, `Seat: wheel ready in <car> (rim radius X m, lock Y deg ...)`, `Seat: grip report L/R`, `Seat: VR wheel
   taken with ...`; turn the wheel; quick LEFT grip-click away from the rim = downshift, A = upshift. Press left X once: expect
   `Seat: the client dropped the sitting pose right after left X ... re-entered at once` and NO gear change (unless seat.vrxdown is on);
   note whether the view jumped. Check: the horizon never tilts.
4. Monitor: W/S/A/D drive, E/Q shift, F (stopped) gets out: `Seat: got out at (x, y, z)`; the client's camera comes back. Getting out of a
   car you looked at from the cockpit: its driver model / cockpit go back to the outside look.
5. Race: on the grid / during the race F and the tablet's GET OUT say "the race is about to start ..." / "the race is running ..." and you
   stay seated (no in-out flicker); after the flag you can get out.
6. Two players: B clicks A's passenger door -> `Seat: asking A for the passenger seat` -> `ride granted` -> B rides; A sees B seated
   (`Seat: rider ... bound` or, after ~1 min, `still unbound after 15 searches`), every 60 s a rider line; if B boarded before N's cockpit
   rebuild: `Seat: N rebuilt the car I ride in ... the seat moved over`.

## Open / requested (REQUESTS.md)
- lead (optional): AcFirstPerson to also hold Sprint / Jump / Navigate while seated (Shift / Space / WASD are S's keys then).
- N: AcRiders.Asker + CanGrantPassenger + ride-in publishing + SetRemoteSeatOffset + rig.OwnerId - DONE N; SwapSeatedCar - plugged by S.
- P: input semantics / edges / STEER_RATIO sign - DONE P. P forwards Input.Horn to A (answered A -> S horn: done S).
- C: keep ReadableSteer, HandL/R, SetViewer, DoorHandle/HasDoor (now used for the door box and the inside handle).
- Answered F -> S (HoldCar honoured, CanExit/Exit/Cycle for tablet buttons); R + F told about the race hold of CanExit.

## Lab pass 2026-11-01 (seat-enter run 3x, last run PASSED 38/38 checks)
- Fixed: passenger grant in the lab never happened - N installs AcRiders.Asker even offline and it refused the owner-less car;
  `AcRiders.GrantLocally` (lab flag) is now checked before the Asker. Run 3: passenger on the right seat (< 1 cm), occupied not driven, exit right 1.83 m.
- Fixed: bonnet / bumper cameras sat INSIDE the bonnet (seat-bonnet.png was black). car.ini BONNET/BUMPER_CAMERA_POS are PHYSICS space:
  model = pos - GRAPHICS_OFFSET (private harness `agents/S/harness` ProbeS2 `camspace` on 458/abarth/miata/e30: read as model space they are
  0.2-0.5 m inside the body, as physics space 0.0-0.27 m above it). BONNET/BUMPER_CAMERA_PITCH now applied (AcSeatCar.BonnetPitchDeg/BumperPitchDeg).
  Picture: level view over the 458's red bonnet edge.
- Lab only: F's hub menu panel and the hub showroom car (abarth) stood between the chase cameras and the lab car - hidden for the pictures.
- Pictures LOOKED at: seat-cockpit (458 cockpit, level, wheel ahead with the cluster visible through it, mirror/windscreen clear),
  seat-wheel-30 (rim turned clockwise), seat-chase (458 from behind, centred low in the frame, level horizon), seat-chase-far (higher/farther,
  level), seat-bonnet, seat-door (left door open, body stood 1.83 m left). Checks verified: door box layer 16 on the left door, hips on knh
  < 1 mm, wheel 0.52 ahead / 0.23 below the eye, cockpit cam at DRIVEREYES, monitor + VR root tilt 0 with the car pitched 8 / rolled 5, VR head
  on vr.ini DRIVEREYES, pedals armed only after trigger release, wheel grab +30 deg -> SteerAbsolute 30.0, ForceExit, car disposed under player.
- Still open: the driver door is still OPEN in seat-chase.png ~30 frames after getting in (batch frames are short: likely the 0.7 s close, not
  verified); chase framing vs AC's F1 views not compared with a real AC screenshot.

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3, check.sh all 117 proven; no lab run)
- AcSeatGlobals fallbacks replaced by NEUTRAL ours values (they were copies of chase_cam.ini / camera_onboard.ini / controls.ini, against the
  ship-no-game-data rule): chase 3.5/4.5 m, height 1.5/2.0 m, pitch 0; FOV 60; HEAD_MAX 45; keyboard min 0, speed 1.5, opposite 2, gain 0.2,
  reset 2; pad gamma 2, filter 0.5, speed sensitivity 0.4, deadzone 0.1, speed 0.25. The install's files still win whenever they are read.
  (The "Fallback numbers ... install value" line above is superseded by this.)
- VR wheel let go (both hands, tracking loss, tablet, DisarmPedals): no one-frame snap - AcDriveInput keeps SteerAbsolute and returns the release
  angle to 0 at controls.ini STEER_RESET_SPEED x lock per second; the left stick takes over from that angle (AcPad.SeedStick). Log once:
  "Seat: the wheel was let go at N deg ...".
- Tablet up (VR): shift up/down, lights, horn, exit, handbrake and stick steering are dropped too; grip-click timers reset (a squeeze on the
  tablet never becomes a shift); the inside door handle is ignored.
- B/Y pedal disarm now lasts: no re-arm on the press frame, at least AcVrDrive.RearmDelay 0.5 s (ours), then the tablet seen closed (link known)
  or - the link never reported the tablet - assumed closed after the next B/Y tap (log once "the tablet's state is unknown ...").
- AcSeatGeometry.Yaw: computed once per frame, held while the flattened forward is short (pitch beyond ~60 deg, YawHoldBelow 0.25 ours),
  rate-limited 720 deg/s (ours), snaps on a body jump > 5 m (reset) - no 180 deg whip in a tumble.
- AcCarCamera: CinemachineBrain re-enabled on hand-back only if we disabled it; C does not cycle while a text field has focus (AcKeys.Typing internal).
- AcPad.Bind throttled to 1 Hz with or without a pad (was a reflection call per frame with none). AcPad.Bound added.
- Lead files: AcHeadHide.Unhook now called by AcSeat.Stop / AcSeatPoses.Stop (the static render hook no longer outlives the world); the
  unused Halo AcViewScale (same leak) deleted. AcFirstPerson also holds the client's "Camera Preset", "Zoom", "Second Camera Action",
  "Gamepad FreeView Enable", "Gamepad Zoom" while seated, and "Exit" (gamepad Y = our horn = pose cancel; also ESC) only while a gamepad is bound.
- Not verified in a lab or client: compiled only.

## Last-gaps pass 2026-11-01 (integration re-run after the road-plane damper fix; no S code changed)
- seat-enter PASS 38/38 (1 run), numbers as in the lab pass (hips 0.000 m, wheel 0.52 / 0.23, wheel grab 29.8 deg, exits at x -1.83 / +1.83,
  ForceExit, dispose -> body 1.99 m beside). Pictures LOOKED at: seat-cockpit (level, cluster through the rim), seat-chase (458 centred,
  level; driver door still open ~30 frames after getting in, as before), seat-wheel-30 (rim turned clockwise).

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 8 "S optional" - done)
- NEW `Game/Seat/AcSeatDamagePulse.cs`, hooked in `AcSeat.Subscribe` / `Stop`: `AcEvents.CarDamaged` (Zones or Suspension, kmh > 0) on the car
  I sit in (`AcLocal.SeatCar`) -> `AcXr.Pulse` both hands, amplitude min(1, kmh / MAX_DAMAGE_KMH (60, read)), 0.12 s // ours, nothing under
  0.05 // ours. No camera shake. AcXr.Pulse keeps the "haptics" setting and no-headset rules; CarDamaged never fires at DAMAGE 0.
- build.sh 0 errors x3, check.sh 117 proven. Not lab-verified (needs a headset; compiled only).
