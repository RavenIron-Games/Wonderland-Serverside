using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.HUDControl
{
    public class HUDSubsystem : IWonderlandSubsystem
    {
        public string Name => "HUDControl";
        public bool IsEnabled => WonderlandConfig.EnableHUD != null && WonderlandConfig.EnableHUD.Value;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            WonderlandHUDManager.Init();
        }

        public void OnUpdate()
        {
            if (WonderlandConfig.HUDHotkey != null && Input.GetKeyDown(WonderlandConfig.HUDHotkey.Value))
            {
                if (WonderlandConfig.EnableHUD != null)
                {
                    WonderlandConfig.EnableHUD.Value = !WonderlandConfig.EnableHUD.Value;
                    WonderlandDebug.LogInfo($"HUD visibility toggled: {WonderlandConfig.EnableHUD.Value}");
                }
            }

            TelemetryData.UpdateTelemetry();
        }

        public void OnGUI()
        {
            WonderlandHUDManager.OnGUI();
        }

        public void Shutdown() { }
    }
}
