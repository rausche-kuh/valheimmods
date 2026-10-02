using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Stamina only drains in combat. Out in the quiet - nothing hostile close by, nothing that
    /// has noticed you coming after you - sprinting, jumping, swimming, sneaking, building,
    /// chopping, mining and weapon swings cost nothing, and the bar keeps refilling while you
    /// swim or swing. The moment something hostile comes close, or something that has spotted
    /// you gives chase, every cost is back at full price, mid swing if need be.
    ///
    /// What counts as combat is <see cref="Danger"/>, shared with the item magnet.
    /// </summary>
    internal sealed class CombatStamina : Tweak
    {
        internal static readonly CombatStamina Instance = new CombatStamina();

        private CombatStamina() { }

        private ConfigEntry<bool> freeSprint;
        private ConfigEntry<bool> freeJump;
        private ConfigEntry<bool> freeSwim;
        private ConfigEntry<bool> freeSneak;
        private ConfigEntry<bool> freeBuild;
        private ConfigEntry<bool> freeAttacks;

        // True while a Player method whose one stamina spend this tweak waives is running, so the
        // UseStamina patch knows to drop the spend it is about to see.
        private static bool waiving;

        internal override string Section => "Combat Stamina";

        protected override string Summary =>
            "Stamina only drains in combat: sprinting, jumping, swimming, sneaking, building, " +
            "chopping, mining and weapon swings cost nothing while nothing hostile is close by " +
            "or after you.";

        protected override void Bind(ConfigFile config)
        {
            freeSprint = config.Bind(Section, "FreeSprint", true,
                "Sprinting costs nothing out of combat.");
            freeJump = config.Bind(Section, "FreeJump", true,
                "Jumping costs nothing out of combat.");
            freeSwim = config.Bind(Section, "FreeSwim", true,
                "Swimming costs nothing out of combat, and stamina refills while you swim. That " +
                "also means you cannot drown out of combat: drowning only starts once stamina is " +
                "empty.");
            freeSneak = config.Bind(Section, "FreeSneak", true,
                "Sneaking costs nothing out of combat.");
            freeBuild = config.Bind(Section, "FreeBuild", true,
                "Building, repairing and removing pieces cost nothing out of combat.");
            freeAttacks = config.Bind(Section, "FreeAttacks", true,
                "Chopping, mining and weapon swings cost nothing out of combat, and stamina " +
                "refills mid swing.");
        }

        /// <summary>
        /// Whether this tweak waives the cost the switch stands for, for this character, right
        /// now. Only the local player is ever waived: Attack runs for every character in the
        /// world, and the threat radius is only ever clear when nothing hostile is near - which
        /// is exactly when a wandering boar would otherwise swing for free.
        /// </summary>
        private bool Waives(ConfigEntry<bool> cost, Character character)
        {
            Player player = Player.m_localPlayer;
            return On && cost.Value && player != null && character == player && !Danger.Near(player);
        }

        /// <summary>
        /// Which switch owns the reason vanilla stopped stamina regenerating this frame, or null
        /// when it did not stop or stopped for a reason this tweak leaves alone. Mirrors the
        /// condition in UpdateStats: swimming answers to the swim switch, attacking and dodging to
        /// the attack switch, and wall running and being encumbered are vanilla's business.
        /// </summary>
        private ConfigEntry<bool> RegenBlockedBy(Player player)
        {
            if (player.IsWallRunning() || player.IsEncumbered())
            {
                return null;
            }
            if (player.IsSwimming() && !player.IsOnGround())
            {
                return freeSwim;
            }
            if (player.InAttack() || player.InDodge())
            {
                return freeAttacks;
            }
            return null;
        }

        // Sprinting, jumping, swimming and sneaking each work out their cost inside one Player
        // method and spend it through UseStamina there, and spend nothing else there. Rather than
        // reaching into the arithmetic, the method runs untouched - skill XP, the empty bar flash,
        // the drown timer - and the spend it makes is dropped at UseStamina. UseStamina is also
        // what restarts the regeneration delay, so a waived cost does not pause the refill either.

        [HarmonyPatch(typeof(Player), nameof(Player.CheckRun))]
        private static class FreeSprinting
        {
            private static void Prefix(Player __instance)
            {
                waiving = Instance.Waives(Instance.freeSprint, __instance);
            }

            private static void Postfix()
            {
                waiving = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnJump))]
        private static class FreeJumping
        {
            private static void Prefix(Player __instance)
            {
                waiving = Instance.Waives(Instance.freeJump, __instance);
            }

            private static void Postfix()
            {
                waiving = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSwimming))]
        private static class FreeSwimming
        {
            private static void Prefix(Player __instance)
            {
                waiving = Instance.Waives(Instance.freeSwim, __instance);
            }

            private static void Postfix()
            {
                waiving = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSneaking))]
        private static class FreeSneaking
        {
            private static void Prefix(Player __instance)
            {
                waiving = Instance.Waives(Instance.freeSneak, __instance);
            }

            private static void Postfix()
            {
                waiving = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
        private static class DropWaivedSpend
        {
            private static bool Prefix()
            {
                return !waiving;
            }
        }

        /// <summary>
        /// Placing, repairing and removing a piece all fetch their cost here and both gate on it
        /// and spend it, so a zero return covers all three.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.GetBuildStamina))]
        private static class FreeBuilding
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (__result > 0f && Instance.Waives(Instance.freeBuild, __instance))
                {
                    __result = 0f;
                }
            }
        }

        /// <summary>
        /// Every swing - axe on a tree, pickaxe on ore, sword on a greydwarf - gates on and spends
        /// this one return, at the start of the attack, on the hit and for a held attack, so a
        /// zero covers the lot. A negative return is a refund (some weapons give stamina back per
        /// missing health) and is left alone.
        /// </summary>
        [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackStamina))]
        private static class FreeAttacking
        {
            private static void Postfix(Attack __instance, ref float __result)
            {
                if (__result > 0f && Instance.Waives(Instance.freeAttacks, __instance.m_character))
                {
                    __result = 0f;
                }
            }
        }

        /// <summary>
        /// Waiving a cost is not the same as refilling: UpdateStats sets the regeneration rate to
        /// zero outright while swimming, attacking or dodging, and the status effect pass after
        /// it only multiplies, so nothing downstream can bring a zeroed rate back. The bar would
        /// sit flat through a long swim, and a bar that hit empty while fleeing into the water
        /// would stay empty - and drowning starts at empty. The rate is a local of the method, so
        /// this runs the game's own regen line again for exactly the frames it skipped, once the
        /// regen delay has passed as usual. Vanilla writes the stamina to the ZDO before this
        /// runs, so another player's view of the bar is a frame behind; the local HUD reads the
        /// field.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateStats), typeof(float))]
        private static class RefillWhileFree
        {
            private static void Postfix(Player __instance, float dt)
            {
                if (!Instance.On || __instance != Player.m_localPlayer || __instance.m_staminaRegenTimer > 0f)
                {
                    return;
                }
                ConfigEntry<bool> blocker = Instance.RegenBlockedBy(__instance);
                if (blocker == null || !Instance.Waives(blocker, __instance))
                {
                    return;
                }
                float max = __instance.GetMaxStamina();
                float stamina = __instance.m_stamina;
                if (stamina >= max)
                {
                    return;
                }
                float rate = __instance.m_staminaRegen
                    + (1f - stamina / max) * __instance.m_staminaRegen * __instance.m_staminaRegenTimeMultiplier;
                if (__instance.IsBlocking())
                {
                    rate *= 0.8f;
                }
                float multiplier = 1f;
                __instance.GetSEMan().ModifyStaminaRegen(ref multiplier);
                __instance.m_stamina = Mathf.Min(max, stamina + rate * multiplier * dt * Game.m_staminaRegenRate);
            }
        }
    }
}
