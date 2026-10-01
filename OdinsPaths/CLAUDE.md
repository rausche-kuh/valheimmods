# OdinsPaths

A road network that grows across the world: paved main roads from the sacrificial stones and
bases to the boss altars (in progression order) and the traders, and dirt spurs to the villages,
crypts and camps nearby (`docs/network.md`). Build, scripts, environment: the root `CLAUDE.md`.

**State:** roads, the network and its growth run in game (it stutters); the harbour stones,
docks and harbour buildings have not run yet. A Release build grows roads on the server after
loading and after every sleep (`[Network] Grow`); a **Debug build holds that** until
`paths auto on` (`Grower.Held`), so a test world changes only through the dev commands. Not
released: `VERSION` stays 0.1.0, `package/icon.png` is a placeholder.

## Docs

| File | Read it when |
| --- | --- |
| `ROADMAP.md` | Picking the next task: up next, what to check in game, later, bugs and risks. |
| `docs/architecture.md` | Changing a source file: each one in full, settings, dev commands. |
| `docs/foundations.md` | Terrain data, triggers, heights, snow; the facts verified in game (zone size, timings). |
| `docs/search.md` | Route costs, the search passes, going around buildings. |
| `docs/laying.md` | Paint, levelling, clearing, landings. |
| `docs/network.md` | The two tiers, progression, traders, bases, storage. |
| `docs/biomes.md` | What each biome asks of a road. |
| `docs/docks.md` | Docks and harbour buildings: placement, blueprint format, making them with PlanBuild. |
| `docs/placement.md` | Fitting docks, buildings, bridges to the terrain: other games, what is built, what is left. |
| `docs/ferry.md` | The night ferryman at the harbours (plan). |
| `docs/signposts.md` | Signposts at forks and harbours: hints, regions, lines per way, placing. |
| `docs/prior-art.md` | Procedural Roads and `PLAN.md` weighed, why no Jötunn. |

## Source map

| File | What |
| --- | --- |
| `src/OdinsPaths.cs` | Entry point and every setting. |
| `src/PathSearch.cs` | A* over cells on a thread, step costs, `Survey` heuristic. |
| `src/Entries.cs` | Ways into the Mistlands; `Worker`, the thread waited for frame by frame. |
| `src/Corridor.cs` | The strip around a coarse route the fine pass may search. |
| `src/Ground.cs` | Ground height as `HeightmapBuilder` builds it, not `GetHeight`'s. |
| `src/RoadKind.cs` | `Main` (paved) and `Spur` (dirt): paint, width, edge, levelling. |
| `src/Trail.cs` | A route shaped for laying: smoothed, water/causeway flags, grade, hairpins, cuts. |
| `src/TerrainWriter.cs` | Writes a trail into each zone's `TerrainComp` blob: levelling, then paint. |
| `src/Network.cs` | The laid network on a data ZDO; `Starts`. |
| `src/Planner.cs` | What is due, in order, each job's starts; `Run` lays a job and its spurs. |
| `src/Progress.cs` | Progress bar, message-log lines, location names, the frame watch. |
| `src/Grower.cs` | Triggers (start-up, sleep, vegvisir) and the shared `Busy` flag. |
| `src/Structures.cs` | Built pieces and obstacles in the search area, gathered off-thread. |
| `src/Lamps.cs` | Demister road posts in the Mistlands. |
| `src/Footprints.cs` | How far a location's buildings reach, measured once, cached per game version. |
| `src/Landings.cs` | Landings at sea crossings; `Spawn` marks what the mod places. |
| `src/Ports.cs` | The game's harbours (Mistlands piers), used before a dock of ours. |
| `src/Harbours.cs` | Harbour stones: the mod's vegvisir prefab, linked across the sea. |
| `src/Blueprints.cs` | Harbour blueprints: settings JSON + PlanBuild `.blueprint`, loading, saving. |
| `src/Json.cs` | A small JSON reader for the blueprints. |
| `src/Builder.cs` | Raises a blueprint: weathering, piles to the ground, spots, chests, spawners. |
| `src/Docks.cs` | A harbour's dock: flush with the road, fitted to the shore. |
| `src/Buildings.cs` | Old buildings at a harbour: door at the road's height, on a pad, a path to it; or onto the dock. |
| `src/Pad.cs` | A building's levelled ground. |
| `src/Signposts.cs` | Posts at forks and harbours: the network as a graph, lines per way, placed and refreshed. |
| `src/Traders.cs` | A trader settled at its road's end: the other camps dropped, the icon on every map. |
| `src/Relics.cs` | The furniture: game pieces copied to recover nothing, refuse the hammer. |
| `src/Clearing.cs` | Removes trees, rocks and scenery from a path; the `SpawnZone` patch. |
| `src/PathLayer.cs` | One road: search passes → trail → write → clear → landings; `LaySpurs`. |
| `src/Dev/PathCommands.cs` | `paths` commands: facts, search, lay, grow, undo, reset, signs… (Debug only). |
| `src/Dev/ThreadCheck.cs` | `paths threads`: the generator read from many threads against one, timed (Debug only). |
| `src/Dev/PathPreview.cs` | `paths preview` / `show` / `auto`, the pins and their tooltips (Debug only). |
| `src/Dev/DockCommands.cs` | `docks` commands: build, undo, capture, export, import… (Debug only). |
| `src/Dev/PlanBuildFiles.cs` | `docks export` / `import` through PlanBuild's folder (Debug only). |

## The rules that always apply

- **Server side only.** The search, the queue and every terrain write run where
  `ZNet.instance.IsServer()`: only the server sees every ZDO, so only it can tell whether a zone
  already has a terrain compiler (a second one gets destroyed, with a player's digging). Clients
  need the mod only to **see the mod's own prefabs** (`Harbours`, `Relics`).
- **A path is vanilla terrain data** (`ZDOVars.s_TCData` on the zone's `TerrainComp` ZDO): it
  outlives the mod. The mod's own data (queue, laid polylines) may vanish with it.
- **Never over the player's work:** a vertex the player raised, lowered, paved or cultivated is
  left alone. A player digging while the server writes the same zone: the server wins.
- **The search reads heights from `WorldGenerator`** on its own thread, as `HeightmapBuilder`
  does; everything else it touches is built on the main thread first (`Ground.Prepare`) and only
  read after. Writing and clearing are main thread work, budgeted per frame.
- Null-guard everything: `Player.m_localPlayer` is null on a dedicated server, always.
