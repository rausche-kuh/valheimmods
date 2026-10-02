# Changelog

## Unreleased

- **Settings reorganised; changed values are not carried over.** Check the config file after
  updating:
  - Station range, comfort range and mist clear range are one tweak, **Ranges**, with one
    multiplier each.
  - Instant comfort and fireside healing are one tweak, **Resting** (`InstantRested`,
    `HealthPerComfortLevel`, `RequireSitting`).
  - Nearby fuel and add all are one tweak, **Station refill** (`SingleFromChests`, `AddAll`).
  - The four chest ranges are one, `General.ChestRange`, and the three hotbar switches of quick
    stack, chest buttons and inventory buttons are one, `General.KeepHotbar`.
  - Combat stamina's `ThreatRadius`, `EnragedEnemies` and `BossFights` moved to `General`: they
    now decide when you are in combat for the item magnet too.
- Added: item magnet. Hold `Y` and your character rises into the Forsaken power pose, wreathed
  in its fire, while every item lying around flies to your feet — from 10m after a second, and
  further the longer you hold. It stops by itself once the last item in reach has landed. What
  fits in your backpack is picked up, the rest piles up at your feet. It only works while nothing hostile is near or after you, and an item that was moved once
  (pulled, or dropped by a player) is never pulled again, so it tidies up after a fight or a felled
  forest but never carries a load for you. No cooldown, no stamina.
- The "Nearby use: off" line on a chest's hover text is translated like the rest.
- Quick stack: hold Shift while pressing the quick stack key to top up instead. Every stack of
  food, meads and ammo you carry is filled up to its cap from the chests around you, nearest
  first, and nothing you don't already carry is added. Each chest that gave something glows with
  how much it gave. The modifier and the item types are in the Quick Stack settings.
- The Stack nearby and Fill your stacks buttons name their quick stack key in their tooltips.
- Auto repair: a station repairs the gear of the other stations of its group too — the forge
  mends black forge gear, the workbench mends Galdr table gear — as long as that station is built
  nearby and upgraded far enough. Works with the repair button as well. Turn
  `RequireRealStation` off to let the workbench and forge repair everything, no other station
  needed.

- Added: collateral damage. A troll's swing and ground slam hit the greydwarfs in the way,
  Eikthyr's lightning hits the dwarfs in the arena, Yagluth's meteors hit fulings and lox, and
  so on for every boss but the Queen. Nothing hit this way fights back or picks a new target,
  bosses never hit what they spawned, and a creature killed this way drops nothing unless you did at least half of
  the work. Every player needs the mod for it to apply everywhere.
- A tweak switched off when the game starts no longer touches the game at all, so switching off
  one that clashes with another mod (and restarting) lets both run. A few tweaks switched on
  mid game now wait for the next start; their `Enabled` setting says so.
- Tried to mitigate broken tweaks by catching and disabling only whats broken on a game update
- Pin looks: Burial Chambers, Troll Caves and Winding Tunnels get icons of their own instead of
  the plain door, fuling villages and tar pits get icons too, and every map icon is now drawn at
  the same size.

## 0.3.2

- Combat stamina: boss fights always cost stamina. While a boss health bar is on screen every cost
  is back, even when the boss is circling, slow or after another player. `BossFights` in the
  Combat Stamina section turns it off.
- Items a chest is marked to take now show their amount in yellow in the chest, so you can see its
  favourites at a glance. The Clear favourites button's tooltip still lists them all, including
  the ones the chest is currently out of.
- Player marks: every other player gets a small glowing gold and orange mark, with a thin gold ring, a rugged dark outline and a few drifting sparks, over
  their head once they are out of sight or more than 50m away, and on the edge of the screen when
  they are off it, so you can always tell which way your friends are. The mark turns pale blue as they
  get further away. Players beyond the area around you only show if they are visible on the map. Distances,
  size and colours are in the Player Marks section.
- Pin looks: dungeon pins get icons of their own — a crypt for Sunken Crypts, an ice cave for
  Frost Caves, a door for every other dungeon — and dragon egg pins a dragon's nest. These pins
  no longer write their name on the map; hover one on the large map to see what it is. `Icons` in
  the Pin Looks section switches back to the plain icons and names.

## 0.3.1

- Auto pins reach everyone at once: a pin you get appears on the map of every other player
  online that moment, and when you join, their pins appear on yours — no map table needed.
  `Share` in the Auto Pins section turns it off. Players without the mod still get the pins
  from a table.
- The inventory and chest buttons show their icons again when the mod was installed through a mod
  manager that unpacks the icons next to the DLL instead of into their folder (Gale does).
- The chest buttons come back after leaving to the main menu and entering a world again; before,
  only the inventory's Sort button survived and opening a chest threw an error every frame.
- Shared map tables no longer lag everyone: two players near one table kept writing the map to
  each other every ten seconds, and every write sends the whole map to everyone in the area. A
  table is now written only when it is actually missing something of yours, never just because
  someone else wrote to it, and at most every thirty seconds (`MinInterval`; an existing config
  keeps its old value, so raise it by hand or delete the line).

## 0.3.0

- Fast portals: walking into a dungeon or cave entrance, and back out, is now instant.
- Shared map tables: walk up to a cartography table and your map goes onto it and its map onto
  yours, without touching it — again whenever a pin of yours changes nearby or someone else has
  written to it. A table behind a ward you cannot use is only read.
- Auto pins: dungeon and cave entrances, ore deposits you strike (tin / obsidian are left out),
  fuling villages and other ruined settlements, tar pits, dragon egg nests
  and the Dvergr sites of the Mistlands.
- Death pins: a death pin disappears by itself once your grave is gone, whoever emptied it, and a
  death that leaves no grave (nothing on you, or everything kept) leaves no pin either.
- Those pins are coloured by the biome they stand in, and ore and place pins hide when the large
  map is zoomed far out. Three new toggles at the bottom right of the large map, next to "Visible
  to other players", show or hide dungeon, ore and place pins on both maps.
- The "Nearby use" button no longer shows on chests you did not build, such as dungeon chests,
  which nearby use never takes from anyway.

## 0.2.3

- Pocket upgrades: Haldor now offers Wider Pockets, the first extra inventory row, once the Elder
  has fallen instead of Moder — the row arrives while your backpack is still what is slowing you
  down. Deeper Pockets still waits for the Queen. Both are still bought from Haldor at his own
  price, and a setting picks the boss each one waits for, up to none at all.
- A chest can now be made the home of a kind of item: Alt-click an item inside an open chest, the
  same click that makes a favourite in your own inventory, and the chest is marked for that kind.
  Quick stack and Fill the chest's stacks then put it there even when the chest holds none of it,
  and quick stacking fills the chests marked for an item before the ones that only happen to hold
  one — so a chest emptied of its wood still draws the next load back.
- Closing the inventory with a chest open no longer flashes the game's old Take all and Stack all
  buttons over the chest panel, nor slides Stack nearby into the buttons beside your inventory,
  while the screen fades out; every button now stays put until the panel is gone.
- While a chest has any marks, a Clear favourites button appears in its panel next to the Nearby
  use switch: it counts them, lists them in its tooltip and clears them all in one click.
- Every word the mod shows now follows the language Valheim is set to. English, German, Russian,
  Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and Ukrainian ship with
  it, each built on Valheim's own wording, and any other language is a column added to
  `translations.csv` beside the DLL.

## 0.2.2

- Auto shield: drawing a one handed weapon now raises a shield with it, if your off hand is empty
  and you carry one. A shield or torch already in your hand stays, and the shield is chosen from
  your favourites first, then your hotbar, then the rest of your backpack.
- Fill your stacks now only tops the stacks you already carry up to their caps; what the chest
  holds beyond that stays in the chest instead of landing in your free slots as a new stack.
- Sorting your inventory now leaves what you have equipped where it is, the way it already left
  your favourites, and lays everything else out around both.
- A favourite item is now marked with a yellow border around its slot instead of a filled
  background, so you can still tell at a glance whether a favourite is equipped.

## 0.2.1

- Equip while running: pressing a hotbar key while sprinting now equips or unequips the weapon,
  shield or armour without you having to slow down. It still takes the usual moment, an
  attack, jump or dodge still interrupts it, and a crossbow still only reloads once you stop.

## 0.2.0

- Endless fuel: campfires, hearths, torches, braziers and the hot tub never go out.
- Mist clear range: wisplights, wisp torches and everything else that clears the mist reach twice
  as far.
- Combat stamina: sprinting, jumping, swimming, sneaking, building, chopping, mining and weapon
  swings cost nothing while nothing hostile is near you or hunting you, and the bar refills while
  you swim or swing. One switch per cost, the radius configurable.
- Instant comfort: sitting down by a fire grants Rested at once, for the comfort of the spot you
  sit in, instead of after ten seconds of Resting.
- Fireside healing: resting by a fire heals you for the comfort of the spot, every ten seconds,
  on top of what your food heals — two health per comfort level by default, so a campfire out in
  the open is a slow mend and a furnished hall patches you up in a minute. The rate is
  configurable, and a switch decides whether you have to be sitting.
- Fast portals: a portal sends you through as soon as the screen is black and the other side has
  loaded, instead of after a fixed eight seconds, and the fade to black is twice as quick.
- Keep gear on death: weapons, armour, ammunition, tools, the belt and your food stay with you
  when you die, and stay equipped, so you respawn ready to fight your way back. Only materials,
  trophies and the rest of the run's loot go to the grave. Which item types stay is configurable.
  Only applies on a world whose death penalty is set to Casual, the lowest setting — on a world
  set any harsher the grave takes everything the game says it should.
- Nearby crafting: crafting, upgrading and building take their materials from chests around you
  without opening them, your backpack first. The counts in the crafting panel and the build menu
  include those chests: an amount your backpack covers stays white, one that needs the chests
  turns yellow, and hovering an ingredient shows what you carry and what the chests hold. Only
  chests placed by a player count; every chest gets a "Nearby use" button in its panel to keep it
  out of this. The range is configurable.
- Quick stack: a hotkey (. by default) stacks your inventory into the chests around you that
  already hold each item. Every chest that took something glows and shows how many it took.
  Alt-click an item in your inventory to mark it as a favourite, which keeps it out of quick
  stacking; equipped items and the hotbar stay too. A favourite only holds while the stack is
  yours — put it in a chest, leave it in your grave or drop it and the mark is gone. Hotkey,
  modifier, range and the hotbar are configurable.
- Nearby fuel: adding fuel to a fire, smelter, oven or shield generator by hand takes it from a
  chest around you when your backpack has none. Nothing refuels itself.
- Add all: Shift + Use on a fire, smelter, oven, cooking station, shield generator or ballista
  puts in everything that fits, instead of one. Fuel, ore, food and bolts all come out of your
  backpack first and then out of the chests around you, without your opening them. The hover
  text says what would go in, and the range is configurable.
- Auto repair: opening a crafting station repairs everything you carry that it can repair, so
  the forge mends what belongs to the forge the moment you walk up to it.
- Area repair: one swing of the hammer repairs every damaged piece around the one you aim at,
  closest first, for the usual cost per piece. The radius is configurable, and holding Left Alt
  repairs the single piece you aim at as before.
- Chest buttons: the chest panel's Take all and Stack all become five icon buttons beside the
  panels: fill your stacks from the chest beside your inventory, between armour and weight;
  take all, place all, fill the chest's stacks from your backpack and sort the chest in a
  column down the side of the chest. Putting things in leaves worn gear, favourites and the
  hotbar alone.
- Inventory buttons: a stack nearby button (quick stack by click, while no chest is open) and a
  sort button in a column beside your inventory, between the armour and the weight. Sort merges
  your stacks and orders them by kind and name, leaving favourites and the hotbar where they
  are. Materials are not just alphabetical: an ore sits with the bar it smelts into and with
  what that bar makes, every log sits with the coal it burns down to, families come in the order
  you meet them, and everything a portal refuses to carry ends up in one block at the end.
- Power picker: a Forsaken powers category in the radial menu, holding the power of every boss you
  have beaten with its own icon. Pick one and it is the power your power key casts, without the
  trip back to the sacrificial stones. The category shows the power you are carrying, and the
  cooldown you are on carries over, so switching mid cooldown buys you nothing.

## 0.1.0

- First release: station range and comfort range, both configurable.
