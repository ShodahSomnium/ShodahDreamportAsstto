namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [Front module, F] The front end's hooks: AcGame calls them only after the whitelist gate let the player in (StartDreamport), so a
    /// visitor never gets the hub, the tablet app or any install file read. Everything is in Game/Front (AcFront is the conductor).
    /// Owner: F (keep the four hooks; AcGame guards each call).
    /// </summary>
    public sealed partial class AcGame
    {
        partial void FrontStart() { AcFront.Start(this); }
        partial void FrontBootDone() { AcFront.BootDone(); }
        partial void FrontTick() { AcFront.Tick(); }
        partial void FrontStop() { AcFront.Stop(); }
    }
}
