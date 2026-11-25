using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

static partial class Program
{
    // vrside <car...>: the dash line across yaw from the cockpit eye and the VR panel fitted beside the wheel (rim edge 21 deg assumed)
    static int Cmd_vrside(List<string> a)
    {
        AcContent content = AcContent.Scan(_install);
        for (int ci = 1; ci < a.Count; ci++)
        {
            AcCarEntry car = content.Car(a[ci]);
            if (car == null) continue;
            AcIni ini = AcdArchive.Open(Path.GetDirectoryName(car.Kn5Path)).ReadIniOrEmpty("car.ini");
            float[] e = ini.GetFloats("GRAPHICS", "DRIVEREYES");
            string problem;
            var s = AcHudSightline.Measure(car.Kn5Path, new AcVec3(e[0], e[1], e[2]), out problem);
            if (s == null) { Console.WriteLine(car.Id + ": " + problem); continue; }
            float side = e[0] > 0f ? 1f : -1f;   // AC +X = car left: a left seat -> the centre is to the right
            string l = "";
            for (int y = -20; y <= 64; y += 6) l += " " + y + ":" + s.LineAt(side * y).ToString("0.0");
            Console.WriteLine(car.Id + " (" + s.Ms + " ms) line by yaw toward the centre:" + l);
            float dist = 0.62f, size = 0.42f, h = size * 660f / 900f, rim = 21f, top;
            float yaw = side * (rim + 3f + (float)(Math.Atan(size * 0.5f / dist) * 180 / Math.PI));
            float pitch = AcHudSightline.FitAt(s, 12f, dist, yaw, size, h, 1f, 6f, out top);
            for (int it = 0; it < 3; it++)
            {
                float inner = AcHudSightline.InnerEdgeYaw(pitch, yaw, dist, size, h);
                yaw += side * (rim + 3f - inner);
                pitch = AcHudSightline.FitAt(s, 12f, dist, yaw, size, h, 1f, 6f, out top);
            }
            float x, yy, z; AcHudSightline.PanelPoint(pitch, yaw, dist, 0f, -h * 0.5f, out x, out yy, out z);
            float bottom = (float)(Math.Atan2(-yy, Math.Sqrt(x * x + z * z)) * 180 / Math.PI);
            Console.WriteLine("  panel: yaw " + yaw.ToString("0.0") + " centre pitch " + pitch.ToString("0.0") + " top " + top.ToString("0.0") + " bottom-centre " + bottom.ToString("0.0")
                + " inner edge " + AcHudSightline.InnerEdgeYaw(pitch, yaw, dist, size, h).ToString("0.0"));
        }
        return 0;
    }
}
