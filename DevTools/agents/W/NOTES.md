# W - World / track: NOTES (2026-11-01, second pass)

## Files (all mine)
- `Game/World/AcTrackWorld.cs` - the contract, filled: Load (worker scan + produce, main-thread time-sliced upload, off-thread collider
  cooking, budgeted collider assignment, every failure path -> Fail), markers -> StartGrid / PitBoxes / HotlapStart / TimeGates (+ AbStart,
  AbFinish, OpenFinish), AiLine / PitLane / Surfaces, AcStartLights (KS_START_LIGHT_n lamps compacted in n order, panels from the VERTICES,
  startinglights.ini EMISSIVE x INTENSITY + DIFFUSE; or a semaphore.ini drag / A2B tree), Unload (+ native memory log a few frames later),
  settings + session look.
- `Game/World/AcTrackJob.cs` (Unity-free: AcShading, AcTrackJob, options; draws the AC_SEMAPHORE_* lamp meshes, loads semaphore.ini),
  `AcTrackJobMesh.cs`, `AcTrackJobTex.cs`, `AcTrackJobModel.cs`; `AcKn5Builder.cs`, `AcTextures.cs`, `AcMaterials.cs` (SetEmissive /
  ClearEmissive (restores ONLY _Emissive now) / SetLamp), `AcTrackLook.cs` (sun/sky/fog, Refresh, mutes the scene's directional light while
  a track is loaded), `AcLodDriver.cs` (lodIn/lodOut + keeps the player camera's far plane >= the track span), `AcCooker.cs` (BakeMesh thread;
  main-thread cooking only after a "main thread" refusal or 8 failures in a row).
- `Shaders/AcCommon.hlsl` (AcShadow: the screen-space shadow variant samples at ComputeScreenPos now), `AcPerPixel.shader`,
  `AcMultilayer.shader`, `AcSky.shader`; carriers `Resources/AcPerPixel.mat`, `AcMultilayer.mat`, `AcSky.mat` in "Ac Game" `_shaderCarriers`.
- `Editor/AcLabWorld.cs` - lab scenarios (run 2026-11-01: see "LAB").
- Probes: `DevTools/harness-ac/ProbeW.cs` (`wmat`) and the W harness copy `DevTools/agents/W/harness` (also compiles `Game/World/AcTrackJob*.cs`):
  `wjob`, `wjoball`, `wmodel`, `wcheck`, `wcensus`, `wnomip`, `wuv`, `wuvgrad <kn5> <material>` (uv per metre), `wtexavg`, `walpha`, `wenc`, `wlights`, `wlamps <track> [layout]`
  (lamp panels from vertices), `wlamptex <kn5> [prefix]` (txDiffuse colour under lamp UVs), `wsem <track> [layout]` (semaphore.ini -> lamps),
  `wshadow <track>` (castShadows census). Build/run: `cd DevTools/agents/W/harness && dotnet build -c Release -v q && dotnet run -c Release --no-build -- <cmd>`.

## Verified (offline: harness on the real install, compile)
- `wjoball` (after this pass): ALL 35 owned layouts, 0 failures, every item produced, 0 texture problems (monza 1.8 s, zandvoort 4.2 s worst).
- **Start lights - correction of the first pass:** the KS_START_LIGHT_n mesh NODE sits at its FBX origin (magione / monza 100.kn5); the lamp is
  only in the vertices. "magione lights 118 m behind the pole" and "monza dot 0.60" were measured at that origin and were WRONG.
  `wlamps` (vertex panels, models.ini placement): magione lamp 0 49 m ahead of the pole, 5.2 m up, faces the pole 1.00; monza gantry 42 m ahead
  (faces 1.00) + a repeater panel in the same mesh 70 m behind, facing the back of the grid; ks_highlands (file ROTATION -17) 38 m ahead, faces
  1.00; ks_nurburgring (ROTATION 180) 71 m ahead, faces 1.00; vallelunga 25, spa 26, silverstone 20, imola 84 m, all facing the pole.
  => the models.ini ROTATION sign (T5) is confirmed offline on both rotated files. The lab aims at `StartLights.PanelSeenFrom(0, eye)`.
- Lamp colour: lit lamp = EMISSIVE (2,0,0) x txDiffuse under the lamp: magione misc.dds 0.29,0.14,0.13; spa / imola 0.12,0.06,0.07; vallelunga
  0.33,0.10,0.17 (`wlamptex`) - red, possibly dim: picture check. DIFFUSE=0.3 (all 28 files) = the lit lamp's ksDiffuse [BELIEF].
- Start tree: ks_drag (10 lamps) and trento-bondone (5): every semaphore.ini NAME resolves to a lamp mesh that is now drawn (`wsem`); the lamp
  textures carry the colour (ks_drag lights.dds yellow 0.63,0.47,0.03 / red 0.63,0.06,0.04 / green 0.03,0.63,0.14; trento LIGHT_*.png), so the
  emissive is white x INTENSITY 2.2.
- Shadows: the kn5 castShadows flag is honoured; monza 777 of 1027 drawn meshes, 1.7 M of 2.9 M triangles (`wshadow`) -> "TRACK SHADOWS" setting.
- Install time / weather: server_cfg SUN_ANGLE=48, [WEATHER_0] GRAPHICS=3_clear; install cfg/race.ini SUN_ANGLE=-48, 4_mid_clear; the player's
  Documents/Assetto Corsa/cfg/race.ini SUN_ANGLE=-48 (10:00), 4_mid_clear (the local default now, else install race.ini, else ours).
- Earlier pass (code unchanged there): wcheck grid headings vs fast lane, models.ini placement = data core ModelMatrix to 0.0000 m, DXT
  re-encoder 33-37 dB, multilayer facts, CARPAINT alpha.
- Compile: build.sh: my files 0 errors (1 warning: Physics.BakeMesh(int,bool) obsolete - kept). check.sh "all 117 Fusion/Somnium members are proven".

## LAB (2026-11-01, third pass) - pictures LOOKED at
- `boot` PASS (slot B; its labproject copy had only Assets/#User - labcopy.sh bug, filed to the lead; W copied Assets/Packages/ProjectSettings).
- `world-magione` 3 runs, last PASS: 18/18/2/4, 36/36 markers over tagged surfaces (pits 18/18 IS_PITLANE), lamp 0 49 m ahead facing 0.99,
  built 1.6 s, worst frame after 1 s 16 ms, memory 802 -> 1099 -> 807 MB (4 MB left). No shader errors; all 3 W shaders carried.
- `world-track -actrack spa` PASS (after both fixes): 48/48 markers, pits 24/24 IS_PITLANE, lamp 0 26 m ahead facing 0.97, 1.6 s, worst
  frame 17 ms, 5 MB left; pictures: pit straight, banner, 3 red lit, spruce billboard upright. The near road shows a fine line pattern (file?).
- `world-monza` PASS (1 run, before the mip fix): 26/26/3/5, 52/52, worst frame 19 ms <= 50, memory 4 MB left, gantry 3 red lit.
- **Fixed: ksMultilayer details are WORLD-projected** (Shaders/AcMultilayer.shader: detail uv = (-x, z) world metres x multX; _objsp keeps
  the mesh uv x B pair, `_Flags.x`). With uv x mult magione's road showed 0.6 m "cobbles", the paddock giant gravel, grass 20:1 streaks
  (`wuvgrad`: asphalt 35 m per u / 15 m per v; grass 8 / 185). Now: grey fine asphalt, paddock tarmac with markings, isotropic grass.
- **Fixed: truncated DXT / RGBA mip chains are completed to 1x1** (AcTrackJobTex `extend`, ExtendDxtChain): magione's gravel trap
  (trap_detail 6 levels, smallest 32 px, tiled 1.7 m) drew moiré bands from above; gone now. wjoball 35/35 ok after it (zandvoort 3.6 s).
- **Fixed (lab only):** world-* waits for the hub (showroom + showroom car) before measuring; run 1's "145 MB left" was the hub building
  during the track load.
- Pictures: sky gradient + sun disc, distant haze OK; trees upright (billboard_Trees: trunks down); start lights off / 3 red / out / all
  red as expected (magione green state = all dark, AC has red lamps only there); grid boxes and checkered line on the road at the pole.
- To judge: monza grid.png has a strong white sun glare with pixel speckle near the camera (asphalt ksSpecular 0.55 x tarmacSpec 0.4 x
  detail alpha, sun 5.1 - geometrically the sun's mirror point); the far terrain textures are low-res and blotchy (the files' own).

## NOT verified (needs the lab / client)
- Not run in a lab: shadows in a client build, the far-plane raise on a real player camera (no player camera in the lab), start tree
  (world-drag), rotated tracks (highlands / nurburgring), VR frame time.
- BELIEFs to judge in pictures: car-paint detail blend, _objsp layer scales, fog formula, colorCurves interpolation, sun path axes, normal-map
  convention, emissive x txDiffuse (lamps dim?), DIFFUSE as lit ksDiffuse, tree READY lamps cumulative (step i lights ORDER < i) and dark at
  START, ksAlphaRef 0 -> 0.5, LOD distance to the bounds centre.

## Lab steps (run later, in this order; LOOK at DevTools/lab/<scenario>/*.png)
1. `bash DevTools/lab.sh world-magione` - counts 18/18/2/4, tagged surface under every slot, lamp-0 panel ahead + facing check, worst frame
   (logged), memory back near the pre-load level after unload; grid.png, grid_behind.png, above.png, far_view.png (player far plane, no lab
   override), lights_off / lights_3red / lights_out_green / lights_all_red_wide.png (lamps lit red, facing the camera?), billboard_*.png (trunks down).
2. `world-monza` - adds the hard check "worst load frame after 1 s <= 50 ms" (brief #8); 26/26/3/5.
3. `world-highlands` (ks_highlands/layout_int, light file ROTATION -17) and `world-nurburgring` (layout_gp_a, ROTATION 180): T5 in pictures.
4. `world-drag` (ks_drag/drag1000): start tree (Count 3, IsSemaphore), lights_*.png + lights_jumpstart.png.
5. `world-track -actrack <id> [-aclayout <id>]` for anything else.

## Numbers that are ours
SliceMs 8 (world.loadslice), MaxQueuedBytes 256 MB, Aniso 8, NoMipsBelow 8 px, LOD sweep 15 frames, sky dome 24x12 + sun disc / glow, flag
wave shape, below-horizon sky tone, URP light intensity = peak sun x 0.4, ambient probe = AC ambient x 0.4, fallback time 14:00 / weather
4_mid_clear (only without any race.ini), default lighting 35/45 when lighting.ini is missing, cooking wait on unload 3 s, Cull Off for
ksTree/ksGrass/ksFlags, lamp panel cluster 3 m, far plane clamp 1500..6000 m checked twice a second, cooker gives up after 8 failures in a
row, lab memory tolerance max(64 MB, 1/4 of what the track took).

## Player.log lines
- `Track: time / weather defaults from <race.ini source>: SUN_ANGLE .. (hh:mm), weather ..` (once)
- `Track: loading <key>: N kn5, M meshes (R drawn, C colliders, K marker cubes hidden, I inactive), T textures, V vertices; scan X ms`
- `Track: markers: G grid, P pits, gates n, hotlap yes/no, start lights k, fast lane ..., pole progress ...`
- `Track: look: weather ..., sun pitch .. heading .. angle .. -> elevation .., ... (time player|session|<race.ini>, weather ...)`
- `Track: the scene's directional light(s) switched off while a track is loaded (n, first '...')` (once)
- `Track: start lights k (KS_START_LIGHT_a..b), emissive r,g,b diffuse d, lamp 0 (p panel(s)) X m from the pole, ahead-dot .., faces the pole ..`
  or `Track: start tree (semaphore.ini): n lamps (3 READY steps), intensity 2.2`
- `Track: built <key> in X s (..., shadows kn5 castShadows|off (setting); worker .. ms, cooking baked off the main thread: n[, f bake failures], worst frame so far ..)`
- `Track: ready <key>: worst frame after 1 s X ms (<stage>), allocated A -> B MB, graphics driver G MB, track needs far plane F m, player camera far a -> b m`
- `Track: player camera far plane X m, near Y m; the track needs F m -> raised while the track is loaded` (once)
- `Track: unloaded <key> (...)` then `Track: memory after unloading <key>: allocated A -> B MB (before the load C MB), graphics driver ..., managed heap ..`
- `Track: session look: SUN_ANGLE .., weather ..` · `Track: look changed: ...` · `Track: loading <key> failed: <why>` (also for exceptions in
  setup / collision / finish now) · `Kn5: built <file>: ...` · `Shader 'SomniumAssetto/PerPixel': carried|Shader.Find|MISSING` · `Textures: ...`.
- Graphics-driver memory reads 0 in non-development players (Unity reports it only in the editor / development builds).

## Public API added (beyond the contract)
AcTrackWorld: Loading, LoadProgress, LoadStage, LoadError, Markers, OpenFinish, AbStart, AbFinish, Sections, SectionAt, LengthM, MaxCars,
DrawnBounds, FindNode, RegisterSettings, Weather, WeatherIds, SunAngle, Grass, Shadows, SliceMs, LookSource, **SetSessionLook(sunAngle,
weather)**, SessionSunAngle, SessionWeather, WorstFrameMs, WorstFrameStage, MemoryBeforeLoad, MemoryBuilt, MemoryAfterUnload, WantFar.
AcStartLights: Reds, IsGreen, Panels(i), PanelSeenFrom(i, eye, out centre, out normal), IsSemaphore, JumpStart, SetJumpStart(on).
AcKn5BuildOptions / AcKn5Model: as before. AcMaterials: SetLight, EnsureGlobals, SetEmissive, ClearEmissive, SetLamp, Create, Acquire/Release.
AcTrackLook.Refresh(weatherToo). AcLodDriver: ViewerOverride, WantFar, ClientFarSeen, KeepFar().
Settings: world.grass (default OFF in VR), world.shadows, world.time (index 08:00..18:00, default from race.ini), world.weather (row index) +
world.weather.id (the folder id = the truth; survives weather mods), world.loadslice.

## Requests
- Filed: "W -> R, N: session time / weather" (AcRules.SunAngle / Weather from server_cfg + call SetSessionLook; N carries them),
  "W -> R: start tree" (SetJumpStart on a jump start), "W -> lead (FYI)": scene light muting + far-plane raise.
- Done by others: F calls RegisterSettings in BootDone (done F). P's hull / layer-14 note: no action (walls stay on 14).

## Open / not done (with impact)
- AC_POBJECT (cones, DRS boards) have no physics: cars pass through them (a static collider would be worse than AC's knock-over props).
- camera_facing crowds (stands empty), audio_sources reverb zones (agree with A), groove.ini rubber, ksGrass txVariation, cubemap
  reflections (sky colour only - also flattens car paint since C uses W's shader), no static batching (render meshes are non-readable);
  shadow cost and VR frame time to measure in the first client run (TRACK SHADOWS / 3D GRASS settings exist for that).

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 2.1 / 2.2 / 9 W; build.sh 0 errors x3, check.sh all 117 proven; NO lab run yet)
Files: `Shaders/AcPerPixel.shader`, `Game/World/AcTrackJob.cs` (AcShading), `AcTrackJobModel.cs` (AcModelJob.DamageSlots), `AcKn5Builder.cs`
(AcKn5BuildOptions.DamageSlots, scope key suffix "|damage"), `AcMaterials.cs`; probe `DevTools/agents/W/harness/ProbeWDmg.cs` (`wdmgslots`).
- Shader (no keywords, float switches, CBUFFER identical in all passes): `_DamageTex` ("txDamage"), `_DamageMaskTex`, `_DamageScratch`,
  `_DamageDent`, `_DamageSet` (x damage paint, w broken glass), `_GlassSet` (x glassDamage). Damage paint: ws / wd = saturate(mask . vector);
  normal = lerp(flat, damage NM, wd); albedo = lerp(albedo, scr.rgb, scr.a * ws); reflection (maps.b) and both speculars x lerp(1, NM.a, ws).
  Broken glass: clip while glassDamage is 0, alpha = _NormalTex.a (the crack map) x glassDamage, independent of tangents. The txNormal sample
  moved out of the tangent test (same `_Flags.y` gate); non-damage materials draw exactly as before.
- AcShading.Of(m, damageSlots): ksPerPixelMultiMap_damage* -> DamagePaint (+ txDamage, + txDamageMask unless the scalar `_damage`), txDust not
  read; ksBrokenGlass -> BrokenGlass, NormalMap = damageSlots, no kn5 txNormal slot (the 64 px proxy is never read now). Of(m) = Of(m, false).
- AcMaterials: binds the slots (missing -> Black; scalar shader -> White mask), `_DamageSet.x = 1`; every ksBrokenGlass `_DamageSet.w = 1`,
  `_GlassSet.x = 0` (G0, always), `_NormalTex` = `AcMaterials.DamageGlassTexture` (lazy, install content/texture/DAMAGE_GLASS.dds, shared,
  never destroyed) only with damageSlots. Public ids `IdDamageTex / IdDamageMaskTex / IdDamageScratch / IdDamageDent / IdDamageSet / IdGlassSet`.
- The gate: AcShading does not read R's AcCarDamageGate; C snapshots it per car and passes `AcKn5BuildOptions.DamageSlots` (departure from
  DAMAGE_DESIGN 9 W with the same effect; tracks / showroom / driver kn5 never get slots). Slots on -> own material scope, so a scope built
  before damage was switched on is never reused with missing slots.
Verified offline: `wdmgslots` 86 cars, 161 LOD_0/LOD_1 files: 260 damage paint materials (0 scalar), 520 extra slots, 0 unresolved (LOD_1
names resolve against LOD_0); 158 ksBrokenGlass materials get the install normal with damage on; slots with damage OFF identical to iteration
1 on every non-glass material (0 differences). C's `dmgvisres`: 138/138 masks decode. NOT verified: the shader compiling in Unity / the upload
(only the lab can prove it), the look. G0 changes DAMAGE-0 visuals on ktm_xbow_r, lotus_exos_125(_s1), bmw_m3_e92(_drift/_s1) AND
ks_ford_mustang_2015 LOD_2 (2 active zone nodes, found by C's probe): the grey film goes.
Log: `Shaders: broken-glass crack map loaded from the install (1024x1024)` / `Shaders: content/texture/DAMAGE_GLASS.dds not found - broken glass stays invisible`.

## Damage pass 2026-11-02 (fixer)
- AcMaterials.DamageGlassTexture: read + decode on a worker (WarmDamageGlass, called by R's AcCarDamageGate.Refresh while damage textures are wanted); the getter waits only if nobody warmed it, then uploads. Latched only when the file is missing; install-not-known returns null without latching; failures retried up to 3 times (// ours). New DamageGlassGone.
- Verified: build.sh 0 errors; lab damage-e2e / cars-damage logs (crack map line).
