using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using Category = OdinsMissingPatch.UniversalPins.Category;
using Biome = Heightmap.Biome;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Universal pins (<see cref="AutoPins"/>, and whatever a map table brings) are coloured by
    /// the biome they stand in instead of the game's dim grey for shared pins, so a coloured pin
    /// reads as an auto pin and its biome at a glance. Ore and place pins disappear when the
    /// large map is zoomed out past their zoom setting, so the continent view is not a carpet of
    /// hammers. Dungeon and dragon nest pins are drawn with an icon of their own instead of a
    /// name, which the game's tooltip shows on hover. Toggles beside the game's own at the bottom
    /// right of the large map show or hide each category on both maps, the way the legend hides
    /// an icon. The shared-map button on the large map still fades all of them together, and the
    /// legend still hides a whole icon.
    /// </summary>
    internal sealed class PinLooks : Tweak
    {
        internal static readonly PinLooks Instance = new PinLooks();

        private PinLooks() { }

        private ConfigEntry<Color> meadows;
        private ConfigEntry<Color> blackForest;
        private ConfigEntry<Color> swamp;
        private ConfigEntry<Color> mountain;
        private ConfigEntry<Color> plains;
        private ConfigEntry<Color> mistlands;
        private ConfigEntry<Color> ashLands;
        private ConfigEntry<Color> deepNorth;
        private ConfigEntry<Color> ocean;
        private readonly ConfigEntry<float>[] zoom = new ConfigEntry<float>[4];
        private readonly ConfigEntry<bool>[] show = new ConfigEntry<bool>[4];
        private ConfigEntry<bool> mapToggles;
        private ConfigEntry<bool> icons;

        internal override string Section => "Pin Looks";

        protected override string Summary =>
            "Pins that belong to nobody (auto pins, and what map tables share of them) are " +
            "coloured by their biome, dungeons and some places get icons of their own, ore and " +
            "place pins hide when the large map is zoomed far out, and toggles on the large map " +
            "show or hide dungeon, ore and place pins.";

        protected override void Bind(ConfigFile config)
        {
            meadows = BindColour(config, "Meadows", new Color(0.62f, 0.9f, 0.45f));
            blackForest = BindColour(config, "BlackForest", new Color(0.35f, 0.75f, 0.45f));
            swamp = BindColour(config, "Swamp", new Color(0.8f, 0.6f, 0.38f));
            mountain = BindColour(config, "Mountain", new Color(0.72f, 0.88f, 1f));
            plains = BindColour(config, "Plains", new Color(1f, 0.9f, 0.4f));
            mistlands = BindColour(config, "Mistlands", new Color(0.8f, 0.75f, 0.9f));
            ashLands = BindColour(config, "AshLands", new Color(1f, 0.45f, 0.35f));
            deepNorth = BindColour(config, "DeepNorth", new Color(0.85f, 0.97f, 1f));
            ocean = BindColour(config, "Ocean", new Color(0.45f, 0.75f, 1f));
            zoom[(int)Category.Dungeon] = BindZoom(config, "DungeonZoom", 1f, "dungeon");
            zoom[(int)Category.Ore] = BindZoom(config, "OreZoom", 0.5f, "ore");
            zoom[(int)Category.Place] = BindZoom(config, "PlaceZoom", 0.5f, "place");
            mapToggles = config.Bind(Section, "MapToggles", true,
                "Show a toggle for dungeon, ore and place pins at the bottom right of the large map, " +
                "above the game's own for your position.");
            show[(int)Category.Dungeon] = BindShow(config, "ShowDungeons", "dungeon");
            show[(int)Category.Ore] = BindShow(config, "ShowOre", "ore");
            show[(int)Category.Place] = BindShow(config, "ShowPlaces", "place");
            icons = config.Bind(Section, "Icons", true,
                "Draw dungeon pins with an icon of their own - burial chambers, a troll cave, a crypt, " +
                "a frost cave, winding tunnels, any other entrance - and dragon eggs, fuling villages " +
                "and tar pits with theirs, without a name on the map; hovering " +
                "one on the large map names it.");
            OnSettingChanged(config, Toggles.Refresh);
        }

        private ConfigEntry<bool> BindShow(ConfigFile config, string key, string category)
        {
            return config.Bind(Section, key, true,
                "Whether " + category + " pins are shown on the map and the minimap; the large map's toggle sets this.");
        }

        private ConfigEntry<Color> BindColour(ConfigFile config, string biome, Color value)
        {
            return config.Bind(Section, biome + "Colour", value,
                "Colour of a pin that belongs to nobody standing in the " + biome + ".");
        }

        private ConfigEntry<float> BindZoom(ConfigFile config, string key, float value, string category)
        {
            return config.Bind(Section, key, value, new ConfigDescription(
                "The large map hides " + category + " pins when zoomed out further than this. " +
                "The map zooms from 0.01 (closest) to 1 (whole world); 1 never hides them, and " +
                "0.5 is where the game stops showing pin names.",
                new AcceptableValueRange<float>(0.01f, 1f)));
        }

        private Color ColourOf(Biome biome)
        {
            switch (biome)
            {
                case Biome.Meadows: return meadows.Value;
                case Biome.BlackForest: return blackForest.Value;
                case Biome.Swamp: return swamp.Value;
                case Biome.Mountain: return mountain.Value;
                case Biome.Plains: return plains.Value;
                case Biome.Mistlands: return mistlands.Value;
                case Biome.AshLands: return ashLands.Value;
                case Biome.DeepNorth: return deepNorth.Value;
                case Biome.Ocean: return ocean.Value;
                default: return Color.white;
            }
        }

        /// <summary>
        /// The biome under each pin, from the world generator's noise - no terrain has to be
        /// loaded, which Heightmap.FindBiome would need. A pin never moves, so it is asked once.
        /// </summary>
        private static readonly ConditionalWeakTable<Minimap.PinData, object> Biomes =
            new ConditionalWeakTable<Minimap.PinData, object>();

        private static Biome BiomeOf(Minimap.PinData pin)
        {
            if (Biomes.TryGetValue(pin, out object cached))
            {
                return (Biome)cached;
            }
            if (WorldGenerator.instance == null)
            {
                return Biome.None;
            }
            Biome biome = WorldGenerator.instance.GetBiome(pin.m_pos);
            Biomes.Add(pin, biome);
            return biome;
        }

        private static readonly ConditionalWeakTable<GameObject, Graphic> NameTexts =
            new ConditionalWeakTable<GameObject, Graphic>();

        /// <summary>
        /// The pin name's text. PinNameData holds it as a TMP_Text, a TextMeshPro type the build
        /// cannot reference, so it is found as the Graphic whose type is TextMeshPro's - the same
        /// child the game's own GetComponentInChildren&lt;TMP_Text&gt; finds.
        /// </summary>
        private static Graphic NameText(Minimap.PinNameData name)
        {
            GameObject label = name != null ? name.PinNameGameObject : null;
            if (label == null)
            {
                return null;
            }
            if (!NameTexts.TryGetValue(label, out Graphic text))
            {
                foreach (Graphic graphic in label.GetComponentsInChildren<Graphic>(includeInactive: true))
                {
                    if (graphic.GetType().Name.StartsWith("TextMeshPro"))
                    {
                        text = graphic;
                        break;
                    }
                }
                NameTexts.Add(label, text);
            }
            return text;
        }

        /// <summary>
        /// The icons of assets/icons that stand in for a pin's name, by the name token the pin was
        /// made with - the one thing about a pin that survives the profile, a table and a
        /// broadcast. The tokens are the game's own (Teleport.m_enterText of the entrance, the
        /// dragon egg's item name) or AutoPins' for the places it recognises.
        /// </summary>
        private static readonly Dictionary<string, string> IconsByName = new Dictionary<string, string>
        {
            { "$location_forestcrypt", "map_chamber" },
            { "$location_forestcave", "map_troll" },
            { "$location_sunkencrypt", "map_crypt" },
            { "$location_mountaincave", "map_ice_cave" },
            { "$location_thehole", "map_winding_tunnels" },
            { "$item_dragonegg", "map_dragons_nest" },
            { "$omp_place_fulingvillage", "map_fuling_village" },
            { "$omp_place_tarpit", "map_tar_pit" },
        };

        /// <summary>What a dungeon whose name has no icon of its own is drawn with.</summary>
        private const string EntranceIcon = "map_entrance";

        /// <summary>
        /// The sprite a universal pin is drawn with instead of its type's - every dungeon pin, and
        /// a place IconsByName knows (dragon eggs, fuling villages, tar pits) - or null when it keeps the vanilla icon and its name.
        /// </summary>
        internal static Sprite IconOf(Minimap.PinData pin)
        {
            if (!Instance.On || !Instance.icons.Value || !UniversalPins.TryGetCategory(pin, out Category category))
            {
                return null;
            }
            if (IconsByName.TryGetValue(pin.m_name, out string icon))
            {
                return UiAssets.Icon(icon);
            }
            return category == Category.Dungeon ? UiAssets.Icon(EntranceIcon) : null;
        }

        /// <summary>
        /// UpdatePins writes every drawn pin's colour on each run, and only runs when the map moved
        /// or a pin changed - so the tint goes on right after it, at the same rate. Culling turns
        /// the pin's marker off; the game never turns a marker back on itself, so this also does
        /// that for every universal pin that is not culled, including after the tweak is switched
        /// off.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        private static class Tint
        {
            private static void Postfix(Minimap __instance)
            {
                bool on = Instance.On;
                bool large = __instance.m_mode == Minimap.MapMode.Large;
                float fade = __instance.m_sharedMapDataFade;
                foreach (Minimap.PinData pin in __instance.m_pins)
                {
                    if (pin.m_uiElement == null || !UniversalPins.TryGetCategory(pin, out Category category))
                    {
                        continue;
                    }
                    // No zoom or show setting (portals, until they are pinned) never hides.
                    ConfigEntry<float> hideAt = Instance.zoom[(int)category];
                    ConfigEntry<bool> shown = Instance.show[(int)category];
                    bool culled = on && ((large && hideAt != null && __instance.LargeZoom > hideAt.Value)
                        || (shown != null && !shown.Value));
                    if (pin.m_uiElement.gameObject.activeSelf == culled)
                    {
                        pin.m_uiElement.gameObject.SetActive(!culled);
                    }
                    Minimap.PinNameData name = pin.m_NamePinData;
                    if (culled)
                    {
                        if (name != null && name.PinNameGameObject != null)
                        {
                            name.PinNameGameObject.SetActive(false);
                        }
                        continue;
                    }
                    if (pin.m_iconElement == null)
                    {
                        continue;
                    }
                    // Put back as well as swapped, so switching Icons off shows the vanilla icon.
                    Sprite icon = IconOf(pin);
                    Sprite sprite = icon != null ? icon : pin.m_icon;
                    if (pin.m_iconElement.sprite != sprite)
                    {
                        pin.m_iconElement.sprite = sprite;
                    }
                    if (!on)
                    {
                        continue;
                    }
                    if (icon != null)
                    {
                        // The icon is drawn in colour and says what the place is; Hover names it.
                        pin.m_iconElement.color = new Color(1f, 1f, 1f, fade);
                        if (name != null && name.PinNameGameObject != null)
                        {
                            name.PinNameGameObject.SetActive(false);
                        }
                        continue;
                    }
                    Color colour = Instance.ColourOf(BiomeOf(pin));
                    colour.a = fade;
                    pin.m_iconElement.color = colour;
                    Graphic text = NameText(name);
                    if (text != null)
                    {
                        text.color = colour;
                    }
                }
            }
        }

        /// <summary>
        /// One toggle per category with a show setting, cut from the game's "visible to other
        /// players" panel and stacked above it and the shared-map panel, at the step between
        /// those two. The copy keeps nothing of the original's wiring: its listener (the
        /// public-position setter) is replaced, and its gamepad hotkey removed, which would
        /// otherwise flip the copy together with the original.
        /// </summary>
        private static class Toggles
        {
            /// <summary>From the centre of the game's public-position panel to the shared-map panel's.</summary>
            private const float Step = 51f;

            private static readonly Toggle[] Built = new Toggle[4];

            internal static void Build(Minimap map)
            {
                Toggle original = map.m_publicPosition;
                RectTransform panel = original != null ? original.transform.parent as RectTransform : null;
                if (panel == null)
                {
                    return;
                }
                int row = 2;
                for (int i = 0; i < Built.Length; i++)
                {
                    ConfigEntry<bool> shown = Instance.show[i];
                    if (shown == null)
                    {
                        continue;
                    }
                    GameObject copy = Object.Instantiate(panel.gameObject, panel.parent);
                    copy.name = "OMP_Show" + (Category)i;
                    ((RectTransform)copy.transform).anchoredPosition = panel.anchoredPosition + new Vector2(0f, Step * row++);
                    foreach (UIGamePad pad in copy.GetComponentsInChildren<UIGamePad>(true))
                    {
                        if (pad.m_hint != null)
                        {
                            Object.DestroyImmediate(pad.m_hint);
                        }
                        Object.DestroyImmediate(pad);
                    }
                    Toggle toggle = copy.GetComponentInChildren<Toggle>(true);
                    toggle.onValueChanged = new Toggle.ToggleEvent();
                    toggle.isOn = shown.Value;
                    toggle.onValueChanged.AddListener(value => shown.Value = value);
                    Transform label = toggle.transform.Find("Label");
                    if (label != null && Localization.instance != null)
                    {
                        SetText(label, Localization.instance.Localize(Tokens[i]));
                    }
                    Built[i] = toggle;
                }
                Refresh();
            }

            private static readonly string[] Tokens = { "$omp_map_dungeons", "$omp_map_ore", "$omp_map_places", null };

            /// <summary>
            /// The label is a TextMeshPro text, which is not among the staged reference assemblies,
            /// so it is set through its text property by reflection.
            /// </summary>
            private static void SetText(Transform label, string text)
            {
                foreach (Component component in label.GetComponents<Component>())
                {
                    System.Reflection.PropertyInfo property = component.GetType().GetProperty("text", typeof(string));
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(component, text, null);
                    }
                }
            }

            /// <summary>A setting of the section changed, from a toggle or the config: the toggles and the pins follow.</summary>
            internal static void Refresh()
            {
                bool visible = Instance.On && Instance.mapToggles.Value;
                for (int i = 0; i < Built.Length; i++)
                {
                    Toggle toggle = Built[i];
                    if (toggle == null)
                    {
                        continue;
                    }
                    Transform panel = toggle.transform.parent;
                    if (panel.gameObject.activeSelf != visible)
                    {
                        panel.gameObject.SetActive(visible);
                    }
                    toggle.SetIsOnWithoutNotify(Instance.show[i].Value);
                }
                if (Minimap.instance != null)
                {
                    Minimap.instance.m_pinUpdateRequired = true;
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
        [LoadHook]
        private static class BuildToggles
        {
            private static void Postfix(Minimap __instance) => Toggles.Build(__instance);
        }

        /// <summary>
        /// A pin drawn with an icon instead of its name shows the name as the game's tooltip while
        /// the pointer is on it in the large map. The pin markers take no raycasts - the map
        /// image under them gets every click - so the hover is found here, by the marker's rect,
        /// and handed to a UITooltip on the marker, which hides itself once the pointer leaves
        /// that rect or the marker is destroyed. Mouse only, like the vanilla pin names.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
        private static class Hover
        {
            private static Minimap.PinData hovered;

            private static void Postfix(Minimap __instance)
            {
                Minimap.PinData pin = null;
                if (Instance.On && Instance.icons.Value && __instance.m_mode == Minimap.MapMode.Large
                    && ZInput.IsMouseActive())
                {
                    pin = PinUnderPointer(__instance);
                }
                if (pin != hovered)
                {
                    hovered = pin;
                    if (pin != null)
                    {
                        Show(pin);
                    }
                }
            }

            private static Minimap.PinData PinUnderPointer(Minimap map)
            {
                Vector2 pointer = ZInput.pointerPosition;
                foreach (Minimap.PinData pin in map.m_pins)
                {
                    RectTransform marker = pin.m_uiElement;
                    if (marker != null && marker.gameObject.activeInHierarchy
                        && pin.m_iconElement != null && pin.m_iconElement.sprite != pin.m_icon
                        && RectTransformUtility.RectangleContainsScreenPoint(marker, pointer)
                        && IconOf(pin) != null)
                    {
                        return pin;
                    }
                }
                return null;
            }

            private static void Show(Minimap.PinData pin)
            {
                GameObject marker = pin.m_uiElement.gameObject;
                UITooltip tip = marker.GetComponent<UITooltip>();
                if (tip == null)
                {
                    GameObject prefab = InventoryGui.instance != null ? UiAssets.TooltipPrefab(InventoryGui.instance) : null;
                    if (prefab == null)
                    {
                        return;
                    }
                    tip = marker.AddComponent<UITooltip>();
                    tip.m_tooltipPrefab = prefab;
                }
                tip.m_topic = pin.m_name;
                tip.m_text = "";
                tip.OnHoverStart(marker);
                // The window is made under the marker's nearest canvas, which is the map's and may
                // clip it; the top canvas draws it over everything, the way an inventory tooltip is.
                GameObject window = Traverse.Create(typeof(UITooltip)).Field("m_tooltip").GetValue<GameObject>();
                Canvas canvas = marker.GetComponentInParent<Canvas>();
                if (window != null && canvas != null && canvas.rootCanvas.transform != window.transform.parent)
                {
                    window.transform.SetParent(canvas.rootCanvas.transform, false);
                }
            }
        }
    }
}
