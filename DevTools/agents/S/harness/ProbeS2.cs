using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

static partial class Program
{
    // camspace <car...>: is car.ini BONNET/BUMPER_CAMERA_POS in model space (like DRIVEREYES) or physics space (model + GRAPHICS_OFFSET)?
    // Prints the body's top surface height (|x| < 0.15 m) at the camera's z for both readings.
    static int Cmd_camspace(List<string> a)
    {
        for (int ci = 1; ci < a.Count; ci++)
        {
            AcCarEntry car = AcContent.Scan(_install).Car(a[ci]);
            if (car == null) { Console.WriteLine(a[ci] + ": no car"); continue; }
            AcdArchive acd = AcdArchive.Open(Path.GetDirectoryName(car.Kn5Path));
            AcIni ini = acd.ReadIniOrEmpty("car.ini");
            float[] off = ini.GetFloats("BASIC", "GRAPHICS_OFFSET");
            float[] eye = ini.GetFloats("GRAPHICS", "DRIVEREYES");
            float[] bon = ini.GetFloats("GRAPHICS", "BONNET_CAMERA_POS");
            float[] bum = ini.GetFloats("GRAPHICS", "BUMPER_CAMERA_POS");
            Kn5File f = Kn5File.Read(car.Kn5Path, Kn5ReadOptions.HeaderOnly);
            var pts = new List<AcVec3>();
            Walk(f, f.Root, pts);
            Console.WriteLine(car.Id + ": offset " + J(off) + " eyes " + J(eye) + " bonnet " + J(bon) + " bumper " + J(bum) + " (" + pts.Count + " centre verts)");
            Report(pts, "bonnet", bon, off);
            Report(pts, "bumper", bum, off);
            Report(pts, "eyes", eye, off);
        }
        return 0;
    }

    static string J(float[] v) { return v == null ? "-" : string.Join(",", Array.ConvertAll(v, x => x.ToString("0.###"))); }

    static void Report(List<AcVec3> pts, string what, float[] p, float[] off)
    {
        if (p == null || p.Length < 3) return;
        float ox = off != null && off.Length >= 3 ? off[1] : 0f, oz = off != null && off.Length >= 3 ? off[2] : 0f;
        Console.WriteLine("  " + what + " as model: y " + p[1].ToString("0.00") + " vs body top " + Top(pts, p[2]).ToString("0.00") + " at z " + p[2].ToString("0.00")
            + " | as physics: y " + (p[1] - ox).ToString("0.00") + " vs body top " + Top(pts, p[2] - oz).ToString("0.00") + " at z " + (p[2] - oz).ToString("0.00"));
    }

    static float Top(List<AcVec3> pts, float z)
    {
        float best = -9f;
        foreach (var q in pts) if (Math.Abs(q.Z - z) < 0.06f && q.Y > best) best = q.Y;
        return best;
    }

    static void Walk(Kn5File f, Kn5Node n, List<AcVec3> pts)
    {
        if (n == null || !n.Active) return;
        if (n.IsMesh && n.Mesh != null)
        {
            f.LoadMesh(n.Mesh);
            AcMat4 w = n.World;
            for (int i = 0; i < n.Mesh.VertexCount; i++)
            {
                AcVec3 p = w.TransformPoint(n.Mesh.Position(i));
                if (Math.Abs(p.X) < 0.15f) pts.Add(p);
            }
            n.Mesh.Release();
        }
        foreach (var c in n.Children) Walk(f, c, pts);
    }
}
