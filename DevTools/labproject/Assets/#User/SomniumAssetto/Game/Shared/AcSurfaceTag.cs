using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Marks a track collider with its surface (Data/Track/AcSurfaces: the [SURFACE_n] record of surfaces.ini - KEY, FRICTION, DAMPING,
    /// WAV, IS_VALID_TRACK, IS_PITLANE, SIN_HEIGHT/LENGTH, DIRT_ADDITIVE, BLACK_FLAG_TIME, VIBRATION_*). W adds one per physics MeshCollider
    /// when it builds a track (and calls <see cref="Register"/>); P's wheel rays, R's lap validity / pit lane and A's skid sounds read it
    /// through <see cref="Of(Collider)"/> - a dictionary lookup by instance id, no allocation, safe per frame and per wheel.
    /// Added at run time behind the gate (never placed in the scene). The surface data class itself is <see cref="AcSurface"/> (module D).
    /// </summary>
    public sealed class AcSurfaceTag : MonoBehaviour
    {
        /// <summary>The surface (never null after <see cref="Register"/>).</summary>
        public AcSurface Surface;
        /// <summary>A collision wall (digits + "WALL" mesh name): no grip data, cars bounce, lap validity unaffected.</summary>
        public bool IsWall;

        private static readonly Dictionary<int, AcSurfaceTag> ByCollider = new Dictionary<int, AcSurfaceTag>(1024);
        private int _colliderId;

        /// <summary>Adds (or updates) the tag on the collider's GameObject and indexes it; returns the tag.</summary>
        public static AcSurfaceTag Register(Collider c, AcSurface surface, bool wall)
        {
            if (c == null) return null;
            AcSurfaceTag t;
            if (!c.TryGetComponent(out t)) t = c.gameObject.AddComponent<AcSurfaceTag>();
            t.Surface = surface;
            t.IsWall = wall;
            t._colliderId = c.GetInstanceID();
            ByCollider[t._colliderId] = t;
            return t;
        }

        /// <summary>The surface of a collider (null when it is not a tagged track collider, e.g. a car, the hub floor, a wall).</summary>
        public static AcSurface Of(Collider c)
        {
            if (c == null) return null;
            AcSurfaceTag t;
            return ByCollider.TryGetValue(c.GetInstanceID(), out t) && t != null && !t.IsWall ? t.Surface : null;
        }

        /// <summary>The tag of a collider (null when untagged), walls included.</summary>
        public static AcSurfaceTag TagOf(Collider c)
        {
            if (c == null) return null;
            AcSurfaceTag t;
            return ByCollider.TryGetValue(c.GetInstanceID(), out t) ? t : null;
        }

        /// <summary>Forgets every tag (W calls it when the track is unloaded).</summary>
        public static void Clear() { ByCollider.Clear(); }

        /// <summary>Number of indexed colliders (log lines).</summary>
        public static int Count { get { return ByCollider.Count; } }

        private void OnDestroy()
        {
            AcSurfaceTag t;
            if (_colliderId != 0 && ByCollider.TryGetValue(_colliderId, out t) && ReferenceEquals(t, this)) ByCollider.Remove(_colliderId);
        }
    }
}
