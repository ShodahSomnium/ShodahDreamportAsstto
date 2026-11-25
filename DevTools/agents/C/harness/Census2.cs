using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;
static partial class Program
{
    // grep <ini> <key> [value-substring]: print car / section / full section for sections containing key (optionally value match)
    static int Cmd_inigrep(List<string> a)
    {
        string ini = a[1], key = a[2], val = a.Count > 3 ? a[3] : null;
        var content = AcContent.Scan(_install);
        foreach (var c in content.Cars)
        {
            if (!c.Owned) continue;
            var acd = AcdArchive.Open(c.Dir); var i = acd.ReadIni(ini); if (i == null) continue;
            foreach (var s in i.Order)
            {
                if (!s.Has(key)) continue;
                string v = s.Get(key);
                if (val != null && (v == null || v.IndexOf(val, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                var sb = new System.Text.StringBuilder();
                foreach (var k in s.Keys) sb.Append(k + "=" + s.Get(k) + " ");
                Console.WriteLine(c.Id + " [" + s.Name + "] " + sb);
            }
        }
        return 0;
    }
}
