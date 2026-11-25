using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [S] Lab scenarios of the seat module (DevTools/lab.sh &lt;name&gt; [-accar ferrari_458]). The editor has no Bridge player, so a stand-in body
    /// (AcSeat.LabRoot / LabHead / LabLeft / LabRight) and simulated controllers (AcXr.Simulated) take its place:
    /// - seat-enter: spawn the car in the hub (P's AcCarRig, frozen); the get-in target exists on layer 16 around the DRIVER'S door (not the
    ///   whole car); pressing it (the VR trigger path of AcDoorHandle) seats the player: car occupied + driven, body root on the seat (knh
    ///   RIG_Hips), the car pitched / rolled -> the body root stays level (zero pitch / roll, the car's heading); VR: the head lands on the eye
    ///   (vr.ini / car.ini DRIVEREYES); pictures from the cockpit eye (steering wheel ahead at the right height), chase near AND far, bonnet,
    ///   the door open on the way out; the wheel grab turns the wheel by the hand's angle, and C's STEER_HR turned +30 moves the rim's own top
    ///   vertex to the driver's right (independent of the angle convention); pedals stay disarmed until both triggers were released; getting
    ///   out stands the body outside the driver's door; a passenger in a second (remote) car through AcRiders.GrantLocally lands on the
    ///   mirrored seat and gets out on the right; ForceExit releases and parks; a car disposed under a seated player releases them (Vanished).
    /// Not covered here: hands on the wheel (needs a humanoid avatar - none ships in the project; client test), the race-phase refusal of
    /// CanExit (AcSession's setters are R's), the Bridge pose / recentre (client only).
    /// </summary>
    [InitializeOnLoad]
    internal static class AcLabSeat
    {
        static AcLabSeat()
        {
            AcLabRunner.Register("seat-enter", r => Enter(r));
        }

        private static AcCarRig _rig, _rig2;
        private static Transform _root;

        private static IEnumerator Enter(AcLabRunner r)
        {
            string id = AcLabBoot.Arg("-accar", "ferrari_458");
            AcCarEntry car = r.Game.Content != null ? r.Game.Content.Car(id) : null;
            if (!r.Check(car != null && car.Owned, "car " + id + " is installed and owned")) yield break;
            AcSeat.Start(r.Game);

            // the car in the hub, frozen (P's factory; C's model)
            _rig = null;
            var opt = new AcCarRigOptions { Kind = AcCarKind.Local, Audio = false };
            yield return AcCarRig.Spawn(car, car.Skins.Count > 0 ? car.Skins[0].Id : "", opt, Got);
            yield return r.Wait(() => _rig != null, 120f, "the car rig");
            if (!r.Check(_rig != null && _rig.Root != null, "car rig spawned")) yield break;
            Transform carT = _rig.Root.transform;
            Vector3 at = AcSpace.RoomOrigin + new Vector3(0f, 0.6f, 6f); // ours: in front of the hub spawn point
            if (_rig.Physics != null) { _rig.Physics.ResetTo(at, Quaternion.identity); _rig.Physics.Freeze(true); }
            else carT.SetPositionAndRotation(at, Quaternion.identity);
            AcDoors.LabCar = _rig;   // (in the world the own car is AcLocal.Car, set by R / P)
            HideHubCar();   // F's showroom car stands on the same spot (it overlapped the 458 in seat-door.png)
            AcSeatCar data = AcSeatData.For(car);
            yield return r.Wait(() => data.Ready, 30f, "the seat data (data.acd + knh)");
            r.Check(data.HasEyes && data.HasKnh, "seat data: DRIVEREYES " + data.HasEyes + ", knh " + data.HasKnh + ", STEER_LOCK " + data.SteerLockDeg + ", vr.ini eyes " + data.HasVrEyes +
                                                 (data.Problem != null ? ", problem " + data.Problem : ""));
            yield return r.Frames(30);

            // the stand-in body: root (feet), head, hands
            _root = new GameObject("Lab Player").transform;
            Transform root = _root;
            var head = new GameObject("Lab Head").transform; head.SetParent(root, false); head.localPosition = new Vector3(0f, 1.2f, 0f);
            var left = new GameObject("Lab Left").transform; left.SetParent(root, false); left.localPosition = new Vector3(-0.25f, 0.9f, 0.3f);
            var right = new GameObject("Lab Right").transform; right.SetParent(root, false); right.localPosition = new Vector3(0.25f, 0.9f, 0.3f);
            root.position = at + new Vector3(-3f, -0.6f, 0f);
            AcSeat.LabRoot = root; AcSeat.LabHead = head; AcSeat.LabLeft = left; AcSeat.LabRight = right;
            AcSeat.LabVr = false;
            AcCarCamera.LabCamera = r.Cam;

            // ---- the get-in target: the driver's door on layer 16
            yield return r.Wait(() => AcDoors.BoxOf(_rig) != null && AcDoors.BoxOf(_rig).gameObject.activeSelf, 5f, "the get-in target");
            BoxCollider box = AcDoors.BoxOf(_rig);
            r.Check(box != null && box.gameObject.activeSelf && box.gameObject.layer == AcLayers.Interactable && !box.isTrigger,
                    "get-in target shown, solid, layer 16 (" + AcDoors.Count + " target(s))");
            if (box != null)
            {
                yield return r.Frames(2);
                Vector3 bl = carT.InverseTransformPoint(box.transform.position);
                r.Check(bl.x < -0.3f && box.size.x < 1.3f && box.size.z < 2.6f,
                        "target = the left (driver's) door, not the whole car: centre car-local x " + bl.x.ToString("0.00") + ", size " + box.size.ToString("F2"));
            }

            // ---- monitor: sit in by pressing the target (AcDoorHandle's VR trigger path; the desktop click ends in the same AcDoors.Use)
            AcDoorHandle handle = box != null ? box.GetComponent<AcDoorHandle>() : null;
            if (handle != null) handle.OnActivated(null);
            else AcSeat.Enter(_rig, AcSeatKind.Driver);
            yield return r.Frames(5);
            bool seated = AcSeat.Seated && AcSeat.IsDriver && AcLocal.Driving;
            r.Check(seated, "pressing the target seats the player as driver (seated " + AcSeat.Seated + ", via " + (handle != null ? "AcDoorHandle" : "Enter") + ")");
            r.Check(box == null || !box.gameObject.activeSelf, "the get-in target hides while seated");
            AcSeatGeometry g = AcSeat.Geometry;
            Vector3 seatM = Vector3.zero;
            bool haveSeat = g != null && g.SeatModel(AcSeatKind.Driver, out seatM);
            if (!r.Check(seated && haveSeat, "seat geometry known")) { Cleanup(); yield break; }
            if (_rig.Physics != null) r.Check(_rig.Physics.Occupied && _rig.Physics.Driven, "car occupied + driven (TrySeat: SetOccupied(true) then SetDriven(true))");
            Vector3 seatW = g.ModelToWorld(seatM);
            r.Check((root.position - seatW).magnitude < 0.01f, "monitor body root on the seat (knh RIG_Hips), off by " + (root.position - seatW).magnitude.ToString("0.000") + " m");
            r.Check(seatM.x < 0f, "driver seat on the car's left in Unity model space (LHD " + id + ": x " + seatM.x.ToString("0.000") + ")");

            // the cockpit picture: the eye, looking ahead; the steering wheel must be ahead and below
            Vector3 eyeM;
            bool fromVr;
            g.EyeModel(AcSeatKind.Driver, false, out eyeM, out fromVr);
            Vector3 eye = g.ModelToWorld(eyeM);
            Transform steer = g.SteeringWheel;
            if (steer != null)
            {
                Vector3 s = carT.InverseTransformPoint(steer.position) - carT.InverseTransformPoint(eye);
                r.Check(s.z > 0.3f && s.z < 0.8f && s.y < -0.1f && s.y > -0.45f && Mathf.Abs(s.x) < 0.1f,
                        "steering wheel ahead of the eye at the right height (forward " + s.z.ToString("0.00") + ", down " + (-s.y).ToString("0.00") + ", across " + s.x.ToString("0.00") + " m)");
            }
            else AcLabRunner.Log("no STEER_HR yet (C's model) - steering wheel position not checked");

            AcCarCamera.Set(AcCameraMode.Cockpit);
            yield return r.Frames(3);
            r.Cam.nearClipPlane = AcCarCamera.CockpitNear;
            r.Check((r.Cam.transform.position - eye).magnitude < 0.01f, "monitor cockpit camera at DRIVEREYES (off by " + (r.Cam.transform.position - eye).magnitude.ToString("0.000") + " m)");
            r.Shot("seat-cockpit");
            // F's hub menu panel stands between the chase cameras and the car in the lab room (it hid the car): switched off for these shots
            var hidden = HideMenus();
            // chase near / far: LOOK at both against AC's own F1 views - the PITCH sign / level-view model is a [BELIEF] (CHASE_1 looks 1 deg up)
            AcCarCamera.Set(AcCameraMode.ChaseNear);
            yield return r.Frames(30);
            r.Cam.nearClipPlane = 0.1f;
            r.Shot("seat-chase");
            AcLabRunner.Log("chase near camera at car-local " + carT.InverseTransformPoint(r.Cam.transform.position).ToString("F2") + ", pitch " + r.Cam.transform.eulerAngles.x.ToString("0.0"));
            AcCarCamera.Set(AcCameraMode.ChaseFar);
            yield return r.Frames(30);
            r.Shot("seat-chase-far");
            AcLabRunner.Log("chase far camera at car-local " + carT.InverseTransformPoint(r.Cam.transform.position).ToString("F2") + ", pitch " + r.Cam.transform.eulerAngles.x.ToString("0.0"));
            AcCarCamera.Set(AcCameraMode.Bonnet);
            yield return r.Frames(3);
            r.Shot("seat-bonnet");
            AcLabRunner.Log("bonnet camera at car-local " + carT.InverseTransformPoint(r.Cam.transform.position).ToString("F2") + " (car.ini BONNET_CAMERA_POS - GRAPHICS_OFFSET)");
            ShowMenus(hidden);

            // ---- the car pitched and rolled: the body stays level, heading only
            carT.rotation = Quaternion.Euler(8f, 30f, 5f);
            yield return r.Frames(3);
            float tilt = Vector3.Angle(root.up, Vector3.up);
            float yawErr = Mathf.Abs(Mathf.DeltaAngle(root.eulerAngles.y, g.Yaw));
            r.Check(tilt < 0.01f && yawErr < 0.05f, "monitor root level with horizon lock (tilt " + tilt.ToString("0.000") + " deg, yaw error " + yawErr.ToString("0.000") + ")");

            // ---- VR: the head on the eye, still level
            AcSeat.LabVr = true;
            AcXr.Simulated = true;
            AcXr.SimLeft = default(AcHand); AcXr.SimRight = default(AcHand);
            AcXr.SimRight.Trigger = 1f;     // the trigger that clicked the door is held: the pedals must not drive yet
            yield return r.Frames(3);
            g.EyeModel(AcSeatKind.Driver, true, out eyeM, out fromVr);
            eye = g.ModelToWorld(eyeM);
            r.Check((head.position - eye).magnitude < 0.01f, "VR head on the eye (" + (fromVr ? "vr.ini" : "car.ini") + " DRIVEREYES), off by " + (head.position - eye).magnitude.ToString("0.000") + " m");
            tilt = Vector3.Angle(root.up, Vector3.up);
            r.Check(tilt < 0.01f, "VR root has zero pitch / roll while the car is pitched 8 / rolled 5 (tilt " + tilt.ToString("0.000") + ")");
            if (_rig.Physics != null)
            {
                r.Check(_rig.Physics.Input.Throttle < 0.01f, "pedals disarmed while the trigger is still held from getting in (throttle " + _rig.Physics.Input.Throttle.ToString("0.00") + ")");
                AcXr.SimRight.Trigger = 0f;
                yield return r.Frames(2);
                AcXr.SimRight.Trigger = 1f;
                yield return r.Frames(2);
                r.Check(_rig.Physics.Input.Throttle > 0.99f, "pedals armed after both triggers were released (throttle " + _rig.Physics.Input.Throttle.ToString("0.00") + ")");
                AcXr.SimRight.Trigger = 0f;
                // left X must NOT shift down by default (it is the client's pose cancel); a quick left grip-click away from the rim does
                AcXr.SimLeft.Primary = true;
                yield return r.Frames(1);
                AcXr.SimLeft.Primary = false;
                r.Check(!AcSeatSettings.XShiftsDown && AcDriveInput.Last.ShiftDown == false, "left X does not shift down (setting seat.vrxdown off)");
            }

            // ---- VR wheel grab: right hand at 3 o'clock, squeeze, move 30 degrees clockwise
            carT.rotation = Quaternion.identity;
            yield return r.Frames(3);
            Vector3 hub;
            Quaternion rest;
            if (g.WheelRestFrame(out hub, out rest))
            {
                yield return r.Frames(2);
                float rim = AcWheelGrab.RimRadius;
                r.Check(rim > 0.1f && rim < 0.4f, "rim radius measured " + rim.ToString("0.000") + " m");
                right.position = hub + rest * new Vector3(rim, 0f, 0f);
                AcXr.SimRight.Grip = 1f;
                yield return r.Frames(3);
                r.Check((AcWheelGrab.Holding & 2) != 0, "right hand holds the rim");
                for (int i = 1; i <= 10; i++)
                {
                    float a = (90f + 3f * i) * Mathf.Deg2Rad;
                    right.position = hub + rest * new Vector3(rim * Mathf.Sin(a), rim * Mathf.Cos(a), 0f);
                    yield return r.Frames(1);
                }

                yield return r.Frames(2);
                r.Check(Mathf.Abs(AcWheelGrab.WheelDeg - 30f) < 2f, "hand moved 30 deg clockwise -> wheel +30 deg (right), got " + AcWheelGrab.WheelDeg.ToString("0.0"));
                if (_rig.Physics != null) r.Check(_rig.Physics.Input.SteerAbsolute && Mathf.Abs(_rig.Physics.Input.SteerWheelDeg - AcWheelGrab.WheelDeg) < 0.5f, "Input.SteerAbsolute with SteerWheelDeg " + _rig.Physics.Input.SteerWheelDeg.ToString("0.0"));
                if (_rig.Model != null && steer != null) CheckVisibleSign(r, carT, steer);
                r.Aim(eye, steer != null ? steer.position : eye + carT.forward, 56f);
                r.Cam.nearClipPlane = AcCarCamera.CockpitNear;
                r.Shot("seat-wheel-30");
                AcXr.SimRight.Grip = 0f;
                yield return r.Frames(2);
                r.Check(AcWheelGrab.Holding == 0, "grip released -> wheel let go");
            }
            else AcLabRunner.Log("no steering wheel frame (C's model not built) - wheel grab not checked");

            // ---- out: the door opens, the body stands outside the driver's door
            AcSeat.LabVr = false;
            AcXr.Simulated = false;
            string why;
            r.Check(AcSeat.CanExit(out why), "CanExit when stopped in the hub (" + (why ?? "ok") + ")");
            r.Check(AcSeat.Exit(), "Exit() when stopped");
            yield return r.Wait(() => AcSeat.DoorOpen > 0.3f, 1.5f, "the door opening");
            r.Check(AcSeat.DoorOpen > 0.3f || _rig.Model == null || _rig.Model.DoorL == null, "door opening (" + AcSeat.DoorOpen.ToString("0.00") + ")");
            Vector3 local = carT.InverseTransformPoint(root.position);
            r.Check(!AcSeat.Seated && local.x < -1.0f, "stood outside the driver's (left) door: car-local x " + local.x.ToString("0.00") + " m");
            if (_rig.Physics != null) r.Check(!_rig.Physics.Driven, "the car is parked again (SetDriven(false))");
            r.Aim(carT.position + carT.rotation * new Vector3(-4.5f, 1.6f, 1.5f), carT.position + Vector3.up * 0.6f, 50f);
            r.Cam.nearClipPlane = 0.1f;
            r.Shot("seat-door");
            yield return r.Frames(90);

            // ---- a passenger in another player's car (a Remote rig; no net layer -> AcRiders.GrantLocally)
            _rig2 = null;
            var opt2 = new AcCarRigOptions { Kind = AcCarKind.Remote, Audio = false };
            yield return AcCarRig.Spawn(car, car.Skins.Count > 1 ? car.Skins[1].Id : (car.Skins.Count > 0 ? car.Skins[0].Id : ""), opt2, Got2);
            yield return r.Wait(() => _rig2 != null, 120f, "the second (remote) car rig");
            if (_rig2 != null && _rig2.Root != null)
            {
                Transform car2 = _rig2.Root.transform;
                car2.SetPositionAndRotation(at + new Vector3(6f, 0f, 0f), Quaternion.identity);
                AcRiders.GrantLocally = true;
                yield return r.Frames(5);
                bool asked = AcSeat.Enter(_rig2, AcSeatKind.Passenger);
                yield return r.Frames(5);
                bool pass = asked && AcSeat.Seated && AcSeat.Kind == AcSeatKind.Passenger && ReferenceEquals(AcSeat.SeatedCar, _rig2) && !AcLocal.Driving;
                r.Check(pass, "passenger seat in a remote car granted locally (seated " + AcSeat.Seated + ", kind " + AcSeat.Kind + ")");
                AcSeatGeometry g2 = AcSeat.Geometry;
                Vector3 p2;
                if (pass && g2 != null && g2.SeatModel(AcSeatKind.Passenger, out p2))
                {
                    r.Check(p2.x > 0f && (root.position - g2.ModelToWorld(p2)).magnitude < 0.01f, "passenger body root on the right-hand seat (model x " + p2.x.ToString("0.000") + ")");
                    r.Check(_rig2.Physics == null || (_rig2.Physics.Occupied && !_rig2.Physics.Driven), "remote car occupied, not driven by the passenger");
                    r.Check(AcSeat.Exit(), "passenger Exit() when stopped");
                    yield return r.Frames(3);
                    Vector3 l2 = car2.InverseTransformPoint(root.position);
                    r.Check(!AcSeat.Seated && l2.x > 1.0f, "passenger stood outside the right door: car-local x " + l2.x.ToString("0.00") + " m");
                }

                AcRiders.GrantLocally = false;
            }
            else AcLabRunner.Log("no second rig - passenger not checked");

            // ---- ForceExit (session unload / R before disposing): releases at once, parks the car
            yield return r.Frames(70);      // the 1 s "solid again" of the first exit
            AcSeat.Enter(_rig, AcSeatKind.Driver);
            yield return r.Frames(5);
            r.Check(AcSeat.Seated, "back in as driver for the force-exit test");
            AcSeat.ForceExit();
            yield return r.Frames(2);
            r.Check(!AcSeat.Seated && !AcLocal.Seated && AcLocal.SeatCar == null && (_rig.Physics == null || !_rig.Physics.Driven), "ForceExit releases the player and parks the car");

            // ---- the car disposed under a seated player (ETS Vanished): released, landed beside where it was
            AcSeat.Enter(_rig, AcSeatKind.Driver);
            yield return r.Frames(5);
            bool inAgain = AcSeat.Seated;
            Vector3 carPos = carT.position;
            _rig.Dispose();
            yield return r.Frames(5);
            r.Check(inAgain && !AcSeat.Seated && AcLocal.SeatCar == null, "car disposed under the seated player -> released (seated " + AcSeat.Seated + ")");
            // landing needs ground under the door (the hub floor); without it the player stays put - logged, not failed
            float landBy = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < landBy && (root.position - carPos).magnitude < 1f) yield return null;
            AcLabRunner.Log("after the car vanished the body stands " + (root.position - carPos).magnitude.ToString("0.00") + " m from where the car was (> 1 m = landed beside it)");
            _rig = null;
            Cleanup();
        }

        /// <summary>The visible sign, independent of the angle convention: C's STEER_HR at 0 and at +30 deg (same frame, no yield) - the rim
        /// mesh's own top vertex must move to the driver's right (car +x in Unity).</summary>
        private static void CheckVisibleSign(AcLabRunner r, Transform carT, Transform steer)
        {
            MeshFilter[] mfs = steer.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter best = null;
            int bestV = -1;
            float bestY = float.MinValue;
            _rig.Model.SetSteer(0f);
            for (int k = 0; k < mfs.Length; k++)
            {
                Mesh m = mfs[k].sharedMesh;
                if (m == null || !m.isReadable) continue;
                Vector3[] v = m.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    float y = carT.InverseTransformPoint(mfs[k].transform.TransformPoint(v[i])).y;
                    if (y > bestY) { bestY = y; best = mfs[k]; bestV = i; }
                }
            }

            if (best == null) { AcLabRunner.Log("no readable STEER_HR mesh - visible steering sign not checked"); return; }
            Vector3 vert = best.sharedMesh.vertices[bestV];
            float x0 = carT.InverseTransformPoint(best.transform.TransformPoint(vert)).x;
            _rig.Model.SetSteer(30f);
            float x30 = carT.InverseTransformPoint(best.transform.TransformPoint(vert)).x;
            r.Check(x30 - x0 > 0.02f, "STEER_HR at +30 deg: the rim's top vertex moves to the driver's right by " + ((x30 - x0) * 100f).ToString("0.0") + " cm (visible sign; LOOK at seat-wheel-30.png)");
        }

        private static void Cleanup()
        {
            AcSeat.ForceExit();
            AcRiders.GrantLocally = false;
            AcSeat.LabRoot = AcSeat.LabHead = AcSeat.LabLeft = AcSeat.LabRight = null;
            AcSeat.LabVr = false;
            AcXr.Simulated = false;
            AcCarCamera.LabCamera = null;
            if (_rig2 != null) { _rig2.Dispose(); _rig2 = null; }
            if (_rig != null) { _rig.Dispose(); _rig = null; }
            AcDoors.LabCar = null;
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }

        private static void Got(AcCarRig rig) { _rig = rig; }
        private static void Got2(AcCarRig rig) { _rig2 = rig; }

        /// <summary>Switches off the canvases of F's non-tablet menus (lab pictures only); returns them for <see cref="ShowMenus"/>.</summary>
        private static System.Collections.Generic.List<Canvas> HideMenus()
        {
            var list = new System.Collections.Generic.List<Canvas>();
            try
            {
                foreach (AcMenu m in AcFront.AllMenus)
                {
                    if (m == null || m.Root == null || m.Tablet) continue;
                    Canvas c = m.Root.GetComponentInParent<Canvas>();
                    if (c != null) c = c.rootCanvas;
                    if (c != null && c.enabled && c.renderMode == RenderMode.WorldSpace) { c.enabled = false; list.Add(c); }
                }
            }
            catch (System.Exception e) { AcLabRunner.Log("menus not hidden: " + e.Message); }
            return list;
        }

        /// <summary>Switches off F's hub showroom car (lab pictures only; the lab ends with the play mode).</summary>
        internal static void HideHubCar()
        {
            try
            {
                AcHub hub = AcHub.Current;
                if (hub != null && hub.Car != null && hub.Car.Root != null) { hub.Car.Root.SetActive(false); AcLabRunner.Log("hub showroom car hidden for the pictures"); }
            }
            catch (System.Exception e) { AcLabRunner.Log("hub car not hidden: " + e.Message); }
        }

        private static void ShowMenus(System.Collections.Generic.List<Canvas> list)
        {
            foreach (Canvas c in list) if (c != null) c.enabled = true;
        }
    }
}
