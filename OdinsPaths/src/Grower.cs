using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// When the network grows (docs/network.md): once the world is up on the server, everything
    /// the planner has due - the main roads to every boss (with <c>Reach</c> Progression up to the second
    /// undefeated one), the traders already
    /// there, the bases, each with its spurs; then on every night slept through, whatever became
    /// due since - a boss fell, a trader's camp was generated, a base was warded. The sleep's time
    /// skip is 12 seconds, so a short road is there at wake up and a long one soon after.
    /// One job at a time, spread over frames. Server only.
    /// </summary>
    internal static class Grower
    {
        /// <summary>Seconds the world has to be up before the first growth, so loading is over.</summary>
        private const float StartDelay = 10f;

        private static bool busy;
        private static ZDOMan busyFor;
        /// <summary>The world the start growth already ran for.</summary>
        private static ZDOMan startedFor;
        private static float readySince = -1f;
        private static bool again;

        /// <summary>
        /// Whether a growth or a dev lay is running in this world. A world left mid-way leaves the
        /// flag behind with its ZDOMan, so a new world starts free.
        /// </summary>
        public static bool Busy
        {
            get => busy && busyFor == ZDOMan.instance;
            set
            {
                busy = value;
                busyFor = ZDOMan.instance;
            }
        }

        /// <summary>
        /// Clears <see cref="Busy"/> for the world a coroutine started in, and only for it: one
        /// that outlived its world must not free the next world's growth.
        /// </summary>
        public static void Release(ZDOMan world)
        {
            if (busyFor == world)
            {
                busy = false;
            }
        }

        /// <summary>
        /// Runs work and the coroutines it yields itself, one step a frame as Unity would, but an
        /// exception anywhere in them is handed to failed and ends it. Unity stops a coroutine whose
        /// nested one threw without running its finally blocks, which left Busy set.
        /// </summary>
        public static IEnumerator Guarded(IEnumerator work, System.Action<System.Exception> failed)
        {
            Stack<IEnumerator> nested = new Stack<IEnumerator>();
            nested.Push(work);
            while (nested.Count > 0)
            {
                IEnumerator top = nested.Peek();
                bool more;
                try
                {
                    more = top.MoveNext();
                }
                catch (System.Exception e)
                {
                    failed(e);
                    yield break;
                }
                if (!more)
                {
                    nested.Pop();
                    // Unity, too, resumes the outer one a frame later: two budgeted steps never share a frame.
                    if (nested.Count > 0)
                    {
                        yield return null;
                    }
                }
                else if (top.Current is IEnumerator inner)
                {
                    nested.Push(inner);
                }
                else
                {
                    yield return top.Current;
                }
            }
        }

        /// <summary>From the plugin's Update: the start growth, once per world.</summary>
        public static void Tick()
        {
            Progress.Tick();
            if (Ready())
            {
                Footprints.Tick();
            }
            if (!Ready() || startedFor == ZDOMan.instance)
            {
                readySince = -1f;
                return;
            }
            if (readySince < 0f)
            {
                readySince = Time.time;
            }
            if (Time.time - readySince < StartDelay)
            {
                return;
            }
            startedFor = ZDOMan.instance;
            Request("the world is up");
        }

        /// <summary>
        /// A Debug build holds the growth, so a test world is only changed by the dev commands
        /// ("paths auto on" lets it go). A Release build never holds it.
        /// </summary>
#if DEBUG
        public static bool Held = true;
#else
        public static bool Held = false;
#endif

        /// <summary>Grows whatever is due; if a growth is running, once more after it.</summary>
        public static void Request(string why)
        {
            if (!OdinsPathsPlugin.GrowNetwork.Value || !Ready())
            {
                return;
            }
            if (Held)
            {
                Debug.Log("[OdinsPaths] Growth held (" + why + "): a Debug build grows only on 'paths auto on', 'paths grow' or 'paths lay'.");
                return;
            }
            if (Busy)
            {
                again = true;
                return;
            }
            Busy = true;
            OdinsPathsPlugin.Instance.StartCoroutine(Run(why));
        }

        private static bool Ready()
        {
            return ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null
                && ZoneSystem.instance != null && ZoneSystem.instance.LocationsGenerated && Network.Current != null;
        }

        private static IEnumerator Run(string why)
        {
            ZDOMan world = ZDOMan.instance;
            int laid = 0;
            // An exception in a road must not leave every later growth refused until the world is left.
            try
            {
                do
                {
                    again = false;
                    Traders.Settle(Network.Current);
                    List<Planner.Job> due = Planner.Due(Network.Current);
                    if (due.Count > 0)
                    {
                        Debug.Log("[OdinsPaths] Growing the network (" + why + "): " + due.Count + " due.");
                        if (!Progress.Active)
                        {
                            Progress.Begin(due.Count);
                            Progress.Announce("Odin's Paths: laying " + (due.Count == 1 ? "a road" : due.Count + " roads")
                                + ". The game may stutter until " + (due.Count == 1 ? "it is" : "they are") + " done.");
                        }
                    }
                    // Planned afresh after each job: the next one may start from the road just laid.
                    while (due.Count > 0 && ZDOMan.instance == world && Ready())
                    {
                        Planner.Job job = due[0];
                        Progress.Jobs(laid + due.Count);
                        Progress.Job(laid, job.Title);
                        PathLayer.Outcome result = null;
                        System.Exception error = null;
                        yield return Guarded(Planner.Run(job, Network.Current, true, new PathLayer.Options(), text => { }, outcome => result = outcome),
                            e => error = e);
                        if (ZDOMan.instance != world)
                        {
                            yield break;
                        }
                        if (error != null)
                        {
                            // Not marked unreachable, so it would be due again at once: the growth stops.
                            Debug.LogError("[OdinsPaths] Laying " + job + " failed; growing stops until the next trigger. " + error);
                            yield break;
                        }
                        Debug.Log("[OdinsPaths] " + (result.Failure == null
                            ? "Laid " + job + ": " + result.Trail.Length.ToString("F0") + " m, " + result.Written.Zones + " zones, "
                                + (result.Spurs != null ? result.Spurs.Roads.Count : 0) + " spurs; " + Progress.Frames() + "."
                            : "No way to " + job + " (" + result.Failure + "); not tried again."));
                        laid++;
                        due = Planner.Due(Network.Current);
                        Progress.Announce(result.Failure == null
                            ? "Odin's Paths: the road to " + job.Title + " is laid (" + laid + " of " + (laid + due.Count) + ")."
                            : "Odin's Paths: no road to " + job.Title + " could be found.");
                    }
                    why = "more became due meanwhile";
                }
                while (again && ZDOMan.instance == world && Ready());
                // Once, at the end of a growth that laid something: new forks get a post, changed ones new boards.
                if (laid > 0 && ZDOMan.instance == world && Ready())
                {
                    System.Exception error = null;
                    yield return Guarded(Signposts.Refresh(Network.Current), e => error = e);
                    if (error != null)
                    {
                        Debug.LogError("[OdinsPaths] Putting up the signposts failed. " + error);
                    }
                }
                if (Progress.Active)
                {
                    Progress.End();
                    if (laid > 1)
                    {
                        Progress.Announce("Odin's Paths: all roads are laid.");
                    }
                }
            }
            finally
            {
                if (Progress.Active && ZDOMan.instance == world)
                {
                    Progress.End();
                }
                Release(world);
            }
        }
    }

    public partial class OdinsPathsPlugin
    {
        void Update()
        {
            Grower.Tick();
        }

        /// <summary>Everyone is in bed and the server skips to morning: grow what became due.</summary>
        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SkipToMorning))]
        public static class GrowOnSleep
        {
            private static void Postfix()
            {
                Grower.Request("a night slept through");
            }
        }

        /// <summary>
        /// A vegvisir asks the server where the closest location of a kind is. For a main location
        /// the network has pinned, the answer is the pinned instance - the one the road leads to -
        /// instead of the one nearest the stone. A harbour stone's question is answered with the
        /// harbours across (<see cref="Harbours.Answer"/>).
        /// </summary>
        [HarmonyPatch(typeof(Game), nameof(Game.RPC_DiscoverClosestLocation))]
        public static class RevealPinned
        {
            private static bool Prefix(long sender, string name, Vector3 point, string pinName, int pinType, bool showMap, bool discoverAll)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                {
                    return true;
                }
                if (Harbours.Answer(sender, name, point, showMap))
                {
                    return false;
                }
                if (discoverAll)
                {
                    return true;
                }
                Network network = Network.Current;
                if (network == null || !network.Pinned.TryGetValue(name, out Vector2 pinned))
                {
                    return true;
                }
                List<ZoneSystem.LocationInstance> instances = new List<ZoneSystem.LocationInstance>();
                if (!ZoneSystem.instance.FindLocations(name, ref instances))
                {
                    return true;
                }
                ZoneSystem.LocationInstance best = instances[0];
                foreach (ZoneSystem.LocationInstance instance in instances)
                {
                    if (Distance(instance, pinned) < Distance(best, pinned))
                    {
                        best = instance;
                    }
                }
                if (Distance(best, pinned) > 1f)
                {
                    return true;
                }
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, "RPC_DiscoverLocationResponse", pinName, pinType, best.m_position, showMap);
                return false;
            }

            private static float Distance(ZoneSystem.LocationInstance instance, Vector2 to)
            {
                return Vector2.Distance(new Vector2(instance.m_position.x, instance.m_position.z), to);
            }
        }
    }
}
