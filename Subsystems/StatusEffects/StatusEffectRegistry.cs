using System.Collections.Generic;
using Wonderland.Core;

namespace Wonderland.Subsystems.StatusEffects
{
    /// <summary>
    /// Resolves configured status-effect asset names (e.g. "GP_Moder", "Potion_hasty") to their live
    /// ObjectDB.instance entry, so the roster is configured by human-readable name rather than a magic
    /// int hash - and so a renamed or removed asset in a future game update degrades to a logged
    /// warning instead of silently granting nothing or throwing.
    ///
    /// StatusEffect.NameHash() (decompile-confirmed, ~31347) hashes base.name - the Unity asset's own
    /// Object.name - NOT m_name, which is a display/localization string ("$se_adrenalinerush 1") on
    /// some assets and would resolve to nothing meaningful here.
    /// </summary>
    public static class StatusEffectRegistry
    {
        private static readonly Dictionary<string, StatusEffect> ByName = new Dictionary<string, StatusEffect>();

        public static void Rebuild()
        {
            ByName.Clear();
            if (ObjectDB.instance == null)
            {
                return;
            }

            foreach (StatusEffect effect in ObjectDB.instance.m_StatusEffects)
            {
                if (effect == null || string.IsNullOrEmpty(effect.name))
                {
                    continue;
                }
                ByName[effect.name] = effect;
            }
            WonderlandDebug.LogInfo($"[StatusEffects] indexed {ByName.Count} status effect assets from ObjectDB.");
        }

        public static bool TryGet(string assetName, out StatusEffect effect)
        {
            return ByName.TryGetValue(assetName, out effect);
        }
    }
}
