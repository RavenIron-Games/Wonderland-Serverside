using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Storage
{
    public class StorageSubsystem : IWonderlandSubsystem
    {
        public string Name => "Storage";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
        }

        public void OnWorldReady()
        {
            ItemCache.Initialize();
            ContainerRowsEngine.Initialize();
            CacheClaimControl.Initialize();
        }

        public void OnUpdate()
        {
            ContainerRowsEngine.OnUpdate(Time.deltaTime);
        }

        public void Shutdown()
        {
            ItemCache.Save();
        }
    }
}
