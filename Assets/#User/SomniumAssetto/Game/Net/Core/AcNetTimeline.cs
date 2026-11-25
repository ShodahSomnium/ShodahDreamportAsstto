using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// A remote car's samples on the WRITER's clock and the drawn state at local time "now": ETS/EtsNetWire.cs EtsNetTimeline (l.221-492) ported
    /// VERBATIM in its rules (better for 300 km/h than HaloNetTimeline), on <see cref="AcNetCar"/> instead of the truck state:
    ///   - samples are placed on the writer's own millisecond stamp (unwrapped), deduplicated on the record seq;
    ///   - the render lag aims at the best recent arrival lag + the spread above it + 1.1 send intervals + 5 ms, clamped
    ///     <see cref="MinDelay"/> .. <see cref="MaxDelay"/>, slewed at 0.05 s/s (jumps when more than 0.5 s off);
    ///   - positions follow a cubic Hermite through the samples' velocities (a bend is a curve, not chords), rotation nlerp, controls lerped;
    ///   - past the newest sample the car is carried on its velocity up to <see cref="MaxExtrapolation"/>, then held;
    ///   - a new placement epoch (spawn, back to pits, grid, reset) or a jump of more than 50 m is snapped to, never slid to.
    /// Racing tune (ets_vehicles.md 8.5): MinDelay 0.03 s (ETS value) - at 300 km/h every 0.1 s of lag is 8 m behind the truth. Pure C#.
    /// </summary>
    public sealed class AcNetTimeline
    {
        public struct Entry
        {
            public double Time;
            public AcNetCar State;
        }

        private const int Capacity = 64;
        private const int LagCapacity = 64;

        /// <summary>How long a remembered arrival counts toward the lag, seconds (ETS value).</summary>
        private const double LagWindow = 2.5;

        /// <summary>How fast the render lag may change, seconds per second (ETS value).</summary>
        private const double Slew = 0.05;

        /// <summary>A move between two samples this long is a jump, not driving, metres (ETS value).</summary>
        public const float JumpMetres = 50f;

        private readonly Entry[] _samples = new Entry[Capacity];
        private int _count;
        private int _lastSeq = int.MinValue;
        private int _lastEpoch = -1;

        private readonly double[] _arrivals = new double[LagCapacity];
        private readonly double[] _lags = new double[LagCapacity];
        private int _lagCount;
        private int _lagNext;

        private double _renderLag = double.NaN;
        private double _lastNow = double.NaN;
        private long _stampBase;
        private int _lastStamp;
        private bool _haveStamp;
        private bool _snapPending;

        /// <summary>Least time kept between the newest sample and the moment drawn, seconds (ETS value; ets_vehicles 8.5 asks for ~0.03).</summary>
        public float MinDelay = 0.03f;

        /// <summary>Most, so a bad connection shows a late car rather than a very late one (ETS value).</summary>
        public float MaxDelay = 0.4f;

        /// <summary>How far past the newest sample the car is carried on its velocity before it waits (ETS value).</summary>
        public float MaxExtrapolation = 0.25f;

        public int Count { get { return _count; } }

        /// <summary>The render lag over the best one: how far behind the newest data the car is drawn.</summary>
        public float Delay { get; private set; }

        /// <summary>True for the one Sample() result that followed an epoch change / jump.</summary>
        public bool WasSnap { get; private set; }

        /// <summary>Local time of the last accepted sample (NaN before any).</summary>
        public double LastArrival { get; private set; } = double.NaN;

        public void Clear()
        {
            _count = 0;
            _lagCount = 0;
            _lagNext = 0;
            _renderLag = double.NaN;
            _lastEpoch = -1;
            _lastSeq = int.MinValue;
        }

        /// <summary>A state read off the network at local time now, with the record's seq and stamp. Returns false if it was not new.</summary>
        public bool Add(in AcNetCar state, int seq, int stampMs, double now)
        {
            if (seq == _lastSeq) return false;
            _lastSeq = seq;
            double stamp = Unwrap(stampMs);

            // A placement (spawn, back to pits, grid) is a new start: nothing before it is worth sliding from.
            if (state.Epoch != _lastEpoch)
            {
                _lastEpoch = state.Epoch;
                _count = 0;
                _lagCount = 0;
                _lagNext = 0;
                _renderLag = double.NaN;
                _snapPending = true;
            }

            if (_count > 0 && stamp <= _samples[_count - 1].Time) return false;

            // A jump of more than 50 m between two samples is not driving: arrive, do not slide across the track.
            if (_count > 0 && (state.Pos - _samples[_count - 1].State.Pos).SqrLength > JumpMetres * JumpMetres)
            {
                _count = 0;
                _snapPending = true;
            }

            _arrivals[_lagNext] = now;
            _lags[_lagNext] = now - stamp;
            _lagNext = (_lagNext + 1) % LagCapacity;
            _lagCount = Math.Min(_lagCount + 1, LagCapacity);
            LastArrival = now;

            if (_count == Capacity)
            {
                Array.Copy(_samples, 1, _samples, 0, Capacity - 1);
                _count--;
            }

            _samples[_count++] = new Entry { Time = stamp, State = state };
            return true;
        }

        /// <summary>The car at local time now. False until there is anything to show.</summary>
        public bool Sample(double now, out AcNetCar state)
        {
            state = default(AcNetCar);
            if (_count == 0) return false;

            // The lag to aim at: best recent arrival, the spread above it, and one send interval.
            double best = double.MaxValue, worst = double.MinValue;
            for (int i = 0; i < _lagCount; i++)
            {
                if (now - _arrivals[i] > LagWindow) continue;
                best = Math.Min(best, _lags[i]);
                worst = Math.Max(worst, _lags[i]);
            }

            if (best == double.MaxValue) best = worst = _lags[(_lagNext + LagCapacity - 1) % LagCapacity];

            double interval = _count > 1 ? (_samples[_count - 1].Time - _samples[Math.Max(0, _count - 9)].Time) / Math.Min(8, _count - 1) : 0.05;
            double delay = Clamp(worst - best + interval * 1.1 + 0.005, MinDelay, MaxDelay);
            double target = best + delay;
            double step = double.IsNaN(_lastNow) ? 0.0 : Math.Max(0.0, now - _lastNow);
            _lastNow = now;

            if (double.IsNaN(_renderLag) || Math.Abs(target - _renderLag) > 0.5) _renderLag = target;
            else
            {
                double most = Slew * step;
                _renderLag += Clamp(target - _renderLag, -most, most);
            }

            Delay = (float)(_renderLag - best);
            double t = now - _renderLag;
            WasSnap = _snapPending;
            _snapPending = false;

            if (_count == 1 || t <= _samples[0].Time)
            {
                state = _samples[0].State;
                if (_count == 1 && t > _samples[0].Time)
                {
                    // only one sample and past it: carry it on its velocity for a moment (as past the newest below)
                    float ahead = (float)Math.Min(t - _samples[0].Time, MaxExtrapolation);
                    if (!state.Frozen) state.Pos = state.Pos + state.Vel * ahead;
                }

                return true;
            }

            Entry newest = _samples[_count - 1];
            if (t >= newest.Time)
            {
                // Past the newest: carry on along its velocity for a moment, then hold.
                float ahead = (float)Math.Min(t - newest.Time, MaxExtrapolation);
                state = newest.State;
                if (!state.Frozen) state.Pos = newest.State.Pos + newest.State.Vel * ahead;
                return true;
            }

            int b = 1;
            while (b < _count - 1 && _samples[b].Time < t) b++;

            Entry from = _samples[b - 1], to = _samples[b];
            float span = (float)(to.Time - from.Time);
            float u = span > 1e-4f ? Clamp01((float)(t - from.Time) / span) : 1f;

            state = u < 0.5f ? from.State : to.State;
            state.Pos = Hermite(from.State.Pos, from.State.Vel, to.State.Pos, to.State.Vel, span, u);
            state.Rot = AcNetQ.Nlerp(from.State.Rot, to.State.Rot, u);
            state.Vel = AcNetV3.Lerp(from.State.Vel, to.State.Vel, u);
            state.YawRateDeg = Lerp(from.State.YawRateDeg, to.State.YawRateDeg, u);
            state.SteerWheelDeg = Lerp(from.State.SteerWheelDeg, to.State.SteerWheelDeg, u);
            state.SteerLF = Lerp(from.State.SteerLF, to.State.SteerLF, u);
            state.SteerRF = Lerp(from.State.SteerRF, to.State.SteerRF, u);
            state.Rpm = Lerp(from.State.Rpm, to.State.Rpm, u);
            state.Throttle = Lerp(from.State.Throttle, to.State.Throttle, u);
            state.Brake = Lerp(from.State.Brake, to.State.Brake, u);
            state.Clutch = Lerp(from.State.Clutch, to.State.Clutch, u);
            state.BoostBar = Lerp(from.State.BoostBar, to.State.BoostBar, u);
            state.DoorL = Lerp(from.State.DoorL, to.State.DoorL, u);
            state.DoorR = Lerp(from.State.DoorR, to.State.DoorR, u);
            for (int i = 0; i < 4; i++)
            {
                AcNetWheel wa = from.State.Wheel(i), wb = to.State.Wheel(i);
                AcNetWheel w = u < 0.5f ? wa : wb;
                w.TravelM = Lerp(wa.TravelM, wb.TravelM, u);
                w.Skid = Lerp(wa.Skid, wb.Skid, u);
                w.AngularVel = Lerp(wa.AngularVel, wb.AngularVel, u);
                state.SetWheel(i, w);
            }

            // Samples older than the one before the drawn moment are no longer needed.
            if (b > 2)
            {
                int drop = b - 2;
                Array.Copy(_samples, drop, _samples, 0, _count - drop);
                _count -= drop;
            }

            return true;
        }

        /// <summary>Cubic Hermite between two positions with their velocities over a span of seconds.</summary>
        public static AcNetV3 Hermite(AcNetV3 p0, AcNetV3 v0, AcNetV3 p1, AcNetV3 v1, float span, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;
            return p0 * h00 + v0 * (h10 * span) + p1 * h01 + v1 * (h11 * span);
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        private static double Clamp(double value, double low, double high) { return value < low ? low : value > high ? high : value; }

        /// <summary>The writer's millisecond clock, carried past int wrap-round (24 days).</summary>
        private double Unwrap(int stampMs)
        {
            if (_haveStamp && stampMs < _lastStamp && (long)_lastStamp - stampMs > int.MaxValue / 2) _stampBase += 1L << 32;
            _haveStamp = true;
            _lastStamp = stampMs;
            return (_stampBase + (uint)stampMs) / 1000.0;
        }
    }
}
