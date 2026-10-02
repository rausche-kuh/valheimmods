# The Odin's Missing Patch family

Odin's Missing Patch, split into mods that stand on their own: `OdinsEssentials`, `OdinsReach`,
`OdinsPins`, `ItemMagnet` and `OdinsBeacon`, bundled by the modpack in `package/`, plus
`ThisIsValheim` and `CollateralDamage`, which share the code but not the pack. Each member's `package/README.md` is its
Thunderstore page.

| Path | What |
| --- | --- |
| `Directory.Build.props` | Imports the repo's build and compiles `Common/src/` into every member. |
| `Common/src/` | The tweak framework and shared helpers; each DLL carries its own copy. |
| `<Member>/` | One mod: `src/<Member>.cs` (plugin, `Tweaks` list), `src/Tweaks/`, `src/Dev/`, `assets/`, `package/`, `docs/`. |
| `package/` | The modpack: `manifest.json` (its `dependencies` are the members), page, changelog, icon. |
| `docs/` | Family docs: architecture of Common, conventions, translations, references. |

## Adding a tweak

Drop `<Member>/src/Tweaks/<Name>.cs` holding an `internal sealed class <Name> : Tweak` with a
private constructor and a `static readonly <Name> Instance`, give it a `Section`, a `Summary`, a
`Bind`, and nest its `[HarmonyPatch]` classes inside it. List `<Name>.Instance` in `Tweaks` in
`<Member>/src/<Member>.cs`.

## Build

```bash
./scripts/deploy.sh OdinsReach        # one member, built and installed into your profile
./scripts/deploy.sh                   # every mod in the repo; the pack only clears its old DLL
dotnet build OdinsMissingPatch/OdinsReach/OdinsReach.csproj
```

## Release

```bash
./scripts/status.sh                   # what each member has waiting since its last release
./scripts/release.sh                  # notes for translation changes, bump, commit, publish
```

A change in `Common/` alone never forces a release; it goes out with each member's next one.
The pack needs a release only when members join or leave — mod managers update members by
themselves. Details in [docs/releasing.md](../docs/releasing.md).
