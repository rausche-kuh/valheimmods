# OdinsReach

The chests around you count as your own: crafting, building and refuelling take from them
(Nearby crafting, Station refill), a key stacks the backpack into them (Quick stack, favourites),
and the inventory screen gets buttons to fill, take, place and sort (Chest buttons, Inventory
buttons). A member of the Odin's Missing Patch family.

**State:** 0.1.0, split out of Odin's Missing Patch 0.3.2 with its tweaks, sections and saved
keys unchanged; not released yet.

See [`../CLAUDE.md`](../CLAUDE.md) for the family rules (Common, the tweak framework,
translations) and the root `CLAUDE.md` for the build. `docs/tweaks.md` says what each tweak does,
its scope and which doc covers it; `ROADMAP.md` is what comes next and the known bugs.

## Docs

Each area doc holds the conventions that area follows and the game facts they rest on: read it
before changing the area, and put back what a change taught.

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) — next features, bugs |
| changing a source file | [`docs/architecture.md`](docs/architecture.md) — what every file in `src/` and `assets/` holds |
| looking up what a tweak does | [`docs/tweaks.md`](docs/tweaks.md) — one entry per tweak, defaults and scope |
| anything reaching into chests | [`docs/chests.md`](docs/chests.md) — the reach, `Claim`, container ZDOs, favourites |
| anything drawn in the inventory screen | [`docs/inventory-ui.md`](docs/inventory-ui.md) — panel buttons and geometry, the sorter |
| sorting or classifying items | [`docs/item-order.md`](docs/item-order.md) — `MaterialOrder`, the crafting tree |
| adding or changing any tweak | [`../docs/conventions.md`](../docs/conventions.md) — tweak shape, config, patch shapes |
| any word a player reads | [`../docs/translations.md`](../docs/translations.md) — tokens, the CSV |
| touching ground another mod already covers | [`../docs/references.md`](../docs/references.md) |

## Source map

| Path | What |
| --- | --- |
| `src/OdinsReach.cs` | Entry point: the `Tweaks` list, `SharedSettings.Bind`, `TweakHost.Start`. |
| `src/SharedSettings.cs` | Settings several tweaks read (`General`): `ChestRange`, `KeepHotbar`. |
| `src/Tweaks/<Name>.cs` | One tweak, its `[HarmonyPatch]` classes nested inside. |
| `src/NearbyChests.cs` | The chest tweaks' registry, reach, `Claim`, opt-out and its switch. |
| `src/ChestFavorites.cs` | The item kinds a chest is marked to take, on its ZDO; Clear favourites. |
| `src/Stash.cs` | What may leave the backpack, which chest takes it, the move; `TopUp`. |
| `src/ChestGlow.cs` | The golden pulse and floating text on a chest. |
| `src/PanelButtons.cs` | The inventory screen's buttons, registered by owners, and their layout. |
| `src/InventorySorter.cs` | Merge-and-sort of an `Inventory` in place. |
| `src/MaterialOrder.cs` | The crafting tree from `ObjectDB`: family and depth of a material. |
| `assets/` | Button icons, `translations.csv`. |

## The rules that always apply

- **Every chest write goes through `NearbyChests.Claim`**, which re-checks the reach and takes
  ownership first; never edit a container's inventory without it.
- A tweak that reads chests opens `NearbyChests.EnterReach` around the game action it widens,
  never a global switch — see [`docs/chests.md`](docs/chests.md).
- **Only a chest a player placed is ever used from afar**, and only while its Nearby use button
  is on; found chests and graves never.
- The two favourites never mix: a stack's mark (`FavoriteMark.Key` in Common, set by QuickStack)
  lives only in the backpack; a chest's marked kinds (`ChestFavorites`) live on the chest's ZDO.
  Both keys are saved, so they never change.
- `PanelButtons` owns the inventory screen's layout: a tweak hands its buttons over once and
  never moves another's — see [`docs/inventory-ui.md`](docs/inventory-ui.md).
- Whole-backpack actions leave equipped items, favourites and the hotbar (`KeepHotbar`) alone.
