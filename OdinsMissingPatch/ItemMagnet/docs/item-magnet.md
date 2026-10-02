# The item magnet

ItemMagnet, with `MovedMarker` (the anti-hauling mark) and `Danger` (`Common/src/Danger.cs`,
shared with Odin's Essentials' CombatStamina, see
[stamina](../../OdinsEssentials/docs/stamina.md); its settings are bound into this mod's own
`General` section).

## Design

- **Hold to channel.** Pressing the key starts the Forsaken power pose; holding it keeps the
  pose, and the reach grows from `BaseRadius` at one second by `RadiusPerSecond` per further
  second up to `MaxRadius`. Letting go, danger arriving, a stagger, a knockback, water, a menu
  or death ends it; items already in flight still land. It also ends by itself once the pose is
  held and nothing pullable is left within `MaxRadius` - every item has landed, none is outside
  the ring or still waiting for its owner to hand it over - so with nothing in reach it is just
  the pose.
- **No cooldown, no stamina.** Two rules carry the balance instead:
  - *An item moves once.* Everything the magnet captures, and every drop a player makes, gets
    the ZDO bool `omp_moved`; marked items are never pulled. A pulled pile cannot be pulled
    further on, and picking it up and dropping it again marks the new drop. That makes the magnet
    useless for hauling without limiting the cleanup.
  - *Only out of danger.* `Danger.Near` (hostile within `General.ThreatRadius`, an enemy coming
    for you, a boss bar) refuses a start and ends a channel, with `$omp_magnet_danger`.
- **Everything lands at your feet.** The magnet moves items itself and does not care whether
  they fit: what the backpack takes is then picked up by the game's own auto pickup, the rest is
  a pile at the player's feet. Auto pickup switched off leaves the whole pile there.
- What is pulled is what vanilla auto pickup would consider: `m_autoPickup`, not `IsPiece()`,
  not `InTar()`, not a unique item the player already has - minus everything marked.

## Game facts

- **The pose.** `m_zanim.SetTrigger("gpower")` is the guardian power animation
  (`Player.StartGuardianPower`). At its peak the clip fires the animation event
  `CharacterAnimEvent.GPower`, which calls `Player.ActivateGuardianPower()` - so playing the pose
  casts the player's real power and starts its cooldown unless that event is swallowed. Remote
  copies replay the trigger and the event too, but `m_guardianSE` is only set on the local
  player, so the call does nothing there. The magnet swallows the first `GPower` after its own
  trigger (with a timeout, in case the clip is cut short) and holds the pose there.
- **Holding the pose.** `ZSyncAnimation.SetSpeed` writes the animator speed to the ZDO, so a
  held pose shows on every client. `Character.RPC_Stagger` resets it to 1 itself; every other
  way out goes through `ItemMagnet.End`, which does the same.
- **The fire.** All seven `GP_*` status effects share one start effect, the GameObject
  `fx_GP_Activation` (bundle `c4210710`, read with UnityPy). `EffectList.Create` instantiates it
  locally - no `ZNetView` - so only the channelling player sees it. The magnet takes the list
  from the first `GP_` in `ObjectDB.m_StatusEffects`, so it plays without a power held.
- **The pause screen sway** is `Game.UpdatePause`: while paused with the menu open it rotates
  `m_eye` around Y by `cos(realtime * 0.3) * 5` degrees per second, flattened near straight up
  or down, then `SetLookDir`. In play `Player.SetMouseLook` rebuilds the eye from `m_lookYaw`
  every frame, so the magnet turns `m_lookYaw` by the same motion instead, faded in.
- **The float** is an offset on the `Visual` child (`Character.m_visual`) set in a
  `Player.LateUpdate` postfix and restored from its resting position. It is local: nothing
  syncs the visual's offset.
- **Auto pickup** (`Player.AutoPickup`, from `FixedUpdate`) overlaps the `item` layer within
  `m_autoPickupRange` into a `Collider[100]` buffer, asks `CanPickup()` - which is
  `m_nview.IsOwner()` after a short spawn delay - and otherwise `RequestOwn()`s the drop
  (`RPC_RequestOwn`: the current owner hands the ZDO over; rate limited per drop by
  `m_ownerRetryTimeout`). It only pulls what fits the inventory and carry weight, moving it with
  `transform.position +=` at a fixed speed. The magnet walks `ItemDrop.s_instances` instead of
  overlapping, so it has no buffer limit, and moves only drops it owns; their `ZSyncTransform`
  sends the position to everyone.
- **Player drops.** `Humanoid.DropItem` calls `ItemDrop.OnPlayerDrop()` on every drop a player
  makes, right after `ItemDrop.DropItem` created it - the dropper is the owner, so the mark can
  be written there. `OnPlayerDrop` itself only clears the local, unsaved `m_autoPickup`.
- **Stack merging.** `ItemDrop.SlowUpdate` runs `AutoStackItems` on the owner once more than 200
  drops are loaded: the stack absorbs same-name, same-quality owned stacks within 4m and destroys
  them. The survivor keeps only its own ZDO, so `MovedMarker` replaces the loop and carries the
  mark over when any absorbed stack had it.
- **What is loaded.** Drops exist on a client only inside its simulated zones
  (`ZoneSystem.m_simulationDistance.NearSimulationDistance` zones of 64m around the player's
  zone); further out there is nothing to pull. `omp_magnet_probe` (Debug) reports loaded drops,
  ownership, marks and sweep cost per ring, which is how `MaxRadius` is chosen;
  `omp_magnet_scatter` and `omp_magnet_unmark` set up a test.
