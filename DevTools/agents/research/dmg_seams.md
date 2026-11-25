# Car damage - code seams (lead-architect view)

Research topic "seams": where damage plugs into the iteration-1 code, per module, with owners (AGENTS.md "Ownership"). Read-only research,
2026-11-02. Sibling files (damage model, visuals, mechanics) may override the MODEL details; this file is about WHERE things go.
Tags: [VERIFIED in install] = read from the install / harness run here; [VERIFIED in code] = read in our tree (file:line at the time of
writing); [DOCUMENTED: url]; [INFERRED] = my reading, not checked in the game. Probe: `DevTools/harness-ac/ProbeDmgSeams.cs`
(`dmgsnd`, `dmgart`).

## 0. What the install says about AC's damage (only what a seam needs)

| Fact | Source |
|---|---|
| acs.exe key names (UTF-16 strings, names only): `DAMAGE_ZONE`, `MAX_DAMAGE`, `SCRATCHES`, `OSCILLATIONS`, `OSCILLATION_AXIS/MIN_ANGLE/MAX_ANGLE`, `/data/damage.ini`, `DAMAGE_GLASS_FRONT/LEFT/RIGHT/REAR/CENTER`, `GLASS_THRESHOLD`, `damageZones`, `glassDamage`, `bodyDamageLevel`, `engineDamageLevel`, `VISUAL_DAMAGE`, `SHOW_DAMAGE/HIDE_DAMAGE`, `DAMAGE_DISPLAYER`, `MAX_DAMAGE_KMH`, `RPM_DAMAGE_K`, `TURBO_DAMAGE_K`, `BODY/ENGINE/SUSP_REPAIR_TIME_SEC`, `BUT_REPAIR_BODY/ENGINE/SUSP`, `REPAIR BODY`, `REPAIR BODY + SUSPENSION`, `REPAIR ENGINE`, `SELECT DAMAGE REPAIR`, `QM_HEADER_REPAIR`, `EXPLOSION`, `FLAT L`/`FLAT R`; ASCII: `AVDamageDisplayer`, `AVVisualDamageManager`, `Damage zone %d: %f`, `ERROR: DAMAGE ZONE %S NOT FOUND`, `Sending ACP_DAMAGE_UPDATE`, `suspension_damage` | [VERIFIED in install] acs.exe strings (perl UTF-16 scan) |
| AC multiplayer sends damage as its own client message `ACP_DAMAGE_UPDATE` ("Sending ..." on the client): damage is **client(owner)-computed and relayed**, not server-simulated | string [VERIFIED]; meaning [INFERRED] (AssettoServer source not reachable here) |
| Kunos' server-plugin example reports `ACSP_CE_COLLISION_WITH_CAR = 10` / `_WITH_ENV = 11` with car id, other car id, **impact speed (float), world pos, rel pos (car-relative)** | [VERIFIED in install] `sdk/dev/acRemoteServerUDP_Example/.../ACSProtocol.cs:26-27`, `Program.cs:303-321` |
| `system/cfg/assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD=0.65` | [VERIFIED in install] |
| `system/cfg/damage_displayer.ini [MAIN] TIME=4 MAX_DAMAGE_KMH=60 POSITION_X=20 DISTANCE_FROM_CENTER_Y=-200 PRINT_VALUES=0`; user `Documents/Assetto Corsa/cfg/gameplay.ini [DAMAGE_DISPLAYER] IS_ACTIVE=1` | [VERIFIED in install] |
| HUD damage art `content/texture/damage/*.png`: white-on-alpha 512x512 layers that stack into a top-down car: `front rear left right` (body panels), `engine`, `gearbox`, `sus_fl/fr/rl/rr`, `tyre_fl/fr/rl/rr`, `flatspot_fl/fr/rl/rr`; plus `center.png` 256x256 (cockpit outline) which is **not named in acs.exe** (likely unused). AC tints each layer by its damage | [VERIFIED in install] `dmgart` composite looked at (bboxes: front 176,94..330,128; rear 178,377..332,419; left x177-190; right x319-332; engine 235,124..274,179; gearbox 238,176..272,237) ; tinting [INFERRED] |
| Pit-stop repair art `content/gui/pitstop/repair_body_ON/OFF.png`, `repair_engine_*`, `repair_sus_*`, `repair_susp_*`, `operating_repair.png`; `system/cfg/pitstop.ini` (layout of AC's pit app) | [VERIFIED in install] |
| car.ini `[PIT_STOP] BODY_REPAIR_TIME_SEC / ENGINE_REPAIR_TIME_SEC / SUSP_REPAIR_TIME_SEC` (458: 20 / 2 / 30, "per 10 % damage") | ac_physics_sound_rules.md 1.2 [READ] |
| Damage-related car keys already found: engine.ini `[DAMAGE] TURBO_BOOST_THRESHOLD TURBO_DAMAGE_K RPM_THRESHOLD RPM_DAMAGE_K`; suspensions.ini `[DAMAGE] MIN_VELOCITY GAIN MAX_DAMAGE`; aero.ini `ZONE_<FRONT/REAR/LEFT/RIGHT>_<CL/CD>`; drivetrain `[DAMAGE] RPM_WINDOW_K`; damage.ini (`[SCRATCHES]`, `[OSCILLATIONS]`, `[DAMAGE] INITIAL_LEVEL`, `[VISUAL_OBJECT_n] NAME ... DAMAGE_ZONE MIN_SPEED FULL_SPEED ...`) in all 86 cars | ac_physics_sound_rules.md 1.3/1.5/1.7/1.9, ac_car_formats.md, formula_k damage.ini [VERIFIED in install] |
| `system/cfg/engine_smoke.ini [TRIGGERS] ENGINE_LIFE=25`, `[LIFE_TIME] SECONDS=15` (engine smoke below an engine life); `sparks.ini` (scrape sparks); `physics.ini [TYRES] FLAT_SPOT_K=0.15 FLAT_SPOT_FF=0.05` | [VERIFIED in install]; semantics [INFERRED] |
| Assists: `cfg/assists.ini DAMAGE=0..100` (mechanical damage %), `VISUALDAMAGE=0/1`; server `DAMAGE_MULTIPLIER=100` (server_cfg template) | [VERIFIED in install] |
| Shared memory (for naming only): `carDamage[5]` = front, rear, left, right, centre; `suspensionDamage[4]` | [DOCUMENTED: https://docs.google.com/document/d/1WzqMLkW2o_C0LGcvdMRelAV31ZIifux0CSHD9k6ddz0/export?format=pdf] (unofficial, says 0..1 - for AC1 the raw zone value is probably in km/h-like units, cf. MAX_DAMAGE_KMH=60; siblings must settle the unit) |
| Sound: every one of 86 car banks has `event:/cars/<id>/wheel` with params `[suspension_damage, speed, inflation, brake]`: `flutter_4` loop on suspension_damage 0..1 (bus dB 0.44 -> -42, 0.75 -> -0.17; speed 5 -> -42, 30 -> 0), `flat_tyre_mono` loop on inflation 0..0.1, `tyre_explosion` one-shot on inflation 0..0.05. common.bank: `collisions/car/hit` (car_hit_2 0.6-40, car_hit 40-100, crash3 100-1000 km/h impact_speed), `collisions/track/hit` (hit 4-20, hit2 20-70, crash2 70-150, crash3 150-1000), `collisions/car/scrape` (flutter_1), `collisions/track/scrape` (scrape3), `collisions/object/hit` (cone1..), `common/screw` (tyre_screw1-3), `common/unscrew` (wrench1-4). **No glass-break, no engine-blow event in any bank** (only turbo blowoff / s1_flutter samples) | [VERIFIED in install] `dmgsnd` census (common + 86 banks), `sndevents ferrari_458/common` |

## 1. Collisions - where impacts are seen today

### 1.1 The only impact source: `AcCarPhysics.Impact` (P) [VERIFIED in code]
`Game/Physics/AcCarPhysics.cs:888-906`:
- `OnCollisionEnter` -> `Impact(c, true)`: kmh = |relativeVelocity . contact0.normal| * 3.6, raised if >= 3 km/h (// ours).
- `OnCollisionStay` -> every >= 0.1 s `Impact(c, false)`: kmh = |relativeVelocity| * 3.6 (sliding), >= 5 km/h.
- Returns early when `!_ready || Puppet || Body == null || Body.isKinematic`: **parked, frozen and puppet cars never see impacts**.
- Uses `collision.GetContact(0)` only; raises `AcEvents.RaiseCarImpact(rig, kmh, cp.point)` (`Game/Shared/AcEvents.cs:23-25,60`; owner lead).
  The event carries NO normal, NO other body, NO impulse, NO "which of my colliders" - A re-derives car-vs-track with
  `AcCarAudio.HitsCar` (OverlapSphere 0.6 m on the Car layer, `AcCarAudio.cs:213-226`) and fixes `impact_angle` at 90 (`:199`, `:422`).
- Colliders of a car (`BuildColliders`, `:177-234`): C's collider.kn5 hull (`Model.Hull`, convex MeshColliders, layer Car, body material
  friction 0.3), else one "Ac Body Box"; plus colliders.ini `GROUND_ENABLE` floor boxes "Ac Floor n". **Wheels are rays** (`CastWheel`,
  `:691-728`) - a wheel never collides; a wheel-against-wall hit shows as a hull/floor contact near that corner (whether collider.kn5
  covers the wheels is car-dependent - unchecked [INFERRED]).
- Recovery / resets: `ResetTo` (`:367-392`, spawn, grid, back-to-pits, reset, fall-through recovery `:863-886`) zeroes velocities, calls
  `Core.Reset(false)`; it is the natural place NOT to touch damage (reset keeps damage; repair is separate).

### 1.2 Consumers today
- A: `AcCarAudio.OnCarImpact` (`AcCarAudio.cs:619`) -> `Collision(kmh, point)` (`:184-211`): new contact (gap > 0.3 s) = hit by impact
  speed, repeats = scrape. Subscribed per car in `Init` (`:113`).
- Lab: `Editor/AcLabPhysics.cs:345-430` counts/logs impacts (root-local point, nearby colliders) in `physics-458-magione`;
  `Editor/AcLabAudio.cs:266, 476-491` fakes `RaiseCarImpact` / `audio.Collision` to test hit -> scrape.
- S: the doc says S "may pulse the controllers" - no subscriber found (grep) [VERIFIED in code].

### 1.3 Car vs car [VERIFIED in code]
- Remote player cars and (on non-masters) AI cars are **kinematic puppets** (`AcCarPhysics.SetPuppet`, `:325-339`) moved by
  `Body.MovePosition/MoveRotation` in FixedUpdate (`MovePuppet`, `:358-363`) so PhysX sees their velocity; `AcNetGame.Cars.cs:8-15` header:
  contacts are **one-sided** - my dynamic car feels the puppet; the puppet's owner feels MY puppet on THEIR client.
- Ghost rule `AcNetGame.Contacts` (`AcNetGame.Cars.cs:579-613`): puppet colliders get `excludeLayers |= Car` while either car is in the
  pit lane or was placed < 3 s ago and relative speed > 10 km/h (solid < 9) -> no contact -> no damage there (good, keep).
- Grey stand-in boxes (`StandIn`, `:540+`) are kinematic Car-layer boxes: hitting one is a car hit (CarImpact `HitsCar` true).
- AI on the master: Kind Ai rigs are ordinary dynamic `AcCarPhysics` (`AcCarRig.cs:47-48`), so AI-vs-AI and AI-vs-local are dynamic-
  dynamic on the master: **both** bodies get OnCollisionEnter with the same |relativeVelocity|.
- Kinematic vs kinematic never collides: a remote car hitting my PARKED (kinematic) car damages only the remote car (on its owner's
  client); my parked car is never damaged by it. Acceptable; document [INFERRED as AC-acceptable].
=> **Owner-authoritative damage falls out naturally**: each client damages only cars whose physics it runs (its own car; the master its AI
cars) from its own PhysX contacts, exactly as AC's client-side ACP_DAMAGE_UPDATE.

### 1.4 Changes (P owns the core)
| File | Owner | Change |
|---|---|---|
| `Data/Physics/AcCarDamage.cs` (NEW) | P | Unity-free damage state + rules: `Zones[5]` (front, rear, left, right, centre - AC order), `Susp[4]`, `EngineLife`, `GearboxLife`?, `Glass[5]` (derived), `Flat[4]`/`Inflation[4]` if siblings confirm punctures; `AddImpact(localPoint, localNormal, kmh, multiplier)`, `Tick(dt, rpm, boost)` (engine.ini RPM_DAMAGE_K / TURBO_DAMAGE_K above thresholds), `Repair(body, engine, susp)`, `Quantize()` for the wire, `Version` counter (bumped on any change so readers re-apply visuals without per-frame diffing). Harness-testable (`psim`-style probe). |
| `Data/Physics/AcCarSpec.cs`, `AcCarSpecParts.cs` | P | Parse what is not parsed yet: aero `ZONE_*_CL/CD` per wing (AcWingSpec), suspensions.ini `[DAMAGE] MIN_VELOCITY GAIN MAX_DAMAGE` per axle, engine `[DAMAGE]` (only RPM_THRESHOLD is read now: `AcCarSpec.cs:291`, `AcCarSpecParts.cs:49-50` "informative"), turbo threshold/K, car.ini `[PIT_STOP] *_REPAIR_TIME_SEC`, damage.ini `[DAMAGE] INITIAL_LEVEL` (+ C needs the rest of damage.ini). |
| `Game/Physics/AcCarPhysics.cs` | P | (a) `Impact`: compute damage BEFORE the 3/5 km/h audio gate, only on `entered` (scrape repeats add none, or a tiny scratch rate - sibling decides); use `cp.thisCollider` to skip "Ac Floor n" floor-box ground contacts and contacts whose normal is ~car-up on the Track layer (bottoming on kerbs must not crumple the front; AC behaviour unknown [INFERRED]); local point = `transform.InverseTransformPoint(cp.point)` -> zone by the car's extent (Model.Bounds or Spec.InertiaBox); wheel-corner contacts within ~wheel radius of a hub -> `Susp[i]` via suspensions.ini [DAMAGE]. Multiplier = `DamageRate` field (like `FuelRate`). (b) public `AcCarDamage Damage`, `float DamageRate`. (c) apply: aero (`Aero()` `:732-768` -> `AcAeroModel.Wing` with CL/(1+zoneCL*d), CD*(1+zoneCD*d)), suspension (toe/camber offset per wheel in `AcChassis` wheel `ToeDeg/CamberDeg`, `AcChassis.cs:15,22,165,181`), engine (`AcCarCore` torque scale / blown = no torque; `Core.Reset(false)` must keep it). (d) Telemetry: add damage fields so A/H/N read them from `AcTelemetry` like everything else. (e) raise an enriched event (below). (f) `ResetTo` keeps damage; `Init` sets `INITIAL_LEVEL` if siblings say so. |
| `Game/Shared/AcEvents.cs` | lead (P raises) | ADD (do not change `CarImpact`): `CarCollision(AcCarRig car, float kmh, Vector3 point, Vector3 normal, AcCarRig other /*null = env*/)` mirroring ACSP_CE_COLLISION_WITH_CAR/ENV (other car resolved via `collision.rigidbody` -> `AcCarPhysics.Rig`; a stand-in box gives null-but-car), and `CarDamaged(AcCarRig car, int zoneMask)` raised when quantized damage changes (H flashes the displayer for TIME=4 s, A plays nothing extra). A then drops `HitsCar` and the constant 90 deg angle. |
| `Game/Shared/AcTelemetry.cs` | lead | ADD `Damage0..4` (zones) / `SuspDamage[4]` (as 4 floats, no arrays - struct copied per frame), `EngineLife`, `GlassMask`, `Inflation` per `AcWheelState` (new field). |
| `Game/Physics/AcCarRig.cs` | P | `ApplyLocalSetup` / Spawn: nothing; `Dispose` nothing. Remote rigs get damage from N via a new `AcCarPhysics.ApplyRemoteDamage(in AcCarDamageWire)` (puppet: stores, pushes visuals/audio only). |

## 2. Network - record layout, authority, protocol 3

### 2.1 Free room [VERIFIED in code `Game/Net/Core/AcNetWire.cs:186-216`, `AcNetBlocks.cs:105-145,384-405`]
Player record (64 ints, owner writes): ints 0-41 used (`Used = 42`), **ints 42-63 spare (22 ints)**. Car block (26 ints, shared by players
at int 5 and AI cars at block offset 4/34): free bits = `CPos2` (car offset 25 = player int 30) **bits 11-31 (21 bits)**; `CDoors` (offset
16 = player int 21) car-flags byte bits 6-7 (**int bits 30-31**). Every other car-block int is full.
AI block ("Ac Net Ai 0..8", master writes): 4 header + 2 x 30 = 64, full; per AI car only int 26 bits 24-31 (**8 bits**, "spare" byte
after nameLine) besides the car-block bits above. So an AI car has 21 + 2 + 8 = 31 free bits; a player 31 + 22 ints.

### 2.2 Proposed layout (protocol 3)
Car block (both players and AI, so `UnpackCar` gives every remote car its body damage):
- `CPos2` bits 11-30: zone levels u4 x 5 (front 11-14, rear 15-18, left 19-22, right 23-26, centre 27-30), level = round(15 *
  min(1, zone / ZoneFull)) where ZoneFull is the sibling model's full-damage value (// ours unless the files have one; MAX_DAMAGE_KMH=60
  is a candidate). Bit 31 spare. Glass is NOT sent: owner AND readers derive glass per zone from the QUANTIZED level with the same rule
  (GLASS_THRESHOLD 0.65 of full [INFERRED unit]) -> identical on every client.
- `CDoors` byte 3 bits 6-7 (int bits 30-31): engine state 0 ok / 1 smoking (life < engine_smoke.ini ENGINE_LIFE) / 2 blown / 3 spare.
Player only: int 42 `AtDamage` = susp u4 x 4 (0-15) | tyre flat bits 16-19 | tyre exploded 20-23 (if punctures exist) | gearbox u4 24-27.
AI only: int 26 bits 24-31 = susp u2 x 4 (4 levels; enough for drawn wheel bend + flutter_4).
Damage ints are NOT motion (no seq/stamp bump: `CMotionFirst..Last` = CPos..CFront unchanged); Fusion sends them only on change.
Rate: a level changes a few times per crash -> negligible bandwidth.

### 2.3 Files (N unless noted)
| File | Change |
|---|---|
| `Game/Net/Core/AcNetWire.cs` | `Protocol = 3` (`:221`; the header field is 4 bits, 24-27); `Used = 43`; `AtDamage = 42`; `AcNetCar` += `Zone0..4` (bytes), `Engine`; `AcNetState` += `SuspDamage` packing; `PackCar/UnpackCar` (`:332-367`, `:370-478`) pack CPos2 bits 11-30 and car flags bits 6-7; `Pack/Unpack` (`:291-330`) int 42. Update the layout comment block and the protocol history line ("3 = damage zones in int 30 / flags / int 42"). |
| `Game/Net/Core/AcNetBlocks.cs` | `PackAi` (`:392-400`) bits 24-31 of int 26 = AI susp u2 x 4; an `UnpackAi` counterpart (where `net.Ai(i)` decodes); `Protocol` follows automatically. World 0 `Damage u16` (int 6) already carries DAMAGE_MULTIPLIER (`:112,192,242`) - no change. |
| `Game/Net/AcNetGame.Own.cs` | `FillCar(rig, ref car, ...)` (`:169`) copies `rig.Physics.Damage` quantized (local car; also called for AI cars by the master, `AcNetGame.Race.cs:326`). |
| `Game/Net/AcNetGame.Cars.cs` | Remote draw (telemetry build `:470-530`): decode damage -> `rc.Rig.Physics.ApplyRemoteDamage(...)` only when the quantized value changed (store last per `AcRemoteCar`), which pushes C's visuals and A's feed. Re-apply after the interior rebuild / `Adopt` (`:291-356`) and after the stand-in is replaced. |
| `Game/Net/AcNetGame.Players.cs` | `AcNetPlayer` gets damage fields for the HUD/standings (optional). |
| `Game/Ai/AcAiField.cs` (I) | `FromNet` (`:516+`, new master adopting the previous master's AI cars): seed the adopted rig's `Damage` from the decoded (quantized) zones/susp/engine so a hand-over does not repair the AI field. |
| `Editor/AcLabNet.cs` (N) | net-loopback: round-trip of zones/susp/engine, protocol-2 record refused by a protocol-3 reader (`Foreign`). |
Authority: the car's owner (own car: its client; AI: the instance master; AI after hand-over: the new master from the block). Nobody else
writes damage; readers only draw/hear it. Repairs are owner actions (the levels drop -> readers follow).
Protocol bump consequences: a protocol-2 client sees protocol-3 records as `Foreign` (slot kept, nothing decoded, logged once
`Net: player n ... runs net protocol 3, this build 2: refused`) - by design (`AcNetWire.cs:216-221`). README "net protocol 2" lines and the
"Builds with another net protocol" item must say 3.

## 3. Race / session / pits / UI

### 3.1 Rule [VERIFIED in code]
- `AcRules.DamageMultiplier` exists (`Game/Shared/AcSession.cs:45-46`, default 0, comment "iteration 1 has no damage model"), edited in
  the lobby (`Game/Front/AcPagesRace.cs:138` "DAMAGE MULTIPLIER", OFF / n %; steps/range from the launcher's own slider `assists?DAMAGE`,
  `Data/Ui/AcLauncherSliders.cs:34,64`, `Game/Front/AcLobby.cs:36-37,136,176,221`), published on the wire (World 0 int 6, `AcNetGame.Race.cs:136,179`).
  **Applied nowhere.** `AcRaceDefaults.cs:65` deliberately skips server_cfg `DAMAGE_MULTIPLIER=100`.
- Apply like fuel/tyre wear: `AcRace.Flow.cs:505-517 ApplyRates(rig)` (local car every frame via `LocalInputTick` `:523-546`) -> add
  `ph.DamageRate = Max(0, r.DamageMultiplier) / 100f`; for AI cars the same where R/I sets AI rates (I's spawn / `EnsureAi`). Offline-AC
  analogue `assists.ini DAMAGE` (0-100) - the Front's assists reader already parses it (`Data/Ui/AcUiData.cs:243,275`) but the
  session rule wins.
- `AcRaceDefaults.cs:65` (R): read `DAMAGE_MULTIPLIER` from server_cfg (100) once damage exists? The lobby default would then be 100 %;
  user decides (flag). Keep 0 until the model is verified.
- `VISUALDAMAGE` (assists.ini) = draw damage even with mechanical damage off [INFERRED]: an AcSettings flag `cars.visualdamage` (C/F),
  per player, local only.

### 3.2 Reset / back to pits / repair (R; F buttons)
- `AcRace.ResetCar` (`AcRace.cs:468-479`) / R key (`AcRace.Flow.cs:528-532`) -> `ResetToTrack` (`:548-562`) -> `AcSpawner.Place` ->
  `ResetTo`: **keep damage** (AC's reset is a CM/CSP extra; nothing in vanilla repairs on a reset [INFERRED]).
- `AcRace.BackToPits` (`AcRace.cs:452-463`) -> `PlaceInPits` (`AcRace.Load.cs:311-328`): in AC, "return to pits" mid-session puts you in
  the pit box where the pit app offers REPAIR BODY / ENGINE / SUSPENSION with car.ini repair times [VERIFIED art/strings; flow
  INFERRED]. Proposal: `BackToPits` keeps damage and opens the repair choice; repair costs `damage% / 10 * *_REPAIR_TIME_SEC` seconds
  frozen in the box (P `Freeze(true)`, R timer, `common/screw`/`unscrew` sounds by A, HUD countdown). Simpler fallback (ours, flag):
  back to pits = full repair.
- New session / grid (`AcRace.Flow` Grid entry; `RespawnLocalCar` "car change") -> full repair (fresh rig repairs by construction;
  grid placement must call `Damage.Repair(all)`) [INFERRED: AC starts each session with an undamaged car].
- Pit-box stop detection (stopped in MY box while `AcLocal.InPitlane`): R has `PitBox` (`AcRace.cs:49`), `AcTrackWorld.PitBoxes`,
  `AcTiming` pit-lane flag (`AcTiming.cs:193-227`). New `AcRace.Repair(bool body, bool engine, bool susp)` (R) used by F.
- Race rules: blown engine in a race = retired (DNF) -> `AcStandingsBoard` / results need a "DNF" (R) and the wire a retired bit
  (car-block `CPos2` bit 31 spare, or Race 1 penalty byte 254 - R/N decide).

### 3.3 Front (F)
- GARAGE page `AcPageGarage` (`Game/Front/AcPagesMisc.cs:19-228`): add a REPAIR section (body / engine / suspension rows with the
  estimated seconds, `content/gui/pitstop/repair_*_ON/OFF.png` as icons, `operating_repair.png` while repairing); enabled only stopped in
  the own pit box (same "waiting" rule as setup changes, `:120`).
- RACE LOBBY `AcPageLobby` (`AcPagesRace.cs:93-97`): RESET CAR / BACK TO PITS unchanged; DAMAGE MULTIPLIER row already exists.
- SETTINGS: `cars.visualdamage`, `hud.damage` (AC's gameplay.ini `[DAMAGE_DISPLAYER] IS_ACTIVE`), via `AcSettingsUi.Toggle`.

### 3.4 HUD (H)
- AC has a damage displayer (`AVDamageDisplayer`): shown for `TIME=4` s after damage, at `POSITION_X=20`, `DISTANCE_FROM_CENTER_Y=-200`,
  colours scaled to `MAX_DAMAGE_KMH=60`, art = the stacked layers in `content/texture/damage/` (section 0). [VERIFIED files; drawing rule INFERRED]
- `Data/Hud/AcHudArtList.cs` (H): add the 20 paths; `Data/Hud/AcHudConfig.cs` (H): read damage_displayer.ini + gameplay.ini IS_ACTIVE;
  `Game/Hud/AcHudArt.cs` loads them; `AcHudView` (desk, `Game/Hud/AcHudView.cs`) and `AcHudVr` (VR panel) draw tinted layers;
  `AcHudFeed` (`Game/Hud/AcHudFeed.cs`) gets the damage values from `AcLocal.Telemetry` (new fields) and a "shown until" stamp set on
  `AcEvents.CarDamaged`. Lab `hud-desk` fake feed (`AcHud.SetFake`, `AcHud.cs:192`) can show it without a crash.

## 4. Audio (A) [VERIFIED in code + install]
- `AcFeed.Constant` (`Game/Audio/AcEventPlayer.cs:52-61`) pins `Inflation = 1`, `SuspDamage = 0`; the player SKIPS and never decodes
  instruments whose region excludes the constant (`:160-169`) -> `flat_tyre_mono`, `tyre_explosion` are not even decoded today.
  Change: remove both from `Constant`, feed `_feed[AcFeed.SuspDamage]` (max of the 4 suspension values, or per-wheel if A splits the wheel
  event - it is ONE event at the body emitter now, `AcCarAudio.cs:287`) and `_feed[AcFeed.Inflation]` (min inflation) in `Feed`
  (`:405-425`) from telemetry (local/AI) or N's record (remote). Memory: 3 more samples decoded per audible car.
- Collision: `Collision(kmh, point)` (`:184-211`) - take normal/other from the new `CarCollision` event: real `impact_angle` (0..180, angle
  between impact normal and car forward [INFERRED meaning]) and car/track choice without `HitsCar`; `collisions/object/hit` for cones
  (no cone physics yet).
- Pit repair: `common/screw` / `common/unscrew` (A plays on R's repair start/end; `AcSfx` or the car emitter).
- Glass break / engine blow: **no event exists** -> silent (or the crash3 layer); nothing to invent (no shipped sounds).
- Engine blown: rpm -> 0 in telemetry stops engine sounds by itself (`running = _rpmS > 50`, `:413`).

## 5. AI (I)
- AI rigs use the same `AcCarPhysics` -> they take damage automatically on the master once P adds it; `DamageRate` from the session rule
  [INFERRED: AC offline AI take damage under the same multiplier].
- `AcAiDriver` (`Game/Ai/AcAiDriver.cs`): states `{Building, Pits, Driving, Reversing, Finished, Rails, Failed}` (`:7`): a blown engine
  or undriveable car -> new `Retired` (or `Failed`) state, hold the car, tell R (DNF); `Recover`/`ResetToLine` (`:350-390`) keep damage.
  Plan (`Data/Ai/AcAiPlan.cs`) assumes full aero: optionally scale the planned corner speeds by the current aero/susp loss (I) - else the
  damaged AI overdrives corners and spins (more recoveries).
- No AI pit stops in iteration 1 (`AcAiDriver.cs:120,281`) -> no AI repairs; fine for short races.
- Avoidance: `AcAiTraffic` (`Game/Ai/AcAiTraffic.cs`) already keeps 2.4-2.7 m (README ai-race); damage raises the stakes of the open
  "AI right behind a standing car" gap - keep `ai-race` contacts = 0 as a regression check.
- Hand-over: `AcAiField.FromNet` seeds damage (section 2.3).

## 6. Car model / visuals (C, W) - seams only (siblings own the look)
- C: `AcCarShading.FlattenDamageNormals` (`Game/Cars/AcCarShading.cs:56-95`) zeroes `_Flags.y` and puts `AcTextures.FlatNormal` on every
  `ksPerPixelMultiMap_damage_dirt` material: the damage normal (txNormal = DAMAGE_CHASSIS_NM), `txDamage@4`, `txDamageMask@21` and the
  kn5 material property `damageZones` / `glassDamage` (ac_car_formats.md:75-83) are dropped today. New `AcCarModel.SetDamage(zones[5],
  glassMask, susp[4])` (C) driving per-material shader values on every LOD set (`_sets`, `AcCarModel.cs:418,735`; re-apply on LOD switch
  and on the interior rebuild), damage.ini `[VISUAL_OBJECT_n]` static rotations/oscillations by `DAMAGE_ZONE`, `DAMAGE_GLASS_*` nodes
  (built inactive by W: `AcKn5Builder.cs:289`) switched on by the glass rule. `ApplyTelemetry` (allocation-free, called every frame by
  P/N) must NOT re-set materials each frame - only when `Damage.Version` changes.
- W: `Shaders/AcPerPixel.shader` has no damage path (`_Flags` x alpha, y normal, z maps, w unlit): needs txDamage / txDamageMask / damage
  normal blend by a per-material zone vector (+ ksBrokenGlass for `DAMAGE_GLASS_*`, shader names in the install `system/shaders/win/
  ksPerPixelMultiMap_damage_dirt_*`, `ksBrokenGlass_*`); `Game/World/AcMaterials.cs` must keep those textures for car materials; any new
  shader/variant must be carried (`AcShaders.Carry`, carrier material in `Resources/`).
- Sparks (`sparks.ini`) / engine smoke (`engine_smoke.ini`) are optional effects (C or A? - lead to assign; no module owns particles).

## 7. Lab (each owner adds to its own `Editor/AcLab<Module>.cs`)
Patterns found: `AcLabRunner.Register("<module>-<what>", r => Run(r))`; `physics-flat` builds a private plane (`BuildPlane`,
`AcLabPhysics.cs:152-170`) and spawns with `SpawnRig(r, car, AcCarKind.Local, ...)`; impact logging via `AcEvents.CarImpact`
(`AcLabPhysics.cs:345-430`); A fakes impacts (`AcLabAudio.cs:476-491`); `r.Check`, `r.Shot`, `r.Aim`, `r.Wait`.
Proposed scenarios:
- `physics-crash` (P): on the flat plane add a Track-layer box wall (layer 14, `AcSurfaceTag` none = wall); for speeds 20/40/60/100/150
  km/h set `Body.linearVelocity` straight at it (front, then rear by reversing heading, then side at 90 deg); check zone hit = expected
  zone, other zones ~0, monotonic with speed, multiplier 0 -> no damage, 50 % -> half; aero loss and engine/susp effects (0-100 / lateral g
  before vs after); `ResetTo` keeps damage, `Repair` clears; pictures of the car after each hit (with C's visuals).
- `cars-damage` (C): `SetDamage` per zone 0 / 0.5 / 1 on the 458 + one open-wheeler (formula/lotus) - pictures of each zone, glass nodes,
  VISUAL_OBJECT rotation; LOD switch keeps it.
- `audio-damage` (A): fed suspension_damage 0 -> 1 (flutter_4 audible above 0.44), inflation 1 -> 0.05 (tyre_explosion once, flat loop),
  collision with the new event (angle).
- `hud-damage` (H): fake feed with each layer at 0/0.5/1, picture; 4 s timeout.
- `net-loopback` extension (N), `race-solo-actions` extension (R: back to pits + repair timer, grid repairs), `ai-race` with damage 100 %
  (I: AI finish, contacts 0, damaged AI retire rule).

## 8. Per-module change list (summary)
| Code | Files |
|---|---|
| lead | `Game/Shared/AcEvents.cs` (+CarCollision, +CarDamaged), `Game/Shared/AcTelemetry.cs` (+damage fields, AcWheelState.Inflation), `README.md` (protocol 3, damage), assign particles owner |
| P | `Data/Physics/AcCarDamage.cs` NEW, `AcCarSpec.cs`/`AcCarSpecParts.cs` (damage keys, aero zones, pit-stop times, damage.ini INITIAL_LEVEL), `AcAeroModel.cs` (zone factors), `AcChassis.cs` (toe/camber offsets), `AcCarCore.cs` (engine life/torque, keep across `Reset`), `Game/Physics/AcCarPhysics.cs` (Impact -> damage, DamageRate, Damage, ApplyRemoteDamage, telemetry), `Editor/AcLabPhysics.cs` (physics-crash), harness probe |
| N | `Game/Net/Core/AcNetWire.cs` (protocol 3, CPos2 bits 11-30, flags 6-7, int 42), `Core/AcNetBlocks.cs` (AI int 26 bits 24-31), `AcNetGame.Own.cs` (FillCar), `AcNetGame.Cars.cs` (apply on change, re-apply after rebuild/adopt), `AcNetGame.Players.cs`, `Editor/AcLabNet.cs` |
| R | `Game/Race/AcRace.Flow.cs` (ApplyRates DamageRate; grid repair; DNF), `AcRace.cs` (Repair API, BackToPits rule), `AcRace.Load.cs` (PlaceInPits / RespawnLocalCar), `AcRaceDefaults.cs` (DAMAGE_MULTIPLIER decision), `AcStandingsBoard.cs` (DNF), `Game/Shared/AcSession.cs` (comment), `Editor/AcLabRace.cs` |
| F | `Game/Front/AcPagesMisc.cs` (GARAGE repair rows), `AcSettingsUi` rows (visual damage, damage displayer), `AcPagesRace.cs` (nothing new; DNF in timing), `Editor/AcLabFront.cs` |
| H | `Data/Hud/AcHudArtList.cs`, `Data/Hud/AcHudConfig.cs`, `Game/Hud/AcHudArt.cs`, `AcHudFeed.cs`, `AcHudView.cs`, `AcHudVr.cs`, `AcHud.cs` (CarDamaged subscription), `Editor/AcLabHud.cs` |
| A | `Game/Audio/AcEventPlayer.cs` (drop Inflation/SuspDamage constants), `AcCarAudio.cs` (feed damage params, CarCollision angle/other, screw/unscrew), `AcSfx.cs` (require screw/unscrew), `Editor/AcLabAudio.cs` |
| I | `Game/Ai/AcAiDriver.cs` (Retired state), `AcAi.cs` (DamageRate from rules), `AcAiField.cs` (FromNet seeds damage), optional `Data/Ai/AcAiPlan.cs` speed scaling, `Editor/AcLabAi.cs` |
| C | `Game/Cars/AcCarModel.cs` (SetDamage, LOD/rebuild re-apply), `AcCarShading.cs` (stop flattening when damage visuals on; zone values), `AcCarParts.cs` (damage.ini VISUAL_OBJECT, glass nodes), `Editor/AcLabCars.cs` |
| W | `Shaders/AcPerPixel.shader` (+ damage blend, broken glass), `Game/World/AcMaterials.cs` (keep txDamage/txDamageMask/damageZones/glassDamage), `Resources/` carriers |
| S | optional: controller pulse on `CarCollision` (`AcXr.Pulse`), seat camera shake none (VR comfort rule) |

## 9. Open / uncertain
- Unit of AC's zone damage (km/h-like vs 0..1) and the full-damage value -> quantization scale (sibling model research).
- Whether AC damages on floor scraping / kerb bottoming, and whether "return to pits" repairs (no doc found; test in the game).
- Whether collider.kn5 hulls include the wheels (decides how suspension damage is detected) - census needed (C/P probe).
- Punctures (`inflation`): the banks support them; whether AC1 simulates them in normal play is unconfirmed here.
- `center.png` unused by acs.exe (no string) - the centre zone may not be drawn on AC's displayer.
- AssettoServer's ACP_DAMAGE_UPDATE layout could not be fetched (GitHub code search needs sign-in).

## Critic addendum (2026-11-02, critic pass; OVERRIDES the text above where they differ)
Method: acs.exe strings re-extracted (perl ASCII + UTF-16LE scan, names only); census probe `DevTools/harness-ac/ProbeDmgCrit.cs`
(`dmgcrit`: every damage-ish key in damage/engine/suspensions/drivetrain/car/aero.ini of all 86 owned cars, from data.acd);
`kn5 ferrari_458*.kn5 tree`; `fev common`; grep/read of the cited code. No web sources were used by the critic.

### Holds [VERIFIED by critic]
- Every acs.exe name in section 0 row 1 exists (UTF-16 or ASCII as stated); `center.png` is indeed absent (the other 19 damage pngs are
  named in full as `content/texture/damage/<x>.png`). `Sending ACP_DAMAGE_UPDATE`, `suspension_damage`, `Damage zone %d: %f` ASCII.
- assetto_corsa.ini GLASS_THRESHOLD=0.65; damage_displayer.ini values; gameplay.ini IS_ACTIVE=1; engine_smoke.ini ENGINE_LIFE=25 /
  SECONDS=15; physics.ini [TYRES] FLAT_SPOT_K/FF; server_cfg DAMAGE_MULTIPLIER=100 (server/cfg and presets/SERVER_00); assists.ini
  DAMAGE=0 / VISUALDAMAGE=0 (both install cfg and Documents copy); SDK ACSProtocol.cs:26-27 and Program.cs ~300-321.
- Pit art files, common.bank events (car/track/object hit with impact_speed + impact_angle 0..180, screw/unscrew), no glass/engine-blow event.
- Code: `Impact` gates / GetContact(0) / no normal in CarImpact; HitsCar OverlapSphere 0.6 m; ImpactAngle fixed 90 (AcCarAudio 199, 422);
  AcFeed.Constant pins Inflation=1 / SuspDamage=0 and the skip rule (AcEventPlayer 52-57, 167); wire layout (ints 42-63 spare, CPos2
  bits 11-31 free, car flags bits 6-7 free, AI int 26 bits 24-31 free, protocol 4 bits 24-27); DamageMultiplier published but applied
  nowhere; AcRaceDefaults:65 skips DAMAGE_MULTIPLIER; engine [DAMAGE] only RPM_THRESHOLD parsed (AcCarSpec:291); DAMAGE_GLASS_* nodes are
  inactive in the kn5 ("(off)") and AcKn5Builder:289 builds inactive nodes inactive (generic, not glass-specific).

### Wrong / missing
1. **Missed per-car glass keys (major).** damage.ini has `[DAMAGE_GLASS_FRONT|REAR|LEFT|RIGHT|CENTER] MIN_SPEED / FULL_SPEED` (km/h) in
   84/86 cars (e.g. FRONT 30/60 x61, LEFT/RIGHT 20/30 x73, CENTER 30/80 x74). Glass is therefore per car and per pane, not a plain
   "GLASS_THRESHOLD of full". Likely rule [INFERRED]: pane breaks when (zone - MIN) / (FULL - MIN) >= GLASS_THRESHOLD. Kn5 nodes are
   `DAMAGE_GLASS_<ZONE>_<n>` (n = 1, 2), several per zone, differing per LOD.
2. **Missed VISUAL_OBJECT keys.** Full set (all in acs.exe UTF-16): NAME, DAMAGE_ZONE (only FRONT x145 / REAR x115 in the census), MIN_SPEED,
   FULL_SPEED, STATIC_ROTATION_AXIS, STATIC_ROTATION_ANGLE, MULT_G, ALLOWED_G, OSCILLATION_AXIS, OSCILLATION_MIN_ANGLE, OSCILLATION_MAX_ANGLE
   (the row 1 shorthand `OSCILLATION_AXIS/MIN_ANGLE/MAX_ANGLE` hides that the real names are `OSCILLATION_MIN_ANGLE` / `_MAX_ANGLE`).
   [SCRATCHES] MIN_SPEED=0 / MAX_SPEED=20 km/h in all 86; [OSCILLATIONS] ENABLED (0 x5).
3. **Unit / quantization error (major).** Every damage.ini threshold is km/h of impact speed, and VISUAL_OBJECT FULL_SPEED goes up to
   **220** (x21 objects), 150 (x40), glass FULL_SPEED up to 80. So zone damage is km/h-like (supports section 9's guess), and section 2.2's
   `level = round(15 * min(1, zone / ZoneFull))` with ZoneFull = MAX_DAMAGE_KMH=60 saturates at 60 km/h: glass with 0.65 of 30..80
   (=62.5) would NEVER break and bonnet/bumper rotations above 60 km/h would be lost on remote clients. MAX_DAMAGE_KMH is the HUD
   displayer's colour scale, not the model's full value. Wire must cover ~0..255 km/h: e.g. u8 1 km/h (players: use spare ints) or u6 at
   4 km/h steps (5 x 6 = 30 bits fits the AI's 31 free bits: CPos2 21 + flags 2 + int 26 8). Readers derive glass/objects from the
   quantized value with the same rule, as proposed.
4. **suspensions.ini [DAMAGE] is ONE section per car, not per axle** (83/86 cars; the 3 ks_audi_sport_quattro* have none). Keys MIN_VELOCITY
   (40 x74), GAIN (0.0004 x63), MAX_DAMAGE (0.05 x81), DEBUG_LOG. formula_k comments: GAIN = "steer rod deflection for impact kmh",
   MAX_DAMAGE = max steer rod deflection [DOCUMENTED in install sdk formula_k suspensions.ini:99-103] -> suspension damage is a
   **steer-rod (toe) offset**, not camber: 1.4(c) "toe/camber" should be toe/rod only (camber = [INFERRED, no key]).
5. **engine.ini [DAMAGE] coverage:** RPM_THRESHOLD + RPM_DAMAGE_K in 86/86, TURBO_BOOST_THRESHOLD + TURBO_DAMAGE_K in 84/86; the sdk
   formula_k engine.ini has NO [DAMAGE] (do not use it as the reference here). drivetrain [DAMAGE] RPM_WINDOW_K=100 only in 54/86 cars.
6. **car.ini [PIT_STOP]**: BODY 20 s x84 / 5 s x2, ENGINE 2 s x85 / 6 s x1, SUSP 30 s x86 per 10 % (formula_k comment) - 458 value holds.
7. **damage.ini [DAMAGE] INITIAL_LEVEL** = 20 in 85 cars, 100 in 1: meaning UNKNOWN (sits beside the damage.ini keys in acs.exe). Do NOT
   apply it as starting damage (1.4(f)) until a sibling proves its meaning; most likely not damage at all [INFERRED].
8. aero zone keys also exist in a `[FIN_n]` section (1 car), not only `[WING_n]`; aero has only FRONT/REAR/LEFT/RIGHT (no centre).
9. Missed exe strings: `content/texture/DAMAGE_GLASS.dds` (exists, 4 MB, the broken-glass texture), `ksBrokenGlass` and
   `ksPerPixelMultiMap_damage` (non-dirt variant) shaders in system/shaders/win, `ksPerPixelMultiMap_damage_dirt_sunspot`, `FAIL|DAMAGE|`,
   `debugDamage`, `testCrash`, `Damage: `, `content/gui/pitstop/repair_sus` (prefix: `repair_susp_*` pngs are NOT referenced -> unused).
10. `AVDamageDisplayer` / `AVVisualDamageManager` are MSVC RTTI manglings (`.?AVDamageDisplayer@@`): the classes are `DamageDisplayer`,
    `VisualDamageManager`. The SDK example does not say rel_pos is car-relative ([INFERRED]). assists.ini holds DAMAGE=0 only; "0..100"
    is the launcher slider range, not the file.
11. **CDoors IS a motion int**: `CMotionFirst..Last` = CPos(3)..CFront(17) includes CDoors(16) (record comment "8-22"). Putting engine
    state in car-flags bits 6-7 makes a change bump seq/stamp (one extra timeline sample - harmless, but 2.2's "damage ints are NOT motion"
    is false for that field). CPos2 / int 42 / AI int 26 are non-motion as claimed.
12. **PlaceInPits is not only BackToPits**: also session changes (AcRace.Flow 82/84), late-for-start 152/158, jump-start penalty 315 and the
    no-racing-line reset fallback 554. Repair/keep logic must live in `BackToPits` / grid code, never in `PlaceInPits`.
13. **ApplyRates already runs for AI cars** (AcRace.Flow ~601 and ~626 over the drivers), plus AcAiField:281 sets AI FuelRate: adding
    DamageRate inside ApplyRates covers AI too; keep AcAiField in step.
14. Missed CarImpact subscriber: `Editor/AcLabAi.cs:167,276` (ai-race contact counting) - keep CarImpact unchanged (as 1.4 says) or the
    `ai-race` regression check breaks.
15. AI record decode lives in `Game/Net/Core/AcNet.Blocks.cs:428-440` (reads at+26 level / nameLine) - that is where AI susp bits 24-31 go.
16. Line drift (code moved since writing, content right): AcCarPhysics Impact 890-907, Aero 728, SetPuppet 328, MovePuppet 360-365, ResetTo
    370-394; AcNetWire layout comment 190-218, Protocol :227, Used :229, PackCar 378-412, UnpackCar 414-478; remote draw ApplyTelemetry
    AcNetGame.Cars.cs:424; Contacts 580-614; StandIn 544; AcCarShading.FlattenDamageNormals :63.
17. Not re-verified by the critic: shared-memory carDamage[5] semantics (unofficial doc), the dmgsnd bus-dB numbers, the 0.3 s hit gap.
