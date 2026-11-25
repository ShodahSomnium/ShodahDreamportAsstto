using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [I, AI drivers] The other cars as the AI sees them, collected once a frame by <see cref="AcAi.Tick"/>: every AI car, the local player's
    /// car and every remote player's car, each projected on the fast lane (arc length + lateral offset). Per AI driver <see cref="Decide"/>
    /// picks the overtaking offset (the side with room between the car ahead and the track edge, inside the line's SideLeft / SideRight),
    /// a follow speed when there is no room (bumper-to-bumper gaps), keeps clear of a car alongside, never approaches ANY car in its current
    /// lane faster than it can stop short of it (<c>LaneGuard</c>, also while pulling out to pass - the grid start), brakes at least as hard
    /// as that needs (<c>AcAiBrain.FollowDecel</c>), and adds an emergency brake (also when slow, along the nose) from a sphere cast on the Car layer
    /// (28) - so an AI never drives through the local player or a remote car (they are kinematic puppets with no velocity of their own).
    /// No allocation per frame after the first frames (arrays and the hint map are reused).
    /// </summary>
    public static class AcAiTraffic
    {
        public const int MaxCars = 80;

        private struct Car
        {
            public AcCarRig Rig;
            public Vector3 Pos;
            public float Speed;      // m/s along its heading
            public float S, Lat;     // on the fast lane
            public bool OnLine;      // projected (fast lane exists)
            public bool InPits;      // in the pit lane (an AI on its pit line, a player whose record / AcLocal says so)
            public float HalfWidth, HalfLength;
            public int AiIndex;      // -1 = not an AI
        }

        private static readonly Car[] Cars = new Car[MaxCars];
        private static int _count;
        private static AcAiTrack _track;
        private static readonly Dictionary<AcCarRig, int> Hints = new Dictionary<AcCarRig, int>();
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>Look-ahead for traffic decisions (m) = 25 + 1.6 s x speed. // ours</summary>
        public const float AheadBase = 25f, AheadSeconds = 1.6f;
        /// <summary>Lateral clearance kept to another car (m). // ours</summary>
        public const float Clearance = 0.45f;
        /// <summary>Time gap kept behind a car that cannot be passed (s) + metres BUMPER TO BUMPER (last-gaps pass: was 4 m centre to
        /// centre = touching for two 4.5 m cars at low speed). // ours</summary>
        public const float FollowGapSeconds = 0.45f, FollowGapMetres = 2f;
        /// <summary>Emergency deceleration assumed for the forward cast (m/s^2). // ours</summary>
        public const float EmergencyDecel = 7f;
        /// <summary>Car in my lane ahead: approach only as fast as I can still stop <see cref="StopShort"/> m behind it braking at
        /// <see cref="TrafficDecel"/> (also while moving out to pass it); a pass in progress may still crawl at <see cref="CrawlSpeed"/>
        /// over its speed so the move can finish; <see cref="BodyMargin"/> = lateral margin of "in my lane" (half widths + it). // ours</summary>
        public const float TrafficDecel = 5f, StopShort = 1f, CrawlSpeed = 2f, BodyMargin = 0.25f;
        /// <summary>Time (s) the car needs on top of the offset slew to follow a lane change: MoveLag + MovePreview x (pure-pursuit preview
        /// distance / speed) - slow, the car turns its lateral offset in far more slowly (lab run 2: from the grid it reached the car ahead
        /// with the offset moved 1.3 m but the car only ~0.5 m). // ours</summary>
        public const float MoveLag = 0.3f, MovePreview = 0.5f;

        /// <summary>Collects every car once a frame (master, from AcAi.Tick).</summary>
        public static void BeginFrame(AcAiTrack fast, IReadOnlyList<AcAiDriver> drivers)
        {
            _track = fast != null && !fast.IsEmpty ? fast : null;
            _count = 0;
            for (int i = 0; i < drivers.Count; i++)
            {
                AcAiDriver d = drivers[i];
                if (d == null || d.Rig == null || d.Rig.Root == null) continue;
                Add(d.Rig, d.Index, d);
                Cars[_count - 1].InPits = d.OnPitLine;
            }
            AcCarRig mine = AcLocal.Car;
            if (mine != null && mine.Root != null) { Add(mine, -1, null); Cars[_count - 1].InPits = AcLocal.InPitlane; }
            try
            {
                var remote = AcNetGame.RemoteCars;
                for (int i = 0; i < remote.Count && _count < MaxCars; i++)
                {
                    // N: a car still building / not installed here is a solid stand-in box - test Drawn + Frame, not only Rig
                    // ("N -> R, I: stand-in boxes are solid now"); hidden cars beyond the draw radius are still posed, so they count too
                    AcRemoteCar rc = remote[i];
                    if (rc == null || !rc.Drawn) continue;
                    Transform fr = rc.Frame;
                    if (fr == null) continue;
                    if (rc.Rig != null && rc.Rig.Root != null) Add(rc.Rig, -1, null);
                    else AddFrame(fr);
                    Cars[_count - 1].Speed = rc.Shown.SpeedKmh / 3.6f;
                    Cars[_count - 1].InPits = rc.Player != null && rc.Player.InPitlane;
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Ai: reading remote cars failed: " + e.Message); }
            CollectWalkers();
            if (++_pruneFrames >= 600) { _pruneFrames = 0; PruneHints(); } // ours: every ~10 s
        }

        // ---- people on foot (the pit lane walk to the cars - DESIGN flow): the local player when not seated, remote players not in a car
        private static readonly Vector3[] Walkers = new Vector3[64];
        private static int _walkers, _pruneFrames;
        private static readonly List<AcCarRig> Dead = new List<AcCarRig>(8);

        private static void CollectWalkers()
        {
            _walkers = 0;
            try
            {
                Vector3 feet;
                if (!AcLocal.Seated && AcPlayer.TryFeet(out feet)) Walkers[_walkers++] = feet;
            }
            catch (Exception e) { AcLog.WarnOnce("Ai: reading the local player's position failed: " + e.Message); }
            try
            {
                IReadOnlyList<AcRemotes.Remote> all = AcRemotes.All;
                for (int i = 0; i < all.Count && _walkers < Walkers.Length; i++)
                {
                    AcRemotes.Remote r = all[i];
                    if (r == null || r.Root == null) continue;
                    AcNetPlayer np = AcNetGame.FindByPhotonId(r.Id);
                    if (np == null || !np.OnTrack) continue;                               // not a Dreamport player on this track
                    if (np.Seated || np.Driving || np.RideInPhotonId >= 0) continue;        // in a car: the car is what counts
                    Walkers[_walkers++] = r.Root.position;
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Ai: reading remote players' positions failed: " + e.Message); }
        }

        /// <summary>Drops projection hints of rigs that are gone (remote cars that left) - the map would grow over a long session.</summary>
        private static void PruneHints()
        {
            Dead.Clear();
            foreach (KeyValuePair<AcCarRig, int> kv in Hints) if (kv.Key == null || kv.Key.Root == null) Dead.Add(kv.Key);
            for (int i = 0; i < Dead.Count; i++) Hints.Remove(Dead[i]);
            Dead.Clear();
        }

        /// <summary>
        /// Is the fast lane clear at arc length <paramref name="fastS"/> for joining or being put down there: no car on the fast lane (not in the
        /// pits, not <paramref name="except"/>) within 8 m past it or arriving from behind within <paramref name="seconds"/> s (+10 m). // ours
        /// </summary>
        public static bool MergeClear(AcCarRig except, float fastS, float seconds)
        {
            if (_track == null || fastS < 0f) return true;
            for (int i = 0; i < _count; i++)
            {
                if (!Cars[i].OnLine || Cars[i].InPits || ReferenceEquals(Cars[i].Rig, except)) continue;
                float behind = _track.Delta(Cars[i].S, fastS); // + = the car is behind the point
                float v = Math.Max(0f, Cars[i].Speed);
                if (behind > -8f && behind < 10f + v * seconds) return false;
            }
            return true;
        }

        private static void Add(AcCarRig rig, int aiIndex, AcAiDriver d)
        {
            if (_count >= MaxCars) return;
            ref Car c = ref Cars[_count++];
            c.Rig = rig; c.AiIndex = aiIndex;
            AcCarPhysics ph = rig.Physics;
            Transform t = rig.Root.transform;
            if (ph != null && ph.Body != null)
            {
                c.Pos = ph.Body.position;
                Vector3 v = ph.Body.isKinematic ? Vector3.zero : ph.Body.linearVelocity;
                c.Speed = Vector3.Dot(v, t.forward);
                if (ph.Body.isKinematic) c.Speed = ph.Telemetry.SpeedKmh / 3.6f;
            }
            else { c.Pos = t.position; c.Speed = d != null ? d.RailsSpeed : 0f; }
            Sizes(rig, out c.HalfWidth, out c.HalfLength);
            c.OnLine = false; c.S = 0f; c.Lat = 0f;
            if (_track == null) return;
            if (d != null && !d.OnPitLine && d.Brain != null && d.Brain.Track == _track && d.Brain.Hint >= 0)
            {
                c.S = d.Brain.S; c.Lat = d.Brain.Lateral; c.OnLine = true; return;
            }
            float x, y, z;
            AcTrackWorld.ToAc(c.Pos, out x, out y, out z);
            int hint;
            if (!Hints.TryGetValue(rig, out hint)) hint = -1;
            c.S = _track.Project(x, y, z, ref hint, out c.Lat);
            Hints[rig] = hint;
            c.OnLine = true;
        }

        /// <summary>A remote car drawn as N's stand-in box (no rig yet): its frame, default size, projected without a cached hint.</summary>
        private static void AddFrame(Transform fr)
        {
            if (_count >= MaxCars) return;
            ref Car c = ref Cars[_count++];
            c.Rig = null; c.AiIndex = -1; c.InPits = false;
            c.Pos = fr.position; c.Speed = 0f;
            c.HalfWidth = 0.95f; c.HalfLength = 2.2f; // ours defaults
            c.OnLine = false; c.S = 0f; c.Lat = 0f;
            if (_track == null) return;
            float x, y, z;
            AcTrackWorld.ToAc(c.Pos, out x, out y, out z);
            int hint = -1;
            c.S = _track.Project(x, y, z, ref hint, out c.Lat);
            c.OnLine = true;
        }

        private static void Sizes(AcCarRig rig, out float hw, out float hl)
        {
            hw = 0.95f; hl = 2.2f; // ours defaults
            try
            {
                if (rig.Model != null)
                {
                    Bounds b = rig.Model.Bounds;
                    if (b.extents.x > 0.4f && b.extents.x < 1.6f) hw = b.extents.x;
                    if (b.extents.z > 1f && b.extents.z < 4f) hl = b.extents.z;
                }
            }
            catch (Exception) { }
        }

        /// <summary>Forget everything (track unloaded / AI cleared).</summary>
        public static void Reset() { _count = 0; _track = null; Hints.Clear(); }

        /// <summary>Is any car (other than <paramref name="except"/>) within <paramref name="radius"/> m (flat) of <paramref name="at"/>?</summary>
        public static bool Occupied(Vector3 at, AcCarRig except, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(Cars[i].Rig, except)) continue;
                float dx = Cars[i].Pos.x - at.x, dz = Cars[i].Pos.z - at.z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>
        /// Sets <c>d.Brain.OffsetTarget</c> and <c>d.Brain.FollowCap</c> for this frame from the cars around (see the class comment).
        /// </summary>
        public static void Decide(AcAiDriver d, Vector3 pos, Vector3 vel)
        {
            AcAiBrain br = d.Brain;
            br.FollowCap = float.PositiveInfinity;
            br.FollowDecel = 0f;
            d.Blocked = false;
            float myV = vel.magnitude;
            int me = -1;
            for (int i = 0; i < _count; i++) if (ReferenceEquals(Cars[i].Rig, d.Rig)) { me = i; break; }
            if (_track != null && me >= 0 && Cars[me].OnLine && !d.OnPitLine) Lanes(d, me, myV);
            else if (!d.OnPitLine) br.OffsetTarget = 0f;
            Emergency(d, pos, vel, myV);
        }

        private static void Lanes(AcAiDriver d, int me, float myV)
        {
            AcAiBrain br = d.Brain;
            Car m = Cars[me];
            float ahead = AheadBase + AheadSeconds * myV;
            int front = -1; float frontDs = float.MaxValue;
            float sideMin = float.NegativeInfinity, sideMax = float.PositiveInfinity; // allowed offset band from cars alongside
            for (int i = 0; i < _count; i++)
            {
                if (i == me || !Cars[i].OnLine) continue;
                Car o = Cars[i];
                float ds = _track.Delta(m.S, o.S);
                float dLat = o.Lat - m.Lat;
                float overlapLen = m.HalfLength + o.HalfLength + 1f; // ours: 1 m
                float overlapLat = m.HalfWidth + o.HalfWidth + Clearance;
                if (Math.Abs(ds) < overlapLen)
                {
                    // alongside: stay on my side of it
                    if (Math.Abs(dLat) < overlapLat + 1.5f)
                    {
                        if (dLat > 0f) sideMax = Math.Min(sideMax, o.Lat - overlapLat);
                        else sideMin = Math.Max(sideMin, o.Lat + overlapLat);
                    }
                    if (ds > 0f && Math.Abs(dLat) < overlapLat && ds < frontDs) { front = i; frontDs = ds; }
                    continue;
                }
                if (ds <= 0f || ds > ahead) continue;
                // in my path (now or where my line goes)?
                float myLatThere = br.OffsetTarget;
                if (Math.Abs(o.Lat - myLatThere) < overlapLat + 0.6f || Math.Abs(dLat) < overlapLat)
                    if (ds < frontDs) { front = i; frontDs = ds; }
            }

            float target = 0f;
            bool keepSide = Time.time < d.PassUntil;
            if (front >= 0)
            {
                Car o = Cars[front];
                float planHere = br.Plan != null ? br.Plan.SpeedAt(o.S) : myV;
                bool faster = planHere > o.Speed + 0.7f || myV > o.Speed + 1.2f; // ours
                float closing = Math.Max(0f, myV - o.Speed);
                int i0 = _track.IndexAt(o.S);
                float sl = _track.SideL[i0], sr = _track.SideR[i0];
                float need = 2f * m.HalfWidth + Clearance * 2f;
                float roomL = sl - (o.Lat + o.HalfWidth), roomR = (o.Lat - o.HalfWidth) + sr;
                bool passing = false;
                // commit distance; a standing / crawling car (the grid, a stalled car) is always passed when there is room - from a
                // standstill the old 10 m never came, so the AI only moved out once it was nearly on top of it // ours: 3 m/s
                if (faster && (o.Speed < 3f || frontDs < 10f + closing * 2.5f + myV * 0.5f)) // ours: commit distance
                {
                    int side = d.PassSide;
                    if (!keepSide || side == 0)
                    {
                        // the side with more room; on a tie the inside of the next corner (AC's line has the apex there)
                        side = roomL >= roomR ? 1 : -1;
                        if (Math.Abs(roomL - roomR) < 0.5f) side = _track.Curv[i0] >= 0f ? 1 : -1;
                    }
                    float room = side > 0 ? roomL : roomR;
                    if (room >= need)
                    {
                        target = side > 0 ? o.Lat + o.HalfWidth + Clearance + m.HalfWidth : o.Lat - o.HalfWidth - Clearance - m.HalfWidth;
                        passing = true;
                        if (side != d.PassSide || !keepSide) { d.PassSide = side; d.PassUntil = Time.time + 1.2f; } // ours: hold the side 1.2 s
                    }
                }
                if (!passing && keepSide && d.PassSide != 0) target = br.OffsetTarget; // mid-move: hold the lane briefly
                // never into a car alongside
                if (target > sideMax) target = sideMax;
                if (target < sideMin) target = sideMin;
                if (float.IsInfinity(target) || float.IsNaN(target)) target = 0f;
                // last-gaps pass (REQUESTS "lead-review -> I": an AI 458 drove into the standing pole car at 7 km/h after the green):
                // a pass whose lane (after the alongside clamp) still runs into the car is no pass; and the gaps are BUMPER to bumper
                if (passing && Math.Abs(target - o.Lat) < m.HalfWidth + o.HalfWidth + BodyMargin) passing = false;
                if (!passing)
                {
                    // follow: keep a time gap; very close -> a bit slower than the car ahead (LaneGuard adds the stopping limit)
                    float gapB = frontDs - m.HalfLength - o.HalfLength;
                    float gap = FollowGapMetres + FollowGapSeconds * myV;
                    float cap = o.Speed + (gapB - gap) * 0.6f; // ours: gap gain
                    if (gapB < StopShort + 0.5f) cap = Math.Min(cap, o.Speed - 2f);
                    br.FollowCap = Math.Max(0f, cap);
                }
            }
            else
            {
                if (keepSide && d.PassSide != 0) target = br.OffsetTarget;
                else d.PassSide = 0;
                if (target > sideMax) target = sideMax;
                if (target < sideMin) target = sideMin;
                if (float.IsInfinity(target) || float.IsNaN(target)) target = 0f;
            }
            br.OffsetTarget = target;
            LaneGuard(d, me, myV, target, ahead);
        }

        /// <summary>
        /// Last-gaps pass: EVERY car ahead in my lane now (lateral overlap at my current offset + <see cref="BodyMargin"/>), not only the one
        /// <see cref="Lanes"/> picked as "front" (on the grid the AI behind the standing pole car picked the moving car beside it once its pass
        /// target pointed there, and the standing car ahead was not looked at any more): approach only as fast as I can still stop
        /// <see cref="StopShort"/> m behind it at <see cref="TrafficDecel"/>; when my target lane clears it (a pass / pulling out) a crawl at
        /// <see cref="CrawlSpeed"/> over its speed is allowed while there is room, so the move can finish; over the limit -> FollowDecel.
        /// </summary>
        private static void LaneGuard(AcAiDriver d, int me, float myV, float target, float ahead)
        {
            AcAiBrain br = d.Brain;
            Car m = Cars[me];
            for (int i = 0; i < _count; i++)
            {
                if (i == me || !Cars[i].OnLine || Cars[i].InPits) continue;
                Car o = Cars[i];
                float ds = _track.Delta(m.S, o.S);
                if (ds <= 0f || ds > ahead) continue;
                float bodyLat = m.HalfWidth + o.HalfWidth + BodyMargin;
                if (Math.Abs(o.Lat - m.Lat) >= bodyLat) continue;
                float gapB = ds - m.HalfLength - o.HalfLength; // bumper to bumper along the line
                float ov = Math.Max(0f, o.Speed);
                float free = gapB - StopShort;
                float cap = ov + Mathf.Sqrt(2f * TrafficDecel * Math.Max(0f, free));
                if (Math.Abs(target - o.Lat) >= bodyLat)
                {
                    // my lane is moving out of its way: approach only as fast as lets the move end before I get there (the offset
                    // slews at AcAiBrain.OffsetRate, the car follows it with a lag) - a pass at speed is not braked for (110 m at
                    // 55 m/s: ~65 m/s allowed), from the grid it holds the car back so the move finishes; then the crawl
                    float shift = bodyLat - Math.Abs(o.Lat - m.Lat);
                    float tMove = shift / AcAiBrain.OffsetRate + MoveLag + MovePreview * br.PreviewDistance(myV) / Math.Max(2f, myV); // ours: 2 m/s floor
                    cap = ov + Math.Max(0f, free) / tMove;
                    if (gapB > StopShort + 0.3f && cap < ov + CrawlSpeed) cap = ov + CrawlSpeed; // ours: 0.3 m
                }
                if (cap < br.FollowCap) br.FollowCap = cap;
                if (myV > cap) NeedDecel(br, myV - ov, gapB - StopShort * 0.5f);
                // standing behind a standing car with no way to creep on: let the recovery back off and steer round (AcAiDriver.Recover)
                if (cap < 0.5f && ov < 0.5f && myV < 0.5f) d.Blocked = true; // ours: 0.5 m/s
            }
        }

        /// <summary>Closing at <paramref name="closing"/> m/s on something <paramref name="free"/> m ahead: the brain must brake at least
        /// closing^2 / (2 free) (kept as the frame's maximum in <c>FollowDecel</c>).</summary>
        private static void NeedDecel(AcAiBrain br, float closing, float free)
        {
            if (closing <= 0.3f) return; // ours: 0.3 m/s
            float a = closing * closing / (2f * Math.Max(0.2f, free)); // ours: 0.2 m floor
            if (a > br.FollowDecel) br.FollowDecel = a;
        }

        /// <summary>People on foot: an AI stops this far (m) short of a person in its path (half width + <see cref="WalkerSide"/> m each side). // ours</summary>
        public const float WalkerGap = 3f, WalkerSide = 1.2f;

        private static void Emergency(AcAiDriver d, Vector3 pos, Vector3 vel, float myV)
        {
            Walk(d, pos, vel, myV);
            if (d.Brain.Reverse) return;
            // last-gaps pass: also when slow / pulling away (the grid) - was skipped under 2 m/s; the car's nose decides then
            Vector3 dir;
            if (myV >= 2f) dir = vel / myV;
            else
            {
                dir = d.Rig.Root.transform.forward; dir.y = 0f;
                float dl = dir.magnitude;
                if (dl < 1e-3f) return;
                dir /= dl;
            }
            float reach = 4f + myV * 1.1f; // ours: 1.1 s of travel
            Vector3 start = pos + dir * (d.HalfLength + 0.4f) + Vector3.up * 0.4f;
            int n = Physics.SphereCastNonAlloc(start, 0.75f, dir, Hits, reach, 1 << AcLayers.Car, QueryTriggerInteraction.Ignore); // ours: radius
            Rigidbody own = d.Rig.Physics != null ? d.Rig.Physics.Body : null;
            float best = float.MaxValue, otherV = 0f;
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (c == null) continue;
                Rigidbody rb = c.attachedRigidbody;
                if (own != null && rb == own) continue;
                if (c.transform.IsChildOf(d.Rig.Root.transform)) continue;
                if (Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                // its speed along my way: a dynamic body has its own velocity; a kinematic puppet (remote car) the speed from the list
                otherV = 0f;
                if (rb != null && !rb.isKinematic) otherV = Vector3.Dot(rb.linearVelocity, dir);
                else for (int k = 0; k < _count; k++) if (Cars[k].Rig != null && Cars[k].Rig.Root != null && c.transform.IsChildOf(Cars[k].Rig.Root.transform)) { otherV = Cars[k].Speed; break; }
                if (otherV < 0f) otherV = 0f;
            }
            if (best == float.MaxValue) return;
            float free = Math.Max(0f, best - 1f); // ours: stop 1 m short (+ the 0.4 m the cast starts ahead of the nose)
            float cap = otherV + Mathf.Sqrt(2f * EmergencyDecel * free); // relative stopping distance
            if (cap < d.Brain.FollowCap) d.Brain.FollowCap = cap;
            // over it: brake as hard as stopping short needs (the speed error alone brakes softly at low speed)
            if (myV > cap) NeedDecel(d.Brain, myV - otherV, best - 0.3f); // ours: 0.3 m hard line
            if (cap < 0.5f && otherV < 0.5f && myV < 0.5f) d.Blocked = true; // nose against a standing car: Recover backs off
        }

        /// <summary>Brakes for people walking in the AI's path (also when standing / pulling out of a pit box: the car's nose decides).</summary>
        private static void Walk(AcAiDriver d, Vector3 pos, Vector3 vel, float myV)
        {
            if (_walkers == 0) return;
            Vector3 dir;
            if (myV >= 2f) dir = vel / myV;
            else { dir = d.Rig.Root.transform.forward; if (d.Brain.Reverse) dir = -dir; }
            dir.y = 0f;
            float dl = dir.magnitude;
            if (dl < 1e-3f) return;
            dir /= dl;
            float reach = d.HalfLength + WalkerGap + 6f + myV * 1.6f; // ours: 1.6 s of travel
            float half = d.Brain.HalfWidth + WalkerSide;
            for (int i = 0; i < _walkers; i++)
            {
                Vector3 rel = Walkers[i] - pos;
                if (Math.Abs(rel.y) > 3f) continue; // ours: another level (hub, bridge)
                rel.y = 0f;
                float ahead = Vector3.Dot(rel, dir);
                if (ahead < 0f || ahead > reach) continue;
                float side = (rel - dir * ahead).magnitude;
                if (side > half) continue;
                float free = Math.Max(0f, ahead - d.HalfLength - WalkerGap);
                float cap = Mathf.Sqrt(2f * EmergencyDecel * free);
                if (cap < 0.5f) cap = 0f;
                if (cap < d.Brain.FollowCap) d.Brain.FollowCap = cap;
            }
        }
    }
}
