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
    ///    against a generous configured ceiling. Suppressed for legitimate transits: portal
    ///    travel, dungeon interior entry/exit, admin teleports, and respawn after death.
    ///  - Fly/noclip: WorldGenerator.GetHeight(x,z) (pure noise, works with zero terrain loaded -
    ///    the same function the starter boat uses to find water) compared against reported Y. A
    ///    persistent large gap flags likely flight or noclip; mining/caving produces real negative
    ///    deltas too, so the tolerance here is deliberately generous and this is a signal to review,
    ///    not an accusation. Skipped in dungeon interiors where players reside high above terrain.
    /// </summary>
    public static class PositionWatch
    {
        private static float _timer;
        private static readonly Dictionary<ZDOID, (Vector3 pos, float time)> _lastSample = new Dictionary<ZDOID, (Vector3, float)>();
        private static readonly HashSet<ZDOID> _seenThisPass = new HashSet<ZDOID>();
        private static readonly HashSet<ZDOID> _hasBeenGrounded = new HashSet<ZDOID>();
        private static readonly HashSet<ZDOID> _wasDead = new HashSet<ZDOID>();
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

                bool isDead = character.Zdo.GetBool(ZDOVars.s_dead);
                bool justRespawned = _wasDead.Remove(uid);
                if (isDead)
                {
                    _wasDead.Add(uid);
                }

                if (_lastSample.TryGetValue(uid, out (Vector3 pos, float time) last))
                {
                    float elapsed = now - last.time;
                    if (elapsed > 0.01f)
                    {
                        float speed = Vector3.Distance(pos, last.pos) / elapsed;
                        if (speed > speedCeiling)
                        {
                            if (!isDead && !justRespawned && !IsLegitimateTransit(character, last.pos, pos))
                            {
                                AuditLog.Flag("PositionWatch", name, $"moved {speed:F1} m/s over {elapsed:F1}s, exceeds ceiling {speedCeiling} m/s.");
                            }
                        }
                    }
                }
                _lastSample[uid] = (pos, now);

                if (WorldGenerator.instance != null && !Character.InInterior(pos) && pos.y < 2000f)
                {
                    float groundHeight = WorldGenerator.instance.GetHeight(pos.x, pos.z);
                    float above = pos.y - groundHeight;
                    if (above <= heightTolerance)
                    {
                        _hasBeenGrounded.Add(uid);
                    }
                    else if (_hasBeenGrounded.Contains(uid))
                    {
                        AuditLog.Flag("PositionWatch", name, $"reported Y {pos.y:F1} is {above:F1}m above expected ground height {groundHeight:F1} - possible fly/noclip.");
                    }

                    // Nothing is flagged until the character has been seen on the ground at least once
                    // this session. The Valkyrie intro carries a brand-new character hundreds of metres
                    // up before it ever touches down, which flagged three fly/noclip warnings against a
                    // player who had not yet landed - the people least likely to be cheating.
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
                _hasBeenGrounded.Remove(uid);
                _wasDead.Remove(uid);
            }
        }

        private static bool IsLegitimateTransit(ConnectedCharacter character, Vector3 fromPos, Vector3 toPos)
        {
            // 1. Admin teleport / devcommands
            if (character.Peer != null && ZNet.instance != null)
            {
                string? host = character.Peer.m_socket?.GetHostName();
                if (!string.IsNullOrEmpty(host) && ZNet.instance.IsAdmin(host))
                {
                    return true;
                }
            }

            // 2. Dungeon / Interior entry or exit (interiors reside at y > 3000m)
            if (Character.InInterior(fromPos) != Character.InInterior(toPos) ||
                (fromPos.y > 2000f != toPos.y > 2000f) ||
                Mathf.Abs(toPos.y - fromPos.y) > 1000f)
            {
                return true;
            }

            // 3. Portal transit
            if (IsPortalTransit(fromPos, toPos))
            {
                return true;
            }

            return false;
        }

        private static bool IsPortalTransit(Vector3 fromPos, Vector3 toPos, float radius = 40f)
        {
            if (ZDOMan.instance == null)
            {
                return false;
            }

            var portals = ZDOMan.instance.GetPortals();
            if (portals == null || portals.Count == 0)
            {
                return false;
            }

            float rSqr = radius * radius;
            ZDO? nearFrom = null;
            ZDO? nearTo = null;

            foreach (var kvp in portals)
            {
                List<ZDO> list = kvp.Value;
                if (list == null) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    ZDO portal = list[i];
                    if (portal == null || !portal.IsValid()) continue;

                    Vector3 portalPos = portal.GetPosition();

                    if (nearFrom == null && Vector3.SqrMagnitude(portalPos - fromPos) <= rSqr)
                    {
                        nearFrom = portal;
                    }

                    if (nearTo == null && Vector3.SqrMagnitude(portalPos - toPos) <= rSqr)
                    {
                        nearTo = portal;
                    }

                    if (nearFrom != null && nearTo != null)
                    {
                        return true;
                    }
                }
            }

            // Extended check: If one endpoint was near a portal, check if its linked target
            // portal is near the other endpoint (within 60m to account for sprinting before/after sample).
            const float extendedRSqr = 60f * 60f;

            if (nearFrom != null)
            {
                ZDOID targetID = nearFrom.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                if (targetID != ZDOID.None)
                {
                    ZDO target = ZDOMan.instance.GetZDO(targetID);
                    if (target != null && Vector3.SqrMagnitude(target.GetPosition() - toPos) <= extendedRSqr)
                    {
                        return true;
                    }
                }
            }

            if (nearTo != null)
            {
                ZDOID sourceID = nearTo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
                if (sourceID != ZDOID.None)
                {
                    ZDO source = ZDOMan.instance.GetZDO(sourceID);
                    if (source != null && Vector3.SqrMagnitude(source.GetPosition() - fromPos) <= extendedRSqr)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
