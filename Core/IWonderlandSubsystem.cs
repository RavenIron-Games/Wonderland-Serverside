using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;

namespace Wonderland.Core
{
    public interface IWonderlandSubsystem
    {
        string Name { get; }
        bool IsEnabled { get; }

        void Initialize(ConfigFile config, ConfigSync configSync, Harmony harmony);
        void OnUpdate();
        void OnGUI();
        void Shutdown();
    }
}
