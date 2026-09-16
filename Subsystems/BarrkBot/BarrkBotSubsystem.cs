using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.BarrkBot
{
    /// <summary>
    /// Publishes what this server knows to BarrkBOT (the community Discord bot on the same box) as
    /// BepInEx/config/Wonderland/barrkbot_wonderland.json, on a timer, from server-observed state only.
    /// No Harmony patches of its own: every counter is fed from the hook the fact already passes through
    /// (PeerJoinLeaveHook, PlayerLifecycleWatch, BossDefeatWatch, ItemLedger, AuditLog, the governors).
    /// The registry loads lazily on the first update where the world name is known, which is the same
    /// "retry until the singleton exists" shape libs-Tools/IMPLEMENTATIONS/Wonderland.md §4 recommends.
    /// </summary>
    public class BarrkBotSubsystem : IWonderlandSubsystem
    {
        private const float SweepSeconds = 5f;

        public string Name => "BarrkBot";
        public bool IsEnabled => true;

        private float _sweepTimer;
        private float _writeTimer;
        private bool _announced;
        private bool? _lastEnabled; // null until the first update, so boot itself is an edge
        private bool _resync;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
        }

        public void OnWorldReady()
        {
        }

        /// <summary>BarrkBotWriteSeconds with the 10 s floor the writer applies - also what the export declares.</summary>
        public static float EffectiveWriteSeconds()
        {
            return UnityEngine.Mathf.Max(10f, WonderlandConfig.BarrkBotWriteSeconds?.Value ?? 60f);
        }

        public void OnUpdate()
        {
            bool enabled = BarrkBotStats.Enabled;
            if (enabled != _lastEnabled)
            {
                bool wasOn = _lastEnabled == true;
                _lastEnabled = enabled;
                if (!enabled)
                {
                    // Off - at boot or at runtime: no file may be left behind saying the server is live (the
                    // reader would keep serving it for an hour). Bindings are kept; what was recorded is saved.
                    BarrkBotStats.MarkAllOffline();
                    if (wasOn) BarrkBotStats.SaveIfDirty(force: true);
                    BarrkBotExport.Remove();
                    WonderlandDebug.LogAlways($"[BarrkBot] BarrkBotExportEnabled is off - {BarrkBotExport.FilePath()} removed if present; counters so far are kept in {BarrkBotStats.FilePath()}.");
                }
                else
                {
                    _resync = true; // sweep first, then write, so the first file agrees with itself
                }
            }
            if (!enabled)
            {
                return;
            }
            if (!BarrkBotStats.TryLoad())
            {
                return;
            }

            float dt = UnityEngine.Time.deltaTime;
            _sweepTimer += dt;
            _writeTimer += dt;

            if (_resync)
            {
                _resync = false;
                BarrkBotStats.Sweep(0f, ConnectedCharacters.All()); // bind who is here now, credit no time
                _sweepTimer = 0f;
                _writeTimer = float.MaxValue;
            }
            else if (_sweepTimer >= SweepSeconds)
            {
                BarrkBotStats.Sweep(_sweepTimer, ConnectedCharacters.All());
                _sweepTimer = 0f;
            }

            float writeSeconds = EffectiveWriteSeconds();
            if (_writeTimer >= writeSeconds)
            {
                _writeTimer = 0f;
                BarrkBotExport.Write();
                BarrkBotStats.SaveIfDirty();
                if (!_announced)
                {
                    _announced = true;
                    WonderlandDebug.LogAlways($"[BarrkBot] exporting to {BarrkBotExport.FilePath()} every {writeSeconds:0} s (BarrkBotWriteSeconds); registry {BarrkBotStats.FilePath()}.");
                }
            }
        }

        /// <summary>Plugin.OnDestroy. By now Game.OnApplicationQuit -> ZNet.Shutdown -> StopAll has disposed every
        /// peer without going through ZNet.Disconnect, so there is nothing to sweep: credit the partial
        /// interval from the last bindings, mark everyone offline, write the export once more saying so,
        /// and save.</summary>
        public void Shutdown()
        {
            if (!BarrkBotStats.Loaded)
            {
                return;
            }
            BarrkBotStats.OnShutdown(_sweepTimer);
            if (BarrkBotStats.Enabled)
            {
                BarrkBotExport.Write(stopping: true);
            }
            BarrkBotStats.SaveIfDirty(force: true);
        }
    }
}
