using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldControl.Patches
{
    [HarmonyPatch]
    public static class EnvironmentPatches
    {
        [HarmonyPatch(typeof(EnvMan), "Awake")]
        [HarmonyPostfix]
        public static void Postfix_EnvManAwake(EnvMan __instance)
        {
            if (WonderlandConfig.CustomDayLengthMinutes != null && WonderlandConfig.CustomDayLengthMinutes.Value > 0f)
            {
                __instance.m_dayLengthSec = (long)DayNightGovernor.ModifyDayLengthSec(__instance.m_dayLengthSec);
            }
        }
    }
}
