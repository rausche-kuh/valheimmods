# Odin's Missing Patch

> **Warning:** this mod is developed heavily with the use of AI, and many of its ideas and even source
> code is copied from other mods.
>
> The mod is actively tested, but co-op playtime may only come every other day. Bugs I find get
> patched, yet a release might not be tested in co-op until a week after it ships. Consider this
> an alpha.
>
> While it is one, settings get renamed, merged and moved between versions without being carried
> over: after an update, check the config file for anything you had changed.

The patch Odin forgot: my personal take on the quality of life changes Valheim should have shipped
years ago, in one mod. Nothing needs a server install.

## Changes beyond QoL

Some tweaks go beyond QoL and change how the game plays, and I ship them on by default
because I want them, not because I can argue they are neutral:

- **Combat stamina** is the big one. Eliminating stamina usage outside of combat removes a huge
  part of Valheim's exploration. This drastically simplifies climbing mountains and allows for
  swimming in the ocean.
- **Keep gear on death** removes the stakes of dying on a world already set to the Casual death
  penalty. The corpse run is a real punishment, but not one I have the time for.
- **Endless fuel** deletes the coal-and-wood upkeep of a lit base. Small, but it is an economy.
- **Pocket upgrades** hands you Haldor's first extra inventory row two bosses early. It is a
  progression change, not a QoL one: the cramped backpack of the Swamp and the Mountains is meant
  to be part of the game, and I would rather spend those hours on the game's other ideas.
- **Collateral damage** makes trolls and bosses hit the creatures in their way. It is mostly for
  the look of it, but a greydwarf crowd around a troll now thins out.

## What it does

- **Ranges** — multipliers (2 by default, `1` is vanilla) on a crafting station's
  build/craft/repair radius, how far an extension may stand from it, the radius `Rested` counts
  furniture in, and the circle a wisplight, wisp torch or any other demister keeps clear of mist.
- **Endless fuel** — tops the fuel back up on every campfire, hearth, torch, brazier and hot tub,
  so nothing that burns for light goes out.
- **Combat stamina** — drops the stamina cost of sprinting, jumping, swimming, sneaking, building,
  chopping, mining and swinging while nothing hostile is within 25m and nothing that has noticed
  you is coming for you. A boss fight always costs stamina: every cost is back while a boss health
  bar is on screen. Free swimming means no drowning unless attacked, since drowning starts
  at empty stamina.
- **Resting** — sitting down grants `Rested` immediately, instead of after the game's ten
  seconds of Resting, and while you rest you heal `comfort level × 2` on the game's own
  ten-second food regen tick.
- **Fast portals** — ends the trip as soon as the screen is fully black and the far side has
  loaded, instead of the game's flat eight seconds. Dungeon and cave entrances are instant, with
  no black screen at all.
- **Keep gear on death** — makes casual death penalty even weaker: an item-type list
  (weapons, armour, ammo, tools, utility, trinkets, consumables by default) defines which stay
  in your inventory and stay equipped.
- **Area repair** — after a hammer swing lands, repeats the game's own repair on every damaged
  piece within 10m, closest first. Hold `Left Alt` for the single piece.
- **Nearby crafting** — while a craft, upgrade or build is being checked or paid for, the game
  also checks player-placed chests within 20m (`ChestRange`). Your backpack pays first, the nearest chest pays
  the rest.
- **Quick stack** — one key (`.`) pushes every carried stack into the nearest chest in range that
  already holds that item, merging into its stacks before taking a slot. Each chest that took
  something glows with a count. Equipped items, the hotbar (`KeepHotbar`) and favourites stay.
  `Alt`-click an item to make it a favourite.
- **Chest favourites** — `Alt`-click an item inside an open chest to mark the chest for that kind
  of item. Quick stack and Fill the chest's stacks put it there even when the chest holds none.
- **Station refill** — Use on a fire, smelter, oven or shield generator takes its one unit of
  fuel from a nearby chest when your backpack has none, and `Shift` + Use on a fire, smelter,
  kiln, oven, cooking station, shield generator or ballista fills up fuel, ore, food and bolts in
  one go, backpack first and the chests after. Each half has its own switch.
- **Auto repair** — on Use of a crafting station repairs all repairable items.
- **Chest buttons** — replaces Take all and Stack all with five icon buttons placed beside the
  panels rather than on them: **fill your stacks** from the chest, and, down the chest's side,
  **take all**, **place all**, **fill the chest's stacks** and **sort the chest**.
- **Inventory buttons** — stack nearby and **sort**, beside your inventory. The sort merges stacks
  and orders by kind, then name, then quality, below the hotbar. What you have equipped and your
  favourites stay in the slot you put them in, and everything else is laid out around them.
- **Power picker** — adds a Forsaken powers ring to the radial menu, so you can pick a power
  without running to the sacrifice stone.
- **Equip while running** — a hotbar press while sprinting still equips the weapon, shield or armour.
- **Auto shield** — drawing a one handed weapon raises a shield with it. It picks the shield
  you marked as a favourite first.
- **Pocket upgrades** — Haldor sells the two extra inventory rows, **Wider Pockets** and **Deeper
  Pockets**, once the boss you choose has fallen in that world. Wider Pockets waits for the Elder
  instead of Moder by default; Deeper Pockets keeps the Queen.
- **Shared map table** — come within 64m of a cartography table and your map is written onto it
  and its map onto yours, silently, with no click. It is read again whenever someone else has
  written to it, and written again when a pin of yours changes nearby — only when the table is
  actually missing something of yours, and never more than every thirty seconds.
- **Auto pins** — dungeon and cave entrances, ore deposits you strike (not tin, which is
  everywhere; soft tissue from the Mistlands giants counts) and places (fuling villages and
  other ruined settlements, tar pits, dragon eggs, Dvergr excavations and watchtowers, ...)
  get an ordinary map pin when you come within 40m. Every pin you get appears on the map of
  every other player online at that moment, and when you join, their pins appear on yours.
- **Pin looks** — those pins are coloured by their biome, dungeons, dragon nests, fuling villages
  and tar pits get icons of their own (burial chambers, troll cave, crypt, frost cave, winding
  tunnels, any other entrance) that name the place when you hover them, and ore and place pins hide when the large map is zoomed far out. Toggles at the bottom right of the large map, beside "Visible to
  other players", show or hide dungeon, ore and place pins.
- **Death pins** — a death pin goes away by itself once your grave is gone, whoever emptied it,
  and a death that leaves no grave leaves no pin.
- **Player marks** — a small glowing mark over every other player who is
  behind a hill or a wall, or more than 50m away, and on the screen's edge in their direction when
  they are off screen.
- **Collateral damage** — a troll's swing and ground slam hit the greydwarfs in the way, Eikthyr's
  lightning hits the dwarfs in the arena, Yagluth's meteors hit fulings and lox, and every other
  boss but the Queen (her arena is closed, all around her is her brood) hits whatever stands in
  its attacks. Nothing hit this way fights back or picks a new target, and your own fight is
  untouched: nothing can stand between you and a swing. Bosses never hit what they spawn (the
  Elder's roots, Bonemass's blobs, Fader's adds). A creature killed this way drops nothing, unless you dealt the killing blow or trolls and
  bosses took no more than half its health (`LootLimit`). `Damage` scales the hits, `Creatures`
  lists which creatures besides bosses do it (`Troll`).

## Configuration

`BepInEx/config/rauschekuh.odinsmissingpatch.cfg`, written on first run. Every tweak has an
`Enabled` switch; the ranges have a multiplier (`1` is vanilla), and the rest have the settings
named above. Two settings in `General` serve several tweaks at once: `ChestRange` (20m), how far
a chest counts for nearby crafting, quick stack and station refill, and `KeepHotbar` (on), which
keeps quick stack, Place all, Fill the chest's stacks and Sort off your hotbar row. Changes apply while the game runs, including from an in-game config manager.

A tweak that is switched off when the game starts does not touch the game at all, so if one
clashes with another mod, switch it off and restart and both run side by side. Switching such a
tweak on later mostly works at once; the few that hook into things as the world loads (the
chest tweaks, Death Pins, Shared Map Table, Pin Looks, Auto Pins) wait for the next
start. If a game update breaks a tweak, only that tweak switches itself off, and the BepInEx
log says which one.

## Translations (AI generated)

The mod's buttons, hover text and messages follow the language Valheim is set to. English, German,
Russian, Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and Ukrainian
ship with it, each built on Valheim's own wording. Any other language is a column in
`translations.csv`, which sits next to the DLL in `BepInEx/plugins/rauschekuh-OdinsMissingPatch/`.
Open it in a spreadsheet or a text editor, add a column headed with the language exactly as Valheim
names it (`Dutch`, `Czech`, `Portuguese_European`, ...) and fill in the rows — anything left empty
stays English, and `$1` and `$2` are the numbers and item names the game fills in, which may stand
anywhere in the sentence. Send one over and it ships with the next version.

## Multiplayer

Client side; no server install, and nothing required of anyone else, except for **collateral
damage** (below).

Most of it never leaves your machine. Four things touch the world, and each does it the way you
would by hand: **area repair** sends the game's own repair, one piece at a time. The **chest
tweaks** take a chest over for the write exactly as opening it does, skip a chest someone else has
open or that you could not open yourself, and respect the per-chest **Nearby use** switch stored
with it. **Endless fuel** writes the fuel on fires your machine owns — Valheim gives a fire to
whoever is nearest, so a fire only a modless player stands by burns down as usual. The **shared
map table** writes a table exactly as its Write button does, and never one behind a ward you
cannot use.

Auto pins go straight to every player with the mod who is online, and a player who joins gets
everyone's once (`Share`, on by default; off, they wait for a table). Each player takes them under
their own settings, so a category you switched off never shows up. They travel on map tables in
the game's own format too, so a player without the mod reads them from a table as ordinary shared
pins. Removing one is yours alone: other players with the mod keep theirs and bring it back to the
table.

Two caveats. Client-side range tweaks reach only you: a player without the mod has vanilla range
at the same bench and sees mist close in at the vanilla distance. And **keep gear on death** goes
further than the Casual death penalty a host chose, for you — on a world set any harsher it
switches itself off, so it can never undo the penalty a host asked for.

**Collateral damage** needs the mod on every machine to apply everywhere: a hit lands only when
the player whose game runs the troll or boss has the mod, and it is handled as a collateral hit
(no fighting back, the loot rule) only when the player whose game runs the creature it hits has
it too. Valheim hands a creature to whoever is nearest, so in a group where everyone has the mod
it simply works.

## Install

A mod manager (Gale or r2modman), or drop the zip's contents into
`BepInEx/plugins/rauschekuh-OdinsMissingPatch/`. Requires the BepInEx pack for Valheim.

## Credits

Several tweaks cover ground others got to first, and are worth a look if you want that one thing
on its own or want it server-enforced. **Do not run both of a pair.**

- Endless fuel follows Digitalroot's
  [Eternal Fire](https://thunderstore.io/c/valheim/p/Digitalroot/Eternal_Fire/), which also covers
  ovens and smelters and is configurable per fire type.
- The mist radius of Ranges and fast portals do what Crystal Ferrai's
  [Clear The Air](https://thunderstore.io/c/valheim/p/Crystal/ClearTheAir/) and
  [Proper Portals](https://thunderstore.io/c/valheim/p/Crystal/ProperPortals/) do; both default to
  vanilla and can be enforced by a server.
- Combat stamina is modelled on Cartur's
  [Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/); this one also
  counts an enemy that has noticed you, wherever it is.
- Area repair does what
  [Venture Area Repair](https://thunderstore.io/c/valheim/p/VentureValheim/Venture_Area_Repair/)
  does at a fixed 20m; here the radius and the single-repair key are settings. Azumatt's
  [AzuAreaRepair](https://valheim.hexium.gg/mods/Azumatt/AzuAreaRepair) covers the same ground.
- The chest tweaks and auto repair cover part of Zellds'
  [SmartCraft-Storage](https://thunderstore.io/c/valheim/p/Zellds/SmartCraftStorage/), which goes
  much further — stations that feed themselves, restocking, animal feeding. This one keeps every
  move yours.
- Equip while running is what blacks7ar's
  [EquipGearWhileRunning](https://thunderstore.io/c/valheim/p/blacks7ar/EquipGearWhileRunning/) got
  to first, and that one is a drop-in with nothing to configure; here it is a switch beside the
  rest.
- Auto shield follows Vapok's
  [ShieldMeBruh](https://thunderstore.io/c/valheim/p/Vapok/ShieldMeBruh/).
- Pocket upgrades follows chooweey's
  [EarlyHaldorPockets](https://valheim.hexium.gg/mods/chooweey/EarlyHaldorPockets).

- Auto pins do what Searica's
  [Discovery Pins](https://thunderstore.io/c/valheim/p/Searica/DiscoveryPins/) does, which also
  clears the death pin when you pick your grave back up and mass-pins on a key; here the pins are
  shared through map tables instead.
- Shared map tables go where nbusseneau's
  [Better Cartography Table](https://thunderstore.io/c/valheim/p/nbusseneau/BetterCartographyTable/)
  goes by another road: that one adds public and private pins and syncs on use; this one keeps the
  game's own table and syncs by walking up to it.

## Recommendations

Mods I run beside this one. None of them overlap it — they fill the gaps this one leaves.

- [Unshamed](https://valheim.hexium.gg/mods/Azumatt/Unshamed) — gives you back the achievements
  Valheim switches off the moment it sees a mod. It unlocks nothing you have not earned, and it
  can backfill the progress your modded hours already made.
- [HUD Compass](https://thunderstore.io/c/valheim/p/Neobotics/HUDCompass/) — a compass across the
  top of the screen, carrying live markers for your ships, carts and portals so you can find where
  you left them.
- [Target Portal](https://valheim.hexium.gg/mods/Smoothbrain/TargetPortal) — pick the portal you
  are travelling to off a map instead of juggling tag pairs, with per-portal access rules and
  favourites. This one wants to be on the server too.
- [Plant Everything](https://thunderstore.io/c/valheim/p/Advize/PlantEverything/) — berry bushes,
  mushrooms, flowers and every kind of tree on the cultivator, with growth timers on the plants and
  a setting for each one.
