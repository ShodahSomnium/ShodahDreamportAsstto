using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The race-control command as the wire carries it (pure twin of AcRaceCommand + AcRules; the Unity side converts with the catalog). Hashes are
    /// <see cref="AcNetWire.NameHash"/> of the folder names. Grid ids follow AcRaceCommand.Grid (Photon id, AI = -(100 + index)).
    /// </summary>
    public sealed class AcNetCommand
    {
        public int Seq;
        /// <summary>AcPhase as int.</summary>
        public int Phase;
        public int TrackHash, LayoutHash;
        /// <summary>AcSessionType as int (1..4).</summary>
        public int Session = 1;
        public int Laps, Minutes;
        public int Abs = 1, Tc = 1;
        public bool Stability, AutoClutch = true, AutoShift = true, Blankets;
        public int TyresOut = 2, JumpStart = 2, FuelRate = 100, TyreWear = 100, Damage, AiCount, AiLevel = 90, RaceOverSeconds = 180;
        public float PitLimitKmh = 80f;
        /// <summary>Green light on the shared UTC clock: whole seconds + milliseconds (0 = not scheduled).</summary>
        public int StartSec, StartMs;
        /// <summary>When the master entered the phase (UTC seconds + ms; R's AcRaceCommand.PhaseUtc), 0 = not set.</summary>
        public int PhaseSec, PhaseMs;
        public int MasterId = -1;
        public readonly List<int> Grid = new List<int>(AcNetBlocks.MaxGrid);
        public readonly List<int> Allowed = new List<int>(AcNetBlocks.MaxAllowed);

        public double StartUtc
        {
            get { return StartSec == 0 && StartMs == 0 ? 0.0 : StartSec + StartMs / 1000.0; }
            set
            {
                if (!(value > 0.0)) { StartSec = 0; StartMs = 0; return; }
                double s = Math.Floor(value);
                StartSec = (int)s;
                StartMs = (int)Math.Round((value - s) * 1000.0);
                if (StartMs >= 1000) { StartSec++; StartMs -= 1000; }
            }
        }

        public double PhaseUtc
        {
            get { return PhaseSec == 0 && PhaseMs == 0 ? 0.0 : PhaseSec + PhaseMs / 1000.0; }
            set
            {
                if (!(value > 0.0)) { PhaseSec = 0; PhaseMs = 0; return; }
                double s = Math.Floor(value);
                PhaseSec = (int)s;
                PhaseMs = (int)Math.Round((value - s) * 1000.0);
                if (PhaseMs >= 1000) { PhaseSec++; PhaseMs -= 1000; }
            }
        }

        public void CopyFrom(AcNetCommand o)
        {
            Seq = o.Seq; Phase = o.Phase; TrackHash = o.TrackHash; LayoutHash = o.LayoutHash; Session = o.Session; Laps = o.Laps; Minutes = o.Minutes;
            Abs = o.Abs; Tc = o.Tc; Stability = o.Stability; AutoClutch = o.AutoClutch; AutoShift = o.AutoShift; Blankets = o.Blankets; TyresOut = o.TyresOut;
            JumpStart = o.JumpStart; FuelRate = o.FuelRate; TyreWear = o.TyreWear; Damage = o.Damage; AiCount = o.AiCount; AiLevel = o.AiLevel;
            RaceOverSeconds = o.RaceOverSeconds; PitLimitKmh = o.PitLimitKmh; StartSec = o.StartSec; StartMs = o.StartMs; PhaseSec = o.PhaseSec; PhaseMs = o.PhaseMs; MasterId = o.MasterId;
            Grid.Clear(); Grid.AddRange(o.Grid);
            Allowed.Clear(); Allowed.AddRange(o.Allowed);
        }

        /// <summary>Same content, Seq and MasterId ignored.</summary>
        public bool SameContent(AcNetCommand o)
        {
            if (o == null) return false;
            if (Phase != o.Phase || TrackHash != o.TrackHash || LayoutHash != o.LayoutHash || Session != o.Session || Laps != o.Laps || Minutes != o.Minutes || Abs != o.Abs
                || Tc != o.Tc || Stability != o.Stability || AutoClutch != o.AutoClutch || AutoShift != o.AutoShift || Blankets != o.Blankets || TyresOut != o.TyresOut
                || JumpStart != o.JumpStart || FuelRate != o.FuelRate || TyreWear != o.TyreWear || Damage != o.Damage || AiCount != o.AiCount || AiLevel != o.AiLevel
                || RaceOverSeconds != o.RaceOverSeconds || Math.Abs(PitLimitKmh - o.PitLimitKmh) > 0.05f || StartSec != o.StartSec || StartMs != o.StartMs || PhaseSec != o.PhaseSec || PhaseMs != o.PhaseMs
                || Grid.Count != o.Grid.Count || Allowed.Count != o.Allowed.Count) return false;
            for (int i = 0; i < Grid.Count; i++) if (Grid[i] != o.Grid[i]) return false;
            for (int i = 0; i < Allowed.Count; i++) if (Allowed[i] != o.Allowed[i]) return false;
            return true;
        }
    }

    /// <summary>Live race state of the master ("Ac Net Race 0"): the start-light sequence on the shared UTC clock and the session clocks.</summary>
    public struct AcNetRaceState
    {
        /// <summary>The first red light (UTC seconds; 0 = none) and the step between lights (ms); <see cref="Reds"/> lights in all.</summary>
        public double LightsUtc;
        public int Reds, StepMs;
        /// <summary>The session clock's start (timed sessions) and the moment the leader took the flag (UTC seconds; 0 = none).</summary>
        public double SessionUtc, LeaderFinishedUtc;
        /// <summary>Free bits for R (flags: yellow, red flag, ... - R's numbering).</summary>
        public int Flags;
    }

    /// <summary>One line of the master's results ("Ac Net World 2..3").</summary>
    public struct AcNetResult
    {
        /// <summary>Photon id, AI = -(100 + index) (AcStanding.PlayerId convention).</summary>
        public int Id;
        public int Position;
        public bool Finished;
        public int TotalMs;
        public int BestMs;
    }

    /// <summary>
    /// The master's blocks, encoded and decoded (pure). Layouts (the header is HaloNetWorld's 7 ints; AI blocks a 4-int header):
    /// <code>
    /// World / Race header: 0 magic 'ACW1' | 1 seq | 2 stamp | 3 writer PlayerId+1 (0-15), block (16-19), kind (20-23), protocol (24-27) |
    ///                      4 track hash of the command | 5 command Seq | 6 command start, UTC seconds
    /// World 0 payload: 0 phase u8 | session u8 | ABS 2b (16) | TC 2b (18) | stability 20 | autoclutch 21 | autoshift 22 | blankets 23 | jump start u8 (24)
    ///                  1 track hash  2 layout hash  3 laps u16 | minutes u16  4 tyres out +1 u8 | AI count u8 | AI level u8
    ///                  5 fuel rate u16 | tyre wear u16  6 damage u16 | race over seconds u16  7 pit limit x10 u16 | allowed count u16
    ///                  8 start UTC seconds  9 start ms  10 master PlayerId+1  11 grid count  12 phase UTC seconds  13 phase ms
    ///                  14-56 allowed car hashes (43)
    /// World 1 payload: 0-33 grid codes, two u16 per int (68)   34-56 allowed car hashes 44..66 (23)
    /// World 2 payload: 0 result count | 1-56 results 0..27 (2 ints each)   World 3 payload: 0-55 results 28..55
    ///                  result: code u16 (AcNetWire.GridCode) | position u8 (16) | finished (24) ; total ms   (best laps come from each record)
    /// Race 0 payload:  0 lights UTC s  1 ms  2 reds u8 | step ms u16 (8)  3 session UTC s  4 ms  5 leader finished UTC s  6 ms  7 flags
    /// Race 1 payload:  0-16 penalties u8 x 68 (player slot 0..49, then AI 0..17): seconds 0..254, 255 = drive-through
    /// Ai block:        0 magic 'ACA1' | 1 seq | 2 stamp (master physics ms) | 3 writer+1 (0-15), block (16-19), protocol (24-27); cars at 4 and 34:
    ///                  26 car ints (AcNetWire car block) | 26 AI index u8 | level u8 | names.txt line + 1 u8 (0 none) | spare | 27-29 name (12 Latin-1 chars)
    /// </code>
    /// </summary>
    public static class AcNetBlocks
    {
        public const int MagicWorld = 0x41435731;   // 'ACW1'
        public const int MagicAi = 0x41434131;      // 'ACA1'
        /// <summary>Block layout version (header int 3 bits 24-27), always the record protocol: AI cars use its car packing. A block of another
        /// protocol is ignored (HeaderOk / AiHeaderOk / a take-over copies nothing from it).</summary>
        public const int Protocol = AcNetWire.Protocol;
        public const int Header = 7;
        public const int PerBlock = AcNetSlots.Ints - Header;     // 57
        public const int AiHeader = 4;
        public const int AiCarInts = 30;
        public const int AiPerBlock = 2;
        public const int MaxAi = AcNetSlots.AiCount * AiPerBlock; // 18
        public const int MaxGrid = 68;
        public const int AllowedInW0 = 43;
        public const int MaxAllowed = AllowedInW0 + 23;
        public const int MaxResults = 28 + 28;
        public const int Penalties = AcNetWire.MaxSlots + MaxAi;  // 68
        public const int DriveThrough = 255;

        /// <summary>Block kinds in header int 3 bits 20-23.</summary>
        public const int KindWorld = 1, KindRace = 2;

        /// <summary>The header; <paramref name="bump"/> = new content (seq + 1, new stamp), else only the identity ints are (re)set, so a forced
        /// rewrite of unchanged content writes nothing (Fusion sends changed ints only).</summary>
        public static void WriteHeader(int[] o, int block, int kind, int writerId, int stampMs, int trackHash, int cmdSeq, int startSec, bool bump)
        {
            o[0] = MagicWorld;
            if (bump || o[1] == 0)
            {
                o[1]++;
                o[2] = stampMs;
            }

            o[3] = ((writerId + 1) & 0xFFFF) | ((block & 0xF) << 16) | ((kind & 0xF) << 20) | (Protocol << 24);
            o[4] = trackHash;
            o[5] = cmdSeq;
            o[6] = startSec;
        }

        public static void WriteHeader(int[] o, int block, int kind, int writerId, int stampMs, AcNetCommand cmd)
        {
            WriteHeader(o, block, kind, writerId, stampMs, cmd != null ? cmd.TrackHash : 0, cmd != null ? cmd.Seq : 0, cmd != null ? cmd.StartSec : 0, true);
        }

        public static bool HeaderOk(int[] rec, int block, int kind)
        {
            return rec != null && rec[0] == MagicWorld && ((rec[3] >> 16) & 0xF) == block && ((rec[3] >> 20) & 0xF) == kind && ((rec[3] >> 24) & 0xF) == Protocol;
        }

        public static int WriterOf(int[] rec) { return rec != null && (rec[0] == MagicWorld || rec[0] == MagicAi) ? (rec[3] & 0xFFFF) - 1 : -1; }

        public static int CommandSeqOf(int[] rec) { return rec != null && rec[0] == MagicWorld ? rec[5] : 0; }

        // ------------------------------------------------------------------ the command (World 0 + 1)

        public static void PackCommand(AcNetCommand c, int[] w0, int[] w1)
        {
            int h = Header;
            Array.Clear(w0, h, PerBlock);
            Array.Clear(w1, h, PerBlock);
            w0[h + 0] = (c.Phase & 0xFF) | ((c.Session & 0xFF) << 8) | ((c.Abs & 3) << 16) | ((c.Tc & 3) << 18) | (c.Stability ? 1 << 20 : 0) | (c.AutoClutch ? 1 << 21 : 0)
                        | (c.AutoShift ? 1 << 22 : 0) | (c.Blankets ? 1 << 23 : 0) | ((c.JumpStart & 0xFF) << 24);
            w0[h + 1] = c.TrackHash;
            w0[h + 2] = c.LayoutHash;
            w0[h + 3] = AcNetWire.Two16(AcNetWire.Clamp(c.Laps, 0, 0xFFFF), AcNetWire.Clamp(c.Minutes, 0, 0xFFFF));
            w0[h + 4] = (AcNetWire.Clamp(c.TyresOut + 1, 0, 255)) | (AcNetWire.Clamp(c.AiCount, 0, 255) << 8) | (AcNetWire.Clamp(c.AiLevel, 0, 255) << 16);
            w0[h + 5] = AcNetWire.Two16(AcNetWire.Clamp(c.FuelRate, 0, 0xFFFF), AcNetWire.Clamp(c.TyreWear, 0, 0xFFFF));
            w0[h + 6] = AcNetWire.Two16(AcNetWire.Clamp(c.Damage, 0, 0xFFFF), AcNetWire.Clamp(c.RaceOverSeconds, 0, 0xFFFF));
            int allowed = Math.Min(c.Allowed.Count, MaxAllowed);
            w0[h + 7] = AcNetWire.Two16(AcNetWire.Clamp((int)Math.Round(c.PitLimitKmh * 10f), 0, 0xFFFF), allowed);
            w0[h + 8] = c.StartSec;
            w0[h + 9] = c.StartMs;
            w0[h + 10] = c.MasterId >= 0 ? c.MasterId + 1 : 0;
            int grid = Math.Min(c.Grid.Count, MaxGrid);
            w0[h + 11] = grid;
            w0[h + 12] = c.PhaseSec;
            w0[h + 13] = c.PhaseMs;
            for (int i = 0; i < allowed; i++)
            {
                if (i < AllowedInW0) w0[h + 14 + i] = c.Allowed[i];
                else w1[h + 34 + (i - AllowedInW0)] = c.Allowed[i];
            }

            for (int i = 0; i < grid; i++)
            {
                int code = AcNetWire.GridCode(c.Grid[i]) & 0xFFFF;
                int at = h + (i >> 1);
                if ((i & 1) == 0) w1[at] = (w1[at] & unchecked((int)0xFFFF0000)) | code;
                else w1[at] = (w1[at] & 0xFFFF) | (code << 16);
            }
        }

        /// <summary>World 0 (+ World 1 when given) into a command. False when block 0 is not a command block.</summary>
        public static bool UnpackCommand(int[] w0, int[] w1, AcNetCommand c)
        {
            if (!HeaderOk(w0, 0, KindWorld)) return false;
            int h = Header;
            int a = w0[h + 0];
            c.Seq = w0[5];
            c.Phase = a & 0xFF;
            c.Session = (a >> 8) & 0xFF;
            c.Abs = (a >> 16) & 3;
            c.Tc = (a >> 18) & 3;
            c.Stability = (a & (1 << 20)) != 0;
            c.AutoClutch = (a & (1 << 21)) != 0;
            c.AutoShift = (a & (1 << 22)) != 0;
            c.Blankets = (a & (1 << 23)) != 0;
            c.JumpStart = (a >> 24) & 0xFF;
            c.TrackHash = w0[h + 1];
            c.LayoutHash = w0[h + 2];
            c.Laps = w0[h + 3] & 0xFFFF;
            c.Minutes = (w0[h + 3] >> 16) & 0xFFFF;
            c.TyresOut = (w0[h + 4] & 0xFF) - 1;
            c.AiCount = (w0[h + 4] >> 8) & 0xFF;
            c.AiLevel = (w0[h + 4] >> 16) & 0xFF;
            c.FuelRate = w0[h + 5] & 0xFFFF;
            c.TyreWear = (w0[h + 5] >> 16) & 0xFFFF;
            c.Damage = w0[h + 6] & 0xFFFF;
            c.RaceOverSeconds = (w0[h + 6] >> 16) & 0xFFFF;
            c.PitLimitKmh = (w0[h + 7] & 0xFFFF) / 10f;
            int allowed = (w0[h + 7] >> 16) & 0xFFFF;
            c.StartSec = w0[h + 8];
            c.StartMs = w0[h + 9];
            c.MasterId = w0[h + 10] - 1;
            int grid = w0[h + 11];
            c.PhaseSec = w0[h + 12];
            c.PhaseMs = w0[h + 13];
            bool haveW1 = w1 != null && HeaderOk(w1, 1, KindWorld) && w1[5] == w0[5];
            c.Allowed.Clear();
            for (int i = 0; i < allowed && i < MaxAllowed; i++)
            {
                if (i < AllowedInW0) c.Allowed.Add(w0[h + 14 + i]);
                else if (haveW1) c.Allowed.Add(w1[h + 34 + (i - AllowedInW0)]);
            }

            c.Grid.Clear();
            if (haveW1)
            {
                for (int i = 0; i < grid && i < MaxGrid; i++)
                {
                    int v = w1[h + (i >> 1)];
                    int code = (i & 1) == 0 ? v & 0xFFFF : (v >> 16) & 0xFFFF;
                    int id = AcNetWire.GridId(code);
                    if (id != int.MinValue) c.Grid.Add(id);
                }
            }

            return true;
        }

        /// <summary>Whether World 1 must have arrived for this command to be complete (grid or more than 43 allowed cars).</summary>
        public static bool NeedsWorld1(int[] w0)
        {
            return w0[Header + 11] > 0 || ((w0[Header + 7] >> 16) & 0xFFFF) > AllowedInW0;
        }

        // ------------------------------------------------------------------ results (World 2 + 3)

        public static void PackResults(IReadOnlyList<AcNetResult> results, int count, int[] w2, int[] w3)
        {
            int h = Header;
            Array.Clear(w2, h, PerBlock);
            Array.Clear(w3, h, PerBlock);
            int n = Math.Min(count, MaxResults);
            w2[h] = n;
            for (int i = 0; i < n; i++)
            {
                AcNetResult r = results[i];
                int code = (AcNetWire.GridCode(r.Id) & 0xFFFF) | (AcNetWire.Clamp(r.Position, 0, 255) << 16) | (r.Finished ? 1 << 24 : 0);
                if (i < 28) { w2[h + 1 + i * 2] = code; w2[h + 2 + i * 2] = Math.Max(0, r.TotalMs); }
                else { int j = i - 28; w3[h + j * 2] = code; w3[h + 1 + j * 2] = Math.Max(0, r.TotalMs); }
            }
        }

        public static int UnpackResults(int[] w2, int[] w3, bool haveW3, AcNetResult[] into)
        {
            int h = Header;
            int n = Math.Min(Math.Min(w2[h], MaxResults), into.Length);
            int got = 0;
            for (int i = 0; i < n; i++)
            {
                int code, total;
                if (i < 28) { code = w2[h + 1 + i * 2]; total = w2[h + 2 + i * 2]; }
                else if (haveW3) { int j = i - 28; code = w3[h + j * 2]; total = w3[h + 1 + j * 2]; }
                else break;
                int id = AcNetWire.GridId(code & 0xFFFF);
                if (id == int.MinValue) continue;
                into[got++] = new AcNetResult { Id = id, Position = (code >> 16) & 0xFF, Finished = (code & (1 << 24)) != 0, TotalMs = total };
            }

            return got;
        }

        // ------------------------------------------------------------------ race state (Race 0) and penalties (Race 1)

        public static void PackRaceState(in AcNetRaceState s, int[] r0)
        {
            int h = Header;
            Array.Clear(r0, h, PerBlock);
            Utc(s.LightsUtc, out r0[h + 0], out r0[h + 1]);
            r0[h + 2] = (AcNetWire.Clamp(s.Reds, 0, 255)) | (AcNetWire.Clamp(s.StepMs, 0, 0xFFFF) << 8);
            Utc(s.SessionUtc, out r0[h + 3], out r0[h + 4]);
            Utc(s.LeaderFinishedUtc, out r0[h + 5], out r0[h + 6]);
            r0[h + 7] = s.Flags;
        }

        public static AcNetRaceState UnpackRaceState(int[] r0)
        {
            int h = Header;
            return new AcNetRaceState
            {
                LightsUtc = FromUtc(r0[h + 0], r0[h + 1]),
                Reds = r0[h + 2] & 0xFF,
                StepMs = (r0[h + 2] >> 8) & 0xFFFF,
                SessionUtc = FromUtc(r0[h + 3], r0[h + 4]),
                LeaderFinishedUtc = FromUtc(r0[h + 5], r0[h + 6]),
                Flags = r0[h + 7],
            };
        }

        public static void PackPenalties(byte[] values, int[] r1)
        {
            int h = Header;
            Array.Clear(r1, h, PerBlock);
            for (int i = 0; i < Penalties && i < values.Length; i++) r1[h + (i >> 2)] |= values[i] << (8 * (i & 3));
        }

        public static void UnpackPenalties(int[] r1, byte[] into)
        {
            int h = Header;
            for (int i = 0; i < Penalties && i < into.Length; i++) into[i] = (byte)((r1[h + (i >> 2)] >> (8 * (i & 3))) & 0xFF);
        }

        private static void Utc(double utc, out int sec, out int ms)
        {
            if (!(utc > 0.0)) { sec = 0; ms = 0; return; }
            double s = Math.Floor(utc);
            sec = (int)s;
            ms = (int)Math.Round((utc - s) * 1000.0);
            if (ms >= 1000) { sec++; ms -= 1000; }
        }

        private static double FromUtc(int sec, int ms) { return sec == 0 && ms == 0 ? 0.0 : sec + ms / 1000.0; }

        // ------------------------------------------------------------------ AI blocks

        public static void WriteAiHeader(int[] o, int block, int writerId, int stampMs)
        {
            o[0] = MagicAi;
            o[1]++;
            o[2] = stampMs;
            o[3] = ((writerId + 1) & 0xFFFF) | ((block & 0xF) << 16) | (Protocol << 24);
        }

        public static bool AiHeaderOk(int[] rec, int block)
        {
            return rec != null && rec[0] == MagicAi && ((rec[3] >> 16) & 0xF) == block && ((rec[3] >> 24) & 0xF) == Protocol;
        }

        /// <summary>One AI car into its 30 ints (car <paramref name="slot"/> 0/1 of the block). True when anything changed.</summary>
        public static bool PackAi(int[] block, int slot, in AcNetCar car, int index, int level, string name)
        {
            return PackAi(block, slot, in car, index, level, name, 0);
        }

        /// <summary>As above with <paramref name="nameLine"/> = 1 + the line of content/career/names.txt the name comes from (0 = none): every client
        /// has the same file, so the full name travels in one byte (27 of its 39 names are longer than the 12 characters of the text).</summary>
        public static bool PackAi(int[] block, int slot, in AcNetCar car, int index, int level, string name, int nameLine)
        {
            int at = AiHeader + slot * AiCarInts;
            bool any = false;
            AcNetWire.PackCar(in car, block, at, ref any);
            any |= AcNetWire.Set(block, at + 26, (index & 0xFF) | (AcNetWire.Clamp(level, 0, 255) << 8) | (AcNetWire.Clamp(nameLine, 0, 255) << 16));
            AcNetWire.PackText(name, block, at + 27, 3, ref any);
            return any;
        }

        public static void ClearAi(int[] block, int slot)
        {
            Array.Clear(block, AiHeader + slot * AiCarInts, AiCarInts);
        }

        // ------------------------------------------------------------------ AI damage table ("Ac Net Host 0", protocol 3; DAMAGE_DESIGN 7)

        /// <summary>'ACD1': the AI damage table in "Ac Net Host 0" (master-written).</summary>
        public const int MagicAiDamage = 0x41434431;
        /// <summary>Header ints of the AI damage table: 0 magic | 1 seq | 2 stamp | 3 writer+1 (0-15), block (16-19), protocol (24-27).</summary>
        public const int AiDamageHeader = 4;
        /// <summary>Ints used by the table: the header + 18 AI x the damage pair (d0, d1) by AI index = 40.</summary>
        public const int AiDamageInts = AiDamageHeader + 2 * MaxAi;

        /// <summary>Writes the pair of AI car <paramref name="aiIndex"/> (0..17) into the table; true when an int changed. d0 / d1 =
        /// AcCarDamage.ToWire (all zero = undamaged).</summary>
        public static bool PackAiDamage(int[] block, int aiIndex, int d0, int d1)
        {
            if (block == null || aiIndex < 0 || aiIndex >= MaxAi || block.Length < AiDamageInts) return false;
            int at = AiDamageHeader + 2 * aiIndex;
            bool any = AcNetWire.Set(block, at, d0);
            any |= AcNetWire.Set(block, at + 1, d1);
            return any;
        }

        /// <summary>Reads the pair of AI car <paramref name="aiIndex"/> (zeros for a bad index / short block).</summary>
        public static void UnpackAiDamage(int[] block, int aiIndex, out int d0, out int d1)
        {
            d0 = 0; d1 = 0;
            if (block == null || aiIndex < 0 || aiIndex >= MaxAi || block.Length < AiDamageInts) return;
            int at = AiDamageHeader + 2 * aiIndex;
            d0 = block[at];
            d1 = block[at + 1];
        }

        public static void WriteAiDamageHeader(int[] o, int block, int writerId, int stampMs)
        {
            o[0] = MagicAiDamage;
            o[1]++;
            o[2] = stampMs;
            o[3] = ((writerId + 1) & 0xFFFF) | ((block & 0xF) << 16) | (Protocol << 24);
        }

        /// <summary>The block is an AI damage table of this protocol (a protocol-2 build never writes Host blocks; another protocol is ignored).</summary>
        public static bool AiDamageHeaderOk(int[] rec, int block)
        {
            return rec != null && rec.Length >= AiDamageInts && rec[0] == MagicAiDamage && ((rec[3] >> 16) & 0xF) == block && ((rec[3] >> 24) & 0xF) == Protocol;
        }

        /// <summary>Writer PlayerId of an AI damage table (-1 none).</summary>
        public static int AiDamageWriterOf(int[] rec) { return rec != null && rec[0] == MagicAiDamage ? (rec[3] & 0xFFFF) - 1 : -1; }
    }
}
