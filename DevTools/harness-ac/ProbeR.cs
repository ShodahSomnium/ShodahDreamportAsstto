// Module R (Race) probes. The race core (Game/Race/AcRaceMath.cs + AcRaceTimer.cs, Unity-free) is not part of this shared harness's
// compile list, so the real probe builds only in R's copy: DevTools/agents/R/harness (defines AC_RACE_CORE):
//   cd "D:/Dreamport/Assetta Corsa/DevTools/agents/R/harness" && dotnet build -c Release -v q && dotnet run -c Release --no-build -- racedist [track [layout]]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumAssetto;

static partial class Program
{
#if !AC_RACE_CORE
    static int Cmd_racedist(List<string> a)
    {
        Console.WriteLine("racedist needs the race core: run it from DevTools/agents/R/harness (see ProbeR.cs header)");
        return 1;
    }
#else
    /// <summary>
    /// T3 / timing check on every owned layout: gate directions vs the fast lane, grid progress (before / after the line), a simulated race
    /// from EVERY grid slot along the fast lane for 2 laps (first crossing counted / not counted correctly), a timed session from the pit
    /// box (out lap + laps), reverse-over-the-line, the cut rule, sector sums, pit surfaces. Prints one line per layout + a summary.
    /// </summary>
    static int Cmd_racedist(List<string> a)
    {
        var c = AcContent.Scan(_install);
        string onlyTrack = a.Count > 1 ? a[1] : null, onlyLayout = a.Count > 2 ? a[2] : null;
        int layouts = 0, ok = 0, slotsTested = 0, slotsOk = 0;
        var problems = new List<string>();
        var cache = new Dictionary<string, Kn5File>();
        foreach (var t in c.Tracks)
            foreach (var l in t.Layouts)
            {
                if (!l.Owned) continue;
                if (onlyTrack != null && !string.Equals(t.Id, onlyTrack, StringComparison.OrdinalIgnoreCase)) continue;
                if (onlyLayout != null && !string.Equals(l.Id, onlyLayout, StringComparison.OrdinalIgnoreCase)) continue;
                layouts++;
                string p = RaceLayout(l, cache, ref slotsTested, ref slotsOk);
                if (p == null) ok++; else problems.Add(l.Key + ": " + p);
                if (cache.Count > 12) cache.Clear();
            }
        Console.WriteLine("racedist: " + ok + "/" + layouts + " layouts OK, grid slots " + slotsOk + "/" + slotsTested + " correct");
        foreach (var p in problems) Console.WriteLine("  PROBLEM " + p);
        return problems.Count == 0 ? 0 : 3;
    }

    static string RaceLayout(AcLayoutEntry l, Dictionary<string, Kn5File> cache, ref int slotsTested, ref int slotsOk)
    {
        var models = AcModelsIni.Load(l.TrackDir, l.Id);
        var files = new List<Kn5File>();
        foreach (var m in models.Models)
        {
            Kn5File k;
            if (!cache.TryGetValue(m.FullPath, out k)) { k = Kn5Scan.Scan(m.FullPath); cache[m.FullPath] = k; }
            files.Add(k);
        }
        var surfaces = AcSurfaces.Load(l.SurfacesIni, _install.SystemSurfacesIni);
        var mk = AcTrackMarkers.Collect(files, models.Models, surfaces);
        var fast = l.FastLanePath != null && File.Exists(l.FastLanePath) ? AcAiSpline.Read(l.FastLanePath) : null;
        var pit = l.PitLanePath != null && File.Exists(l.PitLanePath) ? AcAiSpline.Read(l.PitLanePath) : null;
        int pitSurf = 0, invalidSurf = 0, blackFlag = 0;
        foreach (var s in surfaces.Surfaces) { if (s.IsPitlane) pitSurf++; if (!s.IsValidTrack) invalidSurf++; if (s.BlackFlagTime > 0) blackFlag++; }

        // gates (complete ones only, indexed by gate number)
        int gateCount = 0;
        foreach (var g in mk.Gates) if (g.Complete && g.Index == gateCount) gateCount++;
        var gates = new float[gateCount * 6];
        for (int i = 0; i < gateCount; i++)
        {
            var g = mk.Gates[i]; var L = g.Left.Position; var R = g.Right.Position;
            gates[i * 6] = L.X; gates[i * 6 + 1] = L.Y; gates[i * 6 + 2] = L.Z; gates[i * 6 + 3] = R.X; gates[i * 6 + 4] = R.Y; gates[i * 6 + 5] = R.Z;
        }
        var sb = new StringBuilder();
        sb.Append(l.Key).Append(": gates ").Append(gateCount).Append(" starts ").Append(mk.Starts.Count).Append(" pits ").Append(mk.Pits.Count)
          .Append(" pitSurfaces ").Append(pitSurf).Append(" invalid ").Append(invalidSurf).Append(" blackflag ").Append(blackFlag)
          .Append(" pit_lane ").Append(pit != null && !pit.IsEmpty ? pit.Count + "pts" : "none")
          .Append(" lights ").Append(mk.StartLights.Count);
        string problem = null;
        if (fast == null || fast.IsEmpty)
        {
        sb.Append(" | no fast lane (gate-only timing)");
            Console.WriteLine(sb.ToString());
            return gateCount == 0 ? "no gates and no fast lane" : null;
        }
        if (!fast.IsClosed) sb.Append(" | OPEN line (point-to-point / drag)");
        // the fast lane must cross every gate exactly once per lap, forward (height from the line, AcRaceMath.GateRoadY)
        string hitInfo = "";
        for (int gi = 0; gi < gateCount && fast.IsClosed; gi++)
        {
            int o = gi * 6, hits = 0, fwdHits = 0; float roadY = AcRaceMath.GateRoadY(fast, gates[o], gates[o + 1], gates[o + 2], gates[o + 3], gates[o + 4], gates[o + 5]);
            for (int i = 0; i < fast.Count; i++)
            {
                int j = (i + 1) % fast.Count; float s; bool f;
                if (AcRaceMath.CrossGate(fast.X(i), fast.Y(i), fast.Z(i), fast.X(j), fast.Y(j), fast.Z(j), gates[o], gates[o + 2], gates[o + 3], gates[o + 5], roadY, out s, out f)) { hits++; if (f) fwdHits++; }
            }
            hitInfo += " g" + gi + ":" + fwdHits + "/" + hits;
            if (hits != 1 || fwdHits != 1) problem = Add(problem, "gate " + gi + " crossed " + hits + "x by the fast lane (" + fwdHits + " forward)");
        }
        sb.Append(" | line crosses gates" + hitInfo);

        // gate direction (from L/R) vs fast-lane direction near the gate; crossing of the line by the fast lane once per lap
        float minDot = 1f;
        for (int i = 0; i < gateCount; i++)
        {
            float fx, fz; int o = i * 6;
            AcRaceMath.GateForward(gates[o], gates[o + 2], gates[o + 3], gates[o + 5], out fx, out fz);
            float mx = (gates[o] + gates[o + 3]) / 2, my = (gates[o + 1] + gates[o + 4]) / 2, mz = (gates[o + 2] + gates[o + 5]) / 2;
            int n = fast.Nearest(mx, my, mz), j = (n + 1) % fast.Count;
            float lx = fast.X(j) - fast.X(n), lz = fast.Z(j) - fast.Z(n), ll = (float)Math.Sqrt(lx * lx + lz * lz);
            float dot = ll > 0 ? (fx * lx + fz * lz) / ll : 0;
            if (dot < minDot) minDot = dot;
        }
        sb.Append(" | gateDir.dot(line) min ").Append(F(minDot));
        if (gateCount > 0 && minDot < 0.5f) problem = Add(problem, "gate direction disagrees with the fast lane (" + F(minDot) + ")");

        // grid progress
        float pole = float.NaN;
        if (mk.Starts.Count > 0) { var pp = mk.Starts[0].Position; int h = -1; pole = fast.Progress(pp.X, pp.Y, pp.Z, ref h); }
        sb.Append(" | pole ").Append(F(pole)).Append(pole > 0.5f ? " (BEFORE line)" : " (after/on line)");

        if (!fast.IsClosed || gateCount == 0) { Console.WriteLine(sb.ToString()); return problem; }

        // race from every grid slot: drive the fast lane from the slot for 2 laps + 5 %, expect exactly 2 laps, counted at D ~ 1 and 2
        var timer = new AcRaceTimer();
        timer.Setup(fast, gates, gateCount);
        int slotBad = 0; string firstBad = null; int lapMsSample = 0, secSumBad = 0;
        foreach (var st in mk.Starts)
        {
            slotsTested++;
            var sp = st.Position;
            float p0 = fast.ProgressAt(sp.X, sp.Z, AcRaceMath.NearestNearLine(fast, sp.X, sp.Y, sp.Z, AcRaceTimer.StartWindow));
            timer.Begin(AcTimerMode.Race, 0.0, sp.X, sp.Y, sp.Z);
            timer.TargetLaps = 2;
            float d0 = timer.RaceDistance;
            int laps = 0; var at = new List<float>(); bool finished = false;
            double tt = 0; float px = sp.X, py = sp.Y, pz = sp.Z;
            int i0 = AcRaceMath.NearestNearLine(fast, sp.X, sp.Y, sp.Z, AcRaceTimer.StartWindow);
            int steps = (int)(fast.Count * 2.3f);
            for (int s = 1; s <= steps; s++)
            {
                int i = (i0 + s) % fast.Count;
                float x = fast.X(i), y = fast.Y(i), z = fast.Z(i);
                float seg = (float)Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                float v = Math.Max(8f, fast.P(i, AcAiSpline.Speed));
                tt += seg / v;
                int bits = timer.Step(tt, x, y, z, 0);
                if ((bits & AcRaceTimer.LapDone) != 0)
                {
                    laps++; at.Add(timer.RaceDistance);
                    if (laps == 2) { lapMsSample = timer.LastMs; int sum = 0; foreach (int ms in timer.LastSectorMs) sum += ms; if (Math.Abs(sum - timer.LastMs) > 2) secSumBad++; }
                }
                if ((bits & AcRaceTimer.FinishedNow) != 0) finished = true;
                px = x; py = y; pz = z;
                if (timer.RaceDistance > 2.05f) break;
            }
            bool good = laps == 2 && finished && at.Count == 2 && Math.Abs(at[0] - 1f) < 0.02f && Math.Abs(at[1] - 2f) < 0.02f;
            if (good) slotsOk++; else { slotBad++; if (firstBad == null) firstBad = "AC_START_" + st.Index + " p0 " + F(p0) + " d0 " + F(d0) + " laps " + laps + " at " + string.Join("/", at.ConvertAll(F)) + (finished ? " fin" : " nofin"); }
        }
        sb.Append(" | race slots ok ").Append(mk.Starts.Count - slotBad).Append("/").Append(mk.Starts.Count);
        if (slotBad > 0) problem = Add(problem, "race lap rule wrong at " + firstBad);
        if (secSumBad > 0) problem = Add(problem, "sector times do not sum to the lap (" + secSumBad + ")");
        sb.Append(" ai-hint lap ").Append(AcRaceMath.FormatLap(lapMsSample));

        // timed session from pit box 0: out lap until the first crossing, then laps; cut rule; reverse over the line
        if (mk.Pits.Count > 0)
        {
            var bp = mk.Pits[0].Position;
            int i0 = fast.Nearest(bp.X, bp.Y, bp.Z);
            float x0 = fast.X(i0), y0 = fast.Y(i0), z0 = fast.Z(i0);
            timer.Begin(AcTimerMode.Timed, 0.0, x0, y0, z0);
            float p0 = timer.Progress;
            int started = 0, laps = 0, validLaps = 0, crossings = 0;
            double tt = 0; float px = x0, pz = z0;
            int steps = (int)(fast.Count * 2.5f);
            for (int s = 1; s <= steps; s++)
            {
                int i = (i0 + s) % fast.Count;
                float x = fast.X(i), y = fast.Y(i), z = fast.Z(i);
                tt += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz)) / 30.0;
                // second timed lap: 3 wheels off for a moment at mid-lap (cut), 2 wheels off elsewhere (allowed)
                int off = laps == 1 && Math.Abs(fast.PointProgress(i) - 0.5f) < 0.002f ? 3 : (Math.Abs(fast.PointProgress(i) - 0.25f) < 0.002f ? 2 : 0);
                int bits = timer.Step(tt, x, y, z, off);
                if ((bits & AcRaceTimer.TimingStarted) != 0) { started++; crossings++; }
                if ((bits & AcRaceTimer.LapDone) != 0) { laps++; crossings++; if (timer.LastValid) validLaps++; }
                px = x; pz = z;
            }
            // expected line crossings while driving 2.5 laps from p0: whole numbers in (p0, p0 + 2.5]
            int expect = (int)Math.Floor(p0 + 2.5f) - (int)Math.Floor(p0);
            bool good = started == 1 && crossings == expect && laps == expect - 1 && validLaps == laps - (laps >= 2 ? 1 : 0);
            sb.Append(" | timed from pit p0 ").Append(F(p0)).Append(" crossings ").Append(crossings).Append("/").Append(expect).Append(" laps ").Append(laps).Append(" valid ").Append(validLaps);
            if (!good) problem = Add(problem, "timed session: started " + started + " crossings " + crossings + "/" + expect + " laps " + laps + " valid " + validLaps);

            // reverse over the line then forward again: no lap
            timer.Begin(AcTimerMode.Timed, 0.0, fast.X(fast.Count - 30), fast.Y(fast.Count - 30), fast.Z(fast.Count - 30));
            int seq = 0; double t2 = 0; int lapsRev = 0;
            var idx = new List<int>();
            for (int s = 1; s <= 40; s++) idx.Add(fast.Count - 30 + s);       // over the line (starts timing)
            for (int s = 1; s <= 25; s++) idx.Add(fast.Count + 10 - s);       // reverse back over the line
            for (int s = 1; s <= 30; s++) idx.Add(fast.Count - 15 + s);       // forward over it again: must NOT be a lap
            foreach (int raw in idx)
            {
                int i = ((raw % fast.Count) + fast.Count) % fast.Count;
                t2 += 0.05; seq++;
                int bits = timer.Step(t2, fast.X(i), fast.Y(i), fast.Z(i), 0);
                if ((bits & AcRaceTimer.LapDone) != 0) lapsRev++;
            }
            sb.Append(" | reverse-over-line laps ").Append(lapsRev);
            if (lapsRev != 0) problem = Add(problem, "reversing over the line counted a lap");
        }

        // pit box / hotlap progress (spawn facts)
        if (mk.Pits.Count > 0) { var bp = mk.Pits[0].Position; int h = -1; sb.Append(" | box0 p ").Append(F(fast.Progress(bp.X, bp.Y, bp.Z, ref h))); }
        if (mk.HotlapStarts.Count > 0) { var hp = mk.HotlapStarts[0].Position; int h = -1; sb.Append(" hotlap p ").Append(F(fast.Progress(hp.X, hp.Y, hp.Z, ref h))); }

        // spawn heading (T2): marker HeadingDeg (Unity yaw) vs the fast lane direction at the slot, worst over starts + pits
        float worst = 0f;
        foreach (var m in mk.Starts) worst = Math.Max(worst, HeadingErr(fast, m));
        sb.Append(" | start heading err max ").Append(F(worst)).Append(" deg");
        if (worst > 25f) problem = Add(problem, "start marker heading off the line by " + F(worst) + " deg");
        Console.WriteLine(sb.ToString());
        return problem;
    }

    static float HeadingErr(AcAiSpline fast, AcMarker m)
    {
        var p = m.Position; int n = fast.Nearest(p.X, p.Y, p.Z), j = (n + 1) % fast.Count;
        float lx = fast.X(j) - fast.X(n), lz = fast.Z(j) - fast.Z(n);
        // Unity yaw of an AC direction (x, z): Unity (-x, z) -> atan2(-x, z)
        float lineYaw = (float)(Math.Atan2(-lx, lz) * 180.0 / Math.PI);
        float e = Math.Abs(m.HeadingDeg - lineYaw) % 360f;
        return e > 180f ? 360f - e : e;
    }

    static string Add(string a, string b) { return a == null ? b : a + "; " + b; }

    /// <summary>The cut rule and start-light arithmetic on their own (no install needed beyond the harness boot).</summary>
    static int Cmd_racerules(List<string> a)
    {
        int bad = 0;
        // ALLOWED_TYRES_OUT
        bool[,] expect = { { false, false, false, false, false }, { false, false, true, true, true }, { false, false, false, true, true } };
        int[] allowed = { -1, 1, 2 };
        for (int k = 0; k < 3; k++) for (int w = 0; w <= 4; w++)
                if (AcRaceMath.IsCut(w, allowed[k]) != expect[k, w]) { bad++; Console.WriteLine("cut rule wrong: allowed " + allowed[k] + " wheels " + w); }
        // lights: 5 reds a second apart, hold 1-3 s, green at StartUtc; same hold on every client (from the ms value)
        double start = 1761955200.123;
        int hold = AcRaceMath.HoldMs(start);
        double first = AcRaceMath.LightsStartUtc(start);
        Console.WriteLine("lights: green at " + start.ToString("0.000") + ", hold " + hold + " ms, first red " + (start - first).ToString("0.000") + " s before green");
        if (hold < 1000 || hold > 3000) bad++;
        bool g; int prev = -1;
        for (double t = first - 1; t < start + 0.5; t += 0.25)
        {
            int r = AcRaceMath.RedsAt(t, start, out g);
            if (r != prev) { Console.WriteLine("  t-" + (start - t).ToString("0.00") + " s: reds " + r + (g ? " GREEN" : "") + " lamps(4) " + AcRaceMath.LampsFor(r, 4)); prev = r; }
        }
        var holds = new int[2001]; int min = 9999, max = 0;
        for (int i = 0; i < 2000; i++) { int h = AcRaceMath.HoldMs(start + i * 0.737); if (h < min) min = h; if (h > max) max = h; }
        Console.WriteLine("hold spread over 2000 starts: " + min + ".." + max + " ms");
        Console.WriteLine("format: " + AcRaceMath.FormatLap(83456) + " " + AcRaceMath.FormatLap(0) + " " + AcRaceMath.FormatGap(1234, 0) + " " + AcRaceMath.FormatGap(0, 2));
        Console.WriteLine("racerules: " + (bad == 0 ? "OK" : bad + " wrong"));
        return bad == 0 ? 0 : 3;
    }
#endif
}
#if AC_RACE_CORE
static partial class Program
{
    static int Cmd_racegates(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var l = FindLayout(c, a[1], a.Count > 2 ? a[2] : "");
        var models = AcModelsIni.Load(l.TrackDir, l.Id);
        var files = new List<Kn5File>();
        foreach (var m in models.Models) files.Add(Kn5Scan.Scan(m.FullPath));
        var mk = AcTrackMarkers.Collect(files, models.Models, null);
        var fast = AcAiSpline.Read(l.FastLanePath);
        foreach (var g in mk.Gates)
        {
            if (!g.Complete) continue;
            var L = g.Left.Position; var R = g.Right.Position;
            int hits = 0; string info = "";
            for (int i = 0; i < fast.Count; i++)
            {
                int j = (i + 1) % fast.Count; float s; bool f;
                bool x = AcRaceMath.CrossGate(fast.X(i), fast.Y(i), fast.Z(i), fast.X(j), fast.Y(j), fast.Z(j), L.X, L.Z, R.X, R.Z, AcRaceMath.GateRoadY(fast, L.X, L.Y, L.Z, R.X, R.Y, R.Z), out s, out f);
                if (x) { hits++; info += " @" + F(fast.PointProgress(i)) + (f ? " fwd" : " back") + " lineY " + F(fast.Y(i)); }
            }
            { var mid = new float[]{(L.X+R.X)/2,(L.Y+R.Y)/2,(L.Z+R.Z)/2}; int n = fast.Nearest(mid[0], mid[1], mid[2]); Console.WriteLine("  nearest line pt " + n + " (" + F(fast.X(n)) + "," + F(fast.Y(n)) + "," + F(fast.Z(n)) + ") p " + F(fast.PointProgress(n)) + " lateral " + F(fast.LateralOffset(mid[0], mid[2], n))); }
            Console.WriteLine("gate " + g.Index + " L " + L + " R " + R + " width " + F((float)Math.Sqrt((L.X-R.X)*(L.X-R.X)+(L.Z-R.Z)*(L.Z-R.Z))) + " hits " + hits + info);
        }
        return 0;
    }

    /// <summary>
    /// Master hand-over seeding (AcRaceTimer.Seed) on every owned closed layout: timer A races from AC_START_0 along the fast lane; at
    /// 1.37 laps a second timer B (the new master) begins at the same green clock from the car's current pose and is seeded with A's
    /// laps / race distance / best / last; both then drive on to a 3-lap finish. B must count the same laps at the same crossings, finish at
    /// the same crossing with the same FinishedMs, and not record the partial lap's time.
    /// </summary>
    static int Cmd_raceseed(List<string> a)
    {
        var c = AcContent.Scan(_install);
        int tested = 0, ok = 0;
        float seedAt = a.Count > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 1.37f;
        foreach (var t in c.Tracks)
            foreach (var l in t.Layouts)
            {
                if (!l.Owned || l.FastLanePath == null || !File.Exists(l.FastLanePath)) continue;
                var fast = AcAiSpline.Read(l.FastLanePath);
                if (fast.IsEmpty || !fast.IsClosed) continue;
                var models = AcModelsIni.Load(l.TrackDir, l.Id);
                var files = new List<Kn5File>();
                foreach (var m in models.Models) files.Add(Kn5Scan.Scan(m.FullPath));
                var mk = AcTrackMarkers.Collect(files, models.Models, null);
                int gateCount = 0;
                foreach (var g in mk.Gates) if (g.Complete && g.Index == gateCount) gateCount++;
                if (gateCount == 0 || mk.Starts.Count == 0) continue;
                tested++;
                var gates = new float[gateCount * 6];
                for (int i = 0; i < gateCount; i++) Array.Copy(Gate6(mk.Gates[i]), 0, gates, i * 6, 6);
                var A = new AcRaceTimer(); var B = new AcRaceTimer();
                A.Setup(fast, gates, gateCount); B.Setup(fast, gates, gateCount);
                var sp = mk.Starts[0].Position;
                A.Begin(AcTimerMode.Race, 0.0, sp.X, sp.Y, sp.Z); A.TargetLaps = 3;
                int i0 = AcRaceMath.NearestNearLine(fast, sp.X, sp.Y, sp.Z, AcRaceTimer.StartWindow);
                double tt = 0; float px = sp.X, pz = sp.Z; bool bOn = false; string why = null;
                int lastA = 0, lastBeforeSeed = 0;
                for (int s = 1; s <= fast.Count * 4; s++)
                {
                    int i = (i0 + s) % fast.Count;
                    float x = fast.X(i), y = fast.Y(i), z = fast.Z(i);
                    tt += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz)) / Math.Max(8.0, fast.P(i, AcAiSpline.Speed));
                    int ba = A.Step(tt, x, y, z, 0);
                    if (!bOn && A.RaceDistance >= seedAt)
                    {
                        bOn = true; lastBeforeSeed = A.LastMs;
                        B.Begin(AcTimerMode.Race, 0.0, x, y, z); B.TargetLaps = 3;      // the green was at clock 0 (ClockAt(StartUtc))
                        B.Seed(A.Laps, A.RaceDistance, A.BestMs, A.LastMs, A.Finished, A.FinishedMs, A.PenaltyMs);
                        if (B.Laps != A.Laps || Math.Abs(B.RaceDistance - A.RaceDistance) > 0.01f) why = Add(why, "seed: laps " + B.Laps + "/" + A.Laps + " dist " + F(B.RaceDistance) + "/" + F(A.RaceDistance));
                    }
                    else if (bOn)
                    {
                        int bb = B.Step(tt, x, y, z, 0);
                        if (((ba ^ bb) & (AcRaceTimer.LapDone | AcRaceTimer.FinishedNow)) != 0) why = Add(why, "crossing differs at lap " + A.Laps + " (A bits " + ba + ", B " + bb + ")");
                        if ((bb & AcRaceTimer.LapDone) != 0 && seedAt >= 1f && B.Laps == (int)seedAt + 1 && B.LastMs != lastBeforeSeed) why = Add(why, "partial lap recorded " + B.LastMs);
                    }
                    lastA = A.Laps;
                    px = x; pz = z;
                    if (A.Finished) break;
                }
                if (!A.Finished || !B.Finished || A.FinishedMs != B.FinishedMs || A.Laps != B.Laps) why = Add(why, "finish A " + A.Finished + " " + A.FinishedMs + " laps " + A.Laps + " / B " + B.Finished + " " + B.FinishedMs + " laps " + B.Laps);
                if (why == null) ok++; else Console.WriteLine("  PROBLEM " + l.Key + ": " + why);
            }
        Console.WriteLine("raceseed (seeded at " + seedAt + " laps): " + ok + "/" + tested + " closed layouts: a seeded timer counts the same laps and finish");
        return ok == tested ? 0 : 3;
    }

    static float[] Gate6(AcTimeGate g)
    {
        if (g == null || !g.Complete) return null;
        var L = g.Left.Position; var R = g.Right.Position;
        return new float[] { L.X, L.Y, L.Z, R.X, R.Y, R.Z };
    }

    /// <summary>
    /// Open courses (point-to-point AC_AB_START/FINISH, drag AC_OPEN_FINISH) on every owned layout that has them: AcRaceTimer.SetupOpen as
    /// AcTiming uses it, a race from AC_START_0 along the fast lane (green at t=0, splits in order, the finish ends the run = 1 lap +
    /// Finished), and a timed run (start gate crossing, or moving off for drag) from the start marker. Sector sums = run time.
    /// </summary>
    static int Cmd_racea2b(List<string> a)
    {
        var c = AcContent.Scan(_install);
        int tested = 0, ok = 0;
        foreach (var t in c.Tracks)
            foreach (var l in t.Layouts)
            {
                if (!l.Owned) continue;
                var models = AcModelsIni.Load(l.TrackDir, l.Id);
                var files = new List<Kn5File>();
                foreach (var m in models.Models) files.Add(Kn5Scan.Scan(m.FullPath));
                var mk = AcTrackMarkers.Collect(files, models.Models, null);
                float[] ab0 = Gate6(mk.AbStart), ab1 = Gate6(mk.AbFinish), of = Gate6(mk.OpenFinish);
                if (!(ab0 != null && ab1 != null) && of == null) continue;
                tested++;
                var fast = l.FastLanePath != null && File.Exists(l.FastLanePath) ? AcAiSpline.Read(l.FastLanePath) : null;
                int gateCount = 0;
                foreach (var g in mk.Gates) if (g.Complete && g.Index == gateCount) gateCount++;
                var splits = new float[gateCount * 6];
                for (int i = 0; i < gateCount; i++) Array.Copy(Gate6(mk.Gates[i]), 0, splits, i * 6, 6);
                bool a2b = ab0 != null && ab1 != null;
                float[] start = a2b ? ab0 : null, finish = a2b ? ab1 : of;
                var timer = new AcRaceTimer();
                timer.SetupOpen(fast, splits, gateCount, start, finish);
                var sb = new StringBuilder();
                sb.Append(l.Key).Append(a2b ? ": A2B" : ": OPEN_FINISH").Append(" splits ").Append(gateCount).Append(" sectors ").Append(timer.SectorCount)
                  .Append(" starts ").Append(mk.Starts.Count).Append(" pits ").Append(mk.Pits.Count)
                  .Append(" | line ").Append(fast == null || fast.IsEmpty ? "none" : fast.Count + " pts " + F(fast.LapLength) + " m " + (fast.IsClosed ? "closed" : "open"));
                if (fast == null || fast.IsEmpty || mk.Starts.Count == 0) { Console.WriteLine(sb + " | SKIP (no line / start)"); continue; }
                int h = -1;
                var sp = mk.Starts[0].Position;
                float pStart = start != null ? fast.Progress((start[0] + start[3]) / 2, (start[1] + start[4]) / 2, (start[2] + start[5]) / 2, ref h) : float.NaN;
                h = -1; float pFin = fast.Progress((finish[0] + finish[3]) / 2, (finish[1] + finish[4]) / 2, (finish[2] + finish[5]) / 2, ref h);
                h = -1; float pGrid = fast.Progress(sp.X, sp.Y, sp.Z, ref h);
                if (mk.Pits.Count > 0) { var pp = mk.Pits[0].Position; h = -1; sb.Append(" | p pit0 ").Append(F(fast.Progress(pp.X, pp.Y, pp.Z, ref h))); }
                sb.Append(" | p grid ").Append(F(pGrid)).Append(" start ").Append(F(pStart)).Append(" finish ").Append(F(pFin));
                for (int i = 0; i < gateCount; i++) { h = -1; sb.Append(" split").Append(i).Append(' ').Append(F(fast.Progress((splits[i * 6] + splits[i * 6 + 3]) / 2, splits[i * 6 + 1], (splits[i * 6 + 2] + splits[i * 6 + 5]) / 2, ref h))); }
                float runM = (float)Math.Sqrt((finish[0] + finish[3] - 2 * sp.X) * (finish[0] + finish[3] - 2 * sp.X) / 4 + (finish[2] + finish[5] - 2 * sp.Z) * (finish[2] + finish[5] - 2 * sp.Z) / 4);
                sb.Append(" | grid0->finish straight ").Append(F(runM)).Append(" m");

                string problem = null;
                for (int mode = 0; mode < 2; mode++)
                {
                    bool race = mode == 0;
                    int i0 = fast.Nearest(sp.X, sp.Y, sp.Z);
                    timer.Begin(race ? AcTimerMode.Race : AcTimerMode.Timed, 0.0, sp.X, sp.Y, sp.Z);
                    double tt = 0; float px = sp.X, pz = sp.Z;
                    int laps = 0, sectors = 0, startedBits = 0, finishedBits = 0, sum = 0;
                    int limit = fast.Count + 5, n = fast.Count;
                    for (int s = 1; s <= limit; s++)
                    {
                        int i = i0 + s;
                        float x, y, z;
                        if (i >= n && !fast.IsClosed)
                        {
                            // the open line ends at the finish: drive on 10 m per step along its last segment (the road goes on)
                            int k = i - n + 1; if (k > 6) break;
                            float dx = fast.X(n - 1) - fast.X(n - 2), dz = fast.Z(n - 1) - fast.Z(n - 2), dl = (float)Math.Sqrt(dx * dx + dz * dz);
                            x = fast.X(n - 1) + dx / dl * 10f * k; y = fast.Y(n - 1); z = fast.Z(n - 1) + dz / dl * 10f * k;
                            i = n - 1;
                        }
                        else { i %= n; x = fast.X(i); y = fast.Y(i); z = fast.Z(i); }
                        tt += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz)) / Math.Max(8.0, fast.P(i, AcAiSpline.Speed));
                        int bits = timer.Step(tt, x, y, z, 0);
                        if ((bits & AcRaceTimer.TimingStarted) != 0) startedBits++;
                        if ((bits & AcRaceTimer.SectorDone) != 0) sectors++;
                        if ((bits & AcRaceTimer.FinishedNow) != 0) finishedBits++;
                        if ((bits & AcRaceTimer.LapDone) != 0) { laps++; sum = 0; foreach (int ms in timer.LastSectorMs) sum += ms; }
                        px = x; pz = z;
                        if (laps > 0 && (race || s > limit)) break;
                    }
                    sb.Append(race ? " | RACE" : " | TIMED").Append(" laps ").Append(laps).Append(" splits ").Append(sectors).Append(race ? " fin " + finishedBits : " started " + startedBits)
                      .Append(" run ").Append(AcRaceMath.FormatLap(timer.LastMs)).Append(" sectors ").Append(string.Join("/", Array.ConvertAll(timer.LastSectorMs, v => AcRaceMath.FormatLap(v))));
                    if (race && timer.Finished) sb.Append(" finishedMs ").Append(AcRaceMath.FormatLap(timer.FinishedMs));
                    bool good = laps == 1 && sectors == gateCount && Math.Abs(sum - timer.LastMs) <= 2 && (race ? finishedBits == 1 && timer.Finished : startedBits == 1);
                    if (!good) problem = Add(problem, (race ? "race" : "timed") + ": laps " + laps + " splits " + sectors + "/" + gateCount + " sum " + sum + " vs " + timer.LastMs);
                }
                if (problem == null) ok++;
                Console.WriteLine(sb + (problem != null ? " | PROBLEM " + problem : " | OK"));
            }
        Console.WriteLine("racea2b: " + ok + "/" + tested + " open-course layouts OK");
        return ok == tested ? 0 : 3;
    }
}
#endif
