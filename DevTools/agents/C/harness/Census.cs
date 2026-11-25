using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;
static partial class Program
{
    // census <ini>: section-prefix -> key counts, and values of TYPE= etc., over all owned cars
    static int Cmd_census(List<string> a)
    {
        string ini = a[1];
        var content = AcContent.Scan(_install);
        var keys = new SortedDictionary<string, int>();
        var vals = new SortedDictionary<string, int>();
        int cars = 0, have = 0;
        foreach (var c in content.Cars)
        {
            if (!c.Owned) continue; cars++;
            var acd = AcdArchive.Open(c.Dir); var i = acd.ReadIni(ini); if (i == null) continue; have++;
            foreach (var s in i.Order)
            {
                string p = s.Name; int u = p.LastIndexOf('_'); if (u > 0 && char.IsDigit(p[p.Length-1])) p = p.Substring(0, u);
                foreach (var k in s.Keys) { string kk = p + "." + k; keys[kk] = keys.TryGetValue(kk, out var n) ? n + 1 : 1;
                    if (a.Count > 2 && a.IndexOf(k) > 1) { string vv = kk + "=" + s.Get(k); vals[vv] = vals.TryGetValue(vv, out var m) ? m + 1 : 1; } }
            }
        }
        Console.WriteLine(ini + ": " + have + "/" + cars);
        foreach (var kv in keys) Console.WriteLine("  " + kv.Key + " x" + kv.Value);
        foreach (var kv in vals) Console.WriteLine("  V " + kv.Key + " x" + kv.Value);
        return 0;
    }
}
static partial class Program
{
    static int Cmd_anim10(List<string> a)
    {
        var k = Ksanim.Read(a[1]);
        foreach (var n in k.Nodes)
        {
            if (a.Count > 2 && n.Name != a[2]) continue;
            Console.Write(n.Name + ":");
            for (int f = 0; f < n.FrameCount; f += Math.Max(1, n.FrameCount / 20))
            { var m = n.Frames[f]; Console.Write(" [" + f + " z(" + m.M8.ToString("0.00") + "," + m.M9.ToString("0.00") + "," + m.M10.ToString("0.00") + ") y(" + m.M4.ToString("0.00") + "," + m.M5.ToString("0.00") + "," + m.M6.ToString("0.00") + ") t" + m.Translation + "]"); }
            Console.WriteLine();
        }
        return 0;
    }
}
