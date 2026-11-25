using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    // [C, Car model] Unity-FREE visual data of a car, parsed from its data.acd (or data/), driver_base_pos.knh and animations/*.ksanim.
    // Lives in Game/Cars (C's folder) but uses no UnityEngine type, so C's private harness (DevTools/agents/C/harness) compiles it
    // and checks it on every owned car. Worker-thread safe. Everything in AC coordinates (+X = car LEFT, +Y up, +Z forward), MODEL space
    // (ground origin) unless said otherwise - ac_car_formats.md 1.5 + Critic addendum C1.

    /// <summary>One lods.ini [LOD_n] entry.</summary>
    public sealed class AcCarLod
    {
        public int Index;
        /// <summary>FILE= as written (e.g. "ferrari_458_LOD_1.kn5").</summary>
        public string File;
        /// <summary>Full path (null when the file is missing).</summary>
        public string Path;
        /// <summary>IN / OUT distance in metres.</summary>
        public float In, Out;
    }

    /// <summary>One lights.ini [LIGHT_n] / [BRAKE_n] entry: a node or mesh whose emissive is switched.</summary>
    public sealed class AcCarLightDef
    {
        public bool Brake;
        public string Name;
        /// <summary>COLOR (AC emissive units, e.g. 200,0,0 brake; 1320,1200,1080 headlight); v1 BRAKE: INTENSITY used as red when COLOR is absent.</summary>
        public AcVec3 Color;
        /// <summary>OFF_COLOR (0 when absent).</summary>
        public AcVec3 OffColor;
        public bool HasOffColor;
        /// <summary>FLASH=1 (headlight flasher), PITLINE=1 (pit limiter light), SPECIAL=1 (unknown meaning; treated as a normal light).</summary>
        public bool Flash, Pitline, Special;
    }

    /// <summary>lights.ini [LIGHT_ANIMATION_n]: a ksanim played to its end while the headlights are on (pop-up lights).</summary>
    public sealed class AcCarLightAnimDef
    {
        public string File;
        /// <summary>TIME (seconds for the full travel).</summary>
        public float Time;
        public Ksanim Anim;
    }

    /// <summary>blurred_objects.ini [OBJECT_n]: show <see cref="Name"/> while the wheel's speed is in [MinKmh, MaxKmh].</summary>
    public sealed class AcCarBlurDef
    {
        public int Wheel;
        public string Name;
        public float MinKmh, MaxKmh;
    }

    public enum AcGaugeKind { Rpm, Speed, Fuel, Turbo, WaterTemp, PlaceHolder }

    /// <summary>analog_instruments.ini needle: angle = ZERO + LUT(v) or ZERO + STEP * (v - MIN_VALUE), v clamped >= MIN_VALUE.</summary>
    public sealed class AcCarNeedleDef
    {
        public AcGaugeKind Kind;
        public string Section;
        /// <summary>OBJECT_NAME (needle node); filled with the default node (e.g. ARROW_RPM) when blank and that node exists - see <see cref="DefaultName"/>.</summary>
        public string ObjectName;
        /// <summary>OBJECT_NAME_MAX (a second needle that keeps the maximum reached), or null.</summary>
        public string ObjectNameMax;
        public float Zero, Step, MinValue;
        public bool HasMinValue;
        public AcLut Lut;
        /// <summary>TURBO_INDICATOR USE_BAR.</summary>
        public bool UseBar;
        /// <summary>True when OBJECT_NAME was blank in the file (the default node name is used [BELIEF]).</summary>
        public bool NameDefaulted;

        /// <summary>
        /// Gauge angle in degrees about the needle node's local Z (AC sense: positive = clockwise for a viewer looking along local +Z,
        /// i.e. clockwise for the driver on the 244/256 needles whose local Z points forward) for value <paramref name="v"/>.
        /// [BELIEF from the harness census (needles): a LUT REPLACES ZERO + STEP (bmw_m3_e30_dtm fuel ZERO=-170, LUT 0=-175..70=11 vs
        /// STEP 2.8 -> -170..26); the STEP form subtracts MIN_VALUE (ks_alfa_33_stradale water MIN 40: STEP*(130-40) = 63 = LUT(130)).]
        /// </summary>
        public float Angle(float v)
        {
            if (HasMinValue && v < MinValue) v = MinValue;
            if (Lut != null && !Lut.IsEmpty) return Lut.Eval(v);
            return Zero + Step * (v - (HasMinValue ? MinValue : 0f));
        }

        /// <summary>The node AC uses when OBJECT_NAME is blank [BELIEF: the 458's blank RPM/SPEED entries + its ARROW_RPM/ARROW_SPEED nodes].</summary>
        public static string DefaultName(AcGaugeKind k)
        {
            switch (k)
            {
                case AcGaugeKind.Rpm: return "ARROW_RPM";
                case AcGaugeKind.Speed: return "ARROW_SPEED";
                case AcGaugeKind.Fuel: return "ARROW_FUEL";
                case AcGaugeKind.Turbo: return "ARROW_TURBO";
                case AcGaugeKind.WaterTemp: return "ARROW_WATER";
                default: return null;
            }
        }
    }

    /// <summary>digital_instruments.ini [ITEM_n]: a text / graph drawn with a bitmap font at POSITION in PARENT's local space.</summary>
    public sealed class AcCarDigitalItemDef
    {
        public string Section;
        public string Type;
        public string Parent;
        public AcVec3 Position;
        /// <summary>SIZE (character height, m).</summary>
        public float Size;
        /// <summary>COLOR r,g,b,a (0..255 as written).</summary>
        public float R = 255, G = 255, B = 255, A = 255;
        public float Intensity = 1f;
        public string Font;
        public int Version;
        /// <summary>LEFT, RIGHT, CENTER.</summary>
        public string Align = "LEFT";
        /// <summary>KMH, MPH, SYSTEM, L100, MPG_UK ...</summary>
        public string Units;
        public string Prefix = "", Postfix = "", Text = "";
        public int Decimals = -1;
        public float Width, Height;
        public string TextureBase, TextureTop;
        public float RpmMin, RpmMax;
        public int TyreNumber;
        /// <summary>GFORCES: MIN / MAX clamp (NaN when absent), ADDSIGN, DIRECTION (X lateral / Z longitudinal, AC axes).</summary>
        public float Min = float.NaN, Max = float.NaN;
        public bool AddSign;
        public char Direction = 'X';
        /// <summary>GEAR: N_TIME (s) - neutral is shown only after it lasted this long [BELIEF: hides the N flash during a shift; 40 items, all 0.3].</summary>
        public float NTime;
        /// <summary>GEAR_TX: TRIGGER = the gear index that shows TEXTURE_BASE (0 R, 1 N, 2 first ...); -1 when absent.</summary>
        public int Trigger = -1;
    }

    /// <summary>digital_instruments.ini [LED_n]: OBJECT_NAME lit (EMISSIVE) at rpm >= RPM_SWITCH, blinking at BLINK_HZ above BLINK_SWITCH.</summary>
    public sealed class AcCarLedDef
    {
        public string ObjectName;
        public float RpmSwitch, BlinkSwitch, BlinkHz, Diffuse;
        public bool HasBlink;
        public AcVec3 Emissive;
    }

    public enum AcLedSeriesKind { Rpm, Speed, Turbo, WaterTemp, GForce }

    /// <summary>A *_SERIE / WATER_TEMP LED bar: nodes PREFIX+i shown progressively between Start and End [BELIEF: visibility].</summary>
    public sealed class AcCarLedSeriesDef
    {
        public AcLedSeriesKind Kind;
        public string Prefix;
        /// <summary>START_INDEX..END_INDEX, or 0..LED_COUNT-1 (C falls back to 1..LED_COUNT when PREFIX+0 is absent).</summary>
        public int StartIndex, EndIndex;
        public bool CountBased;
        public float Start, End;
        /// <summary>GFORCE_SERIE: DIRECTION (X / Z, AC axes) and SIGN (+1 / -1): the LEDs light for g * SIGN between FORCE_START and FORCE_END.</summary>
        public char Axis = 'X';
        public float Sign = 1f;
    }

    /// <summary>digital_instruments.ini [FUEL_n] (bar segment shown at fuel >= FUEL_SWITCH) and [FUEL_WARNING_n] / analog FUEL_WARNING (lit below).</summary>
    public sealed class AcCarFuelLedDef
    {
        public string ObjectName;
        public float FuelSwitch;
        public bool Warning, Inverted;
        public AcVec3 Emissive;
        public bool HasEmissive;
    }

    /// <summary>digital_instruments.ini [DISPLAY_n]: a display mesh lit with EMISSIVE * INTENSITY.</summary>
    public sealed class AcCarDisplayDef
    {
        public string Name;
        public AcVec3 Emissive;
        public float Intensity = 1f;
    }

    /// <summary>driver3d.ini.</summary>
    public sealed class AcCarDriverDef
    {
        /// <summary>[MODEL] NAME (content/driver/NAME.kn5) and POSITION (model-space offset, AC coords).</summary>
        public string Model;
        public AcVec3 Position;
        /// <summary>[STEER_ANIMATION] NAME / LOCK (deg of steering wheel at the last frame).</summary>
        public string SteerAnimName;
        public float SteerLock = 360f;
        /// <summary>[SHIFT_ANIMATION] times in ms.</summary>
        public float BlendMs, PositiveMs, StaticMs, NegativeMs, PreloadRpm;
        public bool InvertShiftingHands;
        /// <summary>[HIDE_OBJECT_n] NAME (hidden in the first-person cockpit view).</summary>
        public readonly List<string> HideObjects = new List<string>();
    }

    /// <summary>
    /// Iteration 2 (DAMAGE_DESIGN 2.3): one damage.ini [VISUAL_OBJECT_n] - a node bent by the zone's damage and wobbling once damaged.
    /// All 11 keys are AC's; axes are in AC model axes (+X = car LEFT) and are NOT unit length in the data (2,0,0 / 1,1,1 / -1,0,0.5).
    /// </summary>
    public sealed class AcCarPanelDef
    {
        public string Name;
        /// <summary>DAMAGE_ZONE as an AcDamageZone index (FRONT 0, REAR 1, LEFT 2, RIGHT 3, CENTER 4); -1 = unknown token (panel skipped).</summary>
        public int Zone = -1;
        public AcVec3 StaticAxis, OscAxis, AllowedG;
        public float StaticAngle, MultG, MinSpeed, FullSpeed, OscMin, OscMax;

        /// <summary>Bend factor f = saturate((zoneKmh - MIN_SPEED) / (FULL_SPEED - MIN_SPEED)) [I].</summary>
        public float Factor(float zoneKmh) { return AcCarDamageDef.Ramp(zoneKmh, MinSpeed, FullSpeed); }
    }

    /// <summary>
    /// Iteration 2 (DAMAGE_DESIGN 2.1-2.3): a car's damage.ini, the visual half ([SCRATCHES], [DAMAGE] INITIAL_LEVEL, [OSCILLATIONS] ENABLED,
    /// [DAMAGE_GLASS_&lt;ZONE&gt;] MIN/FULL_SPEED, [VISUAL_OBJECT_n]). Zone indices follow AcDamageZone. Unity-free.
    /// </summary>
    public sealed class AcCarDamageDef
    {
        public static readonly string[] ZoneNames = { "FRONT", "REAR", "LEFT", "RIGHT", "CENTER" };
        public const string GlassPrefix = "DAMAGE_GLASS_";

        /// <summary>damage.ini was found.</summary>
        public bool Present;
        /// <summary>[SCRATCHES] MIN_SPEED / MAX_SPEED (km/h; 0 / 20 on 86/86 cars). <see cref="HasScratches"/> false = no scratch layer.</summary>
        public float ScratchMin, ScratchMax;
        public bool HasScratches;
        /// <summary>[DAMAGE] INITIAL_LEVEL (km/h) read as "dents start here" (// ours reading [I weak]; 0 when absent = dents from 0 - R4).</summary>
        public float InitialLevel;
        public bool HasInitialLevel;
        /// <summary>[OSCILLATIONS] ENABLED (1 when the section is absent // ours; 0 on f40, f40_s3, ktm_xbow_r, lotus_49, levante).</summary>
        public bool Oscillations = true;
        /// <summary>Per zone: [DAMAGE_GLASS_&lt;ZONE&gt;] present and its MIN_SPEED / FULL_SPEED (km/h). Missing section -> that zone never breaks.</summary>
        public readonly bool[] HasGlass = new bool[5];
        public readonly float[] GlassMin = new float[5], GlassFull = new float[5];
        public readonly List<AcCarPanelDef> Panels = new List<AcCarPanelDef>();

        /// <summary>saturate((v - min) / (full - min)); a step at min when full &lt;= min.</summary>
        public static float Ramp(float v, float min, float full)
        {
            if (full <= min) return v >= min && v > 0f ? 1f : 0f;
            float f = (v - min) / (full - min);
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        /// <summary>Glass factor g of a zone (0 when the section is missing); the zone shows broken at g >= GLASS_THRESHOLD [I].</summary>
        public float Glass01(int zone, float zoneKmh)
        {
            if (zone < 0 || zone > 4 || !HasGlass[zone] || zoneKmh <= 0f) return 0f;
            return Ramp(zoneKmh, GlassMin[zone], GlassFull[zone]);
        }

        /// <summary>Scratch weight of a zone value: saturate((z - SCR_MIN) / (SCR_MAX - SCR_MIN)).</summary>
        public float Scratch01(float zoneKmh) { return HasScratches && zoneKmh > 0f ? Ramp(zoneKmh, ScratchMin, ScratchMax) : 0f; }

        /// <summary>Dent weight: saturate((z - INITIAL_LEVEL) / maxDamageKmh) - full dent at INITIAL_LEVEL + MAX_DAMAGE_KMH (60). // ours reading</summary>
        public float Dent01(float zoneKmh, float maxDamageKmh)
        {
            if (zoneKmh <= 0f) return 0f;
            float span = maxDamageKmh > 1f ? maxDamageKmh : 60f; // ours guard (= damage_displayer MAX_DAMAGE_KMH of the install)
            return Ramp(zoneKmh, InitialLevel, InitialLevel + span);
        }

        /// <summary>Zone index of a DAMAGE_ZONE / section token (FRONT REAR LEFT RIGHT CENTER, CENTRE alias // ours), -1 when unknown.</summary>
        public static int ZoneOf(string token)
        {
            if (string.IsNullOrEmpty(token)) return -1;
            token = token.Trim();
            for (int i = 0; i < ZoneNames.Length; i++) if (string.Equals(token, ZoneNames[i], StringComparison.OrdinalIgnoreCase)) return i;
            if (string.Equals(token, "CENTRE", StringComparison.OrdinalIgnoreCase)) return 4;
            return -1;
        }

        /// <summary>
        /// The glass zone a node NAME names: <c>DAMAGE_GLASS_&lt;ZONE&gt;</c> with the exact token FRONT|REAR|LEFT|RIGHT|CENTER right after the prefix,
        /// followed by '_' or the end of the name (DAMAGE_GLASS_FRONT_1, DAMAGE_GLASS_CENTER_MESH). -1 for anything else (DAMAGE_GLASS_Vtr_Ant1,
        /// DAMAGE_GLASS_Vtr_Left_3, polymsh_extracted). Callers group a mesh by its TOP-MOST ancestor with a zone (critic rule).
        /// </summary>
        public static int GlassZoneOfName(string name)
        {
            if (name == null || name.Length <= GlassPrefix.Length || !name.StartsWith(GlassPrefix, StringComparison.OrdinalIgnoreCase)) return -1;
            for (int z = 0; z < ZoneNames.Length; z++)
            {
                string t = ZoneNames[z];
                int end = GlassPrefix.Length + t.Length;
                if (name.Length < end) continue;
                if (string.Compare(name, GlassPrefix.Length, t, 0, t.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
                if (name.Length == end || name[end] == '_') return z;
            }
            return -1;
        }

        /// <summary>True for a node name that starts with DAMAGE_GLASS_ (with or without a zone token).</summary>
        public static bool IsGlassName(string name) { return name != null && name.StartsWith(GlassPrefix, StringComparison.OrdinalIgnoreCase); }

        /// <summary>Parses damage.ini (null = absent: nothing breaks, nothing bends). Problems are appended to <paramref name="problems"/>.</summary>
        public static AcCarDamageDef Parse(AcIni ini, List<string> problems)
        {
            var d = new AcCarDamageDef();
            if (ini == null) return d;
            d.Present = true;
            var sc = ini["SCRATCHES"];
            if (sc != null && (sc.Has("MIN_SPEED") || sc.Has("MAX_SPEED")))
            {
                d.HasScratches = true;
                d.ScratchMin = sc.GetFloat("MIN_SPEED", 0f);
                d.ScratchMax = sc.GetFloat("MAX_SPEED", 0f);
            }
            var dm = ini["DAMAGE"];
            if (dm != null && dm.Has("INITIAL_LEVEL")) { d.InitialLevel = dm.GetFloat("INITIAL_LEVEL", 0f); d.HasInitialLevel = true; }
            var os = ini["OSCILLATIONS"];
            if (os != null && os.Has("ENABLED")) d.Oscillations = os.GetInt("ENABLED", 1) != 0;
            for (int z = 0; z < 5; z++)
            {
                var g = ini[GlassPrefix + ZoneNames[z]];
                if (g == null) continue;
                d.HasGlass[z] = true;
                d.GlassMin[z] = g.GetFloat("MIN_SPEED", 0f);
                d.GlassFull[z] = g.GetFloat("FULL_SPEED", d.GlassMin[z]);
            }
            var vo = ini.WithPrefix("VISUAL_OBJECT_");
            for (int i = 0; i < vo.Count; i++)
            {
                var s = vo[i];
                string name = s.Get("NAME");
                name = name != null ? name.Trim() : null;
                if (string.IsNullOrEmpty(name)) { if (problems != null) problems.Add("damage.ini " + s.Name + " has no NAME"); continue; }
                var p = new AcCarPanelDef
                {
                    Name = name,
                    Zone = ZoneOf(s.Get("DAMAGE_ZONE")),
                    StaticAxis = V3(s, "STATIC_ROTATION_AXIS"),
                    StaticAngle = s.GetFloat("STATIC_ROTATION_ANGLE", 0f),
                    MultG = s.GetFloat("MULT_G", 0f),
                    MinSpeed = s.GetFloat("MIN_SPEED", 0f),
                    FullSpeed = s.GetFloat("FULL_SPEED", 0f),
                    OscAxis = V3(s, "OSCILLATION_AXIS"),
                    OscMin = s.GetFloat("OSCILLATION_MIN_ANGLE", 0f),
                    OscMax = s.GetFloat("OSCILLATION_MAX_ANGLE", 0f),
                    AllowedG = V3(s, "ALLOWED_G"),
                };
                if (p.Zone < 0) { if (problems != null) problems.Add("damage.ini " + s.Name + " DAMAGE_ZONE '" + s.Get("DAMAGE_ZONE") + "' unknown"); continue; }
                d.Panels.Add(p);
            }
            return d;
        }

        static AcVec3 V3(AcIniSection s, string key)
        {
            var f = s.GetFloats(key);
            if (f == null || f.Length == 0) return new AcVec3(0, 0, 0);
            return new AcVec3(f[0], f.Length > 1 ? f[1] : 0f, f.Length > 2 ? f[2] : 0f);
        }
    }

    /// <summary>
    /// Everything C needs to build and animate a car, read on a worker. <see cref="Load"/> never throws; problems go to <see cref="Problems"/>.
    /// </summary>
    public sealed class AcCarVisual
    {
        public string CarId = "", CarDir;
        public readonly List<string> Problems = new List<string>();

        // ---- car.ini
        public AcVec3 GraphicsOffset;
        public float GraphicsPitchDeg;
        /// <summary>[GRAPHICS] DRIVEREYES (MODEL space).</summary>
        public AcVec3 DriverEyes;
        public bool HasDriverEyes;
        public float SteerLockDeg = 360f, SteerRatio = 14f; // ours defaults when car.ini lacks them (never seen in the install)

        // ---- tyres.ini (VERSION 10: [FRONT]/[REAR] RADIUS)
        public float RadiusFront = 0.33f, RadiusRear = 0.33f; // ours defaults when absent
        public bool HasRadius;
        /// <summary>[COMPOUND_DEFAULT] INDEX (the compound whose RADIUS / PRESSURE_STATIC are read).</summary>
        public int DefaultCompound;
        /// <summary>PRESSURE_STATIC (psi, cold) of the default compound - the dash PRESSURE items without a tyre heat model.</summary>
        public float PressureFront = 26f, PressureRear = 26f; // ours defaults when absent (every owned car has PRESSURE_STATIC)
        public bool HasPressure;

        // ---- lods.ini
        public readonly List<AcCarLod> Lods = new List<AcCarLod>();
        /// <summary>[COCKPIT_HR] DISTANCE_SWITCH (m): COCKPIT_HR shown closer than this, COCKPIT_LR beyond.</summary>
        public float CockpitHrSwitch = 7f; // ours default when absent (the 458's value)
        /// <summary>[DRIVER_HR] DISTANCE_SWITCH (m), 0 when absent.</summary>
        public float DriverHrSwitch;

        // ---- lights.ini
        public int LightsVersion;
        public readonly List<AcCarLightDef> Lights = new List<AcCarLightDef>();
        public readonly List<AcCarLightAnimDef> LightAnims = new List<AcCarLightAnimDef>();
        public bool NoLightSwitch;

        // ---- blurred_objects.ini
        public readonly List<AcCarBlurDef> Blur = new List<AcCarBlurDef>();

        // ---- analog_instruments.ini
        public readonly List<AcCarNeedleDef> Needles = new List<AcCarNeedleDef>();

        // ---- digital_instruments.ini (+ FUEL_WARNING of analog_instruments.ini)
        public readonly List<AcCarDigitalItemDef> Items = new List<AcCarDigitalItemDef>();
        public readonly List<AcCarLedDef> Leds = new List<AcCarLedDef>();
        public readonly List<AcCarLedSeriesDef> Series = new List<AcCarLedSeriesDef>();
        public readonly List<AcCarFuelLedDef> FuelLeds = new List<AcCarFuelLedDef>();
        public readonly List<AcCarDisplayDef> Displays = new List<AcCarDisplayDef>();

        // ---- driver3d.ini
        public AcCarDriverDef Driver = new AcCarDriverDef();

        // ---- damage.ini (iteration 2; never null)
        public AcCarDamageDef Damage = new AcCarDamageDef();

        // ---- driver_base_pos.knh (MODEL space world positions)
        public Knh Knh;
        public bool HasKnh;
        public AcVec3 KnhCenter, KnhHips, KnhHead, KnhHandL, KnhHandR;

        // ---- animations/ (null when the car has none)
        public Ksanim DoorL, DoorR, Wiper, ShiftUp, ShiftDown, Shift, DriverSteer, DriverShiftUp, DriverShiftDown, DriverShift;
        public readonly Ksanim[] Susp = new Ksanim[4];
        /// <summary>Every *.ksanim file of the car by lower-case file name (paths).</summary>
        public readonly Dictionary<string, string> AnimFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Front wheels 0/1 use <see cref="RadiusFront"/>, rears 2/3 <see cref="RadiusRear"/>.</summary>
        public float Radius(int wheel) { return wheel < 2 ? RadiusFront : RadiusRear; }

        /// <summary>The LOD entry by index (null when absent).</summary>
        public AcCarLod Lod(int i)
        {
            for (int k = 0; k < Lods.Count; k++) if (Lods[k].Index == i) return Lods[k];
            return null;
        }

        /// <summary>Loads a car's visual data. <paramref name="animations"/>/<paramref name="knh"/> = also read those files.</summary>
        public static AcCarVisual Load(AcCarEntry car, bool animations = true, bool knh = true)
        {
            var v = new AcCarVisual();
            if (car == null) { v.Problems.Add("no car"); return v; }
            v.CarId = car.Id ?? "";
            v.CarDir = car.Dir;
            AcdArchive acd = null;
            try { acd = AcdArchive.Open(car.Dir); }
            catch (Exception e) { v.Problems.Add("data.acd: " + e.Message); }
            if (acd == null) v.Problems.Add("no data.acd / data folder");
            v.ReadArchive(acd, car.Kn5Path);
            if (knh) v.ReadKnh(car.KnhPath);
            if (animations) v.ReadAnimations(car.AnimationsDir);
            return v;
        }

        /// <summary>Parses the inis from an opened archive (harness / tests). <paramref name="fallbackKn5"/> = LOD_0 when lods.ini is missing.</summary>
        public void ReadArchive(AcdArchive acd, string fallbackKn5)
        {
            if (acd != null)
            {
                Safe("car.ini", () => ReadCarIni(acd.ReadIni("car.ini")));
                Safe("tyres.ini", () => ReadTyres(acd.ReadIni("tyres.ini")));
                Safe("lods.ini", () => ReadLods(acd.ReadIni("lods.ini")));
                Safe("lights.ini", () => ReadLights(acd.ReadIni("lights.ini")));
                Safe("blurred_objects.ini", () => ReadBlur(acd.ReadIni("blurred_objects.ini")));
                Safe("analog_instruments.ini", () => ReadAnalog(acd.ReadIni("analog_instruments.ini")));
                Safe("digital_instruments.ini", () => ReadDigital(acd.ReadIni("digital_instruments.ini")));
                Safe("driver3d.ini", () => ReadDriver(acd.ReadIni("driver3d.ini")));
                Safe("damage.ini", () => Damage = AcCarDamageDef.Parse(acd.Has("damage.ini") ? acd.ReadIni("damage.ini") : null, Problems));
            }
            if (Lod(0) == null && !string.IsNullOrEmpty(fallbackKn5))
            {
                Lods.Insert(0, new AcCarLod { Index = 0, File = System.IO.Path.GetFileName(fallbackKn5), Path = fallbackKn5, In = 0, Out = 2000 }); // ours range
                Problems.Add("lods.ini has no LOD_0: using " + System.IO.Path.GetFileName(fallbackKn5));
            }
        }

        void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Problems.Add(what + ": " + e.Message); }
        }

        static AcVec3 Vec(AcIniSection s, string key, AcVec3 def)
        {
            if (s == null) return def;
            var f = s.GetFloats(key);
            if (f == null || f.Length == 0) return def;
            return new AcVec3(f[0], f.Length > 1 ? f[1] : 0f, f.Length > 2 ? f[2] : 0f);
        }

        static string Str(AcIniSection s, string key)
        {
            string v = s != null ? s.Get(key) : null;
            if (v == null) return null;
            v = v.Trim();
            return v.Length == 0 ? null : v;
        }

        void ReadCarIni(AcIni ini)
        {
            if (ini == null) { Problems.Add("car.ini missing"); return; }
            var b = ini["BASIC"];
            GraphicsOffset = Vec(b, "GRAPHICS_OFFSET", new AcVec3(0, 0, 0));
            GraphicsPitchDeg = b != null ? b.GetFloat("GRAPHICS_PITCH_ROTATION", 0f) : 0f;
            var g = ini["GRAPHICS"];
            if (g != null && g.Has("DRIVEREYES")) { DriverEyes = Vec(g, "DRIVEREYES", new AcVec3(0, 0, 0)); HasDriverEyes = true; }
            var c = ini["CONTROLS"];
            if (c != null)
            {
                SteerLockDeg = c.GetFloat("STEER_LOCK", SteerLockDeg);
                SteerRatio = c.GetFloat("STEER_RATIO", SteerRatio);
            }
        }

        void ReadTyres(AcIni ini)
        {
            if (ini == null) { Problems.Add("tyres.ini missing"); return; }
            // the compound AC mounts: [COMPOUND_DEFAULT] INDEX n -> [FRONT] / [REAR] for 0, [FRONT_n] / [REAR_n] otherwise (56 of 86 cars use 1 or 2)
            var cd = ini["COMPOUND_DEFAULT"];
            DefaultCompound = cd != null ? cd.GetInt("INDEX", 0) : 0;
            string sfx = DefaultCompound > 0 ? "_" + DefaultCompound : "";
            var f = ini["FRONT" + sfx]; var r = ini["REAR" + sfx];
            if (f == null || r == null) { if (DefaultCompound > 0) Problems.Add("tyres.ini compound " + DefaultCompound + " missing, using [FRONT]/[REAR]"); f = ini["FRONT"]; r = ini["REAR"]; }
            if (f != null && f.Has("RADIUS")) { RadiusFront = f.GetFloat("RADIUS", RadiusFront); HasRadius = true; }
            if (r != null && r.Has("RADIUS")) RadiusRear = r.GetFloat("RADIUS", RadiusRear);
            else if (HasRadius) RadiusRear = RadiusFront;
            if (f != null && f.Has("PRESSURE_STATIC")) { PressureFront = f.GetFloat("PRESSURE_STATIC", PressureFront); HasPressure = true; }
            if (r != null && r.Has("PRESSURE_STATIC")) PressureRear = r.GetFloat("PRESSURE_STATIC", PressureRear);
            else if (HasPressure) PressureRear = PressureFront;
        }

        /// <summary>skin.ini [&lt;driver model&gt;] SUIT / GLOVES / HELMET (e.g. SUIT=\sparco\white) -> folders under content/texture/driver_suit,
        /// driver_gloves, driver_helmet whose .dds files replace the driver kn5's textures of the same name. Null entries when absent.</summary>
        public static string[] DriverSkinDirs(string skinDir, string driverModel, string contentDir)
        {
            var dirs = new string[3];
            if (string.IsNullOrEmpty(skinDir) || string.IsNullOrEmpty(driverModel) || string.IsNullOrEmpty(contentDir)) return dirs;
            string p = System.IO.Path.Combine(skinDir, "skin.ini");
            if (!File.Exists(p)) return dirs;
            var ini = AcIni.Load(p);
            var s = ini != null ? ini[driverModel] : null;
            if (s == null) return dirs;
            string[] keys = { "SUIT", "GLOVES", "HELMET" };
            string[] roots = { "driver_suit", "driver_gloves", "driver_helmet" };
            for (int i = 0; i < 3; i++)
            {
                string v = Str(s, keys[i]);
                if (v == null) continue;
                string d = System.IO.Path.Combine(System.IO.Path.Combine(contentDir, "texture"), roots[i]);
                d = System.IO.Path.Combine(d, v.Trim().TrimStart('\\', '/').Replace('\\', System.IO.Path.DirectorySeparatorChar));
                if (Directory.Exists(d)) dirs[i] = d;
            }
            return dirs;
        }

        void ReadLods(AcIni ini)
        {
            if (ini == null) { Problems.Add("lods.ini missing"); return; }
            var ch = ini["COCKPIT_HR"];
            if (ch != null) CockpitHrSwitch = ch.GetFloat("DISTANCE_SWITCH", CockpitHrSwitch);
            var dh = ini["DRIVER_HR"];
            if (dh != null) DriverHrSwitch = dh.GetFloat("DISTANCE_SWITCH", 0f);
            var l = ini.WithPrefix("LOD_");
            for (int i = 0; i < l.Count; i++)
            {
                int idx;
                if (!int.TryParse(l[i].Name.Substring(4), out idx)) continue;
                string file = Str(l[i], "FILE");
                if (file == null) continue;
                var lod = new AcCarLod { Index = idx, File = file, In = l[i].GetFloat("IN", 0f), Out = l[i].GetFloat("OUT", 2000f) }; // ours default OUT
                if (CarDir != null)
                {
                    string p = System.IO.Path.Combine(CarDir, file);
                    if (File.Exists(p)) lod.Path = p;
                    else Problems.Add("lods.ini LOD_" + idx + " file missing: " + file);
                }
                Lods.Add(lod);
            }
            Lods.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        void ReadLights(AcIni ini)
        {
            if (ini == null) return;
            var h = ini["HEADER"];
            LightsVersion = h != null ? h.GetInt("VERSION", 1) : 1;
            NoLightSwitch = h != null && h.GetBool("NO_LIGHT_SWITCH", false);
            AddLights(ini.WithPrefix("BRAKE_"), true);
            AddLights(ini.WithPrefix("LIGHT_"), false);
            var anims = ini.WithPrefix("LIGHT_ANIMATION_");
            for (int i = 0; i < anims.Count; i++)
            {
                string file = Str(anims[i], "FILE");
                if (file == null) continue;
                LightAnims.Add(new AcCarLightAnimDef { File = file, Time = anims[i].GetFloat("TIME", 1f) }); // ours default 1 s
            }
        }

        void AddLights(List<AcIniSection> l, bool brake)
        {
            for (int i = 0; i < l.Count; i++)
            {
                var s = l[i];
                if (s.Name.StartsWith("LIGHT_ANIMATION", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Str(s, "NAME");
                if (name == null || string.Equals(name, "NULL", StringComparison.OrdinalIgnoreCase)) continue;
                var d = new AcCarLightDef { Brake = brake, Name = name };
                if (s.Has("COLOR")) d.Color = Vec(s, "COLOR", new AcVec3(0, 0, 0));
                else if (s.Has("INTENSITY")) d.Color = new AcVec3(s.GetFloat("INTENSITY", 0f), 0f, 0f); // v1 brake: red [BELIEF]
                if (s.Has("OFF_COLOR")) { d.OffColor = Vec(s, "OFF_COLOR", new AcVec3(0, 0, 0)); d.HasOffColor = true; }
                d.Flash = s.GetBool("FLASH", false);
                d.Pitline = s.GetBool("PITLINE", false);
                d.Special = s.GetBool("SPECIAL", false);
                Lights.Add(d);
            }
        }

        void ReadBlur(AcIni ini)
        {
            if (ini == null) return;
            var l = ini.WithPrefix("OBJECT_");
            for (int i = 0; i < l.Count; i++)
            {
                string name = Str(l[i], "NAME");
                if (name == null) continue;
                Blur.Add(new AcCarBlurDef
                {
                    Wheel = l[i].GetInt("WHEEL_INDEX", 0),
                    Name = name,
                    MinKmh = l[i].GetFloat("MIN_SPEED", 0f),
                    MaxKmh = l[i].GetFloat("MAX_SPEED", 100000f), // ours default (no upper bound)
                });
            }
        }

        void ReadAnalog(AcIni ini)
        {
            if (ini == null) return;
            for (int i = 0; i < ini.Order.Count; i++)
            {
                var s = ini.Order[i];
                string n = s.Name.ToUpperInvariant();
                AcGaugeKind k;
                if (n.StartsWith("RPM_INDICATOR")) k = AcGaugeKind.Rpm;
                else if (n.StartsWith("SPEED_INDICATOR")) k = AcGaugeKind.Speed;
                else if (n.StartsWith("FUEL_INDICATOR")) k = AcGaugeKind.Fuel;
                else if (n.StartsWith("TURBO_INDICATOR")) k = AcGaugeKind.Turbo;
                else if (n.StartsWith("WATER_TEMP")) k = AcGaugeKind.WaterTemp;
                else if (n.StartsWith("PLACE_HOLDER")) k = AcGaugeKind.PlaceHolder;
                else if (n.StartsWith("FUEL_WARNING")) { AddFuelWarning(s); continue; }
                else continue;
                var d = new AcCarNeedleDef
                {
                    Kind = k,
                    Section = s.Name,
                    ObjectName = Str(s, "OBJECT_NAME"),
                    ObjectNameMax = Str(s, "OBJECT_NAME_MAX"),
                    Zero = s.GetFloat("ZERO", 0f),
                    Step = s.GetFloat("STEP", 0f),
                    HasMinValue = s.Has("MIN_VALUE"),
                    MinValue = s.GetFloat("MIN_VALUE", 0f),
                    UseBar = s.GetBool("USE_BAR", false),
                };
                string lut = Str(s, "LUT");
                if (lut != null) d.Lut = AcLut.ParseInline(lut);
                if (d.ObjectName == null) { d.NameDefaulted = true; d.ObjectName = AcCarNeedleDef.DefaultName(k); }
                if (d.ObjectName == null) continue;
                Needles.Add(d);
            }
        }

        void AddFuelWarning(AcIniSection s)
        {
            string name = Str(s, "OBJECT_NAME");
            if (name == null) return;
            var d = new AcCarFuelLedDef { ObjectName = name, FuelSwitch = s.GetFloat("FUEL_SWITCH", 0f), Warning = true, Inverted = s.GetBool("INVERTED", false) };
            if (s.Has("EMISSIVE")) { d.Emissive = Vec(s, "EMISSIVE", new AcVec3(0, 0, 0)); d.HasEmissive = true; }
            FuelLeds.Add(d);
        }

        void ReadDigital(AcIni ini)
        {
            if (ini == null) return;
            for (int i = 0; i < ini.Order.Count; i++)
            {
                var s = ini.Order[i];
                string n = s.Name.ToUpperInvariant();
                if (n.StartsWith("ITEM_")) AddItem(s);
                else if (n.StartsWith("LED_")) AddLed(s);
                else if (n.StartsWith("DISPLAY_"))
                {
                    string name = Str(s, "NAME");
                    if (name != null) Displays.Add(new AcCarDisplayDef { Name = name, Emissive = Vec(s, "EMISSIVE", new AcVec3(0, 0, 0)), Intensity = s.GetFloat("INTENSITY", 1f) });
                }
                else if (n.StartsWith("FUEL_WARNING")) AddFuelWarning(s);
                else if (n.StartsWith("FUEL_"))
                {
                    string name = Str(s, "OBJECT_NAME");
                    if (name != null) FuelLeds.Add(new AcCarFuelLedDef { ObjectName = name, FuelSwitch = s.GetFloat("FUEL_SWITCH", 0f) });
                }
                else if (n.StartsWith("RPM_SERIE")) AddSeries(s, AcLedSeriesKind.Rpm, "RPM_START", "RPM_END");
                else if (n.StartsWith("SPEED_SERIE")) AddSeries(s, AcLedSeriesKind.Speed, "START_SPEED", "END_SPEED");
                else if (n.StartsWith("TURBO_BOOST_SERIE")) AddSeries(s, AcLedSeriesKind.Turbo, "BOOST_START", "BOOST_END");
                else if (n.StartsWith("WATER_TEMP")) AddSeries(s, AcLedSeriesKind.WaterTemp, "START_TEMP", "END_TEMP");
                else if (n.StartsWith("GFORCE_SERIE")) // huracan performante: 4 bars (DIRECTION X/Z, SIGN +-1, FORCE_START..FORCE_END g)
                {
                    var d = AddSeries(s, AcLedSeriesKind.GForce, "FORCE_START", "FORCE_END");
                    if (d != null)
                    {
                        string dir = Str(s, "DIRECTION");
                        d.Axis = dir != null && char.ToUpperInvariant(dir[0]) == 'Z' ? 'Z' : 'X';
                        d.Sign = s.GetFloat("SIGN", 1f) < 0f ? -1f : 1f;
                    }
                }
                // KERS_CHARGE_SERIE / KERS_INPUT_SERIE (1 car each): no KERS in the Dreamport physics - not built.
            }
        }

        AcCarLedSeriesDef AddSeries(AcIniSection s, AcLedSeriesKind kind, string startKey, string endKey)
        {
            string prefix = Str(s, "PREFIX");
            if (prefix == null) return null;
            var d = new AcCarLedSeriesDef { Kind = kind, Prefix = prefix, Start = s.GetFloat(startKey, 0f), End = s.GetFloat(endKey, 0f) };
            if (s.Has("LED_COUNT")) { d.CountBased = true; d.StartIndex = 0; d.EndIndex = s.GetInt("LED_COUNT", 0) - 1; }
            else { d.StartIndex = s.GetInt("START_INDEX", 0); d.EndIndex = s.GetInt("END_INDEX", 0); }
            if (d.EndIndex < d.StartIndex) return null;
            Series.Add(d);
            return d;
        }

        void AddLed(AcIniSection s)
        {
            string name = Str(s, "OBJECT_NAME");
            if (name == null) return;
            Leds.Add(new AcCarLedDef
            {
                ObjectName = name,
                RpmSwitch = s.GetFloat("RPM_SWITCH", 0f),
                HasBlink = s.Has("BLINK_SWITCH"),
                BlinkSwitch = s.GetFloat("BLINK_SWITCH", 0f),
                BlinkHz = s.GetFloat("BLINK_HZ", 0f),
                Diffuse = s.GetFloat("DIFFUSE", 0f),
                Emissive = Vec(s, "EMISSIVE", new AcVec3(0, 0, 0)),
            });
        }

        void AddItem(AcIniSection s)
        {
            string type = Str(s, "TYPE");
            string parent = Str(s, "PARENT");
            if (type == null || parent == null) return;
            var d = new AcCarDigitalItemDef
            {
                Section = s.Name,
                Type = type.ToUpperInvariant(),
                Parent = parent,
                Position = Vec(s, "POSITION", new AcVec3(0, 0, 0)),
                Size = s.GetFloat("SIZE", 0.01f), // ours default when absent (only RPM_GRAPH items lack SIZE)
                Intensity = s.GetFloat("INTENSITY", 1f),
                Font = Str(s, "FONT"),
                Version = s.GetInt("VERSION", 1),
                Align = (Str(s, "ALIGN") ?? "LEFT").ToUpperInvariant(),
                Units = Str(s, "UNITS"),
                Prefix = s.Get("PREFIX", "") ?? "",
                Postfix = s.Get("POSTFIX", "") ?? "",
                Text = s.Get("TEXT", "") ?? "",
                Decimals = s.GetInt("DECIMALS", -1),
                Width = s.GetFloat("WIDTH", 0f),
                Height = s.GetFloat("HEIGHT", 0f),
                TextureBase = Str(s, "TEXTURE_BASE"),
                TextureTop = Str(s, "TEXTURE_TOP"),
                RpmMin = s.GetFloat("RPM_MIN", 0f),
                RpmMax = s.GetFloat("RPM_MAX", 0f),
                TyreNumber = s.GetInt("TYRE_NUMBER", 0),
            };
            var c = s.GetFloats("COLOR");
            if (c != null && c.Length >= 3) { d.R = c[0]; d.G = c[1]; d.B = c[2]; d.A = c.Length > 3 ? c[3] : 255f; }
            if (s.Has("MIN")) d.Min = s.GetFloat("MIN", 0f);
            if (s.Has("MAX")) d.Max = s.GetFloat("MAX", 0f);
            d.AddSign = s.GetInt("ADDSIGN", 0) != 0;
            string dir = Str(s, "DIRECTION");
            if (dir != null) d.Direction = char.ToUpperInvariant(dir[0]) == 'Z' ? 'Z' : 'X';
            d.NTime = s.GetFloat("N_TIME", 0f);
            if (s.Has("TRIGGER")) d.Trigger = s.GetInt("TRIGGER", -1);
            // VERSION (=2 on all 366 items that carry it) is read but changes nothing [BELIEF: v1/v2 differ only in older font metrics].
            d.Prefix = d.Prefix.Trim(); d.Postfix = d.Postfix.Trim(); d.Text = d.Text.Trim();
            Items.Add(d);
        }

        void ReadDriver(AcIni ini)
        {
            if (ini == null) return;
            var m = ini["MODEL"];
            Driver.Model = Str(m, "NAME");
            Driver.Position = Vec(m, "POSITION", new AcVec3(0, 0, 0));
            var st = ini["STEER_ANIMATION"];
            Driver.SteerAnimName = Str(st, "NAME");
            if (st != null) Driver.SteerLock = st.GetFloat("LOCK", Driver.SteerLock);
            var sh = ini["SHIFT_ANIMATION"];
            if (sh != null)
            {
                Driver.BlendMs = sh.GetFloat("BLEND_TIME", 100f);
                Driver.PositiveMs = sh.GetFloat("POSITIVE_TIME", 150f);
                Driver.StaticMs = sh.GetFloat("STATIC_TIME", 10f);
                Driver.NegativeMs = sh.GetFloat("NEGATIVE_TIME", 150f);
                Driver.PreloadRpm = sh.GetFloat("PRELOAD_RPM", 0f);
                Driver.InvertShiftingHands = sh.GetBool("INVERT_SHIFTING_HANDS", false);
            }
            else { Driver.BlendMs = 100f; Driver.PositiveMs = 150f; Driver.StaticMs = 10f; Driver.NegativeMs = 150f; } // ours: the most common values in the install
            var hide = ini.WithPrefix("HIDE_OBJECT_");
            for (int i = 0; i < hide.Count; i++) { string n = Str(hide[i], "NAME"); if (n != null) Driver.HideObjects.Add(n); }
        }

        /// <summary>Reads driver_base_pos.knh (seat, hips, head, hands in MODEL space).</summary>
        public void ReadKnh(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Problems.Add("no driver_base_pos.knh"); return; }
            try
            {
                Knh = Knh.Read(path);
                HasKnh = true;
                KnhCenter = Pos("DRIVER:RIG_Center", "RIG_Center");
                KnhHips = Pos("DRIVER:RIG_Hips", "RIG_Hips");
                KnhHead = Pos("DRIVER:RIG_Head", "RIG_Head");
                KnhHandL = Pos("DRIVER:RIG_HAND_L", "RIG_HAND_L");
                KnhHandR = Pos("DRIVER:RIG_HAND_R", "RIG_HAND_R");
            }
            catch (Exception e) { Problems.Add("knh: " + e.Message); HasKnh = false; }
        }

        AcVec3 Pos(string name, string alt)
        {
            var p = Knh.WorldPosition(name) ?? Knh.WorldPosition(alt);
            if (p == null) { Problems.Add("knh has no " + name); return new AcVec3(0, 0, 0); }
            return p.Value;
        }

        /// <summary>Reads the car's ksanim files (names case-insensitive: car_DOOR_L / car_door_L).</summary>
        public void ReadAnimations(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.ksanim"); }
            catch (Exception e) { Problems.Add("animations: " + e.Message); return; }
            for (int i = 0; i < files.Length; i++) AnimFiles[System.IO.Path.GetFileName(files[i]).ToLowerInvariant()] = files[i];
            DoorL = Anim("car_door_l.ksanim");
            DoorR = Anim("car_door_r.ksanim");
            Wiper = Anim("car_wiper.ksanim");
            ShiftUp = Anim("car_shift_up.ksanim");
            ShiftDown = Anim("car_shift_dw.ksanim");
            Shift = Anim("car_shift.ksanim");
            Susp[0] = Anim("car_susp_lf.ksanim"); Susp[1] = Anim("car_susp_rf.ksanim");
            Susp[2] = Anim("car_susp_lr.ksanim"); Susp[3] = Anim("car_susp_rr.ksanim");
            DriverSteer = Anim(string.IsNullOrEmpty(Driver.SteerAnimName) ? "steer.ksanim" : Driver.SteerAnimName);
            DriverShiftUp = Anim("shift_up.ksanim");
            DriverShiftDown = Anim("shift_dw.ksanim");
            DriverShift = Anim("shift.ksanim");
            // 4 cars of 6 with an animations/lights.ksanim declare no [LIGHT_ANIMATION_n] (ks_mazda_miata pop-ups, ks_lamborghini_miura_sv
            // lids, ks_audi_r8_plus, ks_maserati_levante): play the file by its conventional name [BELIEF: AC's default], TIME 1 s like the 4 that do
            if (LightAnims.Count == 0 && AnimFiles.ContainsKey("lights.ksanim")) LightAnims.Add(new AcCarLightAnimDef { File = "lights.ksanim", Time = 1f }); // ours TIME
            for (int i = 0; i < LightAnims.Count; i++) LightAnims[i].Anim = Anim(LightAnims[i].File);
        }

        Ksanim Anim(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string p;
            if (!AnimFiles.TryGetValue(System.IO.Path.GetFileName(file).ToLowerInvariant(), out p)) return null;
            try { return Ksanim.Read(p); }
            catch (Exception e) { Problems.Add(file + ": " + e.Message); return null; }
        }

        /// <summary>One-line summary for logs / the harness.</summary>
        public string Summary()
        {
            return CarId + ": lods " + Lods.Count + ", lights " + Lights.Count + " (v" + LightsVersion + "), blur " + Blur.Count + ", needles " + Needles.Count
                + ", items " + Items.Count + ", leds " + Leds.Count + ", series " + Series.Count + ", fuel leds " + FuelLeds.Count
                + ", knh " + (HasKnh ? "yes" : "no") + ", doors " + (DoorL != null ? "L" : "-") + (DoorR != null ? "R" : "-")
                + ", wiper " + (Wiper != null ? "yes" : "no") + ", shift " + (ShiftUp != null ? "up/dw" : Shift != null ? "single" : "-")
                + ", driver " + (Driver.Model ?? "-") + ", tyres compound " + DefaultCompound + " r " + RadiusFront.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "/" + RadiusRear.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " psi " + PressureFront.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "/" + PressureRear.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + (HasPressure ? "" : " (default)");
        }
    }
}
