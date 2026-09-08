using Wonderland.Core;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// A distinct security/audit log channel, separate from routine debug noise (which is gated
    /// behind VerboseLogging and silent by default) - this always prints, so an admin can grep one
    /// place for actual anomalies instead of wading through normal operation logs. Every flag names
    /// who it's about; when that can't be resolved it says "Unknown" rather than silently folding into
    /// a category that reads as "caught cheating" (same distinction CONSOLE-COMMAND-ROUTING-FACTS.md
    /// already draws for admin checks - an unresolved identity is a different fact than an unauthorized one).
    /// </summary>
    public static class AuditLog
    {
        public static void Flag(string category, string who, string detail)
        {
            string identity = string.IsNullOrEmpty(who) ? "Unknown" : who;
            WonderlandDebug.LogAlways($"[SECURITY:{category}] {identity} - {detail}");
        }
    }
}
