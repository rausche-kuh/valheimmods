# Harbours: docks and buildings

Every new harbour of a main road gets a dock carrying the road out to sea, its harbour stone on
the dock or beside the road, and a few old buildings beside the road (reworked 2026-09-26, not run
in game yet). Docks and buildings are **blueprints** in PlanBuild's `.blueprint` format with a
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
| `assets/harbours/*.json` | The shipped blueprints, generated once from the old code styles, still in the old format (below). |

## Where things go

- **Dock height.** The deck's top is the road's height 1.5 m inland of the shore point (the
  road as the terrain writer levels it, `Landings.Height`), kept 0.8-3 m above the water. The
  shore point is often the foot of the bank the road comes down, so the deck is not set there.
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
- **Buildings.** Up to `Buildings`, a different blueprint each while there are, tried every 3 m
  along the road from 4 to 40 m inland of the land end, on both sides: front just past the
  road's levelled edge (`RoadKind.Reach` + 0.5 m), facing the road. Every metre of the box around
  its floors, walls and piles must be 1 m above the water, rise no more than 2.5 m, be 1.5 m from
  structures, off this road and outside locations. The floor goes at the highest ground; the
  piles reach down to the rest. Trees and rocks within 1.5 m of its pieces are cleared, now in
  generated zones and later when a zone is generated (`Clearing.ClearNewZone`, by the pieces'
  `OdinsPaths_Building` mark). Another road running past is not checked.

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
is no role (a PlanBuild capture writes the hammer's tab there) is guessed. A `sign` reading
`chest`, `enemy`, `deco` or `stone` is that **spot**, at the sign's foot, facing its yaw.
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

**The old format**, what the shipped files still are: the settings JSON alone, with the pieces
in it (`"pieces": [{"prefab", "pos": [x, y, z], "rot": [yaw] | [x, y, z] Euler | [x, y, z, w],
"role", "anchor": "pivot" | "top" | "bottom"}]`, `anchor` the point of the prefab's measured
collider box at `pos`) and `"spots": [{"kind", "pos", "yaw"}]`. It still loads; `docks export`
converts it.

- **Frame.** A dock: origin the middle of its land end (where the road runs on), z out to sea,
  x right looking out, y up from the deck's top there. A building: origin the middle of its
  front (the side facing the road), z into it, x right looking in, y up from its floor's top.
- `kind` `dock` or `building`; `biomes` `Heightmap.Biome` names (`Meadows`, `BlackForest`,
  `Swamp`, `Mountain`, `Plains`, `Mistlands`, `AshLands`, `DeepNorth`), none for any;
  `preSnow` starts every piece snowed over; `roofReach` (m, 0 = 3) how far a roof piece may be
  from a standing wall or post; `deco` the furniture `deco` spots pick from (a piece at most
  2.3 m wide, a rug 3.2 m); `clutter` loose pieces laid on deck pieces at `clutterChance` each.
- **Roles:**

| Role | Weathering | Else |
| --- | --- | --- |
| `deck` | decay × 35%, never within 1.2 m of a post, lamp, furniture or spot, never a dock's first 2 m | left out where the ground is over it |
| `floor` | never | as deck |
| `pile` | never | the lowest in each column (within 0.3 m across) is stacked on down to the ground, up to 6 more; one wholly underground is left out |
| `wall` | decay × 50% | |
| `roof` | decay × 60%, and whenever no wall or post stands within `roofReach` | |
| `post` | decay × 50% | |
| `lamp` | with the post under it (within 0.6 m) | |
| `deco` | decay × 40% | a relic: drops nothing, refuses the hammer |
| `clutter`, `keep` | never | |

- **Spots:** `chest` (the biome's treasure chest, at `ChestChance`; else on a `deco` spot, else a
  free deck), `enemy` (one to three of the biome's one-shot spawners, at `EnemyChance`), `deco`
  (furniture from `deco`, at 1 - decay × 40%), `stone` (a dock's harbour stone). A spot whose
  deck is gone and has no ground under it is dropped.
- Condition 0.05-0.95 per dock or building; every piece's health is condition ± 0.3 of its
  maximum, which the game shows as new, worn or broken. The chests, spawners and relics:
  `Builder.Chest`, `Builder.Enemies`, `Relics`.

## Making and reworking blueprints in game

Debug build, devcommands on, a creative/debug-mode player with the hammer:

1. `docks build <name> edit` (on a shore, standing where the road would end, looking out) or
   `docks house <name> edit` (at the building's front, looking in): the blueprint as new, of the
   game's pieces placed as yours, so the hammer takes them down, a **sign** at each spot reading
   its kind. A new one: build it yourself, standing at its origin when you start.
2. Rework it with the hammer. A spot is a sign reading `chest`, `enemy`, `deco` or `stone`.
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
  shipped wood docks put piles on the outer edges every 4 m (a floor is at most one tile from
  one); grausten keeps a block under every tile until its floor's material is checked.
- Every `Spawner_*` used has a respawn time of 0 (read from the bundles): it raises its creature
  once, when a player comes within 60 m, and never again.
- The Mistlands' `DvergrHarbourPier` is the pier of `Mistlands_Harbour1`, read from its bundle
  (2026-09-26). `dvergrprops_*` are no build pieces (no `Piece`) but wear and drop wood and copper.
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
- Every piece at its height: the `anchor`s of the shipped files were not measured in game
  (piles 0.1 m into the deck, walls on the floor, roofs at 2 m, lamps on their posts).
- Nothing collapses when a player comes near; a weathered roof or a post over a gone deck.
- The stone on the dock (its size on a 4 m walkway) and beside the road (its height).
- Buildings: their spots beside the road, the floor at the highest ground, piles on the slope,
  trees cleared, nothing on another road.
- The edit → capture round trip: same places, roles kept, pile stacks left out, signs as spots.
- The PlanBuild round trip: `docks export` of the old files (the anchors resolved to the same
  places), PlanBuild placing them, its capture fitted back by `docks import` (turn, shift, roles).
- Whether the `sign` can be placed on a floor with the hammer.

## To make

- Better shipped blueprints, built in game: pitched roofs, doors, railings, a Mistlands building.
- Swamp (darkwood on log piles?), Plains and Mountain docks of their own.
- Spurs' minor harbours still get only a post.
