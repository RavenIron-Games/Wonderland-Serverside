using System;
using System.Collections.Generic;
using HarmonyLib;
using PlayFab.MultiplayerModels;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// Keeps a crossplay (-crossplay) server joinable across restarts by guarding vanilla's join-code
    /// uniqueness check. Verified against the 1.0.15 server decompile and the Vanilla Bean log
    /// (2026-09-20, libs-Tools PLAYFAB-LOBBY-AND-JOIN-CODE-FACTS.md).
    ///
    /// ZPlayFabMatchmaking registers the server as a PlayFab lobby with string_key2 = "False", then runs
    /// FindLobbies(string_key4 == JoinCode) and, in OnCheckJoinCodeSuccess, activates the lobby
    /// (string_key2 = "True") only when exactly one lobby comes back and `Lobbies[0].Owner.Id` is this
    /// server; any other non-empty answer regenerates the join code. Every client lookup - join code,
    /// host name, IP, the public list - filters string_key2 eq 'True', and a crossplay server has no
    /// Steam listen socket, so a lobby that never activates is a server nobody can join.
    ///
    /// Two vanilla quirks make that check trip over the previous run: the join code is deterministic
    /// per boot (world generation re-seeds UnityEngine.Random before GenerateJoinCode draws from it -
    /// 637457 on every Vanilla Bean boot since 1.0.15), and the shutdown-time deactivate/leave calls
    /// are fire-and-forget HTTP issued from OnApplicationQuit that almost never get out (1 of 47
    /// shutdowns). So the next boot's FindLobbies often returns the previous run's orphan lobby. While
    /// PlayFab still records its owner that is harmless (same custom-ID login, same entity id, "mine",
    /// activate). Once PlayFab has dropped the owner, `Lobbies[0].Owner` is null: OnCheckJoinCodeSuccess
    /// throws NullReferenceException at IL 0x70, the exception unwinds the PlayFab HTTP callback, nothing
    /// reschedules the check and the session sits in State.Creating until a restart. 4 of the last 8
    /// boots (0.10.1-0.10.6) did exactly that, one of them 48 minutes after the previous shutdown.
    ///
    /// The guard: a prefix that sends an answer containing any ownerless lobby down vanilla's own
    /// RegenerateJoinCode branch (new code on our lobby, re-check, activate) - the branch vanilla already
    /// takes for "someone else holds this code" - and a finalizer that turns any other exception in the
    /// callback into a logged, rescheduled check instead of a dead state machine. Nothing else in the
    /// registration flow is touched; every field and method used here is vanilla's own, reached through
    /// the publicized assembly.
    /// </summary>
    [HarmonyPatch]
    public static class CrossplayLobbyGuard
    {
        private const string Tag = "[CrossplayLobbyGuard]";

        // Read at use time so a cfg edit takes effect on the next check, without a restart.
        private static bool Enabled => WonderlandConfig.CrossplayLobbyGuardEnabled?.Value ?? true;

        [HarmonyPatch(typeof(ZPlayFabMatchmaking), nameof(ZPlayFabMatchmaking.OnCheckJoinCodeSuccess))]
        [HarmonyPrefix]
        public static bool BeforeJoinCodeCheck(ZPlayFabMatchmaking __instance, FindLobbiesResult result)
        {
            if (!Enabled)
            {
                return true;
            }
            if (__instance.m_serverData == null)
            {
                // UnregisterServer ran while the query was in flight; every vanilla branch would
                // dereference m_serverData, so there is nothing left to activate.
                WonderlandDebug.LogInfo($"{Tag} join-code reply arrived after the session was unregistered - ignored.");
                return false;
            }

            List<LobbySummary>? lobbies = result?.Lobbies;
            if (lobbies == null)
            {
                WonderlandDebug.LogWarning($"{Tag} join-code reply carried no lobby list - checking again in 1s.");
                Reschedule(__instance);
                return false;
            }

            var orphanIds = new List<string>();
            foreach (LobbySummary? lobby in lobbies)
            {
                if (lobby == null || lobby.Owner == null || string.IsNullOrEmpty(lobby.Owner.Id))
                {
                    orphanIds.Add(lobby?.LobbyId ?? "?");
                }
            }
            if (orphanIds.Count == 0)
            {
                return true;
            }

            WonderlandDebug.LogWarning($"{Tag} join code {ZPlayFabMatchmaking.JoinCode} is still held by {orphanIds.Count} ownerless lobby(ies) left behind by a previous run ({string.Join(", ", orphanIds)}) - vanilla would crash on this reply and leave the server unjoinable; regenerating the join code instead.");
            __instance.OnSessionUpdated(ZPlayFabMatchmaking.State.RegenerateJoinCode);
            return false;
        }

        [HarmonyPatch(typeof(ZPlayFabMatchmaking), nameof(ZPlayFabMatchmaking.OnCheckJoinCodeSuccess))]
        [HarmonyFinalizer]
        public static Exception? AfterJoinCodeCheck(Exception? __exception, ZPlayFabMatchmaking __instance)
        {
            if (__exception == null)
            {
                return null;
            }
            if (!Enabled)
            {
                return __exception;
            }
            if (__instance.m_serverData != null && __instance.m_retries > 0)
            {
                Reschedule(__instance);
                WonderlandDebug.LogError($"{Tag} join-code check threw {__exception.GetType().Name}: {__exception.Message} - checking again in 1s ({__instance.m_retries} retries left).\n{__exception.StackTrace}");
            }
            else
            {
                WonderlandDebug.LogError($"{Tag} join-code check threw {__exception.GetType().Name} with no retries left - the PlayFab session will not activate; restart the server.\n{__exception.StackTrace}");
            }
            return null;
        }

        // Vanilla's own retry: m_retryIn = 1s, consumed by ZPlayFabMatchmaking.Update ->
        // RetryJoinCodeUniquenessCheck -> CheckJoinCodeIsUnique. Counted against the same budget of 100
        // that OnSessionUpdated(State.Creating) hands out.
        private static void Reschedule(ZPlayFabMatchmaking matchmaking)
        {
            if (matchmaking.m_retries > 0)
            {
                matchmaking.m_retries--;
            }
            matchmaking.ScheduleJoinCodeCheck();
        }
    }
}
