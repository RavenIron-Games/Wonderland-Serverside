using HarmonyLib;
using UnityEngine;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Prefix on ZDOMan.RPC_DestroyZDO (1.0.12 server decompile 76953): every "DestroyZDO" batch a client
    /// sends passes through here before vanilla applies it. Only an owner can send one (ZDOMan.DestroyZDO
    /// 76929 is owner-gated), and after the vacuum's settle claim a client is not the owner unless the claim
    /// never reached it in time - so a client's destroy for a stack the vacuum moved a moment ago is the one
    /// certain sign that the player committed a pickup first. VacuumEngine.OnClientDestroy answers it by
    /// taking the copy back out of the container. Read-only on the package (position restored), never throws
    /// into vanilla - an exception here would drop the whole destroy batch for every peer.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "RPC_DestroyZDO")]
    public static class LatePickupPatch
    {
        private static float _lastFailureLog = -999f;

        [HarmonyPrefix]
        public static void Prefix(long sender, ZPackage pkg)
        {
            try
            {
                VacuumEngine.OnClientDestroy(sender, pkg);
                Core.Data.ClaimEcho.OnClientDestroyBatch(sender, pkg); // a ghost left by a claim that raced this pickup
            }
            catch (System.Exception ex)
            {
                if (Time.time - _lastFailureLog >= 60f)
                {
                    _lastFailureLog = Time.time;
                    Core.WonderlandDebug.LogWarning($"[Vacuum] late-pickup check failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
