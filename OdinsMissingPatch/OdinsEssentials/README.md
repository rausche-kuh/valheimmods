# OdinsEssentials

Odin's Essentials: the everyday tweaks — ranges, fuel, rest, portals, death, repairing, stamina,
equipping and the trader — each one its own switch.
[`package/README.md`](package/README.md) is the Thunderstore page and describes what the mod
actually does. It is a member of the Odin's Missing Patch family: it also compiles the shared
source in [`../Common/`](../Common/) (see [`../CLAUDE.md`](../CLAUDE.md)).

| Path | What |
| --- | --- |
| `src/OdinsEssentials.cs` | The plugin: name, version and the list of tweaks. |
| `src/Tweaks/` | One file per tweak, its Harmony patches nested inside. |
| `assets/translations.csv` | This mod's words, one column per language. |
| `package/` | What Thunderstore gets: `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`. |

## Build

```bash
./scripts/setup.sh                     # once, and after every Valheim update
./scripts/deploy.sh OdinsEssentials    # build + install into your profile
./scripts/package.sh OdinsEssentials   # dist/OdinsEssentials-<version>.zip
```

`VERSION` in `src/OdinsEssentials.cs` is the version — `package` stamps it into
`package/manifest.json`. See [scripts/README.md](../../scripts/README.md) for the full workflow
on Windows and Linux.
