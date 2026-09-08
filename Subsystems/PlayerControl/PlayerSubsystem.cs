using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.PlayerControl.Patches;

namespace Wonderland.Subsystems.PlayerControl
{
    public class PlayerSubsystem : IWonderlandSubsystem
    {
        public string Name => "PlayerControl";
        public bool IsEnabled => WonderlandConfig.AttackSpeedMultiplier != null;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(PlayerCombatPatches));
        }

        public void OnUpdate() { }
        public void OnGUI() { }
        public void Shutdown() { }
    }
}
