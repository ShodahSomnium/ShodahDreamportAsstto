using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] Runs the HUD once a frame, after the seat has carried the body and the car (S's late carry at 10000) so the VR panel and the
    /// numbers belong to the same frame. Added to the "Ac Game" object by <see cref="AcHud.Start"/> (after the whitelist gate, through R).
    /// </summary>
    [DefaultExecutionOrder(10010)]
    public sealed class AcHudDriver : MonoBehaviour
    {
        private void LateUpdate() { AcHud.Late(); }
    }
}
