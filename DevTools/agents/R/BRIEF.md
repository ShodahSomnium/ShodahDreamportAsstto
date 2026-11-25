# R - Race (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
The session conductor and the racing rules: AcSession flow Hub -> Loading -> Practice -> Grid -> Countdown -> Racing -> Finished ->
Results -> back to Practice; track loading through W; spawning the local car through P's AcCarRig in its pit box / on the grid / at the
hotlap start (raycast down onto the road, heading from the horizontal +Z); start lights (W's meshes + H's panel + A's beeps); jump-start
detection; timing (AC_TIME gates, sectors, lap validity from surfaces, the grid-before-the-line lap rule); positions from fast_lane.ai
progress; pit lane + limiter; finish flag, results, back to practice; reset / back to pits. Master-only actions through N's race control.

## You own
`Game/Race/` (stub `AcRace.cs` - fill it; add `AcTiming.cs`, `AcStartLights.cs` (the sequence; the meshes are W's), `AcSpawner.cs`,
`AcRaceDirector.cs`, ...), `Game/AcGame.Race.cs` (wired), `Game/Shared/AcSession.cs` (+AcRules, AcLap, AcStanding, AcRaceCommand - you own
it; keep the existing members), `Game/Shared/AcLocal.Race.cs`, `Editor/AcLabRace.cs`.

## Port from
- `HALO/Game/Match/HaloMatchControl.cs` (453) + `HaloMatchControl.Load.cs` (236: load sequence with the loading screen and world build),
  `HaloSpawns.cs` (122), `HaloRoster.cs` (112: standings), `HaloMatchNet.cs` (103: master command -> everyone), `HaloAnnouncer.cs` (91: messages).
- `HALO/Game/Net/Core/HaloNet.Master.cs` lines 1-190 (StartGame: master writes map + running number + UTC start; late joiners join) -
  read for the semantics; N implements the transport, you implement `AcRace.Apply(command)`.
- Data: `AcAiSpline.Progress / PointProgress / IsClosed` (lap progress), `AcTrackMarkers` via W's `AcTrackWorld`.

## Spec sections
`research/ac_physics_sound_rules.md` 3 (sessions, server rules, track side, rules to implement) + Critic addendum (no pit limit key).
`research/ac_track_formats.md` 0, 3.2, 5.2-5.3 (progress, sectors, positions, pit lane), 6.4-6.7, and **Critic addendum T1 (spawn
raycast), T2 (heading), T3 (grid before/after the line: race distance rule)**. DESIGN.md "Racing (multiplayer)" + "Iteration-1 user flow".

## Must do (acceptance)
1. `AcRace.Tick(game)`: first call starts `AcSeat` and `AcHud` (already in the stub - keep); drives the phase machine; `Stop()` gets out,
   unloads, forgets. Every call into other modules guarded.
2. Load: `RequestLoad(layout, rules)` (alone or master -> `AcNetGame.RaceControl.Publish(cmd)`; others refused with a toast) and
   `Apply(cmd)` (everyone, also late joiners): `AcSession.Begin`, `SetPhase(Loading)`, `StartCoroutine(AcTrackWorld.Load(layout, progress))`,
   `ReportLoading`, `NotifyTrackBuilt(AcTrackWorld.Root)`, spawn my car (`AcCarRig.Spawn(AcLocal.CarId/SkinId)`) in my pit box (box index
   from my slot / join order, // ours rule), `AcEvents.RaiseLocalCarChanged`, `SetPhase(Practice)`. Unload: `AcSession.Close` then
   `AcTrackWorld.Unload` (seat ForceExit first).
3. Spawn: marker XZ + heading (T2), `AcTrackWorld.TryGround` from marker + 1 m, `rig.Physics.ResetTo(ground, Euler(0, heading, 0))`,
   parking brake / frozen until the player drives. Never at the node height (cars would drop up to 7.7 m on ks_black_cat_county).
4. Race: master START -> command with grid order (practice/qualify best laps, else join order) and StartUtc = now + grid time
   (// ours, e.g. 10 s) -> Grid (cars teleported to AC_START_n, players seated via `AcSeat.Enter`, `Freeze(true)`) -> Countdown (5 reds one
   per second then all out, random 1-3 s hold - // ours, confirm with the user; `AcTrackWorld.StartLights.SetLights`, `AcHud.StartLights`,
   `AcSfx.StartBeep/GoBeep`) on the shared clock -> green: `Freeze(false)`, `AcSession.NotifyRaceStarted` -> jump start (moved > 0.5 m
   before green, // ours) = +5 s penalty (JumpStartPenalty 2 mapped to +5 s, // ours) and "FALSE START!" message.
5. Timing (`AcTiming`, own car authoritative): gate crossing detection between physics poses (segment vs gate line, race direction from
   the gate Forward), sectors, lap time, last/best/delta, validity (more than `Rules.AllowedTyresOut` wheels on IS_VALID_TRACK=0 surfaces =
   cut; -1 disables), the T3 rule (race distance = laps + progress, initialised at green; a crossing counts only when the distance passed
   the next integer), `AcSession.NotifyLap`, write `AcLocal.*` race fields (N publishes them).
6. Positions / standings: race by race distance (mine + `AcNetGame.Players[i].RaceDistance` + AI), practice/qualify by best valid lap;
   `Standings` list reused (no per-frame allocation). Finish: leader completes `Rules.Laps` (or time + finish the lap; OVER_TIME_MULT 1.2) ->
   Finished; others finish their lap; `RaceOverTimeSeconds` closes -> Results (`NotifyRaceFinished`) -> back to Practice after ~30 s (// ours).
7. Pit lane: `InPitlane` from the surface under the car (IS_PITLANE) or pit_lane.ai distance; limiter `Rules.PitLimitKmh` (80, // ours) via
   `AcLightState.PitLimiter` / a P assist request (ask P in REQUESTS.md for `AcCarPhysics.SpeedLimitKmh`).
8. `BackToPits()`, reset (`Input.ResetRequest` -> nearest AI-line point, heading along the line), `ChooseCar` respawn; master hand-over:
   a new master keeps publishing from the newest command (N's rule) - verify by reading the code path, describe in NOTES.md.

## Player.log lines to add
`Race: load <key> (<rules>) by <master|me>` · `Race: track built in X s, my pit box n` · `Race: grid: n cars, pole <name>, start at UTC` ·
`Race: lights out (+jump starts)` · `Race: lap n <time> valid|cut (sectors ...)` · `Race: finished P<n> <total>` · `Race: results: ...` ·
`Race: back to practice` / `Race: hub`.

## Verify
build.sh x3, check.sh; harness probe `Cmd_racedist` (T3 rule on the grid progress of all 35 layouts: first crossing counted / not counted
correctly); lab `race-solo-magione`: load, spawn (shot of the car on the road in its pit box, not floating), START alone, lights sequence
(log), drive a scripted path across gate 0 twice (move the rig by ResetTo along the AI line) -> one lap counted with the right validity.

## Consumes / provides
Consumes: AcTrackWorld (W), AcCarRig/AcCarPhysics (P), AcSeat (S), AcHud (H), AcSfx (A), AcNetGame.RaceControl/Players (N), AcAi (I),
AcContent (D). Provides: AcSession (+events) for everyone, AcRace (F's tablet actions, N's Apply), Standings (H, F), AcLocal race fields (N, H).
