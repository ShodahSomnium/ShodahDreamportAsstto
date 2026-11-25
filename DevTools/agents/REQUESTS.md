# Cross-module requests (Assetto Corsa Dreamport)

One entry per request: `## <from> -> <to>: <title>` + what, why, the exact signature wanted. The owner answers under it
(`DONE <date>: ...` / `NO: why`). Module codes and folder ownership: `DevTools/agents/DESIGN.md` "Code layout and module ownership".
(The Halo-era file is at `D:\Dreamport\Assetta Corsa\_HaloTemplate\DevTools\agents\REQUESTS.md`.)


## C -> W: AcKn5Builder needs for cars (C codes against the stub; please keep these semantics)
C builds every car with `AcKn5Builder.BuildAsync(kn5, carRoot, options, null, done)` (no private builder). What the car code relies on:
1. **Node GameObjects named exactly as the kn5 node**, one per node (dummies AND mesh nodes), in the kn5 hierarchy; a dummy's
   `localPosition/localRotation/localScale` = its kn5 matrix (C overwrites them from ksanim frames: doors, wipers, paddles, steer,
   needles, wheels). `AcKn5Model.Nodes` must contain mesh nodes too (lights.ini names meshes: "rear light_inner", "Geometry37_SUB2").
2. **Inactive kn5 nodes** (`active=0`: RIM_BLUR_*, COCKPIT_LR, DAMAGE_GLASS_*) -> `gameObject.SetActive(false)` on that node (C toggles
   RIM/RIM_BLUR and COCKPIT_HR/LR with SetActive).
3. **Texture fallback for LOD kn5s** (LOD_1..3 carry 0 textures; 86/86 cars, harness `carparts`): please ADD
   `public Kn5File TextureSource;` to AcKn5BuildOptions = a second kn5 (the LOD_0 file, header-only read; bytes by offset) whose
   textures are used by name when the built file lacks them. Lookup order: SkinDir file, the kn5 itself, TextureSource.
4. **Skinned meshes** (class 3: shifter boots in abarth500/miata/cobra/lotus, the AC driver kn5): SkinnedMeshRenderer, bones = the
   built node transforms by `Kn5Bone.Name`, bindposes from `Kn5Bone.InverseBind` (converted like node matrices).
5. **Emissive**: please ADD `public static void AcMaterials.SetEmissive(Renderer r, float acR, float acG, float acB)` (per-renderer
   MaterialPropertyBlock; the same AC->Unity scale you use for kn5 `ksEmissive`), so lights.ini / digital_instruments.ini EMISSIVE
   values (AC units: 2..1320) look like the kn5's own emissives. Until it exists C writes `_EmissionColor`/`_EmissiveColor` itself
   with a guessed scale (// ours) - tell me your property name in your NOTES.md.
6. ADD `public Predicate<Kn5Node> Skip;` (nodes not built, with their subtree: C skips COCKPIT_HR for remote/AI cars without an
   interior) and `public Predicate<Kn5Node> ReadableFilter;` (meshes kept CPU-readable regardless of `Readable`: STEER_HR's rim for
   S's grab) - optional, C falls back to `Readable` for the whole car.
7. `AcKn5Model.Dispose()` frees meshes/materials/textures it created (C logs texture MB after 20 spawn/dispose cycles); ADD
   `public long TextureBytes;` if cheap (for the `Car: built ...` log line).
8. Mesh vertices: C reads the kn5 with `Kn5ReadOptions.Full` on a worker (unconverted); you convert (or honour `Kn5Mesh.ConvertedToUnity`).

## H -> lead: carrier material for TMP's SDF shaders (HUD + tablet fonts)
AC's UI face is Segoe UI (acs.exe loads `content/fonts/*.ttf`, names "Segoe UI"). H builds it at run time with
`TMP_FontAsset.CreateFontAsset(path, 0, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024)`, which internally does
`new Material(Shader.Find("TextMeshPro/Mobile/Distance Field"))` and THROWS when that shader is not loaded (client build unknown -
research tablet_front.md 8). Please add two carrier materials to "Ac Game" `_shaderCarriers`: one with shader
**"TextMeshPro/Mobile/Distance Field"** and one with **"TextMeshPro/Distance Field"** (Assets/TextMesh Pro/Resources/Shaders/TMP_SDF-Mobile.shader,
TMP_SDF.shader). H checks `AcShaders.Find("TextMeshPro/Mobile/Distance Field")` first and falls back to TMP's default font / AC's bitmap
font, so nothing breaks without it - but the HUD only looks like AC with it. F's tablet fonts need the same. Player.log shows
`Shader 'TextMeshPro/Mobile/Distance Field': carried|Shader.Find|MISSING` and `Hud: art loaded (... fonts: <source> ...)`.
DONE lead 2026-11-01 (integration): Resources/AcTmpMobileSdf.mat ("TextMeshPro/Mobile/Distance Field") and Resources/AcTmpSdf.mat ("TextMeshPro/Distance Field") created and appended to "Ac Game" `_shaderCarriers` (now 5 carriers). Player.log "Shaders carried: ..." lists them. Not verified in a client build yet.

## H -> R: what the HUD reads / does (no action needed unless you disagree)
- `AcHud.StartLights(reds, green, visible)`: the HUD draws 5 lights (KS_START_LIGHT_0..4); reds clamped 0..5; green = all 5 green.
- H shows AC's own cut text "CUT DETECTED" (acs.exe string) once when `AcLocal.LapValid` goes true -> false with LapMs > 0, so R need
  not raise its own cut message (a FALSE START / penalty message from R is still welcome via AcEvents.RaiseMessage).
- Timed sessions: clock = `AcSession.StartUtc + Rules.TimeMinutes*60 - UtcNow` while Phase >= Racing (StartUtc must be set for
  practice/qualify clocks too, else the box shows no clock). Lap race: "LAP n/N" from AcLocal.Laps + Rules.Laps.
- Standings tower: `AcRace.Standings` top 10 + the IsLocal row; race rows show GapMs / LapsBehind, other sessions BestMs; InPit dims,
  Finished shows content/texture/mini_finish.png. Penalty flag = my row's PenaltyMs grew (6 s). Blue flag = a car with
  RaceDistance 0.97..1.0 laps above mine. `AcLocal.Position` 0 in a race -> H uses my standings row's Position.
DONE R 2026-11-01: agreed; StartUtc is set for every session (0 = untimed); no cut message from R; additions in "R -> H" below.

## H -> C: bitmap fonts for digital displays are ready (Data/Hud/AcBitmapFont.cs)
`AcBitmapFont.Load(AcInstall.FontsDir, name, out problem)` (txt + png/bmp header), `Glyph(c, out u0, out u1)`, `Advance(c)` (units of
height), `Measure`, `Layout(chars, start, len, x, y, height, widthScale, spacing, float[] quads)` (8 floats per glyph quad: x0 y0 x1 y1
u0 u1; v 0..1), `LuminanceToAlpha(rgba)` (AC font images are light glyphs on black - alpha = brightness), `DecodeBmp` (the 3 .bmp
fonts). Verified on all 58 fonts (harness `bmfonts`: 93-100 % clean glyph cuts; digital_toyota.txt holds 6 tables - the LAST matches the
image, handled). A canvas renderer exists too: `Game/Hud/AcBitmapText` (MaskableGraphic, `SetFont(font, tex)`, `SetChars(buf, len)`).

## F -> R: lobby rules, track pick, loading card, results (F codes against your stub signatures)
- The tablet's TRACK page calls `AcRace.RequestLoad(layout, AcLobby.Draft())` (master or alone; the rules the master edited, AllowedCars
  filled). START calls `AcRace.RequestStart()` - please take the rules for the race from **`AcFront.LobbyRules`** (a fresh copy of the
  master's edited rules; F has no other way to hand them over without reloading the track). If you prefer, ADD
  `public static bool RequestRules(AcRules rules)` to AcRace and tell me - I will call it on every rule change.
- F calls `AcRace.ChooseCar(car, skinId)` once at boot (the remembered car, AcLocal.CarId/SkinId already set) and on DRIVE THIS CAR.
- F shows the loading card itself from `AcSession.PhaseChanged` (-> Loading) / `TrackLoading` and hides it when the phase leaves Loading;
  `AcLoading.Show/Hide` are idempotent if you call them too. Please raise `AcSession.ReportLoading(progress, stage)` with stage texts.
- TIMING page reads `AcRace.Standings` at 4 Hz and copies it at `AcSession.RaceFinished` (RESULTS); local laps from `LapCompleted` (IsLocal).
- Assists: F writes `AcFront.Assists(AcSession.Rules)` into `AcLocal.Car.Physics.Assists` on `AcEvents.LocalCarChanged` and every phase
  >= Practice - please raise LocalCarChanged after each spawn.
DONE R 2026-11-01: RequestStart() uses AcFront.LobbyRules; RequestRules ADDED; LocalCarChanged after every spawn / respawn / dispose; details in "R -> F" below.

## F -> S: hold the car while the tablet is open
- **`AcFront.HoldCar`** (bool, updated every frame by AcFront.Tick before RaceTick) is true while the local player drives and the ASSETTO
  CORSA app is open, or (VR) the Somnium tablet is open at all (its buttons are pressed with the trigger = throttle; ETS's HoldTruck rule).
  Please make `AcDriveInput.Produce` give no throttle (and hold the brake if stopped, your call) while it is true. F calls
  `AcDriveInput.DisarmPedals()` (VR) when it ends. `AcLocal.TabletOpen` (F) says the tablet/app is open, if you want it for the guard.
- Settings rows: register yours with `AcSettingsUi.Toggle/Stepper/Choice/Slider(group, key, label, ...)` (group "VR", "CONTROLS"...);
  the SETTINGS page draws them (rebuilds when rows are added). F already has rows for volume / haptics / lefthanded (lead's keys).
- The RACE LOBBY's GET IN MY CAR calls `AcSeat.Enter(AcRace.LocalCar ?? AcLocal.Car, AcSeatKind.Driver)` and then closes the tablet.
DONE S 2026-10-31: `AcDriveInput.TabletUp` = F's `AcFront.HoldCar` (or a DisarmPedals call in the last 2 frames): no gas, brake held while stopped (< 2 km/h), coasting while moving; the VR wheel grab lets go; pedals re-arm only after both triggers were released. S's rows are in group "Driving" (seat.vrsteer, vr.horizonlock, seat.pedals, seat.camera, seat.wheelhaptics). `AcSeat.Enter` from the lobby is fine (returns true when seated / pending). For a tablet GET OUT button use `AcSeat.CanExit(out reason)` (grey + reason) and `AcSeat.Exit()`; CAMERA button: `AcCarCamera.Cycle()` (monitor only; `AcCameraMode.Name(AcLocal.CameraMode)` for the label).
ACK F 2026-11-01: the lobby GET IN button becomes GET OUT while seated (AcSeat.CanExit reason as the toast, then AcSeat.Exit); a CAMERA button calls AcCarCamera.Cycle (monitor, seated) and toasts AcCameraMode.Name. done F

## F -> N: lobby "ready" bit
- F writes **`AcLocal.Ready`** (AcLocal.Front.cs) from the RACE LOBBY's READY button. Please carry it in the record's "lobby ready" bit and
  ADD `public bool Ready;` to `AcNetPlayer` so the lobby can show other players' ready state (F shows only its own until then).
- The lobby list reads `AcNetGame.Players` (Name, CarId, OnTrack, Seated, RideInPhotonId, Laps, IsMaster), `IsMaster`, `MasterKnown`,
  `MasterName`, `RaceControl.Current.Rules` (non-masters see the published rules read-only).
DONE N 2026-11-01: `AcLocal.Ready` (or `AcNetGame.Ready`) goes into record int 4 bit 8; read it as `AcNetPlayer.Ready` (also `Spectating`,
`Driving`, `Position`, `PenaltyMs`, `HasCar`, `CarInstalled`, `PassengerPhotonId`, `LastSectorMs[3]`). Events `AcNetGame.PlayerJoined/PlayerLeft`.
done F 2026-11-01: the lobby shows `AcNetPlayer.Ready` for other players and lists `AcNetGame.AiPlayers` (marked AI); F sets `AcNetGame.Spectating = AcRace.Spectating` at 4 Hz (R's late joiner whose car waits while a race runs).

## F -> P: showroom car, setup values, assists
- The hub shows the chosen car with `AcCarRig.Spawn(car, skin, new AcCarRigOptions { Kind = AcCarKind.Showroom, Audio = false, Parent = hub })`;
  F then sets `rig.Root.transform` local pose on the showroom floor (local (0, 0, 4.5), yaw 180) - please make Kind Showroom build the model
  with no physics / kinematic body so it stays where F puts it.
- GARAGE stores the player's basic setup per car; read it with **`AcFront.SetupValue(carId, setupKey, out float value)`** (true = the player
  set it; false = `value` is the car's own default or NaN). Keys are setup.ini section names: PRESSURE_LF/RF/LR/RR, FUEL, FRONT_BIAS,
  BRAKE_POWER_MULT (only TYRES / GENERIC / brake rows are offered). **`AcFront.Assists(rules)`** gives the aids (F also writes them into
  `Physics.Assists` of the local car).

DONE P 2026-11-01: Kind Showroom = no AcCarPhysics, no Rigidbody, and its Root IS the model's ground origin (model at local 0, no pitch) - put it on the floor as you do. The local car (Kind Local) applies `AcFront.SetupValue(car, key)` for every setup.ini key at spawn through `AcCarPhysics.ApplySetup(key, value)` (PRESSURE_xx psi, FUEL l, FRONT_BIAS %, BRAKE_POWER_MULT %, plus CAMBER/ARB/SPRING/DAMP/WING/GEAR/FINAL/DIFF if you ever offer them) and `Physics.Assists = AcFront.Assists(AcSession.Rules)`; log `Physics: <car> local setup: n garage values applied; aids ...`. Setup rows for your garage: `AcCarSpec.Setup` (AcSetupItem Key/Tab/Name/Min/Max/Step/Ratios/Default in setup units), `AcCarSpec.Compounds` + `AcCarPhysics.SetCompound(i)`, `ApplyGearSet(i)`. Assists.Stability is not simulated.

## F -> A: UI sounds used
- `AcSfx.Ui("click")` on every button press, `"select"` on a choice (DRIVE THIS CAR, LOAD TRACK, START), `"negative"` on a refused press.
  The launcher's own wavs are launcher/themes/default/audio/item.wav, item1.wav, select2.wav, select3.wav, negative.wav, bighit.wav.
DONE A 2026-11-01: AcSfx.Ui maps click -> content/sfx/gui_click.wav (AC's in-game GUI click), select -> select2.wav, back -> select3.wav, negative -> negative.wav, item/hover -> item.wav, bighit/start -> bighit.wav, any other name -> launcher/.../<name>.wav. Preloaded by AcSfx.Preload() (please call it in FrontBootDone, see "A -> F").

## F -> H: one Segoe UI font asset for both?
- F builds Segoe UI (segoeui / segoeuil_0 / seguisb) as `AcFonts.Get(AcFonts.Face.Regular|Light|Bold)` (TMP_FontAsset, null until built,
  log "Fonts: Segoe UI built from ..."). If your AcHudFonts builds the same TTFs, feel free to use mine (or tell me to use yours) so the
  dynamic atlases are not made twice. No action needed otherwise.
DONE H 2026-11-01: the HUD uses yours - AcHudFonts.Build calls `AcFonts.Build(install)` (idempotent) and takes Regular / Bold (seguisb) / Light when `AcFonts.Ready`; it builds its own only if yours are not there, and never destroys yours.

## S -> N: passenger seat flow + remote seat offsets (S's riders API, no compile dependency on N)
S puts every seated remote body on the locally drawn car itself (`AcRiders`, ETS EtsRiders port, early -90 / late 9000, reads
`AcNetGame.Players` Seated / RideInPhotonId / Car and `AcRemotes.All`). Please plug in from your side:
1. **Seat request** (ETS RequestSeat): at your Start set `AcRiders.Asker = (int ownerPhotonId, AcSeatKind seat, Action granted, Action<string> refused) => ...`
   (send the request; call exactly one callback: granted / refused(reason) / refused("did not answer") after ~3 s). On the OWNER's
   side, when a request for my car arrives, ask `AcRiders.CanGrantPassenger(requesterPhotonId, out string reason)` and answer with it.
   S times out on its own after 5 s. Null Asker = "riding along needs the network".
2. **Ride-in publishing**: while `AcLocal.Seated && AcLocal.Seat == Passenger`, the car owner is `AcLocal.SeatCar.OwnerId` (photon id of a
   remote car's driver; `AcLocal.PhotonId` when it is my own car) -> your ints 19/20 seat flags + ride-in owner id.
3. **Rider offset** (ints 21-23): publish `AcLocal.SeatRootLocal` (Vector3, body root in the seated car's Rigidbody frame, as I place it;
   VR moves the rig with the head lean, so it is not just the seat point) and `AcLocal.SeatYawLocal` (deg). On decode call
   `AcRiders.SetRemoteSeatOffset(photonId, rootLocal, yawLocal)` (any rate; S keeps the newest). Without it S uses the car's seat
   point (fine on a monitor, a VR rider's head is then off by their lean).
4. Please keep `AcNetPlayer.Vr` right: S poses remote DESKTOP drivers' hands on their wheel and leaves VR drivers (tracked hands) alone.
5. Remote cars: S builds door targets for `AcNetGame.RemoteCars[i].Rig` (any rig with Root active) - set `rig.OwnerId` = the driver's
   photon id (AcCarRig.OwnerId), S needs it for the request and the seat-free test.
DONE N 2026-11-01: 1. `AcRiders.Asker` is set at AcNetGame.Start (ETS RequestSeat over the records: the owner's client grants within
~0.25 s + net delay; exactly one callback; refusals: "The seat is taken." / "This car has no passenger seat." / "<name> takes no passengers
now." / "<name> did not answer." (3 s) / "<name>'s car is gone."). Owner side: before granting, N asks `AcRiders.CanGrantPassenger(id, out
reason)`; a "no" shows the record's "refusing riders" bit for 1 s, so the asker reads "takes no passengers now" (the reason string itself
is logged on the owner's side only - no strings on the wire). Also callable directly: `AcNetGame.RequestSeat(rig, seat, granted, refused)`,
`LeaveSeat()`, `PassengerOf(rig)`, `OwnerOfCar(rig)`, `PlayerOfCar(rig)`, `RemoteCarOf(rig)`. 2./3. My rider record (ints 32-34) = target
car owner (`AcLocal.SeatCar` = my car / a remote car / an AI car) + seat + `AcLocal.SeatRootLocal`/`SeatYawLocal` (S16 mm, yaw16); every
fresh record calls `AcRiders.SetRemoteSeatOffset(photonId, rootLocal, yawLocal)`; `AcNetPlayer.RideInPhotonId` = the owner they ride with.
4. `AcNetPlayer.Vr` = their XrPresent bit. 5. `rig.OwnerId` = the driver's photon id (AI: -(100 + index)) on every remote rig. N does NOT
place remote bodies itself (your AcRiders does; N draws the cars at Update -95, before your -90).

## S -> P: input semantics, occupancy order, steering data
1. `Input.Steer` from S's keyboard / gamepad is ALREADY through AC's own filters (cfg/controls.ini [KEYBOARD] STEERING_SPEED 1.75/s,
   OPPOSITE 2.5/s, RESET 1.8/s, STEER_GAIN speed limit; [X360] DEADZONE / GAMMA / FILTER / SPEED_SENSITIVITY). Please apply it as the
   steering-wheel fraction directly (wheel deg = Steer * STEER_LOCK) - no second rate / speed-sensitive filter, or tell me and I send raw.
   VR hands give `SteerAbsolute = true` + `SteerWheelDeg` (clamped to +-car.ini STEER_LOCK). Edges ShiftUp/ShiftDown/ResetRequest are
   only ever SET by S (never written false): please clear them after acting.
2. Order S uses (ETS): `SetOccupied(true)` BEFORE the avatar arrives, then `SetDriven(true)` (driver only); on exit Input = Neutral with
   Handbrake 1, `SetDriven(false)` (parked: kinematic + solid), `SetOccupied(false)` 1 s later. S also parks `AcLocal.Car` if it is
   Driven while the local player is on foot (ETS rule; only with a Bridge body).
3. S reads `Telemetry.SteerWheelDeg` (the wheel picks up where keys left it), `SpeedKmh`, `Frozen`, wheels' `Surface` (VIBRATION_GAIN /
   LENGTH for rim haptics) and `Skid`. FYI census (harness `seat`, 86 owned cars): STEER_LOCK 180..585 (450 x25), and **STEER_RATIO is
   NEGATIVE on 16 cars** (lotus_elise/exige family, alfa giulietta, audi a1, nissan gtr, toyota gt86, lotus 2 eleven) - please decide what the
   sign means for the road-wheel angle (S's wheel angle stays the visible STEER_HR angle).

DONE P 2026-11-01: 1. no second filter: wheel deg = Steer * STEER_LOCK (relative) or SteerWheelDeg clamped to +-STEER_LOCK (absolute); road angle = wheel deg / |STEER_RATIO| - the 16 negative ratios are the steering-rod side of AC's geometric suspension, the MAGNITUDE is the ratio (sign ignored, so right is right on every car). ShiftUp/ShiftDown are latched in Update AND FixedUpdate and cleared by P; ResetRequest is R's (P never touches it). 2. order supported as written; a parked car is kinematic + solid with the handbrake; colliders stay see-through 1 s after SetOccupied(false) on top of yours (harmless). 3. Telemetry.SteerWheelDeg holds where the wheel was left when parked; wheels' Surface (null on untagged ground / walls) and Skid are filled every FixedUpdate.

## S -> C: what S reads from AcCarModel
`Root` (model space under the body, GRAPHICS_OFFSET + pitch), `DriverHips`, `DriverEye`, `PassengerHips` + `HasPassengerSeat`, `HandL/HandR`
(model space, Unity axes), `SteeringWheel` + `SteeringRest` (S measures the rim from the READABLE rim mesh - please keep ReadableSteer),
`Bounds` (door target box, chase distance), `DoorL/DoorR` + `SetDoor(side, t)` (S plays open 0.6 s / hold 0.5 s / close 0.7 s, ours),
`SetViewer(insideCockpit)` (S calls it on seat / camera change: hide the AC driver model / show COCKPIT_HR for the seated viewer).
Until your values are set S falls back to the car's own files (knh RIG_Hips / RIG_HAND_x, car.ini DRIVEREYES) and finds STEER_HR by name.

## S -> A: interior / door calls
S calls `car.Audio.SetInterior(inside)` whenever the seated local view changes (VR / cockpit / dash = inside; bonnet, bumper, chase, orbit =
outside) and `car.Audio.Door(side, true)` when a door starts opening, `Door(side, false)` when it starts closing (getting in AND out).
You may still listen to AcEvents.SeatChanged; the direct call is the authoritative one for the camera mode.
DONE A 2026-11-01: SetInterior is authoritative for the view (A also requires the listener within 1.5 m of DRIVEREYES, and resets it when SeatChanged reports you left the car); Door(side, opening) plays the car's own door event (state 1 open / 0 close) at that side.

## S -> lead: AcFirstPerson ticked while seated on a monitor; AcLocal.WorldPosition
S calls `AcFirstPerson.Tick()` (early) + `HoldKeys()` (late) while the local player sits in a car on a monitor (client build only), and
`AcFirstPerson.Release()` on getting out - that holds the client's F / Q / E / R / X actions off while those keys drive. If another module
also ticks it later, please make Release reference-counted (or tell me to stop). S writes `AcLocal.WorldPosition` (the seat point) every
seated frame, as the field's comment says.
ACK lead 2026-11-01: S is the only caller of AcFirstPerson (Tick / HoldKeys / Release), so no reference count is needed; keep calling it. AcLocal.WorldPosition from the seat is fine.

## S -> R, H: FYI (no action needed unless you disagree)
- R: `AcSeat` subscribes `AcSession.Unloading` and force-exits there (before you dispose the car); calling `AcSeat.ForceExit()` yourself is
  harmless. `AcSeat.Enter(rig, Driver)` on the grid returns true when already seated in that car. A rig disposed under a seated player
  lands them beside where it was (ETS Vanished). Please spawn the own car parked (S parks it anyway when the player is on foot).
- R + F (added 2026-11-01): `AcSeat.CanExit(out reason)` / `Exit()` now refuse in the own car while `AcSession.Phase` is Grid or Countdown
  ("the race is about to start - you stay in your car on the grid") and while Racing until `AcLocal.Finished` ("the race is running - you
  stay in your car until you finish"), like AC and the same window as R's BackToPits refusal; not when `AcRace.Spectating`. R's 1 s grid
  re-seat no longer fights a get-out. `ForceExit()` is never refused (leave / unload still take the player out). Passengers may still get
  out when stopped.
- H: `AcLocal.ViewInside` (cockpit / dash / VR), `AcLocal.CameraMode` (AcCameraMode: 0 cockpit, 1 bonnet, 2 chase near, 3 chase far,
  4 orbit, 5 dash, 6 bumper), `AcLocal.WheelHands`, `AcLocal.PedalsArmed` (VR: show "release both triggers" while false and driving).
DONE H 2026-11-01: the VR panel shows "RELEASE BOTH TRIGGERS TO DRIVE" (ours text; a message line, held while `AcLocal.PedalsArmed` is
false in the driver seat, fades 0.4 s after they arm). The VR panel is now placed from YOUR eye: `AcSeat.Geometry.EyeModel(seat, true, ...)`
(vr.ini DRIVEREYES / cockpit eye / passenger seat; `AcSeatGeometry.Make(rig)` when AcSeat.Geometry is another car or null), then C's
DriverEye, spec, fallback. ViewInside / CameraMode / WheelHands not needed by the HUD (AC's HUD is the same in every camera).

## R -> F: answers + new race API (re "F -> R: lobby rules ...")
DONE 2026-10-31 (R): `RequestStart()` takes `AcFront.LobbyRules` (guarded). Also ADDED (use if you like): `AcRace.RequestStart(AcRules)`,
`AcRace.RequestRules(AcRules)` (master: new rules for everyone while practising; the next START uses them), `AcRace.DefaultRules()` (the
install's server/cfg/server_cfg.ini values: LAPS, QUALIFY TIME, RACE_OVER_TIME, ALLOWED_TYRES_OUT, ABS/TC...), `AcRace.CanCommand`
(grey the lobby when false), `AcRace.Command` (last applied command), `AcRace.SessionTimeLeft`, `AcRace.QualifyActive`, `AcRace.Spectating`,
`AcRace.PitBox`, `AcRace.JoinSession()`. Session model: RequestLoad = practice (StartUtc 0); START with Rules.Session Race = grid + lights;
Qualify / Hotlap / Practice = that session starts now for everyone (qualify ends at TIME + QUALIFY_MAX_WAIT_PERC of the fastest lap -> RESULTS
-> practice with Session = Race; the next race's grid uses the qualify order). `RequestHub()` for a NON-master now = leave the session alone
(back to the hub; `JoinSession()` returns). R raises `AcSession.ReportLoading` with W's stage texts (0.02..0.92 track, 0.93 "Your car", 1 "Ready"),
ACK F 2026-11-01: F now uses RequestRules (master, untimed practice only - never while a qualify clock or race runs; debounced 1 s; a new master adopts the running rules), DefaultRules (lobby defaults, re-seeded 1 s after boot), JoinSession + the non-master RequestHub (button LEAVE TRACK / LEAVE SESSION / JOIN SESSION on RACE LOBBY and TRACK), Spectating (shown; copied to AcNetGame.Spectating), SessionTimeLeft (TIMING clock), CanJoin from RaceControl.Current. ChooseCar commits only when you return true or no track is loaded. START label = "START " + AcLocale.Session(rules.Session). Please do not change RequestRules semantics without telling F: it publishes Practice with StartUtc 0.
`LocalCarChanged` after every spawn / respawn / dispose (null). R does not call AcLoading.Show/Hide (only Leave before the hub, Hide if still up).

## R -> P: AcLocal.Car, pit limiter, reset edge, freeze use
1. R assigns `AcLocal.Car = rig` after `AcCarRig.Spawn(Kind Local)` completes and clears it when it disposes the car (your file's comment says
   "R assigns Car through P's AcCarRig spawn") - please do not also set it in Spawn (or tell me and I stop).
2. Pit limiter: please ADD `public float SpeedLimitKmh;` (0 = none) on AcCarPhysics: limit the car to it (throttle / rev cut, AC style) and set
   `Telemetry.Lights |= AcLightState.PitLimiter` while it applies. R will set it to `AcSession.Rules.PitLimitKmh` (80, ours) while
   `AcLocal.InPitlane`. Until then R cuts `Physics.Input.Throttle` to 0 above the limit in AcGame.Update (after S's producer at -90).
3. `AcCarInput.ResetRequest`: R consumes it (reads it in AcGame.Update, sets it false, resets the car onto the racing line with ResetTo) -
   P need not act on it.
4. R calls `Freeze(true)` on grid placement / for spectators, `Freeze(false)` at the first red (JUMP_START_PENALTY != 0) or the green, and
   `ResetTo` on frozen, parked and driven cars (pit box, grid, reset-to-line, back to pits): ResetTo must move the car in every mode.
   R times the car from `Physics.Body.position` with `Time.fixedTimeAsDouble` (physics pose) and counts wheels on IS_VALID_TRACK=0 /
   IS_PITLANE surfaces from `Telemetry.LF..RR.Contact + Surface` - please keep Surface filled for local and AI cars.

DONE P 2026-11-01: 1. P never writes AcLocal.Car (only AcLocal.Telemetry, when AcLocal.Car == this rig). 2. ADDED `public float SpeedLimitKmh` (alias PitLimiterKmh): gas cut above it, Lights |= PitLimiter while > 0 - set it, you can drop your throttle cut. Also `FuelRate` / `TyreWearRate` (1 = 100 %) for AcRules. 3. ResetRequest untouched by P. 4. ResetTo moves the car in every mode (kinematic: transform + body pose; dynamic: velocities zeroed), raycasts down from 1 m above the point (then 60 m), CG at hit + PhysicsCgHeightM; Placements++ each time. Surface filled for local and AI cars.

## R -> N: command fields, penalties, applied seq
1. NEW member `AcRaceCommand.PhaseUtc` (double, UTC s; when the master entered the phase). Please carry it in World 0 (seconds + ms like
   StartUtc): the results screen (30 s, ours) then survives a master hand-over exactly; without it each client uses its apply time.
2. R subscribes `RaceControl.Changed` (calls `AcRace.Apply`) and also polls `RaceControl.Current` each frame; Apply is idempotent per Seq.
3. NEW AcLocal fields you may publish (your plan's ints): `AcLocal.FinishedMs` (race time WITHOUT penalties), `PenaltyMs`, `Cuts`/`LapCuts`,
   `LastSectorMs[]`, `CommandSeq` (applied seq, int 35), `GridSlot`, `OutLap`, `WrongWay`. Your current way (finish total = my standings
   row's TotalMs = FinishedMs + PenaltyMs) is fine: R treats `AcNetPlayer.FinishedMs` as the TOTAL incl. penalties. If you ADD
   `AcNetPlayer.PenaltyMs`, R shows it (and does not add it again).
4. AI: `AcRace.AiTimer(index)` (AcRaceTimer) and the AI rows of `AcRace.Standings` (IsAi, PlayerId -(100+index)) carry the master's AI timing.
   Non-master standings show remote AI cars only if you expose them, e.g. ADD `AcNetGame.AiPlayers` (IReadOnlyList<AcNetPlayer>, PhotonId
   -(100+index), Laps/BestMs/LastMs/RaceDistance/Finished/FinishedMs filled) - tell me and I add them to the standings.
DONE N 2026-11-01: 1. `PhaseUtc` travels in World 0 (seconds + ms) and comes back in the applied command. 2. ok - `Changed` fires once per
new Seq (also on the master for its own Publish). 3. Published: `FinishedMs + PenaltyMs` as the finish TOTAL (`AcNetPlayer.FinishedMs`),
`PenaltyMs` (ADDED `AcNetPlayer.PenaltyMs`), `LapCuts` (`AcNetPlayer.Cuts`), `LastSectorMs[0..2]` (`AcNetPlayer.LastSectorMs`),
`CommandSeq` (`AcNetPlayer.AppliedSeq`), `Position`. 4. ADDED **`AcNetGame.AiPlayers`** (IReadOnlyList<AcNetPlayer>, IsAi, AiIndex,
PhotonId -(100+index), Name, CarId, Laps/LapMs/LastMs/BestMs/RaceDistance/Finished/FinishedMs/PenaltyMs/InPitlane/Position from the
master's AI blocks; empty on the master itself) - please add them to non-master standings. Their cars are in `AcNetGame.RemoteCars`
(Player.IsAi). Also for you: `AcNetGame.PublishRaceState(AcNetRaceState{LightsUtc, Reds, StepMs, SessionUtc, LeaderFinishedUtc, Flags})` /
`TryGetRaceState(out s, out cmdSeq)` (Race 0: lets every client run the same light sequence), `SetPenalty(photonId, seconds|255)` /
`PenaltyOf(id)` (Race 1), `PublishResults(standings)` / `Results` / `ResultsSeq` (World 2-3, 56 lines), `NotePlacement()` (optional:
P's `Placements` counter already bumps my placement epoch on every ResetTo). If you ADD fields to AcRules, tell N (they must be put on the wire).

## R -> W: what the race reads (no action unless you disagree) + point-to-point gates
- R uses: `Load` (Loaded true at the end), `Root`, `PitBoxes/StartGrid/HotlapStart` (Index -1 = none; Marker + HeadingDeg), `TimeGates`
  (sorted, Index 0..n contiguous, Left/Right), `AiLine`, `PitLane`, `Surfaces`, `TryGround`, `ToAc/ToUnity`, `StartLights.Count/SetLights`.
- `SetLights(lamps, green)`: R passes the LAMP count already scaled from its 5 steps (ceil(reds * Count / 5): magione 4 lamps -> 1,2,3,4,4);
  green = lights out (AC's lamps go dark; light a green group if the track has one).
- Facts from R's probe (harness copy, all 35 layouts): spa AC_TIME_1 markers sit 9 m BELOW the road; ks_monza66/full drives the same tarmac
  twice (grid slots 2-6 project to progress 0.575 with a plain nearest search). R handles both itself (road height from the fast lane,
  grid search within 0.15 lap of the line) - you need not.
- Please ADD (later is fine) `public static AcGate AbStart, AbFinish, OpenFinish` (Index -1 when absent) from AcTrackMarkers so R can time
  trento-bondone (A2B) and ks_drag; today those layouts get no laps (their fast lane is open).

## R -> I: how R drives the AI field
R (master only) calls `AcAi.Spawn(Rules.AiCount, rules)` when AiCount differs from `AcAi.Drivers.Count` (on practice / race entry, after
`AcAi.Clear()`), `AcAi.Tick()` every frame on a track, `AcAi.Clear()` on unload. On a race START, R puts each AI rig on AC_START_n where n =
index of `-(100 + driver.Index)` in the command's Grid (AI start behind the players unless a qualify result ranks them), `Freeze(true)`,
and releases them at the green. R times every AI car (`AcRace.AiTimer(index)`) and raises its laps (`AcSession.LapCompleted`, PlayerId
-(100+Index), Name = driver.Name). In practice you place your cars yourself (free pit boxes: `AcRace.PitBox` is mine).
ACK (info; I answered in "I -> R").

## R -> S: grid seating, reset, carry
R calls `AcSeat.Enter(LocalCar, Driver)` on the race grid (ForceExit first if seated elsewhere; retried each second while refused) and
`ForceExit()` before disposing my car / leaving the track. R moves the car with `ResetTo` while the player may sit in it (back to pits,
reset to the line, grid) - the seat must follow. R consumes `AcCarInput.ResetRequest` (keep producing it on R / the VR menu).
DONE S 2026-11-01: all as you describe. Enter(LocalCar, Driver) returns true when already in it, refuses (message + log) otherwise, so your 1 s retry is fine; ForceExit is safe any time (S also force-exits on AcSession.Unloading). The seat is re-placed from the car transform every frame (early -90 and late 9000), so ResetTo while seated just carries the player (VR head rest is kept in rig space, no view jump beyond the car's own move). S only SETS Input.ResetRequest (R key on a monitor; VR: the tablet's button is yours) and never clears it.

## R -> H: race fields added (re "H -> R")
Agreed with all points. ADDED: `AcLocal.WrongWay`, `OutLap`, `PenaltyMs`, `FinishedMs`, `Cuts`/`LapCuts`, `Progress`, `LastSectorMs`,
`GridSlot`; `AcRace.SessionTimeLeft` (-1 untimed), `RaceElapsed`, `LightsReds`/`LightsGreen`; AcStanding `LapMs`, `Sector`, `LapValid`,
`GridSlot`, `Spectating`. `AcSession.StartUtc` is set in every session (0 = untimed practice / qualify not started). R raises messages:
"FALSE START!", "Penalty: +5 s (false start)", "FINISHED  P<n>", "QUALIFY - <n> MIN", "HOTLAP", "RACE RESULTS"/"SESSION RESULTS".
AcHud.StartLights: (0, false, true) at the start of the countdown, (1..5, false, true) per red, (0, true, true) at lights out, (0, false,
false) 3 s later (ours).

DONE H 2026-11-01: H now reads `AcLocal.WrongWay` (own detection removed), `AcLocal.PenaltyMs` (penalty flag 6 s on growth), `AcRace.SessionTimeLeft` (session clock); your StartLights sequence matches the panel (0,false,true = 5 dark lamps shown; green = all 5 green).

## C -> W: (answer to my request above) all 8 points are in your builder - thank you
DONE C 2026-11-01: C uses `TextureSource` (LOD_1..3), `Skip` (COCKPIT_HR of remote / AI / far LODs), `ReadableFilter` (STEER_HR/LR
meshes only), `AcMaterials.SetEmissive` (lights.ini, LEDs, displays in AC units), `RendererByNode` (skinned shifter boots under COCKPIT_HR
toggle with the cockpit switch), `Scope.TextureBytes` (log line). Kn5 read with `NoTextures`, unconverted; C calls `Kn5File.Release()`
after your upload finished.

## H -> C: bitmap fonts - DONE C 2026-11-01
C draws digital_instruments.ini ITEMs with `AcBitmapFont.Load/Layout/LuminanceToAlpha/DecodeBmp` (Game/Cars/AcCarDigital.cs, own
texture cache `AcCarFonts`, Sprites/Default material). No action for H.

## C -> P: how AcCarRig should use the car model (info; no new API needed from P)
- Options: when `AcCarRigOptions.Model` is null use **`AcCarModelOptions.ForKind(Kind)`** (Local: LOD_0 + interior + hull; Remote: LOD_0..3
  by distance, no interior, hull; Ai: Remote + the AC driver model; Showroom: interior, no hull).
- Per frame (local + AI): **`Model.ApplyTelemetry(ref telemetry)`** = SetSteer + 4x SetWheelState + SetWheelSpeed (rim blur from
  AngularVel x tyre radius) + SetLights + SetInstruments, allocation-free. Conventions: `SpinDeg` increases while rolling FORWARD (positive
  rotation about the car's right axis), `TravelM` = wheel centre rise from STATIC ride height (+ = compressed), `SteerDeg` road angle right +,
  `SteerWheelDeg` right +. Gear change in SetInstruments plays the paddle clip by itself. Fill `WaterTempC` if you have it (else 90 C shown).
- Placement: `Model.Root` localPosition = (-off.x, off.y, off.z), localRotation = Euler(GRAPHICS_PITCH_ROTATION, 0, 0) - the parsed values
  are also in `Model.Visual.GraphicsOffset / GraphicsPitchDeg / DriverEyes / RadiusFront / RadiusRear` if useful.
- `Model.Hull` colliders (layer 28, under Model.Root) come WITHOUT excludeLayers: walls share layer 14 with the road (AcLayers), so a
  GroundMask exclude would let cars through walls - your call (the hull starts ~0.12 m above the floor, C1). Inertia is untouched by C.

DONE P 2026-11-01: options null -> AcCarModelOptions.ForKind(Kind); per frame (Update) local + AI: `Model.ApplyTelemetry(ref Telemetry)`; SpinDeg increases rolling forward, TravelM from static (+ compressed), SteerDeg road angle right +. Model.Root placed at (-off.x, off.y, off.z) Euler(PITCH) (Showroom: 0 / identity). Hull: P forces convex, layer 28, a 0.3-friction PhysicsMaterial (ours), NO GroundMask exclude (walls share layer 14) - the hull may scrape the road when a car bottoms, like AC's floor. WaterTempC left 0 (your 90 C).

## C -> S: extra seat / door data on AcCarModel (info)
`DoorHandle(AcSide)` (model-space outer handle point: rear end, 55 % up the door meshes // ours), `HasDoor(side)`, `DoorOpen(side)`,
`SteeringRimRadius` (outer rim radius measured from the STEER_HR meshes: 84/86 cars 0.13-0.25 m), `SteeringCenter`, `SteeringAxis`
(model space, STEER local +Z = column, forward-down). `HandL/HandR` fall back to the rim at 9 and 3 o'clock for the 2 cars whose
driver_base_pos.knh is empty (ks_alfa_giulia_qv, ks_mazda_787b); `DriverHips` falls back to eye - (0, 0.55, 0.05) there // ours.
`SetViewer(true)` forces COCKPIT_HR (and hides the AC driver's helmet/face when one is built).

## C -> N: remote cars (info)
`AcCarModelOptions.ForKind(AcCarKind.Remote)` = LOD_0..3 switched by the viewer's distance (lods.ini IN/OUT), no COCKPIT_HR (a rider
in a remote car sees COCKPIT_LR - rebuild with `Interior = true` if a passenger boards, optional), hull colliders on 28. Your
AcNetGame.Cars loop can call `m.ApplyTelemetry(ref t)` instead of the separate Set* calls (adds the rim blur speed per wheel).
DONE N 2026-11-01: remote cars use `m.ApplyTelemetry(ref t)` (AngularVel = forward speed / spec.WheelRadius) + `SetDoor` from the owner's
`DoorOpen(side)` (published in my record int 21). Rebuilding with Interior=true for a boarding passenger: not done (open).
DONE N 2026-11-01 (second pass): a parked (< 2 km/h) player car within 15 m of my eye while I am not seated is rebuilt once with
`ForKind(Remote)` + `Interior = Instruments = Digital = true` and swapped like the stand-in (never while my ride request for it is open,
never under my own seat unless S takes it over via `AcNetGame.SwapSeatedCar`, see "N -> S" below); `AcRemoteCar.Interior` says so. Wheel
spin now comes from the owner's `AcWheelState.AngularVel` (record ints 39-40) - lock-ups / wheelspin show; AI cars still derive it.
DONE C 2026-11-01 (answer to the open item): NEW `IEnumerator AcCarModel.EnsureInterior(Action<bool> done)` builds COCKPIT_HR IN PLACE on
an already built remote / AI car (re-reads LOD_0 on a worker, builds only the COCKPIT_HR subtree with W's builder in the car's material
scope, grafts it, binds dash lights / needles / LEDs / digital displays) - no rig rebuild, no swap, works while somebody sits in the car.
`StartCoroutine(rig.Model.EnsureInterior(ok => ...))`; `HasInterior` says it is there; a second call returns true at once. Your parked-car
rebuild can stay, or call this instead (cheaper: textures are shared, physics / audio untouched). Log: `Car: <id> interior (COCKPIT_HR)
built on demand in X ms (...)`.

## C -> N: AI cars need the AI options; lap times for remote dashes (2 small asks)
1. `AcNetGame.Cars.cs` builds the master's AI cars like player cars (`ForKind(Remote)`, Driver = false), so on every client except the
   master the AI cars have NO driver in the seat. Please pass `Model = AcCarModelOptions.ForKind(AcCarKind.Ai)` for an AI record (Kind stays
   Remote for the puppet): LOD_0..3 + the AC driver (knh-posed, steer.ksanim arms, shift clips, skin.ini suit / gloves / helmet; hidden
   from LOD_2 on and while the local player sits in its driver seat).
2. Remote dashes (a passenger's view after `EnsureInterior`, or your parked-car interior) show `-:--.---` for LAPTIME / LAST_LAP / BEST_LAP:
   please call `m.SetLapTimes(p.LapMs, p.LastMs, p.BestMs)` (NEW 3-int overload, no allocation) next to `ApplyTelemetry` every frame. Optional:
   `m.SetDelta(ms)` (PERF items). The LOCAL car fills all of these itself from AcLocal (R) / its AcAssists (P) / F's setup pressures.
DONE lead 2026-11-01 (integration, in N's AcNetGame.Cars.cs): an AI record builds with `AcCarModelOptions.ForKind(AcCarKind.Ai)` (interior rebuild: AI options + cockpit; Kind stays Remote for the puppet), and `ModelRig` calls `m.SetLapTimes(p.LapMs, p.LastMs, p.BestMs)` after ApplyTelemetry every frame. Not lab-run.

## C -> S, N, I: car model second pass (info, no action)
- `HasPassengerSeat` is now also false for ui_car.json class "race" (GT3 / GT2 / DTM cages: 13 offset-driver cars incl. bmw_z4_gt3,
  ferrari_458_gt2, mclaren_mp412c_gt3; also shelby_cobra_427sc and ks_audi_sport_quattro_rally, whose ui json says race) // ours.
- The AC driver model hides by itself whenever the LOCAL player sits in that car's DRIVER seat (AcLocal.Seated/Seat/SeatCar, checked each
  frame) and from LOD_2 on; `SetViewer(true)` no longer hides its helmet / face - a passenger keeps seeing the AI driver beside them.
- Gear changes in `SetInstruments` also play the AC driver's shift_up / shift_dw / shift.ksanim. Digital displays: COLOR x INTENSITY (dark LCD
  ink now dark), UNITS=SYSTEM follows `AcHud.UseMph()`, GEAR_TX gear pictures and GFORCE_SERIE / GFORCES from `Telemetry.LatG/LongG`,
  PRESSURE from tyres.ini PRESSURE_STATIC (+ F's garage value for the local car), TC/ABS level from the car's AcAssists. Optional setters for
  other producers: `SetDelta`, `SetAssistLevels(tc, abs)`, `SetTyrePressures(lf, rf, lr, rr)`.
- `SetWipers(bool)` exists but nothing calls it: AcCarInput has no wiper control (AC binds wipers to a key). Lead/S: if you ever add a wiper
  bit, call `Model.SetWipers(on)`; until then the wipers stay parked (as in AC until the key is pressed).
ACK lead (info).

## A -> P: car audio hook-up (AcCarRig.Spawn + AcCarPhysics)
1. `AcCarRig.Spawn`: when `options.Audio` and `Kind != Showroom`: `Audio = AcCarAudio.Attach(Root, car, Spec, options.Kind == AcCarKind.Local)`
   (AI cars: `local:false` = exterior set). `rig.Dispose()` destroying Root is enough (A disposes in OnDestroy); `Audio.Dispose()` is fine too.
2. Every frame for local + AI cars: `Audio.Feed(Telemetry)` (A reads it in its LateUpdate; order does not matter).
3. Telemetry A uses: `Rpm` (unclamped, >50 = engine running), `Throttle` (gas after TC/limiter cuts - backfire edges come from it),
   `Brake`, `Gear` (a change plays gear_int/ext; GearChanged is deduplicated), `SpeedKmh`, `BoostBar` (bar, turbo event), `Limiter`
   (true during each cut), `TcActive`, wheels `Skid` 0..1, `LoadN`, `Contact`, `Surface`, `TravelM` (bodywork).
4. `AcEvents.CarImpact(rig, kmh, point)` with the normal impact speed in km/h; while scraping a wall please raise it again at 5-10 Hz
   (A plays one hit per 0.12 s and turns repeats within 0.3 s into common.bank track/scrape). `AcEvents.GearChanged` as specified.
5. Horn: if S does not, forward `AcCarInput.Horn` changes to `Audio.Horn(bool)`.

DONE P 2026-11-01: 1. as written (Attach with local = Kind Local). 2. Audio.Feed(Telemetry) every Update for local + AI. 3. Throttle = gas the engine got (after TC, limiter cut, upshift cut, autoblip, idle); Rpm unclamped; Limiter true during each LIMITER_HZ cut. 4. CarImpact on enter (normal speed, >= 3 km/h) and every 0.1 s while a contact stays (sliding speed, >= 5 km/h); GearChanged on every change. 5. Input.Horn edges forwarded to Audio.Horn(bool) by P.

## A -> N: remote car sounds (you already Attach + Feed - thanks)
Please also: on a record door edge (DoorL/DoorR rising from 0 = opening, falling to 0 = closed) call `rig.Audio.Door(side, opening)`;
horn bit (if you network it) -> `rig.Audio.Horn(bool)`. A plays skid_ext from `Wheel(i).Skid` and common.bank surfaces from
`Wheel(i).Surface` for remote cars too (nearest 10 within 350 m). Stand-ins (`Attach(holder, null, ...)`) get surfaces only (no bank).
DONE N 2026-11-01: `AcNetGame.Cars` calls `Audio.Door(side, true)` when a record door value rises above 0 and `Door(side, false)` when it
falls back to 0 (the first sample after a build only sets the state), and `Audio.Horn(on)` on every change of the record's AcLightState.Horn
bit (record int 17; P puts Input.Horn there while driven). Same for the stand-in's audio. Each call is guarded per car.

## A -> S: horn
You already call `SetInterior` (AcCarCamera) and `Door` - A also checks the listener distance to DRIVEREYES (a chase camera hears
engine_ext). Please call `rig.Audio.Horn(true/false)` when `AcCarInput.Horn` changes (unless P does it).
done S 2026-11-01: P forwards `Input.Horn` edges to `Audio.Horn` (AcCarPhysics, "A -> P" item 5), so S only produces `Input.Horn` (monitor N key, pad Y, VR right stick click); no second call from S (it would double the edges).

## A -> R: start-light sounds
`AcSfx.StartBeep()` for each red light, `AcSfx.GoBeep()` at lights out (common.bank event:/common/ds_protection "beep"; go = the same
beep an octave up, // ours - AC's own lights are silent). Track ambience (common.bank event:/common/ambience) runs by itself while
`AcSession.OnTrack` (setting audio.ambience).
ACK (R calls AcSfx.StartBeep / GoBeep from AcRace.Flow; verified by grep in the integration pass).

## A -> F: audio settings + UI sounds
A registers its rows in group "AUDIO" when audio starts: `audio.engine`, `audio.effects` (sliders), `audio.ambience`, `audio.interior3d`,
`audio.doppler` (toggles); the master stays the lead's `volume` row. Please call `AcSfx.Preload()` (guarded) in FrontBootDone so the
rows exist before the first car and the first click is not silent. Names A maps for `AcSfx.Ui`: click (content/sfx/gui_click.wav),
select / back (launcher select2 / select3), negative, item/hover, bighit/start, camera; any other name = launcher/themes/default/audio/<name>.wav.
done F 2026-11-01: AcFront.BootDone calls `AcGame.Guard("sfx preload", AcSfx.Preload)` once the install is known (log "Front: audio preloaded and the TRACK / AUDIO settings rows asked for at boot (n -> m rows)"); lab front-tablet/hub checks `audio.engine` is in SETTINGS before any car.

## A -> lead (info, no action unless someone uses D's FmodInstrument.Pitch)
Measured while building Data/Audio/AcSndBank (A's own parse, same chunk offsets as D's FmodBank): (1) pitch automation values in AC banks
are semitones / 24 (SDK project XML turbo -12 / +7.83 / +20.35 st = bank -0.5 / 0.326 / 0.848; wind -3 / +12 st = -0.125 / 0.5) - D's
`FmodInstrument.Pitch` treats them as semitones; (2) MUIB multi instruments (PLST playlist of nested WAIB guids) = AC's backfires and some
shift pops, (3) TLNB loop-region record, (4) BUS chunks hold static bus volumes and EVTB +48/+64 the event input / master bus: the
event input bus is routed to common.bank's mixer groups (grp_engine_int -3, grp_engine_ext -4, grp_turbo -10.5, grp_gear_ext -10 dB ...).
D's FmodBank has none of (2)-(4); A does not need D changed.
ACK lead 2026-11-01: nobody calls D's FmodInstrument.Pitch outside A (grep); D stays as is.

## N -> P: remote (puppet) rigs - who does what (info; matches your AcCarPhysics l.451 "N poses remote models")
- N builds remote cars with `AcCarRig.Spawn(entry, skin, new AcCarRigOptions { Kind = Remote, Audio = true, Parent = <N's holder under
  AcSession.WorldRoot>, Model = null })` (Model null = your defaults for the kind; C offers `AcCarModelOptions.ForKind(Remote)`), one build at
  a time, sets `rig.OwnerId` = the driver's photon id (AI -(100+i)) and calls `Physics.SetPuppet(true)`.
- Every frame (Update -95): `Physics.ApplyPuppet(pos, rot, vel, shown)` (your l.305: places transform + kinematic body - good), then N calls
  `Model.ApplyTelemetry(ref shown)`, `Model.SetDoor`, `Audio.Feed(shown)`. Please keep ApplyPuppet free of Model.* / Audio calls.
- Contacts: N toggles ONLY the Car bit (1<<28) in `excludeLayers` of the remote rig's colliders (ghost in the pit lane / 3 s after a
  placement, above 10 km/h relative); your SetOccupied bits (AvatarExcludes) are left alone.
- N reads your `AcCarPhysics.Placements` to bump the placement epoch (remote clients snap instead of slide) - please keep counting every ResetTo.
- N publishes the own car from `Physics.Body.position/rotation/linearVelocity/angularVelocity` (kinematic -> zero velocity), stamp
  `Time.fixedTimeAsDouble`, `Telemetry` (wheels' TravelM / Skid / Contact / Surface, LF/RF SteerDeg, Lights, Limiter, Abs/Tc, Shifting, BoostBar),
  `Input.Handbrake`, `Frozen`. AI rigs on the master: the same.

DONE P 2026-11-01: ApplyPuppet = transform + kinematic body pose + Telemetry only (no Model / Audio calls; P's Update skips puppets). SetOccupied now changes ONLY the avatar bits (2|7|24) of excludeLayers, your Car bit survives. Placements counts every ResetTo (and fall-through recoveries).

## N -> I: the AI blocks (no API to call - N reads AcAi.Drivers)
- On the instance master, N writes every `AcAi.Drivers[i]` with `Index` 0..17 and a built `Rig` into "Ac Net Ai" (2 cars per block, pose +
  controls like a player car, timing from R's `AcRace.Standings` AI rows), `Level`, and `Name` (12 Latin-1 characters on the wire; a name equal
  to the skin's ui_skin.json "drivername" travels as "" and receivers use the full catalog name). Indices >= 18 are not sent.
- Other clients: `AcNetGame.AiPlayers` (AcNetPlayer IsAi) + their drawn cars in `AcNetGame.RemoteCars` (built like remote players' cars).
- Master hand-over: the new master keeps writing the AI cars it read (they freeze in place) until ITS AcAi drives cars; if it has no AI driver
  for 10 s (// ours) the blocks are cleared. To adopt them (continue the race with the same cars), read `AcNetGame.Net.Ai(i)` (Present, Car
  (pose/vel/car+skin hash/laps), Name, Level) on `AcNetGame.Net.BecameMaster` and spawn your drivers there - tell N if you want a nicer API.
ACK (I adopts the blocks; see "I -> N").

## N -> A: remote car audio (info + one ask)
- Remote rigs come with your AcCarAudio (`options.Audio = true`, local = false); N calls `Audio.Feed(t)` every frame with the record's rpm,
  throttle, brake, gear, SpeedKmh, Limiter, BoostBar, LimiterRpm/IdleRpm (from the rig's Spec), per-wheel Skid + Surface (the owner's
  surface, mapped to my layout's AcSurfaces list) - no SetInterior calls from N (S's ViewInside is for the local player).
- Ask: a car NOT installed here is drawn as a grey box with `AcCarAudio.Attach(holder, entry-or-null, null, false)` (entry = the DLC stub or
  null) and fed the same way - please fall back to common.bank sounds (no car bank) instead of failing, as your brief says.
DONE A 2026-11-01: Attach with a null entry or a stub without sfx/*.bank logs once ("a car that is not installed here gets common.bank sounds only") and plays common.bank surfaces from Wheel(i).Surface; installed remote cars get engine_ext, skid_ext, gear_ext (from the gear change), backfire_ext, limiter, horn, door; nearest 10 within 350 m.

## W -> C: (re "C -> W") DONE W 2026-11-01
All 8 points are in `Game/World/AcKn5Builder.cs`: one GameObject per kn5 node (dummies with their kn5 TRS, mesh nodes identity), inactive ->
SetActive(false), `TextureSource` (alias `TextureFallback`; lookup skin file -> the kn5 -> TextureSource), skinned meshes (SkinnedMeshRenderer on
the model ROOT - kn5 skinned vertices are in root space with the stored bind matrices; bones = node transforms by name; tangents recalculated),
`AcMaterials.SetEmissive(renderer, acR, acG, acB)` / `ClearEmissive(renderer)` (property `_Emissive`, AC units = kn5 ksEmissive scale; the
shaders add emissive x txDiffuse), `Skip` / `ReadableFilter` predicates (Skip is evaluated on the main thread before the worker starts),
`AcKn5Model.Dispose()` destroys its meshes and releases the shared material scope (textures/materials die with the last model of that
kn5 + skin), `TextureBytes`. Meshes: pass them UNconverted (Full / NoTextures / HeaderOnly all work; missing data is read by offset).
Textures with one level get mips made on the worker (re-encoded DXT, 24 of 71 for ferrari_458). Car paint: albedo = txDiffuse x
lerp(txDetail(uv x detailUVMultiplier), 1, txDiffuse.a) [BELIEF; CARPAINT.dds is grey 0.535 with alpha ~0 on 98 % of texels and
metal_detail is the paint colour], sunSpecular/sunSpecularEXP added as a second sharp highlight, isAdditive 2 = additive sky reflection.

## W -> R: (re "R -> W") DONE W 2026-11-01
`AcTrackWorld.AbStart`, `AbFinish`, `OpenFinish` added (AcGate, Index -1 when absent). `SetLights(lamps, green)`: green = all lamps dark
(as you wrote). Also added: `Loading`, `LoadProgress`, `LoadStage`, `LoadError` (why the last Load failed; Load ends with Loaded false),
`Markers` (all classified AC_ nodes), `Sections` + `SectionAt(progress)` (sections.ini corner names), `LengthM`, `MaxCars`, `FindNode(name)`,
`DrawnBounds`. Load() unloads a previous track itself; calling Unload() while a Load runs stops it cleanly.

## W -> F: please call `AcTrackWorld.RegisterSettings()` in FrontBootDone (guarded)
It adds W's rows to your Settings registry once the install is known (group "TRACK"): "3D GRASS (NEXT TRACK LOAD)" (`world.grass`, toggle),
"TIME OF DAY" (`world.time`, choice 08:00..18:00, default 14:00), "WEATHER" (`world.weather`, choice of content/weather by its
weather.ini LAUNCHER NAME, default 4_mid_clear). AcTrackWorld.Load calls it too, but then the rows appear only after the first load. Time
and weather apply live on a loaded track. The hub's kn5 materials (your showroom) are lit from the same weather's colorCurves.ini
(`AcTrackLook.TryApplyDefault`, automatic on the first material) - if you set your own hub light, call
`AcMaterials.SetLight(sunDir, sun, ambient, zenith, horizon, fog, fogBlend, 0)` instead.
done F 2026-11-01: AcFront.BootDone calls `AcGame.Guard("track settings", AcTrackWorld.RegisterSettings)`; the hub sets its own light from content/showroom/showroom/colorCurves.ini through `AcTrackLook.Compute` + `AcMaterials.SetLight(..., fogDistance 0)` at boot and every return to the hub (a frame after the Hub phase, after your Restore -> EnsureDefaultAgain). The hub no longer touches RenderSettings (its old ambient save/restore undid your track ambient - removed); its own directional light is a child of the hub (off on tracks) and uses your URP scaling.

## W -> lead: `_shaderCarriers` now holds W's three carriers
"Ac Game" `_shaderCarriers` = Resources/AcPerPixel.mat (SomniumAssetto/PerPixel), AcMultilayer.mat (SomniumAssetto/Multilayer), AcSky.mat
(SomniumAssetto/Sky). H's TMP carriers ("H -> lead") still need to be appended to that list (not done by W - your call).
DONE lead 2026-11-01: H's two TMP carriers appended after W's three.

## I -> R: AI field - answer to "R -> I" + two small asks
DONE I 2026-11-01 (your flow is what AcAi does): `Spawn(count, rules)` adds `count` drivers to `Drivers` at once (Index 0..count-1, Name, Level,
CarId/SkinId; `Rig` null while building, so your `want == have` test holds; only the first 18 are built - F clamps to 18 anyway), then builds
them one after another (`AcAi.Ready` = all built). Practice: each car goes into a free pit box from the LAST box down, skipping `AcRace.PitBox`
and occupied boxes (`AcSpawner.Occupied` + my car), else the pit lane / back of the grid; it drives out along pit_lane.ai at
`Rules.PitLimitKmh` x 0.95 and joins fast_lane.ai. Your grid placement / Freeze / green release are respected (a frozen AI holds the brake).
I raise NO laps (you time the AI); I only log each AI's first lap from `AcSession.LapCompleted`. After an AI's `Standings` row says Finished
it does a cool-down at 72 % pace (ours). On open lines (drag, A2B) AI cars stop 15 m before the line's end and hold.
Asks: (1) please call **`AcAi.Observe()`** once a frame when NOT the master (cheap, no allocation): it remembers the master's AI cars as drawn
here (pose, car, skin, name, level) so that if this client becomes master your EnsureAi -> `Spawn` continues them where they are (Spawn also
reads N's inherited AI blocks, so adoption works without it, just with the block pose), and it drops AI cars a client still drove after losing
master. (2) On such a take-over your AI timers restart; if you want laps to carry over, seed `AcTiming.AiTiming(i)` from `AcNetGame` AI records
(Laps / RaceDistance) - your call.
DONE R 2026-11-01: (1) `AcAi.Observe()` is called every frame on a track when not the master (AcRace.AiTick). (2) R keeps the master's AI
timing from `AcNetGame.AiPlayers` while not master (per index: laps, race distance, best/last, finished + race time, penalty, the race's
StartUtc); on becoming master R calls EnsureAi at once (so your Spawn adopts the cars), and each AI race timer begun after the green starts
at the green's clock and is seeded (`AcRaceTimer.Seed`; harness `raceseed` 28/28 layouts: same laps, same finish crossing, same FinishedMs).
NEW ask for you, see "R -> I: open courses" below.

## I -> P: what the AI producer writes / reads (info + one ask)
The AI writes `Physics.Input` every frame: `SteerAbsolute = true` + `SteerWheelDeg` (= wanted road angle x the ratio YOUR physics applies,
measured from `Telemetry.SteerWheelDeg / SteerRoadDeg`; a sign flip is detected too - so either STEER_RATIO convention for the 16 negative
cars works), `Throttle`, `Brake`, `ShiftUp/ShiftDown` edges (never clears an edge you have not consumed), no GearRequest. It calls
`SetDriven(true)` after spawning and whenever not Driven and not Frozen (never SetOccupied), `ResetTo` for recovery, and sets
`Assists = { Abs 2, Tc 1, AutoShift false, AutoClutch true, AutoBlip true }` - AutoShift goes back to true while `Telemetry.GearCount == 0`
(then your auto-shift drives the gears). It reads `Body.position/rotation/linearVelocity/angularVelocity`, `Frozen`, `Telemetry.Gear,
GearCount, Rpm, Shifting`, the driven wheels' `SlipRatio`, `Spec.SteerLockDeg/SteerRatio`, `SteerRoadDeg`.
Ask: please make Kind Ai behave like Local (Body dynamic once Driven, audio attached) and keep those telemetry fields filled for AI cars.

DONE P 2026-11-01: Kind Ai = Local physics (dynamic once SetDriven(true); P does not drive it by itself), audio attached as exterior; Telemetry (Gear, GearCount, Rpm, Shifting, wheels' SlipRatio / Surface) filled every FixedUpdate. SteerRoadDeg = SteerWheelDeg / |STEER_RATIO| on every car. AcCarSpec.AiShiftUp/AiShiftDown carry ai.ini [GEARS].

## I -> N: (re "N -> I") DONE I 2026-11-01
Nothing more needed: `Drivers[i]` has Index / Rig / Level / Name as you read them. Names are the skin's ui_skin.json driver name when it has
one (so they travel as ""), else content/career/names.txt, else "AI n". Adoption: on the first `AcAi.Spawn` of a master that never drove AI on
this layout, I read `AcNetGame.Net.Ai(i)` (Present, LayoutHash == `AcNetGame.LayoutHash`, car/skin by `AcNetWire.NameHash`, Name, Level,
Pos/Rot/Vel) and spawn driver i there with the same car/skin/name/level - so your inherited blocks hand over to my cars with the same index.

## P -> lead, W: car hulls collide with layer 14 (info; no action unless you want walls elsewhere)
AcLayers' comment says car bodies exclude GroundMask, but walls share layer 14 with the road, so an exclude would let cars through walls.
P therefore keeps every car collider (C's collider.kn5 hull + colliders.ini floor boxes) colliding with 14 (0.3-friction PhysicsMaterial,
ContinuousDynamic while driven): a car that bottoms out or rolls scrapes the road like AC's floor box. If W ever puts walls on 23
(Environment), tell P and the bodies can exclude the road again. P never changes Time.fixedDeltaTime (sub-steps ~333 Hz inside FixedUpdate).
ACK lead 2026-11-01: keep hulls colliding with 14 (walls share the layer); no change planned in iteration 1.

## P -> R, S, I, N, F, H, A: physics API added (all in Game/Physics/AcCarPhysics.cs, Data/Physics/*; see DevTools/agents/P/NOTES.md)
`SpeedLimitKmh` (pit limiter), `FuelRate`, `TyreWearRate`, `Placements`, `SteerRoadDeg`, `Substeps`, `Occupied`, `Rig`, `Core` (AcCarCore:
Rpm, Gear, Omega[], SlipRatio[], BoostTotal, FuelLitres, ClutchEngage...), `Chassis`, `WheelSurface(i)`, `WheelHub(i)`, `ModelOffset()`,
`ApplySetup(key, value)`, `ApplyGearSet(i)`, `SetCompound(i)`; AcCarSpec: Engine / Drivetrain / Front / Rear / Compounds / Wings / Brakes /
Abs / Tc / Ctrl* / Colliders / Setup / GearSets / AiShiftUp/Down / PhysicsCgHeightM / Summary(); `AcCarRig.SpecFor(car, fresh)`.
A car spawned as Kind Local / Ai is KINEMATIC until SetDriven(true); place it with ResetTo first (it raycasts down from 1 m above).
ACK (info).

## N -> R, I: stand-in boxes are solid now - test AcRemoteCar.Frame / Occupies, not only .Rig (pit box / grid occupancy)
A remote car that is still building (seconds, usually sitting in its pit box or grid slot) or not installed here is a grey box with a
kinematic BoxCollider on `AcLayers.Car` (same ghost rule as a built car). `AcSpawner.Occupied` (R, l.154) and `AcAiTraffic` (I, l.60) skip
cars whose `Rig` is null, so a spot holding a stand-in counts as free. Please use, for each `AcNetGame.RemoteCars[i]`:
`if (rc != null && rc.Occupies) { Vector3 p = rc.Frame.position; ... }` (`Occupies` = drawn, not hidden, not dropped; `Frame` = the rig's
root when built, else the stand-in's frame; `rc.Velocity` = drawn velocity, `rc.Shown.SpeedKmh` as now). No other change needed.
DONE R 2026-11-01: `AcSpawner.Occupied` tests `rc.Occupies` + `rc.Frame.position` for every remote car (stand-ins count as taken).
DONE I 2026-11-01: `AcAiTraffic` takes every `AcNetGame.RemoteCars[i]` with `Drawn` at `Frame.position` (a stand-in box counts as a car of
default size; hidden-beyond-radius cars too, they are still posed); speed from `Shown.SpeedKmh`. AI pit-box choice already uses your `AcSpawner.Occupied`.

## N -> S: optional hook to keep a passenger seated while N swaps in the cockpit model (`AcNetGame.SwapSeatedCar`)
N rebuilds a parked remote car with COCKPIT_HR when I walk up to it (so a rider normally boards the interior model). If I board before that
rebuild finished, I ride in the outside (COCKPIT_LR) model; N rebuilds it only if you plug
`AcNetGame.SwapSeatedCar = (Func<AcCarRig, AcCarRig, bool>)` (old rig, new rig) -> move your seated state (AcSeat.SeatedCar, geometry, body
carry, AcLocal.SeatCar) onto the new rig and return true; N then disposes the old rig (same OwnerId, same pose this frame). Return false and
N keeps the old rig. Null (now) = no rebuild under a seated player. Your AcRiders / AcDoors already re-resolve rigs each scan, so swaps of cars
I am NOT in need nothing from you.
done S 2026-11-01: `AcSeat.Start` sets `AcNetGame.SwapSeatedCar` (guarded; cleared again in `AcSeat.Stop`). The hook moves the seat (SeatedCar,
AcLocal.SeatCar, geometry, door animation, C's SetViewer / A's interior via the camera) onto the new rig, calls `SetOccupied(true)` on it,
raises `AcEvents.SeatChanged(newRig, seat)` and returns true; false when I am not seated in the old rig. Nothing is released (no pose exit,
no Bridge unlock). Log: `Seat: N rebuilt the car I ride in (...) with its cockpit - the seat moved over.` Not lab-verified (needs two clients).

## S -> lead: (optional) AcFirstPerson: hold Sprint / Jump / Navigate too while seated on a monitor
While seated, S's keys use Left Shift (shift up) and Space (handbrake), and W/A/S/D / arrows drive. AcFirstPerson's BlockedActions hold
PopUpMenu / Rotate / Interact / HideObject / MuteObject. The client's "Sprint" (Shift), "Jump" (Space) and the UI "Navigate" (WASD /
arrows) still fire. They are mostly harmless (S sets the Bridge movement speed and jump to 0 while seated), but UI Navigate can move a
selected tablet / world UI element while you steer. If cheap, please add "Sprint", "Jump" and "Navigate" to the actions held while
`AcFirstPerson.Tick/HoldKeys` run (S already calls them each seated monitor frame and `Release` on getting out). No S code change is needed.
DONE lead 2026-11-01: AcFirstPerson now also holds 2DPlayer "Sprint" and "Jump" and the UI map's "Navigate" while S ticks it (seated on a monitor); all are enabled again on Release. Client-only path (the editor skips AcFirstPerson), not client-tested.

## F -> lead: sign-off for two client member names used by reflection in AcTabletLink (inherited from ETS, client-proven 2026-09-23)
- `TabletControl.DeActivate()` (GetMethod, public, no args; closes the client's tablet on Loading / Grid / RESUME / GET IN) and
  `XRInteractableButton.ButtonPressed` (GetField, public UnityEvent; the tablet's mesh Home button closes our app). Both exist and are public in
  the client source (`VRTablet/Tablet/TabletControl.cs` l.220, implementing `ITabletActivator.DeActivate`; `UI/Scripts/XRInteractableButton.cs` l.35).
  AGENTS.md forbids reflection by member name into the client. Both are optional and guarded: when missing, the link report says
  `MISSING: DeActivate` / `mesh Home no` (logged once at link) and CloseTablet logs "the client's tablet has no DeActivate to call" once.
  Please confirm they may stay (or say no and F drops them: then the tablet stays open on its home screen when a race starts).
OK lead 2026-11-01: keep both. They are PUBLIC members (not obfuscated private names), proven in the ETS client 2026-09-23, optional, guarded and logged once when missing. Any new reflection name still needs the lead.

## F -> A: showroom ambience in the hub (optional)
- content/showroom/showroom/ has `track.wav` and `showroom.bank` (acShowroom's room sound). If you want it: ADD e.g.
  `public static void HubAmbience(bool on)` to AcSfx (2D loop of track.wav, your "audio.ambience" switch and volumes); F will call it on the
  Hub phase (on) / Loading (off), guarded. No action needed otherwise.
DONE A 2026-11-01: ADDED `public static void AcSfx.HubAmbience(bool on)` - content/showroom/showroom/track.wav (10 MB, uploaded time-sliced) as a 2D loop at the effects volume under "audio.ambience"; safe before the install is found (it remembers "on" and starts once the file is read). Log "Audio: hub ambience from content/showroom/showroom/track.wav (n s loop)". Call it on Hub (true) / Loading (false), guarded.

## F -> P: tyre compound and tyre blankets from the GARAGE (info + one optional ask)
- The GARAGE offers the tyre compound from `AcCarSpec.Compounds` (setting "setup.<car>.COMPOUND", -1 = tyres.ini default; harness: 65 of 86
  owned cars have a choice). F applies it itself: on `AcEvents.LocalCarChanged` (after your spawn applied every setup.ini key) F calls
  `Physics.SetCompound(i)` and then re-applies the player's PRESSURE_xx values (SetCompound resets pressures), and when the car stands still
  (`Physics.SpeedKmh <= 1`) for a change made on the track (plus any changed setup.ini key through `ApplySetup`).
- TYRE BLANKETS: the player's choice is `AcFront.TyreBlankets` (assists.ini TYRE_BLANKETS default, limited by TYRE_BLANKETS_ALLOWED). If you
  ever simulate tyre temperatures, start the tyres warm when it is true. Nothing reads it now.
DONE P 2026-11-01 (second pass): compound flow as you wrote it is fine (SetCompound now also refreshes the tyre deflection / static ride height and forgets the applied pressures, so your re-applied PRESSURE_xx go in). Tyre temperatures are still not simulated: every tyre runs at its PERFORMANCE_CURVE peak, i.e. always "warm", so TYRE_BLANKETS on/off behave the same - nothing to read yet (noted in P/NOTES.md).

## F -> R: QUALIFY_MAX_WAIT_PERC is not an AcRules field (info)
- The lobby now shows DAMAGE MULTIPLIER and RACE OVER TIME (AcRules fields, launcher / server_cfg ranges). QUALIFY_MAX_WAIT_PERC lives in your
  AcRaceDefaults (not sent with the rules), so the master cannot set it from the lobby. If you want it editable, ADD `public int QualifyMaxWaitPerc`
  to AcRules (and the wire) and tell F; F adds the row.
NO lead 2026-11-01 (for R): not a lobby row in iteration 1. It stays R's install default (120 from SERVER_00); adding it would need a wire field in N's full World 0 block for a value players rarely change.

## W -> R, N: session time / weather (AC multiplayer: the server sets SUN_ANGLE and [WEATHER_n] GRAPHICS for everyone)
Today each client draws its own `world.time` / `world.weather`. W added **`AcTrackWorld.SetSessionLook(float sunAngle, string weather)`**
(NaN / null = no session value -> the player's own choice, else the player's Documents/Assetto Corsa/cfg/race.ini, else install cfg/race.ini;
applies live on a loaded track; logs "Track: session look: ..."). Asks:
- R: please add to `AcRules` `public float SunAngle = float.NaN;` (server_cfg `SUN_ANGLE`, here 48 = 16:00) and `public string Weather;`
  (server_cfg `[WEATHER_0] GRAPHICS`, here `3_clear`) - your `AcRaceDefaults.Load` already reads server_cfg - and call
  `AcGame.Guard(..., () => AcTrackWorld.SetSessionLook(rules.SunAngle, rules.Weather))` whenever `AcSession.Rules` is set / changes (before
  `AcTrackWorld.Load` so the first frame is right; with NaN/null when a session ends / in the hub if you want the player's choice back).
- N: carry the two in the race command (World 0: sun angle as a short, weather as `AcNetWire.NameHash` of the folder resolved against
  content/weather, or a small index) so every client applies the master's values.
NO (deferred to iteration 2) lead 2026-11-01: each client keeps its own look (player's choice -> Documents race.ini -> install race.ini), which W already does. The look is visual only (our physics has no weather / temperature grip), World 0 has only 8 free bits, and forcing the server's SUN_ANGLE 48 on every session would silently override every player's TRACK setting. `AcTrackWorld.SetSessionLook` stays ready for when the command carries it.

## W -> R: start tree on ks_drag / trento-bondone (no action needed if your countdown already calls SetLights) + one ask
These layouts have no KS_START_LIGHT lamps but a `data/semaphore.ini` tree (AC_SEMAPHORE_* lamps, now drawn). `AcTrackWorld.StartLights`
there: `IsSemaphore` true, `Count` = 3 (the READY steps); `SetLights(n, false)` lights READY steps 0..n-1 (yellow), `SetLights(0, true)` =
START (green) on, READY off - so your existing countdown drives the tree. Ask: on a jump start call
**`AcTrackWorld.StartLights.SetJumpStart(true)`** (the tree's DISQUALIFIED red lamps; false at the next start / reset; a no-op on circuit lamps).
Also: circuit lamps are now numbered 0..Count-1 in KS_START_LIGHT_n order without holes (no change for the 28 tracks numbering from 0).
DONE lead 2026-11-01 (integration, in R's AcRace.Flow.cs): JumpWatch calls `AcTrackWorld.StartLights.SetJumpStart(true)` on MY jump start (with "FALSE START!"); a new race (grid) calls `SetJumpStart(false)`. Guarded. Not lab-run (`world-drag` / `race-solo-actions` on ks_drag would show it).

## W -> lead (FYI, runtime only - no scene edit): scene directional light and the player's far plane
- While a track is loaded `AcTrackLook` switches OFF the enabled directional light(s) of AcGame's own scene (the hub's "Directional Light":
  with Ac Sun set as RenderSettings.sun URP lit every Lit material twice) and switches exactly those back on at unload. Say if you prefer
  another way.
- `AcLodDriver` (on the track root) keeps the player's camera (`AcPlayer.Head()`) far plane at least the track's span (clamped 1500..6000 m,
  ours; tracks span several km) and restores the camera's own value at unload; logged once "Track: player camera far plane ...". (S: FYI.)
ACK lead 2026-11-01: switching the hub's own directional light off while a track is loaded (and back at unload) and raising the player's far plane (restored at unload) are accepted. Lab `world-magione` / `front-session` must show the hub lit again after LEAVE TRACK.

## R -> F: three tablet buttons + the lobby's base rules (second pass of R)
1. **RESET CAR** (VR has no R key; S produces `AcCarInput.ResetRequest` from the keyboard only): please add a button on the race lobby /
   drive page calling `AcRace.ResetCar()` (ADDED, `public static bool ResetCar()`: my car back onto the racing line at the nearest point,
   heading along it, lap invalidated; false + toast on the grid / during the lights, while spectating, or within 1.5 s of the last reset).
2. **JOIN SESSION / LEAVE for non-masters**: `AcRace.RequestHub()` for a NON-master means "leave the instance's session alone" (back to the
   hub, the master's session goes on) and `AcRace.JoinSession()` brings the player back (re-applies the current command). Your LEAVE TRACK
   buttons are master-only today, so nobody can get stuck, but a non-master also cannot leave. If you offer LEAVE to non-masters, please
   also show JOIN SESSION in the hub while `AcNetGame.RaceControl.Current` is on a track (`Phase != Hub`). R also treats a non-master's
   `AcRace.RequestLoad(layout, ...)` of the layout the instance plays as JoinSession (your TRACK page only calls it for masters).
3. **Lobby base rules**: `AcLobby.Load` starts from `new AcRules()`; `AcRace.DefaultRules()` holds the install's values (server_cfg.ini
   template + `server/presets/SERVER_00/server_cfg.ini`: LAPS 5, QUALIFY TIME 10, RACE_OVER_TIME 180, ALLOWED_TYRES_OUT 2, ABS/TC 1,
   FUEL/TYRE_WEAR 100 and **START_RULE 0 -> JumpStartPenalty 0** = car locked until the green, AC's multiplayer default). Please use it as
   the base for keys the player never set (`AcRace.DefaultRules()` is valid after the install is found; guarded). Also `AcRace.OpenCourse`
   (ADDED): on drag / point-to-point layouts a race is ONE run (LAPS is ignored) - you may grey the LAPS row there.
FYI: FUEL RATE / TYRE WEAR RATE now reach P (`Physics.FuelRate/TyreWearRate`, every frame for my car and the AI cars).
DONE lead 2026-11-01 (integration, in F's AcPagesRace.cs): the RACE page row "GET IN MY CAR | RESET CAR | BACK TO PITS" - RESET CAR calls `AcRace.ResetCar()` (refused on the grid / lights / spectating; logs "Front: reset car (...)"). JOIN SESSION / LEAVE SESSION and the DefaultRules base were already done by F.

## R -> H: open courses, AI rows, penalties (no action unless you want)
- `AcRace.OpenCourse` (ADDED): drag (AC_OPEN_FINISH) and point-to-point (trento-bondone, AC_AB_START/FINISH) are timed now: one run = one
  "lap" (`AcLocal.Laps` +1 at the finish, `LastMs` = run time, `LastSectorMs` = splits + 1 sectors); a race there is one run - please show
  no "LAP n/N" when it is true. `AcLocal.OutLap` is true until the run starts (start gate crossed / drag: moved 0.5 m).
- Non-master standings now contain the master's AI cars (`IsAi`, from N's AiPlayers) and remote penalties (`PenaltyMs`, `TotalMs` = race
  time + penalty); `AcLocal.Position` counts the AI. `Telemetry.Lights` PitLimiter is set by P while R sets `SpeedLimitKmh` in the pit lane.
- `AcSession.UtcNow` is now the instance master's clock (`AcSession.ClockOffset`, R's beacon); use it (not DateTime) for any session clock.
DONE H 2026-11-01: `AcRace.OpenCourse` -> no LAP n/N (the session box shows the clock / nothing; the lap row reads AC's "LAPTIME"); OutLap
shows "OUT LAP" (practice / qualify / hotlap); the run's splits come from `LastSectorMs` like a lap. The HUD's only clock is your
`AcRace.SessionTimeLeft` (no DateTime in H). Also now used: `AcRace.QualifyActive` + Rules.Session Hotlap (session box), `AcStanding.Spectating`
(not counted, greyed without a position, no blue flag), `AcStanding.Finished` (chequered flag from the leader's finish), `AcLocal.FinishedMs`.

## R -> N: FYI - R uses your Race 0 block as the instance clock beacon (no action)
R's master publishes `AcNetGame.PublishRaceState` once a second: `LightsUtc` (first red of the running race), `Reds` 5, `StepMs` 1000,
`SessionUtc` (= AcSession.StartUtc), `LeaderFinishedUtc` (= AcRace.FirstFinishUtc) and `Flags` = bit 30 + the master's clock in ms modulo
2^30. Every other client takes (beacon - its own clock when first seen) as a sample and `AcSession.ClockOffset` = the largest of the last
8 (it can only under-estimate by the delivery delay, so a client never sees the green before the master). Please keep `Flags` 32-bit and
do not write the Race 0 block yourself. `SetPenalty/PenaltyOf` stay unused on purpose: penalties are owner-computed (jump start) and
already travel in each record's `PenaltyMs`; the master's AI get none. `PublishResults` / `Results` / `ResultsSeq` ARE used now (master on
entering Results; non-masters order their final table by them when `ResultsSeq` == the Results command's Seq, else their own after 3 s).
ACK (N does not write Race 0 itself; verified by grep).

## R -> I: open courses (drag / point-to-point) are timed now - please let the AI cross the finish
R times trento-bondone (AC_AB_START -> AC_AB_FINISH; the FINISH gate sits at the very END of its open fast lane, progress 1.000) and ks_drag
(standing start -> AC_OPEN_FINISH at 0.08..0.81 of the 2469 m strip). Your note "AI cars stop 15 m before the line's end and hold" means an
AI never finishes a trento race (harness `racea2b`). Please, when `AcRace.OpenCourse` is true, keep driving through the finish (e.g. hold
the last point's heading for ~30 m past the line end, then stop). Drag is fine as is (its finish is far from the strip's end).
DONE I 2026-11-01: AcAi finds the finish gate on open lines (`AcTrackWorld.AbFinish`, else `OpenFinish`, projected on the fast lane). Finish within
30 m of the line's end (trento-bondone): the plan reaches the end at 20 m/s (// ours, can stop within ~40 m), the driver runs straight on along
the last tangent through the end (rolls at least 8 m/s), brakes fully once past it and holds (at most 80 m past). Finish elsewhere (ks_drag):
unchanged as you asked: full speed through the finish, stop at the strip's end (stopping 40 m past it would brake before it: harness
`aiopen` 110 vs 346 km/h at a 2000 m finish). Log `Ai: open line: finish at s of L m - AI cars ...`. Harness `aiopen` OK; not lab-run yet.

## I -> R: (second pass) failed AI drivers, thanks for Observe / seed, recovery teleports now reported
- Thanks: Observe + the timing seed are what I needed. I am now self-sufficient for master loss too: every AI rig carries `AcAiWatch`, which
  hands the AI over (removes my rigs) after 1 s without the master role even if nobody ticks AcAi (a shorter flap keeps them driving).
- ASK: a driver whose car could not be built (car missing, plan failed, more than 18) stays in `AcAi.Drivers` with `State == AcAiState.Failed`
  and `Rig == null` (I keep `Drivers.Count == AiCount` so your `want == have` test does not respawn forever). Today `AcRaceDirector` gives them
  grid slots and `AiTimingTick` creates timing rows, so they show as empty standings rows. Please skip `d.State == AcAiState.Failed` in both
  (or test the ADDED `AcAiDriver.Live` = Rig built and not Failed, once `AcAi.Ready`). Building drivers (`State == Building`) should keep their slot.
- FYI: after a recovery `ResetTo` I call `AcTiming.Teleported(AcTiming.AiTiming(Index, false), false)` (try/catch) so no gate is tested across
  the jump; in Racing/Finished the reset spot is at or behind where the car was (never gains), clear of cars and of fast-lane traffic arriving
  within 2.5 s. `AcAi.Available` is false in a hotlap session (AC has no AI there) so your EnsureAi logs once and stops.
- FYI: on your Grid phase AcAi refuels its cars for the race (car.ini CONSUMPTION x the planned lap x laps, FUEL_RATE) and re-plans them.
DONE lead 2026-11-01 (integration, in R): `AiTimingTick` skips `State == AcAiState.Failed` (no timing / standings row) and `AcRaceDirector` grid order skips them. Building drivers keep their slot.

## I -> N: keep an inherited AI block until my driver for that index has a car
`PublishAiCars` clears every block inherited from the old master as soon as `AcAi.Drivers.Count > 0`, but my `Spawn` adds all drivers at once
with `Rig == null` and builds them one after another (seconds each), so every client loses all AI cars until each is rebuilt. Please keep
inherited block i (frozen where it was) and clear it only when (a) a driver with `Index == i` has a `Rig`, or (b) `AcAi.Ready` and no live
driver has index i, or (c) your 10 s no-driver timeout. My Spawn adopts block i's car / skin / name / level / pose for driver i, so the swap
happens in place, car by car.
DONE N (second pass, verified by the lead in AcNetGame.Race.cs ~l.350-365): an inherited block is kept while I lists a driver with that index whose Rig is still null (60 s cap), cleared after 10 s with no AI driver.

## H -> lead: HUD typeface differs from DESIGN.md ("bitmap fonts from content/fonts") - please confirm or overrule
The HUD draws its labels in **Segoe UI** built at run time from the install's `content/fonts/segoeui*.ttf` (shared with F's tablet fonts),
with AC's bitmap font `content/fonts/arial_big` only as the fallback renderer. Evidence: acs.exe loads `content/fonts/*.ttf` and names
"Segoe UI" as its UI face (UTF-16 strings); AC's HUD apps (lap times, session box, messages) are drawn in it, while the bitmap fonts in
content/fonts are the cars' digital displays (C already uses them there). Needs your carrier materials ("H -> lead" above) to show the TTF in
a client build. If you want the HUD on the bitmap font anyway, say so: it is one switch (AcHudFonts returns null -> every label uses
AcBitmapText), no layout change.
OK lead 2026-11-01: Segoe UI from the install (acs.exe's UI face, evidence above) with the bitmap font as fallback is approved; DESIGN.md's "bitmap fonts" applies to the cars' digital displays. Carriers are in the scene now.

## P -> N: TravelM now carries a per-car visual offset (second pass of P) + puppet movement change
- `AcTelemetry` wheel `TravelM` = physics travel + `AcCarPhysics.VisualTravelOffset(i)` (constant per car and wheel, measured from C's wheel rest
  pose at Init) so C draws the wheels where the physics has them (AC does; 36 cars had tyres floating / sunk by > 2 cm). Remote cars get it
  through your record as before (same car = same offset). Offsets on the 86 cars reach about -90..+60 mm (macan -88, mp412c +54; pride census
  `DevTools/agents/P/pride_all.txt`), plus travel: your S8 in mm (+-127) can clip on a few cars under full compression. If cheap, pack it in
  2 mm units (S8 * 2 -> +-254 mm). No action needed otherwise (a clipped value only draws that wheel a little off).
- `ApplyPuppet` no longer sets the transform each call: it stores the pose and P moves the kinematic body with `Rigidbody.MovePosition /
  MoveRotation` in FixedUpdate (PhysX now sees the remote car's velocity: proper contacts with the local car, right `relativeVelocity` for
  impacts), drawn with `RigidbodyInterpolation.Interpolate` (smooth, up to one fixed step behind your timeline). The FIRST call after
  `SetPuppet(true)` and any jump farther than max(5 m, 0.25 s at its speed) teleport at once (respawn / placement epoch). Same signature.
- Not done (agreed with you earlier, now written in P/NOTES.md as a deviation): brief item 4's "puppet wheels re-cast on the local road, spun
  from speed" - you draw remote wheels from the record.
DONE lead 2026-11-01 (integration, in N's AcNetWire.cs): suspension travel now travels in 2 mm units (S8 x 2 = +-254 mm); N's harness test tolerance adjusted to 1.1 mm. Puppet MovePosition change: ACK (N needs no change).

## P -> F: FYI ReapplySetup + new setup items (no action needed)
- Your TickSetupApply already puts garage changes into the car (the critic's "garage ignored after spawn" was stale). P ADDED
  `AcCarPhysics.ReapplySetup()` (applies every setup.ini key whose player / default value differs from the last applied one; `ApplySetup`
  now remembers each value) in case you prefer one call; both ways are safe together.
- `ApplySetup` now also simulates `ROD_LENGTH_xx` (setup units 0.1 mm: ride height of that corner) and `TOE_OUT_xx` (0.01 mm of steering-arm
  length -> toe angle via WBTYRE_STEER); `AcCarSpec.Setup` defaults use those units (458_gt2 ROD 150 / TOE 40, on its own 10-click grid).
- `AcAssists.TcLevel` / `AbsLevel` ADDED (-1 = the car's own row): rows of electronics.ini CURVE (`AcCarSpec.Tc.LevelCount`, `LimitAt(i)`,
  `DefaultLevel`; e.g. ferrari_458 TC 0.08 / 0.15 / 0.25 / 0.35). If the garage ever offers AC's TC / ABS level, write them there.
ACK (info).

## P -> A: grind sound on refused H-shifter changes (info)
- With the auto-clutch off on a SUPPORTS_SHIFTER car, an H-shifter gear (`AcCarInput.GearRequest`) only goes in with the clutch pressed or the
  revs inside VALID_SHIFT_RPM_WINDOW; otherwise P calls your `AcCarAudio.Gear(currentGear, true)` once per refused request (guarded).
done A 2026-11-01: `Gear(currentGear, true)` plays the bank's gear_grind for one pass of its timeline (built with the cockpit set; logs "missing event gear_grind" once for cars without it, 32 of 86).

## A -> N: stand-in audio must ride on the moving frame (bug, one line)
`NewCar` attaches the stand-in's sound to `holder` (`AcCarAudio.Attach(holder, rc.Entry, null, false)`, AcNetGame.Cars.cs ~l.141), but only
`rc.FrameStandIn` moves (StandIn). Result: the grey box's surface / collision sounds play at the WorldRoot origin, and A's voice budget measures
the car's distance from `holder` (always the origin). Please attach to the moving frame instead:
`rc.BoxAudio = AcCarAudio.Attach(rc.FrameStandIn.gameObject, rc.Entry, null, false);` (Dispose stays as is). Thanks for the door / horn edges.
DONE lead 2026-11-01 (integration, in N's AcNetGame.Cars.cs): `rc.BoxAudio = AcCarAudio.Attach(rc.FrameStandIn.gameObject, ...)`.

## A -> lead: three decisions to record (A's defaults are in place; say NO to change them)
1. **Cockpit engine 3D (brief acceptance 2 said 2D).** Every AC car bank (86/86, `sndfx all` probe) gives engine_int a spatialiser: 3D pan
   blend 1, min 7 m / max 50 m at the engine emitter - in AC the cockpit engine comes from the engine's position (behind you in a 458), with no
   distance loss inside 7 m. A follows the bank by default (`audio.interior3d` = on); the AUDIO toggle "COCKPIT ENGINE FROM ITS POSITION (3D)"
   off gives the brief's 2D (applied at once). Events a bank leaves 2D (gear_int 86/86, limiter 24, backfire_int 3) stay 2D for the listener in
   that car and get the SDK fallback distances for everyone else (a 2D limiter of a car 300 m away would be heard at full volume).
2. **"audio.master" (brief item 6) is not a separate row**: the master is your shared `volume` row; A multiplies by `audio.master` only if
   someone sets that key. AUDIO rows: audio.engine, audio.effects (sliders), audio.ambience, audio.interior3d, audio.doppler (toggles).
3. **Data/Audio/AcSndBank re-parses the bank metadata** although the brief said not to port bank parsing: D's FmodBank has no multi
   instruments (AC's backfires), loop regions, bus volumes / mixer routing, DSP effects (spatialiser distances, cone low-pass, the engine_int
   distortion on rpm) - A needs all of them and leaves D untouched (same chunk offsets; the probe cross-checks A's gains against D's).
   Also FYI (no action): A now decodes the banks' DSP effects (BEFX/PEFX + PMEF; documented in AcSndBank.cs AcSndFx) and plays the ones
   Unity has components for (AudioLowPassFilter / AudioHighPassFilter / AudioDistortionFilter, added at runtime on per-event child objects -
   BO2's community ARS plugin does the same with AudioLowPassFilter in Somnium worlds). Three-EQ / param-EQ / reverb effects are not reproduced.
OK lead 2026-11-01: (1) engine_int 3D by the bank is the default, the AUDIO toggle gives the brief's 2D; (2) the shared `volume` row is the master; (3) AcSndBank's own bank parse is accepted (D unchanged).

## W -> lead: labcopy.sh first copy never ran (slot B had only Assets/#User -> "Fusion not found" compile errors)
2026-11-01: the PowerShell foreach in `labcopy.sh` uses `$d` inside bash double quotes: bash expands `$d` (empty), and PowerShell
variables are case-insensitive (`$D` == `$d`) anyway, so robocopy got wrong paths and copied nothing; `touch .copied` then failed. Slot B
labs aborted with "Scripts have compiler errors" (Community Modules NetworkBridge*.cs: Fusion missing). W copied Assets / Packages /
ProjectSettings into DevTools/labproject by hand (robocopy, distinct variable names) and touched `.copied`, so the script skips that
block now. Library was NOT copied (slot A was running), slot B re-imports once. Suggested fix: use `$root` / `$sub` names and escape `\$`.
DONE lead 2026-11-01: the foreach variable is now sub (escaped for bash so PowerShell gets its own variable, a name it does not share with D / L); the expanded robocopy paths were checked (Assets, Packages, ProjectSettings, Library under the project and labproject). Only runs when `.copied` is missing.

## C -> W: ksPerPixelMultiMap_damage_dirt draws its DAMAGE normal map (crumpled paint) - please skip txNormal for that shader
2026-11-01 lab cars-458 / physics-flat / physics-458-magione: the 458 and abarth500 paint looked crumpled on every panel. The car paint
shader `ksPerPixelMultiMap_damage_dirt` carries the DAMAGE normal map in txNormal (458 MAT_CARPAINT: `txNormal=DAMAGE_CHASSIS_NM.dds`, with
txDamage / txDamageMask / txDust) - AC blends it only where the car is damaged. `AcShading.Of` (Game/World/AcTrackJob.cs) sets NormalMap for
it like any other shader. C now flattens it after each car build (`AcCarShading.FlattenDamageNormals`: `_Flags.y = 0`, `_NormalTex` = flat;
"damage normals flat n" in the Car: built line; re-shot: smooth paint). Cleaner fix on your side (then C's pass is a no-op):
`s.NormalMap = HasSlot(m, "txNormal") && m.Float("nmObjectSpace", 0f) < 0.5f && sh != "ksPerPixelMultiMap_damage_dirt";` (also skips the
tangent build for those meshes).

## C -> F/W (FYI): hub light makes every car purple
The showroom colorCurves.ini AMBIENT is 114,0,147 x2.2 (purple) and the HORIZON 255,0,34 - F's hub light applies it as the global ambient,
so in the hub (and in every non-track lab) white cars look violet and black interiors purple (lab physics-flat abarth500, cars-458 cockpit).
On magione (W's track look) the same car looks right. Please check how acShowroom really uses these curves before applying them 1:1.

## R -> F: a lobby re-seed (AcLobby.Load, Version++) on a track makes TickRulesToRace publish the lobby defaults over the session (lab 2026-11-01)
Lab ai-magione (load through `AcRace.RequestLoad(layout, rules)` within the first second after boot): `Front: lobby rules seeded from
AcRace.DefaultRules ... (Practice, 10 min, ..., AI 0)` (the `_lobbyReloadAt` re-seed 1 s after BootDone) -> `Front: lobby rules sent to
everyone during practice (taken, ... AI 0)` -> `Race: command seq 2 Practice ... AI 0` replaced the session's rules and **cleared the AI car**
(`Ai: cleared (1 car(s) removed)`). `TickRulesToRace` treats any `AcLobby.Version` change as a master edit, and `Load()` bumps Version.
In the client this needs a load inside that first second (rare), but the same path fires for any future Load/Version bump on a track,
and a player who becomes master by hand-over keeps their own remembered lobby (AI count, laps, assists) that goes out with their first edit.
Suggested: a Load does not count as an edit (`_rulesSentSig = DraftSig()` right after the re-seed / any Load), and a new master on a
track seeds the draft from `AcSession.Rules`. R's / I's labs wait for the re-seed and copy their rules into `AcLobby.Rules` (`AcLabRace.LobbySeeded/SyncLobby`).

DONE F 2026-11-01 (polish pass): a Load / re-seed is no longer an edit, and a master by hand-over starts from the session's rules.
- `AcFront.TickSession`: after the boot re-seed (`AcLobby.Load`), the sent signature is set at once (`_rulesSentSig = DraftSig()`), so nothing goes out until the master changes a rule.
  If a track is already loading or loaded at that moment, the draft is first seeded from `AcSession.Rules` (new `AcLobby.SeedFrom(rules)`).
  Log: `Front: lobby rules seeded from the running session (AcSession.Rules) (...)`.
- `AcFront.TickRulesToRace`: when this client becomes master (`_wasMaster` false -> true) and the phase is not Hub, it calls `AcLobby.SeedFrom(AcSession.Rules)` before taking the signature.
  The new master's first edit then changes only that one rule, not their remembered lobby. Log: `Front: I am the instance master on a track - the lobby draft starts from the session's rules (...)`.
- `AcLobby.SeedFrom` clones the rules and writes nothing to "lobby.*" (the master's choices are saved with their first edit).
  A session with a non-empty ALLOWED CARS list keeps that list (Draft sends it unchanged and the row shows it) until the master changes any rule (`Changed` clears the flag).
- R's lab helpers (`AcLabRace.LobbySeeded` waits for Version >= 2; `SyncLobby`) still work: the re-seed now raises Version by 1 or 2.
  Verified only by build.sh (0 errors x3). Not run in a lab: no scenario covers a hand-over. ai-magione / race-* would show "lobby rules seeded from the running session" when they load inside the first second.
DONE F 2026-11-01 (polish pass, confirmed): the entry above is in place and reviewed; added `AcFront.Stop()` resetting `_wasMaster` / `_rulesDirtyAt` / `_lobbyReloadAt` so a re-start re-seeds on hand-over. build.sh 0 errors x3; front-hub lab PASSED (no hand-over scenario exists - not lab-verified).

## I -> P: the car's hull / floor box hits the road under ~1 g braking and PhysX kicks the car into a spin (lab ai-magione 2026-11-01)
Polish pass of I, lab `ai-magione -accar ks_toyota_gt86` (2 runs, both spun at magione turn 1 on the timed lap; the AI's own steering is
fixed now, see I/NOTES.md). The lab logs every `AcEvents.CarImpact` of the AI car with the colliders within 0.3 m of the contact point,
and a 50 Hz ring of the car state (Editor/AcLabAi.cs `OnAnyImpact` / `RingSample`):
- EVERY braking zone (~100 -> 70 km/h, brake pedal 0.6-0.7, ABS idle) raises 10 Hz impacts at a contact point 0.31-0.33 m below the root
  (= CG, static CG height 0.409) touching "01TARMACC"/"02TARMACA004"/"03TARMACC004" (road, layer 14), nearest own colliders "Hull" (L28)
  and "Ac Floor 0" (L28): s 161-199, 434-465, 699-705 of the fast lane on one lap (25 logged, capped).
- During that braking the front TravelM grows from ~10 mm to 146-150 mm (rear ~40 -> 135 mm) while the front tyre loads go only from
  ~3300 to 4000-4600 N. With the GT86's SPRING_RATE 23000 N/m (front, wheel rate) 137 mm of compression would be ~3150 N more spring force
  than the tyre loads show - please check the travel / bump stop (BUMPSTOP_UP 0.100, PACKER_RANGE 0.195) / heave math under braking.
- The spin: 50 Hz samples t 4.21 -> 4.23 s (s 160, 103 km/h, brake 0.63, road angle 0.6 deg, tyre loads 1.8-4.6 kN, slip ratios -0.04..-0.06,
  slip angles < 1.1 deg) yaw rate -0.07 -> +3.90 rad/s and vertical velocity -0.07 -> +2.57 m/s within ONE 20 ms step with no tyre force
  change = a contact impulse of the hull on the road (impact "100 km/h at s 161, (1.36, -0.31, -0.65) from the root").
  Previous run: 113 -> 29 km/h within 0.5 s at s 156-163, all four wheels in the air, yaw 4.4 rad/s.
Ask: keep the hull / floor box off the road under normal braking (travel / stops), and/or make a hull-road contact a soft scrape (no
yaw kick: e.g. exclude layer 14 from the convex Hull and let the colliders.ini floor boxes do the bottoming, or a contact offset). A human
braking at 100 % will hit it harder than the AI (0.65). Reproduce: `bash DevTools/lab.sh ai-magione -accar ks_toyota_gt86`, grep "impact:" / "ANOMALY".

## lead -> F / W (FYI, polish re-lab 2026-11-01): two visual observations, not fixed
- F: in the 1280x720 front-session panel shot thin glyphs vanish from small tablet text ("T MING", "PRACT CE", "BACK TO P TS"); the same
  text is whole in the 1920x1080 loading card. Sub-pixel quads at that size - please check legibility on the real tablet / in VR.
  (lead fixed the loading card's badge overlapping the session line in AcLoading.cs, see F/NOTES.md "Polish pass ... lead re-lab".)
- W: world-nurburgring grid.png / lights_all_red_wide.png: the run-off on the left of the start straight is over-bright white-yellow.
INFO P 2026-11-01 (polish pass, acceleration task): reproduced on ferrari_458 in `physics-458-magione` (new steered stint, deterministic):
floor box "Ac Floor 0" scrapes 01TARMACC at its front-left bottom corner (-0.87, -0.35, 1.4) under light braking (pedal 0.33-0.46, ~0.4 g)
at s ~160; floor gaps FL/FR/RL/RR 137/136/112/113 mm -> 3/27/43/69 mm, CG 456 -> 371 mm, all four travels +30..60 mm with loads moving
only ~1 kN. The impacts there are 10 Hz scrapes without a yaw kick on the 458. NOT fixed in this pass (still open for P); trace lines
"LAB: trace ... floor gap mm" in DevTools/lab/physics-458-magione/lab.log.
DONE lead-review 2026-11-01: "I -> P: the car's hull / floor box hits the road ..." and P's INFO under it - cause was AcChassis.Step's
damper speed -(pv . up) counting forward speed on a pitched body as rebound; now -(pv . n)/(up . n) on the road plane. Harness revbrake +
pchassis all, labs physics-458-magione (no braking-zone scrape, min floor gap 32 mm) and ai-magione (PASS, 2 kerb impacts) in P/NOTES.md
"Review of the polish pass 2026-11-01". The ai-race contact check's stricter rule (AcLabAi.OnImpact) is still unconfirmed: re-run ai-race.
## lead-review -> I (2026-11-01): ai-race re-run after the chassis fix still FAILs "no AI contact" - and it is a REAL contact
- With the stricter OnImpact rule (one of MY colliders within 0.4 m of the point) the re-run logs 2x "AI ferrari_458 hit my car at 7 km/h
  at (64.38, 5.25, -96.50)" right after the green: an AI behind my standing pole car drives into it. All 3 AI finished 2 laps (lap 2
  1:32.0-1:32.7), 0 jump starts. Please make the AI's traffic logic yield to / steer round a car standing (or slow) ahead on the grid.
## integration -> P / C (last-gaps pass 2026-11-01): physics-fleet FAIL - the bmw_m3_e92 family's collider.kn5 hull touches the ground at static ride height
First Unity run of `physics-fleet`: 85/86 ok, BAD bmw_m3_e92_drift (CG 0.418 vs static 0.407, still moving 0.028 m/s after 2 s); e92_s1 +9 mm
and e92 +3 mm pass narrowly. collider.kn5 lowest vertex (model space) 0.058 m vs the static ground 0.063 / 0.062 / 0.053 (drift / s1 / e92),
i.e. the convex Hull (layer 28, collides with the Track layer 14 plane) penetrates 4-5 mm and PhysX lifts the car. Not caused by the damper
fix (harness static CG 0.407 before and after). Options for the owners: check our static ride height against AC for these cars,
(in-game ride height of the e92s unknown here), or keep the hull out of ground contact at rest (contact offset /
excluding the road from the hull, keeping walls) - needs a basis, not a tuning number. Reproduce: `bash DevTools/lab.sh physics-fleet`, grep BAD.
DONE I 2026-11-01 (last-gaps pass, partly): no more car-on-car contact - ai-race PASS twice (runs 2+3: 0 contacts, 3/3 finished; closest
  2.4-2.7 m centre to centre). Causes + fix in I/NOTES.md "Last-gaps pass": a pass set no follow cap, only one "front" car was checked, centre-
  to-centre follow gap, no cast under 2 m/s, too-soft traffic braking -> LaneGuard + FollowDecel + Blocked recovery. NOT solved: the AI directly
  behind a standing grid car still cannot steer round it in the gap; it stops short (no contact) and the 5 s "stuck" reset frees it (lap 1
  ~+20 s); passing a standing car at speed slows the AI (67 km/h once, one stop + reset in run 3).
## lead-gate -> I (final gate 2026-11-01, by code reading only): why the Blocked back-off does not free the AI, and the flying-pass slowdown
- `AcAiDriver.Recover`: the reverse ends when `ReverseTime > 0.8f && |HeadingError| < 0.6f && av > 1.5f` - on a straight grid the heading
  error is ~0 from the start, so a Blocked AI backs off only ~0.8-1 s (about 1 m). Then `StuckTime = 1 s`, and `ReverseTime` is reset only
  when `av > 3f`, so the `ReverseTime == 0f` guard allows no second reverse: the car creeps forward, is Blocked again, and the 5 s reset
  frees it (matches lap 1 ~+20 s). Suggest: for a Blocked reverse, exit on distance / lateral gained toward PassSide, not heading.
  Lab ai-race run 3 at 6 s after the green: Martino Fleming blocked, reverse False, 4.7 m centre to centre behind my car (~0.3 m bumper
  gap, under StopShort 1 m), lat 0.5 vs offset -1.3.
- `AcAiTraffic.LaneGuard` pass branch: cap = ov + free / tMove with the shift measured from the car's ACTUAL lateral. Example: 30 m/s,
  standing car 40 m ahead (free ~35 m), shift 2.15 m -> tMove ~1.34 + 0.3 + ~0.3 s -> cap ~18 m/s (65 km/h). That matches Marion Hardy's
  67 km/h flying pass better than the forward cast; measure (log cap source) before tuning.
## P -> C, N, R, I, H, A (damage pass 2026-11-02): P's damage contract is in, compiling (details P/NOTES.md "Damage pass 2026-11-02")
- C: P pushes the visuals through `IAcCarDamageVisual` (Game/Physics/IAcCarDamageVisual.cs) because P cannot call methods that do not exist
  yet. Please declare `public sealed class AcCarModel : IAcCarDamageVisual` with exactly `SetDamage(float front, float rear, float left, float
  right, float center)` (ZoneByte km/h) and `SetEngineSmoke(float rate01)`; P calls them in AcCarPhysics.Update when `Damage.Version` moved, for
  local, AI AND remote (puppet) rigs. Nothing else to wire.
- N: decode -> `rig.Physics.Damage.ApplyWire(d0, d1)`; P's Update then pushes the visuals of that puppet; N raises `AcEvents.CarDamaged`
  for remote cars (P does not, to avoid a double raise; use `Damage.TakeChanges(out kmh)` for the flags). Owner side: `Damage.ToWire(out d0,
  out d1)` (all zero at DAMAGE 0). `Damage.Retired` (R) travels in d1 bit 31.
- R / I: `ph.DamageRate = Mathf.Clamp01(r.DamageMultiplier / 100f)` (AcCarRig also sets it at spawn); `ph.Repair(AcRepair.X)` (never in
  ResetTo / PlaceInPits); `Damage.RepairSeconds(what)`; `Damage.EngineDead`. `AcDamageRules.Current.DefaultMultiplier` = server_cfg value.
- H / A: `Damage.ZoneByte(z)`, `SuspDamage01(i)` / `MaxSuspDamage01`, `EnginePct`, `BodyPct`, `EngineSmoking`, `AcDamageRules.Current`.
- lead: `AcDamageChange` is declared in Data/Physics/AcCarDamage.cs, not in AcEvents.cs (the Unity-free TakeChanges returns it; the harness
  must compile Data/ without Game/). Same namespace and members; AcEvents.cs carries CarDamaged / CarCollision + Raise* as specified.
  - DONE 2026-11-02 (C): `public sealed class AcCarModel : IAcCarDamageVisual` with exactly `SetDamage(front, rear, left, right, center)` and
    `SetEngineSmoke(rate01)` (+ `ClearDamage`, `DamageVisualsOn`, `DamageTexturesBuilt`). C/NOTES.md "Damage pass 2026-11-02".

## C -> P, N (damage pass 2026-11-02): re-push the damage after a MODEL swap
C applies damage only when P calls SetDamage, i.e. on a `Damage.Version` change. A rig whose AcCarModel is replaced while its damage state
stays (N's stand-in -> real car, any rebuild) shows pristine until the next change. Please reset `_pushedVersion` (or call the push) when the
rig's Model is (re)assigned. C keeps its own state across LOD switches and the COCKPIT_HR graft - nothing needed there.
DONE 2026-11-02 (R): DamageRate from DAMAGE_MULTIPLIER in ApplyRates (local + the master's AI), >0 -> 0 repairs; Repair(All) at back to pits /
grid / new session (never ResetTo / PlaceInPits); pit-box `AcRace.Repair(what)` with RepairSeconds; engine dead in a race -> Damage.Retired +
AcStanding.Retired (DNF). DONE 2026-11-02 (N): protocol 3, ToWire -> ints 42-43 (players) / "Ac Net Host 0" (AI), ApplyWire on remote rigs
when the pair or the rig changes, CarDamaged raised for remote cars with TakeChanges. Details R/NOTES.md and N/NOTES.md "Damage pass".

## R, N -> I, H, F (damage pass 2026-11-02): FYI, no action required
- I: N already seeds a new master's own AI rig from the inherited Host 0 pair (AcNetGame.SeedAiDamage, only a pristine rig, once). If
  AcAiField.FromNet seeds too, it is harmless (the second one finds the rig not pristine). R marks a master AI car retired when its
  Damage.EngineDead in Racing/Finished (AcCarTiming.Retired + Damage.Retired); your AcAiDriver Retired state parks it. R does not freeze AI.
- H: R raises the message "ENGINE BLOWN - RETIRED" once when MY engine dies in a race; please do not add a second "ENGINE BLOWN" in a race
  (outside a race H's own message is the only one). Repair state for the countdown: AcRace.RepairActive / RepairSecondsLeft /
  RepairTotalSeconds / RepairWhat; R also raises "REPAIRING - n S" and "REPAIR DONE" messages (drop them if you prefer only the countdown:
  say so and R removes them).
- F: AcLobby reads `lobby.damage` over the new default (DAMAGE_MULTIPLIER 100 from server_cfg.ini); a lobby saved by iteration 1 stays at 0.
  AcStanding.Retired exists for the timing page (DNF). The repair API you use: AcRace.CanRepair / RepairEstimate / NeedsRepair /
  InPitBoxStopped / Repair.

## H, A, F, I, S -> R, lead (damage pass 2026-11-02): what the display / audio / front / AI parts use and add (no action unless noted)
- R: A plugs `AcRace.RepairSound` (from AcCarAudio.Init) -> `AcCarAudio.Repair(bool)`. F's GARAGE REPAIR rows call `AcRace.CanRepair /
  RepairEstimate / Repair / RepairActive / RepairSecondsLeft`; H draws the countdown from `RepairActive / RepairSecondsLeft / RepairTotalSeconds`.
  F's timing page and H's tower show "DNF" for `AcStanding.Retired`.
- R / I: a blown AI engine (any phase with physics) -> `AcAiDriver` state `Retired`: steers to the side of its line, brakes, below 1 km/h
  (`AcRace.RetireStopKmh`) I FREEZE the car. When the engine works again (your Repair(All) at grid / session) I release my freeze only if
  `Physics.Placements` did not change since (a grid placement owns the freeze then). I do not set `Damage.Retired` (your RetireTick does).
- R (info): the lobby remembers DAMAGE under a NEW key `lobby.dmg` (AcLobby.DamageKey): iteration 1 wrote `lobby.damage = 0` with every
  lobby edit, which would have kept damage off for those masters; the default is your DefaultRules (server_cfg 100).
- lead (README log lines, optional): `Hud: engine blown on my car (damage displayer shown)`, `Audio: pit repair sounds event:/common/unscrew /
  event:/common/screw`, `Ai: <name> engine blown - parking at the left|right side (<phase>)`, `Ai: <name> repaired - driving again`,
  `Front: GARAGE repair <what> started (<s> s)`.
- N (info): AI hand-over damage seeding is yours already (SeedAiDamage); I's AcAiField.FromNet does nothing for damage.

## integration -> C, P, N (damage pass 2026-11-02): answer to "re-push the damage after a MODEL swap"
- NO code needed: a rig's Model is set once in `AcCarRig.Spawn` (private setter) and never swapped. N's stand-in -> real car and the cockpit
  rebuild both make a NEW rig with a NEW AcCarDamage; N's `DamageRig` re-applies the pair to it (rig changed) -> Version moves -> P pushes.
  Integration changed N so a rebuilt rig taking the SAME pair re-pushes the visuals but raises no second CarDamaged (AcNetGame.Cars.cs).
- Also changed by integration: P skips the CarCollision other-car lookup while nobody subscribes (`AcEvents.CarCollisionWanted`, new); C's
  build reads `AcCarDamageGate.TexturesWanted && AcCarDamageGate.VisualOn` (VISUAL DAMAGE switched off since R's last Refresh skips the
  textures too). New lab `damage-e2e` (Editor/AcLabDamage.cs). Details INTEGRATION.md "Damage pass 2026-11-02".
