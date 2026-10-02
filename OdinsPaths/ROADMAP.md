# Roadmap

The design behind each entry is in `docs/`: `architecture.md` (every source file),
`foundations.md` (terrain data, triggers, the facts verified in game), `search.md`, `laying.md`,
`network.md`, `biomes.md`, `docks.md`, `signposts.md`, `ferry.md`, `prior-art.md`.

## Up next

- **Signposts** (built 2026-10-01, `docs/signposts.md`): run `paths signs` / `paths signs place`
  in game - its "To check in game" list. Then: a post at the sacrificial stones and at a base,
  spur forks (a single board naming the spur's place, behind a setting), the words as
  translations.
- **Road ends** (seen 2026-10-01): roads that go round an Eikthyr altar without reaching it, or
  stop short of their place. Find out first whether it is the pinned instance or another one
  (next entry) and whether the new structure distance stop sends the road round it.
- **Altars and places passed by** (the user, 2026-10-01): a road passing near another instance
  of a boss altar (or a pinned group's other instances) gives it a spur, as a point of interest
  does - today only the pinned instance is connected.
- **Close but unconnected main roads** (the user, 2026-10-01; with "Webbing" below): two main
  roads that pass close without meeting get a link between them, main roads only.
- **From Procedural Roads' PRs** (read 2026-10-01, `docs/prior-art.md`), in order:
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
- **Structure distance stop** (the user's, 2026-10-01: roads keep off structures): polish, lower tier.
- Swamp paths should be dirt
- structure road ends: paint dirt without leveling - trying a wider area to mask not knowing the "stair" position
- **Harbour blueprints** (the user's own work in PlanBuild; harbours have a working foundation,
  seen 2026-10-01, but want polish and variants): only the bare minimum ships, to be replaced: `WoodJetty` (the one
  dock, old format) and `WoodHut` (the one building). Every biome builds those until it has
  its own; the user makes them with PlanBuild (`docs/docks.md`). No building joins a dock yet
  (a `dock` sign).
- **Harbour placement, the rest** (`docs/placement.md`; the quay, door heights, pads, scored
  sites and the fallback onto the dock were built 2026-10-01, see "To check in game"): steps
  at a door too steep for a path, a dock platform that carries a hut, clutter as the last
  fallback, a `docks check` pass; bridges on the same base (both ends pinned like the quay).
- **The game's harbours:** **find** the swamp location that looks like a dock (not named like
  one; the `SwampHut*` stand on log piles) and whether it can serve as the Mistlands piers do;
  spurs landing at a pier.
- **A busy harbour.** Make a harbour look used, not abandoned: better condition than a ruin's,
  lit lamps, crates and barrels.
- **The ferryman:** a ghost beside each harbour stone, there only at night, who takes the player
  to a linked harbour for coins (or a late meal for far trips and the Ashlands / Deep North).
  Plan in `docs/ferry.md`; the harbour stones work in game (2026-10-01).
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

Seen working 2026-10-01: the traders settled and revealed at their road's end, the harbour
stones revealing each other, harbours as a foundation, no stutter in a growth.

- **Names:** `$npc_haldor` / `hildir` / `bogwitch` in the messages are guesses.
- **The game's harbours** (`paths ports` near a `Mistlands_Harbour1`, then a `paths lay` across
  the sea to it): the pins on the berth and the pier's land end (the turn read from the crane),
  the road led down the pier and round the hut, the stone beside the land end (its height), no
  dock, buildings outside the harbour, how long a harbour's zone takes to generate (the log).
- **Docks and harbour buildings** (`docs/docks.md`): never run yet; the quay and the pads
  (2026-10-01) with them - its "To check in game" list.
- **2026-10-01 changes:** no grass in a dirt/stone fade (`Trail.Handover`), a player's broken
  boulder (`*_frac`) cleared off a road, no "more than one terrain compiler" warnings in the log.
- **Other:** leaving the world mid-search, `paths reset` on a copy, Black Forest clearing, a lay
  out of a base, junction heights, `paths bench` at a biome border, snow in the Deep North.

## Later

- Spurs to portals and to dungeons a player has entered.
- Wear: busy spurs widen or turn to stone, as AntTrails does.

# Bugs

- Roads that go round an Eikthyr altar without reaching it (under "Up next", road ends).
- vegetation / stones / sticks can float or sink when comming near
