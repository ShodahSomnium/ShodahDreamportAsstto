using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The speed an AI car aims for at every point of a line, computed for THIS car on THIS line (quasi-steady lap simulation):
    /// corner limit from the line curvature with the car's tyre grip + downforce, then a backward pass (braking zones, brake capability x
    /// ai.ini BRAKE_HINT, friction circle softened by TRAIL_HINT) and a forward pass (traction / power per gear, drag, rolling resistance).
    ///
    /// Why not the spline's payload speed: it is the speed of whatever car recorded the line and differs wildly between tracks (magione
    /// 83.8 s/lap, a realistic race pace; monza 361.6 s, a crawl at 7-31 m/s) [VERIFIED harness <c>aiprof</c> / <c>ailine</c>] - AC's
    /// AI clearly does not drive that speed. The payload gas/brake hints are kept for the probe only.
    ///
    /// The layout's ai_hints.ini (<see cref="AcAiHints"/>, Kunos' per-track corrections) multiplies the corner speed (HINT) and the
    /// braking (BRAKEHINT) over its progress ranges [BELIEF semantics]. Crests (<see cref="AcAiTrack.VCurv"/> &lt; 0) unload the car by
    /// v^2 x |curvature|: less grip in corners and braking zones on a crest (compressions are not credited - margin) // ours.
    ///
    /// AI_LEVEL (0..100, race.ini / AcRules.AiLevel) scales the grip the plan uses (<see cref="GripUse"/>). Unity-free, no allocation
    /// after construction; rebuilt when the level changes.
    /// </summary>
    public sealed class AcAiPlan
    {
        public readonly AcAiTrack Track;
        public readonly AcAiCar Car;
        public readonly int Level;
        public readonly AcAiHints Hints;
        /// <summary>Target speed (m/s) per line point.</summary>
        public readonly float[] V;
        /// <summary>Corner limit (m/s) per line point before the passes.</summary>
        public readonly float[] VCorner;
        /// <summary>ai_hints.ini multipliers per point (1 = none): corner speed (HINT) and braking (BRAKEHINT).</summary>
        public readonly float[] SpeedHint, BrakeHint;
        /// <summary>Planned lap time (s) at <see cref="V"/>.</summary>
        public float LapTime;
        /// <summary>Fuel one planned lap burns at FUEL_RATE 100 (litres; car.ini CONSUMPTION: rpm x gas x CONSUMPTION / 1000 per second,
        /// gas estimated from the planned acceleration, rpm in the gear the AI holds); 0 when the car has no CONSUMPTION.</summary>
        public float FuelPerLap;
        /// <summary>Lateral grip fraction used (of the tyre's peak at static load) and the surface friction assumed.</summary>
        public float GripUse, SurfaceGrip;
        /// <summary>Speed cap (m/s, pit limiter) or +inf.</summary>
        public float SpeedCap;
        /// <summary>Points slowed by ai_hints / by a crest (for the log).</summary>
        public int HintedPoints, CrestPoints;

        /// <summary>Fraction of the tyre's peak grip (DY_REF / DX_REF at static load) the AI uses at level 100 (load transfer, camber, combined
        /// slip and a margin). // ours (calibrated against P's physics, polish pass 2026-11-01)
        /// Was 0.87 (tuned on the harness' simple car against Kunos' hotlap golds). P's chassis - the physics the AI really drives - gives
        /// 0.78-0.85 x that mu at its limit (harness pchassis steer ramp @100 km/h: ks_toyota_gt86 0.96 g of mu 1.22, ferrari_458 1.07 of 1.26,
        /// abarth500 0.98 of 1.26, bmw_m3_e30 0.98 of 1.24, ks_audi_r8_plus 1.01 of 1.24), so 0.87 planned corners faster than the car can turn
        /// at all: it understeered wide and hit the grip circle (aichassis gt86 magione: under target 44 %, saturated 2 %). At 0.78 (aichassis,
        /// P's chassis, lap 2): gt86 magione 1:40.5 (x1.060, max 0.9 m beyond the room; was x1.092 / 1.8 m), gt86 imola 2:27.9 x1.061 with no
        /// reset (was 2:31.7 x1.124, 2 resets, 10.1 m out), abarth500 magione 1:36.5 (was 1:37.0), ferrari_458 magione 1:31.9 (was 1:30.9, 3.5 -> 2.8 m).
        /// If P's tyres change, re-measure (pchassis) and move this one number.</summary>
        public static float GripAtLevel100 = 0.78f;
        /// <summary>Grip fraction at level 0 relative to level 100. // ours</summary>
        public const float GripAtLevel0 = 0.65f;
        /// <summary>Least vertical load a crest may leave in the plan (fraction of g). // ours</summary>
        public const float CrestMinG = 0.35f;

        /// <summary>Plans <paramref name="car"/> on <paramref name="track"/> at <paramref name="level"/> (0..100).
        /// <paramref name="surfaceGrip"/> = the road's FRICTION from surfaces.ini (~0.97-1.0), <paramref name="speedCap"/> m/s (pit limit) or +inf.</summary>
        public AcAiPlan(AcAiTrack track, AcAiCar car, int level, float surfaceGrip = 1f, float speedCap = float.PositiveInfinity)
            : this(track, car, level, surfaceGrip, speedCap, null) { }

        /// <summary>As above with the layout's ai_hints.ini (null = none; only for the fast lane - its ranges are fast-lane progress).</summary>
        public AcAiPlan(AcAiTrack track, AcAiCar car, int level, float surfaceGrip, float speedCap, AcAiHints hints)
            : this(track, car, level, surfaceGrip, speedCap, hints, -1f, 0f) { }

        /// <summary>Open lines only: stop by arc length <see cref="StopS"/> (a drag strip: 40 m past AC_OPEN_FINISH), or with
        /// <see cref="EndSpeed"/> &gt; 0 reach the line's end at that speed (a hill climb whose AC_AB_FINISH is at the end: the driver runs
        /// through it and brakes after). Default (-1, 0): stop at the line's end.</summary>
        public readonly float StopS, EndSpeed;

        /// <summary>Speed (m/s) an AI may cross a finish at the very end of an open line with: it can stop within ~40 m (0.5 g) after it. // ours</summary>
        public const float RunOutSpeed = 20f;

        public AcAiPlan(AcAiTrack track, AcAiCar car, int level, float surfaceGrip, float speedCap, AcAiHints hints, float stopS, float endSpeed)
        {
            StopS = stopS; EndSpeed = endSpeed;
            Track = track; Car = car; Hints = hints != null && !hints.IsEmpty ? hints : null;
            Level = level < 0 ? 0 : level > 100 ? 100 : level;
            SurfaceGrip = surfaceGrip > 0.3f && surfaceGrip < 1.5f ? surfaceGrip : 1f;
            SpeedCap = speedCap > 1f ? speedCap : float.PositiveInfinity;
            GripUse = GripAtLevel100 * (GripAtLevel0 + (1f - GripAtLevel0) * Level / 100f);
            int n = track != null ? track.Count : 0;
            V = new float[n]; VCorner = new float[n]; SpeedHint = new float[n]; BrakeHint = new float[n];
            for (int i = 0; i < n; i++) { SpeedHint[i] = 1f; BrakeHint[i] = 1f; }
            if (n < 3 || car == null) return;
            if (Hints != null)
                for (int i = 0; i < n; i++)
                {
                    float p = track.Spline.PointProgress(i);
                    SpeedHint[i] = Hints.SpeedFactor(p);
                    BrakeHint[i] = Hints.BrakeFactor(p);
                    if (SpeedHint[i] != 1f || BrakeHint[i] != 1f) HintedPoints++;
                }
            Build();
        }

        float MuLat { get { return Car.MuLat * GripUse * SurfaceGrip; } }
        float MuLong { get { return Car.MuLong * GripUse * SurfaceGrip; } }

        /// <summary>Lateral acceleration capacity (m/s^2) at speed v (flat road).</summary>
        public float LatCap(float v) { return MuLat * (AcAiCar.G + Car.Downforce(v) / Car.MassKg); }

        /// <summary>Braking deceleration (m/s^2) at speed v with lateral demand aLat (flat road, no hint; friction circle; drag helps).</summary>
        public float BrakeCap(float v, float aLat) { return BrakeCapG(v, aLat, AcAiCar.G, 1f); }

        /// <summary>Forward acceleration (m/s^2) at speed v with lateral demand aLat (flat road).</summary>
        public float AccelCap(float v, float aLat) { return AccelCapG(v, aLat, AcAiCar.G); }

        /// <summary>Vertical load (m/s^2) at point i and speed v: g reduced on a crest.</summary>
        float GAt(int i, float v)
        {
            float kv = Track.VCurv[i];
            if (kv >= 0f) return AcAiCar.G;
            return Math.Max(CrestMinG * AcAiCar.G, AcAiCar.G + kv * v * v);
        }

        float LatCapG(float v, float g) { return MuLat * (g + Car.Downforce(v) / Car.MassKg); }

        float BrakeCapG(float v, float aLat, float g, float hint)
        {
            float grip = MuLong * (g + Car.Downforce(v) / Car.MassKg);
            float brakes = Car.BrakeDecelMax * GripUse;
            float a = Math.Min(grip, brakes) * Car.Ai.BrakeHint * hint;
            float lc = LatCapG(v, g), use = lc > 0.1f ? Math.Min(1f, Math.Abs(aLat) / lc) : 0f;
            // friction circle; TRAIL_HINT < 1 keeps the AI from braking hard while already cornering [BELIEF]
            float circle = (float)Math.Sqrt(Math.Max(0f, 1f - use * use));
            float trail = Car.Ai.TrailHint >= 1f ? circle : (float)Math.Pow(circle, 1f / Math.Max(0.2f, Car.Ai.TrailHint));
            return Math.Max(0.5f, a * Math.Max(0.15f, trail)) + (Car.Drag(v) + Car.RollingN) / Car.MassKg; // ours: 15 % floor
        }

        float AccelCapG(float v, float aLat, float g)
        {
            float traction = MuLong * (g * Car.DrivenWeightShare + Car.Downforce(v) / Car.MassKg * Car.DrivenWeightShare);
            float power = Car.DriveAccel(v); // rotating inertias included (polish pass 2026-11-01)
            float lc = LatCapG(v, g), use = lc > 0.1f ? Math.Min(1f, Math.Abs(aLat) / lc) : 0f;
            float circle = (float)Math.Sqrt(Math.Max(0f, 1f - use * use));
            float a = Math.Min(power, traction * Math.Max(0.1f, circle)); // ours: 10 % floor (never stall)
            return a - (Car.Drag(v) + Car.RollingN) / Car.MassKg;
        }

        void Build()
        {
            var t = Track;
            int n = t.Count;
            float vtop = Math.Min(Car.TopSpeed(), SpeedCap);
            float k = 0.5f * AcAiCar.AirDensity * Car.ClA / Car.MassKg; // downforce per v^2 per kg
            for (int i = 0; i < n; i++)
            {
                float c = Math.Abs(t.Curv[i]);
                float v = vtop;
                if (c > 1e-5f)
                {
                    // v^2 c = mu (g + kv v^2 + k v^2), kv = crest curvature (<= 0)
                    float kv = Math.Min(0f, t.VCurv[i]);
                    float den = c - MuLat * k - MuLat * kv;
                    if (den > 1e-6f) v = (float)Math.Sqrt(MuLat * AcAiCar.G / den);
                    if (kv < 0f)
                    {
                        float flat = c - MuLat * k > 1e-6f ? (float)Math.Sqrt(MuLat * AcAiCar.G / (c - MuLat * k)) : vtop;
                        float vMin = (float)Math.Sqrt((1f - CrestMinG) * AcAiCar.G / -kv); // where the crest floor takes over
                        if (v > vMin) v = Math.Max(vMin, (float)Math.Sqrt(MuLat * CrestMinG * AcAiCar.G / Math.Max(1e-6f, c - MuLat * k)));
                        if (v < flat * 0.995f && v < vtop) CrestPoints++;
                    }
                }
                v *= SpeedHint[i];
                VCorner[i] = Math.Max(3f, Math.Min(v, vtop)); // ours: 3 m/s floor
                V[i] = VCorner[i];
            }
            if (!t.IsClosed)
            {
                // open line (drag strip, hill climb): stop at StopS / the end, or run through the end at EndSpeed
                if (StopS > 0f && StopS < t.Length - 1f) { for (int i = 0; i < n; i++) if (t.S(i) >= StopS) V[i] = 0f; }
                else V[n - 1] = EndSpeed > 0f ? Math.Min(V[n - 1], EndSpeed) : 0f;
            }
            // closed lines: two laps of passes so the wrap converges; open lines: one pass
            int laps = t.IsClosed ? 2 : 1;
            for (int lap = 0; lap < laps; lap++)
            {
                for (int q = n - 1; q >= 0; q--) BackStep(q);
                if (t.IsClosed) BackStep(n - 1);
            }
            for (int lap = 0; lap < laps; lap++)
            {
                for (int q = 0; q < n; q++)
                {
                    int i = q, j = t.IsClosed ? t.Idx(q + 1) : q + 1;
                    if (j >= n) continue;
                    float ds = Seg(i, j);
                    float vi = V[i];
                    float aLat = vi * vi * Math.Abs(t.Curv[i]);
                    float a = AccelCapG(vi, aLat, GAt(i, vi));
                    float vj = (float)Math.Sqrt(Math.Max(0f, vi * vi + 2f * a * ds));
                    if (vj < V[j]) V[j] = vj;
                }
            }
            // a third backward pass after the forward pass keeps braking points consistent at the wrap
            for (int q = n - 1; q >= 0; q--) BackStep(q);
            double time = 0, fuel = 0;
            int segs = t.IsClosed ? n : n - 1;
            for (int q = 0; q < segs; q++)
            {
                int i = q, j = t.Idx(q + 1);
                float avg = 0.5f * (V[i] + V[j]);
                if (avg <= 0.1f) continue;
                float ds = Seg(i, j);
                double dt = ds / avg;
                time += dt;
                fuel += dt * FuelRate(avg, (V[j] * V[j] - V[i] * V[i]) / (2f * ds));
            }
            LapTime = (float)time;
            FuelPerLap = (float)fuel;
        }

        /// <summary>One backward (braking) step from point q+1 to q.</summary>
        void BackStep(int q)
        {
            var t = Track;
            int n = t.Count;
            int i = q, j = t.IsClosed ? t.Idx(q + 1) : q + 1;
            if (j >= n) return;
            float ds = Seg(i, j);
            float vj = V[j];
            float aLat = vj * vj * Math.Abs(t.Curv[i]);
            float vi = (float)Math.Sqrt(vj * vj + 2f * BrakeCapG(vj, aLat, GAt(i, vj), BrakeHint[i]) * ds);
            if (vi < V[i]) V[i] = vi;
        }

        /// <summary>Litres per second at speed v with longitudinal acceleration a: rpm in the gear under the AI's UP rpm, gas from the force
        /// needed over the force available (0 when braking). // ours estimate</summary>
        float FuelRate(float v, float a)
        {
            if (Car.FuelConsumption <= 0f || a < -0.5f) return 0f;
            float need = a * Car.MassKg + Car.Drag(v) + Car.RollingN, have = Car.DriveForce(v);
            float gas = have > 1f ? Math.Max(0.05f, Math.Min(1f, need / have)) : 0f; // ours: 5 % floor (part throttle never 0)
            float rpm = Car.HoldRpm(v);
            return rpm * gas * Car.FuelConsumption * 0.001f;
        }

        /// <summary>
        /// Litres an AI car starts with. Race: the distance (LAPS, or a timed race's minutes / planned lap + 1) + one lap (the cool-down)
        /// x 1.25 + 1 l (// ours margins: the plan's fuel estimate is ours), at the server FUEL_RATE (<paramref name="rate"/> 1 = 100 %),
        /// capped at MAX_FUEL. Practice / qualify, FUEL_RATE 0 or a car without CONSUMPTION: car.ini FUEL (AC's default start fuel). The AI
        /// never pits here: a car that still runs low gets topped up (AcAiDriver) - see <see cref="NeedsTopUp"/>.
        /// </summary>
        public static float FuelLoad(AcAiCar car, float perLap, float lapTime, bool race, int laps, int minutes, float rate)
        {
            if (car == null) return 0f;
            float start = Math.Min(car.FuelStart, car.FuelMax);
            float use = perLap * Math.Max(0f, rate);
            if (!race || use <= 0.001f) return start;
            float n = laps > 0 ? laps : (lapTime > 1f ? (float)Math.Ceiling(minutes * 60f / lapTime) + 1f : 10f); // ours: 10 laps when unknown
            float want = (n + 1f) * use * 1.25f + 1f; // ours: cool-down lap + 25 % + 1 l
            return Math.Min(want, car.FuelMax);
        }

        /// <summary>True when an AI car has less than one planned lap of fuel left (+ 1 l floor) - it would run dry; AI pit stops are not done.
        /// <paramref name="lapUse"/> = <see cref="FuelPerLap"/> x the FUEL_RATE. // ours threshold</summary>
        public static bool NeedsTopUp(AcAiCar car, float litres, float lapUse)
        {
            return car != null && car.FuelConsumption > 0f && lapUse > 0f && litres < Math.Max(1f, lapUse);
        }

        float Seg(int i, int j)
        {
            if (j == 0 && i == Track.Count - 1) return Math.Max(0.01f, Track.Length - Track.Spline.Total); // closing gap of a loop
            return Math.Max(0.01f, Track.Spline.Length[j] - Track.Spline.Length[i]);
        }

        /// <summary>Target speed at arc length s (linear between points).</summary>
        public float SpeedAt(float s)
        {
            var t = Track;
            if (t.Count < 3) return 0f;
            s = t.WrapS(s);
            int i = t.IndexAt(s), j = t.Idx(i + 1);
            float a = t.Spline.Length[i], seg = j == i ? 0f : (j == 0 ? t.Length - t.Spline.Total : t.Spline.Length[j] - a);
            float f = seg > 1e-4f ? Math.Max(0f, Math.Min(1f, (s - a) / seg)) : 0f;
            return V[i] + (V[j] - V[i]) * f;
        }

        /// <summary>Lowest target speed over [s, s + ahead] (m) - what the speed controller must respect.</summary>
        public float MinSpeedAhead(float s, float ahead)
        {
            var t = Track;
            float v = SpeedAt(s);
            int i = t.IndexAt(s), steps = 0;
            float walked = 0f;
            while (walked < ahead && steps++ < t.Count)
            {
                int j = t.Idx(i + 1);
                if (j == i) break;
                float seg = j == 0 ? t.Length - t.Spline.Total : t.Spline.Length[j] - t.Spline.Length[i];
                walked += Math.Max(0.01f, seg);
                if (V[j] < v) v = V[j];
                i = j;
            }
            return v;
        }
    }
}
