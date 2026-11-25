using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumAssetto;

// Damage research probe (MECHANICAL topic; output -> DevTools/agents/research/dmg_mechanical.md). Read-only sweep over every owned car.
//   mechdmg [car]   per-car engine/turbo/susp/drivetrain/aero-zone damage values + PIT_STOP + key census
//   mechkeys        every section/key in every data.acd ini (except damage.ini) matching damage-ish words
static partial class Program
{
    static string MechF(AcIniSection s, string k) { return s == null ? "-" : (s.Get(k) ?? "-").Trim(); }
    static string MechN(float f) { return f.ToString(CultureInfo.InvariantCulture); }

    static int Cmd_mechdmg(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var keyCensus = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int cars = 0, turboCars = 0, turboOver = 0, rpmBelowLimiter = 0;
        Console.WriteLine("car | LIMITER RPM_THR RPM_K | turbos maxBoost wastegate TB_THR TB_K | suspF suspR MINV GAIN MAXD | RPM_WINDOW_K | wingZones(CL/CD) | PIT body/eng/susp");
        foreach (var car in c.Cars)
        {
            if (!car.Owned || car.IsDlcStub) continue;
            if (a.Count > 1 && car.Id != a[1]) continue;
            cars++;
            var acd = AcdArchive.Open(car.Dir);
            var eng = acd.ReadIniOrEmpty("engine.ini");
            var sus = acd.ReadIniOrEmpty("suspensions.ini");
            var drv = acd.ReadIniOrEmpty("drivetrain.ini");
            var aero = acd.ReadIniOrEmpty("aero.ini");
            var carIni = acd.ReadIniOrEmpty("car.ini");
            var files = new[] { "engine", "susp", "drivetrain", "car" };
            var inis = new[] { eng, sus, drv, carIni };
            for (int f = 0; f < inis.Length; f++)
                foreach (var s in inis[f].Order)
                    if (s.Name.IndexOf("DAMAGE", StringComparison.OrdinalIgnoreCase) >= 0 || s.Name == "PIT_STOP")
                        foreach (var k in s.Keys)
                        {
                            string key = files[f] + "." + s.Name + "." + k;
                            SortedSet<string> set; if (!keyCensus.TryGetValue(key, out set)) keyCensus[key] = set = new SortedSet<string>();
                            set.Add(car.Id);
                        }
            var ed = eng["DAMAGE"];
            var turbos = eng.Numbered("TURBO_");
            float maxB = 0, wg = 0;
            foreach (var t in turbos) { maxB = Math.Max(maxB, t.GetFloat("MAX_BOOST")); wg = Math.Max(wg, t.GetFloat("WASTEGATE")); }
            float tbThr = ed == null ? 0 : ed.GetFloat("TURBO_BOOST_THRESHOLD");
            if (turbos.Count > 0) { turboCars++; float eff = wg > 0 ? Math.Min(wg, maxB) : maxB; if (eff > tbThr) turboOver++; }
            float lim = eng.GetFloat("ENGINE_DATA", "LIMITER"), thr = ed == null ? 0 : ed.GetFloat("RPM_THRESHOLD");
            if (thr > 0 && lim > 0 && thr <= lim) rpmBelowLimiter++;
            var sd = sus["DAMAGE"];
            string zones = "";
            foreach (var w in aero.Numbered("WING_"))
            {
                string z = "";
                foreach (var zn in new[] { "FRONT", "REAR", "LEFT", "RIGHT" })
                {
                    float cl = w.GetFloat("ZONE_" + zn + "_CL"), cd = w.GetFloat("ZONE_" + zn + "_CD");
                    if (cl != 0 || cd != 0) z += zn[0] + "(" + MechN(cl) + "/" + MechN(cd) + ")";
                }
                if (z.Length > 0) zones += (w.Get("NAME") ?? w.Name) + ":" + z + " ";
            }
            var ps = carIni["PIT_STOP"];
            Console.WriteLine(car.Id + " | " + MechF(eng["ENGINE_DATA"], "LIMITER") + " " + MechF(ed, "RPM_THRESHOLD") + " " + MechF(ed, "RPM_DAMAGE_K")
                + " | " + turbos.Count + " " + MechN(maxB) + " " + MechN(wg) + " " + MechF(ed, "TURBO_BOOST_THRESHOLD") + " " + MechF(ed, "TURBO_DAMAGE_K")
                + " | " + MechF(sus["FRONT"], "TYPE") + " " + MechF(sus["REAR"], "TYPE") + " " + MechF(sd, "MIN_VELOCITY") + " " + MechF(sd, "GAIN") + " " + MechF(sd, "MAX_DAMAGE")
                + " | " + MechF(drv["DAMAGE"], "RPM_WINDOW_K") + " sh=" + MechF(drv["GEARBOX"], "SUPPORTS_SHIFTER") + " win=" + MechF(drv["GEARBOX"], "VALID_SHIFT_RPM_WINDOW") + " | " + zones.Trim()
                + " | " + MechF(ps, "BODY_REPAIR_TIME_SEC") + "/" + MechF(ps, "ENGINE_REPAIR_TIME_SEC") + "/" + MechF(ps, "SUSP_REPAIR_TIME_SEC"));
        }
        Console.WriteLine("== cars " + cars + "; turbo cars " + turboCars + " (min(wastegate,maxBoost) > TURBO_BOOST_THRESHOLD: " + turboOver + "); RPM_THRESHOLD <= LIMITER: " + rpmBelowLimiter);
        Console.WriteLine("== key census");
        foreach (var kv in keyCensus) Console.WriteLine("  " + kv.Key + "  n=" + kv.Value.Count + (kv.Value.Count < 10 ? "  " + string.Join(",", kv.Value) : ""));
        return 0;
    }

    static int Cmd_mechkeys(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var rx = new Regex("DAMAGE|LIFE|BLOW|PUNCT|EXPLO|BURST|BROK|FAIL|REPAIR|INFLAT|FLAT", RegexOptions.IgnoreCase);
        var hits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var car in c.Cars)
        {
            if (!car.Owned || car.IsDlcStub) continue;
            var acd = AcdArchive.Open(car.Dir);
            foreach (var n in acd.Names)
            {
                if (!n.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || n == "damage.ini" || n == "proview_nodes.ini") continue;
                var ini = acd.ReadIni(n);
                if (ini == null) continue;
                foreach (var s in ini.Order)
                {
                    bool secHit = rx.IsMatch(s.Name);
                    foreach (var k in s.Keys)
                        if (secHit || rx.IsMatch(k))
                        {
                            string key = n + " [" + Regex.Replace(s.Name, "_[0-9]+$", "_n") + "] " + k;
                            int h; hits.TryGetValue(key, out h); hits[key] = h + 1;
                        }
                }
            }
        }
        foreach (var kv in hits) Console.WriteLine(kv.Key + "  x" + kv.Value);
        return 0;
    }
}
