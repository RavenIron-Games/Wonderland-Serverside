using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    public class SecuritySubsystem : IWonderlandSubsystem
    {
        public string Name => "Security";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            ModEnforcement.Initialize();
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZNet_RPC_PeerInfo));
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZRpc_HandlePackage));
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZRoutedRpc_HandleRoutedRPC));
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZNet_RPC_ServerSyncedPlayerData));
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZNet_Disconnect));
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZDOMan_CreateNewZDO));
            SubsystemRegistry.SafePatch(harmony, typeof(ModEnforcementPatches.Patch_ZDO_Deserialize));
            SubsystemRegistry.SafePatch(harmony, typeof(MapVisibilityPatches.Patch_ZNet_UpdatePlayerList));
        }

        public void OnWorldReady()
        {
            ItemTierClassifier.OnWorldReady(); // before the sweep and the guard ask for a single tier
            ItemIntegritySweep.Initialize();
            ModEnforcement.OnWorldReady();
            EquipmentGuard.OnWorldReady();
        }

        public void OnUpdate()
        {
            float dt = Time.deltaTime;
            ItemTierClassifier.OnUpdate(); // builds the tier table the first tick ObjectDB exists, rewrites it after a pin edit
            ItemIntegritySweep.OnUpdate(dt);
            VitalsGuard.OnUpdate(dt);
            PositionWatch.OnUpdate(dt);
            EquipmentGuard.OnUpdate(dt);
        }

        public void Shutdown()
        {
        }
    }
}
