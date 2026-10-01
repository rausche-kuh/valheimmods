using HarmonyLib;
using BepInEx.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OdinsPaths
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships. Nothing else
    // refers to it - delete the file to drop the commands.
    public partial class OdinsPathsPlugin
    {
        private const string Usage = "paths facts | bench [cells] | where <location> | costs [<name> <value> ...] | search <target> [pass ...] | "
            + "lay <target> [pass ...] [main|spur] [solo] | spurs | undo | plan | grow [count|all] | preview [all] [full] [spacing] | show [spacing] | relink | auto [on|off] | ports [radius] | "
            + "network | forget | clearpins | reset [confirm]   (target: <x> <z> or a location name; "
            + "pass: a cell in metres, or cell:corridor - one alone is the coarse cell before the settings' fine pass, 0 = none; "
            + "main = a paved main road, the default, spur = a dirt spur; solo = from the player alone, not the network)";

        /// <summary>What the last "paths lay" replaced, zone by zone, for "paths undo".</summary>
        private static Dictionary<Vector2s, TerrainWriter.ZoneBackup> lastBackups;
        /// <summary>The harbour stones, posts and Mistlands lamps the last "paths lay" set, for "paths undo".</summary>
        private static List<ZDOID> lastLandings;
        /// <summary>The road the last "paths lay" added to the network, for "paths undo".</summary>
        private static Network.Road lastRoad;
        /// <summary>The spurs the last "paths grow" or "paths spurs" laid, for "paths undo".</summary>
        private static PathLayer.SpurOutcome lastSpurs;

        /// <summary>The unsaved map pins the last lays dropped along their trails.</summary>
        private static readonly List<Minimap.PinData> trailPins = new List<Minimap.PinData>();


        /// <summary>
        /// "paths ..." - the checks ROADMAP.md section 0 asks for (facts, bench), "where" to list a
        /// location's instances by distance, "costs" to show or set the route cost settings (sea,
        /// boarding, landing, swamp) without leaving the game, "search": a path from the player to a point or to
        /// the location of a name cheapest to reach (every instance is a goal), searched and shaped but not written, with unsaved map
        /// pins along it and the numbers of every pass, and "lay": the same written into the
        /// terrain exactly as the sleep trigger will do it. Both take the passes after the target:
        /// one number is the coarse cell before the settings' fine pass (0 = the fine pass alone),
        /// two or more are every pass in order, each "cell" or "cell:corridor" ("64 16:256 4:96"),
        /// defaulting to the settings. "undo"
        /// puts back the terrain data the last lay replaced; "clearpins" removes the pins. Server
        /// only for search, lay and undo - the host of a local game is one. Everything it prints
        /// also goes to the BepInEx log. Needs devcommands.
        /// </summary>
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        public static class Commands
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("paths", Usage, args =>
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    switch (sub)
                    {
                        case "facts": Say(args.Context, Facts()); break;
                        case "bench":
                            int cells = args.Length > 2 && int.TryParse(args[2], out int n) ? Mathf.Clamp(n, 10, 1000) : 200;
                            Say(args.Context, Bench(cells));
                            break;
                        case "where": Say(args.Context, Where(args.Length > 2 ? args[2] : null)); break;
                        case "costs": Say(args.Context, Costs(args)); break;
                        case "search": Lay(args, write: false); break;
                        case "lay": Lay(args, write: true); break;
                        case "undo": Say(args.Context, Undo()); break;
                        case "network": Say(args.Context, ShowNetwork()); break;
                        case "plan": Grow(args.Context, 1, write: false); break;
                        case "spurs": Spurs(args.Context); break;
                        case "grow":
                            if (args.Length > 2 && args[2] == "all")
                            {
                                Grow(args.Context, 200, write: true, everything: true);
                            }
                            else
                            {
                                Grow(args.Context, args.Length > 2 && int.TryParse(args[2], out int count) ? Mathf.Clamp(count, 1, 20) : 1, write: true);
                            }
                            break;
                        case "forget": Say(args.Context, Forget()); break;
                        case "reset": Say(args.Context, Reset(args.Length > 2 && args[2] == "confirm")); break;
                        case "clearpins": Say(args.Context, ClearPins()); break;
                        case "preview": Preview(args); break;
                        case "show": Say(args.Context, Show(args)); break;
                        case "relink": Say(args.Context, Relink()); break;
                        case "auto": Say(args.Context, Auto(args)); break;
                        case "ports": Ports(args); break;
                        default: Say(args.Context, Usage); break;
                    }
                }, isCheat: true);
            }
        }

        /// <summary>
        /// The zone heightmap's width and scale (the TerrainComp arrays are (width+1)^2), the
        /// terrain compiler's prefab name, the water level, and under the player: biome, generated
        /// versus actual height, the paint mask (r dirt, g cultivated - snow depth in the Deep
        /// North -, b paved, a vegetation) and the size of the zone's saved terrain data.
        /// </summary>
        private static string Facts()
        {
            if (ZoneSystem.instance == null || WorldGenerator.instance == null)
            {
                return "Not in a world.";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("server: ").Append(ZNet.instance != null && ZNet.instance.IsServer())
                .Append("  dedicated: ").Append(ZNet.instance != null && ZNet.instance.IsDedicated()).Append('\n');
            Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
            sb.Append("zone heightmap: width ").Append(prefabMap.m_width).Append(" scale ").Append(prefabMap.m_scale)
                .Append("  terrain compiler prefab: ")
                .Append(prefabMap.m_terrainCompilerPrefab != null ? prefabMap.m_terrainCompilerPrefab.name : "none").Append('\n');
            sb.Append("water level: ").Append(ZoneSystem.instance.m_waterLevel).Append('\n');

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return sb.Append("No local player - the rest needs one.").ToString();
            }
            Vector3 pos = player.transform.position;
            sb.Append("at ").Append(pos.ToString("F1")).Append("  zone ").Append(ZoneSystem.GetZone(pos))
                .Append("  biome ").Append(WorldGenerator.instance.GetBiome(pos))
                .Append("  deep north: ").Append(WorldGenerator.IsDeepnorth(pos.x, pos.z)).Append('\n');
            float generated = WorldGenerator.instance.GetHeight(pos.x, pos.z, out Color genMask);
            sb.Append("generated height ").Append(generated.ToString("F2"))
                .Append("  generated mask ").Append(genMask.ToString("F2")).Append('\n');
            Heightmap hmap = Heightmap.FindHeightmap(pos);
            if (hmap != null)
            {
                hmap.WorldToVertex(pos, out int x, out int y);
                sb.Append("heightmap vertex ").Append(x).Append(',').Append(y)
                    .Append("  height ").Append(hmap.GetHeight(x, y).ToString("F2"))
                    .Append("  paint ").Append(hmap.GetPaintMask(x, y).ToString("F2")).Append('\n');
            }
            TerrainComp comp = TerrainComp.FindTerrainCompiler(pos);
            if (comp == null)
            {
                sb.Append("no terrain compiler in this zone (never modified)");
            }
            else
            {
                byte[] data = comp.m_nview != null && comp.m_nview.IsValid()
                    ? comp.m_nview.GetZDO().GetByteArray(ZDOVars.s_TCData) : null;
                sb.Append("terrain compiler at ").Append(comp.transform.position.ToString("F1"))
                    .Append("  saved data ").Append(data != null ? data.Length + " bytes compressed" : "none")
                    .Append("  operations ").Append(comp.m_operations);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Times what a path search would do per cell: one generated height (biome lookup plus
        /// that biome's noise), on a cells x cells grid 4 m apart around the player - and, for
        /// comparison, the game's own exact heightmap build of one zone. Then the player's zone as the
        /// game built it against Ground.Height and WorldGenerator.GetHeight at every vertex: Ground
        /// should match to rounding, GetHeight is off wherever the zone's corners disagree on the biome.
        /// </summary>
        private static string Bench(int cells)
        {
            if (WorldGenerator.instance == null || Player.m_localPlayer == null)
            {
                return "Needs a world and a local player.";
            }
            Vector3 center = Player.m_localPlayer.transform.position;
            WorldGenerator gen = WorldGenerator.instance;
            float half = cells * 2f;
            float sum = 0f;
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < cells; i++)
            {
                for (int j = 0; j < cells; j++)
                {
                    sum += gen.GetHeight(center.x - half + i * 4f, center.z - half + j * 4f);
                }
            }
            watch.Stop();
            int samples = cells * cells;
            double perSample = watch.Elapsed.TotalMilliseconds * 1000.0 / samples;

            Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
            // A zone far from the player, so HeightmapBuilder has nothing cached for it.
            Vector3 far = ZoneSystem.GetZonePos(ZoneSystem.GetZone(center + new Vector3(640f, 0f, 640f)));
            Stopwatch zoneWatch = Stopwatch.StartNew();
            HeightmapBuilder.instance.RequestTerrainSync(far, prefabMap.m_width, prefabMap.m_scale, false, gen);
            zoneWatch.Stop();

            Vector3 here = ZoneSystem.GetZonePos(ZoneSystem.GetZone(center));
            HeightmapBuilder.HMBuildData built = HeightmapBuilder.instance.RequestTerrainSync(here, prefabMap.m_width, prefabMap.m_scale, false, gen);
            int pitch = prefabMap.m_width + 1;
            float origin = -prefabMap.m_width * prefabMap.m_scale * 0.5f;
            float groundError = 0f;
            float generatorError = 0f;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    float vx = here.x + origin + x * prefabMap.m_scale;
                    float vz = here.z + origin + y * prefabMap.m_scale;
                    float actual = built.m_baseHeights[y * pitch + x];
                    groundError = Mathf.Max(groundError, Mathf.Abs(Ground.Height(vx, vz) - actual));
                    generatorError = Mathf.Max(generatorError, Mathf.Abs(gen.GetHeight(vx, vz) - actual));
                }
            }

            return samples + " generated heights in " + watch.Elapsed.TotalMilliseconds.ToString("F0") + " ms = "
                + perSample.ToString("F2") + " us each (checksum " + sum.ToString("F0") + ")\n"
                + "one zone's exact heightmap build: " + zoneWatch.Elapsed.TotalMilliseconds.ToString("F1") + " ms\n"
                + "this zone as built vs Ground.Height: " + groundError.ToString("F3") + " m at worst, vs GetHeight: "
                + generatorError.ToString("F2") + " m (0 where the zone is one biome through and through)";
        }

        /// <summary>Every instance of a location, nearest first, so a far one can be searched by its coordinates.</summary>
        private static string Where(string name)
        {
            if (name == null)
            {
                return Usage;
            }
            if (ZoneSystem.instance == null || Player.m_localPlayer == null)
            {
                return "Needs a world and a local player.";
            }
            Vector3 from = Player.m_localPlayer.transform.position;
            List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
            if (!ZoneSystem.instance.FindLocations(name, ref instances))
            {
                return "No location called " + name + " - boss altars are Eikthyrnir, GDKing, Bonemass, Dragonqueen, GoblinKing, "
                    + "the traders Vendor_BlackForest, Hildir_camp, BogWitch_Camp.";
            }
            instances.Sort((a, b) => Vector3.Distance(a.m_position, from).CompareTo(Vector3.Distance(b.m_position, from)));
            StringBuilder sb = new StringBuilder();
            sb.Append(instances.Count).Append(" x ").Append(name).Append(", nearest first:");
            foreach (ZoneSystem.LocationInstance instance in instances)
            {
                sb.Append('\n').Append(instance.m_position.x.ToString("F0")).Append(' ').Append(instance.m_position.z.ToString("F0"))
                    .Append("  ").Append(Vector3.Distance(instance.m_position, from).ToString("F0")).Append(" m  ")
                    .Append(WorldGenerator.instance.GetBiome(instance.m_position))
                    .Append(instance.m_placed ? "  generated" : "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// "paths ports [radius]": the game's harbours (<see cref="OdinsPaths.Ports"/>) within the
        /// radius of the player (1500 m), their zones generated where need be (the nearest few),
        /// each pinned at its berth and its pier's land end, with the turn read from it.
        /// </summary>
        private static void Ports(Terminal.ConsoleEventArgs args)
        {
            Terminal terminal = args.Context;
            Player player = Player.m_localPlayer;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || player == null)
            {
                Say(terminal, "paths ports runs on the server with a local player - a local game, or its host.");
                return;
            }
            if (Grower.Busy)
            {
                Say(terminal, "Still busy with the last one.");
                return;
            }
            float radius = args.Length > 2 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 1500f;
            Vector2 at = new Vector2(player.transform.position.x, player.transform.position.z);
            Grower.Busy = true;
            Instance.StartCoroutine(ShowPorts(terminal, at, radius));
        }

        private static IEnumerator ShowPorts(Terminal terminal, Vector2 at, float radius)
        {
            List<OdinsPaths.Ports.Port> found = null;
            try
            {
                yield return OdinsPaths.Ports.Prepare(p => Vector2.Distance(p, at) <= radius, new List<Vector2> { at },
                    text => Say(terminal, text), ports => found = ports);
            }
            finally
            {
                Grower.Busy = false;
            }
            if (found == null || found.Count == 0)
            {
                Say(terminal, "No " + OdinsPaths.Ports.LocationName + " read within " + radius.ToString("F0") + " m ('paths where "
                    + OdinsPaths.Ports.LocationName + "' lists them; a few zones are generated per call).");
                yield break;
            }
            foreach (OdinsPaths.Ports.Port port in found)
            {
                Pin(port.Berth, Minimap.PinType.Icon4, "", HarbourColour, port + ": the berth");
                Pin(port.LandEnd, Minimap.PinType.Icon3, "", HarbourColour, port + ": the pier's land end");
                Say(terminal, port + "  " + Vector2.Distance(port.Centre, at).ToString("F0") + " m, land end at "
                    + port.LandEnd.ToString("F0") + ", berth at " + port.Berth.ToString("F0"));
            }
        }

        /// <summary>
        /// The route cost settings, set from the console by name: "paths costs sea 2 landing 300"
        /// and the next search uses them. Alone it prints them all.
        /// </summary>
        private static string Costs(Terminal.ConsoleEventArgs args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 2; i + 1 < args.Length; i += 2)
            {
                ConfigEntry<float> entry = CostSetting(args[i]);
                if (entry == null)
                {
                    sb.Append("No cost called ").Append(args[i]).Append(" - sea, boarding, landing or swamp. ");
                }
                else if (float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    entry.Value = value;
                }
                else
                {
                    sb.Append(args[i + 1]).Append(" is not a number. ");
                }
            }
            sb.Append("sea ").Append(SeaCost.Value.ToString("F1", CultureInfo.InvariantCulture)).Append(" per metre, boarding ")
                .Append(BoardingCost.Value.ToString("F0", CultureInfo.InvariantCulture)).Append(" m, landing ")
                .Append(LandingCost.Value.ToString("F0", CultureInfo.InvariantCulture)).Append(" m, swamp ")
                .Append(SwampCost.Value.ToString("F1", CultureInfo.InvariantCulture)).Append(" per metre (settings clamp to their ranges).");
            return sb.ToString();
        }

        private static ConfigEntry<float> CostSetting(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "sea": return SeaCost;
                case "boarding": return BoardingCost;
                case "landing": return LandingCost;
                case "swamp": return SwampCost;
                default: return null;
            }
        }

        /// <summary>
        /// "search" and "lay": the target is "x z" or a location name (every instance, the search takes the cheapest), then
        /// optionally the passes.
        /// </summary>
        private static void Lay(Terminal.ConsoleEventArgs args, bool write)
        {
            Terminal terminal = args.Context;
            Player player = Player.m_localPlayer;
            string verb = write ? "lay" : "search";
            if (ZNet.instance == null || !ZNet.instance.IsServer() || player == null)
            {
                Say(terminal, "paths " + verb + " runs on the server with a local player - a local game, or its host.");
                return;
            }
            if (Grower.Busy)
            {
                Say(terminal, "Still busy with the last one.");
                return;
            }
            Vector3 from = player.transform.position;
            List<Vector2> goals = new List<Vector2>();
            List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
            int next;
            if (args.Length > 3 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                goals.Add(new Vector2(x, z));
                next = 4;
            }
            else if (args.Length > 2 && ZoneSystem.instance.FindLocations(args[2], ref instances) && instances.Count > 0)
            {
                // Every instance: the first pass finds the one cheapest to walk to, which need not be the nearest.
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    goals.Add(new Vector2(instance.m_position.x, instance.m_position.z));
                }
                next = 3;
            }
            else
            {
                Say(terminal, args.Length > 2 ? "No location called " + args[2] + " - 'paths where <name>' lists a location's instances." : Usage);
                return;
            }
            PathLayer.Options options = new PathLayer.Options { Write = write };
            List<string> passArgs = new List<string>();
            bool solo = false;
            for (int a = next; a < args.Length; a++)
            {
                RoadKind kind = RoadKind.Parse(args[a]);
                if (kind != null)
                {
                    options.Kind = kind;
                }
                else if (args[a].ToLowerInvariant() == "solo")
                {
                    solo = true;
                }
                else
                {
                    passArgs.Add(args[a]);
                }
            }
            List<PathLayer.Pass> given = new List<PathLayer.Pass>();
            for (int a = 0; a < passArgs.Count; a++)
            {
                if (!ParsePass(passArgs[a], a == passArgs.Count - 1, out PathLayer.Pass pass))
                {
                    Say(terminal, "Not a pass: " + passArgs[a] + " - a cell in metres, or cell:corridor; or main or spur.");
                    return;
                }
                given.Add(pass);
            }
            if (given.Count == 1)
            {
                options.Passes = new List<PathLayer.Pass>();
                if (given[0].Cell > 0f)
                {
                    options.Passes.Add(given[0]);
                }
                options.Passes.Add(new PathLayer.Pass(PathSearch.FineCell, CorridorWidth.Value));
            }
            else if (given.Count > 1)
            {
                options.Passes = given.FindAll(pass => pass.Cell > 0f);
            }

            Grower.Busy = true;
            ZDOMan world = ZDOMan.instance;
            // The player stands where a hub would: cost 0. The network's roads are further starts.
            List<Start> starts = new List<Start> { new Start(new Vector2(from.x, from.z)) };
            Network network = Network.Current;
            if (!solo)
            {
                starts.AddRange(network.Starts(options.Kind, Directness.Value));
            }
            string target = goals.Count == 1 && next == 4 ? "point" : args[2];
            Say(terminal, (write ? "Laying" : "Searching") + " a " + options.Kind + " from " + starts[0].Position.ToString("F0")
                + (starts.Count > 1 ? " or " + (starts.Count - 1) + " points of the network" : "") + " to "
                + (goals.Count == 1
                    ? goals[0].ToString("F0") + ", " + Vector2.Distance(starts[0].Position, goals[0]).ToString("F0") + " m as the raven flies."
                    : "the cheapest of " + goals.Count + " x " + args[2] + "."));
            Progress.Begin(1);
            Progress.Job(0, target);
            Instance.StartCoroutine(PathLayer.Lay(starts, goals, target, options, text => Say(terminal, text), outcome =>
            {
                Grower.Release(world);
                if (world != ZDOMan.instance)
                {
                    return;
                }
                Say(terminal, Progress.Frames() + ".");
                Progress.End();
                if (outcome.Failure == null && write)
                {
                    lastBackups = outcome.Written.Backups;
                    lastLandings = new List<ZDOID>(outcome.Landings);
                    lastLandings.AddRange(outcome.Lamps);
                    lastRoad = outcome.Road;
                    lastSpurs = null;
                    network.Roads.Add(outcome.Road);
                    network.Save();
                }
                Report(terminal, outcome, write, goals, starts[0].Position);
            }));
        }

        /// <summary>What a search or lay found and did, with pins along it; goals and from only say whether the nearest was chosen.</summary>
        private static void Report(Terminal terminal, PathLayer.Outcome outcome, bool write, List<Vector2> goals, Vector2 from)
        {
            if (outcome.Failure != null)
            {
                PathSearch failed = outcome.Search;
                Say(terminal, "No path: " + outcome.Failure + " (" + failed.Expanded + " cells searched in "
                    + failed.Milliseconds.ToString("F0") + " ms).");
                return;
            }
            if (goals.Count > 1)
            {
                Vector2 chosen = outcome.Goal;
                Say(terminal, "Chose the one at " + chosen.ToString("F0") + ", " + Vector2.Distance(from, chosen).ToString("F0")
                    + " m as the raven flies" + (IsNearest(chosen, goals, from) ? " - the nearest." : " - not the nearest."));
            }
            for (int p = 0; p < outcome.Passes.Count - 1; p++)
            {
                DropLinePins(outcome.Passes[p].Result, p + 1);
            }
            string title = outcome.Road != null && outcome.Road.Target != null ? Progress.NameOf(outcome.Road.Target) : "the target";
            Pin(outcome.Goal, Minimap.PinType.Boss, title, TargetColour, title + ": the road leads here, " + outcome.Trail.Length.ToString("F0") + " m");
            PinTrail(outcome.Trail, DefaultDotSpacing, "Road to " + title);
            outcome.Trail.Stats(out float water, out float steepest);
            StringBuilder searched = new StringBuilder();
            for (int p = 0; p < outcome.Passes.Count; p++)
            {
                PathSearch pass = outcome.Passes[p];
                searched.Append("Pass ").Append(p + 1).Append(" at ").Append(pass.CellSize.ToString("F0")).Append(" m: ")
                    .Append(pass.Expanded).Append(" cells expanded, ").Append(pass.Sampled).Append(" sampled, ")
                    .Append(pass.Milliseconds.ToString("F0")).Append(" ms (").Append(pass.WorkMilliseconds.ToString("F0"))
                    .Append(" ms of work), cost ").Append(pass.RouteCost.ToString("F0")).Append(".\n");
            }
            searched.Append("All spread over frames, around ").Append(outcome.Structures).Append(" built pieces.");
            Start origin = outcome.Search.Origin;
            searched.Append(origin.HubCost > 0f
                ? "\nForks off the network at " + origin.Position.ToString("F0") + ", which is " + origin.HubCost.ToString("F0")
                    + " from its hub (started at " + origin.Cost.ToString("F0") + "); the road ends " + outcome.Road.Costs[outcome.Road.Costs.Count - 1].ToString("F0") + " from it."
                : "\nStarts from a hub; the road ends " + outcome.Road.Costs[outcome.Road.Costs.Count - 1].ToString("F0") + " from it.");
            if (!write)
            {
                Say(terminal, "Found " + outcome.Trail.Length.ToString("F0") + " m, " + water.ToString("F0") + " m of it over water, "
                    + "steepest grade " + (steepest * 100f).ToString("F0") + "%. Nothing written; pins every 50 m "
                    + "(fire = the first pass, house = a middle one).\n" + searched);
                return;
            }
            TerrainWriter.Result written = outcome.Written;
            Say(terminal, "Laid " + outcome.Trail.Length.ToString("F0") + " m, " + water.ToString("F0") + " m of it over water, "
                + "steepest grade " + (steepest * 100f).ToString("F0") + "%.\n" + searched + "\n"
                + "Terrain: " + written.Zones + " zones written (" + written.Created + " new compilers), "
                + written.Painted + " texels painted, " + written.Levelled + " vertices levelled, "
                + written.Skipped + " zones skipped; " + outcome.Cleared.Cleared + " trees and rocks cleared, "
                + outcome.Cleared.Kept + " kept near buildings; " + outcome.Landings.Count + " harbour stones and posts, "
                + (outcome.Lamps.Count + 1) / 2 + " Mistlands road posts; " + outcome.Trail.Bends.Count + " hairpin landings; "
                + DeepCutMetres(outcome.Trail).ToString("F0") + " m cut deeper than MaxCut on steep ground; "
                + NarrowedMetres(outcome.Trail).ToString("F0") + " m narrowed where its legs run close; "
                + outcome.WriteMilliseconds.ToString("F0") + " ms. "
                + "'paths undo' takes the terrain, the harbour stones, posts and lamps and the road in the network back, not the trees.");
        }

        private static float DeepCutMetres(Trail trail)
        {
            int count = 0;
            foreach (float cut in trail.MaxCut)
            {
                if (cut > trail.Kind.MaxCut + 0.01f)
                {
                    count++;
                }
            }
            return count * Trail.Spacing;
        }

        private static float NarrowedMetres(Trail trail)
        {
            int count = 0;
            foreach (float room in trail.LegRoom)
            {
                if (room < trail.Kind.MaxHalfWidth)
                {
                    count++;
                }
            }
            return count * Trail.Spacing;
        }

        private static bool IsNearest(Vector2 chosen, List<Vector2> goals, Vector2 from)
        {
            float distance = Vector2.Distance(chosen, from);
            return goals.TrueForAll(goal => Vector2.Distance(goal, from) >= distance);
        }

        /// <summary>"cell" or "cell:corridor"; a pass without a corridor gets the settings' (the fine one's for the last).</summary>
        private static bool ParsePass(string text, bool last, out PathLayer.Pass pass)
        {
            pass = new PathLayer.Pass(0f, last ? CorridorWidth.Value : MidCorridor.Value);
            string[] parts = text.Split(':');
            if (parts.Length > 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float cell))
            {
                return false;
            }
            pass.Cell = Mathf.Clamp(cell, 0f, 128f);
            if (parts.Length == 2)
            {
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float corridor))
                {
                    return false;
                }
                pass.Corridor = Mathf.Clamp(corridor, 16f, 2000f);
            }
            return true;
        }

        /// <summary>A grey pin every 300 m of a pass's line, to compare with the path.</summary>
        private static void DropLinePins(List<Vector2> route, int pass)
        {
            if (Minimap.instance == null)
            {
                return;
            }
            float since = DefaultDotSpacing;
            for (int i = 0; i < route.Count; i++)
            {
                if (i > 0)
                {
                    since += Vector2.Distance(route[i - 1], route[i]);
                }
                if (since >= DefaultDotSpacing)
                {
                    since = 0f;
                    Pin(route[i], Minimap.PinType.Icon3, "", PassedColour, "Pass " + pass + "'s line, before the finer pass");
                }
            }
        }

        private static string ClearPins()
        {
            if (Minimap.instance != null)
            {
                foreach (Minimap.PinData pin in trailPins)
                {
                    Minimap.instance.RemovePin(pin);
                }
            }
            int count = trailPins.Count;
            trailPins.Clear();
            pinColours.Clear();
            pinTips.Clear();
            return "Removed " + count + " pins.";
        }

        /// <summary>
        /// Writes back what each zone held before the last lay and takes its harbour stones, posts and lamps away. A zone that had no terrain data
        /// gets an empty blob rather than losing its compiler, which is what vanilla leaves
        /// behind too, and needs no ownership.
        /// </summary>
        private static string Undo()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return "paths undo runs on the server.";
            }
            if (lastBackups == null)
            {
                return "Nothing to undo.";
            }
            Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
            int restored = 0;
            foreach (KeyValuePair<Vector2s, TerrainWriter.ZoneBackup> entry in lastBackups)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(entry.Value.Compiler);
                if (zdo == null)
                {
                    continue;
                }
                byte[] data = entry.Value.Data ?? new TerrainWriter.TerrainData(prefabMap.m_width + 1)
                    .Encode(ZoneSystem.GetZonePos(entry.Key), prefabMap.m_width * prefabMap.m_scale * 0.72f);
                zdo.Set(ZDOVars.s_TCData, data);
                restored++;
            }
            lastBackups = null;
            int removed = 0;
            foreach (ZDOID id in lastLandings ?? new List<ZDOID>())
            {
                ZDO placed = ZDOMan.instance.GetZDO(id);
                if (placed != null)
                {
                    placed.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(placed);
                    removed++;
                }
            }
            lastLandings = null;
            Network network = Network.Current;
            bool forgotten = lastRoad != null && network.Remove(lastRoad);
            if (forgotten)
            {
                network.Save();
            }
            lastRoad = null;
            int spurs = 0;
            if (lastSpurs != null)
            {
                foreach (Network.Road spur in lastSpurs.Roads)
                {
                    spurs += network.Remove(spur) ? 1 : 0;
                }
                foreach (Vector2 poi in lastSpurs.Connected)
                {
                    network.Connected.Remove(poi);
                }
                forgotten |= spurs > 0;
                network.Save();
            }
            lastSpurs = null;
            return "Restored " + restored + " zones, removed " + removed + " harbour stones, posts and lamps"
                + (forgotten ? " and the road" + (spurs > 0 ? " and its " + spurs + " spurs" : "") + " from the network." : ".");
        }

        /// <summary>
        /// "plan": what the planner has due, in order, and the first of it searched and pinned but
        /// not written. "grow": the next count jobs laid one after another, each planned afresh
        /// from the network the one before left - what the triggers will do. "paths undo" takes
        /// back the last of them only. "grow all": every road of the whole network, every boss
        /// included - what "paths preview all" shows, written.
        /// </summary>
        private static void Grow(Terminal terminal, int count, bool write, bool everything = false)
        {
            Network network = Network.Current;
            if (network == null)
            {
                Say(terminal, "paths " + (write ? "grow" : "plan") + " runs on the server - a local game, or its host.");
                return;
            }
            if (Grower.Busy)
            {
                Say(terminal, "Still busy with the last one.");
                return;
            }
            List<Planner.Job> due = Planner.Due(network, everything);
            StringBuilder sb = new StringBuilder();
            sb.Append(due.Count).Append(" due").Append(due.Count > 0 ? ":" : " - the network is complete for now.");
            foreach (Planner.Job job in due)
            {
                sb.Append("\n  ").Append(job).Append(", ").Append(job.Goals.Count).Append(job.Goals.Count == 1 ? " instance" : " instances");
            }
            if (Planner.Temples().Count == 0)
            {
                sb.Append("\nNo sacrificial stones in this world - roads can only start from bases.");
            }
            Say(terminal, sb.ToString());
            if (due.Count > 0)
            {
                Grower.Busy = true;
                Instance.StartCoroutine(GrowRoutine(terminal, everything ? due.Count : count, write, everything));
            }
        }

        private static System.Collections.IEnumerator GrowRoutine(Terminal terminal, int count, bool write, bool everything)
        {
            ZDOMan world = ZDOMan.instance;
            // A plan changes nothing: it records into a copy of the network.
            Network network = write ? Network.Current : Network.Current.Copy();
            Progress.Begin(count);
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;
            // An exception in a road must not leave the bar up and every later growth refused.
            try
            {
                for (int i = 0; i < count; i++)
                {
                    List<Planner.Job> due = Planner.Due(network, everything);
                    if (due.Count == 0)
                    {
                        Say(terminal, "Nothing more is due.");
                        break;
                    }
                    Planner.Job job = due[0];
                    Say(terminal, (write ? "Laying " : "Searching ") + job + ".");
                    Progress.Jobs(i + Mathf.Min(due.Count, count - i));
                    Progress.Job(i, job.Title);
                    PathLayer.Outcome result = null;
                    System.Exception error = null;
                    yield return Grower.Guarded(Planner.Run(job, network, write, new PathLayer.Options(), text => Say(terminal, text), outcome => result = outcome),
                        e => error = e);
                    if (world != ZDOMan.instance)
                    {
                        break;
                    }
                    if (error != null)
                    {
                        Say(terminal, "Laying " + job + " failed, see the log: " + error.Message);
                        UnityEngine.Debug.LogError("[OdinsPaths] " + error);
                        break;
                    }
                    done++;
                    Say(terminal, Progress.Frames() + ".");
                    if (write && result.Failure == null)
                    {
                        lastBackups = result.Written.Backups;
                        lastLandings = new List<ZDOID>(result.Landings);
                        lastLandings.AddRange(result.Lamps);
                        lastRoad = result.Road;
                        RememberSpurs(result.Spurs);
                    }
                    Report(terminal, result, write, job.Goals, result.Failure == null ? result.Search.Origin.Position : job.Goals[0]);
                    if (result.Spurs != null)
                    {
                        ReportSpurs(terminal, result.Spurs, write);
                    }
                    if (write && result.Failure != null)
                    {
                        Say(terminal, job.Name + " is marked unreachable; 'paths forget' clears that.");
                    }
                }
                if (world == ZDOMan.instance && done > 1)
                {
                    Say(terminal, done + " roads in " + (total.ElapsedMilliseconds / 1000f).ToString("F1") + " s.");
                }
            }
            finally
            {
                if (world == ZDOMan.instance)
                {
                    Progress.End();
                }
                Grower.Release(world);
            }
        }

        /// <summary>
        /// Lays the spurs of the network's newest main road, as the planner does after each - for a
        /// road from "paths lay". "paths undo" then takes back the spurs (and the road, if the lay was the last thing).
        /// </summary>
        private static void Spurs(Terminal terminal)
        {
            Network network = Network.Current;
            Network.Road road = network != null ? network.Roads.FindLast(r => r.Kind == RoadKind.Main) : null;
            if (road == null)
            {
                Say(terminal, network == null ? "paths spurs runs on the server." : "No main road in the network yet.");
                return;
            }
            if (Grower.Busy)
            {
                Say(terminal, "Still busy with the last one.");
                return;
            }
            Grower.Busy = true;
            ZDOMan world = ZDOMan.instance;
            Say(terminal, "Spurs off the road to " + road.Target + "...");
            Progress.Begin(1);
            Progress.Job(0, "side paths off the road to " + Progress.NameOf(road.Target));
            Instance.StartCoroutine(PathLayer.LaySpurs(road, network, true, new PathLayer.Options(), text => Say(terminal, text), spurs =>
            {
                Grower.Release(world);
                if (world != ZDOMan.instance)
                {
                    return;
                }
                Say(terminal, Progress.Frames() + ".");
                Progress.End();
                network.Roads.AddRange(spurs.Roads);
                network.Connected.AddRange(spurs.Connected);
                network.Save();
                if (road != lastRoad)
                {
                    lastBackups = null;
                    lastLandings = null;
                    lastRoad = null;
                }
                RememberSpurs(spurs);
                ReportSpurs(terminal, spurs, true);
            }));
        }

        /// <summary>Adds the spurs' zone backups to the last lay's, so "paths undo" takes both back.</summary>
        private static void RememberSpurs(PathLayer.SpurOutcome spurs)
        {
            lastSpurs = spurs;
            if (spurs == null)
            {
                return;
            }
            if (lastBackups == null)
            {
                lastBackups = new Dictionary<Vector2s, TerrainWriter.ZoneBackup>();
            }
            if (lastLandings == null)
            {
                lastLandings = new List<ZDOID>();
            }
            lastLandings.AddRange(spurs.Lamps);
            lastLandings.AddRange(spurs.Landings);
            // A zone the road wrote first keeps the road's backup: that one holds what was there before either.
            foreach (KeyValuePair<Vector2s, TerrainWriter.ZoneBackup> entry in spurs.Written.Backups)
            {
                if (!lastBackups.ContainsKey(entry.Key))
                {
                    lastBackups[entry.Key] = entry.Value;
                }
            }
        }

        private static void ReportSpurs(Terminal terminal, PathLayer.SpurOutcome spurs, bool write)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Spurs: ").Append(spurs.Candidates).Append(" points of interest near the road, ")
                .Append(spurs.Roads.Count).Append(write ? " laid" : " would be laid");
            if (spurs.Search != null)
            {
                sb.Append(" (").Append(spurs.Search.Expanded).Append(" cells, ").Append(spurs.Search.Milliseconds.ToString("F0")).Append(" ms)");
            }
            sb.Append('.');
            foreach (Network.Road spur in spurs.Roads)
            {
                sb.Append("\n  ").Append(spur.Target).Append(" at ").Append(spur.Goal.ToString("F0")).Append(": ")
                    .Append(spur.Length.ToString("F0")).Append(" m");
            }
            if (write && spurs.Roads.Count > 0)
            {
                sb.Append("\nTerrain: ").Append(spurs.Written.Zones).Append(" zones, ").Append(spurs.Written.Painted)
                    .Append(" texels painted; ").Append(spurs.Cleared.Cleared).Append(" trees and rocks cleared.");
            }
            for (int t = 0; t < spurs.Trails.Count; t++)
            {
                PinTrail(spurs.Trails[t], DefaultDotSpacing, "Spur to " + spurs.Roads[t].Target);
                Pin(spurs.Roads[t].Goal, Minimap.PinType.Icon0, "", PoiColour, spurs.Roads[t].Target + ": a spur leads here");
            }
            Say(terminal, sb.ToString());
        }

        /// <summary>Every road of the network: kind, target, length, where it starts and what its end costs from the hub.</summary>
        private static string ShowNetwork()
        {
            Network network = Network.Current;
            if (network == null)
            {
                return "paths network runs on the server.";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(network.Roads.Count).Append(" roads, ").Append(network.Pinned.Count).Append(" pinned, ")
                .Append(network.Connected.Count).Append(" points of interest connected, ").Append(network.Bases.Count).Append(" bases.");
            foreach (Network.Road road in network.Roads)
            {
                sb.Append('\n').Append(road.Kind).Append(" to ").Append(road.Target).Append(" at ").Append(road.Goal.ToString("F0"))
                    .Append(": ").Append(road.Length.ToString("F0")).Append(" m from ").Append(road.Points[0].ToString("F0"))
                    .Append(", ").Append(road.Points.Count).Append(" points, cost ").Append(road.Costs[0].ToString("F0"))
                    .Append(" to ").Append(road.Costs[road.Costs.Count - 1].ToString("F0")).Append(" from the hub");
            }
            foreach (KeyValuePair<string, Vector2> pin in network.Pinned)
            {
                sb.Append("\npinned ").Append(pin.Key).Append(" at ").Append(pin.Value.ToString("F0"));
            }
            foreach (Vector2 hub in network.Bases)
            {
                sb.Append("\nbase at ").Append(hub.ToString("F0"));
            }
            foreach (string name in network.Unreachable)
            {
                sb.Append("\nunreachable: ").Append(name);
            }
            return sb.ToString();
        }

        /// <summary>Links the harbours of roads that fork off at sea to the crossing they forked off, and colours every group.</summary>
        private static string Relink()
        {
            Network network = Network.Current;
            if (network == null)
            {
                return "paths relink runs on the server - a local game, or its host.";
            }
            int linked = Harbours.Relink(network);
            return linked + " links between roads that set out at sea and the crossing they forked off; every group coloured. "
                + "A stone used again replaces its pin with the group's colour.";
        }

        /// <summary>Empties the network - the roads stay in the terrain, the next lay just no longer knows them.</summary>
        private static string Forget()
        {
            Network network = Network.Current;
            if (network == null)
            {
                return "paths forget runs on the server.";
            }
            int roads = network.Roads.Count;
            network.Clear();
            network.Save();
            lastRoad = null;
            return "Forgot " + roads + " roads; the terrain keeps them.";
        }

        /// <summary>
        /// Takes the world back to before any road, as far as that can be done: every zone's
        /// terrain data emptied - the roads' paint and levelling, and every other change to the
        /// ground too, a player's digging and paving included -, every harbour stone, post and lamp (and old barrel) the
        /// mod placed removed, and the network forgotten, so the next growth starts afresh. The
        /// trees and rocks the clearing took stay gone. Needs "confirm"; without it, says what it would do.
        /// "paths undo" only takes back the last lay.
        /// </summary>
        private static string Reset(bool confirm)
        {
            Network network = Network.Current;
            if (network == null)
            {
                return "paths reset runs on the server.";
            }
            if (Grower.Busy)
            {
                return "Roads are being laid; wait until they are done.";
            }
            int compilerHash = "_TerrainCompiler".GetStableHashCode();
            HashSet<int> placedHashes = new HashSet<int>();
            foreach (string name in Landings.Placed)
            {
                placedHashes.Add(name.GetStableHashCode());
            }
            // What was placed before the mod marked it: those prefabs without a creator, on a road -
            // and outside every location, since the Mistlands' own road posts are the same prefabs
            // and the mod never placed a post or a lamp inside one.
            Dictionary<Vector2s, List<Vector2>> roadPoints = new Dictionary<Vector2s, List<Vector2>>();
            foreach (Network.Road road in network.Roads)
            {
                foreach (Vector2 p in road.Points)
                {
                    Vector2s zone = ZoneSystem.GetZone(new Vector3(p.x, 0f, p.y));
                    if (!roadPoints.TryGetValue(zone, out List<Vector2> list))
                    {
                        roadPoints[zone] = list = new List<Vector2>();
                    }
                    list.Add(p);
                }
            }
            List<ZDO> compilers = new List<ZDO>();
            List<ZDO> placed = new List<ZDO>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                int prefab = zdo.GetPrefab();
                if (prefab == compilerHash)
                {
                    compilers.Add(zdo);
                }
                else if (zdo.GetBool(Landings.PlacedKey) || (placedHashes.Contains(prefab) && zdo.GetLong(ZDOVars.s_creator, 0L) == 0L && OnRoad(roadPoints, zdo.GetPosition()) && !InLocation(zdo.GetPosition())))
                {
                    placed.Add(zdo);
                }
            }
            if (!confirm)
            {
                return "paths reset would empty the terrain data of " + compilers.Count + " zones - every road, and every change a player made to the ground too -, "
                    + "remove " + placed.Count + " harbour stones, dock pieces, posts and lamps, and forget " + network.Roads.Count + " roads. Cleared trees and rocks do not come back. "
                    + "Back up the world first; then 'paths reset confirm'.";
            }
            Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
            int pitch = prefabMap.m_width + 1;
            float radius = prefabMap.m_width * prefabMap.m_scale * 0.72f;
            foreach (ZDO zdo in compilers)
            {
                // An empty blob rather than no compiler, as undo does: a loaded zone reloads it at once.
                Vector3 centre = ZoneSystem.GetZonePos(ZoneSystem.GetZone(zdo.GetPosition()));
                zdo.Set(ZDOVars.s_TCData, new TerrainWriter.TerrainData(pitch).Encode(centre, radius));
            }
            foreach (ZDO zdo in placed)
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
            }
            int roads = network.Roads.Count;
            network.Clear();
            network.Save();
            lastBackups = null;
            lastLandings = null;
            lastRoad = null;
            lastSpurs = null;
            return "Emptied the terrain data of " + compilers.Count + " zones, removed " + placed.Count + " harbour stones, dock pieces, posts and lamps, and forgot "
                + roads + " roads. The next growth starts afresh ('paths auto on', or 'paths grow').";
        }

        /// <summary>Whether a position is within the footprint of the location in its zone or a neighbour's.</summary>
        private static bool InLocation(Vector3 position)
        {
            Vector2 at = new Vector2(position.x, position.z);
            Vector2s zone = ZoneSystem.GetZone(position);
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (ZoneSystem.instance.m_locationInstances.TryGetValue(new Vector2s(zone.x + x, zone.y + y), out ZoneSystem.LocationInstance instance)
                        && Vector2.Distance(new Vector2(instance.m_position.x, instance.m_position.z), at) <= Mathf.Max(Footprints.Radius(instance.m_location), 8f))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Whether a position is within 12 m of a point of the network's roads (8 m apart).</summary>
        private static bool OnRoad(Dictionary<Vector2s, List<Vector2>> roadPoints, Vector3 position)
        {
            Vector2 at = new Vector2(position.x, position.z);
            Vector2s zone = ZoneSystem.GetZone(position);
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (roadPoints.TryGetValue(new Vector2s(zone.x + x, zone.y + y), out List<Vector2> points)
                        && points.Exists(p => (p - at).sqrMagnitude < 12f * 12f))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void Say(Terminal terminal, string text)
        {
            UnityEngine.Debug.Log("[OdinsPaths] " + text);
            foreach (string line in text.Split('\n'))
            {
                terminal.AddString(line);
            }
        }
    }
}
