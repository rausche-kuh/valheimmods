using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// What the network lays next, and laying it (docs/network.md). The main locations come in
    /// order: the boss altars of the progression list (all of them, or with <c>Reach</c> Progression
    /// up to the second one not yet defeated), the
    /// traders (their camps generated or not), the custom main locations, then every base without a road.
    /// Each is one <see cref="Job"/>: a search from the hubs and the network to every instance of the
    /// group, the cheapest taken and pinned - or, for a group in <c>[Network] Central</c>, to the
    /// instance in the middle of the others alone -; a base's is a search from the network to the
    /// base. A progression entry can join groups and ask for several of them (the infected mines
    /// before the Queen: three roads, each to another mine), make its roads side roads - dirt,
    /// branching off the network wherever it is nearest, but due like any other (the Deep North's
    /// villages, tunnels and Mörkhalla) -, and prefer the instances near another location (the
    /// village beside the winding tunnels).
    /// A group no road can reach is remembered as unreachable and skipped. Every main road is
    /// followed by its spurs, before the next one is planned. Server only.
    /// </summary>
    internal static class Planner
    {
        internal enum JobKind
        {
            /// <summary>A road from the network to the cheapest instance of a main location group.</summary>
            Location,
            /// <summary>A road from the nearest point of the network to a base, which becomes a hub.</summary>
            Base,
        }

        internal sealed class Job
        {
            public JobKind Kind;
            /// <summary>
            /// The location group's prefab name - several joined by '|', and " #2" on the second road
            /// of an entry that asks for more than one -, or the base's name. The network pins by it.
            /// </summary>
            public string Name;
            /// <summary>The group's name without the number: every road of the entry shares it.</summary>
            public string Group;
            /// <summary>What players are told the road leads to; the game's name where known.</summary>
            public string Label;
            /// <summary>Why it is due, for the log: "boss 2 of 5", "trader", "custom", "base".</summary>
            public string Reason;
            public List<Vector2> Goals = new List<Vector2>();
            /// <summary>The instances not searched for, since the group takes its central or remote one - for the preview.</summary>
            public List<Vector2> Others = new List<Vector2>();
            /// <summary>"central" or "remote" when the instance was chosen, not searched for; null otherwise.</summary>
            public string Choice;
            /// <summary>A base's position, for the network's list once its road is laid.</summary>
            public Vector2 Base;
            /// <summary>A side road: dirt, like a spur, and setting out from wherever the network is nearest.</summary>
            public bool Side;

            /// <summary>What the road leads to, for players: the game's name for it where known.</summary>
            public string Title => Kind == JobKind.Base
                ? "the base at " + Mathf.RoundToInt(Base.x) + ", " + Mathf.RoundToInt(Base.y)
                : Label ?? Progress.NameOf(Name);

            public override string ToString() => Kind == JobKind.Base ? "a road to the base at " + Base.ToString("F0") : "a road to " + Name + " (" + Reason + ")";
        }

        /// <summary>
        /// A progression entry: its location groups (usually one altar), how many roads it asks for,
        /// and the global key a defeat sets - the entry is done with once that key is set, so the
        /// mines carry the Queen's.
        /// </summary>
        private struct Boss
        {
            public string[] Locations;
            public int Count;
            public string Key;
            /// <summary>'~': its roads are side roads (<see cref="Job.Side"/>).</summary>
            public bool Side;
            /// <summary>'@': only the instances with one of these within <see cref="NearBy"/>, where there are any.</summary>
            public string[] Near;

            public string Group => string.Join("|", Locations);
        }

        /// <summary>Pieces this close to a ward count toward its base.</summary>
        private const float BaseCore = 30f;
        /// <summary>How close an instance's '@' location has to be for it to count as near.</summary>
        private const float NearBy = 400f;

        /// <summary>
        /// Every job due now, in order. Groups already pinned, unreachable, or missing from the
        /// world are left out; with <c>Reach</c> Progression, so is every boss after the second
        /// undefeated one. With everything set (a dev preview of the whole network), every boss of
        /// the list is due whatever <c>Reach</c> says. A
        /// trader is due from the start, its camps generated or not, unless <c>RevealTraders</c> is
        /// off: the road leads to the camp it chose, where the trader is then settled (<see cref="Traders"/>).
        /// </summary>
        public static List<Job> Due(Network network, bool everything = false)
        {
            List<Job> jobs = new List<Job>();
            List<Boss> bosses = Progression();
            HashSet<string> central = Chosen(OdinsPathsPlugin.Central.Value, "FaderLocation, DN_Bossroom");
            HashSet<string> remote = Chosen(OdinsPathsPlugin.Remote.Value, "Hildir_camp, BogWitch_Camp");
            List<string> traders = OdinsPathsPlugin.RevealTraders.Value ? Names(OdinsPathsPlugin.Traders.Value) : new List<string>();
            // Entries sharing a key (the mines and the Queen) are one step of the progression.
            HashSet<string> undefeated = new HashSet<string>();
            for (int b = 0; b < bosses.Count; b++)
            {
                Boss boss = bosses[b];
                if (!ZoneSystem.instance.GetGlobalKey(boss.Key) && !undefeated.Contains(boss.Key))
                {
                    if (!everything && OdinsPathsPlugin.Reach.Value == OdinsPathsPlugin.ProgressionReach.Progression && undefeated.Count >= 2)
                    {
                        break;
                    }
                    undefeated.Add(boss.Key);
                }
                for (int k = 0; k < boss.Count; k++)
                {
                    string reason = boss.Count > 1
                        ? "road " + (k + 1) + " of " + boss.Count + " before boss " + BossNumber(bosses, b) + " of " + BossCount(bosses)
                        : "boss " + BossNumber(bosses, b) + " of " + BossCount(bosses);
                    AddLocation(jobs, network, boss.Locations, boss.Count > 1 ? k + 1 : 0, boss.Count, reason, central, remote, boss.Side, boss.Near);
                }
            }
            foreach (string trader in traders)
            {
                if (!remote.Contains(trader))
                {
                    AddLocation(jobs, network, new[] { trader }, 0, 1, "trader", central, remote);
                }
            }
            foreach (string custom in Names(OdinsPathsPlugin.CustomLocations.Value))
            {
                AddLocation(jobs, network, new[] { custom }, 0, 1, "custom", central, remote);
            }
            foreach (Vector2 ward in Bases())
            {
                if (!network.HasBase(ward, OdinsPathsPlugin.BaseRadius.Value) && !network.Unreachable.Contains(BaseName(ward)))
                {
                    Job job = new Job { Kind = JobKind.Base, Name = BaseName(ward), Reason = "base", Base = ward };
                    job.Goals.Add(ward);
                    jobs.Add(job);
                }
            }
            // The remote traders last, so that "far from the roads" is measured against as much of
            // the network as there is.
            foreach (string trader in traders)
            {
                if (remote.Contains(trader))
                {
                    AddLocation(jobs, network, new[] { trader }, 0, 1, "trader, remote", central, remote);
                }
            }
            return jobs;
        }

        /// <summary>
        /// Where a job's search sets out from. A location's: every hub - the sacrificial stones and
        /// the bases with a road - at cost 0, and the main roads at alpha x their cost from the hub.
        /// A base's: the stones and the main roads, all at cost 0 - it is joined to wherever the
        /// network is nearest, and becomes a hub itself. A side road's the same, without the hub.
        /// </summary>
        public static List<Start> Starts(Job job, Network network)
        {
            List<Start> starts = new List<Start>();
            foreach (Vector2 temple in Temples())
            {
                starts.Add(new Start(temple));
            }
            if (job.Kind == JobKind.Base || job.Side)
            {
                foreach (Start point in network.Starts(RoadKind.Main, 0f))
                {
                    starts.Add(new Start(point.Position));
                }
                return starts;
            }
            foreach (Vector2 hub in network.Bases)
            {
                starts.Add(new Start(hub));
            }
            starts.AddRange(network.Starts(RoadKind.Main, OdinsPathsPlugin.Directness.Value));
            return starts;
        }

        /// <summary>
        /// Searches a job, records it in the network given - the road, the pinned instance or the
        /// base; a job that finds no way is recorded as unreachable - and, if writing, lays it and
        /// saves the network. Not writing, pass a copy (<see cref="Network.Copy"/>): a preview
        /// plans on it without touching the world's.
        /// </summary>
        public static IEnumerator Run(Job job, Network network, bool write, PathLayer.Options options, Action<string> report, Action<PathLayer.Outcome> done)
        {
            ZDOMan world = ZDOMan.instance;
            // Another road of the same entry may have been laid since the job was planned.
            if (job.Kind == JobKind.Location)
            {
                job.Goals.RemoveAll(goal => TakenBySibling(job, network, goal));
            }
            options.Write = write;
            options.Kind = job.Side ? RoadKind.Spur : RoadKind.Main;
            PathLayer.Outcome result = null;
            yield return PathLayer.Lay(Starts(job, network), job.Goals, job.Name, options, report, outcome => result = outcome);
            // A world left mid-search: its network is not this world's to save into.
            if (ZDOMan.instance != world)
            {
                done(result);
                yield break;
            }
            if (result.Failure != null)
            {
                network.Unreachable.Add(job.Name);
                if (job.Group != null && job.Group != job.Name)
                {
                    // The entry's other roads would search the same instances and fail the same way.
                    network.Unreachable.Add(job.Group);
                }
            }
            else
            {
                if (job.Kind == JobKind.Base)
                {
                    // Searched from the network to the base, but the base is the hub.
                    result.Road.MeasureFromEnd();
                    network.Bases.Add(job.Base);
                }
                else
                {
                    network.Pinned[job.Name] = result.Goal;
                }
                network.Roads.Add(result.Road);
            }
            if (write)
            {
                network.Save();
                if (result.Failure == null && Traders.Is(job.Name))
                {
                    Traders.Settle(network);
                }
            }
            if (result.Failure == null)
            {
                // A main road, then its spurs, then the next main road: a point of interest near
                // two roads is connected once, to the first.
                yield return PathLayer.LaySpurs(result.Road, network, write, options, report, spurs => result.Spurs = spurs);
                if (ZDOMan.instance == world)
                {
                    network.Roads.AddRange(result.Spurs.Roads);
                    network.Connected.AddRange(result.Spurs.Connected);
                    if (write)
                    {
                        network.Save();
                    }
                }
            }
            done(result);
        }

        /// <summary>
        /// A road to a location group, unless pinned or unreachable already: number is 0 for an
        /// entry of one road, else which of count it is. Every instance of every location in it is a
        /// goal, less those another road of the entry leads to; a group in central gets the one in
        /// the middle of the others (the medoid) alone.
        /// </summary>
        private static void AddLocation(List<Job> jobs, Network network, string[] locations, int number, int count,
            string reason, HashSet<string> central, HashSet<string> remote, bool side = false, string[] near = null)
        {
            string group = string.Join("|", locations);
            string name = number > 0 ? group + " #" + number : group;
            if (network.Pinned.TryGetValue(name, out Vector2 pinned) && Vanished(locations, pinned))
            {
                // A trader's road led to a camp the game dropped: placed elsewhere before the road
                // settled it (a road laid before 2026-10-01). The road stays; a new one goes to the trader.
                network.Pinned.Remove(name);
            }
            if (network.Pinned.ContainsKey(name) || network.Unreachable.Contains(name) || network.Unreachable.Contains(group))
            {
                return;
            }
            string label = Progress.NameOf(locations[0]) + (number > 0 ? " (" + number + " of " + count + ")" : "");
            Job job = new Job { Kind = JobKind.Location, Name = name, Group = group, Label = label, Reason = reason, Side = side };
            bool isCentral = false;
            bool isRemote = false;
            foreach (string location in locations)
            {
                isCentral |= central.Contains(location);
                isRemote |= remote.Contains(location);
                List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
                if (!ZoneSystem.instance.FindLocations(location, ref instances))
                {
                    continue;
                }
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    Vector2 at = new Vector2(instance.m_position.x, instance.m_position.z);
                    if (!TakenBySibling(job, network, at))
                    {
                        job.Goals.Add(at);
                    }
                }
            }
            if (near != null)
            {
                List<Vector2> beside = job.Goals.FindAll(goal => HasNear(near, goal));
                if (beside.Count > 0)
                {
                    job.Others.AddRange(job.Goals.FindAll(goal => !beside.Contains(goal)));
                    job.Goals = beside;
                }
            }
            if (isCentral && job.Goals.Count > 2)
            {
                Vector2 middle = Medoid(job.Goals);
                job.Others = job.Goals.FindAll(goal => goal != middle);
                job.Goals = new List<Vector2> { middle };
                job.Choice = "central";
            }
            else if (isRemote && job.Goals.Count > 1)
            {
                Vector2 farthest = Farthest(job.Goals, Starts(job, network));
                job.Others = job.Goals.FindAll(goal => goal != farthest);
                job.Goals = new List<Vector2> { farthest };
                job.Choice = "remote";
            }
            if (job.Goals.Count > 0)
            {
                jobs.Add(job);
            }
        }

        /// <summary>Whether an instance of one of these locations is within <see cref="NearBy"/> of p.</summary>
        private static bool HasNear(string[] near, Vector2 p)
        {
            List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
            foreach (string location in near)
            {
                if (!ZoneSystem.instance.FindLocations(location, ref instances))
                {
                    continue;
                }
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    if ((new Vector2(instance.m_position.x, instance.m_position.z) - p).sqrMagnitude < NearBy * NearBy)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Whether no instance of the locations is at the pinned point any more - a unique location
        /// (a trader) placed elsewhere since. A location missing from the world altogether (a mod
        /// removed) keeps its pin.
        /// </summary>
        private static bool Vanished(string[] locations, Vector2 pinned)
        {
            bool known = false;
            foreach (string location in locations)
            {
                List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
                if (!ZoneSystem.instance.FindLocations(location, ref instances))
                {
                    continue;
                }
                known = true;
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    if ((new Vector2(instance.m_position.x, instance.m_position.z) - pinned).sqrMagnitude < 1f)
                    {
                        return false;
                    }
                }
            }
            return known;
        }

        /// <summary>
        /// Of a trader's camps, the one farthest from every hub and road: where the network does not
        /// go yet, so that the road opens up a part of the world instead of adding to one.
        /// </summary>
        private static Vector2 Farthest(List<Vector2> points, List<Start> network)
        {
            Vector2 best = points[0];
            float most = -1f;
            foreach (Vector2 p in points)
            {
                float nearest = float.MaxValue;
                foreach (Start start in network)
                {
                    nearest = Mathf.Min(nearest, (start.Position - p).sqrMagnitude);
                }
                if (nearest > most)
                {
                    most = nearest;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>
        /// The locations a choice applies to under the play style: Explore's given here, none for
        /// Fastest (every road to the instance easiest to reach), the setting's list for Custom.
        /// </summary>
        private static HashSet<string> Chosen(string custom, string explore)
        {
            switch (OdinsPathsPlugin.Style.Value)
            {
                case OdinsPathsPlugin.PlayStyle.Fastest: return new HashSet<string>();
                case OdinsPathsPlugin.PlayStyle.Custom: return new HashSet<string>(Names(custom));
                default: return new HashSet<string>(Names(explore));
            }
        }

        /// <summary>Whether another road of the job's entry leads to this instance already.</summary>
        private static bool TakenBySibling(Job job, Network network, Vector2 at)
        {
            if (job.Group == null || job.Group == job.Name)
            {
                return false;
            }
            foreach (KeyValuePair<string, Vector2> pinned in network.Pinned)
            {
                if (pinned.Key != job.Name && pinned.Key.StartsWith(job.Group + " #") && (pinned.Value - at).sqrMagnitude < 1f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The point whose distances to the others add up least: of a boss's altars spread along
        /// a biome's arc, the one in its middle, so the road crosses the biome instead of reaching
        /// the altar nearest a coast by boat.
        /// </summary>
        private static Vector2 Medoid(List<Vector2> points)
        {
            Vector2 best = points[0];
            float least = float.MaxValue;
            foreach (Vector2 p in points)
            {
                float sum = 0f;
                foreach (Vector2 q in points)
                {
                    sum += Vector2.Distance(p, q);
                }
                if (sum < least)
                {
                    least = sum;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>A boss's number in the progression, counting the entries that share a key once.</summary>
        private static int BossNumber(List<Boss> bosses, int index)
        {
            HashSet<string> keys = new HashSet<string>();
            for (int b = 0; b <= index; b++)
            {
                keys.Add(bosses[b].Key.Length > 0 ? bosses[b].Key : "#" + b);
            }
            return keys.Count;
        }

        private static int BossCount(List<Boss> bosses) => BossNumber(bosses, bosses.Count - 1);

        /// <summary>The sacrificial stones: the world's start location, <c>Game.m_StartLocation</c>.</summary>
        public static List<Vector2> Temples()
        {
            List<Vector2> result = new List<Vector2>();
            List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
            string start = Game.instance != null ? Game.instance.m_StartLocation : "StartTemple";
            if (ZoneSystem.instance.FindLocations(start, ref instances))
            {
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    result.Add(new Vector2(instance.m_position.x, instance.m_position.z));
                }
            }
            return result;
        }

        /// <summary>
        /// Every base: the markers (wards by default) within <c>BaseRadius</c> of each other count as
        /// one, placed at its first marker, and a base needs <c>BaseMinPieces</c> pieces a player
        /// built within 30 m of one of its markers - an outpost's lone ward is not a base.
        /// </summary>
        public static List<Vector2> Bases()
        {
            int marker = OdinsPathsPlugin.BaseMarker.Value.GetStableHashCode();
            float radius = OdinsPathsPlugin.BaseRadius.Value;
            List<List<Vector2>> clusters = new List<List<Vector2>>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetPrefab() != marker)
                {
                    continue;
                }
                Vector3 p = zdo.GetPosition();
                Vector2 at = new Vector2(p.x, p.z);
                List<Vector2> joined = clusters.Find(cluster => cluster.Exists(w => (w - at).sqrMagnitude < radius * radius));
                if (joined != null)
                {
                    joined.Add(at);
                }
                else
                {
                    clusters.Add(new List<Vector2> { at });
                }
            }
            int[] pieces = new int[clusters.Count];
            int need = OdinsPathsPlugin.BaseMinPieces.Value;
            if (clusters.Count > 0 && need > 0)
            {
                foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
                {
                    if (zdo.GetLong(ZDOVars.s_creator, 0L) == 0L)
                    {
                        continue;
                    }
                    Vector3 p = zdo.GetPosition();
                    Vector2 at = new Vector2(p.x, p.z);
                    for (int c = 0; c < clusters.Count; c++)
                    {
                        if (pieces[c] < need && clusters[c].Exists(w => (w - at).sqrMagnitude < BaseCore * BaseCore))
                        {
                            pieces[c]++;
                        }
                    }
                }
            }
            List<Vector2> bases = new List<Vector2>();
            for (int c = 0; c < clusters.Count; c++)
            {
                if (pieces[c] >= need)
                {
                    bases.Add(clusters[c][0]);
                }
            }
            return bases;
        }

        private static string BaseName(Vector2 ward) => "base " + Mathf.RoundToInt(ward.x) + " " + Mathf.RoundToInt(ward.y);

        /// <summary>
        /// "Eikthyrnir:defeated_eikthyr, A|B*3:defeated_queen, ~C@D*2:key" into its entries: the
        /// location groups ('|' joins several into one), how many roads ('*n', else one), the key;
        /// a leading '~' makes them side roads, and '@' names the locations (again joined by '|')
        /// an instance should have near it. An entry without a key never counts as defeated.
        /// </summary>
        private static List<Boss> Progression()
        {
            List<Boss> bosses = new List<Boss>();
            foreach (string entry in Names(OdinsPathsPlugin.Progression.Value))
            {
                int colon = entry.IndexOf(':');
                string what = colon < 0 ? entry : entry.Substring(0, colon);
                string key = colon < 0 ? "" : entry.Substring(colon + 1).Trim();
                bool side = what.TrimStart().StartsWith("~");
                what = what.Replace("~", "");
                int count = 1;
                int star = what.IndexOf('*');
                if (star >= 0)
                {
                    int.TryParse(what.Substring(star + 1).Trim(), out count);
                    what = what.Substring(0, star);
                }
                string[] near = null;
                int at = what.IndexOf('@');
                if (at >= 0)
                {
                    near = Array.FindAll(what.Substring(at + 1).Split('|'), n => n.Trim().Length > 0);
                    near = Array.ConvertAll(near, n => n.Trim());
                    what = what.Substring(0, at);
                }
                List<string> locations = new List<string>();
                foreach (string part in what.Split('|'))
                {
                    if (part.Trim().Length > 0)
                    {
                        locations.Add(part.Trim());
                    }
                }
                if (locations.Count > 0)
                {
                    bosses.Add(new Boss { Locations = locations.ToArray(), Count = Mathf.Clamp(count, 1, 10), Key = key, Side = side, Near = near != null && near.Length > 0 ? near : null });
                }
            }
            return bosses;
        }

        private static List<string> Names(string list)
        {
            List<string> names = new List<string>();
            foreach (string part in list.Split(','))
            {
                string name = part.Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            return names;
        }
    }
}
