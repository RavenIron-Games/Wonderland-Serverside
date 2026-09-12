using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    [HarmonyPatch(typeof(ZoneSystem))]
    public static class WorldRatesPatches
    {
        [HarmonyPatch(nameof(ZoneSystem.SetStartingGlobalKeys))]
        [HarmonyPostfix]
        public static void Postfix_SetStartingGlobalKeys()
        {
            WorldRatesEngine.ApplyCarryWeightRate(send: false);
            WorldRatesEngine.ApplyStaminaRegenRate(send: false);
        }

        [HarmonyPatch(nameof(ZoneSystem.Start))]
        [HarmonyPostfix]
        public static void Postfix_ZoneSystemStart()
        {
            WorldRatesEngine.ApplyCarryWeightRate(send: false);
            WorldRatesEngine.ApplyStaminaRegenRate(send: false);
        }

        [HarmonyPatch("SendGlobalKeys")]
        [HarmonyPrefix]
        public static void Prefix_SendGlobalKeys(ZoneSystem __instance, long peer)
        {
            WorldRatesEngine.ApplyCarryWeightRate(send: false);
            WorldRatesEngine.ApplyStaminaRegenRate(send: false);
            WonderlandDebug.LogAlways($"[WorldRates] SendGlobalKeys to peer {peer}: keys count={__instance.m_globalKeys.Count}, " +
                $"carryweightrate={(__instance.GetGlobalKey(GlobalKeys.CarryWeightRate, out string v) ? v : "unset")}, " +
                $"staminaregenrate={(__instance.GetGlobalKey(GlobalKeys.StaminaRegenRate, out string v2) ? v2 : "unset")}");
        }
    }
}
