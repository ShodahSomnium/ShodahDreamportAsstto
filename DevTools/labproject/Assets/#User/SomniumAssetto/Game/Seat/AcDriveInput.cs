using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] The local driver's AcCarInput producer: VR (wheel grab angle, triggers = throttle/brake, paddles A/X, grip-click), monitor
    /// keys (W/S or arrows, A/D, Q/E or Shift/Ctrl, Space handbrake, H lights, R reset), gamepad via the Input System by action/reflection.
    /// Called once a frame while the player drives; writes into <c>car.Physics.Input</c>. Owner: S.
    /// Rules: pedals / steering are levels written every frame; the edges (ShiftUp / ShiftDown / ResetRequest) are only ever SET here and
    /// cleared by P after acting (a frame without a FixedUpdate must not lose a shift). Keyboard / pad steering is RELATIVE and already through
    /// AC's own controls.ini filters (AcKeys / AcPad); VR hands on the rim give the ABSOLUTE wheel angle (SteerAbsolute + SteerWheelDeg).
    /// </summary>
    public static class AcDriveInput
    {
        private static bool _lights;
        private static bool _seededLights;
        private static int _tabletFrame = -100;
        private static AcDriveRequest _r;
        private static bool _saidVr, _saidMonitor, _saidAbs, _saidHold;
        // VR wheel release: the angle the hands left the rim at returns to centre at controls.ini [KEYBOARD] STEER_RESET_SPEED x lock per second
        // (no one-frame snap when both hands let go, tracking drops or the tablet opens); the stick takes over from that angle
        private static bool _wasHeld, _releasing, _saidRelease;
        private static float _lastHeldDeg, _releaseDeg;

        /// <summary>Set by any device / the VR inside door handle: the seat gets out at its next early frame.</summary>
        public static bool ExitPressed;

        /// <summary>F holds the car while the tablet is up (calls <see cref="DisarmPedals"/> every frame): true for 2 frames after the last call.</summary>
        public static bool TabletUp { get { return Time.frameCount - _tabletFrame <= 2 || FrontHolds(); } }

        private static bool _frontBroken;

        /// <summary>F's AcFront.HoldCar, guarded (a failing F holds nothing and logs once).</summary>
        private static bool FrontHolds()
        {
            if (_frontBroken) return false;
            try { return AcFront.HoldCar; }
            catch (Exception e) { _frontBroken = true; AcLog.Warn("Seat: reading AcFront.HoldCar failed (" + e.GetType().Name + ": " + e.Message + "); the tablet no longer holds the car."); return false; }
        }

        /// <summary>The last request (lab / HUD).</summary>
        public static AcDriveRequest Last { get { return _r; } }

        /// <summary>Reads the devices and writes the controls of <paramref name="car"/> (no-op when not driving it).</summary>
        public static void Produce(AcCarRig car)
        {
            if (car == null || car.Physics == null || !AcSeat.Seated || !AcSeat.IsDriver || !ReferenceEquals(car, AcSeat.SeatedCar)) return;
            AcCarPhysics ph = car.Physics;
            float kmh = ph.SpeedKmh;
            _r = default(AcDriveRequest);
            bool vr = AcSeat.Vr;
            if (vr)
            {
                AcVrDrive.Read(ref _r, kmh);
                if (!_saidVr) { _saidVr = true; AcLog.Info("Seat: driving in VR (pedals " + PedalText() + ", A / right grip-click up, left grip-click down" + (AcSeatSettings.XShiftsDown ? ", X down" : "") + ", wheel " + (AcSeatSettings.VrSteer == 2 ? "stick" : "grab") + ")."); }
            }
            else
            {
                AcKeys.Read(kmh, ref _r);
                AcPad.Read(kmh, ref _r);
                if (!_saidMonitor) { _saidMonitor = true; AcLog.Info("Seat: driving on a monitor (W/S or arrows, A/D, E/Shift up, Q/Ctrl down, Space handbrake, H/L lights, R reset, C camera, F get out; AC keyboard filter " + (AcSeatData.Globals != null && AcSeatData.Globals.ControlsFromFile ? "from cfg/controls.ini" : "defaults") + ")."); }
            }

            bool tabletUp = TabletUp;
            if (tabletUp)
            {
                // F: the tablet / app is open (its buttons are pressed with the trigger = gas): no gas; the brake is held while stopped so the car
                // does not roll in the pits, and left alone (coasting) while moving // ours
                _r.Throttle = 0f;
                _r.Brake = Mathf.Abs(kmh) < 2f ? 1f : 0f;
                if (vr)
                {
                    // VR: the controllers work the tablet (grip = grab / move it, sticks, A) - nothing else of theirs reaches the car either
                    _r.ShiftUp = false; _r.ShiftDown = false; _r.LightsToggle = false; _r.Horn = false; _r.Exit = false;
                    _r.Handbrake = 0f; _r.Steering = false;
                }
                if (!_saidHold) { _saidHold = true; AcLog.Info("Seat: the car is held while the tablet is open (no gas; brake held when stopped)."); }
            }

            AcCarInput input = ph.Input;
            input.Throttle = Mathf.Clamp01(_r.Throttle);
            input.Brake = Mathf.Clamp01(_r.Brake);
            input.Handbrake = Mathf.Clamp01(_r.Handbrake);
            input.Clutch = Mathf.Clamp01(_r.Clutch);
            int held = AcWheelGrab.Holding;
            float lockDeg = Mathf.Max(1f, AcSeat.Geometry != null ? AcSeat.Geometry.SteerLockDeg : 450f);
            if (vr && held != 0)
            {
                input.SteerAbsolute = true;
                input.SteerWheelDeg = AcWheelGrab.WheelDeg;
                input.Steer = Mathf.Clamp(AcWheelGrab.WheelDeg / lockDeg, -1f, 1f);
                _wasHeld = true;
                _releasing = false;
                _lastHeldDeg = AcWheelGrab.WheelDeg;
                if (!_saidAbs) { _saidAbs = true; AcLog.Info("Seat: absolute steering from the hands on the rim (SteerWheelDeg)."); }
            }
            else if (vr && Releasing(lockDeg))
            {
                input.SteerAbsolute = true;
                input.SteerWheelDeg = _releaseDeg;
                input.Steer = Mathf.Clamp(_releaseDeg / lockDeg, -1f, 1f);
            }
            else
            {
                // keys / pad / VR stick (already through AC's own filters); no device steering = centred
                input.SteerAbsolute = false;
                input.Steer = _r.Steering ? Mathf.Clamp(_r.Steer, -1f, 1f) : 0f;
            }

            if (_r.ShiftUp) input.ShiftUp = true;
            if (_r.ShiftDown) input.ShiftDown = true;
            if (_r.Reset) input.ResetRequest = true;
            if (!_seededLights) { _seededLights = true; _lights = (ph.Telemetry.Lights & AcLightState.Headlights) != 0; }
            if (_r.LightsToggle) _lights = !_lights;
            input.Lights = _lights;
            input.Horn = _r.Horn;
            input.GearRequest = AcCarInput.NoGearRequest;
            ph.Input = input;
            if (_r.Exit) ExitPressed = true;
            if (_r.Camera) AcCarCamera.Cycle();
            AcLocal.PedalsArmed = !vr || AcVrDrive.Armed;
        }

        /// <summary>No hand on the rim this frame (VR): true while the angle the hands let go at is still returning to centre. The return rate is
        /// controls.ini [KEYBOARD] STEER_RESET_SPEED (normalised lock per second, AC's own self-centring of digital steering); the left stick
        /// takes over the moment it steers (its filter starts from the wheel's angle).</summary>
        private static bool Releasing(float lockDeg)
        {
            if (_wasHeld)
            {
                _wasHeld = false;
                _releaseDeg = _lastHeldDeg;
                _releasing = Mathf.Abs(_releaseDeg) > 0.5f;   // ours: half a degree is centred
                if (_releasing && !_saidRelease) { _saidRelease = true; AcLog.Info("Seat: the wheel was let go at " + _releaseDeg.ToString("0") + " deg - it returns to centre at controls.ini STEER_RESET_SPEED (no snap)."); }
            }

            if (!_releasing) return false;
            if (_r.Steering)
            {
                // the stick steers: hand over, its filter continuing from this angle (this frame still shows the wheel's angle)
                AcPad.SeedStick(_releaseDeg / lockDeg);
                _releasing = false;
                return true;
            }

            _releaseDeg = Mathf.MoveTowards(_releaseDeg, 0f, Mathf.Max(0.1f, AcSeatData.G.KeyResetSpeed) * lockDeg * Time.deltaTime);
            if (Mathf.Abs(_releaseDeg) <= 0.5f) { _releasing = false; _releaseDeg = 0f; }
            return true;
        }

        /// <summary>A passenger's frame: only get out (F / D-pad down / the inside door handle) and the camera.</summary>
        public static void PassengerKeys()
        {
            AcDriveRequest r = default(AcDriveRequest);
            if (!AcSeat.Vr) AcKeys.ReadPassenger(ref r);
            if (r.Exit) ExitPressed = true;
        }

        /// <summary>Ignores the pedals until both triggers are released (after getting in / while the tablet is up - ETS lesson).
        /// F calls it when the tablet closes (and may every frame while it shows); AcFront.HoldCar holds the pedals and the wheel grab meanwhile.</summary>
        public static void DisarmPedals()
        {
            _tabletFrame = Time.frameCount;
            AcVrDrive.Disarm();
            if (AcWheelGrab.Holding != 0) AcWheelGrab.LetGo();
        }

        /// <summary>The seat took the player (a new drive): pedals disarmed, filters reset, lights from the car.</summary>
        internal static void OnSeated()
        {
            _wasHeld = false;
            _releasing = false;
            AcVrDrive.OnSeated();
            AcKeys.Reset();
            AcPad.ResetStick();
            ExitPressed = false;
            _seededLights = false;
        }

        private static string PedalText()
        {
            switch (AcSeatSettings.Pedals)
            {
                case 1: return "left trigger gas / right trigger brake";
                case 2: return "right stick";
                default: return "right trigger gas / left trigger brake";
            }
        }
    }
}
