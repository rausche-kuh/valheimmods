# The Odin's Missing Patch family

Mods built on one shared tweak framework. See the root `CLAUDE.md` for the shared build, the
scripts and the environment.

**Odin's Missing Patch** was one all-in-one QOL mod; it is now a Thunderstore **modpack**:
`package/` only, no csproj, its manifest depending on the members below. Each member is a mod of
its own with its own page, config file, version and `CLAUDE.md`. `ThisIsValheim` and
`CollateralDamage` live here because they share `Common/`, but are not in the pack: they change
the game rather than ease it, and collateral damage needs every player to have it.

## Members

| Dir | Thunderstore | What | Tweaks |
| --- | --- | --- | --- |
| `OdinsEssentials/` | Odin's Essentials | Base game tweaks | Ranges, EndlessFuel, CombatStamina, Resting, FastPortals, KeepGearOnDeath, AreaRepair, AutoRepair, PowerPicker, EquipWhileRunning, BuildInWater, AutoShield, PocketUpgrades |
| `OdinsReach/` | Odin's Reach | Nearby chests count as your own; inventory buttons | NearbyCrafting, QuickStack, StationRefill, ChestButtons, InventoryButtons |
| `OdinsPins/` | Odin's Pins | Map pins that make and share themselves | SharedMapTable, AutoPins, PinLooks, DeathPins |
| `ItemMagnet/` | Item Magnet | Hold a key to pull drops to your feet | ItemMagnet |
| `OdinsBeacon/` | Odin's Beacon | A mark over every other player, through walls | PlayerMarks |
| `ThisIsValheim/` | This Is Valheim! | Kicked doors (not in the pack) | DoorKick |
| `CollateralDamage/` | Collateral Damage | Trolls and bosses hitting creatures (not in the pack) | CollateralDamage |

`package/` is the pack's page, changelog and manifest. `ROADMAP.md` holds what has no member yet.

## Docs

| Read before | Doc |
| --- | --- |
| changing anything in `Common/` or the family build | [`docs/architecture.md`](docs/architecture.md) — what Common holds, how it is compiled in |
| adding or changing any tweak | [`docs/conventions.md`](docs/conventions.md) — member shape, config, patching, multipliers |
| any word a player reads | [`docs/translations.md`](docs/translations.md) — tokens, the per-member CSV |
| touching ground another mod already covers | [`docs/references.md`](docs/references.md) — the reference checkouts, per member |
| working on one member | `<Member>/CLAUDE.md` — its docs, sources, rules |

## Common/src

| File | What |
| --- | --- |
| `TweakHost.cs` | `Start`: what a plugin's `Awake` calls - bind, import old settings, patch. The member's `Log`, `Plugin`. |
| `Tweak.cs` | The base class: section, `Enabled`, `On`, `Uses`, `BindMultiplier`, `BindList`, `OnSettingChanged`. |
| `Patcher.cs` | Patches the tweaks that are on, class by class; `Serves`, `Always`, `LoadHook`. |
| `LegacyConfig.cs` | Sections new to a member's file take matching settings from the old all-in-one config file. |
| `Translations.cs` | Feeds the member's `assets/translations.csv` to the game's localization. |
| `Danger.cs` | Whether the local player is in danger; its settings, bound by each member that asks. |
| `Hotkeys.cs` | `Pressed` / `Held` for a `KeyboardShortcut` through `ZInput`. |
| `Palette.cs` | The colours the family draws with. |
| `UiAssets.cs` | Icons shipped beside the DLL; the inventory's tooltip prefab. |
| `FavoriteMark.cs` | The favourite mark on a stack (`OMP_Favorite`), set by Reach, read by Essentials. |

## The rules that always apply

- A tweak is an `internal sealed class : Tweak` with a private constructor and a
  `static readonly Instance`, listed in `Tweaks` in its member's plugin class — the only
  registration. Its patches are nested in it, so one file holds its settings and their code.
- **Only a tweak that is on gets its patches** (`Patcher`), and every patch still asks
  `Instance.On` each time it runs. A patch class outside a tweak needs `[Serves(...)]` or
  `[Always]`, one whose target runs once as an object loads `[LoadHook]`. A patch in `Common/`
  serves a helper, `[Serves(typeof(Danger))]`, and goes in with any tweak listing it in `Uses`
  — see "Patching" in [`docs/conventions.md`](docs/conventions.md).
- **`Common/` never names a member's type and holds no player text.** It is compiled whole into
  every member, so anything it patches must stay inert where unused.
- One namespace, `OdinsMissingPatch`, for Common and every member.
- Null-guard everything: `Player.m_localPlayer` is frequently null.
- Nothing the player reads is a literal in the code: a `$omp_` token, words in the member's own
  `assets/translations.csv`. Config descriptions stay English — see
  [`docs/translations.md`](docs/translations.md).
- **Saved names never change:** the `omp_*` ZDO keys, `OMP_Favorite`, the removed-pin record,
  and every tweak's config `Section` and keys (`LegacyConfig` matches on them).
- **A Common change ships with each member's next release** and forces none (`status.sh` shows
  `shared`). Where players see it, add a changelog note to each member it matters to.
- A member's `package/CHANGELOG.md` gets the note in the same commit as the change; the pack's
  changelog only for changes to the pack itself (members joining or leaving).
