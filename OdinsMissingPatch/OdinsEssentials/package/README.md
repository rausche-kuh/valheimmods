# Odin's Essentials

The small fixes Valheim should have shipped with: bigger station ranges, fires that stay lit,
repairing everything in one go, instant `Rested`, faster portals, no stamina for chores out of
combat, and gear that equips while you run. **Client side, no server install.** Every tweak can
be switched off on its own.

Part of [Odin's Missing Patch](https://thunderstore.io/c/valheim/p/rauschekuh/OdinsMissingPatch/),
the pack. It works just as well on its own.

> ⚠️ **Alpha.** Built heavily with AI, and many ideas come from other mods (see
> [Credits](#credits)). Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## Features

Tweaks marked ⚖️ go beyond QoL and change how the game plays. They are on by default because I
want them, not because they are neutral. Switch them off if you are after tweaks only.

**Combat stamina** changes exploration the most: climbing mountains is easy, and you can even swim
across an ocean. ⛔ A sea serpent still eats you, and since stamina costs return once it hunts
you, fleeing it may drown you.

### Building and crafting

| Tweak               | What it does                                                                                                                                                                      |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Ranges**          | Larger build, craft and repair radius at stations, extensions can stand further away, a larger furniture radius for `Rested`, and demisters clear a wider circle. `1` is vanilla. |
| **Area repair**     | One hammer repair fixes every damaged piece around you. Hold `Left Alt` to repair just one.                                                                                       |
| **Auto repair**     | Using a crafting station repairs all your gear, including gear from its sister stations if they are built nearby.                                                                 |
| **Build in water**  | The hammer and the pickaxe stay in your hand while swimming, so you can build docks and mine rocks without going ashore. Everything else is still put away.                       |
| **Endless fuel** ⚖️ | Campfires, hearths, torches, braziers and hot tubs never go out.                                                                                                                  |

### Moving around

| Tweak                 | What it does                                                                                                                                                                           |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Combat stamina** ⚖️ | Sprinting, jumping, swimming, building, chopping and mining cost no stamina while nothing hostile is near or hunting you. Bosses always cost stamina. Free swimming means no drowning. |
| **Fast portals**      | The trip ends as soon as the other side has loaded. Dungeon entrances are instant.                                                                                                     |
| **Resting**           | Sitting down grants `Rested` immediately, and you heal based on your comfort level while sitting.                                                                                      |

### Gear

| Tweak                     | What it does                                                                                                       |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| **Keep gear on death** ⚖️ | On the Casual death penalty, chosen item types (weapons, armour, tools, ...) stay in your inventory and equipped.   |
| **Equip while running**   | Hotbar presses equip weapons, shields and armour while sprinting.                                                  |
| **Auto shield**           | Drawing a one handed weapon raises a shield with it, your favourite first.                                         |
| **Power picker** ⚖️       | Pick a Forsaken power from the radial menu, without going back to the sacrifice stone.                             |
| **Pocket upgrades** ⚖️    | Haldor sells the extra inventory rows after the boss you choose. _Wider Pockets_ comes after the Elder by default. |

Favourite shields are the ones marked with `Alt`-click in
[Odin's Reach](https://thunderstore.io/c/valheim/p/rauschekuh/OdinsReach/); without it, the
hotbar comes first.

## Configuration

The config file is `BepInEx/config/rauschekuh.odinsessentials.cfg`. You can also change settings
in game with a config manager, and they apply right away.

- Every tweak has an `Enabled` switch and its own section.
- `General` → `ThreatRadius`, `EnragedEnemies` and `BossFights` decide when you count as in
  combat for combat stamina.
- Coming from Odin's Missing Patch? On the first start, every setting it had under the same name
  is carried over.

> 💡 A tweak that is **off at game start doesn't touch the game at all**. If it clashes with
> another mod, switch it off and restart. If a game update breaks a tweak, only that tweak
> switches off, and the BepInEx log names it.

## Multiplayer

Nobody else needs the mod. When it touches the world (keeping a fire lit, repairing a piece), it
does exactly what you could do by hand. Keep gear on death switches itself off on worlds harsher
than Casual.

## Translations

The mod follows Valheim's language setting. AI translations ship for English, German, Russian,
Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and Ukrainian. To add a
language, add a column to `translations.csv` in the mod's folder, named the way Valheim names the
language (`Dutch`, `Czech`, ...), and send it in a pull request.

## Install

Use a mod manager (Gale or r2modman), or unzip into
`BepInEx/plugins/rauschekuh-OdinsEssentials/`. Requires the BepInEx pack for Valheim.

## Credits

These mods covered the same ground first. Use one if you want just that feature or need it
enforced by a server. **⛔ Don't run both of a pair.**

| Tweak               | Similar mod                                                                                                                                                           | Difference                                              |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| Endless fuel        | [Eternal Fire](https://thunderstore.io/c/valheim/p/Digitalroot/Eternal_Fire/)                                                                                         | Also covers ovens and smelters, configurable per fire.  |
| Ranges (mist)       | [Clear The Air](https://thunderstore.io/c/valheim/p/Crystal/ClearTheAir/)                                                                                             | Server-enforceable.                                     |
| Fast portals        | [Proper Portals](https://thunderstore.io/c/valheim/p/Crystal/ProperPortals/)                                                                                          | Server-enforceable.                                     |
| Combat stamina      | [Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/)                                                                                      | This one also counts enemies that noticed you.          |
| Area repair         | [Venture Area Repair](https://thunderstore.io/c/valheim/p/VentureValheim/Venture_Area_Repair/), [AzuAreaRepair](https://valheim.hexium.gg/mods/Azumatt/AzuAreaRepair) | Here the radius and the single repair key are settings. |
| Auto repair         | [SmartCraft-Storage](https://thunderstore.io/c/valheim/p/Zellds/SmartCraftStorage/)                                                                                   | Goes much further with automation.                      |
| Equip while running | [EquipGearWhileRunning](https://thunderstore.io/c/valheim/p/blacks7ar/EquipGearWhileRunning/)                                                                         | Drop-in, nothing to configure.                          |
| Build in water      | [UseEquipmentInWater](https://thunderstore.io/c/valheim/p/LVH-IT/UseEquipmentInWater/)                                                                                | Lets you use all your gear in water, weapons included.  |
| Auto shield         | [ShieldMeBruh](https://thunderstore.io/c/valheim/p/Vapok/ShieldMeBruh/)                                                                                               |                                                         |
| Pocket upgrades     | [EarlyHaldorPockets](https://valheim.hexium.gg/mods/chooweey/EarlyHaldorPockets)                                                                                      |                                                         |
