using BepInEx.Configuration;
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
    ///
    /// Stations come in groups, so a kit needs one visit rather than four: at any station of a
    /// group, an item of another station of that group is repaired too - as long as that other
    /// station stands in the base, within its own build range of the player, upgraded far enough
    /// for the item's recipe. The base still needs every station; the player only no longer has
    /// to walk to each one. With RequireRealStation off, the workbench and the forge repair
    /// everything, and no other station has to exist at all.
    /// </summary>
    internal sealed class AutoRepair : Tweak
    {
        internal static readonly AutoRepair Instance = new AutoRepair();

        private AutoRepair() { }

        private const string Workbench = "$piece_workbench";
        private const string Forge = "$piece_forge";

        // The game caps the level a recipe is checked against at this, see InventoryGui.CanRepair.
        private const int MaxCheckedLevel = 4;

        private ConfigEntry<bool> groupRepair;
        private ConfigEntry<bool> requireRealStation;

        // Station name -> its group, parsed from the group settings. A station named in both
        // lists belongs to the one read last.
        private Dictionary<string, int> groups = new Dictionary<string, int>();
        private List<string> forgeGroup = new List<string>();
        private List<string> workbenchGroup = new List<string>();

        // The worn items of one visit. Reused rather than handing out a new list per station.
        private static readonly List<ItemDrop.ItemData> Worn = new List<ItemDrop.ItemData>();

        internal override string Section => "Auto Repair";

        protected override string Summary =>
            "Opening a crafting station repairs everything you carry that it can repair, " +
            "instead of one item per click of the repair button.";

        protected override void Bind(ConfigFile config)
        {
            groupRepair = config.Bind(Section, "GroupRepair", true,
                "A station also repairs the items of the other stations in its group (ForgeGroup, " +
                "WorkbenchGroup), at the repair button and when it is opened.");
            requireRealStation = config.Bind(Section, "RequireRealStation", true,
                "With GroupRepair: the item's own station has to stand within its build range of " +
                "you, upgraded far enough for the item. Off: the workbench and the forge repair " +
                "every item, with no other station needed and no station level checked.");
            BindList(config, "ForgeGroup", "$piece_forge, $piece_blackforge",
                "Comma separated station names (the game's $piece_ tokens) that repair each " +
                "other's items.", names => { forgeGroup = names; Regroup(); });
            BindList(config, "WorkbenchGroup",
                "$piece_workbench, $piece_magetable, $piece_artisanstation, $piece_stonecutter, $piece_cauldron",
                "Comma separated station names (the game's $piece_ tokens) that repair each " +
                "other's items.", names => { workbenchGroup = names; Regroup(); });
        }

        private void Regroup()
        {
            var parsed = new Dictionary<string, int>();
            foreach (string name in forgeGroup)
            {
                parsed[name] = 0;
            }
            foreach (string name in workbenchGroup)
            {
                parsed[name] = 1;
            }
            groups = parsed;
        }

        /// <summary>
        /// Whether the station the player stands at may repair an item vanilla turned away, for
        /// its group. With RequireRealStation the item's own station - the recipe's crafting or
        /// repair station - has to be of the same group and stand within its build range of the
        /// player, and the best such station has to be upgraded far enough for the recipe.
        /// </summary>
        private bool GroupMayRepair(Player player, CraftingStation current, ItemDrop.ItemData item)
        {
            if (!requireRealStation.Value)
            {
                return current.m_name == Workbench || current.m_name == Forge;
            }
            if (!groups.TryGetValue(current.m_name, out int group))
            {
                return false;
            }
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null)
            {
                return false;
            }
            int best = Mathf.Max(
                BestLevelInRange(recipe.m_craftingStation, group, player.transform.position),
                BestLevelInRange(recipe.m_repairStation, group, player.transform.position));
            return best > 0 && Mathf.Min(best, MaxCheckedLevel) >= recipe.m_minStationLevel;
        }

        /// <summary>
        /// The highest level among the stations of this kind standing within their build range of
        /// the point, 0 if none does or the kind is not of the group. The game's own
        /// HaveBuildStationInRange stops at the first one, which may be the base's spare.
        /// </summary>
        private int BestLevelInRange(CraftingStation kind, int group, Vector3 point)
        {
            if (kind == null || !groups.TryGetValue(kind.m_name, out int kindGroup) || kindGroup != group)
            {
                return 0;
            }
            int best = 0;
            foreach (CraftingStation station in CraftingStation.m_allStations)
            {
                if (station == null || station.m_name != kind.m_name)
                {
                    continue;
                }
                Vector3 position = station.transform.position;
                point.y = position.y;
                if (Vector3.Distance(position, point) < station.GetStationBuildRange())
                {
                    best = Mathf.Max(best, station.GetLevel());
                }
            }
            return best;
        }

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

        /// <summary>
        /// The crafting panel's own question, "may the station I am at repair this?", widened to
        /// the station's group. Only a no is ever turned into a yes, and only for an item that can
        /// be repaired at all; both the repair button and the repair on opening ask it.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.CanRepair))]
        private static class GroupRepair
        {
            private static void Postfix(ItemDrop.ItemData item, ref bool __result)
            {
                if (__result || !Instance.On || !Instance.groupRepair.Value || item?.m_shared == null
                    || !item.m_shared.m_canBeReparied)
                {
                    return;
                }
                Player player = Player.m_localPlayer;
                CraftingStation current = player != null ? player.GetCurrentCraftingStation() : null;
                if (current == null)
                {
                    return;
                }
                __result = Instance.GroupMayRepair(player, current, item);
            }
        }
    }
}
