namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[R, Race] The local player's timing and position (AcTiming writes; H, N, F read). Times in milliseconds, 0 = none. Owner: R.</summary>
    public static partial class AcLocal
    {
        /// <summary>Completed laps this session (race: since the green, with the T3 grid rule).</summary>
        public static int Laps;
        /// <summary>Current lap time so far.</summary>
        public static int LapMs;
        /// <summary>Last completed lap.</summary>
        public static int LastMs;
        /// <summary>Best VALID lap this session.</summary>
        public static int BestMs;
        /// <summary>Live delta to the best lap (ms, negative = faster).</summary>
        public static int DeltaMs;
        /// <summary>Current sector (0-based, gate n -> sector n).</summary>
        public static int Sector;
        /// <summary>The current lap is still valid (no cut: ALLOWED_TYRES_OUT rule).</summary>
        public static bool LapValid = true;
        /// <summary>Race position (1-based; 0 = not racing).</summary>
        public static int Position;
        /// <summary>Race distance in laps = laps + fast_lane progress (research T3); positions sort by it.</summary>
        public static float RaceDistance;
        /// <summary>On an IS_PITLANE surface (pit limiter applies).</summary>
        public static bool InPitlane;
        /// <summary>Finished the race (crossed the line after the leader finished / laps done).</summary>
        public static bool Finished;

        // ---- added by R (N may publish them: record ints 24-35 of its plan) ----
        /// <summary>Race time at the finish (ms from the green, without penalties); 0 = not finished.</summary>
        public static int FinishedMs;
        /// <summary>Time penalties this race (ms): jump start (JUMP_START_PENALTY 2 -> +5 s, ours).</summary>
        public static int PenaltyMs;
        /// <summary>Cuts this session (ALLOWED_TYRES_OUT rule) / on the current lap.</summary>
        public static int Cuts, LapCuts;
        /// <summary>Still on the out lap (practice / qualify / hotlap: no timing line crossed yet).</summary>
        public static bool OutLap = true;
        /// <summary>Driving against the race direction (fast lane) for 2 s (HUD "wrong way").</summary>
        public static bool WrongWay;
        /// <summary>Fast-lane progress 0..1 (0 = the AC_TIME_0 line; NaN without a line) - track map / tower.</summary>
        public static float Progress = float.NaN;
        /// <summary>Sector times of the last completed lap (ms; one per timing gate, gate n ends sector n-1, gate 0 the last). Reused array.</summary>
        public static int[] LastSectorMs = new int[0];
        /// <summary>My grid slot in the running race (0 = pole, -1 = none / spectating).</summary>
        public static int GridSlot = -1;
        /// <summary>The race-control command seq this client applied (N plan: record int 35).</summary>
        public static int CommandSeq;
    }
}
