using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] Iteration 2 (DAMAGE_DESIGN 2.4): engine smoke of a damaged engine - a Unity ParticleSystem configured from the install's
    /// <c>system/cfg/engine_smoke.ini</c> ([SETTINGS] FREQUENCY_HZ, SIZE / COLOR / OPACITY / GRAVITY / DRAG bases + randoms, VELOCITY_RANDOM,
    /// SIZE_VELOCITY, OPACITY_VELOCITY; [LIFE_TIME] SECONDS), read through P's <see cref="AcDamageRules.EngineSmokeIni"/>. No texture ships:
    /// the puff is a soft disc generated in code, drawn with the client's stock Sprites/Default shader (as the cockpit displays). Created
    /// lazily by the first <see cref="Set"/> above 0 (no cost until a car smokes); world-space particles so the trail stays behind.
    /// </summary>
    public sealed class AcCarSmoke
    {
        static Texture2D _puff;

        ParticleSystem _ps;
        Material _mat;
        float _rate = -1f, _freqHz;
        bool _failed;

        public bool Created { get { return _ps != null; } }
        public float Rate { get { return _rate < 0f ? 0f : _rate; } }

        /// <summary>Smoke rate 0 (off) .. 1 at <paramref name="localPos"/> (model space) under <paramref name="root"/>. No allocation after
        /// the first call.</summary>
        public void Set(float rate01, Transform root, Vector3 localPos)
        {
            rate01 = Mathf.Clamp01(rate01);
            if (rate01 == _rate) return;
            _rate = rate01;
            if (rate01 <= 0f && _ps == null) return;
            if (_ps == null && !_failed) Create(root, localPos);
            if (_ps == null) return;
            var em = _ps.emission;
            em.rateOverTime = _freqHz * rate01;
            if (rate01 > 0f && !_ps.isPlaying) _ps.Play(true);
            else if (rate01 <= 0f && _ps.isPlaying) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Create(Transform root, Vector3 localPos)
        {
            if (root == null) return;
            try
            {
                var rules = AcDamageRules.Current;
                var ini = rules != null ? rules.EngineSmokeIni : null;
                var s = ini != null ? ini["SETTINGS"] : null;
                if (s == null) { _failed = true; AcLog.InfoOnce("Car: engine_smoke.ini [SETTINGS] missing - no engine smoke"); return; }
                Shader sh = AcShaders.Find("Sprites/Default");
                if (sh == null) sh = AcShaders.Find("UI/Default");
                if (sh == null) { _failed = true; AcLog.InfoOnce("Car: no particle shader (Sprites/Default) - no engine smoke"); return; }

                // fixer 2026-11-02: every fallback below is // ours (used only when the key is missing from engine_smoke.ini [SETTINGS])
                _freqHz = s.GetFloat("FREQUENCY_HZ", 15f); // ours fallback
                float[] sizeB = s.GetFloats("SIZE_BASE"), sizeR = s.GetFloats("SIZE_RANDOM"), colB = s.GetFloats("COLOR_BASE"), colR = s.GetFloats("COLOR_RANDOM");
                float[] velR = s.GetFloats("VELOCITY_RANDOM"), velB = s.GetFloats("VELOCITY_BASE"), sizeV = s.GetFloats("SIZE_VELOCITY_BASE");
                float opB = s.GetFloat("OPACITY_BASE", 0.3f), opR = s.GetFloat("OPACITY_RANDOM", 0f), opV = s.GetFloat("OPACITY_VELOCITY_BASE", -0.1f); // ours fallbacks
                float grav = s.GetFloat("GRAVITY_BASE", 0f), drag = s.GetFloat("DRAG_BASE", 0f); // ours fallbacks
                float lifeCap = rules.SmokeSeconds > 0f ? rules.SmokeSeconds : 15f;   // ours fallback when [LIFE_TIME] is absent
                // a puff lives until it is fully transparent (OPACITY / -OPACITY_VELOCITY), at most [LIFE_TIME] SECONDS // ours reading
                float life = opV < -1e-4f ? Mathf.Min(lifeCap, (opB + opR) / -opV) : lifeCap;
                life = Mathf.Max(0.2f, life);
                float size0 = At(sizeB, 0, 1f), sizeRand = At(sizeR, 0, 0f), grow = At(sizeV, 0, 0f); // ours fallbacks
                float c0 = At(colB, 0, 0.5f), cr = At(colR, 0, 0f); // ours fallbacks

                var go = new GameObject("Ac Engine Smoke");
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = localPos;
                _ps = go.AddComponent<ParticleSystem>();
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _ps.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = life;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Max(0.01f, size0 - sizeRand), size0 + sizeRand);
                float a0 = Mathf.Clamp01(opB - opR), a1 = Mathf.Clamp01(opB + opR);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(Mathf.Clamp01(c0 - cr), Mathf.Clamp01(c0 - cr), Mathf.Clamp01(c0 - cr), a0), new Color(Mathf.Clamp01(c0 + cr), Mathf.Clamp01(c0 + cr), Mathf.Clamp01(c0 + cr), a1));
                main.gravityModifier = -grav;   // [I] smoke rises: GRAVITY read as buoyancy (sign ours)
                main.maxParticles = Mathf.Clamp(Mathf.CeilToInt(_freqHz * life * 1.5f), 8, 500);   // ours headroom
                var shape = _ps.shape;
                shape.enabled = false;
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(At(velB, 0, 0f) - At(velR, 0, 0f), At(velB, 0, 0f) + At(velR, 0, 0f));
                vel.y = new ParticleSystem.MinMaxCurve(At(velB, 1, 0f) - At(velR, 1, 0f), At(velB, 1, 0f) + At(velR, 1, 0f));
                vel.z = new ParticleSystem.MinMaxCurve(At(velB, 2, 0f) - At(velR, 2, 0f), At(velB, 2, 0f) + At(velR, 2, 0f));
                if (drag > 0f)
                {
                    var lim = _ps.limitVelocityOverLifetime;
                    lim.enabled = true;
                    lim.drag = drag;
                    lim.limit = 1000f;
                }
                // SIZE_VELOCITY (m/s growth) -> size over life; OPACITY_VELOCITY -> linear fade to 0 by the end of life
                if (grow > 0f && size0 > 0f)
                {
                    var sol = _ps.sizeOverLifetime;
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1f + grow * life / size0));
                }
                var col = _ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;

                var pr = go.GetComponent<ParticleSystemRenderer>();
                _mat = new Material(sh) { name = "Ac engine smoke" };
                _mat.mainTexture = Puff();
                _mat.renderQueue = 3002; // ours: after the car's glass and displays
                pr.sharedMaterial = _mat;
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pr.receiveShadows = false;
                AcLog.InfoOnce("Car: engine smoke from engine_smoke.ini: " + _freqHz + " Hz, life " + life.ToString("0.0") + " s, size " + size0 + " (+" + grow + "/s), grey " + c0 + ", opacity " + opB);
            }
            catch (Exception e)
            {
                _failed = true;
                AcLog.Warn("Car: engine smoke failed: " + e.Message);
                Destroy();
            }
        }

        static float At(float[] a, int i, float def) { return a != null && a.Length > i ? a[i] : (a != null && a.Length > 0 ? a[0] : def); }

        /// <summary>A soft white disc (alpha falls off to the edge) - generated, not a game asset. // ours</summary>
        static Texture2D Puff()
        {
            if (_puff != null) return _puff;
            const int n = 64;
            _puff = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Ac smoke puff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            _puff.SetPixels32(px);
            _puff.Apply(false, true);
            return _puff;
        }

        /// <summary>Destroys the particle object and its material (the car's Dispose; the root's destruction would take the object too).</summary>
        public void Destroy()
        {
            if (_ps != null) UnityEngine.Object.Destroy(_ps.gameObject);
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            _ps = null; _mat = null; _rate = -1f;
        }
    }
}
