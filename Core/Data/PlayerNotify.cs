using System;
using Wonderland.Core;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Server-to-player feedback on a vanilla client. Player registers a "Message" RPC on its own
    /// character ZDO (owner only, which is exactly the recipient): Player.RPC_Message(int type, string
    /// msg, int amount) hands the text to MessageHud.ShowMessage, which runs it through
    /// Localization.Localize first - so "$piece_charcoalkiln" arrives as "Charcoal kiln" in the player's
    /// language. TopLeft is the ordinary pickup-style toast; Center is the big fading line.
    /// </summary>
    public static class PlayerNotify
    {
        public static void Toast(ConnectedCharacter who, string message, bool center = false)
        {
            if (ZRoutedRpc.instance == null || who.Peer == null || who.Zdo == null)
            {
                return;
            }
            try
            {
                int type = (int)(center ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft);
                ZRoutedRpc.instance.InvokeRoutedRPC(who.Peer.m_uid, who.Zdo.m_uid, "Message", type, message, 0);
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[PlayerNotify] failed to message {who.Name}: {ex.Message}");
            }
        }
    }
}
