using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The old buildings of a new harbour - huts, sheds, storehouses: building blueprints of the
    /// land's biome (<see cref="Blueprints"/>), a different one each while there are, raised and
    /// weathered by <see cref="Builder"/>. Two kinds:
    ///
    /// - **On their own**, placed by their lowest door (<see cref="Builder.Entry"/>): somewhere along
    ///   the road inland of the dock, on either side, the door facing it
    ///   <see cref="NearestDoor"/> to <see cref="FarthestDoor"/> past its levelled edge, and a
    ///   dirt path forking off the road to the door (<see cref="DoorPath"/>, written by the caller
    ///   after the road). Where the ground is dry, no steeper than <see cref="MaxRise"/> across,
    ///   clear of structures, locations, the road and the other door paths. Its floor is at the
    ///   highest ground under it, and its piles reach down to the rest.
    /// - **Joined to the dock**, a building with a "dock" spot: that spot on the edge of the dock's
    ///   deck, level with it, on either side, the building off the dock, over water no deeper than
    ///   the dock's piles reach. No door path: it is entered from the dock.
    ///
    /// Trees and rocks on a building's footprint are cleared, now and when its zone is generated
    /// later (<see cref="Clearing"/>). Server only.
    /// </summary>
    internal static class Buildings
    {
        /// <summary>Along the road, from the dock's land end: where the first building may stand, the last, and the step between tries.</summary>
        private const float From = 4f;
        private const float To = 40f;
        private const float Step = 3f;
        /// <summary>How far a door stands off the road's levelled edge, at least and at most.</summary>
        private const float NearestDoor = 1f;
        private const float FarthestDoor = 5f;
        /// <summary>A door path ends this far short of its door, so it is not levelled into the threshold.</summary>
        private const float Threshold = 0.3f;
        /// <summary>The most the ground may rise across a building's footprint: its piles make up the rest.</summary>
        private const float MaxRise = 2.5f;
        /// <summary>How far above the water its ground has to be everywhere.</summary>
        private const float Dry = 1f;
        /// <summary>A structure this close to its footprint keeps it away.</summary>
        private const float Taken = 1.5f;
        /// <summary>How far round a building's pieces its trees are cleared.</summary>
        private const float ClearRadius = 1.5f;

        /// <summary>
        /// A dirt path from the road to a building's door, and the structures to write it with:
        /// all of them but the building's own around its door, so the path reaches the threshold.
        /// </summary>
        internal sealed class DoorPath
        {
            public Trail Trail;
            public Structures Structures;
            public Vector2 Door;
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
            bool hasDock = dock != null && dock.Reason == null && dock.Decks.Count > 0;
            // Beside the dock, the dock's own pieces are no obstacle.
            Structures besideDock = structures;
            if (hasDock && structures != null)
            {
                List<Circle> own = new List<Circle>();
                foreach (Vector2 point in dock.Footprint)
                {
                    own.Add(new Circle { Center = point, Radius = 0.05f });
                }
                besideDock = structures.Without(own);
            }
            List<DoorPath> paths = new List<DoorPath>();
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
                Builder.Result result = blueprint.Joint == null
                    ? BesideRoad(trail, landing, landEnd, blueprint, biome, structures, paths, rng)
                    : hasDock ? AtDock(trail, blueprint, dock, besideDock, paths, biome, rng) : null;
                if (result == null)
                {
                    failed.Add(blueprint);
                    continue;
                }
                foreach (Vector2 point in result.Footprint)
                {
                    structures?.Add(point);
                    if (besideDock != structures)
                    {
                        besideDock.Add(point);
                    }
                }
                Clearing.ClearAround(result.Footprint, ClearRadius);
                placed.AddRange(result.Placed);
                names.Add(result.ToString());
                built++;
            }
            if (structures != null && doorPaths != null)
            {
                // Every building is in the structures now; each path leaves out only its own door.
                foreach (DoorPath path in paths)
                {
                    path.Structures = structures.Without(new List<Circle> { new Circle { Center = path.Door, Radius = Structures.PieceReach + 0.5f } });
                    doorPaths.Add(path);
                }
            }
            Debug.Log("[OdinsPaths] Harbour at " + trail.Points[landing.Shore].ToString("F0") + ": " + built + " buildings"
                + (names.Count > 0 ? " - " + string.Join("; ", names) : ""));
            return placed;
        }

        /// <summary>
        /// A building on its own: the first place along the road, from the dock inland, either
        /// side, where it fits with its door facing the road <see cref="NearestDoor"/> to
        /// <see cref="FarthestDoor"/> off it (a random distance first, then from the nearest), and
        /// the path to that door.
        /// </summary>
        private static Builder.Result BesideRoad(Trail trail, Landings.Landing landing, float landEnd, Blueprint blueprint,
            Heightmap.Biome biome, Structures structures, List<DoorPath> paths, System.Random rng)
        {
            List<Builder.Part> parts = Builder.Resolve(blueprint, false);
            if (!Builder.Entry(parts, out Vector3 foot, out Vector2 inward))
            {
                // One without a door is placed by its front's middle, as before doors.
                if (doorless.Add(blueprint.name))
                {
                    Debug.LogWarning("[OdinsPaths] Harbour building " + blueprint.name + " has no door: placed by its front's middle.");
                }
            }
            for (float d = landEnd + From; d <= landEnd + To; d += Step)
            {
                Vector2 road = Docks.Along(trail, landing, d, out float _, out Vector2 seaward);
                int first = rng.Next(2) == 0 ? -1 : 1;
                for (int k = 0; k < 2; k++)
                {
                    Vector2 away = new Vector2(seaward.y, -seaward.x) * (k == 0 ? first : -first);
                    float edge = trail.Kind.Reach;
                    float chosen = Mathf.Lerp(NearestDoor, FarthestDoor, (float)rng.NextDouble());
                    for (float setback = NearestDoor - 1f; setback <= FarthestDoor; setback += 1f)
                    {
                        Vector2 door = road + away * (edge + (setback < NearestDoor ? chosen : setback));
                        Builder.Frame frame = Builder.Frame.Make(door, away, 0f).Entered(foot, inward);
                        if (!Ground(trail, parts, frame, structures, paths, out float floor) || !PathClear(road + away * edge, door, structures))
                        {
                            continue;
                        }
                        frame.Floor = floor + foot.y;
                        Builder.Result result = Raise(blueprint, parts, frame, biome, rng);
                        paths.Add(new DoorPath
                        {
                            Trail = new Trail(new List<Vector2> { road, door - away * Threshold }, RoadKind.Spur),
                            Door = door,
                        });
                        return result;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// A building joined to the dock: its "dock" spot on the edge of the dock's deck, at the
        /// deck's height, the building off that side - tried along both edges past the land end,
        /// from a random place on.
        /// </summary>
        private static Builder.Result AtDock(Trail trail, Blueprint blueprint, Builder.Result dock, Structures structures,
            List<DoorPath> paths, Heightmap.Biome biome, System.Random rng)
        {
            List<Builder.Part> parts = Builder.Resolve(blueprint, false);
            Vector3 joint = blueprint.Joint.Position;
            // Onto the dock: from the building's middle out through the joint.
            Vector2 onto = new Vector2(joint.x, joint.z) - Builder.Middle(parts);
            if (onto.sqrMagnitude < 0.01f)
            {
                return null;
            }
            onto.Normalize();
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
            int start = tries.Count > 0 ? rng.Next(tries.Count) : 0;
            for (int n = 0; n < tries.Count; n++)
            {
                Vector2 at = tries[(start + n) % tries.Count];
                if (!Edge(dock.Decks, at.y, at.x, out float x, out float top))
                {
                    continue;
                }
                Builder.Frame frame = Builder.Frame.Make(dock.Frame.Flat(x, at.y), dock.Frame.Right * -at.x, dock.Frame.Height(top))
                    .Entered(joint, onto);
                if (BesideDock(trail, parts, frame, dock, structures, paths))
                {
                    return Raise(blueprint, parts, frame, biome, rng);
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
        /// floors neither under the ground nor over water deeper than a dock may stand in, clear of
        /// structures and door paths, and on land off the road and out of locations.
        /// </summary>
        private static bool BesideDock(Trail trail, List<Builder.Part> parts, Builder.Frame frame, Builder.Result dock, Structures structures,
            List<DoorPath> paths)
        {
            float water = ZoneSystem.instance.m_waterLevel;
            int near = NearestPoint(trail, frame.Origin);
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
                if (part.Stands && (ground > frame.Height(part.Max.y) + Builder.Buried || frame.Height(part.Max.y) - ground > Docks.MaxDepth))
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

        /// <summary>
        /// Whether the ground under a building's footprint (every metre of the box around its
        /// floors, walls, doors and piles) will do, and the height its floor (the blueprint's y 0)
        /// goes at: the highest of it.
        /// </summary>
        private static bool Ground(Trail trail, List<Builder.Part> parts, Builder.Frame frame, Structures structures, List<DoorPath> paths, out float floor)
        {
            floor = 0f;
            Vector2 min = Vector2.one * float.MaxValue;
            Vector2 max = Vector2.one * float.MinValue;
            foreach (Builder.Part part in parts)
            {
                if (part.Solid)
                {
                    min = Vector2.Min(min, new Vector2(part.Min.x, part.Min.z));
                    max = Vector2.Max(max, new Vector2(part.Max.x, part.Max.z));
                }
            }
            if (min.x > max.x)
            {
                return false;
            }
            float water = ZoneSystem.instance.m_waterLevel;
            float low = float.MaxValue;
            float high = float.MinValue;
            int near = NearestPoint(trail, frame.Origin);
            for (float x = min.x; x <= max.x + 0.01f; x += Mathf.Min(1f, max.x - min.x + 0.01f))
            {
                for (float z = min.y; z <= max.y + 0.01f; z += Mathf.Min(1f, max.y - min.y + 0.01f))
                {
                    Vector2 at = frame.Flat(x, z);
                    float ground = OdinsPaths.Ground.Height(at.x, at.y);
                    low = Mathf.Min(low, ground);
                    high = Mathf.Max(high, ground);
                    if (ground < water + Dry || high - low > MaxRise
                        || (structures != null && structures.Distance(at, Taken) < Taken) || NearPath(paths, at)
                        || OnRoad(trail, near, at) || Clearing.InLocation(at))
                    {
                        return false;
                    }
                }
            }
            floor = high + 0.05f;
            return true;
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
