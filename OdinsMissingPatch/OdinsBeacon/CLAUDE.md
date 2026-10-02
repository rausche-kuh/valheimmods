# OdinsBeacon

Odin's Beacon: a mark over every other player's head, seen through terrain and buildings and held
at the screen's edge when they are off screen. A member of the Odin's Missing Patch family — see
[`../CLAUDE.md`](../CLAUDE.md) for the family rules (tweak shape, `Common/`) and the root
`CLAUDE.md` for the build.

**State:** 0.1.0, split out of Odin's Missing Patch 0.3.2, where it was the Player marks tweak;
not released yet. One tweak, still the class `PlayerMarks` with section `Player Marks`, so old
settings carry over.

## Docs

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) |
| anything about the marks | [`docs/player-marks.md`](docs/player-marks.md) — when they show, size, edge arrows, colours |
| the shared code, conventions | [`../docs/`](../docs/) — `architecture.md`, `conventions.md` |

## The tweak

A small round mark over the head of every other player, drawn on the HUD: shown when they are
hidden behind terrain or a piece or far off, shrinking with distance, and held at the screen's
edge when they are off screen. Scope: client.

## Rules that always apply

- The family's rules hold here unchanged — see [`../CLAUDE.md`](../CLAUDE.md).
- Nothing is networked: the marks are drawn from what the client already knows of other players.
- No words, so no `translations.csv`; a word added later brings one (see `../docs/translations.md`).

## Source map

| Path | What |
| --- | --- |
| `src/OdinsBeacon.cs` | The plugin: name, version, the `Tweaks` list, `TweakHost.Start`. |
| `src/Tweaks/PlayerMarks.cs` | The tweak: finding the players, the HUD marks, occlusion, distance, the screen edge. |
| `src/Dev/MarkWards.cs` | Debug only: wards stand in for other players so the marks can be tried alone; `omp_marks_wards` switches it, `omp_mark` tunes live. |
