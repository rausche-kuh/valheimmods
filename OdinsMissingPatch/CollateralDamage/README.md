# CollateralDamage

Trolls and bosses hit the creatures in the way of their attacks.
[`package/README.md`](package/README.md) is the Thunderstore page and describes what the mod
actually does. It is a member of the Odin's Missing Patch family: it also compiles the shared
source in [`../Common/`](../Common/) (see [`../CLAUDE.md`](../CLAUDE.md)).

| Path | What |
| --- | --- |
| `src/CollateralDamage.cs` | The plugin: name, version and its one tweak. |
| `src/Tweaks/CollateralDamage.cs` | Trolls and bosses hitting their peers. |
| `src/Dev/` | Collateral test scenes; Debug builds only (`deploy.sh -c Debug`). |
| `package/` | What Thunderstore gets: `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`. |

## Build

```bash
./scripts/setup.sh                      # once, and after every Valheim update
./scripts/deploy.sh CollateralDamage    # build + install into your profile
./scripts/package.sh CollateralDamage   # dist/CollateralDamage-<version>.zip
```

`VERSION` in `src/CollateralDamage.cs` is the version — `package` stamps it into
`package/manifest.json`. See [scripts/README.md](../../scripts/README.md) for the full workflow
on Windows and Linux.
