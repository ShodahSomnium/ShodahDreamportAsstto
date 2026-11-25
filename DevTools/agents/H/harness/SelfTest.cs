using System;
using SomniumSpace.Worlds.SomniumAssetto;

// Pure-math self tests of the AC -> Unity conversion helpers and the LUT / ini / json readers.
static partial class Program
{
    static int SelfTest()
    {
        int fails = 0;
        var rnd = new Random(7);
        double worstQ = 0, worstM = 0;
        for (int k = 0; k < 10000; k++)
        {
            var q = new AcQuat((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
            float n = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W); q = new AcQuat(q.X / n, q.Y / n, q.Z / n, q.W / n);
            var t = new AcVec3((float)rnd.NextDouble() * 10, (float)rnd.NextDouble() * 10, (float)rnd.NextDouble() * 10);
            var m = AcMat4.FromTrs(q, t, new AcVec3(1, 1, 1));
            // 1) Unity rotation of the mirrored matrix == (x, -y, -z, w) of the AC quaternion (up to sign)
            var u = m.UnityRotation; var e = q.MirrorX;
            double d1 = Math.Abs(u.X - e.X) + Math.Abs(u.Y - e.Y) + Math.Abs(u.Z - e.Z) + Math.Abs(u.W - e.W);
            double d2 = Math.Abs(u.X + e.X) + Math.Abs(u.Y + e.Y) + Math.Abs(u.Z + e.Z) + Math.Abs(u.W + e.W);
            worstQ = Math.Max(worstQ, Math.Min(d1, d2));
            // 2) U*mirror(p) == mirror(p*M) for a point (ToUnity is the column-vector conjugate)
            var p = new AcVec3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
            var a = m.TransformPoint(p).MirrorX;
            var U = m.ToUnity(); var pm = p.MirrorX;
            var b = new AcVec3(U[0] * pm.X + U[1] * pm.Y + U[2] * pm.Z + U[3], U[4] * pm.X + U[5] * pm.Y + U[6] * pm.Z + U[7], U[8] * pm.X + U[9] * pm.Y + U[10] * pm.Z + U[11]);
            worstM = Math.Max(worstM, (a - b).Length);
            // 3) inverse
            var id = AcMat4.Mul(m, m.InverseAffine());
            if (Math.Abs(id.M0 - 1) > 1e-4 || Math.Abs(id.M13) > 1e-3) fails++;
        }
        Console.WriteLine("quaternion mirror worst " + worstQ.ToString("E2") + ", ToUnity point worst " + worstM.ToString("E2") + ", inverse fails " + fails);
        if (worstQ > 1e-4 || worstM > 1e-4) fails++;
        // heading: node +Z = AC (+1,0,0) (car left) -> Unity yaw -90
        var h = AcMat4.Identity; h.M8 = 1; h.M10 = 0; h.M0 = 0; h.M2 = -1;
        Console.WriteLine("heading of AC +X forward: " + h.HeadingDeg + " (expect -90)"); if (Math.Abs(h.HeadingDeg + 90) > 1e-3) fails++;
        // LUTs
        var lut = AcLut.Parse("; c\n-3000|100\n0|200 ; x\n\n9500|0\r\n");
        var inl = AcLut.ParseInline("(|0=1|500=0.5|)");
        Console.WriteLine("lut " + lut.Count + " eval(-5000)=" + lut.Eval(-5000) + " eval(4750)=" + lut.Eval(4750) + "; inline " + inl.Count + " eval(250)=" + inl.Eval(250));
        if (lut.Count != 3 || Math.Abs(lut.Eval(4750) - 100) > 1e-3 || inl.Count != 2 || Math.Abs(inl.Eval(250) - 0.75f) > 1e-4) fails++;
        // ini
        var ini = AcIni.Parse("KEYLESS=1\n[A]\nX=1 ; c\n//// comment\nV=0 ,-0.285 ,0.3\t; v\n[B]\nU=http://x\n[a]\nX=2\nF=0,5\nL=(0=0|1000=24)\n");
        Console.WriteLine("ini X=" + ini.Get("A", "X") + " V=" + string.Join("|", ini.GetFloats("A", "V")) + " U=" + ini.Get("B", "U") + " F=" + ini.GetFloat("A", "F") + " L=" + ini["A"].GetInlineLut("L").Eval(500) + " root=" + ini.Get("", "KEYLESS"));
        if (ini.Get("A", "X") != "2" || ini.GetFloats("A", "V").Length != 3 || ini.Get("B", "U") != "http://x" || Math.Abs(ini.GetFloat("A", "F") - 0.5f) > 1e-6) fails++;
        // json
        var j = AcJson.Parse("\uFEFF{ \"name\" : \"A\tB\nC\", \"tags\": [\"x\",\"y\",], \"len\": \"3.602\", n: 5, \"bad\": \"a\\qb\", }") as System.Collections.Generic.Dictionary<string, object>;
        Console.WriteLine("json name=" + AcJson.Str(j, "name").Replace("\n", "\n").Replace("\t", "\t") + " tags=" + AcJson.Strings(j, "tags").Count + " len=" + AcContent.ParseLengthM(AcJson.Str(j, "len")) + " n=" + AcJson.Num(j, "n") + " bad=" + AcJson.Str(j, "bad"));
        if (AcJson.Strings(j, "tags").Count != 2 || AcContent.ParseLengthM("3.602") != 3602f || AcContent.ParseLengthM("4655m") != 4655f || AcJson.Num(j, "n") != 5f) fails++;
        // binary reader across buffer boundaries
        var bytes = new byte[100000]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)i;
        using (var r = new AcBinary(new System.IO.MemoryStream(bytes), true, 64))
        {
            r.Skip(3); var f = new float[1000]; r.ReadFloats(f, 0, 1000); r.Position = 50000; int v = r.ReadInt32();
            if (v != BitConverter.ToInt32(bytes, 50000) || f[999] != BitConverter.ToSingle(bytes, 3 + 999 * 4)) { fails++; Console.WriteLine("AcBinary mismatch"); }
        }
        Console.WriteLine(fails == 0 ? "SELFTEST PASS" : "SELFTEST FAILURES " + fails);
        return fails == 0 ? 0 : 6;
    }
}
