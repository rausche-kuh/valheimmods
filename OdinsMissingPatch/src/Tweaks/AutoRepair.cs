using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Walking up to a workbench and pressing Use repairs everything you carry that the bench can
    /// repair, instead of leaving you clicking the hammer button once per item. The forge mends
    /// what belongs to the forge, the workbench what belongs to the workbench - which is what the
    /// repair button does, asked for every worn item at once.
    ///
    /// Nothing about a repair itself changes. Each item is the game's own repair: the same
    /// question about whether this station may repair it, the same full durability, the same
    /// crafting skill for the wear that was mended, the same station effect. Repairing is free in
    /// Valheim, so there is nothing to pay and nothing to run out of.
    /// </summary>
    internal sealed class AutoRepair : Tweak
    {
        internal static readonly AutoRepair Instance = new AutoRepair();

        private AutoRepair() { }

        // The worn items of one visit. Reused rather than handing out a new list per station.
        private static readonly List<ItemDrop.ItemData> Worn = new List<ItemDrop.ItemData>();

        internal override string Section => "Auto Repair";

        protected override string Summary =>
            "Opening a crafting station repairs everything you carry that it can repair, " +
            "instead of one item per click of the repair button.";

        /// <summary>
        /// Every worn item the station is allowed to repair, repaired. The question asked per item
        /// is the crafting panel's own <see cref="InventoryGui.CanRepair"/> - the item's recipe
        /// names this station (or the world has outgrown the item), the station is high enough
        /// level, and the item can be repaired at all - so exactly what the repair button would
        /// have worked through is worked through here, in one go.
        /// </summary>
        private void RepairEverything(Player player, CraftingStation station)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !station.m_canRepair)
            {
                return;
            }

            Worn.Clear();
            player.GetInventory().GetWornItems(Worn);
            int repaired = 0;
            foreach (ItemDrop.ItemData item in Worn)
            {
                if (!gui.CanRepair(item))
                {
                    continue;
                }
                player.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                repaired++;
            }
            Worn.Clear();

            if (repaired == 0)
            {
                return;
            }
            // Once for the visit, not once per item: the station's clang is a sound, and a
            // dozen of them on the same frame is a noise.
            station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize("$msg_repaired", repaired.ToString()));
        }

        /// <summary>
        /// Pressing Use on a station is the one place the game hands the station to the player,
        /// and it only does so once it has decided the station may be used from where the player
        /// stands - so a postfix that finds the player holding this station is a station that has
        /// just been opened, and nothing else. The player lets go of the station again the moment
        /// the panel closes, so opening it again repairs again.
        /// </summary>
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
        private static class RepairOnOpen
        {
            private static void Postfix(CraftingStation __instance, Humanoid user, bool repeat)
            {
                // A held Use key repeats; the game turns those away before it opens anything.
                if (!Instance.On || repeat)
                {
                    return;
                }
                Player player = user as Player;
                if (player == null || player != Player.m_localPlayer)
                {
                    return;
                }
                if (player.GetCurrentCraftingStation() != __instance)
                {
                    return;
                }
                Instance.RepairEverything(player, __instance);
            }
        }
    }
}
