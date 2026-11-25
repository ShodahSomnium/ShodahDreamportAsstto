namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] The scene's pre-placed network objects (Fusion NetworkObject + NetworkBridgeData64 = 64 ints each, children of "Ac Net"),
    /// found BY NAME (prefix + index) like HaloFusionTransport.Find. 69 objects in "Assets/Assetto Scene.unity" (verified by the contracts
    /// step 2026-10-31: 50 + 4 + 9 + 2 + 4, every name present once, SortKeys unique). Record numbers are Halo's (player 0..49, world
    /// 50..53, then the former Actor / Mission / Host blocks). The slot plan (what each block carries) is in DevTools/AGENTS.md "Network
    /// slot plan" and DevTools/agents/N/BRIEF.md. Owner N (may add members; the names and counts must match the scene).
    /// </summary>
    public static class AcNetSlots
    {
        /// <summary>Parent object of every block (AcIsolation.OwnNetRoot skips names starting with it).</summary>
        public const string Root = "Ac Net";

        /// <summary>"Ac Net Player 0..49": one per Dreamport player (claimed), written only by its owner ~10-30 Hz.</summary>
        public const string PlayerPrefix = "Ac Net Player ";
        public const int PlayerCount = 50;
        /// <summary>"Ac Net World 0..3": race control written by the instance master.</summary>
        public const string WorldPrefix = "Ac Net World ";
        public const int WorldCount = 4;
        /// <summary>"Ac Net Ai 0..8" (ex Halo Actor): AI cars written by the master (module I's cars, 2 per block).</summary>
        public const string AiPrefix = "Ac Net Ai ";
        public const int AiCount = 9;
        /// <summary>"Ac Net Race 0..1" (ex Halo Mission): live race state written by the master (lights, flags, penalties).</summary>
        public const string RacePrefix = "Ac Net Race ";
        public const int RaceCount = 2;
        /// <summary>"Ac Net Host 0..3": host hand-over stream (HaloNetStream port, 224 bytes per block) written by the master.</summary>
        public const string HostPrefix = "Ac Net Host ";
        public const int HostCount = 4;

        /// <summary>Ints per block (NetworkBridgeData64).</summary>
        public const int Ints = 64;

        /// <summary>First record number of each block kind (records are numbered 0..68 in this order).</summary>
        public const int FirstPlayer = 0, FirstWorld = PlayerCount, FirstAi = FirstWorld + WorldCount, FirstRace = FirstAi + AiCount,
            FirstHost = FirstRace + RaceCount, Total = FirstHost + HostCount;

        /// <summary>Record number of a scene object name, or -1 when the name is not one of ours.</summary>
        public static int RecordOf(string name)
        {
            if (name == null) return -1;
            int k;
            if (Match(name, PlayerPrefix, PlayerCount, out k)) return FirstPlayer + k;
            if (Match(name, WorldPrefix, WorldCount, out k)) return FirstWorld + k;
            if (Match(name, AiPrefix, AiCount, out k)) return FirstAi + k;
            if (Match(name, RacePrefix, RaceCount, out k)) return FirstRace + k;
            if (Match(name, HostPrefix, HostCount, out k)) return FirstHost + k;
            return -1;
        }

        private static bool Match(string name, string prefix, int count, out int index)
        {
            index = -1;
            if (!name.StartsWith(prefix, System.StringComparison.Ordinal)) return false;
            return int.TryParse(name.Substring(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index)
                   && index >= 0 && index < count;
        }
    }
}
