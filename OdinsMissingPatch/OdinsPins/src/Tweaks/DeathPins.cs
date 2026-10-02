using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using PinType = Minimap.PinType;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A death pin marks your grave, so it goes when the grave does - emptied by you or by
    /// anyone, or gone some other way - and it is not placed at all when the death left no grave
    /// (nothing on you, KeepGearOnDeath kept it all, or the world keeps your inventory).
    /// <para>
    /// The grave is asked of the world rather than of the looting: an empty grave destroys itself
    /// on whichever machine owns it, which need not be yours, and merely vanishes on the others.
    /// So when you stand near a death pin in a loaded area and none of your graves lies within a
    /// few metres of it on two sweeps in a row, the pin is removed. A grave never strays far: its
    /// owner puts it back within 4m of where it fell (TombStone.PositionCheck).
    /// </para>
    /// </summary>
    internal sealed class DeathPins : Tweak
    {
        internal static readonly DeathPins Instance = new DeathPins();

        private DeathPins() { }

        private const float SweepInterval = 2f;

        /// <summary>
        /// How near a death pin has to be for its grave to be looked for: well inside the loaded
        /// area (at least 64m every way), grave radius included.
        /// </summary>
        private const float CheckRange = 32f;

        /// <summary>How far from its pin a grave may lie (XZ): 4m of drift allowed, doubled.</summary>
        private const float GraveRadius = 8f;

        private ConfigEntry<bool> removeWithGrave;
        private ConfigEntry<bool> onlyWithGrave;

        internal override string Section => "Death Pins";

        protected override string Summary =>
            "A death pin disappears once your grave is gone, whoever emptied it, and a death that " +
            "leaves no grave leaves no pin.";

        protected override void Bind(ConfigFile config)
        {
            removeWithGrave = config.Bind(Section, "RemoveWithGrave", true,
                "Remove a death pin once its grave is gone - emptied by you or anyone else - noticed " +
                "within a few seconds when you are near it. Also clears old death pins whose grave is long gone.");
            onlyWithGrave = config.Bind(Section, "OnlyWithGrave", true,
                "Place no death pin when the death left no grave, because there was nothing to leave behind.");
        }

        // --- graves -------------------------------------------------------------------------

        private static readonly List<TombStone> Graves = new List<TombStone>();

        /// <summary>Death pins found without a grave; two sweeps in a row remove one.</summary>
        private static readonly PinSweep GraveSweep = new PinSweep();

        private static bool GraveNear(Vector3 pos, long owner)
        {
            bool found = false;
            for (int i = Graves.Count - 1; i >= 0; i--)
            {
                TombStone grave = Graves[i];
                if (grave == null)
                {
                    Graves.RemoveAt(i);
                    continue;
                }
                if (!found && grave.m_nview != null && grave.m_nview.IsValid() && grave.GetOwner() == owner
                    && Utils.DistanceXZ(grave.transform.position, pos) < GraveRadius)
                {
                    found = true;
                }
            }
            return found;
        }

        private static void SweepDeathPins(Minimap map, Vector3 origin)
        {
            if (Game.instance == null)
            {
                return;
            }
            long me = Game.instance.GetPlayerProfile().GetPlayerID();
            // The sweep's range is 3D on purpose: a death in a dungeon is pinned 5000m up, where
            // only a player inside that dungeon comes near it - and its grave lies up there too.
            GraveSweep.Run(map, origin, CheckRange,
                pin => pin.m_type == PinType.Death && pin.m_save,
                pos => GraveNear(pos, me),
                pin => map.RemovePin(pin));
        }

        // --- patches ------------------------------------------------------------------------

        private static float nextSweep;

        /// <summary>Set by the local player's grave being set up during their own OnDeath.</summary>
        private static bool graveMade;

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Sweep
        {
            private static void Postfix(Player __instance)
            {
                if (!Instance.On || !Instance.removeWithGrave.Value || __instance != Player.m_localPlayer
                    || Time.time < nextSweep)
                {
                    return;
                }
                nextSweep = Time.time + SweepInterval;
                if (Minimap.instance != null)
                {
                    SweepDeathPins(Minimap.instance, __instance.transform.position);
                }
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Awake))]
        [LoadHook]
        private static class RegisterGrave
        {
            private static void Postfix(TombStone __instance)
            {
                if (__instance.m_nview != null && __instance.m_nview.GetZDO() != null)
                {
                    Graves.Add(__instance);
                }
            }
        }

        /// <summary>
        /// Player.CreateTombStone sets the grave up with the dying player's id, and only when it
        /// makes one at all - which is decided by the inventory, KeepGearOnDeath and the world's
        /// death settings together. So this is the one reliable "a grave was made".
        /// </summary>
        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
        private static class NoteGrave
        {
            private static void Postfix(long ownerUID)
            {
                if (Game.instance != null && ownerUID == Game.instance.GetPlayerProfile().GetPlayerID())
                {
                    graveMade = true;
                }
            }
        }

        /// <summary>
        /// OnDeath makes the grave, then adds the death pin at the player's feet. A death that
        /// made no grave has its pin taken off again right after.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class DeathWithoutGrave
        {
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                {
                    graveMade = false;
                }
            }

            private static void Postfix(Player __instance)
            {
                Minimap map = Minimap.instance;
                if (!Instance.On || !Instance.onlyWithGrave.Value || graveMade || map == null
                    || __instance != Player.m_localPlayer || __instance.m_nview == null || !__instance.m_nview.IsOwner())
                {
                    return;
                }
                Vector3 pos = __instance.transform.position;
                for (int i = map.m_pins.Count - 1; i >= 0; i--)
                {
                    Minimap.PinData pin = map.m_pins[i];
                    if (pin.m_type == PinType.Death && Utils.DistanceXZ(pin.m_pos, pos) < 1f)
                    {
                        map.RemovePin(pin);
                        return;
                    }
                }
            }
        }
    }
}
