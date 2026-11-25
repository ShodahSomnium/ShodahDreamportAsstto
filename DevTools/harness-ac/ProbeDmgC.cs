#if AC_CARDATA
using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// Module C damage probe (iteration 2, DAMAGE_DESIGN 10). Needs C's Unity-free Game/Cars/AcCarData.cs, so it compiles only in C's private
// harness (DevTools/agents/C/harness, AC_CARDATA).
//   dmgvisres [car]  per owned car and LOD file: damage.ini parse (AcCarDamageDef), VISUAL_OBJECT resolution (exact, then case-insensitive
//                    = AcCarPartSet.Find), glass zone groups by TOP-MOST ancestor (AcCarDamageDef.GlassZoneOfName - the same rule the game
//                    code uses), zone nodes the kn5 builds ACTIVE (G0), stray ksBrokenGlass meshes outside every zone, token-less
//                    DAMAGE_GLASS_ names, txDamageMask decode for every damage_dirt material (format / size / channel means), and the
//                    weights (glass g, panel f, scratch, dent) at 30 / 60 / 150 km/h for one car.
static partial class Program
{
    static int Cmd_dmgvisres(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        int cars = 0, withIni = 0, panelsTotal = 0, osc0 = 0, scr = 0, init20 = 0, glassAll5 = 0, problems = 0;
        var found = new int[4]; var lodFiles = new int[4];
        int strayTotal = 0, activeZoneTotal = 0, looseTotal = 0, masks = 0, maskOk = 0, maskMissing = 0, paintMats = 0;
        var zoneNodes = new int[5];
        var formats = new Dictionary<string, int>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            cars++;
            var v = AcCarVisual.Load(car, false, false);
            var d = v.Damage;
            foreach (var p in v.Problems) if (p.StartsWith("damage.ini", StringComparison.Ordinal)) { problems++; Console.WriteLine("  " + car.Id + " problem: " + p); }
            if (d.Present) withIni++;
            if (!d.Oscillations) osc0++;
            if (d.HasScratches && d.ScratchMin == 0f && d.ScratchMax == 20f) scr++;
            if (d.InitialLevel == 20f) init20++;
            bool all5 = true; for (int z = 0; z < 5; z++) if (!d.HasGlass[z]) all5 = false;
            if (all5) glassAll5++;
            panelsTotal += d.Panels.Count;
            var line = new System.Text.StringBuilder();
            line.Append(car.Id).Append(": panels ").Append(d.Panels.Count);
            foreach (var lod in v.Lods)
            {
                if (lod.Path == null || lod.Index > 3) continue;
                Kn5File k;
                try { k = Kn5File.Read(lod.Path, Kn5ReadOptions.HeaderOnly); }
                catch (Exception e) { line.Append(" | LOD_" + lod.Index + " read failed " + e.Message); continue; }
                lodFiles[lod.Index]++;
                var exact = new HashSet<string>(StringComparer.Ordinal); var ci = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in k.Nodes) { exact.Add(n.Name); ci.Add(n.Name); }
                int f = 0; var miss = new List<string>();
                foreach (var p in d.Panels) { if (exact.Contains(p.Name) || ci.Contains(p.Name)) f++; else miss.Add(p.Name); }
                found[lod.Index] += f;
                // glass
                var zc = new int[5]; int stray = 0, active = 0; var loose = new List<string>();
                foreach (var n in k.Nodes)
                {
                    int z = AcCarDamageDef.GlassZoneOfName(n.Name);
                    bool zoneAncestor = false, glassAncestor = false;
                    for (var pp = n.Parent; pp != null; pp = pp.Parent) { if (AcCarDamageDef.GlassZoneOfName(pp.Name) >= 0) zoneAncestor = true; if (AcCarDamageDef.IsGlassName(pp.Name)) glassAncestor = true; }
                    if (z >= 0 && !zoneAncestor) { zc[z]++; if (Active(n)) active++; }
                    else if (z < 0 && AcCarDamageDef.IsGlassName(n.Name) && !glassAncestor) loose.Add(n.Name + (n.Active ? "(active)" : ""));
                    if (n.Mesh != null)
                    {
                        var m = k.Material(n.Mesh.MaterialId);
                        if (m != null && m.Shader == "ksBrokenGlass" && z < 0 && !zoneAncestor) { stray++; if (lod.Index == 0 || only != null) Console.WriteLine("  " + car.Id + " LOD_" + lod.Index + " stray ksBrokenGlass mesh " + n.Name + (Active(n) ? " (active)" : " (inactive)")); }
                    }
                }
                for (int z = 0; z < 5; z++) if (lod.Index == 0) zoneNodes[z] += zc[z];
                strayTotal += stray; activeZoneTotal += active; looseTotal += loose.Count;
                line.Append(" | LOD_" + lod.Index + " panels " + f + (miss.Count > 0 ? " (missing " + string.Join(",", miss.ToArray()) + ")" : "")
                    + " glass " + zc[0] + "/" + zc[1] + "/" + zc[2] + "/" + zc[3] + "/" + zc[4] + (active > 0 ? " ACTIVE " + active : "") + (stray > 0 ? " stray " + stray : "")
                    + (loose.Count > 0 ? " loose " + string.Join(",", loose.ToArray()) : ""));
                // masks (LOD_0 only: LOD_1..3 carry no textures)
                if (lod.Index != 0) continue;
                foreach (var m in k.Materials)
                {
                    if (m == null || m.Shader == null || !m.Shader.StartsWith("ksPerPixelMultiMap_damage", StringComparison.Ordinal)) continue;
                    paintMats++;
                    string tn = m.Texture("txDamageMask");
                    if (string.IsNullOrEmpty(tn)) continue;
                    masks++;
                    var t = k.Texture(tn);
                    if (t == null) { maskMissing++; Console.WriteLine("  " + car.Id + " mask " + tn + " not embedded (black = no zones)"); continue; }
                    try
                    {
                        var bytes = k.ReadTextureBytes(t);
                        var info = AcDds.Parse(bytes, 0, bytes.Length, bytes.Length);
                        int w, h;
                        var rgba = AcDds.DecodeRgba32(bytes, info, 0, out w, out h);
                        if (rgba == null || w <= 0) { Console.WriteLine("  " + car.Id + " mask " + tn + " decode failed (" + info.Format + ")"); continue; }
                        maskOk++;
                        string key = info.Format + " " + w + "x" + h;
                        int c; formats.TryGetValue(key, out c); formats[key] = c + 1;
                        if (only != null)
                        {
                            double r = 0, g = 0, b = 0, al = 0; int px = w * h;
                            for (int i = 0; i < px; i++) { r += rgba[i * 4]; g += rgba[i * 4 + 1]; b += rgba[i * 4 + 2]; al += rgba[i * 4 + 3]; }
                            Console.WriteLine("  mask " + tn + " " + key + " mean R(left) " + (r / px).ToString("0") + " G(rear) " + (g / px).ToString("0") + " B(right) " + (b / px).ToString("0") + " A(front) " + (al / px).ToString("0"));
                        }
                    }
                    catch (Exception e) { Console.WriteLine("  " + car.Id + " mask " + tn + " failed: " + e.Message); }
                }
            }
            if (only != null || cars <= 3) Console.WriteLine(line.ToString());
            else if (line.ToString().IndexOf("missing", StringComparison.Ordinal) >= 0 || line.ToString().IndexOf("ACTIVE", StringComparison.Ordinal) >= 0 || line.ToString().IndexOf("stray", StringComparison.Ordinal) >= 0 || line.ToString().IndexOf("loose", StringComparison.Ordinal) >= 0)
                Console.WriteLine(line.ToString());
            if (only != null) Weights(d);
        }
        Console.WriteLine("cars " + cars + ", damage.ini " + withIni + ", parse problems " + problems + ", SCRATCHES 0..20 on " + scr + ", INITIAL_LEVEL 20 on " + init20 + ", OSCILLATIONS off on " + osc0 + ", all 5 glass sections on " + glassAll5);
        Console.WriteLine("VISUAL_OBJECTs " + panelsTotal + ": found LOD_0 " + found[0] + " (" + lodFiles[0] + " files), LOD_1 " + found[1] + " (" + lodFiles[1] + "), LOD_2 " + found[2] + ", LOD_3 " + found[3]);
        Console.WriteLine("glass zone groups (top-most, LOD_0) F/R/L/R/C " + zoneNodes[0] + "/" + zoneNodes[1] + "/" + zoneNodes[2] + "/" + zoneNodes[3] + "/" + zoneNodes[4] + "; zone groups built ACTIVE (all LODs) " + activeZoneTotal + "; stray ksBrokenGlass meshes (all LODs) " + strayTotal + "; token-less DAMAGE_GLASS_ names " + looseTotal);
        var fl = new List<string>(); foreach (var kv in formats) fl.Add(kv.Key + " x" + kv.Value);
        Console.WriteLine("damage paint materials (LOD_0) " + paintMats + ", masks " + masks + ": decoded " + maskOk + ", not embedded " + maskMissing + "; formats " + string.Join(", ", fl.ToArray()));
        return 0;
    }

    static bool Active(Kn5Node n) { for (var p = n; p != null; p = p.Parent) if (!p.Active) return false; return true; }

    static void Weights(AcCarDamageDef d)
    {
        float[] kmh = { 10f, 30f, 60f, 150f, 255f };
        foreach (float z in kmh)
        {
            var s = new System.Text.StringBuilder();
            s.Append("  at " + z + " km/h: scratch " + d.Scratch01(z).ToString("0.00") + " dent " + d.Dent01(z, 60f).ToString("0.00") + " glass g");
            for (int i = 0; i < 5; i++) s.Append(" " + AcCarDamageDef.ZoneNames[i][0] + d.Glass01(i, z).ToString("0.00") + (d.Glass01(i, z) >= 0.65f ? "*" : ""));
            s.Append(" panels");
            foreach (var p in d.Panels) s.Append(" " + p.Name + "(" + AcCarDamageDef.ZoneNames[p.Zone] + ") f " + p.Factor(z).ToString("0.00") + " = " + (p.Factor(z) * p.StaticAngle).ToString("0.00") + " deg");
            Console.WriteLine(s.ToString());
        }
    }
}
#endif
