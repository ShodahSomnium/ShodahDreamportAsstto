namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] Everything the HUD draws in one frame, gathered by <see cref="AcHud"/> from AcLocal (P telemetry, R timing, S seat),
    /// AcSession (R) and the seated car - or set by a lab scenario through <see cref="AcHud.SetFake"/> (so tests never write other
    /// modules' fields). A plain struct: no allocation.
    /// </summary>
    public struct AcHudFeed
    {
        /// <summary>Seated in a car (driving groups shown).</summary>
        public bool Car;
        /// <summary>In the driver seat (the lap times are ours; a passenger sees the gauges and the standings).</summary>
        public bool Driver;
        public float Rpm, LimiterRpm, SpeedKmh, Fuel, MaxFuel, Boost, MaxBoost;
        /// <summary>Shift-light red line (rpm; 0 = the limiter): min(LIMITER, engine.ini [DAMAGE] RPM_THRESHOLD) while DAMAGE         public float Rpm, LimiterRpm, SpeedKmh, Fuel, MaxFuel, Boost, MaxBoost;gt; 0
        /// (DAMAGE_DESIGN 3.3, changelog 1.7; fixer 2026-11-02). At DAMAGE 0 it stays 0 = the limiter, as in iteration 1.</summary>
        public float RedLineRpm;
        public int Gear;
        public bool Limiter, Abs, Tc;

        public AcPhase Phase;
        public AcSessionType Session;
        /// <summary>Race laps (0 = timed / not a lap race).</summary>
        public int TotalLaps;
        /// <summary>Seconds left in a timed session, -1 = no clock.</summary>
        public int SecondsLeft;

        public int Laps, LapMs, LastMs, BestMs, DeltaMs, Sector;
        public bool LapValid, InPitlane, Finished;
        public int Position, Cars;
        public float PitLimitKmh;

        public bool WrongWay, BlueFlag, PenaltyFlag, YellowFlag;
        public bool Mph;

        // ---- added in the second pass (H)
        /// <summary>The session the box names (AcHud: Qualify while R's qualify runs, Hotlap in a hotlap session, the master's session from
        /// the grid on, else Practice). 0 = not set (lab feeds): AcHudView falls back to the phase rule.</summary>
        public AcSessionType ShownSession;
        /// <summary>A racing (non-spectating) car has finished the race: AC shows the chequered flag to everyone from then on.</summary>
        public bool LeaderFinished;
        /// <summary>Practice / qualify / hotlap: no timing line crossed yet (R's AcLocal.OutLap).</summary>
        public bool OutLap;
        /// <summary>Race time at my finish (ms, R's AcLocal.FinishedMs, without penalties).</summary>
        public int FinishedMs;
        /// <summary>The sector split to show: completed sector (1-based, 0 = none), its time, and the delta to the best lap's same sector
        /// (int.MinValue = no best yet).</summary>
        public int SplitSector, SplitMs, SplitDeltaMs;
        /// <summary>Drag / point-to-point layout (R's AcRace.OpenCourse): one run = one "lap", no LAP n/N.</summary>
        public bool OpenCourse;

        // ---- iteration 2: damage displayer (DAMAGE_DESIGN 5)
        /// <summary>Draw the damage displayer this frame (damage on, hud.damage on, within TIME s of a hit on my car or a repair running).</summary>
        public bool Damage;
        /// <summary>Layer values 0..1: body = ZoneByte / MAX_DAMAGE_KMH (front, rear, left, right), engine = EnginePct / 100, suspension =
        /// SuspDamage01 per corner (FL, FR, RL, RR). Gearbox and tyres are drawn at 0 (not simulated).</summary>
        public float DmgFront, DmgRear, DmgLeft, DmgRight, DmgEngine, DmgSusFl, DmgSusFr, DmgSusRl, DmgSusRr;
        /// <summary>damage_displayer.ini PRINT_VALUES: the percentages are printed beside the car.</summary>
        public bool DmgPrint;
        /// <summary>A pit-box repair runs (R): seconds left / total (the displayer also shows a countdown line).</summary>
        public float RepairLeft, RepairTotal;
    }
}
