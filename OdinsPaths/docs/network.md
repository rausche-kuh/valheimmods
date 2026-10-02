# The network

What the network connects, in which order, how the two kinds of road differ, where a base is,
and where the mod's own data lives. Decided 2026-09-24 (POI network) and 2026-09-25 (two tiers,
stone and dirt); built 2026-09-25 (`src/Network.cs`, `src/Planner.cs`, `src/Grower.cs`), not yet
run in game. Facts marked **verify** still need checking in game.

## Two tiers

**Main roads, paved.** They connect the hubs to the places a player has to reach:

- **Hubs:** the sacrificial stones (`StartTemple`) and every base (below).
- **Main locations:** the boss altars in a **progression list** (config; default every boss,
  Eikthyr to the Frozen King, three infected mines before the Queen, two charred fortresses
  before Fader, a Deep North village and the winding tunnels before the Frozen King), the traders (Haldor
  `Vendor_BlackForest`, Hildir `Hildir_camp`, the Bog Witch `BogWitch_Camp`), and a config list
  of **custom main locations** for a server that wants more (another mod's locations included -
  they are read from `ZoneSystem.m_locationInstances` by prefab name, so Expand World Data and
  friends work).

Each main location is a group of instances (three Eikthyr altars, four Elder altars...). The
next group in the list gets **one** road: a search from the whole network as it stands to every
instance of the group, stopping at the cheapest (`search.md`, built). That instance is pinned
for the world, and the vegvisirs are overridden to reveal it (`../ROADMAP.md`, port markers).
Groups are connected greedily, in list order - "the nearest altar from where the roads already
go", which is what a player expects, rather than a global optimum.

**Central instead of cheapest** (2026-09-25, `[Network] Central`, default Fader and the Frozen
King). In the first full preview both late roads took the altar nearest a coast, reached mostly by
boat (Fader 5 km of 8.6 over water, the Frozen King 3.6 of 4.1), and left a third to half of the
disc without a road into those biomes. A group in the list is searched to its **medoid** alone -
the instance whose distances to the others add up least, of altars spread along a biome's arc the
one in its middle - so the road has to cross the biome. The others are grey pins in the preview.
**Verify** in a preview that the medoid lies deep enough in; if not, the next idea is the instance
farthest from the biome's edge, or from the world's centre along its angle.

**Play style** (2026-09-25, `[Network] Style`): `Explore` (default) sends Fader's and the Frozen
King's roads to the medoid and Hildir's and the Bog Witch's to the camp farthest from every road
and base, planned after every other road due; `Fastest` takes the instance easiest to reach
everywhere; `Custom` reads the `Central` and `Remote` lists. Asked for after the same preview: the
traders' roads went to the camps nearest the network and left parts of the world empty. Since
the traders' roads go to camps not generated yet (below), `Remote` chooses among all of them in a
running game too.

**More than one of a group** (2026-09-25). A progression entry joins location groups with `|`
(any of them will do) and asks for several roads with `*n`, each to another instance: the Queen
needs the sealbreaker, whose fragments are in the **infected mines** (`Mistlands_DvergrTownEntrance1`
and `2`, 120 each), so the default has `...TownEntrance1|...TownEntrance2*3:defeated_queen` before
her. Since the second lay (2026-09-25, "the Deep North needs at least one, likely two of its
villages or winding tunnels in the network; the same for the Ashlands' fortresses") the default
also has `CharredFortress*2:defeated_fader` before Fader and `NorthVillage:defeated_frozenking,
MorkBorg:defeated_frozenking` before the Frozen King - prefab names of the bundles' `Location`
components (`CharredFortress` ext 32 m, `NorthVillage` 32 m, `MorkBorg` 28 m with a 32 m
interior). **Third lay** (2026-09-25): both are placed (seen in game), but `MorkBorg` is
Mörkhalla (`$location_morkhalla`, its interior `DG_MorkHalla`); the **Winding Tunnels**
(`$location_thehole`) are `TheHole01` (ext 28 m, a hut over the tunnels' dungeon `DG_Hole`), and
a village is a generated camp (`DG_NorthVillage`, 15-20 rooms of the northvillage theme in a ring
of 28-30 m), so which rooms it has is decided when its zone generates. Asked for: the Deep North's
places as side paths, but required - the winding tunnels, which a village does not necessarily
have beside it, above all, Mörkhalla, and two villages. So an entry may now start with `~`: its
roads are **side roads**, dirt like a spur (`RoadKind.Spur`: narrow, barely levelled, crossing
water at a spur's price), set out from the main roads and the stones at cost 0, never a start
for a later main road, but due, pinned and followed by their own spurs like any road. And
`@` after the locations names others to prefer instances near (within 400 m, `Planner.NearBy`);
where none is, every instance is a goal. The default now ends with `DN_Bossroom`, then
`~TheHole01`, `~NorthVillage@TheHole01*2` and `~MorkBorg`, all under `defeated_frozenking`, and
the spurs' list has the Deep North's huts, frozen ships, memorial and hot springs, which those
side roads pick up (the user's "one to three other points of interest along the path").
**Verify** whether villages stand near the tunnels at all. The same lay added the **Forge of
Potential** (`AncientUpgradeStation`, `$piece_upgradestation`, ext 16 m, Mountain biome in its
`Location`) as a main road under `defeated_dragon`, due with Moder. A config
still holding a previous default takes the new one. Entries sharing a key are one step of the progression (the mines do not push the Queen out of
the two undefeated bosses due with `Reach` Progression). The roads are pinned as `group #1` to `#3`; a road to a mine
skips the mines its siblings lead to. Found in the same preview: the road to the Queen went as
close as it could by boat, which is right, but a player has to find mines anyway.

**Reuse is what makes it a network.** A new main road may start from any point of a road already
laid, at a starting cost of `alpha × (that point's cost to its hub)` (`search.md`). With alpha
near 1 every location gets a road of its own from the hub - direct, and with its own charm; near
0 roads branch off wherever is nearest, however long the walk from the hub gets. Around 0.4 is
the start: trunks are shared and forks appear where the roads part. Alpha is a setting, so a
server can choose direct roads. Where a road is a fork, the fork is a junction on the old road,
not a second road beside it.

**Spurs, dirt.** Once a main road is laid, the **points of interest** along it get a side path:
a config list of location prefabs, by default the draugr villages (`WoodVillage1`,
`WoodFarm1`), burial chambers and crypts (`Crypt2`-`4`, `SunkenCrypt4`), troll caves
(`TrollCave02`), fuling villages (`GoblinCamp2`), stone towers and the Mistlands' dvergr
entrances - **verify** the prefab names with a location dump. A point of interest gets a spur
when walking to it from the nearest road costs less than its **prize** (config, default about
400 m of easy walking): it is the prize-collecting idea of PLAN.md, reduced to one rule. One
search does all of them: from every point of the new road at cost 0, inside a corridor of the
prize's width around it, to every point of interest in reach; each one settled under its prize
gets its route traced back to where it leaves the road. A spur ends at the location's edge
(its exterior radius, at the side facing the road), not at its centre - it leads to the
entrance of a crypt, not through the village walls. Spurs are narrower (config, about 2.5 m
against the main roads' 4 m) and unlevelled or barely levelled. Until the third lay they never
crossed the sea; since then (2026-09-25, the user: side paths off the roads' sea crossings,
with a harbour to be noticed) a spur pays 15% of a road's boarding and landing, so one may
cross a strait or a fjord, or set out from the road's own crossing to a place on the shore
beside it, and a tall log post (`wood_pole_log_4`) marks where it lands: a **minor harbour**,
told apart from the main roads' harbours, whose vegvisir pins the harbour across
(`laying.md`, landings).
Spurs of later main roads may meet earlier ones; they start from main roads only, so there is
no spur of a spur.

The order is therefore: a main road → its spurs → the next main road. A point of interest near
two main roads is connected once, to whichever road reached it first.

## Where a road ends

(2026-09-25, after the first lay: the Elder's road ran into his altar stones, a mine's to the
door at the bottom of its stairs.) A main road to a location ends where it first crosses the
location's exterior radius (`PathLayer.EndAtEdge`) - read from the game data: Eikthyr 10 m, the
Elder 25, Bonemass 20, Moder 12, Yagluth 32, the mines 20 and 32, the Queen 32, Fader 32, Haldor
12, Hildir 24. The pin and the vegvisir still name the centre. A dungeon entrance the game turns
to the slope (`m_slopeRotation` with an interior radius: the mines, the Queen's gate) is searched
to the edge on its downhill side instead (`PathLayer.Approach`): the game looks along the fall
between the highest and lowest of ten random points in its radius and snaps it to 22.5°, and the
road takes the same fall from 32 points of its own. The location's prefab could not be found in
the bundles to check which way its stairs open - **verify** in game that they face downhill.

Since the third lay (2026-09-25: Yagluth's road ended in one of the pillars around his arena,
which stand past its 32 m) the edge is the location's measured footprint (`Footprints`, the
reach of its prefab's meshes, at most 48 m) where that is wider - except for an entrance turned
to the slope, whose road still ends at its stairs. A side road (`~`) ends the same way.

## Traders

The trader locations are `m_unique`: the first instance whose zone *generates* becomes the trader
and `ZoneSystem.RemoveUnplacedLocations` drops every other instance (read 2026-09-25). Their map
icon shows once placed (`m_iconPlaced`, `GetLocationIcons`). The game also reveals and pins them
to the player by a trigger of its own - a boss kill or similar; it is prefab data, not code,
**verify** which (a `RuneStone` or `Vegvisir` naming the location is the likely form).

**A trader's road goes to a camp before it is generated** (2026-09-25, chosen over waiting for
the camp): due from the start, every camp a goal, the cheapest taken - or with `Remote`, the one
farthest from the network. A player who follows the road generates that camp and makes it the
trader. Until 2026-09-25 a trader was due only once placed, which made `Remote` choose nothing:
by then there is one camp.

**The trader is settled at the road's end** (2026-10-01; before, the first full growth laid a
road to Haldor and nothing showed, and a player finding another camp first left the road ending
at an empty clearing). Once its road is laid, `Traders.Settle` does what the game's
`RemoveUnplacedLocations` would, keeping the pinned camp: every other unplaced instance leaves
`m_locationInstances` and the three location caches, so the game can only place the trader
there (the trimmed list is what the world saves, `PrepareSave`). A `GetLocationIcons` postfix on
the server lists the pinned camp before it is placed, and `SendLocationIcons(0L)` sends the list
to every client, vanilla ones too. `Settle` also runs before each growth: the game may regenerate
locations after an update. A trader placed elsewhere anyway (a road older than this) is still
caught by `Planner.Vanished`: the pin is dropped, a road goes to the camp that is there, the old
road stays.

**`[Network] RevealTraders`** (on): off, no road leads to a trader, nothing is settled or
revealed - for players who want to search for them, which fits camps that move.

## When it grows

- **At server start**, on a new world or an old one: the main roads to **every boss** in the
  list, the traders, and their spurs - the whole network from the start (the user, 2026-10-01).
  `[Network] Reach = Progression` keeps the old growth: up to the **second undefeated boss**,
  and one more group on the next sleep after a boss falls.
- **On the next sleep** after a trader is placed elsewhere
  than its road leads, and
  after a new base appears (a road from the base to the nearest network point, with the same
  search and the base as a hub).

## Bases

The player decides where a base is by placing something. Options:

| Marker | For | Against |
| --- | --- | --- |
| **The ward** (`guard_stone`, recommended to start) | Vanilla, placed exactly where people live. The server sees every ward ZDO. Clients need no mod. | People also ward outposts and portal huts. Wards within 150 m count as one base, and a config setting can require a minimum of pieces nearby. |
| A named sign (`sign`, text starting with a keyword) | Vanilla, deliberate, and names the base for later signposts | Needs text typing. Reads as a hack. |
| A new piece, the *Waystone* (cloned from a vanilla prefab, like Odin's Compass's items) | Clearest to players, can carry its own look and hover text | Every client needs the mod, or the piece is an unknown prefab to them. That undoes "server side only". |

**Built:** the ward, with `BaseMarker` (a prefab name, `guard_stone`) in the config so a server
can switch to another vanilla piece; markers within `BaseRadius` (150 m) are one base, which
needs `BaseMinPieces` (20) pieces with a creator within 30 m of one of its markers. The Waystone comes
later, if the mod ever wants a client side anyway.

## Storage

The network - every laid road as a polyline with its tier, each point's cost to its hub (for the
start costs), the pinned instance of every group, the points of interest already connected - and
the groups no road could reach are world data (there is no queue: the planner works out what is
due from the world each time). They go on one data ZDO of the mod's own at a position no player
ever comes near: `ZNetScene` destroys a ZDO with an unknown prefab only when it is near someone,
and the server never sends a far ZDO to a client, so no prefab has to be registered and Jötunn is
not needed (`prior-art.md`). Not 50 km out: `ZoneSystem.SectorToIndex` covers ±256 zones
(±16 km), and anything outside falls into sector 0 (read 2026-09-25). So it sits at
(-15000, -15000), inside the grid and 11 km past the world's edge. It is created persistent, and a
`Set` on a persistent ZDO marks its chunk dirty for the save. `ZDOMan` loads a ZDO with an
unknown prefab with a warning ("Will load anyway"); **verify** in game that a save and a reload
keep it. The fallback is a file next to the world save. A 3 km road at a point every 8 m is
about 4.5 KB before compression.
