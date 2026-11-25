# W - World / track (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/` = `D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo`,
`ETS/` = `D:/Dreamport/Euro Truck Simulator/Euro Truck Sim/Assets/#User/SomniumEuroTruck`, code root = `Assets/#User/SomniumAssetto`.

## Scope
Build any AC track layout (and the hub showroom) in Unity at run time from the player's install, faithful to the game's look, with
working collision on `AcLayers.Track` for cars AND the walking avatar, every physics mesh tagged with its surface, markers hidden,
and the race data (spawn points, gates, AI line, start lights) exposed through the `AcTrackWorld` contract. You also own the generic
kn5 builder that C (cars) and F (showroom hub) use.

## You own
`Game/World/` (stubs already there: `AcTrackWorld.cs`, `AcKn5Builder.cs` - fill them; add `AcTextures.cs`, `AcMaterials.cs`,
`AcTrackLook.cs` (lighting/sky/fog), `AcTrackJob.cs` (worker), ...), `Shaders/` (new folder), `Resources/` (carrier materials),
`Editor/AcLabWorld.cs`. You may set `_shaderCarriers` on "Ac Game" in `Assets/Assetto Scene.unity` (only that list; keep YAML valid,
then run metas.sh; note it in NOTES.md).

## Port from
- `HALO/Game/World/HaloTextures.cs` (200), `HaloMaterials.cs` (533), `HaloModelBuilder.cs` (269), `HaloWorldJob.cs` (252: worker job
  pattern), `HaloWorldDriver.cs` (56), `HaloWorldLook.cs` (179: ambientProbe, sun, fog - guide section 7), `HaloCollision.cs` (126),
  `HALO/Shaders/HaloCommon.hlsl` + `HaloEnv.shader` (URP shader skeleton that survived the upload) and `HALO/Resources/*.mat` (carrier pattern).
- `ETS/Ets/EtsTextureCache.cs`, `ETS/Ets/EtsDds.cs` (raw DDS upload), `ETS/Ets/EtsMaterialCache.cs`, `ETS/Shaders/EtsMulti.shader`
  (multi-layer terrain shader - closest to AC's ksMultilayer), `ETS/Editor/EtsShaderReferences.cs` (keeping shader variants).
- Data side is done: `Kn5File`, `Kn5Scan`, `AcDds`, `AcMat4.ToUnity`, `AcConvert`, `AcModelsIni`, `AcTrackMarkers`, `AcSurfaces`, `AcAiSpline`.

## Spec sections
`research/ac_track_formats.md`: 0, 1, 3 (all), 4, 6.1, 6.3 (lighting.ini), 6.6 (startinglights.ini), 7 (loading pipeline), 9, and the
**Critic addendum T1-T5** (markers float, use horizontal +Z only, UVs, models.ini ROTATION check, ksMultilayer needs our shader).
`research/ac_car_formats.md` 1.2-1.5, 1.7, C2 (UV/DDS orientation). `research/tablet_front.md` 7 (showroom kn5 for the hub).
DREAMPORT_GUIDE section 3 (carrier materials), 7 (fog ON + Linear, ambientProbe, render queue 2501+ for skies).

## Must do (acceptance)
1. `AcTrackWorld.Load(layout, progress)`: worker-thread header scan + vertex reads of every `layout.Kn5Files` kn5 (positions from
   `AcModelsIni`), main-thread time-sliced upload (SetVertexBufferData/SetIndexBufferData managed arrays, UploadMeshData(true) for render
   meshes), raw DDS textures (only those referenced by drawn materials), LODs (lodIn/lodOut -> LODGroup or distance culling), hidden
   AC_ marker cubes and isRenderable=0 meshes, progress reported (0..1 + stage) - the loading card follows it.
2. Collision: one MeshCollider per physics mesh (digit-prefixed names) on layer 14, `AcSurfaceTag.Register(collider, surface, isWall)`
   via `AcSurfaces.Match`; walls too (IsWall). The avatar must be able to walk on the pit lane (layer 14 collides with 2).
3. Spawn points / gates / AI line: `StartGrid`, `PitBoxes`, `HotlapStart` (Marker = node position in Unity; HeadingDeg from the
   HORIZONTAL part of node +Z, research T2; Progress from `AiLine.Progress`), `TimeGates` (Forward from the AI line), `AiLine`, `PitLane`,
   `Surfaces`. `TryGround` raycasts down from marker + 1 m (T1).
4. Materials: ksPerPixel* -> URP Lit-like carried shader (diffuse, normal map, alpha test/blend, emissive); ksMultilayer* (road) -> your
   own multilayer shader as a carrier material; grass/trees alpha-tested. Fog ON + Linear kept; `RenderSettings.ambientProbe` set;
   sun from data/lighting.ini (SUN_PITCH/HEADING). Shaders only through carriers listed in `_shaderCarriers`.
5. `StartLights`: find KS_START_LIGHT_n meshes, emissive per startinglights.ini; `SetLights(redCount, green)` (R calls it).
6. `Unload()` frees everything (meshes, textures, colliders, `AcSurfaceTag.Clear()`); memory back near the hub level (log it).
7. `AcKn5Builder.Build/BuildAsync` (generic: used for tracks, cars (C), showroom (F)); `AcKn5Model.Nodes` by name; skin texture overrides
   by file name (`SkinDir`); `Readable` keeps CPU copies.
8. Performance: monza must load without a frame > 50 ms on the main thread after the first second; log total load time and triangle /
   texture memory. Optional: skip ksGrass meshes in VR (setting `world.grass`).

## Player.log lines to add
`Track: loading <track>/<layout>: N kn5, M meshes, T textures` · `Track: built <key> in X s (meshes, tris, textures MB, colliders, walls,
surfaces matched/unmatched)` · `Track: markers: G grid, P pits, gates n, hotlap yes/no, start lights k` · `Track: unloaded <key>` ·
`Shaders: ...` (AcShaders logs carried shaders) · failures with the file name.

## Verify
build.sh x3 0 errors + check.sh; harness probe (e.g. `Cmd_trackload` timing the worker part for monza and ks_black_cat_county);
lab `world-magione` and `world-monza`: shots from the grid (AC_START_0 eye height 1.1 m), from above, and a tree / grass close-up (UV
orientation check, research C2/T4), gantry facing the grid (models.ini ROTATION check, T5); `r.Check` that a ray down at every
AC_START_n hits a tagged surface. LOOK at the pictures.

## Consumes / provides
Consumes: AcContent layouts (D), AcSurfaces/AcAiSpline/AcTrackMarkers (D), AcLayers/AcSurfaceTag (lead), AcSession (R calls you).
Provides: `AcTrackWorld` (R, P, I, H, N), `AcKn5Builder` (C, F), `AcSurfaceTag` registrations (P, R, A), start-light meshes (R).
