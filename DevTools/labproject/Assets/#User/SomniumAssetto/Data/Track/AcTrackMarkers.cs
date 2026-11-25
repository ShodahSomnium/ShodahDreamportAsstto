using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    public enum AcMarkerKind
    {
        None = 0,
        Start,          // AC_START_n      grid slot n (0 = pole, n go backwards)
        Pit,            // AC_PIT_n        pit box n (count == ui pitboxes)
        TimeGate,       // AC_TIME_n_L/R   gate n (0 = start/finish, 1.. = sector splits); use only the L/R positions
        HotlapStart,    // AC_HOTLAP_START_n  rolling hotlap start
        TimeAttack,     // AC_TIME_ATTACK_n   (monza only, purpose unproven)
        OpenFinish,     // AC_OPEN_FINISH_L/R drag strip finish gate
        AbStart,        // AC_AB_START_L/R    point-to-point start gate
        AbFinish,       // AC_AB_FINISH_L/R   point-to-point finish gate
        StartLight,     // KS_START_LIGHT_n   start-light meshes (100.kn5), emissive from data/startinglights.ini
        Semaphore,      // AC_SEMAPHORE_<RED|YELLOW|GREEN>_n  drag/A2B lamps (data/semaphore.ini)
        Audio,          // AC_AUDIO_<name>    reverb zone centres (data/audio_sources.ini NODE=)
        Crew,           // AC_CREW_*          pit equipment per box (draw)
        PObject,        // AC_POBJECT*        movable props (draw)
        Other,          // any other AC_* name
    }

    /// <summary>A classified marker node with its AC world matrix (kn5 world * models.ini transform).</summary>
    public sealed class AcMarker
    {
        public AcMarkerKind Kind;
        public string Name;
        /// <summary>Number in the name (AC_START_3 -> 3, AC_TIME_1_L -> 1); -1 when none.</summary>
        public int Index = -1;
        /// <summary>'L' / 'R' for gates, '\0' otherwise.</summary>
        public char Side;
        /// <summary>Colour for semaphores ("RED", "YELLOW", "GREEN"), audio zone name, else null.</summary>
        public string Tag;
        public AcMat4 World;
        public string Kn5File;
        public Kn5Node Node;

        /// <summary>AC world position of the node (spawn markers float 0-7.7 m above the road: raycast down).</summary>
        public AcVec3 Position { get { return World.Translation; } }
        /// <summary>Unity yaw (deg) from the horizontal part of node +Z (car forward); see <see cref="AcMat4.HeadingDeg"/>.</summary>
        public float HeadingDeg { get { return World.HeadingDeg; } }
        public override string ToString() { return Name + " " + Position; }
    }

    /// <summary>A timing gate: the segment Left -> Right (L is left of travel). Gate 0 = start/finish.</summary>
    public sealed class AcTimeGate
    {
        public int Index;
        public AcMarker Left, Right;
        public bool Complete { get { return Left != null && Right != null; } }
    }

    /// <summary>
    /// Classifies track node names and collects the markers / physics meshes of a whole layout (all its kn5 files; markers can be
    /// in any of them). Names [VERIFIED in the 35 layouts and acs.exe strings; ac_track_formats 3.2]: AC_START_n, AC_PIT_n,
    /// AC_TIME_n_L/R, AC_HOTLAP_START_n, AC_TIME_ATTACK_n, AC_OPEN_FINISH_L/R, AC_AB_START_L/R, AC_AB_FINISH_L/R, KS_START_LIGHT_n,
    /// AC_SEMAPHORE_*, AC_AUDIO_*, AC_CREW_*, AC_POBJECT*. Each AC_ marker is a dummy with a same-named child cube mesh (hide the
    /// cube: <see cref="IsMarkerCube"/>). Physics meshes: digit-prefixed names (see <see cref="AcSurfaces"/>), WALL = walls.
    /// </summary>
    public sealed class AcTrackMarkers
    {
        public readonly List<AcMarker> All = new List<AcMarker>();
        public readonly List<AcMarker> Starts = new List<AcMarker>();      // sorted by Index
        public readonly List<AcMarker> Pits = new List<AcMarker>();        // sorted by Index
        public readonly List<AcTimeGate> Gates = new List<AcTimeGate>();   // sorted by Index
        public readonly List<AcMarker> HotlapStarts = new List<AcMarker>();
        public readonly List<AcMarker> StartLights = new List<AcMarker>(); // sorted by Index
        public AcTimeGate OpenFinish, AbStart, AbFinish;

        /// <summary>Physics surface meshes found (digit-prefixed, not WALL) and walls.</summary>
        public int PhysicsMeshes, WallMeshes;
        /// <summary>Physics meshes that matched no surface key exactly (contains-fallback or ROAD default used).</summary>
        public readonly List<string> UnmatchedPhysics = new List<string>();

        /// <summary>Max cars for a race = min(#AC_START, #AC_PIT).</summary>
        public int MaxCars { get { return Math.Min(Starts.Count, Pits.Count); } }

        // ---------------- classification ----------------

        /// <summary>Classifies a node name; index/side/tag out.</summary>
        public static AcMarkerKind Classify(string name, out int index, out char side, out string tag)
        {
            index = -1; side = '\0'; tag = null;
            if (string.IsNullOrEmpty(name)) return AcMarkerKind.None;
            if (name.StartsWith("KS_START_LIGHT", StringComparison.Ordinal)) { index = TrailingNumber(name, name.Length); return AcMarkerKind.StartLight; }
            if (!name.StartsWith("AC_", StringComparison.Ordinal)) return AcMarkerKind.None;
            if (Is(name, "AC_START_")) { index = TrailingNumber(name, name.Length); return AcMarkerKind.Start; }
            if (Is(name, "AC_PIT_")) { index = TrailingNumber(name, name.Length); return AcMarkerKind.Pit; }
            if (Is(name, "AC_HOTLAP_START")) { index = Math.Max(0, TrailingNumber(name, name.Length)); return AcMarkerKind.HotlapStart; }
            if (Is(name, "AC_TIME_ATTACK")) { index = Math.Max(0, TrailingNumber(name, name.Length)); return AcMarkerKind.TimeAttack; }
            if (Is(name, "AC_TIME_"))
            {
                side = SideOf(name);
                index = TrailingNumber(name, side != '\0' ? name.Length - 2 : name.Length);
                return AcMarkerKind.TimeGate;
            }
            if (Is(name, "AC_OPEN_FINISH")) { side = SideOf(name); return AcMarkerKind.OpenFinish; }
            if (Is(name, "AC_AB_START")) { side = SideOf(name); return AcMarkerKind.AbStart; }
            if (Is(name, "AC_AB_FINISH")) { side = SideOf(name); return AcMarkerKind.AbFinish; }
            if (Is(name, "AC_SEMAPHORE_"))
            {
                index = TrailingNumber(name, name.Length);
                string rest = name.Substring(13);
                int us = rest.IndexOf('_');
                tag = us > 0 ? rest.Substring(0, us) : rest;
                return AcMarkerKind.Semaphore;
            }
            if (Is(name, "AC_AUDIO_")) { tag = name.Substring(9); return AcMarkerKind.Audio; }
            if (Is(name, "AC_CREW")) { index = LeadingNumberAfter(name, 8); return AcMarkerKind.Crew; }
            if (Is(name, "AC_POBJECT")) { index = TrailingNumber(name, name.Length); return AcMarkerKind.PObject; }
            return AcMarkerKind.Other;
        }

        public static AcMarkerKind Classify(string name) { int i; char s; string t; return Classify(name, out i, out s, out t); }

        static bool Is(string name, string prefix) { return name.StartsWith(prefix, StringComparison.Ordinal); }

        static char SideOf(string name)
        {
            int n = name.Length;
            if (n >= 2 && name[n - 2] == '_' && (name[n - 1] == 'L' || name[n - 1] == 'R')) return name[n - 1];
            return '\0';
        }

        /// <summary>The decimal number that ends at <paramref name="end"/> (exclusive), -1 if none.</summary>
        static int TrailingNumber(string s, int end)
        {
            int i = end;
            while (i > 0 && s[i - 1] >= '0' && s[i - 1] <= '9') i--;
            if (i == end) return -1;
            int v = 0;
            for (int k = i; k < end && k - i < 9; k++) v = v * 10 + (s[k] - '0');
            return v;
        }

        static int LeadingNumberAfter(string s, int start)
        {
            if (start >= s.Length) return -1;
            int v = 0, i = start;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9' && i - start < 9) { v = v * 10 + (s[i] - '0'); i++; }
            return i == start ? -1 : v;
        }

        /// <summary>
        /// True for the child cube mesh of an AC_ marker dummy (same name as its dummy parent): never draw it. AC_CREW and
        /// AC_POBJECT meshes are real props and stay visible.
        /// </summary>
        public static bool IsMarkerCube(Kn5Node n)
        {
            if (n == null || n.Mesh == null || n.Parent == null) return false;
            if (!n.Name.StartsWith("AC_", StringComparison.Ordinal)) return false;
            var k = Classify(n.Name);
            if (k == AcMarkerKind.Crew || k == AcMarkerKind.PObject) return false;
            return string.Equals(n.Parent.Name, n.Name, StringComparison.Ordinal) || n.Parent.Name.StartsWith("AC_", StringComparison.Ordinal);
        }

        // ---------------- collection ----------------

        /// <summary>
        /// Collects markers from the scanned kn5 files of a layout (<paramref name="files"/>[i] placed by <paramref name="models"/>[i];
        /// models may be null = identity). AC_ markers come from dummies; KS_START_LIGHT from any node class (one per name).
        /// When <paramref name="surfaces"/> is given, physics meshes are counted and unmatched names listed.
        /// </summary>
        public static AcTrackMarkers Collect(IList<Kn5File> files, IList<AcTrackModel> models, AcSurfaces surfaces)
        {
            var res = new AcTrackMarkers();
            var gates = new Dictionary<int, AcTimeGate>();
            var seenLights = new HashSet<string>(StringComparer.Ordinal);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            for (int f = 0; f < files.Count; f++)
            {
                var k = files[f];
                if (k == null) continue;
                AcMat4 place = models != null && f < models.Count && models[f] != null ? ModelMatrix(models[f]) : AcMat4.Identity;
                var world = Kn5Scan.WorldMatrices(k);
                for (int i = 0; i < k.Nodes.Count; i++)
                {
                    var n = k.Nodes[i];
                    if (n.Mesh != null && surfaces != null && AcSurfaces.IsPhysicsName(n.Name))
                    {
                        bool wall, exact;
                        surfaces.Match(n.Name, out wall, out exact);
                        if (wall) res.WallMeshes++;
                        else { res.PhysicsMeshes++; if (!exact) res.UnmatchedPhysics.Add(n.Name); }
                    }
                    int idx; char side; string tag;
                    var kind = Classify(n.Name, out idx, out side, out tag);
                    if (kind == AcMarkerKind.None) continue;
                    if (kind == AcMarkerKind.StartLight) { if (!seenLights.Add(n.Name)) continue; }
                    else
                    {
                        if (n.Class != Kn5NodeClass.Dummy) continue;
                        if (kind != AcMarkerKind.Crew && kind != AcMarkerKind.PObject && !seenNames.Add(n.Name)) continue;
                    }
                    var m = new AcMarker
                    {
                        Kind = kind, Name = n.Name, Index = idx, Side = side, Tag = tag, Node = n, Kn5File = k.Path,
                        World = AcMat4.Mul(world[i], place),
                    };
                    res.All.Add(m);
                    switch (kind)
                    {
                        case AcMarkerKind.Start: res.Starts.Add(m); break;
                        case AcMarkerKind.Pit: res.Pits.Add(m); break;
                        case AcMarkerKind.HotlapStart: res.HotlapStarts.Add(m); break;
                        case AcMarkerKind.StartLight: res.StartLights.Add(m); break;
                        case AcMarkerKind.TimeGate:
                        {
                            AcTimeGate g;
                            if (!gates.TryGetValue(idx, out g)) { g = new AcTimeGate { Index = idx }; gates[idx] = g; }
                            if (side == 'L') g.Left = m; else if (side == 'R') g.Right = m;
                            break;
                        }
                        case AcMarkerKind.OpenFinish: res.OpenFinish = AddSide(res.OpenFinish, m); break;
                        case AcMarkerKind.AbStart: res.AbStart = AddSide(res.AbStart, m); break;
                        case AcMarkerKind.AbFinish: res.AbFinish = AddSide(res.AbFinish, m); break;
                    }
                }
            }
            res.Starts.Sort(ByIndex);
            res.Pits.Sort(ByIndex);
            res.HotlapStarts.Sort(ByIndex);
            res.StartLights.Sort(ByIndex);
            foreach (var kv in gates) res.Gates.Add(kv.Value);
            res.Gates.Sort(ByGateIndex);
            return res;
        }

        static AcTimeGate AddSide(AcTimeGate g, AcMarker m)
        {
            if (g == null) g = new AcTimeGate();
            if (m.Side == 'L') g.Left = m; else if (m.Side == 'R') g.Right = m;
            return g;
        }

        static int ByIndex(AcMarker a, AcMarker b) { return a.Index.CompareTo(b.Index); }
        static int ByGateIndex(AcTimeGate a, AcTimeGate b) { return a.Index.CompareTo(b.Index); }

        /// <summary>
        /// The AC row-vector matrix of a models.ini placement: yaw ROTATION[0] degrees + POSITION. Equivalent Unity transform:
        /// localPosition (-x, y, z), localRotation Euler(0, ROTATION[0], 0) [ac_track_formats 1.3; sign rests on 3 gantries].
        /// </summary>
        public static AcMat4 ModelMatrix(AcTrackModel m)
        {
            float yaw = m.Rotation[0] * (float)(Math.PI / 180.0);
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            // Unity Ry(yaw) mirrored into AC (S*Ry*S = Ry(-yaw)), written in row-vector form (transpose)
            var r = AcMat4.Identity;
            r.M0 = c; r.M2 = s;
            r.M8 = -s; r.M10 = c;
            r.M12 = m.Position[0]; r.M13 = m.Position[1]; r.M14 = m.Position[2];
            return r;
        }
    }
}
