namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [Race module, R] The race hooks: AcRace is the session conductor (track load, cars, grid, lights, timing, results). Ticked every
    /// frame after the net layer and the front; its first tick starts the race-side modules (seat, HUD). Owner: R.
    /// </summary>
    public sealed partial class AcGame
    {
        partial void RaceTick() { AcRace.Tick(this); }
        partial void RaceStop() { AcRace.Stop(); }
    }
}
