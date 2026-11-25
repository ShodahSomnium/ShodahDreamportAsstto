using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] The numbers the seat, the cameras and the controls read from the player's own install (nothing ships):
    /// per car (data.acd car.ini [CONTROLS] STEER_LOCK, [GRAPHICS] DRIVEREYES / ON_BOARD_PITCH_ANGLE / BONNET_CAMERA_POS / BUMPER_CAMERA_POS,
    /// dash_cam.ini [DASH_CAM] POS, driver_base_pos.knh RIG_Hips / RIG_Head / RIG_HAND_L / RIG_HAND_R, system/cfg/vr.ini [car] DRIVEREYES)
    /// and global (system/cfg/chase_cam.ini CHASE_0 / CHASE_1, cfg/camera_onboard.ini FOV / HEAD_MAX_DEGREES / IS_WORLD_ALIGNED,
    /// cfg/controls.ini [KEYBOARD] / [X360] / [BRAKES]). Read on a worker thread (Unity-free work only); the main thread polls
    /// <see cref="AcSeatCar.Ready"/>. Positions are kept in AC MODEL space (AC axes: +X = car LEFT) and converted with <see cref="ToUnity"/>
    /// (mirror X, ac_car_formats.md 1.5). Verified offline by the harness probe <c>seat</c> (86/86 owned cars, see S/NOTES.md).
    /// </summary>
    public static class AcSeatData
    {
        private static readonly Dictionary<string, AcSeatCar> Cars = new Dictionary<string, AcSeatCar>(StringComparer.OrdinalIgnoreCase);
        private static AcSeatGlobals _globals;
        private static readonly object Gate = new object();
        private static AcIni _vrIni;
        private static bool _vrIniRead;

        /// <summary>The global camera / controls numbers (null until loaded; <see cref="Globals"/> starts the load).</summary>
        public static AcSeatGlobals Globals
        {
            get
            {
                if (_globals == null) StartGlobals();
                if (_globals == null || !_globals.Ready) return null;
                if (!_globalsReported) ReportGlobals();
                return _globals;
            }
        }

        /// <summary>The loaded globals, or <see cref="AcSeatGlobals.Fallback"/> (neutral values of ours, used only while / when the files are not
        /// read) - never null; the ONE place the fallback numbers live.</summary>
        public static AcSeatGlobals G { get { return Globals ?? AcSeatGlobals.Fallback; } }

        private static bool _globalsReported;

        private static void ReportGlobals()
        {
            _globalsReported = true;
            AcSeatGlobals g = _globals;
            if (g.Problem != null) AcLog.Warn("Seat: reading the install's camera / controls files failed (" + g.Problem + ") - using the install's values built in (// ours fallback).");
            AcLog.Info("Seat: game cameras / controls - chase_cam.ini " + (g.ChaseFromFile ? "read" : "MISSING (fallback)") + " (CHASE_0 " + g.ChaseDistance[0].ToString("0.0#") + "/" + g.ChaseHeight[0].ToString("0.0#") +
                       "/" + g.ChasePitchRad[0].ToString("0.####") + ", CHASE_1 " + g.ChaseDistance[1].ToString("0.0#") + "/" + g.ChaseHeight[1].ToString("0.0#") + "/" + g.ChasePitchRad[1].ToString("0.####") +
                       "), camera_onboard.ini " + (g.OnboardFromFile ? "read" : "MISSING (fallback)") + " (FOV " + g.OnboardFov.ToString("0.#") + ", HEAD_MAX_DEGREES " + g.HeadMaxDegrees.ToString("0") +
                       ", world aligned " + (g.OnboardWorldAligned ? "1" : "0") + "), controls.ini " + (g.ControlsFromFile ? "read" : "MISSING (fallback)") + ".");
        }

        /// <summary>Main thread, any frame: one Player.log line per car once its data is read (what the seat uses, and any read failure - a
        /// failed data.acd / knh read falls back silently otherwise).</summary>
        public static void ReportOnce(AcSeatCar d)
        {
            if (d == null || d.Reported || !d.Ready) return;
            d.Reported = true;
            if (d.Problem != null) AcLog.Warn("Seat: " + d.CarId + " seat data read failed (" + d.Problem + ") - the seat falls back to C's model / ours.");
            AcLog.Info("Seat: " + d.CarId + " seat data - knh " + (d.HasKnh ? "yes" : "no") + ", DRIVEREYES " + (d.HasEyes ? "yes" : "no") + ", vr.ini eyes " + (d.HasVrEyes ? "yes" : "no") +
                       ", STEER_LOCK " + d.SteerLockDeg.ToString("0") + " (STEER_RATIO " + d.SteerRatio.ToString("0.##") + ", P's; driver3d LOCK " + d.SteerAnimLockDeg.ToString("0") +
                       ", AC's hand-over-hand clip range - not used: our arms follow the wheel to +-" + AcSeatPoses.HandFollowDeg.ToString("0") + " deg), ON_BOARD_PITCH " + d.OnboardPitchDeg.ToString("0.#") +
                       ", dash/bonnet/bumper " + (d.HasDash ? "D" : "-") + (d.HasBonnet ? "B" : "-") + (d.HasBumper ? "U" : "-") + ", GRAPHICS_PITCH " + d.GraphicsPitchDeg.ToString("0.##") + ".");
        }

        private static bool _globalsStarted;

        private static void StartGlobals()
        {
            if (_globalsStarted) return;
            AcInstall install = AcGame.Current != null ? AcGame.Current.Install : null;
            if (install == null) return;
            _globalsStarted = true;
            var g = new AcSeatGlobals();
            _globals = g;
            ThreadPool.QueueUserWorkItem(LoadGlobals, new object[] { g, install });
        }

        /// <summary>The per-car seat data, loading it on a worker the first time (check <see cref="AcSeatCar.Ready"/>). Null without a car.</summary>
        public static AcSeatCar For(AcCarEntry car)
        {
            if (car == null || string.IsNullOrEmpty(car.Id)) return null;
            AcSeatCar d;
            lock (Gate)
            {
                if (Cars.TryGetValue(car.Id, out d)) return d;
                d = new AcSeatCar { CarId = car.Id };
                Cars[car.Id] = d;
            }

            AcInstall install = AcGame.Current != null ? AcGame.Current.Install : null;
            ThreadPool.QueueUserWorkItem(LoadCar, new object[] { d, car, install });
            return d;
        }

        /// <summary>AC model-space point -> Unity model-space point (mirror X).</summary>
        public static Vector3 ToUnity(AcVec3 p) { return new Vector3(-p.X, p.Y, p.Z); }

        // ------------------------------------------------------------------ worker side (no Unity API below)

        private static void LoadCar(object state)
        {
            var a = (object[])state;
            var d = (AcSeatCar)a[0];
            var car = (AcCarEntry)a[1];
            var install = (AcInstall)a[2];
            try
            {
                AcdArchive acd = AcdArchive.Open(car.Dir);
                AcIni ci = acd.ReadIniOrEmpty("car.ini");
                d.SteerLockDeg = ci.GetFloat("CONTROLS", "STEER_LOCK", 0f);
                d.SteerRatio = ci.GetFloat("CONTROLS", "STEER_RATIO", 0f);
                d.HasEyes = Vec(ci.GetFloats("GRAPHICS", "DRIVEREYES"), out d.Eyes);
                d.OnboardPitchDeg = ci.GetFloat("GRAPHICS", "ON_BOARD_PITCH_ANGLE", 0f);
                d.HasBonnet = Vec(ci.GetFloats("GRAPHICS", "BONNET_CAMERA_POS"), out d.Bonnet);
                d.HasBumper = Vec(ci.GetFloats("GRAPHICS", "BUMPER_CAMERA_POS"), out d.Bumper);
                d.BonnetPitchDeg = ci.GetFloat("GRAPHICS", "BONNET_CAMERA_PITCH", 0f);
                d.BumperPitchDeg = ci.GetFloat("GRAPHICS", "BUMPER_CAMERA_PITCH", 0f);
                d.HasOffset = Vec(ci.GetFloats("BASIC", "GRAPHICS_OFFSET"), out d.GraphicsOffset);
                d.GraphicsPitchDeg = ci.GetFloat("BASIC", "GRAPHICS_PITCH_ROTATION", 0f);
                AcIni dash = acd.ReadIni("dash_cam.ini");
                if (dash != null) d.HasDash = Vec(dash.GetFloats("DASH_CAM", "POS"), out d.Dash);
                AcIni d3 = acd.ReadIni("driver3d.ini");
                if (d3 != null) d.SteerAnimLockDeg = d3.GetFloat("STEER_ANIMATION", "LOCK", 0f);
            }
            catch (Exception e) { d.Problem = "data.acd: " + e.GetType().Name + ": " + e.Message; }

            try
            {
                if (!string.IsNullOrEmpty(car.KnhPath) && File.Exists(car.KnhPath))
                {
                    Knh k = Knh.Read(car.KnhPath);
                    d.HasKnh = Find(k, "RIG_Hips", out d.Hips) & Find(k, "RIG_Head", out d.Head) & Find(k, "RIG_HAND_L", out d.HandL) & Find(k, "RIG_HAND_R", out d.HandR);
                }
            }
            catch (Exception e) { d.Problem = (d.Problem != null ? d.Problem + "; " : "") + "knh: " + e.GetType().Name + ": " + e.Message; }

            try
            {
                AcIni vr = VrIni(install);
                if (vr != null) d.HasVrEyes = Vec(vr.GetFloats(car.Id, "DRIVEREYES"), out d.VrEyes);
            }
            catch (Exception) { /* no vr.ini: the cockpit eye is used in VR */ }

            d.Ready = true;
        }

        private static AcIni VrIni(AcInstall install)
        {
            lock (Gate)
            {
                if (_vrIniRead) return _vrIni;
                _vrIniRead = true;
                if (install == null) return null;
                string p = Path.Combine(Path.Combine(install.SystemDir, "cfg"), "vr.ini");
                _vrIni = File.Exists(p) ? AcIni.Load(p) : null;
                return _vrIni;
            }
        }

        private static void LoadGlobals(object state)
        {
            var a = (object[])state;
            var g = (AcSeatGlobals)a[0];
            var install = (AcInstall)a[1];
            try
            {
                string sys = Path.Combine(install.SystemDir, "cfg");
                string cfg = Path.Combine(install.Root, "cfg");
                AcIni chase = AcIni.LoadOrEmpty(Path.Combine(sys, "chase_cam.ini"));
                g.ChaseDistance[0] = chase.GetFloat("CHASE_0", "DISTANCE", g.ChaseDistance[0]);
                g.ChaseHeight[0] = chase.GetFloat("CHASE_0", "HEIGHT", g.ChaseHeight[0]);
                g.ChasePitchRad[0] = chase.GetFloat("CHASE_0", "PITCH", g.ChasePitchRad[0]);
                g.ChaseDistance[1] = chase.GetFloat("CHASE_1", "DISTANCE", g.ChaseDistance[1]);
                g.ChaseHeight[1] = chase.GetFloat("CHASE_1", "HEIGHT", g.ChaseHeight[1]);
                g.ChasePitchRad[1] = chase.GetFloat("CHASE_1", "PITCH", g.ChasePitchRad[1]);
                g.ChaseFromFile = chase.Has("CHASE_0");

                AcIni onboard = AcIni.LoadOrEmpty(Path.Combine(cfg, "camera_onboard.ini"));
                g.OnboardFov = onboard.GetFloat("MODE", "FOV", g.OnboardFov);
                g.OnboardWorldAligned = onboard.GetInt("MODE", "IS_WORLD_ALIGNED", 0) != 0;
                g.HeadMaxDegrees = onboard.GetFloat("ROTATION", "HEAD_MAX_DEGREES", g.HeadMaxDegrees);
                g.OnboardFromFile = onboard.Has("MODE");

                AcIni controls = AcIni.LoadOrEmpty(Path.Combine(cfg, "controls.ini"));
                g.KeyMinimumSteering = controls.GetFloat("KEYBOARD", "MINIMUM_STEERING", g.KeyMinimumSteering);
                g.KeySteeringSpeed = controls.GetFloat("KEYBOARD", "STEERING_SPEED", g.KeySteeringSpeed);
                g.KeyOppositeSpeed = controls.GetFloat("KEYBOARD", "STEERING_OPPOSITE_DIRECTION_SPEED", g.KeyOppositeSpeed);
                g.KeySteerGain = controls.GetFloat("KEYBOARD", "STEER_GAIN", g.KeySteerGain);
                g.KeyResetSpeed = controls.GetFloat("KEYBOARD", "STEER_RESET_SPEED", g.KeyResetSpeed);
                g.PadSteerGamma = controls.GetFloat("X360", "STEER_GAMMA", g.PadSteerGamma);
                g.PadSteerFilter = controls.GetFloat("X360", "STEER_FILTER", g.PadSteerFilter);
                g.PadSpeedSensitivity = controls.GetFloat("X360", "SPEED_SENSITIVITY", g.PadSpeedSensitivity);
                g.PadDeadzone = controls.GetFloat("X360", "STEER_DEADZONE", g.PadDeadzone);
                g.PadSteerSpeed = controls.GetFloat("X360", "STEER_SPEED", g.PadSteerSpeed);
                g.ControlsFromFile = controls.Has("KEYBOARD");
            }
            catch (Exception e) { g.Problem = e.GetType().Name + ": " + e.Message; }

            g.Ready = true;
        }

        private static bool Vec(float[] v, out AcVec3 r)
        {
            r = default(AcVec3);
            if (v == null || v.Length < 3) return false;
            r = new AcVec3(v[0], v[1], v[2]);
            return true;
        }

        private static bool Find(Knh k, string suffix, out AcVec3 p)
        {
            p = default(AcVec3);
            for (int i = 0; i < k.Nodes.Count; i++)
            {
                KnhNode n = k.Nodes[i];
                if (n.Name != null && n.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { p = n.World.Translation; return true; }
            }

            return false;
        }
    }

    /// <summary>[S] One car's seat / camera numbers from its own files (AC model space, AC axes). Filled on a worker; read after <see cref="Ready"/>.</summary>
    public sealed class AcSeatCar
    {
        public string CarId = "";
        public volatile bool Ready;
        public string Problem;
        /// <summary>The one-time Player.log line was written (<see cref="AcSeatData.ReportOnce"/>, main thread).</summary>
        public bool Reported;
        /// <summary>car.ini [CONTROLS] STEER_LOCK (deg, centre to full right; 0 = not read) and STEER_RATIO (negative on 16 installed cars).</summary>
        public float SteerLockDeg, SteerRatio;
        /// <summary>driver3d.ini [STEER_ANIMATION] LOCK (the driver's arm animation range).</summary>
        public float SteerAnimLockDeg;
        public bool HasEyes, HasVrEyes, HasBonnet, HasBumper, HasDash, HasKnh, HasOffset;
        /// <summary>car.ini DRIVEREYES (cockpit camera) / system/cfg/vr.ini [car] DRIVEREYES (the game's VR eye, 42 of 86 cars) - model space.</summary>
        public AcVec3 Eyes, VrEyes;
        /// <summary>car.ini BONNET_CAMERA_POS / BUMPER_CAMERA_POS are in PHYSICS space (model = pos - GRAPHICS_OFFSET: harness camspace on 4 cars puts them
        /// 0.0-0.27 m over the body top that way, 0.2-0.5 m INSIDE the body when read as model space); dash_cam.ini POS as read.</summary>
        public AcVec3 Bonnet, Bumper, Dash;
        /// <summary>car.ini BONNET_CAMERA_PITCH / BUMPER_CAMERA_PITCH (deg, negative = down like ON_BOARD_PITCH_ANGLE).</summary>
        public float BonnetPitchDeg, BumperPitchDeg;
        /// <summary>car.ini [GRAPHICS] ON_BOARD_PITCH_ANGLE (deg).</summary>
        public float OnboardPitchDeg;
        /// <summary>car.ini [BASIC] GRAPHICS_OFFSET / GRAPHICS_PITCH_ROTATION: the model's placement in the body when C's model root is missing.</summary>
        public AcVec3 GraphicsOffset;
        public float GraphicsPitchDeg;
        /// <summary>driver_base_pos.knh joints (model space): hips (seat), head, wrists.</summary>
        public AcVec3 Hips, Head, HandL, HandR;
    }

    /// <summary>[S] The game's global camera and controller numbers, read from the install. The field defaults are NEUTRAL values of ours (not
    /// copied from any game file: the world ships no game numbers), used only when a file is missing or not read yet (<see cref="Fallback"/>, read through <see cref="AcSeatData.G"/>) - the ONE place they live.</summary>
    public sealed class AcSeatGlobals
    {
        /// <summary>The built-in values (never loaded; Ready so callers can treat it like a loaded set).</summary>
        public static readonly AcSeatGlobals Fallback = new AcSeatGlobals { Ready = true };

        public volatile bool Ready;
        public string Problem;
        public bool ChaseFromFile, OnboardFromFile, ControlsFromFile;
        // system/cfg/chase_cam.ini [CHASE_0] / [CHASE_1] DISTANCE, HEIGHT, PITCH (rad)
        public readonly float[] ChaseDistance = { 3.5f, 4.5f };          // ours (neutral fallback, file missing)
        public readonly float[] ChaseHeight = { 1.5f, 2.0f };            // ours (neutral fallback, file missing)
        public readonly float[] ChasePitchRad = { 0f, 0f };               // ours (neutral fallback: level, file missing)
        // cfg/camera_onboard.ini [MODE] FOV, IS_WORLD_ALIGNED, [ROTATION] HEAD_MAX_DEGREES
        public float OnboardFov = 60f;                                     // ours (neutral fallback, file missing)
        public bool OnboardWorldAligned;
        public float HeadMaxDegrees = 45f;                                 // ours (neutral fallback, file missing)
        // cfg/controls.ini [KEYBOARD] MINIMUM_STEERING, STEERING_SPEED, STEERING_OPPOSITE_DIRECTION_SPEED, STEER_GAIN, STEER_RESET_SPEED
        public float KeyMinimumSteering = 0f, KeySteeringSpeed = 1.5f, KeyOppositeSpeed = 2f, KeySteerGain = 0.2f, KeyResetSpeed = 2f; // ours (neutral fallbacks, file missing)
        // cfg/controls.ini [X360] STEER_GAMMA, STEER_FILTER, SPEED_SENSITIVITY, STEER_DEADZONE, STEER_SPEED
        public float PadSteerGamma = 2f, PadSteerFilter = 0.5f, PadSpeedSensitivity = 0.4f, PadDeadzone = 0.1f, PadSteerSpeed = 0.25f; // ours (neutral fallbacks, file missing)
    }
}
