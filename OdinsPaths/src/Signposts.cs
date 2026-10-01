using BepInEx.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Signposts at the main network's forks and harbour landings (docs/signposts.md): a log pole
    /// with one vanilla <c>sign</c> board per line, each pointing the way it names. A line is a
    /// boss's name, a hint for other places (a deep mine, a village), or a region (<see cref="Regions"/>) for the far biomes
    /// of many places, so a sign leads to a harbour and the posts there go on with the hints. The
    /// network is read as a graph: main roads and side roads joined where one set out from
    /// another; spurs to points of interest are left out.
    ///
    /// <see cref="Refresh"/> places what is missing, rebuilds a post whose lines changed or lost a
    /// piece, and removes posts the network no longer has. A player's edit of a board's text stays
    /// until its post's lines change. Server only; the pieces are vanilla, so clients need nothing.
    /// </summary>
    internal static class Signposts
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Lines;
        internal static ConfigEntry<string> Regions;
        internal static ConfigEntry<string> Remote;

        private const string PolePrefab = "wood_pole_log_4";
        private const string BoardPrefab = "sign";
        /// <summary>On every piece of a post: the post's position, so its pieces are found together.</summary>
        private static readonly int SiteKey = "OdinsPaths_Signpost".GetStableHashCode();
        /// <summary>On a post's pole: its lines as laid out, to tell whether it needs rebuilding.</summary>
        private static readonly int ContentKey = "OdinsPaths_SignpostLines".GetStableHashCode();
        /// <summary>On a post's pole: how many pieces it was built of.</summary>
        private static readonly int PiecesKey = "OdinsPaths_SignpostPieces".GetStableHashCode();

        /// <summary>A road's first point this close to another road's point joins it there.</summary>
        private const float JoinRadius = 12f;
        /// <summary>No post this close to the sacrificial stones: roads leaving them join in the circle.</summary>
        private const float TempleClear = 20f;
        /// <summary>Two sites closer than this get one post, the one with more ways.</summary>
        private const float SiteSpacing = 30f;
        /// <summary>A place this close (walked) to a post is in sight: no board for it.</summary>
        private const float Beside = 60f;
        /// <summary>A harbour stone this close to a main road's point gets a post there, beside the stone.</summary>
        private const float StoneReach = 40f;
        /// <summary>How far up the road from its harbour stone a landing's post stands.</summary>
        private const float StoneGap = 3f;
        /// <summary>How far over the water the ground under a pole must be.</summary>
        private const float DryMargin = 0.3f;
        /// <summary>The pole's footing is the lowest ground this far around it.</summary>
        private const float FootRadius = 0.5f;
        /// <summary>A board's far end from the pole, and how far the ground there may rise over the foot.</summary>
        private const float BoardReach = 1.6f;
        private const float MaxRise = 0.6f;
        /// <summary>How far past the road's edge the pole stands.</summary>
        private const float PostSide = 1.5f;
        /// <summary>A built piece this close to a post's spot sends it elsewhere.</summary>
        private const float Taken = 2.5f;
        /// <summary>How far the pole goes into the ground, so it never floats.</summary>
        private const float Sink = 0.4f;
        /// <summary>Clear pole above the top board, the gap between boards, and the lowest board's bottom over the ground.</summary>
        private const float TopClear = 0.3f;
        private const float BoardGap = 0.08f;
        private const float LowestBoard = 1.1f;

        /// <summary>
        /// Location prefab -> the hint its signs give (the user's, 2026-10-01). A boss is not
        /// here: its signs give its name in the game (<see cref="Progress.NameOf"/>).
        /// </summary>
        private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            { "Mistlands_DvergrTownEntrance1", "Dvergr mine" },
            { "Mistlands_DvergrTownEntrance2", "Dvergr mine" },
            { "CharredFortress", "Fortress" },
            { "NorthVillage", "Village" },
            { "TheHole01", "Tunnels" },
            { "MorkBorg", "Dark hall" },
            { "Vendor_BlackForest", "Trader" },
            { "Hildir_camp", "Merchant" },
            { "BogWitch_Camp", "Witch's hut" },
            { "AncientUpgradeStation", "Old forge" },
        };
        /// <summary>Places no sign names (the user, 2026-10-01: Eikthyr matters only for the first hours).</summary>
        private static readonly HashSet<string> Unsigned = new HashSet<string> { "Eikthyrnir" };
        private const string TempleHint = "Sacrificial stones";
        private const string BaseHint = "{0}'s hut";
        private const string NamelessBase = "Hut";

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Signposts", "Enabled", true,
                "Put up signposts at the main roads' forks and harbours, naming in hints what lies each way. Vanilla pieces: " +
                "players need no mod to see them.");
            Lines = config.Bind("Signposts", "Lines", 3, new ConfigDescription(
                "At most this many boards per direction; more places are grouped by region, and the farthest left to the next post.",
                new AcceptableValueRange<int>(1, 4)));
            Regions = config.Bind("Signposts", "Regions", "Mistlands, AshLands, DeepNorth",
                "Biomes named as a whole on a sign outside them (\"Deep North\"), not place by place; inside them, and everywhere " +
                "else, a sign names its places (\"Giant tree\"). Comma separated biome names.");
            Remote = config.Bind("Signposts", "Remote", "AshLands, DeepNorth",
                "Biomes whose signs name only their own places and the way back to the sacrificial stones, nothing of the " +
                "lands left behind. Comma separated biome names.");
        }

        /// <summary>A post as planned: where it stands and its boards, top down.</summary>
        internal sealed class Post
        {
            public Vector2 Site;
            public Vector2 Position;
            public float Ground;
            public bool Harbour;
            /// <summary>A landing's harbour stone the post stands beside, and the way up the road from it.</summary>
            public Vector3? Stone;
            public Vector2 Inland;
            /// <summary>From the pole toward the road, the side the boards read from.</summary>
            public Vector2 ToRoad;
            /// <summary>Every way the post stands between, signed or not: the pole keeps off all of them.</summary>
            public readonly List<Vector2> Ways = new List<Vector2>();
            public readonly List<Board> Boards = new List<Board>();

            public string Content
            {
                get
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (Board board in Boards)
                    {
                        sb.Append(board.Text).Append('@').Append(Mathf.RoundToInt(Mathf.Atan2(board.Toward.y, board.Toward.x) * Mathf.Rad2Deg)).Append('\n');
                    }
                    return sb.ToString();
                }
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder((Harbour ? "Harbour post at " : "Fork post at ") + Position.ToString("F0") + ":");
                foreach (Board board in Boards)
                {
                    sb.Append("\n  ").Append(Compass(board.Toward)).Append(": ").Append(board.Text);
                }
                return sb.ToString();
            }
        }

        internal struct Board
        {
            public string Text;
            /// <summary>The way the board points, flat and of length 1.</summary>
            public Vector2 Toward;
        }

        /// <summary>What a destination is: its words, its biome, how far.</summary>
        private struct Reached
        {
            public string Hint;
            public Heightmap.Biome Biome;
            public float Distance;
        }

        /// <summary>A post's node and the ways it shows; a harbour's also its stone and the way inland (-1 at a fork).</summary>
        private sealed class Site
        {
            public int Node;
            public List<int> Ways;
            public int Inland = -1;
            public Vector3? Stone;
        }

        /// <summary>The network as a graph of road points.</summary>
        private sealed class Graph
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public readonly List<List<int>> Next = new List<List<int>>();
            /// <summary>Node -> what is there: a road's end at its place, the sacrificial stones.</summary>
            public readonly Dictionary<int, string> Destinations = new Dictionary<int, string>();
            /// <summary>Node -> the biome of the destination there.</summary>
            public readonly Dictionary<int, Heightmap.Biome> Biomes = new Dictionary<int, Heightmap.Biome>();
            public List<Vector2> Temples;
            /// <summary>Per road, its nodes in order.</summary>
            public readonly List<int[]> Roads = new List<int[]>();

            public void Link(int a, int b)
            {
                if (a != b && !Next[a].Contains(b))
                {
                    Next[a].Add(b);
                    Next[b].Add(a);
                }
            }
        }

        /// <summary>
        /// Every post the network asks for, its lines worked out and its spot chosen; nothing
        /// built. A site without dry, free ground for a pole gets no post. done gets the posts.
        /// </summary>
        public static IEnumerator Plan(Network network, System.Action<List<Post>> done)
        {
            Graph graph = Build(network);
            Surface surface = new Surface();
            Dictionary<Vector2s, List<Vector2>> blocking = Blocking();
            yield return null;
            List<Site> sites = Sites(graph, surface, Harbours.Stones());
            HashSet<Heightmap.Biome> regions = BiomesOf(Regions.Value);
            HashSet<Heightmap.Biome> remote = BiomesOf(Remote.Value);
            List<Post> posts = new List<Post>();
            int fit = Fit();
            int dropped = 0;
            foreach (Site site in sites)
            {
                Post post = Lay(graph, site.Node, site.Ways, regions, remote);
                if (post == null)
                {
                    continue;
                }
                post.Stone = site.Stone;
                if (site.Inland >= 0)
                {
                    post.Harbour = true;
                    post.Inland = Heading(graph, site.Node, site.Inland);
                }
                Trim(post, fit);
                if (Place(post, blocking, surface))
                {
                    posts.Add(post);
                }
                else
                {
                    dropped++;
                    Debug.Log("[OdinsPaths] No ground for a signpost at " + post.Site.ToString("F0") + ".");
                }
                yield return null;
            }
            if (dropped > 0)
            {
                Debug.Log("[OdinsPaths] " + dropped + " signposts left out: no dry, free ground beside the road.");
            }
            done(posts);
        }

        /// <summary>Every built or placed piece but our posts' own, by zone: a pole keeps off them.</summary>
        private static Dictionary<Vector2s, List<Vector2>> Blocking()
        {
            Dictionary<Vector2s, List<Vector2>> blocking = new Dictionary<Vector2s, List<Vector2>>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetVec3(SiteKey, Vector3.zero) != Vector3.zero
                    || !(zdo.GetBool(Landings.PlacedKey) || zdo.GetLong(ZDOVars.s_creator, 0L) != 0L))
                {
                    continue;
                }
                Vector3 p = zdo.GetPosition();
                Vector2s zone = ZoneSystem.GetZone(p);
                if (!blocking.TryGetValue(zone, out List<Vector2> list))
                {
                    blocking[zone] = list = new List<Vector2>();
                }
                list.Add(new Vector2(p.x, p.z));
            }
            return blocking;
        }

        /// <summary>
        /// Brings the world's posts in line with the network: new ones placed, changed or broken
        /// ones rebuilt, ones the network lost removed. Returns through done what it did.
        /// </summary>
        public static IEnumerator Refresh(Network network, System.Action<string> done = null)
        {
            if (!Enabled.Value)
            {
                done?.Invoke("signposts are off");
                yield break;
            }
            ZDOMan world = ZDOMan.instance;
            List<Post> posts = null;
            yield return Plan(network, found => posts = found);
            if (ZDOMan.instance != world)
            {
                yield break;
            }
            GameObject pole = ZNetScene.instance.GetPrefab(PolePrefab);
            GameObject board = ZNetScene.instance.GetPrefab(BoardPrefab);
            if (pole == null || board == null)
            {
                Debug.LogWarning("[OdinsPaths] No " + PolePrefab + " or " + BoardPrefab + " prefab - no signposts.");
                done?.Invoke("no " + PolePrefab + " or " + BoardPrefab + " prefab");
                yield break;
            }

            // What stands: our posts by site.
            Dictionary<Vector3, List<ZDO>> standing = new Dictionary<Vector3, List<ZDO>>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                Vector3 site = zdo.GetVec3(SiteKey, Vector3.zero);
                if (site != Vector3.zero)
                {
                    if (!standing.TryGetValue(site, out List<ZDO> pieces))
                    {
                        standing[site] = pieces = new List<ZDO>();
                    }
                    pieces.Add(zdo);
                }
            }
            yield return null;

            int kept = 0, built = 0, removed = 0;
            HashSet<Vector3> wanted = new HashSet<Vector3>();
            foreach (Post post in posts)
            {
                if (ZDOMan.instance != world)
                {
                    yield break;
                }
                Vector3 key = Key(post);
                wanted.Add(key);
                if (standing.TryGetValue(key, out List<ZDO> pieces) && Intact(pieces, post))
                {
                    kept++;
                    continue;
                }
                if (pieces != null)
                {
                    Remove(pieces);
                }
                Build(post, key, pole, board);
                built++;
                yield return null;
            }
            foreach (KeyValuePair<Vector3, List<ZDO>> entry in standing)
            {
                if (!wanted.Contains(entry.Key))
                {
                    Remove(entry.Value);
                    removed++;
                }
            }
            string report = posts.Count + " signposts: " + built + " put up, " + kept + " kept, " + removed + " taken down";
            Debug.Log("[OdinsPaths] " + report + ".");
            done?.Invoke(report);
        }

        private static Graph Build(Network network)
        {
            Graph graph = new Graph { Temples = Planner.Temples() };
            List<Vector2> temples = graph.Temples;
            HashSet<int> templesNamed = new HashSet<int>();
            Dictionary<Vector2, string> bases = BaseNames(network);
            foreach (Network.Road road in network.Roads)
            {
                // Main roads only (the user, 2026-10-01): a spur's fork needs no post.
                if (road.Points.Count < 2 || road.Kind != RoadKind.Main)
                {
                    continue;
                }
                int[] nodes = new int[road.Points.Count];
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i] = graph.Points.Count;
                    graph.Points.Add(road.Points[i]);
                    graph.Next.Add(new List<int>());
                    if (i > 0)
                    {
                        graph.Link(nodes[i - 1], nodes[i]);
                    }
                }
                graph.Roads.Add(nodes);
                int end = nodes[nodes.Length - 1];
                Vector2 last = road.Points[road.Points.Count - 1];
                string hint = HintFor(road, bases);
                if (hint != null)
                {
                    graph.Destinations[end] = hint;
                    graph.Biomes[end] = WorldGenerator.instance.GetBiome(last.x, last.y);
                }
                Vector2 first = road.Points[0];
                // Once per temple: several of its roads would read as a whole biome of places.
                int temple = temples.FindIndex(t => (t - first).sqrMagnitude < JoinRadius * JoinRadius);
                if (temple >= 0 && templesNamed.Add(temple))
                {
                    graph.Destinations[nodes[0]] = TempleHint;
                    graph.Biomes[nodes[0]] = WorldGenerator.instance.GetBiome(first.x, first.y);
                }
            }

            // Each road joins the network where it set out: its first point on another road's.
            Dictionary<Vector2s, List<int>> buckets = new Dictionary<Vector2s, List<int>>();
            for (int i = 0; i < graph.Points.Count; i++)
            {
                Vector2s cell = Cell(graph.Points[i]);
                if (!buckets.TryGetValue(cell, out List<int> list))
                {
                    buckets[cell] = list = new List<int>();
                }
                list.Add(i);
            }
            foreach (int[] nodes in graph.Roads)
            {
                int start = nodes[0];
                int last = nodes[nodes.Length - 1];
                Vector2 at = graph.Points[start];
                Vector2s cell = Cell(at);
                int best = -1;
                float bestDistance = JoinRadius * JoinRadius;
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (!buckets.TryGetValue(new Vector2s(cell.x + x, cell.y + y), out List<int> list))
                        {
                            continue;
                        }
                        foreach (int other in list)
                        {
                            // Not a point of its own road.
                            if (other >= start && other <= last)
                            {
                                continue;
                            }
                            float d = (graph.Points[other] - at).sqrMagnitude;
                            if (d < bestDistance)
                            {
                                bestDistance = d;
                                best = other;
                            }
                        }
                    }
                }
                if (best >= 0)
                {
                    graph.Link(start, best);
                }
            }
            return graph;
        }

        private static Vector2s Cell(Vector2 at) => new Vector2s(Mathf.FloorToInt(at.x / JoinRadius), Mathf.FloorToInt(at.y / JoinRadius));

        /// <summary>
        /// Where posts go, with the ways each one shows: every fork (three ways or more) on dry
        /// ground, and every harbour stone beside a main road, at the road's dry point nearest it.
        /// Sites close together share one post: a harbour's first, then the one with more ways.
        /// </summary>
        private static List<Site> Sites(Graph graph, Surface surface, List<ZDO> stones)
        {
            List<Site> sites = new List<Site>();
            for (int i = 0; i < graph.Points.Count; i++)
            {
                Vector2 at = graph.Points[i];
                if (graph.Next[i].Count >= 3 && Dry(surface, at) && !graph.Temples.Exists(t => (t - at).sqrMagnitude < TempleClear * TempleClear))
                {
                    sites.Add(new Site { Node = i, Ways = new List<int>(graph.Next[i]) });
                }
            }
            foreach (ZDO zdo in stones)
            {
                Vector3 stone = zdo.GetPosition();
                Vector2 flat = new Vector2(stone.x, stone.z);
                int node = -1;
                float best = StoneReach * StoneReach;
                for (int i = 0; i < graph.Points.Count; i++)
                {
                    float d = (graph.Points[i] - flat).sqrMagnitude;
                    if (d < best && Dry(surface, graph.Points[i]))
                    {
                        best = d;
                        node = i;
                    }
                }
                if (node < 0)
                {
                    continue;
                }
                // Inland is the way whose ground stays highest; the other runs down to the water.
                int inland = -1;
                float highest = float.MinValue;
                foreach (int next in graph.Next[node])
                {
                    float low = LowestAlong(graph, surface, node, next);
                    if (low > highest)
                    {
                        highest = low;
                        inland = next;
                    }
                }
                sites.Add(new Site { Node = node, Ways = new List<int>(graph.Next[node]), Inland = inland, Stone = stone });
            }
            sites.Sort((a, b) => a.Stone.HasValue != b.Stone.HasValue ? (a.Stone.HasValue ? -1 : 1) : b.Ways.Count.CompareTo(a.Ways.Count));
            List<Site> kept = new List<Site>();
            foreach (Site site in sites)
            {
                Vector2 at = graph.Points[site.Node];
                if (!kept.Exists(k => (graph.Points[k.Node] - at).sqrMagnitude < SiteSpacing * SiteSpacing))
                {
                    kept.Add(site);
                }
            }
            return kept;
        }

        private static bool Dry(Surface surface, Vector2 at) => surface.Height(at.x, at.y) > ZoneSystem.instance.m_waterLevel + DryMargin;

        /// <summary>The lowest ground a few points along the way from node through next.</summary>
        private static float LowestAlong(Graph graph, Surface surface, int node, int next)
        {
            int previous = node;
            int at = next;
            float low = float.MaxValue;
            for (int step = 0; step < 5; step++)
            {
                Vector2 p = graph.Points[at];
                low = Mathf.Min(low, surface.Height(p.x, p.y));
                if (graph.Next[at].Count != 2)
                {
                    break;
                }
                int further = graph.Next[at][0] == previous ? graph.Next[at][1] : graph.Next[at][0];
                previous = at;
                at = further;
            }
            return low;
        }

        /// <summary>
        /// A post at node site: per way, what is reached through it (the way its shortest walk
        /// from here leaves by), grouped into at most <see cref="Lines"/> boards. Places in sight
        /// of the post get none. Null when no way leads anywhere named.
        /// </summary>
        private static Post Lay(Graph graph, int site, List<int> ways, HashSet<Heightmap.Biome> regions, HashSet<Heightmap.Biome> remote)
        {
            int count = graph.Points.Count;
            float[] distance = new float[count];
            int[] way = new int[count];
            for (int i = 0; i < count; i++)
            {
                distance[i] = float.MaxValue;
            }
            Heap open = new Heap();
            distance[site] = 0f;
            way[site] = -1;
            open.Push(site, 0f);
            while (open.Count > 0)
            {
                int u = open.Pop(out float du);
                if (du > distance[u])
                {
                    continue;
                }
                foreach (int v in graph.Next[u])
                {
                    if (u == site && !ways.Contains(v))
                    {
                        continue;
                    }
                    float step = Vector2.Distance(graph.Points[u], graph.Points[v]);
                    float dv = du + step;
                    if (dv >= distance[v])
                    {
                        continue;
                    }
                    distance[v] = dv;
                    way[v] = u == site ? ways.IndexOf(v) : way[u];
                    open.Push(v, dv);
                }
            }

            List<Reached>[] reached = new List<Reached>[ways.Count];
            for (int w = 0; w < ways.Count; w++)
            {
                reached[w] = new List<Reached>();
            }
            foreach (KeyValuePair<int, string> destination in graph.Destinations)
            {
                int node = destination.Key;
                if (node == site || distance[node] == float.MaxValue || way[node] < 0 || distance[node] < Beside)
                {
                    continue;
                }
                reached[way[node]].Add(new Reached
                {
                    Hint = destination.Value,
                    Biome = graph.Biomes[node],
                    Distance = distance[node],
                });
            }

            Vector2 here = graph.Points[site];
            Heightmap.Biome own = WorldGenerator.instance.GetBiome(here.x, here.y);
            Post post = new Post { Site = here };
            for (int w = 0; w < ways.Count; w++)
            {
                List<string> lines = LinesFor(reached[w], own, regions, remote.Contains(own), Lines.Value);
                Vector2 toward = Heading(graph, site, ways[w]);
                post.Ways.Add(toward);
                foreach (string line in lines)
                {
                    post.Boards.Add(new Board { Text = line, Toward = toward });
                }
            }
            return post.Boards.Count > 0 ? post : null;
        }

        /// <summary>
        /// One way's lines, nearest first: the places of a region biome other than the post's own
        /// are that biome; every other place is its hint, the same words once. A post in a remote
        /// biome names only its own biome's places and the sacrificial stones. The nearest limit
        /// lines stay.
        /// </summary>
        private static List<string> LinesFor(List<Reached> reached, Heightmap.Biome own, HashSet<Heightmap.Biome> regions, bool remote, int limit)
        {
            List<KeyValuePair<float, string>> lines = new List<KeyValuePair<float, string>>();
            foreach (Reached r in reached)
            {
                if (remote && r.Biome != own && r.Hint != TempleHint)
                {
                    continue;
                }
                bool region = r.Biome != own && regions.Contains(r.Biome);
                lines.Add(new KeyValuePair<float, string>(r.Distance, region ? BiomeName(r.Biome) : r.Hint));
            }
            lines.Sort((a, b) => a.Key.CompareTo(b.Key));
            List<string> result = new List<string>();
            foreach (KeyValuePair<float, string> line in lines)
            {
                if (result.Count < limit && !result.Contains(line.Value))
                {
                    result.Add(line.Value);
                }
            }
            return result;
        }

        /// <summary>The biomes of a comma separated setting; a name the game does not know is skipped.</summary>
        private static HashSet<Heightmap.Biome> BiomesOf(string setting)
        {
            HashSet<Heightmap.Biome> regions = new HashSet<Heightmap.Biome>();
            foreach (string name in setting.Split(','))
            {
                string trimmed = name.Trim();
                if (trimmed.Length > 0 && System.Enum.TryParse(trimmed, true, out Heightmap.Biome biome))
                {
                    regions.Add(biome);
                }
            }
            return regions;
        }

        private static string BiomeName(Heightmap.Biome biome)
        {
            string name = Localization.instance != null ? Localization.instance.Localize(BiomeSector.GetBiomeName(biome)) : "";
            return name.Length > 0 && !name.StartsWith("[") && !name.StartsWith("$") ? name : biome.ToString();
        }

        /// <summary>What a road's end is called on a sign: the hint for its location, a base by its ward's placer; null for no sign.</summary>
        private static string HintFor(Network.Road road, Dictionary<Vector2, string> bases)
        {
            foreach (KeyValuePair<Vector2, string> ward in bases)
            {
                if ((ward.Key - road.Goal).sqrMagnitude < 1f)
                {
                    return ward.Value.Length > 0 ? string.Format(BaseHint, ward.Value) : NamelessBase;
                }
            }
            string target = road.Target ?? "";
            int number = target.IndexOf(" #");
            if (number >= 0)
            {
                target = target.Substring(0, number);
            }
            string location = target.Split('|')[0];
            if (Unsigned.Contains(location))
            {
                return null;
            }
            return Hints.TryGetValue(location, out string hint) ? hint : Progress.NameOf(location);
        }

        /// <summary>The network's bases (their first ward) -> the name of the player who placed the ward.</summary>
        private static Dictionary<Vector2, string> BaseNames(Network network)
        {
            Dictionary<Vector2, string> names = new Dictionary<Vector2, string>();
            if (network.Bases.Count == 0)
            {
                return names;
            }
            int marker = OdinsPathsPlugin.BaseMarker.Value.GetStableHashCode();
            foreach (Vector2 ward in network.Bases)
            {
                names[ward] = "";
            }
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetPrefab() != marker)
                {
                    continue;
                }
                Vector3 p = zdo.GetPosition();
                Vector2 at = new Vector2(p.x, p.z);
                foreach (Vector2 ward in network.Bases)
                {
                    if ((ward - at).sqrMagnitude < 1f)
                    {
                        names[ward] = zdo.GetString(ZDOVars.s_creatorName);
                    }
                }
            }
            return names;
        }

        /// <summary>The way from site through next, looked at a few points on so a bend at the fork does not turn it.</summary>
        private static Vector2 Heading(Graph graph, int site, int next)
        {
            int previous = site;
            int at = next;
            for (int step = 0; step < 2 && graph.Next[at].Count == 2; step++)
            {
                int further = graph.Next[at][0] == previous ? graph.Next[at][1] : graph.Next[at][0];
                previous = at;
                at = further;
            }
            Vector2 toward = graph.Points[at] - graph.Points[site];
            return toward.sqrMagnitude > 0f ? toward.normalized : Vector2.up;
        }

        /// <summary>
        /// Where the pole stands: off the road in the widest gap between its ways, far enough that
        /// both roads beside the gap pass clear; moved out further where something is built there,
        /// and into the next gap where that is water. False where no gap has dry, free ground.
        /// </summary>
        private static bool Place(Post post, Dictionary<Vector2s, List<Vector2>> blocking, Surface surface)
        {
            if (post.Stone.HasValue && BesideStone(post, blocking, surface))
            {
                return true;
            }
            List<float> angles = new List<float>();
            foreach (Vector2 way in post.Ways)
            {
                float a = Mathf.Atan2(way.y, way.x);
                if (!angles.Exists(b => Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg)) < 1f))
                {
                    angles.Add(a);
                }
            }
            angles.Sort();
            // Each gap as (its size, its bisector), widest first; a way alone has a side each way, square to it.
            List<KeyValuePair<float, float>> gaps = new List<KeyValuePair<float, float>>();
            if (angles.Count == 1)
            {
                gaps.Add(new KeyValuePair<float, float>(Mathf.PI, angles[0] + Mathf.PI * 0.5f));
                gaps.Add(new KeyValuePair<float, float>(Mathf.PI, angles[0] - Mathf.PI * 0.5f));
            }
            else
            {
                for (int i = 0; i < angles.Count; i++)
                {
                    float from = angles[i];
                    float to = i + 1 < angles.Count ? angles[i + 1] : angles[0] + 2f * Mathf.PI;
                    gaps.Add(new KeyValuePair<float, float>(to - from, from + (to - from) * 0.5f));
                }
                gaps.Sort((a, b) => b.Key.CompareTo(a.Key));
            }
            foreach (KeyValuePair<float, float> gap in gaps)
            {
                Vector2 out_ = new Vector2(Mathf.Cos(gap.Value), Mathf.Sin(gap.Value));
                float clear = (RoadKind.Main.MaxHalfWidth + PostSide) / Mathf.Max(0.3f, Mathf.Sin(Mathf.Min(gap.Key, Mathf.PI) * 0.5f));
                Vector2 spot = post.Site + out_ * clear;
                for (int tries = 0; tries < 4; tries++, spot += out_ * Taken)
                {
                    if (Blocked(spot, blocking) || !Footing(post, spot, surface, out float ground))
                    {
                        continue;
                    }
                    post.Position = spot;
                    post.Ground = ground;
                    post.ToRoad = post.Site - spot;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether a pole may stand at spot, and the height its foot goes in at: the lowest ground
        /// around it, so no side shows a gap. Not in the water or the shallows, and not on a slope
        /// that would bury a board's far end.
        /// </summary>
        private static bool Footing(Post post, Vector2 spot, Surface surface, out float ground)
        {
            ground = surface.Height(spot.x, spot.y);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Vector2 at = spot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * FootRadius;
                ground = Mathf.Min(ground, surface.Height(at.x, at.y));
            }
            if (ground <= ZoneSystem.instance.m_waterLevel + DryMargin)
            {
                return false;
            }
            foreach (Vector2 way in post.Ways)
            {
                Vector2 end = spot + way * BoardReach;
                if (surface.Height(end.x, end.y) - ground > MaxRise)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A landing's post beside its harbour stone, a little up the road from it on the same
        /// side, so it is passed on the way to the boat: the stone already stands clear of the road.
        /// False where the stone is on a dock or the spot is wet - the post then stands at the fork.
        /// </summary>
        private static bool BesideStone(Post post, Dictionary<Vector2s, List<Vector2>> blocking, Surface surface)
        {
            Vector3 stone = post.Stone.Value;
            Vector2 spot = new Vector2(stone.x, stone.z) + post.Inland * StoneGap;
            for (int tries = 0; tries < 4 && Blocked(spot, blocking); tries++)
            {
                spot += post.Inland * Taken;
            }
            // On a dock the stone stands well over the ground.
            if (!Footing(post, spot, surface, out float ground) || Mathf.Abs(stone.y - ground) > 1.5f)
            {
                return false;
            }
            post.Position = spot;
            post.Ground = ground;
            // The road is across from the stone's side of it, not back at the site up the road.
            Vector2 right = new Vector2(post.Inland.y, -post.Inland.x);
            post.ToRoad = Vector2.Dot(new Vector2(stone.x, stone.z) - post.Site, right) > 0f ? -right : right;
            return true;
        }

        /// <summary>
        /// The ground as it stands, not as generated: the generator's height plus the zone's
        /// terrain edits (the road's levelling, a player's digging), as <c>TerrainComp</c> applies
        /// them to the heightmap's vertices. Read from the compiler ZDOs, so the zone need not be loaded.
        /// </summary>
        private sealed class Surface
        {
            private readonly Dictionary<Vector2s, TerrainWriter.TerrainData> zones = new Dictionary<Vector2s, TerrainWriter.TerrainData>();
            private readonly int width;
            private readonly float scale;

            public Surface()
            {
                Heightmap map = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
                width = map.m_width;
                scale = map.m_scale;
            }

            public float Height(float x, float z)
            {
                float height = Ground.Height(x, z);
                Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0f, z));
                if (!zones.TryGetValue(zone, out TerrainWriter.TerrainData terrain))
                {
                    ZDO compiler = TerrainWriter.FindCompiler(zone);
                    byte[] bytes = compiler != null ? compiler.GetByteArray(ZDOVars.s_TCData) : null;
                    zones[zone] = terrain = bytes != null ? TerrainWriter.TerrainData.Decode(bytes, width + 1) : null;
                }
                if (terrain == null)
                {
                    return height;
                }
                Vector3 center = ZoneSystem.GetZonePos(zone);
                float fx = Mathf.Clamp((x - center.x) / scale + width * 0.5f, 0f, width);
                float fz = Mathf.Clamp((z - center.z) / scale + width * 0.5f, 0f, width);
                int x0 = Mathf.Min(Mathf.FloorToInt(fx), width - 1);
                int z0 = Mathf.Min(Mathf.FloorToInt(fz), width - 1);
                float tx = fx - x0;
                float tz = fz - z0;
                float d00 = Delta(terrain, x0, z0), d10 = Delta(terrain, x0 + 1, z0);
                float d01 = Delta(terrain, x0, z0 + 1), d11 = Delta(terrain, x0 + 1, z0 + 1);
                return height + Mathf.Lerp(Mathf.Lerp(d00, d10, tx), Mathf.Lerp(d01, d11, tx), tz);
            }

            private float Delta(TerrainWriter.TerrainData terrain, int x, int z)
            {
                int i = z * (width + 1) + x;
                return terrain.ModifiedHeight[i] ? terrain.LevelDelta[i] + terrain.SmoothDelta[i] : 0f;
            }
        }

        private static bool Blocked(Vector2 spot, Dictionary<Vector2s, List<Vector2>> blocking)
        {
            Vector2s zone = ZoneSystem.GetZone(new Vector3(spot.x, 0f, spot.y));
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (blocking.TryGetValue(new Vector2s(zone.x + x, zone.y + y), out List<Vector2> list)
                        && list.Exists(p => (p - spot).sqrMagnitude < Taken * Taken))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static Vector3 Key(Post post) => new Vector3(Mathf.Round(post.Position.x * 10f) / 10f, 0f, Mathf.Round(post.Position.y * 10f) / 10f);

        /// <summary>Whether a standing post is this one, every piece still there.</summary>
        private static bool Intact(List<ZDO> pieces, Post post)
        {
            ZDO pole = pieces.Find(z => z.GetString(ContentKey, null) != null);
            return pole != null && pole.GetString(ContentKey) == post.Content && pole.GetInt(PiecesKey) == pieces.Count;
        }

        private static void Remove(List<ZDO> pieces)
        {
            foreach (ZDO zdo in pieces)
            {
                // The server may take any ZDO over; DestroyZDO only sends what its owner destroys.
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
            }
        }

        /// <summary>
        /// The pole, its foot in the ground, and its boards from the top down, each pointing its
        /// way with its text toward the road, worn but whole.
        /// </summary>
        private static void Build(Post post, Vector3 key, GameObject polePrefab, GameObject boardPrefab)
        {
            System.Random rng = new System.Random(Mathf.RoundToInt(post.Position.x) * 7919 + Mathf.RoundToInt(post.Position.y));
            Bounds poleShape = Builder.Shape(polePrefab);
            Bounds boardShape = Builder.Shape(boardPrefab);
            float foot = post.Ground - Sink;
            Vector3 polePosition = new Vector3(post.Position.x, foot - poleShape.min.y, post.Position.y);
            ZDO pole = Spawn(polePrefab, polePosition, Quaternion.identity, key, rng);
            float poleRadius = Mathf.Max(poleShape.extents.x, poleShape.extents.z);

            // The side its text reads from, in the board's own frame: against the text widget's forward.
            Vector3 reads = Vector3.back;
            // Read as a plain Component: the mod does not reference TextMeshPro.
            Sign sign = boardPrefab.GetComponent<Sign>();
            Component widget = sign != null ? HarmonyLib.AccessTools.Field(typeof(Sign), "m_textWidget").GetValue(sign) as Component : null;
            if (widget != null)
            {
                reads = -boardPrefab.transform.InverseTransformDirection(widget.transform.forward);
            }
            reads.y = 0f;
            reads = reads.sqrMagnitude > 0f ? reads.normalized : Vector3.back;
            // Its long side is across that, flat.
            Vector3 along = Vector3.Cross(Vector3.up, reads);
            float halfLength = Mathf.Abs(Vector3.Dot(boardShape.extents, new Vector3(Mathf.Abs(along.x), 0f, Mathf.Abs(along.z))));
            float boardHeight = boardShape.size.y;

            float top = foot + poleShape.size.y - TopClear - boardHeight * 0.5f;
            Vector2 toRoad = post.ToRoad;
            int pieces = 1;
            for (int i = 0; i < post.Boards.Count; i++)
            {
                Board board = post.Boards[i];
                Vector2 normal = new Vector2(-board.Toward.y, board.Toward.x);
                if (Vector2.Dot(normal, toRoad) < 0f)
                {
                    normal = -normal;
                }
                Quaternion rotation = Quaternion.LookRotation(new Vector3(normal.x, 0f, normal.y)) * Quaternion.Inverse(Quaternion.LookRotation(reads));
                Vector2 middle = post.Position + board.Toward * (halfLength + poleRadius);
                Vector3 centre = new Vector3(middle.x, top - i * (boardHeight + BoardGap), middle.y);
                ZDO zdo = Spawn(boardPrefab, centre - rotation * boardShape.center, rotation, key, rng);
                zdo.Set(ZDOVars.s_text, board.Text);
                pieces++;
            }
            pole.Set(ContentKey, post.Content);
            pole.Set(PiecesKey, pieces);
        }

        /// <summary>How many boards a pole has room for, from the top down to <see cref="LowestBoard"/>.</summary>
        private static int Fit()
        {
            GameObject pole = ZNetScene.instance.GetPrefab(PolePrefab);
            GameObject board = ZNetScene.instance.GetPrefab(BoardPrefab);
            if (pole == null || board == null)
            {
                return int.MaxValue;
            }
            float boardHeight = Builder.Shape(board).size.y;
            float room = Builder.Shape(pole).size.y - Sink - TopClear - LowestBoard;
            return Mathf.Max(1, Mathf.FloorToInt((room + BoardGap) / (boardHeight + BoardGap)));
        }

        /// <summary>Fewer boards where the pole has no room for all: one off the way with the most, until they fit.</summary>
        private static void Trim(Post post, int fit)
        {
            while (post.Boards.Count > fit)
            {
                Dictionary<Vector2, int> counts = new Dictionary<Vector2, int>();
                foreach (Board board in post.Boards)
                {
                    counts.TryGetValue(board.Toward, out int n);
                    counts[board.Toward] = n + 1;
                }
                Vector2 most = post.Boards[0].Toward;
                foreach (KeyValuePair<Vector2, int> count in counts)
                {
                    if (count.Value > counts[most])
                    {
                        most = count.Key;
                    }
                }
                post.Boards.RemoveAt(post.Boards.FindLastIndex(b => b.Toward == most));
            }
        }

        private static ZDO Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 key, System.Random rng)
        {
            ZDO zdo = Landings.Spawn(prefab, position, rotation);
            zdo.Set(SiteKey, key);
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                // Worn, never broken: every piece stays.
                zdo.Set(ZDOVars.s_health, Mathf.Lerp(0.45f, 0.8f, (float)rng.NextDouble()) * wear.m_health);
            }
            return zdo;
        }

        private static string Compass(Vector2 toward)
        {
            string[] names = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            float angle = Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg;
            return names[((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8];
        }

        /// <summary>A binary min-heap of nodes by distance.</summary>
        private sealed class Heap
        {
            private readonly List<KeyValuePair<float, int>> items = new List<KeyValuePair<float, int>>();
            public int Count => items.Count;

            public void Push(int node, float key)
            {
                items.Add(new KeyValuePair<float, int>(key, node));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].Key <= items[i].Key)
                    {
                        break;
                    }
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public int Pop(out float key)
            {
                KeyValuePair<float, int> top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = 2 * i + 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left < items.Count && items[left].Key < items[smallest].Key)
                    {
                        smallest = left;
                    }
                    if (right < items.Count && items[right].Key < items[smallest].Key)
                    {
                        smallest = right;
                    }
                    if (smallest == i)
                    {
                        break;
                    }
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
                key = top.Key;
                return top.Value;
            }
        }
    }
}
