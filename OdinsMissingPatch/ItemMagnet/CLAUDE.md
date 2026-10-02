# ItemMagnet

Item Magnet: hold a key to pull every item lying around to your feet, out of combat only, and
never one that was moved once. A member of the Odin's Missing Patch family — see
[`../CLAUDE.md`](../CLAUDE.md) for the family rules (tweak shape, `Common/`, translations) and
the root `CLAUDE.md` for the build.

**State:** 0.1.0, split out of Odin's Missing Patch 0.3.2 (where it was still unreleased) with its
section name kept; not released yet. One tweak, `ItemMagnet`, section `Item Magnet`.

## Docs

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) |
| anything the magnet does | [`docs/item-magnet.md`](docs/item-magnet.md) — design, the moved mark, `GPower`, pickup |
| what counts as danger | [`../OdinsEssentials/docs/stamina.md`](../OdinsEssentials/docs/stamina.md) — `Danger`, shared with Combat Stamina |
| the shared code, conventions, words | [`../docs/`](../docs/) — `architecture.md`, `conventions.md`, `translations.md` |

## The tweak

Hold `Hotkey` (Y) to channel the guardian power pose and pull every drop within a growing ring
(`BaseRadius`, `RadiusPerSecond`, `MaxRadius`) to the player's feet, where vanilla auto pickup
takes what fits; it ends by itself once nothing pullable is left within `MaxRadius`. A drop moved
once (pulled, or dropped by a player) carries `omp_moved` and is never pulled again. It only
starts and only lasts out of danger (`Danger`, this mod's own `General` threat settings). No
cooldown, no cost. Scope: world state (drop ZDOs: position and the moved mark, owned first).

## Rules that always apply

- The family's rules hold here unchanged — see [`../CLAUDE.md`](../CLAUDE.md).
- `General` is bound by `Danger.Bind` from the plugin; the tweak opts into `Danger`'s patch with
  `Uses`. Essentials binds the same keys into its own file: two mods, two settings.
- `omp_moved` is saved on drops in the world: never rename it.

## Source map

| Path | What |
| --- | --- |
| `src/ItemMagnet.cs` | The plugin: name, version, the `Tweaks` list, `TweakHost.Start` with `Danger.Bind`. |
| `src/Tweaks/ItemMagnet.cs` | The tweak: hotkey, the pose, the growing ring, the pull. |
| `src/MovedMarker.cs` | The anti-hauling mark on a drop's ZDO: set on pulled and player-dropped items, carried over when the game merges stacks. |
| `src/Dev/MagnetCommands.cs` | Debug only: `omp_magnet_scatter`, `omp_magnet_probe` (loaded drops per ring, for choosing `MaxRadius`), `omp_magnet_unmark`. |
| `assets/translations.csv` | This mod's words: the refusal while enemies are near. |
