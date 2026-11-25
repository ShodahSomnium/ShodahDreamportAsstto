# Assetto Corsa GAMEPLAY data: car physics INIs, sounds (FMOD banks), sessions / race rules, HUD

Research spec for the AC Dreamport implementers. Everything is read at run time from the player's install
(`E:\SteamLibrary\steamapps\common\assettocorsa` on the dev machine; the world must locate it, not hard-code it). Nothing ships.

Tags: **[VERIFIED]** = proven by running code on the real files (probe named) or by reading the file; **[READ]** = stated by a comment
or document inside the install (Kunos' own words), not cross-checked by measurement; **[BELIEF]** = my understanding / public
knowledge, not proven here; **[OURS]** = a number we must choose (mark it `// ours` in code).

Probes (dotnet 8 console, all under `C:/Users/derii/AppData/Local/Temp/claude/acscratch/physsound/`):
| probe | what | run |
|---|---|---|
| `acd/` | data.acd decryptor (same algorithm as `ac_car_formats.md` section 2) | `acd/bin/AcdProbe.exe <carDir> <outDir>` |
| `fsb/` | FEV RIFF walk + FSB5 lister + PCM16 -> .wav extractor | `fsb/bin/fsb.exe <bank> [nameOrIndex outDir]` |
| `acev/` | **AC (FMOD Studio 1.08) bank metadata parser**: events, instruments, rpm regions, auto-pitch, crossfade curves | `acev/bin/acev.exe <bank> <content/sfx/GUIDs.txt> [filter]` |
| `fsp/` | dumps the SDK FMOD Studio *project* XML (`sdk/audio/ac_fmod_sdk_1_9/Metadata`) | `fsp/bin/fsp.exe <Metadata> [eventName]` |
| `pdf/` | LibreOffice-PDF text extractor (ToUnicode CMaps) | `pdf/bin/pdf.exe <pdf>` -> `pdf/audio_pipeline.txt` |
| `chunks/`, `gfind/` | hex dump of FEV chunks / find a GUID's references | |
| `cmp/` | byte-compare two .wav data chunks | |
| `wstr/` | UTF-16 strings of an exe (`wstr/acs_w.txt` = acs.exe keys) | |

Outputs kept there: `ferrari_458_data/` (58 decrypted files), `all/<car>/` (all 86 cars decrypted), `acev/f458_events.txt`,
`acev/common_events.txt`, `fsb/f458.txt`, `fsb/common.txt`, `fsp/all_events.txt`, `wav/ext_on5000.wav`, `wav/car_hit_2.wav`.

Related specs: `ac_car_formats.md` (kn5, data.acd, ksanim, driver, LODs, coordinate conversion - do not duplicate), `ets_vehicles.md`,
`tablet_front.md`.

---

## 0. Key facts in one screen

- 180 car folders, **86 drivable** (have `data.acd` + `sfx/<car>.bank`), 94 are DLC stubs (`ui/` only) [VERIFIED]. 21 track folders.
- `data.acd` decrypts for **86/86** cars with the folder-name key [VERIFIED, `acd` probe over all]. Every physics key below comes from it.
- A fully **commented** reference car exists unencrypted: `sdk/dev/content/cars/formula_k/data/*.ini` - Kunos' comments there are the
  semantics source for most keys (quoted as [READ]).
- **Every car bank and common.bank is FSB5 PCM16 (mode 2), 44100 Hz, 1-2 ch, with a sample NAME TABLE** [VERIFIED 86/86 car banks +
  common.bank]. A sample pulled out of `formula_k.bank` is **byte-identical** to the SDK's source `ext_fa01_on_4250.wav` [VERIFIED].
  The FSB5 `mode` field is at **+0x18** (not +0x14; +0x14 is the data size).
- Interior engine samples have no rpm in their names (`F4CH_ONLOW`...), so the rpm layout must come from the bank's FMOD Studio 1.08
  metadata (`LIST PROJ`). I decoded that format: per instrument the **rpm region (start, length), auto-pitch reference rpm, volume dB,
  crossfade curves, and the throttle load/coast bus curves** [VERIFIED: parsed engine_int AND engine_ext with auto-pitch instruments in
  **86/86** car banks]. That is everything needed to rebuild AC's engine sound exactly, with Unity AudioSources.
- The ETS2 Dreamport's `EtsFmodStudio` (FMOD 2.x banks) does NOT parse AC banks (0 params, 0 instruments) [VERIFIED]: AC uses an older
  layout (section 2.4 gives the differences).
- Session types: `Practice 1, Qualify 2, Race 3, Hotlap 4, TimeAttack 5, Drift 6, Drag 7`; spawn sets `PIT, START, HOTLAP_START`
  [READ, career event.ini comment]. Lap validity: track `data/surfaces.ini` `IS_VALID_TRACK`, `ALLOWED_TYRES_OUT` (server / race) [READ].
- Track timing/start objects in the track kn5: `AC_START_n`, `AC_PIT_n`, `AC_HOTLAP_START_0`, `AC_TIME_n_L/R` (gate pairs),
  start-light meshes `KS_START_LIGHT_0..4` [VERIFIED by string scan of magione/spa/monza kn5].

---

## 1. Car physics data (data.acd)

### 1.1 Reading
- Decrypt with the folder-name key (algorithm and verified C# reader in `ac_car_formats.md` section 2; key for `ferrari_458` =
  `235-167-105-254-54-143-17-57` [VERIFIED]). If a mod car has a `data/` folder instead, read that.
- INI dialect [VERIFIED by reading all 86]: `[SECTION]`, `KEY=VALUE`, trailing `; comment` and tab padding on most lines, whole lines of
  `/////` or `;` (setup.ini), sections may repeat or appear out of order (`ai.ini` has `[HEADER]` after `[LOOKAHEAD]`), vectors are
  comma lists with optional spaces (`0 ,-0.285 ,0.3`). Parse: strip from the first `;` (and lines starting `//`), trim, split on first `=`.
  Some keys have stray text after the value (`TYPE=RWD    ; Vampire`), so always strip comments before parsing numbers.
- LUT files: one `x|y` per line, invariant-culture floats, blank lines and `;` comments possible. Evaluate piecewise linear, clamp to ends
  [BELIEF: AC clamps]. `.rto` files (gear ratio choices for setup): `13//41|3.15` = label `|` ratio [VERIFIED bmw_m3_e30_dtm/final.rto].
- Units: SI (m, kg, N, N/m, N*s/m, Nm, kg*m^2) unless noted. Car-local frame: origin = CG [READ: "position ... starting from the CoG"].
  Axes are AC's (see `ac_car_formats.md` 1.5 for kn5 -> Unity); physics INI vectors use the same x,y,z as the kn5 (x lateral, y up,
  z forward: front wing at z=+2.0, diffuser z=-1.6 on the 458 [VERIFIED by values]).

Census over the 86 cars [VERIFIED]: `TRACTION TYPE`: RWD 69, AWD 5, AWD2 7, FWD 5. Suspension `TYPE`: DWB 142 axles, STRUT 30.
`tyres.ini VERSION=10` on all 86. `[TURBO_0]` present in e.g. abarth500, alfa giulietta, bmw_1m.

### 1.2 car.ini (ferrari_458 values) [VERIFIED values, semantics READ from comments]
| key | 458 | meaning |
|---|---|---|
| `[INFO] SCREEN_NAME` | Ferrari 458 Italia | display name |
| `[BASIC] GRAPHICS_OFFSET` | 0.0,-0.45,0.34 | offset applied only to the 3D model relative to the physics body (m) |
| `GRAPHICS_PITCH_ROTATION` | -0.8 | model pitch (deg) |
| `TOTALMASS` | 1578 | kg **with driver, no fuel** |
| `INERTIA` | 1.84,1.00,4.01 | "polar inertia calculated from the car dimensions: width,height,length" -> these are BOX DIMENSIONS (m); inertia = box of TOTALMASS [READ; BELIEF on the box formula] |
| `[GRAPHICS] DRIVEREYES` | 0.3447,0.9865,-0.1678 | cockpit eye point (car frame) - VR seat anchor |
| `BONNET_/BUMPER_CAMERA_POS`, `*_PITCH`, `ON_BOARD_PITCH_ANGLE`, `SHAKE_MUL`, `MIRROR_POSITION`, `VIRTUAL_MIRROR_ENABLED`, `FUEL_LIGHT_MIN_LITERS`=7 | | camera/cockpit |
| `[CONTROLS] STEER_LOCK` | 360 | steering WHEEL degrees centre->right lock [READ] |
| `STEER_RATIO` | 11.9 | steering ratio -> road-wheel max ~ 360/11.9 = 30.3 deg |
| `LINEAR_STEER_ROD_RATIO` | 0.00245 | rack travel per wheel-deg for AC's geometric suspension (ignore in a simplified model) |
| `FFMULT`, `STEER_ASSIST` | 2.951, 1 | force feedback |
| `[FUEL] CONSUMPTION` | 0.0036 | **litres per second = rpm * gas * CONSUMPTION / 1000** [READ, formula_k comment] |
| `FUEL` / `MAX_FUEL` | 30 / 86 | default start litres / tank |
| `[FUELTANK] POSITION` | 0,-0.1,-0.4 | tank offset from CG (fuel mass added there) |
| `[RIDE] PICKUP_FRONT/REAR_HEIGHT` | -0.325/-0.31 | ride-height measuring points below CG |
| `[RULES] MIN_HEIGHT` | (formula_k 0.010) | minimum ride height rule |
| `[PIT_STOP] TYRE_CHANGE_TIME_SEC=10 FUEL_LITER_TIME_SEC=0.6 BODY/ENGINE/SUSP_REPAIR_TIME_SEC=20/2/30` | | pit stop time per tyre / per litre / per 10 % damage [READ] |

### 1.3 engine.ini + power.lut [VERIFIED values]
```
[HEADER] POWER_CURVE=power.lut  COAST_CURVE=FROM_COAST_REF
[ENGINE_DATA] INERTIA=0.110 LIMITER=9200 LIMITER_HZ=40 MINIMUM=900 ALTITUDE_SENSITIVITY=0.1
[COAST_REF] RPM=9000 TORQUE=100 NON_LINEARITY=0
[COAST_DATA]/[COAST_CURVE]  -> OBSOLETE (formula_k comment: "must copy to keep AC from crashing, do not use")
[DAMAGE] TURBO_BOOST_THRESHOLD=0.8 TURBO_DAMAGE_K=30 RPM_THRESHOLD=9500 RPM_DAMAGE_K=0.2
```
- `power.lut` = **rpm | torque (Nm)** (458: 0|100 ... 4000|442, 6000|466 peak, 9000|395, 9250|360, 9500|0; negative rpm rows exist).
- The lut torque is ~12 % below the brochure crank torque: ui_car.json `torqueCurve` 4000 rpm = 504 Nm vs lut 442 (x1.14) [VERIFIED
  numbers]. [BELIEF] AC's lut is "at the wheels" (drivetrain loss already removed) -> apply it directly, add no extra efficiency factor.
- `LIMITER` rev limit (0 = none), `LIMITER_HZ` cut frequency, `MINIMUM` idle rpm, `INERTIA` engine inertia kg*m^2 [READ].
- Coast (engine braking): `TORQUE` Nm at `RPM`; `NON_LINEARITY` 0 = linear from 0 to TORQUE, 1 = fully exponential [READ]. So off-gas
  torque = -TORQUE * (rpm/RPM) for NON_LINEARITY=0 [READ->formula].
- `[TURBO_n]` (formula_k / bmw_1m) [READ comments]: `MAX_BOOST` never exceeded, torque **T = T*(1 + boost)**; `WASTEGATE` max boost before
  the wastegate (0 = none); `REFERENCE_RPM` rpm where full boost is reached at full gas; `GAMMA` boost sensitivity to the pedal;
  `LAG_UP`/`LAG_DN` interpolation lags (0.99-0.996 = per physics step filter factors [BELIEF: boost += (target-boost)*(1-LAG) each step
  at AC's step rate]); `DISPLAY_MAX_BOOST` for gauges; `COCKPIT_ADJUSTABLE`, `DEFAULT_TURBO_ADJUSTMENT`.
- `[DAMAGE]`: damage per second = K * (rpm - RPM_THRESHOLD) above threshold; same for boost [READ].

### 1.4 drivetrain.ini [VERIFIED values]
458: `[TRACTION] TYPE=RWD`; `[GEARS] COUNT=7 GEAR_R=-3.818 GEAR_1..7 = 3.077 2.158 1.626 1.286 1.028 0.839 0.69 FINAL=5.14`;
`[DIFFERENTIAL] POWER=0.20 COAST=0.40 PRELOAD=10` ("lock under power/coast, 1.0 = 100 %", preload Nm) [READ];
`[GEARBOX] CHANGE_UP_TIME=40 CHANGE_DN_TIME=100` (ms) `AUTO_CUTOFF_TIME=60` (ms gas cut on upshift) `SUPPORTS_SHIFTER=0` (paddles
only) `VALID_SHIFT_RPM_WINDOW=800` `CONTROLS_WINDOW_GAIN=0.4` (H-shifter rev-match tolerance) `INERTIA=0.012`;
`[CLUTCH] MAX_TORQUE=750` Nm; `[AUTOCLUTCH] UPSHIFT_PROFILE=NONE DOWNSHIFT_PROFILE=DOWNSHIFT_PROFILE USE_ON_CHANGES=1 MIN_RPM=1100
MAX_RPM=1700 FORCED_ON=1`; `[DOWNSHIFT_PROFILE] POINT_0/1/2` = ms clutch fully in / start release / fully out;
`[AUTOBLIP] ELECTRONIC=1 POINT_0..2 LEVEL=0.90` (blip gas level and timing); `[AUTO_SHIFTER] UP=8900 DOWN=5000 SLIP_THRESHOLD=0.95
GAS_CUTOFF_TIME=0.130` (auto gearbox shift rpm) [READ]. `[DAMAGE] RPM_WINDOW_K` (formula_k).
- AWD (formula_k): `[AWD] FRONT_SHARE=35` (% torque front) `FRONT/CENTRE/REAR_DIFF_POWER/COAST/PRELOAD`. AWD2 (7 cars): `[AWD2]
  FRONT_DIFF_*`, `CENTRE_RAMP_TORQUE`, `CENTRE_MAX_TORQUE` (viscous/haldex-like centre) [VERIFIED keys]. Extra controllers in some cars:
  `ctrl_*.ini`, `difflock_*_mult.lut`, `gear_start.lut` [VERIFIED file names].

### 1.5 suspensions.ini [VERIFIED values; semantics READ]
458: `[BASIC] WHEELBASE=2.650 CG_LOCATION=0.4367` (front weight fraction); `[ARB] FRONT=28000 REAR=6400` (N/m);
per axle `[FRONT]`/`[REAR]`: `TYPE=DWB|STRUT`, `BASEY=-0.095/-0.100` ("distance of CG from the wheel centre"), `TRACK=1.672/1.606`,
`ROD_LENGTH` (ride height adjust, + raises), `HUB_MASS=40/45` kg, hard points `WBCAR_TOP_FRONT/REAR`, `WBCAR_BOTTOM_FRONT/REAR`,
`WBTYRE_TOP/BOTTOM`, `WBCAR_STEER`, `WBTYRE_STEER` (x,y,z in m, wheel-local; STRUT uses `STRUT_CAR`/`STRUT_TYRE` [BELIEF]),
`TOE_OUT` (m of steering arm), `STATIC_CAMBER=-1.8/-2.1` deg, `SPRING_RATE=40000/70000` (**wheel rate** N/m), `PROGRESSIVE_SPRING_RATE`
(N/m/m), `BUMP_STOP_RATE`, `BUMPSTOP_UP/DN` (m from design position), `PACKER_RANGE` (total travel before packers),
`DAMP_BUMP=3500/7056 DAMP_FAST_BUMP DAMP_FAST_BUMPTHRESHOLD=0.060 DAMP_REBOUND=4200/10800 DAMP_FAST_REBOUND DAMP_FAST_REBOUNDTHRESHOLD`
(N*s/m; the "threshold" is the damper SPEED in m/s where the fast rate takes over [BELIEF; the comment says "seconds"]),
`RIM_OFFSET` (formula_k), `[HEAVE_FRONT/REAR]` third springs (formula_k), `[GRAPHICS_OFFSETS] WHEEL_xx / SUSP_xx` (visual x shifts),
`[DAMAGE] MIN_VELOCITY GAIN MAX_DAMAGE` (steer-rod bend per km/h impact).

Derived geometry (use these in Unity) [BELIEF, consistent with the comments and sane for the 458]:
- front axle z = +WHEELBASE*(1-CG_LOCATION) = +1.493 m; rear axle z = -WHEELBASE*CG_LOCATION = -1.157 m.
- wheel centre y (car frame, CG = 0) = `BASEY` (458 front -0.095); contact patch y = BASEY - tyre RADIUS.
- CG height above ground = RADIUS - BASEY per axle: front 0.3235+0.095 = 0.419, rear 0.357+0.100 = 0.457; blended ~0.44 m.
- wheel x = +-TRACK/2.

### 1.6 tyres.ini (VERSION=10) [VERIFIED values; semantics READ where a comment exists]
Sections: `[COMPOUND_DEFAULT] INDEX` (default compound), `[FRONT]`/`[REAR]` = compound 0, `[FRONT_1]`/`[REAR_1]` = compound 1, ...;
`[THERMAL_FRONT]`, `[THERMAL_FRONT_1]` ... ; `NAME`, `SHORT_NAME` (= server `LEGAL_TYRES` codes, e.g. SM, ST, SV).
458 compound 0 "Semislicks" front / rear:
| key | front | rear | meaning |
|---|---|---|---|
| WIDTH | 0.235 | 0.295 | m |
| RADIUS / RIM_RADIUS | 0.3235 / 0.254 | 0.35725 / 0.2667 | m |
| ANGULAR_INERTIA | 1.56 | 1.98 | kg*m^2 rim+tyre+disc |
| RATE / DAMP | 283402 / 500 | 293778 / 500 | vertical tyre spring N/m, damping N*s/m |
| DX_REF / DY_REF | 1.30 / 1.28 | (rear similar) | **peak friction coefficient long./lat. at load FZ0** |
| FZ0 | 3348 | | reference load N |
| LS_EXPX / LS_EXPY | 0.8853 / 0.8196 | | load-sensitivity exponents: mu(Fz) = D_REF * (Fz/FZ0)^(LS_EXP-1) [BELIEF] |
| FRICTION_LIMIT_ANGLE | 7.52 | 7.06 | slip angle (deg) of peak lateral force |
| CX_MULT | 1.04 | | longitudinal stiffness multiplier [BELIEF] |
| FALLOFF_LEVEL / FALLOFF_SPEED | 0.86 / 2 | | grip beyond the peak falls to LEVEL*peak, SPEED = how fast [BELIEF] |
| SPEED_SENSITIVITY | 0.003471 | | grip loss with speed [READ] |
| RELAXATION_LENGTH | 0.07788 | | m - lateral slip lag |
| ROLLING_RESISTANCE_0 / _1 / _SLIP | 12 / 0.001109 / 5166 | | constant, *v^2, slip-angle terms [READ] |
| FLEX, FLEX_GAIN | 0.000684, 0.0296 | | carcass flex adds slip angle with load [READ] |
| CAMBER_GAIN, DCAMBER_0, DCAMBER_1 | 0.115, 1.2, -13 | | **D = D*(1 - (camberRAD*DCAMBER_0 + camberRAD^2*DCAMBER_1))** [READ formula] |
| XMU | 0.27 | | |
| PRESSURE_STATIC / PRESSURE_IDEAL | 28 / 33 | | cold / ideal psi; `PRESSURE_SPRING_GAIN` N/m per psi from 26 psi, `_FLEX_GAIN`, `_RR_GAIN`, `_D_GAIN` (footprint loss per psi) [READ] |
| RADIUS_ANGULAR_K | 0.024 | | radius growth in MILLIMETRES = K * omega rad/s [READ] |
| BRAKE_DX_MOD | 0.05 | | |
| WEAR_CURVE | semislicks_front.lut | | virtual km -> grip % (0|100, 0.25|100 ...) [VERIFIED file] |
| DY0/DY1/DX0/DX1 | 1.3027/-0.048/1.3115/-0.046 | | legacy (pre-v10) friction, superseded by *_REF + LS_EXP [BELIEF] |
`[THERMAL_*]`: `SURFACE_TRANSFER PATCH_TRANSFER CORE_TRANSFER INTERNAL_CORE_TRANSFER FRICTION_K ROLLING_K SURFACE_ROLLING_K COOL_FACTOR
GRAIN_* BLISTER_*`, `PERFORMANCE_CURVE=tcurve_street.lut` = temperature C -> grip multiplier (458: 0|0.80 20|0.97 60|0.995 75|1.0 85|1.0
100|0.98 160|0.95 220|0.90 250|0.60) [VERIFIED file]. `[VIRTUALKM] USE_LOAD=1`.

### 1.7 aero.ini [VERIFIED values]
`[WING_n] NAME CHORD SPAN POSITION LUT_AOA_CL LUT_GH_CL CL_GAIN LUT_AOA_CD LUT_GH_CD CD_GAIN ANGLE YAW_CL_GAIN ZONE_<FRONT|REAR|LEFT|RIGHT>_<CL|CD>`.
458: WING_0 BODY (CHORD 1, SPAN 2.022, CL_GAIN **0**, CD_GAIN 1, CD(0 deg)=0.33), WING_1 FRONT (CL 0.11 at 0 deg, CL_GAIN 1, CD_GAIN 0,
pos z +2.0), WING_2 DIFFUSER (pos z -1.6). Ground-height luts `height_*_CL/CD.lut`. Damage: `CL=CL/(1+ZONE_x_CL*DAMAGE)`,
`CD=CD*(1+ZONE_x_CD*DAMAGE)` [READ]. Active aero: `[DYNAMIC_CONTROLLER_n] WING INPUT(BRAKE|GAS|SPEED_KMH|LATG|LONG|STEER) LUT COMBINATOR
(ADD|MULT) FILTER UP_LIMIT DOWN_LIMIT` [READ formula_k]; `drs.ini`, `wing_animations.ini`.
Force per wing [BELIEF]: `F = 0.5 * rho * v^2 * CHORD * SPAN * C(aoa) * GAIN * height_lut(rideHeight)`, aoa = ANGLE (+ chassis pitch),
**positive CL = downforce**, applied at POSITION. Sanity: 458 body drag at 300 km/h = 0.5*1.2*83.3^2*2.022*0.33 = 2.8 kN (~235 kW) and
front-wing downforce 0.94 kN - plausible for a 325 km/h car [computed].

### 1.8 brakes.ini, electronics.ini, setup.ini, ai.ini [VERIFIED values]
- brakes.ini 458: `MAX_TORQUE=3500 FRONT_SHARE=0.62 HANDBRAKE_TORQUE=600 COCKPIT_ADJUSTABLE=0 ADJUST_STEP=0.5`; `[DISCS_GRAPHICS] DISC_LF..RR`
  mesh names, `FRONT/REAR_MAX_GLOW`, `LAG_HOT/COOL`. [BELIEF] torque **per wheel**: front wheel = MAX_TORQUE*FRONT_SHARE, rear wheel =
  MAX_TORQUE*(1-FRONT_SHARE) (gives 1.35 g for the 458, matching its tyres; the "total" reading would give only 0.66 g).
- electronics.ini 458: `[ABS] SLIP_RATIO_LIMIT=0.11 CURVE= PRESENT=1 ACTIVE=1 RATE_HZ=250`; `[TRACTION_CONTROL] SLIP_RATIO_LIMIT=0.08
  CURVE=traction_control.lut (level|slip: 0|0.08 1|0.15 2|0.25 3|0.35) PRESENT=1 ACTIVE=1 RATE_HZ=667 MIN_SPEED_KMH=25`; `[EDL]`.
  [READ] "ABS always works if the ABS assist is on in the realism menu even if not PRESENT"; TC off below MIN_SPEED_KMH.
- setup.ini: one section per adjustable item (`[PRESSURE_LF]`, `[CAMBER_LF]`, `[TOE_OUT_LF]`, `[FUEL]`, `[BRAKE_POWER_MULT]`,
  `[FINAL_GEAR_RATIO] RATIOS=final.rto`, wings ...) with `TAB NAME MIN MAX STEP POS_X POS_Y HELP SHOW_CLICKS`.
- ai.ini: `[GEARS] UP=8900 DOWN=5600`, `[PEDALS] GASGAIN BRAKE_HINT TRAIL_HINT`, `[STEER] STEER_GAIN`, `[LOOKAHEAD] BASE GAS_BRAKE_LOOKAHEAD`.

### 1.9 Visual/cockpit INIs used by gameplay [VERIFIED]
- `colliders.ini` 458: `[COLLIDER_0] CENTRE=0,-0.285,0.3 SIZE=1.75,0.10,3.5 GROUND_ENABLE=1` (car-floor box vs ground; the real car-car
  collision uses `collider.kn5`) [READ formula_k comment].
- `digital_instruments.ini`: `[ITEM_n] PARENT TYPE(GEAR|RPM|SPEED|FUEL|RPM_GRAPH|AMBIENT_TEMP|PLACE_HOLDER ...) POSITION SIZE COLOR INTENSITY
  FONT(e.g. 599_big -> content/fonts/599_big.png/.txt) ALIGN UNITS N_TIME`; `[LED_n] OBJECT_NAME RPM_SWITCH EMISSIVE BLINK_SWITCH BLINK_HZ`.
- `analog_instruments.ini`: `[RPM_INDICATOR] ZERO STEP OBJECT_NAME MIN_VALUE` (needle deg = ZERO + rpm*STEP [BELIEF]), `[SPEED_INDICATOR]`.
- `lights.ini`: `[BRAKE_n] NAME COLOR OFF_COLOR`, `[LIGHT_n] NAME COLOR` (head/dash lights by mesh name).
- `driver3d.ini`: `[MODEL] NAME=driver_no_HANS`, `[STEER_ANIMATION] NAME=steer.ksanim LOCK=360`, `[SHIFT_ANIMATION] BLEND_TIME=100
  POSITIVE_TIME=150 STATIC_TIME=10 NEGATIVE_TIME=150 PRELOAD_RPM=8500 INVERT_SHIFTING_HANDS`, `[HIDE_OBJECT_n]` (driver nodes hidden in
  cockpit view).
- `damage.ini` (visual damage, glass, oscillating panels), `fuel_cons.ini [FUEL_EVAL] KM_PER_LITER=1.78`, `sounds.ini` (section 2.6).

### 1.10 Recommended Unity vehicle model ("AC-like", stable) [OURS unless the number is named from an INI]
AC's physics runs at **333 Hz** [BELIEF: public knowledge, not found in the install]. Somnium decides `Time.fixedDeltaTime`; do not
change it globally. Design:
1. One `Rigidbody` per car: `mass = TOTALMASS + fuelLitres*0.75` (0.75 kg/l // ours), `centerOfMass = 0` (body origin AT the CG; place the
   kn5 with GRAPHICS_OFFSET/PITCH), `inertiaTensor` from INERTIA box (w,h,l): `Ix = m(h^2+l^2)/12, Iy = m(w^2+l^2)/12, Iz = m(w^2+h^2)/12`
   (x right, y up, z fwd), `inertiaTensorRotation = identity`, interpolation on, continuous-speculative collision with `collider.kn5`
   convex hull (or the colliders.ini box as the floor).
2. Four ray/sphere-cast wheels at (+-TRACK/2, BASEY, axle z). Suspension force = SPRING_RATE*x + PROGRESSIVE_SPRING_RATE*x^2 + bump-stop
   (BUMP_STOP_RATE beyond BUMPSTOP_UP/DN) + damper (DAMP_BUMP/REBOUND below the threshold speed, FAST_* above). Travel limit = PACKER_RANGE.
   ARB: F = ARB * (xL - xR), applied +/- to the two sides. Tyre vertical spring RATE can be ignored (or put in series) in v1.
3. **Inner sub-step loop for the stiff part** (wheel spin, tyre forces, drivetrain): n = ceil(fixedDeltaTime * 333) sub-steps per
   FixedUpdate (6-7 at 50 Hz); per sub-step: read contact velocities from the body's current velocity at the patch, update wheel
   omega (semi-implicit: drive torque - brake torque - Fx*R)/ANGULAR_INERTIA, lateral slip with relaxation (d alpha/dt =
   |v|/RELAXATION_LENGTH*(alpha_target - alpha)), accumulate forces; after the loop `AddForceAtPosition(avgForce)` once. Clamp brake
   torque so it can only stop, not reverse, the wheel within a sub-step (the classic lock-up jitter fix).
4. Tyre (simplified AC brush): per tyre `Dy = DY_REF * (Fz/FZ0)^(LS_EXPY-1) * tcurve(T)/1 * wear%/100 * surface FRICTION * camber
   factor (DCAMBER formula) * (1 - SPEED_SENSITIVITY*speed)` (same for Dx with DX_REF/LS_EXPX). Normalised slips:
   `sy = tan(alpha)/tan(FRICTION_LIMIT_ANGLE)`, `sx = slipRatio*CX_MULT/tan(FRICTION_LIMIT_ANGLE)` (// ours: AC's brush treats both slips
   with one stiffness). Combined `s = |(sx,sy)|`; force magnitude `f(s) = s<=1 ? (2s - s^2)` (brush-like rise to 1 at the peak) `: 1 -
   (1-FALLOFF_LEVEL)*(1-exp(-(s-1)*FALLOFF_SPEED))` (// ours shape); `Fx = Dx*Fz*f*(sx/s)`, `Fy = Dy*Fz*f*(sy/s)` (friction ellipse).
   Low-speed: below ~2 m/s blend to a damped "sticky" constraint to stop creep (// ours). Rolling resistance: `RR0 + RR1*v^2`.
5. Drivetrain: engine omega integrates (engine torque(rpm, gas) + coast) - clutch torque, engine INERTIA; clutch locks when slip < eps
   and |torque| < CLUTCH MAX_TORQUE. Locked: one inertia (engine + gearbox INERTIA reflected) driving the axle through ratio = GEAR_n *
   FINAL. Gear change = no drive for CHANGE_UP/DN_TIME ms; auto gearbox uses AUTO_SHIFTER UP/DOWN; AUTO_CUTOFF_TIME cuts gas on upshift;
   AUTOBLIP on downshift. Rev limiter: gas = 0 while rpm >= LIMITER (toggle at LIMITER_HZ - also drives the `limiter` sound). Idle: hold
   MINIMUM with a small idle-gas controller (// ours). Turbo per 1.3.
6. Differential: open + clutch-type LSD: lock torque = PRELOAD + (POWER or COAST)*|axle input torque|; transfer up to that torque from the
   faster to the slower wheel. AWD: split by FRONT_SHARE then the same per diff.
7. Steering: road angle = input * STEER_LOCK / STEER_RATIO (deg) (VR: the grabbed wheel angle / STEER_RATIO). Optional Ackermann: outer
   wheel * 0.9 (// ours).
8. Aero per 1.7 at each wing POSITION; body drag CD at v^2. rho = 1.2 (// ours).
9. Brakes per 1.8 (per-wheel torques, bias FRONT_SHARE); handbrake = HANDBRAKE_TORQUE on rears. ABS: release brake while slipRatio <
   -SLIP_RATIO_LIMIT (pulse at RATE_HZ); TC: cut gas while slipRatio > limit (level from traction_control.lut), off below MIN_SPEED_KMH.
10. Fuel: litres -= rpm*gas*CONSUMPTION/1000 * dt * (server FUEL_RATE/100).
11. Validation targets (the user judges "like the game"): 458 at full throttle in 1st should hit the 8900 rpm shift in ~2 s, 0-100 km/h
   3.3-3.6 s (ui_car.json "3.3s"), top ~325 km/h; lateral ~1.3 g on semislicks. Log these once per car in the lab.

---

## 2. Sound (FMOD Studio banks, PCM16)

### 2.1 Files [VERIFIED]
- `content/cars/<car>/sfx/<car>.bank` (ferrari_458 52,562,176 B; 49 samples), `content/sfx/common.bank` (43,809,792 B; 29 samples),
  `content/sfx/common.strings.bank` (not needed), `content/sfx/GUIDs.txt` (3237 lines: `{guid} bank:/x`, `{guid} event:/cars/<car>/<event>`,
  `bus:/...`, `vca:/...`). Mod cars may carry their own `sfx/GUIDs.txt` [READ, pipeline PDF p.11].
- GUIDs.txt gives several car paths the SAME event GUID when a variant reuses a bank (`ferrari_458` and `ferrari_458_s3` share
  `{c5c17772-...} skid_ext`) -> resolve **path -> GUID from GUIDs.txt, then GUID -> event in the car's own bank**.
- Per-car event names and how many of the 181 car folders in GUIDs.txt have them [VERIFIED]: engine_ext, engine_int, gear_ext, gear_int,
  skid_ext, skid_int, wheel, wind, bodywork (181 each), door 168, limiter 164, tractioncontrol_ext/int 153, backfire_ext 140,
  backfire_int 137, horn 135, gear_grind 120, turbo 86, transmission 80.
- Global events: `event:/surfaces/{grass,gravel,sand,kerb,extraturf,old,tyre_rolling}`, `event:/collisions/{car/hit, car/scrape,
  object/hit, track/hit, track/scrape}`, `event:/common/{ambience,dirt,screw,unscrew,ds_protection}`, `event:/showrooms/*`.

### 2.2 FEV RIFF container [VERIFIED ferrari_458.bank]
```
0x00 "RIFF" u32 size  "FEV "
0x0C "FMT " size 8: u32 0x50, u32 0x46       (FMOD Studio 1.08 bank format; ETS2's 2.x banks differ)
0x1C "LIST" size 48968 "PROJ"  -> metadata chunks (section 2.4), LIST-nested
0xBF6C "SND " size 52513164 -> padding to a 32-byte boundary, then FSB5 at 0xBF80
```
Walk: `id, size` = 4 + 4 bytes, `LIST` = id, size, 4-char type, children; pad odd sizes. Find FSB5 by scanning from the SND data start
for the bytes "FSB5" (padding varies).

### 2.3 FSB5 [VERIFIED on 86/86 car banks + common.bank + sdk formula_k.bank]
Header (version 1 => 0x3C bytes; version 0 => 0x40):
| off | type | ferrari_458 | meaning |
|---|---|---|---|
| 0x00 | char[4] | "FSB5" | |
| 0x04 | u32 | 1 | version |
| 0x08 | u32 | 49 | sample count |
| 0x0C | u32 | 15204 | sample-header block size |
| 0x10 | u32 | 832 | name-table size (0 = no names) |
| 0x14 | u32 | 52497056 | sample data size |
| 0x18 | u32 | **2** | mode = FMOD_SOUND_FORMAT: 2 = **PCM16** (all AC banks) (15 = Vorbis, 16 = FADPCM ...) |
| 0x1C.. | 32 B | | zero/hash/guid, unused |
Sample header i (sequential from 0x3C): u64 `m`:
- bit 0 = has extra chunks; bits 1-4 = rate index {1:8000,2:11000,3:11025,4:16000,5:22050,6:24000,7:32000,8:44100,9:48000,10:96000};
  bits 5-6 = channels code {0:1,1:2,2:6,3:8}; bits 7-33 = data offset / 32; bits 34-63 = frame count.
- chunks while the "more" bit is set: u32 `c` (bit 0 = more, bits 1-24 = size, bits 25-31 = type), then `size` bytes. Types: 1 = u8
  channels, 2 = u32 rate, 3 = u32 loopStart,u32 loopEnd (frames), seen in AC: type 4 (520 / 260 / 8840 B, on looped engine/wind samples;
  sync points/markers [BELIEF]), type 5 (4-12 B), type 13 (4 B on every sample) - skip unknown types.
Name table: `count` u32 offsets relative to the table start, then NUL-terminated ASCII names. **In newer builds names carry a GUID suffix**
(`ext_fa01_on_4250 {976816bf-3244-4767-9c1f-61797d34d224}` in sdk formula_k.bank) -> cut at `" {"`.
Sample data base = 0x3C + headerBlock + nameTable (relative to "FSB5"). Sample i bytes = `frames * 2 * channels` starting at
`base + offset` (the gap to the next offset is FMOD padding, 32-92 bytes seen) - always use the frame count. PCM16 little-endian,
channels interleaved. **No loop chunk in AC banks: looping is set per instrument in the metadata (INST loop count -1 = loop).**

ferrari_458 samples [VERIFIED, `fsb/f458.txt`] (all 44100 Hz): exterior engine (stereo, rpm in the name) `ext_on3500/5000/6750/8500`,
`ext_off3000/5000/7000/8500`, `F4CH_IDLE_EXT` (mono); interior engine (stereo, NO rpm in the name) `F4CH_IDLE, F4CH_ONIDLE-2,
F4CH_ONVERYLOW, F4CH_ONLOW, F4CH_ONLOWMID, F4CH_ONMID, F4CH_ONMIDHIGH, F4CH_ONHIGH, F4CH_ONMAX, F4CH_OFFVERYLOW-1/-2, F4CH_OFFLOW,
F4CH_OFFMIDLOW(-3), F4CH_OFFMID-3, F4CH_OFFHIGHMID-3, F4CH_OFFMAX, F4CH_LOW/MID/HIGH_RSSNNC(-2)`; others `skid_ext_mono, Skid, tyre_rolling,
wind, wind_draft_mono, flutter_4, flat_tyre_mono, tyre_explosion, gearup, geardn, gearupEXT, geardnEXT, horn, door_open, door_close,
traction_control, bodywork`.
common.bank [VERIFIED `fsb/common.txt`]: `car_hit, car_hit_2, crash2, crash3 (x2), hit, hit2, scrape3, cone1, cone2, bodywork_light,
dirt, dirt_min, dirt_full, sand, gravel, grass_stereo, extraturf_stereo, kerb_short, flutter_1, ambient1, beep, wrench1_a..4_b,
tyre_screw1_a..3_a`.

Verified C# (C# 9, no Span; same logic as the `fsb` probe whose output was byte-identical to the SDK wav):
```csharp
public sealed class AcFsbSample { public string Name; public int Rate, Channels; public long Frames; public int DataOffset; } // DataOffset = absolute in the bank
public static class AcFsb5
{
    static readonly int[] Rates = { 0, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
    /// <summary>Finds the FSB5 inside an FMOD .bank (or a bare .fsb) and lists its samples. Returns null if absent/unsupported.</summary>
    public static List<AcFsbSample> Read(byte[] b, out int mode)
    {
        mode = -1;
        int f = -1;
        for (int i = 0; i + 4 <= b.Length; i++) if (b[i] == 'F' && b[i + 1] == 'S' && b[i + 2] == 'B' && b[i + 3] == '5') { f = i; break; }
        if (f < 0) return null;
        int ver = BitConverter.ToInt32(b, f + 4), num = BitConverter.ToInt32(b, f + 8);
        int shdr = BitConverter.ToInt32(b, f + 0x0C), nsz = BitConverter.ToInt32(b, f + 0x10);
        mode = BitConverter.ToInt32(b, f + 0x18);
        int hdr = ver == 0 ? 0x40 : 0x3C, sp = f + hdr, nameBase = f + hdr + shdr, dataBase = nameBase + nsz;
        var list = new List<AcFsbSample>(num);
        for (int i = 0; i < num; i++)
        {
            ulong m = BitConverter.ToUInt64(b, sp); sp += 8;
            var s = new AcFsbSample();
            bool more = (m & 1) != 0;
            s.Rate = Rates[(int)((m >> 1) & 0xF)];
            int cc = (int)((m >> 5) & 3); s.Channels = cc == 0 ? 1 : cc == 1 ? 2 : cc == 2 ? 6 : 8;
            s.DataOffset = dataBase + (int)(((m >> 7) & 0x7FFFFFF) << 5);
            s.Frames = (long)((m >> 34) & 0x3FFFFFFF);
            while (more)
            {
                uint c = BitConverter.ToUInt32(b, sp); sp += 4;
                more = (c & 1) != 0;
                int size = (int)((c >> 1) & 0xFFFFFF), type = (int)(c >> 25);
                if (type == 1) s.Channels = b[sp]; else if (type == 2) s.Rate = BitConverter.ToInt32(b, sp);
                sp += size;
            }
            if (nsz > 0)
            {
                int s0 = nameBase + BitConverter.ToInt32(b, nameBase + i * 4), s1 = s0;
                while (b[s1] != 0) s1++;
                string n = System.Text.Encoding.ASCII.GetString(b, s0, s1 - s0);
                int g = n.IndexOf(" {", StringComparison.Ordinal); s.Name = g >= 0 ? n.Substring(0, g) : n;
            }
            else s.Name = "#" + i;
            list.Add(s);
        }
        return list;
    }
    /// <summary>PCM16 -> float [-1,1] interleaved, for AudioClip.Create(name, frames, channels, rate, false) + SetData. Worker-thread safe.</summary>
    public static float[] Pcm16ToFloats(byte[] b, AcFsbSample s)
    {
        int n = (int)(s.Frames * s.Channels); var o = new float[n]; int p = s.DataOffset;
        for (int i = 0; i < n; i++, p += 2) o[i] = (short)(b[p] | (b[p + 1] << 8)) / 32768f;
        return o;
    }
}
```
Memory note: a car bank holds 50 MB of PCM; convert only the samples its events use (engine_int ~22 stereo loops ~40 MB of floats -
consider creating the clips lazily, or streaming with `AudioClip.Create(..., stream: true, PCMReaderCallback)` reading from the byte[]).

### 2.4 FMOD Studio 1.08 metadata ("LIST PROJ") - decoded [VERIFIED by the `acev` probe: 86/86 car banks + common.bank]
FMOD publishes no spec. Layout found by hex-dumping ferrari_458.bank and matching GUIDs (all GUIDs = 16 bytes, .NET `new Guid(byte[16])`
order, which matches GUIDs.txt text). Offsets are relative to the chunk DATA start. Chunk counts in ferrari_458: PRMB 19, WAV 49,
WAIB 51 + INST 51, PMLB 19 (+CTRO), CTRL 78, CURV 78, GBSB 21, EVTB 13, TLNB 13, MODU 3, PROP 56, MAP 7, BEFB/PMEF/PEFB effects.

| chunk | layout (AC 1.08) | ETS2 2.x (EtsFmodStudio) differs |
|---|---|---|
| `PRMB` parameter | guid; +16 u32 flags (1 at +17 for built-in Distance); +21 **u16 name length**; +23 name (no NUL); then f32 min, max, default | name length at +24 |
| `WAV ` waveform | guid; +16 u32 (12); **+22 u16 FSB5 sample index** | same |
| `WAIB` waveform instrument | instrument guid; +16 waveform guid; the next chunk is its `INST` | same |
| `INST` (123 B) | +0 timeline guid (zero on a sheet); **+16 f32 volume dB**; **+20 f32 pitch semitones**; **+24 i32 loop count (-1 = loop)**; +28 u32 1; +33 f32 100 (probability?); **+53 guid auto-pitch parameter (zero = off)**; **+69 f32 auto-pitch reference value** (1.0 when off); +81 u16 24; **+83 guid output group bus** | 130 B, autopitch +56/+72, bus +86, region inside INST |
| `PMLB` parameter sheet | guid (= the parameter guid); +16 parameter guid; **+32 event guid**; +48 u16 2n+1; +50 u16 entry size (24); entries at +52: **{instrument guid, f32 start, f32 length}** on the parameter axis (n = 0: automation-only sheet) | region not here |
| `CTRL` automation | guid; **+16 owner guid** (instrument or bus); **+32 parameter guid** (the axis); **+48 curve guid**; **+64 u32 property** (4 = instrument volume, linear 0..1; 0 = bus/track volume dB; 1 = pitch; 0xE8 = effect) | owner/curve offsets differ |
| `CURV` curve | guid; guid; +32 u16 2n+1; +34 u16 entry size (16); entries {f32 x, f32 y, f32 shape, u32 flags} | same |
| `GBSB`/`MBSB`/`IBSB` bus | guid; +16 u16 24; **+18 parent bus guid** | same |
| `TLNB` timeline (non-sheet events: skid, horn, surfaces, ambience) | +0 timeline guid; **+16 event guid**; +32 u16; +34 u16 2n+1; +36 u16 entry 24; entries {instrument guid, u32 position, u32 length} in **48 kHz frames** (skid_ext_mono: 149959 frames@44.1k = 163221 @48k [VERIFIED]); then a loop-region record | |
| `EVTB` event | event guid; ...; +93 u16 2n+1, u16 16, n parameter guids | |

How things connect: event GUID (GUIDs.txt) <- `PMLB +32` / `TLNB +16`; sheet entries -> instrument GUID -> `WAIB` -> waveform GUID ->
`WAV +22` -> FSB5 index -> sample name. Instrument automations: `CTRL` whose owner = instrument; the throttle load/coast crossfade is on
the instrument's output BUS (`INST +83`) and its parents (`GBSB +18`): `CTRL` with owner = bus, property 0, axis = throttle (or rpms).
Not decoded (not needed for v1): MODU (modulators: randomizer etc.), PROP, MAP, effect chunks (spatialiser distances, low-pass on the
cone angle - use the SDK project values in 2.5), CTRO (sheet automation list - CTRL +32 gives the axis directly).

Verified C# core (same logic as `acev/Program.cs`; C# 9, no Span). The full probe source is the reference:
```csharp
public sealed class AcFmodCurve
{
    public float[] X, Y;   // shape values (+-0.25 typical) ignored: piecewise linear, clamped (same as EtsFmodStudio.Curve.Eval)
    public float Eval(float x)
    {
        int n = X.Length; if (n == 0) return 0f; if (x <= X[0]) return Y[0]; if (x >= X[n - 1]) return Y[n - 1];
        for (int i = 1; i < n; i++) if (x <= X[i]) { float s = X[i] - X[i - 1]; return s <= 1e-6f ? Y[i] : Y[i - 1] + (Y[i] - Y[i - 1]) * (x - X[i - 1]) / s; }
        return Y[n - 1];
    }
}
public sealed class AcFmodAuto { public Guid Param; public int Property; public AcFmodCurve Curve; }
public sealed class AcFmodInst
{
    public Guid Id, Wave, AutoPitchParam, Bus; public int Sample = -1; public float VolumeDb, PitchSemis, AutoPitchRef = 1f; public int Loop;
    public Guid RegionParam; public float RegionStart, RegionLength;   // from the PMLB entry
    public readonly List<AcFmodAuto> Autos = new List<AcFmodAuto>();  // CTRL owner == this instrument
}
// Chunk walk -> List<(string id, int off, int size)> (LIST recursed, "SND " skipped); then one pass:
//  "PRMB": len = U16(o+21); name = ASCII(o+23, len); min,max,def = F(o+23+len), +4, +8
//  "WAV ": wavSample[G(o)] = U16(o+22)
//  "WAIB": next chunk "INST" at q: inst[G(o)] = { Wave=G(o+16), VolumeDb=F(q+16), PitchSemis=F(q+20), Loop=I32(q+24),
//          AutoPitchParam=G(q+53), AutoPitchRef=F(q+69), Bus=G(q+83) }
//  "GBSB"/"MBSB"/"IBSB": busParent[G(o)] = G(o+18)
//  "CURV": n=(U16(o+32)-1)/2, es=U16(o+34); points (F(o+36+k*es), F(o+40+k*es))
//  "CTRL": ctrl(owner=G(o+16), param=G(o+32), curve=G(o+48), prop=I32(o+64))
//  "PMLB": param=G(o+16), evt=G(o+32), n=(U16(o+48)-1)/2, es=U16(o+50); if es==24: entries (G(o+52+k*24), F(o+68+k*24), F(o+72+k*24))
//  "TLNB": evt=G(o+16), n=(U16(o+34)-1)/2, es=U16(o+36); entries (G(o+38+k*es), U32(o+54+k*es) pos, U32(o+58+k*es) len)  [layout from one dump; check es==24]
```

### 2.5 The engine event, decoded (ferrari_458) [VERIFIED, `acev/f458_events.txt`]
Parameters: `rpms` 0..18000, `throttle` 0..1 (+ built-ins Distance, Event Cone Angle). **engine_int**: 23 looping instruments on the
`rpms` sheet, in 4 group buses:
```
LOAD bus  (volume dB on throttle: 0.12 -> -27.3 dB ... 0.95 -> +0.39 dB)
 F4CH_ONVERYLOW    0..2800  ref 4682   fade out 1800->2800
 F4CH_ONIDLE-2  1800..3400  ref 2986   in 1800->2800, out 3100->3400
 F4CH_ONLOW     3100..5380  ref 5127   ...
 F4CH_ONLOWMID  4880..5880  ref 6660
 F4CH_ONMID     5740..7200  ref 7126
 F4CH_ONMIDHIGH 6625..9200  ref 8196
 F4CH_ONHIGH    8000..9400  ref 8276  (+1 dB)
 F4CH_ONMAX     9200..18000 ref 8396
COAST bus (volume dB on throttle: 0.1 -> +0.39 dB ... 0.93 -> -42 dB)
 F4CH_IDLE 0..1800 ref 1559 (+1.5 dB), F4CH_OFFVERYLOW-2 1300..2400 ref 1453, F4CH_OFFVERYLOW-1 2100..3400 ref 2906, F4CH_OFFLOW 2600..3800
 ref 4017, F4CH_OFFMIDLOW 3600..4050 ref 4966, F4CH_OFFMIDLOW-3 3950..5000 ref 6378, F4CH_OFFMID-3 4800..6200 ref 6540,
 F4CH_OFFHIGHMID-3 5900..8150 ref 6298, F4CH_OFFMAX 7200..18000 ref 7952
RESONANCE bus A (coast-weighted: throttle 0.1 -> +0.39 .. 0.93 -> -42 dB; rpms 1037 -> -42 .. 4750 -> 0 dB): F4CH_LOW/MID/HIGH_RSSNNC-2
RESONANCE bus B (rpms 0 -> -42 .. 3000 -> 0 dB): F4CH_LOW_RSSNNC 0..5625, F4CH_MID_RSSNNC 5500..7125, F4CH_HIGH_RSSNNC 7000..18000 (ref 5228/6702/8114)
```
Each neighbouring pair crossfades over the overlap with a linear-volume instrument automation (fade-in curve 0->1, fade-out 1->0, shape
+-0.25). **engine_ext**: 10 instruments; LOAD bus (throttle 0.09 -> -42 dB, 0.9 -> -1.87 dB): `F4CH_IDLE_EXT 0..2000 ref 1250 (-11 dB)`,
`ext_on3500 200..2800 ref 3500 (+1.5 dB)`, `ext_on5000 2200..4800`, `ext_on6750 4200..6800`, `ext_on8500 6000..18000`; COAST bus
(throttle 0.1 -> 0 dB, 0.8 -> -42 dB): `F4CH_IDLE_EXT 0..2200 (-10 dB)`, `ext_off3000 1200..3000`, `ext_off5000 2200..4600`,
`ext_off7000 3800..6800 (-1 dB)`, `ext_off8500 5600..18000`. Exterior auto-pitch refs equal the rpm in the sample name.

**Rebuild in Unity (per car, per listener mode)** - `AcEngineSound`:
1. For each instrument of the event: one looping `AudioSource` (2D for the local cockpit `engine_int`, 3D for `engine_ext`), clip from
   the FSB5 sample, started at a random offset (FMOD starts loops mid-sample; SDK `startOffset` values 0.4-1.1) and kept playing (mute
   by volume, do not Stop/Play - avoids clicks).
2. Every frame with `rpm` (engine rpm, not clamped to the limiter) and `throttle` (gas pedal 0..1 after TC/limiter cuts):
   `active = rpm in [start, start+length]`; `gain = dB(INST vol) * prod(instrument linear automations at rpm) * prod over bus chain of
   dB(bus curve(throttle or rpm))`; outside the region gain = 0. `dB(x) = x <= -80 ? 0 : 10^(x/20)`.
3. `pitch = rpm / AutoPitchRef * 2^(PitchSemis/12)` [BELIEF: FMOD 1.08 auto-pitch with minimum pitch 0, i.e. playback rate proportional to
   rpm/ref; the SDK project XML only sets `root`]. Unity `AudioSource.pitch` allows up to 3; rpm/ref here stays 0.2..1.3 in the regions.
4. Smooth rpm/throttle (~30 ms) to avoid zipper noise; volume per voice clamp 0..1 then `AudioSource.volume`.
5. Cockpit (local driver in seat) -> engine_int + gear_int + backfire_int + skid_int + tractioncontrol_int; everyone else (remote cars,
   outside view) -> the `_ext` events at the emitters below. The SDK says each car's engine_int range is set to twice the real limit "to hear
   the sound even when overrevving" [READ].
Emitters [READ, pipeline PDF p.4-5]: engine at the front axle for `[ENGINE] POSITION=front`, **0.5 m ahead of the rear axle** for
`POSITION=rear` (CoG when missing); backfire 0.5 m behind the rear axle; wheel/skid/surface at each wheel; horn, bodywork, wind at CoG;
gear, limiter, TC, transmission, turbo with the engine. 3D panner in the SDK project: engine_ext max volume at 6 m, silent at 350 m
(Distance rolloff); track cameras multiply by `system/cfg/camera_track.ini DISTANCE_SCALE=7` [READ]. engine_ext has a low-pass on the
event cone angle: 30 deg -> 22 kHz, 180 deg (behind the car) -> 5.4 kHz [VERIFIED in SDK project XML] (Unity: AudioLowPassFilter by
angle between car forward and listener direction).

### 2.6 Other events (parameters) [READ pipeline PDF table + VERIFIED in banks]
| event | params | in the 458 bank / SDK |
|---|---|---|
| gear_ext / gear_int | `state` 0 = down, 1 = up | sheet: down sample 0..0.5, up sample 0.5..1 (`geardn(EXT)`, `gearup(EXT)`), one-shots |
| door | `state` 0 close / 1 open | `door_close` 0..0.5, `door_open` 0.5..1 - **use for entering/leaving the car** |
| wind | `speed` 0..500 (km/h), `air_pressure` 0.8..1.5 (slipstream) | `wind_draft_mono` 0.8..1.1, `wind` 1..1.5; bus dB on speed: 10 -> -42, 120 -> -8, 279 -> 0 |
| wheel | `speed`, `brake`, `inflation` (puncture 0..1), `suspension_damage` | `tyre_rolling` (dB on speed 15 -> -42, 180 -> -4.5, 275 -> 0), `flat_tyre_mono`, `tyre_explosion` (inflation 0..0.1), `flutter_4` (damage) |
| skid_int / skid_ext | none (timeline loop) | `Skid` / `skid_ext_mono`; volume and pitch from code: sounds.ini `[SKIDS] ENTRY_POINT` (tyre-load related; lower = audible at lower load), `MIX_VOLUME`; `PITCH_BASE/GAIN` deprecated [READ] |
| tractioncontrol_ext/int | `decay` 0..1 | `traction_control` 0..0.2, bus dB on decay 0.02 -> 0, 0.14 -> -42 |
| limiter | `decay` | trigger at each limiter cut |
| backfire_ext/int | `throttle` | sounds.ini `[BACKFIRE] MAXGAS` (no backfire if gas > MAXGAS), `MINRPM`, `MAXRPM`, `TRIGGERGAS` (trigger point), `VOLUME_IN/OUT/SCALE_OUT` |
| transmission | `drivetrain_speed` 0..350, `throttle` | gear whine |
| turbo | `boost` (bar), `bov` 0..1, `bov_decay` | |
| bodywork | `susp_travel_speed` 0..1 | `bodywork` loop, bus dB 0.3 -> -42 .. 1 -> +0.39 |
| horn | (timeline) | `horn` |
| gear_grind | (timeline) | missed shift (H-shifter) |
| surfaces/* (common) | `speed` 0..500, `decay` 0..10 | the track `surfaces.ini WAV=grass.wav` names the event: grass.wav -> `event:/surfaces/grass`, kerb.wav -> kerb, sand.wav -> sand [VERIFIED name match in system/data/surfaces.ini]; `WAV_PITCH` |
| collisions/* (common) | `impact_speed` 0..1000, `impact_angle` 0..180; scrape: `speed`, `decay` | car/hit: `car_hit_2` 0.6..40, `car_hit` 40..100, `crash3` 100..1000 (-3 st) |
sounds.ini 458 [VERIFIED]: `[SKIDS] ENTRY_POINT=0.5 MIX_VOLUME=0.65 PITCH_BASE=0.55 PITCH_GAIN=0.15`, `[WIND] SPEED_GAIN_0/1 VOLUME_GAIN
PITCH_REFERENCE PITCH_GAIN`, `[TYRE_ROLLING]` same keys, `[BACKFIRE] MAXGAS=0.4 MINRPM=6500 MAXRPM=12000 TRIGGERGAS=0.8 VOLUME_IN=1.0
VOLUME_OUT=0.8 VOLUME_SCALE_OUT=5`, `[BODY_WORK] MIN=0.2 GAIN=1`, `[GEAR_EXT] DISTANCE_SCALE=5`, `[ENGINE] POSITION=rear`.
Mixer buses (`GUIDs.txt`): grp_engine_ext/int, grp_skid_*, grp_wheel_open/closed, grp_wind_open/closed (open vs closed cockpit), grp_turbo,
grp_limiter, grp_transmission, grp_surfaces, grp_collisions... - map to our own volume settings (tablet "Volumes" page).

### 2.7 Networking the sound
Send per car at the net rate: rpm (u16), throttle (u8), gear (s8, change -> gear_ext event), brake (u8), speed, per-wheel slip/load
summary for skids (u8 x4), surface key index per wheel, limiter/backfire/TC flags. Each client rebuilds every remote car's engine_ext
locally from its own copy of that car's bank (all players own the game; a car missing on a client -> fall back to common sounds only).

---

## 3. Sessions and race rules

### 3.1 Session definitions (race.ini style) [VERIFIED in content/career/*/event*/event.ini and content/specialevents/*/event.ini]
```
[RACE] TRACK= CONFIG_TRACK= MODEL= CARS= AI_LEVEL= FIXED_SETUP= PENALTIES=1 JUMP_START_PENALTY=0|2 DRIFT_MODE= SKIN=
[SESSION_n] NAME=  TYPE=   ;Practice 1, Qualify 2 Race 3, Hotlap 4, TimeAttack 5, Drift 6, Drag 7     (comment in career event.ini)
            SPAWN_SET= ;PIT, START, HOTLAP_START     DURATION_MINUTES=  LAPS=  STARTING_POSITION=
[LAP_INVALIDATOR] ALLOWED_TYRES_OUT=2   (-1 / 0 / 2 seen)
[DYNAMIC_TRACK] PRESET SESSION_START SESSION_TRANSFER RANDOMNESS LAP_GAIN   [GROOVE] VIRTUAL_LAPS MAX_LAPS STARTING_LAPS
[TEMPERATURE] AMBIENT ROAD   [WEATHER] NAME   [LIGHTING] SUN_ANGLE TIME_MULT CLOUD_SPEED   [WIND]   [GHOST_CAR]
[CAR_n] MODEL SKIN SETUP AI_LEVEL DRIVER_NAME NATIONALITY NATION_CODE
[CONDITION_n] TYPE=TIME|POINTS|AI|WINS OBJECTIVE=   (career medal goals; TIME in ms)
```
Seen values: career sessions Practice (TYPE=1, 30 min, SPAWN_SET=PIT), Qualifying (TYPE=2, 30 min, PIT), Race (TYPE=3, LAPS 4..35, START);
special events Hotlap TYPE=4 + HOTLAP_START + ALLOWED_TYRES_OUT=0; JUMP_START_PENALTY 2 (56 events) or 0 (13). [BELIEF, AC public
docs] JUMP_START_PENALTY: 0 = none, 1 = teleport to pits, 2 = drive-through; server `START_RULE` 0 = car locked until green, 1 =
teleport, 2 = drive-through. Further keys present in acs.exe [VERIFIED string list `wstr/acs_w.txt`]: `ADDITITIONAL_LAP` (sic),
`BLACK_FLAG_TIME`, `MICRO_SECTORS`, `SHOW_SECTORS`, `STARTING_LIGHTS`, `STARTING_TRACK_SEMAPHORE`, `KS_START_LIGHT_`, `OVER_TIME_MULT`,
`TIME_TO_FINISH`, `PENALTIES`, `DT_PENALTY_AVATAR`, `LAP_INVALIDATOR_AVATAR`, `HOTLAP_START`, `SPAWN_SET`, `DURATION_MINUTES`, plus
log strings "FALSE START!", "DISQUALIFIED", "LAP VALID: %d CUTS:%d", "isCarInPitlane", "STARTING LIGHTS ACTIVE %i".
`system/cfg/assetto_corsa.ini [RACE] OVER_TIME_MULT=1.2` [VERIFIED] (timed race: after time expires, [BELIEF] the leader finishes the lap;
over-time cap = 1.2x).

### 3.2 Multiplayer rules AC itself uses (server/cfg/server_cfg.ini + entry_list.ini) [VERIFIED files; extra keys from acServer.exe strings]
```
[SERVER] NAME CARS=bmw_m3_e30 (;-separated list) TRACK=magione CONFIG_TRACK= SUN_ANGLE=48 PASSWORD ADMIN_PASSWORD UDP_PORT=9600 TCP_PORT=9600
 HTTP_PORT=8081 PICKUP_MODE_ENABLED=1 LOOP_MODE=1 CLIENT_SEND_INTERVAL_HZ=18 RACE_OVER_TIME=180 (s after the winner) KICK_QUORUM=85
 VOTING_QUORUM=80 VOTE_DURATION=20 BLACKLIST_MODE=1 FUEL_RATE=100 DAMAGE_MULTIPLIER=100 TYRE_WEAR_RATE=100 ALLOWED_TYRES_OUT=2
 ABS_ALLOWED=1 TC_ALLOWED=1 STABILITY_ALLOWED=0 AUTOCLUTCH_ALLOWED=0 TYRE_BLANKETS_ALLOWED=0 FORCE_VIRTUAL_MIRROR=1 REGISTER_TO_LOBBY=1
 MAX_CLIENTS=18 LEGAL_TYRES=SV   (preset adds: MAX_BALLAST_KG=0 QUALIFY_MAX_WAIT_PERC=120 START_RULE=0 WELCOME_MESSAGE=)
[BOOKING] NAME TIME   (key exists in acServer.exe)
[PRACTICE] NAME=Practice TIME=10 (min) IS_OPEN=1
[QUALIFY] NAME=Qualify TIME=10 IS_OPEN=1
[RACE] NAME=Race LAPS=5 (or TIME=) WAIT_TIME=60 (s grid wait) IS_OPEN=1 (0 closed, 1 open, 2 open until green [BELIEF])
[DYNAMIC_TRACK] SESSION_START=89 RANDOMNESS=3 SESSION_TRANSFER=80 LAP_GAIN=50
[WEATHER_n] GRAPHICS=3_clear BASE_TEMPERATURE_AMBIENT=18 BASE_TEMPERATURE_ROAD=6 VARIATION_AMBIENT=1 VARIATION_ROAD=1 (+WIND_* keys)
entry_list.ini: [CAR_n] MODEL SKIN SPECTATOR_MODE DRIVERNAME TEAM GUID BALLAST (kg) (+RESTRICTOR % [exe string])
```
acServer.exe also knows [VERIFIED strings]: `RACE_PIT_WINDOW_START/END`, `REVERSED_GRID_RACE_POSITIONS`, `RACE_EXTRA_LAP`,
`RACE_GAS_PENALTY_DISABLED`, `MAX_CONTACTS_PER_KM`, `RESULT_SCREEN_TIME`, `TIME_OF_DAY_MULT`, `LOCKED_ENTRY_LIST`, `FIXED_SETUP`,
`MAX_BALLAST_KG`, `RESTRICTOR`, `QUALIFY_MAX_WAIT_PERC`. `server/manager/ks_tyres.ini` maps car -> legal tyre short names.
**Use these exact names and defaults for our race lobby settings on the tablet** (that is how AC players know them).

### 3.3 Track side (what a race needs) [VERIFIED for magione unless noted]
- `content/tracks/<track>[/<layout>]/data/` is plain text (no acd): `surfaces.ini`, `startinglights.ini`, `sections.ini` (named corners
  `IN/OUT` as normalised lap position + `TEXT`), `drs_zones.ini` (`DETECTION START END`), `map.ini` (+ `map.png`), `cameras*.ini`,
  `audio_sources.ini`, `crew.ini`, `ai_hints.ini`, `groove.ini`, `lighting.ini`; `ai/fast_lane.ai` (racing line spline, header i32
  version=7, i32 points=1754, ...) and `ai/pit_lane.ai` -> lap progress and pit-lane detection; `ui/ui_track.json` (`length` 2507 m,
  `pitboxes` 18, `run` clockwise).
- Surfaces: global `system/data/surfaces.ini` (ROAD, GRASS, KERB, SAND) + the track's own (magione: PITLANE, TARMACA/B/C...). Keys per
  surface: `KEY FRICTION DAMPING WAV WAV_PITCH FF_EFFECT DIRT_ADDITIVE BLACK_FLAG_TIME IS_VALID_TRACK SIN_HEIGHT SIN_LENGTH IS_PITLANE
  VIBRATION_GAIN VIBRATION_LENGTH`. Mesh binding: physics meshes in the kn5 are named `<digit><KEY>...` (`1WALL`, `3TARMACC`, `0TARMACA`,
  `5TARMACA`) [VERIFIED name census]; [BELIEF] digit = surface group, KEY = surface. GRASS friction 0.6 / SAND 0.8 + DAMPING 0.1 /
  KERB 0.92 [VERIFIED]. Use FRICTION as the tyre mu multiplier, DAMPING as rolling drag, SIN_HEIGHT/LENGTH as bumpy ground.
- Kn5 empties: `AC_START_0..17` (grid slots, 0 = pole), `AC_PIT_0..17` (pit boxes), `AC_HOTLAP_START_0`, `AC_TIME_0_L/R` and
  `AC_TIME_1_L/R` (magione; spa has 3 pairs) = timing gates; `AC_AUDIO_*`, `AC_CREW_*`, `AC_POBJECT_n` (movable objects, spa).
  [BELIEF] `AC_TIME_0` = start/finish line; higher indices = split points -> sectors (spa 3 pairs -> 3 sectors).
- Start lights: track meshes `KS_START_LIGHT_0..4` (magione, monza) lit with `startinglights.ini [SETTINGS] EMISSIVE=255,0,0,255
  INTENSITY=2 DIFFUSE=0.3`; HUD lights `content/texture/texture_trafficlight_red/green/off.png` (74x74) [VERIFIED files]. [BELIEF]
  AC sequence: cars locked on the grid, red lights come on one by one, then GREEN = go (AC uses a green light, the textures prove a
  red/green/off set); moving before green = "FALSE START!" -> JUMP_START_PENALTY / START_RULE. Exact timings are in acs.exe code, not in a
  file: pick ~1 s per light // ours, and ask the user to compare with the game.

### 3.4 Rules to implement (AC behaviour) [BELIEF unless marked]
- Lap = crossing AC_TIME_0 gate in the race direction; sector times at the other gates; best lap, last lap, delta.
- Cut: a lap is invalid when more than `ALLOWED_TYRES_OUT` tyres (2 = all four out invalidates? AC counts "tyres out": with 2, three or
  more wheels on IS_VALID_TRACK=0 surfaces cut the lap) [BELIEF on the exact count rule - verify by playing]; -1 disables. Invalid laps do
  not count for best lap / qualifying; with `PENALTIES=1` repeated cuts in a race give a penalty (`BLACK_FLAG_TIME` surfaces give a black
  flag after N s).
- Pit lane: surfaces with `IS_PITLANE=1` (and pit_lane.ai) -> pit limiter (speed limit [BELIEF] 80 km/h, // ours if not found), pit box
  `AC_PIT_n` -> pit stop menu: tyres, fuel, repair times from car.ini `[PIT_STOP]`.
- Race end: lap race ends when the leader completes LAPS; others finish their lap; `RACE_OVER_TIME` s after the winner closes it.
  Results: position, best lap, total time, laps, gap - show on the tablet and as the AC "sessionRanking" style list.
- Qualify: best valid lap orders the race grid (AC_START_0 = pole); `QUALIFY_MAX_WAIT_PERC` = how long a qualify lap in progress may
  continue after time is up (120 % of best lap [BELIEF]).

---

## 4. HUD, fonts, GUI art (what exists to make it "exactly like the game") [VERIFIED listings]
- **Bitmap fonts** `content/fonts/<name>.png + <name>.txt` (58 .txt): the png is one row of glyphs (arial 1024x64, e92_big/599_big
  4096x64 RGBA); the txt is a list of **normalised U start positions** for consecutive ASCII chars from 32 (space): 95 or 96 lines
  (one font, digital_toyota, has 570). Glyph i = chars (32+i), spans [u_i, u_(i+1)) x full height; last value may exceed 1 (arial
  1.1016) -> clamp [VERIFIED counts/format; char mapping BELIEF]. These are the fonts digital_instruments.ini `FONT=` refers to (cockpit
  displays) - render them as quads with the png as texture.
- TrueType fonts shipped in the install: `content/fonts/segoeui*.ttf, seguisb*, consola*.ttf, Orbitron-Medium.ttf` (AC's UI is Segoe UI)
  [VERIFIED files] - load at run time (`new Font(path)` is editor-only; use the world's existing font path from the Halo/ETS dreamports,
  e.g. `EtsFont`, or rasterise the bitmap fonts).
- `content/gui/` (61 entries): `essential/rpmBar.png` (132x23), `essential/fuelBar.png` (81x25) = the "Essentials" app (gear, speed,
  rpm bar, fuel); `PlayerCurrentTime/` (best.png 26x31, last.png, up/down arrows, white_line 265x40 - the lap-time app); `sessionRanking/
  currentLapBg.png` (476x26); `flags/` (blackflag, blueFlag, yellowFlag, whiteFlag, finish, penalty, blackFlag_small); `pitstop/` (fuel,
  tyre_1..5, repair_body/engine/susp ON/OFF buttons, fuel_meter, operating_*); `icons/` (every app's ON/OFF taskbar icon); `inGameMenu/`
  (drive, setup, racecontrol, telemetry, time, exit, next ON/OFF); `drag/false_start.png`, `drift/`, `timeAttack/`, `endRace/`,
  `raceControl/`, `skin.ini` (UI colours: FORM BACK 0,0,0,128, TITLE_BACK 255,0,0,255 ...).
- `content/texture/`: `texture_trafficlight_red/green/off.png`, `rpm_on/off.png` (156x156), `turbo_on/off.png`, `kers*.png`,
  `wrongway.png`, `checkpoint.png`, `mini_finish.png`, `ideal_line.png/.dds`, `skids.dds`, `smoke_0.png`, `spark.png`.
- `system/cfg/*.ini` app configs (session_info.ini colours, pitstop.ini layout, tyres_app.ini, proximity_indicator.ini, messages.ini).
- `apps/python/gMeter/gMeter.py` + `apps/python/system/acsys.py` (AC's Python app API sample - shows which telemetry the HUD apps use).
- The HUD app LAYOUTS (positions, sizes, text) are drawn by acs.exe code, not described in files -> match them from screenshots of the
  real game, using these exact images and fonts. Tablet menus: use `skin.ini` colours + `inGameMenu`/`icons` art.

---

## 5. Open questions / to verify next
1. Auto-pitch "minimum pitch" in 1.08 INST (not located; assumed 0 => rate = rpm/ref). Compare by ear against the game (record the
   458 at 3000/6000/9000 rpm in AC and in our lab).
2. Tyre force shape constants (section 1.10 step 4) are ours; tune against the validation targets.
3. Cut rule exact count, pit speed limit, start-light timing, sector gate semantics: confirm in-game (Player.log can't see AC; ask the
   user for a short video or play it).
4. FEV chunks not decoded: spatialiser distances per event in each bank (SDK defaults used), MODU (randomizers), TLNB tail.
5. fast_lane.ai record layout beyond the header (needed for lap progress / AI line) - see the track spec if one exists, else decode.

---

## Critic addendum (completeness critic, 2026-10-31)

- **Section 1.5 "derived geometry" is now VERIFIED on 86/86 cars** (`ac_car_formats.md` C1, probe `acscratch/critic`): front axle
  z = +WHEELBASE*(1-CG_LOCATION) and rear z = -WHEELBASE*CG_LOCATION match the kn5 WHEEL dummies + GRAPHICS_OFFSET within 3 cm (85/86,
  84/86); wheel centre y = BASEY within 10 cm (86/86); wheel x = +-TRACK/2 (median error 0.007 m). CG height above ground = -GRAPHICS_OFFSET.y
  (= RADIUS - BASEY within a few cm). Note the BASEY comment text differs between cars (458: "Radius-BASEY", SDK formula_k: "Radius+BASEY") -
  the numbers prove BASEY is the wheel-centre height relative to the CG (negative = below), so CG height = RADIUS - BASEY.
- `car.ini [GRAPHICS] DRIVEREYES` is in MODEL space (ground origin), not CG space - the VR eye in body space is
  `(-(eye.x+off.x), eye.y+off.y, eye.z+off.z)` (Unity). Table 1.2 "car frame" should be read that way.
- `colliders.ini` boxes are physics (CG) space; `collider.kn5` is model space (one data bug: ks_ford_mustang_2015's hull is 5 m off).
- SDK formula_k comments [READ]: brakes.ini `MAX_TORQUE ; Maximum Brake torque in Nm`, `FRONT_SHARE ; Percentance of brake torque at front
  axis`, `HANDBRAKE_TORQUE ; (at the rear wheels)`; car.ini `INERTIA ; Car polar inertia. Calculated from the car dimensions. Start with the
  generic width,height,length` -> confirms INERTIA = box dimensions. The comment does NOT settle per-wheel vs per-axle brake torque; keep the
  per-wheel reading (1.8) but validate with a braking target (458 100-0 km/h ~ 32-35 m) in the lab.
- No pit-lane speed limit exists in `system/cfg/*.ini` or `cfg/*.ini` (grep for PIT/SPEED/LIMIT) [VERIFIED absent] -> // ours (80 km/h).
- Contradiction resolved: `halo_skeleton.md` says AC's FSB5 samples "are usually Vorbis" - wrong; this spec's census (PCM16, mode 2, 86/86
  banks + common.bank) is right. NVorbis is not needed for AC.
