# OdinsBeacon

Odin's Beacon: a mark over every other player's head, seen through walls and terrain and held at
the screen's edge when they are off screen.
[`package/README.md`](package/README.md) is the Thunderstore page and describes what the mod
actually does. It is a member of the Odin's Missing Patch family: it also compiles the shared
source in [`../Common/`](../Common/) (see [`../CLAUDE.md`](../CLAUDE.md)).

| Path | What |
| --- | --- |
| `src/OdinsBeacon.cs` | The plugin: name, version and its one tweak. |
| `src/Tweaks/PlayerMarks.cs` | The marks. |
| `src/Dev/` | Wards standing in for players; Debug builds only (`deploy.sh -c Debug`). |
| `package/` | What Thunderstore gets: `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`. |

## Build

```bash
./scripts/setup.sh                 # once, and after every Valheim update
./scripts/deploy.sh OdinsBeacon    # build + install into your profile
./scripts/package.sh OdinsBeacon   # dist/OdinsBeacon-<version>.zip
```

`VERSION` in `src/OdinsBeacon.cs` is the version — `package` stamps it into
`package/manifest.json`. See [scripts/README.md](../../scripts/README.md) for the full workflow
on Windows and Linux.
