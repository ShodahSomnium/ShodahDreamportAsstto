namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] The install images the HUD draws, relative to the AC root (research ac_physics_sound_rules.md 4; sizes VERIFIED by the
    /// harness <c>hudart</c> probe). Names only - the pictures are read from the player's install at run time. Unity-free (the harness
    /// checks the list; Game/Hud/AcHudArt loads it).
    /// </summary>
    public static class AcHudArtList
    {
        // "Essentials" app (gear, speed, rpm bar, fuel)
        public const string RpmBar = "content/gui/essential/rpmBar.png";              // 132x23
        public const string FuelBar = "content/gui/essential/fuelBar.png";            // 81x25
        // "Player current time" app (lap times)
        public const string Best = "content/gui/PlayerCurrentTime/best.png";          // 26x31
        public const string Last = "content/gui/PlayerCurrentTime/last.png";          // 27x31
        public const string Up = "content/gui/PlayerCurrentTime/up.png";              // 18x17 green arrow pointing up (measured RGB 0,199,0): delta FASTER (AcHudView shows it when DeltaMs < 0)
        public const string Down = "content/gui/PlayerCurrentTime/down.png";          // 18x17 red arrow pointing down: delta SLOWER
        public const string WhiteLine = "content/gui/PlayerCurrentTime/white_line.png";   // 265x40 row background
        public const string Separator = "content/gui/PlayerCurrentTime/separator.png";    // 265x1
        public const string Infinity = "content/gui/PlayerCurrentTime/infinity_icon.png"; // 29x17: untimed session (session box, no clock)
        // "Session ranking" (standings tower)
        public const string CurrentLapBg = "content/gui/sessionRanking/currentLapBg.png"; // 476x26 (own row)
        // flags
        public const string FlagBlue = "content/gui/flags/blueFlag.png";
        public const string FlagYellow = "content/gui/flags/yellowFlag.png";
        public const string FlagFinish = "content/gui/flags/finish.png";
        public const string FlagPenalty = "content/gui/flags/penalty.png";
        // start lights, warnings
        public const string LightRed = "content/texture/texture_trafficlight_red.png";      // 74x74
        public const string LightGreen = "content/texture/texture_trafficlight_green.png";
        public const string LightOff = "content/texture/texture_trafficlight_off.png";
        public const string WrongWay = "content/texture/wrongway.png";                      // 128x128
        public const string FalseStart = "content/gui/drag/false_start.png";                // 708x33 drag bar with 5 red lights: shown with R's "FALSE START!"
        public const string MessageBar = "content/gui/systemMessage/bar.png";               // 708x1
        public const string FuelLow = "content/gui/fuelIndicator/fuelIndicator.png";
        public const string FuelOut = "content/gui/fuelIndicator/outOfFuel.png";         // tank empty (fuel gauge at 0)
        public const string Abs = "content/gui/electronicaids/abs.png";
        public const string Tc = "content/gui/electronicaids/tc.png";
        // the AC dial (bottom-centre rings): ink from 30 to 328 degrees clockwise from straight down (harness `arc`), rpm red zone from 297
        public const string RpmOn = "content/texture/rpm_on.png";                           // 156x156 outer ring, radius 58-78 px
        public const string RpmOff = "content/texture/rpm_off.png";
        public const string TurboOn = "content/texture/turbo_on.png";                       // 156x156 inner ring
        public const string TurboOff = "content/texture/turbo_off.png";
        public const string MiniFinish = "content/texture/mini_finish.png";                 // 32x32 (finished, standings)

        /// <summary>Every file above, in load order (each one is drawn somewhere: AcHudView). Not loaded: white_line_for_repeat, rightArrow,
        /// downArrow (other apps' art), blackFlag / whiteFlag (no disqualification or slow-car rule in the Dreamport's race control).</summary>
        public static readonly string[] Files =
        {
            RpmBar, FuelBar, Best, Last, Up, Down, WhiteLine, Separator, Infinity, CurrentLapBg,
            FlagBlue, FlagYellow, FlagFinish, FlagPenalty,
            LightRed, LightGreen, LightOff, WrongWay, FalseStart, MessageBar, FuelLow, FuelOut, Abs, Tc, RpmOn, RpmOff, TurboOn, TurboOff, MiniFinish,
        };

        // ---- damage displayer (iteration 2, DAMAGE_DESIGN 5): white-on-alpha 512x512 layers in content/texture/damage/ that stack into a
        // top-down car (acs.exe names these 18 as full paths; center.png is on disk but NOT referenced -> not drawn; flatspot_* are referenced
        // but the Dreamport has no flat spots -> not loaded). Drawn stacked, each tinted by its own damage (AcHudView.TickDamage).
        public const string DmgFront = "content/texture/damage/front.png";
        public const string DmgRear = "content/texture/damage/rear.png";
        public const string DmgLeft = "content/texture/damage/left.png";
        public const string DmgRight = "content/texture/damage/right.png";
        public const string DmgEngine = "content/texture/damage/engine.png";
        public const string DmgGearbox = "content/texture/damage/gearbox.png";
        public const string DmgSusFl = "content/texture/damage/sus_fl.png";
        public const string DmgSusFr = "content/texture/damage/sus_fr.png";
        public const string DmgSusRl = "content/texture/damage/sus_rl.png";
        public const string DmgSusRr = "content/texture/damage/sus_rr.png";
        public const string DmgTyreFl = "content/texture/damage/tyre_fl.png";
        public const string DmgTyreFr = "content/texture/damage/tyre_fr.png";
        public const string DmgTyreRl = "content/texture/damage/tyre_rl.png";
        public const string DmgTyreRr = "content/texture/damage/tyre_rr.png";
        /// <summary>Referenced by acs.exe, never drawn here (no flat-spot model) - listed so the probe can check the 18 paths.</summary>
        public static readonly string[] DamageFlatspots =
        {
            "content/texture/damage/flatspot_fl.png", "content/texture/damage/flatspot_fr.png", "content/texture/damage/flatspot_rl.png", "content/texture/damage/flatspot_rr.png",
        };

        /// <summary>The damage layers the HUD loads, in draw order (bottom to top): tyres and suspension under the body panels, engine and
        /// gearbox on top. Order matters only where layers overlap (they barely do: dmgart bboxes).</summary>
        public static readonly string[] DamageFiles =
        {
            DmgTyreFl, DmgTyreFr, DmgTyreRl, DmgTyreRr, DmgSusFl, DmgSusFr, DmgSusRl, DmgSusRr,
            DmgFront, DmgRear, DmgLeft, DmgRight, DmgGearbox, DmgEngine,
        };

        /// <summary>The part of every 512x512 damage layer that holds ink (x 176..333, y 94..419 top-down over all 19 files, harness dmgart),
        /// with a margin: x 160..352, y 80..432 -&gt; texture rect (x, y-from-bottom, w, h) = 160, 80, 192, 352. The HUD draws only this crop so
        /// the stacked car is not lost in empty margins. // ours margin</summary>
        public const float CropX = 160f, CropY = 80f, CropW = 192f, CropH = 352f, CropOf = 512f;

        /// <summary>Is <paramref name="rel"/> one of the damage layers (AcHudArt compresses those: 14 x 512x512).</summary>
        public static bool IsDamage(string rel)
        {
            for (int i = 0; i < DamageFiles.Length; i++) if (string.Equals(DamageFiles[i], rel, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Everything AcHudArt loads: <see cref="Files"/> then <see cref="DamageFiles"/>.</summary>
        public static readonly string[] All = Concat(Files, DamageFiles);

        private static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            System.Array.Copy(a, r, a.Length);
            System.Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
