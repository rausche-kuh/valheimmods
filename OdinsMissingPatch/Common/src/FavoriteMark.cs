namespace OdinsMissingPatch
{
    /// <summary>
    /// The golden favourite mark on a stack, as Odin's Reach's quick stack sets it: a key in the
    /// item's custom data, so it travels with the item and needs nothing from that mod to be read.
    /// Odin's Essentials' auto shield reads it to pick a favourite shield first.
    /// </summary>
    internal static class FavoriteMark
    {
        /// <summary>The custom data key. Saved on characters, so it may never change.</summary>
        internal const string Key = "OMP_Favorite";

        internal static bool IsSet(ItemDrop.ItemData item)
        {
            return item != null && item.m_customData != null
                && item.m_customData.TryGetValue(Key, out string value) && value == "1";
        }
    }
}
