using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// Per-prefab-type max stack size multiplier. Idempotent by construction (StackIT's pattern):
    /// remembers each prefab's vanilla m_maxStackSize the first time it's seen and always recomputes
    /// the target from that remembered original, never from the current live value - the old
    /// Wonderland bug this replaces multiplied m_shared.m_maxStackSize in place on every single
    /// ItemDrop.Awake(), and because m_shared is one instance shared by every drop of that item type,
    /// it compounded without bound for as long as the server stayed up.
    /// Gear (weapons/armor/tools) is always left alone: vanilla's own stack-matching only checks name
    /// + quality, so stacking two swords would silently discard whichever one had less durability.
    /// </summary>
    public static class StackCapacity
    {
        private static readonly Dictionary<string, int> OriginalMaxStack = new Dictionary<string, int>();
        private static bool _applied;

        public static int GetOriginalMaxStack(string prefabName, int fallback)
        {
            return OriginalMaxStack.TryGetValue(prefabName, out int original) ? original : fallback;
        }

        /// <summary>
        /// The ceiling ItemSanityGuard and the overflow guard must check against for a given prefab -
        /// Wonderland's own configured/boosted max, never vanilla's raw number, or the guard would
        /// flag its own legitimate boosted stacks as fabricated the moment this feature is turned on.
        /// </summary>
        public static int GetIntendedMaxStack(string prefabName, int liveSharedMax)
        {
            int original = GetOriginalMaxStack(prefabName, liveSharedMax);
            if (original <= 1 || IsExcluded(prefabName))
            {
                return original;
            }
            float multiplier = WonderlandConfig.StackSizeMultiplier?.Value ?? 1f;
            float target = original * multiplier;
            target = Mathf.Clamp(target, 1f, WonderlandConfig.StackSizeAbsoluteMax?.Value ?? 999f);
            return Mathf.RoundToInt(target);
        }

        /// <summary>
        /// Applies the configured multiplier to every known item prefab's live SharedData. Called
        /// once at startup and again on every config change (ServerSync pushes a live re-bind), and
        /// safe to call any number of times since it always recomputes from the remembered original.
        /// </summary>
        public static void Apply()
        {
            if (WonderlandConfig.StackSizeEnabled?.Value != true)
            {
                return;
            }

            var visited = new HashSet<string>();
            int changed = 0;
            if (ObjectDB.instance != null)
            {
                changed += ApplyToPrefabs(ObjectDB.instance.m_items, visited);
            }
            if (ZNetScene.instance != null)
            {
                changed += ApplyToPrefabs(ZNetScene.instance.m_prefabs, visited);
            }
            _applied = true;
            WonderlandDebug.LogInfo($"[StackCapacity] applied x{WonderlandConfig.StackSizeMultiplier?.Value} to {changed} item prefabs.");
        }

        public static bool HasApplied => _applied;

        private static int ApplyToPrefabs(List<GameObject> prefabs, HashSet<string> visited)
        {
            if (prefabs == null)
            {
                return 0;
            }

            int count = 0;
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
                if (itemDrop == null || itemDrop.m_itemData?.m_shared == null)
                {
                    continue;
                }
                string prefabName = prefab.name;
                if (!visited.Add(prefabName))
                {
                    continue;
                }

                ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
                if (!OriginalMaxStack.ContainsKey(prefabName))
                {
                    OriginalMaxStack[prefabName] = shared.m_maxStackSize;
                }

                if (IsGear(shared.m_itemType) || OriginalMaxStack[prefabName] <= 1)
                {
                    shared.m_maxStackSize = OriginalMaxStack[prefabName];
                    continue;
                }

                shared.m_maxStackSize = GetIntendedMaxStack(prefabName, shared.m_maxStackSize);
                count++;
            }
            return count;
        }

        private static bool IsExcluded(string prefabName)
        {
            string list = WonderlandConfig.StackSizeExcludedPrefabs?.Value ?? "";
            if (string.IsNullOrWhiteSpace(list))
            {
                return false;
            }
            foreach (string entry in list.Split(','))
            {
                if (string.Equals(entry.Trim(), prefabName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsGear(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                case ItemDrop.ItemData.ItemType.Tool:
                    return true;
                default:
                    return false;
            }
        }
    }
}
