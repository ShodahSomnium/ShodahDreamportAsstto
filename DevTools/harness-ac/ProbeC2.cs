using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// Module C (car model) probes, part 2 (data-only; compiles in the shared harness).
//   driverpose [car]  the AC driver as C poses it (AcCarDriverModel: knh local matrices on the DRIVER:* nodes, the driver kn5's own
//                     FBX wrapper above them) vs the knh's own world positions (RIG_Hips / RIG_Head / RIG_HAND_L / RIG_HAND_R).
//   digitems [car]    digital_instruments.ini census for C's display code: types, COLOR x INTENSITY, textures (png / dds, where found).
static partial class Program
{
    static int Cmd_driverpose(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        string ddir = _install.DriverDir;
        int cars = 0, ok = 0, bad = 0, skipped = 0; float worst = 0f; string worstCar = "";
        var kn5Cache = new Dictionary<string, Kn5File>(StringComparer.OrdinalIgnoreCase);
        string[] probes = { "DRIVER:RIG_Hips", "DRIVER:RIG_Head", "DRIVER:RIG_HAND_L", "DRIVER:RIG_HAND_R" };
        var wrappers = new Dictionary<string, int>();
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            cars++;
            var acd = AcdArchive.Open(car.Dir);
            var d3 = acd != null ? acd.ReadIni("driver3d.ini") : null;
            string model = d3 != null ? d3.Get("MODEL", "NAME") : null;
            bool hasKnh = car.KnhPath != null && File.Exists(car.KnhPath);
            if (model == null || !hasKnh) { skipped++; Console.WriteLine(car.Id + ": skipped (model " + (model ?? "-") + ", knh " + hasKnh + ")"); continue; }
            model = model.Trim();
            Kn5File k;
            if (!kn5Cache.TryGetValue(model, out k))
            {
                string p = Path.Combine(ddir, model + ".kn5");
                k = File.Exists(p) ? Kn5File.Read(p, Kn5ReadOptions.HeaderOnly) : null;
                kn5Cache[model] = k;
            }
            if (k == null) { skipped++; Console.WriteLine(car.Id + ": driver kn5 " + model + " missing"); continue; }
            var knh = Knh.Read(car.KnhPath);
            float carWorst = 0f; int found = 0; var sb = new System.Text.StringBuilder();
            foreach (var name in probes)
            {
                var kn = knh.Find(name);
                Kn5Node dn = null;
                foreach (var n in k.Nodes) if (string.Equals(n.Name, name, StringComparison.Ordinal)) { dn = n; break; }
                if (kn == null || dn == null) { sb.Append(" " + name.Substring(7) + (kn == null ? "(no knh)" : "(no kn5)")); continue; }
                // posed world (row vectors: local * parent * ...): knh local on DRIVER:* nodes the knh has, the kn5's own local otherwise
                AcMat4 m = AcMat4.Identity;
                string chain = "";
                for (var q = dn; q != null; q = q.Parent)
                {
                    AcMat4 local = q.Local;
                    bool drv = q.Name != null && q.Name.StartsWith("DRIVER:", StringComparison.Ordinal);
                    if (drv) { var kq = knh.Find(q.Name); if (kq != null) local = kq.Local; }
                    else chain = q.Name + (chain.Length > 0 ? "/" + chain : "");
                    m = AcMat4.Mul(m, local);
                }
                if (name == "DRIVER:RIG_Hips") { int c; wrappers.TryGetValue(model + " above DRIVER: " + chain, out c); wrappers[model + " above DRIVER: " + chain] = c + 1; }
                var posed = m.Translation; var want = kn.World.Translation;
                float dx = posed.X - want.X, dy = posed.Y - want.Y, dz = posed.Z - want.Z;
                float e = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                found++;
                if (e > carWorst) carWorst = e;
                sb.Append(" " + name.Substring(11) + " " + F(e));
            }
            if (found == 4 && carWorst <= 0.01f) ok++; else bad++;
            if (carWorst > worst) { worst = carWorst; worstCar = car.Id; }
            Console.WriteLine(car.Id + " (" + model + "): err" + sb + (carWorst > 0.01f || found < 4 ? "  <-- CHECK" : ""));
        }
        foreach (var kv in wrappers) Console.WriteLine("  wrapper " + kv.Key + " x" + kv.Value);
        Console.WriteLine("driverpose: " + cars + " cars, " + ok + " within 1 cm (4/4 points), " + bad + " off or incomplete, " + skipped + " skipped; worst " + F(worst) + " m (" + worstCar + ")");
        return 0;
    }

    /// <summary>seats: ui_car.json class vs the knh driver hips x (C's passenger-seat rule: |x| > 0.2 m and class != race).</summary>
    static int Cmd_seats(List<string> a)
    {
        var content = AcContent.Scan(_install);
        int offset = 0, offsetRace = 0, offsetStreet = 0;
        foreach (var car in content.Cars)
        {
            if (!car.Owned) continue;
            if (car.KnhPath == null || !File.Exists(car.KnhPath)) { Console.WriteLine(car.Id + " class " + car.Class + " no knh"); continue; }
            var knh = Knh.Read(car.KnhPath);
            var h = knh.WorldPosition("DRIVER:RIG_Hips");
            float x = h != null ? h.Value.X : 0f;
            bool off = Math.Abs(x) > 0.2f;
            bool race = string.Equals((car.Class ?? "").Trim(), "race", StringComparison.OrdinalIgnoreCase);
            if (off) { offset++; if (race) offsetRace++; else offsetStreet++; }
            Console.WriteLine(car.Id + " class " + car.Class + " hips.x " + F(x) + (off ? (race ? "  offset RACE -> no passenger" : "  offset -> passenger") : ""));
        }
        Console.WriteLine("seats: " + offset + " offset drivers, " + offsetRace + " of them class race (no passenger seat), " + offsetStreet + " with a passenger seat");
        return 0;
    }

    static int Cmd_digitems(List<string> a)
    {
        string only = a.Count > 1 ? a[1] : null;
        var content = AcContent.Scan(_install);
        var types = new SortedDictionary<string, int>();
        int dark = 0, items = 0, texItems = 0, dds = 0, png = 0, texMissing = 0;
        foreach (var car in content.Cars)
        {
            if (!car.Owned || (only != null && car.Id != only)) continue;
            var acd = AcdArchive.Open(car.Dir);
            var ini = acd != null ? acd.ReadIni("digital_instruments.ini") : null;
            if (ini == null) continue;
            Kn5File l0 = null;
            foreach (var s in ini.Order)
            {
                if (!s.Name.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase)) continue;
                items++;
                string type = (s.Get("TYPE") ?? "?").Trim().ToUpperInvariant();
                int c; types.TryGetValue(type, out c); types[type] = c + 1;
                var col = s.GetFloats("COLOR");
                float inten = s.GetFloat("INTENSITY", 1f);
                if (col != null && col.Length >= 3)
                {
                    float lum = Math.Max(col[0], Math.Max(col[1], col[2])) / 255f * inten;
                    if (lum < 0.2f) dark++;
                }
                foreach (var key in new[] { "TEXTURE_BASE", "TEXTURE_TOP" })
                {
                    string tn = s.Get(key);
                    if (string.IsNullOrEmpty(tn)) continue;
                    tn = tn.Trim();
                    texItems++;
                    if (tn.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) dds++; else png++;
                    if (l0 == null) l0 = Kn5File.Read(car.Kn5Path, Kn5ReadOptions.HeaderOnly);
                    bool inKn5 = l0.Texture(tn) != null, inDir = File.Exists(Path.Combine(car.Dir, "texture", tn)) || File.Exists(Path.Combine(car.Dir, tn));
                    if (!inKn5 && !inDir) texMissing++;
                    Console.WriteLine(car.Id + " [" + s.Name + "] " + type + " " + key + "=" + tn + (inKn5 ? " (kn5)" : inDir ? " (car texture/ folder)" : " MISSING"));
                }
            }
        }
        foreach (var kv in types) Console.WriteLine("  type " + kv.Key + " x" + kv.Value);
        Console.WriteLine("digitems: " + items + " items, " + dark + " dark (max(COLOR)/255 x INTENSITY < 0.2), textures " + texItems + " (" + dds + " dds, " + png + " png, " + texMissing + " missing)");
        return 0;
    }
}
