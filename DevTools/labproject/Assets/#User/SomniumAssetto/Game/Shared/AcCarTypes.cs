using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Car side (doors, getting out). AC +X = car LEFT; Unity x is mirrored (Unity -x = car left), see ac_car_formats.md 1.5.</summary>
    public enum AcSide { Left = 0, Right = 1 }

    /// <summary>Which seat of a car (AC cars have a driver seat and, in road cars, one passenger seat).</summary>
    public enum AcSeatKind { Driver = 0, Passenger = 1 }

    /// <summary>Who drives a built car: the local player's physics, a remote player's record (kinematic puppet), or the master's AI.</summary>
    public enum AcCarKind { Local = 0, Remote = 1, Ai = 2, Showroom = 3 }

    /// <summary>Car light / indicator state (lights.ini groups; networked as one int by N). Bits are ours, keep them stable (wire format).</summary>
    [Flags]
    public enum AcLightState
    {
        None = 0,
        Headlights = 1 << 0,
        Brake = 1 << 1,
        Reverse = 1 << 2,
        HighBeam = 1 << 3,
        Limiter = 1 << 4,     // shift / limiter LEDs (digital_instruments LED_n)
        PitLimiter = 1 << 5,
        Horn = 1 << 6,
        Hazard = 1 << 7,
    }

    /// <summary>Values the cockpit instruments show (analog needles, digital displays, LEDs). Filled from <see cref="AcTelemetry"/> by
    /// whoever drives the car (P for local/AI, N for remote) and handed to <c>AcCarModel.SetInstruments</c>.</summary>
    public struct AcInstruments
    {
        public float Rpm;
        public float SpeedKmh;
        /// <summary>-1 reverse, 0 neutral, 1.. forward gears.</summary>
        public int Gear;
        public float FuelLitres;
        public float TurboBar;
        public float WaterTempC;
        public float LapTimeS;
        public bool Limiter;
    }
}
