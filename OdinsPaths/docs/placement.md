# Placing built things on terrain: research and plan

Research of 2026-10-01 into the usual problems of placing procedural buildings: a hut beside the
harbour standing well up the slope with its door out of reach, a dock not flush with the road,
and later bridges whose two banks are at different heights. Covers how other games and papers
handle it and what OdinsPaths should take from them. Steps 1-4 of the order of work were built
the same day, partly (see "Built" at the end), and have not run in game; `docs/docks.md`
describes what is there.

## Why it goes wrong today

The order of work is the problem: **the road's profile is fixed first, the dock is fitted to the
road afterwards, and the buildings to the untouched ground.** No step changes the terrain for a
building, and no step changes the road for a dock.

- **Buildings** (`Buildings.BesideRoad` / `Ground`): the floor goes at the *highest* natural
  ground under the footprint (`Ground.Height`, which does not include the road's levelling), allowed
  up to `MaxRise` above the lowest. The door's height is never compared with the road's. Where the
  road is cut into a slope, the ground beside it is already above the road by the cut, then the
  slope across the setback adds more, then `MaxRise` adds more: the door ends up metres up the
  hill. The door path is a straight two-point `Trail` with no grade check, so it cannot fix that.
- **Docks** (`Docks.ForHarbour`): the deck is the road's height `Inland` of the shore, clamped
  to `DeckAboveMin`..`DeckAboveMax` above the water. On a bank higher than the clamp the deck sits
  lower than the road, and `LandEndSearch` only looks *inland* for a point as high as the deck,
  so a low road gets a step up and a high one gets a step down. The trail's profile does not know
  a dock will be there.
- **Bridges** (roadmap): two banks, two heights, one deck that has to be level (hammer turns),
  and no way yet to change the road's profile for it.

## How others do it

### Minecraft: jigsaw structures (villages, bastions, trail ruins)

- **Each piece is attached at a connector, never placed by its footprint.** A house's jigsaw
  block is at its door and links to a street's jigsaw block, so a door always faces a street and
  sits at the street's height. The house's height comes from that connection, not from the ground
  under it.
- **Projection, per pool:** `terrain_matching` pieces (streets) follow the heightmap block by
  block; `rigid` pieces (houses) keep their shape and take their height from the connector.
- **Terrain adaptation** (since 1.18, the "beardifier"): the terrain is changed to fit the
  structure, not the other way round. `beard_thin` adds ground under the structure and removes
  it just above the ground; `beard_box` removes it from the whole box; `bury` and `encapsulate`
  cover it. All of these fade out with distance, so the result looks like a natural mound or
  cutting, not a cube.
- **Fallback pools:** every pool names another pool to use when none of its pieces fit
  (collision, out of bounds) or the depth limit is reached. That is how a street ends in a
  cap instead of running into a cliff. It is the "a hut on the dock instead" idea already worked
  out as data.
- Before 1.18, villages took the *average* ground height over a piece's box and filled columns
  of cobblestone down to the ground. That is close to what we do now, and it gave the famous
  floating and buried village doors.

### Valheim itself

- A location is rejected when the terrain under it varies more than `m_maxTerrainDelta` (or
  less than `m_minTerrainDelta`, `ZoneSystem.cs`). That is a site budget, checked before
  anything is placed.
- After that, the location's `TerrainModifier`s **level** (`m_level`, `m_levelRadius`,
  `m_levelOffset`) and **smooth** (`m_smoothRadius`, `m_smoothPower`) the ground to the
  location's own height: the beard, in Valheim terms.
- `Mistlands_Harbour1` is the precedent for us: its modifiers raise the ground at the pier's
  land end to the deck's height and dredge the berth (`docs/docks.md`). The game makes the
  terrain meet the pier. It does not search for a height where they already meet.
- `m_slopeRotation` turns a location to face down the slope: orientation chosen from the
  terrain.

### Emilien et al., *Procedural Generation of Villages on Arbitrary Terrains* (2012)

- A house's footprint starts **at the closest point to the road, turned to the road's normal**,
  and grows inside its parcel. Parcels grow with an anisotropic slope cost, so they spread along
  contour lines, and on a slope you get terraced rows along the hill, not up it.
- **Open shape grammar:** a door (or window, or stair) is first put where it fits best, then
  moved across the wall by a priority queue weighted by a displacement kernel (sideways is cheap,
  vertical is expensive) until it no longer meets the ground. After N failed tries the rule
  *fails*, and the grammar takes another branch. Stairs are adaptive elements of the same kind.
- A fisherman village is the same algorithm with "distance to the sea" weighted up.

### Roads, bridges and city builders

- Galin et al., *Procedural Generation of Roads* (2010): the route is a weighted anisotropic
  shortest path that pays for slope, rivers and other obstacles, with bridges and tunnels as
  costed transitions. Then the terrain is excavated along the path. The search already knows
  what a crossing costs.
- City and transport builders (Cities: Skylines, Transport Fever, OpenTTD and the like) solve
  the road's **vertical profile first**, under a grade limit, then classify each stretch by its
  height over the terrain: embankment, cutting, bridge where it is too high, tunnel where it is
  too low. A bridge's ends are just two points of that profile. Buildings level their lot to
  the road at the frontage and refuse lots steeper than they tolerate.

### The common pattern

1. **Pick the height from the connection** (door ↔ road, deck ↔ road, deck ↔ bank), not from
   the footprint.
2. **Change the terrain to fit**, with a falloff, and reject a site by how much has to change
   (a cut/fill budget), not by how much the natural ground varies.
3. **Change the road to fit**, where it is the road that has to meet a fixed thing (a dock, a
   bridge): pin the profile and let the grade limit ease the road into it.
4. **Fall back by data**: a chain of ever simpler options, each tried only when the one before
   fails.

## What to do in OdinsPaths

### 1. A harbour is one site with one height (fixes the dock)

Choose the **quay height** `Q` first: from the water level and the bank, inside the deck bounds.
Then **pin the trail's profile to `Q`** over the last few metres before the shore, the way
`Trail` already forces causeways to their height. `LimitGrade` then eases the road down to it,
and where it cannot, that is the "too steep for a harbour" signal (try another landing, or the
next fallback). The dock's land end goes at the end of the pinned stretch, at `Q`. Flush by
construction, with no search afterwards. The pinned stretch can widen into a small quay
(a wider `RoadKind` reach there) that the buildings can stand on.

### 2. Buildings hang off the road, then the ground is shaped to them (fixes the hut)

- **Door height from the fork:** `door = road height at the fork + at most the door path's
  length × the spur's grade`. The building is rigid, the door path terrain matching, as in
  Minecraft. The floor follows from the door (`Builder.Entry`'s foot), not from the ground.
- **A pad, as Valheim's locations do it:** level the footprint plus a margin to the floor's
  height, with a smoothed falloff. A cut where the ground is higher, piles where it is lower
  (the piles exist already), and fill only where the gap is small. That needs a new write shape
  in `TerrainWriter` (a levelled polygon or a rotated box with a falloff ring) beside trails.
  The same rules apply: skip the player's vertices, skip locations.
- **Reject by earthwork, not by `MaxRise`:** sum the cut and fill under the pad and reject past
  a budget, as `m_maxTerrainDelta` does. Cut depth matters most, since a deep cut next to a hut
  looks like a quarry; it can use the batter from the roadmap.
- **Score instead of first fit:** today the first candidate along the road wins. Collect every
  candidate (the distance along the road, the side, the setback, and also the house turned so
  its long side runs along the contour, as Emilien's parcels do) and take the cheapest:
  earthwork, door path grade, distance from the dock.
- **Door path grade:** check the path's rise against the spur's grade. If it is too steep,
  first set the building further back (a longer path), then add steps: a short `stairs`
  blueprint attached at the door, an adaptive element in the open shape grammar sense.

### 3. A fallback chain, as data (the "hut on the dock" idea)

Minecraft's `fallback` pool, applied to harbours. For each building wanted, try in order and
stop at the first that fits:

1. Beside the road, on a pad (above).
2. Beside the road on piles, only on the downhill side, so the door stays at road height and
   the floor runs out over the slope (no cut).
3. Joined to the dock (`dock` spot, exists).
4. **On the dock:** a dock blueprint variant with a platform and a `building` spot that takes a
   small building, or a dedicated "pier with a hut" dock blueprint. On a steep shore this should
   be tried *first*, since that is how real cliff harbours look.
5. Clutter only: crates, a lean-to, a drying rack on the quay or the dock.
6. Nothing.

As data: a blueprint gets an optional `fallback` (the name of a blueprint or a kind), and the
harbour reorders the chain by the shore it found (steep: dock first; flat beach: the road first).
That keeps `Buildings.ForHarbour`'s loop, which already tries blueprints in turn, and gives it
an order with meaning.

### 4. Bridges: two pinned ends and a choice of deck

- **In the search:** pay for `|bank height − bank height|` and for the approach earthwork in a
  crossing's cost, so the search prefers crossings with matching banks (Galin's costed
  transitions; the roadmap already halves the cost of reused crossings).
- **Abutment heights:** read both banks, choose the deck height(s), **pin the profile** at both
  ends (as for the quay), and let `LimitGrade` build the approaches: an embankment on the low
  bank, a cutting on the high one (the batter for deep cuts again).
- **The deck, by height difference:**
  1. Small: one level deck at a height between the banks, the approaches take up the rest.
  2. Medium: a **stepped bridge**: each span level, at its own height, steps or a short ramp
     on the pier between spans. That stays within "level deck per blueprint" and the known span
     and pier limits.
  3. Large (a river cut into a mountainside): bridge at the low bank's height and put the
     climb on the high bank, as a switchback the search finds by itself if the pinned end is a
     goal, or reject the crossing.
  - An inclined deck is possible since the mod spawns the pieces itself, but blueprints are made
    level and support on tilted pieces is unknown. Check it in game before relying on it.
- Bridges reuse the harbour's machinery: a blueprint per span, `Builder` raising it, piles to
  the ground.
- **Limits** (from Procedural Roads' PRs, read 2026-10-01): 16 m free span (20 m falls), piers
  ~18 m at most, the hammer turns in 22.5° steps (so a level deck); crossings already reached by
  road cost half.

### 5. A check after placing

A cheap pass after a harbour is built: walk from the road to every door and onto the dock
through the final heightfield and the pieces, and log every step higher than a player can walk
up. A dev command (`docks check`) pins the failures. This catches what the rules above miss,
before anyone sees it in game.

## Order of work

1. The quay: pinned profile + dock at `Q` (small; `Trail` already pins causeways).
2. The door height from the fork + the door path grade check (small, removes the worst case).
3. The pad write in `TerrainWriter` + the earthwork budget + scored candidates.
4. The fallback chain and an "on the dock" blueprint.
5. The check pass.
6. Bridges, on top of 1 and 3.

## Built (2026-10-01), and what is left

- **Quay:** `Trail.PinQuays` / `Quay`, `Docks.ForHarbour` takes its deck from it.
- **Door height, door path, pad, scoring:** `Buildings.Site` / `BesideRoad`, `Trail.Ramp`
  (with `OverOwn`, the writer re-levelling the road's shoulder the same lay wrote),
  `src/Pad.cs`, `TerrainWriter.WritePad`, `Builder.Frame.Pad` / `GroundAt`.
- **Fallbacks:** beside the road → onto the dock by the door (dock first on a `Steep` shore)
  → the blueprint's `fallback`. Not built: piles-only on the downhill side as its own step
  (the scoring covers it: piles cost least), a dock platform with a `building` spot, clutter
  only, a house turned along the contour.
- **Steps at a door** (a `stairs` blueprint) where the path would be too steep: not built; such
  a site is not taken.
- **The check pass** (`docks check`): not built; the harbour's log line gives each door's height
  against the road instead.
- **Bridges:** not built; they need the river crossings first.

## To check in game

- How high a step a player walks up, and how steep a slope, before jumping (not found as a
  field in `Character`; measure). These set the door path grade and the check.
- Whether a pad written next to a road reads as natural with Valheim's vertex spacing and a
  smoothed falloff, or needs a wider ring.
- Support and walkability of tilted floor pieces spawned by code, before any inclined deck.

## Sources

- Minecraft template pools (projection, fallback): <https://minecraft.fandom.com/wiki/Custom_world_generation/template_pool>
- Terrain adaptation values: <https://kore.ayfri.com/docs/data-driven/worldgen/structures>,
  <https://www.mintlify.com/Apollounknowndev/lithostitched/guides/custom-structures>
- Emilien et al. 2012: <https://perso.liris.cnrs.fr/egalin/Articles/2012-villages.pdf>
- Galin et al. 2010: <https://perso.liris.cnrs.fr/egalin/Articles/2010-roads.pdf>
- Valheim: `decompiled/assembly_valheim/ZoneSystem.cs` (`m_maxTerrainDelta`, `m_slopeRotation`),
  `TerrainModifier.cs`.
