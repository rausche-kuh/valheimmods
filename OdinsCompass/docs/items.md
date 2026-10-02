# The compass items - built

Eight items, not one item with eight qualities: the tiers, their recipes, and how the items are
made without an asset bundle; then the words. The plan is [`../ROADMAP.md`](../ROADMAP.md).

**The player:** a *Compass* is crafted at a workbench from 1 deer trophy and 10 stone. It goes in
the utility slot - the wishbone's, the wisplight's, the belt's - so it competes with those, which
is part of the point: you wear the compass *instead* of the wishbone. Each upgrade is a new
recipe that consumes the compass below it plus the tier's materials, so the crafting tab shows
*Compass (Black Forest)* needing *Compass (Meadows)* + 2 bronze + 2 surtling cores. The tiers,
one per biome:

| Tier | Biome | Default recipe (on top of the tier below) | Why these |
| --- | --- | --- | --- |
| 1 | Meadows | TrophyDeer 1, Stone 10 | the first thing a new player can make |
| 2 | Black Forest | Bronze 2, SurtlingCore 2 | the needle is bronze; cores light it |
| 3 | Swamp | Iron 2, WitheredBone 2 | |
| 4 | Mountain | Silver 2, Crystal 1 | a lens from a stone golem |
| 5 | Plains | BlackMetal 2, Needle 2 | a deathsquito's needle for a needle |
| 6 | Mistlands | Eitr 2, Wisp 1 | the wisplight's own material |
| 7 | Ashlands | FlametalNew 2, CharredCogwheel 1 | a cogwheel exists as an item (`CharredCogwheel.prefab` in the manifest) |
| 8 | Deep North | FrostCore 1 | a placeholder: the item exists (`FrostCore.prefab`), the tier's real material is unknown - **verify** |

Every item in the table exists as a prefab in the manifest (checked 2026-09-24).

Every recipe is a config string (`Tier N <Biome>` / `Recipe`, `Item:amount,...`), so a wrong
guess is fixed in the config, not the code. Boss trophies stay out of the recipes: one drops per
kill and it belongs on the sacrificial stones.

**Why separate items:** a vanilla quality upgrade cannot do this. `Piece.Requirement.GetAmount`
scales one resource list by quality (×1, ×2, ×3, then ×4, ×4.5, ...), so tier materials cannot
differ per level, and `Recipe.GetRequiredStationLevel` is `m_minStationLevel + quality - 1`, so a
quality 8 compass would need a workbench of level 8. Separate items keep every recipe a plain
`Recipe` and the crafting UI untouched; the price is eight prefabs instead of one.

**Making the items without an asset bundle and without Jotunn** (the reference mods that add
items use Jotunn's `ItemManager`; this workspace has no such dependency, so the game's own
`Wishbone` is cloned instead). This is `src/Items.cs`, and it follows the list below with two
adjustments: the clones sit under an inactive `DontDestroyOnLoad` root so their `Awake` (which
would register them as items lying in the world) never runs, and the workbench is taken from the
first vanilla recipe whose station is named `$piece_workbench`, because the main menu's database
has no `ZNetScene` to look the prefab up in. Whether `Instantiate` copies `m_shared` is checked
at runtime and a shared instance is copied by hand, with a warning:

- Postfix `ObjectDB.CopyOtherDB` (the main menu's database, which `FejdStartup` copies from the
  prefab) and `ObjectDB.Awake` (the game scene's), and run once per `ObjectDB` instance: fetch
  `ObjectDB.GetItemPrefab("Wishbone")`, `Object.Instantiate` it under an inactive, hidden
  `DontDestroyOnLoad` root, name the clone `OdinsCompass1`..`OdinsCompass8`. `Instantiate` copies
  the serialized `ItemDrop.m_itemData` by value, so the clone's `m_shared` is its own and the
  wishbone is not touched (**verify** the shared data is not the same instance).
  Seen 2026-09-24: the main menu's database answered `GetItemPrefab("Wishbone")` with null, the
  world's did not (no warning after the world loaded), so the menu copy is treated as optional
  and only noted in the log. **verify** with `compass items wish` from the menu console what the
  menu database holds - if it lacks every wearable, the compass simply cannot exist before a
  world loads, which nothing in the menu needs.
- Set on the clone's `m_shared`: `m_name` (`$oc_compass1`..), `m_description`, `m_icons[0]` (a
  sprite from `assets/icons/`, loaded like the OdinsMissingPatch family's `UiAssets.Icon` through
  `ImageConversion.LoadImage` found by reflection), `m_maxQuality = 1`, `m_equipStatusEffect` =
  the compass status effect (`guidance.md`), `m_itemType` stays `Utility`.
- Add the clone to `ObjectDB.m_items` and call the private `UpdateRegisters()` (publicized), and
  add it to `ZNetScene.m_prefabs` + `m_namedPrefabs` in a postfix on `ZNetScene.Awake` (the
  `Awake` fills `m_namedPrefabs` from `m_prefabs`, so a prefab added before `Awake` runs is enough
  there, added after needs both). A dropped compass is a ZDO with that prefab hash; a player
  without the mod who sees one dropped gets an unknown prefab and the game skips it, no crash
  (**verify** with a second client).
- A `Recipe` is `ScriptableObject.CreateInstance<Recipe>()`: `m_item` = the clone's `ItemDrop`,
  `m_amount = 1`, `m_craftingStation` = `piece_workbench` (found among `ZNetScene` prefabs by
  name, **verify** the name), `m_minStationLevel = 1`, `m_resources` = the config list resolved
  through `ObjectDB.GetItemPrefab` (a name that resolves to nothing is logged and skipped) plus
  the tier below's compass with `m_amount = 1`, `m_recover = false`. Added to
  `ObjectDB.m_recipes`. `m_enabled = true`.
- Looks: the clone wears the wishbone's model on the belt and drops the wishbone's mesh. That is
  acceptable for the first release; a compass of its own is under "Later" in the roadmap. If `Wishbone` turns out to
  have an attach visual that reads wrong, `BeltStrength` or `Demister` are the other utility
  items to clone (`Assets/GameElements/Items/utility/`: `BeltStrength`, `Demister`, `IceShoes`,
  `IceSkates`, `Wishbone`).

## Words - built

`assets/translations.csv` like OdinsMissingPatch's (`key,English,German,...`, fed to
`Localization.SetupLanguage` by a postfix), holding the item names and descriptions
(`$oc_compass1`..`8`, `$oc_compass_desc`), the group names the config defaults use (`$oc_eikthyr`,
`$oc_burialchamber`, ...), the status effect's name and the *Compass: ...* message. A group name
in the config that is not a `$` token is shown as typed, so a player adding a group needs no
translation file.
