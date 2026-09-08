using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldGovernor
{
    /// <summary>
    /// Raises (or lowers) the vanilla dedicated-server player cap. Verified directly against the
    /// decompile: ZNet.RPC_PeerInfo does not read the limit from any field or launch argument - it is
    /// a bare `if (GetNrOfPlayers() >= 10)` literal in the middle of the connection handshake, which
    /// also checks password/version/ban/duplicate-connection state before and after it. A prefix that
    /// sometimes skips the original method can't work here: skipping past the vanilla check to admit
    /// an 11th player would also skip the rest of that handshake (password check, duplicate-connection
    /// check) for everyone, real risk for a surgical "just raise the number" feature. A transpiler that
    /// only replaces the literal 10 leaves every surrounding check completely untouched.
    /// This is the most likely-to-drift piece in the whole mod if a future game patch reshapes
    /// RPC_PeerInfo - the "pattern not found" log line below is not decorative, it is the signal that
    /// this needs re-verification against a new build.
    /// </summary>
    [HarmonyPatch]
    public static class PlayerCapGovernor
    {
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getNrOfPlayers = AccessTools.Method(typeof(ZNet), "GetNrOfPlayers");
            var matcher = new CodeMatcher(instructions)
                .MatchForward(false, new CodeMatch(ci => ci.Calls(getNrOfPlayers)));

            if (!matcher.IsValid)
            {
                WonderlandDebug.LogWarning("[PlayerCapGovernor] could not find GetNrOfPlayers() call in RPC_PeerInfo - player cap left at vanilla default. Needs re-verification against this game build.");
                return matcher.InstructionEnumeration();
            }

            matcher.Advance(1);
            if (!matcher.IsValid || matcher.Opcode != OpCodes.Ldc_I4_S && matcher.Opcode != OpCodes.Ldc_I4)
            {
                WonderlandDebug.LogWarning("[PlayerCapGovernor] GetNrOfPlayers() call found, but the following instruction was not the expected constant - player cap left at vanilla default. Needs re-verification against this game build.");
                return matcher.InstructionEnumeration();
            }

            matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerCapGovernor), nameof(GetConfiguredCap))));
            WonderlandDebug.LogInfo("[PlayerCapGovernor] patched RPC_PeerInfo's player-count check to read the configured cap.");
            return matcher.InstructionEnumeration();
        }

        // Called from the patched IL in place of the vanilla literal 10.
        public static int GetConfiguredCap()
        {
            return WonderlandConfig.MaxPlayerCount?.Value ?? 10;
        }
    }
}
