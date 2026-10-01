using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Category = OdinsMissingPatch.UniversalPins.Category;
using PinType = Minimap.PinType;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The places worth a pin get one when you have been there: dungeon entrances, the ore
    /// deposits you strike, and places without an interior (fuling villages, tar pits, dragon
    /// eggs, Dvergr excavations, ...). Each is an ordinary map pin with a vanilla
    /// icon and the game's own name for the place, made universal (<see cref="UniversalPins"/>) so
    /// a map table hands it to everyone exactly once - and, with Share on, sent to every player
    /// online the moment it is made (<see cref="PinBroadcast"/>). Right click removes one for good.
    /// <para>
    /// What counts is read off the game's own data rather than a list of names, so a new biome's
    /// places come in without a change here: a dungeon is a location with an interior, a place is
    /// a location holding a dungeon generator, tar or an item that cannot be teleported, and ore
    /// is a rock whose drop some fuelled smelter takes. A ruin holding a vegvisir is not
    /// pinned unless PlaceList names it: reading the stone pins the boss, which is what the
    /// ruin is there for. PlaceList adds what no rule sees,
    /// ExtraOre and SkipOre adjust what counts as ore.
    /// </para>
    /// <para>
    /// Nothing sweeps the world. Locations come from the game's registry of what is loaded -
    /// tens of entries - looked at every few seconds within DiscoverRange of the player; ore is
    /// pinned from the hit itself, so a vein nobody has struck is never given away.
    /// </para>
    /// <para>
    /// Portals are not pinned here; they will be a feature of their own. Category.Portal stays
    /// reserved in <see cref="UniversalPins"/> for it.
    /// </para>
    /// </summary>
    internal sealed class AutoPins : Tweak
    {
        internal static readonly AutoPins Instance = new AutoPins();

        private AutoPins() { }

        private const float SweepInterval = 3f;

        /// <summary>
        /// The places no rule sees, pinned by default, each with the token its pin is named by -
        /// the Dvergr sites of the Mistlands hold nothing that sets them apart from a ruin. Names
        /// from the game's location table as of the Deep North; the icon-flagged ones (boss
        /// altars, traders, Hildir, the bog witch, the Forge) are drawn by the game already and
        /// never belong here.
        /// </summary>
        private static readonly Dictionary<string, string> DefaultPlaces = new Dictionary<string, string>
        {
            { "Mistlands_Excavation1", "$omp_place_excavation" },
            { "Mistlands_Excavation2", "$omp_place_excavation" },
            { "Mistlands_Excavation3", "$omp_place_excavation" },
            { "Mistlands_GuardTower1_new", "$omp_place_guardtower" },
            { "Mistlands_GuardTower2_new", "$omp_place_guardtower" },
            { "Mistlands_GuardTower3_new", "$omp_place_guardtower" },
            { "Mistlands_GuardTower1_ruined_new", "$omp_place_guardtower" },
            { "Mistlands_GuardTower1_ruined_new2", "$omp_place_guardtower" },
            { "Mistlands_GuardTower3_ruined_new", "$omp_place_guardtower" },
            { "Mistlands_Lighthouse1_new", "$omp_place_lighthouse" },
        };

        private static readonly PinType[] AllowedIcons =
            { PinType.Icon0, PinType.Icon1, PinType.Icon2, PinType.Icon3, PinType.Icon4 };

        private ConfigEntry<bool> dungeons;
        private ConfigEntry<bool> ore;
        private ConfigEntry<bool> places;
        private ConfigEntry<float> discoverRange;
        private ConfigEntry<float> pinSpacing;
        private ConfigEntry<PinType> dungeonIcon;
        private ConfigEntry<PinType> oreIcon;
        private ConfigEntry<PinType> placeIcon;
        private ConfigEntry<string> extraOre;
        private ConfigEntry<string> skipOre;
        private ConfigEntry<bool> showMessage;
        private ConfigEntry<MinedOut> minedOut;
        private ConfigEntry<bool> share;

        // Parsed from PlaceList: prefab name -> the pin's name, or null for "the game's label".
        private Dictionary<string, string> placeNames = new Dictionary<string, string>();

        internal override string Section => "Auto Pins";

        protected override string Summary =>
            "Dungeon entrances, ore deposits you strike and places get a map " +
            "pin by themselves when you get there. The pins belong to nobody: map tables share " +
            "them once, the large map's shared-map button hides them, right click removes one for good.";

        protected override void Bind(ConfigFile config)
        {
            dungeons = config.Bind(Section, "Dungeons", true,
                "Pin dungeon and cave entrances, named the way the game names them when you enter.");
            ore = config.Bind(Section, "Ore", true,
                "Pin an ore deposit when you strike it: any rock that drops something a smelter or " +
                "blast furnace takes (copper, muddy scrap piles, silver, giant armour, flametal, ...), " +
                "plus what ExtraOre adds and minus what SkipOre leaves out.");
            places = config.Bind(Section, "Places", true,
                "Pin places without an interior: camps and villages the game generates (fuling " +
                "villages, the Ashlands ruins, ...), tar pits, places holding an item that cannot be " +
                "teleported (dragon eggs), and the places in PlaceList. Ruins holding a vegvisir are " +
                "left out: reading the stone pins the boss already.");
            discoverRange = config.Bind(Section, "DiscoverRange", 40f, new ConfigDescription(
                "Metres you have to come within for a dungeon or place to be pinned - you have to " +
                "have been there, not merely seen it from a hill. 0 pins whatever is loaded around you.",
                new AcceptableValueRange<float>(0f, 300f)));
            pinSpacing = config.Bind(Section, "PinSpacing", 10f, new ConfigDescription(
                "No pin is added within this many metres of a pin already on your map - one you " +
                "placed by hand, one another player shared, one another mod made - so a place " +
                "marked once is not marked twice. Death pins do not count. 0 turns this off.",
                new AcceptableValueRange<float>(0f, 50f)));
            const string icons = " Icon0 is the fire, Icon1 the house, Icon2 the hammer, Icon3 the orb, " +
                "Icon4 the portal; each category having its own icon is what lets the legend hide one.";
            dungeonIcon = config.Bind(Section, "DungeonIcon", PinType.Icon1, "Map icon of a dungeon pin." + icons);
            oreIcon = config.Bind(Section, "OreIcon", PinType.Icon3, "Map icon of an ore pin." + icons);
            placeIcon = config.Bind(Section, "PlaceIcon", PinType.Icon0, "Map icon of a place pin." + icons);
            BindList(config, "PlaceList", string.Join(", ", DefaultPlaces.Keys),
                "Comma separated location names to pin besides the ones found by themselves. A " +
                "name may be followed by =$token to name the pin by a translation token, which " +
                "also renames a place found by itself; a name the mod does not know is pinned under " +
                "the game's own label for the place, or the location name when it has none. The dev " +
                "build's omp_locations console command lists every location of the current game.",
                ParsePlaces);
            extraOre = BindList(config, "ExtraOre", "Softtissue",
                "Comma separated item names that count as ore besides what a smelter takes, the pin " +
                "named after the item. Soft tissue, which the eitr refinery burns, comes from the " +
                "giant remains of the Mistlands. Obsidian or BlackMarble could go here too.",
                names => ForgetOre());
            skipOre = BindList(config, "SkipOre", "TinOre",
                "Comma separated item names that do not count as ore although a smelter takes them. " +
                "Tin is left out by default: its deposits line every Black Forest shore and would " +
                "carpet the map.",
                names => ForgetOre());
            minedOut = config.Bind(Section, "MinedOut", MinedOut.Tick,
                "What happens to an ore pin once its deposit is mined out, by you or anyone, noticed " +
                "when you come by: Tick crosses it off the way a click does, Remove takes it off the " +
                "map for good, Keep leaves it as it was.");
            showMessage = config.Bind(Section, "ShowMessage", true,
                "Say so in the top left corner when a pin is added, the way the game does for a boss altar.");
            share = config.Bind(Section, "Share", true,
                "Send every pin added here to every player online the moment it is made, and take " +
                "the pins they send, without a map table in between; on joining, ask everyone for " +
                "theirs once. Only players with the mod take part, and each applies their own " +
                "settings to what they get. Off, the pins travel on map tables only.");
        }

        private void ParsePlaces(List<string> entries)
        {
            var parsed = new Dictionary<string, string>();
            foreach (string entry in entries)
            {
                string name = entry;
                string token = null;
                int eq = entry.IndexOf('=');
                if (eq >= 0)
                {
                    name = entry.Substring(0, eq).Trim();
                    token = entry.Substring(eq + 1).Trim();
                }
                if (name.Length == 0)
                {
                    OdinsMissingPatchPlugin.Log.LogWarning(Section + ": '" + entry + "' has no location name and is ignored");
                    continue;
                }
                if (string.IsNullOrEmpty(token))
                {
                    DefaultPlaces.TryGetValue(name, out token);
                }
                parsed[name] = token;
            }
            placeNames = parsed;
        }

        /// <summary>Whether pins go out to and come in from the other players right away.</summary>
        internal bool Sharing => On && share.Value;

        private static PinType Icon(ConfigEntry<PinType> entry)
        {
            return Array.IndexOf(AllowedIcons, entry.Value) >= 0 ? entry.Value : (PinType)entry.DefaultValue;
        }

        /// <summary>This client's icon for a category, and whether it wants that category at all.</summary>
        private bool Wants(Category category, out PinType type)
        {
            switch (category)
            {
                case Category.Dungeon:
                    type = Icon(dungeonIcon);
                    return dungeons.Value;
                case Category.Ore:
                    type = Icon(oreIcon);
                    return ore.Value;
                case Category.Place:
                    type = Icon(placeIcon);
                    return places.Value;
                default:
                    // Portals are not pinned yet, by anyone.
                    type = PinType.Icon4;
                    return false;
            }
        }

        private bool InRange(Vector3 origin, Vector3 pos)
        {
            float range = discoverRange.Value;
            return range <= 0f || (pos - origin).sqrMagnitude <= range * range;
        }

        /// <summary>
        /// Whether a saved pin that is not a universal one lies within PinSpacing of pos: the
        /// player's own, one another player shared through a table, one another mod made (the
        /// discovery pin mods place ordinary pins). Death pins mark nothing worth finding again.
        /// </summary>
        private bool NearOtherPin(Minimap map, Vector3 pos)
        {
            float spacing = pinSpacing.Value;
            if (spacing <= 0f)
            {
                return false;
            }
            foreach (Minimap.PinData pin in map.m_pins)
            {
                if (pin.m_save && pin.m_type != PinType.Death && !UniversalPins.IsUniversal(pin)
                    && Utils.DistanceXZ(pos, pin.m_pos) < spacing)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pins a place this client found itself, and tells the other players about it.
        /// </summary>
        private void TryPin(Category category, Vector3 pos, string name, PinType type)
        {
            if (Place(category, pos, name, type, showMessage.Value))
            {
                PinBroadcast.Send(category, pos, name);
            }
        }

        /// <summary>
        /// A pin another player made, sent to us: taken under this client's own switches and icon,
        /// and never sent on. A live one is announced like a find of our own; a catch-up is not.
        /// </summary>
        internal void Receive(Category category, Vector3 pos, string name, bool announce)
        {
            if (Wants(category, out PinType type))
            {
                Place(category, pos, name, type, announce && showMessage.Value);
            }
        }

        /// <summary>
        /// Pins pos unless a universal pin of the category is already there - whoever put it
        /// there - the player removed one there before, or some other pin is close by. True when
        /// a pin was added.
        /// </summary>
        private bool Place(Category category, Vector3 pos, string name, PinType type, bool announce)
        {
            Minimap map = Minimap.instance;
            Player player = Player.m_localPlayer;
            if (map == null || player == null
                || UniversalPins.Find(map, pos, category) != null || UniversalPins.IsDismissed(category, pos)
                || NearOtherPin(map, pos))
            {
                return false;
            }
            Minimap.PinData pin = UniversalPins.Add(map, pos, category, type, name);
            if (announce)
            {
                Sprite icon = PinLooks.IconOf(pin);
                player.Message(MessageHud.MessageType.TopLeft,
                    string.IsNullOrEmpty(name) ? "$msg_pin_added" : "$msg_pin_added: " + name, 0,
                    icon != null ? icon : pin.m_icon);
            }
            return true;
        }

        // --- locations ------------------------------------------------------------------------

        /// <summary>What a loaded location is, worked out once per instance.</summary>
        private sealed class Site
        {
            public string PrefabName;
            /// <summary>The game draws its own icon for it (altars, traders, the Forge).</summary>
            public bool GameIcon;
            /// <summary>The door from outside, when it has an interior and one was found.</summary>
            public Teleport Entrance;
            /// <summary>Which rule makes a place without an interior worth a pin, if any.</summary>
            public PlaceKind Kind;
            /// <summary>The name that rule gives it: a token, never null when Kind is not None.</summary>
            public string KindName;
        }

        internal enum PlaceKind { None, Treasure, Generated, Tar }

        private static readonly ConditionalWeakTable<Location, Site> Sites = new ConditionalWeakTable<Location, Site>();

        private static Site Describe(Location location)
        {
            if (Sites.TryGetValue(location, out Site site))
            {
                if (location.m_hasInterior && site.Entrance == null)
                {
                    // The door may not have been there yet the first time round.
                    site.Entrance = FindEntrance(location);
                }
                return site;
            }
            site = new Site { PrefabName = Utils.GetPrefabName(location.gameObject) };
            // A location spawned on a client hangs under the LocationProxy whose ZDO names it.
            LocationProxy proxy = location.GetComponentInParent<LocationProxy>();
            ZDO zdo = proxy != null && proxy.m_nview != null ? proxy.m_nview.GetZDO() : null;
            int hash = zdo != null ? zdo.GetInt(ZDOVars.s_location) : 0;
            ZoneSystem.ZoneLocation zoneLocation = hash != 0 && ZoneSystem.instance != null
                ? ZoneSystem.instance.GetLocation(hash) : null;
            if (zoneLocation != null)
            {
                site.PrefabName = zoneLocation.m_prefabName;
                site.GameIcon = zoneLocation.m_iconAlways || zoneLocation.m_iconPlaced;
            }
            if (location.m_hasInterior)
            {
                site.Entrance = FindEntrance(location);
            }
            else
            {
                Classify(location, site);
            }
            Sites.Add(location, site);
            return site;
        }

        /// <summary>
        /// What makes a place without an interior worth a pin, most telling first. A location's
        /// networked pieces (pickables, the tar, a generator) are spawned as objects of their own;
        /// the copy under the LocationProxy keeps them as inactive children, which is why every
        /// lookup here includes inactive ones. A vegvisir rules the place out before anything
        /// else: its ruin may hold tar or rooms, and still the stone is all it is for.
        /// </summary>
        private static void Classify(Location location, Site site)
        {
            if (location.GetComponentInChildren<Vegvisir>(true) != null)
            {
                return;
            }
            foreach (Pickable pickable in location.GetComponentsInChildren<Pickable>(true))
            {
                // Only the dragon egg as of the Deep North: ores, metals and Hildir's chests are
                // the other things the game will not let through a portal, and none is picked.
                ItemDrop item = pickable.m_itemPrefab != null ? pickable.m_itemPrefab.GetComponent<ItemDrop>() : null;
                if (item != null && !item.m_itemData.m_shared.m_teleportable)
                {
                    site.Kind = PlaceKind.Treasure;
                    site.KindName = item.m_itemData.m_shared.m_name;
                    return;
                }
            }
            DungeonGenerator generator = location.m_generator != null
                ? location.m_generator : location.GetComponentInChildren<DungeonGenerator>(true);
            if (generator != null)
            {
                site.Kind = PlaceKind.Generated;
                site.KindName = ThemeName(generator.m_themes);
                return;
            }
            foreach (LiquidVolume liquid in location.GetComponentsInChildren<LiquidVolume>(true))
            {
                if (liquid.m_liquidType == LiquidType.Tar)
                {
                    site.Kind = PlaceKind.Tar;
                    site.KindName = "$omp_place_tarpit";
                    return;
                }
            }
        }

        /// <summary>For the dev build's omp_locations: which rule a location prefab falls under, and its name.</summary>
        internal static string RuleFor(Location location)
        {
            var site = new Site();
            if (!location.m_hasInterior)
            {
                Classify(location, site);
            }
            return site.Kind == PlaceKind.None ? "" : site.Kind + " " + site.KindName;
        }

        /// <summary>A camp or village the game builds from rooms, by the rooms' theme; a theme this does not know is "Ruins".</summary>
        private static string ThemeName(Room.Theme theme)
        {
            if ((theme & Room.Theme.GoblinCamp) != 0)
            {
                return "$omp_place_fulingvillage";
            }
            if ((theme & (Room.Theme.MeadowsVillage | Room.Theme.NorthVillage)) != 0)
            {
                return "$omp_place_village";
            }
            if ((theme & Room.Theme.MeadowsFarm) != 0)
            {
                return "$omp_place_farm";
            }
            if ((theme & Room.Theme.FortressRuins) != 0)
            {
                return "$omp_place_fortress";
            }
            return "$omp_place_ruins";
        }

        /// <summary>
        /// The name of the pin a place gets, or null when it gets none: PlaceList first (it both
        /// adds places and renames them), then what a rule found, named by the game's label for
        /// the place when it has one.
        /// </summary>
        private string PlaceName(Location location, Site site)
        {
            string label = !string.IsNullOrEmpty(location.m_discoverLabel) ? location.m_discoverLabel : null;
            if (placeNames.TryGetValue(site.PrefabName, out string token))
            {
                return !string.IsNullOrEmpty(token) ? token : label ?? site.KindName ?? site.PrefabName;
            }
            if (site.Kind == PlaceKind.None)
            {
                return null;
            }
            return label ?? site.KindName;
        }

        /// <summary>The dungeon's door on the outside: a Teleport below the interiors, which sit ~5000m up.</summary>
        private static Teleport FindEntrance(Location location)
        {
            foreach (Teleport teleport in location.GetComponentsInChildren<Teleport>())
            {
                if (!Character.InInterior(teleport.transform.position))
                {
                    return teleport;
                }
            }
            return null;
        }

        private void SweepLocations(Vector3 origin)
        {
            foreach (Location location in Location.s_allLocations)
            {
                if (location == null)
                {
                    continue;
                }
                Site site = Describe(location);
                if (site.GameIcon)
                {
                    continue;
                }
                if (location.m_hasInterior)
                {
                    if (!dungeons.Value)
                    {
                        continue;
                    }
                    Vector3 pos = site.Entrance != null ? site.Entrance.transform.position : location.transform.position;
                    if (InRange(origin, pos))
                    {
                        TryPin(Category.Dungeon, pos, DungeonName(location, site), Icon(dungeonIcon));
                    }
                }
                else if (places.Value && InRange(origin, location.transform.position))
                {
                    string name = PlaceName(location, site);
                    if (name != null)
                    {
                        TryPin(Category.Place, location.transform.position, name, Icon(placeIcon));
                    }
                }
            }
        }

        /// <summary>What the game shows on the way in; its discovery label; or just "Dungeon".</summary>
        private static string DungeonName(Location location, Site site)
        {
            if (site.Entrance != null && !string.IsNullOrEmpty(site.Entrance.m_enterText))
            {
                return site.Entrance.m_enterText;
            }
            return !string.IsNullOrEmpty(location.m_discoverLabel) ? location.m_discoverLabel : "$omp_dungeon";
        }

        // --- ore ------------------------------------------------------------------------------

        /// <summary>
        /// Item prefab name -> the name of what a fuelled smelter makes of it; null until the
        /// scene has prefabs. Ore is whatever a smelter that burns fuel accepts - the smelter, the
        /// blast furnace, the eitr refinery; the windmill, the kiln and the spinning wheel take no
        /// fuel and would make wood and barley "ore" - rather than a list of names that would miss
        /// the next update's metal. The pin says the metal: "Iron" for scrap iron. ExtraOre adds
        /// items under their own name (soft tissue is the refinery's fuel, not its input), SkipOre
        /// takes them out.
        /// </summary>
        private static Dictionary<string, string> oreMetals;

        /// <summary>A rock's label by its prefab name; "" when it drops nothing a smelter takes.</summary>
        private static readonly Dictionary<string, string> RockLabels = new Dictionary<string, string>();

        private static Dictionary<string, string> OreMetals()
        {
            if (oreMetals != null || ZNetScene.instance == null)
            {
                return oreMetals;
            }
            oreMetals = new Dictionary<string, string>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Smelter smelter = prefab != null ? prefab.GetComponent<Smelter>() : null;
                if (smelter == null || smelter.m_fuelItem == null)
                {
                    continue;
                }
                foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                {
                    if (conversion.m_from != null && !oreMetals.ContainsKey(conversion.m_from.gameObject.name))
                    {
                        ItemDrop product = conversion.m_to != null ? conversion.m_to : conversion.m_from;
                        oreMetals[conversion.m_from.gameObject.name] = product.m_itemData.m_shared.m_name;
                    }
                }
            }
            foreach (string name in Items(Instance.extraOre.Value))
            {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
                ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (item != null)
                {
                    oreMetals[name] = item.m_itemData.m_shared.m_name;
                }
                else
                {
                    OdinsMissingPatchPlugin.Log.LogWarning(Instance.Section + ": ExtraOre names '" + name + "', which is no item");
                }
            }
            foreach (string name in Items(Instance.skipOre.Value))
            {
                oreMetals.Remove(name);
            }
            return oreMetals;
        }

        /// <summary>ExtraOre or SkipOre changed: what counts as ore is worked out again on the next strike.</summary>
        private static void ForgetOre()
        {
            oreMetals = null;
            RockLabels.Clear();
        }

        /// <summary>
        /// The label of whatever rock was struck, or "" when it is no ore deposit. The three kinds
        /// of rock the game has are all asked: a MineRock5 (copper, silver, scrap piles, giant
        /// armour), a MineRock (the lava leviathan's flametal), and a Destructible, which either
        /// drops the ore itself (tin) or turns into one of the others on the first hit (the
        /// silver vein) - but only a Destructible nothing but a pickaxe can harm, so a Dvergr
        /// barrel holding copper scrap is not a deposit.
        /// </summary>
        private static string OreLabel(Component rock)
        {
            string key = Utils.GetPrefabName(rock.gameObject);
            if (RockLabels.TryGetValue(key, out string label))
            {
                return label;
            }
            Dictionary<string, string> metals = OreMetals();
            if (metals == null)
            {
                return "";
            }
            label = "";
            switch (rock)
            {
                case MineRock5 rock5:
                    label = DropLabel(rock5.m_dropItems, metals);
                    break;
                case MineRock mineRock:
                    label = DropLabel(mineRock.m_dropItems, metals);
                    break;
                case Destructible destructible when OnlyPickaxe(destructible):
                    DropOnDestroyed drops = destructible.GetComponent<DropOnDestroyed>();
                    label = drops != null ? DropLabel(drops.m_dropWhenDestroyed, metals) : "";
                    GameObject becomes = destructible.m_spawnWhenDestroyed;
                    if (label.Length == 0 && becomes != null)
                    {
                        MineRock5 into5 = becomes.GetComponent<MineRock5>();
                        MineRock into = becomes.GetComponent<MineRock>();
                        label = into5 != null ? DropLabel(into5.m_dropItems, metals)
                            : into != null ? DropLabel(into.m_dropItems, metals) : "";
                    }
                    break;
            }
            RockLabels[key] = label;
            return label;
        }

        private static bool OnlyPickaxe(Destructible destructible)
        {
            HitData.DamageModifiers damages = destructible.m_damages;
            return damages.m_chop == HitData.DamageModifier.Immune && damages.m_blunt == HitData.DamageModifier.Immune
                && damages.m_pickaxe != HitData.DamageModifier.Immune;
        }

        // --- mined out -----------------------------------------------------------------------

        internal enum MinedOut { Tick, Remove, Keep }

        /// <summary>
        /// How near an ore pin has to be for its deposit to be looked for: well inside the loaded
        /// area, so a rock that is not there is gone rather than not spawned yet.
        /// </summary>
        private const float MinedOutCheckRange = 32f;

        /// <summary>
        /// How far from the pin a piece of the deposit may lie. A pin sits where the rock was
        /// first struck; a copper deposit's pieces spread several metres from there.
        /// </summary>
        private const float DepositRadius = 12f;

        /// <summary>Ore pins found without a deposit; two sweeps in a row decide.</summary>
        private static readonly PinSweep DepositSweep = new PinSweep();

        /// <summary>Pins this session ticked, so one the player unticks again is left alone.</summary>
        private static readonly HashSet<Minimap.PinData> Ticked = new HashSet<Minimap.PinData>();

        private static readonly Collider[] Hits = new Collider[256];

        /// <summary>
        /// A deposit that has been mined out - by anyone, seen or not - loses its pin or gets the
        /// game's tick. Asked of the world rather than of the last hit, since the rock is
        /// destroyed on its owner's machine and merely vanishes on everyone else's: when you
        /// stand near an ore pin in a loaded area and no rock that drops ore is left around it,
        /// on two sweeps in a row, it is gone.
        /// </summary>
        private void SweepMinedOut(Minimap map, Vector3 origin)
        {
            bool remove = minedOut.Value == MinedOut.Remove;
            DepositSweep.Run(map, origin, MinedOutCheckRange,
                pin => UniversalPins.TryGetCategory(pin, out Category category) && category == Category.Ore
                    && (remove || (!pin.m_checked && !Ticked.Contains(pin))),
                DepositAt,
                pin =>
                {
                    if (remove)
                    {
                        UniversalPins.Discard(map, pin, Category.Ore);
                    }
                    else
                    {
                        pin.m_checked = true;
                        Ticked.Add(pin);
                    }
                });
        }

        /// <summary>Whether any rock that drops ore is left within DepositRadius of pos.</summary>
        private static bool DepositAt(Vector3 pos)
        {
            int count = Physics.OverlapSphereNonAlloc(pos, DepositRadius, Hits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i];
                Component rock = hit.GetComponentInParent<MineRock5>();
                if (rock == null)
                {
                    rock = hit.GetComponentInParent<MineRock>();
                }
                if (rock == null)
                {
                    rock = hit.GetComponentInParent<Destructible>();
                }
                if (rock != null && OreLabel(rock).Length > 0)
                {
                    return true;
                }
            }
            // A full buffer may have cut off the rock; better a pin that stays than one that goes.
            return count == Hits.Length;
        }

        private static string DropLabel(DropTable table, Dictionary<string, string> metals)
        {
            if (table == null)
            {
                return "";
            }
            foreach (DropTable.DropData drop in table.m_drops)
            {
                if (drop.m_item != null && metals.TryGetValue(drop.m_item.name, out string metal))
                {
                    return metal;
                }
            }
            return "";
        }

        /// <summary>
        /// Every hit on a rock goes through Damage on the client that swung, before it is sent to
        /// the rock's owner - so this sees exactly the local player's strikes and nothing else.
        /// </summary>
        private static void Strike(Component rock, ZNetView view, HitData hit)
        {
            if (!Instance.On || !Instance.ore.Value || hit == null || Player.m_localPlayer == null
                || view == null || !view.IsValid())
            {
                return;
            }
            Vector3 pos = rock.transform.position;
            // A muddy scrap pile in a crypt is not a deposit anyone needs to find again.
            if (Character.InInterior(pos) || hit.GetAttacker() != Player.m_localPlayer)
            {
                return;
            }
            string label = OreLabel(rock);
            if (label.Length > 0)
            {
                Instance.TryPin(Category.Ore, pos, label, Icon(Instance.oreIcon));
            }
        }

        // --- patches --------------------------------------------------------------------------

        private static float nextSweep;

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Sweep
        {
            private static void Postfix(Player __instance)
            {
                if (!Instance.On || __instance != Player.m_localPlayer || Time.time < nextSweep)
                {
                    return;
                }
                nextSweep = Time.time + SweepInterval;
                PinBroadcast.RequestOnce();
                Minimap map = Minimap.instance;
                Vector3 origin = __instance.transform.position;
                // Inside a dungeon everything is 5000m below; nothing to pin from up there.
                if (map == null || Character.InInterior(origin))
                {
                    return;
                }
                if (Instance.dungeons.Value || Instance.places.Value)
                {
                    Instance.SweepLocations(origin);
                }
                if (Instance.minedOut.Value != MinedOut.Keep)
                {
                    Instance.SweepMinedOut(map, origin);
                }
            }
        }

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        private static class StrikeMineRock5
        {
            private static void Postfix(MineRock5 __instance, HitData hit) => Strike(__instance, __instance.m_nview, hit);
        }

        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        private static class StrikeMineRock
        {
            private static void Postfix(MineRock __instance, HitData hit) => Strike(__instance, __instance.m_nview, hit);
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
        private static class StrikeDestructible
        {
            private static void Postfix(Destructible __instance, HitData hit) => Strike(__instance, __instance.m_nview, hit);
        }
    }
}
