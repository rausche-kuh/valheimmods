# Harbours: docks and buildings

Every new harbour of a main road gets a dock carrying the road out to sea, its harbour stone on
the dock or beside the road, and a few old buildings: off the road with a path to each door, or
built onto the dock (reworked 2026-09-26 and 2026-10-01, not run in game yet). Docks and buildings are **blueprints** in PlanBuild's `.blueprint` format with a
JSON file of settings beside each, loaded at start, made and reworked in game with `docks
capture` or with PlanBuild. Settings: `[Harbours] Docks` (on), `Buildings` (2),
`ChestChance` (0.35), `EnemyChance` (0.2).

| File | What |
| --- | --- |
| `src/Blueprints.cs` | The format (settings JSON + PlanBuild `.blueprint`), loading from both folders, saving. |
| `src/Builder.cs` | Raising a blueprint in a frame: weathering, piles to the ground, spots, relics, chests, spawners. |
| `src/Docks.cs` | Where a harbour's dock goes and at what height; the fit. |
| `src/Buildings.cs` | Where the buildings go beside the road. |
| `src/Relics.cs` | Furniture that drops nothing (below). |
| `src/Dev/DockCommands.cs` | `docks list / reload / build / house / undo / capture / export / import`. |
| `src/Dev/PlanBuildFiles.cs` | `docks export` into PlanBuild's folder, `docks import` of a PlanBuild capture: the fit. |
| `assets/harbours/` | The shipped blueprints, the bare minimum to be replaced: `WoodJetty` (a dock, still in the old format below) and the buildings `WoodHut`, `WoodTower` and `RuinWoodHouse3`, `11`, `12` (the game's ruined Meadows houses of those numbers, converted from the bundles and reworked). A biome with none of its own builds as the Meadows do. |

## Where things go

- **Quay and dock height.** The harbour has one height, chosen before the road is laid: the
  trail holds its last points before the shore at the **quay height** (`Trail.PinQuays`: the
  dock's deck height for the road just inland, kept 0.8-3 m above the water), and the grade
  limit brings the road down or up to it, as the game's own harbours level the ground at their
  pier to its deck. The deck's top is the quay as levelled (`Landings.Height`). No quay where the
  levelling cannot reach it, or at a harbour of the game's: then the road's height 1.5 m inland
  of the shore point, in the same bounds (the shore point is often the foot of the bank).
- **Land end.** Walking from the shore point inland along the road (up to 8 m), the first point
  where the road is as high as the deck: the dock's origin, on the road's middle, so the road
  runs onto the planks flush. Its heading: from 4 m inland of that to a few trail points out
  over the water. A dock's first 2 m never weather and never count as buried.
- **Fit.** A dock blueprint is ruled out by water deeper than 12 m under any deck or floor,
  anything built within 1.5 m of a piece, a third or more of its decks under the ground, or never
  reaching water. Blueprints of the land's biome are tried in a random order weighted by
  `weight`; none for the biome: the Meadows'.
- **Stone.** On the dock's `stone` spot, facing as the spot says; a dock without one (or no dock):
  on the ground 1.2 m past the road's edge, 2 m inland of the land end, moved up the road off
  anything within 2 m, its height the lower of the ground and the road, sunk 0.2 m.
- **Buildings.** Up to `Buildings`, a different blueprint each while there are, each blueprint
  tried once before any is repeated. Two kinds (`src/Buildings.cs` has the distances):
  - **Beside the road** (no `dock` spot): placed by the **lowest door** (`door` role) - its foot
    is the building's snap point, and the way in is across the door toward the middle of the
    floors. Every place along the road inland of the land end, on both sides, the door facing
    the road a few metres past its levelled edge, is weighed (`Buildings.Site`). The **door's
    height comes from the road's**: no further above or below it than a path climbs at
    `DoorGrade` from the road's flat edge to the door. For each door height in that range the
    ground under the box (its floors, walls, doors and piles, plus an apron) is compared with
    the floor: a **pad** (`src/Pad.cs`) cuts it down where it is higher (at most `PadCut`),
    fills it where it is lower (at most `PadFill`), and the piles, if the blueprint has any,
    carry the floor down `MaxPiles` further; past that the height does not fit. The cheapest
    height and place win: the ground moved (a cut costs most, piles least), the door off the
    road's height, the setback and the distance up the road, plus a little at random. The box
    must be dry and clear of structures, this road, locations and the other door paths; so must
    the way from the road to the door. A **door path** (a short dirt `Trail` from the road's
    middle to just short of the door) is written after the road, held at the road's height
    across it and **ramped to the door's** (`Trail.Ramp`), levelling the road's own shoulder
    again on the way (`Trail.OverOwn`), with the building's own pieces around the door left out
    of the structures so it reaches the threshold; then the pad, around what the path levelled.
    The builder reads the ground through the pad, so a floor the pad digs out stands. A building
    without a door is placed by its front's middle (a warning in the log).
  - **Onto the dock**: a building with a `dock` spot by that spot, one beside the road that
    found no place there by its door's foot (the way out of the door onto the deck): on the outer
    edge of the dock's deck, level with it, on either side past the land end; the building off
    that side. None of it on the deck, its floors neither buried nor over water deeper than a
    dock may stand in, clear of structures (the dock's own aside), door paths, the road and
    locations. No door path, no pad. Not at a harbour of the game's (no dock of the mod's).
  - **The order:** beside the road, then onto the dock; the dock first where the land beside the
    road just inland is steep (`Buildings.Steep`: its ground a few metres off the road never
    within `SteepRise` of the road's height). A blueprint that fits nowhere gives way to its
    `fallback`, and that one's, a few deep (Minecraft's fallback pools).
  - The log line per harbour names each building, where it went and how far its door is above
    or below the road.
  - Trees and rocks around its pieces are cleared, now in generated zones and later when a zone
    is generated (`Clearing.ClearNewZone`, by the pieces' `OdinsPaths_Building` mark). Another
    road running past is not checked.

## The game's harbours first

`src/Ports.cs` (built 2026-09-27, not run in game yet). A main road that crosses the sea near a
`Mistlands_Harbour1` uses its dvergr pier instead of a dock of the mod's:

- **Reading it.** Only a harbour whose zone is generated can be used: the location is turned by
  the slope under it, sampled at random as its zone is generated, so its pier's direction is
  unknown before. Before its search, a road has the zones of the harbours on its way generated
  as the game generates the zones around a player (`ZoneSystem.SpawnZone` as a ghost), the
  nearest to its goals first and only a few per road, and reads the turn from the harbour's
  crane (else its guardstone), checked against where that piece belongs. Read harbours are kept
  for the session; the log lists only the ones read for the road at hand.
- **The search.** A step boarding or landing near a berth (or near a stone of the mod's own) pays a share of the lump sum; the
  harbour's circle is no obstacle to the search (its pieces are structures), but it still keeps
  the levelling out. A pier's land end within reach of a Mistlands goal is an entry into them
  (`Entries`).
- **The route.** A route that crosses the harbour's footprint with the sea on one side and land
  on the other is spliced (`Ports.Splice`): water off the berth, the berth, the land end, and
  round the hut on the side the route goes on to.
- **The harbour.** The stone stands beside the pier's land end, on the side the road does not
  take, facing the pier; no dock; buildings as for any harbour, which keeps them outside the
  harbour's footprint.
- `paths ports [radius]` generates and reads the harbours around the player and pins each berth
  and land end.

## Blueprint format

Two files per blueprint: `<name>.json`, its settings, and `<name>.blueprint`, its pieces in
[PlanBuild](https://github.com/sirskunkalot/PlanBuild)'s format, so
PlanBuild's rune places it and its capture reads it back (`docks import`, below). Shipped ones
are in the mod's `harbours/` folder; the player's own in `BepInEx/config/OdinsPaths/harbours/`,
where a blueprint of the same `name` replaces the shipped one. A `.blueprint` without its
`.json` is not loaded. Clients load the files too, for the relics: furniture in a server-only
file is invisible to clients.

The settings, read by the mod's own `src/Json.cs` (Unity's `JsonUtility` read the names but left
every list empty in game, 2026-09-26): plain JSON, **no comments, no trailing commas**; a field
left out is 0, false or empty; a file that does not parse is left out with its line in the log.

```json
{
  "name": "WoodJetty",
  "kind": "dock",
  "biomes": ["Meadows", "BlackForest"],
  "weight": 1,
  "preSnow": false,
  "roofReach": 3,
  "deco": ["piece_table_round", "rug_wolf"],
  "clutter": [],
  "clutterChance": 0
}
```

The pieces (`Blueprints.ReadPlan` / `WritePlan`), as PlanBuild writes them: header lines, then
`#Pieces` and a line per piece, `prefab;category;x;y;z;qx;qy;qz;qw;info;sx;sy;sz` - position
of the prefab's pivot, rotation as a quaternion, `info` a JSON string (a sign's text), scale
ignored. PlanBuild never reads the category back, so it holds the piece's **role**; one that
is no role (a PlanBuild capture writes the hammer's tab there) is guessed. A `sign` reading a
spot's kind (below) is that **spot**, at the sign's foot, facing its yaw.
`#SnapPoints`, `#Terrain` and unknown sections are skipped, as PlanBuild does.

```
#Name:WoodJetty
#Creator:OdinsPaths
#Description:"OdinsPaths harbour dock: ..."
#Category:OdinsPaths
#Pieces
wood_floor;deck;-1;-0.25;1;0;0;0;1;"";1;1;1
woodwall;wall;2;0;5;0;0.707107;0;0.707107;"";1;1;1
sign;spot;1.2;0.9;1;0;1;0;0;"stone";1;1;1
```

**The old format**, what the shipped `WoodJetty` still is: the settings JSON alone, with the pieces
in it (`"pieces": [{"prefab", "pos": [x, y, z], "rot": [yaw] | [x, y, z] Euler | [x, y, z, w],
"role", "anchor": "pivot" | "top" | "bottom"}]`, `anchor` the point of the prefab's measured
collider box at `pos`) and `"spots": [{"kind", "pos", "yaw"}]`. It still loads; `docks export`
converts it.

- **Frame.** A dock: origin the middle of its land end (where the road runs on), z out to sea,
  x right looking out, y up from the deck's top there. A building: any frame, y up; it is placed
  by its lowest door or its `dock` spot (above), not by its origin, and its pad levelled under
  the floor that door opens onto (`Builder.DoorFloor`), or the door's foot where it has none.
  An old-format building without a door: origin the middle of its front, z into it.
- `kind` `dock` or `building`; `biomes` `Heightmap.Biome` names (`Meadows`, `BlackForest`,
  `Swamp`, `Mountain`, `Plains`, `Mistlands`, `AshLands`, `DeepNorth`), none for any;
  `preSnow` starts every piece snowed over; `fallback` (a building's) the building of that name to
  try where this one fits nowhere; `roofReach` (m, 0 = 3) how far a roof piece may be
  from a standing wall or post; `deco` the furniture the deco spots pick from, each spot what
  fits its kind (below; when the list has nothing for a kind, `Builder.DefaultDeco`'s);
  `clutter` loose pieces laid on deck pieces at `clutterChance` each.
- **Roles:**

| Role | Weathering | Else |
| --- | --- | --- |
| `deck` | decay × 35%, never within 1.2 m of a post, lamp, furniture or spot, never a dock's first 2 m | left out where the ground is over it |
| `floor` | never | as deck |
| `pile` | never | the lowest in each column (within 0.3 m across) is stacked on down to the ground, up to 6 more; one wholly underground is left out |
| `wall` | decay × 50% | |
| `door` | decay × 50% | the lowest is where a building on its own is entered; a captured piece with the game's `Door` is guessed one |
| `roof` | decay × 60%, and whenever no wall or post stands within `roofReach` | |
| `post` | decay × 50% | in a building, one standing at its bottom (no higher than its lowest floor, deck or pile) is a stilt: stacked on down as a pile. A building with no pile and no such post is never placed off the ground beside a dock |
| `lamp` | with the post under it (within 0.6 m) | |
| `deco` | decay × 40% | furniture built as placed, never swapped; a relic: drops nothing, refuses the hammer. Captured furniture (the hammer's Furniture tab) is guessed one |
| `clutter`, `keep` | never | |

- **Spots** - the signs are the only thing ever replaced:
  - `chest`: the biome's treasure chest, at `ChestChance` - a building's only, never a dock's
    (the user, 2026-10-02); else on a standing deco spot, else a
    free deck.
  - `barrel`: shares the `chest` roll; the loot goes to one chest or barrel spot at random, and
    on a barrel spot it is a barrel with the chest's loot written into its ZDO (`Builder.Fill`).
  - `enemy`: one to three of the biome's one-shot spawners, at `EnemyChance`.
  - Furniture from `deco`, at 1 - decay × 40%, turned as its sign. `deco_h1`: standing, no
    higher than a wall; `deco_h2`: standing, no higher than two; both on the sign's foot.
    `deco_wall`: what the hammer keeps off floors (`Piece.m_notOnFloor`: banners); `deco_hanging`:
    what it fixes to ceilings only (`m_inCeilingOnly`: the hanging brazier); both by their pivot
    at the sign's, as the hammer fixes a sign and a banner alike. The old `deco` reads as `deco_h1`.
  - `stone`: a dock's harbour stone. `dock`: where a building joins a dock (above); nothing goes there.
  - A standing spot whose deck is gone and has no ground under it is dropped; a wall or hanging
    one whose wall or ceiling is gone, too.
- Condition 0.05-0.95 per dock or building; every piece's health is condition ± 0.3 of its
  maximum, which the game shows as new, worn or broken. The chests, spawners and relics:
  `Builder.Chest`, `Builder.Enemies`, `Relics`.

## Making and reworking blueprints in game

Debug build, devcommands on, a creative/debug-mode player with the hammer:

1. `docks build <name> edit` (on a shore, standing where the road would end, looking out) or
   `docks house <name> edit` (at its lowest door, looking in): the blueprint as new, of the
   game's pieces placed as yours, so the hammer takes them down, a **sign** at each spot reading
   its kind. A new one: build it yourself, standing at its origin when you start.
2. Rework it with the hammer. A spot is a sign reading `chest`, `barrel`, `enemy`, `deco_h1`, `deco_h2`,
   `deco_wall`, `deco_hanging`, `stone` or `dock`. Give a building one door at the ground (the
   lowest counts), and a `dock` sign only to one that is built onto a dock.
   Piles only need to reach a little below the deck; the rest is added where it is raised.
3. `docks capture <name> [dock|building] [radius]` (16 m): everything built around you, in the
   frame of the last edit build within 40 m (else your feet and view), written to the player's
   folder and in use at once. Roles come back as built; new pieces get a guess (an upright piece
   below the deck is a pile, then by name) - check them. An existing blueprint's settings are
   kept; a new one gets your biome.
4. Copy both files into `assets/harbours/` to ship it. `docks reload` reads the files again.

**With PlanBuild** (Debug build as well; PlanBuild's code is in `~/Documents/Code/test/othervalheimmods/PlanBuild/`):

1. `docks export [name]`: every blueprint (or one) copied into PlanBuild's save folder (its
   `Save directory` setting, relative to the game's folder; `bp.local` lists them), one still in
   the old format first converted into the player's folder - this needs the game's prefabs for
   the anchors, so it runs in a world. `docks capture` and `docks import` copy there too.
2. Place it with PlanBuild's rune, anywhere, turned any way; rework it with the hammer. Spots
   are the signs; a new sign reading `chest` etc. is a new spot.
3. Capture it with PlanBuild (any name; a centre marker is not needed).
4. `docks import <file> [as <name>]`: the file by path, or by name in PlanBuild's folders,
   also after PlanBuild's `<player>_` prefix, the newest. It is fitted onto the blueprint of
   its name: the turn about the vertical and the shift that lay the most old pieces (same
   prefab, same rotation within 2°, within 0.2 m) over captured ones; at least 30% of them must
   match. Each matched piece keeps its role, the rest are guessed - check them. The settings
   are kept. `as <new name> dock|building` makes a new blueprint in the capture's own frame
   (PlanBuild's centre marker, or its lowest corner, as the origin; z north).

`docks build [name] [condition] [chest] [enemies]` without `edit` raises it as a harbour would
(weathered, the deck at your feet's height within its bounds); `docks undo`; `docks list`.

## Relics: valuable furniture that drops nothing

A piece nobody placed still drops a third of its resources, at least one of each, when it breaks
(`Piece.DropResources`), and the hammer removes any piece with `m_canBeRemoved` outside a ward
(`Player.RemovePiece`), creator or not: a dragon bed on a Meadows dock would be iron nails. So
the furniture is a copy (`OdinsPaths_Relic_<name>`), registered after `ZoneSystem.Start`, with
every requirement's `m_recover` off and `m_canBeRemoved` off. Structural pieces stay vanilla and
drop their third as any ruin does, each only where it is found anyway.

## Game facts

- Support (`WearNTear.GetMaterialProperties`): Wood 100 max, 20% lost sideways, 12.5% up;
  Timberwood (Frostwood) 200, 20%; Stone 1000, all lost sideways; Ashstone 2000, a third. The
  shipped wood dock puts piles on the outer edges every 4 m (a floor is at most one tile from
  one).
- Every `Spawner_*` used has a respawn time of 0 (read from the bundles): it raises its creature
  once, when a player comes within 60 m, and never again.
- `dvergrprops_*` are no build pieces (no `Piece`) but wear and drop wood and copper.
- **`Mistlands_Harbour1`** (bundle `c920e237` and the location list, 2026-09-27): slope rotation
  (`ZoneSystem.PlaceLocations`: the location's z points down the slope, out to sea, rounded to
  22.5°, from random samples), snapped to the water, its origin at the water's edge. The pier
  runs inland from the origin, 6 m wide, of `dvergrprops_wood_beam` planks on
  `dvergrprops_wood_pole` piles, with three `dverger_demister`, the `dvergrtown_wood_crane` and a
  `dverger_guardstone` at its sea end and a `Spawner_DvergerArbalest` on it. The walled hut
  (`Spawner_DvergerMage`, gate facing inland) stands right behind the land end. Its terrain
  modifiers raise the ground at the land end to the deck's height (1.5 m above the water) and
  dredge the berth. Every piece with a `ZNetView` is spawned as its own ZDO at the location's
  position and turn (`SpawnLocation`), so the server can read them once the zone is generated.
- **Deep North** (bundles, 2026-09-26): Frostwood builds the *stave* set (`stave_pole_2m/_4m`,
  `stave_beam_2m/_4m`, `stave_wall_*`, material Timberwood) and the *scale* shingles
  (`scale_wall_2x2`, `scale_wall_roof_26/45/67`); no floor, so docks lay beams. Ice:
  `piece_icecube`, `crystal_wall_1x1`; scenery `IceWall`, `Ice_floor*`, `IceShelf_01-10`.
  `ZDOVars.s_preSnow` starts a piece snowed over; heavy snow damages pieces under 0.25 support.
  Furniture: `piece_table_runed(_small)`, `piece_chair_runed`, `piece_bench_runed`,
  `piece_moose_throne`, `rug_moose`, `piece_hoodedlantern`.

## To check in game

- The deck flush with the road at the land end, on a steep bank and on a flat beach; the log
  line gives the deck's height above the water and how far inland the dock starts.
- Every piece at its height: the `anchor`s of the shipped `WoodJetty` were not measured in game
  (piles 0.1 m into the deck, walls on the floor, roofs at 2 m, lamps on their posts).
- Nothing collapses when a player comes near; a weathered roof or a post over a gone deck.
- The stone on the dock (its size on a 4 m walkway) and beside the road (its height).
- Buildings: their spots beside the road, piles on the slope, trees cleared, nothing on another
  road.
- The quay and the pads (2026-10-01): the road's last metres level at the deck's height and
  the dock flush with them (the log says "on the road's quay"); a hut's door within a step of
  its path, the path ramped across the road's shoulder without a ledge, the pad cut and filled
  under the floor with no ledge at its margin, nothing of the floor left out where the pad dug;
  a hut with a door built onto the dock on a steep shore, its door on the deck's edge.
- Doors (2026-10-01): the way in found right (the door faces the road, not the building's back),
  the door path painted and levelled up to the threshold, not into the floor; a building joined
  to a dock flush with the deck, on the right side of it, its piles reaching the seabed.
- Deco spots: a banner on a `deco_wall` sign's wall and a brazier under a `deco_hanging` sign's
  ceiling (pivot to pivot, the same turn - the sign's and the banner's fronts may face apart),
  standing furniture turned as its sign.
- The edit → capture round trip: same places, roles kept, pile stacks left out, signs as spots.
- The PlanBuild round trip: `docks export` of the old files (the anchors resolved to the same
  places), PlanBuild placing them, its capture fitted back by `docks import` (turn, shift, roles).
- Whether the `sign` can be placed on a floor with the hammer.

## To make

- Blueprints built in game to replace the shipped two, and docks and buildings for every
  biome: pitched roofs, doors, railings, a `dock` sign on a building.
- Spurs' minor harbours still get only a post.
- **Wrecks** beside a harbour: a `wreck` blueprint kind from the game's wreck parts
  (`shipwreck_karve_*`, `shipwreck_vikingship_*`; **verify** they spawn by name through
  `ZNetScene`), laid in the shallows.
- **Spurs to the game's wrecks**, only if cheap to search: `ShipWreck01`-`04` (Swamp, Black
  Forest, Plains, Ocean shores), `ShipWreck01_DN` / `02_DN` (Deep North shore), `FrozenShip01`-`03`
  (Deep North, in the ice). Most lie in water or ice, so a spur ends at the nearest shore, and
  wrecks close together share one spur.
