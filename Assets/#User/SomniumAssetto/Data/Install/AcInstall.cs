using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Finds the player's own Assetto Corsa install (read-only; nothing from the game ships with the world).
    ///
    /// Search order: (1) the settings hint (tablet "install folder" override) - accepted if valid, else <see cref="HintProblem"/> says
    /// why; (2) every Steam library: <c>C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf</c> (registry-free) + the usual
    /// SteamLibrary / Steam folders on every fixed drive, preferring a library whose <c>appmanifest_244210.acf</c> exists (its
    /// "installdir", normally <c>assettocorsa</c>); (3) common places on C/D/E. A root is valid when <c>AssettoCorsa.exe</c> (or acs.exe)
    /// and <c>content/cars</c> exist. Steam app id 244210 [VERIFIED on this PC: E:\SteamLibrary\steamapps\common\assettocorsa].
    /// Unity-free; call from a worker thread (touches the disk).
    /// </summary>
    public sealed class AcInstall
    {
        public const string SteamAppId = "244210";
        public const string DefaultFolderName = "assettocorsa";

        /// <summary>The install root (folder holding AssettoCorsa.exe and content/).</summary>
        public string Root;

        /// <summary>Why the last hint passed to <see cref="Find"/> was rejected (null when the hint was empty or valid).</summary>
        public static string HintProblem;

        /// <summary>How the root was found: "hint", "steam library", "common folder".</summary>
        public string FoundBy;

        /// <summary>Number of Steam libraries looked at in the last search (diagnostics).</summary>
        public static int LibrariesSearched;

        public string Content { get { return Path.Combine(Root, "content"); } }
        public string CarsDir { get { return Path.Combine(Content, "cars"); } }
        public string TracksDir { get { return Path.Combine(Content, "tracks"); } }
        public string SfxDir { get { return Path.Combine(Content, "sfx"); } }
        public string DriverDir { get { return Path.Combine(Content, "driver"); } }
        public string FontsDir { get { return Path.Combine(Content, "fonts"); } }
        public string ShowroomDir { get { return Path.Combine(Content, "showroom"); } }
        public string SystemDir { get { return Path.Combine(Root, "system"); } }
        /// <summary>content/sfx/GUIDs.txt (FMOD event names).</summary>
        public string GuidsTxt { get { return Path.Combine(SfxDir, "GUIDs.txt"); } }
        /// <summary>content/sfx/common.bank (collisions, surfaces, UI).</summary>
        public string CommonBank { get { return Path.Combine(SfxDir, "common.bank"); } }
        /// <summary>system/data/surfaces.ini (ROAD/GRASS/KERB/SAND defaults merged under every track's surfaces).</summary>
        public string SystemSurfacesIni { get { return Path.Combine(Path.Combine(SystemDir, "data"), "surfaces.ini"); } }

        public override string ToString() { return Root; }

        /// <summary>
        /// Finds the install. <paramref name="hint"/> (may be null) = a folder the player chose; also accepted: the content folder or
        /// a folder containing "assettocorsa". Returns null when nothing valid was found. Never throws.
        /// </summary>
        public static AcInstall Find(string hint)
        {
            HintProblem = null;
            LibrariesSearched = 0;
            try
            {
                if (!string.IsNullOrEmpty(hint))
                {
                    string h = NormaliseHint(hint);
                    string why;
                    if (h != null && IsInstall(h, out why)) return new AcInstall { Root = Full(h), FoundBy = "hint" };
                    IsInstall(h ?? hint.Trim(), out why);
                    HintProblem = "Assetto Corsa was not found in \"" + hint.Trim() + "\": " + why + ".";
                }

                var libs = SteamLibraries();
                LibrariesSearched = libs.Count;
                // libraries whose manifest says AC is installed first
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < libs.Count; i++)
                    {
                        string steamapps = Path.Combine(libs[i], "steamapps");
                        string manifest = Path.Combine(steamapps, "appmanifest_" + SteamAppId + ".acf");
                        bool hasManifest = SafeFileExists(manifest);
                        if ((pass == 0) != hasManifest) continue;
                        string dir = hasManifest ? ManifestInstallDir(manifest) : null;
                        string cand = Path.Combine(Path.Combine(steamapps, "common"), string.IsNullOrEmpty(dir) ? DefaultFolderName : dir);
                        string why;
                        if (IsInstall(cand, out why)) return new AcInstall { Root = cand, FoundBy = "steam library" };
                        if (!string.IsNullOrEmpty(dir) && !string.Equals(dir, DefaultFolderName, StringComparison.OrdinalIgnoreCase))
                        {
                            cand = Path.Combine(Path.Combine(steamapps, "common"), DefaultFolderName);
                            if (IsInstall(cand, out why)) return new AcInstall { Root = cand, FoundBy = "steam library" };
                        }
                    }
                }

                foreach (string c in CommonPlaces())
                {
                    string why;
                    if (IsInstall(c, out why)) return new AcInstall { Root = c, FoundBy = "common folder" };
                }
            }
            catch (Exception)
            {
                // never throw out of the finder: an unreadable drive costs one candidate, not the boot
            }
            return null;
        }

        /// <summary>Accepts the root, its content folder, or a parent holding "assettocorsa" / "steamapps/common/assettocorsa".</summary>
        static string NormaliseHint(string hint)
        {
            string h = hint.Trim().Trim('"').TrimEnd('/', '\\');
            if (h.Length == 0) return null;
            try
            {
                string why;
                if (IsInstall(h, out why)) return h;
                if (string.Equals(Path.GetFileName(h), "content", StringComparison.OrdinalIgnoreCase))
                {
                    string up = Path.GetDirectoryName(h);
                    if (up != null && IsInstall(up, out why)) return up;
                }
                string sub = Path.Combine(h, DefaultFolderName);
                if (IsInstall(sub, out why)) return sub;
                sub = Path.Combine(Path.Combine(Path.Combine(h, "steamapps"), "common"), DefaultFolderName);
                if (IsInstall(sub, out why)) return sub;
            }
            catch (Exception) { }
            return h;
        }

        /// <summary>A usable root has AssettoCorsa.exe (or acs.exe) and content/cars; <paramref name="why"/> names what is missing.</summary>
        public static bool IsInstall(string root, out string why)
        {
            why = null;
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { why = "the folder does not exist"; return false; }
                bool exe = File.Exists(Path.Combine(root, "AssettoCorsa.exe")) || File.Exists(Path.Combine(root, "acs.exe"));
                bool cars = Directory.Exists(Path.Combine(Path.Combine(root, "content"), "cars"));
                if (exe && cars) return true;
                if (!exe && !cars) why = "AssettoCorsa.exe and content\\cars are missing";
                else if (!exe) why = "AssettoCorsa.exe is missing";
                else why = "content\\cars is missing";
                return false;
            }
            catch (Exception e)
            {
                why = "the folder cannot be read (" + e.GetType().Name + ")";
                return false;
            }
        }

        static string Full(string p) { try { return Path.GetFullPath(p).TrimEnd('/', '\\'); } catch (Exception) { return p; } }

        static bool SafeFileExists(string p) { try { return File.Exists(p); } catch (Exception) { return false; } }
        static bool SafeDirExists(string p) { try { return Directory.Exists(p); } catch (Exception) { return false; } }

        /// <summary>Every Steam library root on the machine (Steam itself first), without the registry.</summary>
        public static List<string> SteamLibraries()
        {
            var libs = new List<string>();
            var vdfs = new List<string>();
            AddSteamRoot(libs, vdfs, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddSteamRoot(libs, vdfs, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddSteamRoot(libs, vdfs, @"C:\Program Files (x86)");
            AddSteamRoot(libs, vdfs, @"C:\Program Files");
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                        string[] names = { "SteamLibrary", "Steam", @"Program Files (x86)\Steam", @"Program Files\Steam", @"Games\Steam", @"Games\SteamLibrary", @"Steam Library" };
                        for (int i = 0; i < names.Length; i++)
                        {
                            string c = Path.Combine(drive.Name, names[i]);
                            if (SafeDirExists(Path.Combine(c, "steamapps"))) AddLib(libs, c);
                            string vdf = Path.Combine(Path.Combine(c, "steamapps"), "libraryfolders.vdf");
                            if (SafeFileExists(vdf) && !vdfs.Contains(vdf)) vdfs.Add(vdf);
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            for (int v = 0; v < vdfs.Count; v++)
            {
                string[] lines;
                try { lines = File.ReadAllLines(vdfs[v]); } catch (Exception) { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    string p = VdfValue(lines[i], "path");
                    if (p != null && SafeDirExists(p)) AddLib(libs, p);
                }
            }
            return libs;
        }

        static void AddSteamRoot(List<string> libs, List<string> vdfs, string programFiles)
        {
            if (string.IsNullOrEmpty(programFiles)) return;
            string steam = Path.Combine(programFiles, "Steam");
            if (!SafeDirExists(Path.Combine(steam, "steamapps"))) return;
            AddLib(libs, steam);
            string vdf = Path.Combine(Path.Combine(steam, "steamapps"), "libraryfolders.vdf");
            if (SafeFileExists(vdf) && !vdfs.Contains(vdf)) vdfs.Add(vdf);
        }

        static void AddLib(List<string> libs, string p)
        {
            string n = p.TrimEnd('/', '\\');
            for (int i = 0; i < libs.Count; i++) if (string.Equals(libs[i], n, StringComparison.OrdinalIgnoreCase)) return;
            libs.Add(n);
        }

        /// <summary>Value of a <c>"key" "value"</c> vdf line (escaped backslashes undone), or null.</summary>
        static string VdfValue(string line, string key)
        {
            if (line == null) return null;
            string k = "\"" + key + "\"";
            int at = line.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            int open = line.IndexOf('"', at + k.Length);
            if (open < 0) return null;
            int close = line.IndexOf('"', open + 1); // Steam paths never contain quotes
            if (close < 0) return null;
            return line.Substring(open + 1, close - open - 1).Replace("\\\\", "\\");
        }

        static string ManifestInstallDir(string acf)
        {
            try
            {
                var lines = File.ReadAllLines(acf);
                for (int i = 0; i < lines.Length; i++) { string v = VdfValue(lines[i], "installdir"); if (!string.IsNullOrEmpty(v)) return v; }
            }
            catch (Exception) { }
            return null;
        }

        static IEnumerable<string> CommonPlaces()
        {
            string[] drives = { "C:", "D:", "E:", "F:" };
            string[] rel =
            {
                @"\Program Files (x86)\Steam\steamapps\common\assettocorsa",
                @"\Program Files\Steam\steamapps\common\assettocorsa",
                @"\SteamLibrary\steamapps\common\assettocorsa",
                @"\Steam\steamapps\common\assettocorsa",
                @"\Games\Steam\steamapps\common\assettocorsa",
                @"\Games\SteamLibrary\steamapps\common\assettocorsa",
                @"\Games\assettocorsa",
                @"\Games\Assetto Corsa",
                @"\assettocorsa",
                @"\Assetto Corsa",
            };
            for (int d = 0; d < drives.Length; d++)
                for (int r = 0; r < rel.Length; r++)
                    yield return drives[d] + rel[r];
        }
    }
}
