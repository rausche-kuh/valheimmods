# Valheim mods

BepInEx 5 / HarmonyX plugins for Valheim. Each mod is a directory with its own `CLAUDE.md`, at the
top level or inside a family; the build, the reference assemblies and the scripts are shared.

**Mods:** `GraveOfTruth/` — dying is embarrassing. `OdinsMissingPatch/` — a family: the modpack,
its members (QOL tweaks, chests, map pins, item magnet, player marks), `ThisIsValheim` and
`CollateralDamage`; see its `CLAUDE.md`. `ImmersiveEntrance/` — dungeon entrances show the dungeon. `OdinsTree/` — a blessed
tree never falls. `OdinsCompass/` — points to the nearest boss, dungeon or ore. `OdinsPaths/` —
roads grow across the world (server side, unreleased).

## Layout

| Path | What |
| --- | --- |
| `<Mod>/` | `<Mod>.csproj`, `src/` (`src/Dev/`: Debug only), `assets/` (shipped beside the DLL), `package/` (Thunderstore), `docs/`, `ROADMAP.md`. |
| `<Family>/` | A `Directory.Build.props` compiling `Common/src/` into each member; maybe a pack (`package/`, no csproj). |
| `Directory.Build.props` | The shared build. Build settings go here, never in a mod's csproj. |
| `scripts/` | Overview in `scripts/README.md`. |
| `docs/` | `environment.md`, `scripts.md`, `releasing.md`, `adding-a-mod.md`. |
| `images/` | Mod page previews, linked by raw GitHub URL. |
| `lib/`, `decompiled/`, `dist/`, `Valheim.props` | Generated, gitignored — never commit game assemblies. |

## Commands

```bash
./scripts/setup.sh       # once, and after every Valheim update
./scripts/deploy.sh      # build + install into the profile (-c Debug for src/Dev/)
./scripts/status.sh      # what each mod has waiting since its last release
./scripts/release.sh     # release everything due: notes, bump, commit, publish
./scripts/decompile.sh   # game source into decompiled/
```

Scripts take mod names, default to every mod then every pack. No solution file:
`dotnet build <path>/<Mod>.csproj` once setup has run.

## Environment facts

More in `docs/environment.md`.

- Unity **6000.0.75f1** (Mono), BepInEx **5.4.x**, `net472`.
- Game code is in **`assembly_valheim.dll`** (publicized): grep `decompiled/assembly_valheim/`.
- Prefabs and UI layout: read the bundles with UnityPy (recipe in `docs/environment.md`).
- Mod manager is **Gale**; mods deploy to `<ProfileDir>/BepInEx/plugins/rauschekuh-<Mod>`.
- `~/Documents/Code/test/othervalheimmods/` holds other people's mods. Read them before
  reinventing a patch; **never copy code out of them**.

## Conventions

- A mod is **any `<Name>/<Name>.csproj` at the top level or one level down**; directory, project
  and assembly name match and are unique. A pack is `package/manifest.json` with no csproj.
- `VERSION` in the plugin source is the mod's version. **Only bump it for an actual Thunderstore
  release, and only with `scripts/bump.sh` or `release.sh`.** A pack's version is its manifest's
  `version_number`. The last release is the commit that set the current version — no tags.
- `package/CHANGELOG.md`: an entry under `## Unreleased`, in the same commit, whenever a change
  alters what a player sees — written for players.
- `package/README.md` is the Thunderstore page and stands alone: no build steps, repo links or
  changelog.
- Patches are small `[HarmonyPatch]` classes nested in the plugin class; null-guard everything
  (`Player.m_localPlayer` is frequently null).

## Keeping the docs small

A `CLAUDE.md` stays under ~6 KB (purpose, state, docs index, rules, one-line source map), a dev
`README.md` under ~3 KB. Mechanics, design and verified game facts go in `docs/`, linked from the
index. Thunderstore pages are exempt.
