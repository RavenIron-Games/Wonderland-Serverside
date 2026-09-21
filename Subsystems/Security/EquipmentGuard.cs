using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Security
{
    /// <summary>
    /// Monitors connected players for equipped gear anomalies.
    /// Reads the VisEquipment item hashes and quality integers off each player's character ZDO - the owning
    /// client writes them itself (VisEquipment.SetRightItemVisual and friends: prefab-name stable hash plus
    /// the item's m_quality, server decompile line ~20599) - and checks every equipped prefab against the
    /// banned list, the server's progression tier ceiling and quality sanity. Tiers come from
    /// ItemTierClassifier, which derives them from the game's own recipe/smelter/station data and honours the
    /// admin pins in ProgressionItemExemptions. Authenticated admins (AdminRegistry) bypass every check.
    /// Violations go to AuditLog (the dedicated security log) with per-player anti-spam suppression keyed on
    /// the profile id (ZDO s_playerID); the cache is cleared on disconnect and whenever the tier ceiling moves.
    ///
    /// There is deliberately no Eitr check. Eitr on the player ZDO (s_eitr) comes only from food -
    /// Player.GetTotalFoodValue starts at 0 and sums food.m_eitr - and Eitr food is cooked from raw Mistlands
    /// pickables (Magecap, Jotun Puffs, Royal Jelly...) which the recipe-derived rule leaves unrestricted:
    /// carrying a biome's raw drops home is exploration, not twinking, so a full Eitr bar proves nothing the
    /// equipped-item check does not already cover. Magic gear proper (staves, robes) is a VisEquipment hash
    /// like any other weapon and is caught there.
    /// </summary>
    public static class EquipmentGuard
    {
        private static float _timer;
        private static readonly Dictionary<long, HashSet<int>> _flaggedHashesPerPlayer = new Dictionary<long, HashSet<int>>();

        private struct SlotCheck
        {
            public int HashKey;
            public int QualityKey;
            public string SlotName;

            public SlotCheck(int hashKey, int qualityKey, string slotName)
            {
                HashKey = hashKey;
                QualityKey = qualityKey;
                SlotName = slotName;
            }
        }

        private static readonly SlotCheck[] Slots = new[]
        {
            new SlotCheck(ZDOVars.s_rightItem, ZDOVars.s_rightItemQuality, "Right Hand"),
            new SlotCheck(ZDOVars.s_leftItem, ZDOVars.s_leftItemQuality, "Left Hand"),
            new SlotCheck(ZDOVars.s_chestItem, 0, "Chest"),
            new SlotCheck(ZDOVars.s_legItem, 0, "Legs"),
            new SlotCheck(ZDOVars.s_helmetItem, 0, "Helmet"),
            new SlotCheck(ZDOVars.s_shoulderItem, ZDOVars.s_shoulderItemQuality, "Shoulder"),
            new SlotCheck(ZDOVars.s_utilityItem, 0, "Utility"),
            new SlotCheck(ZDOVars.s_rightBackItem, ZDOVars.s_rightBackItemQuality, "Back Right"),
            new SlotCheck(ZDOVars.s_leftBackItem, ZDOVars.s_leftBackItemQuality, "Back Left"),
            new SlotCheck(ZDOVars.s_trinketItem, 0, "Trinket")
        };

        public static void OnWorldReady()
        {
            ResetPlayerCache();
        }

        public static void ResetPlayerCache()
        {
            _flaggedHashesPerPlayer.Clear();
        }

        /// <summary>
        /// Drops the anti-spam cache for a departing peer so the same gear is logged again next session.
        /// The scan keys on ConnectedCharacter.PlayerId (ZDO s_playerID, the PlayerProfile id), and the peer
        /// carries that very number: ZNet.RPC_PlayerID stores the id the client sends on connect in
        /// ZNetPeer.m_playerID (server decompile line ~80962), the same value Player.SetPlayerID later writes
        /// into the ZDO. Not peer.m_characterID.UserID - a ZDOID's user is the ZDOMan session
        /// (Utils.GenerateUID(), new every launch), not a player - and not peer.m_uid, which is the connection.
        /// A peer that disconnects before its PlayerID RPC arrived never got a cache entry, so there is nothing
        /// to clear.
        /// </summary>
        public static void OnPeerDisconnected(ZNetPeer peer)
        {
            if (peer == null) return;
            long playerId = peer.m_playerID;
            if (playerId == 0L) return;
            _flaggedHashesPerPlayer.Remove(playerId);
        }

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.EquipmentGuardEnabled?.Value == false)
            {
                return;
            }

            _timer += dt;
            if (_timer < (WonderlandConfig.EquipmentGuardInterval?.Value ?? 5f))
            {
                return;
            }
            _timer = 0f;

            if (ObjectDB.instance == null)
            {
                return;
            }

            ItemTier maxTier = ItemTierClassifier.GetEffectiveMaxTier();
            bool kickEnabled = WonderlandConfig.EquipmentGuardKick?.Value == true;
            bool checkQuality = WonderlandConfig.EquipmentGuardEnforceQuality?.Value != false;

            foreach (ConnectedCharacter character in ConnectedCharacters.All())
            {
                if (character.Zdo == null || !character.Zdo.IsValid())
                {
                    continue;
                }

                // Admin bypass: authenticated server admins on adminlist.txt bypass all equipment, cheat, and tier checks
                if (WonderlandConfig.EquipmentGuardAdminBypass?.Value != false && AdminRegistry.IsAdmin(character))
                {
                    continue;
                }

                long key = character.PlayerId != 0L ? character.PlayerId : character.Zdo.m_uid.GetHashCode();
                string name = character.Name;
                Vector3 pos = character.Position;

                if (!_flaggedHashesPerPlayer.TryGetValue(key, out HashSet<int> flaggedHashes))
                {
                    flaggedHashes = new HashSet<int>();
                    _flaggedHashesPerPlayer[key] = flaggedHashes;
                }

                // Check equipped slots
                for (int i = 0; i < Slots.Length; i++)
                {
                    SlotCheck slot = Slots[i];
                    int hash = character.Zdo.GetInt(slot.HashKey, 0);
                    if (hash == 0)
                    {
                        continue;
                    }

                    if (flaggedHashes.Contains(hash))
                    {
                        continue;
                    }

                    GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
                    if (prefab == null)
                    {
                        flaggedHashes.Add(hash);
                        AuditLog.Flag("EquipmentGuard", character, $"equipped unknown/modded item hash 0x{hash:X8} ({hash}) in slot {slot.SlotName}");
                        if (kickEnabled)
                        {
                            Kick(character.Peer, "Unknown or modded equipment detected");
                            break;
                        }
                        continue;
                    }

                    ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
                    ItemDrop.ItemData? itemData = itemDrop?.m_itemData;

                    // 1. Cheat / Banned items
                    bool isBanned = ItemTierClassifier.IsBanned(prefab.name);
                    ItemTier tier = ItemTierClassifier.GetTier(prefab, itemData);

                    if (isBanned || tier == ItemTier.Cheat)
                    {
                        flaggedHashes.Add(hash);
                        AuditLog.Flag("EquipmentGuard", character, $"equipped cheat/banned item '{prefab.name}' in slot {slot.SlotName}");
                        if (kickEnabled)
                        {
                            Kick(character.Peer, WonderlandConfig.EquipmentGuardKickMessage?.Value ?? "Unauthorized equipment");
                            break;
                        }
                        continue;
                    }

                    // 2. Progression tier ceiling
                    if (maxTier != ItemTier.None && ItemTierClassifier.ExceedsTier(tier, maxTier))
                    {
                        flaggedHashes.Add(hash);
                        AuditLog.Flag("EquipmentGuard", character, $"equipped item '{prefab.name}' (Tier: {tier}) exceeding server ceiling '{maxTier}' in slot {slot.SlotName}");
                        if (kickEnabled)
                        {
                            Kick(character.Peer, WonderlandConfig.EquipmentGuardKickMessage?.Value ?? "High-tier equipment not permitted");
                            break;
                        }
                        continue;
                    }

                    // 3. Quality sanity
                    // m_maxQuality is only a ceiling at ordinary stations. Valheim 1.0's upgrader stations
                    // (CraftingStation.m_upgrader) let a recipe with an m_upgraderResource requirement keep
                    // upgrading past it - InventoryGui.UpdateRecipe accepts the next quality when
                    // "num <= m_maxQuality || currentCraftingStation.m_upgrader" (server decompile line ~51318).
                    // A PlayStation player was flagged at quality 10 for exactly this, so such items are exempt.
                    if (checkQuality && slot.QualityKey != 0 && itemData?.m_shared != null && itemData.m_shared.m_maxQuality > 0 && !ItemTierClassifier.CanExceedMaxQuality(prefab))
                    {
                        int quality = character.Zdo.GetInt(slot.QualityKey, 1);
                        if (quality > itemData.m_shared.m_maxQuality)
                        {
                            flaggedHashes.Add(hash);
                            AuditLog.Flag("EquipmentGuard", character, $"equipped item '{prefab.name}' with impossible quality {quality} (max: {itemData.m_shared.m_maxQuality}) in slot {slot.SlotName}");
                            if (kickEnabled)
                            {
                                Kick(character.Peer, WonderlandConfig.EquipmentGuardKickMessage?.Value ?? "Illegal item quality");
                                break;
                            }
                            continue;
                        }
                    }
                }
            }
        }

        private static void Kick(ZNetPeer? peer, string reason)
        {
            if (peer == null || peer.m_rpc == null) return;
            try
            {
                WonderlandDebug.LogWarning($"[EquipmentGuard] Kicking player '{peer.m_playerName}': {reason}");
                peer.m_rpc.Invoke("Error", 3); // ErrorVersion: Incompatible version popup
                ZNet.instance?.Disconnect(peer);
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[EquipmentGuard] Kick exception: {ex.Message}");
            }
        }
    }
}
