using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OdinsMissingPatch
{
    /// <summary>
    /// One key stacks your inventory away into the chests around you: every stack you carry goes
    /// to the nearest chest that already holds that item, topping up its stacks before taking a
    /// free slot. Each chest that took something pulses and shows how many it took; a message
    /// sums it up. Equipped items and the hotbar stay (the hotbar is the shared KeepHotbar
    /// switch), and so does
    /// anything marked as a favourite: a modifier-click on an item in the inventory draws a golden
    /// border around it, and quick stacking leaves it alone.
    ///
    /// A favourite is a flag on the stack itself and only means anything in your own backpack:
    /// it can only be set there, and a stack that leaves it — into a chest, into the grave your
    /// death fills, onto the ground — loses the mark. Splitting a favourite makes two.
    ///
    /// The same modifier-click in an open chest's grid marks the chest for that kind of item
    /// instead (<see cref="ChestFavorites"/>): a marked chest is stacked into even when it holds
    /// none of it, and is filled before the chests that merely happen to hold one.
    /// </summary>
    internal sealed class QuickStack : Tweak
    {
        internal static readonly QuickStack Instance = new QuickStack();

        private QuickStack() { }

        internal const string FavoriteKey = "OMP_Favorite";

        private ConfigEntry<KeyboardShortcut> hotkey;
        private ConfigEntry<KeyboardShortcut> favoriteModifier;

        internal override string Section => "Quick Stack";

        protected override string Summary =>
            "A hotkey stacks your inventory away into the chests around you that already hold " +
            "each item. Equipped items, favourites and (unless General.KeepHotbar is off) the hotbar stay.";

        protected override void Bind(ConfigFile config)
        {
            hotkey = config.Bind(Section, "Hotkey", new KeyboardShortcut(KeyCode.Period),
                "The key that stacks your inventory into the chests around you. Works in the " +
                "world and with the inventory open.");
            favoriteModifier = config.Bind(Section, "FavoriteModifier", new KeyboardShortcut(KeyCode.LeftAlt),
                "Held while clicking an item in the inventory to mark it as a favourite, or to " +
                "clear the mark. Favourites are never quick stacked.");
        }

        // ---- The hotkey ----------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Hotkey
        {
            private static void Postfix(Player __instance)
            {
                if (!Instance.On || __instance != Player.m_localPlayer || !CanTakeHotkey(__instance))
                {
                    return;
                }
                if (Hotkeys.Pressed(Instance.hotkey.Value))
                {
                    Instance.Stack(__instance);
                }
            }

            /// <summary>
            /// The player's own input gate, plus the one screen it refuses that the key should
            /// still work through: the inventory. Anything that takes typed text still blocks.
            /// </summary>
            private static bool CanTakeHotkey(Player player)
            {
                if (player.TakeInput())
                {
                    return true;
                }
                return InventoryGui.IsVisible()
                    && !(Chat.instance != null && Chat.instance.HasFocus())
                    && !Console.IsVisible() && !TextInput.IsVisible() && !Menu.IsVisible()
                    && !UnifiedPopup.IsVisible()
                    && !player.IsDead() && !player.IsTeleporting();
            }
        }

        /// <summary>The whole action, for the hotkey and for the inventory panel's Stack nearby button.</summary>
        internal void Stack(Player player)
        {
            Inventory backpack = player.GetInventory();
            List<Container> chests = new List<Container>(NearbyChests.Find(player.transform.position));
            if (chests.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "$omp_no_chest");
                return;
            }
            Dictionary<Container, int> stashed = new Dictionary<Container, int>();
            List<Container> ordered = new List<Container>();
            int moved = 0;
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(backpack.GetAllItems()))
            {
                if (!Stash.MayLeave(player, item))
                {
                    continue;
                }
                string name = item.m_shared.m_name;
                Order(chests, name, ordered);
                foreach (Container chest in ordered)
                {
                    if (item.m_stack <= 0)
                    {
                        break;
                    }
                    Inventory inventory = chest.GetInventory();
                    // Claim reloads the chest from its ZDO, so what it holds is asked again after it.
                    if (!Takes(chest, inventory, name) || !NearbyChests.Claim(chest)
                        || !Takes(chest, inventory, name))
                    {
                        continue;
                    }
                    int before = item.m_stack;
                    int part = Stash.Move(item, backpack, inventory);
                    if (part > 0)
                    {
                        moved += part;
                        Add(stashed, chest, part);
                    }
                    if (part == before)
                    {
                        // Everything went, and the item is no longer the backpack's.
                        break;
                    }
                }
            }
            foreach (KeyValuePair<Container, int> pair in stashed)
            {
                ChestGlow.Flash(pair.Key, "+" + pair.Value);
                InventoryGui gui = InventoryGui.instance;
                if (gui != null)
                {
                    gui.m_moveItemEffects.Create(pair.Key.transform.position, Quaternion.identity);
                }
            }
            player.Message(MessageHud.MessageType.Center, moved > 0
                ? Localization.instance.Localize(
                    stashed.Count == 1 ? "$omp_stacked_one_chest" : "$omp_stacked_chests",
                    moved.ToString(), stashed.Count.ToString())
                : "$omp_stacked_none");
        }

        private static bool Takes(Container chest, Inventory inventory, string name)
        {
            return Stash.Takes(inventory, ChestFavorites.Marks(chest), name);
        }

        /// <summary>
        /// The chests to try for one kind of item: those marked for it first, each group still
        /// nearest first, since a mark says where the item belongs while holding one is only a
        /// hint. Refills the list it is given rather than making one per item.
        /// </summary>
        private static void Order(List<Container> chests, string name, List<Container> ordered)
        {
            ordered.Clear();
            foreach (Container chest in chests)
            {
                if (ChestFavorites.Accepts(chest, name))
                {
                    ordered.Add(chest);
                }
            }
            foreach (Container chest in chests)
            {
                if (!ChestFavorites.Accepts(chest, name))
                {
                    ordered.Add(chest);
                }
            }
        }

        private static void Add(Dictionary<Container, int> stashed, Container chest, int count)
        {
            stashed.TryGetValue(chest, out int total);
            stashed[chest] = total + count;
        }

        // ---- Favourites ----------------------------------------------------------------------

        /// <summary>A favourite, while quick stacking is on: switched off, the marks mean nothing anywhere.</summary>
        internal static bool IsFavorite(ItemDrop.ItemData item)
        {
            return Instance.On && item.m_customData.TryGetValue(FavoriteKey, out string value) && value == "1";
        }

        private static void SetFavorite(ItemDrop.ItemData item, bool favorite)
        {
            if (favorite)
            {
                item.m_customData[FavoriteKey] = "1";
            }
            else
            {
                item.m_customData.Remove(FavoriteKey);
            }
        }

        /// <summary>
        /// A click with the modifier held toggles a favourite instead of picking the item up:
        /// the stack itself in the backpack, the chest's mark for that kind of item in an open
        /// chest. The grid raises its select callback from here, so returning false is the whole
        /// veto. Each half answers to the tweaks that read it, so the chest's marks can still be
        /// set with quick stacking switched off and Chest buttons on.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftDown))]
        private static class ToggleFavorite
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler clickHandler)
            {
                Player player = Player.m_localPlayer;
                if (player == null || __instance.m_inventory == null
                    || !Hotkeys.Held(Instance.favoriteModifier.Value))
                {
                    return true;
                }
                bool backpack = __instance.m_inventory == player.GetInventory();
                if (backpack ? !Instance.On : !ChestFavorites.Used)
                {
                    return true;
                }
                Vector2i pos = __instance.GetButtonPos(clickHandler.gameObject);
                ItemDrop.ItemData item = __instance.m_inventory.GetItemAt(pos.x, pos.y);
                if (item == null)
                {
                    return true;
                }
                if (!backpack)
                {
                    return MarkChest(player, __instance.m_inventory, item);
                }
                SetFavorite(item, !IsFavorite(item));
                // Re-weighs the backpack; harmless.
                __instance.m_inventory.Changed();
                return false;
            }

            /// <summary>
            /// Marks the open chest for the kind of item clicked, or unmarks it. Any other
            /// inventory has no mark to set: a flag on the stack would be stripped the moment
            /// that inventory changed, so the click is refused with a word on why.
            /// </summary>
            private static bool MarkChest(Player player, Inventory inventory, ItemDrop.ItemData item)
            {
                Container chest = ChestFavorites.OpenChest(inventory);
                if (chest == null)
                {
                    player.Message(MessageHud.MessageType.Center, "$omp_favourites_inventory");
                    return false;
                }
                string name = item.m_shared.m_name;
                bool marked = ChestFavorites.Toggle(chest, name);
                player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(
                    marked ? "$omp_chest_takes" : "$omp_chest_takes_not", ChestFavorites.Localize(name)));
                return false;
            }
        }

        /// <summary>
        /// Every way out of the backpack but one ends in another inventory, and every inventory
        /// raises Changed once it has taken an item — a drag, Place all, a quick stack, the grave
        /// a death fills all pass through here, before a container saves itself — so that is the
        /// one place the mark has to come off. While there is no local player their own inventory
        /// is the one being loaded, so nothing is touched then.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class ClearOutsideBackpack
        {
            private static void Prefix(Inventory __instance)
            {
                Player player = Player.m_localPlayer;
                if (!Instance.On || player == null || __instance == player.GetInventory())
                {
                    return;
                }
                foreach (ItemDrop.ItemData item in __instance.GetAllItems())
                {
                    if (IsFavorite(item))
                    {
                        SetFavorite(item, false);
                    }
                }
            }
        }

        /// <summary>
        /// The one way out no inventory sees: a dropped stack is a clone of its own, living in
        /// its own ZDO. The drop has already saved it by the time this runs, so clearing the
        /// mark needs a second save.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        private static class ClearOnDrop
        {
            private static void Postfix(ItemDrop __result)
            {
                if (!Instance.On || __result == null || __result.m_itemData == null
                    || !IsFavorite(__result.m_itemData))
                {
                    return;
                }
                SetFavorite(__result.m_itemData, false);
                __result.Save();
            }
        }

        /// <summary>
        /// The golden border: four thin gold bars along the edges of the slot's own "equipped"
        /// frame, switched on for favourites after every grid refresh. A border rather than a
        /// filled frame leaves the game's own equipped highlight visible underneath, so an
        /// equipped favourite still reads as equipped. Slots are rebuilt when an inventory
        /// changes size, so a border whose slot is gone is simply made again.
        ///
        /// Only the backpack's own flag gets the border. A chest's marks show as a yellow amount
        /// instead (ChestFavorites.ShowMarks), so the two favourites never look alike.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        private static class ShowFavorites
        {
            /// <summary>How thick each bar is, in the canvas' units — a slot is about seventy.</summary>
            private const float Thickness = 3f;

            private static readonly Dictionary<InventoryElement, GameObject> borders =
                new Dictionary<InventoryElement, GameObject>();

            private static void Postfix(InventoryGrid __instance)
            {
                Inventory inventory = __instance.m_inventory;
                if (inventory == null)
                {
                    return;
                }
                foreach (InventoryElement element in __instance.m_elements)
                {
                    if (borders.TryGetValue(element, out GameObject border) && border != null)
                    {
                        border.SetActive(false);
                    }
                }
                if (!Instance.On)
                {
                    return;
                }
                int width = inventory.GetWidth();
                foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                {
                    if (!IsFavorite(item))
                    {
                        continue;
                    }
                    InventoryElement element = __instance.GetElement(item.m_gridPos.x, item.m_gridPos.y, width);
                    GameObject border = element != null ? BorderFor(element) : null;
                    if (border != null)
                    {
                        border.SetActive(true);
                    }
                }
            }

            private static GameObject BorderFor(InventoryElement element)
            {
                if (borders.TryGetValue(element, out GameObject border) && border != null)
                {
                    return border;
                }
                Image template = element.m_equiped;
                if (template == null)
                {
                    return null;
                }
                RectTransform source = template.rectTransform;
                border = new GameObject("OMP_Favorite", typeof(RectTransform));
                border.layer = source.gameObject.layer;
                RectTransform rect = (RectTransform)border.transform;
                rect.SetParent(source.parent, false);
                rect.anchorMin = source.anchorMin;
                rect.anchorMax = source.anchorMax;
                rect.pivot = source.pivot;
                rect.anchoredPosition = source.anchoredPosition;
                rect.sizeDelta = source.sizeDelta;
                rect.localScale = source.localScale;
                rect.localRotation = source.localRotation;
                rect.SetSiblingIndex(source.GetSiblingIndex() + 1);
                Bar(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, Thickness));
                Bar(rect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, Thickness));
                Bar(rect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(Thickness, 0f));
                Bar(rect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(Thickness, 0f));
                borders[element] = border;
                return border;
            }

            /// <summary>
            /// One edge: an image stretched along the side its two anchors share and sized across
            /// it, pivoted onto that side so it sits inside the frame.
            /// </summary>
            private static void Bar(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
            {
                GameObject bar = new GameObject("Bar", typeof(RectTransform));
                bar.layer = parent.gameObject.layer;
                RectTransform rect = (RectTransform)bar.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = (anchorMin + anchorMax) * 0.5f;
                rect.sizeDelta = size;
                rect.anchoredPosition = Vector2.zero;
                Image image = bar.AddComponent<Image>();
                image.color = Palette.Gold;
                image.raycastTarget = false;
            }
        }
    }
}
