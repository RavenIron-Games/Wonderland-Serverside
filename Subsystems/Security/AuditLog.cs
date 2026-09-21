using System;
using System.IO;
using BepInEx;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// A distinct security/audit log channel, separate from routine debug noise.
    /// In addition to printing via WonderlandDebug.LogAlways, this writes to a dedicated,
    /// append-only security log file (e.g. BepInEx/logs/wonderland_security.log) recording
    /// the timestamp, player name, platform/Steam ID, player ID, coordinates, and infraction detail.
    /// </summary>
    public static class AuditLog
    {
        private static readonly object _fileLock = new object();
        private static string? _resolvedPath;

        public static string ResolveLogFilePath()
        {
            if (!string.IsNullOrEmpty(_resolvedPath))
            {
                return _resolvedPath!;
            }

            string configured = WonderlandConfig.SecurityLogFileName?.Value ?? "logs/wonderland_security.log";
            if (Path.IsPathRooted(configured))
            {
                _resolvedPath = configured;
            }
            else
            {
                _resolvedPath = Path.Combine(Paths.BepInExRootPath, configured);
            }

            return _resolvedPath!;
        }

        /// <param name="playerId">The character's stable id (ConnectedCharacter.PlayerId) when the flag is
        /// about a player; 0 when it is about a place or unresolved. Names are not unique, so the per-player
        /// tally in the BarrkBOT registry is keyed on this alone.</param>
        /// <param name="steamId">The player's socket host / Steam64 or PlatformUserID.</param>
        /// <param name="location">The player's world position (X, Y, Z) at the time of the event.</param>
        public static void Flag(
            string category,
            string who,
            string detail,
            long playerId = 0L,
            string? steamId = null,
            Vector3? location = null)
        {
            string identity = string.IsNullOrEmpty(who) ? "Unknown" : who;
            string extra = "";
            if (!string.IsNullOrEmpty(steamId))
            {
                extra += $" [SteamID: {steamId}]";
            }
            if (location.HasValue)
            {
                extra += $" at ({location.Value.x:F1}, {location.Value.y:F1}, {location.Value.z:F1})";
            }

            string logLine = $"[SECURITY:{category}] {identity}{extra} - {detail}";

            WonderlandDebug.LogAlways(logLine);
            BarrkBot.BarrkBotStats.OnSecurityFlag(who, playerId);

            WriteToFile(category, identity, detail, playerId, steamId, location);
        }

        /// <summary>
        /// Convenience overload that automatically extracts Player Name, Steam64/Platform ID,
        /// stable PlayerID, and current coordinates from a ConnectedCharacter.
        /// </summary>
        public static void Flag(string category, ConnectedCharacter character, string detail)
        {
            Flag(category, character.NameWithPlatform, detail, character.PlayerId, character.SocketHost, character.Position);
        }

        private static void WriteToFile(
            string category,
            string identity,
            string detail,
            long playerId,
            string? steamId,
            Vector3? location)
        {
            if (WonderlandConfig.SecurityLogEnabled?.Value == false)
            {
                return;
            }

            try
            {
                string filePath = ResolveLogFilePath();
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                string steamPart = !string.IsNullOrEmpty(steamId) ? $" | SteamID: {steamId}" : "";
                string idPart = playerId != 0L ? $" | PlayerID: {playerId}" : "";
                string locPart = location.HasValue ? $" | Location: ({location.Value.x:F1}, {location.Value.y:F1}, {location.Value.z:F1})" : "";

                string entry = $"[{time} UTC] [SECURITY:{category}] {identity}{steamPart}{idPart}{locPart} - {detail}{Environment.NewLine}";

                lock (_fileLock)
                {
                    File.AppendAllText(filePath, entry);
                }
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[AuditLog] Failed writing to security log file: {ex.Message}");
            }
        }
    }
}


