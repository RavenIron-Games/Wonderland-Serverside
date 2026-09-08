using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace Wonderland.Core
{
    public class SubsystemRegistry
    {
        private readonly List<IWonderlandSubsystem> _subsystems = new List<IWonderlandSubsystem>();
        private bool _initialized = false;
        private bool _worldReady = false;

        public void Register(IWonderlandSubsystem subsystem)
        {
            if (!_subsystems.Contains(subsystem))
            {
                _subsystems.Add(subsystem);
            }
        }

        public void InitializeAll(ConfigFile config, ConfigSync configSync, Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            foreach (var subsystem in _subsystems)
            {
                try
                {
                    WonderlandDebug.LogInfo($"Initializing subsystem: {subsystem.Name}...");
                    subsystem.Initialize(config, configSync, harmony);
                    WonderlandDebug.LogInfo($"Subsystem {subsystem.Name} initialized successfully.");
                }
                catch (Exception ex)
                {
                    WonderlandDebug.LogError($"Error initializing subsystem '{subsystem.Name}': {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        /// <summary>Fires once, from a Harmony postfix on ZNetScene.Awake - see IWonderlandSubsystem.OnWorldReady.</summary>
        public void OnWorldReady()
        {
            if (!_initialized || _worldReady) return;
            _worldReady = true;

            foreach (var subsystem in _subsystems)
            {
                try
                {
                    subsystem.OnWorldReady();
                }
                catch (Exception ex)
                {
                    WonderlandDebug.LogError($"Error in subsystem '{subsystem.Name}' OnWorldReady: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public void OnUpdate()
        {
            if (!_initialized) return;

            foreach (var subsystem in _subsystems)
            {
                if (!subsystem.IsEnabled) continue;
                try
                {
                    subsystem.OnUpdate();
                }
                catch (Exception ex)
                {
                    WonderlandDebug.LogError($"Error in subsystem '{subsystem.Name}' OnUpdate: {ex.Message}");
                }
            }
        }

        public void ShutdownAll()
        {
            foreach (var subsystem in _subsystems)
            {
                try
                {
                    subsystem.Shutdown();
                }
                catch (Exception ex)
                {
                    WonderlandDebug.LogError($"Error shutting down subsystem '{subsystem.Name}': {ex.Message}");
                }
            }
            _subsystems.Clear();
            _initialized = false;
            _worldReady = false;
        }

        public static bool SafePatch(Harmony harmony, Type patchType)
        {
            try
            {
                harmony.PatchAll(patchType);
                WonderlandDebug.LogInfo($"Successfully applied Harmony patch set: {patchType.Name}");
                return true;
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"Failed to apply Harmony patch set '{patchType.Name}': {ex.Message}");
                return false;
            }
        }
    }
}
