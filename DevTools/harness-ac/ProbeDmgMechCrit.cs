using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumAssetto;

// Critic probe for dmg_mechanical.md (read-only): turbo TOTAL boost vs TURBO_BOOST_THRESHOLD (per-turbo sum, with
// [ENGINE_DATA] DEFAULT_TURBO_ADJUSTMENT), setup-adjustable limiter vs RPM_THRESHOLD, steer-arm z and toe per MAX_DAMAGE.
//   mechcrit
static partial class Program
{
    static string Cf(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

    static int Cmd_mechcrit(List<string> a)
    {
        var c = AcContent.Scan(_install);
        Console.WriteLine("== turbo: car | n | sumMin(wg,max) | defAdj([ENGINE_DATA]) cockpitAdj | total@def | thr | over@100% over@def");
        foreach (var car in c.Cars)
        {
            if (!car.Owned || car.IsDlcStub) continue;
            var acd = AcdArchive.Open(car.Dir);
            var eng = acd.ReadIniOrEmpty("engine.ini");
            var turbos = eng.Numbered("TURBO_");
            var ed = eng["DAMAGE"];
            if (turbos.Count > 0)
            {
                float sum = 0, sumDef = 0; bool adj = false;
                string defS = eng["ENGINE_DATA"] != null ? eng["ENGINE_DATA"].Get("DEFAULT_TURBO_ADJUSTMENT") : null;
                float def = defS != null ? float.Parse(defS.Trim(), CultureInfo.InvariantCulture) : 1f;
                foreach (var t in turbos)
                {
                    float mb = t.GetFloat("MAX_BOOST"), wg = t.GetFloat("WASTEGATE");
                    bool ca = t.GetFloat("COCKPIT_ADJUSTABLE") > 0.5f; adj |= ca;
                    float e = wg > 0 ? Math.Min(wg, mb) : mb;
                    sum += e; sumDef += (wg > 0 && ca) ? Math.Min(mb, wg * def) : e;
                }
                float thr = ed == null ? -1 : ed.GetFloat("TURBO_BOOST_THRESHOLD");
                Console.WriteLine(car.Id + " | " + turbos.Count + " | " + Cf(sum) + " | " + (defS ?? "-") + " " + (adj ? "adj" : "fixed") + " | " + Cf(sumDef) + " | " + Cf(thr)
                    + " | " + (thr > 0 && sum > thr ? "OVER" : "-") + " " + (thr > 0 && sumDef > thr ? "OVER" : "-"));
            }
        }
        Console.WriteLine("== limiter setup: car | LIMITER | RPM_THRESHOLD | setup sections with LIMITER (MIN..MAX step)");
        foreach (var car in c.Cars)
        {
            if (!car.Owned || car.IsDlcStub) continue;
            var acd = AcdArchive.Open(car.Dir);
            var setup = acd.ReadIniOrEmpty("setup.ini");
            var eng = acd.ReadIniOrEmpty("engine.ini");
            string s = "";
            foreach (var sec in setup.Order)
                if (sec.Name.IndexOf("LIMIT", StringComparison.OrdinalIgnoreCase) >= 0 || sec.Name.IndexOf("TURBO", StringComparison.OrdinalIgnoreCase) >= 0)
                    s += sec.Name + "(" + sec.Get("MIN") + ".." + sec.Get("MAX") + " st " + sec.Get("STEP") + ") ";
            if (s.Length > 0)
                Console.WriteLine(car.Id + " | " + eng.GetFloat("ENGINE_DATA", "LIMITER") + " | " + (eng["DAMAGE"] != null ? eng["DAMAGE"].Get("RPM_THRESHOLD") : "-") + " | " + s);
        }
        Console.WriteLine("== steer arm: car | front WBTYRE_STEER z, rear z | MAX_DAMAGE | toe deg at MAX (front, rear) | TOE_OUT F R");
        foreach (var car in c.Cars)
        {
            if (!car.Owned || car.IsDlcStub) continue;
            var acd = AcdArchive.Open(car.Dir);
            var sus = acd.ReadIniOrEmpty("suspensions.ini");
            float zf = Z(sus["FRONT"]), zr = Z(sus["REAR"]);
            float md = sus["DAMAGE"] != null ? sus["DAMAGE"].GetFloat("MAX_DAMAGE") : 0f;
            Func<float, float> deg = z => Math.Abs(z) > 0.02f ? (float)(Math.Atan(md / z) * 57.29578) : 0f;
            Console.WriteLine(car.Id + " | " + Cf(zf) + " " + Cf(zr) + " | " + Cf(md) + " | " + Cf(deg(zf)) + " " + Cf(deg(zr)) + " | "
                + (sus["FRONT"] != null ? sus["FRONT"].Get("TOE_OUT") : "-") + " " + (sus["REAR"] != null ? sus["REAR"].Get("TOE_OUT") : "-"));
        }
        return 0;
    }

    static float Z(AcIniSection s)
    {
        if (s == null) return 0f;
        string v = s.Get("WBTYRE_STEER"); if (v == null) return 0f;
        var p = v.Split(',');
        return p.Length >= 3 ? float.Parse(p[2].Trim(), CultureInfo.InvariantCulture) : 0f;
    }
}
