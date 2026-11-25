using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Which volume slider a sound follows (AcSettings "audio.engine" / "audio.effects", both under the shared "volume").</summary>
    internal enum AcAudioGroup { Engine = 0, Effects = 1 }

    /// <summary>
    /// [A, Audio] Shared audio state: the listener, the volume settings, the 3D settings per event (the SDK FMOD project's spatialiser
    /// distances), the registry of car sounds and the voice budget for other players' / AI cars (the nearest <see cref="MaxRemoteCars"/>
    /// within the engine's audible range play; the rest are stopped).
    /// </summary>
    internal static class AcAudioMix
    {
        /// <summary>Every source plays at this fraction so layered AC samples (+3 dB instruments, several layers) do not clip. // ours</summary>
        public const float Headroom = 0.5f;
        /// <summary>Below this linear gain a loop is treated as silent (about -66 dB). // ours</summary>
        public const float Silent = 0.0005f;
        /// <summary>At most this many other cars sound at once (nearest first). // ours</summary>
        public const int MaxRemoteCars = 10;
        /// <summary>engine_ext spatialiser maximum distance in the SDK FMOD project (AC Audio Pipeline 1.9 p.7: "the minimum is set at 350 m"):
        /// other cars farther than this are silent and their sources stopped.</summary>
        public const float RemoteAudibleMetres = 350f;

        public static float Master, Engine, Effects;
        public static bool Doppler = true, Interior3D = true, Ambience = true;
        /// <summary>Bumped when a setting that changes source configuration (doppler, cockpit 3D) changes: cars re-apply it.</summary>
        public static int ConfigVersion;
        static int _settingsFrame = -1;
        static bool _registered;

        public static readonly List<AcCarAudio> Cars = new List<AcCarAudio>();
        static AcCarAudio[] _sort = new AcCarAudio[16];
        static float[] _sortD = new float[16];

        static AudioListener _listener;
        static float _listenerLookAt;
        static bool _loggedListener;
        public static Vector3 ListenerPos;
        public static Vector3 ListenerFwd = Vector3.forward;
        public static bool HasListener;

        /// <summary>Registers the audio rows of the tablet's settings page (group AUDIO; "volume" is the lead's master row).</summary>
        public static void RegisterSettings()
        {
            if (_registered) return;
            _registered = true;
            try
            {
                AcSettingsUi.Slider("AUDIO", "audio.engine", "ENGINE VOLUME", 0f, 1f, 0.05f, 1f);
                AcSettingsUi.Slider("AUDIO", "audio.effects", "EFFECTS VOLUME (TYRES, WIND, HITS, UI)", 0f, 1f, 0.05f, 1f);
                AcSettingsUi.Toggle("AUDIO", "audio.ambience", "TRACK AMBIENCE", true);
                AcSettingsUi.Toggle("AUDIO", "audio.interior3d", "COCKPIT ENGINE FROM ITS POSITION (3D)", true);
                AcSettingsUi.Toggle("AUDIO", "audio.doppler", "DOPPLER ON PASSING CARS", true);
            }
            catch (Exception e) { AcLog.WarnOnce("Audio: the settings rows could not be registered: " + e.Message); }
        }

        /// <summary>Re-reads the volume settings once a frame.</summary>
        public static void ReadSettings()
        {
            if (_settingsFrame == Time.frameCount) return;
            _settingsFrame = Time.frameCount;
            Master = Mathf.Clamp01(AcSettings.Volume) * Mathf.Clamp01(AcSettings.Num("audio.master", 1f)); // the lead's shared "volume" row x an optional audio-only master
            Engine = Mathf.Clamp01(AcSettings.Num("audio.engine", 1f));
            Effects = Mathf.Clamp01(AcSettings.Num("audio.effects", 1f));
            bool doppler = AcSettings.Flag("audio.doppler", true), i3d = AcSettings.Flag("audio.interior3d", true);
            if (doppler != Doppler || i3d != Interior3D) ConfigVersion++;
            Doppler = doppler;
            Interior3D = i3d;
            Ambience = AcSettings.Flag("audio.ambience", true);
        }

        public static float Volume(AcAudioGroup g) { return Master * (g == AcAudioGroup.Engine ? Engine : Effects) * Headroom; }

        /// <summary>Main thread, once a frame: the listener and the remote-car budget.</summary>
        public static void Tick()
        {
            ReadSettings();
            FindListener();
            Budget();
        }

        static void FindListener()
        {
            bool usable = _listener != null && _listener.enabled && _listener.gameObject.activeInHierarchy;
            if (!usable && Time.unscaledTime >= _listenerLookAt)
            {
                _listenerLookAt = Time.unscaledTime + 1f;
                AudioListener best = null;
                int total = 0;
                try
                {
                    var all = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
                    for (int i = 0; i < all.Length; i++)
                    {
                        total++;
                        if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy) best = all[i];
                    }
                }
                catch (Exception) { }
                _listener = best;
                usable = best != null;
                if (!_loggedListener && usable)
                {
                    _loggedListener = true;
                    var cfg = AudioSettings.GetConfiguration();
                    AcLog.Info("Audio: listener '" + best.name + "' (" + total + " listeners), output " + AudioSettings.outputSampleRate + " Hz, " + cfg.numRealVoices + " real / " + cfg.numVirtualVoices + " virtual voices, dsp buffer " + cfg.dspBufferSize);
                }
            }
            Transform t = null;
            if (usable) t = _listener.transform;
            else
            {
                var head = AcPlayer.Head();
                if (head != null) t = head.transform;
                else if (Camera.main != null) t = Camera.main.transform;
            }
            HasListener = t != null;
            if (t != null) { ListenerPos = t.position; ListenerFwd = t.forward; }
        }

        static void Budget()
        {
            int n = 0;
            if (_sort.Length < Cars.Count) { _sort = new AcCarAudio[Cars.Count * 2]; _sortD = new float[Cars.Count * 2]; }
            for (int i = 0; i < Cars.Count; i++)
            {
                var c = Cars[i];
                if (c == null) continue;
                if (c.Local) { c.Audible = true; continue; }
                float d = HasListener ? (c.transform.position - ListenerPos).sqrMagnitude : 0f;
                // insertion by distance
                int k = n++;
                while (k > 0 && _sortD[k - 1] > d) { _sort[k] = _sort[k - 1]; _sortD[k] = _sortD[k - 1]; k--; }
                _sort[k] = c; _sortD[k] = d;
            }
            float max2 = RemoteAudibleMetres * RemoteAudibleMetres;
            int audible = 0;
            for (int i = 0; i < n; i++)
            {
                bool ok = _sortD[i] < max2 && audible < MaxRemoteCars;
                if (ok) audible++;
                _sort[i].Audible = ok;
                _sort[i] = null;
            }
        }

        // ------------------------------------------------------------------ 3D settings

        struct Spatial { public float Min, Max, Spread; public bool Tapered; }

        /// <summary>
        /// FALLBACK spatialiser distances (the bank's own spatialiser is used whenever the event has one - see Configure(AcSndEvent)): the
        /// SDK FMOD project (sdk/audio/ac_fmod_sdk_1_9/Metadata/Event/*.xml SpatialiserEffect minimumDistance / maximumDistance;
        /// engine_ext distanceRolloffType 3 = inverse tapered), used for events a bank leaves 2D when they belong to another car (a 2D
        /// limiter 300 m away would be heard at full volume). Unlisted values = FMOD Studio's defaults 1 m / 20 m [BELIEF: FMOD
        /// defaults]. Car cameras use these unscaled (pipeline PDF p.7: DISTANCE_SCALE is 1 for car views; every Somnium listener is an
        /// avatar present in the world, so the track-camera scale never applies).
        /// </summary>
        static Spatial SpatialOf(string ev)
        {
            switch (ev)
            {
                case "engine_ext": return new Spatial { Min = 6f, Max = 350f, Tapered = true };
                case "engine_int": return new Spatial { Min = 7f, Max = 50f };
                case "backfire_ext": return new Spatial { Min = 3f, Max = 25f };
                case "backfire_int": return new Spatial { Min = 4f, Max = 20f };
                case "gear_ext": return new Spatial { Min = 2f, Max = 18f };
                case "gear_grind": return new Spatial { Min = 5f, Max = 15f };
                case "limiter": return new Spatial { Min = 5f, Max = 25f };
                case "transmission": return new Spatial { Min = 5f, Max = 15f };
                case "turbo": return new Spatial { Min = 4f, Max = 20f };
                case "wind": return new Spatial { Min = 5f, Max = 25f };
                case "wheel": return new Spatial { Min = 1.5f, Max = 10f };
                case "bodywork": return new Spatial { Min = 3f, Max = 10f };
                case "skid_ext": return new Spatial { Min = 1.5f, Max = 20f };
                case "skid_int": return new Spatial { Min = 1f, Max = 15f };
                case "horn": return new Spatial { Min = 5f, Max = 20f };
                case "door": return new Spatial { Min = 1f, Max = 15f };
                case "tractioncontrol_ext": return new Spatial { Min = 3f, Max = 12f };
                case "tractioncontrol_int": return new Spatial { Min = 3f, Max = 15f };
                case "extraturf": return new Spatial { Min = 1f, Max = 25f };
                case "hit": return new Spatial { Min = 2f, Max = 30f };
                case "scrape": return new Spatial { Min = 2f, Max = 25f };
                default: return new Spatial { Min = 1f, Max = 20f };
            }
        }

        static readonly Dictionary<int, AnimationCurve> Curves = new Dictionary<int, AnimationCurve>();

        /// <summary>Sets a source up for a bank event: 2D, or 3D with THIS bank's spatialiser (pan DSP min / max distance, rolloff 3 =
        /// inverse tapered - decoded per bank: engine_ext 6/350 tapered, engine_int 7/50, door 1/10 or 1/15 ... [VERIFIED 86/86 banks by
        /// the sndfx probe]); an event without a spatialiser (gear_int, some limiters / backfires) falls back to the SDK table below.
        /// The spatialiser's 3D MIN EXTENT (pan DSP parameter 16, degrees: engine_ext 120, gear_ext 150, wind 220, engine_int 0) becomes
        /// AudioSource.spread (both are FMOD's 3D spread): a stereo sample is spread over that angle instead of both channels summed into
        /// one point (+3..6 dB on the 458's stereo exterior layers against its mono idle - lab audio-458, polish pass 2026-11-01).</summary>
        public static void Configure(AudioSource s, AcSndEvent ev, bool spatial, bool local)
        {
            if (ev == null || ev.Spatialiser == null || !spatial) { Configure(s, ev != null ? ev.Name : "", spatial, local); return; }
            Apply(s, new Spatial
            {
                Min = Mathf.Max(0.1f, ev.MinDistance(1f)), Max = Mathf.Max(ev.MinDistance(1f) + 0.5f, ev.MaxDistance(20f)), Tapered = ev.Rolloff == 3,
                Spread = Mathf.Clamp(ev.Spatialiser.P(16, 0f), 0f, 360f),
            }, local);
        }

        /// <summary>Sets a source up for an event by name: 2D (spatial false) or 3D with the SDK table's distances and an FMOD-like rolloff.</summary>
        public static void Configure(AudioSource s, string eventName, bool spatial, bool local)
        {
            if (spatial) { Apply(s, SpatialOf(eventName), local); return; }
            s.playOnAwake = false;
            s.priority = local ? 32 : 128;
            s.spread = 0f;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            s.bypassReverbZones = true;
        }

        static void Apply(AudioSource s, Spatial sp, bool local)
        {
            s.playOnAwake = false;
            s.priority = local ? 32 : 128;
            s.spread = sp.Spread;
            s.spatialBlend = 1f;
            s.bypassReverbZones = false;
            s.dopplerLevel = Doppler && !local ? 1f : 0f;   // SDK events have doppler on (FMOD scale 1); the own car moves with the listener
            s.rolloffMode = AudioRolloffMode.Custom;
            s.minDistance = sp.Min;
            s.maxDistance = sp.Max;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Curve(sp.Min / sp.Max, sp.Tapered));
        }

        /// <summary>Attenuation over distance / max distance: full to the minimum distance, then min/d (FMOD inverse); tapered (engine_ext:
        /// FMOD inverse tapered) fades as (m/x)(1-u^2) to silence at the max; plain inverse is cut over the last 10 % (FMOD culls the event
        /// beyond its max distance). Taper shapes // ours.</summary>
        static AnimationCurve Curve(float ratio, bool tapered)
        {
            int key = Mathf.Clamp(Mathf.RoundToInt(ratio * 1000f), 1, 999) * 2 + (tapered ? 1 : 0);
            AnimationCurve c;
            if (Curves.TryGetValue(key, out c)) return c;
            float m = Mathf.Clamp(ratio, 0.001f, 0.999f);
            var keys = new List<Keyframe> { new Keyframe(0f, 1f), new Keyframe(m, 1f) };
            for (int i = 1; i <= 16; i++)
            {
                float x = m + (1f - m) * i / 16f;
                float u = (x - m) / (1f - m);
                float v = m / x;
                if (tapered) v *= 1f - u * u;
                else if (x > 0.9f) v *= Mathf.Clamp01((1f - x) / 0.1f);
                keys.Add(new Keyframe(x, v));
            }
            c = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            Curves[key] = c;
            return c;
        }
    }

    /// <summary>
    /// [A, Audio] The audio module's one MonoBehaviour outside the cars: created on first use under "Ac Game" (after the gate - every caller
    /// runs from AcGame's hooks), it pumps the worker / clip uploads and the budget each frame, plays the 2D sounds of <see cref="AcSfx"/>,
    /// and frees everything when the Dreamport closes.
    /// </summary>
    public sealed class AcAudioHost : MonoBehaviour
    {
        internal static AcAudioHost Instance { get; private set; }
        float _collectAt;
        int _tickFrame = -1;

        /// <summary>Makes sure the host exists (main thread; no-op when the game is not running).</summary>
        public static AcAudioHost Ensure()
        {
            if (Instance != null) return Instance;
            var game = AcGame.Current;
            if (game == null) return null;
            var go = new GameObject("Ac Audio");
            go.transform.SetParent(game.transform, false);
            Instance = go.AddComponent<AcAudioHost>();
            AcAudioMix.RegisterSettings();
            try { AcSession.Unloading -= OnUnloading; AcSession.Unloading += OnUnloading; AcSession.TrackBuilt -= OnTrackBuilt; AcSession.TrackBuilt += OnTrackBuilt; }
            catch (Exception e) { AcLog.WarnOnce("Audio: AcSession events unavailable: " + e.Message); }
            try { if (AcSession.OnTrack || AcSession.Phase == AcPhase.Loading) AcSfx.TrackBuilt(); } catch (Exception) { }
            AcAudioMix.ReadSettings();
            AcLog.Info("Audio: started (car banks from the install, worker thread, " + AcAudioWorker.UploadFloatsPerFrame + " floats/frame upload)");
            return Instance;
        }

        /// <summary>Runs the per-frame work once even if called from several places this frame.</summary>
        internal void TickOnce()
        {
            if (_tickFrame == Time.frameCount) return;
            _tickFrame = Time.frameCount;
            try { AcAudioWorker.Pump(); } catch (Exception e) { AcLog.Error("Audio: pump failed: " + e); }
            try { AcAudioMix.Tick(); } catch (Exception e) { AcLog.Error("Audio: tick failed: " + e); }
            try { AcSfx.Tick(); } catch (Exception e) { AcLog.Error("Audio: sfx tick failed: " + e); }
            if (Time.unscaledTime >= _collectAt)
            {
                _collectAt = Time.unscaledTime + 5f;
                try { AcSoundBank.Collect(); } catch (Exception e) { AcLog.Error("Audio: collect failed: " + e); }
            }
        }

        void Update() { TickOnce(); }

        /// <summary>The track is going away: forget its surfaces (the cars themselves are destroyed with their rigs).</summary>
        static void OnUnloading() { AcCarAudio.ClearSurfaceCache(); }

        /// <summary>A track is built: decode its surfaces' common.bank sounds now, so the first excursion onto grass is not silent.</summary>
        static void OnTrackBuilt() { AcSfx.TrackBuilt(); }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            try { AcSession.Unloading -= OnUnloading; AcSession.TrackBuilt -= OnTrackBuilt; } catch (Exception) { }
            AcCarAudio.ClearSurfaceCache();
            try { AcSfx.Shutdown(); } catch (Exception) { }
            try { AcAudioWorker.Stop(); } catch (Exception) { }
            try { AcSoundBank.ReleaseAll(); } catch (Exception) { }
            AcAudioMix.Cars.Clear();
        }
    }
}
