namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[F, Front] The local player's choices made on the tablet (remembered in AcSettings "drive.*"). Owner: F.</summary>
    public static partial class AcLocal
    {
        /// <summary>The chosen car (AcCarEntry.Id = folder name; "" = none yet).</summary>
        public static string CarId = "";
        /// <summary>The chosen skin of that car (AcSkinEntry.Id = skin folder; "" = the car's first skin).</summary>
        public static string SkinId = "";

        // ---- added by F (only ADD members)

        /// <summary>The player pressed READY in the race lobby (N publishes it as the record's "lobby ready" bit).</summary>
        public static bool Ready;
        /// <summary>The Somnium tablet (or the app on it) is open in front of the local player (S may hold the car's pedals; see AcFront.HoldCar).</summary>
        public static bool TabletOpen;
    }
}
