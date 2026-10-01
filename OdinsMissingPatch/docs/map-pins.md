# Map pins and map tables

The area doc for `SharedMapTable`, `AutoPins`, `PinLooks`, the helpers they share
(`src/UniversalPins.cs`, `src/PinBroadcast.cs`), plus `DeathPins`, which only tidies the game's
own death pin. Implemented 2026-09-23 from the handover plan this file used to be; it
**builds but has not been run in game yet** — see "Not yet verified in game" at the end before
trusting any of it. Every game fact it rests on is in [`map-facts.md`](map-facts.md), read out of
`decompiled/assembly_valheim/` for the current build, with line numbers.

## What it is for

The cartography table exists and nobody uses it, because sharing is a chore: walk to the table,
write, and everyone else has to walk there and read. And pins are a chore: placing one means a
click, a name, another click, so nobody pins the crypt they just cleared. The result is a map that
only the one player who explored an area can navigate.

Four things, all meant to feel like the game already did them:

1. **A table shares by itself** (`SharedMapTable`). Stand near a map table and your map is on it
   and its map is on yours. Nothing to click.
2. **The places worth a pin get one** (`AutoPins`). Dungeon entrances, ore deposits and places
   without an interior (fuling villages, tar pits, dragon eggs, Dvergr outposts, ...) — never
   a vegvisir ruin, since reading the stone pins the boss already. The pin is an ordinary map pin with an ordinary icon and the game's own name for the
   place. What is worth a pin is read off the game's data - an interior, a generator, tar, an item
   that cannot be teleported, a rock a smelter takes the drop of - not a list of names, so a new
   biome's places come in without a change.
3. **Those pins are universal** (`UniversalPins`, `PinLooks`). They belong to nobody, never
   collide with a pin a player placed by hand, never duplicate however many players and tables
   they pass through, and can be hidden as a group or by category (toggles on the large map),
   coloured by biome so they read at a glance.

4. **Those pins reach everyone at once** (`PinBroadcast`). A pin made on one client is on every
   other modded client's map the same moment, through a routed RPC to everybody, and a player
   who joins asks the others for theirs once. The tables are still the road for a player without
   the mod.

Deliberately not done: new pin types with own icons and cloned legend buttons (BetterMap, see
`references.md`; it degrades on uninstall and is a lot of UI surgery), a server-side component,
or replacing the table's protocol (BetterCartographyTable). Everything is client side and speaks
the game's own table format, so a player without the mod sees the auto pins as ordinary shared
pins; the broadcast is a routed RPC the server forwards without knowing it.

The Forge of Potential is location `AncientUpgradeStation`, flagged icon-placed and unique in the
game data: the game itself draws it on every map once its zone has been generated, like Haldor and
Hildir. Every icon-flagged location is skipped (below), so it gets no pin from us.

## How the game does it

The game facts all of this rests on (pins, the table protocol, locations, ore, graves,
the RPCs) are in [`map-facts.md`](map-facts.md). Read them before changing any of it.

## How it works

### Identity: a universal pin is a shared pin nobody owns

An auto pin is an ordinary saved pin (`m_save = true`, vanilla type) with

- `m_ownerID = UniversalPins.Owner`, a fixed non-zero constant
  (`"OdinsMissingPatch".GetStableHashCode()` shifted left with the low bit set); and
- `m_author = new PlatformUserID("OdinsMissingPatch", "<category>")`, category one of `dungeon`,
  `ore`, `place`, `portal`.

That single choice gets the whole "universal" behaviour from vanilla: the table carries it to
everyone, the position dedupes it, the shared-map toggle hides all of them at once, and a player
without the mod sees a normal shared pin. **The author is the identity**; the owner is restored
from it (`Normalize`) whenever it has been lost — to a claim (owner 0) or to a player without the
mod writing it to a table under their own id.

The helper's patches run while either `AutoPins` or `SharedMapTable` is on:

1. **Table read keeps our pins** (`KeepOnTableRead`). `AddSharedMapData` deletes foreign pins
   the table does not hold, which would eat a fresh discovery at the first table that has not seen
   it. The prefix zeroes the owner of every universal pin for the length of the read — the game
   then treats them as the player's own, never marks them, and still position-dedupes the blob's
   pins against them — and the finalizer puts the owner back. Swapping the owner keeps each pin's
   `PinData`, its ticked state and its marker; re-adding the missing ones afterwards would not.
   The finalizer then normalizes what the blob brought, removes what the player has dismissed, and
   drops a universal pin the read added within the category's merge radius of one the player
   already had (the table's own check is 1m; two strikes on one ore deposit are metres apart).
2. **A claim becomes a tick** (`UndoClaims`, `Minimap.Update` postfix with the large map open):
   a universal pin with owner 0 gets its owner back and `m_checked` flipped, so the vanilla first
   click ticks it. Owner 0 can only come from a claim, since a blob never carries it.
3. **Removal is remembered** (`RecordRemoval`, prefix on `RemovePin(Vector3, float)`): the
   position and category go into `m_customData["omp.pins.dismissed"]` as
   `world;category;x;z|...` (world UID, one decimal). Discovery and the table read both skip a
   dismissed position within the category's merge radius. `omp_pins_forget` (dev build) clears
   the current world's.

A removal is local to the character. Our next write leaves the pin off the table (a write carries
only the writer's own map), so a player without the mod loses it at their next read, while a
player with the mod keeps theirs through the read and puts it back on the next write.

### Discovery: the client, from registries, not sweeps

Everything is decided on the client that is there; there is no scan of `ZNetScene.m_instances`.

| Source | Hook | Position | Name |
| --- | --- | --- | --- |
| Dungeon | `Location.s_allLocations` with `m_hasInterior`, every 3s | its outside `Teleport` (fall back to the location) | `Teleport.m_enterText`, else `m_discoverLabel`, else `$omp_dungeon` |
| Place | `Location.s_allLocations` without an interior, a rule or `PlaceList`, every 3s | the location | the list's token (`Name=$token`, or the default's), else `m_discoverLabel`, else the rule's name, else the prefab name |
| Ore | `MineRock5`/`MineRock`/`Destructible.Damage` postfixes, attacker is the local player | the rock | the `m_name` of what the smelter makes of the drop (`$item_copper`, `$item_iron` for scrap, ...), or of the item itself for an `ExtraOre` one (`$item_softtissue`) |

A place without an interior gets a pin when it is in `PlaceList` or one of these holds, first
match wins (`Classify`, once per `Location`):

1. **Treasure**: a `Pickable` of an item that cannot be teleported — named by the item (the
   dragon egg in `DrakeNest01`).
2. **Generated**: it has a `DungeonGenerator` — named by `Room.Theme`: fuling village, village
   (Meadows and Deep North), farm, fortress ruins, and "Ruins" for the Ashlands ruins and any
   theme a later update adds.
3. **Tar**: a `LiquidVolume` of tar — "Tar Pit".

Before any rule, a location holding a `Vegvisir` is ruled out: the stone pins its boss when read,
which is all the ruin is for, so a pin of its own is clutter. `PlaceList` still wins, so a vegvisir
ruin named there is pinned.

What is left over holds nothing that names it: houses, huts, shipwrecks, runestones, the Dvergr
sites. The Dvergr sites are worth a pin and are the default `PlaceList`. Against the game's table
as of 2026-09-23 the rules pin all 12 interiors, 7 generated places, 3 tar pits and the drake nest
and rule out 16 vegvisir ruins; the list adds the 10 Dvergr sites. (The two outdoor places of
mystery and the Deep North memorial were in the list until vegvisirs were dropped; they are
vegvisir ruins too.)

Portals are not pinned: that is to become a feature of its own. `Category.Portal` (author
`OdinsMissingPatch_portal`) stays in `UniversalPins` for it, and `PinLooks` has no zoom setting
for it (never hidden).

Each location is described once (`ConditionalWeakTable<Location, Site>`): prefab name from the
proxy's `s_location` hash via `ZoneSystem.GetLocation`, whether the game draws its own icon
(`m_iconAlways || m_iconPlaced` — boss altars, traders, Hildir, the Forge: skipped in both
categories), and the entrance (looked for again until found, in case the door spawns late).
Locations count only within `DiscoverRange` (40m, 3D, so the interior 5000m up is
never "near"; 0 pins whatever is loaded), and the sweep skips while the player is in an interior.
A muddy scrap pile inside a crypt is excluded by `InInterior`.

**Mined out** (`SweepMinedOut`, the same 3s sweep): an ore pin within 32m of the player, in an
area `ZNetScene.IsAreaReady` calls loaded, with no rock that drops ore within 12m of it
(`Physics.OverlapSphereNonAlloc`, then `GetComponentInParent` for the three rock kinds and the
same `OreLabel` test) on two sweeps in a row, has been mined out — by anyone, since a rock is
destroyed on its owner's machine and just vanishes on the others. `MinedOut` decides: `Tick`
(default) sets `m_checked`, the game's cross, once per pin and session so a player who unticks
it is not overruled; `Remove` takes it off through `UniversalPins.Discard`, which records it as
dismissed like a right click; `Keep` does nothing. 12m because a pin sits where the deposit was
first struck and a copper deposit's pieces spread from there; a neighbouring deposit that close
keeps the pin, which errs the right way.

Before adding: a universal pin of the same category within `MergeRadius` (1m for locations and
portals, whose positions every client derives from the same ZDO; 8m for ore) means it is already
there, whoever put it there; a dismissed position means no; and so does any other saved pin that
is not a death pin within `PinSpacing` (10m, XZ; 0 off) — the player's own, a friend's from a
table, or another mod's. That last one is what keeps a map made with a discovery pin mod
(ordinary pins, the dungeon pin on the entrance and the ore pin on the rock, as ours) from
getting a second pin beside each of its own. It is checked only before adding; a universal pin a
table brings next to such a pin is kept. The pin goes in through
`UniversalPins.Add`, and `$msg_pin_added: <name>` goes to the top left with the pin's icon, as
`DiscoverLocation` does.

### Sharing: read what is new, write only what the table lacks

`SharedMapTable.Sync` is `OnWrite` minus what a player would notice and minus what is not
needed: decompress `s_data` once, `AddSharedMapData` (the read) only when the data is new to
this client, `GetMapData` + `InvokeRPC("MapData")` (the write) only when the table is out of step
with this map, no message, no effect. Tables register in a `MapTable.Start` postfix; a dead entry
is pruned on the next check.

**A write is the expensive half, and it is expensive for everyone**: the packing is a main-thread
hitch for the writer, and the blob (the whole compressed exploration bitmap, hundreds of KB on a
well-travelled map) then goes to the table's owner, to the server and on to every client that has
the zone loaded, modded or not, where each modded one reads it. So reads and writes have
separate triggers. A 1s check (Player.Update postfix, local player) handles at most **one** table
per check. A table within `SyncRange` (64m):

- is **read** when its `DataRevision` differs from the one this client last read or wrote — on
  arrival with new data, or when someone else wrote. Our own write moves the revision too once it
  reaches the owner; the owner stores exactly the sent bytes, so a revision whose data is
  byte-for-byte what we sent is taken as read (`Unread`).
- is **written** on **arrival** (not up to date for this visit; beyond `SyncRange + 8m` a table
  is forgotten, so the next arrival counts again, and the margin keeps the edge from flickering)
  and when **a pin changed** (`AddPin`/`RemovePin(PinData)` postfixes on saved pins bump a
  generation counter, not while a sync itself runs) — if the ward allows it, and only if `Lacks`
  says the table is out of step: a cell explored here (`m_explored | m_exploredOthers`, copied
  out as `int[]` words and compared against the blob's one-byte-per-cell bitmap straight out of
  the array, a few ms) that the blob has not; a saved non-death pin with no table pin within 1m
  (XZ, the table's own merge rule); or a table pin with no pin of ours within 1m, which — since
  the table has been read — is one the player took off, and the write is what leaves it off.

**Someone else's write never triggers a write back.** The first version did that, and two modded
clients at one table wrote the map to each other every `MinInterval` for as long as they stood
there: each write serialises the map in the writer's own pin order, so the bytes never matched
"what I sent", each side saw a foreign write, read, and wrote again. Three players meant three
blobs through the server every interval, and every client in the zone paid for each one. That
was the lag. With the split the sequence converges: A arrives and writes, B reads it, and B
writes only if B has something A did not — after which A reads and has nothing to add.

No table syncs more often than `MinInterval` (30s; it was 10s, and an existing config keeps the
old value). A table behind a ward the player cannot use is read, never written. Two players
writing one table in the same second is last-writer-wins on the ZDO; since each write merges
first, the loser's pins return on their next write. A "pins only, keep my exploration private"
switch is deliberately absent: the game's writer always merges the bitmap, and faking it needs
BetterCartographyTable's transpiler.

### Broadcast: a pin goes to everyone the moment it is made

`PinBroadcast` is the road that needs no table, on for as long as `AutoPins` is on and its
`Share` setting (default on) is. Two routed RPCs, registered per session in a `Game.Start`
postfix:

- **`OdinsMissingPatch_Pins`** carries a list of pins: version int, an "announce" bool, a
  count, then category int, name and position per pin. `AutoPins.TryPin` sends one to
  `Everybody` right after it has added a pin of its own (never for one it received, so nothing
  echoes). Since `Everybody` is handled locally too, the handler drops a call whose sender is
  `m_id`. What arrives goes through `AutoPins.Receive`: the receiver's own category switch
  decides whether it wants the category at all, its own icon setting picks the type, and then
  the same `Place` as a local find - the merge radius, the dismissed record and `PinSpacing` all
  apply - so a pin the receiver removed by hand, or has a hand-placed pin next to, is not added.
  A live pin is announced in the top left like a find of one's own (`ShowMessage`); a catch-up
  is not, since it can be dozens at once.
- **`OdinsMissingPatch_PinsRequest`** has no payload. `RequestOnce` sends it to `Everybody` from
  the 3s sweep, once per `ZRoutedRpc` instance - so once per session, and only once sharing is
  on, which covers a player who switches it on mid-game. Every modded client that gets it
  answers the sender alone with all its saved universal pins, announce off. A newcomer with N
  modded peers gets N lists; the merge radius makes them one.

Only category, name and position travel: the owner and author are the receiver's to set
(`UniversalPins.Add`), the tick state stays local (the mined-out sweep ticks it again where the
receiver comes by). A pin received adds through `AddPin`, so `SharedMapTable`'s generation
counter moves and a table in range is written on the next check - which is right, the table
lacks it - and three players at one table all seeing the same broadcast each check `Lacks` first,
so only the first of them writes.

What the broadcast does not do: reach a player without the mod (the tables do), reach a player
who was offline and never asks (the request goes out once per session, so a pin made while they
were away is a table's job), carry a removal (a removal is per character by design), or carry
`AutoPins` pins to a player whose `AutoPins` is off (`Sharing` is `On && Share`, on both ends).

### Looks: colour by biome, hide by zoom or by toggle

`PinLooks.Tint` is an `UpdatePins` postfix. For each universal pin with a marker: in the large map,
zoomed out past the category's zoom setting (`OreZoom` and `PlaceZoom` 0.5, `DungeonZoom` 1 =
never; a portal pin has no setting and is never hidden), the marker and the name are deactivated; otherwise the marker is active
again (also after the tweak is switched off) and, while on, icon and name take the biome's colour
with alpha = `m_sharedMapDataFade`, so the vanilla toggle still fades them. The biome is asked of
`WorldGenerator` once per `PinData`. Colours are one `ConfigEntry<Color>` per biome, hand-picked
light tints (Meadows green, Black Forest darker green, Swamp brown, Mountain ice blue, Plains
yellow, Mistlands lilac grey, Ashlands red, Deep North pale blue, Ocean blue).

**Toggles** (`MapToggles`): at `Minimap.Start` the panel of `m_publicPosition` (`PublicPanel`,
bottom right, 250x42 at y 20, pivot bottom) is copied once per category with a show setting and
stacked above it at 51 px a row — the step from it to `SharedPanel` (y 92, pivot middle, which is
`m_sharedMapHint` and only shown once there is shared data) — so the copies sit at 122, 173, 224,
clear of the legend (`IconPanel`, x -85..-9). Each copy's `onValueChanged` is replaced, since it
still carried the prefab's call to `OnTogglePublicPosition`, and its `UIGamePad` and hint are
destroyed, since the copy would answer the same gamepad key and flip with the original (so the
toggles are mouse only). The label is set by reflection on the TextMeshPro `text`. A toggle writes
`ShowDungeons` / `ShowOre` / `ShowPlaces`; `Tint` then culls that category on **both** maps the
same way as the zoom does, so a hidden pin cannot be clicked either. Every change in the section
(`Tweak.OnSettingChanged`) re-syncs the toggles and sets `m_pinUpdateRequired`; the tweak off
hides the toggles and shows every pin.

**Icons** (`Icons`, on by default): a dungeon pin and a dragon egg pin are drawn with a
sprite out of `assets/icons` (loaded by `PanelButtons.Icon`) instead of the type's, picked by the
pin's name token, the only thing about the pin that survives the profile, a table and a broadcast:
`$location_sunkencrypt` the crypt, `$location_mountaincave` the ice
cave, `$item_dragonegg` the nest, any other dungeon `map_entrance` (`PinLooks.IconOf`, also used
for the top left message). `Tint` swaps `m_iconElement.sprite` and puts `m_icon` back when the
switch is off; `m_type` and `m_icon` stay vanilla, so the legend, a player without the mod and a
table all see the configured icon type. The sprites are coloured, so they get white at
`m_sharedMapDataFade` instead of the biome tint, and their name is deactivated after
`UpdatePins` turned it on. The name moves to a tooltip: the markers take no raycasts (the map
image under them gets every click, `OnMapLeftUp`/`RightClick` then look for the closest pin), so
`PinLooks.Hover`, a `Minimap.Update` postfix, finds the marker under the pointer by its rect in
the large map with the mouse active and calls `OnHoverStart` on a `UITooltip` added to it (the
window prefab borrowed from the inventory, `PanelButtons.TooltipPrefab`, topic = the name). The
tooltip's own `LateUpdate` hides it once the pointer leaves that rect, and `OnDisable` when the
marker goes. `OnHoverStart` puts the window under the marker's nearest canvas; it is moved to the
root canvas so the map cannot clip it. The game's location name tokens were read out of
`resources.assets` (the English localization): `location_forestcrypt` Burial Chambers,
`location_sunkencrypt` Sunken Crypts, `location_mountaincave` Frost Caves, `location_forestcave`
Troll Cave, `location_bearcave`, `location_mausoleum` Tomb of Lord Reto, `location_morkhalla`,
`location_dvergrtown` Infested Mine, `location_morgenhole`, `location_thehole`, ...

Icons are per category and vanilla: dungeons `Icon1` (house), ore `Icon3` (the orb), places
`Icon0` (fire), `Icon4` (portal) kept for the portal feature; a value outside `Icon0..Icon4` falls back to the default (BepInEx
cannot restrict an enum entry to a list — `AcceptableValueList<T>` needs `IEquatable<T>`). Because
each category has its own icon, the legend's double tap hides a category. `Icon2`, the hammer, is
left free for the player's own pins.

### Death pins: gone with the grave

`DeathPins` leaves the game's death pin alone except for two things:

1. **No grave, no pin** (`OnlyWithGrave`): a `Player.OnDeath` prefix clears a flag, a
   `TombStone.Setup` postfix sets it when the owner id is the local player's, and the `OnDeath`
   postfix removes the death pin within 1m of the player if the flag is still clear.
2. **The pin goes with the grave** (`RemoveWithGrave`): graves register in a `TombStone.Awake`
   postfix. Every 2s, a death pin within 32m (3D, so a death in a dungeon is checked only from
   inside it, where its grave is too) in an area `ZNetScene.IsAreaReady` calls loaded, with no
   grave of the local player within 8m (XZ) on two sweeps in a row, is removed. It asks the
   world, like `MinedOut` and through the same `PinSweep`, so it does not matter who emptied the
   grave or on which machine it was destroyed; an old death pin whose grave is long gone is
   cleared on the next visit.

## The pieces

| File | Holds |
| --- | --- |
| `src/UniversalPins.cs` | owner, author, `IsUniversal`, `TryGetCategory`, `Find`, `Add`, `Normalize`, the dismissed record, `UndoClaims`, `KeepOnTableRead`, `RecordRemoval` |
| `src/Tweaks/SharedMapTable.cs` | section "Shared Map Table": `SyncRange` (64), `MinInterval` (30s); the registry, the check, `Unread`, `Lacks`, the silent read and write |
| `src/Tweaks/AutoPins.cs` | section "Auto Pins": `Dungeons`/`Ore`/`Places`, `DiscoverRange` (40), `PinSpacing` (10), the three icons, `PlaceList`, `ExtraOre` (`Softtissue`), `SkipOre` (`TinOre`), `MinedOut`, `ShowMessage`, `Share`; the sweep, the place rules, the ore strike, the mined-out check, `Receive` |
| `src/PinBroadcast.cs` | the two routed RPCs: `Send` (one pin to everybody), `RequestOnce` (ask everybody once per session), the handlers, the per-session registration |
| `src/Tweaks/PinLooks.cs` | section "Pin Looks": a colour per biome, a zoom for dungeons, ore and places, `MapToggles` and `ShowDungeons`/`ShowOre`/`ShowPlaces`, `Icons`; the tint and icons, the hover tooltip, the large map's toggles |
| `src/Tweaks/DeathPins.cs` | section "Death Pins": `RemoveWithGrave`, `OnlyWithGrave`; the grave registry, the sweep, the no-grave check |
| `src/PinSweep.cs` | the "still there?" check the mined-out and the grave sweeps share: pins in range and in a loaded area, gone on two sweeps in a row, one instance (its own marks) per sweep |
| `src/Dev/MapCommands.cs` | Debug only: `omp_locations [filter]` (every `ZoneLocation`: name, biome, interior, game icon, discover label, entrance text, the place rule it falls under — also to the BepInEx log), `omp_pins`, `omp_pins_forget`, `omp_pins_clear` |

Default `PlaceList`, from the game's location table (BetterMap's `Mistlands_GuardTower1-3` no
longer exist; the towers are `_new` and `_ruined_new` now): `Mistlands_Excavation1-3`,
`Mistlands_GuardTower1-3_new`, `Mistlands_GuardTower1_ruined_new`, `_ruined_new2`,
`Mistlands_GuardTower3_ruined_new` and `Mistlands_Lighthouse1_new`, each with its
`$omp_place_*` token in `AutoPins.DefaultPlaces`. Translations: `$omp_dungeon`, nine
`$omp_place_*` (the rules' and the list's names) and the three toggle labels `$omp_map_*`. Dungeons, ore and treasure are named by the
game's own tokens (`$location_*`, `$item_*`) and need no row.

## Not yet verified in game

Built on Linux in Debug and Release; nothing below has been seen running. In order:

1. **`omp_locations`**: the `rule` column should match the offline read above (read from the
   prefab asset, where every child is active); the `PlaceList` names exist; Morkhalla (`MorkBorg`)
   comes through the dungeon rule.
2. **Location on the host.** The zone's generator spawns locations in `SpawnMode.Full`; check that
   a `Location` under a `LocationProxy` exists there too, so the host pins the same way a client
   does (the prefab-name fallback `Utils.GetPrefabName` covers a missing proxy).
3. **Two clients on a local host**: A places a table, B walks up, both maps merge without a click;
   a table under B's ward is read but not written by A; the arrival hitch is no worse than a
   manual write; two players standing next to a table do **not** keep writing it every
   `MinInterval` (watch the BepInEx log for the game's `Compressed map data:` line, one per
   write; the first version looped here and lagged every client in the zone); walking in and out
   of range at a base with nothing new explored writes nothing; a pin placed by hand, and one
   removed, reach the table within `MinInterval`.
4. **Pins**: a burial chamber is pinned once with the game's name, entering does not pin again,
   B gets it through a table at the same position; striking copper pins "Copper" once, tin
   "Tin", a lava leviathan "Flametal", a Mistlands giant helmet "Iron"; silver is not pinned
   until struck; a Dvergr barrel and a crypt mudpile are not pinned; a tar pit, a fuling village
   and a drake nest are pinned on walking up (the rules see the inactive children); a copper
   deposit mined to the last piece is ticked a few seconds later, and one mined by B while A was
   away is ticked when A comes by; a deposit still half there keeps its pin; with `Remove` the pin
   goes and no table brings it back; a removed pin stays removed across a table
   round trip; a foreign pin the table lacks survives a read; a first left click (mouse and
   gamepad) ticks a universal pin and the shared-map button still hides it.
5. **Looks**: Swamp and Mountain tints read against the map; the fade follows the toggle; culled
   pins come back on zooming in and after switching the tweak off; the name text is found (the
   `TextMeshPro` type-name lookup).

6. **Changes of 2026-09-23 (from the DiscoveryPins comparison)**: a hand-placed pin at a crypt
   entrance keeps the auto pin away, a death pin nearby does not; the Mistlands giant remains
   are pinned as soft tissue (is the drop in their `DropTable` / `DropOnDestroyed`, and does a
   `Destructible` one pass `OnlyPickaxe`?); tin is not pinned; a death with an empty inventory
   leaves no pin; a grave looted by you, and one looted by another player while you were away,
   takes its pin with it a few seconds after you are near; a grave that slid down a slope keeps
   its pin; dying inside a dungeon, the pin stays until the grave there is emptied.
7. **Toggles and vegvisirs**: the three toggles sit above "visible to other players" without
   overlapping the shared-map panel or the legend, at 1080p and at a wide aspect; each hides its
   category on the large map and the minimap and survives a restart; the original toggle still
   sets the public position and a gamepad press flips only the original; walking up to a
   vegvisir ruin adds no pin (Charred Fortress included).
8. **Broadcast (two clients on a local host, then on a dedicated server without the mod)**: A
   strikes copper and B, anywhere in the world, gets "Copper" in the top left and the pin on the
   map at once, coloured and toggled like B's own; A gets no second message and no duplicate
   from its own broadcast; B with `Ore` off gets nothing; B with a hand-placed pin at the
   crypt A found gets no auto pin there; a pin B removed by hand does not come back when A
   finds the place again. C logs in later: A's and B's pins are on C's map a few seconds after
   spawning, without a message, and A and B get nothing back. A vanilla client on the same
   server sees no error in its log. Switching `Share` on mid-game triggers the request once.
9. **Icons (2026-09-24)**: a burial chamber, a sunken crypt, a frost cave, a troll cave and a
   drake nest show the entrance, crypt, ice cave, entrance and nest icon on both maps, without a
   name, not greyed or tinted; hovering one on the large map shows the game's tooltip with the
   place's name after half a second, over the map and not clipped, and it goes when the pointer
   leaves; clicking and right clicking the pin still tick and remove it; the top left message on
   discovery shows the new icon; switching `Icons` off brings back the vanilla icon and the name
   without reopening the map; a table read or a broadcast gives the other player the same icons.

Known limits: if the arrival hitch is noticed, the
blob can be built off the main thread (copy `m_explored`/`m_exploredOthers`, pack and compress on
a `Task`, send on the next Update). `AddSharedMapData` is version-tolerant and the write goes
through the game's own serializer, so nothing needs doing unless `Version.SharedMap` grows.

## References

The three checkouts that cover this ground, with what each one settled, are in
[`references.md`](references.md) under "Map tables and auto pins". Copy nothing (MIT needs the
notice, the other two carry no license at all).
