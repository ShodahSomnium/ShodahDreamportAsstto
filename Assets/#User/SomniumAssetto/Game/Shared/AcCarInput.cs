namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The driver's controls for one car, written once a frame by the PRODUCER (S's AcDriveInput for the local player's car - VR wheel
    /// grab, triggers, keys, gamepad; I's AcAiDriver for an AI car) into <c>AcCarPhysics.Input</c>, read by P's physics in FixedUpdate.
    /// A plain struct (no allocation). Edges (<see cref="ShiftUp"/>, <see cref="ShiftDown"/>, <see cref="ResetRequest"/>) are consumed by
    /// the physics: the producer sets them true for one frame; P clears them after acting.
    /// </summary>
    public struct AcCarInput
    {
        /// <summary>Relative steering -1 (full left) .. +1 (full right) = fraction of car.ini STEER_LOCK. Used when <see cref="SteerAbsolute"/> is false.</summary>
        public float Steer;
        /// <summary>True when <see cref="SteerWheelDeg"/> is the steering-wheel angle the VR hands hold (absolute); false for keys / stick (relative, P applies the speed-sensitive rate).</summary>
        public bool SteerAbsolute;
        /// <summary>Steering-WHEEL angle in degrees (right positive), clamped by S to +-STEER_LOCK; road wheel angle = this / STEER_RATIO (P).</summary>
        public float SteerWheelDeg;
        /// <summary>0..1 gas pedal.</summary>
        public float Throttle;
        /// <summary>0..1 brake pedal.</summary>
        public float Brake;
        /// <summary>0..1 clutch pedal (1 = fully pressed / disengaged). Ignored with auto-clutch.</summary>
        public float Clutch;
        /// <summary>0..1 handbrake.</summary>
        public float Handbrake;
        /// <summary>Edge: one gear up (paddle / key) this frame.</summary>
        public bool ShiftUp;
        /// <summary>Edge: one gear down this frame.</summary>
        public bool ShiftDown;
        /// <summary>Requested gear for an H-shifter (-1 R, 0 N, 1..); <see cref="NoGearRequest"/> = none (sequential shifting only).</summary>
        public int GearRequest;
        /// <summary>Headlights wanted on.</summary>
        public bool Lights;
        /// <summary>Horn held.</summary>
        public bool Horn;
        /// <summary>Edge: "put me back on the track" (R key / tablet "Back to pits" is R's job; this is the in-place reset).</summary>
        public bool ResetRequest;

        public const int NoGearRequest = int.MinValue;

        /// <summary>Neutral controls (no pedals, wheel straight, no gear request).</summary>
        public static AcCarInput Neutral { get { return new AcCarInput { GearRequest = NoGearRequest }; } }
    }
}
