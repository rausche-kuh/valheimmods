using BepInEx;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The chests around you count as your own: crafting and refuelling from them, quick stacking
    /// into them, and buttons to fill, take, place and sort in the inventory screen.
    /// The plugin binds the config and applies the patches through <see cref="TweakHost"/>; every
    /// change lives in its own <see cref="Tweak"/>.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class OdinsReachPlugin : BaseUnityPlugin
    {
        public const string GUID = "rauschekuh.odinsreach";
        public const string NAME = "Odin's Reach";
        public const string VERSION = "0.1.0";

        /// <summary>Every tweak the mod ships. Listing one here is all it takes to enable it.</summary>
        private static readonly Tweak[] Tweaks =
        {
            NearbyCrafting.Instance,
            QuickStack.Instance,
            StationRefill.Instance,
            ChestButtons.Instance,
            InventoryButtons.Instance,
        };

        void Awake()
        {
            TweakHost.Start(this, Logger, Config, GUID, Tweaks, SharedSettings.Bind);
        }
    }
}
