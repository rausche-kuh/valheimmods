using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OdinsPaths
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships.

    /// <summary>
    /// The harbour blueprints and PlanBuild: "docks export" puts every .blueprint into PlanBuild's
    /// save folder, where its rune places them; "docks import" reads a PlanBuild capture of one
    /// placed and reworked back in. PlanBuild captures in the world's axes, from its centre marker
    /// or the lowest corner, and writes the hammer's tab where the role was, so the import finds
    /// the turn and shift that lay the old blueprint over the most captured pieces, reads it back
    /// into the old one's frame and gives each piece still where it was its old role.
    /// </summary>
    internal static class PlanBuildFiles
    {
        private const string Guid = "marcopogo.PlanBuild";
        private const string DefaultFolder = "BepInEx/config/PlanBuild/blueprints";

        /// <summary>A captured piece this close to where an old one lands, and turned the same, is that piece.</summary>
        private const float Same = 0.2f;
        private const float SameAngle = 2f;

        /// <summary>
        /// PlanBuild's save folder, when PlanBuild is loaded: its setting, a relative path taken
        /// from the game's working folder as PlanBuild takes it. Null without PlanBuild.
        /// </summary>
        public static string Folder()
        {
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out PluginInfo info) || info.Instance == null)
            {
                return null;
            }
            ConfigDefinition key = new ConfigDefinition("Directories", "Save directory");
            string folder = info.Instance.Config.ContainsKey(key) ? info.Instance.Config[key].BoxedValue as string : null;
            return Path.GetFullPath(string.IsNullOrEmpty(folder) ? DefaultFolder : folder);
        }

        /// <summary>A .blueprint copied into PlanBuild's save folder; its path there, or null without PlanBuild.</summary>
        public static string Offer(string file)
        {
            string folder = Folder();
            if (folder == null)
            {
                return null;
            }
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, Path.GetFileName(file));
            File.Copy(file, copy, true);
            return copy;
        }

        /// <summary>
        /// "docks export [name]": every blueprint, or the one named, into PlanBuild's folder; one
        /// still in the old JSON format first rewritten as .blueprint and .json in the player's folder.
        /// </summary>
        public static string Export(Terminal.ConsoleEventArgs args)
        {
            if (ZNetScene.instance == null)
            {
                return "docks export needs the game's prefabs: run it in a world.";
            }
            string name = args.Length > 2 ? args[2] : null;
            List<Blueprint> blueprints = name == null ? new List<Blueprint>(Blueprints.All) : new List<Blueprint> { Blueprints.Named(name) };
            if (blueprints[0] == null)
            {
                return "No blueprint " + name + ". docks list shows them.";
            }
            int converted = 0;
            int offered = 0;
            List<string> failed = new List<string>();
            foreach (Blueprint blueprint in blueprints)
            {
                string plan = Path.ChangeExtension(blueprint.File, ".blueprint");
                if (blueprint.Legacy)
                {
                    try
                    {
                        plan = Blueprints.Save(blueprint);
                        converted++;
                    }
                    catch (InvalidOperationException e)
                    {
                        failed.Add(blueprint.name + " (" + e.Message + ")");
                        continue;
                    }
                }
                if (Offer(plan) != null)
                {
                    offered++;
                }
            }
            Blueprints.Load();
            Relics.Register();
            string folder = Folder();
            return converted + " converted from the old JSON format into " + Blueprints.UserFolder + " (copy each .blueprint with its .json "
                + "into assets/harbours/ to ship it), "
                + (folder != null ? offered + " copied to PlanBuild's " + folder + " (bp.local lists them)" : "PlanBuild is not loaded, nothing copied")
                + (failed.Count > 0 ? ". Not converted: " + string.Join(", ", failed.ToArray()) : "") + ".";
        }

        /// <summary>
        /// "docks import &lt;file&gt; [as &lt;name&gt;] [dock|building]": a PlanBuild capture as
        /// the blueprint of its name. The file is a path, or a name in PlanBuild's folders -
        /// its own, or with PlanBuild's player name prefix; the newest wins. The blueprint of the
        /// target name gives the frame, the roles and the settings; without one the capture's
        /// frame is taken as it is (PlanBuild's centre marker the origin, z north), and a kind is needed.
        /// </summary>
        public static string Import(Terminal.ConsoleEventArgs args)
        {
            if (ZNetScene.instance == null)
            {
                return "docks import needs the game's prefabs: run it in a world.";
            }
            if (args.Length < 3)
            {
                return "docks import <PlanBuild file> [as <name>] [dock|building]";
            }
            string wanted = args[2];
            string name = Path.GetFileNameWithoutExtension(wanted);
            string kind = null;
            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "as" && i + 1 < args.Length)
                {
                    name = args[++i];
                }
                else if (args[i] == "dock" || args[i] == "building")
                {
                    kind = args[i];
                }
            }
            string file = Locate(wanted);
            if (file == null)
            {
                return "No PlanBuild file " + wanted + " in " + string.Join(", ", Folders().ToArray()) + ".";
            }
            List<Blueprints.PlanPiece> captured;
            try
            {
                captured = Blueprints.ReadPlan(file, out string _);
            }
            catch (Exception e)
            {
                return file + " could not be read: " + e.Message;
            }

            Blueprint old = Blueprints.Named(name);
            if (old == null && kind == null)
            {
                return "No blueprint " + name + " to fit it to: say dock or building to import it as a new one, in the capture's own frame.";
            }
            Blueprint blueprint = new Blueprint
            {
                name = name,
                kind = kind ?? (old.IsDock ? "dock" : "building"),
                biomes = old?.biomes ?? new[] { WorldGenerator.instance.GetBiome(Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero).ToString() },
                weight = old?.weight ?? 1f,
                preSnow = old?.preSnow ?? false,
                roofReach = old?.roofReach ?? 0f,
                deco = old?.deco,
                clutter = old?.clutter,
                clutterChance = old?.clutterChance ?? 0f,
            };
            blueprint.IsDock = blueprint.kind == "dock";

            string fit = "in the capture's own frame";
            int kept = 0;
            if (old != null)
            {
                List<Blueprints.PlanPiece> before;
                try
                {
                    before = Blueprints.ToPlan(old);
                }
                catch (InvalidOperationException e)
                {
                    return name + " could not be laid out to fit to: " + e.Message;
                }
                int matched = Fit(before, captured, out float yaw, out Vector3 shift);
                int needed = Mathf.Max(3, Mathf.CeilToInt(before.Count * 0.3f));
                if (matched < needed)
                {
                    return "Only " + matched + " of the " + before.Count + " pieces of " + name + " are in " + file
                        + " - is it a capture of it? 'as <new name> dock|building' imports it as a new blueprint.";
                }
                Quaternion back = Quaternion.Euler(0f, -yaw, 0f);
                for (int i = 0; i < captured.Count; i++)
                {
                    Blueprints.PlanPiece piece = captured[i];
                    piece.Position = back * (piece.Position - shift);
                    piece.Rotation = back * piece.Rotation;
                    captured[i] = piece;
                }
                kept = Roles(before, captured);
                fit = "fitted to " + name + " (" + matched + " of its " + before.Count + " pieces found, turned " + Blueprints.F(yaw)
                    + "°, shifted " + Blueprints.F(shift.x) + ", " + Blueprints.F(shift.y) + ", " + Blueprints.F(shift.z) + ")";
            }
            Blueprints.FromPlan(captured, blueprint);
            if (blueprint.pieces.Count == 0)
            {
                return file + " holds no pieces.";
            }
            string path = Blueprints.Save(blueprint);
            string copy = Offer(path);
            Blueprints.Load();
            Relics.Register();
            return blueprint.pieces.Count + " pieces and " + blueprint.spots.Count + " spots from " + file + ", " + fit + "; "
                + kept + " kept their roles, " + (blueprint.pieces.Count - kept) + " guessed - check them. Written to " + path
                + (copy != null ? " and " + copy : "") + "; it is in use now.";
        }

        private static List<string> Folders()
        {
            List<string> folders = new List<string>();
            foreach (string folder in new[] { Folder(), Path.Combine(Path.Combine(Paths.ConfigPath, "PlanBuild"), "blueprints"), Path.GetFullPath(DefaultFolder) })
            {
                if (folder != null && !folders.Exists(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    folders.Add(folder);
                }
            }
            return folders;
        }

        /// <summary>The file: a path, else the newest .blueprint in PlanBuild's folders named so, or so after a player's name and "_".</summary>
        private static string Locate(string wanted)
        {
            if (File.Exists(wanted))
            {
                return wanted;
            }
            string stem = Path.GetFileNameWithoutExtension(wanted);
            string found = null;
            DateTime newest = DateTime.MinValue;
            foreach (string folder in Folders())
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }
                foreach (string file in Directory.GetFiles(folder, "*.blueprint", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    bool named = string.Equals(name, stem, StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("_" + stem, StringComparison.OrdinalIgnoreCase);
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    if (named && written > newest)
                    {
                        found = file;
                        newest = written;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// The turn about the vertical and the shift that lay the old pieces over the most captured
        /// ones: each pair of the same prefab proposes a turn (the one between their rotations,
        /// if it is about the vertical alone) and, under it, a shift; the shift most pairs agree
        /// on wins, averaged over the pieces it matches. How many old pieces it matches.
        /// </summary>
        private static int Fit(List<Blueprints.PlanPiece> old, List<Blueprints.PlanPiece> captured, out float bestYaw, out Vector3 bestShift)
        {
            bestYaw = 0f;
            bestShift = Vector3.zero;
            Dictionary<string, List<int>> byPrefab = new Dictionary<string, List<int>>();
            for (int i = 0; i < old.Count; i++)
            {
                if (!byPrefab.TryGetValue(old[i].Prefab, out List<int> list))
                {
                    byPrefab[old[i].Prefab] = list = new List<int>();
                }
                list.Add(i);
            }

            Dictionary<int, int> yawVotes = new Dictionary<int, int>();
            foreach (Blueprints.PlanPiece c in captured)
            {
                if (!byPrefab.TryGetValue(c.Prefab, out List<int> same))
                {
                    continue;
                }
                foreach (int i in same)
                {
                    Vector3 forward = c.Rotation * Quaternion.Inverse(old[i].Rotation) * Vector3.forward;
                    if (Mathf.Abs(forward.y) > 0.05f)
                    {
                        continue;
                    }
                    int yaw = ((int)Mathf.Round(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg) + 360) % 360;
                    yawVotes.TryGetValue(yaw, out int votes);
                    yawVotes[yaw] = votes + 1;
                }
            }
            List<KeyValuePair<int, int>> yaws = new List<KeyValuePair<int, int>>(yawVotes);
            yaws.Sort((a, b) => b.Value.CompareTo(a.Value));

            int best = 0;
            for (int y = 0; y < yaws.Count && y < 8; y++)
            {
                float yaw = yaws[y].Key;
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                Dictionary<long, int> shiftVotes = new Dictionary<long, int>();
                Dictionary<long, Vector3> shifts = new Dictionary<long, Vector3>();
                foreach (Blueprints.PlanPiece c in captured)
                {
                    if (!byPrefab.TryGetValue(c.Prefab, out List<int> same))
                    {
                        continue;
                    }
                    foreach (int i in same)
                    {
                        if (Quaternion.Angle(c.Rotation, turn * old[i].Rotation) > SameAngle)
                        {
                            continue;
                        }
                        Vector3 shift = c.Position - turn * old[i].Position;
                        long key = ((long)Mathf.Round(shift.x * 10f) & 0x1FFFFF) << 42 | ((long)Mathf.Round(shift.y * 10f) & 0x1FFFFF) << 21
                            | ((long)Mathf.Round(shift.z * 10f) & 0x1FFFFF);
                        shiftVotes.TryGetValue(key, out int votes);
                        shiftVotes[key] = votes + 1;
                        shifts[key] = shift;
                    }
                }
                long top = 0;
                int topVotes = 0;
                foreach (KeyValuePair<long, int> vote in shiftVotes)
                {
                    if (vote.Value > topVotes)
                    {
                        top = vote.Key;
                        topVotes = vote.Value;
                    }
                }
                if (topVotes <= best)
                {
                    continue;
                }
                // Averaged over the pieces it matches, to undo the vote's 0.1 m steps.
                Vector3 guess = shifts[top];
                Vector3 sum = Vector3.zero;
                int matched = 0;
                bool[] taken = new bool[captured.Count];
                foreach (Blueprints.PlanPiece o in old)
                {
                    int at = Nearest(captured, taken, o.Prefab, turn * o.Position + guess, turn * o.Rotation);
                    if (at >= 0)
                    {
                        taken[at] = true;
                        sum += captured[at].Position - turn * o.Position;
                        matched++;
                    }
                }
                if (matched > best)
                {
                    best = matched;
                    bestYaw = yaw;
                    bestShift = sum / matched;
                }
            }
            return best;
        }

        /// <summary>Each captured piece, now in the old frame, where an old one of its prefab was gets its role; how many did.</summary>
        private static int Roles(List<Blueprints.PlanPiece> old, List<Blueprints.PlanPiece> captured)
        {
            bool[] taken = new bool[captured.Count];
            int kept = 0;
            foreach (Blueprints.PlanPiece o in old)
            {
                if (o.Category == "spot")
                {
                    continue;
                }
                int at = Nearest(captured, taken, o.Prefab, o.Position, o.Rotation);
                if (at >= 0)
                {
                    taken[at] = true;
                    Blueprints.PlanPiece piece = captured[at];
                    piece.Category = o.Category;
                    captured[at] = piece;
                    kept++;
                }
            }
            // What PlanBuild wrote there is a hammer tab: the rest is guessed.
            for (int i = 0; i < captured.Count; i++)
            {
                if (!taken[i])
                {
                    Blueprints.PlanPiece piece = captured[i];
                    piece.Category = "";
                    captured[i] = piece;
                }
            }
            return kept;
        }

        private static int Nearest(List<Blueprints.PlanPiece> pieces, bool[] taken, string prefab, Vector3 at, Quaternion rotation)
        {
            int found = -1;
            float nearest = Same;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (taken[i] || pieces[i].Prefab != prefab || Quaternion.Angle(pieces[i].Rotation, rotation) > SameAngle)
                {
                    continue;
                }
                float d = Vector3.Distance(pieces[i].Position, at);
                if (d < nearest)
                {
                    nearest = d;
                    found = i;
                }
            }
            return found;
        }
    }
}
