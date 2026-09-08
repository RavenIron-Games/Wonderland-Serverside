using System;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.HUDControl
{
    public static class WonderlandHUDManager
    {
        private static Rect _hudRect = new Rect(20, 180, 280, 320);
        private static bool _stylesCreated = false;
        private static GUIStyle _boxStyle = null!;
        private static GUIStyle _headerStyle = null!;
        private static GUIStyle _labelStyle = null!;

        private static bool _isDragging = false;
        private static Vector2 _dragOffset;

        public static void Init()
        {
            float posX = WonderlandConfig.HUDPositionX?.Value ?? 20f;
            float posY = WonderlandConfig.HUDPositionY?.Value ?? 180f;
            _hudRect = new Rect(posX, posY, 280, 320);
        }

        public static void OnGUI()
        {
            if (WonderlandConfig.EnableHUD == null || !WonderlandConfig.EnableHUD.Value) return;
            if (Player.m_localPlayer == null) return;

            CreateStylesIfNeeded();
            HandleDragging();

            GUI.Box(_hudRect, GUIContent.none, _boxStyle);

            GUILayout.BeginArea(_hudRect);
            GUILayout.BeginVertical();
            
            GUILayout.Label("── WONDERLAND TELEMETRY ──", _headerStyle);
            GUILayout.Space(4);

            if (WonderlandConfig.ShowMovementSpeed != null && WonderlandConfig.ShowMovementSpeed.Value)
            {
                GUILayout.Label($"Speed: {TelemetryData.MovementSpeed:F1} m/s", _labelStyle);
            }

            if (WonderlandConfig.ShowArmor != null && WonderlandConfig.ShowArmor.Value)
            {
                GUILayout.Label($"Armor: {TelemetryData.TotalArmor:F0}", _labelStyle);
            }

            if (WonderlandConfig.ShowPureStats != null && WonderlandConfig.ShowPureStats.Value)
            {
                GUILayout.Label($"Health: {TelemetryData.CurrentHP:F0} / {TelemetryData.MaxHP:F0}", _labelStyle);
                GUILayout.Label($"Stamina: {TelemetryData.CurrentStamina:F0} / {TelemetryData.MaxStamina:F0}", _labelStyle);
                if (TelemetryData.MaxEitr > 0)
                {
                    GUILayout.Label($"Eitr: {TelemetryData.CurrentEitr:F0} / {TelemetryData.MaxEitr:F0}", _labelStyle);
                }
            }

            if (WonderlandConfig.ShowEnvironmentInfo != null && WonderlandConfig.ShowEnvironmentInfo.Value)
            {
                GUILayout.Label($"Biome: {TelemetryData.BiomeName}", _labelStyle);
                GUILayout.Label($"Weather: {TelemetryData.WeatherName}", _labelStyle);
                GUILayout.Label($"Pos: {TelemetryData.Position.x:F0}, {TelemetryData.Position.z:F0}", _labelStyle);
            }

            if (WonderlandConfig.ShowWindVector != null && WonderlandConfig.ShowWindVector.Value)
            {
                GUILayout.Label($"Wind: {TelemetryData.WindSpeed * 100f:F0}%", _labelStyle);
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static void CreateStylesIfNeeded()
        {
            if (_stylesCreated) return;
            _stylesCreated = true;

            Texture2D backgroundTex = new Texture2D(1, 1);
            backgroundTex.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.12f, 0.85f));
            backgroundTex.Apply();

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = backgroundTex;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
            _headerStyle.normal.textColor = new Color(0.95f, 0.85f, 0.45f);

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12
            };
            _labelStyle.normal.textColor = Color.white;
        }

        private static void HandleDragging()
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDown && _hudRect.Contains(e.mousePosition))
            {
                _isDragging = true;
                _dragOffset = e.mousePosition - new Vector2(_hudRect.x, _hudRect.y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _isDragging)
            {
                _hudRect.x = e.mousePosition.x - _dragOffset.x;
                _hudRect.y = e.mousePosition.y - _dragOffset.y;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _isDragging)
            {
                _isDragging = false;
                if (WonderlandConfig.HUDPositionX != null) WonderlandConfig.HUDPositionX.Value = _hudRect.x;
                if (WonderlandConfig.HUDPositionY != null) WonderlandConfig.HUDPositionY.Value = _hudRect.y;
                e.Use();
            }
        }
    }
}
