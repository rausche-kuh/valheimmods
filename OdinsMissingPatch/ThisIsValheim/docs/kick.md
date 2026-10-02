# The kick: detection, checks, refusals

How a kick is noticed and whether it may open the door. The bang, the network and the fast
swing are in [effects-and-network.md](effects-and-network.md).

## What happens, by release

- **0.1.0:** the bare handed secondary attack — the game's own kick, `unarmed_kick` — landing on
  a closed door bursts it open at `SwingSpeed`× the normal animation speed while a battering ram,
  splintering wood and a puff of sawdust go off at the door. The door is opened through the
  game's own `UseDoor` RPC, so the server and unmodded clients see an ordinary door swing.
- **0.1.1:** a warded or key-locked door that refuses the kick plays its own `m_lockedEffects`,
  shows the game's `$msg_door_needkey` / `$piece_noaccess` and staggers the kicker; a player
  looking at a door the kick would open gets a `$KEY_SecondaryAttack` "Kick" line in the hover
  text (`ShowHint`).
- **0.1.2:** the secondary attack while hovering a shut door is the kick whatever is held
  (`KickAtDoor`), and the hint shows regardless of the weapon; a kick that opens a door also
  kicks any other door with a collider within `SeamReach` (0.6 m) of the hit point
  (`KickNeighbours`), so double doors no longer need the seam hit exactly. Only after the first
  door opened, so a locked pair rebuffs once; each half gets its own bang. The fast swing ends
  when the door has opened or is shut again, so closing it right after the kick is at normal
  speed.

## The tweak

Since the move into the Odin's Missing Patch family the kick is the `DoorKick` tweak
(`src/Tweaks/DoorKick.cs`) on the family's framework: section `Kick` (with `Enabled`) plus
`Effects`, patched only when on, and every patch and `RPC_Kick` still ask `Instance.On`. The
plugin (`src/ThisIsValheim.cs`) only lists the tweaks; logging is `TweakHost.Log`.

## Noticing the kick

- **The kick is the game's, not the mod's.** `PlayerUnarmed`'s `m_secondaryAttack` is
  `unarmed_kick` (range 1.6, force ×3, stagger ×6) — there is no extra key and no new input. The
  hook (`NoticeKick`) is a postfix on the private `Attack.AddHitPoint`, which every melee sweep
  calls for every object it touches *before* the game asks whether that object can be damaged.
  So the reach, the aim and the angle are the game's own, and doors that have no `WearNTear` at
  all (the dungeon and Mistlands gates) still register. The attack is identified by
  `m_attackAnimation` containing "kick" (`IsKick`), so a punch or a sword never counts and another
  mod's kick would.
- **Any weapon kicks a door.** `KickAtDoor` is a `Humanoid.StartAttack` prefix: for the local
  player's secondary attack with a door under `GetHoverObject()` and `Check` not `Busy`, it sets
  `forceUnarmed`, and a `GetCurrentWeapon` postfix (`KickWeapon`) answers with `m_unarmedWeapon`
  while that is set; a finalizer clears it. `Attack` keeps its own `m_weapon` from then on, so
  only that one call is fooled and the rest is the ordinary unarmed kick (range, stamina,
  `NoticeKick`). Bows never get here — their input goes through `UpdateAttackBowDraw`, not
  `StartAttack(secondary)` — and hovering reaches further than the kick (1.6), so from across the
  room it is a whiffed kick.
- One swing sweeps several rays and can land on the same door more than once, and the door's
  state takes a round trip to its owner before it changes, so a second hit would read the door
  as still shut and slam it closed again. `KickCooldown` (0.6 s, per door in `kicked`, `Recent`)
  is what stops that; the dictionary is cleared once it grows past eight doors, since by then all
  but the newest are long out of cooldown.

## Checking the door

- **`Door.Open` asks nothing.** `RPC_UseDoor` flips the state for whoever sends it — no key
  check, no ward check, no hover range — so every one of those is `Check`'s job.
  `Door.CanInteract()` is the game's own "is it standing still and openable", and
  `ZDOVars.s_state != 0` is "already open"; both are needed.
  `IncrementPlayerStat(PlayerStatType.DoorsOpened)` is done in `TryKick` too, because
  `Door.Interact` — the only thing that normally counts a door — never runs.
- `Check` returns a `Refusal`: `Busy` (open, swinging, not loaded) is silent; `Ward` and `Key`
  go to `Rebuff`, which plays the door's `m_lockedEffects`, puts the game's own reason on screen
  and calls `Player.Stagger(away)`. `RPC_Stagger` turns the character to face `-forceDirection`,
  so passing the door→player direction keeps the kicker facing the door as they reel back. The
  cooldown is set before the rebuff too, so several rays of one kick stagger once.
- A key door only refuses a kicker without the key (`Door.HaveKey`, world level matched like
  vanilla, `NeedsKey`). With the key, `TryKick` does what `Door.Interact` does:
  `$msg_door_usingkey`, and the key removed when `m_consumeKey`. With `LockedDoors` on, the lock
  is ignored and no key is spent.
- The ward is checked but deliberately **not** flashed
  (`PrivateArea.CheckAccess(..., flash: false)`). The kick that got us here is a hit like any
  other, and `WearNTear.RPC_Damage` already calls `PrivateArea.OnObjectDamaged` for it; flashing
  again would flash twice for one kick.

## The hover hint

`ShowKickHint` is a `Door.GetHoverText` postfix that appends `[$KEY_SecondaryAttack] Kick` only
when `Check` is `None`, whatever is held. `Localization.Translate` turns `KEY_<name>` into the
bound key, and into the `Joy<name>` binding when a gamepad is active. "Kick" is literal English,
as the game's own "Change pose" on the armor stand is.
