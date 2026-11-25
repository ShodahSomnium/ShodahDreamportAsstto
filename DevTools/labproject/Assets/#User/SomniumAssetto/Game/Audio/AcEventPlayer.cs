using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>The game values an AC event parameter can be fed with (by parameter name; ac_physics_sound_rules 2.6 + pipeline PDF table).</summary>
    internal static class AcFeed
    {
        public const int Rpm = 0, Throttle = 1, Speed = 2, State = 3, Decay = 4, Brake = 5, Inflation = 6, SuspDamage = 7, AirPressure = 8,
            SuspTravelSpeed = 9, Boost = 10, Bov = 11, BovDecay = 12, DrivetrainSpeed = 13, TransmissionLoad = 14, ImpactSpeed = 15,
            ImpactAngle = 16, Dirtiness = 17, Distance = 18, ScaledDistance = 19, ConeAngle = 20, Count = 21;

        /// <summary>air_pressure held for still air (no slipstream simulated). The banks' wind event crossfades wind_draft (0.8..1.05)
        /// into wind (0.98..1.5) between 0.98 and 1.05 and its bus reaches 0 dB at 1.073: 1.1 = wind only, the draft layer silent,
        /// as a car alone on the road. // ours (AC lowers air_pressure in a slipstream; we have none)</summary>
        public const float StillAir = 1.1f;

        public static int Of(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            string n = name.ToLowerInvariant();
            switch (n)
            {
                case "rpms": case "rpm": return Rpm;
                case "throttle": return Throttle;
                case "speed": return Speed;
                case "state": return State;
                case "decay": return Decay;
                case "brake": return Brake;
                case "inflation": return Inflation;
                case "suspension_damage": return SuspDamage;
                case "air_pressure": return AirPressure;
                case "susp_travel_speed": return SuspTravelSpeed;
                case "boost": return Boost;
                case "bov": return Bov;
                case "bov_decay": return BovDecay;
                case "drivetrain_speed": return DrivetrainSpeed;
                case "transmission_load": return TransmissionLoad;
                case "impact_speed": return ImpactSpeed;
                case "impact_angle": return ImpactAngle;
                case "dirtiness": return Dirtiness;
                case "distance": return Distance;
                case "scaled_distance": return ScaledDistance;
                case "event cone angle": return ConeAngle;
            }
            return n.IndexOf("rpm", StringComparison.Ordinal) >= 0 ? Rpm : -1;
        }

        /// <summary>Values held constant because nothing in the Dreamport changes them: tyres inflated (1), no suspension damage, still
        /// air (<see cref="StillAir"/>), clean tyres. Instruments whose region excludes these never play and are not decoded.</summary>
        public static bool Constant(int id, out float v) { return Constant(id, out v, false); }

        /// <summary>As <see cref="Constant(int, out float)"/>; with <paramref name="suspDamageLive"/> the suspension_damage parameter is FED
        /// (DAMAGE_DESIGN 6: the car's own max steer-rod damage 0..1 -> the wheel event's flutter layer) instead of pinned at 0. A player re-
        /// decides this every Drive (<see cref="DamageOn"/>, fixer 2026-11-02): at DAMAGE 0 the value stays pinned at 0 exactly as in iteration 1
        /// (the 458's wheel event: flutter_4 on suspension_damage 0..1, bus -42 dB at 0.44 -&gt; -0.17 dB at 0.75, harness fev).</summary>
        public static bool Constant(int id, out float v, bool suspDamageLive)
        {
            switch (id)
            {
                case Inflation: v = 1f; return true;
                case SuspDamage: if (suspDamageLive) break; v = 0f; return true;
                case AirPressure: v = StillAir; return true;
                case Dirtiness: v = 0f; return true;
            }
            v = 0f; return false;
        }

        /// <summary>The session's damage multiplier is above 0 (R's AcSession.Rules; lobby DAMAGE). Read when a player is built.</summary>
        public static bool DamageOn()
        {
            if (AcCarAudio.LabDamageOn) return true;
            try { AcRules r = AcSession.Rules; return r != null && r.DamageMultiplier > 0; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// [A, Audio] Plays one AC event (<see cref="AcSndEvent"/>) with ordinary AudioSources, the way FMOD does for AC's banks, without
    /// allocation per frame. Everything is created when the player is built (car spawn - never mid-drive): one AudioSource per playable
    /// instrument and, when the event's buses carry DSP effects Unity can run, a child GameObject per effect set holding them
    /// (AudioLowPassFilter for FMOD lowpass / lowpass simple / the Distance Filter plugin, AudioHighPassFilter for highpass, and
    /// AudioDistortionFilter for the "FMOD Distortion" plugin - Unity's audio is FMOD, these components are FMOD's DSPs), so a filter
    /// never touches another event's sources. Effect parameters follow their automations every frame (engine_ext lowpass on the event
    /// cone angle, engine_int distortion on rpm). Unity runs these filters after AudioSource.volume, so a distortion bus plays its sources
    /// at the level before its downstream faders and an AudioEchoFilter (wet 0) applies those faders after the waveshaper. Three-EQ / param-EQ / reverb effects have no Unity component and are not reproduced
    /// (one log line per event kind).
    ///  - Looping instruments (INST loop -1, or timeline instruments of an event with a loop region) play while the event plays and
    ///    their gain is audible; gain / pitch from the instrument's region, crossfade automations, bus curves and auto-pitch every frame;
    ///    a loop starts at a random point (FMOD starts loops mid-sample) and stops after being silent for <see cref="QuietStop"/> s.
    ///  - One-shots (loop 0) fire when the event starts with their region containing the values, or when the values enter their region
    ///    later (FMOD sheet behaviour: R8 shift pops on the rpm sheet), or when the timeline cursor reaches them; automations keep
    ///    shaping a playing one-shot. Multi instruments pick a playlist entry by weight each time (AC backfires); a looping multi
    ///    instrument keeps picking entries for its timeline length (the backfire burst).
    ///  - An event without a loop region whose instruments are all on the timeline ends after its timeline length.
    /// </summary>
    internal sealed class AcEventPlayer
    {
        /// <summary>A loop silent for this long is stopped (it is at about -66 dB already, so no click). // ours</summary>
        public const float QuietStop = 0.5f;

        public readonly AcSndEvent Ev;
        public readonly AcSoundBank Bank;
        public readonly Transform At;
        public readonly AcAudioGroup Group;
        public readonly float[] Values;
        /// <summary>3D with the event's spatialiser distances, or 2D.</summary>
        public bool Spatial { get; private set; }
        /// <summary>The listener rides in this car (no doppler).</summary>
        public bool Local { get; private set; }
        /// <summary>Extra linear gain set by code (sounds.ini skid MIX_VOLUME x slide, backfire VOLUME_IN/OUT ...).</summary>
        public float Gain = 1f;
        /// <summary>The whole car's fade (feed timeout). // ours</summary>
        public float CarGain = 1f;
        public float PitchMul = 1f;
        public bool Playing { get; private set; }
        /// <summary>Muted by the budget / culling: sources stopped, state kept.</summary>
        public bool Muted;
        public float StartedAt { get; private set; }

        sealed class Host
        {
            public GameObject Go;
            public bool Own;
            public AcSndFx[] Low, High, Dist;
            public AudioLowPassFilter Lp;
            public AudioHighPassFilter Hp;
            public AudioDistortionFilter Dx;
            /// <summary>The faders that follow the distortion (bus static + mixer group + the car's / player's volumes) as a pure gain after
            /// it: an AudioEchoFilter with wet 0. Unity runs a source's filters AFTER AudioSource.volume (lab probe 2026-11-01).</summary>
            public AudioEchoFilter Post;
            public float LastLp = -1f, LastHp = -1f, LastDx = -1f, Down = 1f, LastPost = -1f, Scale = 1f, MaxPre;
        }

        readonly int[] _feed;
        /// <summary>suspension_damage is fed (damage on NOW), not pinned at 0 (AcFeed.Constant). Fixer 2026-11-02: decided every Drive, not
        /// once at build, so a lobby raising DAMAGE from 0 mid-session feeds the flutter layer at once (design 1.4).</summary>
        bool _suspLive;
        /// <summary>The event has a suspension_damage parameter (the wheel event): only then is <see cref="AcFeed.DamageOn"/> read per Drive.</summary>
        readonly bool _hasSusp;
        readonly float[] _local = new float[AcFeed.Count];
        readonly bool[] _localSet = new bool[AcFeed.Count];
        readonly AudioSource[] _src;
        readonly int[] _hostOf;
        readonly Host[] _hosts;
        readonly float[] _quiet, _g, _p;
        readonly bool[] _wasIn, _skip;
        readonly bool _timelineOnly;
        readonly float _duration, _lastStart;
        bool _ready, _required, _autoStop;
        float _stopAt = -1f;

        static readonly HashSet<string> LoggedFx = new HashSet<string>();

        public AcEventPlayer(AcSndEvent ev, AcSoundBank bank, Transform at, AcAudioGroup group, bool spatial, bool local)
        {
            Ev = ev; Bank = bank; At = at; Group = group; Spatial = spatial; Local = local;
            Values = ev.NewValues();
            _suspLive = AcFeed.DamageOn();
            _feed = new int[ev.Params.Length];
            for (int i = 0; i < _feed.Length; i++)
            {
                _feed[i] = AcFeed.Of(ev.Params[i]);
                if (_feed[i] == AcFeed.SuspDamage) _hasSusp = true;
                float c;
                if (_feed[i] >= 0 && AcFeed.Constant(_feed[i], out c, _suspLive)) Values[i] = c;
            }
            int n = ev.Instruments.Length;
            _src = new AudioSource[n];
            _hostOf = new int[n];
            _quiet = new float[n];
            _g = new float[n];
            _p = new float[n];
            _wasIn = new bool[n];
            _skip = new bool[n];
            bool tl = n > 0;
            long end = 0, last = 0;
            for (int i = 0; i < n; i++)
            {
                var ins = ev.Instruments[i];
                if (ins.RegionParam >= 0) tl = false;
                else { end = Math.Max(end, ins.TimelinePos + Math.Max(0, ins.TimelineLength)); last = Math.Max(last, ins.TimelinePos); }
                // never audible with the constant values (tyre explosion, flat tyre, damage flutter, slipstream draft): not decoded, never played
                if (ins.RegionParam >= 0)
                {
                    int f = _feed[ins.RegionParam]; float c;
                    // fixer 2026-11-02: suspension_damage never counts as constant here (DAMAGE can rise mid-session), so the flutter layer
                    // is prepared; at DAMAGE 0 its value stays pinned at 0 in Drive, i.e. it stays outside its region and silent as before
                    if (f >= 0 && AcFeed.Constant(f, out c, true) && (c < ins.RegionStart || c > ins.RegionEnd)) _skip[i] = true;
                }
            }
            _timelineOnly = tl;
            _duration = end / 48000f;
            _lastStart = last / 48000f;
            _hosts = BuildHosts();
        }

        // ------------------------------------------------------------------ build (once)

        static bool IsLow(AcSndFx f) { return f.Dsp == AcSndFx.DspLowpass || f.Dsp == AcSndFx.DspLowpassSimple || f.IsPlugin("Distance Filter") || f.IsPlugin("Lowpass"); }
        static bool IsHigh(AcSndFx f) { return f.Dsp == AcSndFx.DspHighpass || f.Dsp == AcSndFx.DspHighpassSimple || f.IsPlugin("Highpass"); }
        static bool IsDist(AcSndFx f) { return f.IsPlugin("Distortion"); }

        static bool Contains(int[] a, int v) { for (int i = 0; i < a.Length; i++) if (a[i] == v) return true; return false; }

        static bool Same(List<AcSndFx> a, AcSndFx[] b)
        {
            if (b == null) return a.Count == 0;
            if (a.Count != b.Length) return false;
            for (int i = 0; i < b.Length; i++) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        Host[] BuildHosts()
        {
            var hosts = new List<Host>();
            var low = new List<AcSndFx>(); var high = new List<AcSndFx>(); var dist = new List<AcSndFx>();
            var fx = Ev.Effects;
            for (int i = 0; i < _src.Length; i++)
            {
                _hostOf[i] = -1;
                if (_skip[i] || At == null) continue;
                low.Clear(); high.Clear(); dist.Clear();
                // signal order: the instrument's group buses first, the master bus last (Effects is master-first)
                for (int k = fx.Length - 1; k >= 0; k--)
                {
                    var f = fx[k];
                    if (!f.OnMaster && !Contains(f.Instruments, i)) continue;
                    if (IsLow(f)) low.Add(f); else if (IsHigh(f)) high.Add(f); else if (IsDist(f)) dist.Add(f);
                    else if (f.Dsp != AcSndFx.DspPan && f.Dsp != 30 && f.Dsp != 29 && f.Dsp != 7 && f.Dsp != 1) LogUnsupported(f);   // loudness meter / FFT / fader / mixer change nothing
                }
                int h = -1;
                for (int k = 0; k < hosts.Count; k++)
                    if (Same(low, hosts[k].Low) && Same(high, hosts[k].High) && Same(dist, hosts[k].Dist)) { h = k; break; }
                if (h < 0)
                {
                    var host = new Host { Low = low.ToArray(), High = high.ToArray(), Dist = dist.ToArray() };
                    bool any = host.Low.Length + host.High.Length + host.Dist.Length > 0;
                    if (any)
                    {
                        host.Go = new GameObject("Ac Snd " + Ev.Name);
                        host.Go.transform.SetParent(At, false);
                        host.Own = true;
                    }
                    else host.Go = At.gameObject;
                    hosts.Add(host);
                    h = hosts.Count - 1;
                }
                _hostOf[i] = h;
                var s = hosts[h].Go.AddComponent<AudioSource>();
                AcAudioMix.Configure(s, Ev, Spatial, Local);
                _src[i] = s;
            }
            // filters after the sources (Unity runs a GameObject's filters in component order: group effects, then the master's)
            for (int k = 0; k < hosts.Count; k++)
            {
                var host = hosts[k];
                if (host.Dist.Length > 0)
                {
                    host.Dx = host.Go.AddComponent<AudioDistortionFilter>();
                    host.Dx.distortionLevel = 0f;
                    var d = host.Dist[host.Dist.Length - 1];
                    float mixer = 0f;
                    for (int i = 0; i < _hostOf.Length; i++) if (_hostOf[i] == k) { mixer = Ev.Instruments[i].MixerDb; break; }
                    host.Down = Mathf.Max(1e-4f, Mathf.Pow(10f, (d.DownstreamDb + mixer) / 20f));
                    // FMOD's order: waveshaper, then the downstream faders - the sources play at the bus level before them, this gain follows
                    host.Post = host.Go.AddComponent<AudioEchoFilter>();
                    host.Post.delay = 10f; host.Post.decayRatio = 0f; host.Post.wetMix = 0f; host.Post.dryMix = host.Down;
                }
                if (host.High.Length > 0)
                {
                    host.Hp = host.Go.AddComponent<AudioHighPassFilter>();
                    host.Hp.highpassResonanceQ = Mathf.Clamp(host.High[0].Dsp == AcSndFx.DspHighpass ? host.High[0].P(1, 1f) : 1f, 1f, 10f);
                    host.Hp.cutoffFrequency = 10f;
                }
                if (host.Low.Length > 0)
                {
                    host.Lp = host.Go.AddComponent<AudioLowPassFilter>();
                    float q = 1f;
                    for (int i = 0; i < host.Low.Length; i++) if (host.Low[i].Dsp == AcSndFx.DspLowpass) q = host.Low[i].P(1, 1f);
                    host.Lp.lowpassResonanceQ = Mathf.Clamp(q, 1f, 10f);
                    host.Lp.cutoffFrequency = 22000f;
                }
            }
            return hosts.ToArray();
        }

        void LogUnsupported(AcSndFx f)
        {
            // a three-EQ at 0 / 0 / 0 dB (not automated) changes nothing
            if (f.Dsp == AcSndFx.DspThreeEq && f.Autos.Length == 0 && Mathf.Abs(f.P(0, 0f)) < 0.01f && Mathf.Abs(f.P(1, 0f)) < 0.01f && Mathf.Abs(f.P(2, 0f)) < 0.01f) return;
            string key = Ev.Name + ":" + f.Kind;
            if (!LoggedFx.Add(key)) return;
            AcLog.Info("Audio: event " + Ev.Name + " has a " + f.Kind + " effect " + (f.OnMaster ? "on its master bus" : "on a group bus") + " - Unity has no component for it, not reproduced");
        }

        /// <summary>Queues this event's samples (once).</summary>
        public void Require(bool high)
        {
            if (_required || Bank == null) return;
            _required = true;
            for (int i = 0; i < _skip.Length; i++) if (!_skip[i]) Bank.Require(Ev.Instruments[i], high);
        }

        public bool Required { get { return _required; } }

        public bool Ready
        {
            get
            {
                if (_ready) return true;
                if (!_required || Bank == null || Bank.Meta == null) return false;
                for (int i = 0; i < _skip.Length; i++)
                {
                    if (_skip[i]) continue;
                    var s = Ev.Instruments[i].Samples;
                    for (int k = 0; k < s.Length; k++) if (!Bank.IsDone(s[k])) return false;
                }
                _ready = true;
                return true;
            }
        }

        /// <summary>Sets a value for this player only (overrides the car's shared feed): door state, decay of this limiter ...</summary>
        public void Set(int feedId, float v) { if (feedId >= 0 && feedId < AcFeed.Count) { _local[feedId] = v; _localSet[feedId] = true; } }

        /// <summary>Gives a value back to the car's shared feed.</summary>
        public void Clear(int feedId) { if (feedId >= 0 && feedId < AcFeed.Count) _localSet[feedId] = false; }

        /// <summary>Starts the event (its in-region one-shots fire on the next Drive).</summary>
        public void Start()
        {
            if (Playing) return;
            Playing = true;
            _autoStop = false;
            _stopAt = -1f;
            StartedAt = Time.time;
            for (int i = 0; i < _wasIn.Length; i++) _wasIn[i] = false;
        }

        /// <summary>Starts the event and lets it end by itself (one-shot events: gear, door, collisions, backfire). Restarts if playing.</summary>
        public void Trigger()
        {
            Playing = false;
            Start();
            _autoStop = true;
        }

        /// <summary>Plays a timeline-loop event for one pass of its instruments, then stops (gear grind). // ours (AC holds it while grinding)</summary>
        public void PlayFor(float seconds)
        {
            Start();
            _stopAt = Time.time + Mathf.Max(0.05f, seconds);
        }

        public float TimelineSeconds { get { return _duration; } }

        /// <summary>Stops the event and its sources.</summary>
        public void Stop()
        {
            Playing = false;
            for (int i = 0; i < _src.Length; i++) if (_src[i] != null && _src[i].isPlaying) _src[i].Stop();
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Main thread, once a frame: values from <paramref name="shared"/> (indexed by AcFeed), gain / pitch to the sources,
        /// effect parameters to the filters.</summary>
        public void Drive(float[] shared, float dt)
        {
            if (!Playing) return;
            if (Muted || !Ready) { StopSources(); if (_autoStop) Playing = false; return; } // a one-shot that cannot play now is dropped, not replayed late
            if (_stopAt > 0f && Time.time >= _stopAt) { Stop(); return; }
            Vector3 p = At.position;
            float distance = AcAudioMix.HasListener ? Vector3.Distance(p, AcAudioMix.ListenerPos) : 0f;
            if (_hasSusp) _suspLive = AcFeed.DamageOn();       // fixer 2026-11-02: per Drive (a field read + the rules), no allocation
            for (int i = 0; i < _feed.Length; i++)
            {
                int f = _feed[i];
                if (f < 0) continue;
                float c;
                if (AcFeed.Constant(f, out c, _suspLive)) { Values[i] = c; continue; }
                if (_localSet[f]) { Values[i] = _local[f]; continue; }
                if (f == AcFeed.Distance || f == AcFeed.ScaledDistance) { Values[i] = Spatial ? distance : 0f; continue; }
                if (f == AcFeed.ConeAngle) { Values[i] = AcAudioMix.HasListener && Spatial ? Vector3.Angle(At.forward, AcAudioMix.ListenerPos - p) : 0f; continue; }
                Values[i] = shared != null ? shared[f] : Values[i];
            }
            DriveFilters(distance);
            float master = AcAudioMix.Volume(Group) * Gain * CarGain;
            float t = Time.time - StartedAt;
            double cursor = t * 48000.0;
            bool any = false;
            var list = Ev.Instruments;
            // gains first: a bus with a distortion plays its sources at the level before its downstream faders (what FMOD's waveshaper
            // sees), scaled down together when one would pass 1 (AudioSource.volume caps at 1); the echo filter's dry gain adds the faders after
            for (int k = 0; k < _hosts.Length; k++) _hosts[k].MaxPre = 0f;
            for (int i = 0; i < list.Length; i++)
            {
                if (_src[i] == null) continue;
                Ev.Evaluate(i, Values, out _g[i], out _p[i]);
                int hk = _hostOf[i];
                if (hk >= 0 && _hosts[hk].Post != null) { var h = _hosts[hk]; float pre = _g[i] / h.Down; if (pre > h.MaxPre) h.MaxPre = pre; }
            }
            for (int k = 0; k < _hosts.Length; k++)
            {
                var h = _hosts[k];
                if (h.Post == null) continue;
                h.Scale = h.MaxPre > 1f ? h.MaxPre : 1f;
                float post = Mathf.Min(1f, h.Down * master * h.Scale);
                if (Mathf.Abs(post - h.LastPost) > 1e-5f) { h.Post.dryMix = post; h.LastPost = post; }
            }
            for (int i = 0; i < list.Length; i++)
            {
                AudioSource s = _src[i];
                if (s == null) continue;
                var ins = list[i];
                bool timeline = ins.RegionParam < 0;
                bool loops = ins.Loops || (timeline && Ev.TimelineLoops);
                bool active = timeline ? (Ev.TimelineLoops || (cursor >= ins.TimelinePos && cursor < ins.TimelinePos + ins.TimelineLength)) : Ev.InRegion(i, Values);
                float g = _g[i], pitch = _p[i];
                float heard = g * master;   // what reaches the output (the silence test)
                int hi = _hostOf[i];
                float v = hi >= 0 && _hosts[hi].Post != null ? g / (_hosts[hi].Down * _hosts[hi].Scale) : heard;
                pitch *= PitchMul;
                if (pitch < 0.05f) pitch = 0.05f; else if (pitch > 3f) pitch = 3f;
                if (v > 1f) v = 1f;
                if (loops)
                {
                    if (active && heard > AcAudioMix.Silent)
                    {
                        s.volume = v; s.pitch = pitch;
                        if (!s.isPlaying)
                        {
                            AudioClip clip = Pick(ins);
                            if (clip != null)
                            {
                                s.clip = clip;
                                s.loop = !ins.Multi;
                                if (!ins.Multi && !timeline) s.time = UnityEngine.Random.value * clip.length * 0.9f;
                                else s.time = 0f;
                                s.Play();
                            }
                        }
                        _quiet[i] = 0f;
                        any = true;
                    }
                    else if (s.isPlaying)
                    {
                        s.volume = active ? v : 0f;
                        _quiet[i] += dt;
                        if (!active || _quiet[i] >= QuietStop) s.Stop();
                        else any = true;
                    }
                }
                else
                {
                    if (active && !_wasIn[i] && heard > AcAudioMix.Silent)
                    {
                        AudioClip clip = Pick(ins);
                        if (clip != null)
                        {
                            s.clip = clip; s.loop = false; s.time = 0f; s.volume = v; s.pitch = pitch;
                            s.Play();
                        }
                    }
                    if (s.isPlaying) { s.volume = v; s.pitch = pitch; any = true; }
                }
                _wasIn[i] = active;
            }
            if (_autoStop && !any && t > _lastStart + 0.05f) Playing = false;
            else if (_timelineOnly && !Ev.TimelineLoops && !any && t > _duration + 0.05f) Playing = false;
        }

        void DriveFilters(float distance)
        {
            for (int k = 0; k < _hosts.Length; k++)
            {
                var h = _hosts[k];
                if (h.Lp != null)
                {
                    float c = 22000f;
                    for (int i = 0; i < h.Low.Length; i++) c = Mathf.Min(c, LowCutoff(h.Low[i], distance));
                    if (Mathf.Abs(c - h.LastLp) > 0.5f) { h.Lp.cutoffFrequency = c; h.LastLp = c; }
                }
                if (h.Hp != null)
                {
                    float c = 10f;
                    for (int i = 0; i < h.High.Length; i++) c = Mathf.Max(c, Freq(h.High[i], 0, 10f));
                    if (Mathf.Abs(c - h.LastHp) > 0.5f) { h.Hp.cutoffFrequency = c; h.LastHp = c; }
                }
                if (h.Dx != null)
                {
                    float level = 0f;
                    for (int i = 0; i < h.Dist.Length; i++) level = Mathf.Max(level, Mathf.Clamp01(h.Dist[i].Value(0, Values, 0.5f)));
                    if (Mathf.Abs(level - h.LastDx) > 0.002f) { h.Dx.distortionLevel = level; h.LastDx = level; }
                }
            }
        }

        /// <summary>A frequency parameter: its automation (stored as a 0..10 position) or the static value (stored in Hz).</summary>
        float Freq(AcSndFx f, int index, float fallback)
        {
            if (f.Automated(index)) return AcSndFx.CutoffFromPosition(f.Value(index, Values, 10f));
            return Mathf.Clamp(f.P(index, fallback), 10f, 22000f);
        }

        /// <summary>FMOD Distance Filter plugin (max distance, cutoff): the cutoff closes from 22 kHz at the emitter to its value at the max
        /// distance, log-spaced. // ours shape (the plugin is not documented)</summary>
        float LowCutoff(AcSndFx f, float distance)
        {
            if (f.IsPlugin("Distance Filter"))
            {
                if (!Spatial) return 22000f;
                float max = Mathf.Max(1f, f.P(0, 300f)), cut = Mathf.Clamp(f.P(1, 1750f), 10f, 22000f);
                float u = Mathf.Clamp01(distance / max);
                return 22000f * Mathf.Pow(cut / 22000f, u);
            }
            return Freq(f, 0, 22000f);
        }

        void StopSources()
        {
            for (int i = 0; i < _src.Length; i++) if (_src[i] != null && _src[i].isPlaying) _src[i].Stop();
        }

        AudioClip Pick(AcSndInstrument ins)
        {
            if (ins.Samples.Length == 1) return Bank.Clip(ins.Samples[0]);
            float sum = 0f;
            for (int k = 0; k < ins.Weights.Length; k++) sum += ins.Weights[k];
            float r = UnityEngine.Random.value * sum;
            for (int k = 0; k < ins.Samples.Length; k++)
            {
                r -= ins.Weights[k];
                if (r <= 0f) return Bank.Clip(ins.Samples[k]);
            }
            return Bank.Clip(ins.Samples[ins.Samples.Length - 1]);
        }

        /// <summary>Re-applies the 3D settings: 2D / 3D, the listener aboard (doppler off) or not, the doppler setting.</summary>
        public void Reconfigure(bool spatial, bool local)
        {
            Spatial = spatial; Local = local;
            for (int i = 0; i < _src.Length; i++) if (_src[i] != null) AcAudioMix.Configure(_src[i], Ev, Spatial, Local);
        }

        /// <summary>Re-applies the current 3D settings.</summary>
        public void Reconfigure() { Reconfigure(Spatial, Local); }

        /// <summary>Playing sources (for the budget log and the lab tap).</summary>
        public int Voices
        {
            get { int n = 0; for (int i = 0; i < _src.Length; i++) if (_src[i] != null && _src[i].isPlaying) n++; return n; }
        }

        /// <summary>AudioSources this player owns (built with it).</summary>
        public int Sources
        {
            get { int n = 0; for (int i = 0; i < _src.Length; i++) if (_src[i] != null) n++; return n; }
        }

        /// <summary>Adds a snapshot of every playing source (lab / diagnostics; allocates only into the caller's list).</summary>
        public void Tap(List<AcAudioVoice> into)
        {
            for (int i = 0; i < _src.Length; i++)
            {
                var s = _src[i];
                if (s == null || !s.isPlaying || s.clip == null) continue;
                var h = _hostOf[i] >= 0 ? _hosts[_hostOf[i]] : null;
                float level = h != null && h.Dx != null ? Mathf.Max(0f, h.LastDx) : 0f;
                float post = h != null && h.Post != null && h.LastPost >= 0f ? h.LastPost : 1f;
                into.Add(new AcAudioVoice
                {
                    Event = Ev.Name, Clip = s.clip, Volume = s.volume * post, Pitch = s.pitch, TimeSamples = s.timeSamples, Loop = s.loop,
                    Spatial = s.spatialBlend > 0.5f, Position = s.transform.position, Distortion = level,
                    DistortionGain = level > 0f ? post : 1f,
                    Lowpass = h != null && h.Lp != null ? h.LastLp : 0f, Group = _hostOf[i], Source = s.GetInstanceID(),
                });
            }
        }

        public void Destroy()
        {
            for (int i = 0; i < _src.Length; i++)
            {
                if (_src[i] != null) { _src[i].Stop(); _src[i].clip = null; UnityEngine.Object.Destroy(_src[i]); }
                _src[i] = null;
            }
            for (int k = 0; k < _hosts.Length; k++)
            {
                var h = _hosts[k];
                if (h.Own && h.Go != null) UnityEngine.Object.Destroy(h.Go);
                h.Go = null; h.Lp = null; h.Hp = null; h.Dx = null; h.Post = null;
            }
            Playing = false;
        }
    }

    /// <summary>One playing source, as seen by <see cref="AcCarAudio.Tap"/> (lab renders, diagnostics).</summary>
    public struct AcAudioVoice
    {
        public string Event;
        public AudioClip Clip;
        public float Volume, Pitch;
        public int TimeSamples;
        public bool Loop, Spatial;
        public Vector3 Position;
        /// <summary>Level of the FMOD Distortion on this voice's bus (0 = none) and the linear gain that follows it (included in
        /// <see cref="Volume"/>; an offline render divides by it before the waveshaper).</summary>
        public float Distortion, DistortionGain;
        /// <summary>Cutoff of the lowpass on this voice's bus (Hz; 0 = no filter).</summary>
        public float Lowpass;
        /// <summary>Effect group of the voice within its event (voices of one group share the bus effects).</summary>
        public int Group;
        /// <summary>The AudioSource's instance id (an offline render keeps one continuous play position per source).</summary>
        public int Source;
    }
}
