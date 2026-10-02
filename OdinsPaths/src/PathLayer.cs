using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// One path, start to finish: search from the cheapest of the starts to the cheapest of the
    /// goals (the altars of one boss, say) around the locations and buildings in the way - a
    /// coarse pass over the whole ellipse for the line, which also picks the goal, then each
    /// further pass to that goal alone in a corridor around the line before it, the last at the fine
    /// cell -, shape the route into a trail, write it into
    /// the terrain, clear the trees and rocks off it, and mark each sea landing (a harbour stone). A
    /// coroutine, run on the plugin. Server only.
    /// Today the dev commands "paths search" and "paths lay" call it; the sleep trigger will (ROADMAP.md section 3).
    /// </summary>
    internal static class PathLayer
    {
        /// <summary>
        /// A goal is dropped when even its straight distance from the network is more than this
        /// times the straight distance of the nearest - the most a road costs over its length,
        /// sea and hills included, as a rule of thumb -, and of the rest only the nearest are kept.
        /// Two hundred and forty infected mines were every one a goal, and each new cell measured
        /// itself against all of them.
        /// </summary>
        private const float CandidateFactor = 3.4f;
        private const int MaxCandidates = 12;
        /// <summary>Cells the fine pass alone may expand before it starts over with a 16 m pass first.</summary>
        private const int FineAloneLimit = 250000;
        /// <summary>Cells a search from the Mistlands' edge to its goal may expand before the road stops at the edge.</summary>
        private const int InnerLimit = 400000;

        /// <summary>Locations smaller than this still keep the path at this distance.</summary>
        private const float MinLocationRadius = 8f;

        /// <summary>
        /// The shares of a job on the progress bar, from the times seen in game: the passes, the
        /// terrain, the clearing, then the spurs' search and their laying.
        /// </summary>
        private const float SearchShare = 0.55f;
        private const float WriteShare = 0.72f;
        private const float ClearShare = 0.78f;
        private const float SpurSearchShare = 0.86f;
        /// <summary>
        /// Past a point of interest's exterior radius, how far its own pieces are no obstacle to its
        /// spur: a village's walls and a crypt's ruins stand up to its edge, and every cell near a
        /// piece costs forty times - reason enough, before, for a spur to give up metres from the goal.
        /// </summary>
        private const float SpurApproach = 12f;
        /// <summary>How far a main road's dirt fan reaches on past its end, at most, and how wide it opens (half).</summary>
        private const float FanLength = 24f;
        private const float FanHalfWidth = 6f;
        /// <summary>Over the road's last metres before a fan, its stone turns to dirt.</summary>
        private const float FanTail = 8f;
        /// <summary>A road ending closer than this to its place's door gets no fan.</summary>
        private const float FanShortest = 4f;

        /// <summary>One search pass: its cell, and the width of the corridor around the pass before it (none for the first).</summary>
        internal struct Pass
        {
            public float Cell;
            public float Corridor;

            public Pass(float cell, float corridor = 0f)
            {
                Cell = cell;
                Corridor = corridor;
            }
        }

        /// <summary>How one lay is searched, and whether it is written. The defaults are the settings.</summary>
        internal sealed class Options
        {
#pragma warning disable CS0649 // set only by the dev commands, which Release builds leave out
            /// <summary>
            /// The passes in order, widest cell first; the last one's route becomes the trail. Null:
            /// chosen by the distance to the goal (<see cref="ForDistance"/>).
            /// </summary>
            public List<Pass> Passes;
            /// <summary>Only the first of the passes: a quick look at the line, for a preview.</summary>
            public bool FirstOnly;
            /// <summary>Off: search and shape only, touch nothing.</summary>
            public bool Write = true;
            /// <summary>Stone or dirt, and how wide and levelled.</summary>
            public RoadKind Kind = RoadKind.Main;
            /// <summary>The cell the spurs' search runs at: the fine cell, or a coarser one for a quick preview.</summary>
            public float SpurCell = PathSearch.FineCell;
            /// <summary>Told of every search as it starts (the survey, each pass, the spurs'), to watch it.</summary>
            public Action<PathSearch> Searching;
            /// <summary>Told of the road as soon as it is found, before it is written and before its spurs are searched.</summary>
            public Action<Outcome> Found;
#pragma warning restore CS0649

            /// <summary>
            /// The passes for a road whose nearest start is this far from its nearest goal, with
            /// AdaptivePasses on: the fine cell alone over the whole ellipse up to 600 m (a few
            /// ten thousand cells, and the best line there is), 16 m then fine up to 1500 m, 32 m then
            /// fine up to 4 km, and beyond 64 m, 16 m in a corridor, then fine. Off, the settings'.
            /// </summary>
            public static List<Pass> ForDistance(float distance)
            {
                if (!OdinsPathsPlugin.AdaptivePasses.Value)
                {
                    return FromSettings();
                }
                List<Pass> passes = new List<Pass>();
                if (distance > 4000f)
                {
                    passes.Add(new Pass(64f));
                    passes.Add(new Pass(16f, OdinsPathsPlugin.MidCorridor.Value));
                }
                else if (distance > 1500f)
                {
                    passes.Add(new Pass(32f));
                }
                else if (distance > 600f)
                {
                    passes.Add(new Pass(16f));
                }
                passes.Add(new Pass(PathSearch.FineCell, OdinsPathsPlugin.CorridorWidth.Value));
                return passes;
            }

            /// <summary>The coarse pass if set, the middle pass if set, then the fine pass.</summary>
            public static List<Pass> FromSettings()
            {
                List<Pass> passes = new List<Pass>();
                if (OdinsPathsPlugin.CoarseCell.Value > 0f)
                {
                    passes.Add(new Pass(OdinsPathsPlugin.CoarseCell.Value));
                }
                if (OdinsPathsPlugin.MidCell.Value > 0f)
                {
                    passes.Add(new Pass(OdinsPathsPlugin.MidCell.Value, OdinsPathsPlugin.MidCorridor.Value));
                }
                passes.Add(new Pass(PathSearch.FineCell, OdinsPathsPlugin.CorridorWidth.Value));
                return passes;
            }
        }

        internal sealed class Outcome
        {
            public string Failure;
            /// <summary>Every pass run, in order; the last one may have failed.</summary>
            public List<PathSearch> Passes = new List<PathSearch>();
            /// <summary>The last pass run - the one the trail comes from when it succeeded.</summary>
            public PathSearch Search => Passes.Count > 0 ? Passes[Passes.Count - 1] : null;
            public Trail Trail;
            /// <summary>The straight way in the road ends along (<see cref="Approaches.LaneTo"/>); null if none was known.</summary>
            public Approaches.Lane? Lane;
            /// <summary>Without a lane, the dirt on from where the road stops toward its place's centre (<see cref="Trail.Fan"/>); null if none.</summary>
            public Trail Fan;
            /// <summary>The trail as a road of the network, costs from the hub and all; the caller adds it.</summary>
            public Network.Road Road;
            /// <summary>The spurs laid off it, when the planner lays them (<see cref="LaySpurs"/>).</summary>
            public SpurOutcome Spurs;
            public TerrainWriter.Result Written = new TerrainWriter.Result();
            public Clearing.Result Cleared = new Clearing.Result();
            public List<ZDOID> Landings = new List<ZDOID>();
            /// <summary>The Dvergr lamps along its rough Mistlands stretches (<see cref="OdinsPaths.Lamps"/>).</summary>
            public List<ZDOID> Lamps = new List<ZDOID>();
            public int Structures;
            public double WriteMilliseconds;
            /// <summary>The coarse survey that guided the passes; null if none ran.</summary>
            public PathSearch Survey;
            /// <summary>What the survey put the road at from the network, which chose the passes; 0 if the passes were given.</summary>
            public float Estimate;
            /// <summary>The goals the job had, before those too far to win were dropped (<see cref="Candidates"/>).</summary>
            public int Candidates;
            /// <summary>The goals left to search for.</summary>
            public int Searched;
            /// <summary>
            /// The goal the road leads to. For a goal in the Mistlands the passes end at an entry on
            /// their edge (<see cref="Entry"/>) and the <see cref="Inner"/> searches go on from there.
            /// </summary>
            public Vector2 Goal;
            public Vector2? Entry;
            public List<PathSearch> Inner = new List<PathSearch>();
            /// <summary>No way in from the entry: the road ends at the Mistlands' edge.</summary>
            public bool StoppedAtEntry;
        }

        public static IEnumerator Lay(List<Start> starts, List<Vector2> goals, string target, Options options,
            Action<string> report, Action<Outcome> done)
        {
            Outcome outcome = new Outcome();
            outcome.Candidates = goals.Count;
            goals = Candidates(starts, goals);
            outcome.Searched = goals.Count;
            List<Vector2> centres = goals;
            Progress.Stage("Measuring the locations", 0f, 0.01f);
            yield return Footprints.Wait();
            // A location is searched to the outer end of its lane - out of its door, or between its
            // stones on the road's side (Approaches) -, else a dungeon entrance to the side its
            // stairs face; the road still leads to (and is pinned at) the location.
            Approaches.Lane?[] lanes = new Approaches.Lane?[centres.Count];
            goals = new List<Vector2>(centres.Count);
            for (int g = 0; g < centres.Count; g++)
            {
                Vector2 from = NearestStart(starts, centres[g]);
                if (Approaches.LaneTo(centres[g], from, out Approaches.Lane lane))
                {
                    lanes[g] = lane;
                    goals.Add(lane.Outer);
                }
                else
                {
                    goals.Add(Approach(centres[g]));
                }
            }
            // A road out of the sacrificial stones sets out along the lane between them toward the
            // nearest goal: it ran straight through a stone (the user, 2026-10-02). Nothing else
            // sets out from within them, or a branch off an earlier road's first metres cuts through one.
            starts = new List<Start>(starts);
            List<Approaches.Lane> startLanes = new List<Approaches.Lane>();
            List<Circle> stones = new List<Circle>();
            List<Vector2> temples = Planner.Temples();
            for (int s = 0; s < starts.Count; s++)
            {
                Vector2 at = starts[s].Position;
                if (temples.Exists(t => (t - at).sqrMagnitude < 1f) && LocationAt(at, out ZoneSystem.ZoneLocation temple)
                    && Approaches.LaneTo(at, Nearest(centres, at), out Approaches.Lane lane))
                {
                    startLanes.Add(lane);
                    stones.Add(new Circle { Center = at, Radius = Footprints.Radius(temple) });
                    starts[s] = new Start(lane.Outer, starts[s].Cost, starts[s].HubCost);
                }
            }
            starts.RemoveAll(start => stones.Exists(c => c.Contains(start.Position)));
            // A goal in the Mistlands is searched to an entry at their edge first (Entries).
            List<Vector2> entryGoals = null;
            List<int> entryOwners = null;
            // The game's harbours on the way, their zones generated first where need be (Ports).
            List<Ports.Port> ports = null;
            if (options.Kind == RoadKind.Main)
            {
                Progress.Stage("Looking at the harbours on the way", 0f, 0.01f);
                yield return Ports.Prepare(Start.Positions(PathSearch.Anchors(starts, goals)), goals, report, found => ports = found);
            }
            Progress.Stage("Looking for a way into the Mistlands", 0f, 0.01f);
            yield return Entries.Find(goals, (found, owners) => { entryGoals = found; entryOwners = owners; }, ports);
            if (entryGoals != null)
            {
                // Eight entries a goal are too many goals again; the same rule keeps the likely ones.
                List<Vector2> keptEntries = Candidates(starts, entryGoals);
                List<int> keptOwners = keptEntries.ConvertAll(e => entryOwners[entryGoals.IndexOf(e)]);
                entryGoals = keptEntries;
                entryOwners = keptOwners;
            }
            List<Vector2> outerGoals = entryGoals ?? goals;
            List<Vector2> everyGoal = new List<Vector2>(outerGoals);
            if (entryGoals != null)
            {
                everyGoal.AddRange(goals);
            }
            // Its own stage: the log named the Mistlands for slow frames that fell in here.
            Progress.Stage("Looking at what stands in the way", 0f, 0.01f);
            // A network has thousands of starts; the areas are drawn around the few that matter.
            List<Vector2> positions = Start.Positions(PathSearch.Anchors(starts, outerGoals));
            List<Circle> locations = null;
            Structures structures = null;
            yield return Worker.Together(
                LocationsAround(positions, everyGoal, null, 0f, found => locations = found),
                Structures.Gather(Structures.Ellipses(positions, everyGoal), found => structures = found));
            // A harbour of the game's is no place to keep out of for the search, only for the
            // levelling: its pieces are structures, and a crossing through it is led along its pier.
            List<Circle> searched = ports == null || ports.Count == 0 ? locations
                : locations.FindAll(c => !ports.Exists(p => (p.Centre - c.Center).sqrMagnitude < 1f));
            // Where a crossing may set out or land for less: the game's piers, and the harbours
            // earlier roads left - landing beside one instead of a new harbour a little way off.
            List<Circle> berths = new List<Circle>();
            if (ports != null)
            {
                berths.AddRange(ports.ConvertAll(p => new Circle { Center = p.Berth, Radius = Ports.Reach }));
            }
            berths.AddRange(structures.HarbourStones.ConvertAll(at => new Circle { Center = at, Radius = Harbours.Merge }));
            outcome.Structures = structures.Count;
            float budget = OdinsPathsPlugin.SearchBudgetMs.Value;

            // Backwards from the goals over the search's ellipses: what reaching a goal costs from
            // anywhere, sea crossings and dear biomes included. It guides every pass, and its
            // estimate from the network picks the passes.
            Progress.Stage("Surveying the land", 0.01f, 0.03f, () => outcome.Survey?.Fraction ?? 0f);
            PathSearch shape = new PathSearch(starts, outerGoals, locations, structures, options.Kind, PathSearch.SurveyCell);
            outcome.Survey = PathSearch.Survey(outerGoals, shape.Contains, options.Kind);
            options.Searching?.Invoke(outcome.Survey);
            yield return outcome.Survey.Run(budget);
            List<Pass> passes = options.Passes;
            if (passes == null)
            {
                float distance = float.MaxValue;
                float estimate = float.MaxValue;
                foreach (Start start in PathSearch.Anchors(starts, outerGoals))
                {
                    foreach (Vector2 goal in outerGoals)
                    {
                        distance = Mathf.Min(distance, Vector2.Distance(start.Position, goal));
                    }
                    float surveyed = outcome.Survey.CostOf(start.Position);
                    if (surveyed >= 0f)
                    {
                        estimate = Mathf.Min(estimate, start.Cost + surveyed);
                    }
                }
                // By the cost, where the survey knows it: a goal 400 m off across a strait is a
                // long road, and the fine pass alone would flood its whole ellipse (2 million
                // cells and 460 s for an infected mine, 2026-09-25).
                outcome.Estimate = estimate < float.MaxValue ? estimate : distance;
                passes = Options.ForDistance(Mathf.Max(distance, outcome.Estimate));
            }
            if (options.FirstOnly && passes.Count > 1)
            {
                passes = passes.GetRange(0, 1);
            }
            List<Vector2> line = null;
            for (int p = 0; p < passes.Count; p++)
            {
                Pass pass = passes[p];
                Func<Vector2, bool> bounds = line != null ? new Corridor(line, pass.Corridor).Contains : null;
                PathSearch search = new PathSearch(starts, outerGoals, searched, structures, options.Kind, pass.Cell, bounds);
                search.Berths = berths;
                if (line == null && pass.Cell <= PathSearch.FineCell && passes.Count == 1 && options.Passes == null)
                {
                    // The fine pass alone is meant for a short road; if it floods, it starts over coarse.
                    search.Limit = FineAloneLimit;
                }
                outcome.Passes.Add(search);
                search.Guide(outcome.Survey);
                options.Searching?.Invoke(search);
                report("Pass " + (p + 1) + " at " + pass.Cell.ToString("F0") + " m"
                    + (line != null ? " in a " + pass.Corridor.ToString("F0") + " m corridor" : " over the whole area")
                    + (p == 0 ? " (" + locations.Count + " locations, " + structures.Count + " built pieces and " + structures.Obstacles + " rocks to avoid)" : "") + "...");
                Progress.Stage(passes.Count > 1 ? "Searching, pass " + (p + 1) + " of " + passes.Count : "Searching",
                    Mathf.Max(0.03f, SearchShare * p / passes.Count), SearchShare * (p + 1) / passes.Count, () => search.Fraction);
                yield return search.Run(budget);
                if (search.Result == null && search.Limit == FineAloneLimit && search.Expanded > FineAloneLimit)
                {
                    report("The fine pass alone flooded; starting over at 16 m.");
                    // Every pass in the outcome has a line; the flooded one has none.
                    outcome.Passes.Remove(search);
                    passes = new List<Pass> { new Pass(16f), new Pass(PathSearch.FineCell, OdinsPathsPlugin.CorridorWidth.Value) };
                    if (options.FirstOnly)
                    {
                        passes.RemoveAt(1);
                    }
                    p = -1;
                    continue;
                }
                if (search.Result == null)
                {
                    outcome.Failure = "pass " + (p + 1) + " at " + pass.Cell.ToString("F0") + " m: " + search.Failure;
                    done(outcome);
                    yield break;
                }
                line = search.Result;
                outerGoals = new List<Vector2> { search.Goal };
            }
            PathSearch last = outcome.Search;
            List<Vector2> route = new List<Vector2>(last.Result);
            List<float> costs = new List<float>(last.RouteCosts);
            // Out of the stones along their lane, then on from its outer end.
            Approaches.Lane? laneOut = null;
            foreach (Approaches.Lane lane in startLanes)
            {
                if ((lane.Outer - last.Origin.Position).sqrMagnitude < 0.01f)
                {
                    laneOut = lane;
                    route[0] = lane.Outer;
                    route.Insert(0, lane.Inner);
                    costs.Insert(0, costs[0]);
                }
            }
            int reached = goals.IndexOf(last.Goal);
            outcome.Goal = reached >= 0 ? centres[reached] : last.Goal;
            int entry = entryGoals != null ? entryGoals.IndexOf(last.Goal) : -1;
            if (entry >= 0 && entryOwners[entry] >= 0)
            {
                reached = entryOwners[entry];
                // From the edge to the goal inside: a search of its own in a small ellipse, so the
                // Mistlands' cliffs are never flooded from afar.
                Vector2 goal = goals[entryOwners[entry]];
                outcome.Goal = centres[entryOwners[entry]];
                outcome.Entry = last.Goal;
                List<Pass> inner = new List<Pass> { new Pass(16f) };
                if (!options.FirstOnly && !(options.Passes != null && options.Passes[options.Passes.Count - 1].Cell > PathSearch.FineCell))
                {
                    inner.Add(new Pass(PathSearch.FineCell, OdinsPathsPlugin.CorridorWidth.Value));
                }
                List<Vector2> innerLine = null;
                List<Start> from = new List<Start> { new Start(last.Goal) };
                List<Vector2> to = new List<Vector2> { goal };
                for (int p = 0; p < inner.Count; p++)
                {
                    Func<Vector2, bool> bounds = innerLine != null ? new Corridor(innerLine, inner[p].Corridor).Contains : null;
                    PathSearch search = new PathSearch(from, to, searched, structures, options.Kind, inner[p].Cell, bounds);
                    search.Berths = berths;
                    search.Limit = InnerLimit;
                    outcome.Inner.Add(search);
                    options.Searching?.Invoke(search);
                    Progress.Stage("Into the Mistlands", SearchShare * 0.9f, SearchShare, () => search.Fraction);
                    yield return search.Run(budget);
                    if (search.Result == null)
                    {
                        innerLine = null;
                        break;
                    }
                    innerLine = search.Result;
                }
                if (innerLine != null)
                {
                    PathSearch innerLast = outcome.Inner[outcome.Inner.Count - 1];
                    float offset = costs[costs.Count - 1];
                    for (int i = 1; i < innerLine.Count; i++)
                    {
                        route.Add(innerLine[i]);
                        costs.Add(offset + innerLast.RouteCosts[i]);
                    }
                }
                else
                {
                    // No way in that is not a climb: the road stops at the edge, the last stretch is the player's.
                    outcome.StoppedAtEntry = true;
                    report("No way from the Mistlands' edge to the goal; the road ends at the edge.");
                }
            }
            Approaches.Lane? laneIn = reached >= 0 && !outcome.StoppedAtEntry ? lanes[reached] : null;
            if (laneIn.HasValue)
            {
                // Straight in along the lane, from its outer end to its inner.
                Approaches.Lane lane = laneIn.Value;
                foreach (Vector2 p in new[] { lane.Outer, lane.Inner })
                {
                    costs.Add(costs[costs.Count - 1] + Vector2.Distance(route[route.Count - 1], p));
                    route.Add(p);
                }
                outcome.Lane = lane;
            }
            else
            {
                EndAtEdge(route, costs, outcome.Goal);
            }
            int piers = Ports.Splice(route, costs, ports);
            if (piers > 0)
            {
                report("Led along the pier of " + piers + " of the game's harbours.");
            }
            // From the network point itself, not its cell's centre: the junction lies on the old road.
            line = new List<Vector2>(route);
            if (!laneOut.HasValue)
            {
                line[0] = last.Origin.Position;
            }
            outcome.Trail = new Trail(line, options.Kind);
            if (laneOut.HasValue)
            {
                outcome.Trail.DirtHead(laneOut.Value.Dirt, FanTail);
                report("Out of the stones between them from " + laneOut.Value.Inner.ToString("F0") + " to " + laneOut.Value.Outer.ToString("F0") + ".");
            }
            if (outcome.Lane.HasValue)
            {
                Approaches.Lane lane = outcome.Lane.Value;
                outcome.Trail.DirtTail(lane.Dirt, FanTail);
                report("Straight in " + (lane.Door ? "to the door's way in" : "between its stones") + " from " + lane.Outer.ToString("F0")
                    + " to " + lane.Inner.ToString("F0") + ", the last " + lane.Dirt.ToString("F0") + " m dirt.");
            }
            else if (!outcome.StoppedAtEntry && LocationAt(outcome.Goal, out ZoneSystem.ZoneLocation _))
            {
                // No lane known: a fan toward the centre hides where the way in is.
                Vector2 end = line[line.Count - 1];
                float distance = Vector2.Distance(end, outcome.Goal);
                if (distance >= FanShortest)
                {
                    outcome.Fan = Trail.Fan(end, outcome.Goal, Mathf.Min(distance, FanLength), options.Kind.HalfWidthAt(end), FanHalfWidth);
                    outcome.Trail.DirtTail(0f, FanTail);
                    report("No lane into " + outcome.Goal.ToString("F0") + "; a dirt fan of " + outcome.Fan.Length.ToString("F0") + " m toward its centre.");
                }
            }
            outcome.Road = Network.FromLay(outcome.Trail, route, costs, last.Origin, outcome.Goal, target);
            options.Found?.Invoke(outcome);
            if (!options.Write)
            {
                done(outcome);
                yield break;
            }
            report("Found " + outcome.Trail.Length.ToString("F0") + " m in " + outcome.Search.Milliseconds.ToString("F0")
                + " ms; writing the terrain...");
            float started = Time.realtimeSinceStartup;
            Progress.Stage("Laying the road", SearchShare, WriteShare);
            // The fan after the road: it never paints over its stone.
            List<Approaches.Lane> written = new List<Approaches.Lane>();
            if (outcome.Lane.HasValue)
            {
                written.Add(outcome.Lane.Value);
            }
            yield return TerrainWriter.Write(outcome.Fan != null ? new List<Trail> { outcome.Trail, outcome.Fan } : new List<Trail> { outcome.Trail },
                ForWriting(locations, written, laneOut), structures, outcome.Written);
            Progress.Stage("Clearing trees and rocks", WriteShare, ClearShare);
            yield return Clearing.Clear(outcome.Trail, outcome.Cleared);
            List<Buildings.DoorPath> doorPaths = new List<Buildings.DoorPath>();
            // Their own stages, or the frame watch puts a slow harbour down to the clearing.
            Progress.Stage("Building the harbours", ClearShare, ClearShare);
            yield return OdinsPaths.Landings.Place(outcome.Trail, structures, outcome.Landings, last.Origin.Position, doorPaths);
            // The harbour buildings' doors fork off the road, written after it; then the ground
            // levelled under each building, around what the path levelled.
            Progress.Stage("Paths to the harbour buildings", ClearShare, ClearShare);
            foreach (Buildings.DoorPath path in doorPaths)
            {
                yield return TerrainWriter.Write(path.Trail, locations, path.Structures, outcome.Written);
                if (path.Pad != null && path.PadStructures != null)
                {
                    yield return TerrainWriter.WritePad(path.Pad, locations, path.PadStructures, outcome.Written);
                }
                yield return Clearing.Clear(path.Trail, outcome.Cleared);
            }
            Progress.Stage("Lamps", ClearShare, ClearShare);
            outcome.Lamps = OdinsPaths.Lamps.Place(outcome.Trail, structures, locations);
            outcome.WriteMilliseconds = (Time.realtimeSinceStartup - started) * 1000.0;
            done(outcome);
        }

        /// <summary>The location instance whose centre is at p, if any - a base is none.</summary>
        private static bool LocationAt(Vector2 p, out ZoneSystem.ZoneLocation location)
        {
            bool found = LocationAt(p, out ZoneSystem.LocationInstance instance);
            location = instance.m_location;
            return found;
        }

        private static bool LocationAt(Vector2 p, out ZoneSystem.LocationInstance instance)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.m_locationInstances.TryGetValue(ZoneSystem.GetZone(new Vector3(p.x, 0f, p.y)), out instance)
                || (new Vector2(instance.m_position.x, instance.m_position.z) - p).sqrMagnitude > 1f)
            {
                instance = default;
                return false;
            }
            return instance.m_location != null;
        }

        /// <summary>The start nearest the point: the side a road will come from.</summary>
        private static Vector2 NearestStart(List<Start> starts, Vector2 to)
        {
            Vector2 best = to;
            float bestDistance = float.MaxValue;
            foreach (Start start in starts)
            {
                float distance = (start.Position - to).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = start.Position;
                }
            }
            return best;
        }

        /// <summary>
        /// Where a main road to a location should arrive. A dungeon entrance the game turns to the
        /// slope (the infected mines, the Queen's gate: m_slopeRotation, an interior radius): it
        /// faces downhill - the game looks along the fall between the highest and the lowest of ten
        /// random points within its radius, snapped to 22.5 degrees -, so the road is searched to
        /// the edge on that side, where the stairs are, and not to the centre, where the door at
        /// their bottom is. **Verify** that the stairs open downhill. Anything else: the centre.
        /// </summary>
        public static Vector2 Approach(Vector2 goal)
        {
            if (!LocationAt(goal, out ZoneSystem.ZoneLocation location) || !location.m_slopeRotation || location.m_interiorRadius <= 0f
                || !Approaches.SlopeYaw(goal, location, out float yaw))
            {
                return goal;
            }
            yaw *= Mathf.Deg2Rad;
            return goal + new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * (location.m_exteriorRadius + 2f);
        }

        /// <summary>
        /// Cuts a main road where it first enters its location: the altar stones, ruins and stairs
        /// within are the location's own (the Elder's reach 25 m out). That is its measured
        /// footprint (<see cref="Footprints"/>) - the road to Yagluth ended in one of the pillars
        /// around his arena, past its exterior radius (seen in game 2026-09-25) -, except for a
        /// dungeon entrance turned to the slope, whose road is searched to its stairs just outside
        /// the exterior radius (<see cref="Approach"/>). A base, or a road that starts inside, is left whole.
        /// </summary>
        private static void EndAtEdge(List<Vector2> route, List<float> costs, Vector2 centre)
        {
            if (!LocationAt(centre, out ZoneSystem.ZoneLocation location) || route.Count < 3)
            {
                return;
            }
            bool stairs = location.m_slopeRotation && location.m_interiorRadius > 0f;
            float radius = Mathf.Max(stairs ? location.m_exteriorRadius : Footprints.Radius(location), MinLocationRadius);
            if (Vector2.Distance(route[0], centre) <= radius)
            {
                return;
            }
            for (int i = 1; i < route.Count; i++)
            {
                float inner = Vector2.Distance(route[i], centre);
                if (inner > radius)
                {
                    continue;
                }
                if (i < 2)
                {
                    return;
                }
                float outer = Vector2.Distance(route[i - 1], centre);
                float t = Mathf.Clamp01((outer - radius) / Mathf.Max(outer - inner, 0.01f));
                Vector2 edge = Vector2.Lerp(route[i - 1], route[i], t);
                float cost = Mathf.Lerp(costs[i - 1], costs[i], t);
                route.RemoveRange(i, route.Count - i);
                costs.RemoveRange(i, costs.Count - i);
                if (t > 0.05f)
                {
                    route.Add(edge);
                    costs.Add(cost);
                }
                return;
            }
        }

        /// <summary>
        /// The goals worth a search: by the lower bound of each (its straight distance from a start,
        /// plus that start's cost), those within <see cref="CandidateFactor"/> of the nearest one's,
        /// and of them the <see cref="MaxCandidates"/> nearest.
        /// </summary>
        public static List<Vector2> Candidates(List<Start> starts, List<Vector2> goals)
        {
            if (goals.Count <= 1)
            {
                return goals;
            }
            List<Start> anchors = PathSearch.Anchors(starts, goals);
            float[] bound = new float[goals.Count];
            float best = float.MaxValue;
            for (int g = 0; g < goals.Count; g++)
            {
                bound[g] = float.MaxValue;
                foreach (Start start in anchors)
                {
                    bound[g] = Mathf.Min(bound[g], start.Cost + Vector2.Distance(start.Position, goals[g]));
                }
                best = Mathf.Min(best, bound[g]);
            }
            List<int> kept = new List<int>();
            for (int g = 0; g < goals.Count; g++)
            {
                if (bound[g] <= best * CandidateFactor)
                {
                    kept.Add(g);
                }
            }
            kept.Sort((a, b) => bound[a].CompareTo(bound[b]));
            if (kept.Count > MaxCandidates)
            {
                kept.RemoveRange(MaxCandidates, kept.Count - MaxCandidates);
            }
            return kept.ConvertAll(g => goals[g]);
        }

        internal sealed class SpurOutcome
        {
            /// <summary>Points of interest near enough to the road to be looked at.</summary>
            public int Candidates;
            /// <summary>Those points of interest, by their centre, and their location names; the ones not in <see cref="Connected"/> were too dear.</summary>
            public List<Vector2> Considered = new List<Vector2>();
            public List<string> ConsideredNames = new List<string>();
            /// <summary>Per point of interest looked at, what reaching its edge cost from the road; -1 where the search never got there.</summary>
            public List<float> ConsideredCosts = new List<float>();
            /// <summary>Per point of interest looked at, how far its edge is from the nearest point of the road.</summary>
            public List<float> ConsideredDistances = new List<float>();
            /// <summary>Null when there was nothing to search for.</summary>
            public PathSearch Search;
            /// <summary>The lanes the spurs end along (<see cref="Approaches.LaneTo"/>).</summary>
            public List<Approaches.Lane> Lanes = new List<Approaches.Lane>();
            /// <summary>One per point of interest cheap enough to reach; the caller adds them and <see cref="Connected"/> to the network.</summary>
            public List<Network.Road> Roads = new List<Network.Road>();
            public List<Trail> Trails = new List<Trail>();
            /// <summary>The locations the spurs lead to, by their centre.</summary>
            public List<Vector2> Connected = new List<Vector2>();
            public TerrainWriter.Result Written = new TerrainWriter.Result();
            public Clearing.Result Cleared = new Clearing.Result();
            public List<ZDOID> Lamps = new List<ZDOID>();
            /// <summary>The minor harbours' posts, where a side path crosses water (<see cref="OdinsPaths.Landings"/>).</summary>
            public List<ZDOID> Landings = new List<ZDOID>();
        }

        /// <summary>
        /// The spurs of a main road (docs/network.md): every point of interest in the settings'
        /// list whose edge is cheaper to walk to from some point of the road than the prize gets a
        /// dirt track there. One search from every point of the road at cost 0, in a corridor as
        /// wide as the prize reaches, collects them all; each spur ends at its location's edge
        /// on the side it comes from, and none leads to a location the network already connects.
        /// A spur may cross a strait, or set out from the road's own sea crossing, at a fraction of
        /// a road's lump sums, and gets a minor harbour where it lands. Written only if write is set.
        /// </summary>
        public static IEnumerator LaySpurs(Network.Road road, Network network, bool write, Options options, Action<string> report, Action<SpurOutcome> done)
        {
            // Its own stage: these frames were counted as the lamps'.
            Progress.Stage("Looking at what stands near the side paths", ClearShare, ClearShare);
            yield return Footprints.Wait();
            float cell = options.SpurCell;
            SpurOutcome outcome = new SpurOutcome();
            float prize = OdinsPathsPlugin.SpurPrize.Value;
            if (prize <= 0f || road.Points.Count < 2)
            {
                done(outcome);
                yield break;
            }
            // The cheapest ground costs 0.7 a metre (the wander), so nothing past this is within the prize.
            float reach = prize / 0.7f;
            List<Vector2> goals = new List<Vector2>();
            List<Vector2> centres = new List<Vector2>();
            List<string> names = new List<string>();
            // Each point of interest with a margin: its ruins and walls are not in a spur's way.
            List<Circle> pois = new List<Circle>();
            List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
            List<Approaches.Lane?> lanes = new List<Approaches.Lane?>();
            foreach (string part in OdinsPathsPlugin.PointsOfInterest.Value.Split(','))
            {
                string name = part.Trim();
                if (name.Length == 0 || !ZoneSystem.instance.FindLocations(name, ref instances))
                {
                    continue;
                }
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    Vector2 centre = new Vector2(instance.m_position.x, instance.m_position.z);
                    if (network.Connected.Exists(c => (c - centre).sqrMagnitude < 1f))
                    {
                        continue;
                    }
                    float radius = Mathf.Max(instance.m_location.m_exteriorRadius, MinLocationRadius);
                    Vector2 nearest = Nearest(road.Points, centre);
                    if (Vector2.Distance(nearest, centre) - radius > reach)
                    {
                        continue;
                    }
                    // Along its lane, out of a crypt's door or between an altar's stones (Approaches);
                    // else just outside its circle, facing the road: to the yard, not through the village walls.
                    if (Approaches.LaneTo(centre, nearest, out Approaches.Lane lane))
                    {
                        goals.Add(lane.Outer);
                        lanes.Add(lane);
                    }
                    else
                    {
                        Vector2 toward = nearest - centre;
                        goals.Add(centre + (toward.sqrMagnitude > 0f ? toward.normalized : Vector2.right) * (radius + 1f));
                        lanes.Add(null);
                    }
                    centres.Add(centre);
                    names.Add(name);
                    pois.Add(new Circle { Center = centre, Radius = radius + SpurApproach });
                }
            }
            outcome.Candidates = goals.Count;
            outcome.Considered.AddRange(centres);
            outcome.ConsideredNames.AddRange(names);
            foreach (Vector2 goal in goals)
            {
                outcome.ConsideredDistances.Add(Vector2.Distance(goal, Nearest(road.Points, goal)));
            }
            if (goals.Count == 0)
            {
                done(outcome);
                yield break;
            }

            List<Start> starts = new List<Start>();
            for (int i = 0; i < road.Points.Count; i++)
            {
                starts.Add(new Start(road.Points[i], 0f, road.Costs[i]));
            }
            List<Vector2> positions = Start.Positions(PathSearch.Anchors(starts, goals));
            // A goal sits 1 m outside its point of interest, but a wide cell's centre can fall
            // inside and pay ten times for the last step: their circles shrink by most of a cell.
            // They still keep a spur to one from cutting through another.
            List<Circle> locations = null;
            Corridor corridor = new Corridor(road.Points, 2f * (reach + cell));
            // The spur search stays in the corridor, and so does every spur written.
            Structures structures = null;
            yield return Worker.Together(
                LocationsAround(positions, goals, centres, cell * 0.75f, found => locations = found),
                Structures.Gather(corridor.Contains, found => structures = found));
            PathSearch search = new PathSearch(starts, goals, locations, structures.Without(pois), RoadKind.Spur, cell, corridor.Contains);
            search.Collect(prize);
            outcome.Search = search;
            report("Spurs: " + goals.Count + " points of interest within " + reach.ToString("F0") + " m of the road...");
            Progress.Stage("Looking for side paths", ClearShare, SpurSearchShare, () => search.Fraction);
            options.Searching?.Invoke(search);
            yield return search.Run(OdinsPathsPlugin.SearchBudgetMs.Value);
            foreach (Vector2 goal in goals)
            {
                outcome.ConsideredCosts.Add(search.CostOf(goal));
            }

            foreach (PathSearch.Settled settled in search.Collected)
            {
                if (settled.Route.Count < 2)
                {
                    // The road passes right by it: reached already, and no spur to lay.
                    outcome.Connected.Add(centres[settled.Goal]);
                    continue;
                }
                // From the road's own point, to the edge itself rather than its cell's centre, and on along its lane.
                List<Vector2> route = new List<Vector2>(settled.Route);
                List<float> costs = new List<float>(settled.Costs);
                route[0] = settled.Origin.Position;
                route[route.Count - 1] = goals[settled.Goal];
                Approaches.Lane? lane = lanes[settled.Goal];
                if (lane.HasValue)
                {
                    costs.Add(costs[costs.Count - 1] + Vector2.Distance(lane.Value.Outer, lane.Value.Inner));
                    route.Add(lane.Value.Inner);
                    outcome.Lanes.Add(lane.Value);
                }
                Trail trail = new Trail(route, RoadKind.Spur);
                outcome.Trails.Add(trail);
                outcome.Roads.Add(Network.FromLay(trail, route, costs, settled.Origin, goals[settled.Goal], names[settled.Goal]));
                outcome.Connected.Add(centres[settled.Goal]);
            }
            if (write && outcome.Trails.Count > 0)
            {
                // All of them in one pass each, the writing four fifths of what is left and the clearing the rest.
                float writing = SpurSearchShare + (1f - SpurSearchShare) * 0.8f;
                Progress.Stage("Laying the side paths", SpurSearchShare, writing);
                yield return TerrainWriter.Write(outcome.Trails, ForWriting(locations, outcome.Lanes, null), structures, outcome.Written);
                Progress.Stage("Clearing the side paths", writing, 1f);
                yield return Clearing.Clear(outcome.Trails, outcome.Cleared);
                foreach (Trail trail in outcome.Trails)
                {
                    yield return Landings.Place(trail, structures, outcome.Landings);
                    outcome.Lamps.AddRange(Lamps.Place(trail, structures, locations));
                }
            }
            done(outcome);
        }

        /// <summary>
        /// The circles the writer keeps its levelling out of: a lane's own location is levelled
        /// into up to the ground it levels itself (<see cref="Approaches.Lane.Flats"/>); the
        /// sacrificial stones a road sets out of keep the writer's own distance (TerrainWriter.TempleKeep).
        /// </summary>
        private static List<Circle> ForWriting(List<Circle> locations, List<Approaches.Lane> lanes, Approaches.Lane? laneOut)
        {
            if (lanes.Count == 0 && !laneOut.HasValue)
            {
                return locations;
            }
            List<Circle> result = new List<Circle>(locations);
            foreach (Approaches.Lane lane in lanes)
            {
                result.RemoveAll(c => (c.Center - lane.Centre).sqrMagnitude < 1f);
                result.AddRange(lane.Flats);
            }
            if (laneOut.HasValue)
            {
                result.RemoveAll(c => (c.Center - laneOut.Value.Centre).sqrMagnitude < 1f);
            }
            return result;
        }

        private static Vector2 Nearest(List<Vector2> points, Vector2 to)
        {
            Vector2 best = points[0];
            foreach (Vector2 p in points)
            {
                if ((p - to).sqrMagnitude < (best - to).sqrMagnitude)
                {
                    best = p;
                }
            }
            return best;
        }

        /// <summary>
        /// Every location near the search area, as a circle to keep out of - except the ones a
        /// start or a goal lies in, which the path has to leave or reach. Sorted on the worker
        /// thread: over a world's thousands of locations, against every start, it was the slowest
        /// frame of a road (2026-10-01). Server only: clients hold no location instances.
        /// </summary>
        private static IEnumerator LocationsAround(List<Vector2> starts, List<Vector2> goals, List<Vector2> shrunk, float shrink,
            Action<List<Circle>> done)
        {
            Circle[] all = EveryLocation();
            List<Circle> result = new List<Circle>();
            yield return Worker.Run(() =>
            {
                float[] reach = new float[goals.Count];
                for (int g = 0; g < goals.Count; g++)
                {
                    foreach (Vector2 start in starts)
                    {
                        reach[g] = Mathf.Max(reach[g], PathSearch.EllipseLimit(start, goals[g]));
                    }
                }
                foreach (Circle location in all)
                {
                    Circle circle = location;
                    bool near = false;
                    for (int g = 0; g < goals.Count && !near; g++)
                    {
                        near = Vector2.Distance(circle.Center, goals[g]) <= reach[g];
                    }
                    if (!near || goals.Exists(goal => circle.Contains(goal)))
                    {
                        continue;
                    }
                    if (shrunk != null && shrunk.Exists(c => (c - circle.Center).sqrMagnitude < 1f))
                    {
                        circle.Radius = Mathf.Max(circle.Radius - shrink, 1f);
                    }
                    if (!starts.Exists(start => circle.Contains(start)))
                    {
                        result.Add(circle);
                    }
                }
            });
            done(result);
        }

        private static Circle[] everyLocation;
        private static ZoneSystem everyLocationFor;

        /// <summary>
        /// Every location instance as a circle of its buildings' reach (Footprints), which can be
        /// well past the exterior radius - read on the main thread once per world: the instances
        /// are set when the world's locations are generated and only flagged placed after.
        /// </summary>
        private static Circle[] EveryLocation()
        {
            Dictionary<Vector2s, ZoneSystem.LocationInstance> instances = ZoneSystem.instance.m_locationInstances;
            if (everyLocation != null && everyLocationFor == ZoneSystem.instance && everyLocation.Length == instances.Count)
            {
                return everyLocation;
            }
            Dictionary<ZoneSystem.ZoneLocation, float> radius = new Dictionary<ZoneSystem.ZoneLocation, float>();
            Circle[] result = new Circle[instances.Count];
            int i = 0;
            foreach (ZoneSystem.LocationInstance instance in instances.Values)
            {
                float r = MinLocationRadius;
                if (instance.m_location != null && !radius.TryGetValue(instance.m_location, out r))
                {
                    radius[instance.m_location] = r = Mathf.Max(Footprints.Radius(instance.m_location), MinLocationRadius);
                }
                result[i++] = new Circle { Center = new Vector2(instance.m_position.x, instance.m_position.z), Radius = r };
            }
            everyLocation = result;
            everyLocationFor = ZoneSystem.instance;
            return result;
        }
    }
}
