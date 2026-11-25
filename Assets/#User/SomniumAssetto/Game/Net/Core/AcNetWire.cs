using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>What a player's slot says about its owner: not there, in the Dreamport (hub), or on a track. Values are on the wire.</summary>
    public enum AcNetStatus { Free = 0, Present = 1, Playing = 2 }

    /// <summary>A position / vector (metres) without a Unity type, so the wire runs in the offline harness (HaloV3 ported).</summary>
    public struct AcNetV3
    {
        public float X, Y, Z;

        public AcNetV3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public float SqrLength { get { return X * X + Y * Y + Z * Z; } }

        public static AcNetV3 Lerp(AcNetV3 a, AcNetV3 b, float t) { return new AcNetV3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t); }

        public static float Distance(AcNetV3 a, AcNetV3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static AcNetV3 operator +(AcNetV3 a, AcNetV3 b) { return new AcNetV3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static AcNetV3 operator -(AcNetV3 a, AcNetV3 b) { return new AcNetV3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static AcNetV3 operator *(AcNetV3 a, float s) { return new AcNetV3(a.X * s, a.Y * s, a.Z * s); }

        public override string ToString() { return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ", " + Z.ToString("0.###") + ")"; }
    }

    /// <summary>A rotation (x, y, z, w) without a Unity type (HaloQ ported).</summary>
    public struct AcNetQ
    {
        public float X, Y, Z, W;

        public AcNetQ(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public static AcNetQ Identity { get { return new AcNetQ(0f, 0f, 0f, 1f); } }

        /// <summary>The angle between two rotations in degrees.</summary>
        public static float AngleDeg(AcNetQ a, AcNetQ b)
        {
            double d = Math.Abs((double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W);
            if (d > 1.0) d = 1.0;
            return (float)(2.0 * Math.Acos(d) * 180.0 / Math.PI);
        }

        /// <summary>Normalised linear interpolation along the shorter way.</summary>
        public static AcNetQ Nlerp(AcNetQ a, AcNetQ b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            float s = dot < 0f ? -1f : 1f;
            var r = new AcNetQ(a.X + (b.X * s - a.X) * t, a.Y + (b.Y * s - a.Y) * t, a.Z + (b.Z * s - a.Z) * t, a.W + (b.W * s - a.W) * t);
            float len = (float)Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W);
            if (len < 1e-6f) return Identity;
            return new AcNetQ(r.X / len, r.Y / len, r.Z / len, r.W / len);
        }

        /// <summary>The heading (yaw about +Y, degrees, Unity convention: atan2(forward.x, forward.z)).</summary>
        public float YawDeg
        {
            get
            {
                // forward = q * (0,0,1)
                float fx = 2f * (X * Z + W * Y);
                float fz = 1f - 2f * (X * X + Y * Y);
                return (float)(Math.Atan2(fx, fz) * 180.0 / Math.PI);
            }
        }
    }

    /// <summary>One wheel as the wire carries it (index 0 LF, 1 RF, 2 LR, 3 RR - AC's WHEEL_INDEX).</summary>
    public struct AcNetWheel
    {
        /// <summary>Suspension travel from static, metres (positive = compressed), +-0.254 m in 2 mm steps (protocol 2).</summary>
        public float TravelM;
        /// <summary>0..1 how much the tyre slides (skid sound / smoke).</summary>
        public float Skid;
        /// <summary>Index of the surface under the tyre in the layout's surface list (AcSurfaces order), -1 none / in the air.</summary>
        public int Surface;
        /// <summary>The wheel's angular velocity (rad/s, positive rolling forward; P's AcWheelState.AngularVel) - player records only (ints 39-40,
        /// S16 0.1 rad/s), so lock-ups and wheelspin show remotely; see <see cref="AcNetCar.HasWheelSpeed"/>.</summary>
        public float AngularVel;
    }

    /// <summary>
    /// A car on the wire: the part of the 64-int records shared by a player's own car ("Ac Net Player" ints 5-30) and an AI car of the master
    /// ("Ac Net Ai", 26 of the 30 ints per car). Identity, pose, controls (for the engine sound and the cockpit), wheels, doors and timing.
    /// Positions are Unity world metres (tracks sit at AcSpace.MapOrigin on every client).
    /// </summary>
    public struct AcNetCar
    {
        /// <summary>A car is out on the track (my own parked or driven car, or a running AI car).</summary>
        public bool Present;
        /// <summary>NameHash of the car folder (AcCarEntry.Id), of the skin folder (AcSkinEntry.Id) and of the layout key (AcLayoutEntry.Key; 0 = hub).</summary>
        public int CarHash, SkinHash, LayoutHash;
        /// <summary>Placement epoch (0..255): +1 on every teleport (spawn, back to pits, grid, reset) - readers snap, never slide.</summary>
        public int Epoch;
        /// <summary>The car root (Rigidbody at the CG) in Unity world metres, its rotation, its velocity (m/s), yaw rate (deg/s).</summary>
        public AcNetV3 Pos;
        public AcNetQ Rot;
        public AcNetV3 Vel;
        public float YawRateDeg;
        /// <summary>Steering-wheel angle (deg, right positive), front road-wheel angles LF / RF (deg).</summary>
        public float SteerWheelDeg, SteerLF, SteerRF;
        public float Rpm, Throttle, Brake, Clutch, Handbrake, BoostBar;
        /// <summary>-1 reverse, 0 neutral, 1.. forward.</summary>
        public int Gear;
        /// <summary>AcLightState bits.</summary>
        public int Lights;
        public bool Limiter, Abs, Tc, Shifting, Frozen;
        public AcNetWheel W0, W1, W2, W3;
        /// <summary>Door opening 0..1 (left, right).</summary>
        public float DoorL, DoorR;
        // timing (owner-authoritative; R's AcLocal race fields)
        public int Laps, Sector, Cuts, LapMs, LastMs, BestMs, FinishMs, PenaltyMs, Position;
        /// <summary>Laps + fast-lane progress (research T3).</summary>
        public float RaceDistance;
        public bool LapValid, InPitlane, Finished;
        /// <summary>Player records only (int 41): the owner's fuel (litres, 0.1 L steps) and whether ints 39-40 carry the wheels' angular velocity
        /// (AI cars in the "Ac Net Ai" blocks have no room: readers derive the spin from the forward speed).</summary>
        public float FuelLitres;
        public bool HasWheelSpeed;
        /// <summary>The damage pair (AcCarDamage.ToWire / ApplyWire; protocol 3; 0 0 = undamaged): player records ints 42-43, AI cars from the
        /// "Ac Net Host 0" table (AcNetBlocks.PackAiDamage). Raw - the game layer decodes it.</summary>
        public int DamageD0, DamageD1;

        public AcNetWheel Wheel(int i)
        {
            switch (i) { case 0: return W0; case 1: return W1; case 2: return W2; default: return W3; }
        }

        public void SetWheel(int i, AcNetWheel w)
        {
            switch (i) { case 0: W0 = w; break; case 1: W1 = w; break; case 2: W2 = w; break; default: W3 = w; break; }
        }

        public static AcNetCar Empty()
        {
            var c = default(AcNetCar);
            c.Rot = AcNetQ.Identity;
            c.LapValid = true;
            c.W0.Surface = c.W1.Surface = c.W2.Surface = c.W3.Surface = -1;
            return c;
        }
    }

    /// <summary>Where a player sits / rides: a car (named by its owner) and the body root's pose in that car's frame as the PUBLISHER draws it
    /// (EtsRiders' rider record). Every client puts the body on its own drawn copy of that car (rigid correction).</summary>
    public struct AcNetRider
    {
        /// <summary>The car: <see cref="AcNetWire.TargetPlayer"/>(photon id) or <see cref="AcNetWire.TargetAi"/>(index); 0 = none.</summary>
        public int Target;
        /// <summary>0 driver, 1 passenger (AcSeatKind).</summary>
        public int Seat;
        /// <summary>+1 when the target or seat changes (readers re-bind).</summary>
        public int Epoch;
        /// <summary>Asking the owner for the seat (not placed until granted).</summary>
        public bool Requesting;
        public bool Xr;
        /// <summary>Body root in the car root's local frame (metres) and its heading relative to the car's (degrees).</summary>
        public AcNetV3 Offset;
        public float YawLocal;
    }

    /// <summary>Everything one player says about itself, decoded (or about to be encoded).</summary>
    public struct AcNetState
    {
        // int 4 flags
        public AcNetStatus Status;
        public bool Vr, OnTrack, Seated, Driving, Passenger, Master, Ready, Spectating;
        public AcNetCar Car;
        // seat table (owner of the car writes): who sits in my passenger seat
        public int PassengerId;           // photon id, -1 none
        public bool NoPassengerSeat, RefuseRiders;
        public AcNetRider Rider;
        /// <summary>Sector times of the last completed lap (ms, up to 3).</summary>
        public int Sector0Ms, Sector1Ms, Sector2Ms;
        /// <summary>The race command Seq this client applied last.</summary>
        public int AppliedSeq;

        public static AcNetState Empty()
        {
            var s = default(AcNetState);
            s.Car = AcNetCar.Empty();
            s.PassengerId = -1;
            s.Rider.Offset = default(AcNetV3);
            return s;
        }
    }

    /// <summary>
    /// A player slot's 64 ints, "Ac Net Player 0..49" (NetworkBridgeData64 each): Halo's header (ints 0-4, HaloNetWire verbatim in meaning) and the
    /// racing payload. Only the slot's owner writes it; every client reads all. Pure C# (the harness runs encode / decode). Fusion sends an int only
    /// when it changes, so a parked car costs nothing. Seq (int 1) and stamp (int 2) advance only when a MOTION int (8-22) changes: a reader adds a
    /// timeline sample per new seq, and re-decodes the rest whenever any int changes.
    ///
    ///   0  magic 'ACP1' (0 = never written)          1  seq            2  stamp: the writer's physics clock in ms (time of the pose)
    ///   3  owner PlayerId+1 (0-15) | claim generation (16-23) | protocol (24-27)
    ///   4  flags: status (0-1: Free / Present / Playing) | VR 2 | on a track 3 | seated 4 | driving 5 | passenger 6 | instance master 7 |
    ///      lobby ready 8 | spectating 9
    ///   5  car: NameHash of the car folder        6  NameHash of the skin folder        7  NameHash of the layout key I am on (0 = hub)
    ///   8-10  car root position, int mm (Unity world)      11-12  rotation, smallest-three 3 x 20 bits (PackQuat2)
    ///  13  velocity x (0-15) | z (16-31), S16 cm/s        14  velocity y S16 cm/s | yaw rate S16 0.1 deg/s
    ///  15  steering-wheel S16 0.1 deg | rpm u16           16  gear s8 | throttle u8 | brake u8 | clutch u8
    ///  17  AcLightState u16 | handbrake u8 | boost u8 (0.02 bar)
    ///  18  suspension travel x4 (s8 2 mm, LF RF LR RR)   19  skid x4 (u8)     20  surface index x4 (u8, 255 none)
    ///  21  door L u8 | door R u8 | placement epoch u8 | car flags u8 (present 0, limiter 1, ABS 2, TC 3, shifting 4, frozen 5)
    ///  22  front road-wheel steer LF S16 0.01 deg | RF S16
    ///  23  laps u16 | sector u8 | cuts u8                 24  current lap ms      25  last lap ms     26  best lap ms
    ///  27  race distance x 1e4 (laps + progress)          28  finish total ms     29  penalty ms
    ///  30  position u8 | lap valid 8 | in pit lane 9 | finished 10
    ///  31  seat table: passenger PlayerId+1 u16 | no passenger seat 16 | refusing riders 17
    ///  32  rider: target u16 (PlayerId+1, or 0x8000|AI index; 0 none) | seat u8 | epoch u4 (24-27) | requesting 28 | VR 29
    ///  33  rider offset x | y (S16 mm)                     34  rider offset z S16 mm | rider yaw16 (relative to the car)
    ///  35-37  last lap's sector ms (3)                     38  race command Seq applied
    ///  39  wheel angular velocity LF | RF (S16 0.1 rad/s)  40  LR | RR             41  fuel u16 (0.1 L) | wheel speeds present 16
    ///  42  damage d0: zone FRONT u8 | REAR u8 &lt;&lt; 8 | LEFT u8 &lt;&lt; 16 | RIGHT u8 &lt;&lt; 24 (km/h, AcCarDamage.ZoneByte; protocol 3)
    ///  43  damage d1: CENTER u8 | rod LF u4 &lt;&lt; 8 | RF &lt;&lt; 12 | LR &lt;&lt; 16 | RR &lt;&lt; 20 | engine damage % u7 &lt;&lt; 24 | retired &lt;&lt; 31 (0 0 = undamaged)
    ///  44-63  spare 0
    /// </summary>
    public static class AcNetWire
    {
        public const int Magic = 0x41435031;   // 'ACP1'
        /// <summary>Wire layout version (header int 3 bits 24-27). Bump it whenever a field's meaning or scale changes. A record of another protocol is
        /// NOT decoded (no player, no car, no seat, no timing); its header (ints 0-4: owner, generation, status, master bit 7 - the same in every
        /// protocol) still keeps its slot taken and counts in the master rule, so two builds in one instance neither fight over slots / blocks nor
        /// misread each other. 1 = first build (suspension travel in 1 mm); 2 = suspension travel in 2 mm units (int 18; lead 2026-11-01).
        /// 3 = car damage (iteration 2): the damage pair in ints 42-43 (player records), AI damage in "Ac Net Host 0" (AcNetBlocks.MagicAiDamage).
        /// Protocol-1 builds never checked this field (none was uploaded).</summary>
        public const int Protocol = 3;
        public const int Ints = 64;
        public const int Used = 44;
        public const int MaxSlots = 50;
        public const int NoSlot = 63;

        public const int AtMagic = 0, AtSeq = 1, AtStamp = 2, AtOwner = 3, AtFlags = 4, AtCar = 5, AtSeats = 31, AtRider = 32, AtRiderXY = 33, AtRiderZ = 34,
            AtSectors = 35, AtApplied = 38, AtWheelSpeed = 39, AtFuel = 41, AtDamage = 42;

        /// <summary>The car block inside a record: 26 ints (player ints 5-30; the first 26 of an AI car's 30).</summary>
        public const int CarInts = 26;
        /// <summary>Car-block offsets (relative to the block start).</summary>
        public const int CId = 0, CSkin = 1, CLayout = 2, CPos = 3, CRot = 6, CVelXZ = 8, CVelY = 9, CSteerRpm = 10, CPedals = 11, CLights = 12, CSusp = 13,
            CSkid = 14, CSurf = 15, CDoors = 16, CFront = 17, CLaps = 18, CLapMs = 19, CLast = 20, CBest = 21, CDist = 22, CFinish = 23, CPenalty = 24, CPos2 = 25;
        /// <summary>First and last car-block offset whose change is "motion" (bumps seq/stamp).</summary>
        public const int CMotionFirst = CPos, CMotionLast = CFront;

        private const int FlagVr = 1 << 2, FlagOnTrack = 1 << 3, FlagSeated = 1 << 4, FlagDriving = 1 << 5, FlagPassenger = 1 << 6, FlagMaster = 1 << 7,
            FlagReady = 1 << 8, FlagSpectate = 1 << 9;

        // ------------------------------------------------------------------ the slot header (HaloNetWire verbatim in meaning)

        /// <summary>The slot's owner as a PlayerId, -1 when never written or free.</summary>
        public static int OwnerOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] & 0xFFFF) - 1 : -1;
        }

        public static int GenerationOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] >> 16) & 0xFF : 0;
        }

        /// <summary>The protocol of the writer's build (0 for a free / never written record).</summary>
        public static int ProtocolOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] >> 24) & 0xF : 0;
        }

        /// <summary>A claimed record written by a build of another protocol (it is not decoded; see <see cref="Protocol"/>).</summary>
        public static bool Foreign(int[] from)
        {
            return StatusOf(from) != AcNetStatus.Free && ProtocolOf(from) != Protocol;
        }

        /// <summary>The instance-master bit 7 of the flags (header int 4, the same in every protocol).</summary>
        public static bool MasterBitOf(int[] from)
        {
            return from != null && from.Length > AtFlags && from[AtMagic] == Magic && (from[AtFlags] & FlagMaster) != 0;
        }

        public static AcNetStatus StatusOf(int[] from)
        {
            if (from == null || from.Length < Used || from[AtMagic] != Magic) return AcNetStatus.Free;
            int s = from[AtFlags] & 3;
            return s <= 2 ? (AcNetStatus)s : AcNetStatus.Free;
        }

        /// <summary>The free record: the magic, seq + 1, the stamp, owner 0 with the claim generation kept, all else 0.</summary>
        public static void WriteFree(int[] into, int stampMs)
        {
            int seq = into[AtSeq] + 1;
            int generation = GenerationOf(into);
            Array.Clear(into, 0, into.Length);
            into[AtMagic] = Magic;
            into[AtSeq] = seq;
            into[AtStamp] = stampMs;
            into[AtOwner] = generation << 16;
        }

        /// <summary>A fresh claim: every int cleared, then owner = this client, the new generation, status Present.</summary>
        public static void WriteClaim(int[] into, int localId, int generation, int stampMs)
        {
            int seq = into[AtMagic] == Magic ? into[AtSeq] + 1 : 1;
            Array.Clear(into, 0, into.Length);
            into[AtMagic] = Magic;
            into[AtSeq] = seq;
            into[AtStamp] = stampMs;
            into[AtOwner] = ((localId < 0 ? 0 : localId + 1) & 0xFFFF) | ((generation & 0xFF) << 16) | (Protocol << 24);
            into[AtFlags] = (int)AcNetStatus.Present;
            into[AtCar + CSurf] = -1;     // 4 x 255: no surface
        }

        // ------------------------------------------------------------------ encode / decode

        /// <summary>Ints 4 and 5-38 from a state; the header (0-3) is kept. Returns true when a MOTION int changed (the caller bumps seq + stamp);
        /// <paramref name="any"/> = anything changed.</summary>
        public static bool Pack(in AcNetState s, int[] into, out bool any)
        {
            int flags = ((int)s.Status & 3) | (s.Vr ? FlagVr : 0) | (s.OnTrack ? FlagOnTrack : 0) | (s.Seated ? FlagSeated : 0) | (s.Driving ? FlagDriving : 0)
                        | (s.Passenger ? FlagPassenger : 0) | (s.Master ? FlagMaster : 0) | (s.Ready ? FlagReady : 0) | (s.Spectating ? FlagSpectate : 0);
            any = Set(into, AtFlags, flags);
            bool motion = PackCar(in s.Car, into, AtCar, ref any);
            int seats = (s.PassengerId >= 0 ? (s.PassengerId + 1) & 0xFFFF : 0) | (s.NoPassengerSeat ? 1 << 16 : 0) | (s.RefuseRiders ? 1 << 17 : 0);
            any |= Set(into, AtSeats, seats);
            PackRider(in s.Rider, into, ref any);
            any |= Set(into, AtSectors, Math.Max(0, s.Sector0Ms));
            any |= Set(into, AtSectors + 1, Math.Max(0, s.Sector1Ms));
            any |= Set(into, AtSectors + 2, Math.Max(0, s.Sector2Ms));
            any |= Set(into, AtApplied, s.AppliedSeq);
            // wheel speeds count as motion (a lock-up / wheelspin is drawn from the timeline); fuel does not (it changes only while moving)
            bool ws = s.Car.HasWheelSpeed;
            bool m2 = Set(into, AtWheelSpeed, ws ? Two16(S16(s.Car.W0.AngularVel * 10f), S16(s.Car.W1.AngularVel * 10f)) : 0);
            m2 |= Set(into, AtWheelSpeed + 1, ws ? Two16(S16(s.Car.W2.AngularVel * 10f), S16(s.Car.W3.AngularVel * 10f)) : 0);
            any |= m2;
            any |= Set(into, AtFuel, U16(s.Car.FuelLitres * 10f) | (ws ? 1 << 16 : 0));
            // the damage pair (protocol 3) is not motion: no seq / stamp bump (DAMAGE_DESIGN 7)
            any |= Set(into, AtDamage, s.Car.DamageD0);
            any |= Set(into, AtDamage + 1, s.Car.DamageD1);
            return motion | m2;
        }

        /// <summary>A record into a state (false when the record is not ours / not written).</summary>
        public static bool Unpack(int[] from, out AcNetState s)
        {
            s = AcNetState.Empty();
            if (from == null || from.Length < Used || from[AtMagic] != Magic || ProtocolOf(from) != Protocol) return false;
            int f = from[AtFlags];
            int st = f & 3;
            s.Status = st <= 2 ? (AcNetStatus)st : AcNetStatus.Free;
            s.Vr = (f & FlagVr) != 0;
            s.OnTrack = (f & FlagOnTrack) != 0;
            s.Seated = (f & FlagSeated) != 0;
            s.Driving = (f & FlagDriving) != 0;
            s.Passenger = (f & FlagPassenger) != 0;
            s.Master = (f & FlagMaster) != 0;
            s.Ready = (f & FlagReady) != 0;
            s.Spectating = (f & FlagSpectate) != 0;
            UnpackCar(from, AtCar, out s.Car);
            int seats = from[AtSeats];
            s.PassengerId = (seats & 0xFFFF) - 1;
            s.NoPassengerSeat = (seats & (1 << 16)) != 0;
            s.RefuseRiders = (seats & (1 << 17)) != 0;
            UnpackRider(from, out s.Rider);
            s.Sector0Ms = from[AtSectors];
            s.Sector1Ms = from[AtSectors + 1];
            s.Sector2Ms = from[AtSectors + 2];
            s.AppliedSeq = from[AtApplied];
            int fuel = from[AtFuel];
            s.Car.FuelLitres = (fuel & 0xFFFF) / 10f;
            s.Car.HasWheelSpeed = (fuel & (1 << 16)) != 0;
            s.Car.DamageD0 = from[AtDamage];
            s.Car.DamageD1 = from[AtDamage + 1];
            if (s.Car.HasWheelSpeed)
            {
                int w01 = from[AtWheelSpeed], w23 = from[AtWheelSpeed + 1];
                s.Car.W0.AngularVel = Lo16(w01) / 10f;
                s.Car.W1.AngularVel = Hi16(w01) / 10f;
                s.Car.W2.AngularVel = Lo16(w23) / 10f;
                s.Car.W3.AngularVel = Hi16(w23) / 10f;
            }

            return true;
        }

        /// <summary>The car block (26 ints) at <paramref name="at"/>. Returns true when a motion int changed; ORs any change into <paramref name="any"/>.</summary>
        public static bool PackCar(in AcNetCar c, int[] into, int at, ref bool any)
        {
            bool m = false;
            any |= Set(into, at + CId, c.CarHash);
            any |= Set(into, at + CSkin, c.SkinHash);
            any |= Set(into, at + CLayout, c.LayoutHash);
            m |= Set(into, at + CPos, Mm(c.Pos.X));
            m |= Set(into, at + CPos + 1, Mm(c.Pos.Y));
            m |= Set(into, at + CPos + 2, Mm(c.Pos.Z));
            int lo, hi;
            PackQuat2(c.Rot, out lo, out hi);
            m |= Set(into, at + CRot, lo);
            m |= Set(into, at + CRot + 1, hi);
            m |= Set(into, at + CVelXZ, Two16(S16(c.Vel.X * 100f), S16(c.Vel.Z * 100f)));
            m |= Set(into, at + CVelY, Two16(S16(c.Vel.Y * 100f), S16(c.YawRateDeg * 10f)));
            m |= Set(into, at + CSteerRpm, Two16(S16(c.SteerWheelDeg * 10f), U16(c.Rpm)));
            m |= Set(into, at + CPedals, (c.Gear & 0xFF) | (Unit8(c.Throttle, 1f) << 8) | (Unit8(c.Brake, 1f) << 16) | (Unit8(c.Clutch, 1f) << 24));
            m |= Set(into, at + CLights, (c.Lights & 0xFFFF) | (Unit8(c.Handbrake, 1f) << 16) | (Unit8(c.BoostBar, 5.1f) << 24));
            m |= Set(into, at + CSusp, Four8(S8(c.W0.TravelM * 500f), S8(c.W1.TravelM * 500f), S8(c.W2.TravelM * 500f), S8(c.W3.TravelM * 500f)));   // 2 mm units, +-254 mm (lead, P -> N: TravelM carries P's visual offset)
            m |= Set(into, at + CSkid, Four8(Unit8(c.W0.Skid, 1f), Unit8(c.W1.Skid, 1f), Unit8(c.W2.Skid, 1f), Unit8(c.W3.Skid, 1f)));
            m |= Set(into, at + CSurf, Four8(Surf8(c.W0.Surface), Surf8(c.W1.Surface), Surf8(c.W2.Surface), Surf8(c.W3.Surface)));
            int carFlags = (c.Present ? 1 : 0) | (c.Limiter ? 2 : 0) | (c.Abs ? 4 : 0) | (c.Tc ? 8 : 0) | (c.Shifting ? 16 : 0) | (c.Frozen ? 32 : 0);
            m |= Set(into, at + CDoors, Four8(Unit8(c.DoorL, 1f), Unit8(c.DoorR, 1f), c.Epoch & 0xFF, carFlags));
            m |= Set(into, at + CFront, Two16(S16(c.SteerLF * 100f), S16(c.SteerRF * 100f)));
            any |= m;
            any |= Set(into, at + CLaps, (Clamp(c.Laps, 0, 0xFFFF)) | (Clamp(c.Sector, 0, 255) << 16) | (Clamp(c.Cuts, 0, 255) << 24));
            any |= Set(into, at + CLapMs, Math.Max(0, c.LapMs));
            any |= Set(into, at + CLast, Math.Max(0, c.LastMs));
            any |= Set(into, at + CBest, Math.Max(0, c.BestMs));
            any |= Set(into, at + CDist, Dist(c.RaceDistance));
            any |= Set(into, at + CFinish, Math.Max(0, c.FinishMs));
            any |= Set(into, at + CPenalty, Math.Max(0, c.PenaltyMs));
            any |= Set(into, at + CPos2, Clamp(c.Position, 0, 255) | (c.LapValid ? 1 << 8 : 0) | (c.InPitlane ? 1 << 9 : 0) | (c.Finished ? 1 << 10 : 0));
            return m;
        }

        public static void UnpackCar(int[] from, int at, out AcNetCar c)
        {
            c = AcNetCar.Empty();
            c.CarHash = from[at + CId];
            c.SkinHash = from[at + CSkin];
            c.LayoutHash = from[at + CLayout];
            c.Pos = new AcNetV3(FromMm(from[at + CPos]), FromMm(from[at + CPos + 1]), FromMm(from[at + CPos + 2]));
            c.Rot = UnpackQuat2(from[at + CRot], from[at + CRot + 1]);
            int vxz = from[at + CVelXZ], vy = from[at + CVelY];
            c.Vel = new AcNetV3(Lo16(vxz) / 100f, Lo16(vy) / 100f, Hi16(vxz) / 100f);
            c.YawRateDeg = Hi16(vy) / 10f;
            int sr = from[at + CSteerRpm];
            c.SteerWheelDeg = Lo16(sr) / 10f;
            c.Rpm = (sr >> 16) & 0xFFFF;
            int p = from[at + CPedals];
            c.Gear = (sbyte)(p & 0xFF);
            c.Throttle = ((p >> 8) & 0xFF) / 255f;
            c.Brake = ((p >> 16) & 0xFF) / 255f;
            c.Clutch = ((p >> 24) & 0xFF) / 255f;
            int l = from[at + CLights];
            c.Lights = l & 0xFFFF;
            c.Handbrake = ((l >> 16) & 0xFF) / 255f;
            c.BoostBar = ((l >> 24) & 0xFF) / 255f * 5.1f;
            int su = from[at + CSusp], sk = from[at + CSkid], sf = from[at + CSurf];
            for (int i = 0; i < 4; i++)
            {
                var w = new AcNetWheel
                {
                    TravelM = (sbyte)((su >> (8 * i)) & 0xFF) / 500f,   // 2 mm units
                    Skid = ((sk >> (8 * i)) & 0xFF) / 255f,
                    Surface = ((sf >> (8 * i)) & 0xFF) == 255 ? -1 : (sf >> (8 * i)) & 0xFF,
                };
                c.SetWheel(i, w);
            }

            int d = from[at + CDoors];
            c.DoorL = (d & 0xFF) / 255f;
            c.DoorR = ((d >> 8) & 0xFF) / 255f;
            c.Epoch = (d >> 16) & 0xFF;
            int cf = (d >> 24) & 0xFF;
            c.Present = (cf & 1) != 0;
            c.Limiter = (cf & 2) != 0;
            c.Abs = (cf & 4) != 0;
            c.Tc = (cf & 8) != 0;
            c.Shifting = (cf & 16) != 0;
            c.Frozen = (cf & 32) != 0;
            int fr = from[at + CFront];
            c.SteerLF = Lo16(fr) / 100f;
            c.SteerRF = Hi16(fr) / 100f;
            int laps = from[at + CLaps];
            c.Laps = laps & 0xFFFF;
            c.Sector = (laps >> 16) & 0xFF;
            c.Cuts = (laps >> 24) & 0xFF;
            c.LapMs = from[at + CLapMs];
            c.LastMs = from[at + CLast];
            c.BestMs = from[at + CBest];
            c.RaceDistance = from[at + CDist] / 10000f;
            c.FinishMs = from[at + CFinish];
            c.PenaltyMs = from[at + CPenalty];
            int p2 = from[at + CPos2];
            c.Position = p2 & 0xFF;
            c.LapValid = (p2 & (1 << 8)) != 0;
            c.InPitlane = (p2 & (1 << 9)) != 0;
            c.Finished = (p2 & (1 << 10)) != 0;
        }

        private static void PackRider(in AcNetRider r, int[] into, ref bool any)
        {
            int v = (r.Target & 0xFFFF) | ((r.Seat & 0xFF) << 16) | ((r.Epoch & 0xF) << 24) | (r.Requesting ? 1 << 28 : 0) | (r.Xr ? 1 << 29 : 0);
            if (r.Target == 0) v = 0;
            any |= Set(into, AtRider, v);
            any |= Set(into, AtRiderXY, r.Target == 0 ? 0 : Two16(S16(r.Offset.X * 1000f), S16(r.Offset.Y * 1000f)));
            any |= Set(into, AtRiderZ, r.Target == 0 ? 0 : Two16(S16(r.Offset.Z * 1000f), Yaw16(r.YawLocal)));
        }

        private static void UnpackRider(int[] from, out AcNetRider r)
        {
            r = default(AcNetRider);
            int v = from[AtRider];
            r.Target = v & 0xFFFF;
            if (r.Target == 0) return;
            r.Seat = (v >> 16) & 0xFF;
            r.Epoch = (v >> 24) & 0xF;
            r.Requesting = (v & (1 << 28)) != 0;
            r.Xr = (v & (1 << 29)) != 0;
            int xy = from[AtRiderXY], z = from[AtRiderZ];
            r.Offset = new AcNetV3(Lo16(xy) / 1000f, Hi16(xy) / 1000f, Lo16(z) / 1000f);
            float yaw = ((z >> 16) & 0xFFFF) / 65536f * 360f;
            r.YawLocal = yaw > 180f ? yaw - 360f : yaw;
        }

        // ------------------------------------------------------------------ rider / grid targets

        /// <summary>A rider / grid code for a player's car (PlayerId 0..32766).</summary>
        public static int TargetPlayer(int playerId) { return playerId < 0 || playerId >= 0x7FFF ? 0 : playerId + 1; }

        /// <summary>A rider / grid code for the master's AI car <paramref name="index"/> (0..0x7FFE).</summary>
        public static int TargetAi(int index) { return 0x8000 | (index & 0x7FFF); }

        public static bool IsAiTarget(int code) { return (code & 0x8000) != 0; }

        /// <summary>The PlayerId of a player target (-1 none / AI).</summary>
        public static int TargetPlayerId(int code) { return code == 0 || IsAiTarget(code) ? -1 : (code & 0xFFFF) - 1; }

        /// <summary>The AI index of an AI target (-1 none / player).</summary>
        public static int TargetAiIndex(int code) { return IsAiTarget(code) ? code & 0x7FFF : -1; }

        /// <summary>A grid / standings id (AcRaceCommand.Grid convention: Photon id, AI = -(100 + index)) as a 16-bit code, and back.</summary>
        public static int GridCode(int id) { return id <= -100 ? TargetAi(-100 - id) : TargetPlayer(id); }

        public static int GridId(int code)
        {
            if (code == 0) return int.MinValue;
            return IsAiTarget(code) ? -100 - TargetAiIndex(code) : TargetPlayerId(code);
        }

        // ------------------------------------------------------------------ packers

        public static int Unit8(float value, float max)
        {
            if (!(value > 0f)) return 0;
            int q = (int)Math.Round(value / max * 255.0);
            return q > 255 ? 255 : q;
        }

        public static int Mm(float metres)
        {
            double v = Math.Round(metres * 1000.0);
            if (double.IsNaN(v)) return 0;
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        public static float FromMm(int mm) { return (float)(mm / 1000.0); }

        public static int S16(float v)
        {
            if (float.IsNaN(v)) return 0;
            double r = Math.Round(v);
            if (r > 32767) return 32767;
            if (r < -32767) return -32767;
            return (int)r;
        }

        public static int S8(float v)
        {
            if (float.IsNaN(v)) return 0;
            double r = Math.Round(v);
            if (r > 127) return 127;
            if (r < -127) return -127;
            return (int)r;
        }

        public static int U16(float v)
        {
            if (!(v > 0f)) return 0;
            double r = Math.Round(v);
            return r > 65535 ? 65535 : (int)r;
        }

        private static int Surf8(int surface) { return surface < 0 || surface > 254 ? 255 : surface; }

        public static int Two16(int lo, int hi) { return (lo & 0xFFFF) | ((hi & 0xFFFF) << 16); }

        public static int Four8(int a, int b, int c, int d) { return (a & 0xFF) | ((b & 0xFF) << 8) | ((c & 0xFF) << 16) | ((d & 0xFF) << 24); }

        public static int Lo16(int v) { return (short)(v & 0xFFFF); }

        public static int Hi16(int v) { return (short)((v >> 16) & 0xFFFF); }

        private static int Dist(float d)
        {
            if (float.IsNaN(d)) return 0;
            double v = Math.Round(d * 10000.0);
            if (v > int.MaxValue) return int.MaxValue;
            if (v < -int.MaxValue) return -int.MaxValue;
            return (int)v;
        }

        public static int Yaw16(float deg)
        {
            if (float.IsNaN(deg)) return 0;
            double d = deg % 360.0;
            if (d < 0.0) d += 360.0;
            int q = (int)Math.Round(d / 360.0 * 65536.0);
            return q & 0xFFFF;
        }

        private const int Q20Max = (1 << 20) - 1;
        private const double R2 = 0.70710678118654752;

        /// <summary>
        /// A rotation in two ints ("smallest three", 3 x 20 bits + the index of the dropped component): about 0.00015 degrees, against 0.16 for
        /// Halo's one-int PackQuat (the brief asks for under 0.01 deg). lo = bits 0-31, hi = bits 32-63.
        /// </summary>
        public static void PackQuat2(AcNetQ q, out int lo, out int hi)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double len = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-9 || double.IsNaN(len)) { x = y = z = 0; w = 1; }
            else { x /= len; y /= len; z /= len; w /= len; }
            int idx = 3;
            double max = Math.Abs(w);
            if (Math.Abs(x) > max) { idx = 0; max = Math.Abs(x); }
            if (Math.Abs(y) > max) { idx = 1; max = Math.Abs(y); }
            if (Math.Abs(z) > max) { idx = 2; }
            double big = idx == 0 ? x : idx == 1 ? y : idx == 2 ? z : w;
            double sign = big < 0 ? -1 : 1;
            double a, b, c;
            switch (idx)
            {
                case 0: a = y; b = z; c = w; break;
                case 1: a = x; b = z; c = w; break;
                case 2: a = x; b = y; c = w; break;
                default: a = x; b = y; c = z; break;
            }

            long bits = ((long)Q20(a * sign)) | ((long)Q20(b * sign) << 20) | ((long)Q20(c * sign) << 40) | ((long)idx << 60);
            lo = unchecked((int)(bits & 0xFFFFFFFFL));
            hi = unchecked((int)((bits >> 32) & 0xFFFFFFFFL));
        }

        public static AcNetQ UnpackQuat2(int lo, int hi)
        {
            long bits = ((long)(uint)lo) | ((long)(uint)hi << 32);
            int idx = (int)((bits >> 60) & 3);
            double a = U20((int)(bits & Q20Max)), b = U20((int)((bits >> 20) & Q20Max)), c = U20((int)((bits >> 40) & Q20Max));
            double big = Math.Sqrt(Math.Max(0.0, 1.0 - a * a - b * b - c * c));
            switch (idx)
            {
                case 0: return new AcNetQ((float)big, (float)a, (float)b, (float)c);
                case 1: return new AcNetQ((float)a, (float)big, (float)b, (float)c);
                case 2: return new AcNetQ((float)a, (float)b, (float)big, (float)c);
                default: return new AcNetQ((float)a, (float)b, (float)c, (float)big);
            }
        }

        private static int Q20(double v)
        {
            double t = v / R2 * 0.5 + 0.5;
            long i = (long)Math.Round(t * Q20Max);
            return i < 0 ? 0 : i > Q20Max ? Q20Max : (int)i;
        }

        private static double U20(int q) { return ((q / (double)Q20Max) - 0.5) * 2.0 * R2; }

        public static bool Set(int[] a, int i, int v)
        {
            if (a[i] == v) return false;
            a[i] = v;
            return true;
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>FNV-1a of the lower-case name (HaloNetWire.NameHash verbatim); 0 only for null / empty.</summary>
        public static int NameHash(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            uint h = 2166136261u;
            for (int i = 0; i < name.Length; i++)
            {
                h ^= char.ToLowerInvariant(name[i]);
                h *= 16777619u;
            }

            return h == 0 ? 1 : unchecked((int)h);
        }

        /// <summary>Up to 4 Latin-1 characters per int (names on the wire; '?' for anything above 255).</summary>
        public static void PackText(string text, int[] into, int at, int ints, ref bool any)
        {
            for (int k = 0; k < ints; k++)
            {
                int v = 0;
                for (int j = 0; j < 4; j++)
                {
                    int i = k * 4 + j;
                    int ch = text != null && i < text.Length ? text[i] : 0;
                    if (ch > 255) ch = '?';
                    v |= (ch & 0xFF) << (8 * j);
                }

                any |= Set(into, at + k, v);
            }
        }

        public static string UnpackText(int[] from, int at, int ints)
        {
            var chars = new char[ints * 4];
            int n = 0;
            for (int k = 0; k < ints; k++)
            {
                int v = from[at + k];
                for (int j = 0; j < 4; j++)
                {
                    int ch = (v >> (8 * j)) & 0xFF;
                    if (ch == 0) return new string(chars, 0, n);
                    chars[n++] = (char)ch;
                }
            }

            return new string(chars, 0, n);
        }
    }
}
