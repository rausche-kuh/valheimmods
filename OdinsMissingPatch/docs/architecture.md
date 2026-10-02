# Architecture

What the family shares: the build that compiles `Common/src/` into every member, and each file
there. A member describes its own `src/` and `assets/` in `<Member>/docs/architecture.md`.

## The build

`OdinsMissingPatch/Directory.Build.props` imports the repo's own `Directory.Build.props` first,
then adds `Common/src/**/*.cs` to every project below it (linked as `Common\...`) and sets
`RootNamespace` to `OdinsMissingPatch`. So:

- **Every DLL carries its own copy of Common.** No member depends on another at runtime, and
  `internal` types with the same full name in two loaded members never meet. Statics such as
  `TweakHost.Log` are per member.
- **A change in Common reaches a mod only when that mod is released again.** It never forces a
  release on its own: `scripts/status.sh` lists it as `shared`. When it changes what players see,
  add a changelog note to each member it matters to, and those are released.
- **Common is compiled whole into every member**, used or not. A Common patch class therefore
  must not apply in a member that does not use it: it carries `[Always]` (`Translations`) or
  `[Serves(typeof(Helper))]`, which only applies when a tweak lists the helper in `Uses`.
- **One namespace, `OdinsMissingPatch`**, for Common and every member, so Common needs no `using`
  and a member needs none for Common.
- The pack itself (`OdinsMissingPatch/package/`) has no csproj; the scripts treat a directory with
  `package/manifest.json` and no project as a pack (see `docs/releasing.md` at the repo root).

## Common/src

| Path | What |
| --- | --- |
| `TweakHost.cs` | `Start`, all a plugin's `Awake` calls: binds the shared settings, maps the patch classes, binds every tweak, runs `LegacyConfig`, applies the patches, logs which tweaks are on. Holds the member's `Log` and `Plugin` (for coroutines). |
| `Tweak.cs` | The base class: the section, the `Enabled` switch, `On` (wanted and patched), `Uses` (the Common helpers whose patches it needs), `BindMultiplier`, `BindList`, `OnSettingChanged`. |
| `Patcher.cs` | Applies the patches of the tweaks that are on, class by class; a failed class switches off the tweaks it serves. The `Serves`, `Always` and `LoadHook` attributes; a `Serves` naming a helper type resolves through `Tweak.Uses`, and a class serving no tweak of the member is dropped. |
| `LegacyConfig.cs` | For every section the member's file did not have yet, copies every setting whose section and key `rauschekuh.odinsmissingpatch.cfg` (the all-in-one mod's file) also has. |
| `Translations.cs` | Hands the member's `translations.csv` to the game's localization on every language setup; a member without one is skipped quietly. |
| `Danger.cs` | Whether the local player is in danger - a hostile near, an enemy coming for them, a boss bar. `Bind` puts its three settings in the calling member's `General`; the enraged-report patch serves tweaks that list `Danger` in `Uses`. |
| `Hotkeys.cs` | `Pressed` / `Held` for a `KeyboardShortcut`, read through `ZInput`. |
| `Palette.cs` | The colours the family draws with, one per meaning. |
| `UiAssets.cs` | The PNG icons shipped beside a member's DLL (`icons/` or flattened beside it, as Gale installs them), loaded once each through `ImageConversion` by reflection; the inventory slots' tooltip prefab for a control made without one. |
| `FavoriteMark.cs` | The favourite mark on a stack (`OMP_Favorite` in the item's custom data): set by OdinsReach's quick stack, read by OdinsEssentials' auto shield. |
