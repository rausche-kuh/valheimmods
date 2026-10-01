using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The kinds of item a chest is marked to take, whether or not it holds any right now. An
    /// Alt-click on an item in the open chest's grid marks its kind — the same modifier that
    /// marks a stack in the backpack, one gesture for both favourites — and quick stacking and
    /// Fill the chest then treat a marked chest as though it already held that item, so a chest
    /// emptied of its wood still draws wood back instead of losing the habit.
    ///
    /// A slot holding a marked kind shows its amount in yellow (<see cref="ShowMarks"/>), but a
    /// marked kind the chest holds none of has no slot, so the list is shown whole only in the
    /// chest panel's Clear favourites button (<see cref="ClearButton"/>) and its tooltip.
    ///
    /// The list is a string on the chest's ZDO, so it persists, survives the chest being emptied
    /// and is the same for every client. Item names are the shared name (a localization token,
    /// "$item_wood"), joined by newlines with one at each end, so a name is matched between two
    /// separators and no name can be a prefix of another.
    /// </summary>
    internal static class ChestFavorites
    {
        private static readonly int FavoritesHash = "OMP_ChestFavorites".GetStableHashCode();

        private const char Separator = '\n';

        /// <summary>Whether a chest's favourites mean anything right now: a tweak that reads them is on.</summary>
        internal static bool Used => Patcher.AnyServedOn(typeof(ShowMarks));

        /// <summary>
        /// The chest's marks as one string, for a caller that asks about many items at once (a
        /// grid refresh, a stack away); pass it to <see cref="Marked"/>. Empty when the chest is
        /// gone, unloaded or unmarked.
        /// </summary>
        internal static string Marks(Container chest)
        {
            ZNetView nview = chest != null ? chest.m_nview : null;
            return nview != null && nview.IsValid() ? nview.GetZDO().GetString(FavoritesHash, "") : "";
        }

        /// <summary>
        /// Whether <paramref name="marks"/> holds <paramref name="itemName"/>. A hit has to have
        /// a separator on either side, which every name in the string does, so Wood never matches
        /// inside WoodArrow.
        /// </summary>
        internal static bool Marked(string marks, string itemName)
        {
            if (string.IsNullOrEmpty(marks) || string.IsNullOrEmpty(itemName))
            {
                return false;
            }
            for (int at = marks.IndexOf(itemName, StringComparison.Ordinal); at >= 0;
                at = marks.IndexOf(itemName, at + 1, StringComparison.Ordinal))
            {
                // Every string this writes has a separator at each end, so a name always has
                // both neighbours; the bounds are asked anyway rather than trusting the ZDO.
                int after = at + itemName.Length;
                if (at > 0 && after < marks.Length
                    && marks[at - 1] == Separator && marks[after] == Separator)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool Accepts(Container chest, string itemName)
        {
            return Marked(Marks(chest), itemName);
        }

        internal static List<string> Names(Container chest)
        {
            List<string> names = new List<string>();
            foreach (string name in Marks(chest).Split(Separator))
            {
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// Marks or unmarks a kind of item, and says which it did. Only for the chest open in the
        /// panel, which is the one the local client owns.
        /// </summary>
        internal static bool Toggle(Container chest, string itemName)
        {
            List<string> names = Names(chest);
            bool marked = !names.Remove(itemName);
            if (marked)
            {
                names.Add(itemName);
            }
            Write(chest, names);
            return marked;
        }

        internal static void Clear(Container chest)
        {
            Write(chest, new List<string>());
        }

        /// <summary>
        /// The marked kinds as a readable list, one per line, for the button's tooltip - the only
        /// place they are all shown, since a kind the chest holds none of has no slot.
        /// </summary>
        internal static string Describe(Container chest)
        {
            StringBuilder text = new StringBuilder();
            foreach (string name in Names(chest))
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }
                text.Append("- ").Append(Localize(name));
            }
            return text.ToString();
        }

        internal static string Localize(string itemName)
        {
            return Localization.instance != null ? Localization.instance.Localize(itemName) : itemName;
        }

        /// <summary>
        /// The chest whose inventory this is, when it is the one open in the panel; null for the
        /// backpack and for any other inventory.
        /// </summary>
        internal static Container OpenChest(Inventory inventory)
        {
            InventoryGui gui = InventoryGui.instance;
            Container chest = gui != null ? gui.m_currentContainer : null;
            return chest != null && inventory != null && chest.m_inventory == inventory ? chest : null;
        }

        private static void Write(Container chest, List<string> names)
        {
            ZNetView nview = chest != null ? chest.m_nview : null;
            if (nview == null || !nview.IsValid())
            {
                return;
            }
            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
            }
            if (!nview.IsOwner())
            {
                return;
            }
            StringBuilder marks = new StringBuilder();
            foreach (string name in names)
            {
                marks.Append(Separator).Append(name);
            }
            if (marks.Length > 0)
            {
                marks.Append(Separator);
            }
            nview.GetZDO().Set(FavoritesHash, marks.ToString());
        }

        /// <summary>
        /// Turns the amount of every slot in the open chest's grid yellow when its item is of a
        /// marked kind: the game's own stack count text, recoloured, rather than a border, since
        /// the golden border is the backpack's stack favourite and the two should never look
        /// alike. Items without a stack size show no amount and so no mark.
        ///
        /// UpdateContainer runs every frame the screen is up, so the slots are only recoloured
        /// when something that decides the colour changed: another chest, a new ZDO revision
        /// (the chest saves its items and its marks to the ZDO, so a move, a sort or a toggled
        /// mark all bump it), or the grid having rebuilt its elements for a new size. Every
        /// recolour starts by putting the game's colour back on each slot, since the grid reuses
        /// its elements for whatever item lands in them.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer))]
        [Serves(typeof(QuickStack), typeof(ChestButtons), Optional = true)]
        private static class ShowMarks
        {
            /// <summary>
            /// Harmony runs this as it patches the class, which is when a tweak that reads the
            /// marks goes on: the moment the Clear favourites button is needed, handed over here.
            /// </summary>
            private static void Prepare()
            {
                PanelButtons.Add(ClearButton.Button, PanelButtons.Spot.StackAll, ClearButton.Shows, ClearButton.Refresh);
            }

            /// <summary>
            /// <c>InventoryElement.m_amount</c>, a <c>TMP_Text</c>, read as the <c>Graphic</c> it
            /// derives from: all a colour needs, and it keeps TextMeshPro out of the references.
            /// </summary>
            private static readonly AccessTools.FieldRef<InventoryElement, Graphic> Amount =
                AccessTools.FieldRefAccess<InventoryElement, Graphic>("m_amount");

            private static Container shownChest;
            private static uint shownRevision;
            private static InventoryElement shownFirst;
            private static bool tinted;

            private static Color plain;
            private static bool plainKnown;

            private static void Postfix(InventoryGui __instance)
            {
                // The chest's grid stays up through the fade; leave it as it is, as the buttons do.
                if (PanelButtons.Closing(__instance))
                {
                    return;
                }
                InventoryGrid grid = __instance.m_containerGrid;
                if (grid == null || !KnowPlain(grid))
                {
                    return;
                }
                List<InventoryElement> elements = grid.m_elements;
                Container chest = __instance.m_currentContainer;
                ZNetView nview = chest != null ? chest.m_nview : null;
                if (!Used || nview == null || !nview.IsValid() || grid.m_inventory != chest.GetInventory())
                {
                    if (tinted)
                    {
                        Reset(elements);
                    }
                    shownChest = null;
                    return;
                }
                uint revision = nview.GetZDO().DataRevision;
                InventoryElement first = elements.Count > 0 ? elements[0] : null;
                if (chest == shownChest && revision == shownRevision && first == shownFirst)
                {
                    return;
                }
                shownChest = chest;
                shownRevision = revision;
                shownFirst = first;
                Reset(elements);
                string marks = Marks(chest);
                if (marks.Length == 0)
                {
                    return;
                }
                Inventory inventory = grid.m_inventory;
                int width = inventory.GetWidth();
                foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                {
                    if (!Marked(marks, item.m_shared.m_name))
                    {
                        continue;
                    }
                    InventoryElement element = grid.GetElement(item.m_gridPos.x, item.m_gridPos.y, width);
                    Graphic amount = element != null ? Amount(element) : null;
                    if (amount != null)
                    {
                        amount.color = Palette.ChestYellow;
                        tinted = true;
                    }
                }
            }

            /// <summary>The amount's colour as the slot prefab has it, read once: every slot starts as a copy.</summary>
            private static bool KnowPlain(InventoryGrid grid)
            {
                if (plainKnown)
                {
                    return true;
                }
                InventoryElement prefab = grid.m_elementPrefab != null
                    ? grid.m_elementPrefab.GetComponent<InventoryElement>()
                    : null;
                Graphic amount = prefab != null ? Amount(prefab) : null;
                if (amount == null)
                {
                    return false;
                }
                plain = amount.color;
                plainKnown = true;
                return true;
            }

            private static void Reset(List<InventoryElement> elements)
            {
                foreach (InventoryElement element in elements)
                {
                    Graphic amount = element != null ? Amount(element) : null;
                    if (amount != null)
                    {
                        amount.color = plain;
                    }
                }
                tinted = false;
            }
        }

        /// <summary>
        /// The chest panel's Clear favourites: a text button (see
        /// <see cref="PanelButtons.TextButton"/>), in the spot of the game's Stack all while that
        /// is hidden, shown while the chest's favourites mean anything and it carries some. Its
        /// label counts the marks and its tooltip names them.
        /// </summary>
        private static class ClearButton
        {
            internal static readonly PanelButtons.TextButton Button =
                new PanelButtons.TextButton("OMP_ClearFavorites", ClearOpen);

            /// <summary>The marks, and the language, the count and the tooltip below were written for.</summary>
            private static string favorites = "";
            private static int revision = -1;
            private static int count;
            private static string tooltip = "";

            internal static bool Shows(InventoryGui gui)
            {
                Container chest = gui.m_currentContainer;
                return chest != null && gui.m_container != null && gui.m_container.gameObject.activeSelf
                    && Used && Marks(chest).Length > 0;
            }

            internal static void Refresh(InventoryGui gui)
            {
                Describe(gui.m_currentContainer, Marks(gui.m_currentContainer));
                Button.SetLabel(count == 1 ? "$omp_clear_favourite" : "$omp_clear_favourites", count.ToString());
                Button.SetTooltip("$omp_favourites_topic", tooltip);
            }

            /// <summary>
            /// Counts a chest's marks and writes the tooltip for them, and only when they have
            /// changed: this runs on every frame the panel is up, and naming the items means
            /// splitting the list and localizing each one. A language change counts as a change,
            /// since the names in the tooltip were translated when it was written.
            /// </summary>
            private static void Describe(Container chest, string marks)
            {
                if (marks == favorites && revision == Translations.Revision)
                {
                    return;
                }
                favorites = marks;
                revision = Translations.Revision;
                count = Names(chest).Count;
                tooltip = "$omp_favourites_tip_head\n"
                    + ChestFavorites.Describe(chest)
                    + "\n\n$omp_favourites_tip_foot";
            }

            private static void ClearOpen()
            {
                InventoryGui gui = InventoryGui.instance;
                Container chest = gui != null ? gui.m_currentContainer : null;
                if (chest == null)
                {
                    return;
                }
                Clear(chest);
                Player player = Player.m_localPlayer;
                if (player != null)
                {
                    player.Message(MessageHud.MessageType.Center, "$omp_favourites_cleared");
                }
            }
        }
    }
}
