using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Desk] The elevator carry for the own MONITOR avatar while seated (ETS EtsAvatarSync Remember l.298 / Carry l.312, order 9990): in Update after the
    /// client placed the avatar's main root (order 9990, after the client's order 0) its pose is remembered in the body root's frame; in
    /// LateUpdate, after the seat's late write, it is put back on the body root by the same pose - so nothing the seat does late (pose
    /// re-entry, the head-on-eye shift) leaves the avatar a frame of travel behind (1.4 m at 300 km/h). VR avatars are placed by VRIK after
    /// LateUpdate and are left alone. The main root is the transform carrying the client's MainRoot / NonXRMainRoot (type names survive the
    /// obfuscation - BO2 / Skyrim / ETS rely on it).
    /// </summary>
    public static class AcDeskCarry
    {
        private static Transform _main, _body;
        private static Vector3 _offPos;
        private static Quaternion _offRot = Quaternion.identity;
        private static int _offFrame = -1;
        private static float _nextFind, _worst;
        private static bool _said;
        private static readonly List<MonoBehaviour> Buf = new List<MonoBehaviour>(8);

        private static bool Active { get { return AcSeat.Seated && !AcSeat.Vr && AcSeat.LabRoot == null; } }

        /// <summary>Update, order 9990.</summary>
        internal static void Remember()
        {
            if (!Active) { _main = null; return; }
            _body = AcPlayer.Root;
            if (_body == null) return;
            if (_main == null || !_main.gameObject.activeInHierarchy)
            {
                if (Time.unscaledTime < _nextFind) return;
                _nextFind = Time.unscaledTime + 1f;
                Find();
                if (_main == null) return;
            }

            Quaternion inv = Quaternion.Inverse(_body.rotation);
            _offPos = inv * (_main.position - _body.position);
            _offRot = inv * _main.rotation;
            _offFrame = Time.frameCount;
        }

        /// <summary>LateUpdate, order 9990 (after the seat's 9000 write, before the hands are posed at 9997; again at 10000 if the seat had to re-write).</summary>
        internal static void Carry()
        {
            if (!Active || _main == null || _body == null || _offFrame != Time.frameCount) return;
            if (_main == _body || _main.IsChildOf(_body)) return;      // it rides on the body already
            Quaternion rot = _body.rotation;
            Vector3 pos = _body.position + rot * _offPos;
            float moved = (pos - _main.position).magnitude;
            _main.SetPositionAndRotation(pos, rot * _offRot);
            if (moved > _worst) _worst = moved;
            if (!_said && moved > 0.01f)
            {
                _said = true;
                AcLog.Info("Seat: my monitor avatar '" + _main.name + "' carried " + (moved * 100f).ToString("0.0") + " cm onto this frame's seat in LateUpdate (the client placed it in Update).");
            }
        }

        private static void Find()
        {
            Transform head = AcPlayer.HeadBone;
            if (head == null) return;
            string kind;
            Animator a = AcAvatarFind.ByHead(head.position, out kind);
            _main = null;
            for (Transform t = a != null ? a.transform : null; t != null; t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                {
                    if (Buf[i] == null) continue;
                    string n = Buf[i].GetType().Name;
                    if (n == "MainRoot" || n == "NonXRMainRoot") { _main = t; break; }
                }

                if (_main != null || t.parent == null || t == a.transform.root) break;
            }

            Buf.Clear();
        }
    }
}
