using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Where things are placed in the Unity scene. Assetto Corsa's kn5 coordinate conversion belongs to the format readers
    /// (Data/Kn5, see research/ac_track_formats.md); this file only holds the two world anchors every module shares.
    /// </summary>
    public static class AcSpace
    {
        /// <summary>The hub (spawn room / showroom) hangs 3 km above the tracks so the two never overlap (as the other Dreamports;
        /// the scene's spawn point is at y 3000.9 and its fallback floor collider at y 2999.5).</summary>
        public static readonly Vector3 RoomOrigin = new Vector3(0f, 3000f, 0f); // ours

        /// <summary>Where a track is built (track coordinates are added to this). AC tracks span up to ~2 km from their own origin.</summary>
        public static readonly Vector3 MapOrigin = Vector3.zero; // ours
    }
}
