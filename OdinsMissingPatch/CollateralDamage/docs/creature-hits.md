# Creature hits

Who a creature's attack may hit, how the game keeps monsters off each other, and what a hit does
to the victim's AI and loot. Read before changing `CollateralDamage` (overview in [`../CLAUDE.md`](../CLAUDE.md)). Verified against
`decompiled/assembly_valheim/` and the prefabs in bundle `c4210710` on 2026-09-26.

## Who counts as an enemy

`BaseAI.IsEnemy(a, b)` (static, `BaseAI.cs` ~1192) decides everything: same non-empty `m_group`
is never an enemy, tamed creatures side with players, aggravated ones fight players, then a
faction table. It is **not symmetric**:

- `ForestMonsters` (troll, greydwarfs), `MountainMonsters`, `PlainsMonsters` (fulings, lox,
  deathsquitos), `MistlandsMonsters` (seekers), `DeepNorth`, `SeaMonsters` are hostile to every
  other faction **except `AnimalsVeg` and `Boss`**; `Undead` and `Demon` also spare each other.
- `Boss` is hostile only to `Players` and `PlayerSpawned`: no boss ever hits a creature.
- `AnimalsVeg` (boar, neck, deer) is hostile to **every** other faction, so a boar hit by a troll
  answers even though the troll never meant it.

Factions read from the prefabs: Troll 2 `ForestMonsters`; Lox (group `lox`) and Goblin 7
`PlainsMonsters`; every boss 8 `Boss`; TentaRoot (the Elder's roots) 8 `Boss`; SeekerBrood 9
`MistlandsMonsters`; Blob 3 `Undead`; Charred_Melee_Fader 4 `Demon`.

## The three friendly fire gates

Each asks `IsEnemy` and lets a non-enemy through only if its own `m_hitFriendly` is set. Every
troll and boss attack checked has `m_hitFriendly = 0`.

| Gate | Runs for | Notes |
| --- | --- | --- |
| `Attack.DoMeleeAttack` (~1319) | Horizontal / Vertical attacks | Sphere casts every 4° over `m_attackAngle`; per ray the hits are sorted and a skipped character lets the ray **through**, the first accepted hit ends the ray unless `m_hitThroughWalls`. `m_lowerDamagePerHit` divides damage by the hit count. Tamed attackers never hit friends. |
| `Attack.DoAreaAttack` (~1153) | `Area` attacks | `OverlapSphereNonAlloc` at one origin: no ray, nothing can shield. |
| `Projectile.IsValidTarget` (~537) | direct hits (`OnHit`) and `DoAOE` | A direct hit on an invalid target returns without exploding, so the projectile flies through. |
| `Aoe.ShouldHit` (~466) | spawned `Aoe` objects | Also refuses the owner's own kind unless `m_hitSame`. |

All of them run on the **attacker's** owner; the damage goes to the victim's owner by
`Character.RPC_Damage`, which scales non-player attackers by difficulty and `m_enemyDamageRate`.

`Attack` is cloned per swing (`Humanoid.StartAttack`, `m_shared.m_attack.Clone()`), `Projectile`
and `Aoe` are instances, so a flag on them is per attack. `Attack.m_attackMaskCharacters` is the
character-only layer mask.

## The attacks

| Attacker | Item | Type | What hits |
| --- | --- | --- | --- |
| Troll | `troll_punch`, `troll_log_swing_h` | Horizontal | melee sweep |
| Troll | `troll_log_swing_v` | Vertical | melee sweep |
| Troll | `troll_groundslam` | Vertical | melee sweep, `m_spawnOnHit` `troll_groundslam_aoe` (blunt 50, `m_hitSame` 0) |
| Troll | `troll_throw` | Projectile | `troll_throw_projectile`, direct, no AoE |
| Eikthyr | `Eikthyr_antler`, `Eikthyr_charge` | Horizontal | melee sweep |
| Eikthyr | `Eikthyr_stomp` | Area | the lightning ring |
| Yagluth (`GoblinKing`) | `GoblinKing_Meteors` | Projectile | `spawn_meteors` (`SpawnAbility`) drops 10 `projectile_meteor` from 50m within 15m: `m_aoe` 5, blunt 40, fire 120 |
| Yagluth | `GoblinKing_Nova` | Area | |
| Yagluth | `GoblinKing_Beam` | Projectile | `projectile_beam`, direct |
| Elder (`gd_king`) | `gd_king_stomp` | Area | |
| Elder | `gd_king_shoot` | Projectile | `gdking_root_projectile` |
| Elder | `gd_king_rootspawn` | Projectile | `spawn_roots` (`SpawnAbility`) → `TentaRoot` |

## What bosses spawn

- `SpawnAbility` (Elder's `spawn_roots`, Bonemass's `bonemass_spawn`, Fader's
  `Fader_Roar_Spawn`) instantiates each creature in its `Spawn` coroutine and then calls the
  private `SetupAoe(spawnedCharacter, point)` for every one, `m_owner` being the boss. It writes
  nothing onto the spawned creature that links it to the boss.
- The Queen's `SeekerQueen_Call` fires `SeekerQueen_triggerspawn_ability` (`TriggerSpawnAbility`),
  which calls `TriggerSpawner.TriggerAllInRange`: an RPC to each spawner in her arena
  (`TriggerSpawner_Brood`, `TriggerSpawner_Seeker`), which spawns in `Spawn()` on the spawner's
  owner — possibly another client. Nothing else in the game triggers a `TriggerSpawner`. Rather
  than tag those spawns, the tweak leaves the Queen (`SeekerQueen`) out: her arena is closed and
  holds nothing but her brood.

## What a hit does to the victim

- `MonsterAI.OnDamaged` (fed by `Character.m_onDamaged`) does `Wakeup()`, `SetAlerted(true)` and
  the private `SetTarget(attacker)`, whatever the attacker is. `UpdateTarget` runs every
  `UpdateAI` and drops a target that is not `IsEnemy`, so a greydwarf hit by a troll lets go the
  next frame; a boar does not (see `AnimalsVeg` above).
- `RPC_OnNearProjectileHit` alerts and targets the shooter for creatures near an impact.
- `Character.m_lastHit` is set in `ApplyDamage` before health drops, so on death it is the
  killing blow. `RPC_Damage` records every player who hit the creature on its ZDO
  (`s_attackers + name`); nothing records how much they dealt.
- `CharacterDrop.OnDeath` (private, on the owner) drops whenever `m_dropsEnabled`, whoever
  killed the creature.
- Burning and poison ticks (`SE_Burning`, `SE_Poison`) call `ApplyDamage` with a `HitData` that
  has **no attacker**, so a creature a meteor set on fire and that burns to death has a
  `m_lastHit` naming nobody. That is why the loot rule counts the damage collateral hits deal
  (fire, spirit and poison included, taken from the hit after resistances in `RPC_Damage`, which
  is where the game splits them off into those effects) rather than asking who killed it.
- Troll swings, the ground slam and Eikthyr's antlers are `m_multiHit` with
  `m_lowerDamagePerHit` off; `projectile_meteor` has `m_noDamageFriendly` off.

## How the tweak tells a collateral hit

No attack of a troll or boss hits a non-enemy in vanilla, so a hit whose attacker is a listed
troll or a boss and whose victim that attacker does not count as an enemy can only be one the
tweak added. The victim's owner asks exactly that again in `RPC_Damage` and `MonsterAI.OnDamaged`,
from what both sides see (prefab hash and faction of the attacker, the victim's ZDO), so nothing
has to travel with the hit.

Every pass runs where vanilla's does, so each hit lands once however many players have the mod:
`Humanoid.OnAttackTrigger` and `Projectile.FixedUpdate` only run on the attacker's owner, an `Aoe`
with a `ZNetView` only on its owner, and one without (or a trigger `Aoe`) hits only the characters
the local client owns. The added passes skip attacks with `m_hitFriendly`, which vanilla already
lets hit peers; the projectile and `Aoe` patches only turn a vanilla "no" into "yes", and their own
hit lists (`s_hitSet`, `m_hitList`) still stop repeats.
