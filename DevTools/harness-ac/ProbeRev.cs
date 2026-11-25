using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumAssetto;
static partial class Program
{
    static int Cmd_revbrake(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string id = a.Count > 1 ? a[1] : "ferrari_458";
        float pedal = a.Count > 2 ? float.Parse(a[2], PInv) : 0.4f;
        foreach (var car in content.Cars)
        {
            if (!car.Owned || !car.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
            AcCarSpec s = AcCarSpec.Load(car);
            var c = new ChassisSim(s, 0.02f);
            c.Core.Brake = 1f; for (int k = 0; k < 100; k++) c.Step();
            Console.WriteLine("static CG " + F(c.B.P.Y, "0.000") + " X F/R " + F(c.Ch.Wheels[0].X * 1000f, "0") + "/" + F(c.Ch.Wheels[2].X * 1000f, "0") + " load " + F(c.Core.Load[0], "0") + "/" + F(c.Core.Load[2], "0") + " rodF " + F(c.Ch.Wheels[0].Rod*1000f,"0") + " k " + s.Front.SpringRate + " bumpUp " + s.Front.BumpStopUp + " packer " + s.Front.PackerRange);
            c.B.V = c.B.Fwd * (110f / 3.6f);
            for (int i = 0; i < 4; i++) c.Core.Omega[i] = 110f / 3.6f / c.Core.Tyre[i].Radius;
            c.Core.Brake = pedal;
            for (int k = 0; k < 150 && c.Kmh > 5f; k++)
            {
                c.Step();
                if (k % 10 == 0) Console.WriteLine(" t " + F(k * 0.02f, "0.00") + " " + F(c.Kmh, "0") + " km/h CG " + F(c.B.P.Y, "0.000") + " pitch " + F((float)(Math.Asin(c.B.Fwd.Y) * 57.3), "0.00") + " X F/R " + F(c.Ch.Wheels[0].X * 1000f, "0") + "/" + F(c.Ch.Wheels[2].X * 1000f, "0") + " load F/R " + F(c.Core.Load[0], "0") + "/" + F(c.Core.Load[2], "0"));
            }
        }
        return 0;
    }
}
