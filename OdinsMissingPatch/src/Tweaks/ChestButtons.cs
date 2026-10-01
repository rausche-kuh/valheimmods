using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The chest panel's Take all and Stack all give way to five icon buttons, each hovering to
    /// a name and a line on what it does. Beside the inventory panel, at the top of the
    /// InventoryButtons column between the armour and weight boxes: fill the stacks you carry
    /// from the chest, up to their caps and no further - it never opens a stack you did not
    /// already have. Beside the chest panel, in a column from its top: take all, place all
    /// (everything you carry that is not equipped, a favourite or on the kept hotbar), fill the
    /// chest's stacks from your backpack, and sort the chest. <see cref="PanelButtons"/> builds
    /// and places them and hides the game's two while they show, so switching the tweak off puts
    /// those back.
    /// </summary>
    internal sealed class ChestButtons : Tweak
    {
        internal static readonly ChestButtons Instance = new ChestButtons();

        /// <summary>
        /// Hands the buttons to PanelButtons. Fill your stacks goes ahead of InventoryButtons'
        /// two in the inventory column: it stands where Stack nearby does with no chest open, the
        /// one the chest replaces. Beside the chest, top to bottom: the whole-chest moves, then
        /// the stack move, then sort.
        /// </summary>
        private ChestButtons()
        {
            PanelButtons.Add(PanelButtons.Column.Inventory, 0, "FillInventory", "fill_inventory",
                "$omp_fill_inventory", "$omp_fill_inventory_tip", FillInventory, ChestShown);
            PanelButtons.Add(PanelButtons.Column.Chest, 0, "TakeAll", "take_all",
                "$omp_take_all", "$omp_take_all_tip", TakeAll, ChestShown);
            PanelButtons.Add(PanelButtons.Column.Chest, 1, "PlaceAll", "place_all",
                "$omp_place_all", "$omp_place_all_tip", PlaceAll, ChestShown);
            PanelButtons.Add(PanelButtons.Column.Chest, 2, "FillChest", "fill_chest",
                "$omp_fill_chest", "$omp_fill_chest_tip", FillChest, ChestShown);
            PanelButtons.Add(PanelButtons.Column.Chest, 3, "Sort", "sort",
                "$omp_sort_chest", "$omp_sort_chest_tip", SortChest, ChestShown);
        }

        internal override string Section => "Chest Buttons";

        protected override string Summary =>
            "Replaces the chest panel's Take all and Stack all with icon buttons beside the " +
            "panels: fill your stacks from the chest in the column beside the inventory; take " +
            "all, place all, fill the chest's stacks from your backpack and sort the chest in a " +
            "column down the side of the chest.";

        /// <summary>When every button shows: the tweak is on and the chest panel is up.</summary>
        private static bool ChestShown(InventoryGui gui)
        {
            RectTransform panel = gui.m_container;
            return Instance.On && gui.m_currentContainer != null && panel != null && panel.gameObject.activeSelf;
        }

        // ---- The actions ---------------------------------------------------------------------

        /// <summary>The panel, player and chest a click may act on, or null: same gate as the game's own buttons.</summary>
        private static InventoryGui Ready(out Player player, out Container chest)
        {
            InventoryGui gui = InventoryGui.instance;
            player = Player.m_localPlayer;
            chest = gui != null ? gui.m_currentContainer : null;
            if (gui == null || player == null || player.IsTeleporting() || chest == null || !chest.IsOwner())
            {
                return null;
            }
            return gui;
        }

        private static void TakeAll()
        {
            InventoryGui gui = Ready(out _, out _);
            if (gui != null)
            {
                gui.OnTakeAll();
            }
        }

        /// <summary>
        /// Tops the stacks you carry up out of the chest, and no further: the game's own Stack all
        /// would spill the rest into your free slots, which turns a top-up into a second stack you
        /// never asked for. Take all is the button for that.
        /// </summary>
        private static void FillInventory()
        {
            InventoryGui gui = Ready(out Player player, out Container chest);
            if (gui == null)
            {
                return;
            }
            gui.SetupDragItem(null, null, 1);
            int moved = TopUp(player.GetInventory(), chest.GetInventory());
            Report(gui, player, moved, "$omp_took", "$omp_took_none");
        }

        /// <summary>
        /// Moves what fits into the stacks <paramref name="target"/> already holds and nothing
        /// more: no free slot is taken, so nothing the target does not already carry appears in
        /// it and no stack grows past its cap. What may merge is the sort's rule
        /// (<see cref="InventorySorter.Merges"/>). Returns how many units went.
        /// </summary>
        private static int TopUp(Inventory target, Inventory source)
        {
            int moved = 0;
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(source.GetAllItems()))
            {
                if (item.m_shared.m_maxStackSize <= 1)
                {
                    continue;
                }
                int before = item.m_stack;
                foreach (ItemDrop.ItemData stack in target.GetAllItems())
                {
                    if (item.m_stack <= 0)
                    {
                        break;
                    }
                    if (!InventorySorter.Merges(stack, item))
                    {
                        continue;
                    }
                    int fits = Mathf.Min(stack.m_shared.m_maxStackSize - stack.m_stack, item.m_stack);
                    stack.m_stack += fits;
                    item.m_stack -= fits;
                }
                if (item.m_stack == before)
                {
                    continue;
                }
                moved += before - item.m_stack;
                if (item.m_stack <= 0)
                {
                    source.RemoveItem(item);
                }
            }
            if (moved > 0)
            {
                target.Changed();
                source.Changed();
            }
            return moved;
        }

        private static void FillChest()
        {
            InventoryGui gui = Ready(out Player player, out Container chest);
            if (gui == null)
            {
                return;
            }
            gui.SetupDragItem(null, null, 1);
            int moved = MoveToChest(player, chest, onlyExisting: true);
            Report(gui, player, moved, "$omp_stacked_chest", "$omp_stacked_chest_none");
        }

        private static void PlaceAll()
        {
            InventoryGui gui = Ready(out Player player, out Container chest);
            if (gui == null)
            {
                return;
            }
            gui.SetupDragItem(null, null, 1);
            int moved = MoveToChest(player, chest, onlyExisting: false);
            Report(gui, player, moved, "$omp_placed", "$omp_placed_none");
        }

        private static void SortChest()
        {
            InventoryGui gui = Ready(out _, out Container chest);
            if (gui == null)
            {
                return;
            }
            gui.SetupDragItem(null, null, 1);
            InventorySorter.Sort(chest.GetInventory(), 0, null);
        }

        /// <summary>
        /// The count goes into <paramref name="done"/> here, since the message hud localizes a
        /// token but cannot fill in a $1; <paramref name="nothing"/> it translates by itself.
        /// </summary>
        private static void Report(InventoryGui gui, Player player, int moved, string done, string nothing)
        {
            if (moved > 0)
            {
                gui.m_moveItemEffects.Create(gui.transform.position, Quaternion.identity);
            }
            player.Message(MessageHud.MessageType.Center, moved > 0
                ? Localization.instance.Localize(done, moved.ToString())
                : nothing);
        }

        /// <summary>
        /// Moves what the backpack may part with (<see cref="Stash.MayLeave"/>) into the chest,
        /// stacks first and free slots after, and returns how many units went. With
        /// <paramref name="onlyExisting"/> only items the chest takes go
        /// (<see cref="Stash.Takes"/>): the game's own Stack all rule plus the chest's favourites.
        /// </summary>
        private static int MoveToChest(Player player, Container container, bool onlyExisting)
        {
            Inventory backpack = player.GetInventory();
            Inventory chest = container.GetInventory();
            string marks = ChestFavorites.Marks(container);
            int moved = 0;
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(backpack.GetAllItems()))
            {
                if (!Stash.MayLeave(player, item))
                {
                    continue;
                }
                if (onlyExisting && !Stash.Takes(chest, marks, item.m_shared.m_name))
                {
                    continue;
                }
                moved += Stash.Move(item, backpack, chest);
            }
            return moved;
        }
    }
}
