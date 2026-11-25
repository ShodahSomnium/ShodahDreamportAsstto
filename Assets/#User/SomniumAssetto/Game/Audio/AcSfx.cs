using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [A, Audio] UI and race sounds not tied to a car, all from the player's install: tablet clicks (content/sfx/gui_click.wav - AC's own
    /// GUI click - and the launcher theme's wavs launcher/themes/default/audio/*.wav), the start-light beep (common.bank
    /// event:/common/ds_protection, sample "beep" - the only beep in the game's banks; AC's start lights themselves are silent), and the
    /// track ambience (common.bank event:/common/ambience, "ambient1", while a session is on track; setting "audio.ambience").
    /// 2D, played by the audio host. Every call is safe before the install is found (it just stays silent).
    /// </summary>
    public static class AcSfx
    {
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> Loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static AudioSource _ui, _hub;
        static bool _hubOn, _hubMissing;
        static string _hubPath;
        static AcEventPlayer _beep, _ambience;
        static AcSoundBank _common;
        static bool _commonTried, _preloaded, _commonLogged;
        static float _pendingBeepPitch;
        static int _pendingBeeps;
        static bool _trackPending;
        static string _pendingUi;
        static float _pendingUiAt;
        static AcSurfaces _requiredFor;

        /// <summary>Plays a UI sound by name ("click", "select", "back", "negative" - mapped by A to the install's files).</summary>
        public static void Ui(string name)
        {
            var host = AcAudioHost.Ensure();
            if (host == null || string.IsNullOrEmpty(name)) return;
            Preload();
            string path = PathOf(name);
            if (path == null) { AcLog.InfoOnce("Audio: no UI sound file for '" + name + "'"); return; }
            AudioClip c;
            if (!Clips.TryGetValue(path, out c)) { LoadWav(path); _pendingUi = path; _pendingUiAt = Time.unscaledTime; return; } // not loaded yet: plays when it is (within 0.5 s)
            if (c == null) return;
            PlayUi(host, c);
        }

        static void PlayUi(AcAudioHost host, AudioClip c)
        {
            if (_ui == null)
            {
                _ui = host.gameObject.AddComponent<AudioSource>();
                AcAudioMix.Configure(_ui, "ui", false, true);
            }
            _ui.PlayOneShot(c, Mathf.Clamp01(AcAudioMix.Volume(AcAudioGroup.Effects)));
        }

        /// <summary>One red start light came on.</summary>
        public static void StartBeep() { Beep(1f); }

        /// <summary>Lights out / green: go. The same bank beep an octave up. // ours (AC has no separate go sound)</summary>
        public static void GoBeep() { Beep(2f); }

        static void Beep(float pitch)
        {
            if (AcAudioHost.Ensure() == null) return;
            EnsureCommon();
            if (_beep == null || !_beep.Ready) { _pendingBeeps = 1; _pendingBeepPitch = pitch; return; }
            _beep.PitchMul = pitch;
            _beep.Trigger();
        }

        /// <summary>Preloads the UI wavs and the common bank's beep (the host calls it once; also safe to call from the front).</summary>
        public static void Preload()
        {
            if (_preloaded) return;
            if (AcAudioHost.Ensure() == null) return;
            if (AcGame.Current == null || AcGame.Current.Install == null) return;
            _preloaded = true;
            string[] names = { "click", "select", "back", "negative", "item", "bighit", "camera" };
            for (int i = 0; i < names.Length; i++) { string p = PathOf(names[i]); if (p != null) LoadWav(p); }
            EnsureCommon();
        }

        /// <summary>UI name -> install file. click = AC's GUI click; the rest = the launcher theme's wavs. // ours mapping (the names are not in the files)</summary>
        static string PathOf(string name)
        {
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst == null) return null;
            string launcher = Path.Combine(Path.Combine(Path.Combine(Path.Combine(inst.Root, "launcher"), "themes"), "default"), "audio");
            string file;
            switch (name.ToLowerInvariant())
            {
                case "click": case "press": file = Path.Combine(inst.SfxDir, "gui_click.wav"); break;
                case "camera": file = Path.Combine(inst.SfxDir, "camera_click.wav"); break;
                case "select": case "ok": file = Path.Combine(launcher, "select2.wav"); break;
                case "back": case "close": file = Path.Combine(launcher, "select3.wav"); break;
                case "negative": case "error": case "denied": file = Path.Combine(launcher, "negative.wav"); break;
                case "item": case "hover": file = Path.Combine(launcher, "item.wav"); break;
                case "item1": file = Path.Combine(launcher, "item1.wav"); break;
                case "bighit": case "start": case "drive": file = Path.Combine(launcher, "bighit.wav"); break;
                default:
                    file = Path.Combine(launcher, name + ".wav");
                    if (!File.Exists(file)) file = Path.Combine(inst.SfxDir, name + ".wav");
                    break;
            }
            if (File.Exists(file)) return file;
            string click = Path.Combine(inst.SfxDir, "gui_click.wav");
            return File.Exists(click) ? click : null;
        }

        static void LoadWav(string path)
        {
            if (Clips.ContainsKey(path) || !Loading.Add(path)) return;
            AcAudioWorker.Enqueue(new WavJob { Path = path }, true);
        }

        sealed class WavJob : AcAudioWorker.Job
        {
            public string Path;
            AcWav _wav;
            internal override void Run() { _wav = AcWav.Read(Path); }
            internal override void Done()
            {
                if (Error != null || _wav == null || _wav.Frames <= 0 || _wav.Samples.Length <= BigWav) Loading.Remove(Path);
                if (Error != null || _wav == null || _wav.Frames <= 0)
                {
                    Clips[Path] = null;
                    AcLog.WarnOnce("Audio: UI sound " + System.IO.Path.GetFileName(Path) + " could not be read: " + (Error != null ? Error.Message : "empty"));
                    return;
                }
                try
                {
                    var clip = AudioClip.Create("ui:" + System.IO.Path.GetFileNameWithoutExtension(Path), _wav.Frames, _wav.Channels, _wav.Rate, false);
                    if (_wav.Samples.Length > BigWav)
                    {
                        // a long wav (the hub's 60 s room sound): time-sliced like the bank samples, never one big SetData frame
                        AcAudioWorker.Reserve(_wav.Samples.Length);
                        AcAudioWorker.QueueUpload(clip, _wav.Samples, _wav.Channels, new WavSink { Path = Path }, 0);
                        return;
                    }
                    clip.SetData(_wav.Samples, 0);
                    Clips[Path] = clip;
                    if (_pendingUi == Path && Time.unscaledTime - _pendingUiAt < 0.5f && AcAudioHost.Instance != null) PlayUi(AcAudioHost.Instance, clip); // the first click, a moment late rather than silent
                    if (_pendingUi == Path) _pendingUi = null;
                    AcLog.InfoOnce("Audio: UI sounds from the install (" + System.IO.Path.GetFileName(Path) + " " + _wav.Rate + " Hz " + _wav.Channels + " ch first)");
                }
                catch (Exception e) { Clips[Path] = null; AcLog.WarnOnce("Audio: UI clip failed: " + e.Message); }
            }
        }

        /// <summary>UI wavs above this many floats are uploaded time-sliced. // ours</summary>
        const int BigWav = 262144;

        sealed class WavSink : IUploadSink
        {
            public string Path;
            public void Uploaded(int tag, AudioClip clip, bool ok)
            {
                Loading.Remove(Path);
                if (ok && clip != null) Clips[Path] = clip;
                else { Clips[Path] = null; if (clip != null) UnityEngine.Object.Destroy(clip); AcLog.WarnOnce("Audio: " + System.IO.Path.GetFileName(Path) + " upload failed"); }
            }
        }

        static void EnsureCommon()
        {
            if (_commonTried && _common != null) return;
            _common = AcSoundBank.Common();
            _commonTried = _common != null;
            if (_common != null) _common.AddUser();
        }

        /// <summary>Host, once a frame: builds the common players when the bank is read, plays a beep that waited for loading, keeps the
        /// ambience playing while on track.</summary>
        internal static void Tick()
        {
            if (_common == null) { if (AcGame.Current != null && AcGame.Current.Install != null) Preload(); return; }
            if (!_common.MetaReady || _common.Meta == null) return;
            var host = AcAudioHost.Instance;
            if (host != null) TickHub(host);
            if (host == null) return;
            if (_beep == null)
            {
                var m = _common.Meta;
                var ev = m.Event("event:/common/ds_protection");
                if (ev != null && ev.Instruments.Length > 0) { _beep = new AcEventPlayer(ev, _common, host.transform, AcAudioGroup.Effects, false, true); _beep.Require(true); }
                if (!_commonLogged)
                {
                    _commonLogged = true;
                    var amb = m.Event("event:/common/ambience");
                    AcLog.Info("Audio: common.bank: " + m.Fsb.Samples.Count + " samples, " + m.Events.Count + " events; start beep " + (_beep != null ? "event:/common/ds_protection (" + _beep.Ev.Instruments[0].Label + ")" : "MISSING")
                               + ", ambience " + (amb != null && amb.Instruments.Length > 0 ? amb.Instruments[0].Label + (amb.TimelineLoops ? " (loop region)" : "") : "MISSING"));
                }
            }
            if (_pendingBeeps > 0 && _beep != null && _beep.Ready) { _pendingBeeps = 0; _beep.PitchMul = _pendingBeepPitch; _beep.Trigger(); }
            bool wantAmb = AcAudioMix.Ambience && AcSession.OnTrack;
            if (wantAmb && _ambience == null)
            {
                var ev = _common.Meta.Event("event:/common/ambience");
                if (ev != null && ev.Instruments.Length > 0) { _ambience = new AcEventPlayer(ev, _common, host.transform, AcAudioGroup.Effects, false, true); _ambience.Require(false); }
            }
            if (_ambience != null)
            {
                if (wantAmb) { if (!_ambience.Playing) _ambience.Start(); }
                else if (_ambience.Playing) _ambience.Stop();
                _ambience.Drive(null, Time.deltaTime);
            }
            if (_beep != null) _beep.Drive(null, Time.deltaTime);
            if (_trackPending) RequireTrackSounds();
        }


        /// <summary>A track was built (AcSession.TrackBuilt via the host): its surfaces' common.bank events and the collision events are
        /// decoded now (low priority), not on the first excursion onto grass / the first hit.</summary>
        internal static void TrackBuilt() { _trackPending = true; }

        static void RequireTrackSounds()
        {
            if (_common == null || !_common.MetaReady) return;
            var m = _common.Meta;
            if (m == null) { _trackPending = false; return; }
            AcSurfaces surfaces = null;
            try { surfaces = AcTrackWorld.Surfaces; } catch (Exception) { }
            if (surfaces == null) return;                      // still loading: try next frame
            _trackPending = false;
            if (ReferenceEquals(surfaces, _requiredFor)) return;
            _requiredFor = surfaces;
            _common.Require(m.Event("event:/collisions/car/hit"), false);
            _common.Require(m.Event("event:/collisions/track/hit"), false);
            _common.Require(m.Event("event:/collisions/car/scrape"), false);
            _common.Require(m.Event("event:/collisions/track/scrape"), false);
            int n = 0;
            for (int i = 0; i < surfaces.Surfaces.Count; i++)
            {
                var s = surfaces.Surfaces[i];
                var ev = s != null && s.SoundEvent != null ? m.Event(s.SoundEvent) : null;
                if (ev == null || ev.Instruments.Length == 0) continue;
                _common.Require(ev, false);
                n++;
            }
            AcLog.InfoOnce("Audio: track surfaces' sounds requested at track load (" + n + " surfaces with a common.bank event)");
        }

        /// <summary>F -> A: the hub's room sound on (Hub phase) / off (Loading): content/showroom/showroom/track.wav (acShowroom's room
        /// sound), a 2D loop at the effects volume, under the "audio.ambience" switch. Safe before the install is found.</summary>
        public static void HubAmbience(bool on)
        {
            _hubOn = on;
            if (!on) { if (_hub != null && _hub.isPlaying) _hub.Stop(); return; }
            if (AcAudioHost.Ensure() == null) return;
            var inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst == null) return;
            _hubPath = Path.Combine(Path.Combine(Path.Combine(Path.Combine(inst.Root, "content"), "showroom"), "showroom"), "track.wav");
            if (!File.Exists(_hubPath)) { AcLog.InfoOnce("Audio: no content/showroom/showroom/track.wav - the hub stays quiet"); _hubPath = null; _hubMissing = true; return; }
            LoadWav(_hubPath);
        }

        static void TickHub(AcAudioHost host)
        {
            if (_hubOn && _hubPath == null && !_hubMissing) HubAmbience(true);   // asked before the install was known
            bool want = _hubOn && AcAudioMix.Ambience && _hubPath != null;
            AudioClip c = null;
            if (want && !Clips.TryGetValue(_hubPath, out c)) return;   // loading
            if (!want || c == null) { if (_hub != null && _hub.isPlaying) _hub.Stop(); return; }
            if (_hub == null)
            {
                _hub = host.gameObject.AddComponent<AudioSource>();
                AcAudioMix.Configure(_hub, "hub", false, true);
            }
            _hub.volume = Mathf.Clamp01(AcAudioMix.Volume(AcAudioGroup.Effects));
            if (!_hub.isPlaying)
            {
                _hub.clip = c; _hub.loop = true; _hub.Play();
                AcLog.InfoOnce("Audio: hub ambience from content/showroom/showroom/track.wav (" + c.length.ToString("0.0") + " s loop)");
            }
        }
        /// <summary>The host is going away: stop and forget everything (clips are destroyed by the bank cache / here).</summary>
        internal static void Shutdown()
        {
            if (_beep != null) _beep.Destroy();
            if (_ambience != null) _ambience.Destroy();
            _beep = null; _ambience = null;
            if (_common != null) _common.RemoveUser();
            _common = null; _commonTried = false; _preloaded = false; _commonLogged = false; _trackPending = false; _requiredFor = null;
            foreach (var c in Clips.Values) if (c != null) UnityEngine.Object.Destroy(c);
            Clips.Clear(); Loading.Clear();
            _ui = null; _hub = null; _hubOn = false; _hubPath = null;
        }
    }
}
