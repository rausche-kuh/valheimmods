using BepInEx.Configuration;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Settings more than one of the mod's tweaks reads, in a section of their own, so one
    /// question is asked once: how far the chests around you count, and whether the hotbar stays
    /// where it is.
    /// </summary>
    internal static class SharedSettings
    {
        internal const string Section = "General";

        /// <summary>How far from the player a chest counts for every tweak that reaches into chests.</summary>
        internal static ConfigEntry<float> ChestRange;

        /// <summary>Whether the hotbar row is left alone by stacking away, placing and sorting.</summary>
        internal static ConfigEntry<bool> KeepHotbar;

        internal static void Bind(ConfigFile config)
        {
            ChestRange = config.Bind(Section, "ChestRange", 20f, new ConfigDescription(
                "How far from you a chest may stand, in metres, for nearby crafting, quick stack " +
                "and station refill to use it.",
                new AcceptableValueRange<float>(1f, 100f)));
            KeepHotbar = config.Bind(Section, "KeepHotbar", true,
                "Quick stack, Place all, Fill the chest and Sort leave the hotbar row alone. Off " +
                "lets them move it too.");
        }

        /// <summary>Whether an item sits in a row a whole-backpack action may not touch.</summary>
        internal static bool OnKeptHotbar(ItemDrop.ItemData item)
        {
            return KeepHotbar.Value && item.m_gridPos.y == 0;
        }
    }
}
