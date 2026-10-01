# Roadmap

The design behind each entry is in `docs/`: `architecture.md` (every source file),
`foundations.md` (terrain data, triggers, the facts verified in game), `search.md`, `laying.md`,
`network.md`, `biomes.md`, `docks.md`, `prior-art.md`.

## Up next

- **Stutter, if a growth still hitches** (see "To check in game"): a harbour's dock and buildings
  over frames (`Builder`); the clearing one zone a frame with the structures already gathered.
- **From Procedural Roads' PRs** (read 2026-10-01, `docs/prior-art.md`), in order:
  - **Compiler ownership:** `TerrainWriter` sets `s_TCData` whoever owns the compiler; a
    client digging there at the same time can wipe the road, or the road its digging. Defer
    such a zone while the owner is a client in it (`IsInPeerActiveArea`), retry later.
  - **Location terrain at road ends:** the game applies a location's own `TerrainModifier`
    levelling before our deltas, and the writer's skip circles leave out the start and goal
    locations (`PathLayer.LocationsAround`), so deltas there are off (theirs up to 9 m). Read
    the prefab's level/smooth radius and keep the levelling out of it.
  - **End-aware profile:** `Trail`'s ±5 point mean is one-sided at road ends and at the water
    break - a shelf on slopes. Fit a line there instead.
  - **Batter for deep cuts:** the shoulder is a fixed 1.5 m while a main road cuts up to 4 m;
    widen it with the cut depth past ~2 m, cuts only.
  - **Border rocks:** a new zone's clearing reads only its own paint, so a rock rooted there
    over a road in the next zone stays. Read the neighbours' compilers within the rock's reach.
  - **An explicit water floor** in the levelling (water + `ShoreMargin`); today only the profile
    keeps it.
  - **Ruined wooden bridges** for deep or wide rivers, built through `Builder`. Their verified
    limits: 16 m free span (20 m falls), piers ~18 m at most, hammer turns in 22.5° steps (so
    a level deck), crossings already reached by road cost half.
  - **Coverage** in `paths grow all`: the share of land within 250 m of a road.
  - **Release admin commands** for a manual road (`paths lay` is Debug only).
  - **Roads drawn on the map** - their issue #8, nobody has it yet.
- **Clearing:** a setting for it / all and every type?
- **Coarse terrain cache** (`docs/prior-art.md`), once a whole growth's times are known.
- **Release:** mod page, translations, icon, first Thunderstore upload.
- more natural roads with varying degrees of width / overgrowth of gras
- dialing in presets for shortest exploration / see or land focused exploration
- Connection of bases
- **Webbing** networks get addtional paths in between
- Swamp paths should be dirt
- sign post for the main network
- structure road ends: paint dirt without leveling - trying a wider area to mask not knowing the "stair" position
- **Harbour blueprints:** rebuild the shipped ones (pitched roofs, doors, a Mistlands
  building); the user makes them with PlanBuild (`docs/docks.md`). Only `WoodHut` has a door
  so far: the old-format buildings are placed by their front, and none joins a dock yet (a
  `dock` sign). Their deco spots are all `deco_h1` (the old `deco`).
- **Harbour buildings on the terrain:** fit a building and its door to the ground at the door
  (today the floor goes at the highest ground and the door path follows the ground).
- **The game's harbours:** **find** the swamp location that looks like a dock (not named like
  one; the `SwampHut*` stand on log piles) and whether it can serve as the Mistlands piers do;
  spurs landing at a pier.
- **A busy harbour.** Make a harbour look used, not abandoned: better condition than a ruin's,
  lit lamps, crates and barrels.
- **Wrecks at the harbours.** A `wreck` blueprint kind from the game's wreck parts
  (`shipwreck_karve_*`, `shipwreck_vikingship_*`; **verify** they spawn by name through
  `ZNetScene`), laid in the shallows beside a harbour so it feels alive.
- **Spurs to the game's wrecks**, only if they cost little search time. The locations:
  `ShipWreck01`-`04` (shores of the Swamp, Black Forest, Plains and Ocean), `ShipWreck01_DN` /
  `02_DN` (Deep North shore), `FrozenShip01`-`03` (Deep North, in the ice; told apart by name).
  Most lie in water or ice, so a spur ends at the nearest shore, not at the wreck, and wrecks
  close together (they form graveyards) share one spur to one shore point.
- paths around bases / structures that the path did not target
- always add mini paths to close by structures (houses)
- spawn houses at road forks

## To check in game

- **Names:** `$npc_haldor` / `hildir` / `bogwitch` in the messages are guesses.
- **The game's harbours** (`paths ports` near a `Mistlands_Harbour1`, then a `paths lay` across
  the sea to it): the pins on the berth and the pier's land end (the turn read from the crane),
  the road led down the pier and round the hut, the stone beside the land end (its height), no
  dock, buildings outside the harbour, how long a harbour's zone takes to generate (the log).
- **Stutter:** a full `paths grow all` (2026-10-01, 21 roads in 128 s) had no hitch with a
  garbage collection and none left in "Looking at what stands in the way"; its slowest frames
  (59-367 ms, roads with sea crossings) were the harbours being built, put down to the clearing.
  They now build one per frame under their own stage - check which stage the next growth names,
  and split a harbour's dock from its buildings if one harbour alone is still a hitch.
- **Docks and harbour buildings** (`docs/docks.md`): never run yet.
- **2026-10-01 changes:** no grass in a dirt/stone fade (`Trail.Handover`), a player's broken
  boulder (`*_frac`) cleared off a road, no "more than one terrain compiler" warnings in the log.
- **Other:** leaving the world mid-search, `paths reset` on a copy, Black Forest clearing, a lay
  out of a base, junction heights, `paths bench` at a biome border, snow in the Deep North.

## Later

- Signposts where roads meet (a vanilla `Sign`, text = where it leads).
- Spurs to portals and to dungeons a player has entered.
- Wear: busy spurs widen or turn to stone, as AntTrails does.

# Bugs

- Traders need to be fixed, before paths are layed out. This may need estimates, but these should be good enough

Nothing is released. Risks to watch:

- An exception in a road now stops the growth (`Grower.Guarded`) without marking the job: its
  half-written terrain stays, the road is not in the network, and the next trigger tries again.
  `paths preview` is not guarded.
- `paths undo` takes back only the last job, not cleared trees and rocks or links added to an
  older harbour; `paths reset confirm` takes everything, the player's digging too.
- A base is stored by its first ward; a base that moves over 150 m gets a second road.
- A compiler created while a client generates the same zone - verify no vanilla path does that.
- In a zone generated after its road, vegetation on the shoulder can float or sink a little.
- A harbour stone's height beside the road (the lower of `Ground.Height` and the road) - verify.
- A rock without meshes counts as 2.5 m wide; a scattered MineRock5 may be cleared early.
- A player digging in a zone while the server rewrites it: last writer wins (see compiler
  ownership above).
- Our pre-written compiler in an ungenerated zone: if generation ever makes a second one, the
  game keeps the one with more operations (`TerrainComp.Awake`) - ours has one. Verify no
  location carries a `TerrainOp`.
