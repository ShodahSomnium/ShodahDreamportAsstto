using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Where things are in one car this frame, in world space: seat (hips), eyes (cockpit / VR), passenger seat, steering-wheel hub
    /// and rest frame, the door side, the cameras' mount points. Model-space points come from C's <see cref="AcCarModel"/> (DriverHips,
    /// DriverEye, PassengerHips, SteeringWheel) and, where C has nothing yet, from the car's own files (<see cref="AcSeatData"/>: knh +
    /// car.ini, mirrored to Unity axes). Model space -> world goes through <c>Model.Root</c> (C places it at GRAPHICS_OFFSET with the
    /// GRAPHICS_PITCH_ROTATION under the Rigidbody), or, without a model root, through the rig root with the offset applied here
    /// (ac_car_formats.md C1). One instance per bound car (cached by <see cref="For"/>); allocation-free per frame.
    /// </summary>
    public sealed class AcSeatGeometry
    {
        public AcCarRig Rig { get; private set; }
        public AcSeatCar Data { get; private set; }
        private Transform _steer;
        private bool _steerSearched;
        private float _nextSteerSearch;
        private static AcSeatGeometry _last;

        /// <summary>Cached geometry for a rig (a new rig = new geometry).</summary>
        public static AcSeatGeometry For(AcCarRig rig)
        {
            if (rig == null) return null;
            if (_last != null && ReferenceEquals(_last.Rig, rig)) return _last;
            _last = new AcSeatGeometry { Rig = rig, Data = AcSeatData.For(rig.Car) };
            return _last;
        }

        /// <summary>A geometry not cached as "the" seat car (doors of other cars, riders).</summary>
        public static AcSeatGeometry Make(AcCarRig rig)
        {
            return rig == null ? null : new AcSeatGeometry { Rig = rig, Data = AcSeatData.For(rig.Car) };
        }

        public bool Alive { get { return Rig != null && Rig.Root != null; } }
        public Transform Body { get { return Rig != null && Rig.Root != null ? Rig.Root.transform : null; } }
        private Transform ModelRoot { get { return Rig != null && Rig.Model != null ? Rig.Model.Root : null; } }

        /// <summary>A Unity model-space point (C's convention) -> world.</summary>
        public Vector3 ModelToWorld(Vector3 p)
        {
            Transform m = ModelRoot;
            Transform b = Body;
            if (m != null && b != null && m != b) return m.TransformPoint(p);
            if (b == null) return p;
            // no separate model root (C not built yet): body-local = GRAPHICS_OFFSET + Euler(GRAPHICS_PITCH_ROTATION, 0, 0) * model point (C1;
            // the same placement P's AcCarRig gives C's model root)
            Vector3 off;
            Quaternion pitch;
            Placement(out off, out pitch);
            return b.TransformPoint(off + pitch * p);
        }

        private void Placement(out Vector3 off, out Quaternion pitch)
        {
            off = Vector3.zero;
            pitch = Quaternion.identity;
            if (Data == null || !Data.Ready) return;
            if (Data.HasOffset) off = new Vector3(-Data.GraphicsOffset.X, Data.GraphicsOffset.Y, Data.GraphicsOffset.Z);
            if (Data.GraphicsPitchDeg != 0f) pitch = Quaternion.Euler(Data.GraphicsPitchDeg, 0f, 0f);
        }

        /// <summary>A world point -> Unity model space (inverse of <see cref="ModelToWorld"/>).</summary>
        public Vector3 WorldToModel(Vector3 w)
        {
            Transform m = ModelRoot;
            Transform b = Body;
            if (m != null && b != null && m != b) return m.InverseTransformPoint(w);
            if (b == null) return w;
            Vector3 off;
            Quaternion pitch;
            Placement(out off, out pitch);
            return Quaternion.Inverse(pitch) * (b.InverseTransformPoint(w) - off);
        }

        private static bool Set(Vector3 v) { return v.sqrMagnitude > 1e-6f; }

        /// <summary>The driver's hip point (knh RIG_Hips) in Unity model space; false while nothing is known.</summary>
        public bool DriverHipsModel(out Vector3 p)
        {
            AcCarModel m = Rig != null ? Rig.Model : null;
            if (m != null && Set(m.DriverHips)) { p = m.DriverHips; return true; }
            if (Data != null && Data.Ready && Data.HasKnh) { p = AcSeatData.ToUnity(Data.Hips); return true; }
            if (Data != null && Data.Ready && Data.HasEyes) { p = AcSeatData.ToUnity(Data.Eyes) + new Vector3(0f, -0.55f, -0.05f); return true; } // ours: C's fallback (eye - (0, 0.55, 0.05)), so seat and model agree
            p = Vector3.zero;
            return false;
        }

        /// <summary>The cockpit eye (car.ini DRIVEREYES) in Unity model space.</summary>
        public bool DriverEyeModel(out Vector3 p)
        {
            AcCarModel m = Rig != null ? Rig.Model : null;
            if (m != null && Set(m.DriverEye)) { p = m.DriverEye; return true; }
            if (Data != null && Data.Ready && Data.HasEyes) { p = AcSeatData.ToUnity(Data.Eyes); return true; }
            if (Data != null && Data.Ready && Data.HasKnh) { p = AcSeatData.ToUnity(Data.Head) + new Vector3(0f, 0f, 0.09f); return true; } // C1: eyes median 0.09 m ahead of RIG_Head
            p = Vector3.zero;
            return false;
        }

        /// <summary>The VR eye: system/cfg/vr.ini [car] DRIVEREYES when the game has one for this car (42 / 86), else the cockpit eye.</summary>
        public bool VrEyeModel(out Vector3 p, out bool fromVrIni)
        {
            fromVrIni = false;
            if (Data != null && Data.Ready && Data.HasVrEyes) { p = AcSeatData.ToUnity(Data.VrEyes); fromVrIni = true; return true; }
            return DriverEyeModel(out p);
        }

        /// <summary>The seat's hip point in Unity model space (passenger = C's PassengerHips, else the driver's mirrored across x = 0, // ours as C).</summary>
        public bool SeatModel(AcSeatKind seat, out Vector3 p)
        {
            if (seat == AcSeatKind.Passenger)
            {
                AcCarModel m = Rig != null ? Rig.Model : null;
                if (m != null && m.HasPassengerSeat && Set(m.PassengerHips)) { p = m.PassengerHips; return true; }
                if (DriverHipsModel(out p)) { p.x = -p.x; return true; }
                return false;
            }

            return DriverHipsModel(out p);
        }

        /// <summary>The seat's eye in Unity model space (passenger: the driver's mirrored).</summary>
        public bool EyeModel(AcSeatKind seat, bool vr, out Vector3 p, out bool fromVrIni)
        {
            fromVrIni = false;
            bool ok = vr ? VrEyeModel(out p, out fromVrIni) : DriverEyeModel(out p);
            if (ok && seat == AcSeatKind.Passenger) p.x = -p.x;
            return ok;
        }

        /// <summary>The car has a passenger seat (C's rule; a stub model says no).</summary>
        public bool HasPassengerSeat { get { return Rig != null && Rig.Model != null && Rig.Model.HasPassengerSeat; } }

        /// <summary>Which side a seat's door is on (Unity model x &lt; 0 = car left, AC +X = left mirrored).</summary>
        public AcSide SideOf(AcSeatKind seat)
        {
            Vector3 p;
            if (!SeatModel(seat, out p)) return seat == AcSeatKind.Driver ? AcSide.Left : AcSide.Right;
            if (Mathf.Abs(p.x) < 0.05f) return AcSide.Left;      // single-seater on the centre line: out on the left // ours
            return p.x < 0f ? AcSide.Left : AcSide.Right;
        }

        /// <summary>STEER_HR (C's SteeringWheel, else found by name under the model once a second).</summary>
        public Transform SteeringWheel
        {
            get
            {
                AcCarModel m = Rig != null ? Rig.Model : null;
                if (m != null && m.SteeringWheel != null) return m.SteeringWheel;
                if (_steer != null) return _steer;
                if (m == null || m.Root == null || Time.unscaledTime < _nextSteerSearch) return null;
                _nextSteerSearch = Time.unscaledTime + 1f;
                _steer = FindDeep(m.Root, "STEER_HR") ?? FindDeep(m.Root, "STEER_LR");
                if (!_steerSearched && _steer != null) _steerSearched = true;
                return _steer;
            }
        }

        /// <summary>The steering wheel's rest rotation in world space (parent x C's SteeringRest), hub at its pivot. +Z = column axis (toward the
        /// dash), +Y = top of the rim, +X = the driver's right (C3, mirrored); the rim lies in the local XY plane.</summary>
        public bool WheelRestFrame(out Vector3 hub, out Quaternion rest)
        {
            Transform s = SteeringWheel;
            if (s == null) { hub = Vector3.zero; rest = Quaternion.identity; return false; }
            hub = s.position;
            AcCarModel m = Rig != null ? Rig.Model : null;
            Quaternion local = m != null && m.SteeringWheel == s ? m.SteeringRest : s.localRotation;
            rest = s.parent != null ? s.parent.rotation * local : local;
            return true;
        }

        /// <summary>car.ini STEER_LOCK (deg) from the game's file, else P's spec, else 450 (the most common installed value, 25 / 86 cars).</summary>
        public float SteerLockDeg
        {
            get
            {
                if (Data != null && Data.Ready && Data.SteerLockDeg > 1f) return Data.SteerLockDeg;
                if (Rig != null && Rig.Spec != null && Rig.Spec.SteerLockDeg > 1f) return Rig.Spec.SteerLockDeg;
                return 450f; // ours fallback (mode of the install's STEER_LOCK census)
            }
        }

        public const float YawHoldBelow = 0.25f;     // ours: flattened forward shorter than this (pitch beyond ~60 deg) = heading held
        public const float YawRateDegPerSec = 720f;  // ours: the heading turns at most this fast (a tumble cannot whip the VR view round in one frame)
        public const float YawSnapJumpM = 5f;        // ours: the body moved this far in a frame = a reset / teleport, the heading snaps
        private int _yawFrame = -1;
        private float _yaw;
        private bool _haveYaw;
        private Vector3 _yawPos;

        /// <summary>The car's heading as a Unity yaw (deg), once per frame: the flattened forward; held while the car stands near vertical
        /// (nose dig, end over end - the flattened forward has no reliable direction there) and rate-limited, so the heading-only VR view never
        /// flips or spins in one frame; a reset (body jump) snaps.</summary>
        public float Yaw
        {
            get
            {
                Transform b = Body;
                if (b == null) return _haveYaw ? _yaw : 0f;
                int frame = Time.frameCount;
                if (_haveYaw && frame == _yawFrame) return _yaw;
                Vector3 f = b.forward;
                f.y = 0f;
                float h = f.sqrMagnitude;
                Vector3 pos = b.position;
                bool jumped = _haveYaw && ((pos - _yawPos).sqrMagnitude > YawSnapJumpM * YawSnapJumpM || frame - _yawFrame > 2);   // (not read for a while: fresh)
                float raw;
                if (h >= YawHoldBelow || !_haveYaw || jumped) raw = h < 1e-6f ? b.eulerAngles.y : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                else raw = _yaw;
                if (!_haveYaw || jumped) _yaw = raw;
                else
                {
                    _yaw = Mathf.MoveTowardsAngle(_yaw, raw, YawRateDegPerSec * Mathf.Min(Time.deltaTime, 0.1f));
                }

                _haveYaw = true;
                _yawFrame = frame;
                _yawPos = pos;
                return _yaw;
            }
        }

        public float SpeedKmh
        {
            get
            {
                if (Rig == null) return 0f;
                if (Rig.Physics != null) return Mathf.Abs(Rig.Physics.SpeedKmh);
                return 0f;
            }
        }

        /// <summary>C's outer door handle point on a side (model space: the door's rear end, 55 % up its meshes) when C found that door
        /// (car_DOOR_x.ksanim bound); false otherwise (no door: single-seaters, roadsters without a door animation, model not built).</summary>
        public bool DoorHandleModel(AcSide side, out Vector3 p)
        {
            p = Vector3.zero;
            AcCarModel m = Rig != null ? Rig.Model : null;
            if (m == null || m.Root == null) return false;
            try
            {
                if (!m.HasDoor(side)) return false;
                p = m.DoorHandle(side);
            }
            catch (System.Exception e) { AcLog.WarnOnce("Seat: AcCarModel.DoorHandle threw: " + e.Message); return false; }
            return Set(p);
        }

        /// <summary>|x| of the door skin beside a seat (Unity model space): C's outer handle point, else the model bounds' half width - 3 cm
        /// (bounds include the mirrors) // ours.</summary>
        public float DoorSkinX(AcSeatKind seat)
        {
            Vector3 h;
            if (DoorHandleModel(SideOf(seat), out h)) return Mathf.Abs(h.x);
            return HalfWidth - 0.03f; // ours
        }

        /// <summary>Half the car's width (model bounds; 0.9 m when unknown // ours).</summary>
        public float HalfWidth
        {
            get
            {
                AcCarModel m = Rig != null ? Rig.Model : null;
                if (m != null && m.Bounds.size.x > 0.5f) return m.Bounds.extents.x;
                return 0.9f; // ours
            }
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }

            return null;
        }
    }
}
