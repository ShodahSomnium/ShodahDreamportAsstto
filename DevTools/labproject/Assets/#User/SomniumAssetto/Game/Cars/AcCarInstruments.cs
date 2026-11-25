using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] The cockpit instruments of one built LOD (port of the ETS EtsInstruments pattern onto AC's data):
    /// <list type="bullet">
    /// <item>analog_instruments.ini needles: localRotation = rest * Euler(0, 0, -angle) about the needle node's local Z, angle from
    /// <see cref="AcCarNeedleDef.Angle"/> (harness <c>needles</c>: the needle meshes lie in their node's XY plane, local Z points forward on
    /// 244 of 256 needles; the 12 backward ones carry negative STEPs, so AC uses one fixed sense - C3's mirror rule gives the Unity sign).
    /// OBJECT_NAME_MAX needles keep the maximum. PLACE_HOLDER needles sit at ZERO.</item>
    /// <item>digital_instruments.ini: [LED_n] emissive at RPM_SWITCH (blinking above BLINK_SWITCH at BLINK_HZ), *_SERIE / WATER_TEMP LED
    /// bars and [FUEL_n] segments shown progressively [BELIEF: visibility], [FUEL_WARNING] lit below FUEL_SWITCH, [DISPLAY_n] meshes lit
    /// with EMISSIVE * INTENSITY, [ITEM_n] texts (<see cref="AcCarDisplayText"/>), RPM graphs and GEAR_TX gear pictures; GFORCE_SERIE bars
    /// from the telemetry g (huracan performante).</item>
    /// </list>
    /// <see cref="Apply"/> allocates nothing; transforms / emissives / meshes are written only on change.
    /// </summary>
    public sealed class AcCarInstruments
    {
        sealed class Needle
        {
            public AcCarNeedleDef Def;
            public Transform Node, MaxNode;
            public Quaternion Rest, MaxRest;
            public float Last = float.NaN, LastMax = float.NaN, Max;
        }

        sealed class Led
        {
            public AcCarLedDef Def;
            public Renderer[] Renderers;
            public int Lit = -1;
        }

        sealed class SeriesLed
        {
            public AcLedSeriesKind Kind;
            public char Axis;
            public float Sign = 1f;
            public GameObject Go;
            public float Threshold;
            public int Shown = -1;
        }

        sealed class FuelLed
        {
            public AcCarFuelLedDef Def;
            public GameObject Go;
            public Renderer[] Renderers;
            public int Lit = -1;
        }

        readonly List<Needle> _needles = new List<Needle>();
        readonly List<Led> _leds = new List<Led>();
        readonly List<SeriesLed> _series = new List<SeriesLed>();
        readonly List<FuelLed> _fuel = new List<FuelLed>();
        readonly List<AcCarDisplayText> _texts = new List<AcCarDisplayText>();
        readonly List<AcCarRpmGraph> _graphs = new List<AcCarRpmGraph>();
        readonly List<AcCarTextureItem> _texItems = new List<AcCarTextureItem>();

        public int NeedleCount { get { return _needles.Count; } }
        public int LedCount { get { return _leds.Count + _series.Count + _fuel.Count; } }
        public int TextCount { get { return _texts.Count + _graphs.Count + _texItems.Count; } }

        // ---- read-only views for the lab (no per-frame use)
        public int NeedleTotal { get { return _needles.Count; } }
        public Transform NeedleNode(int i) { return _needles[i].Node; }
        public AcGaugeKind NeedleKind(int i) { return _needles[i].Def.Kind; }
        public int TextItemCount { get { return _texts.Count; } }
        public AcCarDisplayText TextItem(int i) { return _texts[i]; }
        public int TextureItemCount { get { return _texItems.Count; } }
        public int TextureItemsShown { get { int n = 0; for (int i = 0; i < _texItems.Count; i++) if (_texItems[i].Shown) n++; return n; } }
        public int GraphCount { get { return _graphs.Count; } }
        /// <summary>Series LEDs of a kind that are shown now / bound.</summary>
        public int SeriesShown(AcLedSeriesKind kind, out int total)
        {
            int n = 0; total = 0;
            for (int i = 0; i < _series.Count; i++) { if (_series[i].Kind != kind) continue; total++; if (_series[i].Shown == 1) n++; }
            return n;
        }

        /// <summary>Binds the instrument inis to a built LOD. <paramref name="texture"/> resolves RPM_GRAPH textures by file name.</summary>
        public static AcCarInstruments Bind(AcCarVisual v, Func<string, Transform> find, Func<string, Texture2D> texture, bool digital, List<string> missing)
        {
            var ins = new AcCarInstruments();
            if (v == null) return ins;
            for (int i = 0; i < v.Needles.Count; i++)
            {
                var d = v.Needles[i];
                var t = find(d.ObjectName);
                if (t == null) { if (!d.NameDefaulted && missing != null) missing.Add(d.ObjectName); continue; }
                var n = new Needle { Def = d, Node = t, Rest = t.localRotation };
                if (d.ObjectNameMax != null) { n.MaxNode = find(d.ObjectNameMax); if (n.MaxNode != null) n.MaxRest = n.MaxNode.localRotation; }
                ins._needles.Add(n);
                if (d.Kind == AcGaugeKind.PlaceHolder) t.localRotation = n.Rest * Quaternion.Euler(0f, 0f, -d.Angle(d.HasMinValue ? d.MinValue : 0f));
            }
            for (int i = 0; i < v.Leds.Count; i++)
            {
                var d = v.Leds[i];
                var t = find(d.ObjectName);
                if (t == null) { if (missing != null) missing.Add(d.ObjectName); continue; }
                var rs = t.GetComponentsInChildren<Renderer>(true);
                for (int k = 0; k < rs.Length; k++) AcCarShading.Prepare(rs[k]);
                ins._leds.Add(new Led { Def = d, Renderers = rs });
            }
            for (int i = 0; i < v.Series.Count; i++)
            {
                var d = v.Series[i];
                int count = d.EndIndex - d.StartIndex + 1;
                int shift = d.CountBased && find(d.Prefix + d.StartIndex) == null ? 1 : 0; // WATER_LED_1..n when there is no _0
                for (int k = 0; k < count; k++)
                {
                    var t = find(d.Prefix + (d.StartIndex + k + shift));
                    if (t == null) continue;
                    float th = count > 1 ? d.Start + (d.End - d.Start) * k / (count - 1) : d.Start;
                    ins._series.Add(new SeriesLed { Kind = d.Kind, Axis = d.Axis, Sign = d.Sign, Go = t.gameObject, Threshold = th });
                }
            }
            for (int i = 0; i < v.FuelLeds.Count; i++)
            {
                var d = v.FuelLeds[i];
                var t = find(d.ObjectName);
                if (t == null) { if (missing != null) missing.Add(d.ObjectName); continue; }
                var f = new FuelLed { Def = d, Go = t.gameObject };
                if (d.HasEmissive) { f.Renderers = t.GetComponentsInChildren<Renderer>(true); for (int k = 0; k < f.Renderers.Length; k++) AcCarShading.Prepare(f.Renderers[k]); }
                ins._fuel.Add(f);
            }
            for (int i = 0; i < v.Displays.Count; i++)
            {
                var d = v.Displays[i];
                var t = find(d.Name);
                if (t == null) { if (missing != null) missing.Add(d.Name); continue; }
                var rs = t.GetComponentsInChildren<Renderer>(true);
                var c = new Vector3(d.Emissive.X * d.Intensity, d.Emissive.Y * d.Intensity, d.Emissive.Z * d.Intensity);
                for (int k = 0; k < rs.Length; k++) { AcCarShading.Prepare(rs[k]); AcCarShading.SetEmissive(rs[k], c); }
            }
            if (digital)
            {
                for (int i = 0; i < v.Items.Count; i++)
                {
                    var d = v.Items[i];
                    var parent = find(d.Parent);
                    if (parent == null) { if (missing != null && !string.Equals(d.Parent, "NULL", StringComparison.OrdinalIgnoreCase)) missing.Add(d.Parent); continue; }
                    try
                    {
                        if (d.Type == "RPM_GRAPH") { var g = AcCarRpmGraph.Create(d, parent, texture); if (g != null) ins._graphs.Add(g); else AcLog.InfoOnce("Car: " + v.CarId + " " + d.Section + " RPM_GRAPH textures " + d.TextureBase + " / " + d.TextureTop + " not found"); }
                        else if (d.Type == "GEAR_TX") { var g = AcCarTextureItem.Create(d, parent, texture); if (g != null) ins._texItems.Add(g); else AcLog.InfoOnce("Car: " + v.CarId + " " + d.Section + " GEAR_TX texture " + d.TextureBase + " not found"); }
                        else { var t = AcCarDisplayText.Create(d, parent); if (t != null) ins._texts.Add(t); }
                    }
                    catch (Exception e) { AcLog.InfoOnce("Car: " + v.CarId + " digital item " + d.Section + " failed: " + e.Message); }
                }
            }
            return ins;
        }

        /// <summary>Drives everything from the values (call when they change, at most once per frame). No allocation.</summary>
        public void Apply(ref AcInstruments v, ref AcCarDisplayExtras x, float time)
        {
            float water = v.WaterTempC > 0f ? v.WaterTempC : 90f; // ours: a warm engine when P gives no water temperature
            for (int i = 0; i < _needles.Count; i++)
            {
                var n = _needles[i];
                float val;
                switch (n.Def.Kind)
                {
                    case AcGaugeKind.Rpm: val = v.Rpm; break;
                    case AcGaugeKind.Speed: val = v.SpeedKmh; break;
                    case AcGaugeKind.Fuel: val = v.FuelLitres; break;
                    case AcGaugeKind.Turbo: val = v.TurboBar; break;
                    case AcGaugeKind.WaterTemp: val = water; break;
                    default: continue;
                }
                float a = n.Def.Angle(val);
                if (!(Mathf.Abs(a - n.Last) < 0.05f)) { n.Last = a; n.Node.localRotation = n.Rest * Quaternion.Euler(0f, 0f, -a); } // 0.05 deg: ours
                if (n.MaxNode != null)
                {
                    if (val > n.Max) n.Max = val;
                    float am = n.Def.Angle(n.Max);
                    if (!(Mathf.Abs(am - n.LastMax) < 0.05f)) { n.LastMax = am; n.MaxNode.localRotation = n.MaxRest * Quaternion.Euler(0f, 0f, -am); }
                }
            }
            for (int i = 0; i < _leds.Count; i++)
            {
                var l = _leds[i];
                bool on = v.Rpm >= l.Def.RpmSwitch && l.Def.RpmSwitch > 0f;
                if (on && l.Def.HasBlink && l.Def.BlinkHz > 0f && v.Rpm >= l.Def.BlinkSwitch) on = Mathf.Repeat(time * l.Def.BlinkHz, 1f) < 0.5f;
                int lit = on ? 1 : 0;
                if (lit == l.Lit) continue;
                l.Lit = lit;
                var c = on ? new Vector3(l.Def.Emissive.X, l.Def.Emissive.Y, l.Def.Emissive.Z) : Vector3.zero;
                for (int k = 0; k < l.Renderers.Length; k++) AcCarShading.SetEmissive(l.Renderers[k], c);
            }
            for (int i = 0; i < _series.Count; i++)
            {
                var s = _series[i];
                float val;
                switch (s.Kind)
                {
                    case AcLedSeriesKind.Rpm: val = v.Rpm; break;
                    case AcLedSeriesKind.Speed: val = v.SpeedKmh; break;
                    case AcLedSeriesKind.Turbo: val = v.TurboBar; break;
                    case AcLedSeriesKind.GForce: val = (s.Axis == 'Z' ? x.LongG : -x.LatG) * s.Sign; break; // AC axes (X = left) [BELIEF: AC's g sign]
                    default: val = water; break;
                }
                int shown = val >= s.Threshold ? 1 : 0;
                if (shown == s.Shown) continue;
                s.Shown = shown;
                if (s.Go != null) s.Go.SetActive(shown == 1);
            }
            for (int i = 0; i < _fuel.Count; i++)
            {
                var f = _fuel[i];
                bool on = f.Def.Warning ? v.FuelLitres < f.Def.FuelSwitch : v.FuelLitres >= f.Def.FuelSwitch;
                if (f.Def.Inverted) on = !on;
                int lit = on ? 1 : 0;
                if (lit == f.Lit) continue;
                f.Lit = lit;
                if (f.Renderers != null)
                {
                    var c = on ? new Vector3(f.Def.Emissive.X, f.Def.Emissive.Y, f.Def.Emissive.Z) : Vector3.zero;
                    for (int k = 0; k < f.Renderers.Length; k++) AcCarShading.SetEmissive(f.Renderers[k], c);
                }
                else if (f.Go != null) f.Go.SetActive(on);
            }
            for (int i = 0; i < _texts.Count; i++) _texts[i].Update(ref v, ref x);
            for (int i = 0; i < _graphs.Count; i++) _graphs[i].Update(v.Rpm);
            for (int i = 0; i < _texItems.Count; i++) _texItems[i].Update(ref v);
        }

        /// <summary>Forces every write on the next <see cref="Apply"/> (after a LOD switch).</summary>
        public void Invalidate()
        {
            for (int i = 0; i < _needles.Count; i++) { _needles[i].Last = float.NaN; _needles[i].LastMax = float.NaN; }
            for (int i = 0; i < _leds.Count; i++) _leds[i].Lit = -1;
            for (int i = 0; i < _series.Count; i++) _series[i].Shown = -1;
            for (int i = 0; i < _fuel.Count; i++) _fuel[i].Lit = -1;
            for (int i = 0; i < _texItems.Count; i++) _texItems[i].Invalidate();
        }

        public void Destroy()
        {
            for (int i = 0; i < _texts.Count; i++) _texts[i].Destroy();
            for (int i = 0; i < _graphs.Count; i++) _graphs[i].Destroy();
            for (int i = 0; i < _texItems.Count; i++) _texItems[i].Destroy();
            _texItems.Clear();
            _texts.Clear(); _graphs.Clear();
        }
    }
}
