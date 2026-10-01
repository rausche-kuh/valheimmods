# The search: what makes it look like a path

How a route is found (`src/PathSearch.cs`, `src/Corridor.cs`, `src/Structures.cs`): the step
costs, the one search over the whole network, the passes and their measured times, and what the
search cannot see. Times and observations are from 2026-09-24 unless marked otherwise.

A least-cost search (A*) over a grid of 4 m cells, with **16 neighbours** (the 8 plus the
knight's moves) so that straight stretches are not stuck at 45° steps. The cost of a step is its
length times:

| Factor | Rule | Why |
| --- | --- | --- |
| slope | `1 + k · (grade / g0)²`, with grade = height difference / step length; above a max grade (about 0.6, 31°) ×20 | Climbing is expensive and gets worse fast. Mountains end up in switchbacks without any code for them. |
| water | height below water level (`ZoneSystem.m_waterLevel`, 30): shallow (up to 1.5 m deep) ×3; deeper in a land biome, down to 6.5 m, is a **river**, swum at ×4; deeper in the Ocean biome, or deeper than 6.5 m anywhere, is the **sea**, ×2 per metre plus a **boarding cost** of 1000 m for setting out (a boat has to be built) and a **landing cost** of 400 m for coming ashore - all three settings (`SeaCost`, `BoardingCost`, `LandingCost`), since a server may like its paths more sea-going or more land-bound; `paths costs sea 2 landing 300` sets them from the console. The search runs the way the player walks, from the network to the goal, so boarding and landing fall on the right shores. The Ocean biome is where the base height is under the water *before* the rivers are cut (`GetBiome`, `baseHeight <= 0.02`, 26 m under the water), so it tells the two apart for free - but only for the open sea: a fjord, a strait or a bay counts as the land biome around it, and roads waded across fjords to islands with no landing (second lay, 2026-09-25). The game cuts its rivers down to 0.12-0.14 of the height scale (24-28 m, at most 6 m under the water, `WorldGenerator.AddRivers`), so water deeper than 6.5 m is sea in every biome (`PathSearch.IsSea`). A cell 16 m or wider is sampled at five points (its centre and an X a third of the cell out) and water counts by its share, so along a coast nothing is paid and a river band inside a cell is always seen. | A path stays on foot wherever land leads: a 200 m strait beats a land route only when that is more than about 1.6 km longer, and a fjord is walked around. A crossing is made at its narrowest, and an island on the way is landed on only when the walk across it saves more than a landing and a boarding (about 1.4 km of sea). A river is forded where a ford is near and swum otherwise, without barrels. Tested 2026-09-24: with the sea at ×2 and only setting out paid, every island got a detour onto it; at parity with a landing cost each way the sea became a highway the path never left, because level ground still carries the slope and the wander; ×4 with 750 m each way kept the path on land, and ×2 with 700 m each way (the same 1400 m per crossing the defaults now split 1000/400) was the setting that felt right. |
| biome | Swamp ×3 (the `SwampCost` setting, also in `paths costs`), its water down to 1.5 m counted as ground (the road is a causeway there, `laying`/`biomes.md`), Mistlands ×8, Ashlands ×2 with lava impassable (a fine cell on it, a wide cell half on it) | Real paths skirt bogs. The swamp is flat and at water level, so its walking surface has no slope: at ×1.5 it was the cheapest ground on the map and paths sought it out (2026-09-24). See `biomes.md`. |
| locations | inside any location's `m_exteriorRadius`, except the target: ×10 | Keeps the path out of villages and crypt yards, and off terrain their `TerrainModifier` has flattened. |
| existing path | none - reuse comes from the start costs below (decided 2026-09-25) | A per-cell discount (×0.3 was planned) would make the straight-line heuristic overestimate wherever an old path runs, and the search would miss the cheap old path off the line. A branch starting from the old path gets the same effect: a junction, and no second road beside the first. |
| wander | × (1 + 0.3 · Perlin noise at 150 m), seeded by the world seed | On flat ground every line costs the same. The noise gives them long, lazy bends, the same for everyone on the world. |
| turns | at the fine cell only, per step: 4 m × (turn − 23°)² in radians, the turn measured against the way the route came over the last 8 m (its ancestors in the search; `PathSearch.Heading`) | Third lay, 2026-09-25: on the steepest slopes the search stacked tight hairpins whose legs lay a few metres apart, and each leg's levelling dug into the next. Squared, so a turn spread over a wide arc costs less than the same turn at once: a tight hairpin about 45 m, one of 5 m radius about 23, one of 10 m about 5 - fewer switchbacks, wider and farther apart. A turn of up to 23° is free, since a straight line at an angle the 16 steps lack zig-zags that much. Not exact A* any more (a cell keeps one parent, and the price depends on it), which only matters in a tie. **Verify** on a steep mountain side. |

**The network is one search.** The search starts from *every* network point: the start
temple and each base at cost 0, and a point every 20 m along each path already laid at a
**starting cost** of `alpha × (that point's route cost to its nearest hub)`. It stops at the
altar, and the attachment falls out on its own: a branch off an existing path competes against a
route of its own from the hub, with real terrain costs on both sides. Starting every point at 0
would be a minimum spanning tree (attach wherever is nearest, however roundabout the walk from
the hub gets); alpha 1 would be a shortest-path tree (a road of its own to everything). Around
0.4 shares trunks and keeps the walk from the hub reasonable, and it is the one knob
(`network.md`). Built 2026-09-25: `Network.Starts` gives every 20 m of the main roads at
`Directness × HubCost`, and the search reports the start it set out from (`Origin`) and the cost
along its route, from which the new road's points get their own hub costs. The ellipses (and
the locations and buildings gathered for them) are drawn around `PathSearch.Anchors` only: the
cheapest start per 100 m square, and of those only the ones whose cost plus straight distance to a goal
is within the ellipse of the best-looking start - a network is thousands of starts, and every
cell would be checked against each.

**Guided by a survey** (2026-09-25). The straight distance is a poor heuristic wherever the
route has to pay what distance does not show: a sea crossing's 1000 + 400 lump sums, the
Mistlands at ×4 (now ×8). A* then settles every cell whose cost plus distance is below the true cost -
the whole ellipse. Seen in a preview at 32 m: Moder and Yagluth 21 s each, and the Queen never
finished, the progress bar at 90% (it measures the distance still to go, which shrank at once).
So before the first pass, `PathSearch.Survey` runs a Dijkstra *backwards* from the goals over the
first pass's ellipses at 128 m (five samples a cell, no locations or buildings), and every pass
takes `max(distance, 0.9 × surveyed cost to the goal)` as its heuristic. A 128 m cell can step
over a strait the real search must go round, so the estimate is trusted to 90%; its lump sums run
the wrong way (a landing where the search boards), which only makes it lower. **Verify** the
times (the preview prints the survey's and each pass's cells and ms) and that the lines are no
worse than before.

**The first pass by distance** (2026-09-25, `[Performance] AdaptivePasses`, on): from the nearest
start to the nearest goal, up to 600 m the fine 4 m pass alone over the whole ellipse (close
targets looked right at once in game, and that is a few ten thousand cells), up to 1.5 km 16 m
first, up to 4 km 32 m, beyond 64 m and then 16 m in a 256 m corridor - each followed by the fine
pass in its corridor. Off, the `CoarseCell` / `MidCell` settings apply to every road, as before.
The preview runs each road's first pass alone.

**On a thread of its own** (2026-09-25, `[Performance] SearchThread`, on). The first full
preview (11 roads, 166 s) was mostly frames in between: at 6 ms of work per frame the Queen's
32 m pass took 61 s of wall time for 72 000 cells. The world generator turned out to be safe off
the main thread (`foundations.md`), so `Run` starts a thread, runs the same stepwise search there
without a budget and waits for it frame by frame; the progress bar and the preview's front pin
read `Fraction` and `Frontier` as it goes. Everything else it reads (starts, goals, locations,
buildings, bounds, the finished survey) is built before and not changed after. The locations are
bucketed in 64 m squares (`LocationGrid`): a long road's ellipse holds a thousand and more, and
each new cell asked every one. The writing and the clearing stay on the main thread.

**Fewer goals, passes by cost, one table** (2026-09-25, after a preview with the infected mines:
520 s, 460 of them for the third mine). Each job's goals are cut to the likely ones first
(`PathLayer.Candidates`): by the lower bound of each (straight distance from a start plus the
start's cost), those within 3.4 times the nearest one's, and of them the 12 nearest - all 240
mines were goals, and every new cell measured itself against each. The survey now runs before
the passes are chosen, and they are chosen by its estimate of the cost from the network, not by
the straight distance: the third mine was 400 m from the network across a strait, so the fine
pass ran alone and flooded. Should the fine pass alone still pass 250 000 cells, it starts over
at 16 m. The search's four dictionaries (ground, cost, parent, closed) became one open-addressed
table of structs (`CellMap`): one lookup per neighbour instead of four or five, and about half
the memory - several hundred MB for a flood like the mine's, whose garbage collections pause the
main thread too. Goals in the Mistlands are searched through entries (`biomes.md`).

**The gathering, off the main thread too** (2026-09-25, after the next preview: 14 roads in 24 s,
every search under 3 s, but two frames of 1 to 2.4 s a road, at the survey and at the spurs). What
was left on the main thread was finding the buildings: every ZDO of the world, and each structure
among them measured against every start and goal ellipse - 24 goals with the mine entries, 60 and
more points of interest for the spurs. Now the ZDO references are copied on the main thread and
sorted on the worker (`Structures.Gather`), the prefab kinds built once per scene beforehand; each
ellipse is tried by its bounding circle before its two distances, and the spurs take the
buildings in their corridor, which is all their search and their writing touch.

**And the locations** (2026-10-01, a `paths grow all`: every road's slowest frame, 127 to 182 ms,
fell in "Looking at what stands in the way"). The locations to keep out of were still sorted on the
main thread: every instance of the world against each goal's reach and every start. Now a circle
per instance is read once per world (`PathLayer.EveryLocation`) and sorted on the worker, and the
array the ZDOs are copied into is kept from road to road instead of a new one of megabytes each
time. The frame watch counts garbage collections too, to tell what stutter is left.

The next full growth (21 roads in 128 s) had no hitch with a collection; the slowest frames left
were on roads with sea crossings, every harbour built in the frame after the clearing and so put
down to it. The harbours are now built one a frame (`Harbours.Place`), with stages of their own
for the harbours, the paths to their buildings and the lamps.

**And it ends at every candidate** (built 2026-09-25). The goals are a list too - every altar of
one boss, say - with the distance to the nearest as the heuristic, and the search stops at the
first one it settles, which is the one cheapest to reach from the network (PLAN.md's
multi-source search, `prior-art.md`). Only the first pass takes them all; the finer passes run to
the one it chose. Whether a cell is inside the ellipses of every start with every goal is decided
once, when the cell is first met. The heuristic counts a metre as 1 while wander drops flat
ground to 0.7, so it overestimates a little: the search is slightly greedy and faster, and the
route at most about 1.4 times the cheapest - invisible on a winding path. Reuse goes through the
start costs, never through cheaper cells, so it stays that way. Then the grid path is smoothed
(Chaikin, two passes) into a polyline with a point every 2 m.

**Spurs are one search per main road** (built 2026-09-25, `PathLayer.LaySpurs` with
`PathSearch.Collect`; the corridor is `Prize / 0.7` wide either side, since no ground costs less than
0.7 a metre; a spur pays 15% of the boarding and landing, `network.md`). Starts: every point of the new
main road at cost 0 (a spur attaches wherever is nearest; the walk from the hub does not
matter for a side path). Goals: the edge of every point of interest within the prize's reach of
the road - a point on its exterior radius facing the road. Bounds: a `Corridor` around the road as
wide as the prize allows. No heuristic (plain Dijkstra, the goals are all around), and the search
runs until the open cost passes the prize instead of stopping at the first goal: every goal
settled by then gets a spur, traced back from it. A single pass at 4 m is enough - spurs are a
few hundred metres at most. Until the third lay no sea: a spur that would board was dropped; now
it crosses at a spur's price and gets a minor harbour.

**Search size (built, tested once).** A route of 3 km, searched at 4 m inside an ellipse around the
straight line, visits on the order of 100 000-300 000 cells, each one height sample (cached, a
neighbour's sample is reused). A Yagluth 6 km out is an ellipse of about 3 million cells: past the
expansion cap, hundreds of MB of dictionaries, tens of seconds. So the search runs in **passes**:
a coarse one on `CoarseCell` (32 m, config) over the whole ellipse finds the line, an optional
middle one on `MidCell` (off; 16 m meant for a 64 m coarse pass) inside a `Corridor` of
`MidCorridor` (256 m) around it, then the 4 m pass inside `CorridorWidth` (96 m) around the last
line, which is linear in the route's length. `paths search <target> [pass ...]` runs them
without writing and pins them (fire = the first line, house = a middle one) so cell sizes can be
compared in game: `paths search GoblinKing 64 16:256 4` against `paths search GoblinKing 32`.

Measured 2026-09-24, wall time at the 6 ms per frame budget, before the water sampling and the
landing cost above: to Yagluth the coarse pass took 1 s at 64 m, 4 s at 32 m, 16 s at 16 m, the
fine pass 6 s; to a target 6 km out 1.6 s at 64 m and 6 s at 32 m. 32 m was good enough and 16 m
no better; 64 m hopped into water and over rivers, because one sample per cell sees a river by
luck. Each pass now prints its work time beside the wall time - the wall time is mostly frames
in between, and what the server feels is the work time spread over them. With the five-point
sampling, `paths search GoblinKing 128 32:512 8:128` (Yagluth 4.5 km out) gave a good line, so
a 128 m first pass works once water is counted by its share; the settings allow `CoarseCell` 128
and `MidCell` 32 with `MidCorridor` 512, while the last pass stays at 4 m in the settings because
the trail is laid from it - whether an 8 m last pass lays as well is still to see. To test next:
Moder - switchbacks need sideways room, and a 96 m corridor up a mountain flank may be too
narrow (widen it, or widen the corridor where the coarse line is steep). One hitch on a steep
mountain stretch was seen in the first lay and not yet looked at.

**Ore** (2026-09-25; in the first lay a road ran into a rock and ended there): the vegetation
the clearing leaves and that stands in the way - a solid collider, at least a metre from its
centre, not a pickable or spawner (`Clearing.Obstacles`: ore, the giants' bones, anything worth
mining) - joins the buildings below, each as a disc of points over its measured reach times its
scale. Only in zones generated already: in the others there is no vegetation yet to see, and a
road may still run into one. Rocks, cliffs and roots nobody can break were obstacles too at
first; the clearing takes them off the road instead, which also works in a zone generated after
the lay (`docs/laying.md`).

**Buildings (built):** before the search, every ZDO in its ellipse (a spur's: its corridor) whose prefab has a `Piece` or
`WearNTear` - players' pieces, ruins, village walls, earlier landing barrels; not dungeon
interiors (y above 1000) - goes into a bucketed point set (`Structures`). A cell within
sqrt(k² + (knight step / 2)²) of one costs 40 times more, k being the writer's reach (half width
with wobble, plus the 1.5 m shoulder) plus 2 m for the piece itself: the smoothed trail runs along
the steps between cell centres, so that radius keeps the whole painted and levelled strip off
the piece. High, not impassable - a start inside a base has to get out - and where the path is
forced through anyway, the writer paints nothing within 2 m of a piece and fades the levelling
out over the 1.5 m beyond that.

**The search cannot see** player digging, trees and rocks in generated zones (they are cleared
instead), and the Mistlands' rock formations (`biomes.md`).
