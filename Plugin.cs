using System;
using BepInEx;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Subsystems.BuildingControl;
using Wonderland.Subsystems.CraftingControl;
using Wonderland.Subsystems.HUDControl;
using Wonderland.Subsystems.InventoryControl;
using Wonderland.Subsystems.PlayerControl;
using Wonderland.Subsystems.ProductionControl;
using Wonderland.Subsystems.WorldControl;

namespace Wonderland
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class WonderlandPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "wubarrk.wonderland";
        public const string ModName = "Wonderland";
        public const string ModVersion = "1.0.0";

        public static WonderlandPlugin Instance { get; private set; } = null!;
        public static ConfigSync ConfigSync { get; private set; } = null!;

        private readonly Harmony _harmony = new Harmony(ModGUID);
        private readonly SubsystemRegistry _subsystems = new SubsystemRegistry();

        private void Awake()
        {
            Instance = this;
            WonderlandDebug.Init(Logger);

            WonderlandDebug.LogAlways($"Starting Wonderland v{ModVersion} core loader...");

            ConfigSync = new ConfigSync(ModGUID)
            {
                DisplayName = ModName,
                CurrentVersion = ModVersion,
                MinimumRequiredVersion = ModVersion
            };

            WonderlandConfig.Bind(Config, ConfigSync);

            RegisterSubsystems();
            _subsystems.InitializeAll(Config, ConfigSync, _harmony);

            WonderlandDebug.LogAlways("Wonderland initialized successfully.");
        }

        private void RegisterSubsystems()
        {
            _subsystems.Register(new BuildingSubsystem());
            _subsystems.Register(new InventorySubsystem());
            _subsystems.Register(new CraftingSubsystem());
            _subsystems.Register(new ProductionSubsystem());
            _subsystems.Register(new PlayerSubsystem());
            _subsystems.Register(new HUDSubsystem());
            _subsystems.Register(new WorldSubsystem());
        }

        private void Update()
        {
            _subsystems.OnUpdate();
        }

        private void OnGUI()
        {
            _subsystems.OnGUI();
        }

        private void OnDestroy()
        {
            _subsystems.ShutdownAll();
            _harmony.UnpatchSelf();
        }
    }

    [HarmonyPatch]
    public static class WonderlandRPCRegistrationPatch
    {
        [HarmonyPatch(typeof(ZNet), "Awake")]
        [HarmonyPostfix]
        public static void Postfix_ZNetAwake()
        {
            WonderlandRPC.RegisterRPCs();
        }
    }
}
