# Assetto Corsa TRACK data formats (verified against the real install)

Research spec for the Assetto Corsa Dreamport. Install (read-only): `E:\SteamLibrary\steamapps\common\assettocorsa`.
Probe (dotnet 10 console, C#): `C:\Users\derii\AppData\Local\Temp\claude\acscratch\tracks\probe\` (`Kn5.cs`, `Ai.cs`, `Layouts.cs`,
`Verify.cs`, `Summary.cs`, `TexAll.cs`, `Winding.cs`, `Png.cs`). Run: `bin\Release\net10.0\TrackProbe.exe <mode> ...`, modes `layouts`,
`summary`, `track <id> [layout]`, `verify <id> [layout]`, `ai <file>`, `aiall`, `kn5 <files>`, `kn5time <file>`, `tex <kn5>`, `texall`,
`nodes <kn5> [filter]`, `wind <kn5> <nameFilter|*>`, `bmp <ai> <map.ini> <out.png>`.

Tags: **[VERIFIED]** = measured by running the probe against the real files (or reading the engine's strings in `acs.exe`).
**[BELIEF]** = inferred / community knowledge, not proven here. Everything not tagged in a table row is VERIFIED.

Read together with `ac_car_formats.md` (same folder): the KN5 container, material layout, DDS census and the AC->Unity conversion
(negate X, swap triangle indices 1<->2, `U = S*transpose(M)*S`) are already specified there and **agree with the track findings below**
(this spec proves the same handedness independently from the world map, section 4.3). Use ONE shared KN5 reader for cars and tracks.

---

## 0. Key facts (TL;DR)

1. 44 layouts are listed under `content/tracks` (21 folders). **35 layouts are playable, 9 are DLC stubs** (folder has only `ui/<layout>/dlc_ui_track.json`,
   `dlc_preview.png`, `outline.png`, no kn5): `ks_barcelona` (2), `ks_brands_hatch` (2), `ks_nordschleife` (3), `ks_red_bull_ring` (2).
   Detect: no `ui_track.json` (only `dlc_ui_track.json`) OR a kn5 of the model list missing.
2. A layout = `models[_<layout>].ini` (list of kn5 files) + `[<layout>/]data/*.ini` + `[<layout>/]ai/*.ai` + `ui/[<layout>/]ui_track.json`.
   Several layouts of one track share the big kn5 and differ by 1 small kn5 (e.g. vallelunga 2.kn5/3.kn5/4.kn5).
3. KN5 track files are v6 (117 files; v5 1 file `objects3D/cubemap_model.kn5`; v4 1 file `axis.kn5` fails = old material format).
   Monza: 7 kn5, 427 MB, 1,428 meshes, 4.62 M vertices, 3.36 M triangles, 139 textures / 213 MB. Header-only parse (seek past
   textures+vertices) = 22-600 ms; full parse with all vertex+texture bytes into managed arrays = ~0.2-0.5 s per 300-400 MB file
   on this machine (warm OS cache) and ~450 MB managed heap for monza.kn5 alone.
4. Special nodes are **dummies (class 1) with a child cube mesh of the same name** (24 verts, renderable - must be hidden):
   `AC_START_n` (grid), `AC_PIT_n` (pit boxes; count == ui `pitboxes` on every installed track), `AC_TIME_n_L/R` (timing gates; gate 0 =
   start/finish, gates 1.. = sector splits), `AC_HOTLAP_START_0`, `AC_TIME_ATTACK_0`, `AC_AUDIO_*`, `AC_CREW_*`, `AC_POBJECT*` (movable
   props), `AC_SEMAPHORE_*`, `AC_OPEN_FINISH_L/R` (drag finish), `AC_AB_START_L/R` + `AC_AB_FINISH_L/R` (A-to-B hillclimb).
   Car forward = node **+Z**, car left = node **+X** (AC space) [VERIFIED dot(nodeZ, AI forward)=1.000 on 6 tracks].
5. Physics meshes: name = `<digits><SURFACEKEY><anything>` (e.g. `01CURB004`, `13ROAD78765`, `17PITS-MNZ01`, `10TARMACA5666`) matched
   by **longest surface KEY prefix** from `data/surfaces.ini` + `system/data/surfaces.ini` (ROAD, GRASS, KERB, SAND). `<digits>WALL...` =
   collision walls (engine string `WALL` exists). All 35 layouts: every digit-prefixed mesh matches a key or WALL except 1
   (`06RDGRAVEL008` in laguna seca: contains `GRAVEL`). Physics meshes may be physics-only (`isRenderable=0`, e.g. monza 2.kn5 is
   the whole physics file) or also visible (magione: 79 of 212 render).
6. `ai/fast_lane.ai` v7: 16 B header, 20 B per point (pos3, cumulative length, id), then `int count` + 18 floats ("payload") per point,
   then an optional nearest-point grid. **Normalized lap progress = Length[i]/TotalLength**: sections.ini (and, same 0..1 scale [BELIEF], drs_zones.ini, starting_bounds.ini)
   uses exactly this (Monza "Prima Variante" 0.151-0.178 has the min radius 18 m at 0.163) [VERIFIED].
   Fast lane point 0 lies on the AC_TIME_0 gate (progress 0.0003 monza, 0.0011 magione, 0.0020 vallelunga club) [VERIFIED].
7. AC world is right-handed, +Y up; drawn with x right / z down it is the real map (north up). `map.png` pixel =
   `((x + X_OFFSET)/SCALE_FACTOR, (z + Z_OFFSET)/SCALE_FACTOR)`, image size = `WIDTH x HEIGHT` of map.ini [VERIFIED by rendering
   the AI line over Monza map.png: identical].

---

## 1. Folder layouts

### 1.1 Single-layout track (e.g. `monza`, `magione`, `imola`, `spa`, `mugello`, `ks_zandvoort`, `ks_laguna_seca`, `ks_silverstone1967`, `drift`, `trento-bondone`)
```
<track>/
  <track>.kn5  [1.kn5 2.kn5 ... 100.kn5]        all kn5 in the track folder root
  models.ini                                     list of kn5 to load (MISSING in trento-bondone -> load <track>.kn5 alone)
  map.png                                        minimap (RGBA8), sized WIDTH x HEIGHT of data/map.ini
  ai/fast_lane.ai  ai/pit_lane.ai
  data/surfaces.ini cameras*.ini ... (section 6)
  ui/ui_track.json  ui/preview.png  ui/outline.png  (+ extras: trento-bondone.png, bgr00..02.jpg, outline_1.png)
  skins/                                         track skins (ignored)
```
### 1.2 Multi-layout track (e.g. `ks_vallelunga`, `ks_silverstone`, `ks_nurburgring`, `ks_highlands`, `ks_monza66`, `ks_black_cat_county`, `ks_drag`)
```
<track>/
  <track>.kn5  1.kn5 2.kn5 ...                   shared pool of kn5 files (root)
  models_<layout>.ini                            one per layout (no plain models.ini)
  <layout>/ai/fast_lane.ai  <layout>/ai/pit_lane.ai
  <layout>/data/*.ini   <layout>/map.png
  ui/<layout>/ui_track.json  preview.png  outline.png
```
Layout ids are the `ui/<layout>` subfolder names (= `<layout>/` data folder = `models_<layout>.ini` suffix), e.g. `ks_vallelunga`:
`classic_circuit`, `club_circuit`, `extended_circuit`. Engine message (acs.exe): `ERROR, models_[config].ini file is required and not found in:`.

**Rule (implemented in `Layouts.cs` `AcTracks.Scan`, verified on all 21 folders):**
- `ui/ui_track.json` or `ui/dlc_ui_track.json` exists -> single layout, LayoutId "".
- else each `ui/<sub>/` containing `ui_track.json` or `dlc_ui_track.json` is a layout.
- models file: `models.ini` (single) / `models_<layout>.ini`; if missing -> `[<track>.kn5]`.
- Only `[MODEL_n]` sections are kn5 to load. `[DYNAMIC_OBJECT_n]` sections (drift, spa, ks_black_cat_county: random flying objects
  `FILE=11.kn5/12.kn5`, `PROBABILITY=75`, `POS_MODE=RANDOM`, `RND_POS_CENTER`, `RND_POS_RANGE`, `VEL_MODE`, `RND_VEL_*`) reference files that
  do NOT exist for drift/spa -> skip DYNAMIC_OBJECT (or load only if present).
- `ks_highlands/models_layout_drift.ini` lists `9.kn5` twice -> de-duplicate file names (load once).
- Available = not a DLC stub AND all MODEL kn5 exist AND `data/surfaces.ini` exists.

### 1.3 models.ini
```
[MODEL_0]
FILE=2.kn5
POSITION=0,0,0
ROTATION=0,0,0
```
- Order of MODEL_n is the load order (monza: 2, monza, 1, 3, 4, 5, 100). All files share ONE world space; combine = instantiate every kn5's
  node tree under one root with the given transform. Markers (AC_START etc.) can be in ANY of the files (monza: monza.kn5; highlands:
  9.kn5; black_cat_county: 1.kn5; drag: start in ks_drag.kn5, finish in 200/400/...kn5) -> scan all files of the layout.
- Non-zero transforms exist only for `100.kn5` (start-light gantry): black_cat_county `POSITION=-280.4,9.4,287.52` (layout_int/long) and
  `-298,9.4,287.35`, ks_highlands `POSITION=705.2,9.7,579.13 ROTATION=-17,0,0`, ks_nurburgring `POSITION=-8.5,68.9,-696.7 ROTATION=180,0,0`.
- ROTATION: first component = yaw about Y in **degrees** [VERIFIED-ish from 2 tracks: the same gantry model faces grid heading ~177 deg at
  rotation 0 (black cat), grid heading 196 deg needs rotation -17 (highlands), grid heading 0 deg needs 180 (nurburgring)]. With the X
  mirror (section 4) the Unity rotation is `Quaternion.Euler(0, ROTATION[0], 0)` and the position `(-x, y, z)`. Order/meaning of the 2nd/3rd
  components unknown (always 0) [BELIEF pitch, roll].

### 1.4 What each kn5 typically holds (monza, by node/material names) [VERIFIED]
| file | content |
|---|---|
| monza.kn5 (316.6 MB) | `FBX: ks_monza.FBX` visual world: 874 meshes, 114 textures, all AC_ markers, AC_CREW props |
| 2.kn5 | `FBX: physics.FBX`: 328 meshes = 240 surface meshes + 88 WALL, all `isRenderable=0`, material `physics/ksPerPixel` |
| 1.kn5 (89 MB) | `FBX: grass.FBX`: 3D grass/bushes, 1.92 M verts (ksGrass `BBgrass*`, layer 5, LOD 0-150) - optional for VR perf |
| 3.kn5 / 4.kn5 | paddock props (gazebos, tractors, TV) / `jersey.FBX` barriers (HI 0-180 + LOW 170-350 LOD pairs) |
| 5.kn5 | `drs.FBX`: AC_POBJECT_033..036 (DRS boards) |
| 100.kn5 | `startlights.FBX`: gantry + `KS_START_LIGHT_0..4` (lights driven by data/startinglights.ini) |
Magione: magione.kn5 (visual + physics in one file) + 100.kn5 (`KS_START_LIGHT_0..3`).

---

## 2. ui_track.json (and dlc_ui_track.json)

Fields seen (all values are STRINGS, even numbers): `name`, `description`, `tags` (string[]), `geotags` (string[2]), `country`, `city`,
`length`, `width`, `pitboxes`, `run`; DLC stubs add `url`. No `year`/`author` in this install.

Quirks [VERIFIED on all 44 files]:
- Tabs, `"tags" :` with space before colon, leading space before `{` (ks_drag/*), no BOM.
- `ks_nordschleife/ui/*/dlc_ui_track.json` is NOT valid UTF-8: the degree sign is a single Latin-1 byte 0xB0 (`"50.3356� N"`).
  -> decode UTF-8 strictly, on failure decode as Latin-1/cp1252.
- `geotags` is the placeholder `["lat","lon"]` on every non-DLC track.
- `length`: metres as string ("5793"), but `ks_laguna_seca` has `"3.602"` (km!) and DLC stubs `"4655m"`. Parse: strip non `[0-9.]`, if
  value < 100 treat as km x1000. Fast-lane length is the better number (monza ui 5793 vs fast_lane 5757.2 m).
- `width` "12", "10m-15m". `pitboxes` "26". `run` values: `clockwise`, `anti-clockwise`, `DRIFT`, `dragstrip`.
- `tags` include `#drift`, `drift`, `circuit`, `original`, `historic`, `A2B`, `Hillclimb`, `touge`, `drag`, country.
- System.Text.Json parsed all 44 (it replaces invalid UTF-8); Unity's JsonUtility cannot (string[] arrays in a typed class are OK but
  the Latin-1 bytes and loose formats are risky) -> use the tiny lenient parser in section 9.3.

Images: `ui/[<layout>/]preview.png` (DLC: `dlc_preview.png`), `outline.png` (track outline, transparent), `<layout or root>/map.png`
(RGBA8, the minimap; see 6.2). Silverstone also has `outline (Stefano Casillo's conflicted copy 2020-01-17).png` - ignore files with spaces.

### 2.1 Installed layouts [VERIFIED by `layouts` + `summary`]
| layout | ui name | len (ui) | pitboxes | run | kn5 files (MODEL order) | AC_START/AC_PIT | TIME gates | special markers |
|---|---|---|---|---|---|---|---|---|
| drift | Drift | 905 | 18 | DRIFT | drift | 17/18 | 2 | fast_lane.ai EMPTY (v3, 0 pts) |
| imola | Imola | 4909 | 24 | anti-clockwise | imola,1,2,100 | 24/24 | 3 | |
| ks_black_cat_county/layout_int, _long, _short | Black Cat County (- Long/- Short) | 6478/11244/6542 | 24 | anti-clockwise | 5,ks_black_cat_county,4,{1/2/3},100 | 24/24 | 3 | |
| ks_drag/drag200,400,500,1000,2000 | Drag 200m.. | 200..2000 | 2 | dragstrip | 1,ks_drag,{200..2000} | 2/2 | 0 | AC_OPEN_FINISH_L/R, AC_SEMAPHORE_* |
| ks_highlands/layout_drift,_int,_long,_short | Highlands (Drift/Long/Short) | 5167/8152/12191/1714 | 24 | clockwise | 8,ks_highlands,2,9,.. ,10,101,100 | 24/24 | 3/2/3/3 | |
| ks_laguna_seca | Laguna Seca | "3.602" | 24 | anti-clockwise | 8,ks_laguna_seca,2..7,9,10 | 24/24 | 3 | |
| ks_monza66/full,junior,road | Monza 1966 - ... | 10000/2405/5793 | 25 | clockwise | 7,ks_monza66,.. | 25/25 | 3 | |
| ks_nurburgring/layout_gp_a,_gp_b,_sprint_a,_sprint_b | Nurburgring - GP/GP (GT)/Sprint/Sprint (GT) | 5148/5137/3629/3618 | 24 | clockwise | 7,(1),ks_nurburgring,..,100 | 24/24 | 3/3/2/2 | |
| ks_silverstone/gp,international,national | Silverstone GP/- International/- National | 5901/3619/2638 | 24 | clockwise | 7,ks_silverstone,.. | 24/24 | 3/2/2 | |
| ks_silverstone1967 | Silverstone 1967 | 4710 | 20 | clockwise | 4,ks_silverstone1967,3,1,2,5 | 20/20 | 3 | |
| ks_vallelunga/classic_circuit,club_circuit,extended_circuit | Vallelunga - Classic/- Club/Vallelunga | 3222/1746/4085 | 24 | clockwise | 1,ks_vallelunga,{4/2/3},100 | 24/24 | 3/2/3 | |
| ks_zandvoort | Zandvoort | 4307 | 18 | clockwise | 2,ks_zandvoort,1,100 | 18/18 | 2 | |
| magione | Magione | 2507 | 18 | clockwise | magione,100 | 18/18 | 2 | |
| monza | Monza | 5793 | 26 | clockwise | 2,monza,1,3,4,5,100 | 26/26 | 3 | |
| mugello | Mugello | 5245 | 24 | clockwise | 1,mugello,2,100 | 24/24 | 3 | |
| spa | Spa | 7004 | 24 | clockwise | 3,spa,1,2,4,100 | 24/24 | 3 | |
| trento-bondone | Trento-Bondone | 17619 | 4 | clockwise | trento-bondone (no models.ini) | 4/4 | 1 | AC_AB_START/FINISH_L/R, AC_SEMAPHORE_* |
| ks_barcelona/layout_gp,layout_moto; ks_brands_hatch/gp,indy; ks_nordschleife/endurance,nordschleife,touristenfahrten; ks_red_bull_ring/layout_gp,layout_national | (DLC) | | | | **UNAVAILABLE** | | | |

Per-layout geometry totals (all kn5 of the layout) [VERIFIED `summary`]: monza 427 MB / 4.62 M verts / 3.36 M tris / 139 tex 213 MB;
magione 303 MB / 3.32 M / 2.38 M / 85 tex 150 MB; spa 504 MB / 5.97 M / 3.08 M; ks_highlands/layout_long (largest) 868 MB / 8.62 M verts /
6.37 M tris / 327 tex 469 MB; ks_drag 147 MB / 0.86 M. Max cars for a race = min(#AC_START, #AC_PIT) (drift has 17 vs 18).

---

## 3. KN5 for tracks

Container exactly as `ac_car_formats.md` 1.1-1.4 [VERIFIED again: all 113 track kn5 parse to EOF with that layout]:
`"sc6969"`, int32 version (6), int32 extra (only if version > 5, always 0); textures (`int32 count`, per texture `int32 type(=1)`, string
name, `int32 size`, bytes); materials (string name, string shader, byte blend, byte alphaTested, int32 depthMode [v>4], int32 propCount
x {string, float A, float[2] B, float[3] C, float[4] D}, int32 samplerCount x {string name, int32 slot, string texture}); node tree.
Strings = int32 length + bytes.

### 3.1 Track-specific node facts [VERIFIED]
- Root is a dummy per FBX (`FBX: ks_monza.FBX`, `FBX: physics.FBX`, ...). Track meshes mostly sit directly under the root dummy (identity).
- Mesh fields used by tracks: `isRenderable` (0 = physics only, do not draw), `layer` (0, 3, 4, 5; grass/crowd `_KSLAYERn` = layer n),
  `lodIn`/`lodOut` metres (0-0 = always; HI/LOW pairs like 0-250 / 245-500, 0-180 / 170-350) -> Unity `LODGroup` or distance culling
  [BELIEF: drawn while lodIn <= cameraDistance < lodOut], `castShadows`, `isTransparent`, bounding sphere.
- No skinned meshes in any track kn5 (skinned only in `content/objects3D/pitcrew*.kn5`).
- Vertex counts per mesh <= 65535 (ushort indices); largest track meshes ~12-22 k verts.

### 3.2 Special (AC_) nodes
Each marker = class-1 dummy `AC_X` (the transform) with ONE class-2 child mesh `AC_X` (24-vert cube, renderable, asphalt material).
**Use the dummy's world matrix; never draw the child cube.** Names known to acs.exe (strings): `AC_TIME_`, `AC_AUDIO_`, `AC_CREW_`,
`AC_POBJECT`, `AC_AB_START_L/R`, `AC_AB_FINISH_L/R`, `AC_OPEN_FINISH_L/R`, `HOTLAP_START`, `KS_START_LIGHT_`, `WALL`.

| node | meaning | evidence |
|---|---|---|
| `AC_START_<n>` | grid slot n (0 = pole). +Z = car forward, +X = car left, +Y up | monza 26, dot(Z, fast-lane fwd) = 1.000; magione/spa/imola/nurburgring/highlands 0.99-1.0 |
| `AC_PIT_<n>` | pit box n (spawn for practice/hotlap, pit stop target) | count == ui pitboxes on all 35 layouts; <= 8.4 m from pit_lane spline (monza) |
| `AC_TIME_<g>_L`, `_R` | timing gate g = segment L->R; g=0 start/finish, g>=1 sector splits; L is LEFT of travel | monza gates at progress 0.0003 / 0.3685 / 0.6737; magione 0.0011 / 0.5254 |
| `AC_HOTLAP_START_0` | hotlap / time-attack rolling start position (monza progress 0.856, on the line approaching Parabolica) | |
| `AC_TIME_ATTACK_0` | monza only (progress 0.9988) [BELIEF time-attack start checkpoint] | |
| `AC_AUDIO_<name>` | reverb zone centres, referenced by data/audio_sources.ini `NODE=` (monza `AC_AUDIO_ROGGIA`, also `AC_START_15`) | |
| `AC_CREW_<box>_LODA<x>`/`_LODB<x>`/`_CWHI`/`_CWLOW` | pit-box equipment meshes per box (LODA 0-10 m, LODB 8-250 m) co-located with AC_PIT_<box> | AC_CREW_0 at monza (-163.8,-7.4,171.8) vs AC_PIT_0 (-154.4,-7.0,173.2) |
| `AC_POBJECT_*` | movable physics props (cones, DRS boards); visible, draw them [BELIEF: dynamic rigid bodies in AC] | |
| `AC_SEMAPHORE_RED/YELLOW/GREEN_<n>` | drag/A2B start tree lamps, driven by data/semaphore.ini | ks_drag, trento-bondone |
| `AC_OPEN_FINISH_L/R` | drag strip finish gate (not a loop) | drag400: start x=26.95, finish x=426.65 -> 399.7 m |
| `AC_AB_START_L/R`, `AC_AB_FINISH_L/R` | point-to-point start / finish gates (trento-bondone; AC_TIME_0 there is a split) | |
| `KS_START_LIGHT_<n>` (meshes in 100.kn5) | start lights, emissive set by data/startinglights.ini | monza 5, magione 4 |
Gate node orientations are inconsistent between tracks (monza gate nodes have Z = up, magione Z = forward) -> use only the L/R POSITIONS.

Positions monza (AC space, world): AC_START_0 (-176.97,-6.60,107.30) Z=(0.099,-0.007,-0.995); AC_PIT_0 (-154.39,-7.03,173.21),
boxes 9.4 m apart; AC_TIME_0_L (-214.44,-8.73,410.40) R (-173.11,-8.91,414.25) (41.5 m wide); AC_TIME_1 L (601.41,0.52,-1165.83)
R (602.55,1.23,-1132.41); AC_TIME_2 L (246.19,-4.73,-339.06) R (227.45,-4.46,-360.70); AC_HOTLAP_START_0 (41.79,-9.67,659.45).
Magione: AC_START_0 (-66.01,5.45,-97.70) Z=(-0.520,0,-0.854); AC_PIT_0 (-71.14,5.06,-75.68); AC_TIME_0 L (-87.18,5.45,-95.08)
R (-58.22,5.45,-111.88); AC_TIME_1 L (56.72,5.45,-71.16) R (15.97,5.27,-62.88). Note the monza grid lies AFTER the line in the travel
direction (grid progress 0.0535..0.0425 > 0): race start = cars cross gate 0 only at the end of lap 1 [BELIEF AC counts the
first crossing after the grid as lap start; handle with "lap 0 until first gate-0 crossing"].

### 3.3 Monza / Magione counts [VERIFIED `track monza`, `track magione`]
| | monza (7 kn5) | magione (2 kn5) |
|---|---|---|
| nodes / meshes | 1,728 / 1,428 | 1,152 / 963 |
| vertices / triangles | 4,623,202 / 3,364,429 | 3,318,131 / 2,380,940 |
| textures / materials | 139 / 82 (monza.kn5 114 / 69) | 85 / 48 |
| physics meshes (surface) | 240 (all in 2.kn5, isRenderable=0) | 212 (in magione.kn5, 79 also render) |
| WALL meshes | 88 | 80 |
| AC_START / AC_PIT | 26 / 26 | 18 / 18 |
| timing gates | 3 (AC_TIME_0..2) | 2 |
| other markers | AC_HOTLAP_START_0, AC_TIME_ATTACK_0, AC_AUDIO_BACK1..3/ROGGIA/SERRAGLIO, AC_CREW x208, AC_POBJECT x20 | AC_HOTLAP_START_0, AC_AUDIO_gstand1/2, AC_CREW x144 |

Physics meshes per surface (count, triangles):
- **monza**: MONZA-ASPH 89 (402,855), CURB 27 (14,509), ROAD 24 (1,869), CONCRETE 21 (2,973), GRASS 18 (11,574), OUT 14 (281), SAND 11 (767),
  KERB 9 (5,246), CARPET 6 (540), PITS-MNZ 4 (599), ILLCONC 4 (290), PEN-CNC-A 2, PEN-GRS-B 2, PEN-ASPH-A/B/C/D 1 each, PEN-CNC-D 1,
  PEN-GRS-D 1, CNC_OU 1 (880), GRILL 1 (542), TARM_OU 1 (497).
- **magione**: TARMACA 51 (124,791), TARMACC 29 (82,452), GRASS 29 (7,013), TARMACE 20 (41,029), TARMACB 18 (65,421), CURB 13 (14,515),
  TARMACD 11 (18,102), OUT 11, KERB 9, SAND 7, CONCRETE 5, PITLANE 3 (4,022), ROAD 2, CUT 2, PEN-GRS-A 1, PEN-GRS-B 1.

### 3.4 Physics surface matching
```
name = <one or more digits><rest>;  key = longest KEY (from layout data/surfaces.ini, then system/data/surfaces.ini) with rest.StartsWith(key)
rest.StartsWith("WALL") -> wall collider (no surface; car bounces/scrapes: FMOD event:/collisions/track/hit, /scrape)
no key matched -> [BELIEF] try rest.Contains(key) (laguna 06RDGRAVEL008 -> GRAVEL), else treat as ROAD
```
Longest-first matters (`PEN-ASPH-A` vs other keys sharing letters; `ROAD` vs `RDOLD`). Monza uses system ROAD/GRASS/SAND for 53 meshes
(not in its own surfaces.ini) -> always merge the system defaults. Use the physics meshes as MeshColliders regardless of isRenderable;
draw only meshes with isRenderable=1.

### 3.5 Textures and shaders in track kn5 (all 113 files, 2,982 textures) [VERIFIED `texall`]
| format (DDS pixel format) | count | MB | Unity |
|---|---|---|---|
| DXT5 | 1698 | 3099 | `TextureFormat.DXT5` raw upload |
| DXT1 | 818 | 815 | `TextureFormat.DXT1` raw upload |
| B8G8R8 24-bit (masks R FF0000 G FF00 B FF, pf 0x40) | 214 | 349 | swizzle -> RGB24 (or RGBA32) |
| B8G8R8A8 32-bit (pf 0x41) | 162 | 255 | `TextureFormat.BGRA32` raw |
| DXT3 | 52 | 19 | CPU decode -> RGBA32 (Unity has no DXT3) |
| A4R4G4B4 16 (R F00, A F000) | 9 | 16 | convert -> RGBA32 |
| L8A8 16 (pf 0x20001) | 9 | 6 | convert |
| PNG | 9 | 1 | `ImageConversion.LoadImage` |
| R5G6B5 (pf 0x40, R F800) | 4 | 5 | `TextureFormat.RGB565` [BELIEF same bit order] |
| X1R5G5B5 / A1R5G5B5 | 2 / 2 | 1 / 2 | convert |
| L8 (pf 0x20000) | 2 | 2 | convert (R8 grey) |
| R8G8B8A8 (R mask FF) | 1 | 3 | `TextureFormat.RGBA32` |
No DX10 headers. 273 textures have mipCount <= 1 (generate mips or leave none); DXT mip chains are often TRUNCATED (2048x2048 with 5 mips)
-> use `new Texture2D(w, h, fmt, mipCount, linear:false)` with the file's mip count. Largest texture: `sbancamento_B.dds` 8192x512.
One texture is not a multiple of 4 (DXT requires multiples of 4 on some GPUs: pad or decode).
Monza.kn5: DXT5 75 (156 MB), RGB24 12 (29 MB), DXT1 23 (9 MB), BGRA32 2, PNG 1, RGB16A 1; sizes mostly 512..2048.

Shaders (material counts across all track kn5): ksPerPixel 469, ksPerPixelMultiMap 285, ksPerPixelAT 129 (alpha test), ksMultilayer_fresnel_nm 105,
ksMultilayer_objsp 88, ksPerPixelNM 83, ksPerPixelMultiMap_NMDetail 61, ksFlags 60 (waving flags: `frequency`, `distortion`), ksPerPixelAlpha 54,
ksPerPixelReflection 54, ksMultilayer 40 (road: txMask + txDetailR/G/B/A blended by mask channels, `multR..multA`, `magicMult`), ksTree 40,
ksGrass 32, ksPerPixelMultiMapSimpleRefl 32, ksPerPixelAT_NM 15, ksPerPixelNM_UVMult 15, GL 14 (unlit), ksPerPixelMultiMap_AT 14, others < 8.
Material props: ksAmbient, ksDiffuse, ksSpecular, ksSpecularEXP, ksEmissive, ksAlphaRef (all materials), fresnelC/EXP/MaxLevel, isAdditive,
useDetail, detailUVMultiplier, multR/G/B/A, tarmacSpecularMultiplier, detailNMMult. Samplers: txDiffuse@0, txNormal@1, txMaps@2, txDetail@3,
ksMultilayer: txMask@1, txDetailR@2, txDetailG@3, txDetailB@4, txDetailA@5, txDetailNM@21; ksGrass: txVariation@1.
Blend/alphaTest/depth combos (monza): 0/False/0 x44, 1/False/0 x8 (blend), 0/True/0 x11 (alpha test), 1/False/1 x6 (blend, no depth write).

---

## 4. Coordinates, winding, UVs (Unity conversion)

### 4.1 Handedness [VERIFIED]
- AC track space: +Y up, right-handed. With **x to the right and z DOWN** the fast lane of monza/magione/spa/vallelunga runs **clockwise**
  (signed area > 0) and imola **anti-clockwise** = exactly the ui `run` values; the rendered monza line is identical to `map.png` (4.3)
  and is the real-world map (north up: Curva Grande/Lesmo in the north-east, Parabolica in the south). So AC +X = east, -Z = north.
- Same conclusion as the car spec: car +Z forward, +X left (verified here with AC_START: node X axis points to the LEFT of travel).

### 4.2 Conversion (use the car spec's helpers unchanged)
- Positions/normals/tangents: `(-x, y, z)`. Triangles: `(i0, i2, i1)`. Matrices: `U = S * transpose(M) * S`, S = diag(-1,1,1,1).
- Winding proof: in raw AC data `dot(cross(b-a, c-a), vertexNormal) > 0` for 447,715 of 447,719 triangles of monza 2.kn5, 1,714,979 vs
  23,871 for monza.kn5 (double-sided foliage etc.), 331,785 vs 4 for magione TARMAC meshes; up-facing road triangles have cross.y > 0.
  Mirroring X flips the cross product -> swap i1/i2 so that Unity's `Vector3.Cross(b-a, c-a)` again points along the normal.
- After conversion: Unity +Z = car forward, +X = car right, +Y up (a normal Unity vehicle frame). Marker rotation in Unity:
  `Quaternion.LookRotation(new Vector3(-Z.x, Z.y, Z.z), new Vector3(-Y.x, Y.y, Y.z))` using rows 2 (Z) and 1 (Y) of the AC world matrix.
- Do NOT switch to a z-flip: it would put car forward on -Z.

### 4.3 map.png / map.ini [VERIFIED]
```
[PARAMETERS] WIDTH=927.422 HEIGHT=1575.82 MARGIN=20 SCALE_FACTOR=1.4 MAX_SIZE=1600 X_OFFSET=232.866 Z_OFFSET=1297.7 DRAWING_SIZE=10   (monza)
pixelX = (x_ac + X_OFFSET) / SCALE_FACTOR ;  pixelY = (z_ac + Z_OFFSET) / SCALE_FACTOR   (pixelY from the TOP of the png)
```
monza map.png = 927x1575 (= WIDTH x HEIGHT), AI line spans px 15..914 x 14..1561 (= MARGIN/SCALE padding); magione (SCALE 1) 342x861,
line spans 20..325 x 19..840. Rendered output: `C:\Users\derii\AppData\Local\Temp\claude\acscratch\tracks\monza_ai.png` (compare to
`content/tracks/monza/map.png` - identical outline). system/cfg/map.ini: `MAX_DIMENSION_FORM=300`, `DRIVER_ICON SIZE=10` (AC's map app).
In Unity-converted coordinates use `x_ac = -x_unity`.

### 4.4 UVs [VERIFIED values, BELIEF on the Unity side - same rule as car spec 1.5]
Stored V is mostly negative/out of 0..1 (monza road physics v -839..8.7, magione TARMAC v -62.5..0, u 0..1: tiled). Upload DDS raw
(`LoadRawTextureData`, row 0 = image top at Unity v=0) and use uv = (u, v_stored) with Repeat wrap; for PNG/converted textures flip
the rows after decoding so the same uv works.

---

## 5. AI splines (`ai/fast_lane.ai`, `ai/pit_lane.ai`, `data/ideal_line.ai`)

### 5.1 Format v7 [VERIFIED: the 34 non-drift fast_lane + 34 pit_lane files parse exactly to EOF]
```
off 0   int32 version            7   (acs.exe: "AI SPLINE VERSION %d", "LOADED OLD SPLINE old Version : %d")
off 4   int32 pointCount         monza 3750
off 8   int32 lapTime            0 in every file
off 12  int32 sampleCount        0 in every file
off 16  pointCount x 20 bytes:   float x, y, z (AC world) ; float length (cumulative 3D distance from point 0, m) ; int32 id (== index)
        int32 payloadCount       == pointCount   ("SPLINE POINTS COUNT : %u | PAYLOADS COUNT : %u")
        payloadCount x 18 floats (72 B):
          [0] speed (m/s)  [1] gas 0..1  [2] brake 0..1  [3] obsoleteLatG  [4] radius (m, huge on straights)
          [5] sideLeft (m, track edge left of line)  [6] sideRight (m)  [7] camber  [8] direction (-1/1)
          [9..11] normal xyz  [12] length to next point  [13..15] forward xyz (unit, to next point)  [16] tag  [17] grade (= forward.y)
        int32 hasGrid            1 for fast_lane/ideal_line, 0 for pit_lane (then EOF)
        if hasGrid: float[3] max ; float[3] min ; int32 neighbours (10) ; float step (10.0 m)
                    int32 Nx ; repeat Nx { int32 Nz ; repeat Nz { int32 n ; int32[n] pointIds } }    (n = 10 near the line, varies)
```
Monza fast_lane: 3750 points, length 5757.2 m, segment 1.456..4.66 m (avg 1.54), last->first gap 1.55 m (closed loop),
payload end 345,020, grid max (1396.6,0,1237.5) min (-562.2,0,-1627.6), 195 x 286 cells, file 2,799,720 B. Grid index:
`cell = cx * Nz + cz`, `cx = floor((x - min.x)/step)`, `cz = floor((z - min.z)/step)` [VERIFIED 536/536 points found in their own cell;
z-major order 0/536]. Monza pit_lane: 1147 points, 1758.8 m, all payload fields except speed/gas/brake/latG/direction(=1) are 0.
Payload checks (monza): length[12] == dist(i,i+1) for 3505/3749, forward[13..15] == normalize(p[i+1]-p[i]) for 3631/3749 (rest are
smoothed). speed 7.0..31.4 m/s; sideLeft 0.48..10.76, sideRight 0.39..10.81 m (track width at point = left+right).
Magione fast_lane has payload speed 0 in a few sections (min AI speed 0 km/h in "Tornantino" etc.) - don't divide by speed.

Exceptions [VERIFIED]:
- `drift/ai/fast_lane.ai` and `pit_lane.ai`: 12 bytes, version 3, pointCount 0 -> **no AI line**: progress/race position must fall back to gates.
- `ks_drag/*/ai/fast_lane.ai` == `pit_lane.ai` (1539 pts, 2468.8 m, with grid) for all 5 drag layouts -> use AC_START -> AC_OPEN_FINISH distance.
- `data/ideal_line.ai`: mostly v7 (same format, the visual ideal line); imola, ks_silverstone/gp, international/`_ideal_line.ai` are
  **v2** (header int32 2, count, int32 114207 = lap time ms?, different point layout) and magione, mugello, vallelunga/extended fail
  as v7 -> ignore ideal_line.ai, use ai/fast_lane.ai for everything.

### 5.2 Lap progress, sectors, race position [VERIFIED numbers, algorithm = design]
- `progress(i) = Length[i] / Length[N-1]` (0 at the start/finish gate). Projecting a car: nearest point (grid cell -> 10 candidate ids, or
  a +-window search around last frame's index), then interpolate on segment i -> i+1: `progress = (Length[i] + t*seg) / total`.
  Probe implementation: `AcAiSpline.Nearest/Progress/Cell` (section 9.2). Monza AC_START_0 -> progress 0.0535 (0.2 m off line).
  The section 9.2 code was compiled and tested: grid-cell nearest == brute-force nearest for 2000/2000 random points (+-8 m) on monza
  and spa, 1998/2000 on magione (2 misses where two track parts run side by side, progress off by 0.18) [VERIFIED] -> use the grid only
  for the first fix (spawn/teleport) and then track with a +-window around last frame's index (and compare Y to reject the wrong level).
- Lap counting: detect crossing of segment AC_TIME_0_L -> AC_TIME_0_R (2D XZ segment intersection of the car's last-frame -> this-frame
  motion) while moving forward (dot with fast-lane forward > 0) AND last progress > ~0.5 (anti-cheat for reversing). Sector splits:
  gates 1..G-1 (monza 3 sectors, magione 2). Fast lane crosses each gate exactly once [VERIFIED for all gates of monza/magione/vallelunga].
- Race position = sort by (completedLaps desc, progress desc); progress wraps at the line.
- Track limits / cuts: car outside `sideLeft/sideRight` of the nearest point, or wheels on surfaces with `IS_VALID_TRACK=0` (5.3) [BELIEF:
  AC invalidates the lap when all 4 wheels are off valid surfaces; server setting "allowed tyres out" default 2].
- Point-to-point (trento-bondone): start at AC_AB_START gate, finish at AC_AB_FINISH gate, AC_TIME_0 = split. Drag: AC_OPEN_FINISH.

### 5.3 Pit lane [VERIFIED]
- pit_lane.ai is not a loop: monza starts at fast progress 0.880 (pit entry before Parabolica, 0.3 m from fast lane) and ends at 0.186
  (rejoin after the first chicane, 1.3 m); magione 0.773 -> 0.272. Pit boxes lie within 6-8.4 m of it (pit progress monza box 0 = 0.530).
- "In pit lane" = wheel surface with `IS_PITLANE=1` (monza `PITS-MNZ`, magione `PITLANE`) and/or near the pit spline.
  Pit speed limit is not a track value [BELIEF: AC server/session setting, default 80 km/h].

---

## 6. data/*.ini (per layout) [VERIFIED contents]

Engine paths (acs.exe strings): `/ai/fast_lane.ai`, `/ai/pit_lane.ai`, `/data/surfaces.ini`, `/data/audio_sources.ini`, `/data/camera_facing.ini`,
`/data/crew.ini`, `/data/drs_zones.ini`, `/data/groove.ini`, `/data/ideal_line.ai`, `/data/lighting.ini`, `/data/sections.ini`,
`/data/semaphore.ini`, `/data/startinglights.ini`, `\data\map.ini`, `system/data/surfaces.ini`, `system/cfg/lighting.ini`, `system/cfg/map.ini`.
INI syntax: `[SECTION]`, `KEY=VALUE`, `;` comments (also after values: `SIDE=-1 ; 1 = left`), numbers with spaces around commas
(`-206.718 ,-8.39364 ,410.949`), invariant culture. Presence over 35 data folders: surfaces/map/crew/cameras/audio_sources 35,
cameras_1 34, camera_facing 31, cameras_2 31, overlays 30, cameras_start 30, startinglights 28, lighting 28 (missing: ks_black_cat_county,
ks_highlands -> use defaults), sections 25, groove 22, drs_zones 21, ai_hints 12, cameras_3 8, semaphore 6 (drag x5, trento), opt/tcameras/
starting_bounds/ai_corners 1 each.

### 6.1 surfaces.ini
```
[SURFACE_n]  KEY=MONZA-ASPH  FRICTION=0.99  DAMPING=0  WAV=  WAV_PITCH=0  FF_EFFECT=NULL  DIRT_ADDITIVE=0  IS_VALID_TRACK=1
             BLACK_FLAG_TIME=0  SIN_HEIGHT=0  SIN_LENGTH=0  IS_PITLANE=0  VIBRATION_GAIN=0  VIBRATION_LENGTH=0
```
- FRICTION multiplies tyre grip (monza asphalt 0.99, kerb 0.95, CARPET 0.7, grass 0.6); DAMPING (sand 0.1 = rolling drag);
  SIN_HEIGHT/SIN_LENGTH = bumpy sine surface (grass 0.03/0.5, sand 0.04/0.5); DIRT_ADDITIVE = dirt on tyres (grass/sand 1);
  IS_VALID_TRACK=0 + BLACK_FLAG_TIME=5/10 s on penalty zones (`PEN-*`, `ILLCONC`); IS_PITLANE=1 on pit surfaces; VIBRATION_* + FF_EFFECT = FFB/rumble.
  [BELIEF on exact physics semantics; values VERIFIED].
- WAV maps to FMOD events in `content/sfx/common.bank`: `kerb.wav` -> `event:/surfaces/kerb`, `grass.wav` -> `/surfaces/grass`, `sand.wav`
  -> `/surfaces/sand`, `extraturf.wav` -> `/surfaces/extraturf`; also `/surfaces/gravel`, `/surfaces/old`, `/surfaces/tyre_rolling`
  (GUIDs.txt; acs.exe has `event:/surfaces/` + name) [VERIFIED names]; WAV_PITCH = pitch.
- System defaults (`system/data/surfaces.ini`): ROAD (1.0), GRASS (0.6, grass.wav, invalid, sin 0.03/0.5, dirt 1), KERB (0.92, kerb.wav, pitch 1.3,
  vib 1.0/1.5), SAND (0.8, damping 0.1, sand.wav, invalid, sin 0.04/0.5).
- Monza keys: CONCRETE CARPET PITS-MNZ ILLCONC OUT KERB CURB MONZA-ASPH PEN-ASPH-B PEN-ASPH-A PEN-CNC-A PEN-GRS-D PEN-GRS-B PEN-ASPH-D
  PEN-CNC-D PEN-ASPH-C TARM_OU CNC_OU GRILL. Magione: PITLANE TARMACA..E OUT KERB CURB CONCRETE CUT PEN-GRS-B PEN-GRS-A.

### 6.2 map.ini -> section 4.3.  overlays.ini: `[CHECKPOINT_n] WORLD_POSITION, OFFSET, WIDTH, HEIGHT, ORIENTATION` (3 in monza; AC's
time-attack checkpoint banners [BELIEF]); uses `content/texture/checkpoint.png`.

### 6.3 lighting.ini  `[LIGHTING] SUN_PITCH_ANGLE=40 SUN_HEADING_ANGLE=-150` (monza; magione 35/240; most tracks 35/45)
[BELIEF]: heading = compass rotation of the sun path for this track, pitch = default elevation. Time of day comes from the session:
`Documents/Assetto Corsa/cfg/race.ini [LIGHTING] SUN_ANGLE=-48` (AC: about 16 deg per hour around 13:00 [BELIEF]).
`system/cfg/lighting.ini`: `[HDR] MIN_EXPOSURE=0.16 MAX_EXPOSURE=0.55`, `[LIGHT] LIGHT_HEIGHT=1000`.

### 6.4 sections.ini (corner names for the HUD)  `[SECTION_n] IN=0.151 OUT=0.178 TEXT=Prima Variante` (normalized progress; may wrap:
monza SECTION_0 0.970-1 and SECTION_1 0-0.123 both "Rettifilo Partenza"). Verified with fast-lane radius minima: Prima Variante 18 m @0.163,
Roggia 31 m @0.371, Lesmo 1/2 78/62 m, Ascari 61 m @0.683, Parabolica 77 m @0.903.

### 6.5 drs_zones.ini  `[ZONE_n] DETECTION=0.880 START=0.029 END=0.152` (normalized progress; monza 2 zones, magione 1).

### 6.6 startinglights.ini  `[SETTINGS] EMISSIVE=255,0,0,255 INTENSITY=2 DIFFUSE=0.3` -> emissive colour of `KS_START_LIGHT_n` meshes.
[BELIEF] AC sequence: lights switch on one by one (red), all off = go (acs.exe: "STARTING LIGHTS ACTIVE %i").
### 6.7 semaphore.ini (drag/A2B christmas tree)
```
[GROUPS] DISQUALIFIED=1 READY=3 START=1     [COLOR] INTENSITY=2.2
[OBJECT_n] TYPE=0|1|2 (0=DISQUALIFIED 1=READY 2=START)  NAME=AC_SEMAPHORE_YELLOW_1  ORDER=0 (step in its group)  [INTENSITY=]
```
### 6.8 crew.ini `[HEADER] SIDE=-1 ; 1 = left , -1 = right` (side of the car the pit crew stands; monza -1, magione 1). Crew models:
`content/objects3D/pitcrew.kn5`, `pitcrew_Left.kn5`, `pitcrewtyre.kn5` (+ `.ksanim`; skinned, v6, parse OK) and `pit_indicator.kn5`.
### 6.9 audio_sources.ini `[REVERB_n] ENABLED=1 NODE=AC_AUDIO_ROGGIA MINDISTANCE=25 MAXDISTANCE=30 PRESET=CUSTOM DECAY_TIME=1200 EARLY_DELAY=5
LATE_DELAY=10 HF_REFERENCE=5000 HF_DECAY_RATIO=20 DIFFUSION=80 DENSITY=60 LOW_SHELF_FREQUENCY=250 LOW_SHELF_GAIN=0 HIGH_CUT=750 EARLY_LATE_MIX=80
WET_LEVEL=-3` -> FMOD-style reverb zone at the node (maps well onto Unity `AudioReverbZone`). Monza 5 zones (one on `AC_START_15`, 250-675 m).
### 6.10 groove.ini `[HEADER] GROOVES_NUMBER=8`, `[GROOVE_n] NAME=groove2 MIN=0.2 MAX=1.0 MULT=5` -> meshes/materials named NAME are the
rubber groove overlay whose opacity grows with track rubber (race.ini `[DYNAMIC_TRACK]` SESSION_START/LAP_GAIN) [BELIEF].
### 6.11 cameras*.ini (replay/TV sets), cameras_start.ini (6 fixed start cams), camera_facing.ini (crowd billboards)
`[HEADER] VERSION=3 CAMERA_COUNT=13 SET_NAME=TV 1`, `[CAMERA_n] NAME POSITION FORWARD UP MIN_FOV MAX_FOV IN_POINT OUT_POINT (progress
range the cam covers) SHADOW_SPLIT0..2 NEAR_PLANE FAR_PLANE MIN/MAX_EXPOSURE DOF_* SPLINE=1.csv (moving cam path in data/*.csv: "x, y, z"
per line) SPLINE_ROTATION FOV_GAMMA SPLINE_ANIMATION_LENGTH IS_FIXED` (useful for a spectator/TV screen in the hub).
camera_facing.ini: `[CAMERA_FACING_n] SURFACE=camerafacing_8_KSLAYER4 ELEMENTS=600 SIZE=0.8,1.4 TEXTURE=content/texture/people_sit.dds
TEXTURE_ROWS=3 TEXTURE_COLUMNS=7 SHADED=1 DIFFUSE AMBIENT` -> scatter ELEMENTS billboards (crowd) over the named mesh.
### 6.12 ai_hints.ini `[DANGER_n] START END LEFT RIGHT`; ai_corners.ini `[CORNER_n] IN APEX OUT` (point indices, magione only);
starting_bounds.ini `[BOUND_n] MIN MAX` (progress); opt.ini `[GRID_RC] IS_ACTIVE=0`. AI only - not needed for player racing.

### 6.13 Weather / sky (content/weather, system/cfg)
`content/weather/1_heavy_fog .. 7_heavy_clouds/weather.ini` (+ colorCurves.ini): `[CLOUDS] COVER CUTOFF COLOR WIDTH HEIGHT RADIUS NUMBER
BASE_SPEED_MULT`, `[FOG] COLOR=1.9,2.45,3.32 BLEND DISTANCE`, `[LAUNCHER] NAME=Clear TEMPERATURE_COEFF`. Cloud textures:
`content/texture/clouds/cloud1C..7C.dds` (+ 256/512/1024 subfolders), `content/texture/cloud.dds`. Defaults `system/cfg/weather.ini`.
race.ini `[WEATHER] NAME=4_mid_clear`, `[TEMPERATURE] AMBIENT=26 ROAD=34`, `[WIND]`. No sky textures: AC draws a procedural sky [BELIEF].

---

## 7. Loading a 300+ MB track in Unity at run time

Measured on this PC (probe, .NET 10, `FileStream` 1 MB buffer + `SequentialScan`, `BinaryReader`) [VERIFIED numbers, warm OS cache]:
| file | size | header-only (seek textures + vertices) | + vertices into arrays | + all texture bytes | managed heap |
|---|---|---|---|---|---|
| monza.kn5 | 316.6 MB | 400-590 ms (incl. JIT, first run) | 380-430 ms | 470-530 ms | ~320-460 MB |
| magione.kn5 | 312.8 MB | 27-150 ms | 93-119 ms | 190-290 ms | ~400-530 MB |
| ks_laguna_seca.kn5 | 359.4 MB | 16-294 ms | 62-67 ms | 190-280 ms | ~440-500 MB |
| ks_highlands.kn5 | 423.1 MB | 47-504 ms | 110-150 ms | 200-250 ms | ~390-500 MB |
Whole-layout header scans: monza 7 files 37-580 ms, highlands long 10 files ~1.1 s. The disk is fast (~1.2 GB/s); a cold HDD will be 5-10x slower.
The car spec measured 1.5 s for magione on its first (cold) run.

Recommended pipeline (design; Unity side NOT verified in Unity):
1. **Worker thread** (System.Threading, no Unity API): header scan of every kn5 of the layout (record per texture `Offset/Size`, per mesh the
   file offset of its vertex block) -> marker list, surface meshes, material table. Lets the tablet show "Loading Monza 0%" immediately.
2. Worker: read vertex blocks (`ReadBytes(vc*44)` + `Buffer.BlockCopy` into `float[]`, NO per-vertex BitConverter) and convert in the same
   loop: pos (-x,y,z), normal (-nx,ny,nz), tangent (-tx,ty,tz, w=1 [check]), uv as-is, indices swapped (i0,i2,i1). Build per-mesh arrays.
3. Main thread, time-sliced (e.g. 4-8 ms per frame): `new Mesh()`, `SetVertexBufferParams` + `SetVertexBufferData(float[] ...)` (managed-array
   overload, no NativeArray/Span needed), `SetIndexBufferParams(n, IndexFormat.UInt16)` + `SetIndexBufferData(ushort[])`, `SetSubMesh`,
   bounds from the kn5 sphere; `UploadMeshData(true)` for render meshes (frees the CPU copy: monza ~222 MB of vertex data otherwise kept twice).
4. Textures: read on the worker by offset (FileStream per thread), main thread: `new Texture2D(w,h,DXT5/DXT1/BGRA32,mips,false)`,
   `LoadRawTextureData(bytes from +128)`, `Apply(false, true)` (non-readable -> CPU copy freed). Only load textures referenced by materials of
   meshes you draw (skip physics-only files' textures: monza 2.kn5 has 1). Monza GPU texture memory ~224 MB as-is (DXT kept compressed);
   RGB24 textures become 32 bpp in Unity (+33%).
5. Physics: one `MeshCollider` per surface mesh (monza 240 meshes / ~450 k tris, 88 walls), cook with `Physics.BakeMesh(mesh.GetInstanceID(), false)`
   on a worker thread [BELIEF thread-safe since Unity 2019.3], then assign on the main thread. Map collider -> surface record
   (`Dictionary<int colliderInstanceId, AcSurface>`) for wheel raycasts (friction, sound, valid-track, pit-lane).
6. Draw calls: ~1,400 meshes / ~140 materials: group by material (`StaticBatchingUtility.Combine(root)` after creation) and use LODGroups for
   lodIn/lodOut; skip `1.kn5`-style grass (ksGrass, ~1.9 M verts at monza) on low settings / VR.
7. Hide: child cube meshes of all AC_ markers except AC_POBJECT/AC_CREW; all `isRenderable=0` meshes.

---

## 8. Things NOT found / open
- No `startinglights` timing values in the track (sequence timing is engine-side). No pit speed limit, no track temperature in track data.
- `AC_TIME_ATTACK_0` purpose unproven (monza only). `direction` and `tag` payload fields not decoded (tag always 0 in monza).
- ideal_line.ai v2 layout not decoded (not needed).
- Track skins (`skins/` folders) and `system/cfg/track_skins.ini` not researched.

---

## 9. Verified C# readers (C# 9, Unity-safe: no Span/unsafe/LINQ in hot paths; copied from the probe that produced every number above)

### 9.1 KN5 header-only scan (track extension of the car spec reader; seeks past bulk data and remembers offsets)
```csharp
// Use the car spec reader (ac_car_formats.md 1.8) for full meshes; this variant is for the fast first pass over 300+ MB track files.
public sealed class Kn5Tex { public string Name; public long Offset; public int Size; }
public sealed class Kn5Node {
    public int Class; public string Name; public bool Active; public float[] Local;     // class 1: 16 floats row-major, row-vector
    public bool CastShadows, Visible, Transparent, Renderable;
    public int VertexCount, IndexCount, MaterialId, Layer; public float LodIn, LodOut;
    public long VertexOffset, IndexOffset;                                            // file offsets of the bulk blocks
    public float[] SphereCenter = new float[3]; public float SphereRadius;
    public List<Kn5Node> Children = new List<Kn5Node>(); public Kn5Node Parent;
}
public static class Kn5Scan {
    public static Kn5Node Read(string path, List<Kn5Tex> textures, List<Kn5Material> materials, out int version) {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        using (var r = new BinaryReader(fs)) {
            if (Encoding.ASCII.GetString(r.ReadBytes(6)) != "sc6969") throw new InvalidDataException("not kn5");
            version = r.ReadInt32(); if (version > 5) r.ReadInt32();
            int tc = r.ReadInt32();
            for (int i = 0; i < tc; i++) {
                r.ReadInt32(); var t = new Kn5Tex { Name = Str(r) }; t.Size = r.ReadInt32(); t.Offset = fs.Position;
                fs.Seek(t.Size, SeekOrigin.Current); textures.Add(t);
            }
            int mc = r.ReadInt32();
            for (int i = 0; i < mc; i++) materials.Add(ReadMaterial(r, version));   // as car spec (props 4+8+12+16 floats, samplers)
            var root = ReadNode(r, fs, null);
            if (fs.Position != fs.Length) throw new InvalidDataException("kn5 trailing bytes");
            return root;
        }
    }
    static Kn5Node ReadNode(BinaryReader r, FileStream fs, Kn5Node parent) {
        var n = new Kn5Node { Parent = parent, Class = r.ReadInt32(), Name = Str(r) };
        int kids = r.ReadInt32(); n.Active = r.ReadBoolean();
        if (n.Class == 1) { n.Local = new float[16]; for (int i = 0; i < 16; i++) n.Local[i] = r.ReadSingle(); }
        else if (n.Class == 2) {
            n.CastShadows = r.ReadBoolean(); n.Visible = r.ReadBoolean(); n.Transparent = r.ReadBoolean();
            n.VertexCount = r.ReadInt32(); n.VertexOffset = fs.Position; fs.Seek((long)n.VertexCount * 44, SeekOrigin.Current);
            n.IndexCount = r.ReadInt32(); n.IndexOffset = fs.Position; fs.Seek((long)n.IndexCount * 2, SeekOrigin.Current);
            n.MaterialId = r.ReadInt32(); n.Layer = r.ReadInt32(); n.LodIn = r.ReadSingle(); n.LodOut = r.ReadSingle();
            for (int k = 0; k < 3; k++) n.SphereCenter[k] = r.ReadSingle(); n.SphereRadius = r.ReadSingle();
            n.Renderable = r.ReadBoolean();
        }
        else if (n.Class == 3) {   // never in tracks; skip safely
            n.CastShadows = r.ReadBoolean(); n.Visible = r.ReadBoolean(); n.Transparent = r.ReadBoolean();
            int bones = r.ReadInt32(); for (int b = 0; b < bones; b++) { Str(r); fs.Seek(64, SeekOrigin.Current); }
            n.VertexCount = r.ReadInt32(); fs.Seek((long)n.VertexCount * 76, SeekOrigin.Current);
            n.IndexCount = r.ReadInt32(); fs.Seek((long)n.IndexCount * 2, SeekOrigin.Current);
            n.MaterialId = r.ReadInt32(); n.Layer = r.ReadInt32(); fs.Seek(8, SeekOrigin.Current);
        }
        else throw new InvalidDataException("kn5 node class " + n.Class);
        for (int i = 0; i < kids; i++) n.Children.Add(ReadNode(r, fs, n));
        return n;
    }
    static string Str(BinaryReader r) { int len = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(len)); }
    // AC world matrix (row-vector convention): world = local * parentLocal * ... * rootLocal
    public static float[] World(Kn5Node n) {
        float[] m = { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
        for (var p = n; p != null; p = p.Parent) if (p.Local != null) m = Mul(m, p.Local);
        return m;   // translation m[12..14], X axis m[0..2] (car left), Y axis m[4..6], Z axis m[8..10] (car forward)
    }
    static float[] Mul(float[] a, float[] b) {
        var o = new float[16];
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { float s = 0; for (int k = 0; k < 4; k++) s += a[i*4+k] * b[k*4+j]; o[i*4+j] = s; }
        return o;
    }
}
// Markers: walk the tree, take class-1 nodes whose Name starts with "AC_" -> World(); hide their class-2 child of the same name.
// Physics: class-2 nodes with Name[0] digit -> AcSurfaces.Match(name) (9.4).
```

### 9.2 AI spline reader + progress (verified on all 34 non-drift fast/pit lanes)
```csharp
public sealed class AcAiSpline {
    public int Version, Count; public float[] Pos, Length; public int[] Id;   // Pos xyz*Count (AC space)
    public int PayloadCount; public float[] Payload;                          // 18 floats per point
    public const int Speed = 0, Gas = 1, Brake = 2, Radius = 4, SideLeft = 5, SideRight = 6, Camber = 7, Direction = 8,
                     NormalX = 9, SegLength = 12, ForwardX = 13, Grade = 17;
    public bool HasGrid; public float[] GridMax = new float[3], GridMin = new float[3]; public int Neighbours, Nx, Nz; public float Step;
    public int[][] Cells;                                                     // index cx*Nz + cz
    public float Total { get { return Count > 0 ? Length[Count - 1] : 0f; } }

    public static AcAiSpline Read(string path) {
        var s = new AcAiSpline();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (var r = new BinaryReader(fs)) {
            s.Version = r.ReadInt32(); s.Count = r.ReadInt32();
            if (s.Version != 7 || s.Count <= 0) { s.Count = 0; s.Pos = new float[0]; s.Length = new float[0]; s.Id = new int[0]; return s; } // drift: v3, 0 pts
            r.ReadInt32(); r.ReadInt32();                                    // lapTime, sampleCount (0)
            s.Pos = new float[s.Count * 3]; s.Length = new float[s.Count]; s.Id = new int[s.Count];
            for (int i = 0; i < s.Count; i++) {
                s.Pos[i*3] = r.ReadSingle(); s.Pos[i*3+1] = r.ReadSingle(); s.Pos[i*3+2] = r.ReadSingle();
                s.Length[i] = r.ReadSingle(); s.Id[i] = r.ReadInt32();
            }
            if (fs.Position < fs.Length) {
                s.PayloadCount = r.ReadInt32(); s.Payload = new float[s.PayloadCount * 18];
                byte[] b = r.ReadBytes(s.Payload.Length * 4); Buffer.BlockCopy(b, 0, s.Payload, 0, b.Length);
            }
            if (fs.Position < fs.Length && r.ReadInt32() != 0) {
                s.HasGrid = true;
                for (int k = 0; k < 3; k++) s.GridMax[k] = r.ReadSingle();
                for (int k = 0; k < 3; k++) s.GridMin[k] = r.ReadSingle();
                s.Neighbours = r.ReadInt32(); s.Step = r.ReadSingle();
                s.Nx = r.ReadInt32(); var cells = new List<int[]>();
                for (int x = 0; x < s.Nx; x++) {
                    int nz = r.ReadInt32(); if (x == 0) s.Nz = nz;
                    for (int z = 0; z < nz; z++) { int n = r.ReadInt32(); var a = new int[n]; for (int k = 0; k < n; k++) a[k] = r.ReadInt32(); cells.Add(a); }
                }
                s.Cells = cells.ToArray();
            }
        }
        return s;
    }
    public float P(int i, int f) { return Payload[i * 18 + f]; }
    public int[] Cell(float x, float z) {                                    // AC-space x,z
        int cx = (int)Math.Floor((x - GridMin[0]) / Step), cz = (int)Math.Floor((z - GridMin[2]) / Step);
        if (cx < 0 || cz < 0 || cx >= Nx || cz >= Nz) return null;
        return Cells[cx * Nz + cz];
    }
    public int Nearest(float x, float z, int hint, int window) {           // hint<0: grid cell, else +-window around hint
        int best = -1; float bd = float.MaxValue;
        if (hint < 0) {
            int[] c = HasGrid ? Cell(x, z) : null;
            if (c != null && c.Length > 0) { for (int k = 0; k < c.Length; k++) Test(c[k], x, z, ref best, ref bd); return best; }
            for (int i = 0; i < Count; i++) Test(i, x, z, ref best, ref bd); return best;
        }
        for (int d = -window; d <= window; d++) { int i = ((hint + d) % Count + Count) % Count; Test(i, x, z, ref best, ref bd); }
        return best;
    }
    void Test(int i, float x, float z, ref int best, ref float bd) {
        float dx = Pos[i*3] - x, dz = Pos[i*3+2] - z, d = dx*dx + dz*dz; if (d < bd) { bd = d; best = i; }
    }
    public float Progress(float x, float z, int i) {                       // 0..1, 0 = AC_TIME_0 gate
        int j = (i + 1) % Count;
        float ax = Pos[i*3], az = Pos[i*3+2], vx = Pos[j*3] - ax, vz = Pos[j*3+2] - az, l2 = vx*vx + vz*vz;
        float t = l2 > 0 ? ((x - ax) * vx + (z - az) * vz) / l2 : 0f; if (t < 0) t = 0; if (t > 1) t = 1;
        float seg = (float)Math.Sqrt(l2), total = Total + (float)Math.Sqrt(Sq(Pos[0] - Pos[(Count-1)*3]) + Sq(Pos[2] - Pos[(Count-1)*3+2]));
        return (Length[i] + t * seg) / total;
    }
    static float Sq(float a) { return a * a; }
}
```
(Remember to convert the car's Unity position back to AC space for these calls: `x_ac = -x_unity, z_ac = z_unity`, or convert the spline once.)

### 9.3 Track scan + lenient JSON + INI (verified on all 21 folders / 44 json files)
The probe's `Layouts.cs` (`AcTracks.Scan`, `AcTrackLayout`, `LenientJson.ParseFile`) and `Program.cs` (`Ini.Load`) are C# 9 compatible and
can be copied verbatim into `Data/` (Unity-free). Key code:
```csharp
// INI: last value wins, ';' and '//' comments, keys case-insensitive
public static Dictionary<string, Dictionary<string, string>> Load(string path) {
    var res = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase); Dictionary<string, string> cur = null;
    foreach (var raw in File.ReadAllLines(path, Encoding.UTF8)) {
        string line = raw; int c = line.IndexOf(';'); if (c >= 0) line = line.Substring(0, c);
        c = line.IndexOf("//", StringComparison.Ordinal); if (c >= 0) line = line.Substring(0, c);
        line = line.Trim(); if (line.Length == 0) continue;
        if (line[0] == '[') { int e = line.IndexOf(']'); string name = line.Substring(1, (e < 0 ? line.Length : e) - 1).Trim();
            if (!res.TryGetValue(name, out cur)) { cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); res[name] = cur; } continue; }
        int eq = line.IndexOf('='); if (eq < 0 || cur == null) continue;
        cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
    }
    return res;
}
// Vectors: "-206.718 ,-8.39364 ,410.949" -> split(','), Trim(), float.Parse(s, CultureInfo.InvariantCulture)
// JSON text: try new UTF8Encoding(false, true).GetString(bytes); on DecoderFallbackException map bytes 1:1 to chars (Latin-1).
// Layout detection: see 1.2. models: only sections whose name starts with "MODEL_" (watch the dangling-else trap:
//   if (exists) { foreach (...) if (MODEL_) add; } else add(track + ".kn5");   <- braces required, the probe had this bug once)
```

### 9.4 Surface matching
```csharp
public sealed class AcSurface { public string Key, Wav; public float Friction = 1, Damping, WavPitch, DirtAdditive, BlackFlagTime, SinHeight,
    SinLength, VibrationGain, VibrationLength; public bool IsValidTrack = true, IsPitlane; }
// keys = layout surfaces.ini SURFACE_* then system/data/surfaces.ini (layout wins on duplicate KEY), sorted by Key.Length descending
public static AcSurface Match(string meshName, List<AcSurface> keysLongestFirst, out bool isWall) {
    isWall = false; int i = 0; while (i < meshName.Length && char.IsDigit(meshName[i])) i++;
    if (i == 0) return null;                                      // not a physics mesh
    string rest = meshName.Substring(i);
    if (rest.StartsWith("WALL", StringComparison.Ordinal)) { isWall = true; return null; }
    for (int k = 0; k < keysLongestFirst.Count; k++) if (rest.StartsWith(keysLongestFirst[k].Key, StringComparison.Ordinal)) return keysLongestFirst[k];
    for (int k = 0; k < keysLongestFirst.Count; k++) if (rest.IndexOf(keysLongestFirst[k].Key, StringComparison.Ordinal) >= 0) return keysLongestFirst[k]; // [BELIEF] fallback
    return null;   // treat as ROAD
}
```

### 9.5 AC -> Unity for markers (uses the car spec's AcToUnity)
```csharp
var w = Kn5Scan.World(node);                                         // AC world
Vector3 pos = new Vector3(-w[12], w[13], w[14]);
Quaternion rot = Quaternion.LookRotation(new Vector3(-w[8], w[9], w[10]), new Vector3(-w[4], w[5], w[6])); // +Z forward, +Y up
// models.ini instance: parent.localPosition = (-px, py, pz); parent.localRotation = Quaternion.Euler(0, rotY, 0)  (section 1.3)
```

---

## Critic addendum (completeness critic, 2026-10-31)

Probe: `C:/Users/derii/AppData/Local/Temp/claude/acscratch/critic/probe/` modes `spawn <acRoot> [layoutPrefix]`, `ups <trackDir> <kn5..>`,
`fwd <fast_lane.ai> <trackDir> <kn5..>`, `uvy`, `rows`. Raw output `acscratch/critic/spawn.txt`. All 35 available layouts were run.

### T1. Spawn markers float ABOVE the road - never place a car at the node height [VERIFIED, 35/35 layouts]
For every `AC_START_n`, `AC_PIT_n`, `AC_HOTLAP_START_0` the physics surface (digit-prefixed non-WALL meshes, all kn5 of the layout) was
intersected vertically under the node. A surface exists under every node (0 misses) and **the node is always above it**:
| layout | node - surface height (median / min / max, m) |
|---|---|
| magione | 0.17 / 0.00 / 0.47 |
| monza | 0.68 / 0.47 / 0.76 |
| imola, mugello, ks_silverstone/*, drift | 0.55-0.62 / 0.47 / 1.26 |
| spa | 1.11 / 0.85 / 1.19 |
| ks_vallelunga/* | 1.01 / 0.29 / 1.10 |
| ks_highlands/* | 1.35 / 1.30 / 1.61 |
| ks_nurburgring/* | 1.32 / 0.30 / 2.99 |
| ks_zandvoort | 1.77 / 1.18 / 1.91 |
| ks_laguna_seca | 2.51 / 1.12 / 3.22 |
| **ks_black_cat_county/*** | **4.6-5.1 / 2.7 / 7.7** |
| ks_monza66/* | 0.11-0.15 / 0.02 / 0.49 |
| ks_drag/* | 0.84 ; trento-bondone 0.91 / 0.81 / 1.10 |
Rule for the spawner: take the node XZ + heading, **raycast down (from node.y + 1 m) onto the track colliders**, put the wheels on the hit
(body CG at hit + up * (-GRAPHICS_OFFSET.y), see `ac_car_formats.md` C1), settle with the parking brake on. Spawning at the node height
drops cars up to 7.7 m.

### T2. Marker orientation: use only the HORIZONTAL part of node +Z [VERIFIED]
The table in 3.2 ("+Y up") is not universal: **ks_silverstone1967 AC_START_0..19 and AC_HOTLAP_START_0, and ks_black_cat_county
AC_HOTLAP_START_0 (1.kn5 and 3.kn5) are rolled 90 deg: node X = world up, node Y horizontal** (`ups` mode; e.g. silverstone1967 AC_START_0
X=(-0.01,1.00,0.00) Y=(0.17,0.00,0.99) Z=(0.99,0.01,-0.17)). Node +Z is still the travel direction: dot(Z_xz, fast-lane forward) =
0.990-1.000 for all of them (`fwd` mode). So: `heading = atan2(Z.x, Z.z)` of the AC world Z row projected on XZ, up = world up (then the
ground normal from the raycast). Do NOT use `Quaternion.LookRotation(Z, Y)` from section 9.5 for spawn markers without this projection.

### T3. Grid position vs the timing line: lap 1 rule [VERIFIED progress values, rule = design]
Normalised fast-lane progress of the pole slot (AC_START_0; 0 = AC_TIME_0 line):
- **Grid BEFORE the line (progress 0.97-0.9997)**: magione 0.9980, mugello 0.9997, ks_vallelunga/*
  0.999, ks_nurburgring/* 0.999, ks_zandvoort 0.9964, ks_laguna_seca 0.9775, ks_silverstone/national 0.9936, ks_silverstone1967 0.9981,
  ks_black_cat_county/* 0.9965-0.9995.
- **Grid AFTER the line (0.006-0.054)**: monza 0.0535 (last slot 0.0188), spa 0.0176, ks_highlands/* 0.007-0.048.
- On the line (pole at 0.0000, rest of the grid before it): imola (START_1 0.9987), ks_silverstone/gp + international, ks_monza66/* 0.0000-0.0001, drag 0.0006, trento-bondone 0.0001.
- AC_START_0 is always the most advanced slot (pole), AC_START_n go backwards. AC_HOTLAP_START_0 sits 0.44-0.97 of a lap before the line
  (rolling hotlap start). Drift has no fast lane (empty), so no progress there.
=> A race must NOT simply count gate-0 crossings: with the grid before the line the first crossing (seconds after the green) is the start,
not a lap. Rule: track each car's **race distance = laps-since-green + progress**, initialised at the green with its grid progress
(progress > 0.5 -> start at `progress - 1`, i.e. negative); a lap is completed at a gate-0 crossing only when race distance has passed the
next integer. Equivalent: count crossings, then subtract 1 for cars whose grid progress > 0.5. Positions = sort by race distance.

### T4. UV orientation
Proven on track billboards (grass/trees/flags/fences, millions of vertex pairs): see `ac_car_formats.md` C2 - DDS raw upload +
uv = (u, v_stored), Repeat. Section 4.4's BELIEF is now VERIFIED on the data side.

### T5. Still open on tracks
- `models.ini ROTATION` sign/axis rests on 3 gantry observations (100.kn5 only) - check the gantry faces the grid in the first lab shot.
- Start-light sequence timing (engine side, not in files), pit speed limit (no key anywhere in `system/cfg`, `cfg` - grep for PIT/SPEED found
  nothing): both must be chosen (// ours) and confirmed with the user.
- Road shader: `ksMultilayer*` (mask + 4 detail layers) cannot be expressed with URP Lit - the World module needs its own shader carried as
  a carrier material (DREAMPORT_GUIDE shader rule); no spec defines it yet.
