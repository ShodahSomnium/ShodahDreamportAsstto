using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] lights.ini on one built LOD: every [LIGHT_n] / [BRAKE_n] NAME (a node or a mesh: 732/744 names resolve on the 86 cars,
    /// harness <c>carparts</c>; the 12 misses are data typos AC cannot light either) switches the emissive of the renderers below it.
    /// Rules (ETS EtsVehicleLampDriver pattern, AC semantics):
    /// <list type="bullet">
    /// <item>LIGHT: COLOR while the headlights are on, else OFF_COLOR (0 when absent). FLASH=1 entries also light with the high beam /
    /// flasher, PITLINE=1 entries also with the pit limiter [BELIEF from the race cars that use them: bmw_m3_gt2 FRONT_LIGHT_LOW FLASH,
    /// ferrari_458_gt2 LIGHT_RAIN PITLINE; SPECIAL=1 is treated as a normal light].</item>
    /// <item>BRAKE: COLOR while braking; else OFF_COLOR while the headlights are on (tail light), else 0 [BELIEF: the 458's centre brake light
    /// has OFF_COLOR=0 while the main one has 50 - a third brake light never doubles as a tail light].</item>
    /// <item>[LIGHT_ANIMATION_n] FILE (pop-up headlights: 4 sections in the install) runs to its end in TIME seconds while the headlights are on, back when off.</item>
    /// <item>AcLightState Reverse / Hazard / indicator bits are not used: vanilla lights.ini has only [HEADER], [LIGHT_n], [BRAKE_n] and
    /// [LIGHT_ANIMATION_n] - a harness scan of all 86 owned cars found no reverse or indicator sections (AC has no reverse lamps).</item>
    /// </list>
    /// No allocation per frame: emissives are written only when a group's state changes.
    /// </summary>
    public sealed class AcCarLights
    {
        sealed class Group
        {
            public AcCarLightDef Def;
            public Renderer[] Renderers;
            public int State = -1;   // 0 off, 1 dim (OFF_COLOR), 2 on
        }

        readonly List<Group> _groups = new List<Group>();
        readonly List<AcKsanimPlayer> _anims = new List<AcKsanimPlayer>();
        readonly List<float> _animTime = new List<float>();
        float[] _animT = new float[0];
        AcLightState _state;
        bool _applied;

        /// <summary>Groups bound (lights.ini entries whose node was found).</summary>
        public int Count { get { return _groups.Count; } }
        public int AnimCount { get { return _anims.Count; } }
        /// <summary>The i-th LIGHT_ANIMATION player (lab).</summary>
        public AcKsanimPlayer AnimPlayer(int i) { return _anims[i]; }

        /// <summary>Binds lights.ini to a built model; <paramref name="find"/> resolves node names. Missing names go to <paramref name="missing"/>.</summary>
        public static AcCarLights Bind(AcCarVisual v, Func<string, Transform> find, List<string> missing)
        {
            var l = new AcCarLights();
            if (v == null) return l;
            for (int i = 0; i < v.Lights.Count; i++)
            {
                var d = v.Lights[i];
                var t = find(d.Name);
                if (t == null) { if (missing != null) missing.Add(d.Name); continue; }
                var rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                for (int k = 0; k < rs.Length; k++) AcCarShading.Prepare(rs[k]);
                l._groups.Add(new Group { Def = d, Renderers = rs });
            }
            for (int i = 0; i < v.LightAnims.Count; i++)
            {
                var p = AcKsanimPlayer.Bind(v.LightAnims[i].Anim, find);
                if (p == null) continue;
                l._anims.Add(p);
                l._animTime.Add(Mathf.Max(0.05f, v.LightAnims[i].Time)); // ours: floor for TIME=0
            }
            l._animT = new float[l._anims.Count];
            return l;
        }

        /// <summary>Applies a light state; writes only the groups whose state changed (or everything with <paramref name="force"/>).</summary>
        public void Set(AcLightState s, bool force)
        {
            if (!force && _applied && s == _state) return;
            _state = s;
            _applied = true;
            bool head = (s & AcLightState.Headlights) != 0;
            bool high = (s & AcLightState.HighBeam) != 0;
            bool brake = (s & AcLightState.Brake) != 0;
            bool pit = (s & AcLightState.PitLimiter) != 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                var g = _groups[i];
                var d = g.Def;
                int st;
                if (d.Brake) st = brake ? 2 : (head ? 1 : 0);
                else st = head || (d.Flash && high) || (d.Pitline && pit) ? 2 : 0;
                if (!force && st == g.State) continue;
                g.State = st;
                Vector3 c;
                if (st == 2) c = new Vector3(d.Color.X, d.Color.Y, d.Color.Z);
                else if (st == 1 || !d.Brake) c = new Vector3(d.OffColor.X, d.OffColor.Y, d.OffColor.Z); // OffColor is 0 when absent
                else c = Vector3.zero;
                for (int k = 0; k < g.Renderers.Length; k++) AcCarShading.SetEmissive(g.Renderers[k], c);
            }
        }

        /// <summary>Moves pop-up light animations toward the headlight state. Call once per frame.</summary>
        public void Tick(float dt)
        {
            if (_anims.Count == 0) return;
            float target = (_state & AcLightState.Headlights) != 0 ? 1f : 0f;
            for (int i = 0; i < _anims.Count; i++)
            {
                float t = _animT[i];
                if (t == target && _anims[i].Time >= 0f) continue;
                t = Mathf.MoveTowards(t, target, dt / _animTime[i]);
                _animT[i] = t;
                _anims[i].Sample(t);
            }
        }

        /// <summary>Re-applies everything (after a LOD switch made this set visible).</summary>
        public void Refresh()
        {
            Set(_state, true);
            for (int i = 0; i < _anims.Count; i++) { _anims[i].Invalidate(); _anims[i].Sample(_animT[i]); }
        }
    }
}
