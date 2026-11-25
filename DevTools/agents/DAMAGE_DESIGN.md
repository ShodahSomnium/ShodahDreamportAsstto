# Assetto Corsa Dreamport - lead design, iteration 2: CAR DAMAGE (BINDING)

Owner: the lead. Date 2026-11-02. Every module agent reads this after `DevTools/AGENTS.md` and `DevTools/agents/DESIGN.md` (iteration 1,
still binding). Evidence: `research/dmg_collision.md`, `dmg_visual.md`, `dmg_mechanical.md`, `dmg_seams.md` (their critic addenda
OVERRIDE their bodies; this file overrides all four where they differ). Tags: **[V]** verified in the install (harness / file read),
**[D: url]** public document, **[I]** inferred (our reading, not checked against a running AC), **// ours** = invented number, with its
value and reason. All AC values are read at run time from the player's install; nothing is copied into code.

User request: "car damage like the game". Binding constraints from the request: lobby DAMAGE 0-100 %; **0 = off = iteration-1 behaviour
exactly**; owner-authoritative net; protocol 2 -> 3; late joiners and remote cars show current damage; AI cars take damage too.

---------------------------------------------------------------------------------------------------------------------------------
## 0. What AC has, and what we build (scope table)

| AC feature (evidence) | Iteration 2 | Notes |
|---|---|---|
| 5 body zones FRONT 0, REAR 1, LEFT 2, RIGHT 3, CENTER 4, in km/h of impact [I-strong: damage_displayer `MAX_DAMAGE_KMH=60`, every damage.ini threshold is km/h, aero `CD*(1+ZONE*DAMAGE)` with ZONE <= 0.015, suspensions `GAIN ... FOR IMPACT KMH`; order D: ACC shared-memory doc v1.7.6] | BUILD | rules section 1 |
| Paint dents / scratches by zone (ksPerPixelMultiMap_damage_dirt: txNormal = dent NM, txDamage t4, txDamageMask t21, `damageZones` float4) [V RDEF, Pipeline PDF] | BUILD | section 2.1 |
| Cracked glass per zone (`DAMAGE_GLASS_<ZONE>_n`, ksBrokenGlass, `content/texture/DAMAGE_GLASS.dds`, damage.ini `[DAMAGE_GLASS_*] MIN/FULL_SPEED`, `GLASS_THRESHOLD=0.65`) [V] | BUILD | section 2.2 |
| Bent + oscillating panels (`[VISUAL_OBJECT_n]`, `[OSCILLATIONS] ENABLED`) [V keys, I maths] | BUILD | section 2.3 |
| Aero zone damage (aero.ini `ZONE_*_CL/CD`, formula in every aero.ini) [V] | BUILD | section 3.1 |
| Steer-rod deflection (suspensions.ini `[DAMAGE]`, acs.exe `Suspension::setSteerLengthOffset`) [V keys, I formula] | BUILD | section 3.2 |
| Engine life from over-rev / over-boost, blown engine (engine.ini `[DAMAGE]`, comments in Kunos' own data.acd; changelog 0.6.5 / 0.21.6 "broken engine") [V] | BUILD | section 3.3 |
| Engine smoke when damaged (`system/cfg/engine_smoke.ini`, changelog 0.6.5) [V] | BUILD | section 2.4 |
| Gearbox damage (drivetrain `RPM_WINDOW_K` on 54 cars) [V key, mechanism UNKNOWN] | **NOT built** | fallback R7 |
| Tyre puncture / explosion (tyres.ini `[EXPLOSION] TEMPERATURE`, set on no owned car; needs a tyre thermal model we do not have) | **NOT built** | inflation stays 1 |
| Flat spots (physics.ini FLAT_SPOT_K; tied to Wear, not Damage, changelog 1.9) | NOT built | not damage |
| Collision sparks (`system/cfg/sparks.ini`) [V file, trigger I] | OPTIONAL (C, last) | section 2.5 |
| Damage multiplier `DAMAGE_MULTIPLIER` 0-100 step 10, launcher label "Mechanical damage" [V] | BUILD (see 1.6 for the 0 rule) | |
| Pit repair: REPAIR BODY / BODY + SUSPENSION / ENGINE, seconds per 10 % from car.ini `[PIT_STOP]` [V strings, art, keys] | BUILD | section 4 |
| HUD damage displayer (`DamageDisplayer`, damage_displayer.ini, `content/texture/damage/*.png`) [V] | BUILD | section 5 |
| Damage sounds: common.bank collision hits/scrapes (exist), car-bank wheel `suspension_damage` -> `flutter_4` [V]; no glass-break / engine-blow event in any bank [V] | BUILD (suspension flutter, repair screw/unscrew) | section 6 |
| Remote damage visible (changelog 1.5; acs.exe `Sending ACP_DAMAGE_UPDATE`) [V] | BUILD | section 7 |

**Bug fixes that ship with iteration 2 independent of the damage setting** (the only changes visible at DAMAGE 0, each because the
iteration-1 output is wrong against AC; the user judges them):
- **G0 (W):** every `ksBrokenGlass` material is drawn with alpha x `glassDamage`, and the SHARED material gets `glassDamage = 0`. Today
  ktm_xbow_r (LOD0), lotus_exos_125 / _s1 (LOD1) and bmw_m3_e92 / _drift / _s1 (`R_DOOR_mesh_SUB3`, LOD0) build ACTIVE broken-glass
  meshes that our PerPixel draws as a grey film [V data, I picture - not lab-seen]. AC never shows them on an undamaged car.
- **P0 (P):** `AcCarSpec.cs:304` reads `DEFAULT_TURBO_ADJUSTMENT` from `[TURBO_n]`; the files have it in `[ENGINE_DATA]` [V
  bmw_z4_drift engine.ini]. Read `[ENGINE_DATA]` first, fall back to `[TURBO_n]`. This lowers boost on bmw_z4_drift, bmw_1m_s3,
  ks_audi_sport_quattro_rally, lotus_98t to AC's default (they over-boost today) and is a precondition for turbo damage (without it 4
  cars lose 0.7-10 life/s at stock settings).

---------------------------------------------------------------------------------------------------------------------------------
## 1. The rules (exact)

### 1.1 Run-time values (all read, none copied)
| Value | Source (read at run time) | Fallback if missing |
|---|---|---|
| `GlassThreshold` | `system/cfg/assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD` (0.65 here) | 0.65 // ours = the install value, only if the key is absent |
| `DisplayTimeS`, `MaxDamageKmh`, `PrintValues`, `PosX`, `CenterY` | `system/cfg/damage_displayer.ini [MAIN] TIME / MAX_DAMAGE_KMH / PRINT_VALUES / POSITION_X / DISTANCE_FROM_CENTER_Y` (4, 60, 0, 20, -200) | 4 / 60 // ours (same) |
| `SmokeEngineLife`, `SmokeSeconds`, smoke particle params | `system/cfg/engine_smoke.ini [TRIGGERS] ENGINE_LIFE`, `[LIFE_TIME] SECONDS`, `[SETTINGS]` | no smoke |
| `DisplayerOn` default | `Documents/Assetto Corsa/cfg/gameplay.ini [DAMAGE_DISPLAYER] IS_ACTIVE` (1 here) | 1 |
| Default lobby multiplier | `server/cfg/server_cfg.ini [SERVER] DAMAGE_MULTIPLIER` (100 here) | 100 // ours (= AC server default) |
| Per car, visual | damage.ini `[DAMAGE] INITIAL_LEVEL`, `[SCRATCHES] MIN_SPEED/MAX_SPEED`, `[OSCILLATIONS] ENABLED`, `[VISUAL_OBJECT_n]` (11 keys), `[DAMAGE_GLASS_<ZONE>] MIN_SPEED/FULL_SPEED` | see 2.x |
| Per car, mechanical | aero.ini `[WING_n]`/`[FIN_n] ZONE_{FRONT,REAR,LEFT,RIGHT}_{CL,CD}`; suspensions.ini `[DAMAGE] MIN_VELOCITY GAIN MAX_DAMAGE`; engine.ini `[DAMAGE] RPM_THRESHOLD RPM_DAMAGE_K TURBO_BOOST_THRESHOLD TURBO_DAMAGE_K`; car.ini `[PIT_STOP] BODY/ENGINE/SUSP_REPAIR_TIME_SEC` | absent key = that effect off (3 Audi quattros: no rod damage; miura_sv / 787b: no turbo damage) |

### 1.2 Damage state per car (`AcCarDamage`, Data/Physics, Unity-free)
- `Zone[5]` float km/h, FRONT 0, REAR 1, LEFT 2, RIGHT 3, CENTER 4. Starts 0, only grows until repair. **Cap 255 // ours**: the largest
  authored threshold is 220 (VISUAL_OBJECT FULL_SPEED) [V], so 255 keeps every visual reachable, bounds the aero extrapolation
  (front CD x4.8 at 255 with ZONE 0.015) and fits a u8 on the wire.
- `ZoneByte(i)` = `(byte)floor(Zone[i])`. **Every visual and the HUD use ZoneByte, on the owner too**, so the owner and every remote
  client draw identical glass / panels (the critic's quantisation rule). Mechanics use the float.
- `RodOffset[4]` metres (LF, RF, LR, RR), >= 0 = toe-out magnitude (sign rule 3.2).
- `EngineLife` float, **1000 = new // ours** (BELIEF from CSP `engineLifeLeft`; no page confirms; one constant `AcCarDamage.LifeNew`).
- `EngineDead` bool (life reached 0). `Version` int, bumped when any quantised value changes (ZoneByte, rod level u4, engine % u7,
  EngineDead). Consumers re-apply only on a Version change: zero per-frame cost.

### 1.3 Impact -> zone (P, owner side; AC's own rule is unpublished, so this whole section is // ours built on [I] evidence)
Run in `AcCarPhysics.OnCollisionEnter/Stay` (main thread, after the PhysX step), only for cars this client simulates (`!Puppet &&
!Body.isKinematic` - the existing gate; local car, and AI on the master). Remote and parked cars never compute damage.
1. **Pre-step cache** at the top of `AcCarPhysics.FixedUpdate`: `_v0 = Body.linearVelocity`, `_w0 = Body.angularVelocity`,
   `_com0 = Body.worldCenterOfMass` (3 Vector3 fields, no allocation).
2. Contacts: `collision.GetContacts(_contacts)` into a preallocated `ContactPoint[16]` // ours (16 > any convex-hull pair we saw).
   Skip a contact whose `thisCollider` is a floor box ("Ac Floor n", colliders.ini) - floor boxes are ground-only [V formula_k comment].
3. Per contact: `vA = _v0 + Cross(_w0, p - _com0)`. `vB`: other collider's `AcCarPhysics` -> puppet: `other.PuppetVelocity` (NEW getter
  of the velocity N passes to ApplyPuppet; kinematic bodies read ~0); dynamic: its own pre-step `PointVelocityPreStep(p)`; else
  `attachedRigidbody ? GetPointVelocity(p) : 0`. `v = |Dot(vA - vB, n)| * 3.6` km/h (normal component [I]: AC's UDP field is "Impact
  speed"; changelog 1.0.2 fixed "wrong impact speed calculation for car to car contact").
4. Event value: `vImp = max v` over the contacts kept, `p = mean point` of those contacts.
5. **Gates // ours:** `vImp < 3 km/h` -> nothing (same as the existing CarImpact gate). `OnCollisionStay` counts again only if
   `vImp >= 10 km/h` AND `>= 0.25 s` since this car's last counted hit (one timer per car, `_lastDamageAt`) - scraping a wall at 200 km/h
   must not wreck the car. The existing `CarImpact` path (10 Hz scrape repeats for audio) stays byte-identical.
6. **Surfaces // ours:** other collider with `AcSurfaceTag.Of(other) != null` (road, kerb, grass, sand - tagged physics surfaces) adds
   NO damage, except the rollover case: if `Dot(transform.up, Vector3.up) < 0` (upside down) the hit goes to CENTER. Walls (tag null) and
   untagged track meshes, cars and stand-in boxes add damage.
7. **Zone pick // ours:** `l = HullFrame.InverseTransformPoint(p)` in body axes (Unity: x right, y up, z forward), relative to the hull
   centre; `nx = l.x / halfW`, `nz = l.z / halfL`, `ny = (l.y - minY) / height`, hull = `Model.Hull` bounds (collider.kn5) in body
   space; if its centre is > 0.5 m from `Model.Bounds` centre (ks_ford_mustang_2015 hull is 5 m off [V ac_car_formats C3]) use
   `Model.Bounds`. Then:
   `zone = rollover ? CENTER : (ny > 0.7 && |nx| < 0.7 && |nz| < 0.7) ? CENTER : |nz| >= |nx| ? (nz > 0 ? FRONT : REAR) : (nx > 0 ? RIGHT : LEFT)`.
   Unity +x is car RIGHT (AC +X is car LEFT; the mask R channel = AC +X = LEFT [V dmguv]). 0.7 = // ours (roof box = central 70 %).
8. **Accumulate (SUM [I]):** `Zone[z] = min(255, Zone[z] + vImp)`. Sum because FULL_SPEED up to 220 is unreachable by one realistic
   hit and suspension GAIN is "per impact".
9. Car vs car: each simulated car does this for itself (dynamic-dynamic on the master: both AI cars; across clients each owner feels the
   other's puppet) [V code, AcNetGame.Cars.cs header]. Ghost rule (`AcNetGame.Contacts`) already removes pit-lane / just-placed contacts.
10. A counted hit also feeds the steer rod (3.2) with the same `vImp` and `p`, then sets the change flags; `CarDamaged` is raised once
    in the next Update (1.7).

### 1.4 Multiplier
`m = AcSession.Rules.DamageMultiplier / 100f` (0..1, launcher step 10). R writes it into `AcCarPhysics.DamageRate` in `ApplyRates`
(already runs for local and AI cars) and in `AcAiField` where FuelRate is set.
- **m == 0: damage is OFF and iteration 1 is reproduced exactly**: no accumulation, no CarDamaged, no damage texture slots loaded, the
  damage normal stays flattened (`FlattenDamageNormals` as today), HUD displayer hidden, wire damage ints all 0, no extra per-frame work.
  (Deviation from AC, deliberate, user rule: AC keeps visual damage with "Mechanical damage" off.)
- **m > 0:** zones accumulate RAW `vImp` (visuals, glass, panels, HUD body cells use the raw zone, as AC's VISUALDAMAGE is separate
  from the mechanical scale [V launcher label, `aidMechanicalDamage`]). Mechanics scale by m: aero uses `Zone*m`; rod increment `*m`;
  engine life loss `*m`.
- m changes mid-session from >0 to 0: R calls `Repair(AcRepair.All)` on every own car (// ours: "off" means no damage on the track).
  0 -> >0 mid-session: mechanics/glass/panels work at once; paint dents need the damage textures, which load at the next car spawn
  (back to pits / new session) - documented, logged once.
- Local setting `cars.visualdamage` (default on; AC's VISUALDAMAGE): off -> this client draws no damage on any car (zones still count).

### 1.5 Repair amounts (car.ini says "time spent to repair 10% of ... damage" [V formula_k]; the % definitions are // ours [I])
- `BodyPct = 100 * saturate(max(Zone[0..4]) / MaxDamageKmh)` (60 -> a 60 km/h hit is 100 %, the displayer's own full scale).
- `SuspPct = 100 * max_i(RodOffset[i]) / MAX_DAMAGE`; `EnginePct = 100 * (1 - EngineLife / LifeNew)`.
- Seconds: `body = BodyPct/10 * BODY_REPAIR_TIME_SEC`; `susp = SuspPct/10 * SUSP_REPAIR_TIME_SEC`; `engine = EnginePct/10 *
  ENGINE_REPAIR_TIME_SEC`. REPAIR BODY + SUSPENSION = body + susp seconds and clears both (changelog 1.12 "Suspension repair will
  automatically fix body too" [V]); total pit time = SUM of selected repairs (sum vs max UNKNOWN - // ours).

### 1.6 Where damage persists / clears (R decides; AC evidence where it exists)
| Action | Damage | Why |
|---|---|---|
| R key / RESET CAR (reset to track), fall-through recovery, jump-start "teleport to pits", no-racing-line fallback (all through `ResetTo` / `PlaceInPits`) | KEPT | not a repair in AC; else a free fix. **Never clear in ResetTo or PlaceInPits.** |
| BACK TO PITS (practice / qualify / hotlap; refused in race as today) | FULL REPAIR, instant | [D-community: https://steamcommunity.com/app/244210/discussions/0/1696040635923810614] |
| Pit-box repair (any session incl. race, changelog 1.11 "Pitstop is possible also in Practice") | the selected parts, timed (section 4) | [V strings + art] |
| New session, grid placement, car change, new rig | REPAIRED (a new rig is clean; grid calls `Repair(All)`) | [I] AC starts every session clean |
| Engine dead | car coasts, cannot restart until repair; in a race = retired (DNF) | changelog 0.21.6 [V], CSP "Exploding engine" [D] |

### 1.7 Events and order (main thread)
`FixedUpdate` (P): pre-step cache; `AcCarCore.Step` sub-steps engine life (333 Hz); aero reads per-wing cached factors.
`OnCollisionEnter/Stay` (P): 1.3 -> `AcCarDamage.AddImpact`; recompute the 8 aero factors per wing only if Version changed.
`Update/LateUpdate` (P push, existing): if `Damage.Version != _pushedVersion` -> `Model.SetDamage(...)`, `Model.SetEngineSmoke(...)`,
raise `AcEvents.CarDamaged(rig, flags, vImpMax)` once (flags accumulated since the last raise), `_pushedVersion = Version`.
N `Late` (LateUpdate 1000): remote records -> `Damage.ApplyWire` -> same push for remote rigs.

---------------------------------------------------------------------------------------------------------------------------------
## 2. Visual damage (C + W)

### 2.1 Paint (W shader, C instances)
- Only when `m > 0` at car spawn (`AcCarDamageGate.TexturesWanted`, see 8): `AcShading.Of` adds slots `txDamage` (t4), `txDust` (not
  bound - dirt is out of scope), `txDamageMask` (t21) for `ksPerPixelMultiMap_damage_dirt` (all 138 damage materials, 86/86 cars [V])
  and `ksPerPixelMultiMap_damage` (mod cars; mask = white, scalar fills all 4 zones). Missing texture -> `AcTextures.Black` (lotus_elise_sc
  x3 mask not embedded; NULL.dds x2) [V]. Resolve by SLOT, never by file name. Any mask size / DXT3 / DXT5 / BGRA32 [V]. ~6 MB GPU per
  car model when on, 0 when off.
- Shader `SomniumAssetto/PerPixel` (no new keywords; float switches; the CBUFFER layout identical in every pass):
  new `_DamageTex`, `_DamageMaskTex` (default black), `_DamageScratch` (float4), `_DamageDent` (float4), `_DamageSet` (x = damage paint
  on, w = broken-glass mode), `_GlassSet` (x = glassDamage). Components in **mask order (R = LEFT, G = REAR, B = RIGHT, A = FRONT)** [V
  dmguv on 5 cars]: C writes `(left, rear, right, front)`.
  Fragment, `if (_DamageSet.x > 0.5)`: `m = mask(uv0)`; `ws = saturate(dot(m, _DamageScratch))`; `wd = saturate(dot(m, _DamageDent))`;
  `nTS = normalize(lerp(float3(0,0,1), damageNM.xyz*2-1, wd))`; `albedo = lerp(albedo, scr.rgb, scr.a * ws)`;
  `refl *= lerp(1, damageNM.a, ws); spec *= lerp(1, damageNM.a, ws)` (Pipeline: NM alpha = scratched, less reflective [V doc]).
  Blend form [I] - Kunos' shader body was not read; tune only against AC screenshots.
- Weights per zone (C, from ZoneByte z): `scratch = saturate((z - SCR_MIN) / (SCR_MAX - SCR_MIN))` (0..20 on 86/86 [V]);
  `dent = saturate((z - INITIAL_LEVEL) / MaxDamageKmh)` // ours reading of INITIAL_LEVEL as "dents start here" [I weak: equals
  SCRATCHES MAX on 85 cars; 100 on ks_alfa_33_stradale, which has no panels]; full dent at INITIAL_LEVEL + 60.
- **At m > 0 C stops `FlattenDamageNormals`** (dent weight 0 = flat, so undamaged cars look the same); at m == 0 it runs as today.
- Per-car values: **material instances, not MPBs** (MPBs drop renderers out of the SRP Batcher and collide with the lights' MPBs). C
  clones a car's damage-paint materials lazily on the first non-zero ZoneByte (`new Material(shared)`, swaps `sharedMaterials` on every
  LOD set of THIS car), writes the two vectors via cached `Shader.PropertyToID` ids only on a Version change; repair writes zeros (clones
  kept); `Dispose` destroys clones. Undamaged cars: no clones, shared materials.

### 2.2 Glass (C nodes, W shader)
- Zone membership (C, at bind, per LOD set): a mesh belongs to zone Z if it or an ANCESTOR is named `DAMAGE_GLASS_<Z>_...` with the exact
  token FRONT|REAR|LEFT|RIGHT|CENTER right after `DAMAGE_GLASS_`; use the TOP-MOST such ancestor (children are named `..._Vtr_Left_3`,
  `polymsh_extracted`, `..._CENTER_MESH` [V critic]). Nodes named `DAMAGE_GLASS_` without a token and without ksBrokenGlass (mustang LOD1
  `DAMAGE_GLASS_Vtr_Ant1`, a real window [V]) are left alone (one log line). Zone nodes: `SetActive(false)` at bind regardless of the kn5
  flag.
- Per zone: `g = saturate((z - MIN_SPEED) / (FULL_SPEED - MIN_SPEED))` from that zone's `[DAMAGE_GLASS_<Z>]`; zone glass shows when
  `g >= GlassThreshold` [I]; never un-breaks until repair. Missing section (ks_mazda_787b, tatuusfa1 [V]) -> that zone never breaks.
  CENTER = windscreen, FRONT = headlights, REAR = tail lights, LEFT/RIGHT = side windows [V Pipeline PDF].
- Material: W's broken-glass mode: normal from the install's `content/texture/DAMAGE_GLASS.dds` (1024 BGRA32, alpha <= 0.39 = crack
  visibility [V]; loaded once, shared, only when m > 0), `alpha = tex.a * _GlassSet.x`, diffuse = the kn5's own proxy colour. Shared
  material `_GlassSet.x = 0` (G0); C gives the zone renderers ONE per-car clone with `_GlassSet.x = 1` on the first break. Stray
  ksBrokenGlass meshes outside any zone (e92 `R_DOOR_mesh_SUB3`) stay on the shared material = invisible (// ours).

### 2.3 Panels `[VISUAL_OBJECT_n]` (C)
- Resolve NAME per LOD set (246/260 in LOD0, 229 in LOD1 [V]); missing -> skip, `AcLog.InfoOnce("Car: <car> damage panel <NAME> not
  found")` (AC: `ERROR: DAMAGE ZONE %S NOT FOUND`). DAMAGE_ZONE is only FRONT/REAR in the data, but parse all 5 (CENTRE alias // ours).
- Static bend [I]: `f = saturate((z - MIN_SPEED) / (FULL_SPEED - MIN_SPEED))`;
  `node.localRotation = rest * Quaternion.AngleAxis(-f * STATIC_ROTATION_ANGLE, normalize(-ax, ay, az))` about the node's own origin
  (pivot = node origin [V Pipeline + measured hinges]). Axes normalised (`2,0,0`, `1,1,1`, `-1,0,0.5` occur [V]). Sign derived from AC's
  right-handed X-left frame [I]; first lab check compares 458 vs miata (opposite authored signs) with AC screenshots.
- Oscillation [I], only if `[OSCILLATIONS] ENABLED=1` and f > 0: per panel a damped angular spring, `exc = MULT_G * dot(accG, ALLOWED_G)`
  with `accG` = this car root's acceleration in g in car axes (AC order x lateral, y vertical, z longitudinal), from a per-car finite
  difference of `Root.position` (works for local, AI and remote cars alike); spring **6 Hz, damping ratio 0.3 // ours** (no AC value);
  angle clamped to `[OSCILLATION_MIN_ANGLE, OSCILLATION_MAX_ANGLE] * f` (no clamp to 5: the 458 uses 6 [V]) about OSCILLATION_AXIS,
  added to the static bend. Cost: a few float ops per damaged panel per frame, only while f > 0; skipped beyond 60 m from the camera
  // ours.
- Re-applied on LOD switch (`ApplyAll`) and after the interior rebuild.

### 2.4 Engine smoke (C)
`AcCarModel.SetEngineSmoke(float rate01)`: a Unity ParticleSystem (built-in particle material, no texture shipped) configured from
`engine_smoke.ini [SETTINGS]` (FREQUENCY_HZ, SIZE/COLOR/OPACITY/GRAVITY/DRAG bases + randoms) and `[LIFE_TIME] SECONDS`, emitter at the
engine-ish position = `Model.Bounds` centre at the front/rear axle by engine position (// ours: front unless car.ini says mid/rear -
unknown; use the bonnet node MOTORHOOD if present, else bounds front). On when `EnginePct >= 100 - ENGINE_LIFE` - i.e. **ENGINE_LIFE=25
read as 25 % life left // ours** (alternative reading "25 of 1000" left near death; one constant to flip). Created lazily (no cost until
smoking).

### 2.5 Sparks (C, OPTIONAL, last)
On `AcEvents.CarCollision` scrapes with walls (stay contacts, v > 20 km/h // ours) emit a burst configured from `sparks.ini`. Trigger
rule [I]. Build only after everything else passes.

---------------------------------------------------------------------------------------------------------------------------------
## 3. Mechanical damage (P)

### 3.1 Aero [V formula in every aero.ini; D = zone km/h I-strong]
Per wing/fin w, on Version change: `clDiv[w] = 1 + m * Σx ZONE_x_CL[w] * Zone[x]`, `cdMul[w] = 1 + m * Σx ZONE_x_CD[w] * Zone[x]`,
x in FRONT, REAR, LEFT, RIGHT (no CENTER key [V]). In `AcAeroModel.Wing`: `cl /= clDiv` (sign kept: 1.11.3 fixed negative-CL wings
[V changelog]), `cd *= cdMul`, before `downforce = q*cl`, `drag = q*cd`. Arrays preallocated per wing in `AcCarPhysics.Init`.

### 3.2 Steer rod [V mechanism name; formula I]
On a counted hit with `vImp > MIN_VELOCITY`: corner `i` = the wheel hub (`WheelHub(i)`, body-local) nearest to `p` in the horizontal
plane, only if within **1.0 m // ours** (else no rod damage). `RodOffset[i] = min(MAX_DAMAGE, RodOffset[i] + m * GAIN * (vImp -
MIN_VELOCITY))` (continuous at the threshold // ours choice between `GAIN*(v-MIN)` and `GAIN*v`).
Effect: new `AcChassisWheel.ToeLen` (setup toe-out length, m; the setup path `AcCarPhysics` TOE_OUT_xx writes it) and
`ToeDeg = clamp(ToeDegFor(ToeLen) + sgn * ToeDegFor(RodOffset[i]) , -12, 12)` where `sgn` makes the damage always ADD toe-out
regardless of the sign of the arm z (many cars have negative SteerArmZ [V]) and **12 deg // ours** caps the atan artefact on short arms
(47-61 deg otherwise [V critic]). Arms < 2 cm (existing guard) get no toe change. All 4 corners (rear toe links exist on every car [V]).
Display / audio value `SuspDamage01(i) = RodOffset[i] / MAX_DAMAGE`.

### 3.3 Engine life [V formula comments in Kunos' data.acd; scale BELIEF]
In `AcCarCore.Step` (each 333 Hz sub-step `h`), after the turbo loop:
`EngineLife -= h * m * (RPM_DAMAGE_K * max(0, Rpm - RPM_THRESHOLD) + TURBO_DAMAGE_K * max(0, BoostTotal - TURBO_BOOST_THRESHOLD))`.
`BoostTotal` = the sum over turbos (the threshold is "TOTAL boost" [V comment]). No power loss above 0 (none documented, // ours). At
`<= 0`: `EngineDead = true`, life 0, throttle to the engine forced 0, idle controller off, starter refused; engine-braking/coast only;
telemetry rpm decays so engine sound stops by itself. Kept through `Core.Reset(false)` and `ResetTo`. Setup limiter stays capped at
100 % (AC allows 108-109 % on 4 cars, changelog 0.22.21; not supported here - documented). HUD/shift-light red line =
`min(LIMITER, RPM_THRESHOLD)` (changelog 1.7 [V]).

### 3.4 What is NOT changed
Body damage never changes mass, grip or handling except through aero (no key exists [V]). No brake / wheel damage in AC [V absence].
Gearbox / tyres: section 0.

---------------------------------------------------------------------------------------------------------------------------------
## 4. Repair and pits (R, F, A, H)

- `AcRace.BackToPits()`: as today + `rig.Physics.Repair(AcRepair.All)` (practice / qualify / hotlap; refused in race).
- Grid placement, session transitions (`AcRace.Flow` 82/84/152) and `RespawnLocalCar`: `Repair(All)`. **Never inside `PlaceInPits`**
  (also used for jump-start penalty, late start and reset fallback) nor `ResetTo`.
- **Pit-box repair** (new `AcRace.Repair(AcRepair what)`): allowed when the local car is stopped (< 1 km/h // ours) in ITS pit box (the
  GARAGE "waiting" rule R already has) in any session. R: `Freeze(true)`, timer = `Damage.RepairSeconds(what)` (1.5), `AcCarAudio.Repair(true)`
  (common `unscrew`), HUD countdown; at the end `Repair(what)`, `Repair(false)` (common `screw`), `Freeze(false)`. Leaving is impossible while
  frozen; a session end / BACK TO PITS cancels (BACK TO PITS repairs fully anyway). Public state for F/H: `AcRace.RepairActive`,
  `AcRace.RepairSecondsLeft`, `AcRace.RepairTotalSeconds`, `AcRace.RepairWhat`.
- F GARAGE page: a REPAIR section with three rows like AC's pit app: REPAIR BODY, REPAIR BODY + SUSPENSION, REPAIR ENGINE, each with its
  estimated seconds and the install's art `content/gui/pitstop/repair_body_ON/OFF.png`, `repair_sus_ON/OFF.png` (exe-referenced;
  `repair_susp_*` is unreferenced [V]), `repair_engine_ON/OFF.png`; `operating_repair.png` + countdown while repairing; rows disabled
  (OFF art) unless in the box and stopped, or when that part is undamaged. Row labels are AC's exe strings re-typed as names
  ("REPAIR BODY", "REPAIR BODY + SUSPENSION", "REPAIR ENGINE", "ESTIMATED PIT STOP TIME") [V strings].
- AI: no pit stops (iteration 1 rule kept); AI repair only by session change / grid.
- Race: engine dead -> R marks the car retired: `AcStanding.Retired` (new, R), results/timing show "DNF", the race ends for it. Local
  player: car frozen where it stops after < 1 km/h (// ours) with a HUD message "ENGINE BLOWN"; they can get out.

---------------------------------------------------------------------------------------------------------------------------------
## 5. HUD damage displayer (H)
- Art: `content/texture/damage/` `front rear left right engine gearbox sus_fl/fr/rl/rr tyre_fl/fr/rl/rr flatspot_fl/fr/rl/rr .png`
  (18 files referenced by acs.exe; `center.png` is on disk but NOT referenced [V] -> not drawn). White-on-alpha 512 layers that stack
  into a top-down car [V dmgart composite].
- Values: body layers `ZoneByte(z) / MaxDamageKmh` (FRONT/REAR/LEFT/RIGHT), engine `EnginePct/100`, sus `SuspDamage01(i)`; gearbox, tyre
  layers drawn at 0 (we do not simulate them); flatspot layers hidden.
- Tint **// ours** (AC's colours unknown): white (0) -> yellow (0.5) -> red (1), alpha 0.85.
- Visibility: shown for `DisplayTimeS` (4 s) after a `CarDamaged` on the local car, then hidden (AC behaviour [I]); also always shown while
  a repair runs. Desk position from `POSITION_X` / `DISTANCE_FROM_CENTER_Y` interpreted in a 1920x1080 reference (// ours mapping); VR panel:
  a cell beside the existing VR HUD panel. Setting `hud.damage` (default = gameplay.ini IS_ACTIVE). `PRINT_VALUES=1`: print the % next to
  each layer (labels are our own text: AC's are exe strings). Hidden entirely when m == 0.
- Engine smoke / blown: HUD message "ENGINE BLOWN" once (our text).

## 6. Audio (A)
- `AcFeed.Constant` stops pinning `SuspDamage` (Inflation stays 1: no punctures); `Feed` writes `_feed[AcFeed.SuspDamage] =
  max_i SuspDamage01(i)` for local, AI and remote cars (remote from N's decoded levels) -> the car bank's `flutter_4` layer [V].
- `CarImpact` handling unchanged. Optional A2: `impact_angle` from the new `CarCollision` event (angle between the contact normal and
  car forward, 0..180 [I]) instead of the fixed 90, and car/track choice from `other` instead of `HitsCar`.
- Repair: `common/unscrew` at start, `common/screw` at the end [V events exist; use I].
- Glass break / engine blow: no event exists in any bank [V] -> silence (nothing invented).

## 7. Network (N): protocol 3, owner-authoritative
- Authority: the car's owner only (own car: its client; AI: the instance master; after hand-over, the new master). Everyone else reads.
- **Damage pair** (2 ints, the same encoding for players and AI; **all zero = undamaged**, so DAMAGE 0 writes zeros):
  - `d0` = front u8 | rear u8 << 8 | left u8 << 16 | right u8 << 24 (ZoneByte, 1 km/h).
  - `d1` = center u8 | rod LF u4 << 8 | RF u4 << 12 | LR u4 << 16 | RR u4 << 20 | engine damage % u7 << 24 | retired 1 << 31.
    Rod level u4 = `round(15 * SuspDamage01)`; engine % = `ceil(EnginePct)` clamped 0..100 (100 = dead).
- Player record: **ints 42 = d0, 43 = d1** (`AtDamage = 42`, `Used = 44`), non-motion (no seq/stamp bump).
- AI cars: the AI blocks are full (31 free bits per car [V]), so **"Ac Net Host 0" becomes the AI damage table**, master-written: 4-int
  header (magic 'ACD1' 0x41434431, seq, stamp, writer+1 | block << 16 | protocol << 24) + 18 x 2 ints by AI index (40 ints). Hosts 1-3
  stay free. Host blocks exist in the scene and are unused today [V code].
- `AcNetWire.Protocol = 3` (history line: "3 = damage pair in ints 42-43, AI damage in Host 0"); protocol-2 records are refused as
  `Foreign` by design. README lines "protocol 2" -> 3.
- Write: `AcNetGame.Own` sets ints 42-43 from `AcLocal.Car.Physics.Damage.ToWire(out d0, out d1)` when Version changed; master writes
  Host 0 for each AI rig likewise.
- Read: in the remote-car draw (`AcNetGame.Cars`, Late) decode -> `rig.Physics.Damage.ApplyWire(d0, d1)` only if the ints differ from the
  last applied (stored per AcRemoteCar) -> the push in 1.7. Re-apply after interior rebuild / Adopt / stand-in replacement.
- **Late joiners**: block state is replicated whole, so a joiner decodes current ints 42-43 / Host 0 on its first read - no extra
  message. (AC also sends zone levels with the car list: acs.exe `Damage zone %d: %f` near ACCLIENT car-list strings [V].)
- Hand-over: `AcAiField.FromNet` seeds each adopted AI rig with `Damage.ApplyWire` from Host 0 (exact floats become ZoneByte /
  levels; engine life = (100 - %) * 10 - accepted loss).
- World 0 int 6 already carries DAMAGE_MULTIPLIER [V] - unchanged.

## 8. AI (I) and seat (S)
- AI rigs take damage through the same physics on the master (dynamic `AcCarPhysics`); DamageRate from the rules (R ApplyRates /
  AcAiField).
- `AcAiDriver` new state `Retired`: entered when `Damage.EngineDead`; steer to the track side over 3 s // ours (AC 0.21.6: "try to park on
  the side"), brake to 0, hold (Freeze), tell R (`AcStanding.Retired`). `Recover` / `ResetToLine` keep damage.
- Optional I2: scale planned corner speeds by `sqrt(Σ cl_eff / Σ cl_base)` of the car's wings so a damaged AI does not overdrive.
- S: optional `AcXr.Pulse` on `CarDamaged` (local car, amplitude `min(1, vImp/60)` // ours). No camera shake (VR comfort).

---------------------------------------------------------------------------------------------------------------------------------
## 9. Module split: files and the exact public contracts

Order of work: **lead (events) -> P (core) -> W (shader) -> C (visuals) | N (wire) | R (rules, repair) -> H | A | F | I -> S (optional)**.
Each owner codes against the signatures below; stubs log once. Threading: everything main thread; damage maths in FixedUpdate /
collision callbacks; visuals only on Version change; no LINQ, lambdas or allocations in steady state; shader writes only through
cached per-car material clones (`Material.SetVector` with static `Shader.PropertyToID` ids).

### lead - `Game/Shared/AcEvents.cs` (+ README)
```csharp
[Flags] public enum AcDamageChange { None = 0, Zones = 1, Suspension = 2, EngineSmoke = 4, EngineDead = 8, Repaired = 16 }
public static event Action<AcCarRig, AcDamageChange, float> CarDamaged;      // P raises (local + AI, own client), N raises for remote on change
public static void RaiseCarDamaged(AcCarRig car, AcDamageChange what, float impactKmh);
public static event Action<AcCarRig, float, Vector3, Vector3, AcCarRig, bool> CarCollision; // car, kmh, point, normal, other car (null), withCar
public static void RaiseCarCollision(AcCarRig car, float kmh, Vector3 point, Vector3 normal, AcCarRig other, bool withCar);
```
`CarImpact` unchanged (A and `AcLabAi` contact counting depend on it). No AcTelemetry change: consumers read `rig.Physics.Damage`.

### P - `Data/Physics/AcCarDamage.cs` NEW, `AcCarSpec.cs`, `AcCarSpecParts.cs`, `AcAeroModel.cs`, `AcChassis.cs`, `AcCarCore.cs`, `Game/Physics/AcCarPhysics.cs`, `Editor/AcLabPhysics.cs`, probe
```csharp
// Data/Physics/AcCarDamage.cs (Unity-free)
public enum AcDamageZone { Front = 0, Rear = 1, Left = 2, Right = 3, Center = 4 }
[Flags] public enum AcRepair { None = 0, Body = 1, Suspension = 2 /* implies Body */, Engine = 4, All = 7 }
public sealed class AcDamageRules {            // system/cfg, loaded once per install, cached
    public float GlassThreshold, DisplayTimeS, MaxDamageKmh, SmokeEngineLife, SmokeSeconds; public bool PrintValues, DisplayerDefaultOn;
    public int DefaultMultiplier; public float PosX, CenterY;
    public static AcDamageRules Load(AcInstall install); public static AcDamageRules Current { get; } }
public sealed class AcDamageSpec {              // per car, parsed in AcCarSpec.FromArchive (worker-safe)
    public float SuspMinVelocity, SuspGain, SuspMaxDamage; public bool HasSusp;
    public float RpmThreshold, RpmDamageK, TurboBoostThreshold, TurboDamageK; public bool HasTurboDamage;
    public float PitBodySecPer10, PitEngineSecPer10, PitSuspSecPer10; }
public sealed class AcCarDamage {
    public const float ZoneCap = 255f, LifeNew = 1000f;                  // ours (1.2)
    public readonly float[] Zone = new float[5]; public readonly float[] RodOffset = new float[4];
    public float EngineLife; public bool EngineDead; public int Version { get; }
    public float Multiplier;                                               // = DamageRate, set by AcCarPhysics
    public AcCarDamage(AcDamageSpec spec, AcDamageRules rules);
    public bool AddImpact(int zone, float kmh, int corner /* -1 none */); // returns true if counted; applies m to the rod
    public void StepEngine(float h, float rpm, float boostTotal);         // called inside AcCarCore sub-steps
    public byte ZoneByte(int zone); public float SuspDamage01(int corner); public float BodyPct, SuspPct, EnginePct { get; }
    public float RepairSeconds(AcRepair what); public void Repair(AcRepair what);
    public void ToWire(out int d0, out int d1); public bool ApplyWire(int d0, int d1);  // ApplyWire: remote / hand-over; true if changed
    public AcDamageChange TakeChanges(); }                                 // flags since last call
// AcWingSpec += float ZoneFrontCl, ZoneFrontCd, ZoneRearCl, ZoneRearCd, ZoneLeftCl, ZoneLeftCd, ZoneRightCl, ZoneRightCd
// AcCarSpec  += AcDamageSpec Damage;  P0 fix: DefaultAdjustment from [ENGINE_DATA] first
// AcAeroModel.Wing(..., float clDiv, float cdMul, out downforce, out drag)   (overload; old one = 1, 1)
// AcChassisWheel += float ToeLen, RodOffset;  AcCarCore += AcCarDamage Damage (set by AcCarPhysics; null = none)
// Game/Physics/AcCarPhysics.cs
public AcCarDamage Damage { get; }            // exists for every rig (remote: written by N via ApplyWire)
public float DamageRate;                      // 0..1, written by R / I (like FuelRate); 0 = off (no accumulation)
public Vector3 PuppetVelocity { get; }        // velocity last given to ApplyPuppet (for the other car's impact speed)
public void Repair(AcRepair what);            // Damage.Repair + chassis/aero/engine refresh + RaiseCarDamaged(Repaired)
public void DebugAddImpact(AcDamageZone z, float kmh);  // lab / harness only; same path as a real hit
```

### W - `Shaders/AcPerPixel.shader`, `Game/World/AcTrackJob.cs` (AcShading), `Game/World/AcMaterials.cs`, `Resources/` carriers if a pass changes
- Shader properties of 2.1/2.2 (`_DamageTex`, `_DamageMaskTex`, `_DamageScratch`, `_DamageDent`, `_DamageSet`, `_GlassSet`).
- `AcShading`: `bool DamagePaint`, `bool BrokenGlass`; add slots only if `AcCarDamageGate.TexturesWanted` (static bool, R sets it from
  the rule before spawning; owner R, file `Game/Race/AcCarDamageGate.cs`).
- `AcMaterials`: bind slots, `_DamageSet.x = 1` on damage paint when slots exist; ksBrokenGlass: `_DamageSet.w = 1`, `_GlassSet.x = 0`
  (G0, always), `_NormalTex` = `AcMaterials.DamageGlassTexture` (lazy, install `content/texture/DAMAGE_GLASS.dds`, only when the gate is
  on; when off the broken-glass material stays invisible).

### C - `Game/Cars/AcCarData.cs` (damage.ini), `AcCarParts.cs` (glass groups, panels per set), `AcCarModel.cs`, `AcCarShading.cs`, new `AcCarDamageVisual.cs`, `AcCarSmoke.cs` (+ optional `AcCarSparks.cs`), `Editor/AcLabCars.cs`
```csharp
public void SetDamage(float frontKmh, float rearKmh, float leftKmh, float rightKmh, float centerKmh); // ZoneByte values; idempotent
public void ClearDamage();                     // = SetDamage(0,0,0,0,0)
public void SetEngineSmoke(float rate01);      // 0 = off
public bool DamageVisualsOn { get; }           // false when cars.visualdamage is off or the gate was off at build
```
`AcCarShading.FlattenDamageNormals` runs only when the gate was off at build. `Tick` advances oscillating panels and the smoke.

### R - `Game/Race/AcRace.Flow.cs`, `AcRace.cs`, `AcRace.Load.cs`, `AcRaceDefaults.cs`, `AcStandingsBoard.cs`, new `AcCarDamageGate.cs`, `Game/Shared/AcSession.cs`, `Editor/AcLabRace.cs`
- `AcRules.DamageMultiplier` default from server_cfg `DAMAGE_MULTIPLIER` via AcRaceDefaults (100 here); the comment "iteration 1 has no
  damage model" goes. `ApplyRates`: `ph.DamageRate = Mathf.Clamp01(r.DamageMultiplier / 100f)`; >0 -> 0 transition: `Repair(All)`.
- `public static class AcCarDamageGate { public static bool TexturesWanted; }` set before every spawn from the rule and `cars.visualdamage`.
- `public static void Repair(AcRepair what)`, `RepairActive`, `RepairSecondsLeft`, `RepairTotalSeconds`, `RepairWhat`; BackToPits / grid /
  session / respawn repairs (1.6); `AcStanding.Retired` (bool) and DNF in results; blown local engine in race -> retired.

### N - `Game/Net/Core/AcNetWire.cs`, `AcNetBlocks.cs`, `AcNet.Blocks.cs`, `AcNetGame.Own.cs`, `AcNetGame.Cars.cs`, `AcNetGame.Race.cs`, `Editor/AcLabNet.cs`, probe
Section 7. `AcNetPlayer` += `int DamageD0, DamageD1` (raw, for the timing page / debugging). `AcNetBlocks.PackAiDamage(int[] block,
int aiIndex, int d0, int d1)`, `UnpackAiDamage(int[] block, int aiIndex, out int d0, out int d1)`, `MagicAiDamage = 0x41434431`.
Remote rigs: N raises `CarDamaged` for a remote car when ApplyWire returns true (H ignores non-local; A/C react through the push).

### H - `Data/Hud/AcHudArtList.cs`, `Data/Hud/AcHudConfig.cs`, `Game/Hud/AcHudArt.cs`, `AcHudFeed.cs`, `AcHudView.cs`, `AcHudVr.cs`, `AcHud.cs`, `Editor/AcLabHud.cs`
Section 5. Reads `AcLocal.Car.Physics.Damage` + `AcDamageRules.Current` per frame (no allocation); `AcHud.SetFakeDamage(float[5] zones,
float[4] susp, float enginePct)` for the lab.

### A - `Game/Audio/AcEventPlayer.cs`, `AcCarAudio.cs`, `AcSfx.cs`, `Editor/AcLabAudio.cs`
`public void Repair(bool starting)` on AcCarAudio; `Feed` adds suspension_damage (section 6); optional CarCollision angle.

### F - `Game/Front/AcPagesMisc.cs` (GARAGE repair section), settings rows, `AcPagesRace.cs` (DNF in timing), `Editor/AcLabFront.cs`
`AcSettingsUi.Toggle("CARS", "cars.visualdamage", "VISUAL DAMAGE", true)`, `Toggle("HUD", "hud.damage", "DAMAGE DISPLAY", <gameplay.ini>)`.
Lobby DAMAGE row exists (range / step from the launcher slider) - unchanged.

### I - `Game/Ai/AcAiDriver.cs` (Retired), `AcAiField.cs` (DamageRate, FromNet seed), `AcAi.cs`, optional `Data/Ai/AcAiPlan.cs`, `Editor/AcLabAi.cs`
### S - optional pulse in `Game/Seat/` on CarDamaged.

---------------------------------------------------------------------------------------------------------------------------------
## 10. Lab scenarios (each owner, own `Editor/AcLab<M>.cs`; LOOK at every picture) and offline probes

| Scenario | Does | Pass criteria | Pictures |
|---|---|---|---|
| `cars-glassfix` (W+C, G0, runs at DAMAGE 0) | ktm_xbow_r LOD0, bmw_m3_e92 LOD0, lotus_exos_125 forced LOD1 | no grey film on windscreen / door / lights; `Car:` log counts broken-glass meshes hidden | 3 cars before-fix reference + after |
| `physics-crash` (P) | flat plane + Track-layer box wall (no surface tag); 458 driven by set `linearVelocity` into it: front 20/40/60/100, rear 40, left side 40 at 90 deg, scrape along the wall 100 km/h 2 s, roll onto the roof; repeat front 40 with DamageRate 0 and 0.5 | hit zone = expected and others < 3; zone within ±20 % of the set normal speed; DamageRate 0 -> all zero and no CarDamaged; 0.5 -> same zones, aero/toe deltas halved; scrape adds < 20; roof -> CENTER; `ResetTo` keeps, `Repair(All)` clears; top speed and 100-0 change after a 60 km/h front hit; toe on the hit corner <= 12 deg | car after each hit (front, rear, side, roof), with C's visuals |
| `physics-engine` (P) | 458 money shift (2nd at 9000 rpm -> 1st), limiter overshoot; z4_drift default boost after P0; drive to life 0 | life loss within ±5 % of the formula; z4_drift loses 0 at default adjustment; life 0 -> EngineDead, rpm -> 0, car coasts, no restart; Repair(Engine) restores | rpm/life trace image |
| `cars-damage` (C+W) | `SetDamage` forced 0/30/60/150 per zone on ferrari_458, ks_mazda_miata (opposite panel signs), abarth500, ferrari_312t (CENTER-only glass); LOD 0 -> 1 switch; repair | dents/scratches only on the hit zone's mask area, none on the roof; glass zones on exactly when g >= 0.65; panel angle = f x ANGLE (logged per panel); state survives LOD switch; repair = pristine; DAMAGE-0 build: no damage slots (log) and FlattenDamageNormals ran | per car: front/rear/left/right/top at each level |
| `audio-damage` (A) | susp 0 -> 1 fed; repair start/end | flutter_4 RMS rises above 0.44, silent at 0; unscrew / screw play once | - |
| `hud-damage` (H) | fake damage per layer 0/0.5/1, desk + VR; timeout | layers tinted as specified, center.png absent, hides after TIME s; hidden at DAMAGE 0 | desk + VR |
| `net-loopback` ext. (N) | round trip d0/d1 for player and Host 0 AI table; a fresh reader (late joiner) decodes existing ints; protocol-2 record | identical bytes; zero = undamaged; protocol 2 refused as Foreign | - |
| `race-repair` (R) | magione practice: `DebugAddImpact` front 60 + rod; stop in own box; Repair(Body+Susp); BackToPits; R reset; race grid; blown engine in race | frozen for RepairSeconds ±0.2 s then clean; BackToPits clean; reset keeps; grid clean; blown -> DNF in standings | GARAGE repair rows, HUD countdown |
| `ai-race` @ damage 100 (I) | existing scenario with damage on + one AI forced EngineDead | contacts 0 regression holds; forced AI retires to the side, DNF | AI parked |
| `front-garage-repair` (F) | GARAGE page in the box with damage | rows show AC art ON/OFF, seconds = RepairSeconds | page |

Offline harness probes (owners add `DevTools/harness-ac/ProbeDmg<M>.cs`, unique command names):
- P `dmgspec`: parse AcDamageSpec + wing zones for 86 cars; must reproduce the census (291 wings + 1 fin; susp 83; engine 86 / turbo 84;
  PIT 20/2/30 with exos 5 and miura 6), 0 problems. `dmgsim <car>`: scripted impacts -> zones, per-corner toe (clamp holds fleet-wide),
  aero factors, engine life under a money shift. `dmgturbo`: default total boost per turbo car after P0; none above threshold.
- C `dmgvisres`: per car and LOD file: VISUAL_OBJECT resolution (246 / 229), glass zone groups by top-most ancestor, stray ksBrokenGlass
  list (e92 x3), mask decode for every format/size.
- N `dmgwire`: pack/unpack round trip incl. caps, zero record, AI table.
- H `dmghud`: the 18 art paths exist; tinted composite PNG to look at.

---------------------------------------------------------------------------------------------------------------------------------
## 11. Risks / unknowns and the defensible fallback for each

| # | Unknown | Our choice | Fallback / how to settle |
|---|---|---|---|
| R1 | Zone unit (km/h) [I-strong] | km/h, cap 255 | All thresholds are in the files; if wrong, only the cap and the dent full value change |
| R2 | Sum vs max, scrape share, normal vs total speed | sum of normal speed, scrape gated | One constant switch `AcCarDamage.Accumulate` (Sum/Max). Settle: a 15-min AC run with a shared-memory `carDamage[5]` logger (user, optional) |
| R3 | CENTER ever written | roof hits + rollover only | if AC never writes it, windscreens would never break - keep ours (windscreen breaking is visible in AC) |
| R4 | INITIAL_LEVEL meaning | dents start at it | read + logged; if wrong, dents from 0 (one line) |
| R5 | Paint blend look, panel bend sign, glass threshold on g | as 2.x | compare lab pictures with AC screenshots of the same car / hit; signs are one negation per car class |
| R6 | Rod formula, sign, corner | GAIN*(v-MIN)*m, always toe-out, nearest hub <= 1 m, clamp 12 deg | clamp keeps every car drivable-but-pulling; miata (MIN 85) rarely damages - acceptable |
| R7 | Gearbox damage mechanism | not built; HUD gearbox layer clean | build later only with evidence |
| R8 | Engine life scale 1000, smoke threshold reading | 1000, smoke at 25 % life | single constants; the formula's relative behaviour (money shift kills in < 1 s) matches AC's reputation |
| R9 | Back to pits = repair in AC | full repair | community statement; pit-box repair exists either way |
| R10 | Whether collider.kn5 covers the wheels | nearest-hub rule works for hull contacts near a corner | if a car's hull misses wheels, rod damage comes from body hits near that corner only |
| R11 | Kinematic parked cars never take damage; puppet velocity | parked = no damage; PuppetVelocity getter | matches one-sided contact design of iteration 1 |
| R12 | Memory (~6 MB per car model) | textures only when m > 0 at spawn | `cars.visualdamage` off also skips them |
| R13 | Protocol bump refuses protocol-2 builds | by design | all players upload/use the same build |
| R14 | P0 changes iteration-1 power on 4 turbo cars | ship (it matches AC defaults) | flagged to the user; revert = one line |
| R15 | G0 changes the look at DAMAGE 0 on 6 cars | ship (removes a wrong grey film) | flagged; not lab-seen yet - `cars-glassfix` proves it |
| R16 | Hand-over loses exact AI damage | quantised seed | invisible difference (1 km/h, 1/15 rod, 1 % engine) |
| R17 | Adjustable limiter > 100 % (4 cars) | not supported | documented in README "Not simulated" |
| R18 | Tyre punctures / explosion, dirt | not built | no thermal model; inflation constant 1 |
| R19 | AC's HUD colours / layout | white-yellow-red, POSITION_X/Y on 1080p | user judges against AC |
| R20 | DAMAGE 0 hides visuals (AC keeps them) | user rule wins | `cars.visualdamage` only matters when m > 0 |
