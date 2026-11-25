# A - Audio: status notes (second pass, 2026-11-01)

## What exists (all compiling; build.sh runtime/server/editor 0 errors, check.sh "all 117 proven")
Data (Unity-free, compiled by the harness):
- `Data/Audio/AcSndBank.cs` - A's parse of AC's FMOD Studio 1.08 bank metadata (same chunk offsets as D's FmodBank): events, WAIB + MUIB
  multi instruments, regions, crossfade / bus automations, auto-pitch, pitch automation in semitones/24, timeline loop flag, static bus
  volumes + mixer routing into common.bank's grp_* groups, and NEW: **DSP effects** (`AcSndFx`: BEFB {guid, placement, FMOD_DSP_TYPE} /
  PEFB {plugin name} + PMEF params; BUS chunk list 1 = pre-fader, list 2 = post-fader; CTRL property 1000+i = effect param automation):
  per-event `Spatialiser` (pan DSP with 3D blend > 0: min/max distance, rolloff), `Effects` with instruments, `DownstreamDb`.
  -42 dB silence floor now per stored value (instrument, each bus, each mixer group), never on the sum.
- `Data/Audio/AcCarSounds.cs` - sounds.ini / engine.ini / geometry; NEW reads [GEAR_EXT] DISTANCE_SCALE, [BACKFIRE] VOLUME_SCALE_OUT (logged,
  not applied: by the pipeline PDF's DISTANCE_SCALE rule these are track-camera multipliers; car cameras = 1, and every Somnium listener is
  an avatar present in the world).
- `Data/Audio/AcOfflineMix.cs` - offline renderer; NEW master FMOD Distortion (k = 2L/(1-L), y = (1+k)x/(1+k|x|) before the downstream
  faders) and `RmsA` (A-weighted RMS, IEC 61672 poles).
Game (Unity):
- `AcCarAudio` - contract component. NEW: feed timeout 0.5 s -> 0.3 s fade + stop; remote horn from `Telemetry.Lights` Horn bit; exception ->
  Dispose; collisions: hit only on a new contact (gap > 0.3 s), P's 0.1 s repeats drive car/scrape (HitsCar) or track/scrape with the
  sliding speed; remote/AI cars build their sound set (players, sources, decode requests) only when first audible; collisions + surfaces
  for remote/AI too; passengers (SetInterior on a non-local car) get the full cockpit set, doppler off while aboard; settings changes
  (doppler, interior3d) re-applied at once (AcAudioMix.ConfigVersion); 4 wheels x every common.bank event:/surfaces/* built with the common
  set (no eviction, no player made while driving); WAV_PITCH on surface players; backfire throttle bus follows the live throttle; surface
  cache cleared on AcSession.Unloading / host close; re-Attach of a bank-less car no longer duplicates emitters; one-time logs.
  Added public members: `Sources`, `Built`, consts `FeedTimeout`, `NewContactGap`.
- `AcEventPlayer` - every AudioSource made at construction (= sound-set build); per effect set a child GameObject "Ac Snd <event>" with
  AudioLowPassFilter (DSP lowpass / lowpass simple / "FMOD Distance Filter"), AudioHighPassFilter, AudioDistortionFilter ("FMOD
  Distortion"); automated params every frame (engine_ext cutoff on the event cone angle, engine_int distortion level on rpm); bank
  spatialiser distances (`AcAudioMix.Configure(src, ev, spatial, aboard)`); `Reconfigure(spatial, aboard)`, `Clear(feed)`; air_pressure 1.1.
- `AcAudioWorker` - upload in fixed 16384-float chunks from one shared buffer (tail re-writes the overlap: no allocation); `IUploadSink`
  replaces the per-clip lambda; `Reserve`.
- `AcSoundBank` - IUploadSink; "clips built" once per bank; meta log lists 3D events + effects.
- `AcAudioBudget` (AcAudioMix + AcAudioHost) - bank spatialiser in Configure (SDK table only as fallback), ConfigVersion, host subscribes
  AcSession.Unloading / TrackBuilt.
- `AcSfx` - NEW `HubAmbience(bool)` (F -> A; content/showroom/showroom/track.wav, time-sliced upload), first UI click plays when its wav
  is read (within 0.5 s) instead of being dropped, track surfaces' + collision sounds requested at TrackBuilt.
- `Editor/AcLabAudio.cs` - `audio-458`, `audio-abarth`, NEW `audio-drive` (not run - phase rule).
- `DevTools/harness-ac/ProbeA.cs` (+ copy in agents/A/harness): sndevents, engine, sndcensus, render [nofx] (dBFS + dB(A)), carsounds,
  chunks, NEW sndfx <car|common|all>.

## Verified (offline, on the real install)
- Critic item 1 (interior idle louder than WOT): the model was missing the engine_int master **FMOD Distortion automated on rpm**
  (458: level 0 at 1200 rpm -> 0.25 at 7000; 12 of 86 cars have one on engine_int: 4 on the master bus, 8 on a group bus). With it and A-weighting:
  `render ferrari_458 int`: idle -46.7 dB(A)FS vs full throttle 8681 rpm -26.1 (plain RMS -17.1 vs -19.0: the 900 rpm idle is bass);
  ext idle -37.6 vs -21.5 dB(A). abarth500 int -46.2 vs -22.1, bmw_m3_e30 int -44.3 vs -22.4. The lab check is now "WOT >= 3 dB(A) louder".
- `sndfx all` (86 cars): engine_ext spatialiser 6/350 r3 + Distance Filter 86/86, cone lowpass 80/86; engine_int spatialiser 7/50 86/86;
  gear_int 2D 86/86; limiter lowpass on cone 58, 2D in 24; three-EQ / param-EQ (dsp 12) / reverb (dsp 19) exist and are NOT reproduced.
- Frequency automation encoding fitted on SDK XML vs banks: Hz = 22000 x (pos/10)^3.765 (limiter 4500 Hz = 6.565, backfire_ext
  4000 = 6.348, engine_ext template 5400 = 6.891 -> predicted 4512 / 3976 / 5414 Hz). Static PMEF params are plain Hz (formula_k 3100 = XML).
- Pan PMEF decode = SDK XML (engine_int 7/50 LFE upmix, engine_ext 6/350 rolloff 3 sound size 0.3 min extent 120).
- `sndcensus`: 86/86 banks parse, 0 failures; A vs D gains equal except where A models more (throttle-sheet pops, multi instruments).
- Compile x3 0 errors in A's files; check.sh all 117 proven; shared harness-ac builds.

## NOT verified (needs the lab / client)
- Nothing played through Unity yet (phase rule): sources, filters, upload chunks, budget, toggles, passenger, stand-in, hub ambience.
- Whether Unity applies AudioDistortionFilter before or after AudioSource.volume (FMOD: before the master fader); per-source vs summed
  distortion; whether filters on a GameObject with several AudioSources process each source (Unity docs: all sources on the object).
- FMOD Distance Filter shape (log from 22 kHz at the emitter to its cutoff at its max distance - ours); three-EQ / param-EQ not reproduced.
- Earlier beliefs unchanged: auto-pitch min pitch 0; skid gain shape; backfire arming; decay = seconds since the last cut; impact angle 90.
- Interior attenuation of other cars' sounds (ETS InteriorAttenuation): not applied - AC has no such value in its data (its banks split
  own-car _int/_ext; other cars come through their spatialisers).

## Lab steps (later; DevTools/lab.sh)
1. `lab.sh audio-458` - clips ready + longest load frame; interior sweep (voices 1..16, engine_int distortion > 0.15, WOT >= 3 dB(A) over
   idle, no source made while driving), exterior sweep; cone low-pass > 15 kHz ahead / < 9 kHz behind; gear / door / horn; one hit then scrape;
   remote 458 from the cache at 60 m, silent at 400 m; UI click + beeps; Dispose frees all sources. Listen to audio-458-int/ext.wav.
2. `lab.sh audio-abarth` - backfire on lift-off, limiter, turbo, gear grind.
3. `lab.sh audio-drive [-actrack magione -accar ferrari_458]` - RequestLoad -> R's car via AcCarRig.Spawn; emitters at the wheels; S seat +
  cockpit/chase -> interior/exterior; P drive: engine_int, wind, skid_int, gear_int, surfaces touched; CarImpact one hit then scrape;
  interior3d toggle 2D/3D at once; Kind Remote rig fed like N (plays, doppler, doppler toggle), passenger cockpit set, fade when not fed;
  stand-in surfaces; 12-car fleet -> <= 10 audible, 2 farthest never built; RequestHub -> nothing left playing.
4. `[SomniumAssetto] Audio:` lines in lab.log.

## Player.log lines
`Audio: started (...)` · `Audio: listener '<name>' ...` · `Audio: <car> bank: N samples (M MB PCM), engine_int k layers, engine_ext k layers,
events found [...], n events 3D by their spatialiser, effects [engine_ext: lowpass~Event Cone Angle, FMOD Distance Filter; engine_int: FMOD
Distortion~rpms ...], routed into the common.bank mixer groups in X ms; sounds.ini: ...` · `Audio: <car> sound set up as local|remote/AI car:
P event players, S sources, L engine layers, engine at rear|front|CoG, engine_int 3D|2D` · `Audio: clips built for <car> (...)` (once per bank) ·
`Audio: <car> engine sound ready (...)` · `Audio: <car> switched to interior (engine_int) sounds [(riding in a remote car)]` ·
`Audio: event <ev> has a <kind> effect ... not reproduced` · `Audio: track surfaces' sounds requested at track load (n surfaces ...)` ·
`Audio: hub ambience from content/showroom/showroom/track.wav (n s loop)` · `Audio: <car> failed, its sound is stopped: ...` (error).

## Requests
Filed now: A -> N (stand-in audio on rc.FrameStandIn), A -> lead (engine_int 3D default per the banks vs the brief's 2D; master = "volume";
AcSndBank re-parse acknowledgement). Answered: F -> A hub ambience (DONE), P -> A grind (done). Already done by others: N door/horn edges,
F Preload in BootDone, P CarImpact / horn / Feed.

## Numbers that are ours
Headroom 0.5; silent 0.0005; loop stop 0.5 s; max 10 remote cars; interior radius 1.5 m (0.45 m unsignalled); engine off < 50 rpm; feed
timeout 0.5 s + 0.3 s fade; new contact gap 0.3 s; skid smoothing 50 ms; surface / scrape decay 0.5 s; transmission_load = 50 x throttle;
drivetrain speed = road speed in gear; impact angle 90; air_pressure 1.1 (still air); door emitter; GoBeep x2; gear grind one pass; clip
cache freed after 60 s; upload pending cap 32 MB, chunk 16384 floats; big UI wav 262144 floats; frequency-position fit exponent 3.765;
Distance Filter shape. From the game: banks (instruments, buses, mixer groups, spatialisers, filters, distortion), sounds.ini, surfaces.ini
WAV / WAV_PITCH, 350 m (pipeline PDF), ETS smoothing 40/70/100 ms.

## Lab pass (2026-11-01, audionet agent)
- `audio-458` run 1: all PASS except "the repeats played collisions/track/scrape". Interior: 7 voices, engine_int distortion max 0.250
  on rpm, WOT -34.4 vs idle -37.7 dB(A)FS (+3.3, passes the 3 dB rule but far below the harness +20); exterior -32.6 vs -45.8 (+13);
  cone low-pass 20100 Hz ahead / 5611 Hz behind; gear / door / horn / one hit; remote 60 m plays, 400 m silent; UI; Dispose. No
  exceptions. Missing in the 458 bank (logged): limiter, backfire_ext.
- Scrape: diag showed the repeat contacts never came (the batch lab runs at ~2000 fps, 100 frames < P's 0.1 s repeat) - the lab's
  contact phase is now time-based. Real fix too: the scrape "decay" ramped 2/s from each repeat, so with P's 0.1 s repeats the banks'
  curve (decay 0 -> 0.3 = -42 dB) pulsed down to about -28 dB between repeats. Now decay is HELD at 0 while repeats keep coming
  (`ScrapeHold` 0.15 s, ours) and then counts seconds since the contact ended (as the limiter's decay). `DescribeCollisions()` added (lab).
- `audio-drive` run 1 (magione + 458, real rig + P): PASS engine_int, wind (65 km/h), skid_int, gear_int (21 changes), grass surface,
  no source made while driving, CarImpact hit then scrape, interior3d toggle, remote plays + doppler + doppler toggle, passenger cockpit
  set (52 -> 87 sources), fade when not fed, stand-in surfaces, budget 10 of 12 audible, RequestHub silent. FAIL: the 2 farthest fleet
  cars built their sound set - `Audible` defaulted to true, so every remote car built before the budget's first decision. Fixed:
  `Audible = local` in Init. NOT exercised: S's seat path (no avatar in batch) - SetInterior was called directly.
- Re-runs: `audio-458` run 3 **all PASS** (one-shot phase 1.27 s, 9 contact repeats, scrape heard; int WOT -32.6 vs idle -38.0 dB(A)FS,
  ext -32.4 vs -45.2). `audio-drive` run 2 **all PASS** (budget 10 of 12 audible, the 2 farthest never built: built 10 of 12).
- OPEN: the 458 interior WOT/idle gap in Unity is +3..+5 dB(A) vs +20 in the offline mix (exterior +13 vs +16) - the 3 dB lab rule
  passes, but the in-Unity interior idle is too loud relative to WOT; suspect order of AudioDistortionFilter vs volume / per-source
  distortion (listed under NOT verified above). Listen to DevTools/lab/audio-458/audio-458-int.wav.

## Fixer pass 2026-11-01 (review findings; build.sh 0 errors x3; no lab run)
- AcAudioWorker: each worker thread has its own stop flag + generation; Stop() starts a new generation, drops a still-running job's result (no
  Done in a later session), zeroes the pending-floats count, and destroys the AudioClips of uploads still queued (they leaked). Not verified in a lab.

## Polish pass 2026-11-01 (458 interior loudness; build.sh 0 errors x3, check.sh all 117 proven; audio-458 x3)
Root causes (evidence):
1. The "+3..+5 dB(A) interior WOT over idle" was mostly the LAB's voice render, not the runtime: it re-seeked every frame to
   AudioSource.timeSamples (updated once per DSP buffer) at ~2000 fps = a broadband buzz (60 clicks/s at idle, 948 at WOT, 8 kHz band +30 dB)
   that swamped the quiet idle in dB(A). Fixed in Editor/AcLabAudio.cs by the cut-off attempt of this pass (one continuous play position per
   source) + a capture of Unity's REAL mixer output (AudioListener.GetOutputData stitched; `OutCapture`). Its 05:33 run: Unity output int
   +24.9 dB(A) (idle -50.1, WOT -25.2), ext +23.0; voice render int +22.6, ext +17.4.
2. Real runtime deviation left: Unity runs a source's filters AFTER AudioSource.volume (lab probe: 0.5 sine, distortion 0.7, volume 1 vs 0.05
   differ by 19.25 dB, not 26.02; the 0.05 phase = clean -26 dB + the filter's small-signal gain). AcEventPlayer fed the distortion the signal
   already through the bus's downstream faders (-10 static -3 mixer = -13 dB on the 458 engine_int): the waveshaper worked as a linear
   (1+k) boost (+4.4 dB at level 0.25 above 7000 rpm) instead of FMOD's saturating shape on the pre-fader sum (offline fx vs nofx: +1.5 dB).
   Offline (harness render f458 int: idle -46.7, 8681 rpm WOT -26.1 dB(A)FS = +20.6) vs Unity +24.9 = the ~4 dB excess.
   FIX (Game/Audio/AcEventPlayer.cs): a distortion bus plays its sources at the level BEFORE its downstream faders (g / Down, scaled down
   together when one passes 1 - AudioSource.volume caps at 1) and an AudioEchoFilter (wet 0, decay 0, delay 10 ms) after the distortion
   applies Down x the car/player volumes (dryMix, set only on change). Gains are evaluated once into `_g/_p` first (no allocation).
   `Tap` reports Volume = source volume x that gain, DistortionGain = that gain (the lab render stays consistent).
   Lab probe: echo dry 0.25 = -12.03 dB on the clean and -12.04 dB on the distorted sine = a pure linear gain AFTER the distortion (runs 2, 3).
   Result, Unity output int idle vs WOT: +21.5 (run 1), +22.8 (run 2), +19.3 (run 3; capture 17.3 s) vs harness +20.6 / voice render +21..22
   (before: +24.9). PASS.
3. Exterior: Unity +22..23 vs harness +16 / voice render +16..17; the excess is broadband at WOT only. The 458's ext idle sample is MONO,
   the WOT layers STEREO; AcAudioMix set spread 0, so a stereo 3D source summed both channels into one point. The bank's spatialiser has
   3D MIN EXTENT (pan DSP param 16) engine_ext 120 deg, gear_ext 150, wind 220, wheel 40, engine_int 0. FIX (Game/Audio/AcAudioBudget.cs):
   AudioSource.spread = that min extent (FMOD's 3D spread). NOT confirmed: the ext Unity capture came out short in runs 2 and 3 (12.4 / 11.6 s
   of 19 - the DSP clock lagged game time under load with the other lab slot running) so no ext Unity number after this change; ext voice
   render +17.1 / +17.5 (it does not model spread). Re-run audio-458 when the other slot is idle and compare ext UNITY t1-2 vs t9-10.
Lab check changes (Editor/AcLabAudio.cs): probe phases e/f (echo gain), "measured" checks judged on rms (THD+N of the stitched capture
varies 0.6..14 % on the clean phase between runs - informative only).
Still open: per-voice vs sum distortion (Unity: one filter instance per AudioSource; FMOD: the event's sum) - the 458's 3 WOT layers saturate
a little less than in AC; Unity's distortion is +7.7 dB on the 0.5 sine vs 5.9 dB by FMOD's documented formula.
Harness: agents/A/harness/ProbeA2.cs `wavspec <wav> <t0> <t1>` (octave bands + clicks) compares lab WAVs with the harness renders.

## Last-gaps pass 2026-11-01 (integration: audio-458 ONCE with the other lab slot idle, after the spread change; no A code changed)
- audio-458 PASS (26 ok), both Unity captures full length: int 19.0 s, ext 18.7 s (slot A, slot B idle).
- Interior, Unity output: idle t1-2 -48.8 vs WOT t9-10 -28.1 dB(A)FS = +20.7 (harness +20.6; voice render +21.6). Matches.
- Exterior, Unity output: idle -50.3 vs WOT -26.6 dB(A)FS = +23.7 (before the spread change +22..23; harness +16; voice render this run
  +18.0 = idle -47.8 / WOT -29.8). So the AcAudioBudget spread (= bank 3D MIN EXTENT, set in Apply: s.spread = P(16)) did NOT bring the
  exterior down. Against the voice render, Unity's ext idle is 2.5 dB QUIETER and its WOT 3.2 dB LOUDER - both ends add to the excess
  (the mono idle may lose level under spread 120 deg while the stereo WOT layers still add up). Not investigated further here (A's model;
  no tuning without a basis). Check still passes (>= 10 dB). Distortion probe unchanged (a-d 7.73 dB, a-b 19.25 dB, echo dry -12.04 dB).

## Damage pass 2026-11-02 (iteration 2, DAMAGE_DESIGN 6)
Files: `Game/Audio/AcEventPlayer.cs`, `Game/Audio/AcCarAudio.cs`, `Editor/AcLabAudio.cs` (new scenario `audio-damage`).
- suspension_damage: `AcFeed.Constant(id, out v, suspDamageLive)` - a player decides once when built (`AcFeed.DamageOn()` = AcSession.Rules
  DamageMultiplier > 0) whether suspension_damage is FED or pinned at 0 as before. `AcCarAudio` writes `_feed[SuspDamage] =
  AcCarPhysics.Damage.MaxSuspDamage01` (P; remote cars from N's ApplyWire) every frame -> the car bank's wheel event layer flutter_4 (458:
  suspension_damage 0..1, bus -42 dB at 0.44 -> -0.17 dB at 0.75, harness `fev ferrari_458 wheel`). DAMAGE 0: pinned at 0 = iteration 1.
  Players built while damage was off stay pinned until the car is rebuilt (next spawn) - same rule as the paint textures.
- `public void Repair(bool starting)`: common.bank `event:/common/unscrew` at the start, `event:/common/screw` at the end (both exist, harness
  `fev common screw`), at the car (hit emitter moved to (0, 0.4, 0) // ours). Made with the common set when damage is on, else on first use
  (the car stands in its box). Plugged into R's `AcRace.RepairSound` delegate from `AcCarAudio.Init` (R calls it guarded).
- Glass break / engine blow: no event in any bank -> silence (unchanged). Optional A2 (CarCollision angle) NOT done: CarImpact path unchanged.
- Lab hooks: `AcCarAudio.LabDamageOn` (static), `LabSuspDamage` (-1 = the car's).
- Verified: build.sh 0 errors x3, check.sh 117 proven. Lab `audio-damage` PASS (5 ok): flutter voice volume 0.0000 at suspension_damage 0,
  0.024 at 0.6, 0.246 at 1.0 (458 rolling 80 km/h); Repair(true) started unscrew once, Repair(false) screw once.
- Not verified: flutter with a real damaged car on a track / remote car (needs N's wire + a crash), by ear.

## Damage pass 2026-11-02 (fixer)
- AcEventPlayer: suspension_damage live/pinned is decided every Drive (only for events that have the parameter: _hasSusp), not once at build; the flutter instrument is no longer skipped at build because of the pin (SuspDamage never counts as constant for _skip). At DAMAGE 0 the value stays pinned 0 -> outside/at its region exactly as before (audibly iteration 1; the wheel event may now decode one extra flutter sample).
- Verified: build.sh 0 errors. Not lab-run (audio-damage lab not re-run).
