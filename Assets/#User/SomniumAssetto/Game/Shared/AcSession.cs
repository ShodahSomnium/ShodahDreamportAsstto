using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Session phases. Hub = no track; Loading = track being built; Practice = free driving on the loaded track (also hotlap and the
    /// state between races); Grid = racers placed on AC_START_n and locked; Countdown = start lights; Racing; Finished = the leader finished,
    /// others completing their lap; Results = table shown, then back to Practice.</summary>
    public enum AcPhase { Hub, Loading, Practice, Grid, Countdown, Racing, Finished, Results }

    /// <summary>AC's own session type numbers (career event.ini comment: Practice 1, Qualify 2, Race 3, Hotlap 4) - keep the values.</summary>
    public enum AcSessionType { Practice = 1, Qualify = 2, Race = 3, Hotlap = 4 }

    /// <summary>
    /// The race rules the instance master picks in the tablet lobby (F edits, N carries in the race-control block, R plays them). Names and
    /// defaults follow AC's own server_cfg.ini / race.ini keys (research ac_physics_sound_rules.md 3.1-3.2) so AC players recognise them.
    /// </summary>
    [Serializable]
    public sealed class AcRules
    {
        public AcSessionType Session = AcSessionType.Practice;
        /// <summary>[RACE] LAPS (0 = timed race using <see cref="TimeMinutes"/>). Server preset default 5.</summary>
        public int Laps = 5;
        /// <summary>DURATION_MINUTES / [PRACTICE|QUALIFY] TIME (0 = unlimited practice). Server preset default 10.</summary>
        public int TimeMinutes = 10;
        /// <summary>ABS_ALLOWED: 0 forbidden, 1 car's factory setting, 2 any (server_cfg values; preset 1).</summary>
        public int AbsAllowed = 1;
        /// <summary>TC_ALLOWED: 0 forbidden, 1 factory, 2 any (preset 1).</summary>
        public int TcAllowed = 1;
        /// <summary>STABILITY_ALLOWED (preset 0).</summary>
        public bool StabilityAllowed = false;
        /// <summary>AUTOCLUTCH_ALLOWED (preset 0 in server_cfg; we default to 1 for VR players without a clutch pedal). // ours</summary>
        public bool AutoClutchAllowed = true;
        /// <summary>Automatic gearbox allowed (AC assists AUTO_SHIFTER; no server key - ours).</summary>
        public bool AutoShiftAllowed = true;
        /// <summary>TYRE_BLANKETS_ALLOWED (preset 0).</summary>
        public bool TyreBlanketsAllowed = false;
        /// <summary>[LAP_INVALIDATOR] ALLOWED_TYRES_OUT (server default 2; -1 disables the cut rule).</summary>
        public int AllowedTyresOut = 2;
        /// <summary>JUMP_START_PENALTY: 0 none, 1 teleport to pits, 2 drive-through (career events mostly 2). We apply 2 as +5 s. // ours</summary>
        public int JumpStartPenalty = 2;
        /// <summary>FUEL_RATE % (100 = real consumption, 0 = none).</summary>
        public int FuelRate = 100;
        /// <summary>DAMAGE_MULTIPLIER % (0 = damage off = exactly iteration 1). The lobby default comes from server_cfg.ini [SERVER]
        /// DAMAGE_MULTIPLIER through AcRaceDefaults (100 in the install); a bare <c>new AcRules()</c> stays 0 (off). DAMAGE_DESIGN 1.4.</summary>
        public int DamageMultiplier = 0;
        /// <summary>TYRE_WEAR_RATE %.</summary>
        public int TyreWearRate = 100;
        /// <summary>RACE_OVER_TIME seconds after the winner before the session closes (server_cfg.ini value 180).</summary>
        public int RaceOverTimeSeconds = 180;
        /// <summary>Pit lane speed limit km/h (no AC key exists anywhere in the install - research addendum). // ours</summary>
        public float PitLimitKmh = 80f;
        /// <summary>Allowed car ids (empty = any owned car).</summary>
        public List<string> AllowedCars = new List<string>();
        /// <summary>AI opponents the master spawns (module I; 0 = none).</summary>
        public int AiCount = 0;
        /// <summary>AI_LEVEL 0..100 (career events use 80-100).</summary>
        public int AiLevel = 90;

        public AcRules Clone()
        {
            var r = (AcRules)MemberwiseClone();
            r.AllowedCars = new List<string>(AllowedCars);
            return r;
        }

        public override string ToString()
        {
            return Session + (Session == AcSessionType.Race && Laps > 0 ? ", " + Laps + " laps" : ", " + TimeMinutes + " min")
                + (DamageMultiplier > 0 ? ", damage " + DamageMultiplier + " %" : "") + ", ABS " + AbsAllowed + ", TC " + TcAllowed + ", tyres out " + AllowedTyresOut + ", AI " + AiCount
                + (AllowedCars.Count > 0 ? ", cars " + AllowedCars.Count : "");
        }
    }

    /// <summary>One completed lap (raised with <see cref="AcSession.LapCompleted"/>).</summary>
    public sealed class AcLap
    {
        /// <summary>Who: Photon id of the driver (-1 = local offline; AI cars use -(100 + index)). // ours convention</summary>
        public int PlayerId;
        public bool IsLocal;
        public string Name = "";
        /// <summary>1-based lap number.</summary>
        public int Lap;
        public int TimeMs;
        /// <summary>Sector times (ms), one per timing gate (gate n ends sector n-1; gate 0 ends the last sector).</summary>
        public int[] SectorMs = new int[0];
        public bool Valid = true;
        /// <summary>Number of cuts on this lap (more than ALLOWED_TYRES_OUT wheels off IS_VALID_TRACK).</summary>
        public int Cuts;
    }

    /// <summary>One line of the standings / results (R computes; H's tower and F's timing page show it).</summary>
    public sealed class AcStanding
    {
        public int Position;
        public int PlayerId;
        public bool IsLocal;
        public bool IsAi;
        public string Name = "";
        public string CarId = "";
        public int Laps;
        public int BestMs, LastMs;
        /// <summary>Gap to the leader in ms (or laps behind when <see cref="LapsBehind"/> &gt; 0).</summary>
        public int GapMs;
        public int LapsBehind;
        public float RaceDistance;
        public bool Finished;
        /// <summary>Total race time (finished cars), ms.</summary>
        public int TotalMs;
        public int PenaltyMs;
        public bool InPit;
        /// <summary>Current lap time so far (ms; live timing page).</summary>
        public int LapMs;
        /// <summary>Current sector (0-based).</summary>
        public int Sector;
        /// <summary>The current lap is still valid (no cut).</summary>
        public bool LapValid = true;
        /// <summary>Grid slot of this car in the running race (0 = pole, -1 = none).</summary>
        public int GridSlot = -1;
        /// <summary>Not racing (late joiner during a race): shown greyed.</summary>
        public bool Spectating;
        /// <summary>Retired from the running race (engine blown, DAMAGE_DESIGN 4): classified DNF, ranked after the running cars. Iteration 2.</summary>
        public bool Retired;
    }

    /// <summary>
    /// The race-control command the instance master publishes (N carries it in the "Ac Net World" blocks; every client - late joiners too -
    /// applies the newest <see cref="Seq"/> through AcRace.Apply). Written by R on the master (through AcNetGame.RaceControl), never by others.
    /// </summary>
    public sealed class AcRaceCommand
    {
        /// <summary>Grows with every change (above any seen, so it keeps growing across a master hand-over).</summary>
        public int Seq;
        public AcPhase Phase = AcPhase.Hub;
        public string TrackId = "";
        public string LayoutId = "";
        public AcRules Rules = new AcRules();
        /// <summary>Green-light time on the shared clock (UTC unix seconds; 0 = not scheduled).</summary>
        public double StartUtc;
        /// <summary>Grid order: Photon ids (players) / -(100+i) (AI), pole first.</summary>
        public List<int> Grid = new List<int>();
        /// <summary>Photon id of the master who wrote it.</summary>
        public int MasterId = -1;
        /// <summary>When the master entered <see cref="Phase"/> (UTC unix seconds; 0 = unknown -> each client uses the time it applied it).
        /// Results: the results screen runs from here. Added by R (N carries it in World 0 when it can).</summary>
        public double PhaseUtc;

        public AcRaceCommand Clone()
        {
            var c = (AcRaceCommand)MemberwiseClone();
            c.Rules = Rules != null ? Rules.Clone() : new AcRules();
            c.Grid = new List<int>(Grid);
            return c;
        }

        public override string ToString()
        {
            return "seq " + Seq + " " + Phase + " " + (string.IsNullOrEmpty(LayoutId) ? TrackId : TrackId + "/" + LayoutId) + " (" + Rules + ")"
                + (StartUtc > 0 ? ", start UTC " + StartUtc.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "") + (Grid.Count > 0 ? ", grid " + Grid.Count : "")
                + ", master " + MasterId;
        }
    }

    /// <summary>
    /// The shared state of the current session: phase, track + layout, rules, the world root. Owned by R (Game/Race): only R calls the
    /// setters; everyone else READS it and subscribes to the events. Events are raised on the main thread, each handler on its own in
    /// try/catch (one that throws is logged, the rest still run).
    /// </summary>
    public static class AcSession
    {
        public static AcPhase Phase { get; private set; } = AcPhase.Hub;
        public static AcTrackEntry Track { get; private set; }
        public static AcLayoutEntry Layout { get; private set; }
        public static AcRules Rules { get; private set; } = new AcRules();
        /// <summary>Everything built for the session hangs under this (W's track root; destroyed on unload).</summary>
        public static Transform WorldRoot { get; private set; }
        /// <summary>Green-light time on the shared clock (UTC unix seconds), 0 = none.</summary>
        public static double StartUtc { get; set; }
        /// <summary>Loading progress 0..1 (valid in <see cref="AcPhase.Loading"/>).</summary>
        public static float LoadProgress { get; private set; }

        /// <summary>Phase changed (old, new).</summary>
        public static event Action<AcPhase, AcPhase> PhaseChanged;
        /// <summary>Track build progress (0..1, stage text) - F's loading card follows it.</summary>
        public static event Action<float, string> TrackLoading;
        /// <summary>The track is built (collision, markers, surfaces ready): R spawns cars, I spawns AI, N draws remote cars.</summary>
        public static event Action TrackBuilt;
        /// <summary>The track is about to be torn down (root still alive): modules drop what they built on it.</summary>
        public static event Action Unloading;
        /// <summary>Green light: the race clock runs.</summary>
        public static event Action RaceStarted;
        /// <summary>A lap was completed (any car whose laps R times or receives).</summary>
        public static event Action<AcLap> LapCompleted;
        /// <summary>The race is over for everyone (results final).</summary>
        public static event Action RaceFinished;

        /// <summary>UTC now in unix seconds - the clock every client shares (start lights, race start): this PC's clock plus
        /// <see cref="ClockOffset"/>, i.e. the instance master's clock (PC clocks can be seconds apart; a fast clock would see the green early).</summary>
        public static double UtcNow { get { return LocalUtcNow + ClockOffset; } }

        /// <summary>This PC's own UTC clock (unix seconds), without the master offset. Added by R.</summary>
        public static double LocalUtcNow { get { return (DateTime.UtcNow - Epoch).TotalSeconds; } }

        /// <summary>Seconds the instance master's clock is ahead of this PC's (R estimates it from the master's clock beacon, AcRaceClock;
        /// 0 on the master / alone). Added by R.</summary>
        public static double ClockOffset { get; private set; }

        /// <summary>[R] Sets <see cref="ClockOffset"/>.</summary>
        public static void SetClockOffset(double seconds) { ClockOffset = double.IsNaN(seconds) || double.IsInfinity(seconds) ? 0.0 : seconds; }

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A track is loaded and drivable (practice or any race phase).</summary>
        public static bool OnTrack { get { return Phase >= AcPhase.Practice; } }
        /// <summary>A race is in progress (grid .. finished).</summary>
        public static bool InRace { get { return Phase >= AcPhase.Grid && Phase <= AcPhase.Finished; } }

        // ---- R only ------------------------------------------------------------------------------------------------

        /// <summary>[R] Moves to a new phase and raises <see cref="PhaseChanged"/> (no-op when unchanged).</summary>
        public static void SetPhase(AcPhase p)
        {
            if (p == Phase) return;
            AcPhase old = Phase; Phase = p;
            AcLog.Info("Session: " + old + " -> " + p);
            var h = PhaseChanged; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcPhase, AcPhase>)d)(old, p); }
                catch (Exception e) { AcLog.Error("Session: a PhaseChanged handler failed: " + e); }
            }
        }

        /// <summary>[R] A track load begins: remembers track, layout, rules (phase is set separately).</summary>
        public static void Begin(AcTrackEntry track, AcLayoutEntry layout, AcRules rules)
        {
            Track = track; Layout = layout; Rules = rules ?? new AcRules(); LoadProgress = 0f;
        }

        /// <summary>[R] Replaces the rules (the master changed them in the lobby / a new command arrived).</summary>
        public static void SetRules(AcRules rules) { Rules = rules ?? new AcRules(); }

        /// <summary>[R] Reports build progress (raises <see cref="TrackLoading"/>).</summary>
        public static void ReportLoading(float progress, string stage)
        {
            LoadProgress = progress;
            var h = TrackLoading; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<float, string>)d)(progress, stage); }
                catch (Exception e) { AcLog.Error("Session: a TrackLoading handler failed: " + e); }
            }
        }

        /// <summary>[R] The track is built under <paramref name="root"/> (raises <see cref="TrackBuilt"/>).</summary>
        public static void NotifyTrackBuilt(Transform root) { WorldRoot = root; LoadProgress = 1f; Raise(TrackBuilt, "TrackBuilt"); }

        /// <summary>[R] Before the track is destroyed (raises <see cref="Unloading"/>), then forgets track/layout/root.</summary>
        public static void Close()
        {
            Raise(Unloading, "Unloading");
            Track = null; Layout = null; WorldRoot = null; StartUtc = 0; LoadProgress = 0f;
        }

        /// <summary>[R] Green light.</summary>
        public static void NotifyRaceStarted() { Raise(RaceStarted, "RaceStarted"); }

        /// <summary>[R] Results are final.</summary>
        public static void NotifyRaceFinished() { Raise(RaceFinished, "RaceFinished"); }

        /// <summary>[R] A lap was completed.</summary>
        public static void NotifyLap(AcLap lap)
        {
            var h = LapCompleted; if (h == null || lap == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcLap>)d)(lap); }
                catch (Exception e) { AcLog.Error("Session: a LapCompleted handler failed: " + e); }
            }
        }

        private static void Raise(Action handlers, string what)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { AcLog.Error("Session: a " + what + " handler failed: " + e); }
            }
        }
    }
}
