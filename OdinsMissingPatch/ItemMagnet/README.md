# ItemMagnet

Hold a key and every item lying around flies to your feet — out of combat only, and never an item
that was moved once.
[`package/README.md`](package/README.md) is the Thunderstore page and describes what the mod
actually does. It is a member of the Odin's Missing Patch family: it also compiles the shared
source in [`../Common/`](../Common/) (see [`../CLAUDE.md`](../CLAUDE.md)).

| Path | What |
| --- | --- |
| `src/ItemMagnet.cs` | The plugin: name, version and its one tweak. |
| `src/Tweaks/ItemMagnet.cs` | The magnet. |
| `src/MovedMarker.cs` | The "moved once" mark on drops. |
| `src/Dev/` | Console commands; Debug builds only (`deploy.sh -c Debug`). |
| `assets/translations.csv` | This mod's words, one column per language. |
| `package/` | What Thunderstore gets: `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`. |

## Build

```bash
./scripts/setup.sh                # once, and after every Valheim update
./scripts/deploy.sh ItemMagnet    # build + install into your profile
./scripts/package.sh ItemMagnet   # dist/ItemMagnet-<version>.zip
```

`VERSION` in `src/ItemMagnet.cs` is the version — `package` stamps it into
`package/manifest.json`. See [scripts/README.md](../../scripts/README.md) for the full workflow
on Windows and Linux.
