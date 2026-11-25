using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumAssetto;

static partial class Program
{
    // wuvgrad <kn5> <materialFilter> [n]: per mesh, median |grad u| and |grad v| in uv units per metre (surface gradient per triangle)
    static int Cmd_wuvgrad(List<string> a)
    {
        var k = Kn5File.Read(a[1], Kn5ReadOptions.NoTextures);
        string filt = a[2]; int max = a.Count > 3 ? int.Parse(a[3]) : 10, shown = 0;
        foreach (var n in k.Nodes)
        {
            if (n.Mesh == null) continue;
            var m = k.Material(n.Mesh.MaterialId);
            if (m == null || m.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var me = n.Mesh; var gu = new List<double>(); var gv = new List<double>();
            for (int t = 0; t + 2 < me.IndexCount; t += 3)
            {
                int i0 = me.Indices[t], i1 = me.Indices[t + 1], i2 = me.Indices[t + 2];
                var p0 = me.Position(i0); var e1 = me.Position(i1) - p0; var e2 = me.Position(i2) - p0;
                double du1 = me.U(i1) - me.U(i0), du2 = me.U(i2) - me.U(i0), dv1 = me.V(i1) - me.V(i0), dv2 = me.V(i2) - me.V(i0);
                // gram matrix
                double a11 = Dot(e1, e1), a12 = Dot(e1, e2), a22 = Dot(e2, e2), det = a11 * a22 - a12 * a12;
                if (det < 1e-6) continue;
                // grad f = c1 e1 + c2 e2 with [a11 a12; a12 a22] c = [df1; df2]; |grad|^2 = c^T G c
                double cu1 = (a22 * du1 - a12 * du2) / det, cu2 = (a11 * du2 - a12 * du1) / det;
                double cv1 = (a22 * dv1 - a12 * dv2) / det, cv2 = (a11 * dv2 - a12 * dv1) / det;
                gu.Add(Math.Sqrt(Math.Max(0, cu1 * cu1 * a11 + 2 * cu1 * cu2 * a12 + cu2 * cu2 * a22)));
                gv.Add(Math.Sqrt(Math.Max(0, cv1 * cv1 * a11 + 2 * cv1 * cv2 * a12 + cv2 * cv2 * a22)));
            }
            if (gu.Count == 0) continue;
            gu.Sort(); gv.Sort();
            Console.WriteLine(n.Name + " [" + m.Name + "] tris " + gu.Count + " |du/dm| median " + WF((float)gu[gu.Count / 2]) + " |dv/dm| median " + WF((float)gv[gv.Count / 2]) + "  (m per u " + WF((float)(1 / Math.Max(1e-9, gu[gu.Count / 2]))) + ", m per v " + WF((float)(1 / Math.Max(1e-9, gv[gv.Count / 2]))) + ")");
            if (++shown >= max) break;
        }
        return 0;
    }
    static double Dot(AcVec3 x, AcVec3 y) { return x.X * y.X + x.Y * y.Y + x.Z * y.Z; }
}
