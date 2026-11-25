using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [N, Net] Lab scenarios of the net layer over an in-memory loopback of the 69 scene blocks (Fusion's rules reduced: AcLoopbackWorld):
    /// A = the local player (the real AcGame with its AcNetGame session, player id 1), B = a scripted AcNet session (id 2) whose car drives the loaded
    /// track's AI line, C = a late joiner (id 3). Run: bash DevTools/lab.sh net-loopback [-actrack magione] [-accar abarth500].
    ///
    /// net-loopback checks: both claim slots (1 and 2), TryHasRecord both ways (the isolation input), A is the master (lowest id), the race command
    /// A publishes through RaceControl reaches B (track hash, Seq), the track loads (R), B's car is drawn on A's screen (stand-in first, then the real
    /// model + skin through P's AcCarRig.Spawn Kind Remote) following B's scripted path within speed x 0.25 s + 1 m, the rider record puts B's
    /// (absent) body nowhere (no Bridge player in the editor: logged, not placed), C joins late and reads the running command, B asks for A's passenger
    /// seat (granted / no seat); then (second pass) B's door open + close and horn reach A's audio (Door x2, Horn on/off), the ghost rule (B fast in
    /// the pit lane: see-through, Car bit in excludeLayers; out of it: solid), B 2 km away is hidden and shown again, B parked within 15 m of my
    /// eye is rebuilt with its cockpit (net_remote_cockpit.png), B becomes the master through the room bit and writes one AI car that A lists and
    /// draws as a remote car with its full names.txt name (net_ai_car.png), A takes over again (keeps the inherited AI block, next command Seq
    /// higher); B leaves: its car and player entry go, A stays master. Pictures: net_standin.png, net_remote_car.png, net_remote_close.png,
    /// net_remote_cockpit.png, net_ai_car.png in DevTools/lab/net-loopback/.
    /// </summary>
    [InitializeOnLoad]
    static class AcLabNet
    {
        static AcLoopbackWorld _world;

        static AcLabNet()
        {
            AcLabRunner.Register("net-loopback", r => Loopback(r));
            string s = AcLabBoot.Arg("-aclab", null);
            if (s != null && s.StartsWith("net"))
            {
                // before the Dreamport starts (this runs again after the play-mode domain reload): the loopback replaces the Fusion session, A is player 1
                _world = new AcLoopbackWorld();
                AcNetGame.TransportFactory = () => _world.Join(1, Clock);
                AcNetGame.LabNames = id => id == 2 ? "Bravo (B)" : id == 3 ? "Charlie (C)" : null;
            }
        }

        static double Clock() { return Time.unscaledTimeAsDouble; }

        sealed class Scripted
        {
            public AcNet Net;
            public int Ai;          // index on the AI line
            public float Along;     // metres travelled
            public Vector3 Pos;
            public Quaternion Rot = Quaternion.identity;
            public Vector3 Vel;
            public bool Driving;
        }

        static Scripted Join(int id)
        {
            var s = new Scripted { Net = new AcNet(_world.Join(id, Clock), Clock, " lab#" + id) };
            s.Net.Own.Status = AcNetStatus.Present;
            return s;
        }

        static IEnumerator Loopback(AcLabRunner r)
        {
            if (_world == null) { r.Fail("the loopback was not installed (run with -aclab net-loopback)"); yield break; }
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 120f, "the catalog");
            if (r.Failed) yield break;
            Scripted B = Join(2);
            Scripted C = null;
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until) { B.Net.Tick(); yield return null; }
            r.Check(AcNetGame.Online && AcNetGame.Claimed && AcLocal.NetSlot == 1, "A online, claimed slot " + AcLocal.NetSlot + " (first try = id 1 % 50)");
            r.Check(B.Net.Claimed && B.Net.MySlot == 2, "B claimed slot " + B.Net.MySlot);
            bool known;
            r.Check(AcNetGame.TryHasRecord(2, out known) && known && B.Net.HasRecord(1), "TryHasRecord: A sees B's record and B sees A's (isolation input), known=" + known);
            r.Check(AcNetGame.IsMaster && AcNetGame.MasterKnown && B.Net.MasterId == 1, "A (id 1) is the instance master on both: '" + AcNetGame.MasterName + "' / B says " + B.Net.MasterId);
            r.Check(AcNetGame.Players.Count == 1 && AcNetGame.Players[0].Name == "Bravo (B)", "A lists B ('" + (AcNetGame.Players.Count > 0 ? AcNetGame.Players[0].Name : "-") + "')");

            // the car and the track
            AcContent content = r.Game.Content;
            string carId = AcLabBoot.Arg("-accar", null);
            AcCarEntry car = carId != null ? content.Car(carId) : null;
            if (car == null) foreach (AcCarEntry c in content.Cars) if (c.Owned && c.Skins.Count > 0) { car = c; break; }
            string trackId = AcLabBoot.Arg("-actrack", "magione");
            AcTrackEntry track = content.Track(trackId);
            AcLayoutEntry layout = track != null && track.Layouts.Count > 0 ? track.Layouts[0] : null;
            if (!r.Check(car != null && layout != null && layout.Owned, "car " + (car != null ? car.Id : "-") + ", layout " + (layout != null ? layout.Key : trackId))) yield break;

            int seqBefore = AcNetGame.RaceControl.Current != null ? AcNetGame.RaceControl.Current.Seq : 0;
            bool requested = false;
            try { requested = AcRace.RequestLoad(layout, new AcRules()); }
            catch (System.Exception e) { r.Fail("AcRace.RequestLoad threw " + e.Message); }
            if (!requested)
            {
                // R not ready: publish the command ourselves to test the wire anyway
                var cmd = new AcRaceCommand { Phase = AcPhase.Loading, TrackId = layout.TrackId, LayoutId = layout.Id, Rules = new AcRules() };
                r.Check(AcNetGame.RaceControl.Publish(cmd), "RaceControl.Publish as master (R's RequestLoad refused: " + requested + ")");
            }

            until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until) { B.Net.Tick(); yield return null; }
            AcNetCommand bc;
            bool got = B.Net.TryGetCommand(out bc);
            r.Check(got && bc.Seq > seqBefore && bc.TrackHash == AcNetWire.NameHash(layout.TrackId), "B reads A's race command (seq " + (got ? bc.Seq : 0) + ", track " + layout.TrackId + ")");

            float loadUntil = Time.realtimeSinceStartup + 420f;
            while (Time.realtimeSinceStartup < loadUntil && !(AcSession.OnTrack && AcTrackWorld.Loaded)) { B.Net.Tick(); yield return null; }
            if (!r.Check(AcSession.OnTrack, "the track is loaded (phase " + AcSession.Phase + ")")) yield break;

            // B drives the AI line
            B.Net.Own.Status = AcNetStatus.Playing;
            B.Net.Own.OnTrack = true;
            B.Net.Own.Seated = B.Net.Own.Driving = true;
            AcSkinEntry skin = car.Skins.Count > 0 ? car.Skins[0] : null;
            var own = AcNetCar.Empty();
            own.Present = true;
            own.CarHash = AcNetWire.NameHash(car.Id);
            own.SkinHash = skin != null ? AcNetWire.NameHash(skin.Id) : 0;
            own.LayoutHash = AcNetWire.NameHash(layout.Key);
            own.Epoch = 1;
            B.Net.Own.Car = own;
            B.Net.Own.Rider = new AcNetRider { Target = AcNetWire.TargetPlayer(2), Seat = 0, Offset = new AcNetV3(0.37f, 0.1f, -0.3f) };
            Place(B, 0f, 0f);
            bool sawStandIn = false;
            float buildUntil = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < buildUntil)
            {
                Drive(B, 20f);   // // ours: 72 km/h scripted speed
                B.Net.Tick();
                AcRemoteCar rc = AcNetGame.RemoteCars.Count > 0 ? AcNetGame.RemoteCars[0] : null;
                if (rc != null && rc.Rig == null && rc.Drawn && !sawStandIn)
                {
                    sawStandIn = true;
                    Vector3 f = rc.Frame.position;
                    r.Aim(f + rc.Frame.rotation * new Vector3(4f, 3f, -9f), f, 60f);
                    r.Shot("net_standin");
                }

                if (rc != null && rc.Rig != null && rc.Drawn) break;
                yield return null;
            }

            AcRemoteCar remote = AcNetGame.RemoteCars.Count > 0 ? AcNetGame.RemoteCars[0] : null;
            r.Check(remote != null, "A draws a remote car for B (" + AcNetGame.RemoteCars.Count + ")");
            r.Check(remote != null && remote.Rig != null, "B's car built through AcCarRig.Spawn(Kind Remote): " + (remote != null && remote.Rig != null ? remote.Rig.Root.name : "not built")
                                                         + (remote != null && remote.StandInOnly ? " (stand-in only)" : ""));
            float worst = 0f;
            for (int f = 0; f < 240; f++)
            {
                Drive(B, 20f);
                B.Net.Tick();
                yield return null;
                if (remote != null && remote.Drawn && f > 60) worst = Mathf.Max(worst, Vector3.Distance(remote.Frame.position, B.Pos));
            }

            r.Check(remote != null && worst < 20f * 0.25f + 1f, "B's drawn car follows its path: worst " + worst.ToString("0.00") + " m behind / off at 20 m/s (render delay ~1 send interval)");
            if (remote != null)
            {
                Transform fr = remote.Frame;
                r.Aim(fr.position + fr.rotation * new Vector3(5f, 2.5f, -10f), fr.position + Vector3.up * 0.5f, 55f);
                r.Shot("net_remote_car");
                r.Aim(fr.position + fr.rotation * new Vector3(2.5f, 1.4f, 3.5f), fr.position + Vector3.up * 0.6f, 55f);
                r.Shot("net_remote_close");
                AcTelemetry t = remote.Shown;
                r.Check(t.SpeedKmh > 60f && t.SpeedKmh < 85f, "the remote telemetry fed to the audio / cockpit says " + t.SpeedKmh.ToString("0") + " km/h");
                // flow pass 2026-11-02 (damage, protocol 3): at DAMAGE 0 both records carry 0 0 and nothing is applied
                AcNet.Remote aOnB0 = B.Net.RemoteOfPlayer(1);
                r.Check(aOnB0 != null && aOnB0.State.Car.DamageD0 == 0 && aOnB0.State.Car.DamageD1 == 0 && remote.DamageEvents == 0 && remote.Rig != null && remote.Rig.Physics.Damage.Pristine,
                        "DAMAGE 0: A's record says 0 0 to B, B's car on A pristine, no CarDamaged (" + remote.DamageEvents + ")");
            }

            // a late joiner
            C = Join(3);
            until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until) { Drive(B, 20f); B.Net.Tick(); C.Net.Tick(); yield return null; }
            AcNetCommand cc;
            r.Check(C.Net.Claimed && C.Net.TryGetCommand(out cc) && cc.Seq == bc.Seq, "late joiner C claimed slot " + C.Net.MySlot + " and reads the running command");
            r.Check(AcNetGame.Players.Count == 2, "A lists B and C (" + AcNetGame.Players.Count + ")");

            // B asks for A's passenger seat
            if (AcLocal.Car != null)
            {
                B.Net.BeginSeatRequest(1, 1);
                AcNet.SeatAnswer ans = AcNet.SeatAnswer.Waiting;
                until = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < until && ans == AcNet.SeatAnswer.Waiting)
                {
                    B.Net.Tick(); C.Net.Tick();
                    AcNet.SeatAnswer a = B.Net.TakeSeatAnswer();
                    if (a != AcNet.SeatAnswer.None) ans = a;
                    yield return null;
                }

                bool hasSeat = AcLocal.Car.Model != null && AcLocal.Car.Model.HasPassengerSeat;
                r.Check(hasSeat ? ans == AcNet.SeatAnswer.Granted : ans == AcNet.SeatAnswer.NoSeat, "B asks for A's passenger seat: " + ans + " (A's car has a passenger seat: " + hasSeat + ")");
            }
            else LabLog("A has no car of its own (R did not spawn one): seat request skipped");

            // ---- flow pass 2026-11-02: damage over the wire (DAMAGE_DESIGN 7) - master A switches DAMAGE 100 on; B (owner) sends a pair; A's copy
            // of B's car applies it once (CarDamaged once, re-encodes identically), the late joiner C decodes the same pair; A's own hit reaches B
            if (remote != null && remote.Rig != null)
            {
                AcRules on = AcSession.Rules != null ? AcSession.Rules.Clone() : new AcRules();
                on.DamageMultiplier = 100;
                r.Check(AcRace.RequestRules(on), "master A: RequestRules DAMAGE 100");
                SetDamage(B, DmgD0, DmgD1);
                float t0d = Time.time;
                while (Time.time - t0d < 2f) { Drive(B, 20f); SetDamage(B, DmgD0, DmgD1); B.Net.Tick(); C.Net.Tick(); yield return null; }
                AcCarDamage rd = remote.Rig.Physics.Damage;
                int e0, e1;
                rd.ToWire(out e0, out e1);
                AcNetPlayer pb = AcNetGame.FindByPhotonId(2);
                r.Check(pb != null && pb.DamageD0 == DmgD0 && pb.DamageD1 == DmgD1, "A decodes B's pair from ints 42-43 (" + (pb != null ? pb.DamageD0.ToString("X8") + " " + pb.DamageD1.ToString("X8") : "-") + ")");
                r.Check(rd.ZoneByte(0) == 45 && rd.ZoneByte(3) == 20 && e0 == DmgD0 && e1 == DmgD1, "B's car on A: front " + rd.ZoneByte(0) + ", right " + rd.ZoneByte(3) + ", re-encodes to the identical pair (" + e0.ToString("X8") + " " + e1.ToString("X8") + ")");
                r.Check(remote.DamageEvents == 1, "CarDamaged once for B's car (" + remote.DamageEvents + ")");
                AcNet.Remote bOnC = C.Net.RemoteOfPlayer(2);
                r.Check(bOnC != null && bOnC.State.Car.DamageD0 == DmgD0 && bOnC.State.Car.DamageD1 == DmgD1, "late joiner C decodes B's current pair");
                Transform frd = remote.Frame;
                r.Aim(frd.position + frd.rotation * new Vector3(2.8f, 1.5f, 4.5f), frd.position + Vector3.up * 0.5f, 55f);
                r.Shot("net_remote_damaged");
                LabLog("net damage: TexturesWanted " + AcCarDamageGate.TexturesWanted + " (B's car was built at DAMAGE 0: paint dents need a rebuild, glass / panels show now)");
                if (AcLocal.Car != null && AcLocal.Car.Physics != null)
                {
                    AcCarPhysics mine = AcLocal.Car.Physics;
                    yield return r.Frames(5);
                    r.Check(mine.DamageRate > 0.99f && mine.DebugAddImpact(AcDamageZone.Rear, 30f, -1), "A's own car takes a rear 30 hit at DAMAGE 100 (rate " + mine.DamageRate + ")");
                    t0d = Time.time;
                    while (Time.time - t0d < 2f) { Drive(B, 20f); SetDamage(B, DmgD0, DmgD1); B.Net.Tick(); C.Net.Tick(); yield return null; }
                    AcNet.Remote aOnB = B.Net.RemoteOfPlayer(1);
                    r.Check(aOnB != null && ((aOnB.State.Car.DamageD0 >> 8) & 255) == 30, "B reads A's rear 30 from A's record (" + (aOnB != null ? aOnB.State.Car.DamageD0.ToString("X8") : "-") + ")");
                    mine.Repair(AcRepair.All);
                    t0d = Time.time;
                    while (Time.time - t0d < 2f) { Drive(B, 20f); SetDamage(B, DmgD0, DmgD1); B.Net.Tick(); C.Net.Tick(); yield return null; }
                    aOnB = B.Net.RemoteOfPlayer(1);
                    r.Check(aOnB != null && aOnB.State.Car.DamageD0 == 0 && aOnB.State.Car.DamageD1 == 0, "A's repair goes out as 0 0");
                }
                r.Check(remote.DamageEvents == 1 && rd.ZoneByte(0) == 45, "the same pair re-sent for 4 s changes nothing (events " + remote.DamageEvents + ")");
            }

            // ---- second pass checks: door / horn sounds, ghost / solid, hiding beyond 1500 m, the cockpit rebuild, AI blocks + master hand-over
            if (remote != null && remote.Rig != null)
            {
                // B opens and closes its left door and sounds the horn: A's copy of B's car plays A's door sound twice and the horn on / off.
                // Time-based (game seconds): a batch lab runs at ~2000 fps, where a frame count is a few ms - shorter than the timeline's render delay.
                int doorsBefore = remote.DoorSounds;
                bool hornSeen = false;
                float t0 = Time.time;
                while (Time.time - t0 < 3f)
                {
                    float t = Time.time - t0;
                    Drive(B, 0f);      // parked (Place rewrites pose, rpm and lights; the door and the horn go on top)
                    AcNetCar c = B.Net.Own.Car;
                    c.DoorL = t < 1.2f ? Mathf.Clamp01(t / 0.4f) : 0f;
                    c.Lights = (int)AcLightState.Headlights | (t >= 0.6f && t < 1.8f ? (int)AcLightState.Horn : 0);
                    B.Net.Own.Car = c;
                    B.Net.Tick(); C.Net.Tick();
                    yield return null;
                    if (remote.HornOn) hornSeen = true;
                }

                if (remote.DoorSounds - doorsBefore < 2 || !hornSeen) Diag("door/horn", remote, B);
                r.Check(remote.DoorSounds - doorsBefore >= 2, "remote door edges -> A's Door(side, opening): " + (remote.DoorSounds - doorsBefore) + " door sound(s) for one open + close");
                r.Check(hornSeen && !remote.HornOn, "remote horn bit -> A's Horn(true) then Horn(false)");

                // ghost / solid (Q-A3): B fast in the pit lane = see-through for my car; out of the pit lane = solid
                for (int phase = 0; phase < 2; phase++)
                {
                    bool pit = phase == 0;
                    t0 = Time.time;
                    while (Time.time - t0 < 1.5f)
                    {
                        Drive(B, 20f);
                        AcNetCar c = B.Net.Own.Car; c.InPitlane = pit; B.Net.Own.Car = c;
                        B.Net.Tick(); C.Net.Tick();
                        yield return null;
                    }

                    bool expect = pit || AcLocal.InPitlane;
                    if (remote.Ghost != expect) Diag(pit ? "ghost in pit" : "ghost out", remote, B);
                    bool bit = true;
                    foreach (Collider col in remote.Rig.Root.GetComponentsInChildren<Collider>(true))
                        bit &= ((col.excludeLayers.value & (1 << AcLayers.Car)) != 0) == remote.Ghost;
                    r.Check(remote.Ghost == expect && bit, "ghost rule: B at 72 km/h " + (pit ? "IN" : "out of") + " the pit lane (my car in pit lane: " + AcLocal.InPitlane + ") -> ghost " + remote.Ghost
                                                            + " (expected " + expect + "), Car bit in excludeLayers matches: " + bit);
                }

                // hidden beyond the draw radius (posed, not shown), shown again when back
                float alongBefore = B.Along;
                t0 = Time.time;
                while (Time.time - t0 < 1.5f)
                {
                    Drive(B, 0f);
                    AcNetCar c = B.Net.Own.Car; c.Pos = new AcNetV3(c.Pos.X + 2000f, c.Pos.Y, c.Pos.Z); c.Epoch = 2; c.InPitlane = false; B.Net.Own.Car = c;
                    B.Net.Tick(); C.Net.Tick();
                    yield return null;
                }

                bool hidden = remote.Hidden && remote.Rig != null && remote.Rig.Root != null && !remote.Rig.Root.activeSelf;
                if (!hidden) Diag("2 km", remote, B);
                t0 = Time.time;
                while (Time.time - t0 < 1.5f)
                {
                    Drive(B, 0f);
                    AcNetCar c = B.Net.Own.Car; c.Epoch = 3; B.Net.Own.Car = c;
                    B.Net.Tick(); C.Net.Tick();
                    yield return null;
                }

                if (remote.Hidden) Diag("back", remote, B);
                r.Check(hidden && !remote.Hidden && remote.Rig.Root.activeSelf, "B 2 km away is hidden (" + hidden + "), shown again when back (" + !remote.Hidden + ") - draw radius " + AcNetGame.DrawRadius + " m");

                // the cockpit rebuild: B parked within 15 m of my eye (I am not seated) -> rebuilt with COCKPIT_HR and swapped like the stand-in.
                // N's eye = AcPlayer.Head(), else AcLocal.WorldPosition (the lab has no Bridge player): park B 7 m from that point, grounded.
                Vector3 eyeP = EyeForLab();
                if (!AcLocal.Seated)
                {
                    Vector3 near = eyeP + Vector3.forward * 7f, g, gn;
                    if (AcTrackWorld.TryGround(near + Vector3.up * 3f, out g, out gn)) near = g + Vector3.up * 0.35f;   // // ours: a CG height
                    if ((near - eyeP).sqrMagnitude > AcNetGame.InteriorNearMetres * AcNetGame.InteriorNearMetres) near = eyeP + Vector3.forward * 7f;
                    float rebuildUntil = Time.realtimeSinceStartup + 90f;
                    while (Time.realtimeSinceStartup < rebuildUntil && !remote.Interior)
                    {
                        AcNetCar c = B.Net.Own.Car; c.Pos = new AcNetV3(near.x, near.y, near.z); c.Vel = default(AcNetV3); c.Epoch = 4; B.Net.Own.Car = c;
                        B.Net.OwnStampMs = (int)(Time.fixedTimeAsDouble * 1000.0);
                        B.Net.Tick(); C.Net.Tick();
                        yield return null;
                    }

                    if (!remote.Interior) Diag("cockpit", remote, B);
                    r.Check(remote.Interior && remote.Rig != null, "B's parked car near me (" + eyeP.ToString("F0") + ") was rebuilt with its cockpit (COCKPIT_HR) for a passenger: " + remote.Interior);
                    if (remote.Rig != null)
                    {
                        yield return r.Frames(10);
                        AcCarDamage rd2 = remote.Rig.Physics.Damage;
                        r.Check(rd2.ZoneByte(0) == 45 && rd2.ZoneByte(3) == 20 && remote.DamageEvents == 1, "the cockpit rebuild keeps B's damage (front " + rd2.ZoneByte(0) + ", right " + rd2.ZoneByte(3) + ") without a second CarDamaged (" + remote.DamageEvents + ")");
                    }
                    if (remote.Rig != null)
                    {
                        Transform fr = remote.Frame;
                        r.Aim(fr.position + fr.rotation * new Vector3(0.35f, 0.75f, -0.1f), fr.position + fr.rotation * new Vector3(0.3f, 0.5f, 2f), 70f);
                        r.Shot("net_remote_cockpit");
                    }
                }
                else LabLog("cockpit rebuild not checked (A is seated)");
                Place(B, alongBefore, 0f);
            }

            // AI blocks drawn as remote cars: B becomes the master (the room's shared-mode master bit), writes one AI car; then A takes over again
            _world.MasterOverride = 2;
            until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until) { Drive(B, 20f); B.Net.Tick(); C.Net.Tick(); yield return null; }
            r.Check(!AcNetGame.IsMaster && AcNetGame.MasterId == 2 && B.Net.IsInstanceMaster, "hand-over to B (room master bit): A says master '" + AcNetGame.MasterName + "' (" + AcNetGame.MasterId + ")");
            int nameLine = 0;
            string fullName = null;
            for (int i = 0; i < 40 && fullName == null; i++) { string n = AcNetGame.DriverNameAt(i); if (n != null && n.Length > 12) { fullName = n; nameLine = i + 1; } }
            float aiUntil = Time.realtimeSinceStartup + 150f;
            AcRemoteCar aiCar = null;
            while (Time.realtimeSinceStartup < aiUntil)
            {
                Drive(B, 20f);
                AcNetCar ai = OnLine(B.Net.Own.Car, B.Along - 25f, 20f);
                ai.DamageD0 = AiD0; ai.DamageD1 = AiD1;   // flow pass: the master's AI damage table (Host 0)
                B.Net.SetAi(0, in ai, 80, fullName ?? "AI driver", nameLine);
                B.Net.Tick(); C.Net.Tick();
                aiCar = null;
                for (int i = 0; i < AcNetGame.RemoteCars.Count; i++) if (AcNetGame.RemoteCars[i].Player != null && AcNetGame.RemoteCars[i].Player.IsAi) aiCar = AcNetGame.RemoteCars[i];
                if (aiCar != null && aiCar.Rig != null && aiCar.Drawn) break;
                yield return null;
            }

            r.Check(AcNetGame.AiPlayers.Count == 1 && aiCar != null && aiCar.Drawn, "B's AI car 0 is listed (AiPlayers " + AcNetGame.AiPlayers.Count + ") and drawn as a remote car on A (built: " + (aiCar != null && aiCar.Rig != null) + ")");
            if (fullName != null)
                r.Check(AcNetGame.AiPlayers.Count == 1 && AcNetGame.AiPlayers[0].Name == fullName, "the AI name travels in full through its names.txt line " + nameLine + ": '" + (AcNetGame.AiPlayers.Count > 0 ? AcNetGame.AiPlayers[0].Name : "-") + "'");
            if (aiCar != null)
            {
                float t0a = Time.time;
                while (Time.time - t0a < 1.5f)
                {
                    Drive(B, 20f);
                    AcNetCar ai = OnLine(B.Net.Own.Car, B.Along - 25f, 20f); ai.DamageD0 = AiD0; ai.DamageD1 = AiD1;
                    B.Net.SetAi(0, in ai, 80, fullName ?? "AI driver", nameLine);
                    B.Net.Tick(); C.Net.Tick();
                    yield return null;
                }
                AcCarDamage ad = aiCar.Rig != null ? aiCar.Rig.Physics.Damage : null;
                r.Check(aiCar.Player.DamageD0 == AiD0 && aiCar.Player.DamageD1 == AiD1 && aiCar.Player.Retired, "A decodes the AI car's pair from Host 0 (" + aiCar.Player.DamageD0.ToString("X8") + " " + aiCar.Player.DamageD1.ToString("X8") + ", retired " + aiCar.Player.Retired + ")");
                r.Check(ad != null && ad.ZoneByte(0) == 33 && ad.EngineDead && ad.Retired, "the AI car on A: front 33, engine dead, retired");
            }
            if (aiCar != null)
            {
                Transform fa = aiCar.Frame;
                r.Aim(fa.position + fa.rotation * new Vector3(4f, 2.5f, -9f), fa.position + Vector3.up * 0.5f, 55f);
                r.Shot("net_ai_car");
            }

            int seqHeld = AcNetGame.RaceControl.Current != null ? AcNetGame.RaceControl.Current.Seq : 0;
            _world.MasterOverride = -1;
            until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until) { Drive(B, 20f); B.Net.Tick(); C.Net.Tick(); yield return null; }
            AcNet.AiCar inherited = AcNetGame.Net.Ai(0);
            r.Check(AcNetGame.IsMaster && B.Net.MasterId == 1, "A (lowest id) takes over again once the room bit is gone (B says " + B.Net.MasterId + ")");
            r.Check(inherited != null && inherited.Present, "A keeps writing the AI car it inherited (no AI driver here: cleared after 10 s)");
            int h0, h1;
            AcNetBlocks.UnpackAiDamage(AcNetGame.Net.AiDamageTable, 0, out h0, out h1);
            r.Check(h0 == AiD0 && h1 == AiD1, "A inherited the AI damage pair from B's Host 0 table (" + h0.ToString("X8") + " " + h1.ToString("X8") + ")");
            if (AcNetGame.RaceControl.Current != null)
            {
                AcRaceCommand again = AcNetGame.RaceControl.Current.Clone();
                bool pub = AcNetGame.RaceControl.Publish(again);
                r.Check(pub && AcNetGame.RaceControl.Current.Seq > seqHeld, "A's next command after the hand-over has seq " + AcNetGame.RaceControl.Current.Seq + " > " + seqHeld);
            }

            // B leaves
            B.Net.Dispose();
            until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until) { C.Net.Tick(); yield return null; }
            // the inherited AI car stays drawn on the new master until its block is cleared (no AI driver here: 10 s after the take-over, fixer pass
            // 2026-11-01 item 9), so B's leave counts only player cars; the AI car is checked after the clear
            r.Check(AcNetGame.Players.Count == 1 && PlayerCars() == 0, "B left: A lists only C and B's car is gone (" + PlayerCars() + " player cars, " + AcNetGame.RemoteCars.Count + " remote cars in all)");
            r.Check(AcNetGame.IsMaster && C.Net.MasterId == 1, "A stays the master");
            until = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < until && AcNetGame.RemoteCars.Count > 0) { C.Net.Tick(); yield return null; }
            r.Check(AcNetGame.RemoteCars.Count == 0 && AcNetGame.AiPlayers.Count == 0, "the inherited AI car is cleared and no longer drawn (no AI driver on this master; " + AcNetGame.RemoteCars.Count + " remote cars)");
            C.Net.Dispose();
        }

        static void LabLog(string s) { AcLabRunner.Log(s); }

        // flow pass 2026-11-02: lab damage pairs - B's car front 45 / right 20 km/h with LF rod level 8; the AI car front 33, engine 100 % (dead), retired
        const int DmgD0 = 45 | (20 << 24), DmgD1 = 8 << 8;
        const int AiD0 = 33, AiD1 = (100 << 24) | int.MinValue;

        static void SetDamage(Scripted s, int d0, int d1)
        {
            AcNetCar c = s.Net.Own.Car; c.DamageD0 = d0; c.DamageD1 = d1; s.Net.Own.Car = c;
        }

        static int PlayerCars()
        {
            int n = 0;
            for (int i = 0; i < AcNetGame.RemoteCars.Count; i++) if (AcNetGame.RemoteCars[i].Player == null || !AcNetGame.RemoteCars[i].Player.IsAi) n++;
            return n;
        }

        /// <summary>Why a remote check failed: A's drawn car, A's decoded copy of B's record, B's own state, the eye.</summary>
        static void Diag(string when, AcRemoteCar rc, Scripted b)
        {
            AcNet.Remote rb = AcNetGame.Net != null ? AcNetGame.Net.RemoteOfPlayer(2) : null;
            bool listed = false;
            for (int i = 0; i < AcNetGame.RemoteCars.Count; i++) if (ReferenceEquals(AcNetGame.RemoteCars[i], rc)) listed = true;
            LabLog("diag " + when + ": " + (rc != null ? rc.Describe() : "no car") + ", in RemoteCars " + listed + " (" + AcNetGame.RemoteCars.Count + ")"
                   + "; A reads B: " + (rb == null ? "no remote" : "decoded " + rb.Decoded + " seq " + rb.Seq + " stamp " + rb.StampMs + " door " + rb.State.Car.DoorL.ToString("0.00")
                                         + " lights " + rb.State.Car.Lights + " pit " + rb.State.Car.InPitlane + " epoch " + rb.State.Car.Epoch + " pos " + rb.State.Car.Pos.X.ToString("0") + "," + rb.State.Car.Pos.Z.ToString("0"))
                   + "; B writes door " + b.Net.Own.Car.DoorL.ToString("0.00") + " stamp " + b.Net.OwnStampMs + " claimed " + b.Net.Claimed
                   + "; A net now " + AcNetGame.Net.Now.ToString("0.00") + ", fixed ms " + (int)(Time.fixedTimeAsDouble * 1000.0) + ", eye " + EyeForLab().ToString("F0"));
        }

        static Vector3 EyeForLab()
        {
            try { Camera c = AcPlayer.Head(); if (c != null) return c.transform.position; } catch (System.Exception) { }
            return AcLocal.WorldPosition;
        }

        /// <summary>B's car on the AI line at <paramref name="along"/> metres (Unity world), grounded.</summary>
        static void Place(Scripted s, float along, float speed)
        {
            AcAiSpline line = AcTrackWorld.AiLine;
            Vector3 p, ahead;
            if (line != null && line.Count > 2)
            {
                float lap = line.Total > 1f ? line.Total : 1f;
                float d = Mathf.Repeat(along, lap);
                int i = 0;
                while (i < line.Count - 2 && line.Length[i + 1] < d) i++;
                float seg = Mathf.Max(0.01f, line.Length[i + 1] - line.Length[i]);
                float u = Mathf.Clamp01((d - line.Length[i]) / seg);
                Vector3 a = AcTrackWorld.ToUnity(line.X(i), line.Y(i), line.Z(i));
                Vector3 b = AcTrackWorld.ToUnity(line.X(i + 1), line.Y(i + 1), line.Z(i + 1));
                p = Vector3.Lerp(a, b, u);
                ahead = b - a;
            }
            else
            {
                AcSpawnPoint sp = AcTrackWorld.StartGrid.Length > 1 ? AcTrackWorld.StartGrid[1] : default(AcSpawnPoint);
                p = sp.Marker + sp.Rotation * Vector3.forward * along;
                ahead = sp.Rotation * Vector3.forward;
            }

            Vector3 g, n;
            if (AcTrackWorld.TryGround(p + Vector3.up * 3f, out g, out n)) p = g;
            p += Vector3.up * 0.35f;   // // ours: a CG height
            ahead.y = 0f;
            Quaternion rot = ahead.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(ahead.normalized, Vector3.up) : s.Rot;
            s.Vel = rot * Vector3.forward * speed;
            s.Pos = p;
            s.Rot = rot;
            s.Along = along;
            AcNetCar c = s.Net.Own.Car;
            c.Pos = new AcNetV3(p.x, p.y, p.z);
            c.Rot = new AcNetQ(rot.x, rot.y, rot.z, rot.w);
            c.Vel = new AcNetV3(s.Vel.x, s.Vel.y, s.Vel.z);
            c.Rpm = 3000f + speed * 150f;
            c.Gear = speed > 1f ? 3 : 0;
            c.Throttle = 0.6f;
            c.Lights = (int)AcLightState.Headlights;
            s.Net.Own.Car = c;
            s.Net.OwnStampMs = (int)(Time.fixedTimeAsDouble * 1000.0);
        }

        static void Drive(Scripted s, float speed) { Place(s, s.Along + speed * Time.deltaTime, speed); }

        /// <summary>A copy of <paramref name="template"/> (car, skin, layout) at <paramref name="along"/> metres on the AI line (an AI car of the master).</summary>
        static AcNetCar OnLine(AcNetCar template, float along, float speed)
        {
            Vector3 p; Quaternion rot;
            LinePose(along, out p, out rot);
            AcNetCar c = template;
            Vector3 v = rot * Vector3.forward * speed;
            c.Pos = new AcNetV3(p.x, p.y, p.z);
            c.Rot = new AcNetQ(rot.x, rot.y, rot.z, rot.w);
            c.Vel = new AcNetV3(v.x, v.y, v.z);
            c.InPitlane = false;
            c.DoorL = 0f;
            c.Lights = (int)AcLightState.Headlights;
            return c;
        }

        /// <summary>The grounded pose on the loaded track's AI line (or the grid) <paramref name="along"/> metres from its start.</summary>
        static void LinePose(float along, out Vector3 p, out Quaternion rot)
        {
            AcAiSpline line = AcTrackWorld.AiLine;
            Vector3 ahead;
            if (line != null && line.Count > 2)
            {
                float lap = line.Total > 1f ? line.Total : 1f;
                float d = Mathf.Repeat(along, lap);
                int i = 0;
                while (i < line.Count - 2 && line.Length[i + 1] < d) i++;
                float seg = Mathf.Max(0.01f, line.Length[i + 1] - line.Length[i]);
                float u = Mathf.Clamp01((d - line.Length[i]) / seg);
                Vector3 a = AcTrackWorld.ToUnity(line.X(i), line.Y(i), line.Z(i));
                Vector3 b = AcTrackWorld.ToUnity(line.X(i + 1), line.Y(i + 1), line.Z(i + 1));
                p = Vector3.Lerp(a, b, u);
                ahead = b - a;
            }
            else
            {
                AcSpawnPoint sp = AcTrackWorld.StartGrid.Length > 1 ? AcTrackWorld.StartGrid[1] : default(AcSpawnPoint);
                p = sp.Marker + sp.Rotation * Vector3.forward * along;
                ahead = sp.Rotation * Vector3.forward;
            }

            Vector3 g, n;
            if (AcTrackWorld.TryGround(p + Vector3.up * 3f, out g, out n)) p = g;
            p += Vector3.up * 0.35f;   // // ours: a CG height
            ahead.y = 0f;
            rot = ahead.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(ahead.normalized, Vector3.up) : Quaternion.identity;
        }
    }
}
