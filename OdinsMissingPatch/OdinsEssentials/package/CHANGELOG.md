# Changelog

## 0.1.0

- First release on its own. These tweaks were part of Odin's Missing Patch, which is now a pack
  of smaller mods: ranges, endless fuel, combat stamina, resting, fast portals, keep gear on
  death, area repair, auto repair, power picker, equip while running, build in water, auto shield
  and pocket upgrades.
- A tweak switched off when the game starts doesn't touch the game at all, so switching off one
  that clashes with another mod (and restarting) lets both run. A few tweaks switched on mid game
  wait for the next start; their `Enabled` setting says so.
- If a game update breaks a tweak, only that tweak switches off, and the BepInEx log names it.
- Added: build in water. The hammer and the pickaxe stay in your hand while you swim, and you
  can take them out in deep water, so docks and piers no longer mean wading back to shore.
  Weapons, shields and torches are still put away.
- Auto repair: a station repairs the gear of the other stations of its group too — the forge
  mends black forge gear, the workbench mends Galdr table gear — as long as that station is built
  nearby and upgraded far enough. Works with the repair button as well. Turn
  `RequireRealStation` off to let the workbench and forge repair everything, no other station
  needed.
- Station range, comfort range and mist clear range are one tweak, **Ranges**, with one
  multiplier each, and instant comfort and fireside healing are one tweak, **Resting**
  (`InstantRested`, `HealthPerComfortLevel`, `RequireSitting`). These start at their defaults.
- Combat stamina's `ThreatRadius`, `EnragedEnemies` and `BossFights` are in `General` now.
