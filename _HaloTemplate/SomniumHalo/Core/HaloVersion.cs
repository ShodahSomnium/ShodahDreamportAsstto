namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Version stamp of the Dreamport: bump Iteration / Date / Summary on every upload and add a row to README "Iterations".</summary>
    public static class HaloVersion
    {
        public const int Iteration = 13;
        public const string Date = "2026-10-21";
        public const string Summary = "Halo folder: forgiving folder setting, checked on SET, loads at once, D: and E: searched by default";

        public static string Label
        {
            get { return "Halo Dreamport - iteration " + Iteration + " (" + Date + ")"; }
        }
    }
}
