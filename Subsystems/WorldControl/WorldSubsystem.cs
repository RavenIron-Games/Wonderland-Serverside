using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.WorldControl.Patches;

namespace Wonderland.Subsystems.WorldControl
{
    public class WorldSubsystem : IWonderlandSubsystem
    {
        public string Name => "WorldControl";
        public bool IsEnabled => WonderlandConfig.SinglePortalDialingEnabled != null;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(PortalPatches));
            SubsystemRegistry.SafePatch(harmony, typeof(RaidPatches));
            SubsystemRegistry.SafePatch(harmony, typeof(EnvironmentPatches));
            PortalNetworkManager.Init();
        }

        public void OnUpdate() { }
        public void OnGUI()
        {
            PortalNetworkManager.OnGUI();
        }
        public void Shutdown() { }
    }
}
