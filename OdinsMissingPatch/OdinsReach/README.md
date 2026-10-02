# OdinsReach

The chests around you count as your own: crafting and refuelling take from them, a key quick
stacks into them, and the inventory screen gets fill, take, place and sort buttons.

A member of the Odin's Missing Patch family: it compiles the family's shared `../Common/src/`
beside its own `src/`. [`package/README.md`](package/README.md) is the Thunderstore page and
describes what the mod does for players.

## Build

```bash
./scripts/setup.sh              # once, and after every Valheim update
./scripts/deploy.sh OdinsReach    # build + install into your profile (-c Debug adds src/Dev/)
./scripts/package.sh OdinsReach   # dist/OdinsReach-<version>.zip
```

`VERSION` in `src/OdinsReach.cs` is the version — `package` stamps it into `package/manifest.json`.
See [scripts/README.md](../../scripts/README.md) for the full workflow on Windows and Linux.

[`CLAUDE.md`](CLAUDE.md) has the source map, the docs index and the rules that always apply.
