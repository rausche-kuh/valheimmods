using HarmonyLib;
using Splatform;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PinData = Minimap.PinData;
using PinType = Minimap.PinType;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A pin that belongs to nobody: an ordinary saved map pin of a vanilla type whose owner is a
    /// fixed id no player has and whose author is <c>OdinsMissingPatch_&lt;category&gt;</c>. To
    /// the game that is a pin shared by some other player, so a map table carries it to everyone,
    /// the table's own position check keeps it from ever being doubled, the shared-map toggle on
    /// the large map hides all of them at once, and a player without the mod sees a normal shared
    /// pin. The author survives the profile and the table, so it is what says a pin is ours and
    /// which category it is; the owner is only restored from it.
    /// <para>
    /// Three vanilla behaviours are corrected here, for AutoPins and SharedMapTable alike: a table
    /// read deletes the foreign pins the table does not hold (it would eat a fresh discovery), a
    /// left click claims a foreign pin instead of ticking it (it would make the pin the player's
    /// own), and a right click removes a pin for good only until the next table brings it back
    /// (so a removal is written down per character and world, and nothing adds it again).
    /// </para>
    /// </summary>
    internal static class UniversalPins
    {
        /// <summary>The platform half of the author: what marks a pin as one of ours.</summary>
        internal const string AuthorPlatform = "OdinsMissingPatch";

        /// <summary>The owner every universal pin carries - no player's id, and never 0.</summary>
        internal static readonly long Owner = (long)AuthorPlatform.GetStableHashCode() << 1 | 1;

        internal enum Category { Dungeon, Ore, Place, Portal }

        private static readonly string[] CategoryIds = { "dungeon", "ore", "place", "portal" };

        internal static int CategoryCount => CategoryIds.Length;

        /// <summary>The pins of one category are one pin when they are this close (XZ).</summary>
        internal static float MergeRadius(Category category)
        {
            // Every client computes a location's or a portal's position from the same ZDO, so
            // those agree to the centimetre; the rocks of one ore deposit are metres apart.
            return category == Category.Ore ? 8f : 1f;
        }

        /// <summary>Whether either tweak that makes or carries universal pins is switched on.</summary>
        private static bool Active => AutoPins.Instance.On || SharedMapTable.Instance.On;

        internal static bool IsUniversal(PinData pin)
        {
            return pin != null && pin.m_author.m_platform.Equals(AuthorPlatform);
        }

        internal static bool TryGetCategory(PinData pin, out Category category)
        {
            category = default;
            if (!IsUniversal(pin))
            {
                return false;
            }
            int index = Array.IndexOf(CategoryIds, pin.m_author.m_userID);
            if (index < 0)
            {
                return false;
            }
            category = (Category)index;
            return true;
        }

        /// <summary>The saved universal pin of this category within its merge radius of pos, if any.</summary>
        internal static PinData Find(Minimap map, Vector3 pos, Category category)
        {
            float radius = MergeRadius(category);
            foreach (PinData pin in map.m_pins)
            {
                if (pin.m_save && Utils.DistanceXZ(pos, pin.m_pos) < radius
                    && TryGetCategory(pin, out Category other) && other == category)
                {
                    return pin;
                }
            }
            return null;
        }

        /// <summary>
        /// Adds a universal pin. AddPin shows a pin type the player has hidden in the legend again;
        /// a pin nobody asked for must not undo that, so the filter is put back afterwards.
        /// </summary>
        internal static PinData Add(Minimap map, Vector3 pos, Category category, PinType type, string name, bool isChecked = false)
        {
            bool hidden = (int)type < map.m_visibleIconTypes.Length && !map.m_visibleIconTypes[(int)type];
            PinData pin = map.AddPin(pos, type, name, save: true, isChecked, Owner,
                new PlatformUserID(AuthorPlatform, CategoryIds[(int)category]));
            if (hidden)
            {
                map.m_visibleIconTypes[(int)type] = false;
                if (map.m_selectedIcons.TryGetValue(type, out var icon) && icon != null)
                {
                    // What ToggleIconFilter does to the legend, without its gamepad rumble.
                    icon.transform.parent.GetComponent<UnityEngine.UI.Image>().color = Color.gray;
                }
            }
            return pin;
        }

        /// <summary>
        /// Gives every universal pin its owner back. A claim (the vanilla first click on a shared
        /// pin) zeroes it, and a player without the mod writes it to a table under their own id;
        /// both leave the author alone. A claim is the player ticking the pin off, so it is turned
        /// into exactly that.
        /// </summary>
        internal static void Normalize(Minimap map)
        {
            foreach (PinData pin in map.m_pins)
            {
                if (pin.m_ownerID == Owner || !pin.m_save || !IsUniversal(pin))
                {
                    continue;
                }
                if (pin.m_ownerID == 0L)
                {
                    pin.m_checked = !pin.m_checked;
                }
                pin.m_ownerID = Owner;
                map.m_pinUpdateRequired = true;
            }
        }

        // --- the dismissed record ---------------------------------------------------------

        /// <summary>
        /// Player.m_customData key. The value is <c>world;category;x;z</c> entries joined by
        /// <c>|</c>, one per universal pin the player removed by hand, for every world at once.
        /// </summary>
        private const string DismissedKey = "omp.pins.dismissed";

        private struct Dismissal
        {
            public long World;
            public Category Category;
            public Vector3 Pos;
        }

        private static List<Dismissal> dismissed;
        private static Player dismissedOwner;

        private static long CurrentWorld => ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;

        private static List<Dismissal> Dismissed()
        {
            Player player = Player.m_localPlayer;
            if (dismissed != null && dismissedOwner == player)
            {
                return dismissed;
            }
            dismissedOwner = player;
            dismissed = new List<Dismissal>();
            if (player != null && player.m_customData.TryGetValue(DismissedKey, out string raw))
            {
                foreach (string entry in raw.Split('|'))
                {
                    string[] parts = entry.Split(';');
                    int category = Array.IndexOf(CategoryIds, parts.Length == 4 ? parts[1] : null);
                    if (category >= 0
                        && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long world)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        dismissed.Add(new Dismissal { World = world, Category = (Category)category, Pos = new Vector3(x, 0f, z) });
                    }
                }
            }
            return dismissed;
        }

        private static void SaveDismissed()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }
            var text = new StringBuilder();
            foreach (Dismissal d in dismissed)
            {
                if (text.Length > 0)
                {
                    text.Append('|');
                }
                text.Append(d.World.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(CategoryIds[(int)d.Category]).Append(';')
                    .Append(d.Pos.x.ToString("0.#", CultureInfo.InvariantCulture)).Append(';')
                    .Append(d.Pos.z.ToString("0.#", CultureInfo.InvariantCulture));
            }
            if (text.Length > 0)
            {
                player.m_customData[DismissedKey] = text.ToString();
            }
            else
            {
                player.m_customData.Remove(DismissedKey);
            }
        }

        /// <summary>Whether the player removed a pin of this category here in this world.</summary>
        internal static bool IsDismissed(Category category, Vector3 pos)
        {
            long world = CurrentWorld;
            float radius = MergeRadius(category);
            foreach (Dismissal d in Dismissed())
            {
                if (d.World == world && d.Category == category && Utils.DistanceXZ(d.Pos, pos) < radius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Takes a universal pin off the map for good, as a right click would: remembered as
        /// removed, so no table read brings it back.
        /// </summary>
        internal static void Discard(Minimap map, PinData pin, Category category)
        {
            map.RemovePin(pin);
            Dismiss(category, pin.m_pos);
        }

        private static void Dismiss(Category category, Vector3 pos)
        {
            if (Player.m_localPlayer == null || IsDismissed(category, pos))
            {
                return;
            }
            Dismissed().Add(new Dismissal { World = CurrentWorld, Category = category, Pos = pos });
            SaveDismissed();
        }

        /// <summary>Forgets every removal in the current world; returns how many there were.</summary>
        internal static int ForgetDismissed()
        {
            long world = CurrentWorld;
            int removed = Dismissed().RemoveAll(d => d.World == world);
            SaveDismissed();
            return removed;
        }

        /// <summary>Removes every universal pin the player has dismissed from the map.</summary>
        private static void PruneDismissed(Minimap map)
        {
            for (int i = map.m_pins.Count - 1; i >= 0; i--)
            {
                PinData pin = map.m_pins[i];
                if (TryGetCategory(pin, out Category category) && IsDismissed(category, pin.m_pos))
                {
                    map.RemovePin(pin);
                }
            }
        }

        // --- patches ------------------------------------------------------------------------

        /// <summary>
        /// A mouse claim happens in a UI callback and a gamepad claim inside UpdateMap, and
        /// neither can be told apart from a real one before it happens - so the owner is put back
        /// right after, while the large map is open. A few hundred pins, compared by a long and a
        /// short string, once a frame and only with the map open.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
        [Serves(typeof(AutoPins), typeof(SharedMapTable))]
        private static class UndoClaims
        {
            private static void Postfix(Minimap __instance)
            {
                if (Active && __instance.m_mode == Minimap.MapMode.Large)
                {
                    Normalize(__instance);
                }
            }
        }

        /// <summary>
        /// A table read marks every pin with a foreign owner for deletion and spares the ones the
        /// table holds. For a universal pin that would delete whatever this player found since
        /// the table last saw them. Their owner is zeroed for the length of the read, which makes
        /// them the player's own in the game's eyes, and put back after. A pin the table adds at
        /// a place this player has dismissed is taken off again.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.AddSharedMapData))]
        [Serves(typeof(AutoPins), typeof(SharedMapTable))]
        private static class KeepOnTableRead
        {
            private static readonly List<PinData> held = new List<PinData>();

            private static void Prefix(Minimap __instance)
            {
                held.Clear();
                if (!Active || Player.m_localPlayer == null)
                {
                    return;
                }
                Normalize(__instance);
                foreach (PinData pin in __instance.m_pins)
                {
                    if (pin.m_ownerID == Owner)
                    {
                        pin.m_ownerID = 0L;
                        held.Add(pin);
                    }
                }
            }

            /// <summary>
            /// The table's own check only merges pins within 1m. Two players who struck two rocks
            /// of one deposit have two ore pins a few metres apart, so a universal pin the read
            /// brought in is dropped when one the player already had lies within the category's
            /// merge radius. The next write then leaves the table with one.
            /// </summary>
            private static void PruneNearDuplicates(Minimap map)
            {
                for (int i = map.m_pins.Count - 1; i >= 0; i--)
                {
                    PinData pin = map.m_pins[i];
                    if (held.Contains(pin) || !TryGetCategory(pin, out Category category))
                    {
                        continue;
                    }
                    float radius = MergeRadius(category);
                    foreach (PinData kept in held)
                    {
                        if (Utils.DistanceXZ(kept.m_pos, pin.m_pos) < radius
                            && TryGetCategory(kept, out Category other) && other == category)
                        {
                            map.RemovePin(pin);
                            break;
                        }
                    }
                }
            }

            private static Exception Finalizer(Minimap __instance, Exception __exception)
            {
                if (held.Count == 0 && !Active)
                {
                    return __exception;
                }
                foreach (PinData pin in held)
                {
                    pin.m_ownerID = Owner;
                }
                if (Player.m_localPlayer != null)
                {
                    Normalize(__instance);
                    PruneDismissed(__instance);
                    PruneNearDuplicates(__instance);
                }
                held.Clear();
                return __exception;
            }
        }

        /// <summary>
        /// Both the right click and the gamepad's remove button go through RemovePin(pos, radius),
        /// and nothing else does - a table read removes by PinData - so this is exactly "the
        /// player took it off the map".
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), typeof(Vector3), typeof(float))]
        [Serves(typeof(AutoPins), typeof(SharedMapTable))]
        private static class RecordRemoval
        {
            private static void Prefix(Minimap __instance, Vector3 pos, float radius)
            {
                if (!Active)
                {
                    return;
                }
                PinData pin = __instance.GetClosestPin(pos, radius);
                if (TryGetCategory(pin, out Category category))
                {
                    Dismiss(category, pin.m_pos);
                }
            }
        }
    }
}
