# Odin's Missing Patch

The patch Odin forgot: the quality of life changes Valheim should have shipped years ago, in one
mod. **Client side, no server install.** Every tweak can be switched off on its own.

> ⚠️ **Alpha.** Built heavily with AI, and many ideas come from other mods (see
> [Credits](#credits)). Co-op testing can lag a release by a week. Settings may be renamed or
> moved between versions without carrying over, so check your config after an update.
>
> It is developed for as long as we play, and may stop at any time. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

**Contents:** [Features](#features) · [Configuration](#configuration) ·
[Multiplayer](#multiplayer) · [Translations](#translations) · [Install](#install) ·
[Credits](#credits) · [Recommendations](#recommendations)

## Features

Tweaks marked ⚖️ go beyond QoL and change how the game plays. They are on by default because I
want them, not because they are neutral. Switch them off if you are after tweaks only. Two of
them change a lot:

- **Collateral damage** changes troll and boss fights. Creatures that wander into an arena no
  longer need spawn-proofing to keep out, since the boss hits them too, and a troll with a crowd of
  greydwarfs at its feet is no longer a death sentence.
- **Combat stamina** changes exploration. Climbing mountains is easy, and you can even swim across
  an ocean. ⛔ A sea serpent still eats you, and since stamina costs return once it hunts you,
  fleeing it may drown you.

### Building and crafting

| Tweak               | What it does                                                                                                                                                                      |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Ranges**          | Larger build, craft and repair radius at stations, extensions can stand further away, a larger furniture radius for `Rested`, and demisters clear a wider circle. `1` is vanilla. |
| **Nearby crafting** | Crafting, upgrading and building also take materials from nearby chests. Your backpack pays first.                                                                                |
| **Area repair**     | One hammer repair fixes every damaged piece around you. Hold `Left Alt` to repair just one.                                                                                       |
| **Auto repair**     | Using a crafting station repairs all your gear, including gear from its sister stations if they are built nearby.                                                                 |
| **Endless fuel** ⚖️ | Campfires, hearths, torches, braziers and hot tubs never go out.                                                                                                                  |

### Chests and inventory

| Tweak                 | What it does                                                                                                                             |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| **Quick stack**       | Press `.` to move every stack into a nearby chest that already holds that item. Chests that took something glow.                         |
| **Favourites**        | `Alt`-click an item to keep it out of quick stack and sorting. `Alt`-click an item _in a chest_ to mark the chest for that kind of item. |
| **Chest buttons**     | Icon buttons for _fill your stacks_, _take all_, _place all_, _fill the chest's stacks_ and _sort the chest_.                            |
| **Inventory buttons** | _Stack nearby_ and _sort_ next to your inventory. Equipped items and favourites stay where you put them.                                 |
| **Station refill**    | Use a fire, smelter or similar with an empty backpack to take fuel from a chest. `Shift` + Use fills fuel, ore, food or bolts in one go. |

### Moving around

| Tweak                 | What it does                                                                                                                                                                           |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Combat stamina** ⚖️ | Sprinting, jumping, swimming, building, chopping and mining cost no stamina while nothing hostile is near or hunting you. Bosses always cost stamina. Free swimming means no drowning. |
| **Fast portals**      | The trip ends as soon as the other side has loaded. Dungeon entrances are instant.                                                                                                     |
| **Resting**           | Sitting down grants `Rested` immediately, and you heal based on your comfort level while sitting.                                                                                      |

### Gear and combat

| Tweak                     | What it does                                                                                                                    |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| **Keep gear on death** ⚖️ | On the Casual death penalty, chosen item types (weapons, armour, tools, ...) stay in your inventory and stay equipped.          |
| **Equip while running**   | Hotbar presses equip weapons, shields and armour while sprinting.                                                               |
| **Auto shield**           | Drawing a one handed weapon raises a shield with it, your favourite first.                                                      |
| **Power picker** ⚖️       | Pick a Forsaken power from the radial menu, without going back to the sacrifice stone.                                          |
| **Pocket upgrades** ⚖️    | Haldor sells the extra inventory rows after the boss you choose. _Wider Pockets_ comes after the Elder by default.              |
| **Collateral damage** ⚖️  | Trolls and bosses hit the creatures in their way. Those creatures never fight back and drop nothing unless you helped kill them |

### Map and players

| Tweak                | What it does                                                                                                                                                  |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Auto pins**        | Dungeons, ores you strike and places like fuling villages, tar pits and dragon eggs are pinned when you come close. Pins are shared with every player online. |
| **Pin looks**        | Pins are coloured by biome and get their own icons, which name the place on hover. Toggles on the large map show or hide each kind.                           |
| **Death pins**       | A death pin disappears once your grave is gone.                                                                                                               |
| **Shared map table** | Walk up to a cartography table and your map and its map merge, no click needed.                                                                               |
| **Player marks**     | A glowing mark shows other players who are far away or behind cover, and an arrow at the screen's edge shows players who are off screen.                      |

![Player marks: a friend's mark seen through the forest](https://raw.githubusercontent.com/rausche-kuh/valheimmods/main/images/friend_marker.webp)

## Configuration

The config file is `BepInEx/config/rauschekuh.odinsmissingpatch.cfg`. You can also change
settings in game with a config manager, and they apply right away.

- Every tweak has an `Enabled` switch and its own section.
- `General` → `ChestRange` sets how far chests count for nearby crafting, quick stack and station
  refill.
- `General` → `KeepHotbar` keeps your hotbar out of quick stack, _place all_ and sorting.

> 💡 A tweak that is **off at game start doesn't touch the game at all**. If it clashes with
> another mod, switch it off and restart. If a game update breaks a tweak, only that tweak
> switches off, and the BepInEx log names it.

## Multiplayer

Nobody else needs the mod, except for collateral damage. When the mod does touch the world, it
does exactly what you could do by hand.

**Things to know:**

- Keep gear on death switches itself off on worlds harsher than Casual.
- **Collateral damage** only fully works when everyone has the mod, because each creature is run
  by the game of whoever is nearest.

## Translations

The mod follows Valheim's language setting. AI translations ship for English, German, Russian,
Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and Ukrainian.

To add a language, open `translations.csv` in `BepInEx/plugins/rauschekuh-OdinsMissingPatch/` and
add a column named the way Valheim names the language (`Dutch`, `Czech`, ...). Empty rows stay
English. `$1` and `$2` are filled in by the game. Add it in a pull request and i might integrate it.
Translations are done with AI and only german and english are somewhat tested / used.

## Install

Use a mod manager (Gale or r2modman), or unzip into
`BepInEx/plugins/rauschekuh-OdinsMissingPatch/`. Requires the BepInEx pack for Valheim.

## Credits

These mods covered the same ground first. Use one if you want just that feature or need it
enforced by a server. **⛔ Don't run both of a pair.**

| Tweak                     | Similar mod                                                                                                                                                           | Difference                                                                          |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Endless fuel              | [Eternal Fire](https://thunderstore.io/c/valheim/p/Digitalroot/Eternal_Fire/)                                                                                         | Also covers ovens and smelters, configurable per fire type.                         |
| Ranges (mist)             | [Clear The Air](https://thunderstore.io/c/valheim/p/Crystal/ClearTheAir/)                                                                                             | Server-enforceable.                                                                 |
| Fast portals              | [Proper Portals](https://thunderstore.io/c/valheim/p/Crystal/ProperPortals/)                                                                                          | Server-enforceable.                                                                 |
| Combat stamina            | [Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/)                                                                                      | This one also counts enemies that noticed you.                                      |
| Area repair               | [Venture Area Repair](https://thunderstore.io/c/valheim/p/VentureValheim/Venture_Area_Repair/), [AzuAreaRepair](https://valheim.hexium.gg/mods/Azumatt/AzuAreaRepair) | Here the radius and the single repair key are settings.                             |
| Chest tweaks, auto repair | [SmartCraft-Storage](https://thunderstore.io/c/valheim/p/Zellds/SmartCraftStorage/)                                                                                   | Goes much further with automation. This one keeps every action yours.               |
| Equip while running       | [EquipGearWhileRunning](https://thunderstore.io/c/valheim/p/blacks7ar/EquipGearWhileRunning/)                                                                         | Drop-in, nothing to configure.                                                      |
| Auto shield               | [ShieldMeBruh](https://thunderstore.io/c/valheim/p/Vapok/ShieldMeBruh/)                                                                                               |                                                                                     |
| Pocket upgrades           | [EarlyHaldorPockets](https://valheim.hexium.gg/mods/chooweey/EarlyHaldorPockets)                                                                                      |                                                                                     |
| Auto pins                 | [Discovery Pins](https://thunderstore.io/c/valheim/p/Searica/DiscoveryPins/)                                                                                          | Mass-pins on a key. This one shares pins with other players and through map tables. |
| Shared map table          | [Better Cartography Table](https://thunderstore.io/c/valheim/p/nbusseneau/BetterCartographyTable/)                                                                    | Public and private pins, syncs when you use the table.                              |

## Recommendations

Mods I run alongside this one. None of them overlap with it.

- [Unshamed](https://valheim.hexium.gg/mods/Azumatt/Unshamed) gives back the achievements Valheim
  disables when it sees a mod.
- [HUD Compass](https://thunderstore.io/c/valheim/p/Neobotics/HUDCompass/) adds a compass with
  live markers for your ships, carts and portals.
- [Target Portal](https://valheim.hexium.gg/mods/Smoothbrain/TargetPortal) lets you pick a portal's
  destination from the map. Needs a server install.
- [Plant Everything](https://thunderstore.io/c/valheim/p/Advize/PlantEverything/) puts bushes,
  mushrooms, flowers and every tree on the cultivator.
