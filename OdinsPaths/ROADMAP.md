# Roadmap

Design and verified facts for each point are in `docs/` (index in `CLAUDE.md`).

## Up next

In the user's order (2026-10-02).

1. **Level forks.** A road setting out from another leaves a step where the levellings meet.
   Likely cause: a later lay never re-levels the older road's vertices (`OverOwn` only covers
   its own lay). Confirm with a `paths lay` off an existing road on a slope, then hold the new
   road at the old one's height for its first metres and let it re-level the old shoulder.
2. **Working serpentines.** Switchbacks fail on most steep ground. Lay a few steep climbs, sort
   the failures (no switchback, too tight, legs digging into each other, bad landings), fix
   that part. Fallback: plan the zig-zag explicitly and search between the turns.
3. **Places in cliffs.** Troll caves (and hill crypts) in a cliff face get bad spurs. Note what
   fails in game; end the spur at the cliff's foot or skip places whose approach is too steep.
4. **Mistlands navigation** - "not functional at all". The lamps are the road there:
   - **No dead ends:** find why lines of lamps stop - posts inside rocks spawned after the lay
     (clear round each post), skipped spots leaving gaps (try nearer spots), or the road itself
     ending at its entry (`Entries`).
   - **Own road lamps:** a colour of their own (a mod prefab), set apart from the Dvergr lamps.
   - **Followable:** lamps spaced by line of sight, closer on bends; something to follow
     between them (stones, chains).
   - **Touch for the next lamp:** interacting turns the camera to the next lamp on; the hover
     names where the road leads.
   - **Infected mines:** the road up to their stairs.

## Then

5. **Road ends, polish:** no 90° turn into a lane (arc, or a fan of goals in front of it);
   check the Elder's arena terrain against a `paths undo`.
6. **Bridges** over rivers, ruined wooden ones for wide crossings (`docs/placement.md`).
7. **Signposts:** in the wedge past a fork with boards along each road further on; posts at the
   stones and bases; spur boards; translated words (`docs/signposts.md`).
8. **Harbours:** the user's blueprints and dock variety, the rest of the placement
   (`docs/placement.md`), the game's swamp dock as a harbour, a lived-in look (lamps, crates),
   wrecks beside them and spurs to the game's wrecks, the night ferryman (`docs/ferry.md`).
9. **A denser network:** spurs to altars and places a road passes by, links between close main
   roads (webbing), bases connected, short paths to nearby houses and around bases, houses at
   forks; later spurs to portals and entered dungeons.
10. **Road looks:** varying width and grass, swamp roads in dirt, a batter widening with deep
    cuts, structure road ends painted without levelling, an explicit water floor, wear on busy
    spurs.
11. **Settings and performance:** clearing options, exploration presets, structure distance,
    border rocks read from neighbour zones, a coarse terrain cache, coverage in `paths grow all`.
12. **Release:** mod page, translations, icon, admin commands for manual roads, roads on the
    map, first Thunderstore upload.

## To check in game

- Message names (`$npc_haldor` / `hildir` / `bogwitch` are guesses).
- The game's harbours, our docks and harbour buildings (never run; `docs/docks.md`).
- No grass in a dirt/stone fade, broken boulders cleared, no "more than one terrain compiler".
- Leaving mid-search, `paths reset` on a copy, Black Forest clearing, a lay out of a base,
  junction heights, `paths bench` at a biome border, snow in the Deep North.

## Bugs

- Vegetation, stones and sticks can float or sink when coming near.
