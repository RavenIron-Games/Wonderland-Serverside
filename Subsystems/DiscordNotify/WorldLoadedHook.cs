using HarmonyLib;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// "The world has finished loading" for a dedicated server. Decompile-verified: ZNet.Start calls
    /// the private parameterless ZNet.ServerLoadWorld, which synchronously runs LoadWorld (chunked
    /// .db2) or LoadOldWorld (legacy .db); each of those runs ZoneSystem.Load / LoadOld (GlobalKeyAdd
    /// per saved key), WorldSetup -&gt; ZoneSystem.SetStartingGlobalKeys and then OnWorldSaveLoaded.
    /// ServerLoadWorld is hooked rather than OnWorldSaveLoaded because both loaders skip
    /// OnWorldSaveLoaded entirely when the save file is missing ("missing ..." -&gt; WorldSetup -&gt;
    /// return, i.e. a brand-new world), while a ServerLoadWorld postfix runs after every path. It is
    /// far later than ZNetScene.Awake (the registry's OnWorldReady), which is why the boss snapshot
    /// and the "server is online" post live here and not there.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "ServerLoadWorld")]
    public static class WorldLoadedHook
    {
        [HarmonyPostfix]
        public static void Postfix(ZNet __instance)
        {
            if (!__instance.IsServer())
            {
                return;
            }

            DiscordNotifySubsystem.OnWorldLoaded();
        }
    }
}
