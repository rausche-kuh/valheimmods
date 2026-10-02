using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Crafting, upgrading and building take their materials from the chests around you, without
    /// a chest being opened. What you carry is spent first; only the shortfall is taken from the
    /// chests, nearest first. The counts in the crafting panel and the build HUD include the
    /// chests, so a recipe reads as craftable exactly when it is.
    ///
    /// The tweak itself only says when: it opens the shared reach (see NearbyChests) around the
    /// game's own requirement checks and spends, and closes it after. Outside those, the
    /// backpack is vanilla, so selling to a trader or feeding a boar never touches a chest.
    /// </summary>
    internal sealed class NearbyCrafting : Tweak
    {
        internal static readonly NearbyCrafting Instance = new NearbyCrafting();

        private NearbyCrafting() { }

        internal override string Section => "Nearby Crafting";

        protected override string Summary =>
            "Crafting, upgrading and building take their materials from chests around you " +
            "(General.ChestRange), without opening them. Only chests placed by a player, and not " +
            "chests switched off with the Nearby use button in their panel.";

        private static bool Enter(Player player) => NearbyChests.EnterReach(Instance, player);

        private static void Leave(bool entered) => NearbyChests.LeaveReach(entered);

        /// <summary>
        /// The recipe check. A recipe that takes any one of its ingredients is left out: the game
        /// picks the ingredient by looking it up in the backpack afterwards, and a chest-only
        /// count would make the panel promise a craft that the lookup then fails.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
        private static class RecipeScope
        {
            private static void Prefix(Player __instance, Recipe piece, out bool __state)
            {
                __state = piece != null && !piece.m_requireOnlyOneIngredient && Enter(__instance);
            }

            private static void Finalizer(bool __state) => Leave(__state);
        }

        /// <summary>The build check: the piece list, the placement ghost, the piece info.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Piece), typeof(Player.RequirementMode) })]
        private static class BuildScope
        {
            private static void Prefix(Player __instance, out bool __state) => __state = Enter(__instance);

            private static void Finalizer(bool __state) => Leave(__state);
        }

        /// <summary>The spend, for a craft, an upgrade and a placed piece alike.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        private static class SpendScope
        {
            private static void Prefix(Player __instance, out bool __state) => __state = Enter(__instance);

            private static void Finalizer(bool __state) => Leave(__state);
        }

        /// <summary>
        /// The ingredient rows of the crafting panel and the build HUD. The row counts inside the
        /// reach, so a need the chests cover is not red. The postfix then tells the two apart:
        /// an amount the backpack covers stays white as in vanilla, one that needs the chests
        /// turns yellow, and the row's tooltip lists what is carried and what the chests hold.
        /// The rows of a recipe that takes any one ingredient are left vanilla, like its check.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
        private static class RowScope
        {
            internal struct State
            {
                public bool Entered;
                public int Carried;
            }

            private static void Prefix(Piece.Requirement req, Player player, bool craft, out State __state)
            {
                __state = default;
                if (req == null || req.m_resItem == null || (craft && SelectedRecipeTakesAnyOne()))
                {
                    return;
                }
                __state.Carried = player != null ? player.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name) : 0;
                __state.Entered = Enter(player);
            }

            private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, bool craft, int quality, int craftMultiplier, bool __result, State __state)
            {
                if (!__state.Entered || !__result || elementRoot == null)
                {
                    return;
                }
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int fromChests = player.GetInventory().CountItems(name) - __state.Carried;
                if (fromChests < 0)
                {
                    fromChests = 0;
                }
                UITooltip tooltip = elementRoot.GetComponent<UITooltip>();
                if (tooltip != null)
                {
                    // The counts are put in here rather than left to the tooltip, which
                    // translates what it is given but cannot fill in a $1.
                    Localization localization = Localization.instance;
                    tooltip.m_text = localization.Localize(name) +
                        "\n" + localization.Localize("$omp_carried", __state.Carried.ToString()) +
                        "\n" + Palette.ChestYellowTag +
                        localization.Localize("$omp_from_chests", fromChests.ToString()) + "</color>";
                }
                int need = req.GetAmount(quality) * craftMultiplier;
                bool free = craft
                    ? ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)
                    : ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);
                if (free || __state.Carried >= need || __state.Carried + fromChests < need)
                {
                    return;
                }
                Transform amount = elementRoot.Find("res_amount");
                Graphic text = amount != null ? amount.GetComponent<Graphic>() : null;
                if (text != null)
                {
                    text.color = Palette.ChestYellow;
                }
            }

            private static void Finalizer(State __state) => Leave(__state.Entered);

            private static bool SelectedRecipeTakesAnyOne()
            {
                InventoryGui gui = InventoryGui.instance;
                return gui != null && gui.m_selectedRecipe.Recipe != null && gui.m_selectedRecipe.Recipe.m_requireOnlyOneIngredient;
            }
        }
    }
}
