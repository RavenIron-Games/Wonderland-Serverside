using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;
using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// Keeps Fireplace-family (torch/hearth/campfire/sconce) and Smelter-family (smelter/blast
    /// furnace/kiln) fed from linked containers within a configurable range, with a reserve floor so
    /// a source container's matching stock is never fully drained. Runs independent of player
    /// proximity or online status - it operates purely on ZDO fields (ZDOVars.s_fuel, s_queued, and
    /// Smelter's dynamic "item0".."itemN-1" ore-queue keys), verified directly against the decompile,
    /// never a live GameObject - so a base doesn't go dark because its owner logged off.
    /// Two distinct resources for Smelter-family, not one: fuel (what keeps it burning) and the ore/
    /// process-material queue (what's actually being converted) are separate ZDO fields with
    /// separate caps (m_maxFuel vs m_maxOre).
    ///
    /// Two player-facing controls sit on top: a station a player has switched off (SupplySwitch) is
    /// skipped entirely, and a charcoal kiln - any smelter-family station whose only product is Coal -
    /// is only ever loaded with the wood types listed in KilnWoodTypes, so fine wood, core wood and
    /// blackwood in a linked chest are not quietly turned into coal. Both only govern what this engine
    /// loads; a player feeding a station by hand is vanilla and untouched.
    ///
    /// How a fed unit reaches the station (0.8.0, after a live "the smelter ate my silver" report): the
    /// server NEVER takes ownership of a station. Up to 0.7.2 every feed did zdo.SetOwner(server) first,
    /// and while the server owned a smelter a nearby player's own hand-feed was lost - Smelter.OnAddOre
    /// removes the ore from the inventory and then m_nview.InvokeRPC("RPC_AddOre"), which ZNetView routes
    /// to the ZDO's owner; a dedicated server has no live Smelter instance (its reference position is
    /// pinned at 1e6, ZNetScene only instantiates around it), so ZRoutedRpc.HandleRoutedRPC found no
    /// instance and dropped the RPC without a log line on either side (VALHEIM-DEDICATED-SERVER-FACTS.md,
    /// "a routed RPC aimed at a ZDO with no local instance is dropped, silently"). Same for RPC_AddFuel and
    /// Fireplace.RPC_AddFuel. ResolveDelivery now picks one of three machines per visit:
    ///
    /// OwnerRpc - a connected peer owns the ZDO, their client instantiates its zone (their own validated
    /// simulation distance around their character's synced position - no margin, and no character means no
    /// instance), AND they are provably ticking it: the owner's own Smelter.UpdateSmelter
    /// writes ZDOVars.s_startTime (Fireplace: s_lastTime) with the synced ZNet.GetTime() ticks every 1 s
    /// (2 s), under IsOwner, so a stamp newer than the current ownership record and younger than about two
    /// ticks proves a live instance that will accept vanilla's own RPC_AddOre / RPC_AddFuel (decompile-
    /// verified free of client-only singletons). Position and stamp are both required: a stamp alone stays
    /// fresh for a tick or two after a portal jump or crash, when the instance is already gone.
    ///
    /// Direct - nobody simulates the ZDO: unowned (owner 0 with OwnerRevision 0, i.e. never owned this
    /// uptime), owned by this server, owned by a session that has left (a client's ZDO copies die with its
    /// ZNet; Valheim never releases a persistent ZDO on disconnect, HEADLESS-AND-EMPTY-SERVER-FACTS.md §3),
    /// or owned by a connected peer who is out of instance range with a stale stamp (vanilla never strips
    /// that ownership - ReleaseNearbyZDOS only walks the owner's CURRENT block, so after a portal jump the
    /// station stays theirs until someone else comes by). ZDO.Set bumps DataRevision unconditionally (only
    /// SetPosition is owner-gated) and ZDOPeer.ShouldSend compares revisions alone, so the write replicates
    /// to whoever loads the area next. The far-owner case has one more step: that client still holds its
    /// own copy of the ZDO, marked owner, and would re-instantiate from it on return - a smelter's catch-up
    /// tick then emits hundreds of revisions from the STALE queue/fuel and ZDOMan.RPC_ZDOData accepts any
    /// higher DataRevision, wiping everything fed in the meantime. So every Direct write to a far-owned
    /// station is followed by ZDOMan.ForceSendZDO(owner, id), which AddForceSendZdos delivers regardless of
    /// sector range; the owner's copy is then current before they can come back.
    ///
    /// Skip - anything ambiguous, because a routed RPC that finds no instance is discarded after the chest
    /// debit and a direct write under a live tick loses to it: an OwnerRevision that has not held still for
    /// SettleSeconds (a handover or a release is propagating; also applies to owner 0 once the ZDO has been
    /// owned this uptime - the settle clock runs between visits, so a border-flapping station is fed on the
    /// first visit that finds it quiet), a fresh stamp with no instance (just left, just arrived and not yet
    /// reported, or dead), and a near owner who is not ticking (loading in after login/teleport - the only
    /// Skip that logs, once per episode at verbose). A skipped station is revisited next scanner cycle. Note
    /// that a "cycle" is NOT the 3 s interval: each family is walked one prefab name at a time,
    /// ProductionSupplyBatchSize sector-chunks per interval, so on a lived-in world a smelter is visited
    /// roughly every 20-45 s and a fireplace every 2-4 min; that is also the first-feed delay after boot.
    ///
    /// What can still be lost, all one unit per station per coincidence: a visit inside the character-
    /// position sync lag (~50-100 ms) of the owner's departure - the RPC path cannot be acknowledged and
    /// nothing server-side sees a jump before the client reports it; a visit inside the same lag after the
    /// owner hand-feeds the last unit into a fireplace (Fireplace.RPC_AddFuel re-checks the cap on the
    /// owner's copy, the smelter RPCs do not); and, for a Direct write, a second client that once held the
    /// station's copy as owner and never received its release (needs a saturated send queue while it left).
    /// A hard crash keeps the peer "connected" until its timeout; its stamp goes stale within two ticks and
    /// the station is skipped from then on.
    /// Every gate (owner resolvable, fuel headroom, queue room, the station's own conversion list) runs
    /// BEFORE the chest debit, per VANILLA-PIECE-INTEROP-FACTS.md §1 - RPC_AddOre re-validates on arrival
    /// and silently discards what it rejects, so nothing may be removed until it is certain to be accepted.
    /// </summary>
    public static class ProductionSupplyEngine
    {
        private const string Tag = "ProductionSupply";
        private const string KilnProduct = "Coal";

        private static ZdoSpatialQuery.PrefabSetScanner _fireplaceScanner;
        private static ZdoSpatialQuery.PrefabSetScanner _smelterScanner;
        /// <summary>Prefab hash of every tracked station -> its own display name ("$piece_charcoalkiln"), for player toasts.</summary>
        private static readonly Dictionary<int, string> _stations = new Dictionary<int, string>();
        private static readonly HashSet<int> _kilns = new HashSet<int>();
        private static HashSet<string> _kilnInputs;
        private static string _kilnInputsRaw;
        private static float _timer;
        private static readonly List<ZDO> _buffer = new List<ZDO>();
        private static readonly List<ZDO> _nearBuffer = new List<ZDO>();

        public static void Initialize()
        {
            _stations.Clear();
            _kilns.Clear();
            var fireplaceNames = new List<string>();
            var smelterNames = new List<string>();
            var kilnNames = new List<string>();
            if (ZNetScene.instance != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }
                    int hash = prefab.name.GetStableHashCode();
                    Fireplace fireplace = prefab.GetComponent<Fireplace>();
                    if (fireplace != null)
                    {
                        fireplaceNames.Add(prefab.name);
                        _stations[hash] = string.IsNullOrEmpty(fireplace.m_name) ? prefab.name : fireplace.m_name;
                    }
                    Smelter smelter = prefab.GetComponent<Smelter>();
                    if (smelter != null)
                    {
                        smelterNames.Add(prefab.name);
                        _stations[hash] = string.IsNullOrEmpty(smelter.m_name) ? prefab.name : smelter.m_name;
                        if (IsKiln(smelter))
                        {
                            _kilns.Add(hash);
                            kilnNames.Add(prefab.name);
                        }
                    }
                }
            }
            _fireplaceScanner = new ZdoSpatialQuery.PrefabSetScanner(fireplaceNames);
            _smelterScanner = new ZdoSpatialQuery.PrefabSetScanner(smelterNames);
            WonderlandDebug.LogInfo($"[ProductionSupplyEngine] tracking {fireplaceNames.Count} fireplace-family and {smelterNames.Count} smelter-family prefab types.");

            HashSet<string> allowed = KilnInputs();
            string filter = allowed.Count == 0 ? "any wood the kiln accepts" : string.Join(", ", allowed);
            WonderlandDebug.LogAlways($"[ProductionSupplyEngine] kiln input filter: {filter} - applies to {kilnNames.Count} kiln prefab(s): {string.Join(", ", kilnNames)}.");
        }

        public static void OnUpdate(float dt)
        {
            if (_fireplaceScanner == null || WonderlandConfig.ProductionSupplyEnabled?.Value != true)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.ProductionSupplyInterval?.Value ?? 3f))
            {
                return;
            }
            _timer = 0f;

            UpdateOwnershipRecords();

            int budget = Mathf.Max(1, WonderlandConfig.ProductionSupplyBatchSize?.Value ?? 20);

            _buffer.Clear();
            for (int i = 0; i < budget; i++)
            {
                _fireplaceScanner.Advance(_buffer);
            }
            foreach (ZDO zdo in _buffer)
            {
                ProcessFireplace(zdo);
            }

            _buffer.Clear();
            for (int i = 0; i < budget; i++)
            {
                _smelterScanner.Advance(_buffer);
            }
            foreach (ZDO zdo in _buffer)
            {
                ProcessSmelter(zdo);
            }
        }

        /// <summary>
        /// The closest tracked station (any fireplace- or smelter-family object) within range of a
        /// point, with its display name. This is what a player's emote is aimed at.
        /// </summary>
        public static bool TryFindNearestStation(Vector3 position, float range, out ZDO station, out string displayName)
        {
            station = null;
            displayName = "";
            float best = float.MaxValue;
            foreach (ZDO zdo in ZdoSpatialQuery.FindNear(position, range, _nearBuffer))
            {
                if (!_stations.TryGetValue(zdo.GetPrefab(), out string name))
                {
                    continue;
                }
                float distance = (zdo.GetPosition() - position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    station = zdo;
                    displayName = name;
                }
            }
            return station != null;
        }

        private static bool IsKiln(Smelter template)
        {
            if (template.m_conversion == null || template.m_conversion.Count == 0)
            {
                return false;
            }
            foreach (Smelter.ItemConversion conversion in template.m_conversion)
            {
                if (conversion == null || conversion.m_to == null || conversion.m_to.gameObject.name != KilnProduct)
                {
                    return false;
                }
            }
            return true;
        }

        private static HashSet<string> KilnInputs()
        {
            string raw = WonderlandConfig.KilnWoodTypes?.Value ?? "Wood";
            if (_kilnInputs == null || !string.Equals(raw, _kilnInputsRaw, StringComparison.Ordinal))
            {
                _kilnInputsRaw = raw;
                _kilnInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in raw.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                    {
                        _kilnInputs.Add(trimmed);
                    }
                }
            }
            return _kilnInputs;
        }

        /// <summary>Which machine applies a feed to a station ZDO - see the class summary.</summary>
        private enum Delivery
        {
            Direct,
            OwnerRpc,
            Skip,
        }

        /// <summary>Seconds within which the owner's tick stamp must have been written for the owner's
        /// instance to count as live: two ticks plus slack. Smelter.UpdateSmelter runs every 1 s,
        /// Fireplace.UpdateFireplace every 2 s. A client hitch longer than this only costs a Skip.</summary>
        private const double SmelterTickFreshSeconds = 2.5;
        private const double FireplaceTickFreshSeconds = 4.5;

        private static readonly HashSet<ZDOID> _skipLogged = new HashSet<ZDOID>();

        /// <summary>OwnerRevision as last seen for a station, the world time at which that revision was first
        /// seen, and whether it has held still long enough to trust. A revision change means a handover or
        /// release is propagating (the new owner's copy may not say "owner" yet, the old owner's tick may
        /// still be in flight). Settling is timed (SettleSeconds of world time with no change), re-checked
        /// every engine interval for the stations waiting on it rather than only when the scanner next
        /// visits them: ownership of a station on a zone border flips on every crossing, and a per-visit
        /// rule could then never see two equal samples while the station burned fuel it was not being fed.
        /// The time also lets a stamp be required to post-date the current ownership: the world clock pauses
        /// with no players, so a stamp from before a solo logout is still "fresh" in world time at re-login.</summary>
        private struct OwnershipSeen
        {
            public ushort Revision;
            public long SinceTicks;
            public bool Settled;
        }

        /// <summary>World seconds an OwnerRevision must hold still before a write under it: longer than any
        /// ZDOData round trip plus one owner tick.</summary>
        private const double SettleSeconds = 3.0;

        private static readonly Dictionary<ZDOID, OwnershipSeen> _ownership = new Dictionary<ZDOID, OwnershipSeen>();
        private static readonly HashSet<ZDOID> _settling = new HashSet<ZDOID>();
        private static readonly List<ZDOID> _scratchIds = new List<ZDOID>();
        private static int _pruneCountdown = PruneEveryIntervals;
        // Records for stations demolished this uptime are dropped by a periodic existence check - the scanner
        // never yields a destroyed ZDO (HandleDestroyedZDO removes it from its sector first), so nothing else
        // would notice.
        private const int PruneEveryIntervals = 200;

        private static void Forget(ZDOID id)
        {
            _ownership.Remove(id);
            _settling.Remove(id);
            _skipLogged.Remove(id);
        }

        /// <summary>Records this visit's OwnerRevision; true once that revision has held for SettleSeconds.</summary>
        private static bool Settled(ZDO zdo, out OwnershipSeen seen)
        {
            ushort revision = zdo.OwnerRevision;
            long now = ZNet.instance != null ? ZNet.instance.GetTime().Ticks : 0L;
            if (_ownership.TryGetValue(zdo.m_uid, out seen) && seen.Revision == revision)
            {
                if (!seen.Settled && now - seen.SinceTicks >= (long)(SettleSeconds * TimeSpan.TicksPerSecond))
                {
                    seen.Settled = true;
                    _ownership[zdo.m_uid] = seen;
                    _settling.Remove(zdo.m_uid);
                }
                return seen.Settled;
            }
            seen = new OwnershipSeen { Revision = revision, SinceTicks = now, Settled = false };
            _ownership[zdo.m_uid] = seen;
            _settling.Add(zdo.m_uid);
            return false;
        }

        /// <summary>Once per engine interval: advance the settle clock for stations the scanner is not
        /// currently looking at, and every PruneEveryIntervals drop records whose ZDO no longer exists.</summary>
        private static void UpdateOwnershipRecords()
        {
            if (ZDOMan.instance == null || ZNet.instance == null)
            {
                return;
            }
            long now = ZNet.instance.GetTime().Ticks;
            long settleTicks = (long)(SettleSeconds * TimeSpan.TicksPerSecond);
            _scratchIds.Clear();
            _scratchIds.AddRange(_settling);
            foreach (ZDOID id in _scratchIds)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || !_ownership.TryGetValue(id, out OwnershipSeen seen))
                {
                    Forget(id);
                    continue;
                }
                if (zdo.OwnerRevision != seen.Revision)
                {
                    _ownership[id] = new OwnershipSeen { Revision = zdo.OwnerRevision, SinceTicks = now, Settled = false };
                }
                else if (now - seen.SinceTicks >= settleTicks)
                {
                    seen.Settled = true;
                    _ownership[id] = seen;
                    _settling.Remove(id);
                }
            }

            if (--_pruneCountdown > 0)
            {
                return;
            }
            _pruneCountdown = PruneEveryIntervals;
            _scratchIds.Clear();
            foreach (ZDOID id in _ownership.Keys)
            {
                if (ZDOMan.instance.GetZDO(id) == null) _scratchIds.Add(id);
            }
            foreach (ZDOID id in _skipLogged)
            {
                if (ZDOMan.instance.GetZDO(id) == null) _scratchIds.Add(id);
            }
            foreach (ZDOID id in _scratchIds) Forget(id);
        }

        /// <summary>The owner's position as the server knows it: the character ZDO (ZSyncTransform keeps it
        /// current every physics tick). False with the 2 s-cadence ZNetPeer.m_refPos when there is no
        /// character - the 8-18 s death-to-respawn gap, during which the client has already moved its own
        /// reference point to the bed and destroyed every instance around the corpse.</summary>
        private static bool TryGetOwnerPosition(ZNetPeer peer, out Vector3 position)
        {
            if (!peer.m_characterID.IsNone() && ZDOMan.instance != null)
            {
                ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (character != null && character.IsValid())
                {
                    position = character.GetPosition();
                    return true;
                }
            }
            position = peer.GetRefPos();
            return false;
        }

        /// <summary>Whether the owner's own client instantiates the station's zone: the same test its
        /// ZNetScene.CreateDestroyObjects uses (FindSectorObjects over the peer's OWN validated simulation
        /// distance - a low-preset client instantiates less than the server's setting - with the corner
        /// zones excluded unless classic), no margin. Ring near+1 is where an owner has provably no instance,
        /// and vanilla's ReleaseNearbyZDOS never walks it either, so a station left there by a teleport or
        /// bed respawn must go the Direct path, not Skip.</summary>
        private static bool OwnerInstantiates(ZNetPeer peer, Vector2s ownerZone, Vector2s stationZone, out int zoneDistance)
        {
            zoneDistance = Mathf.Max(Mathf.Abs(stationZone.x - ownerZone.x), Mathf.Abs(stationZone.y - ownerZone.y));
            SimulationDistance sd = peer.m_simulationDistance;
            int near = sd.NearSimulationDistance > 0 ? sd.NearSimulationDistance : ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance;
            if (zoneDistance > near)
            {
                return false;
            }
            return sd.IsClassic || ZoneSystem.instance == null || ZoneSystem.instance.ZonesWithinRadius(ownerZone, stationZone, near);
        }

        /// <summary>
        /// See the class summary for the three outcomes. refreshOwner is set with Direct when the owner is a
        /// connected but out-of-range peer whose own copy of the ZDO must be brought up to date after the
        /// write (ZDOMan.ForceSendZDO).
        /// </summary>
        private static Delivery ResolveDelivery(ZDO zdo, int ownerTickHash, double freshSeconds, out long ownerUid, out ZNetPeer ownerPeer, out bool refreshOwner)
        {
            ownerUid = zdo.GetOwner();
            ownerPeer = null;
            refreshOwner = false;
            if (ZNet.instance == null || ownerUid == ZNet.GetUID())
            {
                Forget(zdo.m_uid);
                return Delivery.Direct;
            }

            if (ownerUid == 0L)
            {
                if (zdo.OwnerRevision == 0)
                {
                    Forget(zdo.m_uid); // never owned this uptime: nothing can be ticking it
                    return Delivery.Direct;
                }
                // Released this uptime. The previous owner keeps ticking its copy until the owner change
                // reaches it (ReleaseNearbyZDOS runs 1.5 zones out while the client instantiates to 2), so
                // let the release propagate for one visit before writing under it.
                _skipLogged.Remove(zdo.m_uid);
                return Settled(zdo, out _) ? Delivery.Direct : Delivery.Skip;
            }

            ownerPeer = ZNet.instance.GetPeer(ownerUid);
            if (ownerPeer == null)
            {
                Forget(zdo.m_uid); // a session that has left - nothing simulates this ZDO any more
                return Delivery.Direct;
            }

            if (!Settled(zdo, out OwnershipSeen seen))
            {
                _skipLogged.Remove(zdo.m_uid);
                return Delivery.Skip; // first sight, or the owner just changed - let the assignment propagate
            }

            long stamp = zdo.GetLong(ownerTickHash, 0L);
            bool stampUsable = stamp > 0L && stamp <= DateTime.MaxValue.Ticks && stamp > seen.SinceTicks;
            double sinceTick = stampUsable ? (ZNet.instance.GetTime() - new DateTime(stamp)).TotalSeconds : double.MaxValue;
            bool fresh = sinceTick >= -1.0 && sinceTick <= freshSeconds;

            Vector2s stationZone = ZoneSystem.GetZone(zdo.GetPosition());
            bool hasCharacter = TryGetOwnerPosition(ownerPeer, out Vector3 ownerPosition);
            Vector2s ownerZone = ZoneSystem.GetZone(ownerPosition);
            // No character (dead, respawning) = no instance anywhere, whatever the stale refPos says.
            bool ownerNear = OwnerInstantiates(ownerPeer, ownerZone, stationZone, out int zoneDistance) && hasCharacter;

            if (fresh && ownerNear)
            {
                _skipLogged.Remove(zdo.m_uid);
                return Delivery.OwnerRpc;
            }
            if (fresh)
            {
                _skipLogged.Remove(zdo.m_uid);
                return Delivery.Skip; // ticking a moment ago but no instance now: leaving, arriving unreported, or dead
            }
            if (!ownerNear)
            {
                _skipLogged.Remove(zdo.m_uid);
                refreshOwner = true;
                return Delivery.Direct;
            }

            if (_skipLogged.Add(zdo.m_uid))
            {
                WonderlandDebug.LogInfo($"[{Tag}] {zdo.m_uid} skipped: owner '{ownerPeer.m_playerName}' is {zoneDistance} zone(s) away but has not ticked it for {(sinceTick == double.MaxValue ? "ever" : sinceTick.ToString("0.0") + " s")} - retrying each cycle.");
            }
            return Delivery.Skip;
        }

        /// <summary>After a Direct write to a station whose connected owner is out of range: push the new
        /// state into that owner's own copy now, so a return does not resurrect the stale one.</summary>
        private static void RefreshOwnerCopy(ZDO zdo, long ownerUid, bool refreshOwner)
        {
            if (refreshOwner && ZDOMan.instance != null)
            {
                ZDOMan.instance.ForceSendZDO(ownerUid, zdo.m_uid);
            }
        }

        private static void ProcessFireplace(ZDO zdo)
        {
            if (!zdo.IsValid() || SupplySwitch.IsOff(zdo.m_uid))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Fireplace template = prefab != null ? prefab.GetComponent<Fireplace>() : null;
            if (template == null || template.m_infiniteFuel || template.m_fuelItem == null)
            {
                return;
            }

            // Ownership bookkeeping first, even when the fireplace is full: a settle that only started once
            // headroom appeared would cost a nearly-empty hearth one extra cycle.
            Delivery delivery = ResolveDelivery(zdo, ZDOVars.s_lastTime, FireplaceTickFreshSeconds, out long ownerUid, out ZNetPeer ownerPeer, out bool refreshOwner);
            if (delivery == Delivery.Skip)
            {
                return;
            }

            // Same headroom test as Fireplace.RPC_AddFuel itself (CeilToInt(fuel) >= m_maxFuel -> no add) -
            // it is the receiver's own gate on the RPC path, so it has to pass here before the chest debit.
            float fuel = zdo.GetFloat(ZDOVars.s_fuel);
            if (Mathf.CeilToInt(fuel) >= template.m_maxFuel)
            {
                return;
            }

            string fuelName = template.m_fuelItem.gameObject.name;
            if (!TryConsumeOne(zdo.GetPosition(), fuelName))
            {
                return;
            }

            float after = Mathf.Clamp(Mathf.Clamp(fuel, 0f, template.m_maxFuel) + 1f, 0f, template.m_maxFuel);
            if (delivery == Delivery.OwnerRpc)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ownerUid, zdo.m_uid, "RPC_AddFuel");
            }
            else
            {
                zdo.Set(ZDOVars.s_fuel, after);
                RefreshOwnerCopy(zdo, ownerUid, refreshOwner);
            }
            LogFeed(prefab.name, zdo, fuelName, delivery, ownerPeer, refreshOwner, delivery == Delivery.OwnerRpc ? $"server copy fuel {fuel:0.0}" : $"fuel {fuel:0.0} -> {after:0.0}");
        }

        private static void ProcessSmelter(ZDO zdo)
        {
            if (!zdo.IsValid() || SupplySwitch.IsOff(zdo.m_uid))
            {
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Smelter template = prefab != null ? prefab.GetComponent<Smelter>() : null;
            if (template == null)
            {
                return;
            }

            Delivery delivery = ResolveDelivery(zdo, ZDOVars.s_startTime, SmelterTickFreshSeconds, out long ownerUid, out ZNetPeer ownerPeer, out bool refreshOwner);
            if (delivery == Delivery.Skip)
            {
                return;
            }

            if (template.m_fuelItem != null && template.m_maxFuel > 0)
            {
                // Same headroom test as Smelter.OnAddFuel ("$msg_itsfull" above m_maxFuel - 1): a whole unit is
                // taken from the chest, so only feed when a whole unit fits. The old "< m_maxFuel - 0.01" test
                // spent a full coal on a fractional top-up on nearly every visit of a burning smelter.
                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                string fuelName = template.m_fuelItem.gameObject.name;
                if (fuel <= template.m_maxFuel - 1f && TryConsumeOne(zdo.GetPosition(), fuelName))
                {
                    if (delivery == Delivery.OwnerRpc)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(ownerUid, zdo.m_uid, "RPC_AddFuel");
                    }
                    else
                    {
                        zdo.Set(ZDOVars.s_fuel, fuel + 1f);
                        RefreshOwnerCopy(zdo, ownerUid, refreshOwner);
                    }
                    LogFeed(prefab.name, zdo, fuelName, delivery, ownerPeer, refreshOwner, delivery == Delivery.OwnerRpc ? $"server copy fuel {fuel:0.0}" : $"fuel {fuel:0.0} -> {fuel + 1f:0.0}");
                }
            }

            if (template.m_maxOre > 0 && template.m_conversion.Count > 0)
            {
                int queued = zdo.GetInt(ZDOVars.s_queued);
                if (queued < template.m_maxOre)
                {
                    HashSet<string> kilnFilter = _kilns.Contains(zdo.GetPrefab()) ? KilnInputs() : null;
                    foreach (Smelter.ItemConversion conversion in template.m_conversion)
                    {
                        if (conversion.m_from == null)
                        {
                            continue;
                        }
                        string input = conversion.m_from.gameObject.name;
                        if (kilnFilter != null && kilnFilter.Count > 0 && !kilnFilter.Contains(input))
                        {
                            continue;
                        }
                        if (TryConsumeOne(zdo.GetPosition(), input))
                        {
                            if (delivery == Delivery.OwnerRpc)
                            {
                                // Smelter.RPC_AddOre(long sender, string name, bool cheated) - registered
                                // Register<string, bool>; false is what OnAddOre sends for a normal item.
                                ZRoutedRpc.instance.InvokeRoutedRPC(ownerUid, zdo.m_uid, "RPC_AddOre", input, false);
                            }
                            else
                            {
                                // Exactly what Smelter.QueueOre writes, including the cheat flag: a stale true
                                // left by a devcommands spawn would mark every later engine-fed bar cheated.
                                zdo.Set("item" + queued, input);
                                zdo.Set(ZDOVars.s_queued, queued + 1);
                                zdo.Set(ZDOVars.s_cheatedQueued, false);
                                RefreshOwnerCopy(zdo, ownerUid, refreshOwner);
                            }
                            LogFeed(prefab.name, zdo, input, delivery, ownerPeer, refreshOwner, delivery == Delivery.OwnerRpc ? $"server copy queue {queued}" : $"queue {queued} -> {queued + 1}");
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>One verbose line per feed with everything a "where did my ore go" report needs: station,
        /// position, which machine applied it, and the field before/after. On the owner-RPC path the server
        /// never observes the after-value, so the line says what was SENT and what the server's copy read -
        /// a line that asserts an outcome nobody saw is the failure class HEADLESS-AND-EMPTY-SERVER-FACTS.md
        /// warns about. Complements the ItemLedger line (which only names the item).</summary>
        private static void LogFeed(string stationPrefab, ZDO zdo, string input, Delivery delivery, ZNetPeer ownerPeer, bool refreshedOwner, string change)
        {
            Vector3 p = zdo.GetPosition();
            string via = delivery == Delivery.OwnerRpc
                ? $"rpc sent to owner '{ownerPeer?.m_playerName}'"
                : refreshedOwner ? $"direct write, copy pushed to far owner '{ownerPeer?.m_playerName}'" : "direct write";
            WonderlandDebug.LogInfo($"[{Tag}] {stationPrefab} {zdo.m_uid} @ ({p.x:0},{p.y:0},{p.z:0}) <- 1x {input} via {via} | {change}");
        }

        /// <summary>
        /// Finds a linked container within range holding at least (reserve + 1) of the named prefab
        /// and removes exactly one, leaving the reserve floor untouched. Returns false (nothing
        /// consumed) if no source has enough spare stock.
        /// </summary>
        private static bool TryConsumeOne(Vector3 position, string fuelOrOrePrefabName)
        {
            float range = WonderlandConfig.ProductionSupplyRange?.Value ?? 15f;
            int reserve = Mathf.Max(0, WonderlandConfig.ProductionSupplyReserve?.Value ?? 1);

            foreach (ZDO containerZdo in ZdoSpatialQuery.FindNear(position, range))
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(containerZdo.GetPrefab());
                Container template = ContainerRegistry.ResolveTemplate(prefab);
                if (template == null || ZdoInventoryIO.IsBusy(containerZdo))
                {
                    continue;
                }

                (int width, int height) = ContainerRows.GetGridSize(prefab, template);
                Inventory inventory = ZdoInventoryIO.Load(containerZdo, width, height);
                if (inventory == null)
                {
                    continue;
                }

                ItemDrop.ItemData found = inventory.GetItem(fuelOrOrePrefabName, -1, isPrefabName: true);
                if (found == null)
                {
                    continue;
                }

                int totalOfThisItem = inventory.CountItems(found.m_shared.m_name);
                if (totalOfThisItem <= reserve)
                {
                    continue;
                }

                inventory.RemoveItem(found, 1);
                ZdoInventoryIO.Save(containerZdo, inventory);
                ItemLedger.RecordTransfer(Tag, found.m_shared.m_name, 1);
                return true;
            }

            return false;
        }
    }
}
