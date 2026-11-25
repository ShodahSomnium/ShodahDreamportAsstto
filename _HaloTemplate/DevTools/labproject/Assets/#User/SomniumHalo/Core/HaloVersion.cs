namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Version stamp of the Dreamport: bump Iteration / Date / Summary on every upload and add a row to README "Iterations".</summary>
    public static class HaloVersion
    {
        public const int Iteration = 9;
        public const string Date = "2026-10-20";
        public const string Summary = "All SFX and NPC dialogue: voice budget no longer flooded, unit pain/death lines, AI weapon and vehicle sounds, devices, and co-op dialogue, music and control presses for every player";

        public static string Label
        {
            get { return "Halo Dreamport - iteration " + Iteration + " (" + Date + ")"; }
        }
    }
}
