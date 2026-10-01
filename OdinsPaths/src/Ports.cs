using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The game's own harbours, used before the mod builds a dock of its own: the Mistlands'
    /// dvergr piers (<see cref="LocationName"/>, on their edge, snapped to the water). A main road
    /// that boards or lands near one pays less for it (<see cref="Share"/>, in the search), is
    /// routed along its pier (<see cref="Splice"/>), and gets its harbour stone beside the pier's
    /// land end instead of a dock (<see cref="Harbours"/>).
    ///
    /// The location is turned by the slope under it, sampled at random when its zone is
    /// generated, so the server cannot tell which way its pier points before then. A road that
    /// might use one therefore has its zone generated first, as the game generates the zones
    /// around a player (<c>SpawnZone</c> as a ghost), and the turn is read from its crane or its
    /// guardstone (<see cref="Markers"/>). Nothing is placed in a harbour whose zone is not
    /// generated. Server only.
    /// </summary>
    internal static class Ports
    {
        public const string LocationName = "Mistlands_Harbour1";

        // The harbour's frame, read from its bundle (2026-09-27): origin the location's, at the
        // water; z out to sea (the location faces downhill); x right looking out. The pier runs
        // from its land end out to its berth, 6 m wide; the walled hut stands behind the land end,
        // its gate facing inland; the game levels the ground at the land end to 1.5 m above the
        // water, the deck's height, and dredges the berth.

        /// <summary>The pier's sea end, on its deck.</summary>
        private static readonly Vector2 BerthAt = new Vector2(0f, -0.5f);
        /// <summary>Open water off the berth, where the road's crossing sets out.</summary>
        private static readonly Vector2 OffBerth = new Vector2(0f, 6f);
        /// <summary>The pier's land end, where it meets the levelled ground.</summary>
        private static readonly Vector2 LandEndAt = new Vector2(0f, -13.5f);
        /// <summary>How far to the side a road passes the hut (its walls are 3 m out).</summary>
        private const float Aside = 6.5f;
        /// <summary>The hut's front (toward the pier) and a little past its back, where the road passes it.</summary>
        private const float HutFront = -14.5f;
        private const float HutBack = -22.5f;
        /// <summary>The harbour stone, on the side of the land end the road does not take (x mirrored to it).</summary>
        private static readonly Vector2 StoneAt = new Vector2(5f, -14f);
        /// <summary>The stone's foot above the water: the ground there is levelled to 2-2.25 m, the stone a little into it.</summary>
        private const float StoneFoot = 1.8f;

        /// <summary>The pieces the harbour's turn is read from: their prefab, their yaw in its frame and their place in it.</summary>
        private static readonly Marker[] Markers =
        {
            new Marker("dvergrtown_wood_crane", 180f, new Vector2(3f, -1.3f)),
            new Marker("dverger_guardstone", -90f, new Vector2(2f, -1.2f)),
        };
        /// <summary>How far a marker may be from where the turn read from it puts it.</summary>
        private const float MarkerSlack = 1.5f;

        /// <summary>A search step boarding or landing this close to a berth pays this share of the lump sum.</summary>
        public const float Reach = 16f;
        public const float Share = 0.25f;
        /// <summary>Harbour zones a road may have generated before its search; the nearest to its goals first.</summary>
        private const int MostGenerated = 4;
        /// <summary>Seconds a zone may take to generate before the road does without its harbour.</summary>
        private const float GiveUp = 15f;

        private struct Marker
        {
            public int Hash;
            public string Prefab;
            public float Yaw;
            public Vector2 At;

            public Marker(string prefab, float yaw, Vector2 at)
            {
                Prefab = prefab;
                Hash = prefab.GetStableHashCode();
                Yaw = yaw;
                At = at;
            }
        }

        /// <summary>A harbour of the game's whose turn is known.</summary>
        internal sealed class Port
        {
            public Vector2 Centre;
            /// <summary>Its turn about the vertical, degrees: its frame's z in the world.</summary>
            public float Yaw;
            /// <summary>Its footprint (<see cref="Footprints"/>): what a road passing through it crosses.</summary>
            public float Radius;

            public Vector2 Berth => World(BerthAt);
            public Vector2 LandEnd => World(LandEndAt);

            /// <summary>A point of its frame in the world.</summary>
            public Vector2 World(Vector2 local)
            {
                float a = Yaw * Mathf.Deg2Rad;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                // Unity's yaw turns z toward x: z' = (sin, cos), x' = (cos, -sin).
                return Centre + new Vector2(local.x * cos + local.y * sin, -local.x * sin + local.y * cos);
            }

            /// <summary>A world point in its frame.</summary>
            public Vector2 Local(Vector2 world)
            {
                Vector2 d = world - Centre;
                float a = Yaw * Mathf.Deg2Rad;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                return new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            }

            public override string ToString() => LocationName + " at " + Centre.ToString("F0") + ", facing " + Yaw.ToString("F1") + "°";
        }

        /// <summary>Every harbour read so far, by its location's zone; for the zone system it was read in.</summary>
        private static readonly Dictionary<Vector2s, Port> known = new Dictionary<Vector2s, Port>();
        private static ZoneSystem knownFor;

        /// <summary>
        /// The harbours a road between these points may use: every one whose centre is inside
        /// the search's ellipses, read if its zone is generated; of the rest the ones nearest a
        /// goal have their zones generated first (<see cref="MostGenerated"/>).
        /// </summary>
        public static IEnumerator Prepare(List<Vector2> starts, List<Vector2> goals, Action<string> report, Action<List<Port>> done)
        {
            return Prepare(p => Inside(p, starts, goals), goals, report, done);
        }

        /// <summary>The harbours whose centres pass the test; the zones of the ones nearest the points given are generated first.</summary>
        public static IEnumerator Prepare(Func<Vector2, bool> inside, List<Vector2> goals, Action<string> report, Action<List<Port>> done)
        {
            List<Port> ports = new List<Port>();
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || goals.Count == 0)
            {
                done(ports);
                yield break;
            }
            Forget(zones);
            List<ZoneSystem.LocationInstance> due = new List<ZoneSystem.LocationInstance>();
            foreach (KeyValuePair<Vector2s, ZoneSystem.LocationInstance> entry in zones.m_locationInstances)
            {
                ZoneSystem.LocationInstance instance = entry.Value;
                if (instance.m_location == null || instance.m_location.m_prefabName != LocationName)
                {
                    continue;
                }
                Vector2 centre = Flat(instance.m_position);
                if (!inside(centre))
                {
                    continue;
                }
                if (known.TryGetValue(entry.Key, out Port port))
                {
                    ports.Add(port);
                }
                else
                {
                    due.Add(instance);
                }
            }
            due.Sort((a, b) => Nearest(Flat(a.m_position), goals).CompareTo(Nearest(Flat(b.m_position), goals)));
            int generated = 0;
            foreach (ZoneSystem.LocationInstance instance in due)
            {
                Vector2s zone = ZoneSystem.GetZone(instance.m_position);
                if (!zones.IsZoneGenerated(zone))
                {
                    if (generated >= MostGenerated)
                    {
                        continue;
                    }
                    generated++;
                    float since = Time.realtimeSinceStartup;
                    while (!zones.IsZoneGenerated(zone) && !zones.SpawnZone(zone, ZoneSystem.SpawnMode.Ghost, out GameObject _))
                    {
                        if (Time.realtimeSinceStartup - since > GiveUp || ZoneSystem.instance != zones)
                        {
                            break;
                        }
                        yield return null;
                    }
                    if (!zones.IsZoneGenerated(zone))
                    {
                        report?.Invoke("The harbour at " + Flat(instance.m_position).ToString("F0") + " did not generate in " + GiveUp.ToString("F0") + " s; left out.");
                        continue;
                    }
                    Debug.Log("[OdinsPaths] Generated the zone of the harbour at " + Flat(instance.m_position).ToString("F0") + " in "
                        + ((Time.realtimeSinceStartup - since) * 1000f).ToString("F0") + " ms.");
                }
                Port read = Read(instance, zone);
                if (read != null)
                {
                    known[zone] = read;
                    ports.Add(read);
                }
            }
            if (ports.Count > 0)
            {
                report?.Invoke(ports.Count + " of the game's harbours on the way: " + string.Join("; ", ports.ConvertAll(p => p.ToString())));
            }
            done(ports);
        }

        /// <summary>The harbour whose footprint holds this point (a landing's shore), among the ones read.</summary>
        public static Port At(Vector2 p)
        {
            Forget(ZoneSystem.instance);
            foreach (Port port in known.Values)
            {
                if ((port.Centre - p).sqrMagnitude < port.Radius * port.Radius)
                {
                    return port;
                }
            }
            return null;
        }

        /// <summary>
        /// A route that comes ashore from the sea (or sets out to it) through a harbour's
        /// footprint is laid along its pier instead: from open water off the berth, down the pier to
        /// its land end, and round the hut to where the route left the footprint. Costs are spread
        /// evenly over the new points. How many harbours the route was led through.
        /// </summary>
        public static int Splice(List<Vector2> route, List<float> costs, List<Port> ports)
        {
            if (ports == null || route.Count < 3)
            {
                return 0;
            }
            int spliced = 0;
            foreach (Port port in ports)
            {
                float r2 = port.Radius * port.Radius;
                int a = route.FindIndex(p => (p - port.Centre).sqrMagnitude < r2);
                if (a <= 0)
                {
                    continue;
                }
                int b = a;
                while (b + 1 < route.Count && (route[b + 1] - port.Centre).sqrMagnitude < r2)
                {
                    b++;
                }
                if (b + 1 >= route.Count)
                {
                    continue;
                }
                // Only a crossing: one side of the stretch at sea, the other on land.
                bool arriving = IsSea(route[a - 1]);
                if (arriving == IsSea(route[b + 1]))
                {
                    continue;
                }
                List<Vector2> path = Walk(port, arriving ? route[b + 1] : route[a - 1]);
                if (!arriving)
                {
                    path.Reverse();
                }
                float before = costs[a - 1];
                float after = costs[b + 1];
                List<float> spread = new List<float>(path.Count);
                for (int i = 0; i < path.Count; i++)
                {
                    spread.Add(Mathf.Lerp(before, after, (i + 1f) / (path.Count + 1f)));
                }
                route.RemoveRange(a, b - a + 1);
                route.InsertRange(a, path);
                costs.RemoveRange(a, b - a + 1);
                costs.InsertRange(a, spread);
                spliced++;
            }
            return spliced;
        }

        /// <summary>From the water off the berth to the land end, and round the hut toward the point the road goes on from.</summary>
        private static List<Vector2> Walk(Port port, Vector2 inland)
        {
            Vector2 local = port.Local(inland);
            float side = local.x >= 0f ? 1f : -1f;
            List<Vector2> path = new List<Vector2>
            {
                port.World(OffBerth),
                port.Berth,
                port.LandEnd,
                port.World(new Vector2(side * Aside, HutFront)),
            };
            if (local.y < HutFront - 3f)
            {
                path.Add(port.World(new Vector2(side * Aside, HutBack)));
            }
            return path;
        }

        /// <summary>
        /// The harbour stone of a road through this harbour: beside the pier's land end, on the
        /// side the road does not pass the hut, facing the pier.
        /// </summary>
        public static void Stone(Port port, Trail trail, out Vector3 position, out Quaternion rotation)
        {
            // The road's side: the one of the two ways round the hut its trail comes nearer.
            float right = Nearest(port.World(new Vector2(Aside, HutFront)), trail.Points);
            float left = Nearest(port.World(new Vector2(-Aside, HutFront)), trail.Points);
            float side = right <= left ? -1f : 1f;
            Vector2 at = port.World(new Vector2(side * StoneAt.x, StoneAt.y));
            position = new Vector3(at.x, ZoneSystem.instance.m_waterLevel + StoneFoot, at.y);
            Vector2 toPier = port.LandEnd - at;
            rotation = Quaternion.LookRotation(new Vector3(toPier.x, 0f, toPier.y));
        }

        /// <summary>How far inland of a landing's shore point, along its trail, the pier's land end lies.</summary>
        public static float LandEndAlong(Port port, Trail trail, Landings.Landing landing)
        {
            int step = landing.Toward < landing.Shore ? 1 : -1;
            int best = landing.Shore;
            float nearest = float.MaxValue;
            for (int i = landing.Shore; i >= 0 && i < trail.Points.Count; i += step)
            {
                float d = (trail.Points[i] - port.LandEnd).sqrMagnitude;
                if (d < nearest)
                {
                    nearest = d;
                    best = i;
                }
            }
            return Mathf.Abs(best - landing.Shore) * Trail.Spacing;
        }

        /// <summary>The harbour's turn from its crane or guardstone, among the objects of its zone and the ones around.</summary>
        private static Port Read(ZoneSystem.LocationInstance instance, Vector2s zone)
        {
            Vector2 centre = Flat(instance.m_position);
            float radius = Mathf.Max(Footprints.Radius(instance.m_location), instance.m_location.m_exteriorRadius);
            List<ZDO> objects = new List<ZDO>();
            HashSet<ZoneSystem.SectorIndex> visited = new HashSet<ZoneSystem.SectorIndex>();
            for (int x = zone.x - 1; x <= zone.x + 1; x++)
            {
                for (int y = zone.y - 1; y <= zone.y + 1; y++)
                {
                    ZDOMan.instance.FindObjects(new Vector2s(x, y), objects, visited);
                }
            }
            foreach (Marker marker in Markers)
            {
                foreach (ZDO zdo in objects)
                {
                    if (zdo.GetPrefab() != marker.Hash || (Flat(zdo.GetPosition()) - centre).sqrMagnitude > radius * radius)
                    {
                        continue;
                    }
                    float yaw = Mathf.Round((zdo.GetRotation().eulerAngles.y - marker.Yaw) / 22.5f) * 22.5f;
                    Port port = new Port { Centre = centre, Yaw = Mathf.Repeat(yaw, 360f), Radius = radius };
                    if ((port.World(marker.At) - Flat(zdo.GetPosition())).sqrMagnitude <= MarkerSlack * MarkerSlack)
                    {
                        return port;
                    }
                }
            }
            Debug.LogWarning("[OdinsPaths] The harbour at " + centre.ToString("F0") + " has neither crane nor guardstone where they belong; roads do without it.");
            return null;
        }

        private static void Forget(ZoneSystem zones)
        {
            if (knownFor != zones)
            {
                known.Clear();
                knownFor = zones;
            }
        }

        /// <summary>Inside the ellipse between some start and some goal, as the search draws them.</summary>
        private static bool Inside(Vector2 p, List<Vector2> starts, List<Vector2> goals)
        {
            foreach (Vector2 goal in goals)
            {
                foreach (Vector2 start in starts)
                {
                    if (Vector2.Distance(p, start) + Vector2.Distance(p, goal) <= PathSearch.EllipseLimit(start, goal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static float Nearest(Vector2 p, List<Vector2> points)
        {
            float best = float.MaxValue;
            foreach (Vector2 q in points)
            {
                best = Mathf.Min(best, (q - p).sqrMagnitude);
            }
            return best;
        }

        private static bool IsSea(Vector2 p)
        {
            return Ground.Height(p.x, p.y) < ZoneSystem.instance.m_waterLevel - PathSearch.FordDepth;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
    }
}
