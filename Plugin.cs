using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Core.Compat;
using Wonderland.Subsystems.DiscordNotify;
using Wonderland.Subsystems.ItemFlow;
using Wonderland.Subsystems.Security;
using Wonderland.Subsystems.Storage;
using Wonderland.Subsystems.StatusEffects;
using Wonderland.Subsystems.WorldGovernor;

namespace Wonderland
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class WonderlandPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "wubarrk.wonderland";
        public const string ModName = "Wonderland";
        public const string ModVersion = "0.7.2";

        private const float ConfigPollInterval = 5f;

        public static WonderlandPlugin Instance { get; private set; } = null!;
        public static ConfigSync ConfigSync { get; private set; } = null!;

        private readonly Harmony _harmony = new Harmony(ModGUID);
        public SubsystemRegistry Subsystems { get; } = new SubsystemRegistry();

        private float _configPollTimer;
        private DateTime _configStamp;

        private void Awake()
        {
            Instance = this;
            WonderlandDebug.Init(Logger);

            WonderlandDebug.LogAlways($"Starting Wonderland v{ModVersion} (server-only, built for Valheim 1.0+)...");

            GameShape.Detect();

            ConfigSync = new ConfigSync(ModGUID)
            {
                DisplayName = ModName,
                CurrentVersion = ModVersion,
                MinimumRequiredVersion = ModVersion
            };

            WonderlandConfig.Bind(Config, ConfigSync);
            _configStamp = ReadConfigStamp();

            RegisterSubsystems();
            Subsystems.InitializeAll(Config, ConfigSync, _harmony);
            SubsystemRegistry.SafePatch(_harmony, typeof(WorldReadyHook));

            WonderlandDebug.LogAlways("Wonderland initialized successfully.");
        }

        private void RegisterSubsystems()
        {
            Subsystems.Register(new ItemFlowSubsystem());
            Subsystems.Register(new StorageSubsystem());
            Subsystems.Register(new WorldGovernorSubsystem());
            Subsystems.Register(new SecuritySubsystem());
            Subsystems.Register(new StatusEffectsSubsystem());
            Subsystems.Register(new DiscordNotifySubsystem());
        }

        private void Update()
        {
            PollConfigFile();
            Heartbeat.OnUpdate(UnityEngine.Time.deltaTime);
            Subsystems.OnUpdate();
        }

        private void OnDestroy()
        {
            Subsystems.ShutdownAll();
            _harmony.UnpatchSelf();
        }

        /// <summary>
        /// Admins edit the .cfg on a running server; pick the change up without a restart. Poll-based
        /// rather than a FileSystemWatcher - BepInEx.Configuration.ConfigFile has no built-in file
        /// watching of its own (confirmed against libs-Tools/BepInEx.dll directly: it exposes a
        /// ConfigReloaded event and a manual Reload() method, nothing that fires on an external edit),
        /// and this exact 5s-poll approach is already live-tested working on this same testbed by the
        /// sibling GetOffMyLawn mod (libs-Tools/IMPLEMENTATIONS/GetOffMyLawn.md section 7). SaveOnConfigSet
        /// is parked false during Reload() because every re-applied value would otherwise rewrite the
        /// file, bump its mtime, and trigger another reload on the very next poll. Every engine that
        /// already subscribes to a ConfigEntry's SettingChanged (WorldRatesEngine.Initialize, etc.) picks
        /// this up for free, since Reload() re-parses the file and raises SettingChanged for every entry
        /// whose value actually changed.
        /// </summary>
        private void PollConfigFile()
        {
            _configPollTimer += UnityEngine.Time.deltaTime;
            if (_configPollTimer < ConfigPollInterval)
            {
                return;
            }
            _configPollTimer = 0f;

            try
            {
                DateTime stamp = ReadConfigStamp();
                if (stamp == _configStamp)
                {
                    return;
                }

                bool save = Config.SaveOnConfigSet;
                Config.SaveOnConfigSet = false;
                try
                {
                    Config.Reload();
                }
                finally
                {
                    Config.SaveOnConfigSet = save;
                }
                _configStamp = ReadConfigStamp();
                WonderlandDebug.LogAlways("[Config] config file changed on disk - settings reloaded without a restart.");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[Config] hot-reload failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private DateTime ReadConfigStamp()
        {
            try
            {
                return File.Exists(Config.ConfigFilePath) ? File.GetLastWriteTimeUtc(Config.ConfigFilePath) : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// BepInEx's own Awake() runs long before ZNetScene/ObjectDB populate their prefab lists, so any
    /// subsystem setup that reads them (discovering container/fireplace/smelter/piece prefab names)
    /// has to wait for this instead - the first point those lists are actually filled in.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class WorldReadyHook
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            WonderlandPlugin.Instance.Subsystems.OnWorldReady();
        }
    }
}
