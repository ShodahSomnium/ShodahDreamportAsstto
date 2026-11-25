using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] The seated player's monitor camera, from the game's own camera data (ETS EtsTruckCamera / Halo HaloVehicleCamera pattern):
    /// it does not own a camera - each late frame it places the client's camera (AcPlayer.Head) and pauses the client's CinemachineBrain by
    /// name while it holds the view (the brain runs at LateUpdate 100 and would overwrite pose / FOV / clip planes); everything is given
    /// back on getting out. Never in VR (the headset's camera is tracked; the seat moves the rig instead).
    /// Modes (C key / gamepad RB cycle, like AC's F1; setting seat.camera is the start mode):
    /// - cockpit: car.ini DRIVEREYES, pitched by car.ini ON_BOARD_PITCH_ANGLE, field of view cfg/camera_onboard.ini FOV (56), car-aligned
    ///   (camera_onboard IS_WORLD_ALIGNED=0; 1 = level); right mouse looks round (// ours limits). Near plane 5 cm while drawing (the client's
    ///   first-person near plane is 35 cm: the wheel and dash would be cut - Halo's beginCameraRendering trick).
    /// - dash: dash_cam.ini [DASH_CAM] POS; bonnet: car.ini BONNET_CAMERA_POS; bumper: car.ini BUMPER_CAMERA_POS (all car-aligned).
    /// - chase near / far: system/cfg/chase_cam.ini CHASE_0 / CHASE_1 (DISTANCE, HEIGHT, PITCH rad) behind the car, following its heading
    ///   with a lag (// ours rate), kept out of walls (AcLayers.CameraObstacles).
    /// - orbit: right mouse orbits, wheel zooms (// ours).
    /// </summary>
    public static class AcCarCamera
    {
        public const float CockpitNear = 0.05f; // ours
        private const float ChaseFollowRate = 4f; // ours (per second)
        private static Camera _cam;
        private static Behaviour _brain;
        private static bool _brainWeDisabled;   // the brain was on and we switched it off: only then is it switched on again
        private static float _restoreFov = -1f, _restoreFar = -1f;
        private static float _lookYaw, _lookPitch, _orbitYaw, _orbitPitch = 15f, _orbitDist = 6f, _chaseYaw;
        private static bool _haveChaseYaw, _hooked, _holding;
        private static int _mode = -1;
        private static int _saidModes;
        private static bool _wantNear;
        private static Camera _nearCam;
        private static float _nearBefore;

        /// <summary>Lab: the camera to place (instead of the client's).</summary>
        public static Camera LabCamera;

        /// <summary>The camera mode now (AcLocal.CameraMode mirrors it).</summary>
        public static int Mode { get { return _mode < 0 ? AcSeatSettings.Camera : _mode; } }

        internal static void OnSeated()
        {
            _mode = AcSeat.Vr ? AcCameraMode.Cockpit : AcSeatSettings.Camera;
            AcLocal.CameraMode = _mode;
            _lookYaw = _lookPitch = 0f;
            _haveChaseYaw = false;
            Interior();
        }

        /// <summary>Next camera in AC's cycle (C key, gamepad RB, the tablet's Camera button).</summary>
        public static void Cycle()
        {
            if (AcSeat.Vr) return;
            int i = Array.IndexOf(AcCameraMode.Cycle, Mode);
            Set(AcCameraMode.Cycle[(i + 1) % AcCameraMode.Cycle.Length]);
        }

        /// <summary>Selects a camera mode (an <see cref="AcCameraMode"/> value).</summary>
        public static void Set(int mode)
        {
            if (mode < 0 || mode >= AcCameraMode.Count || AcSeat.Vr) return;
            _mode = mode;
            AcLocal.CameraMode = mode;
            _lookYaw = _lookPitch = 0f;
            _haveChaseYaw = false;
            AcSeatSettings.RememberCamera(mode);
            Interior();
            if ((_saidModes & (1 << mode)) == 0 && _holding) Say();
        }

        private static void Say()
        {
            _saidModes |= 1 << _mode;
            AcLog.Info("Seat: camera " + AcCameraMode.Name(_mode) + " (brain " + (_brain != null ? "held" : "not found") + ", fov " + Fov().ToString("0") + ").");
        }

        /// <summary>Re-applies the viewer state (interior sound, COCKPIT_HR) to the seated car - N swapped the rig under the seat.</summary>
        internal static void Refresh() { Interior(); }

        private static void Interior()
        {
            AcCarRig car = AcSeat.SeatedCar;
            if (car == null) return;
            bool inside = AcSeat.Vr || AcCameraMode.Inside(Mode);
            AcLocal.ViewInside = inside;
            try { if (car.Audio != null) car.Audio.SetInterior(inside); } catch (Exception e) { AcLog.WarnOnce("Seat: AcCarAudio.SetInterior threw: " + e.Message); }
            try { if (car.Model != null) car.Model.SetViewer(inside); } catch (Exception e) { AcLog.WarnOnce("Seat: AcCarModel.SetViewer threw: " + e.Message); }
        }

        /// <summary>Early frame (monitor, seated): C cycles; the mouse looks / orbits.</summary>
        internal static void Keys()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.C) && !AcKeys.Typing()) Cycle();   // not while a text field (tablet) has the keys
                if (Input.GetMouseButton(1))
                {
                    float mx = Input.GetAxisRaw("Mouse X") * 3f, my = Input.GetAxisRaw("Mouse Y") * 3f; // ours sensitivity (ETS)
                    if (Mode == AcCameraMode.Orbit) { _orbitYaw += mx; _orbitPitch = Mathf.Clamp(_orbitPitch - my, -10f, 80f); }
                    else { _lookYaw += mx; _lookPitch -= my; }
                }
                else if (Mode != AcCameraMode.Orbit)
                {
                    float k = 1f - Mathf.Exp(-Time.deltaTime * 3f); // ours: the view drifts back ahead when the mouse is let go
                    _lookYaw = Mathf.Lerp(_lookYaw, 0f, k);
                    _lookPitch = Mathf.Lerp(_lookPitch, 0f, k);
                }

                if (Mode == AcCameraMode.Orbit)
                {
                    float wheel = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(wheel) > 0.01f) _orbitDist = Mathf.Clamp(_orbitDist * (1f - wheel * 0.1f), 2.5f, 20f);
                }
            }
            catch (Exception) { /* legacy Input missing: keys off, the camera still follows */ }
        }

        /// <summary>cfg/camera_onboard.ini FOV for every mode (AC's chase cameras use the same vertical FOV setting). </summary>
        private static float Fov() { return AcSeatData.G.OnboardFov; }

        /// <summary>The mouse look's yaw limit: cfg/camera_onboard.ini [ROTATION] HEAD_MAX_DEGREES (60), AC's own head-turn limit, for every
        /// car-mounted view; the pitch limit is ours.</summary>
        private static void ClampLook()
        {
            float head = Mathf.Clamp(AcSeatData.G.HeadMaxDegrees, 1f, 179f);
            _lookYaw = Mathf.Clamp(_lookYaw, -head, head);
            _lookPitch = Mathf.Clamp(_lookPitch, -LookPitchMax, LookPitchMax);
        }

        private const float LookPitchMax = 45f; // ours

        /// <summary>Late frame (order 9000, monitor, seated): the camera onto the car.</summary>
        internal static void Place(AcSeatGeometry geo)
        {
            if (AcSeat.Vr || geo == null || !geo.Alive) { HandBack(); return; }
            Camera cam = LabCamera != null ? LabCamera : AcPlayer.Head();
            if (cam == null) return;
            if (cam != _cam) { HandBack(); _cam = cam; }
            Hold();
            Hook();
            Transform car = geo.Body;
            AcSeatCar d = geo.Data;
            bool data = d != null && d.Ready;
            AcSeatGlobals g = AcSeatData.G;
            int mode = Mode;
            Vector3 pos;
            Quaternion rot;
            _wantNear = false;
            switch (mode)
            {
                case AcCameraMode.Cockpit:
                {
                    Vector3 eye;
                    bool fromVr;
                    if (!geo.EyeModel(AcSeat.Kind, false, out eye, out fromVr)) eye = new Vector3(0f, 1f, 0f);
                    pos = geo.ModelToWorld(eye);
                    float pitch = data ? -d.OnboardPitchDeg : 0f;      // ON_BOARD_PITCH_ANGLE is -7.8..0 on all 86 cars (probe seat): negative = looking down
                    Quaternion basis = g.OnboardWorldAligned ? Quaternion.Euler(0f, geo.Yaw, 0f) : car.rotation;
                    ClampLook();
                    rot = basis * Quaternion.Euler(pitch + _lookPitch, _lookYaw, 0f);
                    _wantNear = true;
                    break;
                }
                case AcCameraMode.Dash:
                case AcCameraMode.Bonnet:
                case AcCameraMode.Bumper:
                {
                    AcVec3 p;
                    bool have = data && (mode == AcCameraMode.Dash ? d.HasDash : mode == AcCameraMode.Bonnet ? d.HasBonnet : d.HasBumper);
                    p = !have ? default(AcVec3) : mode == AcCameraMode.Dash ? d.Dash : mode == AcCameraMode.Bonnet ? d.Bonnet : d.Bumper;
                    // BONNET/BUMPER_CAMERA_POS are physics space (relative to the CG): model = pos - GRAPHICS_OFFSET (AcSeatCar.Bonnet; lab
                    // seat-bonnet showed the inside of the 458's bonnet when they were read as model space)
                    if (have && mode != AcCameraMode.Dash && d.HasOffset)
                        p = new AcVec3(p.X - d.GraphicsOffset.X, p.Y - d.GraphicsOffset.Y, p.Z - d.GraphicsOffset.Z);
                    Vector3 m = have ? AcSeatData.ToUnity(p) : new Vector3(0f, 0.9f, 1.2f); // ours fallback
                    pos = geo.ModelToWorld(m);
                    ClampLook();
                    float camPitch = !data ? 0f : mode == AcCameraMode.Bonnet ? -d.BonnetPitchDeg : mode == AcCameraMode.Bumper ? -d.BumperPitchDeg : 0f;
                    rot = car.rotation * Quaternion.Euler(camPitch + _lookPitch, _lookYaw, 0f);
                    _wantNear = mode == AcCameraMode.Dash;
                    break;
                }
                case AcCameraMode.Orbit:
                {
                    Vector3 target = car.position + Vector3.up * 0.5f;
                    rot = Quaternion.Euler(_orbitPitch, geo.Yaw + _orbitYaw, 0f);
                    pos = Clear(target, target - rot * Vector3.forward * _orbitDist);
                    break;
                }
                default:
                {
                    int k = mode == AcCameraMode.ChaseFar ? 1 : 0;
                    float dist = g.ChaseDistance[k];
                    float height = g.ChaseHeight[k];
                    float pitchRad = g.ChasePitchRad[k];
                    float yaw = geo.Yaw;
                    if (!_haveChaseYaw) { _chaseYaw = yaw; _haveChaseYaw = true; }
                    _chaseYaw = Mathf.LerpAngle(_chaseYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime * ChaseFollowRate));
                    // [BELIEF] DISTANCE is measured from the car's rear (CG + half the model's length), HEIGHT above the CG.
                    // [BELIEF] the view is LEVEL plus PITCH (no look-at the car), and PITCH > 0 looks DOWN: a rotation about AC's +X (= car left,
                    // right-handed) by +a takes +Z forward to (0, -sin a, cos a), i.e. down - the same as Unity's +X Euler. With the install's values
                    // CHASE_0 looks 2 deg down and CHASE_1 (higher) 1 deg UP, which is suspicious: the lab shots seat-chase / seat-chase-far compare
                    // both with AC's own F1 views before this is trusted (S/NOTES.md).
                    float half = geo.Rig.Model != null && geo.Rig.Model.Bounds.size.z > 1f ? geo.Rig.Model.Bounds.extents.z : 2.2f; // ours fallback
                    Quaternion heading = Quaternion.Euler(0f, _chaseYaw, 0f);
                    Vector3 target = car.position + Vector3.up * height;
                    Vector3 want = car.position - heading * Vector3.forward * (dist + half) + Vector3.up * height;
                    pos = Clear(target, want);
                    rot = Quaternion.Euler(pitchRad * Mathf.Rad2Deg + _lookPitch, _chaseYaw + _lookYaw, 0f);
                    break;
                }
            }

            _cam.transform.SetPositionAndRotation(pos, rot);
            if (_restoreFov < 0f) _restoreFov = _cam.fieldOfView;
            _cam.fieldOfView = Fov();
            if (_cam.farClipPlane < 5000f) { if (_restoreFar < 0f) _restoreFar = _cam.farClipPlane; _cam.farClipPlane = 5000f; } // ours: a track is up to ~2 km
            if ((_saidModes & (1 << mode)) == 0) Say();
        }

        /// <summary>Pulls a camera in front of anything between the target and it.</summary>
        private static Vector3 Clear(Vector3 target, Vector3 want)
        {
            Vector3 d = want - target;
            float len = d.magnitude;
            if (len < 0.01f) return want;
            RaycastHit hit;
            if (Physics.SphereCast(target, 0.2f, d / len, out hit, len, AcLayers.CameraObstacles, QueryTriggerInteraction.Ignore))
                return target + d / len * Mathf.Max(0.5f, hit.distance - 0.1f);
            return want;
        }

        private static void Hold()
        {
            _holding = true;
            if (_brain == null || _brain.gameObject != _cam.gameObject)
            {
                GiveBrainBack();
                _brain = _cam.GetComponent("CinemachineBrain") as Behaviour;
            }

            if (_brain != null && _brain.enabled) { _brain.enabled = false; _brainWeDisabled = true; }
        }

        private static void GiveBrainBack()
        {
            if (_brain != null && _brainWeDisabled && !_brain.enabled) _brain.enabled = true;
            _brainWeDisabled = false;
            _brain = null;
        }

        /// <summary>Gives the client its camera back as it was (brain, field of view, clip planes).</summary>
        public static void HandBack()
        {
            if (_cam != null)
            {
                if (_restoreFov > 0f) _cam.fieldOfView = _restoreFov;
                if (_restoreFar > 0f) _cam.farClipPlane = _restoreFar;
                if (_nearCam == _cam && _nearBefore > 0f) _cam.nearClipPlane = _nearBefore;
            }

            _restoreFov = _restoreFar = -1f;
            _nearCam = null;
            _wantNear = false;
            GiveBrainBack();
            if (_holding) AcLog.InfoOnce("Seat: camera handed back to the client (brain on, field of view restored).");
            _holding = false;
            _cam = null;
            Unhook();
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
        }

        private static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_wantNear || cam == null || cam != _cam) return;
            _nearCam = cam;
            _nearBefore = cam.nearClipPlane;
            if (cam.nearClipPlane > CockpitNear) cam.nearClipPlane = CockpitNear;
        }

        private static void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || cam != _nearCam) return;
            if (_nearBefore > 0f) cam.nearClipPlane = _nearBefore;
            _nearCam = null;
        }

        /// <summary>Lab: the near plane the cockpit view uses (the lab renders outside the render loop's callbacks).</summary>
        public static bool WantsNear { get { return _wantNear; } }
    }
}
