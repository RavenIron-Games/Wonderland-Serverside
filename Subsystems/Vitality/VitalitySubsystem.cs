using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Vitality
{
    public class VitalitySubsystem : IWonderlandSubsystem
    {
        public string Name => "Vitality";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
        }

        public void OnWorldReady()
        {
        }

        public void OnUpdate()
        {
            VitalityGovernor.OnUpdate(Time.deltaTime);
        }

        public void Shutdown()
        {
        }
    }
}
