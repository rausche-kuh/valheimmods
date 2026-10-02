# Equipping

EquipWhileRunning, BuildInWater, AutoShield.

## EquipWhileRunning

- A hotbar press is `Player.UseHotbarItem` → `Humanoid.UseItem` → `Player.ToggleEquipped`. For
  an item whose `m_shared.m_equipDuration` is `0` (a torch, most tools) that equips on the spot
  through `EquipItem`; for everything with a duration (weapons, shields, armour - the prefab
  default is `1f`) it appends a `MinorActionData` (`Equip` or `Unequip`) to the private
  `Player.m_actionQueue`, and `UpdateActionQueue` (owner only, from `FixedUpdate`) plays the
  `equipping` animation on the upper body layer, counts `m_time` up by `dt` and calls
  `EquipItem` / `UnequipItem` when it passes the duration. A second press on a queued item
  removes it from the queue again. The same queue carries a crossbow's reload (`Reload`), which
  `UpdateWeaponLoading` re-queues on every frame the weapon is unloaded and no reload is queued.
- Nothing in that path asks whether the player is running. The block is `Player.CheckRun`
  (`Character.UpdateWalking` calls it every tick, owner only): after `Character.CheckRun` has
  accepted the run - run held, moving, not crouching, encumbered or dodging - it pays the sprint
  stamina and, when there is still stamina left, calls `ClearActionQueue()` and returns `true`.
  So a sprint wipes the queue on every tick it is actually running; a queued equip never
  accumulates time, and the press only lands once the sprint stops. Out of stamina, the player
  stops sprinting and the queue survives.
- `ClearActionQueue` is `Player`'s override of a protected virtual on `Humanoid`, and its other
  callers are an attack that just started (`Humanoid.StartAttack`), a jump (`Player.OnJump`) and
  a dodge that just fired (`Player.UpdateDodge`). `EquipWhileRunning` therefore does not patch
  the call away but flags the frames a `CheckRun` is on the stack (its prefix and postfix, the
  same shape as `CombatStamina`'s waiver) and has a `ClearActionQueue` prefix drop the wipe under
  the flag, so an attack, jump or dodge still interrupts an equip as in vanilla. `CheckRun` makes
  exactly that one call; check that still holds after a game update.
- The prefix does not skip the wipe outright: it removes the `Reload` entries and keeps the rest.
  Keeping the reload would let a crossbow load while sprinting, since the reload is re-queued
  every frame it is missing - a change to combat rather than to convenience, so the bolt still
  waits for the player to slow down.
- `EquipItem` itself refuses during an attack or a dodge, while swimming off the ground and for a
  broken item, so the equip landing mid sprint asks the same questions as one landing on foot.
  `InMinorAction()` is true while the equipping animation plays, which for the duration blocks
  a swing, a block, a bow draw and the guardian power (`Humanoid.StartAttack`,
  `Humanoid.IsBlocking`, `Player.UpdateAttackBowDraw`, `Player.StartGuardianPower`) but not
  the run itself: `Character.CheckRun` never asks for it.

## BuildInWater

- Swimming takes the hands away in two places, both behind `IsPlayer() && IsSwimming() &&
  !IsOnGround()`: `Humanoid.UpdateEquipment` calls `HideHandItems()` every tick, and
  `Humanoid.EquipItem` refuses to equip anything. Nothing on the way to a swing
  (`Humanoid.StartAttack`, `Attack.Start`) or a placement (`Player.UpdatePlacement`) asks about
  swimming, so a tool that stays in hand works as on land.
- `HideHandItems` has other callers (the hide key, a crafting station, a chair, eating), so the
  tweak does not patch it away but flags the frames `UpdateEquipment` is on the stack, like
  `EquipWhileRunning`. Under the flag only the off hand goes, put into `m_hiddenLeftItem` the way
  vanilla hides it, so the hide key brings a torch or shield back on land.
- `EquipItem`'s check sits in the middle of a dozen refusals, so a transpiler replaces its one
  `IsSwimming` call with `BuildInWater.SwimmingBlocks(this, item)`. If a game update drops that
  call the transpiler throws and `Patcher` switches the tweak off.
- A tool is anything with `m_buildPieces` (the hammer, and the hoe and cultivator for free) or an
  item with `m_skillType == Pickaxes`. Vanilla never re-draws hidden items when the player leaves
  the water; the hide key does.

## AutoShield

- The off hand is decided inside `Humanoid.EquipItem`, not around it. Its `OneHandedWeapon` branch
  unequips `m_leftItem` *unless* it is a `Shield` or a `Torch`, so by the time the weapon has
  landed the hand is either still holding something the player chose to keep, or empty. That is
  the whole condition the tweak needs: `GetLeftItem() == null` after the weapon, and nothing has
  to be guessed beforehand - a bow, which lives in the left hand, is gone by then, and the shield
  fills the hand it left.
- `EquipItem` is reached by far more than a press: `Player.EquipInventoryItems` restores what was
  worn at logout, `Humanoid.ShowHandItems` puts back what `HideHandItems` took away, and
  `InventoryGui`'s drag re-equips what was already equipped after a slot swap. None of those are
  a player drawing a weapon, so the tweak does not trigger off `EquipItem` alone: a `ToggleEquipped`
  prefix records the one handed weapon the press is *for*, and the `EquipItem` postfix only acts on
  that exact item. `ToggleEquipped` is the single entry both a hotbar key
  (`Player.UseHotbarItem` → `Humanoid.UseItem`) and a use in the inventory screen
  (`InventoryGui` → `Humanoid.UseItem`) go through, which is why the record is taken there.
- The same press on an item that is already equipped is an unequip, and on one whose equip is still
  in the action queue it is a cancel (`QueueEquipAction` calls `RemoveEquipAction` when the item is
  already queued). Both are read off before the original runs - `IsItemEquiped`, `IsEquipActionQueued` -
  and neither records anything; a cancel also drops the mark its own first press left, so a weapon
  equip that is called off never lands a shield on its own. The mark is otherwise left alone rather
  than cleared on every press, so a helmet pressed while the sword is still queued does not cost the
  sword its shield.
- The shield is equipped by calling `Player.ToggleEquipped` again rather than `EquipItem` directly,
  so it queues with its own `m_equipDuration` behind the weapon (`UpdateActionQueue` has already
  set the 0.3s `m_actionQueuePause`) and every refusal - mid attack, mid dodge, swimming, broken -
  is the game's own. The re-entry is safe because a shield is not a `OneHandedWeapon`: the
  `ToggleEquipped` prefix records nothing for it and the chain stops there.
- Which shield: favourites (`FavoriteMark.IsSet` in Common: the flag Odin's Reach's quick stack keeps in `m_customData`, read whether or not that mod is installed) before
  the hotbar row (`m_gridPos.y == 0`) before the rest, and the first slot within whichever tier
  wins, reading along each row and then down. Inventory order rather than the best block value, so
  the choice stays the player's - moving a shield to the hotbar or marking it is how you pick.
