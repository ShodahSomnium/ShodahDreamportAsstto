using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The race lobby's model: the rules the instance master edits on the tablet (AC's server_cfg / race.ini names and defaults:
    /// AcRules, research ac_physics_sound_rules.md 3.1-3.2), remembered on the master's computer (AcSettings "lobby.*"); what everyone else sees
    /// is the race command's rules (AcNetGame.RaceControl.Current) or the session's. The master's draft goes to R with every request
    /// (AcRace.RequestLoad(layout, AcLobby.Draft())) and is readable by R as <see cref="AcFront.LobbyRules"/> for START.
    /// </summary>
    public static class AcLobby
    {
        /// <summary>ALLOWED CARS choice: 0 any owned car, 1 only the master's car, 2 the master's car's class (ui_car.json "class"). // ours (no AC key: AC uses an entry list)</summary>
        public static int AllowedMode;
        public static int Version { get; private set; }
        private static AcRules _rules;
        private static bool _loaded;
        /// <summary>The draft was seeded from a running session with an ALLOWED CARS list: the list goes out as it is until the master edits a rule.</summary>
        private static bool _keepAllowed;

        public static readonly string[] AbsChoices = { "FORBIDDEN", "FACTORY", "ANY" };       // server_cfg ABS_ALLOWED / TC_ALLOWED 0 / 1 / 2
        public static readonly string[] JumpStartChoices = { "NONE", "PITS", "DRIVE-THROUGH" }; // JUMP_START_PENALTY 0 / 1 / 2 (the launcher's sliderFalseStart words)
        public static readonly string[] AllowedChoices = { "ANY CAR", "SAME CAR AS MASTER", "SAME CLASS AS MASTER" };

        // The slider ranges come from the player's own launcher page (Data/Ui AcLauncherSliders: launcher/themes/default/index.html, read at
        // boot - race laps, qualify minutes, AI strength, mechanical damage, fuel consumption and tyre wear values x 100 = server_cfg %).
        // Until / unless it is read, NEUTRAL fallbacks of ours (not the launcher's numbers: the world ships no game data).
        public static int LapsMin { get { AcLauncherSlider s = AcLauncherSliders.RaceLaps; return s.Found ? Math.Max(1, s.Min) : 1; } }          // ours fallback
        public static int LapsMax { get { AcLauncherSlider s = AcLauncherSliders.RaceLaps; return s.Found ? Math.Max(LapsMin, s.Max) : 50; } }  // ours fallback
        public static int TimeStep { get { AcLauncherSlider s = TimeSlider(); return s.Found ? Math.Max(1, s.Step) : 10; } }                  // ours fallback
        public static int TimeMax { get { AcLauncherSlider s = TimeSlider(); return s.Found ? Math.Max(TimeStep, s.Max) : 60; } }              // ours fallback
        public static int AiLevelMin { get { AcLauncherSlider s = AcLauncherSliders.AiLevel; return s.Found ? Mathf.Clamp(s.Min, 0, 100) : 50; } }   // ours fallback
        public static int AiLevelMax { get { AcLauncherSlider s = AcLauncherSliders.AiLevel; return s.Found ? Mathf.Clamp(s.Max, AiLevelMin, 100) : 100; } } // ours fallback (100 %)
        public static int DamageStep { get { AcLauncherSlider s = AcLauncherSliders.Damage; return s.Found ? Math.Max(1, s.Step) : 25; } }      // ours fallback
        public static int DamageMax { get { AcLauncherSlider s = AcLauncherSliders.Damage; return s.Found ? Math.Max(DamageStep, s.Max) : 100; } } // ours fallback (100 %)
        /// <summary>The remembered DAMAGE choice (iteration 2 key; the iteration-1 "lobby.damage" is ignored).</summary>
        public const string DamageKey = "lobby.dmg";
        /// <summary>Fuel consumption choices (server_cfg FUEL_RATE %): the launcher's values x 100; fallback off / normal (ours).</summary>
        public static int[] FuelRates { get { return Percent(AcLauncherSliders.FuelRate, ref _fuelFrom, ref _fuel, FallbackOffOn); } }
        /// <summary>Tyre wear choices (TYRE_WEAR_RATE %): the launcher's values x 100; fallback off / normal (ours).</summary>
        public static int[] TyreWearRates { get { return Percent(AcLauncherSliders.TyreWear, ref _wearFrom, ref _wear, FallbackOffOn); } }
        private static readonly int[] FallbackOffOn = { 0, 100 };      // ours: off / normal rate
        private static AcLauncherSlider _fuelFrom, _wearFrom;
        private static int[] _fuel, _wear;

        private static AcLauncherSlider TimeSlider()
        {
            AcLauncherSlider q = AcLauncherSliders.QualifyMinutes;
            return q.Found ? q : AcLauncherSliders.PracticeMinutes;
        }

        // the launcher's list x 100 (built once per loaded slider, no allocation per call)
        private static int[] Percent(AcLauncherSlider s, ref AcLauncherSlider from, ref int[] cache, int[] fallback)
        {
            if (s == null || !s.Found || s.Values == null || s.Values.Length == 0) return fallback;
            if (!ReferenceEquals(s, from) || cache == null)
            {
                cache = new int[s.Values.Length];
                for (int i = 0; i < cache.Length; i++) cache[i] = Mathf.Max(0, s.Values[i]) * 100;
                from = s;
            }

            return cache;
        }
        /// <summary>RACE_OVER_TIME steps (s): no launcher control exists (server_cfg value 180). // ours: 0..600 in 30 s steps</summary>
        public const int OverTimeStep = 30, OverTimeMax = 600;
        /// <summary>AI opponents at most: the network slot plan's 9 "Ac Net Ai" blocks x 2 cars. // ours (not an AC number)</summary>
        public const int AiMax = 18;

        /// <summary>Where the lobby's defaults came from (R's AcRace.DefaultRules: server/cfg/server_cfg.ini, or built-in).</summary>
        public static string DefaultsSource { get; private set; } = "built-in";

        /// <summary>The master's draft (never null).</summary>
        public static AcRules Rules
        {
            get { if (!_loaded) Load(); return _rules; }
        }

        /// <summary>A copy of the draft with the allowed cars filled from <see cref="AllowedMode"/> - what goes to R.</summary>
        public static AcRules Draft()
        {
            AcRules r = Rules.Clone();
            if (_keepAllowed) return r; // seeded from the running session: its allowed cars go out unchanged
            r.AllowedCars.Clear();
            AcContent c = AcGame.Current != null ? AcGame.Current.Content : null;
            AcCarEntry mine = c != null && !string.IsNullOrEmpty(AcLocal.CarId) ? c.Car(AcLocal.CarId) : null;
            if (AllowedMode == 1 && mine != null) r.AllowedCars.Add(mine.Id);
            else if (AllowedMode == 2 && mine != null && c != null)
            {
                foreach (AcCarEntry car in c.Cars)
                    if (car.Owned && string.Equals(car.Class, mine.Class, StringComparison.OrdinalIgnoreCase)) r.AllowedCars.Add(car.Id);
            }

            return r;
        }

        /// <summary>The rules the page shows: the draft for the master, the published / session rules for everyone else.</summary>
        public static AcRules Shown
        {
            get
            {
                if (AcMenu.SafeIsMaster()) return Rules;
                try
                {
                    AcRaceCommand cmd = AcNetGame.RaceControl != null ? AcNetGame.RaceControl.Current : null;
                    if (cmd != null && cmd.Rules != null) return cmd.Rules;
                }
                catch (Exception) { }
                return AcSession.Rules ?? Rules;
            }
        }

        /// <summary>The master's remembered choices ("lobby.*") over the install's defaults (R's AcRace.DefaultRules = server_cfg.ini values).</summary>
        public static void Load()
        {
            _loaded = true;
            AcRules r = null;
            try { r = AcRace.DefaultRules(); }
            catch (Exception e) { AcLog.WarnOnce("Front: AcRace.DefaultRules failed (" + e.GetType().Name + ") - the lobby starts from AcRules' own defaults"); }
            DefaultsSource = r != null ? "AcRace.DefaultRules (R: server/cfg/server_cfg.ini once the install is read)" : "AcRules defaults";
            if (r == null) r = new AcRules();
            r.AllowedCars.Clear();
            r.Session = (AcSessionType)Mathf.Clamp(AcSettings.Int("lobby.session", (int)r.Session), 1, 4);
            r.Laps = Mathf.Clamp(AcSettings.Int("lobby.laps", r.Laps), 0, LapsMax);
            r.TimeMinutes = Mathf.Clamp(AcSettings.Int("lobby.minutes", r.TimeMinutes), 0, TimeMax);
            r.AbsAllowed = Mathf.Clamp(AcSettings.Int("lobby.abs", r.AbsAllowed), 0, 2);
            r.TcAllowed = Mathf.Clamp(AcSettings.Int("lobby.tc", r.TcAllowed), 0, 2);
            r.StabilityAllowed = AcSettings.Flag("lobby.stability", r.StabilityAllowed);
            r.AutoClutchAllowed = AcSettings.Flag("lobby.autoclutch", r.AutoClutchAllowed);
            r.AutoShiftAllowed = AcSettings.Flag("lobby.autoshift", r.AutoShiftAllowed);
            r.TyreBlanketsAllowed = AcSettings.Flag("lobby.blankets", r.TyreBlanketsAllowed);
            r.AllowedTyresOut = Mathf.Clamp(AcSettings.Int("lobby.tyresout", r.AllowedTyresOut), -1, 4);
            r.JumpStartPenalty = Mathf.Clamp(AcSettings.Int("lobby.jumpstart", r.JumpStartPenalty), 0, 2);
            r.FuelRate = Mathf.Clamp(AcSettings.Int("lobby.fuelrate", r.FuelRate), 0, 500);
            r.TyreWearRate = Mathf.Clamp(AcSettings.Int("lobby.tyrewear", r.TyreWearRate), 0, 500);
            // iteration 2: DAMAGE under a NEW key - iteration 1 stored "lobby.damage" = 0 with every lobby edit (the row had no effect then), which
            // would keep damage off for those masters; the default is R's server_cfg.ini DAMAGE_MULTIPLIER (100 in the install)
            r.DamageMultiplier = Mathf.Clamp(AcSettings.Int(DamageKey, r.DamageMultiplier), 0, DamageMax);
            r.RaceOverTimeSeconds = Mathf.Clamp(AcSettings.Int("lobby.overtime", r.RaceOverTimeSeconds), 0, OverTimeMax);
            r.AiCount = Mathf.Clamp(AcSettings.Int("lobby.ai", r.AiCount), 0, AiMax);
            r.AiLevel = Mathf.Clamp(AcSettings.Int("lobby.ailevel", r.AiLevel), 0, 100);
            AllowedMode = Mathf.Clamp(AcSettings.Int("lobby.allowed", 0), 0, 2);
            _rules = r;
            _keepAllowed = false;
            Version++;
        }

        /// <summary>The draft becomes a copy of the running session's rules (a master by hand-over, or the boot re-seed while a track is
        /// loading / loaded): the master's next edit then changes only what they touched instead of sending this computer's remembered lobby
        /// over the session. Nothing is written to "lobby.*" here (the master's choices are remembered with their first edit, <see cref="Changed"/>).</summary>
        public static void SeedFrom(AcRules session)
        {
            if (session == null) return;
            _loaded = true;
            _rules = session.Clone();
            _keepAllowed = _rules.AllowedCars.Count > 0;
            if (!_keepAllowed) AllowedMode = 0;
            Version++;
        }

        /// <summary>The master changed a rule: remembered and announced (pages refresh on <see cref="Version"/>).</summary>
        public static void Changed()
        {
            AcRules r = Rules;
            AcSettings.SetInt("lobby.session", (int)r.Session);
            AcSettings.SetInt("lobby.laps", r.Laps);
            AcSettings.SetInt("lobby.minutes", r.TimeMinutes);
            AcSettings.SetInt("lobby.abs", r.AbsAllowed);
            AcSettings.SetInt("lobby.tc", r.TcAllowed);
            AcSettings.SetFlag("lobby.stability", r.StabilityAllowed);
            AcSettings.SetFlag("lobby.autoclutch", r.AutoClutchAllowed);
            AcSettings.SetFlag("lobby.autoshift", r.AutoShiftAllowed);
            AcSettings.SetFlag("lobby.blankets", r.TyreBlanketsAllowed);
            AcSettings.SetInt("lobby.tyresout", r.AllowedTyresOut);
            AcSettings.SetInt("lobby.jumpstart", r.JumpStartPenalty);
            AcSettings.SetInt("lobby.fuelrate", r.FuelRate);
            AcSettings.SetInt("lobby.tyrewear", r.TyreWearRate);
            AcSettings.SetInt(DamageKey, r.DamageMultiplier);
            AcSettings.SetInt("lobby.overtime", r.RaceOverTimeSeconds);
            AcSettings.SetInt("lobby.ai", r.AiCount);
            AcSettings.SetInt("lobby.ailevel", r.AiLevel);
            AcSettings.SetInt("lobby.allowed", AllowedMode);
            _keepAllowed = false; // ALLOWED CARS follows the master's choice again (Draft fills it)
            Version++;
        }

        /// <summary>The session line ("RACE  5 LAPS" / "PRACTICE  10 min").</summary>
        public static string SessionLine(AcRules r)
        {
            if (r == null) return "";
            string s = AcLocale.Session((int)r.Session);
            if (r.Session == AcSessionType.Race) return s + "   " + (r.Laps > 0 ? r.Laps + " " + AcLocale.S("Laps", "LAPS") : r.TimeMinutes + " min");
            if (r.Session == AcSessionType.Hotlap) return s;
            return s + "   " + (r.TimeMinutes > 0 ? r.TimeMinutes + " min" : "no time limit");
        }

        /// <summary>The players for the lobby list: the other Dreamport players (N's records), then the master's AI drivers (N's AiPlayers).</summary>
        public static void Players(List<AcNetPlayer> into)
        {
            into.Clear();
            try
            {
                IReadOnlyList<AcNetPlayer> l = AcNetGame.Players;
                if (l != null) for (int i = 0; i < l.Count; i++) if (l[i] != null && !l[i].IsAi) into.Add(l[i]);
            }
            catch (Exception e) { AcLog.WarnOnce("Front: reading AcNetGame.Players failed (" + e.GetType().Name + ")"); }
            try
            {
                IReadOnlyList<AcNetPlayer> ai = AcNetGame.AiPlayers;
                if (ai != null) for (int i = 0; i < ai.Count; i++) if (ai[i] != null) into.Add(ai[i]);
            }
            catch (Exception e) { AcLog.WarnOnce("Front: reading AcNetGame.AiPlayers failed (" + e.GetType().Name + ")"); }
        }

        /// <summary>A number that changes when any rule of <paramref name="r"/> changes (no allocation; pages refresh on it).</summary>
        public static int Sig(AcRules r)
        {
            if (r == null) return 0;
            int s = (int)r.Session;
            s = s * 31 + r.Laps; s = s * 31 + r.TimeMinutes; s = s * 31 + r.AbsAllowed; s = s * 31 + r.TcAllowed;
            s = s * 31 + (r.StabilityAllowed ? 1 : 0) + (r.AutoClutchAllowed ? 2 : 0) + (r.AutoShiftAllowed ? 4 : 0) + (r.TyreBlanketsAllowed ? 8 : 0);
            s = s * 31 + r.AllowedTyresOut; s = s * 31 + r.JumpStartPenalty; s = s * 31 + r.FuelRate; s = s * 31 + r.TyreWearRate;
            s = s * 31 + r.DamageMultiplier; s = s * 31 + r.RaceOverTimeSeconds; s = s * 31 + r.AiCount; s = s * 31 + r.AiLevel;
            int n = r.AllowedCars != null ? r.AllowedCars.Count : 0;
            s = s * 31 + n;
            if (n > 0 && r.AllowedCars[0] != null) s = s * 31 + r.AllowedCars[0].GetHashCode();
            return s;
        }

        /// <summary>The ALLOWED CARS words for <paramref name="r"/>: the master's own choice; everyone else reads the published list.</summary>
        public static string AllowedText(AcRules r, bool master)
        {
            if (master && !_keepAllowed) return AllowedChoices[Mathf.Clamp(AllowedMode, 0, 2)];
            if (master) r = Rules; // a seeded draft shows the session's list
            int n = r != null && r.AllowedCars != null ? r.AllowedCars.Count : 0;
            if (n == 0) return AllowedChoices[0];
            AcContent c = AcGame.Current != null ? AcGame.Current.Content : null;
            AcCarEntry first = c != null ? c.Car(r.AllowedCars[0]) : null;
            if (n == 1) return first != null ? first.Name.ToUpperInvariant() : r.AllowedCars[0];
            return n + " CARS" + (first != null && !string.IsNullOrEmpty(first.Class) ? " (" + first.Class.ToUpperInvariant() + ")" : "");
        }
    }
}
