using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>What one input device asked for this frame (AcDriveInput merges keyboard, gamepad and VR).</summary>
    public struct AcDriveRequest
    {
        public float Throttle, Brake, Handbrake, Clutch;
        /// <summary>Relative steering -1..1 (already through AC's own keyboard / pad filter) and whether this device steered at all.</summary>
        public float Steer;
        public bool Steering;
        public bool ShiftUp, ShiftDown, LightsToggle, Horn, Reset, Camera, Exit;
    }

    /// <summary>
    /// [S, Seat] The monitor keyboard while driving (legacy Input: the client runs both input back ends). Keys (DESIGN; AC's defaults where
    /// they do not clash): W / Up gas, S / Down brake, A / Left and D / Right steer, E or Left Shift shift up, Q or Left Ctrl shift down,
    /// Space handbrake, H or L headlights, N horn (// ours), R reset to the track (P's in-place reset), C camera, F get out (F is the client's
    /// Interact key: it is ours only while seated - AcFirstPerson holds the client's F / Q / E / R actions off then). Tab / M (the client's
    /// tablet) are never ours. Steering follows AC's own keyboard filter from cfg/controls.ini [KEYBOARD] (STEERING_SPEED 1.75/s toward the
    /// key, STEERING_OPPOSITE_DIRECTION_SPEED 2.5/s when reversing, STEER_RESET_SPEED 1.8/s back to centre, STEER_GAIN speed sensitivity).
    /// </summary>
    public static class AcKeys
    {
        private static float _steer;
        private static bool _broken;

        /// <summary>A text field has the keyboard (the tablet's search box): no driving keys.</summary>
        internal static bool Typing()
        {
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
        }

        public static void Reset() { _steer = 0f; }

        public static void Read(float speedKmh, ref AcDriveRequest r)
        {
            if (_broken) return;
            try
            {
                if (Typing()) { Decay(Time.deltaTime); return; }
                bool gas = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                bool brake = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
                bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
                bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                if (gas) r.Throttle = 1f;
                if (brake) r.Brake = 1f;
                if (Input.GetKey(KeyCode.Space)) r.Handbrake = 1f;
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftShift)) r.ShiftUp = true;
                if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftControl)) r.ShiftDown = true;
                if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.L)) r.LightsToggle = true;
                if (Input.GetKey(KeyCode.N)) r.Horn = true;
                if (Input.GetKeyDown(KeyCode.R)) r.Reset = true;
                if (Input.GetKeyDown(KeyCode.F)) r.Exit = true;
                float target = (right ? 1f : 0f) - (left ? 1f : 0f);
                Filter(target, speedKmh, Time.deltaTime);
                if (target != 0f || Mathf.Abs(_steer) > 1e-4f)
                {
                    r.Steer = _steer;
                    r.Steering = true;
                }
            }
            catch (Exception e)
            {
                _broken = true;
                AcLog.Warn("Seat: the keyboard (legacy Input) is unavailable: " + e.Message);
            }
        }

        /// <summary>Passenger keys: F gets out, C camera.</summary>
        public static void ReadPassenger(ref AcDriveRequest r)
        {
            if (_broken) return;
            try
            {
                if (Typing()) return;
                if (Input.GetKeyDown(KeyCode.F)) r.Exit = true;
            }
            catch (Exception) { _broken = true; }
        }

        /// <summary>AC's keyboard steering (cfg/controls.ini [KEYBOARD]); the speed-sensitive limit formula is ours: 1 / (1 + STEER_GAIN * v[m/s]),
        /// never below MINIMUM_STEERING.</summary>
        private static void Filter(float target, float speedKmh, float dt)
        {
            AcSeatGlobals g = AcSeatData.G;
            float limit = Mathf.Clamp(1f / (1f + g.KeySteerGain * Mathf.Abs(speedKmh) / 3.6f), Mathf.Clamp01(g.KeyMinimumSteering), 1f); // ours (formula)
            if (target == 0f) { Decay(dt); return; }
            float want = target * limit;
            bool reversing = _steer != 0f && Mathf.Sign(_steer) != Mathf.Sign(target);
            float rate = reversing ? g.KeyOppositeSpeed : g.KeySteeringSpeed;
            _steer = Mathf.MoveTowards(_steer, want, rate * dt);
        }

        /// <summary>Back to centre at controls.ini [KEYBOARD] STEER_RESET_SPEED.</summary>
        private static void Decay(float dt) { _steer = Mathf.MoveTowards(_steer, 0f, AcSeatData.G.KeyResetSpeed * dt); }
    }

    /// <summary>
    /// [S, Seat] A gamepad on a monitor through the Unity Input System, by reflection on its public names (no Input System type is compiled
    /// against - BO2 / AcFirstPerson pattern): bound once a second while missing, read through delegates (no allocation per frame).
    /// AC's X360 defaults (cfg/controls.ini): left stick steer (STEER_DEADZONE, STEER_GAMMA, STEER_FILTER, SPEED_SENSITIVITY, STEER_SPEED),
    /// right trigger gas, left trigger brake, A shift up, X shift down, B handbrake, Y horn, D-pad up headlights; ours: right shoulder camera,
    /// D-pad down get out. Select / Start are the client's tablet - never ours.
    /// </summary>
    public static class AcPad
    {
        private static object _pad;
        private static Func<Vector2> _stick;
        private static Func<float> _rt, _lt;
        private static Func<bool> _a, _b, _x, _y, _up, _down, _rb;
        private static bool _wa, _wx, _wy, _wup, _wdown, _wrb;
        private static PropertyInfo _current;
        private static float _nextBind, _steer;
        private static bool _failed, _typeLooked, _said;

        public static void Read(float speedKmh, ref AcDriveRequest r)
        {
            if (_failed) return;
            try
            {
                Bind();
                if (_pad == null) return;
                Vector2 s = _stick();
                float rt = _rt(), lt = _lt();
                bool a = _a(), b = _b(), x = _x(), y = _y(), up = _up(), down = _down(), rb = _rb();
                if (rt > 0.02f) r.Throttle = Mathf.Max(r.Throttle, Mathf.Clamp01(rt));
                if (lt > 0.02f) r.Brake = Mathf.Max(r.Brake, Mathf.Clamp01(lt));
                if (b) r.Handbrake = 1f;
                if (a && !_wa) r.ShiftUp = true;
                if (x && !_wx) r.ShiftDown = true;
                if (y) r.Horn = true;
                if (up && !_wup) r.LightsToggle = true;
                if (down && !_wdown) r.Exit = true;
                if (rb && !_wrb) r.Camera = true;
                _wa = a; _wx = x; _wy = y; _wup = up; _wdown = down; _wrb = rb;
                float steer = Shape(ref _steer, s.x, speedKmh, Time.deltaTime, 0f);
                if (Mathf.Abs(steer) > 1e-3f) { r.Steer = steer; r.Steering = true; }
            }
            catch (Exception e)
            {
                _failed = true;
                AcLog.Warn("Seat: reading the gamepad failed (" + e.GetType().Name + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message) + "); the keyboard still drives.");
            }
        }

        /// <summary>AC X360 steering: dead zone, gamma, speed sensitivity, filter (the filter / rate interpretation is ours; numbers from controls.ini).</summary>
        private static float Shape(ref float state, float x, float speedKmh, float dt, float minDeadzone)
        {
            AcSeatGlobals g = AcSeatData.G;
            float dz = Mathf.Max(g.PadDeadzone, minDeadzone);
            float filter = Mathf.Clamp(g.PadSteerFilter, 0f, 0.99f);
            float ax = Mathf.Abs(x);
            float v = ax <= dz ? 0f : (ax - dz) / Mathf.Max(1e-3f, 1f - dz);
            v = Mathf.Pow(Mathf.Clamp01(v), Mathf.Max(0.1f, g.PadSteerGamma)) * Mathf.Sign(x);
            v *= 1f / (1f + g.PadSpeedSensitivity * Mathf.Abs(speedKmh) / 100f);    // ours (formula)
            float alpha = 1f - Mathf.Pow(filter, Mathf.Max(0f, dt) * 60f);           // ours: the filter per 60 Hz frame
            float next = Mathf.Lerp(state, v, alpha);
            state = Mathf.MoveTowards(state, next, Mathf.Max(0.01f, g.PadSteerSpeed) * 60f * dt);  // ours: STEER_SPEED per 60 Hz frame
            return state;
        }

        private static float _vrSteer;

        /// <summary>A VR thumbstick steering through the same AC [X360] shaping as the gamepad (own filter state; dead zone at least
        /// <paramref name="minDeadzone"/> - VR sticks drift more than pads).</summary>
        public static float ShapeStick(float x, float speedKmh, float dt, float minDeadzone)
        {
            return Shape(ref _vrSteer, x, speedKmh, dt, minDeadzone);
        }

        /// <summary>Forgets the VR stick's filter state (a hand took the rim, a new drive).</summary>
        public static void ResetStick() { _vrSteer = 0f; }

        /// <summary>A gamepad is bound (read while driving on a monitor).</summary>
        public static bool Bound { get { return _pad != null; } }

        /// <summary>Starts the VR stick's filter from <paramref name="steer"/> (-1..1): the stick takes over from the wheel's release angle, not from 0.</summary>
        public static void SeedStick(float steer) { _vrSteer = Mathf.Clamp(steer, -1f, 1f); }

        private static void Bind()
        {
            if (_current == null)
            {
                if (_typeLooked) return;
                _typeLooked = true;
                Type t = Type.GetType("UnityEngine.InputSystem.Gamepad, Unity.InputSystem");
                if (t == null) { AcLog.Info("Seat: no Input System - gamepads are not read (keyboard and VR still drive)."); _failed = true; return; }
                _current = t.GetProperty("current", BindingFlags.Static | BindingFlags.Public);
                if (_current == null) { _failed = true; return; }
            }

            if (Time.unscaledTime < _nextBind) return;   // once a second whether or not a pad is bound (no per-frame reflection)
            _nextBind = Time.unscaledTime + 1f;
            object pad = _current.GetValue(null, null);
            if (ReferenceEquals(pad, _pad)) return;
            _pad = null;
            if (pad == null) return;
            _stick = Value<Vector2>(pad, "leftStick");
            _rt = Value<float>(pad, "rightTrigger");
            _lt = Value<float>(pad, "leftTrigger");
            _a = Pressed(pad, "buttonSouth");
            _b = Pressed(pad, "buttonEast");
            _x = Pressed(pad, "buttonWest");
            _y = Pressed(pad, "buttonNorth");
            _rb = Pressed(pad, "rightShoulder");
            object dpad = Prop(pad, "dpad");
            _up = dpad != null ? Pressed(dpad, "up") : null;
            _down = dpad != null ? Pressed(dpad, "down") : null;
            if (_stick == null || _rt == null || _lt == null || _a == null || _b == null || _x == null || _y == null || _rb == null || _up == null || _down == null)
            {
                AcLog.WarnOnce("Seat: the gamepad's controls were not all found by name; gamepad driving is off.");
                _failed = true;
                return;
            }

            _pad = pad;
            if (!_said) { _said = true; AcLog.Info("Seat: gamepad bound (AC X360 layout: left stick steer, RT gas, LT brake, A / X shift, B handbrake, Y horn, D-pad up lights)."); }
        }

        private static object Prop(object o, string name)
        {
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static Func<T> Value<T>(object owner, string name)
        {
            object control = Prop(owner, name);
            if (control == null) return null;
            MethodInfo m = control.GetType().GetMethod("ReadValue", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (m == null || m.ReturnType != typeof(T)) return null;
            return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), control, m);
        }

        private static Func<bool> Pressed(object owner, string name)
        {
            object control = Prop(owner, name);
            if (control == null) return null;
            PropertyInfo p = control.GetType().GetProperty("isPressed", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo get = p != null ? p.GetGetMethod() : null;
            return get != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), control, get) : null;
        }
    }
}
