using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front - Unity-free] The game's own words for the tablet: system/locales/en.ini ([STRINGS]: PRACTICE, QUALIFY, RACE, Laps, BEST, LAST,
    /// GAP, ...), system/locales/launcher/en.ini ([STRINGS]: ABS, TRACTION_CONTROL, AUTOMATIC_GEARS, ...) and system/locales/setup/en.ini
    /// ([SETUP]: FRONT_BIAS = Brake Bias, PRESSURE_LF = Pressure LF, FUEL ...). Every lookup has the English fallback the caller passes, so a
    /// missing file or key never shows a raw key. Load once on a worker (AcFront does it after the boot), read from anywhere.
    /// </summary>
    public static class AcLocale
    {
        static readonly Dictionary<string, string> Game = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> Launcher = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> SetupWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new object();

        /// <summary>Keys read (game + launcher + setup); 0 before <see cref="Load"/> or when the files are missing.</summary>
        public static int Count { get; private set; }
        public static string Root { get; private set; }

        /// <summary>Reads the three English locale files of <paramref name="install"/> (worker-safe). Returns the number of keys.</summary>
        public static int Load(AcInstall install)
        {
            if (install == null || string.IsNullOrEmpty(install.Root)) return 0;
            string loc = Path.Combine(install.SystemDir, "locales");
            var g = Read(Path.Combine(loc, "en.ini"), "STRINGS");
            var l = Read(Path.Combine(Path.Combine(loc, "launcher"), "en.ini"), "STRINGS");
            var s = Read(Path.Combine(Path.Combine(loc, "setup"), "en.ini"), "SETUP");
            lock (Gate)
            {
                Game.Clear(); Launcher.Clear(); SetupWords.Clear();
                foreach (var kv in g) Game[kv.Key] = kv.Value;
                foreach (var kv in l) Launcher[kv.Key] = kv.Value;
                foreach (var kv in s) SetupWords[kv.Key] = kv.Value;
                Count = Game.Count + Launcher.Count + SetupWords.Count;
                Root = install.Root;
            }
            return Count;
        }

        static Dictionary<string, string> Read(string path, string section)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string text = AcText.ReadFile(path);
                if (text == null) return d;
                // A plain key=value reader (not AcIni: values here may hold ';' and "//", which AcIni treats as comments).
                bool inSection = false;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim().TrimStart('﻿');
                    if (line.Length == 0) continue;
                    if (line[0] == '[') { inSection = string.Equals(line.Trim('[', ']').Trim(), section, StringComparison.OrdinalIgnoreCase); continue; }
                    if (!inSection) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                    if (key.Length > 0 && !d.ContainsKey(key)) d[key] = val;
                }
            }
            catch (Exception) { }
            return d;
        }

        /// <summary>A word of the game (system/locales/en.ini [STRINGS]).</summary>
        public static string S(string key, string fallback) { return Get(Game, key, fallback); }
        /// <summary>A word of the launcher (system/locales/launcher/en.ini).</summary>
        public static string L(string key, string fallback) { return Get(Launcher, key, fallback); }
        /// <summary>A setup row's label (system/locales/setup/en.ini [SETUP]).</summary>
        public static string Setup(string key, string fallback) { return Get(SetupWords, key, fallback); }

        static string Get(Dictionary<string, string> d, string key, string fallback)
        {
            if (key == null) return fallback;
            lock (Gate)
            {
                string v;
                return d.TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : fallback;
            }
        }

        /// <summary>The session type in the game's words (PRACTICE / QUALIFY / RACE / HOTLAP).</summary>
        public static string Session(int acType)
        {
            switch (acType)
            {
                case 1: return S("PRACTICE", "PRACTICE");
                case 2: return S("QUALIFY", "QUALIFY");
                case 3: return S("RACE", "RACE");
                case 4: return S("HOTLAP", "HOTLAP");
                default: return "SESSION";
            }
        }
    }

    /// <summary>One RGBA colour as the game writes it ("0,0,0,128").</summary>
    public struct AcRgba
    {
        public byte R, G, B, A;
        public AcRgba(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        public override string ToString() { return R + "," + G + "," + B + "," + A; }

        /// <summary>"255,0,0,255" / "128,8,8" (alpha 255) / "#bd0000"; false when it is not a colour.</summary>
        public static bool TryParse(string s, out AcRgba c)
        {
            c = new AcRgba(0, 0, 0, 255);
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s[0] == '#')
            {
                int v;
                if ((s.Length == 7 || s.Length == 9) && int.TryParse(s.Substring(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                {
                    c = new AcRgba((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
                    int a;
                    if (s.Length == 9 && int.TryParse(s.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) c.A = (byte)a;
                    return true;
                }
                return false;
            }
            string[] p = s.Split(',');
            if (p.Length < 3) return false;
            int[] n = new int[4] { 0, 0, 0, 255 };
            for (int i = 0; i < p.Length && i < 4; i++)
            {
                int x;
                if (!int.TryParse(p[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
                n[i] = Math.Max(0, Math.Min(255, x));
            }
            c = new AcRgba((byte)n[0], (byte)n[1], (byte)n[2], (byte)n[3]);
            return true;
        }
    }

    /// <summary>
    /// [F] The game's UI colours: content/gui/skin.ini (ksGui: [FORM] BACK 0,0,0,128, TITLE_BACK 255,0,0,255, [TEXT_INPUT] FOCUS_BACK 128,8,8,255,
    /// [CONTROL] BACK 128,128,128,128 ...) and the launcher's accent (launcher/themes/default/*.css, the colour used most: #bd0000 in the
    /// shipped theme). Values found are used; the shipped values are only the fallbacks (and the lab compares them).
    /// </summary>
    public sealed class AcGuiSkin
    {
        public AcRgba FormBack = new AcRgba(0, 0, 0, 128);
        public AcRgba FormBorder = new AcRgba(64, 64, 64, 128);
        public AcRgba FormFore = new AcRgba(255, 255, 255, 255);
        public AcRgba TitleBack = new AcRgba(255, 0, 0, 255);
        public AcRgba TitleFore = new AcRgba(255, 255, 255, 255);
        public AcRgba ControlBack = new AcRgba(128, 128, 128, 128);
        public AcRgba ControlBorder = new AcRgba(155, 155, 155, 255);
        public AcRgba InputFocus = new AcRgba(128, 8, 8, 255);
        public AcRgba InputUnfocus = new AcRgba(128, 128, 128, 128);
        /// <summary>The launcher theme's accent (#bd0000 in the shipped theme).</summary>
        public AcRgba Accent = new AcRgba(0xbd, 0, 0, 255);
        /// <summary>What was read: "skin.ini" / "css" flags for the log line.</summary>
        public bool SkinRead, AccentRead;
        public int AccentUses;

        /// <summary>Reads skin.ini and the launcher css of <paramref name="install"/> (worker-safe, never throws).</summary>
        public static AcGuiSkin Load(AcInstall install)
        {
            var s = new AcGuiSkin();
            if (install == null || string.IsNullOrEmpty(install.Root)) return s;
            try
            {
                AcIni ini = AcIni.Load(Path.Combine(Path.Combine(install.Content, "gui"), "skin.ini"));
                if (ini != null)
                {
                    s.SkinRead = true;
                    Read(ini, "FORM", "BACK", ref s.FormBack);
                    Read(ini, "FORM", "BORDER", ref s.FormBorder);
                    Read(ini, "FORM", "FORE", ref s.FormFore);
                    Read(ini, "FORM", "TITLE_BACK", ref s.TitleBack);
                    Read(ini, "FORM", "TITLE_FORE", ref s.TitleFore);
                    Read(ini, "CONTROL", "BACK", ref s.ControlBack);
                    Read(ini, "CONTROL", "BORDER", ref s.ControlBorder);
                    Read(ini, "TEXT_INPUT", "FOCUS_BACK", ref s.InputFocus);
                    Read(ini, "TEXT_INPUT", "UNFOCUS_BACK", ref s.InputUnfocus);
                }
            }
            catch (Exception) { }
            try
            {
                string themes = Path.Combine(Path.Combine(Path.Combine(install.Root, "launcher"), "themes"), "default");
                AcRgba accent;
                int uses;
                if (MostUsedColour(themes, out accent, out uses)) { s.Accent = accent; s.AccentRead = true; s.AccentUses = uses; }
            }
            catch (Exception) { }
            return s;
        }

        static void Read(AcIni ini, string section, string key, ref AcRgba into)
        {
            AcRgba c;
            if (AcRgba.TryParse(ini.Get(section, key), out c)) into = c;
        }

        /// <summary>The most used saturated "#rrggbb" in the theme's css files (black/white/greys skipped): the launcher's accent.</summary>
        public static bool MostUsedColour(string themeDir, out AcRgba colour, out int uses)
        {
            colour = new AcRgba(0, 0, 0, 255); uses = 0;
            if (!Directory.Exists(themeDir)) return false;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.GetFiles(themeDir, "*.css", SearchOption.AllDirectories))
            {
                string t = AcText.ReadFile(f);
                if (t == null) continue;
                for (int i = 0; i + 7 <= t.Length; i++)
                {
                    if (t[i] != '#') continue;
                    int j = i + 1;
                    while (j < t.Length && j - i <= 7 && Uri.IsHexDigit(t[j])) j++;
                    if (j - i != 7 || (j < t.Length && Uri.IsHexDigit(t[j]))) continue;
                    string hex = t.Substring(i, 7);
                    AcRgba c;
                    if (!AcRgba.TryParse(hex, out c)) continue;
                    int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                    if (max - min < 60) continue; // greys, black, white
                    int n; counts.TryGetValue(hex, out n); counts[hex] = n + 1;
                }
            }
            string best = null;
            foreach (var kv in counts) if (kv.Value > uses) { uses = kv.Value; best = kv.Key; }
            return best != null && AcRgba.TryParse(best, out colour);
        }
    }

    /// <summary>
    /// [F] The player's driving aids as AC keeps them: Documents\Assetto Corsa\cfg\assists.ini (the player's own last choice in the game),
    /// else the install's cfg\assists.ini (IDEAL_LINE=1 AUTO_BLIP=1 STABILITY_CONTROL=100 AUTO_SHIFTER=1 ABS=2 TRACTION_CONTROL=2 AUTO_CLUTCH=1
    /// DAMAGE=0 FUEL_RATE=0 TYRE_WEAR=1 TYRE_BLANKETS=0 SLIPSTREAM=1 in the shipped file). ABS / TC: 0 off, 1 factory, 2 on.
    /// </summary>
    public sealed class AcAssistsIni
    {
        public int Abs = 1, Tc = 1, Stability = 0;
        public bool AutoShifter = true, AutoClutch = true, AutoBlip = true, IdealLine, TyreBlankets, Slipstream = true;
        public int FuelRate = 1, TyreWear = 1, Damage;
        /// <summary>Where the values came from (path), or null when no file was found (the fields keep the defaults above).</summary>
        public string Source;

        public static AcAssistsIni Load(AcInstall install)
        {
            var a = new AcAssistsIni();
            var tries = new List<string>();
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs)) tries.Add(Path.Combine(Path.Combine(Path.Combine(docs, "Assetto Corsa"), "cfg"), "assists.ini"));
            }
            catch (Exception) { }
            if (install != null && !string.IsNullOrEmpty(install.Root)) tries.Add(Path.Combine(Path.Combine(install.Root, "cfg"), "assists.ini"));
            foreach (string p in tries)
            {
                AcIni ini = null;
                try { ini = AcIni.Load(p); } catch (Exception) { }
                if (ini == null || !ini.Has("ASSISTS")) continue;
                a.Source = p;
                a.Abs = Clamp(ini.GetInt("ASSISTS", "ABS", a.Abs), 0, 2);
                a.Tc = Clamp(ini.GetInt("ASSISTS", "TRACTION_CONTROL", a.Tc), 0, 2);
                a.Stability = Clamp(ini.GetInt("ASSISTS", "STABILITY_CONTROL", a.Stability), 0, 100);
                a.AutoShifter = ini.GetInt("ASSISTS", "AUTO_SHIFTER", 1) != 0;
                a.AutoClutch = ini.GetInt("ASSISTS", "AUTO_CLUTCH", 1) != 0;
                a.AutoBlip = ini.GetInt("ASSISTS", "AUTO_BLIP", 1) != 0;
                a.IdealLine = ini.GetInt("ASSISTS", "IDEAL_LINE", 0) != 0;
                a.TyreBlankets = ini.GetInt("ASSISTS", "TYRE_BLANKETS", 0) != 0;
                a.Slipstream = ini.GetInt("ASSISTS", "SLIPSTREAM", 1) != 0;
                a.FuelRate = ini.GetInt("ASSISTS", "FUEL_RATE", 1);
                a.TyreWear = ini.GetInt("ASSISTS", "TYRE_WEAR", 1);
                a.Damage = ini.GetInt("ASSISTS", "DAMAGE", 0);
                break;
            }
            return a;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        /// <summary>The ABS/TC value the master's rule allows (server_cfg ABS_ALLOWED / TC_ALLOWED: 0 forbidden, 1 factory only, 2 any).</summary>
        public static int Allowed(int wanted, int allowedRule)
        {
            if (allowedRule <= 0) return 0;
            if (allowedRule == 1) return wanted > 0 ? 1 : 0;
            return wanted;
        }
    }

    /// <summary>One adjustable row of a car's setup.ini (data.acd), as AC's setup screen shows it.</summary>
    public sealed class AcSetupRow
    {
        /// <summary>The section name (PRESSURE_LF, FRONT_BIAS, FUEL, CAMBER_LF, ...).</summary>
        public string Key;
        /// <summary>TAB= (TYRES, ALIGNMENT, GENERIC, SUSPENSION, ...).</summary>
        public string Tab;
        /// <summary>The label: system/locales/setup/en.ini [SETUP] KEY, else NAME= of the section.</summary>
        public string Label;
        public float Min, Max, Step;
        /// <summary>SHOW_CLICKS (0 value, 1 clicks, 2 both - AC's display mode).</summary>
        public int ShowClicks;
        /// <summary>The car's own value when the setup is untouched (read from car.ini / brakes.ini / tyres.ini where known; NaN otherwise).</summary>
        public float Default = float.NaN;
        public override string ToString() { return Key + " " + Min + ".." + Max + " step " + Step; }
    }

    /// <summary>
    /// [F] A car's setup rows (data.acd setup.ini: [KEY] TAB NAME MIN MAX STEP SHOW_CLICKS) with the car's own defaults where the physics files
    /// say them: FUEL = car.ini [FUEL] FUEL, FRONT_BIAS = brakes.ini [DATA] FRONT_SHARE x 100, BRAKE_POWER_MULT = 100, PRESSURE_xx = tyres.ini
    /// [FRONT|REAR] PRESSURE_STATIC of the default compound. Rows without MIN/MAX (gear RATIOS lists) are skipped. Worker-safe.
    /// </summary>
    public static class AcSetupRows
    {
        public static List<AcSetupRow> Read(AcdArchive acd)
        {
            var list = new List<AcSetupRow>();
            if (acd == null) return list;
            AcIni setup = acd.ReadIni("setup.ini");
            if (setup == null) return list;
            AcIni car = acd.ReadIniOrEmpty("car.ini"), brakes = acd.ReadIniOrEmpty("brakes.ini"), tyres = acd.ReadIniOrEmpty("tyres.ini");
            int compound = tyres.GetInt("COMPOUND_DEFAULT", "INDEX", 0);
            string front = compound > 0 ? "FRONT_" + compound : "FRONT", rear = compound > 0 ? "REAR_" + compound : "REAR";
            foreach (AcIniSection s in setup.Order)
            {
                if (s.Name.Length == 0 || !s.Has("MIN") || !s.Has("MAX") || s.Has("RATIOS")) continue;
                var it = new AcSetupRow
                {
                    Key = s.Name,
                    Tab = s.Get("TAB", "GENERIC"),
                    Min = s.GetFloat("MIN", 0f),
                    Max = s.GetFloat("MAX", 0f),
                    Step = s.GetFloat("STEP", 1f),
                    ShowClicks = s.GetInt("SHOW_CLICKS", 0),
                };
                if (it.Step <= 0f) it.Step = 1f;
                if (it.Max < it.Min) { float t = it.Max; it.Max = it.Min; it.Min = t; }
                it.Label = AcLocale.Setup(s.Name, s.Get("NAME", s.Name));
                string k = s.Name.ToUpperInvariant();
                if (k == "FUEL") it.Default = car.GetFloat("FUEL", "FUEL", float.NaN);
                else if (k == "FRONT_BIAS") { float f = brakes.GetFloat("DATA", "FRONT_SHARE", float.NaN); it.Default = float.IsNaN(f) ? f : (float)Math.Round(f * 100f); }
                else if (k == "BRAKE_POWER_MULT") it.Default = 100f;
                else if (k == "PRESSURE_LF" || k == "PRESSURE_RF") it.Default = tyres.GetFloat(tyres.Has(front) ? front : "FRONT", "PRESSURE_STATIC", float.NaN);
                else if (k == "PRESSURE_LR" || k == "PRESSURE_RR") it.Default = tyres.GetFloat(tyres.Has(rear) ? rear : "REAR", "PRESSURE_STATIC", float.NaN);
                if (!float.IsNaN(it.Default)) it.Default = Math.Max(it.Min, Math.Min(it.Max, it.Default));
                list.Add(it);
            }
            return list;
        }
    }

    /// <summary>[F] Text formats the tablet uses (AC's own: lap times m:ss.fff, gaps +s.fff, lengths in km).</summary>
    public static class AcUiFormat
    {
        /// <summary>AC's lap time: "1:23.456"; "--:--.---" for none (0 or negative).</summary>
        public static string LapTime(int ms)
        {
            if (ms <= 0) return "--:--.---";
            int m = ms / 60000, s = (ms / 1000) % 60, f = ms % 1000;
            return m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture) + "." + f.ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>A race time that may pass an hour: "1:02:03.456".</summary>
        public static string RaceTime(int ms)
        {
            if (ms <= 0) return "--:--.---";
            if (ms < 3600000) return LapTime(ms);
            int h = ms / 3600000;
            return h.ToString(CultureInfo.InvariantCulture) + ":" + LapTime(ms % 3600000).PadLeft(9, '0');
        }

        /// <summary>The gap column: "+1.234", "+1:02.345", "+2 LAPS" / "+1 LAP" ("" for the leader).</summary>
        public static string Gap(int gapMs, int lapsBehind, bool leader)
        {
            if (leader) return "";
            if (lapsBehind > 0) return "+" + lapsBehind + (lapsBehind == 1 ? " LAP" : " LAPS");
            if (gapMs <= 0) return "";
            if (gapMs < 60000) return "+" + (gapMs / 1000).ToString(CultureInfo.InvariantCulture) + "." + (gapMs % 1000).ToString("000", CultureInfo.InvariantCulture);
            return "+" + LapTime(gapMs);
        }

        /// <summary>"4.909 km" (or "3.05 mi" with <paramref name="miles"/>); "" when unknown.</summary>
        public static string Length(float metres, bool miles)
        {
            if (float.IsNaN(metres) || metres <= 0f) return "";
            return miles ? (metres / 1609.344f).ToString("0.00", CultureInfo.InvariantCulture) + " mi" : (metres / 1000f).ToString("0.000", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>A setup value as AC writes it (whole numbers without decimals).</summary>
        public static string Number(float v)
        {
            if (float.IsNaN(v)) return "-";
            return Math.Abs(v - Math.Round(v)) < 1e-4 ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>"Ferrari 458 Italia" + class "street" -> the card's second line ("Ferrari  -  street").</summary>
        public static string CarLine(string brand, string cls)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(brand)) sb.Append(brand);
            if (!string.IsNullOrEmpty(cls)) { if (sb.Length > 0) sb.Append("  -  "); sb.Append(cls); }
            return sb.ToString();
        }

        /// <summary>The specs line of ui_car.json as the launcher shows them ("570bhp  540Nm  1485kg  325+km/h  3.4s 0-100").</summary>
        public static string Specs(AcCarSpecs s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            Add(sb, s.BhpText); Add(sb, s.TorqueText); Add(sb, s.WeightText); Add(sb, s.TopSpeedText); Add(sb, s.AccelerationText); Add(sb, s.PwRatioText);
            return sb.ToString();
        }

        static void Add(StringBuilder sb, string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            if (sb.Length > 0) sb.Append("    ");
            sb.Append(t.Trim());
        }
    }

    /// <summary>[F] Paging for the tablet's grids (no scroll views: a VR ray drag on a ScrollRect is unproven in the client).</summary>
    public static class AcPaging
    {
        public static int Pages(int count, int perPage) { return perPage <= 0 ? 1 : Math.Max(1, (count + perPage - 1) / perPage); }
        public static int Clamp(int page, int count, int perPage) { int n = Pages(count, perPage); return page < 0 ? 0 : page >= n ? n - 1 : page; }
        /// <summary>The page that shows item <paramref name="index"/> (0 when not found).</summary>
        public static int PageOf(int index, int perPage) { return index < 0 || perPage <= 0 ? 0 : index / perPage; }
    }

    /// <summary>[F] The Drive page's car list: owned cars first-class, DLC stubs listed greyed; filtered by brand ("" = all).</summary>
    public static class AcCarFilter
    {
        /// <summary>Brands of the listed cars, sorted, each once ("" brands skipped).</summary>
        public static List<string> Brands(List<AcCarEntry> cars, bool includeStubs)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cars != null) foreach (var c in cars) if (c != null && !string.IsNullOrEmpty(c.Brand) && (includeStubs || c.Owned)) set.Add(c.Brand);
            return new List<string>(set);
        }

        /// <summary>The cars to list: owned ones of the brand (all brands for ""), then (with <paramref name="includeStubs"/>) the DLC stubs.
        /// <paramref name="allowed"/> non-empty = only those ids are selectable (others still listed, marked by the page).</summary>
        public static void Filter(List<AcCarEntry> cars, string brand, bool includeStubs, List<AcCarEntry> into)
        {
            into.Clear();
            if (cars == null) return;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var c in cars)
                {
                    if (c == null || (pass == 0) != c.Owned) continue;
                    if (pass == 1 && !includeStubs) continue;
                    if (!string.IsNullOrEmpty(brand) && !string.Equals(c.Brand, brand, StringComparison.OrdinalIgnoreCase)) continue;
                    into.Add(c);
                }
            }
        }

        /// <summary>Skins in AC's order: ui_skin.json priority (higher first), then folder name.</summary>
        public static List<AcSkinEntry> SkinsInOrder(AcCarEntry car)
        {
            var l = new List<AcSkinEntry>();
            if (car == null || car.Skins == null) return l;
            l.AddRange(car.Skins);
            l.Sort((a, b) =>
            {
                int c = b.Priority.CompareTo(a.Priority);
                return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });
            return l;
        }

        /// <summary>Whether the master's allowed-car list lets <paramref name="carId"/> race (empty list = any owned car).</summary>
        public static bool Allowed(List<string> allowed, string carId)
        {
            if (allowed == null || allowed.Count == 0) return true;
            for (int i = 0; i < allowed.Count; i++) if (string.Equals(allowed[i], carId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
