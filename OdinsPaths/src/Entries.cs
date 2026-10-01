using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Where a road into the Mistlands leaves the rest of the world behind. Their cliffs and
    /// terraces cost many times what the survey sees at 128 m, so a search that has to cross them
    /// from afar floods its whole ellipse first (an infected mine: 2 million cells, 460 s,
    /// 2026-09-25). Instead, around each goal in the Mistlands, the nearest ground outside them in
    /// each of eight directions - or a Mistlands beach, with deep water beyond - is an entry: the
    /// road is searched to the cheapest entry, and from there to the goal in a small search of
    /// its own (PathLayer).
    /// </summary>
    internal static class Entries
    {
        private const int Sectors = 8;
        /// <summary>How far from its goal an entry may lie.</summary>
        private const float MaxDistance = 1500f;
        private const float FirstRing = 48f;
        /// <summary>Between rings, and between samples along a ring.</summary>
        private const float Step = 24f;
        /// <summary>A Mistlands shore counts up to this far above the water.</summary>
        private const float BeachHeight = 2.5f;
        /// <summary>Beyond a beach, this far out along the ray, the water must be this deep.</summary>
        private const float SeaBeyond = 24f;
        private const float SeaDepth = 1f;

        /// <summary>
        /// The goals as a search should see them: every goal outside the Mistlands as it is (owner
        /// -1), and for every one inside, its entries (owner: the goal's index). Hands (null,
        /// null) back when no goal is in the Mistlands, or none has an entry. The land end of every
        /// harbour of the game's within reach of a goal is an entry too (<see cref="Ports"/>).
        /// </summary>
        public static IEnumerator Find(List<Vector2> goals, Action<List<Vector2>, List<int>> done, List<Ports.Port> ports = null)
        {
            List<Vector2> piers = ports != null ? ports.ConvertAll(p => p.LandEnd) : new List<Vector2>();
            WorldGenerator gen = WorldGenerator.instance;
            bool any = false;
            foreach (Vector2 goal in goals)
            {
                any |= gen.GetBiome(goal.x, goal.y) == Heightmap.Biome.Mistlands;
            }
            if (!any)
            {
                done(null, null);
                yield break;
            }
            float water = ZoneSystem.instance.m_waterLevel;
            List<Vector2> found = new List<Vector2>();
            List<int> owners = new List<int>();
            bool inner = false;
            yield return Worker.Run(() =>
            {
                for (int g = 0; g < goals.Count; g++)
                {
                    if (gen.GetBiome(goals[g].x, goals[g].y) != Heightmap.Biome.Mistlands)
                    {
                        found.Add(goals[g]);
                        owners.Add(-1);
                        continue;
                    }
                    List<Vector2> entries = Around(gen, goals[g], water);
                    entries.AddRange(piers.FindAll(p => Vector2.Distance(p, goals[g]) <= MaxDistance));
                    if (entries.Count == 0)
                    {
                        // Nowhere to come in from: searched to as it is, cliffs and all.
                        found.Add(goals[g]);
                        owners.Add(-1);
                        continue;
                    }
                    inner = true;
                    foreach (Vector2 entry in entries)
                    {
                        found.Add(entry);
                        owners.Add(g);
                    }
                }
            });
            if (inner)
            {
                done(found, owners);
            }
            else
            {
                done(null, null);
            }
        }

        /// <summary>Per direction, the nearest point on rings around the goal that is a way in.</summary>
        private static List<Vector2> Around(WorldGenerator gen, Vector2 goal, float water)
        {
            Vector2?[] best = new Vector2?[Sectors];
            int filled = 0;
            for (float r = FirstRing; r <= MaxDistance && filled < Sectors; r += Step)
            {
                int samples = Mathf.Max(Sectors * 2, Mathf.CeilToInt(2f * Mathf.PI * r / Step));
                for (int k = 0; k < samples; k++)
                {
                    float angle = 2f * Mathf.PI * k / samples;
                    int sector = Mathf.Min(Sectors - 1, (int)(angle / (2f * Mathf.PI) * Sectors));
                    if (best[sector] != null)
                    {
                        continue;
                    }
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 p = goal + direction * r;
                    if (p.sqrMagnitude > 9800f * 9800f)
                    {
                        continue;
                    }
                    if (IsWayIn(gen, p, direction, water))
                    {
                        best[sector] = p;
                        filled++;
                    }
                }
            }
            List<Vector2> result = new List<Vector2>();
            foreach (Vector2? p in best)
            {
                if (p != null)
                {
                    result.Add(p.Value);
                }
            }
            return result;
        }

        /// <summary>Dry ground (or a ford) outside the Mistlands, or a Mistlands beach facing open water.</summary>
        private static bool IsWayIn(WorldGenerator gen, Vector2 p, Vector2 outward, float water)
        {
            float height = Ground.Height(p.x, p.y);
            if (height < water - PathSearch.FordDepth)
            {
                return false;
            }
            Heightmap.Biome biome = gen.GetBiome(p.x, p.y);
            if (biome != Heightmap.Biome.Mistlands)
            {
                return true;
            }
            if (height > water + BeachHeight)
            {
                return false;
            }
            Vector2 beyond = p + outward * SeaBeyond;
            return Ground.Height(beyond.x, beyond.y) < water - SeaDepth;
        }
    }

    /// <summary>Work on a thread of its own, waited for frame by frame: the search, the entries.</summary>
    internal static class Worker
    {
        /// <summary>Runs work on a background thread; a failure is logged and handed to failed, if given.</summary>
        public static IEnumerator Run(Action work, Action<Exception> failed = null)
        {
            // Ground reads the zone prefab once, which only the main thread may.
            Ground.Prepare();
            Exception error = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    error = e;
                }
            })
            {
                IsBackground = true,
                Name = "OdinsPaths",
                Priority = System.Threading.ThreadPriority.BelowNormal,
            };
            thread.Start();
            while (thread.IsAlive)
            {
                yield return null;
            }
            thread.Join();
            if (error != null)
            {
                UnityEngine.Debug.LogError("[OdinsPaths] " + error);
                failed?.Invoke(error);
            }
        }
    }
}
