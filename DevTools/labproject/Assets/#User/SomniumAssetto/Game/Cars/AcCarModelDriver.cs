using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] Runs a built car's time-based work (<see cref="AcCarModel.Tick"/>) in LateUpdate. Added at run time by
    /// <see cref="AcCarModel.BuildAsync"/> on the model root (never placed in the scene, so nothing runs before the whitelist gate).
    /// </summary>
    public sealed class AcCarModelDriver : MonoBehaviour
    {
        public AcCarModel Model;

        void LateUpdate()
        {
            var m = Model;
            if (m != null) m.Tick(Time.deltaTime);
        }
    }
}
