using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wonderland.Core;
using Wonderland.Core.Data;

namespace Wonderland.Subsystems.Storage
{
    /// <summary>
    /// Server-side persistent cache for items that would otherwise be lost - specifically container
    /// overflow that cannot fit into vanilla-bounded containers or
    /// nearby siblings. When a vanilla client opens a container whose contents exceed vanilla bounds,
    /// vanilla's Inventory.Load silently discards the excess items; ItemCache extracts and preserves
    /// those items before any client can open the container, stores them on disk across reboots,
    /// and automatically restores them into containers when space becomes available (or drops them
    /// on-demand via server chat command /cache claim).
    /// </summary>
    public static class ItemCache
    {
        public sealed class CachedItem
        {
            public Vector3 Position;
            public long TimestampTicks;
            public string SourceReason = "";
            public string PrefabName = "";
            public ItemDrop.ItemData Item = null!;
        }

        private static readonly List<CachedItem> _cache = new List<CachedItem>();
        private static readonly object _lock = new object();
        private static string? _loadedWorld;

        public static int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }

        public static void Initialize()
        {
            lock (_lock)
            {
                string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "default";
                if (_loadedWorld == worldName)
                {
                    return;
                }

                _cache.Clear();
                _loadedWorld = worldName;
                Load();
            }
        }

        public static void Store(Vector3 pos, ItemDrop.ItemData item, string sourceReason)
        {
            if (item == null || item.m_stack <= 0 || item.m_dropPrefab == null)
            {
                return;
            }

            lock (_lock)
            {
                Initialize();

                ItemDrop.ItemData clone = item.Clone();
                clone.m_dropPrefab = item.m_dropPrefab;
                clone.m_shared = item.m_shared;

                var entry = new CachedItem
                {
                    Position = pos,
                    TimestampTicks = DateTime.UtcNow.Ticks,
                    SourceReason = sourceReason ?? "Unknown",
                    PrefabName = item.m_dropPrefab.name,
                    Item = clone
                };

                _cache.Add(entry);
                Save();

                ItemLedger.RecordTransfer("ItemCache:Store", clone.m_shared?.m_name ?? clone.m_dropPrefab.name, clone.m_stack);
                WonderlandDebug.LogAlways($"[ItemCache] Safely cached {clone.m_stack}x '{clone.m_shared?.m_name ?? clone.m_dropPrefab.name}' from {sourceReason} at {pos:F1} to prevent item loss.");
            }
        }

        /// <summary>
        /// Attempts to drain cached items located near <paramref name="containerZdo"/> into the target
        /// <paramref name="inventory"/>. Returns true if any items were moved (caller must save inventory).
        /// </summary>
        public static bool TryDrainInto(ZDO containerZdo, Inventory inventory, float maxDistance = 30f)
        {
            if (!containerZdo.IsValid() || inventory == null)
            {
                return false;
            }

            Vector3 containerPos = containerZdo.GetPosition();
            bool changed = false;

            lock (_lock)
            {
                Initialize();

                for (int i = _cache.Count - 1; i >= 0; i--)
                {
                    CachedItem cached = _cache[i];
                    if (Vector3.Distance(cached.Position, containerPos) > maxDistance)
                    {
                        continue;
                    }

                    ItemDrop.ItemData item = cached.Item;
                    if (item == null || item.m_stack <= 0)
                    {
                        _cache.RemoveAt(i);
                        continue;
                    }

                    int toMove = item.m_stack;
                    int room = inventory.CanAddItem(item, toMove) ? toMove : CountRoomFor(inventory, item, toMove);
                    if (room <= 0)
                    {
                        continue;
                    }

                    ItemDrop.ItemData addClone = item.Clone();
                    addClone.m_dropPrefab = item.m_dropPrefab;
                    addClone.m_shared = item.m_shared;
                    addClone.m_stack = room;

                    if (inventory.AddItem(addClone))
                    {
                        changed = true;
                        item.m_stack -= room;

                        ItemLedger.RecordTransfer("ItemCache:Drain", item.m_shared?.m_name ?? item.m_dropPrefab.name, room);
                        WonderlandDebug.LogAlways($"[ItemCache] Restored {room}x '{item.m_shared?.m_name ?? item.m_dropPrefab.name}' from cache into container at {containerPos:F1}.");

                        if (item.m_stack <= 0)
                        {
                            _cache.RemoveAt(i);
                        }
                    }
                }

                if (changed)
                {
                    Save();
                }
            }

            return changed;
        }

        /// <summary>
        /// Drops cached items near <paramref name="pos"/> onto the ground at the caller's feet.
        /// If <paramref name="radius"/> is negative, drops ALL cached items globally.
        /// </summary>
        public static int ClaimAt(Vector3 pos, float radius = 30f)
        {
            int droppedStacks = 0;

            lock (_lock)
            {
                Initialize();

                for (int i = _cache.Count - 1; i >= 0; i--)
                {
                    CachedItem cached = _cache[i];
                    if (radius >= 0f && Vector3.Distance(cached.Position, pos) > radius)
                    {
                        continue;
                    }

                    ItemDrop.ItemData item = cached.Item;
                    if (item != null && item.m_stack > 0 && item.m_dropPrefab != null)
                    {
                        Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.8f;
                        float ground = WorldGenerator.instance != null
                            ? WorldGenerator.instance.GetHeight(pos.x + offset.x, pos.z + offset.y)
                            : pos.y;
                        Vector3 dropPos = new Vector3(pos.x + offset.x, Mathf.Max(pos.y, ground) + 0.35f, pos.z + offset.y);

                        ItemDrop.DropItem(item, item.m_stack, dropPos, Quaternion.identity);
                        ItemLedger.RecordTransfer("ItemCache:Claim", item.m_shared?.m_name ?? item.m_dropPrefab.name, item.m_stack);
                        droppedStacks++;
                    }

                    _cache.RemoveAt(i);
                }

                if (droppedStacks > 0)
                {
                    Save();
                    WonderlandDebug.LogAlways($"[ItemCache] Claimed {droppedStacks} stack(s) at {pos:F1}.");
                }
            }

            return droppedStacks;
        }

        public static (int stackCount, int totalQuantity) GetSummary(Vector3 pos, float radius = 30f)
        {
            lock (_lock)
            {
                Initialize();

                int stacks = 0;
                int total = 0;
                foreach (CachedItem cached in _cache)
                {
                    if (radius < 0f || Vector3.Distance(cached.Position, pos) <= radius)
                    {
                        stacks++;
                        total += cached.Item?.m_stack ?? 0;
                    }
                }
                return (stacks, total);
            }
        }

        private static int CountRoomFor(Inventory to, ItemDrop.ItemData item, int amount)
        {
            int lo = 0;
            int hi = amount - 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (to.CanAddItem(item, mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return to.CanAddItem(item, lo) ? lo : 0;
        }

        private static string GetCacheFilePath()
        {
            string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "default";
            string configDir = BepInEx.Paths.ConfigPath;
            return Path.Combine(configDir, $"Wonderland.Cache.{worldName}.dat");
        }

        public static void Save()
        {
            try
            {
                string filePath = GetCacheFilePath();
                string tempPath = filePath + ".tmp";

                var pkg = new ZPackage();
                pkg.Write(1); // Cache data format version
                pkg.Write(_cache.Count);

                foreach (CachedItem entry in _cache)
                {
                    pkg.Write(entry.Position);
                    pkg.Write(entry.TimestampTicks);
                    pkg.Write(entry.SourceReason ?? "");
                    pkg.Write(entry.PrefabName ?? "");

                    // ItemData native serialization
                    entry.Item.Save(pkg);
                }

                File.WriteAllBytes(tempPath, pkg.GetArray());
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogError($"[ItemCache] Failed to save item cache to disk: {ex.Message}");
            }
        }

        public static void Load()
        {
            try
            {
                string filePath = GetCacheFilePath();
                if (!File.Exists(filePath))
                {
                    return;
                }

                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length < 8)
                {
                    return;
                }

                var pkg = new ZPackage(data);
                int formatVersion = pkg.ReadInt();
                if (formatVersion != 1)
                {
                    WonderlandDebug.LogWarning($"[ItemCache] Unknown cache format version: {formatVersion}");
                    return;
                }

                int count = pkg.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = pkg.ReadVector3();
                    long ticks = pkg.ReadLong();
                    string sourceReason = pkg.ReadString();
                    string prefabName = pkg.ReadString();

                    var (prefabHash, itemData) = ItemDrop.ItemData.Load(pkg, (Version.Item)109);

                    GameObject? prefab = null;
                    if (ZNetScene.instance != null)
                    {
                        prefab = !string.IsNullOrEmpty(prefabName) ? ZNetScene.instance.GetPrefab(prefabName) : null;
                        if (prefab == null && prefabHash != 0)
                        {
                            prefab = ZNetScene.instance.GetPrefab(prefabHash);
                        }
                    }

                    if (prefab != null)
                    {
                        ItemDrop template = prefab.GetComponent<ItemDrop>();
                        if (template != null && template.m_itemData?.m_shared != null)
                        {
                            itemData.m_dropPrefab = prefab;
                            itemData.m_shared = template.m_itemData.m_shared;

                            _cache.Add(new CachedItem
                            {
                                Position = pos,
                                TimestampTicks = ticks,
                                SourceReason = sourceReason,
                                PrefabName = prefab.name,
                                Item = itemData
                            });
                        }
                    }
                }

                WonderlandDebug.LogAlways($"[ItemCache] Loaded {_cache.Count} cached item stack(s) from {filePath}.");
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogError($"[ItemCache] Failed to load item cache from disk: {ex.Message}");
            }
        }
    }
}
