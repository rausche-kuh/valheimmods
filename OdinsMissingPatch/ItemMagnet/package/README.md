# Item Magnet

Hold `Y` and your character rises into the Forsaken power pose, wreathed in its fire, while every
item lying around flies to your feet: from 10m after a second, and further the longer you hold.
It stops by itself once the last item in reach has landed. What fits in your backpack is picked
up, the rest piles up at your feet. **Client side, no server install.**

It cleans up, it never hauls: it only works while nothing hostile is near or after you, and an
item that was moved once (pulled, or dropped by a player) is never pulled again. So it tidies up
after a fight or a felled forest, but never carries a load for you. No cooldown, no stamina.

Part of [Odin's Missing Patch](https://thunderstore.io/c/valheim/p/rauschekuh/OdinsMissingPatch/),
the pack. It works just as well on its own.

> ⚠️ **Alpha.** Built heavily with AI. Co-op testing can lag a release by a week. The
> [source](https://github.com/rausche-kuh/valheimmods) is free to copy under the MIT license; a
> credit is appreciated.

## Configuration

The config file is `BepInEx/config/rauschekuh.itemmagnet.cfg`. You can also change settings in
game with a config manager, and they apply right away.

- `Item Magnet` → the key, how far the pull starts, how fast it grows and how far it reaches.
- `General` → `ThreatRadius`, `EnragedEnemies` and `BossFights` decide when you count as in
  combat.
- Coming from Odin's Missing Patch? On the first start, every setting it had under the same name
  is carried over.

## Multiplayer

Nobody else needs the mod. Your game takes over an item before moving it, the way picking it up
works, and the game's own auto pickup does the rest.

## Translations

The one message it shows follows Valheim's language setting, with AI translations for English,
German, Russian, Chinese, Spanish, French, Brazilian Portuguese, Polish, Italian, Japanese and
Ukrainian.

## Install

Use a mod manager (Gale or r2modman), or unzip into `BepInEx/plugins/rauschekuh-ItemMagnet/`.
Requires the BepInEx pack for Valheim.

## Credits

[Magnetic Wishbone](https://thunderstore.io/c/valheim/p/Terrenteller/MagneticWishbone/) covered
similar ground first: the Wishbone, upgraded, becomes a wider auto pickup. **⛔ Don't run both.**
