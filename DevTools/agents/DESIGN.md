# Assetto Corsa Dreamport - lead design (iteration 1)

Owner: the lead (main session). Every module agent reads this, `DevTools/AGENTS.md` (rewritten for AC by the contracts step) and the
research specs in `DevTools/agents/research/*.md` (verified against the real install - trust their VERIFIED facts and their
"Critic addendum" corrections over anything older).

## The product (what the user asked for)
Assetto Corsa playable inside Somnium Space, as the player's own avatar, VR and monitor, for players who own AC. Nothing from the game
ships: every model, texture, sound, number, string is read at run time from the player's install
(`E:\SteamLibrary\steamapps\common\assettocorsa` here; found via Steam `libraryfolders.vdf`, app 244210, folder `assettocorsa`,
plus a settings override). Like the Euro Truck Simulator Dreamport, and more:
- **Menus inside the Somnium tablet** (an "ASSETTO CORSA" app on the tablet home screen; port of `EtsTabletLink`).
- **Cars**: every owned car (86 here; DLC stubs with only `ui/` are listed greyed "not installed"), every skin, the game's own
  physics numbers from `data.acd`.
- **Entering the car**: walk up to your car (parked in your pit box), open the door (its own `car_DOOR_L/R.ksanim`), sit in, the
  avatar is seated at the driver position (DRIVEREYES / driver_base_pos.knh), the car's own door sound.
- **Steering**: VR - grab the car's own steering wheel (STEER_HR rim) with one or two hands and turn it; triggers = throttle / brake
  (setting: right trigger throttle, left trigger brake; or stick), paddles = A/X (up/down) and grip-click; monitor - keys/mouse
  (W/S or arrows, A/D, Q/E or Shift/Ctrl shift, Space handbrake, R reset to track, C camera, H lights, F/E exit), and a gamepad via the
  Input System if present. Wheel turns visibly in all cases (`steer.ksanim` / STEER_HR node, ratio STEER_LOCK/STEER_RATIO).
- **Passengers**: other players may take the passenger seat (ETS riders pattern) - they ride with the car.
- **Driving** feels like AC: the car's engine curve, gears, diff, tyre DX/DY refs, aero, brakes, ABS/TC from `data.acd`;
  track surfaces (grip per surfaces.ini key), kerbs, grass, walls.
- **Sound**: the car's own FMOD bank samples (FSB5 PCM16) rebuilt into the engine (interior/exterior, on/off load, rpm crossfades),
  gears, turbo, limiter, backfire, skids by surface, wind, collisions, door; common.bank for UI / start lights.
- **Racing (multiplayer)**: the instance master picks track + layout + session (Practice, Qualify, Race with laps or time, Hotlap),
  car restrictions and assists rules in the tablet lobby; START puts every joined player's car on the grid (AC_START_n, ordered by
  qualifying or join order), the avatar seated, AC's starting lights (track `KS_START_LIGHT_*` meshes + HUD lights), jump-start
  detection, laps / sectors via AC_TIME gates, lap validity from surfaces (IS_VALID_TRACK), live positions from `ai/fast_lane.ai`
  progress, pit lane (IS_PITLANE, limiter 80 km/h - ours), finish flag, results table, back to practice. Late joiners spectate /
  join practice. If the master leaves, the next player takes race control (Halo hand-over rule).
- **AI opponents** (stretch, module I): AC-style AI cars following `fast_lane.ai` (speed/gas/brake hints), run by the master,
  drawn by everyone - lets a single player race.
- **HUD**: AC-like essentials (speed, gear, rpm bar, lap / best / last / delta, position, lap count, start lights, flags, pit limiter),
  monitor overlay and a VR panel (dash-anchored, not head-locked while driving), bitmap fonts from `content/fonts`.
- **Networking**: every car, its controls (steer, wheel spin, lights, brake lights, gear, rpm, throttle for sound), door states,
  seat occupancy, passengers, lap/timing data, race control. Remote cars drawn with the real model + skin, interpolated
  (`AcNetTimeline`, ~0.12-0.16 s behind), engine sound from rpm/throttle. Car contact: remote cars are kinematic colliders; the local
  car feels them (one-sided).

## Rules (from DREAMPORT_GUIDE.md - non-negotiable)
Whitelist gate first (Halo's `HaloAccess` list verbatim, renamed `AcAccess`), visitor sees/hears nothing, isolation both ways; no game
assets in the world; `// ours` on every invented number; VR comfort (the seated VR view follows the car by HEADING only - no pitch/roll -
option `vr.horizonlock` default on; never move the VR camera without the player except carrying the seated body); monitor path for
everything; C# 9, no Span/Memory/stackalloc (except SceneNetworking's Fusion 2.1 callback), no unsafe/dynamic/LINQ-per-frame/
closures-per-frame; no new assembly references; no reflection into obfuscated client names; every module call guarded; Player.log
one-time facts with `AcLog.Info("Module: ...")`.

## Names
Prefix `Ac`. Namespace `SomniumSpace.Worlds.SomniumAssetto` (Editor: `.Editor`). asmdef `SomniumAssetto`. Folder
`Assets/#User/SomniumAssetto`. Log tag `[SomniumAssetto]`. Scene `Assets/Assetto Scene.unity`: root "Ac Game" (AcGame),
"Ac Net" (SceneNetworking), slots "Ac Net Player 0..49", "Ac Net World 0..3", "Ac Net Ai 0..8" (ex Actor), "Ac Net Race 0..1"
(ex Mission), "Ac Net Host 0..3". Community Modules asmdef renamed `SomniumAssetto.CommunityModules`. Settings folder
`AssettoDreamport`. The Halo code is moved (not deleted) to `D:\Dreamport\Assetta Corsa\_HaloTemplate\`.

## Code layout and module ownership
| Code | Module | Owns |
|---|---|---|
| lead/K | Skeleton + contracts (lead) | Core/, Game/AcGame*.cs (except owners' hook files), Game/AcSettings.cs, Game/Access/, Game/Shared/ (except owners' `AcLocal.<M>.cs`), Game/Vr/ (AcPlayer, AcXr, AcTabletGuard, AcLaserAim, AcBody, AcFirstPerson - owners may ADD files there only via REQUESTS), Game/Net/SceneNetworking.cs, Editor/AcLab.cs, Editor/AcSceneResave.cs, Editor/LabStubs/, asmdefs, the scene, DevTools scripts, README.md |
| D | Data core (pure C#) | Data/Core/ (AcIni, AcLut, AcJson lenient, AcBinary), Data/Install/ (AcInstall, AcContent catalog: cars, skins, tracks, layouts with ui json + availability), Data/Kn5/ (Kn5 reader: textures, materials, nodes, meshes, skinned; header-only scan; streaming), Data/Acd/ (AcdArchive decrypt), Data/Anim/ (Ksanim, Knh), Data/Track/ (AiSpline, Surfaces, ModelsIni, TrackMarkers), Data/Fmod/ (Fsb5 PCM, FEV event metadata for engine layers), DevTools/harness-ac (AC probes) |
| W | World / track | Game/World/ (AcTextures DDS upload, AcMaterials kn5 shader -> URP, AcKn5Builder generic node->GameObject builder used by Cars too, track build: models.ini, LODs, collision meshes with AcSurface tags, walls, markers hidden, lighting.ini sun/sky/fog, start-light meshes API, track streaming/loading progress, hub showroom), Shaders/, Resources/ (carrier materials), Editor/AcLabWorld.cs |
| C | Car model | Game/Cars/ (AcCarModel: build car kn5 + skin overrides + LODs, named parts: wheels, susp, discs, steer, doors, shifter, wipers, lights/brake lights/emissive groups from lights.ini, digital/analog instruments from digital_instruments.ini/analog_instruments.ini, ksanim player for doors/wipers/shift/steer, rim blur, collider.kn5 hull; one API used for local, remote and AI cars), Editor/AcLabCars.cs |
| P | Physics | Data/Physics/ (AcCarSpec: every physics ini parsed from data.acd: car, engine+power.lut+turbo, drivetrain, gearbox, diff, suspensions, tyres, aero, brakes, electronics, fuel, setup defaults), Game/Physics/ (AcCarPhysics: Rigidbody at CG, raycast wheels with sub-steps, tyre model, drivetrain, ABS/TC/auto-shift, surfaces from AcSurface, reset-to-track helper, telemetry for sound/HUD/net), Game/Shared/AcLocal.Physics.cs, Editor/AcLabPhysics.cs |
| S | Seat & cockpit & driving input | Game/Seat/ (door approach + door animation, enter/exit, seat the avatar (Halo/ETS recipe), passenger seat (riders), carry body root, heading-only VR view, VR steering wheel grab two hands, VR pedals/paddles mapping, monitor keys + gamepad, monitor cameras (cockpit DRIVEREYES, chase, hood, orbit) with Halo's camera hooks, own head hide, hands on wheel posing (monitor avatars and VR when not gripping), tablet guard while seated, AcCarInput producer for the local car), Game/Desk/ (monitor avatar bits), Game/Shared/AcLocal.Seat.cs, Editor/AcLabSeat.cs |
| A | Audio | Game/Audio/ (AcSoundBank loader: FSB5 PCM -> AudioClip, engine model from FEV metadata (int/ext, on/off load layers, pitch/volume curves), gear, turbo, limiter, backfire, skid per surface, wind, collisions, door, horn; common.bank UI + start-light beeps; positional 3D for remote cars; interior/exterior switch by listener), Editor/AcLabAudio.cs |
| F | Front & tablet | Data/Ui/, Game/Front/ (AcTabletLink port of EtsTabletLink, AcTabletApp pages: Drive (car grid with badges, skins with previews, specs from ui_car.json), Track (track/layout with previews, outlines, length, pitboxes), Lobby (master: session type, laps/time, assists rules, start/stop; everyone: joined players, ready), Garage (basic setup: brake bias, fuel, tyre pressure? from setup.ini ranges; assists ABS/TC/auto-shift/auto-clutch, steering), Settings (install folder, VR steering mode, horizon lock, units, volumes, camera), Results/Timing (live standings, lap times); hub (spawn room: showroom kn5 + loading card); fonts; AcUi kit), Game/AcGame.Front.cs, Editor/AcLabFront.cs, Editor/LabStubs/ |
| R | Race | Game/Race/ (AcSession flow Hub -> Loading -> Practice -> Grid -> Countdown -> Racing -> Finished -> Results -> back; spawning in pit boxes / grid / hotlap start with raycast-down onto the road (markers float 0-7.7 m), heading from node +Z flattened; AcStartLights sequence (ours: AC style, 5 reds then out, randomised 1-3 s - ours) driving W's start-light meshes and the HUD; jump start = penalty drive-through? (ours: +5 s); AcTiming: gates AC_TIME_n_L/R crossing (gate 0 = finish), sectors, lap validity (any 2 wheels off IS_VALID_TRACK, or 4? - AC rule: 4 wheels off = cut; ours if unknown), best/last/delta, race distance lap rule (critic T5: grid before/after line); positions by laps + fast_lane.ai progress; pit lane detection + limiter; blue/yellow flags (stretch); results; respawn/reset to track), Game/AcGame.Race.cs, Game/Shared/AcSession.cs + AcRules, Editor/AcLabRace.cs |
| N | Net | Game/Net/ (except SceneNetworking.cs): transport (port HaloFusionTransport, prefixes "Ac Net ..."), slot claim (verbatim), AcNetWire 64-int player record (header ints 0-4 as Halo; payload: car id hash + skin index, pose pos/rot/vel/angvel, steer, wheel spin angles, suspension travel x4 (packed), rpm, gear, throttle/brake/clutch, lights flags, door states, seat occupancy (driver/passenger photon ids), lap count, last/best lap ms, sector times, race progress, flags), AcNetTimeline (from HaloNetTimeline), remote cars (AcRemoteCar via C's AcCarModel + kinematic collider + A's remote engine sound), passengers riding a remote car (S's riders API), master rule (verbatim), race control world blocks (track, layout, session, laps, grid order, start time on the shared clock, results), AI car blocks (for module I), host hand-over, AcNetGame public face incl. `TryHasRecord`, Game/AcGame.Net.cs, Game/Shared/AcLocal.Net.cs, Editor/AcLabNet.cs |
| H | HUD | Data/Hud/ (bitmap font reader content/fonts *.txt+png), Game/Hud/ (AcHud: essentials, lap times, position, delta, rpm bar with LIMITER, gear, speed km/h|mph, start lights, flags, pit limiter, wrong way, messages; monitor screen overlay; VR panel anchored to the car dash; standings tower), Editor/AcLabHud.cs |
| I | AI drivers (stretch) | Game/Ai/ (AcAiDriver: an AcCarInput producer following fast_lane.ai with speed/gas/brake hints, ai.ini, difficulty slider, overtaking lateral offset, recovery; master spawns N AI cars on the grid for races/practice, they use P's physics and C's model; synced through N's AI blocks), Editor/AcLabAi.cs |

Cross-module requests: `DevTools/agents/REQUESTS.md`. Notes: `DevTools/agents/<code>/NOTES.md`.

## Core contracts (the contracts step writes these as compiling stubs; owners fill bodies, only ADD members)
- `AcGame` partial hooks: `FrontStart/FrontBootDone/FrontTick/FrontStop` (F), `NetStart/NetTick/NetStop` (N), `RaceTick/RaceStop` (R),
  plus `BootDone` order: install found -> `AcContent` catalog built on a worker thread -> hooks.
- `AcContent` (D): `Cars` (AcCarEntry: Id, Name, Brand, Class, Owned, Skins[], UiJson fields, BadgePath), `Tracks` (AcTrackEntry:
  Id, Layouts[] (AcLayoutEntry: Id or "", Name, Owned, Pitboxes, LengthM, PreviewPath, OutlinePath, Folders)).
- `AcSession` (R): `Phase {Hub, Loading, Practice, Grid, Countdown, Racing, Finished, Results}`, `Track`, `Layout`, `Rules`
  (AcRules: SessionType, Laps, TimeMinutes, assists allowed, allowed cars, AI count), events `PhaseChanged`, `TrackLoading(progress)`,
  `TrackBuilt`, `Unloading`, `RaceStarted`, `LapCompleted(AcLap)`, `RaceFinished`.
- `AcTrackWorld` (W): `Load(AcLayoutEntry, progress)` coroutine/async, `Root`, `PitBoxes[]`, `StartGrid[]`, `HotlapStart`, `TimeGates[]`,
  `AiLine` (AcAiSpline from D), `SurfaceAt(Collider/hit) -> AcSurface` (key, friction, damping, valid, pitlane, sinHeight...),
  `StartLights` (SetLights(int redCount, bool green)), `Unload()`. Physics layer for the track collision.
- `AcCarModel` (C): `Build(AcCarEntry, skinId, AcCarModelOptions{Lod, Interior, Driver}) -> AcCarModel` with `Root`, `Wheels[4]`
  (transform + radius), `SteeringWheel`, `DriverEye` (model space), `DoorL/DoorR` + `SetDoor(side, 0..1)`, `SetSteer(deg)`,
  `SetWheelState(i, spinDeg, travelM, steerDeg)`, `SetLights(AcLightState)`, `SetInstruments(rpm, speed, gear, ...)`, `Hull` colliders,
  `Dispose()`.
- `AcCarSpec` (P): parsed physics; `AcCarPhysics` (P) MonoBehaviour on the car root: `Init(spec, model, surfaces)`, `Input` (AcCarInput:
  Steer -1..1 or wheel angle, Throttle, Brake, Clutch, Handbrake, ShiftUp/Down, Gear request), `Telemetry` (rpm, gear, speedKmh, wheel
  slip/spin/load/surface per wheel, throttle, boost, limiter, abs/tc active, fuel), `ResetTo(pos, rot)`, `Freeze(bool)`, `Assists`.
- `AcSeat` (S): `Enter(car, seat)`, `Exit()`, `Seated`, `SeatedCar`, `IsDriver`, events; `AcDriveInput` producer -> `AcCarPhysics.Input`.
- `AcCarAudio` (A): `Attach(car, spec, bank, local)`, `Feed(telemetry)`, door/gear/collision one-shots; `AcSfx.Ui(name)`, `AcSfx.StartBeep()`.
- `AcHud` (H): `Show(car)`, `Hide()`, reads AcLocal + AcSession + telemetry; `Message(text, seconds)`.
- `AcNetGame` (N): `Start/Tick/Stop`, `TryHasRecord(photonId, out known)`, `IsMaster`, `MasterName`, `Players` (joined racers with car,
  skin, laps, best, progress, finished time), `RaceControl` (read/write race command by master), `RemoteCars`.
- `AcLocal` (static partial): local car id, skin, seat state, lap data, position - fields per owner partial.
- `AcLayers`: World 0, Avatar 2, Track collision (new, choose a free layer; avatar CharacterController must stand on it), Car (ours,
  excluded from avatar 2|7|24 per guide), Ui 5.

## Iteration-1 user flow
1. Enter world -> gate -> whitelisted: spawn in the hub (the AC showroom room from the install, or the current track's pit lane if a
   session runs). Open the tablet -> ASSETTO CORSA app.
2. Drive page: pick car + skin. Track page (master only, or anyone when alone): pick track/layout -> everyone in the instance loads it
   (loading card with the track preview). Practice starts: you appear in the pit lane beside your car in your pit box.
3. Walk to the car (or press "Get in" in the tablet / E on monitor), door opens, you sit, drive. R / tablet "Back to pits" resets.
4. Lobby page: master sets Race (laps), presses START -> all racers' cars teleported to grid slots, players seated, lights, race,
   finish, results on the tablet + HUD, then back to practice.
5. Exit car: stop, open door (F / grip door handle), step out beside the car.
