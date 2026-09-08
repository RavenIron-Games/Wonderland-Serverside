using BepInEx.Logging;
using Wonderland.Core;

namespace Wonderland.Core
{
    public static class WonderlandDebug
    {
        private static ManualLogSource? _logSource;

        public static void Init(ManualLogSource logSource)
        {
            _logSource = logSource;
        }

        public static void LogInfo(string message)
        {
            if (WonderlandConfig.VerboseLogging != null && !WonderlandConfig.VerboseLogging.Value) return;
            _logSource?.LogInfo($"[Wonderland] {message}");
        }

        public static void LogWarning(string message)
        {
            _logSource?.LogWarning($"[Wonderland] {message}");
        }

        public static void LogError(string message)
        {
            _logSource?.LogError($"[Wonderland] {message}");
        }

        public static void LogAlways(string message)
        {
            _logSource?.LogMessage($"[Wonderland] {message}");
        }
    }
}
