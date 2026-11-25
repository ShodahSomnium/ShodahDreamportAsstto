using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Turning the car's own steering wheel (STEER_HR) with your hands in a headset - the Euro Truck Sim Dreamport's client-proven
    /// EtsSteeringGrab for AC: squeeze the grip with a hand on the rim and the wheel follows that hand round its column; with both hands
    /// on it, it follows the line between them (setting seat.vrsteer: one or two hands / one hand only / stick). The limit is the car's own
    /// car.ini [CONTROLS] STEER_LOCK (degrees centre to full right, 180..585 on the 86 installed cars); the output is absolute
    /// (<c>Input.SteerAbsolute = true; SteerWheelDeg</c>, AcDriveInput). Let go and the steering returns to P's relative path (keys / stick
    /// / self-centring).
    ///
    /// No collider, no XRI on the wheel (a collider would join the car's compound body; the client's grip selects nothing on layer 28).
    /// Called by the seat at the end of its late write (<see cref="AfterRigPlaced"/>): the hands ride on the rig the seat moves and are only
    /// in the car once it has (read earlier they are a frame of travel behind - 1.4 m at 300 km/h). Taking hold is decided on where the
    /// hand is now; turning on how the hand moved INSIDE THE RIG since last frame, carried through this frame's rig and wheel - so the car
    /// pitching / rolling under a heading-only rig never turns the wheel by itself (ETS key trick).
    ///
    /// The wheel frame is C's: STEER_HR rest rotation (parent x SteeringRest), local Z = the steering column (toward the dash), the rim in
    /// the local XY plane, +Y the top, +X the driver's right; angle = atan2(x, y): 0 at twelve o'clock, clockwise (as the driver sees it)
    /// positive = right, the same sense as C's SetSteer(rest * Euler(0, 0, -deg)) (ac_car_formats.md C3). The rim radius and plane are
    /// measured from the readable rim mesh (outermost vertex - 2 cm tube; probe on the install: 0.11..0.34 m, road cars ~0.17-0.18 m).
    /// Haptics: a light tick when a hand reaches the rim, a pulse on taking hold, bumps past the lock, and the road through the rim from the
    /// front tyres' surfaces (surfaces.ini VIBRATION_GAIN / VIBRATION_LENGTH, kerbs) and skids (ETS EtsWheelHaptics idea, AC data).
    /// </summary>
    public static class AcWheelGrab
    {
        public const float Reach = 0.12f;            // ours (ETS)
        public const float LetGoDistance = 0.35f;    // ours (ETS)
        private const float MaxStepDegrees = 60f;    // ours (ETS: a tracking jump)
        private const float ChordMin = 0.15f;        // ours (ETS)
        private const float HubDeadRadius = 0.05f;   // ours (ETS)
        private const float TubeInset = 0.02f;       // ours (ETS)
        private const float FallbackRimRadius = 0.18f; // ours: the install's road-car median (outer 0.19-0.20 m minus the tube)

        private sealed class Hand
        {
            public bool Right, GripDown, Holding, InReach, HaveLast;
            public Vector3 LastInRig;
            public float FromRim = float.MaxValue;
            public float OneShotUntil;
            public float SentAt = -1f;
        }

        private static readonly Hand L = new Hand { Right = false }, R = new Hand { Right = true };
        private static int _held;
        private static float _wheelDeg, _rimRadius = -1f, _rimZ;
        private static Transform _measuredFor;
        private static float _nextMeasure;
        private static float _nextLockBump;
        private static bool _reportedGrip;
        private static AcCarRig _readyFor;      // the "wheel ready" line once per car
        private static int _placedFrame = -10;
        private static float _hapticLevel, _kerbPhase;

        /// <summary>Hands on the rim (bit 0 left, bit 1 right).</summary>
        public static int Holding { get { return Time.frameCount - _placedFrame > 2 ? 0 : _held; } }
        /// <summary>The steering-wheel angle the hands hold (deg, right positive, within +-STEER_LOCK).</summary>
        public static float WheelDeg { get { return _wheelDeg; } }
        /// <summary>The measured rim radius (m; -1 before measuring).</summary>
        public static float RimRadius { get { return _rimRadius; } }
        /// <summary>A hand is within reach of the rim (the HUD / lab).</summary>
        public static bool AnyInReach { get { return L.InReach || R.InReach; } }

        /// <summary>Drops both hands (getting out, tablet, car change).</summary>
        public static void LetGo()
        {
            L.Holding = R.Holding = false;
            L.InReach = R.InReach = false;
            L.HaveLast = R.HaveLast = false;
            _held = 0;
            _hapticLevel = 0f;
            AcLocal.WheelHands = 0;
        }

        /// <summary>A hand's distance from the rim this frame (m; MaxValue when untracked) - AcVrDrive keeps grip-clicks away from the rim.</summary>
        public static float FromRim(bool right) { return right ? R.FromRim : L.FromRim; }
        public static bool HandHolding(bool right) { return right ? R.Holding : L.Holding; }

        /// <summary>Called by the seat after the rig was placed this frame (VR driver only).</summary>
        public static void AfterRigPlaced(AcSeatGeometry g)
        {
            _placedFrame = Time.frameCount;
            Vector3 hub;
            Quaternion rest;
            if (g == null || !AcSeat.Vr || !AcSeat.IsDriver || AcSeatSettings.VrSteer == 2 || AcDriveInput.TabletUp || !g.WheelRestFrame(out hub, out rest))
            {
                if (_held != 0) LetGo();
                L.FromRim = R.FromRim = float.MaxValue;
                return;
            }

            MeasureRim(g);
            float lockDeg = g.SteerLockDeg;
            if (!ReferenceEquals(_readyFor, g.Rig) && (g.Data == null || g.Data.Ready))
            {
                _readyFor = g.Rig;
                AcLog.Info("Seat: wheel ready in " + (g.Rig.Car != null ? g.Rig.Car.Id : "?") + " (rim radius " + _rimRadius.ToString("0.000") + " m, lock " + lockDeg.ToString("0") + " deg, " +
                           (g.Data != null && g.Data.Ready && g.Data.SteerLockDeg > 1f ? "car.ini STEER_LOCK" : "fallback lock") + "; steering " +
                           (AcSeatSettings.VrSteer == 1 ? "one hand" : "one or two hands") + ").");
            }

            Matrix4x4 toWheel = Matrix4x4.TRS(hub + rest * new Vector3(0f, 0f, _rimZ), rest, Vector3.one).inverse;
            Transform rig = AcSeat.BodyRoot;
            Matrix4x4 rigToWorld = rig != null ? rig.localToWorldMatrix : Matrix4x4.identity;
            Vector3 lIn, rIn;
            bool haveL = InRig(AcSeat.LeftHand, rig, out lIn);
            bool haveR = InRig(AcSeat.RightHand, rig, out rIn);
            Vector3 lw = toWheel.MultiplyPoint3x4(rigToWorld.MultiplyPoint3x4(lIn));
            Vector3 rw = toWheel.MultiplyPoint3x4(rigToWorld.MultiplyPoint3x4(rIn));
            AcHand xl = AcXr.Hand(false), xr = AcXr.Hand(true);
            bool oneHand = AcSeatSettings.VrSteer == 1;
            Step(L, haveL, lw, xl.Grip, oneHand && R.Holding);
            Step(R, haveR, rw, xr.Grip, oneHand && L.Holding);

            if (!_reportedGrip && (xl.Grip > 0.01f || xr.Grip > 0.01f))
            {
                _reportedGrip = true;
                AcLog.Info("Seat: grip report L " + xl.Grip.ToString("0.00") + " / R " + xr.Grip.ToString("0.00") + " (hands " + (haveL ? "L" : "-") + (haveR ? "R" : "-") +
                           ", from rim L " + Dist(L.FromRim) + " R " + Dist(R.FromRim) + ").");
            }

            int mask = (L.Holding ? 1 : 0) | (R.Holding ? 2 : 0);
            if (mask == 0)
            {
                _held = 0;
                AcLocal.WheelHands = 0;
                Remember(L, haveL, lIn);
                Remember(R, haveR, rIn);
                Haptics(g, false, false);
                return;
            }

            if (_held == 0)
            {
                // first hand on: the wheel starts from where the keys / stick / self-centring left it
                AcCarRig car = g.Rig;
                _wheelDeg = car != null && car.Physics != null ? car.Physics.Telemetry.SteerWheelDeg : 0f;
                if (float.IsNaN(_wheelDeg)) _wheelDeg = 0f;
            }

            float delta = 0f;
            if (mask == _held)
            {
                Vector2 lNow = Flat(lw), rNow = Flat(rw);
                Vector2 lThen = Flat(toWheel.MultiplyPoint3x4(rigToWorld.MultiplyPoint3x4(L.LastInRig)));
                Vector2 rThen = Flat(toWheel.MultiplyPoint3x4(rigToWorld.MultiplyPoint3x4(R.LastInRig)));
                if (mask == 3 && L.HaveLast && R.HaveLast && (rNow - lNow).magnitude > ChordMin && (rThen - lThen).magnitude > ChordMin)
                {
                    delta = Mathf.DeltaAngle(Angle(rThen - lThen), Angle(rNow - lNow));
                }
                else
                {
                    float sum = 0f;
                    int count = 0;
                    Accumulate(L, lThen, lNow, ref sum, ref count);
                    Accumulate(R, rThen, rNow, ref sum, ref count);
                    delta = count > 0 ? sum / count : 0f;
                }

                if (Mathf.Abs(delta) > MaxStepDegrees) delta = 0f;
            }

            Remember(L, haveL, lIn);
            Remember(R, haveR, rIn);
            _held = mask;
            AcLocal.WheelHands = mask;
            float wanted = _wheelDeg + delta;
            _wheelDeg = Mathf.Clamp(wanted, -lockDeg, lockDeg);

            if (Mathf.Abs(wanted) > lockDeg && Time.unscaledTime >= _nextLockBump)
            {
                _nextLockBump = Time.unscaledTime + 0.15f;
                if (L.Holding) OneShot(L, 0.5f, 0.04f);
                if (R.Holding) OneShot(R, 0.5f, 0.04f);
            }

            Haptics(g, L.Holding, R.Holding);
        }

        private static string Dist(float d) { return d > 100f ? "-" : d.ToString("0.00") + " m"; }

        private static bool InRig(Transform hand, Transform rig, out Vector3 inRig)
        {
            if (hand == null || rig == null) { inRig = Vector3.zero; return false; }
            inRig = rig.InverseTransformPoint(hand.position);
            return true;
        }

        private static void Remember(Hand h, bool tracked, Vector3 inRig)
        {
            h.HaveLast = tracked;
            if (tracked) h.LastInRig = inRig;
        }

        private static void Accumulate(Hand h, Vector2 then, Vector2 now, ref float sum, ref int count)
        {
            if (!h.Holding || !h.HaveLast || now.magnitude < HubDeadRadius || then.magnitude < HubDeadRadius) return;
            sum += Mathf.DeltaAngle(Angle(then), Angle(now));
            count++;
        }

        /// <summary>The rim plane coordinates (x right, y up).</summary>
        private static Vector2 Flat(Vector3 inWheel) { return new Vector2(inWheel.x, inWheel.y); }

        /// <summary>0 at the top of the rim, growing clockwise as the driver sees it (toward +X = the driver's right).</summary>
        public static float Angle(Vector2 flat) { return Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg; }

        private static void Step(Hand h, bool tracked, Vector3 inWheel, float grip, bool blocked)
        {
            bool was = h.GripDown;
            h.GripDown = grip >= (was ? AcXr.GripOff : AcXr.GripOn);
            bool squeezed = h.GripDown && !was;
            if (!tracked)
            {
                h.Holding = false;
                h.InReach = false;
                h.FromRim = float.MaxValue;
                return;
            }

            float radial = Flat(inWheel).magnitude;
            float fromRim = Mathf.Sqrt((radial - _rimRadius) * (radial - _rimRadius) + inWheel.z * inWheel.z);
            h.FromRim = fromRim;
            bool inReach = fromRim <= Reach;
            if (h.Holding)
            {
                if (!h.GripDown || fromRim > LetGoDistance) h.Holding = false;
            }
            else if (squeezed && inReach && !blocked)
            {
                h.Holding = true;
                OneShot(h, 0.35f, 0.05f);
                AcLog.InfoOnce("Seat: VR wheel taken with the " + (h.Right ? "right" : "left") + " hand, " + fromRim.ToString("0.00") + " m from the rim (rim radius " + _rimRadius.ToString("0.000") + " m).");
            }

            if (inReach && !h.InReach && !h.Holding && !h.GripDown) OneShot(h, 0.1f, 0.02f);
            h.InReach = inReach;
        }

        private static void OneShot(Hand h, float amp, float seconds)
        {
            AcXr.Pulse(h.Right, amp, seconds);
            h.OneShotUntil = Time.unscaledTime + seconds;
        }

        /// <summary>The rim radius and plane (local z) from the readable mesh under STEER_HR (ETS MeasureRim, AC axes: rim in local XY).</summary>
        private static void MeasureRim(AcSeatGeometry g)
        {
            Transform wheel = g.SteeringWheel;
            if (wheel == null || (_measuredFor == wheel && _rimRadius > 0f)) return;
            if (Time.unscaledTime < _nextMeasure) return;
            _measuredFor = wheel;
            float outer = 0f, zSum = 0f;
            int zCount = 0, readable = 0, unreadable = 0;
            Matrix4x4 toWheel = wheel.worldToLocalMatrix;
            MeshFilter[] filters = wheel.GetComponentsInChildren<MeshFilter>(true);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int f = 0; f < filters.Length; f++)
                {
                    Mesh mesh = filters[f].sharedMesh;
                    if (mesh == null) continue;
                    if (!mesh.isReadable) { if (pass == 0) unreadable++; continue; }
                    if (pass == 0) readable++;
                    Matrix4x4 toLocal = toWheel * filters[f].transform.localToWorldMatrix;
                    Vector3[] verts = mesh.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 p = toLocal.MultiplyPoint3x4(verts[i]);
                        float r = new Vector2(p.x, p.y).magnitude;
                        if (pass == 0) { if (r > outer) outer = r; }
                        else if (r > outer - 2f * TubeInset) { zSum += p.z; zCount++; }
                    }
                }
            }

            bool measured = outer > 0.10f && outer < 0.4f;
            if (!measured && readable == 0 && unreadable == 0) { _measuredFor = null; _nextMeasure = Time.unscaledTime + 1f; }      // the model is still building: again in a second
            float knh = KnhRadius(g);
            _rimRadius = measured ? outer - TubeInset : knh > 0.1f ? knh - TubeInset : FallbackRimRadius;
            _rimZ = measured && zCount > 0 ? zSum / zCount : 0f;
            AcLog.Info("Seat: VR wheel rim radius " + _rimRadius.ToString("0.000") + " m, plane z " + _rimZ.ToString("0.000") + " (" +
                       (measured ? "from the rim mesh, outermost vertex " + outer.ToString("0.000") + " m, " + readable + " readable mesh(es)" :
                           (knh > 0.1f ? "from the knh hands' radius " + knh.ToString("0.000") : "fallback") + "; the mesh gave " + outer.ToString("0.000") + " m, " + readable + " readable / " + unreadable + " unreadable") + ").");
        }

        /// <summary>The knh wrists' mean distance from the hub in the rim plane (the driver's 9-and-3 grip), 0 when unknown.</summary>
        private static float KnhRadius(AcSeatGeometry g)
        {
            AcSeatCar d = g.Data;
            Vector3 hub;
            Quaternion rest;
            if (d == null || !d.Ready || !d.HasKnh || !g.WheelRestFrame(out hub, out rest)) return 0f;
            Matrix4x4 toWheel = Matrix4x4.TRS(hub, rest, Vector3.one).inverse;
            Vector3 l = toWheel.MultiplyPoint3x4(g.ModelToWorld(AcSeatData.ToUnity(d.HandL)));
            Vector3 r = toWheel.MultiplyPoint3x4(g.ModelToWorld(AcSeatData.ToUnity(d.HandR)));
            return 0.5f * (new Vector2(l.x, l.y).magnitude + new Vector2(r.x, r.y).magnitude);
        }

        // ---------------------------------------------------------------- the road through the rim

        private const float Resend = 0.05f, Overlap = 0.12f, Cap = 0.3f; // ours (ETS EtsWheelHaptics)
        private const float KerbGain = 0.35f, SkidGain = 0.2f, Idle = 0.03f; // ours

        private static void Haptics(AcSeatGeometry g, bool left, bool right)
        {
            float scale = AcSeatSettings.Haptics;
            if ((!left && !right) || scale <= 0f || !AcSettings.Haptics) { _hapticLevel = 0f; return; }
            AcCarRig car = g.Rig;
            if (car == null || car.Physics == null) return;
            AcTelemetry t = car.Physics.Telemetry;
            float dt = Time.deltaTime;
            float speed = Mathf.Abs(t.SpeedKmh) / 3.6f;
            // kerbs / rumble strips: surfaces.ini VIBRATION_GAIN over VIBRATION_LENGTH (m) - an amplitude wave at speed / length Hz
            float gain = 0f, length = 0f;
            Surface(t.LF, ref gain, ref length);
            Surface(t.RF, ref gain, ref length);
            float kerb = 0f;
            if (gain > 0f && length > 0.01f)
            {
                _kerbPhase += speed / length * dt;
                _kerbPhase -= Mathf.Floor(_kerbPhase);
                kerb = KerbGain * Mathf.Clamp01(gain) * Mathf.Abs(Mathf.Sin(_kerbPhase * Mathf.PI * 2f)) * Mathf.Clamp01(speed / 5f);
            }

            float skid = SkidGain * Mathf.Clamp01(Mathf.Max(t.LF.Skid, t.RF.Skid));
            float target = Mathf.Min(Cap, (t.Rpm > 1f ? Idle : 0f) + kerb + skid) * scale;
            _hapticLevel += (target - _hapticLevel) * (1f - Mathf.Exp(-dt / 0.05f));
            if (left) Rumble(L);
            if (right) Rumble(R);
        }

        private static void Surface(AcWheelState w, ref float gain, ref float length)
        {
            if (!w.Contact || w.Surface == null) return;
            if (w.Surface.VibrationGain > gain) { gain = w.Surface.VibrationGain; length = w.Surface.VibrationLength; }
        }

        private static void Rumble(Hand h)
        {
            float now = Time.unscaledTime;
            if (now < h.OneShotUntil || _hapticLevel < 0.01f) return;
            if (h.SentAt >= 0f && now - h.SentAt < Resend) return;
            h.SentAt = now;
            AcXr.Pulse(h.Right, _hapticLevel, Overlap);
        }
    }
}
