# The scripts

Every script comes twice, `*.ps1` for Windows PowerShell and `*.sh` for bash, except `status`,
`release`, `publish` and `icon`, which are Linux only. Both sets share `lib.ps1` / `lib.sh` and
the gitignored state they produce (`lib/`, `decompiled/`, `Valheim.props`, `dist/`), so they are
interchangeable on the same checkout.

|             |                                                                                            |
| ----------- | ------------------------------------------------------------------------------------------ |
| `setup`     | Find Valheim + BepInEx, stage the reference assemblies into `lib/`, write `Valheim.props`. |
| `deploy`    | Build and install into your mod manager profile.                                           |
| `status`    | Show what each mod has waiting since its last release, and whether it needs one.           |
| `release`   | Release everything due: write translation notes, bump, commit, publish (Linux only).       |
| `bump`      | Raise a mod's version for a release and close off its changelog.                           |
| `package`   | Build the Thunderstore zips in `dist/`.                                                    |
| `publish`   | Upload every version that is not on Thunderstore and Hexium yet (Linux only).              |
| `decompile` | Dump the game's own C# into `decompiled/` for API lookup.                                  |
| `clean`     | Delete what the others produced.                                                           |
| `icon`      | Trim and square a map icon to 64x64 (Linux only, ImageMagick).                             |

```powershell
.\scripts\setup.ps1      # once, and after every Valheim update
.\scripts\deploy.ps1     # build + install every mod
.\scripts\bump.ps1       # raise a version, rename ## Unreleased
.\scripts\package.ps1    # dist\<Mod>-<version>.zip
.\scripts\decompile.ps1  # game source into decompiled\
.\scripts\clean.ps1      # bin\, obj\, dist\
```

```bash
./scripts/setup.sh
./scripts/deploy.sh      # -c Debug also compiles each mod's src/Dev/ helpers
./scripts/status.sh      # what is waiting since each mod's last release
./scripts/release.sh     # bump, commit and publish everything due
./scripts/bump.sh
./scripts/package.sh
./scripts/publish.sh     # -n for a dry run
./scripts/decompile.sh
./scripts/clean.sh
./scripts/icon.sh <image>
```

Scripts but `bump` take mod names and default to every mod, then every pack:
`./scripts/deploy.sh GraveOfTruth` builds just that one.

**More:** [docs/scripts.md](../docs/scripts.md) — every flag, where things end up, troubleshooting.
[docs/releasing.md](../docs/releasing.md) — `status`, `release`, `bump`, `package`, preview images,
`publish`. [docs/environment.md](../docs/environment.md) — what you need installed.
[docs/adding-a-mod.md](../docs/adding-a-mod.md) — a new mod or pack.
