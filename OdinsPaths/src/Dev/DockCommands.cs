using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OdinsPaths
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships.
    public partial class OdinsPathsPlugin
    {
        private const string DockUsage = "docks list | reload | build [blueprint] [condition 0-1] [chest] [enemies] [edit] | "
            + "house [blueprint] [condition 0-1] [chest] [enemies] [edit] | undo | capture <name> [dock|building] [radius] | "
            + "export [name] | import <PlanBuild file> [as <name>] [dock|building]   "
            + "(build: a dock from where you stand out along where you look, you standing where the road ends; house: a building "
            + "with its lowest door where you stand, looking in; edit: as new, of pieces the hammer takes, signs at the spots; "
            + "capture: what was built around you into BepInEx/config/OdinsPaths/harbours/<name>.blueprint and .json; "
            + "export: every blueprint, or one, rewritten there and copied to PlanBuild's folder; "
            + "import: a PlanBuild capture of a placed blueprint, fitted back into its frame)";

        /// <summary>The objects of what "docks build" and "docks house" made, newest last, for "docks undo".</summary>
        private static readonly List<List<ZDOID>> builtDocks = new List<List<ZDOID>>();

        /// <summary>The frame of the last edit build, which a capture near it reads back in, so pieces keep their places exactly.</summary>
        private static Builder.Frame? editFrame;
        private static string editName;
        private const float EditReach = 40f;

        /// <summary>
        /// "docks ..." - to make and look at harbour blueprints without laying roads to a shore.
        /// "build" raises a dock where the player stands, out the way the player looks (a blueprint
        /// by name, else one of the biome's that fits; a condition, else a random one; "chest" and
        /// "enemies" make both certain, else none), "house" a building with its front at the
        /// player's feet; "edit" raises either as new, of pieces the player may take down and add
        /// to with the hammer, with a sign for each spot. "capture" writes the pieces built around
        /// the player (in the frame of the last edit build nearby, else of the player's feet and
        /// view) as a blueprint the harbours use at once; "reload" reads the files again. Server
        /// only for build, house and undo. Needs devcommands.
        /// </summary>
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        public static class DockCommands
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("docks", DockUsage, args =>
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    switch (sub)
                    {
                        case "list": Say(args.Context, Docks.Describe()); break;
                        case "reload":
                            Blueprints.Load();
                            Relics.Register();
                            Say(args.Context, Docks.Describe());
                            break;
                        case "build": Say(args.Context, BuildDock(args, house: false)); break;
                        case "house": Say(args.Context, BuildDock(args, house: true)); break;
                        case "undo": Say(args.Context, UndoDock()); break;
                        case "capture": Say(args.Context, Capture(args)); break;
                        case "export": Say(args.Context, PlanBuildFiles.Export(args)); break;
                        case "import": Say(args.Context, PlanBuildFiles.Import(args)); break;
                        default: Say(args.Context, DockUsage); break;
                    }
                }, isCheat: true);
            }
        }

        private static string BuildDock(Terminal.ConsoleEventArgs args, bool house)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return "docks build and house run on the server, with a player.";
            }
            string name = null;
            float condition = float.NaN;
            bool chest = false;
            bool enemies = false;
            bool edit = false;
            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "chest")
                {
                    chest = true;
                }
                else if (arg == "enemies")
                {
                    enemies = true;
                }
                else if (arg == "edit")
                {
                    edit = true;
                }
                else if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float c))
                {
                    condition = c;
                }
                else
                {
                    name = arg;
                }
            }
            Vector3 feet = player.transform.position;
            Vector3 look = player.transform.forward;
            Vector2 origin = new Vector2(feet.x, feet.z);
            Vector2 forward = new Vector2(look.x, look.z);
            long creator = edit ? player.GetPlayerID() : 0L;
            Builder.Result result;
            Builder.Frame frame = Builder.Frame.Make(origin, forward, feet.y);
            if (house)
            {
                Blueprint blueprint = name != null ? Blueprints.Named(name)
                    : Blueprints.Shuffled(dock: false, Builder.LandBiome(origin, -forward), new System.Random()).Find(b => true);
                if (blueprint == null || blueprint.IsDock)
                {
                    return "No building blueprint " + (name ?? "for this biome") + ". docks list shows them.";
                }
                result = Buildings.Here(blueprint, origin, forward, feet.y, new Builder.Options
                {
                    Condition = condition,
                    ChestChance = chest ? 1f : 0f,
                    EnemyChance = enemies ? 1f : 0f,
                    Edit = edit,
                    Creator = creator,
                }, Random.Range(int.MinValue, int.MaxValue), out frame);
            }
            else
            {
                result = Docks.Build(new Docks.Request
                {
                    LandEnd = origin,
                    Forward = forward,
                    Deck = edit ? feet.y : Docks.DeckHeight(feet.y),
                    Seed = Random.Range(int.MinValue, int.MaxValue),
                    Blueprint = name,
                    Condition = condition,
                    ChestChance = chest ? 1f : 0f,
                    EnemyChance = enemies ? 1f : 0f,
                    Force = true,
                    Edit = edit,
                    Creator = creator,
                });
            }
            if (result.Placed.Count > 0)
            {
                builtDocks.Add(result.Placed);
            }
            if (edit && result.Reason == null)
            {
                editFrame = frame;
                editName = result.Name;
                return (house ? "House: " : "Dock: ") + result + ". Rework it with the hammer, then 'docks capture " + result.Name
                    + "' within " + EditReach + " m of here.";
            }
            return (house ? "House: " : "Dock: ") + result;
        }

        private static string UndoDock()
        {
            if (builtDocks.Count == 0)
            {
                return "Nothing built with 'docks build' or 'docks house' to take away.";
            }
            List<ZDOID> last = builtDocks[builtDocks.Count - 1];
            builtDocks.RemoveAt(builtDocks.Count - 1);
            int removed = 0;
            foreach (ZDOID id in last)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo != null)
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(zdo);
                    removed++;
                }
            }
            return "Took away " + removed + " of its " + last.Count + " objects (a creature already risen stays; pieces added with the hammer stay).";
        }

        /// <summary>
        /// The pieces around the player - built by a player, or by an edit build and still
        /// standing - as a blueprint: in the last edit build's frame when the player is near it,
        /// else from the player's feet (on the deck at the land end, or on the floor at the
        /// front) the way the player looks. A sign reading a spot's kind (Spots) is that spot.
        /// A blueprint of the same name keeps its settings (biomes, deco list, clutter...).
        /// </summary>
        private static string Capture(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                return "docks capture needs a player.";
            }
            if (args.Length < 3)
            {
                return "docks capture <name> [dock|building] [radius]";
            }
            string name = args[2];
            string kind = null;
            float radius = 16f;
            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "dock" || args[i] == "building")
                {
                    kind = args[i];
                }
                else if (float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                {
                    radius = Mathf.Clamp(r, 2f, 60f);
                }
            }
            Vector3 feet = player.transform.position;
            Builder.Frame frame;
            string from;
            if (editFrame.HasValue && Vector2.Distance(editFrame.Value.Origin, new Vector2(feet.x, feet.z)) < EditReach)
            {
                frame = editFrame.Value;
                from = "the frame of the edit build of " + editName;
            }
            else
            {
                Vector3 look = player.transform.forward;
                frame = Builder.Frame.Make(new Vector2(feet.x, feet.z), new Vector2(look.x, look.z), feet.y);
                from = "your feet and view";
            }
            Quaternion inverse = Quaternion.Inverse(frame.Rotation);

            Blueprint old = Blueprints.Named(name);
            Blueprint blueprint = new Blueprint
            {
                name = name,
                kind = kind ?? (old != null ? (old.IsDock ? "dock" : "building") : "dock"),
                biomes = old?.biomes ?? new[] { WorldGenerator.instance.GetBiome(feet).ToString() },
                weight = old?.weight ?? 1f,
                preSnow = old?.preSnow ?? false,
                roofReach = old?.roofReach ?? 0f,
                deco = old?.deco,
                clutter = old?.clutter,
                clutterChance = old?.clutterChance ?? 0f,
            };
            blueprint.IsDock = blueprint.kind == "dock";
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                Vector3 at = zdo.GetPosition();
                if (new Vector2(at.x - frame.Origin.x, at.z - frame.Origin.y).sqrMagnitude > radius * radius || zdo.GetBool(Builder.ExtensionKey))
                {
                    continue;
                }
                string role = zdo.GetString(Builder.RoleKey, "");
                if (zdo.GetLong(ZDOVars.s_creator, 0L) == 0L && role == "")
                {
                    continue;
                }
                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab == null || (prefab.GetComponent<Piece>() == null && prefab.GetComponent<WearNTear>() == null))
                {
                    continue;
                }
                Vector3 local = frame.Local(at);
                Quaternion rotation = inverse * zdo.GetRotation();
                if (prefab.GetComponent<Sign>() != null)
                {
                    string text = Spots.Normal(zdo.GetString(ZDOVars.s_text, ""));
                    if (Spots.Is(text))
                    {
                        // A spot stands on the deck: the sign's foot.
                        Bounds shape = Builder.Shape(prefab);
                        Vector3 foot = local + rotation * new Vector3(shape.center.x, shape.min.y, shape.center.z);
                        blueprint.spots.Add(new BlueprintSpot { kind = text, pos = Round(foot), yaw = Mathf.Round(rotation.eulerAngles.y * 10f) / 10f });
                        continue;
                    }
                }
                string prefabName = prefab.name.StartsWith(Relics.Prefix) ? prefab.name.Substring(Relics.Prefix.Length) : prefab.name;
                blueprint.pieces.Add(new BlueprintPiece
                {
                    prefab = prefabName,
                    pos = Round(local),
                    rot = Angles(rotation),
                    role = role != "" ? role : Blueprints.Guess(prefabName, local.y, blueprint.IsDock),
                });
            }
            if (blueprint.pieces.Count == 0)
            {
                return "No pieces built within " + radius + " m.";
            }
            string path = Blueprints.Save(blueprint);
            string copy = PlanBuildFiles.Offer(path);
            Blueprints.Load();
            Relics.Register();
            return blueprint.pieces.Count + " pieces and " + blueprint.spots.Count + " spots, in " + from + ", written to " + path
                + (copy != null ? " and " + copy : "")
                + ". Check the guessed roles (pile below the deck, deck, floor, wall, door, roof, post, lamp, deco, clutter, keep); "
                + "it is in use now.";
        }

        private static float[] Round(Vector3 v)
        {
            return new[] { Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f };
        }

        /// <summary>Euler angles to 0.01 degrees, quarter turns exact; the yaw alone when that is all there is.</summary>
        private static float[] Angles(Quaternion rotation)
        {
            Vector3 e = rotation.eulerAngles;
            float[] a = new float[3];
            for (int i = 0; i < 3; i++)
            {
                float v = Mathf.Round(e[i] * 100f) / 100f;
                float quarter = Mathf.Round(v / 90f) * 90f;
                v = Mathf.Abs(v - quarter) < 0.2f ? quarter : v;
                a[i] = v >= 360f ? v - 360f : v;
            }
            if (a[0] == 0f && a[2] == 0f)
            {
                return a[1] == 0f ? null : new[] { a[1] };
            }
            return a;
        }
    }
}
