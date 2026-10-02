using HarmonyLib;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace OdinsMissingPatch
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships.
    internal sealed partial class ItemMagnet
    {
        // What the scatter drops: light stacks, a heavy one, and one that does not stack.
        private static readonly string[] ScatterPrefabs = { "Wood", "Stone", "Resin", "CopperOre", "Club" };

        // The rings the probe reports on, in metres.
        private static readonly float[] ProbeRings = { 10f, 20f, 30f, 40f, 60f, 80f, 120f };

        /// <summary>
        /// omp_magnet_scatter [count] [radius] drops test items in rings out to the radius;
        /// omp_magnet_probe counts the loaded drops per ring (owned by you, marked as moved),
        /// names the furthest one loaded and times one capture sweep - how to pick MaxRadius;
        /// omp_magnet_unmark [radius] clears the moved mark of the drops you own around you.
        /// </summary>
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class Commands
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("omp_magnet_scatter", "[count] [radius] drops test items in rings around you",
                    args => Scatter(args.Context, Int(args, 1, 40), Float(args, 2, 40f)));
                new Terminal.ConsoleCommand("omp_magnet_probe", "loaded drops per ring, ownership, marks, sweep cost",
                    args => Probe(args.Context));
                new Terminal.ConsoleCommand("omp_magnet_unmark", "[radius] clears the moved mark of drops you own nearby",
                    args => Unmark(args.Context, Float(args, 1, 60f)));
            }
        }

        private static int Int(Terminal.ConsoleEventArgs args, int index, int fallback)
        {
            return args.Length > index && int.TryParse(args[index], out int value) ? value : fallback;
        }

        private static float Float(Terminal.ConsoleEventArgs args, int index, float fallback)
        {
            return args.Length > index && float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;
        }

        private static void Scatter(Terminal context, int count, float radius)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                context.AddString("no player");
                return;
            }
            Vector3 center = player.transform.position;
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(ScatterPrefabs[i % ScatterPrefabs.Length]);
                if (prefab == null)
                {
                    continue;
                }
                // Spread evenly over the area out to the radius, from 3m out so nothing lands at your feet.
                float distance = Mathf.Lerp(3f, radius, Mathf.Sqrt((i + 0.5f) / count));
                float angle = i * 2.39996f;
                Vector3 position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                position.y = ZoneSystem.instance.GetGroundHeight(position) + 0.5f;
                Object.Instantiate(prefab, position, Quaternion.identity);
            }
            context.AddString($"scattered {count} items out to {radius:0}m");
        }

        private static void Probe(Terminal context)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                context.AddString("no player");
                return;
            }
            Vector3 feet = player.transform.position;
            int[] loaded = new int[ProbeRings.Length];
            int[] owned = new int[ProbeRings.Length];
            int[] marked = new int[ProbeRings.Length];
            int[] pullable = new int[ProbeRings.Length];
            float furthest = 0f;
            foreach (ItemDrop item in ItemDrop.s_instances)
            {
                if (item == null || item.m_nview == null || !item.m_nview.IsValid())
                {
                    continue;
                }
                float distance = Vector3.Distance(item.transform.position, feet);
                furthest = Mathf.Max(furthest, distance);
                for (int ring = 0; ring < ProbeRings.Length; ring++)
                {
                    if (distance > ProbeRings[ring])
                    {
                        continue;
                    }
                    loaded[ring]++;
                    owned[ring] += item.m_nview.IsOwner() ? 1 : 0;
                    marked[ring] += MovedMarker.IsMoved(item) ? 1 : 0;
                    pullable[ring] += Pullable(player, item) ? 1 : 0;
                }
            }
            Stopwatch watch = Stopwatch.StartNew();
            int inReach = 0;
            float sqrReach = Instance.maxRadius.Value * Instance.maxRadius.Value;
            foreach (ItemDrop item in ItemDrop.s_instances)
            {
                if (item != null && (item.transform.position - feet).sqrMagnitude <= sqrReach && Pullable(player, item))
                {
                    inReach++;
                }
            }
            watch.Stop();
            context.AddString($"drops loaded: {ItemDrop.s_instances.Count}, furthest {furthest:0}m, " +
                $"simulated {ZoneSystem.instance.m_simulationDistance.NearSimulationDistance} zones of {ZoneSystem.c_ZoneSize:0}m around your zone");
            for (int ring = 0; ring < ProbeRings.Length; ring++)
            {
                context.AddString($"  <= {ProbeRings[ring]:0}m: {loaded[ring]} loaded, {owned[ring]} yours, " +
                    $"{marked[ring]} marked, {pullable[ring]} pullable, flight {ProbeRings[ring] / PullSpeed:0.0}s");
            }
            context.AddString($"one sweep at MaxRadius {Instance.maxRadius.Value:0}m: {inReach} pullable in " +
                $"{watch.Elapsed.TotalMilliseconds:0.000}ms; danger now: {Danger.Near(player)}");
        }

        private static void Unmark(Terminal context, float radius)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                context.AddString("no player");
                return;
            }
            int cleared = 0;
            foreach (ItemDrop item in ItemDrop.s_instances)
            {
                if (item != null && item.m_nview != null && item.m_nview.IsValid() && item.m_nview.IsOwner()
                    && Vector3.Distance(item.transform.position, player.transform.position) <= radius
                    && MovedMarker.IsMoved(item))
                {
                    item.m_nview.GetZDO().Set(MovedMarker.Key, false);
                    cleared++;
                }
            }
            context.AddString($"cleared the moved mark of {cleared} drops within {radius:0}m");
        }
    }
}
