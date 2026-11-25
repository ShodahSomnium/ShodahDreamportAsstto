# V notes (Vehicles)

Status: complete; offline verified in the lab (vehicles: passed, 2026-10-19). Net link coded against N's live API (HaloNetGame.World) - NOT yet seen with two clients.

## Files
- `Data/Vehicles/HaloVehicleDef.cs` - vehi/unit/object reader: type, speeds (wu/tick), turn, slide, seats (label, marker, flags,
  yaw/pitch rates + ranges, camera marker, camera track, hud), weapons, attachments, physics (mass points, powered points, antigrav,
  inertia), collision model (vitality, regions, materials, bbox), animation graph vehicle block (aim limits, suspension depths per
  mass point), camera tracks (trak control points). Weapons / projectiles / damage use Combat's Data/Combat readers.
- `Game/Vehicles/HaloVehicles.cs` - the stub facade: Spawn(rules) (spawn flags x vehicle set), ExitSeat, Clear, + All, Find(id),
  IsVehicleWeapon, ReplayShot (for Net), RespawnAbandoned, AddLab.
- `HaloVehicle.cs` (+ `.Drive.cs`) - one vehicle: model, Rigidbody, contact spheres = Halo mass points, coll BSP hit meshes per node
  (non-convex, touch nothing), seats, wheels (raycast suspension from the antr suspension depths), antigrav points, node animation
  (steering wheel, front hubs, tyre spin, suspension travel, gun yaw/pitch, spinning barrels), flip, respawn, IHaloDamageable.
  Driving: jeep (wheel grip + drive force, steer to view or stick), tank (treads, yaw servo), scout (antigrav hover, heading servo,
  strafe), fighter (weightless while powered, flies toward the view), turret (stationary). Splatter by overlap on the Hitbox layer.
- `HaloVehicleGun.cs` - vehicle weapon (rate ramp, magazine + chamber time, error cone, alternating muzzles, firing effect,
  HaloEvents.RaiseFired) + `HaloVehicleShots` fallback projectiles (until Combat exposes FireProjectile).
- `HaloVehicleSeat.cs` - the local seat (ETS EtsTruckSeat port) + `HaloAvatarHide`.
- `HaloVehicleCamera.cs` - monitor camera. `HaloVehicleInput.cs` - controls. `HaloVehicleEntry.cs` - getting in.
- `HaloVehicleRiders.cs` - remote riders on my drawn vehicle (ETS rider technique). `HaloVehicleNet.cs` - wire adapter (offline now).
- `HaloVehicleDirector.cs` - frame order (-90 early, 10000 late). `Game/Shared/HaloLocal.Vehicles.cs`. `Editor/HaloLabVehicles.cs`.

## Facts from the tags (Blood Gulch, harness `vehi` in DevTools/agents/V/harness)
- Halo CE MP vehicles are INVULNERABLE: every MP coll has body vitality 0 (cyborg reads 75/75 with the same reader). So no vehicle
  destruction; "damage" = push + rider damage fraction (0.18 warthog) passed to a local rider. Abandoned vehicles respawn (30 s, ours).
- Speeds are wu/tick: warthog 0.275 = 25.1 m/s, accel 0.0028 = 7.7 m/s^2; ghost 0.225 = 20.6 m/s; banshee 0.22; scorpion 0.14.
- Seat entry: CE has no enter radius - the model has "<seat marker> enter" markers ("driver enter", "passenger lf enter").
- Warthog gunner seat has "gunner left/right hand" grips on "frame barrels" and foot markers (standing seat).
- Camera tracks: warthog_driver_loose middle point (-2.75, 0, 0.68) wu = 8.4 m behind, 2.1 m up (client's own view: 7.3 / 1.74).
- MP spawn flags: bits 0-3 default in slayer/ctf/king/oddball, bits 8-11 allowed. Blood Gulch slayer default = 2 warthogs + 2 rocket warthogs.

## How the Somnium client does vehicles (studied E:\Somnium Space\Main Build; what we copied)
- Desktop: the player's own Cinemachine FreeLook follows the body moved to the vehicle's bounds centre; view preset 0 ~7.3 m back,
  1.74 m up; collides with layers 14/16/23 only. We: hold the brain off (EtsTruckCamera way) and place the camera ourselves with Halo's
  camera track around the vehicle centre (or the seat's camera marker), mouse orbit always (Halo), collision 0/14/16/23, radius 0.1.
- VR: the client goes THIRD PERSON while driving: the HMD camera is re-parented under a rig at the vehicle - heading only, Lerp 2*dt
  toward the vehicle heading (reversing: looks back), 4 m up, 6 + speed/10 (max 10) m behind; left Y switches first/third person;
  the avatar is IK-retargeted into the seat. A world cannot re-parent the client's HMD camera or retarget VRIK, so we move the XR
  rig so the head lands at that chase point (ETS PlaceHead, lean kept), hide our own avatar on our screen (hips bone scale, not
  networked) and every other client draws our body in the seat (HaloVehicleRiders: head forced to the seat eye when far from it).
  Heading only - no pitch/roll (comfort). Setting `vehicles.vrthird` (1 default) keeps the Y toggle.
- VR bindings copied from XRI Vehicle inputs: right trigger accelerate, left trigger brake/reverse, left stick X steer, right B exit,
  left Y camera switch. For drivers who also shoot (ghost/banshee/scorpion/turret): left stick drives, right stick X turns, right
  trigger fires where the right hand points, left trigger = secondary.
- VR steering choice: STICK by default (head stays free to look around); Halo's "drive where you look" (head heading) is
  `vehicles.vrsteer = 1`. Banshee always flies toward the head's gaze when the right stick is centred.
- Monitor = Halo PC: W/S throttle, A/D turn (warthog/scorpion) or strafe (ghost/banshee), mouse looks and the vehicle steers toward
  the view, LMB/RMB triggers, Space brake, E in/out. VR in: grip/trigger on the vehicle (ETS door handle: layer 16 box +
  XRSimpleInteractable + IPointerClickHandler) or A.

## Passengers (ETS logic)
- Seat: Bridge motion/collision/gravity/jump/teleport off, falling anim + VRIK legs off, Poses.SetPose(SittingStraight) every frame
  (re-entered when the client drops it; UndoPoseRecentre in VR), capsule excludes layer 28, vehicle shapes exclude 2|7|24 while
  anyone local sits in it; parked vehicles kinematic (settle 3 s dynamic after spawn/release with avatar layers excluded).
- Seat written early (Update order -90, before the client's order-0 avatar placement) and again late (LateUpdate 10000).
- Remote riders: ETS EtsRiders Correct() on Fusion Body/Root, Head, LeftHand, RightHand (found by StateAuthority.PlayerId) + Bridge
  transforms, early and late; IK legs + falling anim off for them.

## Net (coded, not verified with 2 clients)
- HaloVehicleNet: occupants from HaloNetGame.Players (VehicleIndex/Seat/Aim/PlayerId), remote driver -> TryDriven (kinematic), master
  PublishParked / others TryParked, local driver PublishDriving + PublishAim; seat clash: lower slot keeps it. Net reads HaloLocal.VehicleId/
  SeatIndex/Driving. Remote vehicle-gun shots: V listens to HaloEvents.Fired (who != me) and replays (Net skips its own replay for them).
- Guns fire through HaloCombat.FireProjectile (V's HaloVehicleShots only when Combat has no catalog). Lights/engine/contrails via HaloFx.Attach.

## Open
- Two-client test of the net link (riders, remote driver, parked sync, seat clash). VR: none of the headset paths (third-person rig, hide,
  grips, stick driving) can run in the lab - first proof will be a client run (Player.log lines below).
- Monitor: client camera brain hold + cursor lock not seen in the client yet.
- Banshee tricks: CE has none (the tag's "flipping angular velocity" is for overturned vehicles; we flip those on E).
- Desk: own-head hiding for first-person seats on a monitor is left to D (scorpion riders are the only first-person seats).

## Verified (lab "vehicles", Blood Gulch tags, flat floor, pictures looked at in DevTools/lab/vehicles/)
- All 6 MP vehicles build (models, seats, markers, grips, camera tracks, contact spheres, 3-16 coll hit meshes) and settle upright
  (banshee rests on wingtips+nose, 22 deg). Warthog side picture: tyres on the floor, hull at Halo's height (suspension = antr depths).
- A bullet from above / the side hits the coll BSP hit mesh ("hit frame hull"), not the contact spheres (layer 31).
- Warthog: top 24.8 m/s (tag 25.1), 0-20 in 3.8 s, 37 deg in 1.5 s at full steer, steers toward the view, brakes to 0; splatters a
  hit-box dummy at 22 m/s (Instant). Exit -> parked kinematic. Gunner: chain gun turns to yaw -90 (picture), 17 rounds in 1.5 s
  (8->15/s ramp) through Combat's projectiles. Ghost: hovers 0.57 m level, 20.5 m/s (tag 20.6), 6 bolts/s. Banshee: climbs 20 m in
  4 s toward the aim, holds height with no throttle. Scorpion: 12.8 m/s (tag 12.8), turret aims, one shell per pull (picture with smoke).
- Chase pictures from Halo's camera tracks (warthog_chase_*, ghost_chase_firing, banshee_flying, scorpion_cannon).

## Player.log lines
`Vehicles: placed N of M scenario vehicles for <type> (vehicle set k): ...`, `Vehicles: net online|not up ...`, `Vehicles: prompt ...`,
`Vehicles: seated in <vehicle> seat i 'label' (driver/gunner/rider, sitting/standing, headset/monitor, third/first person)`,
`Vehicles: seat pose SittingStraight entered ...`, `Vehicles: VR seat - the camera rides on the body ...`, `Vehicles: own avatar hidden ...`,
`Vehicles: monitor vehicle camera on (Halo chase track ...); ... CinemachineBrain disabled`, `Vehicles: <v> powered up: n looping sound(s)...`,
`Vehicles: vehicle guns fire the Combat module's projectiles`, `Vehicles: got out of ...`, `Vehicles: rider <id> in <v> seat s: network body bound`,
`Vehicles: <v> is driven by another player`, `Vehicles: <v> hit a player at x m/s (splatter)`, `Vehicles: <v> respawned at its scenario spot.`

## Seat animations (2026-10-19, after the first client test: "put avatar in vehicle when entering, same animations as real Halo")
- `Game/Vehicles/HaloSeatPoses.cs`: `HaloSeatPuppet` samples the cyborg's own seat clips (antr unit seat of the seat label: enter [7],
  exit [8], class idle [0] + aim-still overlay [25] on the class's yaw/pitch grid, acc-front-back / acc-left-right overlays [2][3]) into
  Desk's HaloPuppetSample seat joints; `HaloSeatPoses` (LateUpdate 9997) lays them on every seated avatar (own + remote riders) with Desk's
  HaloPuppetPoser.ApplySeated + HaloAvatarCopies (mirror / shadow copies). Enter on sitting down, exit clip on getting out (own and remote).
  Warthog driver: hands on the wheel grips (seat IK), body into the steering; gunner: on the turning mount, aim-still pitch = gun pitch,
  hands on "gunner left/right hand"; passenger: aim-still from where the head looks.
- Why the client log said "hips False, arms False": Desk's seated path passes hips/arms = monitor only (VR got legs only). Now VR =
  hips + legs + torso from the animation, arms IK'd to the tracked controllers (hand rotation kept from the controllers); a VR driver in
  the third-person view gets the whole body on the seat with hands on the wheel (the own avatar is no longer hidden there).
- VR comfort: the view CUTS (teleport) into the seat and out to the exit point; only the body plays enter / exit. The exit point is where
  Halo's exit clip ends (HaloSeatPoses.ExitPoint), so the body lands on the player's real spot.
- Desk held off while V poses: HaloDesk.PoseSeated = false, PoseOwn = false while the own avatar is V's. Asked D (REQUESTS.md) for one line:
  Remote(): `if (HaloSeatPoses.OwnsSlot(r.Slot)) { Drop(e); return; }` - until then a remote MONITOR rider is posed twice.
- Lab "vehicles-seats" (passed, pictures looked at): enter_* (driver climbs in from the side), warthog_seated_* (driver on the wheel,
  passenger, gunner on the grips), warthog_steer_gun (gunner turned with the gun), ghost / scorpion (driver in the hatch, rider on the
  fender) / banshee (prone pilot) seated, exit_* (all seven play their exit clips). Hips land 0.00-0.07 m from the seat markers.

## Iteration-2 VR fixes (2026-10-19, user test on Death Island)
- Tablet: while seated in a headset and 1 s after getting out, V calls Combat's HaloTabletGuard.Update(true) (the client's B / Y tablet
  actions off; B gets out, Y switches the view). `HaloVehicleDirector.TabletHeld`.
- Head: V never scales a head. The client shrinks the own head for first person; in the VR third-person view V gives the own main copy's
  head its size back on this screen only and puts the client's scale back for first person / on getting out (`HaloSeatPoses.OwnHead`).
- Gunner: either trigger fires in the gunner seat, on the grips or not; on the grips the gun turns so the grips follow the hands, off them
  it turns to where the firing hand points. First shot per seat/trigger is logged ("Vehicles: first shot from ...").
- Seat choice: seats flagged "not valid without driver" (the scorpion's 4 riders) are only offered once somebody drives; the driver seat
  is also reachable from beside the hull (counted 1 m from its door). The prompt and the press use the same choice (`HaloVehicleEntry.Choose`).
- Ghost / banshee guns are fixed: aim yaw / pitch 0, bolts along the hull (VR and monitor). Scorpion: left stick drives / turns, cannon
  turret follows the head, right trigger = cannon (one shell per pull, 4 s chamber), left trigger = machine gun.
- Lab "vehicles-vr" (passed): simulated Touch controllers through HaloXr.Simulated + stand-in hands/head (HaloVehicleInput.LabVr).

## Campaign (phase 2)
- `HaloVehicles.SpawnPlacement(...)` (2 forms) + `Remove(v)`; `HaloVehicle.Campaign.cs`: Campaign flag (no MP respawn), ObjectName (HaloObjects),
  Vitality from the coll's body (campaign banshee 100, ghost 160; MP tags 0), Destroyed (effect: coll destroyed / depleted / region / vehicle
  effect; riders out; not enterable), units in seats (UnitIn / SeatUnit / UnseatUnit; NoteUnits marks seats with a HaloUnit child taken),
  AI surface SetAiInput / SteerToward / AiDriven (AiStep in the director), Hover(bool), Open(bool) (logs), EnterableByPlayer.
- Vehicle contact spheres now always exclude the Hitbox layer (24): a rider's kinematic hit boxes under the seat marker pinned the hull.
- Lab "vehicles-campaign" (a30, passed): 'jeep' placed with full rotation (roll 9.9) and registered; marine gunner fires the chain gun at
  its aim point (picture campaign_ai_gunner_firing), marine driver drives 18.5 m in 3 s, banshee destroyed by 120 of 100 points.
- unit_open / unit_close (2026-10-19): `HaloVehicle.Open(bool)` plays the vehicle's own graph's unit seat "stand" slot 27 "opening" / 28
  "closing" (none: opening backwards) on the model nodes over the clip's length, held at the end; `IsOpen`, `HasHatch`. A vehicle with a
  hatch lets the local player out only while open; Action (E / VR A or B) gets out. a10 cryotube: 'stand opening' 154 frames (5.13 s),
  door swings 90 deg (lab vehicles-cryo, passed, pictures cryo_closed / cryo_opening / cryo_open). Dropship: opening / closing 14 frames.
  Pelicans have no opening animation in their graph (logged once). Vehicles without a physics tag (cryotube) are never settled.
- a30 dropship / banshee (2026-10-19, lab vehicles-dropship passed): lz_cship placed by name, teleported to lz_cship_flag, 4 grunts in the
  CD-passenger seats, unit_close, recording_play_and_hover lz_cship_enter (Q's kinematic path: 1037 ticks, flew 199 m) then HOVERS (0.00 m in
  2 s; human-plane hulls never settle / drop, Hover(false) keeps them kinematic), engine loop runs while a recording plays, unit_open plays
  'stand opening' (14 frames), UnseatUnit drops each grunt under its side pod - they fall and land (4/4). Banshee: SetAiFlight (3D), banking
  of the model into turns (the rider's view stays level), CE has no boost / tricks (the tag has none).
- SFX (2026-10-20, lab vehicles-sfx passed): engine loops (HaloFx.Attach sounds, scale = speed / tag top speed) on every vehicle incl. AI-driven,
  recording-flown and co-op followers (Velocity = measured transform motion when kinematic without a net velocity); tyre slip (HaloImpacts
  TireSlip, 8 in a hard warthog turn), crash (vehi crash sound + vehicle.foot effect 10), suspension slam (vehi suspension sound). Banshee
  dive whoosh = its banshee_contrail.lsnd attachment scaled by speed. Vehicle explosion = the coll / region destroyed effect (its sounds). No
  horn: Halo CE vehicles have none (no tag field).
