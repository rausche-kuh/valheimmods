# The scripts in detail

Every flag, the paths `setup` searches, and what to do when something goes wrong. The overview is
[scripts/README.md](../scripts/README.md); releasing (`status`, `release`, `bump`, `package`,
`publish`) is [releasing.md](releasing.md).

Every script finds a mod by its name: a directory `<Name>/<Name>.csproj` at the repo root or one
level down (a family member such as `OdinsMissingPatch/OdinsReach/`), or a pack, a directory with
`package/manifest.json` and no csproj. `decompiled/`, `lib/` and `dist/` are never searched.
With no names, a script acts on every mod, then every pack.

## `setup` — find the game, stage the references

```powershell
.\scripts\setup.ps1 [-ValheimDir DIR] [-GaleProfile NAME]
```

```bash
./scripts/setup.sh [--valheim-dir DIR] [--profile NAME]
```

Locates the game and BepInEx, copies the reference assemblies into `lib/`, writes `Valheim.props`,
and restores every mod. Re-run it after a Valheim update — `lib/` is wiped and re-staged.

It searches, in order:

- **Valheim** — the Steam install from the registry on Windows, and on Linux every Steam library it
  can find: `~/.steam/steam`, `~/.steam/root`, `~/.local/share/Steam`, the Flatpak Steam under
  `~/.var/app/com.valvesoftware.Steam/`. Both platforms also read the extra libraries listed as
  `"path"` entries in `steamapps/libraryfolders.vdf`. A directory is the game if it has
  `valheim_Data/Managed/assembly_valheim.dll`.
- **BepInEx** — the game directory first, then every mod manager profile: Gale
  (`%APPDATA%\com.kesomannen.gale\valheim\profiles\`,
  `~/.local/share/com.kesomannen.gale/valheim/profiles/`) and r2modman
  (`%APPDATA%\r2modmanPlus-local\Valheim\profiles\`,
  `~/.config/r2modmanPlus-local/Valheim/profiles/`), Flatpak variants included. The newest
  `BepInEx.dll` across all of them wins.

`Valheim.props` gets a `<ValheimDir>` and a `<ProfileDir>`; `deploy` installs each mod into
`<ProfileDir>/BepInEx/plugins/rauschekuh-<mod>/`. On Windows the profile defaults to Gale's
`Default` and `-GaleProfile` picks another; on Linux it defaults to whichever profile BepInEx was
found in — usually the one you actually play — and `--profile <name>` picks another.

## `deploy` — build and install

```powershell
.\scripts\deploy.ps1 [mod ...] [-Configuration Debug|Release] [-ProfileDir DIR]
```

```bash
./scripts/deploy.sh [-c Debug|Release] [--profile-dir DIR] [mod ...]
```

Builds (Release by default; `Debug` also compiles each mod's `src/Dev/` test helpers) and copies each mod's DLL, everything in its `assets/`, its
`manifest.json` and its `icon.png` into the profile. The profile override is for a one-off install
against a second profile without re-running `setup`.

A pack builds nothing and installs nothing (a mod manager installs it through its members). It
only deletes a `<pack>.dll` left in its plugin folder from before it was a pack: beside the
members, that old DLL would patch everything twice.

## `decompile` — read the game's API

```powershell
.\scripts\decompile.ps1 [-Force] [assembly ...]
```

```bash
./scripts/decompile.sh [--force] [assembly ...]
```

Dumps the game's own C# into `decompiled/<assembly>/` so you can grep for game types. Defaults to
`assembly_valheim` and `assembly_utils`; skips anything already decompiled unless forced. Shared by
every mod.

Installs `ilspycmd` 9.1.0.7988 as a global tool on first run (pinned — 10.x and newer are net10.0
tools and refuse to install on the .NET 8 SDK). It is invoked by its full path, so `~/.dotnet/tools`
does not need to be on your `PATH`; on Linux set `DOTNET_TOOLS_DIR` if yours lives elsewhere.
Remove it again with `dotnet tool uninstall -g ilspycmd`.

Note that `Assembly-CSharp.dll` is a ~23 KB stub — the game's code is in `assembly_valheim`.

## `clean` — undo the rest

```powershell
.\scripts\clean.ps1 [mod ...] [-Deployed] [-All]
```

```bash
./scripts/clean.sh [--deployed] [--all] [mod ...]
```

Removes each mod's `bin/` and `obj/` plus `dist/` (a pack has neither). `-Deployed` / `--deployed` also removes the mods
from the profile they were installed into, and `-All` / `--all` also removes the shared `lib/`,
`decompiled/` and `Valheim.props` — after that, `setup` has to run again before anything builds.

## `icon` — square up a map icon (Linux only)

```bash
./scripts/icon.sh [-s 64] [-t 8] [-o out.png] image.png [image.png ...]
```

Cuts the transparent padding off an icon, scales it so its longer side fills the canvas and centres
it on a transparent `64x64` square, so it touches at least two opposite edges. Map pins are drawn at
one fixed size, so an icon with more padding shows up smaller in game; run every map icon through
this and they match. The input can be any size — a full size render works best, since a small one
gets scaled up. Without `-o` each image is overwritten in place. `-s` changes the output size, `-t`
the alpha (0-255) a pixel needs to count as part of the icon, so a faint glow does not keep padding
alive. Needs ImageMagick 7 (`magick`).

## Where things end up

|                   |                                                                                                                               |
| ----------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Game              | `C:\Program Files (x86)\Steam\steamapps\common\Valheim`, `~/.local/share/Steam/steamapps/common/Valheim`, or another library  |
| Game assemblies   | `<game>/valheim_Data/Managed/`                                                                                                |
| Gale profiles     | `%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\`, `~/.local/share/com.kesomannen.gale/valheim/profiles/<profile>/` |
| r2modman profiles | `%APPDATA%\r2modmanPlus-local\Valheim\profiles\<profile>\`, `~/.config/r2modmanPlus-local/Valheim/profiles/<profile>/`        |
| Installed plugin  | `<profile>/BepInEx/plugins/rauschekuh-<mod>/`                                                                                 |
| BepInEx log       | `<profile>/BepInEx/LogOutput.log`                                                                                             |

`lib/`, `decompiled/`, `dist/`, every mod's `bin/` and `obj/` and `Valheim.props` are generated
and gitignored. Game assemblies must never be committed.

## When it goes wrong

**`Valheim not found`** — the game is on a drive whose Steam library isn't registered, or on Linux
you are on the Proton/Windows build. Point at it directly:

```bash
./scripts/setup.sh --valheim-dir ~/Games/SteamLibrary/steamapps/common/Valheim
```

**`BepInEx not found`** — install the BepInEx pack for Valheim in your mod manager and launch the
game once, so the profile is populated. A freshly created profile is an empty directory until then.

**`No .NET SDK found`** — install the SDK, not just the runtime; `dotnet --list-sdks` has to print a
version.

**`unknown mod '...'`** — the directory name and the `.csproj` name have to match exactly.

**Build errors about missing game types** — `lib/` is stale after a Valheim update. Re-run `setup`.

**The mod doesn't load in game** — check that it landed in the profile you actually launch (`setup`
prints `Profile : …`), and read `<profile>/BepInEx/LogOutput.log`.

**`BadImageFormatException: Method has zero rva`**, with garbled method names in the stack trace,
after a deploy — the game was running when the DLL was copied over. Mono reads method bodies out of
the file lazily, so the running game read the new file at the old offsets. Nothing is wrong with the
build: restart the game. `deploy` warns when it sees Valheim running.
