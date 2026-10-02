# The tweaks

What each tweak of Odin's Pins changes, in registration order (`Tweaks` in `src/OdinsPins.cs`), with
its defaults and whether it touches anything but the local client. `package/README.md` says the same
for players; this is the developer's index — the deep notes live in the docs linked per entry.

**Scope** is one of: *client* (nothing leaves the machine, no server install), *world state*
(writes a ZDO — needs the mod on whichever client owns the object), *character* (writes the
player's own save).

| Tweak | Scope | Deep notes |
| --- | --- | --- |
| SharedMapTable | world state (map tables, the game's own write) | [map-pins](map-pins.md) |
| AutoPins | character (the removed-pin record); a routed RPC to the other clients | [map-pins](map-pins.md) |
| PinLooks | client | [map-pins](map-pins.md) |
| DeathPins | client | [map-pins](map-pins.md) |

- **Shared map table** — a cartography table syncs by itself, silently, within `SyncRange` (64m):
  it is read whenever it holds data this client has not read (arrival, someone else's write), and
  written on arrival and when a saved pin changes while in range — only if the table actually
  lacks something of this map, and never in answer to someone else's write, since that looped.
  At most every `MinInterval` (30s) per table. A table behind a ward the player has no access to
  is only read.

- **Auto pins** — dungeon entrances, struck ore deposits, and places found by rule
  (generated camps and villages, tar pits, dragon eggs; never a vegvisir ruin) or listed in `PlaceList`
  get an ordinary map pin with a vanilla icon, owned by nobody (`UniversalPins`), so a table
  hands each one to everyone exactly once. Locations within `DiscoverRange` (40m) every three
  seconds; ore from the strike itself (`ExtraOre` adds soft tissue, `SkipOre` drops tin). No pin
  within `PinSpacing` (10m) of a saved non-universal, non-death pin. Portals are not pinned — a
  separate feature to come. A mined-out deposit's pin is ticked when you
  come by (`MinedOut`: `Tick`, `Remove` or `Keep`). A right click removes one for good (recorded
  on the character, per world). With `Share` (on) every pin made here goes to every player
  online the moment it is made, as a routed RPC (`PinBroadcast`), and a player who joins asks
  everyone for theirs once; the receiver applies its own switches, icons, spacing and removals.
  Players without the mod still get the pins from a table.

- **Pin looks** — those pins are tinted by the biome they stand in instead of the shared-pin
  grey, and ore and place pins hide beyond zoom 0.5 on the large map. `MapToggles` adds a toggle
  per category above the large map's "visible to other players" one; each sets `ShowDungeons`,
  `ShowOre` or `ShowPlaces`, which hide that category on both maps.

- **Death pins** — a death pin within 32m whose grave (a `TombStone` the local player owns) is not
  within 8m of it on two sweeps in a row is removed (`RemoveWithGrave`); a death that set up no
  grave has its pin removed right after `Player.OnDeath` (`OnlyWithGrave`).
