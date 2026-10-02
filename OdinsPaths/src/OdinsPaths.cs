using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;

namespace OdinsPaths
{
    /// <summary>
    /// A road network grows across the world the way real ones do: paved main roads from the
    /// sacrificial stones and the bases to the boss altars in progression order and to the
    /// traders, forking off each other, and dirt spurs to the villages and crypts beside them -
    /// following the easiest ground: around hills rather than over them, along valleys, to the
    /// shore and on from the next one. It grows when the world is up and after each night slept.
    ///
    /// The mod is meant to run on the server (the host in a local game): the server knows every
    /// boss altar, every zone's terrain data and when the world sleeps, and a path written as the
    /// game's own terrain data is seen by every player, modded or not.
    ///
    /// PathLayer runs one road (PathSearch → Trail → TerrainWriter → Clearing → Landings) and
    /// its spurs; Network is what is laid, Planner what comes next, Grower when. The dev commands
    /// are in src/Dev/; ROADMAP.md is the plan.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public partial class OdinsPathsPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.odinspaths";
        public const string NAME = "Odin's Paths";
        public const string VERSION = "0.1.0";

        /// <summary>The running plugin, for coroutines.</summary>
        internal static OdinsPathsPlugin Instance;

        internal static ConfigEntry<bool> GrowNetwork;
        internal static ConfigEntry<float> Directness;
        internal static ConfigEntry<bool> Announce;
        internal static ConfigEntry<bool> ShowProgress;
        internal static ConfigEntry<float> SpurPrize;
        internal static ConfigEntry<string> PointsOfInterest;
        internal static ConfigEntry<string> Progression;
        /// <summary>How far along the progression the main roads lead.</summary>
        internal enum ProgressionReach
        {
            /// <summary>Every boss of the list from the start, the network as it ends up.</summary>
            Everything,
            /// <summary>Up to the second boss not yet defeated, one more each time a boss falls.</summary>
            Progression,
        }
        internal static ConfigEntry<ProgressionReach> Reach;
        /// <summary>
        /// Every boss in order: the Queen's location is her dungeon's entrance, and the Deep North's
        /// boss (the Frozen King) sits in DN_Bossroom - both from their vegvisirs, and every key from
        /// the bosses' m_defeatSetGlobalKey (read out of the game's bundles, 2026-09-25). Before the
        /// Queen, three of the infected mines (both Dvergr town entrances), where the sealbreaker's
        /// fragments are. With Moder, the Forge of Potential (<c>AncientUpgradeStation</c>, in the
        /// Mountains). Before Fader, two of the Ashlands' charred fortresses. After the Frozen
        /// King's altar, side roads to the Winding Tunnels (<c>TheHole01</c>), two Deep North villages
        /// - the ones beside the tunnels where there are any - and Mörkhalla (<c>MorkBorg</c>)
        /// (location prefabs and names read out of the game's bundles, 2026-09-25).
        /// </summary>
        private const string DefaultProgression =
            "Eikthyrnir:defeated_eikthyr, GDKing:defeated_gdking, Bonemass:defeated_bonemass, " +
            "AncientUpgradeStation:defeated_dragon, Dragonqueen:defeated_dragon, GoblinKing:defeated_goblinking, " +
            "Mistlands_DvergrTownEntrance1|Mistlands_DvergrTownEntrance2*3:defeated_queen, " +
            "Mistlands_DvergrBossEntrance1:defeated_queen, CharredFortress*2:defeated_fader, FaderLocation:defeated_fader, " +
            "DN_Bossroom:defeated_frozenking, ~TheHole01:defeated_frozenking, ~NorthVillage@TheHole01*2:defeated_frozenking, " +
            "~MorkBorg:defeated_frozenking";
        /// <summary>The fourth default, with a village and Mörkhalla as main roads before the altar.</summary>
        private const string FourthProgression =
            "Eikthyrnir:defeated_eikthyr, GDKing:defeated_gdking, Bonemass:defeated_bonemass, " +
            "Dragonqueen:defeated_dragon, GoblinKing:defeated_goblinking, " +
            "Mistlands_DvergrTownEntrance1|Mistlands_DvergrTownEntrance2*3:defeated_queen, " +
            "Mistlands_DvergrBossEntrance1:defeated_queen, CharredFortress*2:defeated_fader, FaderLocation:defeated_fader, " +
            "NorthVillage:defeated_frozenking, MorkBorg:defeated_frozenking, DN_Bossroom:defeated_frozenking";
        /// <summary>The third default, without the fortresses, the village and the tunnels.</summary>
        private const string ThirdProgression =
            "Eikthyrnir:defeated_eikthyr, GDKing:defeated_gdking, Bonemass:defeated_bonemass, " +
            "Dragonqueen:defeated_dragon, GoblinKing:defeated_goblinking, " +
            "Mistlands_DvergrTownEntrance1|Mistlands_DvergrTownEntrance2*3:defeated_queen, " +
            "Mistlands_DvergrBossEntrance1:defeated_queen, FaderLocation:defeated_fader, DN_Bossroom:defeated_frozenking";
        /// <summary>The second default, without the infected mines.</summary>
        private const string SecondProgression =
            "Eikthyrnir:defeated_eikthyr, GDKing:defeated_gdking, Bonemass:defeated_bonemass, " +
            "Dragonqueen:defeated_dragon, GoblinKing:defeated_goblinking, Mistlands_DvergrBossEntrance1:defeated_queen, " +
            "FaderLocation:defeated_fader, DN_Bossroom:defeated_frozenking";
        /// <summary>The first default, which stopped at Yagluth.</summary>
        private const string FirstProgression =
            "Eikthyrnir:defeated_eikthyr, GDKing:defeated_gdking, Bonemass:defeated_bonemass, " +
            "Dragonqueen:defeated_dragon, GoblinKing:defeated_goblinking";
        /// <summary>
        /// The points of interest; the Deep North's since 2026-09-25 (its huts, frozen ships, the
        /// memorial and the hot springs, prefab names read out of the game's bundles), so its side
        /// roads pick up what they pass.
        /// </summary>
        private const string DefaultPointsOfInterest =
            "WoodVillage1, WoodFarm1, Crypt2, Crypt3, Crypt4, SunkenCrypt4, TrollCave02, GoblinCamp2, " +
            "Mistlands_DvergrTownEntrance1, Mistlands_DvergrTownEntrance2, " +
            "DN_hut01, FrozenShip01_DN, FrozenShip02_DN, FrozenShip03_DN, NorthMemorialPlace, HotSpring1, HotSpring2, HotSpring3";
        private const string FirstPointsOfInterest =
            "WoodVillage1, WoodFarm1, Crypt2, Crypt3, Crypt4, SunkenCrypt4, TrollCave02, GoblinCamp2, " +
            "Mistlands_DvergrTownEntrance1, Mistlands_DvergrTownEntrance2";
        /// <summary>Which instance of a boss or trader a road goes to, in one setting.</summary>
        internal enum PlayStyle
        {
            /// <summary>Fader and the Frozen King in the middle of their biomes, Hildir and the Bog Witch far from the roads.</summary>
            Explore,
            /// <summary>Every road to the instance easiest to reach from the network.</summary>
            Fastest,
            /// <summary>The Central and Remote lists.</summary>
            Custom,
        }
        internal static ConfigEntry<PlayStyle> Style;
        internal static ConfigEntry<string> Central;
        internal static ConfigEntry<string> Remote;
        internal static ConfigEntry<string> Traders;
        internal static ConfigEntry<bool> RevealTraders;
        internal static ConfigEntry<string> CustomLocations;
        internal static ConfigEntry<string> BaseMarker;
        internal static ConfigEntry<float> BaseRadius;
        internal static ConfigEntry<int> BaseMinPieces;
        internal static ConfigEntry<float> SeaCost;
        internal static ConfigEntry<float> BoardingCost;
        internal static ConfigEntry<float> LandingCost;
        internal static ConfigEntry<float> SwampCost;
        internal static ConfigEntry<float> SearchBudgetMs;
        internal static ConfigEntry<bool> AdaptivePasses;
        internal static ConfigEntry<bool> SearchThread;
        internal static ConfigEntry<float> CoarseCell;
        internal static ConfigEntry<float> MidCell;
        internal static ConfigEntry<float> MidCorridor;
        internal static ConfigEntry<float> CorridorWidth;

        void Awake()
        {
            Instance = this;
            RoadKind.Bind(Config);
            RoadKind.UpgradeDefaults();
            Docks.Bind(Config);
            Signposts.Bind(Config);
            GrowNetwork = Config.Bind("Network", "Grow", true,
                "Grow the road network: when the world is up, and after every night slept through. Off, " +
                "the roads already laid stay and no new ones come.");
            Announce = Config.Bind("Network", "Announce", true,
                "Tell every player in the message log when roads are being laid, as each one is done, and " +
                "when all are: the game may stutter meanwhile. Players without the mod see it too.");
            ShowProgress = Config.Bind("Network", "ProgressBar", true,
                "Show a progress bar at the top of the screen while roads are being laid - to the player " +
                "hosting the game, since the server does the work.");
            Directness = Config.Bind("Network", "Directness", 0.4f, new ConfigDescription(
                "How much a new main road minds the walk from home when it branches off an old one " +
                "(alpha in the docs). 0: it forks off wherever the network is nearest, however long the way " +
                "round from the start then gets. 1: every place gets a road of its own from the start, and roads " +
                "only share where they would run side by side anyway. In between, roads share their trunks and fork.",
                new AcceptableValueRange<float>(0f, 1f)));
            Progression = Config.Bind("Network", "Progression", DefaultProgression,
                "The boss altars main roads lead to, in order, each as location:key - the location's prefab " +
                "name and the global key its boss's defeat sets. Reach decides how far along it the roads lead. " +
                "Remove an entry to leave its boss without a road. " +
                "Locations joined by | count as one (any of them will do), and *n asks for n roads, each to " +
                "another one: the infected mines before the Queen, where her seal's fragments are, and the charred " +
                "fortresses before Fader. A leading ~ makes a side road: dirt, from wherever the roads are nearest " +
                "(the Deep North's tunnels, villages and Mörkhalla). @ after the name prefers the ones with another " +
                "location within 400 m: ~NorthVillage@TheHole01*2 is two villages beside the Winding Tunnels, where there are.");
            // A config written before the later bosses, the mines, the fortresses or the Deep North's side roads were in the list gets them.
            if (Progression.Value == FirstProgression || Progression.Value == SecondProgression || Progression.Value == ThirdProgression
                || Progression.Value == FourthProgression)
            {
                Progression.Value = DefaultProgression;
            }
            Reach = Config.Bind("Network", "Reach", ProgressionReach.Everything,
                "How far along the progression the main roads lead. Everything: to every boss of the list from " +
                "the start - the first growth of a new world then takes a few minutes. Progression: up to the " +
                "second boss not yet defeated, and one more on the next sleep after a boss falls.");
            Style = Config.Bind("Network", "Style", PlayStyle.Explore,
                "Which of a boss's altars or a trader's camps a road goes to. Explore: Fader's and the Frozen " +
                "King's in the middle of their biome, so the road crosses it, and Hildir's and the Bog Witch's " +
                "farthest from the roads, laid after everything else, so they open up another part of the world. " +
                "Fastest: always the one easiest to reach. Custom: the Central and Remote lists below.");
            Central = Config.Bind("Network", "Central", "FaderLocation, DN_Bossroom",
                "With Style Custom: locations whose road goes to the instance in the middle of the others rather " +
                "than the one easiest to reach - the easiest is often the one nearest a coast, reached by boat, " +
                "and the road then shows nothing of the biome.");
            Remote = Config.Bind("Network", "Remote", "Hildir_camp, BogWitch_Camp",
                "With Style Custom: traders whose road goes to the camp farthest from every road and base, " +
                "and is laid after every other road due.");
            Traders = Config.Bind("Network", "Traders", "Vendor_BlackForest, Hildir_camp, BogWitch_Camp",
                "Trader locations main roads lead to. The road goes to one of the trader's possible camps before any is " +
                "generated, and the trader settles in that one.");
            RevealTraders = Config.Bind("Network", "RevealTraders", true,
                "Lay main roads to the traders and show their camps on the map as soon as the road is there. " +
                "Off: no road leads to a trader, and players search for them as in the unmodded game.");
            CustomLocations = Config.Bind("Network", "CustomLocations", "",
                "More locations for main roads, by prefab name, comma separated - another mod's too. Each " +
                "gets one road, to its instance cheapest to reach.");
            BaseMarker = Config.Bind("Network", "BaseMarker", "guard_stone",
                "The piece that marks a base, by prefab name: every base gets a main road to the network, " +
                "and new roads may start from it.");
            BaseRadius = Config.Bind("Network", "BaseRadius", 150f, new ConfigDescription(
                "Base markers this close to each other, in metres, are one base.",
                new AcceptableValueRange<float>(20f, 1000f)));
            BaseMinPieces = Config.Bind("Network", "BaseMinPieces", 20, new ConfigDescription(
                "How many built pieces a base needs within 30 m of its marker, so that a lone ward at an " +
                "outpost does not get a road. 0 counts every marker.",
                new AcceptableValueRange<int>(0, 1000)));
            SpurPrize = Config.Bind("Spurs", "Prize", 400f, new ConfigDescription(
                "How far off a main road a point of interest may lie and still get a spur, in metres of easy " +
                "walking: climbs, fords and bogs on the way count extra. 0 lays no spurs.",
                new AcceptableValueRange<float>(0f, 1500f)));
            PointsOfInterest = Config.Bind("Spurs", "PointsOfInterest", DefaultPointsOfInterest,
                "The locations that get a spur when a main road passes near, by prefab name, comma separated - " +
                "another mod's too.");
            if (PointsOfInterest.Value == FirstPointsOfInterest)
            {
                PointsOfInterest.Value = DefaultPointsOfInterest;
            }
            SeaCost = Config.Bind("Route", "SeaCost", 2f, new ConfigDescription(
                "How much dearer a metre of sailing is than a metre of easy walking. Paths stay on land " +
                "wherever it leads and cross water where it is narrowest; lower makes them take to the " +
                "water sooner, 1 makes the sea cheaper than any ground.",
                new AcceptableValueRange<float>(1f, 10f)));
            BoardingCost = Config.Bind("Route", "BoardingCost", 1000f, new ConfigDescription(
                "What setting out on the sea costs, in metres of easy walking: a boat has to be built. " +
                "Together with LandingCost it is what a crossing pays on top of its length. Lower makes " +
                "paths hop between islands and across fjords; higher makes them walk around. Rivers are " +
                "swum and pay nothing.",
                new AcceptableValueRange<float>(0f, 3000f)));
            LandingCost = Config.Bind("Route", "LandingCost", 400f, new ConfigDescription(
                "What coming ashore costs, in metres of easy walking. Cheaper than boarding, since " +
                "stopping is easy; it is what makes a stop on an island on the way worth less than " +
                "the boat it needs again afterwards.",
                new AcceptableValueRange<float>(0f, 3000f)));
            SwampCost = Config.Bind("Route", "SwampCost", 3f, new ConfigDescription(
                "How much dearer a metre through the Swamp is than a metre of easy walking. The swamp is " +
                "flat, so without this it is the cheapest ground there is; paths go around it where " +
                "a way around exists.",
                new AcceptableValueRange<float>(1f, 10f)));
            SearchBudgetMs = Config.Bind("Performance", "SearchBudgetMs", 6f, new ConfigDescription(
                "How many milliseconds per frame laying a road may take - the search, the terrain writing " +
                "and the clearing. More lays roads sooner and costs frame rate while they are laid.",
                new AcceptableValueRange<float>(1f, 50f)));
            AdaptivePasses = Config.Bind("Performance", "AdaptivePasses", true,
                "Choose the searches by how far a road has to go: up to 600 m one fine search, up to 1.5 km a " +
                "16 m search first, up to 4 km 32 m, farther 64 m and then 16 m, each followed by the fine search " +
                "in a corridor. Off, CoarseCell and MidCell below are used for every road.");
            SearchThread = Config.Bind("Performance", "SearchThread", true,
                "Search for roads on a thread of their own, so the game does not stutter while it thinks. Off, " +
                "the search takes turns with the frames, within SearchBudgetMs each.");
            CoarseCell = Config.Bind("Performance", "CoarseCell", 32f, new ConfigDescription(
                "A first, rough search on a grid this many metres wide finds the general line of a " +
                "path, and the fine search then runs only in a corridor around it. 32 sees rivers and " +
                "shores well; 64 is several times faster and wants the middle search below. 0 skips the " +
                "rough search, which finds a slightly better path and takes many times longer for a far target.",
                new AcceptableValueRange<float>(0f, 128f)));
            MidCell = Config.Bind("Performance", "MidCell", 0f, new ConfigDescription(
                "An optional middle search on a grid this many metres wide, in a corridor around the " +
                "rough line, before the fine search: with CoarseCell at 64, a middle search at 16 " +
                "catches the rivers and shores the rough line stepped over. 0 skips it.",
                new AcceptableValueRange<float>(0f, 64f)));
            MidCorridor = Config.Bind("Performance", "MidCorridor", 256f, new ConfigDescription(
                "How wide the corridor around the rough line is for the middle search, in metres.",
                new AcceptableValueRange<float>(64f, 1000f)));
            CorridorWidth = Config.Bind("Performance", "CorridorWidth", 96f, new ConfigDescription(
                "How wide the corridor around the line before it is for the fine search, in metres. " +
                "Wider lets the fine search find fords and switchbacks the rougher one missed, and costs time.",
                new AcceptableValueRange<float>(32f, 400f)));
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), GUID);
        }
    }
}
