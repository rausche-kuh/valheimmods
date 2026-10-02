namespace OdinsMissingPatch
{
    /// <summary>
    /// Two icon buttons in a column beside the inventory panel, between the armour box and
    /// the weight box: stack nearby, which is quick stacking by click (shown while Quick Stack is on, since
    /// it is that tweak's range and rules, and only while no chest is open, when ChestButtons'
    /// fill your stacks takes its place), and sort, which merges and sorts the backpack below
    /// the hotbar while the hotbar is kept. Equipped items and favourites keep their slot;
    /// everything else flows around them. <see cref="PanelButtons"/> builds and places them.
    /// </summary>
    internal sealed class InventoryButtons : Tweak
    {
        internal static readonly InventoryButtons Instance = new InventoryButtons();

        /// <summary>Hands the buttons to PanelButtons, below ChestButtons' fill your stacks.</summary>
        private InventoryButtons()
        {
            PanelButtons.Add(PanelButtons.Column.Inventory, 10, "StackNearby", "stack_nearby",
                "$omp_stack_nearby", () => QuickStack.Instance.StackNearbyTip(), StackNearby,
                gui => Instance.On && QuickStack.Instance.On && gui.m_currentContainer == null);
            PanelButtons.Add(PanelButtons.Column.Inventory, 11, "SortInventory", "sort",
                "$omp_sort", "$omp_sort_tip", SortInventory, gui => Instance.On);
        }

        internal override string Section => "Inventory Buttons";

        protected override string Summary =>
            "Two icon buttons beside the inventory panel: stack nearby (quick stacking by click, " +
            "while Quick Stack is on and no chest is open) and sort.";

        // ---- The actions ---------------------------------------------------------------------

        private static void StackNearby()
        {
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;
            if (gui == null || player == null || player.IsTeleporting() || !QuickStack.Instance.On)
            {
                return;
            }
            gui.SetupDragItem(null, null, 1);
            QuickStack.Instance.Stack(player);
        }

        private static void SortInventory()
        {
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;
            if (gui == null || player == null || player.IsTeleporting())
            {
                return;
            }
            gui.SetupDragItem(null, null, 1);
            InventorySorter.Sort(player.GetInventory(), SharedSettings.KeepHotbar.Value ? 1 : 0,
                item => Stash.StaysPut(player, item));
        }
    }
}
