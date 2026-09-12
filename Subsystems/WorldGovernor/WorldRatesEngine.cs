using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// Governs Valheim 1.0+ world modifier rates strictly server-side.
    /// Valheim 1.0 introduced native World Rates (Game.UpdateWorldRates, Player.GetMaxCarryWeight),
    /// which multiply base limits by world modifier global keys (GlobalKeys.CarryWeightRate -> "carryweightrate <int_percentage>").
    /// The dedicated server broadcasts these keys to connected peers via ZRoutedRpc "GlobalKeys".
    /// Stock vanilla clients parse the rate, display the increased number in the inventory GUI, and enforce
    /// encumbrance against that limit without any client mod installed.
    /// </summary>
    public static class WorldRatesEngine
    {
        public static void Initialize()
        {
            if (WonderlandConfig.CarryWeightMultiplier != null)
            {
                WonderlandConfig.CarryWeightMultiplier.SettingChanged += (_, _) => ApplyCarryWeightRate();
            }
            if (WonderlandConfig.StaminaRegenRateMultiplier != null)
            {
                WonderlandConfig.StaminaRegenRateMultiplier.SettingChanged += (_, _) => ApplyStaminaRegenRate();
            }
        }

        public static void OnWorldReady()
        {
            // Global keys are managed safely during and after ZoneSystem.Start and SetStartingGlobalKeys.
        }

        public static void ApplyCarryWeightRate(bool send = true)
        {
            if (ZoneSystem.instance == null)
            {
                return;
            }

            float multiplier = WonderlandConfig.CarryWeightMultiplier?.Value ?? 1.0f;
            int ratePercentage = Mathf.Clamp(Mathf.RoundToInt(multiplier * 100f), 50, 1000);
            string key = $"carryweightrate {ratePercentage}";

            bool hasKeyInList = ZoneSystem.instance.m_globalKeys.Contains(key);
            bool hasValidValue = ZoneSystem.instance.GetGlobalKey(GlobalKeys.CarryWeightRate, out string currentStr)
                && int.TryParse(currentStr, out int currentVal)
                && currentVal == ratePercentage;

            if (hasKeyInList && hasValidValue)
            {
                return;
            }

            WonderlandDebug.LogAlways($"[WorldRates] Setting carry weight modifier to {multiplier:0.00}x ({ratePercentage}% -> base {Mathf.RoundToInt(300f * (ratePercentage / 100f))} lbs)...");
            ZoneSystem.instance.GlobalKeyAdd(key, true);
            if (send)
            {
                ZoneSystem.instance.SendGlobalKeys(0L);
            }
        }

        /// <summary>
        /// Same mechanism as ApplyCarryWeightRate, targeting GlobalKeys.StaminaRegenRate ->
        /// "staminaregenrate &lt;int_percentage&gt;". Game.UpdateWorldRates parses this into
        /// Game.m_staminaRegenRate on every process independently (client included), which
        /// Player.UpdateStats then applies as a straight multiplier on the final regen tick
        /// (num2 * dt * Game.m_staminaRegenRate) - completely server-side, no client mod needed.
        /// This compounds MULTIPLICATIVELY with any Status Effect Roster entry that also boosts
        /// stamina regen (they are two independent factors on the same final tick, decompile-
        /// confirmed in Player.UpdateStats) - see the config description for the concrete example.
        /// </summary>
        public static void ApplyStaminaRegenRate(bool send = true)
        {
            if (ZoneSystem.instance == null)
            {
                return;
            }

            float multiplier = WonderlandConfig.StaminaRegenRateMultiplier?.Value ?? 1.0f;
            int ratePercentage = Mathf.Clamp(Mathf.RoundToInt(multiplier * 100f), 10, 1000);
            string key = $"staminaregenrate {ratePercentage}";

            bool hasKeyInList = ZoneSystem.instance.m_globalKeys.Contains(key);
            bool hasValidValue = ZoneSystem.instance.GetGlobalKey(GlobalKeys.StaminaRegenRate, out string currentStr)
                && int.TryParse(currentStr, out int currentVal)
                && currentVal == ratePercentage;

            if (hasKeyInList && hasValidValue)
            {
                return;
            }

            WonderlandDebug.LogAlways($"[WorldRates] Setting stamina regen modifier to {multiplier:0.00}x ({ratePercentage}%)...");
            ZoneSystem.instance.GlobalKeyAdd(key, true);
            if (send)
            {
                ZoneSystem.instance.SendGlobalKeys(0L);
            }
        }
    }
}
