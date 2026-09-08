using BepInEx;
using HarmonyLib;
using ServerSync;
using Wonderland.Core;
using Wonderland.Subsystems.ItemFlow;
using Wonderland.Subsystems.Security;
using Wonderland.Subsystems.Storage;
using Wonderland.Subsystems.Vitality;
using Wonderland.Subsystems.WorldGovernor;

namespace Wonderland
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class WonderlandPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "wubarrk.wonderland";
        public const string ModName = "Wonderland";
        public const string ModVersion = "2.0.0";

        public static WonderlandPlugin Instance { get; private set; } = null!;
        public static ConfigSync ConfigSync { get; private set; } = null!;

        private readonly Harmony _harmony = new Harmony(ModGUID);
        public SubsystemRegistry Subsystems { get; } = new SubsystemRegistry();

        private void Awake()
        {
            Instance = this;
            WonderlandDebug.Init(Logger);

            WonderlandDebug.LogAlways($"Starting Wonderland v{ModVersion} (server-only rebuild)...");

            ConfigSync = new ConfigSync(ModGUID)
            {
                DisplayName = ModName,
                CurrentVersion = ModVersion,
                MinimumRequiredVersion = ModVersion
            };

            WonderlandConfig.Bind(Config, ConfigSync);

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
            Subsystems.Register(new VitalitySubsystem());
            Subsystems.Register(new SecuritySubsystem());
        }

        private void Update()
        {
            Subsystems.OnUpdate();
        }

        private void OnDestroy()
        {
            Subsystems.ShutdownAll();
            _harmony.UnpatchSelf();
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
