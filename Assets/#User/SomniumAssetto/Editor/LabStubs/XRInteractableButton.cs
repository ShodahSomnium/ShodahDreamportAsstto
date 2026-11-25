using UnityEngine;
using UnityEngine.Events;

namespace SomniumSpace.Worlds.SomniumAssetto.LabStubs
{
    /// <summary>The mesh Home button's XRInteractableButton: a public UnityEvent field ButtonPressed (AcTabletLink reads it by reflection). For the tablet lab only; see TabletControl.cs.</summary>
    public sealed class XRInteractableButton : MonoBehaviour
    {
        public UnityEvent ButtonPressed = new UnityEvent();
    }
}
