using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The campaign side of a vehicle (phase 2): scenario placements with their name (HaloObjects), vitality from the collision model
    /// (campaign banshees 100, ghosts 160 body points; the MP tags have none), AI / scripted units in seats (the Characters module's
    /// HaloUnit.EnterSeat parents the unit under the seat marker - such a seat counts as taken), the AI module's driving surface
    /// (<see cref="SetAiInput"/>), and the script's vehicle switches (enterable by the player, hover, doors).
    /// </summary>
    public sealed partial class HaloVehicle
    {
        /// <summary>A seat a HaloUnit sits in (SeatInfo.Occupant): units are not net slots.</summary>
        public const int UnitOccupant = 1 << 20;

        /// <summary>Placed for a campaign mission: no MP abandoned-respawn, the tag's vitality applies.</summary>
        public bool Campaign;
        /// <summary>The scenario object name ("" = unnamed).</summary>
        public string ObjectName = "";
        /// <summary>Body vitality left (fraction; campaign vehicles whose collision model has body vitality).</summary>
        public float Vitality = 1f;
        public bool Destroyed { get; private set; }
        /// <summary>unit_set_enterable_by_player: false = the player cannot get in.</summary>
        public bool EnterableByPlayer = true;
        /// <summary>vehicle_hover: held still in the air where it is.</summary>
        public bool Hovering { get; private set; }

        public static event Action<HaloVehicle> VehicleDestroyed;

        // ------------------------------------------------------------------ units in seats

        /// <summary>The HaloUnit sitting on a seat (its GameObject under the seat marker), or null.</summary>
        public HaloUnit UnitIn(int seat)
        {
            var s = SeatOf(seat);
            if (s == null || s.Marker == null || s.Marker.childCount == 0) return null;
            var u = s.Marker.GetComponentInChildren<HaloUnit>();
            return u != null && u.Alive ? u : null;
        }

        /// <summary>The seat a unit sits in here (-1 none).</summary>
        public int SeatOfUnit(HaloUnit u)
        {
            if (u == null || u.Seat == null) return -1;
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Marker == u.Seat) return i;
            return -1;
        }

        /// <summary>Seats a unit (vehicle_load_magic / unit_enter_vehicle / AI boarding): the Characters module's EnterSeat on the seat marker.
        /// False when the seat is taken.</summary>
        public bool SeatUnit(int seat, HaloUnit u)
        {
            var s = SeatOf(seat);
            if (s == null || u == null || s.Marker == null) return false;
            if (s.Occupant >= 0 && s.Unit != u) return false;
            u.EnterSeat(s.Marker, s.Label);
            s.Occupant = UnitOccupant; s.Unit = u;
            LastUsed = Time.time;
            return true;
        }

        /// <summary>Gets a unit out at the seat's exit point (Halo's enter marker / the exit clip's end).</summary>
        public void UnseatUnit(HaloUnit u)
        {
            int seat = SeatOfUnit(u);
            if (seat < 0) return;
            Vector3 at; float yaw;
            HaloVehicleSeat.StandPoint(this, Seats[seat], out at, out yaw);
            u.ExitSeat(at, yaw);
            Seats[seat].Occupant = -1; Seats[seat].Unit = null;
        }

        /// <summary>Marks the seats units sit in as taken (every frame, after the net reset of the occupants).</summary>
        internal void NoteUnits()
        {
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                s.Unit = null;
                if (s.Occupant >= 0 || s.Marker == null || s.Marker.childCount == 0) continue;
                var u = s.Marker.GetComponentInChildren<HaloUnit>();
                if (u != null && u.Alive) { s.Occupant = UnitOccupant; s.Unit = u; }
            }
        }

        // ------------------------------------------------------------------ the AI's driving surface

        private float _aiAt = -10f, _aiThrottle, _aiTurn;
        private Vector3 _aiAim;
        private bool _aiFire, _aiFire2, _aiDriving, _saidAi;

        /// <summary>An AI (or script) drives / fires this tick.</summary>
        public bool AiDriven => _aiDriving;

        /// <summary>
        /// The AI module's control of this vehicle for this tick (call every AI tick; the input lapses 0.5 s after the last call):
        /// throttle -1..1 (back / forward), turn -1..1 (left / right; <see cref="SteerToward"/> gives it for a point), aimPoint = where the gun
        /// should point (world; zero = straight ahead), fire / fire2 = the vehicle weapon's triggers. The driver seat must hold a HaloUnit for
        /// the throttle / turn to drive; a unit in the gunner seat (or a driver of a ghost / banshee / scorpion) fires. Runs on the host (the
        /// AI's client): the vehicle is simulated there and published like a player's.
        /// </summary>
        public void SetAiInput(float throttle, float turn, Vector3 aimPoint, bool fire, bool fire2 = false)
        {
            _aiAt = Time.time;
            _aiThrottle = Mathf.Clamp(throttle, -1f, 1f); _aiTurn = Mathf.Clamp(turn, -1f, 1f);
            _aiAim = aimPoint; _aiFire = fire; _aiFire2 = fire2;
        }

        /// <summary>The turn (-1 left .. 1 right) that heads the vehicle at a world point (full lock beyond the vehicle's maximum turn).</summary>
        public float SteerToward(Vector3 point)
        {
            Vector3 to = point - transform.position;
            float a = SignedYaw(transform.forward, to);
            float full = Def.Type == HaloVehicleDef.Kind.HumanJeep ? Mathf.Max(5f, Def.MaxLeftTurn) : 30f;
            return Mathf.Clamp(a / full, -1f, 1f);
        }

        /// <summary>The vehicle's own frame for the AI each frame (the director calls it); the local player's driving takes precedence.</summary>
        private Vector3 _aiFlyTo;
        private bool _aiFlight;

        /// <summary>
        /// 3D flight for an AI pilot (banshee; any flier): throttle -1..1 along the nose, <paramref name="flyToward"/> = the world point the nose
        /// turns to (climbs and dives included: the banshee pitches up to 60 deg toward it at the seat's yaw rate, its own flight numbers -
        /// top speed 0.22 wu/tick = 20 m/s, acceleration 0.008 wu/tick^2 - from the vehicle tag), aimPoint / fire / fire2 as SetAiInput (the
        /// banshee's guns are fixed: they fire along the nose). Same lapse rule (0.5 s).
        /// </summary>
        public void SetAiFlight(float throttle, Vector3 flyToward, Vector3 aimPoint, bool fire, bool fire2 = false)
        {
            SetAiInput(throttle, 0f, aimPoint, fire, fire2);
            _aiFlyTo = flyToward; _aiFlight = true;
        }

        internal void AiStep(float dt)
        {
            bool fresh = Time.time - _aiAt < 0.5f;
            if (HaloCinematic.IsRecording(gameObject)) { _aiDriving = false; return; }      // a recording flies it (Halo: the recording replaces the controls)
            if (!fresh) _aiFlight = false;
            if (LocalSeated && HaloVehicleSeat.Local.Driver && HaloVehicleSeat.Local.Vehicle == this) { _aiDriving = false; return; }
            var drv = DriverSeat;
            bool unitDrives = drv != null && drv.Unit != null;
            if (!fresh)
            {
                if (_aiDriving)
                {
                    _aiDriving = false;
                    Controls = default(HaloVehicleControls);
                    if (!unitDrives && !LocalSeated) Park(true);
                }
                return;
            }
            if (!_aiDriving && !_saidAi) { _saidAi = true; HaloLog.Info("Vehicles: " + name + " is driven / manned by AI (" + (unitDrives ? drv.Unit.name + " drives" : "no driver") + ")."); }
            _aiDriving = true;
            if (unitDrives && !RemoteDriven && Def.Type != HaloVehicleDef.Kind.Turret)
            {
                if (!LocalDriven) TakeControl();
                if (_aiFlight && (_aiFlyTo - transform.position).sqrMagnitude > 1f)
                    Controls = new HaloVehicleControls { Throttle = _aiThrottle, Aim = (_aiFlyTo - transform.position).normalized, SteerToAim = true };
                else Controls = new HaloVehicleControls { Throttle = _aiThrottle, Turn = _aiTurn, Aim = transform.forward };
            }
            if (Gun == null) return;
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                if (!s.Guns || s.Unit == null) continue;
                if (HaloVehicleInput.FixedGuns(this)) { AimYaw = 0f; AimPitch = 0f; }
                else if (_aiAim.sqrMagnitude > 0.01f) { Vector3 d = _aiAim - GunPivot.position; if (d.sqrMagnitude > 0.25f) AimToward(d, s, dt); }
                Gun.Tick(_aiFire, _aiFire2, dt, s.Label, -2);
                break;
            }
        }

        // ------------------------------------------------------------------ script switches

        /// <summary>vehicle_hover: true = held still where it is (kinematic, no gravity); false = back to its physics.</summary>
        public void Hover(bool on)
        {
            if (Hovering == on) return;
            Hovering = on;
            if (Body == null) return;
            if (on) { if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; } Body.isKinematic = true; }
            else if (LocalDriven) Body.isKinematic = false;
            else Park(true);
        }

        // ------------------------------------------------------------------ unit_open / unit_close (cryotube lid, pelican / dropship doors)

        /// <summary>The unit seat animation slots of Halo's "stand" block for a unit's own model: 27 opening, 28 closing, 29 hovering
        /// (the a10 cryotube's "stand opening" 154 frames; the dropship's "stand opening" / "stand closing" 14 frames, "stand hovering").</summary>
        private const int AnimOpening = 27, AnimClosing = 28;

        private HaloAnimGraph _ownGraph;
        private bool _ownGraphRead;
        private HaloAnimation _doorClip;
        private bool _doorReverse, _doorPlaying;
        private float _doorT;
        private Transform[] _doorNodes;
        private Vector3[] _doorRestPos;
        private Quaternion[] _doorRestRot;
        private static bool _saidDoors;

        /// <summary>Opened by unit_open (false before / after unit_close). A unit with an opening animation lets nobody out while closed.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>The vehicle's own graph has an opening animation (cryotube, dropship): its seats can only be left while open.</summary>
        public bool HasHatch => OwnSeatAnim(AnimOpening) != null;

        private HaloAnimation OwnSeatAnim(int id)
        {
            if (!_ownGraphRead)
            {
                _ownGraphRead = true;
                try { if (Def.Animations != null) _ownGraph = new HaloAnimGraph(Def.Animations); }
                catch (Exception e) { HaloLog.Warn("Vehicles: animation graph of " + Def.Name + ": " + e.Message); }
            }
            if (_ownGraph == null) return null;
            var stand = _ownGraph.FindSeat("stand");
            var a = stand != null ? _ownGraph.Get(stand.Anim(id)) : null;
            return a != null && a.Decode() ? a : null;
        }

        /// <summary>
        /// unit_open / unit_close: Halo's unit opening / closing animation of the vehicle's own graph ("stand opening", "stand closing"; a
        /// unit with only an opening plays it backwards to close), on the model's nodes over the clip's own length (30 frames a second), held
        /// on its last frame. Pelicans have no such animation in their graph (nothing to play: logged once).
        /// </summary>
        public void Open(bool open)
        {
            if (Model == null) return;
            HaloAnimation clip = open ? OwnSeatAnim(AnimOpening) : (OwnSeatAnim(AnimClosing) ?? OwnSeatAnim(AnimOpening));
            bool reverse = !open && OwnSeatAnim(AnimClosing) == null;
            IsOpen = open;
            if (clip == null)
            {
                if (!_saidDoors) { _saidDoors = true; HaloLog.Info("Vehicles: unit_" + (open ? "open" : "close") + " on " + Def.Name + ": its animation graph has no opening / closing animation - nothing to move."); }
                return;
            }
            if (_doorNodes == null || _doorClip == null || _doorClip.NodeCount != clip.NodeCount) BindDoor(clip);
            _doorClip = clip; _doorReverse = reverse; _doorT = 0f; _doorPlaying = true;
            HaloLog.Info("Vehicles: " + name + " " + (open ? "opens" : "closes") + ": '" + clip.Name + "'" + (reverse ? " backwards" : "") + " over " + clip.Duration.ToString("0.00") + " s.");
        }

        private void BindDoor(HaloAnimation clip)
        {
            int n = _ownGraph.NodeNames.Length;
            _doorNodes = new Transform[n]; _doorRestPos = new Vector3[n]; _doorRestRot = new Quaternion[n];
            for (int k = 0; k < n; k++)
            {
                _doorNodes[k] = Model.Node(_ownGraph.NodeNames[k]);
                if (_doorNodes[k] != null) { _doorRestPos[k] = _doorNodes[k].localPosition; _doorRestRot[k] = _doorNodes[k].localRotation; }
            }
        }

        /// <summary>Every frame (LateUpdate, after the node animation): the opening / closing clip on the nodes it animates.</summary>
        private void StepDoor(float dt)
        {
            if (_doorClip == null || _doorNodes == null) return;
            if (_doorPlaying) { _doorT += dt; if (_doorT >= _doorClip.Duration) { _doorT = _doorClip.Duration; _doorPlaying = false; } }
            var a = _doorClip;
            float frame = Mathf.Clamp(_doorT / HaloAnimation.FrameTime, 0f, a.Frames - 1);
            if (_doorReverse) frame = a.Frames - 1 - frame;
            int f0 = Mathf.Clamp(Mathf.FloorToInt(frame), 0, a.Frames - 1), f1 = Mathf.Min(a.Frames - 1, f0 + 1);
            float u = frame - f0;
            int n = a.NodeCount;
            float s = HaloSpace.Unit;
            for (int k = 0; k < n && k < _doorNodes.Length; k++)
            {
                var t = _doorNodes[k];
                if (t == null) continue;
                bool all = a.Type == HaloAnimation.Kind.Base;
                if (all || a.Rotates(k))
                {
                    int i0 = (f0 * n + k) * 4, i1 = (f1 * n + k) * 4;
                    var q0 = new Quaternion(a.Rot[i0], a.Rot[i0 + 1], a.Rot[i0 + 2], a.Rot[i0 + 3]);
                    var q1 = new Quaternion(a.Rot[i1], a.Rot[i1 + 1], a.Rot[i1 + 2], a.Rot[i1 + 3]);
                    Quaternion q = Quaternion.Slerp(q0, q1, u);
                    t.localRotation = HaloSpace.Rot(q.x, q.y, q.z, q.w);      // decoded rotations are standard (not Halo's inverse): no conjugate
                }
                if (all || a.Translates(k))
                {
                    int i0 = (f0 * n + k) * 3, i1 = (f1 * n + k) * 3;
                    float x = Mathf.Lerp(a.Trans[i0], a.Trans[i1], u), y = Mathf.Lerp(a.Trans[i0 + 1], a.Trans[i1 + 1], u), z = Mathf.Lerp(a.Trans[i0 + 2], a.Trans[i1 + 2], u);
                    t.localPosition = new Vector3(x * s, z * s, y * s);
                }
            }
        }

        // ------------------------------------------------------------------ campaign vitality

        /// <summary>Campaign damage: the collision model's body vitality (points); true when it applied (the MP rule is then skipped).</summary>
        private bool CampaignDamage(HaloHit hit)
        {
            var c = Def.Collision;
            if (c == null || c.MaxBody <= 0f || Destroyed) return false;
            float amount = hit.Instant ? c.MaxBody : Mathf.Max(0f, hit.Amount);
            Vitality = Mathf.Max(0f, Vitality - amount / c.MaxBody);
            if (Body != null && LocalDriven && hit.Explosion && hit.Direction.sqrMagnitude > 0f)
                Body.AddForceAtPosition(hit.Direction.normalized * Mathf.Min(hit.Amount, 200f) * 40f, hit.Point, ForceMode.Impulse); // ours
            if (LocalSeated && !hit.Instant && hit.SourcePlayer != HaloLocal.NetId && Def.RiderDamageFraction > 0f)
            {
                HaloHit pass = hit; pass.Amount = hit.Amount * Def.RiderDamageFraction;
                HaloGame.Guard("vehicle rider damage", () => HaloCombat.TakeHit(pass));
            }
            if (Vitality <= 0f) Destroy(hit);
            return true;
        }

        /// <summary>Destroyed (campaign): the collision model's destroyed / depleted effect, everybody out, a dead wreck (no seats, no driving).</summary>
        private void Destroy(HaloHit hit)
        {
            Destroyed = true;
            var c = Def.Collision;
            HaloTag fx = c.BodyDestroyedEffect ?? c.BodyDepletedEffect;
            if (fx == null) foreach (var r in c.Regions) if (r.DestroyedEffect != null) { fx = r.DestroyedEffect; break; }
            if (fx == null) fx = Def.Effect;
            Vector3 at = ShapeBounds().center;
            if (fx != null) HaloFx.Play(fx, at, transform.rotation, Model != null ? Model.Root.transform : transform);
            HaloLog.Info("Vehicles: " + name + (ObjectName.Length > 0 ? " '" + ObjectName + "'" : "") + " destroyed (" + c.MaxBody.ToString("0") + " body points" + (fx != null ? ", effect " + fx.Name : ", no effect in its tags") + ").");
            if (HaloVehicleSeat.Local.Vehicle == this) HaloVehicleSeat.Local.GetOut("vehicle destroyed");
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Unit != null) UnseatUnit(Seats[i].Unit);
            _aiDriving = false;
            Controls = default(HaloVehicleControls);
            EnterableByPlayer = false;
            if (Body != null && !Body.isKinematic) Body.AddForce(Vector3.up * Body.mass * 4f, ForceMode.Impulse); // ours: the blast lifts the wreck
            var h = VehicleDestroyed;
            if (h != null) HaloGame.Guard("vehicle destroyed", () => h(this));
        }
    }
}
