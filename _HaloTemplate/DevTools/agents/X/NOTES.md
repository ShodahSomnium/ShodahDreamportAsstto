# X notes (Effects)

## Files
- `Data/Effects/HaloFxData.cs` - Unity-free tag readers + cache (`HaloFxLibrary.For(map)`): effe (events/parts/particles), part, pphy,
  pctl (types/states/particle states), cont (+point states), ligh, deca, sprite sheets (`FxSpriteSheet`: sequences -> frames with uv
  rect, registration point, pixel size). All 29 maps parse with 0 failures (harness `DevTools/agents/X/harness`: `dotnet run -- lib`),
  9-90 ms per map.
- `Game/Fx/HaloFx.cs` - the public API (below). `HaloFxRuntime.cs` (effect runs, locations, parts), `HaloFxParticles.cs` (particle pool
  4096 + particle systems + physics + drawing), `HaloFxTrails.cs` (contrails), `HaloFxDecals.cs` (decals 512, lights 8), `HaloFxRender.cs`
  (one dynamic quad mesh per texture x blend function; alpha-blended batches sorted back to front).
- `Shaders/Fx/HaloFx.shader` ("SomniumHalo/Fx") + carrier `Shaders/Fx/HaloFx.mat` (guid df210229a519dbc49ccf7d0fe9bfc2dc) - quads are
  expanded per eye in the vertex shader (single-pass instanced safe), Halo's 8 framebuffer blend functions, URP fog. **Needs to be in the
  scene's HaloGame `_shaderCarriers`** (asked W in REQUESTS.md); without it the log says `Fx: shader SomniumHalo/Fx is missing`.
- `Editor/HaloLabFx.cs` - lab `fx` (pictures in DevTools/lab/fx/).

## HaloFx API (main thread)
- `Play(effect, pos, rot, attach = null)` (stub signature) / `Play(effect, pos, rot, attach, firstPerson)` -> `HaloFx.Handle`.
  `rot` = the effect's own frame, Unity convention (+Z forward). `attach` = follow it (particles flagged "stay attached" move with it).
- `PlayOn(effect, modelRoot, firstPerson)` - for effects whose locations are MARKERS of a HaloModelBuilder model (weapon "fire" effects:
  "primary trigger", "primary ejection"...). The model's +X is taken as forward (Halo axes). Markers are found as "marker:name" children.
- `Impact(effect, point, normal, incoming, attach = null)` - detonation / material response effects: Halo's locations "normal",
  "incident", "negative incident", "reflection", "gravity" come from these (decals project along them).
- `Loop(effect, pos, rot, attach, firstPerson)` + `Stop(handle, now = false)` - effects with loop start/stop events (overheat, jets).
- `Contrail(contTag, Transform follow)` / `Contrail(contTag, Vector3 start)` + `MoveContrail(trail, head)` + `StopContrail(trail)`;
  a followed trail stops by itself when the transform is destroyed or deactivated, then fades out.
- `Attach(objectTag, root, firstPerson = false, lights = true)` -> `Attachments` - an object's own attachments (Object +320): contrails,
  lights, effects (looped), particle systems, looping sounds (HaloAudio.Loop) on its markers (root when missing). `Detach(a)` stops them
  (contrails fade, systems stop emitting). Projectiles: Attach on spawn, Detach on detonation (rocket = exhaust contrail + exhaust
  effect + rocket exhaust pctl + lsnd; plasma bolts = light + contrail; frag = smoke effect + light). `SetLights(a, scale)`.
  For a non-marker root, Unity +Z is its forward (flight direction).
- `Decal(decalTag, point, normal)` (+ its "next decal in chain"). `Flash(lightTag, pos, attach, seconds)` - weapons' muzzle lights are
  object attachments Halo turns on while firing: Combat calls Flash per shot (e.g. weapons\assault rifle\muzzle flash.ligh @ primary trigger).
- `Preload(map)` (Match calls it at map open; ~10-90 ms), `PreloadWorker(map)` (worker-thread variant), `Clear()` (Match calls it on leave).
- `ObjectSpawned` event (tag, pos, velocity m/s) for effect parts that create objects (garbage debris, projectiles) - not created here.
- Settings: `Ambient` (multiplies non-self-illuminated particles), `ViewCamera` (sorting), `CollisionMask` / `DecalMask` (default
  HaloLayers.GroundMask), `LightIntensity` (HaloFxRuntime, 2.5). Stats: `Stats`, `ParticleCount`, `DecalCount`, `LightCount`.
- **Sounds inside effects are played by HaloFx** (snd! parts through HaloAudio.Play at the location / attached transform). E.g. the AR's
  "fire bullet" effect contains the fire sound and the casing sound: Combat must not play them a second time.
- First person: pass firstPerson = true for the local player's own gun (Halo's "only in first person" particles: e.g. the AR's FP flash
  petals); default false = third-person flashes (what remote players show).

## How Halo data is interpreted (verified / ours)
- Effect events run in order: delay, then parts once + particles over the duration by the distribution function; skip fraction; loops
  (loop start..stop) at most one pass per game tick (1/30 s). Locations: "" = the frame, special names above, else markers.
- Particles: size = radius (world units) x frame pixels x cached "sprite size" (1/largest frame px) for sprite sheets; whole images draw
  radius wide. Registration point honoured. Orientation screen-facing / parallel to velocity (sprite x along it) / perpendicular.
  Sequences: initial (played once) then looping; random start frame / mirroring / backwards flags; fade in/out; radius animation.
  "dies in air" particles (bubbles, water smoke) are not created (no water simulation).
- Point physics: gravity 0.0035651 wu/tick^2 (9.77 m/s^2) x air gravity scale (cached). Drag: a = -(air friction / mass scale) / r |v| v
  (cache values; Invader: friction x 10000, mass = density x sphere of 1 wu) - formula is OURS (consistent units; Halo's integrator unknown).
  Collisions (flag "collides with structures") raycast on CollisionMask: bounce with elasticity / surface friction, rest, dies-at-rest,
  collision effect (sound or effect) - budget 192 rays/frame.
- Particle systems: type states (duration + transition, colour multiplier, creation rate) and particle states (scale, colour, rotation,
  physics, bitmap); initial count at start. Explosion creation physics: speed between the type's constants 0..1 read as wu/tick (ours);
  system particle size = scale x frame pixels (diameter, ours).
- Contrails: points at "point generation rate", states duration + transition (width, colour), viewer-facing strip, u along the length x
  "texture repeats u", scrolled by "texture animation u".
- Lights: Unity point lights (pool 8), range = radius x modifier x 3.048 m; ligh "duration" read as TICKS (frag 45 -> 1.5 s; ours, the
  tag's "seconds" makes no sense); colour starts at the brighter bound and falls to the other by the falloff function; no-duration lights
  flash for the event's duration (min 2 ticks). Intensity 2.5 (ours).
- Decals: raycast from the part's location along its direction (reach = the part's velocity in wu), random sprite / rotation, radius x
  the part's radius modifier when set, lifetime then decay fade, chain, blend functions as the tag (multiply / double multiply / add).

## Colour
- Tints / colours are Halo gamma values: the shader converts vertex colours to linear (linear project). Double multiply (bullet holes,
  scorches) is done in gamma like Halo: 50% grey leaves the surface unchanged (Blend DstColor Zero with (2 x src_gamma)^2.2).
- "Alpha blend" particles on images without alpha (X8R8G8B8 / R5G6B5 / Y8 / P8: the explosions' lens-flare sprites) are added instead.

## Status (2026-10-19)
- Lab `fx` passed, pictures looked at (DevTools/lab/fx): frag explosion (fireball -> rising smoke column, scorch decal, lights), AR fire on
  the AR model (third-person star flash, first-person petals, casing ejected from the ejection marker, smoke), plasma rifle impact (cyan
  flash, gravel bouncing to rest, glow decal fading), AR dirt impacts (dust kick), rocket in flight via `Attach` (exhaust contrail + pctl
  fire/smoke puffs) and its explosion (lens flare flash, fire, smoke, scorch), plasma grenade (blue burst), contrails of AR / plasma /
  needler / sniper / fuel rod. `Clear()` empties the pool. No exceptions from Fx; no allocations in steady state by design (not profiled).
- NOT verified: in the Somnium client / VR (per-eye billboards are by construction), lights' look on a real BSP, decals on non-flat
  surfaces (flat quads, no clipping: they overhang edges), particles cut by the ground (no soft particles), water (none), weather (none),
  first-person-only effects drawn over the gun (no special depth), object parts (only the ObjectSpawned event), effect "scale" inputs.
- Player.log: `[SomniumHalo] Fx: <map> effects ready: N tags ... in X ms` at match load; `Shader 'SomniumHalo/Fx': carried`; problems:
  `Fx: shader SomniumHalo/Fx is missing`, `Fx: <tag> skipped (...)`, `Fx: ... failed: ...`.

## 2026-10-19 "bullet sprites exist for too long" (client test)
- Cause 1 (bug, fixed): FxBatch.End uploaded vertices before shrinking the index buffer after a batch had been hidden -> Unity refused
  ("Mesh.vertices is too small") and the mesh KEPT its old quads: sprites froze in place and stayed. Fixed (lab log: 0 such errors).
- Cause 2 (tag-faithful but wrong for VR): AR / pistol casings live 30 s (tag). Halo draws the local player's casings in the view-model
  space where they never land; ours landed at the VR player's feet (15 a second). Now: first-person particles (camera mode "only first
  person" / "first person if possible" of the local gun, `firstPerson = true`) end at their first world contact; any debris at rest is
  capped (`MaxResting` 96, oldest first). Contrails of bullets end at once on Detach (verified).
- Lab `fx-burst` (AR burst at a wall, local + remote gun, 90 fps): after 0.5 s smoke puffs at the wall + falling debris; after 2 s a few
  specks at the wall foot; after 5 s only the remote gun's casings on the ground and the bullet-hole decals (300 s, as the tag). No contrail.
- Combat's LineRenderer tracer fallback is off (`HaloProjectiles.PlainTracers = false`): not touched.

## 2026-10-20 collision sound flood (A)
- Particle collision effects/sounds: first contact per particle only + max 10/s per tag (ours; Halo has no flag). audio-sfx a30: metal_chips 9440 -> 250 asks.
