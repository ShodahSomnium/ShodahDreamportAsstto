# Assetto Corsa Dreamport (Somnium Space)

Assetto Corsa inside a Somnium Space world, played as your own Somnium avatar, in VR and on a monitor, for players who own the game. The
world ships **no game assets**: every car and track model, texture, sound, physics number, menu picture, font and string is read at run
time from the player's own Assetto Corsa install. It is found through Steam (`libraryfolders.vdf`, app 244210, folder `assettocorsa`;
here `E:\SteamLibrary\steamapps\common\assettocorsa`) or through the folder set in SETTINGS. Our code carries only names, offsets and the
numbers the game files do not hold; each of those is marked `// ours` in the code.

Access uses the same whitelist as the other Dreamports (Shodah, Wildy, Meloz, "ꨄ Rage Quit ꨄ", denevraut, Ninquiet; `Core/AcAccess.cs`).
For anyone else nothing starts: they see and hear nothing, and other players are hidden and silent for them. Every log line is tagged
`[SomniumAssetto]`.

Code layout: `Core/` (log, access, version, space, shader carriers), `Data/` (Unity-free readers: install and catalog, kn5, data.acd,
ksanim/knh, track markers, surfaces, AI splines, FMOD banks, the physics core, AI maths, HUD fonts, UI data), `Game/` (AcGame root and the
gate, isolation, shared contracts, one folder per module: World, Cars, Physics, Seat, Audio, Front, Race, Net, Hud, Ai; Vr/Desk client
helpers), `Editor/` (lab runner and scenarios). Developer docs: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`,
`DevTools/agents/INTEGRATION.md`, `DevTools/agents/<module>/NOTES.md`.

## Iterations

| # | Date | What |
|---|------|------|
| 1 | 2026-11-01 | First playable build. It has the ASSETTO CORSA tablet app (DRIVE, TRACK, RACE LOBBY, GARAGE, TIMING, SETTINGS, HOW TO PLAY) and the AC showroom as the hub. All 35 owned layouts build from their kn5 with the track's light, sky, surfaces, pit boxes, grid and start lights. All 86 owned cars come with every skin, working cockpits, lights, doors and the driver. You walk to your car, open the door and sit in. Steering: the car's own wheel in VR (grab and turn), or keys / gamepad / mouse cameras on a monitor. Passengers are supported. Physics come from each car's data.acd, and engine, gear, skid, wind and surface sounds from the car's own FMOD bank. The HUD has a monitor overlay and a VR panel beside the wheel. Racing rules: practice / qualify / race / hotlap, grid, lights, jump start, laps, sectors, cuts, pit limiter, results. AI opponents. Multiplayer for up to 50 players (remote cars, race control, master hand-over, net protocol 2) |

## What iteration 1 does

* **Tablet app** (Front): an "ASSETTO CORSA" app on the Somnium tablet's home screen, with the launcher's own logo. It has these pages:
  * **DRIVE**: brand tabs, the car grid with badges, a spec line from ui_car.json, the car preview, the skin thumbnails and DRIVE THIS CAR.
    Cars you do not own are greyed out.
  * **TRACK**: track previews with "n LAYOUTS" / NOT INSTALLED badges, the preview and outline, length and pit boxes, LOAD / LEAVE TRACK
    (JOIN SESSION when the master's session runs and you are in the hub).
  * **RACE LOBBY**: the rules with the launcher's own ranges: session, laps or time, the assists allowed (ABS, TC, auto-clutch ...), tyres out,
    fuel and tyre-wear rates, damage, jump start rule, AI count and level, allowed cars. It also lists the drivers (ready, seated,
    passenger, spectating, AI) and has START (it names the session, press twice), GET IN MY CAR / GET OUT, RESET CAR and BACK TO PITS.
  * **GARAGE**: driving aids, tyre compound, fuel and setup rows from the car's setup.ini. The values go into the car when it stands still.
  * **TIMING**: live standings (paged, your row pinned), the session clock or "LAP n / N". It opens by itself on results.
  * **SETTINGS**: install folder, tablet, seat / VR steering, HUD, audio and track options.
  * **HOW TO PLAY**.

  The tablet holds the car while it is open (setting). Without a tablet, the hub's menu panel is the menu, and on a track it stands in front
  of you.
* **Hub** (Front + World): the AC showroom kn5 at the hub height, lit from `system/cfg/colorCurves.ini` (the file acShowroom.exe uses). The
  chosen car stands in it with its skin, and the menu panel stands beside it.
* **Tracks** (World): every owned layout (35 here) builds from its kn5 files and models.ini. It includes AC's shaders (multilayer road
  and grass, per-pixel, alpha trees), the track's lighting.ini sun / sky / fog, the weather and time of day (from race.ini, or SETTINGS),
  collision with surfaces.ini tags (grip, valid track, pit lane, kerbs), hidden markers, pit boxes, grid slots, timing gates and the
  start-light meshes (gantry lamps, drag-strip tree). Loading shows a card with the track preview. The scene light is switched off and the
  far plane raised while a track is loaded, and both are restored when it unloads.
* **Cars** (Cars): every owned car (86 here), every skin, LODs from lods.ini. The model has wheels, suspension, discs, steering wheel,
  doors (their own ksanim), lights / brake lights / pit-limiter light from lights.ini, needles and digital displays, the driver with the
  skin's suit, and the collider.kn5 hull. The car paint's damage normal map is kept flat, as AC does for an undamaged car.
* **Entering, seat and steering** (Seat):
  * Walk to your car in your pit box and open the driver's door: click it, press E next to it (monitor), or point at it and pull the
    trigger (VR). The door opens with the car's own animation and sound, and you sit at the car's DRIVEREYES / driver_base_pos.knh
    position.
  * The seated VR view follows the car by **heading only** (no pitch or roll; `vr.horizonlock`, default on).
  * **VR:**
    * Grab the car's own steering-wheel rim with one or both hands and turn it.
    * Right trigger is throttle, left trigger is brake (or swapped, or the right stick: `seat.pedals`). The pedals arm only after both
      triggers have been released after you get in.
    * A shifts up. A quick grip-click away from the rim shifts: right is up, left is down. Left X shifts down only with `seat.vrxdown`.
    * Left stick X steers while no hand is on the rim. Left stick back is the handbrake, left stick click the lights, right stick click
      the horn.
    * Grip the inside door handle to get out. B / Y open the tablet; while it is up, driving input is dropped, and the pedals re-arm only
      after the tablet is closed.
  * **Monitor keys:**
    * W / S or Up / Down: throttle / brake.
    * A / D or Left / Right: steer.
    * E or Left Shift: shift up. Q or Left Ctrl: shift down.
    * Space: handbrake. H or L: lights. N: horn.
    * R: reset to the track. C: camera (cockpit, dash, bonnet, bumper, chase near, chase far, orbit). F: get out (stopped).
    * Mouse: look in the cockpit (yaw limited by camera_onboard.ini HEAD_MAX_DEGREES, 60 here; pitch 45°, ours).
    * On foot, E next to the door gets in.
  * **Gamepad** (AC's X360 defaults): RT / LT pedals, left stick steers, A / X shift up / down, B handbrake, Y horn, D-pad up lights.
    Ours: RB camera, D-pad down get out.
  * Keyboard, pad and stick steering pass through AC's own controls.ini filters. While seated, the client's conflicting keys are held off
    (the client's ESC "Exit" only while a gamepad is bound).
* **Passengers** (Seat + Net): another player opens the passenger door of your car and asks for the seat. Your client grants it, and the
  passenger rides on the right-hand seat, carried with the car. A parked car near a passenger is rebuilt with its cockpit. A passenger
  cannot get out above 10 km/h.
* **Physics** (Physics): each car's data.acd is used: engine and power.lut, turbo, gearbox, differential, suspension, tyres (DX / DY
  refs, load sensitivity), aero wings, brakes, ABS / TC levels, fuel, and the setup defaults. It runs as a sub-stepped chassis on the
  track's tagged colliders with grip per surface. It has auto-clutch / auto-shifter aids, a pit-lane limiter, reset to the track,
  fuel use and tyre-wear rates from the lobby, and contact with remote cars (they are kinematic).
* **Sound** (Audio): the car's own FMOD bank (FSB5 PCM) is rebuilt into the engine: interior and exterior, on / off load layers, rpm
  crossfades and a cone low-pass. Also turbo, gears, skids per surface, wind, hits and scrapes, door, horn, and the track's surface
  sounds. Remote cars are positional, with doppler (toggle) and a 12-car budget. UI and start-light beeps come from the install.
* **HUD** (Hud):
  * The monitor overlay shows the AC-style rpm ring, gear, speed, lap / best / last, delta, position and lap count, the standings tower,
    start lights, flags, pit limiter, wrong way and messages ("FALSE START!", "CUT DETECTED", penalties).
  * The VR panel sits beside the wheel over the centre console, with its top edge under the dash line. `hud.vr.place` puts it straight
    ahead instead, and a passenger always gets it straight ahead.
  * The HUD art comes from the install, the fonts are Segoe UI from `content/fonts`, and units follow AC's gameplay.ini.
* **Racing rules** (Race):
  * Sessions: practice (open or timed), qualify, race by laps or time, hotlap. Rules use AC's server_cfg names and defaults, and open
    courses (A to B) are supported.
  * START puts every joined player on the grid (qualifying order, then best lap, then join order; AI behind), seated and frozen. Then
    come the 5 reds on the instance clock (one second apart, a random hold, lights out) on the track's lamps, the HUD and the beeps.
  * Jump start follows the lobby rule (none / back to pits / drive-through; "FALSE START!" and +5 s, ours).
  * Laps and sectors are timed through the AC_TIME gates. A lap is invalid when more wheels than ALLOWED_TYRES_OUT leave
    IS_VALID_TRACK, and also after a reset. Positions come from laps plus fast_lane.ai progress.
  * The pit lane has an 80 km/h limiter (ours) with the limiter light.
  * Finish: the flag, a 30 s results table (ours), then back to practice in your pit box. RESET CAR / R key (once per 1.5 s), BACK TO PITS.
* **AI** (Ai): the instance master runs AI cars (up to 18) that follow fast_lane.ai with each car's ai.ini, the layout's ai_hints.ini, a
  difficulty level, traffic and overtaking, pit exit, recovery and fuel. They use the same physics as players and are drawn on every
  client.
* **Multiplayer** (Net):
  * The scene has 50 player slots plus race-control, AI, race and host blocks.
  * Remote cars are drawn with the real model and skin, interpolated, with steering, wheels, lights, doors, horn and dash laps. Their
    engine sound comes from rpm / throttle. A grey stand-in shows until the model is built.
  * Other sync: passengers, lap and timing data. A ghost rule applies in the pit lane, and cars over 1.5 km away are hidden.
  * The instance master publishes the race command, grid, results and AI cars, and a new master takes over (Halo's rule) and keeps the
    session.
  * Builds with another net protocol (this is protocol 3: the damage pair in ints 42-43, AI damage in "Ac Net Host 0") are refused cleanly and logged.

## How to test

**Solo (one client):**
1. Upload the world and enter with a whitelisted name. Check Player.log for `Access: ... is on the whitelist` and `Boot: install ...`.
2. You stand in the AC showroom with a car and the menu panel. Open the Somnium tablet and find ASSETTO CORSA on its home screen.
3. DRIVE: pick a car and skin; the showroom car changes. TRACK: pick a track and LOAD. The loading card appears, then you stand beside
   your car in your pit box.
4. Walk to the driver's door and open it (VR: point and trigger; monitor: E or click). Sit in. Check that the horizon stays level when the
   car pitches (VR) and that the wheel sits ahead.
5. Drive out of the pits: the limiter light should go off at the pit exit. Do a lap and check the lap time, sectors and delta on the HUD,
   and a cut (4 wheels on the grass). Try R / RESET CAR, BACK TO PITS, the C cameras (monitor) and the gears.
6. RACE LOBBY: set Race, 2 laps, AI 3, then press START twice. You should get the grid, seated, then the 5 reds with beeps and lights out.
   Race against the AI; a jump start gives "FALSE START!". After the finish and results (TIMING page), you are back in practice.
7. GET OUT when stopped; you stand beside the left door. LEAVE TRACK brings back the showroom.
8. Listen: idle versus full throttle inside the car, gear changes, skids, the wind.

**Two players (two clients, both whitelisted):**
1. Both enter. The first one is the instance master (RACE LOBBY shows it). Each picks a car.
2. The master loads a track: the second player's game loads it too (or JOIN SESSION from the hub). Each sees the other's car with its skin
   in the other pit box (a grey box for a moment first).
3. Drive near each other. Check that the other car moves smoothly, its wheels steer, its lights / brake lights / horn / door work, and its
   engine is heard. Bump gently (the local car feels the remote one).
4. Passenger: park, and let the second player open your passenger door. They should sit beside you and ride along; check that both views
   stay steady.
5. Race: the master sets Race, 3 laps, then START. Both should be put on the grid and seated, see the same lights at the same moment,
   race, and get the same results table with the same order and times.
6. Hand-over: the master leaves mid-session. The other player becomes master (`Net: took over as instance master ...`) and the session
   keeps going (AI cars stay).
7. Send both Player.logs.

## Player.log lines to look for

* `Assetto Corsa Dreamport - iteration 1 (2026-11-01): ...`
* `Access: <name> (id ...) is on the whitelist: starting the Dreamport`
* `Shaders carried: SomniumAssetto/PerPixel, ...Multilayer, ...Sky, TextMeshPro/...` (no "missing" in that line)
* `Boot: install <root>, 86 owned cars (176 listed), 35 playable layouts ...`
* `Front: the showroom hub and its menu are up ...`, `Tablet: the app icon carries the launcher's logo ...`, `Fonts: Segoe UI built from ...`
* `Net: started (Fusion session, protocol 3) ...`, `Net: the instance master is ...`, `Net: race command seq N applied (...)`
* `Session: Hub -> Loading`, `Loading: <track> ...`, `Track: look: weather ...`, `Track: built ...`, `Session: Loading -> Practice`
* `Car: built <car>/<skin> LOD ... damage normals flat n`, `Physics: rig <car> (Local) built ...`, `Physics: <car> spec: mass ...`
* Iteration 2 (damage): `Physics: damage rules from assetto_corsa.ini damage_displayer.ini engine_smoke.ini server_cfg.ini gameplay.ini: ...`, `Physics: <car> damage box from the hull ...`, `Physics: <car> first damage (multiplier n %): zones F/R/L/R/C ...` (events `AcEvents.CarDamaged` / `CarCollision`, Game/Shared/AcEvents.cs)
* `Seat: a car can be got into ...`, `Seat: VR pedals armed`, `Seat: wheel ready in <car> ...`, `Seat: camera cockpit ...`, `Seat: got out at ...`
* `Hud: art loaded (29 images, ...)`, `Hud: units ...`
* `Race: command seq N ...`, `Session: Practice -> Grid`, the race phases, and `Race: ...` finish / results lines
* `Ai: <name> lap ... | under target x %, ...` (the AI diagnostics, first 3 laps of each AI driver)
* `Net: remote car <name> <car>/<skin> built ...`, `Net: <name> left`, `Net: took over as instance master ...`
* `Net: player n ... runs net protocol x, this build 2: refused ...` (a mismatched build)
* Warnings and errors: `... failed`, `... threw`, `Shaders: '...' missing`.

## First client run: must test

In this order. Each item is something the lab cannot show, or something changed in the last pass that has not run in the client.
1. **Gate and boot:** the `Access:`, `Shaders carried:` (no "missing") and `Boot:` lines; the ASSETTO CORSA icon on the real tablet.
   Read the small tablet text: at 720p thin letters dropped out in the lab ("T MING", "PRACT CE").
2. **Getting in:** load magione, walk to the door and sit in, in VR and on a monitor. The horizon must stay level (heading only), and the
   hands must reach the wheel rim of a real avatar. On a monitor, check the keys, the C cameras and that the client's keys are held off.
3. **Braking and kerbs (physics core changed in the last pass):** brake at 100 % from top speed into magione turn 1. The car must not
   dive, scrape or spin. Then ride the kerbs on a kerb-heavy track (imola, nurburgring). The suspension now follows the road plane, and
   kerbs that slope under one wheel have not been tried.
4. **HUD:** on a monitor, check that the gear digit and speed on the dial can be read on a light background (the dial has no backdrop).
   In VR, check how far you have to glance to see the panel.
5. **Sound:** in the 458, compare idle and full throttle from inside (lab: about +20 dB(A), as in the offline mix). Then listen from
   outside the car: the lab measured +23.7 dB(A) idle to full throttle there, against +16 in the offline mix (known, open). Say whether
   full throttle from outside sounds too loud next to idle.
6. **Race with AI 3** (magione): the AI should no longer touch your car on the grid (lab: 0 contacts in 2 runs). **Expect the AI right
   behind you to stop behind your car** and be put back on the line after about 5 s (known, open; its lap 1 is about 20 s slow). Watch
   how much the AI slows when it passes a stopped car, the AI pace, and whether the AI cars stay on the road for the whole race.
7. **Two players:** remote car and sounds, passenger, lights at the same moment, the same results table. Then the master leaves
   mid-session: the other player takes over, the AI cars stay, and the lobby keeps the session's rules (a new fix, checked by build only).
8. LEAVE TRACK brings back the showroom. Send Player.log (two logs for the two-player test).

## Verified and not verified

Verified in the Unity editor's play-mode lab on the real install (`DevTools/lab.sh`), with the pictures looked at. **Order matters:** the
last review changed the physics core's suspension speed (`AcChassis.Step`) after most labs had run. These ran after that change:
`physics-458-magione`, `physics-flat`, `physics-fleet`, `cars-458`, `seat-enter`, `audio-458`, `ai-magione` and `ai-race`. Offline,
the change checks out on the whole fleet: 86 cars, 0-100 x0.999, braking x0.989, cornering x0.987, no car newly unstable.

* **Boot** (`boot`): the install is found, with 86 cars and 35 layouts.
* **Tracks:** `world-magione`, `world-monza` and `world-track spa` pass. The rotated tracks also pass (polish pass): `world-highlands`
  (ROTATION -17) and `world-nurburgring` (ROTATION 180). Every grid slot and pit box sits on a tagged surface. Lamp 0 is ahead of the
  pole and faces it. The start lights are right in every state, the worst load frame is 16–19 ms, and memory comes back after unload.
* **Cars:** `cars-458` (wheels, steering, doors, lights, smooth paint) and `cars-gauges` (needles turn clockwise, digits read correctly)
  pass. `cars-458` passes again after the suspension fix: wheel bottoms 0–12 mm above the ground, wheels centred in the arches.
* **Physics:**
  * `physics-flat` (abarth500) passes after the suspension fix: 0-100 9.05 s, 100-0 35.9 m, 0.95 g, straight line 0.00 m, ride
    height at static, tyres on the plane. Offline now gives 100-0 38.0 m and 0.94 g (the lab's reference numbers were updated).
  * `physics-fleet` (first Unity run) FAILS 85/86: `bmw_m3_e92_drift` settles at CG 0.418 m against a static 0.407 m and still creeps
    (0.028 m/s after 2 s). Its collider.kn5 hull reaches 4–5 mm below the ground at static ride height, and PhysX lifts the car.
    `bmw_m3_e92_s1` (+9 mm) and `bmw_m3_e92` (+3 mm) pass narrowly. Every other car settles at 0 mm.
  * `physics-458-magione` has two parts:
    * Pit-box placement and surfaces pass.
    * The new steered stint FAILS. Under braking into turn 1 the CG now stays at 410–423 mm and the smallest floor gap is 32 mm
      (before the fix: 371 mm and 3 mm). The run fails when the simple lab driver runs wide at the turn-2 hairpin and spins after a
      kerb/grass landing.
  * The earlier "54 m in 6 s" was the unsteered car hitting a wall.
* **Seat:** `seat-enter` passes 38/38, re-run after the suspension fix.
* **HUD:**
  * `hud-desk` passes 30/30 (polish pass), and `hud-vr` passes.
  * In the hub pictures, the dial's gear digit cannot be seen on the white floor.
* **Front:** these pass:
  * `front-tablet` 34/34;
  * `front-hub` 22/22 and `front-session` 25/25 (both polish pass);
  * `front-loading` twice (first runs; the badge overlap is fixed).

  The lobby re-seed and hand-over fix is checked only by the build.
* **Race:** `race-solo-magione` 32/32 (polish pass: grid within 0.00 m, lights, laps, results) and `race-solo-actions` pass. The
  lap-cut rule is covered only by `race-solo-actions`' cut sensing.
* **Audio:**
  * `audio-drive` passes (polish pass).
  * `audio-458` passes (re-run with the other lab slot idle, both captures full length). Inside the car, Unity's real output gives
    +20.7 dB(A) from idle to full throttle (offline +20.6). The old +3..+5 dB was a lab measuring error plus the distortion running
    after the faders, both fixed.
  * Outside the car Unity gives +23.7 dB(A) (idle -50.3, full throttle -26.6) against the offline +16. The stereo-spread change did
    not fix this. Against the lab's own voice render (+18.0), Unity's idle is 2.5 dB quieter and its full throttle 3.2 dB louder.
  * All of this is by measurement, not by listening.
* **Net:** `net-loopback` passes 30/30 (polish pass), including the master take-over with an inherited AI car.
* **AI:**
  * `ai-magione` **passes** (GT86, before and after the suspension fix): lap 1:40.5–1:40.6, x1.06 of the plan and x1.09 of Kunos'
    gold time, 0 recoveries, never off the track. It failed 2 of 3 runs before the suspension fix (hull spin at turn 1).
  * `ai-race` (458, magione, 3 AI) **passes** in the last 2 of 3 runs: 0 contacts with my standing pole car (it was 2 at 7 km/h),
    all 3 AI finish 2 laps, closest approach 2.4–2.7 m centre to centre. The AI right behind me stops about 0.3 m short of my bumper
    and is reset as "stuck" after 5 s (lap 1 2:00.8 / 2:02.2). Lap 2 in traffic is 1:34.2–1:36.4 (x1.09–1.11 of the plan), against
    1:32.0–1:32.7 before this change. In run 1 that AI stood behind my car for the whole race; that was fixed for runs 2 and 3.
  * `ai-magione` passes twice more with the new traffic code (1:40.46 / 1:40.61), so solo pace is unchanged.

**Not lab-run:**
* the drag-strip tree;
* cars-lod, cars-leak, cars-leak-ai, cars-five, cars-driver, cars-anim, cars-dash and cars-interior;
* hud-live and audio-abarth;
* a lobby hand-over.

**Not re-run after the suspension fix:** race-*, audio-drive, net-loopback, front-*, hud-*, world-*.

**Only a client or two-player test can show:**
* The real Somnium tablet link and icon, and the tablet text at headset resolution.
* VR comfort and the wheel grab with a real headset and avatar. The editor has no player body.
* The client's camera brain order, and the AcFirstPerson key holds.
* TMP fonts and shadows in a client build, and the far plane on a real camera.
* VR frame time.
* The Fusion transport on the upload server, and the clock offset between two PCs.
* Results agreement, master hand-over with real players, the lobby re-seed on hand-over, the cockpit swap under a seated passenger,
  and refusing to get out during a race.
* How the sounds actually sound.

## Known gaps

* **AI:**
  * The AI directly behind a car standing on the grid cannot steer round it in the gap. It stops short (no contact), its back-off
    does not free it, and the 5 s "stuck" reset does (about 20 s lost on lap 1).
  * Passing a standing car at speed slows the AI. In the lab one slowed to 67 km/h, and one stopped 19 m short and was reset.
  * AI lap times in traffic are about 3 s slower than before the grid-contact fix.
  * AI plans are about 4 % slower than Kunos' gold times. Our chassis reaches about 0.8 of the tyre grip value, so the plan uses 0.78
    (ours).
  * The abarth500 resets twice at imola in the offline sim.
* **Physics:**
  * The fleet's 0-100 is 1.28x the ui_car.json times, and the 458 does about 4.1 s against 3.4 s. The split, checked offline (energy
    balance closes, no double losses):
    * about 11 points are AC's own shift times and gas cut;
    * about 2 points are the auto-clutch launch window;
    * about 9 points are optimistic published figures (the ideal from the car's own data is 1.09x).
  * Whether AC's auto-clutch launches the same way is unverified in the game.
  * A kerb or grass landing can spin the car.
  * The new suspension speed follows the mean road plane; kerbs that slope under one wheel are untested.
  * The bmw_m3_e92 family's collider.kn5 hull touches the ground at static ride height. `bmw_m3_e92_drift` sits 11 mm high and creeps
    at rest; the s1 sits 9 mm high. AC may do the same (its colliders.ini says the 3D collider also hits the ground); unchecked.
* **Audio:**
  * The 458's exterior level is too loud: +23.7 dB(A) from idle to full throttle, against the offline +16. The stereo-spread change did
    not fix it.
  * Unity distorts each voice, while FMOD distorts the event's sum. Unity's distortion is also about 2 dB stronger than FMOD's formula.
  * The 458's bank has no limiter or backfire_ext event.
* **Rendering:**
  * The hub's white floor and walls clip to white, because AC's ppfilters / auto-exposure are not built.
  * Monza's sun glare shows pixel speckle, and spa's near road shows a fine line pattern.
  * Nurburgring's left run-off looks over-bright.
  * The far terrain is low-res (as in the files), and magione's rubber groove looks streaky from above.
* **HUD and tablet:**
  * The monitor dial has no backdrop, so its gear digit is lost on light ground. The time box's small labels show the hub roof through.
  * The VR panel takes a glance of about 47° sideways and 25° down, and sits low on open-wheelers (lotus_49). "CUT DETECTED" has not
    been seen on it.
  * Thin glyphs drop out of small tablet text at 720p.
* **Not simulated:** tyre temperatures, damage, fuel-tank position, Ackermann.
* **Not built:** yellow / blue flags, cone and DRS-board physics, crowds, reverb, wipers (they stay parked), and the KERS / FUEL_CONS
  dash items.
* Time of day and weather are per player (not synced).
* **Small issues:**
  * The driver door may still be open just after you get in.
  * The garage setup column says "Reading..." for a moment on first open, and slider labels touch their bars.
  * The batch lab spams "no audio listeners" (lab only).
* **Old builds:** a protocol-1 build never checks the protocol number, but none was ever uploaded.
* **Invented numbers** (marked `// ours`) to judge:
  * the pit limiter 80 km/h, results 30 s, the +5 s jump start;
  * the VR panel size and placement, the seat camera fallbacks, the steering yaw limits;
  * the scrape hold, the AI recovery rules, the AI plan grip 0.78;
  * the suspension's 0.2 steep-road guard, the loading-card row offsets.

## License note

There is no third-party code in this world. The code was written for it or ported from our own Dreamports (Halo, Euro Truck Simulator).
The Assetto Corsa file formats (kn5, data.acd, ksanim, FSB5 / FEV, ai splines) are read from the player's own files and were worked out
from those files. Nothing from Assetto Corsa ships with the world. Assetto Corsa is © Kunos Simulazioni; the world is for private testing
by the whitelisted team.
