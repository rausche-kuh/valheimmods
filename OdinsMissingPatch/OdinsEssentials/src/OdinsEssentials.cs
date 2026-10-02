using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The everyday tweaks of the Odin's Missing Patch family: ranges, fuel, rest, portals, death,
    /// repairing, stamina, equipping and the trader - small changes to how the base game plays.
    /// The plugin binds the config and applies the patches through <see cref="TweakHost"/>; every
    /// change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class OdinsEssentialsPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.odinsessentials";
        public const string NAME = "Odin's Essentials";
        public const string VERSION = "0.1.0";

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
            AutoRepair.Instance,
            PowerPicker.Instance,
            EquipWhileRunning.Instance,
            BuildInWater.Instance,
            AutoShield.Instance,
            PocketUpgrades.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks,
                config => Danger.Bind(config, "stamina costs return (Combat Stamina)."));
        }
    }
}
