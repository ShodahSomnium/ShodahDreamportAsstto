using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>ui_car.json "specs" (raw strings as written + parsed numbers; NaN when absent/unparsable).</summary>
    public sealed class AcCarSpecs
    {
        public string BhpText, TorqueText, WeightText, TopSpeedText, AccelerationText, PwRatioText, RangeText;
        /// <summary>"570bhp" -> 570.</summary>
        public float Bhp = float.NaN;
        /// <summary>"540Nm" -> 540.</summary>
        public float TorqueNm = float.NaN;
        /// <summary>"1573kg" -> 1573.</summary>
        public float WeightKg = float.NaN;
        /// <summary>"325+km/h" -> 325.</summary>
        public float TopSpeedKmh = float.NaN;
        /// <summary>"3.3s 0-100" -> 3.3.</summary>
        public float Acceleration0100 = float.NaN;
        /// <summary>"2.76kg/hp" -> 2.76.</summary>
        public float PwRatio = float.NaN;
    }

    public sealed class AcSkinEntry
    {
        public string Id;              // folder name
        public string Dir;
        public string Name;            // ui_skin.json "skinname" (trimmed) or the folder name
        public string Number = "", Driver = "", Team = "", Country = "";
        public int Priority;
        public string PreviewPath;     // preview.jpg (null if missing)
        public string LiveryPath;      // livery.png 64x64 swatch (null if missing)
        public override string ToString() { return Id; }
    }

    public sealed class AcCarEntry
    {
        public string Id;              // folder name = data.acd key
        public string Dir;
        public string Name, Brand, Class, Description, Parent, Country, Year, Author, Url;
        public List<string> Tags = new List<string>();
        public AcCarSpecs Specs = new AcCarSpecs();
        public AcLut PowerCurve = new AcLut(), TorqueCurve = new AcLut();
        /// <summary>Usable: data.acd (or data/) AND the LOD_0 kn5 exist. DLC stubs (ui/ only) are listed greyed "not installed".</summary>
        public bool Owned;
        public bool IsDlcStub;
        public bool HasAcd, HasDataFolder;
        /// <summary>Main (LOD_0) model guess: the largest kn5 other than collider.kn5 (LOD_0 carries the textures). lods.ini in
        /// data.acd is the authority for LOD names - see <see cref="AcdArchive"/>.</summary>
        public string Kn5Path;
        public List<string> Kn5Files = new List<string>();
        public string ColliderPath;    // collider.kn5 or null
        public string KnhPath;         // driver_base_pos.knh or null
        public string BankPath;        // sfx/<id>.bank (or the first .bank there) or null
        public string GuidsPath;       // a mod car's own sfx/GUIDs.txt or null
        public string AnimationsDir;   // animations/ or null
        public string BadgePath;       // ui/badge.png
        public string LogoPath;        // logo.png
        public string UiJsonPath;
        public List<AcSkinEntry> Skins = new List<AcSkinEntry>();
        public Dictionary<string, object> UiJson;

        public AcSkinEntry Skin(string id)
        {
            for (int i = 0; i < Skins.Count; i++) if (string.Equals(Skins[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Skins[i];
            return null;
        }

        public override string ToString() { return Id; }
    }

    public sealed class AcLayoutEntry
    {
        public string TrackId;
        /// <summary>Layout id ("" for a single-layout track) = ui/&lt;layout&gt; = &lt;layout&gt;/ data folder = models_&lt;layout&gt;.ini suffix.</summary>
        public string Id = "";
        public string Name, Description, Country, City, Run, LengthText, WidthText;
        public List<string> Tags = new List<string>();
        /// <summary>Length in metres from ui "length" ("5793", "3.602" (km), "4655m"); NaN when unknown. fast_lane.ai is more exact.</summary>
        public float LengthM = float.NaN;
        public int Pitboxes;
        /// <summary>Playable: not a DLC stub, every MODEL kn5 present, data/surfaces.ini present.</summary>
        public bool Owned;
        public bool IsDlcStub;
        public string TrackDir;        // content/tracks/<id>
        public string BaseDir;         // TrackDir or TrackDir/<layout>
        public string DataDir, AiDir, UiDir;
        public string UiJsonPath;
        public string PreviewPath, OutlinePath, MapPath, MapIniPath;
        public string FastLanePath, PitLanePath;
        public string SurfacesIni;
        public string ModelsIniPath;   // null when missing (then <track>.kn5 alone)
        public List<string> Kn5Files = new List<string>();   // full paths of the MODEL kn5 in load order
        public List<string> MissingKn5 = new List<string>();
        /// <summary>The per-layout folders {BaseDir, DataDir, AiDir, UiDir}.</summary>
        public string[] Folders { get { return new[] { BaseDir, DataDir, AiDir, UiDir }; } }
        public Dictionary<string, object> UiJson;

        /// <summary>"monza" or "ks_vallelunga/club_circuit".</summary>
        public string Key { get { return string.IsNullOrEmpty(Id) ? TrackId : TrackId + "/" + Id; } }
        public override string ToString() { return Key; }
    }

    public sealed class AcTrackEntry
    {
        public string Id;
        public string Dir;
        public string Name;
        public bool MultiLayout;
        public List<AcLayoutEntry> Layouts = new List<AcLayoutEntry>();
        /// <summary>At least one layout is owned.</summary>
        public bool Owned
        {
            get { for (int i = 0; i < Layouts.Count; i++) if (Layouts[i].Owned) return true; return false; }
        }

        public AcLayoutEntry Layout(string id)
        {
            id = id ?? "";
            for (int i = 0; i < Layouts.Count; i++) if (string.Equals(Layouts[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Layouts[i];
            return null;
        }

        public override string ToString() { return Id; }
    }

    /// <summary>
    /// The catalog of the install: every car (owned or DLC stub) with skins and ui data, every track with its layouts. Fast and
    /// header-only: reads ui json, file listings and models ini - never a kn5 or data.acd. Safe on missing/odd files (each entry
    /// is guarded; problems are collected in <see cref="Problems"/>). Sorted by display name. Run on a worker thread.
    /// Rules: ac_car_formats.md 0/6, ac_track_formats.md 1.1-1.2/2 (single vs multi layout, DLC stubs, length quirks).
    /// </summary>
    public sealed class AcContent
    {
        public AcInstall Install;
        public List<AcCarEntry> Cars = new List<AcCarEntry>();
        public List<AcTrackEntry> Tracks = new List<AcTrackEntry>();
        public int OwnedCars;
        public int OwnedLayouts;
        public int StubCars, StubLayouts, TotalLayouts;
        public long ScanMs;
        public List<string> Problems = new List<string>();

        public static AcContent Scan(AcInstall install)
        {
            var c = new AcContent { Install = install };
            if (install == null || string.IsNullOrEmpty(install.Root)) return c;
            var sw = Stopwatch.StartNew();
            foreach (string dir in SafeDirs(install.CarsDir))
            {
                try
                {
                    var car = ScanCar(dir);
                    if (car == null) continue;
                    c.Cars.Add(car);
                    if (car.Owned) c.OwnedCars++;
                    if (car.IsDlcStub) c.StubCars++;
                }
                catch (Exception e) { c.Problems.Add("car " + Path.GetFileName(dir) + ": " + e.GetType().Name + " " + e.Message); }
            }
            foreach (string dir in SafeDirs(install.TracksDir))
            {
                try
                {
                    var t = ScanTrack(dir, c.Problems);
                    if (t == null || t.Layouts.Count == 0) continue;
                    c.Tracks.Add(t);
                    for (int i = 0; i < t.Layouts.Count; i++)
                    {
                        c.TotalLayouts++;
                        if (t.Layouts[i].Owned) c.OwnedLayouts++;
                        if (t.Layouts[i].IsDlcStub) c.StubLayouts++;
                    }
                }
                catch (Exception e) { c.Problems.Add("track " + Path.GetFileName(dir) + ": " + e.GetType().Name + " " + e.Message); }
            }
            c.Cars.Sort(CompareCars);
            c.Tracks.Sort(CompareTracks);
            c.ScanMs = sw.ElapsedMilliseconds;
            return c;
        }

        public AcCarEntry Car(string id)
        {
            for (int i = 0; i < Cars.Count; i++) if (string.Equals(Cars[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Cars[i];
            return null;
        }

        public AcTrackEntry Track(string id)
        {
            for (int i = 0; i < Tracks.Count; i++) if (string.Equals(Tracks[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Tracks[i];
            return null;
        }

        /// <summary>Layout by track id + layout id ("" for single-layout tracks), or null.</summary>
        public AcLayoutEntry Layout(string trackId, string layoutId)
        {
            var t = Track(trackId);
            return t == null ? null : t.Layout(layoutId);
        }

        static int CompareCars(AcCarEntry a, AcCarEntry b)
        {
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        static int CompareTracks(AcTrackEntry a, AcTrackEntry b)
        {
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        static int CompareLayouts(AcLayoutEntry a, AcLayoutEntry b)
        {
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        static string[] SafeDirs(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return new string[0];
                var d = Directory.GetDirectories(dir);
                Array.Sort(d, StringComparer.OrdinalIgnoreCase);
                return d;
            }
            catch (Exception) { return new string[0]; }
        }

        static string[] SafeFiles(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : new string[0]; }
            catch (Exception) { return new string[0]; }
        }

        static string IfFile(string p) { try { return File.Exists(p) ? p : null; } catch (Exception) { return null; } }

        // ---------------- cars ----------------

        static AcCarEntry ScanCar(string dir)
        {
            var car = new AcCarEntry { Id = Path.GetFileName(dir), Dir = dir };
            string ui = Path.Combine(dir, "ui");
            car.UiJsonPath = IfFile(Path.Combine(ui, "ui_car.json")) ?? IfFile(Path.Combine(ui, "dlc_ui_car.json"));
            car.UiJson = car.UiJsonPath != null ? AcJson.ParseObjectFile(car.UiJsonPath) : null;
            car.HasAcd = IfFile(Path.Combine(dir, "data.acd")) != null;
            try { car.HasDataFolder = Directory.Exists(Path.Combine(dir, "data")); } catch (Exception) { }

            long best = -1;
            foreach (string f in SafeFiles(dir, "*.kn5"))
            {
                string name = Path.GetFileName(f);
                if (string.Equals(name, "collider.kn5", StringComparison.OrdinalIgnoreCase)) { car.ColliderPath = f; continue; }
                car.Kn5Files.Add(f);
                long len = 0;
                try { len = new FileInfo(f).Length; } catch (Exception) { }
                if (len > best) { best = len; car.Kn5Path = f; }
            }
            car.Kn5Files.Sort(StringComparer.OrdinalIgnoreCase);
            if (car.UiJson == null && car.Kn5Path == null && !car.HasAcd && !car.HasDataFolder) return null; // not a car folder

            car.Owned = (car.HasAcd || car.HasDataFolder) && car.Kn5Path != null;
            car.IsDlcStub = !car.Owned;
            car.KnhPath = IfFile(Path.Combine(dir, "driver_base_pos.knh"));
            car.BadgePath = IfFile(Path.Combine(ui, "badge.png"));
            car.LogoPath = IfFile(Path.Combine(dir, "logo.png"));
            string anim = Path.Combine(dir, "animations");
            try { if (Directory.Exists(anim)) car.AnimationsDir = anim; } catch (Exception) { }
            string sfx = Path.Combine(dir, "sfx");
            car.BankPath = IfFile(Path.Combine(sfx, car.Id + ".bank"));
            if (car.BankPath == null)
            {
                var banks = SafeFiles(sfx, "*.bank");
                Array.Sort(banks, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < banks.Length; i++) if (!banks[i].EndsWith(".strings.bank", StringComparison.OrdinalIgnoreCase)) { car.BankPath = banks[i]; break; }
            }
            car.GuidsPath = IfFile(Path.Combine(sfx, "GUIDs.txt"));

            var j = car.UiJson;
            car.Name = AcJson.Text(j, "name", car.Id);
            car.Brand = AcJson.Text(j, "brand", "");
            car.Class = AcJson.Text(j, "class", "");
            car.Description = AcText.StripHtml(AcJson.Text(j, "description", ""));
            car.Parent = AcJson.Text(j, "parent", null);
            car.Country = AcJson.Text(j, "country", "");
            car.Year = AcJson.Text(j, "year", "");
            car.Author = AcJson.Text(j, "author", "");
            car.Url = AcJson.Text(j, "url", "");
            car.Tags = AcJson.Strings(j, "tags");
            var sp = AcJson.Obj(j, "specs");
            var s = car.Specs;
            s.BhpText = AcJson.Text(sp, "bhp", ""); s.Bhp = Num(s.BhpText);
            s.TorqueText = AcJson.Text(sp, "torque", ""); s.TorqueNm = Num(s.TorqueText);
            s.WeightText = AcJson.Text(sp, "weight", ""); s.WeightKg = Num(s.WeightText);
            s.TopSpeedText = AcJson.Text(sp, "topspeed", ""); s.TopSpeedKmh = Num(s.TopSpeedText);
            s.AccelerationText = AcJson.Text(sp, "acceleration", ""); s.Acceleration0100 = Num(s.AccelerationText);
            s.PwRatioText = AcJson.Text(sp, "pwratio", ""); s.PwRatio = Num(s.PwRatioText);
            s.RangeText = AcJson.Text(sp, "range", "");
            car.PowerCurve = AcJson.Curve(j, "powerCurve");
            car.TorqueCurve = AcJson.Curve(j, "torqueCurve");

            foreach (string sd in SafeDirs(Path.Combine(dir, "skins")))
            {
                var sk = new AcSkinEntry { Id = Path.GetFileName(sd), Dir = sd };
                var sj = AcJson.ParseObjectFile(Path.Combine(sd, "ui_skin.json"));
                sk.Name = AcJson.Text(sj, "skinname", sk.Id);
                sk.Number = AcJson.Text(sj, "number", "");
                sk.Driver = AcJson.Text(sj, "drivername", "");
                sk.Team = AcJson.Text(sj, "team", "");
                sk.Country = AcJson.Text(sj, "country", "");
                sk.Priority = (int)AcJson.Num(sj, "priority", 0f);
                sk.PreviewPath = IfFile(Path.Combine(sd, "preview.jpg")) ?? IfFile(Path.Combine(sd, "preview.png"));
                sk.LiveryPath = IfFile(Path.Combine(sd, "livery.png"));
                car.Skins.Add(sk);
            }
            return car;
        }

        static float Num(string s) { float v; return !string.IsNullOrEmpty(s) && AcText.TryLeadingNumber(s, out v) ? v : float.NaN; }

        // ---------------- tracks ----------------

        static AcTrackEntry ScanTrack(string dir, List<string> problems)
        {
            var t = new AcTrackEntry { Id = Path.GetFileName(dir), Dir = dir };
            string ui = Path.Combine(dir, "ui");
            bool single = IfFile(Path.Combine(ui, "ui_track.json")) != null || IfFile(Path.Combine(ui, "dlc_ui_track.json")) != null;
            if (single) t.Layouts.Add(ScanLayout(dir, t.Id, ""));
            else
            {
                foreach (string sub in SafeDirs(ui))
                {
                    if (IfFile(Path.Combine(sub, "ui_track.json")) == null && IfFile(Path.Combine(sub, "dlc_ui_track.json")) == null) continue;
                    try { t.Layouts.Add(ScanLayout(dir, t.Id, Path.GetFileName(sub))); }
                    catch (Exception e) { problems.Add("layout " + t.Id + "/" + Path.GetFileName(sub) + ": " + e.Message); }
                }
                t.MultiLayout = true;
            }
            if (t.Layouts.Count == 0) return t;
            t.Layouts.Sort(CompareLayouts);
            t.Name = TrackName(t);
            return t;
        }

        /// <summary>
        /// Single layout: its name. Multi: the common leading words of the layout names ("Drag 200m"/"Drag 1000m" -> "Drag",
        /// "Monza 1966 - Full Course"/... -> "Monza 1966"), else the shortest name.
        /// </summary>
        static string TrackName(AcTrackEntry t)
        {
            if (t.Layouts.Count == 1) return t.Layouts[0].Name;
            string shortest = null;
            for (int i = 0; i < t.Layouts.Count; i++) if (shortest == null || t.Layouts[i].Name.Length < shortest.Length) shortest = t.Layouts[i].Name;
            var words = shortest.Split(' ');
            int common = words.Length;
            for (int i = 0; i < t.Layouts.Count; i++)
            {
                var w = t.Layouts[i].Name.Split(' ');
                int k = 0;
                while (k < common && k < w.Length && string.Equals(w[k], words[k], StringComparison.OrdinalIgnoreCase)) k++;
                common = k;
            }
            if (common == 0) return shortest;
            string head = string.Join(" ", words, 0, common).Trim().TrimEnd('-').Trim();
            return head.Length > 0 ? head : shortest;
        }

        static AcLayoutEntry ScanLayout(string dir, string trackId, string layout)
        {
            var l = new AcLayoutEntry { TrackId = trackId, Id = layout, TrackDir = dir };
            l.BaseDir = layout.Length == 0 ? dir : Path.Combine(dir, layout);
            l.DataDir = Path.Combine(l.BaseDir, "data");
            l.AiDir = Path.Combine(l.BaseDir, "ai");
            l.UiDir = layout.Length == 0 ? Path.Combine(dir, "ui") : Path.Combine(Path.Combine(dir, "ui"), layout);
            l.UiJsonPath = IfFile(Path.Combine(l.UiDir, "ui_track.json"));
            if (l.UiJsonPath == null) { l.UiJsonPath = IfFile(Path.Combine(l.UiDir, "dlc_ui_track.json")); l.IsDlcStub = true; }
            l.UiJson = l.UiJsonPath != null ? AcJson.ParseObjectFile(l.UiJsonPath) : null;
            var j = l.UiJson;
            l.Name = AcJson.Text(j, "name", layout.Length == 0 ? trackId : trackId + " " + layout);
            l.Description = AcText.StripHtml(AcJson.Text(j, "description", ""));
            l.Country = AcJson.Text(j, "country", "");
            l.City = AcJson.Text(j, "city", "");
            l.Run = AcJson.Text(j, "run", "");
            l.Tags = AcJson.Strings(j, "tags");
            l.LengthText = AcJson.Text(j, "length", "");
            l.LengthM = ParseLengthM(l.LengthText);
            l.WidthText = AcJson.Text(j, "width", "");
            l.Pitboxes = (int)AcJson.Num(j, "pitboxes", 0f);

            l.PreviewPath = IfFile(Path.Combine(l.UiDir, "preview.png")) ?? IfFile(Path.Combine(l.UiDir, "dlc_preview.png"));
            l.OutlinePath = IfFile(Path.Combine(l.UiDir, "outline.png"));
            l.MapPath = IfFile(Path.Combine(l.BaseDir, "map.png"));
            l.MapIniPath = IfFile(Path.Combine(l.DataDir, "map.ini"));
            l.FastLanePath = IfFile(Path.Combine(l.AiDir, "fast_lane.ai"));
            l.PitLanePath = IfFile(Path.Combine(l.AiDir, "pit_lane.ai"));
            l.SurfacesIni = IfFile(Path.Combine(l.DataDir, "surfaces.ini"));

            var models = AcModelsIni.Load(dir, layout);
            l.ModelsIniPath = models.Path;
            for (int i = 0; i < models.Models.Count; i++)
            {
                l.Kn5Files.Add(models.Models[i].FullPath);
                if (!models.Models[i].Exists) l.MissingKn5.Add(models.Models[i].File);
            }
            l.Owned = !l.IsDlcStub && l.Kn5Files.Count > 0 && l.MissingKn5.Count == 0 && l.SurfacesIni != null;
            return l;
        }

        /// <summary>
        /// ui "length" to metres: "5793" -> 5793, "3.602" -> 3602 (km: values &lt; 100 are km), "4655m" -> 4655, "3.6 km" -> 3600.
        /// NaN when there is no number.
        /// </summary>
        public static float ParseLengthM(string s)
        {
            if (string.IsNullOrEmpty(s)) return float.NaN;
            float v;
            if (!AcText.TryLeadingNumber(s, out v) || v <= 0f) return float.NaN;
            string low = s.ToLowerInvariant();
            if (low.Contains("km") || (v < 100f && !low.Contains("m"))) v *= 1000f;
            else if (low.Contains("mi")) v *= 1609.344f;
            return v;
        }
    }
}
