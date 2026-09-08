using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.ItemFlow
{
    public class ItemFlowSubsystem : IWonderlandSubsystem
    {
        public string Name => "ItemFlow";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
        }

        public void OnWorldReady()
        {
            Core.Data.ContainerRegistry.Discover();
            VacuumEngine.Initialize();
            ProductionSupplyEngine.Initialize();
            SortEngine.Initialize();
        }

        public void OnUpdate()
        {
            float dt = Time.deltaTime;
            VacuumEngine.OnUpdate(dt);
            ProductionSupplyEngine.OnUpdate(dt);
            SortEngine.OnUpdate(dt);
        }

        public void Shutdown()
        {
        }
    }
}
