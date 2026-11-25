# Mechanical damage in AC -> our physics (research, 2026-11-02)

Topic: aero zone damage, steer-rod (suspension) damage, engine over-rev / over-boost damage, gearbox damage, tyres, pit-stop repair,
reset / back-to-pits. Then where each plugs into `Data/Physics` + `Game/Physics`.
Tags: **[VERIFIED in install]** = read from the player's install (data.acd via harness, sdk formula_k comments, system/cfg, the game's
`changelog.txt`, key-name strings of `acs.exe`); **[DOCUMENTED: url]**; **[INFERRED]** = my reasoning, flagged; **[UNKNOWN]**.
Nothing here is copied into code: every number below is read per car at run time, or is marked `// ours` when we have to invent it.

Reproduce:
```
cd "D:/Dreamport/Assetta Corsa/DevTools/harness-ac" && dotnet build -c Release -v q
dotnet run -c Release --no-build -- mechdmg [car]   # per-car table + key census (ProbeDmgMech.cs)
dotnet run -c Release --no-build -- mechkeys        # every damage-ish key in every data.acd ini
```
(My first probe file `ProbeDmg2.cs` was overwritten by a parallel damage agent, whose `dmgstrings` now lives there. Mine is
`ProbeDmgMech.cs`; outputs went to the session scratchpad as `mech_dmg.txt`, `mech_keys.txt` and `mech_acs_w.txt`.)

---------------------------------------------------------------------------------------------------------------------------------
## 0. The big picture: what AC has (from the game itself)

Key evidence (all **[VERIFIED in install]**):
- `acs.exe` strings for the damage displayer (system/cfg/damage_displayer.ini `[MAIN] TIME=4 MAX_DAMAGE_KMH=60 PRINT_VALUES=0`):
  icons `content/texture/damage/{engine,gearbox,front,rear,left,right,center,tyre_fl..rr,sus_fl..rr,flatspot_fl..rr}.png`, and value
  labels `BODY FRONT : / BODY REAR : / BODY LEFT : / BODY RIGHT : / SUSPENSION FL : .. RR : / ENGINE LIFE : / GEAR : `.
  So AC's damage state is: **4 (+centre) body zones, 4 suspension corners, engine life, gearbox, 4 tyres (puncture / flat spot)**.
- The pit menu: `SELECT DAMAGE REPAIR`, buttons `BUT_REPAIR_BODY` "REPAIR BODY", `BUT_REPAIR_SUSP` "REPAIR BODY + SUSPENSION",
  `BUT_REPAIR_ENGINE` "REPAIR ENGINE", `ESTIMATED PIT STOP TIME : `, `CONFIRM`; quick-menu headers `QM_HEADER_REPAIR` " BODY"
  " SUSPENSION". The art is in `content/gui/pitstop/repair_{body,engine,sus,susp}_{ON,OFF}.png` and `operating_repair.png`.
- Physics key names in acs.exe (UTF-16): `DAMAGE`, `ZONE_`, `MIN_VELOCITY`, `MAX_DAMAGE`, `DEBUG_LOG`, `RPM_THRESHOLD`, `RPM_DAMAGE_K`,
  `TURBO_DAMAGE_K`, `RPM_WINDOW_K`, `PIT_STOP`, `BODY_/ENGINE_/SUSP_REPAIR_TIME_SEC`, `EXPLOSION` (it sits among the tyres.ini keys).
  ASCII: `Suspension::setSteerLengthOffset`, `INIT ROD LENGTH:%f`, `Damage zone %d: %f`, `Sending ACP_DAMAGE_UPDATE`,
  `Adding wrecking contact at km: %fm tot: %d, ratio: %f`. FMOD params: `impact_speed`, `inflation`, `suspension_damage`.
- Telemetry channel names (ctelemetry): `ELIFE`, `FLAT L`, `FLAT R`, `VKM F/R`, `BLISTER`, `GRAIN`.
- `changelog.txt` (Kunos'), with versions:
  0.6.5 "Engine damage (not balanced yet)", "Engine damage when turbo overboost is over 80% on cars that support it", "Engine damage from
  overreving the enging (i.e. downshifts)", "Engine smoke when damaged"; 0.7.5 "Damage OFF option"; 0.21.6 "Server is now in control of
  damage level", "AI cars now try to park on the side if they have a broken engine"; 0.22.15 "Aero damage for F312T, L98T, Exos, Exos S1,
  Tatuus F.Abarth"; 0.22.17 "drivetrain damage for cars with H shifter gearboxes.", "suspension damage for all cars"; 0.22.20 "added
  sounds for tyre puncture"; 0.22.21 "Engine damage implemented for all cars"; 1.0.2 "Fixed wrong impact speed calculation for car to car
  contact resulting in too much damage"; 1.1 "Added "final" damage for high speed impacts", "[DATA] section in aero.ini is now obsolete
  ... HEADER=2 is mandatory if damage is used"; 1.3.1 "Fixed Nissan GT-R NISMO front suspension to correct damage under full lock";
  1.5 "Damage is now visible in Multiplayer mode for remote cars"; 1.7 "Red lines and indications for gear change in the GUI are now
  considering damage rpms as well as limiters"; 1.9 "Tyre blister and grain are now tied to the "Wear" settings and not "Damage"";
  1.9.3 shared memory `aidMechanicalDamage`; 1.10 "Tyre explosion temperature is now configurable ... optional section in tyres.ini:
  [EXPLOSION] TEMPERATURE=350"; 1.11.0 "Reviewed damage curve + repair times", "Pitstop is possible also in Practice session";
  1.11.3 "Fixed new damage formulas not considering wings with negative cl", "Fixed damage wrong influence on drag", "... on vertical
  wings (fins)"; 1.12 "Pitstop: Suspension repair will automatically fix body too"; 1.13 "Fixed Alfa Romeo Giulietta QV gearbox damage";
  1.14 "Fixed Tyre App thermal value for exploded tyres".
- The player's own settings: `Documents/Assetto Corsa/cfg/assists.ini [ASSISTS] DAMAGE=0 VISUALDAMAGE=0` (0..100 = %), the server's
  `server_cfg.ini [SERVER] DAMAGE_MULTIPLIER` (0..100) **[DOCUMENTED: https://www.gamehostbros.com/guides/games/assetto-corsa/assists-and-realism]**.
  Our `AcRules.DamageMultiplier` already exists: lobby UI (`AcLobby` / `AcPagesRace` "DAMAGE MULTIPLIER"), net (`AcNetBlocks` World 0
  word 6) and `AcRaceDefaults`. **Nothing reads it yet.**
- Shared memory (AC 1.x, not ACC): `SPageFilePhysics.carDamage[5]` (front, rear, left, right, centre), `SPageFileStatic.aidMechanicalDamage`;
  **no engine life, no suspension damage, no gearbox** in AC's own shared memory **[DOCUMENTED: https://github.com/mdjarv/assettocorsasharedmemory
  (Physics.cs / StaticInfo.cs) + changelog 1.9.3]**. CSP adds those: dynamic-controller inputs `DAMAGE_ENGINE` / `DAMAGE_GEARBOX` "from 0 to 1"
  **[DOCUMENTED: https://cup.acstuff.club/docs/csp/cars/new-inputs-for-dynamic-controllers]**; physics scripts may set
  `ac.accessCarPhysics().engineLifeLeft = 0` ("Exploding engine"), set "Car damage; Engine damage; Gearbox damage;" and "Tyres inflation
  (set to 0 to blow them up)" **[DOCUMENTED: https://cup.acstuff.club/docs/csp/cars/physics-scripts]**.

### Units of "DAMAGE" (question a)
**Body-zone damage is in km/h of impact speed** **[INFERRED, strong]**:
1. damage_displayer.ini calls its full scale `MAX_DAMAGE_KMH=60`;
2. damage.ini `[VISUAL_OBJECT_n] MIN_SPEED/FULL_SPEED` (0..220) and `[DAMAGE_GLASS_*] MIN_SPEED/FULL_SPEED` (10..80) are read against the
   same zone value; FMOD's collision parameter is `impact_speed`;
3. the aero ZONE gains only make sense in km/h: `ZONE_FRONT_CL=0.015` with D = 60 gives CL / 1.9 (front wing lift almost halved at
   "100 %" on the displayer); with D in 0..1 it would be a 1.5 % effect (invisible).
The CL / CD formulas are in every aero.ini (formula_k comment, VERIFIED): `CL = CL / (1 + ZONE_x_CL * DAMAGE)`,
`CD = CD * (1 + ZONE_x_CD * DAMAGE)` per zone x in FRONT/REAR/LEFT/RIGHT. 1.11.3 fixed "wings with negative cl" -> the division
also applies to negative CL (lift is reduced in size, keeping its sign).
**How impacts become zone km/h [UNKNOWN]**: AC passes the impact speed through a "damage curve" (1.11.0 "Reviewed damage curve") with a
"final" extra for high-speed hits (1.1), and scales it by the damage multiplier (assists DAMAGE / server DAMAGE_MULTIPLIER, 0..100 %).
Whether zones keep the MAX or the SUM of hits is not documented. Recommended // ours reading: `zone = min(cap, zone + kmhNormal * mult)` for
hits above a threshold, with the contact direction picking the zone (section 6). Body damage itself does NOT slow the car except through
aero (and visuals); there is no "body -> handling" key.

---------------------------------------------------------------------------------------------------------------------------------
## 1. Census of the 86 owned cars [VERIFIED in install, harness `mechdmg` / `mechkeys`]

Every damage-ish key outside damage.ini / proview_nodes.ini, across all inis of all 86 data.acd:
| file [section] key | cars | values |
|---|---|---|
| aero.ini [WING_n] ZONE_{FRONT,REAR,LEFT,RIGHT}_{CL,CD} | 86 (291 wings; 1 [FIN_n] all 0) | 0 .. 0.015 |
| suspensions.ini [DAMAGE] MIN_VELOCITY / GAIN / MAX_DAMAGE / DEBUG_LOG | 83 | MIN_VELOCITY 40 x74, 30 x7, 25, 85; GAIN 0.0004 x63, 0.0006 x9, 0.0003 x7, 0.00045, 0.0005, 0.001 (ferrari_458), 0.005 (ks_mazda_miata); MAX_DAMAGE 0.05 x81, 0.02 (ferrari_458), 0.11 (ks_mazda_miata) |
| engine.ini [DAMAGE] RPM_THRESHOLD / RPM_DAMAGE_K | 86 | K: 1 x75, 0.2 x4, 0.5 x3, 0.05, 0.08, 3, 4 |
| engine.ini [DAMAGE] TURBO_BOOST_THRESHOLD / TURBO_DAMAGE_K | 84 (missing: ks_lamborghini_miura_sv, ks_mazda_787b) | thr 0.8..3.0; K 5 x76, 30 x6, 10, 7 |
| drivetrain.ini [DAMAGE] RPM_WINDOW_K | 54 | 100 always |
| car.ini [PIT_STOP] BODY / ENGINE / SUSP_REPAIR_TIME_SEC | 86 | 20 x84 / 5 x2 (exos); 2 x85 / 6.0 (miura); 30 x86 |
No other key anywhere: **no tyre puncture / blow-out key** (no tyres.ini [EXPLOSION] on any owned car -> AC's built-in default
temperature applies, value not in the files [UNKNOWN; the changelog example says 350]), **no brake / wheel damage key**.
The 3 cars without suspensions.ini [DAMAGE] are ks_audi_sport_quattro, _rally, _s1 (STRUT/STRUT) -> AC's defaults [UNKNOWN] - treat as
"no steer-rod damage" (// ours) or use the formula_k values.

Per-car highlights (full table: `mechdmg`):
- `RPM_THRESHOLD > LIMITER` on 85/86 cars (typically +100..+300 rpm: 458 9200/9500, e30 7250/7500, alfieri 7500/9200). The only car with
  the threshold BELOW its limiter: ks_lamborghini_miura_sv (LIMITER 9000, RPM_THRESHOLD 8550, K 0.5) - it damages itself on the limiter.
  So in practice over-rev damage comes from **downshifts (money shift), limiter overshoot and coasting downhill / wheel-driven revs**.
- Turbo cars: 31. Only ks_audi_sport_quattro_rally (min(WASTEGATE 1.5, MAX_BOOST 1.8) = 1.5 > TURBO_BOOST_THRESHOLD 1.4) can overboost
  with the default settings; the others need a cockpit-adjustable / controller boost above the threshold (0.6.5 "over 80 % on cars that
  support it": bmw_z4_drift has TB_K 30, threshold 1.11, MAX_BOOST 0.85 per turbo x 2 turbos -> total boost matters, see 3).
- Aero zones, typical road/GT car: WING_0 BODY F/R CD 0.005, L/R CD 0.01 (drag only); FRONT wing F CL/CD 0.015; REAR wing R CL 0.01 / CD
  0.015. Open-wheelers (312t, 98t, exos, tatuus): wings 0.01, the DIFFUSER R (+L/R) 0.01. Miata: CD only 0.002/0.005. ks_mazda_787b body
  has CL zones too. ks_alfa_33_stradale: all zones 0 (no aero damage). Lotus 49: body only. Levante / Quattroporte: body (+grill) only.
- PIT_STOP: everyone 20 s per 10 % body, 2 s per 10 % engine, 30 s per 10 % suspension (exos 5/2/30, miura 20/6/30).

---------------------------------------------------------------------------------------------------------------------------------
## 2. (b) suspensions.ini [DAMAGE] - steer-rod deflection

formula_k comments [VERIFIED]: `MIN_VELOCITY=30 ; MINUMUM VELOCITY TO START TAKING DAMAGE`, `GAIN=0.0006 ; AMOUNT OF STEER ROD DEFLECTION FOR
IMPACT KMH`, `MAX_DAMAGE=0.05 ; MAXIMUM AMOUNT OF STEER ROD DEFLECTION ALLOWED`, `DEBUG_LOG=1 ; ACTIVATES DAMAGE DEBUG IN THE LOG`.
- **What it changes: the steer-rod LENGTH (m)** [VERIFIED name: acs.exe `Suspension::setSteerLengthOffset`, `INIT ROD LENGTH:%f`]. In AC the
  steer rod (WBCAR_STEER -> WBTYRE_STEER) is a distance joint whose length is also how TOE_OUT is set ("Toe-out expressed as the length of
  the steering arm in meters", formula_k). A length offset = a toe change. Units: metres, same as TOE_OUT [INFERRED from the shared
  mechanism; MAX_DAMAGE 0.05 m vs TOE_OUT ~0.0001..0.0004 m].
- **Size**: offset = GAIN * impact km/h above MIN_VELOCITY, capped at MAX_DAMAGE [INFERRED; whether it is `GAIN*(v - MIN)` or `GAIN*v`
  once v > MIN is UNKNOWN - recommend `GAIN * (v - MIN_VELOCITY)` (continuous at the threshold) // ours]. Accumulates per corner up to
  MAX_DAMAGE [INFERRED].
- **Toe angle it gives** (our `AcAxleSpec.ToeDegFor`, atan(len / WBTYRE_STEER z)): ferrari_458 front arm z 0.140 -> MAX 0.02 m = 8.1 deg;
  typical car arm 0.10..0.15 m -> MAX 0.05 m = 18..26 deg (a wrecked corner); 80 km/h wall hit on a GAIN 0.0004 / MIN 40 car ->
  0.016 m -> ~6..9 deg. Numbers checked on 458 / miata / e30 / abarth500 suspensions.ini (WBTYRE_STEER z 0.106..0.153 front,
  -0.045..0.19 rear).
- **Which wheels**: all four. The displayer has SUSPENSION FL/FR/RL/RR and sus_fl..rr icons, CSP exposes `suspensionDamage` per wheel,
  and every owned car has WBCAR_STEER / WBTYRE_STEER on the REAR axle too (rear toe links) [VERIFIED keys; damage on rears INFERRED].
  Which corner a hit damages: the one(s) nearest the contact point [INFERRED; AC's rule UNKNOWN].
- **Sign / direction [UNKNOWN]**: nothing documents it. Recommended // ours: the rod is pushed by the impact - the offset sign follows the
  lateral component of the impact on that corner (a hit on the front-left wheel from the left toes it in), so damage visibly pulls the car
  to one side; the displayer value is |offset| / MAX_DAMAGE.
- **Impact velocity**: AC fixed "wrong impact speed calculation for car to car contact" (1.0.2), so it is a relative contact speed;
  normal vs total component UNKNOWN. Use the normal relative speed at the contact (what our OnCollisionEnter already computes, 6).
- 1.3.1 "GT-R ... correct damage under full lock" hints that rod geometry at full lock could register as damage - irrelevant for us.
- FMOD: the wheel event has `suspension_damage` (common.bank flutter_4 "damage" layer - research ac_physics_sound_rules 469) -> feed
  |offset| / MAX_DAMAGE (0..1) [INFERRED scale].

---------------------------------------------------------------------------------------------------------------------------------
## 3. (c) engine.ini [DAMAGE] - engine life

Keys [VERIFIED]: `RPM_THRESHOLD`, `RPM_DAMAGE_K`, `TURBO_BOOST_THRESHOLD`, `TURBO_DAMAGE_K` (formula_k has no [DAMAGE] in engine.ini; the
comments come from shipped/modded cars): "TURBO_DAMAGE_K=5 ; amount of damage per second per (boost - threshold)", "RPM_THRESHOLD=9200 ; RPM at
which the engine starts to take damage", "RPM_DAMAGE_K=1 ; amount of damage per second per (rpm-threshold)", "TURBO_BOOST_THRESHOLD=2.0 ;
level of TOTAL boost before the engine starts to take damage" **[DOCUMENTED: https://www.gtplanet.net/forum/threads/assetto-corsa-pc-mods-general-discussion.307899/post-12843982
and post-12844009 - community-quoted ini comments]**. So:
```
lifeLoss/s = RPM_DAMAGE_K * max(0, rpm - RPM_THRESHOLD) + TURBO_DAMAGE_K * max(0, totalBoost - TURBO_BOOST_THRESHOLD)   (x damage multiplier [INFERRED])
```
- **Life scale**: CSP's `engineLifeLeft` (0 = blown); a new engine = **1000** [BELIEF, not confirmed by any page I could fetch]. Consistent
  with the K values: a 500 rpm over-rev at K 1 = 500/s -> ~2 s to destroy; a 2000 rpm money shift kills in ~0.5 s - matches AC's reputation
  ("one bad downshift blows it"). `system/cfg/engine_smoke.ini [TRIGGERS] ENGINE_LIFE=25` [VERIFIED] = smoke below 25 (units of life ->
  2.5 % of 1000? or 25 %? [UNKNOWN]); `[LIFE_TIME] SECONDS=15`. Telemetry `ELIFE`, displayer "ENGINE LIFE : ".
- **Effect on power [UNKNOWN]**: nothing documents a gradual power loss. 0.21.6 "AI cars now try to park on the side if they have a
  broken engine" + CSP "Exploding engine ... engineLifeLeft = 0" -> **at 0 the engine is dead** (no torque, stops; no restart until repaired /
  back to pits). Recommended: no power change above 0 (// ours, as nothing in the files describes one), smoke below ENGINE_LIFE, dead at 0.
- Pit: ENGINE_REPAIR_TIME_SEC per 10 % -> engine % = (1000 - life) / 10 [INFERRED].
- AC's GUI shift lights consider "damage rpms as well as limiters" (1.7) -> HUD red line = min(LIMITER, RPM_THRESHOLD).

---------------------------------------------------------------------------------------------------------------------------------
## 4. (d) gearbox, tyres, brakes

- **Gearbox**: `drivetrain.ini [DAMAGE] RPM_WINDOW_K=100` on 54 cars [VERIFIED]: all 46 cars with `[GEARBOX] SUPPORTS_SHIFTER=1` have it
  (+8 sequential cars). Changelog 0.22.17 "drivetrain damage for cars with H shifter gearboxes" [VERIFIED]; displayer "GEAR :" +
  gearbox.png; CSP `DAMAGE_GEARBOX` 0..1. **Mechanism [UNKNOWN]**; most plausible [INFERRED]: a grind (an H-shifter change without the
  clutch, outside `VALID_SHIFT_RPM_WINDOW` - the name pairs with RPM_WINDOW) costs gearbox life proportional to the rpm mismatch /
  RPM_WINDOW_K. The money shift itself is ENGINE damage (3), not gearbox. Effect of a damaged gearbox: [UNKNOWN] (CSP only exposes 0..1).
  Recommended: count it (display + pit) and keep it without a driving effect until evidence (// ours), or skip in iteration 2.
- **Tyres**: puncture / blow-out sounds exist (0.22.20; FMOD `inflation` 0..1, `flat_tyre_mono`, `tyre_explosion` at inflation 0..0.1).
  Trigger in vanilla AC = **tyre over-temperature** ("Tyre explosion temperature ... tyres.ini [EXPLOSION] TEMPERATURE=350", 1.10; "thermal
  value for exploded tyres", 1.14) [VERIFIED changelog]. No owned car sets it (built-in default UNKNOWN). Impacts do not puncture tyres in
  vanilla as far as the files show [INFERRED]. **We have no tyre thermal model** (AcTyreModel: "With no thermal model (// ours)"), so tyre
  explosion cannot be driven like AC: skip (or // ours rule). `flatspot_*` icons + system/cfg/physics.ini `[TYRES] FLAT_SPOT_K=0.15
  FLAT_SPOT_FF=0.05` = flat spots from lock-ups (wear, not "Damage"; 1.9 ties blister/grain to Wear).
- **Brakes / wheels**: no key, no displayer item -> **AC has no brake or wheel damage** [VERIFIED by absence].

---------------------------------------------------------------------------------------------------------------------------------
## 5. (e) Repair: pit stop, reset, back to pits

- car.ini `[PIT_STOP] BODY/ENGINE/SUSP_REPAIR_TIME_SEC` = seconds per 10 % of damage (formula_k comment "time spent to repair 10% of body
  damage") [VERIFIED]. Damage % per system [INFERRED]: body = max zone km/h / MAX_DAMAGE_KMH (60, damage_displayer.ini) * 100;
  suspension = |rod offset| / MAX_DAMAGE * 100 (worst corner); engine = (1000 - life) / 10.
- AC's pit stop [VERIFIED strings + gui art; flow INFERRED]: stop in your own pit box in the pit lane -> the pit app (or the quick menu
  `QUICK_MENU_PITSTOP`, presets `PITSTOP_PRESETS_`, `system/cfg/pitstop.ini AUTO_APP_ON_PITLANE=1`) offers tyres, fuel and three repair
  toggles REPAIR BODY / REPAIR BODY + SUSPENSION / REPAIR ENGINE, shows ESTIMATED PIT STOP TIME (tyres x TYRE_CHANGE_TIME_SEC + litres x
  FUEL_LITER_TIME_SEC + repair seconds - sum vs max is UNKNOWN), CONFIRM -> car held for that time, then released repaired. Since 1.12 the
  suspension repair also fixes the body. Pit stops exist in practice / qualify / race (1.11.0, 1.?). Forum: repair options live in the pit
  menu strategy presets **[DOCUMENTED: https://steamcommunity.com/app/244210/discussions/0/1696040635923810614]**.
- **Back to pits (ESC menu "PITS / Back to pits")**: the forum thread above uses it as the way to get a repaired car ("not able to repair
  my vehicle without pausing and selecting back to pits") -> back to pits = full repair [DOCUMENTED-community, INFERRED]. Mid-race it is
  allowed but costs time / penalty in AC; ours already refuses it during the race (AcRace.BackToPits).
- **"Reset car"** (our R key / AcRace.ResetCar - AC itself has no reset-to-track in normal sessions; CM / CSP add one): no AC rule. Recommended
  // ours: reset keeps damage (it is a teleport, like AC's "jump start: teleport to pits" keeps state), back to pits repairs everything.
- **Iteration 1 has no pit stop at all** (grep: no PIT_STOP / pit-service code; AI "no pit stops", tops up fuel instead). Repairs are
  therefore: back to pits / new session / car change = new car (full life). A real pit service is a separate R/F feature.

---------------------------------------------------------------------------------------------------------------------------------
## 6. Mapping onto OUR physics (exact plug points)

### 6.1 Collision detection today [read in code]
`Game/Physics/AcCarPhysics.cs` 888-906:
- `OnCollisionEnter(c)` -> `Impact(c, true)`; `OnCollisionStay(c)` -> `Impact(c, false)` at most 10 Hz (scrape repeats for audio).
- `Impact`: skipped when `!_ready || Puppet || Body == null || Body.isKinematic`; uses only `collision.GetContact(0)`;
  kmh = enter: `|dot(relativeVelocity, cp.normal)| * 3.6`, stay: `|relativeVelocity| * 3.6`; ignored below 3 (hit) / 5 (scrape) km/h;
  raises `AcEvents.RaiseCarImpact(rig, kmh, cp.point)` (`Game/Shared/AcEvents.cs` 25/60, `Action<AcCarRig, float, Vector3>`), which A's
  `AcCarAudio.OnCarImpact -> Collision(kmh, point)` turns into hit / scrape sounds.
- Consequences for damage: (1) **only ENTER events with the normal speed should damage** (stay events carry the sliding speed - scraping
  a wall at 200 km/h must not wreck the car); keep the existing event untouched for audio and add a separate damage call from
  `Impact(entered == true)`. (2) Local and AI cars are dynamic -> get damage; remote cars are puppets (kinematic) -> their own owner
  computes damage; a local car hitting a remote puppet sees the puppet's MovePosition velocity in `relativeVelocity` (SetPuppet comment) ->
  car-to-car works. (3) Use `cp.point` -> body-local (`transform.InverseTransformPoint`, Unity axes) for the zone and corner; use
  `cp.normal` / `collision.impulse` for the direction. Consider averaging all contacts (`GetContacts`) - allocation-free with a
  preallocated `ContactPoint[]`.
- Zone rule // ours (AC's UNKNOWN): local point p vs the car's half extents (Spec collider boxes / model bounds): |p.z| beyond 0.6 of the
  half length -> FRONT/REAR by sign, else LEFT/RIGHT by sign of x (Unity +x = car right); hits from above/below (normal mostly vertical,
  e.g. landing) -> no zone or CENTRE. Corner for the steer rod: nearest wheel hub (`Chassis.Wheels[i].Hub`, body-local) within ~0.8 m
  // ours.

### 6.2 Where each input goes
New state - one Unity-free class `AcCarDamage` in `Data/Physics/` (P owns it; harness-testable), held by `AcCarPhysics` (`Damage` field),
created in `Init` next to `Core = new AcCarCore(spec)` (line 139) / `Chassis = new AcChassis(...)` (150):
`float[] Zone = new float[5]` (km/h; F, R, L, R, centre), `float[] RodOffset = new float[4]` (m, signed), `float EngineLife = 1000`,
`float Gearbox` (0..1), `bool EngineDead`, `float Multiplier` (= AcRules.DamageMultiplier / 100; 0 = damage off), `Repair(body, susp,
engine)`, `Reset()`.
Parsing - `AcCarSpec` / `AcCarSpecParts` (currently only `AcEngineSpec.DamageRpmThreshold`, AcCarSpec.cs 291, "informative"):
- `AcWingSpec` + `float ZoneFrontCl, ZoneFrontCd, ZoneRearCl, ZoneRearCd, ZoneLeftCl, ZoneLeftCd, ZoneRightCl, ZoneRightCd` (parsed where
  wings are built, AcCarSpec.cs ~606 `Wings.Add(spec)`).
- `AcAxleSpec` or a new `AcSuspDamageSpec` on AcCarSpec: `MinVelocity, Gain, MaxDamage` (suspensions.ini [DAMAGE]; absent on 3 cars).
- `AcEngineSpec`: + `DamageRpmK, TurboBoostThreshold, TurboDamageK` (absent turbo keys on 2 cars -> no turbo damage).
- `AcDrivetrainSpec`: + `DamageRpmWindowK` (0 when absent = no gearbox damage).
- `AcCarSpec`: + `PitBodyRepairSec, PitEngineRepairSec, PitSuspRepairSec` (+ the TYRE / FUEL times if R builds a pit service).
Effects:
| damage | plug point | how |
|---|---|---|
| aero zones | `Data/Physics/AcAeroModel.Wing(...)` lines 27-39 | add the 4 zone values (or a precomputed per-wing `clDiv = 1 + sum ZONE_x_CL*Zone[x]`, `cdMul = 1 + sum ZONE_x_CD*Zone[x]`) and apply `cl /= clDiv` (sign kept), `cd *= cdMul` before `downforce = q*cl` / `drag = q*cd`. Caller: `AcCarPhysics.Aero` line 758 (per wing, per FixedUpdate; no allocation). |
| steer rod | `AcChassisWheel.ToeDeg` (AcChassis.cs 22; set at 83 `ToeDeg = ax.ToeDegFor(ax.ToeOut)` and by setup `AcCarPhysics.cs 483` TOE_OUT_xx) | keep the setup toe length separately (add `AcChassisWheel.ToeLen`, m) and set `ToeDeg = Axle.ToeDegFor(ToeLen + RodOffset[i])`; `Prepare` (AcChassis.cs 165) already turns ToeDeg into the road angle of all 4 wheels (left wheel mirrored). The setup path at 483 must then write ToeLen and recompute. Cars with SteerArmZ < 2 cm get 0 deg from ToeDegFor (guard) -> no visible damage (check per car). |
| engine life | `AcCarCore.Step` engine section (lines ~254-295): `Rpm`, `BoostTotal` are there | after the turbo loop: `life -= h * mult * (RPM_K*max(0,Rpm-RPM_THR) + TB_K*max(0,BoostTotal-TB_THR))`; when life <= 0: `gas = 0` and torque = coast only, idle controller off (engine stops; the existing stall path / `EngineOmega` decays). Either inside AcCarCore (it is sub-stepped at 333 Hz: correct for short money-shift spikes - preferred) or read back once per FixedUpdate in AcCarPhysics (would miss sub-step peaks). |
| gearbox | `AcCarCore.RequestGear` line 194 (`Grinds++` on a refused H-shift) | `Gearbox += mult * |Rpm - need| / (RPM_WINDOW_K * 1000)` // ours scale, only when DamageRpmWindowK > 0. |
| telemetry | `Game/Shared/AcTelemetry.cs` (lead file) + `AcCarPhysics.BuildTelemetry` (779) | new fields (via REQUESTS to the lead): `DamageZone0..4` or a small struct, `SuspDamage[4]` (0..1 = |off|/MAX), `EngineLife`, `GearboxDamage`, `EngineDead`. N needs them in the player record for remote smoke / visuals (AcNetBlocks payload), H for a damage displayer (AC: show for `TIME=4` s after a hit, 100 % at `MAX_DAMAGE_KMH=60`). |
| audio | `Game/Audio/AcEventPlayer.cs` 50-57 holds `inflation = 1`, `suspension_damage = 0` constant | A feeds the new telemetry (max corner damage 0..1) -> flutter_4 / damage layers start sounding. |
| visuals | C's `AcCarModel` (separate topic: damage.ini, txDamage/DamageMask, glass) | reads `Zone[]` km/h against damage.ini MIN_SPEED/FULL_SPEED. |
| smoke | engine_smoke.ini (system/cfg) | W/C particle when EngineLife < `[TRIGGERS] ENGINE_LIFE`. |
Repairs: `AcCarPhysics.ResetTo` (370-394) is called by spawn, grid, back-to-pits AND reset-to-track (`AcRace.ResetToTrack` ->
`AcSpawner.Place`) and the fall-through recovery - so **do not clear damage inside ResetTo**. Clear it in R's `PlaceInPits` path
(`AcRace.BackToPits` 452 / Flow.cs 82-152: back to pits, session change, qualify from pits) and on a new rig (`AcCarRig.Spawn`, car change)
via a new `AcCarPhysics.RepairAll()`; a future pit service calls `Repair(body, susp, engine)` and waits the PIT_STOP seconds.
Multiplier: set `Damage.Multiplier = AcRules.DamageMultiplier / 100` where the fuel / wear rates are set (AcCarRig.cs 229-238 rules hook,
AcRace.Flow.cs 513-514, AcAiField.cs 281 for AI). Defaults today: `AcRules.DamageMultiplier = 0` (AcSession.cs 46), `AcNetBlocks.Damage` 0, and AcRaceDefaults
does NOT read server_cfg DAMAGE_MULTIPLIER (it reads FUEL_RATE / TYRE_WEAR_RATE at lines 62-63) - so damage is OFF until someone moves the
lobby slider; decide the default with the user (AC's default single-player assists.ini DAMAGE is the player's choice; this
install has DAMAGE=0).

### 6.3 Net
Damage is owner-computed (each client its own car; master its AI). Remote cars only need what is drawn / heard: zones (5 x u8 km/h),
suspension (4 x u8), engine life (u8 /4 or a dead+smoke bit). AC itself sends `ACP_DAMAGE_UPDATE` with the zone floats ("Damage zone %d:
%f") and shows remote damage since 1.5. That is N's slot payload (AcNetBlocks Player record) - additive, via REQUESTS.

---------------------------------------------------------------------------------------------------------------------------------
## 7. Open questions / uncertainty (be honest)
1. Impact km/h -> zone km/h curve, cap, MAX vs SUM, the "final damage" for high speed: UNKNOWN (AC binary only). Our rule is // ours.
2. Steer-rod formula (`GAIN*(v-MIN)` vs `GAIN*v`), sign, which corners per hit: UNKNOWN; mechanism (rod length -> toe) VERIFIED by name.
3. Engine life 1000 scale: BELIEF (CSP); per-second linear damage: community-quoted ini comments. Power loss before 0: none documented.
4. Gearbox damage rule and effect: UNKNOWN beyond "H-shifter cars" + RPM_WINDOW_K.
5. Tyre explosion default temperature and puncture from impacts: UNKNOWN / not applicable (no thermal model).
6. Whether "Back to pits" repairs in AC: community statement, not a Kunos document.
To settle 1-2 the user could run AC with any car (suspensions.ini DEBUG_LOG=1 is already set on 82 cars) and read
`Documents/Assetto Corsa/logs/log.txt` after a crash: the "Damage zone %d: %f" / steer-rod debug lines give the real numbers.
The current log.txt (2025-03-29, damage off) has none.

---------------------------------------------------------------------------------------------------------------------------------
## Critic addendum (2026-11-02, mechanical) - OVERRIDES the text above where they differ

Checked against the install (harness `mechdmg`, `mechkeys`, `dmgcrit` and my new `mechcrit` = `DevTools/harness-ac/ProbeDmgMechCrit.cs`),
the sdk formula_k files, `changelog.txt`, acs.exe strings (ASCII + UTF-16 pulled with a PowerShell regex, key names only), and the project code.

### What holds [VERIFIED in install]
- The whole census table in section 1: every count and value matches `dmgcrit`:
  - aero zones on 291 wings, plus 1 FIN at 0;
  - suspensions [DAMAGE] on 83 cars (the 3 Audi Quattros have none);
  - engine [DAMAGE] on 86 cars, the turbo keys on 84;
  - RPM_WINDOW_K on 54 cars, always 100;
  - PIT_STOP 20/2/30, with exos 5 and miura 6.0;
  - DEBUG_LOG=1 on 82 cars.
  No other damage-ish key exists: no tyres.ini [EXPLOSION], no brake or wheel key.
- formula_k comments:
  - aero: `CL=CL/(1.0+ZONE_0_CL*DAMAGE)`, `CD=CD*(1.0+ZONE_0_CD*DAMAGE)`;
  - suspensions [DAMAGE] comments;
  - car.ini: "time spent to repair 10% of ... damage".
  Also verified: damage_displayer.ini, engine_smoke.ini (ENGINE_LIFE=25, SECONDS=15) and the physics.ini FLAT_SPOT values.
- acs.exe strings:
  - the displayer labels, repair buttons and damage icons;
  - `Suspension::setSteerLengthOffset` and `INIT ROD LENGTH:%f`;
  - telemetry ELIFE, and `EXPLOSION` among the tyres keys;
  - `MIN_VELOCITY|MAX_DAMAGE|DEBUG_LOG` right after the suspension keys (`TOE_OUT`, `STATIC_CAMBER`);
  - `TURBO_BOOST_THRESHOLD|TURBO_DAMAGE_K|RPM_THRESHOLD|RPM_DAMAGE_K` next to `ctrl_turbo|ctrl_wastegate`;
  - `RPM_WINDOW_K` next to the gearbox keys.
- The changelog quotes and their versions are right, except the one corrected in item 5.
- Every code line I checked is right:
  - AcCarPhysics 139, 150, 370, 483, 758, 779, 890-906;
  - AcCarCore 194, 291; AcCarSpec 291, 606; AcCarSpecParts 136; AcChassis 83; AcEvents 60;
  - AcRace 452; AcRace.Flow 513-514; AcCarRig 229-238; AcAiField 281; AcSession 46;
  - AcRaceDefaults 62-65 (line 65 notes that DAMAGE_MULTIPLIER is deliberately not read).

### Corrections
1. **Tag upgrade, section 3.** The engine [DAMAGE] comments are in Kunos' own data.acd in this install:
   - cars: pagani_huayra, bmw_z4_drift, bmw_1m_s3, lotus_98t;
   - comments: "level of TOTAL boost before the engine starts to take damage", "amount of damage per second per (boost - threshold)",
     "amount of damage per second per (rpm-threshold)".
   So they are **[VERIFIED in install]**, not only [DOCUMENTED: gtplanet].
2. **The turbo over-boost census is wrong (sections 1 and 3).**
   - Why: `mechdmg` takes max(MAX_BOOST) and max(WASTEGATE) across turbos. But the threshold is on TOTAL boost (the sum, as AcCarCore
     does), and the probe ignores `[ENGINE_DATA] DEFAULT_TURBO_ADJUSTMENT`.
   - Redone in `mechcrit`: the sum of min(WASTEGATE, MAX_BOOST), times the default adjustment on cockpit-adjustable turbos.
   - Result: **no owned car over-boosts at default settings.**
     - quattro_rally: 1.5 x 0.9 = 1.35 < 1.4.
     - bmw_1m_s3 / bmw_z4_drift: 1.38 x 0.8 = 1.104 < 1.11. This matches changelog 0.6.5 line 2611 ("keys 1-0 for 10% to 100% turbo
       boost. 80% default") and "over 80%".
     - lotus_98t: 4.0 x 0.6 = 2.4 < 3.0.
     - ruf_yellowbird (adjustment 0.8) never over-boosts.
   - These 4 cars over-boost only when the turbo adjustment is turned up (at 100 %).
3. **Code bug that blocks turbo damage [VERIFIED in code + install].**
   - The bug: `AcCarSpec.cs` 304 reads `DEFAULT_TURBO_ADJUSTMENT` from each `[TURBO_n]` section. In the files it sits in `[ENGINE_DATA]`
     (bmw_z4_drift engine.ini line 14). So `DefaultAdjustment` is always 1, and AcCarCore 284 runs adjustable turbos at their full wastegate.
   - The effect: turning on turbo damage as section 6.2 proposes would wreck 4 cars **at stock settings**, all the time:
     z4_drift 30 x 0.27 = 8.1 life/s, lotus_98t 10 x 1.0 = 10/s, 1m_s3 1.35/s, quattro_rally 7 x 0.1 = 0.7/s.
   - The fix: read [ENGINE_DATA] first, falling back to the turbo section. This also changes iteration-1 power on these 4 cars
     (they make too much boost today). P must fix it before any damage work; raise it via REQUESTS.
4. **Setup-adjustable limiters were missed (section 3).**
   - 15 cars have setup.ini `[ENGINE_LIMITER]` (MIN..MAX %). On 4 of them the top of the range passes RPM_THRESHOLD:
     - ks_alfa_33_stradale 109 %: 10028 > 9500;
     - ferrari_312t 108 %: 13068 > 12300;
     - lotus_98t 108 %: 12528 > 12300;
     - lotus_49 108 %: 10044 > 9900.
   - Changelog 0.22.21: "Lotus 49, Lotus 98T, Ferrari 312T have lower default rev limiters. Can go over 100% but damage may occur".
   - Our AcCarCore (~264) clamps `EngineLimiterFraction` to 0.5..1, so >100 % is impossible here today. Either allow >1 together with
     damage, or say plainly that we don't support it.
5. **aidMechanicalDamage came in 1.5, not 1.9.3.** Changelog line 1139 is in the 1.5 block (lines 1036-1164); 1.9.3 holds only DX11 and
   refresh-rate fixes.
6. **"Damage zone %d: %f" is a multiplayer string.**
   - Evidence [VERIFIED adjacency]: it sits in the ACCLIENT block, next to `requesting car list` and
     `ACCLIENT ERROR: UNEXPECTED ACP_CAR_LIST_RESPONSE`.
   - So it is probably logged when a remote car's damage arrives online, not after a local crash [INFERRED].
   - Section 7's advice (crash offline, then read log.txt for these lines) may therefore produce nothing. The suspension DEBUG_LOG output is
     more likely the `INIT ROD LENGTH:%f` / steer-offset lines; the format is UNKNOWN.
7. **`ZONE_` (UTF-16) is not aero evidence.** That literal sits in the `/data/drs_zones.ini|ZONE_|DETECTION` block. No `ZONE_FRONT_CL` or
   `_CL` literal exists in acs.exe. The aero key names are verified only from the aero.ini files, which is enough.
8. **Gearbox (section 4).**
   - The 8 cars with RPM_WINDOW_K but SUPPORTS_SHIFTER=0 are road DCT / automatic cars, not "sequential" cars: audi_r8_plus, sesto_elemento,
     levante, quattroporte, nissan_gtr, cayenne, macan, panamera.
   - The other 32 SUPPORTS_SHIFTER=0 cars have no key.
   - Missed changelog line: 1.5 "Fixed Lamborghini Miura gearbox damage". The miura's VALID_SHIFT_RPM_WINDOW is only 100 rpm, so it
     grinds easily under our rule.
9. **Steer-rod -> toe numbers (section 2) are an artefact of atan(len / armZ) on small arms.**
   - The fleet range is wider than the 4 cars sampled: front arm z -0.132..0.205 m, rear -0.40..0.225 m.
   - Several rear arms are only 0.028..0.045 m: lotus_exos 0.028; abarth500, mito, giulietta, z4_gt3 0.045.
   - On those, MAX_DAMAGE 0.05 m gives 47..61 deg. The miata front gives 46 deg (0.11 on a 0.106 arm). An offset longer than the arm
     itself cannot be a real angle.
   - Fix: clamp the damaged toe (// ours, e.g. |toe| <= 10..15 deg) or use asin with a clamp.
   - Many cars have a NEGATIVE arm z (a1s1, giulietta, elise / exige / 2-eleven, gtr, gt86). So the sign of the damage toe flips per car:
     define it on the toe direction, not on the raw length.
   - The formula choice matters a lot on ks_mazda_miata (MIN 85, GAIN 0.005). A 90 km/h hit gives 0.025 m with GAIN*(v-MIN), but 0.45 m
     with GAIN*v (instantly at the 0.11 cap). Both readings stay UNKNOWN.
10. **The repair hook (section 6.2) contradicts its own rule.**
    - `PlaceInPits` (AcRace.Load.cs 311) is also used for:
      - the jump-start penalty (Flow.cs 315);
      - the reset fallback when there is no racing line (Flow.cs 554);
      - "late for the start" (Flow.cs 158);
      - the session start (Load.cs 307).
    - Clearing damage inside PlaceInPits would therefore repair on a jump-start penalty and on R-reset, which breaks "reset keeps damage".
    - Instead call `RepairAll()` from BackToPits (452), the session-transition sites (Flow.cs 82/84/152) and car spawn.
11. **The audio row (6.2) is incomplete.**
    - `AcFeed.Constant` (AcEventPlayer.cs 52-57) hard-codes Inflation=1 and SuspDamage=0.
    - Line 167 marks every instrument whose region excludes that constant as `_skip`, so it is never decoded.
    - Feeding telemetry is therefore not enough: A must also remove SuspDamage (and Inflation, if punctures ever exist) from `Constant()`.
12. **Still unconfirmed: engine life = 1000.** No public CSP page found; GitHub code search needs a login. Keep it as BELIEF and make the
    scale one `// ours` constant.
