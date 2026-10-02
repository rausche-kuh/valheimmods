# Changelog

## 0.1.0

- First release on its own. These tweaks were part of Odin's Missing Patch, which is now a pack
  of smaller mods: nearby crafting, quick stack, station refill, chest buttons and inventory
  buttons.
- A tweak switched off when the game starts doesn't touch the game at all, so switching off one
  that clashes with another mod (and restarting) lets both run. A few tweaks switched on mid game
  wait for the next start; their `Enabled` setting says so.
- If a game update breaks a tweak, only that tweak switches off, and the BepInEx log names it.
- Quick stack: hold Shift while pressing the quick stack key to top up instead. Every stack of
  food, meads and ammo you carry is filled up to its cap from the chests around you, nearest
  first, and nothing you don't already carry is added. Each chest that gave something glows with
  how much it gave. The modifier and the item types are in the Quick Stack settings.
- The Stack nearby and Fill your stacks buttons name their quick stack key in their tooltips.
- The "Nearby use: off" line on a chest's hover text is translated like the rest.
- Nearby fuel and add all are one tweak, **Station refill** (`SingleFromChests`, `AddAll`), the
  four chest ranges are one, `General.ChestRange`, and the three hotbar switches are one,
  `General.KeepHotbar`. These start at their defaults.
