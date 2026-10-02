# Chests

`src/NearbyChests.cs`, `src/ChestFavorites.cs`, `src/ChestGlow.cs`, `src/Stash.cs` and the tweaks
that reach into chests (NearbyCrafting, StationRefill, QuickStack, ChestButtons).

## Conventions

- A tweak that lets a game action reach into chests (`NearbyCrafting`, `StationRefill`) does not
  patch the action: it opens `NearbyChests.EnterReach(tweak, user)` in a prefix on the action and closes
  it in a **finalizer** (`__state` says whether it opened), and the three `Inventory` methods the
  action goes through (`CountItems`, `HaveItem(string)`, `RemoveItem(string, ...)`) are widened
  in one place while a reach is open, for the local player's backpack only. The backpack pays
  first: the remove prefix lowers `amount` to what is carried and takes the rest from chests. A
  finalizer rather than a postfix because Harmony skips postfixes when the original throws, and
  a reach left open would make every later backpack read see the chests. Nothing outside an
  opened reach touches a chest, so "which actions" is the whole design decision of such a tweak,
  and each is named in the tweak. Reaches nest (a `using NearbyChests.Scope` inside an action
  that already opened one); the chests count while any is open, always within
  `General.ChestRange`, the one range every chest tweak shares.
- Shared code that is not a tweak (`NearbyChests`) may hold patches of its own when the thing
  they serve belongs to no single tweak (the registry, the opt-out button); they gate on
  `NearbyChests.AnyTweakOn` rather than on one tweak. State that outlives a tweak's switch and
  belongs to the chest rather than to the player (the opt-out flag, `ChestFavorites`) lives with
  it, and each such flag has its own gate, `Patcher.AnyServedOn` of the patch class whose
  `[Serves]` names the tweaks that read it (`AnyTweakOn` is `Register`'s, `ChestFavorites.Used`
  is `ShowMarks'`), so the tweaks are listed once and a player who runs only one of them can
  still set it.
- Putting the backpack away (QuickStack, Place all, Fill the chest) goes through `Stash`: what
  may leave (`MayLeave`: not equipped, not a favourite, not on the hotbar while
  `General.KeepHotbar` is on), which chest takes which kind (`Takes`: holds one, or is marked for
  it) and the move itself (`Move`, the `AddItem` dance below, returning the units that went).
- There are two favourites and they never mix: QuickStack's is a flag on one stack and only in
  the backpack, `ChestFavorites` is a list of item *kinds* on one chest. The same Alt-click sets
  both - which one depends on the grid clicked - and they are drawn differently so they never
  look alike: the backpack's stack gets the golden border, a slot of a chest's marked kind gets its
  amount in yellow (`ChestFavorites.ShowMarks`). The list can name a kind the chest holds none of,
  which has no slot, so only the Clear favourites button's tooltip shows the marks whole.
- Every chest write goes through `NearbyChests.Claim`: re-checks the in-reach rule (someone may
  have opened the chest since it was found), reloads the inventory from the ZDO, then claims
  ownership. `Find` hands out one reused list, so copy it before a loop that writes. The one
  exception is the chest open in the panel (`ChestButtons`): the panel only shows while the
  local client owns it, so its buttons write straight to it, as the game's own two do.

## Game facts

- A `Container`'s inventory is a `ZDO` byte blob (`s_items`), written by `Container.Save` only
  when the local client **owns** the ZDO, and read back by `Load` in a 1s `CheckForChanges` tick
  whenever the data revision moved and the chest is not in use. Opening a chest is an
  `RPC_RequestOpen` to the owner, who refuses if it is in use, else hands the ZDO over
  (`SetOwner`) and answers `RPC_OpenResponse`; "in use" is `m_inUse` on the owner and `s_inUse`
  on the ZDO for everyone else, and a cart's hold also asks `Vagon.InUse()`. So a write from afar
  is: reload, `ClaimOwnership`, edit the `Inventory` (its `m_onChanged` calls `Save`). There is no
  lock; the in-use check is the only courtesy, which is why it is re-asked right before a write.
  `CheckAccess(playerId)` (private) is the privacy setting and dereferences `m_piece`, null on a
  container whose `Piece` sits on a parent (ship hold); the ward check is
  `m_checkGuardStone && !PrivateArea.CheckAccess(pos, 0, flash: false)`, as in `Interact`.
- "Placed by a player" is `Piece.IsPlacedByPlayer()` = `s_creator != 0`. A ruin's chest is the
  same prefab with creator 0; loot chests have no `Piece`. Tombstones are a `Container` with a
  `TombStone` beside it. No static list of containers exists; `Container.Awake` (only when the
  object has a ZDO, so never a prefab or a placement ghost) is the place to register one.
- `Inventory.AddItem(ItemData)` (what `StackAll` and `MoveItemToThis` use) is a merge-then-place:
  it bumps the chest's own stacks one unit at a time and, for the remainder, moves the very
  `ItemData` object into a free slot. **True** means everything went and the caller must remove
  the object from the source (the unit count on it was never decremented when it all merged);
  **false** means the merged part was subtracted from `item.m_stack`, the object is still the
  source's, and an error line was logged — hence `HaveEmptySlot() || FindFreeStackSpace() > 0`
  first. `FindFreeStackItem` matches name, quality, world level and cheated flag, not
  `m_customData`, so a flagged stack absorbs unflagged units and keeps its flag. The mod's own
  merges (`InventorySorter.Merges`) are stricter and match variant and custom data too.
- A chest's favourites are one ZDO string (`OMP_ChestFavorites`) holding the shared names
  (`$item_wood`, the localization token) joined by newlines with one at each end, so a name is
  always matched between two separators and Wood never matches inside WoodArrow. It is read on
  every grid refresh and once per item per chest in a quick stack, so `Marks` hands the raw
  string out once and `Marked` searches it without splitting or concatenating; only the rare
  writes and the tooltip's list pay for a `Split`. A mark means "treat this chest as holding
  that item": it never adds a slot, so the `HaveEmptySlot() || FindFreeStackSpace() > 0` check
  in front of `AddItem` still decides whether anything actually fits.
- Food and meads are both `ItemType.Consumable` (food has `m_food > 0`, a mead a
  `m_consumeStatusEffect`): `Humanoid.UseItem` sends only that type to `ConsumeItem`. Arrows,
  bolts and missiles are `Ammo`; `AmmoNonEquipable` is ammo that cannot be equipped itself.
  `Inventory.GetAmmoItem` searches all three by `m_ammoType`.
- `ItemData.m_customData` is a string dictionary saved with the item (inventory blob, character
  file, dropped item) and copied by `Clone()`, so it is the place for a per-stack flag
  (`QuickStack`'s favourite); splitting a stack copies the flag.
- To keep such a flag inside the player's backpack, the one choke point is the private
  `Inventory.Changed(bool, bool)`: every add, every `MoveItemToThis`, `MoveAll`, `Load` and
  `RemoveAll` ends there, and a container saves from its `m_onChanged`, so a prefix that strips
  the flag from any inventory that is not `Player.m_localPlayer.GetInventory()` catches every
  route in and runs before the save. It has to bow out while `m_localPlayer` is null, since
  `Game.SpawnPlayer` only sets the local player before `LoadPlayerData`, and a null there would
  mean wiping the flags out of the character file as it loads. The ground is the exception:
  `Humanoid.DropItem` hands a `Clone()` to the static `ItemDrop.DropItem`, which saves it to its
  own ZDO, so that one needs a postfix that clears the flag on `__result.m_itemData` and calls
  `Save()` again.
- The requirement checks, and where each spends: `Player.HaveRequirementItems` (private; counts
  per quality level 1..max and takes the best) behind `HaveRequirements(Recipe, ...)`, which with
  `discover: true` reads `m_knownMaterial` and no inventory; `HaveRequirements(Piece, mode)` for
  the build menu, the ghost and `Hud`; `ConsumeResources` for a craft, an upgrade and
  `PlacePiece`; `InventoryGui.SetupRequirement` (static, takes the `Player`) for every ingredient
  row of the crafting panel and the piece info, per frame; it shows only the needed amount
  (`res_amount`, a `TMP_Text`, reachable as `Graphic` for its colour) in white, or blinking red
  when the count falls short, and sets the row's `UITooltip.m_text` to the item name (the HUD
  rows have one too, but no cursor to hover them). `res_amount` is set in `prstartk`, a pixel
  font whose every character is one em wide, in a 47.5x20 box (15pt) on a 64px slot: three
  characters fit. Writing carried and chest counts into it (`3(12)` over the need) was tried on
  2026-09-24 and dropped: at a readable size it crowds the slot and reads as the cost itself, so
  the split stays in the tooltip and the amount only turns yellow. Harmony's `__state` may be a
  struct, which is how `NearbyCrafting.RowScope` carries the carried count taken before the
  reach opens into its postfix. A `m_requireOnlyOneIngredient` recipe
  additionally looks the ingredient up in the backpack (`GetFirstRequiredItem`), so a widened
  count there promises a craft the lookup then fails; `NearbyCrafting` leaves those recipes alone.
  `InventoryGui.UpdateRecipeList` counts every ingredient of every recipe per quality level in one
  frame, hence the per-frame chest count cache.
- Manual refuelling is one unit per Use: `Fireplace.Interact`, `Smelter.OnAddFuel`,
  `CookingStation.OnAddFuelSwitch`, `ShieldGenerator.OnAddFuel` (a list of fuels), each a
  `HaveItem(name)` then `RemoveItem(name, 1)` on the user's inventory and an `RPC_AddFuel`; the
  game refuses once `fuel > maxFuel - 1`. `Fireplace.UseItem` is the hotbar drop and takes the
  item it is given. A kiln is a `Smelter` with no `m_fuelItem` (its wood is the ore).
- The add RPCs run on the station's owner and, for `Smelter`, `CookingStation` and
  `ShieldGenerator`, do not clamp (`SetFuel(fuel + 1)`; `RPC_AddOre` and `RPC_AddItem` do check
  the queue and the free slot), so a non-owner that sends more than the room it saw overfills;
  `Fireplace.RPC_AddFuelAmount(float)` clamps to the cap. `ZRoutedRpc` handles an RPC to
  oneself synchronously, so on the owner the ZDO has moved by the time `InvokeRPC` returns.
- `Switch` is the Use target of a smelter's ore and fuel hoppers, an oven's fuel and food, a
  shield generator's fuel: `Switch.Interact` ignores `alt` and calls `m_onUse(this, user, null)`;
  the owning component sits on a parent (`GetComponentInParent`), and the switch is told apart
  by reference (`m_addWoodSwitch`, `m_addOreSwitch`, `m_addFuelSwitch`, `m_addFoodSwitch`).
  `Player.Update` passes `alt` as `AltPlace` (Shift) held, or `JoyAltKeys` on a non-classic
  gamepad layout; hover text writes it as `$KEY_AltPlace + $KEY_Use` (`ItemStand`, `Sadle`,
  `Tameable`). `Fireplace.Interact` with `alt` refuels a fire that could otherwise be toggled.
- `MaterialMan.instance.SetValue(go, ShaderProps._EmissionColor / _Color, color)` and
  `ResetValue` tint every renderer under `go` through a property block, which is how
  `WearNTear.Highlight` flashes a piece; `ShaderProps` is in `assembly_utils`. A ship's hold has
  `m_rootObjectOverride`, so the ship is what glows.
- `DamageText.AddInworldText(type, pos, distance, text, mySelf)` (private) is the floating combat
  text without the RPC that `ShowText` sends to everyone; `TextType.Bonus` is the large orange one
  that lingers 3s.
