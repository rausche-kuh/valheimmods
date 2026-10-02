# OdinsPins

Map pins that make themselves: dungeons, struck ore and places are pinned as you find them and
shared with everyone online (Auto pins), map tables sync on their own (Shared map table), those
pins get biome colours, icons and toggles (Pin looks), and a death pin goes with its grave (Death
pins). A member of the Odin's Missing Patch family.

**State:** 0.1.0, split out of Odin's Missing Patch 0.3.2 with its tweaks, sections and saved
keys unchanged; not released yet.

See [`../CLAUDE.md`](../CLAUDE.md) for the family rules (Common, the tweak framework,
translations) and the root `CLAUDE.md` for the build. `docs/tweaks.md` says what each tweak does,
its scope and which doc covers it; `ROADMAP.md` is what comes next and the known bugs.

## Docs

Each area doc holds the conventions that area follows and the game facts they rest on: read it
before changing the area, and put back what a change taught.

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) — next features, bugs |
| changing a source file | [`docs/architecture.md`](docs/architecture.md) — what every file in `src/` and `assets/` holds |
| looking up what a tweak does | [`docs/tweaks.md`](docs/tweaks.md) — one entry per tweak, defaults and scope |
| the map, its pins, or map tables | [`docs/map-pins.md`](docs/map-pins.md) — design and pieces |
| any game fact about pins, tables, locations, graves | [`docs/map-facts.md`](docs/map-facts.md) |
| adding or changing any tweak | [`../docs/conventions.md`](../docs/conventions.md) — tweak shape, config, patch shapes |
| any word a player reads | [`../docs/translations.md`](../docs/translations.md) — tokens, the CSV |
| touching ground another mod already covers | [`../docs/references.md`](../docs/references.md) — "Map tables and auto pins" |

## Source map

| Path | What |
| --- | --- |
| `src/OdinsPins.cs` | Entry point: the `Tweaks` list, `TweakHost.Start`. |
| `src/Tweaks/<Name>.cs` | One tweak, its `[HarmonyPatch]` classes nested inside. |
| `src/UniversalPins.cs` | Map pins that belong to nobody: identity, adding, the removed-pin record. |
| `src/PinBroadcast.cs` | Routed RPCs handing auto pins to every player online. |
| `src/PinSweep.cs` | The two-sweep "still there?" check for pins near the player. |
| `src/Dev/` | `omp_*` map console commands, Debug builds only. |
| `assets/` | Map pin icons, `translations.csv`. |

## The rules that always apply

- **A universal pin's identity never changes:** the fixed `UniversalPins.Owner` and the author
  `OdinsMissingPatch_<category>` are on every player's saved map and every table; renaming
  either orphans them. The mod's rename did not touch them, and nothing may.
- Everything speaks the game's own table format and ordinary pins, so a player without the mod
  still gets the pins from a table; no new pin types, no server component.
- A table is never written in answer to someone else's write (that looped) and only when it
  actually lacks something — see [`docs/map-pins.md`](docs/map-pins.md).
- Every game fact goes in [`docs/map-facts.md`](docs/map-facts.md), checked against
  `decompiled/assembly_valheim/`, before code relies on it.
