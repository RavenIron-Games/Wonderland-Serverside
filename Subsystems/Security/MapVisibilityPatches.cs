using HarmonyLib;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Forces all connected players to be publicly visible on the map and minimap.
    /// In vanilla Valheim, a player can uncheck 'Visible to other players' in their map UI,
    /// setting peer.m_publicRefPos = false.
    /// When ForcePlayerMapPosition is enabled, this patch intercepts ZNet.UpdatePlayerList
    /// right before SendPlayerList broadcasts the player list, forcing peer.m_publicRefPos = true
    /// and populating their real-time coordinates from their character ZDO.
    ///
    /// Completely server-side: vanilla clients automatically receive public positions and
    /// render player pins on their map with no client mods required.
    /// </summary>
    public static class MapVisibilityPatches
    {
        [HarmonyPatch(typeof(ZNet), "UpdatePlayerList")]
        public static class Patch_ZNet_UpdatePlayerList
        {
            [HarmonyPrefix]
            public static void Prefix(ZNet __instance)
            {
                if (!__instance.IsServer()) return;
                if (WonderlandConfig.ForcePlayerMapPosition?.Value != true) return;

                bool adminBypass = WonderlandConfig.ForcePlayerMapPositionAdminBypass?.Value == true;

                foreach (ZNetPeer peer in __instance.GetPeers())
                {
                    if (peer == null || !peer.IsReady()) continue;

                    // Server admins can toggle off their visibility to spectate if admin bypass is enabled
                    if (adminBypass && ModEnforcement.IsAdmin(peer))
                    {
                        continue;
                    }

                    peer.m_publicRefPos = true;

                    // Use real-time ZDO position if available instead of 2-second periodic refPos
                    if (peer.m_characterID != ZDOID.None && ZDOMan.instance != null)
                    {
                        ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                        if (zdo != null && zdo.IsValid())
                        {
                            peer.m_refPos = zdo.GetPosition();
                        }
                    }
                }
            }
        }
    }
}
