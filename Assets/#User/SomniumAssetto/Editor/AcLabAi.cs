using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [I, AI drivers] Lab scenarios (DevTools/lab.sh &lt;name&gt; [-actrack magione] [-aclayout id] [-accar ferrari_458] [-acai n] [-aclevel 100]):
    /// <list type="bullet">
    /// <item><c>ai-magione</c>: practice on the layout with ONE AI car (the -accar car via AllowedCars, level -aclevel): it is built, placed
    /// in a free pit box from the far end (shot ai-pitbox), drives out along pit_lane.ai (leaving it at the pit exit) onto fast_lane.ai, and
    /// laps. The timed lap = the lap after R's FIRST LapCompleted of PlayerId -100 (the out lap) - R's events only. Shots at 25 / 50 / 75 % of
    /// the planned lap time (ai-lap25/50/75). Checks: lap within 10 % of the AI's own plan, and - when the install has a Kunos hotlap special
    /// event for the car on the layout (AcAiReference; -accar ks_toyota_gt86 on magione = SPECIAL_EVENT_53, gold 1:32.000; harness sim lap 2
    /// 1:32.8) - within 10 % of its gold time; no recovery; never more than 2 m outside the line's room; fuel used ~ the planned l/lap;
    /// physics present (state Rails = P's body missing).</item>
    /// <item><c>ai-race</c>: a 2-lap race alone with -acai (default 3) AI cars: the grid (AI behind my car, frozen; shot ai-grid), lights,
    /// green; my car stays still on pole (nobody drives it), so every AI car must get round it (AcEvents.CarImpact of an AI car near my car
    /// counts as a contact - must be 0); all AI cars finish 2 laps; shots at the start (ai-start), when two AI cars run within 12 m
    /// (ai-traffic) and at the end (ai-finish); order changes between AI cars logged (overtakes). On an open course (-actrack trento-bondone:
    /// R times one run) every AI car must finish the run and reach the run-out past AC_AB_FINISH (REQUESTS "R -> I: open courses").</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    static class AcLabAi
    {
        static AcLabAi()
        {
            AcLabRunner.Register("ai-magione", r => Practice(r));
            AcLabRunner.Register("ai-race", r => Race(r));
            AcLabRunner.Register("ai-retire", r => Retire(r));
            AcLabRunner.Register("ai-retire-race", r => RetireRace(r));
        }

        static readonly List<AcLap> Laps = new List<AcLap>();
        static int _contacts;

        static void OnLap(AcLap l)
        {
            Laps.Add(l);
            AcLabRunner.Log("lap event: " + l.Name + " (id " + l.PlayerId + ") lap " + l.Lap + " " + Ms(l.TimeMs) + (l.Valid ? " valid" : " CUT " + l.Cuts));
        }

        static void OnImpact(AcCarRig car, float kmh, Vector3 point)
        {
            if (car == null || car.Kind != AcCarKind.Ai) return;
            AcCarRig mine = AcRace.LocalCar;
            if (mine == null || mine.Root == null) return;
            if ((mine.Root.transform.position - point).magnitude >= 4f) return;
            // polish pass 2026-11-01: P's hull scrapes the road under braking / launch (REQUESTS "I -> P: the car's hull ..."), so an impact
            // near my car counts as a contact only when one of MY car's colliders is at the contact point (else it is logged as a scrape)
            Collider[] near = Physics.OverlapSphere(point, 0.4f, ~0, QueryTriggerInteraction.Ignore);
            bool mineHit = false;
            for (int i = 0; i < near.Length && !mineHit; i++) mineHit = near[i].transform.IsChildOf(mine.Root.transform);
            if (mineHit) { _contacts++; AcLabRunner.Log("contact: AI " + car.Car.Id + " hit my car at " + kmh.ToString("0") + " km/h at " + point.ToString("F2")); }
            else AcLabRunner.Log("impact of AI " + car.Car.Id + " near my car (" + kmh.ToString("0") + " km/h) with no collider of my car at the point (a road scrape)");
        }

        // ---- polish pass 2026-11-01: what hit the AI car (ai-magione lap 2 at 05:32 / 07:46: 113 -> 29 km/h in < 0.5 s at turn 1, all
        // four wheels in the air, yaw 4.4 rad/s) - every impact of the AI car with the colliders round the contact point, and a 50 Hz ring of
        // the car's state dumped once an anomaly shows (|yaw| > 1.5 rad/s, > 12 km/h lost in 0.1 s, or no wheel on the ground). Lab only.
        static int _impactLogs, _dumps;
        static readonly string[] Ring = new string[90];
        static readonly float[] RingV = new float[90];
        static int _ringN;
        static float _ringAt;

        static void OnAnyImpact(AcCarRig car, float kmh, Vector3 point)
        {
            if (car == null || car.Kind != AcCarKind.Ai || _impactLogs >= 25) return;
            _impactLogs++;
            var sb = new System.Text.StringBuilder(200);
            sb.Append("impact: AI ").Append(car.Car.Id).Append(' ').Append(kmh.ToString("0")).Append(" km/h at ").Append(point.ToString("F2"));
            if (car.Root != null) sb.Append(" (").Append((point - car.Root.transform.position).ToString("F2")).Append(" from the root)");
            if (AcAi.Drivers.Count > 0) sb.Append(" s ").Append(AcAi.Drivers[0].LineS.ToString("0"));
            Collider[] near = Physics.OverlapSphere(point, 0.3f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < near.Length && i < 6; i++)
            {
                Collider c = near[i];
                bool own = car.Root != null && c.transform.IsChildOf(car.Root.transform);
                sb.Append(i == 0 ? " | near: " : ", ").Append(own ? "own " : "").Append(c.name).Append(" L").Append(c.gameObject.layer).Append(c.isTrigger ? " trig" : "");
            }
            AcLabRunner.Log(sb.ToString());
        }

        /// <summary>Keeps the last 1.8 s of the AI car's state at 50 Hz (game time); dumps it (max 2 times) at an anomaly.</summary>
        static void RingSample(AcAiDriver d, AcCarRig rig, float t)
        {
            AcCarPhysics ph = rig != null ? rig.Physics : null;
            if (ph == null || ph.Body == null || d.Brain == null || Time.time < _ringAt) return;
            _ringAt = Time.time + 0.02f;
            AcTelemetry tl = ph.Telemetry;
            float yaw = -ph.Body.angularVelocity.y, v = ph.SpeedKmh;
            int air = 0;
            var sb = new System.Text.StringBuilder(220);
            sb.Append("  r t ").Append(t.ToString("0.00")).Append(" s ").Append(d.LineS.ToString("0")).Append(" v ").Append(v.ToString("0.0")).Append(" lat ").Append(d.Lateral.ToString("0.0"))
              .Append(" yaw ").Append(yaw.ToString("0.00")).Append(" road ").Append(ph.SteerRoadDeg.ToString("0.0")).Append(" thr ").Append(ph.Input.Throttle.ToString("0.00"))
              .Append(" brk ").Append(ph.Input.Brake.ToString("0.00")).Append(" g ").Append(tl.Gear).Append(" rpm ").Append(tl.Rpm.ToString("0")).Append(" sh ").Append(tl.Shifting ? 1 : 0)
              .Append(" abs ").Append(tl.AbsActive ? 1 : 0).Append(" up ").Append(ph.transform.up.y.ToString("0.000")).Append(" pos ").Append(ph.Body.position.ToString("F2"))
              .Append(" vy ").Append(ph.Body.linearVelocity.y.ToString("0.00"));
            for (int i = 0; i < 4; i++)
            {
                AcWheelState w = tl.Wheel(i);
                if (!w.Contact) air++;
                sb.Append(i == 0 ? " |" : " ;").Append(w.Contact ? " " : " AIR ").Append(w.LoadN.ToString("0")).Append(" sr").Append(w.SlipRatio.ToString("0.00"))
                  .Append(" a").Append(w.SlipAngleDeg.ToString("0.0")).Append(" tr").Append((w.TravelM * 1000f).ToString("0"));
            }
            int k = _ringN % Ring.Length;
            Ring[k] = sb.ToString(); RingV[k] = v; _ringN++;
            float vAgo = _ringN > 5 ? RingV[(_ringN - 6) % Ring.Length] : v;
            bool anomaly = Mathf.Abs(yaw) > 1.5f || vAgo - v > 12f || air == 4;
            if (anomaly && _dumps < 2 && _ringN > 10)
            {
                _dumps++;
                AcLabRunner.Log("ANOMALY " + _dumps + " (|yaw| " + Mathf.Abs(yaw).ToString("0.00") + ", lost " + (vAgo - v).ToString("0.0") + " km/h in 0.1 s, wheels in the air " + air + ") - last " + Mathf.Min(_ringN, Ring.Length) + " samples at 50 Hz:");
                for (int j = Mathf.Max(0, _ringN - Ring.Length); j < _ringN; j++) AcLabRunner.Log(Ring[j % Ring.Length]);
                _ringAt = Time.time + 1.5f; _ringN = 0; // ours: a fresh ring after a dump
            }
        }

        // ------------------------------------------------------------------ setup shared by both

        static IEnumerator Load(AcLabRunner r, int aiCount, AcSessionType session, int laps, Action<AcCarEntry> chosen)
        {
            yield return r.Wait(() => r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            string trackId = AcLabBoot.Arg("-actrack", "magione"), layoutId = AcLabBoot.Arg("-aclayout", "");
            AcLayoutEntry layout = content.Layout(trackId, layoutId);
            if (layout == null) { AcTrackEntry t = content.Track(trackId); if (t != null && t.Layouts.Count > 0) layout = t.Layouts[0]; }
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + "/" + layoutId + " owned")) yield break;
            AcCarEntry car = content.Car(AcLabBoot.Arg("-accar", "ferrari_458"));
            if (car == null || !car.Owned) for (int i = 0; i < content.Cars.Count && (car == null || !car.Owned); i++) car = content.Cars[i];
            r.Check(AcRace.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : ""), "ChooseCar " + car.Id + " (my car; the AI drive it too)");
            chosen(car);
            AcRules rules = AcRace.DefaultRules();
            rules.Session = session; rules.Laps = laps;
            rules.AiCount = aiCount;
            rules.AiLevel = Mathf.Clamp(int.Parse(AcLabBoot.Arg("-aclevel", "100")), 0, 100);
            rules.AllowedCars.Clear(); rules.AllowedCars.Add(car.Id);
            yield return AcLabRace.LobbySeeded();
            AcLabRace.SyncLobby(rules);
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key + " with " + aiCount + " AI (level " + rules.AiLevel + ")");
            bool sawLoading = false;
            yield return r.Wait(() => { if (AcSession.Phase == AcPhase.Loading) sawLoading = true; return AcSession.Phase == AcPhase.Practice || (sawLoading && AcSession.Phase == AcPhase.Hub); }, 900f, "practice on " + layout.Key);
            if (!r.Check(AcSession.Phase == AcPhase.Practice, "practice after the load")) yield break;
            r.Check(AcAi.Available, "AcAi.Available (fast lane loaded)");
            yield return r.Wait(() => AcAi.Drivers.Count == aiCount && AcAi.Ready, 300f, aiCount + " AI car(s) built");
            if (r.Failed) yield break;
            int built = 0;
            for (int i = 0; i < AcAi.Drivers.Count; i++) if (AcAi.Drivers[i].Rig != null) built++;
            r.Check(built == aiCount, "AI cars built " + built + "/" + aiCount);
            for (int i = 0; i < AcAi.Drivers.Count; i++)
            {
                AcAiDriver d = AcAi.Drivers[i];
                AcLabRunner.Log("AI " + d.Index + ": " + d.Name + " " + d.CarId + "/" + d.SkinId + " level " + d.Level + " state " + d.State + " planned " + Ms((int)(d.PlannedLapS * 1000f))
                                + (d.CarData != null ? " | " + d.CarData.Ai : ""));
            }
        }

        // ------------------------------------------------------------------ ai-magione

        static IEnumerator Practice(AcLabRunner r)
        {
            Laps.Clear();
            AcSession.LapCompleted += OnLap;
            AcEvents.CarImpact += OnAnyImpact;
            AcCarEntry car = null;
            yield return Load(r, 1, AcSessionType.Practice, 5, c => car = c);
            if (r.Failed) yield break;
            AcAiDriver d = AcAi.Drivers[0];
            AcCarRig rig = d.Rig;
            r.Check(d.State != AcAiState.Rails, "the AI car has P's physics (state " + d.State + ")");
            r.Check(d.OnPitLine || AcAi.PitTrack == null, "starts on the pit lane (pit_lane.ai " + (AcAi.PitTrack != null) + ")");
            Chase(r, rig, 7f);
            r.Shot("ai-pitbox");

            // out lap: wait for the first crossing (R starts timing it), then watch one full timed lap
            float t0 = Time.realtimeSinceStartup;
            yield return r.Wait(() => !d.OnPitLine, 180f, "the AI car leaves the pit lane");
            if (r.Failed) { AcLabRunner.Log("AI state " + d.State + " s " + d.LineS.ToString("0") + " target " + d.TargetKmh.ToString("0") + " km/h"); yield break; }
            AcLabRunner.Log("left the pits after " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s");
            // the timed lap = the lap AFTER R's first lap event of this car (that one is the out lap from the pits, or R's first crossing):
            // detected from R's own LapCompleted events only (not from fast_lane point 0, which need not be at the timing gate)
            int idLaps = -1;
            float maxOut = 0f, shotAt = 0.25f, lapStart = 0f, traceAt = 0f;
            float timeout = Time.realtimeSinceStartup + Mathf.Max(300f, d.PlannedLapS * 3.5f + 90f); // ours
            int recoveriesAtStart = d.Recoveries;
            AcAiTrack fast = AcAi.FastTrack;
            while (Time.realtimeSinceStartup < timeout)
            {
                int n = CountLaps(-100);
                if (idLaps < 0 && n >= 1)
                {
                    idLaps = n; recoveriesAtStart = d.Recoveries; maxOut = 0f; shotAt = 0.25f; lapStart = Time.time;
                    AcLabRunner.Log("first lap event (out lap " + Ms(LastLap(-100).TimeMs) + ") - the timed lap begins");
                }
                if (idLaps >= 0 && n > idLaps) break; // the timed lap is in (R raised it)
                if (idLaps >= 0 && Time.time >= traceAt) { traceAt = Time.time + 0.5f; Trace(d, rig, Time.time - lapStart); } // ours: 2 Hz game time
                if (!d.OnPitLine) RingSample(d, rig, idLaps >= 0 ? Time.time - lapStart : -1f);
                if (idLaps >= 0 && fast != null && d.Brain != null && !d.OnPitLine)
                {
                    int i = fast.IndexAt(d.LineS);
                    float room = d.Lateral > 0f ? fast.SideL[i] : fast.SideR[i];
                    float o = Mathf.Abs(d.Lateral) - room;
                    if (o > maxOut) maxOut = o;
                    float frac = d.PlannedLapS > 1f ? (Time.time - lapStart) / d.PlannedLapS : 0f; // shots by lap time, not by line point
                    if (shotAt < 0.8f && frac >= shotAt)
                    {
                        Chase(r, rig, 8f);
                        r.Shot("ai-lap" + Mathf.RoundToInt(shotAt * 100f));
                        AcLabRunner.Log("at " + Mathf.RoundToInt(shotAt * 100f) + " % of the planned lap: " + (rig.Physics != null ? rig.Physics.SpeedKmh.ToString("0") : "-") + " km/h, target " + d.TargetKmh.ToString("0")
                                        + ", lateral " + d.Lateral.ToString("0.0") + " m, gear " + (rig.Physics != null ? rig.Physics.Telemetry.Gear : 0)
                                        + ", fuel " + (rig.Physics != null ? rig.Physics.Telemetry.FuelLitres.ToString("0.0") : "-") + " l");
                        shotAt += 0.25f;
                    }
                }
                yield return null;
            }
            AcLap lap = LastLap(-100);
            if (!r.Check(lap != null && idLaps >= 0 && CountLaps(-100) > idLaps, "a timed AI lap completed (lap events " + CountLaps(-100) + ")")) yield break;
            float planned = d.PlannedLapS * 1000f;
            float ratio = planned > 0f ? lap.TimeMs / planned : 0f;
            r.Check(ratio > 0.9f && ratio < 1.1f, "lap " + Ms(lap.TimeMs) + " within 10 % of the AI's plan " + Ms((int)planned) + " (x" + ratio.ToString("0.000") + ")");
            // the external reference: Kunos' hotlap special event for this car on this layout (gold objective), when the install has one -
            // run with -accar ks_toyota_gt86 on magione (SPECIAL_EVENT_53, gold 1:32.000; harness sim lap 2 1:32.8)
            AcLayoutEntry lay = AcSession.Layout;
            AcAiReference refLap = lay != null && r.Game.Install != null ? AcAiReference.Find(r.Game.Install.Root, lay.TrackId, lay.Id, d.CarId) : null;
            if (refLap != null)
            {
                float rr = lap.TimeMs / (float)refLap.GoldMs;
                r.Check(rr > 0.9f && rr < 1.1f, "lap " + Ms(lap.TimeMs) + " within 10 % of Kunos' hotlap gold " + Ms(refLap.GoldMs) + " (" + refLap.EventDir + ", x" + rr.ToString("0.000") + ")");
            }
            else AcLabRunner.Log("no Kunos hotlap event for " + d.CarId + " here (run -accar ks_toyota_gt86 on magione for the external reference: SPECIAL_EVENT_53 gold 1:32.000)");
            AcLabRunner.Log("other references: fast_lane.ai LapTime is 0 on every track; payload-speed lap " + PayloadLap(AcTrackWorld.AiLine) + " (the line's recording car, not AC's AI)");
            r.Check(d.Recoveries == recoveriesAtStart, "no recovery on the timed lap (" + (d.Recoveries - recoveriesAtStart) + ")");
            r.Check(maxOut < 2f, "never more than 2 m outside the line's room on the timed lap (max " + maxOut.ToString("0.00") + " m)");
            if (rig.Physics != null)
                r.Check(d.FuelLoad <= 0f || Mathf.Abs(rig.Physics.Telemetry.FuelLitres - d.FuelLoad) < Mathf.Max(3f, d.FastLanePlan != null ? d.FastLanePlan.FuelPerLap * 3f : 3f),
                        "fuel " + rig.Physics.Telemetry.FuelLitres.ToString("0.0") + " l after ~2 laps from " + d.FuelLoad.ToString("0.0") + " l (planned " + (d.FastLanePlan != null ? d.FastLanePlan.FuelPerLap.ToString("0.00") : "-") + " l/lap)");
            AcSession.LapCompleted -= OnLap;
            AcEvents.CarImpact -= OnAnyImpact;
            AcLabRunner.Log("AI car impacts logged " + _impactLogs + ", anomaly dumps " + _dumps);
        }

        /// <summary>One line of the AI car's state on the timed lap (lab only): line s, speed / target, lateral, heading error, the road angle
        /// asked vs P's, yaw rate vs asked, pedals, gear / rpm, wheel loads, slip angles, contacts and the surface under each wheel.</summary>
        static void Trace(AcAiDriver d, AcCarRig rig, float t)
        {
            AcCarPhysics ph = rig != null ? rig.Physics : null;
            if (ph == null || ph.Body == null || d.Brain == null) return;
            AcTelemetry tl = ph.Telemetry;
            AcCarInput inp = ph.Input;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
            sb.Append("TRACE t ").Append(t.ToString("0.0")).Append(" s ").Append(d.LineS.ToString("0")).Append(" v ").Append(ph.SpeedKmh.ToString("0"))
              .Append(" tgt ").Append(d.TargetKmh.ToString("0")).Append(" lat ").Append(d.Lateral.ToString("0.0")).Append(" he ").Append(d.Brain.HeadingError.ToString("0.00"))
              .Append(" wheel ").Append(inp.SteerWheelDeg.ToString("0")).Append(" road ").Append(ph.SteerRoadDeg.ToString("0.0"))
              .Append(" yaw ").Append((-ph.Body.angularVelocity.y).ToString("0.00")).Append(" thr ").Append(inp.Throttle.ToString("0.00")).Append(" brk ").Append(inp.Brake.ToString("0.00"))
              .Append(" g ").Append(tl.Gear).Append(" rpm ").Append(tl.Rpm.ToString("0")).Append(" sat ").Append(d.Brain.Saturated ? 1 : 0).Append(" ov ").Append(d.Brain.Oversteer ? 1 : 0)
              .Append(" latG ").Append(tl.LatG.ToString("0.00")).Append(" up ").Append(ph.transform.up.y.ToString("0.000"));
            for (int i = 0; i < 4; i++)
            {
                AcWheelState w = tl.Wheel(i);
                sb.Append(i == 0 ? " | " : " ; ").Append(w.Contact ? "" : "AIR ").Append(w.LoadN.ToString("0")).Append("N a").Append(w.SlipAngleDeg.ToString("0.0"))
                  .Append(" sr").Append(w.SlipRatio.ToString("0.00")).Append(" st").Append(w.SteerDeg.ToString("0.0")).Append(' ').Append(w.Surface != null ? w.Surface.Key : "-");
            }
            AcLabRunner.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ ai-race

        static IEnumerator Race(AcLabRunner r)
        {
            Laps.Clear(); _contacts = 0;
            AcSession.LapCompleted += OnLap;
            AcEvents.CarImpact += OnImpact;
            int n = Mathf.Clamp(int.Parse(AcLabBoot.Arg("-acai", "3")), 1, 18);
            AcCarEntry car = null;
            yield return Load(r, n, AcSessionType.Race, 2, c => car = c);
            if (r.Failed) yield break;
            AcRules rules = AcSession.Rules.Clone();
            rules.Session = AcSessionType.Race; rules.Laps = 2;
            int target = AcRace.OpenCourse ? 1 : 2; // an open course (trento-bondone, ks_drag) race is one run (R)
            r.Check(AcRace.RequestStart(rules), "RequestStart (" + (target == 1 ? "one run" : "2 laps") + ", " + n + " AI)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown, 20f, "the grid");
            if (r.Failed) yield break;
            yield return r.Frames(30);
            int frozen = 0;
            for (int i = 0; i < AcAi.Drivers.Count; i++) { var ph = AcAi.Drivers[i].Rig != null ? AcAi.Drivers[i].Rig.Physics : null; if (ph != null && ph.Frozen) frozen++; }
            r.Check(frozen == n, "AI cars frozen on the grid " + frozen + "/" + n);
            if (AcRace.LocalCar != null) { Chase(r, AcRace.LocalCar, 14f); r.Shot("ai-grid"); }
            yield return r.Wait(() => AcSession.Phase == AcPhase.Racing, 40f, "the green");
            if (r.Failed) yield break;
            yield return r.Frames(90);
            if (AcAi.Drivers[0].Rig != null) { Chase(r, AcAi.Drivers[0].Rig, 9f); r.Shot("ai-start"); }
            bool trafficShot = false, passShot = false, startSampled = false;
            float greenAt = Time.time - 0.05f; // ~90 frames after the green at batch pace
            float[] passMin = new float[n], passMinAt = new float[n];
            for (int i = 0; i < n; i++) passMin[i] = float.MaxValue;
            float closest = float.MaxValue; string closestWho = "-";
            int[] order = new int[n], prev = new int[n];
            int overtakes = 0;
            float timeout = Time.realtimeSinceStartup + 600f; // ours
            while (Time.realtimeSinceStartup < timeout && (AcSession.Phase == AcPhase.Racing || AcSession.Phase == AcPhase.Finished))
            {
                bool allDone = true;
                for (int i = 0; i < n; i++) if (CountLaps(-100 - AcAi.Drivers[i].Index) < target) allDone = false;
                if (allDone) break;
                // order by line position (wrap-aware via R's race distance would be better; this only logs changes)
                Order(order);
                bool changed = false;
                for (int i = 0; i < n; i++) if (order[i] != prev[i]) changed = true;
                if (changed && Time.frameCount > 10) { overtakes++; Array.Copy(order, prev, n); }
                // last-gaps pass: how close each AI came to my standing car (centre to centre, flat) + a shot of the first close pass
                AcCarRig mineRig = AcRace.LocalCar;
                if (!startSampled && Time.time > greenAt + 6f) { startSampled = true; for (int i = 0; i < n; i++) AcLabRunner.Log("6 s after the green: " + AiState(AcAi.Drivers[i], mineRig)); }
                if (mineRig != null && mineRig.Root != null)
                    for (int i = 0; i < n; i++)
                    {
                        AcCarRig a = AcAi.Drivers[i].Rig;
                        if (a == null || a.Root == null) continue;
                        Vector3 dv = a.Root.transform.position - mineRig.Root.transform.position; dv.y = 0f;
                        float dm = dv.magnitude;
                        if (dm < closest) { closest = dm; closestWho = AcAi.Drivers[i].Name + " at " + AcAi.Drivers[i].TargetKmh.ToString("0") + " km/h target, offset " + AcAi.Drivers[i].Brain.Offset.ToString("0.0") + " m"; }
                        if (!passShot && dm < 6f) { passShot = true; Chase(r, mineRig, 12f); r.Shot("ai-pass"); }
                        // a flying pass (after the start): the slowest speed in the 150 m before my car - what passing a standing car costs
                        Vector3 mf = mineRig.Root.transform.forward; mf.y = 0f; mf.Normalize();
                        float along = -Vector3.Dot(dv, mf), side = Vector3.Cross(mf, dv).magnitude;
                        if (Time.time > greenAt + 20f && along > 0f && along < 150f && side < 8f && a.Physics != null && a.Physics.Body != null)
                        {
                            float k = a.Physics.Body.linearVelocity.magnitude * 3.6f;
                            if (k < passMin[i]) { passMin[i] = k; passMinAt[i] = along; }
                        }
                    }
                if (!trafficShot)
                    for (int i = 0; i < n && !trafficShot; i++)
                        for (int j = i + 1; j < n && !trafficShot; j++)
                        {
                            AcCarRig a = AcAi.Drivers[i].Rig, b = AcAi.Drivers[j].Rig;
                            if (a == null || b == null) continue;
                            if ((a.Root.transform.position - b.Root.transform.position).magnitude < 12f && Time.realtimeSinceStartup > 0f)
                            {
                                Chase(r, a, 14f); r.Shot("ai-traffic"); trafficShot = true;
                                AcLabRunner.Log("traffic: " + AcAi.Drivers[i].Name + " offset " + AcAi.Drivers[i].Brain.Offset.ToString("0.0") + " m, " + AcAi.Drivers[j].Name + " offset " + AcAi.Drivers[j].Brain.Offset.ToString("0.0") + " m");
                            }
                        }
                yield return null;
            }
            if (AcAi.Drivers[0].Rig != null) { Chase(r, AcAi.Drivers[0].Rig, 9f); r.Shot("ai-finish"); }
            int finished = 0, recov = 0;
            for (int i = 0; i < n; i++)
            {
                AcAiDriver d = AcAi.Drivers[i];
                int laps = CountLaps(-100 - d.Index);
                if (laps >= target) finished++;
                recov += d.Recoveries;
                AcLap l = LastLap(-100 - d.Index);
                AcLabRunner.Log("AI " + d.Name + ": laps " + laps + ", last " + (l != null ? Ms(l.TimeMs) : "-") + ", recoveries " + d.Recoveries + ", state " + d.State);
                if (laps < target) AcLabRunner.Log("  unfinished: " + AiState(d, AcRace.LocalCar));
                if (passMin[i] < float.MaxValue) AcLabRunner.Log("  flying pass of my car: slowest " + passMin[i].ToString("0") + " km/h at " + passMinAt[i].ToString("0") + " m before it");
            }
            r.Check(finished == n, "all AI cars completed " + target + " lap(s) / run (" + finished + "/" + n + ")");
            if (target == 1) { int stopped = 0; for (int i = 0; i < n; i++) if (AcAi.Drivers[i].State == AcAiState.Finished) stopped++; r.Check(stopped == n, "open course: every AI car reached the run-out past the finish (" + stopped + "/" + n + ")"); }
            r.Check(_contacts == 0, "no AI contact with my (standing) car (" + _contacts + ")");
            AcLabRunner.Log("order changes between AI cars (overtakes or the start): " + overtakes + "; recoveries total " + recov);
            AcLabRunner.Log("closest AI approach to my standing car: " + closest.ToString("0.00") + " m centre to centre (" + closestWho + ")");
            AcSession.LapCompleted -= OnLap;
            AcEvents.CarImpact -= OnImpact;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>One line of an AI's traffic state (last-gaps pass: the stuck / slow-start cases).</summary>
        static string AiState(AcAiDriver d, AcCarRig mine)
        {
            if (d == null || d.Rig == null || d.Rig.Root == null || d.Brain == null) return "no car";
            var b = d.Brain;
            var ph = d.Rig.Physics;
            float kmh = ph != null && ph.Body != null ? ph.Body.linearVelocity.magnitude * 3.6f : 0f;
            string dist = "-";
            if (mine != null && mine.Root != null) { Vector3 dv = d.Rig.Root.transform.position - mine.Root.transform.position; dv.y = 0f; dist = dv.magnitude.ToString("0.0"); }
            return d.Name + " " + d.State + " " + kmh.ToString("0") + " km/h, s " + b.S.ToString("0") + " lat " + b.Lateral.ToString("0.0") + " offset " + b.Offset.ToString("0.0")
                 + "/" + b.OffsetTarget.ToString("0.0") + ", target " + d.TargetKmh.ToString("0") + " km/h, follow cap " + (float.IsInfinity(b.FollowCap) ? "-" : (b.FollowCap * 3.6f).ToString("0"))
                 + ", decel " + b.FollowDecel.ToString("0.0") + ", blocked " + d.Blocked + ", reverse " + b.Reverse + ", pass side " + d.PassSide + ", " + dist + " m from my car";
        }

        static void Order(int[] order)
        {
            int n = order.Length;
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = 1; i < n; i++)
            {
                int k = order[i], j = i - 1;
                float sk = Dist(k);
                while (j >= 0 && Dist(order[j]) < sk) { order[j + 1] = order[j]; j--; }
                order[j + 1] = k;
            }
        }

        static float Dist(int i)
        {
            AcAiDriver d = AcAi.Drivers[i];
            AcAiTrack f = AcAi.FastTrack;
            return CountLaps(-100 - d.Index) * (f != null ? f.Length : 1f) + d.LineS;
        }

        static int CountLaps(int id) { int n = 0; for (int i = 0; i < Laps.Count; i++) if (Laps[i].PlayerId == id) n++; return n; }

        static AcLap LastLap(int id) { for (int i = Laps.Count - 1; i >= 0; i--) if (Laps[i].PlayerId == id) return Laps[i]; return null; }

        static void Chase(AcLabRunner r, AcCarRig rig, float back)
        {
            if (rig == null || rig.Root == null) return;
            Transform t = rig.Root.transform;
            Vector3 f = t.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            r.Aim(t.position - f * back + Vector3.up * (back * 0.35f + 1f), t.position + f * 6f, 60f);
        }

        static string PayloadLap(AcAiSpline s)
        {
            if (s == null || s.IsEmpty) return "-";
            double t = 0; for (int i = 0; i + 1 < s.Count; i++) { float v = s.P(i, AcAiSpline.Speed); if (v > 0.5f) t += (s.Length[i + 1] - s.Length[i]) / v; }
            return Ms((int)(t * 1000));
        }

        // ------------------------------------------------------------------ ai-retire (iteration 2, DAMAGE_DESIGN 8 / 10)

        /// <summary><c>ai-retire</c>: practice with ONE AI car at DAMAGE (the lobby default from server_cfg.ini, 100): once it runs on the fast
        /// lane above 60 km/h its engine is blown through P's own engine-life path (StepEngine with an over-rev); it must enter state Retired,
        /// steer to the side of the line (|lateral| grows), stop and be frozen there within 25 s, never teleported (no recovery); after
        /// Repair(All) it drives again. Shots ai-retire-blown, ai-retire-parked.</summary>
        static IEnumerator Retire(AcLabRunner r)
        {
            AcCarEntry car = null;
            yield return Load(r, 1, AcSessionType.Practice, 5, c => car = c);
            if (r.Failed) yield break;
            r.Check(AcSession.Rules != null && AcSession.Rules.DamageMultiplier > 0, "damage on in the session (DAMAGE_MULTIPLIER " + (AcSession.Rules != null ? AcSession.Rules.DamageMultiplier : -1) + ")");
            AcAiDriver d = AcAi.Drivers[0];
            AcCarRig rig = d.Rig;
            AcCarPhysics ph = rig.Physics;
            r.Check(ph.DamageRate > 0f, "the AI car's DamageRate " + ph.DamageRate);
            yield return r.Wait(() => !d.OnPitLine && ph.SpeedKmh > 60f, 240f, "the AI car on the fast lane above 60 km/h");
            if (r.Failed) yield break;
            AcCarDamage dmg = ph.Damage;
            float lat0 = d.Lateral;
            int rec0 = d.Recoveries;
            if (dmg.Spec != null && dmg.Spec.HasRpmDamage) dmg.StepEngine(1f, dmg.Spec.RpmThreshold + 1e6f, 0f);   // P's own path: life -> 0, EngineDead, change flags
            if (!dmg.EngineDead) { dmg.EngineLife = 0f; dmg.EngineDead = true; AcLabRunner.Log("ai-retire: the car has no RPM damage keys - EngineDead set directly"); }
            r.Check(dmg.EngineDead, "engine blown at " + ph.SpeedKmh.ToString("0") + " km/h, lateral " + lat0.ToString("0.0") + " m");
            yield return r.Frames(10);
            Chase(r, rig, 8f);
            r.Shot("ai-retire-blown");
            yield return r.Wait(() => d.State == AcAiState.Retired, 5f, "state Retired");
            float maxLat = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < 25f && !ph.Frozen) { maxLat = Mathf.Max(maxLat, Mathf.Abs(d.Lateral)); yield return null; }
            AcLabRunner.Log("ai-retire: parked after " + (Time.time - t0).ToString("0.0") + " s, lateral " + d.Lateral.ToString("0.0") + " m (max " + maxLat.ToString("0.0") + ", start " + lat0.ToString("0.0") + "), offset target "
                            + (d.Brain != null ? d.Brain.OffsetTarget.ToString("0.0") : "-") + ", speed " + ph.SpeedKmh.ToString("0.0") + " km/h");
            r.Check(ph.Frozen && d.State == AcAiState.Retired, "stopped and held (frozen) within 25 s");
            bool sameSide = Mathf.Sign(d.Lateral) == Mathf.Sign(lat0) || Mathf.Abs(lat0) < 0.5f;
            r.Check(sameSide && Mathf.Abs(d.Lateral) >= Mathf.Max(Mathf.Abs(lat0), d.RetireRoom) - 1f, "parked at the side it was on (lateral " + lat0.ToString("0.0") + " -> " + d.Lateral.ToString("0.0") + " m, room " + d.RetireRoom.ToString("0.0") + " m, target " + d.RetireTarget.ToString("0.0") + ", want |lat| >= " + (Mathf.Max(Mathf.Abs(lat0), d.RetireRoom) - 1f).ToString("0.0") + ")");
            r.Check(d.Recoveries == rec0, "no recovery teleport while retired");
            Chase(r, rig, 9f);
            r.Shot("ai-retire-parked");
            ph.Repair(AcRepair.All);
            yield return r.Wait(() => d.State != AcAiState.Retired && !ph.Frozen, 5f, "driving again after Repair(All)");
            yield return r.Wait(() => ph.SpeedKmh > 20f, 30f, "the repaired AI car moves");
        }

        // ------------------------------------------------------------------ ai-retire-race (flow pass 2026-11-02)

        /// <summary>
        /// <c>ai-retire-race</c>: a 1-lap race at DAMAGE 100 (the lobby default) with 2 AI cars and my car standing on pole. After the green AI 0's
        /// engine blows (P's own StepEngine path): it parks + freezes (I's Retired), R's standings mark it Retired / not finished (DNF); my
        /// engine blows too (retired, ENGINE BLOWN - RETIRED); AI 1 finishes, the director closes the race: results AI 1 finished, AI 0 and me
        /// DNF (results picture from behind AI 0). Every AI car's damage zones are logged at the end (contacts / hull scrapes at DAMAGE 100).
        /// Back in practice both AI cars are repaired and driving.
        /// </summary>
        static IEnumerator RetireRace(AcLabRunner r)
        {
            Laps.Clear(); _contacts = 0;
            AcSession.LapCompleted += OnLap;
            AcEvents.CarImpact += OnImpact;
            AcCarEntry car = null;
            yield return Load(r, 2, AcSessionType.Race, 1, c => car = c);
            if (r.Failed) yield break;
            AcRules rules = AcSession.Rules.Clone();
            rules.Session = AcSessionType.Race; rules.Laps = 1;
            r.Check(rules.DamageMultiplier > 0, "DAMAGE " + rules.DamageMultiplier + " (lobby default)");
            r.Check(AcRace.RequestStart(rules), "RequestStart (1 lap, 2 AI)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Racing, 60f, "the green");
            if (r.Failed) yield break;
            AcAiDriver a0 = AcAi.Drivers[0], a1 = AcAi.Drivers[1];
            AcCarPhysics p0 = a0.Rig.Physics;
            yield return r.Wait(() => !a0.OnPitLine && p0.SpeedKmh > 60f, 60f, "AI 0 above 60 km/h");
            if (r.Failed) yield break;
            AcCarDamage d0 = p0.Damage;
            if (d0.Spec != null && d0.Spec.HasRpmDamage) d0.StepEngine(1f, d0.Spec.RpmThreshold + 1e6f, 0f);
            if (!d0.EngineDead) { d0.EngineLife = 0f; d0.EngineDead = true; }
            r.Check(d0.EngineDead, "AI 0 (" + a0.Name + ") engine blown at " + p0.SpeedKmh.ToString("0") + " km/h");
            float t0 = Time.time;
            while (Time.time - t0 < 25f && !(p0.Frozen && a0.State == AcAiState.Retired)) yield return null;
            r.Check(p0.Frozen && a0.State == AcAiState.Retired, "AI 0 parked and held (state " + a0.State + ", " + (Time.time - t0).ToString("0.0") + " s)");
            yield return r.Frames(10);
            AcStanding s0 = Row(a0.Name), me = LocalRow();
            r.Check(s0 != null && s0.Retired && !s0.Finished, "standings: AI 0 Retired, not finished (DNF)");
            r.Check(d0.Retired, "AI 0 retired bit set (wire d1 bit 31 for the other clients)");
            Chase(r, a0.Rig, 9f);
            r.Shot("ai-retire-race-parked");

            AcCarRig mine = AcRace.LocalCar;
            if (mine != null && mine.Physics != null)
            {
                mine.Physics.Damage.EngineLife = 0f; mine.Physics.Damage.EngineDead = true;
                yield return r.Wait(() => AcRace.LocalRetired, 5f, "my car retired");
                me = LocalRow();
                r.Check(me != null && me.Retired && !me.Finished, "standings: my row Retired (DNF)");
            }
            yield return r.Wait(() => AcSession.Phase == AcPhase.Results, 420f, "AI 1 finishes and the director closes the race");
            if (!r.Failed)
            {
                yield return r.Frames(10);
                s0 = Row(a0.Name);
                AcStanding s1 = Row(a1.Name);
                me = LocalRow();
                r.Check(s1 != null && s1.Finished && s1.Position == 1, "results: AI 1 finished first (" + (s1 != null ? s1.Position + ", " + Ms(s1.TotalMs) : "-") + ")");
                r.Check(s0 != null && s0.Retired && !s0.Finished && me != null && me.Retired && !me.Finished, "results: AI 0 and my car DNF (retired)");
                foreach (AcStanding s in AcRace.Standings)
                    AcLabRunner.Log("result " + s.Position + " " + s.Name + (s.IsLocal ? " (me)" : s.IsAi ? " (AI)" : "") + " laps " + s.Laps + (s.Finished ? " " + Ms(s.TotalMs) : s.Retired ? " DNF (retired)" : " DNF"));
                for (int i = 0; i < AcAi.Drivers.Count; i++)
                {
                    AcCarDamage d = AcAi.Drivers[i].Rig != null ? AcAi.Drivers[i].Rig.Physics.Damage : null;
                    if (d != null) AcLabRunner.Log("AI " + i + " damage at the end: zones F/R/L/R/C " + d.ZoneByte(0) + "/" + d.ZoneByte(1) + "/" + d.ZoneByte(2) + "/" + d.ZoneByte(3) + "/" + d.ZoneByte(4)
                                                   + " km/h, susp " + d.SuspPct.ToString("0") + " %, engine " + d.EnginePct.ToString("0") + " %");
                }
                r.Check(_contacts == 0, "no AI contact with my standing car (" + _contacts + ")");
            }
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice, AcRace.Defaults.ResultScreenSeconds + 20f, "back to practice");
            if (!r.Failed)
            {
                yield return r.Wait(() => d0.Pristine && a0.State != AcAiState.Retired && !p0.Frozen, 10f, "AI 0 repaired and released");
                r.Check(d0.Pristine && !d0.Retired && a0.State != AcAiState.Retired, "practice: AI 0 repaired, not retired (state " + a0.State + ")");
            }
            AcSession.LapCompleted -= OnLap;
            AcEvents.CarImpact -= OnImpact;
        }

        static AcStanding Row(string name)
        {
            foreach (AcStanding s in AcRace.Standings) if (s.IsAi && s.Name == name) return s;
            return null;
        }

        static AcStanding LocalRow()
        {
            foreach (AcStanding s in AcRace.Standings) if (s.IsLocal) return s;
            return null;
        }

        static string Ms(int ms) { return ms <= 0 ? "-" : (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00") + "." + (ms % 1000).ToString("000"); }
    }
}
