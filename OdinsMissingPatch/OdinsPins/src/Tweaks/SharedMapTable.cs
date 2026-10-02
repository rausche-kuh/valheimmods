using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A cartography table shares by itself: walk up to one and your map goes onto it and its
    /// map onto yours, without touching it. That is the game's own write - read the table, merge,
    /// send - done silently: no message, no write effect, and never on a table behind a ward you
    /// may not use (that one is only read).
    /// <para>
    /// Reads and writes are kept apart, because a write is expensive for everyone: the game packs
    /// the whole exploration bitmap (a main-thread hitch for the writer) and the blob then travels
    /// through the server to every client in the zone. So a table is <b>read</b> whenever it holds
    /// data this client has not read yet - on arrival, or when someone else wrote to it - and
    /// <b>written</b> only on arrival and when a pin of yours changes while you are there, and
    /// even then only when the table is actually out of step with your map (an explored cell or
    /// a saved pin it lacks, or a pin you took off). Someone else's write never causes a write back: that would make two clients
    /// at one table pass the map to each other forever, since each write serialises the map in
    /// its own pin order and so never matches byte for byte. Nothing happens more often than
    /// MinInterval per table, and leaving the range forgets the table, so the next arrival
    /// syncs again.
    /// </para>
    /// </summary>
    internal sealed class SharedMapTable : Tweak
    {
        internal static readonly SharedMapTable Instance = new SharedMapTable();

        private SharedMapTable() { }

        /// <summary>How often the tables around the player are looked at.</summary>
        private const float CheckInterval = 1f;

        /// <summary>A table counts as left this far beyond SyncRange, so its edge does not flicker.</summary>
        private const float LeaveMargin = 8f;

        /// <summary>The table blob: version int, cell count int, then one byte per cell.</summary>
        private const int BitmapOffset = 8;

        private ConfigEntry<float> syncRange;
        private ConfigEntry<float> minInterval;

        internal override string Section => "Shared Map Table";

        protected override string Summary =>
            "A cartography table shares by itself: come near one and your map is written onto it " +
            "and its map onto yours, without a click. Tables behind a ward you have no access to " +
            "are only read.";

        protected override void Bind(ConfigFile config)
        {
            syncRange = config.Bind(Section, "SyncRange", 64f, new ConfigDescription(
                "Metres from a table within which it syncs. 64 is one zone.",
                new AcceptableValueRange<float>(5f, 256f)));
            minInterval = config.Bind(Section, "MinInterval", 30f, new ConfigDescription(
                "Seconds between two syncs of the same table. A write is as heavy as writing the " +
                "table by hand and sends the whole map to everyone in the zone, so a low value " +
                "makes the game stutter for everybody near a table.",
                new AcceptableValueRange<float>(1f, 600f)));
        }

        private sealed class TableState
        {
            /// <summary>Synced since the player last came into range.</summary>
            public bool UpToDate;
            /// <summary>The data revision this client last read (or wrote itself).</summary>
            public uint Revision;
            public float LastSync = float.NegativeInfinity;
            /// <summary>The pin generation the last sync saw.</summary>
            public int Generation;
            /// <summary>What the last write sent, to tell our own write from someone else's.</summary>
            public byte[] Sent;
        }

        private static readonly List<MapTable> Tables = new List<MapTable>();
        private static readonly ConditionalWeakTable<MapTable, TableState> States =
            new ConditionalWeakTable<MapTable, TableState>();

        /// <summary>Counts the saved pins added or removed, so a table knows it is behind.</summary>
        private static int generation;
        private static bool syncing;
        private static float nextCheck;

        private void Check(Player player)
        {
            Vector3 origin = player.transform.position;
            float range = syncRange.Value;
            for (int i = Tables.Count - 1; i >= 0; i--)
            {
                MapTable table = Tables[i];
                if (table == null)
                {
                    Tables.RemoveAt(i);
                    continue;
                }
                if (table.m_nview == null || !table.m_nview.IsValid())
                {
                    continue;
                }
                TableState state = States.GetOrCreateValue(table);
                float distance = Vector3.Distance(origin, table.transform.position);
                if (distance > range + LeaveMargin)
                {
                    state.UpToDate = false;
                    continue;
                }
                if (distance > range || Time.time - state.LastSync < minInterval.Value)
                {
                    continue;
                }
                ZDO zdo = table.m_nview.GetZDO();
                bool read = Unread(zdo, state);
                bool write = (!state.UpToDate || state.Generation != generation)
                    && PrivateArea.CheckAccess(table.transform.position, 0f, flash: false);
                if (!read && !write)
                {
                    state.UpToDate = true;
                    continue;
                }
                Sync(table, state, zdo, read, write);
                // One table per check: each sync is a hitch of its own.
                return;
            }
        }

        /// <summary>
        /// Whether the table holds data this client has not read. Our own write changes the
        /// revision too, once it reaches the table's owner, so a revision change whose data is
        /// exactly what we sent is taken as read instead.
        /// </summary>
        private static bool Unread(ZDO zdo, TableState state)
        {
            if (zdo.DataRevision == state.Revision)
            {
                return false;
            }
            byte[] data = zdo.GetByteArray(ZDOVars.s_data);
            if (state.Sent != null && data != null && SameBytes(data, state.Sent))
            {
                state.Revision = zdo.DataRevision;
                return false;
            }
            return true;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// MapTable.OnWrite without the parts a player would notice, and without the parts that
        /// are not needed: the read only when the data is new to us, the write only when the
        /// table lacks something of ours, no message, no effect.
        /// </summary>
        private static void Sync(MapTable table, TableState state, ZDO zdo, bool read, bool write)
        {
            byte[] data = zdo.GetByteArray(ZDOVars.s_data);
            if (data != null)
            {
                data = Utils.Decompress(data);
            }
            if (read && data != null)
            {
                syncing = true;
                try
                {
                    Minimap.instance.AddSharedMapData(data);
                }
                finally
                {
                    syncing = false;
                }
            }
            state.Revision = zdo.DataRevision;
            if (write && (data == null || Lacks(data)))
            {
                ZPackage package = table.GetMapData(data);
                state.Sent = package.GetArray();
                table.m_nview.InvokeRPC("MapData", package);
            }
            state.UpToDate = true;
            state.LastSync = Time.time;
            state.Generation = generation;
        }

        /// <summary>
        /// Whether the decompressed table blob is missing something this map has: a cell explored
        /// here (by us or read from another table) that the table has not, or a saved non-death
        /// pin without a table pin within 1m of it, which is the table's own merge rule. A blob
        /// that does not parse, or of a size the game would refuse, counts as lacking.
        /// </summary>
        private static bool Lacks(byte[] data)
        {
            Minimap map = Minimap.instance;
            int cells = map.m_explored.Length;
            if (data.Length < BitmapOffset + cells)
            {
                return true;
            }
            ZPackage package = new ZPackage(data);
            int version = package.ReadInt();
            if (package.ReadInt() != cells)
            {
                return true;
            }
            // The bitmap is one byte per cell, so it is compared straight out of the array.
            int[] explored = new int[(cells + 31) / 32];
            int[] others = new int[explored.Length];
            map.m_explored.CopyTo(explored, 0);
            map.m_exploredOthers.CopyTo(others, 0);
            for (int word = 0; word < explored.Length; word++)
            {
                int bits = explored[word] | others[word];
                if (bits == 0)
                {
                    continue;
                }
                int offset = BitmapOffset + word * 32;
                for (int bit = 0; bit < 32; bit++)
                {
                    if ((bits & (1 << bit)) != 0 && data[offset + bit] == 0)
                    {
                        return true;
                    }
                }
            }
            if (version < (int)Version.SharedMap.Pins)
            {
                return true;
            }
            package.SetPos(BitmapOffset + cells);
            int count = package.ReadInt();
            List<Vector3> positions = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                package.ReadLong();
                package.ReadString();
                positions.Add(package.ReadVector3());
                package.ReadInt();
                package.ReadBool();
                if (version >= (int)Version.SharedMap.PinsAuthor)
                {
                    package.ReadString();
                }
            }
            bool[] onTable = new bool[count];
            foreach (Minimap.PinData pin in map.m_pins)
            {
                if (!pin.m_save || pin.m_type == Minimap.PinType.Death)
                {
                    continue;
                }
                bool onMap = false;
                for (int i = 0; i < count; i++)
                {
                    if (Utils.DistanceXZ(positions[i], pin.m_pos) < 1f)
                    {
                        onTable[i] = true;
                        onMap = true;
                    }
                }
                if (!onMap)
                {
                    return true;
                }
            }
            // The table was read, so a table pin this map does not have is one the player took
            // off: the write is what leaves it off the table.
            for (int i = 0; i < count; i++)
            {
                if (!onTable[i])
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>MapTable keeps no list of itself, so every one that starts is put on ours.</summary>
        [HarmonyPatch(typeof(MapTable), nameof(MapTable.Start))]
        [LoadHook]
        private static class Register
        {
            private static void Postfix(MapTable __instance)
            {
                if (__instance.m_nview != null && __instance.m_nview.GetZDO() != null)
                {
                    Tables.Add(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Tick
        {
            private static void Postfix(Player __instance)
            {
                if (!Instance.On || __instance != Player.m_localPlayer || Minimap.instance == null
                    || Time.time < nextCheck)
                {
                    return;
                }
                nextCheck = Time.time + CheckInterval;
                Instance.Check(__instance);
            }
        }

        /// <summary>A saved pin was added: the tables in range are behind. Not for the pins a sync itself reads in.</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.AddPin))]
        private static class PinAdded
        {
            private static void Postfix(bool save)
            {
                if (save && !syncing)
                {
                    generation++;
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), typeof(Minimap.PinData))]
        private static class PinRemoved
        {
            private static void Postfix(Minimap.PinData pin)
            {
                if (pin != null && pin.m_save && !syncing)
                {
                    generation++;
                }
            }
        }
    }
}
