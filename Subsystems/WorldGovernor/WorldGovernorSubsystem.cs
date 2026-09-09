using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    public class WorldGovernorSubsystem : IWonderlandSubsystem
    {
        public string Name => "WorldGovernor";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            SubsystemRegistry.SafePatch(harmony, typeof(RaidGovernor));
            SubsystemRegistry.SafePatch(harmony, typeof(SpawnGovernor));
            SubsystemRegistry.SafePatch(harmony, typeof(PlayerCapGovernor));
        }

        public void OnWorldReady()
        {
            StructureUpkeep.Initialize();
            PlayerCapGovernor.WarnIfCrossplayCapMismatch();
        }

        public void OnUpdate()
        {
            float dt = Time.deltaTime;
            StructureUpkeep.OnUpdate(dt);
            FirstSpawnGrant.OnUpdate(dt);
        }

        public void Shutdown()
        {
        }
    }
}
