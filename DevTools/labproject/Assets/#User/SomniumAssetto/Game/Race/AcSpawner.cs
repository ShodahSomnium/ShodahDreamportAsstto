using System;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] Putting cars and the player where AC puts them: pit box (AC_PIT_n), grid slot (AC_START_n), hotlap start
    /// (AC_HOTLAP_START_0), back on the racing line. Research T1: markers float 0-7.7 m above the road -> the marker's XZ + a ray down
    /// (W's TryGround from marker + 1 m) and P's ResetTo (which casts again and puts the wheels on the hit); T2: heading from the
    /// HORIZONTAL part of node +Z only (W's AcSpawnPoint.HeadingDeg). Never at the node height. Main thread.
    /// </summary>
    public static class AcSpawner
    {
        /// <summary>The player stands this far beside the car's door line after a spawn (m). // ours</summary>
        public const float BesideCarMetres = 2.2f; // ours
        /// <summary>A pit box / hotlap start with another car within this distance (XZ, m) is taken. // ours</summary>
        public const float OccupiedMetres = 3.5f; // ours

        private static bool _warnedGround;

        /// <summary>data/crew.ini [HEADER] SIDE of the loaded layout: 1 = the crew (garage) is on the car's LEFT, -1 = right (monza -1,
        /// magione 1 - research 6.8). The player is put on that side (away from the fast lane). 0 = unknown.</summary>
        public static int CrewSide { get; private set; }

        /// <summary>Reads the layout's crew.ini (call after a track load; never throws).</summary>
        public static void ReadLayout(AcLayoutEntry layout)
        {
            CrewSide = 0;
            try
            {
                if (layout == null || string.IsNullOrEmpty(layout.DataDir)) return;
                string p = Path.Combine(layout.DataDir, "crew.ini");
                if (!File.Exists(p)) return;
                int side = AcIni.Load(p).GetInt("HEADER", "SIDE", 0);
                CrewSide = side > 0 ? 1 : side < 0 ? -1 : 0;
            }
            catch (Exception e) { AcLog.Warn("Race: crew.ini: " + e.Message); }
        }

        /// <summary>The road point under a marker (ray down from marker + 1 m, research T1); false = no ground found (then the marker itself).</summary>
        public static Vector3 GroundUnder(Vector3 marker, out bool hit)
        {
            Vector3 p, n;
            hit = false;
            try { hit = AcTrackWorld.TryGround(marker, out p, out n); }
            catch (Exception e) { p = marker; AcLog.Warn("Race: TryGround failed: " + e.Message); }
            if (!hit)
            {
                if (!_warnedGround) { _warnedGround = true; AcLog.Warn("Race: no track ground under the marker at " + marker.ToString("0.0") + " - placing the car at the marker (P's ResetTo casts again)"); }
                return marker;
            }
            return p;
        }

        /// <summary>Puts a car on a spawn marker: ground under the marker, yaw from the marker's horizontal heading, stopped.</summary>
        public static Vector3 PlaceAt(AcCarRig rig, AcSpawnPoint point)
        {
            bool hit;
            Vector3 ground = GroundUnder(point.Marker, out hit);
            Place(rig, ground, point.HeadingDeg);
            return ground;
        }

        /// <summary>Teleports a car (stopped) to <paramref name="ground"/> with yaw <paramref name="headingDeg"/> through P's ResetTo.</summary>
        public static void Place(AcCarRig rig, Vector3 ground, float headingDeg)
        {
            if (rig == null || rig.Root == null) return;
            Quaternion rot = Quaternion.Euler(0f, headingDeg, 0f);
            if (rig.Physics != null)
            {
                try { rig.Physics.ResetTo(ground, rot); return; }
                catch (Exception e) { AcLog.Error("Race: ResetTo failed: " + e); }
            }
            rig.Root.transform.SetPositionAndRotation(ground, rot);
        }

        /// <summary>The car's pose for timing: the Rigidbody's physics pose when there is one, else the root transform.</summary>
        public static Vector3 CarPosition(AcCarRig rig)
        {
            if (rig == null) return Vector3.zero;
            AcCarPhysics ph = rig.Physics;
            if (ph != null && ph.Body != null) return ph.Body.position;
            return rig.Root != null ? rig.Root.transform.position : Vector3.zero;
        }

        /// <summary>The clock that belongs to <see cref="CarPosition"/>: physics time with a Rigidbody, frame time without.</summary>
        public static double CarClock(AcCarRig rig)
        {
            AcCarPhysics ph = rig != null ? rig.Physics : null;
            return ph != null && ph.Body != null ? Time.fixedTimeAsDouble : Time.timeAsDouble;
        }

        /// <summary>The racing-line point nearest the car and the heading along the line (reset to track). False without a line.</summary>
        public static bool NearestOnLine(Vector3 carPos, out Vector3 linePoint, out float headingDeg)
        {
            linePoint = carPos; headingDeg = 0f;
            AcAiSpline line = AcTrackWorld.AiLine;
            if (line == null || line.IsEmpty) return false;
            float x, y, z;
            AcTrackWorld.ToAc(carPos, out x, out y, out z);
            int i = line.Nearest(x, y, z);
            if (i < 0) return false;
            int j = i + 1 >= line.Count ? (line.IsClosed ? 0 : i) : i + 1;
            if (j == i) { j = i; i = Math.Max(0, i - 1); }
            linePoint = AcTrackWorld.ToUnity(line.X(i), line.Y(i), line.Z(i));
            Vector3 ahead = AcTrackWorld.ToUnity(line.X(j), line.Y(j), line.Z(j));
            Vector3 d = ahead - linePoint; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return false;
            headingDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Where the player should stand beside a parked car (crew side, else the car's left), on the ground, and the yaw facing the car.</summary>
        public static Vector3 BesideCar(AcCarRig rig, out float yawDeg)
        {
            yawDeg = 0f;
            if (rig == null || rig.Root == null) return Vector3.zero;
            Transform t = rig.Root.transform;
            Vector3 right = t.right; right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            // Unity's +right is the car's right; crew.ini SIDE 1 = left, -1 = right (unknown: left, the usual garage side for clockwise boxes - ours)
            float side = CrewSide < 0 ? 1f : -1f;
            Vector3 p = t.position + right * (side * BesideCarMetres);
            bool hit;
            Vector3 ground = GroundUnder(p + Vector3.up * 1.5f, out hit);
            if (!hit) ground = new Vector3(p.x, t.position.y, p.z);
            Vector3 look = t.position - ground; look.y = 0f;
            if (look.sqrMagnitude > 1e-6f) yawDeg = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            return ground;
        }

        /// <summary>Teleports the local player (Bridge) beside <paramref name="rig"/>. Logs the result once per call site reason.</summary>
        public static bool PlayerBeside(AcCarRig rig, string why)
        {
            float yaw;
            Vector3 p = BesideCar(rig, out yaw);
            if (p == Vector3.zero) return false;
            string how;
            bool ok = false;
            try { ok = AcPlayer.Teleport(p + Vector3.up * 0.05f, yaw, out how); }
            catch (Exception e) { how = e.Message; }
            AcLog.Info("Race: player " + (ok ? "moved" : "NOT moved") + " beside the car (" + why + ", " + how + ")");
            return ok;
        }

        /// <summary>Is another car (remote, AI) parked within <see cref="OccupiedMetres"/> of <paramref name="at"/>?</summary>
        public static bool Occupied(Vector3 at, AcCarRig except)
        {
            float r2 = OccupiedMetres * OccupiedMetres;
            try
            {
                var cars = AcNetGame.RemoteCars;
                for (int i = 0; i < cars.Count; i++)
                {
                    // N: a remote car still building / not installed here is a solid grey stand-in - Occupies + Frame cover both
                    AcRemoteCar rc = cars[i];
                    if (rc == null || !rc.Occupies || (except != null && rc.Rig == except)) continue;
                    Transform f = rc.Frame;
                    if (f != null && FlatDist2(f.position, at) < r2) return true;
                }
                var ai = AcAi.Drivers;
                for (int i = 0; i < ai.Count; i++)
                {
                    AcCarRig r = ai[i] != null ? ai[i].Rig : null;
                    if (r != null && r != except && r.Root != null && FlatDist2(r.Root.transform.position, at) < r2) return true;
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Race: occupancy check failed: " + e.Message); }
            return false;
        }

        private static float FlatDist2(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
    }
}
