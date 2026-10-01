using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The old buildings of a new harbour - huts, sheds, storehouses: building blueprints of the
    /// land's biome (<see cref="Blueprints"/>), a different one each while there are, raised and
    /// weathered by <see cref="Builder"/>. Placed as Minecraft's villages place a house, by the
    /// way in and not by the footprint (<c>docs/placement.md</c>):
    ///
    /// - **Beside the road**, by their lowest door (<see cref="Builder.Entry"/>): the door facing
    ///   the road <see cref="NearestDoor"/> to <see cref="FarthestDoor"/> past its edge, its height
    ///   taken from the road's - no more above or below it than a path at <see cref="DoorGrade"/>
    ///   climbs -, and the ground under the building levelled to its floor (a <see cref="Pad"/>):
    ///   cut down where it is higher, filled a little where it is lower, the piles carrying the
    ///   rest. Every place along the road inland of the dock, on either side, is weighed by the
    ///   ground it moves and how far the door is above or below the road, and the cheapest wins.
    ///   A dirt path forks off the road to the door, ramped from the road's height to the door's
    ///   (<see cref="DoorPath"/>, written by the caller after the road, then the pad).
    /// - **Onto the dock**: a building with a "dock" spot has that spot on the edge of the dock's
    ///   deck; one without has its door there. Level with the deck, off either side, over water no
    ///   deeper than the dock's piles reach. No door path: it is entered from the dock.
    ///
    /// A building on its own that finds no place beside the road is tried onto the dock, and the
    /// dock first where the land beside the road is steep (<see cref="Steep"/>); a blueprint that
    /// fits nowhere gives way to its <c>fallback</c>, as a Minecraft pool falls back to another.
    /// Trees and rocks on a building's footprint are cleared, now and when its zone is generated
    /// later (<see cref="Clearing"/>). Server only.
    /// </summary>
    internal static class Buildings
    {
        /// <summary>Along the road, from the dock's land end: where the first building may stand, the last, and the step between tries.</summary>
        private const float From = 4f;
        private const float To = 40f;
        private const float Step = 3f;
        /// <summary>How far a door stands off the road's levelled edge, at least and at most, and the step between tries.</summary>
        private const float NearestDoor = 1f;
        private const float FarthestDoor = 5f;
        private const float SetbackStep = 2f;
        /// <summary>A door path ends this far short of its door, so it is not levelled into the threshold.</summary>
        private const float Threshold = 0.3f;
        /// <summary>The steepest a door path climbs or falls from the road's flat edge to the door, rise over run.</summary>
        private const float DoorGrade = 0.45f;
        /// <summary>How deep a pad cuts into the ground at most, and how high it fills it.</summary>
        private const float PadCut = 2f;
        private const float PadFill = 1f;
        /// <summary>How far under the pad's fill the ground may still be where the blueprint has piles to carry its floor down.</summary>
        private const float MaxPiles = 3f;
        /// <summary>A site's cost per metre cut, filled and left to the piles, averaged over its footprint, and per metre the door is off the road's height.</summary>
        private const float CutCost = 2f;
        private const float FillCost = 1f;
        private const float PileCost = 0.5f;
        private const float RiseCost = 0.5f;
        /// <summary>A site's cost per metre its door is set back, and per metre further up the road; and up to how much is added at random, so harbours differ.</summary>
        private const float SetbackCost = 0.1f;
        private const float AlongCost = 0.03f;
        private const float Jitter = 0.3f;
        /// <summary>The ground under a site is sampled this far apart, and door heights tried this far apart.</summary>
        private const float Sample = 1.5f;
        private const float DoorStep = 0.25f;
        /// <summary>The land beside the road is steep where its ground is this far above or below the road everywhere near the dock.</summary>
        private const float SteepRise = 2.5f;
        /// <summary>How many blueprints one's fallbacks lead on to at most.</summary>
        private const int MaxFallbacks = 3;
        /// <summary>How far over the ground a floor may stand beside the dock when nothing carries it down.</summary>
        private const float Stiltless = 0.5f;
        /// <summary>How far above the water its ground has to be everywhere.</summary>
        private const float Dry = 1f;
        /// <summary>A structure this close to its footprint keeps it away.</summary>
        private const float Taken = 1.5f;
        /// <summary>How far round a building's pieces its trees are cleared.</summary>
        private const float ClearRadius = 1.5f;

        /// <summary>
        /// A dirt path from the road to a building's door, the structures to write it with - all
        /// of them but the building's own around its door, so the path reaches the threshold -,
        /// and the building's pad with the structures to write that with (all but the building's).
        /// </summary>
        internal sealed class DoorPath
        {
            public Trail Trail;
            public Structures Structures;
            public Vector2 Door;
            public Pad Pad;
            public Structures PadStructures;
            /// <summary>The building's footprint, left out of the pad's structures.</summary>
            public List<Vector2> Footprint;
        }

        /// <summary>What every building of one harbour is placed against.</summary>
        private sealed class Harbour
        {
            public Trail Trail;
            public Landings.Landing Landing;
            public float LandEnd;
            public Builder.Result Dock;
            public bool HasDock;
            /// <summary>The land beside the road is steep: onto the dock first.</summary>
            public bool DockFirst;
            public Structures Structures;
            /// <summary>The structures without the dock's own pieces, for a building beside it.</summary>
            public Structures BesideDock;
            public List<DoorPath> Paths;
            public Heightmap.Biome Biome;
            public System.Random Rng;
        }

        private static readonly HashSet<string> doorless = new HashSet<string>();

        /// <summary>The buildings of a new harbour, as their objects; the paths to their doors are added to doorPaths, to be written.</summary>
        public static List<ZDOID> ForHarbour(Trail trail, Landings.Landing landing, float landEnd, Builder.Result dock, Structures structures,
            int seed, List<DoorPath> doorPaths)
        {
            List<ZDOID> placed = new List<ZDOID>();
            int wanted = Docks.BuildingCount.Value;
            if (!Docks.Enabled.Value || wanted <= 0)
            {
                return placed;
            }
            System.Random rng = new System.Random(seed);
            Docks.Along(trail, landing, landEnd, out float _, out Vector2 seaward);
            Heightmap.Biome biome = Builder.LandBiome(trail.Points[landing.Shore], seaward);
            List<Blueprint> order = Blueprints.Shuffled(dock: false, biome, rng);
            if (order.Count == 0)
            {
                return placed;
            }
            Harbour harbour = new Harbour
            {
                Trail = trail,
                Landing = landing,
                LandEnd = landEnd,
                Dock = dock,
                HasDock = dock != null && dock.Reason == null && dock.Decks.Count > 0,
                Structures = structures,
                BesideDock = structures,
                Paths = new List<DoorPath>(),
                Biome = biome,
                Rng = rng,
            };
            harbour.DockFirst = harbour.HasDock && Steep(trail, landing, landEnd);
            // Beside the dock, the dock's own pieces are no obstacle.
            if (harbour.HasDock && structures != null)
            {
                List<Circle> own = new List<Circle>();
                foreach (Vector2 point in dock.Footprint)
                {
                    own.Add(new Circle { Center = point, Radius = 0.05f });
                }
                harbour.BesideDock = structures.Without(own);
            }
            HashSet<Blueprint> failed = new HashSet<Blueprint>();
            int built = 0;
            List<string> names = new List<string>();
            // Each blueprint once, then again while more are wanted than there are.
            for (int k = 0; k < order.Count * wanted && built < wanted; k++)
            {
                Blueprint blueprint = order[k % order.Count];
                if (failed.Contains(blueprint))
                {
                    continue;
                }
                Builder.Result result = Place(harbour, blueprint, out string where);
                // Nowhere for it: its fallbacks in turn, as a Minecraft pool falls back to another.
                Blueprint tried = blueprint;
                for (int depth = 0; result == null && depth < MaxFallbacks; depth++)
                {
                    Blueprint next = string.IsNullOrEmpty(tried.fallback) ? null : Blueprints.Named(tried.fallback);
                    if (next == null || next.IsDock || failed.Contains(next))
                    {
                        break;
                    }
                    result = Place(harbour, next, out where);
                    if (result == null)
                    {
                        failed.Add(next);
                    }
                    tried = next;
                }
                if (result == null)
                {
                    failed.Add(blueprint);
                    continue;
                }
                foreach (Vector2 point in result.Footprint)
                {
                    structures?.Add(point);
                    if (harbour.BesideDock != structures)
                    {
                        harbour.BesideDock.Add(point);
                    }
                }
                Clearing.ClearAround(result.Footprint, ClearRadius);
                placed.AddRange(result.Placed);
                names.Add(result + " (" + where + ")");
                built++;
            }
            if (structures != null && doorPaths != null)
            {
                // Every building is in the structures now; each path leaves out only its own door,
                // each pad only its own building.
                foreach (DoorPath path in harbour.Paths)
                {
                    path.Structures = structures.Without(new List<Circle> { new Circle { Center = path.Door, Radius = Structures.PieceReach + 0.5f } });
                    if (path.Pad != null)
                    {
                        List<Circle> own = new List<Circle>();
                        foreach (Vector2 point in path.Footprint)
                        {
                            own.Add(new Circle { Center = point, Radius = 0.05f });
                        }
                        path.PadStructures = structures.Without(own);
                    }
                    doorPaths.Add(path);
                }
            }
            Debug.Log("[OdinsPaths] Harbour at " + trail.Points[landing.Shore].ToString("F0") + ": " + built + " buildings"
                + (harbour.DockFirst ? ", the land beside the road steep" : "") + (names.Count > 0 ? " - " + string.Join("; ", names) : ""));
            return placed;
        }

        /// <summary>One blueprint where it may go, in the harbour's order: beside the road or onto the dock; null if neither.</summary>
        private static Builder.Result Place(Harbour harbour, Blueprint blueprint, out string where)
        {
            List<Builder.Part> parts = Builder.Resolve(blueprint, false);
            where = "onto the dock";
            if (blueprint.Joint != null)
            {
                // Joined to the dock: from the building's middle out through the joint.
                Vector3 joint = blueprint.Joint.Position;
                Vector2 onto = new Vector2(joint.x, joint.z) - Builder.Middle(parts);
                return harbour.HasDock && onto.sqrMagnitude >= 0.01f ? AtDock(harbour, blueprint, parts, joint, onto.normalized) : null;
            }
            bool door = Builder.Entry(parts, out Vector3 foot, out Vector2 inward);
            if (!door && doorless.Add(blueprint.name))
            {
                // One without a door is placed by its front's middle, as before doors.
                Debug.LogWarning("[OdinsPaths] Harbour building " + blueprint.name + " has no door: placed by its front's middle.");
            }
            Builder.Result result = null;
            if (harbour.DockFirst && door)
            {
                result = AtDock(harbour, blueprint, parts, foot, -inward);
            }
            if (result == null)
            {
                result = BesideRoad(harbour, blueprint, parts, foot, inward, out where);
            }
            if (result == null && harbour.HasDock && door && !harbour.DockFirst)
            {
                where = "onto the dock";
                result = AtDock(harbour, blueprint, parts, foot, -inward);
            }
            return result;
        }

        /// <summary>
        /// Whether the land beside the road just inland of the dock is steep everywhere: its ground,
        /// a few metres off the road on either side, never within <see cref="SteepRise"/> of the
        /// road's height. A hut there would stand on a pad cut deep into the bank, or high on piles.
        /// </summary>
        private static bool Steep(Trail trail, Landings.Landing landing, float landEnd)
        {
            for (float d = landEnd + From; d <= landEnd + From + 12f; d += 4f)
            {
                Vector2 road = Docks.Along(trail, landing, d, out float height, out Vector2 seaward);
                Vector2 side = new Vector2(seaward.y, -seaward.x) * (trail.Kind.Reach + 3f);
                foreach (Vector2 at in new[] { road + side, road - side })
                {
                    if (Mathf.Abs(Ground.Height(at.x, at.y) - height) < SteepRise)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// A building on its own beside the road: the cheapest place along the road from the dock
        /// inland, either side, where it fits with its door facing the road <see cref="NearestDoor"/>
        /// to <see cref="FarthestDoor"/> off it (<see cref="Site"/>), its pad and the path to its door.
        /// </summary>
        private static Builder.Result BesideRoad(Harbour harbour, Blueprint blueprint, List<Builder.Part> parts, Vector3 foot, Vector2 inward,
            out string where)
        {
            where = null;
            Trail trail = harbour.Trail;
            if (!Box(parts, out Vector2 min, out Vector2 max))
            {
                return null;
            }
            bool piles = Builder.HasStilts(parts);
            // The pad is levelled under the floor the door opens onto.
            float floor = Builder.DoorFloor(parts, foot);
            float edge = trail.Kind.Reach;
            Builder.Frame best = default;
            Vector2 bestRoad = Vector2.zero;
            Vector2 bestAway = Vector2.zero;
            float bestRoadHeight = 0f;
            float bestFlat = 0f;
            float bestCost = float.MaxValue;
            for (float d = harbour.LandEnd + From; d <= harbour.LandEnd + To; d += Step)
            {
                Vector2 road = Docks.Along(trail, harbour.Landing, d, out float roadHeight, out Vector2 seaward);
                float flat = trail.Kind.HalfWidthAt(road) * trail.Kind.FlatFactor;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 away = new Vector2(seaward.y, -seaward.x) * side;
                    for (float setback = NearestDoor; setback <= FarthestDoor + 0.01f; setback += SetbackStep)
                    {
                        Vector2 door = road + away * (edge + setback);
                        Builder.Frame frame = Builder.Frame.Make(door, away, 0f).Entered(foot, inward);
                        // From the road's flat edge to just short of the door, at the grade a path may climb.
                        float rise = (edge + setback - Threshold - flat) * DoorGrade;
                        if (!Site(harbour, min, max, piles, floor, frame, roadHeight, rise, out float doorHeight, out float cost)
                            || !PathClear(road + away * edge, door, harbour.Structures))
                        {
                            continue;
                        }
                        cost += setback * SetbackCost + (d - harbour.LandEnd - From) * AlongCost + (float)harbour.Rng.NextDouble() * Jitter;
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = frame;
                            best.Floor = doorHeight;
                            bestRoad = road;
                            bestAway = away;
                            bestRoadHeight = roadHeight;
                            bestFlat = flat;
                        }
                    }
                }
            }
            if (bestCost == float.MaxValue)
            {
                return null;
            }
            best.Pad = new Pad
            {
                Frame = best,
                Min = min - Vector2.one * Pad.Apron,
                Max = max + Vector2.one * Pad.Apron,
                Height = best.Height(floor) - Pad.UnderFloor,
                MaxCut = PadCut,
                MaxFill = PadFill,
            };
            Builder.Result result = Raise(blueprint, parts, best, harbour.Biome, harbour.Rng);
            Trail path = new Trail(new List<Vector2> { bestRoad, best.Origin - bestAway * Threshold }, RoadKind.Spur);
            path.Ramp(bestRoadHeight, best.Floor, PadCut + 1f, bestFlat);
            harbour.Paths.Add(new DoorPath
            {
                Trail = path,
                Door = best.Origin,
                Pad = best.Pad,
                Footprint = result.Footprint,
            });
            float above = best.Floor - bestRoadHeight;
            where = "beside the road, its door " + Mathf.Abs(above).ToString("F1") + " m " + (above >= 0f ? "above" : "below") + " it, "
                + Vector2.Distance(bestRoad, best.Origin).ToString("F1") + " m off its middle";
            return result;
        }

        /// <summary>
        /// Whether a building beside the road stands here, and at what height its door does: the
        /// one between the road's height less rise and plus rise that moves the least ground for
        /// its pad - cutting at most <see cref="PadCut"/>, filling <see cref="PadFill"/>, and piles,
        /// if it has any, carrying its floor <see cref="MaxPiles"/> further down -, and what that
        /// costs. The ground under its box must be dry, clear of structures, this road, locations
        /// and the other door paths.
        /// </summary>
        private static bool Site(Harbour harbour, Vector2 min, Vector2 max, bool piles, float floor, Builder.Frame frame, float roadHeight, float rise,
            out float door, out float cost)
        {
            door = roadHeight;
            cost = float.MaxValue;
            Trail trail = harbour.Trail;
            float water = ZoneSystem.instance.m_waterLevel;
            int near = NearestPoint(trail, frame.Origin);
            List<float> heights = new List<float>();
            float stepX = Mathf.Min(Sample, max.x - min.x + 0.01f);
            float stepZ = Mathf.Min(Sample, max.y - min.y + 0.01f);
            for (float x = min.x; x <= max.x + 0.01f; x += stepX)
            {
                for (float z = min.y; z <= max.y + 0.01f; z += stepZ)
                {
                    Vector2 at = frame.Flat(x, z);
                    float ground = Ground.Height(at.x, at.y);
                    if (ground < water + Dry || (harbour.Structures != null && harbour.Structures.Distance(at, Taken) < Taken)
                        || NearPath(harbour.Paths, at) || OnRoad(trail, near, at) || Clearing.InLocation(at))
                    {
                        return false;
                    }
                    heights.Add(ground);
                }
            }
            float deepest = PadFill + (piles ? MaxPiles : 0f);
            for (float height = roadHeight - rise; height <= roadHeight + rise + 0.01f; height += DoorStep)
            {
                // The pad's level for a door at this height: just under the floor's top.
                float level = height + floor - frame.Anchor.y - Pad.UnderFloor;
                float cut = 0f;
                float fill = 0f;
                float carried = 0f;
                bool fits = true;
                foreach (float ground in heights)
                {
                    float above = ground - level;
                    if (above > PadCut || -above > deepest)
                    {
                        fits = false;
                        break;
                    }
                    cut += Mathf.Max(above, 0f);
                    fill += Mathf.Clamp(-above, 0f, PadFill);
                    carried += Mathf.Max(-above - PadFill, 0f);
                }
                if (!fits)
                {
                    continue;
                }
                float here = (cut * CutCost + fill * FillCost + carried * PileCost) / heights.Count + Mathf.Abs(height - roadHeight) * RiseCost;
                if (here < cost)
                {
                    cost = here;
                    door = height;
                }
            }
            return cost < float.MaxValue;
        }

        /// <summary>The box around a building's floors, walls, doors and piles, x and z in its blueprint; false if it has none.</summary>
        private static bool Box(List<Builder.Part> parts, out Vector2 min, out Vector2 max)
        {
            min = Vector2.one * float.MaxValue;
            max = Vector2.one * float.MinValue;
            foreach (Builder.Part part in parts)
            {
                if (part.Solid)
                {
                    min = Vector2.Min(min, new Vector2(part.Min.x, part.Min.z));
                    max = Vector2.Max(max, new Vector2(part.Max.x, part.Max.z));
                }
            }
            return min.x <= max.x;
        }

        /// <summary>
        /// A building onto the dock: its joint - a "dock" spot, or the foot of its door - on the
        /// edge of the dock's deck, at the deck's height, the building off that side, onto pointing
        /// out of it onto the deck - tried along both edges past the land end, from a random place on.
        /// </summary>
        private static Builder.Result AtDock(Harbour harbour, Blueprint blueprint, List<Builder.Part> parts, Vector3 joint, Vector2 onto)
        {
            Builder.Result dock = harbour.Dock;
            if (!harbour.HasDock)
            {
                return null;
            }
            float far = float.MinValue;
            foreach (Bounds deck in dock.Decks)
            {
                far = Mathf.Max(far, deck.max.z);
            }
            List<Vector2> tries = new List<Vector2>();
            for (float z = Builder.LandEndKept + 1f; z <= far; z += 1f)
            {
                tries.Add(new Vector2(1f, z));
                tries.Add(new Vector2(-1f, z));
            }
            int start = tries.Count > 0 ? harbour.Rng.Next(tries.Count) : 0;
            for (int n = 0; n < tries.Count; n++)
            {
                Vector2 at = tries[(start + n) % tries.Count];
                if (!Edge(dock.Decks, at.y, at.x, out float x, out float top))
                {
                    continue;
                }
                Builder.Frame frame = Builder.Frame.Make(dock.Frame.Flat(x, at.y), dock.Frame.Right * -at.x, dock.Frame.Height(top))
                    .Entered(joint, onto);
                if (BesideDock(harbour.Trail, parts, frame, dock, harbour.BesideDock, harbour.Paths))
                {
                    return Raise(blueprint, parts, frame, harbour.Biome, harbour.Rng);
                }
            }
            return null;
        }

        /// <summary>A dock's outer edge on one side (1 right looking out, -1 left) z metres out, in its frame, and the deck's top there.</summary>
        private static bool Edge(List<Bounds> decks, float z, float side, out float x, out float top)
        {
            x = 0f;
            top = 0f;
            bool any = false;
            foreach (Bounds deck in decks)
            {
                if (z < deck.min.z - 0.01f || z > deck.max.z + 0.01f)
                {
                    continue;
                }
                float edge = side > 0f ? deck.max.x : deck.min.x;
                if (!any || edge * side > x * side)
                {
                    x = edge;
                    top = deck.max.y;
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// Whether a building beside the dock stands there: none of it on the dock's deck, its
        /// floors neither under the ground nor over water deeper than a dock may stand in - nor off
        /// the ground at all without stilts (<see cref="Builder.HasStilts"/>) -, clear of
        /// structures and door paths, and on land off the road and out of locations.
        /// </summary>
        private static bool BesideDock(Trail trail, List<Builder.Part> parts, Builder.Frame frame, Builder.Result dock, Structures structures,
            List<DoorPath> paths)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            int near = NearestPoint(trail, frame.Origin);
            // Without stilts to carry it down, a building stands only where its floors are on the ground.
            float deepest = Builder.HasStilts(parts) ? Docks.MaxDepth : Stiltless;
            foreach (Builder.Part part in parts)
            {
                if (!part.Solid)
                {
                    continue;
                }
                Vector2 at = frame.Flat(part.Centre.x, part.Centre.y);
                Vector3 onDock = dock.Frame.Local(new Vector3(at.x, 0f, at.y));
                foreach (Bounds deck in dock.Decks)
                {
                    if (onDock.x > deck.min.x + 0.1f && onDock.x < deck.max.x - 0.1f && onDock.z > deck.min.z + 0.1f && onDock.z < deck.max.z - 0.1f)
                    {
                        return false;
                    }
                }
                float ground = OdinsPaths.Ground.Height(at.x, at.y);
                if (part.Stands && (ground > frame.Height(part.Max.y) + Builder.Buried || frame.Height(part.Max.y) - ground > deepest))
                {
                    return false;
                }
                if ((structures != null && structures.Distance(at, Taken) < Taken) || NearPath(paths, at)
                    || (ground > water && (OnRoad(trail, near, at) || Clearing.InLocation(at))))
                {
                    return false;
                }
            }
            return true;
        }

        private static Builder.Result Raise(Blueprint blueprint, List<Builder.Part> parts, Builder.Frame frame, Heightmap.Biome biome, System.Random rng)
        {
            Builder.Result result = new Builder.Result();
            Builder.Raise(blueprint, parts, frame, new Builder.Options
            {
                Biome = biome,
                Building = true,
                ChestChance = Docks.ChestChance.Value,
                EnemyChance = Docks.EnemyChance.Value,
            }, rng, result);
            return result;
        }

        /// <summary>Whether the way from the road's edge to a door is dry, unbuilt and outside locations.</summary>
        private static bool PathClear(Vector2 from, Vector2 door, Structures structures)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            float length = Vector2.Distance(from, door);
            for (float t = 0f; t <= length; t += 0.5f)
            {
                Vector2 at = Vector2.Lerp(from, door, length > 0f ? t / length : 1f);
                if (OdinsPaths.Ground.Height(at.x, at.y) < water + Dry || (structures != null && structures.Distance(at, 1f) < 1f)
                    || Clearing.InLocation(at))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Whether a point is within <see cref="Taken"/> of a door path laid out already.</summary>
        private static bool NearPath(List<DoorPath> paths, Vector2 at)
        {
            foreach (DoorPath path in paths)
            {
                for (int s = 0; s < path.Trail.Points.Count - 1; s++)
                {
                    if (path.Trail.Nearest(at, s, out float _) < Taken + path.Trail.Kind.MaxHalfWidth)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static int NearestPoint(Trail trail, Vector2 at)
        {
            int best = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < trail.Points.Count; i++)
            {
                float d = (trail.Points[i] - at).sqrMagnitude;
                if (d < nearest)
                {
                    nearest = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Whether a point is on the road or its levelled edge, near the trail point given (within 60 m of it along the trail).</summary>
        private static bool OnRoad(Trail trail, int near, Vector2 at)
        {
            for (int s = Mathf.Max(0, near - 30); s < Mathf.Min(trail.Points.Count - 1, near + 30); s++)
            {
                if (trail.Nearest(at, s, out float _) < trail.Kind.Reach)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// For the dev command: a building exactly here, its lowest door's foot at the origin (else
        /// its front's middle), entered along inward, the door's foot at the height given; and the
        /// frame it was built in.
        /// </summary>
        public static Builder.Result Here(Blueprint blueprint, Vector2 origin, Vector2 inward, float floor, Builder.Options options, int seed,
            out Builder.Frame frame)
        {
            System.Random rng = new System.Random(seed);
            List<Builder.Part> parts = Builder.Resolve(blueprint, options.Edit);
            frame = Builder.Frame.Make(origin, inward, floor);
            if (Builder.Entry(parts, out Vector3 foot, out Vector2 way))
            {
                frame = frame.Entered(foot, way);
            }
            options.Biome = Builder.LandBiome(origin, -inward);
            options.Building = true;
            Builder.Result result = new Builder.Result();
            Builder.Raise(blueprint, parts, frame, options, rng, result);
            return result;
        }
    }
}
