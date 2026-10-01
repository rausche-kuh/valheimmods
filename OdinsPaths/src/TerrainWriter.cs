using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Lays a trail into the world as the game's own terrain data: per zone, the blob a
    /// <c>TerrainComp</c> saves in <c>ZDOVars.s_TCData</c>, merged with what is there or written
    /// onto a new terrain compiler ZDO. No zone needs to be loaded; one that is picks the change
    /// up by itself (<c>TerrainComp.CheckLoad</c> watches the ZDO's data revision). Server only:
    /// only the server holds every ZDO, so only it can be sure a zone has no compiler yet.
    ///
    /// The base heights and base paint of a zone come from the game's heightmap builder thread,
    /// asked without blocking (<c>RequestTerrain</c>), so the main thread only merges and packs.
    /// </summary>
    internal static class TerrainWriter
    {
        private const string CompilerPrefab = "_TerrainCompiler";
        /// <summary>The blob format <c>TerrainComp.Save</c> writes.</summary>
        private const int FormatVersion = 1;
        /// <summary>How much the painted width wobbles, as a share of it.</summary>
        internal const float EdgeWobble = 0.15f;
        /// <summary>Beyond the path's edge, the levelling blends back into the ground over this.</summary>
        internal const float Shoulder = 1.5f;
        /// <summary>Segments further apart along the trail than this (20 m) are different legs of it.</summary>
        private const int OtherLeg = Trail.OtherLeg;
        /// <summary>
        /// At most this many zones asked of the game's heightmap builder at once. It has one thread
        /// for everything, first come first served, so a whole road's zones queued at once made the
        /// zones a walking player needs wait behind them (found through Procedural Roads #27).
        /// </summary>
        private const int MaxRequested = 6;
        /// <summary>
        /// Around the sacrificial stones no levelling within this, fading in over <see cref="Shoulder"/>
        /// past it: the start temple levels its own ground, and a road's levelling on top of it was
        /// bumpy (seen in game 2026-09-25). The paint still runs up to the stones.
        /// </summary>
        private const float TempleKeep = 10f;
        /// <summary>
        /// Where the ground is too steep for a main road's stone it is painted dirt instead, fading
        /// out from this slope to <see cref="SteepestDirt"/> (42° to 53°): a road that cut and filled
        /// all it could and still climbs steeply looked flattened with no road on it.
        /// </summary>
        private const float SteepDirtFade = 0.9f;
        private const float SteepestDirt = 1.3f;

        /// <summary>What was in a zone before the trail, so a dev "undo" can put it back.</summary>
        internal sealed class ZoneBackup
        {
            public ZDOID Compiler;
            public byte[] Data;
        }

        internal sealed class Result
        {
            public int Zones;
            public int Created;
            public int Painted;
            public int Levelled;
            public int Skipped;
            public readonly Dictionary<Vector2s, ZoneBackup> Backups = new Dictionary<Vector2s, ZoneBackup>();
            /// <summary>The vertices levelled through this result (<see cref="Vertex"/>): a door path may level them again (<see cref="Trail.OverOwn"/>).</summary>
            public readonly HashSet<long> Ours = new HashSet<long>();
        }

        /// <summary>A zone's vertex as one key.</summary>
        private static long Vertex(Vector2s zone, int index) => ((long)(zone.x + 32768) << 40) | ((long)(zone.y + 32768) << 20) | (uint)index;

        public static IEnumerator Write(Trail trail, List<Circle> locations, Structures structures, Result result)
        {
            float scale = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>().m_scale;
            float reach = trail.Kind.Reach + scale;

            Dictionary<Vector2s, List<int>> zones = ZonesNear(trail, reach);
            List<Vector2> temples = Planner.Temples();
            yield return EachZone(zones.Keys,
                (zone, data) => WriteZone(zone, data, trail, zones[zone], locations, structures, temples, result));
        }

        /// <summary>
        /// A harbour building's pad (<see cref="Pad"/>): every vertex in its box levelled to its
        /// height, fading out across its margin, as far as it may cut and fill. Skipped as a road's
        /// levelling is: what is modified already (the player's work, the road, the path to the
        /// door), the shore, locations, and what stands within a piece's reach - structures must
        /// leave out the building's own pieces.
        /// </summary>
        public static IEnumerator WritePad(Pad pad, List<Circle> locations, Structures structures, Result result)
        {
            pad.Bounds(out Vector2 min, out Vector2 max);
            Vector2s low = ZoneSystem.GetZone(new Vector3(min.x, 0f, min.y));
            Vector2s high = ZoneSystem.GetZone(new Vector3(max.x, 0f, max.y));
            List<Vector2s> zones = new List<Vector2s>();
            for (int x = low.x; x <= high.x; x++)
            {
                for (int y = low.y; y <= high.y; y++)
                {
                    zones.Add(new Vector2s(x, y));
                }
            }
            yield return EachZone(zones, (zone, data) => WritePadZone(zone, data, pad, locations, structures, result));
        }

        /// <summary>
        /// Asks the game's heightmap builder for the zones a few at a time, takes each as it is
        /// ready, and hands it to write while the frame's budget lasts, at least one a frame.
        /// </summary>
        private static IEnumerator EachZone(ICollection<Vector2s> zones, System.Action<Vector2s, HeightmapBuilder.HMBuildData> write)
        {
            Heightmap prefabMap = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>();
            int width = prefabMap.m_width;
            float scale = prefabMap.m_scale;
            float budget = OdinsPathsPlugin.SearchBudgetMs.Value;
            int total = zones.Count;
            System.Diagnostics.Stopwatch frame = new System.Diagnostics.Stopwatch();
            List<Vector2s> pending = new List<Vector2s>(zones);
            List<Vector2s> requested = new List<Vector2s>();
            Dictionary<Vector2s, HeightmapBuilder.HMBuildData> built = new Dictionary<Vector2s, HeightmapBuilder.HMBuildData>();
            List<Vector2s> done = new List<Vector2s>();
            while (pending.Count > 0 || requested.Count > 0 || built.Count > 0)
            {
                while (requested.Count + built.Count < MaxRequested && pending.Count > 0)
                {
                    requested.Add(pending[pending.Count - 1]);
                    pending.RemoveAt(pending.Count - 1);
                }
                for (int i = requested.Count - 1; i >= 0; i--)
                {
                    HeightmapBuilder.HMBuildData data = HeightmapBuilder.instance.RequestTerrain(
                        ZoneSystem.GetZonePos(requested[i]), width, scale, false, WorldGenerator.instance);
                    if (data != null)
                    {
                        built[requested[i]] = data;
                        requested.RemoveAt(i);
                    }
                }
                done.Clear();
                frame.Restart();
                foreach (KeyValuePair<Vector2s, HeightmapBuilder.HMBuildData> entry in built)
                {
                    if (done.Count > 0 && frame.Elapsed.TotalMilliseconds > budget)
                    {
                        break;
                    }
                    write(entry.Key, entry.Value);
                    done.Add(entry.Key);
                }
                foreach (Vector2s zone in done)
                {
                    built.Remove(zone);
                }
                Progress.Set(1f - (float)(pending.Count + requested.Count + built.Count) / Mathf.Max(total, 1));
                yield return null;
            }
        }

        /// <summary>
        /// Every segment with dry land at one end, filed under each zone that has a point within
        /// reach of it.
        /// </summary>
        internal static Dictionary<Vector2s, List<int>> ZonesNear(Trail trail, float reach)
        {
            Dictionary<Vector2s, List<int>> zones = new Dictionary<Vector2s, List<int>>();
            for (int i = 0; i < trail.Points.Count - 1; i++)
            {
                if (trail.Water[i] && trail.Water[i + 1])
                {
                    continue;
                }
                Vector2 a = trail.Points[i];
                Vector2 b = trail.Points[i + 1];
                Vector2s min = ZoneSystem.GetZone(new Vector3(Mathf.Min(a.x, b.x) - reach, 0f, Mathf.Min(a.y, b.y) - reach));
                Vector2s max = ZoneSystem.GetZone(new Vector3(Mathf.Max(a.x, b.x) + reach, 0f, Mathf.Max(a.y, b.y) + reach));
                for (int x = min.x; x <= max.x; x++)
                {
                    for (int y = min.y; y <= max.y; y++)
                    {
                        Vector2s zone = new Vector2s(x, y);
                        if (!zones.TryGetValue(zone, out List<int> segments))
                        {
                            zones[zone] = segments = new List<int>();
                        }
                        segments.Add(i);
                    }
                }
            }
            return zones;
        }

        private static void WriteZone(Vector2s zone, HeightmapBuilder.HMBuildData data, Trail trail,
            List<int> segments, List<Circle> locations, Structures structures, List<Vector2> temples, Result result)
        {
            int width = data.m_width;
            int pitch = width + 1;
            float scale = data.m_scale;
            Vector3 center = ZoneSystem.GetZonePos(zone);
            if (CountCompilers(zone, out ZDO compiler) > 1)
            {
                // The game keeps one and destroys the other as soon as the zone loads; which one
                // is not ours to guess (Procedural Roads #27 leaves such zones alone too).
                Debug.LogWarning("[OdinsPaths] Zone " + zone + ": more than one terrain compiler, left alone.");
                result.Skipped++;
                return;
            }
            // A road's search list holds thousands of locations; a zone overlaps a few at most.
            locations = Overlapping(locations, new Vector2(center.x, center.z), width * scale * 0.5f);
            byte[] old = compiler != null ? compiler.GetByteArray(ZDOVars.s_TCData) : null;
            TerrainData terrain = old != null ? TerrainData.Decode(old, pitch) : new TerrainData(pitch);
            if (terrain == null)
            {
                Debug.LogWarning("[OdinsPaths] Zone " + zone + ": terrain data in an unknown shape, left alone.");
                result.Skipped++;
                return;
            }

            float waterLevel = ZoneSystem.instance.m_waterLevel;
            RoadKind kind = trail.Kind;
            float maxHalfWidth = kind.MaxHalfWidth;
            bool level = kind.Levelling;
            float origin = -width * scale * 0.5f;
            // Heights sit on the vertices. The paint mask's texels are half a metre off them, as
            // TerrainComp.PaintCleared's half offset has it.
            Vector2 firstVertex = new Vector2(center.x + origin, center.z + origin);
            Vector2 firstTexel = firstVertex + new Vector2(0.5f, 0.5f) * scale;
            float flatFactor = kind.FlatFactor;
            float[] texelDistance = Nearest(trail, segments, firstTexel, pitch, scale, maxHalfWidth * (1f + EdgeWobble), out float[] _, out bool[] texelRaised, out int[] texelSegment);
            float[] vertexDistance = Nearest(trail, segments, firstVertex, pitch, scale, maxHalfWidth * flatFactor + Shoulder, out float[] profile, out bool[] raised, out int[] vertexSegment);

            int painted = 0;
            int levelled = 0;
            // The levelling first, so that the paint sees the ground as it will be.
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    float baseHeight = data.m_baseHeights[index];
                    float distance = vertexDistance[index];
                    Vector2 vertex = firstVertex + new Vector2(x, y) * scale;
                    if (!level || distance >= maxHalfWidth * flatFactor + Shoulder)
                    {
                        continue;
                    }
                    // Flat to the paint's widest on a main road, to half its width on a spur.
                    float halfWidth = trail.HalfWidthAt(vertexSegment[index], vertex) * flatFactor;
                    bool causeway = raised[index];
                    long key = Vertex(zone, index);
                    // Levelled before by this lay (the road under a door path): levelled again, blended from that.
                    bool again = terrain.ModifiedHeight[index] && trail.OverOwn && result.Ours.Contains(key);
                    if (distance >= halfWidth + Shoulder || (terrain.ModifiedHeight[index] && !again)
                        || (!causeway && baseHeight < waterLevel + Trail.ShoreMargin + 0.2f) || InAny(locations, vertex))
                    {
                        continue;
                    }
                    float building = structures.Distance(vertex, Structures.PieceReach + Shoulder);
                    if (building < Structures.PieceReach)
                    {
                        continue;
                    }
                    float temple = TempleDistance(temples, vertex);
                    if (temple < TempleKeep)
                    {
                        continue;
                    }
                    float blend = distance <= halfWidth ? 1f : Falloff(kind, (distance - halfWidth) / Shoulder);
                    blend *= Mathf.SmoothStep(0f, 1f, (building - Structures.PieceReach) / Shoulder);
                    blend *= Mathf.SmoothStep(0f, 1f, (temple - TempleKeep) / Shoulder);
                    // A causeway fills up from as deep as the swamp is let go; elsewhere the cut is the
                    // trail's there: the kind's, deeper at a hairpin's landing and in the Mistlands.
                    float maxCut = trail.MaxCutAt(vertexSegment[index]);
                    float maxRaise = causeway ? Trail.SwampFill + Trail.CausewayHeight : maxCut;
                    float before = again ? terrain.LevelDelta[index] + terrain.SmoothDelta[index] : 0f;
                    float delta = Mathf.Lerp(before, Mathf.Clamp(profile[index] - baseHeight, -maxCut, maxRaise), blend);
                    if (Mathf.Abs(delta - before) < 0.02f)
                    {
                        continue;
                    }
                    terrain.ModifiedHeight[index] = true;
                    terrain.LevelDelta[index] = delta;
                    terrain.SmoothDelta[index] = 0f;
                    result.Ours.Add(key);
                    levelled++;
                }
            }
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    float baseHeight = data.m_baseHeights[index];
                    Vector2 texel = firstTexel + new Vector2(x, y) * scale;
                    float wobble = 1f + EdgeWobble * (Mathf.PerlinNoise(texel.x / 5f, texel.y / 5f) * 2f - 1f);
                    float paintDistance = texelDistance[index];
                    float edge = paintDistance < float.MaxValue ? trail.HalfWidthAt(texelSegment[index], texel) * wobble : 0f;
                    // No paint under a building (the levelling fades out over a shoulder beside one),
                    // none on a bank or cliff. A causeway's texels are painted though the ground is under the water now: it is raised.
                    // Stone too steep for it gives way to dirt, which may go a little steeper.
                    float slope = paintDistance < edge ? Slope(terrain, data.m_baseHeights, pitch, scale, x, y) : float.MaxValue;
                    float stone = Fade(slope, kind.PaintSlopeFade, kind.MaxPaintSlope);
                    float dirt = kind.Paved ? Fade(slope, SteepDirtFade, SteepestDirt) : 0f;
                    float steep = Mathf.Max(stone, dirt);
                    if (paintDistance < edge && steep > 0f && (baseHeight >= waterLevel + Trail.ShoreMargin || (level && texelRaised[index]))
                        && structures.Distance(texel, Structures.PieceReach) >= Structures.PieceReach
                        && Paint(terrain, data.m_baseMask[index], index, texel,
                            Trail.Handover(Heightmap.m_paintMaskDirt, trail.PaintAt(texelSegment[index]), dirt > stone ? stone / dirt : 1f),
                            Mathf.Clamp01((edge - paintDistance) / kind.EdgeSoftness) * steep))
                    {
                        painted++;
                    }
                }
            }
            if (painted == 0 && levelled == 0)
            {
                return;
            }
            Commit(zone, compiler, old, terrain, center, width * scale, result);
            result.Painted += painted;
            result.Levelled += levelled;
        }

        private static void WritePadZone(Vector2s zone, HeightmapBuilder.HMBuildData data, Pad pad, List<Circle> locations, Structures structures, Result result)
        {
            int width = data.m_width;
            int pitch = width + 1;
            float scale = data.m_scale;
            Vector3 center = ZoneSystem.GetZonePos(zone);
            if (CountCompilers(zone, out ZDO compiler) > 1)
            {
                Debug.LogWarning("[OdinsPaths] Zone " + zone + ": more than one terrain compiler, left alone.");
                result.Skipped++;
                return;
            }
            locations = Overlapping(locations, new Vector2(center.x, center.z), width * scale * 0.5f);
            byte[] old = compiler != null ? compiler.GetByteArray(ZDOVars.s_TCData) : null;
            TerrainData terrain = old != null ? TerrainData.Decode(old, pitch) : new TerrainData(pitch);
            if (terrain == null)
            {
                Debug.LogWarning("[OdinsPaths] Zone " + zone + ": terrain data in an unknown shape, left alone.");
                result.Skipped++;
                return;
            }
            float waterLevel = ZoneSystem.instance.m_waterLevel;
            float origin = -width * scale * 0.5f;
            Vector2 firstVertex = new Vector2(center.x + origin, center.z + origin);
            int levelled = 0;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    Vector2 vertex = firstVertex + new Vector2(x, y) * scale;
                    float weight = pad.Weight(vertex.x, vertex.y);
                    float baseHeight = data.m_baseHeights[index];
                    if (weight <= 0f || terrain.ModifiedHeight[index] || baseHeight < waterLevel + Trail.ShoreMargin + 0.2f || InAny(locations, vertex))
                    {
                        continue;
                    }
                    float building = structures.Distance(vertex, Structures.PieceReach + Shoulder);
                    if (building < Structures.PieceReach)
                    {
                        continue;
                    }
                    float delta = pad.Delta(baseHeight) * weight * Mathf.SmoothStep(0f, 1f, (building - Structures.PieceReach) / Shoulder);
                    if (Mathf.Abs(delta) < 0.02f)
                    {
                        continue;
                    }
                    terrain.ModifiedHeight[index] = true;
                    terrain.LevelDelta[index] = delta;
                    terrain.SmoothDelta[index] = 0f;
                    result.Ours.Add(Vertex(zone, index));
                    levelled++;
                }
            }
            if (levelled == 0)
            {
                return;
            }
            Commit(zone, compiler, old, terrain, center, width * scale, result);
            result.Levelled += levelled;
        }

        /// <summary>A zone's terrain written back: onto its compiler, a new one if it has none, the old data kept for a dev undo.</summary>
        private static void Commit(Vector2s zone, ZDO compiler, byte[] old, TerrainData terrain, Vector3 center, float size, Result result)
        {
            ZoneBackup backup = new ZoneBackup { Data = old };
            if (compiler == null)
            {
                compiler = CreateCompiler(center);
                result.Created++;
            }
            backup.Compiler = compiler.m_uid;
            if (!result.Backups.ContainsKey(zone))
            {
                result.Backups[zone] = backup;
            }
            // Zone centre and a radius over its corners: a loaded client resets the grass of
            // the whole zone when it reloads (TerrainComp.CheckLoad, one operation more).
            compiler.Set(ZDOVars.s_TCData, terrain.Encode(center, size * 0.72f));
            result.Zones++;
        }

        /// <summary>
        /// How much of the levelling a vertex t of the way across the shoulder keeps: with a sharp
        /// edge it drops off at once and eases into the ground (a bench with a bank beside it);
        /// otherwise it rounds off at both ends.
        /// </summary>
        private static float Falloff(RoadKind kind, float t)
        {
            t = Mathf.Clamp01(t);
            return kind.SharpEdge ? (1f - t) * (1f - t) : 1f - Mathf.SmoothStep(0f, 1f, t);
        }

        /// <summary>
        /// 1 up to a slope of from, fading to 0 at to: the kind's <see cref="RoadKind.MaxPaintSlope"/>
        /// for its own paint - stone on a bank or a cliff beside a cut looks drawn on (seen in game
        /// 2026-09-25) -, <see cref="SteepestDirt"/> for the dirt that stands in for stone.
        /// </summary>
        private static float Fade(float slope, float from, float to)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, (slope - from) / (to - from));
        }

        /// <summary>The slope, rise over run, of a texel's ground: the four vertices around it, levelled.</summary>
        private static float Slope(TerrainData terrain, List<float> baseHeights, int pitch, float scale, int x, int y)
        {
            int x1 = Mathf.Min(x + 1, pitch - 1);
            int y1 = Mathf.Min(y + 1, pitch - 1);
            float a = Final(terrain, baseHeights, y * pitch + x);
            float b = Final(terrain, baseHeights, y * pitch + x1);
            float c = Final(terrain, baseHeights, y1 * pitch + x);
            float d = Final(terrain, baseHeights, y1 * pitch + x1);
            float dx = ((b - a) + (d - c)) * 0.5f;
            float dy = ((c - a) + (d - b)) * 0.5f;
            return Mathf.Sqrt(dx * dx + dy * dy) / scale;
        }

        /// <summary>How far p is from the nearest sacrificial stones' centre.</summary>
        private static float TempleDistance(List<Vector2> temples, Vector2 p)
        {
            float nearest = float.MaxValue;
            foreach (Vector2 temple in temples)
            {
                nearest = Mathf.Min(nearest, Vector2.Distance(temple, p));
            }
            return nearest;
        }

        private static float Final(TerrainData terrain, List<float> baseHeights, int index)
        {
            return terrain.ModifiedHeight[index] ? baseHeights[index] + terrain.LevelDelta[index] + terrain.SmoothDelta[index] : baseHeights[index];
        }

        /// <summary>
        /// The road's paint (dirt or stone) over the ground, fading at the edge, lerped as
        /// <c>TerrainComp.PaintCleared</c> does; alpha (the vegetation mask) is kept. A texel
        /// cultivated or paved already - by a player, or by a main road laid before, which a spur
        /// then stops at - is left alone, except that in the Deep North the green channel is snow
        /// depth, which either paint clears.
        /// </summary>
        private static bool Paint(TerrainData terrain, Color baseMask, int index, Vector2 at, Color paint, float weight)
        {
            Color current = terrain.ModifiedPaint[index] ? terrain.Paint[index] : baseMask;
            bool deepNorth = WorldGenerator.IsDeepnorth(at.x, at.y);
            if (current.b > 0.5f || (!deepNorth && current.g > 0.5f))
            {
                return false;
            }
            Color painted = current;
            painted.r = Mathf.Lerp(current.r, paint.r, weight);
            painted.g = Mathf.Lerp(current.g, paint.g, weight);
            painted.b = Mathf.Lerp(current.b, paint.b, weight);
            if (terrain.ModifiedPaint[index] && painted == current)
            {
                return false;
            }
            terrain.ModifiedPaint[index] = true;
            terrain.Paint[index] = painted;
            return true;
        }

        /// <summary>
        /// For a pitch x pitch grid starting at first: each point's distance to the nearest
        /// segment, and the path height there. Each segment only visits the points within reach
        /// of it; the rest stay at float.MaxValue. Where another stretch of the trail - more than
        /// <see cref="OtherLeg"/> points along it - is within reach too, the leg of a hairpin
        /// above or below, the height between their flat parts is blended between the two by
        /// how far past each one's flat it is: the nearest alone left a ridge or a step between
        /// the legs (messy turns on steep Black Forest slopes, seen in game 2026-09-25), and
        /// blending on the flat itself tilted the road toward the other leg.
        /// </summary>
        private static float[] Nearest(Trail trail, List<int> segments, Vector2 first, int pitch, float scale,
            float reach, out float[] profile, out bool[] causeway, out int[] nearest)
        {
            float[] distance = new float[pitch * pitch];
            profile = new float[pitch * pitch];
            causeway = new bool[pitch * pitch];
            nearest = new int[pitch * pitch];
            float[] otherDistance = new float[pitch * pitch];
            float[] otherProfile = new float[pitch * pitch];
            int[] other = new int[pitch * pitch];
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = float.MaxValue;
                otherDistance[i] = float.MaxValue;
                nearest[i] = int.MinValue;
                other[i] = int.MinValue;
            }
            foreach (int segment in segments)
            {
                Vector2 a = trail.Points[segment];
                Vector2 b = trail.Points[segment + 1];
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - first.x) / scale));
                int x1 = Mathf.Min(pitch - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - first.x) / scale));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach - first.y) / scale));
                int y1 = Mathf.Min(pitch - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach - first.y) / scale));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int index = y * pitch + x;
                        float d = trail.Nearest(first + new Vector2(x, y) * scale, segment, out float height);
                        if (d > reach)
                        {
                            continue;
                        }
                        if (d < distance[index])
                        {
                            // The nearest so far becomes the other leg, if it is one.
                            if (nearest[index] != int.MinValue && Mathf.Abs(nearest[index] - segment) > OtherLeg)
                            {
                                otherDistance[index] = distance[index];
                                otherProfile[index] = profile[index];
                                other[index] = nearest[index];
                            }
                            distance[index] = d;
                            profile[index] = height;
                            nearest[index] = segment;
                            causeway[index] = trail.CausewayAt(segment);
                        }
                        else if (d < otherDistance[index] && Mathf.Abs(nearest[index] - segment) > OtherLeg)
                        {
                            otherDistance[index] = d;
                            otherProfile[index] = height;
                            other[index] = segment;
                        }
                    }
                }
            }
            for (int i = 0; i < distance.Length; i++)
            {
                // The other leg must still be another leg of the final nearest.
                if (otherDistance[i] < float.MaxValue && Mathf.Abs(other[i] - nearest[i]) > OtherLeg && !causeway[i])
                {
                    Vector2 p = first + new Vector2(i % pitch, i / pitch) * scale;
                    float flat = trail.Kind.FlatFactor;
                    float past = distance[i] - trail.HalfWidthAt(nearest[i], p) * flat;
                    float otherPast = Mathf.Max(0f, otherDistance[i] - trail.HalfWidthAt(other[i], p) * flat);
                    if (past > 0f)
                    {
                        profile[i] = Mathf.Lerp(profile[i], otherProfile[i], past / (past + otherPast + 0.01f));
                    }
                }
            }
            return distance;
        }

        private static bool InAny(List<Circle> circles, Vector2 p)
        {
            foreach (Circle circle in circles)
            {
                if (circle.Contains(p))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The circles that reach into the square of the given half size around center.</summary>
        private static List<Circle> Overlapping(List<Circle> circles, Vector2 center, float half)
        {
            List<Circle> result = new List<Circle>();
            foreach (Circle circle in circles)
            {
                float dx = Mathf.Max(Mathf.Abs(circle.Center.x - center.x) - half, 0f);
                float dy = Mathf.Max(Mathf.Abs(circle.Center.y - center.y) - half, 0f);
                if (dx * dx + dy * dy < circle.Radius * circle.Radius)
                {
                    result.Add(circle);
                }
            }
            return result;
        }

        internal static ZDO FindCompiler(Vector2s zone)
        {
            CountCompilers(zone, out ZDO first);
            return first;
        }

        /// <summary>How many terrain compilers a zone has, and the first of them.</summary>
        private static int CountCompilers(Vector2s zone, out ZDO first)
        {
            int hash = CompilerPrefab.GetStableHashCode();
            List<ZDO> objects = new List<ZDO>();
            ZDOMan.instance.FindObjects(zone, objects, new HashSet<ZoneSystem.SectorIndex>());
            first = null;
            int count = 0;
            foreach (ZDO zdo in objects)
            {
                if (zdo.GetPrefab() == hash)
                {
                    first = first ?? zdo;
                    count++;
                }
            }
            return count;
        }

        /// <summary>A compiler ZDO as <c>ZNetView.Awake</c> would make it for the prefab.</summary>
        private static ZDO CreateCompiler(Vector3 center)
        {
            int hash = CompilerPrefab.GetStableHashCode();
            ZNetView view = ZNetScene.instance.GetPrefab(hash).GetComponent<ZNetView>();
            ZDO zdo = ZDOMan.instance.CreateNewZDO(center, hash);
            zdo.Persistent = view.m_persistent;
            zdo.Type = view.m_type;
            zdo.Distant = view.m_distant;
            zdo.SetPrefab(hash);
            zdo.SetRotation(Quaternion.identity);
            return zdo;
        }

        /// <summary>A zone's terrain edits, in <c>TerrainComp</c>'s layout: (width+1)² per array.</summary>
        internal sealed class TerrainData
        {
            public int Operations;
            public readonly bool[] ModifiedHeight;
            public readonly float[] LevelDelta;
            public readonly float[] SmoothDelta;
            public readonly bool[] ModifiedPaint;
            public readonly Color[] Paint;

            public TerrainData(int pitch)
            {
                int n = pitch * pitch;
                ModifiedHeight = new bool[n];
                LevelDelta = new float[n];
                SmoothDelta = new float[n];
                ModifiedPaint = new bool[n];
                Paint = new Color[n];
            }

            /// <summary>
            /// <c>TerrainComp.Load</c>, including its conversion of old saves whose paint arrays
            /// were width² instead of (width+1)². Null if the heights do not fit this pitch.
            /// </summary>
            public static TerrainData Decode(byte[] bytes, int pitch)
            {
                ZPackage pkg = new ZPackage(Utils.Decompress(bytes));
                TerrainData data = new TerrainData(pitch);
                pkg.ReadInt();
                data.Operations = pkg.ReadInt();
                pkg.ReadVector3();
                pkg.ReadSingle();
                int heights = pkg.ReadInt();
                if (heights != data.ModifiedHeight.Length)
                {
                    return null;
                }
                for (int i = 0; i < heights; i++)
                {
                    data.ModifiedHeight[i] = pkg.ReadBool();
                    if (data.ModifiedHeight[i])
                    {
                        data.LevelDelta[i] = pkg.ReadSingle();
                        data.SmoothDelta[i] = pkg.ReadSingle();
                    }
                }
                int paints = pkg.ReadInt();
                int width = pitch - 1;
                if (paints != data.ModifiedPaint.Length && paints != width * width)
                {
                    return null;
                }
                for (int j = 0; j < paints; j++)
                {
                    data.ModifiedPaint[j] = pkg.ReadBool();
                    if (data.ModifiedPaint[j])
                    {
                        data.Paint[j] = new Color(pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle());
                    }
                }
                if (paints == width * width)
                {
                    Color[] paint = (Color[])data.Paint.Clone();
                    bool[] modified = (bool[])data.ModifiedPaint.Clone();
                    for (int k = 0; k < data.Paint.Length; k++)
                    {
                        int row = k / pitch;
                        int nextRow = (k + 1) / pitch;
                        int source = k - row;
                        if (row == width)
                        {
                            source -= width;
                        }
                        if (k > 0 && (k - row) % width == 0 && (k + 1 - nextRow) % width == 0)
                        {
                            source--;
                        }
                        data.Paint[k] = paint[source];
                        data.ModifiedPaint[k] = modified[source];
                    }
                }
                return data;
            }

            /// <summary><c>TerrainComp.Save</c>, one operation more than before.</summary>
            public byte[] Encode(Vector3 opPoint, float opRadius)
            {
                ZPackage pkg = new ZPackage();
                pkg.Write(FormatVersion);
                pkg.Write(Operations + 1);
                pkg.Write(opPoint);
                pkg.Write(opRadius);
                pkg.Write(ModifiedHeight.Length);
                for (int i = 0; i < ModifiedHeight.Length; i++)
                {
                    pkg.Write(ModifiedHeight[i]);
                    if (ModifiedHeight[i])
                    {
                        pkg.Write(LevelDelta[i]);
                        pkg.Write(SmoothDelta[i]);
                    }
                }
                pkg.Write(ModifiedPaint.Length);
                for (int j = 0; j < ModifiedPaint.Length; j++)
                {
                    pkg.Write(ModifiedPaint[j]);
                    if (ModifiedPaint[j])
                    {
                        pkg.Write(Paint[j].r);
                        pkg.Write(Paint[j].g);
                        pkg.Write(Paint[j].b);
                        pkg.Write(Paint[j].a);
                    }
                }
                return Utils.Compress(pkg.GetArray());
            }
        }
    }
}
