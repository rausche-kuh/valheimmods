# Adding a mod

A mod is any directory holding `<Name>/<Name>.csproj`, at the repo root or one level down (a
member of a family such as `OdinsMissingPatch/`, which compiles that family's `Common/src/` too);
the scripts discover them that way, so nothing needs registering. Names are unique across the
repo. Create:

```
<Name>/
  <Name>.csproj      AssemblyName + RootNamespace only - the build lives in Directory.Build.props
  src/<Name>.cs      the BaseUnityPlugin, with the GUID/NAME/VERSION consts
  src/Dev/           optional, test helpers compiled into Debug builds only
  assets/            optional, shipped next to the DLL
  package/           what Thunderstore gets: manifest.json, icon.png, README.md, CHANGELOG.md,
                     plus publish.json (the categories per site, not shipped)
  README.md          dev facing, not shipped - what the mod is, how to build it
  CLAUDE.md          optional, the notes an agent needs; keep it short, detail goes in docs/
```

`package/manifest.json` needs `name`, `version_number` (whatever — `package` overwrites it from
`VERSION`), `website_url`, `description` and the BepInEx pack in `dependencies`.
`package/README.md` is the Thunderstore page and `package/CHANGELOG.md` its changelog (see
[docs/releasing.md](releasing.md)); `package` refuses to build a zip without either. Preview
images go in the top level `images/`, not in the mod. Then `./scripts/deploy.sh <Name>`.

A **pack** (a Thunderstore modpack) is a directory with `package/` only — `manifest.json` whose
`dependencies` name the members, `icon.png`, `README.md`, `CHANGELOG.md`, `publish.json` with
the `modpacks` category — and no csproj. Its version is the manifest's `version_number`.
