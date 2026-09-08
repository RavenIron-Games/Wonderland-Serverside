using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.HUDControl
{
    public static class TelemetryData
    {
        public static float MovementSpeed { get; private set; }
        public static float CurrentHP { get; private set; }
        public static float MaxHP { get; private set; }
        public static float CurrentStamina { get; private set; }
        public static float MaxStamina { get; private set; }
        public static float CurrentEitr { get; private set; }
        public static float MaxEitr { get; private set; }
        public static float TotalArmor { get; private set; }
        public static Vector3 Position { get; private set; }
        public static string BiomeName { get; private set; } = "Unknown";
        public static string WeatherName { get; private set; } = "Clear";
        public static float WindAngle { get; private set; }
        public static float WindSpeed { get; private set; }

        private static Vector3 _lastPos;
        private static float _lastTime;

        public static void UpdateTelemetry()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            CurrentHP = SafeMath.Safe(player.GetHealth());
            MaxHP = SafeMath.Safe(player.GetMaxHealth());
            CurrentStamina = SafeMath.Safe(player.GetStamina());
            MaxStamina = SafeMath.Safe(player.GetMaxStamina());
            CurrentEitr = SafeMath.Safe(player.GetEitr());
            MaxEitr = SafeMath.Safe(player.GetMaxEitr());
            TotalArmor = SafeMath.Safe(player.GetBodyArmor());
            Position = player.transform.position;

            float dt = Time.time - _lastTime;
            if (dt > 0.1f)
            {
                MovementSpeed = SafeMath.Safe(Vector3.Distance(player.transform.position, _lastPos) / dt);
                _lastPos = player.transform.position;
                _lastTime = Time.time;
            }

            if (WorldGenerator.instance != null)
            {
                BiomeName = WorldGenerator.instance.GetBiome(Position).ToString();
            }

            if (EnvMan.instance != null)
            {
                WeatherName = EnvMan.instance.GetCurrentEnvironment()?.m_name ?? "Clear";
                WindSpeed = SafeMath.Safe(EnvMan.instance.GetWindIntensity());
                WindAngle = SafeMath.Safe(EnvMan.instance.GetWindDir().y);
            }
        }
    }
}
