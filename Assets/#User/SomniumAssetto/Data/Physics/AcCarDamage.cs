using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    // [P, Physics] Iteration 2: car damage (DevTools/agents/DAMAGE_DESIGN.md sections 1, 3, 7, 9). Unity-FREE (the harness compiles it),
    // allocation-free in steady state, main thread only. Every AC value is read at run time (system/cfg, the car's data.acd); numbers we
    // had to invent are marked // ours. Damage multiplier 0 = off = iteration-1 behaviour exactly (nothing accumulates, wire ints are 0).

    /// <summary>AC's five body zones (shared-memory carDamage[5] order; damage_displayer / damage.ini names).</summary>
    public enum AcDamageZone { Front = 0, Rear = 1, Left = 2, Right = 3, Center = 4 }

    /// <summary>What a repair fixes (AC's pit menu: REPAIR BODY, REPAIR BODY + SUSPENSION, REPAIR ENGINE). Suspension implies Body
    /// (changelog 1.12 "Suspension repair will automatically fix body too").</summary>
    [Flags] public enum AcRepair { None = 0, Body = 1, Suspension = 2 /* implies Body */, Engine = 4, All = 7 }

    /// <summary>What changed in a car's damage since the last AcEvents.CarDamaged (DAMAGE_DESIGN 1.7 / 9 "lead"). Declared here, not in
    /// Game/Shared/AcEvents.cs, because the Unity-free <see cref="AcCarDamage.TakeChanges()"/> returns it (Data/ compiles without Game/).</summary>
    [Flags] public enum AcDamageChange { None = 0, Zones = 1, Suspension = 2, EngineSmoke = 4, EngineDead = 8, Repaired = 16 }

    /// <summary>How repeated hits combine in a zone (risk R2): SUM (the design's reading) or MAX. One switch, // ours.</summary>
    public enum AcDamageAccumulate { Sum = 0, Max = 1 }

    /// <summary>Install-wide damage settings from system/cfg (and the player's Documents cfg, the server cfg), loaded once per install and
    /// cached. Never null through <see cref="Current"/> (fallbacks = the install's own values, used only when a key is absent).</summary>
    public sealed class AcDamageRules
    {
        /// <summary>system/cfg/assetto_corsa.ini [DAMAGE] GLASS_THRESHOLD: a glass zone shows broken at g >= this.</summary>
        public float GlassThreshold = 0.65f;      // ours fallback = the install value, only when the key is absent
        /// <summary>system/cfg/damage_displayer.ini [MAIN] TIME (s the displayer stays after a hit), MAX_DAMAGE_KMH (100 % body).</summary>
        public float DisplayTimeS = 4f, MaxDamageKmh = 60f; // ours fallbacks (= the install values)
        /// <summary>system/cfg/engine_smoke.ini [TRIGGERS] ENGINE_LIFE (read as % of life left, // ours reading) and [LIFE_TIME] SECONDS;
        /// 0 / 0 when the file is missing (no smoke).</summary>
        public float SmokeEngineLife, SmokeSeconds;
        /// <summary>damage_displayer.ini PRINT_VALUES; Documents/Assetto Corsa/cfg/gameplay.ini [DAMAGE_DISPLAYER] IS_ACTIVE (1 when absent).</summary>
        public bool PrintValues, DisplayerDefaultOn = true;
        /// <summary>server/cfg/server_cfg.ini [SERVER] DAMAGE_MULTIPLIER (0..100); 100 when absent (// ours = AC's server default).</summary>
        public int DefaultMultiplier = 100;
        /// <summary>damage_displayer.ini POSITION_X / DISTANCE_FROM_CENTER_Y (H maps them).</summary>
        public float PosX = 20f, CenterY = -200f; // ours fallbacks (= the install values)
        /// <summary>The whole engine_smoke.ini ([SETTINGS] particle values for C); empty when missing.</summary>
        public AcIni EngineSmokeIni = AcIni.Empty;
        /// <summary>Install root this was read from ("" = fallbacks only).</summary>
        public string Root = "";
        /// <summary>What was read (one log line).</summary>
        public string Source = "fallbacks";

        private static AcDamageRules _current;
        private static readonly object Gate = new object();

        /// <summary>The rules of the last loaded install, or the fallbacks before any load. Never null.</summary>
        public static AcDamageRules Current { get { AcDamageRules c = _current; if (c != null) return c; lock (Gate) { if (_current == null) _current = new AcDamageRules(); return _current; } } }

        /// <summary>True once <see cref="Load"/> read an install.</summary>
        public static bool Loaded { get { AcDamageRules c = _current; return c != null && c.Root.Length > 0; } }

        /// <summary>Reads the install's damage settings (cached per install root; a second call with the same install returns the cache) and
        /// makes them <see cref="Current"/>. Never throws; missing files keep the fallbacks.</summary>
        public static AcDamageRules Load(AcInstall install)
        {
            if (install == null || string.IsNullOrEmpty(install.Root)) return Current;
            lock (Gate)
            {
                if (_current != null && string.Equals(_current.Root, install.Root, StringComparison.OrdinalIgnoreCase)) return _current;
                var r = new AcDamageRules { Root = install.Root };
                var src = new System.Text.StringBuilder();
                try
                {
                    string cfg = Path.Combine(install.SystemDir, "cfg");
                    AcIni ac = AcIni.Load(Path.Combine(cfg, "assetto_corsa.ini"));
                    if (ac != null && ac.Has("DAMAGE", "GLASS_THRESHOLD")) { r.GlassThreshold = ac.GetFloat("DAMAGE", "GLASS_THRESHOLD", r.GlassThreshold); src.Append("assetto_corsa.ini "); }
                    AcIni dd = AcIni.Load(Path.Combine(cfg, "damage_displayer.ini"));
                    if (dd != null)
                    {
                        r.DisplayTimeS = dd.GetFloat("MAIN", "TIME", r.DisplayTimeS);
                        r.MaxDamageKmh = dd.GetFloat("MAIN", "MAX_DAMAGE_KMH", r.MaxDamageKmh);
                        r.PrintValues = dd.GetInt("MAIN", "PRINT_VALUES", 0) != 0;
                        r.PosX = dd.GetFloat("MAIN", "POSITION_X", r.PosX);
                        r.CenterY = dd.GetFloat("MAIN", "DISTANCE_FROM_CENTER_Y", r.CenterY);
                        src.Append("damage_displayer.ini ");
                    }
                    if (r.MaxDamageKmh < 1f) r.MaxDamageKmh = 60f; // ours guard (division)
                    AcIni sm = AcIni.Load(Path.Combine(cfg, "engine_smoke.ini"));
                    if (sm != null)
                    {
                        r.EngineSmokeIni = sm;
                        r.SmokeEngineLife = sm.GetFloat("TRIGGERS", "ENGINE_LIFE", 0f);
                        r.SmokeSeconds = sm.GetFloat("LIFE_TIME", "SECONDS", 0f);
                        src.Append("engine_smoke.ini ");
                    }
                    AcIni sv = AcIni.Load(Path.Combine(Path.Combine(Path.Combine(install.Root, "server"), "cfg"), "server_cfg.ini"));
                    if (sv != null && sv.Has("SERVER", "DAMAGE_MULTIPLIER")) { r.DefaultMultiplier = Math.Max(0, Math.Min(100, sv.GetInt("SERVER", "DAMAGE_MULTIPLIER", 100))); src.Append("server_cfg.ini "); }
                    string docs = null;
                    try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch (Exception) { docs = null; }
                    if (!string.IsNullOrEmpty(docs))
                    {
                        AcIni gp = AcIni.Load(Path.Combine(Path.Combine(Path.Combine(docs, "Assetto Corsa"), "cfg"), "gameplay.ini"));
                        if (gp != null && gp.Has("DAMAGE_DISPLAYER", "IS_ACTIVE")) { r.DisplayerDefaultOn = gp.GetInt("DAMAGE_DISPLAYER", "IS_ACTIVE", 1) != 0; src.Append("gameplay.ini"); }
                    }
                }
                catch (Exception e) { src.Append(" (failed: " + e.GetType().Name + " " + e.Message + ")"); }
                r.Source = src.Length > 0 ? src.ToString().Trim() : "fallbacks";
                _current = r;
                AcLog.Info("Physics: damage rules from " + r.Source + ": glass threshold " + r.GlassThreshold + ", displayer " + r.DisplayTimeS + " s / " + r.MaxDamageKmh +
                    " km/h, smoke at " + r.SmokeEngineLife + " % life for " + r.SmokeSeconds + " s, default multiplier " + r.DefaultMultiplier + " %, displayer " + (r.DisplayerDefaultOn ? "on" : "off"));
                return r;
            }
        }
    }

    /// <summary>A car's mechanical damage keys (parsed in AcCarSpec.FromArchive, worker-safe): suspensions.ini [DAMAGE], engine.ini [DAMAGE],
    /// car.ini [PIT_STOP]. An absent key = that effect off (the 3 Audi quattros: no rod damage; miura_sv / 787b: no turbo damage).</summary>
    public sealed class AcDamageSpec
    {
        /// <summary>suspensions.ini [DAMAGE] MIN_VELOCITY (km/h), GAIN (m of steer-rod deflection per impact km/h), MAX_DAMAGE (m).</summary>
        public float SuspMinVelocity, SuspGain, SuspMaxDamage; public bool HasSusp;
        /// <summary>engine.ini [DAMAGE] RPM_THRESHOLD / RPM_DAMAGE_K (life per s per rpm over), TURBO_BOOST_THRESHOLD (TOTAL boost) /
        /// TURBO_DAMAGE_K (life per s per bar over).</summary>
        public float RpmThreshold, RpmDamageK, TurboBoostThreshold, TurboDamageK; public bool HasTurboDamage;
        /// <summary>True when engine.ini [DAMAGE] RPM_THRESHOLD and RPM_DAMAGE_K are present (86/86 owned cars).</summary>
        public bool HasRpmDamage;
        /// <summary>car.ini [PIT_STOP] BODY / ENGINE / SUSP_REPAIR_TIME_SEC: seconds per 10 % of that damage (0 when absent).</summary>
        public float PitBodySecPer10, PitEngineSecPer10, PitSuspSecPer10;
        /// <summary>True when car.ini has a [PIT_STOP] section.</summary>
        public bool HasPitStop;

        /// <summary>Reads the keys from the car's parsed inis (missing inis = everything off). Never throws.</summary>
        public static AcDamageSpec From(AcIni car, AcIni engine, AcIni susp)
        {
            var d = new AcDamageSpec();
            AcIniSection s = susp != null ? susp["DAMAGE"] : null;
            if (s != null && s.Has("GAIN") && s.Has("MAX_DAMAGE"))
            {
                d.HasSusp = true;
                d.SuspMinVelocity = s.GetFloat("MIN_VELOCITY", 0f);
                d.SuspGain = s.GetFloat("GAIN", 0f);
                d.SuspMaxDamage = s.GetFloat("MAX_DAMAGE", 0f);
                if (d.SuspMaxDamage <= 0f || d.SuspGain <= 0f) d.HasSusp = false;
            }
            AcIniSection e = engine != null ? engine["DAMAGE"] : null;
            if (e != null)
            {
                d.HasRpmDamage = e.Has("RPM_THRESHOLD") && e.Has("RPM_DAMAGE_K");
                d.RpmThreshold = e.GetFloat("RPM_THRESHOLD", 0f);
                d.RpmDamageK = e.GetFloat("RPM_DAMAGE_K", 0f);
                if (d.RpmThreshold <= 0f) d.HasRpmDamage = false;
                d.HasTurboDamage = e.Has("TURBO_BOOST_THRESHOLD") && e.Has("TURBO_DAMAGE_K");
                d.TurboBoostThreshold = e.GetFloat("TURBO_BOOST_THRESHOLD", 0f);
                d.TurboDamageK = e.GetFloat("TURBO_DAMAGE_K", 0f);
            }
            AcIniSection p = car != null ? car["PIT_STOP"] : null;
            if (p != null)
            {
                d.HasPitStop = true;
                d.PitBodySecPer10 = p.GetFloat("BODY_REPAIR_TIME_SEC", 0f);
                d.PitEngineSecPer10 = p.GetFloat("ENGINE_REPAIR_TIME_SEC", 0f);
                d.PitSuspSecPer10 = p.GetFloat("SUSP_REPAIR_TIME_SEC", 0f);
            }
            return d;
        }
    }

    /// <summary>
    /// One car's damage state (DAMAGE_DESIGN 1.2). Held by AcCarPhysics (every rig; remote rigs are written by N through <see cref="ApplyWire"/>).
    /// Zones in km/h of impact (raw, the multiplier does not scale them - AC's visual damage is separate from the mechanical scale), steer-rod
    /// offsets in m (the multiplier scales each increment), engine life 0..<see cref="LifeNew"/> (the multiplier scales the loss).
    /// <see cref="Version"/> moves when a quantised value changes (ZoneByte, rod level u4, engine % u7, dead, retired): consumers re-apply only
    /// then. <see cref="MechVersion"/> moves on every float change of the zones / rods (P's aero and toe refresh).
    /// </summary>
    public sealed class AcCarDamage
    {
        /// <summary>Zone cap (km/h, // ours: the largest authored threshold is 220 (VISUAL_OBJECT FULL_SPEED), 255 keeps every visual reachable,
        /// bounds the aero extrapolation and fits a u8 on the wire). New engine life (// ours BELIEF from CSP engineLifeLeft).</summary>
        public const float ZoneCap = 255f, LifeNew = 1000f;
        /// <summary>Accumulation rule (risk R2), // ours: SUM.</summary>
        public static AcDamageAccumulate Accumulate = AcDamageAccumulate.Sum;

        public readonly float[] Zone = new float[5];
        /// <summary>Steer-rod deflection per corner (m, LF RF LR RR), >= 0 = toe-out magnitude.</summary>
        public readonly float[] RodOffset = new float[4];
        public float EngineLife = LifeNew;
        public bool EngineDead;
        /// <summary>Retired from the race (R sets it with AcStanding.Retired; travels in the wire pair d1 bit 31). Not cleared by Repair.</summary>
        public bool Retired;
        /// <summary>= AcCarPhysics.DamageRate (0..1), set by the physics every FixedUpdate; 0 = off (nothing accumulates).</summary>
        public float Multiplier;
        public int Version { get; private set; }
        /// <summary>Moves on every zone / rod float change (impact, repair, wire) - P recomputes the aero factors and the toe then.</summary>
        public int MechVersion { get; private set; }

        public readonly AcDamageSpec Spec;
        public readonly AcDamageRules Rules;

        private AcDamageChange _changes;
        private float _pendingKmh;
        private int _enginePctQ;      // last engine % u7 published (Version)
        private bool _smoking;

        /// <summary><paramref name="spec"/> null = no mechanical keys (rod / engine damage off); <paramref name="rules"/> null = <see cref="AcDamageRules.Current"/>.</summary>
        public AcCarDamage(AcDamageSpec spec, AcDamageRules rules)
        {
            Spec = spec ?? new AcDamageSpec();
            Rules = rules ?? AcDamageRules.Current;
        }

        /// <summary>True when the multiplier is above 0 (damage on).</summary>
        public bool On { get { return Multiplier > 0f; } }

        /// <summary>All zero (no zone, rod or engine damage).</summary>
        public bool Pristine
        {
            get
            {
                for (int i = 0; i < 5; i++) if (Zone[i] > 0f) return false;
                for (int i = 0; i < 4; i++) if (RodOffset[i] > 0f) return false;
                return EngineLife >= LifeNew && !EngineDead;
            }
        }

        /// <summary>One counted hit (DAMAGE_DESIGN 1.3 step 8 / 3.2): zone += kmh (raw, capped at 255; or MAX), and the steer rod of
        /// <paramref name="corner"/> (0..3, -1 none) += m * GAIN * (kmh - MIN_VELOCITY) up to MAX_DAMAGE when kmh > MIN_VELOCITY. Returns false
        /// (nothing changes) when the multiplier is 0 or the input is invalid.</summary>
        public bool AddImpact(int zone, float kmh, int corner)
        {
            float m = Multiplier;
            if (!(m > 0f) || zone < 0 || zone > 4 || !(kmh > 0f) || float.IsInfinity(kmh)) return false;
            if (m > 1f) m = 1f;
            byte before = ZoneByte(zone);
            float z = Zone[zone];
            float nz = Accumulate == AcDamageAccumulate.Max ? Math.Max(z, kmh) : z + kmh;
            if (nz > ZoneCap) nz = ZoneCap;
            bool changed = false;
            if (nz != z) { Zone[zone] = nz; changed = true; if (ZoneByte(zone) != before) { _changes |= AcDamageChange.Zones; Version++; } }
            if (corner >= 0 && corner < 4 && Spec.HasSusp && kmh > Spec.SuspMinVelocity)
            {
                int lvl = RodLevel(corner);
                float r = RodOffset[corner];
                float nr = Math.Min(Spec.SuspMaxDamage, r + m * Spec.SuspGain * (kmh - Spec.SuspMinVelocity));
                if (nr > r)
                {
                    RodOffset[corner] = nr; changed = true;
                    _changes |= AcDamageChange.Suspension;
                    if (RodLevel(corner) != lvl) Version++;
                }
            }
            if (kmh > _pendingKmh) _pendingKmh = kmh;
            if (changed) MechVersion++;
            return true;
        }

        /// <summary>Engine life per AcCarCore sub-step h (DAMAGE_DESIGN 3.3): life -= h * m * (RPM_DAMAGE_K * max(0, rpm - RPM_THRESHOLD) +
        /// TURBO_DAMAGE_K * max(0, boostTotal - TURBO_BOOST_THRESHOLD)). At 0: <see cref="EngineDead"/>. No-op at m 0 / dead / no keys.</summary>
        public void StepEngine(float h, float rpm, float boostTotal)
        {
            float m = Multiplier;
            if (!(m > 0f) || EngineDead) return;
            if (m > 1f) m = 1f;
            float rate = 0f;
            if (Spec.HasRpmDamage && rpm > Spec.RpmThreshold) rate += Spec.RpmDamageK * (rpm - Spec.RpmThreshold);
            if (Spec.HasTurboDamage && boostTotal > Spec.TurboBoostThreshold) rate += Spec.TurboDamageK * (boostTotal - Spec.TurboBoostThreshold);
            if (!(rate > 0f)) return;
            EngineLife -= h * m * rate;
            if (EngineLife <= 0f) { EngineLife = 0f; EngineDead = true; _changes |= AcDamageChange.EngineDead; Version++; }
            EngineQuantise();
        }

        private void EngineQuantise()
        {
            int q = EnginePctWire();
            if (q != _enginePctQ) { _enginePctQ = q; Version++; }
            bool smoke = EngineSmoking;
            if (smoke != _smoking) { _smoking = smoke; if (smoke) _changes |= AcDamageChange.EngineSmoke; Version++; }
        }

        /// <summary>floor(zone km/h) as a byte: what every visual and the HUD use, on the owner too (owner and remotes draw identically).</summary>
        public byte ZoneByte(int zone)
        {
            if (zone < 0 || zone > 4) return 0;
            float z = Zone[zone];
            return z <= 0f ? (byte)0 : z >= 255f ? (byte)255 : (byte)Math.Floor(z);
        }

        /// <summary>Steer-rod damage of a corner 0..1 (RodOffset / MAX_DAMAGE; 0 without suspension keys) - display and audio.</summary>
        public float SuspDamage01(int corner)
        {
            if (corner < 0 || corner > 3 || !Spec.HasSusp || Spec.SuspMaxDamage <= 0f) return 0f;
            float v = RodOffset[corner] / Spec.SuspMaxDamage;
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        /// <summary>Largest corner of <see cref="SuspDamage01"/>.</summary>
        public float MaxSuspDamage01 { get { float m = 0f; for (int i = 0; i < 4; i++) { float v = SuspDamage01(i); if (v > m) m = v; } return m; } }

        /// <summary>100 * saturate(max zone / MAX_DAMAGE_KMH) (DAMAGE_DESIGN 1.5, // ours % definition).</summary>
        public float BodyPct
        {
            get
            {
                float mx = 0f;
                for (int i = 0; i < 5; i++) if (Zone[i] > mx) mx = Zone[i];
                float v = mx / Math.Max(1f, Rules.MaxDamageKmh);
                return 100f * (v > 1f ? 1f : v);
            }
        }

        /// <summary>100 * max corner rod offset / MAX_DAMAGE.</summary>
        public float SuspPct { get { return 100f * MaxSuspDamage01; } }

        /// <summary>100 * (1 - life / LifeNew); 100 when dead.</summary>
        public float EnginePct
        {
            get
            {
                if (EngineDead) return 100f;
                float v = 100f * (1f - EngineLife / LifeNew);
                return v < 0f ? 0f : v > 100f ? 100f : v;
            }
        }

        /// <summary>Engine smoke wanted: EnginePct >= 100 - engine_smoke.ini ENGINE_LIFE (ENGINE_LIFE=25 read as "25 % life left", // ours;
        /// the alternative reading "25 of 1000" is one line here). False without engine_smoke.ini or while undamaged.</summary>
        public bool EngineSmoking
        {
            get
            {
                float t = Rules.SmokeEngineLife;
                if (!(t > 0f)) return false;
                int pct = EnginePctWire(); // fixer 2026-11-02: the quantised wire % on every client (design 1.2), so owner and remotes agree
                return pct > 0 && pct >= 100f - t;
            }
        }

        /// <summary>Pit seconds of a repair (DAMAGE_DESIGN 1.5): Body = BodyPct/10 * BODY_REPAIR_TIME_SEC; Suspension = body + SuspPct/10 *
        /// SUSP_REPAIR_TIME_SEC (it fixes the body too); Engine = EnginePct/10 * ENGINE_REPAIR_TIME_SEC; selected parts are SUMMED (// ours).</summary>
        public float RepairSeconds(AcRepair what)
        {
            float s = 0f;
            if ((what & (AcRepair.Body | AcRepair.Suspension)) != 0) s += BodyPct * 0.1f * Spec.PitBodySecPer10;
            if ((what & AcRepair.Suspension) != 0) s += SuspPct * 0.1f * Spec.PitSuspSecPer10;
            if ((what & AcRepair.Engine) != 0) s += EnginePct * 0.1f * Spec.PitEngineSecPer10;
            return s;
        }

        /// <summary>Clears the selected parts (Suspension also clears the body). Raises <see cref="AcDamageChange.Repaired"/> when anything changed.</summary>
        public void Repair(AcRepair what)
        {
            bool changed = false, mech = false;
            if ((what & (AcRepair.Body | AcRepair.Suspension)) != 0)
                for (int i = 0; i < 5; i++) if (Zone[i] != 0f) { Zone[i] = 0f; changed = mech = true; }
            if ((what & AcRepair.Suspension) != 0)
                for (int i = 0; i < 4; i++) if (RodOffset[i] != 0f) { RodOffset[i] = 0f; changed = mech = true; }
            if ((what & AcRepair.Engine) != 0 && (EngineLife != LifeNew || EngineDead)) { EngineLife = LifeNew; EngineDead = false; changed = true; }
            if (!changed) return;
            _enginePctQ = EnginePctWire(); _smoking = EngineSmoking;
            if (mech) MechVersion++;
            Version++;
            _changes |= AcDamageChange.Repaired;
        }

        /// <summary>Changes since the last call (and clears them).</summary>
        public AcDamageChange TakeChanges() { float k; return TakeChanges(out k); }

        /// <summary>Changes since the last call and the largest impact km/h counted since then (both cleared).</summary>
        public AcDamageChange TakeChanges(out float impactKmh)
        {
            AcDamageChange c = _changes; _changes = AcDamageChange.None;
            impactKmh = _pendingKmh; _pendingKmh = 0f;
            return c;
        }

        // ------------------------------------------------------------------ wire (DAMAGE_DESIGN 7, protocol 3)

        private int RodLevel(int corner) { return (int)Math.Round(15f * SuspDamage01(corner), MidpointRounding.AwayFromZero); }

        /// <summary>Engine damage % for the wire: ceil(EnginePct) 0..100; 100 only when dead (a living engine is sent as at most 99).</summary>
        private int EnginePctWire()
        {
            if (EngineDead) return 100;
            int q = (int)Math.Ceiling(EnginePct - 1e-4f);
            return q < 0 ? 0 : q > 99 ? 99 : q;
        }

        /// <summary>The damage pair: d0 = front u8 | rear u8 &lt;&lt; 8 | left u8 &lt;&lt; 16 | right u8 &lt;&lt; 24 (ZoneByte);
        /// d1 = center u8 | rod LF u4 &lt;&lt; 8 | RF &lt;&lt; 12 | LR &lt;&lt; 16 | RR &lt;&lt; 20 | engine % u7 &lt;&lt; 24 | retired &lt;&lt; 31.
        /// All zero = undamaged (DAMAGE 0 writes zeros).</summary>
        public void ToWire(out int d0, out int d1)
        {
            d0 = ZoneByte(0) | (ZoneByte(1) << 8) | (ZoneByte(2) << 16) | (ZoneByte(3) << 24);
            int v = ZoneByte(4);
            for (int i = 0; i < 4; i++) v |= (RodLevel(i) & 15) << (8 + 4 * i);
            v |= (EnginePctWire() & 127) << 24;
            if (Retired) v |= 1 << 31;
            d1 = v;
        }

        /// <summary>Applies a pair from the wire (remote cars, AI hand-over): zones = the bytes, rods = level / 15 * MAX_DAMAGE, engine life =
        /// (100 - %) * LifeNew / 100, dead at 100, retired bit. Returns true when anything changed (then <see cref="Version"/> moved and the
        /// change flags are set). Independent of the multiplier (remote damage shows whatever the owner sends).</summary>
        public bool ApplyWire(int d0, int d1)
        {
            // fixer 2026-11-02: per field - a value going DOWN is a repair (Repaired), only increases flag Zones / Suspension / EngineSmoke,
            // so a partial pit repair or BACK TO PITS of a retired car reaches the other clients as a repair, as on the owner
            bool changed = false, mech = false, repaired = false;
            for (int i = 0; i < 5; i++)
            {
                int b = i < 4 ? (d0 >> (8 * i)) & 255 : d1 & 255;
                if (Zone[i] != b)
                {
                    int before = ZoneByte(i);
                    if (b < before) repaired = true; else if (b > before) _changes |= AcDamageChange.Zones;
                    Zone[i] = b; mech = true; changed = true;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                int lvl = (d1 >> (8 + 4 * i)) & 15;
                float r = Spec.HasSusp ? lvl / 15f * Spec.SuspMaxDamage : 0f;
                if (RodOffset[i] != r)
                {
                    if (r < RodOffset[i]) repaired = true; else _changes |= AcDamageChange.Suspension;
                    RodOffset[i] = r; mech = true; changed = true;
                }
            }
            int pct = (d1 >> 24) & 127;
            if (pct > 100) pct = 100;
            bool dead = pct >= 100;
            float life = dead ? 0f : (100 - pct) * (LifeNew / 100f);
            if (dead != EngineDead) { if (!dead) repaired = true; EngineDead = dead; changed = true; if (dead) _changes |= AcDamageChange.EngineDead; }
            if (EngineLife != life) { if (life > EngineLife) repaired = true; EngineLife = life; changed = true; }
            bool ret = (d1 & (1 << 31)) != 0;
            if (ret != Retired) { Retired = ret; changed = true; }
            if (!changed) return false;
            bool smoke = EngineSmoking;
            if (smoke && !_smoking) _changes |= AcDamageChange.EngineSmoke;
            _smoking = smoke;
            _enginePctQ = EnginePctWire();
            if (mech) MechVersion++;
            if (repaired || (d0 == 0 && d1 == 0)) _changes |= AcDamageChange.Repaired;
            Version++;
            return true;
        }

        // ------------------------------------------------------------------ mechanical effects (P)

        /// <summary>Aero factors of one wing (DAMAGE_DESIGN 3.1): clDiv = 1 + m * sum ZONE_x_CL * Zone[x], cdMul = 1 + m * sum ZONE_x_CD * Zone[x],
        /// x in FRONT, REAR, LEFT, RIGHT (no CENTER key exists). 1 / 1 when undamaged or the multiplier is 0.</summary>
        public void AeroFactors(AcWingSpec w, out float clDiv, out float cdMul)
        {
            clDiv = 1f; cdMul = 1f;
            float m = Multiplier;
            if (w == null || !(m > 0f)) return;
            if (m > 1f) m = 1f;
            float f = Zone[0], r = Zone[1], l = Zone[2], rt = Zone[3];
            clDiv = 1f + m * (w.ZoneFrontCl * f + w.ZoneRearCl * r + w.ZoneLeftCl * l + w.ZoneRightCl * rt);
            cdMul = 1f + m * (w.ZoneFrontCd * f + w.ZoneRearCd * r + w.ZoneLeftCd * l + w.ZoneRightCd * rt);
            if (clDiv < 1e-3f) clDiv = 1e-3f; // ours guard (negative zone gains do not occur; keeps the division finite)
            if (cdMul < 0f) cdMul = 0f;
        }

        /// <summary>Damaged toe (deg, + = toe-out) of a corner from its setup toe length and rod offset (DAMAGE_DESIGN 3.2): the setup toe as
        /// today, plus |ToeDegFor(rod)| so damage always ADDS toe-out whatever the sign of the steering arm, clamped to +-12 deg (// ours: caps
        /// the atan artefact on short arms). With no rod damage the result is exactly ToeDegFor(toeLen) (iteration 1).</summary>
        public static float ToeDeg(AcAxleSpec axle, float toeLen, float rodOffset)
        {
            float t = axle.ToeDegFor(toeLen);
            if (!(rodOffset > 0f)) return t;
            t += Math.Abs(axle.ToeDegFor(rodOffset));
            return t > MaxToeDeg ? MaxToeDeg : t < -MaxToeDeg ? -MaxToeDeg : t;
        }

        /// <summary>Clamp of the damaged toe (deg). // ours</summary>
        public const float MaxToeDeg = 12f;

        /// <summary>Roof box: the central share of width / length and the height share above which a hit is CENTER (// ours 0.7).</summary>
        public const float RoofShare = 0.7f;

        /// <summary>Zone of a contact point (DAMAGE_DESIGN 1.3 step 7, // ours): normalised body coordinates (Unity axes: nx right, nz forward,
        /// both -1..1 over the damage box; ny 0 bottom .. 1 top). Upside down -> CENTER; roof box (ny > 0.7, |nx|, |nz| < 0.7) -> CENTER; else
        /// the dominant horizontal axis: FRONT / REAR by nz, RIGHT / LEFT by nx (Unity +x = car right).</summary>
        public static int PickZone(float nx, float ny, float nz, bool rollover)
        {
            if (rollover) return (int)AcDamageZone.Center;
            float ax = Math.Abs(nx), az = Math.Abs(nz);
            if (ny > RoofShare && ax < RoofShare && az < RoofShare) return (int)AcDamageZone.Center;
            if (az >= ax) return nz > 0f ? (int)AcDamageZone.Front : (int)AcDamageZone.Rear;
            return nx > 0f ? (int)AcDamageZone.Right : (int)AcDamageZone.Left;
        }
    }
}
