using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.ItemFlow
{
    public class ItemFlowSubsystem : IWonderlandSubsystem
    {
        public string Name => "ItemFlow";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            WaterBuoyancyEngine.Initialize(harmony);
        }

        public void OnWorldReady()
        {
            Core.Data.ContainerRegistry.Discover();
            VacuumEngine.Initialize();
            ProductionSupplyEngine.Initialize();
            SupplySwitch.Initialize();
            SortEngine.Initialize();
            WaterBuoyancyEngine.OnWorldReady();
        }

        public void OnUpdate()
        {
            float dt = Time.deltaTime;
            EmoteSignals.OnUpdate(dt);
            VacuumEngine.OnUpdate(dt);
            ProductionSupplyEngine.OnUpdate(dt);
            SupplySwitch.OnUpdate(dt);
            SortEngine.OnUpdate(dt);
            WaterBuoyancyEngine.OnUpdate(dt);
        }

        public void Shutdown()
        {
            SupplySwitch.Shutdown();
        }
    }
}
