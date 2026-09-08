using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.ProductionControl.Patches;

namespace Wonderland.Subsystems.ProductionControl
{
    public class ProductionSubsystem : IWonderlandSubsystem
    {
        public string Name => "ProductionControl";
        public bool IsEnabled => WonderlandConfig.PlantAnythingEnabled != null;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(FarmingPatches));
            SubsystemRegistry.SafePatch(harmony, typeof(AutoFuelPatches));
        }

        public void OnUpdate()
        {
            AutoFuelManager.ProcessAutoFuel();
        }

        public void OnGUI() { }
        public void Shutdown() { }
    }
}
