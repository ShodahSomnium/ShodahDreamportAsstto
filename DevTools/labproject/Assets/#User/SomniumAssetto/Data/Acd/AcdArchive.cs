using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// A car's configuration/physics files: the encrypted <c>data.acd</c> archive, or (mod cars) an unpacked <c>data/</c> folder.
    ///
    /// data.acd layout [VERIFIED: 86/86 installed cars decrypt; ac_car_formats.md 2]: optional header <c>int32 -1111, int32</c>
    /// (not used by any installed car), then until EOF: <c>int32 nameLength, name (ASCII), int32 length, int32[length]</c> where each
    /// int32 holds one encrypted byte in its LOW byte; <c>plain[i] = (byte)(low[i] - key[i % key.Length])</c>, key = ASCII bytes of
    /// <see cref="Key"/>(lower-cased car folder name). Variants carry their own data.acd keyed by their own folder name.
    /// The archive decrypts everything at open (~150 KB per car, ~1-3 ms); entries are looked up case-insensitively.
    /// </summary>
    public sealed class AcdArchive
    {
        readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _names = new List<string>();

        /// <summary>The car folder this archive belongs to.</summary>
        public string CarDir;
        /// <summary>Path of data.acd, or of the data/ folder when unpacked.</summary>
        public string SourcePath;
        /// <summary>True when read from an unpacked data/ folder.</summary>
        public bool Unpacked;

        /// <summary>Entry names in archive order.</summary>
        public List<string> Names { get { return _names; } }
        public int Count { get { return _names.Count; } }

        /// <summary>
        /// Opens a car's data: <c>data.acd</c> if present (AC's priority [BELIEF]), else the <c>data/</c> folder. Null when neither exists.
        /// Throws <see cref="InvalidDataException"/> for a corrupt archive.
        /// </summary>
        public static AcdArchive Open(string carDir)
        {
            if (string.IsNullOrEmpty(carDir)) return null;
            string acd = Path.Combine(carDir, "data.acd");
            string folderName = Path.GetFileName(carDir.TrimEnd('/', '\\'));
            if (File.Exists(acd)) return OpenAcd(acd, folderName, carDir);
            string data = Path.Combine(carDir, "data");
            if (Directory.Exists(data)) return OpenFolder(data, carDir);
            return null;
        }

        /// <summary>Decrypts a data.acd with the key derived from <paramref name="keyFolderName"/> (the car folder name).</summary>
        public static AcdArchive OpenAcd(string acdPath, string keyFolderName, string carDir = null)
        {
            return FromBytes(File.ReadAllBytes(acdPath), keyFolderName, acdPath, carDir ?? Path.GetDirectoryName(acdPath));
        }

        public static AcdArchive FromBytes(byte[] b, string keyFolderName, string sourcePath = null, string carDir = null)
        {
            var a = new AcdArchive { SourcePath = sourcePath, CarDir = carDir };
            byte[] key = Encoding.ASCII.GetBytes(Key(keyFolderName));
            int p = 0, n = b.Length;
            if (n >= 8 && I32(b, 0) == -1111) p = 8;
            while (p < n)
            {
                if (p + 4 > n) throw new InvalidDataException("acd: truncated name length at " + p);
                int nameLen = I32(b, p); p += 4;
                if (nameLen <= 0 || nameLen > 512 || p + nameLen > n) throw new InvalidDataException("acd: bad name length " + nameLen + " at " + (p - 4));
                string name = Encoding.ASCII.GetString(b, p, nameLen); p += nameLen;
                if (p + 4 > n) throw new InvalidDataException("acd: truncated length for " + name);
                int len = I32(b, p); p += 4;
                if (len < 0 || (long)p + (long)len * 4 > n) throw new InvalidDataException("acd: bad length " + len + " for " + name);
                var plain = new byte[len];
                int kl = key.Length;
                for (int i = 0, q = p; i < len; i++, q += 4) plain[i] = (byte)(b[q] - key[i % kl]);
                p += len * 4;
                a.Add(name, plain);
            }
            return a;
        }

        /// <summary>Reads every file of an unpacked data/ folder (top level only, as AC does).</summary>
        public static AcdArchive OpenFolder(string dataDir, string carDir = null)
        {
            var a = new AcdArchive { SourcePath = dataDir, Unpacked = true, CarDir = carDir ?? Path.GetDirectoryName(dataDir.TrimEnd('/', '\\')) };
            var files = Directory.GetFiles(dataDir);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                try { a.Add(Path.GetFileName(files[i]), File.ReadAllBytes(files[i])); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return a;
        }

        void Add(string name, byte[] data)
        {
            if (!_files.ContainsKey(name)) _names.Add(name);
            _files[name] = data;
        }

        static int I32(byte[] b, int o) { return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24); }

        public bool Has(string name) { return name != null && _files.ContainsKey(name); }

        /// <summary>Raw bytes of an entry, or null.</summary>
        public byte[] ReadBytes(string name)
        {
            byte[] d;
            return name != null && _files.TryGetValue(name, out d) ? d : null;
        }

        /// <summary>Text of an entry (UTF-8 / Latin-1 fallback, BOM stripped), or null.</summary>
        public string ReadText(string name)
        {
            var d = ReadBytes(name);
            return d == null ? null : AcText.Decode(d);
        }

        /// <summary>Parsed ini entry, or null when absent (an empty file gives an empty ini).</summary>
        public AcIni ReadIni(string name)
        {
            var t = ReadText(name);
            return t == null ? null : AcIni.Parse(t, name);
        }

        /// <summary>Parsed ini entry, never null.</summary>
        public AcIni ReadIniOrEmpty(string name) { return ReadIni(name) ?? new AcIni { Source = name }; }

        /// <summary>
        /// A LUT referenced by an ini value: inline "(0=0|1000=24)" or an entry name ("power.lut"). Null when absent.
        /// </summary>
        public AcLut ReadLut(string nameOrInline)
        {
            if (string.IsNullOrEmpty(nameOrInline)) return null;
            var inline = AcLut.ParseInline(nameOrInline);
            if (inline != null) return inline;
            var t = ReadText(nameOrInline.Trim());
            return t == null ? null : AcLut.Parse(t);
        }

        /// <summary>
        /// The data.acd key for a car folder name [VERIFIED: ferrari_458 = "235-167-105-254-54-143-17-57",
        /// ks_mazda_miata = "181-123-119-57-70-14-64-98"]: 8 integer factors of the lower-cased name, each &amp; 0xFF, joined with '-'.
        /// C# int arithmetic (wrapping, integer division) exactly as Content Manager.
        /// </summary>
        public static string Key(string carFolderName)
        {
            string s = (carFolderName ?? "").ToLowerInvariant();
            int f0 = 0; for (int i = 0; i < s.Length; i++) f0 += s[i];
            int f1 = 0; for (int i = 0; i < s.Length - 1; i += 2) { f1 *= s[i]; f1 -= s[i + 1]; }
            int f2 = 0; for (int i = 1; i < s.Length - 3; i += 3) { f2 *= s[i]; f2 /= s[i + 1] + 0x1b; f2 += -0x1b - s[i - 1]; }
            int f3 = 0x1683; for (int i = 1; i < s.Length; i++) f3 -= s[i];
            int f4 = 0x42; for (int i = 1; i < s.Length - 4; i += 4) f4 = (s[i] + 0xf) * f4 * (s[i - 1] + 0xf) + 0x16;
            int f5 = 0x65; for (int i = 0; i < s.Length - 2; i += 2) f5 -= s[i];
            int f6 = 0xab; for (int i = 0; i < s.Length - 2; i += 2) f6 %= s[i];
            int f7 = 0xab; for (int i = 0; i < s.Length - 1; i++) { f7 /= s[i]; f7 += s[i + 1]; }
            return (f0 & 0xff) + "-" + (f1 & 0xff) + "-" + (f2 & 0xff) + "-" + (f3 & 0xff) + "-" + (f4 & 0xff) + "-" + (f5 & 0xff) + "-" + (f6 & 0xff) + "-" + (f7 & 0xff);
        }
    }
}
