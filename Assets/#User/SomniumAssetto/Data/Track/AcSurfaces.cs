using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One [SURFACE_n] of surfaces.ini (values VERIFIED; physics semantics partly BELIEF, see ac_track_formats 6.1).</summary>
    public sealed class AcSurface
    {
        public string Key = "ROAD";
        /// <summary>Tyre grip multiplier (asphalt ~0.99, kerb 0.92-0.95, grass 0.6, sand 0.8).</summary>
        public float Friction = 1f;
        /// <summary>Rolling drag (sand 0.1).</summary>
        public float Damping;
        /// <summary>Sound: "grass.wav" -> FMOD event:/surfaces/grass in common.bank (see <see cref="SoundEvent"/>).</summary>
        public string Wav = "";
        public float WavPitch;
        public string FfEffect = "";
        public float DirtAdditive;
        public bool IsValidTrack = true;
        public float BlackFlagTime;
        public float SinHeight, SinLength;
        public bool IsPitlane;
        public float VibrationGain, VibrationLength;
        /// <summary>True when the key came from system/data/surfaces.ini (not overridden by the track).</summary>
        public bool FromSystem;

        /// <summary>FMOD event path for <see cref="Wav"/> ("event:/surfaces/grass"), or null when silent.</summary>
        public string SoundEvent
        {
            get
            {
                if (string.IsNullOrEmpty(Wav)) return null;
                string w = Wav.Trim();
                int dot = w.LastIndexOf('.');
                if (dot > 0) w = w.Substring(0, dot);
                return w.Length == 0 ? null : "event:/surfaces/" + w.ToLowerInvariant();
            }
        }

        public static AcSurface FromSection(AcIniSection s)
        {
            var r = new AcSurface();
            r.Key = (s.Get("KEY", "") ?? "").Trim();
            r.Friction = s.GetFloat("FRICTION", 1f);
            r.Damping = s.GetFloat("DAMPING", 0f);
            r.Wav = (s.Get("WAV", "") ?? "").Trim();
            r.WavPitch = s.GetFloat("WAV_PITCH", 0f);
            r.FfEffect = (s.Get("FF_EFFECT", "") ?? "").Trim();
            r.DirtAdditive = s.GetFloat("DIRT_ADDITIVE", 0f);
            r.IsValidTrack = s.GetBool("IS_VALID_TRACK", true);
            r.BlackFlagTime = s.GetFloat("BLACK_FLAG_TIME", 0f);
            r.SinHeight = s.GetFloat("SIN_HEIGHT", 0f);
            r.SinLength = s.GetFloat("SIN_LENGTH", 0f);
            r.IsPitlane = s.GetBool("IS_PITLANE", false);
            r.VibrationGain = s.GetFloat("VIBRATION_GAIN", 0f);
            r.VibrationLength = s.GetFloat("VIBRATION_LENGTH", 0f);
            return r;
        }

        public override string ToString() { return Key + " f=" + Friction + (IsValidTrack ? "" : " invalid") + (IsPitlane ? " pit" : ""); }
    }

    /// <summary>
    /// The surfaces of a layout: <c>[layout/]data/surfaces.ini</c> merged over <c>system/data/surfaces.ini</c> (layout wins on a duplicate
    /// KEY; monza needs the system ROAD/GRASS/SAND for 53 meshes), matched against physics mesh names.
    ///
    /// Physics mesh name = <c>&lt;digits&gt;&lt;KEY&gt;&lt;anything&gt;</c> (<c>01CURB004</c>, <c>10TARMACA5666</c>, <c>17PITS-MNZ01</c>); the LONGEST key
    /// that the rest starts with wins; <c>&lt;digits&gt;WALL...</c> = collision wall; no key -> a key contained in the rest (laguna
    /// <c>06RDGRAVEL008</c> -> GRAVEL) [BELIEF], else ROAD. [VERIFIED: every digit-prefixed mesh of the 35 layouts matches a key or WALL
    /// except that laguna one.] Thread-safe for lookups after construction.
    /// </summary>
    public sealed class AcSurfaces
    {
        /// <summary>All surfaces, longest key first.</summary>
        public readonly List<AcSurface> Surfaces = new List<AcSurface>();
        readonly Dictionary<string, AcSurface> _byKey = new Dictionary<string, AcSurface>(StringComparer.Ordinal);

        /// <summary>Fallback for unmatched physics meshes: the ROAD key if present, else a default friction-1 surface.</summary>
        public AcSurface Default;

        /// <summary>Loads and merges. Either path may be missing.</summary>
        public static AcSurfaces Load(string layoutSurfacesIni, string systemSurfacesIni)
        {
            return FromIni(AcIni.Load(layoutSurfacesIni), AcIni.Load(systemSurfacesIni));
        }

        public static AcSurfaces FromIni(AcIni layout, AcIni system)
        {
            var s = new AcSurfaces();
            if (layout != null) s.AddAll(layout, false);
            if (system != null) s.AddAll(system, true);
            s.Surfaces.Sort(CompareLongestFirst);
            AcSurface road;
            s.Default = s._byKey.TryGetValue("ROAD", out road) ? road : new AcSurface();
            return s;
        }

        static int CompareLongestFirst(AcSurface a, AcSurface b)
        {
            int c = b.Key.Length.CompareTo(a.Key.Length);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        }

        void AddAll(AcIni ini, bool system)
        {
            var secs = ini.WithPrefix("SURFACE_");
            for (int i = 0; i < secs.Count; i++)
            {
                var sf = AcSurface.FromSection(secs[i]);
                if (sf.Key.Length == 0 || _byKey.ContainsKey(sf.Key)) continue; // layout first: it wins
                sf.FromSystem = system;
                _byKey[sf.Key] = sf;
                Surfaces.Add(sf);
            }
        }

        public AcSurface ByKey(string key) { AcSurface s; return key != null && _byKey.TryGetValue(key, out s) ? s : null; }

        /// <summary>True for a physics mesh name (starts with a digit).</summary>
        public static bool IsPhysicsName(string meshName) { return !string.IsNullOrEmpty(meshName) && meshName[0] >= '0' && meshName[0] <= '9'; }

        /// <summary>True for a wall mesh name (<c>&lt;digits&gt;WALL...</c>).</summary>
        public static bool IsWallName(string meshName)
        {
            int i = DigitPrefix(meshName);
            return i > 0 && string.CompareOrdinal(meshName, i, "WALL", 0, 4) == 0;
        }

        static int DigitPrefix(string s)
        {
            if (s == null) return 0;
            int i = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            return i;
        }

        /// <summary>
        /// Surface of a physics mesh. Null with isWall=false for a non-physics (visual) mesh; null with isWall=true for a wall.
        /// <paramref name="exact"/> is false when the contains-fallback or the ROAD default was used.
        /// </summary>
        public AcSurface Match(string meshName, out bool isWall, out bool exact)
        {
            isWall = false; exact = false;
            int i = DigitPrefix(meshName);
            if (i == 0) return null;
            if (string.CompareOrdinal(meshName, i, "WALL", 0, 4) == 0) { isWall = true; exact = true; return null; }
            for (int k = 0; k < Surfaces.Count; k++)
            {
                string key = Surfaces[k].Key;
                if (meshName.Length - i >= key.Length && string.CompareOrdinal(meshName, i, key, 0, key.Length) == 0) { exact = true; return Surfaces[k]; }
            }
            for (int k = 0; k < Surfaces.Count; k++)
                if (meshName.IndexOf(Surfaces[k].Key, i, StringComparison.Ordinal) >= 0) return Surfaces[k];
            return Default;
        }

        public AcSurface Match(string meshName, out bool isWall) { bool exact; return Match(meshName, out isWall, out exact); }
    }
}
