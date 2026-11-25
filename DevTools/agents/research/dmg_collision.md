# AC car damage, part 1: how a collision becomes zone damage (research, 2026-11-02)

Topic: how AC turns a contact into per-zone damage (FRONT, REAR, LEFT, RIGHT, CENTER), what the units are, what the multiplier
does, and what damage.ini holds on all 86 owned cars. Visual effects (panels, glass, shader) and mechanical effects (aero, suspension,
engine) are the sibling topics; they are only covered here where they show what a zone value means.

Tags: **[VERIFIED in install]** = read from the player's install by a harness probe or a file read today. **[DOCUMENTED: url]** = a
public source. **[INFERRED]** = my reasoning from the evidence given. **// ours** = a proposed number or rule the install does not give.

Probes: `DevTools/harness-ac/ProbeDmgCol.cs`
- `colsweep [car]`: damage.ini over every owned car, plus damage keys in car/aero/suspensions/engine/colliders, VISUAL_OBJECT and
  DAMAGE_GLASS node presence in the main kn5, and the collider.kn5 extent. The output is copied in section 5.
- `colraw <car>`: raw damage.ini plus the damage sections of one car.
- `colstrings <file> <regex>` and `colnear <file> <exact> [radius]`: key-name strings of acs.exe / acServer.exe, and the strings stored
  next to them. This reads names only; nothing is disassembled.

Run: `cd DevTools/harness-ac && dotnet build -c Release -v q && dotnet run -c Release --no-build -- colsweep`.

---------------------------------------------------------------------------------------------------------------------------------
## 0. Short answer (the rules to implement; section 4 has the full spec)

1. **Five zone values per car, `D[5]` in the order FRONT=0, REAR=1, LEFT=2, RIGHT=3, CENTER=4.** Each is a float in **impact-speed
   km/h units**. It starts at 0, only grows, and has no ceiling of 1.
2. **A hit adds damage.** One new contact event adds its impact speed to ONE zone: `D[z] += v_imp_kmh`. The impact speed is the
   relative velocity at the contact point along the contact normal, in km/h. Scraping does not keep adding.
3. **Zone pick** uses the contact point in the car's own body frame, normalised by the hull half-extents. FRONT/REAR when the length
   axis dominates, LEFT/RIGHT when the width axis dominates. CENTER for hits on the roof or cabin top: rollovers, and landing upside down.
4. **The lobby multiplier** `DAMAGE_MULTIPLIER` (0-100 %; `AcRules.DamageMultiplier`) scales the MECHANICAL effects only. Visual damage
   uses the raw `D` and has its own on/off toggle.
5. **damage.ini never says how a hit becomes damage.** It only maps zone levels (km/h) to visuals. The thresholds are `[SCRATCHES]`
   0..20, `[DAMAGE] INITIAL_LEVEL` 20, `VISUAL_OBJECT MIN/FULL_SPEED`, and `DAMAGE_GLASS_* MIN/FULL_SPEED` with the global
   `GLASS_THRESHOLD` 0.65. Mechanical effects use their own inis: aero.ini ZONE_*, suspensions.ini [DAMAGE], engine.ini [DAMAGE].

---------------------------------------------------------------------------------------------------------------------------------
## 1. Evidence

### 1.1 What the install shows
- **The zone vocabulary is exactly 5 names.**
  - acs.exe holds `DAMAGE_GLASS_FRONT/REAR/LEFT/RIGHT/CENTER`, `DAMAGE_ZONE`, `ZONE_`, `[ZONE_` and
    `ERROR: DAMAGE ZONE %S NOT FOUND` (`colstrings`) [VERIFIED in install].
  - aero.ini has `ZONE_FRONT/REAR/LEFT/RIGHT_CL/CD` on every wing; there is no CENTER key [VERIFIED].
  - The HUD damage displayer textures in `content/texture/damage/` are `front.png rear.png left.png right.png center.png`, plus
    `engine gearbox sus_* tyre_* flatspot_*` [VERIFIED].
  - The zone order FRONT, REAR, LEFT, RIGHT, (CENTER) is the order of every list in the install: the aero.ini keys, the glass sections,
    and the formula_k sdk comments.
  - The shared-memory index order is the same: [DOCUMENTED, ACC shared memory doc v1.8, linked from
    https://www.assettocorsamods.net/threads/doc-shared-memory-reference-acc.3061/ ; AC inherited the struct, see 1.2]. I could not
    re-read the PDF today, so the index order is [INFERRED-strong].
- **Zone values are in km/h**, not 0..1 [INFERRED, strong]. Five independent pieces of evidence:
  1. `system/cfg/damage_displayer.ini [MAIN] MAX_DAMAGE_KMH=60` (with TIME=4, POSITION_X=20, DISTANCE_FROM_CENTER_Y=-200,
     PRINT_VALUES=0) [VERIFIED]. Content Manager labels this setting "Max damage at" with a speed slider of 30..90
     [DOCUMENTED: https://github.com/gro-ove/actools/blob/master/AcManager/Pages/AcSettings/AcSettingsDamageDisplayer.xaml ,
     `DamageDisplayerSettings.cs` reads `MAX_DAMAGE_KMH` default 60]. So the HUD shows a zone as fully damaged at a level of 60 km/h.
  2. Every damage.ini threshold is a speed. The Kunos comments read `MIN_SPEED ; MINIMUM DAMAGE SPEED` / `FULL_SPEED ; MAXIMUM DAMAGE
     SPEED` and `[SCRATCHES] MAX_SPEED=20 ;kmh` (ferrari_458, formula_k) [VERIFIED]. They are compared against a zone level, because
     `VISUAL_OBJECT` has only `DAMAGE_ZONE` and no event key.
  3. aero.ini says `CL=CL/(1.0+ZONE_x_CL*DAMAGE)`, `CD=CD*(1.0+ZONE_x_CD*DAMAGE)` with ZONE values 0..0.015 (section 5) [VERIFIED].
     With DAMAGE in 0..1, the largest possible effect would be 1.5 %, which nobody would bother to author. With km/h, 60 gives
     CD x1.9 and CL /1.9, which is plausible for a smashed nose.
  4. suspensions.ini `[DAMAGE] MIN_VELOCITY=40 ; MINUMUM VELOCITY TO START TAKING DAMAGE`, `GAIN=0.0004 ; AMOUNT OF STEER ROD
     DEFLECTION FOR IMPACT KMH`, `MAX_DAMAGE=0.05` [VERIFIED]. Impacts are measured in km/h.
  5. The common.bank collision events take the parameter `impact_speed` 0..1000, with layers car_hit_2 0.6..40, car_hit 40..100 and
     crash3 100..1000 (ac_physics_sound_rules.md 2.x) [VERIFIED earlier]. The same impact speed in km/h feeds the sound.
- **VisualDamageManager reads damage.ini.**
  - Next to each other in acs.exe are the class `VisualDamageManager`, the strings `VISUAL_DAMAGE`, `GLASS_THRESHOLD` and
    `/data/damage.ini`, then `OSCILLATIONS INITIAL_LEVEL FULL_SPEED DAMAGE_ZONE STATIC_ROTATION_* OSCILLATION_* MULT_G ALLOWED_G`,
    `content/texture/DAMAGE_GLASS.dds`, `txNormal glassDamage debugDamage ksPerPixelMultiMap_damage_dirt_sunspot SCRATCHES damageZones`
    (`colnear INITIAL_LEVEL`, `colnear SCRATCHES`) [VERIFIED].
  - So **INITIAL_LEVEL, SCRATCHES, OSCILLATIONS and the glass keys are VISUAL parameters**. None of them affects how a hit becomes damage.
  - `system/cfg/assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD=0.65` [VERIFIED].
- **The two damage settings are separate.**
  - Launcher `launcher/themes/default/index.html`: slider `data-bind="assists?DAMAGE"`, `data-slidermin=0 data-slidermax=100
    data-sliderstep=10 data-slidermintext="Off"`, labelled **"Mechanical damage"** [VERIFIED].
  - The `VISUALDAMAGE` toggle ("Visual damage") is commented out in that HTML [VERIFIED].
  - `Documents/Assetto Corsa/cfg/assists.ini` has `VISUALDAMAGE=` and `DAMAGE=` (0..100) as separate keys [VERIFIED; the user's file
    has 0/0].
  - Content Manager writes `["VISUALDAMAGE"]=bool, ["DAMAGE"]=0..100` (default true / 100) and takes Damage from the server's
    DamageMultiplier online [DOCUMENTED: https://github.com/gro-ove/actools/blob/master/AcTools/Processes/Game.Properties.cs ,
    `AcManager.Controls/ViewModels/AssistsViewModelBase.cs`].
  - Shared memory static has `aidMechanicalDamage` (float) [DOCUMENTED: https://assettocorsamods.net/threads/doc-shared-memory-reference.58/ ;
    `AcSharedStaticInfo.cs` in actools]. Its name says the multiplier is a MECHANICAL aid.
- **Server side.**
  - `server/cfg/server_cfg.ini DAMAGE_MULTIPLIER=100` [VERIFIED].
  - acServer.exe holds `DAMAGE_MULTIPLIER`, `DamageMultiplier`, `DamageZoneLevel`, `main.BroadcastDamageUpdate`,
    `AddClientCollisionEvent`, `ImpactSpeed`, `COLLISION_WITH_CAR`, `COLLISION_WITH_ENV`, `COLLISION BETWEEN:` [VERIFIED strings].
  - acs.exe holds `Sending ACP_DAMAGE_UPDATE` and `Damage zone %d: %f`; the latter sits among the client network strings [VERIFIED].
  - **So each client computes its own damage and sends its zone levels, which the server rebroadcasts so other clients can draw
    remote cars' damage** [INFERRED from the names]. This maps onto our owner-authoritative records.
- **UDP plugin collision event**, from Kunos' sdk example `sdk/dev/acRemoteServerUDP_Example/Program.cs` [VERIFIED]:
  - `ACSP_CLIENT_EVENT(130)` with type `ACSP_CE_COLLISION_WITH_CAR=10` (+ other_car_id) or `ACSP_CE_COLLISION_WITH_ENV=11`.
  - The payload is `float speed // Impact speed`, `vec3 world_pos`, `vec3 rel_pos`.
  - So AC's contact record is (impact speed, world point, point RELATIVE to the car). I read that as zone-by-relative-position
    [INFERRED], with car-vs-car and car-vs-environment as the only two kinds.
- **Collision geometry**:
  - acs.exe holds `LOADING COLLIDER MESH FILE: %S`, `Car::initColliderMesh`, `/collider.kn5`, `/data/colliders.ini`, `COLLIDER_`,
    `MESH_COLLIDER`, and ODE flags `ODE_EXT_trimesh ODE_EXT_opcode` [VERIFIED].
  - The formula_k colliders.ini comment: the boxes are "for optimizing performance of collisions of the bottom of the car with the
    asphalt and kerbs ... Keep in mind that the 3D collider also does collisions properly so use the colliders.ini just for car
    bottom collisions" [VERIFIED].
  - The car pipeline PDF (sdk/dev/car_pipeline_2.0rev) says the collider is used "especially if 20 cars collide in a turn" and should
    have at most 40/60 triangles with material "GL" [VERIFIED, text extract].
  - So **body hits come from collider.kn5. The colliders.ini boxes are only for the floor against the ground and do not define
    zones** [INFERRED]. No damage-zone geometry exists anywhere in car data: no ini key, no kn5 node [VERIFIED by the sweep].
- **The visual zone mask**, from the car pipeline PDF: `txDamageMask` 512x512, "WHITE (alpha) = front, RED = left, BLUE = right,
  GREEN = rear ... Never paint the roof and the top of the bonnet" [VERIFIED, pdf text]. The body visual has 4 directional zones and
  no roof zone. CENTER shows only as `DAMAGE_GLASS_CENTER` (the windscreen) and the HUD's center.png (the cabin outline).
- **Things that are not damage.** acs.exe `MIN_SPEED_KMH` belongs to `[ABS_V2]/TractionControl`. `MAX SPEED %f MIN_SPEED %f` is
  RaceEngineer, the AI [VERIFIED by `colnear`]. Neither relates to collisions.

### 1.2 Public documentation (thin; Kunos never published the damage algorithm)
- Shared memory physics page has `float carDamage[5]` with no comment in the Kunos header [DOCUMENTED: assettocorsamods.net
  reference above; CM `AcSharedPhysics.cs`; CSP `acc-extension-apps/.../sim_info.py` `('carDamage', c_float * 5)`].
  - A community doc says "Level of damage for each car section [only the first 4 are valid]" [DOCUMENTED:
    https://github.com/dcratliff19/AC-SimInfo-Docs-Markdown]. This hints that AC rarely or never writes index 4 (CENTER) [uncertain].
  - SimHub folds the 5 values into CarDamage1..5 with no documented scaling [DOCUMENTED: https://github.com/xorob0/OpenDash/issues/582].
- CSP Lua has `car.collisionDepth` (m) and `car.collidedWith` (car index + 1, 0 = track) [DOCUMENTED: source of
  https://github.com/kitaaaaaa/insain-ac-damage softdamage.lua l.1481/1495]. It also has physics scripts that can modify "Car damage;
  Engine damage; Gearbox damage" [DOCUMENTED: https://cup.acstuff.club/docs/csp/cars/physics-scripts]. No ranges are documented.
- No Kunos post found that defines the accumulation, the zone pick or the minimum speed. Searches covered assettocorsa.net (403 to the
  fetcher), Steam, overtake and GitHub. Everything in section 4 that is not tagged VERIFIED is therefore a proposal.

---------------------------------------------------------------------------------------------------------------------------------
## 2. What each damage key means (one table)

| Key (file) | Values on 86 cars | Meaning | Tag |
|---|---|---|---|
| `D[z]` zone level | float, km/h units, >= 0 | Accumulated impact speed for that zone | INFERRED-strong |
| `[DAMAGE] INITIAL_LEVEL` (damage.ini) | 20 on 85 cars; **100 on ks_alfa_33_stradale** | VISUAL parameter (read by VisualDamageManager). Most likely the zone level where body DEFORMATION (dent normal map / txNormal damage blend) starts, below which only scratches show. Evidence: on 85 cars it equals `[SCRATCHES] MAX_SPEED=20`; the alfa (no movable panels) gets 100 = almost never dents. It cannot be a level the car starts with (100 would spawn wrecked) | INFERRED (weak-medium) |
| `[SCRATCHES] MIN_SPEED / MAX_SPEED` | 0 / 20 on **all 86** | Zone level range over which the scratch layer (txDamage, masked by txDamageMask per zone) fades in 0 -> 1. Alternative reading: the sliding speed range of scrapes; less likely, because 20 km/h is too low a scrape speed to cap | INFERRED |
| `[OSCILLATIONS] ENABLED` | 1 x81, 0 x5 (ferrari_f40, f40_s3, ktm_xbow_r, lotus_49, ks_maserati_levante) | Damaged panels wobble with G (MULT_G, ALLOWED_G, OSCILLATION_*). Visual only | VERIFIED key / INFERRED use |
| `[VISUAL_OBJECT_n] DAMAGE_ZONE MIN_SPEED FULL_SPEED STATIC_ROTATION_AXIS/ANGLE` | 260 objects; DAMAGE_ZONE only FRONT x145 / REAR x115; MIN 0..30; FULL 20..220 | Panel NAME rotates by `ANGLE * saturate((D[zone]-MIN)/(FULL-MIN))` about AXIS at the node pivot (pipeline: "Upon impact, a script ... makes the parts vibrate/rotate on the basis of the location and pivot of these nulls") | INFERRED (visual topic owns it) |
| `[DAMAGE_GLASS_<ZONE>] MIN_SPEED FULL_SPEED` | 84 cars; F 15-30/60-80, R 10-30/30-80, L,R 10-30/30-80, C 20-30/50-80 | Broken-glass mesh `DAMAGE_GLASS_<ZONE>_n` shown when `saturate((D[zone]-MIN)/(FULL-MIN)) >= GLASS_THRESHOLD (0.65, assetto_corsa.ini)`. **This is the only data that uses LEFT, RIGHT and CENTER** besides aero.ini | INFERRED |
| `DAMAGE_MULTIPLIER` (server_cfg) / `DAMAGE` (assists.ini) | 0..100 % (launcher step 10, "Off" at 0) | "Mechanical damage" scale | VERIFIED label; scope INFERRED |
| `VISUALDAMAGE` (assists.ini) | 0/1 | Visual damage on/off | VERIFIED key |
| `MAX_DAMAGE_KMH` (system damage_displayer.ini) | 60 | HUD zone colour full at 60 | VERIFIED value, meaning DOCUMENTED (CM) |
| `GLASS_THRESHOLD` (system assetto_corsa.ini [DAMAGE]) | 0.65 | Glass break threshold on the normalised glass level | INFERRED use |
| car.ini `[PIT_STOP] BODY/ENGINE/SUSP_REPAIR_TIME_SEC` | body 20 (x84) / 5 (x2); engine 2 (x85) / 6; susp 30 (all) | "time spent to repair 10% of body/engine/suspension damage" (formula_k comment). Body damage is therefore ALSO expressed as a 0..100 % somewhere | VERIFIED comment |
| aero.ini `ZONE_<Z>_CL/CD` | 0..0.015 | `CL/(1+k*D[z])`, `CD*(1+k*D[z])` per wing (mechanical topic) | VERIFIED formula comment |
| suspensions.ini `[DAMAGE] MIN_VELOCITY GAIN MAX_DAMAGE DEBUG_LOG` | 83 cars: MIN 40 (x74) 30/25/85; GAIN 0.0004 (x63) .. 0.005; MAX 0.05 (x81) 0.02 / 0.11; **missing on ks_audi_sport_quattro, _s1, _rally** | Steer-rod bend per impact km/h above MIN_VELOCITY (mechanical topic; it uses the SAME impact speed as the zones) | VERIFIED comments |

---------------------------------------------------------------------------------------------------------------------------------
## 3. Uncertain points, each with the evidence for and against

1. **Sum or max?** I propose a **SUM** per impact. Evidence:
   - Per-impact `GAIN` wording on the suspension.
   - FULL_SPEED up to 220 on bumpers is barely reachable by one hit.
   - The HUD flashes per hit (`TIME=4` s) and the colour deepens with repeated hits; that last part is community memory, not verified.
   - Against: none found. A **max** model would make FULL_SPEED 150-220 almost unreachable and repeated taps harmless.
   - // ours choice: sum.
2. **Normal component or full relative speed?** I propose the **normal component** at the contact point. With full relative speed,
   a 200 km/h glancing wall scrape would add 200 to a zone, which is far heavier than AC feels. The UDP field is called "Impact speed",
   not "relative speed" [INFERRED]. // ours.
3. **Zone pick: position or normal?** I propose **position** (UDP gives `rel_pos`, the point in the car frame).
   - Normalise by the collider.kn5 half-extents. Cars are 1.4-3.7 m wide and 3.0-5.3 m long (sweep), so raw coordinates would favour
     FRONT/REAR.
   - // ours: `nx = x/(W/2)`, `nz = z/(L/2)` in body space; `zone = |nz| >= |nx| ? (nz > 0 ? FRONT : REAR) : (nx < 0 ? LEFT : RIGHT)`
     in Unity body axes (x right). **In AC axes +X = car LEFT, so in AC coordinates LEFT is x > 0.**
4. **CENTER.** No VISUAL_OBJECT uses it, the body mask has no roof, and a doc says "only the first 4 are valid". Only
   DAMAGE_GLASS_CENTER (the windscreen) and the HUD center.png consume it.
   - // ours: CENTER takes a hit when the contact point lies in the top 30 % of the hull height AND |nx| < 0.7 AND |nz| < 0.7 (a roof
     hit), or when a road/ground contact happens with the car's up vector below the horizon (rollover, upside-down landing).
   - Alternative if the user disagrees: CENTER = 0 always, and the windscreen breaks from FRONT. That matches "only the first 4 are valid".
5. **Minimum speed.** damage.ini has no global minimum: zones count from 0, and scratches start at 0. P already ignores impacts
   < 3 km/h (`AcCarPhysics.Impact`, `// ours`). // ours: count only `v_imp >= 3 km/h`, the same threshold. The suspension uses its own
   MIN_VELOCITY.
6. **Repeats during one contact (scraping).** AC's sound has separate hit and scrape events, and a scrape never reads like a stream of
   crashes [INFERRED].
   - // ours: damage only on OnCollisionEnter per collider pair.
   - While staying in contact, a new spike counts only if the normal speed is >= 10 km/h and at least 0.25 s has passed since the last
     count on that pair (// ours).
   - **Do NOT feed the existing `AcEvents.CarImpact` stay repeats into damage.** P raises them at 10 Hz with the full sliding speed
     (`collision.relativeVelocity.magnitude`), meant for the scrape sound.
7. **Car vs car against car vs wall.** The UDP event distinguishes the two, but nothing says the damage differs. // ours: the same rule.
   - In a car-car contact, BOTH cars take `v_imp` (the same normal relative speed), each in its own zone from its own local contact point.
   - Each owner computes only its own car: the local player for their car, the master for AI cars. Remote cars receive D[5] over the
     net, the way AC uses ACP_DAMAGE_UPDATE / BroadcastDamageUpdate.
8. **Track surfaces.** Kerbs and bottoming hit the colliders.ini floor box and the road, which is not body damage in AC (common
   experience) [INFERRED].
   - // ours: contacts with tagged road surfaces (`AcSurfaceTag.Of(collider) != null`) never add zone damage, except the CENTER
     rollover rule in item 4.
   - Walls and untagged track geometry (`AcSurfaceTag` null / wall) and cars do add.
9. **Scope of the multiplier.** I propose this split (// ours where noted):
   - Visual damage and the HUD use raw `D`.
   - Mechanical effects use `D * m` with `m = DamageMultiplier/100`: aero `k*D*m`; suspension `GAIN*(v-MIN)*m`, capped at MAX_DAMAGE;
     engine hit damage `* m`.
   - Evidence: the launcher calls the setting "Mechanical damage", there is a separate VISUALDAMAGE toggle, and the static field is
     `aidMechanicalDamage`.
   - Uncertain: whether shared-memory carDamage reports raw or scaled values. We have no reader, so it does not matter.
   - Our lobby default `AcRules.DamageMultiplier = 0` ("iteration 1 has no damage model") should become 100, the server_cfg.ini default.
     That is R's call.
10. **Ceiling.** None in the data, and FULL_SPEED max is 220. // ours: clamp `D[z]` to 6553 so it packs as a u16 in 0.1 km/h steps
    for the net record. Visual and mechanical mappings saturate on their own.
11. **Body damage % (pit repair time).** The repair time is `BODY_REPAIR_TIME_SEC` per 10 % body damage, but no file defines the
    percentage. // ours: `bodyPct = 100 * saturate(max(D[0..3]) / MAX_DAMAGE_KMH)`, with 60 from damage_displayer.ini read at run time.
    Time = `BODY_REPAIR_TIME_SEC * bodyPct / 10`; repair sets `D[0..4] = 0`. Suspension uses the same idea with its own % (mechanical topic).
12. **Repair points.**
    - Pit-stop repair (AC pit menu: "REPAIR BODY", "REPAIR BODY + SUSPENSION", "REPAIR ENGINE"; acs.exe strings [VERIFIED]).
    - New session / restart.
    - AC's ESC "back to pits" in practice resets the car [INFERRED, community advice; unverified]: `AcRace.BackToPits` repairs.
    - Our own "reset to track" (`AcCarInput.ResetRequest`) should NOT repair (// ours), otherwise it becomes a free fix.

---------------------------------------------------------------------------------------------------------------------------------
## 4. Exact rules to implement (Unity side; Data side Unity-free)

**Data (Unity-free, e.g. `Data/Physics/AcDamageSpec.cs`, P or the damage owner)**, read from data.acd at spawn:
- damage.ini: `[DAMAGE] INITIAL_LEVEL` (default 20 // ours if absent); `[SCRATCHES] MIN_SPEED/MAX_SPEED` (0/20 // ours default);
  `[OSCILLATIONS] ENABLED`; `VISUAL_OBJECT_n` with NAME / DAMAGE_ZONE / MIN / FULL / ...; `DAMAGE_GLASS_<Z>` MIN / FULL.
  - A missing glass section means that zone's glass never breaks (// ours; ks_mazda_787b has glass nodes but no sections).
  - Skip a VISUAL_OBJECT whose NAME is not a node (13 such on 10 cars, section 5) with one log line, like AC's
    `ERROR: DAMAGE ZONE %S NOT FOUND`.
  - Zone names are parsed case-insensitively: FRONT=0, REAR=1, LEFT=2, RIGHT=3, CENTER=4 (CENTRE as an alias // ours).
- car.ini `[PIT_STOP] BODY/ENGINE/SUSP_REPAIR_TIME_SEC`; aero.ini `ZONE_*`; suspensions.ini `[DAMAGE]` (mechanical topic).
- System files: `system/cfg/damage_displayer.ini [MAIN] MAX_DAMAGE_KMH` (60) and `TIME` (4 s HUD flash);
  `system/cfg/assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD` (0.65).

**Per car state**: `float[5] D` (km/h units), `float lastCountAt` per pair (cooldown).

**On contact (P, in `AcCarPhysics`, physics thread, local and AI cars only, not puppets)**:
```
// pre-step cache in FixedUpdate: v0 = body.velocity, w0 = body.angularVelocity, com0 = body.worldCenterOfMass (and the same for the other car)
p  = contact.point (world); n = contact.normal (world, Unity: points from the other collider toward this one)
vA = v0 + Cross(w0, p - com0)
vB = other car ? (other.v0 + Cross(other.w0, p - other.com0)) : (other.attachedRigidbody ? other.attachedRigidbody.GetPointVelocity(p) : 0)
vImp = |Dot(vA - vB, n)| * 3.6f                          // km/h, normal component [INFERRED]
if (vImp < 3f) return                                     // ours (same as the CarImpact threshold)
if (road surface tag && !rolloverCase) return             // ours (section 3.8)
local = bodyInverse * (p) -> (x right, y up, z fwd), relative to the hull centre (collider.kn5 bounds centre in body space)
nx = local.x / halfW; nz = local.z / halfL; ny = (local.y - hullMinY) / hullH           // hull = collider.kn5 bounds (model->body)
zone = (ny > 0.7f && |nx| < 0.7f && |nz| < 0.7f) || upsideDownGroundHit ? CENTER        // ours
     : |nz| >= |nx| ? (nz > 0 ? FRONT : REAR) : (nx < 0 ? LEFT : RIGHT)                 // Unity body axes; AC +X = LEFT
D[zone] = min(D[zone] + vImp, 6553f)                      // sum [INFERRED], cap ours
raise the damage-changed event (zone, vImp) -> visuals, HUD flash (TIME s), the net record, mechanics (the suspension uses vImp itself)
```
- Use `OnCollisionEnter`. For `OnCollisionStay`, only re-count when `vImp >= 10 km/h` and >= 0.25 s since the last count on that
  collider (// ours).
- Fallback if pre-step caching is not wanted: `Dot(collision.relativeVelocity, n)`. This is linear only and ignores spin, so a
  spinning car hitting a wall reads too low (// ours, acceptable).
- Use ALL contacts of the event (average point, max vImp), not only `GetContact(0)` (// ours).
- Hull extents: the collider.kn5 AABB converted with `AcCarModel.ModelToBody` (sweep: width 1.38-3.74 m, length 2.99-5.32 m). For
  **ks_ford_mustang_2015** the hull is displaced by 5 m (ac_car_formats.md C3), so fall back to the LOD_0 model bounds when the hull
  centre is > 0.5 m from the model centre (// ours).

**Multiplier**: `m = AcSession.Rules.DamageMultiplier / 100f`. Visual and HUD use `D`; mechanics use `D * m` and the suspension
`vImp` scaled by m. With m = 0 there are no mechanical effects at all, which is the launcher's "Off".

**Net (N)**: 5 x u16 (0.1 km/h) in the player record and in the AI blocks, sent when changed. Remote cars apply D to visuals only.

---------------------------------------------------------------------------------------------------------------------------------
## 5. Sweep: damage.ini over the 86 owned cars (`colsweep`, 2026-11-02) [VERIFIED in install]
- **All 86 have damage.ini.**
  - Sections: SCRATCHES 86, OSCILLATIONS 86, DAMAGE 86, DAMAGE_GLASS_FRONT/REAR/LEFT/RIGHT/CENTER 84 each (missing on
    **ks_mazda_787b**, which has the glass nodes, and **tatuusfa1**, which has none), VISUAL_OBJECT_n 260 in total.
  - No other sections or keys exist. The section order varies but is irrelevant.
- `[DAMAGE] INITIAL_LEVEL`: 20 x85, **100 x1 (ks_alfa_33_stradale)**. This is the only key in [DAMAGE].
- `[SCRATCHES]`: MIN_SPEED 0 / MAX_SPEED 20 on all 86, so it is effectively a constant.
- `[OSCILLATIONS] ENABLED`: 1 x81, 0 x5 (ferrari_f40, ferrari_f40_s3, ktm_xbow_r, lotus_49, ks_maserati_levante).
- **VISUAL_OBJECT_n** (260 objects):
  - Count per car: 0-5; **vo = 0 on ks_alfa_33_stradale, ktm_xbow_r, lotus_49, ks_maserati_levante, ks_porsche_macan**.
  - DAMAGE_ZONE: FRONT 145 / REAR 115, never LEFT/RIGHT/CENTER.
  - MIN_SPEED: 30 x118, 20 x113, 0 x25, 25 x2, 29 x2.
  - FULL_SPEED: 100 x62, 80 x49, 150 x40, 60 x28, 50 x26, 20 x25, 220 x21, 70/110/120/90 (2-3 each); 20..220.
  - STATIC_ROTATION_ANGLE: -6..12 deg, 34 distinct values. STATIC_ROTATION_AXIS: mostly +-1,0,0; one is `2,0,0`, so **normalise
    the axis** (// ours).
  - OSCILLATION_MIN/MAX_ANGLE: -1..2 / -4..6. The comment says "never less than 0 and never more than 5", but the data has -4, -2 and 6,
    so do not clamp (// ours).
  - MULT_G: 0.01..3. ALLOWED_G: `0,1,1` x84, `1,1,1` x63, `0,1,0` x60, `1,0.1,0.1` x30, `1,0,0` x18, `1,1,0` x5.
  - NAME: 26 distinct, including typos that ARE the node names in their kn5 (`REARE_BUMPER` x5).
  - FULL_SPEED <= MIN_SPEED never happens.
- **Odd data (13 VISUAL_OBJECTs whose NAME is not a node of the main kn5, checked against `car.Kn5Path`; the visual topic should
  re-check against the lods.ini LOD_0 file)**:
  - ks_audi_r8_plus WING
  - ks_audi_sport_quattro / _s1 MOTORHOOD
  - bmw_z4_gt3 MOTORHOOD
  - ks_lamborghini_huracan_performante MOTORHOOD
  - ks_lamborghini_sesto_elemento MOTORHOOD
  - **ks_mazda_787b NAME=0 (x2)**
  - ks_nissan_gtr `REAR_HOODc`
  - ks_pagani_huayra_bc FRONT_WING
  - ks_porsche_cayenne REARHOOD
  - **shelby_cobra_427sc MOTORHOOD, FRONT_BUMPER, REAR_BUMPER**
- **DAMAGE_GLASS nodes in the main kn5** per zone (F/R/L/R/C): the most common pattern is F1R1L2R2C2 (18 cars). lotus_49 has F0R0
  (no front/rear glass) and tatuusfa1 has none at all.
- **Glass sections** (84 cars):
  - FRONT MIN 30 x61 / 20 x13 / 15 x10, FULL 60 x74 / 80 x10.
  - REAR MIN 30 x61 / 20 x13 / 10 x10, FULL 60 x53 / 30 x18 / 80 x10 / 40 x3.
  - LEFT = RIGHT: MIN 20 x79 / 10 x4 / 30 x1, FULL 30 x73 / 80 x10 / 60 x1.
  - CENTER MIN 30 x74 / 20 x10, FULL 80 x80 / 50 x3 / 60 x1.
- Other inis (for the mechanical topic):
  - aero.ini WING_n ZONE_FRONT_CD 0..0.015 (0 x119, 0.005 x79, 0.015 x77); ZONE_FRONT_CL 0 x202, 0.015 x73, 0.01 x15; REAR_CD/CL similar;
    LEFT/RIGHT_CD 0.01 x91; LEFT/RIGHT_CL 0.01 x7. One FIN_n with all 0.
  - suspensions.ini [DAMAGE] on 83 cars, as in section 2.
  - engine.ini [DAMAGE] on 86: RPM_THRESHOLD 6600..18200, RPM_DAMAGE_K 1 x75 (0.05..4), TURBO_BOOST_THRESHOLD 0.8..3,
    TURBO_DAMAGE_K 5 x76 (30 x6).
  - car.ini PIT_STOP repair times as in section 2.
  - colliders.ini: 1 box x79, 2 x4, 3 x2, 4 x1. GROUND_ENABLE=1 on 92 boxes and absent on 5. 5 files have a `[HEADER] COLLIDERS=1`.
  - collider.kn5 present on 86/86.

---------------------------------------------------------------------------------------------------------------------------------
## 6. Seams for the implementers (who would own what)
- **P** (`AcCarPhysics`):
  - Pre-step velocity cache, the contact rule in section 4, `D[5]`, and the multiplier on the mechanics.
  - A NEW event or field is needed: zone, vImp, and normal/other kind (car / wall / road). The existing
    `AcEvents.CarImpact(rig, kmh, point)` mixes hits and 10 Hz scrape repeats and must stay as it is for audio.
- **C** (visuals): consumes `D` (panels, glass, shader scratches and dents). Remote cars get D from N.
- **H** (HUD): the AC damage displayer. It uses `content/texture/damage/*.png` and `damage_displayer.ini`; a zone is fully red at
  MAX_DAMAGE_KMH; it shows for TIME seconds after a hit; PRINT_VALUES shows the numbers.
- **R**: change the default of `AcRules.DamageMultiplier` (0 -> 100 like server_cfg), own pit repair and the BackToPits repair, and
  reset D on session start.
- **N**: 5 x u16 per record.
- **A**: no change. A could add the `impact_angle` parameter from the contact normal (currently a fixed 90 // ours in AcCarAudio).

## 7. Open questions only a real AC run could settle (not done: the game was not launched)
- Exact accumulation (sum against max), whether CENTER is ever written, and the scrape contribution. A Python app or shared-memory
  reader logging `carDamage[5]` across scripted wall hits at 20/40/60 km/h head-on, 45 deg and scraping would settle all three in one
  session. The user owns AC and Content Manager; that would take about 15 minutes.
- Whether `DAMAGE=0` also zeroes the visual zones and the HUD.

---------------------------------------------------------------------------------------------------------------------------------
## Critic addendum (2026-11-02, independent re-check; original text above kept unchanged)

Method: re-ran `colsweep` (harness Release build, 86 owned cars), `colstrings`/`colnear` on acs.exe and acServer.exe, read the raw
ferrari_458 + sdk formula_k inis, the sdk UDP example, `pdftotext` of `sdk/dev/car_pipeline_2.0rev/AC_Pipeline_PUB_Rev2.0.pdf`, the
launcher HTML, system cfgs, and the project code. Web: 3 fetches/searches (URLs below).

### C1. Corrections (wrong or overstated)
1. **"Alternative: CENTER = 0 always, and the windscreen breaks from FRONT" (3.4) is wrong as a mapping.** The pipeline PDF names the
   glass zones: `DAMAGE_GLASS_CENTER_1 central glass, usually windscreen`, `DAMAGE_GLASS_FRONT_1 front headlight glass or similar`,
   `REAR_1 rear/brake light glass`, `LEFT_1/RIGHT_1 side windows` [VERIFIED in install, PDF text]. FRONT glass = headlights, not the
   windscreen. With CENTER = 0 the windscreen never breaks on any car (84 cars author CENTER glass MIN 20-30 / FULL 50-80). If the
   CENTER=0 alternative is chosen, the windscreen must be driven explicitly from FRONT (an // ours remap); it does not "match the data".
2. **"the HUD's center.png consumes CENTER" (1.1, 3.4) is not supported.** acs.exe references `content/texture/damage/` front, rear,
   left, right, engine, gearbox, sus_*, tyre_*, flatspot_* as full literal paths, but **no `center.png` string exists** in acs.exe
   (`colstrings acs.exe "center\.png|damage/"`) [VERIFIED in install]. The file is on disk but nothing names it; the HUD most likely
   draws only the 4 directional zones (consistent with "only the first 4 are valid") [INFERRED]. Treat CENTER as consumed only by
   DAMAGE_GLASS_CENTER. H should not plan a CENTER HUD cell from AC evidence (// ours if wanted).
3. **Zone index order is DOCUMENTED, not "INFERRED-strong".** ACC shared memory doc v1.7.6: `float carDamage[5]` "front 0, rear 1,
   left 2, right 3, centre 4" [DOCUMENTED: https://github.com/fenegroni/simdisplay/files/6255394/ACCSharedMemoryDocumentationV1.7.6.pdf].
   It is ACC's doc; AC's struct has the same field, so for AC it remains [DOCUMENTED for ACC / INFERRED for AC]. No units are given.
   The "only the first 4 are valid" quote is confirmed verbatim at https://github.com/dcratliff19/AC-SimInfo-Docs-Markdown [DOCUMENTED].
4. **Missed cars with no front/rear glass nodes.** 1.1/5 single out lotus_49 ("F0R0"). The sweep gives **7** cars with F0R0: ferrari_312t,
   lotus_98t, lotus_exos_125, lotus_exos_125_s1, lotus_49, shelby_cobra_427sc, tatuusfa1; ferrari_312t also has L0R0 (CENTER only)
   and ferrari_599xxevo has R0 [VERIFIED in install]. F1R1L2R2C2 = 18 cars is correct.
5. **Axis normalisation applies to OSCILLATION_AXIS too**, not only STATIC_ROTATION_AXIS: `OSCILLATION_AXIS '2,0,0' x3` (and STATIC x1)
   [VERIFIED in install].
6. **Small range errors in sections 2/5** [VERIFIED in install]: suspensions.ini GAIN min is **0.0003** (x7), not 0.0004;
   aero ZONE_REAR_CL max is **0.01**, not "similar" to FRONT's 0.015; engine.ini TURBO_BOOST_THRESHOLD / TURBO_DAMAGE_K exist on **84**
   cars, not 86 (RPM_* on 86). formula_k damage.ini has no glass sections or MIN/FULL comments; only the `;kmh` SCRATCHES comment is
   in both files cited.
7. **Path**: the UDP example is `sdk/dev/acRemoteServerUDP_Example/acRemoteServerUDP_Example/Program.cs` (+ `ACSProtocol.cs` for
   130/10/11). Content as stated [VERIFIED]. Its `speed` field has **no unit** in the sdk; km/h there is [INFERRED].
8. **"P already ignores impacts < 3 km/h"** is true only for OnCollisionEnter; stay repeats are dropped below **5 km/h**, and the 0.1 s
   gate `_lastImpactAt` is ONE timer per car (shared by enter and stay), not per collider pair
   (`Game/Physics/AcCarPhysics.cs` l.888-906) [VERIFIED in code]. The enter path ALREADY computes `|Dot(relativeVelocity, n)|*3.6`
   on `GetContact(0)`, i.e. the section 4 "fallback" is what P does today.
9. **Pseudocode uses `body.velocity`**; this project (Unity 6) uses `Rigidbody.linearVelocity` everywhere (`Game/Ai/AcAiDriver.cs:104` etc.).
10. **Car vs REMOTE car gap (section 4 vB).** Remote cars are puppets: kinematic, moved by `Body.MovePosition` (`AcCarPhysics.cs`
    l.324-365), and `Impact` returns early for puppets. A kinematic body's `linearVelocity`/`GetPointVelocity` reads ~0 in Unity
    (the project's own `Game/Ai/AcAiTraffic.cs:159` treats kinematic as zero), so `vB` for a remote car would be 0 and the local car
    alone supplies the closing speed. Use the velocity N feeds in `ApplyPuppet(..., velocity, ...)` (stored as `_prevVel`, private
    today -> needs a public getter, P's call) [INFERRED from code].
11. **Contact normal direction** claim ("points from the other collider toward this one") is uncited; it does not matter because
    the rule uses `Abs(Dot)` [INFERRED].

### C2. Missed install evidence
- acs.exe also holds `ERROR: Trying to send a collision to car out of bounds: %d cars: %u` [VERIFIED]: collisions are dispatched to a
  car INDEX, which supports 3.7 (each car of a car-car contact receives its own collision) [INFERRED].
- acs.exe holds `bodyDamageLevel`, `engineDamageLevel` (next to hdr/escmode/acos.ini keys, purpose unknown), `HIDE_DAMAGE`/
  `SHOW_DAMAGE`, `BUT_REPAIR_BODY/ENGINE/SUSP`, `SELECT DAMAGE REPAIR`, `QM_HEADER_REPAIR` [VERIFIED strings]. `bodyDamageLevel` hints
  that AC keeps a scalar body damage level (fits 3.11's body % idea) but its scale is unknown [INFERRED].
- `Damage zone %d: %f` sits next to `requesting car list`, `received`, `THIS IS MY CAR!`, `ASK FOR NEXT 10 CARS` [VERIFIED]: it is
  logged when the car list arrives from the server, i.e. a joining client receives each car's zone levels. Supports owner-sends /
  server-rebroadcasts, and means late joiners need the current D[5] (N: include D in the full snapshot, not only on change) [INFERRED].
- Pipeline PDF: "The collider must not extend below the floor of the car" and "pivot must be in the 0,0,0" [VERIFIED]. Supports
  "floor contacts are the colliders.ini box, not body damage", and marks the mustang hull offset as an authoring error (fallback is right).
- Pipeline PDF gives txDamageMask as 512x512 in the damage section but `Damage_Mask.dds is 256x256` in its texture table [VERIFIED];
  the visual topic should read the real size, not assume.

### C3. What holds (re-verified)
- damage.ini sweep numbers in section 5 (sections, INITIAL_LEVEL 20x85/100 alfa, SCRATCHES 0/20 x86, OSCILLATIONS 0 on the 5 named
  cars, 260 VISUAL_OBJECTs FRONT 145 / REAR 115, MIN/FULL distributions, glass MIN/FULL tables, the 13 missing NAMEs, mazda_787b /
  tatuusfa1 without glass sections, 3 audi without suspension [DAMAGE], collider.kn5 extents 1.38-3.74 x 2.99-5.32, colliders.ini
  counts): identical in today's run [VERIFIED in install].
- System values: damage_displayer.ini TIME=4, MAX_DAMAGE_KMH=60, PRINT_VALUES=0; assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD=0.65;
  server_cfg DAMAGE_MULTIPLIER=100; assists.ini VISUALDAMAGE=0, DAMAGE=0 (separate keys); launcher slider 0..100 step 10 "Off",
  label "Mechanical damage", VISUALDAMAGE toggle commented out [VERIFIED in install].
- acServer strings (DamageMultiplier, DamageZoneLevel, BroadcastDamageUpdate, AddClientCollisionEvent, ImpactSpeed,
  COLLISION_WITH_CAR/ENV, COLLISION BETWEEN:) and acs.exe `Sending ACP_DAMAGE_UPDATE`, `ERROR: DAMAGE ZONE %S NOT FOUND` [VERIFIED].
- Comments quoted from aero.ini, suspensions.ini, car.ini [PIT_STOP], damage.ini, colliders.ini, pipeline collider/mask/vibrate text
  [VERIFIED verbatim]. The km/h-units argument (aero 1.5 % vs x1.9, suspension "per impact kmh", MAX_DAMAGE_KMH) stays the strongest
  reading but is still [INFERRED]; no public source states carDamage units (search found none).
- CSP physics-scripts page exists and lists "Car damage; Engine damage; Gearbox damage" with no ranges
  [DOCUMENTED: https://cup.acstuff.club/docs/csp/cars/physics-scripts].
- Code references exist as named: `AcSession.Rules` / `AcRules.DamageMultiplier = 0` (`Game/Shared/AcSession.cs:46`),
  `AcEvents.CarImpact`/`RaiseCarImpact`, `AcSurfaceTag.Of` (null for walls and untagged), `AcCarModel.ModelToBody`,
  `AcRace.BackToPits`, `AcCarInput.ResetRequest`, AcCarAudio `ImpactAngle` fixed 90 // ours [VERIFIED in code].
- Still unverifiable without running AC: sum vs max, whether CENTER is ever non-zero, scrape contribution, and whether DAMAGE=0
  hides visuals (section 7 stands).
