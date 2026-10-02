# Architecture

What each file under `src/` and `assets/` of Odin's Reach holds. `CLAUDE.md` has the one-line map;
the area docs hold the conventions and game facts behind each piece. The family's shared code in
`Common/src/` (the tweak framework, `Hotkeys`, `Palette`, `UiAssets`, `FavoriteMark`,
`Translations`) is described in [`../../docs/architecture.md`](../../docs/architecture.md).

| Path | What |
| --- | --- |
| `src/OdinsReach.cs` | BepInEx entry point: the `Tweaks` list, `SharedSettings.Bind`, then `TweakHost.Start`. |
| `src/Tweaks/<Name>.cs` | One tweak, with its `[HarmonyPatch]` classes nested inside it. |
| `src/SharedSettings.cs` | The settings several tweaks read, in the `General` section: `ChestRange`, `KeepHotbar`, `OnKeptHotbar`. |
| `src/NearbyChests.cs` | Shared by the chest tweaks: the registry of loaded containers, the in-reach rule, `Claim`, the "reach" that widens the backpack, the per-chest opt-out flag and its Nearby use switch. |
| `src/ChestFavorites.cs` | The kinds of item a chest is marked to take, on its ZDO: read by QuickStack and ChestButtons, set by an Alt-click in the chest's grid, a yellow amount on a marked slot, listed in the Clear favourites button and its tooltip. |
| `src/ChestGlow.cs` | The golden pulse plus floating text on a chest (`ChestGlow.Flash`). |
| `src/PanelButtons.cs` | The inventory screen's buttons and their layout: icon and text buttons cut from the chest panel's Take all button, registered by their owners, placed once per frame in a column beside each panel or in the vanilla spots, and the switch that hides the game's Take all and Stack all. Icons and the tooltip window come from `UiAssets` (Common). |
| `src/Stash.cs` | Putting the backpack away: what may leave it, which chest takes which kind, the move into a chest; and `TopUp`, filling existing stacks without opening new ones. Shared by QuickStack and ChestButtons. |
| `src/InventorySorter.cs` | Merge-and-sort of an `Inventory` in place, from a given row down, around items a caller keeps. |
| `src/MaterialOrder.cs` | The crafting tree derived from `ObjectDB`: which family a material belongs to and how deep it lies. |
| `assets/icons/` | The button icons, 64px white-on-transparent PNGs, shipped beside the DLL. Gale flattens the folder on install, so `UiAssets.Icon` looks in `icons/` and then beside the DLL. |
| `assets/translations.csv` | Every word the mod shows, one row per `$omp_` token, one column per language — see [`../../docs/translations.md`](../../docs/translations.md). |
