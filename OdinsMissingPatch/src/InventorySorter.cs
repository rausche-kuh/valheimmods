using System;
using System.Collections.Generic;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Sorts an inventory in place: stacks of the same item are merged, then everything is laid
    /// out row by row by kind (weapons, shields, tools, armour, belts and trinkets, ammunition,
    /// food, materials, trophies, the rest), name and quality. Materials come first by whether a
    /// portal carries them, then by family and depth in the crafting tree (see
    /// <see cref="MaterialOrder"/>), and only then by name - so metals sit with metals and logs
    /// with logs. Rows above a chosen one and items a caller wants kept are not touched and keep
    /// their slot; the rest flows around them.
    /// </summary>
    internal static class InventorySorter
    {
        /// <summary>
        /// Sorts rows <paramref name="fromRow"/> and below, leaving every item <paramref name="keep"/>
        /// accepts where it is. False if nothing could be laid out, in which case nothing changed.
        /// </summary>
        internal static bool Sort(Inventory inventory, int fromRow, Func<ItemDrop.ItemData, bool> keep)
        {
            if (inventory == null)
            {
                return false;
            }
            List<ItemDrop.ItemData> all = inventory.GetAllItems();
            int width = inventory.GetWidth();
            int height = inventory.GetHeight();
            bool[,] fixedSlot = new bool[width, height];
            List<ItemDrop.ItemData> movable = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in all)
            {
                Vector2i pos = item.m_gridPos;
                bool inRange = pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
                if (inRange && pos.y >= fromRow && (keep == null || !keep(item)))
                {
                    movable.Add(item);
                }
                else if (inRange)
                {
                    fixedSlot[pos.x, pos.y] = true;
                }
            }
            if (movable.Count == 0)
            {
                return false;
            }
            List<Vector2i> free = new List<Vector2i>();
            for (int y = fromRow; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!fixedSlot[x, y])
                    {
                        free.Add(new Vector2i(x, y));
                    }
                }
            }
            if (free.Count < movable.Count)
            {
                return false;
            }

            MaterialOrder.Prepare();
            movable.Sort(Compare);
            List<ItemDrop.ItemData> merged = Merge(movable, all);
            for (int i = 0; i < merged.Count; i++)
            {
                merged[i].m_gridPos = free[i];
            }
            inventory.Changed();
            return true;
        }

        /// <summary>
        /// Pours each stack into the earlier stacks of the same item that have room, the way the
        /// game's own add does, and drops a stack from the inventory once it is empty. Sorted
        /// input keeps like stacks together, so the fuller stacks come first.
        /// </summary>
        private static List<ItemDrop.ItemData> Merge(List<ItemDrop.ItemData> sorted, List<ItemDrop.ItemData> all)
        {
            List<ItemDrop.ItemData> merged = new List<ItemDrop.ItemData>(sorted.Count);
            foreach (ItemDrop.ItemData item in sorted)
            {
                if (item.m_shared.m_maxStackSize > 1)
                {
                    foreach (ItemDrop.ItemData target in merged)
                    {
                        if (item.m_stack <= 0)
                        {
                            break;
                        }
                        if (!Merges(target, item))
                        {
                            continue;
                        }
                        int move = Math.Min(target.m_shared.m_maxStackSize - target.m_stack, item.m_stack);
                        target.m_stack += move;
                        item.m_stack -= move;
                    }
                }
                if (item.m_stack > 0)
                {
                    merged.Add(item);
                }
                else
                {
                    all.Remove(item);
                }
            }
            return merged;
        }

        /// <summary>
        /// Whether units of <paramref name="item"/> may join <paramref name="stack"/>: the same
        /// kind of stack and room left under its cap. The one merge rule of the sort and of the
        /// chest panel's Fill your stacks and quick stack's top-up.
        /// </summary>
        internal static bool Merges(ItemDrop.ItemData stack, ItemDrop.ItemData item)
        {
            return stack.m_stack < stack.m_shared.m_maxStackSize && SameStack(stack, item);
        }

        /// <summary>
        /// What the game matches when it tops a stack up (name, quality, world level, cheated),
        /// plus the variant and the custom data, so a stack another mod has tagged never swallows
        /// a plain one. The favourite mark is left out: it belongs to the stack in the backpack,
        /// not to the item, so a favourite is still topped up from a chest.
        /// </summary>
        private static bool SameStack(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            if (a.m_shared.m_name != b.m_shared.m_name || a.m_quality != b.m_quality
                || a.m_variant != b.m_variant || a.m_worldLevel != b.m_worldLevel || a.m_cheated != b.m_cheated)
            {
                return false;
            }
            return Covers(a.m_customData, b.m_customData) && Covers(b.m_customData, a.m_customData);
        }

        /// <summary>Whether every custom entry of <paramref name="a"/> but the favourite mark is in <paramref name="b"/>.</summary>
        private static bool Covers(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            foreach (KeyValuePair<string, string> pair in a)
            {
                if (pair.Key != QuickStack.FavoriteKey
                    && (!b.TryGetValue(pair.Key, out string value) || value != pair.Value))
                {
                    return false;
                }
            }
            return true;
        }

        private static int Compare(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int order = Rank(a.m_shared.m_itemType).CompareTo(Rank(b.m_shared.m_itemType));
            if (order != 0)
            {
                return order;
            }
            if (a.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material
                && b.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material)
            {
                // Everything a portal refuses in one block at the end of the materials, then the
                // crafting tree's own grouping; the name only settles what it leaves tied.
                order = Portable(a).CompareTo(Portable(b));
                if (order != 0)
                {
                    return order;
                }
                order = MaterialOrder.Compare(a, b);
                if (order != 0)
                {
                    return order;
                }
            }
            order = string.Compare(DisplayName(a), DisplayName(b), StringComparison.CurrentCultureIgnoreCase);
            if (order != 0)
            {
                return order;
            }
            order = b.m_quality.CompareTo(a.m_quality);
            if (order != 0)
            {
                return order;
            }
            order = a.m_variant.CompareTo(b.m_variant);
            return order != 0 ? order : b.m_stack.CompareTo(a.m_stack);
        }

        /// <summary>0 for what a portal takes, 1 for the ores and bars it does not.</summary>
        private static int Portable(ItemDrop.ItemData item)
        {
            return item.m_shared.m_teleportable ? 0 : 1;
        }

        /// <summary>The name as the player reads it, so the order matches the tooltips, not the tokens.</summary>
        private static string DisplayName(ItemDrop.ItemData item)
        {
            string name = item.m_shared.m_name;
            Localization localization = Localization.instance;
            return localization != null ? localization.Localize(name) : name;
        }

        private static int Rank(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return 0;
                case ItemDrop.ItemData.ItemType.Shield: return 1;
                case ItemDrop.ItemData.ItemType.Tool: return 2;
                case ItemDrop.ItemData.ItemType.Helmet: return 3;
                case ItemDrop.ItemData.ItemType.Chest: return 4;
                case ItemDrop.ItemData.ItemType.Legs: return 5;
                case ItemDrop.ItemData.ItemType.Shoulder: return 6;
                case ItemDrop.ItemData.ItemType.Hands: return 7;
                case ItemDrop.ItemData.ItemType.Customization: return 8;
                case ItemDrop.ItemData.ItemType.Utility: return 9;
                case ItemDrop.ItemData.ItemType.Trinket: return 10;
                case ItemDrop.ItemData.ItemType.Ammo: return 11;
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return 12;
                case ItemDrop.ItemData.ItemType.Consumable: return 13;
                case ItemDrop.ItemData.ItemType.Fish: return 14;
                case ItemDrop.ItemData.ItemType.Material: return 15;
                case ItemDrop.ItemData.ItemType.Trophy: return 16;
                case ItemDrop.ItemData.ItemType.Misc: return 17;
                default: return 18;
            }
        }
    }
}
