using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Trolls and bosses hit whatever stands in their way. A troll's log swing kicks the greydwarfs
    /// beside the player aside, Eikthyr's lightning ring burns the dwarfs in the arena, Yagluth's
    /// meteors fall on fulings and lox. Vanilla only lets them hit what they count as an enemy.
    ///
    /// Nothing else changes: the vanilla hit is left exactly as it is and the creatures in the way
    /// ("peers") are hit by a separate pass afterwards, so a peer never shields the player. No
    /// targeting is touched, so a troll or boss only ever hits a peer while swinging at a player,
    /// and a peer that is hit does not wake, alert or turn on its attacker. What a boss spawned
    /// (the Elder's roots, Bonemass's blobs, Fader's charred) is never hit. The Queen hits nothing:
    /// her arena is closed and all she has around her is her own brood.
    /// A creature that trolls and bosses took most of the health of drops nothing, unless a player
    /// finished it. The facts behind it are in docs/creature-hits.md.
    /// </summary>
    internal sealed partial class CollateralDamage : Tweak
    {
        internal static readonly CollateralDamage Instance = new CollateralDamage();

        private CollateralDamage() { }

        // On the victim's ZDO: spawned by a boss, so never collateral; and how much health
        // collateral hits took, for the loot rule.
        private static readonly int BossSpawnKey = "omp_bossSpawn".GetStableHashCode();
        private static readonly int CollateralKey = "omp_collateralDamage".GetStableHashCode();

        // The Queen fights in a closed arena among her brood, which her spawners bring and nothing
        // marks as hers, so she is never an attacker.
        private static readonly int QueenPrefab = "SeekerQueen".GetStableHashCode();

        // The game's melee sweep steps its rays every 4 degrees.
        private const float SweepStep = 4f;

        private ConfigEntry<bool> bosses;
        private ConfigEntry<float> damage;
        private ConfigEntry<float> lootLimit;

        // Prefab hashes parsed from Creatures; re-read when the setting changes, never per hit.
        private HashSet<int> attackers = new HashSet<int>();

        internal override string Section => "Collateral Damage";

        protected override string Summary =>
            "Trolls and bosses hit the creatures in their way - greydwarfs beside a troll's swing, " +
            "dwarfs in Eikthyr's lightning, fulings and lox under Yagluth's meteors - without them " +
            "fighting back or anyone picking new targets. What a boss spawns is never hit. Every " +
            "player needs the mod for it to apply everywhere.";

        protected override void Bind(ConfigFile config)
        {
            BindList(config, "Creatures", "Troll",
                "Comma separated prefab names of the creatures whose attacks hit the creatures in " +
                "their way, e.g. Troll, Abomination, Gjall. Tamed ones never do.", Parse);
            bosses = config.Bind(Section, "Bosses", true,
                "Every boss's attacks but the Queen's hit the creatures in their way too. What a boss spawns is never hit.");
            damage = BindMultiplier(config, "Damage", 1f,
                "Multiplier on the damage a creature takes from a troll or boss that did not mean to hit it.");
            lootLimit = config.Bind(Section, "LootLimit", 0.5f, new ConfigDescription(
                "A creature that was not finished by a player drops nothing once trolls and bosses " +
                "hitting it by accident took more than this share of its health. 1 keeps all loot.",
                new AcceptableValueRange<float>(0f, 1f)));
        }

        private void Parse(List<string> names)
        {
            var parsed = new HashSet<int>();
            foreach (string name in names)
            {
                parsed.Add(name.GetStableHashCode());
            }
            attackers = parsed;
        }

        private static ZDO GetZDO(Character character)
        {
            ZNetView nview = character.m_nview;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        /// <summary>A creature whose attacks hit its peers: a listed prefab or a boss but the Queen, never tamed.</summary>
        private bool IsAttacker(Character character)
        {
            if (character == null || character.IsPlayer() || character.IsTamed())
            {
                return false;
            }
            ZDO zdo = GetZDO(character);
            if (zdo == null)
            {
                return false;
            }
            int prefab = zdo.GetPrefab();
            return attackers.Contains(prefab) || (bosses.Value && character.IsBoss() && prefab != QueenPrefab);
        }

        /// <summary>
        /// Whether the attacker's hit on the victim is one this tweak adds: a listed attacker, a
        /// victim it does not count as an enemy (vanilla hits the rest), and never a player, a
        /// tamed creature, a boss, a boss's roots or anything a boss spawned. The victim's owner
        /// asks it again, so the answer has to depend only on what both sides see.
        /// </summary>
        private bool IsCollateral(Character attacker, Character victim)
        {
            if (victim == null || victim == attacker || !IsAttacker(attacker))
            {
                return false;
            }
            if (victim.IsPlayer() || victim.IsTamed() || victim.IsBoss() || victim.m_faction == Character.Faction.Boss)
            {
                return false;
            }
            if (BaseAI.IsEnemy(attacker, victim))
            {
                return false;
            }
            ZDO zdo = GetZDO(victim);
            return zdo != null && !zdo.GetBool(BossSpawnKey);
        }

        private static void MarkBossSpawn(Character character)
        {
            if (character != null && character.m_nview != null && character.m_nview.IsValid() && character.m_nview.IsOwner())
            {
                character.m_nview.GetZDO().Set(BossSpawnKey, true);
            }
        }

        private struct Victim
        {
            internal Character Character;
            internal Collider Collider;
            internal Vector3 Point;
        }

        private static readonly RaycastHit[] rayHits = new RaycastHit[128];
        private static readonly List<RaycastHit> rayList = new List<RaycastHit>();
        private static readonly Collider[] overlaps = new Collider[128];
        private static readonly List<Victim> victims = new List<Victim>();
        private static readonly Comparison<RaycastHit> ByDistance = (x, y) => x.distance.CompareTo(y.distance);

        private static void AddVictim(Character character, Collider collider, Vector3 point)
        {
            foreach (Victim victim in victims)
            {
                if (victim.Character == character)
                {
                    return;
                }
            }
            victims.Add(new Victim { Character = character, Collider = collider, Point = point });
        }

        /// <summary>
        /// The hit the attack would have dealt the victim, built the way the game builds it for
        /// an enemy, minus what would reward the attacker: no spawn on hit, no health or eitr.
        /// </summary>
        private static HitData BuildHit(Attack attack, Victim victim, Vector3 dir, float factor, float chainMultiplier)
        {
            Humanoid attacker = attack.m_character;
            ItemDrop.ItemData.SharedData shared = attack.m_weapon.m_shared;
            var hit = new HitData();
            hit.m_toolTier = (short)shared.m_toolTier;
            hit.m_statusEffectHash = shared.m_attackStatusEffect != null &&
                (shared.m_attackStatusEffectChance == 1f || UnityEngine.Random.Range(0f, 1f) < shared.m_attackStatusEffectChance)
                ? shared.m_attackStatusEffect.NameHash() : 0;
            hit.m_skillLevel = attacker.GetSkillLevel(shared.m_skillType);
            hit.m_itemLevel = (short)attack.m_weapon.m_quality;
            hit.m_itemWorldLevel = (byte)attack.m_weapon.m_worldLevel;
            hit.m_pushForce = shared.m_attackForce * factor * attack.m_forceMultiplier;
            hit.m_backstabBonus = shared.m_backstabBonus;
            hit.m_staggerMultiplier = attack.m_staggerMultiplier;
            hit.m_dodgeable = shared.m_dodgeable;
            hit.m_blockable = shared.m_blockable;
            hit.m_skill = shared.m_skillType;
            hit.m_skillRaiseAmount = attack.m_raiseSkillAmount;
            hit.m_damage = attack.m_weapon.GetDamage();
            hit.m_point = victim.Point;
            hit.m_dir = dir;
            hit.m_hitCollider = victim.Collider;
            hit.SetAttacker(attacker);
            hit.m_hitType = HitData.HitType.EnemyHit;
            hit.m_variant = shared.m_hitVariant;
            attack.ModifyDamage(hit, factor);
            if (chainMultiplier > 1f)
            {
                hit.m_damage.Modify(chainMultiplier);
                hit.m_pushForce *= 1.2f;
            }
            attacker.GetSEMan().ModifyAttack(shared.m_skillType, ref hit);
            return hit;
        }

        private static void Strike(Attack attack, Victim victim, HitData hit)
        {
            ZDOID attackerId = attack.m_character.GetZDOID();
            attack.m_weapon.m_shared.m_hitEffect.Create(victim.Point, Quaternion.identity, null, 1f, -1, attackerId);
            attack.m_hitEffect.Create(victim.Point, Quaternion.identity, null, 1f, -1, attackerId);
            victim.Character.Damage(hit);
        }

        private static bool Dodges(Attack attack, Character victim)
        {
            return attack.m_weapon.m_shared.m_dodgeable && victim.IsDodgeInvincible();
        }

        /// <summary>
        /// Troll swings and punches, Eikthyr's antlers: the game's own sweep again, over the same
        /// rays, collecting the peers it skipped. A character never stops these rays (the vanilla
        /// hit already happened), terrain and anything else solid still does. An attack with
        /// m_hitFriendly already hit the peers itself, so it is left alone here and in AreaBlast.
        /// </summary>
        [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
        private static class MeleeSweep
        {
            private static void Postfix(Attack __instance)
            {
                Humanoid attacker = __instance.m_character;
                if (!Instance.On || __instance.m_hitFriendly || !Instance.IsAttacker(attacker) || __instance.m_weapon == null)
                {
                    return;
                }
                Attack a = __instance;
                a.GetMeleeAttackDir(out Transform originJoint, out Vector3 attackDir);
                Transform body = attacker.transform;
                Vector3 local = body.InverseTransformDirection(attackDir);
                Vector3 origin = originJoint.position + Vector3.up * a.m_attackHeight + body.right * a.m_attackOffset;
                float half = a.m_attackAngle / 2f;
                int layerMask = a.m_hitTerrain ? Attack.m_attackMaskTerrain : Attack.m_attackMask;
                float charWidth = a.m_attackRayWidth + a.m_attackRayWidthCharExtra;
                victims.Clear();
                for (float angle = -half; angle <= half; angle += SweepStep)
                {
                    Quaternion turn = Quaternion.identity;
                    if (a.m_attackType == Attack.AttackType.Horizontal)
                    {
                        turn = Quaternion.Euler(0f, -angle, 0f);
                    }
                    else if (a.m_attackType == Attack.AttackType.Vertical)
                    {
                        turn = Quaternion.Euler(angle, 0f, 0f);
                    }
                    Vector3 dir = body.TransformDirection(turn * local);
                    rayList.Clear();
                    if (a.m_attackRayWidth > 0f)
                    {
                        AddRays(Physics.SphereCastNonAlloc(origin, a.m_attackRayWidth, dir, rayHits,
                            Mathf.Max(0f, a.m_attackRange - a.m_attackRayWidth), layerMask, QueryTriggerInteraction.Ignore));
                        if (a.m_attackRayWidthCharExtra > 0f || a.m_attackHeightChar1 != 0f)
                        {
                            AddRays(Physics.SphereCastNonAlloc(origin + Vector3.up * a.m_attackHeightChar1, charWidth, dir, rayHits,
                                Mathf.Max(0f, a.m_attackRange - charWidth), Attack.m_attackMaskCharacters, QueryTriggerInteraction.Ignore));
                            if (a.m_attackHeightChar2 != a.m_attackHeightChar1)
                            {
                                AddRays(Physics.SphereCastNonAlloc(origin + Vector3.up * a.m_attackHeightChar2, charWidth, dir, rayHits,
                                    Mathf.Max(0f, a.m_attackRange - charWidth), Attack.m_attackMaskCharacters, QueryTriggerInteraction.Ignore));
                            }
                        }
                    }
                    else
                    {
                        AddRays(Physics.RaycastNonAlloc(origin, dir, rayHits, a.m_attackRange, layerMask, QueryTriggerInteraction.Ignore));
                    }
                    rayList.Sort(ByDistance);
                    WalkRay(a, attacker, origin, dir, attackDir);
                }

                float chain = a.m_attackChainLevels > 1 && a.m_currentAttackCainLevel == a.m_attackChainLevels - 1 ? 2f : 1f;
                foreach (Victim victim in victims)
                {
                    if (Dodges(a, victim.Character))
                    {
                        continue;
                    }
                    float factor = attacker.GetRandomSkillFactor(a.m_weapon.m_shared.m_skillType);
                    Strike(a, victim, BuildHit(a, victim, (victim.Point - origin).normalized, factor, chain));
                    if (!a.m_multiHit)
                    {
                        break;
                    }
                }
                victims.Clear();
            }

            private static void AddRays(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    rayList.Add(rayHits[i]);
                }
            }

            /// <summary>One ray, nearest first: peers are collected, the first solid thing ends it.</summary>
            private static void WalkRay(Attack a, Humanoid attacker, Vector3 origin, Vector3 dir, Vector3 attackDir)
            {
                foreach (RaycastHit rayHit in rayList)
                {
                    if (rayHit.collider.gameObject == attacker.gameObject)
                    {
                        continue;
                    }
                    Vector3 point = rayHit.point;
                    if (rayHit.normal == -dir && rayHit.point == Vector3.zero)
                    {
                        point = rayHit.collider is MeshCollider ? origin + dir * a.m_attackRange : rayHit.collider.ClosestPoint(origin);
                    }
                    if (a.m_attackAngle < 180f && Vector3.Dot(point - origin, attackDir) <= 0f)
                    {
                        continue;
                    }
                    GameObject hitObject = Projectile.FindHitObject(rayHit.collider);
                    if (hitObject == null || hitObject == attacker.gameObject)
                    {
                        continue;
                    }
                    Character character = hitObject.GetComponent<Character>();
                    if (character != null)
                    {
                        if (Instance.IsCollateral(attacker, character))
                        {
                            AddVictim(character, rayHit.collider, point);
                        }
                        continue;
                    }
                    if (!a.m_hitThroughWalls)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>Eikthyr's lightning ring, Yagluth's nova, the Elder's stomp: the same overlap again.</summary>
        [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
        private static class AreaBlast
        {
            private static void Postfix(Attack __instance)
            {
                Humanoid attacker = __instance.m_character;
                if (!Instance.On || __instance.m_hitFriendly || !Instance.IsAttacker(attacker) || __instance.m_weapon == null)
                {
                    return;
                }
                Attack a = __instance;
                Transform body = attacker.transform;
                Vector3 origin = a.GetAttackOrigin().position + Vector3.up * a.m_attackHeight +
                    body.forward * a.m_attackRange + body.right * a.m_attackOffset;
                float charWidth = a.m_attackRayWidth + a.m_attackRayWidthCharExtra;
                victims.Clear();
                Collect(attacker, origin, Physics.OverlapSphereNonAlloc(origin, a.m_attackRayWidth, overlaps,
                    Attack.m_attackMaskCharacters, QueryTriggerInteraction.UseGlobal));
                if (a.m_attackRayWidthCharExtra > 0f || a.m_attackHeightChar1 != 0f)
                {
                    Collect(attacker, origin, Physics.OverlapSphereNonAlloc(origin + Vector3.up * a.m_attackHeightChar1, charWidth,
                        overlaps, Attack.m_attackMaskCharacters, QueryTriggerInteraction.UseGlobal));
                    if (a.m_attackHeightChar2 != a.m_attackHeightChar1)
                    {
                        Collect(attacker, origin, Physics.OverlapSphereNonAlloc(origin + Vector3.up * a.m_attackHeightChar2, charWidth,
                            overlaps, Attack.m_attackMaskCharacters, QueryTriggerInteraction.UseGlobal));
                    }
                }

                float factor = attacker.GetRandomSkillFactor(a.m_weapon.m_shared.m_skillType);
                float chain = a.m_attackChainLevels > 1 && a.m_currentAttackCainLevel == a.m_attackChainLevels - 1
                    ? a.m_lastChainDamageMultiplier : 1f;
                foreach (Victim victim in victims)
                {
                    if (Dodges(a, victim.Character))
                    {
                        continue;
                    }
                    // The push points away from the blast, or away from the attacker when the
                    // victim stands between the two, as the game's own area hit does.
                    Vector3 dir = victim.Point - origin;
                    dir.y = 0f;
                    Vector3 fromBody = victim.Point - body.position;
                    if (Vector3.Dot(fromBody, dir) < 0f)
                    {
                        dir = fromBody;
                    }
                    Strike(a, victim, BuildHit(a, victim, dir.normalized, factor, chain));
                }
                victims.Clear();
            }

            private static void Collect(Humanoid attacker, Vector3 origin, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Collider collider = overlaps[i];
                    GameObject hitObject = Projectile.FindHitObject(collider);
                    Character character = hitObject != null ? hitObject.GetComponent<Character>() : null;
                    if (character != null && Instance.IsCollateral(attacker, character))
                    {
                        Vector3 point = collider is MeshCollider ? collider.ClosestPointOnBounds(origin) : collider.ClosestPoint(origin);
                        AddVictim(character, collider, point);
                    }
                }
            }
        }

        // The projectile exploding right now. Only while it explodes does a peer count as a
        // target, so in flight a meteor still passes through fulings and lands where it was aimed.
        private static Projectile exploding;

        /// <summary>Yagluth's meteors: their explosion reaches the peers under them.</summary>
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.DoAOE))]
        private static class ProjectileBlast
        {
            private static void Prefix(Projectile __instance)
            {
                exploding = Instance.On && Instance.IsAttacker(__instance.m_owner) ? __instance : null;
            }

            private static void Finalizer()
            {
                exploding = null;
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.IsValidTarget))]
        private static class ProjectileTarget
        {
            private static void Postfix(Projectile __instance, IDestructible destr, ref bool __result)
            {
                if (__result || exploding == null || __instance != exploding || __instance.m_noDamageFriendly)
                {
                    return;
                }
                Character character = destr as Character;
                if (character == null || (__instance.m_dodgeable && character.IsDodgeInvincible()))
                {
                    return;
                }
                __result = Instance.IsCollateral(__instance.m_owner, character);
            }
        }

        /// <summary>The troll's ground slam and every other area a troll or boss leaves behind.</summary>
        [HarmonyPatch(typeof(Aoe), nameof(Aoe.ShouldHit))]
        private static class AreaEffect
        {
            private static void Postfix(Aoe __instance, Collider collider, ref bool __result)
            {
                if (__result || !Instance.On || !__instance.m_hitCharacters)
                {
                    return;
                }
                Character owner = __instance.m_owner;
                if (!Instance.IsAttacker(owner))
                {
                    return;
                }
                GameObject hitObject = Projectile.FindHitObject(collider);
                Character character = hitObject != null ? hitObject.GetComponent<Character>() : null;
                if (character == null || character == owner)
                {
                    return;
                }
                // An Aoe without a ZNetView runs on every client and hits only what each owns.
                if ((__instance.m_nview == null && !character.IsOwner()) ||
                    (__instance.m_dodgeable && character.IsDodgeInvincible()))
                {
                    return;
                }
                __result = Instance.IsCollateral(owner, character);
            }
        }

        /// <summary>
        /// On the victim's owner: a collateral hit is scaled by Damage, and what it takes off is
        /// added up on the victim's ZDO for the loot rule, after the victim's resistances.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class VictimSide
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!Instance.On || hit == null || !hit.HaveAttacker() || __instance.m_nview == null
                    || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner())
                {
                    return;
                }
                if (!Instance.IsCollateral(hit.GetAttacker(), __instance))
                {
                    return;
                }
                hit.ApplyModifier(Instance.damage.Value);
                HitData taken = hit.Clone();
                taken.ApplyResistance(__instance.GetDamageModifiers(), out _);
                float dealt = Mathf.Min(taken.GetTotalDamage(), Mathf.Max(0f, __instance.GetHealth()));
                if (dealt > 0f)
                {
                    ZDO zdo = __instance.m_nview.GetZDO();
                    zdo.Set(CollateralKey, zdo.GetFloat(CollateralKey) + dealt);
                }
            }
        }

        /// <summary>
        /// A collateral hit does not wake, alert or turn the victim on its attacker; only the
        /// "recently hurt" timer the base class keeps is still reset.
        /// </summary>
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.OnDamaged))]
        private static class NoAggro
        {
            private static bool Prefix(MonsterAI __instance, Character attacker)
            {
                if (!Instance.On || !Instance.IsCollateral(attacker, __instance.m_character))
                {
                    return true;
                }
                __instance.m_timeSinceHurt = 0f;
                return false;
            }
        }

        /// <summary>
        /// No drops for a creature that trolls and bosses took more than LootLimit of its health
        /// from, unless a player dealt the killing blow.
        /// </summary>
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.OnDeath))]
        private static class Loot
        {
            private static bool Prefix(CharacterDrop __instance)
            {
                Character character = __instance.m_character;
                if (!Instance.On || character == null)
                {
                    return true;
                }
                ZDO zdo = GetZDO(character);
                float collateral = zdo != null ? zdo.GetFloat(CollateralKey) : 0f;
                if (collateral <= 0f || (character.m_lastHit != null && character.m_lastHit.GetAttacker() is Player))
                {
                    return true;
                }
                return collateral <= Instance.lootLimit.Value * character.GetMaxHealth();
            }
        }

        /// <summary>The Elder's roots, Bonemass's blobs and Fader's charred, as the boss spawns them.</summary>
        [HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.SetupAoe))]
        private static class SpawnedByBoss
        {
            private static void Prefix(SpawnAbility __instance, Character owner)
            {
                if (Instance.On && owner != null && __instance.m_owner != null && __instance.m_owner.IsBoss())
                {
                    MarkBossSpawn(owner);
                }
            }
        }
    }
}
