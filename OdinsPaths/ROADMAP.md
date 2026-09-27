# Roadmap

The design behind each entry is in `docs/`: `architecture.md` (every source file),
`foundations.md` (terrain data, triggers, the facts verified in game), `search.md`, `laying.md`,
`network.md`, `biomes.md`, `docks.md`, `prior-art.md`.

## Up next

- **Stutter:** check a start-up growth still stutters; the log's slowest frame per road names
  the stage to fix (the writing is the likeliest).
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
- **Harbours** - docks flush with the road, the stone on the dock or beside the road, old
  buildings beside it, all as JSON blueprints made in game with `docks ... edit` / `docks capture`
  (reworked 2026-09-26, not run in game yet): check them, then rebuild the shipped blueprints in
  game (pitched roofs, doors, a Mistlands building). What to check in `docs/docks.md`.
  The blueprints themselves: the user makes their own with PlanBuild.
- **The game's harbours first, the mod's docks as the fallback.** `Mistlands_Harbour1` (dvergr
  pier, crane, guardstone, a walled hut with dvergr spawners) stands on the Mistlands' edge,
  snapped to the water: the game's only harbour location, and a natural way into the Mistlands.
  A crossing landing there (or boarding) uses its pier instead of a dock of the mod's: the search
  prefers its shore (an entry in `Entries`, or a cheaper landing near it), the stone goes where
  the pier meets the land, and buildings grow beside the road. The server knows every harbour
  from `ZoneSystem.m_locationInstances` before its zone is generated, but its objects spawn only
  then: build nothing in its footprint until they have, and measure that footprint
  (`Footprints`) - its pieces reach past its exterior radius. **Find** the swamp location that
  looks like a dock (not named like one; the `SwampHut*` stand on log piles) and whether it can
  serve the same way.
- **A busy harbour.** Make a harbour look used, not abandoned: buildings are the first step,
  then better condition than a ruin's, lit lamps, crates and barrels.
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
- **Other:** leaving the world mid-search, `paths reset` on a copy, Black Forest clearing, a lay
  out of a base, junction heights, `paths bench` at a biome border, snow in the Deep North.

## Later

- Signposts where roads meet (a vanilla `Sign`, text = where it leads).
- Spurs to portals and to dungeons a player has entered.
- Wear: busy spurs widen or turn to stone, as AntTrails does.

# Bugs

- Traders need to be fixed, before paths are layed out. This may need estimates, but these should be good enough

Nothing is released. Risks to watch:

- An exception in a growth leaves `Grower.Busy` set until the world is left.
- `paths undo` takes back only the last job, not cleared trees and rocks or links added to an
  older harbour; `paths reset confirm` takes everything, the player's digging too.
- A base is stored by its first ward; a base that moves over 150 m gets a second road.
- A compiler created while a client generates the same zone - verify no vanilla path does that.
- In a zone generated after its road, vegetation on the shoulder can float or sink a little.
- A harbour stone's height beside the road (the lower of `Ground.Height` and the road) - verify.
- A rock without meshes counts as 2.5 m wide; a scattered MineRock5 may be cleared early.
- A player digging in a zone while the server rewrites it: last writer wins.
