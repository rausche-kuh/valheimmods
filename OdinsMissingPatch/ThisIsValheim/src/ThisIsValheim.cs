using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Valheim, louder: doors are kicked open, not opened. The plugin binds the config and applies the patches through
    /// <see cref="TweakHost"/>; each change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class ThisIsValheimPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.thisisvalheim";
        public const string NAME = "This Is Valheim!";
        public const string VERSION = "0.1.2";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            DoorKick.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks);
        }
    }
}
