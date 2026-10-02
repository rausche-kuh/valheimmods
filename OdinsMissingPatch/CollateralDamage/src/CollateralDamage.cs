using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Trolls and bosses hit the creatures in the way of their attacks.
    /// The plugin binds the config and applies the patches through <see cref="TweakHost"/>; every
    /// change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class CollateralDamagePlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.collateraldamage";
        public const string NAME = "Collateral Damage";
        public const string VERSION = "0.1.0";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            CollateralDamage.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks);
        }
    }
}
