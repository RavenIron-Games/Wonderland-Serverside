using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.BuildingControl.Patches;

namespace Wonderland.Subsystems.BuildingControl
{
    public class BuildingSubsystem : IWonderlandSubsystem
    {
        public string Name => "BuildingControl";
        public bool IsEnabled => WonderlandConfig.BuildRangeMultiplier != null;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(BuildingPatches));
        }

        public void OnUpdate()
        {
            // Auto repair scan loop when player holds hammer
            BuildingPatches.ProcessAutoRepair();
        }

        public void OnGUI() { }
        public void Shutdown() { }
    }
}
