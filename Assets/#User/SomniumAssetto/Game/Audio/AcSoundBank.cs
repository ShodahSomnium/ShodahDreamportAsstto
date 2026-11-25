using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [A, Audio] One FMOD bank of the player's install, loaded once per client and shared: a car's <c>sfx/&lt;car&gt;.bank</c> (every car of
    /// that model - local, remote, AI - reuses the same clips) or <c>content/sfx/common.bank</c>. The metadata (Data/Audio
    /// <see cref="AcSndBank"/>) and the car's data.acd sound values (<see cref="AcCarSounds"/>) are read on the audio worker; samples are
    /// converted on the worker only when an event that uses them is <see cref="Require"/>d, and uploaded time-sliced. A bank nobody
    /// uses frees its clips after <see cref="IdleFreeSeconds"/>.
    /// </summary>
    internal sealed class AcSoundBank : IUploadSink
    {
        /// <summary>Clips of an unused bank are destroyed after this. // ours</summary>
        public const float IdleFreeSeconds = 60f;

        static readonly Dictionary<string, AcSoundBank> Cache = new Dictionary<string, AcSoundBank>(StringComparer.OrdinalIgnoreCase);
        static FmodGuids _installGuids;
        static AcSndBank _mixer;
        static string _mixerPath;
        static readonly object MixerLock = new object();
        static string _commonPath;

        /// <summary>content/sfx/common.bank parsed once (worker thread): the master mixer every car bank is routed into, and the common events.</summary>
        static AcSndBank Mixer(string path, string guidsTxt)
        {
            lock (MixerLock)
            {
                if (_mixer == null || _mixerPath != path) { _mixer = AcSndBank.Load(path, Guids(guidsTxt, null), null); _mixerPath = path; }
                return _mixer;
            }
        }
        static readonly object GuidLock = new object();

        public readonly string Key, Label, BankPath, CarDir, GuidsTxt, CarGuids;
        public readonly bool IsCommon;
        public AcSndBank Meta { get; private set; }
        public AcCarSounds Sounds { get; private set; }
        public bool MetaReady { get; private set; }
        public string Error { get; private set; }

        AudioClip[] _clips = new AudioClip[0];
        byte[] _state = new byte[0];        // 0 none, 1 queued, 2 ready, 3 failed
        int _users;
        float _idleSince = -1f;
        int _pendingBatch, _batchClips, _cacheHits;
        long _batchFloats;
        float _batchStart;
        bool _metaQueued, _builtLogged;
        int _laterBatches, _laterClips;

        const byte None = 0, Queued = 1, Ready = 2, Failed = 3;

        AcSoundBank(string key, string label, string bankPath, string carDir, string guidsTxt, string carGuids, bool common)
        {
            Key = key; Label = label; BankPath = bankPath; CarDir = carDir; GuidsTxt = guidsTxt; CarGuids = carGuids; IsCommon = common;
        }

        /// <summary>The bank of a car (null when the car has no bank); loading starts on first use.</summary>
        public static AcSoundBank ForCar(AcCarEntry car)
        {
            if (car == null || string.IsNullOrEmpty(car.BankPath)) return null;
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst != null) _commonPath = inst.CommonBank;
            AcSoundBank b;
            if (!Cache.TryGetValue(car.BankPath, out b))
            {
                b = new AcSoundBank(car.BankPath, car.Id, car.BankPath, car.Dir, inst != null ? inst.GuidsTxt : null, car.GuidsPath, false);
                Cache[car.BankPath] = b;
            }
            b.LoadMeta();
            return b;
        }

        /// <summary>content/sfx/common.bank (surfaces, collisions, the beep, ambience); null without an install.</summary>
        public static AcSoundBank Common()
        {
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst == null) return null;
            string p = inst.CommonBank;
            _commonPath = p;
            AcSoundBank b;
            if (!Cache.TryGetValue(p, out b))
            {
                b = new AcSoundBank(p, "common.bank", p, null, inst.GuidsTxt, null, true);
                Cache[p] = b;
            }
            b.LoadMeta();
            return b;
        }

        public void AddUser() { _users++; _idleSince = -1f; }
        public void RemoveUser() { if (--_users <= 0) { _users = 0; _idleSince = Time.unscaledTime; } }

        void LoadMeta()
        {
            if (_metaQueued) return;
            _metaQueued = true;
            AcAudioWorker.Enqueue(new MetaJob { Bank = this }, true);
        }

        sealed class MetaJob : AcAudioWorker.Job
        {
            public AcSoundBank Bank;
            AcSndBank _meta; AcCarSounds _sounds; double _ms;
            bool _mixed;
            internal override void Run()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                FmodGuids g = Guids(Bank.GuidsTxt, Bank.CarGuids);
                string prefer = Bank.IsCommon ? null : "/cars/" + Bank.Label + "/";
                if (Bank.IsCommon) _meta = Mixer(Bank.BankPath, Bank.GuidsTxt);
                else
                {
                    _meta = AcSndBank.Load(Bank.BankPath, g, prefer);
                    // the master mixer (common.bank grp_* group volumes) the car's events are routed into
                    AcSndBank mix = null;
                    try { if (_commonPath != null) mix = Mixer(_commonPath, Bank.GuidsTxt); } catch (Exception) { }
                    _meta.ApplyMixer(mix);
                    _mixed = mix != null;
                }
                if (!Bank.IsCommon && Bank.CarDir != null) _sounds = AcCarSounds.Load(Bank.CarDir, Bank.Label);
                _ms = sw.Elapsed.TotalMilliseconds;
            }
            internal override void Done()
            {
                if (Error != null || _meta == null || _meta.Fsb == null)
                {
                    Bank.Error = Error != null ? Error.Message : "no FSB5 in the bank";
                    AcLog.Warn("Audio: " + Bank.Label + " bank could not be read: " + Bank.Error + " - that car stays silent");
                    Bank.MetaReady = true;
                    return;
                }
                if (!_meta.Fsb.IsPcm16)
                {
                    Bank.Error = "FSB5 mode " + _meta.Fsb.Mode + " is not PCM16";
                    AcLog.Warn("Audio: " + Bank.Label + ": " + Bank.Error + " (mod car?) - silent");
                    Bank.MetaReady = true;
                    return;
                }
                Bank.Meta = _meta;
                Bank.Sounds = _sounds;
                int n = _meta.Fsb.Samples.Count;
                Bank._clips = new AudioClip[n];
                Bank._state = new byte[n];
                Bank.MetaReady = true;
                Bank.LogMeta(_ms, _mixed);
            }
        }

        static FmodGuids Guids(string install, string car)
        {
            FmodGuids g;
            lock (GuidLock)
            {
                if (_installGuids == null && install != null && File.Exists(install)) _installGuids = FmodGuids.Load(install);
                g = _installGuids;
            }
            if (car == null || !File.Exists(car)) return g;
            var own = install != null && File.Exists(install) ? FmodGuids.Load(install) : new FmodGuids();
            own.AddFile(car);
            return own;
        }

        void LogMeta(double ms, bool mixed)
        {
            var m = Meta;
            double mb = 0;
            for (int i = 0; i < m.Fsb.Samples.Count; i++) mb += m.Fsb.Samples[i].Pcm16Bytes / 1048576.0;
            var sb = new StringBuilder();
            sb.Append("Audio: ").Append(Label).Append(" bank: ").Append(m.Fsb.Samples.Count).Append(" samples (").Append(mb.ToString("0.0")).Append(" MB PCM)");
            if (!IsCommon)
            {
                var ei = m.Event("engine_int"); var ee = m.Event("engine_ext");
                sb.Append(", engine_int ").Append(ei != null ? ei.Instruments.Length : 0).Append(" layers, engine_ext ").Append(ee != null ? ee.Instruments.Length : 0).Append(" layers");
            }
            sb.Append(", events found [");
            for (int i = 0; i < m.Events.Count; i++) { if (i > 0) sb.Append(' '); sb.Append(IsCommon ? m.Events[i].Path.Replace("event:/", "") : m.Events[i].Name); }
            sb.Append("]");
            if (m.Multis > 0) sb.Append(", ").Append(m.Multis).Append(" multi instruments");
            // DSP effects (spatialisers are the 3D settings; the rest are filters / plugins, "~param" = automated)
            int spatial = 0;
            var fx = new StringBuilder();
            for (int i = 0; i < m.Events.Count; i++)
            {
                var ev = m.Events[i];
                if (ev.Spatialiser != null) spatial++;
                bool head = false;
                for (int k = 0; k < ev.Effects.Length; k++)
                {
                    var f = ev.Effects[k];
                    if (f.Dsp == AcSndFx.DspPan || f.Dsp == 30 || f.Dsp == 29 || f.Dsp == 7 || f.Dsp == 1) continue;
                    if (IsCommon && fx.Length > 300) break;
                    fx.Append(head ? ", " : (fx.Length > 0 ? "; " : "") + ev.Name + ": ").Append(f.Kind);
                    for (int a = 0; a < f.Autos.Length; a++) fx.Append('~').Append(ev.Params[f.Autos[a].Param]);
                    head = true;
                }
            }
            sb.Append(", ").Append(spatial).Append(" events 3D by their spatialiser");
            if (fx.Length > 0) sb.Append(", effects [").Append(fx).Append(']');
            if (m.Unresolved > 0) sb.Append(", ").Append(m.Unresolved).Append(" unresolved placements");
            if (!IsCommon) sb.Append(mixed ? ", routed into the common.bank mixer groups" : ", NO master mixer (common.bank unreadable): group volumes missing");
            sb.Append(" in ").Append(ms.ToString("0")).Append(" ms");
            if (Sounds != null) sb.Append("; sounds.ini: ").Append(Sounds).Append(Sounds.Problems.Count > 0 ? " (" + string.Join("; ", Sounds.Problems.ToArray()) + ")" : "");
            AcLog.Info(sb.ToString());
        }

        /// <summary>Queues every sample of an event for conversion + upload (no-op for samples already queued or ready).</summary>
        public void Require(AcSndEvent ev, bool high)
        {
            if (ev == null || Meta == null) return;
            var ins = ev.Instruments;
            for (int i = 0; i < ins.Length; i++)
                for (int k = 0; k < ins[i].Samples.Length; k++) RequireSample(ins[i].Samples[k], high);
        }

        /// <summary>Queues one instrument's samples only.</summary>
        public void Require(AcSndInstrument ins, bool high)
        {
            if (ins == null || Meta == null) return;
            for (int k = 0; k < ins.Samples.Length; k++) RequireSample(ins.Samples[k], high);
        }

        void RequireSample(int s, bool high)
        {
            if (s < 0 || s >= _state.Length) return;
            if (_state[s] != None) { if (_state[s] == Ready) _cacheHits++; return; }
            _state[s] = Queued;
            if (_pendingBatch == 0) { _batchStart = Time.realtimeSinceStartup; _batchClips = 0; _batchFloats = 0; }
            _pendingBatch++;
            AcAudioWorker.Enqueue(new DecodeJob { Bank = this, Sample = s }, high);
        }

        sealed class DecodeJob : AcAudioWorker.Job
        {
            public AcSoundBank Bank;
            public int Sample;
            float[] _pcm;
            internal override void Run()
            {
                var smp = Bank.Meta.Fsb.Samples[Sample];
                AcAudioWorker.WaitForRoom(smp.Frames * smp.Channels);
                try { _pcm = Bank.Meta.ReadSampleFloats(Sample); }
                catch { AcAudioWorker.Release(smp.Frames * smp.Channels); throw; }
            }
            internal override void Done()
            {
                var smp = Bank.Meta != null ? Bank.Meta.Fsb.Samples[Sample] : null;
                if (Error != null || _pcm == null || smp == null || smp.Frames <= 0)
                {
                    if (smp != null && _pcm == null && Error == null) AcAudioWorker.Release(smp.Frames * smp.Channels);
                    Bank.Finish(Sample, null, false, Error != null ? Error.Message : "empty sample");
                    return;
                }
                AudioClip clip;
                try { clip = AudioClip.Create(Bank.Label + ":" + smp.Name, (int)smp.Frames, smp.Channels, smp.Rate, false); }
                catch (Exception e) { AcAudioWorker.Release(_pcm.Length); Bank.Finish(Sample, null, false, e.Message); return; }
                AcAudioWorker.QueueUpload(clip, _pcm, smp.Channels, Bank, Sample);
                _pcm = null;
            }
        }

        void Finish(int s, AudioClip clip, bool ok, string why)
        {
            if (s < 0 || s >= _state.Length) { if (clip != null) UnityEngine.Object.Destroy(clip); return; }
            if (ok && clip != null)
            {
                _clips[s] = clip; _state[s] = Ready; _batchClips++;
                var smp = Meta.Fsb.Samples[s];
                _batchFloats += smp.Frames * smp.Channels;
            }
            else
            {
                _state[s] = Failed;
                if (clip != null) UnityEngine.Object.Destroy(clip);
                AcLog.WarnOnce("Audio: " + Label + " sample '" + Meta.SampleName(s) + "' failed: " + why);
            }
            if (--_pendingBatch <= 0)
            {
                _pendingBatch = 0;
                if (!_builtLogged)
                {
                    // one line per bank (the first batch = the car's sound set); later batches (another car's events, the interior set,
                    // a remote car of the model) are counted, not logged
                    _builtLogged = true;
                    AcLog.Info("Audio: clips built for " + Label + " (" + _batchClips + " clips, " + (_batchFloats * 4.0 / 1048576.0).ToString("0.0") + " MB, cache hits " + _cacheHits + ") in "
                               + (Time.realtimeSinceStartup - _batchStart).ToString("0.0") + " s");
                }
                else { _laterBatches++; _laterClips += _batchClips; }
            }
        }

        /// <summary>IUploadSink: a sample's clip is uploaded (or failed).</summary>
        public void Uploaded(int tag, AudioClip clip, bool ok) { Finish(tag, clip, ok, ok ? null : "upload failed"); }

        /// <summary>Clip batches after the first one (not logged), and their clips.</summary>
        public int LaterBatches { get { return _laterBatches; } }
        public int LaterClips { get { return _laterClips; } }

        /// <summary>Every sample of the event is uploaded (or failed - a failed sample just stays silent).</summary>
        public bool IsReady(AcSndEvent ev)
        {
            if (ev == null || Meta == null) return false;
            var ins = ev.Instruments;
            for (int i = 0; i < ins.Length; i++)
                for (int k = 0; k < ins[i].Samples.Length; k++)
                {
                    int s = ins[i].Samples[k];
                    if (s >= 0 && s < _state.Length && (_state[s] == None || _state[s] == Queued)) return false;
                }
            return true;
        }

        public AudioClip Clip(int sample) { return sample >= 0 && sample < _clips.Length ? _clips[sample] : null; }

        /// <summary>The sample is uploaded or failed for good (out-of-range indices count as done: nothing to wait for).</summary>
        public bool IsDone(int sample) { return sample < 0 || sample >= _state.Length || _state[sample] == Ready || _state[sample] == Failed; }

        public int ClipCount
        {
            get { int n = 0; for (int i = 0; i < _clips.Length; i++) if (_clips[i] != null) n++; return n; }
        }

        void FreeClips()
        {
            int n = 0;
            for (int i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null) { UnityEngine.Object.Destroy(_clips[i]); n++; }
                _clips[i] = null;
                if (_state[i] == Ready) _state[i] = None;
            }
            if (n > 0) AcLog.Info("Audio: freed " + n + " clips of " + Label + " (unused for " + IdleFreeSeconds + " s)");
        }

        /// <summary>Main thread, now and then: frees the clips of banks nobody used for a while (never while a batch is loading).</summary>
        public static void Collect()
        {
            float now = Time.unscaledTime;
            foreach (var b in Cache.Values)
                if (b._users == 0 && b._idleSince >= 0f && now - b._idleSince > IdleFreeSeconds && b._pendingBatch == 0)
                {
                    b.FreeClips();
                    b._idleSince = -1f;
                }
        }

        /// <summary>Destroys every clip (the Dreamport is closing).</summary>
        public static void ReleaseAll()
        {
            foreach (var b in Cache.Values)
            {
                for (int i = 0; i < b._clips.Length; i++) if (b._clips[i] != null) UnityEngine.Object.Destroy(b._clips[i]);
            }
            Cache.Clear();
        }

        public static int BankCount { get { return Cache.Count; } }
    }
}
