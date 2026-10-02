using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The buttons the mod adds to the inventory screen, and the one place that lays them out.
    /// An icon button is a copy of the chest panel's own Take all button, so it keeps the game's
    /// button skin, hover tint and click sound, with the label blanked and an icon from
    /// assets/icons/ drawn in its place in the colour the label had; a tooltip names it, since
    /// the icon is all that shows. Icon buttons stand beside the panels, clear of their
    /// background, in a column each at the same distance from the panel: beside the inventory
    /// between the armour and weight boxes, beside the chest from its top edge down. A
    /// <see cref="TextButton"/> keeps a label instead and takes the spot of the game's Take all
    /// or Stack all while those are hidden.
    ///
    /// Owners hand their buttons over once (<see cref="Add(Column, int, string, string, string, string, UnityAction, Func{InventoryGui, bool})"/>,
    /// <see cref="Add(TextButton, Spot, Func{InventoryGui, bool}, Action{InventoryGui})"/>)
    /// with a check of when each shows; every frame the screen is up, <see cref="Arrange"/>
    /// builds what is missing, shows what applies, decides the game's two chest buttons and
    /// places everything, so no owner depends on another's patch running first.
    /// </summary>
    internal static class PanelButtons
    {
        /// <summary>The space between two buttons, in UI pixels.</summary>
        private const float Gap = 6f;

        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        /// <summary>Which panel an icon button stands beside.</summary>
        internal enum Column
        {
            Inventory,
            Chest,
        }

        /// <summary>Which of the game's chest buttons a text button stands in for while it is hidden.</summary>
        internal enum Spot
        {
            TakeAll,
            StackAll,
        }

        private sealed class IconEntry
        {
            internal Column Column;
            internal int Order;
            internal string Name;
            internal string Icon;
            internal string Title;
            internal Func<string> Tooltip;
            internal UnityAction OnClick;
            internal Func<InventoryGui, bool> Visible;
            internal Button Button;
            internal UITooltip Tip;
        }

        private sealed class TextEntry
        {
            internal TextButton Button;
            internal Spot Spot;
            internal Func<InventoryGui, bool> Visible;
            internal Action<InventoryGui> Refresh;
        }

        /// <summary>Every icon button, by column and then by its place in the column.</summary>
        private static readonly List<IconEntry> iconButtons = new List<IconEntry>();

        /// <summary>Every text button, Take all's spot first.</summary>
        private static readonly List<TextEntry> textButtons = new List<TextEntry>();

        /// <summary>
        /// Hands an icon button over, to be built the first time <paramref name="visible"/> says
        /// it shows and laid out every frame from then on. <paramref name="order"/> is its place
        /// in its column from the top; only buttons that show take a place, so a column closes
        /// up around whatever is hidden. <paramref name="title"/> and <paramref name="tooltip"/>
        /// are $omp_ tokens from <see cref="Translations"/>: UITooltip localizes both when it
        /// shows the tooltip, so they follow a language change without the button being rebuilt.
        /// </summary>
        internal static void Add(Column column, int order, string name, string icon, string title,
            string tooltip, UnityAction onClick, Func<InventoryGui, bool> visible)
        {
            Add(column, order, name, icon, title, () => tooltip, onClick, visible);
        }

        /// <summary>
        /// The same, with a tooltip asked for on every frame the button shows, for one that
        /// names a configurable key. It is written to the button only when the string handed
        /// back is a different object, so the source should cache it.
        /// </summary>
        internal static void Add(Column column, int order, string name, string icon, string title,
            Func<string> tooltip, UnityAction onClick, Func<InventoryGui, bool> visible)
        {
            iconButtons.Add(new IconEntry
            {
                Column = column,
                Order = order,
                Name = name,
                Icon = icon,
                Title = title,
                Tooltip = tooltip,
                OnClick = onClick,
                Visible = visible,
            });
            iconButtons.Sort((a, b) => a.Column != b.Column ? a.Column.CompareTo(b.Column) : a.Order.CompareTo(b.Order));
        }

        /// <summary>
        /// Hands a text button over; a second call with the same button is ignored.
        /// <paramref name="refresh"/> runs on every frame it shows, before it is placed, and is
        /// where its owner sets the label and the tooltip.
        /// </summary>
        internal static void Add(TextButton button, Spot spot, Func<InventoryGui, bool> visible,
            Action<InventoryGui> refresh)
        {
            if (textButtons.Exists(e => e.Button == button))
            {
                return;
            }
            textButtons.Add(new TextEntry { Button = button, Spot = spot, Visible = visible, Refresh = refresh });
            textButtons.Sort((a, b) => a.Spot.CompareTo(b.Spot));
        }

        /// <summary>The side of a button: the Take all button's height, so it matches the panel.</summary>
        private static float Size(InventoryGui gui)
        {
            Button template = gui != null ? gui.m_takeAllButton : null;
            return template != null ? ((RectTransform)template.transform).rect.height : 32f;
        }

        /// <summary>
        /// A new icon button under <paramref name="parent"/>, inactive until it is placed. Null
        /// when the panel has no Take all button to copy, in which case there is nothing to build
        /// a column from and the panel stays as it is.
        /// </summary>
        private static Button Create(InventoryGui gui, Transform parent, string name, string icon,
            string title, string tooltip, UnityAction onClick)
        {
            Button template = gui != null ? gui.m_takeAllButton : null;
            if (template == null)
            {
                return null;
            }
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = "OMP_" + name;
            Button button = go.GetComponent<Button>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            button.interactable = true;

            float size = Size(gui);
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);

            AddIcon(go, icon, BlankLabel(go), size);

            UITooltip tip = Tooltip(gui, go);
            if (tip != null)
            {
                tip.m_topic = title;
                tip.m_text = tooltip;
            }
            go.SetActive(false);
            return button;
        }

        /// <summary>
        /// Whether the screen is on its way out, in which case every button stays exactly as it
        /// is. Closing the screen clears the animator's "visible" flag and drops
        /// <c>m_currentContainer</c> in the same frame, while the chest panel stays up for the
        /// fade - and one more UpdateContainer still runs on that frame, with the flag already
        /// read. Reading it as "no chest open" would put the game's Take all and Stack all back
        /// over the panel, and take our own buttons off it, for the whole fade.
        /// </summary>
        internal static bool Closing(InventoryGui gui)
        {
            Animator animator = gui != null ? gui.m_animator : null;
            return animator != null && !animator.GetBool("visible");
        }

        // ---- The layout ------------------------------------------------------------------------

        /// <summary>Whether the game's Take all and Stack all are hidden behind the chest's column right now.</summary>
        private static bool vanillaHidden;

        private static int arrangedFrame = -1;

        /// <summary>
        /// Lays the screen out once a frame. UpdateContainer runs on every frame the screen is
        /// up, after UpdateInventory and whether or not a chest is open, so both columns are
        /// settled there. The chest's column stands in for the game's Take all and Stack all:
        /// while any icon button shows in it those two are hidden and the text buttons take
        /// their spots, the only free width the chest panel's top band has; otherwise the band
        /// is full and the text buttons go into the column beside the chest.
        /// </summary>
        private static void Arrange(InventoryGui gui)
        {
            if (Closing(gui) || arrangedFrame == Time.frameCount)
            {
                return;
            }
            arrangedFrame = Time.frameCount;
            bool chestColumn = false;
            foreach (IconEntry entry in iconButtons)
            {
                bool show = entry.Visible(gui) && (entry.Button != null || Build(gui, entry));
                if (entry.Button != null)
                {
                    entry.Button.gameObject.SetActive(show);
                }
                if (show && entry.Tip != null)
                {
                    string tooltip = entry.Tooltip();
                    if (!ReferenceEquals(entry.Tip.m_text, tooltip))
                    {
                        entry.Tip.m_text = tooltip;
                    }
                }
                chestColumn |= show && entry.Column == Column.Chest;
            }
            SetVanilla(gui, !chestColumn);
            int slot = LayoutChestColumn(gui);
            LayoutInventoryColumn(gui);
            foreach (TextEntry entry in textButtons)
            {
                if (!entry.Visible(gui) || !entry.Button.Show(gui))
                {
                    entry.Button.Hide();
                    continue;
                }
                entry.Refresh(gui);
                Button at = !vanillaHidden ? null
                    : entry.Spot == Spot.TakeAll ? gui.m_takeAllButton : gui.m_stackAllButton;
                entry.Button.Place(gui, at, keepLeft: entry.Spot == Spot.TakeAll, slot: slot);
                if (at == null)
                {
                    slot++;
                }
            }
        }

        private static bool Build(InventoryGui gui, IconEntry entry)
        {
            Transform parent = entry.Column == Column.Chest ? gui.m_container : gui.m_player;
            if (parent == null)
            {
                return false;
            }
            entry.Button = Create(gui, parent, entry.Name, entry.Icon, entry.Title, entry.Tooltip(), entry.OnClick);
            if (entry.Button == null)
            {
                return false;
            }
            entry.Tip = entry.Button.GetComponent<UITooltip>();
            return true;
        }

        /// <summary>Only ever restores what it hid, so a button another mod hid stays hidden.</summary>
        private static void SetVanilla(InventoryGui gui, bool active)
        {
            if (active == !vanillaHidden)
            {
                return;
            }
            vanillaHidden = !active;
            if (gui.m_takeAllButton != null)
            {
                gui.m_takeAllButton.gameObject.SetActive(active);
            }
            if (gui.m_stackAllButton != null)
            {
                gui.m_stackAllButton.gameObject.SetActive(active);
            }
        }

        /// <summary>
        /// The layout, on the method that runs every frame the screen is up, for the tweaks whose
        /// icon buttons it places.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer))]
        [Serves(typeof(ChestButtons), typeof(InventoryButtons))]
        private static class LayOut
        {
            private static void Postfix(InventoryGui __instance)
            {
                Arrange(__instance);
            }
        }

        /// <summary>
        /// The same layout for the tweaks that only have text buttons here, for whom a failure
        /// costs just the button. With both classes in, the first to run each frame lays out.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer))]
        [Serves(typeof(QuickStack), typeof(StationRefill), typeof(NearbyCrafting), Optional = true)]
        private static class LayOutText
        {
            private static void Postfix(InventoryGui __instance)
            {
                Arrange(__instance);
            }
        }

        /// <summary>
        /// The inventory screen goes with the world on the way back to the main menu, and every
        /// button on it with it; the next world builds a new screen. This is where that is
        /// heard, once, so no button has to be tested for life every frame: every reference is
        /// let go, to be made afresh on the new screen the first time it shows, and the new
        /// screen's Take all and Stack all were never hidden. A text button left out while this
        /// is not in rebuilds anyway, since a destroyed button reads as missing.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        [Serves(typeof(ChestButtons), typeof(InventoryButtons))]
        private static class ScreenDestroyed
        {
            private static void Postfix()
            {
                foreach (IconEntry entry in iconButtons)
                {
                    entry.Button = null;
                }
                foreach (TextEntry entry in textButtons)
                {
                    entry.Button.Forget();
                }
                vanillaHidden = false;
            }
        }

        // ---- Where things are ------------------------------------------------------------------

        /// <summary>
        /// Puts a rect's centre at <paramref name="center"/>, measured from its parent's bottom
        /// left corner, whatever it was anchored to before; the size is kept.
        /// </summary>
        private static void Pin(RectTransform rect, Vector2 center)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
        }

        /// <summary>
        /// How far a panel's visible background reaches past its rect, to the right (x) and
        /// above (y): the background is a stretched child (`Bkg`) with a size delta, half of
        /// which hangs out on each side. 10px either way when the child is not as expected.
        /// </summary>
        private static Vector2 Overhang(RectTransform panel)
        {
            RectTransform bkg = panel.Find("Bkg") as RectTransform;
            if (bkg != null && bkg.anchorMin == Vector2.zero && bkg.anchorMax == Vector2.one)
            {
                return Vector2.Max(Vector2.zero, bkg.sizeDelta * 0.5f);
            }
            return new Vector2(10f, 10f);
        }

        /// <summary>
        /// Where a button's left edge goes beside a panel, measured from the panel's left: past
        /// the panel's background plus a gap. The readout boxes' own rects overlap that border,
        /// so lining up with them would put a button on the panel's edge.
        /// </summary>
        private static float ColumnLeft(RectTransform panel)
        {
            return panel.rect.width + Overhang(panel).x + Gap;
        }

        /// <summary>
        /// Where the first button's top edge goes beside a panel, measured from the panel's
        /// bottom: level with the top of the panel's background, so the buttons read as part
        /// of the panel they belong to.
        /// </summary>
        private static float ColumnTop(RectTransform panel)
        {
            return panel.rect.height + Overhang(panel).y;
        }

        /// <summary>
        /// A readout box beside a panel (the armour or weight box): the parent of the
        /// InventoryGui text that shows the number, found by field name since the text is a
        /// TextMeshPro type the build cannot reference, else the panel's child of that name.
        /// Null when neither is there.
        /// </summary>
        private static RectTransform Box(InventoryGui gui, string field, RectTransform panel, string name)
        {
            FieldInfo info = typeof(InventoryGui).GetField(field,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Component text = info != null ? info.GetValue(gui) as Component : null;
            RectTransform box = text != null ? text.transform.parent as RectTransform : null;
            if (box == null && panel != null)
            {
                box = panel.Find(name) as RectTransform;
            }
            return box;
        }

        /// <summary>
        /// A rect's edges in a panel's space, measured from the panel's bottom left corner like
        /// everything Pin places, whatever the rect is anchored to and wherever it sits in the
        /// hierarchy.
        /// </summary>
        private static Rect InPanel(RectTransform panel, RectTransform rect)
        {
            Vector3 min = panel.InverseTransformPoint(rect.TransformPoint(rect.rect.min));
            Vector3 max = panel.InverseTransformPoint(rect.TransformPoint(rect.rect.max));
            Vector2 origin = Vector2.Scale(panel.pivot, panel.rect.size);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x) + origin.x, Mathf.Min(min.y, max.y) + origin.y,
                Mathf.Max(min.x, max.x) + origin.x, Mathf.Max(min.y, max.y) + origin.y);
        }

        /// <summary>Whether an icon button is in the column and showing this frame.</summary>
        private static bool Showing(IconEntry entry, Column column)
        {
            return entry.Column == column && entry.Button != null && entry.Button.gameObject.activeSelf;
        }

        /// <summary>
        /// Stacks the inventory column's showing buttons top to bottom from the column's left
        /// edge, centred in the space between the armour box and the weight box (the whole side
        /// of the panel when a box is missing).
        /// </summary>
        private static void LayoutInventoryColumn(InventoryGui gui)
        {
            RectTransform panel = gui != null ? gui.m_player : null;
            if (panel == null)
            {
                return;
            }
            float size = Size(gui);
            int count = 0;
            foreach (IconEntry entry in iconButtons)
            {
                if (Showing(entry, Column.Inventory))
                {
                    count++;
                }
            }
            if (count == 0)
            {
                return;
            }
            RectTransform armor = Box(gui, "m_armor", panel, "Armor");
            RectTransform weight = Box(gui, "m_weight", panel, "Weight");
            float top = armor != null ? InPanel(panel, armor).yMin : panel.rect.height;
            float bottom = weight != null ? InPanel(panel, weight).yMax : 0f;
            float stack = count * size + (count - 1) * Gap;
            float x = ColumnLeft(panel) + size * 0.5f;
            float y = (top + bottom) * 0.5f + stack * 0.5f - size * 0.5f;
            foreach (IconEntry entry in iconButtons)
            {
                if (!Showing(entry, Column.Inventory))
                {
                    continue;
                }
                RectTransform rect = (RectTransform)entry.Button.transform;
                rect.sizeDelta = new Vector2(size, size);
                Pin(rect, new Vector2(x, y));
                y -= size + Gap;
            }
        }

        /// <summary>
        /// Stacks the chest column's showing buttons beside the chest panel from the top down,
        /// the first one level with the top of the panel, and says how many places they took.
        /// </summary>
        private static int LayoutChestColumn(InventoryGui gui)
        {
            RectTransform panel = gui != null ? gui.m_container : null;
            if (panel == null)
            {
                return 0;
            }
            float size = Size(gui);
            float x = ColumnLeft(panel) + size * 0.5f;
            float y = ColumnTop(panel) - size * 0.5f;
            int used = 0;
            foreach (IconEntry entry in iconButtons)
            {
                if (!Showing(entry, Column.Chest))
                {
                    continue;
                }
                RectTransform rect = (RectTransform)entry.Button.transform;
                rect.sizeDelta = new Vector2(size, size);
                Pin(rect, new Vector2(x, y));
                y -= size + Gap;
                used++;
            }
            return used;
        }

        /// <summary>
        /// The icon named by a PNG shipped with the DLL, loaded once. Null when the file is
        /// missing or unreadable, which leaves the button blank but working.
        /// The zip carries the PNGs in icons/, but a mod manager may flatten that folder into the
        /// plugin directory on install (Gale does), so both places are tried.
        /// </summary>
        internal static Sprite Icon(string name)
        {
            if (icons.TryGetValue(name, out Sprite sprite))
            {
                return sprite;
            }
            sprite = null;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = IconPath(dir, name);
                if (path != null)
                {
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (LoadPng(texture, File.ReadAllBytes(path)))
                    {
                        texture.filterMode = FilterMode.Bilinear;
                        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f), 100f);
                    }
                }
                else
                {
                    OdinsMissingPatchPlugin.Log.LogWarning("icon missing: " + name + ".png, looked in "
                        + Path.Combine(dir, "icons") + " and " + dir);
                }
            }
            catch (Exception e)
            {
                OdinsMissingPatchPlugin.Log.LogWarning("could not load icon " + name + ": " + e.Message);
            }
            icons[name] = sprite;
            return sprite;
        }

        /// <summary>
        /// The first place the named PNG exists: icons/ under the DLL's directory, then the
        /// directory itself. Null when it is in neither.
        /// </summary>
        private static string IconPath(string dir, string name)
        {
            string file = name + ".png";
            string[] candidates = { Path.Combine(Path.Combine(dir, "icons"), file), Path.Combine(dir, file) };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static MethodInfo loadImage;

        /// <summary>
        /// The game's PNG decoder, ImageConversion.LoadImage(Texture2D, byte[]). Its module is
        /// built against netstandard 2.1, which a net472 build cannot reference, so it is
        /// found at runtime instead of at compile time.
        /// </summary>
        private static bool LoadPng(Texture2D texture, byte[] png)
        {
            if (loadImage == null)
            {
                Type conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                loadImage = conversion != null
                    ? conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) })
                    : null;
                if (loadImage == null)
                {
                    OdinsMissingPatchPlugin.Log.LogWarning("ImageConversion.LoadImage not found; icons stay blank");
                    return false;
                }
            }
            return (bool)loadImage.Invoke(null, new object[] { texture, png });
        }

        /// <summary>
        /// Empties the copied label and returns its colour. The label is a TextMeshPro text,
        /// which is not among the staged reference assemblies, so it is found by its text
        /// property; its colour is the Graphic colour every UI text has.
        /// </summary>
        private static Color BlankLabel(GameObject go)
        {
            Color tint = Color.white;
            foreach (Component component in go.GetComponentsInChildren<Component>(true))
            {
                PropertyInfo text = component.GetType().GetProperty("text", typeof(string));
                if (text == null || !text.CanWrite)
                {
                    continue;
                }
                if (component is Graphic graphic)
                {
                    tint = graphic.color;
                }
                text.SetValue(component, "", null);
            }
            return tint;
        }

        private static void AddIcon(GameObject go, string icon, Color tint, float size)
        {
            GameObject child = new GameObject("icon", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(go.transform, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            float inset = size * 0.2f;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = child.GetComponent<Image>();
            image.sprite = Icon(icon);
            image.color = tint;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = image.sprite != null;
        }

        /// <summary>
        /// The tooltip of a button copied from the chest panel, made if it has none: the vanilla
        /// button carries no `UITooltip`, so every copy needs one added, and a `UITooltip` without
        /// a window prefab shows nothing at all - both are borrowed from the inventory slots.
        /// Null only when no window prefab was found anywhere, and then a caller has no tooltip.
        /// </summary>
        internal static UITooltip Tooltip(InventoryGui gui, GameObject go)
        {
            UITooltip tip = go.GetComponent<UITooltip>();
            if (tip == null)
            {
                GameObject prefab = TooltipPrefab(gui);
                if (prefab == null)
                {
                    return null;
                }
                tip = go.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = prefab;
            }
            else if (tip.m_tooltipPrefab == null)
            {
                tip.m_tooltipPrefab = TooltipPrefab(gui);
            }
            return tip;
        }

        /// <summary>
        /// The tooltip prefab the inventory slots use, for a button that was copied without one.
        /// </summary>
        internal static GameObject TooltipPrefab(InventoryGui gui)
        {
            InventoryGrid grid = gui.m_playerGrid;
            GameObject prefab = grid != null ? grid.m_elementPrefab : null;
            InventoryElement element = prefab != null ? prefab.GetComponent<InventoryElement>() : null;
            if (element != null && element.m_tooltip != null && element.m_tooltip.m_tooltipPrefab != null)
            {
                return element.m_tooltip.m_tooltipPrefab;
            }
            foreach (UITooltip tip in gui.GetComponentsInChildren<UITooltip>(true))
            {
                if (tip.m_tooltipPrefab != null)
                {
                    return tip.m_tooltipPrefab;
                }
            }
            return null;
        }

        /// <summary>
        /// A copy of the chest panel's Take all button that keeps its label instead of trading it
        /// for an icon: the game's skin, hover tint and click sound, with words of our own and a
        /// width cut to fit them. It is built the first time it is shown and kept from then on,
        /// and placed on every frame, since where it belongs follows whether the game's two
        /// buttons are hidden and what it says follows the chest. Its owner makes it, hands it
        /// to <see cref="Add(TextButton, Spot, Func{InventoryGui, bool}, Action{InventoryGui})"/>
        /// and sets its words from the refresh it hands over with it.
        /// </summary>
        internal sealed class TextButton
        {
            /// <summary>The room between the label and each end of the button, in UI pixels.</summary>
            private const float Margin = 16f;

            private readonly string name;
            private readonly UnityAction onClick;

            private Button button;
            private UITooltip tip;
            private string label;
            private string tooltip;
            private Component labelText;
            private PropertyInfo labelWidth;

            internal TextButton(string name, UnityAction onClick)
            {
                this.name = name;
                this.onClick = onClick;
            }

            /// <summary>
            /// Shows the button, building it the first time. False when the panel has no Take all
            /// button to copy, in which case the caller leaves the panel alone.
            /// </summary>
            internal bool Show(InventoryGui gui)
            {
                if (button == null && !Create(gui))
                {
                    return false;
                }
                button.gameObject.SetActive(true);
                return true;
            }

            internal void Hide()
            {
                if (button != null)
                {
                    button.gameObject.SetActive(false);
                }
            }

            /// <summary>The screen it was on is gone; the next Show builds it afresh.</summary>
            internal void Forget()
            {
                button = null;
            }

            /// <summary>
            /// Either in the spot of the vanilla button <paramref name="at"/>, keeping the edge
            /// that button is lined up on - its left for Take all, its right for Stack all -
            /// while the extra width grows the other way, or, without one, in the column beside
            /// the panel, <paramref name="slot"/> places down from its top edge.
            /// </summary>
            internal void Place(InventoryGui gui, Button at, bool keepLeft, int slot)
            {
                RectTransform takeAll = gui.m_takeAllButton != null
                    ? (RectTransform)gui.m_takeAllButton.transform : null;
                if (takeAll == null)
                {
                    return;
                }
                float height = takeAll.rect.height;
                float width = Mathf.Max(takeAll.rect.width, LabelWidth() + 2f * Margin);
                RectTransform rect = (RectTransform)button.transform;
                rect.sizeDelta = new Vector2(width, height);
                RectTransform spot = at != null ? (RectTransform)at.transform : null;
                if (spot == null)
                {
                    RectTransform panel = gui.m_container;
                    Pin(rect, new Vector2(
                        ColumnLeft(panel) + width * 0.5f,
                        ColumnTop(panel) - height * 0.5f - slot * (height + Gap)));
                    return;
                }
                rect.anchorMin = spot.anchorMin;
                rect.anchorMax = spot.anchorMax;
                rect.pivot = spot.pivot;
                float grown = width - spot.rect.width;
                rect.anchoredPosition = spot.anchoredPosition
                    + new Vector2(keepLeft ? grown * (1f - spot.pivot.x) : -grown * spot.pivot.x, 0f);
            }

            /// <summary>
            /// How wide the label wants to be for its current text, from the text's own
            /// preferred width (a TextMeshPro property, read by reflection since that assembly
            /// is not referenced). Zero when there is no label to ask.
            /// </summary>
            private float LabelWidth()
            {
                if (labelText == null)
                {
                    Transform text = button.transform.Find("Text");
                    if (text == null)
                    {
                        return 0f;
                    }
                    foreach (Component component in text.GetComponents<Component>())
                    {
                        PropertyInfo property = component.GetType().GetProperty("preferredWidth", typeof(float));
                        if (property != null && property.CanRead)
                        {
                            labelText = component;
                            labelWidth = property;
                            break;
                        }
                    }
                }
                return labelText != null ? (float)labelWidth.GetValue(labelText, null) : 0f;
            }

            private bool Create(InventoryGui gui)
            {
                Button takeAll = gui.m_takeAllButton;
                if (takeAll == null)
                {
                    return false;
                }
                GameObject go = UnityEngine.Object.Instantiate(takeAll.gameObject, takeAll.transform.parent);
                go.name = name;
                button = go.GetComponent<Button>();
                if (button == null)
                {
                    UnityEngine.Object.Destroy(go);
                    return false;
                }
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(onClick);
                go.transform.SetSiblingIndex(takeAll.transform.GetSiblingIndex());
                tip = Tooltip(gui, go);
                label = null;
                tooltip = null;
                labelText = null;
                labelWidth = null;
                return true;
            }

            /// <summary>
            /// The label is a TextMeshPro text, which is not among the staged reference
            /// assemblies, so it is set through the one property both it and a legacy Text have.
            /// <paramref name="text"/> is a $omp_ token, which nothing else would translate -
            /// unlike a tooltip, a label on a component is shown exactly as it is written. It is
            /// translated on the way in and compared translated, so the caller may hand the same
            /// token over every frame and the label still follows a language change.
            /// </summary>
            internal void SetLabel(string text, params string[] words)
            {
                if (Localization.instance != null)
                {
                    text = Localization.instance.Localize(text, words);
                }
                if (text == label)
                {
                    return;
                }
                label = text;
                foreach (Component component in button.GetComponentsInChildren<Component>(true))
                {
                    PropertyInfo property = component.GetType().GetProperty("text", typeof(string));
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(component, text, null);
                    }
                }
            }

            /// <summary>
            /// The hover text. The copy comes without a `UITooltip`, so <see cref="Tooltip"/>
            /// made one when the button was built; without it there is nothing to write to.
            /// </summary>
            internal void SetTooltip(string topic, string text)
            {
                if (text == tooltip || tip == null)
                {
                    return;
                }
                tooltip = text;
                tip.m_topic = topic;
                tip.m_text = text;
            }
        }
    }
}
