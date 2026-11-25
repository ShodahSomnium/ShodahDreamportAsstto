using System.Collections.Generic;
namespace SomniumSpace.Worlds.SomniumAssetto
{
    public sealed class AcAiDriver { public int Index; public string Name = ""; public AcCarRig Rig; public int Level = 90; public void Produce() { } }
    public static class AcAi
    {
        private static readonly List<AcAiDriver> List = new List<AcAiDriver>();
        public static IReadOnlyList<AcAiDriver> Drivers { get { return List; } }
        public static bool Available { get { return false; } }
        public static void Spawn(int count, AcRules rules) { }
        public static void Tick() { }
        public static void Clear() { List.Clear(); }
    }
}
