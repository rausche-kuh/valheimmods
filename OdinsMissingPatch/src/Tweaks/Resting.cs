using BepInEx.Configuration;
using HarmonyLib;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Makes resting by the fire worth sitting down for: Rested comes at once instead of after the
    /// wait, and the comfort of the spot heals you on top of your food - a campfire out in the
    /// open is worth little, a furnished hall with a fire, a bed, a banner and a chair ten times
    /// as much.
    ///
    /// Whether you are resting at all is the game's own verdict (the Resting effect): a fire
    /// within reach, and sitting or under your own roof, and not wet, cold, burning or spotted.
    /// So the fire is always required, the wet and the cold still get in the way, and a monster
    /// finding you cuts both off with the rest of it. Rested is still the effect Resting hands out.
    /// </summary>
    internal sealed class Resting : Tweak
    {
        internal static readonly Resting Instance = new Resting();

        private Resting() { }

        // The game's own regen period: Player.UpdateFood heals once m_foodRegenTimer passes it.
        private const float RegenInterval = 10f;

        private ConfigEntry<bool> instantRested;
        private ConfigEntry<float> healthPerComfortLevel;
        private ConfigEntry<bool> requireSitting;

        // What this regen tick owes for comfort, waiting to be folded into the game's own heal.
        // Non-zero only while Player.UpdateFood is running.
        private static float pending;

        internal override string Section => "Resting";

        protected override string Summary =>
            "Sitting down by a fire grants Rested at once, and resting by a fire heals you for the " +
            "comfort of the spot, on top of what your food heals.";

        protected override void Bind(ConfigFile config)
        {
            instantRested = config.Bind(Section, "InstantRested", true,
                "Sitting down by a fire grants Rested at once, at the comfort of where you sit, " +
                "instead of after ten seconds of Resting. Standing by the fire waits as in vanilla.");
            healthPerComfortLevel = config.Bind(Section, "HealthPerComfortLevel", 2f, new ConfigDescription(
                "Health healed per comfort level every ten seconds while you rest by a fire, on " +
                "the same beat as your food heals you. A lone campfire is comfort 1, a furnished " +
                "hall reaches ten and more, so 2 is about 12 health a minute at a campfire and " +
                "120 in a good hall. 0 is vanilla.",
                new AcceptableValueRange<float>(0f, 100f)));
            requireSitting = config.Bind(Section, "RequireSitting", true,
                "Only heal for comfort while you are actually sitting - a chair, a bench or the " +
                "sit emote. Off heals whenever you are Resting, which includes standing by the " +
                "fire under your own roof.");
        }

        /// <summary>
        /// Resting (SE_Cozy) is added by the player's environment update on every frame the
        /// resting conditions hold and removed the moment they stop, so it is a fresh instance
        /// each time you sit down, and its own tick hands out Rested once its age passes
        /// m_delay. This postfix hands it out from the first tick instead, while the player is
        /// sitting, and steps aside once the vanilla tick has taken over.
        ///
        /// The comfort level Rested's duration is computed from is re-measured by the player on
        /// its own timer, which the wait used to hide: sit down straight after walking in and the
        /// level may still be the one from outside. So it is measured again before the first
        /// grant. Later ticks only refresh the duration, which the game never shortens, so they
        /// can use the timer's value.
        /// </summary>
        [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
        private static class GrantWhileSitting
        {
            private static void Postfix(SE_Cozy __instance)
            {
                if (!Instance.On || !Instance.instantRested.Value || __instance.m_time > __instance.m_delay)
                {
                    return;
                }
                Player player = __instance.m_character as Player;
                if (player == null || player != Player.m_localPlayer || !player.IsSitting())
                {
                    return;
                }
                int rested = __instance.m_statusEffectHash;
                if (rested == 0)
                {
                    return;
                }
                SEMan seman = player.GetSEMan();
                if (seman == null)
                {
                    return;
                }
                if (!seman.HaveStatusEffect(rested))
                {
                    player.m_comfortLevel = SE_Rested.CalculateComfortLevel(player);
                }
                seman.AddStatusEffect(rested, resetTime: true);
            }
        }

        /// <summary>
        /// What the spot the player is in is worth this tick, or 0 if it owes nothing. The
        /// comfort level is the one the player measured on their own timer, which is as fresh
        /// as anything on the regen beat needs.
        /// </summary>
        private float ComfortHealth(Player player)
        {
            if (!On || player == null || player != Player.m_localPlayer)
            {
                return 0f;
            }
            if (requireSitting.Value && !player.IsSitting())
            {
                return 0f;
            }
            SEMan seman = player.GetSEMan();
            if (seman == null || !seman.HaveStatusEffect(SEMan.s_statusEffectResting))
            {
                return 0f;
            }
            int comfort = player.GetComfortLevel();
            return comfort > 0 ? comfort * healthPerComfortLevel.Value : 0f;
        }

        /// <summary>
        /// The regen tick lives inside <c>Player.UpdateFood</c>: once its timer runs out it sums
        /// the food's regen, lets the status effects multiply it and heals that much - and when no
        /// food is eaten it heals nothing at all. So the tick is worked out in the prefix (the
        /// timer plus this frame's dt, exactly what the method is about to test) and put aside
        /// for the <see cref="FoldIntoFoodHeal"/> patch to add to the game's own heal, which
        /// keeps it one number on screen rather than two floating over each other.
        ///
        /// The finalizer heals what is left over, which is the tick where the player has eaten
        /// nothing and the game never healed at all. It heals only once the timer confirms the
        /// tick really did fire, so a mispredicted frame pays nothing, and it clears what was
        /// put aside whether the method returned or threw - a stale amount would otherwise be
        /// picked up by the next heal the player gets from anywhere.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
        private static class HealForComfort
        {
            private static void Prefix(Player __instance, float dt, bool forceUpdate, out float __state)
            {
                __state = __instance.m_foodRegenTimer;
                pending = forceUpdate || __instance.m_foodRegenTimer + dt < RegenInterval
                    ? 0f
                    : Instance.ComfortHealth(__instance);
            }

            private static void Finalizer(Player __instance, float __state)
            {
                float owed = pending;
                pending = 0f;
                if (owed > 0f && __instance.m_foodRegenTimer < __state)
                {
                    __instance.Heal(owed);
                }
            }
        }

        /// <summary>
        /// The one heal the regen tick does, made bigger. Nothing else can be caught by this:
        /// the amount is only ever put aside for the length of <c>Player.UpdateFood</c>, and the
        /// one heal in there is the tick's own.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Heal))]
        private static class FoldIntoFoodHeal
        {
            private static void Prefix(Character __instance, ref float hp)
            {
                if (pending > 0f && __instance == Player.m_localPlayer)
                {
                    hp += pending;
                    pending = 0f;
                }
            }
        }
    }
}
