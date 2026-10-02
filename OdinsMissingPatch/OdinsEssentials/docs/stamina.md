# Stamina and hostility

CombatStamina, and `Danger` (`Common/src/Danger.cs`): what counts as combat, shared with the Item
Magnet ([item-magnet](../../ItemMagnet/docs/item-magnet.md)). Each mod that asks binds the three
settings into its own `General` section (`Danger.Bind(config, effect)` from its plugin); a tweak
opts in with `Uses => new[] { typeof(Danger) }`, which brings in the `[Serves(typeof(Danger))]`
patch.

- The player's stamina spends, and where each is decided: sprint in `Player.CheckRun` off
  `m_runStaminaDrain`, jump in `Player.OnJump` off `m_jumpStaminaUsage`, swim in
  `Player.OnSwimming` as a lerp between `m_swimStaminaDrainMinSkill` and `...MaxSkill`, sneak in
  `Player.OnSneaking` off `m_sneakStaminaDrain`, each ending in one `UseStamina` call. Place,
  repair and remove piece all fetch `Player.GetBuildStamina()`; every swing (tools included) fetches
  `Attack.GetAttackStamina()`, which can be negative (`m_staminaReturnPerMissingHP` is a refund).
  The bow's `m_drawStaminaDrain` has no reader in `assembly_valheim`. `Player.UseStamina` returns
  early on `0`, otherwise it is what restarts the regen delay (`m_staminaRegenTimer =
  m_staminaRegenDelay` in `RPC_UseStamina`).
- `Player.UpdateStats(float)` (the overload matters, there is a parameterless one) sets the regen
  rate to a local `0f` while `(IsSwimming() && !IsOnGround()) || InAttack() || InDodge() ||
  m_wallRunning || IsEncumbered()`, and `SEMan.ModifyStaminaRegen` after it only multiplies. The
  ZDO stamina write is inside the method, so a postfix that changes `m_stamina` is a frame late
  for other clients. Drowning (`OnSwimming`) starts only when `!HaveStamina()`, i.e. at 0.
- Hostility is `BaseAI.IsEnemy(a, b)` (static): factions, tame status, groups and aggravation
  (an aggravated dvergr is an enemy of players). `Character.GetAllCharacters()` is every loaded
  character, `BaseAI.BaseAIInstances` every loaded AI.
- Alert state (`BaseAI.IsAlerted()`, the animator's `alert`, the "!" effect) is on the ZDO as
  `s_alert` and a non-owner re-reads it every `UpdateAI`, so it is right on every client. A
  monster's target (`MonsterAI.m_targetCreature`) is **not** on the ZDO - only a "has a target"
  bool is - so on a client that does not own the monster `GetTargetCreature()` is null. What does
  cross the network is `MonsterAI.UpdateTarget` calling `Player.OnTargeted(sensed, alerted)` on its
  target when that is a player, which throttles to one `OnTargeted` RPC per ~0.5s per player (the
  timers advance on remote copies too) and lands in `Player.RPC_OnTargeted` on the player's own
  client. That is what `IsTargeted()` / `IsSensed()` (the stealth HUD eye) and the combat music
  timer are fed from, and what `Danger` reads its enraged signal from: alerted, and coming
  for you, whoever owns the monster. `HuntPlayer()` monsters (bosses, event creatures) are alerted
  permanently and target the closest player within 200m, so they report too.
- The boss health bar: `EnemyHud.TestShow` shows a `Character.IsBoss()` hud while its `BaseAI` is
  alerted and it is within `m_maxShowDistanceBoss` (100m) of the local player;
  `EnemyHud.instance.ShowingBossHud()` (public) is whether any boss hud is up. `Danger` reads
  that under `General.BossFights`.
