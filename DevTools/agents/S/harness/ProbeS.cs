using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// Module S (seat, cockpit, driving input) probes. Run: dotnet run -c Release --no-build -- <name> [args]
//   seat [car]      per owned car: STEER_LOCK / STEER_RATIO, DRIVEREYES (+ system/cfg/vr.ini override), knh hips / head / hands, STEER_HR hub,
//                   rim radius measured from the kn5 mesh (the VR grab's MeasureRim on the real data), hands vs rim, doors + door ksanims,
//                   driver3d STEER_ANIMATION LOCK. Ends with a census line.
//   seatcams        system/cfg/chase_cam.ini, camera_onboard.ini, controls.ini [KEYBOARD]/[X360] - the numbers AcCarCamera / AcKeys read.
static partial class Program
{
    static int Cmd_seat(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        AcContent content = AcContent.Scan(_install);
        AcIni vr = null;
        string vrPath = Path.Combine(Path.Combine(_install.SystemDir, "cfg"), "vr.ini");
        if (File.Exists(vrPath)) vr = AcIni.Load(vrPath);
        int cars = 0, withVr = 0, withSteer = 0, withRim = 0, doorsL = 0, doorsR = 0, doorAnimL = 0, doorAnimR = 0, knhOk = 0, handsOnRim = 0;
        float rimMin = 9f, rimMax = 0f, lockMin = 9999f, lockMax = 0f;
        int camBonnet = 0, camBumper = 0, camDash = 0, camPitch = 0;
        float pitchMin = 99f, pitchMax = -99f;
        var lockHist = new SortedDictionary<int, int>();
        foreach (AcCarEntry car in content.Cars)
        {
            if (!car.Owned) continue;
            if (only != null && !string.Equals(car.Id, only, StringComparison.OrdinalIgnoreCase)) continue;
            cars++;
            string line = car.Id + ":";
            try
            {
                AcdArchive acd = AcdArchive.Open(CarPath(car.Id));
                AcIni ci = acd.ReadIniOrEmpty("car.ini");
                float steerLock = ci.GetFloat("CONTROLS", "STEER_LOCK", float.NaN);
                float steerRatio = ci.GetFloat("CONTROLS", "STEER_RATIO", float.NaN);
                float[] eyes = ci.GetFloats("GRAPHICS", "DRIVEREYES");
                float[] off = ci.GetFloats("BASIC", "GRAPHICS_OFFSET");
                AcIni d3 = acd.ReadIniOrEmpty("driver3d.ini");
                float animLock = d3.GetFloat("STEER_ANIMATION", "LOCK", float.NaN);
                line += " lock " + F(steerLock) + " ratio " + F(steerRatio) + " animLock " + F(animLock);
                float[] bonnet = ci.GetFloats("GRAPHICS", "BONNET_CAMERA_POS"), bumper = ci.GetFloats("GRAPHICS", "BUMPER_CAMERA_POS");
                float pitch = ci.GetFloat("GRAPHICS", "ON_BOARD_PITCH_ANGLE", float.NaN);
                AcIni dash = acd.ReadIni("dash_cam.ini");
                float[] dashPos = dash != null ? dash.GetFloats("DASH_CAM", "POS") : null;
                if (bonnet != null && bonnet.Length >= 3) camBonnet++;
                if (bumper != null && bumper.Length >= 3) camBumper++;
                if (dashPos != null && dashPos.Length >= 3) camDash++;
                if (!float.IsNaN(pitch)) { camPitch++; pitchMin = Math.Min(pitchMin, pitch); pitchMax = Math.Max(pitchMax, pitch); }
                line += " cams bonnet " + SV(bonnet) + " bumper " + SV(bumper) + " dash " + SV(dashPos) + " pitch " + F(pitch);
                if (!float.IsNaN(steerLock)) { lockMin = Math.Min(lockMin, steerLock); lockMax = Math.Max(lockMax, steerLock); int k = (int)steerLock; lockHist[k] = lockHist.TryGetValue(k, out int c) ? c + 1 : 1; }
                line += " eyes " + SV(eyes) + " off " + SV(off);
                float[] vrEyes = vr != null ? vr.GetFloats(car.Id, "DRIVEREYES") : null;
                if (vrEyes != null && vrEyes.Length >= 3) { withVr++; line += " vrEyes " + SV(vrEyes); }

                AcVec3 hub = default(AcVec3);
                bool haveHub = false;
                AcMat4 steerWorld = AcMat4.Identity;
                string kn5 = car.Kn5Path;
                if (kn5 != null && File.Exists(kn5))
                {
                    Kn5File f = Kn5File.Read(kn5, Kn5ReadOptions.HeaderOnly);
                    Kn5Node steer = f.Find("STEER_HR") ?? f.Find("STEER_LR");
                    bool dl = f.Find("DOOR_L") != null, dr = f.Find("DOOR_R") != null;
                    if (dl) doorsL++;
                    if (dr) doorsR++;
                    line += " doors " + (dl ? "L" : "-") + (dr ? "R" : "-");
                    if (steer != null)
                    {
                        withSteer++;
                        steerWorld = steer.World;
                        hub = steerWorld.Translation;
                        haveHub = true;
                        AcMat4 inv = steerWorld.InverseAffine();
                        float outer = 0f;
                        int verts = 0;
                        var meshes = new List<Kn5Node>();
                        SCollect(steer, meshes);
                        foreach (Kn5Node m in meshes)
                        {
                            f.LoadMesh(m.Mesh);
                            AcMat4 toSteer = AcMat4.Mul(m.World, inv);
                            for (int i = 0; i < m.Mesh.VertexCount; i++)
                            {
                                AcVec3 p = toSteer.TransformPoint(m.Mesh.Position(i));
                                float r = (float)Math.Sqrt(p.X * p.X + p.Y * p.Y);
                                if (r > outer) outer = r;
                                verts++;
                            }

                            m.Mesh.Release();
                        }

                        AcVec3 z = steerWorld.AxisZ.Normalized;
                        line += " " + steer.Name + " hub " + hub + " axisZ " + z + " outer " + F(outer) + " (" + verts + " v)";
                        if (outer > 0.1f && outer < 0.4f) { withRim++; float rim = outer - 0.02f; rimMin = Math.Min(rimMin, rim); rimMax = Math.Max(rimMax, rim); }
                    }
                }

                string knhPath = car.KnhPath;
                if (knhPath != null && File.Exists(knhPath))
                {
                    Knh k = Knh.Read(knhPath);
                    AcVec3? hips = SPos(k, "RIG_Hips"), head = SPos(k, "RIG_Head"), hl = SPos(k, "RIG_HAND_L"), hr = SPos(k, "RIG_HAND_R"), centre = SPos(k, "RIG_Center");
                    if (hips.HasValue && head.HasValue && hl.HasValue && hr.HasValue)
                    {
                        knhOk++;
                        line += " hips " + hips.Value + " centre " + (centre.HasValue ? centre.Value.ToString() : "-") + " head " + head.Value;
                        if (haveHub)
                        {
                            AcMat4 inv = steerWorld.InverseAffine();
                            AcVec3 l = inv.TransformPoint(hl.Value), r = inv.TransformPoint(hr.Value);
                            float rl = (float)Math.Sqrt(l.X * l.X + l.Y * l.Y), rr = (float)Math.Sqrt(r.X * r.X + r.Y * r.Y);
                            // clock angle in the wheel plane, as the driver sees it: AC +X = car left, so the driver's right is -X; 0 = top (+Y)
                            float al = (float)(Math.Atan2(-l.X, l.Y) * 180.0 / Math.PI), ar = (float)(Math.Atan2(-r.X, r.Y) * 180.0 / Math.PI);
                            line += " handsInWheel L r" + F(rl) + " a" + F(al) + " z" + F(l.Z) + " R r" + F(rr) + " a" + F(ar) + " z" + F(r.Z);
                            if (rl > 0.1f && rl < 0.3f && rr > 0.1f && rr < 0.3f) handsOnRim++;
                        }
                        if (eyes != null && eyes.Length >= 3) line += " eye-head " + F((new AcVec3(eyes[0], eyes[1], eyes[2]) - head.Value).Length);
                    }
                }

                if (Directory.Exists(car.AnimationsDir ?? ""))
                {
                    bool al2 = false, ar2 = false;
                    foreach (string fn in Directory.GetFiles(car.AnimationsDir, "*.ksanim"))
                    {
                        string n = Path.GetFileNameWithoutExtension(fn);
                        if (string.Equals(n, "car_door_L", StringComparison.OrdinalIgnoreCase)) al2 = true;
                        if (string.Equals(n, "car_door_R", StringComparison.OrdinalIgnoreCase)) ar2 = true;
                    }

                    if (al2) doorAnimL++;
                    if (ar2) doorAnimR++;
                    line += " doorAnim " + (al2 ? "L" : "-") + (ar2 ? "R" : "-");
                }
            }
            catch (Exception e) { line += " ERROR " + e.GetType().Name + ": " + e.Message; }

            Console.WriteLine(line);
        }

        var hist = new List<string>();
        foreach (var kv in lockHist) hist.Add(kv.Key + "x" + kv.Value);
        Console.WriteLine("CENSUS: " + cars + " owned cars; knh ok " + knhOk + "; steering node " + withSteer + "; rim measured " + withRim + " (radius " + F(rimMin) + ".." + F(rimMax) + " m after the 2 cm tube inset); knh hands within 0.1-0.3 m of the hub in the rim plane " + handsOnRim +
                          "; vr.ini DRIVEREYES " + withVr + "; DOOR_L " + doorsL + " DOOR_R " + doorsR + "; car_door_L.ksanim " + doorAnimL + " car_door_R " + doorAnimR + "; STEER_LOCK " + F(lockMin) + ".." + F(lockMax) + " [" + string.Join(" ", hist) + "]" + "; cameras: BONNET_CAMERA_POS " + camBonnet + ", BUMPER_CAMERA_POS " + camBumper + ", dash_cam.ini POS " + camDash + ", ON_BOARD_PITCH_ANGLE " + camPitch + " (" + F(pitchMin) + ".." + F(pitchMax) + " deg)");
        return 0;
    }

    static int Cmd_seatcams(List<string> a)
    {
        string sys = Path.Combine(_install.SystemDir, "cfg");
        string cfg = Path.Combine(_install.Root, "cfg");
        SDump(Path.Combine(sys, "chase_cam.ini"));
        SDump(Path.Combine(cfg, "camera_onboard.ini"));
        SDump(Path.Combine(sys, "camera_onboard_free.ini"));
        AcIni c = File.Exists(Path.Combine(cfg, "controls.ini")) ? AcIni.Load(Path.Combine(cfg, "controls.ini")) : null;
        if (c != null)
        {
            foreach (string sec in new[] { "KEYBOARD", "X360", "GEARUP", "GEARDN", "HANDBRAKE", "ACTION_HEADLIGHTS", "ACTION_HORN" })
            {
                Console.WriteLine("controls.ini [" + sec + "]");
                foreach (string key in new[] { "MINIMUM_STEERING", "STEERING_SPEED", "STEERING_OPPOSITE_DIRECTION_SPEED", "STEER_GAIN", "STEER_RESET_SPEED", "STEER_GAMMA", "STEER_FILTER", "SPEED_SENSITIVITY", "STEER_DEADZONE", "STEER_SPEED", "KEY", "XBOXBUTTON", "GAS", "BRAKE", "LEFT", "RIGHT" })
                {
                    string v = c.Get(sec, key, null);
                    if (v != null) Console.WriteLine("  " + key + " = " + v);
                }
            }
        }

        return 0;
    }

    static void SDump(string path)
    {
        if (!File.Exists(path)) { Console.WriteLine(path + ": missing"); return; }
        AcIni ini = AcIni.Load(path);
        Console.WriteLine(path + ": " + File.ReadAllText(path).Replace("\r", "").Replace("\n", " | "));
    }

    static void SCollect(Kn5Node n, List<Kn5Node> into)
    {
        if (n.IsMesh && n.Mesh != null && n.Mesh.VertexCount > 0) into.Add(n);
        for (int i = 0; i < n.Children.Count; i++) SCollect(n.Children[i], into);
    }

    static AcVec3? SPos(Knh k, string suffix)
    {
        foreach (KnhNode n in k.Nodes)
            if (n.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return n.World.Translation;
        return null;
    }

    static string SV(float[] v)
    {
        if (v == null || v.Length < 3) return "-";
        return "(" + F(v[0]) + "," + F(v[1]) + "," + F(v[2]) + ")";
    }
}
