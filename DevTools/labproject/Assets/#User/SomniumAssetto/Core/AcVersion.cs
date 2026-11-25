namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Version stamp of the Dreamport: bump Iteration / Date / Summary on every upload and add a row to README "Iterations".</summary>
    public static class AcVersion
    {
        public const int Iteration = 2;
        public const string Date = "2026-11-02";
        public const string Summary = "car damage: zone damage from impacts (DAMAGE multiplier, 0 = iteration 1), paint / glass / panel visuals, aero, steer-rod and engine-life effects, engine smoke, pit and GARAGE repair, HUD damage display, damage sounds, DNF for retired cars, damage sync over net protocol 3";

        public static string Label
        {
            get { return "Assetto Corsa Dreamport - iteration " + Iteration + " (" + Date + ")"; }
        }
    }
}
