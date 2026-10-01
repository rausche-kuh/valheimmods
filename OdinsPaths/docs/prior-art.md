# Prior art: Procedural Roads and PLAN.md

What warpalicious' **Procedural Roads** (v1.4.3, MIT, checked out in
`~/Documents/Code/test/othervalheimmods/ProceduralRoads/`, read 2026-09-25) and the research
report in `../PLAN.md` offer this mod, what was taken and what was not. MIT asks for the license
notice with any copied code, so nothing was copied: every idea below was rebuilt from the game's
own source.

## Procedural Roads, how it works

- **When:** once, when `ZoneSystem.GenerateLocationsCompleted` fires (or on the first
  `Game.SpawnPlayer` of an old world). It finds islands, takes the largest 50 %, and on each one
  connects up to 12 locations from a priority table (bosses 100, crypts 80, villages 60, towers
  40...) with a Euclidean MST or a nearest-neighbour chain, alternating by island id. Roads never
  cross water.
- **Search:** A* on 8 m cells, 16 neighbours, a `SortedSet` as the open list, at most 10 000
  expansions (a setting). Every step calls `WorldGenerator.GetHeight` twice, `GetRiverWeight`,
  `GetBiome`, and 9 more heights for a "terrain variance" ring - over a dozen generator calls per
  neighbour, uncached, on the main thread. Water and rivers are impassable, slope above 0.6 costs
  2000, variance over 5 m costs 1000. No reuse of existing roads: two roads to nearby places run
  side by side.
- **Shaping:** Catmull-Rom through the cell centres, a point every width/4, heights averaged over
  41 points (about 40 m), and where a new road overlaps an old one the heights are blended.
  The path is trimmed to the locations' exterior radius at both ends.
- **Writing:** only in zones generated *after* the roads exist: a `SpawnZone` postfix edits the
  live `TerrainComp` of the new zone (paved paint, level deltas within ±8 m) and a
  `PlaceVegetation` prefix adds `ClearArea`s along the road. A zone generated before the roads is
  never touched - on an old world the roads only appear in unexplored land.
- **Persistence:** every road point goes onto one ZDO of a custom prefab
  (`ProceduralRoads_Metadata`), placed at (50 000, 0, 50 000).
- **Jötunn is used for exactly one thing:** `PrefabManager.AddPrefab` for that empty
  `ZNetView` prefab, so `ZNetScene` knows it. Nothing else - no items, pieces, localisation or
  config sync.
- Also ships an anonymous analytics ping (`Utils/Analytics.cs`, a `UnityWebRequest` POST) and a
  headless xUnit harness that subclasses `WorldGenerator` with a synthetic island
  (`ProceduralRoads.Tests/`).

## Would Jötunn help this mod? No

The only thing Procedural Roads needs it for is a prefab for its data ZDO, and that is not needed
at all. `ZNetScene.CreateObjectsSorted` destroys a ZDO with an unknown prefab only when it is
near someone (read 2026-09-25: `CreateObject` returns null, then on the server `DestroyZDO`),
and a ZDO 50 km out is never near anyone and never sent to a client. So the far position alone
keeps an unregistered data ZDO alive; registering the prefab is a second safety that a
`ZNetScene.Awake` postfix can add in a dozen lines if it turns out to be needed (`network.md`,
**verify** that nothing else sweeps it on load). Jötunn would add a hard dependency for every
server, and nothing else in the design calls for it - the mod adds no items or pieces, and stays
server side only.

## What this mod already does better

| | Procedural Roads | Odin's Paths |
| --- | --- | --- |
| Existing worlds | Only zones generated later | Any zone, generated or not, by writing the `TerrainComp` blob itself (`TerrainWriter`) |
| Water | Impassable; one network per island | Fords, rivers swum, sea crossings with boarding and landing costs, barrels at the landings |
| Search cost | over a dozen generator calls per neighbour, uncached, capped at 10 000 expansions | Each cell sampled once and cached; coarse → fine passes in corridors; 6 ms per frame budget |
| Topology | Euclidean MST or chain, no reuse | Least-cost from the whole network with start costs (the SRN/alpha idea of PLAN.md), multiple goals |
| Buildings, locations | Not avoided | Avoided in the search, left alone by the writer |
| Vegetation | `ClearArea` on new zones only | Removed in existing zones too; in new zones only trees and plain rocks, never ore or pickables |

## Taken from Procedural Roads

- **The ground as the game builds it** (`src/Ground.cs`, 2026-09-25). Its `BiomeBlendedHeight`
  (the fix PLAN.md credits to its PR #21) points at a real trap: `HeightmapBuilder.Build` does not use the biome at a
  vertex, it uses the biomes at the zone's four corners and, where they differ, blends their
  heights with a smoothstep across the whole zone. `WorldGenerator.GetHeight` does neither. The
  mod levelled the trail against GetHeight (`Trail.Ground` → `Profile`) and the writer then
  applied `profile - base` against the *built* base: at a biome border that is a cut or fill of
  up to the full 1 m clamp in the wrong place, the landing barrels floated or sank (a bug listed
  in the roadmap), and the fine search saw cliffs that are not there. `Ground.Height` replays the
  builder exactly (`DUtils.SmoothStep`/`Lerp`, corner biomes cached per zone) and is used by the
  search, the trail and so the landings. Procedural Roads' version has its own bug: where the
  four corners agree it falls back to `GetHeight`, which is wrong again for a small biome patch
  inside the zone. `paths bench` now measures `Ground.Height` against a real build of the
  player's zone (**verify** 0.000 m in game, and a non-zero GetHeight error near a biome border).
- **Trim at the goal's edge** - taken for spurs, see below.

## Taken from PLAN.md

- **Many goals, one search** (2026-09-25). PLAN.md's multi-source Dijkstra, stopped at the first
  candidate of a group, is the right tool for the rework's "each POI type pinned to the instance
  cheapest to reach from the network". `PathSearch` already started from many points; it now
  also takes many goals (heuristic = distance to the nearest, bounds = the ellipses of every
  start with every goal, stops at the first goal settled, reports it as `Goal`). `PathLayer`
  runs the first pass against all goals and the finer passes against the chosen one.
  `paths search GDKing` now searches to every Elder altar and says which it took and whether it
  was the nearest.
- **Bounds decided once per cell.** Not from the report, but found while doing the above: the
  ellipse test ran for every neighbour of every expansion, over every start - with a network of
  a few hundred start points that would have dominated the search. It now runs once per cell,
  and cells outside are never sampled.

## Not taken from PLAN.md, and why

- **Sampling in parallel (`Parallel.For`, `Task.Run`).** Wrong for this game: the generator's
  river cache (`m_cachedRiverGrid`, `m_cachedRiverPoints`) is unlocked and shared with the
  heightmap builder thread (`foundations.md`). PLAN.md itself lists it as unverified. The search
  stays a time-sliced coroutine.
- **A two-layer land/sea state graph.** The sea share per cell with a boarding cost on the way
  out and a landing cost on the way in does the same - the switch is paid once per crossing -
  without doubling the state, and handles wide cells that are part sea.
- **A pre-sampled dense grid of the whole world.** Each search samples its cells lazily and
  once. Worth reconsidering for the planner, which runs ~10 searches at server start: a coarse
  terrain cache per world (height, water shares, biome factor, wander - not the per-search
  location and building factors) shared between searches. See the roadmap.
- **Exhaustive group choice on a metric closure.** The greedy "cheapest from where the network
  already is" is what players expect, and PLAN.md itself recommends it as the default.
- **Prize-collecting Steiner in full.** Taken in its simple form instead: after each main road, a
  point of interest gets a dirt spur when its cost to the road is under a prize (`network.md`).
- **String-pulling and centripetal Catmull-Rom** instead of Chaikin: no sign yet that the Chaikin
  trail looks gridded with 16 neighbours at 4 m; revisit if the in-game test shows zig-zags.
- **A grade clamp on the height profile:** with levelling limited to a 1 m cut, the profile
  cannot flatten a slope anyway.

## Decided since (2026-09-25)

- **Traders join once placed.** PLAN.md's trader trap is real in the code - the traders are
  `m_unique`, the first instance whose zone generates becomes the trader and
  `RemoveUnplacedLocations` drops the rest - but it only bites a road laid to a trader *before*
  one is placed. The game reveals and pins the trader by its own trigger (a boss kill or
  similar), so a trader's group is skipped until its instance is `m_placed`, and then there is
  exactly one (`network.md`).
- **Reuse through start costs, not a cell discount.** A ×0.3 cell factor would make the
  straight-line heuristic overestimate along old paths and hide them. A branch starting from any
  point of an old road, at `alpha ×` its cost to the hub, gives the junctions without that
  (`search.md`); alpha is a setting, so direct roads to every location stay one number away.
- **Main roads stone, spurs dirt:** altars, traders and custom main locations
  on paved roads; villages, crypts, chambers, fuling camps and the like on dirt spurs off them.
- **Spurs end at the location's edge**, the Procedural Roads way; main roads still run up to the
  altar (levelling already stops at the location's circle) - look at a finished lay.

## Their open PRs (read 2026-10-01)

Fourteen open PRs (#19-#33, by a contributor, each tested in game) rework most of the mod;
master is unchanged since 1.4.3. Fetch them with `git fetch origin pull/<n>/head:pr-<n>`; they
stack, so diff each against the one it names as its base.

- **Taken (2026-10-01):** the dirt → both → stone paint fade (#32, `laying.md`), broken
  boulders cleared (#31), zones with two compilers left alone (#27), and from #27's time-sliced
  bake queue the writer's frame budget and its cap on builder requests.
- **On the roadmap:** compiler ownership (#27), location terrain at road ends and an
  end-aware profile (#21), a batter for deep cuts (#20), border rocks (#27), ruined bridges
  (#26), coverage (#29), manual road commands (#30).
- **Not taken:** parallel islands (#28) - our roads start from the ones laid before, so they are
  sequential by design; carving boulders chunk by chunk instead of removing them (#31) - more
  work than it is worth for now; dirt near spawn (#32) - spurs already are dirt. The re-apply
  on spawn that #15/#19 fight (stale roads, duplicate compilers, appended networks) does not
  exist here: each zone's blob is written once.
- **Their numbers, for scale:** a whole world 33 s to 284 s at load (#28, #29); one zone's
  write ~10 ms (#22); a search up to 5.5 s, 100 of 146 failing (#22). Ours, from the 2026-10-01
  log: 30-370 ms a main road's search on its own thread, under 2 s for its spurs.
- **Game facts from them, unverified here:** `StaticPhysics` first checks 20 s after an object
  wakes; the river core is `GetRiverWeight > 0.5`; a cart is 1.72 m wide; swim depth is 2 m.

## Still to look at in game

- **Height agreement where roads meet:** Procedural Roads blends a new road's heights into an
  old one's where they overlap. Here the writer leaves vertices another road already levelled
  (`ModifiedHeight`), so the old road wins, and the new one meets it with at most a 1 m step at
  its shoulder. Worth a look at the first junction.
