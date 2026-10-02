# Teleporting and death

FastPortals, KeepGearOnDeath.

- A portal trip is `Player.UpdateTeleport(dt)` (owner only, from `FixedUpdate`) counting
  `m_teleportTimer` against three literals: past `2f` the player is moved to the target, past
  `8f` (distant teleports only) *and* once `ZNetScene.IsAreaReady` the floor is looked for and
  the trip ends, past `15f` with no floor found the player is dropped at `GetSolidHeight`. The
  portal trigger itself has no delay. `FastPortals` does not touch the literals: once the loading
  screen is fully black it sets the timer to `8f`, so both gates are passed in one frame and the
  fallback keeps its seven seconds. The black screen is `Hud.m_loadingScreen` (a `CanvasGroup`),
  moved at `dt / Hud.GetFadeDuration(player)`, a literal `1f` unless dead or sleeping - and the
  fade out on arrival calls it with the teleport already over, so nothing on the player says why
  the screen is up; the tweak remembers that itself. `Player.TeleportTo` refuses a new trip while
  `m_teleportCooldown < 2f`, counted from the end of the last one - left alone, it is what keeps
  you from bouncing straight back through the portal you arrived at.
- A dungeon or cave entrance is a `Teleport` (not `TeleportWorld`): `Interact` - also reached
  from its `OnTriggerEnter` - calls `TeleportTo(..., distantTeleport: false)`, then counts the
  `PortalDungeonIn/Out` stat by `InInterior()` and shows `m_enterText` ("Caves" and the like)
  through `ShowBiomeFoundMsg`. Both read the player *before* the move, so the move must not
  happen inside `TeleportTo` or the stat flips. A local trip has no eight second minimum: past
  `2f` it only waits for `IsAreaReady`, and a missing floor sends you straight back with
  `$msg_portal_blocked`. The interior sits ~5000m above the entrance in the same zone;
  `DungeonGenerator` spawns its rooms asynchronously and marks the zone with `SetLoadingInZone`
  meanwhile, which `IsZoneLoaded` (and so `IsAreaReady`) honours. A ready area still does not
  promise a floor: a dungeon whose saved room list names a room `DungeonDB` does not have
  (`Missing room:<hash>` in the game log; seen for `forestcrypt_entrance`, hash -453332780, in a
  DeepNorth world) spawns without it, and when that is the entrance the door blocks every time,
  vanilla included. `InstantDungeonDoors` therefore also runs
  `ZoneSystem.FindFloor` on the target itself - the same raycast the trip ends on - and only
  then jumps the timer to `2f` and returns `float.MaxValue` from `GetFadeDuration`, which keeps
  the screen's alpha at zero until the next `FixedUpdate` ends the trip. A door trip without its
  floor is never hurried, not even by the black screen jump, and logs how long the floor took
  once it appears. The camera needs no help: `GameCamera.UpdateBaseOffset`
  snaps its smoothed player position across any jump over 20m, and `UpdateTeleport` forces the
  instant environment switch itself.
- Death and the inventory is `Player.CreateTombStone()`, called from `Player.OnDeath` (owner
  only) and skipped entirely when the inventory is empty or the world has
  `GlobalKeys.DeathKeepInventory`. It reads three more world modifier keys: unless
  `DeathKeepEquip` (or `DeathDeleteUnequipped`) it calls `Humanoid.UnequipAllItems()`, which
  walks the nine equipment slot fields through `UnequipItem(item, false)`; under
  `DeathDeleteItems` / `DeathDeleteUnequipped` it calls `Inventory.RemoveUnequipped()`; then it
  spawns `m_tombstone` and calls `Inventory.MoveInventoryToGrave(original)` on its container. Both
  `Inventory` methods filter on `!m_questItem && !m_equipped` and have no other caller, so
  "equipped" is the game's whole notion of "stays with you", which is why `KeepGearOnDeath` keeps
  its items equipped (skipping their `UnequipItem` while the local player's `CreateTombStone`
  runs) and folds its type list into both filters. All of that is gated on the world's death
  penalty being the lowest step of the slider, "Casual" - which is exactly `DeathKeepEquip`, the
  only step that keeps equipment, so on the worlds the tweak does run on the game skips its own
  `UnequipAllItems` and never reaches `RemoveUnequipped`: the unequip and delete patches only
  bite on a world that combined those keys by hand. Respawn goes through `Player.Load`, which
  unequips everything and then `EquipInventoryItems()` re-equips whatever has `m_equipped` set -
  so gear that goes through death equipped comes back worn. An empty tombstone destroys itself
  (`TombStone.UpdateDespawn`, not in use and zero items). `ItemType` is
  `ItemDrop.ItemData.ItemType`; `Ammo` is arrows and bolts, `AmmoNonEquipable` the rest,
  `Consumable` covers food and meads alike, `Misc` is coins and the like, `Tool` the hammer, hoe
  and cultivator.
