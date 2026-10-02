# Odin's Reach

The chests around you count as your own. Craft and build from them, quick stack into them with one
key, refuel smelters and fires from them, and sort, fill and empty with buttons beside the
inventory. **Client side, no server install.** Every tweak can be switched off on its own.

Part of [Odin's Missing Patch](https://thunderstore.io/c/valheim/p/rauschekuh/OdinsMissingPatch/),
the pack. It works just as well on its own.

> ⚠️ **Alpha.** Built heavily with AI, and many ideas come from other mods (see
> [Credits](#credits)). Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## Features

| Tweak                 | What it does                                                                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Nearby crafting**   | Crafting, upgrading and building also take materials from nearby chests. Your backpack pays first, and a tooltip shows what came from where.        |
| **Quick stack**       | Press `.` to move every stack into a nearby chest that already holds that item. Chests that took something glow. `Shift` + `.` tops up food and ammo instead. |
| **Favourites**        | `Alt`-click an item to keep it out of quick stack and sorting. `Alt`-click an item _in a chest_ to mark the chest for that kind of item.           |
| **Chest buttons**     | Icon buttons for _fill your stacks_, _take all_, _place all_, _fill the chest's stacks_ and _sort the chest_.                                      |
| **Inventory buttons** | _Stack nearby_ and _sort_ next to your inventory. Equipped items and favourites stay where you put them.                                           |
| **Station refill**    | Use a fire, smelter or similar with an empty backpack to take fuel from a chest. `Shift` + Use fills fuel, ore, food or bolts in one go.           |

Every chest you placed has a _Nearby use_ button: switch it off and no tweak reaches into that
chest from afar. Chests you found in the world and graves are never used.

## Configuration

The config file is `BepInEx/config/rauschekuh.odinsreach.cfg`. You can also change settings in
game with a config manager, and they apply right away.

- Every tweak has an `Enabled` switch and its own section.
- `General` → `ChestRange` sets how far chests count for nearby crafting, quick stack and station
  refill.
- `General` → `KeepHotbar` keeps your hotbar out of quick stack, _place all_ and sorting.
- Coming from Odin's Missing Patch? On the first start, every setting it had under the same name
  is carried over.

> 💡 A tweak that is **off at game start doesn't touch the game at all**. If it clashes with
> another mod, switch it off and restart. If a game update breaks a tweak, only that tweak
> switches off, and the BepInEx log names it.

## Multiplayer

Nobody else needs the mod. Taking from or putting into a chest does exactly what you could do by
hand: a chest someone else has open is left alone.

## Translations

The mod follows Valheim's language setting. AI translations ship for English, German, Russian,
Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and Ukrainian. To add a
language, add a column to `translations.csv` in the mod's folder, named the way Valheim names the
language (`Dutch`, `Czech`, ...), and send it in a pull request.

## Install

Use a mod manager (Gale or r2modman), or unzip into `BepInEx/plugins/rauschekuh-OdinsReach/`.
Requires the BepInEx pack for Valheim.

## Credits

These mods covered the same ground first. **⛔ Don't run both of a pair.**

| Tweak        | Similar mod                                                                         | Difference                                                            |
| ------------ | ----------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| Chest tweaks | [SmartCraft-Storage](https://thunderstore.io/c/valheim/p/Zellds/SmartCraftStorage/) | Goes much further with automation. This one keeps every action yours. |
