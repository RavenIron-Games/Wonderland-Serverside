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
            // No Harmony patches here any more. The former DamagePlausibility postfix on
            // Character.RPC_Damage could never run on a dedicated server: a damage RPC is routed to the
            // victim's OWNING peer and the server only relays it (ZRoutedRpc handles a routed RPC locally
            // only when it is the target or the target is Everybody), and even a locally-handled ZDO RPC
            // needs a live instance via ZNetScene.FindInstance, which the server never has away from its
            // pinned reference position. Removed in 0.2.0 rather than shipped as a dead switch.
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
