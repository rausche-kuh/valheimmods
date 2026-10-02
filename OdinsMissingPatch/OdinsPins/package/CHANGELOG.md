# Changelog

## 0.1.0

- First release on its own. These tweaks were part of Odin's Missing Patch, which is now a pack
  of smaller mods: shared map table, auto pins, pin looks and death pins.
- A tweak switched off when the game starts doesn't touch the game at all, so switching off one
  that clashes with another mod (and restarting) lets both run. A few tweaks switched on mid game
  wait for the next start; their `Enabled` setting says so.
- If a game update breaks a tweak, only that tweak switches off, and the BepInEx log names it.
- Pin looks: Burial Chambers, Troll Caves and Winding Tunnels get icons of their own instead of
  the plain door, fuling villages and tar pits get icons too, and every map icon is now drawn at
  the same size.
- Auto pins: the surtling fire holes of the swamp are pinned as places.
