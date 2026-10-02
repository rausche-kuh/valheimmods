# OdinsEssentials

Odin's Essentials: the everyday tweaks of the Odin's Missing Patch family — ranges, fuel, rest,
portals, death, repairing, stamina, equipping and the trader. Small changes to how the base game
plays, each one its own switch. See [`../CLAUDE.md`](../CLAUDE.md) for the family rules (tweak
shape, `Common/`, translations) and the root `CLAUDE.md` for the build.

**State:** 0.1.0, split out of Odin's Missing Patch 0.3.2 with its tweaks' section names kept (so
`LegacyConfig` carries old settings over); not released yet. Thirteen tweaks, registered in
`Tweaks` in `src/OdinsEssentials.cs`.

## Docs

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) — next features, the research each needs, bugs |
| looking up what a tweak does | [`docs/tweaks.md`](docs/tweaks.md) — one entry per tweak, defaults and scope |
| stations, fires, demisters, repairing | [`docs/building-and-world.md`](docs/building-and-world.md) |
| Rested, comfort and healing | [`docs/comfort-and-healing.md`](docs/comfort-and-healing.md) |
| portals and the death path | [`docs/death-and-portals.md`](docs/death-and-portals.md) |
| equipping, the hotbar, the equip queue | [`docs/equipping.md`](docs/equipping.md) |
| the radial menu and guardian powers | [`docs/radial-menu.md`](docs/radial-menu.md) |
| the trader's shelf and inventory rows | [`docs/trader.md`](docs/trader.md) — the shelf, moving a gate, pocket upgrades |
| stamina costs and what counts as hostile | [`docs/stamina.md`](docs/stamina.md) — and `Danger`, shared with the Item Magnet |
| the shared code, conventions, words | [`../docs/`](../docs/) — `architecture.md`, `conventions.md`, `translations.md`, `references.md` |

## Rules that always apply

- The family's rules hold here unchanged: one `Tweak` per file, patches nested in it, only a tweak
  that is on is patched and every patch asks `Instance.On`, null-guard everything, no literal
  words — see [`../CLAUDE.md`](../CLAUDE.md).
- `General` (`ThreatRadius`, `EnragedEnemies`, `BossFights`) is bound by `Danger.Bind` from the
  plugin; CombatStamina opts into `Danger`'s patch with `Uses`.
- AutoShield reads the favourite mark through `FavoriteMark.IsSet` (Common) — it is set by Odin's
  Reach's quick stack, so without that mod there are simply no favourites.

## Source map

| Path | What |
| --- | --- |
| `src/OdinsEssentials.cs` | The plugin: name, version, the `Tweaks` list, `TweakHost.Start` with `Danger.Bind`. |
| `src/Tweaks/Ranges.cs` | Station build/craft/repair radius, extension distance, comfort radius, demister circle. |
| `src/Tweaks/EndlessFuel.cs` | Every `Fireplace` kept topped up (owner only, world state). |
| `src/Tweaks/CombatStamina.cs` | Free sprint, jump, swim, build, chop, mine and swings out of danger. |
| `src/Tweaks/Resting.cs` | Instant Rested by a fire; healing by comfort level while resting. |
| `src/Tweaks/FastPortals.cs` | A portal trip ends once the far side is loaded; instant dungeon doors. |
| `src/Tweaks/KeepGearOnDeath.cs` | Chosen item types stay on death, on the Casual death penalty only. |
| `src/Tweaks/AreaRepair.cs` | One hammer repair carries on to every damaged piece in a radius. |
| `src/Tweaks/AutoRepair.cs` | Use on a station repairs all it can; `GroupRepair` across sister stations. |
| `src/Tweaks/PowerPicker.cs` | A Forsaken powers group in the radial menu. |
| `src/Tweaks/EquipWhileRunning.cs` | The equip queue survives a sprint. |
| `src/Tweaks/BuildInWater.cs` | Hammer and pickaxe stay in hand while swimming. |
| `src/Tweaks/AutoShield.cs` | A one handed weapon raises a shield with it. |
| `src/Tweaks/PocketUpgrades.cs` | Which boss gates each of Haldor's extra inventory rows. |
| `assets/translations.csv` | This mod's words: the power picker's group name. |
