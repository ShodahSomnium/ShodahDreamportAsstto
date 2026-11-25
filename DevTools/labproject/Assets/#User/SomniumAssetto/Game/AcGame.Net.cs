namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [Net module, N] The net layer's hooks (Game/Net/AcNetGame): one session for the whole visit, started with the Dreamport (a
    /// whitelisted player only - a visitor never claims a slot), ticked every frame BEFORE the front and the race, stopped when the world
    /// closes (the own slot is written free). AcNetEarlyDriver runs the early frame (Update order -95: remote cars drawn, remote riders placed
    /// before the client's order-0 scripts); AcNetDriver the late frame (LateUpdate order 1000: riders again after Fusion's render). Owner: N.
    /// </summary>
    public sealed partial class AcGame
    {
        partial void NetStart()
        {
            AcNetGame.Start(this);
            AddOnce<AcNetDriver>();
            AddOnce<AcNetEarlyDriver>();
        }

        partial void NetTick() { AcNetGame.Tick(); }

        partial void NetStop() { AcNetGame.Stop(); }
    }
}
