using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Map pins that make themselves: dungeons, ore and places pinned as you find them and shared
    /// with everyone, map tables that sync on their own, tidier pins and death pins that clean up.
    /// The plugin binds the config and applies the patches through <see cref="TweakHost"/>; every
    /// change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class OdinsPinsPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.odinspins";
        public const string NAME = "Odin's Pins";
        public const string VERSION = "0.1.0";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            SharedMapTable.Instance,
            AutoPins.Instance,
            PinLooks.Instance,
            DeathPins.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks);
        }
    }
}
