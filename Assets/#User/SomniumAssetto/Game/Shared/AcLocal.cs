using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The local player's state that several modules read (HUD, net record, tablet, audio). Plain static fields (no events, no cost per
    /// frame). Owners extend it ONLY in their own partial file - AcLocal.Front.cs (F: chosen car/skin), AcLocal.Physics.cs (P: the car and
    /// its telemetry), AcLocal.Seat.cs (S: seat state, camera), AcLocal.Net.cs (N: slot), AcLocal.Race.cs (R: laps, timing, position) -
    /// and never write another owner's fields. This file (lead) holds only what nobody owns.
    /// </summary>
    public static partial class AcLocal
    {
        /// <summary>The local player's Somnium name (set by N from the Bridge once known; "" before).</summary>
        public static string Nick = "";

        /// <summary>Where the local player is (avatar feet or the seated car), written each frame by whoever moves the player (S while
        /// seated, else nobody: read the Bridge through AcPlayer).</summary>
        public static Vector3 WorldPosition;
    }
}
