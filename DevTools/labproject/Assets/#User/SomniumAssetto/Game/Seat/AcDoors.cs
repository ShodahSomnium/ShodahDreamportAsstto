using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Getting in: click the car (desktop), point at it and pull the trigger or squeeze the grip (VR), or press E standing next to it
    /// - the Euro Truck Sim Dreamport's client-proven EtsTruckDoor. What the client's pointers can hit decides the shape (client source, ETS
    /// doc): desktop clicks are UI pointer events through the client's EventSystem + PhysicsRaycaster on layers 0,1,4,5,14,16; the VR
    /// Near-Far interactor's far ray hits 0,5,6,10,14,16,23 at 5 m (triggers skipped, interaction layer 4 'XRInteractable'). Neither sees the
    /// car layer 28 and 0 / 14 are the ground, so each car gets a target box around the DOOR of the seat on offer, on layer 16 (<see cref="AcLayers.Interactable"/>): SOLID
    /// (not a trigger), touching nothing (excludeLayers = all, no contacts), on its own kinematic body that follows the car each frame - never
    /// a child of the car's Rigidbody (a collider there joins the compound body: ETS "as soon as I enter the truck it spins").
    /// Targets: the own car while parked (driver seat; the door on the seat's side opens), and every remote car with a free passenger seat
    /// (asks its owner through N - AcRiders). Shown only to a player on foot, never while the tablet is up.
    /// </summary>
    public static class AcDoors
    {
        public const float ClickRange = 12f;   // ours: no getting in from across the paddock
        public const float KeyRange = 3f;      // ours: E within this of the car's box
        public const float ParkedKmh = 3f;     // ours
        private const int InteractionLayer = 1 << 4;   // XRI interaction layer 4 'XRInteractable' (the client's Near-Far interactor)

        private sealed class Target
        {
            public AcCarRig Car;
            public AcSeatKind Seat;
            public GameObject Go;
            public BoxCollider Box;
            public Vector3 Centre;
            public AcSeatGeometry Geo;
            public bool Sized;
            public bool Seen;
        }

        private static readonly List<Target> Targets = new List<Target>();
        private static Transform _root;
        private static float _nextScan;
        private static bool _saidUsable, _hinted;

        /// <summary>The targets shown now (lab).</summary>
        public static int Count { get { int n = 0; for (int i = 0; i < Targets.Count; i++) if (Targets[i].Go != null && Targets[i].Go.activeSelf) n++; return n; } }

        /// <summary>Lab: the "own car" when R runs no session (AcLocal.Car is R's / P's field).</summary>
        public static AcCarRig LabCar;

        /// <summary>The get-in box of a car (null when it has none) - the lab presses it through its <see cref="AcDoorHandle"/>.</summary>
        public static BoxCollider BoxOf(AcCarRig car)
        {
            for (int i = 0; i < Targets.Count; i++) if (ReferenceEquals(Targets[i].Car, car)) return Targets[i].Box;
            return null;
        }

        /// <summary>On foot, once a frame (seat's early frame).</summary>
        internal static void Tick()
        {
            if (Time.unscaledTime >= _nextScan) Scan();
            bool canUse = CanUseNow();
            Vector3 feet;
            bool haveFeet = Feet(out feet);
            for (int i = 0; i < Targets.Count; i++)
            {
                Target t = Targets[i];
                if (t.Car == null || t.Car.Root == null || t.Go == null) continue;
                bool usable = canUse && Usable(t);
                Follow(t);
                if (t.Go.activeSelf != usable) t.Go.SetActive(usable);
                if (usable && !_saidUsable)
                {
                    _saidUsable = true;
                    AcLog.Info("Seat: a car can be got into: " + (AcSeat.Vr ? "point at it and pull the trigger (or squeeze the grip)." : "click it or press E next to it."));
                }

                if (usable && !_hinted && haveFeet && t.Car.Kind == AcCarKind.Local && (t.Box.transform.position - feet).sqrMagnitude < 15f * 15f)
                {
                    _hinted = true;
                    AcEvents.RaiseMessage(AcSeat.Vr ? "Point at your car and pull the trigger to get in" : "Click your car or press E to get in", 4f);
                }
            }

            if (!canUse) return;
            try
            {
                if (!AcSeat.Vr && Input.GetKeyDown(KeyCode.E) && haveFeet) KeyNear(feet);
                if (!AcSeat.Vr && Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())) LegacyClick(feet, haveFeet);
            }
            catch (Exception) { /* legacy Input missing: pointer events still work */ }
        }

        private static bool CanUseNow()
        {
            if (AcSeat.Seated || AcSeat.Pending || AcDriveInput.TabletUp || AcLocal.TabletOpen) return false;
            return AcSeat.LabRoot != null || AcPlayer.HasBridgePlayer || Application.isEditor;
        }

        private static bool Usable(Target t)
        {
            AcCarRig car = t.Car;
            if (car.Kind == AcCarKind.Local)
            {
                if (car.Physics != null && car.Physics.Driven) return false;
                return car.Physics == null || Mathf.Abs(car.Physics.SpeedKmh) < ParkedKmh;
            }

            return AcRiders.PassengerSeatFree(car) && (car.Physics == null || Mathf.Abs(car.Physics.SpeedKmh) < AcSeat.PassengerExitKmh);
        }

        private static bool Feet(out Vector3 feet)
        {
            if (AcSeat.LabRoot != null) { feet = AcSeat.LabRoot.position; return true; }
            return AcPlayer.TryFeet(out feet);
        }

        /// <summary>Hides every target (the player sat down).</summary>
        internal static void HideAll()
        {
            for (int i = 0; i < Targets.Count; i++) if (Targets[i].Go != null && Targets[i].Go.activeSelf) Targets[i].Go.SetActive(false);
        }

        /// <summary>Destroys every target (module stop).</summary>
        internal static void Clear()
        {
            for (int i = 0; i < Targets.Count; i++) if (Targets[i].Go != null) UnityEngine.Object.Destroy(Targets[i].Go);
            Targets.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }

        /// <summary>Which cars get a target (twice a second): the own car, and remote cars (passenger seat).</summary>
        private static void Scan()
        {
            _nextScan = Time.unscaledTime + 0.5f;
            for (int i = 0; i < Targets.Count; i++) Targets[i].Seen = false;
            AcCarRig own = LabCar != null ? LabCar : AcLocal.Car;
            if (own != null && own.Root != null && own.Kind == AcCarKind.Local) Want(own, AcSeatKind.Driver);
            try
            {
                IReadOnlyList<AcRemoteCar> remotes = AcNetGame.RemoteCars;
                for (int i = 0; remotes != null && i < remotes.Count; i++)
                {
                    AcRemoteCar rc = remotes[i];
                    if (rc != null && rc.Rig != null && rc.Rig.Root != null && rc.Rig.Root.activeInHierarchy) Want(rc.Rig, AcSeatKind.Passenger);
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Seat: reading N's remote cars failed: " + e.Message); }

            for (int i = Targets.Count - 1; i >= 0; i--)
            {
                Target t = Targets[i];
                if (t.Seen && t.Car != null && t.Car.Root != null) continue;
                if (t.Go != null) UnityEngine.Object.Destroy(t.Go);
                Targets.RemoveAt(i);
            }
        }

        private static void Want(AcCarRig car, AcSeatKind seat)
        {
            for (int i = 0; i < Targets.Count; i++)
                if (ReferenceEquals(Targets[i].Car, car)) { Targets[i].Seen = true; Targets[i].Seat = seat; if (!Targets[i].Sized) Size(Targets[i]); return; }
            Target t = Build(car, seat);
            if (t != null) { t.Seen = true; Targets.Add(t); }
        }

        private static Target Build(AcCarRig car, AcSeatKind seat)
        {
            if (_root == null)
            {
                var r = new GameObject("Ac Seat Targets");
                _root = r.transform;
                if (AcGame.Current != null) _root.SetParent(AcGame.Current.transform, false);
            }

            var go = new GameObject("Ac get in (" + (car.Car != null ? car.Car.Id : "car") + ")");
            go.SetActive(false);
            go.layer = AcLayers.Interactable;
            go.transform.SetParent(_root, false);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.excludeLayers = ~0;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.excludeLayers = ~0;
            box.providesContacts = false;
            var t = new Target { Car = car, Seat = seat, Go = go, Box = box, Geo = AcSeatGeometry.Make(car) };
            Size(t);
            // VR: after the collider (the interactable collects its colliders when it wakes)
            var interactable = go.AddComponent<XRSimpleInteractable>();
            interactable.interactionLayers = InteractionLayer;
            var handle = go.AddComponent<AcDoorHandle>();
            handle.Target = car;
            interactable.activated.AddListener(handle.OnActivated);
            interactable.selectEntered.AddListener(handle.OnSelected);
            return t;
        }

        /// <summary>The box: the DOOR on the seat's side (brief: a target per side) - from the seat line out past the door skin (C's outer
        /// handle point, else the model bounds), from behind the door handle (the door's rear end) to 0.85 m ahead of the hips, sill to roof.
        /// Small on purpose: whole-car boxes on layer 16 around every remote car caught the VR teleport ray and stray desktop clicks. Until C's
        /// bounds and the seat point are known: the whole car (1.9 x 1.3 x 4.4 m // ours fallback), re-sized on the next scan.</summary>
        private static void Size(Target t)
        {
            AcCarModel m = t.Car.Model;
            bool haveModel = m != null && m.Bounds.size.x > 0.5f;
            Bounds b = haveModel ? m.Bounds : new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(1.9f, 1.3f, 4.4f)); // ours fallback
            float bottom = Mathf.Min(b.min.y, 0.3f), top = Mathf.Max(b.max.y, bottom + 0.6f);
            Vector3 sm;
            if (!haveModel || !t.Geo.SeatModel(t.Seat, out sm))
            {
                t.Sized = false;
                t.Box.center = Vector3.zero;
                t.Box.size = new Vector3(b.size.x, top - bottom, b.size.z);
                t.Centre = new Vector3(b.center.x, (top + bottom) * 0.5f, b.center.z);
                return;
            }

            AcSide side = t.Geo.SideOf(t.Seat);
            float sx = side == AcSide.Left ? -1f : 1f;
            float inner = Mathf.Max(0f, Mathf.Abs(sm.x) - 0.1f);                                    // ours: a little inside the seat line
            float outer = Mathf.Max(t.Geo.DoorSkinX(t.Seat), Mathf.Abs(sm.x) + 0.3f) + 0.15f;         // ours: 15 cm proud of the skin
            Vector3 handle;
            bool haveHandle = t.Geo.DoorHandleModel(side, out handle);
            float zMin = (haveHandle ? Mathf.Min(handle.z, sm.z - 0.3f) : sm.z - 0.55f) - 0.1f;      // ours: door rear end (C's handle) - 10 cm
            float zMax = sm.z + 0.85f;                                                               // ours: the A-pillar region
            zMin = Mathf.Max(zMin, b.min.z);
            zMax = Mathf.Min(zMax, b.max.z);
            if (zMax - zMin < 0.6f) { float zc = (zMin + zMax) * 0.5f; zMin = zc - 0.3f; zMax = zc + 0.3f; }
            t.Sized = true;
            t.Box.center = Vector3.zero;
            t.Box.size = new Vector3(outer - inner, top - bottom, zMax - zMin);
            t.Centre = new Vector3(sx * (inner + outer) * 0.5f, (top + bottom) * 0.5f, (zMin + zMax) * 0.5f);
            if (!_saidSize)
            {
                _saidSize = true;
                AcLog.Info("Seat: get-in target = the " + (side == AcSide.Left ? "left" : "right") + " door of " + (t.Car.Car != null ? t.Car.Car.Id : "the car") + " (" +
                           t.Box.size.x.ToString("0.00") + " x " + t.Box.size.y.ToString("0.00") + " x " + t.Box.size.z.ToString("0.00") + " m, " + (haveHandle ? "C's door handle" : "no door animation: seat side") + ").");
            }
        }

        private static bool _saidSize;

        private static void Follow(Target t)
        {
            AcSeatGeometry g = t.Geo;
            t.Go.transform.SetPositionAndRotation(g.ModelToWorld(t.Centre), t.Car.Root.transform.rotation);
        }

        /// <summary>Every way of pressing a car ends here.</summary>
        internal static void Use(AcCarRig car, string how)
        {
            if (car == null || !CanUseNow()) return;
            Target t = null;
            for (int i = 0; i < Targets.Count; i++) if (ReferenceEquals(Targets[i].Car, car)) t = Targets[i];
            if (t == null || !Usable(t)) return;
            AcLog.InfoOnce("Seat: first get-in by " + how + ".");
            t.Go.SetActive(false);
            AcSeat.Enter(car, t.Seat);
        }

        private static void KeyNear(Vector3 feet)
        {
            Target best = null;
            float bestD = KeyRange;
            for (int i = 0; i < Targets.Count; i++)
            {
                Target t = Targets[i];
                if (t.Go == null || !t.Go.activeSelf) continue;
                Vector3 p = t.Box.ClosestPoint(feet + Vector3.up * 1f);
                float d = Vector3.Distance(p, feet + Vector3.up * 1f);
                if (d < bestD) { bestD = d; best = t; }
            }

            if (best != null) Use(best.Car, "the E key");
        }

        /// <summary>ETS belt and braces: a plain left click on a target still counts if the client's pointer event never arrives.</summary>
        private static void LegacyClick(Vector3 feet, bool haveFeet)
        {
            Camera camera = AcPlayer.Head();
            if (camera == null) return;
            int seen = (1 << AcLayers.Interactable) | AcLayers.GroundMask | (1 << AcLayers.Environment);
            RaycastHit hit;
            if (!Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out hit, 60f, seen, QueryTriggerInteraction.Ignore)) return;
            for (int i = 0; i < Targets.Count; i++)
            {
                Target t = Targets[i];
                if (t.Box != hit.collider) continue;
                if (haveFeet && Vector3.Distance(t.Box.ClosestPoint(feet), feet) > ClickRange) { AcSeat.Refuse("walk up to the car to get in", 2f); return; }
                Use(t.Car, "a click on the car");
                return;
            }
        }
    }

    /// <summary>[S] Takes the desktop click (client EventSystem) and the VR trigger / grip on a car's target box (see <see cref="AcDoors"/>).</summary>
    public sealed class AcDoorHandle : MonoBehaviour, IPointerClickHandler
    {
        public AcCarRig Target;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left) Check("a click on the car");
        }

        public void OnActivated(ActivateEventArgs args) { Check("the trigger on the car"); }
        public void OnSelected(SelectEnterEventArgs args) { Check("the grip on the car"); }

        private void Check(string how)
        {
            Vector3 feet;
            if (AcSeat.LabRoot == null && AcPlayer.TryFeet(out feet))
            {
                BoxCollider box = GetComponent<BoxCollider>();
                if (box != null && Vector3.Distance(box.ClosestPoint(feet), feet) > AcDoors.ClickRange) { AcSeat.Refuse("walk up to the car to get in", 2f); return; }
            }

            AcDoors.Use(Target, how);
        }
    }
}
