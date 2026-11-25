using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using SomniumSpace.Worlds.SomniumAssetto;

// Module W (World / track) probes. Commands: wmat <kn5> [filter]  (every property A/B/C/D + samplers of matching materials)
static partial class Program
{
    static string WF(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    static int Cmd_wmat(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.HeaderOnly);
        string filt = a.Count > 2 ? a[2] : null;
        foreach (var m in k.Materials)
        {
            if (filt != null && m.Shader.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0 && m.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine(m.Name + " [" + m.Shader + "] blend " + m.BlendMode + " at " + m.AlphaTested + " depth " + m.DepthMode);
            foreach (var p in m.Properties)
                Console.WriteLine("   " + p.Name + " A=" + WF(p.A) + " B=(" + WF(p.B[0]) + "," + WF(p.B[1]) + ") C=(" + WF(p.C[0]) + "," + WF(p.C[1]) + "," + WF(p.C[2]) + ") D=(" + WF(p.D[0]) + "," + WF(p.D[1]) + "," + WF(p.D[2]) + "," + WF(p.D[3]) + ")");
            foreach (var s in m.Samplers) Console.WriteLine("   tex " + s.Name + "@" + s.Slot + " = " + s.Texture);
        }
        return 0;
    }
}
