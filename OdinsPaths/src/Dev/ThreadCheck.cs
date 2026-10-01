using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OdinsPaths
{
    // Dev only: src/Dev/ is compiled into Debug builds alone.
    public partial class OdinsPathsPlugin
    {
        /// <summary>Clusters are squares of this many samples a side, 4 m apart, the way a fine search samples.</summary>
        private const int ClusterSide = 64;
        private const float ClusterSpacing = 4f;

        private static bool threadCheckRunning;

        /// <summary>What the search's thread asks of the world generator for one sample.</summary>
        private struct Probe
        {
            public float Ground;
            public float Height;
            public Color Mask;
            public Heightmap.Biome Biome;
        }

        /// <summary>Mismatches against the reference, per field, and the first exception any thread threw.</summary>
        private sealed class Tally
        {
            public int Ground, Height, Mask, Biome;
            public Exception Error;

            public int Total => Ground + Height + Mask + Biome;

            public void Compare(Probe expected, Probe got)
            {
                if (!expected.Ground.Equals(got.Ground))
                {
                    Interlocked.Increment(ref Ground);
                }
                if (!expected.Height.Equals(got.Height))
                {
                    Interlocked.Increment(ref Height);
                }
                if (!expected.Mask.Equals(got.Mask))
                {
                    Interlocked.Increment(ref Mask);
                }
                if (expected.Biome != got.Biome)
                {
                    Interlocked.Increment(ref Biome);
                }
            }

            public override string ToString()
            {
                return Total + " mismatches (ground " + Ground + ", height " + Height + ", mask " + Mask + ", biome " + Biome + "), "
                    + (Error != null ? "exception: " + Error.GetType().Name + ": " + Error.Message : "no exception");
            }
        }

        /// <summary>
        /// "paths threads [threads] [samples]" - whether what the search reads off its thread
        /// (Ground.Height, WorldGenerator.GetBiome, GetHeight with its mask) may be asked from many
        /// threads at once, and how much faster that is. Samples come in clusters spread over the
        /// world, each read in a search's order. First one thread makes the reference; then every
        /// thread reads every cluster, each in its own order of clusters, so they keep evicting
        /// each other from the generator's one-entry river cache and race to fill Ground's corner
        /// cache (emptied first), and every value must match the reference to the bit. Then the
        /// samples split between 1, 2, 4... threads, timed. Last a control: the same stress over a
        /// cache that is not thread safe on purpose (one shared entry, its key published before
        /// its value, the shape of the river cache before the game locked it), to show the check
        /// does see a race when there is one. The game's own main and heightmap builder threads go
        /// on reading the generator meanwhile.
        /// </summary>
        private static void ThreadCheck(Terminal terminal, int threads, int samples)
        {
            if (WorldGenerator.instance == null || ZoneSystem.instance == null)
            {
                Say(terminal, "Not in a world.");
                return;
            }
            if (Grower.Busy || threadCheckRunning)
            {
                Say(terminal, "A growth, a lay or a check is running; try again when it is done.");
                return;
            }
            threadCheckRunning = true;
            Instance.StartCoroutine(Run());

            IEnumerator Run()
            {
                try
                {
                    WorldGenerator gen = WorldGenerator.instance;
                    Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
                    float zoneSize = prefabMap.m_width * prefabMap.m_scale;
                    List<Vector2[]> clusters = Clusters(gen.GetSeed(), Mathf.Max(4, samples / (ClusterSide * ClusterSide)));
                    int count = clusters.Count * ClusterSide * ClusterSide;
                    Say(terminal, "Thread check: " + count + " samples in " + clusters.Count + " clusters of "
                        + ClusterSide + " x " + ClusterSide + ", up to " + threads + " threads ("
                        + Environment.ProcessorCount + " logical cores)...");

                    Probe[][] reference = new Probe[clusters.Count][];
                    ForgetCorners();
                    double single = 0;
                    Tally failures = new Tally();
                    yield return Worker.Run(() =>
                    {
                        single = Timed(1, _ =>
                        {
                            for (int c = 0; c < clusters.Count; c++)
                            {
                                reference[c] = new Probe[clusters[c].Length];
                                for (int i = 0; i < clusters[c].Length; i++)
                                {
                                    reference[c][i] = Sample(gen, clusters[c][i]);
                                }
                            }
                        }, failures);
                    });
                    if (failures.Error != null)
                    {
                        Say(terminal, "The reference failed: " + failures.Error);
                        yield break;
                    }
                    Say(terminal, "Reference, 1 thread: " + single.ToString("F0") + " ms = "
                        + (single * 1000.0 / count).ToString("F2") + " us a sample.");

                    ForgetCorners();
                    Tally stress = new Tally();
                    double stressMs = 0;
                    yield return Worker.Run(() => stressMs = Timed(threads, t => Stress(clusters, reference, t, stress,
                        p => Sample(gen, p)), stress));
                    Say(terminal, "Stress, " + threads + " threads each reading all " + count + " samples: "
                        + stress + " (" + stressMs.ToString("F0") + " ms).");

                    StringBuilder scaling = new StringBuilder("Split, each sample once:");
                    Vector2[] flat = Flatten(clusters);
                    foreach (int k in ThreadCounts(threads))
                    {
                        ForgetCorners();
                        double ms = 0;
                        yield return Worker.Run(() => ms = Timed(k, t =>
                        {
                            int from = (int)((long)flat.Length * t / k);
                            int to = (int)((long)flat.Length * (t + 1) / k);
                            for (int i = from; i < to; i++)
                            {
                                Sample(gen, flat[i]);
                            }
                        }, failures));
                        scaling.Append("\n  ").Append(k).Append(k == 1 ? " thread:  " : " threads: ").Append(ms.ToString("F0"))
                            .Append(" ms, ").Append((single / ms).ToString("F1")).Append("x the reference");
                    }
                    if (failures.Error != null)
                    {
                        scaling.Append("\n  a thread failed: ").Append(failures.Error);
                    }
                    Say(terminal, scaling.ToString());

                    Tally controlOne = new Tally();
                    UnsafeCorners lone = new UnsafeCorners(gen, zoneSize);
                    yield return Worker.Run(() => Timed(1, t => Stress(clusters, reference, t, controlOne, p => Control(gen, lone, p)), controlOne));
                    Tally controlMany = new Tally();
                    UnsafeCorners shared = new UnsafeCorners(gen, zoneSize);
                    yield return Worker.Run(() => Timed(threads, t => Stress(clusters, reference, t, controlMany, p => Control(gen, shared, p)), controlMany));
                    Say(terminal, "Control, an unlocked one-entry corner cache: 1 thread " + controlOne.Total + " mismatches, "
                        + threads + " threads " + controlMany + ".\n"
                        + (stress.Total == 0 && stress.Error == null
                            ? (controlMany.Total > 0
                                ? "PASS: the generator read from many threads matches one thread to the bit, while the check catches an unsafe cache."
                                : "PASS on the generator, but the control found no race either: run it again with more samples to trust the result.")
                            : "FAIL: reading the generator from many threads gave different values or threw."));
                }
                finally
                {
                    threadCheckRunning = false;
                }
            }
        }

        private static Probe Sample(WorldGenerator gen, Vector2 p)
        {
            Probe probe;
            probe.Ground = Ground.Height(p.x, p.y);
            probe.Biome = gen.GetBiome(p.x, p.y);
            probe.Height = gen.GetHeight(p.x, p.y, out probe.Mask);
            return probe;
        }

        /// <summary>As <see cref="Sample"/>, with the control's cache standing in for Ground's.</summary>
        private static Probe Control(WorldGenerator gen, UnsafeCorners corners, Vector2 p)
        {
            Probe probe;
            probe.Ground = corners.Height(p.x, p.y);
            probe.Biome = gen.GetBiome(p.x, p.y);
            probe.Height = gen.GetHeight(p.x, p.y, out probe.Mask);
            return probe;
        }

        /// <summary>Thread t reads every cluster, in an order of clusters of its own, each in the search's order.</summary>
        private static void Stress(List<Vector2[]> clusters, Probe[][] reference, int t, Tally tally, Func<Vector2, Probe> sample)
        {
            int[] order = new int[clusters.Count];
            for (int c = 0; c < order.Length; c++)
            {
                order[c] = c;
            }
            System.Random random = new System.Random(t + 1);
            for (int c = order.Length - 1; c > 0; c--)
            {
                int swap = random.Next(c + 1);
                int kept = order[c];
                order[c] = order[swap];
                order[swap] = kept;
            }
            foreach (int c in order)
            {
                for (int i = 0; i < clusters[c].Length; i++)
                {
                    tally.Compare(reference[c][i], sample(clusters[c][i]));
                }
            }
        }

        /// <summary>Runs body(0..k-1) on k threads at once and hands back the wall time in ms; a thread's exception goes to the tally's Error.</summary>
        private static double Timed(int k, Action<int> body, Tally tally)
        {
            Thread[] started = new Thread[k];
            using (ManualResetEvent go = new ManualResetEvent(false))
            {
                for (int t = 0; t < k; t++)
                {
                    int index = t;
                    started[t] = new Thread(() =>
                    {
                        go.WaitOne();
                        try
                        {
                            body(index);
                        }
                        catch (Exception e)
                        {
                            // Never thrown on: an unhandled exception on a thread ends the game.
                            Interlocked.CompareExchange(ref tally.Error, e, null);
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "OdinsPaths check " + t,
                    };
                    started[t].Start();
                }
                Stopwatch watch = Stopwatch.StartNew();
                go.Set();
                foreach (Thread thread in started)
                {
                    thread.Join();
                }
                return watch.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>Squares of samples at random places on land and sea, inside the world's edge.</summary>
        private static List<Vector2[]> Clusters(int seed, int count)
        {
            System.Random random = new System.Random(seed);
            List<Vector2[]> clusters = new List<Vector2[]>(count);
            for (int c = 0; c < count; c++)
            {
                float r = 9500f * Mathf.Sqrt((float)random.NextDouble());
                float angle = (float)random.NextDouble() * 2f * Mathf.PI;
                Vector2 corner = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                Vector2[] points = new Vector2[ClusterSide * ClusterSide];
                for (int j = 0; j < ClusterSide; j++)
                {
                    for (int i = 0; i < ClusterSide; i++)
                    {
                        points[j * ClusterSide + i] = corner + new Vector2(i, j) * ClusterSpacing;
                    }
                }
                clusters.Add(points);
            }
            return clusters;
        }

        private static Vector2[] Flatten(List<Vector2[]> clusters)
        {
            List<Vector2> flat = new List<Vector2>();
            foreach (Vector2[] cluster in clusters)
            {
                flat.AddRange(cluster);
            }
            return flat.ToArray();
        }

        /// <summary>1, 2, 4... up to max, and max itself.</summary>
        private static List<int> ThreadCounts(int max)
        {
            List<int> counts = new List<int>();
            for (int k = 1; k < max; k *= 2)
            {
                counts.Add(k);
            }
            counts.Add(max);
            return counts;
        }

        /// <summary>Empties Ground's corner cache, so the next run fills it again - from many threads at once, for the stress.</summary>
        private static void ForgetCorners()
        {
            IDictionary corners = (IDictionary)AccessTools.Field(typeof(Ground), "corners").GetValue(null);
            lock (corners)
            {
                corners.Clear();
            }
        }

        /// <summary>
        /// Ground.Height with its corner cache replaced by a broken one: one shared entry whose
        /// key is published before the corners are looked up and stored, so a thread reading in
        /// between takes the last zone's corners for this one. Alone it is exact.
        /// </summary>
        private sealed class UnsafeCorners
        {
            private readonly WorldGenerator gen;
            private readonly float size;
            private long key = long.MinValue;
            private Heightmap.Biome sw, se, nw, ne;

            public UnsafeCorners(WorldGenerator gen, float size)
            {
                this.gen = gen;
                this.size = size;
            }

            public float Height(float x, float z)
            {
                Vector3 center = ZoneSystem.GetZonePos(ZoneSystem.GetZone(new Vector3(x, 0f, z)));
                float cornerX = center.x - size * 0.5f;
                float cornerZ = center.z - size * 0.5f;
                long zone = ((long)Mathf.RoundToInt(cornerX) << 32) | (uint)Mathf.RoundToInt(cornerZ);
                if (key != zone)
                {
                    key = zone;
                    sw = gen.GetBiome(cornerX, cornerZ);
                    se = gen.GetBiome(cornerX + size, cornerZ);
                    nw = gen.GetBiome(cornerX, cornerZ + size);
                    ne = gen.GetBiome(cornerX + size, cornerZ + size);
                }
                Heightmap.Biome a = sw, b = se, c = nw, d = ne;
                if (b == a && c == a && d == a)
                {
                    return gen.GetBiomeHeight(a, x, z, out Color _);
                }
                float tx = DUtils.SmoothStep(0f, 1f, (x - cornerX) / size);
                float tz = DUtils.SmoothStep(0f, 1f, (z - cornerZ) / size);
                float south = DUtils.Lerp(gen.GetBiomeHeight(a, x, z, out Color _), gen.GetBiomeHeight(b, x, z, out Color _), tx);
                float north = DUtils.Lerp(gen.GetBiomeHeight(c, x, z, out Color _), gen.GetBiomeHeight(d, x, z, out Color _), tx);
                return DUtils.Lerp(south, north, tz);
            }
        }
    }
}
