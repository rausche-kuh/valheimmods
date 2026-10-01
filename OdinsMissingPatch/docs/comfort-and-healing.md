# Comfort, Rested and healing

Ranges (`ComfortRadiusMultiplier`), Resting (`InstantRested`, `HealthPerComfortLevel`, `RequireSitting`).

- The comfort radius is a literal `10f` in `SE_Rested.GetNearbyComfortPieces`, but it reaches the
  world through `Piece.GetAllComfortPiecesInRadius(p, radius, pieces)`, whose only caller that is.
  A `ref float radius` prefix there is the whole change, no transpiler needed.
- Build range and comfort are both decided client side (`Player.PlacePiece`, `Hud`, `Player`'s
  comfort level), so these tweaks need no server install and change nothing in the world.
- Rested is handed out by Resting, not by the player: `Player.UpdateEnvStatusEffects` adds
  `Resting` (`SE_Cozy`, `resetTime: false`) every frame the conditions hold - a fire within the
  last 0.25s (`m_nearFireTimer`), and sitting or in shelter, and not wet, cold, freezing, burning
  or sensed - and removes it the frame they stop, so each rest is a fresh clone with `m_time` at
  0. `SE_Cozy.UpdateStatusEffect` adds its `m_statusEffect` (`Rested`, `resetTime: true`) on
  every tick past `m_delay` (10s). `SE_Rested.ResetTime` → `UpdateTTL` sets the duration to
  `m_baseTTL + (comfort - 1) * m_TTLPerComfortLevel` only if that is longer than what is left,
  so refreshing it every frame never shortens it. The comfort it reads is
  `Player.m_comfortLevel`, re-measured by `Player.UpdateBaseValue` on a 2s timer - stale for up to
  2s after walking in, which is why `InstantRested` measures it again before the first grant.
  `IsSitting()` is the animator tag, so a chair, a bench and the sit emote all count.
- All health regen is one tick at the tail of `Player.UpdateFood(dt, forceUpdate)` (local player
  only, from `UpdateStats` in `FixedUpdate`): `m_foodRegenTimer` counts plain `dt` to a literal
  `10f`, resets, sums `m_foodRegen` over the eaten foods, lets `SEMan.ModifyHealthRegen` (whose
  only caller it is) multiply it and calls `Heal` once - so an unfed player never heals, and the
  timer runs on regardless of what is eaten. `forceUpdate: true` returns before the timer, so it
  never ticks. A tweak that wants to *add* to that heal has no game hook to add at, but the
  prefix can see the tick coming (`m_foodRegenTimer + dt >= 10f`) and put the amount aside for a
  `Character.Heal` prefix to fold into the one call, which keeps it one floating number instead
  of two on top of each other; nothing else in `UpdateFood` heals (`SetMaxHealth` only ever
  lowers), so the window is the tick's own. Confirm the tick really fired in the finalizer, by
  the timer having gone backwards, before healing what no vanilla heal carried away.
