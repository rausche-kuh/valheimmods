using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Hold a key and every item lying around flies to your feet, out of combat only.
    /// The plugin binds the config and applies the patches through <see cref="TweakHost"/>; every
    /// change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class ItemMagnetPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.itemmagnet";
        public const string NAME = "Item Magnet";
        public const string VERSION = "0.1.0";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            ItemMagnet.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks,
                config => Danger.Bind(config, "the item magnet cannot be used."));
        }
    }
}
