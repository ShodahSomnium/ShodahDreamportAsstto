using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] One clock for the whole instance. Start lights, the green, the jump-start window, qualify / results timeouts all compare the
    /// master's UTC instants (AcRaceCommand.StartUtc / PhaseUtc) with <see cref="AcSession.UtcNow"/>; PC clocks drift seconds apart, and a client
    /// whose clock runs 2 s fast would see the green 2 s early - a real head start. So the master publishes a clock beacon in N's live race
    /// state block (Race 0, R's own record: <see cref="AcNetRaceState.Flags"/> = beacon bit + the master's clock in ms modulo 2^30, plus the
    /// light / session / leader-finished times) once a second, and every other client estimates
    /// <see cref="AcSession.ClockOffset"/> = master clock - my clock from it.
    /// Each sample is (master clock when written) - (my clock when I first saw it) = offset - delivery delay, so it can only UNDER-estimate:
    /// the estimate is the largest of the last <see cref="Samples"/> samples (the one that travelled fastest). A client therefore runs at most
    /// one network delivery (~0.05-0.2 s) behind the master - never ahead of it. A new master keeps its offset (its beacon carries the old
    /// master's clock on), so a hand-over does not jump the race clock. No allocation per frame.
    /// </summary>
    public static class AcRaceClock
    {
        /// <summary>Beacon period on the master (s). // ours</summary>
        public const float BeaconSeconds = 1f; // ours
        /// <summary>Samples kept for the max estimate (= seconds of history at one beacon per second). // ours</summary>
        public const int Samples = 8; // ours
        /// <summary>Flags bit 30 = this record carries a beacon; bits 0..29 = the master's clock ms modulo 2^30 (12.4 days).</summary>
        public const int BeaconBit = 1 << 30;
        private const long Wrap = 1L << 30;

        private static readonly double[] Ring = new double[Samples];
        private static int _count, _next, _lastFlags, _lastMaster = int.MinValue, _logs;
        private static float _nextBeacon;
        private static bool _wasMaster;
        private static double _loggedOffset = double.NaN;

        /// <summary>Samples taken from the current master (0 = no estimate yet; the offset is then this PC's own clock).</summary>
        public static int SampleCount { get { return _count; } }

        /// <summary>Once a frame (AcRace.Tick, hub too, so the offset is settled before any race).</summary>
        public static void Tick()
        {
            bool up, master;
            int masterId;
            try { up = AcNetGame.Up && AcNetGame.Online; master = AcNetGame.IsMaster; masterId = AcNetGame.MasterId; }
            catch (Exception) { return; }
            if (!up) { _wasMaster = false; return; }
            if (master)
            {
                if (!_wasMaster) { _wasMaster = true; Reset(); if (AcSession.ClockOffset != 0.0) AcLog.Info("Race: clock: I am the master now - keeping the instance clock (" + Ms(AcSession.ClockOffset) + " from mine)"); }
                if (Time.unscaledTime >= _nextBeacon) { _nextBeacon = Time.unscaledTime + BeaconSeconds; Publish(); }
                return;
            }
            _wasMaster = false;
            if (masterId != _lastMaster) { _lastMaster = masterId; Reset(); }
            AcNetRaceState s;
            int seq;
            bool have;
            try { have = AcNetGame.TryGetRaceState(out s, out seq); } catch (Exception) { return; }
            if (!have || (s.Flags & BeaconBit) == 0 || s.Flags == _lastFlags) return;
            _lastFlags = s.Flags;
            long mine = (long)Math.Floor(AcSession.LocalUtcNow * 1000.0) % Wrap;
            long d = ((s.Flags & (BeaconBit - 1)) - mine) % Wrap;
            if (d >= Wrap / 2) d -= Wrap; else if (d < -Wrap / 2) d += Wrap;
            Ring[_next] = d / 1000.0;
            _next = (_next + 1) % Samples;
            if (_count < Samples) _count++;
            double est = double.NegativeInfinity;
            for (int i = 0; i < _count; i++) if (Ring[i] > est) est = Ring[i];
            AcSession.SetClockOffset(est);
            if (double.IsNaN(_loggedOffset) || (Math.Abs(est - _loggedOffset) > 0.25 && _logs < 10)) // ours: log changes above 250 ms
            {
                _logs++;
                _loggedOffset = est;
                AcLog.Info("Race: clock: the master's clock is " + Ms(est) + " from mine (" + _count + " beacon sample(s); start lights follow the master's clock)");
            }
        }

        private static void Reset() { _count = 0; _next = 0; _lastFlags = 0; }

        /// <summary>The master's live race state: light times of the running race, session clock, leader finish, and the clock beacon.</summary>
        private static void Publish()
        {
            double now = AcSession.UtcNow, start = AcSession.StartUtc;
            var s = new AcNetRaceState
            {
                LightsUtc = AcSession.InRace && start > 0 ? AcRaceMath.LightsStartUtc(start) : 0.0,
                Reds = AcRaceMath.RedLights,
                StepMs = (int)Math.Round(AcRaceMath.RedStepSeconds * 1000.0),
                SessionUtc = start,
                LeaderFinishedUtc = AcRace.FirstFinishUtc,
                Flags = BeaconBit | (int)((long)Math.Floor(now * 1000.0) % Wrap),
            };
            try { AcNetGame.PublishRaceState(s); }
            catch (Exception e) { AcLog.WarnOnce("Race: clock beacon could not be published: " + e.Message); }
        }

        private static string Ms(double s) { return (s >= 0 ? "+" : "") + (s * 1000.0).ToString("0") + " ms"; }
    }
}
