using BepInEx.Configuration;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// A dock at each new harbour, carrying the road on out to sea: its land end on the road's
    /// middle, where the road is as high as the deck, so the road runs onto the planks without a
    /// step, and its deck as high as the road 1.5 m inland of the shore (<see cref="Inland"/>) -
    /// the shore point itself is often the foot of a steep bank the road comes down. Kept between
    /// <see cref="DeckAboveMin"/> and <see cref="DeckAboveMax"/> above the water. The dock is a
    /// blueprint of the land's biome (<see cref="Blueprints"/>), raised and weathered by
    /// <see cref="Builder"/>; one that would stand in water deeper than <see cref="MaxDepth"/>,
    /// on something built, or never reach the water is passed over for another. Server only.
    /// </summary>
    internal static class Docks
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> ChestChance;
        internal static ConfigEntry<float> EnemyChance;
        internal static ConfigEntry<int> BuildingCount;

        private const float DeckAboveMin = 0.8f;
        private const float DeckAboveMax = 3f;
        /// <summary>How far inland of the shore point the road's height sets the deck's.</summary>
        private const float Inland = 1.5f;
        /// <summary>How far inland the land end may move to where the road is as high as the deck.</summary>
        private const float LandEndSearch = 8f;
        /// <summary>Water deeper under the deck than this rules a blueprint out: the piles would be too long.</summary>
        private const float MaxDepth = 12f;
        /// <summary>A structure closer than this to a piece (a player's dock, a ruin) rules a blueprint out.</summary>
        private const float Taken = 1.5f;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Harbours", "Docks", true,
                "Build a small, weathered dock at every new harbour, of the biome's own materials, and the buildings " +
                "beside its road. Players need the mod to see the furniture; the rest is the game's own building pieces.");
            ChestChance = config.Bind("Harbours", "ChestChance", 0.35f, new ConfigDescription(
                "How likely a dock or a harbour building holds a treasure chest with the loot of its biome.",
                new AcceptableValueRange<float>(0f, 1f)));
            EnemyChance = config.Bind("Harbours", "EnemyChance", 0.2f, new ConfigDescription(
                "How likely a dock or a harbour building is haunted by one to three enemies of its biome, which rise when a " +
                "player comes near and do not come back.",
                new AcceptableValueRange<float>(0f, 1f)));
            BuildingCount = config.Bind("Harbours", "Buildings", 2, new ConfigDescription(
                "How many old buildings - huts, sheds, storehouses - stand beside the road at a new harbour, at most: " +
                "fewer where the ground beside the road is too steep, wet or built on.",
                new AcceptableValueRange<int>(0, 6)));
        }

        /// <summary>What to build; the dev command sets more of it than the harbours do.</summary>
        internal sealed class Request
        {
            /// <summary>Where the road runs onto the dock: the middle of its land end.</summary>
            public Vector2 LandEnd;
            /// <summary>Out to sea.</summary>
            public Vector2 Forward;
            /// <summary>The deck's top at the land end: the road's height there.</summary>
            public float Deck;
            public int Seed;
            /// <summary>What is built already, kept clear of; null checks nothing.</summary>
            public Structures Structures;
#pragma warning disable CS0649 // set only by the dev commands, which Release builds leave out
            /// <summary>Null: any dock blueprint of the land's biome that fits.</summary>
            public string Blueprint;
            /// <summary>0 a ruin, 1 as good as new; NaN: at random.</summary>
            public float Condition = float.NaN;
            /// <summary>NaN: the settings'.</summary>
            public float ChestChance = float.NaN;
            public float EnemyChance = float.NaN;
            /// <summary>Build even with docks off in the settings.</summary>
            public bool Force;
            /// <summary>Exactly here, fitted to nothing, to rework in game (<see cref="Builder.Options.Edit"/>).</summary>
            public bool Edit;
            public long Creator;
#pragma warning restore CS0649
        }

        /// <summary>A harbour's dock: where it goes along the trail, what was built, and how far inland its land end is.</summary>
        internal sealed class Harbour
        {
            public Builder.Result Built;
            /// <summary>Metres inland of the shore point, along the road, the dock begins.</summary>
            public float LandEnd;
        }

        /// <summary>A dock for a new harbour at a landing of the trail, if one fits.</summary>
        public static Harbour ForHarbour(Trail trail, Landings.Landing landing, Structures structures, int seed)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            Along(trail, landing, Inland, out float road, out Vector2 _);
            float deck = DeckHeight(road);
            // The land end: where the road, coming down to the shore, is as high as the deck.
            float landEnd = 0f;
            float best = float.MinValue;
            for (float d = 0f; d <= LandEndSearch; d += 0.25f)
            {
                Along(trail, landing, d, out float height, out Vector2 _);
                if (height >= deck - 0.05f)
                {
                    landEnd = d;
                    break;
                }
                if (height > best)
                {
                    best = height;
                    landEnd = d;
                }
            }
            Vector2 at = Along(trail, landing, landEnd, out float _, out Vector2 _);
            Vector2 back = Along(trail, landing, landEnd + 4f, out float _, out Vector2 _);
            Harbour harbour = new Harbour
            {
                LandEnd = landEnd,
                Built = Build(new Request
                {
                    LandEnd = at,
                    Forward = SeaPoint(trail, landing) - back,
                    Deck = deck,
                    Seed = seed,
                    Structures = structures,
                }),
            };
            if (harbour.Built.Reason == null || Enabled.Value)
            {
                Debug.Log("[OdinsPaths] Harbour at " + trail.Points[landing.Shore].ToString("F0") + ": dock " + harbour.Built
                    + " (deck " + (deck - water).ToString("F1") + " m above the water, " + landEnd.ToString("F1") + " m inland)");
            }
            return harbour;
        }

        /// <summary>A deck as high as the road, within its bounds above the water.</summary>
        public static float DeckHeight(float road)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            return Mathf.Clamp(road, water + DeckAboveMin, water + DeckAboveMax);
        }

        public static Builder.Result Build(Request request)
        {
            Builder.Result result = new Builder.Result();
            if (!request.Force && !Enabled.Value)
            {
                result.Reason = "docks are off in the settings";
                return result;
            }
            if (request.Forward == Vector2.zero || ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                result.Reason = "no direction to the sea";
                return result;
            }
            System.Random rng = new System.Random(request.Seed);
            Heightmap.Biome biome = Builder.LandBiome(request.LandEnd, request.Forward);
            List<Blueprint> order;
            if (request.Blueprint != null)
            {
                Blueprint named = Blueprints.Named(request.Blueprint);
                order = named != null ? new List<Blueprint> { named } : new List<Blueprint>();
            }
            else
            {
                order = Blueprints.Shuffled(dock: true, biome, rng);
            }
            if (order.Count == 0)
            {
                result.Reason = request.Blueprint != null ? "no blueprint " + request.Blueprint : "no dock blueprints";
                return result;
            }
            Builder.Frame frame = Builder.Frame.Make(request.LandEnd, request.Forward, request.Deck);
            string why = null;
            foreach (Blueprint blueprint in order)
            {
                List<Builder.Part> parts = Builder.Resolve(blueprint, request.Edit);
                if (request.Edit || Fit(blueprint, parts, frame, request.Structures, out why))
                {
                    Builder.Raise(blueprint, parts, frame, new Builder.Options
                    {
                        Biome = biome,
                        Dock = true,
                        Condition = request.Condition,
                        ChestChance = float.IsNaN(request.ChestChance) ? ChestChance.Value : request.ChestChance,
                        EnemyChance = float.IsNaN(request.EnemyChance) ? EnemyChance.Value : request.EnemyChance,
                        Edit = request.Edit,
                        Creator = request.Creator,
                    }, rng, result);
                    if (request.Structures != null)
                    {
                        foreach (Vector2 point in result.Footprint)
                        {
                            request.Structures.Add(point);
                        }
                    }
                    return result;
                }
            }
            result.Reason = why;
            return result;
        }

        /// <summary>Whether a dock blueprint stands here: over water no deeper than MaxDepth, reaching it, clear of structures, not mostly under ground.</summary>
        private static bool Fit(Blueprint blueprint, List<Builder.Part> parts, Builder.Frame frame, Structures structures, out string why)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            bool wet = false;
            int decks = 0;
            int buried = 0;
            foreach (Builder.Part part in parts)
            {
                Vector2 at = frame.Flat(part.Centre.x, part.Centre.y);
                if (part.Stands)
                {
                    decks++;
                    float ground = Ground.Height(at.x, at.y);
                    if (Builder.IsBuried(part, frame, dock: true))
                    {
                        buried++;
                        continue;
                    }
                    if (frame.Floor + part.Max.y - ground > MaxDepth)
                    {
                        why = blueprint.name + ": the water gets deeper than " + MaxDepth + " m under it";
                        return false;
                    }
                    wet |= ground < water - 0.3f;
                }
                if (structures != null && part.Role != Role.Roof && part.Role != Role.Lamp && structures.Distance(at, Taken) < Taken)
                {
                    why = blueprint.name + ": something is built where it would stand";
                    return false;
                }
            }
            if (!wet)
            {
                why = blueprint.name + ": it would not reach the water";
                return false;
            }
            if (buried * 3 > decks)
            {
                why = blueprint.name + ": the shore rises under a third of it";
                return false;
            }
            why = null;
            return true;
        }

        /// <summary>
        /// The point d metres inland of a landing's shore point, along the trail (never past its
        /// end or into water again), with the road's height there as the terrain writer levels it,
        /// and the direction out to sea along the road there.
        /// </summary>
        internal static Vector2 Along(Trail trail, Landings.Landing landing, float d, out float height, out Vector2 seaward)
        {
            int step = landing.Shore < landing.Toward ? -1 : 1;
            int count = trail.Points.Count;
            bool Dry(int k) => k >= 0 && k < count && !trail.Water[k];
            float steps = Mathf.Max(0f, d) / Trail.Spacing;
            int n = Mathf.FloorToInt(steps);
            float t = steps - n;
            int i = landing.Shore;
            for (int s = 0; s < n; s++)
            {
                if (!Dry(i + step))
                {
                    t = 0f;
                    break;
                }
                i += step;
            }
            int j = Dry(i + step) ? i + step : i;
            height = Mathf.Lerp(Landings.Height(trail, i), Landings.Height(trail, j), t);
            int outer = Mathf.Clamp(i - step, 0, count - 1);
            int inner = Dry(i + step) ? i + step : i;
            seaward = (trail.Points[outer] - trail.Points[inner]).normalized;
            return Vector2.Lerp(trail.Points[i], trail.Points[j], t);
        }

        /// <summary>A few points into the water from the shore: a steadier heading than the first step's.</summary>
        private static Vector2 SeaPoint(Trail trail, Landings.Landing landing)
        {
            int step = landing.Shore < landing.Toward ? -1 : 1;
            int sea = landing.Toward;
            for (int i = 0; i < 5 && sea - step >= 0 && sea - step < trail.Points.Count && trail.Water[sea - step]; i++)
            {
                sea -= step;
            }
            return trail.Points[sea];
        }

        /// <summary>For the dev command's list: every blueprint, its kind, biomes and size.</summary>
        internal static string Describe()
        {
            StringBuilder text = new StringBuilder();
            foreach (Blueprint blueprint in Blueprints.All)
            {
                text.Append(blueprint.name).Append(" (").Append(blueprint.IsDock ? "dock" : "building").Append(", ")
                    .Append(blueprint.Biomes == Heightmap.Biome.None ? "any biome" : blueprint.Biomes.ToString()).Append(", ")
                    .Append(blueprint.pieces.Count).Append(" pieces, ").Append(blueprint.spots.Count).Append(" spots)\n");
            }
            text.Append("Your own: ").Append(Blueprints.UserFolder);
            return text.ToString();
        }
    }
}
