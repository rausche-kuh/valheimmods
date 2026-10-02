# Environment

The game, the build and the tools around it, in the detail the root `CLAUDE.md` leaves out.

## What you need

|                      |                                                                                                                                            |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| .NET SDK 8           | `winget install Microsoft.DotNet.SDK.8`, `sudo pacman -S dotnet-sdk`, `sudo apt install dotnet-sdk-8.0`, `sudo dnf install dotnet-sdk-8.0` |
| Valheim              | Installed through Steam. On Linux, the native build.                                                                                       |
| BepInEx              | Installed into a profile by a mod manager — [Gale](https://github.com/Kesomannen/gale) or r2modman.                                        |
| binutils             | Linux only, for `strings`; used just to read the Unity and BepInEx versions. Optional.                                                     |
| `python3` or `zip`   | Linux only, for `package.sh`.                                                                                                              |
| `python3` and `curl` | Linux only, for `publish.sh`.                                                                                                              |
| ImageMagick 7        | Linux only, for `icon.sh`.                                                                                                                 |

No Mono, no Wine on Linux: the game assemblies are `net472` references and the NuGet package
`Microsoft.NETFramework.ReferenceAssemblies` supplies the framework, so the .NET 8 SDK builds the
plugins on its own.

## The game

- Valheim runs on **Unity 6000.0.75f1** (Mono), BepInEx **5.4.x**.
- Game code lives in **`assembly_valheim.dll`** — `Assembly-CSharp.dll` is a ~23 KB stub, so
  grep `decompiled/assembly_valheim/` for game types (`TombStone`, `Player`, `EnvMan`, `ZSFX`, ...).

## Reading prefabs and UI layout without running the game

Scenes and prefabs are not in `valheim_Data/*.assets` but in the addressable bundles under
`valheim_Data/StreamingAssets/SoftRef/Bundles/<id>`; `SoftRef/manifest_extended` maps asset
paths to bundle ids (`Assets/Systems/_GameMain.prefab`, which holds the whole HUD and the
inventory screen, was in `d59cfac` as of 2026-09-22 - the ids can change with an update).

To read UI layout out of one, `uv venv` + `uv pip install UnityPy` in a scratch directory,
`UnityPy.load(bundle)`, find the `GameObject` by name and walk its `RectTransform` children
printing `m_AnchoredPosition`, `m_SizeDelta`, `m_AnchorMin/Max` and `m_Pivot`. That is how the
positions of the inventory screen's panels and readout boxes in
`OdinsMissingPatch/OdinsReach/docs/inventory-ui.md` were measured.

## The build

- References come from `lib/`, staged out of `valheim_Data/Managed` plus a BepInEx `core` folder.
  The only NuGet packages are the net472 reference assemblies and `BepInEx.AssemblyPublicizer.MSBuild`,
  which publicizes `assembly_valheim` / `assembly_utils` so private game members are reachable
  (e.g. `TombStone.IsOwner()` and `TombStone.m_nview` are private in the current build).
- The build is cross-platform: the .NET 8 SDK plus the `net472` reference assemblies are enough on
  Linux, no Mono or Wine. Backslash paths in the MSBuild files are normalised by MSBuild, so leave
  them alone.
- Target framework is `net472` (matches previously shipped builds).
- Build settings live in `Directory.Build.props`, never in a mod's csproj. Its item globs resolve
  against each mod directory; shared paths use `$(MSBuildThisFileDirectory)`.
- `EnableDefaultCompileItems` is off and `Compile` is globbed from `src/` only — otherwise the
  SDK would try to compile everything under `decompiled/`.
- `ilspycmd` is pinned to `9.1.0.7988`: 10.x and newer ship as net10.0 tools and refuse to
  install on the .NET 8 SDK.

## The mod manager

Mod manager is **Gale** (`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>` on Windows,
`~/.local/share/com.kesomannen.gale/valheim/profiles/<profile>` on Linux). `setup.ps1` defaults to
the `Default` profile (`-GaleProfile` to override), `setup.sh` defaults to whichever profile
BepInEx was found in (`--profile` to override). The profile path lands in `Valheim.props` as
`<ProfileDir>`; each mod deploys to `<ProfileDir>/BepInEx/plugins/rauschekuh-<Mod>`.

## Other people's mods

`~/Documents/Code/test/othervalheimmods/` holds reference checkouts of other people's mods, kept
purely to see how a working mod solves something:

| Checkout | License |
| --- | --- |
| `ValheimMods` (Crystal Ferrai's collection, 19 shipped mods) | Apache-2.0 |
| `VentureValheim` (OrianaVenture's collection) | MIT |
| `Digitalroot.Valheim.EternalFire` | AGPL-3.0 |
| `cartur-safe-stamina`, `SmartCraft-Storage` | none, so all rights reserved |

Read them before reinventing a patch; never copy code out of them — Apache and MIT both need the
license notice, AGPL would bind the mod, and an unlicensed one allows nothing at all.
