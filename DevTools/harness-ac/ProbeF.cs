using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SomniumSpace.Worlds.SomniumAssetto;

// Module F (Front / tablet) probes: what the tablet pages read from the install, run against the real files.
//   fui            locale words, skin.ini colours, launcher accent, assists.ini, fonts, art files, the catalog numbers the pages use
//   fsetup <car>   the car's setup.ini rows with labels and defaults (Garage page)
//   ffmt           the text formats (lap time, gap, length) with known answers
static partial class Program
{
    static int Cmd_fui(List<string> a)
    {
        var sw = Stopwatch.StartNew();
        int words = AcLocale.Load(_install);
        Console.WriteLine("locale: " + words + " keys in " + sw.ElapsedMilliseconds + " ms; PRACTICE=" + AcLocale.Session(1) + " QUALIFY=" + AcLocale.Session(2) + " RACE=" + AcLocale.Session(3) +
                          " HOTLAP=" + AcLocale.Session(4) + " BEST=" + AcLocale.S("BEST", "?") + " LAST=" + AcLocale.S("LAST", "?") + " GAP=" + AcLocale.S("GAP", "?") +
                          " Laps=" + AcLocale.S("Laps", "?") + " | launcher ABS=" + AcLocale.L("ABS", "?") + " TC=" + AcLocale.L("TRACTION_CONTROL", "?") + " AUTO=" + AcLocale.L("AUTOMATIC_GEARS", "?") +
                          " | setup FRONT_BIAS=" + AcLocale.Setup("FRONT_BIAS", "?") + " FUEL=" + AcLocale.Setup("FUEL", "?") + " PRESSURE_LF=" + AcLocale.Setup("PRESSURE_LF", "?"));
        var skin = AcGuiSkin.Load(_install);
        Console.WriteLine("skin.ini read " + skin.SkinRead + ": FORM BACK " + skin.FormBack + " TITLE_BACK " + skin.TitleBack + " CONTROL BACK " + skin.ControlBack + " INPUT FOCUS " + skin.InputFocus +
                          "; launcher accent " + skin.Accent + " (read " + skin.AccentRead + ", " + skin.AccentUses + " uses)");
        var assists = AcAssistsIni.Load(_install);
        Console.WriteLine("assists from " + (assists.Source ?? "(none: defaults)") + ": ABS " + assists.Abs + " TC " + assists.Tc + " STABILITY " + assists.Stability + " AUTO_SHIFTER " + assists.AutoShifter +
                          " AUTO_CLUTCH " + assists.AutoClutch + " AUTO_BLIP " + assists.AutoBlip + " IDEAL_LINE " + assists.IdealLine);
        Console.WriteLine("allowed(abs 2, rule 0/1/2) = " + AcAssistsIni.Allowed(2, 0) + "/" + AcAssistsIni.Allowed(2, 1) + "/" + AcAssistsIni.Allowed(2, 2));
        foreach (string f in new[] { "segoeui.ttf", "segoeuib.ttf", "segoeuil_0.ttf", "seguisb.ttf" })
        {
            string p = Path.Combine(_install.FontsDir, f);
            Console.WriteLine("font " + f + ": " + (File.Exists(p) ? new FileInfo(p).Length + " bytes" : "MISSING"));
        }
        string gui = Path.Combine(_install.Content, "gui"), theme = Path.Combine(Path.Combine(Path.Combine(_install.Root, "launcher"), "themes"), "default");
        foreach (string p in new[] { Path.Combine(gui, "pauseMenuLogo.png"), Path.Combine(Path.Combine(theme, "graphics"), "logo.png"), Path.Combine(Path.Combine(theme, "graphics"), "nopreview.png"),
                                     Path.Combine(Path.Combine(Path.Combine(theme, "graphics"), "badges"), "badge_event_1st.png"), Path.Combine(Path.Combine(theme, "audio"), "item.wav"),
                                     Path.Combine(Path.Combine(_install.ShowroomDir, "showroom"), "showroom.kn5") })
            Console.WriteLine("art " + p.Substring(_install.Root.Length) + ": " + (File.Exists(p) ? PngSize(p) : "MISSING"));

        sw.Restart();
        var c = AcContent.Scan(_install);
        Console.WriteLine("catalog in " + sw.ElapsedMilliseconds + " ms: " + c.Cars.Count + " cars (" + c.OwnedCars + " owned, " + c.StubCars + " DLC stubs), " + c.Tracks.Count + " tracks, " + c.OwnedLayouts + "/" + c.TotalLayouts + " layouts owned");
        var brands = AcCarFilter.Brands(c.Cars, false);
        Console.WriteLine("brands (owned): " + brands.Count + ": " + string.Join(", ", brands));
        int skins = 0, previews = 0, noBadge = 0, maxSkins = 0; string maxCar = "";
        foreach (var car in c.Cars)
        {
            if (!car.Owned) continue;
            if (car.BadgePath == null) noBadge++;
            skins += car.Skins.Count;
            if (car.Skins.Count > maxSkins) { maxSkins = car.Skins.Count; maxCar = car.Id; }
            foreach (var s in car.Skins) if (s.PreviewPath != null) previews++;
        }
        Console.WriteLine("owned cars: " + skins + " skins, " + previews + " with a preview, " + noBadge + " without badge; most skins: " + maxCar + " (" + maxSkins + ")");
        var list = new List<AcCarEntry>();
        AcCarFilter.Filter(c.Cars, "", true, list);
        Console.WriteLine("drive list (all brands + stubs): " + list.Count + " entries, " + AcPaging.Pages(list.Count, 12) + " pages of 12; first owned " + (list.Count > 0 ? list[0].Id : "-") + ", last " + (list.Count > 0 ? list[list.Count - 1].Id + (list[list.Count - 1].Owned ? "" : " (stub)") : "-"));
        var f458 = c.Car("ferrari_458");
        if (f458 != null)
        {
            var ordered = AcCarFilter.SkinsInOrder(f458);
            Console.WriteLine("ferrari_458: " + f458.Name + " / " + AcUiFormat.CarLine(f458.Brand, f458.Class) + " / " + AcUiFormat.Specs(f458.Specs) + "; skins " + ordered.Count + " first " + (ordered.Count > 0 ? ordered[0].Id + " '" + ordered[0].Name + "' preview " + PngSize(ordered[0].PreviewPath) : "-"));
        }
        int previewsL = 0, outlines = 0;
        foreach (var t in c.Tracks)
            foreach (var l in t.Layouts)
            {
                if (l.PreviewPath != null) previewsL++;
                if (l.OutlinePath != null) outlines++;
                Console.WriteLine("  layout " + l.Key.PadRight(36) + (l.Owned ? "owned " : "stub  ") + (l.Name ?? "").PadRight(36) + AcUiFormat.Length(l.LengthM, false).PadLeft(10) + "  pits " + l.Pitboxes.ToString().PadLeft(2) +
                                  "  preview " + (l.PreviewPath != null ? PngSize(l.PreviewPath) : "-") + "  outline " + (l.OutlinePath != null ? PngSize(l.OutlinePath) : "-"));
            }
        Console.WriteLine("layouts: " + previewsL + " with preview, " + outlines + " with outline");
        return 0;
    }

    static int Cmd_fsetup(List<string> a)
    {
        AcLocale.Load(_install);
        string id = a.Count > 1 ? a[1] : "ferrari_458";
        var acd = AcdArchive.Open(CarPath(id));
        var items = AcSetupRows.Read(acd);
        Console.WriteLine(id + ": " + items.Count + " setup rows");
        foreach (var it in items)
            Console.WriteLine("  " + it.Tab.PadRight(12) + it.Key.PadRight(22) + ("'" + it.Label + "'").PadRight(24) + AcUiFormat.Number(it.Min) + ".." + AcUiFormat.Number(it.Max) + " step " + AcUiFormat.Number(it.Step) +
                              "  clicks " + it.ShowClicks + "  default " + AcUiFormat.Number(it.Default));
        if (a.Count > 2 && a[2] == "all")
        {
            var c = AcContent.Scan(_install);
            int cars = 0, rows = 0, fuel = 0, bias = 0, press = 0, fail = 0;
            foreach (var car in c.Cars)
            {
                if (!car.Owned) continue;
                try
                {
                    var l = AcSetupRows.Read(AcdArchive.Open(car.Dir));
                    cars++; rows += l.Count;
                    foreach (var it in l)
                    {
                        if (it.Key == "FUEL" && !float.IsNaN(it.Default)) fuel++;
                        if (it.Key == "FRONT_BIAS" && !float.IsNaN(it.Default)) bias++;
                        if (it.Key == "PRESSURE_LF" && !float.IsNaN(it.Default)) press++;
                    }
                }
                catch (Exception e) { fail++; Console.WriteLine("  FAIL " + car.Id + ": " + e.Message); }
            }
            Console.WriteLine("all owned cars: " + cars + " read, " + fail + " failed, " + rows + " rows; defaults known: FUEL " + fuel + ", FRONT_BIAS " + bias + ", PRESSURE_LF " + press);
        }
        return 0;
    }

    static int Cmd_ffmt(List<string> a)
    {
        int bad = 0;
        bad += Expect(AcUiFormat.LapTime(83456), "1:23.456");
        bad += Expect(AcUiFormat.LapTime(0), "--:--.---");
        bad += Expect(AcUiFormat.LapTime(59999), "0:59.999");
        bad += Expect(AcUiFormat.RaceTime(3723456), "1:02:03.456");
        bad += Expect(AcUiFormat.Gap(1234, 0, false), "+1.234");
        bad += Expect(AcUiFormat.Gap(62345, 0, false), "+1:02.345");
        bad += Expect(AcUiFormat.Gap(0, 2, false), "+2 LAPS");
        bad += Expect(AcUiFormat.Gap(500, 0, true), "");
        bad += Expect(AcUiFormat.Length(4909f, false), "4.909 km");
        bad += Expect(AcUiFormat.Number(15f), "15");
        bad += Expect(AcUiFormat.Number(-3.5f), "-3.5");
        bad += Expect(AcPaging.Pages(86, 12).ToString(), "8");
        bad += Expect(AcPaging.Clamp(9, 86, 12).ToString(), "7");
        AcRgba c;
        bad += Expect(AcRgba.TryParse("#bd0000", out c) ? c.ToString() : "fail", "189,0,0,255");
        bad += Expect(AcRgba.TryParse("128,8,8", out c) ? c.ToString() : "fail", "128,8,8,255");
        Console.WriteLine(bad == 0 ? "ffmt: all passed" : "ffmt: " + bad + " FAILED");
        return bad == 0 ? 0 : 1;
    }

    static int Expect(string got, string want)
    {
        bool ok = got == want;
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + "'" + got + "'" + (ok ? "" : " (want '" + want + "')"));
        return ok ? 0 : 1;
    }

    /// <summary>"WxH png/jpg, N bytes" from the file header (PNG IHDR / JPEG SOFn).</summary>
    static string PngSize(string path)
    {
        if (path == null || !File.Exists(path)) return "-";
        try
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P')
                return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19]) + "x" + ((b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]) + " png";
            if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    int m = b[i + 1];
                    int len = (b[i + 2] << 8) | b[i + 3];
                    if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                        return ((b[i + 7] << 8) | b[i + 8]) + "x" + ((b[i + 5] << 8) | b[i + 6]) + " jpg";
                    i += 2 + len;
                }
                return "jpg";
            }
            if (b.Length > 12 && b[0] == 'R' && b[1] == 'I') return b.Length + " bytes riff";
            return b.Length + " bytes";
        }
        catch (Exception e) { return "unreadable: " + e.Message; }
    }
}


static partial class Program
{
    /// <summary>fbounds &lt;kn5&gt;: world-space (AC) bounding box of every mesh and of the whole file - the hub's floor / walls (showroom).</summary>
    static int Cmd_fbounds(List<string> a)
    {
        string path = a.Count > 1 ? a[1] : Path.Combine(Path.Combine(_install.ShowroomDir, "showroom"), "showroom.kn5");
        var kn5 = Kn5File.Read(path, Kn5ReadOptions.Full);
        float[] all = { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
        foreach (var n in kn5.Nodes)
        {
            if (n.Mesh == null || !n.Mesh.Loaded) continue;
            float[] b = { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < n.Mesh.VertexCount; i++)
            {
                AcVec3 p = n.World.TransformPoint(n.Mesh.Position(i));
                Grow(b, p); Grow(all, p);
            }
            var m = kn5.Material(n.Mesh.MaterialId);
            Console.WriteLine("  " + n.Name.PadRight(28) + " min (" + b[0].ToString("0.00") + "," + b[1].ToString("0.00") + "," + b[2].ToString("0.00") + ") max (" + b[3].ToString("0.00") + "," + b[4].ToString("0.00") + "," + b[5].ToString("0.00") + ")  " + (m != null ? m.Name + " / " + m.Shader : ""));
        }
        Console.WriteLine("all: min (" + all[0].ToString("0.00") + "," + all[1].ToString("0.00") + "," + all[2].ToString("0.00") + ") max (" + all[3].ToString("0.00") + "," + all[4].ToString("0.00") + "," + all[5].ToString("0.00") + ")");
        return 0;
    }

    static void Grow(float[] b, AcVec3 p)
    {
        b[0] = Math.Min(b[0], p.X); b[1] = Math.Min(b[1], p.Y); b[2] = Math.Min(b[2], p.Z);
        b[3] = Math.Max(b[3], p.X); b[4] = Math.Max(b[4], p.Y); b[5] = Math.Max(b[5], p.Z);
    }

    // flobby: the lobby's launcher ranges + server_cfg defaults, the showroom light (AcTrackLook's maths on content/showroom/showroom/colorCurves.ini),
    // the words the new rows use, and the tyre compounds the GARAGE offers (AcCarSpec.Compounds over every owned car)
    static int Cmd_flobby(List<string> a)
    {
        string root = _install.Root;
        string html = File.ReadAllText(Path.Combine(root, "launcher", "themes", "default", "index.html"));
        foreach (string id in new[] { "sliderRaceLaps", "practiceslider", "qualifyslider", "ailevel", "damageslider", "fuelrate", "Div12", "sliderFalseStart" })
        {
            int i = html.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
            if (i < 0) { Console.WriteLine("launcher " + id + ": not found"); continue; }
            int end = html.IndexOf('>', i);
            string tag = html.Substring(i, end - i);
            Console.WriteLine("launcher " + id + ": " + tag.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
        }
        AcIni s = AcIni.Load(Path.Combine(root, "server", "cfg", "server_cfg.ini"));
        Console.WriteLine("server_cfg: LAPS=" + s.GetInt("RACE", "LAPS", -1) + " QUALIFY TIME=" + s.GetInt("QUALIFY", "TIME", -1) + " RACE_OVER_TIME=" + s.GetInt("SERVER", "RACE_OVER_TIME", -1) +
                          " ABS=" + s.GetInt("SERVER", "ABS_ALLOWED", -1) + " TC=" + s.GetInt("SERVER", "TC_ALLOWED", -1) + " AUTOCLUTCH=" + s.GetInt("SERVER", "AUTOCLUTCH_ALLOWED", -1) +
                          " DAMAGE=" + s.GetInt("SERVER", "DAMAGE_MULTIPLIER", -1) + " FUEL_RATE=" + s.GetInt("SERVER", "FUEL_RATE", -1) + " TYRE_WEAR=" + s.GetInt("SERVER", "TYRE_WEAR_RATE", -1) +
                          " TYRES_OUT=" + s.GetInt("SERVER", "ALLOWED_TYRES_OUT", -9) + " BLANKETS=" + s.GetInt("SERVER", "TYRE_BLANKETS_ALLOWED", -1));

        // the showroom light: W's AcTrackLook.Compute with no lighting.ini (pitch 35, heading 45) at SUN_ANGLE 16
        AcIni cc = AcIni.Load(Path.Combine(root, "content", "showroom", "showroom", "colorCurves.ini"));
        double p = 35 * Math.PI / 180, t = 16 * Math.PI / 180, h = 45 * Math.PI / 180;
        double nx = 0, ny = Math.Cos(p), nz = Math.Sin(p);
        double dx = nx * Math.Cos(t) - Math.Sin(t), dy = ny * Math.Cos(t), dz = nz * Math.Cos(t);
        double rx = dx * Math.Cos(h) - dz * Math.Sin(h), rz = dx * Math.Sin(h) + dz * Math.Cos(h);
        double len = Math.Sqrt(rx * rx + dy * dy + rz * rz);
        double sy = dy / len;
        float hdr = cc.GetFloat("HEADER", "HDR_OFF_MULT", 0.3f), gamma = cc.GetFloat("HEADER", "ANGLE_GAMMA", 2.4f);
        double f = Math.Pow(Math.Max(0, Math.Min(1, sy)), 1.0 / Math.Max(0.1, gamma));
        Console.WriteLine("showroom light: HDR_OFF_MULT " + hdr + " ANGLE_GAMMA " + gamma + ", sun elevation " + (Math.Asin(sy) * 180 / Math.PI).ToString("0") + " deg, curve f " + f.ToString("0.00"));
        foreach (string sec in new[] { "SUN", "AMBIENT", "SKY", "HORIZON" })
        {
            float[] lo = cc.GetFloats(sec, "LOW"), hi = cc.GetFloats(sec, "HIGH");
            if (lo == null || hi == null || lo.Length < 4 || hi.Length < 4) { Console.WriteLine("  " + sec + ": missing"); continue; }
            string c = "";
            for (int k = 0; k < 3; k++) c += (k > 0 ? "," : "") + (((lo[k] / 255f * lo[3]) * (1 - f) + (hi[k] / 255f * hi[3]) * f) * hdr).ToString("0.00");
            Console.WriteLine("  " + sec + " = " + c);
        }

        AcLocale.Load(_install);
        Console.WriteLine("words: LAP=" + AcLocale.S("LAP", "(fallback)") + " TYRES(setup)=" + AcLocale.Setup("TYRES", "(fallback)") + " QUALIFY=" + AcLocale.Session(2));

        var content = AcContent.Scan(_install);
        int cars = 0, multi = 0, failed = 0, maxN = 0;
        string example = "";
        foreach (AcCarEntry car in content.Cars)
        {
            if (!car.Owned) continue;
            cars++;
            try
            {
                AcCarSpec spec = AcCarSpec.Load(car);
                int n = spec.Compounds.Count;
                if (n > 1) multi++;
                if (n > maxN) { maxN = n; example = car.Id + ": "; for (int k = 0; k < n; k++) example += (k > 0 ? ", " : "") + spec.Compounds[k].Name + " (" + spec.Compounds[k].ShortName + ")" + (k == spec.DefaultCompound ? " default" : ""); }
            }
            catch (Exception e) { failed++; if (failed <= 3) Console.WriteLine("  spec " + car.Id + " failed: " + e.Message); }
        }
        Console.WriteLine("tyre compounds: " + cars + " owned cars, " + multi + " with a choice (> 1 compound), " + failed + " failed; most: " + example);
        return 0;
    }
}
