using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Detect-only (never corrected - rubber-banding a player back on a guess is worse than the
    /// problem it solves). Two independent signals, both read off each connected player's character
    /// ZDO (ConnectedCharacters - the position there is refreshed every physics tick by the owning
    /// client's ZSyncTransform; Player.GetAllPlayers() is always empty on a dedicated server):
    ///  - Speed: distance covered between two position samples divided by elapsed time, flagged
    ///    against a generous configured ceiling.
    ///  - Fly/noclip: WorldGenerator.GetHeight(x,z) (pure noise, works with zero terrain loaded -
    ///    the same function the starter boat uses to find water) compared against reported Y. A
    ///    persistent large gap flags likely flight or noclip; mining/caving produces real negative
    ///    deltas too, so the tolerance here is deliberately generous and this is a signal to review,
    ///    not an accusation.
    /// </summary>
    public static class PositionWatch
    {
        private static float _timer;
        private static readonly Dictionary<ZDOID, (Vector3 pos, float time)> _lastSample = new Dictionary<ZDOID, (Vector3, float)>();
        private static readonly HashSet<ZDOID> _seenThisPass = new HashSet<ZDOID>();
        private static readonly List<ZDOID> _stale = new List<ZDOID>();

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.PositionWatchEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.PositionWatchInterval?.Value ?? 3f))
            {
                return;
            }
            _timer = 0f;

            float speedCeiling = WonderlandConfig.SpeedPlausibilityCeiling?.Value ?? 40f;
            float heightTolerance = WonderlandConfig.FlyDetectionTolerance?.Value ?? 15f;
            float now = Time.time;
            _seenThisPass.Clear();

            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                ZDOID uid = character.Zdo.m_uid;
                Vector3 pos = character.Position;
                string name = character.Name;
                _seenThisPass.Add(uid);

                if (_lastSample.TryGetValue(uid, out (Vector3 pos, float time) last))
                {
                    float elapsed = now - last.time;
                    if (elapsed > 0.01f)
                    {
                        float speed = Vector3.Distance(pos, last.pos) / elapsed;
                        if (speed > speedCeiling)
                        {
                            AuditLog.Flag("PositionWatch", name, $"moved {speed:F1} m/s over {elapsed:F1}s, exceeds ceiling {speedCeiling} m/s.");
                        }
                    }
                }
                _lastSample[uid] = (pos, now);

                if (WorldGenerator.instance != null)
                {
                    float groundHeight = WorldGenerator.instance.GetHeight(pos.x, pos.z);
                    if (pos.y - groundHeight > heightTolerance)
                    {
                        AuditLog.Flag("PositionWatch", name, $"reported Y {pos.y:F1} is {pos.y - groundHeight:F1}m above expected ground height {groundHeight:F1} - possible fly/noclip.");
                    }
                }
            }

            // Character ZDOs are per-session, so a disconnected player's sample would otherwise sit
            // here forever; drop whatever wasn't seen this pass.
            _stale.Clear();
            foreach (ZDOID uid in _lastSample.Keys)
            {
                if (!_seenThisPass.Contains(uid))
                {
                    _stale.Add(uid);
                }
            }
            foreach (ZDOID uid in _stale)
            {
                _lastSample.Remove(uid);
            }
        }
    }
}
