using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumAssetto;

// Module P, iteration 2 (car damage): offline probes for Data/Physics/AcCarDamage.cs and the damage keys in AcCarSpec.
//   dmgspec            parse AcDamageSpec + wing zones for every owned car: census (DAMAGE_DESIGN 10: 291 wings + 1 fin, susp 83, engine 86 /
//                      turbo 84, PIT 20/2/30 with exos 5 and miura 6), no new spec problems, the system/cfg rules
//   dmgunit            formula unit tests (accumulation, cap, multiplier, rod, engine life, repair, %, seconds, wire, aero, toe, zone pick)
//   dmgsim <car|all>   scripted impacts -> zones, per-corner toe (clamp holds fleet-wide), aero factors, engine life under a money shift
//   dmgturbo           default total boost per turbo car after the P0 fix (and before it); none above TURBO_BOOST_THRESHOLD
// Harness hook used by ProbeP's psim / pchassis: env PDMG = damage multiplier % (default 0 = off) on the simulated core.
static partial class Program
{
    static readonly CultureInfo DInv = CultureInfo.InvariantCulture;
    static string D(float v, string fmt = "0.###") { return float.IsNaN(v) ? "-" : v.ToString(fmt, DInv); }

    /// <summary>The damage state ProbeP's simulations attach to their AcCarCore: multiplier from env PDMG (%), default 0 (off).</summary>
    static AcCarDamage HarnessDamage(AcCarSpec s)
    {
        float m = 0f;
        string ev = Environment.GetEnvironmentVariable("PDMG");
        if (!string.IsNullOrEmpty(ev)) float.TryParse(ev, NumberStyles.Float, DInv, out m);
        return new AcCarDamage(s.Damage, AcDamageRules.Current) { Multiplier = Math.Max(0f, Math.Min(1f, m / 100f)) };
    }

    static List<AcCarEntry> OwnedCarsFor(string only)
    {
        var content = AcContent.Scan(_install);
        var list = new List<AcCarEntry>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (only != null && only != "all" && !car.Id.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(car);
        }
        return list;
    }

    // ------------------------------------------------------------------ dmgspec
    static int Cmd_dmgspec(List<string> a)
    {
        AcDamageRules rules = AcDamageRules.Load(_install);
        Console.WriteLine("rules (" + rules.Source + "): GLASS_THRESHOLD " + rules.GlassThreshold + ", displayer TIME " + rules.DisplayTimeS + " MAX_DAMAGE_KMH " + rules.MaxDamageKmh + " PRINT " + rules.PrintValues +
            " POS " + rules.PosX + "/" + rules.CenterY + ", smoke ENGINE_LIFE " + rules.SmokeEngineLife + " SECONDS " + rules.SmokeSeconds + ", gameplay IS_ACTIVE " + rules.DisplayerDefaultOn + ", server DAMAGE_MULTIPLIER " + rules.DefaultMultiplier);
        int cars = 0, wingsZoned = 0, wingsAll = 0, fins = 0, finsZoned = 0, susp = 0, eng = 0, turbo = 0, pit = 0, mismatch = 0, damageProblems = 0, carsWithProblems = 0, problems = 0;
        var pitBody = new SortedDictionary<float, int>(); var pitEng = new SortedDictionary<float, int>(); var pitSusp = new SortedDictionary<float, int>();
        var noSusp = new List<string>(); var noTurbo = new List<string>(); var odd = new List<string>();
        foreach (var car in OwnedCarsFor("all"))
        {
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) { odd.Add(car.Id + " (no spec)"); continue; }
            cars++;
            if (s.Problems.Count > 0) { carsWithProblems++; problems += s.Problems.Count; }
            foreach (string p in s.Problems) if (p.IndexOf("DAMAGE", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("PIT_STOP", StringComparison.OrdinalIgnoreCase) >= 0) { damageProblems++; odd.Add(car.Id + ": " + p); }
            AcIni aero; s.Inis.TryGetValue("aero.ini", out aero);
            if (aero != null)
            {
                foreach (AcIniSection w in aero.WithPrefix("WING_")) { wingsAll++; if (HasZoneKey(w)) wingsZoned++; }
                foreach (AcIniSection f in aero.WithPrefix("FIN_")) { fins++; if (HasZoneKey(f)) finsZoned++; }
                // the parsed wing values equal the raw ini
                foreach (AcWingSpec ws in s.Wings)
                {
                    AcIniSection w = aero["WING_" + ws.Index];
                    if (w == null) continue;
                    if (ws.ZoneFrontCl != w.GetFloat("ZONE_FRONT_CL", 0f) || ws.ZoneFrontCd != w.GetFloat("ZONE_FRONT_CD", 0f) || ws.ZoneRearCl != w.GetFloat("ZONE_REAR_CL", 0f) || ws.ZoneRearCd != w.GetFloat("ZONE_REAR_CD", 0f) ||
                        ws.ZoneLeftCl != w.GetFloat("ZONE_LEFT_CL", 0f) || ws.ZoneLeftCd != w.GetFloat("ZONE_LEFT_CD", 0f) || ws.ZoneRightCl != w.GetFloat("ZONE_RIGHT_CL", 0f) || ws.ZoneRightCd != w.GetFloat("ZONE_RIGHT_CD", 0f))
                    { mismatch++; odd.Add(car.Id + " WING_" + ws.Index + " zone values differ from the ini"); }
                }
            }
            AcDamageSpec d = s.Damage;
            if (d.HasSusp) susp++; else noSusp.Add(car.Id);
            if (d.HasRpmDamage) eng++;
            if (d.HasTurboDamage) turbo++; else noTurbo.Add(car.Id);
            if (d.HasPitStop) { pit++; Inc(pitBody, d.PitBodySecPer10); Inc(pitEng, d.PitEngineSecPer10); Inc(pitSusp, d.PitSuspSecPer10); }
            if (a.Count > 1 && a[1] == "-v")
                Console.WriteLine(car.Id.PadRight(36) + " susp " + (d.HasSusp ? D(d.SuspMinVelocity) + "/" + D(d.SuspGain, "0.#####") + "/" + D(d.SuspMaxDamage) : "-") + "  rpm " + (d.HasRpmDamage ? D(d.RpmThreshold) + " k" + D(d.RpmDamageK) : "-") +
                    "  turbo " + (d.HasTurboDamage ? D(d.TurboBoostThreshold) + " k" + D(d.TurboDamageK) : "-") + "  pit " + D(d.PitBodySecPer10) + "/" + D(d.PitEngineSecPer10) + "/" + D(d.PitSuspSecPer10) + "  wings " + s.Wings.Count);
        }
        Console.WriteLine("cars " + cars + "; wings " + wingsAll + " (with ZONE_ keys " + wingsZoned + "), fins " + fins + " (with ZONE_ keys " + finsZoned + ", not simulated: no fin model in iteration 1)");
        Console.WriteLine("suspensions.ini [DAMAGE] " + susp + " (none: " + string.Join(", ", noSusp) + ")");
        Console.WriteLine("engine.ini [DAMAGE] rpm " + eng + ", turbo " + turbo + " (none: " + string.Join(", ", noTurbo) + ")");
        Console.WriteLine("car.ini [PIT_STOP] " + pit + ": body " + Hist(pitBody) + " | engine " + Hist(pitEng) + " | susp " + Hist(pitSusp));
        Console.WriteLine("parsed wing zones vs ini mismatches " + mismatch + "; damage-related spec problems " + damageProblems + "; all spec problems " + problems + " on " + carsWithProblems + " cars (iteration 1: 8 on 5, explained)");
        foreach (string o in odd) Console.WriteLine("  " + o);
        bool ok = cars == 86 && wingsZoned == 291 && fins == 1 && susp == 83 && eng == 86 && turbo == 84 && pit == 86 && mismatch == 0 && damageProblems == 0;
        Console.WriteLine(ok ? "DMGSPEC PASS (census reproduced, 0 damage problems)" : "DMGSPEC FAIL");
        return ok ? 0 : 1;
    }

    static bool HasZoneKey(AcIniSection s)
    {
        foreach (string k in s.Keys) if (k.StartsWith("ZONE_", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static void Inc(SortedDictionary<float, int> h, float v) { int n; h.TryGetValue(v, out n); h[v] = n + 1; }
    static string Hist(SortedDictionary<float, int> h) { var sb = new System.Text.StringBuilder(); foreach (var kv in h) sb.Append(D(kv.Key)).Append(" x").Append(kv.Value).Append("  "); return sb.ToString().Trim(); }

    // ------------------------------------------------------------------ dmgunit
    static int _uPass, _uFail;
    static void U(bool ok, string what) { if (ok) _uPass++; else _uFail++; Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); }
    static bool Near(float a, float b, float tol = 1e-4f) { return Math.Abs(a - b) <= tol * Math.Max(1f, Math.Abs(b)); }

    static int Cmd_dmgunit(List<string> a)
    {
        _uPass = 0; _uFail = 0;
        var rules = new AcDamageRules(); // fallbacks: MaxDamageKmh 60, smoke 0
        var spec = new AcDamageSpec { HasSusp = true, SuspMinVelocity = 40f, SuspGain = 0.0004f, SuspMaxDamage = 0.05f, HasRpmDamage = true, RpmThreshold = 9200f, RpmDamageK = 1f,
            HasTurboDamage = true, TurboBoostThreshold = 1.4f, TurboDamageK = 5f, HasPitStop = true, PitBodySecPer10 = 20f, PitEngineSecPer10 = 2f, PitSuspSecPer10 = 30f };

        Console.WriteLine("multiplier 0 = off");
        var d = new AcCarDamage(spec, rules);
        int v0 = d.Version, m0 = d.MechVersion;
        U(!d.AddImpact(0, 80f, 0), "AddImpact at m 0 returns false");
        d.StepEngine(0.003f, 12000f, 3f);
        int w0, w1; d.ToWire(out w0, out w1);
        U(d.Zone[0] == 0f && d.RodOffset[0] == 0f && d.EngineLife == AcCarDamage.LifeNew && d.Version == v0 && d.MechVersion == m0 && w0 == 0 && w1 == 0 && d.TakeChanges() == AcDamageChange.None, "nothing changes, wire 0/0, no change flags");
        float cl, cd; var wing = new AcWingSpec { ZoneFrontCl = 0.015f, ZoneFrontCd = 0.015f, ZoneRearCd = 0.005f, ZoneLeftCd = 0.01f, ZoneRightCd = 0.01f };
        d.Zone[0] = 60f; d.AeroFactors(wing, out cl, out cd); d.Zone[0] = 0f;
        U(cl == 1f && cd == 1f, "aero factors 1 / 1 at m 0 even with zone damage");

        Console.WriteLine("accumulation (m 1)");
        d = new AcCarDamage(spec, rules) { Multiplier = 1f };
        U(d.AddImpact(0, 40f, -1) && d.Zone[0] == 40f && d.ZoneByte(0) == 40, "front 40 -> 40 km/h");
        d.AddImpact(0, 30.7f, -1);
        U(Near(d.Zone[0], 70.7f) && d.ZoneByte(0) == 70, "sum: +30.7 -> 70.7, ZoneByte floor 70");
        d.AddImpact(0, 500f, -1);
        U(d.Zone[0] == 255f && d.ZoneByte(0) == 255, "cap 255");
        AcDamageChange ch; float kmh; ch = d.TakeChanges(out kmh);
        U(ch == AcDamageChange.Zones && kmh == 500f && d.TakeChanges() == AcDamageChange.None, "flags Zones, max impact 500, cleared after take");
        AcCarDamage.Accumulate = AcDamageAccumulate.Max;
        var dm = new AcCarDamage(spec, rules) { Multiplier = 1f }; dm.AddImpact(1, 40f, -1); dm.AddImpact(1, 30f, -1); dm.AddImpact(1, 50f, -1);
        AcCarDamage.Accumulate = AcDamageAccumulate.Sum;
        U(dm.Zone[1] == 50f, "MAX switch: 40, 30, 50 -> 50");
        var dh = new AcCarDamage(spec, rules) { Multiplier = 0.5f }; dh.AddImpact(2, 40f, -1);
        U(dh.Zone[2] == 40f, "zones are RAW at m 0.5 (visual damage not scaled)");

        Console.WriteLine("steer rod (GAIN * (v - MIN) * m, cap MAX_DAMAGE)");
        d = new AcCarDamage(spec, rules) { Multiplier = 1f };
        d.AddImpact(0, 40f, 0);
        U(d.RodOffset[0] == 0f, "v = MIN_VELOCITY: no rod damage");
        d.AddImpact(0, 80f, 0);
        U(Near(d.RodOffset[0], 0.0004f * 40f), "80 km/h: 0.0004 * 40 = 0.016 m");
        U(Near(d.SuspDamage01(0), 0.016f / 0.05f) && Near(d.SuspPct, 32f), "SuspDamage01 0.32, SuspPct 32");
        U((d.TakeChanges() & AcDamageChange.Suspension) != 0, "flag Suspension");
        d.AddImpact(0, 400f, 0);
        U(d.RodOffset[0] == 0.05f && d.SuspDamage01(0) == 1f, "capped at MAX_DAMAGE 0.05");
        dh = new AcCarDamage(spec, rules) { Multiplier = 0.5f }; dh.AddImpact(0, 80f, 1);
        U(Near(dh.RodOffset[1], 0.008f), "m 0.5 halves the rod: 0.008");
        var noSusp = new AcCarDamage(new AcDamageSpec(), rules) { Multiplier = 1f }; noSusp.AddImpact(0, 200f, 0);
        U(noSusp.RodOffset[0] == 0f && noSusp.SuspDamage01(0) == 0f, "no suspensions.ini [DAMAGE]: no rod damage");

        Console.WriteLine("engine life");
        d = new AcCarDamage(spec, rules) { Multiplier = 1f };
        d.StepEngine(0.003f, 9700f, 0f);
        U(Near(d.EngineLife, 1000f - 0.003f * 500f), "500 rpm over at K 1 for 3 ms: -1.5");
        d.StepEngine(0.003f, 8000f, 2.4f);
        U(Near(d.EngineLife, 1000f - 1.5f - 0.003f * 5f * 1.0f), "boost 2.4 vs 1.4 at K 5: -0.015");
        dh = new AcCarDamage(spec, rules) { Multiplier = 0.5f }; dh.StepEngine(0.003f, 9700f, 0f);
        U(Near(dh.EngineLife, 1000f - 0.75f), "m 0.5 halves the loss");
        for (int i = 0; i < 2000 && !d.EngineDead; i++) d.StepEngine(0.003f, 11200f, 0f); // 2000 rpm over: 2000/s -> ~0.5 s
        ch = d.TakeChanges();
        U(d.EngineDead && d.EngineLife == 0f && d.EnginePct == 100f && (ch & AcDamageChange.EngineDead) != 0, "money shift 2000 rpm over: dead (" + D(d.EnginePct) + " %), flag EngineDead");
        float before = d.EngineLife; d.StepEngine(0.003f, 15000f, 5f);
        U(d.EngineLife == before, "dead engine: no further steps");

        Console.WriteLine("percentages and repair seconds (MAX_DAMAGE_KMH 60, pit 20/2/30)");
        d = new AcCarDamage(spec, rules) { Multiplier = 1f };
        d.AddImpact(0, 30f, -1); d.AddImpact(2, 45f, -1); d.AddImpact(0, 85f, 0); // front 115 -> body 100 %, rod 0.018
        d.EngineLife = 900f;
        U(d.BodyPct == 100f && Near(d.SuspPct, 36f) && Near(d.EnginePct, 10f), "body 100 % (capped), susp 36 %, engine 10 %");
        U(Near(d.RepairSeconds(AcRepair.Body), 200f) && Near(d.RepairSeconds(AcRepair.Suspension), 200f + 108f) && Near(d.RepairSeconds(AcRepair.Engine), 2f) && Near(d.RepairSeconds(AcRepair.All), 310f),
            "seconds: body 200, body+susp 308, engine 2, all 310 (sum)");
        d.TakeChanges();
        d.Repair(AcRepair.Body);
        U(d.Zone[0] == 0f && d.Zone[2] == 0f && d.RodOffset[0] > 0f && d.EngineLife == 900f && d.TakeChanges() == AcDamageChange.Repaired, "Repair(Body): zones only, flag Repaired");
        d.AddImpact(3, 20f, -1); d.Repair(AcRepair.Suspension);
        U(d.Zone[3] == 0f && d.RodOffset[0] == 0f && d.EngineLife == 900f, "Repair(Suspension) also fixes the body");
        d.EngineLife = 0f; d.EngineDead = true; d.Repair(AcRepair.Engine);
        U(!d.EngineDead && d.EngineLife == AcCarDamage.LifeNew && d.Pristine, "Repair(Engine): new engine, pristine");
        d.TakeChanges(); int vv = d.Version; d.Repair(AcRepair.All);
        U(d.Version == vv && d.TakeChanges() == AcDamageChange.None, "repairing a pristine car changes nothing (Version kept, no flag)");

        Console.WriteLine("wire (protocol 3 pair)");
        d = new AcCarDamage(spec, rules) { Multiplier = 1f };
        d.AddImpact(0, 61.9f, 0); d.AddImpact(1, 12f, -1); d.AddImpact(2, 300f, 2); d.AddImpact(3, 3f, -1); d.AddImpact(4, 77f, -1); d.EngineLife = 333f; d.Retired = true;
        d.ToWire(out w0, out w1);
        var r = new AcCarDamage(spec, rules);
        U(r.ApplyWire(w0, w1), "ApplyWire on a fresh state returns true");
        int r0, r1; r.ToWire(out r0, out r1);
        U(r0 == w0 && r1 == w1, "round trip identical: " + w0.ToString("X8") + " " + w1.ToString("X8"));
        U(r.ZoneByte(0) == 61 && r.ZoneByte(2) == 255 && r.ZoneByte(4) == 77 && r.Retired && Near(r.EnginePct, 67f) && Near(r.SuspDamage01(0), (float)Math.Round(15f * d.SuspDamage01(0)) / 15f), "decoded: front 61, left 255, center 77, retired, engine 67 %, rod level / 15");
        U(!r.ApplyWire(w0, w1), "same ints again: false (no change)");
        d.EngineDead = true; d.EngineLife = 0f; d.ToWire(out w0, out w1);
        U(((w1 >> 24) & 127) == 100, "dead engine = 100 %");
        var alive = new AcCarDamage(spec, rules) { EngineLife = 0.01f }; alive.ToWire(out w0, out w1);
        U(((w1 >> 24) & 127) == 99, "living engine at 99.999 % sent as 99 (100 only when dead)");
        r.ApplyWire(0, 0);
        U(r.Pristine && !r.Retired && (r.TakeChanges() & AcDamageChange.Repaired) != 0, "0 / 0 = undamaged (Repaired flag)");

        Console.WriteLine("aero factors (formula in every aero.ini)");
        d = new AcCarDamage(spec, rules) { Multiplier = 1f }; d.AddImpact(0, 60f, -1); d.AddImpact(2, 20f, -1);
        d.AeroFactors(wing, out cl, out cd);
        U(Near(cl, 1f + 0.015f * 60f) && Near(cd, 1f + 0.015f * 60f + 0.01f * 20f), "clDiv 1.9, cdMul 2.1 (front 60, left 20)");
        d.Multiplier = 0.5f; d.AeroFactors(wing, out cl, out cd);
        U(Near(cl, 1f + 0.5f * 0.9f), "m 0.5 halves the zone effect");
        float df1, dr1, df2, dr2;
        var lw = new AcWingSpec { Chord = 1f, Span = 1f, ClGain = 1f, CdGain = 1f, AoaCl = new AcLut(new[] { -10f, 10f }, new[] { -0.5f, -0.5f }), AoaCd = new AcLut(new[] { -10f, 10f }, new[] { 0.2f, 0.2f }) };
        AcAeroModel.Wing(lw, 50f, 0f, 0.3f, 0f, 0f, out df1, out dr1);
        AcAeroModel.Wing(lw, 50f, 0f, 0.3f, 0f, 0f, 1f, 1f, out df2, out dr2);
        U(df1 == df2 && dr1 == dr2, "Wing(...) == Wing(..., 1, 1) bit for bit");
        AcAeroModel.Wing(lw, 50f, 0f, 0.3f, 0f, 0f, 2f, 1.5f, out df2, out dr2);
        U(Near(df2, df1 / 2f) && df2 < 0f && Near(dr2, dr1 * 1.5f), "negative CL keeps its sign, halved; drag x1.5");

        Console.WriteLine("toe (always toe-out, clamp 12 deg)");
        var axPos = new AcAxleSpec { SteerArmZ = 0.14f }; var axNeg = new AcAxleSpec { SteerArmZ = -0.12f }; var axShort = new AcAxleSpec { SteerArmZ = 0.028f }; var axNone = new AcAxleSpec { SteerArmZ = 0.01f };
        U(AcCarDamage.ToeDeg(axPos, 0.0003f, 0f) == axPos.ToeDegFor(0.0003f) && AcCarDamage.ToeDeg(axNeg, 0.0003f, 0f) == axNeg.ToeDegFor(0.0003f), "rod 0: exactly the iteration-1 toe (both arm signs)");
        float tp = AcCarDamage.ToeDeg(axPos, 0f, 0.02f), tn = AcCarDamage.ToeDeg(axNeg, 0f, 0.02f);
        U(tp > 0f && tn > 0f && Near(tp, (float)(Math.Atan(0.02 / 0.14) * 57.29578)), "rod 0.02: +" + D(tp) + " deg (arm +0.14), +" + D(tn) + " deg (arm -0.12): both toe-OUT");
        U(AcCarDamage.ToeDeg(axShort, 0f, 0.05f) == 12f, "short arm 0.028 m at 0.05 m: clamped to 12 (atan gives 60.8)");
        U(AcCarDamage.ToeDeg(axNone, 0.001f, 0.05f) == 0f, "arm < 2 cm: no toe change (existing guard)");

        Console.WriteLine("zone pick (Unity axes: x right, z forward)");
        U(AcCarDamage.PickZone(0f, 0.5f, 1f, false) == 0 && AcCarDamage.PickZone(0.2f, 0.3f, -0.9f, false) == 1 && AcCarDamage.PickZone(-1f, 0.5f, 0.3f, false) == 2 && AcCarDamage.PickZone(0.9f, 0.5f, -0.5f, false) == 3,
            "front / rear / left (-x) / right (+x)");
        U(AcCarDamage.PickZone(0.1f, 0.9f, 0.2f, false) == 4 && AcCarDamage.PickZone(0.1f, 0.9f, 0.8f, false) == 0 && AcCarDamage.PickZone(0.9f, 0.1f, 0.1f, true) == 4, "roof box -> CENTER, roof edge -> FRONT, upside down -> CENTER");

        Console.WriteLine("smoke reading (ENGINE_LIFE 25 = 25 % life left)");
        var sr = new AcDamageRules { SmokeEngineLife = 25f };
        var ds = new AcCarDamage(spec, sr) { Multiplier = 1f, EngineLife = 260f };
        bool s0 = ds.EngineSmoking; ds.EngineLife = 250f; bool s1 = ds.EngineSmoking;
        U(!s0 && s1 && !new AcCarDamage(spec, rules) { EngineLife = 10f }.EngineSmoking, "smoke at <= 250 / 1000, never without engine_smoke.ini");
        // fixer 2026-11-02: smoke from the quantised wire % on every client (owner 74.3 % -> wire 75 -> smoke on owner AND remote)
        var so = new AcCarDamage(spec, sr) { Multiplier = 1f, EngineLife = 257f };
        int sw0, sw1; so.ToWire(out sw0, out sw1);
        var sremote = new AcCarDamage(spec, sr); sremote.ApplyWire(sw0, sw1);
        U(so.EngineSmoking == sremote.EngineSmoking && so.EngineSmoking, "owner at 74.3 % and its remote agree on smoke (both on)");

        Console.WriteLine("wire repairs (fixer 2026-11-02: a value going down = Repaired, not new damage)");
        var wr = new AcCarDamage(spec, rules);
        wr.ApplyWire(40, 50 << 24); wr.TakeChanges();
        wr.ApplyWire(0, 50 << 24); AcDamageChange wc1 = wr.TakeChanges();
        U((wc1 & AcDamageChange.Repaired) != 0 && (wc1 & AcDamageChange.Zones) == 0, "body-only repair (engine still 50 %): Repaired, no Zones (" + wc1 + ")");
        wr.ApplyWire(30, 0); wr.TakeChanges();
        wr.ApplyWire(0, 1 << 31); AcDamageChange wc2 = wr.TakeChanges();
        U((wc2 & AcDamageChange.Repaired) != 0 && (wc2 & (AcDamageChange.Zones | AcDamageChange.Suspension)) == 0, "BACK TO PITS of a retired car (0 / 1<<31): Repaired (" + wc2 + ")");
        wr.ApplyWire(20, 1 << 31); AcDamageChange wc3 = wr.TakeChanges();
        U((wc3 & AcDamageChange.Zones) != 0 && (wc3 & AcDamageChange.Repaired) == 0, "a new hit after it: Zones only (" + wc3 + ")");

        Console.WriteLine("DMGUNIT " + (_uFail == 0 ? "PASS" : "FAIL") + ": " + _uPass + " ok, " + _uFail + " failed");
        return _uFail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ dmgsim
    static int Cmd_dmgsim(List<string> a)
    {
        AcDamageRules.Load(_install);
        string only = a.Count > 1 ? a[1] : "ferrari_458";
        bool all = only == "all";
        int cars = 0, clampViolations = 0, toeClamped = 0;
        float worstToe = 0f; string worstCar = "";
        foreach (var car in OwnedCarsFor(only))
        {
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null) continue;
            cars++;
            var core = new AcCarCore(s);
            var dmg = new AcCarDamage(s.Damage, AcDamageRules.Current) { Multiplier = 1f };
            core.Damage = dmg;
            var ch = new AcChassis(s, core, s.TotalMassKg + s.FuelLitres * 0.75f, 9.81f);
            // fleet clamp check: every corner at MAX_DAMAGE
            float carWorst = 0f;
            for (int i = 0; i < 4; i++)
            {
                AcChassisWheel w = ch.Wheels[i];
                float unclamped = Math.Abs(w.Axle.ToeDegFor(w.ToeLen)) + Math.Abs(w.Axle.ToeDegFor(s.Damage.HasSusp ? s.Damage.SuspMaxDamage : 0f));
                float t = AcCarDamage.ToeDeg(w.Axle, w.ToeLen, s.Damage.HasSusp ? s.Damage.SuspMaxDamage : 0f);
                if (Math.Abs(t) > AcCarDamage.MaxToeDeg + 1e-4f) clampViolations++;
                if (unclamped > AcCarDamage.MaxToeDeg) toeClamped++;
                if (Math.Abs(t) > carWorst) carWorst = Math.Abs(t);
            }
            if (carWorst > worstToe) { worstToe = carWorst; worstCar = car.Id; }
            // scripted impacts: front 40 (LF hub), front 60, rear 40, left 60 at LR, right 100 at RF, roof 30
            dmg.AddImpact(0, 40f, 0); dmg.AddImpact(0, 60f, -1); dmg.AddImpact(1, 40f, -1); dmg.AddImpact(2, 60f, 2); dmg.AddImpact(3, 100f, 1); dmg.AddImpact(4, 30f, -1);
            for (int i = 0; i < 4; i++) { ch.Wheels[i].RodOffset = dmg.RodOffset[i]; ch.Wheels[i].UpdateToe(); }
            var sb = new System.Text.StringBuilder();
            sb.Append(car.Id.PadRight(34)).Append(" zones F/R/L/R/C ").Append(dmg.ZoneByte(0)).Append('/').Append(dmg.ZoneByte(1)).Append('/').Append(dmg.ZoneByte(2)).Append('/').Append(dmg.ZoneByte(3)).Append('/').Append(dmg.ZoneByte(4));
            sb.Append(" | rod mm ").Append(D(dmg.RodOffset[0] * 1000f, "0.0")).Append('/').Append(D(dmg.RodOffset[1] * 1000f, "0.0")).Append('/').Append(D(dmg.RodOffset[2] * 1000f, "0.0")).Append('/').Append(D(dmg.RodOffset[3] * 1000f, "0.0"));
            sb.Append(" toe deg ").Append(D(ch.Wheels[0].ToeDeg, "0.00")).Append('/').Append(D(ch.Wheels[1].ToeDeg, "0.00")).Append('/').Append(D(ch.Wheels[2].ToeDeg, "0.00")).Append('/').Append(D(ch.Wheels[3].ToeDeg, "0.00"));
            sb.Append(" (max at MAX_DAMAGE ").Append(D(carWorst, "0.0")).Append(')');
            // aero factors at these zones
            float sumCl0 = 0f, sumCl = 0f, worstClDiv = 1f, worstCdMul = 1f;
            foreach (AcWingSpec w in s.Wings)
            {
                float cl, cd; dmg.AeroFactors(w, out cl, out cd);
                if (cl > worstClDiv) worstClDiv = cl;
                if (cd > worstCdMul) worstCdMul = cd;
                float c0 = w.AoaCl != null ? w.AoaCl.Eval(w.Angle) * w.ClGain : 0f;
                sumCl0 += c0 * w.Area; sumCl += c0 * w.Area / cl;
            }
            sb.Append(" | aero worst clDiv ").Append(D(worstClDiv, "0.00")).Append(" cdMul ").Append(D(worstCdMul, "0.00")).Append(" sum CL*A ").Append(D(sumCl0, "0.000")).Append(" -> ").Append(D(sumCl, "0.000"));
            // money shift: wheels held at the speed of 2nd gear at 95 % of the limiter, 1st forced in, clutch closed, gas 0, 1 s
            sb.Append(" | ").Append(MoneyShift(s, !all));
            sb.Append(" | pit s body/susp/eng ").Append(D(dmg.RepairSeconds(AcRepair.Body), "0")).Append('/').Append(D(dmg.RepairSeconds(AcRepair.Suspension), "0")).Append('/').Append(D(dmg.RepairSeconds(AcRepair.Engine), "0"));
            Console.WriteLine(sb.ToString());
        }
        Console.WriteLine("cars " + cars + "; toe clamp: corners needing it at MAX_DAMAGE " + toeClamped + ", violations " + clampViolations + " (worst " + D(worstToe, "0.0") + " deg on " + worstCar + ")");
        Console.WriteLine(clampViolations == 0 ? "DMGSIM PASS (toe <= 12 deg fleet-wide)" : "DMGSIM FAIL");
        return clampViolations == 0 ? 0 : 1;
    }

    /// <summary>Money-shift test on the core alone: driven wheels spun at the road speed of 2nd gear at 95 % of the limiter, 1st forced in
    /// (auto-clutch closes), gas 0, 1 s at 333 Hz with the wheel speed held (a heavy car). Returns peak rpm, life left, time to death.</summary>
    static string MoneyShift(AcCarSpec s, bool verbose)
    {
        var core = new AcCarCore(s);
        var dmg = new AcCarDamage(s.Damage, AcDamageRules.Current) { Multiplier = 1f };
        core.Damage = dmg;
        if (s.Drivetrain.Gears.Length < 2) return "money shift n/a (" + s.Drivetrain.Gears.Length + " gears)";
        float rpm2 = s.LimiterRpm * 0.95f;
        float wheelOmega = rpm2 / 9.549297f / core.TotalRatio(2);
        float r = core.Tyre[2].Radius;
        float v = wheelOmega * r;
        core.AutoShift = false; core.AutoClutch = true; core.AutoBlip = false;
        core.RequestGear(2);
        float h = 1f / 333f;
        for (int i = 0; i < 200; i++) { HoldWheels(core, s, wheelOmega, v); core.EngineOmega = rpm2 / 9.549297f; core.Gas = 0f; core.Step(h); } // engine held at the 2nd-gear rpm (the auto-clutch stays closed)
        core.RequestGear(1);
        float peak = 0f, deathT = float.NaN, t = 0f;
        for (int i = 0; i < 333; i++)
        {
            HoldWheels(core, s, wheelOmega, v); core.Gas = 0f; core.Step(h); t += h;
            if (core.Rpm > peak) peak = core.Rpm;
            if (dmg.EngineDead && float.IsNaN(deathT)) deathT = t;
        }
        return "money shift 2nd@" + D(rpm2, "0") + " -> 1st: peak " + D(peak, "0") + " rpm (threshold " + D(s.Damage.RpmThreshold, "0") + "), life " + D(dmg.EngineLife, "0") + (dmg.EngineDead ? " DEAD after " + D(deathT, "0.00") + " s" : "");
    }

    static void HoldWheels(AcCarCore core, AcCarSpec s, float omega, float v)
    {
        float load = (s.TotalMassKg + s.FuelLitres * 0.75f) * 9.81f * 0.25f;
        for (int i = 0; i < 4; i++) { core.Load[i] = load; core.VLong[i] = v; core.VLat[i] = 0f; core.Omega[i] = omega; }
        core.Speed = v;
    }

    // ------------------------------------------------------------------ dmgturbo
    static int Cmd_dmgturbo(List<string> a)
    {
        int turboCars = 0, over = 0, changed = 0;
        foreach (var car in OwnedCarsFor("all"))
        {
            AcCarSpec s = AcCarSpec.Load(car);
            if (s == null || s.Engine.Turbos.Count == 0) continue;
            turboCars++;
            AcIni eng; s.Inis.TryGetValue("engine.ini", out eng);
            float total = 0f, totalOld = 0f;
            bool adj = false;
            for (int i = 0; i < s.Engine.Turbos.Count; i++)
            {
                AcTurboSpec t = s.Engine.Turbos[i];
                AcIniSection ts = eng != null ? eng["TURBO_" + i] : null;
                float oldAdj = ts != null && ts.Has("DEFAULT_TURBO_ADJUSTMENT") ? ts.GetFloat("DEFAULT_TURBO_ADJUSTMENT", 1f) : 1f; // the iteration-1 reading
                float cap = t.MaxBoost, capOld = t.MaxBoost;
                if (t.Wastegate > 0f)
                {
                    cap = Math.Min(cap, t.Wastegate * (t.CockpitAdjustable ? Math.Max(0f, Math.Min(1f, t.DefaultAdjustment)) : 1f));
                    capOld = Math.Min(capOld, t.Wastegate * (t.CockpitAdjustable ? Math.Max(0f, Math.Min(1f, oldAdj)) : 1f));
                }
                if (t.CockpitAdjustable) adj = true;
                total += cap; totalOld += capOld;
            }
            // core check: free revving at full gas for 5 s (engine disconnected) - peak BoostTotal and life lost at multiplier 1
            var core = new AcCarCore(s);
            var dmg = new AcCarDamage(s.Damage, AcDamageRules.Current) { Multiplier = 1f };
            core.Damage = dmg; core.Disconnected = true;
            float peak = 0f;
            for (int i = 0; i < 333 * 5; i++) { core.Gas = 1f; core.Step(1f / 333f); if (core.BoostTotal > peak) peak = core.BoostTotal; }
            bool isOver = s.Damage.HasTurboDamage && total > s.Damage.TurboBoostThreshold;
            if (isOver) over++;
            if (Math.Abs(total - totalOld) > 1e-4f) changed++;
            Console.WriteLine(car.Id.PadRight(34) + " turbos " + s.Engine.Turbos.Count + (adj ? " adjustable" : "           ") + "  default total boost " + D(total, "0.000") + " (iteration 1 read " + D(totalOld, "0.000") + ")  threshold " +
                (s.Damage.HasTurboDamage ? D(s.Damage.TurboBoostThreshold, "0.00") : "-") + (isOver ? "  OVER" : "") + "  | core free-rev 5 s: peak boost " + D(peak, "0.000") + ", life lost " + D(AcCarDamage.LifeNew - dmg.EngineLife, "0.0") +
                (AcCarDamage.LifeNew - dmg.EngineLife > 0f ? " (rpm threshold " + D(s.Damage.RpmThreshold, "0") + " vs limiter " + D(s.LimiterRpm, "0") + ")" : ""));
        }
        Console.WriteLine("turbo cars " + turboCars + ", default boost changed by the P0 fix " + changed + ", above TURBO_BOOST_THRESHOLD at default " + over);
        Console.WriteLine(over == 0 ? "DMGTURBO PASS" : "DMGTURBO FAIL");
        return over == 0 ? 0 : 1;
    }
}
