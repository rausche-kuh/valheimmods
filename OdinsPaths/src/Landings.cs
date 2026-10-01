using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// Where a trail leaves the land for the open sea, and where it comes ashore again, on its
    /// last dry point. Fords and rivers get none: only a stretch of water with sea in it
    /// (<see cref="PathSearch.IsSea"/>: the Ocean biome, or deeper than a river anywhere - a
    /// fjord) is a crossing, and one shorter than a swim gets none either; water longer than
    /// <see cref="LongCrossing"/> is one however shallow.
    ///
    /// A main road's landing is a harbour: a vegvisir with blue runes that pins the harbours
    /// across (<see cref="Harbours"/>). A side path's is a minor harbour, a tall log post
    /// (<see cref="PostPrefab"/>) and no stone; a landing that already has something built on it
    /// (an earlier path's post, a player's dock) gets no second post. Both are structures, so
    /// later paths walk around them. Server only.
    /// </summary>
    internal static class Landings
    {
        /// <summary>What main roads' landings were marked with before the harbour stones; only a dev reset still looks for it.</summary>
        private const string BarrelPrefab = "piece_chest_barrel";
        /// <summary>A minor harbour's mark: the 4 m log pole players build, standing like a mooring post.</summary>
        private const string PostPrefab = "wood_pole_log_4";
        /// <summary>A structure this close to a landing already marks it.</summary>
        private const float Taken = 4f;
        /// <summary>Water shorter than this is swum, not boated: no harbour on a small river.</summary>
        private const float MinCrossing = 40f;
        /// <summary>Water this long needs a boat however shallow it is: nobody swims it, and no river is this wide.</summary>
        private const float LongCrossing = 100f;

        /// <summary>A landing: the trail's last dry point before a sea crossing or its first after one, and the point it looks toward.</summary>
        internal struct Landing
        {
            public int Shore;
            public int Toward;
            /// <summary>Which crossing of the trail it is a shore of: the two shores of one share it.</summary>
            public int Crossing;
        }

        /// <summary>
        /// The harbour stones or posts placed, added to placed as their ZDOs; fork is where a main
        /// road sets out from the network. The paths to the doors of a harbour's buildings are added to
        /// doorPaths, for the caller to write after the road.
        /// </summary>
        public static IEnumerator Place(Trail trail, Structures structures, List<ZDOID> placed, Vector2? fork = null, List<Buildings.DoorPath> doorPaths = null)
        {
            if (trail.Kind == RoadKind.Main)
            {
                yield return Harbours.Place(trail, Find(trail), structures, fork, doorPaths, placed);
                yield break;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(PostPrefab);
            if (prefab == null)
            {
                Debug.LogWarning("[OdinsPaths] No " + PostPrefab + " prefab - landings left unmarked.");
                yield break;
            }
            foreach (Landing landing in Find(trail))
            {
                Mark(trail, landing.Shore, landing.Toward, prefab, structures, placed);
            }
        }

        /// <summary>Where the trail's landings are, in order; nothing placed.</summary>
        public static List<Landing> Find(Trail trail)
        {
            List<Landing> landings = new List<Landing>();
            float waterLevel = ZoneSystem.instance.m_waterLevel;
            int crossing = 0;
            int i = 0;
            while (i < trail.Points.Count)
            {
                if (!trail.Water[i])
                {
                    i++;
                    continue;
                }
                int first = i;
                bool sea = false;
                while (i < trail.Points.Count && trail.Water[i])
                {
                    sea |= PathSearch.IsSea(waterLevel - trail.Ground[i], WorldGenerator.instance.GetBiome(trail.Points[i].x, trail.Points[i].y));
                    i++;
                }
                float length = (i - first) * Trail.Spacing;
                if ((!sea || length < MinCrossing) && length < LongCrossing)
                {
                    continue;
                }
                // The shore before the crossing looks out over it, the one after looks back.
                if (first > 0)
                {
                    landings.Add(new Landing { Shore = first - 1, Toward = first, Crossing = crossing });
                }
                if (i < trail.Points.Count)
                {
                    landings.Add(new Landing { Shore = i, Toward = i - 1, Crossing = crossing });
                }
                crossing++;
            }
            return landings;
        }

        private static void Mark(Trail trail, int shore, int toward, GameObject prefab, Structures structures, List<ZDOID> placed)
        {
            Vector2 at = trail.Points[shore];
            if (structures.Distance(at, Taken) < Taken)
            {
                return;
            }
            Vector2 look = trail.Points[toward] - at;
            Quaternion rotation = look.sqrMagnitude > 0f
                ? Quaternion.LookRotation(new Vector3(look.x, 0f, look.y)) : Quaternion.identity;
            Vector3 position = new Vector3(at.x, Height(trail, shore), at.y);
            ZDO zdo = Spawn(prefab, position, rotation);
            structures.Add(at);
            placed.Add(zdo.m_uid);
        }

        /// <summary>
        /// Set on everything the mod places - harbour stones, posts, lamps -, so that a dev reset
        /// finds it among the game's own: the Mistlands' road posts are the same prefabs.
        /// </summary>
        internal static readonly int PlacedKey = "OdinsPaths_Placed".GetStableHashCode();
        /// <summary>The prefabs the mod places (and the barrels it once did), for a dev reset of what was placed before it was marked.</summary>
        internal static readonly string[] Placed = { Harbours.PrefabName, BarrelPrefab, PostPrefab, Lamps.LampPrefab, Lamps.PostPrefab };

        /// <summary>
        /// A prefab's ZDO as <c>ZNetView.Awake</c> would make it, whether its zone is loaded or not;
        /// no creator, so nobody's work, and marked as the mod's (<see cref="PlacedKey"/>).
        /// </summary>
        internal static ZDO Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            int hash = prefab.name.GetStableHashCode();
            ZNetView view = prefab.GetComponent<ZNetView>();
            ZDO zdo = ZDOMan.instance.CreateNewZDO(position, hash);
            zdo.Persistent = view.m_persistent;
            zdo.Type = view.m_type;
            zdo.Distant = view.m_distant;
            zdo.SetPrefab(hash);
            zdo.SetRotation(rotation);
            zdo.Set(PlacedKey, true);
            return zdo;
        }

        /// <summary>The ground as the game builds it, moved as the terrain writer's levelling moves it.</summary>
        internal static float Height(Trail trail, int index)
        {
            float ground = trail.Ground[index];
            if (trail.Kind.Levelling && ground >= ZoneSystem.instance.m_waterLevel + Trail.ShoreMargin + 0.2f)
            {
                float maxCut = trail.MaxCut[index];
                ground += Mathf.Clamp(trail.Profile[index] - ground, -maxCut, maxCut);
            }
            return ground;
        }
    }
}
