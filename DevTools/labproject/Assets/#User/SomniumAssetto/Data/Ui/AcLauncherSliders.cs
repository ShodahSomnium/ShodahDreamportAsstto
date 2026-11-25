using System;
using System.Globalization;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One launcher slider: numeric range (data-slidermin / max / step) or a value list (data-slidervalues).</summary>
    public sealed class AcLauncherSlider
    {
        public bool Found;
        public int Min, Max, Step = 1;
        /// <summary>data-slidervalues ("0,1,2,3"), null when the slider is numeric.</summary>
        public int[] Values;

        public override string ToString()
        {
            if (!Found) return "missing";
            return Values != null ? "values " + string.Join(",", Values) : Min + ".." + Max + " step " + Step;
        }
    }

    /// <summary>
    /// [F, Front - Unity-free] The race lobby's slider ranges as the player's own launcher defines them (launcher/themes/default/index.html):
    /// race laps, practice / qualify minutes, AI strength, mechanical damage, fuel consumption, tyre wear. Each slider is found by the
    /// data-bind of the value shown above it (the ids are not unique in the file: "ailevel" and "damageslider" appear twice), never hard-coded
    /// in the world. Load once on a worker after the boot; read from anywhere (immutable after Load).
    /// </summary>
    public static class AcLauncherSliders
    {
        public static AcLauncherSlider RaceLaps = new AcLauncherSlider();
        public static AcLauncherSlider PracticeMinutes = new AcLauncherSlider();
        public static AcLauncherSlider QualifyMinutes = new AcLauncherSlider();
        public static AcLauncherSlider AiLevel = new AcLauncherSlider();
        public static AcLauncherSlider Damage = new AcLauncherSlider();
        public static AcLauncherSlider FuelRate = new AcLauncherSlider();
        public static AcLauncherSlider TyreWear = new AcLauncherSlider();
        public static volatile bool Loaded;
        public static string Source = "not read";

        /// <summary>Reads the install's launcher page; returns how many of the 7 sliders were found.</summary>
        public static int Load(AcInstall install)
        {
            int found = 0;
            try
            {
                if (install == null || string.IsNullOrEmpty(install.Root)) { Source = "no install"; return 0; }
                string path = Path.Combine(Path.Combine(Path.Combine(Path.Combine(install.Root, "launcher"), "themes"), "default"), "index.html");
                if (!File.Exists(path)) { Source = "launcher/themes/default/index.html missing"; return 0; }
                found = Parse(File.ReadAllText(path));
                Source = "launcher/themes/default/index.html (" + found + "/7 sliders)";
            }
            catch (Exception e) { Source = "launcher page unreadable (" + e.GetType().Name + ")"; }
            finally { Loaded = true; }
            return found;
        }

        /// <summary>Parses the page's text (harness / tests). Returns how many sliders were found.</summary>
        public static int Parse(string html)
        {
            RaceLaps = After(html, "launcher?SAVED/RACELAPS");
            PracticeMinutes = After(html, "launcher?SAVED/PRACTICEMINS");
            QualifyMinutes = After(html, "launcher?SAVED/QUALIFYMINS");
            AiLevel = After(html, "race?RACE/AI_LEVEL");
            Damage = After(html, "assists?DAMAGE");
            FuelRate = After(html, "assists?FUEL_RATE");
            TyreWear = After(html, "assists?TYRE_WEAR");
            int n = 0;
            if (RaceLaps.Found) n++;
            if (PracticeMinutes.Found) n++;
            if (QualifyMinutes.Found) n++;
            if (AiLevel.Found) n++;
            if (Damage.Found) n++;
            if (FuelRate.Found) n++;
            if (TyreWear.Found) n++;
            return n;
        }

        /// <summary>The first slider div (class "slider") after the element whose data-bind is exactly <paramref name="bind"/>.</summary>
        public static AcLauncherSlider After(string html, string bind)
        {
            var s = new AcLauncherSlider();
            if (string.IsNullOrEmpty(html)) return s;
            int at = html.IndexOf("data-bind=\"" + bind + "\"", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return s;
            int pos = at;
            for (int guard = 0; guard < 8; guard++)
            {
                int open = html.IndexOf("<div", pos, StringComparison.OrdinalIgnoreCase);
                if (open < 0) return s;
                int close = html.IndexOf('>', open);
                if (close < 0) return s;
                string tag = html.Substring(open, close - open);
                pos = close;
                string cls = Attr(tag, "class");
                if (cls == null || !HasWord(cls, "slider")) continue;
                string values = Attr(tag, "data-slidervalues");
                if (values != null)
                {
                    string[] parts = values.Split(',');
                    var v = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return s;
                    s.Values = v;
                    s.Found = v.Length > 0;
                    return s;
                }

                int min, max, step;
                if (!Int(tag, "data-slidermin", out min) || !Int(tag, "data-slidermax", out max)) return s;
                if (!Int(tag, "data-sliderstep", out step) || step <= 0) step = 1;
                s.Min = min; s.Max = max; s.Step = step;
                s.Found = max >= min;
                return s;
            }

            return s;
        }

        private static bool HasWord(string list, string word)
        {
            string[] parts = list.Split(' ', '\t', '\r', '\n');
            for (int i = 0; i < parts.Length; i++) if (string.Equals(parts[i], word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool Int(string tag, string name, out int v)
        {
            v = 0;
            string a = Attr(tag, name);
            return a != null && int.TryParse(a.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        private static string Attr(string tag, string name)
        {
            int i = 0;
            while (true)
            {
                i = tag.IndexOf(name + "=\"", i, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                // a whole attribute name (not the tail of a longer one)
                if (i > 0 && !char.IsWhiteSpace(tag[i - 1])) { i += name.Length; continue; }
                int start = i + name.Length + 2;
                int end = tag.IndexOf('"', start);
                return end < 0 ? null : tag.Substring(start, end - start);
            }
        }
    }
}
