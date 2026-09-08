using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.InventoryControl.Patches;

namespace Wonderland.Subsystems.InventoryControl
{
    public class InventorySubsystem : IWonderlandSubsystem
    {
        public string Name => "InventoryControl";
        public bool IsEnabled => WonderlandConfig.StackMaxMultiplier != null;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(InventoryPatches));
        }

        public void OnUpdate()
        {
            ContainerVacuumEngine.ProcessVacuumScan();
        }

        public void OnGUI() { }
        public void Shutdown() { }
    }
}
