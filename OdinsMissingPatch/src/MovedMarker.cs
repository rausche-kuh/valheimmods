using HarmonyLib;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The item magnet's answer to hauling: an item on the ground that has been moved once - pulled
    /// by the magnet, or dropped by a player - carries a mark on its ZDO and is never pulled again.
    /// Pull a pile to your feet, walk on and it stays behind; pick it up and drop it further on and
    /// it is a player drop. Only the owner of a ZDO may write it, and both are owners at that
    /// moment: the magnet moves only what it owns, and the dropper creates the drop.
    /// </summary>
    internal static class MovedMarker
    {
        internal static readonly int Key = "omp_moved".GetStableHashCode();

        internal static bool IsMoved(ItemDrop item)
        {
            ZDO zdo = item.m_nview != null && item.m_nview.IsValid() ? item.m_nview.GetZDO() : null;
            return zdo != null && zdo.GetBool(Key);
        }

        internal static void Mark(ItemDrop item)
        {
            ZNetView nview = item.m_nview;
            if (nview != null && nview.IsValid() && nview.IsOwner())
            {
                nview.GetZDO().Set(Key, true);
            }
        }

        /// <summary>Humanoid.DropItem calls this on every drop a player makes, right after creating it.</summary>
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnPlayerDrop))]
        [Serves(typeof(ItemMagnet))]
        private static class MarkPlayerDrop
        {
            private static void Postfix(ItemDrop __instance)
            {
                Mark(__instance);
            }
        }

        /// <summary>
        /// With more than 200 drops loaded, the game merges stacks lying within 4m of each other:
        /// the stack doing it absorbs the others and they are destroyed, so a marked stack merged
        /// into an unmarked one would come out clean. The game's own loop, with the mark carried
        /// over to the survivor - a filter over a private list is replaced, not wrapped (see
        /// "Writing a tweak" in docs/conventions.md).
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.AutoStackItems))]
        [Serves(typeof(ItemMagnet))]
        private static class KeepMarkWhenMerged
        {
            private static bool Prefix(ItemDrop __instance)
            {
                if (!ItemMagnet.Instance.On)
                {
                    return true;
                }
                ItemDrop.ItemData data = __instance.m_itemData;
                if (data.m_shared.m_maxStackSize <= 1 || data.m_stack >= data.m_shared.m_maxStackSize || __instance.m_haveAutoStacked)
                {
                    return false;
                }
                __instance.m_haveAutoStacked = true;
                if (ItemDrop.s_itemMask == 0)
                {
                    ItemDrop.s_itemMask = LayerMask.GetMask("item");
                }
                bool merged = false;
                bool marked = false;
                foreach (Collider collider in Physics.OverlapSphere(__instance.transform.position, 4f, ItemDrop.s_itemMask))
                {
                    if (!collider.attachedRigidbody)
                    {
                        continue;
                    }
                    ItemDrop other = collider.attachedRigidbody.GetComponent<ItemDrop>();
                    if (other == null || other == __instance || !other.m_itemData.m_shared.m_autoStack
                        || other.m_nview == null || !other.m_nview.IsValid() || !other.m_nview.IsOwner()
                        || other.m_itemData.m_shared.m_name != data.m_shared.m_name
                        || other.m_itemData.m_quality != data.m_quality)
                    {
                        continue;
                    }
                    int room = data.m_shared.m_maxStackSize - data.m_stack;
                    if (room == 0)
                    {
                        break;
                    }
                    if (other.m_itemData.m_stack <= room)
                    {
                        data.m_stack += other.m_itemData.m_stack;
                        merged = true;
                        marked |= IsMoved(other);
                        other.m_nview.Destroy();
                    }
                }
                if (merged)
                {
                    __instance.Save();
                }
                if (marked)
                {
                    Mark(__instance);
                }
                return false;
            }
        }
    }
}
