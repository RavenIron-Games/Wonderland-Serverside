using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.CraftingControl.Patches;

namespace Wonderland.Subsystems.CraftingControl
{
    public class CraftingSubsystem : IWonderlandSubsystem
    {
        public string Name => "CraftingControl";
        public bool IsEnabled => WonderlandConfig.CraftFromChestsEnabled != null;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(CraftingPatches));
        }

        public void OnUpdate() { }
        public void OnGUI() { }
        public void Shutdown() { }
    }
}
