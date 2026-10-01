using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The traders at the end of their roads (docs/network.md). A trader's location is
    /// <c>m_unique</c>: the game places it in the first of its camps whose zone generates and then
    /// drops the others. A road goes to a camp before that, so once it is laid the trader is
    /// settled there - every other camp not placed yet is dropped now, as the game would - and the
    /// camp's icon goes on every map at once, not only after its zone generates. With
    /// <c>[Network] RevealTraders</c> off no road goes to a trader and nothing here runs. Server only.
    /// </summary>
    internal static class Traders
    {
        /// <summary>Whether a job or a pin by this name is a trader's.</summary>
        public static bool Is(string name)
        {
            foreach (string part in OdinsPathsPlugin.Traders.Value.Split(','))
            {
                if (part.Trim() == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Drops every camp of a pinned trader other than the pinned one, unless the trader is
        /// placed already (elsewhere: the planner lays a new road), and sends every client the
        /// location icons. Run after a trader's road is laid and before each growth - the game may
        /// make new camps when it regenerates locations after an update.
        /// </summary>
        public static void Settle(Network network)
        {
            if (!OdinsPathsPlugin.RevealTraders.Value || network == null || ZoneSystem.instance == null
                || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            ZoneSystem zones = ZoneSystem.instance;
            bool pinned = false;
            foreach (KeyValuePair<string, Vector2> pin in network.Pinned)
            {
                if (!Is(pin.Key))
                {
                    continue;
                }
                ZoneSystem.ZoneLocation location = null;
                bool placed = false;
                bool there = false;
                List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
                zones.FindLocations(pin.Key, ref instances);
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    location = instance.m_location;
                    placed |= instance.m_placed;
                    there |= At(instance, pin.Value);
                }
                if (location == null || placed || !there)
                {
                    continue;
                }
                pinned = true;
                List<Vector2s> others = new List<Vector2s>();
                foreach (KeyValuePair<Vector2s, ZoneSystem.LocationInstance> entry in zones.m_locationInstances)
                {
                    if (entry.Value.m_location == location && !entry.Value.m_placed && !At(entry.Value, pin.Value))
                    {
                        others.Add(entry.Key);
                    }
                }
                foreach (Vector2s zone in others)
                {
                    zones.m_locationInstances.Remove(zone);
                }
                foreach (string cache in Caches)
                {
                    Drop(AccessTools.Field(typeof(ZoneSystem), cache).GetValue(zones) as IDictionary, location, pin.Value);
                }
                if (others.Count > 0)
                {
                    Debug.Log("[OdinsPaths] " + pin.Key + " settled at " + pin.Value.ToString("F0") + ": " + others.Count + " other camps dropped.");
                }
            }
            if (pinned)
            {
                zones.SendLocationIcons(0L);
            }
        }

        /// <summary>
        /// The game's lists of location instances besides <c>m_locationInstances</c>, which its
        /// <c>RemoveUnplacedLocations</c> trims too. Read by name: one is keyed by a type of an
        /// assembly the build does not reference.
        /// </summary>
        private static readonly string[] Caches = { "m_locationIDCache", "m_locationGroupCache", "m_locationMaxGroupCache" };

        /// <summary>The game's <c>RemoveNonPlaced</c> for one location, keeping the instance at the pin.</summary>
        private static void Drop(IDictionary lists, ZoneSystem.ZoneLocation location, Vector2 pin)
        {
            if (lists == null)
            {
                return;
            }
            foreach (object value in lists.Values)
            {
                (value as List<ZoneSystem.LocationInstance>)?.RemoveAll(instance => instance.m_location == location && !instance.m_placed && !At(instance, pin));
            }
        }

        private static bool At(ZoneSystem.LocationInstance instance, Vector2 pin)
        {
            return (new Vector2(instance.m_position.x, instance.m_position.z) - pin).sqrMagnitude < 1f;
        }

        /// <summary>
        /// A pinned trader's camp has its icon before it is placed. The server's list is what
        /// <c>SendLocationIcons</c> sends, so clients without the mod see it too; once the zone
        /// generates, the vanilla entry is the same.
        /// </summary>
        public static void AddIcons(Dictionary<Vector3, string> icons)
        {
            Network network = Network.Current;
            if (!OdinsPathsPlugin.RevealTraders.Value || network == null)
            {
                return;
            }
            List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
            foreach (KeyValuePair<string, Vector2> pin in network.Pinned)
            {
                if (!Is(pin.Key) || !ZoneSystem.instance.FindLocations(pin.Key, ref instances))
                {
                    continue;
                }
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    if (!instance.m_placed && instance.m_location.m_iconPlaced && At(instance, pin.Value))
                    {
                        icons[instance.m_position] = pin.Key;
                    }
                }
            }
        }
    }

    public partial class OdinsPathsPlugin
    {
        /// <summary>The server's location icons: a pinned trader's camp too (<see cref="Traders.AddIcons"/>).</summary>
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.GetLocationIcons))]
        public static class TraderIcons
        {
            private static void Postfix(Dictionary<Vector3, string> icons)
            {
                if (ZNet.instance != null && ZNet.instance.IsServer() && ZoneSystem.instance != null)
                {
                    OdinsPaths.Traders.AddIcons(icons);
                }
            }
        }
    }
}
