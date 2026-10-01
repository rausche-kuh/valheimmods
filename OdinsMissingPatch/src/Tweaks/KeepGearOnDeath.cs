using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Dying keeps your gear. Weapons, armour, ammunition, tools, the belt and the food you carry
    /// stay in your inventory, and stay equipped, so you respawn ready to fight your way back;
    /// only the loot of the run - materials, trophies, fish, coins - goes to the grave.
    ///
    /// It only applies on a world whose death penalty is on the lowest setting - "Casual", the
    /// one that keeps the items you happen to have equipped and so leaves the spare arrows, the
    /// food and the backup weapon lying in the grave. The tweak decides by item type instead,
    /// from a configurable list, so the whole kit comes back with you. On any harsher world -
    /// Very Easy through Hardcore - it does nothing and the world's own death penalty stands.
    /// </summary>
    internal sealed class KeepGearOnDeath : Tweak
    {
        internal static readonly KeepGearOnDeath Instance = new KeepGearOnDeath();

        private KeepGearOnDeath() { }

        private const string DefaultKeepTypes =
            "OneHandedWeapon, TwoHandedWeapon, TwoHandedWeaponLeft, Bow, Shield, Torch, Tool, " +
            "Helmet, Chest, Legs, Shoulder, Hands, Utility, Trinket, Ammo, AmmoNonEquipable, " +
            "Consumable";

        // Parsed from KeepTypes; re-read whenever the setting changes, since parsing a comma
        // separated list on every item of every death would be silly.
        private HashSet<ItemDrop.ItemData.ItemType> kept = new HashSet<ItemDrop.ItemData.ItemType>();

        // The player whose tombstone is being filled right now, so the unequip patch knows that
        // the unequip it is about to see is the one that happens on death and not, say, a swap.
        private static Player dying;

        internal override string Section => "Keep Gear On Death";

        protected override string Summary =>
            "Dying keeps the item types listed in KeepTypes - weapons, armour, ammunition, tools " +
            "and food by default - in your inventory and equipped; only the rest goes to the " +
            "grave. Applies on worlds whose death penalty is set to Casual, the lowest setting - " +
            "on a harsher world it does nothing.";

        protected override void Bind(ConfigFile config)
        {
            string types = string.Join(", ", Enum.GetNames(typeof(ItemDrop.ItemData.ItemType))
                .Where(name => name != nameof(ItemDrop.ItemData.ItemType.None)).ToArray());
            BindList(config, "KeepTypes", DefaultKeepTypes,
                "Comma separated item types that stay with you when you die. Everything else " +
                "goes to the grave. The types the game knows: " + types + ".", Parse);
        }

        private void Parse(List<string> names)
        {
            var parsed = new HashSet<ItemDrop.ItemData.ItemType>();
            foreach (string name in names)
            {
                try
                {
                    parsed.Add((ItemDrop.ItemData.ItemType)Enum.Parse(typeof(ItemDrop.ItemData.ItemType), name, ignoreCase: true));
                }
                catch (ArgumentException)
                {
                    OdinsMissingPatchPlugin.Log.LogWarning(Section + ": '" + name + "' is not an item type and is ignored");
                }
            }
            kept = parsed;
        }

        /// <summary>
        /// Whether the tweak applies right now: switched on, and on a world whose death penalty is
        /// the lowest step of the slider - "Casual", the one that keeps equipped gear. That step is
        /// exactly <see cref="GlobalKeys.DeathKeepEquip"/> and no other sets it, since from Very
        /// Easy on the grave takes the equipment too. Anywhere harsher the tweak stays out of the
        /// way, so it can never soften a death penalty the world asked for.
        /// </summary>
        private bool Active
        {
            get
            {
                ZoneSystem zones = ZoneSystem.instance;
                return On && zones != null && zones.GetGlobalKey(GlobalKeys.DeathKeepEquip);
            }
        }

        /// <summary>Whether this item stays with the player, by type. Quest items always do.</summary>
        private bool Keeps(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null &&
                (item.m_shared.m_questItem || kept.Contains(item.m_shared.m_itemType));
        }

        /// <summary>
        /// The game's own grave filter - not a quest item, not equipped - with the kept types
        /// taken out. What the player had equipped is still equipped, since keeping equipment is
        /// exactly what the death penalty this tweak runs under does.
        /// </summary>
        private bool Drops(ItemDrop.ItemData item)
        {
            return item != null && !item.m_equipped && !Keeps(item);
        }

        /// <summary>
        /// The death path unequips everything before it fills the grave, so anything that stays
        /// in the inventory comes back unequipped on respawn - unless it is still equipped, which
        /// is how the game's own keep-equipment penalty sends you back into the fight dressed.
        /// While the local player's tombstone is being made, unequipping a kept item is skipped,
        /// so it goes through death equipped and is put back on by the respawn's load. Casual is
        /// that keep-equipment penalty, so the game skips its own unequip anyway and this only
        /// catches a world that set the keys by hand.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        private static class DeathScope
        {
            private static void Prefix(Player __instance)
            {
                dying = Instance.Active ? __instance : null;
            }

            private static void Postfix()
            {
                dying = null;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem), typeof(ItemDrop.ItemData), typeof(bool))]
        private static class KeepEquipped
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
            {
                if (dying == null || __instance != dying || !Instance.Active)
                {
                    return true;
                }
                return !Instance.Keeps(item);
            }
        }

        /// <summary>
        /// The move into the grave: the game's loop, with the kept types left where they are.
        /// Its only caller is the death path.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
        private static class FillGrave
        {
            private static bool Prefix(Inventory __instance, Inventory original)
            {
                if (!Instance.Active || original == null)
                {
                    return true;
                }
                __instance.m_inventory.Clear();
                __instance.m_width = original.m_width;
                __instance.m_height = original.m_height;
                foreach (ItemDrop.ItemData item in original.m_inventory)
                {
                    if (Instance.Drops(item))
                    {
                        __instance.m_inventory.Add(item);
                    }
                }
                original.m_inventory.RemoveAll(Instance.Drops);
                original.Changed();
                __instance.Changed();
                return false;
            }
        }

        /// <summary>
        /// The death penalties that delete instead of dropping call this before the grave is
        /// filled, with the same filter, so the kept types survive that too - again only reachable
        /// on a world that combined those keys with keep-equipment by hand, since the slider's
        /// deleting steps drop the equipment. Its only caller is the death path.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveUnequipped))]
        private static class DeleteUnkept
        {
            private static bool Prefix(Inventory __instance)
            {
                if (!Instance.Active)
                {
                    return true;
                }
                __instance.m_inventory.RemoveAll(Instance.Drops);
                __instance.Changed();
                return false;
            }
        }
    }
}
