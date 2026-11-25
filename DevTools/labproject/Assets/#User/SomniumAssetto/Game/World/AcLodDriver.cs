using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// AC's per-mesh LOD distances (kn5 lodIn / lodOut in metres: drawn while lodIn &lt;= distance &lt; lodOut; 0/0 = always): HI/LOW pairs
    /// such as 0-250 / 245-500 on track barriers, grass 0-150. Added at run time to a built track / model root (never placed in the scene).
    /// Distance from the viewer (the player's camera, <see cref="AcPlayer.Head"/>, or <see cref="ViewerOverride"/>) to the mesh's bounds
    /// centre [BELIEF: AC measures to the bounding sphere centre]. A round-robin sweep checks <see cref="SweepFrames"/>-th of the meshes per
    /// frame (no allocation): the whole set is re-decided every ~0.25 s.
    /// On a track (<see cref="WantFar"/> &gt; 0) it also keeps the player's camera far plane at least that far (tracks span several km, Unity's
    /// default is 1000 m: the far side of the track and the sky dome would be cut off) and puts the camera's own value back when the track goes.
    /// </summary>
    public sealed class AcLodDriver : MonoBehaviour
    {
        /// <summary>Frames per full sweep. // ours</summary>
        public const int SweepFrames = 15;
        /// <summary>A viewer to use instead of the player's camera (the lab camera).</summary>
        public static Transform ViewerOverride;

        /// <summary>The far plane (m) the player's camera needs on this track (0 = leave the camera alone).</summary>
        public float WantFar;
        /// <summary>The player's camera far plane when this track first saw it (-1 = not seen yet) - for the log.</summary>
        public static float ClientFarSeen = -1f;

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Transform> _transforms = new List<Transform>();
        readonly List<Vector3> _centres = new List<Vector3>();
        readonly List<float> _in2 = new List<float>(), _out2 = new List<float>();
        int _next;
        Camera _farCam; float _farOriginal = -1f, _farSet = -1f; int _farTick;

        public int Count { get { return _renderers.Count; } }

        /// <summary>Adds a renderer with its kn5 LOD range (call only for lodOut &gt; 0). <paramref name="localCentre"/> in the renderer's space.</summary>
        public void Add(Renderer r, Vector3 localCentre, float lodIn, float lodOut)
        {
            if (r == null || lodOut <= 0f) return;
            _renderers.Add(r);
            _transforms.Add(r.transform);
            _centres.Add(localCentre);
            _in2.Add(lodIn * lodIn);
            _out2.Add(lodOut * lodOut);
            r.enabled = false;   // decided by the first sweep
        }

        void Update()
        {
            if (WantFar > 0f && ++_farTick >= 30) { _farTick = 0; KeepFar(); }   // // ours: twice a second at 60 fps
            int n = _renderers.Count;
            if (n == 0) return;
            Vector3 eye;
            if (ViewerOverride != null) eye = ViewerOverride.position;
            else
            {
                Camera cam = AcPlayer.Head();
                if (cam == null) return;
                eye = cam.transform.position;
            }
            int per = (n + SweepFrames - 1) / SweepFrames;
            for (int k = 0; k < per; k++)
            {
                if (_next >= n) _next = 0;
                int i = _next++;
                var r = _renderers[i];
                if (r == null) continue;
                float d2 = (_transforms[i].TransformPoint(_centres[i]) - eye).sqrMagnitude;
                bool on = d2 >= _in2[i] && d2 < _out2[i];
                if (r.enabled != on) r.enabled = on;
            }
        }

        /// <summary>Raises the player's camera far plane to <see cref="WantFar"/> (never lowers it); a new camera (seat / view change) is handled too.</summary>
        public void KeepFar()
        {
            Camera cam;
            try { cam = AcPlayer.Head(); } catch (System.Exception) { cam = null; }
            if (cam == null) return;
            if (cam != _farCam)
            {
                RestoreFar();
                _farCam = cam; _farOriginal = cam.farClipPlane; _farSet = -1f;
                if (ClientFarSeen < 0f)
                {
                    ClientFarSeen = _farOriginal;
                    AcLog.Info("Track: player camera far plane " + _farOriginal.ToString("0") + " m, near " + cam.nearClipPlane.ToString("0.###") + " m; the track needs " + WantFar.ToString("0") + " m" +
                               (_farOriginal < WantFar ? " -> raised while the track is loaded" : ""));
                }
            }
            if (cam.farClipPlane < WantFar) { cam.farClipPlane = WantFar; _farSet = WantFar; }
        }

        void RestoreFar()
        {
            if (_farCam != null && _farSet > 0f && Mathf.Approximately(_farCam.farClipPlane, _farSet)) _farCam.farClipPlane = _farOriginal;
            _farCam = null; _farSet = -1f;
        }

        void OnDestroy() { RestoreFar(); }

        /// <summary>Decides every renderer now (after a teleport or for a lab picture).</summary>
        public void SweepAll()
        {
            int keep = _next;
            _next = 0;
            int ft = _farTick; float wf = WantFar; WantFar = 0f;
            for (int s = 0; s < SweepFrames; s++) Update();
            WantFar = wf; _farTick = ft;
        }
    }
}
