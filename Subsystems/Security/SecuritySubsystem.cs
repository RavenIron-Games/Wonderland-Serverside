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
            SubsystemRegistry.SafePatch(harmony, typeof(DamagePlausibility));
        }

        public void OnWorldReady()
        {
            ItemIntegritySweep.Initialize();
        }

        public void OnUpdate()
        {
            float dt = Time.deltaTime;
            ItemIntegritySweep.OnUpdate(dt);
            VitalsGuard.OnUpdate(dt);
            PositionWatch.OnUpdate(dt);
        }

        public void Shutdown()
        {
        }
    }
}
