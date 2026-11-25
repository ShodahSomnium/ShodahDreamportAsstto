using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Monitor first person: the own avatar's head shrunk away on this screen only, for the frame being drawn (BO2's Bo2HeadHide: the client never hides
    /// it, and its first-person camera sits inside it). Shrunk in LateUpdate after the pose, given back when every camera has drawn
    /// (RenderPipelineManager.endContextRendering): nothing is networked from it, others see the head, and the mirror copy (rotations only) keeps it.
    /// </summary>
    public static class AcHeadHide
    {
        /// <summary>Lab: keep it shrunk between frames (the lab draws its pictures outside the frame's rendering).</summary>
        public static bool LabHold;
        private static Transform _head;
        private static Vector3 _scale;
        private static bool _hooked;

        public static bool Hidden { get { return _head != null; } }

        public static void Hide(Transform head)
        {
            if (head == null) return;
            if (_head != null && _head != head) Restore();
            if (_head == null)
            {
                _head = head;
                _scale = head.localScale;
            }

            head.localScale = _scale * 0.0001f;
            if (!_hooked)
            {
                _hooked = true;
                RenderPipelineManager.endContextRendering += OnEnd;
            }
        }

        private static void OnEnd(ScriptableRenderContext context, List<Camera> cams)
        {
            if (!LabHold) Restore();
        }

        public static void Restore()
        {
            try { if (_head != null) _head.localScale = _scale; }
            catch (System.Exception) { /* the avatar went away */ }
            _head = null;
        }

        /// <summary>The match is over: unhook (the static event would otherwise outlive the world).</summary>
        public static void Unhook()
        {
            Restore();
            if (_hooked) { RenderPipelineManager.endContextRendering -= OnEnd; _hooked = false; }
        }
    }
}
