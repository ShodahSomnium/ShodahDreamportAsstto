# N - Net (brief)

Read first: `DevTools/AGENTS.md` (especially "Network slot plan"), `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/` as in AGENTS.md;
code root `Assets/#User/SomniumAssetto`.

## Scope
Multiplayer through the scene's 69 pre-placed blocks: slot claim, the own 64-int player record (car, pose, controls, lights, doors,
seats, passengers, timing), remote players and their cars drawn interpolated with the real model + skin and engine sound, passengers
riding a remote car, the instance master (Halo's record-bit rule) and its race-control / AI / race-state blocks, host hand-over, and the
public face `AcNetGame` (incl. `TryHasRecord`, which the isolation depends on).

## You own
`Game/Net/` except `SceneNetworking.cs` (stubs `AcNetGame.cs` (AcNetGame, AcNetPlayer, AcRemoteCar, AcRaceControl, AcNetDriver) and
`AcNetSlots.cs` - fill / split them, keep every member), `Game/AcGame.Net.cs` (wired: Start + AddOnce<AcNetDriver> + Tick + Stop),
`Game/Shared/AcLocal.Net.cs`, `Editor/AcLabNet.cs`.

## Port from (rename Halo/Ets -> Ac)
- `HALO/Game/Net/Core/HaloNetTransport.cs` (255: `IHaloSlotTransport` + loopback world for lab/harness - drop CoopCount, add AI/Race/Host
  counts), `HaloSlotClaim.cs` (305, VERBATIM), `HaloNetWire.cs` (783: keep header ints 0-4, the helpers OwnerOf/GenerationOf/StatusOf/
  WriteFree/WriteClaim, packers PackCm2/PackQuat/PackVel/Yaw16/Unit8/NameHash, structs; new racing payload), `HaloNet.cs` (894: keep
  TickCore lines 211-330, HasRecord, Stop; drop shooter parts), `HaloNet.Master.cs` (520: master rule VERBATIM, command seq, lobby),
  `HaloNetWorld.cs` (349: header 7 ints, race-control payload), `HaloNetStream.cs` (185) + `HALO/Game/Net/HaloNetHostState.cs` (180) for
  the Host blocks (optional in iteration 1).
- `HALO/Game/Net/HaloFusionTransport.cs` (317): object discovery BY NAME - use `AcNetSlots.RecordOf(name)` (prefixes "Ac Net Player ",
  "Ac Net World ", "Ac Net Ai ", "Ac Net Race ", "Ac Net Host "), IntCount >= 64 check, write only changed ints, RequestAuthority, Present.
- `HALO/Game/Net/HaloNetGame.cs` (public face pattern, `HaloNetDriver` l.338).
- `ETS/EtsNetWire.cs` `EtsNetTimeline` (l.221-492, stamped Hermite timeline, VERBATIM - better for 300 km/h than HaloNetTimeline; tune
  MinDelay ~0.03 / 1 send interval per ets_vehicles 8.5), `ETS/EtsNetVehicle.cs` (587: remote vehicle build sliced + stand-in box + Draw +
  Contacts l.461 solid/ghost by relative speed with excludeLayers), `ETS/EtsPlayerWire.cs` (1788: packing ideas, seat table, rider record),
  `ETS/EtsNet.cs` (frame order, WriteOwnSlot l.1400 30 Hz cap, PackOwnMotion l.1514 from body.position + fixed-time stamp),
  `ETS/EtsRiders.cs` + `ETS/Ets/EtsRiderMath.cs` (passengers: rigid correction of remote bodies on the locally drawn car) - with S.

## Spec sections
`research/halo_skeleton.md` 2.2 (net rows), 2.5 (SceneNetworking, NetworkBridgeData64 API, record header, master + hand-over), 4.1
(scene blocks, SortKeys), 6 (PROVEN Fusion / Bridge API - use nothing else). `research/ets_vehicles.md` 8 (all) + 12 (lessons 13, 14, 17).
`research/ac_physics_sound_rules.md` 2.7 (sound over the net). `research/ac_track_formats.md` T3 (race distance).

## Slot plan (verified by the contracts step: 69 objects present, names exact, unique SortKeys; records 0..68 in AcNetSlots order)
- **Player 0..49** (owner writes): header ints 0-4 as Halo (magic 'ACP1' = 0x41435031). Proposed payload (finalise, keep it in
  NOTES.md as a table): 5 car id hash (NameHash of the folder), 6 skin hash, 7 layout hash I am on (0 = hub), 8-9 position PackCm2,
  10 rotation PackQuat, 11 velocity x|z S16 cm/s, 12 velocity y S16 | yaw rate S16, 13 steering-wheel deg S16 x10 | rpm u16,
  14 gear s8 | throttle u8 | brake u8 | clutch u8, 15 AcLightState u16 | handbrake u8 | boost u8, 16 suspension travel x4 (u8 mm offset),
  17 skid x4 (u8), 18 surface index x4 (u8, index into the layout's AcSurfaces list), 19 door L/R u8 | placement epoch u8 | seat flags u8,
  20 passenger photon id+1 u16 | ride-in owner photon id+1 u16, 21-23 rider record (ETS), 24 laps u16 | sector u8 | cuts u8, 25 lap ms,
  26 last ms, 27 best ms, 28 race distance x1e4, 29 finish total ms, 30-32 last-lap sector ms, 33 penalty ms, 34 lobby ready / spectate
  / VR flags, 35 race-command seq applied, 36-63 spare. Flags int 4: status bits 0-1, VR, on-track, seated, driving, passenger,
  bit 7 instance master, in-pitlane, lap-valid, finished. Write ~10 Hz idle, up to 30 Hz driving; seq/stamp advance only when motion ints change.
- **World 0..3** (master): Halo world header 7 ints (magic 'ACW1'). World 0 = AcRaceCommand (phase, track/layout hash, session type,
  laps, minutes, rule bits, fuel/wear %, AI count/level, StartUtc seconds + ms, command Seq, allowed-car hashes), World 1 = grid order
  (2 ids per int) + lobby state, World 2-3 = results (id/position + total ms each).
- **Ai 0..8** (master): 4-int header + 2 AI cars x 30 ints (same packing as player ints 5-34) -> up to 18 AI cars.
- **Race 0..1** (master): Race 0 = start-light steps on the shared UTC clock, session clock, leader-finished time, flags; Race 1 =
  penalties per slot / AI (u8).
- **Host 0..3** (master): HaloNetStream hand-over blob (224 bytes per block) - only if AI / director state must survive a master change.
- Hashes resolve through the catalog every client has (`AcContent.Cars`/`Tracks`, NameHash lower-case); a car the receiver does not own
  -> grey stand-in box + common sounds, log once.

## Must do (acceptance)
1. `Start/Tick/Late/Stop` (Tick first of all hooks; Late from AcNetDriver at 1000). Alone / offline / editor: `IsMaster` true, nothing
   sent, `RaceControl.Publish` applies locally (keep the stub's behaviour), `TryHasRecord` known=false.
2. Claim a player slot (HaloSlotClaim), write `AcLocal.NetSlot/PhotonId/Nick`; `TryHasRecord(photonId, out known)` true for holders of a
   written slot (the isolation shows only them). Stop writes the slot free.
3. Own record from `AcLocal` (+ `AcLocal.Telemetry`, `AcLocal.Car` body pose from `Rigidbody.position/rotation/linearVelocity` with a
   fixed-time stamp, `AcLocal.Seat*`, race fields); read all others into `Players` (reused objects, no per-frame allocation).
4. Remote cars: after `AcSession.TrackBuilt`, for each player on my layout build `AcCarRig.Spawn(car, skin, Kind Remote)` (stand-in box
   meanwhile, one build at a time), draw on EtsNetTimeline (`ApplyPuppet`, `Model.Set*`, `Audio.Feed(shown)`), hide beyond 1500 m, contacts
   one-sided (kinematic puppets; ghost when relative speed > 10 km/h near spawn or in the pit lane, // ours - ETS Q-A3). Destroy on unload / leave.
5. Master: rule verbatim; `RaceControl` World 0..3 write (master) / read (all) with Seq growing across hand-over; `Changed` fires once per new
   Seq -> R's `AcRace.Apply`. AI blocks for I (API for I: `AcNetGame.PublishAi(index, rig)` / remote AI cars drawn like remote players - agree in REQUESTS.md).
6. Passengers: seat request to the car owner (ETS RequestSeat flow: grant within 0.25 s, refuse / 3 s timeout) and rider records; S
   seats the local passenger on the locally drawn car (coordinate the API with S in REQUESTS.md).
7. Only proven Fusion API (check.sh): no GetMasterClient, no GetAllBehaviours<NetworkTransform>, no NetworkBridgeEvents (the scene has no
   events object; ask the lead if you need one).

## Player.log lines to add
`Net: N player slot object(s) found ('Ac Net Player 0..49'), K usable; world 4, ai 9, race 2, host 4` · `Net: claimed slot n (photon id p)`
· `Net: master is <name> (me|record bit|lowest id)` · `Net: race command seq n applied (<phase> <track>)` · `Net: remote car <name> <car>
built (stand-in s)` · `Net: <name> joined/left` · a stats line every 60 s (bytes/s written, players, remote cars).

## Verify
build.sh x3, check.sh (all proven). Harness/lab with the loopback transport: two simulated clients claim different slots, records
round-trip (pose quantisation error < 1 cm, rotation < 0.01 deg), master election + hand-over when the master leaves, command Seq grows.
Lab `net-loopback` (two AcNet sessions on HaloLoopbackWorld in one play mode, a remote car drawn following a scripted path - shot).
Real multi-client behaviour needs the user's client test: write the exact steps.

## Consumes / provides
Consumes: AcLocal (all owners), AcCarRig (P), AcSession/AcRaceCommand (R), AcSeat riders API (S), AcContent (D). Provides: AcNetGame
(TryHasRecord for the isolation, IsMaster/MasterName/Players for F/R/H, RaceControl for R, RemoteCars), AcNetSlots.
