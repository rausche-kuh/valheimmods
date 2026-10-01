using BepInEx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>What a piece of a blueprint is, for how likely the weather takes it (<see cref="Builder"/>).</summary>
    internal enum Role
    {
        /// <summary>A plank or floor to walk on; goes at times, never near the land end or under something that stands on it.</summary>
        Deck,
        /// <summary>A floor that stays: a hut's, what the walls and furniture stand on.</summary>
        Floor,
        /// <summary>Below the deck or floor: always stays, and the lowest of each column is carried on down to the ground.</summary>
        Pile,
        Wall,
        /// <summary>
        /// A building's way in, weathered like a wall: the lowest one is where the building meets
        /// the ground, the point it is placed by (<see cref="Builder.Entry"/>).
        /// </summary>
        Door,
        /// <summary>Goes at times, and always when no wall is left standing within the blueprint's roofReach.</summary>
        Roof,
        /// <summary>A mooring post, a railing post: goes at times, with the lamp on it.</summary>
        Post,
        /// <summary>A lamp on a post: goes with its post.</summary>
        Lamp,
        /// <summary>Furniture, built as placed: a relic copy that drops nothing (<see cref="Relics"/>).</summary>
        Deco,
        /// <summary>Loose things lying about: never weathered away, they are the weathering.</summary>
        Clutter,
        /// <summary>Anything else: stays as it is.</summary>
        Keep,
    }

    /// <summary>Which point of a piece's measured box goes to its <c>pos</c>.</summary>
    internal enum Anchor
    {
        /// <summary>The prefab's own pivot, as the game placed it: what <c>docks capture</c> writes.</summary>
        Pivot,
        /// <summary>The top face's centre: decks, whose top is the walking surface.</summary>
        Top,
        /// <summary>The bottom face's centre: walls, furniture, standing on the deck.</summary>
        Bottom,
    }

    /// <summary>
    /// A dock or a building, as two files in <c>harbours/</c> beside the DLL (shipped from the
    /// mod's <c>assets/harbours/</c>) or in <c>BepInEx/config/OdinsPaths/harbours/</c> (the
    /// player's own, and what <c>docks capture</c> writes; one of the same name replaces the
    /// shipped one): <c>&lt;name&gt;.json</c>, the settings, plain JSON read by <see cref="Json"/>
    /// (Unity's <c>JsonUtility</c> left lists empty in game), a field left out 0, false or empty;
    /// and <c>&lt;name&gt;.blueprint</c>, the pieces in PlanBuild's format, so PlanBuild can place
    /// and capture them (<see cref="Blueprints.ReadPlan"/>). A JSON file without a .blueprint
    /// beside it holds its pieces itself, the old format, until <c>docks export</c> converts it.
    /// The format is in <c>docs/docks.md</c>.
    /// </summary>
    [Serializable]
    public sealed class Blueprint
    {
        public string name;
        /// <summary>"dock" or "building".</summary>
        public string kind;
        /// <summary><c>Heightmap.Biome</c> names; none: any biome.</summary>
        public string[] biomes;
        /// <summary>How often it is picked among those that fit, against the others' (0 counts as 1).</summary>
        public float weight;
        /// <summary>Starts snowed over (the game's <c>preSnow</c>), as the Deep North's own buildings do.</summary>
        public bool preSnow;
        /// <summary>How far, in metres, a roof piece may be from a standing wall and still hold (0: 3 m).</summary>
        public float roofReach;
        /// <summary>The furniture the deco spots pick from, each spot what fits it (<see cref="Builder.FitsSpot"/>).</summary>
        public string[] deco;
        /// <summary>Loose pieces some deck pieces get, clutterChance each.</summary>
        public string[] clutter;
        public float clutterChance;
        /// <summary>A building that fits nowhere at a harbour gives way to the building of this name; none: to nothing.</summary>
        public string fallback;
        public List<BlueprintPiece> pieces = new List<BlueprintPiece>();
        public List<BlueprintSpot> spots = new List<BlueprintSpot>();

        [NonSerialized] internal string File;
        /// <summary>Its pieces are in the JSON file itself, the format before .blueprint files.</summary>
        [NonSerialized] internal bool Legacy;
        [NonSerialized] internal bool IsDock;
        [NonSerialized] internal Heightmap.Biome Biomes;
        /// <summary>A building's "dock" spot: where it joins a dock's deck; null for one that stands on its own.</summary>
        [NonSerialized] internal BlueprintSpot Joint;

        internal float RoofReach => roofReach > 0f ? roofReach : 3f;
        internal float Weight => weight > 0f ? weight : 1f;
        internal bool Fits(Heightmap.Biome biome) => Biomes == Heightmap.Biome.None || (Biomes & biome) != 0;
    }

    /// <summary>
    /// One piece, in the blueprint's frame: a dock's origin is the middle of its land end, where
    /// the road runs onto it, z out to sea, x to the right looking out, y up from the deck's top
    /// there (the road's height); a building's is the middle of its front, the side facing the
    /// road, z into the building, x to the right looking in, y up from its floor's top.
    /// </summary>
    [Serializable]
    public sealed class BlueprintPiece
    {
        public string prefab;
        /// <summary>[x, y, z] in metres.</summary>
        public float[] pos;
        /// <summary>[yaw], or [x, y, z] Euler angles in degrees, or [x, y, z, w] a quaternion; none: unturned.</summary>
        public float[] rot;
        /// <summary>A <see cref="OdinsPaths.Role"/> by name; none: keep.</summary>
        public string role;
        /// <summary>"pivot" (none), "top" or "bottom": which point of its measured box goes to pos.</summary>
        public string anchor;

        [NonSerialized] internal Vector3 Position;
        [NonSerialized] internal Quaternion Rotation;
        [NonSerialized] internal Role Role;
        [NonSerialized] internal Anchor Anchor;
    }

    /// <summary>
    /// A place something may go (<see cref="Spots"/>): "chest", "enemy", furniture from the deco
    /// list - "deco_wall" on a wall, "deco_hanging" from a ceiling, "deco_h1" standing no higher
    /// than a wall, "deco_h2" than two -, "stone" (a dock's harbour stone), or "dock" (where a
    /// building joins a dock). In a .blueprint a sign reading its kind, pos its foot, yaw its turn.
    /// </summary>
    [Serializable]
    public sealed class BlueprintSpot
    {
        public string kind;
        public float[] pos;
        public float yaw;

        [NonSerialized] internal Vector3 Position;
    }

    /// <summary>The kinds of spot, as the signs read them. Only a spot is ever replaced; every piece is built as placed.</summary>
    internal static class Spots
    {
        public const string Chest = "chest";
        public const string Enemy = "enemy";
        public const string Stone = "stone";
        public const string Dock = "dock";
        public const string Wall = "deco_wall";
        public const string Hanging = "deco_hanging";
        /// <summary>Standing furniture no higher than a wall.</summary>
        public const string Low = "deco_h1";
        /// <summary>Standing furniture no higher than two walls.</summary>
        public const string High = "deco_h2";
        /// <summary>What the furniture spots were before they had kinds: standing, a wall high.</summary>
        private const string Old = "deco";

        public const string Names = "chest, enemy, stone, dock, deco_wall, deco_hanging, deco_h1 or deco_h2";

        /// <summary>A sign's text as a spot's kind: trimmed, lower case, the old "deco" as deco_h1.</summary>
        public static string Normal(string text)
        {
            text = (text ?? "").Trim().ToLowerInvariant();
            return text == Old ? Low : text;
        }

        public static bool Is(string text)
        {
            text = Normal(text);
            return text == Chest || text == Enemy || text == Stone || text == Dock || IsDeco(text);
        }

        public static bool IsDeco(string kind) => kind == Wall || kind == Hanging || IsStanding(kind);

        /// <summary>Furniture standing on the floor, where a chest may go too.</summary>
        public static bool IsStanding(string kind) => kind == Low || kind == High;
    }

    internal static class Blueprints
    {
        public const string Folder = "harbours";

        private static List<Blueprint> all;

        /// <summary>Every blueprint, read the first time it is asked for.</summary>
        public static List<Blueprint> All
        {
            get
            {
                if (all == null)
                {
                    Load();
                }
                return all;
            }
        }

        /// <summary>The player's own folder, where captures go: <c>BepInEx/config/OdinsPaths/harbours</c>.</summary>
        public static string UserFolder => Path.Combine(Path.Combine(Paths.ConfigPath, "OdinsPaths"), Folder);

        private static string ShippedFolder => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", Folder);

        /// <summary>Reads both folders again; a file in the player's folder replaces a shipped one of the same name.</summary>
        public static void Load()
        {
            Dictionary<string, Blueprint> byName = new Dictionary<string, Blueprint>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in new[] { ShippedFolder, UserFolder })
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }
                // The settings name a blueprint; a .blueprint without them is PlanBuild's alone.
                string[] files = Directory.GetFiles(folder, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    Blueprint blueprint = Read(file);
                    if (blueprint != null)
                    {
                        byName[blueprint.name] = blueprint;
                    }
                }
            }
            all = new List<Blueprint>(byName.Values);
            all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Debug.Log("[OdinsPaths] " + all.Count + " harbour blueprints (" + ShippedFolder + ", " + UserFolder + ").");
        }

        /// <summary>A blueprint from its file, checked and made ready; null (with a warning) if it cannot be used.</summary>
        public static Blueprint Read(string file)
        {
            Blueprint blueprint;
            try
            {
                blueprint = FromJson(Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OdinsPaths] Harbour blueprint " + file + " is no valid JSON - left out: " + e.Message);
                return null;
            }
            if (blueprint == null)
            {
                return null;
            }
            string plan = Path.ChangeExtension(file, ".blueprint");
            blueprint.Legacy = !File.Exists(plan);
            if (!blueprint.Legacy)
            {
                try
                {
                    FromPlan(ReadPlan(plan, out string _), blueprint);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[OdinsPaths] Harbour blueprint " + plan + " could not be read - left out: " + e.Message);
                    return null;
                }
            }
            blueprint.File = file;
            if (string.IsNullOrEmpty(blueprint.name))
            {
                blueprint.name = Path.GetFileNameWithoutExtension(file);
            }
            string kind = (blueprint.kind ?? "").Trim().ToLowerInvariant();
            if (kind != "dock" && kind != "building")
            {
                Debug.LogWarning("[OdinsPaths] Harbour blueprint " + file + ": kind must be \"dock\" or \"building\" - left out.");
                return null;
            }
            blueprint.IsDock = kind == "dock";
            blueprint.Biomes = Heightmap.Biome.None;
            foreach (string name in blueprint.biomes ?? new string[0])
            {
                try
                {
                    blueprint.Biomes |= (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), name.Trim(), true);
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning("[OdinsPaths] Harbour blueprint " + blueprint.name + ": no biome " + name + ".");
                }
            }
            List<BlueprintPiece> pieces = new List<BlueprintPiece>();
            foreach (BlueprintPiece piece in blueprint.pieces ?? new List<BlueprintPiece>())
            {
                if (piece == null || string.IsNullOrEmpty(piece.prefab) || piece.pos == null || piece.pos.Length != 3)
                {
                    Debug.LogWarning("[OdinsPaths] Harbour blueprint " + blueprint.name + ": a piece without prefab or [x, y, z] pos - left out.");
                    continue;
                }
                piece.Position = new Vector3(piece.pos[0], piece.pos[1], piece.pos[2]);
                piece.Rotation = Rotation(piece.rot);
                piece.Role = Parse(piece.role, Role.Keep, blueprint.name);
                piece.Anchor = Parse(piece.anchor, Anchor.Pivot, blueprint.name);
                pieces.Add(piece);
            }
            blueprint.pieces = pieces;
            List<BlueprintSpot> spots = new List<BlueprintSpot>();
            foreach (BlueprintSpot spot in blueprint.spots ?? new List<BlueprintSpot>())
            {
                if (spot == null || spot.pos == null || spot.pos.Length != 3)
                {
                    continue;
                }
                spot.kind = Spots.Normal(spot.kind);
                spot.Position = new Vector3(spot.pos[0], spot.pos[1], spot.pos[2]);
                spots.Add(spot);
            }
            blueprint.spots = spots;
            blueprint.Joint = blueprint.IsDock ? null : spots.Find(s => s.kind == Spots.Dock);
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[OdinsPaths] Harbour blueprint " + blueprint.name + " has no pieces - left out.");
                return null;
            }
            return blueprint;
        }

        /// <summary>A blueprint from a parsed JSON object; null if the text was no object.</summary>
        private static Blueprint FromJson(Dictionary<string, object> o)
        {
            if (o == null)
            {
                return null;
            }
            Blueprint blueprint = new Blueprint
            {
                name = Json.String(o, "name"),
                kind = Json.String(o, "kind"),
                biomes = Json.Strings(o, "biomes"),
                weight = Json.Float(o, "weight"),
                preSnow = Json.Bool(o, "preSnow"),
                roofReach = Json.Float(o, "roofReach"),
                deco = Json.Strings(o, "deco"),
                clutter = Json.Strings(o, "clutter"),
                clutterChance = Json.Float(o, "clutterChance"),
                fallback = Json.String(o, "fallback"),
            };
            foreach (Dictionary<string, object> p in Json.Objects(o, "pieces"))
            {
                blueprint.pieces.Add(new BlueprintPiece
                {
                    prefab = Json.String(p, "prefab"),
                    pos = Json.Floats(p, "pos"),
                    rot = Json.Floats(p, "rot"),
                    role = Json.String(p, "role"),
                    anchor = Json.String(p, "anchor"),
                });
            }
            foreach (Dictionary<string, object> p in Json.Objects(o, "spots"))
            {
                blueprint.spots.Add(new BlueprintSpot
                {
                    kind = Json.String(p, "kind"),
                    pos = Json.Floats(p, "pos"),
                    yaw = Json.Float(p, "yaw"),
                });
            }
            return blueprint;
        }

        private static T Parse<T>(string text, T otherwise, string blueprint) where T : struct
        {
            if (string.IsNullOrEmpty(text))
            {
                return otherwise;
            }
            try
            {
                return (T)Enum.Parse(typeof(T), text.Trim(), true);
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[OdinsPaths] Harbour blueprint " + blueprint + ": no " + typeof(T).Name.ToLowerInvariant() + " " + text + ".");
                return otherwise;
            }
        }

        private static Quaternion Rotation(float[] rot)
        {
            if (rot == null || rot.Length == 0)
            {
                return Quaternion.identity;
            }
            if (rot.Length == 1)
            {
                return Quaternion.Euler(0f, rot[0], 0f);
            }
            if (rot.Length == 4)
            {
                return new Quaternion(rot[0], rot[1], rot[2], rot[3]).normalized;
            }
            return Quaternion.Euler(rot[0], rot[1], rot.Length > 2 ? rot[2] : 0f);
        }

        public static Blueprint Named(string name)
        {
            return All.Find(b => string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The blueprints of a kind for a biome, in a random order weighted by each one's weight.</summary>
        public static List<Blueprint> Shuffled(bool dock, Heightmap.Biome biome, System.Random rng)
        {
            List<Blueprint> pool = All.FindAll(b => b.IsDock == dock && b.Fits(biome));
            if (pool.Count == 0)
            {
                // A biome nothing is drawn for yet builds as the Meadows do.
                pool = All.FindAll(b => b.IsDock == dock && b.Fits(Heightmap.Biome.Meadows));
            }
            List<Blueprint> order = new List<Blueprint>();
            while (pool.Count > 0)
            {
                float total = 0f;
                foreach (Blueprint b in pool)
                {
                    total += b.Weight;
                }
                double pick = rng.NextDouble() * total;
                int at = 0;
                for (; at < pool.Count - 1; at++)
                {
                    pick -= pool[at].Weight;
                    if (pick < 0)
                    {
                        break;
                    }
                }
                order.Add(pool[at]);
                pool.RemoveAt(at);
            }
            return order;
        }

        /// <summary>
        /// Writes a blueprint as its two files in the player's folder - the settings as JSON, the
        /// pieces as a PlanBuild .blueprint - and returns the .blueprint's path. Pieces of the old
        /// format anchored at their top or bottom are written by their pivots, so it needs the game's prefabs.
        /// </summary>
        public static string Save(Blueprint blueprint)
        {
            Directory.CreateDirectory(UserFolder);
            string path = Path.Combine(UserFolder, blueprint.name + ".json");
            StringBuilder s = new StringBuilder();
            s.Append("{\n");
            s.Append("  \"name\": ").Append(Quote(blueprint.name)).Append(",\n");
            s.Append("  \"kind\": ").Append(Quote(blueprint.IsDock ? "dock" : "building")).Append(",\n");
            s.Append("  \"biomes\": ").Append(Strings(blueprint.biomes)).Append(",\n");
            s.Append("  \"weight\": ").Append(F(blueprint.Weight)).Append(",\n");
            s.Append("  \"preSnow\": ").Append(blueprint.preSnow ? "true" : "false").Append(",\n");
            s.Append("  \"roofReach\": ").Append(F(blueprint.RoofReach)).Append(",\n");
            s.Append("  \"deco\": ").Append(Strings(blueprint.deco)).Append(",\n");
            s.Append("  \"clutter\": ").Append(Strings(blueprint.clutter)).Append(",\n");
            s.Append("  \"clutterChance\": ").Append(F(blueprint.clutterChance)).Append(string.IsNullOrEmpty(blueprint.fallback) ? "\n" : ",\n");
            if (!string.IsNullOrEmpty(blueprint.fallback))
            {
                s.Append("  \"fallback\": ").Append(Quote(blueprint.fallback)).Append("\n");
            }
            s.Append("}\n");
            string plan = Path.ChangeExtension(path, ".blueprint");
            File.WriteAllLines(plan, WritePlan(blueprint).ToArray());
            File.WriteAllText(path, s.ToString());
            return plan;
        }

        // ---- PlanBuild's .blueprint format ----
        //
        // Text, a header line each (#Name:, #Creator:, #Description: a JSON string, #Category:),
        // then #Pieces and a line per piece: prefab;category;x;y;z;qx;qy;qz;qw;info;sx;sy;sz, the
        // info a JSON string (a sign's text). PlanBuild never reads the category back, so it
        // carries the role; a PlanBuild capture writes the hammer's tab there instead, and the
        // role is guessed. #SnapPoints, #Terrain and anything else it knows or not are skipped.

        /// <summary>One line of a .blueprint's #Pieces.</summary>
        internal struct PlanPiece
        {
            public string Prefab;
            public string Category;
            public Vector3 Position;
            public Quaternion Rotation;
            /// <summary>The piece's extra data: a sign's text, a door's state...; "" for none.</summary>
            public string Info;
        }

        /// <summary>The pieces of a .blueprint file and its #Name (else the file's). Throws FormatException on a bad piece line.</summary>
        public static List<PlanPiece> ReadPlan(string path, out string name)
        {
            name = Path.GetFileNameWithoutExtension(path);
            List<PlanPiece> pieces = new List<PlanPiece>();
            // As PlanBuild reads it: lines before any section are pieces; the four headers keep the section.
            bool inPieces = true;
            string[] lines = File.ReadAllLines(path);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.StartsWith("#"))
                {
                    if (line.StartsWith("#Name:"))
                    {
                        string named = line.Substring("#Name:".Length).Trim();
                        name = named.Length > 0 ? named : name;
                    }
                    else if (!line.StartsWith("#Creator:") && !line.StartsWith("#Description:") && !line.StartsWith("#Category:"))
                    {
                        inPieces = line == "#Pieces";
                    }
                    continue;
                }
                if (!inPieces)
                {
                    continue;
                }
                string[] parts = line.Split(';');
                if (parts.Length < 9)
                {
                    throw new FormatException("line " + (n + 1) + " is no piece: " + line);
                }
                string info = parts.Length > 9 ? parts[9].Trim() : "";
                if (info.StartsWith("\""))
                {
                    info = Json.Parse(info) as string ?? "";
                }
                pieces.Add(new PlanPiece
                {
                    Prefab = parts[0].Trim(),
                    Category = parts[1].Trim(),
                    Position = new Vector3(Number(parts[2]), Number(parts[3]), Number(parts[4])),
                    Rotation = new Quaternion(Number(parts[5]), Number(parts[6]), Number(parts[7]), Number(parts[8])).normalized,
                    Info = info,
                });
            }
            return pieces;
        }

        /// <summary>Old PlanBuild files wrote decimal commas.</summary>
        private static float Number(string text) => string.IsNullOrEmpty(text) ? 0f
            : float.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>A blueprint's pieces and spots from PlanBuild lines in its frame: a sign reading a spot's kind is that spot.</summary>
        internal static void FromPlan(List<PlanPiece> plan, Blueprint blueprint)
        {
            bool dock = string.Equals((blueprint.kind ?? "").Trim(), "dock", StringComparison.OrdinalIgnoreCase);
            blueprint.pieces = new List<BlueprintPiece>();
            blueprint.spots = new List<BlueprintSpot>();
            foreach (PlanPiece p in plan)
            {
                string text = p.Info.Trim().ToLowerInvariant();
                if (p.Prefab == Builder.SignPrefab && Spots.Is(text))
                {
                    text = Spots.Normal(text);
                    float yaw = Mathf.Round(p.Rotation.eulerAngles.y * 10f) / 10f % 360f;
                    Vector3 foot = p.Position + Quaternion.Euler(0f, yaw, 0f) * SignBase();
                    blueprint.spots.Add(new BlueprintSpot { kind = text, pos = Rounded(foot), yaw = yaw });
                    continue;
                }
                string prefab = p.Prefab.StartsWith(Relics.Prefix) ? p.Prefab.Substring(Relics.Prefix.Length) : p.Prefab;
                string category = p.Category.ToLowerInvariant();
                blueprint.pieces.Add(new BlueprintPiece
                {
                    prefab = prefab,
                    pos = Rounded(p.Position),
                    rot = new[] { p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w },
                    role = IsRole(category) ? category : Guess(prefab, p.Position.y, dock),
                });
            }
        }

        /// <summary>A blueprint as .blueprint lines: every piece by its pivot, its role as its category, a sign for each spot.</summary>
        public static List<string> WritePlan(Blueprint blueprint)
        {
            List<string> lines = new List<string>
            {
                "#Name:" + blueprint.name,
                "#Creator:OdinsPaths",
                "#Description:" + Quote("OdinsPaths harbour " + (blueprint.IsDock ? "dock" : "building") + ": each piece's category is its role, "
                    + "a sign reading " + Spots.Names + " is a spot. The settings are in " + blueprint.name + ".json."),
                "#Category:OdinsPaths",
                "#Pieces",
            };
            foreach (PlanPiece p in ToPlan(blueprint))
            {
                lines.Add(PlanLine(p.Prefab, p.Category, p.Position, p.Rotation, p.Info));
            }
            return lines;
        }

        /// <summary>
        /// A blueprint's pieces as PlanBuild has them: by their pivots (the old format's top and
        /// bottom anchors resolved, which needs the game's prefabs), the role as the category, and
        /// a sign for each spot.
        /// </summary>
        internal static List<PlanPiece> ToPlan(Blueprint blueprint)
        {
            List<PlanPiece> plan = new List<PlanPiece>();
            foreach (BlueprintPiece piece in blueprint.pieces)
            {
                Vector3 position = Vector(piece.pos);
                Quaternion rotation = Rotation(piece.rot);
                Anchor anchor = Parse(piece.anchor, Anchor.Pivot, blueprint.name);
                if (anchor != Anchor.Pivot)
                {
                    GameObject prefab = ZNetScene.instance != null ? Builder.Prefab(piece.prefab) : null;
                    if (prefab == null)
                    {
                        throw new InvalidOperationException("the pivot of " + piece.prefab + " needs its prefab in the game");
                    }
                    Bounds shape = Builder.Shape(prefab);
                    position -= rotation * new Vector3(shape.center.x, anchor == Anchor.Top ? shape.max.y : shape.min.y, shape.center.z);
                }
                string role = Parse(piece.role, Role.Keep, blueprint.name).ToString().ToLowerInvariant();
                plan.Add(new PlanPiece { Prefab = piece.prefab, Category = role, Position = position, Rotation = rotation, Info = "" });
            }
            foreach (BlueprintSpot spot in blueprint.spots)
            {
                Quaternion turn = Quaternion.Euler(0f, spot.yaw, 0f);
                plan.Add(new PlanPiece { Prefab = Builder.SignPrefab, Category = "spot", Position = Vector(spot.pos) - turn * SignBase(), Rotation = turn, Info = spot.kind });
            }
            return plan;
        }

        internal static string PlanLine(string prefab, string category, Vector3 position, Quaternion rotation, string info)
        {
            return string.Join(";", new[]
            {
                prefab, category,
                P(position.x), P(position.y), P(position.z),
                P(rotation.x), P(rotation.y), P(rotation.z), P(rotation.w),
                Quote((info ?? "").Replace(";", "")), "1", "1", "1",
            });
        }

        private static string P(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static bool IsRole(string text) => text.Length > 0 && !char.IsDigit(text[0]) && Enum.TryParse(text, true, out Role _);

        private static bool signWarned;

        /// <summary>From a sign's pivot to its foot, which is the spot: the bottom face's centre of its measured box.</summary>
        internal static Vector3 SignBase()
        {
            GameObject sign = ZNetScene.instance != null ? Builder.Prefab(Builder.SignPrefab) : null;
            if (sign == null)
            {
                if (!signWarned)
                {
                    signWarned = true;
                    Debug.LogWarning("[OdinsPaths] Harbour blueprints read before the game's prefabs: spots are at their signs' pivots.");
                }
                return Vector3.zero;
            }
            Bounds shape = Builder.Shape(sign);
            return new Vector3(shape.center.x, shape.min.y, shape.center.z);
        }

        private static Vector3 Vector(float[] v) => v == null || v.Length < 3 ? Vector3.zero : new Vector3(v[0], v[1], v[2]);

        private static float[] Rounded(Vector3 v)
        {
            return new[] { Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f };
        }

        /// <summary>
        /// A role for a piece nobody gave one, from what the game's prefab is when it is loaded
        /// (a door, furniture) and else from its name and height: what stands below the deck or
        /// floor is a pile, anything that is no building part furniture.
        /// </summary>
        internal static string Guess(string name, float y, bool dock)
        {
            string lower = name.ToLowerInvariant();
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if ((prefab != null && prefab.GetComponent<Door>() != null) || lower.Contains("door") || lower.Contains("gate"))
            {
                return "door";
            }
            bool upright = lower.Contains("pole") || lower.Contains("pillar") || lower.Contains("log") || lower.Contains("post")
                || lower.Contains("beam") || lower.Contains("block");
            if (upright && y < -0.2f)
            {
                return "pile";
            }
            if (lower.Contains("lantern") || lower.Contains("demister") || lower.Contains("torch") || lower.Contains("lamp"))
            {
                return "lamp";
            }
            // A log bench is furniture, not a post.
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece != null && piece.m_category == Piece.PieceCategory.Furniture)
            {
                return "deco";
            }
            if (lower.Contains("roof"))
            {
                return "roof";
            }
            if (lower.Contains("wall") || lower.Contains("window"))
            {
                return "wall";
            }
            if (upright && !lower.Contains("beam"))
            {
                return "post";
            }
            if (lower.Contains("floor") || lower.Contains("beam") || lower.Contains("stair"))
            {
                return dock ? "deck" : "floor";
            }
            return "deco";
        }

        private static string Quote(string text) => "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string Strings(string[] items)
        {
            if (items == null || items.Length == 0)
            {
                return "[]";
            }
            StringBuilder s = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                s.Append(i > 0 ? ", " : "").Append(Quote(items[i]));
            }
            return s.Append(']').ToString();
        }

        internal static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
