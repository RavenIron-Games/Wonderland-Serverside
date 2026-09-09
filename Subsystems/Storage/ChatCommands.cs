using System;
using HarmonyLib;
using Splatform;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// Server-side chat command interceptor. Any connected vanilla client on any platform (Steam,
    /// Xbox, Crossplay) can type commands into in-game chat. This patch intercepts recognized commands
    /// on the dedicated server, processes them, and routes a private response back to the commanding
    /// player without requiring any client-side mod.
    /// </summary>
    [HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
    public static class ChatCommands
    {
        private static bool Prefix(long sender, Vector3 position, int type, UserInfo userInfo, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string trimmed = text.Trim();
            if (trimmed.Equals("/cache", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/lostitems", StringComparison.OrdinalIgnoreCase))
            {
                (int stacks, int total) = ItemCache.GetSummary(position, 30f);
                (int allStacks, int allTotal) = ItemCache.GetSummary(position, -1f);

                string reply;
                if (allStacks == 0)
                {
                    reply = "<color=cyan>[Wonderland Cache]</color> The item cache is currently empty. No items are waiting.";
                }
                else
                {
                    reply = $"<color=cyan>[Wonderland Cache]</color> {stacks} stack(s) ({total} items) within 30m; {allStacks} stack(s) ({allTotal} items) world-wide.\nType <color=yellow>/cache claim</color> to drop nearby cached items at your feet, or place chests nearby to receive them automatically.";
                }

                Reply(sender, position, reply);
                return false;
            }

            if (trimmed.Equals("/cache claim", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/cache recover", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/lostitems claim", StringComparison.OrdinalIgnoreCase))
            {
                int claimed = ItemCache.ClaimAt(position, 30f);
                if (claimed == 0)
                {
                    // If nothing was within 30m, claim all available cached items
                    claimed = ItemCache.ClaimAt(position, -1f);
                }

                string reply = claimed > 0
                    ? $"<color=cyan>[Wonderland Cache]</color> Recovered {claimed} item stack(s) directly to the ground at your feet!"
                    : "<color=cyan>[Wonderland Cache]</color> No cached items available to claim.";

                Reply(sender, position, reply);
                return false;
            }

            return true;
        }

        private static void Reply(long sender, Vector3 pos, string message)
        {
            try
            {
                var serverUser = new UserInfo
                {
                    Name = "Server",
                    UserId = PlatformUserID.None
                };

                ZRoutedRpc.instance?.InvokeRoutedRPC(
                    sender,
                    "ChatMessage",
                    pos,
                    (int)Talker.Type.Normal,
                    serverUser,
                    message);
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[ChatCommands] Failed to send chat reply to {sender}: {ex.Message}");
            }
        }
    }
}
