using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// One swing of the hammer repairs the whole neighbourhood: every damaged piece within a
    /// configurable radius of the one you aim at, closest first, until the swing runs out of
    /// stamina or the hammer is about to break. The game repairs one piece per click, which after
    /// a raid means clicking your way along every wall you own.
    ///
    /// Nothing about a repair itself changes. Each extra piece is the game's own repair - it costs
    /// the same stamina, eitr and hammer durability, it needs the piece's crafting station in
    /// range just as the game does, it respects wards, and it plays the same effect. Holding the
    /// single repair key while you swing gives you the vanilla one-piece repair back for that
    /// swing.
    /// </summary>
    internal sealed class AreaRepair : Tweak
    {
        internal static readonly AreaRepair Instance = new AreaRepair();

        private AreaRepair() { }

        private ConfigEntry<float> radius;
        private ConfigEntry<KeyCode> singleRepairKey;

        // Which crafting stations are in range, answered once per swing rather than once per
        // piece: a wall of fifty pieces would otherwise walk the station list fifty times.
        private static readonly Dictionary<string, bool> StationsInRange = new Dictionary<string, bool>();

        // The pieces of one swing, closest first. Reused so a swing inside a big base does not
        // hand the collector a new list every click.
        private static readonly List<KeyValuePair<Piece, float>> Nearby = new List<KeyValuePair<Piece, float>>();

        internal override string Section => "Area Repair";

        protected override string Summary =>
            "One swing of the hammer repairs every damaged piece around the one you aim at, " +
            "instead of that one piece alone.";

        protected override void Bind(ConfigFile config)
        {
            radius = config.Bind(Section, "Radius", 10f, new ConfigDescription(
                "Metres around the piece you aim at that one swing reaches. Everything inside it " +
                "is repaired closest first, so a short swing mends what is nearest.",
                new AcceptableValueRange<float>(1f, 64f)));
            singleRepairKey = config.Bind(Section, "SingleRepairKey", KeyCode.LeftAlt,
                "Hold this while you swing to repair only the piece you aim at, the way the game " +
                "does. None to always repair the area.");
        }

        /// <summary>
        /// The pieces within <paramref name="radius"/> of <paramref name="centre"/>, closest
        /// first. The registry is the game's own list of every loaded piece; placement ghosts sit
        /// on the ghost layer and are left out of it, as the game's own radius searches do.
        /// </summary>
        private static List<KeyValuePair<Piece, float>> PiecesInRadius(Vector3 centre, float radius)
        {
            Nearby.Clear();
            foreach (Piece piece in Piece.s_allPieces)
            {
                if (piece == null || piece.gameObject.layer == Piece.s_ghostLayer)
                {
                    continue;
                }
                float distance = Vector3.Distance(centre, piece.transform.position);
                if (distance <= radius)
                {
                    Nearby.Add(new KeyValuePair<Piece, float>(piece, distance));
                }
            }
            Nearby.Sort((a, b) => a.Value.CompareTo(b.Value));
            return Nearby;
        }

        /// <summary>
        /// The game's own two conditions for repairing a piece, asked about a piece the player is
        /// not aiming at: its crafting station has to be within range of the player (unless
        /// building is free) and the ward has to let the player build there.
        /// </summary>
        private static bool CanRepair(Player player, Piece piece, bool stationFree)
        {
            if (!stationFree && piece.m_craftingStation != null)
            {
                string station = piece.m_craftingStation.m_name;
                bool inRange;
                if (!StationsInRange.TryGetValue(station, out inRange))
                {
                    inRange = CraftingStation.HaveBuildStationInRange(station, player.transform.position);
                    StationsInRange[station] = inRange;
                }
                if (!inRange)
                {
                    return false;
                }
            }
            return PrivateArea.CheckAccess(piece.transform.position);
        }

        /// <summary>
        /// The game repairs the piece under the cursor and stops. This postfix carries the same
        /// swing on to everything around it.
        ///
        /// It runs whether or not the game repaired anything: aiming at an undamaged wall of a
        /// damaged house still mends the house, and a swing the game refused - no ward access
        /// where you aimed, a missing station - simply finds nothing it is allowed to repair
        /// either, because every piece is asked the same two questions. The piece the game
        /// already repaired is asked again and turns the swing down itself: a piece at full health,
        /// or one repaired less than a second ago, refuses, so it is never paid for twice.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Repair))]
        private static class RepairEverythingAround
        {
            private static void Postfix(Player __instance, ItemDrop.ItemData toolItem)
            {
                if (!Instance.On || __instance == null || __instance != Player.m_localPlayer)
                {
                    return;
                }
                if (toolItem == null || toolItem.m_shared == null || !__instance.InPlaceMode())
                {
                    return;
                }
                KeyCode single = Instance.singleRepairKey.Value;
                if (single != KeyCode.None && ZInput.GetKey(single, logWarning: false))
                {
                    return;
                }

                // No cost means no gates either, so the loop below runs to the end of the radius.
                bool free = __instance.PlacementCostDisabled;
                bool stationFree = free ||
                    (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench));

                float stamina = __instance.GetBuildStamina();
                float eitr = toolItem.m_shared.m_attack.m_attackEitr;
                float wear = toolItem.m_shared.m_useDurability
                    ? toolItem.m_shared.m_useDurabilityDrain * Game.m_durabilityRate
                    : 0f;
                if (!free && !CanPayFor(__instance, toolItem, stamina, eitr, wear))
                {
                    return;
                }

                Piece aimedAt = __instance.GetHoveringPiece();
                Vector3 centre = aimedAt != null ? aimedAt.transform.position : __instance.transform.position;

                int repaired = 0;
                List<KeyValuePair<Piece, float>> pieces = PiecesInRadius(centre, Instance.radius.Value);
                for (int i = 0; i < pieces.Count; i++)
                {
                    Piece piece = pieces[i].Key;
                    WearNTear damage = piece.GetComponent<WearNTear>();
                    if (damage == null || !CanRepair(__instance, piece, stationFree) || !damage.Repair())
                    {
                        continue;
                    }
                    repaired++;

                    if (!free)
                    {
                        __instance.UseStamina(stamina);
                        __instance.UseEitr(eitr);
                        toolItem.m_durability -= wear;
                    }
                    if (piece.m_placeEffect != null)
                    {
                        piece.m_placeEffect.Create(piece.transform.position, piece.transform.rotation,
                            null, 1f, -1, __instance.GetZDOID());
                    }

                    if (!free && !CanPayFor(__instance, toolItem, stamina, eitr, wear))
                    {
                        break;
                    }
                }
                StationsInRange.Clear();
                Nearby.Clear();

                if (repaired > 0)
                {
                    // The swing the game did not play, for the case where the piece under the
                    // cursor needed nothing and only its neighbours did.
                    __instance.FaceLookDirection();
                    __instance.m_zanim.SetTrigger(toolItem.m_shared.m_attack.m_attackAnimation);
                    __instance.Message(MessageHud.MessageType.TopLeft,
                        Localization.instance.Localize("$msg_repaired", repaired.ToString()));
                }
            }

            /// <summary>Whether one more repair is still paid for - stamina, eitr and a hammer
            /// that survives it. The last one keeps a swing from breaking the tool, which a
            /// single vanilla repair cannot do either.</summary>
            private static bool CanPayFor(Player player, ItemDrop.ItemData toolItem, float stamina, float eitr, float wear)
            {
                return player.HaveStamina(stamina)
                    && (eitr <= 0f || player.HaveEitr(eitr))
                    && toolItem.m_durability >= wear;
            }
        }
    }
}
