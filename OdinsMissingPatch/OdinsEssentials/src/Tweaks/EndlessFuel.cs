using HarmonyLib;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Fires, torches, braziers and everything else that burns fuel to give light never run out
    /// of it: a campfire lit once stays lit, and the torches along the wall no longer need a
    /// round with a stack of resin.
    ///
    /// The approach is the one Digitalroot's Eternal Fire takes - keep the fuel stored on the
    /// fire's ZDO topped up from its update tick, rather than flipping the game's own
    /// m_infiniteFuel, which would also blank the hover text and refuse fuel. Topping up rather
    /// than freezing the level keeps the fire looking full (a hearth shows its logs, a campfire
    /// its wood) and relights one that had burnt out before the tweak was switched on.
    /// </summary>
    internal sealed class EndlessFuel : Tweak
    {
        internal static readonly EndlessFuel Instance = new EndlessFuel();

        private EndlessFuel() { }

        internal override string Section => "Endless Fuel";

        protected override string Summary =>
            "Fires, torches, braziers and the hot tub never run out of fuel: whatever burns fuel " +
            "to give light stays lit once it is lit.";

        /// <summary>
        /// UpdateFireplace is the fire's two second tick: the owner of the ZDO burns fuel for the
        /// time passed since the last tick, then UpdateState turns the flames and the fuel
        /// models on or off from what is left. Refilling after the burn instead of before it
        /// means a fire loaded after hours away never dips through empty for a tick, and calling
        /// UpdateState again shows it full straight away.
        ///
        /// Only the owner writes: a ZDO written by anyone else is not synced and would be
        /// overwritten. Fires the game already marks infinite are left alone, as are ones that
        /// do not burn fuel at all - there is nothing to top up in either.
        /// </summary>
        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateFireplace))]
        private static class KeepFuelled
        {
            private static void Postfix(Fireplace __instance)
            {
                if (!Instance.On || __instance.m_infiniteFuel || __instance.m_secPerFuel <= 0f)
                {
                    return;
                }
                ZNetView nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                {
                    return;
                }
                ZDO zdo = nview.GetZDO();
                if (zdo.GetFloat(ZDOVars.s_fuel) < __instance.m_maxFuel)
                {
                    zdo.Set(ZDOVars.s_fuel, __instance.m_maxFuel);
                    __instance.UpdateState();
                }
            }
        }
    }
}
