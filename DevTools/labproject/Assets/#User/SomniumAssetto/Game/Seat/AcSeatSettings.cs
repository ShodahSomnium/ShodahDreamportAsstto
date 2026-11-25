using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] The seat's settings rows (tablet Settings page, group "Driving", drawn by F from the <see cref="AcSettingsUi"/> registry) and
    /// cached values (re-read when <see cref="AcSettings.Changed"/> names one of our keys - no parsing per frame).
    /// </summary>
    public static class AcSeatSettings
    {
        public const string Group = "Driving";
        public const string KeyVrSteer = "seat.vrsteer", KeyHorizon = "vr.horizonlock", KeyPedals = "seat.pedals", KeyCamera = "seat.camera", KeyHaptics = "seat.wheelhaptics", KeyXDown = "seat.vrxdown";

        /// <summary>VR steering: 0 = the wheel with one or both hands (both: the line between them turns it), 1 = one hand only (the first hand on
        /// the rim steers, the other stays free), 2 = left stick. The stick always steers while no hand holds the rim.</summary>
        public static int VrSteer { get; private set; }
        /// <summary>vr.horizonlock: the VR view is ALWAYS level (heading only); off lets a MONITOR body tilt with the car. Default on.</summary>
        public static bool HorizonLock { get; private set; } = true;
        /// <summary>VR pedals: 0 = right trigger gas, left trigger brake; 1 = swapped; 2 = right stick (up gas, down brake).</summary>
        public static int Pedals { get; private set; }
        /// <summary>The monitor camera a seated player starts in (an <see cref="AcCameraMode"/> value).</summary>
        public static int Camera { get; private set; }
        /// <summary>Steering-wheel vibration scale (0 off, 0.6 light, 1 normal, 1.6 strong).</summary>
        public static float Haptics { get; private set; } = 1f;
        /// <summary>VR: left X also shifts down (default off). X is the client's pose-cancel action: every press drops the sitting pose and the seat
        /// re-enters it the next frame (a re-centre undo, possibly a small head jump), so the default downshift is a quick left grip-click.</summary>
        public static bool XShiftsDown { get; private set; }

        private static readonly string[] SteerChoices = { "Wheel (one or two hands)", "Wheel (one hand)", "Left stick" };
        private static readonly string[] PedalChoices = { "Right trigger gas, left trigger brake", "Left trigger gas, right trigger brake", "Right stick (up gas, down brake)" };
        private static readonly string[] CameraChoices = { "Cockpit", "Dash", "Bonnet", "Bumper", "Chase near", "Chase far", "Orbit" };
        private static readonly string[] HapticChoices = { "Normal", "Strong", "Off", "Light" };
        private static readonly float[] HapticScale = { 1f, 1.6f, 0f, 0.6f }; // ours (ETS EtsWheelHaptics steps)
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            AcSettingsUi.Choice(Group, KeyVrSteer, "VR steering", SteerChoices, 0);
            AcSettingsUi.Toggle(Group, KeyHorizon, "Horizon lock (VR view always level; off: the seated body tilts with the car on a monitor)", true);
            AcSettingsUi.Choice(Group, KeyPedals, "VR pedals", PedalChoices, 0);
            AcSettingsUi.Choice(Group, KeyCamera, "Monitor camera", CameraChoices, 0);
            AcSettingsUi.Choice(Group, KeyHaptics, "Steering wheel vibration", HapticChoices, 0);
            AcSettingsUi.Toggle(Group, KeyXDown, "VR: X also shifts down (X is Somnium's pose cancel - each press re-seats you; off: quick left grip-click shifts down)", false);
            Read();
            AcSettings.Changed += OnChanged;
        }

        private static void OnChanged(string key)
        {
            if (key != null && (key.StartsWith("seat.", StringComparison.OrdinalIgnoreCase) || key.Equals(KeyHorizon, StringComparison.OrdinalIgnoreCase))) Read();
        }

        private static void Read()
        {
            VrSteer = Clamp(AcSettings.Int(KeyVrSteer, 0), 0, 2);
            HorizonLock = AcSettings.Flag(KeyHorizon, true);
            Pedals = Clamp(AcSettings.Int(KeyPedals, 0), 0, 2);
            Camera = AcCameraMode.Cycle[Clamp(AcSettings.Int(KeyCamera, 0), 0, AcCameraMode.Cycle.Length - 1)];
            Haptics = HapticScale[Clamp(AcSettings.Int(KeyHaptics, 0), 0, HapticScale.Length - 1)];
            XShiftsDown = AcSettings.Flag(KeyXDown, false);
        }

        private static int _pendingCamera = -1;

        /// <summary>Remembers the camera the player switched to (C key) as the next default - kept in memory and written once on getting out
        /// (<see cref="FlushCamera"/>), not on every press.</summary>
        public static void RememberCamera(int mode)
        {
            if (Array.IndexOf(AcCameraMode.Cycle, mode) >= 0) _pendingCamera = mode;
        }

        /// <summary>Writes the remembered camera (getting out / module stop). No-op when unchanged.</summary>
        public static void FlushCamera()
        {
            int mode = _pendingCamera;
            _pendingCamera = -1;
            int idx = mode >= 0 ? Array.IndexOf(AcCameraMode.Cycle, mode) : -1;
            if (idx < 0) return;
            try { if (AcCameraMode.Cycle[Clamp(AcSettings.Int(KeyCamera, 0), 0, AcCameraMode.Cycle.Length - 1)] != mode) AcSettings.SetInt(KeyCamera, idx); }
            catch (Exception e) { AcLog.WarnOnce("Seat: saving the camera setting failed: " + e.Message); }
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
