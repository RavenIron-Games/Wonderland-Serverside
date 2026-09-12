using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.StatusEffects
{
    /// <summary>
    /// Grants a curated, config-driven roster of EXISTING vanilla status-effect assets to every
    /// connected player and keeps each one alive indefinitely, entirely server-side.
    ///
    /// Mechanism (decompile-verified, see -NextSteps.md sections 3.1 / 3.5 / 4): SEMan.AddStatusEffect's
    /// non-owner branch routes through a routed RPC the client executes with its own already-loaded
    /// copy of the named asset - no client mod needed, and the RPC's nameHash only ever resolves
    /// against an asset the client already has (a brand-new custom effect is not possible over this
    /// channel). There is no removal RPC, so "off" means "stop re-sending it" and let its own m_ttl
    /// run out, not an instant revoke. Distinct effects stack ADDITIVELY off the same base
    /// (SEMan.ApplyStatusEffectSpeedMods / SE_Stats.ModifyStaminaRegen), and a server-triggered grant
    /// bypasses vanilla's "one potion effect per category" rule entirely - that check only lives in
    /// the player's own item-consumption code (Humanoid.ConsumeItem, ~15453), never in
    /// SEMan.AddStatusEffect itself - so multiple roster slots can be enabled at once and their
    /// bonuses genuinely combine, something a real player drinking potions could never do.
    ///
    /// Each slot's re-ping cadence is derived from the REAL asset's own m_ttl, read live off the
    /// resolved ObjectDB entry at world-ready rather than hardcoded: roster assets range from a 1s
    /// signal state (Rested - vanilla itself re-applies it continuously while the condition holds) to
    /// a 600s potion (Potion_hasty) to a status effect that never expires at all (m_ttl &lt;= 0 -
    /// StatusEffect.IsDone() requires m_ttl &gt; 0 to ever trip, confirmed true of the vanilla "Warm"
    /// asset). Per-slot-per-player cooldowns are decremented every Update() tick by the real frame
    /// dt (not a batched sweep interval), so a 1-second asset can be kept genuinely alive without a
    /// separate "sweep interval" config fighting it - the cost is a handful of dictionary lookups per
    /// frame, immaterial next to everything else this mod already does per tick.
    /// </summary>
    public static class BuffRosterEngine
    {
        private class Slot
        {
            public string AssetName = "";
            public ConfigEntry<bool>? Enabled;

            /// <summary>Null = always eligible. Non-null = only granted while ShipAttachment.IsOnShip is true for that player.</summary>
            public ConfigEntry<bool>? RequireBoat;
            public int NameHash;
            public float RepingInterval;
            public readonly Dictionary<long, float> Cooldown = new Dictionary<long, float>();
        }

        private static readonly List<Slot> Slots = new List<Slot>();
        private static bool _built;
        private static bool _resolved;

        private static void BuildRosterOnce()
        {
            if (_built)
            {
                return;
            }
            _built = true;

            Slots.Add(new Slot { AssetName = "GP_Moder", Enabled = WonderlandConfig.BuffRoster_GP_Moder_Enabled, RequireBoat = WonderlandConfig.BuffRoster_GP_Moder_RequireBoat });
            Slots.Add(new Slot { AssetName = "Potion_hasty", Enabled = WonderlandConfig.BuffRoster_Potion_hasty_Enabled });
            Slots.Add(new Slot { AssetName = "TrinketIronStamina", Enabled = WonderlandConfig.BuffRoster_TrinketIronStamina_Enabled });
            Slots.Add(new Slot { AssetName = "Potion_swimmer", Enabled = WonderlandConfig.BuffRoster_Potion_swimmer_Enabled });
            Slots.Add(new Slot { AssetName = "TrinketChitinSwim", Enabled = WonderlandConfig.BuffRoster_TrinketChitinSwim_Enabled });
            Slots.Add(new Slot { AssetName = "Warm", Enabled = WonderlandConfig.BuffRoster_Warm_Enabled });
            Slots.Add(new Slot { AssetName = "Potion_stamina_lingering", Enabled = WonderlandConfig.BuffRoster_Potion_stamina_lingering_Enabled });
            Slots.Add(new Slot { AssetName = "Potion_tasty", Enabled = WonderlandConfig.BuffRoster_Potion_tasty_Enabled });
            Slots.Add(new Slot { AssetName = "Rested", Enabled = WonderlandConfig.BuffRoster_Rested_Enabled });
            Slots.Add(new Slot { AssetName = "GP_Eikthyr", Enabled = WonderlandConfig.BuffRoster_GP_Eikthyr_Enabled });
            Slots.Add(new Slot { AssetName = "GP_Bonemass", Enabled = WonderlandConfig.BuffRoster_GP_Bonemass_Enabled });
            Slots.Add(new Slot { AssetName = "GP_TheElder", Enabled = WonderlandConfig.BuffRoster_GP_TheElder_Enabled });
            Slots.Add(new Slot { AssetName = "GP_Yagluth", Enabled = WonderlandConfig.BuffRoster_GP_Yagluth_Enabled });
            Slots.Add(new Slot { AssetName = "GP_Queen", Enabled = WonderlandConfig.BuffRoster_GP_Queen_Enabled });
        }

        public static void OnWorldReady()
        {
            BuildRosterOnce();
            TryResolveRoster();
        }

        /// <summary>
        /// ObjectDB.instance is frequently still null at the ZNetScene.Awake postfix this codebase
        /// otherwise treats as "world ready" - live-tested and confirmed 2026-09-11: WaterBuoyancyEngine's
        /// own ObjectDB.instance read at that exact hook silently no-ops for the same reason, masked there
        /// because its ZNetScene-prefab-scan loop happens to find every item anyway. Rather than depend on
        /// exactly when Unity happens to run ObjectDB's own Awake() relative to ZNetScene's, this retries
        /// once per tick from OnUpdate until it succeeds - cheap (a null check) when not yet resolved, and
        /// entirely skipped once it is.
        /// </summary>
        private static void TryResolveRoster()
        {
            if (_resolved || ObjectDB.instance == null)
            {
                return;
            }
            _resolved = true;

            StatusEffectRegistry.Rebuild();

            foreach (Slot slot in Slots)
            {
                if (!StatusEffectRegistry.TryGet(slot.AssetName, out StatusEffect effect))
                {
                    WonderlandDebug.LogWarning($"[BuffRoster] '{slot.AssetName}' was not found in ObjectDB - this slot will be skipped even if enabled (renamed or removed by a game update?).");
                    slot.NameHash = 0;
                    continue;
                }

                slot.NameHash = effect.NameHash();
                slot.RepingInterval = effect.m_ttl <= 0f
                    ? 120f // naturally permanent (IsDone() never trips) - an occasional re-ping is cheap and harmless
                    : Mathf.Max(0.2f, Mathf.Min(effect.m_ttl * 0.4f, effect.m_ttl - 0.2f));
                slot.Cooldown.Clear();

                WonderlandDebug.LogInfo($"[BuffRoster] resolved '{slot.AssetName}' (hash {slot.NameHash}, ttl {effect.m_ttl:0.#}s, re-ping every {slot.RepingInterval:0.##}s).");
            }
        }

        public static void OnUpdate(float dt)
        {
            if (WonderlandConfig.BuffRosterEnabled?.Value != true)
            {
                return;
            }

            if (!_resolved)
            {
                BuildRosterOnce();
                TryResolveRoster();
                if (!_resolved)
                {
                    return;
                }
            }

            List<ConnectedCharacter> characters = ConnectedCharacters.All();
            if (characters.Count == 0)
            {
                return;
            }

            foreach (Slot slot in Slots)
            {
                if (slot.NameHash == 0 || slot.Enabled?.Value != true)
                {
                    continue;
                }

                foreach (ConnectedCharacter character in characters)
                {
                    long playerId = character.PlayerId;
                    if (playerId == 0L)
                    {
                        continue; // Player.SetPlayerID hasn't written the identity into the ZDO yet - next tick.
                    }

                    if (slot.RequireBoat?.Value == true && !ShipAttachment.IsSteeringShip(character))
                    {
                        // Not currently eligible - forget any leftover cooldown rather than freezing it.
                        // Freezing it was the bug: leaving the boat mid-cooldown (e.g. 20s into a 120s
                        // re-ping window) left that ~100s remaining "counting" only while back on a boat,
                        // so a player who hopped on/off in short bursts could go the whole session after
                        // the very first grant without ever reaching zero again. Removing the entry means
                        // re-boarding (the same ship or a different one) always grants immediately.
                        slot.Cooldown.Remove(playerId);
                        continue;
                    }

                    slot.Cooldown.TryGetValue(playerId, out float due);
                    due -= dt;
                    if (due > 0f)
                    {
                        slot.Cooldown[playerId] = due;
                        continue;
                    }

                    StatusEffectRpc.Grant(character, slot.NameHash);
                    slot.Cooldown[playerId] = slot.RepingInterval;
                }
            }
        }
    }
}
