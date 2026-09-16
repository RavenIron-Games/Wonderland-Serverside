using System;
using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// The auto-harvest trigger. Every routed RPC the dedicated server handles passes through
    /// ZRoutedRpc.HandleRoutedRPC (1.0.12 server decompile 83646); a pick is the owner client's
    /// "RPC_SetPicked" true addressed to Everybody (Pickable.RPC_Pick 71050), and for a ZDO the server has no
    /// instance of - every pickable near a player - vanilla drops it there without a trace
    /// (VALHEIM-DEDICATED-SERVER-FACTS: "a routed RPC aimed at a ZDO with no local instance is dropped,
    /// silently"). This prefix reads it first. A patch on Pickable itself could never fire here: the server
    /// build pins ZNet's reference position at (1e6, 0, 1e6) every physics tick (Game.FixedUpdate) and only
    /// instantiates around that point (see ConnectedCharacters). Separate from WaterBuoyancyEngine's
    /// RoutedRpcHandlerPatch on the same method on purpose - SafePatch isolates each set's failure, and
    /// HarmonyX runs every prefix regardless of what another returns. The body never throws outward: an
    /// exception here would unwind RPC_RoutedRPC before it relays the message to the other clients, and
    /// they would keep a stale plant on screen.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    public static class HarvestTriggerPatch
    {
        private static readonly int SetPickedHash = "RPC_SetPicked".GetStableHashCode();
        private static float _lastFailureLog = -999f;
        private const float FailureLogIntervalSeconds = 60f;

        [HarmonyPrefix]
        public static void Prefix(ZRoutedRpc.RoutedRPCData data)
        {
            if (data == null || data.m_methodHash != SetPickedHash || data.m_targetZDO.IsNone())
            {
                return;
            }
            try
            {
                VacuumEngine.OnPickedRpc(data);
            }
            catch (Exception ex)
            {
                if (UnityEngine.Time.time - _lastFailureLog >= FailureLogIntervalSeconds)
                {
                    _lastFailureLog = UnityEngine.Time.time;
                    WonderlandDebug.LogWarning($"[AutoHarvest] trigger failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
