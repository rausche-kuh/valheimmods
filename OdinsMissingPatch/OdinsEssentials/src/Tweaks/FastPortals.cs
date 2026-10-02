using BepInEx.Configuration;
using HarmonyLib;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Takes the padding out of a portal trip. The game waits a flat two seconds before it moves
    /// you and a flat eight before it lets you go, whether or not the other side is ready; this
    /// moves you the moment the screen is black and lets you go the moment the other side is
    /// loaded and has ground to stand on. The fade to black gets shorter too.
    ///
    /// The one wait the game needs is kept as it is: a distant teleport still waits for the
    /// target area to load, and still gives it the same seven seconds to produce a floor before
    /// it drops you on the terrain height instead.
    ///
    /// A dungeon door is the same trip with distantTeleport off, and with InstantDungeonDoors
    /// it has no black screen at all when the inside is already loaded: the screen never starts
    /// to darken and the move happens on the next physics step. Everything else the door does
    /// is left alone - the location name it shows and the stat it counts run in Teleport.Interact
    /// after TeleportTo returns, before the player has moved, and the instant environment switch
    /// is part of the move itself.
    /// </summary>
    internal sealed class FastPortals : Tweak
    {
        internal static readonly FastPortals Instance = new FastPortals();

        private FastPortals() { }

        /// <summary>
        /// The game's minimum trip time: the timer has to pass it before the floor is looked for.
        /// Also the moment the "no floor" fallback starts counting from, which is why the timer
        /// is jumped to exactly this and not past it.
        /// </summary>
        private const float VanillaMinimumTrip = 8f;

        /// <summary>
        /// The timer UpdateTeleport has to pass before it moves you - the whole wait of a dungeon
        /// door, which never has the eight second minimum.
        /// </summary>
        private const float VanillaMoveDelay = 2f;

        private ConfigEntry<float> fadeSeconds;
        private ConfigEntry<bool> instantDungeonDoors;

        internal override string Section => "Fast Portals";

        protected override string Summary =>
            "Go through a portal as soon as the screen is black and the other side has loaded, " +
            "instead of after the game's fixed eight second wait.";

        protected override void Bind(ConfigFile config)
        {
            fadeSeconds = config.Bind(Section, "FadeSeconds", 0.5f, new ConfigDescription(
                "Seconds the screen takes to fade to black when you step into a portal, and to " +
                "fade back in on the other side. 1 is vanilla.",
                new AcceptableValueRange<float>(0f, 5f)));
            instantDungeonDoors = config.Bind(Section, "InstantDungeonDoors", true,
                "Going through a dungeon or cave entrance is instant, without the black screen, " +
                "whenever the inside is already loaded (it nearly always is). Off means the door " +
                "fades like a portal, over FadeSeconds.");
        }

        /// <summary>
        /// A dungeon door trip that can finish right now: the local teleport a Teleport starts
        /// (not a portal's distant one) into a zone that is loaded with every object in it
        /// spawned, with the floor under the exit already there. The floor is checked on its own
        /// because the area being ready does not guarantee it on the first frame, and a local
        /// trip that moves you onto no floor is sent straight back ("portal blocked").
        /// </summary>
        private static bool IsInstantDoorTrip(Player player)
        {
            return Instance.On && Instance.instantDungeonDoors.Value
                && IsDoorTrip(player)
                && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(player.m_teleportTargetPos)
                && HasFloor(player);
        }

        private static bool IsDoorTrip(Player player)
        {
            return player.m_teleporting && !player.m_distantTeleport;
        }

        /// <summary>The same raycast UpdateTeleport ends a trip on.</summary>
        private static bool HasFloor(Player player)
        {
            return ZoneSystem.instance != null && ZoneSystem.instance.FindFloor(player.m_teleportTargetPos, out _);
        }

        /// <summary>
        /// UpdateTeleport counts a timer and gates on it twice: past 2s you are moved, past 8s
        /// (and once the area is loaded) you are released. Both gates sit on the same timer, so
        /// once the screen is fully black the timer is set to the second gate and the rest of the
        /// method runs as vanilla from there - the area check, the floor check and the 15s
        /// fallback all keep their meaning, and the fallback still gets its seven seconds. Reading
        /// the loading screen's alpha instead of a clock means the jump never happens on a visible
        /// screen, whatever the fade takes. Owner only, so this is the local player.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateTeleport))]
        private static class SkipTheWait
        {
            private static bool waitedForFloor;

            private static void Prefix(Player __instance)
            {
                if (!__instance.m_teleporting)
                {
                    waitedForFloor = false;
                }
                if (!Instance.On || !__instance.m_teleporting || __instance.m_teleportTimer >= VanillaMinimumTrip)
                {
                    return;
                }
                if (IsInstantDoorTrip(__instance))
                {
                    if (waitedForFloor)
                    {
                        TweakHost.Log.LogInfo(Instance.Section + ": the floor inside the door appeared after " +
                            __instance.m_teleportTimer.ToString("0.00") + "s");
                        waitedForFloor = false;
                    }
                    // The method adds this frame's dt before it compares, so this moves you and
                    // then finds the floor in the same call, exactly as vanilla does at two
                    // seconds.
                    if (__instance.m_teleportTimer < VanillaMoveDelay)
                    {
                        __instance.m_teleportTimer = VanillaMoveDelay;
                    }
                    return;
                }
                if (IsDoorTrip(__instance) && !HasFloor(__instance))
                {
                    // Never hurry a door trip onto a missing floor: that is an instant "portal
                    // blocked". Vanilla's two seconds run on until the floor turns up.
                    waitedForFloor = true;
                    return;
                }
                Hud hud = Hud.instance;
                if (hud == null || hud.m_loadingScreen == null || hud.m_loadingScreen.alpha < 1f)
                {
                    return;
                }
                __instance.m_teleportTimer = VanillaMinimumTrip;
            }
        }

        /// <summary>
        /// The black screen fades in and out at Hud.GetFadeDuration, which is a literal 1s unless
        /// you are dead or asleep. The fade in is easy to spot (the player is teleporting); the
        /// fade out on the other side is not, since the teleport is over by then, so the patch
        /// remembers that the screen went up for a teleport until it is down again or something
        /// else (death, sleep) has claimed it.
        /// </summary>
        [HarmonyPatch(typeof(Hud), nameof(Hud.GetFadeDuration))]
        private static class FadeFaster
        {
            private static bool fadingForTeleport;

            private static void Postfix(Hud __instance, Player player, ref float __result)
            {
                if (player == null)
                {
                    return;
                }
                if (player.IsTeleporting())
                {
                    fadingForTeleport = true;
                }
                else if (player.IsDead() || player.IsSleeping()
                    || __instance.m_loadingScreen == null || __instance.m_loadingScreen.alpha <= 0f)
                {
                    fadingForTeleport = false;
                }
                if (IsInstantDoorTrip(player))
                {
                    // The fade moves alpha by dt / duration, so this keeps the screen clear for
                    // the frame or two before the next physics step ends the trip; on arrival
                    // there is nothing to fade back out.
                    __result = float.MaxValue;
                }
                else if (Instance.On && fadingForTeleport)
                {
                    __result = Instance.fadeSeconds.Value;
                }
            }
        }
    }
}
