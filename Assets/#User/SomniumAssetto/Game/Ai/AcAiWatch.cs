using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [I, AI drivers] Added by AcAiField.Setup to every AI rig root (after the gate, on the master only): once a frame calls
    /// <see cref="AcAi.Watch"/>, which keeps the AI cars driving through a short master flap and hands them over (removes them here) once
    /// this client has not been the instance master for <see cref="AcAi.HandOverAfter"/> s - R ticks AcAi only on the master, so without
    /// this a client that lost the role would keep dynamic AI bodies with frozen inputs under the new master's copies. Nothing else.
    /// </summary>
    public sealed class AcAiWatch : MonoBehaviour
    {
        private static int _errors;

        private void Update()
        {
            try { AcAi.Watch(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Ai: watcher failed: " + e); }
        }
    }
}
