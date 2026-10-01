using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace OdinsPaths
{
    // Dev only, like PathCommands.cs: what the network would do, and what it did, as map pins.
    public partial class OdinsPathsPlugin
    {
        /// <summary>The most jobs one preview plans: the progression, the traders, the bases.</summary>
        private const int PreviewJobs = 30;
        /// <summary>The preview's cell for the spurs' search, instead of the fine 4 m.</summary>
        private const float PreviewSpurCell = 16f;
        private const float DefaultDotSpacing = 300f;
        /// <summary>How often the search front's pin moves, in seconds.</summary>
        private const float FrontEvery = 0.25f;

        private static readonly Color TargetColour = new Color(0.95f, 0.15f, 0.1f);
        private static readonly Color PoiColour = new Color(1f, 0.55f, 0.05f);
        private static readonly Color PassedColour = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color HubColour = Color.white;
        private static readonly Color MainColour = new Color(0.85f, 0.85f, 0.78f);
        private static readonly Color SpurColour = new Color(1f, 0.55f, 0.05f);
        private static readonly Color HarbourColour = new Color(0.2f, 0.5f, 1f);
        private static readonly Color FrontColour = new Color(1f, 0.2f, 1f);
        private static readonly Color SignColour = new Color(0.55f, 0.95f, 0.35f);

        private const string Legend = "Pins: red skull = the target a road leads to (boss, trader, base), grey skull = another instance "
            + "of it, orange fire = a point of interest with a spur, grey fire = one looked at and passed, white house = a hub, pale orb = "
            + "a stone road every {0} m of land, orange orb = where a spur forks off, blue portal = a landing (a harbour stone, a boat needed; "
            + "'show' pins the stones standing instead, one colour per group of linked harbours), "
            + "magenta fire = where the running search has got to. Hover a pin on the large map for what it is. "
            + "'paths clearpins' removes them.";

        /// <summary>Pins with a colour of their own, re-tinted after the game paints every pin white.</summary>
        private static readonly Dictionary<Minimap.PinData, Color> pinColours = new Dictionary<Minimap.PinData, Color>();
        /// <summary>What each pin is, shown when the pointer is over it on the large map.</summary>
        private static readonly Dictionary<Minimap.PinData, string> pinTips = new Dictionary<Minimap.PinData, string>();

        /// <summary>The search the preview is waiting on, for the front pin.</summary>
        private static PathSearch watched;
        private static Minimap.PinData frontPin;

        /// <summary>
        /// "paths auto [on|off]": whether the automatic growth runs (at world start and after a
        /// sleep). A Debug build starts with it held.
        /// </summary>
        private static string Auto(Terminal.ConsoleEventArgs args)
        {
            if (args.Length > 2)
            {
                string value = args[2].ToLowerInvariant();
                if (value != "on" && value != "off")
                {
                    return "paths auto [on|off]";
                }
                Grower.Held = value == "off";
                if (!Grower.Held)
                {
                    Grower.Request("paths auto on");
                }
            }
            return "Automatic growth is " + (Grower.Held ? "held (Debug build default; 'paths auto on' lets it go)" : "on")
                + (GrowNetwork.Value ? "." : ", but [Network] Grow is off in the settings.");
        }

        /// <summary>
        /// "paths preview [all] [full] [spacing]": every road the growth would lay now ("all": every
        /// boss of the progression and every trader, as the network will be in the end), one after the
        /// other on a copy of the network - each forking off the ones planned before it, with its
        /// spurs - searched but nothing written, and pinned on the map as it goes: a job's instances
        /// when it starts, the road the moment it is found, the spurs when they are, and a pin that
        /// follows the running search. Coarse by default (the coarse pass alone, spurs at 16 m);
        /// "full" runs the passes a real growth runs.
        /// </summary>
        private static void Preview(Terminal.ConsoleEventArgs args)
        {
            Terminal terminal = args.Context;
            Network current = Network.Current;
            if (current == null)
            {
                Say(terminal, "paths preview runs on the server - a local game, or its host.");
                return;
            }
            if (Grower.Busy)
            {
                Say(terminal, "Still busy with the last one.");
                return;
            }
            bool full = false;
            bool all = false;
            float spacing = DefaultDotSpacing;
            for (int a = 2; a < args.Length; a++)
            {
                if (args[a].ToLowerInvariant() == "full")
                {
                    full = true;
                }
                else if (args[a].ToLowerInvariant() == "all")
                {
                    all = true;
                }
                else if (float.TryParse(args[a], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s))
                {
                    spacing = Mathf.Clamp(s, 8f, 500f);
                }
            }
            PathLayer.Options options = new PathLayer.Options { Write = false };
            if (!full)
            {
                options.FirstOnly = true;
                options.SpurCell = PreviewSpurCell;
            }
            ClearPins();
            Grower.Busy = true;
            Say(terminal, "Previewing the growth " + (full ? "at full resolution" : "coarsely (each road's first pass alone - by its distance "
                + "4 m up to 600 m, 16 m up to 1.5 km, 32 m up to 4 km, 64 m beyond -, spurs at " + PreviewSpurCell.ToString("F0") + " m)")
                + " from the network as it is ("
                + current.Roads.Count + " roads; 'paths show' pins those)"
                + (all ? ", every boss of the progression and every trader, as if all were due" : "") + ". Nothing is written.");
            Instance.StartCoroutine(PreviewRoutine(terminal, current.Copy(), options, spacing, all));
        }

        private static IEnumerator PreviewRoutine(Terminal terminal, Network network, PathLayer.Options options, float spacing, bool all)
        {
            ZDOMan world = ZDOMan.instance;
            Stopwatch clock = Stopwatch.StartNew();
            PinHubs(network);
            int roads = 0;
            int spurs = 0;
            List<Planner.Job> due = Planner.Due(network, all);
            Progress.Begin(due.Count);
            Instance.StartCoroutine(FollowFront(world));
            for (int i = 0; i < PreviewJobs && due.Count > 0; i++)
            {
                Planner.Job job = due[0];
                Progress.Jobs(i + due.Count);
                Progress.Job(i, job.Title);
                // The instances the road may lead to, at once; the chosen one turns red when found.
                // Only the candidates the search will get: a group of 240 mines is 240 pins.
                foreach (Vector2 goal in PathLayer.Candidates(Planner.Starts(job, network), job.Goals))
                {
                    Pin(goal, Minimap.PinType.Boss, "", PassedColour, job.Title + ": an instance the road could lead to");
                }
                foreach (Vector2 other in job.Others)
                {
                    Pin(other, Minimap.PinType.Boss, "", PassedColour, job.Title + (job.Choice == "remote"
                        ? ": not taken - the road goes to the one farthest from the roads ([Network] Style)"
                        : ": not taken - the road goes to the one in the middle of the others ([Network] Style)"));
                }
                Stopwatch jobClock = Stopwatch.StartNew();
                options.Searching = search => watched = search;
                options.Found = found => PinRoad(job, found, spacing);
                PathLayer.Outcome result = null;
                yield return Planner.Run(job, network, false, options, text => { }, outcome => result = outcome);
                watched = null;
                if (world != ZDOMan.instance)
                {
                    Grower.Release(world);
                    yield break;
                }
                StringBuilder sb = new StringBuilder();
                sb.Append(i + 1).Append(". ").Append(job.Title).Append(" (").Append(job.Kind == Planner.JobKind.Base ? "base" : job.Reason).Append("): ");
                if (result.Failure != null)
                {
                    sb.Append("no way - ").Append(result.Failure).Append('.');
                }
                else
                {
                    roads++;
                    result.Trail.Stats(out float water, out float steepest);
                    Start origin = result.Search.Origin;
                    sb.Append(result.Trail.Length.ToString("F0")).Append(" m, ").Append(water.ToString("F0")).Append(" m over water, ")
                        .Append(Landings.Find(result.Trail).Count).Append(" landings, ")
                        .Append(origin.HubCost > 0f ? "forks off at " + origin.Position.ToString("F0") : "from a hub at " + origin.Position.ToString("F0"))
                        .Append(", goal at ").Append(result.Goal.ToString("F0"))
                        .Append(result.Entry != null ? " entering the Mistlands at " + result.Entry.Value.ToString("F0") + (result.StoppedAtEntry ? " (the road stops there)" : "") : "")
                        .Append(result.Candidates > 1 ? ", " + result.Searched + " of " + result.Candidates + " instances searched" : "")
                        .Append(result.Estimate > 0f ? ", estimated " + result.Estimate.ToString("F0") : "").Append(". ")
                        .Append(Timings(result)).Append(", ").Append((jobClock.ElapsedMilliseconds / 1000f).ToString("F1")).Append(" s in all; ")
                        .Append(Progress.Frames()).Append('.');
                    spurs += PinSpurs(result.Spurs, spacing, sb);
                }
                Say(terminal, sb.ToString());
                due = Planner.Due(network, all);
            }
            Progress.End();
            Grower.Release(world);
            Say(terminal, "Previewed " + roads + " roads and " + spurs + " spurs in " + (clock.ElapsedMilliseconds / 1000f).ToString("F0") + " s"
                + (due.Count > 0 ? "; " + due.Count + " more due, past the preview's " + PreviewJobs + "." : ".") + "\n"
                + string.Format(Legend, spacing.ToString("F0")));
        }

        /// <summary>
        /// The survey, each pass and the spurs' search: cells expanded and sampled, wall time, and
        /// the work time where it differs (a search spread over frames, SearchThread off).
        /// </summary>
        private static string Timing(PathSearch search)
        {
            string text = search.Expanded + " cells (" + search.Sampled + " sampled) " + search.Milliseconds.ToString("F0") + " ms";
            if (search.WorkMilliseconds < search.Milliseconds * 0.9)
            {
                text += " (" + search.WorkMilliseconds.ToString("F0") + " ms of work)";
            }
            return text;
        }

        private static string Timings(PathLayer.Outcome result)
        {
            StringBuilder sb = new StringBuilder();
            if (result.Survey != null)
            {
                sb.Append("survey ").Append(Timing(result.Survey)).Append(", ");
            }
            foreach (PathSearch pass in result.Passes)
            {
                sb.Append(pass.CellSize.ToString("F0")).Append(" m pass ").Append(Timing(pass)).Append(", ");
            }
            foreach (PathSearch pass in result.Inner)
            {
                sb.Append(pass.CellSize.ToString("F0")).Append(" m from the Mistlands' edge ").Append(Timing(pass))
                    .Append(pass.Result == null ? " (no way: " + pass.Failure + ")" : "").Append(", ");
            }
            PathSearch spurs = result.Spurs != null ? result.Spurs.Search : null;
            if (spurs != null)
            {
                sb.Append("spurs ").Append(Timing(spurs));
            }
            else
            {
                sb.Append("no spur search");
            }
            return sb.ToString();
        }

        /// <summary>A pin that follows the search the preview waits on - where it has got to - until the preview ends.</summary>
        private static IEnumerator FollowFront(ZDOMan world)
        {
            while (Grower.Busy && ZDOMan.instance == world && Minimap.instance != null)
            {
                PathSearch search = watched;
                Vector2? front = search != null ? search.Frontier : null;
                if (front != null)
                {
                    // Made again if "paths clearpins" took it meanwhile.
                    if (frontPin == null || !trailPins.Contains(frontPin))
                    {
                        frontPin = Pin(front.Value, Minimap.PinType.Icon0, "", FrontColour, "");
                    }
                    frontPin.m_pos = new Vector3(front.Value.x, 0f, front.Value.y);
                    pinTips[frontPin] = "The search front: " + search.CellSize.ToString("F0") + " m cells, " + search.Expanded
                        + " expanded, " + Mathf.FloorToInt(search.Fraction * 100f) + "% of the way";
                }
                yield return new WaitForSecondsRealtime(FrontEvery);
            }
            if (frontPin != null && Minimap.instance != null)
            {
                Minimap.instance.RemovePin(frontPin);
                trailPins.Remove(frontPin);
            }
            frontPin = null;
        }

        /// <summary>
        /// "paths show [spacing]": the network as laid and saved - its roads, landings, pinned
        /// targets, connected points of interest and bases - as map pins.
        /// </summary>
        private static string Show(Terminal.ConsoleEventArgs args)
        {
            Network network = Network.Current;
            if (network == null)
            {
                return "paths show runs on the server - a local game, or its host.";
            }
            float spacing = args.Length > 2 && float.TryParse(args[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float s) ? Mathf.Clamp(s, 8f, 500f) : DefaultDotSpacing;
            ClearPins();
            foreach (Network.Road road in network.Roads)
            {
                if (road.Points.Count > 1)
                {
                    string what = (road.Kind == RoadKind.Main ? "Road to " : "Spur to ") + Progress.NameOf(road.Target);
                    PinTrail(new Trail(road.Points, road.Kind), spacing, what, landings: false);
                }
            }
            int harbours = PinHarbours();
            foreach (KeyValuePair<string, Vector2> pinned in network.Pinned)
            {
                Pin(pinned.Value, Minimap.PinType.Boss, Progress.NameOf(pinned.Key), TargetColour, Progress.NameOf(pinned.Key) + ": the road leads here");
            }
            foreach (Vector2 poi in network.Connected)
            {
                Network.Road spur = network.Roads.Find(r => r.Kind == RoadKind.Spur && (r.Goal - poi).sqrMagnitude < 150f * 150f);
                Pin(poi, Minimap.PinType.Icon0, "", PoiColour, (spur != null ? spur.Target : "A point of interest") + ": connected");
            }
            PinHubs(network);
            return network.Roads.Count + " roads, " + harbours + " harbour stones, " + network.Pinned.Count + " targets, "
                + network.Connected.Count + " points of interest, " + network.Bases.Count + " bases"
                + (network.Unreachable.Count > 0 ? "; no way to " + string.Join(", ", new List<string>(network.Unreachable).ToArray()) : "")
                + ".\n" + string.Format(Legend, spacing.ToString("F0"));
        }

        /// <summary>
        /// Every harbour stone standing, in its group's colour - the stones linked to each other,
        /// directly or through others -, its links in the tooltip. How many.
        /// </summary>
        private static int PinHarbours()
        {
            List<ZDO> stones = Harbours.Stones();
            foreach (ZDO stone in stones)
            {
                Vector3 p = stone.GetPosition();
                int colour = Harbours.Colour(stone, stones);
                int group = Harbours.Group(stone, stones).Count;
                Pin(new Vector2(p.x, p.z), Minimap.PinType.Icon4, "", Harbours.Colours[colour],
                    "A harbour stone, group colour " + (colour + 1) + " of " + Harbours.Colours.Length + ": "
                    + (group > 1 ? group + " harbours linked, directly or through others" : "linked to no harbour"));
            }
            return stones.Count;
        }

        private static void PinHubs(Network network)
        {
            foreach (Vector2 hub in Planner.Temples())
            {
                Pin(hub, Minimap.PinType.Icon1, "start", HubColour, "The sacrificial stones: a hub every road may start from");
            }
            foreach (Vector2 hub in network.Bases)
            {
                Pin(hub, Minimap.PinType.Icon1, "base", HubColour, "A base with a road: a hub");
            }
        }

        /// <summary>A road the moment it is found: its target in red, its line, water and landings.</summary>
        private static void PinRoad(Planner.Job job, PathLayer.Outcome found, float spacing)
        {
            Vector2 target = job.Kind == Planner.JobKind.Base ? job.Base : found.Goal;
            Start origin = found.Search.Origin;
            Pin(target, Minimap.PinType.Boss, job.Title, TargetColour, job.Title + " (" + job.Reason + "): the road leads here, "
                + found.Trail.Length.ToString("F0") + " m, " + (origin.HubCost > 0f ? "forking off at " + origin.Position.ToString("F0") : "from a hub"));
            PinTrail(found.Trail, spacing, "Road to " + job.Title);
        }

        /// <summary>
        /// A main road: an orb every spacing metres of land - none over water - and a portal at each
        /// landing, unless landings is off. A spur: one orb where it forks off. The landings' count.
        /// </summary>
        private static int PinTrail(Trail trail, float spacing, string what, bool landings = true)
        {
            if (trail.Points.Count == 0)
            {
                return 0;
            }
            if (trail.Kind == RoadKind.Spur)
            {
                Pin(trail.Points[0], Minimap.PinType.Icon3, "", SpurColour, what + ": forks off here, "
                    + (trail.Points.Count * Trail.Spacing).ToString("F0") + " m");
                return 0;
            }
            int every = Mathf.Max(1, Mathf.RoundToInt(spacing / Trail.Spacing));
            for (int i = every / 2; i < trail.Points.Count; i += every)
            {
                if (!trail.Water[i])
                {
                    Pin(trail.Points[i], Minimap.PinType.Icon3, "", MainColour, what + ", " + (i * Trail.Spacing).ToString("F0") + " m along");
                }
            }
            if (!landings)
            {
                return 0;
            }
            List<Landings.Landing> found = Landings.Find(trail);
            foreach (Landings.Landing landing in found)
            {
                Pin(trail.Points[landing.Shore], Minimap.PinType.Icon4, "", HarbourColour,
                    what + ": a landing - a harbour stone pinning the far shore, and a boat");
            }
            return found.Count;
        }

        /// <summary>A road's spurs and the points of interest looked at; the spurs' count. Names them into sb.</summary>
        private static int PinSpurs(PathLayer.SpurOutcome outcome, float spacing, StringBuilder sb)
        {
            if (outcome == null || outcome.Considered.Count == 0)
            {
                sb.Append(" No points of interest near.");
                return 0;
            }
            for (int t = 0; t < outcome.Trails.Count; t++)
            {
                PinTrail(outcome.Trails[t], spacing, "Spur to " + outcome.Roads[t].Target);
            }
            List<string> reached = new List<string>();
            List<string> passed = new List<string>();
            float prize = SpurPrize.Value;
            for (int c = 0; c < outcome.Considered.Count; c++)
            {
                Vector2 centre = outcome.Considered[c];
                string name = outcome.ConsideredNames[c];
                bool connected = outcome.Connected.Exists(p => (p - centre).sqrMagnitude < 1f);
                float cost = c < outcome.ConsideredCosts.Count ? outcome.ConsideredCosts[c] : -1f;
                float off = outcome.ConsideredDistances[c];
                string why = connected
                    ? "a spur, cost " + Mathf.Max(cost, 0f).ToString("F0") + " of " + prize.ToString("F0")
                    : cost >= 0f
                        ? "passed: cost " + cost.ToString("F0") + ", more than " + prize.ToString("F0")
                        : "passed: not reached within " + prize.ToString("F0") + ", " + off.ToString("F0") + " m from the road";
                Pin(centre, Minimap.PinType.Icon0, "", connected ? PoiColour : PassedColour, name + ": " + why);
                if (connected)
                {
                    reached.Add(name);
                }
                else
                {
                    passed.Add(name + (cost >= 0f ? " (cost " + cost.ToString("F0") + ")" : " (not reached, " + off.ToString("F0") + " m off)"));
                }
            }
            sb.Append(" Spurs to ").Append(reached.Count > 0 ? string.Join(", ", reached.ToArray()) : "none")
                .Append(passed.Count > 0 ? "; too far (prize " + prize.ToString("F0") + "): " + string.Join(", ", passed.ToArray()) : "").Append('.');
            return outcome.Trails.Count;
        }

        private static Minimap.PinData Pin(Vector2 at, Minimap.PinType type, string name, Color colour, string tip)
        {
            if (Minimap.instance == null)
            {
                return null;
            }
            Minimap.PinData pin = Minimap.instance.AddPin(new Vector3(at.x, 0f, at.y), type, name, save: false, isChecked: false);
            trailPins.Add(pin);
            pinColours[pin] = colour;
            if (!string.IsNullOrEmpty(tip))
            {
                pinTips[pin] = tip;
            }
            if (Instance.GetComponent<PinTips>() == null)
            {
                Instance.gameObject.AddComponent<PinTips>();
            }
            return pin;
        }

        /// <summary>The game paints every pin white (grey if shared) each update; the preview's get their colour back.</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        public static class TintPreviewPins
        {
            private static void Postfix()
            {
                foreach (KeyValuePair<Minimap.PinData, Color> entry in pinColours)
                {
                    Minimap.PinData pin = entry.Key;
                    if (pin.m_iconElement != null && pin.m_iconElement.color != entry.Value)
                    {
                        pin.m_iconElement.color = entry.Value;
                    }
                }
            }
        }

        /// <summary>
        /// The tooltip of the preview's pins: on the large map, what every pin under the pointer is,
        /// in a box beside it. Plain IMGUI - it is a dev tool.
        /// </summary>
        private sealed class PinTips : MonoBehaviour
        {
            private GUIStyle style;
            private readonly List<string> under = new List<string>();

            private void OnGUI()
            {
                Minimap map = Minimap.instance;
                if (map == null || map.m_mode != Minimap.MapMode.Large || pinTips.Count == 0)
                {
                    return;
                }
                Vector2 pointer = ZInput.pointerPosition;
                under.Clear();
                foreach (KeyValuePair<Minimap.PinData, string> entry in pinTips)
                {
                    RectTransform marker = entry.Key.m_uiElement;
                    if (marker != null && marker.gameObject.activeInHierarchy
                        && RectTransformUtility.RectangleContainsScreenPoint(marker, pointer) && under.Count < 6)
                    {
                        under.Add(entry.Value);
                    }
                }
                if (under.Count == 0)
                {
                    return;
                }
                if (style == null)
                {
                    style = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.UpperLeft, wordWrap = true };
                    style.normal.textColor = new Color(1f, 0.93f, 0.8f);
                }
                GUIContent content = new GUIContent(string.Join("\n", under.ToArray()));
                const float width = 420f;
                float height = style.CalcHeight(content, width);
                float x = Mathf.Min(pointer.x + 20f, Screen.width - width - 4f);
                float y = Mathf.Min(Screen.height - pointer.y + 20f, Screen.height - height - 4f);
                GUI.Box(new Rect(x, y, width, height), content, style);
            }
        }
    }
}
