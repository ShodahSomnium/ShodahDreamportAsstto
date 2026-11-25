using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumAssetto;

// W damage probe (iteration 2, DAMAGE_DESIGN 2.1 / 2.2): AcShading.Of(m, damageSlots) over every owned car's LOD_0..1 kn5.
//   wdmgslots [car]: damage paint / broken glass census, the slots read with damage on vs off, every damage slot resolved to an
//   embedded texture (or the LOD_0 file for LOD_1), and slots with damage OFF identical to iteration 1's rule except ksBrokenGlass.
static partial class Program
{
    static int Cmd_wdmgslots(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        int cars = 0, paint = 0, scalar = 0, glass = 0, extraSlots = 0, missing = 0, glassNm = 0, offDiff = 0, files = 0;
        var missingList = new List<string>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            cars++;
            Kn5File l0 = null;
            foreach (var path in car.Kn5Files)
            {
                string fn = System.IO.Path.GetFileName(path).ToLowerInvariant();
                bool isL0 = path == car.Kn5Path;
                if (!isL0 && fn.IndexOf("_lod_b", StringComparison.Ordinal) < 0 && fn.IndexOf("lod_1", StringComparison.Ordinal) < 0) continue;
                Kn5File k;
                try { k = Kn5File.Read(path, Kn5ReadOptions.HeaderOnly); } catch (Exception) { continue; }
                if (isL0) l0 = k;
                files++;
                foreach (var m in k.Materials)
                {
                    if (m == null) continue;
                    var on = AcShading.Of(m, true); var off = AcShading.Of(m, false);
                    // iteration-1 rule (before this pass) for the OFF slots
                    bool nm1 = AcShading.HasSlot(m, "txNormal") && m.Float("nmObjectSpace", 0f) < 0.5f;
                    if (m.Shader != "ksBrokenGlass" && off.Slots.Contains("txNormal") != nm1) offDiff++;
                    if (off.DamagePaint || off.Slots.Contains("txDamage") || off.Slots.Contains("txDamageMask")) offDiff++;
                    if (on.BrokenGlass) { glass++; if (on.NormalMap && !on.Slots.Contains("txNormal")) glassNm++; }
                    if (!on.DamagePaint) continue;
                    paint++; if (on.DamageScalar) scalar++;
                    extraSlots += on.Slots.Count - off.Slots.Count;
                    foreach (var slot in new[] { "txDamage", "txDamageMask" })
                    {
                        if (!on.Slots.Contains(slot)) continue;
                        string tn = m.Texture(slot);
                        bool ok = k.Texture(tn) != null || (l0 != null && l0.Texture(tn) != null);
                        if (!ok) { missing++; missingList.Add(car.Id + " " + fn + " " + m.Name + " " + slot + "=" + tn); }
                    }
                }
            }
        }
        Console.WriteLine("cars " + cars + ", kn5 files (LOD_0 + LOD_1) " + files + ": damage paint materials " + paint + " (scalar ksPerPixelMultiMap_damage " + scalar + "), extra slots read with damage on " + extraSlots
            + ", unresolved " + missing + "; ksBrokenGlass materials " + glass + " (normal map from the install with damage on: " + glassNm + "); OFF slots differing from iteration 1 (non-glass): " + offDiff);
        foreach (var s in missingList) Console.WriteLine("  unresolved " + s);
        return 0;
    }
}
