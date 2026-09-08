using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.InventoryControl
{
    public static class ContainerVacuumEngine
    {
        private static float _lastVacuumTime = 0f;

        public static void ProcessVacuumScan()
        {
            if (WonderlandConfig.ContainerVacuumEnabled == null || !WonderlandConfig.ContainerVacuumEnabled.Value) return;
            float interval = WonderlandConfig.ContainerVacuumInterval?.Value ?? 2.0f;

            if (Time.time - _lastVacuumTime < interval) return;
            _lastVacuumTime = Time.time;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead()) return;

            float radius = WonderlandConfig.ContainerVacuumRadius?.Value ?? 10.0f;
            Vector3 playerPos = localPlayer.transform.position;

            int containerMask = LayerMask.GetMask("piece", "piece_nonsolid");
            Collider[] colliders = Physics.OverlapSphere(playerPos, radius, containerMask);

            foreach (var col in colliders)
            {
                Container container = col.GetComponentInParent<Container>();
                if (container == null || container.GetInventory() == null || container.IsInUse()) continue;

                VacuumGroundItemsToContainer(container, radius);
            }
        }

        private static void VacuumGroundItemsToContainer(Container container, float radius)
        {
            Vector3 chestPos = container.transform.position;
            Collider[] itemColliders = Physics.OverlapSphere(chestPos, radius, LayerMask.GetMask("item"));

            Inventory chestInventory = container.GetInventory();

            foreach (var itemCol in itemColliders)
            {
                ItemDrop itemDrop = itemCol.GetComponentInParent<ItemDrop>();
                if (itemDrop == null || !itemDrop.CanPickup() || itemDrop.m_itemData == null) continue;

                string itemName = itemDrop.m_itemData.m_shared.m_name;

                if (chestInventory.ContainsItem(itemDrop.m_itemData))
                {
                    int amountToAdd = itemDrop.m_itemData.m_stack;
                    if (chestInventory.AddItem(itemDrop.m_itemData))
                    {
                        ZNetScene.instance.Destroy(itemDrop.gameObject);
                        WonderlandDebug.LogInfo($"Container vacuumed {amountToAdd} x {itemName} into chest.");
                    }
                }
            }
        }
    }
}
