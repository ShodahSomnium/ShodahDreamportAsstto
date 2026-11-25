# Assetto Corsa CAR file formats (verified against the real install)

Research spec for the Assetto Corsa Dreamport implementers. Topic: everything inside `content/cars/<car>/` plus the driver model.
Install used: `E:\SteamLibrary\steamapps\common\assettocorsa` (read-only). Probe source (dotnet console, read-only on the game):
`C:\Users\derii\AppData\Local\Temp\claude\acscratch\carformats\probe\Program.cs` + `Geom.cs` (build: `dotnet build -c Release -o ../bin`,
run `bin/probe.exe <mode> <path>`; modes `kn5 <file> [summary|tree|mats|tex|dummies|names]`, `kn5all <dir>`, `acd <car> [file,file]`,
`acdall`, `ksanim <file>`, `ksall <dir>`, `knh <file>`, `geom <kn5>`, `bones <kn5>`, `dds <kn5|dir>`, `masks <dir>`, `json <dir>`, `json2 <dir>`).

Legend: **[VERIFIED]** = probe code ran against real files and the result is stated; **[READ]** = seen in file bytes/text;
**[BELIEF]** = known from community tools (Content Manager / kn5 converters) or inference, NOT proven here.

---

## 0. Install facts (what a "car the player owns" looks like)

- `content/cars` has **180 folders** [VERIFIED]. Only **86** are full cars (have `data.acd`, kn5s, skins...). The other **94** are
  DLC stubs (ks_porsche_*, ks_toyota_ae86, ks_ferrari_488_gt3 ...) containing only `ui/badge.png`, `ui/dlc_preview.png`,
  `ui/dlc_ui_car.json` [VERIFIED]. **Rule: a car is usable iff `data.acd` (or a `data/` folder) AND the LOD_0 kn5 exist.** Do not list stubs.
- No car in this install has an unpacked `data/` folder (0/180) [VERIFIED]. Mods often ship `data/` instead of `data.acd`; support
  it as a fallback: read `data.acd` if present, else files from `data/` [BELIEF about AC's priority].
- Full car folder (ferrari_458) [READ]:
  ```
  ferrari_458.kn5 (43,542,249 B)  ferrari_458_LOD_1.kn5  ferrari_458_LOD_2.kn5  ferrari_458_LOD_3.kn5  collider.kn5
  data.acd (154,043 B)  driver_base_pos.knh  logo.png (128x128)  body_shadow.png  tyre_0..3_shadow.png
  animations/ car_DOOR_L.ksanim car_DOOR_R.ksanim car_shift_dw.ksanim car_shift_up.ksanim car_wiper.ksanim shift_dw.ksanim shift_up.ksanim steer.ksanim
  sfx/ferrari_458.bank (FMOD)   texture/flames/   ui/ui_car.json ui/badge.png (128x128)
  skins/<skin>/ ui_skin.json preview.jpg livery.png (64x64) skin.ini + *.dds texture overrides
  ```
- **The main kn5 name is NOT always `<folder>.kn5`**: `ks_mazda_miata/mazda_miata.kn5`, variants `ferrari_458_s3/ferrari_458.kn5`,
  `lotus_elise_sc_s1/lotus_elise_sc.kn5` [VERIFIED]. LOD naming varies: `_LOD_1.._LOD_3` (458), `_B/_C/_D` (abarth500),
  `_lod_B/_lod_C/_lod_D` (miata). **Always take file names from `lods.ini` inside data.acd** (section 5).
- Variants (`"parent": "ferrari_458"` in ui_car.json, 19 cars) carry their own full copies (ferrari_458_s3 kn5 is byte-identical
  to ferrari_458's) and their own data.acd encrypted with **their own folder name** [VERIFIED].

---

## 1. KN5 (models) [VERIFIED: 430/430 car kn5 + 18/18 driver kn5 + magione track kn5 parse and consume the file to the exact last byte]

All little-endian. `string` = `int32 byteLength` + UTF-8 bytes (no terminator). Names can contain spaces (`rim blur rr`).

### 1.1 Header
| off | type | value |
|---|---|---|
| 0 | char[6] | `sc6969` |
| 6 | int32 | version: **5 or 6** (installed: v6 x377 files, v5 x53) |
| 10 | int32 | only if version > 5: unknown, **always 0** in this install |

### 1.2 Textures block
`int32 count`, then per texture: `int32 type` (always 1), `string name` (e.g. `Pinzel.dds`), `int32 size`, `byte[size]` raw file
(DDS `"DDS "` or PNG). Car census: 5956 DDS + 215 PNG across all car kn5 [VERIFIED].
ferrari_458.kn5: count 85 at off 14; first record: type@18, nameLen@22=10, name@26 `Pinzel.dds`, size@36=43776, DDS bytes @40;
texture block ends at **38,784,997** (89% of the file is textures) [VERIFIED].
**LOD_1..3 and collider kn5 contain 0 textures** (texEnd=18) and refer to textures by name -> resolve names against the LOD_0 kn5
textures + skin overrides [VERIFIED].

### 1.3 Materials block
`int32 count`, per material:
```
string name; string shaderName;
byte  blendMode;      // 0 opaque, 1 alpha blend (2 = alpha-to-coverage [BELIEF], not seen in cars)
byte  alphaTested;    // bool
int32 depthMode;      // only if version > 4. 0 normal, 1 = no depth write [BELIEF names]; seen 0/1
int32 propCount; per prop: string name; float valueA; float[2] valueB; float[3] valueC; float[4] valueD   // 4 + 40 bytes after name
int32 mapCount;  per map:  string slotName; int32 slot; string textureFileName
```
- valueA holds the normal scalar. **`ksEmissive` colour is in valueC** (e.g. ferrari `INT_Display_Luminosi.ksEmissive` a=0,
  C=(2.3,2.3,2.4)); track multilayer `multR/G/B/A`, `detailNMMult` use valueB [VERIFIED].
- ferrari_458: 60 materials, block ends at 38,832,052. Shaders: ksPerPixel, ksPerPixelNM, ksPerPixelMultiMap,
  ksPerPixelMultiMap_damage_dirt (CARPAINT), ksPerPixelMultiMap_NMDetail, ksPerPixelReflection, ksPerPixelAT, ksPerPixelAT_NM,
  ksPerPixelNM_UVMult, ksBrakeDisc, ksTyres, ksWindscreen, ksBrokenGlass. Others seen: ksSkinnedMesh, ksSkinnedMesh_NMDetaill (sic,
  driver), ksPerPixelSimpleRefl (miata), `GL` (collider.kn5). Track: ksMultilayer, ksMultilayer_fresnel_nm, ksMultilayer_objsp,
  ksTree, ksGrass, ksFlags, ksPerPixelAlpha [VERIFIED].
- blend/alphaTested/depth combos in 458: 0/F/0 x41, 1/F/1 x7 (decals), 0/T/0 x3 (cutout grilles), 1/F/0 x9 [VERIFIED].
- Prop names seen: ksAmbient ksDiffuse ksSpecular ksSpecularEXP ksEmissive ksAlphaRef fresnelC fresnelEXP fresnelMaxLevel nmObjectSpace
  isAdditive useDetail detailUVMultiplier detailNormalBlend shadowBiasMult damageZones dirt sunSpecular sunSpecularEXP blurLevel
  glowLevel dirtyLevel diffuseMult normalMult glassDamage boh bo bones (+ track: multR multG multB multA magicMult
  tarmacSpecularMultiplier detailNMMult frequency distortion scale gain alpha).
- Texture slots seen: txDiffuse@0 txNormal@1 txMaps@2 txDetail@3 txNormalDetail@4 txGlow@2 txBlur@3 txNormalBlur@4 txDirty@2
  txDamage@4 txDust@5 txDamageMask@21 (+ track txMask@1 txDetailR/G/B/A@2..5 txDetailNM@21 txVariation@1).
- Example (verified dump): `MAT_CARPAINT ksPerPixelMultiMap_damage_dirt: ksAmbient=0.45 ksDiffuse=0.5 ksSpecular=1 ksSpecularEXP=50
  fresnelC=0.07 fresnelEXP=3.5 fresnelMaxLevel=0.6 isAdditive=2 useDetail=1 detailUVMultiplier=35 sunSpecular=10 sunSpecularEXP=1500;
  txDiffuse=CARPAINT.dds txNormal=DAMAGE_CHASSIS_NM.dds txMaps=CARPAINT_map.dds txDetail=metal_detail.dds txDamage=Damage.dds
  txDust=dust.dds txDamageMask=damage_mask.dds`. `MAT_GOMMA ksTyres: txDiffuse=wheel.dds txNormal=wheel_NM.dds txDirty=dirt.dds
  txBlur=wheel_blur.dds txNormalBlur=wheel_NM_blur.dds`. `MAT_Dischi ksBrakeDisc: txGlow=DISK_warm.dds txBlur=disc_brake_blur.dds`.
- [BELIEF, standard AC shader semantics] txMaps: R = specular mult, G = gloss (specEXP mult), B = reflection mult; isAdditive 1/2 =
  reflection mode; txDetail tiled by detailUVMultiplier and blended by diffuse alpha (CARPAINT colour comes from the skin's
  metal_detail / CARPAINT textures).

### 1.4 Node tree (single root node, recursive depth-first: node record, then its children)
```
int32 nodeClass;  string name;  int32 childCount;  byte active (bool)
class 1 (dummy/transform): float[16] matrix (local, row-major, row-vector convention, translation at [12],[13],[14])
class 2 (static mesh):
   byte castShadows; byte isVisible; byte isTransparent;
   int32 vertexCount; per vertex 11 floats (44 B): pos.xyz, normal.xyz, uv.xy, tangent.xyz
   int32 indexCount;  ushort[indexCount]  (triangle list)
   int32 materialId (index into materials); int32 layer; float lodIn; float lodOut;
   float[3] boundingSphereCenter; float boundingSphereRadius; byte isRenderable
class 3 (skinned mesh):
   byte castShadows; byte isVisible; byte isTransparent;
   int32 boneCount; per bone: string boneNodeName; float[16] inverseBindMatrix
   int32 vertexCount; per vertex 19 floats (76 B): pos3 normal3 uv2 tangent3 weights4 boneIndices4 (indices stored AS FLOATS, -1 = unused)
   int32 indexCount;  ushort[indexCount]
   int32 materialId; int32 layer; byte[8] unknown (all zero in driver kn5)
```
- Mesh nodes have no matrix: **vertices are in the space of the parent dummy chain** (e.g. `WHEEL_LF444` tyre mesh centred at
  ~(0.036,0,0) under `WHEEL_LF`/`TYRE_LF`) [VERIFIED by the numbers].
- Bone matrix = inverse of the bone node's world matrix in the kn5's own node tree: `boneMatrix * world(boneNode) = I` to 1e-5 for
  all checked bones of driver_no_HANS.kn5 (Gloves 36 bones, suit 21 bones) [VERIFIED]. (shelby_cobra `GEAR_bone1` differs by 0.47,
  i.e. the node was moved after binding - use the stored matrices as bindposes, not the node tree.)
- Skinned vertex example (driver Gloves): weights (0.793,0.207,0,0), indices (19,23,-1,-1) [VERIFIED].
- Cars: all meshes layer 0, lodIn=lodOut=0, isVisible=1, isRenderable=1. Flags in 458: cast1/transp0 x137, cast0/transp0 x20,
  cast1/transp1 x11, cast0/transp1 x21. 14 nodes `active=0` (RIM_BLUR_*, COCKPIT_LR, DAMAGE_GLASS_*) - **inactive = start hidden**
  [VERIFIED]. Some cars have skinned meshes inside the car kn5 (shifter boots: abarth500 x2 `ksSkinnedMesh`, miata `gear_leather_SKINNED`,
  shelby_cobra `GEAR_bone0/1`, lotus_*) [VERIFIED].
- Counts [VERIFIED]:
  | file | ver | tex | mats | dummies | meshes | skinned | verts | indices |
  |---|---|---|---|---|---|---|---|---|
  | ferrari_458.kn5 | 6 | 85 | 60 | 150 | 189 | 0 | 89,325 | 376,434 |
  | ferrari_458_LOD_1.kn5 | 6 | 0 | 25 | 90 | 68 | 0 | 13,973 | 44,583 |
  | ferrari_458_LOD_3.kn5 | 6 | 0 | 5 | 10 | 5 | 0 | 4,250 | 12,258 |
  | ferrari_458/collider.kn5 | 6 | 0 | 1 (`GL`) | 2 | 1 | 0 | 42 | 240 |
  | abarth500.kn5 | 6 | 80 | 59 | 165 | 180 | 2 | 80,144 | 344,619 |
  | abarth500_D.kn5 | 6 | 0 | 11 | 34 | 26 | 0 | 2,770 | 7,281 |
  | ks_mazda_miata/mazda_miata.kn5 | 6 | 68 | 46 | 242 | 177 | 2 | 169,450 | 663,171 |
  | driver/driver_no_HANS.kn5 | 6 | 17 | 7 | 63 | 6 | 2 | 6,901 | 33,846 |
  | tracks/magione/magione.kn5 (312.8 MB) | 6 | 84 | 47 | 188 | 959 | 0 | 3,318,059 | 7,142,628 |
- Track kn5 differences [VERIFIED]: layers 0/3/4/5 used, real lodIn/lodOut distances (e.g. 0-350, 20-100, 140-450), 216 meshes
  `isRenderable=0` (physics-only surfaces), surface meshes named `<digits><KEY>` e.g. `01TARMACA`, `01CURB`, `01GRASS` matching
  `data/surfaces.ini` `KEY=TARMACA`; dummies `AC_START_0..17`, `AC_PIT_0..17`, `AC_HOTLAP_START_0`, `AC_TIME_0_L/R`, `AC_TIME_1_L/R`,
  `AC_CREW_*`, `AC_AUDIO_*`. `models.ini` lists the kn5 files of a track (magione: magione.kn5 + 100.kn5; imola: imola.kn5, 1.kn5,
  2.kn5, 100.kn5). Parse of 312 MB took 1.5 s streaming (skipping vertex data).

### 1.5 Coordinate system and conversion to Unity [VERIFIED by data]
- AC is **right-handed, +Y up, +Z forward (car front), +X = car's LEFT**. Evidence: WHEEL_LF translation (+0.846, 0.336, +1.154),
  WHEEL_LR (+0.808, 0.358, -1.495), FRONT_BUMPER z=+2.15, REAR_BUMPER z=-2.17; the LHD 458's STEER_HR x=+0.343, car.ini
  DRIVEREYES=0.3447,0.986,-0.168 and the knh driver RIG_Center x=+0.355 (driver sits on the left).
- Matrices: 16 floats row-major, **row-vector convention** (`p' = p * M`), translation in elements [12],[13],[14] (m41..m43), [15]=1.
  Evidence: translations only ever in 12..14; STEER_HR rows (1,0,0)/(0,0.943,0.334)/(0,-0.334,0.943) = local Y tilted toward +Z
  (top of the wheel leans forward, which is physically right); world = local * parentWorld reproduces bone bindposes exactly.
- Winding: for 125,460 of 125,474 ferrari triangles (14 degenerate), `cross(b-a, c-a)` points along the vertex normals (same for
  abarth500 and 11632/11632 driver) [VERIFIED]. Unity's front face also satisfies `cross(b-a,c-a)` = outward normal, so after the
  X mirror the order is wrong -> **convert = negate X of positions/normals/tangents AND swap indices 1<->2 of every triangle**.
- Matrix conversion: `U = S * transpose(M_ac) * S`, S = diag(-1,1,1,1). Equivalently: transpose, then negate m01,m02,m10,m20,m03(tx).
  Quaternion (from ksanim v2, Hamilton x,y,z,w): Unity `(x, -y, -z, w)`; position `(-x, y, z)`.
- UV: stored V is mostly negative (driver UVs u in [0.002,0.998], **v in [-0.997,-0.004]**; car 87,650 of 89,325 vertices have v<0)
  [VERIFIED]. AC (DX11) samples with wrap, so DX-style v_dx = frac(v_stored) [BELIEF]. Consequences [BELIEF, check visually on the
  first build]: a DDS uploaded raw with `Texture2D.LoadRawTextureData` keeps row 0 (image top) at Unity v=0, i.e. use **uv = (u, v_stored)**
  with Repeat wrap; for textures decoded upright (PNG via `ImageConversion.LoadImage`) use **uv = (u, -v_stored)**. Keep ONE convention for
  all textures (simplest: flip PNG rows after decoding and always use (u, v_stored)).
- Tangents: the stored tangent points along **-dP/du** for most triangles (driver: 645 agree / 10,937 against; 458: 30,486 / 91,640)
  [VERIFIED]. After the X mirror use `tangent = (-tx, ty, tz, w)` and pick w by checking one triangle, or just call
  `mesh.RecalculateTangents()` (simplest, recommended).

### 1.6 Important node names (dummies) actually found [VERIFIED dumps]
**ferrari_458** (root `FBX: FERRARI_458_italia_LOD_0.fbx`):
`WHEEL_LF/RF/LR/RR` > `TYRE_xx` (mesh WHEEL_LF44x, ksTyres) + `RIM_xx` + `RIM_BLUR_xx` (inactive); `SUSP_LF/RF/LR/RR` (same pivot as
wheels, carry calipers); `DISC_LF/RF/LR/RR` > `GEO_DISC_xx`; `COCKPIT_HR` (active; high-res interior) > `STEER_HR` > `LED_0..4`,
`GEO_Steer_HR`; `ARROW_RPM`, `ARROW_SPEED` (needles), `ARROW_LED_RPM`, `DISPLAY_DATA`, `DISPLAY_DATA1` (digital_instruments PARENT),
`SHIFT_PAD_L/R` (paddles), `FUEL_LED_0..8`, `CINTURE_ON/OFF` (belts), `DOOR_L1/DOOR_R1` (interior door halves), `GEO_DISPLAY_FRONT`;
`COCKPIT_LR` (inactive; low-res interior) > `STEER_LR`; `DOOR_L/DOOR_R` (pivot 0.938,0.632,0.725); `FRONT_BUMPER`, `REAR_BUMPER`,
`MOTORHOOD`, `FRONT_LIGHTS` > `FRONT_LIGHT_1/2/3`; `REAR_LIGHTS` > `REAR_LIGHT_REAR`, `REAR_LIGHT_BRAKE`, `REAR_LIGHT_BRAKE_CENTRAL`;
`REAR_LIGHT_POSITION`; `WIPER_L/R` > `TERGI`; `EXHAUST1..3`; `FLYCAM_L_0..5`, `FLYCAM_R_0..4` (camera spots);
`DAMAGE_GLASS_FRONT_1/LEFT_1/LEFT_2/RIGHT_1/RIGHT_2/REAR_1/CENTER_1/CENTER_2` (inactive); `Plate_LODA`.
**abarth500**: same WHEEL/TYRE/RIM/RIM_BLUR/SUSP/DISC/COCKPIT_HR/COCKPIT_LR/STEER_HR/STEER_LR set plus `SHIFT_HR` > `BONE_SHIFT_1/2`
(skinned shifter), `INT_DOOR_L/R`, `ARROW_TURBO`, `WATER_LED_1..9`, `SHIFT_UP_LIGHT`, `LIGHT_DASH`, `WIPER_REAR`, `FRONT_LIGHT`,
`LIGHT_FRONT`, `LIGHT_BRAKE`, `LIGHT_REAR`, `LIGHT_POSITION`, `Side_lights`.
**ks_mazda_miata**: `WHEEL_*`, `RIM_*`, `RIM_BLUR_*`, `TYRE_*`, `SUSP_*` (+ `GEO_HUB_*`, `GEO_SPRING_*`, `DIR_arm*` suspension
arms), `COCKPIT_HR`, `STEER_HR`, `ARROW_SPEED/RPM/FUEL/WATER/OIL`, `SHIFTER_1/2/SHIFTER_BASE` + `gear_leather_SKINNED`,
`DOOR_L/DOOR_R` + `DOOR_L001/002`, `DOOR_R001/002`, `BUMPER_FRONT/BUMPER_REAR` (not FRONT_BUMPER!), `HEADLIGHT_HB`, `FRONT_LIGHT_BEAM`,
`wiper_L/wiper_R` (lowercase), `MOTORHOOD`, `mirror_left/middle/right`, `COCKPIT_LR`, `STEER_LR`.
**Driver hooks are named in data.acd, not fixed**: lights.ini (`NAME=REAR_LIGHT_BRAKE`, `FRONT_LIGHT_1`, `GEO_DISPLAY_FRONT`, ...),
blurred_objects.ini (`RIM_LF` 0-40 km/h / `RIM_BLUR_LF` 30+ km/h, WHEEL_INDEX 0=LF 1=RF 2=LR 3=RR), digital_instruments.ini
(PARENT=DISPLAY_DATA, LED_n OBJECT_NAME), analog_instruments.ini (OBJECT_NAME=ARROW_SPEED/ARROW_RPM, ZERO, STEP deg per unit, LUT),
mirrors.ini (mesh names: 458 `polymsh5_SUB2`, `polymsh4_SUB2`, `GEO_Cockpit_SUB0`), lods.ini `[COCKPIT_HR] DISTANCE_SWITCH=7`
(switch COCKPIT_HR<->COCKPIT_LR at 7 m), driver3d.ini HIDE_OBJECT. Wheel/steer/susp/door/cockpit names (WHEEL_xx, SUSP_xx, DISC_xx,
STEER_HR/LR, COCKPIT_HR/LR, DOOR_L/R, RIM_BLUR_xx) are the hard-coded convention and were present in all three cars.

### 1.7 DDS formats inside car kn5 (needed by the texture loader) [VERIFIED census, all cars]
DXT5 2465 (927 without mips), DXT1 741, DXT3 306, PNG 215, and uncompressed: B8G8R8 24-bit (masks R FF0000 G FF00 B FF) 991,
B8G8R8A8 32-bit 710, L8A8 16-bit (pf flags 0x20001) 581, L8 (0x20000) 124, X1R5G5B5 (0x40, R 7C00) 33, A4R4G4B4 2, R8G8B8A8
(R mask FF) 2, A8 (0x20) 1. No DX10 headers, no cubemaps in car kn5. Many textures have mipCount <= 1 -> generate mips in Unity.
Unity has no DXT3 format: decode DXT3 on the CPU (or to RGBA32). Header: dwHeight@12 dwWidth@16 mipMapCount@28 pfFlags@80 fourCC@84
rgbBitCount@88 masks@92/96/100/104 caps2@112, data @128.

### 1.8 Verified C# reader (C# 9, Unity-safe: no Span/unsafe; same logic as the probe that parsed 449 files exactly)
```csharp
public sealed class Kn5Texture { public string Name; public byte[] Bytes; }
public sealed class Kn5Material {
    public string Name, Shader; public byte BlendMode; public bool AlphaTested; public int DepthMode;
    public Dictionary<string, float> A = new Dictionary<string, float>();      // valueA
    public Dictionary<string, float[]> Vec = new Dictionary<string, float[]>(); // 9 floats: B(0..1) C(2..4) D(5..8)
    public Dictionary<string, string> Tex = new Dictionary<string, string>();   // txDiffuse -> file name
}
public sealed class Kn5Node {
    public int Class; public string Name; public bool Active; public float[] Matrix; // class 1
    public bool CastShadows, IsVisible, IsTransparent, IsRenderable;
    public float[] Verts; public int Stride; public ushort[] Indices;               // Stride 11 (class 2) or 19 (class 3)
    public int MaterialId, Layer; public float LodIn, LodOut, Radius; public float[] Center;
    public string[] BoneNames; public float[][] BoneBind;                            // class 3
    public List<Kn5Node> Children = new List<Kn5Node>();
}
public sealed class Kn5 {
    public int Version; public List<Kn5Texture> Textures = new List<Kn5Texture>();
    public List<Kn5Material> Materials = new List<Kn5Material>(); public Kn5Node Root;

    static string Str(BinaryReader r) { int n = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
    static float[] Floats(BinaryReader r, int n) {
        var b = r.ReadBytes(n * 4); var f = new float[n]; Buffer.BlockCopy(b, 0, f, 0, b.Length); return f; }

    public static Kn5 Read(string path, bool loadTextures = true) {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (var r = new BinaryReader(fs)) {
            if (Encoding.ASCII.GetString(r.ReadBytes(6)) != "sc6969") throw new InvalidDataException("not kn5");
            var k = new Kn5 { Version = r.ReadInt32() };
            if (k.Version > 5) r.ReadInt32();                       // unknown, 0
            int tc = r.ReadInt32();
            for (int i = 0; i < tc; i++) {
                r.ReadInt32();                                     // type (1)
                var t = new Kn5Texture { Name = Str(r) }; int size = r.ReadInt32();
                if (loadTextures) t.Bytes = r.ReadBytes(size); else fs.Position += size;
                k.Textures.Add(t);
            }
            int mc = r.ReadInt32();
            for (int i = 0; i < mc; i++) {
                var m = new Kn5Material { Name = Str(r), Shader = Str(r) };
                m.BlendMode = r.ReadByte(); m.AlphaTested = r.ReadByte() != 0;
                if (k.Version > 4) m.DepthMode = r.ReadInt32();
                int pc = r.ReadInt32();
                for (int p = 0; p < pc; p++) { string pn = Str(r); m.A[pn] = r.ReadSingle(); m.Vec[pn] = Floats(r, 9); }
                int mapc = r.ReadInt32();
                for (int t = 0; t < mapc; t++) { string slot = Str(r); r.ReadInt32(); m.Tex[slot] = Str(r); }
                k.Materials.Add(m);
            }
            k.Root = ReadNode(r);
            return k;
        }
    }
    static Kn5Node ReadNode(BinaryReader r) {   // recursion depth in real files < 20
        var n = new Kn5Node { Class = r.ReadInt32(), Name = Str(r) };
        int kids = r.ReadInt32(); n.Active = r.ReadByte() != 0;
        if (n.Class == 1) n.Matrix = Floats(r, 16);
        else if (n.Class == 2 || n.Class == 3) {
            n.CastShadows = r.ReadByte() != 0; n.IsVisible = r.ReadByte() != 0; n.IsTransparent = r.ReadByte() != 0;
            if (n.Class == 3) {
                int bc = r.ReadInt32(); n.BoneNames = new string[bc]; n.BoneBind = new float[bc][];
                for (int b = 0; b < bc; b++) { n.BoneNames[b] = Str(r); n.BoneBind[b] = Floats(r, 16); }
            }
            n.Stride = n.Class == 2 ? 11 : 19;
            int vc = r.ReadInt32(); n.Verts = Floats(r, vc * n.Stride);
            int ic = r.ReadInt32(); var ib = r.ReadBytes(ic * 2); n.Indices = new ushort[ic]; Buffer.BlockCopy(ib, 0, n.Indices, 0, ib.Length);
            n.MaterialId = r.ReadInt32(); n.Layer = r.ReadInt32();
            if (n.Class == 2) {
                n.LodIn = r.ReadSingle(); n.LodOut = r.ReadSingle(); n.Center = Floats(r, 3); n.Radius = r.ReadSingle();
                n.IsRenderable = r.ReadByte() != 0;
            } else { r.ReadBytes(8); n.IsRenderable = true; }
        } else throw new InvalidDataException("kn5 node class " + n.Class);
        for (int i = 0; i < kids; i++) n.Children.Add(ReadNode(r));
        return n;
    }
}
```
Unity conversion helpers (from 1.5):
```csharp
// AC row-major row-vector matrix -> Unity local TRS-able Matrix4x4 (X mirrored)
static Matrix4x4 AcToUnity(float[] m) {
    var u = new Matrix4x4();
    for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) u[row, col] = m[col * 4 + row]; // transpose
    // S*U*S with S=diag(-1,1,1,1): negate entries where exactly one of (row,col) is 0
    u.m01 = -u.m01; u.m02 = -u.m02; u.m03 = -u.m03; u.m10 = -u.m10; u.m20 = -u.m20; u.m30 = -u.m30;
    return u;   // then transform.localPosition = u.GetColumn(3); localRotation = u.rotation; localScale = u.lossyScale
}
// vertex i (stride s): pos = (-v[o], v[o+1], v[o+2]); normal = (-v[o+3], v[o+4], v[o+5]); uv = (v[o+6], v[o+7]) (see 1.5 UV note)
// triangles: for (t = 0; t < idx.Length; t += 3) { tri[t] = idx[t]; tri[t+1] = idx[t+2]; tri[t+2] = idx[t+1]; }
// skinned: weights v[o+11..14], bone indices (int)v[o+15..18] (-1 -> weight 0, index 0); bindposes = AcToUnity(BoneBind[b])
```
Note: some AC matrices contain negative scale (FRONT_BUMPER/REAR_BUMPER rows (-1,0,0),(0,1,0),(0,0,-1) = 180 deg yaw, fine; GEO_DISC_LR
has scale 1.062). Decomposing to TRS works for these; if a determinant < 0 shows up, bake the matrix into the vertices instead.

---

## 2. data.acd (encrypted physics/config archive) [VERIFIED: 86/86 installed cars decrypt; every car.ini starts with `[HEADER]`/`[BASIC]`]

Layout (no global header in any installed car; the optional `int32 -1111` + `int32` header is handled as CM does [BELIEF]):
```
repeat until EOF:
  int32 nameLength; byte[nameLength] name (ASCII, e.g. "aero.ini")
  int32 length                       // decrypted byte count
  int32[length]                      // each int32 holds one encrypted byte in its LOW byte (other 3 bytes are 0)
  plain[i] = (byte)(lowByte[i] - key[i % key.Length])   // key = ASCII bytes of the key string
```
ferrari_458/data.acd bytes 0..: `08000000 "aero.ini" 7D0B0000 (2941) 8D000000 ...`; key "235-167-105-254-54-143-17-57", key[0]='2'(50),
0x8D(141)-50 = 91 = `[` [VERIFIED]. Key for ks_mazda_miata = "181-123-119-57-70-14-64-98" [VERIFIED].

Key = 8 integer factors of the **lower-cased car folder name**, each `& 0xFF`, joined with `-` (C# int arithmetic, wraps, integer division):
```csharp
public static string AcdKey(string carFolderName) {
    string s = carFolderName.ToLowerInvariant();
    int f0 = 0;      for (int i = 0; i < s.Length; i++) f0 += s[i];
    int f1 = 0;      for (int i = 0; i < s.Length - 1; i += 2) { f1 *= s[i]; f1 -= s[i + 1]; }
    int f2 = 0;      for (int i = 1; i < s.Length - 3; i += 3) { f2 *= s[i]; f2 /= s[i + 1] + 0x1b; f2 += -0x1b - s[i - 1]; }
    int f3 = 0x1683; for (int i = 1; i < s.Length; i++) f3 -= s[i];
    int f4 = 0x42;   for (int i = 1; i < s.Length - 4; i += 4) f4 = (s[i] + 0xf) * f4 * (s[i - 1] + 0xf) + 0x16;
    int f5 = 0x65;   for (int i = 0; i < s.Length - 2; i += 2) f5 -= s[i];
    int f6 = 0xab;   for (int i = 0; i < s.Length - 2; i += 2) f6 %= s[i];
    int f7 = 0xab;   for (int i = 0; i < s.Length - 1; i++) { f7 /= s[i]; f7 += s[i + 1]; }
    return string.Join("-", new[] { f0, f1, f2, f3, f4, f5, f6, f7 }.Select(x => (x & 0xff).ToString()));
}
public static Dictionary<string, byte[]> ReadAcd(string acdPath, string carFolderName) {
    byte[] key = Encoding.ASCII.GetBytes(AcdKey(carFolderName));
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    using (var r = new BinaryReader(File.OpenRead(acdPath))) {
        if (r.ReadInt32() == -1111) r.ReadInt32(); else r.BaseStream.Position = 0;
        while (r.BaseStream.Position < r.BaseStream.Length) {
            string name = Encoding.ASCII.GetString(r.ReadBytes(r.ReadInt32()));
            int len = r.ReadInt32(); byte[] raw = r.ReadBytes(len * 4); var plain = new byte[len];
            for (int i = 0; i < len; i++) plain[i] = (byte)(raw[i * 4] - key[i % key.Length]);
            files[name] = plain;
        }
    }
    return files;
}
```
(`.Select` needs `System.Linq`; fine in Unity.) The car folder name is the key - use the folder name exactly as on disk, lower-cased.

ferrari_458 data.acd = **58 files** [VERIFIED]: aero.ini ai.ini ambient_shadows.ini analog_instruments.ini blurred_objects.ini brakes.ini
cameras.ini car.ini colliders.ini ctrl_single_lock.ini damage.ini dash_cam.ini difflock_brake_mult.lut difflock_gear_mult.lut
digital_instruments.ini driver3d.ini drivetrain.ini electronics.ini engine.ini escmode.ini fuel_cons.ini gear_start.lut height_*.lut
lights.ini lods.ini mirrors.ini power.lut proview_nodes.ini semislicks_front/rear.lut setup.ini sounds.ini street_front/rear.lut
suspensions.ini suspension_graphics.ini (empty) tcurve_semis.lut tcurve_street.lut throttle.lut traction_control.lut tyres.ini
understeer_factor_vs_lock.lut wing_animations.ini (empty) wing_*_AOA_CD/CL.lut, plus 5-byte dummy body_shadow.png/tyre_N_shadow.png.
In ALL 86 cars [VERIFIED]: aero.ini ai.ini ambient_shadows.ini blurred_objects.ini brakes.ini cameras.ini car.ini colliders.ini damage.ini
dash_cam.ini driver3d.ini drivetrain.ini electronics.ini engine.ini escmode.ini fuel_cons.ini lights.ini lods.ini mirrors.ini power.lut
proview_nodes.ini setup.ini sounds.ini suspensions.ini throttle.lut tyres.ini wing_body_aoa_cd/cl.lut. Common: digital_instruments.ini 80,
analog_instruments.ini 79, drs.ini 56, wing_animations.ini 60, flames.ini 26, *.rto gear ratio files, extra_animations.ini 2, kers.ini 1.
Files whose names were left by Kunos (`suspensions - copy.ini`, `tyres (stefano casillo's conflicted copy ...).ini`) exist - only load the
names referenced from the main inis.

Text quirks [READ]: INI with `;` comments after values (often tab-separated), mixed LF and CRLF endings, empty files (0 bytes). LUT
format `x|y` per line (power.lut: `-3000|100 ... 9500|0`). Inline LUTs in inis: `LUT=(0=0|1000=24|3000=98)`.

Useful verified values (ferrari_458): car.ini `GRAPHICS_OFFSET=0.0,-0.45,0.34` (shift the 3D model vs the physics body),
`GRAPHICS_PITCH_ROTATION=-0.8`, `TOTALMASS=1578`, `[GRAPHICS] DRIVEREYES=0.344656,0.986471,-0.167752`, `STEER_LOCK=360`,
`STEER_RATIO=11.9`; lights.ini v2 `[BRAKE_0] NAME=REAR_LIGHT_BRAKE COLOR=200,0,0 OFF_COLOR=50,0,0`, `[LIGHT_0] NAME=FRONT_LIGHT_1
COLOR=1320,1200,1080`; colliders.ini `[COLLIDER_0] CENTRE=0,-0.285,0.3 SIZE=1.75,0.10,3.5 GROUND_ENABLE=1`; dash_cam.ini
`POS=0.344656,0.974783,0.153423`; sounds.ini `[ENGINE] POSITION=rear`; ambient_shadows.ini `WIDTH=1.0 LENGTH=2.8`.

---

## 3. ksanim (animations/*.ksanim) [VERIFIED: 654/654 files in content parse to the exact byte: v1 x195, v2 x459]
```
int32 version (1 or 2); int32 nodeCount
per node: string nodeName; int32 frameCount; frames:
   v1: float[16] local matrix per frame (same convention as kn5 dummies)
   v2: float[10] per frame = rotation quaternion (x,y,z,w), translation (x,y,z), scale (x,y,z)
```
- All nodes of a file have the same frameCount (100 in every file checked). Frames are LOCAL transforms that replace the node's kn5
  matrix (frame 0 of car_DOOR_L `DOOR_L` == the kn5 DOOR_L matrix, translation 0.938,0.632,0.725) [VERIFIED].
- Quaternion convention cross-checked: steer.ksanim (v2) `RIG_Hips` q=(0.171,0,-0.003,0.985) equals the rotation of shift_up.ksanim (v1)
  `RIG_Hips` matrix rows (1,..)/(0.006,0.941,0.337)/(0,-0.337,0.941) (19.7 deg about X) -> Hamilton quaternion, matrix = its transpose
  in row-vector form [VERIFIED].
- ferrari_458 files [VERIFIED]: `car_DOOR_L` v1 3 nodes (DOOR_L, COCKPIT_HR, DOOR_L1; last frame = 65.7 deg yaw), `car_DOOR_R` same,
  `car_shift_up/dw` v1 2 nodes (COCKPIT_HR, SHIFT_PAD_R / _L), `car_wiper` v1 4 nodes (WIPER_L, WIPER_L1, WIPER_R, WIPER_R1),
  `steer.ksanim` **v2 60 nodes `DRIVER:*`** (driver arms/hands/fingers), `shift_up/dw.ksanim` v1 25 `DRIVER:*` nodes (driver hand shift).
- Other cars: `car_door_L/R`, `car_shift`, `car_wiper`, `shift`, `steer`, `lights` (miata pop-up lights), lotus_49 `CAR_SUSP_LF/RF/LR/RR`
  (v2, suspension arm motion: BRAC_*, HUB_LF, MESH_*). Names are case-insensitive on Windows (`car_DOOR_L` vs `car_door_L`).
- Playback [BELIEF, AC behaviour]: `car_*` = car-node animations, time 0..1 maps to frame 0..frameCount-1 (door open, wiper sweep, paddle);
  `steer.ksanim` frame = (steerAngle/LOCK * 0.5 + 0.5) * (frames-1) with LOCK from driver3d.ini `[STEER_ANIMATION] LOCK=360`;
  shift anims timed by driver3d.ini `[SHIFT_ANIMATION] BLEND_TIME / POSITIVE_TIME / STATIC_TIME / NEGATIVE_TIME` (ms).
- Reader (C# 9):
```csharp
public static Dictionary<string, float[][]> ReadKsAnim(string path, out int version) {
    var res = new Dictionary<string, float[][]>();
    using (var r = new BinaryReader(File.OpenRead(path))) {
        version = r.ReadInt32(); int nc = r.ReadInt32(); int per = version == 1 ? 16 : 10;
        for (int i = 0; i < nc; i++) {
            string name = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
            int fc = r.ReadInt32(); var frames = new float[fc][];
            for (int f = 0; f < fc; f++) { var b = r.ReadBytes(per * 4); frames[f] = new float[per]; Buffer.BlockCopy(b, 0, frames[f], 0, b.Length); }
            res[name] = frames;
        }
    }
    return res;
}
```

---

## 4. Driver: driver_base_pos.knh, content/driver/*.kn5, driver3d.ini

### 4.1 driver_base_pos.knh [VERIFIED: ferrari_458 file 6480 B = 71 nodes, consumed exactly]
Recursive, depth-first: `string name; float[16] localMatrix; int32 childCount; children...` (no header).
Tree: `SCENE_ROOT` > `MODEL: Pilota_posizione_BASE_FERRARI_458_Italia.fbx` > `FBX: ...` > `DRIVER:DRIVER` > `DRIVER:RIG_Center`
(T=0.355,0.319,0.009 = seat position in car space, left seat) > RIG_Hips > RIG_Cest > RIG_Nek > RIG_Head > HELMET...;
RIG_Clave_L/R > RIG_Arm > RIG_ForeArm > RIG_ForeArm_END > RIG_HAND > HAND_L_Thumb1..3 / HAND_Index1..3 / Middle / Ring / Pinkie (right
hand: HAND_R_Thumb1..3, HAND_Index4..6, Middle4..6, Ring4..6, Pinkie4..6); RIG_Shoulder_L/R; RIG_Leg_L/R > RIG_Shin > RIG_Hill > RIG_Foot;
plus `DRIVER:Driver_Body` (T=0.01,0.658,1.263).
Use [BELIEF, CM does the same]: load the driver kn5, then overwrite the local matrix of every driver kn5 node whose name matches a knh node
with the knh matrix -> the driver is seated for this car (driver kn5's own RIG_Center is T=0,0.486,0.118 = generic pose). Then apply
steer.ksanim (same `DRIVER:*` names) on top. The knh is in car-model space (apply the same GRAPHICS_OFFSET as the car model [BELIEF]).

### 4.2 Driver models (content/driver) [VERIFIED]
`driver.kn5 driver_B.kn5 driver_60.kn5 driver_60_B.kn5 driver_70(_B).kn5 driver_80(_B).kn5 driver_no_HANS(_B).kn5 driver_back.kn5
driver_lod_b.kn5 driver_ocolus.kn5 new_driver.kn5 2016_Driver.kn5`, `Drivers_oculus/` (extra driver3d(...).ini + kn5), `driver_skins/`
(suit/helmet/glove dds by name, used by skin.ini). All 18 parse (v6 x12, v5 x6). driver_no_HANS.kn5: 63 dummies (DRIVER:* bone names
identical to the knh), 6 static meshes (HELMET_SUB0/1, HELMET_GLASS_SUB0/1, HELMET_plastic, GEO_Driver_FACE), 2 skinned:
`DRIVER:Gloves` (2330 v, 36 bones, ksSkinnedMesh_NMDetaill, 2016_Gloves_DIFF.dds) and `DRIVER:suit` (2488 v, 21 bones,
2016_Suit_DIFF.dds).

### 4.3 driver3d.ini (in data.acd) [READ]
ferrari_458: `[MODEL] NAME=driver_no_HANS POSITION=0,0,0` -> `content/driver/driver_no_HANS.kn5`; `[STEER_ANIMATION] NAME=steer.ksanim
LOCK=360`; `[SHIFT_ANIMATION] BLEND_TIME=100 POSITIVE_TIME=150 STATIC_TIME=10 NEGATIVE_TIME=150 PRELOAD_RPM=8500 INVERT_SHIFTING_HANDS=0`;
`[HIDE_OBJECT_0] NAME=DRIVER:HELMET`, `[HIDE_OBJECT_1] NAME=DRIVER:GEO_Driver_FACE` (hidden in first-person cockpit view).
ks_mazda_miata: `POSITION=0,0,0.5`, POSITIVE_TIME=300, STATIC_TIME=102, PRELOAD_RPM=6870.
There are no explicit hand-position keys; hands come from steer.ksanim + knh. For the Dreamport the player's own avatar sits in the seat;
the AC driver model is optional (AI/remote cars without a Somnium avatar) - its seat position (knh RIG_Center, car.ini DRIVEREYES) is
the reference for placing the avatar.

---

## 5. LODs and collider [VERIFIED]
- `lods.ini` (in data.acd) is the authority: ferrari_458 `[LOD_0] FILE=ferrari_458.kn5 IN=0 OUT=15`, `LOD_1 ..._LOD_1.kn5 15-45`,
  `LOD_2 45-201`, `LOD_3 201-2000`, `[COCKPIT_HR] DISTANCE_SWITCH=7`; miata `LOD_0 mazda_miata.kn5 0-13`, `LOD_1 mazda_miata_lod_B.kn5
  13-45`, `LOD_2 _lod_C 45-220`, `LOD_3 _lod_D 220-2000`, plus `[DRIVER_HR] DISTANCE_SWITCH=25`. Distances in metres.
- LOD kn5s have no textures and fewer nodes but keep the key dummies (WHEEL_*, etc.) so wheels can still be animated. Recommended: LOD_0 for
  the local player's car, LOD_1/2 for other networked cars (map with Unity LODGroup by distance).
- `collider.kn5` = one mesh `collider` (42 verts, 240 indices, material `INT_Skin_Base` shader `GL`, root `FBX: 458_collider_shape.fbx`):
  a low-poly convex-ish hull of the car body for car-vs-car/wall collision -> Unity MeshCollider (convex). The physics ground contact box
  is separate: colliders.ini `CENTRE/SIZE` boxes.

---

## 6. UI and skins

### 6.1 ui/ui_car.json [VERIFIED]
Keys (86 full cars): name, brand, description, tags (array), class, specs {bhp, torque, weight, topspeed, acceleration, pwratio, range(64)},
torqueCurve, powerCurve (arrays of `["rpm","value"]` STRING pairs), parent (19 variants). DLC stubs use `ui/dlc_ui_car.json` (+ `url`).
ferrari_458: name "Ferrari 458 Italia", brand "Ferrari", class "street", tags ["#Supercars","rwd","semiautomatic","street","supercar",
"italy"], specs bhp "570bhp", torque "540Nm", weight "1573kg", topspeed "325+km/h", acceleration "3.3s 0-100", pwratio "2.76kg/hp".
Descriptions contain HTML (`<br>`, `</br>`).
**JSON quirks** [VERIFIED over 1142 ui_car/ui_skin files]: 171 fail a strict parser - 161 have raw CR/LF/TAB inside strings
(multi-line descriptions), 10 start with a UTF-8 BOM. A sanitizer (strip BOM, escape control chars inside strings, keep invalid `\x` escapes
as literal backslash) + trailing-comma tolerance parses **1186/1186** ui json files (cars + tracks) [VERIFIED]. System.Text.Json is not
available in the Unity project (no new assembly refs) and JsonUtility can't do dictionaries -> write a small tolerant JSON reader
(MiniJSON style) and run this sanitizer first:
```csharp
static string SanitizeAcJson(string s) {
    if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
    var sb = new StringBuilder(s.Length); bool inStr = false, esc = false;
    foreach (char c in s) {
        if (inStr) {
            if (esc) { esc = false; if ("\"\\/bfnrtu".IndexOf(c) < 0) sb.Append('\\'); sb.Append(c); continue; }
            if (c == '\\') { esc = true; sb.Append(c); continue; }
            if (c == '"') { inStr = false; sb.Append(c); continue; }
            if (c < 0x20) { sb.Append(c == '\n' ? "\\n" : c == '\t' ? "\\t" : c == '\r' ? "" : " "); continue; }
            sb.Append(c);
        } else { if (c == '"') inStr = true; sb.Append(c); }
    }
    return sb.ToString();   // then parse allowing trailing commas
}
```
Images: `ui/badge.png` 128x128 (brand badge), `logo.png` 128x128 (car folder root), DLC stubs: `ui/dlc_preview.png`.

### 6.2 skins/<skin>/ [VERIFIED listing; override rule BELIEF]
- ferrari_458 has 10 skins (`00_rosso_scuderia` ... `09_bianco_avus`); order = folder name / `priority` in ui_skin.json.
- `ui_skin.json`: `{ "skinname": "Rosso Scuderia ", "drivername": "", "country": "", "team": "", "number": "0", "priority": 1 }`.
- `preview.jpg` (showroom shot, use in the tablet car picker), `livery.png` 64x64 (colour swatch icon).
- `skin.ini`: `[driver_no_HANS] SUIT=\sparco\white GLOVES=\sparco_roadcars_rg3.1\red HELMET=\helmet_base_white\7`, `[CREW] SUIT=\type1\red_black
  HELMET=\red BRAND=\ferrari` (paths into content/driver/driver_skins and the pit crew textures).
- Texture overrides: every `*.dds`/`*.png` in the skin folder replaces the kn5 embedded texture **with the same file name
  (case-insensitive)**. Verified the names match kn5 textures: 00_rosso_scuderia `CARPAINT_map.dds`, `Plate_D.dds`, `metal_detail.dds`,
  `metal_detail_roof.dds` all exist in ferrari_458.kn5; 01_rosso_scuderia_dark_top also overrides `F458_Cerchione*.dds`, `display.dds`,
  `INT_Display_Color.dds`. `ac_crew.dds` is NOT in the kn5 (pit crew texture). Apply overrides to LOD_1..3 materials too (they share names).
- `texture/` folder at car root (458: `texture/flames/`) holds extra effect textures (flames.ini) - not needed for v1.

---

## 7. Practical loading recipe for the implementers (summary)
1. Enumerate `content/cars/*`; keep folders with `data.acd` (or `data/`); read `ui/ui_car.json` (sanitized) + `skins/*/ui_skin.json`.
2. `ReadAcd(data.acd, folderName)` -> car.ini, lods.ini, driver3d.ini, lights.ini, blurred_objects.ini, digital/analog_instruments.ini,
   mirrors.ini, colliders.ini, physics inis (another spec covers physics).
3. `Kn5.Read(LOD_0 file from lods.ini)`; build GameObjects (X mirror, flip winding, matrices via AcToUnity); textures = kn5 embedded,
   then skin folder overrides by name; materials map ksPerPixel* -> URP Lit (blend 1 = transparent, alphaTested = cutout at ksAlphaRef/0.5).
4. Start-hidden: nodes with active=0 (RIM_BLUR_*, COCKPIT_LR, DAMAGE_GLASS_*).
5. Find animated dummies by name: WHEEL_xx (spin + steer on front), SUSP_xx/DISC_xx (steer + bump, no spin), STEER_HR/LR (rotate around
   its local Z axis by steerAngle - axis [BELIEF]), RIM/RIM_BLUR swap by speed, car_*.ksanim for doors/wipers/paddles, ARROW_* needles.
6. Apply `car.ini GRAPHICS_OFFSET` / `GRAPHICS_PITCH_ROTATION` between the physics body and the model root [BELIEF on sign: offset
   applied to the 3D object as written, in AC coords].

---

## Critic addendum (completeness critic, 2026-10-31)

Probe: `C:/Users/derii/AppData/Local/Temp/claude/acscratch/critic/probe/` (dotnet, reuses the track probe's `Kn5.cs`; build
`dotnet build -c Release -o ../bin`, run `bin/Critic.exe cars <acRoot> <dir of decrypted data.acd folders>` | `uv <kn5..>` |
`uvy <kn5> <shader/material filters>` | `rows <kn5> <texName>` | `steer <kn5..>`). Decrypted car data used:
`acscratch/physsound/all/<car>/` (86 cars). Raw output: `acscratch/critic/cars.txt`, `uv.txt`, `uvy.txt`.

### C1. The three car spaces, reconciled [VERIFIED on 86/86 cars, `cars` mode]
There are exactly two frames. Everything in the kn5, `driver_base_pos.knh`, `collider.kn5` and `car.ini DRIVEREYES` is in **MODEL space**;
everything in the physics inis (suspensions, colliders.ini, aero POSITION, fuel tank ...) is in **PHYSICS (body/CG) space**.
- **Model space origin = ground at static ride height**, under the car centre: `WHEEL_xx.y - tyres.ini RADIUS` = 0 within 3 cm for
  83/86 front and 81/86 rear wheels (median -0.001). Exceptions: ks_lamborghini_sesto_elemento (-0.14, its model floor is at y=-0.149).
- **physics_point = model_point + GRAPHICS_OFFSET** (offset added AS WRITTEN, AC coords; so the body/CG origin sits at model point
  `-GRAPHICS_OFFSET`, i.e. CG height above ground = `-GRAPHICS_OFFSET.y`, 0.45 m for the 458). Proof: `WHEEL_LF.z + off.z` equals the front
  axle `WHEELBASE*(1-CG_LOCATION)` within 3 cm for 85/86 cars and `WHEEL_LR.z + off.z` equals `-WHEELBASE*CG_LOCATION` for 84/86 (median
  error 0.001 m); `WHEEL.y + off.y` equals suspensions.ini `BASEY` within 10 cm for 86/86 (median -0.015 front, 0.001 rear).
  -> the physics spec's derived axle positions and "wheel centre y = BASEY" are now VERIFIED; TRACK = distance between the WHEEL_L/R
  dummies (median error -0.007 m; the SDK comment says "from pivot 3D placement of the 3d model of a wheel").
- **GRAPHICS_PITCH_ROTATION** (deg, range -2.8..+1.6, 0 on 36 cars): correlates r = 0.79 (51 cars with pitch != 0) with the front/rear
  mismatch `(dyF - dyR)` and has the matching magnitude (`-1.5` <-> 5.6 cm over a 2.6 m wheelbase). Negative = model nose UP, i.e. a
  rotation about AC +X (car left) by the given degrees, right-handed [VERIFIED statistically; pivot (model origin vs CG) not determined,
  < 1 cm difference]. **Unity: modelRoot.localRotation = Quaternion.Euler(GRAPHICS_PITCH_ROTATION, 0, 0)** (Unity +X rotation pitches the
  nose down, mirrored X keeps the sign: -0.8 -> nose up).
- **DRIVEREYES is MODEL space**, not physics space: it lies 0.015-0.26 m (median 0.09 m, i.e. eyes in front of the head pivot) from the knh
  `DRIVER:RIG_Head` world position on 84 cars; eye height above ground = DRIVEREYES.y = 0.65 (tatuus) .. 1.37 m, median 0.986.
  (In physics space it would be 0.45 m higher - above the roof.)
- **collider.kn5 is MODEL space** (body hull from sill height up): bounds centre z equals the LOD_0 model centre z within 3 cm on 82/86
  cars, its min y is ~0.12 m above the model floor; 54-152 triangles (all < 255 -> valid convex MeshCollider). **Data bug:**
  `ks_ford_mustang_2015/collider.kn5` is displaced (bounds z -7.6..-3.1, y -0.95..0.62) -> sanity-check the hull against the model bounds and
  fall back to the LOD_0 bounding box when its centre is > 0.5 m off.
- **colliders.ini boxes are PHYSICS space**: box floor (`CENTRE.y - SIZE.y/2`) is 0.03-0.39 m (median 0.15 m) above the model floor.

Unity conversion of all of this (body = the Rigidbody at the CG, Unity axes x right / y up / z forward):
```csharp
// AC model-space point p (kn5 / knh / DRIVEREYES / collider.kn5) -> body-local Unity point
Vector3 ModelToBody(Vector3 p, Vector3 off) => new Vector3(-(p.x + off.x), p.y + off.y, p.z + off.z);   // + pitch rotation, see above
// AC physics-space point q (suspensions/colliders/aero) -> body-local Unity point
Vector3 PhysToBody(Vector3 q) => new Vector3(-q.x, q.y, q.z);
// model root GameObject under the body: localPosition = (-off.x, off.y, off.z); localRotation = Euler(GRAPHICS_PITCH_ROTATION, 0, 0)
```
Examples (AC coords): ferrari_458 off (0,-0.45,0.34), WHEEL_LF (0.846,0.336,1.154), eye (0.345,0.986,-0.168) vs knh head
(0.352,0.931,-0.193); abarth500 off (0,-0.56,0.038), eye (0.331,1.191,-0.490); ks_mazda_miata off (0,-0.47,-0.075), eye (0.350,1.005,-0.447).

### C2. UV / texture orientation [VERIFIED on the data side]
The section 1.5 belief "upload DDS raw, use uv = (u, v_stored), Repeat" is now proven for the stored data:
- Billboards whose image is upright by nature (grass, trees, flags, fences): for vertex pairs of a triangle, **higher world Y has the
  SMALLER stored v** in 99-100 % of pairs (magione Trees 82,440 pairs 1.000, Trees_ext 145,016 1.000, all 8 ksFlags 1.000, grass_BB 0.866;
  monza 1.kn5 BBgrass2 260,435 pairs 0.941, monza fences 0.966/1.000, trees 1.000; spa bbgrass 518,558 1.000, trees 0.99-1.0, fences 0.99;
  one exception spa `treesline` 0.23).
- The grass atlases store their image TOP first: per 1/16 row band in FILE order (DDS row 0 = image top) each grass strip goes sparse ->
  dense (`BBgrass4.dds`: 0.00 0.17 0.38 0.42 | 0.00 0.15 0.34 0.33 | ...; `bbgrass.dds`, `BBgrass2.dds` the same) = blade tips at low rows.
- Together: **row_from_top = frac(v_stored) * H** (DirectX convention). `Texture2D.LoadRawTextureData(dds bytes from +128)` puts file row 0
  at Unity v = 0, so **uv = (u, v_stored)** with `TextureWrapMode.Repeat` is exact for DDS (DXT and uncompressed). For images decoded with
  `ImageConversion.LoadImage` (PNG/JPG, Unity puts the image top at v = 1) either flip the rows after decoding or use (u, -v_stored).
  The centroid-on-opaque-texel test (`uv` mode) was inconclusive for car decals (quads include transparent margins) - do not rely on it.
  Unity-side behaviour (raw upload row order) is standard Unity, not run in a lab here: check one track tree / grass in the first lab shot.

### C3. Steering wheel node [VERIFIED geometry, rotation sign DERIVED]
- The rim mesh under `STEER_HR`/`STEER_LR` lies in the node's local XY plane; **local Z = steering column axis** (thin dimension):
  extents X/Y/Z = 0.374/0.353/0.149 (458), 0.383/0.381/0.113 (abarth500), 0.371/0.371/0.120 (miata), 0.381/0.381/0.120 (m3 e30),
  0.260/0.179/0.131 (tatuusfa1, formula wheel). Local Z in car space points forward-down (458 (0,-0.334,0.943), abarth (0,-0.448,0.894),
  formula (0,0,1)). Rim diameter 0.37-0.39 m on road cars -> radius ~0.19 m (ETS's MeasureRim fallback 0.22 m is too big; measure).
- STEER_HR world position = wheel hub (458 (0.343,0.746,0.353) model space, 0.52 m ahead of DRIVEREYES).
- Sign (derived from the mirror, not run): driver turning right = clockwise as seen by the driver = positive rotation about AC +Z_local
  (right-handed); in Unity after the X mirror: **`steer.localRotation = restLocalRotation * Quaternion.Euler(0, 0, -wheelDeg)`**, wheelDeg > 0 =
  right (check: Euler(0,0,-90) * up = +X = driver's right, top of the rim moves right).
