using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// A layout's <c>data/ai_hints.ini</c> (research ac_track_formats.md 6.12): Kunos' per-track corrections for the AI, ranges in lap
    /// progress (0..1 of fast_lane.ai). Present on 12 installed layouts [VERIFIED: imola, ks_black_cat_county x3, ks_laguna_seca,
    /// ks_nurburgring gp_a/gp_b, ks_silverstone gp, ks_vallelunga extended_circuit, magione, monza, spa].
    /// <list type="bullet">
    /// <item><c>[HINT_n] START END VALUE</c> - corner speed multiplier over the range (magione 1.2, laguna seca corkscrew 0.7) [BELIEF:
    /// applied to the planned corner speed].</item>
    /// <item><c>[BRAKEHINT_n] START END VALUE</c> - braking multiplier over the range (0.6..0.95 in the install: brake earlier / softer)
    /// [BELIEF: applied to the braking deceleration the plan assumes, like ai.ini BRAKE_HINT].</item>
    /// <item><c>[DANGER_n] START END LEFT RIGHT</c> - fraction of the room left / right the AI may use (monza 0.5/0.5, imola 0.5/1.0)
    /// [BELIEF: multiplies the payload side room].</item>
    /// </list>
    /// Where ranges overlap (black_cat_county does) the most cautious value wins (smallest) [BELIEF]. START &gt; END wraps over the line.
    /// ai_corners.ini (magione only) is not used. Unity-free, never throws (problems in <see cref="Problems"/>).
    /// </summary>
    public sealed class AcAiHints
    {
        public struct Range
        {
            public float Start, End, Value, Left, Right;
            public bool Covers(float p) { return Start <= End ? p >= Start && p <= End : p >= Start || p <= End; }
        }

        public readonly List<Range> Hints = new List<Range>(), BrakeHints = new List<Range>(), Dangers = new List<Range>();
        public readonly List<string> Problems = new List<string>();
        public string Path = "";

        public int Count { get { return Hints.Count + BrakeHints.Count + Dangers.Count; } }
        public bool IsEmpty { get { return Count == 0; } }

        /// <summary>Reads <c>&lt;dataDir&gt;/ai_hints.ini</c>; null when the layout has none (the usual case).</summary>
        public static AcAiHints Load(string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir)) return null;
            string p = System.IO.Path.Combine(dataDir, "ai_hints.ini");
            try
            {
                if (!File.Exists(p)) return null;
                AcAiHints h = FromIni(AcIni.Load(p));
                h.Path = p;
                return h;
            }
            catch (Exception e) { var h = new AcAiHints { Path = p }; h.Problems.Add(e.Message); return h; }
        }

        public static AcAiHints FromIni(AcIni ini)
        {
            var h = new AcAiHints();
            if (ini == null) return h;
            for (int i = 0; i < ini.Order.Count; i++)
            {
                AcIniSection s = ini.Order[i];
                string n = s.Name.ToUpperInvariant();
                var r = new Range { Start = s.GetFloat("START", -1f), End = s.GetFloat("END", -1f), Value = s.GetFloat("VALUE", 1f), Left = s.GetFloat("LEFT", 1f), Right = s.GetFloat("RIGHT", 1f) };
                bool ok = r.Start >= 0f && r.Start <= 1f && r.End >= 0f && r.End <= 1f;
                if (n.StartsWith("BRAKEHINT_", StringComparison.Ordinal)) { if (ok && r.Value > 0.05f) h.BrakeHints.Add(r); else h.Problems.Add(s.Name + ": bad range/value"); }
                else if (n.StartsWith("HINT_", StringComparison.Ordinal)) { if (ok && r.Value > 0.05f) h.Hints.Add(r); else h.Problems.Add(s.Name + ": bad range/value"); }
                else if (n.StartsWith("DANGER_", StringComparison.Ordinal)) { if (ok) h.Dangers.Add(r); else h.Problems.Add(s.Name + ": bad range"); }
                else h.Problems.Add("unknown section " + s.Name);
            }
            return h;
        }

        /// <summary>Corner-speed multiplier at lap progress <paramref name="p"/> (1 = none).</summary>
        public float SpeedFactor(float p) { return Lowest(Hints, p); }

        /// <summary>Braking multiplier at lap progress <paramref name="p"/> (1 = none).</summary>
        public float BrakeFactor(float p) { return Lowest(BrakeHints, p); }

        static float Lowest(List<Range> list, float p)
        {
            float v = 1f; bool any = false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Covers(p)) { v = any ? Math.Min(v, list[i].Value) : list[i].Value; any = true; }
            return v;
        }

        /// <summary>Shrinks the side room of <paramref name="tr"/> inside the DANGER ranges (once, when the track is prepared). Returns the
        /// number of points changed.</summary>
        public int ApplyDanger(AcAiTrack tr)
        {
            if (tr == null || tr.IsEmpty || Dangers.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < tr.Count; i++)
            {
                float p = tr.Spline.PointProgress(i);
                float l = 1f, r = 1f; bool hit = false;
                for (int k = 0; k < Dangers.Count; k++)
                    if (Dangers[k].Covers(p)) { l = Math.Min(l, Dangers[k].Left); r = Math.Min(r, Dangers[k].Right); hit = true; }
                if (!hit) continue;
                tr.SideL[i] = Math.Max(0.3f, tr.SideL[i] * Math.Max(0f, l)); // ours: 0.3 m floor as AcAiTrack
                tr.SideR[i] = Math.Max(0.3f, tr.SideR[i] * Math.Max(0f, r));
                n++;
            }
            return n;
        }

        public override string ToString()
        {
            return Hints.Count + " hint(s), " + BrakeHints.Count + " brake hint(s), " + Dangers.Count + " danger zone(s)" + (Problems.Count > 0 ? " (" + string.Join("; ", Problems.ToArray()) + ")" : "");
        }
    }
}
