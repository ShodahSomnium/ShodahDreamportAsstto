using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Kunos' own lap times for a car on a layout, from the install's special events (<c>content/specialevents/*/event.ini</c>): a hotlap event
    /// (<c>[SESSION_0] TYPE=4</c>) with <c>[RACE] TRACK / CONFIG_TRACK / MODEL</c> and <c>[CONDITION_n] TYPE=TIME OBJECTIVE=ms</c> (bronze / silver /
    /// gold; e.g. SPECIAL_EVENT_53 ks_toyota_gt86 at magione 95000 / 93000 / 92000) [VERIFIED file]. A human target, not an AI time - the only
    /// external lap reference in the files (fast_lane.ai LapTime is 0 everywhere). Used by the harness (<c>airef</c>) and the lab (<c>ai-magione</c>).
    /// Unity-free, never throws.
    /// </summary>
    public sealed class AcAiReference
    {
        public string EventDir = "", EventName = "", Track = "", Layout = "", Car = "";
        /// <summary>The time objectives (ms), easiest first as in the file (bronze, silver, gold).</summary>
        public readonly List<int> Objectives = new List<int>();
        /// <summary>The hardest objective (ms) = gold.</summary>
        public int GoldMs { get { int b = int.MaxValue; for (int i = 0; i < Objectives.Count; i++) b = Math.Min(b, Objectives[i]); return b == int.MaxValue ? 0 : b; } }

        /// <summary>Every hotlap special event with time objectives in the install.</summary>
        public static List<AcAiReference> All(string installRoot)
        {
            var list = new List<AcAiReference>();
            try
            {
                string dir = Path.Combine(Path.Combine(installRoot ?? "", "content"), "specialevents");
                if (!Directory.Exists(dir)) return list;
                string[] evs = Directory.GetDirectories(dir);
                Array.Sort(evs, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < evs.Length; i++)
                {
                    string ini = Path.Combine(evs[i], "event.ini");
                    if (!File.Exists(ini)) continue;
                    AcIni e = AcIni.Load(ini);
                    if (e.GetInt("SESSION_0", "TYPE", 0) != 4) continue; // hotlap
                    var r = new AcAiReference
                    {
                        EventDir = Path.GetFileName(evs[i]), EventName = e.Get("EVENT", "NAME", ""),
                        Track = e.Get("RACE", "TRACK", "").Trim(), Layout = e.Get("RACE", "CONFIG_TRACK", "").Trim(), Car = e.Get("RACE", "MODEL", "").Trim()
                    };
                    for (int k = 0; k < 8; k++)
                    {
                        string s = "CONDITION_" + k;
                        if (!e.Has(s)) continue;
                        if (!string.Equals(e.Get(s, "TYPE", ""), "TIME", StringComparison.OrdinalIgnoreCase)) continue;
                        int ms = e.GetInt(s, "OBJECTIVE", 0);
                        if (ms > 0) r.Objectives.Add(ms);
                    }
                    if (r.Objectives.Count > 0 && r.Track.Length > 0 && r.Car.Length > 0) list.Add(r);
                }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>The hotlap event for this car on this layout (layout "" = the track's only layout), or null.</summary>
        public static AcAiReference Find(string installRoot, string track, string layout, string car)
        {
            List<AcAiReference> all = All(installRoot);
            for (int i = 0; i < all.Count; i++)
            {
                AcAiReference r = all[i];
                if (string.Equals(r.Track, track, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Layout, layout ?? "", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Car, car, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        public override string ToString()
        {
            return EventDir + " \"" + EventName + "\": " + Car + " at " + Track + (Layout.Length > 0 ? "/" + Layout : "") + ", objectives " + string.Join("/", Objectives.ConvertAll(Ms).ToArray());
        }

        static string Ms(int ms) { return (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00") + "." + (ms % 1000).ToString("000"); }
    }
}
