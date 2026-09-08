using System;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Core
{
    public static class WonderlandRPC
    {
        private static bool _registered = false;

        public static void RegisterRPCs()
        {
            if (_registered) return;
            if (ZRoutedRpc.instance == null) return;
            
            try
            {
                _registered = true;
                ZRoutedRpc.instance.Register<ZDOID, string>("Wonderland_RequestPortalDial", RPC_RequestPortalDial);
                ZRoutedRpc.instance.Register<ZDOID, string>("Wonderland_SetPortalPIN", RPC_SetPortalPIN);
                ZRoutedRpc.instance.Register<ZDOID>("Wonderland_ContainerVacuumReq", RPC_ContainerVacuumReq);
                WonderlandDebug.LogInfo("ZRoutedRpc handlers registered successfully.");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"Failed to register custom RPCs: {ex.Message}");
            }
        }

        private static void RPC_RequestPortalDial(long sender, ZDOID portalId, string targetTag)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;

            ZDO zdo = ZDOMan.instance.GetZDO(portalId);
            if (zdo != null)
            {
                zdo.Set(ZDOVars.s_tag, targetTag);
                WonderlandDebug.LogInfo($"Server set portal ZDO {portalId} target tag to '{targetTag}' via RPC.");
            }
        }

        private static void RPC_SetPortalPIN(long sender, ZDOID portalId, string pin)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;

            ZDO zdo = ZDOMan.instance.GetZDO(portalId);
            if (zdo != null)
            {
                zdo.Set("Wonderland_PIN", pin);
                WonderlandDebug.LogInfo($"Server set portal ZDO {portalId} PIN code.");
            }
        }

        private static void RPC_ContainerVacuumReq(long sender, ZDOID containerId)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
        }
    }
}
