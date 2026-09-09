using System.Collections.Generic;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Detect-only vitals plausibility, read off each connected player's character ZDO
    /// (ConnectedCharacters): s_maxHealth and s_stamina are both mirrored there by the owning Player.
    ///
    /// Max HP is observable but NOT correctable from the server, and the first version of this file
    /// (and a whole Vitality subsystem beside it) was wrong to claim otherwise. Two independent reasons,
    /// both read from the 1.0.7 server decompile: the owning client rewrites s_maxHealth from its food
    /// values every second (Player.UpdateFood -> SetMaxHealth), so any server write is undone within a
    /// second; and while the player is moving, the owner's own DataRevision on that ZDO outruns the
    /// server's copy, so ZDOMan.RPC_ZDOData on the owner discards the server's write as stale before it
    /// ever applies. Nothing on the server consumes s_maxHealth either - damage is resolved on the
    /// victim's client - so a server-side clamp would not even change the game. What remains is the
    /// honest half: a character reporting a max HP no legitimate build can reach is flagged for an admin.
    ///
    /// Stamina was always detection-only: there is no stored max, only the current value, so this
    /// compares the observed current stamina against a single generous configured ceiling - a tripwire
    /// for "not achievable by any legitimate build", not a model of any one player's real cap.
    /// A given player is flagged once per distinct offending value, not once per pass, so a standing
    /// anomaly does not flood the audit log every few seconds.
    /// </summary>
    public static class VitalsGuard
    {
        private static float _timer;
        private static readonly Dictionary<long, float> _lastFlaggedMaxHealth = new Dictionary<long, float>();
        private static readonly Dictionary<long, float> _lastFlaggedStamina = new Dictionary<long, float>();

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.VitalsGuardEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.VitalsGuardInterval?.Value ?? 5f))
            {
                return;
            }
            _timer = 0f;

            float maxHealthCeiling = WonderlandConfig.MaxHealthCeiling?.Value ?? 0f;
            float staminaCeiling = WonderlandConfig.StaminaPlausibilityCeiling?.Value ?? 0f;

            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                long key = character.PlayerId != 0L ? character.PlayerId : character.Zdo.m_uid.GetHashCode();
                string name = character.Name;

                if (maxHealthCeiling > 0f)
                {
                    float maxHealth = character.Zdo.GetFloat(ZDOVars.s_maxHealth, 0f);
                    if (maxHealth > maxHealthCeiling + 0.01f && ShouldFlag(_lastFlaggedMaxHealth, key, maxHealth))
                    {
                        AuditLog.Flag("VitalsGuard", name, $"max HP {maxHealth:F0} exceeds configured ceiling {maxHealthCeiling:F0} - flagged, not corrected (the owning client rewrites max HP from food every second; see VitalsGuard.cs).");
                    }
                }

                if (staminaCeiling > 0f)
                {
                    float stamina = character.Zdo.GetFloat(ZDOVars.s_stamina, 0f);
                    if (stamina > staminaCeiling && ShouldFlag(_lastFlaggedStamina, key, stamina))
                    {
                        AuditLog.Flag("VitalsGuard", name, $"current stamina {stamina:F0} exceeds plausibility ceiling {staminaCeiling:F0} - flagged, not corrected (no real max to clamp to).");
                    }
                }
            }
        }

        private static bool ShouldFlag(Dictionary<long, float> lastFlagged, long key, float value)
        {
            if (lastFlagged.TryGetValue(key, out float previous) && System.Math.Abs(previous - value) < 0.5f)
            {
                return false;
            }
            lastFlagged[key] = value;
            return true;
        }
    }
}
