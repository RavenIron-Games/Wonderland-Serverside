using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Max HP: real enforcement, not just detection - Character.GetMaxHealth()/SetMaxHealth() are a
    /// plain ZDO field (ZDOVars.s_maxHealth), so a character reporting above the configured ceiling
    /// gets clamped back down, not just logged (same mechanism VitalityGovernor uses to raise a floor,
    /// run here to enforce a ceiling instead).
    /// Stamina: detection only, and necessarily coarse. There is no stored max-stamina ZDO field to
    /// read (confirmed against the decompile - GetMaxStamina() returns a plain in-memory m_maxStamina,
    /// never networked), so this cannot compare against "this player's real max" the way HP can. It
    /// compares observed current stamina against a single generous configured ceiling instead - a
    /// tripwire for "this number is not achievable by any legitimate build", not a precise model of
    /// any one player's actual cap.
    /// </summary>
    public static class VitalsGuard
    {
        private static float _timer;

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.VitalsGuardEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.VitalityCheckInterval?.Value ?? 5f))
            {
                return;
            }
            _timer = 0f;

            float maxHealthCeiling = WonderlandConfig.MaxHealthCeiling?.Value ?? 0f;
            float staminaCeiling = WonderlandConfig.StaminaPlausibilityCeiling?.Value ?? 0f;

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null)
                {
                    continue;
                }
                string name = player.GetPlayerName();

                if (maxHealthCeiling > 0f && player.GetMaxHealth() > maxHealthCeiling + 0.01f)
                {
                    float before = player.GetMaxHealth();
                    player.SetMaxHealth(maxHealthCeiling);
                    AuditLog.Flag("VitalsGuard", name, $"max HP {before} exceeded configured ceiling {maxHealthCeiling} - clamped.");
                }

                if (staminaCeiling > 0f && player.GetStamina() > staminaCeiling)
                {
                    AuditLog.Flag("VitalsGuard", name, $"current stamina {player.GetStamina()} exceeds plausibility ceiling {staminaCeiling} - flagged, not corrected (no real max to clamp to).");
                }
            }
        }
    }
}
