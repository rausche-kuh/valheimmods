# Odin's Pins

Map pins that make themselves. Dungeons, the ore you strike and places like fuling villages, tar
pits and dragon eggs are pinned as you find them, and every player online gets them too. Map
tables sync on their own, pins are coloured by biome, and death pins clear away with the grave.
**Client side, no server install.** Every tweak can be switched off on its own.

Part of [Odin's Missing Patch](https://thunderstore.io/c/valheim/p/rauschekuh/OdinsMissingPatch/),
the pack. It works just as well on its own.

> ⚠️ **Alpha.** Built heavily with AI, and many ideas come from other mods (see
> [Credits](#credits)). Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## Features

| Tweak                | What it does                                                                                                                                                  |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Auto pins**        | Dungeons, ores you strike and places like fuling villages, tar pits, surtling fire holes and dragon eggs are pinned when you come close. Pins are shared with every player online. |
| **Pin looks**        | Pins are coloured by biome and get their own icons, which name the place on hover. Toggles on the large map show or hide each kind.                           |
| **Death pins**       | A death pin disappears once your grave is gone.                                                                                                               |
| **Shared map table** | Walk up to a cartography table and your map and its map merge, no click needed.                                                                               |

A mined-out deposit's pin is ticked off when you come by, and a right click removes an auto pin
for good.

## Configuration

The config file is `BepInEx/config/rauschekuh.odinspins.cfg`. You can also change settings in
game with a config manager, and they apply right away.

- Every tweak has an `Enabled` switch and its own section.
- Coming from Odin's Missing Patch? On the first start, every setting it had under the same name
  is carried over.

> 💡 A tweak that is **off at game start doesn't touch the game at all**. If it clashes with
> another mod, switch it off and restart. If a game update breaks a tweak, only that tweak
> switches off, and the BepInEx log names it.

## Multiplayer

Nobody else needs the mod. Players with it get every auto pin the moment someone finds the place;
players without it still get them from a map table. A table behind a ward you have no access to is
only read, never written.

## Translations

The mod follows Valheim's language setting. AI translations ship for English, German, Russian,
Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and Ukrainian. To add a
language, add a column to `translations.csv` in the mod's folder, named the way Valheim names the
language (`Dutch`, `Czech`, ...), and send it in a pull request.

## Install

Use a mod manager (Gale or r2modman), or unzip into `BepInEx/plugins/rauschekuh-OdinsPins/`.
Requires the BepInEx pack for Valheim.

## Credits

These mods covered the same ground first. **⛔ Don't run both of a pair.**

| Tweak            | Similar mod                                                                                        | Difference                                                                          |
| ---------------- | -------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Auto pins        | [Discovery Pins](https://thunderstore.io/c/valheim/p/Searica/DiscoveryPins/)                       | Mass-pins on a key. This one shares pins with other players and through map tables. |
| Shared map table | [Better Cartography Table](https://thunderstore.io/c/valheim/p/nbusseneau/BetterCartographyTable/) | Public and private pins, syncs when you use the table.                              |
