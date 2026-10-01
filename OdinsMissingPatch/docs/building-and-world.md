# Building, stations and the world

Ranges (stations, extensions, demisters), EndlessFuel, AreaRepair, AutoRepair.

- `CraftingStation.m_rangeBuild` is an instance field copied off the prefab, and the area marker
  circle plus the station's `m_effectAreaCollider` are both recomputed from it inside the private
  `GetExtensions()`, on a 2s timer. Scale the field and the check, the circle and the collider all
  follow — patching `GetStationBuildRange()` instead would let you build outside the circle drawn.
- What that recompute does *not* touch is `CircleProjector.m_nrOfSegments`, the number of markers
  the ring is laid out with. It is a fixed count from the prefab, so a wider circle is drawn by the
  same markers spread thinner until the ring reads as a dotted line. Scale it with the radius.
- `Ranges` writes the station, extension and demister radii on load (`CraftingStation.Start`,
  `StationExtension.Awake`, `Demister.OnEnable`) and again from `OnSettingChanged` over the game's
  registries (`m_allStations`, `m_allExtensions`, `GetDemisters()`). Each object's vanilla values
  are kept in a `ConditionalWeakTable` the first time it is seen and the field is set to
  `vanilla * scale`, so applying it again is harmless and needs no record of the last scale.
  An extension that fails the game's own `GetZDO() != null` registration check (a placement
  ghost) is left vanilla, since no rescale could reach it again.
- A fire's fuel is a float on its ZDO (`ZDOVars.s_fuel`), burnt down by whoever **owns** the ZDO
  in `Fireplace.UpdateFireplace`, a 2s `InvokeRepeating` tick that then calls the private
  `UpdateState()` to switch the flame and the full/half/empty models. Only the owner's writes are
  synced, so `EndlessFuel` checks `m_nview.IsOwner()` before it tops up — and the top-up sits in a
  postfix, after the burn, so a fire loaded after hours away does not dip through empty for one tick.
  Because it is world state, it needs the mod on whichever client owns the fire: a player without
  it standing closest lets the fire burn normally.
- A demister's radius is its `ParticleSystemForceField.endRange` (`Demister.m_forceField`, found in
  `Awake`): the force field pushes the mist particles out to it, and `ParticleMist` reads the same
  value to decide where mist is emitted around a demister and whether a point is inside one. One
  instance field, so scaling it is the whole change. `Demister.GetDemisters()` is the list of the
  enabled ones (`m_instances`, kept by `OnEnable`/`OnDisable`). Placement ghosts have their
  `Demister` destroyed by `Player.SetupPlacementGhost`, so they never register. The mist is rendered
  per client, so the wider circle is only seen by clients with the mod — the wisplight ball is a
  networked prefab, but its `Demister` runs its own `Awake` on every client. `ParticleMist` emits
  ~4πr² particles around each demister's edge, so the particle count grows with the square of the
  multiplier.
- `Fireplace.m_infiniteFuel` is the game's own eternal flame flag, but it also blanks the hover
  text, refuses fuel and lets the fire burn at 0 fuel showing its empty model — which is why the
  tweak tops the fuel up instead of setting the flag. `m_secPerFuel <= 0` fires do not burn at all.
- Repairing is `Player.Repair(toolItem, repairPiece)` (private, called from `Player.Update` when
  the selected build piece is `m_repairPiece` and attack is pressed): it repairs
  `GetHoveringPiece()` alone, after `CheckCanRemovePiece` (the piece's `m_craftingStation` within
  `CraftingStation.HaveBuildStationInRange` of the *player*, waived by `m_noPlacementCost` /
  `GlobalKeys.NoWorkbench`) and `PrivateArea.CheckAccess`, then spends `GetBuildStamina()`,
  `m_attack.m_attackEitr` and `m_useDurabilityDrain * Game.m_durabilityRate`. `WearNTear.Repair()`
  is the whole write and is idempotent on its own: it returns false at full health and false again
  within `1f` of the last repair (`m_lastRepair`), otherwise it sends `RPC_Repair` — so the health
  is set by whoever owns the piece and the mod is never needed on the other side.
- Repairing an item is the crafting panel's repair button: `InventoryGui.OnRepairPressed` →
  `RepairOneItem()`, which walks `Inventory.GetWornItems` (every item with `m_useDurability`
  below its max) and repairs the **first** one the private `CanRepair(item)` accepts, then
  returns. `CanRepair` is the whole question: the item may be repaired at all
  (`m_canBeReparied`), the player's current station is named by the item's recipe as its
  `m_craftingStation` or `m_repairStation` (or `item.m_worldLevel < Game.m_worldLevel`), and
  the station's level is at least the recipe's `m_minStationLevel`. A repair costs nothing:
  it raises Crafting by the wear it mended, sets `m_durability` to `GetMaxDurability()` and
  plays `CraftingStation.m_repairItemDoneEffects`. `m_canRepair` is not asked there - it is
  what hides the button in `UpdateRepair` - so a tweak that repairs by itself has to ask it.
- `CraftingStation.Interact` is the whole of opening a station: for the local player, after
  `InUseDistance` and `CheckUsable`, it calls `Player.SetCraftingStation(this)` and shows the
  crafting panel. That is the only caller that sets a station, and `Player.UpdateStations`
  clears it again the frame the panel closes or the player walks out of range, so "the local
  player now holds this station" is exactly one station opening.
- `Piece.s_allPieces` is the registry of every loaded piece (private static, publicized), and
  `Piece.s_ghostLayer` is the layer the placement ghost sits on; the game's own radius searches
  (`GetAllPiecesInRadius`, `GetAllComfortPiecesInRadius`) walk the one and skip the other, which is
  what `AreaRepair` repeats. `Player.PlacementCostDisabled` is the public read of
  `m_noPlacementCost`.
- `ZInput.GetKey(KeyCode, logWarning)` is the game's own keyboard read and is null-safe before
  `ZInput` exists; the legacy `UnityEngine.Input` is not what Valheim reads. `JoyAltKeys` (the
  copy-piece modifier at the repair call site) is bound to a gamepad trigger only, so `LeftAlt`
  reaches `Repair` untouched and is free for a modifier of our own.
