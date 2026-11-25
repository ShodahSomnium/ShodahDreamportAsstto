using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>A piecewise-linear FMOD 1.08 curve (points x, y; FMOD's curve shapes ignored, as D's FmodCurve and ETS did).</summary>
    public sealed class AcSndCurve
    {
        public float[] X = new float[0], Y = new float[0];

        /// <summary>Clamped piecewise-linear value. Allocation-free.</summary>
        public float Eval(float x)
        {
            float[] xs = X, ys = Y;
            int n = xs.Length;
            if (n == 0) return 0f;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            for (int i = 1; i < n; i++)
                if (x <= xs[i])
                {
                    float s = xs[i] - xs[i - 1];
                    return s <= 1e-6f ? ys[i] : ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / s;
                }
            return ys[n - 1];
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < X.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(X[i].ToString("0.###", ci)).Append('>').Append(Y[i].ToString("0.###", ci)); }
            return sb.ToString();
        }
    }

    /// <summary>One automation of an instrument (or of a bus on its output chain) along one event parameter (index into
    /// <see cref="AcSndEvent.Params"/>).</summary>
    public sealed class AcSndAuto
    {
        public const int PropVolumeDb = 0, PropPitch = 1, PropVolumeLinear = 4;
        public int Param;
        public int Prop;
        public AcSndCurve Curve;
        public bool OnBus;
    }

    /// <summary>
    /// One DSP effect on a bus of an event (BEFX / PEFX chunks) [VERIFIED by bytes on ferrari_458.bank + the SDK formula_k.bank against
    /// the SDK project XML]: BEFB = {guid, u32 placement, u32 FMOD_DSP_TYPE} (3 lowpass, 5 highpass, 20 lowpass simple, 26 highpass simple,
    /// 27 pan = the SpatialiserEffect (3D pan blend &gt; 0) or the bus's 2D MixerBusPanner, 28 three-EQ); PEFB = {guid, u32, u16 length,
    /// plugin name} ("FMOD Distortion", "FMOD Distance Filter", "FMOD Gain"); the next PMEF holds the parameters (u32 count, then per
    /// parameter u32 type: 0 float / 1 int + 4 bytes, 3 data + u16 length + bytes). CTRL property 1000 + i automates parameter i.
    /// The BUS chunk's first list holds the pre-fader effects, the second the post-fader ones.
    /// </summary>
    public sealed class AcSndFx
    {
        public const int DspLowpass = 3, DspHighpass = 5, DspLowpassSimple = 20, DspHighpassSimple = 26, DspPan = 27, DspThreeEq = 28;
        public Guid Id;
        /// <summary>FMOD_DSP_TYPE for a built-in effect; -1 for a plugin (<see cref="Plugin"/>).</summary>
        public int Dsp = -1;
        public string Plugin = "";
        public float[] Params = new float[0];
        public bool PostFader;
        /// <summary>The effect sits on the event's master bus (every instrument passes it) - else on a group bus (<see cref="Instruments"/>).</summary>
        public bool OnMaster;
        /// <summary>Indices of the event's instruments routed through this effect.</summary>
        public int[] Instruments = new int[0];
        /// <summary>Automations: <see cref="AcSndAuto.Prop"/> = the effect parameter index, <see cref="AcSndAuto.Param"/> = the event parameter.</summary>
        public AcSndAuto[] Autos = new AcSndAuto[0];
        /// <summary>Static bus volumes (dB) between this effect and the bank's exit (the effect's own fader when it is pre-fader) - the
        /// master-bank group volume (instrument MixerDb) comes on top. A nonlinear effect (distortion) sees the signal without them.</summary>
        public float DownstreamDb;

        /// <summary>FMOD distortion waveshaper (FMOD_DSP_DISTORTION_LEVEL 0..1): k = 2L/(1-L), y = (1+k)x/(1+k|x|) [BELIEF: the FMOD Ex
        /// distortion DSP, which Unity's AudioDistortionFilter wraps].</summary>
        public static float Distort(float x, float level)
        {
            if (level <= 0f) return x;
            if (level > 0.999f) level = 0.999f;
            float k = 2f * level / (1f - level);
            return (1f + k) * x / (1f + k * (x < 0f ? -x : x));
        }

        /// <summary>Lowpass cutoff (Hz) from an automation curve value: FMOD Studio stores frequency automation as a 0..10 position,
        /// Hz = 22000 x (pos/10)^3.765 [fitted on the SDK project XML vs the banks: limiter 4500 Hz = 6.565, backfire_ext 4000 Hz = 6.348,
        /// engine_ext 5400 Hz = 6.891 (one car carries the SDK template curve); errors under 0.02 position]. // ours fit</summary>
        public static float CutoffFromPosition(float pos)
        {
            if (pos >= 10f) return 22000f;
            if (pos <= 0f) return 10f;
            float hz = 22000f * (float)Math.Pow(pos / 10.0, 3.765);
            return hz < 10f ? 10f : hz;
        }

        public float P(int i, float fallback) { return i >= 0 && i < Params.Length && !float.IsNaN(Params[i]) ? Params[i] : fallback; }
        public bool IsPlugin(string name) { return Dsp < 0 && Plugin.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0; }
        public bool IsSpatialiser { get { return Dsp == DspPan && P(17, 0f) > 0f; } }

        /// <summary>Value of parameter <paramref name="index"/> at the event values (its automation if any, else the static value).</summary>
        public float Value(int index, float[] values, float fallback)
        {
            for (int k = 0; k < Autos.Length; k++)
                if (Autos[k].Prop == index) return Autos[k].Curve.Eval(values[Autos[k].Param]);
            return P(index, fallback);
        }

        public bool Automated(int index) { for (int k = 0; k < Autos.Length; k++) if (Autos[k].Prop == index) return true; return false; }

        public string Kind
        {
            get
            {
                if (Dsp < 0) return Plugin;
                switch (Dsp)
                {
                    case DspLowpass: return "lowpass"; case DspHighpass: return "highpass"; case DspLowpassSimple: return "lowpass simple";
                    case DspHighpassSimple: return "highpass simple"; case DspPan: return IsSpatialiser ? "spatialiser" : "panner"; case DspThreeEq: return "three-eq";
                    default: return "dsp " + Dsp;
                }
            }
        }
    }

    /// <summary>
    /// One instrument of an event, compiled for per-frame evaluation without allocation: a waveform instrument (WAIB, one sample) or a
    /// multi instrument (MUIB + PLST playlist: several samples with weights - AC's backfires), placed on a parameter sheet (region on
    /// <see cref="RegionParam"/>) or on the event timeline.
    /// </summary>
    public sealed class AcSndInstrument
    {
        public Guid Id;
        /// <summary>FSB5 sample indices (one for a waveform instrument; the playlist for a multi instrument). Never empty.</summary>
        public int[] Samples = new int[0];
        public float[] Weights = new float[0];
        public string[] SampleNames = new string[0];
        public bool Multi;
        public float VolumeDb, PitchSemis;
        /// <summary>Static volumes of the output bus chain inside this bank (event group track, event master: BUS chunks) and in the
        /// master mixer (common.bank grp_* groups) once <see cref="AcSndBank.ApplyMixer"/> ran. dB.</summary>
        public float StaticDb, MixerDb;
        /// <summary>The first bus of the chain this bank does not define (a master-bank group like bus:/grp_engine_int).</summary>
        public Guid ExitBus;
        /// <summary>A single stored volume on the chain (instrument, a bank bus, a mixer group) is at the -42 dB silence floor.</summary>
        public bool StaticSilent, MixerSilent;
        /// <summary>10^((VolumeDb + StaticDb + MixerDb)/20), 0 when any single stored value is at the floor (the floor is applied per stored
        /// value, never to the sum), cached.</summary>
        public float BaseGain = 1f;
        public int LoopCount;
        /// <summary>The instrument itself loops (INST loop count -1). Timeline events with a loop region loop too (<see cref="AcSndEvent.TimelineLoops"/>).</summary>
        public bool Loops { get { return LoopCount < 0; } }
        public int AutoPitchParam = -1;
        public float AutoPitchRef = 1f;
        /// <summary>Sheet placement: parameter index and region; -1 = timeline instrument.</summary>
        public int RegionParam = -1;
        public float RegionStart, RegionEnd;
        /// <summary>Timeline placement in 48 kHz frames (-1 on a sheet).</summary>
        public long TimelinePos = -1, TimelineLength = -1;
        public AcSndAuto[] Autos = new AcSndAuto[0];

        /// <summary>Recomputes <see cref="BaseGain"/> from the stored values.</summary>
        public void Recompute()
        {
            bool silent = VolumeDb <= AcSndEvent.SilentDb || StaticSilent || MixerSilent;
            BaseGain = silent ? 0f : (float)Math.Pow(10.0, (VolumeDb + StaticDb + MixerDb) / 20.0);
        }

        public string Label { get { return SampleNames.Length == 1 ? SampleNames[0] : "multi[" + SampleNames.Length + "]"; } }
        public override string ToString() { return Label + (RegionParam >= 0 ? " region " + RegionStart + ".." + RegionEnd : " timeline") + " ref " + AutoPitchRef + " " + VolumeDb + "dB"; }
    }

    /// <summary>
    /// An FMOD event of an AC bank, compiled: its parameter names (values are passed as a float array in this order), its instruments
    /// with region / automations / auto-pitch, and the evaluation rules (ac_physics_sound_rules 2.4-2.5):
    /// gain = dB(INST volume) x every linear-volume automation x every dB automation (instrument + output bus chain), 0 outside the
    /// region; pitch = 2^((INST semitones + pitch automations) / 12) x value / auto-pitch reference.
    /// Pitch automation values are stored as semitones / 24 [VERIFIED: SDK project XML turbo pitch -12 / +7.83 / +20.35 st = bank
    /// -0.5 / 0.326 / 0.848; wind -3 / +12 st = bank -0.125 / 0.5]. Volume dB values at or below -42 (the floor every AC bank uses
    /// where the SDK project says -80 = silent) are silence.
    /// </summary>
    public sealed class AcSndEvent
    {
        public Guid Id;
        public string Path = "", Name = "";
        public string[] Params = new string[0];
        public float[] ParamMin = new float[0], ParamMax = new float[0], ParamDefault = new float[0];
        public AcSndInstrument[] Instruments = new AcSndInstrument[0];
        /// <summary>The timeline has a loop region (TLNB tail record): its timeline instruments repeat while the event plays
        /// [VERIFIED by byte pattern on AC banks; the SDK project has LoopRegions on skid, horn, turbo, limiter, tc, gear_grind,
        /// surfaces, ambience and none on backfire].</summary>
        public bool TimelineLoops;
        /// <summary>Every DSP effect on the event's buses (spatialiser, panners, filters, EQ, plugins), master bus first.</summary>
        public AcSndFx[] Effects = new AcSndFx[0];
        /// <summary>The event's spatialiser (pan DSP with 3D pan blend &gt; 0), or null: the event is 2D.</summary>
        public AcSndFx Spatialiser;

        /// <summary>Spatialiser minimum distance (m), FMOD pan parameter 12; <paramref name="fallback"/> without a spatialiser.</summary>
        public float MinDistance(float fallback) { return Spatialiser != null ? Spatialiser.P(12, fallback) : fallback; }
        /// <summary>Spatialiser maximum distance (m), FMOD pan parameter 13.</summary>
        public float MaxDistance(float fallback) { return Spatialiser != null ? Spatialiser.P(13, fallback) : fallback; }
        /// <summary>Spatialiser rolloff (pan parameter 11: the SDK XML distanceRolloffType - 3 on engine_ext = inverse tapered; 0 when the
        /// XML leaves the default).</summary>
        public int Rolloff { get { return Spatialiser != null ? (int)Spatialiser.P(11, 0f) : 0; } }

        /// <summary>First effect of a kind on this event (null if none).</summary>
        public AcSndFx Effect(int dsp, string plugin)
        {
            for (int i = 0; i < Effects.Length; i++)
            {
                var f = Effects[i];
                if (plugin != null ? f.IsPlugin(plugin) : f.Dsp == dsp) return f;
            }
            return null;
        }

        /// <summary>The dB value FMOD 1.08 banks store for silence (SDK project -80 dB). // ours interpretation</summary>
        public const float SilentDb = -42f;

        public static float Db(float db) { return db <= SilentDb ? 0f : (float)Math.Pow(10.0, db / 20.0); }

        public int ParamIndex(string name)
        {
            for (int i = 0; i < Params.Length; i++) if (string.Equals(Params[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public bool HasParam(string name) { return ParamIndex(name) >= 0; }

        /// <summary>A values array with every parameter at its default.</summary>
        public float[] NewValues()
        {
            var v = new float[Params.Length];
            for (int i = 0; i < v.Length; i++) v[i] = ParamDefault[i];
            return v;
        }

        /// <summary>True when instrument <paramref name="i"/> is inside its region at these values (timeline instruments always).</summary>
        public bool InRegion(int i, float[] values)
        {
            var ins = Instruments[i];
            if (ins.RegionParam < 0) return true;
            float v = values[ins.RegionParam];
            return v >= ins.RegionStart && v <= ins.RegionEnd;
        }

        /// <summary>Linear gain and playback rate of instrument <paramref name="i"/> at <paramref name="values"/>. Allocation-free.</summary>
        public void Evaluate(int i, float[] values, out float gain, out float pitch)
        {
            var ins = Instruments[i];
            bool inside = InRegion(i, values);
            float g = inside ? ins.BaseGain : 0f;
            float semis = ins.PitchSemis;
            var autos = ins.Autos;
            for (int k = 0; k < autos.Length; k++)
            {
                var a = autos[k];
                float y = a.Curve.Eval(values[a.Param]);
                if (a.Prop == AcSndAuto.PropVolumeLinear) g *= y < 0f ? 0f : y > 1f ? 1f : y;
                else if (a.Prop == AcSndAuto.PropVolumeDb) g *= Db(y);
                else if (a.Prop == AcSndAuto.PropPitch) semis += y * 24f;
            }
            gain = g;
            float rate = semis == 0f ? 1f : (float)Math.Pow(2.0, semis / 12.0);
            if (ins.AutoPitchParam >= 0 && ins.AutoPitchRef > 0f) rate *= values[ins.AutoPitchParam] / ins.AutoPitchRef;
            pitch = rate;
        }

        public override string ToString() { return Path; }
    }

    /// <summary>
    /// An Assetto Corsa FMOD Studio 1.08 bank compiled for playback (module A's own parse of the LIST PROJ metadata, same chunk offsets
    /// as D's <see cref="FmodBank"/> [VERIFIED 86/86 banks], plus what D does not decode: multi instruments (MUIB + PLST playlists,
    /// AC's backfires), the timeline loop-region flag, parameter indices per event, pitch automation units). Sample headers and PCM
    /// come from D's <see cref="Fsb5"/>. Worker-thread safe (no Unity).
    /// </summary>
    public sealed class AcSndBank
    {
        public string Path;
        public Fsb5 Fsb;
        public readonly List<AcSndEvent> Events = new List<AcSndEvent>();
        public int Multis, Unresolved;
        /// <summary>Bus -> parent bus (GBSB / MBSB / IBSB) and each bus's static volume (its BUS chunk) [VERIFIED by bytes: common.bank
        /// grp_turbo -10.5, grp_gear_ext -10, grp_transmission -11.5, grp_skid_int -4.5 dB ...].</summary>
        public readonly Dictionary<Guid, Guid> BusParent = new Dictionary<Guid, Guid>();
        public readonly Dictionary<Guid, float> BusVolumeDb = new Dictionary<Guid, float>();
        public double ParseMs;

        struct Chunk { public string Id; public byte[] Data; }

        sealed class Inst
        {
            public Guid Id, Bus;
            public List<Guid> Waves = new List<Guid>();
            public List<float> Weights = new List<float>();
            public bool Multi;
            public float VolumeDb, PitchSemis, AutoRef = 1f;
            public int Loop;
            public Guid AutoParam;
        }

        sealed class Ctrl { public Guid Owner, Param, Curve; public int Prop; }

        sealed class Placement { public Guid Inst; public Guid Param; public float Start, Length; public long Pos = -1, Len = -1; }

        sealed class EvtBuild
        {
            public Guid Id;
            public readonly List<Guid> Params = new List<Guid>();
            public readonly List<Placement> Placed = new List<Placement>();
            public bool Loops;
            /// <summary>EVTB +48: the event's mixer input bus (IBSB, routed to a master-bank group such as bus:/grp_engine_int); +64 its
            /// master bus (MBSB) [VERIFIED ferrari_458 wind: +48 = IBSB 31825c82, +64 = MBSB 706cc617].</summary>
            public Guid Input;
        }

        public AcSndEvent Event(string nameOrPath)
        {
            for (int i = 0; i < Events.Count; i++)
                if (string.Equals(Events[i].Name, nameOrPath, StringComparison.OrdinalIgnoreCase) || string.Equals(Events[i].Path, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return Events[i];
            return null;
        }

        public string SampleName(int index) { return Fsb != null && index >= 0 && index < Fsb.Samples.Count ? Fsb.Samples[index].Name : "#" + index; }

        /// <summary>Reads a bank's metadata and sample headers (the PCM stays on disk).</summary>
        public static AcSndBank Load(string path, FmodGuids guids, string preferPath)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bank = new AcSndBank { Path = path };
            var chunks = new List<Chunk>();
            using (var r = AcBinary.OpenFile(path, 1 << 16))
            {
                if (r.Length < 12) throw new InvalidDataException("bank too small: " + path);
                string riff = r.ReadAscii(4); r.ReadInt32(); string fev = r.ReadAscii(4);
                long snd = -1;
                if (riff == "RIFF" && fev == "FEV ") Walk(r, 12, r.Length, chunks, 0, ref snd);
                long at = snd >= 0 ? Fsb5.FindMagic(r, snd, 4096) : -1;
                if (at < 0) at = Fsb5.FindMagic(r, 0, r.Length);
                if (at >= 0) bank.Fsb = Fsb5.Read(r, at);
            }
            bank.Parse(chunks, guids, preferPath);
            bank.ParseMs = sw.Elapsed.TotalMilliseconds;
            return bank;
        }

        static void Walk(AcBinary r, long p, long end, List<Chunk> chunks, int depth, ref long snd)
        {
            if (depth > 16) return;
            while (p + 8 <= end)
            {
                r.Position = p;
                string id = r.ReadAscii(4);
                long sz = r.ReadUInt32();
                if (p + 8 + sz > end) return;
                if (id == "LIST") Walk(r, p + 12, p + 8 + sz, chunks, depth + 1, ref snd);
                else if (id == "SND ") snd = p + 8;
                else if (sz <= (1 << 22) && id != "FMT ") chunks.Add(new Chunk { Id = id, Data = r.ReadBytes((int)sz) });
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

        static void ReadInst(byte[] q, Inst ins)
        {
            ins.VolumeDb = F(q, 16); ins.PitchSemis = F(q, 20); ins.Loop = I32(q, 24);
            ins.AutoParam = G(q, 53); ins.AutoRef = F(q, 69); ins.Bus = G(q, 83);
        }

        void Parse(List<Chunk> chunks, FmodGuids guids, string preferPath)
        {
            var prm = new Dictionary<Guid, FmodParameter>();
            var wav = new Dictionary<Guid, int>();
            var insts = new Dictionary<Guid, Inst>();
            var busParent = BusParent;
            var curves = new Dictionary<Guid, AcSndCurve>();
            var ctrls = new List<Ctrl>();
            var evts = new Dictionary<Guid, EvtBuild>();
            var fxById = new Dictionary<Guid, AcSndFx>();
            var busFx = new Dictionary<Guid, List<Guid>>();
            var busPost = new HashSet<Guid>();
            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] d = chunks[i].Data;
                switch (chunks[i].Id)
                {
                    case "PRMB":
                    {
                        int len = U16(d, 21);
                        if (23 + len + 12 > d.Length) break;
                        string n = Encoding.ASCII.GetString(d, 23, len).TrimEnd('\0');
                        int q = 23 + len;
                        prm[G(d, 0)] = new FmodParameter { Id = G(d, 0), Name = n, Min = F(d, q), Max = F(d, q + 4), Default = F(d, q + 8) };
                        break;
                    }
                    case "WAV ": wav[G(d, 0)] = U16(d, 22); break;
                    case "WAIB":
                    {
                        if (i + 1 >= chunks.Count || chunks[i + 1].Id != "INST" || chunks[i + 1].Data.Length < 99) break;
                        var ins = new Inst { Id = G(d, 0) };
                        ins.Waves.Add(G(d, 16)); ins.Weights.Add(1f);
                        ReadInst(chunks[i + 1].Data, ins);
                        insts[ins.Id] = ins;
                        break;
                    }
                    case "MUIB":
                    {
                        // multi instrument: MUIB {guid} then PLST (playlist {waveform guid, f32 weight} entries, stride 20) and INST
                        // [VERIFIED abarth500: two MUIB, playlists of 6 backfireEXT_* and 5 500_backfire* waveforms].
                        var ins = new Inst { Id = G(d, 0), Multi = true };
                        bool gotInst = false;
                        for (int k = i + 1; k <= i + 3 && k < chunks.Count; k++)
                        {
                            string id = chunks[k].Id;
                            byte[] q = chunks[k].Data;
                            if (id == "PLST")
                            {
                                for (int at = 0; at <= 16 && at + 4 <= q.Length; at += 2)
                                {
                                    int cnt = U16(q, at), es = U16(q, at + 2);
                                    if ((cnt & 1) == 0 || cnt < 3 || es != 20) continue;
                                    int n = (cnt - 1) / 2;
                                    for (int e = 0; e < n && at + 4 + e * 20 + 20 <= q.Length; e++)
                                    {
                                        ins.Waves.Add(G(q, at + 4 + e * 20));
                                        ins.Weights.Add(F(q, at + 4 + e * 20 + 16));
                                    }
                                    break;
                                }
                            }
                            else if (id == "INST" && q.Length >= 99) { ReadInst(q, ins); gotInst = true; break; }
                            else if (id == "WAIB" || id == "MUIB") break;
                        }
                        if (gotInst && ins.Waves.Count > 0) { insts[ins.Id] = ins; Multis++; }
                        break;
                    }
                    case "GBSB": case "MBSB": case "IBSB": busParent[G(d, 0)] = G(d, 18); break;
                    case "BUS ":
                    {
                        string prev = i > 0 ? chunks[i - 1].Id : "";
                        float bv;
                        if ((prev == "GBSB" || prev == "MBSB" || prev == "IBSB") && TryBusVolume(d, out bv)) BusVolumeDb[G(chunks[i - 1].Data, 0)] = bv;
                        if (prev == "GBSB" || prev == "MBSB" || prev == "IBSB") BusEffects(d, G(chunks[i - 1].Data, 0), busFx, busPost);
                        break;
                    }
                    case "BEFB":
                    case "PEFB":
                    {
                        if (d.Length < 22) break;
                        var fx = new AcSndFx { Id = G(d, 0) };
                        if (chunks[i].Id == "BEFB") fx.Dsp = I32(d, 20);
                        else { int len = U16(d, 20); if (22 + len <= d.Length) fx.Plugin = Encoding.ASCII.GetString(d, 22, len).TrimEnd('\0'); }
                        if (i + 1 < chunks.Count && chunks[i + 1].Id == "PMEF") fx.Params = EffectParams(chunks[i + 1].Data);
                        fxById[fx.Id] = fx;
                        break;
                    }
                    case "CURV":
                    {
                        int n = (U16(d, 32) - 1) / 2, es = U16(d, 34);
                        if (n < 0 || es < 12 || d.Length < 48) n = 0;
                        else n = Math.Min(n, (d.Length - 48) / es + 1);
                        var c = new AcSndCurve { X = new float[n], Y = new float[n] };
                        for (int k = 0; k < n; k++) { c.X[k] = F(d, 36 + k * es); c.Y[k] = F(d, 40 + k * es); }
                        curves[G(d, 0)] = c;
                        break;
                    }
                    case "CTRL":
                        if (d.Length >= 68) ctrls.Add(new Ctrl { Owner = G(d, 16), Param = G(d, 32), Curve = G(d, 48), Prop = I32(d, 64) });
                        break;
                    case "PMLB":
                    {
                        if (d.Length < 52) break;
                        var ev = Evt(evts, G(d, 32));
                        Guid param = G(d, 16);
                        int n = (U16(d, 48) - 1) / 2, es = U16(d, 50);
                        if (es == 24)
                            for (int k = 0; k < n && 52 + k * 24 + 24 <= d.Length; k++)
                                ev.Placed.Add(new Placement { Inst = G(d, 52 + k * 24), Param = param, Start = F(d, 68 + k * 24), Length = F(d, 72 + k * 24) });
                        break;
                    }
                    case "TLNB":
                    {
                        if (d.Length < 36) break;
                        var ev = Evt(evts, G(d, 16));
                        int listEnd = 32;
                        for (int at = 32; at <= 40 && at + 4 <= d.Length; at += 2)
                        {
                            int cnt = U16(d, at), es = U16(d, at + 2);
                            if ((cnt & 1) == 0 || cnt < 3 || es != 24) continue;
                            int n = (cnt - 1) / 2, e0 = at + 4;
                            for (int k = 0; k < n && e0 + k * es + 24 <= d.Length; k++)
                                ev.Placed.Add(new Placement { Inst = G(d, e0 + k * es), Pos = (uint)I32(d, e0 + 16 + k * es), Len = (uint)I32(d, e0 + 20 + k * es) });
                            listEnd = e0 + n * es;
                            break;
                        }
                        // loop region record after the instrument list: "02 00 16 00" + 22 bytes (absent on backfire events)
                        for (int at = listEnd; at + 4 <= d.Length; at += 2)
                            if (U16(d, at) == 2 && U16(d, at + 2) == 22) { ev.Loops = true; break; }
                        break;
                    }
                    case "EVTB":
                    {
                        var ev = Evt(evts, G(d, 0));
                        if (d.Length >= 80) ev.Input = G(d, 48);
                        if (d.Length >= 97)
                        {
                            int n = (U16(d, 93) - 1) / 2, es = U16(d, 95);
                            if (es == 16 && n > 0 && 97 + n * 16 <= d.Length)
                                for (int k = 0; k < n; k++) ev.Params.Add(G(d, 97 + k * 16));
                        }
                        break;
                    }
                }
            }

            var byOwner = new Dictionary<Guid, List<Ctrl>>();
            for (int i = 0; i < ctrls.Count; i++)
            {
                List<Ctrl> l;
                if (!byOwner.TryGetValue(ctrls[i].Owner, out l)) { l = new List<Ctrl>(); byOwner[ctrls[i].Owner] = l; }
                l.Add(ctrls[i]);
            }

            foreach (var eb in evts.Values)
            {
                var ev = new AcSndEvent { Id = eb.Id, TimelineLoops = eb.Loops };
                string path = guids != null ? guids.PathOf(eb.Id, preferPath) : null;
                ev.Path = path ?? eb.Id.ToString("B");
                int slash = ev.Path.LastIndexOf('/');
                ev.Name = slash >= 0 ? ev.Path.Substring(slash + 1) : ev.Path;

                // parameter list: EVTB order, then anything the placements / automations use
                var plist = new List<FmodParameter>();
                for (int k = 0; k < eb.Params.Count; k++) { FmodParameter p; if (prm.TryGetValue(eb.Params[k], out p) && !plist.Contains(p)) plist.Add(p); }
                var built = new List<AcSndInstrument>();
                var chains = new List<List<Guid>>();
                for (int k = 0; k < eb.Placed.Count; k++)
                {
                    var pl = eb.Placed[k];
                    Inst src;
                    if (!insts.TryGetValue(pl.Inst, out src)) { Unresolved++; continue; }
                    var ins = new AcSndInstrument
                    {
                        Id = src.Id, Multi = src.Multi, VolumeDb = src.VolumeDb, PitchSemis = src.PitchSemis, LoopCount = src.Loop,
                        TimelinePos = pl.Pos, TimelineLength = pl.Len,
                    };
                    var samples = new List<int>(); var weights = new List<float>(); var names = new List<string>();
                    for (int w = 0; w < src.Waves.Count; w++)
                    {
                        int si;
                        Guid wg = src.Waves[w];
                        // playlist entries name waveform INSTRUMENTS (nested WAIB) [VERIFIED abarth500], not waveforms
                        Inst member;
                        if (!wav.ContainsKey(wg) && insts.TryGetValue(wg, out member) && !member.Multi && member.Waves.Count > 0) wg = member.Waves[0];
                        if (!wav.TryGetValue(wg, out si)) continue;
                        samples.Add(si); weights.Add(src.Weights[w] > 0f ? src.Weights[w] : 1f); names.Add(SampleName(si));
                    }
                    if (samples.Count == 0) { Unresolved++; continue; }
                    ins.Samples = samples.ToArray(); ins.Weights = weights.ToArray(); ins.SampleNames = names.ToArray();
                    if (pl.Pos < 0)
                    {
                        FmodParameter rp;
                        if (prm.TryGetValue(pl.Param, out rp))
                        {
                            ins.RegionParam = AddParam(plist, rp);
                            ins.RegionStart = pl.Start; ins.RegionEnd = pl.Start + pl.Length;
                        }
                    }
                    FmodParameter ap;
                    if (src.AutoParam != Guid.Empty && prm.TryGetValue(src.AutoParam, out ap)) { ins.AutoPitchParam = AddParam(plist, ap); ins.AutoPitchRef = src.AutoRef; }
                    var autos = new List<AcSndAuto>();
                    AddAutos(byOwner, src.Id, false, prm, curves, plist, autos);
                    var seen = new HashSet<Guid>();
                    var chain = new List<Guid>();
                    for (Guid bus = src.Bus; bus != Guid.Empty && seen.Add(bus); )
                    {
                        AddAutos(byOwner, bus, true, prm, curves, plist, autos);
                        chain.Add(bus);
                        float bv;
                        if (BusVolumeDb.TryGetValue(bus, out bv)) { ins.StaticDb += bv; if (bv <= AcSndEvent.SilentDb) ins.StaticSilent = true; }
                        Guid parent;
                        if (!busParent.TryGetValue(bus, out parent)) { ins.ExitBus = bus; break; }
                        // the event master bus has no parent: the signal continues at the event's mixer input bus
                        if (parent == Guid.Empty && eb.Input != Guid.Empty && !seen.Contains(eb.Input)) parent = eb.Input;
                        bus = parent;
                    }
                    ins.Autos = autos.ToArray();
                    chains.Add(chain);
                    ins.Recompute();
                    built.Add(ins);
                }
                ev.Instruments = built.ToArray();
                BuildEffects(ev, chains, busFx, busPost, fxById, byOwner, prm, curves, plist);
                ev.Params = new string[plist.Count];
                ev.ParamMin = new float[plist.Count]; ev.ParamMax = new float[plist.Count]; ev.ParamDefault = new float[plist.Count];
                for (int k = 0; k < plist.Count; k++) { ev.Params[k] = plist[k].Name; ev.ParamMin[k] = plist[k].Min; ev.ParamMax[k] = plist[k].Max; ev.ParamDefault[k] = plist[k].Default; }
                Events.Add(ev);
            }
            Events.Sort(CompareEvents);
        }

        static int CompareEvents(AcSndEvent a, AcSndEvent b) { return string.CompareOrdinal(a.Path, b.Path); }

        /// <summary>A BUS chunk's two effect lists (pre-fader, post-fader; u16 2n+1, u16 entry size, effect guids).</summary>
        static void BusEffects(byte[] d, Guid bus, Dictionary<Guid, List<Guid>> busFx, HashSet<Guid> busPost)
        {
            int p = 5;
            for (int list = 0; list < 2; list++)
            {
                if (p + 2 > d.Length) return;
                int c = U16(d, p);
                if ((c & 1) == 0) return;
                if (c == 1) { p += 2; continue; }
                if (p + 4 > d.Length) return;
                int es = U16(d, p + 2), n = (c - 1) / 2;
                if (es < 16 || p + 4 + n * es > d.Length) return;
                for (int e = 0; e < n; e++)
                {
                    Guid g = G(d, p + 4 + e * es);
                    List<Guid> l;
                    if (!busFx.TryGetValue(bus, out l)) { l = new List<Guid>(); busFx[bus] = l; }
                    l.Add(g);
                    if (list == 1) busPost.Add(g);
                }
                p += 4 + n * es;
            }
        }

        /// <summary>PMEF: u32 count, then per parameter u32 type (0 float, 1 int, 2 bool: 4 bytes; 3 data: u16 length + bytes). Data
        /// parameters are NaN.</summary>
        static float[] EffectParams(byte[] d)
        {
            int count = I32(d, 0);
            if (count <= 0 || count > 64) return new float[0];
            var v = new float[count];
            for (int k = 0; k < count; k++) v[k] = float.NaN;
            int p = 4;
            for (int k = 0; k < count && p + 4 <= d.Length; k++)
            {
                int type = I32(d, p); p += 4;
                if (type == 0) { v[k] = F(d, p); p += 4; }
                else if (type == 1 || type == 2) { v[k] = I32(d, p); p += 4; }
                else if (type == 3) { int len = U16(d, p); p += 2 + len; }
                else break;
            }
            return v;
        }

        void BuildEffects(AcSndEvent ev, List<List<Guid>> chains, Dictionary<Guid, List<Guid>> busFx, HashSet<Guid> busPost,
            Dictionary<Guid, AcSndFx> fxById, Dictionary<Guid, List<Ctrl>> byOwner, Dictionary<Guid, FmodParameter> prm,
            Dictionary<Guid, AcSndCurve> curves, List<FmodParameter> plist)
        {
            var order = new List<Guid>();
            var users = new Dictionary<Guid, List<int>>();
            for (int i = 0; i < chains.Count; i++)
                for (int k = 0; k < chains[i].Count; k++)
                {
                    Guid b = chains[i][k];
                    List<int> u;
                    if (!users.TryGetValue(b, out u)) { u = new List<int>(); users[b] = u; order.Add(b); }
                    if (!u.Contains(i)) u.Add(i);
                }
            // master (shared by every instrument) first; stable among equals
            var sorted = new List<Guid>();
            for (int want = chains.Count; want >= 1; want--)
                for (int k = 0; k < order.Count; k++) if (users[order[k]].Count == want) sorted.Add(order[k]);
            var list = new List<AcSndFx>();
            for (int b = 0; b < sorted.Count; b++)
            {
                List<Guid> fxs;
                if (!busFx.TryGetValue(sorted[b], out fxs)) continue;
                var u = users[sorted[b]];
                for (int k = 0; k < fxs.Count; k++)
                {
                    AcSndFx proto;
                    if (!fxById.TryGetValue(fxs[k], out proto)) continue;
                    var fx = new AcSndFx
                    {
                        Id = proto.Id, Dsp = proto.Dsp, Plugin = proto.Plugin, Params = proto.Params, PostFader = busPost.Contains(proto.Id),
                        OnMaster = u.Count == chains.Count, Instruments = u.ToArray(),
                    };
                    List<Ctrl> cl;
                    if (byOwner.TryGetValue(proto.Id, out cl))
                    {
                        var autos = new List<AcSndAuto>();
                        for (int c = 0; c < cl.Count; c++)
                        {
                            if (cl[c].Prop < 1000 || cl[c].Prop >= 1064) continue;
                            FmodParameter p; AcSndCurve cv;
                            if (!prm.TryGetValue(cl[c].Param, out p) || !curves.TryGetValue(cl[c].Curve, out cv) || cv.X.Length == 0) continue;
                            autos.Add(new AcSndAuto { Param = AddParam(plist, p), Prop = cl[c].Prop - 1000, Curve = cv, OnBus = true });
                        }
                        fx.Autos = autos.ToArray();
                    }
                    // static bus volumes after the effect inside this bank (its own fader too when pre-fader)
                    var ch = chains[u[0]];
                    int at = ch.IndexOf(sorted[b]);
                    for (int c = at; c >= 0 && c < ch.Count; c++)
                    {
                        if (c == at && fx.PostFader) continue;
                        float bv;
                        if (BusVolumeDb.TryGetValue(ch[c], out bv)) fx.DownstreamDb += bv;
                    }
                    list.Add(fx);
                    if (ev.Spatialiser == null && fx.IsSpatialiser) ev.Spatialiser = fx;
                }
            }
            ev.Effects = list.ToArray();
        }

        static EvtBuild Evt(Dictionary<Guid, EvtBuild> evts, Guid id)
        {
            EvtBuild e;
            if (!evts.TryGetValue(id, out e)) { e = new EvtBuild { Id = id }; evts[id] = e; }
            return e;
        }

        static int AddParam(List<FmodParameter> plist, FmodParameter p)
        {
            int i = plist.IndexOf(p);
            if (i >= 0) return i;
            plist.Add(p);
            return plist.Count - 1;
        }

        static void AddAutos(Dictionary<Guid, List<Ctrl>> byOwner, Guid owner, bool onBus, Dictionary<Guid, FmodParameter> prm,
            Dictionary<Guid, AcSndCurve> curves, List<FmodParameter> plist, List<AcSndAuto> autos)
        {
            List<Ctrl> l;
            if (!byOwner.TryGetValue(owner, out l)) return;
            for (int k = 0; k < l.Count; k++)
            {
                var c = l[k];
                if (c.Prop != AcSndAuto.PropVolumeDb && c.Prop != AcSndAuto.PropPitch && c.Prop != AcSndAuto.PropVolumeLinear) continue;
                FmodParameter p; AcSndCurve cv;
                if (!prm.TryGetValue(c.Param, out p) || !curves.TryGetValue(c.Curve, out cv) || cv.X.Length == 0) continue; // timeline-position automations: not a parameter
                autos.Add(new AcSndAuto { Param = AddParam(plist, p), Prop = c.Prop, Curve = cv, OnBus = onBus });
            }
        }

        /// <summary>Static volume of a BUS chunk: u8, u32, two u16-counted lists (2n+1, entry size, entries), then a property block
        /// "08 00" + f32 volume dB + f32. [VERIFIED by bytes on common.bank / ferrari_458.bank: sizes 27 / 45 / 61 / 63 all parse.]</summary>
        public static bool TryBusVolume(byte[] d, out float vol)
        {
            vol = 0f;
            if (d == null) return false;
            int p = 5;
            for (int list = 0; list < 2; list++)
            {
                if (p + 2 > d.Length) return false;
                int c = U16(d, p);
                if ((c & 1) == 0) return false;
                if (c == 1) { p += 2; continue; }
                if (p + 4 > d.Length) return false;
                int es = U16(d, p + 2), n = (c - 1) / 2;
                p += 4 + n * es;
            }
            if (p + 10 > d.Length || U16(d, p) != 8) return false;
            vol = F(d, p + 2);
            return !float.IsNaN(vol) && vol > -100f && vol < 40f;
        }

        /// <summary>Sum of the static bus volumes from <paramref name="bus"/> up to this bank's master bus (this bank used as the master mixer).</summary>
        public float ChainDb(Guid bus) { bool silent; return ChainDb(bus, out silent); }

        /// <summary><see cref="ChainDb(Guid)"/>; <paramref name="silent"/> = one of the bus volumes is at the -42 dB floor.</summary>
        public float ChainDb(Guid bus, out bool silent)
        {
            float db = 0f;
            silent = false;
            var seen = new HashSet<Guid>();
            while (bus != Guid.Empty && seen.Add(bus))
            {
                float v;
                if (BusVolumeDb.TryGetValue(bus, out v)) { db += v; if (v <= AcSndEvent.SilentDb) silent = true; }
                Guid parent;
                if (!BusParent.TryGetValue(bus, out parent)) break;
                bus = parent;
            }
            return db;
        }

        /// <summary>Adds the master mixer's group volumes (common.bank: bus:/grp_engine_int, grp_gear_ext ...) to every instrument whose chain
        /// leaves this bank. Pass null to remove them.</summary>
        public void ApplyMixer(AcSndBank mixer)
        {
            for (int e = 0; e < Events.Count; e++)
            {
                var ins = Events[e].Instruments;
                for (int i = 0; i < ins.Length; i++)
                {
                    bool silent = false;
                    ins[i].MixerDb = mixer != null && mixer != this && ins[i].ExitBus != Guid.Empty ? mixer.ChainDb(ins[i].ExitBus, out silent) : 0f;
                    ins[i].MixerSilent = silent;
                    ins[i].Recompute();
                }
            }
        }

        /// <summary>Distinct sample indices used by the given events (null = all events).</summary>
        public List<int> SamplesOf(IList<AcSndEvent> events)
        {
            var set = new List<int>();
            var src = events ?? (IList<AcSndEvent>)Events;
            for (int e = 0; e < src.Count; e++)
            {
                if (src[e] == null) continue;
                var ins = src[e].Instruments;
                for (int i = 0; i < ins.Length; i++)
                    for (int s = 0; s < ins[i].Samples.Length; s++) if (!set.Contains(ins[i].Samples[s])) set.Add(ins[i].Samples[s]);
            }
            return set;
        }

        /// <summary>A sample's PCM16 as interleaved floats (own file handle; worker-thread safe).</summary>
        public float[] ReadSampleFloats(int index)
        {
            if (Fsb == null || index < 0 || index >= Fsb.Samples.Count) return null;
            using (var r = AcBinary.OpenFile(Path, 1 << 16)) return Fsb.ReadFloats(r, Fsb.Samples[index]);
        }

        public override string ToString() { return System.IO.Path.GetFileName(Path) + ": " + Events.Count + " events, " + (Fsb != null ? Fsb.Samples.Count : 0) + " samples"; }
    }
}
