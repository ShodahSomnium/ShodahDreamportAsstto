# Assetto Corsa Dreamport - integration (lead, 2026-11-01, after the second pass of all ten modules)

Paths are relative to `Assets/#User/SomniumAssetto/`. "Verified" means checked offline (compiler, harness on the real install, grep or
reading). **Nothing below has run in Unity or the Somnium client yet.** The lab phase comes next. No lab picture has been looked at.

## Build state (integration pass)
- `DevTools/build/build.sh`: runtime 0 errors (2 warnings: W's obsolete `Physics.BakeMesh(int,bool)`, SceneNetworking), runtime_server
  0 errors, editortool 0 errors. Span check: only SceneNetworking's Fusion 2.1 callback.
- `DevTools/fusionapi/check.sh .../server_like.dll`: "all 117 Fusion/Somnium members are proven".
- `metas.sh`: nothing missing (2 new metas this pass: Resources/AcTmpMobileSdf.mat, AcTmpSdf.mat). Every file and folder under the code tree has
  a meta. There are no duplicate guids under Assets/.
- `harness-ac` (Data/ is free of Unity) builds with 0 errors. N's `nettest` passes 45 ok, 0 failed (re-run after the wire change below).
- Grep for `stub|TODO|not implemented|NotImplementedException|FIXME` in Game/, Data/ and Core/ finds no code stubs. The only hits are "DLC stub" (AC's
  own word for cars and tracks you do not own) and comments.

## What the lead changed in this pass
| Change | File | Why |
|---|---|---|
| AI records build with `AcCarModelOptions.ForKind(Ai)` (the interior rebuild too); Kind stays Remote | Game/Net/AcNetGame.Cars.cs `Build`, `InteriorOptions(ai)` | C -> N: AI cars had no driver on non-masters |
| `m.SetLapTimes(p.LapMs, p.LastMs, p.BestMs)` after `ApplyTelemetry` | AcNetGame.Cars.cs `ModelRig` | C -> N: remote dashes showed -:--.--- |
| Stand-in sound attached to `rc.FrameStandIn` | AcNetGame.Cars.cs `NewCar` | A -> N: sound came from the track origin |
| Suspension travel sent in 2 mm units (+-254 mm) | Game/Net/Core/AcNetWire.cs (pack + unpack), N harness tolerance | P -> N: P's visual offset is up to -90..+60 mm |
| Failed AI drivers get no timing row and no grid slot | Game/Race/AcRace.Flow.cs `AiTimingTick`, AcRaceDirector.cs grid order | I -> R |
| Drag / A2B tree DISQUALIFIED lamps on my jump start, cleared at the next race | AcRace.Flow.cs `JumpWatch`, `EnterRace` | W -> R |
| RESET CAR button (VR has no R key) | Game/Front/AcPagesRace.cs (row GET IN / RESET CAR / BACK TO PITS, `ResetCarPressed`) | R -> F |
| Client 2DPlayer "Sprint" / "Jump" and UI "Navigate" also switched off while seated on a monitor | Game/Vr/AcFirstPerson.cs | S -> lead |
| TMP SDF carrier materials added to "Ac Game" `_shaderCarriers` (now 5) | Resources/AcTmp*.mat, Assets/Assetto Scene.unity | H -> lead |
| Every open REQUESTS.md item answered (DONE / OK / ACK / NO with the reason) | DevTools/agents/REQUESTS.md | |

Decisions recorded in REQUESTS.md:
- **Session time and weather stay per player** in iteration 1 (W's SetSessionLook is ready, but the command does not carry them).
- QUALIFY_MAX_WAIT_PERC gets no lobby row.
- Approved:
  - F's two public client reflection names (proven in ETS);
  - H's Segoe UI HUD;
  - A's 3D cockpit engine by default, `volume` as the master, and A's own bank parse;
  - W's switching off the scene light and raising the far plane at run time.

## The end-to-end flow (file : method per step)
1. **Gate.** `Game/AcGame.cs` runs the frozen whitelist check first. A visitor gets only `AcIsolationDriver`.
   The hooks `FrontStart` (-> `AcFront.Start`) and `NetStart` (-> `AcNetGame.Start`, `AddOnce<AcNetDriver>` and `AcNetEarlyDriver`) run only after the gate.
2. **Boot.** `AcGame.Boot` (worker thread) calls `AcInstall.Find` and then `AcContent.Scan`. `AcGame.FinishBoot` calls `FrontBootDone` -> `AcFront.BootDone`, which:
   - calls `AcSfx.Preload` and `AcTrackWorld.RegisterSettings`, so the AUDIO and TRACK rows exist;
   - builds the hub;
   - starts the tablet link.
3. **Every frame,** `AcGame.Update` calls `NetTick` -> `AcNetGame.Tick`, `FrontTick` -> `AcFront.Tick`, and `RaceTick` -> `AcRace.Tick`, in that order, each guarded. On R's first tick, `AcRace.StartModules` starts `AcSeat` and `AcHud`.
4. **Hub and tablet.**
   - Front/AcHub.cs builds the showroom kn5, takes its light from colorCurves.ini, and builds the showroom car with `AcHub.SetCar` -> `AcCarRig.Spawn(Kind Showroom)`.
   - `AcFront.TickKeep` puts a player who leaves the showroom back in it (Hub phase only).
   - Front/AcTabletLink.cs (order 1000) adds the "ASSETTO CORSA" app to the client's tablet, and AcTabletApp.cs (order 1001) draws its pages.
   - With no tablet on a track, the menu panel stands in front of the player (`AcPanelPlacer`).
5. **Drive page.** Front/AcPagesDrive.cs `Choose` -> `AcFront.ChooseCar` -> `AcRace.ChooseCar(car, skin)`. When R refuses, false comes back and nothing is kept. On a track, R respawns the car (`RespawnLocalCar`).
6. **Track page / lobby.**
   - AcPagesDrive.cs `Load` -> `AcFront.RequestLoad` -> `AcRace.RequestLoad(layout, rules)`. Rules come from `AcLobby.Rules`, based on `AcRace.DefaultRules()`.
   - The master publishes through `AcRace.Publish` -> `AcNetGame.RaceControl.Publish`. Every client gets it from `RaceControl.Changed` -> `AcRace.OnRaceCommand` -> `AcRace.Apply` -> `StartLoad`.
   - A non-master picking the instance's layout counts as `JoinSession`.
7. **Loading.**
   - `AcRace.StartLoad` calls `AcSession.Begin` and `SetPhase(Loading)`. Then `LoadRoutine` runs `AcTrackWorld.Load(layout, OnTrackProgress)` -> `AcSession.ReportLoading`, and F's `AcFront.OnTrackLoading` -> `AcLoading.Progress` updates the card. `AcFront.OnPhase` shows and hides the card.
   - Failure paths: W's `Fail()`, then R's `LoadFailed` -> `Teardown(toHub)`. An exception after the build fails the load cleanly (the card is never stuck).
8. **Track built.**
   - `AcRace.AfterTrackBuilt` calls `AcSession.NotifyTrackBuilt` and `AcSpawner.ReadLayout`.
   - It then calls `AcTiming.SetupTrack` (gates, sectors, open course) and `PickPitBox`.
   - `TrackBuilt` subscribers: N's `OnTrackBuilt` (starts the remote cars), and A's `AcAudioBudget` (track surface sounds).
9. **Own car.**
   - `AcRace.SpawnRoutine` -> `AcCarRig.Spawn(Kind Local)` builds:
     - the spec from `AcCarSpec.Load` on a worker;
     - C's `AcCarModel.BuildAsync` (through W's `AcKn5Builder`);
     - P's `AcCarPhysics.Init`;
     - A's `AcCarAudio.Attach`.
   - Then `PlaceForSession` (pit box, ResetTo), `AcEvents.RaiseLocalCarChanged`, and `AcSpawner.PlayerBeside` (teleports the player next to the car).
   - Finally `EnterCommandPhase` -> `EnterPractice`.
10. **Door and seat.**
    - Seat/AcDoors.cs `Use` (the door box on layer 16, monitor E / VR ray) -> `AcSeat.Enter(rig, Driver)`; the tablet's GET IN MY CAR is `AcPagesRace.GetIn`.
    - `AcSeat.Enter` locks the Bridge pose, seats the body, calls P's `SetOccupied` / `SetDriven`, C's `SetViewer`, and A's interior (through the camera), then raises `AcEvents.SeatChanged`.
    - `AcHud.OnSeatChanged` -> `AcHud.Show`. `AcCarAudio.OnSeatChanged` switches to the interior sound.
11. **Driving.**
    - `AcSeatEarly` (Update -90) -> `AcSeat.Early` -> `ProduceInput` -> `AcDriveInput.Produce(rig)` -> `rig.Physics.Input`. VR wheel grab and monitor cameras run in `AcSeatLate` (9000).
    - `AcCarPhysics.FixedUpdate` runs the sub-stepped chassis.
    - `AcCarPhysics.Update` pushes the telemetry to C's model, `AcCarAudio.Feed` and `AcLocal.Telemetry`. C's `AcCarModelDriver.LateUpdate` writes the needles and displays once per frame.
    - R's `PracticeTick` / `AcTiming.Step` writes `AcLocal` laps and sectors.
    - `AcHudDriver` (LateUpdate 10010) -> `AcHud.Late` draws the HUD.
12. **Net.**
    - Own car: `AcNetGame.Tick` -> `ComposeOwn` / `FillCar` writes my record (pose, controls, travel, doors, seat, laps).
    - Remote cars: `AcNetGame.Early` (Update -95) -> `DrawCars`:
      - `SyncCar` -> `NewCar` (a grey stand-in until built), then `Build` -> `AcCarRig.Spawn(Kind Remote)`;
      - `PoseRig` -> P's `ApplyPuppet` (MovePosition in FixedUpdate, interpolated);
      - `ModelRig` -> C's `ApplyTelemetry` and `SetLapTimes`;
      - `SoundRig` -> A's `Feed`, door and horn.
    - Passengers: S's `AcRiders` (Update -90 and LateUpdate 9000) places riders on the cars posed at -95. Swapping in the cockpit model goes through `AcNetGame.SwapSeatedCar` -> `AcSeat.SwapSeated`.
13. **Race start (master).**
    - AcPagesRace.cs `StartRace` (press twice) -> `AcFront.RequestStart` -> `AcRace.RequestStart(rules)`. The grid order comes from `AcRaceDirector`, then `Publish(Grid)`.
    - Every client: `EnterRace` -> `GridPlace` and `SeatDriver` (`AcSeat.Enter`; `CanExit` refuses during Grid, Countdown and Racing).
    - `RaceTick` steps the lights on the instance clock (`AcRaceClock`, R's Race 0 beacon):
      - `SetLights` -> `AcTrackWorld.StartLights.SetLights` and `AcHud.StartLights`;
      - `AcSfx.StartBeep` for each red.
    - `JumpWatch` handles jump starts ("FALSE START!", the tree's DQ lamps).
    - `Green` -> `AcSfx.GoBeep`, releases the cars, starts the timers, and calls `NotifyRaceStarted`.
14. **Laps and positions.**
    - `AcTiming.Step` / `LapEvent` / `Sectors` / `WheelsOff` (lap validity from IS_VALID_TRACK and ALLOWED_TYRES_OUT) / `Pit` (pit lane -> P `SpeedLimitKmh`).
    - Remote laps come from the records (`RemoteLapsStep`), AI laps from `AiTimingTick`.
    - `AcRace.UpdateStandings` fills positions by laps and fast_lane progress.
15. **Finish and results.**
    - `LocalFinished` / `RecordFinished`. In a timed race the flag shows from the leader's finish.
    - `EnterResults`: the master calls `AcNetGame.PublishResults`, and non-masters order the table by it (or by their own order after 3 s).
    - `FinalizeResults` -> `NotifyRaceFinished`. `AcTabletApp` switches to the TIMING page on Results.
    - `AcRaceDirector` returns to Practice after `ResultScreenSeconds` (30, ours).
16. **Exit car.** `AcSeat.Exit` (F / E, the tablet's GET OUT, or the door) is refused while the car moves or the race holds the driver. It then calls `StandBeside` -> `StandAt` (`AcPlayer.Teleport`), parks the car, and raises SeatChanged -> `AcHud.Hide`.
17. **Leave track.** `AcFront.LeaveOrJoin` -> `AcRace.RequestHub` (master: everyone; others: themselves only) -> `Teardown` -> `AcTrackWorld.Unload`, which:
    - restores the scene light and the far plane;
    - fires `AcSession.Unloading` (N drops remote cars, S releases the seat, A clears surfaces, I clears AI);
    - then `GoHub` teleports the player and the hub returns with its showroom car.
18. **AI.**
    - Master: `AcRace.AiTick` -> `EnsureAi` -> `AcAi.Spawn(count, rules)` (Ai/AcAiField.cs `BuildAll` -> `AcCarRig.Spawn(Kind Ai)`), then `AcAi.Tick` each frame and `AiGridTick` at the start.
    - Non-master: `AcAi.Observe()`. N publishes the AI blocks, and non-masters draw them as remote cars (AI model options now).
19. **Master hand-over.**
    - N's master rule (copied verbatim from Halo) decides who is master.
    - The new master re-publishes the newest command (Seq above any seen); AI timing carries over through R's `RememberRemoteAi` / `SeedAiTimer`.
    - I's `AcAiWatch` drops this client's AI cars 1 s after it loses the master role. N keeps inherited AI blocks until the new drivers have cars (10 s with no AI driver at most).

## Frame order (checked by reading; no conflict found)
| Order | Who | What |
|---|---|---|
| FixedUpdate | P `AcCarPhysics` | local / AI chassis sub-steps; remote puppets moved with MovePosition (interpolated) |
| Update -95 | N `AcNetEarlyDriver` | remote cars posed (puppet target, model, sound) |
| Update -90 | S `AcSeatEarly` | riders placed on the cars posed at -95; early seat write; `AcDriveInput.Produce` (the physics reads it next FixedUpdate: one frame, as in Halo / ETS) |
| Update 0 | AcGame (Net, Front, Race ticks), P `AcCarPhysics.Update` (telemetry -> model / audio / AcLocal), W `AcLodDriver`, A budget, I `AcAiWatch` | order among these does not matter (no one reads another's same-frame write) |
| Update 9990 | S `AcSeatCarry.Update` | remembers the monitor avatar after the client placed it |
| LateUpdate 0 | C `AcCarModelDriver`, A `AcCarAudio` | instruments written once, after every producer |
| LateUpdate 1000 / 1001 | N `AcNetDriver` (empty now), F `AcTabletLink` / `AcTabletApp` | |
| LateUpdate 9000 | S `AcSeatLate` | seat, VR head (heading only), wheel grab, monitor camera, riders again. It reads the interpolated car transform |
| LateUpdate 9990 | S `AcSeatCarry` (monitor avatar carry), lead `AcIsolationDriver` | independent (one moves the avatar root, the other hides renderers) |
| LateUpdate 9997 | S `AcSeatPosesDriver` | hands on the wheel, own head hidden (after the carry, before VRIK 9998) |
| LateUpdate 10000 | S `AcSeatFinal`, AcGear | the seat is re-written only if something moved the body |
| LateUpdate 10010 | H `AcHudDriver` | HUD after the final seat carry (same-frame numbers and panel) |

Remaining order risks (lab / client):
- The VR steering wheel angle comes from the grab at 9000 of the previous frame, so the input is one frame late.
- S's seat writes at 9000 assume the client's camera brain runs at 100. This is unverified in the current client build.

## Verified offline (by module, from the module reports and checked where cheap)
- **W:** worker load on 35/35 layouts. The start-light lamp 0 sits ahead of the pole and faces it on 8 tracks, including the ROTATION -17 and 180 tracks. The tree lamps are found on ks_drag and trento.
- **C:** driver pose with 0 mm error on 84 cars; 392 digital items with 26 textures, none missing; 86/86 car data loads.
- **P:** no drift; 81/86 cars recover from a yaw gust; the 458 does 0-100 in 4.18 s, 100-0 in 35.5 m, 1.07 g; the settled CG equals the static height on 86/86 cars. The fleet's 0-100 is 1.28x the ui times.
- **S:** door frame counts and the steer lock checked in the harness.
- **A:** 86/86 banks parse; 458 WOT is +20 dB(A) over idle in the offline mix.
- **F:** launcher ranges, server_cfg defaults, 65/86 cars with a tyre compound choice.
- **R:**
  - `racedist`: 661/661 grid slots;
  - `racea2b`: 6/6 open courses;
  - `raceseed`: 28/28 layouts;
  - `racerules`: OK.
- **N:** `nettest` 45/45; 40 names.txt names survive the round trip.
- **H:** `vrhud` on 86 cars, the `sightself` synthetic test, 29/29 HUD art images, acs.exe strings.
- **I:** 112/112 simulated runs finish; median +5.7 % vs the Kunos gold times; pit exits on 29/29 layouts; open courses.

## NOT verified (everything at run time)
- **Rendering:** shaders, materials, sky, shadows, the HUD (TMP fonts in a client build: carriers just added), the tablet pages, the hub light (the purple ambient from the game's file).
- **Physics on W's colliders:** ride height (bodies sit about 29 mm under GRAPHICS_OFFSET), wheel placement on C's nodes, remote-car contacts.
- **Seat:** Bridge pose, VR wheel grab, door boxes, AcFirstPerson keys (client only; the editor skips it).
- **Audio** through Unity filters (the order of distortion and volume).
- **Net:** everything that needs two clients (clock beacon sign, results agreement, AI hand-over, cockpit swap, ghost rule).
- **Load:** frame times and memory after unload.
- **The lead's own changes in this pass:** AI options on remote AI cars, remote dash lap times, stand-in sound position, 2 mm travel, failed-AI skip, tree DQ lamps, RESET CAR button, Sprint / Jump / Navigate hold, TMP carriers.
- **Open gaps** (by design or deferred):
  - not simulated: thermal tyres, damage, fuel-tank position, Ackermann;
  - the 458's 0-100 is 4.2 s vs AC's about 3.4 s;
  - not built: yellow flags, cone / DRS-board physics, crowds, reverb;
  - each client draws its own time and weather;
  - KERS / FUEL_CONS dash items are not driven;
  - wipers stay parked (no input for them).

## Lab scenarios registered (`bash DevTools/lab.sh <name> [-actrack id -accar id]`)
Suggested order:
1. `boot`
2. `world-magione`
3. `cars-458`
4. `physics-flat`
5. `seat-enter`
6. `audio-458`
7. `hud-desk`
8. `front-tablet`
9. `race-solo-magione`
10. `race-solo-actions`
11. `ai-magione`
12. `net-loopback`
13. the rest

| Scenario | Checks |
|---|---|
| `boot` (AcLab.cs) | install found, catalog scanned, hub up |
| `world-magione` | marker counts (18 grid / 18 pits / 2 gates / 4 lamps); a tagged surface under every grid slot and pit box; lamp 0 ahead of the pole and facing it; worst load frame; memory after unload; pictures (grid, above, far, lights in each state, billboard) |
| `world-monza` | same, plus a hard worst load frame of 50 ms or less; gantry + repeater lamps |
| `world-highlands` / `world-nurburgring` | rotated tracks (ROTATION -17 / 180): markers and lamps |
| `world-drag` | semaphore tree (3 READY steps), jump-start shot |
| `world-track -actrack <id>` | any layout, generic checks |
| `cars-458` (any `-accar`) | 4 wheels, steer, doors, lights, rim blur, cockpit shots |
| `cars-five` | 5 cars in turn; mustang hull rejected (box fallback) |
| `cars-lod` | LOD_0..3 follow lods.ini by distance; LOD textures from LOD_0 |
| `cars-leak` / `cars-leak-ai` | 20 (abarth) / 10 (458 AI options) build and dispose cycles: LiveCount 0, memory back |
| `cars-gauges` | needles turn clockwise, digits read left to right and upright (miata, tatuusfa1) |
| `cars-driver` | driver posed (hips within 2 cm), hands follow the steering, skin, shift clip, hidden at 100 m and back at 5 m |
| `cars-anim` | pop-up lights, suspension clip, paddles, wipers, cockpit HR/LR by distance |
| `cars-dash` | 458 dark LCD text, lap times / TC level, huracan gear pictures + g LEDs, mustang tyre pressure |
| `cars-interior` | remote 458 gets its cockpit through `EnsureInterior` |
| `physics-flat` (`-accar abarth500`) | ride height within 1 cm, tyres on the plane within 2 cm, steer each frame, 0-100 / 100-0 / lateral g vs the harness, straight line, park / freeze, 60 km/h limiter + light, remote teleport and MovePosition |
| `physics-458-magione` | pit box 0 placement, surface keys under the wheels, 6 s drive with a 0.25 s trace and impact log; (polish pass) a 20 s steered stint along the fast lane with floor-gap rays (fails on the lab driver's turn-2 spin) |
| `physics-fleet` | one ok/BAD line per car (CG, tyre gaps, wheels, hull) |
| `seat-enter` | door box on layer 16; seating; level horizon in monitor and VR; pedals disarmed until both triggers are released; wheel grab +30 deg; door wait; exit at the left door; passenger in a remote car; ForceExit; dispose under a seated player; camera pictures |
| `audio-458` | clips ready; distortion probe in Unity's real mixer output (filters run after `volume`; echo-filter gain after the distortion); full throttle at least 10 dB(A) over idle, inside and outside, in the lab's voice render AND in the captured Unity output (needs 15 s of the 19 s sweep captured: run it with the other slot idle); no AudioSource while driving; cone low-pass; gear / door / horn; one hit then scrape; remote car audible at 60 m and silent at 400 m; Dispose frees all |
| `audio-abarth` | backfire, limiter, turbo, gear grind |
| `audio-drive` | magione + 458 with the real rig, seat, physics: engine_int, wind, skid, surface; toggles; remote / passenger / fade-out; 12-car budget; RequestHub silent |
| `hud-desk` | message queue, bar widths, ring direction, spectators, qualify / hotlap / out lap, split, flags, out of fuel |
| `hud-vr` | eye source, kn5 dash line, top edge at least 0.9 deg under it and 5.9 deg under the eye, text at least 1 deg, pedal hint, HUD off, passenger |
| `hud-live` | R practice load + S seated + live feed; qualify clock; VR panel from S's eye; exit hides |
| `front-tablet` | TRACK / AUDIO rows before any load; launcher icon; mouse clicks on RACE LOBBY / TIMING / GARAGE; car held while the app is open; tablet size restored |
| `front-hub` | GARAGE click and an aid; setup rows; hub light set |
| `front-loading` | loading card follows progress |
| `front-session -actrack magione` | tablet closes on load; showroom car removed; a lobby rule change reaches the session; START names the session; garage fuel goes into the stopped car; panel stands on the track; LEAVE TRACK brings back the hub |
| `race-solo-magione` | load, spawn on the road, practice lap, grid, lights, lap rule, cut, finish, results, back to pits |
| `race-solo-actions` | START_RULE default, rates reach P, ChooseCar respawn, pit lane limiter + light, ResetCar (rate, R key), BackToPits, grid seat, 2 m jump start -> FALSE START + 5000 ms, RequestStop / RequestHub teardown |
| `ai-magione` (`-actrack`, `-accar ks_toyota_gt86`) | lap within 10 % of its plan and of the Kunos gold time, no recovery, never more than 2 m off the track, fuel per lap; impact log ("impact:" / "ANOMALY" lines) |
| `ai-race` (default magione, `-actrack trento-bondone` for the open course) | 2-lap race with 3 AI around my parked pole car: built, frozen on the grid, no jump start, all finish; no contact (polish pass: counted only when one of MY colliders is within 0.4 m of the AI's impact point); on trento every AI car reaches the area past the finish. Lab-only impact log with the colliders at the point + a 50 Hz state dump on an anomaly |
| `net-loopback` | loopback record round trip; door / horn sounds; ghost rule in the pit lane; 2 km hide; parked-car cockpit rebuild; master by room bit with an AI car drawn by name; take-over keeps the inherited AI block and the Seq goes up |

Not covered by any scenario (client test with two PCs needed):
- the race-hold refusal on getting out;
- hands on the wheel of a real avatar;
- the clock offset between two PCs;
- results agreement;
- the cockpit swap under a seated passenger;
- the Fusion transport on the upload server.

## Iteration-1 finish (lead, 2026-11-01, after the lab pass and the fixer pass)
- build.sh 0 errors x3 (2 known warnings), Span check clean, check.sh "all 117 proven", metas complete, no duplicate guids under Assets/, no
  orphan metas. Scene uploader: WorldIdForUpload 0, DoReplaceExistingWorld 0, ScriptingAssembly SomniumAssetto, AsmdefReference = the
  SomniumAssetto.asmdef guid (73c53c1f...), AssetName "Wheel Test" (neutral: never name the game); `_shaderCarriers` = the 5 materials in Resources/.
- AcVersion: iteration 1, 2026-11-01, summary updated. README.md written (iteration row 1, test steps, verified / not verified, gaps).
- Every lab ran BEFORE the fixer pass (lab logs 03:40-04:30, fixer edits 04:54-05:11). Smoke re-runs after it: race-solo-actions PASS,
  seat-enter PASS, net-loopback all ok but "B left" (stale lab expectation vs fixer item 9; check corrected in AcLabNet.cs, not re-run).
- labcopy.sh first-copy bug fixed (W's request).
- Open for iteration 2: AI pace (ai-magione FAIL x1.74), 458 0-100, 458 interior loudness, hub clipping (no ppfilters), the unrun scenarios.

## Polish pass 2026-11-01 (lead: the labs not re-run after the fixer)
build.sh 0 errors x3 after every edit, check.sh "all 117 proven", metas complete. Lab runs (each 1x unless noted), all exit 0:
| Scenario | Result | Seen |
|---|---|---|
| net-loopback | PASS (30 ok) | corrected "B left" check passes after the inherited AI block's 10 s clear; seq 2 > 1 after the take-over; pictures: remote abarth on magione, cockpit rebuild (COCKPIT_HR), AI car drawn by name |
| race-solo-magione | PASS (32 ok) | pit box / grid within 0.00 m and 0.0 deg of the markers, lights 1..5 then green, out lap, laps 11.7 s (scripted ResetTo drive), P1, results, back in the box. The cut rule is NOT exercised here (car not driven -> no wheel contacts; race-solo-actions covers cut sensing) |
| audio-drive | PASS (28 ok) | worker stop / generation path: "my car's audio stopped with the rig", 0 car audio after the unload; seat path not exercised (no avatar in batch) |
| front-session -actrack magione | PASS (25 ok) | panel on the track, hub again |
| front-loading | PASS x2 (11 ok; first runs ever) | lab frame wait -> 1.5 s; badge overlapped the session line -> fixed in AcLoading.cs (F/NOTES.md) |
| world-highlands (ROTATION -17) | PASS | 24 grid, 48/48 markers on tagged surface (24/24 pit boxes on IS_PITLANE), lamp 0 38 m ahead, faces 1.00; pictures: grid box on the cobbles, gantry 3 reds / off / 5 reds; memory back (5 MB left of 741) |
| world-nurburgring (ROTATION 180) | PASS | 24 grid, 48/48, lamp 0 71 m ahead faces 1.00, 5 reds under the BMW bridge; memory 5 MB left of 400 |
Only exceptions in the logs: Unity's own SearchDatabase indexer at editor start (not ours).
Observations (not fixed): thin glyphs drop out of small tablet text at 720p (see F/NOTES.md); nurburgring's left run-off looks
over-bright white-yellow in the grid shots (W to check the material); the batch lab spams "no audio listeners" (race-solo log 9.6 MB).

## Final gate after the polish pass (lead, 2026-11-01)
- build.sh: runtime 0 errors (2 known warnings), runtime_server 0 errors, editortool 0 errors; Span check: only SceneNetworking's
  Fusion 2.1 callback. check.sh: "all 117 Fusion/Somnium members are proven". metas.sh: nothing missing. No duplicate guids under
  Assets/, no orphan metas in the code tree. harness-ac builds 0 errors.
- No game files under Assets/ (the code tree holds only .cs / .shader / .hlsl / .mat / .asmdef / README.md; the .ogg/.wav/.png/.ttf
  hits are Somnium's Community Modules / SDK). No game-derived tables in code (no long numeric initialisers except the identity matrix).
- Scene uploader unchanged: WorldIdForUpload 0, DoReplaceExistingWorld 0, ScriptingAssembly SomniumAssetto, AsmdefReference guid
  73c53c1f..., AssetName "Wheel Test" (neutral: never name the game). The gate (Game/AcGame.cs, 10-31 23:09) untouched. AcVersion stays iteration 1.
- Polish pass results (module NOTES "Polish pass 2026-11-01" + "Review of the polish pass 2026-11-01"):
  - P (review): damper speed on the road plane (AcChassis.Step) - the braking dive / floor-box scrape is gone (physics-458-magione min
    floor gap 32 mm, ai-magione braking-zone impacts gone). The steered stint still FAILs on the lab driver's turn-2 spin.
  - I: AI steering ratio = |Spec.SteerRatio| (was a runaway re-measure), plan grip 0.78 (// ours). ai-magione PASS (3rd run 1:40.493
    x1.061; review re-run 1:40.634 x1.062). ai-race FAIL: an AI 458 drives into my standing pole car at 7 km/h after the green
    (REQUESTS "lead-review -> I", open).
  - A: distortion bus plays before the faders + echo-filter gain (interior idle->WOT +19..+23 dB(A) in Unity's output, PASS);
    spread from the bank's min extent (exterior NOT measured after the change: the capture ran short in all 3 runs).
  - F: lobby re-seed / hand-over fix (build only); loading-card overlap fixed (front-loading PASS x2); front-hub PASS, hud-desk PASS.
- Lab scenario list unchanged (no new scenario names); the checks of physics-458-magione, audio-458, ai-magione and ai-race changed
  (table above).

## Final gate after the last-gaps pass (lead, 2026-11-01)
- build.sh: runtime 0 errors (2 known warnings), runtime_server 0 errors (2), editortool 0; Span check: only SceneNetworking's callback.
  check.sh "all 117 Fusion/Somnium members are proven". metas.sh: nothing missing. No duplicate guids under Assets/, no orphan metas in
  the code tree (207 .cs, 5 .mat, 3 .shader, 3 .asmdef, 1 .hlsl, 1 .md). No kn5/acd/ini/dds/lut/ai/bank/ksanim/knh under Assets/; no
  long numeric initialisers (Fsb5 rate list + the identity matrix only).
- Scene uploader unchanged: WorldIdForUpload 0, DoReplaceExistingWorld 0, ScriptingAssembly SomniumAssetto, AsmdefReference 73c53c1f...,
  AssetName "Wheel Test" (neutral: never name the game). Game/AcGame.cs 10-31 23:09 (gate untouched). AcVersion iteration 1. AcNetWire.Protocol 2: no net
  file changed this pass (Blocked / PassSide / FollowDecel / ReverseNose are master-local, nothing new on the wire).
- Code spot-check of the pass (AcAiTraffic.LaneGuard / NeedDecel / Emergency, AcAiDriver.Recover, AcAiBrain.FollowDecel / ReverseNose,
  AcLabPhysics refs, AcLabAi logging): no per-frame allocation in runtime code (struct copies, reused arrays, NonAlloc cast), numbers
  marked // ours, FollowDecel reset every Decide and ignored when reversing (signed v). Lab evidence matches the reports
  (ai-race lab.log: 0 contacts, closest 2.38 m, Martino Fleming blocked 4.7 m behind my car at 6 s; physics-fleet BAD e92_drift 0.418).
- Findings filed to I (REQUESTS "lead-gate -> I"): Recover's reverse exit (ReverseTime > 0.8 s, |HeadingError| < 0.6, > 1.5 m/s) fires
  at once on a straight grid, and ReverseTime is not cleared below 3 m/s, so a Blocked AI gets one ~1 s back-off and then waits for the 5 s
  reset; and LaneGuard's move-time cap (free / tMove: 35 m free, 2.15 m shift -> ~1.95 s -> ~18 m/s) matches the 67 km/h flying pass
  better than the forward cast. Both by code reading, not lab-verified.
- Verdict: READY TO UPLOAD iteration 1. Open (README known gaps / must-test): AI behind a standing grid car stops + 5 s reset, flying-pass
  slowdown, 458 exterior +23.7 vs +16 dB(A), e92 hull vs ride height (physics-fleet 85/86), 458 door open after getting in.

## Damage pass 2026-11-02 (iteration 2, integrator)
### The damage path, traced in code (file : method)
1. Crash: `AcCarPhysics.FixedUpdate` pre-step cache -> `OnCollisionEnter/Stay` -> `DamageHit` (gates, surfaces, pre-step point velocities,
   `ZoneAt` / `CornerAt`) -> `AcCarDamage.AddImpact` (raw km/h, rod x m) -> `RefreshDamageEffects` (aero factors per wing, toe). Only at DamageRate > 0.
2. `AcCarPhysics.Update` -> `PushDamage` on a Version change: C `IAcCarDamageVisual.SetDamage/SetEngineSmoke` (AcCarModel -> AcCarDamageVisual,
   AcCarSmoke), then `AcEvents.RaiseCarDamaged` (own cars; puppets push visuals only).
3. Listeners: H `AcHud.OnCarDamaged` (4 s displayer, ENGINE BLOWN outside a race), S `AcSeatDamagePulse`, A reads `MaxSuspDamage01` in Feed.
4. Net out: `AcNetGame.Own` FillCar `ToWire` -> ints 42-43 / master's Host 0 table (`AcNet.Blocks`). Remote in: `AcNetGame.Cars.Draw` ->
   `DamageRig` -> `ApplyWire` -> CarDamaged (N) -> P's Update pushes the puppet's visuals. Hand-over: `AcNetGame.Race.SeedAiDamage`.
5. Repair: `AcRace.Repair` (box, frozen, RepairSeconds, A's RepairSound) -> `AcCarPhysics.Repair`; BackToPits / new session / grid / spectate
   -> `RepairCar`; never in ResetTo / PlaceInPits. `ApplyDamageRate` (local + master AI each frame) >0 -> 0 repairs. Race: `RetireTick` -> DNF.
6. AI: `AcAiField` DamageRate at spawn, R's ApplyRates every frame; `AcAiDriver` Retired state parks + freezes on EngineDead.
7. DAMAGE 0: DamageRate 0 -> no DamageHit, no accumulation, no event, wire 0 0, gate off -> no texture slots, FlattenDamageNormals as before,
   HUD hidden, A pins flutter at 0. Visible at 0 by design only: G0 glass fix, P0 turbo fix, protocol 3, lobby default 100 (server_cfg).

### Seams fixed by the integrator
| Change | File | Why |
|---|---|---|
| `AcEvents.CarCollisionWanted` (new); P returns after CarImpact when nobody listens | Game/Shared/AcEvents.cs, Game/Physics/AcCarPhysics.cs `Impact` | no extra contact work at DAMAGE 0 (A2 / sparks not built) |
| Rebuilt remote rig with the same pair: visuals re-pushed, no second CarDamaged | Game/Net/AcNetGame.Cars.cs `DamageRig` | cockpit rebuild re-popped H's displayer when riding |
| Damage textures need `VisualOn` too at build | Game/Cars/AcCarModel.cs `Begin` | VISUAL DAMAGE switched off after R's Refresh still loaded ~6 MB |
| cars-damage LiveCount check ignores the hub's showroom car | Editor/AcLabCars.cs | lab artefact (only FAIL of 471) |
| C's "re-push after model swap": no code - a rig's Model never changes, a rebuild is a new rig | REQUESTS.md | answered |

### Lab scenarios for damage (Editor/AcLab*.cs)
physics-crash, physics-engine (P) | cars-glassfix, cars-damage (C+W) | race-repair (R) | hud-damage (H) | audio-damage (A) |
front-garage-repair (F) | ai-retire (I) | **damage-e2e (new, Editor/AcLabDamage.cs)**: DAMAGE 0 wall hit = nothing; real PhysX front 60
and right 45 -> zones, CarDamaged, C state = ZoneByte, paint copies, panels, glass, aero, H timer; ToWire -> Kind Remote puppet ApplyWire ->
identical visuals and pair; Repair(All) pristine everywhere; reset keeps; damage off = no accumulation. Pictures in DevTools/lab/damage-e2e/.
Not extended: net-loopback (two real clients; N's offline NetTest loopback covers it: 68 ok).

### Verified this pass (integrator runs)
- build.sh x3 0 errors (2 old warnings); check.sh "all 117 proven"; metas.sh (AcLabDamage.cs.meta); harness dmgspec / dmgunit (45 ok) / dmgturbo /
  dmghud PASS; N NetTest 68 ok, 0 failed.
- Lab damage-e2e PASSED twice (26 checks). Looked at: e2e-front60-front34 (front dented / scratched, headlights cracked, bonnet bent),
  e2e-remote-front34 (puppet shows the same front + right damage, right window cracked), e2e-repaired-front34 (pristine).
- Lab cars-glassfix PASSED (first run ever): no glass zone shown at DAMAGE 0 on ktm_xbow_r / bmw_m3_e92 / lotus_exos_125 LOD1; xbow windscreen
  picture has no grey film. Lab cars-damage: 471 ok, 1 FAIL (hub-car LiveCount artefact, fixed after the run, not re-run).
### Open (user to judge)
- Head-on wall hits bend no steer rod (1 m hub rule); miata vs 458 bonnet bend sign vs AC screenshots; smoke sits on the 458's FRONT bonnet
  (mid-rear engine); repair times (suspension 300 s); VR damage cell position; flutter needs a respawn after damage 0 -> on mid-session.
- Not run: two real clients, AI hand-over damage in Unity, a live race DNF in standings, the controller pulse in a headset.

## Damage pass 2026-11-02 - final gate for iteration 2 (lead)
- build.sh: runtime 0 errors (2 old warnings), runtime_server 0 (2), editortool 0; Span check: only SceneNetworking's callback.
  check.sh "all 117 Fusion/Somnium members are proven". metas.sh: nothing missing; no orphan metas, no duplicate guids under Assets/
  (216 .cs). No kn5/acd/ini/dds/lut/bank/fsb/ksanim/knh/rto under Assets/; no long numeric tables in code.
- Scene untouched (file 11-01 16:26): AssetName "Wheel Test", the neutral AssetDescription, WorldIdForUpload 0, DoReplaceExistingWorld 0,
  ScriptingAssembly SomniumAssetto. Game/AcGame.cs 10-31 23:09 (gate untouched). AcNetWire.Protocol 3 (AcNetBlocks.Protocol follows it).
- AcVersion -> iteration 2, 2026-11-02, damage summary. README: Iterations row 2, "What iteration 2 adds: car damage", how-to-test
  damage steps, damage log lines, must-test (iteration 2 first), verified / known gaps; "protocol x, this build 3".
- Harness re-run by the lead: dmgunit 49/49, dmgspec PASS (0 damage problems on 86 cars).
- The fixer's last changes (16:24-16:27: per-step hit gathering, AcRemoteContact, AI ghost rule, hand-over tag, red line, flutter check,
  crack worker) landed after most flow labs. Re-run after them: damage-e2e 26/26 + cars-damage 472 (fixer), physics-crash 16/16 and
  net-loopback 44 ok (lead; looked at physics-crash-post50-front34: left door / window scratched, dented, cracked; nose clean).
  Not re-run after them: race-damage-live, ai-retire-race, hud-damage, front-garage-repair, physics-flat / cars-458 (DAMAGE 0 path
  unchanged by the fixer: every damage call is behind DamageRate > 0; only A's flutter instrument is now built at DAMAGE 0, pinned at 0).
- Verdict: READY TO UPLOAD iteration 2. Never run: two real clients, the late-start repair, the HUD red line, the flutter turning on
  mid-session, the controller pulse in a headset, a race DNF on F's TIMING page.
