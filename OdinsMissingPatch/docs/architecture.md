# Architecture

What each file under `src/` and `assets/` holds. `CLAUDE.md` has the one-line map; the area docs
hold the conventions and game facts behind each piece.

| Path | What |
| --- | --- |
| `src/OdinsMissingPatch.cs` | BepInEx entry point: binds every tweak's config, then patches all. |
| `src/Tweak.cs` | The base class: the section, the `Enabled` switch, `On` (wanted and patched), `BindMultiplier`, `BindList`, `OnSettingChanged`. |
| `src/Patcher.cs` | Applies the patches of the tweaks that are on, class by class; a failed class switches off the tweaks it serves. The `Serves`, `Always` and `LoadHook` attributes. |
| `src/Tweaks/<Name>.cs` | One quality of life change, with its `[HarmonyPatch]` classes nested inside it. |
| `src/NearbyChests.cs` | Shared by the chest tweaks: the registry of loaded containers, the in-reach rule, `Claim`, the "reach" that widens the backpack, the per-chest opt-out flag and its Nearby use switch. |
| `src/ChestFavorites.cs` | The kinds of item a chest is marked to take, on its ZDO: read by QuickStack and ChestButtons, set by an Alt-click in the chest's grid, a yellow amount on a marked slot, listed in the Clear favourites button and its tooltip. |
| `src/ChestGlow.cs` | The golden pulse plus floating text on a chest (`ChestGlow.Flash`). |
| `src/Hotkeys.cs` | `Pressed` / `Held` for a `KeyboardShortcut`, read through `ZInput`. |
| `src/PanelButtons.cs` | The inventory screen's buttons and their layout: icon and text buttons cut from the chest panel's Take all button, registered by their owners, placed once per frame in a column beside each panel or in the vanilla spots, and the switch that hides the game's Take all and Stack all. |
| `src/Stash.cs` | Putting the backpack away: what may leave it, which chest takes which kind, the move into a chest; and `TopUp`, filling existing stacks without opening new ones. Shared by QuickStack and ChestButtons. |
| `src/SharedSettings.cs` | The settings several tweaks read, in the `General` section: `ChestRange`, `KeepHotbar`, and the danger settings `ThreatRadius`, `EnragedEnemies`, `BossFights`. |
| `src/Danger.cs` | Whether the local player is in danger - a hostile near, an enemy coming for them, a boss bar - for CombatStamina and ItemMagnet; the enraged-report patch. |
| `src/MovedMarker.cs` | The item magnet's anti-hauling mark on a drop's ZDO: set on pulled and player-dropped items, carried over when the game merges stacks. |
| `src/Palette.cs` | The colours the mod draws with, one per meaning. |
| `src/PinSweep.cs` | The two-sweep "is it still there?" check for pins near the player, shared by DeathPins and AutoPins' mined-out check. |
| `src/InventorySorter.cs` | Merge-and-sort of an `Inventory` in place, from a given row down, around items a caller keeps. |
| `src/MaterialOrder.cs` | The crafting tree derived from `ObjectDB`: which family a material belongs to and how deep it lies. |
| `src/UniversalPins.cs` | Map pins that belong to nobody (a fixed owner, an `OdinsMissingPatch_<category>` author): the identity, adding, the removed-pin record, and the patches that keep them through a table read and turn a claim into a tick. Used by AutoPins, SharedMapTable and PinLooks. |
| `src/PinBroadcast.cs` | The routed RPCs that hand an auto pin to every player online the moment it is made, and that give a joining player everyone's pins once. Sends for AutoPins, receives into it. |
| `src/Translations.cs` | Hands `assets/translations.csv` to the game's localization on every language setup. |
| `src/Dev/MapCommands.cs` | Debug only: `omp_locations [filter]`, `omp_pins`, `omp_pins_forget`, `omp_pins_clear` — see [`map-pins.md`](map-pins.md). |
| `src/Dev/CollateralTest.cs` | Debug only: `omp_cd <scene>` spawns a troll or boss with peers, `omp_cd_info`, `omp_cd_clear`; every collateral hit and loot decision shown top left. |
| `src/Dev/MagnetCommands.cs` | Debug only: `omp_magnet_scatter`, `omp_magnet_probe` (loaded drops per ring, for choosing `MaxRadius`), `omp_magnet_unmark` — see [`item-magnet.md`](item-magnet.md). |
| `src/Dev/MarkWards.cs` | Debug only: wards stand in for other players so `PlayerMarks` can be tried alone; `omp_marks_wards` switches it. |
| `assets/icons/` | The button icons, 64px white-on-transparent PNGs, and the coloured `map_*` pin icons PinLooks draws, shipped beside the DLL. Gale flattens the folder on install, so `PanelButtons.Icon` looks in `icons/` and then beside the DLL. |
| `assets/translations.csv` | Every word the mod shows, one row per `$omp_` token, one column per language |
