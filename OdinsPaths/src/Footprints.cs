using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// How far a location's buildings really reach. Its exterior radius is what the game clears
    /// and levels for it, and some reach far past that: the Mistlands' viaducts (8 m) and giant
    /// skeletons (10 m) stand 20 m and more out, and a road went through one (seen in game
    /// 2026-09-25), its clearing taking pieces of it as scenery. Measured once per location from
    /// its prefab's meshes as seen from above; never less than the exterior radius and never more
    /// than <see cref="Largest"/>. Main thread only.
    ///
    /// Loading a prefab took 20-640 ms (CharredFortress 516, TheHole01 635; 2026-09-26), and a
    /// road's first look at 57 of them stalled one frame for 5.9 s. So once the world is up
    /// <see cref="Warm"/> loads every location the world has instances of asynchronously, a few at
    /// a time, and a lay waits for it (<see cref="Wait"/>); the reaches are kept on disk per game
    /// version (<see cref="CacheFile"/>), so it happens once. A location it has not measured yet
    /// is still loaded on the spot.
    ///
    /// The same look reads the location's <see cref="Layout"/> for a road's lane into it
    /// (<see cref="Approaches"/>): its door and the corridor before it, or the solid things a
    /// road must pass between - read from the bundles, 2026-10-02, in <c>docs/laying.md</c>.
    /// </summary>
    internal static class Footprints
    {
        /// <summary>No footprint is taken as wider than this: a stray water plane or backdrop mesh would close off a valley.</summary>
        private const float Largest = 48f;
        /// <summary>Meshes further above or below the location's origin than this are its interior (dungeons sit thousands of metres up), not its footprint.</summary>
        private const float Above = 60f;
        /// <summary>Prefabs loading at once in the warm-up.</summary>
        private const int InFlight = 4;
        /// <summary>Seconds a load may take before the warm-up gives up on it (and takes the exterior radius).</summary>
        private const float GiveUp = 20f;
        /// <summary>Raised whenever the measuring changes, so the reaches on disk are taken again.</summary>
        private const int Format = 5;
        /// <summary>The walking band over the location's ground: what reaches into it is in the way, a floor slab or a roof is not.</summary>
        private const float BandLow = 0.8f;
        private const float BandHigh = 2f;
        /// <summary>A Default-layer mesh this wide (half) and no higher than this over the ground is ground: the rock an altar stands on.</summary>
        private const float GroundWide = 5f;
        private const float GroundTop = 3.5f;
        /// <summary>Half the strip out of a door whose walls, slabs and stairs make its corridor.</summary>
        private const float CorridorHalf = 2.5f;
        /// <summary>A rock wider (half) than this beside a door is a cliff, not its corridor: the Dvergr gate's cliffs read past its stair.</summary>
        private const float Boulder = 12f;

        /// <summary>A location prefab as measured: in its own frame, the game turning the whole around its origin.</summary>
        internal sealed class Layout
        {
            /// <summary>Unclamped.</summary>
            public float Reach;
            /// <summary>The door, NaN for none or more than one; Out the way out of it (the teleport's forward: a player leaving lands along it).</summary>
            public Vector2 Door = new Vector2(float.NaN, float.NaN);
            public Vector2 Out;
            /// <summary>How far out of the door its corridor, ramp or stair reaches: the location's own way in.</summary>
            public float Corridor;
            /// <summary>What a road must pass between, as discs: x, z and radius.</summary>
            public readonly List<Vector3> Solids = new List<Vector3>();
            /// <summary>The ground the location levels and smooths itself (its <c>TerrainModifier</c>s), as discs: x, z and radius.</summary>
            public readonly List<Vector3> Flats = new List<Vector3>();

            public bool HasDoor => !float.IsNaN(Door.x);

            public static readonly Layout None = new Layout();
        }

        /// <summary>What was measured of each location prefab, by name.</summary>
        private static readonly Dictionary<string, Layout> measured = new Dictionary<string, Layout>();
        private static bool cacheRead;
        private static bool warming;
        private static ZoneSystem warmedFor;

        private static string CacheFile => Path.Combine(BepInEx.Paths.CachePath, "OdinsPaths.footprints.txt");

        /// <summary>The radius a road keeps out of around an instance of this location.</summary>
        public static float Radius(ZoneSystem.ZoneLocation location)
        {
            if (location == null)
            {
                return 0f;
            }
            return Mathf.Max(location.m_exteriorRadius, Mathf.Min(Get(location).Reach, Largest));
        }

        /// <summary>The location's layout, in its prefab's frame (turn it by the instance's rotation).</summary>
        public static Layout LayoutOf(ZoneSystem.ZoneLocation location) => location != null ? Get(location) : Layout.None;

        private static Layout Get(ZoneSystem.ZoneLocation location)
        {
            ReadCache();
            if (!measured.TryGetValue(location.m_prefabName, out Layout measure))
            {
                measure = MeasureNow(location);
                measured[location.m_prefabName] = measure;
            }
            return measure;
        }

        /// <summary>
        /// From the plugin's Update on the server: starts the warm-up once per world, as soon as
        /// its locations are known.
        /// </summary>
        public static void Tick()
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || warmedFor == zones || !zones.LocationsGenerated)
            {
                return;
            }
            Begin();
        }

        /// <summary>Until every location of the world is measured: a lay yields to this before it looks at any.</summary>
        public static IEnumerator Wait()
        {
            if (warmedFor != ZoneSystem.instance)
            {
                Begin();
            }
            while (warming)
            {
                yield return null;
            }
        }

        private static void Begin()
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (warming || zones == null || OdinsPathsPlugin.Instance == null)
            {
                return;
            }
            warmedFor = zones;
            ReadCache();
            List<ZoneSystem.ZoneLocation> due = new List<ZoneSystem.ZoneLocation>();
            HashSet<string> seen = new HashSet<string>();
            foreach (ZoneSystem.LocationInstance instance in zones.m_locationInstances.Values)
            {
                ZoneSystem.ZoneLocation location = instance.m_location;
                if (location != null && !measured.ContainsKey(location.m_prefabName) && seen.Add(location.m_prefabName))
                {
                    due.Add(location);
                }
            }
            if (due.Count == 0)
            {
                return;
            }
            warming = true;
            OdinsPathsPlugin.Instance.StartCoroutine(Warm(due));
        }

        /// <summary>
        /// Loads the prefabs asynchronously, a few at a time, and measures each once it is in,
        /// within the frame budget. Started only by <see cref="Begin"/>, as a coroutine of its own
        /// that nothing stops, so <see cref="warming"/> always comes down.
        /// </summary>
        private static IEnumerator Warm(List<ZoneSystem.ZoneLocation> due)
        {
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Stopwatch frame = new System.Diagnostics.Stopwatch();
            List<Loading> loading = new List<Loading>();
            int next = 0;
            while (next < due.Count || loading.Count > 0)
            {
                while (loading.Count < InFlight && next < due.Count)
                {
                    ZoneSystem.ZoneLocation location = due[next++];
                    Loading started = Loading.Begin(location);
                    if (started != null)
                    {
                        loading.Add(started);
                    }
                    else
                    {
                        measured[location.m_prefabName] = Layout.None;
                    }
                }
                yield return null;
                frame.Restart();
                for (int i = loading.Count - 1; i >= 0 && frame.Elapsed.TotalMilliseconds < OdinsPathsPlugin.SearchBudgetMs.Value; i--)
                {
                    if (loading[i].Done)
                    {
                        measured[loading[i].Location.m_prefabName] = loading[i].Finish();
                        loading.RemoveAt(i);
                    }
                }
            }
            WriteCache();
            Debug.Log("[OdinsPaths] Measured " + due.Count + " locations' footprints in " + total.ElapsedMilliseconds + " ms, loaded in the background.");
            warming = false;
        }

        /// <summary>
        /// <c>ZoneLocation.m_prefab</c> is a <c>SoftReference</c> from an assembly the build does
        /// not reference, so it is reached by reflection: a boxed copy of it loads and releases the
        /// same asset, which is all it holds the id of. <c>Load</c> and <c>LoadAsync</c> both hold
        /// a reference that <c>Release</c> gives back.
        /// </summary>
        private class Loading
        {
            public ZoneSystem.ZoneLocation Location;
            private object reference;
            private PropertyInfo loaded;
            private PropertyInfo busy;
            private PropertyInfo asset;
            private MethodInfo release;
            private float since;

            public static Loading Begin(ZoneSystem.ZoneLocation location, bool wait = false)
            {
                object reference = typeof(ZoneSystem.ZoneLocation).GetField("m_prefab")?.GetValue(location);
                System.Type type = reference?.GetType();
                PropertyInfo valid = type?.GetProperty("IsValid");
                MethodInfo load = type?.GetMethod(wait ? "Load" : "LoadAsync", System.Type.EmptyTypes);
                Loading loading = new Loading
                {
                    Location = location,
                    reference = reference,
                    loaded = type?.GetProperty("IsLoaded"),
                    busy = type?.GetProperty("IsLoading"),
                    asset = type?.GetProperty("Asset"),
                    release = type?.GetMethod("Release", System.Type.EmptyTypes),
                    since = Time.realtimeSinceStartup,
                };
                if (valid == null || load == null || loading.loaded == null || loading.busy == null || loading.asset == null
                    || loading.release == null || !(bool)valid.GetValue(reference))
                {
                    return null;
                }
                load.Invoke(reference, null);
                return loading;
            }

            /// <summary>Loaded, or given up on.</summary>
            public bool Done => (bool)loaded.GetValue(reference) || !(bool)busy.GetValue(reference)
                || Time.realtimeSinceStartup - since > GiveUp;

            /// <summary>The reach and the entrance, and the asset released.</summary>
            public Layout Finish()
            {
                try
                {
                    GameObject prefab = (bool)loaded.GetValue(reference) ? asset.GetValue(reference) as GameObject : null;
                    return prefab != null ? Read(prefab, Location) : Layout.None;
                }
                catch (System.Exception error)
                {
                    Debug.LogWarning("[OdinsPaths] Could not measure " + Location.m_prefabName + ": " + error.Message);
                    return Layout.None;
                }
                finally
                {
                    release.Invoke(reference, null);
                }
            }
        }

        /// <summary>A location the warm-up has not measured, loaded on the spot.</summary>
        private static Layout MeasureNow(ZoneSystem.ZoneLocation location)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            Loading loading = Loading.Begin(location, true);
            Layout measure = loading != null ? loading.Finish() : Layout.None;
            if (watch.ElapsedMilliseconds > 20)
            {
                Debug.Log("[OdinsPaths] Measuring " + location.m_prefabName + " on the spot took " + watch.ElapsedMilliseconds + " ms.");
            }
            return measure;
        }

        private static Layout Read(GameObject prefab, ZoneSystem.ZoneLocation location)
        {
            Layout layout = new Layout { Reach = Reach(prefab, location) };
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            Door(prefab, toRoot, layout, out float doorHeight);
            Flats(prefab, toRoot, layout);
            List<Box> boxes = Boxes(prefab, toRoot);
            if (layout.HasDoor)
            {
                layout.Corridor = Corridor(boxes, layout, doorHeight);
            }
            else
            {
                foreach (Box box in boxes)
                {
                    if (box.Top > BandLow && box.Bottom < BandHigh && !(box.Layer == 0 && box.Half > GroundWide && box.Top < GroundTop)
                        && (!box.Cut || box.InBand))
                    {
                        // The box's corners reach past a stone's waist; its mean half extent is nearer.
                        Vector2 centre = box.Cut ? box.BandCentre : box.Centre;
                        Vector2 extent = box.Cut ? box.BandExtent : box.Extent;
                        layout.Solids.Add(new Vector3(centre.x, centre.y, (extent.x + extent.y) * 0.5f));
                    }
                }
            }
            return layout;
        }

        /// <summary>
        /// Where the location levels or smooths its own ground: a road's levelling on top would
        /// count from the ground before it (<c>TerrainComp</c> adds its deltas after the
        /// location's modifiers), so it keeps out of these.
        /// </summary>
        private static void Flats(GameObject prefab, Matrix4x4 toRoot, Layout layout)
        {
            foreach (TerrainModifier modifier in prefab.GetComponentsInChildren<TerrainModifier>(false))
            {
                float radius = Mathf.Max(modifier.m_level ? modifier.m_levelRadius : 0f, modifier.m_smooth ? modifier.m_smoothRadius : 0f);
                Vector3 p = toRoot.MultiplyPoint3x4(modifier.transform.position);
                if (!modifier.enabled || radius <= 0f || Mathf.Abs(p.y) > Above)
                {
                    continue;
                }
                layout.Flats.Add(new Vector3(p.x, p.z, modifier.m_square ? radius * Mathf.Sqrt(2f) : radius));
            }
        }

        /// <summary>
        /// The one teleport at ground level - a dungeon's door; its twin inside sits thousands of
        /// metres up - in the prefab's frame, and its forward: the way out (<c>Teleport.GetTeleportPoint</c>).
        /// The game spawns a location with its root at the origin and unturned
        /// (<c>ZoneSystem.SpawnLocation</c>), then turns the whole. None for none or several.
        /// </summary>
        private static void Door(GameObject prefab, Matrix4x4 toRoot, Layout layout, out float height)
        {
            height = 0f;
            Teleport found = null;
            foreach (Teleport teleport in prefab.GetComponentsInChildren<Teleport>(true))
            {
                if (Mathf.Abs(toRoot.MultiplyPoint3x4(teleport.transform.position).y) > Above)
                {
                    continue;
                }
                if (found != null)
                {
                    return;
                }
                found = teleport;
            }
            if (found == null)
            {
                return;
            }
            Vector3 p = toRoot.MultiplyPoint3x4(found.transform.position);
            Vector3 forward = toRoot.MultiplyVector(found.transform.forward);
            Vector2 flat = new Vector2(forward.x, forward.z);
            if (flat.sqrMagnitude < 0.01f)
            {
                return;
            }
            layout.Door = new Vector2(p.x, p.z);
            layout.Out = flat.normalized;
            height = p.y;
        }

        /// <summary>
        /// How far out of the door the location's own way in reaches: the far end of every wall,
        /// slab, ramp and stair standing in the strip before it, above the ground and below the
        /// door's head - a crypt's corridor, a sunken crypt's stair, the Dvergr gate's long stair.
        /// </summary>
        private static float Corridor(List<Box> boxes, Layout layout, float doorHeight)
        {
            Vector2 side = new Vector2(layout.Out.y, -layout.Out.x);
            float reach = 0f;
            foreach (Box box in boxes)
            {
                if (box.Top < Mathf.Min(0.4f, doorHeight) || box.Bottom > doorHeight + 4f || box.Half > Boulder)
                {
                    continue;
                }
                Vector2 offset = box.Centre - layout.Door;
                float along = Vector2.Dot(offset, layout.Out);
                float across = Mathf.Abs(Vector2.Dot(offset, side));
                // The box's extent along and across the door's line, from its corners.
                float alongReach = Mathf.Abs(box.Extent.x * layout.Out.x) + Mathf.Abs(box.Extent.y * layout.Out.y);
                float acrossReach = Mathf.Abs(box.Extent.x * side.x) + Mathf.Abs(box.Extent.y * side.y);
                if (along + alongReach > 0f && across - acrossReach < CorridorHalf)
                {
                    reach = Mathf.Max(reach, along + alongReach);
                }
            }
            return Mathf.Min(reach, Largest);
        }

        /// <summary>A solid collider seen from above: centre and half extents in the prefab's frame, its height span, its layer.</summary>
        private struct Box
        {
            public Vector2 Centre;
            public Vector2 Extent;
            public float Bottom;
            public float Top;
            public int Layer;
            /// <summary>
            /// A readable mesh cut at the walking band (<see cref="Band"/>): what of it stands there,
            /// if anything. Yagluth's leaning rock fingers are boxes 30 to 44 m wide and 12 m
            /// thick where a player walks (the bundles, 2026-10-02), and their boxes shut every gap.
            /// </summary>
            public bool Cut;
            public bool InBand;
            public Vector2 BandCentre;
            public Vector2 BandExtent;
            public float Half => Mathf.Max(Extent.x, Extent.y);
            /// <summary>A disc for it: the mean of its half extents.</summary>
            public float Disc => (Extent.x + Extent.y) * 0.5f;
        }

        /// <summary>
        /// Every solid collider near the ground: not a trigger, not disabled, on a layer that stops
        /// a player (Default, piece, static_solid, Default_small, blocker), as the box of its
        /// corners in the prefab's frame.
        /// </summary>
        private static List<Box> Boxes(GameObject prefab, Matrix4x4 toRoot)
        {
            List<Box> boxes = new List<Box>();
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(false))
            {
                int layer = collider.gameObject.layer;
                if (collider.isTrigger || !collider.enabled || !(layer == 0 || layer == 10 || layer == 15 || layer == 20 || layer == 23))
                {
                    continue;
                }
                Bounds local;
                Mesh cut = null;
                switch (collider)
                {
                    case BoxCollider b: local = new Bounds(b.center, b.size); break;
                    case SphereCollider sphere: local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f); break;
                    case CapsuleCollider capsule:
                        Vector3 size = Vector3.one * capsule.radius * 2f;
                        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                        local = new Bounds(capsule.center, size);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        local = mesh.sharedMesh.bounds;
                        cut = mesh.sharedMesh.isReadable ? mesh.sharedMesh : null;
                        break;
                    default: continue;
                }
                Matrix4x4 toPrefab = toRoot * collider.transform.localToWorldMatrix;
                Vector3 min = Vector3.one * float.MaxValue;
                Vector3 max = Vector3.one * float.MinValue;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 p = toPrefab.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, offset));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                if (Mathf.Abs((min.y + max.y) * 0.5f) > Above)
                {
                    continue;
                }
                Box box = new Box
                {
                    Centre = new Vector2((min.x + max.x) * 0.5f, (min.z + max.z) * 0.5f),
                    Extent = new Vector2((max.x - min.x) * 0.5f, (max.z - min.z) * 0.5f),
                    Bottom = min.y,
                    Top = max.y,
                    Layer = layer,
                };
                if (cut != null && box.Top > BandLow && box.Bottom < BandHigh)
                {
                    box.Cut = true;
                    box.InBand = Band(cut, toPrefab, out box.BandCentre, out box.BandExtent);
                }
                boxes.Add(box);
            }
            return boxes;
        }

        /// <summary>
        /// The box, seen from above, of what of the mesh lies in the walking band: its vertices
        /// there and where its triangles' edges cross the band's floor and ceiling. False if
        /// nothing does (an arch overhead).
        /// </summary>
        private static bool Band(Mesh mesh, Matrix4x4 toPrefab, out Vector2 centre, out Vector2 extent)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = toPrefab.MultiplyPoint3x4(vertices[i]);
            }
            Vector2 min = Vector2.one * float.MaxValue;
            Vector2 max = Vector2.one * float.MinValue;
            void Add(Vector3 p)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            void Cross(Vector3 a, Vector3 b, float h)
            {
                if ((a.y - h) * (b.y - h) < 0f)
                {
                    Add(Vector3.Lerp(a, b, (h - a.y) / (b.y - a.y)));
                }
            }
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = vertices[triangles[t + k]];
                    Vector3 b = vertices[triangles[t + (k + 1) % 3]];
                    if (a.y >= BandLow && a.y <= BandHigh)
                    {
                        Add(a);
                    }
                    Cross(a, b, BandLow);
                    Cross(a, b, BandHigh);
                }
            }
            centre = (min + max) * 0.5f;
            extent = (max - min) * 0.5f;
            return min.x <= max.x;
        }

        private static float Reach(GameObject prefab, ZoneSystem.ZoneLocation location)
        {
            float reach = 0f;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }
                Bounds bounds = filter.sharedMesh.bounds;
                Matrix4x4 toPrefab = toRoot * filter.transform.localToWorldMatrix;
                if (Mathf.Abs(toPrefab.MultiplyPoint3x4(bounds.center).y) > Above)
                {
                    continue;
                }
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 p = toPrefab.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, offset));
                    reach = Mathf.Max(reach, new Vector2(p.x, p.z).magnitude);
                }
            }
            if (reach > location.m_exteriorRadius + 1f)
            {
                Debug.Log("[OdinsPaths] " + location.m_prefabName + " reaches " + reach.ToString("0") + " m, past its exterior radius of "
                    + location.m_exteriorRadius.ToString("0") + " m" + (reach > Largest ? " - kept to " + Largest.ToString("0") + " m." : "."));
            }
            return reach;
        }

        /// <summary>The first line names the format and the game version; a mismatch reads nothing.</summary>
        private static string Header => "# OdinsPaths footprints " + Format + " " + Version.GetVersionString();

        private static void ReadCache()
        {
            if (cacheRead)
            {
                return;
            }
            cacheRead = true;
            try
            {
                if (!File.Exists(CacheFile))
                {
                    return;
                }
                string[] lines = File.ReadAllLines(CacheFile);
                if (lines.Length == 0 || lines[0] != Header)
                {
                    return;
                }
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length != 9)
                    {
                        continue;
                    }
                    Layout layout = new Layout
                    {
                        Reach = Number(parts[1]),
                        Door = new Vector2(Number(parts[2]), Number(parts[3])),
                        Out = new Vector2(Number(parts[4]), Number(parts[5])),
                        Corridor = Number(parts[6]),
                    };
                    Discs(parts[7], layout.Solids);
                    Discs(parts[8], layout.Flats);
                    measured[parts[0]] = layout;
                }
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("[OdinsPaths] Could not read " + CacheFile + ": " + error.Message);
            }
        }

        private static void Discs(string text, List<Vector3> into)
        {
            foreach (string part in text.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string[] disc = part.Split(',');
                into.Add(new Vector3(Number(disc[0]), Number(disc[1]), Number(disc[2])));
            }
        }

        private static string Discs(List<Vector3> discs) => string.Join(";", discs.ConvertAll(d => Text(d.x) + "," + Text(d.y) + "," + Text(d.z)));

        /// <summary>A cache line's number; a malformed one ends the reading, and what is left is measured again.</summary>
        private static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string Text(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static void WriteCache()
        {
            try
            {
                List<string> lines = new List<string> { Header };
                foreach (KeyValuePair<string, Layout> entry in measured)
                {
                    Layout layout = entry.Value;
                    lines.Add(string.Join("\t", entry.Key, Text(layout.Reach), Text(layout.Door.x), Text(layout.Door.y),
                        Text(layout.Out.x), Text(layout.Out.y), Text(layout.Corridor), Discs(layout.Solids), Discs(layout.Flats)));
                }
                Directory.CreateDirectory(BepInEx.Paths.CachePath);
                File.WriteAllLines(CacheFile, lines);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("[OdinsPaths] Could not write " + CacheFile + ": " + error.Message);
            }
        }
    }
}
