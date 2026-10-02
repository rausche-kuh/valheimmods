# CollateralDamage

Collateral Damage: trolls and bosses hit the creatures in the way of their attacks. A member of
the Odin's Missing Patch family — see [`../CLAUDE.md`](../CLAUDE.md) for the family rules (tweak
shape, `Common/`) and the root `CLAUDE.md` for the build. Not in the pack: it changes fights.

**State:** 0.1.0, not released yet. Written inside Odin's Missing Patch (never released there),
briefly part of This Is Valheim, now a mod of its own. One tweak, `CollateralDamage`, section
`Collateral Damage`.

## Docs

| Read before | Doc |
| --- | --- |
| picking the next task | [`ROADMAP.md`](ROADMAP.md) |
| anything the tweak does | [`docs/creature-hits.md`](docs/creature-hits.md) — who an attack hits, factions, boss spawns, the victim's AI and loot |
| the shared code, conventions | [`../docs/`](../docs/) — `architecture.md`, `conventions.md` |

## The tweak

Trolls (`Creatures`, prefab names) and every boss but `SeekerQueen` (`Bosses`) hit the creatures
they do not count as enemies: a postfix on `Attack.DoMeleeAttack` repeats the sweep (characters
never end a ray, anything solid does), one on `DoAreaAttack` repeats the overlap,
`Projectile.IsValidTarget` says yes to a peer only inside `DoAOE` (meteors explode on peers but
never stop on them), `Aoe.ShouldHit` likewise (the troll's ground slam). The vanilla hit is never
touched, so peers cannot shield. Never a player, tamed creature, boss, `Boss` faction (the Elder's
roots) or boss spawn: `SpawnAbility.SetupAoe` from a boss gives the ZDO bool `omp_bossSpawn`. The
Queen is left out because her brood comes from arena spawners nothing ties to her, and her closed
arena holds nothing else. The victim's owner recognises the hit by the same check in `RPC_Damage`,
scales it by `Damage` (1) and adds what it took after resistances to the ZDO float
`omp_collateralDamage`; the `MonsterAI.OnDamaged` reaction (wake, alert, target) is skipped.
`CharacterDrop.OnDeath` drops nothing when that float is above `LootLimit` (0.5) of max health and
no player dealt the killing blow.

Scope: world state (victim ZDOs); both the attacker's and the victim's owner need the mod.

## Rules that always apply

- The family's rules hold here unchanged — see [`../CLAUDE.md`](../CLAUDE.md).
- Its ZDO keys (`omp_collateralDamage`, `omp_bossSpawn`) live in worlds — never rename them.
- No player text, so no `assets/translations.csv`.

## Source map

| Path | What |
| --- | --- |
| `src/CollateralDamage.cs` | The plugin: name, version, the `Tweaks` list, `TweakHost.Start`. |
| `src/Tweaks/CollateralDamage.cs` | Trolls and bosses hit the creatures they do not count as enemies; the victim's side scales, records and silences the hit; loot only with a player's share. |
| `src/Dev/CollateralTest.cs` | Debug only: `omp_cd <scene>` spawns a troll or boss with peers, `omp_cd_info`, `omp_cd_clear`; every collateral hit and loot decision shown top left. |
