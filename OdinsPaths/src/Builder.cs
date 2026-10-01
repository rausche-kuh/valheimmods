using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Raises a <see cref="Blueprint"/> in a frame on the ground - a dock at a harbour
    /// (<see cref="Docks"/>), a building beside its road (<see cref="Buildings"/>) - and leaves it
    /// to the weather for years: every piece worn to some degree (its health, which the game shows
    /// as new, worn or broken), and planks, walls, roof, posts and furniture gone, the more the
    /// worse its condition. Piles and floors are never taken: they hold up what is left, and
    /// without them the game brings the rest down the first time a player comes near. Each
    /// column of piles is carried on down to the ground, so a blueprint drawn on one shore stands
    /// on any other. Its furniture is the good kind, as relics that drop nothing
    /// (<see cref="Relics"/>); some hold a treasure chest of the biome, some a few of its enemies,
    /// spawned once when a player comes near.
    ///
    /// Everything is ZDOs written on the server (<see cref="Landings.Spawn"/>), in or out of a
    /// loaded zone, marked as the mod's, and returned for a dev undo. The pieces are vanilla
    /// prefabs, so a client without the mod sees them; only the relics need the mod. Server only.
    /// </summary>
    internal static class Builder
    {
        private const int MaxPileStack = 6;
        /// <summary>Ground this far above a deck's top buries it: it is left out (the shore rises there).</summary>
        public const float Buried = 0.3f;
        /// <summary>A deck piece this close to something standing on it (a post, furniture, a spot) stays.</summary>
        private const float Holds = 1.2f;
        /// <summary>Near the land end a dock's deck always stays: it is where the road runs on.</summary>
        internal const float LandEndKept = 2f;
        /// <summary>A lamp this close above a post stands on it, and goes with it.</summary>
        private const float OnPost = 0.6f;
        /// <summary>How high a wall stands: a deco_h1 spot takes furniture up to this high, a deco_h2 up to twice it.</summary>
        private const float WallHeight = 2f;
        /// <summary>Furniture's widest across, and a rug's (anything flatter than <see cref="RugHeight"/>).</summary>
        private const float MaxWidth = 2.3f;
        private const float MaxRugWidth = 3.2f;
        private const float RugHeight = 0.2f;

        /// <summary>A piece's role as <see cref="Role"/>'s name, so <c>docks capture</c> reads back what was built.</summary>
        internal static readonly int RoleKey = "OdinsPaths_Role".GetStableHashCode();
        /// <summary>A pile piece added below the blueprint's own to reach the ground: capture leaves it out.</summary>
        internal static readonly int ExtensionKey = "OdinsPaths_PileExtension".GetStableHashCode();
        /// <summary>Part of a harbour building: a zone generated later has its trees taken off it (<see cref="Clearing"/>).</summary>
        internal static readonly int BuildingKey = "OdinsPaths_Building".GetStableHashCode();

        /// <summary>
        /// Where a blueprint's frame lies in the world: its <see cref="Anchor"/> at the origin, on
        /// the floor height given, and the blueprint's <see cref="Inward"/> along forward. A dock's
        /// anchor is the frame's own origin, z its forward; a building's its door's foot, the way
        /// in through it (<see cref="Entered"/>), or a dock spot's foot, the way onto the dock.
        /// </summary>
        internal struct Frame
        {
            public Vector2 Origin;
            public Vector2 Right;
            public Vector2 Forward;
            /// <summary>The world height of the anchor: a dock's deck top at the land end, a building's door foot.</summary>
            public float Floor;
            /// <summary>From the blueprint's frame to the world's.</summary>
            public Quaternion Rotation;
            /// <summary>The blueprint's point at the origin.</summary>
            public Vector3 Anchor;
            /// <summary>Turns the blueprint's frame so its inward direction is z.</summary>
            private Quaternion inner;

            public static Frame Make(Vector2 origin, Vector2 forward, float floor)
            {
                forward = forward.normalized;
                return new Frame
                {
                    Origin = origin,
                    Forward = forward,
                    Right = new Vector2(forward.y, -forward.x),
                    Floor = floor,
                    Rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.y)),
                    inner = Quaternion.identity,
                };
            }

            /// <summary>This frame with the blueprint's point anchor at its origin and the blueprint's direction inward along its forward.</summary>
            public Frame Entered(Vector3 anchor, Vector2 inward)
            {
                Frame frame = this;
                frame.Anchor = anchor;
                frame.inner = Quaternion.Inverse(Quaternion.LookRotation(new Vector3(inward.x, 0f, inward.y)));
                frame.Rotation = Quaternion.LookRotation(new Vector3(Forward.x, 0f, Forward.y)) * frame.inner;
                return frame;
            }

            public Vector2 Flat(float x, float z)
            {
                Vector3 v = inner * new Vector3(x - Anchor.x, 0f, z - Anchor.z);
                return Origin + Right * v.x + Forward * v.z;
            }

            /// <summary>The world height of a height in the blueprint.</summary>
            public float Height(float y) => Floor + y - Anchor.y;

            public Vector3 World(Vector3 local)
            {
                Vector2 flat = Flat(local.x, local.z);
                return new Vector3(flat.x, Height(local.y), flat.y);
            }

            /// <summary>A world point in the blueprint's frame.</summary>
            public Vector3 Local(Vector3 world)
            {
                Vector2 d = new Vector2(world.x, world.z) - Origin;
                Vector3 v = Quaternion.Inverse(inner) * new Vector3(Vector2.Dot(d, Right), 0f, Vector2.Dot(d, Forward));
                return new Vector3(v.x + Anchor.x, world.y - Floor + Anchor.y, v.z + Anchor.z);
            }
        }

        internal sealed class Options
        {
            public Heightmap.Biome Biome;
            /// <summary>A dock: its land end's deck always stays.</summary>
            public bool Dock;
            /// <summary>0 a ruin, 1 as good as new; NaN: at random.</summary>
            public float Condition = float.NaN;
            public float ChestChance;
            public float EnemyChance;
            /// <summary>
            /// To rework it in game: nothing weathered, the game's own pieces instead of relics,
            /// placed as the player's (<see cref="Creator"/>) so the hammer takes them, a sign for
            /// each spot, no chest, enemies or clutter.
            /// </summary>
            public bool Edit;
            public long Creator;
            /// <summary>Marks every piece as a building's (<see cref="BuildingKey"/>).</summary>
            public bool Building;
        }

        internal sealed class Result
        {
            public readonly List<ZDOID> Placed = new List<ZDOID>();
            public string Name;
            public float Condition;
            public int Removed;
            public bool Chest;
            public int Enemies;
            /// <summary>Where a "stone" spot put the harbour stone, if the blueprint has one.</summary>
            public bool HasStone;
            public Vector3 Stone;
            public Quaternion StoneRotation;
            /// <summary>Points over what it stands on, in the world: structures for later roads, and ground to clear.</summary>
            public readonly List<Vector2> Footprint = new List<Vector2>();
            /// <summary>Why nothing was built, when nothing was.</summary>
            public string Reason;
            /// <summary>The frame it was built in, and the boxes of its decks and floors still there in it: where a building may join a dock.</summary>
            public Frame Frame;
            public readonly List<Bounds> Decks = new List<Bounds>();

            public override string ToString()
            {
                if (Reason != null)
                {
                    return "none: " + Reason;
                }
                return Name + ", " + Placed.Count + " objects, condition " + Condition.ToString("F2") + ", " + Removed
                    + " pieces weathered away, " + (Chest ? "a chest, " : "no chest, ") + Enemies + " enemies";
            }
        }

        /// <summary>A blueprint's piece measured and placed in the frame: its pivot and its box, both in the frame.</summary>
        internal sealed class Part
        {
            public BlueprintPiece Piece;
            public GameObject Prefab;
            public Vector3 Pivot;
            public Vector3 Min;
            public Vector3 Max;

            public Role Role => Piece.Role;
            public Vector2 Centre => new Vector2((Min.x + Max.x) * 0.5f, (Min.z + Max.z) * 0.5f);
            public bool Stands => Role == Role.Deck || Role == Role.Floor;
            /// <summary>What a building stands on and is closed by: its footprint.</summary>
            public bool Solid => Stands || Role == Role.Wall || Role == Role.Door || Role == Role.Pile;
        }

        /// <summary>
        /// Where a building is entered from the ground: its lowest door's foot (the middle of its
        /// box's bottom), and the way in through it - across the door, toward the middle of the
        /// floors. False for a building without a door.
        /// </summary>
        public static bool Entry(List<Part> parts, out Vector3 foot, out Vector2 inward)
        {
            foot = Vector3.zero;
            inward = Vector2.up;
            Part door = null;
            foreach (Part part in parts)
            {
                if (part.Role == Role.Door && (door == null || part.Min.y < door.Min.y))
                {
                    door = part;
                }
            }
            if (door == null)
            {
                return false;
            }
            foot = new Vector3(door.Centre.x, door.Min.y, door.Centre.y);
            // Across the door: the thinner of its own two level axes, as it is turned.
            Bounds shape = Shape(door.Prefab);
            Vector3 across = door.Piece.Rotation * (shape.size.x < shape.size.z ? Vector3.right : Vector3.forward);
            inward = new Vector2(across.x, across.z);
            if (inward.sqrMagnitude < 0.01f)
            {
                inward = Vector2.up;
            }
            inward.Normalize();
            if (Vector2.Dot(Middle(parts) - door.Centre, inward) < 0f)
            {
                inward = -inward;
            }
            return true;
        }

        /// <summary>The middle of a blueprint's floors, or of all its pieces if it has none.</summary>
        public static Vector2 Middle(List<Part> parts)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (Part part in parts)
            {
                if (part.Stands)
                {
                    sum += part.Centre;
                    count++;
                }
            }
            if (count == 0)
            {
                foreach (Part part in parts)
                {
                    sum += part.Centre;
                    count++;
                }
            }
            return count > 0 ? sum / count : Vector2.zero;
        }

        /// <summary>The blueprint's pieces with their prefabs and measured boxes; a piece whose prefab the game lacks is left out.</summary>
        public static List<Part> Resolve(Blueprint blueprint, bool edit)
        {
            List<Part> parts = new List<Part>();
            foreach (BlueprintPiece piece in blueprint.pieces)
            {
                GameObject prefab = piece.Role == Role.Deco && !edit ? Relics.Get(piece.prefab) : Prefab(piece.prefab);
                if (prefab == null)
                {
                    continue;
                }
                Box(prefab, piece.Position, piece.Rotation, piece.Anchor, out Vector3 pivot, out Vector3 min, out Vector3 max);
                parts.Add(new Part { Piece = piece, Prefab = prefab, Pivot = pivot, Min = min, Max = max });
            }
            return parts;
        }

        /// <summary>Where a prefab's pivot goes for its anchor to be at position, and the box it then fills.</summary>
        private static void Box(GameObject prefab, Vector3 position, Quaternion rotation, Anchor anchor, out Vector3 pivot, out Vector3 min, out Vector3 max)
        {
            Bounds shape = Shape(prefab);
            pivot = position;
            if (anchor != Anchor.Pivot)
            {
                pivot -= rotation * new Vector3(shape.center.x, anchor == Anchor.Top ? shape.max.y : shape.min.y, shape.center.z);
            }
            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = shape.center + Vector3.Scale(shape.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 p = pivot + rotation * corner;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        /// <summary>
        /// Whether the ground buries a deck or floor. Never a dock's land end: the road there is
        /// cut down to the deck, lower than the ground the game generated.
        /// </summary>
        public static bool IsBuried(Part part, Frame frame, bool dock)
        {
            if (!part.Stands || (dock && part.Min.z < LandEndKept))
            {
                return false;
            }
            Vector2 at = frame.Flat(part.Centre.x, part.Centre.y);
            return Ground.Height(at.x, at.y) > frame.Height(part.Max.y) + Buried;
        }

        /// <summary>Weathers the parts, then writes every piece, pile, relic, chest, spawner and loose piece that is left.</summary>
        public static void Raise(Blueprint blueprint, List<Part> parts, Frame frame, Options options, System.Random rng, Result result)
        {
            result.Name = blueprint.name;
            result.Frame = frame;
            float condition = options.Edit ? 1f
                : float.IsNaN(options.Condition) ? Mathf.Lerp(0.05f, 0.95f, (float)rng.NextDouble()) : Mathf.Clamp01(options.Condition);
            result.Condition = condition;
            float decay = 1f - condition;

            // Out before the weather: decks under the ground, piles wholly in it.
            bool[] gone = new bool[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                Vector2 at = frame.Flat(part.Centre.x, part.Centre.y);
                gone[i] = IsBuried(part, frame, options.Dock) || (part.Role == Role.Pile && frame.Height(part.Max.y) < Ground.Height(at.x, at.y));
            }
            if (!options.Edit)
            {
                result.Removed = Weather(blueprint, parts, gone, options.Dock, decay, rng);
            }

            Placer placer = new Placer(frame, blueprint, condition, rng, options, result);
            for (int i = 0; i < parts.Count; i++)
            {
                if (!gone[i])
                {
                    placer.Part(parts[i]);
                }
            }
            ExtendPiles(parts, gone, frame, placer);

            List<BlueprintSpot> spots = new List<BlueprintSpot>();
            foreach (BlueprintSpot spot in blueprint.spots)
            {
                if (spot.kind == Spots.Dock)
                {
                    // Where it was joined to the dock: nothing goes there.
                    continue;
                }
                if (spot.kind == Spots.Stone)
                {
                    if (!result.HasStone)
                    {
                        result.HasStone = true;
                        result.Stone = frame.World(spot.Position);
                        result.StoneRotation = frame.Rotation * Quaternion.Euler(0f, spot.yaw, 0f);
                    }
                }
                else if (Held(parts, gone, frame, spot))
                {
                    spots.Add(spot);
                }
            }
            if (options.Edit)
            {
                // Each spot as a sign with its kind, for capture to read back.
                foreach (BlueprintSpot spot in blueprint.spots)
                {
                    placer.Sign(spot.kind, spot.Position, spot.yaw);
                }
            }
            else
            {
                Furnish(blueprint, parts, gone, spots, placer, options, decay, rng, result);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (!gone[i] && parts[i].Solid)
                {
                    result.Footprint.Add(frame.Flat(parts[i].Centre.x, parts[i].Centre.y));
                }
                if (!gone[i] && parts[i].Stands)
                {
                    Bounds box = new Bounds();
                    box.SetMinMax(parts[i].Min, parts[i].Max);
                    result.Decks.Add(box);
                }
            }
        }

        /// <summary>
        /// Which parts the weather took: deck pieces not holding anything up, walls, posts with
        /// their lamps, furniture, and roof pieces - those too that no standing wall or post is
        /// within the blueprint's roofReach of. How many went.
        /// </summary>
        private static int Weather(Blueprint blueprint, List<Part> parts, bool[] gone, bool dock, float decay, System.Random rng)
        {
            List<Vector2> held = new List<Vector2>();
            foreach (Part part in parts)
            {
                if (part.Role == Role.Post || part.Role == Role.Lamp || part.Role == Role.Deco)
                {
                    held.Add(part.Centre);
                }
            }
            foreach (BlueprintSpot spot in blueprint.spots)
            {
                held.Add(new Vector2(spot.Position.x, spot.Position.z));
            }
            int removed = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (gone[i])
                {
                    continue;
                }
                Part part = parts[i];
                double chance;
                switch (part.Role)
                {
                    case Role.Deck:
                        chance = (dock && part.Min.z < LandEndKept) || Near(held, part.Centre, Holds) ? 0.0 : decay * 0.35;
                        break;
                    case Role.Wall:
                    case Role.Door: chance = decay * 0.5; break;
                    case Role.Roof: chance = decay * 0.6; break;
                    case Role.Post: chance = decay * 0.5; break;
                    case Role.Deco: chance = decay * 0.4; break;
                    default: chance = 0.0; break;
                }
                if (rng.NextDouble() < chance)
                {
                    gone[i] = true;
                    removed++;
                }
            }
            List<Vector2> standing = new List<Vector2>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (!gone[i] && (parts[i].Role == Role.Wall || parts[i].Role == Role.Post))
                {
                    standing.Add(parts[i].Centre);
                }
            }
            for (int i = 0; i < parts.Count; i++)
            {
                if (gone[i])
                {
                    continue;
                }
                Part part = parts[i];
                bool falls = false;
                if (part.Role == Role.Roof)
                {
                    falls = !Near(standing, part.Centre, blueprint.RoofReach);
                }
                else if (part.Role == Role.Lamp)
                {
                    // The post it stands on: the nearest one below it.
                    for (int j = 0; j < parts.Count; j++)
                    {
                        if (parts[j].Role == Role.Post && gone[j] && parts[j].Min.y < part.Min.y
                            && (parts[j].Centre - part.Centre).sqrMagnitude < OnPost * OnPost)
                        {
                            falls = true;
                        }
                    }
                }
                if (falls)
                {
                    gone[i] = true;
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>The lowest pile of each column (the piles within 0.3 m of each other across), carried on down to the ground.</summary>
        private static void ExtendPiles(List<Part> parts, bool[] gone, Frame frame, Placer placer)
        {
            List<int> lowest = new List<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (gone[i] || parts[i].Role != Role.Pile)
                {
                    continue;
                }
                int column = lowest.FindIndex(k => (parts[k].Centre - parts[i].Centre).sqrMagnitude < 0.09f);
                if (column < 0)
                {
                    lowest.Add(i);
                }
                else if (parts[i].Min.y < parts[lowest[column]].Min.y)
                {
                    lowest[column] = i;
                }
            }
            foreach (int i in lowest)
            {
                Part pile = parts[i];
                Vector2 at = frame.Flat(pile.Centre.x, pile.Centre.y);
                float ground = Ground.Height(at.x, at.y);
                float height = Mathf.Max(0.5f, pile.Max.y - pile.Min.y);
                float bottom = pile.Min.y;
                for (int k = 1; k <= MaxPileStack && frame.Height(bottom) > ground - 0.3f; k++)
                {
                    placer.Extension(pile, height * k);
                    bottom -= height;
                }
            }
        }

        /// <summary>
        /// Whether what a spot needs is still there: for furniture on a wall or from a ceiling,
        /// the piece its sign was fixed to (a building part around its pivot); for anything else
        /// a deck or floor under it, or the ground.
        /// </summary>
        private static bool Held(List<Part> parts, bool[] gone, Frame frame, BlueprintSpot spot)
        {
            if (spot.kind == Spots.Wall || spot.kind == Spots.Hanging)
            {
                Vector3 pivot = SignPivot(spot);
                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i];
                    if (!gone[i] && part.Role != Role.Deco && part.Role != Role.Clutter && part.Role != Role.Lamp
                        && Inside(pivot, part.Min, part.Max, 0.3f))
                    {
                        return true;
                    }
                }
                return false;
            }
            return Standing(parts, gone, frame, spot.Position);
        }

        private static bool Inside(Vector3 p, Vector3 min, Vector3 max, float margin)
        {
            return p.x > min.x - margin && p.x < max.x + margin && p.y > min.y - margin && p.y < max.y + margin
                && p.z > min.z - margin && p.z < max.z + margin;
        }

        /// <summary>Where a spot's sign had its pivot: what a wall's or a ceiling's furniture is fixed by.</summary>
        private static Vector3 SignPivot(BlueprintSpot spot) => spot.Position - Quaternion.Euler(0f, spot.yaw, 0f) * Blueprints.SignBase();

        /// <summary>Whether a spot has something under it: a deck or floor still there, or the ground.</summary>
        private static bool Standing(List<Part> parts, bool[] gone, Frame frame, Vector3 spot)
        {
            Vector2 at = frame.Flat(spot.x, spot.z);
            if (Ground.Height(at.x, at.y) > frame.Height(spot.y) - 0.3f)
            {
                return true;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (!gone[i] && part.Stands && spot.x > part.Min.x - 0.3f && spot.x < part.Max.x + 0.3f
                    && spot.z > part.Min.z - 0.3f && spot.z < part.Max.z + 0.3f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The chest first (on a chest spot, else a standing deco spot), then furniture on the deco spots, then the enemies, then clutter.</summary>
        private static void Furnish(Blueprint blueprint, List<Part> parts, bool[] gone, List<BlueprintSpot> spots, Placer placer,
            Options options, float decay, System.Random rng, Result result)
        {
            if (rng.NextDouble() < options.ChestChance)
            {
                int at = Take(spots, Spots.Chest, rng);
                if (at < 0)
                {
                    at = Take(spots, Spots.Low, rng);
                }
                if (at < 0)
                {
                    at = Take(spots, Spots.High, rng);
                }
                Vector3 where = at >= 0 ? spots[at].Position : Free(parts, gone, options.Dock, rng);
                if (at >= 0)
                {
                    spots.RemoveAt(at);
                }
                result.Chest = placer.Loose(Chest(options.Biome), where, 90f * rng.Next(4), Anchor.Bottom);
            }
            foreach (BlueprintSpot spot in spots)
            {
                if (Spots.IsDeco(spot.kind) && rng.NextDouble() >= decay * 0.4f)
                {
                    placer.Deco(DecoFor(blueprint, spot.kind), spot);
                }
            }
            if (rng.NextDouble() < options.EnemyChance)
            {
                string[] spawners = Enemies(options.Biome);
                int count = 1 + rng.Next(3);
                for (int k = 0; k < count; k++)
                {
                    int at = Take(spots, Spots.Enemy, rng);
                    Vector3 where = at >= 0 ? spots[at].Position : Free(parts, gone, options.Dock, rng);
                    if (at >= 0)
                    {
                        spots.RemoveAt(at);
                    }
                    if (placer.Loose(spawners[rng.Next(spawners.Length)], where + Vector3.up * 0.5f, 0f, Anchor.Pivot))
                    {
                        result.Enemies++;
                    }
                }
            }
            string[] clutter = blueprint.clutter;
            if (clutter != null && clutter.Length > 0)
            {
                List<Vector2> taken = new List<Vector2>();
                foreach (BlueprintSpot spot in blueprint.spots)
                {
                    taken.Add(new Vector2(spot.Position.x, spot.Position.z));
                }
                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i];
                    if (gone[i] || part.Role != Role.Deck || (options.Dock && part.Min.z < LandEndKept) || Near(taken, part.Centre, Holds)
                        || rng.NextDouble() >= blueprint.clutterChance)
                    {
                        continue;
                    }
                    Vector2 jitter = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.8f;
                    Vector2 c = part.Centre + jitter;
                    placer.Loose(clutter[rng.Next(clutter.Length)], new Vector3(c.x, part.Max.y, c.y), (float)rng.NextDouble() * 360f, Anchor.Bottom);
                }
            }
        }

        /// <summary>A random spot of a kind, as an index into spots; -1 if none.</summary>
        private static int Take(List<BlueprintSpot> spots, string kind, System.Random rng)
        {
            int found = -1;
            int seen = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i].kind == kind && rng.Next(++seen) == 0)
                {
                    found = i;
                }
            }
            return found;
        }

        /// <summary>The top of a deck or floor still there, off a dock's land end, for a chest or an enemy without a spot.</summary>
        private static Vector3 Free(List<Part> parts, bool[] gone, bool dock, System.Random rng)
        {
            List<Vector3> free = new List<Vector3>();
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (!gone[i] && part.Stands && (!dock || part.Min.z >= LandEndKept))
                {
                    free.Add(new Vector3(part.Centre.x, part.Max.y, part.Centre.y));
                }
            }
            return free.Count > 0 ? free[rng.Next(free.Count)] : Vector3.zero;
        }

        private static bool Near(List<Vector2> points, Vector2 at, float radius)
        {
            foreach (Vector2 p in points)
            {
                if ((p - at).sqrMagnitude < radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Writes pieces in the frame: measured, anchored, worn, snowed over, marked.</summary>
        private sealed class Placer
        {
            private readonly Frame frame;
            private readonly Blueprint blueprint;
            private readonly float condition;
            private readonly System.Random rng;
            private readonly Options options;
            private readonly Result result;

            public Placer(Frame frame, Blueprint blueprint, float condition, System.Random rng, Options options, Result result)
            {
                this.frame = frame;
                this.blueprint = blueprint;
                this.condition = condition;
                this.rng = rng;
                this.options = options;
                this.result = result;
            }

            public void Part(Part part)
            {
                Place(part.Prefab, part.Pivot, part.Piece.Rotation, part.Role, part.Role != Role.Clutter);
            }

            /// <summary>A copy of a pile below it, depth further down.</summary>
            public void Extension(Part pile, float depth)
            {
                ZDO zdo = Place(pile.Prefab, pile.Pivot - Vector3.up * depth, pile.Piece.Rotation, Role.Pile, true);
                zdo?.Set(ExtensionKey, true);
            }

            /// <summary>
            /// A piece of furniture from those that fit the spot, as a relic, turned as its sign:
            /// standing with its foot on the sign's, or fixed to a wall or ceiling by its pivot
            /// where the sign's was, as the hammer fixes both.
            /// </summary>
            public void Deco(List<string> names, BlueprintSpot spot)
            {
                if (names.Count == 0)
                {
                    return;
                }
                GameObject relic = Relics.Get(names[rng.Next(names.Count)]);
                if (relic == null)
                {
                    return;
                }
                Quaternion turn = Quaternion.Euler(0f, spot.yaw, 0f);
                Vector3 pivot = SignPivot(spot);
                if (Spots.IsStanding(spot.kind))
                {
                    Box(relic, spot.Position, turn, Anchor.Bottom, out pivot, out Vector3 _, out Vector3 _);
                }
                Place(relic, pivot, turn, Role.Deco, true);
            }

            /// <summary>A chest, a spawner or a loose piece.</summary>
            public bool Loose(string name, Vector3 at, float yaw, Anchor anchor)
            {
                GameObject prefab = Prefab(name);
                if (prefab == null)
                {
                    return false;
                }
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                Box(prefab, at, turn, anchor, out Vector3 pivot, out Vector3 _, out Vector3 _);
                Place(prefab, pivot, turn, Role.Clutter, prefab.GetComponent<CreatureSpawner>() == null);
                return true;
            }

            /// <summary>A sign reading the spot's kind, standing at it.</summary>
            public void Sign(string kind, Vector3 at, float yaw)
            {
                GameObject prefab = Prefab(SignPrefab);
                if (prefab == null)
                {
                    return;
                }
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                Box(prefab, at, turn, Anchor.Bottom, out Vector3 pivot, out Vector3 _, out Vector3 _);
                ZDO zdo = Place(prefab, pivot, turn, Role.Keep, false);
                zdo?.Set(ZDOVars.s_text, kind);
            }

            private ZDO Place(GameObject prefab, Vector3 pivot, Quaternion rotation, Role role, bool weather)
            {
                ZDO zdo = Landings.Spawn(prefab, frame.World(pivot), frame.Rotation * rotation);
                zdo.Set(RoleKey, role.ToString().ToLowerInvariant());
                if (options.Creator != 0L)
                {
                    zdo.Set(ZDOVars.s_creator, options.Creator);
                }
                if (options.Building)
                {
                    zdo.Set(BuildingKey, true);
                }
                WearNTear wear = prefab.GetComponent<WearNTear>();
                if (wear != null)
                {
                    if (weather && !options.Edit)
                    {
                        float health = Mathf.Clamp(condition + Mathf.Lerp(-0.3f, 0.2f, (float)rng.NextDouble()), 0.03f, 1f);
                        zdo.Set(ZDOVars.s_health, health * wear.m_health);
                    }
                    if (blueprint.preSnow)
                    {
                        zdo.Set(ZDOVars.s_preSnow, true);
                    }
                }
                result.Placed.Add(zdo.m_uid);
                return zdo;
            }
        }

        /// <summary>
        /// The furniture of a blueprint's deco list that fits a kind of spot, else of
        /// <see cref="DefaultDeco"/>: the list may hold nothing for a wall or a ceiling.
        /// </summary>
        private static List<string> DecoFor(Blueprint blueprint, string kind)
        {
            List<string> fits = new List<string>();
            foreach (string[] names in new[] { blueprint.deco, DefaultDeco })
            {
                foreach (string name in names ?? new string[0])
                {
                    GameObject prefab = Prefab(name);
                    if (prefab != null && FitsSpot(kind, prefab))
                    {
                        fits.Add(name);
                    }
                }
                if (fits.Count > 0)
                {
                    break;
                }
            }
            return fits;
        }

        /// <summary>Furniture for a deco spot whose blueprint lists none of its kind.</summary>
        internal static readonly string[] DefaultDeco =
        {
            "piece_banner01", "piece_banner02", "piece_banner03", "piece_banner04", "piece_banner05", "piece_banner06",
            "piece_brazierceiling01",
            "piece_table", "piece_bench01", "piece_chair", "rug_deer",
        };

        /// <summary>
        /// Whether a piece of furniture fits a kind of deco spot: one the hammer fixes to ceilings
        /// only (<c>m_inCeilingOnly</c>) hangs, one it keeps off floors (<c>m_notOnFloor</c>)
        /// goes on a wall, the rest stands, no higher than a wall (deco_h1) or two (deco_h2);
        /// none wider than a tile and a bit, a rug a little more.
        /// </summary>
        internal static bool FitsSpot(string kind, GameObject prefab)
        {
            Piece piece = prefab.GetComponent<Piece>();
            bool hangs = piece != null && piece.m_inCeilingOnly;
            bool onWall = piece != null && piece.m_notOnFloor && !hangs;
            Bounds shape = Shape(prefab);
            float width = Mathf.Max(shape.size.x, shape.size.z);
            switch (kind)
            {
                case Spots.Hanging: return hangs && width <= MaxWidth;
                case Spots.Wall: return onWall && width <= MaxWidth;
                case Spots.Low:
                case Spots.High:
                    float tallest = (kind == Spots.Low ? WallHeight : 2f * WallHeight) + 0.1f;
                    return !hangs && !onWall && shape.size.y <= tallest && width <= (shape.size.y < RugHeight ? MaxRugWidth : MaxWidth);
                default: return false;
            }
        }

        /// <summary>What <c>docks capture</c> turns back into a spot: a sign of the game's.</summary>
        public const string SignPrefab = "sign";

        /// <summary>The treasure chest a biome's harbours may hold, as its own locations have it.</summary>
        public static string Chest(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return "TreasureChest_blackforest";
                case Heightmap.Biome.Swamp: return "TreasureChest_swamp";
                case Heightmap.Biome.Mountain: return "TreasureChest_mountains";
                case Heightmap.Biome.Plains: return "TreasureChest_heath";
                case Heightmap.Biome.Mistlands: return "TreasureChest_dvergrtown";
                case Heightmap.Biome.AshLands: return "TreasureChest_ashland_stone";
                case Heightmap.Biome.DeepNorth: return "TreasureChest_deepnorth_village";
                default: return "TreasureChest_meadows";
            }
        }

        /// <summary>
        /// The creature spawners a biome's harbours may hold: each spawns its creature once, when a
        /// player comes within 60 m, and never again (their respawn time is 0), as at a stone tower.
        /// </summary>
        public static string[] Enemies(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return new[] { "Spawner_Skeleton" };
                case Heightmap.Biome.Swamp: return new[] { "Spawner_Draugr", "Spawner_Draugr_Ranged", "Spawner_Skeleton_Swamp" };
                case Heightmap.Biome.Mountain: return new[] { "Spawner_Skeleton_Mountains" };
                case Heightmap.Biome.Plains: return new[] { "Spawner_Goblin", "Spawner_GoblinArcher" };
                case Heightmap.Biome.Mistlands: return new[] { "Spawner_Seeker" };
                case Heightmap.Biome.AshLands: return new[] { "Spawner_Charred", "Spawner_Charred_Archer" };
                case Heightmap.Biome.DeepNorth: return new[] { "Spawner_GoblinDeepNorth" };
                default: return new[] { "Spawner_Skeleton_Meadows" };
            }
        }

        /// <summary>The land's biome at a point, or behind it away from the sea: a shore point itself can be the Ocean's.</summary>
        public static Heightmap.Biome LandBiome(Vector2 at, Vector2 seaward)
        {
            Vector2 back = seaward.normalized;
            foreach (float distance in new[] { 0f, 4f, 10f, 20f })
            {
                Vector2 p = at - back * distance;
                Heightmap.Biome biome = WorldGenerator.instance.GetBiome(p.x, p.y);
                if (biome != Heightmap.Biome.Ocean && biome != Heightmap.Biome.None)
                {
                    return biome;
                }
            }
            return Heightmap.Biome.Meadows;
        }

        // ---- Prefabs and their shapes ----

        private static readonly HashSet<string> missing = new HashSet<string>();
        private static readonly Dictionary<GameObject, Bounds> shapes = new Dictionary<GameObject, Bounds>();

        public static GameObject Prefab(string name)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null && missing.Add(name))
            {
                Debug.LogWarning("[OdinsPaths] Harbours: the game has no prefab " + name + " - left out.");
            }
            return prefab == null || prefab.GetComponent<ZNetView>() == null ? null : prefab;
        }

        /// <summary>
        /// A prefab's solid shape in its own frame: the box around its colliders (triggers and
        /// inactive children aside), or around its meshes if it has none. Measured on the prefab
        /// itself, never instantiated, and kept.
        /// </summary>
        internal static Bounds Shape(GameObject prefab)
        {
            if (shapes.TryGetValue(prefab, out Bounds known))
            {
                return known;
            }
            Transform root = prefab.transform;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            bool any = false;
            Bounds bounds = new Bounds();
            void Take(Matrix4x4 m, Bounds local)
            {
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || !Active(collider.transform, root))
                {
                    continue;
                }
                Matrix4x4 m = toRoot * collider.transform.localToWorldMatrix;
                switch (collider)
                {
                    case BoxCollider box:
                        Take(m, new Bounds(box.center, box.size));
                        break;
                    case SphereCollider sphere:
                        Take(m, new Bounds(sphere.center, Vector3.one * sphere.radius * 2f));
                        break;
                    case CapsuleCollider capsule:
                        Vector3 size = Vector3.one * capsule.radius * 2f;
                        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                        Take(m, new Bounds(capsule.center, size));
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        Take(m, mesh.sharedMesh.bounds);
                        break;
                }
            }
            if (!any)
            {
                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null && Active(filter.transform, root))
                    {
                        Take(toRoot * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
                    }
                }
            }
            if (!any)
            {
                bounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            }
            shapes[prefab] = bounds;
            return bounds;
        }

        private static bool Active(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
