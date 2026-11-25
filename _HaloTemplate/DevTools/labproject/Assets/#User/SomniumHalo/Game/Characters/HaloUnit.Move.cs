using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Moving the unit: kinematic, along <see cref="MoveVelocity"/>, walls stop it (a capsule cast against the world, scenery, vehicles and
    /// players' capsules, sliding along), the feet follow the ground (ray down, steps up to a third of the body), gravity when the ground
    /// drops away (airborne, landing soft / hard by the biped's landing velocities). Flying units (sentinels, the monitor) move in 3D at their
    /// tag's speed without gravity. The body turns toward what it looks / aims at (stationary: only past the biped's stationary turning
    /// threshold, the turn animations playing), else toward where it goes. Animation root motion (dives, evades, deaths) moves it too.
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>Gravity (m/s^2): Halo's globals gravity is 0.0036 world units / tick^2 ~ 9.8 m/s^2.</summary>
        public static float Gravity = 9.8f;
        /// <summary>What blocks a unit's body: map geometry, scenery, vehicles, the players' capsules.</summary>
        public static int BlockMask = HaloLayers.GroundMask | (1 << HaloLayers.Vehicle) | (1 << HaloLayers.Avatar);

        /// <summary>The body's facing (Unity yaw, degrees).</summary>
        public float Yaw => _yaw;
        /// <summary>On the ground (false while falling / jumping; flying units are never "on the ground").</summary>
        public bool Grounded => _grounded;
        /// <summary>The speed (m/s) of the unit's own move-front animation for its current stance and weapon: the pace it runs at in the game.</summary>
        public float NaturalSpeed => Animator != null ? Animator.NaturalSpeed * HaloSpace.Unit : 3f;
        /// <summary>Riding a vehicle seat (the unit sits on the seat marker; no own movement).</summary>
        public Transform Seat { get; private set; }
        public string SeatLabel { get; private set; }
        public Vector3 Velocity => _velocity;

        private float _yaw, _turnRate;
        private bool _grounded = true, _turning, _wasAirborne;
        private float _airTime, _fallSpeed;
        private Vector3 _velocity;
        private float _aimYaw, _aimPitch, _lookYaw, _lookPitch;
        private bool _aiming, _looking;
        private float _radius = 0.5f, _height = 1.8f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private void InitMove()
        {
            _radius = Mathf.Clamp(Def.Radius * HaloSpace.Unit, 0.15f, 1.5f);
            _height = Def.StandingHeight > 0.01f ? Def.StandingHeight * HaloSpace.Unit : 1.8f;
            if (Def.Flying) _grounded = false;
            else SnapToGround(0.6f);
        }

        /// <summary>Puts the feet on the ground under / just above the unit (spawning, teleports).</summary>
        public void SnapToGround(float reach)
        {
            Vector3 p = transform.position;
            RaycastHit h;
            if (CastDown(p + Vector3.up * reach, reach + 4f, out h)) { transform.position = new Vector3(p.x, h.point.y, p.z); _grounded = true; _fallSpeed = 0f; }
        }

        /// <summary>Puts the unit at a point facing a Unity yaw (scripts, teleports).</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            transform.position = position;
            _yaw = yawDegrees;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            if (!Def.Flying && Seat == null) SnapToGround(1f);
        }

        /// <summary>Sits the unit on a vehicle seat marker with the graph's seat of that label ("W-passenger", "G-driver" ...).</summary>
        public void EnterSeat(Transform seatMarker, string seatLabel)
        {
            Seat = seatMarker;
            SeatLabel = seatLabel;
            if (seatMarker != null)
            {
                transform.SetParent(seatMarker, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.Euler(0f, 90f, 0f);      // the seat marker's +X (Halo forward) is the unit's forward
            }

            _exiting = false;
            // the seat's own enter clip (unit seat animation 7: climbing in), played in the seat's frame
            HaloAnimation enter = SeatClip(HaloAnimIds.Seat.Enter);
            if (enter != null && Alive) Animator.PlayCustom(Animator.Graph, enter, true, false, false);
        }

        /// <summary>
        /// Out of the seat. With the seat's exit clip (unit seat animation 8: the dropship troops' jump out of the pod, a marine climbing out of a
        /// warthog / pelican) the unit stays on the seat while the clip moves its body out, then stands where the clip left its pelvis (falling
        /// from there when that is in the air: airborne, then landing); without one it is put at <paramref name="at"/> at once.
        /// </summary>
        public void ExitSeat(Vector3 at, float yawDegrees)
        {
            if (Seat != null && Alive && !_exiting)
            {
                HaloAnimation exit = SeatClip(HaloAnimIds.Seat.Exit);
                if (exit != null && Animator.PlayCustom(Animator.Graph, exit, false, false, true))
                {
                    _exiting = true;
                    _exitAt = at;
                    _exitYaw = yawDegrees;
                    return;
                }
            }

            _exiting = false;
            Seat = null;
            SeatLabel = null;
            transform.SetParent(HaloBipeds.Parent, true);
            Teleport(at, yawDegrees);
        }

        /// <summary>Getting out: the exit clip playing on the seat (the seat is let go when it ends).</summary>
        public bool Exiting => _exiting;

        private bool _exiting;
        private Vector3 _exitAt;
        private float _exitYaw, _pelvisHeight = -1f;

        private HaloAnimation SeatClip(int id)
        {
            if (Animator == null || string.IsNullOrEmpty(SeatLabel)) return null;
            HaloAnimGraph.Seat s = Animator.Graph.FindSeat(SeatLabel);
            HaloAnimation a = s != null ? Animator.Pick(s.Anim(id)) : null;
            return a != null && a.Decode() ? a : null;
        }

        /// <summary>The exit clip ended: stand where it left the body (feet under the pelvis at the body's own standing pelvis height), facing
        /// the way it faces; in the air it falls and lands (Halo's airborne / land animations), then moves normally.</summary>
        private void FinishExit()
        {
            _exiting = false;
            Vector3 pelvis = _pelvisNode != null ? _pelvisNode.position : transform.position;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            float yaw = fwd.sqrMagnitude > 1e-4f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : _exitYaw;
            float ph = _pelvisHeight > 0.05f ? _pelvisHeight : 0.9f;
            Vector3 feet = new Vector3(pelvis.x, pelvis.y - ph, pelvis.z);
            Seat = null;
            SeatLabel = null;
            transform.SetParent(HaloBipeds.Parent, true);
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            _yaw = yaw;
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            _grounded = false;
            _airTime = 0.15f;
            if (Animator != null) Animator.StopCustom(false);      // (no blend: the clip's pelvis offset is now the root's move)
            if (_exitsLogged++ < 12) HaloLog.Info("Characters: " + Def.Name + " got out (exit clip) at " + feet.ToString("F1") + ", " + (feet - _exitAt).magnitude.ToString("0.0") + " m from the vehicle's stand point");
        }

        private static int _exitsLogged;

        /// <summary>The body's standing pelvis height (the stand seat's first class idle, frame 0): where the feet are under a pelvis.</summary>
        private void MeasurePelvis()
        {
            _pelvisHeight = -1f;
            if (Animator == null || _skel == null) return;
            int pv = Def.PelvisNode >= 0 && Def.PelvisNode < _skel.Count ? Def.PelvisNode : _skel.Node("bip01 pelvis");
            if (pv < 0) return;
            HaloAnimGraph g = Animator.Graph;
            HaloAnimGraph.Seat s = g.FindSeat("stand") ?? (g.Seats.Count > 0 ? g.Seats[0] : null);
            HaloAnimation idle = null;
            if (s != null) for (int i = 0; i < s.Weapons.Count && idle == null; i++) idle = g.Get(s.Weapons[i].Anim(HaloAnimIds.Weapon.Idle));
            var p = new HaloPose(_skel);
            p.Reset();
            if (idle != null) p.Apply(idle, Animator.Map, 0f, true, 1f);
            p.ToModel();
            _pelvisHeight = p.MTrans[pv * 3 + 2] * HaloSpace.Unit;
        }

        private void TickMove(float dt)
        {
            if (Seat != null) { _velocity = Vector3.zero; AimAngles(dt); return; }
            Vector3 want = Frozen ? Vector3.zero : MoveVelocity;
            if (!Def.Flying) want.y = 0f;
            // dives, evades and landings move the body by their own root motion
            bool rooted = Animator != null && (Animator.Action == HaloUnitAnimator.ActionKind.Dive || Animator.CustomPlaying);
            if (rooted) want = Vector3.zero;
            Facing(dt, want);
            AimAngles(dt);
            if (Def.Flying) { Fly(dt, want); return; }

            if (Jump && _grounded && !Frozen)
            {
                Jump = false;
                _grounded = false;
                _fallSpeed = -Mathf.Max(0.5f, Def.JumpVelocity * 30f * HaloSpace.Unit);      // jump velocity: world units a tick
            }
            else Jump = false;

            // accelerate toward the wanted velocity (Halo bipeds: quick; ours)
            Vector3 v = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), want, 40f * dt);
            if (!_grounded) v = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), want, 4f * dt);
            _velocity = v;
            Vector3 step = v * dt;
            MoveHorizontal(step);
            Vertical(dt);
        }

        private void TickDeadMove(float dt)
        {
            if (Seat != null || Def.Flying && Animator != null && !Animator.Dying) return;
            _velocity = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), Vector3.zero, 8f * dt);
            MoveHorizontal(_velocity * dt);
            Vertical(dt);
        }

        /// <summary>The body turns toward the aim / look (stationary: past the turning threshold), else toward the movement.</summary>
        private void Facing(float dt, Vector3 want)
        {
            if (Animator != null && Animator.CustomPlaying) { _turnRate = 0f; return; }      // (the custom animation turns the body itself)
            Vector3 flatMove = new Vector3(want.x, 0f, want.z);
            bool moving = flatMove.sqrMagnitude > 0.01f;
            Vector3 target = AimPoint != Vector3.zero ? AimPoint : LookPoint;
            float desired = _yaw;
            bool hasTarget = target != Vector3.zero;
            if (_recorded) { desired = _recordedYaw; hasTarget = false; moving = true; }      // a recording: the recorded facing, turned like moving
            else if (hasTarget && !(moving && Animator != null && Animator.FaceMovement))
            {
                Vector3 d = target - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) desired = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            else if (moving) desired = Mathf.Atan2(flatMove.x, flatMove.z) * Mathf.Rad2Deg;

            float err = Mathf.DeltaAngle(_yaw, desired);
            float speed = Mathf.Max(90f, (Def.MovingTurningSpeed > 0.01f ? Def.MovingTurningSpeed : 3.14f) * Mathf.Rad2Deg);
            _turnRate = 0f;
            if (moving || Def.Flying || Def.TurnsWithoutAnimating || !hasTarget)
            {
                _turning = false;
                float stepDeg = Mathf.Clamp(err, -speed * dt, speed * dt);
                _yaw += stepDeg;
            }
            else
            {
                // standing: the legs stay until the aim leaves the threshold, then turn (the turn animations) until it is ahead again
                float threshold = Def.StationaryTurningThreshold > 0.01f ? Def.StationaryTurningThreshold * Mathf.Rad2Deg : 60f;
                if (!_turning && Mathf.Abs(err) > threshold) _turning = true;
                if (_turning)
                {
                    float stepDeg = Mathf.Clamp(err, -speed * dt, speed * dt);
                    _yaw += stepDeg;
                    _turnRate = -stepDeg * Mathf.Deg2Rad / dt;      // + = left (Halo)
                    if (Mathf.Abs(err) < 3f) _turning = false;
                }
            }

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Aim and look angles relative to the body (Halo: yaw + left, pitch + up), turned at the biped's aiming / looking speeds.</summary>
        private void AimAngles(float dt)
        {
            Vector3 eye = Eye;
            Vector3 fwd = transform.forward;
            float ay = 0f, ap = 0f, ly = 0f, lp = 0f;
            _aiming = AimPoint != Vector3.zero;
            _looking = LookPoint != Vector3.zero || _aiming;
            Vector3 aimFrom = Muzzle != null ? Vector3.Lerp(Center, eye, 0.7f) : eye;
            if (_aiming) Angles(AimPoint - aimFrom, fwd, out ay, out ap);
            if (_looking) Angles((LookPoint != Vector3.zero ? LookPoint : AimPoint) - eye, fwd, out ly, out lp);
            float aimSpeed = (Def.AimingVelocityMaximum > 0.01f ? Def.AimingVelocityMaximum : 6.28f) * dt;
            float lookSpeed = (Def.LookingVelocityMaximum > 0.01f ? Def.LookingVelocityMaximum : 12.5f) * dt;
            _aimYaw = Mathf.MoveTowards(_aimYaw, ay, aimSpeed);
            _aimPitch = Mathf.MoveTowards(_aimPitch, ap, aimSpeed);
            _lookYaw = Mathf.MoveTowards(_lookYaw, ly, lookSpeed);
            _lookPitch = Mathf.MoveTowards(_lookPitch, lp, lookSpeed);
        }

        private static void Angles(Vector3 d, Vector3 fwd, out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            yaw = -Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), flat, Vector3.up) * Mathf.Deg2Rad;
            pitch = Mathf.Atan2(d.y, flat.magnitude);
        }

        private void MoveHorizontal(Vector3 step)
        {
            float len = step.magnitude;
            if (len < 1e-5f) return;
            Vector3 pos = transform.position;
            for (int pass = 0; pass < 2 && len > 1e-5f; pass++)
            {
                Vector3 dir = step / len;
                RaycastHit h;
                if (CapsuleCast(pos, dir, len, out h))
                {
                    float go = Mathf.Max(0f, h.distance - 0.02f);
                    pos += dir * go;
                    Vector3 n = h.normal; n.y = 0f;
                    if (n.sqrMagnitude < 1e-4f) break;
                    n.Normalize();
                    Vector3 rest = step - dir * go;
                    step = rest - n * Vector3.Dot(rest, n);
                    len = step.magnitude;
                }
                else { pos += step; break; }
            }

            transform.position = pos;
        }

        private void Vertical(float dt)
        {
            Vector3 p = transform.position;
            float stepUp = Mathf.Clamp(_height * 0.33f, 0.25f, 0.6f);
            RaycastHit h;
            bool ground = CastDown(p + Vector3.up * stepUp, stepUp + (_grounded ? 0.35f : Mathf.Max(0.05f, _fallSpeed * dt + 0.02f)), out h);
            if (_fallSpeed < 0f) ground = false;      // going up (a jump)
            if (ground && h.normal.y > 0.5f)
            {
                if (!_grounded) Landed();
                _grounded = true;
                _fallSpeed = 0f;
                _airTime = 0f;
                transform.position = new Vector3(p.x, h.point.y, p.z);
                return;
            }

            _grounded = false;
            // nothing at all below (the world's collision not built yet, or a gap in it): wait in place instead of falling forever
            RaycastHit below;
            if (_fallSpeed >= 0f && !CastDown(p + Vector3.up * 0.3f, 60f, out below)) { _fallSpeed = 0f; _airTime = 0f; return; }
            _airTime += dt;
            _fallSpeed += Gravity * dt;
            float dy = _fallSpeed * dt;
            if (dy < 0f)
            {
                // rising: a ceiling stops it
                RaycastHit c;
                if (Physics.Raycast(p + Vector3.up * _height, Vector3.up, out c, -dy + 0.05f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) { _fallSpeed = 0f; dy = 0f; }
            }

            transform.position = p - Vector3.up * dy;
        }

        private void Landed()
        {
            float v = _fallSpeed / HaloSpace.Unit / 30f;      // world units a tick, as the biped's landing velocities
            if (_airTime < 0.25f || Animator == null || !Alive) return;
            try { HaloFootsteps.Step(transform.position, HaloFootsteps.Land, UnitTag, Mathf.Clamp(0.6f + _airTime, 0.6f, 1.2f)); } catch (System.Exception) { }   // (A: the landing sound of the unit's footsteps tag)
            bool hard = Def.MinHardLandingVelocity > 0f && v >= Def.MinHardLandingVelocity * 0.033f;
            if (Def.MinSoftLandingVelocity <= 0f || v >= Def.MinSoftLandingVelocity * 0.033f || _airTime > 0.6f) Animator.Land(hard);
        }

        private void Fly(float dt, Vector3 want)
        {
            float max = Def.MaxVelocity > 0.01f ? Def.MaxVelocity * HaloSpace.Unit : 6f;      // world units a second
            if (want.magnitude > max) want = want.normalized * max;
            float acc = Mathf.Max(2f, (Def.Acceleration > 0f ? Def.Acceleration : 0.1f) * 30f * HaloSpace.Unit);
            _velocity = Vector3.MoveTowards(_velocity, want, acc * dt);
            Vector3 step = _velocity * dt;
            float len = step.magnitude;
            if (len < 1e-5f) return;
            Vector3 c = transform.position + (Def.PillCenteredAtOrigin ? Vector3.zero : Vector3.up * _radius);
            RaycastHit h;
            int n = Physics.SphereCastNonAlloc(c, _radius * 0.8f, step / len, Hits, len, BlockMask, QueryTriggerInteraction.Ignore);
            float best = len;
            bool hit = false;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider == null || IsOwn(Hits[i].collider) || Hits[i].distance <= 0f) continue;
                if (Hits[i].distance < best) { best = Hits[i].distance; hit = true; h = Hits[i]; }
            }

            transform.position += step / len * Mathf.Max(0f, best - 0.02f);
            if (hit) _velocity *= 0.5f;
        }

        private bool CapsuleCast(Vector3 feet, Vector3 dir, float len, out RaycastHit best)
        {
            float r = _radius * 0.9f;
            float bottom = Mathf.Clamp(_height * 0.33f, 0.25f, 0.6f) + r;      // above the step height: steps are climbed, not walls
            float top = Mathf.Max(bottom, _height - r);
            Vector3 a = feet + Vector3.up * bottom, b = feet + Vector3.up * top;
            int n = Physics.CapsuleCastNonAlloc(a, b, r, dir, Hits, len + 0.02f, BlockMask, QueryTriggerInteraction.Ignore);
            best = default(RaycastHit);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || IsOwn(h.collider)) continue;
                if (h.distance <= 0f) { if (Vector3.Dot(h.normal, dir) >= 0f) continue; h.distance = 0f; }      // started inside: only what faces us
                if (h.distance < bd) { bd = h.distance; best = h; }
            }

            return bd < float.MaxValue;
        }

        private bool CastDown(Vector3 from, float len, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, len, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
            best = default(RaycastHit);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider == null || IsOwn(Hits[i].collider)) continue;
                if (Hits[i].distance < bd) { bd = Hits[i].distance; best = Hits[i]; }
            }

            return bd < float.MaxValue;
        }

        private bool IsOwn(Collider c) { return c.transform.IsChildOf(transform); }

        // ------------------------------------------------------------------ the animation input / output

        private HaloUnitAnimInput AnimInput()
        {
            var i = new HaloUnitAnimInput();
            if (Seat != null) i.Seat = SeatLabel;
            else if (Crouching) i.Seat = "crouch";
            else i.Seat = string.IsNullOrEmpty(Stance) ? "stand" : Stance;
            i.WeaponLabel = WeaponLabel;
            if (Alive && Seat == null)
            {
                Vector3 v = _velocity / HaloSpace.Unit;          // world units a second
                Vector3 f = transform.forward, r = transform.right;
                i.MoveX = Vector3.Dot(v, f);
                i.MoveY = -Vector3.Dot(v, r);
                if (Def.Flying) { i.MoveX = 0f; i.MoveY = 0f; }
                i.TurnRate = _turnRate;
                i.Airborne = !Def.Flying && !_grounded && _airTime > 0.12f;
            }
            else i.Airborne = !Def.Flying && !_grounded && _airTime > 0.12f;

            i.Aiming = _aiming && Alive;
            i.Looking = _looking && Alive;
            i.AimYaw = _aimYaw; i.AimPitch = _aimPitch;
            i.LookYaw = _lookYaw; i.LookPitch = _lookPitch;
            _wasAirborne = i.Airborne;
            return i;
        }

        /// <summary>After the animation step: its root motion moves the body; its key frames fire the action (grenade release, melee hit) and footsteps.</summary>
        private void AfterAnimation()
        {
            HaloUnitAnimator a = Animator;
            if ((a.RootX != 0f || a.RootY != 0f || a.RootYaw != 0f) && Seat == null)
            {
                Vector3 local = new Vector3(-a.RootY, 0f, a.RootX) * HaloSpace.Unit;      // Halo x forward, y left -> Unity +z forward, +x right
                MoveHorizontal(transform.rotation * local);
                if (a.RootYaw != 0f) { _yaw -= a.RootYaw * Mathf.Rad2Deg; transform.rotation = Quaternion.Euler(0f, _yaw, 0f); }
            }

            if (a.ActionKey) ActionKeyFrame(a.Action);
            if (a.FootDown && Alive && _grounded && Seat == null)
            {
                try { HaloFootsteps.Step(transform.position, _velocity.magnitude > 2.5f ? HaloFootsteps.Run : HaloFootsteps.Walk, UnitTag, 0.8f); }   // (A: the unit's own footsteps tag - grunts, elites, flood)
                catch (System.Exception) { }
            }
        }
    }
}
