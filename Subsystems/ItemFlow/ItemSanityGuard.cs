using Wonderland.Subsystems.Storage;

namespace Wonderland.Subsystems.ItemFlow
{
    /// <summary>
    /// The point-of-entry half of the item-fabrication check (see the plan's Security section) - every
    /// pickup the vacuum/production-supply engines consider gets checked here before it's allowed to
    /// move anywhere. The standing integrity sweep (Security/ItemIntegritySweep.cs) runs the same
    /// check against everything already sitting in a container, independent of whether it ever passed
    /// through here. The ceiling is always Wonderland's own configured/boosted max
    /// (StackCapacity.GetIntendedMaxStack), never vanilla's raw number - checking against vanilla would
    /// flag the mod's own legitimate boosted stacks as fabricated the moment StackCapacity is enabled.
    /// </summary>
    public static class ItemSanityGuard
    {
        public static bool IsPlausible(ItemDrop.ItemData item, out string reason)
        {
            if (item == null || item.m_shared == null || item.m_dropPrefab == null || string.IsNullOrEmpty(item.m_shared.m_name))
            {
                reason = "missing item/shared data";
                return false;
            }

            if (item.m_stack < 1)
            {
                reason = $"stack {item.m_stack} below 1";
                return false;
            }

            int ceiling = StackCapacity.GetIntendedMaxStack(item.m_dropPrefab.name, item.m_shared.m_maxStackSize);
            if (item.m_stack > ceiling)
            {
                reason = $"stack {item.m_stack} exceeds ceiling {ceiling} for '{item.m_dropPrefab.name}'";
                return false;
            }

            if (item.m_shared.m_maxQuality > 0 && (item.m_quality < 1 || item.m_quality > item.m_shared.m_maxQuality))
            {
                reason = $"quality {item.m_quality} outside 1..{item.m_shared.m_maxQuality} for '{item.m_dropPrefab.name}'";
                return false;
            }

            if (item.m_shared.m_variants > 0 && (item.m_variant < 0 || item.m_variant >= item.m_shared.m_variants))
            {
                reason = $"variant {item.m_variant} outside 0..{item.m_shared.m_variants - 1} for '{item.m_dropPrefab.name}'";
                return false;
            }

            reason = "";
            return true;
        }
    }
}
