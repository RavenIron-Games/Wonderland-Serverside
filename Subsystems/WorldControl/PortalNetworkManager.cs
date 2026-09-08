using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.WorldControl
{
    public static class PortalNetworkManager
    {
        private static bool _showMenu = false;
        private static TeleportWorld? _activePortal = null;
        private static List<string> _availableDestinations = new List<string>();
        private static Rect _menuRect = new Rect(Screen.width / 2 - 160, Screen.height / 2 - 200, 320, 400);

        public static void Init() { }

        public static void OpenPortalMenu(TeleportWorld portal)
        {
            if (WonderlandConfig.SinglePortalDialingEnabled == null || !WonderlandConfig.SinglePortalDialingEnabled.Value) return;

            _activePortal = portal;
            _availableDestinations.Clear();

            var zdos = ZDOMan.instance?.GetPortals();
            if (zdos != null)
            {
                foreach (var zdo in zdos)
                {
                    string tag = zdo.GetString(ZDOVars.s_tag);
                    if (!string.IsNullOrEmpty(tag) && !_availableDestinations.Contains(tag))
                    {
                        _availableDestinations.Add(tag);
                    }
                }
            }

            _showMenu = true;
            WonderlandDebug.LogInfo($"Opened single-portal destination menu for portal. Destinations found: {_availableDestinations.Count}");
        }

        public static void CloseMenu()
        {
            _showMenu = false;
            _activePortal = null;
        }

        public static void OnGUI()
        {
            if (!_showMenu || _activePortal == null) return;

            GUI.Box(_menuRect, "── SELECT PORTAL DESTINATION ──");
            GUILayout.BeginArea(_menuRect);
            GUILayout.Space(25);

            if (_availableDestinations.Count == 0)
            {
                GUILayout.Label("No other connected portals found.");
            }
            else
            {
                foreach (var dest in _availableDestinations)
                {
                    if (GUILayout.Button($"Dial: {dest}", GUILayout.Height(30)))
                    {
                        DialDestination(dest);
                        CloseMenu();
                    }
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                CloseMenu();
            }
            GUILayout.EndArea();
        }

        private static void DialDestination(string targetTag)
        {
            if (_activePortal == null) return;
            ZNetView nview = _activePortal.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                nview.GetZDO().Set(ZDOVars.s_tag, targetTag);
                WonderlandDebug.LogInfo($"Dialed portal target tag to: '{targetTag}'");
            }
        }
    }
}
