# A - Audio (brief)

Read first: `DevTools/AGENTS.md`, `DevTools/agents/DESIGN.md`. Paths: `HALO/`, `ETS/` as in AGENTS.md; code root `Assets/#User/SomniumAssetto`.

## Scope
Every car sounds like itself in AC, rebuilt from its own FMOD bank (FSB5 PCM16 samples + FMOD Studio 1.08 metadata, decoded by D):
engine interior/exterior with rpm-region crossfades, auto-pitch and throttle on/off load layers; gear up/down/grind; turbo + blow-off;
limiter; backfire; tyre skids per surface (common.bank event:/surfaces/*); wind; collisions (common.bank); door; horn. Remote and AI cars
get 3D exterior engine + skids from the networked rpm/throttle. UI clicks and start-light beeps (`AcSfx`).

## You own
`Game/Audio/` (stub `AcCarAudio.cs` with `AcCarAudio` + `AcSfx` - fill it; add `AcSoundBank.cs` (bank -> AudioClips cache per car),
`AcEngineSound.cs`, `AcAudioBudget.cs`, ...), `Data/Audio/` (only if you need Unity-free helpers beyond Data/Fmod - ask the lead before
changing Data/Fmod), `Editor/AcLabAudio.cs`, harness probe `DevTools/harness-ac/ProbeA.cs`.

## Port from
- `ETS/EtsTruckAudio.cs` (2173) + `ETS/EtsGameSound.cs` (1376: `EtsGameEventPlayer` l.423 = one AudioSource per instrument, `Drive`
  l.492, parameter smoothing l.645) + `ETS/EtsCarAudio.cs` (852: traffic/puppet cars). Bank parsing is NOT to be ported: D's
  `FmodBank` / `Fsb5` already read AC banks (86/86 + common.bank).
- `HALO/Game/Audio/HaloAudioSystem.cs` (710: AudioSource pool, 3D settings), `HaloAudioBudget.cs` (68: voice budget), `HaloAudioLoop.cs`.
- Data: `FmodBank.Read(car.BankPath, FmodGuids.Load(install.GuidsTxt))` (per-car GUIDs.txt when `car.GuidsPath` != null),
  `EngineLayers(interior)` (RpmMin/Max, PitchRefRpm, VolumeDb, Load, `Gain(rpm, throttle)`, `Pitch(rpm)`), `Event("gear_int")`,
  `ReadSampleFloats(sample)`, `Fsb5Sample` loops; `AcSurface.SoundEvent` ("event:/surfaces/grass").

## Spec sections
`research/ac_physics_sound_rules.md` 2.1-2.7 (banks, FSB5, Studio 1.08 metadata, the engine event decoded for ferrari_458, other
events + their parameters, networking the sound), 5.1/5.4 (open: minimum pitch, curve shapes) + Critic addendum. `research/ets_vehicles.md`
10 (ETS pipeline: 2D in cab / 3D outside, InteriorAttenuation, puppets within 300 m). DREAMPORT_GUIDE section 4 (no OnAudioFilterRead).

## Must do (acceptance)
1. `AcCarAudio.Attach(root, car, spec, local)`: decode the bank on a worker (float samples), build AudioClips on the main thread
   time-sliced (`AudioClip.Create` + `SetData`, never one huge frame), cache per car id (a second car of the same model reuses the clips).
2. Engine: interior (`engine_int`) as 2D when the listener is in this car's cockpit (`SetInterior(true)`; S/AcEvents.SeatChanged tells you),
   exterior (`engine_ext`) 3D otherwise; every layer's gain/pitch from `Feed(telemetry)` each frame (rpm, throttle; smoothing like ETS
   40/70/100 ms); on/off load layers crossfade with throttle; limiter sound while `Telemetry.Limiter`; turbo/BOV from BoostBar.
3. One-shots: gear (gear_int/ext; also on `AcEvents.GearChanged`), backfire on lift-off at high rpm (bank events), door, horn, collision
   (`AcEvents.CarImpact` -> common.bank car_hit / crash / scrape by speed), skids per wheel from `Skid` + `Surface.SoundEvent`, wind by speed.
4. Remote / AI cars (`local == false`): exterior engine + skids only, silent beyond 300 m (// ours, ETS value), voice budget (Halo).
5. `AcSfx.Ui(name)` (launcher/themes/default/audio/*.wav or common.bank UI events), `StartBeep()` / `GoBeep()` (common.bank race start
   sounds if present - search the GUIDs.txt event list; else // ours tones are NOT allowed: use a bank sample).
6. Volumes from AcSettings ("audio.master", "audio.engine", "audio.effects") registered in `AcSettingsUi` (group "Audio").
7. No allocation per frame; AudioSources pooled; everything stopped and freed in `Dispose()`.

## Player.log lines to add
`Audio: <car> bank: N samples (M MB PCM), engine_int k layers, engine_ext k layers, events found [...] in X ms` · `Audio: clips built for <car>
(cache hits n)` · `Audio: common.bank: ...` · `Audio: missing event <name> in <car>` (once).

## Verify
build.sh x3, check.sh; harness `Cmd_engine <car> <rpm> <throttle>` printing every layer's gain/pitch (compare with spec 2.5's 458
table); lab `audio-458`: run the engine through a rpm sweep with the AudioListener, write a WAV of the mix (AudioListener.GetOutputData or
render the layers offline from the same gains) and report levels; the "by ear" check is the user's - say so.

## Consumes / provides
Consumes: FmodBank/Fsb5 (D), AcTelemetry (P / N), AcEvents (CarImpact, GearChanged, SeatChanged), AcCarSpec (P, optional).
Provides: AcCarAudio (P's AcCarRig attaches it; N feeds remote cars), AcSfx (R beeps, F clicks).
