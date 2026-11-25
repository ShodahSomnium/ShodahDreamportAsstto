namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One wheel's state as P's physics measured it this step (or N decoded it for a remote car). Index order everywhere: 0 LF, 1 RF, 2 LR, 3 RR (AC's WHEEL_INDEX).</summary>
    public struct AcWheelState
    {
        /// <summary>Visual spin angle in degrees (wraps), for C's WHEEL_xx rotation and rim blur.</summary>
        public float SpinDeg;
        /// <summary>Wheel angular speed, rad/s.</summary>
        public float AngularVel;
        /// <summary>Road-wheel steer angle in degrees (fronts), right positive.</summary>
        public float SteerDeg;
        /// <summary>Suspension travel in metres from static (positive = compressed), for C's SUSP_xx and the net record.</summary>
        public float TravelM;
        /// <summary>Longitudinal slip ratio (signed).</summary>
        public float SlipRatio;
        /// <summary>Slip angle in degrees.</summary>
        public float SlipAngleDeg;
        /// <summary>Vertical load in newtons.</summary>
        public float LoadN;
        /// <summary>0..1 how much the tyre slides (combined slip past the peak) - skid sound / smoke / HUD.</summary>
        public float Skid;
        /// <summary>The tyre touches ground.</summary>
        public bool Contact;
        /// <summary>The surface under the tyre (null in the air / unknown). From AcSurfaceTag on the hit collider (W).</summary>
        public AcSurface Surface;
    }

    /// <summary>
    /// What a car is doing, written by P's physics every FixedUpdate (local / AI cars) or by N from the record (remote cars), read by A
    /// (engine sound), H (HUD), C (instruments via AcInstruments), N (record), R (timing: speed, pit lane). A plain struct: copy it, do not
    /// keep references. Wheels are four fields (no array: no allocation, no shared reference when copied).
    /// </summary>
    public struct AcTelemetry
    {
        public float Rpm;
        /// <summary>engine.ini [ENGINE_DATA] LIMITER (rpm) of this car.</summary>
        public float LimiterRpm;
        /// <summary>engine.ini [ENGINE_DATA] MINIMUM (idle rpm).</summary>
        public float IdleRpm;
        /// <summary>-1 reverse, 0 neutral, 1.. forward.</summary>
        public int Gear;
        public int GearCount;
        public float SpeedKmh;
        public float Throttle;
        public float Brake;
        public float Clutch;
        /// <summary>Steering-wheel angle in degrees (right positive) - C turns STEER_HR by this.</summary>
        public float SteerWheelDeg;
        /// <summary>Turbo boost in bar (0 = none).</summary>
        public float BoostBar;
        public float FuelLitres;
        /// <summary>The rev limiter is cutting (drives the limiter sound and LEDs).</summary>
        public bool Limiter;
        public bool AbsActive;
        public bool TcActive;
        /// <summary>A gear change is in progress (CHANGE_UP/DN_TIME).</summary>
        public bool Shifting;
        /// <summary>Lateral / longitudinal acceleration in g (HUD, AI).</summary>
        public float LatG, LongG;
        public AcLightState Lights;
        public AcWheelState LF, RF, LR, RR;

        /// <summary>Wheel by index (0 LF, 1 RF, 2 LR, 3 RR).</summary>
        public AcWheelState Wheel(int i)
        {
            switch (i) { case 0: return LF; case 1: return RF; case 2: return LR; default: return RR; }
        }

        /// <summary>Sets a wheel by index (0 LF, 1 RF, 2 LR, 3 RR).</summary>
        public void SetWheel(int i, AcWheelState w)
        {
            switch (i) { case 0: LF = w; break; case 1: RF = w; break; case 2: LR = w; break; default: RR = w; break; }
        }

        /// <summary>The instrument values for C's cockpit (needles, displays).</summary>
        public AcInstruments Instruments
        {
            get { return new AcInstruments { Rpm = Rpm, SpeedKmh = SpeedKmh, Gear = Gear, FuelLitres = FuelLitres, TurboBar = BoostBar, Limiter = Limiter }; }
        }
    }
}
