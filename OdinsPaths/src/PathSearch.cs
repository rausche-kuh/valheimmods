using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>A location's footprint the search keeps out of, and levelling leaves alone.</summary>
    internal struct Circle
    {
        public Vector2 Center;
        public float Radius;

        public bool Contains(Vector2 p) => (p - Center).sqrMagnitude < Radius * Radius;
    }

    /// <summary>
    /// Where a search may set out from, and what being there already costs. A network point
    /// carries a share of its route to the nearest hub, so the search weighs branching off there
    /// against a route of its own from the hub - a low share makes paths share their trunks.
    /// </summary>
    internal struct Start
    {
        public Vector2 Position;
        /// <summary>What the search starts it at: alpha x HubCost for a road's point, 0 for a hub.</summary>
        public float Cost;
        /// <summary>The whole cost of its route from the hub, which a road branching off here carries on.</summary>
        public float HubCost;

        public Start(Vector2 position, float cost = 0f, float hubCost = 0f)
        {
            Position = position;
            Cost = cost;
            HubCost = hubCost;
        }

        public static List<Vector2> Positions(List<Start> starts)
        {
            List<Vector2> result = new List<Vector2>(starts.Count);
            foreach (Start start in starts)
            {
                result.Add(start.Position);
            }
            return result;
        }
    }

    /// <summary>
    /// The least-cost route from the cheapest of several starts to the cheapest of several goals
    /// - one search finds which boss altar of a kind is easiest to reach from the network, instead of
    /// one search per altar -, over a grid of square cells with heights as the game builds them
    /// (<see cref="Ground"/>) - so it works on zones nobody has loaded. The
    /// cost of a step is its length times how hard it is: climbing gets expensive fast, fords cost
    /// a little, a river is swum at a price, the sea (deep water in the Ocean biome, which is
    /// where the ground was under the water before the rivers were cut) costs more per metre than
    /// any ground and a big lump sum to set out on and again to come ashore - so a path stays on
    /// land wherever land leads, crosses at a strait, and does not stop at an island on the way -,
    /// bogs and the Mistlands are avoided, locations and buildings are
    /// walked around, and a slow noise makes flat ground wander instead of running straight. The
    /// search is A* with 16 neighbours (the 8 plus the knight's moves), so a straight stretch can
    /// run at any of 16 angles instead of zig-zagging at 45°.
    ///
    /// The cell size is the caller's: a coarse pass over a wide ellipse finds the line, and a fine
    /// pass inside a <see cref="Corridor"/> around it finds the path (PathLayer). A cell 16 m or
    /// wider is sampled at five points, so a river or a shore inside it counts by its share rather
    /// than by luck. Without a bounds rule the search stays inside an ellipse around each start
    /// and each goal; whether a cell is inside is decided once, when it is first met.
    ///
    /// <see cref="Run"/> is a coroutine that runs the search on a thread of its own and waits for
    /// it: everything it reads is either built before it starts (locations, buildings, bounds, the
    /// survey) or the world generator, which the game's own heightmap thread reads the same way
    /// (its river cache is locked, the rest is arithmetic). Off (<c>[Performance] SearchThread</c>),
    /// the work is spread over frames instead, within the per-frame budget.
    /// </summary>
    internal sealed class PathSearch
    {
        /// <summary>The cell size a path is finally searched at; the terrain is laid from it.</summary>
        public const float FineCell = 4f;

        /// <summary>A grade (rise / run) that costs double. The cost grows with its square.</summary>
        private const float ComfortGrade = 0.15f;
        /// <summary>Above this grade (about 31°) a step costs twenty times more again.</summary>
        private const float MaxGrade = 0.6f;
        /// <summary>Water up to this deep is a ford.</summary>
        internal const float FordDepth = 1.5f;
        /// <summary>
        /// Water deeper than this is the sea whatever the biome: the game cuts its rivers down to
        /// 24-28 m, at most 6 m under the water, while the Ocean biome is only the open sea - a
        /// fjord, a strait or a bay counts as the land around it (<c>WorldGenerator.GetBiome</c>
        /// goes by the height before the rivers). Roads swam fjords to islands (seen in game 2026-09-25).
        /// </summary>
        internal const float RiverDepth = 6.5f;
        private const float FordFactor = 3f;
        /// <summary>Swimming a river (deep water outside the Ocean biome), per metre.</summary>
        private const float SwimFactor = 4f;
        /// <summary>
        /// The sea, per metre: the SeaCost setting, default 2. Dearer than any ground, since
        /// players follow a path on foot: land is taken wherever it leads, and a crossing is made
        /// at its narrowest. At parity the sea was a highway - flat ground still carries the
        /// wander and the slope, so water is cheaper than any land - and the path never came
        /// ashore (tested 2026-09-24).
        /// </summary>
        private readonly float seaFactor;
        /// <summary>
        /// What setting out on the sea costs, in metres of flat walking: the BoardingCost
        /// setting, default 1000, since a boat has to be built. Together with the landing this is
        /// what a crossing pays on top of its length: a 200 m strait beats a land route only when
        /// that is more than about 1.6 km longer.
        /// </summary>
        private readonly float boardingCost;
        /// <summary>
        /// What coming ashore costs: the LandingCost setting, default 400, cheaper than boarding
        /// since stopping is easy. An island on the way is worth landing on only when the walk
        /// across it saves more than a landing and the boarding after it, about 1.4 km of sea.
        /// </summary>
        private readonly float landingCost;
        /// <summary>Cells this wide and wider are sampled at the centre and an X a third of the cell out.</summary>
        private const float SampledCell = 16f;
        /// <summary>
        /// The Swamp, per metre: the SwampCost setting, default 3. It is flat and at water
        /// level, so at 1.5 it was the cheapest ground there is and paths sought it out
        /// (tested 2026-09-24).
        /// </summary>
        private readonly float swampFactor;
        /// <summary>
        /// The Mistlands: cliffs and terraces, and the rock formations the search cannot see. A
        /// road goes in only where it has to - the Queen's entrance - and at the edge nearest it.
        /// </summary>
        private const float MistlandsFactor = 8f;
        private const float DeepNorthFactor = 1.2f;
        /// <summary>
        /// The Ashlands' ground, where it is not lava: walkable, but nobody lingers - a road
        /// crosses it to Fader's altar and does not take it as a shortcut.
        /// </summary>
        private const float AshlandsFactor = 2f;
        /// <summary>
        /// The vegetation mask's alpha above which the Ashlands ground is lava, as
        /// <c>ZoneSystem.IsLavaPreHeightmap</c> has it. Lava is never crossed: a fine cell on it is
        /// impassable, a wide cell half or more on it too, and a wide cell less on it pays for its share.
        /// </summary>
        private const float LavaValue = 0.6f;
        private const float LavaFactor = 20f;
        private const float LocationFactor = 10f;
        /// <summary>
        /// A cell near a building. High rather than impassable: a start inside a base has to get
        /// out, and the terrain writer leaves the ground beside the pieces alone anyway.
        /// </summary>
        private const float StructureFactor = 40f;
        /// <summary>How much the slow noise bends flat ground: 0.3 = each cell costs 0.7 to 1.3.</summary>
        private const float WanderStrength = 0.3f;
        private const float WanderScale = 150f;
        /// <summary>The search stays inside an ellipse around each start and the goal.</summary>
        private const float EllipseStretch = 1.4f;
        private const float EllipseMargin = 300f;
        /// <summary>Of the starts in a square this wide, only the cheapest draws an ellipse.</summary>
        private const float AnchorBucket = 100f;
        private const int MaxExpanded = 3000000;
        /// <summary>
        /// Cells expanded between looks at the clock. A new cell samples the ground and checks the
        /// locations nearby, so 256 of them could overrun the frame's budget by several ms.
        /// </summary>
        private const int ClockEvery = 32;
        /// <summary>The cell of the survey that guides a search (<see cref="Survey"/>).</summary>
        public const float SurveyCell = 128f;
        /// <summary>
        /// How much of the survey's cost to the goal the heuristic trusts. Below 1, since a 128 m
        /// cell can step over a strait or a gully the real search has to go round; not much below,
        /// since every point of slack lets the search flood sideways again.
        /// </summary>
        private const float SurveyWeight = 0.9f;
        /// <summary>
        /// What turning costs at the fine cell, in metres of easy walking per radian² of a step's
        /// turn against the way the route came over the last <see cref="TurnLookBack"/>, less
        /// <see cref="TurnFree"/>. Squared, so a turn spread over a wide arc costs far less than the
        /// same turn made at once: a tight hairpin pays about 45 m, one of 5 m radius about 23, one
        /// of 10 m about 5. So the search takes fewer hairpins, wider and farther apart: stacked
        /// tight ones on the steepest slopes had legs a few metres apart, and each leg's levelling
        /// dug into the other's (seen in game 2026-09-25, third lay).
        /// </summary>
        private const float TurnWeight = 4f;
        /// <summary>How far back the route's heading is taken from, in metres.</summary>
        private const float TurnLookBack = 8f;
        /// <summary>A turn this small (23°) is free: a straight line at an angle the 16 steps do not have zig-zags this much.</summary>
        private const float TurnFree = 0.4f;

        private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1, 2, 2, -2, -2 };
        private static readonly int[] StepY = { 0, 0, 1, -1, 1, -1, 1, -1, 2, -2, 2, -2, 1, -1, 1, -1 };
        /// <summary>Each step's length in cells.</summary>
        private static readonly float[] StepLength = Lengths();

        private static float[] Lengths()
        {
            float[] lengths = new float[StepX.Length];
            for (int d = 0; d < lengths.Length; d++)
            {
                lengths[d] = Mathf.Sqrt(StepX[d] * StepX[d] + StepY[d] * StepY[d]);
            }
            return lengths;
        }

        private struct Cell
        {
            /// <summary>The walking surface: the generated ground, or the water level over water; a wide cell's mean.</summary>
            public float Height;
            /// <summary>Biome, wander and location multiplier; 0 = impassable.</summary>
            public float Factor;
            /// <summary>The share of the cell under water no deeper than a ford.</summary>
            public float Shallow;
            /// <summary>The share deeper than a ford outside the Ocean biome, but no deeper than a river: swum.</summary>
            public float River;
            /// <summary>The share that is sea (<see cref="IsSea"/>): boated. 0 or 1 for a fine cell.</summary>
            public float Sea;
            /// <summary>The share on lava, in the Ashlands.</summary>
            public float Lava;
            /// <summary>Near a harbour (<see cref="Berths"/>): boarding or landing here is cheaper.</summary>
            public bool Berth;
            /// <summary>Outside the bounds: never sampled, never entered.</summary>
            public bool Outside;
            /// <summary>The search's own state, kept with the ground in one table (<see cref="CellMap"/>).</summary>
            public bool Reached;
            public bool Closed;
            public bool HasParent;
            /// <summary>What reaching the cell cost, once reached.</summary>
            public float Cost;
            public long Parent;
        }

        public readonly float CellSize;
        private readonly List<Start> starts;
        private readonly List<Vector2> goals;
        /// <summary>Each goal's cell, to the goal's index.</summary>
        private readonly Dictionary<long, int> goalKeys = new Dictionary<long, int>();
        private readonly List<Circle> locations;
        private readonly Structures structures;
        /// <summary>Where the search may go; null means the ellipses.</summary>
        private readonly Func<Vector2, bool> bounds;
        /// <summary>A cell centre this close to a structure is near it; see the constructor.</summary>
        private readonly float structureRadius;
        private readonly float waterLevel;
        private readonly Vector2 wanderOffset;
        /// <summary>Per anchor and goal, anchor-major.</summary>
        private readonly float[] ellipseLimit;
        private readonly LocationGrid locationGrid;
        /// <summary>The world the search runs in; once it is not the game's any more, the search stops.</summary>
        private readonly WorldGenerator generator;
        private int sampled;

        /// <summary>Every cell met: its ground, its cost, its parent, whether it is closed.</summary>
        private readonly CellMap cells = new CellMap();
        /// <summary>The start each start cell was seeded from.</summary>
        private readonly Dictionary<long, Start> startCells = new Dictionary<long, Start>();
        /// <summary>The starts the ellipses are drawn around (<see cref="Anchors"/>).</summary>
        private readonly List<Start> anchors;
        private readonly MinHeap open = new MinHeap();

        /// <summary>The route, start to goal, as cell centres; null until found or on failure.</summary>
        public List<Vector2> Result { get; private set; }
        /// <summary>The goal the route reached - the cheapest of them - as given.</summary>
        public Vector2 Goal { get; private set; }
        /// <summary>What the route costs from its start, in metres of easy walking, without the start's own cost.</summary>
        public float RouteCost { get; private set; }
        /// <summary>The cost the route's start carried into the search.</summary>
        public float StartCost { get; private set; }
        /// <summary>The start the route set out from - of several in one cell, the cheapest.</summary>
        public Start Origin { get; private set; }
        /// <summary>Per cell of the route, what reaching it cost from the route's start, without the start's own cost.</summary>
        public List<float> RouteCosts { get; private set; }

        /// <summary>A goal a collecting search settled, and its route there.</summary>
        internal struct Settled
        {
            public int Goal;
            public List<Vector2> Route;
            public Start Origin;
            public List<float> Costs;
        }

        /// <summary>
        /// With <see cref="Collect"/> set: every goal settled for no more than the limit, in the
        /// order settled, cheapest first. Nothing else is set; no goal at all is no failure.
        /// </summary>
        public readonly List<Settled> Collected = new List<Settled>();
        private float collectUpTo;
        /// <summary>
        /// A side path's share of the boarding and the landing: it may cross a strait or a fjord to a
        /// point of interest on the far side, or set out from a main road's sea crossing to one on
        /// the shore beside it, and gets a minor harbour where it lands (<see cref="Landings"/>).
        /// At the full lump sums no side path within its prize ever could (until 2026-09-25 it
        /// never tried: sea was impassable to it).
        /// </summary>
        private const float SideCrossing = 0.15f;

        /// <summary>
        /// Instead of stopping at the first goal, go on until the cheapest open cell costs more than
        /// the limit and collect every goal settled by then - the spurs of a main road, each a point
        /// of interest cheap enough to walk to from it. Before <see cref="Run"/>.
        /// </summary>
        public void Collect(float limit)
        {
            collectUpTo = limit;
        }

        /// <summary>
        /// Roughly how far along the search is, 0 to 1, for the progress bar: for A*, how much
        /// closer to the goal the nearest cell expanded yet is than the start was; when collecting,
        /// how far the settled cost has climbed toward the limit. It jumps and stalls when the
        /// route has to go round something, but it only goes forward.
        /// </summary>
        public float Fraction { get; private set; }
        private float startHeuristic = -1f;

        /// <summary>For A*: the expanded cell nearest the goal by the heuristic, where the search has got to; null before.</summary>
        public Vector2? Frontier { get; private set; }

        /// <summary>The cost to the goal as a survey has it (-1: unknown), or null for the straight distance.</summary>
        private Func<Vector2, float> guide;

        /// <summary>
        /// A survey (<see cref="Survey"/>) to guide this search, before <see cref="Run"/>. The straight
        /// distance knows nothing of the sea's lump sums or the Mistlands' price, so without one a
        /// search that has to pay them floods its whole ellipse before it reaches the goal - 20 s
        /// and more at 32 m for Moder, never done for the Queen (2026-09-25).
        /// </summary>
        public void Guide(PathSearch survey)
        {
            guide = survey.CostOf;
        }

        /// <summary>
        /// The harbours on the way, before <see cref="Run"/>: the berths of the game's
        /// (<see cref="Ports"/>, within <see cref="Ports.Reach"/>) and the stones of the mod's own
        /// (within <see cref="Harbours.Merge"/>, where a landing joins the harbour). A step boarding
        /// or landing inside one pays <see cref="Ports.Share"/> of the lump sum, so a crossing sets
        /// out from a harbour that is there rather than a new one beside it.
        /// </summary>
        public List<Circle> Berths
        {
            set
            {
                // Half a cell more: a cell counts if any of it may reach.
                berths = value == null || value.Count == 0 ? null
                    : new LocationGrid(value.ConvertAll(c => new Circle { Center = c.Center, Radius = c.Radius + CellSize * 0.5f }));
            }
        }
        private LocationGrid berths;

        /// <summary>Whether a point is inside this search's bounds - its ellipses, or the rule it was given.</summary>
        public bool Contains(Vector2 p) => InBounds(p);

        /// <summary>
        /// The cost of reaching the nearest of these points from everywhere inside the bounds, on
        /// a coarse grid: a Dijkstra from them without goal or limit, read back with
        /// <see cref="CostOf"/>. Run backwards from a search's goals, it is the cost to the goal the
        /// search is then guided by. No locations or buildings: at this cell they would only
        /// blur the estimate upward. A step's lump sums run the other way (a landing where the
        /// search boards), which only makes the estimate low.
        /// </summary>
        public static PathSearch Survey(List<Vector2> from, Func<Vector2, bool> bounds, RoadKind kind)
        {
            List<Start> starts = new List<Start>();
            foreach (Vector2 p in from)
            {
                starts.Add(new Start(p));
            }
            PathSearch survey = new PathSearch(starts, new List<Vector2>(), new List<Circle>(), new Structures(), kind, SurveyCell, bounds);
            survey.Collect(float.MaxValue);
            return survey;
        }
        public string Failure { get; private set; }
        /// <summary>Cells expanded before the search gives up; lower it before <see cref="Run"/> for a search that may flood.</summary>
        public int Limit = MaxExpanded;
        public int Expanded { get; private set; }
        public int Sampled => sampled;
        /// <summary>Wall time from the first cell to the last, frames in between included.</summary>
        public double Milliseconds { get; private set; }
        /// <summary>The time actually spent searching, within the per-frame budget.</summary>
        public double WorkMilliseconds { get; private set; }

        public PathSearch(List<Start> starts, List<Vector2> goals, List<Circle> locations, Structures structures,
            RoadKind kind, float cell = FineCell, Func<Vector2, bool> bounds = null)
        {
            CellSize = cell;
            seaFactor = OdinsPathsPlugin.SeaCost.Value;
            float crossing = kind == RoadKind.Spur ? SideCrossing : 1f;
            boardingCost = OdinsPathsPlugin.BoardingCost.Value * crossing;
            landingCost = OdinsPathsPlugin.LandingCost.Value * crossing;
            swampFactor = OdinsPathsPlugin.SwampCost.Value;
            this.starts = starts;
            this.goals = goals;
            this.locations = locations;
            this.structures = structures;
            this.bounds = bounds;
            // The trail runs along the steps between cell centres, and the longest step (a
            // knight's move) passes half its length closer to a point than its ends do.
            float keep = kind.Reach + Structures.PieceReach;
            float halfStep = cell * Mathf.Sqrt(5f) * 0.5f;
            structureRadius = Mathf.Sqrt(keep * keep + halfStep * halfStep);
            waterLevel = ZoneSystem.instance.m_waterLevel;
            generator = WorldGenerator.instance;
            locationGrid = new LocationGrid(locations);
            System.Random random = new System.Random(generator.GetSeed());
            wanderOffset = new Vector2(random.Next(1000, 9000), random.Next(1000, 9000));
            anchors = Anchors(starts, goals);
            ellipseLimit = new float[anchors.Count * goals.Count];
            for (int s = 0; s < anchors.Count; s++)
            {
                for (int g = 0; g < goals.Count; g++)
                {
                    ellipseLimit[s * goals.Count + g] = EllipseLimit(anchors[s].Position, goals[g]);
                }
            }
            for (int g = 0; g < goals.Count; g++)
            {
                long key = Key(Mathf.RoundToInt(goals[g].x / CellSize), Mathf.RoundToInt(goals[g].y / CellSize));
                if (!goalKeys.ContainsKey(key))
                {
                    goalKeys[key] = g;
                }
            }
        }

        /// <summary>A cell is searched if its distances to this start and the goal add up to no more.</summary>
        public static float EllipseLimit(Vector2 start, Vector2 goal)
        {
            return Vector2.Distance(start, goal) * EllipseStretch + EllipseMargin;
        }

        /// <summary>
        /// The starts worth drawing a search ellipse around - a network is thousands of points, and
        /// every cell, location and building would be checked against each: one start per
        /// <see cref="AnchorBucket"/> square (its cheapest), and of those only the ones that could
        /// still win for some goal - whose cost plus straight distance to it is within the ellipse
        /// of the start that looks best for that goal. The search still sets out from every start
        /// inside the ellipses; this only draws them.
        /// </summary>
        public static List<Start> Anchors(List<Start> starts, List<Vector2> goals)
        {
            if (starts.Count <= 16)
            {
                return starts;
            }
            Dictionary<long, Start> buckets = new Dictionary<long, Start>();
            foreach (Start start in starts)
            {
                long key = Key(Mathf.FloorToInt(start.Position.x / AnchorBucket), Mathf.FloorToInt(start.Position.y / AnchorBucket));
                if (!buckets.TryGetValue(key, out Start kept) || start.Cost < kept.Cost)
                {
                    buckets[key] = start;
                }
            }
            float[] limit = new float[goals.Count];
            for (int g = 0; g < goals.Count; g++)
            {
                float best = float.MaxValue;
                foreach (Start start in buckets.Values)
                {
                    best = Mathf.Min(best, start.Cost + Vector2.Distance(start.Position, goals[g]));
                }
                limit[g] = best * EllipseStretch + EllipseMargin;
            }
            List<Start> result = new List<Start>();
            foreach (Start start in buckets.Values)
            {
                for (int g = 0; g < goals.Count; g++)
                {
                    if (start.Cost + Vector2.Distance(start.Position, goals[g]) <= limit[g])
                    {
                        result.Add(start);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// The search, as a coroutine: on its own thread, the frames going on meanwhile, or - with
        /// <c>SearchThread</c> off - on the main thread within budgetMs per frame.
        /// </summary>
        public IEnumerator Run(float budgetMs)
        {
            return OdinsPathsPlugin.SearchThread.Value ? Threaded() : Steps(budgetMs);
        }

        private IEnumerator Threaded()
        {
            return Worker.Run(() =>
            {
                IEnumerator steps = Steps(float.MaxValue);
                while (steps.MoveNext())
                {
                }
            }, error =>
            {
                Result = null;
                Failure = "the search failed: " + error.Message;
            });
        }

        private IEnumerator Steps(float budgetMs)
        {
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch frame = Stopwatch.StartNew();
            foreach (Start start in starts)
            {
                // A network has hundreds of starts, and each one's cell is sampled.
                if (frame.Elapsed.TotalMilliseconds > budgetMs)
                {
                    WorkMilliseconds += frame.Elapsed.TotalMilliseconds;
                    yield return null;
                    frame.Restart();
                }
                Seed(start);
            }

            while (open.Count > 0)
            {
                if (Expanded % ClockEvery == 0 && WorldGenerator.instance != generator)
                {
                    Failure = "the world was left";
                    yield break;
                }
                if (Expanded % ClockEvery == 0 && frame.Elapsed.TotalMilliseconds > budgetMs)
                {
                    WorkMilliseconds += frame.Elapsed.TotalMilliseconds;
                    yield return null;
                    frame.Restart();
                }
                long current = open.Pop();
                if (!Close(current, out Cell from))
                {
                    continue;
                }
                float fromCost = from.Cost;
                if (collectUpTo > 0f && fromCost > collectUpTo)
                {
                    break;
                }
                Advance(current, fromCost);
                if (goalKeys.TryGetValue(current, out int reachedGoal))
                {
                    List<Vector2> route = Rebuild(current);
                    long first = Key(Mathf.RoundToInt(route[0].x / CellSize), Mathf.RoundToInt(route[0].y / CellSize));
                    if (collectUpTo > 0f)
                    {
                        // Every goal settled within the limit; the search goes on past it.
                        Collected.Add(new Settled
                        {
                            Goal = reachedGoal,
                            Route = route,
                            Origin = startCells[first],
                            Costs = CostsAlong(route),
                        });
                    }
                    else
                    {
                        Result = route;
                        Fraction = 1f;
                        Goal = goals[reachedGoal];
                        Origin = startCells[first];
                        StartCost = CostAt(first);
                        RouteCost = fromCost - StartCost;
                        RouteCosts = CostsAlong(route);
                        Milliseconds = total.Elapsed.TotalMilliseconds;
                        WorkMilliseconds += frame.Elapsed.TotalMilliseconds;
                        yield break;
                    }
                }
                if (++Expanded > Limit)
                {
                    break;
                }
                Expand(current, from);
            }
            Milliseconds = total.Elapsed.TotalMilliseconds;
            WorkMilliseconds += frame.Elapsed.TotalMilliseconds;
            Fraction = 1f;
            if (collectUpTo > 0f)
            {
                yield break;
            }
            Failure ??= Expanded > Limit
                ? "gave up after " + Limit + " cells"
                : "no way through - water, lava or the world's edge is in the way";
        }

        /// <summary>A start's cell opened at the start's cost, unless outside, impassable, or opened cheaper.</summary>
        private void Seed(Start start)
        {
            int i = Mathf.RoundToInt(start.Position.x / CellSize);
            int j = Mathf.RoundToInt(start.Position.y / CellSize);
            long key = Key(i, j);
            ref Cell cell = ref cells.At(GetCell(i, j));
            if (cell.Outside || cell.Factor <= 0f || (cell.Reached && cell.Cost <= start.Cost))
            {
                return;
            }
            cell.Cost = start.Cost;
            cell.Reached = true;
            startCells[key] = start;
            open.Push(start.Cost + Heuristic(i, j), key);
        }

        /// <summary>Closes a popped cell and hands out a copy of it; false if it was closed already (a stale heap entry).</summary>
        private bool Close(long key, out Cell cell)
        {
            ref Cell popped = ref cells.At(cells.IndexOf(key));
            cell = popped;
            if (popped.Closed)
            {
                return false;
            }
            popped.Closed = true;
            return true;
        }

        /// <summary>Every neighbour of a closed cell reached through it, where that is cheaper.</summary>
        private void Expand(long current, Cell from)
        {
            int ci = (int)(current >> 32);
            int cj = (int)(uint)current;
            Vector2 heading = CellSize <= FineCell ? Heading(ci, cj, from) : Vector2.zero;
            for (int d = 0; d < StepX.Length; d++)
            {
                int ni = ci + StepX[d];
                int nj = cj + StepY[d];
                long next = Key(ni, nj);
                // Valid until the table grows again, which only the next GetCell can make it do.
                ref Cell to = ref cells.At(GetCell(ni, nj));
                if (to.Closed || to.Outside || (to.Factor <= 0f && !goalKeys.ContainsKey(next)))
                {
                    continue;
                }
                float reached = from.Cost + StepCost(from, to, CellSize * StepLength[d]);
                if (heading != Vector2.zero && to.Sea < 0.5f)
                {
                    float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(heading, new Vector2(StepX[d], StepY[d]) / StepLength[d]), -1f, 1f)) - TurnFree;
                    if (turn > 0f)
                    {
                        reached += TurnWeight * turn * turn;
                    }
                }
                if (to.Reached && to.Cost <= reached)
                {
                    continue;
                }
                to.Cost = reached;
                to.Reached = true;
                to.Parent = current;
                to.HasParent = true;
                open.Push(reached + Heuristic(ni, nj), next);
            }
        }

        /// <summary>
        /// The way the route to a cell came, as a unit vector: from its ancestor about
        /// <see cref="TurnLookBack"/> back (a few steps) to it. Zero at a start, where any way out is free.
        /// </summary>
        private Vector2 Heading(int ci, int cj, Cell from)
        {
            Cell back = from;
            long key = 0;
            bool found = false;
            float reach = TurnLookBack / CellSize;
            for (int step = 0; step < 6 && back.HasParent; step++)
            {
                key = back.Parent;
                int index = cells.IndexOf(key);
                if (index < 0)
                {
                    break;
                }
                back = cells.At(index);
                found = true;
                Vector2 way = new Vector2(ci - (int)(key >> 32), cj - (int)(uint)key);
                if (way.sqrMagnitude >= reach * reach)
                {
                    return way.normalized;
                }
            }
            if (!found)
            {
                return Vector2.zero;
            }
            Vector2 whole = new Vector2(ci - (int)(key >> 32), cj - (int)(uint)key);
            return whole.sqrMagnitude > 0f ? whole.normalized : Vector2.zero;
        }

        private void Advance(long current, float reachedFor)
        {
            if (collectUpTo > 0f)
            {
                Fraction = Mathf.Max(Fraction, Mathf.Clamp01(reachedFor / collectUpTo));
                return;
            }
            float h = Heuristic((int)(current >> 32), (int)(uint)current);
            if (startHeuristic < 0f)
            {
                // The first cell expanded is the start the search believes in most.
                startHeuristic = Mathf.Max(h, 1f);
            }
            float fraction = Mathf.Clamp01(1f - h / startHeuristic);
            if (fraction > Fraction || Frontier == null)
            {
                Fraction = Mathf.Max(Fraction, fraction);
                Frontier = new Vector2((int)(current >> 32) * CellSize, (int)(uint)current * CellSize);
            }
        }

        /// <summary>What reaching the cell of this point cost, once the search is done; -1 if it never got there.</summary>
        public float CostOf(Vector2 p)
        {
            return CostAt(Key(Mathf.RoundToInt(p.x / CellSize), Mathf.RoundToInt(p.y / CellSize)));
        }

        /// <summary>What reaching this cell cost; -1 if it was never reached.</summary>
        private float CostAt(long key)
        {
            int index = cells.IndexOf(key);
            return index >= 0 && cells.At(index).Reached ? cells.At(index).Cost : -1f;
        }

        /// <summary>Per cell of a route, what reaching it cost from the route's first cell.</summary>
        private List<float> CostsAlong(List<Vector2> route)
        {
            List<float> costs = new List<float>(route.Count);
            float start = CostAt(Key(Mathf.RoundToInt(route[0].x / CellSize), Mathf.RoundToInt(route[0].y / CellSize)));
            foreach (Vector2 p in route)
            {
                costs.Add(CostAt(Key(Mathf.RoundToInt(p.x / CellSize), Mathf.RoundToInt(p.y / CellSize))) - start);
            }
            return costs;
        }

        private float StepCost(Cell from, Cell to, float length)
        {
            // Heights are walking surfaces, so stepping into or out of the water is not a climb.
            float grade = Mathf.Abs(to.Height - from.Height) / length;
            float ratio = grade / ComfortGrade;
            float land = 1f + ratio * ratio;
            if (grade > MaxGrade)
            {
                land *= 20f;
            }
            float dry = 1f - to.Shallow - to.River - to.Sea;
            float factor = dry * land + to.Shallow * FordFactor + to.River * SwimFactor + to.Sea * seaFactor;
            // Along a coast the sea share stays the same and nothing is paid; a crossing pays
            // the boarding going out and the landing coming in, a wide shore cell its share of
            // each. The search runs the way the player walks, from the network to the goal.
            float shore = to.Sea - from.Sea;
            float lump = shore > 0f ? boardingCost * shore : landingCost * -shore;
            if (from.Berth || to.Berth)
            {
                lump *= Ports.Share;
            }
            return length * factor * (to.Factor > 0f ? to.Factor : 1f) + lump;
        }

        /// <summary>The straight distance to the nearest goal; none when collecting, which is plain Dijkstra.</summary>
        private float Heuristic(int i, int j)
        {
            if (collectUpTo > 0f)
            {
                return 0f;
            }
            Vector2 p = new Vector2(i * CellSize, j * CellSize);
            float nearest = float.MaxValue;
            foreach (Vector2 goal in goals)
            {
                nearest = Mathf.Min(nearest, Vector2.Distance(p, goal));
            }
            if (guide != null)
            {
                float surveyed = guide(p);
                if (surveyed > 0f)
                {
                    return Mathf.Max(nearest, SurveyWeight * surveyed);
                }
            }
            return nearest;
        }

        private bool InBounds(Vector2 p)
        {
            if (bounds != null)
            {
                return bounds(p);
            }
            for (int g = 0; g < goals.Count; g++)
            {
                float toGoal = Vector2.Distance(p, goals[g]);
                for (int s = 0; s < anchors.Count; s++)
                {
                    if (Vector2.Distance(p, anchors[s].Position) + toGoal <= ellipseLimit[s * goals.Count + g])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The cell's index in the table, sampled first if new; valid until the next new cell.</summary>
        private int GetCell(int i, int j)
        {
            long key = Key(i, j);
            int index = cells.IndexOf(key);
            if (index >= 0)
            {
                return index;
            }
            Cell cell = default;
            float x = i * CellSize;
            float z = j * CellSize;
            if (!InBounds(new Vector2(x, z)))
            {
                cell.Outside = true;
                index = cells.Add(key);
                cells.At(index) = cell;
                return index;
            }
            sampled++;
            WorldGenerator gen = generator;
            Heightmap.Biome biome = gen.GetBiome(x, z);
            Sample(gen, x, z, biome, ref cell);
            cell.Factor = BiomeFactor(biome);
            if (cell.Lava >= 0.5f)
            {
                cell.Factor = 0f;
            }
            else if (cell.Lava > 0f)
            {
                cell.Factor *= 1f + LavaFactor * cell.Lava;
            }
            if (x * x + z * z > 9900f * 9900f)
            {
                cell.Factor = 0f;
            }
            if (cell.Factor > 0f)
            {
                float noise = Mathf.PerlinNoise(x / WanderScale + wanderOffset.x, z / WanderScale + wanderOffset.y);
                cell.Factor *= 1f + WanderStrength * (noise * 2f - 1f);
                Vector2 p = new Vector2(x, z);
                if (locationGrid.Contains(p))
                {
                    cell.Factor *= LocationFactor;
                }
                if (structures.Distance(p, structureRadius) < structureRadius)
                {
                    cell.Factor *= StructureFactor;
                }
                if (berths != null)
                {
                    cell.Berth = berths.Contains(p);
                }
            }
            index = cells.Add(key);
            cells.At(index) = cell;
            return index;
        }

        /// <summary>
        /// The cell's surface height and water shares from its centre height and, for a wide
        /// cell, four more samples a third of the cell out on the diagonals - so at 32 m nothing
        /// narrower than 11 m slips between them, at 64 m nothing narrower than 21 m.
        /// </summary>
        private void Sample(WorldGenerator gen, float x, float z, Heightmap.Biome biome, ref Cell cell)
        {
            int count = 1;
            float top = 0f;
            Count(gen, x, z, biome, ref top, ref cell);
            if (CellSize >= SampledCell)
            {
                float d = CellSize / 3f;
                Count(gen, x - d, z - d, gen.GetBiome(x - d, z - d), ref top, ref cell);
                Count(gen, x + d, z - d, gen.GetBiome(x + d, z - d), ref top, ref cell);
                Count(gen, x - d, z + d, gen.GetBiome(x - d, z + d), ref top, ref cell);
                Count(gen, x + d, z + d, gen.GetBiome(x + d, z + d), ref top, ref cell);
                count = 5;
            }
            cell.Height = top / count;
            cell.Shallow /= count;
            cell.River /= count;
            cell.Sea /= count;
            cell.Lava /= count;
        }

        /// <summary>One sample: its walking surface into top, and which kind of water it is, if any, into the cell's counts.</summary>
        private void Count(WorldGenerator gen, float x, float z, Heightmap.Biome biome, ref float top, ref Cell cell)
        {
            if (biome == Heightmap.Biome.AshLands)
            {
                gen.GetBiomeHeight(Heightmap.Biome.AshLands, x, z, out Color mask);
                if (mask.a > LavaValue)
                {
                    cell.Lava += 1f;
                }
            }
            float height = Ground.Height(x, z);
            top += Mathf.Max(height, waterLevel);
            float depth = waterLevel - height;
            if (biome == Heightmap.Biome.Swamp && depth <= Trail.SwampFill)
            {
                // The road is built up over it (Trail's causeway): ground, at the swamp's price.
                return;
            }
            if (depth > FordDepth)
            {
                if (IsSea(depth, biome))
                {
                    cell.Sea += 1f;
                }
                else
                {
                    cell.River += 1f;
                }
            }
            else if (depth > 0f)
            {
                cell.Shallow += 1f;
            }
        }

        /// <summary>Water this deep here needs a boat: deeper than a ford in the Ocean biome, deeper than any river elsewhere.</summary>
        internal static bool IsSea(float depth, Heightmap.Biome biome)
        {
            return depth > FordDepth && (biome == Heightmap.Biome.Ocean || depth > RiverDepth);
        }

        private float BiomeFactor(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.AshLands: return AshlandsFactor;
                case Heightmap.Biome.Swamp: return swampFactor;
                case Heightmap.Biome.Mistlands: return MistlandsFactor;
                case Heightmap.Biome.DeepNorth: return DeepNorthFactor;
                default: return 1f;
            }
        }

        private List<Vector2> Rebuild(long goalKey)
        {
            List<Vector2> route = new List<Vector2>();
            long key = goalKey;
            while (true)
            {
                route.Add(new Vector2((int)(key >> 32) * CellSize, (int)(uint)key * CellSize));
                int index = cells.IndexOf(key);
                if (index < 0 || !cells.At(index).HasParent)
                {
                    break;
                }
                key = cells.At(index).Parent;
            }
            route.Reverse();
            return route;
        }

        private static long Key(int i, int j) => ((long)i << 32) | (uint)j;

        /// <summary>
        /// Every cell met, in one open-addressed table: a key array and a cell array, grown by
        /// doubling. Four dictionaries (ground, cost, parent, closed) were four lookups per
        /// neighbour and, for a search of two million cells, several hundred MB for the garbage
        /// collector, whose pauses stop the game's main thread too.
        /// </summary>
        private sealed class CellMap
        {
            private const long Empty = long.MinValue;
            private long[] keys;
            private Cell[] values;
            private int shift;
            private int count;

            public CellMap()
            {
                Allocate(1 << 12);
            }

            public ref Cell At(int index) => ref values[index];

            public int IndexOf(long key)
            {
                int mask = keys.Length - 1;
                for (int slot = Slot(key); ; slot = (slot + 1) & mask)
                {
                    long found = keys[slot];
                    if (found == key)
                    {
                        return slot;
                    }
                    if (found == Empty)
                    {
                        return -1;
                    }
                }
            }

            /// <summary>A new key's slot; the caller knows it is not in yet. Every index given out before may move.</summary>
            public int Add(long key)
            {
                if ((count + 1) * 10 > keys.Length * 7)
                {
                    long[] oldKeys = keys;
                    Cell[] oldValues = values;
                    Allocate(keys.Length * 2);
                    for (int i = 0; i < oldKeys.Length; i++)
                    {
                        if (oldKeys[i] != Empty)
                        {
                            int moved = Insert(oldKeys[i]);
                            values[moved] = oldValues[i];
                        }
                    }
                }
                count++;
                return Insert(key);
            }

            private int Insert(long key)
            {
                int mask = keys.Length - 1;
                int slot = Slot(key);
                while (keys[slot] != Empty)
                {
                    slot = (slot + 1) & mask;
                }
                keys[slot] = key;
                return slot;
            }

            private int Slot(long key) => (int)((ulong)(key * -7046029254386353131L) >> shift);

            private void Allocate(int size)
            {
                keys = new long[size];
                for (int i = 0; i < size; i++)
                {
                    keys[i] = Empty;
                }
                values = new Cell[size];
                shift = 64;
                for (int s = size; s > 1; s >>= 1)
                {
                    shift--;
                }
            }
        }

        /// <summary>A plain binary min-heap; stale entries are skipped by the closed flag.</summary>
        private sealed class MinHeap
        {
            private float[] keys = new float[1024];
            private long[] values = new long[1024];
            public int Count { get; private set; }

            public void Push(float key, long value)
            {
                if (Count == keys.Length)
                {
                    System.Array.Resize(ref keys, Count * 2);
                    System.Array.Resize(ref values, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int up = (i - 1) >> 1;
                    if (keys[up] <= key)
                    {
                        break;
                    }
                    keys[i] = keys[up];
                    values[i] = values[up];
                    i = up;
                }
                keys[i] = key;
                values[i] = value;
            }

            public long Pop()
            {
                long top = values[0];
                if (--Count > 0)
                {
                    float key = keys[Count];
                    long value = values[Count];
                    int i = 0;
                    while (true)
                    {
                        int child = 2 * i + 1;
                        if (child >= Count)
                        {
                            break;
                        }
                        if (child + 1 < Count && keys[child + 1] < keys[child])
                        {
                            child++;
                        }
                        if (keys[child] >= key)
                        {
                            break;
                        }
                        keys[i] = keys[child];
                        values[i] = values[child];
                        i = child;
                    }
                    keys[i] = key;
                    values[i] = value;
                }
                return top;
            }
        }
    }

    /// <summary>
    /// The locations a search walks around, bucketed: a cell looks at the circles overlapping its
    /// 64 m square instead of at every location in the search's ellipse - a long road's ellipse
    /// holds a thousand and more, and every new cell asked each of them.
    /// </summary>
    internal sealed class LocationGrid
    {
        private const float BucketSize = 64f;
        private readonly Dictionary<long, List<Circle>> buckets = new Dictionary<long, List<Circle>>();

        public LocationGrid(List<Circle> circles)
        {
            foreach (Circle circle in circles)
            {
                int x0 = Mathf.FloorToInt((circle.Center.x - circle.Radius) / BucketSize);
                int x1 = Mathf.FloorToInt((circle.Center.x + circle.Radius) / BucketSize);
                int z0 = Mathf.FloorToInt((circle.Center.y - circle.Radius) / BucketSize);
                int z1 = Mathf.FloorToInt((circle.Center.y + circle.Radius) / BucketSize);
                for (int i = x0; i <= x1; i++)
                {
                    for (int j = z0; j <= z1; j++)
                    {
                        long key = ((long)i << 32) | (uint)j;
                        if (!buckets.TryGetValue(key, out List<Circle> list))
                        {
                            buckets[key] = list = new List<Circle>();
                        }
                        list.Add(circle);
                    }
                }
            }
        }

        public bool Contains(Vector2 p)
        {
            long key = ((long)Mathf.FloorToInt(p.x / BucketSize) << 32) | (uint)Mathf.FloorToInt(p.y / BucketSize);
            if (!buckets.TryGetValue(key, out List<Circle> list))
            {
                return false;
            }
            foreach (Circle circle in list)
            {
                if (circle.Contains(p))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
