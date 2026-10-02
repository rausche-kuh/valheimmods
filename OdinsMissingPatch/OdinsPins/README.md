# OdinsPins

Map pins that make themselves: dungeons, ore and places pinned as you find them and shared with
everyone online, map tables that sync on their own, tidier pin looks and death pins that clean up.

A member of the Odin's Missing Patch family: it compiles the family's shared `../Common/src/`
beside its own `src/`. [`package/README.md`](package/README.md) is the Thunderstore page and
describes what the mod does for players.

## Build

```bash
./scripts/setup.sh              # once, and after every Valheim update
./scripts/deploy.sh OdinsPins    # build + install into your profile (-c Debug adds src/Dev/)
./scripts/package.sh OdinsPins   # dist/OdinsPins-<version>.zip
```

`VERSION` in `src/OdinsPins.cs` is the version — `package` stamps it into `package/manifest.json`.
See [scripts/README.md](../../scripts/README.md) for the full workflow on Windows and Linux.

[`CLAUDE.md`](CLAUDE.md) has the source map, the docs index and the rules that always apply.
