namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[P, Physics] The local player's own car and what it is doing. Owner: P (R assigns <see cref="Car"/> through P's AcCarRig spawn).</summary>
    public static partial class AcLocal
    {
        /// <summary>The local player's own car (null on foot in the hub / before spawn). The same rig the player may sit in.</summary>
        public static AcCarRig Car;
        /// <summary>Last telemetry of <see cref="Car"/> (copied by P every FixedUpdate; default when there is no car).</summary>
        public static AcTelemetry Telemetry;
    }
}
