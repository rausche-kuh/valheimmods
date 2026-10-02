using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Linq;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A collection of small quality of life changes. The plugin itself does nothing but bind the
    /// config file and apply the patches - every change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class OdinsMissingPatchPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.odinsmissingpatch";
        public const string NAME = "Odin's Missing Patch";
        public const string VERSION = "0.3.2";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            Ranges.Instance,
            EndlessFuel.Instance,
            CombatStamina.Instance,
            Resting.Instance,
            FastPortals.Instance,
            KeepGearOnDeath.Instance,
            AreaRepair.Instance,
            NearbyCrafting.Instance,
            QuickStack.Instance,
            StationRefill.Instance,
            AutoRepair.Instance,
            ChestButtons.Instance,
            InventoryButtons.Instance,
            PowerPicker.Instance,
            EquipWhileRunning.Instance,
            AutoShield.Instance,
            PocketUpgrades.Instance,
            SharedMapTable.Instance,
            AutoPins.Instance,
            PinLooks.Instance,
            DeathPins.Instance,
            PlayerMarks.Instance,
            CollateralDamage.Instance,
            ItemMagnet.Instance,
        };

        /// <summary>The mod's one log, for every tweak and helper.</summary>
        internal static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            SharedSettings.Bind(Config);
            Patcher.Map(Tweaks, Logger);
            foreach (Tweak tweak in Tweaks)
            {
                tweak.Setup(Config, Patcher.MayNeedRestart(tweak));
            }

            // Only the tweaks switched on get their patches, each on its own, so a tweak switched
            // off leaves the game code it would touch to other mods, and a patch a game update
            // broke takes down only the tweaks that need it.
            Patcher.Apply(new Harmony(GUID), Tweaks);

            string on = string.Join(", ", Tweaks.Where(t => t.On).Select(t => t.Section).ToArray());
            Logger.LogInfo(on.Length > 0 ? "tweaks on: " + on : "every tweak is switched off");
        }
    }
}
