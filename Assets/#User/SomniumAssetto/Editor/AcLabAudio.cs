using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// Module A lab scenarios (DevTools/lab.sh &lt;scenario&gt;):
    ///   audio-458    ferrari_458 local car: bank -> clips (time-sliced, frame times reported), interior sweep idle -> limiter on throttle,
    ///                lift, back to idle; exterior sweep from 8 m; one-shots (gear up/down, door, horn, collision hit then scrape); a remote 458
    ///                sharing the clips (cache hits) at 60 m and culled at 400 m; UI click + start beeps. Renders what the runtime asked its
    ///                sources to play (clip, volume, pitch, position, the engine_int distortion level every frame) into audio-458-int.wav /
    ///                -ext.wav and reports levels per second in dBFS and dB(A) - the loudness check is A-weighted (the 458's 900 rpm idle is
    ///                bass: louder in plain RMS, 20 dB(A) quieter than full throttle - harness `render ferrari_458 int`).
    ///   audio-abarth abarth500: backfire on lift-off (sounds.ini [BACKFIRE]), limiter, turbo boost, gear grind.
    ///   audio-drive  the real product path: AcRace.RequestLoad (magione) -> R spawns the local car through AcCarRig.Spawn with P's physics;
    ///                emitters on the real rig; S's AcSeat.Enter + AcCarCamera cockpit / chase -> SetInterior; P drives (throttle, brake,
    ///                steer): skids / surfaces / wind / gear from P's telemetry; CarImpact hit then scrape; a Kind Remote rig fed like N
    ///                (ApplyPuppet + Feed), a stand-in (no bank), a passenger SetInterior on the remote car (full cockpit set, no doppler);
    ///                12 more remote cars (budget: nearest 10 audible, the rest never built); the audio.doppler / audio.interior3d toggles
    ///                taking effect at once; a remote car that stops being fed fades out; RequestHub -> AcSession.Unloading tears it down.
    /// The "by ear" judgement is the user's (play the wavs, or listen in the client).
    /// </summary>
    [InitializeOnLoad]
    static class AcLabAudio
    {
        static AcLabAudio()
        {
            AcLabRunner.Register("audio-458", r => Run458(r));
            AcLabRunner.Register("audio-abarth", r => RunAbarth(r));
            AcLabRunner.Register("audio-drive", r => RunDrive(r));
            AcLabRunner.Register("audio-damage", r => RunDamage(r));
        }

        sealed class Recorder
        {
            public readonly List<float> Times = new List<float>();
            public readonly List<AcAudioVoice[]> Frames = new List<AcAudioVoice[]>();
            public double OutSum; public long OutN;
            readonly List<AcAudioVoice> _tmp = new List<AcAudioVoice>();
            readonly float[] _out = new float[1024];
            float _t0 = -1f;

            /// <summary>What Unity's mixer really played (AudioListener output, filters and 3D included).</summary>
            public readonly OutCapture Out = new OutCapture();

            public void Capture(AcCarAudio a)
            {
                if (_t0 < 0f) _t0 = Time.time;
                _tmp.Clear();
                a.Tap(_tmp);
                Times.Add(Time.time - _t0);
                Frames.Add(_tmp.ToArray());
                AudioListener.GetOutputData(_out, 0);
                for (int i = 0; i < _out.Length; i++) { OutSum += _out[i] * _out[i]; OutN++; }
                Out.Step();
            }

            public float OutputRms { get { return OutN > 0 ? (float)Math.Sqrt(OutSum / OutN) : 0f; } }
        }

        /// <summary>
        /// Records Unity's real mixer output as one continuous stream (polish pass 2026-11-01): each time the DSP clock moves, the newest
        /// samples of AudioListener.GetOutputData are appended - found by matching the stream's last 16 samples inside the new block
        /// (exact floats), else by the DSP clock difference. This is the sound the player hears (AudioSource volume, rolloff, Unity's
        /// low-pass and distortion filters in their real order), unlike the voice-state render.
        /// </summary>
        sealed class OutCapture
        {
            const int N = 8192;
            public readonly int Rate = AudioSettings.outputSampleRate;
            public readonly List<float> St = new List<float>(1 << 21);
            public int Aligned, ByClock, Gaps;
            readonly float[] _l = new float[N], _r = new float[N];
            long _lastDsp = -1;
            bool _mono;

            public void Step()
            {
                long dsp = (long)Math.Round(AudioSettings.dspTime * Rate);
                if (_lastDsp < 0) { _lastDsp = dsp; return; }
                if (dsp == _lastDsp) return;
                long d = dsp - _lastDsp;
                _lastDsp = dsp;
                AudioListener.GetOutputData(_l, 0);
                if (!_mono) { try { AudioListener.GetOutputData(_r, 1); } catch (Exception) { _mono = true; } }
                if (_mono) Array.Copy(_l, _r, N);
                int start = -1, tail = St.Count / 2;
                if (tail >= 16)
                {
                    bool zero = true;
                    for (int k = 0; k < 16 && zero; k++) if (Math.Abs(St[2 * (tail - 16 + k)]) > 1e-7f) zero = false;
                    if (!zero)
                        for (int p = N; p >= 16 && start < 0; p--)
                        {
                            bool ok = true;
                            for (int k = 0; k < 16 && ok; k++) ok = _l[p - 16 + k] == St[2 * (tail - 16 + k)];
                            if (ok) start = p;
                        }
                }
                if (start >= 0) Aligned++;
                else { ByClock++; if (d > N) Gaps++; start = N - (int)Math.Min(d, N); }
                for (int i = start; i < N; i++) { St.Add(_l[i]); St.Add(_r[i]); }
            }

            public float[] ToArray() { return St.ToArray(); }
            public float Seconds { get { return St.Count / 2f / Rate; } }
        }

        /// <summary>
        /// A 0.5-amplitude 1 kHz sine on a 2D AudioSource with an AudioDistortionFilter (level 0.7) behind it, measured in the real mixer
        /// output: (a) volume 1, (b) volume 0.05, (c) a second source on the same GameObject alone, (d) filter level 0 (clean reference).
        /// If the filter runs BEFORE the source volume, (a) and (b) have the same distortion (THD) and differ by exactly 26 dB; if AFTER,
        /// (b) is nearly clean. (c) shows whether the filter also processes the other AudioSources of its GameObject (AcEventPlayer puts
        /// all of an effect set's sources on one GameObject). (a)/(d) compared with AcSndFx.Distort says whether the formula is FMOD's.
        /// </summary>
        static IEnumerator DistortionOrder(AcLabRunner r, AudioListener l)
        {
            int rate = AudioSettings.outputSampleRate;
            var clip = AudioClip.Create("Lab Sine 1k", rate, 1, rate, false);
            var data = new float[rate];
            for (int i = 0; i < rate; i++) data[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 1000f * i / rate);   // 1000 whole cycles: a seamless loop
            clip.SetData(data, 0);
            var go = new GameObject("Lab Distortion Probe");
            go.transform.position = l.transform.position;
            var sa = go.AddComponent<AudioSource>(); var sb = go.AddComponent<AudioSource>();
            foreach (var s in new[] { sa, sb }) { s.playOnAwake = false; s.clip = clip; s.loop = true; s.spatialBlend = 0f; s.volume = 1f; s.dopplerLevel = 0f; s.bypassReverbZones = true; }
            var dx = go.AddComponent<AudioDistortionFilter>();
            dx.distortionLevel = 0.7f;
            var ec = go.AddComponent<AudioEchoFilter>();   // AcEventPlayer's post-distortion gain: wet 0, dry = the downstream faders
            ec.delay = 10f; ec.decayRatio = 0f; ec.wetMix = 0f; ec.dryMix = 1f;
            string[] names = { "a vol 1", "b vol 0.05", "c 2nd source vol 1", "d level 0 vol 1", "e level 0 echo dry 0.25", "f level 0.7 echo dry 0.25" };
            var rms = new float[6]; var thd = new float[6];
            for (int ph = 0; ph < 6; ph++)
            {
                sa.Stop(); sb.Stop();
                dx.distortionLevel = ph == 3 || ph == 4 ? 0f : 0.7f;
                ec.dryMix = ph >= 4 ? 0.25f : 1f;
                var src = ph == 2 ? sb : sa;
                src.volume = ph == 1 ? 0.05f : 1f;
                src.Play();
                var cap = new OutCapture();
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 0.9f) { cap.Step(); yield return null; }
                float[] u = cap.ToArray();
                float secs = cap.Seconds;
                float w0 = Mathf.Max(0f, secs - 0.4f), w1 = secs;   // the last 0.4 s (the filter settled, the previous phase gone)
                float pk; AcOfflineMix.Levels(u, w0, w1, rate, out rms[ph], out pk);
                float fund = Goertzel(u, w0, w1, rate, 1000f);
                thd[ph] = rms[ph] > 1e-6f && fund > 0f ? Mathf.Sqrt(Mathf.Max(0f, rms[ph] * rms[ph] - fund * fund)) / fund : -1f;
                AcLabRunner.Log("  distortion probe " + names[ph] + ": rms " + AcOfflineMix.ToDb(rms[ph]).ToString("0.00") + " dBFS, THD+N " + (thd[ph] * 100f).ToString("0.0") + " % (" + secs.ToString("0.00") + " s captured)");
            }
            sa.Stop(); sb.Stop();
            UnityEngine.Object.Destroy(go);
            UnityEngine.Object.Destroy(clip);
            // FMOD's waveshaper on the 0.5 sine at 0.7, offline (the clean reference = phase d)
            double s2 = 0, c2 = 0;
            for (int i = 0; i < rate; i++) { float x = data[i]; float y = AcSndFx.Distort(x, 0.7f); s2 += y * y; c2 += x * x; }
            float predDb = (float)(10.0 * Math.Log10(s2 / c2));
            float aVsD = AcOfflineMix.ToDb(rms[0]) - AcOfflineMix.ToDb(rms[3]), aVsB = AcOfflineMix.ToDb(rms[0]) - AcOfflineMix.ToDb(rms[1]);
            bool pre = thd[1] > 0.5f * thd[0] && Mathf.Abs(aVsB - 26.02f) < 1f;
            AcLabRunner.Log("  distortion probe: a-d " + aVsD.ToString("0.00") + " dB (FMOD formula predicts " + predDb.ToString("0.00") + " dB), a-b " + aVsB.ToString("0.00") + " dB (26.02 if the filter runs before the volume) -> AudioDistortionFilter runs "
                + (pre ? "BEFORE AudioSource.volume (sees the clip at full scale)" : "AFTER AudioSource.volume (sees the scaled signal)") + "; 2nd source on the GameObject " + (thd[2] > 0.5f * thd[0] ? "IS" : "is NOT") + " distorted");
            // judged on levels: THD+N of the stitched capture is unreliable under load (clean phase d read 0.6 % in one run, 14 % in the next)
            r.Check(aVsD > 3f && Mathf.Abs(AcOfflineMix.ToDb(rms[3]) + 12.04f) < 0.5f, "distortion probe measured in the real mixer output (the filter adds " + aVsD.ToString("0.0") + " dB to the 0.5 sine, clean -12.04 dBFS = " + AcOfflineMix.ToDb(rms[3]).ToString("0.00") + "; THD+N " + (thd[0] * 100f).ToString("0") + " % / " + (thd[3] * 100f).ToString("0.0") + " %, informative only)");
            // the echo filter with wet 0 is a pure linear gain AFTER the distortion: dry 0.25 = -12.04 dB on the clean and on the distorted sine
            // (were it before the distortion, the 0.125 sine would be shaped less and come out far louder than a - 12 dB). Judged on rms only:
            // THD+N of the stitched capture varies +-6 % between identical phases (a vs c) - run 1 (polish pass 2026-11-01)
            float dVsE = AcOfflineMix.ToDb(rms[3]) - AcOfflineMix.ToDb(rms[4]), aVsF = AcOfflineMix.ToDb(rms[0]) - AcOfflineMix.ToDb(rms[5]);
            r.Check(Mathf.Abs(dVsE - 12.04f) < 0.5f && Mathf.Abs(aVsF - 12.04f) < 0.5f,
                "echo filter (wet 0, dry 0.25) = a linear gain after the distortion (clean -" + dVsE.ToString("0.00") + " dB, distorted -" + aVsF.ToString("0.00") + " dB, 12.04 expected; THD+N " + (thd[5] * 100f).ToString("0.0") + " % vs " + (thd[0] * 100f).ToString("0.0") + " %)");
        }

        /// <summary>RMS of the component at <paramref name="hz"/> (mono sum) between two times.</summary>
        static float Goertzel(float[] st, float t0, float t1, int rate, float hz)
        {
            int a = Math.Max(0, (int)(t0 * rate)), b = Math.Min(st.Length / 2, (int)(t1 * rate)), n = b - a;
            if (n <= 0) return 0f;
            double w = 2 * Math.PI * hz / rate, cw = 2 * Math.Cos(w), s1 = 0, s2 = 0;
            for (int i = a; i < b; i++) { double x = (st[2 * i] + st[2 * i + 1]) * 0.5 + cw * s1 - s2; s2 = s1; s1 = x; }
            double power = s1 * s1 + s2 * s2 - cw * s1 * s2;
            return (float)(Math.Sqrt(Math.Max(0, power)) * 2.0 / n / Math.Sqrt(2.0));
        }

        /// <summary>Sample-to-sample steps larger than 0.05 (clicks: a voice restarted or re-seeked) between two times.</summary>
        static int Jumps(float[] st, float t0, float t1, int rate)
        {
            int a = Math.Max(1, (int)(t0 * rate)), b = Math.Min(st.Length / 2, (int)(t1 * rate)), n = 0;
            for (int i = a; i < b; i++) if (Math.Abs(st[2 * i] - st[2 * i - 2]) > 0.05f) n++;
            return n;
        }

        // ------------------------------------------------------------------ audio-458

        static IEnumerator Run458(AcLabRunner r)
        {
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            var car = r.Game.Content.Car("ferrari_458");
            if (!r.Check(car != null && car.Owned && car.BankPath != null, "ferrari_458 installed with a bank")) yield break;
            var listener = EnsureListener(r);

            var root = new GameObject("Lab Car 458");
            root.transform.position = new Vector3(0f, 3000f, 0f);
            float maxFrame = 0f, t0 = Time.realtimeSinceStartup;
            var audio = AcCarAudio.Attach(root, car, null, true);
            r.Check(audio != null, "AcCarAudio.Attach returned a component");
            while (!audio.Ready && Time.realtimeSinceStartup - t0 < 180f)
            {
                maxFrame = Mathf.Max(maxFrame, Time.unscaledDeltaTime);
                Feed(audio, 0f, 0f, 0, false);
                yield return null;
            }
            r.Check(audio.Ready, "engine clips ready in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s (longest frame while loading " + (maxFrame * 1000f).ToString("0") + " ms)");
            if (!audio.Ready) yield break;
            int sources0 = audio.Sources;
            for (int i = 0; i < 600; i++) { Feed(audio, 0f, 0f, 0, false); maxFrame = Mathf.Max(maxFrame, Time.unscaledDeltaTime); yield return null; }
            AcLabRunner.Log("longest frame during the whole load " + (maxFrame * 1000f).ToString("0") + " ms; " + audio.Describe());

            // ---- where Unity's AudioDistortionFilter sits relative to AudioSource.volume (a 1 kHz sine through it, real mixer output)
            yield return DistortionOrder(r, listener);

            // ---- interior sweep
            audio.SetInterior(true);
            listener.transform.position = root.transform.TransformPoint(new Vector3(-0.34f, 0.54f, 0.17f)); // 458 DRIVEREYES in the body frame
            listener.transform.rotation = root.transform.rotation;
            yield return Sweep(r, audio, listener, 900f, 9000f, "audio-458-int", true);
            r.Check(audio.Interior, "interior sounds while the listener sits at the driver eye");
            r.Check(audio.Sources == sources0, "no AudioSource made while driving (" + audio.Sources + " = " + sources0 + " made at build)");

            // ---- exterior sweep from the side, 8 m
            audio.SetInterior(false);
            listener.transform.position = root.transform.TransformPoint(new Vector3(8f, 1.2f, -2f));
            listener.transform.LookAt(root.transform.position);
            yield return Sweep(r, audio, listener, 900f, 9000f, "audio-458-ext", false);
            r.Check(!audio.Interior, "exterior sounds from 8 m");

            // ---- cone low-pass: engine_ext from ahead vs from behind (bank lowpass automated on the event cone angle)
            float front = 0f, back = 0f;
            listener.transform.position = root.transform.TransformPoint(new Vector3(0f, 1f, 10f));
            for (int i = 0; i < 30; i++) { Feed(audio, 6000f, 1f, 3, false); yield return null; front = Mathf.Max(front, LowpassOf(audio, "engine_ext")); }
            listener.transform.position = root.transform.TransformPoint(new Vector3(0f, 1f, -10f));
            for (int i = 0; i < 30; i++) { Feed(audio, 6000f, 1f, 3, false); yield return null; back = LowpassOf(audio, "engine_ext"); }
            r.Check(front > 15000f && back > 0f && back < 9000f, "engine_ext low-pass by cone angle: " + front.ToString("0") + " Hz ahead, " + back.ToString("0") + " Hz behind");

            // ---- one-shots
            var seen = new HashSet<string>();
            var counts = new Dictionary<string, int>();
            var tmp = new List<AcAudioVoice>();
            var prev = new HashSet<string>();
            float lastHit = -9f, firstHit = -9f;
            int repeats = 0;
            float loopStart = Time.time;
            // frame count for the one-shots, then at least 1.2 s after the contact (a fast batch frame rate would end before P's first 0.1 s repeat)
            for (int i = 0; i < 300 || (firstHit > -9f && Time.time - firstHit < 1.2f && i < 100000); i++)
            {
                int gear = i < 60 ? 1 : i < 120 ? 2 : 1;
                Feed(audio, 3000f, 0.3f, gear, false);
                if (i == 10) audio.Door(AcSide.Left, true);
                if (i == 70) audio.Door(AcSide.Left, false);
                if (i == 130) audio.Horn(true);
                if (i == 190) audio.Horn(false);
                // a wall contact: the hit, then P's repeats every 0.1 s while it stays (sliding) -> scrape, never a second crash
                if (i == 200) { audio.Collision(55f, root.transform.position + root.transform.forward * 2f); lastHit = Time.time; firstHit = Time.time; }
                else if (i > 200 && Time.time - firstHit < 1f && Time.time - lastHit >= 0.1f) { audio.Collision(30f, root.transform.position + root.transform.forward * 2f); lastHit = Time.time; repeats++; }   // P: every 0.1 s while it stays
                tmp.Clear(); audio.Tap(tmp);
                var now = new HashSet<string>();
                for (int k = 0; k < tmp.Count; k++) { seen.Add(tmp[k].Event); now.Add(tmp[k].Event); }
                foreach (var e in now) if (!prev.Contains(e)) { int c; counts.TryGetValue(e, out c); counts[e] = c + 1; }
                prev = now;
                yield return null;
            }
            AcLabRunner.Log("events heard in the one-shot phase: " + string.Join(", ", new List<string>(seen).ToArray()));
            r.Check(seen.Contains("gear_ext"), "gear change played gear_ext");
            r.Check(seen.Contains("door"), "door played");
            r.Check(seen.Contains("horn"), "horn played");
            int hits; counts.TryGetValue("hit", out hits);
            r.Check(hits == 1, "the contact played collisions/track/hit once (" + hits + " starts)");
            AcLabRunner.Log("one-shot phase: " + (Time.time - loopStart).ToString("0.00") + " s of game time, " + repeats + " contact repeats after the hit");
            if (!seen.Contains("scrape")) AcLabRunner.Log("diag scrape: " + audio.DescribeCollisions());
            r.Check(seen.Contains("scrape"), "the repeats played collisions/track/scrape");

            // ---- a remote 458 (shares the clips), audible at 60 m, culled at 400 m
            var remoteGo = new GameObject("Lab Remote 458");
            remoteGo.transform.position = listener.transform.position + new Vector3(60f, 0f, 0f);
            var remote = AcCarAudio.Attach(remoteGo, car, null, false);
            float rt0 = Time.realtimeSinceStartup;
            while (!remote.Ready && Time.realtimeSinceStartup - rt0 < 30f) { Feed(remote, 4000f, 0.8f, 3, false); Feed(audio, 0f, 0f, 0, false); yield return null; }
            r.Check(remote.Ready, "remote 458 ready from the cache in " + (Time.realtimeSinceStartup - rt0).ToString("0.00") + " s");
            for (int i = 0; i < 60; i++) { Feed(remote, 4000f, 0.8f, 3, false); Feed(audio, 0f, 0f, 0, false); yield return null; }
            AcLabRunner.Log("remote at 60 m: " + remote.Describe());
            r.Check(remote.Voices > 0, "remote engine plays at 60 m");
            remoteGo.transform.position = listener.transform.position + new Vector3(400f, 0f, 0f);
            for (int i = 0; i < 30; i++) { Feed(remote, 4000f, 0.8f, 3, false); Feed(audio, 0f, 0f, 0, false); yield return null; }
            AcLabRunner.Log("remote at 400 m: " + remote.Describe());
            r.Check(remote.Voices == 0, "remote engine silent beyond 350 m");
            UnityEngine.Object.Destroy(remoteGo);

            // ---- UI + beeps
            AcSfx.Ui("click");
            yield return r.Frames(2);
            var host = AcAudioHost.Ensure();
            AcLabRunner.Log("first click: " + PlayingOn(host) + " sources playing 2 frames later (a click before its wav is read plays when it is, within 0.5 s)");
            for (int i = 0; i < 90; i++) yield return null;
            AcSfx.Ui("click"); AcSfx.StartBeep();
            yield return r.Frames(30);
            AcSfx.GoBeep();
            yield return r.Frames(10);
            r.Check(PlayingOn(host) > 0, "UI click / beep sources play on the audio host (" + PlayingOn(host) + ")");

            audio.Dispose();
            r.Check(audio.Voices == 0 && !audio.Ready && audio.Sources == 0, "Dispose stops and frees everything");
            UnityEngine.Object.Destroy(root);
        }

        static int PlayingOn(AcAudioHost host)
        {
            int playing = 0;
            if (host != null) foreach (var s in host.GetComponentsInChildren<AudioSource>()) if (s.isPlaying) playing++;
            return playing;
        }

        static float LowpassOf(AcCarAudio a, string ev)
        {
            var tmp = new List<AcAudioVoice>();
            a.Tap(tmp);
            float c = 0f;
            for (int i = 0; i < tmp.Count; i++) if (tmp[i].Event == ev && tmp[i].Lowpass > 0f) c = Mathf.Max(c, tmp[i].Lowpass);
            return c;
        }

        // ------------------------------------------------------------------ audio-abarth

        static IEnumerator RunAbarth(AcLabRunner r)
        {
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            var car = r.Game.Content.Car("abarth500");
            if (!r.Check(car != null && car.Owned, "abarth500 installed")) yield break;
            var listener = EnsureListener(r);
            var root = new GameObject("Lab Car Abarth");
            root.transform.position = new Vector3(0f, 3000f, 0f);
            var audio = AcCarAudio.Attach(root, car, null, true);
            float t0 = Time.realtimeSinceStartup;
            while (!audio.Ready && Time.realtimeSinceStartup - t0 < 180f) { Feed(audio, 0f, 0f, 0, false); yield return null; }
            if (!r.Check(audio.Ready, "abarth500 engine ready")) yield break;
            for (int i = 0; i < 600; i++) { Feed(audio, 0f, 0f, 0, false); yield return null; }
            listener.transform.position = root.transform.TransformPoint(new Vector3(3f, 1f, -4f));
            listener.transform.LookAt(root.transform.position);
            var seen = new HashSet<string>();
            var tmp = new List<AcAudioVoice>();
            for (int i = 0; i < 600; i++)
            {
                float rpm = i < 200 ? 2000f + i * 25f : i < 300 ? 6900f : 6000f - (i - 300) * 10f;
                float thr = i < 300 ? 1f : 0f;                     // lift at 300: [BACKFIRE] MAXGAS 0.275 after TRIGGERGAS 0.8, rpm >= 4700
                var t = new AcTelemetry { Rpm = rpm, Throttle = thr, Gear = 3, SpeedKmh = 80f, BoostBar = thr * 0.9f, Limiter = i >= 200 && i < 300 && (i % 6) < 2, LimiterRpm = 6900f };
                audio.Feed(t);
                if (i == 450) audio.Gear(3, true);
                tmp.Clear(); audio.Tap(tmp);
                for (int k = 0; k < tmp.Count; k++) seen.Add(tmp[k].Event);
                yield return null;
            }
            AcLabRunner.Log("abarth events heard: " + string.Join(", ", new List<string>(seen).ToArray()));
            r.Check(seen.Contains("backfire_ext"), "backfire on lift-off");
            r.Check(seen.Contains("limiter"), "limiter while cutting");
            r.Check(seen.Contains("turbo"), "turbo with boost");
            r.Check(seen.Contains("gear_grind"), "gear grind");
            audio.Dispose();
            UnityEngine.Object.Destroy(root);
        }

        // ------------------------------------------------------------------ audio-drive

        static IEnumerator RunDrive(AcLabRunner r)
        {
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            AcContent content = r.Game.Content;
            string trackId = AcLabBoot.Arg("-actrack", "magione");
            AcLayoutEntry layout = content.Layout(trackId, AcLabBoot.Arg("-aclayout", ""));
            if (layout == null) { AcTrackEntry tr = content.Track(trackId); if (tr != null && tr.Layouts.Count > 0) layout = tr.Layouts[0]; }
            if (!r.Check(layout != null && layout.Owned, "layout " + trackId + " owned")) yield break;
            AcCarEntry car = content.Car(AcLabBoot.Arg("-accar", "ferrari_458"));
            if (!r.Check(car != null && car.Owned && car.BankPath != null, "car " + (car != null ? car.Id : "?") + " owned with a bank")) yield break;
            var listener = EnsureListener(r);

            // ---- the real flow: R loads the track and spawns my car through P's factory (Audio = true)
            AcRace.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : "");
            AcRules rules = AcRace.DefaultRules();
            rules.AiCount = 0;
            r.Check(AcRace.RequestLoad(layout, rules), "AcRace.RequestLoad " + layout.Key);
            yield return r.Wait(() => AcSession.Phase == AcPhase.Practice && AcRace.LocalCar != null && AcRace.LocalCar.Root != null, 900f, "practice + my car");
            if (r.Failed) yield break;
            AcCarRig rig = AcRace.LocalCar;
            AcCarAudio audio = rig.Audio;
            if (!r.Check(audio != null && audio.Local, "R's local car carries a local AcCarAudio (AcCarRig.Spawn Audio = true)")) yield break;
            float t0 = Time.realtimeSinceStartup;
            yield return r.Wait(() => audio.Ready, 180f, "the car's engine clips");
            if (r.Failed) yield break;
            AcLabRunner.Log("ready in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s: " + audio.Describe());
            int sources0 = audio.Sources;
            Transform eng = rig.Root.transform.Find("Ac Snd Engine"), w0 = rig.Root.transform.Find("Ac Snd Wheel 0"), w3 = rig.Root.transform.Find("Ac Snd Wheel 3");
            r.Check(eng != null && w0 != null && w3 != null, "emitters on the real rig");
            if (eng != null && w0 != null && w3 != null)
                r.Check(w0.localPosition.x < 0f && w0.localPosition.z > 0f && w3.localPosition.x > 0f && w3.localPosition.z < 0f,
                        "wheel emitters at the wheels (LF " + w0.localPosition.ToString("F2") + ", RR " + w3.localPosition.ToString("F2") + "), engine " + eng.localPosition.ToString("F2"));

            // ---- S: sit in, cockpit -> interior, chase -> exterior, get out
            bool seatedOk = false;
            try { seatedOk = AcSeat.Enter(rig, AcSeatKind.Driver); } catch (Exception e) { AcLabRunner.Log("AcSeat.Enter threw " + e.Message); }
            yield return r.Frames(10);
            if (seatedOk && AcSeat.Seated)
            {
                AcCarCamera.Set(AcCameraMode.Cockpit);
                PlaceAtEye(listener, rig);
                yield return r.Frames(10);
                r.Check(audio.Interior, "seated + cockpit camera (S's SetInterior) -> interior sounds");
                AcCarCamera.Set(AcCameraMode.ChaseNear);
                yield return r.Frames(10);
                r.Check(!audio.Interior, "chase camera -> exterior sounds");
                AcSeat.Exit();
                yield return r.Frames(30);
            }
            else AcLabRunner.Log("note: S could not seat the lab player (no avatar in batch?) - seat path not exercised, SetInterior used directly below");

            // ---- P drives (not seated: P takes Input from the lab, as in physics-458-magione)
            AcCarPhysics ph = rig.Physics;
            if (!r.Check(ph != null, "P's physics on the rig")) yield break;
            ph.SetOccupied(true); ph.SetDriven(true);
            audio.SetInterior(true);
            var seen = new HashSet<string>();
            var tmp = new List<AcAudioVoice>();
            float maxSkid = 0f, maxSpeed = 0f; int gears = 0, lastGear = ph.Telemetry.Gear;
            string surfacesSeen = "";
            var soundSurfaces = new HashSet<string>();
            float drive0 = Time.time;
            while (Time.time - drive0 < 14f)
            {
                float t = Time.time - drive0;
                PlaceAtEye(listener, rig);
                ph.Input = AcCarInput.Neutral;
                if (t < 7f) { ph.Input.Throttle = 1f; ph.Input.Steer = t > 5f ? 0.15f : 0f; }
                else if (t < 9f) { ph.Input.Brake = 1f; ph.Input.Steer = 0.8f; ph.Input.Handbrake = 1f; }   // lock-up + slide
                else { ph.Input.Throttle = 0.6f; ph.Input.Steer = 1f; }                                   // off the road onto whatever is beside it
                if (ph.Telemetry.Gear != lastGear) { gears++; lastGear = ph.Telemetry.Gear; }
                if (t > 1f && t < 6.5f && ph.Telemetry.Gear <= 1 && ph.Telemetry.Rpm > ph.Telemetry.LimiterRpm * 0.85f) ph.Input.ShiftUp = true;
                maxSpeed = Mathf.Max(maxSpeed, ph.Telemetry.SpeedKmh);
                for (int i = 0; i < 4; i++)
                {
                    var w = ph.Telemetry.Wheel(i);
                    maxSkid = Mathf.Max(maxSkid, w.Skid);
                    if (w.Contact && w.Surface != null && surfacesSeen.IndexOf(w.Surface.Key, StringComparison.Ordinal) < 0) surfacesSeen += w.Surface.Key + " ";
                    if (w.Contact && w.Surface != null && !string.IsNullOrEmpty(w.Surface.SoundEvent)) soundSurfaces.Add(w.Surface.SoundEvent.Substring(w.Surface.SoundEvent.LastIndexOf('/') + 1));
                }
                tmp.Clear(); audio.Tap(tmp);
                for (int k = 0; k < tmp.Count; k++) seen.Add(tmp[k].Event);
                yield return null;
            }
            ph.Input = AcCarInput.Neutral; ph.Input.Brake = 1f;
            AcLabRunner.Log("drive: max " + maxSpeed.ToString("0") + " km/h, max skid " + maxSkid.ToString("0.00") + ", gear changes " + gears + ", surfaces " + surfacesSeen + "; heard " + string.Join(", ", new List<string>(seen).ToArray()));
            r.Check(seen.Contains("engine_int"), "engine_int from P's telemetry");
            r.Check(maxSpeed < 40f || seen.Contains("wind"), "wind at speed (max " + maxSpeed.ToString("0") + " km/h)");
            r.Check(maxSkid < 0.15f || seen.Contains("skid_int"), "skid_int follows P's Skid (max " + maxSkid.ToString("0.00") + ")");
            r.Check(gears == 0 || seen.Contains("gear_int"), "gear_int on P's gear changes (" + gears + ")");
            foreach (var s in soundSurfaces) r.Check(seen.Contains(s), "surface sound '" + s + "' while a wheel touched it");
            if (soundSurfaces.Count == 0) AcLabRunner.Log("note: no wheel touched a surface with a WAV - surface sounds not exercised on " + layout.Key);
            r.Check(audio.Sources == sources0, "no AudioSource made while driving (" + audio.Sources + " = " + sources0 + ")");

            // ---- P's collision event: the first raise is a hit, repeats every 0.1 s are a scrape
            var counts = new Dictionary<string, int>();
            var prev = new HashSet<string>();
            float lastHit = -9f, firstHit = -9f;
            Vector3 wall = rig.Root.transform.position + rig.Root.transform.right * 1f;
            float c0 = Time.time;
            while (Time.time - c0 < 1.6f)
            {
                if (firstHit < 0f) { AcEvents.RaiseCarImpact(rig, 60f, wall); firstHit = lastHit = Time.time; }
                else if (Time.time - firstHit < 1f && Time.time - lastHit >= 0.1f) { AcEvents.RaiseCarImpact(rig, 35f, wall); lastHit = Time.time; }   // P: every 0.1 s while it stays
                tmp.Clear(); audio.Tap(tmp);
                var now = new HashSet<string>();
                for (int k = 0; k < tmp.Count; k++) now.Add(tmp[k].Event);
                foreach (var e in now) if (!prev.Contains(e)) { int c; counts.TryGetValue(e, out c); counts[e] = c + 1; }
                prev = now;
                yield return null;
            }
            int hits, scrapes; counts.TryGetValue("hit", out hits); counts.TryGetValue("scrape", out scrapes);
            r.Check(hits == 1 && scrapes >= 1, "AcEvents.CarImpact: one hit, then scrape while it stays (hit starts " + hits + ", scrape starts " + scrapes + ")");

            // ---- settings toggles take effect without a respawn
            AcSettings.SetFlag("audio.interior3d", false);
            yield return r.Frames(5);
            r.Check(SpatialOf(audio, "engine_int") == 0, "audio.interior3d off -> engine_int 2D at once");
            AcSettings.SetFlag("audio.interior3d", true);
            yield return r.Frames(5);
            r.Check(SpatialOf(audio, "engine_int") == 1, "audio.interior3d on -> engine_int 3D (the bank's spatialiser) again");

            // ---- a Kind Remote rig fed like N, a stand-in, a passenger
            AcCarRig remote = null; bool got = false;
            yield return AcCarRig.Spawn(car, car.Skins.Count > 1 ? car.Skins[1].Id : (car.Skins.Count > 0 ? car.Skins[0].Id : ""), new AcCarRigOptions { Kind = AcCarKind.Remote, Audio = true, Parent = AcSession.WorldRoot }, x => { remote = x; got = true; });
            yield return r.Wait(() => got, 120f, "a Kind Remote rig");
            if (!r.Check(remote != null && remote.Audio != null && !remote.Audio.Local, "remote rig with a remote AcCarAudio")) yield break;
            if (remote.Physics != null) remote.Physics.SetPuppet(true);
            Vector3 rpos = rig.Root.transform.position + rig.Root.transform.forward * 25f;
            Quaternion rrot = rig.Root.transform.rotation;
            listener.transform.position = rig.Root.transform.position + Vector3.up * 1.2f;
            audio.SetInterior(false);
            float rt0 = Time.time;
            while (Time.time - rt0 < 6f && !(remote.Audio.Ready && remote.Audio.Voices > 0)) { FeedRemote(remote, rpos, rrot, 4500f, 0.7f, 30f); yield return null; }
            r.Check(remote.Audio.Voices > 0, "remote car (fed like N) plays: " + remote.Audio.Describe());
            r.Check(DopplerOf(remote.Audio) > 0.5f, "remote car sources have doppler");
            AcSettings.SetFlag("audio.doppler", false);
            for (int i = 0; i < 5; i++) { FeedRemote(remote, rpos, rrot, 4500f, 0.7f, 30f); yield return null; }
            r.Check(DopplerOf(remote.Audio) == 0f, "audio.doppler off -> doppler 0 on the remote car at once");
            AcSettings.SetFlag("audio.doppler", true);
            // passenger in the remote car: SetInterior(true) + the listener at its eye -> the full cockpit set, no doppler
            int before = remote.Audio.Sources;
            remote.Audio.SetInterior(true);
            for (int i = 0; i < 20; i++) { FeedRemote(remote, rpos, rrot, 4500f, 0.7f, 30f); PlaceAtEye(listener, remote); yield return null; }
            r.Check(remote.Audio.Interior && remote.Audio.Sources > before && DopplerOf(remote.Audio) == 0f,
                    "passenger: remote car switches to its cockpit set (" + before + " -> " + remote.Audio.Sources + " sources, doppler " + DopplerOf(remote.Audio) + ")");
            remote.Audio.SetInterior(false);
            listener.transform.position = rig.Root.transform.position + Vector3.up * 1.2f;
            // the owner stops sending: fade out
            float quiet0 = Time.time;
            while (Time.time - quiet0 < 1.2f) yield return null;   // > FeedTimeout 0.5 s + the 0.3 s fade
            r.Check(remote.Audio.Voices == 0, "remote car not fed for 1.2 s is silent (" + remote.Audio.Describe() + ")");

            // stand-in (car not installed here): common.bank surfaces only, at the stand-in's moving frame
            var standIn = new GameObject("Lab Stand-in");
            standIn.transform.position = rig.Root.transform.position + rig.Root.transform.right * 6f;
            var box = AcCarAudio.Attach(standIn, null, null, false);
            AcSurface grass = null;
            AcSurfaces surf = AcTrackWorld.Surfaces;
            if (surf != null) for (int i = 0; i < surf.Surfaces.Count && grass == null; i++) if (surf.Surfaces[i] != null && !string.IsNullOrEmpty(surf.Surfaces[i].SoundEvent)) grass = surf.Surfaces[i];
            var seenBox = new HashSet<string>();
            for (int i = 0; i < 60; i++)
            {
                var t = new AcTelemetry { Rpm = 3000f, SpeedKmh = 60f };
                if (grass != null) for (int w = 0; w < 4; w++) { var ws = t.Wheel(w); ws.Contact = true; ws.Surface = grass; t.SetWheel(w, ws); }
                box.Feed(t);
                tmp.Clear(); box.Tap(tmp);
                for (int k = 0; k < tmp.Count; k++) seenBox.Add(tmp[k].Event);
                yield return null;
            }
            r.Check(grass == null || seenBox.Count > 0, "stand-in plays common.bank surfaces (" + (grass != null ? grass.Key : "no sound surface") + ": " + string.Join(",", new List<string>(seenBox).ToArray()) + ")");
            UnityEngine.Object.Destroy(standIn);

            // ---- budget: 12 more remote cars at 30..250 m - the nearest 10 audible, the 2 farthest never built
            var fleet = new List<AcCarAudio>();
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject("Lab Fleet " + i);
                go.transform.position = listener.transform.position + new Vector3(30f + i * 20f, 0f, 0f);
                fleet.Add(AcCarAudio.Attach(go, car, null, false));
            }
            float ft0 = Time.time;
            while (Time.time - ft0 < 8f)
            {
                for (int i = 0; i < fleet.Count; i++) Feed(fleet[i], 5000f, 0.8f, 3, false);
                FeedRemote(remote, rpos + Vector3.up * 1000f, rrot, 0f, 0f, 0f);   // park the remote far away (its own cull)
                yield return null;
            }
            int audibleN = 0, built = 0;
            for (int i = 0; i < fleet.Count; i++) { if (fleet[i].Voices > 0) audibleN++; if (fleet[i].Built) built++; }
            AcLabRunner.Log("fleet: nearest " + fleet[0].Describe() + " | farthest " + fleet[fleet.Count - 1].Describe());
            r.Check(audibleN <= 10 && audibleN >= 8, "budget: " + audibleN + " of 12 remote cars audible (max 10, nearest first)");
            r.Check(!fleet[fleet.Count - 1].Built && !fleet[fleet.Count - 2].Built, "the 2 farthest cars never built their sound set (no decode, no sources): built " + built + " of 12");
            for (int i = 0; i < fleet.Count; i++) UnityEngine.Object.Destroy(fleet[i].gameObject);
            remote.Dispose();

            // ---- teardown through R: RequestHub -> AcSession.Unloading -> the rig and its audio go
            r.Check(AcRace.RequestHub(), "AcRace.RequestHub");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Hub, 30f, "the hub");
            yield return r.Frames(5);
            r.Check(audio == null || audio.Voices == 0, "my car's audio stopped with the rig");
            int left = 0;
            foreach (var c in UnityEngine.Object.FindObjectsByType<AcCarAudio>(FindObjectsSortMode.None)) if (c != null && c.Voices > 0) left++;
            r.Check(left == 0, "no car audio playing after the track unloaded (" + left + ")");
        }

        static void PlaceAtEye(AudioListener l, AcCarRig rig)
        {
            Vector3 eye = new Vector3(0f, 0.6f, 0f);
            if (rig.Model != null && rig.Model.Root != null && rig.Model.DriverEye != Vector3.zero) { l.transform.position = rig.Model.Root.TransformPoint(rig.Model.DriverEye); l.transform.rotation = rig.Root.transform.rotation; return; }
            l.transform.position = rig.Root.transform.TransformPoint(eye);
            l.transform.rotation = rig.Root.transform.rotation;
        }

        static void FeedRemote(AcCarRig rig, Vector3 pos, Quaternion rot, float rpm, float thr, float kmh)
        {
            var t = new AcTelemetry { Rpm = rpm, Throttle = thr, Gear = 3, SpeedKmh = kmh, IdleRpm = 900f, LimiterRpm = 9000f };
            if (rig.Physics != null) rig.Physics.ApplyPuppet(pos, rot, rot * Vector3.forward * (kmh / 3.6f), t);
            else rig.Root.transform.SetPositionAndRotation(pos, rot);
            if (rig.Audio != null) rig.Audio.Feed(t);
        }

        /// <summary>1 when every engine_int voice is 3D, 0 when every one is 2D, -1 when none plays.</summary>
        static int SpatialOf(AcCarAudio a, string ev)
        {
            var tmp = new List<AcAudioVoice>();
            a.Tap(tmp);
            int n3 = 0, n2 = 0;
            for (int i = 0; i < tmp.Count; i++) if (tmp[i].Event == ev) { if (tmp[i].Spatial) n3++; else n2++; }
            return n3 + n2 == 0 ? -1 : n2 == 0 ? 1 : n3 == 0 ? 0 : 2;
        }

        static float DopplerOf(AcCarAudio a)
        {
            float d = 0f;
            foreach (var s in a.GetComponentsInChildren<AudioSource>()) if (s.spatialBlend > 0.5f) d = Mathf.Max(d, s.dopplerLevel);
            return d;
        }

        // ------------------------------------------------------------------ shared

        static AudioListener EnsureListener(AcLabRunner r)
        {
            foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l != null && l.enabled && l.gameObject.activeInHierarchy) l.enabled = false;
            var al = r.Cam.GetComponent<AudioListener>();
            if (al == null) al = r.Cam.gameObject.AddComponent<AudioListener>();
            al.enabled = true;
            return al;
        }

        // ------------------------------------------------------------------ audio-damage (iteration 2, DAMAGE_DESIGN 6 / 10)

        /// <summary><c>audio-damage</c>: the 458 rolling at 80 km/h with suspension_damage fed 0 -> 0.6 -> 1 (AcCarAudio.LabSuspDamage, players built
        /// as with damage on): the wheel event's flutter_4 voice must be silent at 0 and grow with the damage (its bus: -42 dB at 0.44, ~0 dB at
        /// 0.75); then Repair(true) / Repair(false) must start common.bank unscrew / screw once each.</summary>
        static IEnumerator RunDamage(AcLabRunner r)
        {
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            var car = r.Game.Content.Car("ferrari_458");
            if (!r.Check(car != null && car.Owned && car.BankPath != null, "ferrari_458 installed with a bank")) yield break;
            var listener = EnsureListener(r);
            AcCarAudio.LabDamageOn = true;
            var root = new GameObject("Lab Car 458 damage");
            root.transform.position = new Vector3(0f, 3000f, 0f);
            var audio = AcCarAudio.Attach(root, car, null, true);
            listener.transform.position = root.transform.TransformPoint(new Vector3(3f, 1f, 0f));
            float t0 = Time.realtimeSinceStartup;
            while (!audio.Ready && Time.realtimeSinceStartup - t0 < 180f) { FeedRolling(audio, 0f); yield return null; }
            if (!r.Check(audio.Ready, "458 sound ready")) { AcCarAudio.LabDamageOn = false; UnityEngine.Object.Destroy(root); yield break; }
            var tmp = new List<AcAudioVoice>();
            float[] levels = { 0f, 0.6f, 1f };
            float[] vol = new float[3];
            for (int k = 0; k < levels.Length; k++)
            {
                audio.LabSuspDamage = levels[k];
                float max = 0f;
                for (int i = 0; i < 120; i++)
                {
                    FeedRolling(audio, 80f);
                    yield return null;
                    if (i < 30) continue;       // settle
                    tmp.Clear(); audio.Tap(tmp);
                    for (int v = 0; v < tmp.Count; v++)
                        if (tmp[v].Clip != null && tmp[v].Clip.name.IndexOf("flutter", StringComparison.OrdinalIgnoreCase) >= 0) max = Mathf.Max(max, tmp[v].Volume);
                }
                vol[k] = max;
                AcLabRunner.Log("audio-damage: suspension_damage " + levels[k].ToString("0.0") + " -> flutter voice volume " + max.ToString("0.0000"));
            }
            r.Check(vol[0] < 0.01f, "flutter silent at suspension_damage 0 (" + vol[0].ToString("0.0000") + ")");
            r.Check(vol[1] > vol[0] && vol[2] > vol[1] && vol[2] > 0.05f, "flutter grows with the damage (0.6: " + vol[1].ToString("0.000") + ", 1: " + vol[2].ToString("0.000") + ")");
            audio.LabSuspDamage = 0f;

            var counts = new Dictionary<string, int>();
            var prev = new HashSet<string>();
            for (int i = 0; i < 240; i++)
            {
                FeedRolling(audio, 0f);
                if (i == 10) audio.Repair(true);
                if (i == 130) audio.Repair(false);
                tmp.Clear(); audio.Tap(tmp);
                var now = new HashSet<string>();
                for (int k = 0; k < tmp.Count; k++) now.Add(tmp[k].Event);
                foreach (var e in now) if (!prev.Contains(e)) { int c; counts.TryGetValue(e, out c); counts[e] = c + 1; }
                prev = now;
                yield return null;
            }
            int us, sc;
            counts.TryGetValue("unscrew", out us); counts.TryGetValue("screw", out sc);
            AcLabRunner.Log("audio-damage: events started in the repair phase: " + string.Join(", ", new List<string>(counts.Keys).ToArray()));
            r.Check(us == 1, "Repair(true) played common unscrew once (" + us + ")");
            r.Check(sc == 1, "Repair(false) played common screw once (" + sc + ")");
            AcCarAudio.LabDamageOn = false;
            UnityEngine.Object.Destroy(root);
        }

        static void FeedRolling(AcCarAudio a, float kmh)
        {
            a.Feed(new AcTelemetry { Rpm = 2500f, Throttle = 0.2f, Gear = kmh > 1f ? 3 : 0, IdleRpm = 900f, LimiterRpm = 9000f, SpeedKmh = kmh });
        }

        static void Feed(AcCarAudio a, float rpm, float thr, int gear, bool limiter)
        {
            a.Feed(new AcTelemetry { Rpm = rpm, Throttle = thr, Gear = gear, Limiter = limiter, IdleRpm = 900f, LimiterRpm = 9000f });
        }

        /// <summary>Idle 2 s, full-throttle sweep 8 s, hold at the limiter 1 s, lift and sweep down 6 s, idle 2 s; capture every frame.</summary>
        static IEnumerator Sweep(AcLabRunner r, AcCarAudio a, AudioListener l, float idle, float lim, string name, bool interior)
        {
            var rec = new Recorder();
            float start = Time.time;
            int maxVoices = 0;
            float maxDist = 0f;
            while (true)
            {
                float t = Time.time - start;
                if (t > 19f) break;
                float rpm, thr;
                if (t < 2f) { rpm = idle; thr = 0f; }
                else if (t < 10f) { rpm = idle + (lim - idle) * (t - 2f) / 8f; thr = 1f; }
                else if (t < 11f) { rpm = lim; thr = 1f; }
                else if (t < 17f) { rpm = lim - (lim - idle) * (t - 11f) / 6f; thr = 0f; }
                else { rpm = idle; thr = 0f; }
                Feed(a, rpm, thr, rpm > 5000f ? 3 : 2, false);
                yield return null;
                rec.Capture(a);
                maxVoices = Mathf.Max(maxVoices, a.Voices);
                var fr = rec.Frames[rec.Frames.Count - 1];
                for (int k = 0; k < fr.Length; k++) maxDist = Mathf.Max(maxDist, fr[k].Distortion);
            }
            AcLabRunner.Log(name + ": " + rec.Frames.Count + " frames, max voices " + maxVoices + ", max distortion level " + maxDist.ToString("0.000") + ", mixer output rms " + rec.OutputRms.ToString("0.0000") + (rec.OutputRms <= 0f ? " (no audio device in batch mode?)" : ""));
            r.Check(maxVoices > 0 && maxVoices <= 16, name + " voices 1..16 (" + maxVoices + ")");
            if (interior) r.Check(maxDist > 0.15f, name + ": the bank's engine_int distortion follows rpm (max level " + maxDist.ToString("0.000") + ")");
            // 1) the voice-state render (what the runtime asked its sources to play; one continuous play position per source)
            float[] st = Render(rec, l.transform.position);
            string path = Path.Combine(r.Out, name + ".wav");
            AcWav.WritePcm16(path, st, st.Length, 2, AcOfflineMix.OutRate);
            AcLabRunner.Log("wrote " + path);
            float idleA = 0f, fullA = 0f;
            for (int s = 0; s < 19; s++)
            {
                float rms, pk; AcOfflineMix.Levels(st, s, s + 1, out rms, out pk);
                float aw = AcOfflineMix.RmsA(st, s, s + 1);
                if (s == 1) idleA = aw;
                if (s == 9) fullA = aw;
                AcLabRunner.Log("  " + name + " t " + s + "-" + (s + 1) + " s rms " + AcOfflineMix.ToDb(rms).ToString("0.0") + " dBFS peak " + AcOfflineMix.ToDb(pk).ToString("0.0") + " dBFS, " + AcOfflineMix.ToDb(aw).ToString("0.0") + " dB(A)FS");
            }
            AcLabRunner.Log("  " + name + " voice render: clicks (steps > 0.05) idle s1-2 " + Jumps(st, 1f, 2f, AcOfflineMix.OutRate) + ", full throttle s9-10 " + Jumps(st, 9f, 10f, AcOfflineMix.OutRate) + " (harness render: 0 / 0)");
            r.Check(fullA > idleA * 3.162f, name + " voice render: full throttle near the limiter at least 10 dB(A) louder than idle (" + AcOfflineMix.ToDb(fullA).ToString("0.0") + " vs " + AcOfflineMix.ToDb(idleA).ToString("0.0") + " dB(A)FS = +" + (AcOfflineMix.ToDb(fullA) - AcOfflineMix.ToDb(idleA)).ToString("0.0") + "; harness: +20 int / +16 ext; 10 dB = ours)");

            // 2) Unity's real mixer output over the same sweep
            var cap = rec.Out;
            float[] u = cap.ToArray();
            AcLabRunner.Log("  " + name + " Unity output capture: " + cap.Seconds.ToString("0.0") + " s at " + cap.Rate + " Hz (blocks aligned " + cap.Aligned + ", by clock " + cap.ByClock + ", gaps " + cap.Gaps + ")");
            if (cap.Seconds < 15f) { r.Check(false, name + " Unity output captured (" + cap.Seconds.ToString("0.0") + " s of 19)"); yield break; }
            string upath = Path.Combine(r.Out, name + "-unity.wav");
            AcWav.WritePcm16(upath, u, u.Length, 2, cap.Rate);
            AcLabRunner.Log("wrote " + upath);
            float uIdle = 0f, uFull = 0f;
            for (int s = 0; s < 19 && s + 1 <= cap.Seconds; s++)
            {
                float rms, pk; AcOfflineMix.Levels(u, s, s + 1, cap.Rate, out rms, out pk);
                float aw = AcOfflineMix.RmsA(u, s, s + 1, cap.Rate);
                if (s == 1) uIdle = aw;
                if (s == 9) uFull = aw;
                AcLabRunner.Log("  " + name + " UNITY t " + s + "-" + (s + 1) + " s rms " + AcOfflineMix.ToDb(rms).ToString("0.0") + " dBFS peak " + AcOfflineMix.ToDb(pk).ToString("0.0") + " dBFS, " + AcOfflineMix.ToDb(aw).ToString("0.0") + " dB(A)FS");
            }
            AcLabRunner.Log("  " + name + " Unity output: clicks idle s1-2 " + Jumps(u, 1f, 2f, cap.Rate) + ", full throttle s9-10 " + Jumps(u, 9f, 10f, cap.Rate));
            r.Check(uFull > uIdle * 3.162f, name + " UNITY OUTPUT: full throttle near the limiter at least 10 dB(A) louder than idle (" + AcOfflineMix.ToDb(uFull).ToString("0.0") + " vs " + AcOfflineMix.ToDb(uIdle).ToString("0.0") + " dB(A)FS = +" + (AcOfflineMix.ToDb(uFull) - AcOfflineMix.ToDb(uIdle)).ToString("0.0") + "; harness: +20 int / +16 ext; 10 dB = ours)");
        }

        /// <summary>
        /// Mixes the captured voices offline: each frame interval, each voice from its clip at its pitch and volume (3D voices: a plain 1/d from
        /// the recorded listener position beyond 6 m - approximate, panning-free); voices on a bus with the bank's FMOD Distortion go through
        /// the same waveshaper before the gain that follows it, as the bank orders it. Low-pass filters are not rendered.
        /// Each source keeps ONE continuous play position (re-synced to its AudioSource.timeSamples only when it starts, changes clip or is
        /// more than 250 ms away = restarted): the earlier render re-seeked every frame to timeSamples, which Unity updates once per DSP
        /// buffer - at the batch lab's ~2000 fps that replayed the same few samples over and over (a broadband buzz: 60 clicks per second
        /// at idle, 948 at full throttle, 8 kHz band +30 dB over the harness) and swamped the quiet idle in dB(A) (polish pass 2026-11-01).
        /// </summary>
        static float[] Render(Recorder rec, Vector3 listener)
        {
            float total = rec.Times.Count > 0 ? rec.Times[rec.Times.Count - 1] : 0f;
            int frames = (int)(total * AcOfflineMix.OutRate) + 1;
            var dst = new float[frames * 2];
            var grp = new float[frames * 2];
            var pcm = new Dictionary<AudioClip, float[]>();
            var pos = new Dictionary<int, double>();
            var clipOf = new Dictionary<int, AudioClip>();
            var volOf = new Dictionary<int, float>();
            var prev = new HashSet<int>();
            var now = new HashSet<int>();
            for (int f = 1; f < rec.Frames.Count; f++)
            {
                int a = (int)(rec.Times[f - 1] * AcOfflineMix.OutRate), b = Math.Min((int)(rec.Times[f] * AcOfflineMix.OutRate), frames);
                if (b <= a) continue;
                float level = 0f, gain = 1f;
                now.Clear();
                foreach (var v in rec.Frames[f])
                {
                    float[] data;
                    if (!pcm.TryGetValue(v.Clip, out data)) { data = new float[v.Clip.samples * v.Clip.channels]; v.Clip.GetData(data, 0); pcm[v.Clip] = data; }
                    double p; AudioClip c; float g0;
                    bool cont = prev.Contains(v.Source) && pos.TryGetValue(v.Source, out p) && clipOf.TryGetValue(v.Source, out c) && c == v.Clip;
                    p = cont ? pos[v.Source] : v.TimeSamples;
                    if (cont)
                    {
                        double len = Math.Max(1, v.Clip.samples - 1), diff = Math.Abs(p - v.TimeSamples);
                        if (v.Loop) diff = Math.Min(diff, len - diff);
                        if (diff > 0.25 * v.Clip.frequency) { cont = false; p = v.TimeSamples; }   // ours: restarted / re-seeked
                    }
                    float att = 1f;
                    if (v.Spatial) { float d = Vector3.Distance(v.Position, listener); att = d > 6f ? 6f / d : 1f; }
                    float g1 = v.Volume * att;
                    g0 = cont && volOf.TryGetValue(v.Source, out g0) ? g0 : g1;
                    bool dist = v.Distortion > 0f && v.DistortionGain > 0f;
                    if (dist) { level = v.Distortion; gain = v.DistortionGain; }
                    AcOfflineMix.MixVoice(dist ? grp : dst, a, b - a, data, v.Clip.channels, v.Clip.frequency, ref p, v.Pitch, g0, g1, v.Loop);
                    pos[v.Source] = p; clipOf[v.Source] = v.Clip; volOf[v.Source] = g1; now.Add(v.Source);
                }
                var t = prev; prev = now; now = t;
                if (level > 0f)
                    for (int i = a * 2; i < b * 2; i++) { dst[i] += AcSndFx.Distort(grp[i] / gain, level) * gain; grp[i] = 0f; }
            }
            return dst;
        }
    }
}
