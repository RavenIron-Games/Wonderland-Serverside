using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;

namespace Wonderland.Subsystems.Storage
{
    public class StorageSubsystem : IWonderlandSubsystem
    {
        public string Name => "Storage";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(ChatCommands));

            // Re-apply whenever an admin changes the multiplier at runtime (ServerSync pushes the new
            // value and fires SettingChanged same as a local edit) - StackCapacity.Apply is idempotent
            // so this is always safe to re-run.
            if (WonderlandConfig.StackSizeMultiplier != null)
            {
                WonderlandConfig.StackSizeMultiplier.SettingChanged += (_, _) => StackCapacity.Apply();
            }
            if (WonderlandConfig.StackSizeEnabled != null)
            {
                WonderlandConfig.StackSizeEnabled.SettingChanged += (_, _) => StackCapacity.Apply();
            }
        }

        public void OnWorldReady()
        {
            StackCapacity.Apply();
            ItemCache.Initialize();

            // Stated once at startup so it is possible to tell from a log alone whether the sizing
            // features actually engaged, rather than inferring it from container behaviour in game.
            WonderlandDebug.LogAlways(
                $"[Storage] stack sizing {(WonderlandConfig.StackSizeEnabled?.Value == true ? $"ON x{WonderlandConfig.StackSizeMultiplier?.Value}" : "off")}, " +
                $"grid growth {(WonderlandConfig.GridGrowthEnabled?.Value == true ? $"ON +{WonderlandConfig.GridGrowthExtraRows?.Value} row(s)" : "off")}.");
        }

        public void OnUpdate()
        {
        }

        public void Shutdown()
        {
            ItemCache.Save();
        }
    }
}
