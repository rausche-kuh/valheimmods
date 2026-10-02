using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// How a location stands: its turn, known before its zone generates. The game turns a location
    /// to the slope, or at random with an unseeded draw as it places the zone
    /// (<c>ZoneSystem.PlaceLocations</c>), the zone's first draw - so the server, which alone
    /// generates zones, seeds that draw from the world and the zone first (<see cref="SeedTurn"/>,
    /// the user's choice, 2026-10-02): the same even spread of turns, now one a road can be laid to.
    /// A location generated before reads its turn from its <c>LocationProxy</c>.
    /// </summary>
    internal static class Approaches
    {
        private static readonly int TurnSalt = "OdinsPaths_Turn".GetStableHashCode();

        /// <summary>How far straight a road runs in before it reaches a location's footprint: laid out to arrive, not to dodge.</summary>
        private const float Straight = 10f;
        /// <summary>Half the lane that has to be clear between a location's solid things.</summary>
        private const float LaneHalf = 1.5f;
        /// <summary>A solid thing this close to the centre is the altar itself; the road stops this far before it.</summary>
        private const float Centre = 3f;
        private const float AltarKeep = 1.5f;
        /// <summary>The bearings a lane between solid things is looked for at.</summary>
        private const int Bearings = 64;
        /// <summary>A road overlaps a door's own way in by this much, so the two meet.</summary>
        private const float Overlap = 1f;

        /// <summary>
        /// The straight way into a location (the user, 2026-10-02: a road through one of the
        /// Eikthyr altar's stones, another stopping short of a burial chamber's corridor or coming
        /// at it from behind). The search goes to <see cref="Outer"/>, outside the location and far
        /// enough out to arrive straight; the road runs on to <see cref="Inner"/>, its last
        /// <see cref="Dirt"/> metres dirt.
        /// </summary>
        internal struct Lane
        {
            public Vector2 Outer;
            public Vector2 Inner;
            public float Dirt;
            /// <summary>Into a door's corridor, or between solid things to the centre.</summary>
            public bool Door;
            /// <summary>The location's centre, and the ground it levels itself: a road levels up to that, not to the location's edge.</summary>
            public Vector2 Centre;
            public List<Circle> Flats;
        }

        /// <summary>
        /// The lane into the location at centre for a road coming from the given side: out of its
        /// door along the corridor, the road meeting its far end; else through the gap between
        /// its solid things nearest that side, along the gap's middle, stopping before the altar,
        /// dirt from the ring inward. False for no location, no known turn, or no gap. A road out
        /// of a location (the sacrificial stones) takes the same lane the other way.
        /// </summary>
        public static bool LaneTo(Vector2 centre, Vector2 from, out Lane lane)
        {
            lane = default;
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.m_locationInstances.TryGetValue(ZoneSystem.GetZone(new Vector3(centre.x, 0f, centre.y)), out ZoneSystem.LocationInstance instance)
                || instance.m_location == null || (new Vector2(instance.m_position.x, instance.m_position.z) - centre).sqrMagnitude > 1f
                || !Turn(instance, out float yaw))
            {
                return false;
            }
            Footprints.Layout layout = Footprints.LayoutOf(instance.m_location);
            float radius = Footprints.Radius(instance.m_location);
            lane.Centre = centre;
            lane.Flats = layout.Flats.ConvertAll(f => new Circle { Center = World(centre, yaw, new Vector2(f.x, f.y)), Radius = f.z });
            if (layout.HasDoor)
            {
                Vector2 door = World(centre, yaw, layout.Door);
                Vector2 way = World(Vector2.zero, yaw, layout.Out);
                lane.Door = true;
                lane.Inner = door + way * Mathf.Max(layout.Corridor - Overlap, 0f);
                lane.Outer = Along(centre, lane.Inner, way, radius + Straight);
                lane.Dirt = Mathf.Max(0f, Vector2.Distance(lane.Inner, Along(centre, lane.Inner, way, radius)));
                return true;
            }
            List<Vector3> solids = new List<Vector3>();
            float keep = 0f;
            float ring = 0f;
            foreach (Vector3 solid in layout.Solids)
            {
                float distance = new Vector2(solid.x, solid.y).magnitude;
                if (distance < Centre)
                {
                    keep = Mathf.Max(keep, distance + solid.z + AltarKeep);
                    continue;
                }
                ring = Mathf.Max(ring, distance + solid.z);
                Vector2 at = World(Vector2.zero, yaw, new Vector2(solid.x, solid.y));
                solids.Add(new Vector3(at.x, at.y, solid.z));
            }
            keep = Mathf.Max(keep, AltarKeep);
            float outer = radius + Straight;
            bool[] clear = new bool[Bearings];
            bool any = false;
            for (int b = 0; b < Bearings; b++)
            {
                Vector2 way = Bearing(b);
                clear[b] = solids.TrueForAll(s => Distance(new Vector2(s.x, s.y), way * keep, way * outer) >= s.z + LaneHalf);
                any |= clear[b];
            }
            if (!any)
            {
                return false;
            }
            // Each run of clear bearings is a gap; the one nearest the road's side, at its middle.
            Vector2 toward = (from - centre).normalized;
            int best = -1;
            float bestAngle = float.MaxValue;
            bool all = System.Array.TrueForAll(clear, c => c);
            for (int b = 0; b < Bearings; b++)
            {
                if (!clear[b] || (!all && clear[(b + Bearings - 1) % Bearings]))
                {
                    continue;
                }
                int length = 1;
                while (length < Bearings && clear[(b + length) % Bearings])
                {
                    length++;
                }
                int middle = all ? Nearest(toward) : (b + length / 2) % Bearings;
                float angle = Vector2.Angle(Bearing(middle), toward);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = middle;
                }
                if (all)
                {
                    break;
                }
            }
            Vector2 chosen = Bearing(best);
            lane.Inner = centre + chosen * keep;
            lane.Outer = centre + chosen * outer;
            lane.Dirt = Mathf.Max(0f, (ring > 0f ? ring : radius) - keep);
            return true;
        }

        private static Vector2 Bearing(int b)
        {
            float angle = b * 2f * Mathf.PI / Bearings;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        private static int Nearest(Vector2 way) => ((Mathf.RoundToInt(Mathf.Atan2(way.x, way.y) / (2f * Mathf.PI) * Bearings) % Bearings) + Bearings) % Bearings;

        /// <summary>From start along way, the first point at least distance from centre.</summary>
        private static Vector2 Along(Vector2 centre, Vector2 start, Vector2 way, float distance)
        {
            // |start + t way - centre| = distance, the larger root.
            Vector2 d = start - centre;
            float b = Vector2.Dot(d, way);
            float c = d.sqrMagnitude - distance * distance;
            float t = -b + Mathf.Sqrt(Mathf.Max(0f, b * b - c));
            return start + way * Mathf.Max(t, 0f);
        }

        /// <summary>The distance from p to the segment a..b.</summary>
        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        private static int TurnSeed(Vector2s zone) => WorldGenerator.instance.GetSeed() ^ (TurnSalt + zone.x * 7919 + zone.y * 104729);

        /// <summary>
        /// The turn, in degrees, of the location instance: as its proxy stands once generated; else
        /// as the game will turn it - to the slope (<see cref="SlopeYaw"/>), by the seeded draw, or
        /// not at all. Main thread: it borrows Unity's random state.
        /// </summary>
        public static bool Turn(ZoneSystem.LocationInstance instance, out float yaw)
        {
            yaw = 0f;
            ZoneSystem.ZoneLocation location = instance.m_location;
            if (location == null)
            {
                return false;
            }
            if (instance.m_placed)
            {
                return ProxyYaw(instance.m_position, out yaw);
            }
            Vector2 centre = new Vector2(instance.m_position.x, instance.m_position.z);
            if (location.m_slopeRotation)
            {
                return SlopeYaw(centre, location, out yaw);
            }
            if (location.m_randomRotation)
            {
                Random.State saved = Random.state;
                Random.InitState(TurnSeed(ZoneSystem.GetZone(instance.m_position)));
                yaw = Random.Range(0, 16) * 22.5f;
                Random.state = saved;
            }
            return true;
        }

        /// <summary>A point in the location's own frame, in the world, the instance turned by yaw.</summary>
        public static Vector2 World(Vector2 centre, float yaw, Vector2 local)
        {
            Vector3 turned = Quaternion.Euler(0f, yaw, 0f) * new Vector3(local.x, 0f, local.y);
            return centre + new Vector2(turned.x, turned.z);
        }

        /// <summary>The turn of the generated location at position, read from its proxy's ZDO.</summary>
        private static bool ProxyYaw(Vector3 position, out float yaw)
        {
            yaw = 0f;
            GameObject proxy = ZoneSystem.instance.m_locationProxyPrefab;
            if (proxy == null || ZDOMan.instance == null)
            {
                return false;
            }
            int hash = proxy.name.GetStableHashCode();
            List<ZDO> objects = new List<ZDO>();
            ZDOMan.instance.FindObjects(ZoneSystem.GetZone(position), objects, new HashSet<ZoneSystem.SectorIndex>());
            foreach (ZDO zdo in objects)
            {
                Vector3 at = zdo.GetPosition();
                if (zdo.GetPrefab() == hash && new Vector2(at.x - position.x, at.z - position.z).sqrMagnitude < 1f)
                {
                    yaw = zdo.GetRotation().eulerAngles.y;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The turn, in degrees, the game gives a location turned to the slope: downhill, from the
        /// highest to the lowest ground within its radius, snapped to 22.5 degrees. The game looks
        /// at ten random points; this at two rings of sixteen.
        /// </summary>
        public static bool SlopeYaw(Vector2 goal, ZoneSystem.ZoneLocation location, out float yaw)
        {
            yaw = 0f;
            float radius = location.m_exteriorRadius;
            Vector2 high = goal;
            Vector2 low = goal;
            float highest = float.MinValue;
            float lowest = float.MaxValue;
            for (int ring = 1; ring <= 2; ring++)
            {
                for (int k = 0; k < 16; k++)
                {
                    float angle = k * Mathf.PI / 8f;
                    Vector2 p = goal + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * ring * 0.5f;
                    float h = Ground.Height(p.x, p.y);
                    if (h > highest)
                    {
                        highest = h;
                        high = p;
                    }
                    if (h < lowest)
                    {
                        lowest = h;
                        low = p;
                    }
                }
            }
            Vector2 downhill = low - high;
            if (downhill.sqrMagnitude < 0.01f)
            {
                return false;
            }
            // Unity's yaw: 0 along +z, clockwise seen from above.
            yaw = Mathf.Round(Mathf.Atan2(downhill.x, downhill.y) * Mathf.Rad2Deg / 22.5f) * 22.5f;
            return true;
        }

        /// <summary>
        /// Seeds the random turn of the location a zone is about to place, server side (only the
        /// server generates zones: a client's are <c>SpawnMode.Client</c>). The draw comes first in
        /// <c>PlaceLocations</c>; the spawn and the vegetation seed their own draws after it.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), "PlaceLocations")]
        public static class SeedTurn
        {
            private static void Prefix(ZoneSystem __instance, Vector2s zoneID)
            {
                if (__instance.m_locationInstances.TryGetValue(zoneID, out ZoneSystem.LocationInstance instance) && !instance.m_placed
                    && instance.m_location != null && !instance.m_location.m_slopeRotation && instance.m_location.m_randomRotation)
                {
                    Random.InitState(TurnSeed(zoneID));
                }
            }
        }
    }
}
