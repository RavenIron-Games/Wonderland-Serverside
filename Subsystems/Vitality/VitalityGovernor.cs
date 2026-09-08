using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Vitality
{
    /// <summary>
    /// Base max HP floor for every connected player, enforced with a periodic correction pass over
    /// Player.GetAllPlayers() - the same live-instance mechanism the Security section's VitalsGuard
    /// uses to clamp a fabricated max HP downward, run in the opposite direction to raise a legitimate
    /// floor. Character.GetMaxHealth()/SetMaxHealth() are a plain ZDO field (ZDOVars.s_maxHealth),
    /// confirmed directly against the decompile, so this is a real, working lever, not a spike.
    ///
    /// Max stamina and max carry weight are NOT here, and are not coming later as a "v2" - checked
    /// both directly: Player.GetMaxStamina() returns m_maxStamina, and Humanoid.GetMaxCarryWeight()
    /// returns m_maxCarryWeight (modified by SEMan.ModifyMaxCarryWeight) - both plain in-memory fields
    /// that are never written to any ZDO. There is nothing server-side to read, nothing to correct,
    /// and no status-effect or world-modifier trick changes that: status effects and world modifiers
    /// are themselves simulated client-side for the same reason. This is a structural blind spot in
    /// the same sense as the Security section's skill-level one - not a missing feature.
    /// </summary>
    public static class VitalityGovernor
    {
        private static float _timer;

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.MaxHealthFloorEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.VitalityCheckInterval?.Value ?? 5f))
            {
                return;
            }
            _timer = 0f;

            float floor = WonderlandConfig.MaxHealthFloor?.Value ?? 0f;
            if (floor <= 0f)
            {
                return;
            }

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null)
                {
                    continue;
                }
                if (player.GetMaxHealth() < floor - 0.01f)
                {
                    player.SetMaxHealth(floor);
                    WonderlandDebug.LogInfo($"[VitalityGovernor] raised '{player.GetPlayerName()}' max HP to configured floor {floor}.");
                }
            }
        }
    }
}
