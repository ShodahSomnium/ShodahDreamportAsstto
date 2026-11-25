// Critic probe for dmg_visual.md: glass-node visibility / naming / material use across all owned cars and LODs. Names only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumAssetto;

static partial class Program
{
    static int Cmd_dmgvcrit(List<string> a)
    {
        var content = AcContent.Scan(_install);
        int cars = 0, visibleGlassCars = 0, brokenNotNamed = 0, namedNotBroken = 0, noZoneTok = 0;
        string[] zones = { "FRONT", "REAR", "LEFT", "RIGHT", "CENTER" };
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (a.Count > 1 && car.Id != a[1]) continue;
            cars++;
            for (int li = 0; li < car.Kn5Files.Count; li++)
            {
                Kn5File k;
                try { k = Kn5File.Read(car.Kn5Files[li], new Kn5ReadOptions { Textures = false, Meshes = false }); } catch (Exception e) { Console.WriteLine(car.Id + " lod" + li + " " + e.Message); continue; }
                var broken = new HashSet<int>();
                for (int i = 0; i < k.Materials.Count; i++) if (k.Materials[i].Shader == "ksBrokenGlass") broken.Add(i);
                var msgs = new List<string>();
                foreach (var n in k.Nodes)
                {
                    if (n.Name == null) continue;
                    bool named = false; Kn5Node top = null;
                    for (var p = n; p != null; p = p.Parent) if (p.Name != null && p.Name.StartsWith("DAMAGE_GLASS", StringComparison.OrdinalIgnoreCase)) { named = true; top = p; }
                    bool hidden = false; for (var p = n; p != null; p = p.Parent) if (!p.Active) hidden = true;
                    if (n.IsMesh)
                    {
                        bool isB = broken.Contains(n.Mesh.MaterialId);
                        if (isB && !named) { brokenNotNamed++; msgs.Add("BROKEN-MAT-NOT-NAMED " + n.Name + (hidden ? " (hidden)" : " (VISIBLE)")); }
                        if (named && !isB) { namedNotBroken++; msgs.Add("NAMED-NOT-BROKEN " + n.Name + " mat " + k.Materials[n.Mesh.MaterialId].Shader + (hidden ? " (hidden)" : " (VISIBLE)")); }
                        if (named && !hidden) msgs.Add("VISIBLE-GLASS " + n.Name);
                    }
                    if (named && top == n)
                    {
                        string tok = n.Name.Length > 13 ? n.Name.Substring(13) : "";
                        if (!zones.Any(z => tok.StartsWith(z + "_", StringComparison.Ordinal) || tok == z)) { noZoneTok++; msgs.Add("TOP-NO-ZONE " + n.Name); }
                    }
                }
                if (msgs.Any(m => m.StartsWith("VISIBLE-GLASS"))) { if (li == 0) visibleGlassCars++; }
                foreach (var g in msgs.GroupBy(m => m.Split(' ')[0]))
                    Console.WriteLine(car.Id + " lod" + li + " " + g.Key + " x" + g.Count() + ": " + string.Join(" | ", g.Take(6).Select(m => m.Substring(m.IndexOf(' ') + 1))));
            }
        }
        Console.WriteLine("== cars " + cars + " visible glass at LOD0 " + visibleGlassCars + " brokenMatNotNamed " + brokenNotNamed + " namedNotBroken " + namedNotBroken + " topNoZone " + noZoneTok);
        return 0;
    }
}

static partial class Program
{
    // counts damage shader materials per shader (LOD0) + lists cars using the old scalar ksPerPixelMultiMap_damage
    static int Cmd_dmgvshaders(List<string> a)
    {
        var content = AcContent.Scan(_install);
        var cnt = new Dictionary<string, int>(); var oldCars = new List<string>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned || car.Kn5Files.Count == 0) continue;
            var k = Kn5File.Read(car.Kn5Files[0], new Kn5ReadOptions { Textures = false, Meshes = false });
            foreach (var m in k.Materials)
            {
                if (m.Shader == null || m.Shader.IndexOf("damage", StringComparison.OrdinalIgnoreCase) < 0) continue;
                cnt[m.Shader] = cnt.TryGetValue(m.Shader, out var c) ? c + 1 : 1;
                if (m.Shader == "ksPerPixelMultiMap_damage") oldCars.Add(car.Id + ":" + m.Name + " damage=" + m.Float("damage", -1) + " nm=" + m.Texture("txNormal") + " txDamage=" + m.Texture("txDamage"));
            }
        }
        foreach (var kv in cnt) Console.WriteLine(kv.Value + " x " + kv.Key);
        foreach (var s in oldCars) Console.WriteLine("  " + s);
        return 0;
    }
}
