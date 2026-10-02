using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Moves the boss each of Haldor's two inventory row upgrades waits for. Wider Pockets, the
    /// first extra row, needs Moder in vanilla - the fourth boss, the Mountains, long after the
    /// walk home with a full backpack has stopped being interesting - and is offered once the
    /// Elder has fallen by default. Deeper Pockets, the second row, keeps the Queen it waits for in vanilla.
    ///
    /// Only the gate moves. The upgrades are still Haldor's, still cost their 1000 and 2000 coins,
    /// are still bought once per character and still give one row each, and a boss the world has
    /// not beaten still holds its upgrade back. The gate is the world's boss progress, as it is in
    /// vanilla, so a character who beat the Elder elsewhere waits for this world's Elder; nothing
    /// is read or written on anyone else's machine, so the mod is only needed on your own.
    /// </summary>
    internal sealed class PocketUpgrades : Tweak
    {
        internal static readonly PocketUpgrades Instance = new PocketUpgrades();

        private PocketUpgrades() { }

        /// <summary>A boss an upgrade may wait for, in the order they fall. None is from the start.</summary>
        internal enum Boss
        {
            None,
            Eikthyr,
            TheElder,
            Bonemass,
            Moder,
            Yagluth,
            TheQueen,
            Fader,
        }

        /// <summary>
        /// The global key each boss sets when it dies - the same keys PowerPicker reads, taken off
        /// the boss prefabs themselves; see <c>docs/radial-menu.md</c>. The Deep North king sets
        /// <c>defeated_frozenking</c> but is not a boss a player can be told to go and beat yet,
        /// so it is not offered as a gate.
        /// </summary>
        private static readonly Dictionary<Boss, string> BossKeys = new Dictionary<Boss, string>
        {
            { Boss.None, "" },
            { Boss.Eikthyr, "defeated_eikthyr" },
            { Boss.TheElder, "defeated_gdking" },
            { Boss.Bonemass, "defeated_bonemass" },
            { Boss.Moder, "defeated_dragon" },
            { Boss.Yagluth, "defeated_goblinking" },
            { Boss.TheQueen, "defeated_queen" },
            { Boss.Fader, "defeated_fader" },
        };

        /// <summary>The player key each upgrade is bought into, which is what names the two items.</summary>
        private const string WiderPocketsKey = "invslot1";
        private const string DeeperPocketsKey = "invslot2";

        private ConfigEntry<Boss> widerPocketsBoss;
        private ConfigEntry<Boss> deeperPocketsBoss;

        // The vanilla gate of every upgrade that has been rewritten, so the config is applied to
        // that rather than to whatever the field holds now, and switching the tweak off puts the
        // vanilla gate back. Weak keys: an entry dies with the trade item it belongs to.
        private readonly ConditionalWeakTable<Trader.TradeItem, string> vanilla =
            new ConditionalWeakTable<Trader.TradeItem, string>();

        internal override string Section => "Pocket Upgrades";

        protected override string Summary =>
            "Choose which boss each of Haldor's two extra inventory rows waits for. Wider " +
            "Pockets comes with the Elder instead of Moder by default, so the early game is " +
            "less of a backpack shuffle.";

        protected override void Bind(ConfigFile config)
        {
            widerPocketsBoss = config.Bind(Section, "WiderPocketsBoss", Boss.TheElder,
                "The boss this world has to have beaten before Haldor sells Wider Pockets, the " +
                "first extra inventory row. Moder in vanilla. None offers it from the start.");
            deeperPocketsBoss = config.Bind(Section, "DeeperPocketsBoss", Boss.TheQueen,
                "The boss this world has to have beaten before Haldor sells Deeper Pockets, the " +
                "second extra inventory row. The Queen in vanilla, which is also the default " +
                "here. None offers it from the start.");
        }

        /// <summary>The setting an upgrade is gated by, or null for anything else on the shelf.</summary>
        private ConfigEntry<Boss> Setting(string buyKey)
        {
            if (buyKey == WiderPocketsKey)
            {
                return widerPocketsBoss;
            }
            return buyKey == DeeperPocketsKey ? deeperPocketsBoss : null;
        }

        /// <summary>
        /// Writes the gate the config asks for onto the two upgrades before the trader filters its
        /// shelf. Rewriting the item's own <c>m_requiredGlobalKey</c> leaves the game to do the
        /// asking, so the filter, the shop list and the purchase stay entirely vanilla and a
        /// condition the game grows later is not quietly dropped. Working from the remembered
        /// vanilla gate makes it idempotent, so it can simply be done every time the shelf is
        /// read: no bookkeeping, and the vanilla gate comes back the moment the tweak is off.
        /// </summary>
        private void ApplyGates(Trader trader)
        {
            if (trader == null || trader.m_items == null)
            {
                return;
            }
            foreach (Trader.TradeItem item in trader.m_items)
            {
                ConfigEntry<Boss> setting = item != null ? Setting(item.m_buyKey) : null;
                if (setting == null)
                {
                    continue;
                }
                // The first time an upgrade is seen its gate is still the prefab's.
                string gate = vanilla.GetValue(item, i => i.m_requiredGlobalKey ?? "");
                string chosen;
                if (!BossKeys.TryGetValue(setting.Value, out chosen))
                {
                    chosen = gate;
                }
                item.m_requiredGlobalKey = On ? chosen : gate;
            }
        }

        /// <summary>
        /// Every look at the shelf goes through here - the shop list the player reads and the
        /// items the trader makes known - so the gates are brought up to date in one place, and a
        /// setting changed with Haldor's window open shows on its next fill.
        /// </summary>
        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        private static class Gates
        {
            private static void Prefix(Trader __instance)
            {
                Instance.ApplyGates(__instance);
            }
        }
    }
}
