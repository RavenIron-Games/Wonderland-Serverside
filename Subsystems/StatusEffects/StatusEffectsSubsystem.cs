using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.StatusEffects
{
    public class StatusEffectsSubsystem : IWonderlandSubsystem
    {
        public string Name => "StatusEffects";
        public bool IsEnabled => true;

        public void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
        }

        public void OnWorldReady()
        {
            BuffRosterEngine.OnWorldReady();
        }

        public void OnUpdate()
        {
            BuffRosterEngine.OnUpdate(Time.deltaTime);
        }

        public void Shutdown()
        {
        }
    }
}
