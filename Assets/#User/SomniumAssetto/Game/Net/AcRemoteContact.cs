using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Fixer 2026-11-02 (DAMAGE_DESIGN 1.3 step 3): sits on a remote car's grey stand-in box. The box is moved through its parent transform,
    /// so PhysX knows no velocity for it; N writes the drawn (timeline-sampled) velocity here every Draw and P's damage hit reads it as the
    /// other side of the impact speed. <see cref="Ghostable"/> mirrors the ghost rule's 'special' state (just placed / in the pit lane) so
    /// cars other than the local one (the master's AI) take no damage from it. Plain data, no Update.
    /// </summary>
    public sealed class AcRemoteContact : MonoBehaviour
    {
        /// <summary>World velocity (m/s) of the remote car as drawn this frame.</summary>
        public Vector3 Velocity;
        /// <summary>True while the remote car is 'special' for the ghost rule.</summary>
        public bool Ghostable;
    }
}
