using System;
using System.Collections.Generic;

namespace OdinsMissingPatch
{
    // The definition behind every stack of one item - what the crafting tree is drawn between.
    using Kind = ItemDrop.ItemData.SharedData;

    /// <summary>
    /// Where a material sits in the game's own crafting tree, derived from <see cref="ObjectDB"/>
    /// when it is first needed rather than from a list of item names: which family it belongs to
    /// and how deep into the tree it lies. Ordering materials by family and then by depth is what
    /// puts the metals in one block in tier order and the logs in another, without the mod ever
    /// knowing what "copper" is.
    ///
    /// The tree is every enabled <see cref="Recipe"/>, every buildable <see cref="Piece"/> and
    /// every conversion a piece runs - <see cref="Smelter"/> (the smelter, the kiln, the windmill,
    /// the spinning wheel, the eitr refinery), <see cref="CookingStation"/> and
    /// <see cref="Fermenter"/>. Depth is a fixpoint over it: a piece is one deeper than the deepest
    /// thing it is built from and than the station it is built at; a crafted or converted item one
    /// deeper than its ingredients and than its station, counting the levels above the first that
    /// the recipe asks for. A material's own depth is that of the shallowest thing made from it,
    /// falling back to the depth it is made at when nothing uses it. Reading it off the *use* is
    /// what tells silver from copper: both come out of the same smelter, so being made says
    /// nothing, but the first thing silver goes into wants a level 3 forge.
    ///
    /// A family is the transitive closure of "made from" over materials: an ore, the bar it smelts
    /// into and the alloy that bar goes into are one, as are every log and the coal they burn down
    /// to. Families are ordered by their shallowest member and then by its name - so the family
    /// met first comes first, and families the tree cannot tell apart (every ore is "once you have
    /// a smelter", which is all the recipes know) stay alphabetical.
    /// </summary>
    internal static class MaterialOrder
    {
        /// <summary>How often depths are grown before a cycle in someone's data is left saturated.</summary>
        private const int MaxPasses = 32;

        private static ObjectDB source;
        private static int sourceRecipes;
        private static int sourceItems;
        private static Dictionary<Kind, Place> places;

        /// <summary>A material's place in the tree.</summary>
        private struct Place
        {
            internal int Family;
            internal int Depth;
        }

        /// <summary>One thing the tree makes: what comes out, what goes in, and at which station.</summary>
        private sealed class Step
        {
            internal Kind Result;
            internal readonly List<Kind> Inputs = new List<Kind>();
            internal int Station = -1;
            internal int Level;
        }

        /// <summary>One family, while it is being measured.</summary>
        private sealed class Group
        {
            internal readonly List<Kind> Members = new List<Kind>();
            internal int Low = int.MaxValue;
            internal string Name;

            internal void Note(Kind kind, int depth)
            {
                Members.Add(kind);
                string name = Label(kind);
                if (depth < Low || (depth == Low && string.Compare(name, Name, StringComparison.CurrentCultureIgnoreCase) < 0))
                {
                    Low = depth;
                    Name = name;
                }
            }
        }

        /// <summary>
        /// Derives the tree if it has not been derived for this <see cref="ObjectDB"/> yet. Callers
        /// do this once before a sort rather than per comparison; without it, and before the game
        /// has an ObjectDB, <see cref="Compare"/> simply has nothing to say.
        /// </summary>
        internal static void Prepare()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                return;
            }
            if (db == source && db.m_recipes.Count == sourceRecipes && db.m_items.Count == sourceItems)
            {
                return;
            }
            source = db;
            sourceRecipes = db.m_recipes.Count;
            sourceItems = db.m_items.Count;
            places = Build(db);
        }

        /// <summary>
        /// Orders two materials by family and then by depth. 0 when they share a place, and 0 for
        /// anything the tree never mentions, which leaves the caller's own keys to decide.
        /// </summary>
        internal static int Compare(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            Place first = Of(a);
            Place second = Of(b);
            int order = first.Family.CompareTo(second.Family);
            return order != 0 ? order : first.Depth.CompareTo(second.Depth);
        }

        /// <summary>Anything unknown sits behind every family, at depth 0, so such items stay together.</summary>
        private static Place Of(ItemDrop.ItemData item)
        {
            if (places != null && item != null && item.m_shared != null
                && places.TryGetValue(item.m_shared, out Place found))
            {
                return found;
            }
            return new Place { Family = int.MaxValue, Depth = 0 };
        }

        private static Dictionary<Kind, Place> Build(ObjectDB db)
        {
            List<Piece> pieces = db.GetAllBuildPieces(true);
            Dictionary<CraftingStation, int> stations = Stations(pieces);
            List<Step> steps = Steps(db, pieces, stations);

            int[] pieceDepth = new int[pieces.Count];
            Dictionary<Kind, int> made = new Dictionary<Kind, int>();
            Settle(pieces, stations, Producers(steps), pieceDepth, made);
            Dictionary<Kind, int> depth = Depths(made, Uses(steps, pieces, made, pieceDepth));

            Dictionary<Kind, Kind> parents = Families(steps);
            Dictionary<Kind, Group> groups = new Dictionary<Kind, Group>();
            foreach (KeyValuePair<Kind, int> entry in depth)
            {
                Kind root = Find(parents, entry.Key);
                if (!groups.TryGetValue(root, out Group group))
                {
                    group = new Group();
                    groups[root] = group;
                }
                group.Note(entry.Key, entry.Value);
            }

            List<Group> ordered = new List<Group>(groups.Values);
            ordered.Sort(CompareGroups);
            Dictionary<Kind, Place> built = new Dictionary<Kind, Place>();
            for (int i = 0; i < ordered.Count; i++)
            {
                foreach (Kind member in ordered[i].Members)
                {
                    built[member] = new Place { Family = i, Depth = depth[member] };
                }
            }
            return built;
        }

        /// <summary>The piece each crafting station stands on, so a recipe can be told how far in its station is.</summary>
        private static Dictionary<CraftingStation, int> Stations(List<Piece> pieces)
        {
            Dictionary<CraftingStation, int> stations = new Dictionary<CraftingStation, int>();
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }
                CraftingStation station = piece.GetComponent<CraftingStation>();
                if (station != null && !stations.ContainsKey(station))
                {
                    stations[station] = i;
                }
            }
            return stations;
        }

        /// <summary>Every recipe plus every conversion a buildable piece runs.</summary>
        private static List<Step> Steps(ObjectDB db, List<Piece> pieces, Dictionary<CraftingStation, int> stations)
        {
            List<Step> steps = new List<Step>();
            foreach (Recipe recipe in db.m_recipes)
            {
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null)
                {
                    continue;
                }
                Step step = new Step
                {
                    Result = recipe.m_item.m_itemData.m_shared,
                    Station = StationPiece(stations, recipe.m_craftingStation),
                    Level = Math.Max(0, recipe.m_minStationLevel - 1),
                };
                foreach (Piece.Requirement requirement in recipe.m_resources)
                {
                    Feed(step.Inputs, requirement == null ? null : requirement.m_resItem);
                }
                Keep(steps, step);
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }
                Smelter smelter = piece.GetComponent<Smelter>();
                if (smelter != null)
                {
                    foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                    {
                        if (conversion != null)
                        {
                            Converted(steps, i, conversion.m_from, conversion.m_to);
                        }
                    }
                }
                CookingStation cooking = piece.GetComponent<CookingStation>();
                if (cooking != null)
                {
                    foreach (CookingStation.ItemConversion conversion in cooking.m_conversion)
                    {
                        if (conversion != null)
                        {
                            Converted(steps, i, conversion.m_from, conversion.m_to);
                        }
                    }
                }
                Fermenter fermenter = piece.GetComponent<Fermenter>();
                if (fermenter != null)
                {
                    foreach (Fermenter.ItemConversion conversion in fermenter.m_conversion)
                    {
                        if (conversion != null)
                        {
                            Converted(steps, i, conversion.m_from, conversion.m_to);
                        }
                    }
                }
            }
            return steps;
        }

        private static void Converted(List<Step> steps, int station, ItemDrop from, ItemDrop to)
        {
            if (to == null)
            {
                return;
            }
            Step step = new Step { Result = to.m_itemData.m_shared, Station = station };
            Feed(step.Inputs, from);
            Keep(steps, step);
        }

        private static void Feed(List<Kind> inputs, ItemDrop drop)
        {
            Kind kind = drop == null ? null : drop.m_itemData.m_shared;
            if (kind != null && !inputs.Contains(kind))
            {
                inputs.Add(kind);
            }
        }

        private static void Keep(List<Step> steps, Step step)
        {
            if (step.Result != null)
            {
                steps.Add(step);
            }
        }

        private static Dictionary<Kind, List<Step>> Producers(List<Step> steps)
        {
            Dictionary<Kind, List<Step>> producers = new Dictionary<Kind, List<Step>>();
            foreach (Step step in steps)
            {
                if (!producers.TryGetValue(step.Result, out List<Step> list))
                {
                    list = new List<Step>();
                    producers[step.Result] = list;
                }
                list.Add(step);
            }
            return producers;
        }

        /// <summary>
        /// Grows every depth until nothing moves: a piece out of what builds it, an item out of the
        /// shallowest way there is to make it. Depths only ever rise, so this settles; the pass cap
        /// is there for data that is circular (a station built from what it makes), which saturates
        /// instead of running away.
        /// </summary>
        private static void Settle(List<Piece> pieces, Dictionary<CraftingStation, int> stations,
            Dictionary<Kind, List<Step>> producers, int[] pieceDepth, Dictionary<Kind, int> made)
        {
            bool moved = true;
            for (int pass = 0; pass < MaxPasses && moved; pass++)
            {
                moved = false;
                for (int i = 0; i < pieces.Count; i++)
                {
                    Piece piece = pieces[i];
                    if (piece == null)
                    {
                        continue;
                    }
                    int deepest = StationDepth(stations, pieceDepth, piece.m_craftingStation);
                    foreach (Piece.Requirement requirement in piece.m_resources)
                    {
                        if (requirement != null && requirement.m_resItem != null)
                        {
                            deepest = Math.Max(deepest, Depth(made, requirement.m_resItem.m_itemData.m_shared));
                        }
                    }
                    if (deepest + 1 > pieceDepth[i])
                    {
                        pieceDepth[i] = deepest + 1;
                        moved = true;
                    }
                }
                foreach (KeyValuePair<Kind, List<Step>> producer in producers)
                {
                    int shallowest = int.MaxValue;
                    foreach (Step step in producer.Value)
                    {
                        shallowest = Math.Min(shallowest, StepDepth(step, made, pieceDepth));
                    }
                    if (shallowest != int.MaxValue && shallowest > Depth(made, producer.Key))
                    {
                        made[producer.Key] = shallowest;
                        moved = true;
                    }
                }
            }
        }

        /// <summary>The shallowest thing each material goes into, the pieces it builds included.</summary>
        private static Dictionary<Kind, int> Uses(List<Step> steps, List<Piece> pieces,
            Dictionary<Kind, int> made, int[] pieceDepth)
        {
            Dictionary<Kind, int> used = new Dictionary<Kind, int>();
            foreach (Step step in steps)
            {
                int depth = StepDepth(step, made, pieceDepth);
                foreach (Kind input in step.Inputs)
                {
                    Lower(used, input, depth);
                }
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }
                foreach (Piece.Requirement requirement in piece.m_resources)
                {
                    if (requirement != null && requirement.m_resItem != null)
                    {
                        Lower(used, requirement.m_resItem.m_itemData.m_shared, pieceDepth[i]);
                    }
                }
            }
            return used;
        }

        /// <summary>Where each material ends up: what it is used for, or, failing that, how it is made.</summary>
        private static Dictionary<Kind, int> Depths(Dictionary<Kind, int> made, Dictionary<Kind, int> used)
        {
            Dictionary<Kind, int> depth = new Dictionary<Kind, int>();
            foreach (KeyValuePair<Kind, int> entry in used)
            {
                if (Material(entry.Key))
                {
                    depth[entry.Key] = entry.Value;
                }
            }
            foreach (KeyValuePair<Kind, int> entry in made)
            {
                if (Material(entry.Key) && !depth.ContainsKey(entry.Key))
                {
                    depth[entry.Key] = entry.Value;
                }
            }
            return depth;
        }

        /// <summary>Ties every material to the materials it is made from, one set per family.</summary>
        private static Dictionary<Kind, Kind> Families(List<Step> steps)
        {
            Dictionary<Kind, Kind> parents = new Dictionary<Kind, Kind>();
            foreach (Step step in steps)
            {
                if (!Material(step.Result))
                {
                    continue;
                }
                foreach (Kind input in step.Inputs)
                {
                    if (Material(input))
                    {
                        Union(parents, step.Result, input);
                    }
                }
            }
            return parents;
        }

        private static Kind Find(Dictionary<Kind, Kind> parents, Kind kind)
        {
            if (!parents.TryGetValue(kind, out Kind up))
            {
                parents[kind] = kind;
                return kind;
            }
            if (up == kind)
            {
                return kind;
            }
            Kind root = Find(parents, up);
            parents[kind] = root;
            return root;
        }

        private static void Union(Dictionary<Kind, Kind> parents, Kind a, Kind b)
        {
            Kind first = Find(parents, a);
            Kind second = Find(parents, b);
            if (first != second)
            {
                parents[second] = first;
            }
        }

        private static int CompareGroups(Group a, Group b)
        {
            int order = a.Low.CompareTo(b.Low);
            return order != 0 ? order : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        private static int StepDepth(Step step, Dictionary<Kind, int> made, int[] pieceDepth)
        {
            int deepest = step.Station >= 0 ? pieceDepth[step.Station] + step.Level : step.Level;
            foreach (Kind input in step.Inputs)
            {
                deepest = Math.Max(deepest, Depth(made, input));
            }
            return deepest + 1;
        }

        private static int StationPiece(Dictionary<CraftingStation, int> stations, CraftingStation station)
        {
            return station != null && stations.TryGetValue(station, out int index) ? index : -1;
        }

        private static int StationDepth(Dictionary<CraftingStation, int> stations, int[] pieceDepth, CraftingStation station)
        {
            int index = StationPiece(stations, station);
            return index >= 0 ? pieceDepth[index] : 0;
        }

        private static int Depth(Dictionary<Kind, int> depths, Kind kind)
        {
            return kind != null && depths.TryGetValue(kind, out int depth) ? depth : 0;
        }

        private static void Lower(Dictionary<Kind, int> depths, Kind kind, int depth)
        {
            if (kind != null && (!depths.TryGetValue(kind, out int known) || depth < known))
            {
                depths[kind] = depth;
            }
        }

        private static bool Material(Kind kind)
        {
            return kind != null && kind.m_itemType == ItemDrop.ItemData.ItemType.Material;
        }

        /// <summary>The name as the player reads it, so families that tie fall in the order they read in.</summary>
        private static string Label(Kind kind)
        {
            Localization localization = Localization.instance;
            return localization != null ? localization.Localize(kind.m_name) : kind.m_name;
        }
    }
}
