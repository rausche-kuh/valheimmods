using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The main network's harbours: where a main road leaves the land for the sea and where it
    /// comes ashore again stands a vegvisir with blue runes, and using it pins every harbour
    /// across the water from it - the one on the far shore of its crossing, and more where
    /// several roads set out from the same shore (a harbour within <see cref="Merge"/> of a new
    /// landing is that landing's harbour, and gains a link) - and the stone's own harbour, if the
    /// player has no pin there yet.
    ///
    /// Vanilla vegvisirs are no network objects, only children of the locations every client
    /// builds itself, so the stone is a prefab of the mod's own (<see cref="PrefabName"/>): the
    /// start temple's vegvisir, copied with a <c>ZNetView</c> and blue runes, registered on the
    /// server and on every client that has the mod. A client without the mod sees no stone (and
    /// logs "Missing prefab hash" near one); only the server removes a ZDO of an unknown prefab.
    ///
    /// Using the stone runs the vanilla path: the client asks the server for the closest
    /// <see cref="LocationName"/>, which no location is called, and the server's
    /// <c>RPC_DiscoverClosestLocation</c> prefix (<see cref="OdinsPathsPlugin.RevealPinned"/>)
    /// answers with the harbours linked to the stone at that point (<see cref="Answer"/>). The
    /// links are kept on the stone's own ZDO, as the positions of the stones across.
    /// </summary>
    internal static class Harbours
    {
        public const string PrefabName = "OdinsPaths_Harbour";
        public static readonly int Hash = PrefabName.GetStableHashCode();
        /// <summary>What the stone asks the server for; no location has this name.</summary>
        public const string LocationName = "OdinsPaths_Harbour";
        /// <summary>The location whose vegvisir the stone is copied from: unique, always there, and near every spawn.</summary>
        private const string SourceLocation = "StartTemple";
        private static readonly int LinksKey = "OdinsPaths_HarbourLinks".GetStableHashCode();
        /// <summary>A landing this close to a harbour is that harbour: roads leaving one shore for different islands share their stone.</summary>
        private const float Merge = 25f;
        /// <summary>
        /// How far from an older road's shore point (rebuilt from its 8 m network points) its
        /// harbour stone may stand: on the dock or up the road, and the shore itself moves a little.
        /// </summary>
        private const float ForkReach = 40f;
        /// <summary>A stone's group colour on its ZDO, as an index into <see cref="Colours"/> plus one (0: none yet).</summary>
        private static readonly int GroupKey = "OdinsPaths_HarbourGroup".GetStableHashCode();
        /// <summary>
        /// The groups' colours on the map - every harbour linked to another, directly or through
        /// others, is one group. No red: the vanilla vegvisirs' pins are white on red runes.
        /// </summary>
        internal static readonly Color[] Colours =
        {
            new Color(0.25f, 0.55f, 1f),   // blue
            new Color(1f, 0.82f, 0.2f),    // yellow
            new Color(0.45f, 0.9f, 0.3f),  // green
            new Color(0.9f, 0.4f, 0.9f),   // magenta
            new Color(0.2f, 0.85f, 0.8f),  // teal
            new Color(1f, 0.55f, 0.15f),   // orange
            new Color(0.62f, 0.5f, 1f),    // violet
            new Color(0.75f, 0.95f, 0.65f), // pale green
        };
        /// <summary>A structure this close to where a stone beside the road would stand: it goes further up the road.</summary>
        private const float Taken = 2f;
        /// <summary>How many trail points (2 m each) up the road a stone may move off something built.</summary>
        private const int Inland = 5;
        /// <summary>How far past the road's edge a stone beside it stands.</summary>
        private const float StoneSide = 1.2f;
        /// <summary>A stone this close to the point a client asked from is the one it used.</summary>
        private const float Asked = 3f;
        /// <summary>
        /// The runes' emission, blue where the vanilla vegvisir's is (1, 0, 0). Its shader
        /// (Custom/StaticRock) reads <c>_EmissionColor</c>; the material also keeps a stale
        /// <c>_EmissiveColor</c> that the shader has no property for.
        /// </summary>
        private static readonly Color Runes = new Color(0.1f, 0.45f, 1f);
        /// <summary>The point light's colour, blue where the vanilla one's is (1, 0.37, 0.37).</summary>
        private static readonly Color Glow = new Color(0.37f, 0.6f, 1f);
        private const string PinName = "Harbour";
        private const Minimap.PinType PinType = Minimap.PinType.Icon4;

        private static GameObject prefab;
        private static bool failed;

        /// <summary>
        /// Adds the stone to this scene's <c>ZNetScene</c>, building it the first time. Runs
        /// after <c>ZoneSystem.Start</c> has set the locations up, before any object is created.
        /// </summary>
        public static void Register()
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                return;
            }
            if (prefab == null && !failed)
            {
                prefab = Build();
                failed = prefab == null;
            }
            if (prefab != null)
            {
                ZNetScene.instance.m_namedPrefabs[Hash] = prefab;
            }
        }

        /// <summary>
        /// The start temple's vegvisir, copied under an inactive holder that outlives the scene
        /// (so a copy's <c>Awake</c> runs only once the game instantiates it), with a
        /// <c>ZNetView</c>, the question for <see cref="LocationName"/> and blue runes. The temple
        /// is loaded through its <c>SoftReference</c> (by reflection, as in <see cref="Footprints"/>)
        /// and never released: the copy shares its meshes and textures.
        /// </summary>
        private static GameObject Build()
        {
            ZoneSystem.ZoneLocation source = ZoneSystem.instance.m_locations.Find(l => l.m_prefabName == SourceLocation);
            object reference = source != null ? typeof(ZoneSystem.ZoneLocation).GetField("m_prefab")?.GetValue(source) : null;
            System.Type type = reference?.GetType();
            System.Reflection.PropertyInfo valid = type?.GetProperty("IsValid");
            System.Reflection.PropertyInfo asset = type?.GetProperty("Asset");
            System.Reflection.MethodInfo load = type?.GetMethod("Load", System.Type.EmptyTypes);
            if (valid == null || asset == null || load == null || !(bool)valid.GetValue(reference))
            {
                Debug.LogWarning("[OdinsPaths] No " + SourceLocation + " location to copy a vegvisir from - harbours get no stone.");
                return null;
            }
            load.Invoke(reference, null);
            Vegvisir original = (asset.GetValue(reference) as GameObject)?.GetComponentInChildren<Vegvisir>(true);
            if (original == null)
            {
                Debug.LogWarning("[OdinsPaths] " + SourceLocation + " holds no vegvisir - harbours get no stone.");
                return null;
            }
            GameObject holder = new GameObject("OdinsPaths_Prefabs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            GameObject stone = Object.Instantiate(original.gameObject, holder.transform);
            stone.name = PrefabName;
            stone.transform.localPosition = Vector3.zero;
            stone.transform.localRotation = Quaternion.identity;
            stone.SetActive(true);

            ZNetView view = stone.AddComponent<ZNetView>();
            view.m_persistent = true;
            view.m_type = ZDO.ObjectType.Default;

            Vegvisir vegvisir = stone.GetComponent<Vegvisir>();
            vegvisir.m_hoverName = PinName;
            vegvisir.m_setsGlobalKey = "";
            vegvisir.m_setsPlayerKey = "";
            vegvisir.m_locations = new List<Vegvisir.VegvisrLocation>
            {
                new Vegvisir.VegvisrLocation { m_locationName = LocationName, m_pinName = PinName, m_pinType = PinType, m_showMap = true },
            };

            int tinted = Tint(stone);
            if (tinted == 0)
            {
                Debug.LogWarning("[OdinsPaths] The vegvisir's material has no _EmissionColor - the harbour stones' runes stay red.");
            }
            return stone;
        }

        /// <summary>The runes' emission and the light turned blue, on copies of the materials; how many materials were.</summary>
        private static int Tint(GameObject stone)
        {
            Dictionary<Material, Material> blue = new Dictionary<Material, Material>();
            foreach (Renderer renderer in stone.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !material.HasProperty("_EmissionColor"))
                    {
                        continue;
                    }
                    if (!blue.TryGetValue(material, out Material copy))
                    {
                        copy = new Material(material) { name = material.name + " (harbour)" };
                        copy.SetColor("_EmissionColor", Runes);
                        blue[material] = copy;
                    }
                    materials[i] = copy;
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
            foreach (Light light in stone.GetComponentsInChildren<Light>(true))
            {
                light.color = Glow;
            }
            return blue.Count;
        }

        /// <summary>
        /// A stone at each landing of a main road's trail, the two shores of each crossing linked
        /// both ways; a landing near a harbour already there joins it. A road that forks off an
        /// older one out at sea (fork: where it sets out) has no shore behind it: its first
        /// harbour is linked to the shores of the older road's crossing instead
        /// (<see cref="ForkHarbours"/>). The stones placed, as ZDOs. Server only.
        /// </summary>
        public static List<ZDOID> Place(Trail trail, List<Landings.Landing> landings, Structures structures, Vector2? fork)
        {
            List<ZDOID> placed = new List<ZDOID>();
            if (landings.Count == 0)
            {
                return placed;
            }
            GameObject stone = ZNetScene.instance.GetPrefab(Hash);
            if (stone == null)
            {
                Debug.LogWarning("[OdinsPaths] No harbour stone prefab - landings left unmarked.");
                return placed;
            }
            List<ZDO> stones = Stones();
            List<ZDO> touched = new List<ZDO>();
            for (int i = 0; i < landings.Count; i++)
            {
                ZDO here = Harbour(trail, landings[i], stone, stones, structures, placed);
                touched.Add(here);
                // The shore across is the next landing of the same crossing; a trail that ends
                // or starts in the water has a crossing with one shore.
                if (i + 1 < landings.Count && landings[i + 1].Crossing == landings[i].Crossing)
                {
                    ZDO there = Harbour(trail, landings[i + 1], stone, stones, structures, placed);
                    touched.Add(there);
                    Link(here, there);
                    Link(there, here);
                    i++;
                }
            }
            if (fork.HasValue && SetsOutAtSea(trail, landings))
            {
                LinkFork(touched[0], fork.Value, stones);
            }
            foreach (ZDO zdo in touched)
            {
                Colour(zdo, stones);
            }
            return placed;
        }

        /// <summary>Whether the trail starts in the water: its first landing is a shore it comes to, with none it left from.</summary>
        private static bool SetsOutAtSea(Trail trail, List<Landings.Landing> landings)
        {
            return landings.Count > 0 && trail.Water[0] && landings[0].Toward < landings[0].Shore;
        }

        /// <summary>Links a sea fork's first harbour both ways with the harbours of the crossing it forked off.</summary>
        private static int LinkFork(ZDO first, Vector2 fork, List<ZDO> stones)
        {
            int linked = 0;
            foreach (ZDO parent in ForkHarbours(fork, stones))
            {
                if (parent != first)
                {
                    Link(first, parent);
                    Link(parent, first);
                    linked++;
                }
            }
            return linked;
        }

        /// <summary>
        /// The harbours at both shores of the crossing a road forks off at sea: the older main
        /// road through the fork point (a network point of it, not its first), walked from there
        /// both ways to its last dry points, each shore's nearest stone within
        /// <see cref="ForkReach"/>. None if the fork is on land or no such road is known.
        /// </summary>
        private static List<ZDO> ForkHarbours(Vector2 fork, List<ZDO> stones)
        {
            List<ZDO> found = new List<ZDO>();
            Network network = Network.Current;
            if (network == null)
            {
                return found;
            }
            foreach (Network.Road road in network.Roads)
            {
                if (road.Kind != RoadKind.Main || road.Points.FindIndex(p => (p - fork).sqrMagnitude < 1f) <= 0)
                {
                    continue;
                }
                Trail parent = new Trail(road.Points, road.Kind);
                int at = 0;
                float best = float.MaxValue;
                for (int i = 0; i < parent.Points.Count; i++)
                {
                    float distance = (parent.Points[i] - fork).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        at = i;
                    }
                }
                if (!parent.Water[at])
                {
                    continue;
                }
                int before = at;
                while (before >= 0 && parent.Water[before])
                {
                    before--;
                }
                int after = at;
                while (after < parent.Points.Count && parent.Water[after])
                {
                    after++;
                }
                foreach (int shore in new[] { before, after })
                {
                    if (shore < 0 || shore >= parent.Points.Count)
                    {
                        continue;
                    }
                    ZDO nearest = Nearest(stones, parent.Points[shore], ForkReach);
                    if (nearest != null && !found.Contains(nearest))
                    {
                        found.Add(nearest);
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Links every road of the network that sets out at sea to the crossing it forked off,
        /// for harbours placed before sea forks were linked, and colours every group. How many
        /// links were added. Server only.
        /// </summary>
        public static int Relink(Network network)
        {
            List<ZDO> stones = Stones();
            int linked = 0;
            foreach (Network.Road road in network.Roads)
            {
                if (road.Kind != RoadKind.Main || road.Points.Count < 2)
                {
                    continue;
                }
                Trail trail = new Trail(road.Points, road.Kind);
                List<Landings.Landing> landings = Landings.Find(trail);
                if (!SetsOutAtSea(trail, landings))
                {
                    continue;
                }
                ZDO first = Nearest(stones, trail.Points[landings[0].Shore], ForkReach);
                if (first != null)
                {
                    linked += LinkFork(first, road.Points[0], stones);
                }
            }
            foreach (ZDO zdo in stones)
            {
                Colour(zdo, stones);
            }
            return linked;
        }

        private static ZDO Nearest(List<ZDO> stones, Vector2 point, float within)
        {
            ZDO nearest = null;
            float best = within;
            foreach (ZDO zdo in stones)
            {
                float distance = Vector2.Distance(Flat(zdo.GetPosition()), point);
                if (distance < best)
                {
                    best = distance;
                    nearest = zdo;
                }
            }
            return nearest;
        }

        /// <summary>The harbour a landing belongs to: the stone within <see cref="Merge"/>, or a new one on its shore.</summary>
        private static ZDO Harbour(Trail trail, Landings.Landing landing, GameObject stone, List<ZDO> stones, Structures structures, List<ZDOID> placed)
        {
            Vector2 shore = trail.Points[landing.Shore];
            ZDO nearest = null;
            float best = Merge;
            foreach (ZDO zdo in stones)
            {
                float distance = Vector2.Distance(Flat(zdo.GetPosition()), shore);
                if (distance < best)
                {
                    best = distance;
                    nearest = zdo;
                }
            }
            if (nearest != null)
            {
                return nearest;
            }
            int seed = Mathf.RoundToInt(shore.x) * 73856093 ^ Mathf.RoundToInt(shore.y) * 19349663;
            Ports.Port port = Ports.At(shore);
            if (port != null)
            {
                // A harbour of the game's: its pier is the dock, the stone stands beside it.
                Ports.Stone(port, trail, out Vector3 at, out Quaternion facing);
                ZDO atPier = Landings.Spawn(stone, at, facing);
                stones.Add(atPier);
                placed.Add(atPier.m_uid);
                structures.Add(new Vector2(at.x, at.z));
                Debug.Log("[OdinsPaths] Harbour at the pier of " + port + ".");
                placed.AddRange(Buildings.ForHarbour(trail, landing, Ports.LandEndAlong(port, trail, landing), structures, seed + 1));
                return atPier;
            }
            // The dock first: the stone may stand on it.
            Docks.Harbour dock = Docks.ForHarbour(trail, landing, structures, seed);
            placed.AddRange(dock.Built.Placed);
            Vector3 position;
            Quaternion rotation;
            if (dock.Built.HasStone)
            {
                position = dock.Built.Stone;
                rotation = dock.Built.StoneRotation;
            }
            else
            {
                StoneBesideRoad(trail, landing, dock.LandEnd, structures, out position, out rotation);
            }
            ZDO spawned = Landings.Spawn(stone, position, rotation);
            stones.Add(spawned);
            placed.Add(spawned.m_uid);
            structures.Add(new Vector2(position.x, position.z));
            placed.AddRange(Buildings.ForHarbour(trail, landing, dock.LandEnd, structures, seed + 1));
            return spawned;
        }

        /// <summary>
        /// A stone without a dock to stand on (or whose dock has no "stone" spot): on the ground
        /// beside the road, a little inland of where the dock begins, and further up the road off
        /// anything built there (a player's dock). Its face to the road's far side, so it is read
        /// on the way down to the boat.
        /// </summary>
        private static void StoneBesideRoad(Trail trail, Landings.Landing landing, float landEnd, Structures structures, out Vector3 position, out Quaternion rotation)
        {
            Vector2 point = Vector2.zero;
            Vector2 seaward = Vector2.up;
            float road = 0f;
            for (int i = 0; i <= Inland; i++)
            {
                Vector2 middle = Docks.Along(trail, landing, landEnd + 2f + i * Trail.Spacing, out road, out seaward);
                Vector2 right = new Vector2(seaward.y, -seaward.x);
                point = middle - right * (trail.Kind.HalfWidthAt(middle) + StoneSide);
                if (structures.Distance(point, Taken) >= Taken)
                {
                    break;
                }
            }
            // The road's edge may be cut into the bank or filled over the beach: the lower of the
            // two, and a little into it, so the stone neither floats nor stands in a pit.
            position = new Vector3(point.x, Mathf.Min(Ground.Height(point.x, point.y), road) - 0.2f, point.y);
            Vector2 across = new Vector2(seaward.y, -seaward.x);
            rotation = Quaternion.LookRotation(new Vector3(across.x, 0f, across.y));
        }

        /// <summary>Adds the harbour at to's position to from's links, once.</summary>
        private static void Link(ZDO from, ZDO to)
        {
            if (from == to)
            {
                return;
            }
            List<Vector2> links = Links(from);
            Vector2 there = Flat(to.GetPosition());
            if (links.Exists(l => (l - there).sqrMagnitude < 1f))
            {
                return;
            }
            links.Add(there);
            ZPackage pkg = new ZPackage();
            pkg.Write(links.Count);
            foreach (Vector2 link in links)
            {
                pkg.Write(link.x);
                pkg.Write(link.y);
            }
            // A client near the stone may own it; the server takes it back to write.
            from.SetOwner(ZDOMan.GetSessionID());
            from.Set(LinksKey, pkg.GetArray());
        }

        /// <summary>The stones linked to start, directly or through others, start among them.</summary>
        internal static List<ZDO> Group(ZDO start, List<ZDO> stones)
        {
            List<ZDO> group = new List<ZDO> { start };
            for (int i = 0; i < group.Count; i++)
            {
                foreach (Vector2 link in Links(group[i]))
                {
                    ZDO across = stones.Find(s => (Flat(s.GetPosition()) - link).sqrMagnitude < 1f);
                    if (across != null && !group.Contains(across))
                    {
                        group.Add(across);
                    }
                }
            }
            return group;
        }

        /// <summary>
        /// The colour of the stone's group, as an index into <see cref="Colours"/>, written to every
        /// stone of it: the colour most of its stones have already (two groups a new road joins
        /// keep the larger one's), else the one fewest stones have. Server only.
        /// </summary>
        internal static int Colour(ZDO stone, List<ZDO> stones)
        {
            List<ZDO> group = Group(stone, stones);
            int[] votes = new int[Colours.Length + 1];
            foreach (ZDO zdo in group)
            {
                int c = zdo.GetInt(GroupKey);
                if (c > 0 && c <= Colours.Length)
                {
                    votes[c]++;
                }
            }
            int chosen = 0;
            for (int c = 1; c <= Colours.Length; c++)
            {
                if (votes[c] > votes[chosen])
                {
                    chosen = c;
                }
            }
            if (chosen == 0)
            {
                int[] used = new int[Colours.Length + 1];
                foreach (ZDO zdo in stones)
                {
                    int c = zdo.GetInt(GroupKey);
                    if (c > 0 && c <= Colours.Length)
                    {
                        used[c]++;
                    }
                }
                chosen = 1;
                for (int c = 2; c <= Colours.Length; c++)
                {
                    if (used[c] < used[chosen])
                    {
                        chosen = c;
                    }
                }
            }
            foreach (ZDO zdo in group)
            {
                if (zdo.GetInt(GroupKey) != chosen)
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    zdo.Set(GroupKey, chosen);
                }
            }
            return chosen - 1;
        }

        /// <summary>A harbour pin's name: "Harbour" in its group's colour, which the client's map also tints the icon with.</summary>
        private static string PinNameFor(int colour)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(Colours[colour]) + ">" + PinName + "</color>";
        }

        /// <summary>Whether a map pin is a harbour's, and its group colour if it has one (pins from before the groups have none).</summary>
        internal static bool IsPin(string name, out Color colour)
        {
            colour = Color.white;
            if (name == PinName)
            {
                return true;
            }
            const string head = "<color=#";
            string tail = ">" + PinName + "</color>";
            return name != null && name.Length == head.Length + 6 + tail.Length && name.StartsWith(head) && name.EndsWith(tail)
                && ColorUtility.TryParseHtmlString(name.Substring(head.Length - 1, 7), out colour);
        }

        private static List<Vector2> Links(ZDO zdo)
        {
            List<Vector2> links = new List<Vector2>();
            byte[] data = zdo.GetByteArray(LinksKey);
            if (data == null)
            {
                return links;
            }
            ZPackage pkg = new ZPackage(data);
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                links.Add(new Vector2(pkg.ReadSingle(), pkg.ReadSingle()));
            }
            return links;
        }

        /// <summary>
        /// The server's answer to a stone: a pin at the stone itself, then one at every harbour
        /// linked to the stone nearest the point asked from, whose stone still stands (a dev undo
        /// takes stones away, not the links to them). Whether the question was a harbour's at all.
        /// The stone's own pin goes without showMap, so the client adds it silently only when it
        /// has none there yet (<c>Minimap.DiscoverLocation</c>), and first, so the player ends up
        /// looking across the water, not at the stone.
        /// </summary>
        public static bool Answer(long sender, string name, Vector3 point, bool showMap)
        {
            if (name != LocationName)
            {
                return false;
            }
            List<ZDO> stones = Stones();
            ZDO asked = null;
            float best = Asked;
            foreach (ZDO zdo in stones)
            {
                float distance = Vector2.Distance(Flat(zdo.GetPosition()), Flat(point));
                if (distance < best)
                {
                    best = distance;
                    asked = zdo;
                }
            }
            if (asked == null)
            {
                Debug.LogWarning("[OdinsPaths] A harbour stone at " + Flat(point) + " asked, but none stands there.");
                return true;
            }
            string pinName = PinNameFor(Colour(asked, stones));
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "RPC_DiscoverLocationResponse", pinName, (int)PinType, asked.GetPosition(), false);
            int answered = 0;
            foreach (Vector2 link in Links(asked))
            {
                ZDO across = stones.Find(s => (Flat(s.GetPosition()) - link).sqrMagnitude < 1f);
                if (across == null)
                {
                    continue;
                }
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, "RPC_DiscoverLocationResponse", pinName, (int)PinType, across.GetPosition(), showMap);
                answered++;
            }
            if (answered == 0)
            {
                Debug.LogWarning("[OdinsPaths] The harbour stone at " + Flat(point) + " has no harbour across standing.");
            }
            return true;
        }

        /// <summary>Every harbour stone in the world. Rare - a lay's landings, a stone used -, so a walk over every ZDO.</summary>
        internal static List<ZDO> Stones()
        {
            List<ZDO> stones = new List<ZDO>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetPrefab() == Hash)
                {
                    stones.Add(zdo);
                }
            }
            return stones;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
    }

    public partial class OdinsPathsPlugin
    {
        /// <summary>The harbour stone joins the scene's prefabs, on the server and on every client with the mod.</summary>
        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        public static class RegisterHarbourStone
        {
            private static void Postfix()
            {
                Harbours.Register();
            }
        }

        /// <summary>
        /// A harbour pin in another colour at the same spot (its group joined a larger one, or it
        /// is from before the groups) gives way to the new one, instead of the map keeping both.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.DiscoverLocation))]
        public static class ReplaceHarbourPin
        {
            private static void Prefix(Minimap __instance, Vector3 pos, Minimap.PinType type, string name)
            {
                if (!Harbours.IsPin(name, out Color _))
                {
                    return;
                }
                List<Minimap.PinData> old = __instance.m_pins.FindAll(pin => pin.m_type == type && pin.m_name != name && pin.m_save
                    && Utils.DistanceXZ(pos, pin.m_pos) < 1f && Harbours.IsPin(pin.m_name, out Color _));
                foreach (Minimap.PinData pin in old)
                {
                    __instance.RemovePin(pin);
                }
            }
        }

        /// <summary>The game paints every pin white each update; a harbour's gets its group's colour, which its name carries.</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        public static class TintHarbourPins
        {
            private static readonly Dictionary<string, Color> colours = new Dictionary<string, Color>();

            private static void Postfix(Minimap __instance)
            {
                foreach (Minimap.PinData pin in __instance.m_pins)
                {
                    if (pin.m_iconElement == null || pin.m_ownerID != 0L || pin.m_name.Length == 0 || pin.m_name[0] != '<')
                    {
                        continue;
                    }
                    if (!colours.TryGetValue(pin.m_name, out Color colour))
                    {
                        colour = Harbours.IsPin(pin.m_name, out Color parsed) ? parsed : Color.clear;
                        colours[pin.m_name] = colour;
                    }
                    if (colour != Color.clear && pin.m_iconElement.color != colour)
                    {
                        pin.m_iconElement.color = colour;
                    }
                }
            }
        }
    }
}
