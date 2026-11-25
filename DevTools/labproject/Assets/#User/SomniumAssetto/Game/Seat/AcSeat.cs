using System;
using System.Collections;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Getting in and out of cars and sitting in them - the Euro Truck Sim Dreamport's client-proven seat (EtsTruckSeat) with
    /// Halo's early/late frame writes (HaloVehicleDirector), for AC cars:
    /// - Getting in: <see cref="Enter"/> (door handle click / VR trigger or grip on the door (<see cref="AcDoors"/>), E on a monitor near
    ///   the car, R's grid, the tablet). Order matters (ETS TrySeat): the car first - <c>Physics.SetOccupied(true)</c> so every car collider
    ///   already ignores the avatar layers 2|7|24 when the avatar arrives, and <c>SetDriven(true)</c> for the driver - then the Bridge locks
    ///   (collision, fly, teleport off, speed / gravity / jump 0, falling animation and VRIK leg stepping off), then the SittingStraight pose.
    ///   The car's own door animation (car_DOOR_L/R.ksanim through C's SetDoor) and door sound (A) play while the player sits down.
    /// - Sitting: the pose is re-entered every seated frame (the client drops it on its cancel action: left X, Escape, avatar reload);
    ///   in VR the pose's re-centring of the head AND both hand offsets is undone (ETS lesson 7). The body root is written every frame
    ///   early (order -90, before the client's order-0 avatar placement) and again late (order 9000, before VRIK at 9998): position = the
    ///   seat (knh RIG_Hips in the car), rotation = the car's HEADING ONLY - never pitch or roll in VR (the ETS cab-tilt follow was "VERY
    ///   disorientating"); option vr.horizonlock off lets a MONITOR body tilt with the car. VR: the rig is moved so the eyes land on the
    ///   eye point (system/cfg/vr.ini [car] DRIVEREYES, else car.ini DRIVEREYES), keeping the head's own lean up to 0.45 m (ETS PlaceHead).
    /// - Getting out: <see cref="Exit"/> only stopped (passenger below 10 km/h): door opens, every Bridge lock released, stood 1.6 m outside
    ///   the door on the ground through DoTeleportToPoint, <c>SetOccupied(false)</c> one second later (ETS _solidAgainAt).
    /// Started by R (AcRace calls <see cref="Start"/>); the per-frame work runs in S's drivers added to the AcGame object
    /// (<see cref="AcSeatEarly"/> -90, <see cref="AcSeatLate"/> 9000, <see cref="AcSeatCarry"/> 9990, <see cref="AcSeatPosesDriver"/> 9997, <see cref="AcSeatFinal"/> 10000).
    /// In the editor there is no Bridge player: the lab stands in with <see cref="LabRoot"/> / <see cref="LabHead"/> / hands.
    /// </summary>
    public static class AcSeat
    {
        /// <summary>The local player sits in a car.</summary>
        public static bool Seated { get; private set; }
        /// <summary>The car sat in (null on foot).</summary>
        public static AcCarRig SeatedCar { get; private set; }
        /// <summary>In the driver seat (the local player produces the car's AcCarInput).</summary>
        public static bool IsDriver { get; private set; }

        /// <summary>Sat in (car, seat) - also raised as AcEvents.SeatChanged.</summary>
        public static event Action<AcCarRig, AcSeatKind> Entered;
        /// <summary>Got out of (car).</summary>
        public static event Action<AcCarRig> Exited;

        // ---------------------------------------------------------------- added members (S)

        /// <summary>Which seat (valid while <see cref="Seated"/> or <see cref="Pending"/>).</summary>
        public static AcSeatKind Kind { get; private set; }
        /// <summary>A seat was asked for and is not taken yet (waiting for the Bridge body, or for the car owner's answer to a ride request).</summary>
        public static bool Pending { get; private set; }
        /// <summary>The headset path (the client's XR, or the lab's simulated headset).</summary>
        public static bool Vr { get { return LabVr || AcPlayer.XrPresent; } }
        /// <summary>The module runs (Start was called).</summary>
        public static bool Started { get; private set; }
        /// <summary>The seat's geometry this frame (null on foot).</summary>
        public static AcSeatGeometry Geometry { get { return _geo; } }

        /// <summary>Lab hooks (null in the world): stand-ins for the Bridge body root, head (the VR camera / the head anchor) and hands.</summary>
        public static Transform LabRoot, LabHead, LabLeft, LabRight;
        /// <summary>Lab: behave as in a headset.</summary>
        public static bool LabVr;

        /// <summary>A passenger may get out (and the driver grant a ride) below this speed. ETS EtsRiders.PassengerExitMaxKmh (Q-B2, 10 km/h).</summary>
        public const float PassengerExitKmh = 10f; // ours (ETS)
        /// <summary>The driver gets out only when the car is stopped. // ours</summary>
        public const float DriverExitKmh = 3f; // ours
        /// <summary>How far the head may lean from where it sat down (ETS LeanLimit).</summary>
        public const float LeanLimit = 0.45f; // ours (ETS)
        /// <summary>Standing point outside the door (ETS StandBeside).</summary>
        public const float StandOut = 1.6f; // ours (ETS)

        private const string PoseOffsetName = "HeadCustomPoseOffset";

        // body (Bridge or lab)
        private static Transform _root, _head, _left, _right, _rig;
        private static ISomniumPlayerMotion _motion;
        private static ISomniumPlayerAnimation _animation;
        private static ISomniumPlayerPoses _poses;
        private static ISomniumPlayerAvatar _avatar;
        private static Transform _poseOffset, _poseOffsetFrom, _leftHandOffset, _rightHandOffset;
        private static bool _forceSitting = true, _poseEntered, _poseReported, _recentred, _restKnown;
        private static Vector3 _headRest;
        private static bool _locked;

        // the seat
        private static AcSeatGeometry _geo;
        private static AcCarRig _wantCar;
        private static AcSeatKind _wantSeat;
        private static float _wantUntil;
        private static float _seatedAt;
        private static bool _entryReported;
        private static AcGame _game;
        private static Pose _lastDrawn;
        private static Vector3 _lastSeatModel;
        private static bool _haveLastDrawn;
        private static float _placedRootYaw;
        private static Vector3 _placedRoot;
        private static int _placedFrame = -1;

        // doors and the deferred "solid again"
        private static AcCarRig _doorCar;
        private static AcSide _doorSide;
        private static float _doorT0 = -10f;
        private static bool _doorClosingSaid;
        private static AcCarRig _solidAgainCar;
        private static float _solidAgainAt;
        private static Coroutine _landing;

        // refusals
        private static string _lastRefusal;
        private static float _lastRefusalAt;

        private static bool _ticksFirstPerson;

        /// <summary>Starts the seat module (adds its drivers to <paramref name="game"/>). Idempotent.</summary>
        public static void Start(AcGame game)
        {
            if (Started || game == null) return;
            Started = true;
            _game = game;
            game.AddOnce<AcSeatEarly>();
            game.AddOnce<AcSeatLate>();
            game.AddOnce<AcSeatCarry>();
            game.AddOnce<AcSeatPosesDriver>();
            game.AddOnce<AcSeatFinal>();
            AcSeatSettings.Register();
            AcGame.Guard("seat: session hooks", Subscribe);
            AcLog.Info("Seat: started (seat writes at -90 / 9000 / 10000, monitor avatar carry 9990, poses 9997; vr " + (Vr ? "yes" : "no") + "; horizon lock " + (AcSeatSettings.HorizonLock ? "on" : "off") +
                       "; the Somnium tablet stays usable while seated - B / Y are not ours - and the VR pedals disarm while it is used).");
        }

        private static void Subscribe()
        {
            AcSession.Unloading += OnUnloading;
            AcSeatDamagePulse.Hook();     // iteration 2: controller pulse on a damage hit (optional, DAMAGE_DESIGN 8)
            AcGame.Guard("seat: N's seated-car swap hook", HookSwap);
        }

        private static Func<AcCarRig, AcCarRig, bool> _swapHook;

        // N rebuilds a remote car with its high-res cockpit (Interior = true) when the local player rides in one built without it; S moves the
        // seat onto the new rig (REQUESTS "N -> S: optional hook ... SwapSeatedCar")
        private static void HookSwap()
        {
            if (_swapHook == null) _swapHook = SwapSeated;
            AcNetGame.SwapSeatedCar = _swapHook;
        }

        private static void UnhookSwap()
        {
            if (_swapHook != null && ReferenceEquals(AcNetGame.SwapSeatedCar, _swapHook)) AcNetGame.SwapSeatedCar = null;
        }

        /// <summary>N's swap: the car I sit in (<paramref name="old"/>) is replaced by <paramref name="rig"/> (same owner, same pose this frame; N
        /// disposes the old one when this returns true). The seat, its geometry, the door, the viewer state and AcLocal move over; nothing is
        /// released (no pose exit, no Bridge unlock, no door). False = keep the old rig.</summary>
        internal static bool SwapSeated(AcCarRig old, AcCarRig rig)
        {
            if (!Seated || old == null || rig == null || rig.Root == null || !ReferenceEquals(SeatedCar, old) || ReferenceEquals(old, rig)) return false;
            AcSeatKind seat = Kind;
            if (rig.Physics != null) Occupy(rig.Physics, true);
            SeatedCar = rig;
            AcLocal.SeatCar = rig;
            _geo = AcSeatGeometry.For(rig);
            if (ReferenceEquals(_doorCar, old)) _doorCar = rig;
            if (ReferenceEquals(_solidAgainCar, old)) _solidAgainCar = null;
            AcCarCamera.Refresh();      // C's SetViewer(true) / A's interior on the new rig
            AcLog.Info("Seat: N rebuilt the car I ride in (" + (rig.Car != null ? rig.Car.Id : "?") + ", " + (seat == AcSeatKind.Driver ? "driver" : "passenger") + ") with its cockpit - the seat moved over.");
            AcEvents.RaiseSeatChanged(rig, seat);
            return true;
        }

        private static void OnUnloading()
        {
            if (Seated || Pending) ForceExit();
        }

        /// <summary>Puts the local player into <paramref name="car"/>'s <paramref name="seat"/> (door animation, seat recipe). False when refused
        /// (seat taken, car moving, no Bridge body); the reason goes to AcEvents.Message.</summary>
        public static bool Enter(AcCarRig car, AcSeatKind seat)
        {
            if (car == null || car.Root == null) return Refuse("there is no car to get into", 2f);
            if (Seated)
            {
                if (ReferenceEquals(car, SeatedCar) && seat == Kind) return true;
                return Refuse("you are already in a car - get out first to change seats", 3f);
            }

            if (seat == AcSeatKind.Driver && car.Kind != AcCarKind.Local)
                return Refuse(car.Kind == AcCarKind.Remote ? "only its owner drives that car - the passenger door is the way in" : "that car is not yours to drive", 3f);
            if (car.Kind == AcCarKind.Ai || car.Kind == AcCarKind.Showroom) return Refuse("that car cannot be sat in", 2f);

            AcSeatGeometry g = AcSeatGeometry.Make(car);
            if (seat == AcSeatKind.Passenger)
            {
                if (car.Model != null && car.Model.Root != null && !g.HasPassengerSeat && car.Model.Bounds.size.x > 0.5f)
                    return Refuse("that car has no passenger seat", 3f);
                if (g.SpeedKmh > PassengerExitKmh) return Refuse("wait until the car stops", 2f);
                if (car.Kind == AcCarKind.Remote) return AcRiders.AskForRide(car);
            }
            else if (g.SpeedKmh > PassengerExitKmh && (car.Physics == null || !car.Physics.Frozen))
            {
                return Refuse("the car is moving", 2f);
            }

            _wantCar = car;
            _wantSeat = seat;
            _wantUntil = Time.unscaledTime + 15f;      // ETS FindPlayer: the Bridge body may arrive late
            Pending = true;
            Kind = seat;
            TrySeat();
            return true;
        }

        /// <summary>The ride request was granted (AcRiders): sit in the remote car's passenger seat now.</summary>
        internal static bool EnterGranted(AcCarRig car)
        {
            if (car == null || car.Root == null || Seated) return false;
            _wantCar = car;
            _wantSeat = AcSeatKind.Passenger;
            _wantUntil = Time.unscaledTime + 15f;
            Pending = true;
            Kind = AcSeatKind.Passenger;
            TrySeat();
            return true;
        }

        internal static void SetPending(bool pending, AcSeatKind kind)
        {
            if (Seated) return;
            Pending = pending;
            Kind = kind;
        }

        /// <summary>Whether getting out is allowed now, and why not (tablet greys its button with the reason).</summary>
        public static bool CanExit(out string reason)
        {
            reason = null;
            if (!Seated) { reason = "you are not in a car"; return false; }
            float kmh = _geo != null ? _geo.SpeedKmh : 0f;
            if (Kind == AcSeatKind.Passenger && kmh > PassengerExitKmh) { reason = "wait until the car slows down to get out"; return false; }
            if (Kind == AcSeatKind.Driver && kmh > DriverExitKmh) { reason = "stop the car to get out"; return false; }
            if (Kind == AcSeatKind.Driver && RaceHoldsDriver(out reason)) return false;
            return true;
        }

        /// <summary>AC keeps a racer in the car from the grid to the flag: refused in the own car while R's session is Grid or Countdown (R
        /// re-seats the driver every second then), and while Racing until the local player finished - the same window in which R refuses
        /// "back to pits". Not for a late joiner (AcRace.Spectating). R's own exits (leaving the track, unload) use <see cref="ForceExit"/>,
        /// which is never refused.</summary>
        private static bool RaceHoldsDriver(out string reason)
        {
            reason = null;
            AcCarRig car = SeatedCar;
            if (car == null || car.Kind != AcCarKind.Local) return false;
            AcPhase phase;
            bool finished, spectating;
            try { phase = AcSession.Phase; finished = AcLocal.Finished; spectating = AcRace.Spectating; }
            catch (Exception e) { AcLog.WarnOnce("Seat: reading R's race state failed (" + e.Message + "); getting out is not held for races."); return false; }
            if (spectating) return false;      // a late joiner's car waits in the pit box: free to get out
            if (phase == AcPhase.Grid || phase == AcPhase.Countdown) { reason = "the race is about to start - you stay in your car on the grid"; return true; }
            if (phase == AcPhase.Racing && !finished) { reason = "the race is running - you stay in your car until you finish"; return true; }
            return false;
        }

        /// <summary>Gets out (stopped car: door opens, stand beside the car on the ground; refused above the passenger exit speed). False when refused.</summary>
        public static bool Exit()
        {
            string why;
            if (!CanExit(out why)) return Refuse(why, 2f);
            AcCarRig car = SeatedCar;
            AcSeatKind seat = Kind;
            AcSide side = _geo != null ? _geo.SideOf(seat) : AcSide.Left;
            AcSeatGeometry g = _geo;
            StartDoor(car, side, true);
            Release("got out");
            bool moved = StandBeside(g, seat);
            AcLog.Info("Seat: got out at " + Fmt(_standAt) + (moved ? "" : " (the Bridge would not move the player)") + ".");
            return true;
        }

        /// <summary>Gets out at once without the door (session unload, car destroyed): releases every Bridge lock. Never fails.</summary>
        public static void ForceExit()
        {
            _wantCar = null;
            bool wasPending = Pending;
            Pending = false;
            if (wasPending && !Seated) AcRiders.CancelAsk();
            if (!Seated) return;
            AcSeatGeometry g = _geo;
            AcSeatKind seat = Kind;
            bool carAlive = g != null && g.Alive;
            Release("forced out");
            if (carAlive) StandBeside(g, seat);
            AcLog.Info("Seat: forced out of the car" + (carAlive ? " at " + Fmt(_standAt) : " (the car is gone)") + ".");
        }

        /// <summary>Stops the module (world closing): ForceExit + remove drivers.</summary>
        public static void Stop()
        {
            try { ForceExit(); } catch (Exception e) { AcLog.Error("Seat: force exit on stop failed: " + e); }
            try { AcSession.Unloading -= OnUnloading; } catch (Exception) { /* nothing */ }
            AcSeatDamagePulse.Unhook();
            try { UnhookSwap(); } catch (Exception) { /* N gone */ }
            AcDoors.Clear();
            AcRiders.Stop();
            AcSeatPoses.Stop();
            AcCarCamera.HandBack();
            AcSeatSettings.FlushCamera();
            AcHeadHide.Unhook(); // the static render event must not outlive the world
            if (_ticksFirstPerson) { _ticksFirstPerson = false; AcFirstPerson.Release(); }
            if (_game != null)
            {
                DestroyDriver<AcSeatEarly>(_game);
                DestroyDriver<AcSeatLate>(_game);
                DestroyDriver<AcSeatCarry>(_game);
                DestroyDriver<AcSeatPosesDriver>(_game);
                DestroyDriver<AcSeatFinal>(_game);
            }

            Started = false;
            _game = null;
        }

        private static void DestroyDriver<T>(AcGame game) where T : Component
        {
            try { T c = game.GetComponent<T>(); if (c != null) UnityEngine.Object.Destroy(c); } catch (Exception) { /* closing */ }
        }

        // ================================================================ taking the avatar over (ETS TrySeat, order matters)

        private static bool FindBody()
        {
            _root = _head = _left = _right = null;
            _motion = null; _animation = null; _poses = null; _avatar = null;
            if (LabRoot != null)
            {
                _root = LabRoot; _head = LabHead; _left = LabLeft; _right = LabRight;
                return true;
            }

            try
            {
                ISomniumPlayer player = SomniumBridge.PlayersContainer != null ? SomniumBridge.PlayersContainer.LocalPlayer : null;
                ISomniumPlayerBody body = player != null && player.References != null ? player.References.Body : null;
                if (body == null || body.Root == null) return false;
                _root = body.Root;
                _head = body.Head;
                _left = body.LeftHand;
                _right = body.RightHand;
                ISomniumPlayerFeatures f = player.Features;
                if (f != null)
                {
                    _motion = f.Motion;
                    _animation = f.Animation;
                    _poses = f.Poses;
                    _avatar = f.Avatar;
                }
            }
            catch (Exception e)
            {
                AcLog.WarnOnce("Seat: reading the Bridge player failed (" + e.GetType().Name + ": " + e.Message + ").");
                return false;
            }

            return _root != null;
        }

        private static void TrySeat()
        {
            AcCarRig car = _wantCar;
            if (car == null || car.Root == null) { Pending = false; _wantCar = null; Refuse("the car is gone", 2f); return; }
            if (!FindBody())
            {
                if (Time.unscaledTime > _wantUntil)
                {
                    Pending = false;
                    _wantCar = null;
                    AcLog.Info("Seat: refused - no Somnium player body to seat (editor run, or the Bridge gave no body in 15 s).");
                }

                return;
            }

            AcSeatKind seat = _wantSeat;
            _wantCar = null;
            Pending = false;
            _geo = AcSeatGeometry.For(car);

            // (1) the car first: its colliders ignore the avatar before the avatar arrives; the driver wakes it
            AcCarPhysics ph = car.Physics;
            if (ph != null)
            {
                Occupy(ph, true);
                if (seat == AcSeatKind.Driver) Drive(ph, true);
            }

            if (ReferenceEquals(_solidAgainCar, car)) _solidAgainCar = null;

            // (2) the Bridge locks (Halo HaloVehicleSeat l.87-93 / ETS TrySeat, proven members)
            Lock(true);

            Seated = true;
            SeatedCar = car;
            IsDriver = seat == AcSeatKind.Driver;
            Kind = seat;
            _seatedAt = Time.unscaledTime;
            _entryReported = false;
            _poseEntered = _poseReported = _recentred = false;
            _restKnown = false;
            _rig = null;
            _poseOffsetFrom = null;
            _forceSitting = true;
            _haveLastDrawn = false;
            AcDriveInput.OnSeated();
            AcWheelGrab.LetGo();
            AcDoors.HideAll();

            AcLocal.Seated = true;
            AcLocal.Driving = IsDriver;
            AcLocal.SeatCar = car;
            AcLocal.Seat = seat;
            AcCarCamera.OnSeated();

            StartDoor(car, _geo.SideOf(seat), false);
            if (!Vr && !Application.isEditor) _ticksFirstPerson = true;
            RaiseEntered(car, seat);
        }

        private static void Lock(bool on)
        {
            if (on == _locked) return;
            _locked = on;
            try
            {
                if (_motion != null)
                {
                    _motion.SetCollisionDisableState(on);
                    _motion.SetFlyModeDisableState(on);
                    _motion.SetTeleportationDisableState(on);
                    float k = on ? 0f : 1f;
                    _motion.SetMovementSpeed(k, k, k);
                    _motion.SetGravity(k, k);
                    _motion.SetJump(k, k);
                }

                if (_animation != null) _animation.SetFallingAnimationDisabled(on);
                if (_avatar != null) _avatar.SetVRIKLegMovementDisabled(on);
            }
            catch (Exception e)
            {
                AcLog.Warn("Seat: the Bridge " + (on ? "locks" : "releases") + " threw (" + e.GetType().Name + ": " + e.Message + ").");
            }
        }

        // (plain methods, no lambdas: a lambda capturing a local allocates its closure at the entry of the calling method - TrySeat runs every
        // frame while a seat is pending)
        private static void Occupy(AcCarPhysics ph, bool on)
        {
            try { ph.SetOccupied(on); }
            catch (Exception e) { AcLog.Error("Seat: SetOccupied(" + on + ") failed: " + e); }
        }

        private static void Drive(AcCarPhysics ph, bool on)
        {
            try { ph.SetDriven(on); }
            catch (Exception e) { AcLog.Error("Seat: SetDriven(" + on + ") failed: " + e); }
        }

        private static void ParkInput(AcCarPhysics ph)
        {
            try { AcCarInput n = AcCarInput.Neutral; n.Handbrake = 1f; ph.Input = n; }
            catch (Exception e) { AcLog.Error("Seat: the parked input failed: " + e); }
        }

        /// <summary>The car's viewer state back to "outside" (C: COCKPIT_HR no longer forced, the AC driver model out of first person; A: exterior
        /// sound). AcCarCamera sets it while seated; it is reset here on every way out (A also resets itself on SeatChanged, C does not).</summary>
        private static void ViewerOutside(AcCarRig car)
        {
            if (car == null || car.Root == null) return;
            try { if (car.Model != null) car.Model.SetViewer(false); } catch (Exception e) { AcLog.WarnOnce("Seat: AcCarModel.SetViewer(false) threw: " + e.Message); }
            try { if (car.Audio != null) car.Audio.SetInterior(false); } catch (Exception e) { AcLog.WarnOnce("Seat: AcCarAudio.SetInterior(false) threw: " + e.Message); }
        }

        /// <summary>Hands the player back (ETS Release): out of the pose first, every lock back to 1, the driver parks the car.</summary>
        private static void Release(string why)
        {
            if (!Seated) return;
            AcCarRig car = SeatedCar;
            AcSeatKind seat = Kind;
            Seated = false;
            AcWheelGrab.LetGo();
            AcCarCamera.HandBack();
            AcSeatSettings.FlushCamera();
            AcHeadHide.Restore();
            AcSeatPoses.OwnReleased();
            if (_ticksFirstPerson) { _ticksFirstPerson = false; AcGame.Guard("seat: first person release", AcFirstPerson.Release); }

            LevelRig();
            if (_poses != null)
            {
                try { _poses.SetPose(PoseType.None); }
                catch (Exception e) { AcLog.Info("Seat: the client threw leaving the pose (" + e.GetType().Name + ": " + e.Message + ")."); }
            }

            Lock(false);

            if (car != null && car.Physics != null)
            {
                AcCarPhysics ph = car.Physics;
                if (seat == AcSeatKind.Driver)
                {
                    ParkInput(ph);
                    Drive(ph, false);
                }

                // solid again for walkers one second later (the avatar must be out of the car first: ETS _solidAgainAt)
                _solidAgainCar = car;
                _solidAgainAt = Time.unscaledTime + 1f;
            }

            if (seat == AcSeatKind.Passenger && car != null && car.Kind == AcCarKind.Remote) AcRiders.LeftRide();
            ViewerOutside(car);

            SeatedCar = null;
            IsDriver = false;
            AcLocal.Seated = false;
            AcLocal.Driving = false;
            AcLocal.SeatCar = null;
            AcLocal.WheelHands = 0;
            AcLocal.PedalsArmed = false;
            AcLocal.SeatRootLocal = Vector3.zero;
            AcLocal.SeatYawLocal = 0f;
            AcLocal.ViewInside = false;
            if (car != null) RaiseExited(car);
            _geo = null;
        }

        /// <summary>Upright before the client's pose exit reads the root (ETS LevelRig; the seat never tilts a VR rig, a monitor body may).</summary>
        private static void LevelRig()
        {
            if (_root == null) return;
            Transform eyes = EyesTransform();
            LevelAbout(_root, eyes);
            if (_rig != null && _rig != _root) LevelAbout(_rig, eyes);
        }

        private static void LevelAbout(Transform body, Transform eyes)
        {
            if (body.up.y > 0.99995f) return;
            bool pivot = eyes != null && eyes.IsChildOf(body);
            Vector3 before = pivot ? eyes.position : body.position;
            body.rotation = Quaternion.Euler(0f, body.eulerAngles.y, 0f);
            if (pivot) body.position += before - eyes.position;
        }

        private static Vector3 _standAt;

        /// <summary>Stands the player on the ground 1.6 m outside the seat's door, facing the car (ETS StandBeside).</summary>
        private static bool StandBeside(AcSeatGeometry g, AcSeatKind seat)
        {
            if (g == null || !g.Alive) return false;
            Vector3 sm;
            if (!g.SeatModel(seat, out sm)) sm = Vector3.zero;
            float side = g.SideOf(seat) == AcSide.Left ? -1f : 1f;
            float outX = side * Mathf.Max(StandOut, g.HalfWidth + 0.7f);       // clear of wide cars // ours
            Vector3 door = g.ModelToWorld(new Vector3(outX, sm.y, sm.z));
            float yaw = g.Yaw - side * 90f;
            return StandAt(door, yaw);
        }

        private static bool StandAt(Vector3 door, float yaw)
        {
            Vector3 standAt = door;
            int solid = AcLayers.GroundMask | (1 << AcLayers.Environment);
            RaycastHit hit;
            if (Physics.Raycast(door + Vector3.up * 3f, Vector3.down, out hit, 30f, solid, QueryTriggerInteraction.Ignore)) standAt = hit.point + Vector3.up * 0.05f;
            _standAt = standAt;
            if (LabRoot != null)
            {
                LabRoot.SetPositionAndRotation(standAt, Quaternion.Euler(0f, yaw, 0f));
                return true;
            }

            string how;
            return AcPlayer.Teleport(standAt, yaw, out how);
        }

        // ================================================================ per frame (called by the drivers)

        /// <summary>Order -90 (before the client's order-0 scripts): pending seat, input, keys, the early seat write.</summary>
        internal static void Early()
        {
            float now = Time.unscaledTime;
            if (_solidAgainCar != null && now >= _solidAgainAt) SolidAgain();

            if (Pending && _wantCar != null) TrySeat();

            if (!Seated)
            {
                DoorsTick();
                ParkOwnCar();
                return;
            }

            AcCarRig car = SeatedCar;
            if (car == null || car.Root == null || _root == null)
            {
                Vanished(_root == null ? "the Bridge body went away" : "the car is gone");
                return;
            }

            ProduceInput(car);

            if (AcDriveInput.ExitPressed)
            {
                AcDriveInput.ExitPressed = false;
                Exit();
                if (!Seated) return;
            }

            if (!Vr) AcCarCamera.Keys();
            if (_ticksFirstPerson) AcFirstPerson.Tick();      // (catches its own failures)
            Place(false);
        }

        // Guarded per-frame steps: an exception in another module's member (P's Input, F's HoldCar, N's players) costs that step for the
        // frame and one log line, never the seat write that follows (try/catch allocates nothing).
        private static int _inputErrors, _doorErrors;

        private static void ProduceInput(AcCarRig car)
        {
            try
            {
                if (IsDriver) AcDriveInput.Produce(car);
                else AcDriveInput.PassengerKeys();
            }
            catch (Exception e) { if (_inputErrors++ < 3) AcLog.Error("Seat: producing the car's input failed: " + e); }
        }

        private static void DoorsTick()
        {
            try { AcDoors.Tick(); }
            catch (Exception e) { if (_doorErrors++ < 3) AcLog.Error("Seat: the get-in targets failed: " + e); }
        }

        // (separate methods: a lambda capturing a local would allocate its closure at the entry of the per-frame method)
        private static void SolidAgain()
        {
            AcCarRig c = _solidAgainCar;
            _solidAgainCar = null;
            if (c.Root == null || c.Physics == null || (Seated && ReferenceEquals(c, SeatedCar))) return;
            try { c.Physics.SetOccupied(false); }
            catch (Exception e) { AcLog.Error("Seat: SetOccupied(false) failed: " + e); }
        }

        /// <summary>Order 9000 LateUpdate (after the client's camera brain at 100, before VRIK at 9998): pose, seat, head, wheel, camera.</summary>
        internal static void Late()
        {
            StepDoor();
            if (!Seated || SeatedCar == null || SeatedCar.Root == null || _root == null) return;
            bool entered = _forceSitting && KeepSitting();
            if (entered) _restKnown = false;
            Place(true);
            if (Vr)
            {
                AcCarCamera.HandBack();      // (cheap when the monitor camera is not held: a headset just appeared)
                if (IsDriver) AcWheelGrab.AfterRigPlaced(_geo);
                AcVrDrive.AfterRigPlaced(_geo);
            }
            else
            {
                AcCarCamera.Place(_geo);
                if (_ticksFirstPerson) AcFirstPerson.HoldKeys();
            }

            AcLocal.ViewInside = Vr || AcCameraMode.Inside(AcLocal.CameraMode);
            AcSeatData.ReportOnce(_geo != null ? _geo.Data : null);
            Publish();
            if (!_entryReported && Time.unscaledTime - _seatedAt > 1.5f) ReportEntry();
        }

        /// <summary>Order 10000 LateUpdate: if anything moved the body after our 9000 write, write it again (Halo HaloVehicleLate).</summary>
        /// <returns>true when it wrote the seat again (the monitor avatar carry then runs again too).</returns>
        internal static bool Final()
        {
            if (!Seated || _root == null || SeatedCar == null || SeatedCar.Root == null) return false;
            if (_placedFrame == Time.frameCount && (_root.position - _placedRoot).sqrMagnitude < 1e-6f) return false;
            bool movedAfter = _placedFrame == Time.frameCount;
            Place(true);
            if (movedAfter && !_saidFinal)
            {
                _saidFinal = true;
                AcLog.Info("Seat: something moved the body after the 9000 seat write - written again at 10000 (Halo HaloVehicleLate case; logged once).");
            }

            return true;
        }

        private static bool _saidFinal;

        /// <summary>Writes the body root (and in VR the rig) onto the seat. Allocation-free.</summary>
        private static void Place(bool late)
        {
            AcSeatGeometry g = _geo;
            if (g == null || !g.Alive) return;
            Transform car = g.Body;
            Vector3 seatModel;
            if (!g.SeatModel(Kind, out seatModel)) seatModel = Vector3.zero;
            Vector3 seat = g.ModelToWorld(seatModel);
            bool vr = Vr;
            Quaternion rot = vr || AcSeatSettings.HorizonLock ? Quaternion.Euler(0f, g.Yaw, 0f) : car.rotation;
            _root.SetPositionAndRotation(seat, rot);
            Vector3 eyeModel;
            bool fromVr;
            if (!g.EyeModel(Kind, vr, out eyeModel, out fromVr)) eyeModel = seatModel + new Vector3(0f, 0.5f, 0.05f); // ours
            Vector3 eye = g.ModelToWorld(eyeModel);
            _lastDrawn = new Pose(car.position, car.rotation);
            _lastSeatModel = seatModel;
            _haveLastDrawn = true;

            if (vr)
            {
                PlaceHead(eye);
            }
            else if (_head != null && PoseLowered)
            {
                // the pose lowered the head anchor to sitting height above the feet (= body root): put that head on the eye so the hips
                // come down onto the seat (ETS LateUpdate, NonXRPosesFeature)
                _root.position += eye - _head.position;
            }

            _placedRoot = _root.position;
            _placedRootYaw = rot.eulerAngles.y;
            _placedFrame = Time.frameCount;
            AcLocal.WorldPosition = seat;
        }

        private static bool PoseLowered { get { return _poseOffset != null && _poseOffset.localPosition.y < -0.05f; } }

        private static Transform EyesTransform()
        {
            if (LabRoot != null) return LabHead;
            if (Vr)
            {
                Camera c = AcPlayer.Head();
                if (c != null) return c.transform;
            }

            return _head;
        }

        /// <summary>ETS PlaceHead: shift the rig so the eyes land at the eye point, keeping the head's own lean (clamped to 0.45 m).</summary>
        private static void PlaceHead(Vector3 eye)
        {
            Transform head = EyesTransform();
            if (head == null) return;
            if (_rig == null)
            {
                bool carried = head.IsChildOf(_root);
                _rig = carried ? _root : head.root;
                AcLog.Info("Seat: VR - the camera " + (carried ? "rides on the body" : "is outside the body") + "; moving rig '" + _rig.name + "'.");
                if (!carried) _rig.rotation = _root.rotation;
            }
            else if (_rig != _root)
            {
                _rig.rotation = _root.rotation;
            }

            Vector3 inRig = _rig.InverseTransformPoint(head.position);
            if (!_restKnown || (inRig - _headRest).sqrMagnitude > 0.7f * 0.7f)
            {
                _headRest = inRig;
                _restKnown = true;
            }

            Vector3 lean = Vector3.ClampMagnitude(inRig - _headRest, LeanLimit);
            Vector3 wanted = eye + _rig.TransformVector(lean);
            _rig.position += wanted - head.position;
        }

        /// <summary>ETS KeepSitting: SittingStraight every seated frame; true when the pose began this frame (the eyes moved in the rig).</summary>
        private static bool KeepSitting()
        {
            if (_poses == null) return false;
            Transform eyes = EyesTransform();
            FindPoseOffset(eyes);
            Vector3 before = eyes != null ? _root.InverseTransformPoint(eyes.position) : Vector3.zero;
            try
            {
                _poses.SetPose(PoseType.SittingStraight);
            }
            catch (Exception e)
            {
                _forceSitting = false;
                AcLog.Info("Seat: the client threw on SetPose (" + e.GetType().Name + ": " + e.Message + "); sitting is off for this visit.");
                return false;
            }

            if (Vr && eyes != null && _poseOffset != null) UndoPoseRecentre(eyes);
            bool moved = eyes != null && (_root.InverseTransformPoint(eyes.position) - before).sqrMagnitude > 0.03f * 0.03f;
            if (moved && _poseEntered) PoseReentered();
            if (moved)
            {
                _poseEntered = true;
                if (!_poseReported)
                {
                    _poseReported = true;
                    AcLog.Info("Seat: pose SittingStraight entered; eyes moved " + (_root.InverseTransformPoint(eyes.position) - before).ToString("F2") + " in the rig; pose offset " +
                               (_poseOffset != null ? _poseOffset.localPosition.ToString("F2") : "not found") + ".");
                }
            }

            return moved;
        }

        private static bool _saidXDrop, _saidReentry;

        /// <summary>The pose had been lost and came back this frame: say once why (left X is the client's pose-cancel action in VR).</summary>
        private static void PoseReentered()
        {
            bool afterX = Vr && Time.unscaledTime - AcVrDrive.LastLeftPrimaryAt < 0.5f;
            if (afterX && !_saidXDrop)
            {
                _saidXDrop = true;
                AcLog.Info("Seat: the client dropped the sitting pose right after left X (its pose-cancel action) - re-entered at once" +
                           (AcSeatSettings.XShiftsDown ? "; the setting 'X also shifts down' is on" : "") + ".");
            }
            else if (!afterX && !_saidReentry)
            {
                _saidReentry = true;
                AcLog.Info("Seat: the client dropped the sitting pose (cancel / Escape / avatar reload) - re-entered at once.");
            }
        }

        private static void UndoPoseRecentre(Transform eyes)
        {
            if (_poseOffset.localRotation != Quaternion.identity)
            {
                _poseOffset.localRotation = Quaternion.identity;
                Vector3 inRig = _root.InverseTransformPoint(eyes.position);
                _poseOffset.position -= _root.TransformVector(new Vector3(inRig.x, 0f, inRig.z));
                _recentred = true;
            }

            UndoHandRecentre(_leftHandOffset);
            UndoHandRecentre(_rightHandOffset);
        }

        private static void UndoHandRecentre(Transform hand)
        {
            if (hand == null) return;
            if (hand.localRotation != Quaternion.identity) hand.localRotation = Quaternion.identity;
            hand.localPosition = _poseOffset.localPosition;
        }

        private static void FindPoseOffset(Transform from)
        {
            if (from == null || from == _poseOffsetFrom) return;
            _poseOffsetFrom = from;
            _poseOffset = null;
            for (Transform t = from; t != null && t != _root; t = t.parent)
                if (t.name == PoseOffsetName) { _poseOffset = t; break; }
            _leftHandOffset = AncestorNamed(_left, "LeftHandCustomPoseOffset", _root);
            _rightHandOffset = AncestorNamed(_right, "RightHandCustomPoseOffset", _root);
            AcLog.Info("Seat: " + (_poseOffset != null
                ? "the client's " + PoseOffsetName + " is above '" + from.name + "'; hand offsets " + (_leftHandOffset != null ? "left found" : "left MISSING") + ", " + (_rightHandOffset != null ? "right found" : "right MISSING") + "."
                : "no " + PoseOffsetName + " above '" + from.name + "' (a headset keeps the client's re-centring; a monitor seat stays on the seat point)."));
        }

        private static Transform AncestorNamed(Transform from, string name, Transform stop)
        {
            for (Transform t = from; t != null && t != stop; t = t.parent) if (t.name == name) return t;
            return null;
        }

        private static void Publish()
        {
            AcSeatGeometry g = _geo;
            if (g == null || !g.Alive || _root == null) return;
            Transform car = g.Body;
            AcLocal.SeatRootLocal = car.InverseTransformPoint(_root.position);
            AcLocal.SeatYawLocal = Mathf.DeltaAngle(g.Yaw, _root.eulerAngles.y);
        }

        private static void ReportEntry()
        {
            _entryReported = true;
            AcSeatGeometry g = _geo;
            Vector3 eye;
            bool fromVr = false;
            bool haveEye = g != null && g.EyeModel(Kind, Vr, out eye, out fromVr);
            string car = SeatedCar != null && SeatedCar.Car != null ? SeatedCar.Car.Id : "?";
            AcLog.Info("Seat: entered " + car + " as " + (Kind == AcSeatKind.Driver ? "driver" : "passenger") + " (" +
                       (_poses == null ? "no Bridge poses (editor)" : _poseEntered ? "pose entered" : "pose not seen to move the eyes") + ", " +
                       (Vr ? (_recentred ? "rig recentred" : "rig not recentred") : "monitor") + ", " +
                       (AcHeadHide.Hidden || AcSeatPoses.OwnHeadHidden ? "head hidden" : "head shown") + "; eye " +
                       (haveEye ? (Vr ? (fromVr ? "vr.ini DRIVEREYES" : "car.ini DRIVEREYES") : "car.ini DRIVEREYES") : "unknown") + ", heading-only " + (Vr || AcSeatSettings.HorizonLock ? "yes" : "no (monitor, horizon lock off)") + ").");
        }

        /// <summary>The car under a seated player is gone: release and land beside where it was last drawn (ETS Vanished / LandBeside).</summary>
        private static void Vanished(string why)
        {
            AcLog.Info("Seat: released - " + why + ".");
            bool drawn = _haveLastDrawn;
            Pose pose = _lastDrawn;
            Vector3 seatModel = _lastSeatModel;
            Release(why);
            AcSeatEarly driver = _game != null ? _game.GetComponent<AcSeatEarly>() : null;
            if (drawn && driver != null)
            {
                if (_landing != null) driver.StopCoroutine(_landing);
                _landing = driver.StartCoroutine(LandBeside(pose, seatModel));
            }
        }

        private static IEnumerator LandBeside(Pose car, Vector3 seatModel)
        {
            float side = seatModel.x < 0f ? -1f : 1f;
            Vector3 door = car.position + car.rotation * new Vector3(seatModel.x + side * StandOut, seatModel.y, seatModel.z);
            Vector3 f = car.rotation * Vector3.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg - side * 90f;
            int solid = AcLayers.GroundMask | (1 << AcLayers.Environment);
            float deadline = Time.time + 10f;
            while (Time.time < deadline && !Seated)
            {
                RaycastHit hit;
                if (Physics.Raycast(door + Vector3.up * 3f, Vector3.down, out hit, 30f, solid, QueryTriggerInteraction.Ignore))
                {
                    bool moved = StandAt(door, yaw);
                    AcLog.Info("Seat: stood beside where the car was, at " + Fmt(_standAt) + (moved ? "." : ", but the Bridge would not move the player."));
                    _landing = null;
                    yield break;
                }

                yield return new WaitForSeconds(0.5f);
            }

            if (!Seated) AcLog.Info("Seat: no ground beside where the car was within 10 s; the player stays where the client has them.");
            _landing = null;
        }

        private static float _nextParkCheck;

        /// <summary>A player on foot and a driven own car cannot both be true: park it (ETS Update; only with a Bridge body).</summary>
        private static void ParkOwnCar()
        {
            if (Time.unscaledTime < _nextParkCheck) return;
            _nextParkCheck = Time.unscaledTime + 0.5f;
            AcCarRig own = AcLocal.Car;
            if (own == null || own.Root == null || own.Physics == null || !own.Physics.Driven || Pending) return;
            if (!AcPlayer.HasBridgePlayer) return;
            try { own.Physics.SetDriven(false); }
            catch (Exception e) { AcLog.Error("Seat: parking the own car failed: " + e); return; }
            AcLog.InfoOnce("Seat: the own car was driven with nobody in it - parked (kinematic, solid) until somebody gets in.");
        }

        // ================================================================ doors (C's SetDoor + A's door sound)

        // ours: AC opens doors only in its showroom / pits and no file gives a time - car_DOOR_L/R.ksanim hold 100 frames (71 cars) or 101 (6)
        // with no frame rate (harness ksanim census 2026-11-01); C samples them by fraction, so the timing is ours
        private const float DoorOpenS = 0.6f, DoorHoldS = 0.5f, DoorCloseS = 0.7f; // ours

        private static void StartDoor(AcCarRig car, AcSide side, bool exiting)
        {
            if (car == null || car.Root == null) return;
            if (_doorCar != null && !ReferenceEquals(_doorCar, car)) SetDoor(_doorCar, _doorSide, 0f);
            _doorCar = car;
            _doorSide = side;
            _doorT0 = Time.unscaledTime;
            _doorClosingSaid = false;
            DoorSound(car, side, true);
        }

        private static void DoorSound(AcCarRig car, AcSide side, bool opening)
        {
            AcCarAudio audio = car != null ? car.Audio : null;
            if (audio == null) return;
            try { audio.Door(side, opening); }
            catch (Exception e) { AcLog.WarnOnce("Seat: AcCarAudio.Door threw: " + e.Message); }
        }

        private static void StepDoor()
        {
            if (_doorCar == null) return;
            if (_doorCar.Root == null) { _doorCar = null; return; }
            float t = Time.unscaledTime - _doorT0;
            float open;
            if (t < DoorOpenS) open = Smooth(t / DoorOpenS);
            else if (t < DoorOpenS + DoorHoldS) open = 1f;
            else if (t < DoorOpenS + DoorHoldS + DoorCloseS)
            {
                if (!_doorClosingSaid)
                {
                    _doorClosingSaid = true;
                    DoorSound(_doorCar, _doorSide, false);
                }

                open = 1f - Smooth((t - DoorOpenS - DoorHoldS) / DoorCloseS);
            }
            else
            {
                SetDoor(_doorCar, _doorSide, 0f);
                _doorCar = null;
                return;
            }

            SetDoor(_doorCar, _doorSide, open);
        }

        /// <summary>The door open fraction now (the lab's picture).</summary>
        public static float DoorOpen { get; private set; }

        private static void SetDoor(AcCarRig car, AcSide side, float t)
        {
            DoorOpen = t;
            AcCarModel m = car.Model;
            if (m == null) return;
            try { m.SetDoor(side, t); }
            catch (Exception e) { AcLog.WarnOnce("Seat: AcCarModel.SetDoor threw: " + e.Message); }
        }

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // ================================================================ helpers

        internal static bool Refuse(string why, float seconds)
        {
            float now = Time.unscaledTime;
            if (why != _lastRefusal || now - _lastRefusalAt > 2f)
            {
                AcLog.Info("Seat: refused - " + why + ".");
                _lastRefusal = why;
            }

            _lastRefusalAt = now;
            AcEvents.RaiseMessage(Capital(why), seconds);
            return false;
        }

        private static string Capital(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        internal static string Fmt(Vector3 v) { return "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")"; }

        /// <summary>The body root the seat writes (lab / poses).</summary>
        internal static Transform BodyRoot { get { return _root; } }
        internal static Transform LeftHand { get { return _left; } }
        internal static Transform RightHand { get { return _right; } }

        private static void RaiseEntered(AcCarRig car, AcSeatKind seat)
        {
            var h = Entered;
            if (h != null) { try { h(car, seat); } catch (Exception e) { AcLog.Error("Seat: an Entered handler failed: " + e); } }
            AcEvents.RaiseSeatChanged(car, seat);
        }

        private static void RaiseExited(AcCarRig car)
        {
            var h = Exited;
            if (h != null) { try { h(car); } catch (Exception e) { AcLog.Error("Seat: an Exited handler failed: " + e); } }
            AcEvents.RaiseSeatChanged(null, AcSeatKind.Driver);
        }
    }
}
