using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] Remote cars (part of <see cref="AcNetGame"/>; ETS/EtsNetVehicle.cs ported): after AcSession.TrackBuilt, every listed player who has a
    /// car out on MY layout (and, on non-masters, every AI car of the master) gets an <see cref="AcRemoteCar"/>: a grey stand-in box at once (solid,
    /// on the Car layer), then the real car through P's ONE factory <c>AcCarRig.Spawn(Kind Remote)</c> (one build at a time, sliced by P/C), drawn
    /// every frame from its timeline (EtsNetTimeline port: Hermite, ~1 send interval behind, snap on a new placement epoch) through
    /// <c>Physics.ApplyPuppet</c>, with the cockpit and wheels from the record (<c>Model.ApplyTelemetry</c>: steer, wheel spin from the owner's wheel
    /// angular velocity, travel, lights, instruments incl. fuel; <c>SetDoor</c>) and A's exterior sounds fed from the record (<c>Audio.Feed</c>, door
    /// edges -> <c>Audio.Door</c>, the horn bit -> <c>Audio.Horn</c>). Hidden (posed, not shown) beyond <see cref="DrawRadius"/>. Contacts are
    /// one-sided (kinematic puppets on the Car layer, the local car feels them); a puppet passes through the local car (Collider.excludeLayers |= Car
    /// - survives deactivation, ETS lesson 12) while either car is in the pit lane or was placed in the last <see cref="SpawnGhostSeconds"/>, and their
    /// relative speed is above <see cref="GhostKmh"/> (solid again below <see cref="SolidKmh"/>, never toggled while they overlap) - ETS Q-A3 adapted
    /// to racing. A parked player car I walk up to is rebuilt with its high-res cockpit (a passenger sees COCKPIT_HR) and swapped like the stand-in.
    /// A car not installed here stays a grey box with A's common sounds. Destroyed on unload / leave. Every call into P / C / A is guarded per car.
    /// </summary>
    public static partial class AcNetGame
    {
        /// <summary>Remote cars farther than this from my eye are posed but not shown (ETS TruckDrawRadius). // ours (ETS value)</summary>
        public const float DrawRadius = 1500f;
        /// <summary>Q-A3: see-through above 10 km/h relative speed, solid again below 9 (hysteresis). // ours (ETS values)</summary>
        public const float GhostKmh = 10f, SolidKmh = 9f;
        /// <summary>A car just placed (spawn, back to pits, grid) is ghost-able this long. // ours</summary>
        public const float SpawnGhostSeconds = 3f;
        /// <summary>A parked remote car (below this speed) within <see cref="InteriorNearMetres"/> of my eye is rebuilt with its interior. // ours</summary>
        public const float InteriorParkedKmh = 2f, InteriorNearMetres = 15f;
        /// <summary>
        /// S may plug this: N built the high-res-cockpit rig (second argument) of the remote car the local player rides in (first argument); S moves its
        /// seat onto it and returns true (N then destroys the old rig), or returns false (N keeps the old rig). Null = N rebuilds only cars I am NOT in.
        /// </summary>
        public static Func<AcCarRig, AcCarRig, bool> SwapSeatedCar;

        /// <summary>Two car roots closer than this count as overlapping (never toggle solidity then). // ours</summary>
        private const float OverlapMetres = 4.5f;
        /// <summary>A door value above this is "open" (the record carries u8 steps of 1/255). // ours</summary>
        private const float DoorClosed = 0.002f;
        /// <summary>A build that never reports back (its coroutine died with its host) frees the build queue after this. // ours</summary>
        private const float BuildStuckSeconds = 120f;
        /// <summary>Stand-in box size and its centre above the car root (CG). // ours (an average road car; AC has no size number for a car that is not installed)</summary>
        private static readonly Vector3 StandInSize = new Vector3(1.9f, 1.3f, 4.5f);
        private static readonly Vector3 StandInCentre = new Vector3(0f, 0.2f, 0f);

        private static bool _trackReady;
        private static AcRemoteCar _building;
        private static float _buildingSince;
        private static Material _standInMaterial;
        private static readonly int[] DrawErrors = new int[6];

        private static void OnTrackBuilt()
        {
            _trackReady = true;
        }

        private static void OnUnloading()
        {
            _trackReady = false;
            ClearCars("the track unloads");
        }

        /// <summary>Early frame: create / drop remote cars, draw them at this frame's moment, set their contacts. Each car on its own guard.</summary>
        private static void DrawCars()
        {
            bool ready = _trackReady && AcSession.OnTrack && AcSession.WorldRoot != null;
            int layout = LayoutHash;
            for (int i = 0; i < PlayerList.Count; i++)
            {
                AcNetPlayer p = PlayerList[i];
                AcNet.Remote r = Net.RemoteAt(p.Slot);
                bool want = ready && r != null && p.HasCar && p.LayoutHash == layout && layout != 0 && !p.Spectating;
                try { SyncCar(p, want, r != null ? r.Timeline : null); }
                catch (Exception e) { DrawFailed(0, "creating / dropping the car of " + p.Name, e); }
            }

            for (int i = 0; i < AiList.Count; i++)
            {
                AcNetPlayer p = AiList[i];
                AcNet.AiCar a = Net.Ai(p.AiIndex);
                bool want = ready && a != null && a.Present && p.LayoutHash == layout && layout != 0;
                try { SyncCar(p, want, a != null ? a.Timeline : null); }
                catch (Exception e) { DrawFailed(0, "creating / dropping the AI car " + p.Name, e); }
            }

            StartNextBuild();
            double now = Net.Now;
            float dt = Time.deltaTime;
            Vector3 eye = EyePosition();
            for (int i = 0; i < CarList.Count; i++)
            {
                AcRemoteCar rc = CarList[i];
                try { Draw(rc, now, dt, eye); }
                catch (Exception e) { DrawFailed(1, "drawing the remote car " + rc.CarId, e); }
            }
        }

        private static void DrawFailed(int kind, string what, Exception e)
        {
            int n = ++DrawErrors[kind];
            if (n <= 3) AcLog.Warn("Net: " + what + " failed (" + n + (n == 3 ? ", later ones of this kind not logged" : "") + "): " + e);
        }

        private static void SyncCar(AcNetPlayer p, bool want, AcNetTimeline timeline)
        {
            AcRemoteCar rc = p.Car;
            if (!want || timeline == null)
            {
                if (rc != null) DropCar(rc, "not on my track");
                return;
            }

            if (rc != null && (!string.Equals(rc.CarId, p.CarId, StringComparison.Ordinal) || !string.Equals(rc.SkinId, p.SkinId, StringComparison.Ordinal)))
            {
                DropCar(rc, "another car / skin");
                rc = null;
            }

            if (rc == null) rc = NewCar(p, timeline);
            rc.Timeline = timeline;
        }

        private static AcRemoteCar NewCar(AcNetPlayer p, AcNetTimeline timeline)
        {
            var rc = new AcRemoteCar { Player = p, CarId = p.CarId, SkinId = p.SkinId, Timeline = timeline, Wanted = true, StandInSince = Time.realtimeSinceStartup };
            rc.Entry = CarByHash(AcNetWire.NameHash(p.CarId));
            rc.StandInOnly = rc.Entry == null || !rc.Entry.Owned;
            var holder = new GameObject("Ac Net Car " + (p.IsAi ? "AI " + (p.AiIndex + 1) : "slot " + p.Slot) + " (" + p.CarId + ")");
            Transform parent = AcSession.WorldRoot;
            if (parent != null) holder.transform.SetParent(parent, false);
            rc.Holder = holder;
            rc.FrameStandIn = new GameObject("frame").transform;
            rc.FrameStandIn.SetParent(holder.transform, false);
            p.Car = rc;
            CarList.Add(rc);
            if (rc.StandInOnly)
            {
                AcLog.InfoOnce("Net: remote car " + p.Name + " " + p.CarId + " is not installed here - grey stand-in with common sounds");
                // lead (A -> N): the sound rides on the moving stand-in frame, not on the holder at the track origin
                try { rc.BoxAudio = AcCarAudio.Attach(rc.FrameStandIn.gameObject, rc.Entry, null, false); }
                catch (Exception e) { AcLog.WarnOnce("Net: stand-in audio failed: " + e.Message); }
            }

            return rc;
        }

        // ------------------------------------------------------------------ building (one at a time)

        /// <summary>One car build at a time (the factory slices its own work; EtsNet's rule): first cars that have no rig yet, then interior rebuilds.</summary>
        private static void StartNextBuild()
        {
            if (_game == null) return;
            if (_building != null)
            {
                if (Time.realtimeSinceStartup - _buildingSince < BuildStuckSeconds) return;
                AcLog.Warn("Net: the build of remote car " + _building.CarId + " never finished - the next one starts");
                _building.Building = false;
                _building.BuildingInterior = false;
                if (_building.Rig == null) _building.StandInOnly = true;
                else _building.InteriorFailed = true;
                _building = null;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < CarList.Count; i++)
                {
                    AcRemoteCar rc = CarList[i];
                    if (rc.Building || rc.StandInOnly || rc.Disposed) continue;
                    bool interior = pass == 1;
                    if (!interior && rc.Rig != null) continue;
                    if (interior && (rc.Rig == null || rc.Interior || !rc.WantInterior || rc.InteriorFailed)) continue;
                    Begin(rc, interior);
                    return;
                }
            }
        }

        private static void Begin(AcRemoteCar rc, bool interior)
        {
            rc.Building = true;
            rc.BuildingInterior = interior;
            _building = rc;
            _buildingSince = Time.realtimeSinceStartup;
            try { _game.StartCoroutine(Build(rc, interior)); }
            catch (Exception e)
            {
                rc.Building = false;
                rc.BuildingInterior = false;
                if (interior) rc.InteriorFailed = true;
                else rc.StandInOnly = true;
                _building = null;
                AcLog.Warn("Net: could not start building " + rc.CarId + ": " + e.Message);
            }
        }

        /// <summary>C's options for a remote car I ride in / walk up to: the Remote defaults (LOD_0..3 by distance, hull) plus the high-res cockpit
        /// with its instruments and digital displays (what a passenger looks at).</summary>
        private static AcCarModelOptions InteriorOptions(bool ai)
        {
            AcCarModelOptions o = AcCarModelOptions.ForKind(ai ? AcCarKind.Ai : AcCarKind.Remote);
            o.Interior = true;
            o.Instruments = true;
            o.Digital = true;
            return o;
        }

        /// <summary>P's factory, drained here so a throw inside it ends this build with a reason (C# allows no yield inside a try with a catch).</summary>
        private static IEnumerator Build(AcRemoteCar rc, bool interior)
        {
            AcCarRig built = null;
            string failure = null;
            IEnumerator build = null;
            bool ai = rc.Player != null && rc.Player.IsAi;
            try
            {
                var options = new AcCarRigOptions
                {
                    Kind = AcCarKind.Remote,
                    Audio = true,
                    Parent = rc.Holder != null ? rc.Holder.transform : AcSession.WorldRoot,
                    // lead (C -> N): an AI record gets C's AI options (LODs + the knh-posed AC driver); Kind stays Remote (puppet)
                    Model = interior ? InteriorOptions(ai)
                          : (ai ? AcCarModelOptions.ForKind(AcCarKind.Ai) : null),
                };
                build = AcCarRig.Spawn(rc.Entry, rc.SkinId, options, r => built = r);
            }
            catch (Exception e) { failure = e.GetType().Name + ": " + e.Message; }

            while (build != null)
            {
                bool more;
                object current = null;
                try
                {
                    more = build.MoveNext();
                    if (more) current = build.Current;
                }
                catch (Exception e)
                {
                    failure = "the car factory threw " + e.GetType().Name + ": " + e.Message;
                    more = false;
                }

                if (!more) break;
                yield return current;
            }

            try { Finish(rc, built, failure, interior); }
            catch (Exception e)
            {
                if (ReferenceEquals(_building, rc)) _building = null;
                rc.Building = rc.BuildingInterior = false;
                AcLog.Warn("Net: finishing remote car " + rc.CarId + " failed: " + e);
            }
        }

        private static void Finish(AcRemoteCar rc, AcCarRig built, string failure, bool interior)
        {
            if (ReferenceEquals(_building, rc)) _building = null;
            rc.Building = false;
            rc.BuildingInterior = false;
            if (rc.Disposed || !_up)
            {
                DisposeRig(built);
                return;
            }

            if (built == null || built.Root == null)
            {
                if (interior)
                {
                    rc.InteriorFailed = true;
                    AcLog.Warn("Net: the cockpit of remote car " + rc.Player.Name + " " + rc.CarId + " could not be built (" + (failure ?? "no rig") + ") - its outside model stays");
                    return;
                }

                rc.StandInOnly = true;
                AcLog.Warn("Net: remote car " + rc.Player.Name + " " + rc.CarId + " could not be built (" + (failure ?? "no rig") + ") - grey stand-in");
                return;
            }

            if (interior)
            {
                SwapInterior(rc, built);
                return;
            }

            Adopt(rc, built);
            // off at once (Destroy waits for the end of the frame: the box and its collider would overlap the new rig for that frame)
            if (rc.Box != null) { rc.Box.SetActive(false); UnityEngine.Object.Destroy(rc.Box); rc.Box = null; }
            float standIn = Time.realtimeSinceStartup - rc.StandInSince;
            AcLog.Info("Net: remote car " + rc.Player.Name + " " + rc.CarId + "/" + rc.SkinId + " built (stand-in " + standIn.ToString("0.0") + " s)");
        }

        /// <summary>A built rig becomes the drawn one: owner, puppet, colliders (solid), the current pose and visibility, fresh audio state.</summary>
        private static void Adopt(AcRemoteCar rc, AcCarRig built)
        {
            Transform from = rc.Frame;     // the stand-in frame, or the old rig when swapping
            Vector3 pos = from != null ? from.position : Vector3.zero;
            Quaternion rot = from != null ? from.rotation : Quaternion.identity;
            rc.Rig = built;
            built.OwnerId = rc.Player.PhotonId;
            try { if (built.Physics != null) built.Physics.SetPuppet(true); }
            catch (Exception e) { AcLog.WarnOnce("Net: SetPuppet failed: " + e.Message); }
            rc.Colliders = built.Root.GetComponentsInChildren<Collider>(true);
            rc.Ghost = false;
            if (rc.Drawn) built.Root.transform.SetPositionAndRotation(pos, rot);
            if (rc.Hidden) built.Root.SetActive(false);
            // the new audio starts from the record's present state: no door sound for a door already open, the horn re-sent if it is held
            rc.AudioDoorL = rc.Drawn ? rc.DoorL : -1f;
            rc.AudioDoorR = rc.Drawn ? rc.DoorR : -1f;
            rc.HornOn = false;
            rc.DoorL = rc.DoorR = -1f;     // the model's doors are set from the record on the next draw
        }

        /// <summary>
        /// The high-res-cockpit rig replaces the outside one, like the stand-in: never under my own seat unless S takes it over
        /// (<see cref="SwapSeatedCar"/>), never while my ride request for this car is open (S holds that rig for its grant).
        /// </summary>
        private static void SwapInterior(AcRemoteCar rc, AcCarRig built)
        {
            AcCarRig old = rc.Rig;
            bool seatedInOld = old != null && ReferenceEquals(AcLocal.SeatCar, old);
            bool asking = Net.RequestActive && rc.Player != null && Net.RequestOwner == rc.Player.PhotonId;
            if (asking || old == null || old.Root == null)
            {
                // no swap now: keep the old rig; the rebuild is tried again later (asking) - or the car changed under us
                DisposeRig(built);
                if (!asking) rc.InteriorFailed = true;
                else rc.WantInterior = false;      // asked again once the request is answered (seated in it: only with S's swap hook)
                return;
            }

            if (seatedInOld)
            {
                bool taken = false;
                Func<AcCarRig, AcCarRig, bool> swap = SwapSeatedCar;
                if (swap != null)
                {
                    try { taken = swap(old, built); }
                    catch (Exception e) { AcLog.Warn("Net: S's SwapSeatedCar threw: " + e.Message); taken = false; }
                }

                if (!taken)
                {
                    DisposeRig(built);
                    rc.InteriorFailed = true;
                    AcLog.InfoOnce("Net: riding in a remote car that was built without its cockpit (S did not take the rebuilt one) - COCKPIT_LR is shown");
                    return;
                }
            }

            Adopt(rc, built);
            rc.Interior = true;
            DisposeRig(old);
            AcLog.Info("Net: remote car " + rc.Player.Name + " " + rc.CarId + " rebuilt with its cockpit (COCKPIT_HR) for a passenger" + (seatedInOld ? " - the seat moved over" : ""));
        }

        private static void DisposeRig(AcCarRig rig)
        {
            if (rig == null) return;
            try { rig.Dispose(); }
            catch (Exception e) { AcLog.WarnOnce("Net: disposing a remote rig failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ drawing

        private static void Draw(AcRemoteCar rc, double now, float dt, Vector3 eye)
        {
            if (rc.Timeline == null) return;
            AcNetCar s;
            if (!rc.Timeline.Sample(now, out s)) return;
            Vector3 pos = V(s.Pos);
            Quaternion rot = Q(s.Rot);
            Vector3 vel = V(s.Vel);
            rc.Velocity = vel;
            if (rc.Timeline.WasSnap || s.Epoch != rc.LastEpoch) { rc.SnapAt = Time.time; rc.LastEpoch = s.Epoch; }
            bool visible = (pos - eye).sqrMagnitude < DrawRadius * DrawRadius;
            SetHidden(rc, !visible);
            rc.FrameStandIn.SetPositionAndRotation(pos, rot);
            rc.Drawn = true;
            AcTelemetry t = Telemetry(rc, in s, dt);
            rc.Shown = t;
            AcCarRig rig = rc.Rig;
            if (rig != null && rig.Root != null)
            {
                DamageRig(rc, rig);                                  // iteration 2: also while hidden (the state is current when it shows)
                if (!visible) { rig.Root.transform.SetPositionAndRotation(pos, rot); return; }
                PoseRig(rc, rig, pos, rot, vel, t);
                ModelRig(rc, rig.Model, in s, ref t);
                SoundRig(rc, rig.Audio, in s, t);
                Contacts(rc, pos, vel, s.InPitlane);
                WantCockpit(rc, eye, pos);
            }
            else if (visible)
            {
                StandIn(rc);
                SoundRig(rc, rc.BoxAudio, in s, t);
                Contacts(rc, pos, vel, s.InPitlane);
            }
        }

        /// <summary>
        /// Iteration 2 (DAMAGE_DESIGN 7): the owner's damage pair (player record ints 42-43 / the master's "Ac Net Host 0" table for AI cars,
        /// decoded into the AcNetPlayer) -> P's <c>Damage.ApplyWire</c> on the drawn rig, only when the pair or the rig changed (a late joiner's
        /// first read, an interior rebuild, the model replacing the stand-in). P's Update then pushes the visuals; N raises CarDamaged for the
        /// remote car (P does not for puppets). All zero (damage 0) on a fresh rig = nothing happens. No allocation.
        /// </summary>
        private static void DamageRig(AcRemoteCar rc, AcCarRig rig)
        {
            AcNetPlayer p = rc.Player;
            if (p == null) return;
            int d0 = p.DamageD0, d1 = p.DamageD1;
            bool pairChanged = d0 != rc.DamageD0 || d1 != rc.DamageD1;
            if (ReferenceEquals(rc.DamageRig, rig) && !pairChanged) return;
            bool firstRig = rc.DamageRig == null;
            rc.DamageRig = rig; rc.DamageD0 = d0; rc.DamageD1 = d1;
            try
            {
                AcCarPhysics ph = rig.Physics;
                AcCarDamage dmg = ph != null ? ph.Damage : null;
                if (dmg == null || !dmg.ApplyWire(d0, d1)) return;
                float kmh;
                AcDamageChange what = dmg.TakeChanges(out kmh);
                if (what == AcDamageChange.None) return;
                if (!pairChanged && !firstRig) return;   // integration: a rebuilt rig (cockpit) took the same pair - visuals yes, no second CarDamaged
                rc.DamageEvents++;
                AcEvents.RaiseCarDamaged(rig, what, kmh);
            }
            catch (Exception e) { DrawFailed(4, "the damage of " + rc.CarId, e); }
        }

        private static void PoseRig(AcRemoteCar rc, AcCarRig rig, Vector3 pos, Quaternion rot, Vector3 vel, AcTelemetry t)
        {
            try
            {
                if (rig.Physics != null) rig.Physics.ApplyPuppet(pos, rot, vel, t);
                else rig.Root.transform.SetPositionAndRotation(pos, rot);
            }
            catch (Exception e)
            {
                DrawFailed(2, "P's ApplyPuppet on " + rc.CarId, e);
                rig.Root.transform.SetPositionAndRotation(pos, rot);
            }
        }

        private static void ModelRig(AcRemoteCar rc, AcCarModel m, in AcNetCar s, ref AcTelemetry t)
        {
            if (m == null) return;
            try
            {
                m.ApplyTelemetry(ref t);      // C: steer, wheels (+ rim blur from AngularVel), lights, instruments - no allocation
                AcNetPlayer lp = rc.Player;   // lead (C -> N): remote dashes show LAPTIME / LAST / BEST (3-int overload, no allocation)
                if (lp != null) m.SetLapTimes(lp.LapMs, lp.LastMs, lp.BestMs);
                if (Mathf.Abs(s.DoorL - rc.DoorL) > DoorClosed) { rc.DoorL = s.DoorL; m.SetDoor(AcSide.Left, s.DoorL); }
                if (Mathf.Abs(s.DoorR - rc.DoorR) > DoorClosed) { rc.DoorR = s.DoorR; m.SetDoor(AcSide.Right, s.DoorR); }
            }
            catch (Exception e) { DrawFailed(3, "C's model update on " + rc.CarId, e); }
        }

        /// <summary>A's sounds from the record: the engine / skid / surfaces (Feed), a door sound on each open / close edge (A -> N request), the horn bit.</summary>
        private static void SoundRig(AcRemoteCar rc, AcCarAudio a, in AcNetCar s, AcTelemetry t)
        {
            if (a == null) return;
            try
            {
                a.Feed(t);
                DoorSound(rc, a, AcSide.Left, s.DoorL, ref rc.AudioDoorL);
                DoorSound(rc, a, AcSide.Right, s.DoorR, ref rc.AudioDoorR);
                bool horn = (s.Lights & (int)AcLightState.Horn) != 0;
                if (horn != rc.HornOn) { rc.HornOn = horn; a.Horn(horn); }
            }
            catch (Exception e) { DrawFailed(4, "A's sound update on " + rc.CarId, e); }
        }

        private static void DoorSound(AcRemoteCar rc, AcCarAudio a, AcSide side, float value, ref float last)
        {
            if (last < 0f) { last = value; return; }       // the first sample sets the state, no sound
            bool wasOpen = last > DoorClosed, open = value > DoorClosed;
            last = value;
            if (open == wasOpen) return;
            a.Door(side, open);                              // rising from 0 = opening, back to 0 = closed
            rc.DoorSounds++;
        }

        /// <summary>A parked player car near me (or the one I ride in, when S can take the rebuilt rig) gets its high-res cockpit (one rebuild, kept).</summary>
        private static void WantCockpit(AcRemoteCar rc, Vector3 eye, Vector3 pos)
        {
            if (rc.WantInterior || rc.Interior || rc.InteriorFailed || rc.StandInOnly) return;
            AcNetPlayer p = rc.Player;
            if (p == null || p.IsAi || !p.HasPassengerSeat) return;
            bool seatedInIt = ReferenceEquals(AcLocal.SeatCar, rc.Rig);
            if (seatedInIt) { rc.WantInterior = SwapSeatedCar != null; return; }
            if (AcLocal.Seated) return;
            float parked = InteriorParkedKmh / 3.6f;
            if (rc.Velocity.sqrMagnitude > parked * parked) return;
            if ((pos - eye).sqrMagnitude > InteriorNearMetres * InteriorNearMetres) return;
            rc.WantInterior = true;
        }

        /// <summary>The record's state as P's / A's / C's telemetry: controls, lights, wheels (spin integrated from the owner's wheel angular velocity -
        /// lock-ups and wheelspin show - or, for AI cars whose blocks have no room for it, from the drawn speed and P's wheel radius; travel / skid /
        /// surface / front steer from the owner's physics), fuel (the owner's; AI: the tank's spec).</summary>
        private static AcTelemetry Telemetry(AcRemoteCar rc, in AcNetCar s, float dt)
        {
            var t = new AcTelemetry
            {
                Rpm = s.Rpm,
                Gear = s.Gear,
                Throttle = s.Throttle,
                Brake = s.Brake,
                Clutch = s.Clutch,
                SteerWheelDeg = s.SteerWheelDeg,
                BoostBar = s.BoostBar,
                Limiter = s.Limiter,
                AbsActive = s.Abs,
                TcActive = s.Tc,
                Shifting = s.Shifting,
                Lights = (AcLightState)s.Lights,
            };
            AcCarSpec spec = rc.Rig != null ? rc.Rig.Spec : null;
            if (spec != null)
            {
                t.LimiterRpm = spec.LimiterRpm;
                t.IdleRpm = spec.IdleRpm;
                t.GearCount = spec.GearRatios != null ? spec.GearRatios.Length : 0;
                t.FuelLitres = spec.FuelLitres;
            }

            if (s.HasWheelSpeed) t.FuelLitres = s.FuelLitres;      // a player record carries the owner's fuel
            Quaternion rot = Q(s.Rot);
            Vector3 vel = V(s.Vel);
            float forward = Vector3.Dot(vel, rot * Vector3.forward);
            t.SpeedKmh = vel.magnitude * 3.6f;
            for (int i = 0; i < 4; i++)
            {
                AcNetWheel nw = s.Wheel(i);
                float w;
                if (s.HasWheelSpeed) w = nw.AngularVel;
                else
                {
                    float radius = spec != null && spec.WheelRadius != null && spec.WheelRadius.Length > i && spec.WheelRadius[i] > 0.05f ? spec.WheelRadius[i] : 0.32f; // ours fallback
                    w = forward / radius;
                }

                rc.Spin[i] = Mathf.Repeat(rc.Spin[i] + w * Mathf.Rad2Deg * dt, 360f);
                t.SetWheel(i, new AcWheelState
                {
                    SpinDeg = rc.Spin[i],
                    AngularVel = w,
                    SteerDeg = i == 0 ? s.SteerLF : i == 1 ? s.SteerRF : 0f,
                    TravelM = nw.TravelM,
                    Skid = nw.Skid,
                    Contact = nw.Surface >= 0,
                    Surface = SurfaceAt(nw.Surface),
                });
            }

            return t;
        }

        private static void SetHidden(AcRemoteCar rc, bool hidden)
        {
            if (rc.Hidden == hidden) return;
            rc.Hidden = hidden;
            if (rc.Rig != null && rc.Rig.Root != null && rc.Rig.Root.activeSelf == hidden) rc.Rig.Root.SetActive(!hidden);
            if (rc.Box != null && rc.Box.activeSelf == hidden) rc.Box.SetActive(!hidden);
        }

        /// <summary>The grey box while the car builds or when it is not installed here: solid on the Car layer (kinematic, the same ghost rule as a
        /// built car), so nobody drives through a car that is still loading at a pit box or grid slot.</summary>
        private static void StandIn(AcRemoteCar rc)
        {
            if (rc.Box != null) return;
            rc.Box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rc.Box.name = rc.StandInOnly ? "stand-in (car not installed here)" : "stand-in (being built)";
            rc.Box.layer = AcLayers.Car;
            rc.Box.transform.SetParent(rc.FrameStandIn, false);
            rc.Box.transform.localScale = StandInSize;
            rc.Box.transform.localPosition = StandInCentre;
            Renderer r = rc.Box.GetComponent<Renderer>();
            Material m = StandInMaterial();
            if (r != null && m != null) r.sharedMaterial = m;
            Collider c = rc.Box.GetComponent<Collider>();         // the primitive's unit BoxCollider, scaled with the box
            if (c != null)
            {
                Rigidbody body = rc.Box.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                rc.BoxContact = rc.Box.AddComponent<AcRemoteContact>(); // fixer 2026-11-02: P reads the drawn velocity (PhysX sees none)
                rc.Colliders = new[] { c };
            }
            else rc.Colliders = null;

            rc.Ghost = false;
        }

        private static Material StandInMaterial()
        {
            if (_standInMaterial != null) return _standInMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) return null;
            _standInMaterial = new Material(s) { name = "Ac Net stand-in" };
            _standInMaterial.color = new Color(0.45f, 0.45f, 0.47f); // ours: neutral grey
            return _standInMaterial;
        }

        /// <summary>Q-A3 for racing: passes through the local car only in the pit lane / just after a placement, and only while fast relative to it.</summary>
        private static void Contacts(AcRemoteCar rc, Vector3 pos, Vector3 vel, bool remoteInPit)
        {
            // fixer 2026-11-02 (damage): the remote car's own 'special' state for cars other than the local one (the master's AI take no damage
            // from a just-placed / pit-lane car), and the stand-in box's drawn velocity (its kinematic body reads zero)
            bool remoteSpecial = remoteInPit || Time.time - rc.SnapAt < SpawnGhostSeconds;
            AcCarPhysics rph = rc.Rig != null ? rc.Rig.Physics : null;
            if (rph != null) rph.PuppetGhostable = remoteSpecial;
            AcRemoteContact box = rc.BoxContact;
            if (box != null) { box.Velocity = vel; box.Ghostable = remoteSpecial; }
            if (rc.Colliders == null) return;
            AcCarRig own = AcLocal.Car;
            Vector3 ownVel = Vector3.zero, ownPos = pos + Vector3.up * 1000f;
            try
            {
                if (own != null && own.Root != null)
                {
                    ownPos = own.Root.transform.position;
                    Rigidbody b = own.Physics != null ? own.Physics.Body : null;
                    if (b != null && !b.isKinematic) ownVel = b.linearVelocity;
                }
            }
            catch (Exception e) { DrawFailed(5, "reading the own car for contacts", e); }

            float rel = (vel - ownVel).magnitude * 3.6f;
            bool special = remoteInPit || AcLocal.InPitlane || Time.time - rc.SnapAt < SpawnGhostSeconds;
            bool overlap = (ownPos - pos).sqrMagnitude < OverlapMetres * OverlapMetres;
            bool ghost = rc.Ghost;
            if (!overlap)
            {
                if (special && rel > GhostKmh) ghost = true;
                else if (!special || rel < SolidKmh) ghost = false;
            }

            if (ghost == rc.Ghost) return;
            rc.Ghost = ghost;
            int carBit = 1 << AcLayers.Car;
            for (int i = 0; i < rc.Colliders.Length; i++)
            {
                Collider c = rc.Colliders[i];
                if (c == null) continue;
                c.excludeLayers = ghost ? (c.excludeLayers | carBit) : (c.excludeLayers & ~carBit);
            }
        }

        private static Vector3 EyePosition()
        {
            try
            {
                Camera cam = AcPlayer.Head();
                if (cam != null) return cam.transform.position;
            }
            catch (Exception) { }

            return AcLocal.WorldPosition;
        }

        // ------------------------------------------------------------------ dropping

        private static void DropCarOf(AcNetPlayer p, string why)
        {
            if (p != null && p.Car != null) DropCar(p.Car, why);
        }

        private static void DropCar(AcRemoteCar rc, string why)
        {
            if (rc == null || rc.Disposed) return;
            rc.Disposed = true;
            CarList.Remove(rc);
            if (rc.Player != null && ReferenceEquals(rc.Player.Car, rc)) rc.Player.Car = null;

            try
            {
                if (rc.Rig != null)
                {
                    // the local passenger riding it gets out first (S's ForceExit releases every Bridge lock)
                    if (ReferenceEquals(AcLocal.SeatCar, rc.Rig)) AcGame.Guard("seat force exit", AcSeat.ForceExit);
                    rc.Rig.Dispose();
                }
            }
            catch (Exception e)
            {
                AcLog.Warn("Net: dropping a remote car failed: " + e.Message);
            }

            try { if (rc.BoxAudio != null) rc.BoxAudio.Dispose(); }
            catch (Exception e) { AcLog.WarnOnce("Net: disposing stand-in audio failed: " + e.Message); }
            if (rc.Holder != null) UnityEngine.Object.Destroy(rc.Holder);
            rc.Rig = null;
            rc.Holder = null;
            rc.Box = null;
            rc.Colliders = null;
            // a build still running for it ends in Finish (rc.Disposed -> the rig it made is destroyed, the build queue is freed there)
        }

        private static void ClearCars(string why)
        {
            for (int i = CarList.Count - 1; i >= 0; i--) DropCar(CarList[i], why);
            CarList.Clear();
            // _building stays until that coroutine reports back (Finish), so a new build never overlaps the dropped one (BuildStuckSeconds frees it)
        }
    }
}
