using System;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] The VR HUD: a world-space panel fixed to the seated car under the windscreen (port of ETS's EtsVrPanel.AttachTo idea). It
    /// moves with the car like the real dashboard - never with the head (DREAMPORT_GUIDE 1.4: nothing head-locked while driving).
    /// <para>Placement: from the eye S seats the head at (AcSeat.Geometry.EyeModel: system/cfg/vr.ini [car] DRIVEREYES when the game has
    /// one, else the cockpit eye; the passenger's own eye), the panel's centre is <c>hud.vr.distance</c> (default 62 cm) from the eye, turned
    /// to face it, <c>hud.vr.size</c> (default 0.42 m) wide. <c>hud.vr.place</c> 0 (default, driver): BESIDE THE STEERING WHEEL, toward the
    /// car's centre (over the centre console) - turned sideways until its inner edge is <see cref="RimClearDeg"/> outside the steering rim
    /// as seen from the eye (C's hub + rim radius; 20 deg before C's model, // ours), so the rim, the hands and the instrument cluster behind
    /// it stay free (lab 2026-11-01: the panel straight ahead covered the dash and the top of the wheel). 1 (and always for a passenger):
    /// straight ahead. Either way it is then lowered until its whole TOP EDGE is 1 degree under the car's dash line at every yaw it covers -
    /// the line where the road seen over the dash ends, measured once per car from the car's own kn5 on a worker
    /// (<see cref="AcHudSightline"/>; until it arrives: the steering-rim top seen from the eye, else 12 degrees, // ours) - and at least
    /// 6 degrees under the horizon. <c>hud.vr.raise</c> (cm) moves it from there. Numbers // ours, tunable in the tablet's HUD settings.</para>
    /// <para>Drawn with ZTest Always (AcHudView overlay materials) so the dash does not cut it - which is why it must stay out of the
    /// windscreen view and off the wheel. Compact canvas 900 x 660 units: dial (bottom left) | standings (bottom right); lap times above the
    /// dial, start lights / delta / split above the standings, flag and wrong way between; pit limiter and messages on top. Text at least
    /// 30 units (1.4 cm at the default size = 1.3 degrees at 62 cm).</para>
    /// </summary>
    public sealed class AcHudVr
    {
        public const float CanvasW = 900f, CanvasH = 660f;     // ours
        /// <summary>The top edge stays this far under the dash line, and this far under the horizon at least. // ours</summary>
        public const float MarginDeg = 1f, MinTopDeg = 6f;
        /// <summary>The panel's inner edge stays this far outside the steering rim (deg, seen from the eye). // ours</summary>
        public const float RimClearDeg = 3f;
        /// <summary>The steering rim's outer edge before C's model is known (deg from ahead). // ours (rim 0.17-0.19 m at ~0.5 m = 18-21 deg)</summary>
        public const float DefaultRimEdgeDeg = 20f;
        /// <summary>The dash line before anything is measured. // ours (the install's measured lines: H NOTES "VR panel")</summary>
        public const float DefaultLineDeg = 12f;      // harness vrhud: the 86 owned cars' lines straight ahead are 0-15.8 deg, 5-10 typical
        /// <summary>hud.vr.size default: the panel width (m). // ours</summary>
        public const float DefaultSize = 0.42f;

        public AcHudView View { get; private set; }
        public GameObject Go { get; private set; }
        public Canvas Canvas { get; private set; }
        public AcCarRig Rig { get; private set; }
        private float _nextCam, _nextEye;
        private int _eyeTries;             // ours: look for a better eye once a second, 30 times at most per car
        private float _dist = -1f, _raise, _size;
        private int _place = -1;
        private bool _passenger;
        private Transform _anchor;
        private Vector3 _eye;              // in the anchor's space
        private int _eyeRank = -1;         // 4 S seat eye from vr.ini, 3 S seat eye (seat data read), 2 S before its data / C model DRIVEREYES, 1 P spec, 0 fallback
        /// <summary>How the eye point was found (for the one-time log).</summary>
        public string EyeSource { get; private set; } = "";
        /// <summary>Where the dash line came from ("kn5 sightline", "steering rim top", "default") and its value straight ahead (deg).</summary>
        public string LineSource { get; private set; } = "";
        public float LineDeg { get; private set; } = DefaultLineDeg;
        /// <summary>The fitted top edge (deg below the horizon, highest point) and the centre's drop below the eye (m).</summary>
        public float TopDeg { get; private set; }
        public float DropM { get; private set; }
        /// <summary>The panel centre's yaw (deg, + = driver's right) and the steering rim's outer edge on that side (deg from ahead; -1 = none).</summary>
        public float YawDeg { get; private set; }
        public float RimEdgeDeg { get; private set; } = -1f;
        /// <summary>The car's centre is to the eye's right (+1, a left-hand-drive driver) or left (-1).</summary>
        public float CentreSide { get; private set; } = 1f;
        /// <summary>The kn5 sightline of the current car (null until measured / when it failed).</summary>
        public AcHudSightline Sightline { get { return _sight; } }

        private AcHudSightline _sight;
        private float _rimLine = -1f;
        // worker: one measurement at a time; the result is matched by key (car id + eye mm)
        private static volatile AcHudSightline _jobResult;
        private static volatile string _jobKey, _jobDoneKey, _jobProblem;
        private static volatile bool _jobBusy;
        private string _wantKey;
        private string _loggedKey;

        public static AcHudVr Create(Transform home)
        {
            var v = new AcHudVr();
            var go = new GameObject("Ac HUD (VR panel)");
            go.layer = AcLayers.Ui;
            go.transform.SetParent(home, false);
            v.Go = go;
            v.Canvas = go.AddComponent<Canvas>();
            v.Canvas.renderMode = RenderMode.WorldSpace;
            v.Canvas.sortingOrder = 100;     // ours: over other world canvases
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);

            var root = new GameObject("Root", typeof(RectTransform));
            root.layer = AcLayers.Ui;
            root.transform.SetParent(go.transform, false);
            var r = (RectTransform)root.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            v.View = new AcHudView(r, true);
            v.Layout();
            go.SetActive(false);
            return v;
        }

        private static void At(RectTransform g, float px, float py, float x, float y)
        {
            g.anchorMin = g.anchorMax = new Vector2(0.5f, 0.5f);
            g.pivot = new Vector2(px, py);
            g.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>Group places on the panel (units from its centre, y up). // ours</summary>
        public void Layout()
        {
            var v = View;
            float bottom = -CanvasH * 0.5f, left = -CanvasW * 0.5f, right = CanvasW * 0.5f;
            // left column: dial (420 x 260) at the bottom, lap times (320 x 194) above it
            At(v.GDial, 0f, 0f, left, bottom);
            At(v.GEss, 0.5f, 0f, left + 210f, bottom + 10f);
            At(v.GTimes, 0f, 0f, left + 10f, bottom + 268f);
            // right column: standings (400 x 244) at the bottom, split / delta / start lights (+ false-start bar) above
            At(v.GTower, 1f, 0f, right, bottom);
            At(v.GSplit, 0.5f, 0.5f, right - 200f, bottom + 270f);
            At(v.GDelta, 0.5f, 0.5f, right - 200f, bottom + 322f);
            At(v.GLights, 0.5f, 0.5f, right - 200f, bottom + 412f);
            // middle: flag, wrong way; top: pit limiter, messages
            At(v.GFlag, 0.5f, 0.5f, -35f, bottom + 322f);
            At(v.GWrong, 0.5f, 0.5f, -35f, bottom + 440f);
            At(v.GPit, 0.5f, 0.5f, 0f, bottom + 525f);
            At(v.GMsg, 0.5f, 1f, 0f, CanvasH * 0.5f - 2f);
            _dmgSide = 0f;
            PlaceDamage();
        }

        private float _dmgSide;

        /// <summary>The damage displayer cell ON the panel, in the lap-times box's place (left column above the dial; AcHudView hides the
        /// times while the displayer shows: TIME s after a hit, or a running pit repair). History: beside the panel on the wheel side it covered
        /// the wheel hub (hud-damage 2026-11-02); beside it on the car centre's side the flow lab race-damage-live 2026-11-02 measured the cell
        /// 77 deg right of straight ahead (and 34 deg down) on the 458 - outside a headset's view, so a hit's displayer was never seen. The
        /// left column is the part of the panel nearest the view centre. // ours</summary>
        private void PlaceDamage()
        {
            float side = CentreSide >= 0f ? 1f : -1f;
            if (side == _dmgSide || View == null || View.GDamage == null) return;
            _dmgSide = side;
            float bottom = -CanvasH * 0.5f, left = -CanvasW * 0.5f;
            At(View.GDamage, 0f, 0f, left + 10f, bottom + 268f);   // = GTimes' corner (Layout)
        }

        /// <summary>Hangs the panel on <paramref name="rig"/> (seat <paramref name="passenger"/>); re-places it when the settings change.</summary>
        public void Attach(AcCarRig rig, bool passenger)
        {
            if (rig == null || rig.Root == null) return;
            bool changed = rig != Rig || passenger != _passenger;
            Rig = rig; _passenger = passenger;
            if (changed) { _eyeRank = -1; _eyeTries = 0; _sight = null; _rimLine = -1f; _wantKey = null; FindEye(); _dist = -1f; }
            Place();
            if (!Go.activeSelf) Go.SetActive(true);
        }

        /// <summary>
        /// The eye in the anchor's space (anchor = C's model root, else the car body). Order: S's seat eye (AcSeat.Geometry.EyeModel - the
        /// eye S really seats the VR head at: vr.ini DRIVEREYES or the cockpit eye; passenger = its own seat), else C's model DriverEye
        /// (passenger mirrored), else P's spec DRIVEREYES in body space (research addendum: (-(eye.x+off.x), eye.y+off.y, eye.z+off.z)),
        /// else a typical saloon eye // ours. Re-placed only when a better source turns up.
        /// </summary>
        private void FindEye()
        {
            Transform modelRoot = null;
            try { if (Rig.Model != null && Rig.Model.Root != null) modelRoot = Rig.Model.Root; } catch (Exception) { modelRoot = null; }
            Transform anchor = modelRoot != null ? modelRoot : Rig.Root.transform;
            Vector3 eye = Vector3.zero; int rank = -1; string src = "";
            try
            {
                AcSeatGeometry g = AcSeat.Geometry;
                if (g == null || !ReferenceEquals(g.Rig, Rig)) g = AcSeatGeometry.Make(Rig);
                Vector3 p; bool fromVr;
                if (g != null && g.EyeModel(_passenger ? AcSeatKind.Passenger : AcSeatKind.Driver, true, out p, out fromVr))
                {
                    eye = anchor.InverseTransformPoint(g.ModelToWorld(p));
                    rank = fromVr ? 4 : (g.Data != null && g.Data.Ready ? 3 : 2);      // S reads vr.ini with the seat data: look again until it is read
                    src = fromVr ? "vr.ini DRIVEREYES (S seat eye)" : "S seat eye (cockpit DRIVEREYES)";
                }
            }
            catch (Exception) { rank = -1; }
            if (rank < 0)
            {
                try
                {
                    if (modelRoot != null && Rig.Model.DriverEye.sqrMagnitude > 0.01f)
                    {
                        eye = Rig.Model.DriverEye; rank = 2; src = "model DRIVEREYES";
                        if (_passenger) eye.x = -eye.x;
                    }
                    else if (Rig.Spec != null && (Rig.Spec.DriverEyes.X != 0f || Rig.Spec.DriverEyes.Y != 0f))
                    {
                        var e = Rig.Spec.DriverEyes; var o = Rig.Spec.GraphicsOffset;
                        anchor = Rig.Root.transform; eye = new Vector3(-(e.X + o.X), e.Y + o.Y, e.Z + o.Z); rank = 1; src = "car.ini DRIVEREYES";
                        if (_passenger) eye.x = -eye.x;
                    }
                }
                catch (Exception) { rank = -1; }
            }
            if (rank < 0) { anchor = Rig.Root.transform; eye = new Vector3(_passenger ? 0.36f : -0.36f, 0.75f, -0.1f); rank = 0; src = "fallback"; }   // ours: body space (CG origin)
            if (rank <= _eyeRank && anchor == _anchor) return;
            _anchor = anchor; _eye = eye; _eyeRank = rank; EyeSource = src;
            _dist = -1f; _sight = null; _wantKey = null;      // a new eye: its own sightline
            RimLine(modelRoot);
            RequestSightline(modelRoot);
        }

        /// <summary>The steering rim's top as seen from the eye (deg below the horizon): C's measured hub, column axis and rim radius.</summary>
        private void RimLine(Transform modelRoot)
        {
            _rimLine = -1f;
            RimEdgeDeg = -1f;
            CentreSide = _eye.x <= 0f ? 1f : -1f;        // Unity model space: x < 0 = the car's left seat -> the centre is to the right
            try
            {
                AcCarModel m = Rig.Model;
                if (m == null || modelRoot == null || _anchor != modelRoot || m.SteeringRimRadius < 0.08f) return;
                Vector3 ax = m.SteeringAxis.sqrMagnitude > 0.5f ? m.SteeringAxis.normalized : Vector3.forward;
                Vector3 up = Vector3.up - ax * Vector3.Dot(Vector3.up, ax);
                if (up.sqrMagnitude < 0.01f) return;
                up.Normalize();
                Vector3 d = m.SteeringCenter + up * m.SteeringRimRadius - _eye;
                _rimLine = Mathf.Atan2(-d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z)) * Mathf.Rad2Deg;
                // the rim's outer edge on the centre side, seen from the eye (16 points round the rim; + the rim tube, 2 cm // ours)
                Vector3 side = Vector3.Cross(up, ax);
                float edge = -90f, r = m.SteeringRimRadius + 0.02f;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * (Mathf.PI * 2f / 16f);
                    Vector3 q = m.SteeringCenter + (up * Mathf.Cos(a) + side * Mathf.Sin(a)) * r - _eye;
                    if (q.z < 0.05f) continue;
                    float yaw = CentreSide * Mathf.Atan2(q.x, q.z) * Mathf.Rad2Deg;
                    if (yaw > edge) edge = yaw;
                }
                if (edge > -90f) RimEdgeDeg = Mathf.Max(0f, edge);
            }
            catch (Exception) { _rimLine = -1f; RimEdgeDeg = -1f; }
        }

        /// <summary>Starts the kn5 sightline of this car from this eye on a worker (one at a time; a newer request waits for the running one).</summary>
        private void RequestSightline(Transform modelRoot)
        {
            if (modelRoot == null || _anchor != modelRoot || Rig.Car == null || string.IsNullOrEmpty(Rig.Car.Kn5Path)) return;
            var ac = new AcVec3(-_eye.x, _eye.y, _eye.z);             // Unity model space -> kn5 (AC) space: X mirrored
            string key = Rig.Car.Id + "@" + Mathf.RoundToInt(_eye.x * 1000f) + "," + Mathf.RoundToInt(_eye.y * 1000f) + "," + Mathf.RoundToInt(_eye.z * 1000f);
            _wantKey = key;
            if (_jobDoneKey == key && _jobResult != null) { _sight = _jobResult; return; }
            if (_jobBusy) return;              // Tick retries once the running job is done
            _jobBusy = true; _jobKey = key;
            StartJob(Rig.Car.Kn5Path, ac, key);
        }

        /// <summary>The worker start in its own method: the lambda's closure is allocated only here, once per measurement.</summary>
        private static void StartJob(string path, AcVec3 ac, string key)
        {
            var t = new Thread(() => MeasureJob(path, ac, key)) { IsBackground = true, Name = "Assetto HUD sightline", Priority = System.Threading.ThreadPriority.BelowNormal };
            t.Start();
        }

        private static void MeasureJob(string path, AcVec3 eye, string key)
        {
            string problem = null;
            AcHudSightline s = null;
            try { s = AcHudSightline.Measure(path, eye, out problem); }
            catch (Exception e) { problem = e.Message; }
            _jobResult = s; _jobProblem = problem; _jobDoneKey = key;
            _jobBusy = false;
        }

        private void PollSightline()
        {
            if (_wantKey == null || _sight != null) return;
            if (_jobDoneKey == _wantKey)
            {
                _sight = _jobResult;
                _dist = -1f;
                if (_sight == null) AcLog.WarnOnce("Hud: VR dash line of " + (Rig.Car != null ? Rig.Car.Id : "?") + " not measured (" + _jobProblem + "): the steering rim / 12 degree default is used");
                _wantKey = null;               // handled (a failed measurement is not retried for this eye)
            }
            else if (!_jobBusy)
            {
                Transform m = null;
                try { if (Rig.Model != null) m = Rig.Model.Root; } catch (Exception) { m = null; }
                RequestSightline(m);
            }
        }

        private void Place()
        {
            float dist = Mathf.Clamp(AcSettings.Num("hud.vr.distance", 62f), 30f, 120f) * 0.01f;
            float raise = Mathf.Clamp(AcSettings.Num("hud.vr.raise", 0f), -40f, 20f) * 0.01f;
            float size = Mathf.Clamp(AcSettings.Num("hud.vr.size", DefaultSize), 0.25f, 0.9f);
            int place = _passenger ? 1 : Mathf.Clamp(AcSettings.Int("hud.vr.place", 0), 0, 1);
            if (_anchor == null) return;
            if (Go.transform.parent != _anchor) { Go.transform.SetParent(_anchor, false); _dist = -1f; }
            if (Mathf.Abs(dist - _dist) < 0.0005f && Mathf.Abs(raise - _raise) < 0.0005f && Mathf.Abs(size - _size) < 0.0005f && place == _place) return;
            _dist = dist; _raise = raise; _size = size; _place = place;
            float heightM = size * CanvasH / CanvasW;
            float flat = _rimLine >= 0f ? _rimLine : DefaultLineDeg;
            LineSource = _sight != null ? "kn5 sightline" : _rimLine >= 0f ? "steering rim top" : "default";
            LineDeg = _sight != null ? _sight.LineAt(0f) : flat;       // straight ahead (the fit uses the whole line under the panel)
            // the yaw: straight ahead, or beside the rim - the inner edge RimClearDeg outside it (solved with the fitted pitch, 3 rounds)
            float yaw = 0f, top, pitch;
            float rimEdge = RimEdgeDeg >= 0f ? RimEdgeDeg : DefaultRimEdgeDeg;
            if (place == 0) yaw = CentreSide * (rimEdge + RimClearDeg + Mathf.Atan(size * 0.5f / dist) * Mathf.Rad2Deg);
            pitch = AcHudSightline.FitAt(_sight, flat, dist, yaw, size, heightM, MarginDeg, MinTopDeg, out top);
            for (int it = 0; place == 0 && it < 3; it++)
            {
                float inner = AcHudSightline.InnerEdgeYaw(pitch, yaw, dist, size, heightM);
                yaw += CentreSide * (rimEdge + RimClearDeg - inner);
                pitch = AcHudSightline.FitAt(_sight, flat, dist, yaw, size, heightM, MarginDeg, MinTopDeg, out top);
            }
            YawDeg = yaw; TopDeg = top;
            float px, py, pz;
            AcHudSightline.PanelPoint(pitch, yaw, dist, 0f, 0f, out px, out py, out pz);   // eye frame = Unity model axes (x right, y up, z forward)
            DropM = -py - raise;
            Vector3 p = _eye + new Vector3(px, py + raise, pz);
            var rt = (RectTransform)Go.transform;
            rt.localPosition = p;
            Vector3 look = p - _eye;   // the canvas faces the eye: its forward points away from it
            rt.localRotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            rt.localScale = Vector3.one * (size / CanvasW);
            string key = (Rig != null && Rig.Car != null ? Rig.Car.Id : "?") + "|" + EyeSource + "|" + LineSource + "|" + _passenger + "|" + place + "|" + (RimEdgeDeg >= 0f);
            if (key != _loggedKey)
            {
                _loggedKey = key;
                AcLog.Info("Hud: VR panel on " + (Rig != null && Rig.Car != null ? Rig.Car.Id : "?") + (_passenger ? " (passenger)" : "") + ": eye " + EyeSource
                    + ", dash line " + LineDeg.ToString("0.0") + " deg (" + LineSource + (_sight != null ? ", " + _sight.Triangles + " tris, " + _sight.Ms + " ms" : "")
                    + "), top edge " + TopDeg.ToString("0.0") + " deg, " + (place == 0 ? "beside the wheel at yaw " + YawDeg.ToString("0") + " deg (rim edge " + rimEdge.ToString("0") + " deg" + (RimEdgeDeg < 0f ? " default" : "") + ")" : "straight ahead") + ", centre " + (DropM * 100f).ToString("0") + " cm below the eye, " + (dist * 100f).ToString("0") + " cm away, "
                    + size.ToString("0.00") + " m wide");
            }
        }

        /// <summary>Once a frame while shown: settings changes, a better eye / the measured dash line, the client's head camera.</summary>
        public void Tick(float now)
        {
            if (Rig == null || Rig.Root == null) { Hide(); return; }
            // C's model / S's seat can arrive after the rig: look again once a second until the best source (S's seat eye) is used
            if (_anchor == null || (_eyeRank < 3 && now >= _nextEye && _eyeTries < 30))
            {
                _nextEye = now + 1f; _eyeTries++;
                FindEye();
            }
            PollSightline();
            Place();
            PlaceDamage();
            if (now >= _nextCam)
            {
                _nextCam = now + 0.5f;
                Camera head = AcPlayer.Head();
                if (head != null && Canvas.worldCamera != head) Canvas.worldCamera = head;
            }
        }

        /// <summary>The eye point the panel is placed from, in world space, and the car's forward (lab camera / checks).</summary>
        public bool EyeWorld(out Vector3 eye, out Vector3 forward)
        {
            eye = Vector3.zero; forward = Vector3.forward;
            if (_anchor == null) return false;
            eye = _anchor.TransformPoint(_eye);
            forward = _anchor.forward;
            return true;
        }

        /// <summary>The panel's top edge as pitch below the eye's horizon (deg), the highest of its two corners and its middle (lab check: measured
        /// from the drawn RectTransform corners, not from the fit).</summary>
        public float MeasuredTopDeg(out float worstClearDeg)
        {
            worstClearDeg = 999f;
            if (_anchor == null) return -999f;
            var corners = new Vector3[4];
            ((RectTransform)Go.transform).GetWorldCorners(corners);     // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            float best = 999f;
            for (int i = 0; i <= 8; i++)
            {
                Vector3 w = Vector3.Lerp(corners[1], corners[2], i / 8f);
                Vector3 l = _anchor.InverseTransformPoint(w) - _eye;
                float yaw = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                float pitch = Mathf.Atan2(-l.y, Mathf.Sqrt(l.x * l.x + l.z * l.z)) * Mathf.Rad2Deg;
                if (pitch < best) best = pitch;
                float line = _sight != null ? _sight.LineAt(yaw) : (_rimLine >= 0f ? _rimLine : DefaultLineDeg);
                if (pitch - line < worstClearDeg) worstClearDeg = pitch - line;
            }
            return best;
        }

        /// <summary>The drawn panel's edge nearest straight ahead, as yaw toward the car's centre (deg; lab check against <see cref="RimEdgeDeg"/>),
        /// and the lowest point of the panel (deg below the horizon).</summary>
        public float MeasuredInnerEdgeDeg(out float bottomDeg)
        {
            bottomDeg = -999f;
            if (_anchor == null) return -999f;
            var corners = new Vector3[4];
            ((RectTransform)Go.transform).GetWorldCorners(corners);
            float inner = 999f;
            for (int k = 0; k < 2; k++)
            {
                Vector3 a = corners[k == 0 ? 0 : 3], b = corners[k == 0 ? 1 : 2];     // left edge, right edge
                for (int i = 0; i <= 8; i++)
                {
                    Vector3 l = _anchor.InverseTransformPoint(Vector3.Lerp(a, b, i / 8f)) - _eye;
                    float yaw = CentreSide * Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                    float pitch = Mathf.Atan2(-l.y, Mathf.Sqrt(l.x * l.x + l.z * l.z)) * Mathf.Rad2Deg;
                    if (yaw < inner) inner = yaw;
                    if (pitch > bottomDeg) bottomDeg = pitch;
                }
            }
            return inner;
        }

        public void Hide()
        {
            if (Go != null && Go.activeSelf) Go.SetActive(false);
        }

        /// <summary>Back under the HUD root (the car is about to go).</summary>
        public void Detach(Transform home)
        {
            if (Go != null && Go.transform.parent != home) Go.transform.SetParent(home, false);
            Rig = null; _anchor = null; _eyeRank = -1; _sight = null; _wantKey = null;
            Hide();
        }

        public void Destroy() { if (Go != null) UnityEngine.Object.Destroy(Go); Go = null; }
    }
}
