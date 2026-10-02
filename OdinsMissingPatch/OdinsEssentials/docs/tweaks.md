# The tweaks

What each tweak changes, in registration order (`Tweaks` in `src/OdinsEssentials.cs`), with its
defaults and whether it touches anything but the local client. `package/README.md` says the same
for players; this is the developer's index — the deep notes live in the docs linked per entry.

**Scope** is one of: *client* (nothing leaves the machine, no server install), *world state*
(writes a ZDO — needs the mod on whichever client owns the object), *character* (writes the
player's own save).

`General` holds `ThreatRadius`, `EnragedEnemies` and `BossFights`, what counts as danger for
Combat stamina, bound by `Danger.Bind` from the plugin (`Danger` is in `Common/`, shared with the
Item Magnet).

| Tweak | Scope | Deep notes |
| --- | --- | --- |
| Ranges | client | [building-and-world](building-and-world.md), [comfort-and-healing](comfort-and-healing.md) |
| EndlessFuel | world state | [building-and-world](building-and-world.md) |
| CombatStamina | client | [stamina](stamina.md) |
| Resting | client | [comfort-and-healing](comfort-and-healing.md) |
| FastPortals | client | [death-and-portals](death-and-portals.md) |
| KeepGearOnDeath | client | [death-and-portals](death-and-portals.md) |
| AreaRepair | world state (game's own RPC) | [building-and-world](building-and-world.md) |
| AutoRepair | client | [building-and-world](building-and-world.md) |
| PowerPicker | character | [radial-menu](radial-menu.md) |
| EquipWhileRunning | client | [equipping](equipping.md) |
| BuildInWater | client | [equipping](equipping.md) |
| AutoShield | client | [equipping](equipping.md) |
| PocketUpgrades | client | [trader](trader.md) |

- **Ranges** — one multiplier each (1 is vanilla, 2 by default) on a crafting station's
  build/craft/repair radius, how far its extensions may stand from it, the radius Rested counts
  furniture in, and the circle a `Demister` (wisplight, wisp torches, ...) clears of mist (0.2.0).
  The area marker circle grows with the station. Was Station range, Comfort range and Mist clear
  range until they were merged.

- **Endless fuel** — every `Fireplace` (campfires, hearths, torches, braziers, the hot tub) is kept
  topped up, so nothing that burns fuel for light goes out. The only tweak of the first three that
  writes world state (the fuel on the fire's ZDO), owner only.

- **Combat stamina** — sprinting, jumping, swimming, sneaking, building, chopping, mining and
  weapon swings cost nothing while nothing hostile is within 25m and nothing that has noticed the
  player is coming for them; the bar also refills while swimming and mid swing. One switch per cost.

- **Resting** — `InstantRested`: sitting down by a fire grants Rested at once, for the comfort of
  the spot, instead of after the game's wait. `HealthPerComfortLevel`: the game's food regen tick
  also heals `comfort level × HealthPerComfortLevel` while the player is Resting (sitting, with
  `RequireSitting`); the amount is folded into the tick's own `Heal` call so it shows as one
  number, and is healed on its own when no food is eaten. Was Instant comfort and Fireside healing.

- **Fast portals** — a portal trip ends as soon as the screen is black and the other side is
  loaded, not after the fixed eight seconds; the fade is shorter too. Dungeon doors
  (`InstantDungeonDoors`, on by default) skip the black screen entirely when the inside is loaded.

- **Keep gear on death** — items of a configurable list of types (weapons, armour, ammo, tools,
  utility, trinkets, consumables by default) stay in the inventory and stay equipped when the player
  dies; only the rest goes to the grave. Only on a world whose death penalty is Casual, the lowest
  step of the slider (`GlobalKeys.DeathKeepEquip`); inert on any harsher one. Owner-only code path.

- **Area repair** — one `Player.Repair` swing carries on to every damaged `Piece` within a
  configurable radius (10m) of the one the player aims at, closest first, at the game's own cost per
  piece. It writes through the game's own `WearNTear.Repair` RPC, so it needs no server install.

- **Auto repair** — pressing Use on a crafting station repairs every worn item in the inventory that
  station could repair, asking the crafting panel's own `CanRepair` per item, instead of one item
  per click of the repair button. Repairing is free in vanilla, so there is nothing to pay.
  `GroupRepair` widens `CanRepair` to station groups (`ForgeGroup`, `WorkbenchGroup`): an item of
  another station of the group is repaired too, if that station stands within its build range of
  the player at a high enough level. `RequireRealStation` off drops that: the workbench and the
  forge repair everything, no level checked. Covers the repair button as well.

- **Power picker** — a ninth element in the radial menu's top level, a Forsaken powers group whose
  sub menu holds one element per power whose boss has fallen, with the power's own `StatusEffect`
  icon; picking one calls `Player.SetGuardianPower`, the same call the sacrificial stone makes.

- **Equip while running** — the equip queue survives a sprint. `Player.CheckRun` wipes it on
  every sprinting tick in vanilla, so a hotbar press for anything with an equip duration only
  lands once the player slows down; the wipe is dropped for equips and unequips under a flag
  set while `CheckRun` runs, and still drops a queued crossbow reload. Attack, jump and dodge
  clear the queue as before.

- **Build in water** — the hammer (anything with build pieces) and the pickaxe stay in hand
  while swimming and can be drawn there; every other item is still put away, and the off hand
  is emptied. Skips the swim's `HideHandItems` under a flag set while `Humanoid.UpdateEquipment`
  runs, and a transpiler swaps `Humanoid.EquipItem`'s `IsSwimming` refusal for one that waives it
  for those tools.

- **Auto shield** — equipping a one handed weapon raises a shield with it, when the off hand comes
  out of the equip empty. A `Player.ToggleEquipped` prefix records the weapon a press is for, and a
  `Humanoid.EquipItem` postfix acts on that item alone, so restoring gear at login, taking hands
  back out and a drag in the inventory never arm anything; a shield or a torch already in the hand
  survives the weapon and is left alone. The shield is picked favourites first, then the hotbar row,
  then the rest of the backpack, first slot within each, and is equipped through `ToggleEquipped`
  so it takes its own duration and can be interrupted like any equip.

- **Pocket upgrades** — which boss each of Haldor's two extra inventory rows waits for is a
  setting: Wider Pockets, `defeated_dragon` (Moder) in vanilla, is offered once the Elder has
  fallen by default, and Deeper Pockets keeps the Queen. Only the gate moves — the upgrades are
  still bought from Haldor at their own price, once per character, for one row each. The item's own
  `m_requiredGlobalKey` is rewritten from the vanilla one kept aside per item, in a prefix on
  `Trader.GetAvailableItems`, so the game's own filter still asks the question.
