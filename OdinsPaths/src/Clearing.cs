using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Takes the trees and rocks the world generated off a path, as their ZDOs: gone for every
    /// player, no drops, no stumps. Server only - the server owns every ZDO's fate.
    ///
    /// Two moments: right after a path is laid, for the zones that already exist (measured
    /// against the trail itself, and never near something a player built - a grown sapling is
    /// the same prefab as a wild tree); and whenever the server generates a zone later, for the
    /// stretch of path already written into it (measured against the dirt and stone in the zone's
    /// terrain data - a zone generated just now holds no player's work, so all its paint is a road's).
    ///
    /// Only what <c>ZoneSystem.m_vegetation</c> places is touched, and of that only trees, logs,
    /// rocks and bushes that give nothing but wood, stone, flint or resin, and the scenery nobody
    /// can break - the Mistlands' cliffs and rocks, the giant roots, the biggest boulders -, which
    /// a road otherwise ran into wherever its zone was generated after the road was laid (seen
    /// in game 2026-09-25): ore, nests and pickables stay. Nothing inside a location is touched.
    /// A tree goes if its trunk stands on the path; a rock or bush if any of it reaches onto the
    /// path, measured as the boxes of its meshes, turned and scaled as it was placed - a boulder
    /// the path runs through goes, however big, and a long cliff beside it stays.
    /// </summary>
    internal static class Clearing
    {
        /// <summary>A trunk this far past the path's edge still goes.</summary>
        private const float Trunk = 1f;
        /// <summary>A rock or bush without meshes to measure counts as this wide from its centre.</summary>
        private const float Unmeasured = 2.5f;
        /// <summary>Vegetation this close to a player-built piece stays: it may have been planted.</summary>
        private const float WorkRadius = 10f;
        /// <summary>A texel with this much dirt or stone counts as road.</summary>
        private const float RoadPaint = 0.3f;
        private const float MinLocationRadius = 8f;
        /// <summary>How far round a harbour building's pieces a zone generated later is cleared.</summary>
        private const float BuildingClear = 1.5f;
        /// <summary>At most this many zones a frame, and more than one only while the frame's budget lasts.</summary>
        private const int ZonesPerFrame = 4;

        private static readonly HashSet<string> PlainDrops = new HashSet<string>
        {
            "Wood", "FineWood", "RoundLog", "Stone", "Flint", "Resin",
        };

        private struct Kind
        {
            /// <summary>How far past its centre it reaches, at scale 1.</summary>
            public float Reach;
            /// <summary>Whether its reach grows with the scale it was placed at (not a trunk's).</summary>
            public bool Scales;
            /// <summary>The prefab's own scale, for a ZDO that stores none.</summary>
            public float PrefabScale;
            /// <summary>Its meshes' footprints in its own unscaled space; null for a trunk or an unmeasured rock.</summary>
            public List<Box> Boxes;
        }

        /// <summary>A mesh's bounds seen from above, in the prefab's own space: centre and half size on x and z.</summary>
        private struct Box
        {
            public Vector2 Center;
            public Vector2 Half;
        }

        /// <summary>Clearable vegetation prefab hash -> its kind; built once per world.</summary>
        private static Dictionary<int, Kind> clearable;
        /// <summary>The farthest any clearable vegetation reaches, at its largest scale.</summary>
        private static float maxReach;
        private static ZoneSystem clearableFor;

        internal sealed class Result
        {
            public int Cleared;
            /// <summary>On the path, but left for being near a player's building.</summary>
            public int Kept;
        }

        /// <summary>The zones that already exist along a freshly laid trail.</summary>
        public static IEnumerator Clear(Trail trail, Result result)
        {
            Clearable();
            Dictionary<Vector2s, List<int>> zones = TerrainWriter.ZonesNear(trail, trail.Kind.MaxHalfWidth + maxReach);
            // A zone is a look at each of its objects and the pieces around it, and every tree
            // taken from a loaded zone is a GameObject destroyed.
            float budget = OdinsPathsPlugin.SearchBudgetMs.Value;
            System.Diagnostics.Stopwatch frame = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;
            int inFrame = 0;
            foreach (KeyValuePair<Vector2s, List<int>> entry in zones)
            {
                List<int> segments = entry.Value;
                ClearZone(entry.Key, (at, reach) => TrailDistance(trail, segments, at) < trail.Kind.HalfWidthAt(at) + reach, true, result);
                Progress.Set((float)++count / zones.Count);
                if (++inFrame == ZonesPerFrame || frame.Elapsed.TotalMilliseconds > budget)
                {
                    yield return null;
                    frame.Restart();
                    inFrame = 0;
                }
            }
        }

        /// <summary>
        /// The trees and rocks on a harbour building (points over its footprint, and radius round
        /// them), in the zones that exist already; a zone generated later is cleared of them by
        /// <see cref="ClearNewZone"/>, from the building's pieces. How many went.
        /// </summary>
        public static int ClearAround(List<Vector2> points, float radius)
        {
            if (points.Count == 0)
            {
                return 0;
            }
            Clearable();
            HashSet<Vector2s> zones = new HashSet<Vector2s>();
            float reach = radius + maxReach;
            foreach (Vector2 p in points)
            {
                for (int k = 0; k < 4; k++)
                {
                    zones.Add(ZoneSystem.GetZone(new Vector3(p.x + ((k & 1) == 0 ? -reach : reach), 0f, p.y + ((k & 2) == 0 ? -reach : reach))));
                }
            }
            Result result = new Result();
            foreach (Vector2s zone in zones)
            {
                if (ZoneSystem.instance.IsZoneGenerated(zone))
                {
                    ClearZone(zone, (at, r) => Near(points, at, radius + r), false, result);
                }
            }
            return result.Cleared;
        }

        /// <summary>A zone the server has just generated, if a path or a harbour building is in it.</summary>
        public static void ClearNewZone(Vector2s zone)
        {
            List<Vector2> buildings = new List<Vector2>();
            foreach (ZDO zdo in Objects(zone))
            {
                if (zdo.GetBool(Builder.BuildingKey))
                {
                    Vector3 p = zdo.GetPosition();
                    buildings.Add(new Vector2(p.x, p.z));
                }
            }
            if (buildings.Count > 0)
            {
                Clearable();
                Result around = new Result();
                ClearZone(zone, (at, r) => Near(buildings, at, BuildingClear + r), false, around);
            }
            ZDO compiler = TerrainWriter.FindCompiler(zone);
            byte[] bytes = compiler != null ? compiler.GetByteArray(ZDOVars.s_TCData) : null;
            if (bytes == null)
            {
                return;
            }
            Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
            int pitch = prefabMap.m_width + 1;
            float scale = prefabMap.m_scale;
            TerrainWriter.TerrainData terrain = TerrainWriter.TerrainData.Decode(bytes, pitch);
            if (terrain == null)
            {
                return;
            }
            Vector3 center = ZoneSystem.GetZonePos(zone);
            float origin = -prefabMap.m_width * scale * 0.5f;
            // Texel x covers the metre from vertex x to vertex x + 1 (see TerrainWriter).
            Vector2 firstTexel = new Vector2(center.x + origin, center.z + origin) + new Vector2(0.5f, 0.5f) * scale;
            Result result = new Result();
            ClearZone(zone, (at, reach) => NearRoad(terrain, pitch, scale, firstTexel, at, reach), false, result);
            if (result.Cleared > 0)
            {
                Debug.Log("[OdinsPaths] Zone " + zone + " generated: cleared " + result.Cleared + " trees and rocks off the path.");
            }
        }

        private static void ClearZone(Vector2s zone, Func<Vector2, float, bool> onPath, bool spareWork, Result result)
        {
            Dictionary<int, Kind> kinds = Clearable();
            List<ZDO> doomed = new List<ZDO>();
            List<Vector2> work = null;
            foreach (ZDO zdo in Objects(zone))
            {
                if (!kinds.TryGetValue(zdo.GetPrefab(), out Kind kind))
                {
                    continue;
                }
                Vector3 p = zdo.GetPosition();
                Vector2 at = new Vector2(p.x, p.z);
                float scale = kind.Scales ? ScaleOf(zdo, kind.PrefabScale) : 1f;
                if (!onPath(at, kind.Reach * scale) || (kind.Boxes != null && !BoxesOnPath(kind.Boxes, zdo, scale, onPath)) || InLocation(at))
                {
                    continue;
                }
                if (spareWork)
                {
                    if (work == null)
                    {
                        work = PlayerWork(zone);
                    }
                    if (Near(work, at, WorkRadius))
                    {
                        result.Kept++;
                        continue;
                    }
                }
                doomed.Add(zdo);
            }
            // The server may take any ZDO over; DestroyZDO only sends what its owner destroys.
            foreach (ZDO zdo in doomed)
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
                result.Cleared++;
            }
        }

        /// <summary>
        /// Whether any of its boxes, turned and scaled as it stands, reaches onto the path: each
        /// box covered by a row of discs along its long side, each disc asked as a small rock.
        /// </summary>
        private static bool BoxesOnPath(List<Box> boxes, ZDO zdo, float scale, Func<Vector2, float, bool> onPath)
        {
            Vector3 p = zdo.GetPosition();
            Quaternion rotation = zdo.GetRotation();
            foreach (Box box in boxes)
            {
                bool alongX = box.Half.x >= box.Half.y;
                float longHalf = alongX ? box.Half.x : box.Half.y;
                float shortHalf = Mathf.Max(alongX ? box.Half.y : box.Half.x, 0.1f);
                int discs = Mathf.Clamp(Mathf.CeilToInt(longHalf / shortHalf), 1, 8);
                float cell = longHalf / discs;
                float radius = Mathf.Sqrt(cell * cell + shortHalf * shortHalf) * scale;
                for (int i = 0; i < discs; i++)
                {
                    float offset = -longHalf + cell * (2 * i + 1);
                    Vector2 local = box.Center + (alongX ? new Vector2(offset, 0f) : new Vector2(0f, offset));
                    Vector3 world = p + rotation * (new Vector3(local.x, 0f, local.y) * scale);
                    if (onPath(new Vector2(world.x, world.z), radius))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The scale it was placed at, as <c>ZNetView.Awake</c> reads it back.</summary>
        internal static float ScaleOf(ZDO zdo, float prefabScale)
        {
            Vector3 scale = zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
            if (scale != Vector3.zero)
            {
                return Mathf.Max(scale.x, scale.z);
            }
            return zdo.GetFloat(ZDOVars.s_scaleScalarHash, prefabScale);
        }

        private static float TrailDistance(Trail trail, List<int> segments, Vector2 at)
        {
            float best = float.MaxValue;
            foreach (int segment in segments)
            {
                best = Mathf.Min(best, trail.Nearest(at, segment, out float _));
            }
            return best;
        }

        private static bool NearRoad(TerrainWriter.TerrainData terrain, int pitch, float scale, Vector2 firstTexel,
            Vector2 at, float radius)
        {
            float fx = (at.x - firstTexel.x) / scale;
            float fy = (at.y - firstTexel.y) / scale;
            float r = radius / scale;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(fx - r));
            int x1 = Mathf.Min(pitch - 1, Mathf.CeilToInt(fx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(fy - r));
            int y1 = Mathf.Min(pitch - 1, Mathf.CeilToInt(fy + r));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int index = y * pitch + x;
                    if (terrain.ModifiedPaint[index] && (terrain.Paint[index].r > RoadPaint || terrain.Paint[index].b > RoadPaint)
                        && (x - fx) * (x - fx) + (y - fy) * (y - fy) <= r * r)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static List<ZDO> Objects(Vector2s zone)
        {
            List<ZDO> objects = new List<ZDO>();
            ZDOMan.instance.FindObjects(zone, objects, new HashSet<ZoneSystem.SectorIndex>());
            return objects;
        }

        /// <summary>Where the pieces players built stand, in the zone and around it.</summary>
        private static List<Vector2> PlayerWork(Vector2s zone)
        {
            List<Vector2> work = new List<Vector2>();
            for (int x = zone.x - 1; x <= zone.x + 1; x++)
            {
                for (int y = zone.y - 1; y <= zone.y + 1; y++)
                {
                    foreach (ZDO zdo in Objects(new Vector2s(x, y)))
                    {
                        if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L)
                        {
                            Vector3 p = zdo.GetPosition();
                            work.Add(new Vector2(p.x, p.z));
                        }
                    }
                }
            }
            return work;
        }

        private static bool Near(List<Vector2> points, Vector2 at, float radius)
        {
            foreach (Vector2 p in points)
            {
                if ((p - at).sqrMagnitude < radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Within any location's buildings' reach (<see cref="Footprints"/>), the altar a path leads to included. Server only.</summary>
        internal static bool InLocation(Vector2 at)
        {
            Vector2s zone = ZoneSystem.GetZone(new Vector3(at.x, 0f, at.y));
            for (int x = zone.x - 1; x <= zone.x + 1; x++)
            {
                for (int y = zone.y - 1; y <= zone.y + 1; y++)
                {
                    if (ZoneSystem.instance.m_locationInstances.TryGetValue(new Vector2s(x, y), out ZoneSystem.LocationInstance instance))
                    {
                        float radius = Mathf.Max(Footprints.Radius(instance.m_location), MinLocationRadius);
                        if ((new Vector2(instance.m_position.x, instance.m_position.z) - at).sqrMagnitude < radius * radius)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Generated vegetation that stays on a path and stands in its way.</summary>
        internal struct Obstacle
        {
            /// <summary>How far past its centre it reaches, at scale 1.</summary>
            public float Reach;
            public float PrefabScale;
        }

        /// <summary>Solid vegetation prefab hash -> its size; built once per world, on the main thread.</summary>
        private static Dictionary<int, Obstacle> obstacles;
        private static ZoneSystem obstaclesFor;

        /// <summary>
        /// The vegetation the clearing leaves - ore, the giants' bones, anything else worth
        /// mining - that a road cannot run through: a solid collider, at least a metre across.
        /// Pickables, spawners and the like are not in the way. The search and the writer treat
        /// each as a disc of its measured reach (<see cref="Structures"/>).
        /// </summary>
        internal static Dictionary<int, Obstacle> Obstacles()
        {
            if (obstacles != null && obstaclesFor == ZoneSystem.instance)
            {
                return obstacles;
            }
            Dictionary<int, Kind> kinds = Clearable();
            obstacles = new Dictionary<int, Obstacle>();
            obstaclesFor = ZoneSystem.instance;
            foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation)
            {
                GameObject prefab = veg.m_prefab;
                if (prefab == null || prefab.GetComponent<ZNetView>() == null)
                {
                    continue;
                }
                int hash = prefab.name.GetStableHashCode();
                if (kinds.ContainsKey(hash) || obstacles.ContainsKey(hash)
                    || prefab.GetComponent<Pickable>() != null || prefab.GetComponent<SpawnArea>() != null
                    || prefab.GetComponent<CreatureSpawner>() != null)
                {
                    continue;
                }
                Boxes(prefab, out float reach);
                if (Solid(prefab) && reach >= 0.5f)
                {
                    obstacles[hash] = new Obstacle { Reach = reach, PrefabScale = prefab.transform.localScale.x };
                }
            }
            return obstacles;
        }

        private static Dictionary<int, Kind> Clearable()
        {
            if (clearable != null && clearableFor == ZoneSystem.instance)
            {
                return clearable;
            }
            clearable = new Dictionary<int, Kind>();
            clearableFor = ZoneSystem.instance;
            maxReach = Trunk;
            foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation)
            {
                GameObject prefab = veg.m_prefab;
                if (prefab == null || prefab.GetComponent<ZNetView>() == null || !KindOf(prefab, out Kind kind))
                {
                    continue;
                }
                clearable[prefab.name.GetStableHashCode()] = kind;
                if (kind.Scales)
                {
                    maxReach = Mathf.Max(maxReach, kind.Reach * Mathf.Max(kind.PrefabScale, veg.m_scaleMax));
                }
                // A boulder a player has hit stands as its broken copy (rock4_forest_frac and the
                // like, Destructible.m_spawnWhenDestroyed, at the boulder's scale), which is no
                // vegetation of its own (found through Procedural Roads #31).
                GameObject broken = prefab.GetComponent<Destructible>()?.m_spawnWhenDestroyed;
                if (broken != null && broken.GetComponent<ZNetView>() != null && KindOf(broken, out Kind brokenKind))
                {
                    clearable[broken.name.GetStableHashCode()] = brokenKind;
                    if (brokenKind.Scales)
                    {
                        maxReach = Mathf.Max(maxReach, brokenKind.Reach * Mathf.Max(brokenKind.PrefabScale, veg.m_scaleMax));
                    }
                }
            }
            return clearable;
        }

        /// <summary>Whether this vegetation may go, and how far it reaches.</summary>
        private static bool KindOf(GameObject prefab, out Kind kind)
        {
            kind = new Kind { Reach = Trunk, PrefabScale = prefab.transform.localScale.x };
            if (prefab.GetComponent<SpawnArea>() != null || prefab.GetComponent<CreatureSpawner>() != null
                || prefab.GetComponent<Pickable>() != null || prefab.GetComponent<Container>() != null)
            {
                return false;
            }
            if (prefab.GetComponent<TreeBase>() != null || prefab.GetComponent<TreeLog>() != null)
            {
                return true;
            }
            MineRock rock = prefab.GetComponent<MineRock>();
            MineRock5 boulder = prefab.GetComponent<MineRock5>();
            if (rock != null && !Plain(rock.m_dropItems) || boulder != null && !Plain(boulder.m_dropItems))
            {
                return false;
            }
            if (rock == null && boulder == null && prefab.GetComponent<Destructible>() == null)
            {
                // Scenery nobody can break: a cliff, a rock, a root. Only what stands solid and
                // big enough to be in the way; mist, water and the like are no rock.
                if (prefab.GetComponent<Piece>() != null || prefab.GetComponent<WearNTear>() != null || !Solid(prefab))
                {
                    return false;
                }
                kind.Boxes = Boxes(prefab, out float size);
                kind.Reach = size;
                kind.Scales = true;
                return size >= 0.5f;
            }
            if (rock == null && boulder == null)
            {
                DropOnDestroyed drops = prefab.GetComponent<DropOnDestroyed>();
                if (drops != null && !Plain(drops.m_dropWhenDestroyed))
                {
                    return false;
                }
            }
            List<Box> boxes = Boxes(prefab, out float measured);
            kind.Reach = measured > 0f ? measured : Unmeasured;
            kind.Scales = measured > 0f;
            kind.Boxes = measured > 0f ? boxes : null;
            return true;
        }

        private static bool Solid(GameObject prefab)
        {
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>())
            {
                if (!collider.isTrigger)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Its meshes' bounds seen from above, in its own unscaled space, one box per mesh - a
        /// level of detail whose box another holds already is left out -, and how far the
        /// farthest reaches from its centre along x or z.
        /// </summary>
        private static List<Box> Boxes(GameObject prefab, out float reach)
        {
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            List<Box> boxes = new List<Box>();
            reach = 0f;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }
                Bounds bounds = filter.sharedMesh.bounds;
                Matrix4x4 toPrefab = toRoot * filter.transform.localToWorldMatrix;
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 p = toPrefab.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, offset));
                    min = Vector2.Min(min, new Vector2(p.x, p.z));
                    max = Vector2.Max(max, new Vector2(p.x, p.z));
                    reach = Mathf.Max(reach, Mathf.Abs(p.x), Mathf.Abs(p.z));
                }
                Box box = new Box { Center = (min + max) * 0.5f, Half = (max - min) * 0.5f };
                if (!boxes.Exists(other => Holds(other, box)))
                {
                    boxes.RemoveAll(other => Holds(box, other));
                    boxes.Add(box);
                }
            }
            return boxes;
        }

        /// <summary>Whether outer holds inner, give or take a tenth of a metre.</summary>
        private static bool Holds(Box outer, Box inner)
        {
            const float slack = 0.1f;
            return inner.Center.x - inner.Half.x >= outer.Center.x - outer.Half.x - slack
                && inner.Center.x + inner.Half.x <= outer.Center.x + outer.Half.x + slack
                && inner.Center.y - inner.Half.y >= outer.Center.y - outer.Half.y - slack
                && inner.Center.y + inner.Half.y <= outer.Center.y + outer.Half.y + slack;
        }

        private static bool Plain(DropTable table)
        {
            if (table == null)
            {
                return true;
            }
            foreach (DropTable.DropData drop in table.m_drops)
            {
                if (drop.m_item != null && !PlainDrops.Contains(drop.m_item.name))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public partial class OdinsPathsPlugin
    {
        /// <summary>
        /// A zone the server generates for the first time - fully, or as a ghost beyond the
        /// players - gets the trees and rocks on a path already written into it taken away
        /// again, right after they were placed.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnZone))]
        public static class ClearNewZones
        {
            private static void Prefix(ZoneSystem __instance, Vector2s zoneID, ZoneSystem.SpawnMode mode, out bool __state)
            {
                __state = (mode == ZoneSystem.SpawnMode.Full || mode == ZoneSystem.SpawnMode.Ghost)
                    && ZNet.instance != null && ZNet.instance.IsServer() && !__instance.IsZoneGenerated(zoneID);
            }

            private static void Postfix(Vector2s zoneID, bool __result, bool __state)
            {
                if (__state && __result)
                {
                    Clearing.ClearNewZone(zoneID);
                }
            }
        }
    }
}
