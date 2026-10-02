# The tweaks

What each tweak of Odin's Reach changes, in registration order (`Tweaks` in `src/OdinsReach.cs`),
with its defaults and whether it touches anything but the local client. `package/README.md` says the
same for players; this is the developer's index — the deep notes live in the docs linked per entry.

**Scope** is one of: *client* (nothing leaves the machine, no server install), *world state*
(writes a ZDO — needs the mod on whichever client owns the object), *character* (writes the
player's own save).

Settings that serve several tweaks live in `SharedSettings` (the `General` section): `ChestRange`,
how far a chest counts for every chest tweak, and `KeepHotbar`, which keeps quick stack, Place
all, Fill the chest and Sort off the hotbar row.

| Tweak | Scope | Deep notes |
| --- | --- | --- |
| NearbyCrafting | world state (chests) | [chests](chests.md) |
| QuickStack | world state (chests) | [chests](chests.md) |
| StationRefill | world state (chests, station RPCs) | [chests](chests.md) |
| ChestButtons | world state (the open chest) | [inventory-ui](inventory-ui.md) |
| InventoryButtons | client (chest writes go to the open chest) | [inventory-ui](inventory-ui.md), [item-order](item-order.md) |

- **Nearby crafting** — the game's requirement checks and spends see the player-placed chests
  within `ChestRange` as part of the backpack, backpack paying first. An ingredient
  amount the chests have to pay for shows yellow, and its tooltip lists carried vs. in chests.

- **Quick stack** — a hotkey (`.` by default; G is bound by the game) moves every carried stack into
  the nearest chest in range that already holds that item, with a glow and a floating count per
  chest. An Alt-click in the player's own inventory marks a stack as a favourite (golden
  frame), which quick stacking skips, as it does equipped items and the hotbar; the mark exists only
  while quick stack is on, only in that inventory and is stripped from every stack that leaves it. The same Alt-click in an open
  chest's grid marks the *chest* for that kind of item (`ChestFavorites`, on the chest's ZDO): a
  marked chest counts as holding it, and is filled before the chests that do. A slot of a marked
  kind shows its amount in yellow; the chest panel's Clear favourites button lists every mark in
  its tooltip, the ones the chest holds none of included. Read by
  Chest buttons' Fill the chest's stacks as well, so either tweak alone is enough for the marks to
  mean something. With `TopUpModifier` (Left Shift) held the key runs the other way: every carried
  stack of the `TopUpTypes` (food and meads, ammo) is filled up to its cap from the chests in range,
  nearest first, never opening a new stack, favourites and the hotbar included; each chest that
  gave something glows with a minus count.

- **Station refill** — `SingleFromChests`: the four manual add-fuel interactions (fire, smelter,
  oven, shield generator) see the chests the way nearby crafting does, so a unit comes out of a
  chest when the backpack has none; nothing refuels itself. `AddAll`: Shift + Use on a
  `Fireplace`, a `Smelter` switch (ore or fuel), a `CookingStation` (fuel switch, food switch or
  the spit itself), a `ShieldGenerator` switch or a `Turret` puts in min(room under the cap,
  carried) units through the station's own add RPC, backpack first and the chests after. It hands
  the Use back to the game whenever the game would do exactly the same, so the vanilla messages
  explain a full station or an empty backpack. Was Nearby fuel and Add all.

- **Chest buttons** — the chest panel's Take all and Stack all give way to five icon buttons placed
  beside the panels: fill your stacks from the chest (in the column beside the inventory panel,
  shared with Inventory buttons), which tops the backpack's stacks up to their caps and opens no
  new one; take all, place all, fill the chest's stacks from the backpack and
  sort the chest, in a column beside the chest panel. The two that put things in skip worn gear,
  favourites and the hotbar (`KeepHotbar`); Fill the chest's stacks also takes the kinds the chest is
  marked for, whether or not it holds any.

- **Inventory buttons** — stack nearby (quick stacking by click, shown while that tweak is on and no
  chest is open) and sort, in the same column. The sort merges stacks and lays out by kind, name and
  quality, leaving equipped items, favourites and the hotbar (`KeepHotbar`) in place; materials come
  first by whether a portal carries them, then by family and depth, then by name.

Each chest tweak is kept out of a chest by that chest's "Nearby use" button in the chest panel,
which only a chest a player placed has: found chests and graves are never used from afar.
