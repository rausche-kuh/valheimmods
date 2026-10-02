# Roadmap

The design behind each entry is in `docs/`: `architecture.md` (every source file),
`foundations.md` (terrain data, triggers, the facts verified in game), `search.md`, `laying.md`,
`network.md`, `biomes.md`, `docks.md`, `signposts.md`, `ferry.md`, `prior-art.md`.

## Up next

In the user's order (2026-10-02):

1. **Road ends: lanes** (built 2026-10-02, `docs/laying.md`). Seen in game the same day: "in
   general all connections look fine now" - roads and spurs come in straight along a lane,
   leave the sacrificial stones between them, and Yagluth's road comes through a gap between
   his rock fingers (cut at the walking band; seen working, as the other places of interest).
   Left open:
   - **Turns into a lane:** some roads reach the lane's outer end from the side and turn 90°
     into it; a real road would come in "straight from the road". Ideas: soften the corner
     into an arc outside the footprint, or search to a fan of goals in front of the lane.
   - The infected mines: under "Mistlands navigation".
   - **The Elder's arena** has terrain sticking out of its platforms; not our levelling (the
     road keeps out of its smoothed 25 m) - check against a `paths undo`.
   - Haldor's road made a strange turn instead of going straight in (fine for now).
2. **Docks:** no chest on a dock (done 2026-10-02, `ChestChance` is the buildings' only); more
   variety - mirrored docks, a varying length, more blueprints (see "Harbour blueprints").
3. **Bridges and bridge ruins** over rivers (`docs/placement.md` §4; limits under "From
   Procedural Roads' PRs").
4. **Forks on slopes:** where a main road sets out from another, the two levellings fight - a
   step or ridge where their shoulders meet. Hold the new road at the old one's height for its
   first metres and let it re-level the old shoulder, as a door path does (`Trail.Ramp`,
   `OverOwn`).
5. **Signposts in the fork** (`paths signs` works, 2026-10-02): the post stands before a
   narrow fork, its boards pointing nearly the same way. Stand it in the wedge past the split
   and turn each board along its road further on, not its first metres.
6. **Serpentines** (the user, 2026-10-02: "these still need more work"): the switchbacks on
   steep ground - the turn cost that spreads them (`docs/search.md`), the hairpin landings and
   the legs' levelling (`docs/laying.md`, Hairpins). Look at them in game first and note
   what is wrong.

Polish, after those (the user, 2026-10-02):

- **Altars and places passed by** (the user, 2026-10-01): a road passing near another instance
  of a boss altar (or a pinned group's other instances) gives it a spur, as a point of interest
  does - today only the pinned instance is connected.
- **Close but unconnected main roads** (the user, 2026-10-01; with "Webbing" below): two main
  roads that pass close without meeting get a link between them, main roads only.
- **Signposts, the rest** (`docs/signposts.md`): a post at the sacrificial stones and at a base,
  spur forks (a single board naming the spur's place, behind a setting), the words as
  translations.
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

## Mistlands navigation

The user, 2026-10-02: "currently barely usable". Today a road there is a dirt track with no
levelling and a road post with its demister every 48 m (`docs/biomes.md`, `src/Lamps.cs`,
`src/Entries.cs`); the height profile is too broken to build a road on, so fighting the
terrain further is not the way. Lean on what a traveller who can barely see would build:

- **Lamps that guide:** each lamp within sight of the last - closer on bends and climbs, not
  a fixed spacing -, a board on each post pointing the way on (a signpost's boards at a fork),
  and something to follow when you can barely see (a low line of stones or a chain between
  the posts, from the game's own pieces).
- **The infected mines** are ringed with stones, and their way in is a small hill of about
  70°: the stairs are found (`Approaches.SlopeYaw`), the road to them is not right yet.
- Questions to settle before building: what the boards say (names, or only the way), how far
  a demister clears and so how far apart lamps can stand to be seen, and whether the Ashlands
  get the same.

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

- vegetation / stones / sticks can float or sink when comming near
