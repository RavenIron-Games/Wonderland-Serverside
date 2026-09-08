using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// The least-verified item in this mod's security surface - flagging the mechanism and the
    /// reasoning, not promising the hook is exactly right until it's been watched fire on a live
    /// testbed. Routed RPCs (which includes combat hit data) pass through the server as the relay hub
    /// even between two clients, so the server is at least positioned to inspect a HitData payload
    /// in flight. This does not attempt to know a player's real skill level (the server can't see that -
    /// Skills.Save/Load never touch a ZDO) or reconstruct the "best legitimate combination of weapon,
    /// quality and skill" ceiling that would make this precise - that's a much larger undertaking than
    /// this first pass. Instead it's a single generous flat ceiling: a coarse tripwire for numbers no
    /// legitimate hit could produce, not a tuned model. Detect-only, always - a damage RPC still needs
    /// to apply normally regardless of what this logs.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage", typeof(long), typeof(HitData))]
    public static class DamagePlausibility
    {
        [HarmonyPostfix]
        public static void Postfix(Character __instance, HitData hit)
        {
            if (WonderlandConfig.DamagePlausibilityEnabled?.Value != true || hit == null)
            {
                return;
            }

            float ceiling = WonderlandConfig.DamagePlausibilityCeiling?.Value ?? 0f;
            if (ceiling <= 0f)
            {
                return;
            }

            float total = hit.m_damage.GetTotalDamage();
            if (total <= ceiling)
            {
                return;
            }

            Character attacker = hit.GetAttacker();
            string attackerName = attacker != null && attacker is Player attackerPlayer ? attackerPlayer.GetPlayerName() : attacker?.name;
            string victimName = __instance is Player victimPlayer ? victimPlayer.GetPlayerName() : __instance.name;

            AuditLog.Flag("DamagePlausibility", attackerName, $"hit '{victimName}' for {total:F0} damage, exceeds configured ceiling {ceiling:F0}.");
        }
    }
}
