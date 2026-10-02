using HarmonyLib;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Lets a weapon, a shield or a piece of armour be equipped - or taken off - without breaking
    /// stride. In vanilla a hotbar press while sprinting queues the equip and the sprint wipes
    /// the queue again on its next tick, so nothing happens until you slow down. Here the queued
    /// equip runs its usual duration while you keep running, and lands when it is done.
    /// </summary>
    internal sealed class EquipWhileRunning : Tweak
    {
        internal static readonly EquipWhileRunning Instance = new EquipWhileRunning();

        private EquipWhileRunning() { }

        // True while Player.CheckRun is running, so the ClearActionQueue patch knows the clear it
        // is about to see is the sprint's, and not an attack's, a jump's or a dodge's.
        private static bool sprinting;

        internal override string Section => "Equip While Running";

        protected override string Summary =>
            "Equip and unequip weapons, shields and armour while sprinting, instead of having " +
            "to slow down first.";

        // CheckRun is the one place the sprint touches the queue, and it makes exactly one
        // ClearActionQueue call, once the drain has been paid and there is still stamina left -
        // the frames the player really is sprinting. The other callers (an attack starting, a
        // jump, a dodge) are left alone, so their interruptions stay vanilla.

        [HarmonyPatch(typeof(Player), nameof(Player.CheckRun))]
        private static class NoticeSprint
        {
            private static void Prefix()
            {
                sprinting = Instance.On;
            }

            private static void Postfix()
            {
                sprinting = false;
            }
        }

        /// <summary>
        /// The sprint's wipe keeps the equips and unequips and drops only a reload: a crossbow
        /// is re-queued every frame it is unloaded, so keeping it would let the bolt load while
        /// sprinting, which is a bigger change than this tweak makes.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ClearActionQueue))]
        private static class KeepEquipsWhileSprinting
        {
            private static bool Prefix(Player __instance)
            {
                if (!sprinting)
                {
                    return true;
                }
                __instance.m_actionQueue.RemoveAll(
                    action => action.m_type == Player.MinorActionData.ActionType.Reload);
                return false;
            }
        }
    }
}
