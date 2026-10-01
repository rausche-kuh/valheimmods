using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Everything built that stands in a search area: every piece a player placed, the pieces of
    /// generated ruins and villages, and the harbour stones and posts earlier paths left at their
    /// landings - any ZDO whose prefab is a <c>Piece</c> or wears (<c>WearNTear</c>), or is a
    /// <see cref="Harbours"/> stone - and the generated
    /// vegetation that stands in the way and stays (<see cref="Clearing.Obstacles"/>: ore, the
    /// Mistlands' rocks and roots, cliffs), as points over its footprint. The search goes around
    /// them, and the terrain writer leaves the ground under and beside them alone. Server only:
    /// only the server holds every ZDO, loaded or not. Vegetation exists only in zones generated
    /// already; in the others a road may still run into a rock (seen in game 2026-09-25: the road
    /// ended at one).
    /// </summary>
    internal sealed class Structures
    {
        /// <summary>How far a piece reaches from its centre - a wall is 4 m long.</summary>
        public const float PieceReach = 2f;
        private const float BucketSize = 8f;
        /// <summary>Dungeon interiors sit at y 5000 above their entrances; they are not in the way.</summary>
        private const float InteriorHeight = 1000f;

        /// <summary>Prefab hash -> whether it is a structure; built once per scene.</summary>
        private static Dictionary<int, bool> kinds;
        private static ZNetScene kindsFor;
        /// <summary>The array a gathering copies every ZDO into, to read them off the main thread.</summary>
        private static ZDO[] pool;

        private readonly Dictionary<long, List<Vector2>> buckets = new Dictionary<long, List<Vector2>>();

        public int Count { get; private set; }
        /// <summary>Of them, the rocks and roots (each one disc of points).</summary>
        public int Obstacles { get; private set; }
        /// <summary>Of them, the harbour stones earlier roads left (<see cref="Harbours"/>): a landing beside one shares its harbour.</summary>
        public readonly List<Vector2> HarbourStones = new List<Vector2>();

        /// <summary>
        /// The structures where inside says, handed to done. The world holds hundreds of thousands
        /// of objects and a search area dozens of ellipses: the objects are listed on the main
        /// thread (a copy of the references, quick) and sorted on a thread of their own - done on
        /// the main thread, this took up to 2.4 s a road (2026-09-25).
        /// </summary>
        public static IEnumerator Gather(Func<Vector2, bool> inside, Action<Structures> done)
        {
            Dictionary<int, bool> structure = Kinds();
            Dictionary<int, Clearing.Obstacle> obstacles = Clearing.Obstacles();
            List<ZDO> standing = new List<ZDO>();
            Dictionary<ZDOID, ZDO> objects = ZDOMan.instance.m_objectsByID;
            // Kept from road to road: a new array of every ZDO was megabytes, a collection each
            // time. Taken out of the pool meanwhile, so a second gathering makes its own.
            ZDO[] all = pool;
            pool = null;
            if (all == null || all.Length < objects.Count)
            {
                all = new ZDO[objects.Count + objects.Count / 4];
            }
            int count = objects.Count;
            objects.Values.CopyTo(all, 0);
            Structures result = new Structures();
            // A ZDO the main thread moves or frees meanwhile is read torn at worst: one piece
            // missed or kept for a road, no more.
            yield return Worker.Run(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    ZDO zdo = all[i];
                    Vector3 p = zdo.GetPosition();
                    if (p.y > InteriorHeight)
                    {
                        continue;
                    }
                    bool built = structure.TryGetValue(zdo.GetPrefab(), out bool isPiece) && isPiece;
                    if (!built && !obstacles.ContainsKey(zdo.GetPrefab()))
                    {
                        continue;
                    }
                    Vector2 at = new Vector2(p.x, p.z);
                    if (!inside(at))
                    {
                        continue;
                    }
                    if (built)
                    {
                        result.Add(at);
                        if (zdo.GetPrefab() == Harbours.Hash)
                        {
                            result.HarbourStones.Add(at);
                        }
                    }
                    else
                    {
                        standing.Add(zdo);
                    }
                }
            });
            // A rock's size is its scale, which only the main thread may read (ZDO extra data).
            foreach (ZDO zdo in standing)
            {
                if (!zdo.IsValid() || !obstacles.TryGetValue(zdo.GetPrefab(), out Clearing.Obstacle obstacle))
                {
                    continue;
                }
                Vector3 p = zdo.GetPosition();
                result.AddDisc(new Vector2(p.x, p.z), obstacle.Reach * Clearing.ScaleOf(zdo, obstacle.PrefabScale));
                result.Obstacles++;
            }
            // The ZDOs are not kept alive by the pool.
            Array.Clear(all, 0, count);
            pool = all;
            done(result);
        }

        /// <summary>
        /// Inside the ellipse of any start with any goal, as the search has them. Every piece
        /// against every ellipse took seconds a road once the network had grown - a hundred
        /// thousand pieces, a Mistlands town's dozens of ways in (2026-10-01) - so the area is cut
        /// into squares first (<see cref="EllipseTree"/>), built on the first point asked: on the
        /// gathering's thread.
        /// </summary>
        public static Func<Vector2, bool> Ellipses(List<Vector2> starts, List<Vector2> goals)
        {
            int count = starts.Count * goals.Count;
            Vector2[] from = new Vector2[count];
            Vector2[] to = new Vector2[count];
            float[] limit = new float[count];
            for (int s = 0; s < starts.Count; s++)
            {
                for (int g = 0; g < goals.Count; g++)
                {
                    int k = s * goals.Count + g;
                    from[k] = starts[s];
                    to[k] = goals[g];
                    limit[k] = PathSearch.EllipseLimit(starts[s], goals[g]);
                }
            }
            Lazy<EllipseTree> tree = new Lazy<EllipseTree>(() => new EllipseTree(from, to, limit));
            return at => tree.Value.Contains(at);
        }

        /// <summary>
        /// Ellipses (two foci and the most their distances may add up to) in a quadtree: a square
        /// wholly inside one of them, or outside all, answers without a test; one on an edge keeps
        /// the few it may be in. Exact: a point is within half the square's diagonal of its
        /// centre, so its two distances are within a diagonal of the centre's.
        /// </summary>
        private sealed class EllipseTree
        {
            /// <summary>A square this small, or with this few ellipses on it, is not cut further.</summary>
            private const float Smallest = 16f;
            private const int Few = 4;

            private sealed class Node
            {
                public bool Inside;
                /// <summary>Null and no children: outside them all.</summary>
                public int[] Candidates;
                public Node[] Children;
            }

            private readonly Vector2[] from;
            private readonly Vector2[] to;
            private readonly float[] limit;
            private readonly Vector2 corner;
            private readonly float size;
            private readonly Node root;

            public EllipseTree(Vector2[] from, Vector2[] to, float[] limit)
            {
                this.from = from;
                this.to = to;
                this.limit = limit;
                if (limit.Length == 0)
                {
                    root = new Node();
                    return;
                }
                // Each ellipse lies in the circle round the middle of its foci, half its limit wide.
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                int[] all = new int[limit.Length];
                for (int k = 0; k < limit.Length; k++)
                {
                    Vector2 middle = (from[k] + to[k]) * 0.5f;
                    float radius = limit[k] * 0.5f;
                    min = Vector2.Min(min, middle - new Vector2(radius, radius));
                    max = Vector2.Max(max, middle + new Vector2(radius, radius));
                    all[k] = k;
                }
                corner = min;
                size = Mathf.Max(max.x - min.x, max.y - min.y, Smallest);
                root = Build(corner, size, all);
            }

            private Node Build(Vector2 at, float side, int[] candidates)
            {
                Vector2 centre = at + new Vector2(side, side) * 0.5f;
                // Twice half the diagonal: how far apart the two distances' sums can be in the square.
                float spread = side * 1.4143f;
                List<int> kept = new List<int>();
                foreach (int k in candidates)
                {
                    float sum = Vector2.Distance(centre, from[k]) + Vector2.Distance(centre, to[k]);
                    if (sum + spread <= limit[k])
                    {
                        return new Node { Inside = true };
                    }
                    if (sum - spread <= limit[k])
                    {
                        kept.Add(k);
                    }
                }
                if (kept.Count == 0)
                {
                    return new Node();
                }
                int[] some = kept.ToArray();
                if (some.Length <= Few || side <= Smallest)
                {
                    return new Node { Candidates = some };
                }
                float half = side * 0.5f;
                Node[] children = new Node[4];
                for (int i = 0; i < 4; i++)
                {
                    children[i] = Build(at + new Vector2((i & 1) * half, (i >> 1) * half), half, some);
                }
                return new Node { Children = children };
            }

            public bool Contains(Vector2 p)
            {
                if (p.x < corner.x || p.y < corner.y || p.x > corner.x + size || p.y > corner.y + size)
                {
                    return false;
                }
                Node node = root;
                Vector2 at = corner;
                float side = size;
                while (node.Children != null)
                {
                    side *= 0.5f;
                    int x = p.x >= at.x + side ? 1 : 0;
                    int y = p.y >= at.y + side ? 1 : 0;
                    at += new Vector2(x * side, y * side);
                    node = node.Children[x + 2 * y];
                }
                if (node.Inside)
                {
                    return true;
                }
                if (node.Candidates == null)
                {
                    return false;
                }
                foreach (int k in node.Candidates)
                {
                    if (Vector2.Distance(p, from[k]) + Vector2.Distance(p, to[k]) <= limit[k])
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// A copy without the structures inside any of the circles: a spur's search lets it walk up
        /// to the ruins it leads to. The terrain writer still gets the whole set.
        /// </summary>
        public Structures Without(List<Circle> circles)
        {
            Structures result = new Structures();
            result.HarbourStones.AddRange(HarbourStones);
            foreach (List<Vector2> bucket in buckets.Values)
            {
                foreach (Vector2 at in bucket)
                {
                    if (!circles.Exists(circle => circle.Contains(at)))
                    {
                        result.Add(at);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Points over a disc, so that every point of it lies within <see cref="PieceReach"/> of
        /// one: rings from the edge inward, a piece's reach apart, a point every 3 m along each.
        /// </summary>
        public void AddDisc(Vector2 centre, float radius)
        {
            Add(centre);
            for (float r = radius - PieceReach; r > PieceReach * 0.5f; r -= PieceReach * 1.5f)
            {
                int count = Mathf.Max(4, Mathf.CeilToInt(2f * Mathf.PI * r / 3f));
                for (int k = 0; k < count; k++)
                {
                    float angle = 2f * Mathf.PI * k / count;
                    Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
                }
            }
        }

        public void Add(Vector2 at)
        {
            long key = Key(Mathf.FloorToInt(at.x / BucketSize), Mathf.FloorToInt(at.y / BucketSize));
            if (!buckets.TryGetValue(key, out List<Vector2> bucket))
            {
                buckets[key] = bucket = new List<Vector2>();
            }
            bucket.Add(at);
            Count++;
        }

        /// <summary>The distance to the nearest structure, or float.MaxValue if none is within reach.</summary>
        public float Distance(Vector2 at, float reach)
        {
            float best = float.MaxValue;
            if (Count == 0)
            {
                return best;
            }
            int x0 = Mathf.FloorToInt((at.x - reach) / BucketSize);
            int x1 = Mathf.FloorToInt((at.x + reach) / BucketSize);
            int y0 = Mathf.FloorToInt((at.y - reach) / BucketSize);
            int y1 = Mathf.FloorToInt((at.y + reach) / BucketSize);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (!buckets.TryGetValue(Key(x, y), out List<Vector2> bucket))
                    {
                        continue;
                    }
                    foreach (Vector2 p in bucket)
                    {
                        best = Mathf.Min(best, (p - at).sqrMagnitude);
                    }
                }
            }
            return best == float.MaxValue ? best : Mathf.Sqrt(best);
        }

        /// <summary>
        /// Prefab hash -> whether it is a structure, for every prefab of the scene; built on the
        /// main thread once per scene (GetComponent is Unity's), then only read.
        /// </summary>
        private static Dictionary<int, bool> Kinds()
        {
            if (kinds == null || kindsFor != ZNetScene.instance)
            {
                kinds = new Dictionary<int, bool>();
                kindsFor = ZNetScene.instance;
                foreach (KeyValuePair<int, GameObject> prefab in ZNetScene.instance.m_namedPrefabs)
                {
                    GameObject go = prefab.Value;
                    kinds[prefab.Key] = go != null && (go.GetComponent<Piece>() != null || go.GetComponent<WearNTear>() != null
                        || prefab.Key == Harbours.Hash);
                }
            }
            return kinds;
        }

        private static long Key(int i, int j) => ((long)i << 32) | (uint)j;
    }
}
