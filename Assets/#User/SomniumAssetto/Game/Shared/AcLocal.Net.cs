namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[N, Net] The local player's network slot. Owner: N.</summary>
    public static partial class AcLocal
    {
        /// <summary>Our "Ac Net Player n" slot index (0..49), -1 offline / not claimed.</summary>
        public static int NetSlot = -1;
        /// <summary>Our Fusion PlayerId (== Bridge Properties.PhotonId), -1 offline.</summary>
        public static int PhotonId = -1;
        /// <summary>In a Fusion room with the slot objects (others can see us); false alone / offline / in the editor.</summary>
        public static bool NetOnline;
    }
}
