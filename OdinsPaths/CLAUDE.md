# OdinsPaths

A road network that grows across the world: paved main roads from the sacrificial stones and
the bases to the boss altars (in progression order) and the traders, forking off each other, and
dirt spurs from them to the villages, crypts and camps nearby (`docs/network.md`). See the root
`CLAUDE.md` for the shared build, the scripts and the environment.

**State:** the search, shaping, terrain writing, clearing and landings have been tried in game
through `paths lay`; the road kinds, network, planner, spurs and triggers were built on
2026-09-25 and the first start-up growth ran the same day (it worked, and stuttered). The
harbour stones, docks and harbour buildings (2026-09-26) have not run in game yet. A Release
build grows roads on the server 10 s after loading and after every sleep, unless `[Network] Grow` is off; a
**Debug build holds that** until `paths auto on` (`Grower.Held`), so a test world changes only
through the dev commands. Nothing is released — `VERSION` stays 0.1.0 until the first
Thunderstore upload; `package/icon.png` is a generated placeholder.

## Docs

| File | Read it when |
| --- | --- |
| `ROADMAP.md` | Picking the next task: up next, what to check in game, later, bugs and risks. |
| `docs/architecture.md` | Changing a source file: what each one does in full, settings and dev commands with their arguments. |
| `docs/foundations.md` | Terrain data, triggers, heights, snow; the facts verified in game (zone size, timings). |
| `docs/search.md` | Route costs, the search passes, going around buildings. |
| `docs/laying.md` | Paint, levelling, clearing, landings. |
| `docs/network.md` | The two tiers, progression, traders, bases, storage. |
| `docs/biomes.md` | What each biome asks of a road. |
| `docs/docks.md` | Docks and harbour buildings: placement, the blueprint format (PlanBuild's), making them in game or with PlanBuild. |
| `docs/placement.md` | Fitting docks, buildings, bridges to the terrain: other games, what is built, what is left. |
| `docs/ferry.md` | The night ferryman at the harbours (plan): the ghost, choosing a harbour, price and payment, the crossing. |
| `docs/prior-art.md` | Procedural Roads and `PLAN.md` weighed, why no Jötunn. |

## Source map

| File | What |
| --- | --- |
| `src/OdinsPaths.cs` | Entry point and every setting. |
| `src/PathSearch.cs` | A* over cells on a thread, step costs, sea/landing costs, `Survey` heuristic. |
| `src/Entries.cs` | Ways into the Mistlands; `Worker`, the thread waited for frame by frame. |
| `src/Corridor.cs` | The strip around a coarse route the fine pass may search. |
| `src/Ground.cs` | Ground height as `HeightmapBuilder` builds it, not `GetHeight`'s. |
| `src/RoadKind.cs` | `Main` (paved) and `Spur` (dirt): paint, width, edge, levelling. |
| `src/Trail.cs` | A route shaped for laying: smoothed, water/causeway flags, grade, hairpins, cuts. |
| `src/TerrainWriter.cs` | Writes a trail into each zone's `TerrainComp` blob: levelling, then paint. |
| `src/Network.cs` | The laid network on a data ZDO at (-15000, -15000); `Starts`. |
| `src/Planner.cs` | What is due, in order, each job's starts; `Run` lays a job and its spurs. |
| `src/Progress.cs` | Progress bar, message-log lines, location names, the frame watch. |
| `src/Grower.cs` | Triggers (start-up, sleep, vegvisir) and the shared `Busy` flag. |
| `src/Structures.cs` | Built pieces and obstacles in the search area, gathered off-thread. |
| `src/Lamps.cs` | Demister road posts every 48 m in the Mistlands. |
| `src/Footprints.cs` | How far a location's buildings reach, measured once, cached per game version. |
| `src/Landings.cs` | Landings at sea crossings; `Spawn` marks what the mod places. |
| `src/Ports.cs` | The game's harbours (Mistlands piers) used before a dock: read, cheaper landing, the route down the pier. |
| `src/Harbours.cs` | Harbour stones: the mod's vegvisir prefab, linked across the sea. |
| `src/Blueprints.cs` | Harbour blueprints: settings JSON + PlanBuild `.blueprint`, loading (`assets/harbours/`, config), saving. |
| `src/Json.cs` | A small JSON reader for the blueprints (`JsonUtility` left their lists empty). |
| `src/Builder.cs` | Raises a blueprint: weathering, piles to the ground, spots, chests, spawners. |
| `src/Docks.cs` | A harbour's dock: flush with the road, fitted to the shore. |
| `src/Buildings.cs` | Old buildings at a harbour: door at the road's height, on a pad, a path to it; or onto the dock. |
| `src/Pad.cs` | A building's levelled ground. |
| `src/Traders.cs` | A trader settled at its road's end: the other camps dropped, the icon on every map. |
| `src/Relics.cs` | The furniture: game pieces copied to recover nothing, refuse the hammer. |
| `src/Clearing.cs` | Removes trees, rocks and scenery from a path; the `SpawnZone` patch. |
| `src/PathLayer.cs` | One road: search passes → trail → write → clear → landings; `LaySpurs`. |
| `src/Dev/PathCommands.cs` | `paths` commands: facts, search, lay, grow, undo, reset… (Debug only). |
| `src/Dev/PathPreview.cs` | `paths preview` / `show` / `auto`, the pins and their tooltips (Debug only). |
| `src/Dev/DockCommands.cs` | `docks list` / `reload` / `build` / `house` / `undo` / `capture` / `export` / `import` (Debug only). |
| `src/Dev/PlanBuildFiles.cs` | `docks export` to PlanBuild's folder, `docks import` of its captures, fitted back (Debug only). |

## The rules that always apply

- **Server side only.** The search, the queue and every terrain write run where
  `ZNet.instance.IsServer()` — the dedicated server, or the host of a local game. Clients must
  not write terrain for the mod: only the server sees every ZDO, so only it can tell whether a
  zone already has a terrain compiler (a second one gets destroyed, and a player's digging with
  it). The one thing a client needs the mod for is **seeing the harbour stones and the harbours'
  furniture** (`Harbours`, `Relics`, prefabs of the mod's own); everything else works on a vanilla client.
- **A path is vanilla terrain data** (`ZDOVars.s_TCData` on the zone's `TerrainComp` ZDO), so it
  outlives the mod and every client sees it. The mod's own data (queue, laid polylines) may
  vanish with the mod.
- **Never over the player's work:** a vertex the player raised, lowered, paved or cultivated is
  left alone - meant for the terrain work already there, in bases. A player digging while the
  server writes the same zone is not guarded against: the server wins.
- **Heights for the search come from `WorldGenerator`**, which the search reads on a thread of its
  own, as the game's `HeightmapBuilder` does (its river cache is locked). Everything else the search
  touches is built on the main thread before it starts and only read after; Unity objects stay
  on the main thread (`Ground.Prepare`). The writing and the clearing are main thread work,
  budgeted per frame.
- Null-guard everything: `Player.m_localPlayer` is null on a dedicated server, always.
