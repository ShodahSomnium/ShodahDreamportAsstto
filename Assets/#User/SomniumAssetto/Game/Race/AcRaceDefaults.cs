using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] The race defaults read from the player's own install (Unity-free): the dedicated-server template
    /// <c>server/cfg/server_cfg.ini</c> ([SERVER] RACE_OVER_TIME, ALLOWED_TYRES_OUT, ABS_ALLOWED, TC_ALLOWED, STABILITY_ALLOWED,
    /// TYRE_BLANKETS_ALLOWED, FUEL_RATE, TYRE_WEAR_RATE; [QUALIFY] TIME; [RACE] LAPS) and <c>system/cfg/assetto_corsa.ini</c>
    /// ([RACE] OVER_TIME_MULT=1.2). Values verified in the install 2026-10-31 (research ac_physics_sound_rules.md 3.1-3.2). A missing file or
    /// key keeps the AcRules defaults (the same numbers). Keys AC has no value for stay ours (see the constants).
    /// </summary>
    public sealed class AcRaceDefaults
    {
        /// <summary>system/cfg/assetto_corsa.ini [RACE] OVER_TIME_MULT (1.2 in the install): a timed race closes at time x this when the
        /// leader has not finished by then [BELIEF on the exact meaning].</summary>
        public float OverTimeMult = 1.2f;
        /// <summary>QUALIFY_MAX_WAIT_PERC: after the qualify time is up, a lap in progress may still finish within this % of the session's
        /// fastest lap. Read from the install's server preset <c>server/presets/SERVER_00/server_cfg.ini</c> (120 there; the template
        /// server/cfg/server_cfg.ini has no such key). 120 when neither file has it.</summary>
        public int QualifyMaxWaitPerc = 120;
        /// <summary>START_RULE of the server preset (0 = the car is locked until the green, 1 = teleport to the pits, 2 = drive-through; the
        /// preset has 0). Mapped onto the lobby default <see cref="AcRules.JumpStartPenalty"/> (same numbering): 0 = locked, so no jump start
        /// is possible - exactly AC's multiplayer default. -1 = not in the install (then AcRules keeps 2, the career events' value).</summary>
        public int StartRule = -1;
        /// <summary>[PRACTICE] TIME of the server files (10 min). Read and logged only: our practice is open-ended between sessions (the
        /// instance has no session rotation); a timed practice is START with Session Practice and TIME.</summary>
        public int PracticeMinutes = 10;
        /// <summary>RESULT_SCREEN_TIME (acServer key, no value in the install): seconds the results show before practice resumes. // ours</summary>
        public int ResultScreenSeconds = 30; // ours
        /// <summary>Seconds between the master's START and the first red light (cars placed on the grid, players seated). The server's
        /// [RACE] WAIT_TIME=60 is a lobby wait for remote players; ours is shorter because everyone is already on the track. // ours</summary>
        public int GridSeconds = 10; // ours
        /// <summary>DAMAGE_MULTIPLIER when server_cfg.ini or its key is missing: 100 // ours (= the AC dedicated-server default, DAMAGE_DESIGN 1.1).</summary>
        public const int DefaultDamageMultiplier = 100; // ours
        /// <summary>Where the values came from (log).</summary>
        public string Source = "built-in";

        private readonly AcRules _rules = new AcRules();

        /// <summary>A fresh copy of the lobby defaults (AC's server_cfg names and values).</summary>
        public AcRules Rules() { return _rules.Clone(); }

        /// <summary>Reads the install's files (never throws; missing pieces keep the defaults).</summary>
        public static AcRaceDefaults Load(string installRoot)
        {
            var d = new AcRaceDefaults();
            if (string.IsNullOrEmpty(installRoot)) return d;
            d._rules.DamageMultiplier = DefaultDamageMultiplier;     // an install without server_cfg.ini / the key
            try
            {
                string server = Path.Combine(Path.Combine(Path.Combine(installRoot, "server"), "cfg"), "server_cfg.ini");
                string ac = Path.Combine(Path.Combine(Path.Combine(installRoot, "system"), "cfg"), "assetto_corsa.ini");
                string src = "";
                if (File.Exists(server))
                {
                    AcIni s = AcIni.Load(server);
                    AcRules r = d._rules;
                    r.RaceOverTimeSeconds = s.GetInt("SERVER", "RACE_OVER_TIME", r.RaceOverTimeSeconds);
                    r.AllowedTyresOut = s.GetInt("SERVER", "ALLOWED_TYRES_OUT", r.AllowedTyresOut);
                    r.AbsAllowed = s.GetInt("SERVER", "ABS_ALLOWED", r.AbsAllowed);
                    r.TcAllowed = s.GetInt("SERVER", "TC_ALLOWED", r.TcAllowed);
                    r.StabilityAllowed = s.GetInt("SERVER", "STABILITY_ALLOWED", r.StabilityAllowed ? 1 : 0) != 0;
                    r.TyreBlanketsAllowed = s.GetInt("SERVER", "TYRE_BLANKETS_ALLOWED", r.TyreBlanketsAllowed ? 1 : 0) != 0;
                    r.FuelRate = s.GetInt("SERVER", "FUEL_RATE", r.FuelRate);
                    r.TyreWearRate = s.GetInt("SERVER", "TYRE_WEAR_RATE", r.TyreWearRate);
                    // AUTOCLUTCH_ALLOWED=0 in the template, but VR players have no clutch pedal: AcRules keeps 1 (ours, documented there).
                    // DAMAGE_MULTIPLIER=100 in the template (iteration 2, DAMAGE_DESIGN 1.1 / 1.4): the lobby default; 0 = off = iteration 1
                    r.DamageMultiplier = Math.Max(0, Math.Min(100, s.GetInt("SERVER", "DAMAGE_MULTIPLIER", r.DamageMultiplier)));
                    r.Laps = s.GetInt("RACE", "LAPS", r.Laps);
                    r.TimeMinutes = s.GetInt("QUALIFY", "TIME", r.TimeMinutes);
                    d.PracticeMinutes = s.GetInt("PRACTICE", "TIME", d.PracticeMinutes);
                    d.QualifyMaxWaitPerc = s.GetInt("SERVER", "QUALIFY_MAX_WAIT_PERC", d.QualifyMaxWaitPerc);
                    d.StartRule = s.GetInt("SERVER", "START_RULE", d.StartRule);
                    src = "server/cfg/server_cfg.ini";
                }
                // the server manager's preset holds the keys the template lacks (QUALIFY_MAX_WAIT_PERC=120, START_RULE=0); the template wins
                // where both have a key
                string preset = Path.Combine(Path.Combine(Path.Combine(Path.Combine(installRoot, "server"), "presets"), "SERVER_00"), "server_cfg.ini");
                if (File.Exists(preset))
                {
                    AcIni p = AcIni.Load(preset);
                    bool tmpl = File.Exists(server);
                    AcIni s = tmpl ? AcIni.Load(server) : null;
                    if (s == null || s.Get("SERVER", "QUALIFY_MAX_WAIT_PERC", null) == null) d.QualifyMaxWaitPerc = p.GetInt("SERVER", "QUALIFY_MAX_WAIT_PERC", d.QualifyMaxWaitPerc);
                    if (s == null || s.Get("SERVER", "START_RULE", null) == null) d.StartRule = p.GetInt("SERVER", "START_RULE", d.StartRule);
                    src += (src.Length > 0 ? " + " : "") + "server/presets/SERVER_00/server_cfg.ini";
                }
                if (d.QualifyMaxWaitPerc < 0 || d.QualifyMaxWaitPerc > 1000) d.QualifyMaxWaitPerc = 120;
                if (d.StartRule >= 0 && d.StartRule <= 2) d._rules.JumpStartPenalty = d.StartRule;
                if (File.Exists(ac))
                {
                    AcIni a = AcIni.Load(ac);
                    float m = a.GetFloat("RACE", "OVER_TIME_MULT", d.OverTimeMult);
                    if (m >= 1f && m <= 5f) d.OverTimeMult = m;
                    src += (src.Length > 0 ? " + " : "") + "system/cfg/assetto_corsa.ini";
                }
                if (src.Length > 0) d.Source = src;
            }
            catch (Exception e)
            {
                d.Source = "built-in (" + e.Message + ")";
            }
            return d;
        }

        public override string ToString()
        {
            return "laps " + _rules.Laps + ", qualify " + _rules.TimeMinutes + " min, RACE_OVER_TIME " + _rules.RaceOverTimeSeconds + " s, ALLOWED_TYRES_OUT "
                + _rules.AllowedTyresOut + ", ABS " + _rules.AbsAllowed + ", TC " + _rules.TcAllowed + ", OVER_TIME_MULT " + OverTimeMult.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + ", QUALIFY_MAX_WAIT_PERC " + QualifyMaxWaitPerc + ", START_RULE " + (StartRule >= 0 ? StartRule + " -> jump start penalty " + _rules.JumpStartPenalty : "none (jump start penalty " + _rules.JumpStartPenalty + ")")
                + ", DAMAGE_MULTIPLIER " + _rules.DamageMultiplier
                + ", [PRACTICE] TIME " + PracticeMinutes + " (practice stays open-ended)"
                + " (" + Source + ")";
        }
    }
}
