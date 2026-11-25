using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumAssetto;

// Damage research probe (collision topic, research/dmg_collision.md). Read-only. All names prefixed "col" to avoid clashes.
//   colsweep [car]       damage.ini sections/keys/value ranges over every owned car + damage keys of car/aero/suspensions/engine/
//                        drivetrain/colliders inis + VISUAL_OBJECT / DAMAGE_GLASS node presence in the LOD_0 kn5 + collider.kn5 extent
//   colraw <car>         raw damage.ini + the damage/collider/pit sections of the other inis of one car
//   colstrings <file> <regex>   ASCII + UTF-16LE strings of a binary matching a regex (key names only, no disassembly)
static partial class Program
{
    sealed class ColStat
    {
        public int Count; public double Min = double.MaxValue, Max = double.MinValue;
        public SortedDictionary<string, int> Values = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public void Add(string v)
        {
            Count++;
            v = (v ?? "").Trim();
            int c; Values.TryGetValue(v, out c); Values[v] = c + 1;
            double d;
            if (double.TryParse(v.Split(',')[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
            { if (d < Min) Min = d; if (d > Max) Max = d; }
        }
    }

    static string ColGroup(string s)
    {
        int i = s.Length;
        while (i > 0 && char.IsDigit(s[i - 1])) i--;
        return i < s.Length && i > 0 && s[i - 1] == '_' ? s.Substring(0, i) + "n" : s;
    }

    static void ColAdd(Dictionary<string, ColStat> st, string key, string v)
    {
        ColStat k; if (!st.TryGetValue(key, out k)) st[key] = k = new ColStat();
        k.Add(v);
    }

    static void ColDump(string title, Dictionary<string, ColStat> st, int maxVals)
    {
        Console.WriteLine("== " + title);
        foreach (var kv in st.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var s = kv.Value;
            string vals = string.Join(" ", s.Values.OrderByDescending(x => x.Value).Take(maxVals).Select(x => "'" + x.Key + "'x" + x.Value));
            Console.WriteLine("  " + kv.Key + "  n=" + s.Count + (s.Min <= s.Max ? "  range " + s.Min + ".." + s.Max : "") + "  distinct=" + s.Values.Count + "  " + vals);
        }
    }

    static int Cmd_colsweep(List<string> a)
    {
        var c = AcContent.Scan(_install);
        var dmg = new Dictionary<string, ColStat>();
        var other = new Dictionary<string, ColStat>();
        var secCount = new SortedDictionary<string, int>();
        var noDmg = new List<string>();
        var perCar = new List<string>();
        var odd = new List<string>();
        int cars = 0;
        foreach (var car in c.Cars)
        {
            if (!car.Owned || car.IsDlcStub) continue;
            if (a.Count > 1 && car.Id != a[1]) continue;
            cars++;
            var acd = AcdArchive.Open(car.Dir);
            Kn5File kn5 = null;
            try { kn5 = Kn5File.Read(car.Kn5Path, Kn5ReadOptions.HeaderOnly); } catch (Exception e) { odd.Add(car.Id + ": kn5 read failed " + e.Message); }
            var nodeNames = new HashSet<string>(StringComparer.Ordinal);
            if (kn5 != null) foreach (var n in kn5.Nodes) nodeNames.Add(n.Name);
            var d = acd.ReadIni("damage.ini");
            if (d == null) { noDmg.Add(car.Id); }
            else
            {
                var zones = new SortedDictionary<string, int>();
                int vo = 0, voMissing = 0;
                foreach (var s in d.Order)
                {
                    string g = ColGroup(s.Name);
                    int n; secCount.TryGetValue(g, out n); secCount[g] = n + 1;
                    if (g == "VISUAL_OBJECT_n")
                    {
                        vo++;
                        string nm = (s.Get("NAME") ?? "").Trim();
                        if (!nodeNames.Contains(nm)) { voMissing++; odd.Add(car.Id + ": " + s.Name + " NAME=" + nm + " not a node of " + Path.GetFileName(car.Kn5Path)); }
                        float mn = s.GetFloat("MIN_SPEED", -1), fs = s.GetFloat("FULL_SPEED", -1);
                        if (fs <= mn) odd.Add(car.Id + ": " + s.Name + " FULL_SPEED " + fs + " <= MIN_SPEED " + mn);
                    }
                    foreach (var k in s.Keys)
                    {
                        ColAdd(dmg, g + "." + k, s.Get(k));
                        if (k == "DAMAGE_ZONE") { int z; zones.TryGetValue(s.Get(k), out z); zones[s.Get(k)] = z + 1; }
                    }
                }
                // glass nodes in the kn5 per zone
                var glass = new StringBuilder();
                foreach (var z in new[] { "FRONT", "REAR", "LEFT", "RIGHT", "CENTER" })
                {
                    int gn = 0; foreach (var nm in nodeNames) if (nm.StartsWith("DAMAGE_GLASS_" + z + "_", StringComparison.Ordinal)) gn++;
                    glass.Append(z[0]).Append(gn);
                    bool sec = d.Has("DAMAGE_GLASS_" + z);
                    if (gn > 0 && !sec) odd.Add(car.Id + ": has DAMAGE_GLASS_" + z + "_* nodes but no [DAMAGE_GLASS_" + z + "] section");
                }
                perCar.Add(string.Format("{0,-34} init={1,-4} scr={2}/{3} osc={4} vo={5}{6} zones[{7}] glassNodes {8} glassSecs={9}", car.Id,
                    d.Get("DAMAGE", "INITIAL_LEVEL", "-"), d.Get("SCRATCHES", "MIN_SPEED", "-"), d.Get("SCRATCHES", "MAX_SPEED", "-"),
                    d.Get("OSCILLATIONS", "ENABLED", "-"), vo, voMissing > 0 ? "(" + voMissing + " missing)" : "",
                    string.Join(",", zones.Select(x => x.Key + ":" + x.Value)), glass, d.Order.Count(x => x.Name.StartsWith("DAMAGE_GLASS_"))));
            }
            var car_ = acd.ReadIniOrEmpty("car.ini");
            foreach (var k in new[] { "BODY_REPAIR_TIME_SEC", "ENGINE_REPAIR_TIME_SEC", "SUSP_REPAIR_TIME_SEC" })
                if (car_.Has("PIT_STOP", k)) ColAdd(other, "car.PIT_STOP." + k, car_.Get("PIT_STOP", k));
            ColAdd(other, "car.BASIC.TOTALMASS", car_.Get("BASIC", "TOTALMASS", "?"));
            var aero = acd.ReadIniOrEmpty("aero.ini");
            foreach (var s in aero.Order)
                foreach (var k in s.Keys)
                    if (k.StartsWith("ZONE_")) ColAdd(other, "aero." + ColGroup(s.Name) + "." + k, s.Get(k));
            var susp = acd.ReadIniOrEmpty("suspensions.ini");
            if (susp["DAMAGE"] != null) foreach (var k in susp["DAMAGE"].Keys) ColAdd(other, "susp.DAMAGE." + k, susp.Get("DAMAGE", k));
            else { ColAdd(other, "susp.DAMAGE.<missing>", "1"); odd.Add(car.Id + ": suspensions.ini has no [DAMAGE]"); }
            var eng = acd.ReadIniOrEmpty("engine.ini");
            if (eng["DAMAGE"] != null) foreach (var k in eng["DAMAGE"].Keys) ColAdd(other, "engine.DAMAGE." + k, eng.Get("DAMAGE", k));
            var col = acd.ReadIniOrEmpty("colliders.ini");
            int nc = 0;
            foreach (var s in col.Order) { if (!s.Name.StartsWith("COLLIDER_")) continue; nc++; ColAdd(other, "colliders.GROUND_ENABLE", s.Get("GROUND_ENABLE", "<absent>")); }
            ColAdd(other, "colliders.count", nc.ToString());
            // collider.kn5 extent (model space) - zone thresholds could be fractions of it
            if (car.ColliderPath != null && File.Exists(car.ColliderPath))
            {
                try
                {
                    var ck = Kn5File.Read(car.ColliderPath, Kn5ReadOptions.Full);
                    AcVec3 mn, mx;
                    if (VertexBounds(ck, out mn, out mx))
                        ColAdd(other, "collider.kn5.size(x)", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2}", mx.X - mn.X));
                    if (VertexBounds(ck, out mn, out mx))
                        ColAdd(other, "collider.kn5.size(z)", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2}", mx.Z - mn.Z));
                }
                catch (Exception e) { odd.Add(car.Id + ": collider.kn5 " + e.Message); }
            }
            else odd.Add(car.Id + ": no collider.kn5");
        }
        Console.WriteLine("owned cars swept: " + cars + "; no damage.ini: " + noDmg.Count + " " + string.Join(",", noDmg));
        Console.WriteLine("== damage.ini section counts (cars having it; VISUAL_OBJECT_n = total objects)");
        foreach (var kv in secCount) Console.WriteLine("  " + kv.Key + " " + kv.Value);
        ColDump("damage.ini keys", dmg, 14);
        ColDump("other damage-related keys", other, 8);
        Console.WriteLine("== odd (" + odd.Count + ")");
        foreach (var l in odd) Console.WriteLine("  " + l);
        Console.WriteLine("== per car");
        foreach (var l in perCar) Console.WriteLine("  " + l);
        return 0;
    }

    static int Cmd_colraw(List<string> a)
    {
        var acd = AcdArchive.Open(CarPath(a[1]));
        Console.WriteLine("---- damage.ini\n" + acd.ReadText("damage.ini"));
        foreach (var f in new[] { "colliders.ini", "suspensions.ini", "engine.ini", "car.ini", "aero.ini" })
        {
            var t = acd.ReadText(f); if (t == null) continue;
            bool on = false;
            Console.WriteLine("---- " + f + " (damage/collider/pit lines)");
            foreach (var l in t.Split('\n'))
            {
                var tl = l.Trim();
                if (tl.StartsWith("[")) on = tl.IndexOf("DAMAGE", StringComparison.OrdinalIgnoreCase) >= 0 || tl.StartsWith("[COLLIDER") || tl.StartsWith("[PIT_STOP");
                if (on || tl.StartsWith("ZONE_")) Console.WriteLine("  " + tl);
            }
        }
        return 0;
    }

    static int Cmd_colstrings(List<string> a)
    {
        var d = File.ReadAllBytes(a[1]);
        var rx = new Regex(a[2], RegexOptions.IgnoreCase);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        for (int i = 0; i <= d.Length; i++)
        {
            byte b = i < d.Length ? d[i] : (byte)0;
            if (b >= 0x20 && b < 0x7f) sb.Append((char)b);
            else { if (sb.Length >= 4) set.Add(sb.ToString()); sb.Clear(); }
        }
        for (int par = 0; par < 2; par++)
        {
            sb.Clear();
            for (int i = par; i + 1 <= d.Length; i += 2)
            {
                bool ok = i + 1 < d.Length && d[i] >= 0x20 && d[i] < 0x7f && d[i + 1] == 0;
                if (ok) sb.Append((char)d[i]);
                else { if (sb.Length >= 4) set.Add("W:" + sb.ToString()); sb.Clear(); }
            }
        }
        foreach (var s in set) if (rx.IsMatch(s) && !s.StartsWith(".?AV") && s.IndexOf("lambda", StringComparison.Ordinal) < 0) Console.WriteLine(s);
        return 0;
    }
}

// colnear <file> <exact> [radiusBytes]: strings (ASCII + UTF-16LE) located within radius of each occurrence of <exact>, in file order
static partial class Program
{
    static int Cmd_colnear(List<string> a)
    {
        var d = File.ReadAllBytes(a[1]);
        int rad = a.Count > 3 ? int.Parse(a[3]) : 400;
        var hits = new List<int>();
        var w = Encoding.Unicode.GetBytes(a[2]); var s8 = Encoding.ASCII.GetBytes(a[2]);
        foreach (var pat in new[] { w, s8 })
            for (int i = 0; i + pat.Length <= d.Length; i++)
            {
                bool m = true; for (int j = 0; j < pat.Length; j++) if (d[i + j] != pat[j]) { m = false; break; }
                if (m) hits.Add(i);
            }
        foreach (int h in hits)
        {
            Console.WriteLine("-- @0x" + h.ToString("X"));
            int st = Math.Max(0, h - rad), en = Math.Min(d.Length, h + rad);
            var sb = new StringBuilder(); int sbStart = st;
            // UTF-16 aligned on h parity, then ASCII
            for (int par = 0; par < 2; par++)
            {
                int off = (h + par) & 1;
                sb.Clear();
                for (int i = st + ((st & 1) ^ off); i + 1 < en; i += 2)
                {
                    bool ok = d[i] >= 0x20 && d[i] < 0x7f && d[i + 1] == 0;
                    if (ok) { if (sb.Length == 0) sbStart = i; sb.Append((char)d[i]); }
                    else { if (sb.Length >= 3) Console.WriteLine("   W " + (sbStart - h) + " " + sb); sb.Clear(); }
                }
            }
            sb.Clear();
            for (int i = st; i < en; i++)
            {
                bool ok = d[i] >= 0x20 && d[i] < 0x7f;
                if (ok) { if (sb.Length == 0) sbStart = i; sb.Append((char)d[i]); }
                else { if (sb.Length >= 4) Console.WriteLine("   A " + (sbStart - h) + " " + sb); sb.Clear(); }
            }
        }
        return 0;
    }
}
