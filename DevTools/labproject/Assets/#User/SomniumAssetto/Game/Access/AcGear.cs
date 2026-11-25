using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Who counts as "another Dreamport player" for the isolation's whitelisted rule. WoW decides it by a claimed network slot
    /// (Gear's record); without the net layer, a remote player counts when they are on the whitelist too -
    /// only whitelisted players ever run the Dreamport, so that is exactly the set that would hold a slot.
    /// </summary>
    public static class AcGear
    {
        public static bool HasRecord(int photonId)
        {
            // Iteration 3: while the net layer is up in a room with the slot objects (Game/Net/AcNetGame), the decision is WoW's: the player holds a written
            // "Ac Net Player" slot (claimed, in the room, not free). A visitor never claims one. Without the layer (the room, the editor, no slots) the
            // whitelist stand-in below decides, as before.
            bool known;
            bool has = AcNetGame.TryHasRecord(photonId, out known);
            if (known) return has;
            try
            {
                foreach (AcRemotes.Remote r in AcRemotes.All)
                {
                    if (r.Id != photonId) continue;
                    string id = null;
                    try { id = r.Player != null && r.Player.Properties != null ? r.Player.Properties.Id : null; }
                    catch (Exception) { id = null; }
                    return AcAccess.Matches(r.Nick, id);
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }
    }
}
