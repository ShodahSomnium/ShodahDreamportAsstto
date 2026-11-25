using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumAssetto;

// Critic probe for dmg_seams.md (read-only census of damage keys over every owned car).
static partial class Program
{
    static int Cmd_dmgcrit(List<string> a)
    {
        var keyCount = new SortedDictionary<string, int>();
        var vals = new SortedDictionary<string, SortedDictionary<string, int>>();
        int cars = 0;
        var noDamageIni = new List<string>(); var multiSusp = new List<string>(); var engNoDmg = new List<string>();
        foreach (var car in AcContent.Scan(_install).Cars)
        {
            if (!car.Owned) continue; cars++;
            var acd = AcdArchive.Open(Path.Combine(_install.CarsDir, car.Id));
            foreach (var f in new[] { "damage.ini", "engine.ini", "suspensions.ini", "drivetrain.ini", "car.ini", "aero.ini", "tyres.ini", "brakes.ini", "electronics.ini", "colliders.ini" })
            {
                string t = acd.Has(f) ? acd.ReadText(f) : null;
                if (t == null) { if (f == "damage.ini") noDamageIni.Add(car.Id); continue; }
                string sec = ""; int dmgSecs = 0;
                foreach (var raw in t.Split('\n'))
                {
                    string l = raw; int c = l.IndexOf(';'); if (c >= 0) l = l.Substring(0, c); l = l.Trim();
                    if (l.Length == 0) continue;
                    if (l.StartsWith("[")) { sec = Regex.Replace(l.Trim('[', ']'), @"_\d+$", "_n"); if (sec == "DAMAGE") dmgSecs++; continue; }
                    int e = l.IndexOf('='); if (e < 0) continue;
                    string k = l.Substring(0, e).Trim(); string v = l.Substring(e + 1).Trim();
                    bool rel = f == "damage.ini" || sec == "DAMAGE" || sec == "PIT_STOP" || k.StartsWith("ZONE_") || k.Contains("DAMAGE") || k.Contains("BLOW") || k.Contains("PUNCT") || k.Contains("EXPLO") || k.Contains("GLASS");
                    if (!rel) continue;
                    string key = f + " [" + sec + "] " + k;
                    int n; keyCount.TryGetValue(key, out n); keyCount[key] = n + 1;
                    SortedDictionary<string, int> vv; if (!vals.TryGetValue(key, out vv)) vals[key] = vv = new SortedDictionary<string, int>();
                    int m; vv.TryGetValue(v, out m); vv[v] = m + 1;
                }
                if (f == "suspensions.ini" && dmgSecs != 1) multiSusp.Add(car.Id + ":" + dmgSecs);
                if (f == "engine.ini" && dmgSecs == 0) engNoDmg.Add(car.Id);
            }
        }
        Console.WriteLine("owned cars " + cars);
        foreach (var kv in keyCount)
        {
            var vv = vals[kv.Key]; string s = ""; int i = 0;
            foreach (var x in vv) { if (i++ < 14) s += x.Key + "x" + x.Value + " "; }
            Console.WriteLine(kv.Value.ToString().PadLeft(3) + " " + kv.Key + "  {" + vv.Count + " vals} " + s);
        }
        Console.WriteLine("no damage.ini: " + string.Join(",", noDamageIni));
        Console.WriteLine("suspensions [DAMAGE] count != 1: " + string.Join(",", multiSusp));
        Console.WriteLine("engine.ini without [DAMAGE]: " + string.Join(",", engNoDmg));
        return 0;
    }
}
