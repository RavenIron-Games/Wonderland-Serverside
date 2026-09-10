using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// Player-side recovery of the overflow guard's item cache, on a vanilla client. This replaced a
    /// "/cache claim" chat hook that could never run: a stock client keeps slash commands to itself and
    /// only sends plain chat to other players, so the server never saw the text (see EmoteSignals).
    /// The emote drops cached stacks within 30 m at the player's feet, or every cached stack in the
    /// world when nothing is nearby - the same semantics the chat command was written for.
    /// </summary>
    public static class CacheClaimControl
    {
        public static void Initialize()
        {
            EmoteSignals.Register(OnEmote);
        }

        private static void OnEmote(ConnectedCharacter who, string emote)
        {
            if (!EmoteSignals.Is(emote, WonderlandConfig.CacheClaimEmote?.Value, "comehere"))
            {
                return;
            }

            Vector3 pos = who.Position;
            (int worldStacks, int worldItems) = ItemCache.GetSummary(pos, -1f);
            if (worldStacks == 0)
            {
                PlayerNotify.Toast(who, "Wonderland: the item cache is empty, nothing to recover");
                return;
            }

            int claimed = ItemCache.ClaimAt(pos, 30f);
            if (claimed == 0)
            {
                claimed = ItemCache.ClaimAt(pos, -1f);
            }
            PlayerNotify.Toast(who, claimed > 0
                ? $"Wonderland: recovered {claimed} cached stack(s) at your feet"
                : $"Wonderland: {worldStacks} cached stack(s) ({worldItems} items) could not be dropped here");
            WonderlandDebug.LogAlways($"[CacheClaimControl] {who.Name} recovered {claimed} cached stack(s) at {pos:F0}.");
        }
    }
}
