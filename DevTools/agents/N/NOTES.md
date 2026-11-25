# N - Net: notes (2026-11-01, second pass)

## Files (all mine)
- `Game/Net/Core/` - PURE C# (no UnityEngine; compiled by my harness `DevTools/agents/N/harness`):
  `AcNetWire.cs` (player record, packers, PackQuat2), `AcNetTransport.cs` (IAcSlotTransport + AcLoopbackWorld/Transport),
  `AcSlotClaim.cs` (HaloSlotClaim VERBATIM, renamed), `AcNetTimeline.cs` (EtsNetTimeline port), `AcNetBlocks.cs` (race command / results /
  race state / penalties / AI codecs), `AcNet.cs` (HaloNet port: tick, claim, housekeeping, remotes, own write, HasRecord, Stop),
  `AcNet.Master.cs` (master rule verbatim, take-over), `AcNet.Blocks.cs` (master blocks write/read, offline-command parking), `AcNet.Seats.cs`.
- `Game/Net/` (Unity): `AcFusionTransport.cs`, `AcNetGame.cs` (public face, AcNetPlayer/AcRemoteCar/AcRaceControl, AcNetDriver LateUpdate 1000,
  AcNetEarlyDriver Update -95; per-step guarded Tick), `AcNetGame.Own.cs` (own record), `AcNetGame.Players.cs`, `AcNetGame.Cars.cs` (remote
  cars), `AcNetGame.Race.cs` (command <-> wire, race state, penalties, results, AI blocks, names.txt lines), `AcNetGame.Seats.cs`, `AcNetSlots.cs`.
  `Game/AcGame.Net.cs`, `Game/Shared/AcLocal.Net.cs` (+NetOnline), `Editor/AcLabNet.cs` (lab `net-loopback`).

## Second pass (critic gaps) - what changed
1. Guards: Tick runs each step in its own try/catch (compose own / AI publish / session tick / players / AI players / commands / seats / riders /
   stats; 3 logs per step). Calls into other modules are guarded one by one: AcPlayer.XrPresent, AcRace.Standings, AcTrackWorld.Surfaces,
   AcAi.Drivers, AcRiders.CanGrantPassenger, P's Placements/Body/Telemetry (FillCar) and C's HasPassengerSeat. Remote cars: each car in its
   own try/catch, and ApplyPuppet / Model.ApplyTelemetry+SetDoor / Audio.Feed+Door+Horn / own-car read each guarded.
2. A -> N DONE: door edges -> `Audio.Door(side, opening)`, horn bit (record int 17) -> `Audio.Horn(bool)` (rig and stand-in audio).
3. Inherited AI blocks: kept while I lists a driver of that index whose Rig is still null (I builds one by one), cleared after 60 s if it
   never builds (// ours); other indices are cleared as before.
4. Offline command (editor / first second / reconnect): parked on joining (not current, Seq not seeded); dropped when the room has a command
   or another master; re-published under a room Seq when I am the master of a room without one (`AcNet.Republished`). Game layer also
   compares MasterId with Seq, so an offline seq 1 never shadows the room's seq 1.
5. A layout this catalog lacks travels as `"#" + hash` (R toasts "not installed" instead of loading the default layout).
6. Passenger cockpit: a parked (< 2 km/h) player car within 15 m of my eye (I am not seated) is rebuilt once with
   `ForKind(Remote)` + Interior/Instruments/Digital and swapped like the stand-in (`AcRemoteCar.Interior`). Not while my ride request for it
   is open; under my own seat only if S plugs `AcNetGame.SwapSeatedCar` (asked in REQUESTS "N -> S"; null now).
7. AI names: the names.txt line (+1) travels in AI int 26 bits 16-23 next to the 12-char text; receivers use the line when it agrees with the
   text (another names.txt falls back to the text). Install: 40 names, 26 longer than 12 chars (harness `netnames`; I's comment says 39).
8. Stand-in box is solid: kinematic Rigidbody + its BoxCollider on AcLayers.Car, same ghost rule. `AcRemoteCar.Occupies` added; R and I asked
   ("N -> R, I") to test `Occupies` + `Frame` instead of only `Rig`.
9. Player record ints 39-41: wheel angular velocity x4 (S16 0.1 rad/s; a change is motion) + fuel u16 0.1 L + "wheel speeds present" bit.
   Remote wheels spin from the owner's AngularVel (lock-ups / wheelspin show); fuel gauge = the owner's. AI cars (no room in their blocks)
   still derive spin from speed and use the spec's fuel.
10. P's factory is real now (AcCarRig builds spec on a worker, model, puppet physics, audio): remote cars build real models. R now consumes
   `AcNetGame.AiPlayers` (AcTiming / AcStandingsBoard / AcRace.Flow) and calls `AcAi.Observe()` (AcRace.cs l.141) - no longer open.
11. ClearCars no longer frees the build slot while a dropped build runs (Finish frees it; a build stuck > 120 s frees it, // ours);
   MasterName's "Player n" string is made once per id; AI name resolution runs only when its inputs change (no per-frame skin hashing).
   Host blocks: still unused (optional) - AI / director state survives a master change only through the World / Ai blocks.

## Verified (how)
- Harness `cd DevTools/agents/N/harness && dotnet build -c Release -v q && dotnet run -c Release --no-build -- nettest` -> **45 ok, 0 failed**
  (was 38): + wheel speeds / fuel round-trip (lock-up alone = motion, fuel change not), record without wheel speeds flagged, AI names.txt line
  round-trip with the 12-char text, offline command parked and the master's equal seq 1 arrives as NEW, never re-published by a non-master,
  an offline master of an empty room re-publishes and a joiner reads it. (The harness csproj now excludes other modules' Data folders.)
- `netnames` on the real install: 40 names, 26 > 12 chars, 0 outside Latin-1, line byte round trip 40/40. `nethash`: 0 collisions (first pass).
- build.sh: my runtime files compile with 0 errors (other modules had transient errors at the time); see the final report for check.sh.

## NOT verified
- Nothing ran in Unity (lab not allowed this phase): remote car build/draw, stand-in collider, cockpit rebuild + swap, door/horn audio, ghost
  rule, hide radius, AI cars drawn, the Fusion transport. Real multi-client behaviour needs the user's client test (below).

## Player record (64 ints, "Ac Net Player 0..49", owner writes; seq/stamp advance only when ints 8-22 or 39-40 change, +1 s keep-alive)
| int | content |
|---|---|
| 0 | magic 'ACP1' 0x41435031 |
| 1 / 2 | seq / stamp = owner's Time.fixedTimeAsDouble ms of the pose |
| 3 | owner PlayerId+1 (0-15), claim generation (16-23), protocol 1 (24-27) |
| 4 | status 0-1 (Free/Present/Playing), VR 2, on track 3, seated 4, driving 5, passenger 6, **instance-master bit 7**, ready 8, spectating 9 |
| 5 / 6 / 7 | NameHash car folder / skin folder / layout key (0 = hub) |
| 8-10 | car root (Rigidbody at CG) position, int mm, Unity world |
| 11-12 | rotation PackQuat2 (smallest three, 3 x 20 bits) |
| 13 / 14 | vel x / z S16 cm/s; vel y S16 cm/s / yaw rate S16 0.1 deg/s |
| 15 | steering wheel S16 0.1 deg / rpm u16 |
| 16 | gear s8, throttle, brake, clutch u8 |
| 17 | AcLightState u16 (horn = bit 6), handbrake u8, boost u8 (0.02 bar) |
| 18 / 19 / 20 | susp travel x4 s8 mm / skid x4 u8 / surface index x4 u8 (255 none; index in the layout's AcSurfaces list) |
| 21 | door L u8, door R u8, placement epoch u8, flags (present, limiter, ABS, TC, shifting, frozen) |
| 22 | front road-wheel steer LF / RF S16 0.01 deg |
| 23 | laps u16, sector u8, cuts (this lap) u8 |
| 24-29 | lap ms, last ms, best ms, race distance x1e4, finish TOTAL ms (FinishedMs + PenaltyMs), penalty ms |
| 30 | position u8, lap valid 8, in pit lane 9, finished 10 |
| 31 | seat table (OWNER writes): passenger PlayerId+1 u16, no passenger seat 16, refusing riders 17 |
| 32 | rider: target u16 (PlayerId+1 / 0x8000|AI index), seat u8, epoch u4, requesting 28, VR 29 |
| 33-34 | rider offset x, y, z S16 mm + yaw16 (S's AcLocal.SeatRootLocal / SeatYawLocal) |
| 35-37 / 38 | last lap sector ms x3 / race command Seq applied |
| 39 / 40 | wheel angular velocity LF / RF, LR / RR (S16 0.1 rad/s) |
| 41 | fuel u16 (0.1 L), wheel speeds present bit 16 |
| 42-63 | spare |
Master blocks: see `Core/AcNetBlocks.cs` (World 0 command incl. PhaseUtc + 43 allowed hashes, World 1 grid (68) + 23 more allowed, World 2-3
results (56), Race 0 lights / clocks, Race 1 penalties u8 x 68, Ai 0..8 = 2 cars x 30 ints: 26 car ints | index u8, level u8, names.txt
line+1 u8 | 3 ints name). Host 0..3: found + counted, unused.

## Public API (additions to the stub)
`AcNetGame`: `Ready`, `Spectating`, `MasterId`, `PlayerJoined/PlayerLeft`, `AiPlayers`, `NameOf(id)`, `LocalNick()`, `LayoutHash`,
`NotePlacement()`, `RequestSeat(rig, seat, granted, refused)`, `LeaveSeat()`, `SeatRequestPending`, `PassengerOf(rig)`, `OwnerOfCar(rig)`,
`PlayerOfCar(rig)`, `RemoteCarOf(rig)`, `PublishRaceState/TryGetRaceState`, `SetPenalty/PenaltyOf`, `PublishResults/Results/ResultsSeq`,
`Early()`, `Net`, `TransportFactory`, `Clock`, `LabNames`, `DrawRadius`, **`SwapSeatedCar`**, **`DriverNameAt(line)`**,
**`DriverNameIndex(name)`**, `InteriorParkedKmh/InteriorNearMetres`. `AcNetPlayer` +Driving, Ready, Spectating, Position, PenaltyMs, Cuts,
LastSectorMs, LayoutHash, HasCar, CarInstalled, PassengerPhotonId, HasPassengerSeat, AppliedSeq, SpeedKmh, Gear, IsAi, AiIndex, AiLevel.
`AcRemoteCar` +Frame, Holder, Velocity, Hidden, Ghost, StandInOnly, Drawn, CarId, SkinId, **Interior, Occupies, DoorSounds, HornOn**.
`AcNet` +`SetAi(..., nameLine)`, `AiCar.NameLine`, `Republished`. `AcNetBlocks.PackAi(..., nameLine)`. `AcNetWheel.AngularVel`,
`AcNetCar.FuelLitres/HasWheelSpeed`. `AcLocal.NetOnline`.

## Frame order
AcNetEarlyDriver Update -95 (draw remote cars at this frame's moment) -> S's AcRiders -90 -> AcGame.Update: NetTick (compose own, AI publish,
session tick, players, commands -> RaceControl.Changed, seats, riders) -> FrontTick -> RaceTick -> AcNetDriver LateUpdate 1000 (nothing yet).

## Numbers that are ours (judge them)
Teleport detection 3 m beyond velocity; ghost rule 10/9 km/h (ETS), 3 s after a placement, 4.5 m overlap guard; draw radius 1500 m (ETS);
stand-in box 1.9 x 1.3 x 4.5 m grey; keep-alive 1 s; refusal bit shown 1 s; inherited AI cars cleared after 10 s, a pending AI build kept 60 s;
cockpit rebuild when parked < 2 km/h within 15 m; build stuck 120 s; wheel radius fallback 0.32 m; timeline MinDelay 0.03 / MaxDelay 0.4 /
extrapolation 0.25 s (ETS values).

## Open
- Host blocks (HaloNetStream hand-over): not implemented (optional in iteration 1); I adopts inherited AI cars from the Ai blocks.
- A player who boards a remote car before its cockpit rebuild finished rides in COCKPIT_LR until S plugs `SwapSeatedCar`.
- AI cars: wheel spin from speed, fuel from the spec (no room in the Ai blocks).
- R / I: use `AcRemoteCar.Occupies` + `Frame` for occupied pit boxes / grid slots (asked).

## Player.log lines (`[SomniumAssetto] Net: ...`)
`started (Fusion session, protocol 1)` · `50 player slot object(s) found ('Ac Net Player 0..49'), 50 usable; world 4, ai 9, race 2, host 4` ·
`room joined (...)` · `claimed slot n (photon id p)` · `master is <name> (me|record bit|lowest id)` · `<name> joined (slot n, ...)` / `left` ·
`race command seq n applied (<Phase> <track>[/<layout>], published by me|from <name>)` · `a race command published offline (seq n) is parked
...` / `... was dropped (...)` / `the race command published offline is now seq n in the room (I am the master)` ·
`remote car <name> <car>/<skin> built (stand-in X s)` · `remote car <name> <car> rebuilt with its cockpit (COCKPIT_HR) for a passenger` ·
`remote car ... is not installed here - grey stand-in` · `N AI driver names from content/career/names.txt travel as their line number` ·
`the master's layout #xxxxxxxx of <track> is not in this install's catalog - shown as not installed` · seat lines · `took over as instance
master ...` · `<step> failed (n): <exception>` (3 per step) · every 60 s `stats: ...` · `stopped (slot written free)`.

## Lab (later): `bash DevTools/lab.sh net-loopback [-actrack magione] [-accar abarth500]`
Checks claims (A slot 1, B slot 2), TryHasRecord both ways, A master, A's command reaches B, track loads, B's car drawn (stand-in first:
net_standin.png, then the model: net_remote_car.png, net_remote_close.png) within 20 m/s x 0.25 s + 1 m, remote telemetry 60-85 km/h, late
joiner C reads the command, B asks for A's passenger seat. Second pass: B's door open/close -> >= 2 door sounds, horn on/off reaches A's
audio; ghost rule in / out of the pit lane (+ Car bit in excludeLayers); B 2 km away hidden and shown again; B parked 7 m in front of the eye
rebuilt with its cockpit (net_remote_cockpit.png); room bit -> B master, B's AI car 0 listed + drawn with its full names.txt name
(net_ai_car.png); bit gone -> A master again, keeps the inherited AI block, next command Seq higher; B leaves -> car gone, A master.
LOOK at the five pictures.

## Client test (user, 2+ whitelisted accounts)
1. Both join the world; each Player.log: `objects found ... 50 usable; world 4, ai 9, race 2, host 4`, `claimed slot n`, the SAME
   `master is <name>` on both, `<other> joined`. A visitor (non-whitelisted) third account: no Net lines in their log, invisible to both.
2. Master: tablet TRACK -> load; the other: `race command seq 1 applied (Loading <track>)` and the same track loads.
3. Both in pit boxes: each sees the other's car (`remote car <name> <car> built`); while it builds a grey box stands there and is solid.
   Drive: the other car moves smoothly, wheels steer/spin (lock a wheel under braking: it stops turning on the other screen), brake lights,
   engine sound; door open/close and horn are heard on the other client.
4. Bump each other slowly in the pit lane at > 10 km/h relative: pass through (ghost); on track: solid (one-sided).
5. Passenger: walk up to the other's parked car (log: `rebuilt with its cockpit`), passenger door -> `asked <name> ...` / `granted` -> you
   ride along and see the high-res cockpit with live gauges; the owner drives.
6. Race with AI (master AI count > 0): the other client's standings show AI names in full (e.g. "Loreto Winterbottom"); master leaves
   mid-race -> the other logs `took over as instance master`, AI cars stay put (not vanishing one by one), the next command has a higher seq.

## Lead integration changes (2026-11-01)
- AcNetGame.Cars.cs: AI records build with `ForKind(AcCarKind.Ai)` (interior rebuild: AI options + cockpit); `ModelRig` calls `SetLapTimes(LapMs, LastMs, BestMs)`; the stand-in sound rides on `FrameStandIn`.
- AcNetWire.cs: suspension travel in 2 mm units (+-254 mm); harness tolerance 1.1 mm (nettest 45/45).

## Lab pass (2026-11-01, audionet agent) - protocol 2 + net-loopback
- **Wire protocol bumped 1 -> 2** (`AcNetWire.Protocol`; `AcNetBlocks.Protocol = AcNetWire.Protocol`) for the lead's 2 mm suspension travel.
  Before, the protocol nibble (int 3 bits 24-27) was written but never read. Now: `Unpack` refuses a record of another protocol (no player,
  no car, no seat, no timing); its header still keeps the slot taken (claim / housekeeping unchanged) and its master bit 7 / player id
  still count in the master rule (`AcNet.Master.cs`: undecoded remotes read by `AcNetWire.MasterBitOf`), so mixed builds agree on ONE
  master and never fight over the World / Ai / Race blocks; `HeaderOk` / `AiHeaderOk` / take-over `CopyPayload` ignore other-protocol
  blocks. New: `AcNetWire.ProtocolOf`, `Foreign`, `MasterBitOf`. Log once per player: `Net: player n (slot k) runs net protocol x, this
  build 2: refused (...)`. Protocol-1 builds never checked the nibble (none was uploaded). Rule: bump Protocol whenever a field changes.
- Harness nettest **49 ok, 0 failed** (+4: protocol written/decoded, protocol-1 record refused but header intact, protocol-1 blocks
  ignored, travel +0.200 / -0.090 m round-trip in 2 mm units).
- Lab `net-loopback` run 1/2: PASS claims, TryHasRecord, master, command, late joiner, seat grant, track load, stand-in -> model,
  follow (worst 1.6 / 3.6 m at 20 m/s), telemetry 72 km/h, room-bit hand-over, AI car + full names.txt name, take-over keeps the AI
  block, Seq 3 > 2, B leaves. FAIL door/horn, ghost-in-pit, 2 km hide: diagnosed as LAB timing, not code - the batch lab runs at
  ~2000 fps (90 frames = 40 ms of game time), shorter than the timeline's render delay, and B's fixedTime stamps repeat across frames
  (only 3 samples kept). `Editor/AcLabNet.cs` second-pass phases are now time-based (3 s / 1.5 s), with `Diag(...)` lines on failure
  (`AcRemoteCar.Describe()` added, lab / diagnostics only). The cockpit-rebuild check now parks B 7 m from N's eye fallback
  (AcLocal.WorldPosition; the lab has no Bridge head) instead of being skipped.
- Pictures looked at: net_standin.png (grey box on magione's grid), net_remote_car.png (white abarth500 from behind, tail lights),
  net_remote_close.png (abarth500 front 3/4, skin + interior visible), net_ai_car.png (a grey stand-in box near the camera, a built
  abarth500 further up the track).
- Lab `net-loopback` run 3/3 (time-based phases): **all PASS** (door 2 sounds, horn on/off, ghost in pit True / out False with the Car
  bit matching, 2 km hidden + shown again, cockpit rebuild of B parked 7 m from the eye, AI car + full name, hand-over, Seq 3 > 2, B
  leaves). No exceptions of ours. Looked at net_remote_cockpit.png: abarth500 cockpit from the passenger side (dash, A-pillar, mirror,
  magione pits through the windscreen). net_ai_car.png showed the AI car's roof poking through its grey stand-in box: the shot is taken
  in the frame the rig is adopted and `Destroy` waits for the frame end - Finish now also `SetActive(false)`s the box at once (compiled,
  not re-run: lab budget used).

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3, check.sh all 117 proven; no lab run)
- AI placement epoch: PublishAiCars sets car.Epoch per AI index (bumped for a new rig - starting above the inherited car's epoch - and for
  every Physics.Placements change): receivers snap on grid / pits / reset, and a new master's rigs never share the old master's epoch.
- ReadAi clears both cars' timelines when an AI block's writer changes (a later-started master's lower stamps were all dropped: AI froze).
- Stale sessions: a World / Race / AI block whose writer is not in the room AND whose command Seq nobody listed (nor I) holds is ignored
  (AcNet.SessionAlive; an empty room list counts as alive). TakeOverAsMaster does not inherit such blocks: race / results / penalty blocks are
  rewritten empty under command 0, AI blocks empty. This also hides the stale Race 0 beacon from AcRaceClock. Log once: "master blocks of
  race command N are from a session nobody is in any more ...".
- TakeOverAsMaster marks World 0/1 used with the command Seq: the new master rewrites both under its writer id (repairs a torn command).
- RefreshAiPlayers: a new master keeps an inherited AI car drawn (stand-in + collider) until its own AI rig for that index exists.
- PublishResults skips Spectating rows (late joiners are not classified).
- Not verified in a lab (net-* not re-run).

## Lead, iteration-1 finish (2026-11-01)
- net-loopback re-run after the fixer pass: every check ok except "B left": A's RemoteCars held 1 car = the AI car A inherited from B. Since
  fixer item 9 a new master keeps drawing an inherited AI car until its own rig exists or the block is cleared (10 s with no AI driver,
  AcNetGame.Race.cs), and the check ran ~8 s after the take-over. Not a product bug (by reading the code). `Editor/AcLabNet.cs` now counts only
  player cars for "B left" and then waits up to 15 s for "the inherited AI car is cleared and no longer drawn". Compiles; NOT re-run.
- Stand-in fix confirmed in the picture: net_ai_car.png shows the AI abarth with no grey box.

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN.md 7 / 9 N) - net protocol 3
Files: `Game/Net/Core/AcNetWire.cs` (Protocol 3, Used 44, AtDamage 42, AcNetCar.DamageD0/D1, Pack/Unpack ints 42-43 - not motion),
`Core/AcNetBlocks.cs` (MagicAiDamage 'ACD1' 0x41434431, AiDamageHeader 4, AiDamageInts 40, PackAiDamage / UnpackAiDamage /
WriteAiDamageHeader / AiDamageHeaderOk / AiDamageWriterOf), `Core/AcNet.Blocks.cs` (master-written AI damage table in "Ac Net Host 0":
SetAi packs car.DamageD0/D1 there, ClearAi zeroes it, written with the AI blocks only when a pair changed, authority asked for Host 0; ReadAi
decodes it into AiCar.Car.DamageD0/D1 (absent / foreign / gone session = 0); `TakeInheritedAiDamage(index, out d0, out d1)`, `AiDamageTable`),
`Core/AcNet.Master.cs` (TakeOverAsMaster keeps the table and marks non-zero pairs as inherited; rewritten only if a table existed),
`AcNetGame.cs` (AcNetPlayer.DamageD0/D1 + Retired; AcRemoteCar.DamageD0/D1/DamageRig/DamageEvents), `AcNetGame.Own.cs` (FillCar: ToWire
for my car and the master's AI), `AcNetGame.Players.cs` (FillTiming copies the pair for players and AI), `AcNetGame.Cars.cs` (DamageRig:
ApplyWire on the drawn rig when the pair OR the rig changed - late join, Adopt, interior rebuild, stand-in -> model; also while hidden; raises
AcEvents.CarDamaged for the remote rig with TakeChanges), `AcNetGame.Race.cs` (SeedAiDamage: a new master's first own rig of an AI index takes
the inherited pair once, only when pristine). README "protocol 2" -> 3 (lines 114 / 153; the history row stays). Harness csproj now compiles
Data/Physics (AcCarDamage round-trips).
Wire (DAMAGE_DESIGN 7 exactly): d0 = F | R<<8 | L<<16 | Ri<<24 (ZoneByte), d1 = C | rods u4 <<8/12/16/20 | engine % u7 <<24 | retired <<31;
0 0 = undamaged, so DAMAGE 0 writes zeros (the ints never change from the claim's zeros, Host 0 is never written). Player ints 42-43;
AI: Host 0 = 4-int header + 18 x 2 ints by AI index. Protocol-2 records are refused as Foreign (existing mechanism), protocol-2 / other
tables ignored.
Deviation: the design's "write when Version changed" - FillCar calls ToWire every compose (a few int ops, no allocation) because P's
`Retired` (set by R) does not bump Version; AcNetWire.Set only marks changed ints, so nothing extra is sent.
Verified: `cd DevTools/agents/N/harness && dotnet build -c Release -v q && dotnet run -c Release --no-build -- nettest` -> **68 ok, 0 failed**
(was 49): zero record = undamaged (ints 42-43 0, Used 44); pair bit-exact incl. bit 31, not motion; DAMAGE 0 (m 0) hit + over-rev -> 0/0;
AcCarDamage ToWire -> record -> ApplyWire -> ToWire identical (F 61, R 255 cap, rods within 1/15, engine 5 %, retired); same pair again =
no change; blown engine = 100 %; repair -> 0/0 + Repaired; AI table 18 pairs by index, 'ACD1', unchanged pair not dirty, out-of-range 0,
protocol-2 table ignored; protocol-2 record Foreign (header kept); loopback room: B reads A's pair within 1 s (3 ints written), late joiner
C decodes it on the first read, B and C read AI 5's pair from Host 0 (AI 6 = 0/0), after A (master) leaves the new master keeps the table (C
still reads it) and inherits AI 5's pair exactly once. build.sh 0 errors x3, check.sh all 117 proven.
Not verified: the Unity side at run time (DamageRig / ApplyWire on a real remote rig, CarDamaged for remote cars, SeedAiDamage), two real
clients. net-loopback lab not extended (the harness covers the wire).
Player.log: `Net: AI car n (<car>) keeps its damage from the previous master (zones F/R/L/R/C ..., engine %[, retired])`, AcNet log
`inherited the damage of n AI car(s) from the previous master (Host 0).`, `Net: player n (slot k) runs net protocol 2, this build 3: refused`.

## Damage pass 2026-11-02 (integration)
- `AcNetGame.Cars.DamageRig`: a rebuilt rig (cockpit rebuild) that takes the SAME pair still gets ApplyWire (visuals re-pushed by P) but raises no
  second CarDamaged; the first rig of a car and every pair change raise as before.
- Lab `damage-e2e` proves the client half of the round trip in Unity: owner ToWire -> a Kind Remote puppet's ApplyWire -> P's push -> C draws the
  identical ZoneByte state and glass; the puppet re-encodes the identical pair; the same pair again changes nothing; 0 0 = repaired.
  NetTest re-run: 68 ok, 0 failed. Two real clients: still not run.

## Damage pass 2026-11-02 - flow labs (lab/flow agent)
- `net-loopback` extended with damage (Editor/AcLabNet.cs) and PASSED (all iteration-1 checks still pass): DAMAGE 0 -> A's record 0 0 on B, B's
  car pristine, 0 CarDamaged; master A RequestRules DAMAGE 100; B's pair 1400002D/00000800 (front 45, right 20, LF rod 8) decoded by A,
  applied once (CarDamaged 1), re-encodes identically, late joiner C decodes it; A's own rear 30 reaches B, A's repair goes out as 0 0; the
  same pair re-sent changes nothing; the cockpit rebuild keeps front 45 / right 20 with no second CarDamaged; B as master writes AI 0 pair
  00000021/E4000000 to Host 0 -> A decodes it (Retired), the AI rig shows front 33 + engine dead + retired; A takes over and holds the same pair
  in its AiDamageTable. Pictures looked at: net_remote_damaged (headlight / bumper of B's abarth damaged; paint dents absent because the car was
  built at DAMAGE 0 - logged), net_ai_car (rear view; small red specks on the rear bumper - not judged, could be the skin).

## Damage pass 2026-11-02 (fixer)
- Contacts(): per remote car, remoteSpecial = remoteInPit || snapped within SpawnGhostSeconds is written to rig.Physics.PuppetGhostable / the stand-in's AcRemoteContact.Ghostable (independent of the local car), so the master's AI take no damage from just-placed / pit-lane remote cars.
- New Game/Net/AcRemoteContact.cs (MonoBehaviour, plain data) added to every stand-in box (rc.BoxContact): drawn Velocity for P's impact speed.
- AI hand-over seed: TakeOverAsMaster records per inherited index the inherited AI block's CarHash (only Present cars) and the command Seq; TakeInheritedAiDamage(index, carHash, ...) seeds only the same car in the same session (else logs and drops the pair). SeedAiDamage passes AcNetWire.NameHash(rig.Car.Id) (= FillCar's CarHash).
- Verified: build.sh 0 errors, fusionapi check OK. Not lab-run with two real clients.
