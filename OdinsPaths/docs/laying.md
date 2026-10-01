# Laying it

How a found trail becomes terrain (`src/Trail.cs`, `src/TerrainWriter.cs`, `src/Clearing.cs`,
`src/Landings.cs`): the paint, the rules that keep it off the player's work and out of the
water, the vegetation it clears, the harbours at its landings, and the levelling.

Per zone the path crosses, once, in one batch: take the path points within reach, and for every
vertex within the half width (1.75 m, config; the width drifts ±0.5 m over some 40 m of trail,
slow world noise offset by the seed, config `WidthVariation`) paint **dirt** with a soft edge. The
weight is `1 - distance/halfWidth` raised to a small power, lerping r, g, b toward `(1, 0, 0)`
exactly as `PaintCleared` does, and alpha stays the base mask's. Then write the blob. Rules:

- **Two kinds of road** (decided 2026-09-25, `network.md`; built the same day, `src/RoadKind.cs`,
  not yet seen in game). A main road
  is painted **paved**, toward `Heightmap.m_paintMaskPaved` `(0, 0, 1)` - the stone road
  texture a player's paving gives, which also clears the Deep North's snow channel -, about 4 m
  wide and levelled. A spur is **dirt** `(1, 0, 0)`, about 2.5 m, and levelled gently or not at
  all. The writer takes the paint, the width and the levelling per trail instead of from the
  settings alone (built: main 5 m ±1 m, edge 0.3 m, cut 1 m; spur 3.5 m ±0.75 m, edge 0.5 m,
  cut 0.4 m - `[MainRoads]` and `[Spurs]` in the config; the width drifts over some 30 m).
  Until 2026-09-25 main 4 m ±0.3 and spur 2.5 m ±0.5, edge 0.8: after the first lay the spurs
  were barely visible and the main roads too even - asked for were 4 to 6 m, open stretches and
  narrows. A config still at the old defaults takes the new ones (`RoadKind.UpgradeDefaults`). Both keep the soft edge and the width
  drift; stone gets a harder edge (less softness), since a paved road has a kerb rather than a
  worn fringe. **Verify** in game how
  paved paint looks in each biome, the Plains' yellow grass and the Mountain's snow first.
- A spur meets its main road without a seam by itself: the writer leaves paved texels alone (the
  rule below), so the dirt stops at the stone's edge.
- **Clearing in zones generated later reads the paint back** (`Clearing.ClearNewZone`): today it
  counts dirt (r > 0.3) and stone (b > 0.3) alike (built) - in a zone generated just now nothing
  is a player's, so all paving there is a road's.

- **Never over the player's work.** Leave vertices whose paint is already paved or cultivated
  (outside the Deep North), and vertices whose height the player changed
  (`modifiedHeight` already set).
- **Not in water:** no paint below the water level plus 0.3 m. The path visibly ends at the
  shore.
- **Not on a bank or cliff** (2026-09-25; stone was seen drawn onto cliffs in the first lay): the
  levelling is written first, then no texel is painted where the ground around it, levelled,
  is too steep: for a spur over 0.9 (42°), fading in from 0.7 (35°); for a main road, since the
  second lay (stone on the slopes of a half-levelled road looked awful), over 0.55 (29°), fading
  in from 0.4 (22°) - stone lies on the flat bench the levelling cuts. Where the cut could not
  tame the ground the road had a gap: "flattened, but on a 45° angle without road" (third lay).
  Since then a main road too steep for stone is painted dirt there instead, fading out from 0.9
  (42°) to 1.3 (53°) (`TerrainWriter.SteepDirtFade`). **Verify** how the dirt stretches look.
- **Fades between dirt and stone** (here and into the Mistlands) go dirt → both → stone
  (`Trail.Handover`), never a straight lerp: the game clears grass only where a channel is over
  0.5 (`Heightmap.IsCleared`), and a lerp is 0.5 of each midway (Procedural Roads #32,
  2026-10-01). **Verify** no grass in a fade.
- **The Mistlands** (2026-09-25; the first lay's roads there were useless, and paving the gentle
  stretches only, with a deeper cut, was still not it after the second): a road there is a dirt
  track that follows the ground - no stone, no levelling, no hairpin landings -, its kind's
  paint and levelling fading out over 8 m at the biome's edge (`Trail.Built`). Along all of it a
  road post stands every 48 m (24 m until the third lay: too many), alternating sides, 1 m past the paint's widest, facing the road,
  as `Mistlands_RoadPost1` has it: a `blackmarble_post01` (a `Destructible` that drops black
  marble and falls to the ground, `StaticPhysics`) with its foot on the lowest ground under it,
  and on top of it, 3.5 m up, a `dverger_demister` - a `WearNTear` with a
  `ParticleSystemForceField` that clears the mist (read from bundles `c4210710` and `fe66e03b`;
  the lamp alone stood in the ground). No creator, a structure like the barrels. Not where the
  ground under it is steeper than 0.8, in a location or within 6 m of a structure. The clearing
  still takes the cliffs and rocks off the track. **Verify** the post upright, the lamp on it.
- **Zone borders:** an edge vertex belongs to two zones' blobs. `PaintCleared` copies edge values
  to the neighbour (`spread`), so write both, or there is a seam.
- **Vegetation (built):** trees and logs, rocks and bushes whose drops are only wood, stone,
  flint or resin, and the scenery nobody can break - vegetation with a `ZNetView` and a solid
  collider but no `MineRock`, `MineRock5`, `Destructible`, `Piece` or `WearNTear`:
  `cliff_mistlands1`/`2` (8 and 50 a zone), `rock_mistlands1` (20 a zone, scaled up to 3), the
  giant roots -, from `ZoneSystem.m_vegetation`. That last group was the obstacles a road
  could only walk around where their zone existed, and ran into where it was generated after
  the lay (2026-09-25: noticeable in the Mountains, unusable in the Mistlands). A tree goes if
  its trunk is within 1 m of the path's edge; a rock or bush if it reaches onto the path at
  all - measured once per prefab as the boxes of its meshes seen from above (one per mesh, a
  level of detail inside another's box left out), in the prefab's unscaled space, then turned
  and scaled as its ZDO was placed, each box covered by a row of discs along its long side. A
  boulder the path runs through goes however big it is; a long cliff beside it stays. A boulder a
  player has hit stands as its broken copy (`Destructible.m_spawnWhenDestroyed`, e.g.
  `rock4_forest_frac`, scaled as the boulder), cleared like it (2026-10-01). Ore,
  nests, spawners and pickables stay, and so does anything within a location's measured
  footprint (`Footprints`, below) or within 10 m of a player-built piece (a grown sapling is the same prefab as a wild tree). No setting yet.
  **Verify** that a cleared cliff leaves no hole or floating piece.
- **Location footprints** (2026-09-25; a road ran through a Mistlands structure): a location's
  exterior radius is only the ground the game clears and levels for it, and the Mistlands'
  viaducts (8 m) and giant skeletons (10-11 m) stand much further out, so the search walked
  through them and the clearing took their pieces for scenery. Each location is measured once
  per session from its prefab's meshes seen from above (loaded for the moment; meshes more than
  60 m above or below the origin are a dungeon's interior and left out), kept to 48 m, never
  under the exterior radius. The search's circles, the writer's and the clearing's use it; a
  road's or a spur's goal still sits at the exterior radius. The log names each location that
  reaches past its radius, and a measuring that took over 20 ms. **Verify** the sizes and the
  time.
- **The sacrificial stones** (third lay, 2026-09-25: "your adjustments prove to be bumpy"): no
  levelling within 10 m of the start temple's centre, fading in over the 1.5 m shoulder past
  that (`TerrainWriter.TempleKeep`) - the temple levels its own ground. The paint still runs
  up to the stones.
- **Landings (built):** where the trail enters a stretch of water that has sea in it - deeper
  than a ford (1.5 m) in the Ocean biome, or deeper than any river (6.5 m) in any biome: a fjord,
  a strait, a deep lake (`docs/search.md`) - and is at least 40 m long, or any water at least
  100 m long - a river is swum, and markers 10 m apart on a brook looked silly -, a main road
  gets a **harbour stone** on the last dry point and another on the first dry point across
  (`Harbours`, since 2026-09-26; a `piece_chest_barrel` before). The stone is a vegvisir with
  blue runes (its shader, `Custom/StaticRock`, reads `_EmissionColor`; the material's
  `_EmissiveColor` is a stale value no property reads, seen in game 2026-09-26), its face to the road; a structure within 4 m of the shore point moves it up to
  10 m inland along the trail. A landing within 25 m of a stone already there is that stone's
  harbour: roads setting out from one shore for different islands share it. Each stone's ZDO
  keeps the positions of the harbours across (`OdinsPaths_HarbourLinks`), both ways, and using
  it pins every one that still stands (pin `Harbour`, the portal icon), and the stone itself if
  the player has no pin there yet.
  How: `Vegvisir.Interact` asks the server for the closest `OdinsPaths_Harbour`, which no
  location is called; the prefix on `Game.RPC_DiscoverClosestLocation` finds the stone at the
  point asked from (within 3 m) and sends `RPC_DiscoverLocationResponse` for the stone itself
  (first, without showMap: `Minimap.DiscoverLocation` then skips an existing pin silently) and
  one per link, and the client
  pins what it is sent and looks toward it (so toward the last one). A vanilla vegvisir has no
  `ZNetView` (read from the bundles 2026-09-25: a Transform, an LODGroup, `Vegvisir` and one
  field-less script; the runes are a white mask, `runetablet_vegvisir_runes`, lit red by the
  material's `_EmissiveColor`/`_EmissionColor` (1, 0, 0) and a child point light (1, 0.37, 0.37)),
  so it exists only inside the locations each client builds. The stone is therefore the mod's
  own prefab: the start temple's vegvisir copied under an inactive holder, with a `ZNetView`
  (persistent), those colours on copied materials, and one `Vegvisir` entry asking for the
  harbour, added to `ZNetScene` after `ZoneSystem.Start` on the server and on every client with
  the mod. A client without it sees no stone; `ZNetScene` there logs "Missing prefab hash" but
  keeps the ZDO - only the server deletes a ZDO of an unknown prefab.
  A stone counts as a structure, so later paths walk around it. Its height is the built ground
  (`Ground.Height`) plus the levelling delta. A spur's landing (spurs cross water since the third
  lay, `network.md`) is a minor harbour: a `wood_pole_log_4`, no stone, and none where a
  structure already stands within 4 m. Everything the mod places carries `OdinsPaths_Placed` on
  its ZDO, for the dev reset. **Verify** that the pole stands (it is a building piece with no
  support under it but the ground).
- Throttle the writes: zones while the frame's budget lasts, at least one. Each is a
  decompress-merge-compress of at most a few KB. Only the locations overlapping the zone are
  checked per vertex - all of a road's, up to 12 000, made the writer the start-up stutter
  (2026-10-01, the log's slowest frames grew with the location count). At most a few zones are
  asked of the game's heightmap builder at once: it is one thread, first come first served, and
  the zones a walking player needs would wait behind a whole road's.
- A zone with two terrain compilers is left alone and logged: the game destroys one when the
  zone loads, and which one is not ours to guess.

**Levelling** (config, default on and gentle):

- Take the height profile along the path from the built ground (`Ground.Height`, the same
  heights the writer levels against), smoothed over about 10 m.
- **Steep stretches of a main road** (2026-09-25, second lay: "sometimes very steep parts"):
  where the smoothed profile still climbs more than 22% (`RoadKind.MaxGrade`), every too-steep
  step gives on both ends - the upper point cut down, the lower one filled up - no further than
  4 m from the ground (`RoadKind.SteepCut`), pass after pass both ways until nothing moves (at
  most 300), then the kinks rounded over a point either way (`Trail.LimitGrade`). A slope longer
  than the cut can tame is eased at both ends and followed in its middle: simulated, a 60 m
  climb at 50% became a 22% ramp into it and 37% where the 4 m ran out. Water, causeways and
  the Mistlands stay as they are. Spurs keep their profile.
- Every vertex out to the flat width gets `levelDelta = profile - base`, blended into 0 over a
  shoulder of 1.5 m. For a main road the flat width is the paint's widest (half the width plus
  its 15% wobble), and the shoulder drops off at once and eases into the ground, `(1 - t)²`: a
  flat bench with a sharp edge, the paint on the bench and none on the bank (second lay:
  "sharper edges"). A spur is flat to half its width and rounds off at both ends (SmoothStep).
- Clamp the cut or fill to 1 m (config, `MaxCut`). That stays far inside the game's ±8 m. On a
  main road, as much deeper as the point needs, up to 4 m: its distance from the profile plus
  how far the ground falls or rises across the flat width, the deepest of two points either way
  (`Trail.FindCuts`) - a 1 m clamp left steep stretches tilted across and lumpy. Up to 2.5
  times the kind's at a hairpin's landing; none in the Mistlands.
- **Hairpins** (2026-09-25): where another leg of the same trail (more than 20 m along it) is
  within reach of a vertex too, its height outside both legs' flat parts is blended between the
  two by how far past each flat it is. The nearest leg alone left a ridge or a step between the
  legs of a switchback; blending on the flat itself tilted the road toward the other leg.
- **Narrow legs** (2026-09-25, second lay: one leg's levelling ate into the other's): where
  another leg comes closer than both legs' flats and shoulders need, each gets half the room -
  its half width at most half the distance less a shoulder, no less than 30% of its width -,
  tapered over 8 m before and after (`Trail.LegRoom`). The paint narrows with it.
- **Hairpin landings** (2026-09-25): where the trail turns more than 60° over 8 m either way
  and the ground there is steeper than 0.25 (14°), the profile is held at its mean for 4 m either
  side of the sharpest point and blended back into the legs over 6 m more: the turn itself is
  flat, the climbing stays in the legs. `paths lay` reports how many.

The result is a path you can walk without hopping, a level bench where it cuts across a slope.
The cost is the same blob as the paint. The risk is looks: on steep ground a 4 m cut over a
1.5 m shoulder makes a wall on the uphill side and an embankment below. That is the in-game test.

**Later, wear:** spurs used often (count players on path zones, as AntTrails does) could widen,
or turn to stone.
