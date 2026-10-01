# What the design rests on

The game facts a path as terrain data depends on: how a path is written, what it costs, where
the heights come from, what triggers it and how vegetation and snow behave. Facts without a mark
were read in `decompiled/assembly_valheim/` on 2026-09-24; facts marked **verify** still need
checking in game (`paths facts` / `paths bench` in `src/Dev/PathCommands.cs`, `deploy -c Debug`),
and what a check shows goes into `CLAUDE.md`.

**The server does everything, in one pass, without loading a single zone.** A zone's terrain
edits (everything the hoe, the pickaxe and the cultivator ever did there) live in one blob on one
ZDO: the zone's *terrain compiler*, a `TerrainComp` prefab placed at the zone centre
(`Heightmap.GetAndCreateTerrainCompiler`, `ZoneSystem.GetZonePos` = `id * 64`, y 0).
`TerrainComp.Save` writes the blob to `ZDOVars.s_TCData`, and the format is simple enough to
write ourselves: version `1`, operation count, last point and radius, then per vertex
`bool modifiedHeight [+ float levelDelta, float smoothDelta]`, then per vertex
`bool modifiedPaint [+ r g b a]`, all run through `Utils.Compress`. So the mod can write a whole
path as data:

- a zone that has a compiler: decompress the blob, merge the path in, write it back;
- a zone without one: `ZDOMan.CreateNewZDO(zoneCenter, compilerPrefabHash)` with the same
  `Persistent` / `Type` / `SetPrefab` / `SetRotation` that `ZNetView.Awake` sets, plus our blob.

Only the server may create compilers this way. It holds every ZDO, so it can see whether a zone
already has one. A client sees only the ZDOs near it and could make a second compiler, and
`TerrainComp.Awake` destroys a duplicate. That would wipe a player's digging. A client that has
the zone loaded picks up the change by itself: `TerrainComp.Update` → `CheckLoad` reloads whenever
the ZDO's `DataRevision` moves, then pokes the heightmap and resets the grass. The precedent is
`ValheimServersideQoL` (in `~/Documents/Code/test/othervalheimmods/`, no license, so read only),
whose admin options rewrite `s_TCData` on the server.

The exact base heights and base mask of any zone, loaded or not, come from
`HeightmapBuilder.instance.RequestTerrainSync(zonePos, width, scale, false, WorldGenerator.instance)`.
That is what the game builds heightmaps from, biome blending included. Deltas computed against
it are the deltas the game will add.

**Because it is ordinary terrain data, only the server needs the mod.** Clients without it see
the paths, and the paths stay when the mod is removed. In a local game the host is the server.

**What it costs.** The blob has a fixed shape per zone whatever is in it: `(width+1)²` vertices,
`65² = 4225`, since the zone heightmap is 64 × 1 m (`paths facts`, 2026-09-24; the compiler
prefab is `_TerrainCompiler`). A path 3 m wide crosses a zone on about 200-300 vertices. At
1 + 8 + 16 bytes each, plus a bool for every other vertex, that compresses to a few KB. A 3 km
route touches about 50-80 zones: a few hundred KB of world save, sent to a client only when it
loads those zones. Loading such a zone costs what loading any hoed zone costs: one heightmap and
collider rebuild. **Levelling costs the same as painting.** Level and smooth deltas sit in the
same blob, and a changed height rebuilds the same mesh as a changed colour. So ground levelling
is no performance question. What limits it is how it looks, and the clamp in
`ApplyToHeightmap`: final height is kept within **±8 m of the base height**.

**Heights for the search come from the world generator, not from loaded terrain.**
`WorldGenerator.GetHeight(x, z)` is the biome lookup plus that biome's noise. It works anywhere,
on any server, loaded or not. It is safe off the main thread: the game's own `HeightmapBuilder`
thread reads it all the time, the river cache (`m_cachedRiverGrid` / `m_cachedRiverPoints`) is
behind a `ReaderWriterLockSlim` in the current build, and the static biome caches serve only
`GetBiomeArea(Vector2s)`, which the search does not call (checked 2026-09-25). So the search
runs on a thread of its own (`[Performance] SearchThread`; off, it is a coroutine on the main
thread with a per-frame budget, as it was first built, when the river cache had no lock). `Ground`
reads the zone prefab once on the main thread first. `paths bench` measured **1.5 µs per sample** (40 000 in 61 ms, 2026-09-24).
The exact zone build takes ~20 ms through `RequestTerrainSync`, but that is mostly waiting on the
builder thread: its non-blocking twin `RequestTerrain` queues a zone and returns null until it is
ready. That thread sleeps 10 ms after every zone and keeps only 16 ready, and a walking player's
zones queue behind the mod's, so `TerrainWriter` calls the builder's own `Build` on threads of its
own instead (2026-10-01): the same heights, without the queue.
Heights the generator does not know about are player digging, the flattening that locations do
with `TerrainModifier`, and vegetation: trees, rocks, the Mistlands' giant rock formations.

**GetHeight is not the built ground** (read 2026-09-25 in `HeightmapBuilder.Build`). The builder
takes the biomes at the zone heightmap's four corners, not at the vertex, and where they differ
blends their `GetBiomeHeight` with a smoothstep across the zone; a small patch of another biome
inside a zone is never built. So the search, the trail profile and the landings use
`Ground.Height` (`src/Ground.cs`), which replays that per point with the corner biomes cached per
zone - at most four biome heights a sample, one where the corners agree. `paths bench` compares
it with a real build of the player's zone. Found through Procedural Roads, `prior-art.md`.
`search.md` deals with each.

**The triggers are two vanilla calls on the server.**

- *Revealed altar:* `Vegvisir.Interact` → `Game.DiscoverClosestLocation` → the routed
  `RPC_DiscoverClosestLocation(sender, name, point, pinName, pinType, showMap, discoverAll)`,
  which the server handles with `ZoneSystem.FindClosestLocation`. A postfix there (server only)
  repeats the lookup and queues a path when `name` is a boss location. Names confirmed by
  Odin's Compass's location dump: `Eikthyrnir`, `GDKing`, `Bonemass`, `Dragonqueen`,
  `GoblinKing`, `Mistlands_DvergrBossEntrance1`, `FaderLocation`. The queue is keyed by location
  position, so reading the same stone twice queues nothing new.
- *Sleep:* `Game.UpdateSleeping` (server, every 2 s) calls `EnvMan.SkipToMorning` once everyone
  is in bed. The skip runs for **12 seconds** (`m_timeSkipSpeed = remaining / 12`), then
  `SleepStop` goes out. A postfix on `SkipToMorning` starts the queued searches. Writing waits
  for a finished search, so the path is ready at wake up when the search fits in the skip, and
  appears a little later when it does not. Nobody is out walking at night anyway.

**Starting points.** The sacrificial stones are the `StartTemple` location
(`Game.m_StartLocation`). The server has its instance in `ZoneSystem.m_locationInstances`, and
`ZoneSystem.GetLocationIcon("StartTemple", out pos)` works too. Bases and existing paths:
`network.md`.

**Vegetation can be kept off the path before it exists.** `ZoneSystem.SpawnZone` generates a
zone once, on the server (`SpawnMode.Full`, or `Ghost` for zones nobody stands in). It calls
`PlaceLocations` and then `PlaceVegetation(..., clearAreas, ...)`, and every vegetation spawn is
skipped `InsideClearArea(clearAreas, p)`. Built instead (`Clearing.cs`): a postfix on
`SpawnZone` that, for a zone generated just now, removes the clearable vegetation ZDOs near the
dirt in the zone's terrain data - no trail storage needed, and it sees exactly which vegetation it
removes, which `InsideClearArea` does not. In zones already generated, the lay itself removes the
vegetation ZDOs near the trail (`ZDOMan.FindObjects`, `SetOwner` to the server, `DestroyZDO`).
Vegetation without a `ZNetView` is placed again by every client on every load and cannot be
cleared by the server. Grass is clutter, not vegetation: `ClutterSystem` reads the paint mask,
so painted ground is bare.

**Snow.** In the Deep North the paint mask's green channel, which is cultivation everywhere else,
is **snow depth**. `Heightmap.GetHeightOffset` sinks the feet by it, and `GetGroundMaterial`
turns it into `SnowDeep` / `SnowVeryDeep`. `PaintCleared` treats `Cultivate` in the Deep North as
*adding* snow. Every other paint type lerps the channel toward its own green, which is 0 for
dirt and for paving, so **a dirt path digs itself out of the snow** at no extra cost.
`TerrainComp.SetSnowMask` shows the base depth comes from the Deep North's `GetBiomeHeight`
mask. The Mountain's snow is the biome's texture, not a channel. Whether dirt or paving paint
shows through it the way a hoed path does is a **verify** (expected: yes).

## Verified in game (2026-09-24, `paths facts` / `paths bench`, local game)

- The zone heightmap is **64 wide at scale 1**: `TerrainComp` arrays are 65 × 65, vertex `x` sits
  at `zoneCenter.x - 32 + x`. The compiler prefab is **`_TerrainCompiler`**, placed at the zone
  centre with y 0. Water level 30.
- `WorldGenerator.GetHeight` costs **1.5 µs** per sample. `RequestTerrainSync` for one zone takes
  ~20 ms, mostly waiting on the builder thread - call `HeightmapBuilder.Build` on a thread instead.
- Search wall times at the 6 ms budget, one sample per cell (2026-09-24): to Yagluth 1 s at 64 m,
  4 s at 32 m, 16 s at 16 m, plus 6 s for the 4 m pass in its corridor; 32 m is good, 64 m alone
  hops into water. With five-point sampling `128 32:512 8:128` gave a good line to Yagluth 4.5 km
  out, and sea 2 with 700 m each way felt right. The numbers behind the passes in `docs/search.md`.
