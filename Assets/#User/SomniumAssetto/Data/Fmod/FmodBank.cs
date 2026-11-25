using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>content/sfx/GUIDs.txt (and a mod car's own sfx/GUIDs.txt): "{guid} event:/cars/x/engine_ext" lines.</summary>
    public sealed class FmodGuids
    {
        /// <summary>GUID -> every path written for it (variants share event GUIDs: ferrari_458 and ferrari_458_s3).</summary>
        public readonly Dictionary<Guid, List<string>> Paths = new Dictionary<Guid, List<string>>();

        public static FmodGuids Load(string path)
        {
            var g = new FmodGuids();
            g.AddFile(path);
            return g;
        }

        public void AddFile(string path)
        {
            string t = AcText.ReadFile(path);
            if (t == null) return;
            var lines = t.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length < 40 || l[0] != '{' || l[37] != '}') continue;
                Guid id;
                if (!Guid.TryParse(l.Substring(1, 36), out id)) continue;
                string p = l.Substring(38).Trim();
                List<string> list;
                if (!Paths.TryGetValue(id, out list)) { list = new List<string>(1); Paths[id] = list; }
                if (!list.Contains(p)) list.Add(p);
            }
        }

        /// <summary>Path for a GUID, preferring one that contains <paramref name="prefer"/> (e.g. "/cars/ferrari_458/"); null if unknown.</summary>
        public string PathOf(Guid id, string prefer = null)
        {
            List<string> list;
            if (!Paths.TryGetValue(id, out list) || list.Count == 0) return null;
            if (prefer != null)
                for (int i = 0; i < list.Count; i++) if (list[i].IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0) return list[i];
            return list[0];
        }
    }

    public sealed class FmodParameter
    {
        public Guid Id;
        public string Name;            // "rpms", "throttle", "speed", "state", "decay", "Distance", "Event Cone Angle" ...
        public float Min, Max, Default;
        public override string ToString() { return Name + "[" + Min + ".." + Max + "]"; }
    }

    /// <summary>An FMOD 1.08 curve: (x, y, shape) points; evaluated piecewise linearly and clamped (shape ignored, as ETS did).</summary>
    public sealed class FmodCurve
    {
        public float[] X = new float[0], Y = new float[0], Shape = new float[0];

        public float Eval(float x)
        {
            int n = X.Length;
            if (n == 0) return 0f;
            if (x <= X[0]) return Y[0];
            if (x >= X[n - 1]) return Y[n - 1];
            for (int i = 1; i < n; i++)
                if (x <= X[i])
                {
                    float s = X[i] - X[i - 1];
                    return s <= 1e-6f ? Y[i] : Y[i - 1] + (Y[i] - Y[i - 1]) * (x - X[i - 1]) / s;
                }
            return Y[n - 1];
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < X.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(X[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('>').Append(Y[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }
    }

    /// <summary>A CTRL automation: a curve of one property of an instrument or bus along a parameter axis.</summary>
    public sealed class FmodAutomation
    {
        public Guid Owner;
        public FmodParameter Parameter;
        /// <summary>4 = instrument volume (linear 0..1), 0 = volume in dB (bus/track), 1 = pitch, 0xE8 = effect.</summary>
        public int Property;
        public FmodCurve Curve;
        /// <summary>True when the owner is a bus of the instrument's output chain (not the instrument itself).</summary>
        public bool OnBus;

        public const int PropVolumeDb = 0, PropPitch = 1, PropVolumeLinear = 4;
        public string ParamName { get { return Parameter != null ? Parameter.Name : "?"; } }
    }

    /// <summary>A waveform instrument of an event (WAIB + INST), placed on a parameter sheet (PMLB) or a timeline (TLNB).</summary>
    public sealed class FmodInstrument
    {
        public Guid Id, Wave, Bus;
        /// <summary>FSB5 sample index (-1 when the waveform was not found) and its name.</summary>
        public int Sample = -1;
        public string SampleName;
        public float VolumeDb, PitchSemis;
        /// <summary>-1 = loop forever, 0 = one-shot.</summary>
        public int LoopCount;
        public FmodParameter AutoPitchParam;
        /// <summary>Auto-pitch reference value (rpm for engines: the sample plays at its own pitch at this rpm). 1 when off.</summary>
        public float AutoPitchRef = 1f;
        /// <summary>Sheet placement (null param = timeline instrument).</summary>
        public FmodParameter RegionParam;
        public float RegionStart, RegionLength;
        /// <summary>Timeline placement in 48 kHz frames (timeline events: skid, horn ...); -1 when on a sheet.</summary>
        public long TimelinePos = -1, TimelineLength = -1;
        /// <summary>Instrument automations (CTRL owner == this) followed by every bus automation up the output chain.</summary>
        public readonly List<FmodAutomation> Automations = new List<FmodAutomation>();

        public bool Loops { get { return LoopCount < 0; } }
        public float RegionEnd { get { return RegionStart + RegionLength; } }

        /// <summary>
        /// Linear gain at the given parameter values (allocation-free): dB(VolumeDb) x every linear-volume automation x every dB-volume
        /// automation (instrument and buses) whose axis is one of the given parameters. Region test on <see cref="RegionParam"/>.
        /// Parameter names not passed (e.g. "Distance") are ignored.
        /// </summary>
        public float Gain(string p0, float v0, string p1 = null, float v1 = 0f)
        {
            if (RegionParam != null)
            {
                float rv; if (Value(RegionParam.Name, p0, v0, p1, v1, out rv) && (rv < RegionStart || rv > RegionEnd)) return 0f;
            }
            float g = FmodBank.DbToLinear(VolumeDb);
            for (int i = 0; i < Automations.Count; i++)
            {
                var a = Automations[i];
                if (a.Parameter == null || a.Curve == null) continue;
                float x;
                if (!Value(a.Parameter.Name, p0, v0, p1, v1, out x)) continue;
                if (a.Property == FmodAutomation.PropVolumeLinear) g *= Clamp01(a.Curve.Eval(x));
                else if (a.Property == FmodAutomation.PropVolumeDb) g *= FmodBank.DbToLinear(a.Curve.Eval(x));
            }
            return g;
        }

        /// <summary>Playback rate: value / AutoPitchRef (auto-pitch, minimum pitch 0 [BELIEF]) x 2^(semitones/12) x pitch automations.</summary>
        public float Pitch(string p0, float v0, string p1 = null, float v1 = 0f)
        {
            float rate = (float)Math.Pow(2.0, PitchSemis / 12.0);
            if (AutoPitchParam != null && AutoPitchRef > 0f)
            {
                float x; if (Value(AutoPitchParam.Name, p0, v0, p1, v1, out x)) rate *= x / AutoPitchRef;
            }
            for (int i = 0; i < Automations.Count; i++)
            {
                var a = Automations[i];
                if (a.Property != FmodAutomation.PropPitch || a.Parameter == null || a.Curve == null) continue;
                float x;
                if (Value(a.Parameter.Name, p0, v0, p1, v1, out x)) rate *= (float)Math.Pow(2.0, a.Curve.Eval(x) / 12.0); // [BELIEF] semitones
            }
            return rate;
        }

        static bool Value(string name, string p0, float v0, string p1, float v1, out float v)
        {
            if (p0 != null && string.Equals(name, p0, StringComparison.Ordinal)) { v = v0; return true; }
            if (p1 != null && string.Equals(name, p1, StringComparison.Ordinal)) { v = v1; return true; }
            v = 0f; return false;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        public override string ToString() { return SampleName + " " + (RegionParam != null ? RegionParam.Name + " " + RegionStart + ".." + RegionEnd : "timeline") + " ref " + AutoPitchRef + " vol " + VolumeDb + "dB"; }
    }

    /// <summary>A parameter sheet of an event: instruments laid out along one parameter (empty = automation-only sheet).</summary>
    public sealed class FmodSheet
    {
        public FmodParameter Parameter;
        public readonly List<FmodInstrument> Instruments = new List<FmodInstrument>();
        public bool AutomationOnly { get { return Instruments.Count == 0; } }
    }

    public sealed class FmodEvent
    {
        public Guid Id;
        /// <summary>"event:/cars/ferrari_458/engine_ext" (from GUIDs.txt), or the GUID text when unknown.</summary>
        public string Path;
        /// <summary>Last path segment: "engine_ext", "door", "kerb" ...</summary>
        public string Name;
        public readonly List<FmodSheet> Sheets = new List<FmodSheet>();
        /// <summary>Instruments placed on the event timeline (TLNB).</summary>
        public readonly List<FmodInstrument> Timeline = new List<FmodInstrument>();
        /// <summary>Parameters of the event (EVTB list when decodable, else the sheet axes).</summary>
        public readonly List<FmodParameter> Parameters = new List<FmodParameter>();

        /// <summary>Every instrument of the event (sheets then timeline).</summary>
        public List<FmodInstrument> AllInstruments
        {
            get
            {
                var l = new List<FmodInstrument>();
                for (int i = 0; i < Sheets.Count; i++) l.AddRange(Sheets[i].Instruments);
                l.AddRange(Timeline);
                return l;
            }
        }

        public override string ToString() { return Path; }
    }

    /// <summary>Which throttle bus an engine layer sits on.</summary>
    public enum FmodEngineLoad { Neutral = 0, OnLoad = 1, OffLoad = 2 }

    /// <summary>
    /// One looping engine sample of engine_int / engine_ext, ready for an AudioSource: active rpm range, pitch reference rpm,
    /// load/coast bus, and the gain/pitch formulas (ac_physics_sound_rules 2.5: gain = dB(INST vol) x instrument rpm crossfades x bus
    /// curves on throttle/rpm; pitch = rpm / ref).
    /// </summary>
    public sealed class FmodEngineLayer
    {
        public FmodInstrument Instrument;
        public string SampleName;
        public int Sample;
        public float RpmMin, RpmMax;
        public float PitchRefRpm;
        public float VolumeDb;
        public FmodEngineLoad Load;
        /// <summary>The rpm parameter name ("rpms") and throttle name ("throttle") used in the event.</summary>
        public string RpmParam = "rpms", ThrottleParam = "throttle";

        /// <summary>Linear gain at (rpm, throttle 0..1). 0 outside the rpm region. Allocation-free.</summary>
        public float Gain(float rpm, float throttle) { return Instrument.Gain(RpmParam, rpm, ThrottleParam, throttle); }

        /// <summary>Playback rate (rpm / PitchRefRpm, with any semitone offset).</summary>
        public float Pitch(float rpm) { return Instrument.Pitch(RpmParam, rpm, ThrottleParam, 0f); }

        public override string ToString() { return SampleName + " " + RpmMin + ".." + RpmMax + " ref " + PitchRefRpm + " " + Load + " " + VolumeDb + "dB"; }
    }

    /// <summary>
    /// An Assetto Corsa FMOD Studio 1.08 bank (<c>content/cars/&lt;car&gt;/sfx/&lt;car&gt;.bank</c>, <c>content/sfx/common.bank</c>):
    /// RIFF "FEV " container with the LIST PROJ metadata and the embedded FSB5 (SND chunk), decoded per ac_physics_sound_rules 2.2-2.6
    /// [VERIFIED by the acev probe on 86/86 car banks + common.bank; this reader re-runs in the harness].
    ///
    /// Reading streams: only the small metadata chunks are loaded (~50 KB); the 50 MB of PCM stays on disk and is read per sample
    /// (<see cref="ReadSampleFloats"/>). Chunk layouts (offsets from chunk data start): PRMB parameter (u16 name length @21, name @23,
    /// min/max/default after), WAV (FSB5 index u16 @22), WAIB (instrument guid, waveform guid @16) followed by INST (volume dB @16,
    /// pitch @20, loop @24, auto-pitch parameter guid @53, reference @69, output bus @83), PMLB sheet (parameter @16, event @32,
    /// entries {instrument, start, length} @52 stride 24), TLNB timeline (event @16, entries stride @36 from @38: instrument, u32 pos,
    /// u32 length in 48 kHz frames), CTRL (owner @16, parameter @32, curve @48, property @64), CURV (points @36 stride @34),
    /// GBSB/MBSB/IBSB bus (parent @18), EVTB event (guid @0).
    /// </summary>
    public sealed class FmodBank
    {
        public string Path;
        public int FormatVersion, FormatSub;    // FMT chunk: 0x50 / 0x46 in AC banks (FMOD Studio 1.08)
        public long SndOffset = -1, SndSize;
        public long Fsb5Offset = -1;
        public Fsb5 Fsb;
        public readonly Dictionary<Guid, FmodParameter> Parameters = new Dictionary<Guid, FmodParameter>();
        public readonly Dictionary<Guid, FmodInstrument> Instruments = new Dictionary<Guid, FmodInstrument>();
        public readonly List<FmodEvent> Events = new List<FmodEvent>();
        /// <summary>Count of each chunk id (diagnostics).</summary>
        public readonly Dictionary<string, int> ChunkCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Sheet/timeline entries whose instrument is not a waveform instrument (multi/scatterer, not decoded).</summary>
        public int UnresolvedEntries;
        /// <summary>Instrument GUIDs of the unresolved entries (diagnostics).</summary>
        public readonly List<Guid> UnresolvedIds = new List<Guid>();

        struct Chunk { public string Id; public byte[] Data; }

        public static float DbToLinear(float db) { return db <= -80f ? 0f : (float)Math.Pow(10.0, db / 20.0); }

        /// <summary>
        /// Reads a bank. <paramref name="guids"/> names the events (content/sfx/GUIDs.txt, plus the car's own sfx/GUIDs.txt if any);
        /// <paramref name="preferPath"/> picks between shared GUIDs ("/cars/ferrari_458_s3/").
        /// </summary>
        public static FmodBank Read(string path, FmodGuids guids = null, string preferPath = null)
        {
            using (var r = AcBinary.OpenFile(path, 1 << 16))
            {
                var b = Read(r, guids, preferPath);
                b.Path = path;
                return b;
            }
        }

        public static FmodBank Read(AcBinary r, FmodGuids guids, string preferPath)
        {
            var bank = new FmodBank();
            if (r.Length < 12) throw new InvalidDataException("bank too small");
            string riff = r.ReadAscii(4);
            r.ReadInt32();
            string fev = r.ReadAscii(4);
            var chunks = new List<Chunk>();
            if (riff == "RIFF" && fev == "FEV ")
            {
                bank.Walk(r, 12, r.Length, chunks, 0);
                if (bank.SndOffset >= 0) bank.Fsb5Offset = Fsb5.FindMagic(r, bank.SndOffset, 4096);
            }
            if (bank.Fsb5Offset < 0) bank.Fsb5Offset = Fsb5.FindMagic(r, 0, r.Length);
            if (bank.Fsb5Offset >= 0) bank.Fsb = Fsb5.Read(r, bank.Fsb5Offset);
            bank.Parse(chunks, guids, preferPath);
            return bank;
        }

        /// <summary>Offset of the FSB5 inside a bank (or 0 for a bare .fsb); -1 if none.</summary>
        public static long FindFsb5(AcBinary r)
        {
            r.Position = 0;
            if (r.Length >= 12 && r.ReadAscii(4) == "RIFF")
            {
                var b = new FmodBank();
                r.ReadInt32();
                if (r.ReadAscii(4) == "FEV ")
                {
                    b.Walk(r, 12, r.Length, null, 0);
                    if (b.SndOffset >= 0) { long at = Fsb5.FindMagic(r, b.SndOffset, 4096); if (at >= 0) return at; }
                }
            }
            return Fsb5.FindMagic(r, 0, r.Length);
        }

        void Walk(AcBinary r, long p, long end, List<Chunk> chunks, int depth)
        {
            if (depth > 16) return;
            while (p + 8 <= end)
            {
                r.Position = p;
                string id = r.ReadAscii(4);
                long sz = r.ReadUInt32();
                if (p + 8 + sz > end) return;
                int cnt; ChunkCounts.TryGetValue(id, out cnt); ChunkCounts[id] = cnt + 1;
                if (id == "LIST") Walk(r, p + 12, p + 8 + sz, chunks, depth + 1);
                else if (id == "SND ") { SndOffset = p + 8; SndSize = sz; }
                else if (id == "FMT " && sz >= 8) { FormatVersion = r.ReadInt32(); FormatSub = r.ReadInt32(); }
                else if (chunks != null && sz <= (1 << 22)) chunks.Add(new Chunk { Id = id, Data = r.ReadBytes((int)sz) });
                p += 8 + sz + (sz & 1);
            }
        }

        static Guid G(byte[] b, int o)
        {
            if (o < 0 || o + 16 > b.Length) return Guid.Empty;
            var t = new byte[16];
            Buffer.BlockCopy(b, o, t, 0, 16);
            return new Guid(t);
        }
        static float F(byte[] b, int o) { return o >= 0 && o + 4 <= b.Length ? BitConverter.ToSingle(b, o) : 0f; }
        static int I32(byte[] b, int o) { return o >= 0 && o + 4 <= b.Length ? BitConverter.ToInt32(b, o) : 0; }
        static int U16(byte[] b, int o) { return o >= 0 && o + 2 <= b.Length ? b[o] | (b[o + 1] << 8) : 0; }

        void Parse(List<Chunk> chunks, FmodGuids guids, string preferPath)
        {
            var wav = new Dictionary<Guid, int>();
            var busParent = new Dictionary<Guid, Guid>();
            var curves = new Dictionary<Guid, FmodCurve>();
            var ctrls = new List<FmodAutomation>();
            var events = new Dictionary<Guid, FmodEvent>();
            var evtParams = new Dictionary<Guid, List<Guid>>();
            var sheets = new List<KeyValuePair<Guid, FmodSheet>>();      // event -> sheet
            var sheetEntries = new List<List<KeyValuePair<Guid, float[]>>>();
            var timelines = new List<KeyValuePair<Guid, List<KeyValuePair<Guid, long[]>>>>();
            var curveOf = new Dictionary<int, Guid>();                   // ctrl index -> curve guid

            for (int i = 0; i < chunks.Count; i++)
            {
                var d = chunks[i].Data;
                switch (chunks[i].Id)
                {
                    case "PRMB":
                    {
                        int len = U16(d, 21);
                        if (23 + len + 12 > d.Length) break;
                        string n = Encoding.ASCII.GetString(d, 23, len).TrimEnd('\0');
                        int q = 23 + len;
                        Parameters[G(d, 0)] = new FmodParameter { Id = G(d, 0), Name = n, Min = F(d, q), Max = F(d, q + 4), Default = F(d, q + 8) };
                        break;
                    }
                    case "WAV ": wav[G(d, 0)] = U16(d, 22); break;
                    case "WAIB":
                    {
                        if (i + 1 >= chunks.Count || chunks[i + 1].Id != "INST") break;
                        var q = chunks[i + 1].Data;
                        if (q.Length < 99) break;
                        var ins = new FmodInstrument
                        {
                            Id = G(d, 0), Wave = G(d, 16), VolumeDb = F(q, 16), PitchSemis = F(q, 20), LoopCount = I32(q, 24),
                            AutoPitchRef = F(q, 69), Bus = G(q, 83),
                        };
                        Guid ap = G(q, 53);
                        if (ap != Guid.Empty) ins.AutoPitchParam = new FmodParameter { Id = ap }; // resolved below
                        Instruments[ins.Id] = ins;
                        break;
                    }
                    case "GBSB": case "MBSB": case "IBSB": busParent[G(d, 0)] = G(d, 18); break;
                    case "CURV":
                    {
                        int n = (U16(d, 32) - 1) / 2, es = U16(d, 34);
                        if (n < 0 || es < 12 || d.Length < 48) n = 0;
                        else n = Math.Min(n, (d.Length - 48) / es + 1);
                        var c = new FmodCurve { X = new float[n], Y = new float[n], Shape = new float[n] };
                        for (int k = 0; k < n; k++) { c.X[k] = F(d, 36 + k * es); c.Y[k] = F(d, 40 + k * es); c.Shape[k] = F(d, 44 + k * es); }
                        curves[G(d, 0)] = c;
                        break;
                    }
                    case "CTRL":
                        if (d.Length >= 68)
                        {
                            ctrls.Add(new FmodAutomation { Owner = G(d, 16), Parameter = new FmodParameter { Id = G(d, 32) }, Property = I32(d, 64) });
                            curveOf[ctrls.Count - 1] = G(d, 48);
                        }
                        break;
                    case "PMLB":
                    {
                        if (d.Length < 52) break;
                        Guid evt = G(d, 32);
                        var sh = new FmodSheet { Parameter = new FmodParameter { Id = G(d, 16) } };
                        int n = (U16(d, 48) - 1) / 2, es = U16(d, 50);
                        var entries = new List<KeyValuePair<Guid, float[]>>();
                        if (es == 24)
                            for (int k = 0; k < n && 52 + k * 24 + 24 <= d.Length; k++)
                                entries.Add(new KeyValuePair<Guid, float[]>(G(d, 52 + k * 24), new[] { F(d, 68 + k * 24), F(d, 72 + k * 24) }));
                        sheets.Add(new KeyValuePair<Guid, FmodSheet>(evt, sh));
                        sheetEntries.Add(entries);
                        break;
                    }
                    case "TLNB":
                    {
                        if (d.Length < 36) break;
                        Guid evt = G(d, 16);
                        // After the two guids come u16-counted lists ("2n+1", then u16 entry size); an empty list is just "01 00",
                        // and it is optional (skid_ext has a leading empty list, skid_int not). Take the first list with entry
                        // size 24 = {instrument guid, u32 position, u32 length} (48 kHz frames) [VERIFIED on ferrari_458 skid_ext/int, horn].
                        var entries = new List<KeyValuePair<Guid, long[]>>();
                        for (int at = 32; at <= 40 && at + 4 <= d.Length; at += 2)
                        {
                            int cnt = U16(d, at), es = U16(d, at + 2);
                            if ((cnt & 1) == 0 || cnt < 3 || es != 24) continue;
                            int n = (cnt - 1) / 2, e0 = at + 4;
                            for (int k = 0; k < n && e0 + k * es + 24 <= d.Length; k++)
                                entries.Add(new KeyValuePair<Guid, long[]>(G(d, e0 + k * es), new long[] { (uint)I32(d, e0 + 16 + k * es), (uint)I32(d, e0 + 20 + k * es) }));
                            break;
                        }
                        timelines.Add(new KeyValuePair<Guid, List<KeyValuePair<Guid, long[]>>>(evt, entries));
                        break;
                    }
                    case "EVTB":
                    {
                        Guid evt = G(d, 0);
                        if (!events.ContainsKey(evt)) events[evt] = new FmodEvent { Id = evt };
                        if (d.Length >= 97)
                        {
                            int n = (U16(d, 93) - 1) / 2, es = U16(d, 95);
                            if (es == 16 && n > 0 && 97 + n * 16 <= d.Length)
                            {
                                var l = new List<Guid>();
                                for (int k = 0; k < n; k++) l.Add(G(d, 97 + k * 16));
                                evtParams[evt] = l;
                            }
                        }
                        break;
                    }
                }
            }

            // resolve curves and parameters of automations
            for (int i = 0; i < ctrls.Count; i++)
            {
                Guid cg;
                if (curveOf.TryGetValue(i, out cg)) { FmodCurve c; if (curves.TryGetValue(cg, out c)) ctrls[i].Curve = c; }
                ctrls[i].Parameter = Param(ctrls[i].Parameter.Id);
            }
            var byOwner = new Dictionary<Guid, List<FmodAutomation>>();
            for (int i = 0; i < ctrls.Count; i++)
            {
                List<FmodAutomation> l;
                if (!byOwner.TryGetValue(ctrls[i].Owner, out l)) { l = new List<FmodAutomation>(); byOwner[ctrls[i].Owner] = l; }
                l.Add(ctrls[i]);
            }

            // instruments: sample index/name, auto-pitch parameter, own + bus-chain automations
            foreach (var ins in Instruments.Values)
            {
                int si;
                if (wav.TryGetValue(ins.Wave, out si))
                {
                    ins.Sample = si;
                    ins.SampleName = Fsb != null && si >= 0 && si < Fsb.Samples.Count ? Fsb.Samples[si].Name : "#" + si;
                }
                if (ins.AutoPitchParam != null)
                {
                    FmodParameter p;
                    ins.AutoPitchParam = Parameters.TryGetValue(ins.AutoPitchParam.Id, out p) ? p : null;
                }
                if (ins.AutoPitchParam == null) ins.AutoPitchRef = 1f;
                List<FmodAutomation> own;
                if (byOwner.TryGetValue(ins.Id, out own)) ins.Automations.AddRange(own);
                var seen = new HashSet<Guid>();
                for (Guid bus = ins.Bus; bus != Guid.Empty && seen.Add(bus); )
                {
                    List<FmodAutomation> ba;
                    if (byOwner.TryGetValue(bus, out ba))
                        for (int k = 0; k < ba.Count; k++)
                            ins.Automations.Add(new FmodAutomation { Owner = bus, Parameter = ba[k].Parameter, Property = ba[k].Property, Curve = ba[k].Curve, OnBus = true });
                    Guid parent;
                    if (!busParent.TryGetValue(bus, out parent)) break;
                    bus = parent;
                }
            }

            // events: sheets and timelines
            for (int i = 0; i < sheets.Count; i++)
            {
                var ev = EventFor(events, sheets[i].Key);
                var sh = sheets[i].Value;
                sh.Parameter = Param(sh.Parameter.Id);
                var entries = sheetEntries[i];
                for (int k = 0; k < entries.Count; k++)
                {
                    FmodInstrument src;
                    if (!Instruments.TryGetValue(entries[k].Key, out src)) { UnresolvedEntries++; UnresolvedIds.Add(entries[k].Key); continue; }
                    var ins = Placed(src, ev, sh, entries[k].Value[0], entries[k].Value[1]);
                    sh.Instruments.Add(ins);
                }
                ev.Sheets.Add(sh);
            }
            for (int i = 0; i < timelines.Count; i++)
            {
                var ev = EventFor(events, timelines[i].Key);
                var entries = timelines[i].Value;
                for (int k = 0; k < entries.Count; k++)
                {
                    FmodInstrument src;
                    if (!Instruments.TryGetValue(entries[k].Key, out src)) { UnresolvedEntries++; UnresolvedIds.Add(entries[k].Key); continue; }
                    var ins = Placed(src, ev, null, 0f, 0f);
                    ins.TimelinePos = entries[k].Value[0];
                    ins.TimelineLength = entries[k].Value[1];
                    ev.Timeline.Add(ins);
                }
            }

            // names + parameter lists, sorted by path
            foreach (var ev in events.Values)
            {
                string path = guids != null ? guids.PathOf(ev.Id, preferPath) : null;
                ev.Path = path ?? ev.Id.ToString("B");
                int slash = ev.Path.LastIndexOf('/');
                ev.Name = slash >= 0 ? ev.Path.Substring(slash + 1) : ev.Path;
                List<Guid> pl;
                if (evtParams.TryGetValue(ev.Id, out pl))
                {
                    for (int k = 0; k < pl.Count; k++) { FmodParameter p; if (Parameters.TryGetValue(pl[k], out p)) ev.Parameters.Add(p); }
                }
                if (ev.Parameters.Count == 0)
                    for (int k = 0; k < ev.Sheets.Count; k++) if (ev.Sheets[k].Parameter != null && !ev.Parameters.Contains(ev.Sheets[k].Parameter)) ev.Parameters.Add(ev.Sheets[k].Parameter);
                Events.Add(ev);
            }
            Events.Sort(CompareEvents);
        }

        static int CompareEvents(FmodEvent a, FmodEvent b) { return string.CompareOrdinal(a.Path, b.Path); }

        FmodParameter Param(Guid id)
        {
            FmodParameter p;
            if (Parameters.TryGetValue(id, out p)) return p;
            p = new FmodParameter { Id = id, Name = "timeline" };
            return p;
        }

        static FmodEvent EventFor(Dictionary<Guid, FmodEvent> events, Guid id)
        {
            FmodEvent ev;
            if (!events.TryGetValue(id, out ev)) { ev = new FmodEvent { Id = id }; events[id] = ev; }
            return ev;
        }

        /// <summary>A per-placement copy (the same instrument GUID is placed once in practice; copy keeps region data per event).</summary>
        static FmodInstrument Placed(FmodInstrument src, FmodEvent ev, FmodSheet sh, float start, float length)
        {
            var c = new FmodInstrument
            {
                Id = src.Id, Wave = src.Wave, Bus = src.Bus, Sample = src.Sample, SampleName = src.SampleName, VolumeDb = src.VolumeDb,
                PitchSemis = src.PitchSemis, LoopCount = src.LoopCount, AutoPitchParam = src.AutoPitchParam, AutoPitchRef = src.AutoPitchRef,
                RegionParam = sh != null ? sh.Parameter : null, RegionStart = start, RegionLength = length,
            };
            c.Automations.AddRange(src.Automations);
            return c;
        }

        // ---------------- queries ----------------

        /// <summary>Event by short name ("engine_int") or full path, case-insensitive; null if absent.</summary>
        public FmodEvent Event(string nameOrPath)
        {
            for (int i = 0; i < Events.Count; i++)
                if (string.Equals(Events[i].Name, nameOrPath, StringComparison.OrdinalIgnoreCase) || string.Equals(Events[i].Path, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return Events[i];
            return null;
        }

        /// <summary>
        /// The engine layers of engine_int (<paramref name="interior"/>) or engine_ext: every instrument on the rpm sheet with its
        /// region, pitch reference and load/coast classification (from the throttle bus curve's slope). Empty list when absent.
        /// </summary>
        public List<FmodEngineLayer> EngineLayers(bool interior)
        {
            var res = new List<FmodEngineLayer>();
            var ev = Event(interior ? "engine_int" : "engine_ext");
            if (ev == null) return res;
            for (int s = 0; s < ev.Sheets.Count; s++)
            {
                var sh = ev.Sheets[s];
                if (sh.Parameter == null || sh.AutomationOnly) continue;
                string rpmName = sh.Parameter.Name;
                if (rpmName.IndexOf("rpm", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string thrName = "throttle";
                for (int p = 0; p < ev.Parameters.Count; p++) if (ev.Parameters[p].Name.IndexOf("throttle", StringComparison.OrdinalIgnoreCase) >= 0) thrName = ev.Parameters[p].Name;
                for (int i = 0; i < sh.Instruments.Count; i++)
                {
                    var ins = sh.Instruments[i];
                    var l = new FmodEngineLayer
                    {
                        Instrument = ins, SampleName = ins.SampleName, Sample = ins.Sample, RpmMin = ins.RegionStart, RpmMax = ins.RegionEnd,
                        PitchRefRpm = ins.AutoPitchParam != null ? ins.AutoPitchRef : 0f, VolumeDb = ins.VolumeDb,
                        RpmParam = rpmName, ThrottleParam = thrName,
                    };
                    float lo = 0f, hi = 0f; bool any = false;
                    for (int a = 0; a < ins.Automations.Count; a++)
                    {
                        var au = ins.Automations[a];
                        if (au.Parameter == null || au.Curve == null || au.Parameter.Name != thrName || au.Property != FmodAutomation.PropVolumeDb) continue;
                        lo += au.Curve.Eval(0f); hi += au.Curve.Eval(1f); any = true;
                    }
                    l.Load = !any ? FmodEngineLoad.Neutral : hi > lo ? FmodEngineLoad.OnLoad : FmodEngineLoad.OffLoad;
                    res.Add(l);
                }
            }
            res.Sort(CompareLayers);
            return res;
        }

        /// <summary>Load bus first (OnLoad, OffLoad, Neutral order as enum), then by rpm start.</summary>
        static int CompareLayers(FmodEngineLayer a, FmodEngineLayer b)
        {
            int c = ((int)a.Load).CompareTo((int)b.Load);
            if (c != 0) return c;
            c = a.RpmMin.CompareTo(b.RpmMin);
            return c != 0 ? c : string.CompareOrdinal(a.SampleName, b.SampleName);
        }

        /// <summary>A sample's PCM16 as floats (interleaved), read from the bank file. Thread-safe per call (own stream).</summary>
        public float[] ReadSampleFloats(Fsb5Sample s)
        {
            using (var r = AcBinary.OpenFile(Path, 1 << 16)) return Fsb.ReadFloats(r, s);
        }

        public float[] ReadSampleFloats(int index)
        {
            return Fsb != null && index >= 0 && index < Fsb.Samples.Count ? ReadSampleFloats(Fsb.Samples[index]) : null;
        }
    }
}
