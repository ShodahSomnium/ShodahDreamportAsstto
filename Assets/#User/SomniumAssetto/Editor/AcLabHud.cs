using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [H, HUD] Lab scenarios (DevTools/lab.sh &lt;name&gt;):
    /// <list type="bullet">
    /// <item><c>hud-desk</c>: the monitor overlay with fake feeds. A message pushed before any view exists must show once the overlay is
    /// built (queue). Race: rpm 2000 / 6500 / limiter, standings of 6 + 1 spectator (position row 2/6, tower: racers 1..6 then the spectator
    /// greyed with "-"), 3 red start lights, "FALSE START!" with AC's drag bar. Checks the DRAWN fills: Essentials rpm bar width = rpm /
    /// limiter, both fuel bars = fuel / tank (rect widths), and the rpm_on ring fill direction against the measured arc (pixels at 9 o'clock
    /// light up at half rpm, 3 o'clock does not). Then Essentials + mph + pit limiter + wrong way + blue flag; qualify in the Practice phase
    /// (QUALIFY + clock), hotlap, untimed practice (infinity icon, OUT LAP), a sector split with its delta, the leader finished (chequered
    /// flag for a car still running), my finish (FINISH + race time), out-of-fuel icon. Pictures hud-desk-*.png (the overlay is hung in world
    /// space exactly filling the lab camera, because a camera's Render() skips ScreenSpaceOverlay canvases).</item>
    /// <item><c>hud-vr</c> [-accar id]: the VR panel on a real car (AcCarRig.Spawn, showroom kind) with the same feed: the eye must come from
    /// S's seat geometry (vr.ini DRIVEREYES when the game has one), the kn5 dash line must be measured, and the panel's DRAWN top edge
    /// (RectTransform corners) must be >= 0.9 deg under the dash line at every point and >= 5.9 deg under the horizon; the smallest text
    /// must be >= 1.0 deg tall at the panel's distance; passenger eye mirrored; the "release both triggers" hint; hud.show off keeps the
    /// messages. Pictures from the eye (fov 90) and from behind.</item>
    /// <item><c>hud-live</c> [-actrack magione] [-accar abarth500]: the live path - R loads a practice (AcRace.RequestLoad), my car in its
    /// pit box, S seats me (AcSeat.Enter with lab stand-ins) -> AcEvents.SeatChanged -> AcHud.Show: the feed must be the live one (car,
    /// driver, PRACTICE, untimed, out lap); then a 10 min qualify (AcRace.RequestStart, Rules.Session Qualify) must show QUALIFY with a
    /// clock while the phase stays Practice; then the VR panel (AcHud.ForceVr + AcSeat.LabVr) from S's eye. Pictures hud-live-*.png.</item>
    /// </list>
    /// Compare the pictures with AC's own HUD (dial bottom centre, times top left, tower left, lights top centre).
    /// </summary>
    [InitializeOnLoad]
    static class AcLabHud
    {
        static AcLabHud()
        {
            AcLabRunner.Register("hud-desk", r => Desk(r));
            AcLabRunner.Register("hud-vr", r => VrPanel(r));
            AcLabRunner.Register("hud-live", r => Live(r));
            AcLabRunner.Register("hud-damage", r => DamageHud(r));
        }

        static List<AcStanding> FakeStandings(bool spectator)
        {
            var l = new List<AcStanding>();
            string[] names = { "Shodah", "Wildy", "Meloz", "denevraut", "AI Rossi", "AI Lauda" };
            int[] gaps = { 0, 1834, 4210, 9055, 15020, 0 };
            for (int i = 0; i < names.Length; i++)
            {
                l.Add(new AcStanding
                {
                    Position = i + 1, PlayerId = i == 1 ? 7 : 100 + i, IsLocal = i == 1, IsAi = i >= 4, Name = names[i], Laps = i == 5 ? 2 : 3,
                    BestMs = 98000 + i * 731, LastMs = 99000 + i * 500, GapMs = gaps[i], LapsBehind = i == 5 ? 1 : 0, RaceDistance = 3.4f - i * 0.05f,
                    InPit = i == 4,
                });
            }
            // a late joiner (R: Spectating, Position 0) listed among them, as R's board may
            if (spectator) l.Insert(3, new AcStanding { Position = 0, PlayerId = 200, Name = "LateJoiner", Spectating = true });
            return l;
        }

        static AcHudFeed Feed(float rpm)
        {
            return new AcHudFeed
            {
                Car = true, Driver = true, Rpm = rpm, LimiterRpm = 7500f, SpeedKmh = 187.4f, Fuel = 31f, MaxFuel = 60f, Boost = 0.6f, MaxBoost = 1.0f,
                Gear = 3, Abs = true, Tc = false, Phase = AcPhase.Racing, Session = AcSessionType.Race, ShownSession = AcSessionType.Race, TotalLaps = 5,
                SecondsLeft = -1, Laps = 3, LapMs = 61234, LastMs = 98765, BestMs = 97531, DeltaMs = -213, Sector = 1, LapValid = true, Position = 2,
                Cars = 6, PitLimitKmh = 80f, SplitDeltaMs = int.MinValue,
            };
        }

        static IEnumerator Ready(AcLabRunner r)
        {
            bool before = AcHud.Ready;
            AcHud.Start(r.Game);                                   // idempotent (R normally does it)
            _queueTest = !before;
            AcHud.Message("QUEUED BEFORE THE VIEW", 15f);          // no view yet: must wait in AcHud's queue
            yield return r.Wait(() => AcHud.Ready, 60f, "the HUD art (AcHudArt.Ready)");
            AcLabRunner.Log("hud: art " + AcHudArt.Loaded + " loaded, " + AcHudArt.Missing + " missing; fonts: " + AcHudFonts.Describe()
                + "; bitmap " + (AcHudArt.Bitmap != null ? AcHudArt.Bitmap.ToString() : "none") + "; language " + AcHudArt.Config.Language
                + ", USE_MPH " + AcHudArt.Config.UseMph + ", TIME_DIFFERENCE POS_Y " + AcHudArt.Config.TimeDifferencePosY
                + ", OVERLAY_LEADERBOARD " + AcHudArt.Config.OverlayLeaderboard + ", session box P/Q/R " + AcHudArt.Config.PracticeVisible + "/" + AcHudArt.Config.QualifyVisible + "/" + AcHudArt.Config.RaceVisible);
            r.Check(AcHudArt.Missing == 0 && AcHudArt.Loaded == AcHudArtList.All.Length, "every HUD picture loaded from the install (" + AcHudArt.Loaded + "/" + AcHudArtList.All.Length + ", incl. " + AcHudArtList.DamageFiles.Length + " damage layers)");
            r.Check(AcHudFonts.Regular != null, "a TrueType HUD font exists (" + AcHudFonts.Describe() + ")");
            r.Check(AcHudArt.Bitmap != null && AcHudArt.BitmapTexture != null, "the bitmap fallback font is uploaded");
            if (before) AcLabRunner.Log("hud: the art was loaded before this scenario - the message queue is not exercised");
        }

        static bool _queueTest;

        static bool Near(float a, float b, float tol) { return Mathf.Abs(a - b) <= tol; }

        /// <summary>Renders the lab camera into a w x h target and returns the pixels (rows bottom-up, as ReadPixels gives them).</summary>
        static Color32[] Render(AcLabRunner r, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            r.Cam.targetTexture = rt;
            r.Cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            r.Cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            return px;
        }

        /// <summary>Mean luminance of a 5x5 block around the screen point of world <paramref name="p"/>.</summary>
        static float Luma(AcLabRunner r, Color32[] px, int w, int h, Vector3 p)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24);
            r.Cam.targetTexture = rt;
            Vector3 s = r.Cam.WorldToScreenPoint(p);
            r.Cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            int cx = Mathf.RoundToInt(s.x), cy = Mathf.RoundToInt(s.y);
            float sum = 0f; int n = 0;
            for (int y = cy - 2; y <= cy + 2; y++) for (int x = cx - 2; x <= cx + 2; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                Color32 c = px[y * w + x];
                sum += (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f; n++;
            }
            return n > 0 ? sum / n : -1f;
        }

        /// <summary>A world point on the rpm ring at <paramref name="deg"/> clockwise from straight down (on screen), at the ring's mid radius
        /// (rpm_on ink radius 58-78 of 78 px: 0.87 of the half size, harness `arc`).</summary>
        static Vector3 RingPoint(RectTransform ring, float deg)
        {
            Rect rc = ring.rect;
            float a = deg * Mathf.Deg2Rad, rad = rc.width * 0.5f * 0.87f;
            Vector2 local = rc.center + new Vector2(-Mathf.Sin(a), -Mathf.Cos(a)) * rad;
            return ring.TransformPoint(local);
        }

        static IEnumerator Desk(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            string gauge = AcSettings.Text("hud.gauge", null);
            AcSettings.SetInt("hud.gauge", 0);
            var st = FakeStandings(true);
            var f0 = Feed(2000f);
            AcHud.ApplyStandings(ref f0, st, f0.Phase);              // the live standings rule on the fake list
            r.Check(f0.Cars == 6, "spectators are not counted: " + f0.Cars + " racers of " + st.Count + " rows (want 6)");
            AcHud.SetFake(f0, st);
            AcHud.Show(null);
            AcHud.StartLights(3, false, true);
            AcHud.Message(AcHudView.FalseStartText, 30f);
            yield return r.Wait(() => AcHud.Desk != null, 10f, "the monitor overlay");
            if (r.Failed) yield break;
            r.Aim(AcSpace.RoomOrigin + new Vector3(0f, 1.7f, -6f), AcSpace.RoomOrigin + new Vector3(0f, 1.2f, 4f), 60f);
            AcHud.Desk.LabFrame(r.Cam, 16f / 9f);
            yield return r.Frames(5);
            var v = AcHud.Desk.View;
            if (_queueTest) r.Check(v.HasMessage("QUEUED BEFORE THE VIEW"), "a message sent before the overlay existed is shown once it is built (AcHud queue)");
            r.Check(v.GDial.gameObject.activeSelf, "dial shown");
            r.Check(v.GTimes.gameObject.activeSelf, "lap times shown");
            r.Check(v.GTower.gameObject.activeSelf, "standings shown");
            r.Check(v.GLights.gameObject.activeSelf && v.LabFalseStartBar, "start lights + AC's false-start bar shown");
            r.Check(v.GDelta.gameObject.activeSelf, "time difference shown");
            r.Check(v.GMsg.gameObject.activeSelf, "message shown");
            r.Check(v.LabSession == AcHudArt.Config.S("RACE", "RACE") && v.LabSessionRight == "LAP 4/5", "race box: " + v.LabSession + " " + v.LabSessionRight);
            r.Check(v.LabPos == "2/6", "position counts racers only: " + v.LabPos);
            string rows = "";
            for (int i = 0; i < v.LabTowerRows; i++) rows += "[" + v.LabTowerRow(i) + "]";
            AcLabRunner.Log("hud-desk tower: " + rows);
            r.Check(v.LabTowerRow(3).StartsWith("4|denevraut") && v.LabTowerRow(6).StartsWith("-|LateJoiner"), "tower: racers 1..6 in order, the spectator after them with no position");
            r.Check(Near(v.LabDialFuelFrac, 31f / 60f, 0.01f), "dial fuel bar drawn at fuel / tank (" + v.LabDialFuelFrac.ToString("0.000") + ", want 0.517)");
            r.Shot("hud-desk-2000rpm", 1920, 1080);

            // ring direction against the measured arc: rpm 0 vs half rpm, pixels at 9 o'clock (90 deg clockwise from down) and 3 o'clock (270)
            var fz = Feed(0f); fz.Abs = false; AcHud.SetFake(fz, st);
            yield return r.Frames(3);
            const int W = 960, H = 540;
            RectTransform ring = v.LabRpmRing;
            Color32[] a = Render(r, W, H);
            var fh = Feed(3750f); fh.Abs = false; AcHud.SetFake(fh, st);
            yield return r.Frames(3);
            Color32[] b = Render(r, W, H);
            float l90a = Luma(r, a, W, H, RingPoint(ring, 90f)), l90b = Luma(r, b, W, H, RingPoint(ring, 90f));
            float l270a = Luma(r, a, W, H, RingPoint(ring, 270f)), l270b = Luma(r, b, W, H, RingPoint(ring, 270f));
            AcLabRunner.Log("hud-desk ring: fill " + v.LabRpmFill.ToString("0.000") + " (want " + AcHudView.RingFill(0.5f).ToString("0.000") + "); luma 9 o'clock " + l90a.ToString("0.00") + " -> " + l90b.ToString("0.00")
                + ", 3 o'clock " + l270a.ToString("0.00") + " -> " + l270b.ToString("0.00"));
            r.Check(Near(v.LabRpmFill, AcHudView.RingFill(0.5f), 0.002f), "rpm ring fill = arc 30..328 mapped (" + v.LabRpmFill.ToString("0.000") + ")");
            r.Check(Mathf.Abs(l90b - l90a) > 0.06f && Mathf.Abs(l270b - l270a) < 0.03f, "the ring fills clockwise from the bottom-left like the art's arc (9 o'clock lit at half rpm, 3 o'clock not)");

            AcHud.SetFake(Feed(6500f), st);
            yield return r.Frames(3);
            r.Shot("hud-desk-6500rpm", 1920, 1080);
            var f = Feed(7500f); f.Limiter = true; AcHud.SetFake(f, st);
            yield return r.Frames(3);
            r.Shot("hud-desk-limiter", 1920, 1080);

            // Essentials gauge, mph, pit limiter + wrong way + blue flag, cut lap, green lights
            AcSettings.SetInt("hud.gauge", 1);
            f = Feed(5200f); f.Mph = true; f.InPitlane = true; f.SpeedKmh = 92f; f.WrongWay = true; f.BlueFlag = true; f.LapValid = false; f.DeltaMs = 412;
            AcHud.SetFake(f, st);
            AcHud.StartLights(5, true, true);
            yield return r.Frames(6);
            r.Check(v.GEss.gameObject.activeSelf && !v.GDial.gameObject.activeSelf, "Essentials gauge replaces the dial");
            r.Check(Near(v.LabEssRpmFrac, 5200f / 7500f, 0.01f), "Essentials rpm bar drawn at rpm / limiter (" + v.LabEssRpmFrac.ToString("0.000") + ", want 0.693)");
            r.Check(Near(v.LabEssFuelFrac, 31f / 60f, 0.01f), "Essentials fuel bar drawn at fuel / tank (" + v.LabEssFuelFrac.ToString("0.000") + ")");
            r.Check(v.GPit.gameObject.activeSelf && v.GWrong.gameObject.activeSelf && v.GFlag.gameObject.activeSelf, "pit limiter, wrong way, flag shown");
            r.Shot("hud-desk-essentials-mph", 1920, 1080);
            AcSettings.SetInt("hud.gauge", 0);
            AcHud.StartLights(0, false, false);

            // qualify runs in the PRACTICE phase (R): the box must say QUALIFY with the clock
            f = Feed(4000f); f.Phase = AcPhase.Practice; f.Session = AcSessionType.Qualify; f.ShownSession = AcSessionType.Qualify; f.TotalLaps = 0; f.SecondsLeft = 512;
            AcHud.SetFake(f, FakeStandings(false));
            yield return r.Frames(3);
            r.Check(v.LabSession == AcHudArt.Config.S("QUALIFY", "QUALIFY") && v.LabSessionRight == "08:32" && !v.LabUntimedIcon, "qualify in the Practice phase: " + v.LabSession + " " + v.LabSessionRight);
            r.Shot("hud-desk-qualify", 1920, 1080);
            f.ShownSession = AcSessionType.Hotlap; f.Session = AcSessionType.Hotlap; f.SecondsLeft = -1;
            AcHud.SetFake(f, FakeStandings(false));
            yield return r.Frames(3);
            r.Check(v.LabSession == AcHudArt.Config.S("HOTLAP", "HOTLAP") && v.LabUntimedIcon, "hotlap: " + v.LabSession + ", untimed icon " + v.LabUntimedIcon);

            // practice alone: untimed (infinity icon), out lap, no tower, no lights
            f = Feed(4000f); f.Phase = AcPhase.Practice; f.Session = AcSessionType.Practice; f.ShownSession = AcSessionType.Practice; f.TotalLaps = 0;
            f.SecondsLeft = -1; f.Position = 0; f.Cars = 1; f.OutLap = true; f.BestMs = 0; f.LastMs = 0;
            AcHud.SetFake(f, new List<AcStanding>());
            yield return r.Frames(4);
            // the false-start bar may still show with the "FALSE START!" message sent earlier (it lasts as long as the message) - the lamps must be off
            r.Check(!v.GTower.gameObject.activeSelf && !v.LabLampsShown, "practice alone: no tower, no start lamps (false-start bar " + v.LabFalseStartBar + " with its message " + v.HasMessage(AcHudView.FalseStartText) + ")");
            r.Check(v.LabUntimedIcon && v.LabLapLabel == "OUT LAP", "untimed practice: infinity icon " + v.LabUntimedIcon + ", lap label '" + v.LabLapLabel + "'");
            r.Shot("hud-desk-practice", 1920, 1080);

            // sector split (S1 31.234, 0.213 faster than the best lap's S1)
            f.OutLap = false; f.BestMs = 97531; f.SplitSector = 1; f.SplitMs = 31234; f.SplitDeltaMs = -213;
            AcHud.SetFake(f, new List<AcStanding>());
            yield return r.Frames(3);
            r.Check(v.LabSplit == "S1  0:31.234 -0.213", "sector split shown: '" + v.LabSplit + "'");
            r.Shot("hud-desk-split", 1920, 1080);

            // the leader finished, I still race: chequered flag; then my finish: FINISH + race time; then a dry tank
            st = FakeStandings(false); st[0].Finished = true;
            f = Feed(5000f); AcHud.ApplyStandings(ref f, st, AcPhase.Racing);
            r.Check(f.LeaderFinished, "leader finished from the standings");
            AcHud.SetFake(f, st);
            yield return r.Frames(3);
            r.Check(v.LabFlag != null && v.LabFlag == AcHudArt.Get(AcHudArtList.FlagFinish), "chequered flag once the leader finished (my lap still running)");
            f.Finished = true; f.FinishedMs = 501234; f.Fuel = 0f;
            AcHud.SetFake(f, st);
            yield return r.Frames(3);
            r.Check(v.LabLapLabel == AcHudArt.Config.S("FINISH", "FINISH") && v.LabLapTime == "8:21.234", "my finish: '" + v.LabLapLabel + "' " + v.LabLapTime);
            r.Check(v.LabFuelIcon == AcHudArt.Get(AcHudArtList.FuelOut), "out-of-fuel icon at an empty tank");
            r.Shot("hud-desk-finished", 1920, 1080);

            AcHud.ClearFake();
            AcHud.Hide();
            AcSettings.SetText("hud.gauge", gauge ?? "0");
        }

        static IEnumerator VrPanel(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            var content = r.Game.Content;
            string want = AcLabBoot.Arg("-accar", "ks_mazda_miata");
            AcCarEntry car = content != null ? content.Car(want) : null;
            if ((car == null || !car.Owned) && content != null) foreach (var c in content.Cars) if (c.Owned) { car = c; break; }
            if (!r.Check(car != null, "a car to sit in (" + want + ")")) yield break;
            AcCarRig rig = null; bool done = false;
            var parent = new GameObject("Lab HUD car").transform;
            parent.position = AcSpace.RoomOrigin + new Vector3(0f, 0.0f, 6f);
            yield return AcCarRig.Spawn(car, "", new AcCarRigOptions { Kind = AcCarKind.Showroom, Audio = false, Parent = parent }, x => { rig = x; done = true; });
            yield return r.Wait(() => done, 120f, "the car rig");
            if (!r.Check(rig != null && rig.Root != null, "car rig spawned (" + car.Id + ", model " + (rig != null && rig.Model != null) + ")")) yield break;
            AcLabSeat.HideHubCar();   // F's showroom car stands where the lab car is (it overlapped the miata in the first hud-vr pictures)
            if (AcHub.Current != null && AcHub.Current.Panel != null) AcHub.Current.Panel.enabled = false;
            AcSeatCar seatData = AcSeatData.For(car);
            yield return r.Wait(() => seatData.Ready, 30f, "S's seat data (vr.ini)");

            AcHud.ForceVr = true;
            AcHud.SetFake(Feed(6100f), FakeStandings(true));
            AcHud.Show(rig);
            AcHud.StartLights(3, false, true);
            AcHud.Message("CUT DETECTED", 30f);
            yield return r.Wait(() => AcHud.Vr != null && AcHud.Vr.Go.activeSelf, 10f, "the VR panel");
            if (r.Failed) yield break;
            AcHudVr vr = AcHud.Vr;
            yield return r.Wait(() => vr.Sightline != null, 20f, "the kn5 dash line (worker)");
            yield return r.Frames(3);
            r.Check(vr.EyeSource.StartsWith(seatData.HasVrEyes ? "vr.ini" : "S seat eye"), "eye from S's seat geometry: " + vr.EyeSource + " (vr.ini eyes " + seatData.HasVrEyes + ")");
            r.Check(vr.LineSource == "kn5 sightline", "dash line measured from the kn5: " + vr.LineDeg.ToString("0.0") + " deg straight ahead (" + vr.LineSource + ")");
            Vector3 eye, fwd;
            r.Check(vr.EyeWorld(out eye, out fwd), "panel eye point");
            Transform panel = vr.Go.transform;
            Vector3 rel = Quaternion.Inverse(Quaternion.LookRotation(fwd, Vector3.up)) * (panel.position - eye);
            float width = vr.Canvas.GetComponent<RectTransform>().rect.width * panel.lossyScale.x;
            float clear;
            float top = vr.MeasuredTopDeg(out clear);
            AcLabRunner.Log("hud-vr: panel centre from the eye (right, up, ahead) = " + rel.ToString("F3") + " m, width " + width.ToString("0.000") + " m; top edge " + top.ToString("0.0")
                + " deg below the horizon, worst clearance under the dash line " + clear.ToString("0.0") + " deg (fit said top " + vr.TopDeg.ToString("0.0") + ")");
            r.Check(rel.magnitude > 0.4f && rel.magnitude < 1.0f && rel.z > 0.1f, "panel 0.4-1 m from the eye and in front of it (" + rel.magnitude.ToString("0.00") + " m)");
            r.Check(clear >= 0.9f, "the panel's drawn top edge is under the car's dash line everywhere (worst " + clear.ToString("0.0") + " deg, want >= 0.9)");
            r.Check(top >= 5.9f, "the top edge is >= 6 deg under the horizon (" + top.ToString("0.0") + ")");
            float bottomDeg;
            float inner = vr.MeasuredInnerEdgeDeg(out bottomDeg);
            AcLabRunner.Log("hud-vr: beside the wheel at yaw " + vr.YawDeg.ToString("0.0") + " deg; rim outer edge " + vr.RimEdgeDeg.ToString("0.0") + " deg, panel inner edge " + inner.ToString("0.0") + " deg toward the centre, bottom " + bottomDeg.ToString("0.0") + " deg below the horizon");
            r.Check(vr.RimEdgeDeg > 5f, "steering rim edge measured from C's hub + rim radius (" + vr.RimEdgeDeg.ToString("0.0") + " deg)");
            r.Check(inner >= vr.RimEdgeDeg + 2f, "the panel stays off the steering wheel: inner edge " + inner.ToString("0.0") + " deg vs rim edge " + vr.RimEdgeDeg.ToString("0.0") + " deg (want >= +2)");
            r.Check(bottomDeg < 55f, "the panel bottom stays within 55 deg under the horizon (" + bottomDeg.ToString("0.0") + ")");
            // text size: the smallest text's em height in metres over the distance to the panel centre
            float em = vr.View.MinTextSize * panel.lossyScale.y;
            float dist = (panel.position - eye).magnitude;
            float deg = 2f * Mathf.Atan(em * 0.5f / dist) * Mathf.Rad2Deg;
            r.Check(deg >= 1.0f, "smallest VR text " + vr.View.MinTextSize + " units = " + (em * 100f).ToString("0.00") + " cm at " + dist.ToString("0.00") + " m = " + deg.ToString("0.00") + " deg (want >= 1.0)");
            r.Check(vr.View.LabTowerRow(4).StartsWith("5|"), "VR tower lists racers (" + vr.View.LabTowerRow(4) + ")");
            r.Aim(eye, eye + fwd * 2f - Vector3.up * 0.35f, 90f);
            r.Shot("hud-vr-eye", 1440, 1080);
            r.Aim(eye - fwd * 1.2f + Vector3.up * 0.5f, eye + fwd * 0.7f - Vector3.up * 0.2f, 70f);
            r.Shot("hud-vr-behind", 1440, 1080);
            r.Aim(eye, panel.position, 80f);
            r.Shot("hud-vr-panel", 1440, 1080);

            // pedals not armed: the hint; hud.show off: messages stay, gauges go
            AcHud.LabPedalsDisarmed = true;
            yield return r.Frames(3);
            r.Check(vr.View.HasMessage(AcHud.PedalHint) && vr.View.GMsg.gameObject.activeSelf, "VR hint '" + AcHud.PedalHint + "' while the pedals are not armed");
            AcHud.LabPedalsDisarmed = false;
            bool showWas = AcSettings.Flag("hud.show", true);
            AcSettings.SetInt("hud.show", 0);
            AcHud.Message("HUD OFF - MESSAGES STAY", 10f);
            yield return r.Frames(3);
            r.Check(vr.Go.activeSelf && vr.View.GMsg.gameObject.activeSelf && !vr.View.GDial.gameObject.activeSelf, "hud.show off in VR: panel keeps the messages, gauges hidden");
            AcSettings.SetInt("hud.show", showWas ? 1 : 0);

            // passenger: the panel moves to the passenger's own eye (S: the driver's mirrored) - checked within one frame (AcHud re-attaches each frame)
            Vector3 eyeD = eye;
            vr.Attach(rig, true);
            Vector3 eyeP;
            vr.EyeWorld(out eyeP, out fwd);
            Vector3 dl = rig.Root.transform.InverseTransformPoint(eyeD), pl = rig.Root.transform.InverseTransformPoint(eyeP);
            r.Check(Mathf.Abs(dl.x + pl.x) < 0.05f && Mathf.Abs(dl.x) > 0.1f, "passenger eye mirrored across the centre line (driver x " + dl.x.ToString("0.00") + ", passenger x " + pl.x.ToString("0.00") + "; " + vr.EyeSource + ")");
            vr.Attach(rig, false);

            AcHud.ClearFake();
            AcHud.Hide();
            AcHud.ForceVr = false;
            rig.Dispose();
        }

        static bool Tint(Color c, Color want) { return Mathf.Abs(c.r - want.r) < 0.02f && Mathf.Abs(c.g - want.g) < 0.02f && Mathf.Abs(c.b - want.b) < 0.02f && Mathf.Abs(c.a - want.a) < 0.02f; }
        static string Rgba(Color c) { return c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00") + "," + c.a.ToString("0.00"); }

        /// <summary>
        /// <c>hud-damage</c> (DAMAGE_DESIGN 10, iteration 2): AC's damage displayer with fake damage (AcHud.SetFakeDamage) - front 0, rear 30,
        /// left 60, right 120 km/h (= 0 / 0.5 / 1 / 1 of MAX_DAMAGE_KMH 60), suspension 0 / 0.5 / 1 / 0, engine 50 %: every layer tinted white
        /// -> yellow -> red as specified, center.png not loaded, hidden after damage_displayer.ini TIME s, hidden at DAMAGE 0 and with
        /// hud.damage off; then the VR panel's cell on a real car. Pictures hud-damage-desk.png, hud-damage-vr-*.png.
        /// </summary>
        static IEnumerator DamageHud(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            AcDamageRules dr = AcDamageRules.Current;
            AcLabRunner.Log("hud-damage: rules " + dr.Source + ": TIME " + dr.DisplayTimeS + " s, MAX_DAMAGE_KMH " + dr.MaxDamageKmh + ", POSITION_X " + dr.PosX
                            + ", DISTANCE_FROM_CENTER_Y " + dr.CenterY + ", PRINT_VALUES " + dr.PrintValues + ", IS_ACTIVE " + dr.DisplayerDefaultOn);
            r.Check(AcHudArt.Get("content/texture/damage/center.png") == null, "center.png is not loaded (acs.exe never names it)");
            bool hudDmg = AcSettings.Flag("hud.damage", dr.DisplayerDefaultOn);
            AcSettings.SetInt("hud.damage", 1);
            AcHud.SetFake(Feed(4200f), FakeStandings(false));
            AcHud.Show(null);
            AcHud.FakeDamageMultiplier = 100;
            float[] zones = { 0f, 30f, 60f, 120f, 15f };
            float[] susp = { 0f, 0.5f, 1f, 0f };
            AcHud.SetFakeDamage(zones, susp, 50f);
            yield return r.Wait(() => AcHud.Desk != null, 10f, "the monitor overlay");
            if (r.Failed) yield break;
            r.Aim(AcSpace.RoomOrigin + new Vector3(0f, 1.7f, -6f), AcSpace.RoomOrigin + new Vector3(0f, 1.2f, 4f), 60f);
            AcHud.Desk.LabFrame(r.Cam, 16f / 9f);
            yield return r.Frames(5);
            var v = AcHud.Desk.View;
            r.Check(v.LabDamageShown, "damage displayer shown after the (fake) hit");
            Color white = AcHudView.DamageTint(0f), yellow = AcHudView.DamageTint(0.5f), red = AcHudView.DamageTint(1f);
            r.Check(Tint(white, new Color(1f, 1f, 1f, 0.85f)) && Tint(yellow, new Color(1f, 1f, 0f, 0.85f)) && Tint(red, new Color(1f, 0f, 0f, 0.85f)), "tint ramp white / yellow / red at alpha 0.85");
            string[] layer = { AcHudArtList.DmgFront, AcHudArtList.DmgRear, AcHudArtList.DmgLeft, AcHudArtList.DmgRight, AcHudArtList.DmgEngine,
                               AcHudArtList.DmgSusFl, AcHudArtList.DmgSusFr, AcHudArtList.DmgSusRl, AcHudArtList.DmgSusRr, AcHudArtList.DmgGearbox, AcHudArtList.DmgTyreFl };
            Color[] want = { white, yellow, red, red, yellow, white, yellow, red, white, white, white };
            for (int i = 0; i < layer.Length; i++)
            {
                Color c = v.LabDamageColor(layer[i]);
                r.Check(Tint(c, want[i]), "layer " + System.IO.Path.GetFileNameWithoutExtension(layer[i]) + " tint " + Rgba(c) + " (want " + Rgba(want[i]) + ")");
            }
            RectTransform g = v.LabDamageRect;
            AcLabRunner.Log("hud-damage: desk group at " + g.anchoredPosition + " size " + g.rect.size + " (POSITION_X / DISTANCE_FROM_CENTER_Y)");
            r.Shot("hud-damage-desk", 1920, 1080);

            // the TIME s timeout
            float left = AcHud.DamageShowLeft;
            r.Check(left > 0f && left <= dr.DisplayTimeS + 0.01f, "display time left " + left.ToString("0.00") + " s of " + dr.DisplayTimeS);
            float until = Time.unscaledTime + left + 0.3f;
            yield return r.Wait(() => Time.unscaledTime > until, left + 5f, "the display time");
            yield return r.Frames(2);
            r.Check(!v.LabDamageShown, "hidden " + dr.DisplayTimeS + " s after the hit");
            // DAMAGE 0: never shown
            AcHud.FakeDamageMultiplier = 0;
            AcHud.SetFakeDamage(zones, susp, 50f);
            yield return r.Frames(3);
            r.Check(!v.LabDamageShown, "hidden at DAMAGE 0");
            // hud.damage off
            AcHud.FakeDamageMultiplier = 100;
            AcSettings.SetInt("hud.damage", 0);
            AcHud.SetFakeDamage(zones, susp, 50f);
            yield return r.Frames(3);
            r.Check(!v.LabDamageShown, "hidden with hud.damage off");
            AcSettings.SetInt("hud.damage", 1);
            AcHud.SetFakeDamage(zones, susp, 50f);
            yield return r.Frames(3);
            r.Check(v.LabDamageShown, "shown again with hud.damage on");

            // VR: the cell beside the panel on a real car
            var content = r.Game.Content;
            AcCarEntry car = content != null ? content.Car(AcLabBoot.Arg("-accar", "ks_mazda_miata")) : null;
            if ((car == null || !car.Owned) && content != null) foreach (var c in content.Cars) if (c.Owned) { car = c; break; }
            if (!r.Check(car != null, "a car to sit in")) { Restore(hudDmg); yield break; }
            AcCarRig rig = null; bool done = false;
            var parent = new GameObject("Lab HUD damage car").transform;
            parent.position = AcSpace.RoomOrigin + new Vector3(0f, 0.0f, 6f);
            yield return AcCarRig.Spawn(car, "", new AcCarRigOptions { Kind = AcCarKind.Showroom, Audio = false, Parent = parent }, x => { rig = x; done = true; });
            yield return r.Wait(() => done, 120f, "the car rig");
            if (!r.Check(rig != null && rig.Root != null, "car rig spawned (" + car.Id + ")")) { Restore(hudDmg); yield break; }
            AcLabSeat.HideHubCar();
            if (AcHub.Current != null && AcHub.Current.Panel != null) AcHub.Current.Panel.enabled = false;
            AcHud.ForceVr = true;
            AcHud.Show(rig);
            AcHud.SetFakeDamage(zones, susp, 50f);
            yield return r.Wait(() => AcHud.Vr != null && AcHud.Vr.Go.activeSelf, 10f, "the VR panel");
            if (!r.Failed)
            {
                AcHudVr vr = AcHud.Vr;
                yield return r.Wait(() => vr.Sightline != null, 20f, "the kn5 dash line (worker)");
                AcHud.SetFakeDamage(zones, susp, 50f);
                yield return r.Frames(3);
                r.Check(vr.View.LabDamageShown, "VR: damage cell shown");
                r.Check(Tint(vr.View.LabDamageColor(AcHudArtList.DmgLeft), red), "VR: left layer red");
                Vector3 eye, fwd;
                if (vr.EyeWorld(out eye, out fwd))
                {
                    r.Aim(eye, eye + fwd * 2f - Vector3.up * 0.35f, 100f);
                    r.Shot("hud-damage-vr-eye", 1440, 1080);
                    r.Aim(eye, vr.View.LabDamageRect.position, 70f);
                    r.Shot("hud-damage-vr-cell", 1440, 1080);
                }
            }
            AcHud.Hide();
            AcHud.ForceVr = false;
            rig.Dispose();
            Restore(hudDmg);
        }

        static void Restore(bool hudDmg)
        {
            AcHud.ClearFakeDamage();
            AcHud.ClearFake();
            AcSettings.SetInt("hud.damage", hudDmg ? 1 : 0);
        }

        static IEnumerator Live(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            yield return r.Wait(() => r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            string trackId = AcLabBoot.Arg("-actrack", "magione"), layoutId = AcLabBoot.Arg("-aclayout", "");
            AcLayoutEntry layout = content.Layout(trackId, layoutId);
            if (layout == null) { AcTrackEntry t = content.Track(trackId); if (t != null && t.Layouts.Count > 0) layout = t.Layouts[0]; }
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + " owned")) yield break;
            AcCarEntry car = content.Car(AcLabBoot.Arg("-accar", "abarth500"));
            if (car == null || !car.Owned) for (int i = 0; i < content.Cars.Count && (car == null || !car.Owned); i++) car = content.Cars[i];
            r.Check(AcRace.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : ""), "ChooseCar " + car.Id);
            AcRules rules = AcRace.DefaultRules();
            rules.Session = AcSessionType.Practice; rules.AiCount = 0;
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key + " (practice)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice, 900f, "practice on " + layout.Key);
            if (r.Failed) yield break;
            yield return r.Wait(() => AcRace.LocalCar != null && AcRace.LocalCar.Root != null, 180f, "my car");
            if (r.Failed) yield break;
            AcCarRig rig = AcRace.LocalCar;
            yield return r.Frames(10);

            // S seats me (stand-in body) -> AcEvents.SeatChanged -> AcHud.Show
            var root = new GameObject("Lab HUD Player").transform;
            var head = new GameObject("Lab Head").transform; head.SetParent(root, false); head.localPosition = new Vector3(0f, 1.2f, 0f);
            var left = new GameObject("Lab Left").transform; left.SetParent(root, false); left.localPosition = new Vector3(-0.25f, 0.9f, 0.3f);
            var right = new GameObject("Lab Right").transform; right.SetParent(root, false); right.localPosition = new Vector3(0.25f, 0.9f, 0.3f);
            root.position = rig.Root.transform.position + rig.Root.transform.right * -2.5f;
            AcSeat.LabRoot = root; AcSeat.LabHead = head; AcSeat.LabLeft = left; AcSeat.LabRight = right; AcSeat.LabVr = false;
            AcCarCamera.LabCamera = r.Cam;
            bool entered = false;
            try { entered = AcSeat.Enter(rig, AcSeatKind.Driver); } catch (Exception e) { AcLabRunner.Log("AcSeat.Enter threw " + e.Message); }
            yield return r.Wait(() => AcHud.Shown, 10f, "AcHud.Show from S's SeatChanged (Enter " + entered + ")");
            if (r.Failed) yield break;
            yield return r.Frames(5);
            AcHudFeed f = AcHud.LastFeed;
            var v = AcHud.Desk != null ? AcHud.Desk.View : null;
            r.Check(f.Car && f.Driver && f.ShownSession == AcSessionType.Practice, "live feed: car " + f.Car + ", driver " + f.Driver + ", session " + f.ShownSession);
            r.Check(v != null && v.GTimes.gameObject.activeSelf && v.LabSession == AcHudArt.Config.S("PRACTICE", "PRACTICE"), "monitor overlay shows PRACTICE (" + (v != null ? v.LabSession : "no view") + ")");
            r.Check(f.SecondsLeft < 0 && (v == null || v.LabUntimedIcon), "untimed practice: no clock, infinity icon");
            r.Check(f.OutLap == AcLocal.OutLap && (!f.OutLap || v == null || v.LabLapLabel == "OUT LAP"), "out lap from R (" + AcLocal.OutLap + ", label '" + (v != null ? v.LabLapLabel : "") + "')");
            if (v != null) { AcHud.Desk.LabFrame(r.Cam, 16f / 9f); yield return r.Frames(2); r.Shot("hud-live-practice", 1920, 1080); }

            // a 10 minute qualify: R keeps the Practice phase with Rules.Session Qualify
            AcRules q = AcRace.DefaultRules();
            q.Session = AcSessionType.Qualify; q.TimeMinutes = 10; q.AiCount = 0;
            r.Check(AcRace.RequestStart(q), "RequestStart (qualify 10 min)");
            yield return r.Wait(() => AcRace.QualifyActive, 15f, "the qualify session");
            if (!r.Failed)
            {
                yield return r.Wait(() => AcHud.Shown, 10f, "seated again after the move to the pits");
                yield return r.Frames(5);
                f = AcHud.LastFeed;
                r.Check(AcSession.Phase == AcPhase.Practice && f.ShownSession == AcSessionType.Qualify, "qualify shows QUALIFY in the Practice phase (feed " + f.ShownSession + ")");
                r.Check(f.SecondsLeft > 500 && f.SecondsLeft <= 600, "qualify clock " + f.SecondsLeft + " s");
                if (v != null) { r.Check(v.LabSession == AcHudArt.Config.S("QUALIFY", "QUALIFY"), "overlay box: " + v.LabSession + " " + v.LabSessionRight); r.Shot("hud-live-qualify", 1920, 1080); }
            }

            // the VR panel from S's eye on the live car
            AcSeat.LabVr = true;
            AcHud.ForceVr = true;
            yield return r.Wait(() => AcHud.Vr != null && AcHud.Vr.Go.activeSelf, 10f, "the VR panel on the live car");
            if (!r.Failed)
            {
                AcHudVr vr = AcHud.Vr;
                yield return r.Wait(() => vr.Sightline != null, 20f, "the kn5 dash line");
                yield return r.Frames(3);
                float clear;
                float top = vr.MeasuredTopDeg(out clear);
                r.Check(vr.EyeSource.Contains("S seat eye"), "live VR eye from S: " + vr.EyeSource);
                r.Check(clear >= 0.9f && top >= 5.9f, "live VR panel under the dash line (top " + top.ToString("0.0") + " deg, clearance " + clear.ToString("0.0") + ")");
                Vector3 eye, fwd;
                if (vr.EyeWorld(out eye, out fwd)) { r.Aim(eye, eye + fwd * 2f - Vector3.up * 0.35f, 90f); r.Shot("hud-live-vr", 1440, 1080); }
            }
            AcHud.ForceVr = false;
            AcSeat.LabVr = false;
            try { AcSeat.ForceExit(); } catch (Exception) { }
            yield return r.Frames(3);
            r.Check(!AcHud.Shown, "getting out hides the HUD (SeatChanged null)");
            AcSeat.LabRoot = AcSeat.LabHead = AcSeat.LabLeft = AcSeat.LabRight = null;
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
