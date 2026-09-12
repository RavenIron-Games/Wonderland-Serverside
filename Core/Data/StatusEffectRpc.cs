using System;
using Wonderland.Core;

namespace Wonderland.Core.Data
{
    /// <summary>
    /// Grants an EXISTING vanilla status effect to a connected player, entirely server-side.
    ///
    /// SEMan.AddStatusEffect(int nameHash, ...)'s non-owner branch invokes a routed RPC
    /// ("RPC_AddStatusEffect") registered on the character's own ZNetView (SEMan shares
    /// Character.m_nview - decompile-confirmed at the SEMan(Character, ZNetView) constructor) -
    /// the exact same wire pattern PlayerNotify.Toast already uses for the "Message" RPC on the
    /// same ZDO. The receiving client's own SEMan resolves nameHash purely against its own
    /// already-loaded ObjectDB.instance.m_StatusEffects, so this can only re-trigger an asset every
    /// vanilla client already has - never a novel one.
    ///
    /// resetTime is always sent true: Internal_AddStatusEffect only acts on it when the effect is
    /// ALREADY active on that character (resets its timer without re-running CanAdd) - when it is
    /// not yet active, the same method forces resetTime:false internally regardless of what was
    /// passed, so a single call site safely covers both "first grant" and "keep it alive" re-pings.
    /// itemLevel/skillLevel/variant are sent at their vanilla no-op defaults: SE_Stats (the class
    /// backing every roster buff) never overrides StatusEffect.SetLevel, so these three parameters
    /// have no effect on any status effect this mod grants.
    /// </summary>
    public static class StatusEffectRpc
    {
        public static void Grant(ConnectedCharacter who, int nameHash)
        {
            if (nameHash == 0 || ZRoutedRpc.instance == null || who.Peer == null || who.Zdo == null)
            {
                return;
            }
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(who.Peer.m_uid, who.Zdo.m_uid, "RPC_AddStatusEffect", nameHash, true, 0, 0f, -1);
                WonderlandDebug.LogInfo($"[StatusEffectRpc] sent RPC_AddStatusEffect(hash {nameHash}) to {who.Name}.");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[StatusEffectRpc] failed to grant effect {nameHash} to {who.Name}: {ex.Message}");
            }
        }
    }
}
