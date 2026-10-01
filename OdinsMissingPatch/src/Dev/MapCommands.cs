using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OdinsMissingPatch
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships.
    internal static class MapCommands
    {
        /// <summary>
        /// omp_locations: every location of the game with what AutoPins asks of it, to check
        /// PlaceList and the dungeon rule against the current build. The whole table is written to
        /// the BepInEx log (the console only shows the tail); an argument filters by name.
        /// omp_pins lists the universal pins, omp_pins_forget clears this world's removals,
        /// omp_pins_clear takes every universal pin off the local map.
        /// </summary>
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        [Always]
        private static class Commands
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("omp_locations",
                    "[filter] lists every location: name, biome, interior, game icon, entrance text, auto pin rule",
                    args => DumpLocations(args.Context, args.Length > 1 ? args[1] : null));
                new Terminal.ConsoleCommand("omp_pins", "lists the pins that belong to nobody",
                    args => ListPins(args.Context));
                new Terminal.ConsoleCommand("omp_pins_forget",
                    "forgets which auto pins you removed in this world, so they can come back",
                    args => args.Context.AddString("forgot " + UniversalPins.ForgetDismissed() + " removed pins"));
                new Terminal.ConsoleCommand("omp_pins_clear",
                    "removes every pin that belongs to nobody from your map (not remembered as removed)",
                    args => ClearPins(args.Context));
            }
        }

        private static void DumpLocations(Terminal context, string filter)
        {
            if (ZoneSystem.instance == null)
            {
                context.AddString("no world loaded");
                return;
            }
            var lines = new List<string>();
            foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations
                .Where(l => l.m_enable).OrderBy(l => l.m_prefabName))
            {
                if (filter != null && location.m_prefabName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                string interior = "?";
                string label = "";
                string enter = "";
                string rule = "";
                GameObject prefab = LoadPrefab(location);
                Location component = prefab != null ? prefab.GetComponent<Location>() : null;
                if (component != null)
                {
                    interior = component.m_hasInterior ? "interior" : "-";
                    label = component.m_discoverLabel;
                    enter = string.Join("/", prefab.GetComponentsInChildren<Teleport>(true)
                        .Select(t => t.m_enterText).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToArray());
                    rule = AutoPins.RuleFor(component);
                }
                ReleasePrefab(location);
                bool icon = location.m_iconAlways || location.m_iconPlaced;
                lines.Add(location.m_prefabName + " | " + location.m_biome + " | " + interior +
                    (icon ? " | game icon" : "") + (label.Length > 0 ? " | label " + label : "") +
                    (enter.Length > 0 ? " | enter " + enter : "") + (rule.Length > 0 ? " | rule " + rule : ""));
            }
            foreach (string line in lines)
            {
                OdinsMissingPatchPlugin.Log.LogInfo("omp_locations: " + line);
                context.AddString(line);
            }
            context.AddString(lines.Count + " locations, also in the BepInEx log");
        }

        // ZoneLocation.m_prefab is a SoftReference<GameObject>, from an assembly setup does not
        // stage; reflection keeps this dev command from needing it.
        private static readonly FieldInfo PrefabField = AccessTools.Field(typeof(ZoneSystem.ZoneLocation), "m_prefab");

        private static GameObject LoadPrefab(ZoneSystem.ZoneLocation location)
        {
            object reference = PrefabField.GetValue(location);
            AccessTools.Method(reference.GetType(), "Load").Invoke(reference, null);
            return AccessTools.Property(reference.GetType(), "Asset").GetValue(reference, null) as GameObject;
        }

        private static void ReleasePrefab(ZoneSystem.ZoneLocation location)
        {
            object reference = PrefabField.GetValue(location);
            AccessTools.Method(reference.GetType(), "Release").Invoke(reference, null);
        }

        private static void ListPins(Terminal context)
        {
            Minimap map = Minimap.instance;
            if (map == null)
            {
                return;
            }
            int count = 0;
            foreach (Minimap.PinData pin in map.m_pins)
            {
                if (UniversalPins.TryGetCategory(pin, out UniversalPins.Category category))
                {
                    count++;
                    context.AddString(category + " " + Localization.instance.Localize(pin.m_name) +
                        " at " + pin.m_pos.ToString("0") + (pin.m_checked ? " (ticked)" : ""));
                }
            }
            context.AddString(count + " pins belong to nobody");
        }

        private static void ClearPins(Terminal context)
        {
            Minimap map = Minimap.instance;
            if (map == null)
            {
                return;
            }
            int removed = 0;
            for (int i = map.m_pins.Count - 1; i >= 0; i--)
            {
                if (UniversalPins.IsUniversal(map.m_pins[i]))
                {
                    map.RemovePin(map.m_pins[i]);
                    removed++;
                }
            }
            context.AddString("removed " + removed + " pins");
        }
    }
}
