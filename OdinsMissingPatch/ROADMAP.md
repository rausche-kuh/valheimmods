# Roadmap

What comes next, roughly in the order it is worth doing: small UI fixes first, the features that
need a server or change balance last. Each entry says which tweak it belongs to (new or existing)
and which doc it has to be checked against before any code is written — see the table in
`CLAUDE.md`.

Game facts marked **verify** were written from memory of the game code, without `decompiled/` at
hand. Check each one against `decompiled/assembly_valheim/` first; what the check teaches goes
into the doc for the area, not here.

## Station repair by groups

_AutoRepair and the crafting panel's repair — [building-and-world](docs/building-and-world.md)_

Repairing a full kit means visiting four stations. Group them:

- **Forge group** — Forge and Black forge (and any other station whose name ends up as an item's
  `m_craftingStation` / `m_repairStation` for metal gear; list them from `ObjectDB` once rather
  than by hand).
- **Workbench group** — Workbench, Galdr table, Artisan table, Stonecutter, Cauldron and the
  rest that craft or repair gear.

At a station of a group, an item whose own station is **another station of the same group** can
be repaired too, if that other station **stands within its own range of the player** (so it has
to exist in the base, and its upgrade level is the one that counts for the recipe's
`m_minStationLevel`). Nothing else changes: crafting and upgrading still need the right station.

How: a postfix on the private `InventoryGui.CanRepair(item)` that, when the answer was no,
retries against every station of the current one's group found with
`CraftingStation.HaveBuildStationInRange(name, playerPos)` (verify the signature) and its level.
AutoRepair and the repair button both ask `CanRepair`, so both pick it up. A switch
(`GroupRepair`) under AutoRepair, on by default. Client side.

## Boss fights

### No world spawns during a boss fight

_New tweak, BossArena — needs research first_

While a boss is fighting (`IsBoss()`, alerted, within ~100m of a player), the ordinary world
spawns stop around it, so a fight is the boss and not the boss plus drakes, deathsquitos or seekers
wandering in. The boss's own adds must keep coming — blocking them would make the fight easier,
which is not the point.

Research:

- World spawns are `SpawnSystem.UpdateSpawning`, run per zone by the zone's owner (verify). A
  prefix that returns false while a fighting boss is within the zone's reach blocks them all at
  once. Random events (`RandEventSystem`) are separate — decide whether a raid may start mid-fight.
- The bosses' adds do not come from `SpawnSystem`, as far as known: Bonemass's blobs, Yagluth's
  and The Queen's seekers and Fader's minions are `SpawnAbility` projectiles/attacks on the boss
  itself (verify per boss, and check for any boss that _does_ rely on a `SpawnSystem` or a
  location `CreatureSpawner` around its altar). If all adds are `SpawnAbility`, blocking
  `SpawnSystem` is safe by construction.
- The zone owner is not necessarily a player with the mod: on a mixed server a zone owned by an
  unmodded client keeps spawning. That is a caveat for the mod page, the same kind the range
  tweaks carry.

## Item magnet

_New tweak, ItemMagnet — [radial-menu](docs/radial-menu.md) for the animation_

A hotkey plays the Forsaken power animation (`m_zanim.SetTrigger("gpower")`, the one
`Player.ActivateGuardianPower` uses, verify) and pulls every `ItemDrop` within reach towards the
player, where the game's own auto pickup takes over — so weight, full inventory and auto pickup's
own rules still apply.

Balance, so it never beats a cart:

- Radius 12m, cooldown 60s, and it costs stamina (a flat 30) — a cleanup after a fight or a
  felled forest, not a hauling tool.
- It only moves items, it does not store them: an encumbered player gets a pile at their feet.
- Only items dropped on the ground; never an `ItemStand`, a grave or a chest.
- Items must be owned to be moved: claim each drop's `ZNetView` before moving it, the way
  pickup does (verify how `ItemDrop.Pickup` hands ownership over).

  /home/hauke/Documents/Code/test/othervalheimmods/Valheim-Plugins/MagneticWishbone/ has some code which might be helpful for this

## Boss markers

_AutoPins / PinLooks / UniversalPins — [map-pins](docs/map-pins.md)_

Boss pins (`PinType.Boss`, set by a vegvisir via `Minimap.DiscoverLocation`) become
**universal pins**: shared with every modded player the moment they are found and through map
tables, like auto pins, and drawn **violet** by PinLooks instead of the shared-pin grey. They are
also kept for good: a right click does not remove them.

Open: what should make them "permanent" beyond that — are boss pins being lost today (e.g. when
the boss dies, or on a table read), or is it that other players never get them? Check what the
game does to a boss pin on the boss's death before building anything.

## Enshrouded style sleeping

_New tweak, SharedSleep — needs a server install_

Going to bed no longer waits for everybody:

- Getting into bed broadcasts `{in bed}/{online} waiting for sleep` to everyone.
- Every player in bed makes time run faster (for example `+1×` per sleeper, capped), so one
  sleeper speeds the night up a little and everyone asleep still skips it the vanilla way.
- Everybody in bed is the normal Valheim sleep: `Game.UpdateSleeping` / `EnvMan.SkipToMorning`
  untouched.

Research: the day is the server's clock (`ZNet.m_netTime` on the server, sent to clients, verify),
so speeding it up is server side — this is the first tweak that needs the mod on the server, and
it breaks the mod page's "nothing needs a server install" line unless it is kept optional and
says so. Things that run on time (plant growth, smelters, fermenters, respawn timers) speed up
with it, which is the point of a shorter night but should be stated.

**Credits:** the idea is copied from https://thunderstore.io/c/valheim/p/Vapok/BetterSleepBruh/

## Use Equipment in Water

https://thunderstore.io/c/valheim/p/LVH-IT/UseEquipmentInWater/

# Bugs

None known.
