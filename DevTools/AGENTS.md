# Assetto Corsa Dreamport - shared brief for module agents

You are one of ten agents building the **Assetto Corsa Dreamport** for Somnium Space (a Unity 6000.3.5f2 URP world) in parallel, each
owning one module (W C P S A F R N H I). The lead set up the project, the data core (module D, finished), the scene and the contracts
below. Read, in this order: this file, `DevTools/agents/DESIGN.md` (the lead design - BINDING), your own `DevTools/agents/<code>/BRIEF.md`,
the research specs your brief lists in `DevTools/agents/research/*.md` (each ends with a "Critic addendum" that OVERRIDES earlier
statements in that file), and `D:/Dreamport/Assetta Corsa/DREAMPORT_GUIDE.md` (the rules every Dreamport follows).

## The product and the rules (non-negotiable)
- Assetto Corsa playable inside Somnium as the player's own avatar, VR first-class and monitor (non-XR) too, for players who own AC.
  The user flow is DESIGN.md "Iteration-1 user flow" (hub -> tablet app -> drive / track -> pit box -> walk to the car -> door -> sit ->
  drive -> race lobby -> grid -> lights -> race -> results).
- **Ship no game assets.** Every model, texture, sound, number and string is read at run time from the player's install
  (`E:\SteamLibrary\steamapps\common\assettocorsa` here, found by `AcInstall.Find` via Steam libraryfolders.vdf app 244210, plus the
  `AcSettings.InstallOverride` setting). Our code holds only names, offsets and the numbers the files do not have: mark every invented
  number `// ours`.
- "Exactly like the game": read AC's own values (data.acd physics, surfaces.ini, sounds from the car's bank, ui json, gui art, fonts,
  server_cfg names for rules). The user judges every build against AC.
- **The whitelist gate runs first**; a visitor sees and hears nothing. Nothing may start outside AcGame's hooks (below); never add a
  MonoBehaviour to the scene or anything that runs in its own Awake/Start before the gate. The gate code in AcGame is frozen.
- **VR comfort:** the seated VR view follows the car by HEADING only (no pitch/roll; option `vr.horizonlock`, default on); never move the
  VR camera without the player except carrying the seated body. Every feature also needs a monitor path (keys, mouse, cameras).
- **C# 9.** No `Span`/`Memory`/`stackalloc` (only SceneNetworking's Fusion 2.1 callback has one; build.sh checks), no `unsafe`, no
  `dynamic`, no new assembly references (allowed: UnityEngine modules, UI, TMP, XRI, URP, Fusion, SomniumSpace.Network, the Bridge),
  no reflection into the Somnium client by class/field name (it is obfuscated; public Bridge types and the Input System by action name are
  fine). Per-frame code: no LINQ, no lambdas/closures (a lambda capturing a parameter or local allocates its closure at METHOD ENTRY -
  put such code in a separate method), no allocations in steady state. `OnAudioFilterRead` is not usable (AudioClip.Create + SetData).
- `Data/` is Unity-FREE pure C# (the dotnet harness compiles it). `Game/` is Unity. Every call from another module goes through
  `AcGame.Guard` / try-catch: a missing piece is one log line, never a crash.
- The user tests uploads in the Somnium client and reads `Player.log`
  (`C:\Users\derii\AppData\LocalLow\Somnium Space Ltd\Somnium Space VR\Player.log`). Log one-time facts with
  `AcLog.Info("Module: ...")` / `AcLog.InfoOnce(...)` (tag `[SomniumAssetto]`), so a client run proves what happened. No per-frame logs.
- Report honestly: say what is verified (harness numbers, a lab picture you LOOKED at, a log line) and what is not.
- **Secrecy:** the scene's upload name / description (WorldSDKConfig AssetName / AssetDescription) and preview picture must never name
  the game, "Dreamport" or the whitelist. Keep the neutral "Wheel Test" / "Another small scripting test for internal use, please ignore."

## Project
- Unity project `D:\Dreamport\Assetta Corsa` (NOT open in Unity now; **never kill Unity.exe** - other editors run on this machine).
  Code: `Assets/#User/SomniumAssetto` (asmdef `SomniumAssetto`, ONE namespace `SomniumSpace.Worlds.SomniumAssetto`; Editor code in
  `Editor/` = asmdef `SomniumAssetto.Editor`, namespace `SomniumSpace.Worlds.SomniumAssetto.Editor`; `Editor/LabStubs` = tablet stand-ins).
- Scene `Assets/Assetto Scene.unity`: "Ac Game" (AcGame: `_shaderCarriers`, `_installHint`), "Ac Net" with "Scene Networking"
  (SceneNetworking) and 69 network blocks (see "Network slot plan"). Do not edit the scene unless your brief says so (W may set
  `_shaderCarriers`; ask the lead via REQUESTS.md for anything else).
- Hub height: `AcSpace.RoomOrigin` = (0, 3000, 0) (spawn point y 3000.9, fallback floor y 2999.5); tracks at `AcSpace.MapOrigin` = 0.
- Read-only templates (port freely: rename the prefix, keep what is generic, replace the game data):
  - **Halo** `D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo` (also copied to `D:/Dreamport/Assetta Corsa/_HaloTemplate/SomniumHalo`;
    this project's skeleton came from it). Below: `HALO/`.
  - **ETS2** `D:/Dreamport/Euro Truck Simulator/Euro Truck Sim/Assets/#User/SomniumEuroTruck` - vehicles, seats, riders, steering
    grab, net vehicles, tablet link, FMOD bank reader. Below: `ETS/` (its pure readers are in `ETS/Ets/`).
  - **BO2** `D:/Dreamport/BO2/Black Ops 2/Assets/#User/SomniumBO2` (`README.md` there), **WoW** `D:/Dreamport/WoW/World Of Warcraft/Assets/#User/SomniumWoW`.
  - Somnium client source `E:\Somnium Space\Main Build\Assets\_SomniumSpace` (`MB/`: VehicleSystem, VRTablet, Inputs/*.inputactions).
  - The game install `E:\SteamLibrary\steamapps\common\assettocorsa` (read-only; `sdk/dev/content/cars/formula_k/data/*.ini` = Kunos'
    commented reference car).

## Tools (all offline except the lab)
- **Compile** (editor-like, server-like, editor tools + Span check): `bash "D:/Dreamport/Assetta Corsa/DevTools/build/build.sh"`.
  The tree is shared: errors in OTHER modules' files are not yours (`grep` your folder); never "fix" another module's file - write in
  REQUESTS.md. Run it often; never leave your files uncompiling (a compile error also hangs every lab).
- **Server API guard**: `bash "D:/Dreamport/Assetta Corsa/DevTools/fusionapi/check.sh" "D:/Dreamport/Assetta Corsa/DevTools/build/out/server_like.dll"`
  must print "all N Fusion/Somnium members are proven". Anything listed is unproven on the upload server: avoid it (use what Halo / ETS /
  BO2 used) or tell the lead. Known traps: no `NetworkRunner.GetMasterClient`, no `GetAllBehaviours<NetworkTransform>`.
- **Metas**: after creating files or folders run `bash "D:/Dreamport/Assetta Corsa/DevTools/metas.sh"` (writes missing .meta files only).
- **Data harness** `DevTools/harness-ac` (dotnet 8, C# 9; compiles `Data/**` + `Core/AcLog.cs`; README.md there):
  `cd "D:/Dreamport/Assetta Corsa/DevTools/harness-ac" && dotnet build -c Release -v q && dotnet run -c Release --no-build -- <cmd>`.
  Built-in: `content`, `kn5 <path> [tree|mats|tex]`, `acd <car> [entry]`, `ksanim <file|dir> [node]`, `knh <car>`, `ai <track> [layout]`,
  `fsb <car|common>`, `fev <car|common> [filter]`, `dds <kn5> <tex> <out.png>`, `chunk <bank> <ID>`, `hint <dir>`, `selftest`, `all [--decode]`.
  **Your own probes:** add `DevTools/harness-ac/Probe<Code>.cs` containing `static partial class Program { static int Cmd_<name>(List<string> a) { ... } }`
  and run `<name>` (a[0] is the command; `_install` is the found AcInstall). Nothing else to register. `Png.Write` writes pictures you can
  look at with the Read tool. If a build fails because of another agent's probe or a parallel build, wait a minute, or copy the harness
  to `DevTools/agents/<code>/harness` (fix the two relative `Compile Include` paths) and build there.
- **Lab** (Unity play mode in batch WITH graphics; boots the real scene; the editor always passes the whitelist):
  `bash "D:/Dreamport/Assetta Corsa/DevTools/lab.sh" <scenario> [-actrack imola ...]`. Pictures + `lab.log` in `DevTools/lab/<scenario>/`;
  the script prints `LAB:` and `[SomniumAssetto]` lines. Built-in scenario `boot` (waits for the install). Add yours in
  `Editor/AcLab<Module>.cs` (your own file, name scenarios `<module>-<what>`, e.g. `world-magione`):
  ```csharp
  [InitializeOnLoad] static class AcLabWorld {
      static AcLabWorld() { AcLabRunner.Register("world-magione", r => Run(r)); }
      static IEnumerator Run(AcLabRunner r) { /* r.Game, r.Cam, r.Aim(eye, target, fov), r.Shot("name"), r.Check(ok, "what"),
          yield return r.Wait(() => cond, 60f, "what"); r.Frames(n); AcLabBoot.Arg("-actrack", "magione") */ yield return null; } }
  ```
  Two slots (A = this project when no editor has it open, B = a private copy `DevTools/labproject` made by `labcopy.sh` on first use).
  **The FIRST lab run of each slot imports the whole project (Library rebuild, slow) - expect it.** A lab ends by its own timeout
  (`-actimeout`, default 900 s; lab.sh hard limit 1500 s). LOOK at the pictures before claiming visuals work. Use the lab sparingly
  (10 agents share 2 slots; the user dislikes long test sweeps): a few runs to verify what you built.

## Data core you can use (module D, finished and verified - ask the lead before changing anything in Data/Core, Install, Kn5, Acd, Anim, Track, Fmod)
Verified by `harness-ac all` on the real install: 176 car folders (86 owned), 44 layouts (35 owned), 430/430 car kn5, 9149 textures,
86/86 data.acd, 87/87 banks, 654/654 ksanim, 35/35 layouts' markers + AI + surfaces, 111/111 track kn5.
- **Install** `AcInstall.Find(hint)` -> `Root`, `CarsDir`, `TracksDir`, `SfxDir`, `CommonBank`, `GuidsTxt`, `DriverDir`, `FontsDir`,
  `ShowroomDir`, `SystemDir`, `SystemSurfacesIni`; `AcInstall.HintProblem`. **Catalog** `AcContent.Scan(install)` (done by AcGame on the
  boot thread; read it as `AcGame.Current.Content`): `Cars` (AcCarEntry: `Id`, `Name`, `Brand`, `Class`, `Owned`, `IsDlcStub`, `Specs`
  (AcCarSpecs from ui_car.json), `PowerCurve`/`TorqueCurve`, `Kn5Path`, `Kn5Files`, `ColliderPath`, `KnhPath`, `BankPath`, `GuidsPath`,
  `AnimationsDir`, `BadgePath`, `Skins` (AcSkinEntry: `Id`, `Name`, `Number`, `Driver`, `PreviewPath`, `LiveryPath`), `UiJson`, `Skin(id)`),
  `Tracks` (AcTrackEntry: `Id`, `Name`, `Layouts`, `Owned`, `Layout(id)`) with AcLayoutEntry (`TrackId`, `Id` ("" single-layout),
  `Key`, `Name`, `LengthM`, `Pitboxes`, `Owned`, `BaseDir`, `DataDir`, `AiDir`, `UiDir`, `PreviewPath`, `OutlinePath`, `MapPath`,
  `MapIniPath`, `FastLanePath`, `PitLanePath`, `SurfacesIni`, `ModelsIniPath`, `Kn5Files` (load order), `Folders`, `UiJson`);
  `Car(id)`, `Track(id)`, `Layout(track, layout)`, `OwnedCars`, `OwnedLayouts`.
- **Text formats**: `AcIni.Load/Parse/FromBytes` (`[section]`, `Get/GetFloat/GetInt/GetBool/GetFloats/GetInlineLut`, `WithPrefix("SURFACE_")`,
  `Numbered`), `AcLut.Parse/ParseInline/FromBytes` (`Eval(x)`, `MaxY`, `ArgMaxY`; `AcLut.ParseRto`), `AcJson.Parse/ParseObjectFile`
  (lenient; `Str/Text/Num/Obj/Arr/Strings/Curve`), `AcText` (UTF-8/Latin-1, loose numbers "4655m", "0,5"), `AcBinary` (buffered LE reader),
  `AcPool<T>.Rent/Return`.
- **Car data**: `AcdArchive.Open(carDir)` (decrypts data.acd with the folder-name key, falls back to `data/`): `Names`, `Has`, `ReadBytes`,
  `ReadText`, `ReadIni(name)`, `ReadIniOrEmpty`, `ReadLut(nameOrInline)`. `Knh.Read(path)` (driver_base_pos.knh: `Find`, `WorldPosition("RIG_Head")`);
  `Ksanim.Read(path)` (v1/v2; `Nodes`, `Find(node)`, `KsanimNode.At(t)` -> AcMat4).
- **Kn5**: `Kn5File.Read(path, Kn5ReadOptions.Full|HeaderOnly|NoTextures)` (`Textures` (Kn5Texture: `Name`, `Offset`, `Size`, `Bytes`, `Info`),
  `Materials` (Kn5Material: `Shader`, `BlendMode`, `AlphaTested`, `Float(name)`, `Texture(slot)`), `Nodes`/`Root` (Kn5Node: `Name`, `Class`,
  `Active`, `Local`, `World`, `Mesh`, `Children`, `Find`), `LoadMesh(s)` (optionally pooled + already converted to Unity), `ReadTextureBytes`,
  `ReadTextureHeader`, `Find`, `Texture(name)`, `Material(id)`, `Release`). Kn5Mesh: `Vertices` (stride 11 / 19 skinned), `Indices`, `LodIn/LodOut`,
  `IsRenderable`, `MaterialId`, `Bones`. `Kn5Scan.Scan` / `WorldMatrices` / `WithPrefix` / `UsedTextures`. `AcDds.Parse` (AcDdsInfo: `Format`,
  `Width`, `Height`, `MipCount`, `DataOffset`, `MipBytes`), `AcDds.DecodeRgba32` (CPU decode, every format in the install except BC6H/BC7).
- **Math / conversion**: `AcVec3`, `AcQuat`, `AcMat4` (`FromRowMajor`, `Mul`, `Translation`, `AxisX/Y/Z`, `ToUnity()` (16 floats, Unity
  column order), `UnityPosition`, `UnityRotation`, `HeadingDeg`, `InverseAffine`), `AcConvert.MirrorVerticesX` / `FlipWinding`. Rules
  (ac_car_formats.md 1.5 + C1-C3): AC is right-handed, +Y up, +Z forward, **+X = car LEFT** -> Unity (-x, y, z), swap triangle indices 1/2;
  DDS raw upload with uv = (u, v_stored), Repeat (PNG/JPG decoded by ImageConversion: flip rows or use (u, -v)); car MODEL space (kn5, knh,
  DRIVEREYES, collider.kn5) vs PHYSICS space (inis) with `physics = model + GRAPHICS_OFFSET` (`AcCarModel.ModelToBody/PhysToBody`).
- **Track**: `AcModelsIni.Load(trackDir, layoutId)` (`Models`: `FullPath`, `Position`, `Rotation`), `AcTrackMarkers.Collect(kn5s, models,
  surfaces)` (`Starts`, `Pits`, `Gates` (AcTimeGate `Left/Right`), `HotlapStarts`, `StartLights`, `OpenFinish`, `AbStart/AbFinish`; AcMarker
  `World`, `Position`, `HeadingDeg`), `AcSurfaces.Load(layoutIni, systemIni)` (`ByKey`, `Default`, `Match(meshName, out isWall[, out exact])`,
  `IsPhysicsName/IsWallName`) with `AcSurface` (`Key`, `Friction`, `Damping`, `Wav`, `SoundEvent`,
  `IsValidTrack`, `IsPitlane`, `SinHeight/SinLength`, `DirtAdditive`, `BlackFlagTime`, `VibrationGain/Length`), `AcAiSpline.Read(path)`
  (`Count`, `X/Y/Z(i)`, `P(i, AcAiSpline.Speed|Gas|Brake|Radius|SideLeft|SideRight...)`, `IsClosed`, `LapLength`, `Nearest`, `NearestNear`,
  `Progress(x,y,z, ref hint)`, `PointProgress`, `LateralOffset`). Markers float above the road and some are rolled 90 deg - research T1/T2.
- **Sound**: `FmodGuids.Load(GUIDs.txt)`, `FmodBank.Read(path, guids)` (`Events`, `Event(nameOrPath)`, `EngineLayers(interior)` ->
  FmodEngineLayer (`RpmMin/Max`, `PitchRefRpm`, `VolumeDb`, `Load` on/off, `Gain(rpm, throttle)`, `Pitch(rpm)`), `ReadSampleFloats(sample)`,
  `Fsb` (Fsb5: `Samples` with `Name`, `Rate`, `Channels`, `Frames`, `LoopStart/End`; PCM16)), `FmodInstrument.Gain/Pitch`, `FmodBank.DbToLinear`.
- **Core / Unity side (lead)**: `AcLog` (`Info/Warn/Error/InfoOnce/WarnOnce`), `AcVersion`, `AcSpace`, `AcShaders.Carry/Find/Carrier`
  (custom shaders reach the upload only as carrier materials on the root), `AcAccess` (whitelist), `AcSettings` (`Text/Num/Int/Flag` +
  setters, `Changed`; use your key prefix "world.", "seat.", "audio."...), `AcPlayer` (Bridge local player: `XrPresent`, `Head()`, `Root`,
  `HeadBone`, `LeftHand/RightHand`, `TryFeet`, `Teleport(pos, yaw...)`), `AcXr` (controllers with hysteresis, `Pulse`), `AcBody`,
  `AcLaserAim`, `AcTabletGuard` (hold the client's tablet buttons off), `AcFirstPerson` (monitor first person, `BlockedActions`),
  `AcAvatarFind`, `AcAvatarCopies`, `AcHeadHide`, `AcIsolation/AcRemotes/AcTags/AcGear` (do not touch), `AcLayers`.

## Contracts (compiling stubs written by the contracts step; owners replace bodies and only ADD members)
Never remove or change an existing public signature without fixing every caller (grep the tree) and noting it in REQUESTS.md. Stub
bodies log once ("... is a stub (module X fills it)") so a client log shows what is missing.

| Contract | File (owner) | Public face |
|---|---|---|
| Hooks | `Game/AcGame.Front.cs` (F), `.Net.cs` (N), `.Race.cs` (R) | `FrontStart/FrontBootDone/FrontTick/FrontStop` -> `AcFront.Start(game)/BootDone/Tick/Stop`; `NetStart/NetTick/NetStop` -> `AcNetGame.Start(game)/Tick/Stop` + `AddOnce<AcNetDriver>` (LateUpdate 1000 -> `AcNetGame.Late`); `RaceTick/RaceStop` -> `AcRace.Tick(game)/Stop` |
| Session | `Game/Shared/AcSession.cs` (R) | `AcPhase {Hub, Loading, Practice, Grid, Countdown, Racing, Finished, Results}`, `AcSessionType` (AC numbers 1-4), `AcRules` (server_cfg names: Laps, TimeMinutes, AbsAllowed, TcAllowed, ..., AllowedTyresOut, JumpStartPenalty, PitLimitKmh // ours, AllowedCars, AiCount, AiLevel), `AcLap`, `AcStanding`, `AcRaceCommand` (Seq, Phase, TrackId, LayoutId, Rules, StartUtc, Grid, MasterId); `AcSession.Phase/Track/Layout/Rules/WorldRoot/StartUtc/LoadProgress/UtcNow/OnTrack/InRace`, events `PhaseChanged`, `TrackLoading(progress, stage)`, `TrackBuilt`, `Unloading`, `RaceStarted`, `LapCompleted(AcLap)`, `RaceFinished` (each handler in try/catch); setters `SetPhase/Begin/SetRules/ReportLoading/NotifyTrackBuilt/Close/NotifyRaceStarted/NotifyLap/NotifyRaceFinished` are R-only |
| Local state | `Game/Shared/AcLocal*.cs` | static partial: `Nick`, `WorldPosition` (lead); `CarId`, `SkinId` (F); `Car` (AcCarRig), `Telemetry` (P); `Seated`, `Driving`, `SeatCar`, `Seat`, `CameraMode` (S); `NetSlot`, `PhotonId` (N); `Laps`, `LapMs`, `LastMs`, `BestMs`, `DeltaMs`, `Sector`, `LapValid`, `Position`, `RaceDistance`, `InPitlane`, `Finished` (R). Write only your own fields |
| Events | `Game/Shared/AcEvents.cs` (lead) | `Message(text, s)`, `LocalCarChanged(rig)` (R raises), `SeatChanged(rig, seat)` (S), `CarImpact(rig, kmh, point)` (P), `GearChanged(rig, gear)` (P); raise with `AcEvents.Raise*` |
| Car I/O | `Game/Shared/AcCarInput.cs`, `AcTelemetry.cs`, `AcCarTypes.cs` (lead) | `AcCarInput` (Steer -1..1 or `SteerAbsolute` + `SteerWheelDeg`, Throttle, Brake, Clutch, Handbrake, ShiftUp/Down edges, GearRequest, Lights, Horn, ResetRequest; `Neutral`), `AcTelemetry` (Rpm, LimiterRpm, IdleRpm, Gear, GearCount, SpeedKmh, pedals, SteerWheelDeg, BoostBar, FuelLitres, Limiter, AbsActive, TcActive, Shifting, LatG/LongG, Lights, wheels `LF/RF/LR/RR` (AcWheelState: SpinDeg, AngularVel, SteerDeg, TravelM, SlipRatio, SlipAngleDeg, LoadN, Skid, Contact, Surface), `Wheel(i)/SetWheel`, `Instruments`), `AcSide`, `AcSeatKind`, `AcCarKind`, `AcLightState` (wire bits), `AcInstruments` |
| Layers | `Game/Shared/AcLayers.cs` (lead) | World 0, Avatar 2, Ui 5, PhysicalObject 7, **Track 14** (road/kerbs/grass/walls; the avatar stands on it, cars hit it), Interactable 16 (door handles - the only layer both pointers hit that is not ground/UI), Environment 23, Player 24, **Car 28**; masks `AvatarExcludes` (2\|7\|24, on every car collider while occupied), `GroundMask` (0\|14), `BodyExcludes`, `SeatedBodyExcludes`, `CameraObstacles`. The decoded collision matrix and the reasons are in the file's comment |
| Surfaces | `Game/Shared/AcSurfaceTag.cs` (lead) + `AcSurface` (Data/Track, D) | `AcSurfaceTag.Register(collider, surface, wall)` (W), `AcSurfaceTag.Of(collider)` -> AcSurface (no allocation; null for walls/untagged), `TagOf`, `Clear` |
| Track | `Game/World/AcTrackWorld.cs` (W) | `Load(layout, progress)` coroutine, `Unload()`, `Loaded`, `Layout`, `Root`, `PitBoxes[]`, `StartGrid[]`, `HotlapStart` (AcSpawnPoint: Index, Marker (raw node pos), HeadingDeg (horizontal +Z), Rotation, Progress), `TimeGates[]` (AcGate: Index, Left, Right, Forward), `AiLine`, `PitLane` (AcAiSpline, AC coords), `Surfaces`, `StartLights` (AcStartLights: `Count`, `SetLights(redCount, green)`), `CollisionLayer`, `SurfaceAt(collider|hit)`, `TryGround(from, out point, out normal)`, `ToUnity(x,y,z)`, `ToAc(p, out x, out y, out z)` |
| Kn5 builder | `Game/World/AcKn5Builder.cs` (W) | `AcKn5Builder.Build(kn5, parent, AcKn5BuildOptions)` / `BuildAsync(..., progress, done)` -> `AcKn5Model` (`Root`, `Nodes` by name, `Find(name)`, `Renderers`, `Colliders`, `Bounds`, `Dispose`); options `Layer`, `SkinDir`, `Readable`, `PhysicsColliders`, `ColliderLayer`, `Surfaces`, `HideMarkers`, `SliceMs` |
| Car model | `Game/Cars/AcCarModel.cs` (C) | `BuildAsync(car, skinId, AcCarModelOptions{Lod, Interior, Driver, Colliders, ReadableSteer}, parent, done)` / `Build(...)`; `Car`, `SkinId`, `Root`, `Wheels[4]` (AcWheelParts: Wheel, Susp, Disc, Rim, RimBlur, Radius, RestLocal), `SteeringWheel`, `SteeringRest`, `DriverEye`, `DriverHips`, `HandL/HandR`, `PassengerHips`, `HasPassengerSeat`, `DoorL/DoorR`, `Hull`, `Bounds`, `Kn5`; `SetDoor(side, 0..1)`, `SetSteer(wheelDeg)`, `SetWheelState(i, spinDeg, travelM, steerDeg)`, `SetLights(AcLightState)`, `SetInstruments(AcInstruments)`, `SetViewer(insideCockpit)`, `Dispose()`; static `ModelToBody`, `PhysToBody` |
| Physics | `Data/Physics/AcCarSpec.cs`, `Game/Physics/AcCarPhysics.cs`, `Game/Physics/AcCarRig.cs` (P) | `AcCarSpec.Load(carEntry)` (worker-safe, Unity-free) / `FromArchive(acd, id)`: `Inis`, `Problems`, TotalMassKg, InertiaBox, GraphicsOffset, GraphicsPitchDeg, DriverEyes, SteerLockDeg, SteerRatio, Fuel, IdleRpm, LimiterRpm, TorqueCurve, GearRatios, ReverseRatio, FinalRatio, Traction, WheelRadius[4], Wheelbase/CgLocation/Track; `AcCarPhysics` (MonoBehaviour on the car root): `Input`, `Telemetry`, `Assists` (AcAssists), `Spec`, `Model`, `Body`, `Driven`, `Puppet`, `Frozen`, `SpeedKmh`, `Init(spec, model, surfaces)`, `SetDriven`, `SetOccupied`, `SetPuppet`, `ApplyPuppet(pos, rot, vel, shown)`, `ResetTo(pos, rot)`, `Freeze`; **`AcCarRig.Spawn(car, skin, AcCarRigOptions{Kind, Model, Audio, Parent}, done)`** = the ONE car factory (R local, I AI, N remote, F showroom): `Kind`, `Car`, `SkinId`, `Root`, `Model`, `Physics`, `Audio`, `Spec`, `OwnerId`, `Dispose()` |
| Seat | `Game/Seat/AcSeat.cs` (S) | `AcSeat.Start(game)`, `Enter(rig, seat)`, `Exit()`, `ForceExit()`, `Stop()`, `Seated`, `SeatedCar`, `IsDriver`, events `Entered`, `Exited` (+ AcEvents.SeatChanged); `AcDriveInput.Produce(rig)`, `DisarmPedals()` |
| Audio | `Game/Audio/AcCarAudio.cs` (A) | `AcCarAudio.Attach(carRoot, car, spec, local)`, `Feed(telemetry)`, `SetInterior(bool)`, `Door(side, opening)`, `Gear(gear, grind)`, `Collision(kmh, point)`, `Horn(bool)`, `Dispose()`, `Ready`; `AcSfx.Ui(name)`, `StartBeep()`, `GoBeep()` |
| HUD | `Game/Hud/AcHud.cs` (H) | `AcHud.Start(game)`, `Show(rig)`, `Hide()`, `Message(text, s)`, `StartLights(reds, green, visible)`, `Stop()`, `Shown` |
| Net | `Game/Net/AcNetGame.cs`, `AcNetSlots.cs` (N) | `AcNetGame.Start(game)/Tick/Late/Stop`, **`TryHasRecord(photonId, out known)`** (AcGear/isolation depend on it), `Up`, `Online`, `Claimed`, `IsMaster`, `MasterKnown`, `MasterName`, `Players` (AcNetPlayer: Slot, PhotonId, Name, CarId, SkinId, OnTrack, Seated, RideInPhotonId, Laps, BestMs, LastMs, LapMs, Sector, LapValid, RaceDistance, InPitlane, Finished, FinishedMs, IsMaster, Vr, Car), `PlayerCount`, `FindByPhotonId`, `RaceControl` (AcRaceControl: `Current`, `Changed`, `Publish(cmd)` master-only), `RemoteCars` (AcRemoteCar: Player, Rig, Shown); `AcNetDriver`; `AcNetSlots` (prefixes, counts, record numbers, `RecordOf(name)`) |
| AI | `Game/Ai/AcAi.cs` (I) | `AcAi.Spawn(count, rules)`, `Tick()`, `Clear()`, `Drivers` (AcAiDriver: Index, Name, Rig, Level, `Produce()`), `Available` |
| Front | `Game/Front/AcFront.cs` (F) | `AcFront.Start(game)/BootDone/Tick/Stop/Toast(text)`; `AcLoading.Show(layout, rules)/Progress(f, label)/Hide()/Leave()/Showing`; `AcSettingsUi.Toggle/Stepper/Choice(group, key, label, ...)`, `Rows`, `Changed` (registry works now; F draws it) |
| Race | `Game/Race/AcRace.cs` (R) | `AcRace.Tick(game)` (first tick starts `AcSeat`, `AcHud`), `Stop()`, `Apply(cmd)`, `RequestLoad(layout, rules)`, `RequestStart()`, `RequestStop()`, `RequestHub()`, `BackToPits()`, `ChooseCar(car, skin)`, `LocalCar`, `Standings` |

**Who starts / drives whom** (the only entry points are AcGame's hooks):
- AcGame (after the gate): `FrontStart` -> F; `NetStart` -> N; boot thread finds the install and scans the catalog -> `FrontBootDone`;
  every frame `NetTick`, `FrontTick`, `RaceTick` (in that order, each guarded); `OnDestroy`: RaceStop, NetStop, FrontStop, isolation reset.
- R is the session conductor: first `RaceTick` -> `AcSeat.Start`, `AcHud.Start`. Track pick / net command -> `AcSession.Begin` +
  `SetPhase(Loading)` -> `StartCoroutine(AcTrackWorld.Load(...))` with progress -> `AcSession.ReportLoading` -> `NotifyTrackBuilt` ->
  `AcCarRig.Spawn` (local car, pit box) -> `AcEvents.RaiseLocalCarChanged` -> Practice. Race: Grid (cars placed/frozen), Countdown
  (`AcTrackWorld.StartLights.SetLights`, `AcHud.StartLights`, `AcSfx.StartBeep/GoBeep`), Racing, Finished, Results. Master: `AcAi.Spawn/Tick`.
- F follows `AcSession.PhaseChanged/TrackLoading` for the loading card and calls `AcRace.Request*` / `ChooseCar` from the tablet.
- N: `AcNetGame.RaceControl.Changed` -> R applies with `AcRace.Apply`; N reads `AcLocal.*` + `AcLocal.Telemetry` to write the own record;
  N builds remote cars with `AcCarRig.Spawn(Kind Remote)` after `AcSession.TrackBuilt` and drives them with `ApplyPuppet`.
- S: while driving calls `AcDriveInput.Produce(rig)` each frame -> `rig.Physics.Input`; sets `SetOccupied/SetDriven`; raises SeatChanged.
- P: each frame (Update/LateUpdate of AcCarPhysics) pushes its telemetry to `Model.SetSteer/SetWheelState/SetLights/SetInstruments`
  and `Audio.Feed` for local and AI cars (N does the same for remote cars from the record); copies the local car's to `AcLocal.Telemetry`.
- H subscribes to `AcEvents.Message/SeatChanged` and reads AcLocal / AcSession / `AcRace.Standings` each frame.
- Frame order (recommended, as Halo/ETS): AcGame Update (order 0); S's early seat write at -90 and late carry at 10000 (Halo
  HaloVehicleDirector), monitor avatar carry 9990 (ETS EtsAvatarSync); AcNetDriver LateUpdate 1000 (remote cars after Fusion render);
  AcIsolationDriver 9990 (lead); HUD after the seat (10001+).

## Network slot plan (scene verified 2026-10-31: 69 blocks, names below, all NetworkObject + NetworkBridgeData64, unique SortKeys)
Found by name like `HALO/Game/Net/HaloFusionTransport.cs` `Find` (prefix + integer, `StartsWith` ordinal); constants in
`Game/Net/AcNetSlots.cs` (`RecordOf(name)`; records 0..68 in this order). `AcIsolation.OwnNetRoot = "Ac Net"` already skips them.
| Block | Records | Writer | Use |
|---|---|---|---|
| "Ac Net Player 0..49" | 0-49 | the slot's owner (claim: `HaloSlotClaim` verbatim, first try = localId % 50) | One Dreamport player each. Header ints 0-4 as Halo (magic 'ACP1' 0x41435031, seq, stamp ms, owner+1 / generation / protocol, flags: status bits 0-1 Free/Present/Playing, bit 7 instance master). Payload (N finalises, documents in NOTES.md): car id hash + skin hash, on-track layout hash, placement epoch (respawn = snap), pose PackCm2 (2 ints) + PackQuat + velocity S16 cm/s + yaw rate, steer wheel deg, rpm, gear/throttle/brake/clutch u8, lights (AcLightState), suspension travel x4 + skid x4 + surface index x4 (u8 each), door L/R, seat occupancy + passenger / ride-in photon ids + ETS rider record, laps, sector, cuts, lap ms, last, best, race distance x1e4, finish time, penalty, lobby ready. ~10 Hz idle, up to 30 Hz driving, only changed ints (Fusion sends ~16 Hz) |
| "Ac Net World 0..3" | 50-53 | instance master | Race control (HaloNetWorld header 7 ints: magic 'ACW1', seq, stamp, writer, track hash, command seq, start). World 0 = the current `AcRaceCommand` (phase, track/layout hash, session type, laps/minutes, rule bits, fuel/wear, AI count/level, StartUtc seconds+ms, allowed car hashes); World 1 = grid order (photon ids / AI ids, 2 per int) + lobby; World 2-3 = results (per finisher id/position + total ms; best laps come from each player's own record) |
| "Ac Net Ai 0..8" | 54-62 | instance master | AI cars of module I: 2 cars per block (4-int header + 2 x 30 ints in the player-record payload packing) = up to 18 AI cars drawn by every client as remote cars |
| "Ac Net Race 0..1" | 63-64 | instance master | Live race state: Race 0 = start-light sequence (step times on the shared UTC clock, reds, green), session clock, leader-finished time, flags; Race 1 = penalties per player slot / AI (drive-through, +5 s, jump start; u8 each) |
| "Ac Net Host 0..3" | 65-68 | instance master | Host hand-over stream (`HALO/Game/Net/Core/HaloNetStream.cs` + `HaloNetHostState.cs`, 224 bytes per block) for state that must survive a master leaving beyond the World blocks (AI driver state); optional in iteration 1 |
Master rule (verbatim from `HALO/Game/Net/Core/HaloNet.Master.cs`): me if my runner is the shared-mode master and I hold a record;
else the first listed record with flag bit 7; else the lowest PlayerId. A new master keeps the newest command (Seq above any seen). Race
timing is owner-authoritative (each client times its own laps from its own physics and publishes them); the master orders positions.

## Ownership (only edit files you own; new files only inside your folders)
| Code | Module | Owns |
|---|---|---|
| lead | Skeleton + contracts | `Core/`, `Game/AcGame.cs`, `Game/AcSettings.cs`, `Game/Access/`, `Game/Shared/` (except the owners' files listed below), `Game/Vr/` (owners may ADD files there only via REQUESTS), `Game/Desk/` existing files, `Game/Net/SceneNetworking.cs`, `Editor/AcLab.cs`, `Editor/AcSceneResave.cs`, asmdefs, the scene, DevTools scripts, `README.md` |
| D | Data core (finished) | `Data/Core`, `Data/Install`, `Data/Kn5`, `Data/Acd`, `Data/Anim`, `Data/Track`, `Data/Fmod`, `DevTools/harness-ac` (agents ADD only `Probe<Code>.cs` files there) |
| W | World / track | `Game/World/` (AcTrackWorld, AcKn5Builder, AcTextures, AcMaterials, lighting, start-light meshes, showroom build), `Shaders/`, `Resources/` (carrier materials), `Editor/AcLabWorld.cs`; may set `_shaderCarriers` in the scene's "Ac Game" block |
| C | Car model | `Game/Cars/`, `Editor/AcLabCars.cs` |
| P | Physics | `Data/Physics/`, `Game/Physics/` (AcCarPhysics, AcCarRig), `Game/Shared/AcLocal.Physics.cs`, `Editor/AcLabPhysics.cs` |
| S | Seat, cockpit, driving input | `Game/Seat/`, new files in `Game/Desk/` (monitor avatar bits), `Game/Shared/AcLocal.Seat.cs`, `Editor/AcLabSeat.cs` |
| A | Audio | `Data/Audio/` (if needed), `Game/Audio/`, `Editor/AcLabAudio.cs` |
| F | Front & tablet | `Data/Ui/`, `Game/Front/`, `Game/AcGame.Front.cs`, `Game/Shared/AcLocal.Front.cs`, `Editor/AcLabFront.cs`, `Editor/LabStubs/` |
| R | Race | `Game/Race/`, `Game/AcGame.Race.cs`, `Game/Shared/AcSession.cs` (+AcRules, AcLap, AcStanding, AcRaceCommand), `Game/Shared/AcLocal.Race.cs`, `Editor/AcLabRace.cs` |
| N | Net | `Game/Net/` (except SceneNetworking.cs), `Game/AcGame.Net.cs`, `Game/Shared/AcLocal.Net.cs`, `Editor/AcLabNet.cs` |
| H | HUD | `Data/Hud/`, `Game/Hud/`, `Editor/AcLabHud.cs` |
| I | AI drivers (stretch) | `Game/Ai/`, `Data/Ai/` (Unity-free driver math, optional), `Editor/AcLabAi.cs` |

## Coordination
- Your notes: `DevTools/agents/<code>/NOTES.md` - a short running status (what works, verified how, what is open, your public API
  additions). Others read it; keep it current.
- Cross-module needs: append to `DevTools/agents/REQUESTS.md` (`## <from> -> <to>: <title>` + what, why, the exact signature wanted).
  Check it now and then for requests to you and answer under the request (`DONE <date>: ...` / `NO: why`). If an API you need is not
  there yet, code against the stub signature; if the stub lacks something, ask (or add an additive member to that stub ONLY after the
  owner agreed in REQUESTS.md).
- Shared files you must not edit: another module's folder, the gate, SceneNetworking, the scene (unless your brief says so), Data/ of D.
- Do not delete things outside your scope. Halo leftovers are moved (not deleted) to `D:\Dreamport\Assetta Corsa\_HaloTemplate\`.

## Final report (your last message; <= 40 lines, factual)
1. Files made / changed (paths). 2. What compiles (build.sh x3 0 errors? check.sh "all proven"?). 3. What is VERIFIED and how (harness
numbers, lab scenario + the pictures you looked at, log lines). 4. What is NOT verified / known gaps. 5. Player.log lines to look for
(`[SomniumAssetto] <Module>: ...`). 6. Requests you made / answered. 7. Numbers you invented (`// ours`) that the user should judge.
