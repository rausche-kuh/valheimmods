namespace OdinsMissingPatch
{
    /// <summary>
    /// The rules for putting the backpack away into a chest, shared by quick stacking and the
    /// chest panel's Place all and Fill the chest: what may leave the backpack, which chest takes
    /// which kind of item, and the move itself.
    /// </summary>
    internal static class Stash
    {
        /// <summary>
        /// Worn or a favourite: what nothing but the player moves. The backpack's Sort keeps
        /// these in their slot, and nothing stacks them away.
        /// </summary>
        internal static bool StaysPut(Player player, ItemDrop.ItemData item)
        {
            return item.m_equipped || player.IsItemEquiped(item) || QuickStack.IsFavorite(item);
        }

        /// <summary>
        /// What a whole-backpack move (a quick stack, Place all, Fill the chest) may take: not
        /// worn, not a favourite, and not on the hotbar while the hotbar is kept.
        /// </summary>
        internal static bool MayLeave(Player player, ItemDrop.ItemData item)
        {
            return !StaysPut(player, item) && !SharedSettings.OnKeptHotbar(item);
        }

        /// <summary>
        /// Whether a chest is one this kind of item may go to: it already holds one, or it has
        /// been marked for it (<see cref="ChestFavorites"/>), which is the same thing said in
        /// advance. <paramref name="marks"/> is <see cref="ChestFavorites.Marks"/> of the chest.
        /// </summary>
        internal static bool Takes(Inventory chest, string marks, string itemName)
        {
            return chest.ContainsItemByName(itemName) || ChestFavorites.Marked(marks, itemName);
        }

        /// <summary>
        /// Moves as much of <paramref name="item"/> from <paramref name="from"/> into
        /// <paramref name="to"/> as fits, the chest's own stacks first and a free slot after, and
        /// returns how many units went; the whole stack went when that is the stack it had. The
        /// game's add either takes everything, and the item then has to leave the source, or
        /// merges what fits and leaves the item, smaller, where it was.
        /// </summary>
        internal static int Move(ItemDrop.ItemData item, Inventory from, Inventory to)
        {
            // AddItem logs an error, rather than declining, when nothing fits.
            if (!to.HaveEmptySlot() && to.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel) <= 0)
            {
                return 0;
            }
            int before = item.m_stack;
            if (to.AddItem(item))
            {
                // Merged into the target's stacks, or the stack itself now sits in a free slot
                // there. Either way it leaves the source.
                from.RemoveItem(item);
                return before;
            }
            int part = before - item.m_stack;
            if (part > 0)
            {
                from.Changed();
            }
            return part;
        }
    }
}
