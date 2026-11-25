using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [R, Race] Lab scenarios (DevTools/lab.sh &lt;name&gt; [-actrack magione] [-aclayout id] [-accar abarth500]):
    /// <list type="bullet">
    /// <item><c>race-solo-magione</c>: the whole solo flow. Load the layout through AcRace.RequestLoad (practice), my car in its pit box:
    /// checks it stands ON the road under the AC_PIT marker (not floating, not sunk - research T1) and still stands after 3 s, shot
    /// race-pitbox.png. Practice timing: the car is moved along fast_lane.ai with ResetTo (a scripted "drive", ~3 points per frame) from the
    /// pit box over the line and once around: the out lap ends at the first crossing, exactly 1 timed lap must be counted. Then START a
    /// 2-lap race alone: Grid (car on AC_START_0, frozen, shot race-grid.png), the light sequence on the shared clock (1..5 reds a second apart,
    /// hold, green - every step logged, shot race-lights.png at 3 reds), lights out, then the scripted drive from the grid: magione's grid is
    /// BEFORE the line (T3), so the first crossing must NOT be a lap; lap 1 on the line (expect valid), lap 2 driven 4 m outside the left
    /// track edge for 250 m (expect a cut when P reports wheel surfaces; reported, not failed, when P gives no wheel contacts), finish P1,
    /// Results (RaceFinished raised), back to practice after the result screen with the car back in its pit box.</item>
    /// <item><c>race-solo-actions</c>: the player actions and rules around the race. Load (practice). ChooseCar to another owned car:
    /// LocalCarChanged raised, the new car in my pit box (on the road). Pit lane: the car moved along ai/pit_lane.ai -> AcLocal.InPitlane
    /// and P's limiter (Physics.SpeedLimitKmh = PitLimitKmh, Telemetry.Lights PitLimiter bit), off again on the track. FUEL_RATE /
    /// TYRE_WEAR_RATE reach P (Physics.FuelRate / TyreWearRate). Reset: the car put 12 m beside the line -> AcRace.ResetCar() puts it back on
    /// the line (heading along it), a second call within 1.5 s is refused, the R-key path (Input.ResetRequest) is consumed. BackToPits()
    /// -> pit box. START a 1-lap race with JUMP_START_PENALTY 2: seated as driver on the grid (AcSeat.Seated, SeatedCar == my car,
    /// IsDriver), the car moved 2 m during the reds -> "FALSE START" + PenaltyMs 5000 after the green; RequestStop -> practice.
    /// RequestHub: my car disposed (LocalCar null, LocalCarChanged(null)), AcTrackWorld.Loaded false, Phase Hub, the player near the hub.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    static class AcLabRace
    {
        static AcLabRace()
        {
            AcLabRunner.Register("race-solo-magione", r => Solo(r));
            AcLabRunner.Register("race-solo-actions", r => Actions(r));
            AcLabRunner.Register("race-repair", r => Repair(r));
            AcLabRunner.Register("race-damage-live", r => DamageLive(r));
        }

        static readonly List<AcCarRig> CarChanges = new List<AcCarRig>();
        static readonly List<string> Messages = new List<string>();
        static void OnCarChanged(AcCarRig rig) { CarChanges.Add(rig); AcLabRunner.Log("LocalCarChanged: " + (rig != null && rig.Car != null ? rig.Car.Id : "null")); }
        static void OnMessage(string text, float s) { Messages.Add(text); AcLabRunner.Log("message: " + text); }

        static IEnumerator Actions(AcLabRunner r)
        {
            AcEvents.LocalCarChanged += OnCarChanged;
            AcEvents.Message += OnMessage;
            yield return r.Wait(() => r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            string trackId = AcLabBoot.Arg("-actrack", "magione"), layoutId = AcLabBoot.Arg("-aclayout", "");
            AcLayoutEntry layout = content.Layout(trackId, layoutId);
            if (layout == null) { AcTrackEntry t = content.Track(trackId); if (t != null && t.Layouts.Count > 0) layout = t.Layouts[0]; }
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + "/" + layoutId + " owned")) yield break;
            var owned = new List<AcCarEntry>();
            for (int i = 0; i < content.Cars.Count; i++) if (content.Cars[i].Owned && !content.Cars[i].IsDlcStub) owned.Add(content.Cars[i]);
            if (!r.Check(owned.Count >= 2, "two owned cars (" + owned.Count + ")")) yield break;
            AcCarEntry car1 = content.Car(AcLabBoot.Arg("-accar", "abarth500"));
            if (car1 == null || !car1.Owned) car1 = owned[0];
            AcCarEntry car2 = owned[0] != car1 ? owned[0] : owned[1];
            AcRace.ChooseCar(car1, car1.Skins.Count > 0 ? car1.Skins[0].Id : "");

            AcRules rules = AcRace.DefaultRules();
            AcLabRunner.Log("install defaults: " + rules + ", jump start penalty " + rules.JumpStartPenalty + ", QUALIFY_MAX_WAIT_PERC " + AcRace.Defaults.QualifyMaxWaitPerc + ", START_RULE " + AcRace.Defaults.StartRule);
            r.Check(AcRace.Defaults.StartRule != 0 || rules.JumpStartPenalty == 0, "START_RULE 0 of the server preset -> lobby default jump start penalty 0 (locked until green)");
            rules.Session = AcSessionType.Race; rules.Laps = 1; rules.AiCount = 0; rules.FuelRate = 50; rules.TyreWearRate = 200;
            yield return AcLabRace.LobbySeeded();
            AcLabRace.SyncLobby(rules);
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key);
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice && AcRace.LocalCar != null && AcRace.LocalCar.Root != null, 900f, "practice + my car");
            if (r.Failed) yield break;
            AcCarRig rig = AcRace.LocalCar;
            yield return r.Frames(5);
            r.Check(rig.Physics != null && Mathf.Approximately(rig.Physics.FuelRate, 0.5f) && Mathf.Approximately(rig.Physics.TyreWearRate, 2f),
                    "FUEL_RATE 50 / TYRE_WEAR_RATE 200 reach P (" + (rig.Physics != null ? rig.Physics.FuelRate + " / " + rig.Physics.TyreWearRate : "no physics") + ")");

            // ---- ChooseCar: respawn in the pit box
            CarChanges.Clear();
            r.Check(AcRace.ChooseCar(car2, car2.Skins.Count > 0 ? car2.Skins[0].Id : ""), "ChooseCar " + car2.Id + " on the track");
            yield return r.Wait(() => AcRace.LocalCar != null && AcRace.LocalCar != rig && AcRace.LocalCar.Root != null, 180f, "the new car");
            if (r.Failed) yield break;
            rig = AcRace.LocalCar;
            r.Check(rig.Car == car2, "my car is now " + (rig.Car != null ? rig.Car.Id : "?"));
            r.Check(CarChanges.Count >= 2 && CarChanges[0] == null && CarChanges[CarChanges.Count - 1] == rig, "LocalCarChanged(null) then LocalCarChanged(new car) (" + CarChanges.Count + " events)");
            r.Check(AcLocal.Car == rig, "AcLocal.Car is the new car");
            yield return r.Frames(10);
            CheckOnGround(r, rig, AcTrackWorld.PitBoxes[Math.Max(0, AcRace.PitBox)], "new car in the pit box");
            AimAt(r, rig, 6f);
            r.Shot("race-actions-newcar");

            // the editor has no Bridge player body: S's seat waits 15 s for one and refuses (lab 2026-11-01: "seating ok" = pending, never
            // seated). A stand-in body as in S's seat-enter lab (AcSeat.LabRoot / head / hands), beside the car.
            Transform labRoot = new GameObject("Lab Player (race)").transform;
            Transform labHead = new GameObject("Lab Head").transform; labHead.SetParent(labRoot, false); labHead.localPosition = new Vector3(0f, 1.2f, 0f);
            Transform labL = new GameObject("Lab Left").transform; labL.SetParent(labRoot, false); labL.localPosition = new Vector3(-0.25f, 0.9f, 0.3f);
            Transform labR = new GameObject("Lab Right").transform; labR.SetParent(labRoot, false); labR.localPosition = new Vector3(0.25f, 0.9f, 0.3f);
            labRoot.position = AcSpawner.CarPosition(rig) - rig.Root.transform.right * 2.5f;
            AcSeat.LabRoot = labRoot; AcSeat.LabHead = labHead; AcSeat.LabLeft = labL; AcSeat.LabRight = labR; AcSeat.LabVr = false;
            // seated + driven from here: P simulates the wheels (contacts, surfaces) only for a driven car - the pit lane and cut rules need them
            r.Check(AcSeat.Enter(rig, AcSeatKind.Driver), "AcSeat.Enter(my car, Driver) with the lab stand-in body");
            yield return r.Wait(() => AcSeat.Seated && AcSeat.SeatedCar == rig, 5f, "seated in my car");
            yield return r.Frames(20);

            // ---- pit lane + limiter
            AcAiSpline pit = AcTrackWorld.PitLane;
            if (pit != null && !pit.IsEmpty)
            {
                bool sawPit = false, sawLimiter = false, sawLight = false, contacts = false;
                int p0 = NearestIndex(pit, rig);
                yield return Drive(rig, pit, p0, Math.Min(pit.Count - 1 - p0, pit.Count / 2), 0f, 0, 0, () =>
                {
                    if (AcLocal.InPitlane) sawPit = true;
                    if (rig.Physics != null && rig.Physics.SpeedLimitKmh > 0f) sawLimiter = true;
                    if (rig.Physics != null && (rig.Physics.Telemetry.Lights & AcLightState.PitLimiter) != 0) sawLight = true;
                    AcTelemetry tl = rig.Physics != null ? rig.Physics.Telemetry : default(AcTelemetry);
                    if (tl.LF.Contact || tl.RR.Contact) contacts = true;
                });
                if (contacts || sawPit)
                {
                    r.Check(sawPit, "AcLocal.InPitlane while moved along pit_lane.ai");
                    r.Check(sawLimiter, "P's SpeedLimitKmh set to " + AcSession.Rules.PitLimitKmh + " km/h in the pit lane");
                    r.Check(sawLight, "Telemetry.Lights has the PitLimiter bit in the pit lane");
                }
                else AcLabRunner.Log("note: P reported no wheel contacts while the car was moved by ResetTo - pit lane detection not exercised (IS_PITLANE surfaces need contacts)");
            }
            else AcLabRunner.Log("note: no pit_lane.ai on " + layout.Key + " - pit lane check skipped");
            AcAiSpline line = AcTrackWorld.AiLine;
            if (!r.Check(line != null && !line.IsEmpty, "a fast lane")) yield break;
            int mid = line.Count / 3;
            yield return Drive(rig, line, mid, 30, 0f, 0, 0);
            yield return r.Frames(40);
            r.Check(!AcLocal.InPitlane, "out of the pit lane on the track");
            r.Check(rig.Physics == null || rig.Physics.SpeedLimitKmh == 0f, "limiter off on the track");

            // ---- reset to the line (VR tablet path ResetCar + the R key path ResetRequest)
            yield return r.Frames(100);   // > 1.5 s since anything reset
            yield return PlaceBeside(rig, line, mid + 40, 12f);
            float before = LineDistance(rig, line);
            bool reset1 = AcRace.ResetCar();
            yield return r.Frames(3);
            float after = LineDistance(rig, line);
            r.Check(reset1 && before > 8f && after < 1.5f, "ResetCar: " + before.ToString("0.0") + " m off the line -> " + after.ToString("0.0") + " m");
            r.Check(!AcRace.ResetCar(), "a second reset within 1.5 s is refused");
            r.Check(!AcLocal.LapValid || AcLocal.OutLap, "the lap is invalid after a reset");
            float wait = Time.realtimeSinceStartup + 1.7f;
            while (Time.realtimeSinceStartup < wait) yield return null;
            yield return PlaceBeside(rig, line, mid + 80, 12f);
            rig.Physics.Input.ResetRequest = true;
            yield return r.Frames(3);
            r.Check(!rig.Physics.Input.ResetRequest && LineDistance(rig, line) < 1.5f, "the R key path: ResetRequest consumed, car back on the line (" + LineDistance(rig, line).ToString("0.0") + " m)");

            // ---- cut sensing: the wheels on IS_VALID_TRACK=0 surfaces (grass) while moved 4 m beyond the left edge (the timer's cut rule itself
            // is checked offline by harness racerules; still in the out lap here, so no lap is invalidated)
            _maxOff = 0; _offRig = rig;
            int cutAt = (mid + 160) % line.Count;
            yield return Drive(rig, line, cutAt, 60, 4f, cutAt, 61, TraceOff);
            AcLabRunner.Log("cut sensing: most wheels off the valid track " + _maxOff + " (ALLOWED_TYRES_OUT " + AcSession.Rules.AllowedTyresOut + "), last surface LF "
                            + (rig.Physics != null && rig.Physics.Telemetry.LF.Surface != null ? rig.Physics.Telemetry.LF.Surface.Key : "none"));
            r.Check(_maxOff > Math.Max(0, AcSession.Rules.AllowedTyresOut), "a cut is seen 4 m off the track (" + _maxOff + " wheels on IS_VALID_TRACK=0)");
            yield return r.Frames(100);

            // ---- back to pits
            r.Check(AcRace.BackToPits(), "BackToPits in practice");
            yield return r.Frames(5);
            CheckOnGround(r, rig, AcTrackWorld.PitBoxes[Math.Max(0, AcRace.PitBox)], "back to pits");

            // ---- race with a jump start: seated on the grid, moved during the reds
            AcRules race = AcSession.Rules.Clone();
            race.Session = AcSessionType.Race; race.Laps = 1; race.JumpStartPenalty = 2;
            Messages.Clear();
            r.Check(AcRace.RequestStart(race), "RequestStart (1 lap, JUMP_START_PENALTY 2)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown, 10f, "the grid");
            if (AcSession.Phase != AcPhase.Grid && AcSession.Phase != AcPhase.Countdown) yield break;   // (r.Failed may hold an earlier, non-fatal FAIL)
            yield return r.Wait(() => AcSeat.Seated && AcSeat.SeatedCar == rig && AcSeat.IsDriver, 8f, "seated as the driver on the grid");
            r.Check(AcSeat.Seated && AcSeat.SeatedCar == rig && AcSeat.IsDriver, "AcSeat: seated in my car as the driver on the grid");
            yield return r.Wait(() => AcRace.LightsReds >= 2, 30f, "two red lights");
            r.Check(rig.Physics != null && !rig.Physics.Frozen, "JUMP_START_PENALTY 2: the car is free during the reds (watched)");
            Transform tr = rig.Root.transform;
            bool hit;
            Vector3 g = AcSpawner.GroundUnder(AcSpawner.CarPosition(rig) + tr.forward * 2f + Vector3.up * 0.5f, out hit);
            AcSpawner.Place(rig, g, tr.eulerAngles.y);
            yield return r.Frames(3);
            r.Check(Messages.Contains("FALSE START!"), "\"FALSE START!\" message after moving 2 m before the green");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Racing, 20f, "the green");
            yield return r.Frames(3);
            r.Check(AcLocal.PenaltyMs == 5000, "jump start penalty +5 s (PenaltyMs " + AcLocal.PenaltyMs + ")");
            r.Check(AcRace.RequestStop(), "RequestStop (end the session)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice, 10f, "practice again");

            // ---- leave the track
            CarChanges.Clear();
            r.Check(AcRace.RequestHub(), "RequestHub (alone = master)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Hub, 10f, "the hub");
            yield return r.Frames(5);
            r.Check(AcRace.LocalCar == null && AcLocal.Car == null, "my car disposed");
            r.Check(CarChanges.Contains(null), "LocalCarChanged(null) raised");
            r.Check(!AcTrackWorld.Loaded, "the track is unloaded");
            r.Check(!AcSeat.Seated, "the seat is released when the track unloads");
            AcSeat.LabRoot = null; AcSeat.LabHead = null; AcSeat.LabLeft = null; AcSeat.LabRight = null;
            UnityEngine.Object.Destroy(labRoot.gameObject);
            Vector3 feet;
            if (AcPlayer.TryFeet(out feet)) r.Check(Mathf.Abs(feet.y - AcSpace.RoomOrigin.y) < 5f, "the player is back in the hub (feet y " + feet.y.ToString("0.0") + ")");
            else AcLabRunner.Log("note: no local player in the lab - hub teleport not checked");
            AcEvents.LocalCarChanged -= OnCarChanged;
            AcEvents.Message -= OnMessage;
        }

        /// <summary>Puts the car <paramref name="side"/> m to the left of fast-lane point <paramref name="i"/>, heading along the line.</summary>
        static IEnumerator PlaceBeside(AcCarRig rig, AcAiSpline line, int i, float side)
        {
            i %= line.Count;
            int j = (i + 1) % line.Count;
            float fx = line.X(j) - line.X(i), fz = line.Z(j) - line.Z(i), fl = Mathf.Max(1e-4f, Mathf.Sqrt(fx * fx + fz * fz));
            Vector3 p = AcTrackWorld.ToUnity(line.X(i) + fz / fl * side, line.Y(i), line.Z(i) - fx / fl * side);
            Vector3 a = AcTrackWorld.ToUnity(line.X(j), line.Y(j), line.Z(j)) - AcTrackWorld.ToUnity(line.X(i), line.Y(i), line.Z(i));
            bool hit;
            AcSpawner.Place(rig, AcSpawner.GroundUnder(p + Vector3.up * 2f, out hit), Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg);
            yield return null;
            yield return null;
        }

        static float LineDistance(AcCarRig rig, AcAiSpline line)
        {
            float x, y, z;
            AcTrackWorld.ToAc(AcSpawner.CarPosition(rig), out x, out y, out z);
            int n = line.Nearest(x, y, z);
            float dx = line.X(n) - x, dz = line.Z(n) - z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static readonly List<AcLap> Laps = new List<AcLap>();
        static bool _raceFinished, _raceStarted;

        static void OnLap(AcLap l) { Laps.Add(l); AcLabRunner.Log("lap event: " + l.Name + " lap " + l.Lap + " " + AcRaceMath.FormatLap(l.TimeMs) + (l.Valid ? " valid" : " CUT " + l.Cuts)); }
        static void OnFinished() { _raceFinished = true; }

        /// <summary>
        /// The lab loads with AcRace.RequestLoad directly (a player loads through F's lobby, so its draft = the session rules). F's practice
        /// rules sync (AcFront.TickRulesToRace) publishes the lobby draft whenever AcLobby.Version moves on a track - and F re-seeds the lobby
        /// (AcLobby.Load, Version++) 1 s after boot, which the lab's load beat: the lobby's own values (Practice, AI 0) replaced the lab's rules
        /// (seen 2026-11-01: "command seq 2 Practice ... AI 0" cleared the AI car in ai-magione). The lab therefore waits for that re-seed
        /// (<see cref="LobbySeeded"/>) and writes its rules into the lobby draft, like a master who set them on the page.
        /// </summary>
        internal static IEnumerator LobbySeeded()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f && AcLobby.Version < 2) yield return null;   // Load at F.Start = 1, the re-seed after boot = 2
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;   // F logs and settles in the same frame; a margin
            AcLabRunner.Log("lobby draft version " + AcLobby.Version + " (seeded from " + AcLobby.DefaultsSource + ")");
        }

        internal static void SyncLobby(AcRules r)
        {
            AcRules d = AcLobby.Rules;
            d.Session = r.Session; d.Laps = r.Laps; d.TimeMinutes = r.TimeMinutes; d.AbsAllowed = r.AbsAllowed; d.TcAllowed = r.TcAllowed;
            d.StabilityAllowed = r.StabilityAllowed; d.AutoClutchAllowed = r.AutoClutchAllowed; d.AutoShiftAllowed = r.AutoShiftAllowed;
            d.TyreBlanketsAllowed = r.TyreBlanketsAllowed; d.AllowedTyresOut = r.AllowedTyresOut; d.JumpStartPenalty = r.JumpStartPenalty;
            d.FuelRate = r.FuelRate; d.DamageMultiplier = r.DamageMultiplier; d.TyreWearRate = r.TyreWearRate; d.RaceOverTimeSeconds = r.RaceOverTimeSeconds;
            d.PitLimitKmh = r.PitLimitKmh; d.AiCount = r.AiCount; d.AiLevel = r.AiLevel;
            d.AllowedCars.Clear(); d.AllowedCars.AddRange(r.AllowedCars);
            AcLobby.AllowedMode = 0;
        }

        static int _maxOff; static AcCarRig _offRig;
        static void TraceOff()
        {
            if (_offRig == null || _offRig.Physics == null) return;
            AcTelemetry t = _offRig.Physics.Telemetry;
            int n = AcTiming.WheelsOff(ref t);
            if (n > _maxOff) _maxOff = n;
        }

        static bool _dOut; static int _dLaps, _dFrames; static AcCarRig _dRig;
        /// <summary>Logs the timer state on every out-lap / lap change and every 150 frames of a scripted drive.</summary>
        static void TraceDrive()
        {
            _dFrames++;
            if (AcLocal.OutLap != _dOut || AcLocal.Laps != _dLaps || _dFrames % 150 == 0)
                AcLabRunner.Log("  drive f" + _dFrames + ": out lap " + AcLocal.OutLap + ", laps " + AcLocal.Laps + ", progress " + AcLocal.Progress.ToString("0.000")
                                + ", distance " + AcLocal.RaceDistance.ToString("0.000") + ", lap ms " + AcLocal.LapMs + ", clock " + AcSpawner.CarClock(_dRig).ToString("0.00")
                                + ", phase " + AcSession.Phase);
            _dOut = AcLocal.OutLap; _dLaps = AcLocal.Laps;
        }
        static void OnStarted() { _raceStarted = true; }

        static IEnumerator Solo(AcLabRunner r)
        {
            AcSession.LapCompleted += OnLap;
            AcSession.RaceFinished += OnFinished;
            AcSession.RaceStarted += OnStarted;
            yield return r.Wait(() => r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            string trackId = AcLabBoot.Arg("-actrack", "magione"), layoutId = AcLabBoot.Arg("-aclayout", "");
            AcLayoutEntry layout = content.Layout(trackId, layoutId);
            if (layout == null) { AcTrackEntry t = content.Track(trackId); if (t != null && t.Layouts.Count > 0) layout = t.Layouts[0]; }
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + "/" + layoutId + " owned")) yield break;
            AcCarEntry car = content.Car(AcLabBoot.Arg("-accar", "abarth500"));
            if (car == null || !car.Owned) for (int i = 0; i < content.Cars.Count && (car == null || !car.Owned); i++) car = content.Cars[i];
            r.Check(AcRace.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : ""), "ChooseCar " + car.Id + " in the hub");

            // ---- load + spawn in the pit box
            AcRules rules = AcRace.DefaultRules();
            rules.Session = AcSessionType.Race; rules.Laps = 2; rules.AiCount = 0;
            yield return AcLabRace.LobbySeeded();
            AcLabRace.SyncLobby(rules);
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key + " (alone = master)");
            bool sawLoading = false;
            yield return r.Wait(() => { if (AcSession.Phase == AcPhase.Loading) sawLoading = true; return AcSession.Phase == AcPhase.Practice || (sawLoading && AcSession.Phase == AcPhase.Hub); }, 900f, "practice on " + layout.Key);
            if (!r.Check(AcSession.Phase == AcPhase.Practice, "phase Practice after the load (track built, car spawned)")) yield break;
            yield return r.Wait(() => AcRace.LocalCar != null && AcRace.LocalCar.Root != null, 180f, "my car");
            if (r.Failed) yield break;
            AcCarRig rig = AcRace.LocalCar;
            r.Check(AcRace.PitBox >= 0, "my pit box " + AcRace.PitBox);
            AcSpawnPoint box = AcTrackWorld.PitBoxes[Math.Max(0, AcRace.PitBox)];
            yield return r.Frames(10);
            Vector3 p0 = AcSpawner.CarPosition(rig);
            CheckOnGround(r, rig, box, "pit box");
            AimAt(r, rig, 6f);
            r.Shot("race-pitbox");
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Vector3 p1 = AcSpawner.CarPosition(rig);
            r.Check(Mathf.Abs(p1.y - p0.y) < 0.5f && (p1 - p0).magnitude < 1f, "the car still stands in its box after 3 s (moved " + (p1 - p0).magnitude.ToString("0.00") + " m)");

            // ---- practice: out lap + one timed lap along the fast lane
            AcAiSpline line = AcTrackWorld.AiLine;
            if (!r.Check(line != null && !line.IsEmpty && line.IsClosed, "a closed fast lane")) yield break;
            int lapsBefore = AcLocal.Laps;
            r.Check(AcLocal.OutLap, "practice starts on the out lap");
            int start = NearestIndex(line, rig);
            AcLabRunner.Log("practice drive from fast-lane point " + start + " (progress " + line.PointProgress(start).ToString("0.000") + ", " + line.Count + " points, " + line.LapLength.ToString("0") + " m)");
            _dOut = AcLocal.OutLap; _dLaps = AcLocal.Laps; _dFrames = 0; _dRig = rig;
            yield return Drive(rig, line, start, (line.Count - start) % line.Count + line.Count + line.Count / 20, 0f, 0, 0, TraceDrive);   // to the line, then one lap
            r.Check(!AcLocal.OutLap, "the out lap ended at the first crossing");
            r.Check(AcLocal.Laps == lapsBefore + 1, "practice: exactly one timed lap counted (laps " + AcLocal.Laps + ", last " + AcRaceMath.FormatLap(AcLocal.LastMs) + ")");

            // ---- START alone: grid, lights, race
            Laps.Clear(); _raceFinished = false; _raceStarted = false;
            r.Check(AcRace.RequestStart(rules), "RequestStart (race, 2 laps)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown, 10f, "the grid");
            if (AcSession.Phase != AcPhase.Grid && AcSession.Phase != AcPhase.Countdown) yield break;   // (r.Failed may hold an earlier, non-fatal FAIL)
            r.Check(AcLocal.GridSlot == 0, "my grid slot 0 (pole), got " + AcLocal.GridSlot);
            yield return r.Frames(5);
            CheckOnGround(r, rig, AcTrackWorld.StartGrid[0], "grid slot 0");
            r.Check(rig.Physics == null || rig.Physics.Frozen, "the car is frozen on the grid");
            AimAt(r, rig, 7f);
            r.Shot("race-grid");
            var seen = new List<int>();
            int last = -1; bool shotLights = false;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (AcSession.Phase != AcPhase.Racing && Time.realtimeSinceStartup - t0 < 30f)
            {
                if (AcRace.LightsReds != last)
                {
                    last = AcRace.LightsReds; seen.Add(last);
                    AcLabRunner.Log("lights: " + last + " red(s) at UTC-green " + (AcSession.UtcNow - AcSession.StartUtc).ToString("0.00") + " s, phase " + AcSession.Phase);
                    if (last == 3 && !shotLights) { shotLights = true; AimLights(r, rig); r.Shot("race-lights"); }
                }
                yield return null;
            }
            r.Check(AcSession.Phase == AcPhase.Racing && _raceStarted, "lights out -> Racing + RaceStarted");
            r.Check(seen.Contains(1) && seen.Contains(5) && AcRace.LightsGreen, "red steps 1..5 then green (seen " + string.Join(",", seen) + ")");

            // lap 1 on the line from the grid: the first crossing (grid before the line, T3) must not count
            start = NearestIndex(line, rig);
            float grid = line.PointProgress(start);
            _dRig = rig; _dFrames = 0; yield return Drive(rig, line, start, line.Count / 20, 0f, 0, 0, TraceDrive);   // over the line
            r.Check(AcLocal.Laps == 0, "grid " + (grid > 0.5f ? "before" : "after") + " the line (progress " + grid.ToString("0.000") + "): first crossing is not a lap (laps " + AcLocal.Laps + ")");
            int i1 = (start + line.Count / 20) % line.Count;
            yield return Drive(rig, line, i1, line.Count, 0f, 0, 0, TraceDrive);           // lap 1
            r.Check(AcLocal.Laps == 1, "lap 1 counted (laps " + AcLocal.Laps + ")");
            r.Check(Laps.Count >= 1 && Laps[Laps.Count - 1].Valid, "lap 1 valid");
            // lap 2: 250 m outside the left edge in the middle of the lap
            bool contacts = false;
            int i2 = (i1 + line.Count) % line.Count;
            int offFrom = line.Count / 2, offLen = (int)(250f / Mathf.Max(0.5f, line.LapLength / line.Count));
            yield return Drive(rig, line, i2, line.Count + line.Count / 30, 4f, offFrom, offLen, () => { TraceDrive(); AcTelemetry t = rig.Physics != null ? rig.Physics.Telemetry : default(AcTelemetry); if (t.LF.Contact || t.RR.Contact) contacts = true; });
            r.Check(AcLocal.Laps >= 2, "lap 2 counted (laps " + AcLocal.Laps + ")");
            if (contacts) r.Check(Laps.Count >= 2 && !Laps[Laps.Count - 1].Valid, "lap 2 cut by the 250 m off the track (ALLOWED_TYRES_OUT " + AcSession.Rules.AllowedTyresOut + ")");
            else AcLabRunner.Log("note: P reported no wheel contacts while the car was moved by ResetTo - the cut rule was not exercised here");
            r.Check(AcLocal.Finished, "finished after " + rules.Laps + " laps (" + AcRaceMath.FormatLap(AcLocal.FinishedMs) + ")");
            r.Check(AcLocal.Position == 1, "P1 alone (position " + AcLocal.Position + ")");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Results, 15f, "the results (everyone finished)");
            r.Check(_raceFinished, "RaceFinished raised");
            r.Check(AcRace.Standings.Count >= 1 && AcRace.Standings[0].IsLocal && AcRace.Standings[0].Finished, "results: me first, finished");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice, AcRace.Defaults.ResultScreenSeconds + 15f, "back to practice after the results");
            yield return r.Frames(10);
            CheckOnGround(r, rig, AcTrackWorld.PitBoxes[Math.Max(0, AcRace.PitBox)], "pit box after the race");
            AimAt(r, rig, 6f);
            r.Shot("race-back-in-pits");
            AcSession.LapCompleted -= OnLap;
            AcSession.RaceFinished -= OnFinished;
            AcSession.RaceStarted -= OnStarted;
        }

        /// <summary>The car stands on the road right under the marker: XZ within 1.5 m of it, the body within 0.05..1.5 m above the ground under it.</summary>
        static void CheckOnGround(AcLabRunner r, AcCarRig rig, AcSpawnPoint sp, string what)
        {
            Vector3 c = AcSpawner.CarPosition(rig);
            Vector3 flat = c - sp.Marker; flat.y = 0f;
            RaycastHit hit;
            bool ground = Physics.Raycast(c + Vector3.up * 0.2f, Vector3.down, out hit, 20f, AcLayers.GroundMask, QueryTriggerInteraction.Ignore);
            float above = ground ? c.y - hit.point.y : float.NaN;
            float heading = rig.Root.transform.eulerAngles.y;
            float dh = Mathf.Abs(Mathf.DeltaAngle(heading, sp.HeadingDeg));
            r.Check(flat.magnitude < 1.5f, what + ": car XZ " + flat.magnitude.ToString("0.00") + " m from the marker");
            r.Check(ground && above > 0.05f && above < 1.5f, what + ": body " + (ground ? above.ToString("0.00") : "?") + " m above the road (marker " + (sp.Marker.y - (ground ? hit.point.y : c.y)).ToString("0.00") + " m above it)");
            r.Check(dh < 5f, what + ": heading " + heading.ToString("0.0") + " vs marker " + sp.HeadingDeg.ToString("0.0") + " deg");
        }

        static int NearestIndex(AcAiSpline line, AcCarRig rig)
        {
            float x, y, z;
            AcTrackWorld.ToAc(AcSpawner.CarPosition(rig), out x, out y, out z);
            return line.Nearest(x, y, z);
        }

        /// <summary>Moves the car along the fast lane with ResetTo, 3 points per physics step (~210 m/s at 50 Hz), <paramref name="count"/> points from <paramref name="from"/>;
        /// points <paramref name="offFrom"/> .. +<paramref name="offLen"/> (absolute indices) are shifted <paramref name="offLeft"/> m beyond the left edge.</summary>
        static IEnumerator Drive(AcCarRig rig, AcAiSpline line, int from, int count, float offLeft, int offFrom, int offLen, Action each = null)
        {
            for (int k = 0; k <= count; k += 3)
            {
                int i = (from + k) % line.Count, j = (i + 1) % line.Count;
                float x = line.X(i), y = line.Y(i), z = line.Z(i);
                float fx = line.X(j) - x, fz = line.Z(j) - z, fl = Mathf.Sqrt(fx * fx + fz * fz);
                if (offLeft > 0f && fl > 1e-4f && InRange(i, offFrom, offLen, line.Count))
                {
                    float side = line.P(i, AcAiSpline.SideLeft) + offLeft;
                    x += fz / fl * side; z += -fx / fl * side;          // AC left of travel = (f.z, -f.x)
                }
                Vector3 p = AcTrackWorld.ToUnity(x, y, z);
                Vector3 a = AcTrackWorld.ToUnity(line.X(j), line.Y(j), line.Z(j)) - AcTrackWorld.ToUnity(line.X(i), line.Y(i), line.Z(i));
                float heading = Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg;
                bool hit;
                Vector3 g = AcSpawner.GroundUnder(p + Vector3.up * 0.5f, out hit);
                AcSpawner.Place(rig, g, heading);
                if (each != null) each();
                // one move per physics step: R times from the physics clock, and the lab runs thousands of frames a second in batch, so a
                // move per FRAME jumped ~200 m (50 frames) between two timer steps and the chord-limited race distance lost ~12 % a lap
                // (lab 2026-11-01: practice lap not counted, distance 0.85 at progress 0.95). Real cars move < 2 m per step.
                double c0 = AcSpawner.CarClock(rig);
                float w0 = Time.realtimeSinceStartup;
                while (AcSpawner.CarClock(rig) <= c0 && Time.realtimeSinceStartup - w0 < 1f) yield return null;
            }
            { double c1 = AcSpawner.CarClock(rig) + Time.fixedDeltaTime * 1.5; float w1 = Time.realtimeSinceStartup;   // two more timer steps on the last pose
              while (AcSpawner.CarClock(rig) < c1 && Time.realtimeSinceStartup - w1 < 1f) yield return null; }
        }


        // ------------------------------------------------------------------ race-repair (iteration 2, DAMAGE_DESIGN 1.4 / 1.6 / 4 / 10)

        static int _repairedEvents;
        static void OnCarDamaged(AcCarRig rig, AcDamageChange what, float kmh)
        {
            if (rig == null || rig != AcRace.LocalCar) return;
            if ((what & AcDamageChange.Repaired) != 0) _repairedEvents++;
        }

        /// <summary>race-repair: magione practice at DAMAGE 100: DamageRate 1 + gate; a front 60 hit with a bent LF rod; stop in my box ->
        /// AcRace.Repair(Body + Suspension) frozen for RepairSeconds +-0.2 s, then clean and free; reset keeps damage; BACK TO PITS repairs; the
        /// rules going 100 -> 0 repair and stop the damage (a hit adds nothing); race: a damaged car is clean on the grid, a blown engine
        /// retires it (LocalRetired, standings Retired, the director closes the race alone), back to practice clean.</summary>
        static IEnumerator Repair(AcLabRunner r)
        {
            AcEvents.Message += OnMessage;
            AcEvents.CarDamaged += OnCarDamaged;
            yield return r.Wait(() => r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            string trackId = AcLabBoot.Arg("-actrack", "magione"), layoutId = AcLabBoot.Arg("-aclayout", "");
            AcLayoutEntry layout = content.Layout(trackId, layoutId);
            if (layout == null) { AcTrackEntry t = content.Track(trackId); if (t != null && t.Layouts.Count > 0) layout = t.Layouts[0]; }
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + " owned")) yield break;
            AcCarEntry car = content.Car(AcLabBoot.Arg("-accar", "ferrari_458"));
            if (car == null || !car.Owned) car = content.Car("abarth500");
            if (!r.Check(car != null && car.Owned, "car owned")) yield break;
            AcRace.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : "");
            AcRules rules = AcRace.DefaultRules();
            r.Check(rules.DamageMultiplier == AcDamageRules.Current.DefaultMultiplier || !AcDamageRules.Loaded,
                    "lobby default DAMAGE_MULTIPLIER " + rules.DamageMultiplier + " = server_cfg.ini (" + AcDamageRules.Current.DefaultMultiplier + ")");
            rules.Session = AcSessionType.Practice; rules.AiCount = 0; rules.DamageMultiplier = 100; rules.Laps = 1;
            yield return LobbySeeded();
            SyncLobby(rules);
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key + " at DAMAGE 100");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice && AcRace.LocalCar != null && AcRace.LocalCar.Root != null, 900f, "practice + my car");
            if (r.Failed) yield break;
            AcCarRig rig = AcRace.LocalCar;
            AcCarPhysics ph = rig.Physics;
            yield return r.Frames(20);
            r.Check(ph.DamageRate == 1f, "DamageRate 1 from DAMAGE_MULTIPLIER 100 (" + ph.DamageRate + ")");
            r.Check(AcCarDamageGate.TexturesWanted == AcCarDamageGate.VisualOn, "AcCarDamageGate.TexturesWanted = " + AcCarDamageGate.TexturesWanted);

            // ---- hit + pit-box repair
            yield return r.Wait(() => AcRace.InPitBoxStopped, 10f, "my car stands in its pit box");
            r.Check(ph.DebugAddImpact(AcDamageZone.Front, 60f, 0), "front 60 km/h hit with the LF rod");
            AcCarDamage d = ph.Damage;
            AcLabRunner.Log("after the hit: body " + d.BodyPct.ToString("0") + " %, susp " + d.SuspPct.ToString("0.0") + " %, engine " + d.EnginePct.ToString("0") + " %");
            float est = AcRace.RepairEstimate(AcRepair.Suspension);
            r.Check(est > 0f && Mathf.Abs(est - d.RepairSeconds(AcRepair.Body | AcRepair.Suspension)) < 0.001f, "estimate BODY + SUSPENSION " + est.ToString("0.0") + " s = RepairSeconds");
            string why;
            r.Check(!AcRace.CanRepair(AcRepair.Engine, out why), "engine row refused while the engine is undamaged (" + why + ")");
            AimAt(r, rig, 6f);
            r.Shot("race-repair-damaged");
            float t0 = Time.unscaledTime;
            r.Check(AcRace.Repair(AcRepair.Suspension), "AcRace.Repair(Suspension) in the box");
            r.Check(AcRace.RepairActive && ph.Frozen && Mathf.Abs(AcRace.RepairTotalSeconds - est) < 0.01f, "repair running, car frozen, " + AcRace.RepairTotalSeconds.ToString("0.0") + " s");
            yield return r.Frames(30);
            r.Check(AcRace.RepairSecondsLeft > 0f && AcRace.RepairSecondsLeft < est, "countdown " + AcRace.RepairSecondsLeft.ToString("0.0") + " s left");
            yield return r.Wait(() => !AcRace.RepairActive, est + 10f, "the repair ends");
            float took = Time.unscaledTime - t0;
            r.Check(Mathf.Abs(took - est) <= 0.25f, "frozen for " + took.ToString("0.00") + " s (RepairSeconds " + est.ToString("0.00") + " +-0.2)");
            r.Check(d.Pristine && !ph.Frozen && _repairedEvents > 0, "clean and free after the repair (Repaired events " + _repairedEvents + ")");

            // ---- reset keeps, back to pits repairs
            ph.DebugAddImpact(AcDamageZone.Rear, 40f);
            AcAiSpline line = AcTrackWorld.AiLine;
            if (line != null && !line.IsEmpty)
            {
                yield return PlaceBeside(rig, line, line.Count / 3, 10f);
                yield return r.Frames(100);
                bool reset = AcRace.ResetCar();
                yield return r.Frames(10);
                r.Check(reset && d.ZoneByte((int)AcDamageZone.Rear) == 40, "R reset keeps the damage (rear " + d.ZoneByte(1) + ")");
                r.Check(!AcRace.Repair(AcRepair.Body), "no repair outside the pit box");
            }
            r.Check(AcRace.BackToPits() && d.Pristine, "BACK TO PITS = full repair");

            // ---- 100 -> 0: repaired, damage off
            ph.DebugAddImpact(AcDamageZone.Left, 30f);
            AcRules off = rules.Clone(); off.DamageMultiplier = 0;
            r.Check(AcRace.RequestRules(off), "RequestRules DAMAGE 0");
            yield return r.Frames(20);
            r.Check(ph.DamageRate == 0f && d.Pristine, "DAMAGE 0 mid-session: DamageRate 0, the car repaired");
            r.Check(!ph.DebugAddImpact(AcDamageZone.Front, 80f) && d.Pristine, "DAMAGE 0: a hit adds nothing");
            AcRules on = rules.Clone(); on.Session = AcSessionType.Race; on.DamageMultiplier = 100;
            SyncLobby(on);
            r.Check(AcRace.RequestRules(on), "RequestRules DAMAGE 100 (race next)");
            yield return r.Frames(20);

            // ---- race: clean on the grid, blown engine -> retired, DNF
            r.Check(ph.DebugAddImpact(AcDamageZone.Right, 50f), "a right 50 hit before the race");
            r.Check(AcRace.RequestStart(on), "START a 1-lap race at DAMAGE 100");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown, 15f, "grid");
            yield return r.Frames(10);
            r.Check(d.Pristine, "the damaged car is clean on the grid");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Racing, 30f, "lights out");
            if (r.Failed) yield break;
            yield return r.Frames(30);
            Messages.Clear();
            d.EngineLife = 0f; d.EngineDead = true;     // lab: a blown engine (the life model itself is P's physics-engine scenario)
            yield return r.Wait(() => AcRace.LocalRetired, 5f, "retired after the engine blew");
            r.Check(d.Retired && Messages.Contains("ENGINE BLOWN - RETIRED"), "retired bit set for the wire + 'ENGINE BLOWN - RETIRED'");
            yield return r.Frames(20);
            bool row = false;
            foreach (AcStanding s in AcRace.Standings) if (s.IsLocal) row = s.Retired && !s.Finished;
            r.Check(row, "standings: my row Retired (DNF)");
            r.Check(!AcRace.Repair(AcRepair.Engine), "a retired car gets no pit-box repair in the race");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Results, 10f, "the director closes the race (every racer retired)");
            r.Shot("race-repair-results");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice, AcRace.Defaults.ResultScreenSeconds + 15f, "back to practice");
            yield return r.Frames(10);
            r.Check(d.Pristine && !d.Retired && !AcRace.LocalRetired, "back in practice: repaired, not retired");
            AcEvents.Message -= OnMessage;
            AcEvents.CarDamaged -= OnCarDamaged;
        }

        // ------------------------------------------------------------------ race-damage-live (flow pass 2026-11-02)

        /// <summary>
        /// <c>race-damage-live</c>: the damage flow as a player meets it on magione practice at DAMAGE 100 with the car chosen through F
        /// (AcFront.ChooseCar, so AcLocal.CarId / the GARAGE match): (1) a pristine roll out of the box and a roll after a front 60 + LF rod hit:
        /// the car's own wheel event flutter layer (suspension_damage from P's live damage, not A's lab override) silent pristine, audible
        /// damaged; BACK TO PITS repairs. (2) seated by S (stand-in body, monitor): a live front 12 hit pops H's displayer from the real
        /// CarDamaged (not SetFakeDamage) for TIME s, then it hides. (3) REPAIR BODY seated in the box: frozen, H's displayer + countdown
        /// line while it runs (desk shot, then the VR panel from S's eye - the cell's angle from straight ahead is logged), A's unscrew at the
        /// start and screw at the end through R's RepairSound hook, pristine + released after RepairSeconds. Pictures live-dmg-*.png.
        /// </summary>
        static IEnumerator DamageLive(AcLabRunner r)
        {
            AcEvents.Message += OnMessage;
            yield return r.Wait(() => r.Game.Content != null && AcFront.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            AcTrackEntry tr = content.Track(AcLabBoot.Arg("-actrack", "magione"));
            AcLayoutEntry layout = tr != null && tr.Layouts.Count > 0 ? tr.Layouts[0] : null;
            if (!r.Check(layout != null && layout.Owned, "layout owned")) yield break;
            AcCarEntry car = content.Car(AcLabBoot.Arg("-accar", "ferrari_458"));
            if (!r.Check(car != null && car.Owned, "car owned")) yield break;
            r.Check(AcFront.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : "", "lab") && AcLocal.CarId == car.Id, "F's ChooseCar " + car.Id + " (AcLocal.CarId " + AcLocal.CarId + ")");
            AcRules rules = AcRace.DefaultRules();
            rules.Session = AcSessionType.Practice; rules.AiCount = 0; rules.DamageMultiplier = 100;
            yield return LobbySeeded();
            SyncLobby(rules);
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key + " at DAMAGE 100");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice && AcRace.LocalCar != null && AcRace.LocalCar.Root != null && AcRace.LocalCar.Audio != null && AcRace.LocalCar.Audio.Ready, 900f, "practice + my car + its sound");
            if (r.Failed) yield break;
            AcCarRig rig = AcRace.LocalCar;
            AcCarPhysics ph = rig.Physics;
            AcCarDamage d = ph.Damage;
            AcCarAudio audio = rig.Audio;
            yield return r.Wait(() => AcRace.InPitBoxStopped, 15f, "my car stands in its pit box");
            if (r.Failed) yield break;

            // ---- (1) flutter from the live damage state on a short roll
            var vol = new float[2];
            var maxKmh = new float[2];
            var tmp = new List<AcAudioVoice>();
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) r.Check(ph.DebugAddImpact(AcDamageZone.Front, 60f, 0) && d.MaxSuspDamage01 > 0.5f, "front 60 hit with the LF rod (susp " + d.MaxSuspDamage01.ToString("0.00") + ")");
                ph.SetOccupied(true); ph.SetDriven(true);
                float t0 = Time.time;
                while (Time.time - t0 < 5f)
                {
                    float t = Time.time - t0;
                    ph.Input = AcCarInput.Neutral;
                    if (t < 3f) ph.Input.Throttle = 0.5f; else ph.Input.Brake = 1f;
                    maxKmh[pass] = Mathf.Max(maxKmh[pass], ph.SpeedKmh);
                    tmp.Clear(); audio.Tap(tmp);
                    for (int v = 0; v < tmp.Count; v++)
                        if (tmp[v].Clip != null && tmp[v].Clip.name.IndexOf("flutter", StringComparison.OrdinalIgnoreCase) >= 0) vol[pass] = Mathf.Max(vol[pass], tmp[v].Volume);
                    yield return null;
                }
                ph.Input = AcCarInput.Neutral;
                ph.SetDriven(false); ph.SetOccupied(false);
                AcLabRunner.Log("live-dmg: roll " + (pass == 0 ? "pristine" : "damaged") + " max " + maxKmh[pass].ToString("0") + " km/h, flutter voice volume " + vol[pass].ToString("0.0000") + ", zones F/R/L/R/C "
                                + d.ZoneByte(0) + "/" + d.ZoneByte(1) + "/" + d.ZoneByte(2) + "/" + d.ZoneByte(3) + "/" + d.ZoneByte(4));
                if (pass == 1) { AimAt(r, rig, 6f); r.Shot("live-dmg-rolled"); }
                r.Check(AcRace.BackToPits(), "BACK TO PITS after roll " + pass);
                yield return r.Frames(10);
                r.Check(d.Pristine, "pristine in the box after BACK TO PITS (roll " + pass + ")");
            }
            r.Check(vol[0] < 0.01f, "pristine roll: flutter silent (" + vol[0].ToString("0.0000") + ")");
            r.Check(vol[1] > vol[0] + 0.005f, "damaged roll: the car's flutter layer sounds (" + vol[1].ToString("0.0000") + ")");
            yield return r.Wait(() => AcRace.InPitBoxStopped, 15f, "stopped in the box again");

            // ---- (2) seated (S, monitor): a live hit pops H's displayer for TIME s
            var root = new GameObject("Lab Damage Player").transform;
            var head = new GameObject("Lab Head").transform; head.SetParent(root, false); head.localPosition = new Vector3(0f, 1.2f, 0f);
            var left = new GameObject("Lab Left").transform; left.SetParent(root, false); left.localPosition = new Vector3(-0.25f, 0.9f, 0.3f);
            var right = new GameObject("Lab Right").transform; right.SetParent(root, false); right.localPosition = new Vector3(0.25f, 0.9f, 0.3f);
            root.position = rig.Root.transform.position + rig.Root.transform.right * -2.5f;
            AcSeat.LabRoot = root; AcSeat.LabHead = head; AcSeat.LabLeft = left; AcSeat.LabRight = right; AcSeat.LabVr = false;
            AcCarCamera.LabCamera = r.Cam;
            bool entered = false;
            try { entered = AcSeat.Enter(rig, AcSeatKind.Driver); } catch (Exception e) { AcLabRunner.Log("AcSeat.Enter threw " + e.Message); }
            yield return r.Wait(() => AcHud.Shown && AcHud.Desk != null, 10f, "H shown from S's SeatChanged (Enter " + entered + ")");
            if (!r.Failed)
            {
                AcHudView v = AcHud.Desk.View;
                // the damaged roll's own wall contact (bent rod -> toe) popped the displayer < TIME s ago; BACK TO PITS repaired, the rest of TIME runs out
                float tq = Time.unscaledTime;
                yield return r.Wait(() => AcHud.DamageShowLeft <= 0f || Time.unscaledTime - tq > 6f, 10f, "the roll's displayer time");
                yield return r.Frames(5);
                r.Check(!v.LabDamageShown, "seated, undamaged: no displayer");
                r.Check(ph.DebugAddImpact(AcDamageZone.Front, 12f, -1), "a live front 12 km/h touch (no corner)");
                yield return r.Frames(5);
                r.Check(v.LabDamageShown && AcHud.DamageShowLeft > 3f, "the displayer pops from the real CarDamaged (" + AcHud.DamageShowLeft.ToString("0.0") + " s left)");
                DeskFrame(r);
                yield return r.Frames(2);
                r.Shot("live-dmg-hit-desk", 1920, 1080);
                float tw = Time.unscaledTime + AcDamageRules.Current.DisplayTimeS + 0.5f;
                yield return r.Wait(() => Time.unscaledTime > tw, 15f, "TIME s");
                yield return r.Frames(3);
                r.Check(!v.LabDamageShown, "hidden again after damage_displayer TIME " + AcDamageRules.Current.DisplayTimeS + " s");

                // ---- (3) seated REPAIR BODY in the box: H's countdown, A's sounds through R's hook
                yield return r.Wait(() => AcRace.InPitBoxStopped, 5f, "stopped in the box (seated)");
                float est = AcRace.RepairEstimate(AcRepair.Body);
                int unscrew = 0, screw = 0;
                bool prevU = false, prevS = false;
                r.Check(est > 0f && AcRace.Repair(AcRepair.Body), "REPAIR BODY seated (" + est.ToString("0.0") + " s)");
                float started = Time.unscaledTime;
                bool deskShot = false, vrShot = false;
                string line = "";
                while (AcRace.RepairActive && Time.unscaledTime - started < est + 10f)
                {
                    tmp.Clear(); audio.Tap(tmp);
                    bool u = false, s = false;
                    for (int k = 0; k < tmp.Count; k++) { if (tmp[k].Event == "unscrew") u = true; else if (tmp[k].Event == "screw") s = true; }
                    if (u && !prevU) unscrew++;
                    if (s && !prevS) screw++;
                    prevU = u; prevS = s;
                    float el = Time.unscaledTime - started;
                    if (!deskShot && el > 2f)
                    {
                        deskShot = true;
                        line = v.LabDamageRepair;
                        r.Check(ph.Frozen && v.LabDamageShown && line.Length > 0, "repairing: frozen, displayer shown with the countdown line '" + line + "'");
                        DeskFrame(r);
                        yield return r.Frames(2);
                        r.Shot("live-dmg-repair-desk", 1920, 1080);
                        AcSeat.LabVr = true; AcHud.ForceVr = true;
                    }
                    else if (deskShot && !vrShot && el > 6f && AcHud.Vr != null && AcHud.Vr.Go.activeSelf)
                    {
                        vrShot = true;
                        AcHudVr vr = AcHud.Vr;
                        Vector3 eye, fwd;
                        if (vr.EyeWorld(out eye, out fwd))
                        {
                            Vector3 to = vr.View.LabDamageRect.position - eye;
                            Vector3 fl = fwd; fl.y = 0f; Vector3 tl = to; tl.y = 0f;
                            float yaw = Vector3.SignedAngle(fl, tl, Vector3.up), pitch = Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                            AcLabRunner.Log("live-dmg: VR damage cell " + to.magnitude.ToString("0.00") + " m from the eye, yaw " + yaw.ToString("0") + " deg, pitch " + pitch.ToString("0") + " deg (repair line '" + vr.View.LabDamageRepair + "')");
                            r.Check(vr.View.LabDamageShown && vr.View.LabDamageRepair.Length > 0, "VR: damage cell with the countdown while repairing");
                            r.Check(Mathf.Abs(yaw) < 45f && !vr.View.GTimes.gameObject.activeSelf, "VR: the cell within 45 deg of straight ahead, in the times box's place (yaw " + yaw.ToString("0") + ")");   // // ours: a headset's half view
                            r.Aim(eye, eye + fwd * 2f - Vector3.up * 0.35f, 100f);
                            r.Shot("live-dmg-repair-vr-eye", 1440, 1080);
                        }
                        AcSeat.LabVr = false; AcHud.ForceVr = false;
                    }
                    yield return null;
                }
                for (int i = 0; i < 30; i++)
                {
                    tmp.Clear(); audio.Tap(tmp);
                    bool s = false;
                    for (int k = 0; k < tmp.Count; k++) if (tmp[k].Event == "screw") s = true;
                    if (s && !prevS) screw++;
                    prevS = s;
                    yield return null;
                }
                float took = Time.unscaledTime - started;
                r.Check(!AcRace.RepairActive && Mathf.Abs(took - est) < 0.6f, "the repair took " + took.ToString("0.0") + " s (RepairSeconds " + est.ToString("0.0") + ")");
                r.Check(d.Pristine && !ph.Frozen, "pristine and released after the repair");
                r.Check(unscrew == 1 && screw == 1, "A through R's hook: unscrew x" + unscrew + " at the start, screw x" + screw + " at the end");
                r.Check(!v.LabDamageShown, "the displayer goes with the repair (a repair alone does not pop it)");
                r.Check(Messages.Contains("REPAIR DONE"), "R says REPAIR DONE");
            }
            AcSeat.LabVr = false; AcHud.ForceVr = false;
            try { AcSeat.ForceExit(); } catch (Exception) { }
            AcSeat.LabRoot = AcSeat.LabHead = AcSeat.LabLeft = AcSeat.LabRight = null;
            AcCarCamera.LabCamera = null;
            UnityEngine.Object.Destroy(root.gameObject);
            AcEvents.Message -= OnMessage;
        }

        /// <summary>The monitor overlay as a world plane 1 m before the lab camera, parented to it: S's car camera moves r.Cam every frame while
        /// seated, and an unparented plane stayed where the camera was at LabFrame (first race-damage-live run: HUD cut off at the left).</summary>
        static void DeskFrame(AcLabRunner r)
        {
            AcHud.Desk.LabFrame(r.Cam, 16f / 9f);
            AcHud.Desk.Go.transform.SetParent(r.Cam.transform, true);
        }

        static bool InRange(int i, int from, int len, int n) { int d = ((i - from) % n + n) % n; return d < len; }

        static void AimAt(AcLabRunner r, AcCarRig rig, float dist)
        {
            Transform t = rig.Root.transform;
            Vector3 c = t.position;
            r.Aim(c + t.right * dist * 0.8f - t.forward * dist * 0.6f + Vector3.up * 2.5f, c + Vector3.up * 0.5f, 60f);
        }

        static void AimLights(AcLabRunner r, AcCarRig rig)
        {
            Transform t = rig.Root.transform;
            Vector3 c = t.position;
            r.Aim(c - t.forward * 6f + Vector3.up * 2.2f, c + t.forward * 40f + Vector3.up * 5f, 60f);
        }
    }
}
