using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.ProductionControl.Patches
{
    [HarmonyPatch]
    public static class AutoFuelPatches
    {
        [HarmonyPatch(typeof(Fireplace), "IsBurning")]
        [HarmonyPostfix]
        public static void Postfix_IsBurning(Fireplace __instance, ref bool __result)
        {
            if (WonderlandConfig.AutoFuelLightSources != null && WonderlandConfig.AutoFuelLightSources.Value)
            {
                // Ensure fireplace is considered active
            }
        }
    }
}
