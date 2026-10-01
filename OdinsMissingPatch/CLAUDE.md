# OdinsMissingPatch

A collection of quality of life changes, each one its own configurable tweak. See the root
`CLAUDE.md` for the shared build, the scripts and the environment.

Twenty-three tweaks are in the source, registered in `Tweaks` in `src/OdinsMissingPatch.cs`;
`docs/tweaks.md` says what each one does, its scope (client, world state, character) and which
doc covers it. `ROADMAP.md` is what comes next and the known bugs.

## Docs

Each area doc holds the conventions that area follows and the game facts they rest on: read it
before changing the area, and put back what a change taught.

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) — next features, the research each needs, bugs |
| changing a source file | [`docs/architecture.md`](docs/architecture.md) — what every file in `src/` and `assets/` holds |
| adding or changing any tweak | [`docs/conventions.md`](docs/conventions.md) — tweak shape, config, multipliers, patch shapes |
| looking up what a tweak does | [`docs/tweaks.md`](docs/tweaks.md) — one entry per tweak, defaults and scope |
| anything reaching into chests | [`docs/chests.md`](docs/chests.md) — the reach, `Claim`, container ZDOs, favourites |
| anything drawn in the inventory screen | [`docs/inventory-ui.md`](docs/inventory-ui.md) — panel buttons and geometry, the sorter |
| any word a player reads | [`docs/translations.md`](docs/translations.md) — tokens, the CSV, what translates itself |
| sorting or classifying items | [`docs/item-order.md`](docs/item-order.md) — `MaterialOrder`, the crafting tree |
| stations, fires, demisters, repairing | [`docs/building-and-world.md`](docs/building-and-world.md) |
| stamina costs and what counts as hostile | [`docs/stamina.md`](docs/stamina.md) |
| who a creature's attack hits, boss spawns, loot | [`docs/creature-hits.md`](docs/creature-hits.md) |
| Rested, comfort and healing | [`docs/comfort-and-healing.md`](docs/comfort-and-healing.md) |
| portals and the death path | [`docs/death-and-portals.md`](docs/death-and-portals.md) |
| the radial menu and guardian powers | [`docs/radial-menu.md`](docs/radial-menu.md) |
| equipping, the hotbar, the equip queue | [`docs/equipping.md`](docs/equipping.md) |
| the trader's shelf and inventory rows | [`docs/trader.md`](docs/trader.md) — the shelf, moving a gate, pocket upgrades |
| the player marks overlay | [`docs/player-marks.md`](docs/player-marks.md) |
| the map, its pins, or map tables | [`docs/map-pins.md`](docs/map-pins.md) — design and pieces; game facts in [`docs/map-facts.md`](docs/map-facts.md) |
| touching ground another mod already covers | [`docs/references.md`](docs/references.md) — the reference checkouts and how this mod differs |

## Source map

| Path | What |
| --- | --- |
| `src/OdinsMissingPatch.cs` | Entry point: binds every tweak's config, then patches; the `Tweaks` list. |
| `src/Tweak.cs` | The base class: section, `Enabled`, `On`, `BindMultiplier`, `BindList`. |
| `src/SharedSettings.cs` | Settings several tweaks read (`General`): `ChestRange`, `KeepHotbar`. |
| `src/Palette.cs` | The colours the mod draws with. |
| `src/Patcher.cs` | Patches the tweaks that are on, class by class; `Serves`, `Always`, `LoadHook`. |
| `src/Tweaks/<Name>.cs` | One tweak, its `[HarmonyPatch]` classes nested inside. |
| `src/NearbyChests.cs` | The chest tweaks' registry, reach, `Claim`, opt-out and its switch. |
| `src/ChestFavorites.cs` | The item kinds a chest is marked to take, on its ZDO; Clear favourites. |
| `src/Stash.cs` | What may leave the backpack, which chest takes it, the move. |
| `src/ChestGlow.cs` | The golden pulse and floating text on a chest. |
| `src/Hotkeys.cs` | `Pressed` / `Held` for a `KeyboardShortcut` through `ZInput`. |
| `src/PanelButtons.cs` | The inventory screen's buttons, registered by owners, and their layout. |
| `src/InventorySorter.cs` | Merge-and-sort of an `Inventory` in place. |
| `src/MaterialOrder.cs` | The crafting tree from `ObjectDB`: family and depth of a material. |
| `src/UniversalPins.cs` | Map pins that belong to nobody: identity, adding, the removed-pin record. |
| `src/PinBroadcast.cs` | Routed RPCs handing auto pins to every player online. |
| `src/PinSweep.cs` | The two-sweep "still there?" check for pins near the player. |
| `src/Translations.cs` | Feeds `assets/translations.csv` to the game's localization. |
| `src/Dev/` | `omp_*` console commands, Debug builds only. |
| `assets/` | Button and map pin icons, `translations.csv`. |

## The rules that always apply

- A tweak is an `internal sealed class : Tweak` with a private constructor and a
  `static readonly Instance`, listed in `Tweaks` in `src/OdinsMissingPatch.cs` — the only
  registration. Its patches are nested in it, so one file holds its settings and their code.
- **Only a tweak that is on gets its patches** (`src/Patcher.cs`), and every patch still asks
  `Instance.On` each time it runs. A patch class outside a tweak needs `[Serves(...)]` or
  `[Always]`, one whose target runs once as an object loads `[LoadHook]` — see "Patching" in
  [`docs/conventions.md`](docs/conventions.md).
- Null-guard everything: `Player.m_localPlayer` is frequently null.
- Nothing the player reads is a literal in the code: a `$omp_` token, words in
  `assets/translations.csv`. Config descriptions stay English — see
  [`docs/translations.md`](docs/translations.md).
