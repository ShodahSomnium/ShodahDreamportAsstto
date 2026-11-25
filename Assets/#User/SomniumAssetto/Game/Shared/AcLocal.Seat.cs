using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[S, Seat] Seat state of the local player. Owner: S (written by AcSeat / AcDriveInput / AcCarCamera every frame).</summary>
    public static partial class AcLocal
    {
        /// <summary>Sitting in a car (driver or passenger).</summary>
        public static bool Seated;
        /// <summary>Sitting in the driver seat (the controls are ours).</summary>
        public static bool Driving;
        /// <summary>The car sat in (null on foot). Not necessarily <see cref="Car"/> (passenger in another player's car).</summary>
        public static AcCarRig SeatCar;
        /// <summary>Which seat.</summary>
        public static AcSeatKind Seat;
        /// <summary>Monitor camera (see <see cref="AcCameraMode"/>): 0 cockpit (DRIVEREYES), 1 bonnet (car.ini BONNET_CAMERA_POS), 2 chase near
        /// (chase_cam.ini CHASE_0), 3 chase far (CHASE_1), 4 orbit, 5 dash (dash_cam.ini), 6 bumper (car.ini BUMPER_CAMERA_POS).
        /// VR: always cockpit (0). // ours numbering (0-4 fixed by the contract, 5-6 added by S)</summary>
        public static int CameraMode;

        // ---- added by S (only ADD members)

        /// <summary>The seated body root in the seated car's Rigidbody frame (metres, Unity axes), as THIS client places it - N publishes it
        /// so every other client puts the body on its own drawn copy of the car (ETS rider record). Zero on foot.</summary>
        public static Vector3 SeatRootLocal;
        /// <summary>The seated body root's yaw relative to the car's heading (degrees; 0 = facing the car's forward).</summary>
        public static float SeatYawLocal;
        /// <summary>VR: hands holding the steering wheel (bit 0 left, bit 1 right). 0 on a monitor.</summary>
        public static int WheelHands;
        /// <summary>The pedals are live (VR: armed after both triggers were released; monitor: always while driving).</summary>
        public static bool PedalsArmed;
        /// <summary>The view is inside the cockpit (cockpit / dash camera, or VR): A's interior sound, C's SetViewer.</summary>
        public static bool ViewInside;
    }

    /// <summary>Monitor camera modes (<see cref="AcLocal.CameraMode"/>). // ours numbering</summary>
    public static class AcCameraMode
    {
        public const int Cockpit = 0, Bonnet = 1, ChaseNear = 2, ChaseFar = 3, Orbit = 4, Dash = 5, Bumper = 6, Count = 7;

        /// <summary>The C key's order: like AC's F1 cycle (cockpit, dash, bonnet, bumper, chase near, chase far) plus our orbit. // ours</summary>
        public static readonly int[] Cycle = { Cockpit, Dash, Bonnet, Bumper, ChaseNear, ChaseFar, Orbit };

        public static string Name(int mode)
        {
            switch (mode)
            {
                case Cockpit: return "cockpit";
                case Bonnet: return "bonnet";
                case ChaseNear: return "chase near";
                case ChaseFar: return "chase far";
                case Orbit: return "orbit";
                case Dash: return "dash";
                case Bumper: return "bumper";
                default: return "mode " + mode;
            }
        }

        /// <summary>The camera is inside the car (interior sound, own head hidden).</summary>
        public static bool Inside(int mode) { return mode == Cockpit || mode == Dash; }
    }
}
