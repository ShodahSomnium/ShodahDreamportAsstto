using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumAssetto;

/// <summary>
/// N's offline tests of the pure net core over the loopback transport (Fusion's rules reduced): wire round-trip precision, two / three simulated
/// clients claiming slots, records round-tripping, the master rule + hand-over, the race command Seq growing across it, seat requests, AI / race
/// blocks, leaving. Run: dotnet run -c Release -- nettest
/// </summary>
static class Program
{
    static int _fails, _oks;
    // test damage pairs (iteration 2): A F60 R12 L0 R255 / C16, rods 2 3 0 1, engine 5 %; AI 5: zones 40 30 20 10 / C50, engine 99, retired
    const int OwnD0 = unchecked((int)0xFF000C3C), OwnD1 = 0x05103210, AiD0 = 0x0A141E28, AiD1 = unchecked((int)0xE3000032);
    static double _now;

    static int Main(string[] args)
    {
        AcLog.Sink = s => { if (Verbose) Console.WriteLine("  log " + s); };
        AcNet.LogSink = s => { if (Verbose) Console.WriteLine("  log " + s); };
        string cmd = args.Length > 0 ? args[0] : "nettest";
        Verbose = Array.IndexOf(args, "-v") >= 0;
        if (cmd == "nettest")
        {
            Wire();
            DamageWire();
            Rooms();
            Offline();
            Seats();
            Timeline();
            Console.WriteLine("nettest: " + _oks + " ok, " + _fails + " FAILED");
            return _fails == 0 ? 0 : 1;
        }

        if (cmd == "nethash") return NetHash(args.Length > 1 && !args[1].StartsWith("-") ? args[1] : null);
        if (cmd == "netnames") return NetNames(args.Length > 1 && !args[1].StartsWith("-") ? args[1] : @"E:\SteamLibrary\steamapps\common\assettocorsa");
        Console.WriteLine("commands: nettest [-v] | nethash [root]");
        return 2;
    }

    /// <summary>
    /// The catalog every client resolves the wire's hashes with (AcNetWire.NameHash = FNV-1a lower-case of folder names), on the real install:
    /// collisions among car ids, track ids, skin ids within a car, layout ids within a track; AI driver names (ui_skin.json "drivername") longer
    /// than the 12 characters an AI block carries.
    /// </summary>
    static int NetHash(string root)
    {
        AcInstall inst = AcInstall.Find(root);
        if (inst == null) { Console.WriteLine("install not found"); return 2; }
        AcContent c = AcContent.Scan(inst);
        Console.WriteLine("install " + inst.Root + ": " + c.Cars.Count + " cars (" + c.OwnedCars + " owned), " + c.Tracks.Count + " tracks, " + c.TotalLayouts + " layouts");
        var cars = new Dictionary<int, string>();
        int carColl = 0, skinColl = 0, skins = 0, trackColl = 0, layoutColl = 0, layouts = 0, longNames = 0, names = 0, maxSkins = 0;
        string longest = "";
        foreach (AcCarEntry car in c.Cars)
        {
            int h = AcNetWire.NameHash(car.Id);
            string other;
            if (cars.TryGetValue(h, out other)) { carColl++; Console.WriteLine("  car hash collision: " + car.Id + " / " + other); }
            else cars.Add(h, car.Id);
            var sk = new Dictionary<int, string>();
            maxSkins = Math.Max(maxSkins, car.Skins.Count);
            foreach (AcSkinEntry s in car.Skins)
            {
                skins++;
                int hs = AcNetWire.NameHash(s.Id);
                if (sk.TryGetValue(hs, out other)) { skinColl++; Console.WriteLine("  skin hash collision in " + car.Id + ": " + s.Id + " / " + other); }
                else sk.Add(hs, s.Id);
                if (!string.IsNullOrEmpty(s.Driver)) { names++; if (s.Driver.Length > 12) longNames++; if (s.Driver.Length > longest.Length) longest = s.Driver; }
            }
        }

        var tracks = new Dictionary<int, string>();
        foreach (AcTrackEntry t in c.Tracks)
        {
            int h = AcNetWire.NameHash(t.Id);
            string other;
            if (tracks.TryGetValue(h, out other)) { trackColl++; Console.WriteLine("  track hash collision: " + t.Id + " / " + other); }
            else tracks.Add(h, t.Id);
            var ls = new Dictionary<int, string>();
            foreach (AcLayoutEntry l in t.Layouts)
            {
                layouts++;
                int hl = AcNetWire.NameHash(l.Id);
                if (ls.TryGetValue(hl, out other)) { layoutColl++; Console.WriteLine("  layout hash collision in " + t.Id + ": '" + l.Id + "' / '" + other + "'"); }
                else ls.Add(hl, l.Id);
            }
        }

        Console.WriteLine("cars: " + c.Cars.Count + " ids, " + carColl + " hash collisions; skins: " + skins + " (max " + maxSkins + " per car), " + skinColl + " collisions within a car");
        Console.WriteLine("tracks: " + c.Tracks.Count + " ids, " + trackColl + " collisions; layouts: " + layouts + ", " + layoutColl + " collisions within a track");
        Console.WriteLine("skin driver names: " + names + ", " + longNames + " longer than 12 chars (longest '" + longest + "', " + longest.Length + ") - an AI named after its skin's driver travels as \"\" (full name from the catalog)");
        return carColl + trackColl + skinColl + layoutColl == 0 ? 0 : 1;
    }

    static bool Verbose;

    /// <summary>content/career/names.txt read exactly as AcNetGame.DriverNames (= I's AcAiField): lines, longest, how many exceed the 12 wire
    /// characters, and the round trip name -> line byte -> name through an AI block for every line.</summary>
    static int NetNames(string root)
    {
        string path = System.IO.Path.Combine(root, "content", "career", "names.txt");
        if (!System.IO.File.Exists(path)) { Console.WriteLine("no " + path); return 1; }
        var names = new List<string>();
        foreach (string l in AcText.ReadFile(path).Split('\n')) { string s = l.Trim().Trim('﻿'); if (s.Length > 0) names.Add(s); }
        int longer = 0, nonLatin = 0, roundTrip = 0;
        int[] block = new int[64];
        var car = AcNetCar.Empty();
        car.Present = true;
        for (int i = 0; i < names.Count; i++)
        {
            string n = names[i];
            if (n.Length > 12) longer++;
            foreach (char ch in n) if (ch > 255) { nonLatin++; break; }
            AcNetBlocks.PackAi(block, 1, in car, 3, 80, n, i + 1);
            int line = (block[AcNetBlocks.AiHeader + AcNetBlocks.AiCarInts + 26] >> 16) & 0xFF;
            if (line == i + 1 && names[line - 1] == n) roundTrip++;
        }

        Console.WriteLine("names.txt: " + names.Count + " names, " + longer + " longer than 12 characters, " + nonLatin + " with characters outside Latin-1; line byte round trip "
                          + roundTrip + "/" + names.Count);
        for (int i = 0; i < names.Count; i++) Console.WriteLine("  " + (i + 1).ToString("00") + " " + names[i]);
        return roundTrip == names.Count ? 0 : 1;
    }

    static void Check(bool ok, string what)
    {
        if (ok) _oks++; else _fails++;
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    }

    // ------------------------------------------------------------------ wire precision

    static void Wire()
    {
        var rnd = new Random(1234);
        double worstPos = 0, worstRot = 0, worstVel = 0;
        int[] rec = new int[64];
        AcNetWire.WriteClaim(rec, 7, 1, 0);
        for (int n = 0; n < 200000; n++)
        {
            var s = AcNetState.Empty();
            s.Status = AcNetStatus.Playing;
            s.Car.Present = true;
            s.Car.Pos = new AcNetV3((float)(rnd.NextDouble() * 4000 - 2000), (float)(rnd.NextDouble() * 3100 - 50), (float)(rnd.NextDouble() * 4000 - 2000));
            s.Car.Rot = RandomQ(rnd);
            s.Car.Vel = new AcNetV3((float)(rnd.NextDouble() * 180 - 90), (float)(rnd.NextDouble() * 20 - 10), (float)(rnd.NextDouble() * 180 - 90));
            bool any;
            AcNetWire.Pack(in s, rec, out any);
            AcNetState d;
            AcNetWire.Unpack(rec, out d);
            worstPos = Math.Max(worstPos, AcNetV3.Distance(s.Car.Pos, d.Car.Pos));
            worstRot = Math.Max(worstRot, AngleDeg(s.Car.Rot, d.Car.Rot));
            worstVel = Math.Max(worstVel, AcNetV3.Distance(s.Car.Vel, d.Car.Vel));
        }

        Check(worstPos < 0.01, "pose quantisation: worst position error " + (worstPos * 1000).ToString("0.000") + " mm over 200000 random poses (+-2 km, y up to 3050 m) < 1 cm");
        Check(worstRot < 0.01, "pose quantisation: worst rotation error " + worstRot.ToString("0.000000") + " deg < 0.01 deg");
        Check(worstVel < 0.01, "velocity quantisation: worst " + (worstVel * 100).ToString("0.00") + " cm/s");

        // every field
        var a = AcNetState.Empty();
        a.Status = AcNetStatus.Playing; a.Vr = true; a.OnTrack = true; a.Seated = true; a.Driving = true; a.Master = true; a.Ready = true;
        a.Car.Present = true; a.Car.CarHash = AcNetWire.NameHash("ks_ferrari_488_gt3"); a.Car.SkinHash = AcNetWire.NameHash("00_official"); a.Car.LayoutHash = AcNetWire.NameHash("ks_vallelunga/extended_circuit");
        a.Car.Epoch = 201; a.Car.YawRateDeg = -37.5f; a.Car.SteerWheelDeg = -245.3f; a.Car.SteerLF = -12.34f; a.Car.SteerRF = -10.5f; a.Car.Rpm = 8473; a.Car.Throttle = 0.5f;
        a.Car.Brake = 1f; a.Car.Clutch = 0.25f; a.Car.Handbrake = 0f; a.Car.BoostBar = 1.2f; a.Car.Gear = -1; a.Car.Lights = 0x2B; a.Car.Limiter = true; a.Car.Tc = true;
        a.Car.W0 = new AcNetWheel { TravelM = 0.042f, Skid = 0.8f, Surface = 3 }; a.Car.W3 = new AcNetWheel { TravelM = -0.011f, Skid = 0f, Surface = -1 };
        a.Car.DoorL = 1f; a.Car.Laps = 12; a.Car.Sector = 2; a.Car.Cuts = 1; a.Car.LapMs = 61234; a.Car.LastMs = 92345; a.Car.BestMs = 91002; a.Car.RaceDistance = -0.0023f;
        a.Car.FinishMs = 0; a.Car.PenaltyMs = 5000; a.Car.Position = 3; a.Car.LapValid = false; a.Car.InPitlane = true;
        a.PassengerId = 12; a.Rider = new AcNetRider { Target = AcNetWire.TargetPlayer(9), Seat = 1, Epoch = 5, Xr = true, Offset = new AcNetV3(-0.37f, 0.2f, -0.4f), YawLocal = -3.5f };
        a.Sector0Ms = 30001; a.Sector1Ms = 31002; a.Sector2Ms = 30003; a.AppliedSeq = 77;
        bool any2;
        AcNetWire.Pack(in a, rec, out any2);
        AcNetState b;
        bool ok = AcNetWire.Unpack(rec, out b);
        ok &= b.Vr && b.OnTrack && b.Seated && b.Driving && b.Master && b.Ready && !b.Passenger && b.Car.Present && b.Car.CarHash == a.Car.CarHash && b.Car.SkinHash == a.Car.SkinHash
              && b.Car.LayoutHash == a.Car.LayoutHash && b.Car.Epoch == 201 && Math.Abs(b.Car.YawRateDeg + 37.5f) < 0.06f && Math.Abs(b.Car.SteerWheelDeg + 245.3f) < 0.06f
              && Math.Abs(b.Car.SteerLF + 12.34f) < 0.006f && Math.Abs(b.Car.Rpm - 8473) < 0.6f && Math.Abs(b.Car.Throttle - 0.5f) < 0.003f && b.Car.Brake == 1f && b.Car.Gear == -1
              && b.Car.Lights == 0x2B && b.Car.Limiter && b.Car.Tc && !b.Car.Abs && Math.Abs(b.Car.BoostBar - 1.2f) < 0.02f && Math.Abs(b.Car.W0.TravelM - 0.042f) < 0.0011f
              && b.Car.W0.Surface == 3 && b.Car.W3.Surface == -1 && b.Car.W1.Surface == -1 && Math.Abs(b.Car.W0.Skid - 0.8f) < 0.003f && b.Car.DoorL == 1f && b.Car.Laps == 12 && b.Car.Sector == 2
              && b.Car.Cuts == 1 && b.Car.LapMs == 61234 && b.Car.LastMs == 92345 && b.Car.BestMs == 91002 && Math.Abs(b.Car.RaceDistance + 0.0023f) < 0.0001f && b.Car.PenaltyMs == 5000
              && b.Car.Position == 3 && !b.Car.LapValid && b.Car.InPitlane && !b.Car.Finished && b.PassengerId == 12 && b.Rider.Target == AcNetWire.TargetPlayer(9) && b.Rider.Seat == 1
              && b.Rider.Epoch == 5 && b.Rider.Xr && !b.Rider.Requesting && Math.Abs(b.Rider.Offset.X + 0.37f) < 0.0006f && Math.Abs(b.Rider.YawLocal + 3.5f) < 0.01f
              && b.Sector2Ms == 30003 && b.AppliedSeq == 77;
        Check(ok, "every player-record field round-trips (flags, identity, controls, wheels, doors, timing, seat table, rider, sectors, applied seq)");

        // ints 39-41: wheel angular velocity (lock-up / wheelspin) and fuel; a change of wheel speed alone is motion (new timeline sample)
        a.Car.HasWheelSpeed = true; a.Car.FuelLitres = 63.47f;
        a.Car.W0.AngularVel = 0f; a.Car.W1.AngularVel = 0.04f; a.Car.W2.AngularVel = 312.36f; a.Car.W3.AngularVel = -45.5f;
        bool motionWs = AcNetWire.Pack(in a, rec, out any2);
        AcNetWire.Unpack(rec, out b);
        bool wsOk = b.Car.HasWheelSpeed && Math.Abs(b.Car.FuelLitres - 63.5f) < 0.051f && b.Car.W0.AngularVel == 0f && Math.Abs(b.Car.W2.AngularVel - 312.4f) < 0.051f
                    && Math.Abs(b.Car.W3.AngularVel + 45.5f) < 0.051f && Math.Abs(b.Car.W1.AngularVel) < 0.051f;
        a.Car.W2.AngularVel = 0f;
        bool motionLock = AcNetWire.Pack(in a, rec, out any2);
        a.Car.FuelLitres = 60f;
        bool motionFuel = AcNetWire.Pack(in a, rec, out any2);
        Check(wsOk && motionWs && motionLock && !motionFuel && any2, "wheel angular velocity x4 (0.1 rad/s) + fuel (0.1 L) round-trip; a lock-up alone is motion, a fuel change is not");
        var ai0 = AcNetState.Empty();
        AcNetWire.Pack(in ai0, rec, out any2);
        AcNetWire.Unpack(rec, out b);
        Check(!b.Car.HasWheelSpeed && b.Car.W2.AngularVel == 0f, "a record without wheel speeds says so (readers derive spin from the speed)");
        AcNetWire.Pack(in a, rec, out any2);
        Check(AcNetWire.OwnerOf(rec) == 7 && AcNetWire.GenerationOf(rec) == 1 && AcNetWire.StatusOf(rec) == AcNetStatus.Playing, "header: owner 7, generation 1, status Playing");

        // protocol: another build's record keeps its header (slot taken, master bit) but is not decoded; blocks of another protocol are ignored
        var old = (int[])rec.Clone();
        old[AcNetWire.AtOwner] = (old[AcNetWire.AtOwner] & 0x00FFFFFF) | (1 << 24);
        old[AcNetWire.AtFlags] |= 1 << 7;
        AcNetState ob;
        bool oldDecoded = AcNetWire.Unpack(old, out ob);
        Check(AcNetWire.Protocol == 3 && AcNetWire.ProtocolOf(rec) == 3 && !AcNetWire.Foreign(rec) && AcNetWire.Unpack(rec, out ob),
              "own protocol " + AcNetWire.Protocol + " is written by the claim and decoded");
        Check(!oldDecoded && AcNetWire.ProtocolOf(old) == 1 && AcNetWire.Foreign(old) && AcNetWire.OwnerOf(old) == 7 && AcNetWire.StatusOf(old) == AcNetStatus.Playing
              && AcNetWire.MasterBitOf(old), "a protocol-1 record is refused (not decoded) but its header still says owner 7, Playing, master bit");
        var aiBlk = new int[AcNetSlots.Ints];
        AcNetBlocks.WriteAiHeader(aiBlk, 3, 4, 1000);
        bool aiOk = AcNetBlocks.AiHeaderOk(aiBlk, 3);
        aiBlk[3] = (aiBlk[3] & 0x00FFFFFF) | (1 << 24);
        var wBlk = new int[AcNetSlots.Ints];
        AcNetBlocks.WriteHeader(wBlk, 0, AcNetBlocks.KindWorld, 4, 1000, 0, 1, 0, true);
        bool wOk = AcNetBlocks.HeaderOk(wBlk, 0, AcNetBlocks.KindWorld);
        wBlk[3] = (wBlk[3] & 0x00FFFFFF) | (1 << 24);
        Check(aiOk && wOk && !AcNetBlocks.AiHeaderOk(aiBlk, 3) && !AcNetBlocks.HeaderOk(wBlk, 0, AcNetBlocks.KindWorld) && AcNetBlocks.Protocol == AcNetWire.Protocol,
              "AI / world blocks of protocol 1 are ignored, protocol " + AcNetWire.Protocol + " blocks are read");
        var tr = AcNetState.Empty();
        tr.Status = AcNetStatus.Playing;
        tr.Car.Present = true;
        tr.Car.W0 = new AcNetWheel { TravelM = 0.2f, Surface = -1 }; tr.Car.W1 = new AcNetWheel { TravelM = -0.09f, Surface = -1 };
        var trRec = new int[AcNetSlots.Ints];
        AcNetWire.WriteClaim(trRec, 3, 1, 0);
        AcNetWire.Pack(in tr, trRec, out any2);
        AcNetState trb;
        AcNetWire.Unpack(trRec, out trb);
        Check(Math.Abs(trb.Car.W0.TravelM - 0.2f) < 0.0011f && Math.Abs(trb.Car.W1.TravelM + 0.09f) < 0.0011f, "travel +0.200 / -0.090 m round-trips in 2 mm units (protocol 2; 1 mm units clipped at 0.127 m)");

        var c = new AcNetCommand { Phase = 4, TrackHash = AcNetWire.NameHash("monza"), LayoutHash = 0, Session = 3, Laps = 7, Minutes = 0, Abs = 2, Tc = 0, Stability = true,
                                   AutoClutch = true, AutoShift = false, TyresOut = -1, JumpStart = 2, AiCount = 5, AiLevel = 97, PitLimitKmh = 80f, MasterId = 4 };
        c.StartUtc = 1793448123.456;
        c.PhaseUtc = 1793448100.789;
        for (int i = 0; i < 60; i++) c.Allowed.Add(AcNetWire.NameHash("car" + i));
        c.Grid.Add(4); c.Grid.Add(9); c.Grid.Add(-100); c.Grid.Add(-117); c.Grid.Add(2);
        c.Seq = 33;
        int[] w0 = new int[64], w1 = new int[64];
        AcNetBlocks.PackCommand(c, w0, w1);
        AcNetBlocks.WriteHeader(w0, 0, AcNetBlocks.KindWorld, 4, 0, c);
        AcNetBlocks.WriteHeader(w1, 1, AcNetBlocks.KindWorld, 4, 0, c);
        var c2 = new AcNetCommand();
        bool cok = AcNetBlocks.UnpackCommand(w0, w1, c2);
        Check(cok && c2.SameContent(c) && c2.Seq == 33 && c2.MasterId == 4 && Math.Abs(c2.StartUtc - c.StartUtc) < 0.0011 && Math.Abs(c2.PhaseUtc - c.PhaseUtc) < 0.0011,
              "race command round-trips (60 allowed cars over World 0+1, grid with AI ids, start + phase UTC to the ms, tyres out -1)");

        var res = new List<AcNetResult>();
        for (int i = 0; i < 40; i++) res.Add(new AcNetResult { Id = i < 30 ? i + 1 : -100 - (i - 30), Position = i + 1, Finished = i < 35, TotalMs = 600000 + i * 1234 });
        int[] w2 = new int[64], w3 = new int[64];
        AcNetBlocks.PackResults(res, res.Count, w2, w3);
        var back = new AcNetResult[56];
        int nres = AcNetBlocks.UnpackResults(w2, w3, true, back);
        Check(nres == 40 && back[39].Id == -109 && back[39].TotalMs == 600000 + 39 * 1234 && back[0].Id == 1 && back[34].Finished && !back[35].Finished, "results round-trip (40 lines, AI ids)");
    }

    /// <summary>The angle between two rotations from the relative quaternion in double (acos near 1 loses ~0.05 deg to float inputs).</summary>
    static double AngleDeg(AcNetQ a, AcNetQ b)
    {
        double ax = -a.X, ay = -a.Y, az = -a.Z, aw = a.W;     // conj(a)
        double rx = aw * b.X + ax * b.W + ay * b.Z - az * b.Y;
        double ry = aw * b.Y - ax * b.Z + ay * b.W + az * b.X;
        double rz = aw * b.Z + ax * b.Y - ay * b.X + az * b.W;
        double rw = aw * b.W - ax * b.X - ay * b.Y - az * b.Z;
        double v = Math.Sqrt(rx * rx + ry * ry + rz * rz);
        return 2.0 * Math.Atan2(v, Math.Abs(rw)) * 180.0 / Math.PI;
    }

    static AcNetQ RandomQ(Random r)
    {
        double u1 = r.NextDouble(), u2 = r.NextDouble(), u3 = r.NextDouble();
        double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
        return new AcNetQ((float)(a * Math.Sin(2 * Math.PI * u2)), (float)(a * Math.Cos(2 * Math.PI * u2)), (float)(b * Math.Sin(2 * Math.PI * u3)), (float)(b * Math.Cos(2 * Math.PI * u3)));
    }

    // ------------------------------------------------------------------ simulated rooms

    sealed class Client
    {
        public int Id;
        public AcNet Net;
        public AcLoopbackTransport T;
        public float Z;
        public void Tick() { Net.Tick(); }
    }

    static Client Join(AcLoopbackWorld w, int id)
    {
        var t = w.Join(id, () => _now);
        var c = new Client { Id = id, T = t, Net = new AcNet(t, () => _now, " #" + id) };
        c.Net.Own.Status = AcNetStatus.Present;
        return c;
    }

    static void Run(List<Client> cs, double seconds, Action perFrame = null)
    {
        int frames = (int)(seconds * 60);
        for (int f = 0; f < frames; f++)
        {
            _now += 1.0 / 60.0;
            if (perFrame != null) perFrame();
            for (int i = 0; i < cs.Count; i++) cs[i].Tick();
        }
    }
    // ------------------------------------------------------------------ damage (iteration 2, protocol 3; DAMAGE_DESIGN 7)

    static AcDamageSpec TestSpec()
    {
        // test values only (no game data): a car with rod damage keys and a pit stop
        return new AcDamageSpec { HasSusp = true, SuspMinVelocity = 20f, SuspGain = 0.0005f, SuspMaxDamage = 0.03f, HasRpmDamage = true, RpmThreshold = 8000f,
                                  RpmDamageK = 1f, HasTurboDamage = false, PitBodySecPer10 = 20f, PitSuspSecPer10 = 30f, PitEngineSecPer10 = 2f, HasPitStop = true };
    }

    static void DamageWire()
    {
        // 1. the player record carries the pair in ints 42-43; a damage change alone is not motion (no seq / stamp bump)
        var rec = new int[AcNetSlots.Ints];
        AcNetWire.WriteClaim(rec, 7, 1, 0);
        var a = AcNetState.Empty();
        a.Status = AcNetStatus.Playing; a.Car.Present = true;
        bool any;
        AcNetWire.Pack(in a, rec, out any);
        Check(rec[AcNetWire.AtDamage] == 0 && rec[AcNetWire.AtDamage + 1] == 0 && AcNetWire.AtDamage == 42 && AcNetWire.Used == 44 && AcNetWire.Used <= AcNetSlots.Ints,
              "zero record = undamaged: ints 42-43 are 0 (AtDamage 42, Used 44)");
        a.Car.DamageD0 = unchecked((int)0xFF3C0A01); a.Car.DamageD1 = unchecked((int)0x80FFF21E);
        bool motion = AcNetWire.Pack(in a, rec, out any);
        AcNetState b;
        bool dec = AcNetWire.Unpack(rec, out b);
        Check(dec && !motion && any && b.Car.DamageD0 == a.Car.DamageD0 && b.Car.DamageD1 == a.Car.DamageD1 && rec[42] == a.Car.DamageD0 && rec[43] == a.Car.DamageD1,
              "damage pair round-trips bit-exact in the player record (incl. bit 31 = retired); it is not motion");

        // 2. AcCarDamage -> wire -> a fresh remote state -> wire: identical bytes, same quantised values
        var spec = TestSpec();
        var rules = new AcDamageRules();
        var own = new AcCarDamage(spec, rules) { Multiplier = 1f };
        int z0, z1;
        own.ToWire(out z0, out z1);
        Check(z0 == 0 && z1 == 0, "a new car writes 0 / 0");
        var off = new AcCarDamage(spec, rules) { Multiplier = 0f };
        off.AddImpact(0, 80f, 1);
        off.StepEngine(1f, 12000f, 0f);
        int o0, o1;
        off.ToWire(out o0, out o1);
        Check(o0 == 0 && o1 == 0 && off.Pristine, "DAMAGE 0: an 80 km/h hit + over-rev change nothing, the wire stays 0 / 0");
        own.AddImpact((int)AcDamageZone.Front, 61.7f, 0);
        own.AddImpact((int)AcDamageZone.Rear, 300f, -1);       // capped at 255
        own.AddImpact((int)AcDamageZone.Left, 12.2f, 2);
        own.AddImpact((int)AcDamageZone.Right, 4.9f, -1);
        own.AddImpact((int)AcDamageZone.Center, 33.3f, -1);
        own.StepEngine(0.05f, 9000f, 0f);                      // 50 life lost (test K 1) -> 5 %
        own.Retired = true;
        int d0, d1;
        own.ToWire(out d0, out d1);
        AcNetState w = AcNetState.Empty();
        w.Status = AcNetStatus.Playing; w.Car.Present = true; w.Car.DamageD0 = d0; w.Car.DamageD1 = d1;
        AcNetWire.Pack(in w, rec, out any);
        AcNetWire.Unpack(rec, out b);
        var remote = new AcCarDamage(spec, rules);              // a remote client's copy (multiplier 0: ApplyWire ignores it)
        bool changed = remote.ApplyWire(b.Car.DamageD0, b.Car.DamageD1);
        int r0, r1;
        remote.ToWire(out r0, out r1);
        bool same = true;
        for (int z = 0; z < 5; z++) same &= remote.ZoneByte(z) == own.ZoneByte(z);
        for (int c = 0; c < 4; c++) same &= Math.Abs(remote.SuspDamage01(c) - own.SuspDamage01(c)) <= 1f / 30f + 1e-5f;
        Check(changed && r0 == d0 && r1 == d1 && same && remote.ZoneByte(1) == 255 && remote.ZoneByte(0) == 61 && remote.Retired && !remote.EngineDead
              && Math.Abs(remote.EnginePct - own.EnginePct) < 1.0001f,
              "AcCarDamage round trip: ToWire -> record -> ApplyWire -> ToWire identical (d0 " + d0.ToString("x8") + ", d1 " + d1.ToString("x8") + "), zones F 61 / R 255 (cap), rods within 1/15, engine %, retired");
        Check(!remote.ApplyWire(b.Car.DamageD0, b.Car.DamageD1), "the same pair again changes nothing (ApplyWire false: no CarDamaged)");
        own.StepEngine(10f, 12000f, 0f);
        own.ToWire(out d0, out d1);
        remote.ApplyWire(d0, d1);
        Check(own.EngineDead && ((d1 >> 24) & 127) == 100 && remote.EngineDead, "a blown engine travels as 100 % (dead on the remote)");
        own.Repair(AcRepair.All); own.Retired = false;
        own.ToWire(out d0, out d1);
        AcDamageChange ch0 = remote.TakeChanges();
        bool back = remote.ApplyWire(d0, d1);
        AcDamageChange ch = remote.TakeChanges();
        Check(d0 == 0 && d1 == 0 && back && remote.Pristine && (ch & AcDamageChange.Repaired) != 0, "repair: the pair goes back to 0 / 0 and the remote reads 'Repaired' (pristine)");

        // 3. the AI damage table ("Ac Net Host 0"): 18 pairs by AI index after a 4-int header, 40 ints
        var tbl = new int[AcNetSlots.Ints];
        bool all = AcNetBlocks.AiDamageInts == 40 && AcNetBlocks.MaxAi == 18;
        for (int i = 0; i < AcNetBlocks.MaxAi; i++) all &= AcNetBlocks.PackAiDamage(tbl, i, 0x01010101 * (i + 1), unchecked((int)0x80000000) | (i * 7));
        AcNetBlocks.WriteAiDamageHeader(tbl, 0, 4, 1234);
        for (int i = 0; i < AcNetBlocks.MaxAi; i++)
        {
            int u0, u1;
            AcNetBlocks.UnpackAiDamage(tbl, i, out u0, out u1);
            all &= u0 == 0x01010101 * (i + 1) && u1 == (unchecked((int)0x80000000) | (i * 7));
        }
        all &= !AcNetBlocks.PackAiDamage(tbl, 3, 0x04040404, unchecked((int)0x80000000) | 21);   // unchanged -> false
        Check(all && AcNetBlocks.AiDamageHeaderOk(tbl, 0) && AcNetBlocks.AiDamageWriterOf(tbl) == 4 && tbl[0] == AcNetBlocks.MagicAiDamage && tbl[0] == 0x41434431,
              "AI damage table: 18 pairs round-trip by index, 'ACD1' header, writer 4; an unchanged pair is not dirty");
        int x0, x1;
        AcNetBlocks.UnpackAiDamage(tbl, 18, out x0, out x1);
        Check(x0 == 0 && x1 == 0 && !AcNetBlocks.PackAiDamage(tbl, -1, 1, 1), "AI damage table: out-of-range indices read 0 and are not written");
        tbl[3] = (tbl[3] & 0x00FFFFFF) | (2 << 24);
        Check(!AcNetBlocks.AiDamageHeaderOk(tbl, 0), "an AI damage table of protocol 2 is ignored");

        // 4. protocol refusal: a protocol-2 record (iteration-1 build) is Foreign - header kept, nothing decoded
        var p2 = new int[AcNetSlots.Ints];
        AcNetWire.WriteClaim(p2, 7, 1, 0);
        AcNetWire.Pack(in a, p2, out any);
        p2[AcNetWire.AtOwner] = (p2[AcNetWire.AtOwner] & 0x00FFFFFF) | (2 << 24);
        AcNetState p2s;
        Check(!AcNetWire.Unpack(p2, out p2s) && AcNetWire.Foreign(p2) && AcNetWire.ProtocolOf(p2) == 2 && AcNetWire.OwnerOf(p2) == 7 && AcNetWire.StatusOf(p2) == AcNetStatus.Playing,
              "a protocol-2 record (iteration-1 build) is refused as Foreign: not decoded, header (owner 7, Playing) still counts");
    }


    static void Rooms()
    {
        _now = 100.0;
        var w = new AcLoopbackWorld();
        w.JoinVisitor(0);           // a visitor (not whitelisted) is the room's oldest client = Fusion's shared-mode master
        var A = Join(w, 4);
        var B = Join(w, 9);
        var cs = new List<Client> { A, B };
        Run(cs, 4.0);
        Check(A.Net.Claimed && B.Net.Claimed && A.Net.MySlot != B.Net.MySlot, "two clients claim different slots (A slot " + A.Net.MySlot + ", B slot " + B.Net.MySlot + ") - first try = id % 50");
        Check(A.Net.MySlot == 4 && B.Net.MySlot == 9, "first-try slots are localId % 50");
        Check(A.Net.HasRecord(9) && B.Net.HasRecord(4) && !A.Net.HasRecord(0) && !B.Net.HasRecord(0), "HasRecord: the Dreamport players see each other, never the visitor (isolation input)");
        Check(A.Net.MasterId == 4 && B.Net.MasterId == 4 && A.Net.MasterReason == "lowest id", "master with a visitor as the room's master: lowest Dreamport id (4) on both (" + B.Net.MasterReason + ")");

        // A drives: B's view of A's car
        double t0 = _now;
        Run(cs, 3.0, () =>
        {
            double t = _now - t0;
            A.Net.Own.Status = AcNetStatus.Playing;
            A.Net.Own.Driving = true;
            A.Net.Own.Car.Present = true;
            A.Net.Own.Car.Pos = new AcNetV3(0f, 0.5f, (float)(t * 70.0));
            A.Net.Own.Car.Vel = new AcNetV3(0f, 0f, 70f);
            A.Net.Own.Car.Rot = AcNetQ.Identity;
            A.Net.Own.Car.Rpm = 7000;
            A.Net.OwnStampMs = (int)(_now * 1000.0);
        });
        AcNet.Remote ra = B.Net.RemoteOfPlayer(4);
        AcNetCar drawn;
        bool sampled = ra != null && ra.Timeline.Sample(_now, out drawn);
        ra.Timeline.Sample(_now, out drawn);
        float truth = (float)((_now - t0) * 70.0);
        float lag = (truth - drawn.Pos.Z) / 70f;
        Check(sampled && ra.State.Car.Rpm == 7000 && ra.State.Driving, "B reads A's driving record (rpm 7000, driving)");
        Check(lag > 0.0f && lag < 0.2f, "B draws A's car " + (lag * 1000).ToString("0") + " ms behind the truth at 70 m/s (timeline delay " + (ra.Timeline.Delay * 1000).ToString("0") + " ms; loopback sends at 16 Hz)");
        long written = A.Net.IntsWrittenTotal;
        Run(cs, 2.0, () => { A.Net.Own.Driving = false; A.Net.Own.Car.Vel = default(AcNetV3); });
        long parkedStart = A.Net.IntsWrittenTotal;
        Run(cs, 5.0);
        long parked = A.Net.IntsWrittenTotal - parkedStart;
        Check(parked <= 15, "a parked car costs almost nothing: " + parked + " ints written in 5 s (keep-alive seq + stamp only)");
        // iteration 2: A's damage pair (ints 42-43) reaches B while parked; it costs only the two ints (not motion: no seq / stamp bump)
        long dmgStart = A.Net.IntsWrittenTotal;
        Run(cs, 1.0, () => { A.Net.Own.Car.DamageD0 = OwnD0; A.Net.Own.Car.DamageD1 = OwnD1; });
        ra = B.Net.RemoteOfPlayer(4);
        Check(ra != null && ra.State.Car.DamageD0 == OwnD0 && ra.State.Car.DamageD1 == OwnD1, "B reads A's damage pair within 1 s (d0 " + OwnD0.ToString("x8") + ", d1 " + OwnD1.ToString("x8") + ")");
        Check(A.Net.IntsWrittenTotal - dmgStart <= 6, "a damage change costs " + (A.Net.IntsWrittenTotal - dmgStart) + " ints (the pair + keep-alive)");

        // race command
        var cmd = new AcNetCommand { Phase = 2, TrackHash = AcNetWire.NameHash("imola"), Session = 1, Laps = 5, Minutes = 10 };
        int seq1 = A.Net.PublishCommand(cmd);
        bool gotB = false;
        Run(cs, 1.5, () => { });
        AcNetCommand bc;
        gotB = B.Net.TryGetCommand(out bc) && bc.Seq == seq1 && bc.TrackHash == cmd.TrackHash;
        Check(seq1 == 1 && gotB, "master A publishes race command 1; B reads it (track imola)");
        Check(B.Net.PublishCommand(cmd) == 0, "B (not master) may not publish");

        // seat request: B asks for A's passenger seat
        B.Net.BeginSeatRequest(4, 1);
        AcNet.SeatAnswer ans = AcNet.SeatAnswer.Waiting;
        double asked = _now;
        Run(cs, 2.0, () => { var x = B.Net.TakeSeatAnswer(); if (x != AcNet.SeatAnswer.Waiting && x != AcNet.SeatAnswer.None) { ans = x; asked = _now - asked; } });
        Check(ans == AcNet.SeatAnswer.Granted && A.Net.SeatOccupant == 9, "B asks A for the passenger seat: " + ans + " in " + (asked * 1000).ToString("0") + " ms; A's seat table names B");
        // B's rider record keeps naming the seat (as AcNetGame does once seated)
        B.Net.Own.Rider = new AcNetRider { Target = AcNetWire.TargetPlayer(4), Seat = 1, Offset = new AcNetV3(-0.4f, 0.3f, -0.2f) };
        var C = Join(w, 13);
        cs.Add(C);
        Run(cs, 4.0);
        C.Net.BeginSeatRequest(4, 1);
        AcNet.SeatAnswer ansC = AcNet.SeatAnswer.Waiting;
        Run(cs, 2.0, () => { var x = C.Net.TakeSeatAnswer(); if (x != AcNet.SeatAnswer.Waiting && x != AcNet.SeatAnswer.None) ansC = x; });
        Check(ansC == AcNet.SeatAnswer.Taken, "late joiner C asks for the same seat: " + ansC);
        AcNetCommand cc;
        Check(C.Net.TryGetCommand(out cc) && cc.Seq == 1, "late joiner C reads the running command (seq " + (cc != null ? cc.Seq : 0) + ")");
        AcNet.Remote rcA = C.Net.RemoteOfPlayer(4);
        Check(rcA != null && rcA.State.Car.DamageD0 == OwnD0 && rcA.State.Car.DamageD1 == OwnD1, "late joiner C decodes A's current damage pair on its first read (no extra message)");
        // B leaves the seat: A frees it after 2 s
        B.Net.Own.Rider = default(AcNetRider);
        Run(cs, 3.0);
        Check(A.Net.SeatOccupant < 0, "the passenger's record stops naming the seat: A frees it within " + AcNet.SeatStaleAfter + " s");

        // AI + race blocks
        var ai = AcNetCar.Empty();
        ai.Pos = new AcNetV3(10f, 0.4f, -50f); ai.Rot = AcNetQ.Identity; ai.CarHash = AcNetWire.NameHash("abarth500"); ai.Laps = 2; ai.RaceDistance = 2.5f;
        ai.DamageD0 = AiD0; ai.DamageD1 = AiD1;
        A.Net.SetAi(5, in ai, 95, "Marco Rossi");
        ai.DamageD0 = 0; ai.DamageD1 = 0;                       // AI 6 undamaged
        A.Net.SetAi(6, in ai, 90, "Florentin Schröter", 22);    // a names.txt name longer than 12 characters: its line travels too
        A.Net.PublishRaceState(new AcNetRaceState { LightsUtc = 1793448100.25, Reds = 5, StepMs = 1000 });
        A.Net.SetPenalty(9, 5);
        A.Net.SetPenalty(50 + 5, AcNetBlocks.DriveThrough);
        Run(cs, 1.0);
        AcNet.AiCar bai = B.Net.Ai(5);
        AcNetRaceState rs;
        Check(bai != null && bai.Present && bai.Name == "Marco Rossi" && bai.Level == 95 && Math.Abs(bai.Car.Pos.Z + 50f) < 0.001f && bai.Car.Laps == 2 && bai.NameLine == 0, "B reads the master's AI car 5 (name, level, pose, laps, no names.txt line)");
        AcNet.AiCar bai6 = B.Net.Ai(6);
        Check(bai6 != null && bai6.Present && bai6.NameLine == 22 && bai6.Name == "Florentin Sc" && bai6.Level == 90, "AI car 6: names.txt line 22 travels with the 12-character text ('" + (bai6 != null ? bai6.Name : "-") + "')");
        Check(B.Net.TryGetRaceState(out rs) && rs.Reds == 5 && rs.StepMs == 1000 && Math.Abs(rs.LightsUtc - 1793448100.25) < 0.0011, "B reads the master's start-light sequence");
        Check(bai != null && bai.Car.DamageD0 == AiD0 && bai.Car.DamageD1 == AiD1 && bai6 != null && bai6.Car.DamageD0 == 0 && bai6.Car.DamageD1 == 0,
              "AI damage: B reads AI 5's pair from the master's 'Ac Net Host 0' table (retired bit too); AI 6 undamaged = 0 / 0");
        Check(C.Net.Ai(5) != null && C.Net.Ai(5).Car.DamageD0 == AiD0, "AI damage: C reads it too");
        Check(C.Net.Penalty(9) == 5 && C.Net.Penalty(55) == AcNetBlocks.DriveThrough, "C reads the penalties (slot 9: 5 s, AI 5: drive-through)");

        // hand-over: A (master) leaves
        int aSlot = A.Net.MySlot;
        A.Net.Dispose();
        cs.Remove(A);
        Run(cs, 3.0);
        Check(B.Net.MasterId == 9 && C.Net.MasterId == 9, "A left: B (lowest Dreamport id 9) is the new master on B and C (" + B.Net.MasterReason + ")");
        Check(!B.Net.HasRecord(4) && !C.Net.HasRecord(4), "A's record no longer counts on B and C");
        AcNetCommand bc2;
        Check(B.Net.TryGetCommand(out bc2) && bc2.Seq == 1, "the new master keeps the newest command (seq 1)");
        Run(cs, 1.5);
        AcNet.AiCar cai = C.Net.Ai(5);
        Check(cai != null && cai.Present && cai.Name == "Marco Rossi", "the new master keeps writing the AI car it read (C still sees it)");
        Check(cai != null && cai.Car.DamageD0 == AiD0 && cai.Car.DamageD1 == AiD1, "hand-over: the new master keeps the AI damage table (Host 0) - C still reads AI 5's damage pair");
        int i0, i1, j0, j1;
        bool inh = B.Net.TakeInheritedAiDamage(5, out i0, out i1);
        bool inh2 = B.Net.TakeInheritedAiDamage(5, out j0, out j1);
        bool inh6 = B.Net.TakeInheritedAiDamage(6, out j0, out j1);
        Check(inh && i0 == AiD0 && i1 == AiD1 && !inh2 && !inh6, "hand-over: the new master inherits AI 5's pair once (to seed its own rig; AI 6 undamaged: nothing)");
        var cmd2 = new AcNetCommand { Phase = 3, TrackHash = AcNetWire.NameHash("imola"), Session = 3, Laps = 3 };
        int seq2 = B.Net.PublishCommand(cmd2);
        Run(cs, 1.5);
        AcNetCommand cc2;
        Check(seq2 == 2 && C.Net.TryGetCommand(out cc2) && cc2.Seq == 2 && cc2.Phase == 3 && C.Net.NewCommand == false, "the new master's next command has seq 2 (grew across the hand-over); C applies it");
        int[] freed = w.LiveOf(aSlot);
        Check(AcNetWire.OwnerOf(freed) < 0 && freed[0] == AcNetWire.Magic, "A's slot " + aSlot + " written free (A's Stop, or housekeeping by the orphan's new holder)");

        // a fourth client joins: B stays master unless the room's master bit says otherwise
        var D = Join(w, 2);   // lower id than B, but joined later
        cs.Add(D);
        Run(cs, 4.0);
        Check(D.Net.Claimed && B.Net.MasterId == 2 && D.Net.MasterId == 2, "D joins with a LOWER id (2): the lowest-id rule names D on every client (B " + B.Net.MasterId + ", D " + D.Net.MasterId + ")");
        AcNetCommand dc;
        Check(D.Net.TryGetCommand(out dc) && dc.Seq == 2, "D (now master) took over command 2 from the blocks");
        int seq3 = D.Net.PublishCommand(cmd);
        Check(seq3 == 3, "D's next command is seq 3");

        // the room's master bit wins once a Dreamport player holds it
        w.MasterOverride = 13;
        Run(cs, 2.0);
        Check(B.Net.MasterId == 13 && C.Net.MasterId == 13 && D.Net.MasterId == 13 && C.Net.MasterReason == "me" && B.Net.MasterReason == "record bit",
              "the room's shared-mode master is C (13): C 'me', others by C's record bit");
        foreach (var c in cs) c.Net.Dispose();
    }

    // ------------------------------------------------------------------ a command published offline

    static void Offline()
    {
        _now = 500.0;
        var w = new AcLoopbackWorld();
        var M = Join(w, 5);
        var cs = new List<Client> { M };
        Run(cs, 3.0);
        var monza = new AcNetCommand { Phase = 2, TrackHash = AcNetWire.NameHash("monza"), Session = 1 };
        int mSeq = M.Net.PublishCommand(monza);
        Run(cs, 1.0);

        // X starts offline (editor / the first second / a reconnect), publishes its own command (seq 1 too), then joins M's room
        var X = Join(w, 7);
        X.T.Connected = false;
        X.Net.Tick();
        var imola = new AcNetCommand { Phase = 2, TrackHash = AcNetWire.NameHash("imola"), Session = 1 };
        int xSeq = X.Net.PublishCommand(imola);
        Check(mSeq == 1 && xSeq == 1, "offline X publishes its own command with the same seq 1 as the room's master (" + mSeq + " / " + xSeq + ")");
        X.T.Connected = true;
        cs.Add(X);
        bool sawNew = false;
        Run(cs, 4.0, () => { if (X.Net.NewCommand) sawNew = true; });
        AcNetCommand xc;
        Check(X.Net.TryGetCommand(out xc) && xc.TrackHash == monza.TrackHash && xc.Seq == 1 && xc.MasterId == 5 && sawNew && X.Net.MasterId == 5,
              "after joining, X's offline command is parked and the master's seq 1 (monza) arrives as a NEW command (" + (xc != null ? xc.TrackHash.ToString("x8") : "-") + ")");
        Check(!X.Net.Republished && M.Net.TryGetCommand(out xc) && xc.TrackHash == monza.TrackHash, "X never re-publishes it (not the master); M's command stays");
        foreach (var c in cs) c.Net.Dispose();

        // Y alone offline, then the first in an empty room: its offline command goes into the room under a room Seq, a joiner reads it
        var w2 = new AcLoopbackWorld();
        var Y = Join(w2, 3);
        Y.T.Connected = false;
        Y.Net.Tick();
        int ySeq = Y.Net.PublishCommand(imola);
        Y.T.Connected = true;
        var cs2 = new List<Client> { Y };
        bool republished = false;
        Run(cs2, 3.0, () => { if (Y.Net.Republished) republished = true; });
        var Z = Join(w2, 8);
        cs2.Add(Z);
        Run(cs2, 4.0);
        AcNetCommand zc;
        Check(ySeq == 1 && republished && Z.Net.TryGetCommand(out zc) && zc.TrackHash == imola.TrackHash && zc.MasterId == 3,
              "Y (offline seq " + ySeq + ") becomes master of an empty room: its command is re-published (Republished) and joiner Z reads imola");
        foreach (var c in cs2) c.Net.Dispose();
    }

    // ------------------------------------------------------------------ seat refusals

    static void Seats()
    {
        _now = 500.0;
        var w = new AcLoopbackWorld();
        var A = Join(w, 1);
        var B = Join(w, 2);
        var cs = new List<Client> { A, B };
        A.Net.Own.Car.Present = true;
        A.Net.Own.Car.LayoutHash = 5;
        Run(cs, 4.0);
        bool moving = true;
        A.Net.CanGrant = id => !moving;            // S's CanGrantPassenger: "their car is moving"
        B.Net.BeginSeatRequest(1, 1);
        AcNet.SeatAnswer ans = AcNet.SeatAnswer.Waiting;
        Run(cs, 4.0, () => { var x = B.Net.TakeSeatAnswer(); if (x != AcNet.SeatAnswer.Waiting && x != AcNet.SeatAnswer.None) ans = x; });
        Check(ans == AcNet.SeatAnswer.Refused && A.Net.SeatOccupant < 0, "the owner's game refuses (car moving): the requester reads " + ans);
        moving = false;
        B.Net.BeginSeatRequest(1, 1);
        ans = AcNet.SeatAnswer.Waiting;
        Run(cs, 4.0, () => { var x = B.Net.TakeSeatAnswer(); if (x != AcNet.SeatAnswer.Waiting && x != AcNet.SeatAnswer.None) ans = x; });
        Check(ans == AcNet.SeatAnswer.Granted, "asked again once stopped: " + ans);
        A.Net.Own.NoPassengerSeat = true;
        A.Net.FreeSeat();
        var C = Join(w, 3);
        cs.Add(C);
        Run(cs, 4.0);
        C.Net.BeginSeatRequest(1, 1);
        ans = AcNet.SeatAnswer.Waiting;
        Run(cs, 4.0, () => { var x = C.Net.TakeSeatAnswer(); if (x != AcNet.SeatAnswer.Waiting && x != AcNet.SeatAnswer.None) ans = x; });
        Check(ans == AcNet.SeatAnswer.NoSeat || ans == AcNet.SeatAnswer.Taken, "a car without a passenger seat: " + ans);
        A.Net.Own.Car.Present = false;
        Run(cs, 1.0);
        C.Net.BeginSeatRequest(1, 1);
        ans = AcNet.SeatAnswer.Waiting;
        Run(cs, 2.0, () => { var x = C.Net.TakeSeatAnswer(); if (x != AcNet.SeatAnswer.Waiting && x != AcNet.SeatAnswer.None) ans = x; });
        Check(ans == AcNet.SeatAnswer.Gone, "the owner's car is gone: " + ans);
        foreach (var c in cs) c.Net.Dispose();
    }

    // ------------------------------------------------------------------ timeline under jitter

    static void Timeline()
    {
        var tl = new AcNetTimeline();
        var rnd = new Random(7);
        double t = 0, worst = 0, sum = 0;
        int n = 0, seq = 0;
        double nextSend = 0;
        var pending = new List<(double at, AcNetCar car, int seq, int stamp)>();
        for (int f = 0; f < 60 * 30; f++)
        {
            t += 1.0 / 60.0;
            // the writer: a car on a 300 m radius circle at 80 m/s, written at 30 Hz, Fusion delivers at 16 Hz with 40-90 ms latency
            if (t >= nextSend)
            {
                nextSend = t + 1.0 / 16.0;
                var car = CircleCar(t);
                car.Epoch = 1;
                pending.Add((t + 0.04 + rnd.NextDouble() * 0.05, car, ++seq, (int)(t * 1000)));
            }

            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].at <= t) { tl.Add(pending[i].car, pending[i].seq, pending[i].stamp, t); pending.RemoveAt(i); }
            AcNetCar d;
            if (f > 120 && tl.Sample(t, out d))
            {
                // distance from the drawn point to the circle (path error, not lag)
                double r = Math.Sqrt(d.Pos.X * d.Pos.X + d.Pos.Z * d.Pos.Z);
                double e = Math.Abs(r - 300.0);
                worst = Math.Max(worst, e);
                sum += e;
                n++;
            }
        }

        Check(worst < 0.05, "timeline: a car at 80 m/s on a 300 m circle, 16 Hz with 40-90 ms jitter, stays on its path (worst " + (worst * 100).ToString("0.0") + " cm, mean "
                            + (sum / n * 100).ToString("0.00") + " cm off the circle; drawn " + (tl.Delay * 1000).ToString("0") + " ms behind the best arrival)");
    }

    static AcNetCar CircleCar(double t)
    {
        double w = 80.0 / 300.0;
        var c = AcNetCar.Empty();
        c.Present = true;
        c.Pos = new AcNetV3((float)(300 * Math.Sin(w * t)), 0f, (float)(300 * Math.Cos(w * t)));
        c.Vel = new AcNetV3((float)(80 * Math.Cos(w * t)), 0f, (float)(-80 * Math.Sin(w * t)));
        double yaw = Math.Atan2(c.Vel.X, c.Vel.Z) / 2;
        c.Rot = new AcNetQ(0f, (float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
        return c;
    }
}
