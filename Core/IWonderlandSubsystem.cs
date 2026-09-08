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

        /// <summary>
        /// Fires once, from a Harmony postfix on ZNetScene.Awake - the first point at which
        /// ZNetScene.instance.m_prefabs/ObjectDB.instance are actually populated. BepInEx's own
        /// Awake() (and therefore Initialize above) runs far earlier, before any world data exists,
        /// so prefab-dependent setup (discovering container/fireplace/smelter prefab names, etc.)
        /// belongs here instead - never in Initialize.
        /// </summary>
        void OnWorldReady();

        void OnUpdate();
        void Shutdown();
    }
}
