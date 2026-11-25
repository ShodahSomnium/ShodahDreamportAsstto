using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] The VR controllers while seated (UnityEngine.XR through <see cref="AcXr"/> - the Input System is not on the server's list):
    /// - pedals (setting seat.pedals): right trigger gas / left trigger brake (default, the client's own XRI vehicle mapping), swapped, or
    ///   the right stick (up gas, down brake); analog and linear. ARMED only once both triggers were released after getting in, after the
    ///   tablet was used (B / Y press, F's DisarmPedals) - the trigger that pointed at the door or pressed a tablet button is the gas (ETS lesson 6);
    /// - paddles: A shift up; a quick grip click (squeeze and release within 0.3 s) of a hand away from the rim: right = up, left = DOWN (the
    ///   default downshift). Left X shifts down only with the setting seat.vrxdown: X is the client's pose-cancel action, so every press drops
    ///   the sitting pose and the seat re-enters it the next frame (AcSeat logs it once);
    /// - steering: the wheel grab (<see cref="AcWheelGrab"/>), else the left stick X through AC's own [X360] pad shaping (cfg/controls.ini
    ///   STEER_DEADZONE / GAMMA / SPEED_SENSITIVITY / FILTER / SPEED, the same as <see cref="AcPad"/>; dead zone at least 0.15 for VR sticks, ETS);
    /// - left stick pulled back = handbrake, left stick click = headlights, right stick click (stick centred) = horn (all // ours);
    /// - the inside door handle: a grip within reach of the handle point inside the door gets out (if the car is stopped).
    /// B and Y are never ours (the client's tablet opens on a tap; it stays usable while seated - S/NOTES.md).
    /// </summary>
    public static class AcVrDrive
    {
        public const float StickDeadZone = 0.15f; // ours (ETS)
        public const float ArmBelow = 0.05f;      // ours (ETS)
        public const float GripClickSeconds = 0.3f; // ours
        public const float HandleReach = 0.15f;   // ours
        public const float RearmDelay = 0.5f;     // ours: a B / Y press holds the pedals at least this long (the tablet starts opening)
        private static bool _armed;
        private static readonly float[] _gripDownAt = { -1f, -1f };
        private static bool _saidArmed, _saidHandle;
        private static bool _handleNear;
        // the B / Y disarm lasts: re-armed only after RearmDelay, with the tablet seen closed (link known) or assumed closed (taps toggle it while
        // the link never reported the tablet: a failed / missing link must not turn the trigger that presses a tablet button into gas)
        private static float _secondaryAt = -10f;
        private static bool _tabletAssumedOpen, _linkSeen, _saidAssumed;

        public static bool Armed { get { return _armed; } }

        /// <summary>When left X (primary) was last pressed while seated (unscaled time) - AcSeat ties a pose drop to it.</summary>
        public static float LastLeftPrimaryAt { get; private set; } = -10f;

        public static void Disarm() { _armed = false; }

        /// <summary>A new drive: the tablet is taken as closed (the seat starts disarmed anyway).</summary>
        internal static void OnSeated() { _armed = false; _tabletAssumedOpen = false; _secondaryAt = -10f; _gripDownAt[0] = -1f; _gripDownAt[1] = -1f; }

        /// <summary>The VR part of this frame's request (driver), speed unknown (stick shaping as at standstill).</summary>
        public static void Read(ref AcDriveRequest r) { Read(ref r, 0f); }

        /// <summary>The VR part of this frame's request (driver) at <paramref name="speedKmh"/> (AC's pad speed sensitivity for the stick).</summary>
        public static void Read(ref AcDriveRequest r, float speedKmh)
        {
            AcHand left = AcXr.Hand(false), right = AcXr.Hand(true);
            NoteX(left);
            bool tabletUp = AcDriveInput.TabletUp;
            if (AcLocal.TabletOpen) _linkSeen = true;
            bool secondary = left.SecondaryPressed || right.SecondaryPressed;
            if (secondary)
            {
                _armed = false;                     // the tablet is opening / closing (B / Y)
                _secondaryAt = Time.unscaledTime;
                _tabletAssumedOpen = !_tabletAssumedOpen;
            }

            if (!_armed && !secondary && left.Trigger < ArmBelow && right.Trigger < ArmBelow && !tabletUp && TabletClosed())
            {
                _armed = true;
                if (!_saidArmed) { _saidArmed = true; AcLog.Info("Seat: VR pedals armed (both triggers released)."); }
            }

            if (_armed)
            {
                switch (AcSeatSettings.Pedals)
                {
                    case 1: r.Throttle = Mathf.Max(r.Throttle, left.Trigger); r.Brake = Mathf.Max(r.Brake, right.Trigger); break;
                    case 2:
                        float y = right.Stick.y;
                        if (y > StickDeadZone) r.Throttle = Mathf.Max(r.Throttle, (y - StickDeadZone) / (1f - StickDeadZone));
                        if (y < -StickDeadZone) r.Brake = Mathf.Max(r.Brake, (-y - StickDeadZone) / (1f - StickDeadZone));
                        break;
                    default: r.Throttle = Mathf.Max(r.Throttle, right.Trigger); r.Brake = Mathf.Max(r.Brake, left.Trigger); break;
                }
            }

            if (right.PrimaryPressed) r.ShiftUp = true;
            if (left.PrimaryPressed && AcSeatSettings.XShiftsDown) r.ShiftDown = true;
            if (tabletUp) { _gripDownAt[0] = -1f; _gripDownAt[1] = -1f; }   // a squeeze on the tablet (grab / move it) never finishes as a shift
            else
            {
                if (GripClick(true, right)) r.ShiftUp = true;
                if (GripClick(false, left)) r.ShiftDown = true;
            }
            if (left.StickClickPressed) r.LightsToggle = true;
            if (right.StickClick && right.Stick.sqrMagnitude < 0.25f && AcSeatSettings.Pedals != 2) r.Horn = true;
            if (left.Stick.y < -0.5f && AcSeatSettings.VrSteer != 2) r.Handbrake = Mathf.Max(r.Handbrake, Mathf.Clamp01((-left.Stick.y - 0.5f) / 0.4f));

            // the stick steers while no hand is on the rim (always in stick mode), shaped like AC's pad
            if (AcWheelGrab.Holding == 0)
            {
                float steer = AcPad.ShapeStick(left.Stick.x, speedKmh, Time.deltaTime, StickDeadZone);
                if (Mathf.Abs(steer) > 1e-3f) { r.Steer = steer; r.Steering = true; }
            }
            else AcPad.ResetStick();
        }

        /// <summary>After a B / Y press: the tablet is taken as closed again (pedals may re-arm).</summary>
        private static bool TabletClosed()
        {
            if (Time.unscaledTime - _secondaryAt < RearmDelay) return false;
            if (_linkSeen) return !AcLocal.TabletOpen;
            if (_tabletAssumedOpen && !_saidAssumed) { _saidAssumed = true; AcLog.Info("Seat: the tablet's state is unknown (no tablet link) - after B / Y the pedals wait for the next B / Y tap (taken as the tablet closing)."); }
            return !_tabletAssumedOpen;
        }

        /// <summary>Seated passengers / drivers: remember left X presses (the client's pose cancel) for AcSeat's one-time explanation.</summary>
        internal static void NoteX(AcHand left)
        {
            if (left.PrimaryPressed) LastLeftPrimaryAt = Time.unscaledTime;
        }

        private static bool GripClick(bool right, AcHand h)
        {
            int i = right ? 1 : 0;
            float now = Time.unscaledTime;
            if (h.GripPressed) _gripDownAt[i] = AcWheelGrab.FromRim(right) > AcWheelGrab.Reach * 1.5f && !_handleNear ? now : -1f;
            if (h.GripReleased && _gripDownAt[i] >= 0f)
            {
                bool quick = now - _gripDownAt[i] <= GripClickSeconds && !AcWheelGrab.HandHolding(right);
                _gripDownAt[i] = -1f;
                return quick;
            }

            return false;
        }

        /// <summary>After the rig was placed (VR, any seat): the inside door handle.</summary>
        public static void AfterRigPlaced(AcSeatGeometry g)
        {
            _handleNear = false;
            if (!AcSeat.IsDriver) NoteX(AcXr.Hand(false));
            if (g == null || !g.Alive || AcDriveInput.TabletUp) return;   // the hand on the tablet is not on the door
            Vector3 seat;
            if (!g.SeatModel(AcSeat.Kind, out seat)) return;
            float side = seat.x < 0f ? -1f : 1f;
            if (Mathf.Abs(seat.x) < 0.05f) side = -1f;
            // the inside door handle: on the door's inner panel beside the seat, 0.25 m above the hips, 0.35 m ahead of them // ours (AC has no
            // inside handle: it never gets out). The panel is 0.12 m inside C's outer handle point (door skin) when C found the door, else
            // 0.15 m inside the model bounds (which include the mirrors) // ours
            float panelX = Mathf.Max(Mathf.Abs(seat.x) + 0.25f, g.DoorSkinX(AcSeat.Kind) - 0.12f);
            Vector3 handle = g.ModelToWorld(new Vector3(side * panelX, seat.y + 0.25f, seat.z + 0.35f));
            Transform hand = side < 0f ? AcSeat.LeftHand : AcSeat.RightHand;
            if (hand == null) return;
            bool right = side > 0f;
            float d = (hand.position - handle).magnitude;
            _handleNear = d < HandleReach;
            AcHand h = AcXr.Hand(right);
            if (_handleNear && h.GripPressed && !AcWheelGrab.HandHolding(right))
            {
                if (!_saidHandle) { _saidHandle = true; AcLog.Info("Seat: the inside door handle was gripped (" + d.ToString("0.00") + " m) - getting out."); }
                AcXr.Pulse(right, 0.3f, 0.05f);
                AcDriveInput.ExitPressed = true;
            }
        }
    }
}
