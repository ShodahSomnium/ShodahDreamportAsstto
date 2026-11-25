# R - Race: notes (2026-11-01, second pass)

## Files (all mine)
- `Game/Race/AcRace.cs` - public face (contract members kept) + Tick / Apply / requests. ADDED: `RequestStart(AcRules)`, `RequestRules`,
  `JoinSession`, `DefaultRules()`, `Defaults`, `CanCommand`, `Command`, `PitBox`, `Spectating`, `LightsReds/LightsGreen`, `SessionTimeLeft`,
  `RaceElapsed`, `QualifyActive`, `FirstFinishUtc`, `AiTimer(index)`, `NameOf(id)`, **`ResetCar()`** (VR tablet reset), **`OpenCourse`**.
  `RequestHub()` for a non-master = leave alone; a non-master `RequestLoad` of the instance's layout = `JoinSession`.
- `Game/Race/AcRace.Load.cs` - load (W), my car (P's AcCarRig.Spawn, Kind Local), pit box pick, teardown, hub. Post-build steps and the
  session entry are wrapped: a throw -> LoadFailed / Practice, never stuck on the loading card.
- `Game/Race/AcRace.Flow.cs` - practice / qualify / hotlap, race (grid, lights, jump start, green, finish), results, reset, pit limiter
  (P's `SpeedLimitKmh`), FUEL_RATE / TYRE_WEAR_RATE -> P, allowed-car enforcement, AI glue (EnsureAi before the grid, benched AI, seeding).
- `Game/Race/AcRaceClock.cs` (NEW) - instance clock: master beacon in N's Race 0 block, `AcSession.ClockOffset` on the others.
- `Game/Race/AcRaceDirector.cs` - master decisions from command + clock (hand-over safe), grid order (qualify > best lap > join order; AI after).
- `Game/Race/AcTiming.cs` - Unity side of timing (gates -> AC space, open courses, wheel surfaces, pit lane, AcLocal fields, lap events,
  remote laps with sectors + cuts, remote AI laps on non-masters).
- `Game/Race/AcStandingsBoard.cs` - standings (pooled): me, players (with their PenaltyMs), the master's AI (own timers on the master, N's
  AiPlayers elsewhere), `ApplyResults` (the master's published final order).
- `Game/Race/AcSpawner.cs` - placement (T1 ray down, T2 heading), player beside the car, occupancy (N's `Occupies/Frame`: stand-ins count).
- `Game/Race/AcRaceMath.cs`, `AcRaceTimer.cs` - **Unity-free** race core. ADDED: open-course mode (`SetupOpen`, `IsOpen`, `SectorCount`),
  `Seed(...)` (hand-over carry-over).
- `Game/Race/AcRaceDefaults.cs` - server/cfg/server_cfg.ini + **server/presets/SERVER_00/server_cfg.ini** (QUALIFY_MAX_WAIT_PERC 120,
  START_RULE 0 -> JumpStartPenalty 0) + system/cfg/assetto_corsa.ini (OVER_TIME_MULT 1.2).
- `Game/Shared/AcSession.cs` - ADDED `AcRaceCommand.PhaseUtc`, `ToString`, AcStanding fields; **`LocalUtcNow`, `ClockOffset`,
  `SetClockOffset`** (`UtcNow` = local + offset = the master's clock).
- `Game/Shared/AcLocal.Race.cs` - ADDED `FinishedMs, PenaltyMs, Cuts, LapCuts, OutLap, WrongWay, Progress, LastSectorMs, GridSlot, CommandSeq`.
- `Editor/AcLabRace.cs` - lab `race-solo-magione` + **`race-solo-actions`** (NOT run - lab phase later).
- `DevTools/harness-ac/ProbeR.cs` (probes `racedist`, `racerules`, `racegates`, **`racea2b`**, **`raceseed [laps]`**; real code only under
  AC_RACE_CORE) + harness copy `DevTools/agents/R/harness/AcHarnessR.csproj`.

## Model
Master publishes AcRaceCommand (N carries it): Practice (load / back to practice / qualify-hotlap start: StartUtc = session start, 0 =
untimed), Grid (START: Grid ids pole first, StartUtc = lights out), Results (PhaseUtc), Hub. Every client applies the newest Seq
(RaceControl.Changed + per-frame poll, idempotent). Countdown / Racing derive from StartUtc on the shared clock (`AcSession.UtcNow` =
the master's clock, see Clock); Finished is local. Lights: first red at StartUtc - hold - 4 s, one red per second, hold 1-3 s derived from
StartUtc's ms value (same on every client). Master hand-over: the director only reads the newest command + clock (Results end = PhaseUtc +
30 s, race close = first finish + RACE_OVER_TIME, all finished, qualify end + QUALIFY_MAX_WAIT_PERC of the fastest lap), so the new master
just carries on; a re-published identical race does not re-grid (session key = StartUtc). AI on hand-over: non-masters call
`AcAi.Observe()` and keep the master's AI timing (N's AiPlayers) per index; the new master runs EnsureAi at once (I adopts the cars), each
AI race timer begun after the green starts at the green's clock and is `Seed`ed (same laps / distance / finish).
- Clock: the master publishes a beacon once a second (Race 0 `Flags` = bit 30 + its clock ms mod 2^30, plus lights/session/leader times);
  others take beacon - own clock as samples, `ClockOffset` = max of the last 8 (only under-estimates by the delivery delay: a client never
  sees the green before the master). A new master keeps its offset (continuity).
- Results: master publishes its final standings (`AcNetGame.PublishResults`) on entering Results; non-masters order by
  `AcNetGame.Results` when `ResultsSeq` == the Results command's Seq (else their own after 3 s, re-ordered if the master's arrive later);
  `RaceFinished` is raised once the table is final.
- Timed race (LAPS 0): at time-up only the leader is flagged; everyone else finishes at the first crossing after the leader finished
  (research 3.1). RACE_EXTRA_LAP (acServer key, no install value) = 0.
- Open courses: trento-bondone = AC_AB_START -> AC_AB_FINISH (AC_TIME_0 is a split -> 2 sectors); ks_drag = standing start (timed: moved
  0.5 m from where the car was put; race: the green) -> AC_OPEN_FINISH. One run = one "lap"; a race is one run (LAPS ignored).

## Verified (offline)
- build.sh: my files 0 errors / 0 warnings (other modules - C, P - had transient errors in the last run); check.sh on server_like.dll:
  "all 117 Fusion/Somnium members are proven".
- `racedist` (real install, ALL 35 owned layouts): 35/35 OK, 661/661 grid slots (T3 rule, sectors, cut rule, out lap, reverse over the line).
- `racea2b` (NEW): 6/6 open-course layouts: drag200/400/500/1000/2000 (finish 200.1/399.7/500.0/1000.1/2000.1 m from AC_START_0) and
  trento-bondone (finish at the open line's end, split at 0.518): race from the grid = 1 run + Finished, timed run = start (gate / move-off)
  + all splits + finish, sector sums = run time. Pit boxes there are at progress 0 (behind the start gate).
- `raceseed` (NEW): 28/28 closed layouts, seeded at 0.6, 1.37 and 2.5 laps: a seeded timer counts the same laps at the same crossings and
  finishes at the same crossing with the same FinishedMs; the partial lap's time is not recorded. Found + fixed by it: Begin's grid-window
  search gave a wrong progress for a car seeded mid-lap (Seed now re-projects on the whole line).
- `racerules`: cut rule table, lights timeline, formats.

## NOT verified (needs the lab / two clients)
- Everything Unity-side at run time (both lab scenarios written, not run): load, spawn on the road, seat on the grid, lights on W/H/A,
  reset, back to pits, ChooseCar respawn, pit limiter light, hub teardown, results flow.
- Net: the clock beacon (offset sign / magnitude with two PCs), results agreement, AI hand-over seeding, remote AI rows, remote penalties.
- Cut rule count [BELIEF: > ALLOWED_TYRES_OUT wheels], pit limiter speed (80, no key in the install), light timing: compare with AC.
- Drift layout: gate-only timing.

## Open (not implemented, by choice or waiting)
- PENALTIES=1 repeated-cut penalties and surface BLACK_FLAG_TIME (`AcSurface.BlackFlagTime` exists; research 3.4 is a BELIEF) - not done.
- `[PRACTICE] TIME=10` is read and logged only: practice stays open-ended (the instance has no session rotation); a timed practice is START
  with Session Practice.
- N's `SetPenalty/PenaltyOf` unused on purpose (penalties are owner-computed and travel in each record's PenaltyMs).
- F: RESET CAR button (`AcRace.ResetCar`), JOIN SESSION for non-masters, lobby base = `AcRace.DefaultRules()` (requested "R -> F").
  Today F's lobby starts from `new AcRules()` (JumpStartPenalty 2) while the install default is START_RULE 0 (locked until green).
- I: let AI drive through the finish on open courses (trento's finish = line end; requested "R -> I").
- H: hide "LAP n/N" on open courses (`AcRace.OpenCourse`; "R -> H").

## Lab steps (run later, slot A or B)
`bash DevTools/lab.sh race-solo-magione` (optional `-actrack monza -accar ferrari_458`). Look at race-pitbox.png (car on the road beside
the pit wall), race-grid.png (car on the pole box, gantry ahead), race-lights.png (3 lamps lit), race-back-in-pits.png.
`bash DevTools/lab.sh race-solo-actions` (same options): checks the START_RULE default, FUEL/TYRE rates in P, ChooseCar respawn +
LocalCarChanged(null, new), pit lane (InPitlane, SpeedLimitKmh, PitLimiter light; skipped with a note if P gives no wheel contacts under
ResetTo), ResetCar (12 m off -> on the line, 1.5 s rate), the R-key path, BackToPits, seat on the grid (AcSeat.Seated/SeatedCar/IsDriver),
jump start with JUMP_START_PENALTY 2 ("FALSE START!", PenaltyMs 5000), RequestStop, RequestHub (car disposed, track unloaded, hub).
Look at race-actions-newcar.png. Optional: `-actrack ks_drag -aclayout drag400` / `trento-bondone` with race-solo-magione's practice part
to see open-course timing (`Race: timing drag ...` / `point-to-point ...` lines).

## Player.log lines
`Race: conductor up` · `Race: defaults laps 5, ..., QUALIFY_MAX_WAIT_PERC 120, START_RULE 0 -> jump start penalty 0, [PRACTICE] TIME 10 ... (server/cfg/server_cfg.ini + server/presets/SERVER_00/server_cfg.ini + system/cfg/assetto_corsa.ini)` ·
`Race: clock: the master's clock is +N ms from mine (k beacon sample(s); ...)` / `Race: clock: I am the master now - keeping the instance clock` ·
`Race: command seq n <Phase> <track> (...)` · `Race: load <key> (<rules>) by me|master <name>` · `Race: joining the instance's session on <key> (track pick by a non-master)` ·
`Race: track built in X s, my pit box n (of N), grid N slots (pole before|after the line), ..., open course` ·
`Race: timing circuit|point-to-point (...)|drag (...), G gate(s) (S sectors), ...` · `Race: my car <id> (<skin>) ready in X s at pit box n` ·
`Race: my car <id> is not allowed by the session's rules (...) - switching` · `Race: AI field n car(s)` · `Race: AI <name> has no grid slot (...) - sits this race out` ·
`Race: grid: n cars, pole <name>, start at UTC hh:mm:ss.fff ...` · `Race: seating the driver on the grid ok|refused` · `Race: start lights: 5 reds 1.0 s apart, hold X ms ...` ·
`Race: jump start (moved X m before the green)` · `Race: lights out, jump starts n` · `Race: pit limiter on (80 km/h, ours ...)` ·
`Race: lap n m:ss.fff valid|cut (k) (sectors ...) [best]` · `Race: AI <name> timing carried over from the previous master (lap n, ...)` ·
`Race: chequered flag - the leader finished (<name>)` · `Race: finished P<n> <total> (...)` · `Race: master: <why> -> Results|Practice` ·
`Race: results (race, mine, published|the master's order|mine - the master published none in 3 s): P1 ...` · `Race: the master's results arrived late - table re-ordered` ·
`Race: back to practice` · `Race: leaving the track (<why>)` · `Race: preparing the built track failed` · `Race: hub (...)`.

## Numbers that are ours (judge them)
grid time 10 s; 5 reds 1 s apart; hold 1-3 s; jump start > 0.5 m; JUMP_START_PENALTY 2 = +5 s; green shown 3 s; results 30 s; results wait
3 s (non-master); pit limit 80 km/h; pit lane = >= 2 wheels on IS_PITLANE (leave after 0.5 s with none); lap tolerance 0.1 lap; gate margin
8 %, gate height 8 m; reset rate 1.5 s; drag move-off 0.5 m; clock beacon 1 s / 8 samples; player 2.2 m beside the car; occupied box 3.5 m;
late-for-grid 5 s; stale remote finish 15 s; AI behind players on the grid; standings 10 Hz.

## Lead integration changes (2026-11-01)
- AcRace.Flow.cs: failed AI drivers (State Failed) get no timing row; JumpWatch lights the drag / A2B tree DQ lamps (`StartLights.SetJumpStart(true)`), cleared on a new race. AcRaceDirector.cs: failed AI drivers get no grid slot.
- Session time / weather stays per player (REQUESTS "W -> R, N": deferred).

## Lab phase (2026-11-01, R agent) - race-solo-magione PASS, race-solo-actions PASS
- `race-solo-magione` (abarth500, magione; run 3 of 3) **passed**: pit box 0 (XZ 0.00 m, body 0.52 m over the road, heading = marker, still after 3 s),
  practice out lap -> 1 timed lap 0:11.693 valid (scripted 210 m/s), grid slot 0 (frozen, on the marker), lights 1..5 reds 1.0 s apart, hold 1041 ms,
  lights out -> Racing + RaceStarted, grid before the line: first crossing no lap, laps 1/2 valid, `finished P1 0:23.557`, master -> Results,
  RaceFinished, results 30 s -> Practice, car back in pit box 0. Pictures looked at: race-pitbox / race-back-in-pits (abarth beside garage "19"),
  race-grid (on the "1" box, marshal, gantry), race-lights (road to the gantry; lamps only a few pixels far ahead - lamp state NOT visually verified).
- `race-solo-actions` (run 2 of 3) **passed**: START_RULE 0 default, FUEL/TYRE rates in P, ChooseCar respawn (LocalCarChanged null -> new, abarth500_s1 in
  the box, picture looked at), seated (lab stand-in body) -> pit lane entered + limiter 80 + PitLimiter light, off again on the track; ResetCar 12 m -> 0 m,
  1.5 s rate, R-key path; **cut sensing**: 4 wheels on GRASS (IS_VALID_TRACK 0) 4 m off the edge; BackToPits; seated on the grid; 2 m jump start ->
  FALSE START! + PenaltyMs 5000; RequestStop; RequestHub (car disposed, track unloaded, seat released). No exceptions of ours in the logs.
- Lab fixes (my lab file only, no product change was needed): (1) the scripted drive moved per FRAME (batch runs ~2500 fps, physics 50 Hz) -> ~200 m
  between two timer steps, the chord-limited race distance lost ~12 %/lap and no lap counted; now one move per physics step. (2) After a FAIL the lab
  stopped at the next `if (r.Failed) yield break`; grid waits now test the phase. (3) Editor has no Bridge body: stand-in `AcSeat.LabRoot` (as S's lab).
  (4) F's lobby re-seed 1 s after boot published the lobby defaults over the lab's session (REQUESTS "R -> F: a lobby re-seed ..."): labs wait for
  it and copy their rules into `AcLobby.Rules` (`AcLabRace.LobbySeeded/SyncLobby`, also used by I's ai-magione).
- Not exercised: the cut rule invalidating a timed lap in Unity (sensing yes, rule = harness racerules), timed race / qualify, open courses, net.

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3, check.sh all 117 proven; no lab run)
- Apply / PollCommand compare (Seq, MasterId) like N: an offline seq 1 (master -1) no longer shadows the room's seq 1.
- AnyFinished counts only timers begun for this race (Begun, not Benched, Mode Race); EnterRace (new race) and benching clear stale
  Finished / FinishOnNextLap - no instant chequered flag after START from Results.
- AcRaceTimer.Teleported (race, closed course): a reset that lands just past the line completes the lap there (invalid), reported with the
  next Step's bits (LapDone / FinishedNow) - Laps no longer lags RaceDistance by one for the rest of the race.
- Gap behind a finished leader uses the running car's RaceDistance (no "+1 LAP" on the lead lap).
- Timed practice (START, Session Practice, TIME > 0): AcRace.TimedPracticeActive / TimedSessionActive drive the clock, the lap cutoff, the
  director's end (-> Results) and a "PRACTICE - N MIN" message; its results are not remembered as a qualify order.
- Standings keep every gridded player's last timing (per race StartUtc); a racer who leaves mid-race stays as a row (DNF n laps, or finished)
  and is published in the results. ApplyResults(results, cmd) keeps rows without a grid slot Spectating; AcLocal.Position 0 when spectating.
- Local car spawn: the build runs in its own coroutine (StepSpawn) with an AcCarSpawnHandle; a respawn bumps _spawnGen and Teardown abandons
  it - a superseded build is cancelled and drained (C disposes the half-built car) instead of StopCoroutine'd. SpawnTimeoutSeconds still applies.
- Not verified in a lab.

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN.md 1.4 / 1.6 / 4 / 9 R)
Files: NEW `Game/Race/AcCarDamageGate.cs` (`TexturesWanted`, `VisualOn` = setting `cars.visualdamage` default on, `Refresh(rules)`, `Reset`),
NEW `Game/Race/AcRace.Damage.cs` (rate, pit-box repair, retire); changed `AcRace.cs` (DamageTick, BackToPits repairs + allowed when retired,
Stop), `AcRace.Flow.cs` (ApplyRates -> ApplyDamageRate, gate refresh on every command, repairs at new session / back to practice / grid /
spectating, ClearRetired, retired car not timed), `AcRace.Load.cs` (gate refresh before my spawn, cancel repair on car removal),
`AcRaceDefaults.cs` (DAMAGE_MULTIPLIER from server_cfg.ini, fallback 100 // ours = AC server default), `AcRaceDirector.cs` (retired = done;
a race where every present racer retired closes without a flag), `AcStandingsBoard.cs` (AcStanding.Retired, ranked after running /
finished cars), `AcTiming.cs` (AcCarTiming.Retired), `Game/Shared/AcSession.cs` (AcStanding.Retired, AcRules doc + ToString damage),
`Editor/AcLabRace.cs` (lab `race-repair`).
Public API (additive): `AcRace.Repair(AcRepair)`, `RepairActive`, `RepairSecondsLeft`, `RepairTotalSeconds`, `RepairWhat`, `CanRepair(what, out
why)`, `RepairEstimate(what)`, `NeedsRepair(what)`, `InPitBoxStopped`, `LocalRetired`, `RepairSound` (Action<AcCarRig,bool>, A plugs it -
done by A), `AcCarDamageGate.*`, `AcStanding.Retired`, `AcCarTiming.Retired`, `AcRaceDefaults.DefaultDamageMultiplier`.
Rules as built:
- Rate: ApplyRates (local car each frame, the master's AI in AiTimingTick / AiGridTick) writes `DamageRate = clamp01(DAMAGE_MULTIPLIER/100)`;
  a car going >0 -> 0 is repaired (Repair(All), retired bit cleared, logged once). AcCarRig already sets it at spawn. AcAiField (I) not touched.
- Gate: refreshed on every applied command and before my spawn; N's / I's later spawns see the same static. A 0 -> >0 change is logged once
  ("paint dents after the next spawn").
- Repairs: BACK TO PITS (full, instant; also cancels a running repair), every new session key / back to practice (my car + the master's AI),
  GridPlace (every car placed on the grid), spectator parked in the pits. NEVER in PlaceInPits / ResetTo (R key, fall-through, no-line, jump
  start teleport keep damage). A new rig (car change, respawn) is clean by itself.
- Pit-box repair: allowed when my car is < 1 km/h (// ours) within AcSpawner.OccupiedMetres (3.5 m, existing // ours) of MY AC_PIT marker, any
  session except Grid / Countdown, not spectating, not retired, and the part is damaged (Suspension implies Body). Freeze(true), timer =
  Damage.RepairSeconds (sum of parts), RepairSound(start) -> at the end Physics.Repair(what), RepairSound(end), Freeze(false), "REPAIR DONE".
  The freeze is re-asserted each frame (session code may unfreeze). Cancelled (nothing repaired) by a session-key change, BACK TO PITS, car
  change / removal, Stop. Messages "REPAIRING - n S" / "REPAIR DONE" (ours) besides H's countdown.
- Engine dead in Racing / Finished: my car -> AcCarTiming.Retired + Damage.Retired (wire d1 bit 31), "ENGINE BLOWN - RETIRED" once (our text;
  H should not add a second "ENGINE BLOWN" in a race), timing stops, the car is frozen once below 1 km/h (// ours); BACK TO PITS allowed
  (repairs, stays DNF, re-frozen in the box). The master's AI likewise (I parks it). Remote players: `AcNetPlayer.DamageD1 < 0`. Standings:
  Retired rows after running / finished ones, results text "DNF n laps (retired)". Cleared at a new race / session.
Verified: build.sh 0 errors x3 (warnings are SceneNetworking / AcCooker, not mine); check.sh all 117 proven; R's harness builds.
Lab `race-repair` (see "Damage lab" below for the result). Not verified: multi-client retired rows, a repair interrupted by a real session
change in a lab.
Numbers ours: repair stop 1 km/h, retire hold 1 km/h, DAMAGE_MULTIPLIER fallback 100, pit-box radius = the existing 3.5 m.
Player.log: `Race: damage textures ON|off for cars built from now on (DAMAGE_MULTIPLIER n %, cars.visualdamage on|off)`, `Race: pit-box
repair <what> started (x s; body %, suspension %, engine %)` / `done` / `cancelled (<why>)`, `Race: my car repaired (back to pits | grid |
new session | back to practice)`, `Race: my engine blew up - retired (DNF, lap n)`, `Race: AI <name> engine blew up - retired`, `Race:
DAMAGE_MULTIPLIER set to 0 mid-session - damage off, own cars repaired`, `Race: master: every racer retired (engine blown) -> Results`,
`Race: defaults ..., DAMAGE_MULTIPLIER 100, ...`.
Note for F: a lobby saved by iteration 1 keeps `lobby.damage` 0 in the player's settings (AcLobby reads the saved value over the new default).
Damage lab (2026-11-02, 1 run, slot A): `race-repair` (ferrari_458, magione) **PASS, 0 FAIL**: lobby default DAMAGE_MULTIPLIER 100 (server_cfg);
DamageRate 1, gate on; front 60 + LF rod -> body 100 % / susp 100 % -> BODY + SUSPENSION estimate 500.0 s (20 x 10 + 30 x 10) = RepairSeconds;
engine row refused (nothing to repair); repair frozen exactly 500.00 s, then pristine + free, 1 Repaired event; R reset kept rear 40; no repair
outside the box; BACK TO PITS repaired; RequestRules DAMAGE 0 -> rate 0 + repaired, a hit adds nothing; race: right-50 car clean on the grid;
forced dead engine after the green -> retired, "ENGINE BLOWN - RETIRED", standings Retired, no pit repair, director "every racer retired" ->
Results ("P1 You DNF 0 laps (retired)"), 30 s -> practice repaired, not retired. Picture looked at: race-repair-damaged.png (458 in box 19 from
behind; no damage visuals visible from that angle - C's part). Observation for the user: a full suspension repair is 300 s on every car
(car.ini SUSP 30 s per 10 %), so BODY + SUSPENSION after one hard hit takes minutes - the design's % definitions (// ours, 1.5) decide it.

## Damage pass 2026-11-02 - flow labs (lab/flow agent)
- New scenario `race-damage-live` (Editor/AcLabRace.cs, `DamageLive`): magione practice DAMAGE 100, car chosen through F (AcFront.ChooseCar).
  (1) pristine roll vs roll after front 60 + LF rod: the car's own flutter voice 0.0000 -> 0.204 (P's live MaxSuspDamage01, not A's override);
  the bent rod steered the 30 km/h roll into the pit building (front 60 -> 73). BACK TO PITS = pristine both times.
  (2) seated (S stand-ins, monitor): live front 12 -> H's displayer from the real CarDamaged, hidden after TIME 4 s.
  (3) seated REPAIR BODY 40.0 s (= RepairSeconds): frozen, H countdown 'REPAIRING  39 S', A's unscrew x1 / screw x1 through RepairSound,
  pristine + released, "REPAIR DONE". PASSED (run 2; run 1 failed only the "no displayer" check: the roll's own wall hit was < TIME s old).
- Lab capture gap: the seated MONITOR overlay does not come out in r.Shot (LabFrame plane shows only where the pit building is behind it, even
  parented to the camera); state checks pass, desk pictures live-dmg-*-desk.png show no HUD. The hub desk shot (hud-damage) is the visual proof.
- Regression `race-solo-magione` PASSED (2026-11-02, 2 laps, results, pit box after the race).

## Damage pass 2026-11-02 (fixer)
- AcRace.Flow EnterRace: the late-start branch (now - start >= LateGridSeconds) of a NEW race now calls RepairCar(LocalCar, "new race (late start)") after PlaceInPits (design 1.6: every session starts clean). PlaceInPits itself still never repairs.
- AcCarDamageGate.Refresh calls AcMaterials.WarmDamageGlass() whenever damage textures are wanted (W decodes DAMAGE_GLASS.dds on a worker, not at the first glass break mid-race).
- Verified: compiles (build.sh 0 errors). Late-start path not lab-run (rare; needs a >5 s late grid command).
