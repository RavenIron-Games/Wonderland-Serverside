using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Per-station off switch for the production supply, flipped by a player standing next to the
    /// station with a vanilla emote (see EmoteSignals for why emotes and not chat). "Off" means
    /// Wonderland stops loading fuel and material into that kiln, smelter or fire; whatever is already
    /// inside burns out as vanilla would, and players can still feed it by hand. Nothing is moved, so
    /// there is nothing to lose.
    ///
    /// State lives in a mod-side file keyed by the station's ZDOID rather than on the station's own ZDO:
    /// the owning client rewrites that ZDO every second while the station runs, so a server write there
    /// races the owner, while a file only the server writes cannot lose. ZDOIDs persist with the world
    /// save; entries whose station no longer exists are pruned on every save.
    /// </summary>
    public static class SupplySwitch
    {
        private const float SaveInterval = 15f;

        private static readonly HashSet<ZDOID> _off = new HashSet<ZDOID>();
        private static bool _dirty;
        private static float _saveTimer;

        public static int Count => _off.Count;

        public static bool IsOff(ZDOID station)
        {
            return _off.Count > 0 && _off.Contains(station);
        }

        public static void Initialize()
        {
            Load();
            EmoteSignals.Register(OnEmote);
        }

        public static void OnUpdate(float dt)
        {
            if (!_dirty)
            {
                return;
            }
            _saveTimer += dt;
            if (_saveTimer >= SaveInterval)
            {
                Save();
            }
        }

        public static void Shutdown()
        {
            if (_dirty)
            {
                Save();
            }
        }

        private static void OnEmote(ConnectedCharacter who, string emote)
        {
            bool turnOff = EmoteSignals.Is(emote, WonderlandConfig.SupplyOffEmote?.Value, "nonono");
            bool turnOn = !turnOff && EmoteSignals.Is(emote, WonderlandConfig.SupplyOnEmote?.Value, "thumbsup");
            if (!turnOff && !turnOn)
            {
                return;
            }

            float range = Mathf.Clamp(WonderlandConfig.ControlRange?.Value ?? 5f, 1f, 20f);
            if (!ProductionSupplyEngine.TryFindNearestStation(who.Position, range, out ZDO station, out string name))
            {
                PlayerNotify.Toast(who, $"Wonderland: no kiln, smelter or fire within {range:0}m");
                return;
            }

            if (turnOff)
            {
                if (_off.Add(station.m_uid))
                {
                    _dirty = true;
                }
                PlayerNotify.Toast(who, $"{name}: auto-supply OFF");
                WonderlandDebug.LogAlways($"[SupplySwitch] {who.Name} switched {name} {station.m_uid} auto-supply OFF at {station.GetPosition():F0} ({_off.Count} station(s) off).");
            }
            else
            {
                if (_off.Remove(station.m_uid))
                {
                    _dirty = true;
                }
                PlayerNotify.Toast(who, $"{name}: auto-supply ON");
                WonderlandDebug.LogAlways($"[SupplySwitch] {who.Name} switched {name} {station.m_uid} auto-supply ON at {station.GetPosition():F0} ({_off.Count} station(s) off).");
            }
        }

        private static string FilePath()
        {
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "default";
            return Path.Combine(BepInEx.Paths.ConfigPath, $"Wonderland.SupplyOff.{world}.dat");
        }

        public static void Save()
        {
            _saveTimer = 0f;
            try
            {
                if (ZDOMan.instance != null)
                {
                    _off.RemoveWhere(id => ZDOMan.instance.GetZDO(id) == null);
                }

                string path = FilePath();
                string temp = path + ".tmp";
                var lines = new List<string>(_off.Count);
                foreach (ZDOID id in _off)
                {
                    lines.Add(id.UserID.ToString() + ":" + id.ID.ToString());
                }
                File.WriteAllLines(temp, lines);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
                _dirty = false;
                WonderlandDebug.LogInfo($"[SupplySwitch] saved {_off.Count} switched-off station(s) to {path}.");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[SupplySwitch] save failed: {ex.Message}");
            }
        }

        private static void Load()
        {
            _off.Clear();
            _dirty = false;
            try
            {
                string path = FilePath();
                if (!File.Exists(path))
                {
                    return;
                }
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                    {
                        continue;
                    }
                    if (long.TryParse(line.Substring(0, colon), out long user) && uint.TryParse(line.Substring(colon + 1), out uint id))
                    {
                        _off.Add(new ZDOID(user, id));
                    }
                }
                WonderlandDebug.LogAlways($"[SupplySwitch] loaded {_off.Count} switched-off station(s) from {path}.");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[SupplySwitch] load failed: {ex.Message}");
            }
        }
    }
}
