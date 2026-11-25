# C - Car model: notes (2026-11-01, second pass)

## Files (all mine)
`Assets/#User/SomniumAssetto/Game/Cars/`
- `AcCarModel.cs` - the contract (filled) + `AcCarModelOptions` (+ `MaxLod`, `Instruments`, `Digital`, `Layer`, `SliceMs`, `ForKind(kind)`).
- `AcCarData.cs` - Unity-FREE `AcCarVisual`: car.ini (GRAPHICS_OFFSET/PITCH, DRIVEREYES, STEER_LOCK/RATIO), tyres.ini (default compound:
  RADIUS, PRESSURE_STATIC), lods.ini, lights.ini, blurred_objects.ini, analog/digital_instruments.ini, driver3d.ini, knh points, ksanims,
  `DriverSkinDirs` (skin.ini [driver model] SUIT/GLOVES/HELMET).
- `AcCarJob.cs` - worker half of a build (+ driver skin textures prepared on the worker, `Reread` for EnsureInterior).
- `AcCarParts.cs` (`AcCarPartSet`, + `GraftInterior`), `AcKsanimPlayer.cs`, `AcCarLights.cs`, `AcCarInstruments.cs`, `AcCarDigital.cs`
  (fonts + `Clear`, texts, RPM graph, `AcCarTextureItem` = GEAR_TX, `AcCarDisplayColor`, `AcCarUnits`, `AcCarDisplayExtras`), `AcCarHull.cs`,
  `AcCarDriverModel.cs` (driver + knh + steer/shift clips + skins + visibility), `AcCarShading.cs`, `AcCarModelDriver.cs`.
- `Editor/AcLabCars.cs` - lab scenarios. Probes: `DevTools/harness-ac/ProbeC.cs`, `ProbeC2.cs` (driverpose, seats, digitems);
  private harness `DevTools/agents/C/harness` (compiles AcCarData.cs with AC_CARDATA; `census`, `inigrep`).

## Public API added (only ADDED, contract untouched)
First pass: `Options`, `Visual`, `Lod`, `ActiveLod`, `SteeringRimRadius`, `SteeringCenter`, `SteeringAxis`, `DriverModel`, `HasDoor`,
`DoorHandle`, `DoorOpen`, `BuildMs`, `Disposed`, static `LiveCount`, `ViewerOverride`; `SetWheelSpeed`, `SetWipers`, `SetLapTimes(last,best)`,
`ApplyTelemetry(ref t)`, `Tick(dt)`.
Second pass: `IEnumerator EnsureInterior(Action<bool> done)`, `HasInterior`, `SetLapTimes(lap, last, best)`, `SetDelta(ms)`,
`SetAssistLevels(tc, abs)`, `SetTyrePressures(lf, rf, lr, rr)`, `PrimarySet`, `ActiveSet` (read-only views); `AcCarFonts.Clear/Count`;
`AcCarDriverModel.Shift/Tick/SetVisible/Visible/Shifting/SkinSlots/SkinTextureCount`; enum member `AcLedSeriesKind.GForce`.

## Second pass - what changed (critic items)
1 dash ink = COLOR/255 x INTENSITY (72/392 items are dark ink, e.g. 458 10,10,10 x 0.5). 2 local car fills LapMs/LastMs/BestMs/DeltaMs from
AcLocal every frame; N asked for `SetLapTimes(3)` on remote cars. 3 PERF = delta, GFORCES = LatG/LongG (MIN/MAX/ADDSIGN/DIRECTION parsed),
GFORCE_SERIE built (huracan 4 bars), PRESSURE = tyres.ini PRESSURE_STATIC of the DEFAULT compound (86/86 have it) or F's garage value
(`AcFront.SetupValue`, re-read on a `setup.*` AcSettings change), TC/ABS level = 0 when the aid is off else 1 (// ours). 4 UNITS=SYSTEM
(26 items) follows `AcHud.UseMph()` (1 s cache). 5 request to N: AI records with `ForKind(Ai)`. 6 `EnsureInterior` (in-place COCKPIT_HR
graft). 7 no passenger seat for class "race". 8 driver pose PROVEN (below). 9 driver hidden from LOD_2 on; shift clips played; skin.ini
suit/gloves/helmet applied (MPB, shared refcounted uploads). 10 driver hides for the local DRIVER seat (each frame); passenger sees it.
11 lab scenarios added (below). 12 N_TIME implemented [BELIEF]; VERSION read, unused (all 366 = 2) [BELIEF no v1/v2 difference].
13 water temp 90 C ours (P has none: 31 WATER_TEMP needles, 19 LED bars, 14 texts constant). 14 RPM_GRAPH textures are all PNG but live in
`<car>/texture/` - the old lookup (kn5 + car root) found NONE of the 26 digital textures; now texture/ first, PNG+DDS via W's
`AcTexPrepare`/`AcTextures` (18 GEAR_TX DDS now drawn). 15 lights doc: no reverse/indicator sections exist. 16 `SetWipers` has no caller
(no wiper input) - noted in REQUESTS. 17 `Wheels[]`/`SteeringWheel`/`DoorL/R` documented as the primary LOD's. 18 hull honours
`ConvertedToUnity`. 19 instruments written once per frame (Tick only). 20 font cache freed with the last car (failed names retry).
21 dispose mid-build: W's builder disposes its half model when its root is destroyed and C disposes a finished one - no change needed.
Extra: lights.ksanim without a [LIGHT_ANIMATION] section (miata pop-ups, miura lids, r8 plus, levante) now plays by name [BELIEF], TIME 1 s.

## Verified (offline, real install E:\SteamLibrary\steamapps\common\assettocorsa)
- build.sh: my files 0 errors (runtime / server / editor tool); remaining errors at the time were in Game/Physics and Game/Audio (P, A
  mid-edit; a later run: runtime + server 0 errors, editor tool errors only in AcLabPhysics.cs). fusionapi check.sh: all 117 proven.
- Harness `driverpose` (86 cars): knh locals on DRIVER:* + the driver kn5's own FBX wrapper -> RIG_Hips/Head/HAND_L/R error 0 mm on all
  84 cars with a full knh (5 driver kn5s, wrapper "FBX: 2016_driver*.fbx"); 2 empty knh (giulia_qv, 787b) -> fallbacks.
- Harness `seats`: 78 offset drivers, 13 race class -> 65 passenger seats.
- Harness `digitems`: 392 items, 26 textures (18 dds GEAR_TX, 8 png RPM_GRAPH), all in `<car>/texture/`, 0 missing.
- Harness `cardata`: 86/86 load; default compound 0/1/2 = 30/45/11; PRESSURE_STATIC present on all 86.
- Census: GFORCE_SERIE 4 sections (1 car), GFORCES 5 items, GEAR_TX 18, PRESSURE 20, PERF 6, LAPTIME 9 / LAST 6 / BEST 2 / CURRENT 2,
  UNITS SYSTEM 26, N_TIME 40 (all 0.3), lights.ini sections only HEADER/LIGHT/BRAKE/LIGHT_ANIMATION (4).
- First pass (still valid): `carparts` node census, `needles`, `rimsusp`, wiper/paddle clips are full cycles.

## Lab pass (2026-11-01, integration lab agent)
- cars-458 x2 PASS: 4 wheels, steer, ground (front 12 mm / rear 0 mm in model space), eye 0.99, hips, hands, hull, door swings out (handle
  x -1.13 -> -2.05), NEW checks: +30 deg spin rolls every tyre top forward (+0.16 / +0.18 m), +25 deg steer points both fronts right (+0.14 m).
  Pictures LOOKED at: paint / glass / rims / tail + head lights / open door / cockpit; steering -90 / +90 rotate the rim (LED strip left /
  right) correctly. FIXED: crumpled paint = ksPerPixelMultiMap_damage_dirt txNormal is the DAMAGE normal map; `AcCarShading.FlattenDamageNormals`
  after each build (+ interior graft) sets `_Flags.y = 0` / flat `_NormalTex` (log "damage normals flat n"; 458: 2, abarth500: 1); re-shot smooth.
  Asked W to skip it in AcShading.Of (REQUESTS). Purple tint in the hub / lab = F's showroom colorCurves ambient (REQUESTS FYI), not C.
- cars-gauges PASS: miata RPM needle clockwise (cross -36), tatuusfa1 display "3 / 6500 / 130 / 20.0L" upright, left-to-right (picture).
- physics-458-magione pictures: rims, tyres in the arches, brake calipers right; no floating / sunk wheel.

## NOT verified (needs the lab / client)
- Nothing rendered yet: paint side, UVs, needle sense on screen, digital text plane, Sprites/Default in the client, emissive / HDR look of
  COLOR x INTENSITY (values up to 6), GEAR_TX / RPM_GRAPH quad placement (POSITION = lower left [BELIEF]), driver skin MPB on W's shader,
  EnsureInterior graft (parent lookup by name), driver shift clip look, pop-up by name.
- [BELIEF] AC's g sign (X = car left, Z = forward), GFORCES ADDSIGN 0 = magnitude, N_TIME meaning, steer.ksanim time mapping, BRAKE
  OFF_COLOR tail rule, FLASH/PITLINE meaning, series LEDs by visibility.
- Not built: KERS series (2 sections), ITEM_REF / TRIGGER other than GEAR_TX, mirrors.ini, wing animations, car_steer_extra, FUEL_CONS
  ("--"), AMBIENT_TEMP (26 C ours), TURBO_LEVEL (1 ours), low-res AC driver (DRIVER_HR 25 m on 13 cars - we hide from LOD_2 instead),
  dropping a built interior again (stays until dispose).

## Numbers we invented (// ours)
wiper cycle 1.4 s; LOD/cockpit hysteresis 0.5 m; shift clip 0.3 s without driver3d times; water 90 C, ambient 26 C, TURBO_LEVEL 1, TC/ABS level
1 when on; driver hidden from LOD_2; passenger seat: |hips x| > 0.2 m and class != race; door handle placement; hip/hand fallbacks; hull box
lifted 0.12 m; light anim TIME 1 s for lights.ksanim by name; PERF format +s.ff; display z offset 0.5 mm.

## Lab (to run later; `bash DevTools/lab.sh <name> [-accar id] [-acskin id]`)
- `cars-458` (any -accar): 4 wheels, steer, wheels on y=0, eye/hips/hands, hull, steer +90 rim top right, left door swings out; shots.
- `cars-five`: 458, abarth500, miata, tatuusfa1, mustang (mustang hull = BoxCollider).
- `cars-lod`: 458 Remote, viewer 5/25/100/400 m -> >= 3 LODs.
- `cars-leak`: 20 cycles abarth500 (Local). `cars-leak-ai`: 10 cycles 458 ForKind(Ai): LiveCount 0, skin textures 0, font cache 0, tex +16 MB.
- `cars-gauges`: miata + tatuusfa1 rpm 0/3000/6500 + NUMERIC: RPM needle tips clockwise on screen, first text quad left-to-right + upright.
- `cars-driver`: 458 Ai: posed > 50 nodes, skin slots > 0, RIG_Hips on DriverHips (2 cm), right hand moves -90/+90, shift clip plays and
  ends, hidden at 100 m (LOD >= 2), back at 5 m. Not covered: the local-driver-seat hide (needs an AcCarRig + S).
- `cars-anim`: miata + f40 pop-ups move, lotus_49 susp clip +5 cm -> 3..7 cm, 458 paddle on gear 1->2, wipers, cockpit HR at 3 m / LR at 20 m.
- `cars-dash`: 458 dark ink colour < 0.05, bmw_z4_gt3 LAPTIME "1:05.4.." + TC_LEVEL 0, huracan 9 GEAR_TX (one shown in 3rd) + GFORCE LEDs
  partly lit at 1.2 g, mustang PRESSURE = PRESSURE_STATIC.
- `cars-interior`: 458 Remote -> EnsureInterior: HasInterior, needles bound, COCKPIT_HR for the passenger, second call true; shots before/after.

## Player.log lines
`Car: built <id>/<skin> LOD n[-m] in X ms (worker Y ms; meshes, tris, textures MB uploaded (MB in the kn5), hull k colliders, doors L/R,
steer yes r R, lights n, needles n, leds n, displays n, blur n, driver yes (posed n, skin slots n)/no, interior yes/no, passenger seat
yes/no, W nodes n)` · `Car: <id> hull rejected (off by X m), using the LOD_0 box` · `Car: <id> missing node a, b, ...` · `Car: <id> data:
<problem>` · `Car: font <name> loaded for cockpit displays` · `Car: <id> interior (COCKPIT_HR) built on demand in X ms (needles n, leds n,
displays n, lights n)` · `Car: <id> ITEM_n RPM_GRAPH|GEAR_TX texture(s) ... not found` · `Car: build of <id>/<skin> failed: <why>` ·
`Car: <id> tick failed, animations stopped: ...` · `Car: AcHud.UseMph threw ...` / `AcFront.SetupValue threw ...` (once, guarded).

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3; no lab run)
- AcCarModel.BuildAsync: every Root-destroyed exit (after the job, after a LOD, after the driver) now calls m.Dispose(): the LODs built so far,
  their texture scopes and LiveCount are freed (before, only the LOD in hand was).
- Driver skin: a file the worker skipped as cached but that was released before ApplySkin is re-read and prepared on the main thread (log once
  "Car: a shared driver skin texture was released before this car used it ...") instead of leaving the kn5's default suit.
- AcCarFonts.Load destroys the atlas when no unlit material can be made (the cached null left it unreferenced).
- Not verified in a lab.

## Last-gaps pass 2026-11-01 (integration re-run after the road-plane damper fix; no C code changed)
- cars-458 PASS (1 run): wheels 0/1 0.012 m, 2/3 0.000 m on the ground, steer / spin / door signs right. Pictures LOOKED at: front34,
  side_left - tyres on the ground, wheels centred in the arches, smooth paint.
- physics-fleet: the bmw_m3_e92 family's collider.kn5 hull (lowest vertex model y 0.058) reaches 4-5 mm below the static ground ->
  e92_drift BAD (REQUESTS "integration -> P / C").

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 2 / 9 C; build.sh 0 errors x3, check.sh all 117 proven; NO lab run yet)
Files: `Game/Cars/AcCarData.cs` (+ `AcCarDamageDef`, `AcCarPanelDef`, `AcCarVisual.Damage` from damage.ini), NEW `Game/Cars/AcCarDamageVisual.cs`
(`AcCarDamageSet` per LOD set, `AcCarDamageVisual` per car), NEW `Game/Cars/AcCarSmoke.cs`, `AcCarParts.cs` (`AcCarPartSet.Damage`, bound in
Bind and for a grafted COCKPIT_HR), `AcCarModel.cs`, `Editor/AcLabCars.cs` (`cars-glassfix`, `cars-damage`), probe `DevTools/harness-ac/ProbeDmgC.cs`
(`dmgvisres [car]`, AC_CARDATA: compiled by C's private harness `DevTools/agents/C/harness`, csproj line added).
Public API (additive): `AcCarModel : IAcCarDamageVisual` (P's interface) with `SetDamage(front, rear, left, right, center)` (ZoneByte km/h,
idempotent), `ClearDamage()`, `SetEngineSmoke(rate01)`, `DamageVisualsOn`, `DamageTexturesBuilt`, `DamageVisual`, `Smoke`;
`AcCarModelOptions.DamageTextures` (bool?, null = `AcCarDamageGate.TexturesWanted` at build start; Showroom = false).
- Paint: one damage-texture decision per car at `Begin` -> W's `AcKn5BuildOptions.DamageSlots` on every LOD and the later interior. Built ON:
  FlattenDamageNormals skipped; on the first non-zero zone each damage-paint material of THIS car is cloned (`new Material`) and swapped into
  every LOD set (+ graft); `_DamageScratch` / `_DamageDent` (mask order left, rear, right, front) written on a change only; repair writes 0,
  clones kept; Dispose destroys them. Built OFF: FlattenDamageNormals as iteration 1, no clones.
- Glass: top-most `DAMAGE_GLASS_<FRONT|REAR|LEFT|RIGHT|CENTER>` node per zone (`AcCarDamageDef.GlassZoneOfName`), SetActive(false) at bind on
  every set (G0); shown when `Glass01 >= AcDamageRules.GlassThreshold`; zone renderers get ONE per-car clone of the ksBrokenGlass material with
  `_GlassSet.x = 1` (+ the install crack map if the car was built without it). Stray ksBrokenGlass meshes keep the shared material (invisible).
  Token-less `DAMAGE_GLASS_` nodes are left alone (log once).
- Panels: `rest * AngleAxis(-f * ANGLE, normalize(-ax, ay, az))` per set (two sections on one node compose); wobble = damped spring 6 Hz /
  0.3 driven by `MULT_G * dot(accG, ALLOWED_G)` (accG from a finite difference of the model root, AC axes), clamped to [MIN, MAX] * f, within 60 m.
- Smoke: `AcCarSmoke` (ParticleSystem, Sprites/Default + a generated soft disc) from `AcDamageRules.EngineSmokeIni` [SETTINGS]; emitter above
  the MOTORHOOD meshes, else the bounds front. Created on the first rate > 0.
- `cars.visualdamage` change (AcSettings.Changed) re-applies the stored zones (off = pristine look; zones keep counting).

Verified offline (C harness `dmgvisres`, all 86 owned cars): damage.ini on 86, 0 parse problems, SCRATCHES 0..20 on 86, INITIAL_LEVEL 20 on 85,
OSCILLATIONS off on 5, all 5 glass sections on 84; VISUAL_OBJECT 260: found LOD_0 246 (= design), LOD_1 238 (design said 229: we also accept
case-insensitive matches, as AcCarPartSet.Find does), LOD_2 47, LOD_3 12; glass groups LOD_0 F/R/L/R/C 100/100/136/136/133; zone groups the kn5
builds ACTIVE: 20 (ktm_xbow_r LOD0 6 + LOD1 6, lotus_exos_125(_s1) LOD1 3+3, AND ks_ford_mustang_2015 LOD_2 2 - not in the research); stray
ksBrokenGlass meshes 3 (bmw_m3_e92 / _drift / _s1 `R_DOOR_mesh_SUB3`); token-less names 1 (mustang LOD1 `DAMAGE_GLASS_Vtr_Ant1`, active - kept);
138/138 masks decoded (DXT3/DXT5/BGRA32, 128..512, 256x128). lotus_elise_sc's `damage_mask.dds` IS embedded (the research said not).
458 weights: 30 km/h -> side glass breaks (20..30), scratch 1.0, dent 0.17; 60 -> front/rear glass, MOTORHOOD 1.14 deg; 150 -> all glass,
MOTORHOOD 3.71, REAR_BUMPER 5.71 deg.
NOT verified: nothing rendered (lab phase later): shader compile in Unity, paint look / mask side, bend sign (458 vs miata), wobble, smoke look,
glass crack look. Departures: DamageVisualsOn = the cars.visualdamage setting only (design: also false when built without textures) so glass
and panels work after a 0 -> >0 change mid-session (design 1.4); paint additionally needs DamageTexturesBuilt. W's AcShading does not read R's
gate itself: C snapshots it per car and passes `DamageSlots` (all LODs + the interior agree; tracks / showroom never get slots).
Open: a rig whose MODEL is replaced (N stand-in, rebuild) gets no push until P's Version moves again (REQUESTS C -> P, N). Sparks (2.5,
optional) not built.
Log lines: `Car: <id> damage visuals: damage.ini yes, textures ON|off (normals flat n), glass zones F/R/L/R/C ..., broken-glass meshes hidden: a
active zone node(s) + s stray mesh(es) outside a zone, panels x of y found (LOD_0), paint renderers p, ...` / `Car: <id> damage panel <NAME> not
found (LOD_n)` / `Car: <id> LOD_n node(s) ... named DAMAGE_GLASS_ without a zone - left alone` / `Car: <id> first visual damage: paint material
copies n on m renderers` / `Car: engine smoke from engine_smoke.ini: ...` / `Car: <id> damage visuals failed: ...`.
Ours: spring 6 Hz / damping 0.3, excitation MULT_G x g as radians (MULT_G 0.1 at 1 g = 5.7 deg), 5 g accel clamp, 60 m wobble range, 120 Hz
spring sub-steps, smoke particle life = OPACITY / -OPACITY_VELOCITY capped by [LIFE_TIME] SECONDS, GRAVITY read as buoyancy (rises), smoke
emitter placement, puff texture, dent full at INITIAL_LEVEL + MAX_DAMAGE_KMH, oscillations on when [OSCILLATIONS] is absent.
Lab to run: `cars-glassfix` (ktm LOD0, e92 LOD0, exos LOD1 at DAMAGE 0: no zone node shown, every broken-glass material glassDamage 0, 2
shots each), `cars-damage` (458 / miata / abarth500 / 312t at 0/30/60/150 per zone: glass vs g, panel angle = f x ANGLE, copies > 0, repair,
LOD 0 -> 1 keeps it, DAMAGE-0 build has no slots, smoke; ~90 shots - LOOK at the mask side: car LEFT = Unity -x).

## Damage pass 2026-11-02 (integration)
- `AcCarModel.Begin`: damage textures = `AcCarDamageGate.TexturesWanted && AcCarDamageGate.VisualOn` (VISUAL DAMAGE switched off since R's last
  Refresh also skips the ~6 MB, design R12).
- `Editor/AcLabCars.cs` cars-damage: the final LiveCount check no longer counts the hub's showroom car (F builds it after boot, in parallel).
- Lab results (integration runs): `cars-damage` 471 ok, only FAIL was that LiveCount artefact (now fixed); pictures looked at: 458 front 150
  (bonnet / bumper bent, headlights cracked, dents only at the front), 458 left 150 (left window cracked + scratches on the car's LEFT = mask
  sides right), miata front 150 (bonnet bends the other way: user to compare with AC), smoke (above the 458's FRONT bonnet - the engine is
  mid-rear: open). Model swap request: not needed (a rig's Model never changes; a rebuild is a new rig) - REQUESTS answered.

## Damage pass 2026-11-02 (fixer)
- AcCarDamageSet.Add: adding a glass zone node resets _shownKnown, so grafted COCKPIT_HR DAMAGE_GLASS_<Z> nodes of an already broken zone are shown.
- AcCarDamageVisual: paint vectors cached (_scr/_dent) and WritePaint/WriteGlass factored out; OnGraft writes the current state to clones made at graft time (glassDamage 1 + crack, scratch/dent).
- Glass breaks only in zones with a [DAMAGE_GLASS_<Z>] section (HasGlass), whatever GLASS_THRESHOLD (also in AcLabCars' expectation).
- Tick: once when the wobble gates off, theta/omega are zeroed and the active set re-applied (panels rest at the static bend).
- SwapGlass latches _glassUnavailable only when AcMaterials.DamageGlassGone (definitive); otherwise retried on the next change, glass not shown meanwhile.
- AcCarSmoke: every engine_smoke.ini fallback marked // ours; the install-quoting comment removed.
- AcCarModel.DamageTextures doc comment repaired.
- Verified: build.sh 0 errors; lab cars-damage (see report).
