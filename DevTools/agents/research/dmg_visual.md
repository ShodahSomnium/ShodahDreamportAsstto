# Car damage - how AC DRAWS it (visual side) and what our code needs

Research 2026-11-02 for "car damage like the game". Scope: paint dents / scratches / dust (ksPerPixelMultiMap_damage_dirt), cracked glass
(DAMAGE_GLASS_* + ksBrokenGlass), bent / wobbling panels ([VISUAL_OBJECT_n]), anything else. Physics of the damage amount (impact ->
zone value) is another agent's topic; here the zone value is an input.

Tags: **[VERIFIED in install]** = read from the real install with the harness (numbers below are tool output); **[DOCUMENTED: src]** =
written down by Kunos / a public source; **[INFERRED]** = our reading, must be checked against the game; **[UNKNOWN]**.

Probe: `DevTools/harness-ac/ProbeDmgVis.cs` (commands `dmgstr <bin> <regex>` names-only strings, `dmgrdef <shader>` names-only DXBC
reflection (RDEF chunk: cbuffer variable names / offsets / USED flag, bound resource names; NO disassembly), `dmgsweep [car]` fleet sweep,
`dmgch <kn5|dds> <tex> <out>` channel split PNG, `dmguv <car>` mask sampled at mesh uv0 per body region + panel pivots).
NOTE: I first wrote this probe as `ProbeDmg1.cs`; the Write tool reported the file as *updated*, so a parallel agent's `ProbeDmg1.cs`
may have been overwritten (I renamed mine to ProbeDmgVis.cs; ProbeDmg1.cs no longer exists). Whoever owned ProbeDmg1 must re-create it.

## 0. Sources found inside the install (all read-only)
- `sdk/dev/car_pipeline_2.0rev/AC_Pipeline_PUB_Rev2.0.pdf` (Kunos car pipeline doc), chapters "DAMAGE GLASS" and "CAR DAMAGE" (pdftotext
  lines 317-335, 1123-1320) - cited below as **[DOCUMENTED: Pipeline]**.
- `sdk/editor/system/shaders/html/<shader>/<shader>.html` - Kunos' per-shader parameter lists **[DOCUMENTED: SDK html]**.
- `sdk/editor/system/shaders/*.shader` (editor copies) and `system/shaders/win/*_ps.fxo` (the RUNTIME shaders, ps_4_1): compiled DXBC.
  Only the RDEF reflection was read (names, types, offsets, "used by this shader" flag) **[VERIFIED in install]**. Editor and runtime
  reflection agree for damage_dirt / BrokenGlass / Tyres.
- `acs.exe` strings (key names only) **[VERIFIED in install]**: `VisualDamageManager`, `DamageDisplayer`, `/data/damage.ini`, `SCRATCHES`,
  `OSCILLATIONS`, `STATIC_ROTATION_AXIS`, `STATIC_ROTATION_ANGLE`, `OSCILLATION_AXIS/MIN_ANGLE/MAX_ANGLE`, `MULT_G`, `ALLOWED_G`,
  `DAMAGE_ZONE`, `FULL_SPEED`, `INITIAL_LEVEL`, `DAMAGE_GLASS_FRONT/REAR/LEFT/RIGHT/CENTER`, `GLASS_THRESHOLD`, `damageZones`, `dirt`,
  `glassDamage`, `dirtyLevel`, `tyreDirtyLevel`, `blurLevel`, `bodyDamageLevel`, `engineDamageLevel`, `suspension_damage`, `dirtiness`,
  `content/texture/DAMAGE_GLASS.dds`, `content/texture/damage/{front,rear,left,right,center,engine,gearbox,sus_xx,tyre_xx,flatspot_xx}.png`,
  `ksPerPixelMultiMap_damage_dirt_sunspot`, `HIDE_DAMAGE`/`SHOW_DAMAGE`, log formats `Damage zone %d: %f`, `MAX SPEED %f MIN_SPEED %f`,
  `ERROR: DAMAGE ZONE %S NOT FOUND`, `ADDING ZONE: %d (%f,%f)`. No string for detaching parts or a flat-tyre mesh.
- `system/cfg/assetto_corsa.ini`: `[DAMAGE] GLASS_THRESHOLD=0.65`, `[DIRT] MULT=0.7`; `system/cfg/damage_displayer.ini`:
  `TIME=4 MAX_DAMAGE_KMH=60 ...` **[VERIFIED in install]**.
- `content/texture/DAMAGE_GLASS.dds` (the real crack map, 1024x1024 BGRA32) and `content/texture/damage/*.png` (HUD damage-displayer
  icons, 21 files) **[VERIFIED in install]**.
- Content Manager source (gro-ove/actools): `AcTools.Render/Kn5SpecificForwardDark/Materials/MaterialsProviderDark.cs` draws
  `ksBrokenGlass` with an `InvisibleMaterial`; `Kn5MaterialDarkMaps.cs` line 26: `_txNormal = ShaderName.Contains("damage") ? null :
  GetTexture("txNormal")` - CM never draws the damage normal map **[DOCUMENTED: https://github.com/gro-ove/actools/blob/master/AcTools.Render/Kn5SpecificForwardDark/Materials/Kn5MaterialDarkMaps.cs]**
  (= what our FlattenDamageNormals does; CM shows no damage at all).
- Web search found no public Kunos/CSP write-up of damageZones / glassDamage maths (searches logged in this session); the only other
  hit was a mod damage.ini whose comments equal Kunos' own ini comments.

## (a) The paint: ksPerPixelMultiMap_damage_dirt

### a1. Shader interface [VERIFIED in install, RDEF of system/shaders/win/ksPerPixelMultiMap_damage_dirt_ps.fxo]
- Resources: `txDiffuse t0, txNormal t1, txMaps t2, txDetail t3, txDamage t4, txDust t5, txShadow0..2 t6..8, txCube t10, txDamageMask t21`.
- `cbCarPaint` (64 B, all USED): `+0 fresnelC, +4 fresnelEXP, +8 fresnelMaxLevel, +12 nmObjectSpace, +16 isAdditive, +20 useDetail,
  +24 detailUVMultiplier, +28 shadowBiasMult, +32 float4 damageZones, +48 float dirt, +52 sunSpecular, +56 sunSpecularEXP`.
- VS input signature: `POSITION NORMAL TEXCOORD0 TANGENT` - **one uv set**, so txDamage / txDust / txDamageMask all sample uv0 (see a3).
- Sibling `ksPerPixelMultiMap_damage` (older): same minus txDust/txDamageMask; `+32 float damage` (one scalar, no zones) **[VERIFIED]**.
  The fleet sweep counted 138 damage materials (both shaders) on 86/86 cars.
- `ksPerPixelMultiMap_damage_dirt_sunspot` exists as a name in acs.exe (no .fxo in system/shaders/win; no owned car checked uses it) [VERIFIED name only].

### a2. Stored values are zero: the game drives them at run time [VERIFIED in install, dmgsweep, 86 cars]
- `damageZones` = A 0, D (0,0,0,0) on **138/138** materials; `dirt` = 0 on 138/138. So the kn5 holds no damage; AC writes
  `damageZones` (float4) and `dirt` per car while driving [INFERRED: names are in acs.exe next to `VisualDamageManager`].
- Texture names vary per car (txNormal: 63x DAMAGE_CHASSIS_NM, 19x DAMAGE_NM, 14x Damage_NM, 9x bodyparts_nm, 2x FLAT_NM ...; txDamage:
  64x damage.dds ...; txDust: 71x dust.dds ...; txDamageMask: 11x Damage_Mask ... 2x NULL.dds as txDamage) -> always resolve by SLOT, never
  by file name. lotus_elise_sc (+s1,s2): mask `damage_mask.dds` NOT embedded in the kn5 (missing -> must fall back to "no zones" = black).

### a3. The mask channels [DOCUMENTED: Pipeline "CAR DAMAGE"] + [VERIFIED in install, dmguv on 5 cars]
Pipeline: "The WHITE part indicates the front of the car (painted in the alpha channel), the RED the left-hand side, the BLUE the right-hand
side, and the GREEN the rear ... Never paint the roof and the top of the bonnet." 512x512 DXT5, blended (soft) transitions.
Sampled at each damage-paint vertex's **raw uv0** (v as stored, DDS row 0 = top, i.e. our existing (u, v_stored) upload rule), mean per
body region (AC model space, +X = car left, +Z = front; front z>1.6, rear z<-1.6, left x>0.75, right x<-0.75, roof y>1.05):

| car | front A | rear G | left R | right B | roof (all) |
|---|---|---|---|---|---|
| ferrari_458 | A 0.90 | G 0.63 | R 0.64 (B 0.07) | B 0.48 (R 0.22) | 0 |
| abarth500 | (no front verts on paint) | G 0.83 | R 0.70 | B 0.70 | R/B 0.14 |
| ks_mazda_miata | A 1.00 | G 0.87 | R 0.89 | B 0.75 | R 0.34 |
| bmw_m3_e30 | A 0.82 | G 0.73 | R 0.90 | B 0.90 | 0 |
| ks_toyota_gt86 | A 0.96 | G 0.87 | R 0.63 | B 0.62 | R/B 0.30 |

=> **mask.R = LEFT (AC +X), mask.G = REAR, mask.B = RIGHT, mask.A = FRONT**, sampled at uv0 unscaled. Fleet coverage of R/B is symmetric
on every car (dmgsweep "mean/cov" column). A few cars use separate masks for plastic / carbon parts with only G and A painted
(alfa_romeo_giulietta_qv `Damage_Plastic_mask`, mercedes_sls_gt3 `damage_mask_carbon`, gt86 `Damage_PLASTIC_mask`, ferrari_312t wing mask).

### a4. The other three textures [DOCUMENTED: Pipeline] + [VERIFIED in install, 458 channel stats]
- **txNormal = DAMAGE normal map** ("metal deformation"), tangent-space. "In the alpha channel you must paint the part of the chassis with
  scratches, which becomes less reflective". 458 `DAMAGE_CHASSIS_NM.dds` DXT3 1024: R mean 128 / G 128 / B 254 (flat except the dents),
  A mean 245 (1 = clean, dark = scratched). The dents sit along the panel edges / bumpers in the same uv layout as the mask.
- **txDamage = SCRATCHES** colour map, 2048 DXT5: "Scratches appear over the car paint texture so the texture needs an alpha channel to use
  as a mask for opacity", grey background. 458 `Damage.dds`: RGB mean ~8, A mean 26 (sparse scratches).
- **txDust = dust/dirt** colour: "shows dirt on the car after driving off-road". 458 `dust.dds` 1024 DXT5: brown RGB (76/68/45), A mean 32
  = where dust collects.
- Damage textures appear in the editor with "show damage" (Pipeline) - there is no Kunos formula.

### a5. Blend model [INFERRED - Kunos' shader body was not read; must be matched against AC screenshots]
Per pixel, with `m = txDamageMask(uv)` and per-zone damage `z` (0..1):
```
w      = saturate(dot(m.rgba, zMaskOrder))          // zMaskOrder = (left, rear, right, front)  - which damageZones component is which
                                                    //   zone is [UNKNOWN]; we own both ends, so pass it in mask order.
n_ts   = normalize(lerp(float3(0,0,1), txNormal.xyz*2-1, w))   // dents only where damaged (undamaged = flat: what AC shows new)
scr    = txDamage(uv)                                // scratches
albedo = lerp(albedo, scr.rgb, scr.a * w)            // scratch colour over the paint
refl  *= lerp(1, txNormal.a, w); spec *= lerp(1, txNormal.a, w)  // scratched area less reflective (Pipeline alpha note)
dust   = txDust(uv); albedo = lerp(albedo, dust.rgb, dust.a * dirt)   // dirt 0..1 (off-road), independent of damage
```
- `[SCRATCHES] MIN_SPEED=0 MAX_SPEED=20` (86/86 cars, identical): scratches ramp in over 0..20 km/h impacts - i.e. the scratch layer is
  driven by a faster-saturating weight than the dents [INFERRED: w_scr = saturate(zone_kmh / 20); dents w = the zone value]. [UNKNOWN]
  whether AC uses this or a single weight.
- `[DAMAGE] INITIAL_LEVEL` (20 on 85 cars, 100 on ks_alfa_33_stradale): meaning [UNKNOWN] (possibly the zone level below which no visual
  damage is drawn). Do not guess in code: read it, log it, leave unused until checked.
- Zone value unit: physics carDamage per zone is in km/h of impact (damage_displayer `MAX_DAMAGE_KMH=60` = full red on the HUD icons;
  the five zones FRONT/REAR/LEFT/RIGHT/CENTER = the five DAMAGE_GLASS sections) [INFERRED; AC shared memory physics page has
  `float carDamage[5]` - from memory of the Kunos "Shared Memory Reference" forum thread, URL not fetched this session].
  A 0..1 paint weight then needs a scale: `z = saturate(zone_kmh / MAX_DAMAGE_KMH)` // ours until verified.

### a6. dirt
`dirt` is a float the game raises off-road; `assetto_corsa.ini [DIRT] MULT=0.7` and surfaces.ini `DIRT_ADDITIVE` (track) feed it
[INFERRED from names]. Tyres: `ksTyres cbTyre {blurLevel, dirtyLevel}` + `txDirty t2` [VERIFIED RDEF]; physics `tyreDirtyLevel` (acs.exe
string). Not damage, but the same plumbing (per-car float on a shared material) - do it in the same pass if wanted.

## (b) Glass: DAMAGE_GLASS_* + ksBrokenGlass

### b1. Model data [VERIFIED in install, dmgsweep]
- 85/86 cars have DAMAGE_GLASS nodes (tatuusfa1: none) and a `ksBrokenGlass` material; 84 cars have all five
  `[DAMAGE_GLASS_FRONT/REAR/LEFT/RIGHT/CENTER] MIN_SPEED FULL_SPEED` (km/h; e.g. 458 FRONT 30/60, REAR 30/60, LEFT 20/30, RIGHT 20/30,
  CENTER 30/80; abarth500 all 20/80; ks_mazda_787b and tatuusfa1 have no glass sections).
- Node naming [DOCUMENTED: Pipeline]: `DAMAGE_GLASS_<ZONE>_<n>` dummies, "the numbers must always be present"; CENTER = windscreen,
  FRONT = headlight glass, REAR = tail-light glass, LEFT/RIGHT = side windows (+mirror glass). A glass on a moving part sits UNDER that part
  (458: `DAMAGE_GLASS_LEFT_2` under `DOOR_L`, `RIGHT_2` under `DOOR_R`; ks_pagani_huayra_bc 15 glass nodes under a panel; "DAMAGE_GLASS_FRONT_2
  ... child of FRONT_BUMPER ... to move along"). Present in LOD A and B (458 LOD_1: 8 glass nodes + the material).
- The mesh is a 0.5 mm offset duplicate of the real glass, TRANSPARENT, no shadows, draw priority -2 [DOCUMENTED: Pipeline].
- Material [VERIFIED]: `DAMAGE_GLASS [ksBrokenGlass] blend 1, depth 0`, txDiffuse `DAMAGE_GLASS_color.dds` (embedded proxy: 16x16 DXT1,
  flat grey 129, alpha 255), txNormal `DAMAGE_GLASS.dds` (embedded PROXY 64x64). `glassDamage` stored **1 on 84 cars, 0 on ktm_xbow_r**.
- The REAL crack map: Pipeline "The Proxy texture is a placeholder ... that the game loads automatically from a common folder"; acs.exe has
  the string `content/texture/DAMAGE_GLASS.dds`; that file is 1024x1024 BGRA32: RG mean 128 (normals), B 230, **A 0..99 (max 0.39) = crack
  visibility** [VERIFIED]. There is no common DAMAGE_GLASS_color (the embedded grey is used).
- Shader [VERIFIED RDEF ksBrokenGlass_ps.fxo]: `txDiffuse t0, txNormal t1, txCube t10`; `cbCarPaint {fresnelC, fresnelEXP, nmObjectSpace,
  isAdditive, fresnelMaxLevel, glassDamage (USED), boh}`.

### b2. When cracks show [INFERRED]
- Glass zone factor `g = saturate((zone_kmh - MIN_SPEED) / (FULL_SPEED - MIN_SPEED))` per DAMAGE_GLASS_<ZONE> section (same MIN/FULL form as
  the VISUAL_OBJECTs; `MAX SPEED %f MIN_SPEED %f` log string).
- **Threshold, not a blend, for visibility**: the nodes of that zone are switched ON when `g >= GLASS_THRESHOLD` (system
  assetto_corsa.ini 0.65). Reason: one ksBrokenGlass material is shared by all zones of a car, so per-zone control must be node visibility.
- **ktm_xbow_r is the proof that AC hides by NAME, not by the kn5 active flag**: its six DAMAGE_GLASS_* dummies are `active=1` (all other
  cars: inactive) and its material has glassDamage 0. So AC (a) hides every DAMAGE_GLASS_* at load and (b) writes `glassDamage` itself
  when showing them (otherwise ktm would never show cracks, or always show them). Use: `glassDamage = 1` while a zone is cracked, alpha =
  `txNormal(common).a * glassDamage` [INFERRED shader body].
- **Current bug in OUR build [INFERRED from data, not lab-checked]**: AcKn5Builder hides only `!node.Active`, so ktm_xbow_r shows its
  DAMAGE_GLASS meshes all the time, drawn by our PerPixel as blended txDiffuse (grey 129, alpha 1 x alpha) = an opaque grey film over the
  windscreen, headlights and side windows. Fix independent of damage: hide `DAMAGE_GLASS*` nodes by name in AcCarModel.Finish.
- Glass never un-breaks until repair (pit stop BODY repair) [INFERRED].

## (c) Panels: [VISUAL_OBJECT_n]

### c1. Data [VERIFIED in install, dmgsweep]
- 260 sections on 86 cars; every one has all 11 keys `NAME STATIC_ROTATION_AXIS STATIC_ROTATION_ANGLE MULT_G DAMAGE_ZONE MIN_SPEED FULL_SPEED
  OSCILLATION_AXIS OSCILLATION_MIN_ANGLE OSCILLATION_MAX_ANGLE ALLOWED_G`. Kunos' own comments: "OSCILLATION_MIN/MAX_ANGLE - never less than 0
  and never more than 5", "MULT_G EFFECT OF THE G FORCES ON THE OSCILLATION", "ALLOWED_G SET WHICH G AXIS HAVE EFFECT ON DAMAGE ANIMATION and
  how much (0 is off, 1 is 100%)", "MIN_SPEED MINIMUM DAMAGE SPEED / FULL_SPEED MAXIMUM DAMAGE SPEED".
- `DAMAGE_ZONE` is only FRONT or REAR on panels. Names: 67 FRONT_BUMPER, 58 MOTORHOOD(front) + 8 MOTORHOOD(rear, mid-engine), 53 REAR_BUMPER,
  13 WING, 8 REAR_WING, 6 REARHOOD, 5 REARE_BUMPER (sic), ... (Pipeline list: FRONT_BUMPER REAR_BUMPER MOTORHOOD REAR_HOOD FRONT_WING
  REAR_WING REAR_EXTRACTOR).
- **246/260 names exist in LOD_0, 229 in LOD_1**. 14 do not (copy-paste inis: ruf_yellowbird / ks_audi_a1s1 / ks_audi_sport_quattro_rally
  use abarth's names, ks_maserati_quattroporte `NAME=0`, mercedes_sls_gt3 `REAR_HOODc`, pagani_zonda_r `REARHOOD`, pagani_huayra
  `FRONT_WING`, ks_alfa_mito_qv `WING`, bmw_z4_s1 / ktm_xbow_r / ks_lamborghini_miura_sv `MOTORHOOD`) -> skip silently with one log line.
- `[OSCILLATIONS] ENABLED=0` on ferrari_f40(+s3), ktm_xbow_r, lotus_49, ks_maserati_levante (all others 1).
- Values (458): MOTORHOOD axis (1,0,0) 3.71 deg, FRONT 20..150 km/h, osc (1,0,0) 0..6, MULT_G 0.1, ALLOWED_G (0,1,0);
  FRONT_BUMPER (-1,0,0) 3.71, 30..220; REAR_BUMPER (-1,0,0) 5.71, REAR 30..150, MULT_G 0.01. Axes are not unit length (abarth (-1,0,1),
  (-1,0,0.5); quattroporte (1,1,1)) -> normalize.

### c2. Pivot = node origin [DOCUMENTED: Pipeline] + [VERIFIED in install, dmguv]
Pipeline: "the following nulls must be placed in the PIVOT point of the object around which the object rotates upon impact ... For the
MOTORHOOD it can be the hinges". Measured (pivot world, mesh bounds relative to it, AC model space, m):
- 458 MOTORHOOD pivot (0, 0.778, 0.879), mesh z -0.008..1.263, y -0.274..0.017 -> pivot at the rear (hinge) edge, top of the hood.
- 458 FRONT_BUMPER pivot (0, 0.511, 2.15), mesh z -0.893..0.114 -> pivot at the nose; REAR_BUMPER (0, 0.842, -2.172), mesh y -0.714..0 ->
  pivot at the top-rear edge. Same pattern on abarth500, miata, e30, gt86.
=> rotate the node's own transform (its children follow) about an axis through its origin. No pivot offset needed.

### c3. Static bend [INFERRED]
`f = saturate((zone_kmh - MIN_SPEED) / (FULL_SPEED - MIN_SPEED))`, bend angle = `f * STATIC_ROTATION_ANGLE` about the normalized
`STATIC_ROTATION_AXIS` (car / model space; the pivot dummies have the root's orientation in the checked cars). Signs differ between cars for
the same part (458 hood +X 3.71 = front edge DOWN under the formula below; miata/e30/gt86 hood -X = front edge UP; e30 FRONT_BUMPER angle
-3.5 vs 458 +3.71), so the direction is authored per car: just apply it.
Rotation convention [INFERRED, D3DX-style RotationAxis on AC's right-handed X-left/Y-up/Z-forward frame]: +angle about +X moves a point at
+Z downward. In Unity after the X mirror: **`node.localRotation = rest * Quaternion.AngleAxis(-f * angle, new Vector3(-ax, ay, az).normalized)`**
(same sign rule as the steering wheel, ac_car_formats C3). Check one car in the lab against an AC screenshot of a front crash before trusting
the sign.

### c4. Oscillation (loose panel) [INFERRED - no formula published]
Only once the panel is damaged (f > 0). Drive a damped angular spring per panel by the chassis acceleration in g (car frame, AC axes:
x lateral, y vertical, z longitudinal), weighted per axis by ALLOWED_G and scaled by MULT_G:
```
exc   = MULT_G * dot(accel_g, ALLOWED_G)          // ALLOWED_G (0,1,0) = vertical bumps only (most hoods)
theta = spring(exc) // ours: frequency ~6 Hz, damping 0.3 (UNKNOWN in AC)
theta = clamp(|theta|, OSC_MIN, OSC_MAX * f) * sign  (deg)  about OSCILLATION_AXIS, added to the static bend
```
Skip when `[OSCILLATIONS] ENABLED=0`. The spring constants are ours and must be marked `// ours`.

## (d) Anything else
- **No detached parts** in vanilla AC: Pipeline only describes rotate/vibrate; no detach string in acs.exe [VERIFIED strings / DOCUMENTED].
- **No flat-tyre mesh / shader**: `content/texture/damage/tyre_xx.png`, `flatspot_xx.png`, `sus_xx.png` etc. are the HUD damage-displayer
  icons (damage_displayer.ini TIME=4 s, MAX_DAMAGE_KMH 60, POSITION_X 20, DISTANCE_FROM_CENTER_Y -200) [VERIFIED files + strings]. Puncture is
  sound only (`flat_tyre_mono`, `tyre_explosion`, ac_physics_sound_rules 2.6). Tyre `dirtyLevel` (ksTyres) is dirt, not damage.
- **Pit repair** resets all of the above (`[PIT_STOP] BODY_REPAIR_TIME_SEC` per 10 %, ac_physics_sound_rules) [INFERRED for visuals].
- Server rule `DAMAGE_MULTIPLIER` (0 = no damage) gates everything (lobby already has "damage").

## (e) Mapping onto OUR code

Current state (read): `AcShading.Of` (Game/World/AcTrackJob.cs) gives damage_dirt NormalMap=true, Maps, Detail; it never lists
`txDamage / txDust / txDamageMask` in `Slots` (so the worker never reads them). `AcMaterials.Create` binds `_NormalTex` = the DAMAGE NM;
`AcCarShading.FlattenDamageNormals` then sets `_Flags.y = 0` and `_NormalTex = FlatNormal` on the scope's SHARED materials (shared by every
instance of that car model, incl. AI / remote cars and all LODs). ksBrokenGlass falls into the generic PerPixel path (blend 1).
AcKn5Builder hides nodes by `active` only. No damage.ini reader.

### e1. Shader (W owns Shaders/) - SomniumAssetto/PerPixel, no new keywords (house rule: float switches only)
- New textures: `_DamageTex ("txDamage")`, `_DustTex ("txDust")`, `_DamageMaskTex ("txDamageMask")` (defaults black / black / black).
  Keep `_NormalTex` = the real damage NM (stop flattening; the weight below makes it flat when undamaged).
- New UnityPerMaterial vectors (keep the CBUFFER layout identical in every pass for the SRP Batcher):
  `_DamageZones` (left, rear, right, front in MASK order, 0..1 dents), `_DamageSet` (x = 1 damage paint, y = dirt 0..1, z = scratch weight
  scale or 2nd zone vector packed, w = 1 BrokenGlass mode), `_GlassSet` (x = glassDamage).
- Frag, only `if (_DamageSet.x > 0.5)` (uniform branch, free when off):
  3 extra samples (mask, txDamage, txDust); normal: `nt = lerp(float3(0,0,1), nt, w)` where the NM sample already exists (move it out of
  the `_Flags.y` gate for damage materials); `maps.b *= lerp(1, nmA, w); spec *= lerp(1, nmA, w)`; albedo scratches and dust lerps (a5).
  Cost: +3 texture fetches and ~15 ALU on car-paint pixels only; zero for every other material.
- BrokenGlass mode (`_DamageSet.w`): normal from `_NormalTex` (the COMMON crack map), alpha = `_NormalTex.a * _GlassSet.x`,
  colour = txDiffuse lighting + the existing fresnel/env path. Depth passes stay disabled (blend).
- DepthNormals pass: unchanged (geometric normal) - acceptable.

### e2. Materials (W: AcShading / AcMaterials)
- `AcShading`: add `bool DamagePaint` (shader == ksPerPixelMultiMap_damage_dirt or _damage) -> `Slots.Add("txDamage","txDust","txDamageMask")`
  when present; `bool BrokenGlass` (shader == ksBrokenGlass). Only when the session allows damage (rules DAMAGE_MULTIPLIER > 0 / setting) -
  the 458 adds Damage.dds 4 MB + dust 1 MB + mask 1 MB = **~6 MB GPU per car model** (shared by all instances; LOD_1..3 share the scope).
- `AcMaterials.Create`: bind the three textures (missing -> AcTextures.Black: lotus_elise_sc mask is not embedded, 2 cars use NULL.dds),
  set `_DamageSet.x = 1`, `_DamageZones = 0`, `_DamageSet.y = 0`. For BrokenGlass: `_NormalTex` = the install's
  `content/texture/DAMAGE_GLASS.dds` (read at run time like any install file, uploaded LINEAR as all kn5 textures; one shared Texture2D for
  all cars, ~4 MB), `_GlassSet.x = 0`.
- `ksPerPixelMultiMap_damage` (scalar `damage`): same path with mask = white and the scalar in all four zones.

### e3. Per-car values: material INSTANCES, not MaterialPropertyBlocks
- The scope's materials are shared by all cars of the same model, so a damaged car needs its own values. Options:
  (1) MaterialPropertyBlock per paint renderer (AcMaterials already uses MPBs for emissive): simple, but every renderer with an MPB drops
  out of the URP SRP Batcher and the lights code already writes MPBs on some of the same renderers (GetPropertyBlock/merge needed).
  (2) **Recommended: clone the damage-paint and broken-glass materials lazily, the first time THIS car takes damage** (AcCarModel owns the
  clones, swaps `sharedMaterials` on its renderers of every LOD set, destroys clones on Dispose / repair). Undamaged cars cost nothing;
  damaged cars keep the SRP Batcher; typical 2-4 paint materials + 1 glass material per car. Write the vectors only when a zone value
  changes (impacts are rare): **0 per-frame cost** except oscillating panels.
- LOD switch (AcCarModel.Tick -> ApplyAll): re-apply glass node visibility and panel rotations to the newly shown set (as lights/doors are).

### e4. Nodes (C: AcCarModel / AcCarParts / AcCarData)
- AcCarData: read `damage.ini` ([SCRATCHES], [DAMAGE] INITIAL_LEVEL, [OSCILLATIONS] ENABLED, [DAMAGE_GLASS_*] MIN/FULL_SPEED,
  [VISUAL_OBJECT_n] all 11 keys) alongside lights.ini etc.
- AcCarPartSet.Bind: collect per set (a) all nodes whose name starts with `DAMAGE_GLASS_` grouped by zone token (FRONT/REAR/LEFT/RIGHT/CENTER),
  **SetActive(false) at bind regardless of the kn5 flag** (fixes ktm_xbow_r today); (b) VISUAL_OBJECT transforms by NAME with their rest
  localRotation (missing -> skip + one log line).
- New `AcCarModel.SetDamage(float front, rear, left, right, center /* km/h */, float dirt)`: computes glass g per zone -> SetActive(g >= 0.65)
  + glassDamage 1; panel f -> static bend; paint zones -> material instance vectors. `RepairDamage()` resets all. No allocation after the
  first damage.
- Oscillation: AcCarModel.Tick for panels with f > 0 and ENABLED=1: one spring per panel (a few float ops), acceleration from the rig
  (local car) or from the interpolated remote/AI pose (finite difference). Cost ~ microseconds.
- Net: the zone values (5 floats, quantize to bytes) must travel with the remote car state so others see the same dents / glass; AI cars
  get them from the master's sim.

### e5. Order of work / checks
1. (no damage needed) hide DAMAGE_GLASS_* by name -> lab ktm_xbow_r windscreen picture before/after.
2. Shader + slots + instances; lab: 458 with forced zones (front 1, others 0) - dents/scratches only on the nose/bonnet, nothing on roof;
   left 1 -> only the car's LEFT side (AC +X; Unity -x after the mirror!). Compare with an AC replay screenshot after a front hit.
3. Panels + glass with forced km/h; check bend direction on 458 and miata (opposite authored signs) vs AC.

## Open / uncertain (must be checked in the game)
- damageZones component order inside AC and the exact blend (a5) - our shader owns both ends, so only the look must match.
- Dent weight vs zone km/h scaling (MAX_DAMAGE_KMH 60? INITIAL_LEVEL 20?) and the SCRATCHES 0..20 km/h ramp.
- Glass: threshold 0.65 on the 0..1 glass factor vs on the raw value; glassDamage blend vs on/off.
- Panel rotation sign (c3) and the oscillation model/spring constants (c4).
- Whether AC scales the bend continuously with damage or snaps (MIN/FULL_SPEED suggests continuous).

## Critic addendum (2026-11-02, critic pass; original text above kept unchanged)

Re-checked with the harness (`dmgrdef`, `dmgstr`, `dmgsweep` re-run on all 86 cars, `acd ferrari_458 damage.ini`), a new critic probe
`DevTools/harness-ac/ProbeDmgVisCrit.cs` (`dmgvcrit [car]`: glass nodes by name / ancestor / material / active flag over EVERY LOD;
`dmgvshaders`: damage-shader census), pdftotext of the Pipeline PDF, the project code, and CM source on GitHub.

### Wrong or overstated
1. **HUD icons: 19 files, not 21** [VERIFIED: `content/texture/damage/` = center, engine, gearbox, front, rear, left, right + 4x sus_, 4x tyre_,
   4x flatspot_]. And `content/texture/damage/center.png` is **not** an acs.exe string (the exe lists engine/front/gearbox/left/rear/right +
   the 12 per-wheel icons only) - section 0 is wrong to list `center` among the exe strings.
2. **"138 damage materials (both shaders)"**: all 138 are `ksPerPixelMultiMap_damage_dirt` [VERIFIED dmgvshaders, LOD0, 86 cars]. **No owned
   car uses `ksPerPixelMultiMap_damage` or `_damage_dirt_sunspot`.** The e2 bullet for the scalar `damage` path is untestable here - keep it
   at most as a fallback for mod cars, do not spend lab time on it.
3. **"ktm_xbow_r ... active=1 (all other cars: inactive)" is true for LOD0 only.** `lotus_exos_125` and `lotus_exos_125_s1` **LOD1** have
   active DAMAGE_GLASS meshes too [VERIFIED dmgvcrit] - the same grey-film bug when the LOD switches. (ktm: the active nodes are the six
   DAMAGE_GLASS_* nodes; their meshes are children named `polymsh_extracted*` / `front_light_glass1`.)
4. **"Hide `DAMAGE_GLASS*` nodes by name" (b2 fix, e4, e5 step 1) is UNSAFE as written:**
   - `ks_ford_mustang_2015` **LOD1** has a top-level node `DAMAGE_GLASS_Vtr_Ant1` (no zone token) whose material is plain
     `ksPerPixelMultiMap` and which is ACTIVE - a real window mislabelled by the modeller [VERIFIED]. A name-prefix hide removes a real
     window at LOD1. (What AC itself does with it is [UNKNOWN]: it would hide it only if AC also hides by prefix.)
   - `bmw_m3_e92`, `_drift`, `_s1` **LOD0**: mesh `R_DOOR_mesh_SUB3` uses the `ksBrokenGlass` material but has NO DAMAGE_GLASS ancestor and
     is ACTIVE [VERIFIED] -> a name-based hide misses it; our current build draws it as the grey blended film (same as ktm) [INFERRED, not lab-checked].
   - Robust rule: (a) a node belongs to zone Z if it OR an ancestor is named `DAMAGE_GLASS_<Z>_...` (exact token FRONT|REAR|LEFT|RIGHT|CENTER
     right after `DAMAGE_GLASS_`); (b) independently, every mesh whose material is `ksBrokenGlass` is drawn with alpha x `glassDamage`, our
     per-instance value starting at 0, so stray broken-glass meshes are invisible until damaged, regardless of node names / flags.
     Nodes named DAMAGE_GLASS_ without a zone token and without ksBrokenGlass: leave alone, log once.
5. **e4 "grouped by zone token" from the node's own name breaks on child meshes**: children are named e.g. `DAMAGE_GLASS_Vtr_Left_3`,
   `DAMAGE_GLASS_Vtr_Stop1`, `DAMAGE_GLASS_CENTER_MESH` (no number), `polymsh_extracted` [VERIFIED mustang / r8_plus / ktm]. Group by the
   TOP-MOST DAMAGE_GLASS ancestor, not by the leaf name. (The dmgsweep "inactive N of M" counts with M > N on r8_plus, quattro_rally,
   corvette, mustang, huracan, sesto, alfieri, huayra_bc are those mesh children under an inactive dummy - not visible glass.)
6. **"MAX SPEED %f MIN_SPEED %f" as evidence for the glass MIN/FULL formula is weak**: it sits next to `MAX SPEED %f TURBOS %llu` in the
   exe (a car-loading / engine log). b2's linear `g` stays [INFERRED] with no string support.
7. **"There is no common DAMAGE_GLASS_color"** is [INFERRED] (absence of an exe string + absence in `content/texture/`), not [VERIFIED].
   The Pipeline says the game loads the proxy textures "automatically from a common folder" (plural reading possible); the SDK
   `sdk/dev/car_pipeline_2.0rev/Common Texture/` holds both DAMAGE_GLASS.dds and DAMAGE_GLASS_color.dds, but the runtime only has the NM.
8. **a5 "carDamage[5] from memory, URL not fetched"** -> now [DOCUMENTED: https://github.com/gro-ove/actools/blob/master/AcManager.Tools/SharedMemory/AcSharedPhysics.cs]
   (`[MarshalAs(ByValArray, SizeConst = 5)] public float[] CarDamage;`). The UNIT (km/h) and the order of the 5 entries are NOT documented
   there - still [INFERRED].

### Missed
- **Collision sparks**: `system/cfg/sparks.ini` (particle settings: COLOR_BASE 290,160,80, EMISSIVE_BLEND 0.1, VELOCITY_RANDOM 6,14,7,
  GRAVITY 9.8 ...) [VERIFIED file]; plus `tyre_pieces_grass.ini`. A visible contact effect "like the game"; trigger (body-ground /
  car-car scrape) [INFERRED]. Section (d) should list it.
- **acs.exe strings not mentioned**: `Sending ACP_DAMAGE_UPDATE` (AC's own multiplayer sends a damage-update packet - supports e4's
  "zones must travel with the remote car"), `VISUAL_DAMAGE`, `MAX_DAMAGE`, `debugDamage`, `debugDirt`, `TRACK_DIRT_MANAGER`, `DIRT_ADDITIVE`
  [VERIFIED names only; meanings UNKNOWN]. (`RPM_DAMAGE_K` / `TURBO_DAMAGE_K` are mechanical - other topic.)
- **ksTyres** also binds `txBlur t3`, `txNormalBlur t4` (cbTyre: blurLevel +0, dirtyLevel +4, fresnelC, fresnelEXP, isAdditive,
  fresnelMaxLevel) [VERIFIED RDEF] - a6 listed only txDirty.
- **OSCILLATION_MAX_ANGLE exceeds Kunos' own "never more than 5" comment** (458 MOTORHOOD and REAR_BUMPER = 6) [VERIFIED acd] -> do NOT clamp
  to 5 in code; use the file value.
- Mask format in the fleet is not the Pipeline's "512 DXT5": 128x128..512x512, also 256x128 (lotus_49), DXT3/DXT5/BGRA32 (458 = BGRA32)
  [VERIFIED dmgsweep] - the reader must accept any DDS format/size.

### Holds (re-verified)
- RDEF of damage_dirt / BrokenGlass / damage / Tyres runtime .fxo: resource slots (txDamageMask t21), cbCarPaint layouts and USED flags,
  BrokenGlass `glassDamage +20 USED`, VS input POSITION/NORMAL/TEXCOORD0/TANGENT (one uv set).
- `assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD=0.65`, `[DIRT] MULT=0.7`; `damage_displayer.ini` TIME=4, MAX_DAMAGE_KMH=60;
  `content/texture/DAMAGE_GLASS.dds` 4,194,432 B = 1024x1024x4 + 128 header (BGRA32, no mips).
- Sweep numbers: 86 cars, 85 with glass nodes + ksBrokenGlass (tatuusfa1 none), 84 with all five glass sections (787b + tatuusfa1 none),
  SCRATCHES 0..20 on 86/86, INITIAL_LEVEL 20 except ks_alfa_33_stradale 100, OSCILLATIONS ENABLED=0 on f40, f40_s3, ktm_xbow_r, lotus_49,
  ks_maserati_levante; 260 VISUAL_OBJECTs with all 11 keys, found 246 LOD0 / 229 LOD1, DAMAGE_ZONE only FRONT/REAR; damageZones / dirt
  stored 0 on 138/138; glassDamage 1 on 84, 0 on ktm; lotus_elise_sc(+s1,s2) mask not embedded; 458 VISUAL_OBJECT values in c1.
- Pipeline quotes (mask colours/alpha, "never paint the roof", 0.5 mm offset, transparent/no shadow, priority -2 on the DUMMY, nulls at
  the pivot / hinges, glass in LOD A and B, DAMAGE_GLASS_FRONT_2 under FRONT_BUMPER).
- CM: `Kn5MaterialDarkMaps.cs` ~l.26 drops txNormal for "damage" shaders; `MaterialsProviderDark.cs` returns InvisibleMaterial for
  ksBrokenGlass and lists ksPerPixelMultiMap_damage / _damage_dirt / _damage_dirt_sunspot [DOCUMENTED, GitHub].
- Code refs: `AcShading.Of` (Game/World/AcTrackJob.cs:28) never adds txDamage/txDust/txDamageMask; `AcCarShading.FlattenDamageNormals`
  (Game/Cars/AcCarShading.cs:63, called AcCarModel.cs:358 and :876) only matches `_damage_dirt` and edits the shared materials;
  `AcKn5Builder.cs:289` hides only `!node.Active`; MPBs exist in AcMaterials.cs:122-159 (and AcCarDriverModel). Note: AcCarJob only uses
  the inactive flag for bounds, so DAMAGE_GLASS meshes ARE built (inactive GameObjects) - toggling SetActive later is enough.
