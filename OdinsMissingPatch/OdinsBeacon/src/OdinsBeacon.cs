using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A mark over every other player's head, seen through walls and terrain and held at the
    /// screen's edge when they are off screen.
    /// The plugin binds the config and applies the patches through <see cref="TweakHost"/>; every
    /// change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class OdinsBeaconPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.odinsbeacon";
        public const string NAME = "Odin's Beacon";
        public const string VERSION = "0.1.0";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            PlayerMarks.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks);
        }
    }
}
