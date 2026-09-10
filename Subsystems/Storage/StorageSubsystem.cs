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
        }

        public void OnWorldReady()
        {
            ItemCache.Initialize();
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
