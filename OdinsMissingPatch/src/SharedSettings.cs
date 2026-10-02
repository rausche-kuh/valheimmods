using BepInEx.Configuration;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Settings more than one tweak reads, in a section of their own, so one question is asked
    /// once: how far the chests around you count, whether the hotbar stays where it is, and what
    /// counts as being in danger (<see cref="Danger"/>).
    /// </summary>
    internal static class SharedSettings
    {
        internal const string Section = "General";

        /// <summary>How far from the player a chest counts for every tweak that reaches into chests.</summary>
        internal static ConfigEntry<float> ChestRange;

        /// <summary>Whether the hotbar row is left alone by stacking away, placing and sorting.</summary>
        internal static ConfigEntry<bool> KeepHotbar;

        /// <summary>How close a hostile creature has to be to put you in danger.</summary>
        internal static ConfigEntry<float> ThreatRadius;

        /// <summary>Whether an enemy coming for you counts at any distance.</summary>
        internal static ConfigEntry<bool> EnragedEnemies;

        /// <summary>Whether a boss health bar on screen counts.</summary>
        internal static ConfigEntry<bool> BossFights;

        internal static void Bind(ConfigFile config)
        {
            ChestRange = config.Bind(Section, "ChestRange", 20f, new ConfigDescription(
                "How far from you a chest may stand, in metres, for nearby crafting, quick stack " +
                "and station refill to use it.",
                new AcceptableValueRange<float>(1f, 100f)));
            KeepHotbar = config.Bind(Section, "KeepHotbar", true,
                "Quick stack, Place all, Fill the chest and Sort leave the hotbar row alone. Off " +
                "lets them move it too.");
            ThreatRadius = config.Bind(Section, "ThreatRadius", 25f, new ConfigDescription(
                "Metres. A hostile creature closer than this puts you in combat, whether it has " +
                "noticed you or not: stamina costs return (Combat Stamina) and the item magnet " +
                "cannot be used.",
                new AcceptableValueRange<float>(0f, 200f)));
            EnragedEnemies = config.Bind(Section, "EnragedEnemies", true,
                "An enemy that has noticed you and is coming for you puts you in combat at any " +
                "distance, not only inside ThreatRadius. Off means only the radius counts.");
            BossFights = config.Bind(Section, "BossFights", true,
                "A boss health bar on screen puts you in combat, whoever the boss is after and " +
                "however far away it is.");
        }

        /// <summary>Whether an item sits in a row a whole-backpack action may not touch.</summary>
        internal static bool OnKeptHotbar(ItemDrop.ItemData item)
        {
            return KeepHotbar.Value && item.m_gridPos.y == 0;
        }
    }
}
