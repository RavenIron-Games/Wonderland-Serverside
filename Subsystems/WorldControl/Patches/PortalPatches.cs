using System;
using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldControl.Patches
{
    [HarmonyPatch]
    public static class PortalPatches
    {
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsTeleportable))]
        [HarmonyPrefix]
        public static bool Prefix_IsTeleportable(Humanoid __instance, ref bool __result)
        {
            if (WonderlandConfig.AllowMetalsThroughPortals != null && WonderlandConfig.AllowMetalsThroughPortals.Value)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
        [HarmonyPrefix]
        public static bool Prefix_TeleportWorldInteract(TeleportWorld __instance, Humanoid human, bool hold, bool alt)
        {
            if (WonderlandConfig.SinglePortalDialingEnabled != null && WonderlandConfig.SinglePortalDialingEnabled.Value)
            {
                if (alt)
                {
                    PortalNetworkManager.OpenPortalMenu(__instance);
                    return false;
                }
            }
            return true;
        }
    }
}
