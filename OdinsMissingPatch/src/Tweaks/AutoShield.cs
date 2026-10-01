using HarmonyLib;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Drawing a one handed weapon raises a shield with it. Vanilla leaves the off hand empty
    /// unless a shield happens to already be in it, so a sword pulled after a bow is a sword with
    /// nothing to block with until the player finds the second hotbar key.
    ///
    /// Only the empty hand is filled: a shield or a torch already there survives the weapon equip
    /// and is left alone. The shield is picked from what the player carries - favourites first,
    /// then the hotbar row, then the rest of the backpack, and within each of those the slot that
    /// comes first - and it is equipped through the game's own path, so it takes its usual moment
    /// and an attack or a dodge interrupts it as any other equip.
    /// </summary>
    internal sealed class AutoShield : Tweak
    {
        internal static readonly AutoShield Instance = new AutoShield();

        private AutoShield() { }

        // The one handed weapon the player has just asked for, from the press until it lands.
        // A weapon that is equipped by anything but a deliberate press - the game restoring what
        // was worn at logout, hands taken back out after building, a drag in the inventory that
        // re-equips what was already equipped - is never in here, so none of those pull a shield.
        private static ItemDrop.ItemData pending;

        internal override string Section => "Auto Shield";

        protected override string Summary =>
            "Equipping a one handed weapon raises a shield with it, if your off hand is empty " +
            "and you carry one.";

        /// <summary>
        /// The shield to raise: a favourite before anything on the hotbar, the hotbar before the
        /// rest of the backpack, and the first slot within whichever of those wins - reading the
        /// grid the way it is drawn, along each row and then down. Broken shields are skipped,
        /// since <see cref="Humanoid.EquipItem"/> would refuse them anyway.
        /// </summary>
        private static ItemDrop.ItemData FindShield(Player player)
        {
            ItemDrop.ItemData best = null;
            int bestRank = int.MaxValue;

            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield
                    || player.IsItemEquiped(item)
                    || (item.m_shared.m_useDurability && item.m_durability <= 0f))
                {
                    continue;
                }

                int rank = QuickStack.IsFavorite(item) ? 0 : item.m_gridPos.y == 0 ? 1 : 2;
                if (best != null && (rank > bestRank || (rank == bestRank && !Before(item, best))))
                {
                    continue;
                }
                best = item;
                bestRank = rank;
            }

            return best;
        }

        /// <summary>Whether one slot is reached before another, along the row and then down.</summary>
        private static bool Before(ItemDrop.ItemData item, ItemDrop.ItemData other)
        {
            return item.m_gridPos.y != other.m_gridPos.y
                ? item.m_gridPos.y < other.m_gridPos.y
                : item.m_gridPos.x < other.m_gridPos.x;
        }

        /// <summary>
        /// A hotbar key or a use in the inventory screen both arrive here, and only for something
        /// the player meant to take in hand. The same press on an item that is already equipped
        /// takes it off, and one on an item whose equip is still queued cancels it - neither is a
        /// weapon being drawn, so neither arms the shield.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ToggleEquipped))]
        private static class NoticeWeaponPress
        {
            private static void Prefix(Player __instance, ItemDrop.ItemData item)
            {
                if (!Instance.On || __instance != Player.m_localPlayer || item == null
                    || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon)
                {
                    return;
                }

                if (__instance.IsItemEquiped(item) || __instance.IsEquipActionQueued(item))
                {
                    // Taking it off, or calling off an equip that is still waiting its duration
                    // out - so whatever this press is, it is not a weapon being drawn, and the
                    // mark the press that queued it left behind goes with it.
                    if (pending == item)
                    {
                        pending = null;
                    }
                    return;
                }
                pending = item;
            }
        }

        /// <summary>
        /// The weapon lands here, whether it was equipped on the spot or waited out its duration
        /// in the action queue - so the off hand is in its final state and the question is simply
        /// whether it is empty. A shield or a torch that was already in it survives the weapon
        /// (see <see cref="Humanoid.EquipItem"/>'s one handed branch) and is what fills it here.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class RaiseShield
        {
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
            {
                if (!__result || item == null || item != pending)
                {
                    return;
                }
                pending = null;

                Player player = __instance as Player;
                if (!Instance.On || player == null || player != Player.m_localPlayer)
                {
                    return;
                }
                if (player.GetLeftItem() != null)
                {
                    return;
                }

                ItemDrop.ItemData shield = FindShield(player);
                if (shield == null)
                {
                    return;
                }
                // The player's own press, asked for them: the equip duration, the animation and
                // every refusal the game makes are the ones a second hotbar key would have got.
                player.ToggleEquipped(shield);
            }
        }
    }
}
