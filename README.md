# Valheim mods

| Mod                                     | What it does                                                                                                                                 |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| [GraveOfTruth](GraveOfTruth/)           | Dying calls down the obliterator's lightning on your grave and plays a loser jingle out of it, echo, thunderstorm and all.                   |
| [OdinsMissingPatch](OdinsMissingPatch/) | A modpack of quality of life mods on one tweak framework: Odin's Essentials, Odin's Reach (nearby chests), Odin's Pins, Item Magnet, Odin's Beacon (player marks). |
| [ThisIsValheim](OdinsMissingPatch/ThisIsValheim/) | Doors are kicked open like a battering ram hit them. Not in the pack. |
| [CollateralDamage](OdinsMissingPatch/CollateralDamage/) | Trolls and bosses hit the creatures in their way. Not in the pack. |
| [ImmersiveEntrance](ImmersiveEntrance/) | Dungeon entrances are no longer a black wall: the doorway shows the dungeon behind it, torches lit. Proof of concept. |
| [OdinsTree](OdinsTree/)                 | Bless a tree with the hammer and it never falls, so the tree house on it is safe — even without the mod. |
| [OdinsCompass](OdinsCompass/)           | A compass worn like the wishbone: blue light blows toward the nearest boss, and each upgrade teaches it a biome's dungeons and ore. |
| [OdinsPaths](OdinsPaths/)               | Read a vegvisir, sleep, and a path winds from home to the boss altar along the easiest ground. Server side, unreleased. |

## Quick start

```bash
./scripts/setup.sh    # find Valheim + BepInEx, stage reference assemblies into lib/
./scripts/deploy.sh   # build every mod and install it into your profile
```

`scripts\setup.ps1` and `scripts\deploy.ps1` are the same thing on Windows.
[scripts/README.md](scripts/README.md) is the overview of the scripts and how to add a new mod;
[docs/](docs/) has every flag, troubleshooting, releasing, and the environment in detail.

## Layout

```
<Mod>/                 one mod: <Mod>.csproj, src/, assets/, package/ (Thunderstore), docs/, ROADMAP.md
OdinsMissingPatch/     a family: package/ is the modpack, Common/src/ the shared code, a member per subfolder
scripts/               setup, deploy, bump, package, decompile, clean (.sh and .ps1); status, release, publish, icon (.sh)
docs/                  repo wide docs: environment, scripts in detail, releasing
images/                preview images for the mod pages (not zipped)
Directory.Build.props  the build every mod shares
lib/                   game + BepInEx reference assemblies   (generated, gitignored)
decompiled/            the game's own C#, for API lookup      (generated, gitignored)
dist/                  Thunderstore zips                      (generated, gitignored)
```
