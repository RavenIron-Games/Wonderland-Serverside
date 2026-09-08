using System;
using System.Collections.Generic;
using UnityEngine;
using Wonderland.Core;

namespace Wonderland.Subsystems.ProductionControl
{
    public static class AutoFuelManager
    {
        private static float _lastAutoFuelTime = 0f;

        public static void ProcessAutoFuel()
        {
            if (WonderlandConfig.AutoFuelLightSources == null || !WonderlandConfig.AutoFuelLightSources.Value) return;

            if (Time.time - _lastAutoFuelTime < 3.0f) return;
            _lastAutoFuelTime = Time.time;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead()) return;

            float radius = WonderlandConfig.AutoFuelRadius?.Value ?? 15.0f;
            Vector3 playerPos = localPlayer.transform.position;

            int mask = LayerMask.GetMask("piece", "piece_nonsolid");
            Collider[] colliders = Physics.OverlapSphere(playerPos, radius, mask);

            List<Container> nearbyChests = GetNearbyContainers(playerPos, radius);

            foreach (var col in colliders)
            {
                Fireplace fireplace = col.GetComponentInParent<Fireplace>();
                if (fireplace == null) continue;

                ZNetView nview = fireplace.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                float currentFuel = nview.GetZDO().GetFloat(ZDOVars.s_fuel);
                float maxFuel = fireplace.m_maxFuel;

                if (currentFuel < maxFuel - 1.0f)
                {
                    ItemDrop fuelPrefab = fireplace.m_fuelItem;
                    if (fuelPrefab == null) continue;

                    string fuelItemName = fuelPrefab.m_itemData.m_shared.m_name;

                    foreach (var chest in nearbyChests)
                    {
                        Inventory inv = chest.GetInventory();
                        if (inv == null) continue;

                        ItemDrop.ItemData item = inv.GetItem(fuelItemName);
                        if (item != null)
                        {
                            inv.RemoveOneItem(item);
                            nview.GetZDO().Set(ZDOVars.s_fuel, currentFuel + 1.0f);
                            WonderlandDebug.LogInfo($"Auto-fueled {fireplace.gameObject.name} with {fuelItemName} from chest.");
                            break;
                        }
                    }
                }
            }
        }

        private static List<Container> GetNearbyContainers(Vector3 pos, float radius)
        {
            List<Container> list = new List<Container>();
            Collider[] colliders = Physics.OverlapSphere(pos, radius, LayerMask.GetMask("piece", "piece_nonsolid"));

            foreach (var col in colliders)
            {
                Container c = col.GetComponentInParent<Container>();
                if (c != null && c.GetInventory() != null && !c.IsInUse())
                {
                    list.Add(c);
                }
            }
            return list;
        }
    }
}
