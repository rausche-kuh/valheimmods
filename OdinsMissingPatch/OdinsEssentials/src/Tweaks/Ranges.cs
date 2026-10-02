using BepInEx.Configuration;
using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Widens the radii the game keeps small: the circle a crafting station covers and how far
    /// its extensions may sit from it, so a workshop can be a room rather than a huddle around the
    /// workbench; the reach of the comfort of a room; and the circle a wisplight keeps clear of
    /// the Mistlands mist. Each is a multiplier, and 1 leaves that radius vanilla.
    /// </summary>
    internal sealed class Ranges : Tweak
    {
        internal static readonly Ranges Instance = new Ranges();

        private Ranges() { }

        // Below this the ring the area marker draws stops being a ring.
        private const int MinSegments = 3;

        private ConfigEntry<float> buildRange;
        private ConfigEntry<float> extensionRange;
        private ConfigEntry<float> comfortRadius;
        private ConfigEntry<float> mistClearRadius;

        /// <summary>What a station stood at before it was first scaled.</summary>
        private sealed class StationVanilla
        {
            internal float RangeBuild;
            internal float ExtraRangePerLevel;
            // -1 when the station has no area marker circle.
            internal int Segments;
        }

        // The vanilla values of everything that has been scaled, so a rescale can start from them
        // instead of from whatever the fields hold now. Weak keys: an entry dies with its object,
        // and nothing here has to know when that happens.
        private readonly ConditionalWeakTable<CraftingStation, StationVanilla> vanillaStations =
            new ConditionalWeakTable<CraftingStation, StationVanilla>();
        private readonly ConditionalWeakTable<StationExtension, StrongBox<float>> vanillaExtensions =
            new ConditionalWeakTable<StationExtension, StrongBox<float>>();
        private readonly ConditionalWeakTable<Demister, StrongBox<float>> vanillaDemisters =
            new ConditionalWeakTable<Demister, StrongBox<float>>();

        internal override string Section => "Ranges";

        protected override string Summary =>
            "Widen the area a crafting station covers, how far its extensions may stand from it, " +
            "the radius comfort is counted in and the circle kept clear of the Mistlands mist.";

        protected override void Bind(ConfigFile config)
        {
            buildRange = BindMultiplier(config, "BuildRangeMultiplier", 2f,
                "Multiplier on the radius in which a station lets you build, craft and repair. " +
                "1 is vanilla - 10m for the workbench, 20m for the forge.");
            extensionRange = BindMultiplier(config, "ExtensionRangeMultiplier", 2f,
                "Multiplier on how far an extension (the forge cooler, the tanning rack, ...) may " +
                "stand from its station and still raise its level. 1 is vanilla, usually 5m.");
            comfortRadius = BindMultiplier(config, "ComfortRadiusMultiplier", 2f,
                "Multiplier on the radius around you in which comfort furniture counts towards " +
                "Rested. 1 is vanilla (10m).");
            mistClearRadius = BindMultiplier(config, "MistClearRadiusMultiplier", 2f,
                "Multiplier on the radius a wisplight, a wisp torch and everything else that " +
                "clears the Mistlands mist keeps clear. 1 is vanilla.");

            OnSettingChanged(config, Rescale);
        }

        /// <summary>1 while the tweak is off, so callers can multiply either way.</summary>
        private float BuildScale => On ? buildRange.Value : 1f;

        private float ExtensionScale => On ? extensionRange.Value : 1f;

        private float MistClearScale => On ? mistClearRadius.Value : 1f;

        /// <summary>
        /// Objects are scaled as they load, so a setting changed mid game has to be carried to the
        /// ones already there. The game's own registries hold exactly those - anything not in them
        /// is not in the world. A demister that is off at the time is caught up by the OnEnable
        /// patch when it comes back.
        /// </summary>
        private void Rescale()
        {
            foreach (CraftingStation station in CraftingStation.m_allStations)
            {
                Apply(station);
            }
            foreach (StationExtension ext in StationExtension.m_allExtensions)
            {
                Apply(ext);
            }
            foreach (Demister demister in Demister.GetDemisters())
            {
                Apply(demister);
            }
        }

        /// <summary>
        /// The station's build range, and the ring the area marker draws it with. Everything the
        /// game derives from m_rangeBuild follows it: the build check, the circle's radius and the
        /// station's effect area collider.
        ///
        /// The number of markers the ring is laid out with is whatever the prefab set and is never
        /// touched by the game - so a widened circle would be drawn by the same handful of
        /// markers, stretched into a dotted line. Scaling the count alongside the radius keeps
        /// the ring as dense as it looks in vanilla, whatever density the prefab chose.
        ///
        /// Set from the remembered vanilla values, so applying it twice is harmless.
        /// </summary>
        private void Apply(CraftingStation station)
        {
            if (station == null)
            {
                return;
            }
            // The first time a station is seen its values are still the prefab's.
            StationVanilla v = vanillaStations.GetValue(station, s => new StationVanilla
            {
                RangeBuild = s.m_rangeBuild,
                ExtraRangePerLevel = s.m_extraRangePerLevel,
                Segments = s.m_areaMarkerCircle != null ? s.m_areaMarkerCircle.m_nrOfSegments : -1,
            });
            float scale = BuildScale;
            station.m_rangeBuild = v.RangeBuild * scale;
            station.m_extraRangePerLevel = v.ExtraRangePerLevel * scale;

            CircleProjector circle = station.m_areaMarkerCircle;
            if (circle != null && v.Segments >= 0)
            {
                circle.m_nrOfSegments = Mathf.Max(MinSegments, Mathf.RoundToInt(v.Segments * scale));
            }
        }

        private void Apply(StationExtension ext)
        {
            if (ext == null)
            {
                return;
            }
            StrongBox<float> distance = vanillaExtensions.GetValue(ext,
                e => new StrongBox<float>(e.m_maxStationDistance));
            ext.m_maxStationDistance = distance.Value * ExtensionScale;
        }

        /// <summary>
        /// The radius is the end range of the demister's particle force field: the field itself
        /// pushes the mist particles out to it, and ParticleMist reads the same value to decide
        /// where mist may be emitted and whether a point counts as inside a demister.
        /// </summary>
        private void Apply(Demister demister)
        {
            if (demister == null || demister.m_forceField == null)
            {
                return;
            }
            StrongBox<float> range = vanillaDemisters.GetValue(demister,
                d => new StrongBox<float>(d.m_forceField.endRange));
            demister.m_forceField.endRange = range.Value * MistClearScale;
        }

        /// <summary>
        /// m_rangeBuild is an instance field copied off the prefab, so scaling it here is per
        /// station and never touches the prefab.
        /// </summary>
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Start))]
        private static class ScaleStation
        {
            private static void Postfix(CraftingStation __instance)
            {
                Instance.Apply(__instance);
            }
        }

        /// <summary>
        /// The same condition the game's own Awake registers an extension under. Anything failing
        /// it - a placement ghost - is not in m_allExtensions, so a rescale could never reach it
        /// again; leaving it vanilla keeps "scaled" and "rescalable" the same set. It is also not
        /// in the list the game searches, so its range is never read anyway.
        /// </summary>
        [HarmonyPatch(typeof(StationExtension), nameof(StationExtension.Awake))]
        private static class ScaleExtension
        {
            private static void Postfix(StationExtension __instance)
            {
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview != null && nview.GetZDO() != null)
                {
                    Instance.Apply(__instance);
                }
            }
        }

        /// <summary>
        /// OnEnable is where the game registers a demister, and it runs after Awake has found
        /// the force field. Patching it rather than Awake also covers a demister the game
        /// switches off and on again - the wisp fountain does that with its nearby-wisps object -
        /// which would otherwise come back at whatever scale it was switched off with.
        /// </summary>
        [HarmonyPatch(typeof(Demister), nameof(Demister.OnEnable))]
        private static class ScaleDemister
        {
            private static void Postfix(Demister __instance)
            {
                Instance.Apply(__instance);
            }
        }

        /// <summary>
        /// The comfort radius is a literal inside SE_Rested, but it reaches the pieces through this
        /// one call, so widening the argument is the whole change - nothing is written to game
        /// state, so there is nothing to restore and nothing to rescale when the setting changes.
        /// </summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.GetAllComfortPiecesInRadius))]
        private static class WidenComfortRadius
        {
            private static void Prefix(ref float radius)
            {
                if (Instance.On)
                {
                    radius *= Instance.comfortRadius.Value;
                }
            }
        }
    }
}
