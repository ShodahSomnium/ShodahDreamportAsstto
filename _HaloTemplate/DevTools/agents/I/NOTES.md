# I (AI) - notes

## Built
- Data/Ai (Unity-free; my harness DevTools/agents/I/harness: `dotnet run -c Release --no-build -- actors|enc [name]|cmd|conv|path|nav [bsp]|navpng <bsp> <enc> <png>|squads|diff <map>`):
  - HaloActorDef.cs: actr (HaloActorDef: type, vision, perception times, cover, danger / panic / retreat, crouch, evasion, melee,
    berserk, position times), actv (HaloActorVariantDef: actor, unit, major variant, weapon, firing / burst / new-target / moving / berserk
    params, grenades, drops, vitality), HaloActorCatalog (per map, cached), HRange, HaloActorType.
  - HaloAiScenario.cs: actor palette, encounters (squads: starts, move positions, difficulty counts, group masks; platoons; firing
    positions), command lists (atoms + points), ai animation / script / recording references, conversations (participants, lines);
    names "enc", "enc/squad", "enc/platoon"; CountFor = normal count on easy/normal, insane count on heroic/legendary (0 = none).
  - HaloAiDifficulty.cs: the globals' (matg +284) difficulty table. a30 legendary: rof x1.5, burst sep x0.5, grenade chance x2, major n0.75.
  - HaloAiNav.cs: navigation from the sbsp itself. "pathfinding surfaces" = one byte per collision surface, bit 0x40 = walkable (exactly
    the surfaces with normal z > 0.7 on a10 / a30 / b30); "pathfinding edges" is EMPTY in the retail maps, so links come from the collision
    edges (left / right surface), plus step links over risers <= 0.12 wu and slope links; grid locate; A* + funnel kept a radius from walls.
    Built in 2-35 ms a BSP. a10: every encounter start / firing position on the nav, start->firing paths 60/63 .. 198/198 per BSP.
    a30 (outdoor): starts on nav 153/166 (dropship starts above ground), failures are mostly between separate terrain islands (cliffs).
- Game/Ai:
  - HaloAi.cs: data (HaloSession or LabMap), Tick() for K (30 Hz), Frame() + HaloAiDriver (labs; stands down when K ticks), Difficulty,
    placement (counts, required starts first, shuffled, major upgrades by the globals' fractions, encounter team), initially created
    encounters placed at mission start / BSP switch, dormant actors outside the active BSP, Erase / Kill / LivingCount / Units / Clear,
    hearing (HaloEvents.Fired + AI bursts), checkpoints (HaloCheckpoints "ai"), one-time Player.log facts, budget line every 60 s.
  - HaloAi.Script.cs: every ai_* function (list in REQUESTS.md "[from I to S]"); vehicles ones log unimplemented.
  - HaloAi.Conversations.cs: ai_conversation (participants, lines with delays, advance, status / line), HaloAudio playback, LineSpoken event.
  - HaloAiEncounter.cs: squads (delay timers, respawn), platoons (attacking / defending, change / maneuver rules), position claims, following.
  - HaloAiBodies.cs: what actors perceive (local player: HaloPlayer.Head + HaloLocal.Position; remote players: HaloNetGame.Players; every
    HaloUnit; labs: StandInPlayer); sight rays with a budget of 48 a tick.
  - HaloActor.cs / .Combat.cs / .Commands.cs: the brain - idle / guard (guard firing positions) / patrol (move positions) / search /
    combat (squad group masks by attacking-defending x target visible, desired combat range, claims, cover) / flee + cower (leader killed,
    friends killed, big hit, surprise; panic in groups) / hide (low shields, elites) / berserk / flying / following / command lists;
    bursts x difficulty, burst origin sweep, target leading, grenades (stimulus, ranges, enemy count, collateral, encounter timeout,
    chance x difficulty), melee (+ leaps), defensive crouch types (jackal shield), evasion from danger.
- Division with C: C fires while `Firing` (actv rof / error / damage), throws on `ThrowGrenade` toward AimPoint, strikes on `Melee`;
  I decide when and where.

## Verified
- harness numbers above (data, nav, a10/a30/b30 encounter dumps: harness/enc_a10.txt, enc_a30.txt, cmd_a10.txt).
- lab `ai -halomap a30 -haloenc lz_search` (real BSP 0, stand-in player + 2 marines following it): 17 covenant placed by difficulty
  counts (incl. major upgrades), grunts / elite / marines acquire by sight, 32 bursts (C fires them), stand-in hit 37x for 518, marines
  killed 7 covenant, panics on friends / leader killed (flee -> cower -> back to combat), elite melee + evades (earlier run), 163 paths,
  tick avg 0.17 ms (max 87 ms = the one-time nav build). Pictures DevTools/lab/ai/ai_a30_*.png. Cave run (cave_floor) showed the
  scripted ai_kill of the elites making the grunts panic.
- round 2: grenades - grunts (stimulus "seek cover", chance 0.2) now throw as they panic / run for cover: lab ai lz_search "first
  grenade thrown by lz_search/grunt#1". AI in vehicles (HaloActor.Vehicle.cs, V's SetAiInput): lab ai-vehicle a30 - 2 marines board the
  warthog (W-driver, W-gunner) in 0.8 s, the driver follows the stand-in player 33 m and stops 8 m short (DevTools/lab/ai-vehicle/*.png).
  object_set_*_attack_inhibited, command list script atoms wake scripts (HaloScript.Vm).
- NOT verified: conversations in a mission, command lists on a10 crew,
  checkpoint restore, co-op, a10 in the full mission.

## Open
- vehicle enterable rules (ai_vehicle_enterable_*, ai_vehicle_encounter) still logged unimplemented; automatic migration; command
  list atoms recording / vocalize;
  dialogue vocalizations; ledge drop links in the nav.
- round 3: co-op host hand-over (HaloAi.Handover.cs, Net part "ai" order 30): lab ai - capture 5.5 KB / 16 actors, adopt with blob
  16/16, without blob 16/16 re-seeded, bursts continue.
- round 4 (user: AI like Halo): nav detours to the nearest reachable spot of the own island (+ a no-radius retry) instead of
  'unreachable'; out-of-firing-range actors close in; vocalizations from the units' dialogue tags (HaloAiVoice: new enemy, grenade,
  flee / leader died, berserk, melee, leap, search, cover, killed enemy, friend died, shooting, vehicle entry); hunters berserk when the
  partner dies; grunt kamikaze (actr berserk grenade chance); actv new-target firing pattern + surprise delay; flying vehicles
  (HaloActor.Vehicle.cs Fly: command list points in 3D, attack runs, ground / obstacle clearance, friendly line of fire); ghost drivers
  strafe / ram; vehicle bursts from the vehicle's actv. Diagnostics in the budget line: 'not firing at a known target' reasons, detours.
  Labs: ai-banshee a30 (aib_*.png banshees in the air), ai a10 crossfire 22 bursts / unreachable 0, containment elites hit the player 75x.
