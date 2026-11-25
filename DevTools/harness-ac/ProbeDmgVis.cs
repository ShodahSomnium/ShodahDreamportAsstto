using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumAssetto;

// Damage-visual research probes (dmg_visual.md). Commands:
//   dmgstr <binary> <regex>        printable ASCII / UTF-16 strings (>= 4 chars) of a file matching regex (key names only)
//   dmgrdef <shader file>          names-only reflection (RDEF) of every DXBC blob in an AC .shader container: cbuffers, variables
//                                  (offset, size, type, USED flag), bound resources. No disassembly.
//   dmgsweep [car]                 every owned car: damage.ini summary, damage_dirt / ksBrokenGlass materials (props A..D, slots),
//                                  DAMAGE_GLASS_* nodes, VISUAL_OBJECT nodes (exist? pivot, LODs), mask channel coverage
static partial class Program
{
    static string DvF(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    static int Cmd_dmgstr(List<string> a)
    {
        var d = File.ReadAllBytes(a[1]);
        var re = new Regex(a[2], RegexOptions.IgnoreCase);
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        for (int i = 0; i < d.Length; i++)
        {
            byte c = d[i];
            if (c >= 0x20 && c < 0x7f) sb.Append((char)c);
            else { if (sb.Length >= 4) { var s = sb.ToString(); if (re.IsMatch(s)) seen.Add(s); } sb.Clear(); }
        }
        sb.Clear();
        for (int o = 0; o < 2; o++)
            for (int i = o; i + 1 < d.Length; i += 2)
            {
                int c = d[i] | (d[i + 1] << 8);
                if (c >= 0x20 && c < 0x7f) sb.Append((char)c);
                else { if (sb.Length >= 4) { var s = sb.ToString(); if (re.IsMatch(s)) seen.Add("(u16) " + s); } sb.Clear(); }
            }
        foreach (var s in seen) Console.WriteLine(s);
        Console.WriteLine("-- " + seen.Count + " strings");
        return 0;
    }

    static string DvStr(byte[] d, int off) { int e = off; while (e < d.Length && d[e] != 0) e++; return Encoding.ASCII.GetString(d, off, e - off); }
    static uint DvU32(byte[] d, int o) { return BitConverter.ToUInt32(d, o); }
    static ushort DvU16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }

    static int Cmd_dmgrdef(List<string> a)
    {
        var d = File.ReadAllBytes(a[1]);
        int blob = 0;
        for (int i = 0; i + 32 < d.Length; i++)
        {
            if (d[i] != 'D' || d[i + 1] != 'X' || d[i + 2] != 'B' || d[i + 3] != 'C') continue;
            uint total = DvU32(d, i + 24), chunks = DvU32(d, i + 28);
            Console.WriteLine("== DXBC #" + blob++ + " at " + i + " size " + total + " chunks " + chunks);
            for (int c = 0; c < chunks; c++)
            {
                int co = i + (int)DvU32(d, i + 32 + 4 * c);
                string fourcc = Encoding.ASCII.GetString(d, co, 4);
                int data = co + 8;
                if (fourcc == "RDEF") DvRdef(d, data);
                else if (fourcc == "ISGN" || fourcc == "OSGN") DvSig(fourcc, d, data);
            }
            i += (int)total - 1;
        }
        return 0;
    }

    static void DvSig(string tag, byte[] d, int b)
    {
        uint n = DvU32(d, b);
        var parts = new List<string>();
        for (int k = 0; k < n; k++)
        {
            int e = b + 8 + k * 24;
            parts.Add(DvStr(d, b + (int)DvU32(d, e)) + DvU32(d, e + 4) + "(r" + DvU32(d, e + 16) + " m" + (d[e + 20]).ToString("X") + ")");
        }
        Console.WriteLine("  " + tag + ": " + string.Join(" ", parts));
    }

    static void DvRdef(byte[] d, int b)
    {
        uint cbCount = DvU32(d, b), cbOff = DvU32(d, b + 4), bindCount = DvU32(d, b + 8), bindOff = DvU32(d, b + 12);
        byte minor = d[b + 16], major = d[b + 17]; ushort prog = DvU16(d, b + 18);
        Console.WriteLine("  RDEF " + (prog == 0xFFFF ? "ps" : prog == 0xFFFE ? "vs" : prog.ToString("X")) + "_" + major + "_" + minor + " creator '" + DvStr(d, b + (int)DvU32(d, b + 24)) + "'");
        int varStride = major >= 5 ? 40 : 24;
        for (int k = 0; k < bindCount; k++)
        {
            int e = b + (int)bindOff + k * 32;
            Console.WriteLine("   bind " + DvStr(d, b + (int)DvU32(d, e)) + " type " + DvU32(d, e + 4) + " dim " + DvU32(d, e + 12) + " slot " + DvU32(d, e + 20) + " count " + DvU32(d, e + 24));
        }
        for (int k = 0; k < cbCount; k++)
        {
            int e = b + (int)cbOff + k * 24;
            uint vc = DvU32(d, e + 4), vo = DvU32(d, e + 8), size = DvU32(d, e + 12);
            Console.WriteLine("   cbuffer " + DvStr(d, b + (int)DvU32(d, e)) + " size " + size + " vars " + vc);
            for (int v = 0; v < vc; v++)
            {
                int ve = b + (int)vo + v * varStride;
                string name = DvStr(d, b + (int)DvU32(d, ve));
                uint start = DvU32(d, ve + 4), vsize = DvU32(d, ve + 8), flags = DvU32(d, ve + 12), typeOff = DvU32(d, ve + 16);
                int t = b + (int)typeOff;
                ushort cls = DvU16(d, t), typ = DvU16(d, t + 2), rows = DvU16(d, t + 4), cols = DvU16(d, t + 6), elems = DvU16(d, t + 8);
                string ty = (typ == 3 ? "float" : typ == 2 ? "int" : typ == 1 ? "bool" : "t" + typ) + (cls == 3 || cls == 2 ? rows + "x" + cols : cols > 1 ? cols.ToString() : "") + (elems > 0 ? "[" + elems + "]" : "");
                Console.WriteLine("     +" + start + " " + ty + " " + name + " (" + vsize + " B)" + ((flags & 2) != 0 ? " USED" : ""));
            }
        }
    }

    static int Cmd_dmgsweep(List<string> a)
    {
        var content = AcContent.Scan(_install);
        string only = a.Count > 1 ? a[1] : null;
        int cars = 0, withDirtMat = 0, withGlassNodes = 0, withBroken = 0, voTotal = 0, voFound = 0, voLod1 = 0;
        var zoneVals = new Dictionary<string, int>(); var dirtVals = new Dictionary<string, int>(); var glassVals = new Dictionary<string, int>();
        var maskNames = new Dictionary<string, int>(); var voNames = new Dictionary<string, int>(); var sections = new Dictionary<string, int>();
        var dmgTexNames = new Dictionary<string, int>(); var dustNames = new Dictionary<string, int>(); var nmNames = new Dictionary<string, int>();
        var oscKeys = new Dictionary<string, int>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (only != null && car.Id != only) continue;
            cars++;
            AcIni ini = null;
            try { ini = AcdArchive.Open(car.Dir).ReadIni("damage.ini"); } catch (Exception e) { Console.WriteLine(car.Id + ": acd " + e.Message); }
            var line = new StringBuilder(car.Id + ":");
            if (ini != null)
            {
                foreach (var s in ini.Order) { string key = Regex.Replace(s.Name, @"_\d+$", "_n"); DvInc(sections, key); }
                var sc = ini["SCRATCHES"]; if (sc != null) line.Append(" scratches " + sc.Get("MIN_SPEED") + ".." + sc.Get("MAX_SPEED"));
                line.Append(" osc " + ini.Get("OSCILLATIONS", "ENABLED", "-") + " init " + ini.Get("DAMAGE", "INITIAL_LEVEL", "-"));
                foreach (var s in ini.Order) if (s.Name.StartsWith("DAMAGE_GLASS", StringComparison.Ordinal)) line.Append(" " + s.Name.Substring(13) + "=" + s.Get("MIN_SPEED") + "/" + s.Get("FULL_SPEED"));
                foreach (var s in ini.Order) foreach (var k in s.Keys) if (s.Name.StartsWith("VISUAL_OBJECT", StringComparison.Ordinal)) DvInc(oscKeys, k);
            }
            // LOD kn5s
            var lodNodes = new List<HashSet<string>>();
            Kn5File k0 = null;
            for (int li = 0; li < car.Kn5Files.Count; li++)
            {
                try
                {
                    var k = Kn5File.Read(car.Kn5Files[li], li == 0 ? new Kn5ReadOptions { Textures = true, Meshes = false } : Kn5ReadOptions.HeaderOnly);
                    var set = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var n in k.Nodes) if (n.Name != null) set.Add(n.Name);
                    lodNodes.Add(set);
                    if (li == 0) k0 = k;
                }
                catch (Exception e) { Console.WriteLine(car.Id + ": kn5 " + Path.GetFileName(car.Kn5Files[li]) + " " + e.Message); lodNodes.Add(new HashSet<string>()); }
            }
            if (k0 == null) { Console.WriteLine(line); continue; }
            // materials
            bool dirtMat = false, broken = false;
            foreach (var m in k0.Materials)
            {
                if (m.Shader == "ksPerPixelMultiMap_damage_dirt" || m.Shader == "ksPerPixelMultiMap_damage")
                {
                    dirtMat = true;
                    var z = m.Property("damageZones"); var dt = m.Property("dirt"); var dm = m.Property("damage");
                    string zs = z == null ? "none" : "A" + DvF(z.A) + " D(" + DvF(z.D[0]) + "," + DvF(z.D[1]) + "," + DvF(z.D[2]) + "," + DvF(z.D[3]) + ")";
                    DvInc(zoneVals, zs); DvInc(dirtVals, dt == null ? "none" : "A" + DvF(dt.A) + " D" + DvF(dt.D[0]));
                    DvInc(maskNames, m.Texture("txDamageMask") ?? "-"); DvInc(dmgTexNames, m.Texture("txDamage") ?? "-"); DvInc(dustNames, m.Texture("txDust") ?? "-"); DvInc(nmNames, m.Texture("txNormal") ?? "-");
                    if (only != null)
                    {
                        Console.WriteLine("  mat " + m.Name + " [" + m.Shader + "] blend " + m.BlendMode);
                        foreach (var p in m.Properties) Console.WriteLine("     " + p.Name + " A=" + DvF(p.A) + " B=(" + DvF(p.B[0]) + "," + DvF(p.B[1]) + ") C=(" + DvF(p.C[0]) + "," + DvF(p.C[1]) + "," + DvF(p.C[2]) + ") D=(" + DvF(p.D[0]) + "," + DvF(p.D[1]) + "," + DvF(p.D[2]) + "," + DvF(p.D[3]) + ")");
                        foreach (var s in m.Samplers) Console.WriteLine("     tex " + s.Name + "@" + s.Slot + " = " + s.Texture);
                    }
                }
                if (m.Shader == "ksBrokenGlass")
                {
                    broken = true;
                    var g = m.Property("glassDamage");
                    DvInc(glassVals, (g == null ? "none" : "A" + DvF(g.A)) + " blend " + m.BlendMode + " depth " + m.DepthMode + " diff " + m.Texture("txDiffuse") + " nm " + m.Texture("txNormal"));
                }
            }
            if (dirtMat) withDirtMat++;
            if (broken) withBroken++;
            // mask channel coverage (first damage material's mask)
            string maskTex = null;
            foreach (var m in k0.Materials) if (m.Shader == "ksPerPixelMultiMap_damage_dirt" && m.Texture("txDamageMask") != null) { maskTex = m.Texture("txDamageMask"); break; }
            if (maskTex != null)
            {
                var t = k0.Texture(maskTex);
                if (t != null && t.Bytes != null && t.Info.IsDds)
                {
                    int w, h; var px = AcDds.DecodeRgba32(t.Bytes, t.Info, 0, out w, out h);
                    if (px != null)
                    {
                        double[] sum = new double[4]; int[] cov = new int[4]; int n = w * h;
                        for (int p = 0; p < n; p++) for (int c = 0; c < 4; c++) { int v = px[p * 4 + c]; sum[c] += v; if (v > 25) cov[c]++; }
                        line.Append(" mask " + maskTex + " " + w + "x" + h + " " + t.Info.Format + " mean/cov R " + (sum[0] / n / 255).ToString("0.00") + "/" + (cov[0] * 100 / n) + "% G " + (sum[1] / n / 255).ToString("0.00") + "/" + (cov[1] * 100 / n) + "% B " + (sum[2] / n / 255).ToString("0.00") + "/" + (cov[2] * 100 / n) + "% A " + (sum[3] / n / 255).ToString("0.00") + "/" + (cov[3] * 100 / n) + "%");
                    }
                }
                else line.Append(" mask " + maskTex + " (not embedded)");
            }
            // glass nodes
            int gN = 0, gInactive = 0, gUnderVo = 0; var gNames = new List<string>();
            foreach (var n in k0.Nodes)
            {
                if (n.Name == null || !n.Name.StartsWith("DAMAGE_GLASS", StringComparison.Ordinal)) continue;
                gN++; if (!n.Active) gInactive++;
                gNames.Add(n.Name + (n.Parent != null && n.Parent.Parent != null ? "<" + n.Parent.Name : ""));
                for (var p = n.Parent; p != null; p = p.Parent) if (p.Name == "FRONT_BUMPER" || p.Name == "REAR_BUMPER" || p.Name == "MOTORHOOD" || p.Name == "REAR_HOOD" || p.Name == "FRONT_WING" || p.Name == "REAR_WING") { gUnderVo++; break; }
            }
            if (gN > 0) withGlassNodes++;
            line.Append(" glass nodes " + gN + " (inactive " + gInactive + ", under a panel " + gUnderVo + ")");
            if (only != null) Console.WriteLine("  glass: " + string.Join(" ", gNames));
            // visual objects
            if (ini != null)
                foreach (var s in ini.Order)
                {
                    if (!s.Name.StartsWith("VISUAL_OBJECT", StringComparison.Ordinal)) continue;
                    voTotal++;
                    string nm = s.Get("NAME", "");
                    DvInc(voNames, nm + " zone " + s.Get("DAMAGE_ZONE", "?"));
                    var node = k0.Nodes.FirstOrDefault(x => x.Name == nm);
                    bool inL1 = lodNodes.Count > 1 && lodNodes[1].Contains(nm);
                    if (node != null) voFound++;
                    if (inL1) voLod1++;
                    string lods = string.Join("", lodNodes.Select((h2, i2) => h2.Contains(nm) ? i2.ToString() : "."));
                    string piv = "";
                    if (node != null)
                    {
                        var wm = node.World; var o = wm.TransformPoint(new AcVec3(0, 0, 0));
                        int meshKids = 0; foreach (var c in node.Children) if (c.IsMesh) meshKids++; else foreach (var c2 in c.Children) if (c2.IsMesh) meshKids++;
                        piv = " pivot(" + DvF(o.X) + "," + DvF(o.Y) + "," + DvF(o.Z) + ") kids " + node.Children.Count + " meshes " + meshKids + " cls " + node.Class;
                    }
                    if (only != null || node == null)
                        Console.WriteLine("  VO " + s.Name + " " + nm + (node == null ? " NOT IN LOD0" : "") + " lods[" + lods + "]" + piv + " zone " + s.Get("DAMAGE_ZONE") + " rot " + s.Get("STATIC_ROTATION_AXIS") + "@" + s.Get("STATIC_ROTATION_ANGLE") + " spd " + s.Get("MIN_SPEED") + ".." + s.Get("FULL_SPEED") + " osc " + s.Get("OSCILLATION_AXIS") + " " + s.Get("OSCILLATION_MIN_ANGLE") + ".." + s.Get("OSCILLATION_MAX_ANGLE") + " multG " + s.Get("MULT_G") + " allowedG " + s.Get("ALLOWED_G"));
                }
            Console.WriteLine(line);
        }
        Console.WriteLine("== cars " + cars + ", with damage paint " + withDirtMat + ", with DAMAGE_GLASS nodes " + withGlassNodes + ", with ksBrokenGlass " + withBroken + "; VISUAL_OBJECT " + voTotal + " found in LOD0 " + voFound + ", in LOD1 " + voLod1);
        DvDump("damage.ini sections", sections); DvDump("VISUAL_OBJECT keys", oscKeys); DvDump("damageZones values", zoneVals); DvDump("dirt values", dirtVals); DvDump("glassDamage values", glassVals);
        DvDump("txDamageMask", maskNames); DvDump("txDamage", dmgTexNames); DvDump("txDust", dustNames); DvDump("txNormal (damage NM)", nmNames); DvDump("VISUAL_OBJECT names", voNames);
        return 0;
    }

    static void DvInc(Dictionary<string, int> d, string k) { int c; d.TryGetValue(k, out c); d[k] = c + 1; }
    static void DvDump(string title, Dictionary<string, int> d)
    {
        Console.WriteLine("-- " + title + ":");
        foreach (var kv in d.OrderByDescending(x => x.Value).Take(40)) Console.WriteLine("   " + kv.Value + " x " + kv.Key);
    }
}

static partial class Program
{
    // dmgch <kn5|dds file> <texture name|-> <out prefix>: writes a 2x2 atlas PNG of the R, G, B, A channels as grey (top-left R, top-right G, bottom-left B, bottom-right A)
    static int Cmd_dmgch(List<string> a)
    {
        byte[] bytes; AcDdsInfo info;
        if (a[1].EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) { bytes = File.ReadAllBytes(a[1]); info = AcDds.Parse(bytes, 0, bytes.Length); }
        else { var k = Kn5File.Read(a[1], new Kn5ReadOptions { Textures = true, Meshes = false }); var t = k.Texture(a[2]); bytes = t.Bytes; info = t.Info; }
        int w, h; var px = AcDds.DecodeRgba32(bytes, info, 0, out w, out h);
        Console.WriteLine(info + " " + w + "x" + h);
        int s = Math.Max(1, w / 256); int ow = w / s, oh = h / s;
        var outp = new byte[ow * 2 * oh * 2 * 4];
        for (int c = 0; c < 4; c++)
        {
            int ox = (c & 1) * ow, oy = (c >> 1) * oh;
            for (int y = 0; y < oh; y++) for (int x = 0; x < ow; x++)
            {
                byte v = px[((y * s) * w + x * s) * 4 + c];
                int o = ((oy + y) * ow * 2 + ox + x) * 4; outp[o] = outp[o + 1] = outp[o + 2] = v; outp[o + 3] = 255;
            }
        }
        Png.Write(a[3] + "_ch.png", ow * 2, oh * 2, outp);
        // channel stats
        for (int c = 0; c < 4; c++) { long sum = 0; int mn = 255, mx = 0; for (int p = 0; p < w * h; p++) { int v = px[p * 4 + c]; sum += v; if (v < mn) mn = v; if (v > mx) mx = v; } Console.WriteLine("RGBA"[c] + " mean " + (sum / (double)(w * h)).ToString("0.0") + " min " + mn + " max " + mx); }
        return 0;
    }
}

static partial class Program
{
    // dmguv <car>: (1) samples each damage material's txDamageMask at the uv0 of its vertices, averaged per body region (AC model space:
    // +X left, +Z front) - which channel is which side; (2) VISUAL_OBJECT mesh bounds relative to the node origin (pivot).
    static int Cmd_dmguv(List<string> a)
    {
        var content = AcContent.Scan(_install);
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (a.Count > 1 && car.Id != a[1])) continue;
            var k = Kn5File.Read(car.Kn5Files[0], new Kn5ReadOptions { Textures = true, Meshes = true });
            var masks = new Dictionary<string, (byte[] px, int w, int h)>();
            string[] reg = { "front", "rear", "left", "right", "roof" };
            var sum = new double[5, 4]; var cnt = new int[5];
            foreach (var n in k.Nodes)
            {
                if (n.Mesh == null || !n.Mesh.Loaded || n.Mesh.MaterialId < 0 || n.Mesh.MaterialId >= k.Materials.Count) continue;
                var m = k.Materials[n.Mesh.MaterialId];
                if (m.Shader != "ksPerPixelMultiMap_damage_dirt") continue;
                string mt = m.Texture("txDamageMask"); if (mt == null) continue;
                if (!masks.ContainsKey(mt)) { var t = k.Texture(mt); if (t == null || t.Bytes == null) continue; int w, h; var px = AcDds.DecodeRgba32(t.Bytes, t.Info, 0, out w, out h); if (px == null) continue; masks[mt] = (px, w, h); }
                var mk = masks[mt];
                var wm = n.Parent != null ? n.Parent.World : AcMat4.Identity;
                for (int i = 0; i < n.Mesh.VertexCount; i += 3)
                {
                    var p = wm.TransformPoint(n.Mesh.Position(i));
                    float u = n.Mesh.U(i), v = n.Mesh.V(i); u -= (float)Math.Floor(u); v -= (float)Math.Floor(v);
                    int x = Math.Min(mk.w - 1, (int)(u * mk.w)), y = Math.Min(mk.h - 1, (int)(v * mk.h));
                    int o = (y * mk.w + x) * 4;
                    int r = p.Y > 1.05f ? 4 : p.Z > 1.6f ? 0 : p.Z < -1.6f ? 1 : p.X > 0.75f ? 2 : p.X < -0.75f ? 3 : -1;
                    if (r < 0) continue;
                    cnt[r]++; for (int c = 0; c < 4; c++) sum[r, c] += mk.px[o + c] / 255.0;
                }
            }
            Console.WriteLine(car.Id + " mask at uv0 (v as stored, row 0 = top), mean R/G/B/A per region:");
            for (int r = 0; r < 5; r++) if (cnt[r] > 0) Console.WriteLine("   " + reg[r].PadRight(6) + " n " + cnt[r].ToString().PadLeft(6) + "  R " + (sum[r, 0] / cnt[r]).ToString("0.00") + " G " + (sum[r, 1] / cnt[r]).ToString("0.00") + " B " + (sum[r, 2] / cnt[r]).ToString("0.00") + " A " + (sum[r, 3] / cnt[r]).ToString("0.00"));
            // VO mesh bounds vs pivot
            AcIni ini = null; try { ini = AcdArchive.Open(car.Dir).ReadIni("damage.ini"); } catch { }
            if (ini != null)
                foreach (var s in ini.Order)
                {
                    if (!s.Name.StartsWith("VISUAL_OBJECT", StringComparison.Ordinal)) continue;
                    var node = k.Nodes.FirstOrDefault(x => x.Name == s.Get("NAME", ""));
                    if (node == null) continue;
                    var piv = node.World.TransformPoint(new AcVec3(0, 0, 0));
                    float x0 = 1e9f, y0 = 1e9f, z0 = 1e9f, x1 = -1e9f, y1 = -1e9f, z1 = -1e9f;
                    var stack = new Stack<Kn5Node>(); stack.Push(node);
                    while (stack.Count > 0)
                    {
                        var q = stack.Pop(); foreach (var c in q.Children) stack.Push(c);
                        if (q.Mesh == null || !q.Mesh.Loaded) continue;
                        var wm = q.Parent.World;
                        for (int i = 0; i < q.Mesh.VertexCount; i++) { var p = wm.TransformPoint(q.Mesh.Position(i)); x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
                    }
                    var lm = node.Local;
                    Console.WriteLine("   VO " + node.Name + " pivot(" + DvF(piv.X) + "," + DvF(piv.Y) + "," + DvF(piv.Z) + ") mesh rel x " + DvF(x0 - piv.X) + ".." + DvF(x1 - piv.X) + " y " + DvF(y0 - piv.Y) + ".." + DvF(y1 - piv.Y) + " z " + DvF(z0 - piv.Z) + ".." + DvF(z1 - piv.Z) + " world=" + node.World + " axis " + s.Get("STATIC_ROTATION_AXIS") + " @" + s.Get("STATIC_ROTATION_ANGLE"));
                }
            k.Release();
        }
        return 0;
    }
}
