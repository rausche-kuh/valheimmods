using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Doors are no longer opened. They are kicked. The game already has a kick - the bare handed
    /// secondary attack - and it already knows when that kick connects with something; all this
    /// does is notice when the something was a door, and answer with a bang and a door that bursts
    /// open several times faster than it has any right to.
    ///
    /// Nothing about the door itself changes: it is opened through the game's own UseDoor RPC, so
    /// unmodded clients and the server see an ordinary door swing. Only the noise and the speed
    /// are the mod's, and those ride on a routed RPC of their own that unmodded clients ignore.
    /// </summary>
    internal sealed class DoorKick : Tweak
    {
        internal static readonly DoorKick Instance = new DoorKick();

        private DoorKick() { }

        internal override string Section => "Kick";

        protected override string Summary =>
            "The secondary attack, aimed at a shut door, kicks it open with a bang, whatever you hold.";

        /// <summary>Broadcast to every modded client: the bang and the fast swing, per door.</summary>
        private const string KickRpc = "ThisIsValheim_Kick";

        /// <summary>
        /// How long the door's animator is left running fast. The open animation is well under a
        /// second even at normal speed; the window only has to outlast the trip the UseDoor RPC
        /// makes to the door's owner and back, which is why it is this generous.
        /// </summary>
        private const float SwingWindow = 1.5f;

        /// <summary>
        /// How long a door is deaf to further kicks. One swing of the foot sweeps several rays and
        /// can land on the same door more than once, and the door's state takes a round trip to its
        /// owner to come back - without this the second hit would read the door as still shut and
        /// slam it closed again.
        /// </summary>
        private const float KickCooldown = 0.6f;

        /// <summary>
        /// How close to the other half of a double door a kick has to land to take it along.
        /// Without this the kick has to find the seam exactly to open both.
        /// </summary>
        private const float SeamReach = 0.6f;

        /// <summary>The bang goes off at boot height rather than at the door's foot.</summary>
        private const float SoundHeight = 1f;

        /// <summary>
        /// How long a spawned effect is given before it is cleaned up by hand. Most of the game's
        /// effects carry a TimedDestruction and tidy themselves; the odd one is a bare sound meant
        /// to live on a machine that outlives it, and those would otherwise stay forever.
        /// </summary>
        private const float EffectLifetime = 6f;

        /// <summary>
        /// The default kick: a siege ram's piston landing on a gate, the splintering of a broken
        /// building piece, the sawdust that comes off a chopped log, and the thump on the camera
        /// every weapon hit in the game uses. Nothing here is shipped with the mod.
        /// </summary>
        private const string DefaultEffects =
            "sfx_battering_ram_impact,sfx_wood_break,vfx_SawDust,fx_hit_camshake";

        /// <summary>The line the door's hover text gains while a kick would open it.</summary>
        private const string KickHint = "\n[<color=yellow><b>$KEY_SecondaryAttack</b></color>] Kick";

        private static ZRoutedRpc registeredOn;

        /// <summary>
        /// Set for the length of one StartAttack call while a secondary attack aimed at a door is
        /// being turned into a kick - see <see cref="KickAtDoor"/>.
        /// </summary>
        private static bool forceUnarmed;

        /// <summary>The configured effects, resolved on every world load and on every config change.</summary>
        private static EffectList effects;

        /// <summary>One running swing per door, so a second kick does not cut the first one short.</summary>
        private static readonly Dictionary<Door, Coroutine> swinging = new Dictionary<Door, Coroutine>();

        /// <summary>When each door was last kicked - see <see cref="KickCooldown"/>.</summary>
        private static readonly Dictionary<Door, float> kicked = new Dictionary<Door, float>();

        // ---- Config --------------------------------------------------------------------------

        private static ConfigEntry<float> swingSpeed;
        private static ConfigEntry<bool> lockedDoors;
        private static ConfigEntry<bool> showHint;
        private static ConfigEntry<string> effectPrefabs;

        /// <summary>Why a kick does not open a door. Only a ward or a lock throws the kicker back.</summary>
        private enum Refusal { None, Busy, Ward, Key }

        protected override void Bind(ConfigFile config)
        {
            swingSpeed = config.Bind(Section, "SwingSpeed", 4f, new ConfigDescription(
                "How many times faster than normal a kicked door swings open.",
                new AcceptableValueRange<float>(1f, 10f)));
            lockedDoors = config.Bind(Section, "LockedDoors", false,
                "Whether a door that wants a key can be kicked open without it. Off keeps the " +
                "crypts shut until you have found the key, the way the game intends - carry the " +
                "key and the kick opens the door with it.");
            showHint = config.Bind(Section, "ShowHint", true,
                "Whether a door you could kick open says so when you look at it.");
            effectPrefabs = config.Bind("Effects", "Prefabs", DefaultEffects,
                "The game's own effect prefabs that go off at the door, by name, separated by " +
                "commas. Drop one to lose that layer, or put fx_GP_Activation in for the sound a " +
                "Forsaken power makes when you call on it. An empty list makes the kick silent.");
            effectPrefabs.SettingChanged += (sender, args) => ResolveEffects();
        }

        /// <summary>
        /// Looks the effects up while the world is still loading, rather than in the middle of the
        /// first kick - the fallback search walks every object Unity has in memory.
        /// </summary>
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ResolveOnLoad
        {
            private static void Postfix()
            {
                ResolveEffects();
            }
        }

        /// <summary>ZNet builds a fresh ZRoutedRpc per session, so re-register once per game.</summary>
        [HarmonyPatch(typeof(Game), "Start")]
        private static class RegisterRpc
        {
            private static void Postfix()
            {
                ZRoutedRpc rpc = ZRoutedRpc.instance;
                if (rpc == null || rpc == registeredOn)
                {
                    return;
                }
                registeredOn = rpc;
                rpc.Register<ZDOID>(KickRpc, RPC_Kick);
            }
        }

        // ---- Noticing the kick ---------------------------------------------------------------

        /// <summary>
        /// Every object a melee swing touches passes through here, before the game has decided
        /// whether it is something that can be damaged - which is exactly the question "would this
        /// kick hit a door", asked by the game itself with the game's own reach and aim. A door
        /// that cannot be broken, like the ones in the dungeons, still comes through.
        /// </summary>
        [HarmonyPatch(typeof(Attack), "AddHitPoint")]
        private static class NoticeKick
        {
            private static void Postfix(Attack __instance, GameObject go, Vector3 point)
            {
                try
                {
                    Player player = Player.m_localPlayer;
                    if (!Instance.On || go == null || player == null || __instance.m_character != player
                        || !IsKick(__instance.m_attackAnimation))
                    {
                        return;
                    }
                    Door door = go.GetComponentInParent<Door>();
                    if (door != null && TryKick(door, player))
                    {
                        KickNeighbours(door, player, point);
                    }
                }
                catch (Exception e)
                {
                    TweakHost.Log.LogWarning("kick check failed: " + e);
                }
            }
        }

        /// <summary>
        /// Turns the secondary attack into the kick while the player is looking at a shut door,
        /// whatever is in their hands - a door gets the boot, not the weapon's special move.
        /// StartAttack takes its attack from GetCurrentWeapon, so for the length of this one call
        /// that answers with the bare hands; everything after (the reach, the stamina, the
        /// animation, the hit that NoticeKick sees) is then the game's own unarmed kick.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class KickAtDoor
        {
            private static void Prefix(Humanoid __instance, bool secondaryAttack)
            {
                forceUnarmed = false;
                Player player = __instance as Player;
                if (!Instance.On || !secondaryAttack || player == null || player != Player.m_localPlayer
                    || player.m_unarmedWeapon == null)
                {
                    return;
                }
                GameObject hovering = player.GetHoverObject();
                Door door = hovering != null ? hovering.GetComponentInParent<Door>() : null;
                // The same doors a bare handed kick acts on: one that would open, and one whose
                // ward or lock throws the kicker back. An open or swinging door is left alone.
                forceUnarmed = door != null && Check(door, player) != Refusal.Busy;
            }

            private static void Finalizer()
            {
                forceUnarmed = false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetCurrentWeapon))]
        private static class KickWeapon
        {
            private static void Postfix(Humanoid __instance, ref ItemDrop.ItemData __result)
            {
                if (forceUnarmed && __instance == Player.m_localPlayer && __instance.m_unarmedWeapon != null)
                {
                    __result = __instance.m_unarmedWeapon.m_itemData;
                }
            }
        }

        /// <summary>
        /// Whether an attack is a kick. The bare handed secondary attack is <c>unarmed_kick</c>;
        /// matching the word rather than the whole name means anything else the game or another
        /// mod calls a kick counts too, and a punch or a sword swing never does.
        /// </summary>
        private static bool IsKick(string animation)
        {
            return !string.IsNullOrEmpty(animation)
                && animation.IndexOf("kick", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- The kick ------------------------------------------------------------------------

        /// <summary>
        /// Every reason the game would have refused the door, asked again, because the kick opens
        /// it through <c>Door.Open</c> directly and that call asks nothing: UseDoor flips the state
        /// for whoever sends it, key or no key.
        /// </summary>
        private static Refusal Check(Door door, Player player)
        {
            ZNetView nview = door.m_nview;
            // Only a shut door gets kicked. A door standing open is closed the ordinary way, and
            // one still swinging is left to finish - CanInteract is the game's own version of that.
            if (nview == null || !nview.IsValid() || door.m_animator == null
                || nview.GetZDO().GetInt(ZDOVars.s_state) != 0 || !door.CanInteract())
            {
                return Refusal.Busy;
            }
            // A ward is a ward, and no kick is hard enough. It is not flashed here: the kick that
            // got us this far is a hit like any other, and the game flashes the ward for that.
            if (door.m_checkGuardStone
                && !PrivateArea.CheckAccess(door.transform.position, 0f, flash: false))
            {
                return Refusal.Ward;
            }
            // A kicker with the key gets in the way a hand on the handle would, key and all.
            if (NeedsKey(door) && !door.HaveKey(player))
            {
                return Refusal.Key;
            }
            return Refusal.None;
        }

        /// <summary>Whether the kick has to go through this door's lock, see <c>LockedDoors</c>.</summary>
        private static bool NeedsKey(Door door)
        {
            return door.m_keyItem != null && !lockedDoors.Value;
        }

        /// <summary>
        /// Takes the other half of a double door along: any door with a collider within
        /// <see cref="SeamReach"/> of where the kick landed. Only called after the first door
        /// opened, so a locked pair rebuffs once, not twice.
        /// </summary>
        private static void KickNeighbours(Door door, Player player, Vector3 point)
        {
            foreach (Collider collider in Physics.OverlapSphere(point, SeamReach))
            {
                Door other = collider.GetComponentInParent<Door>();
                if (other != null && other != door)
                {
                    TryKick(other, player);
                }
            }
        }

        /// <summary>
        /// Boots the door open, or bounces the kicker off one that will not give. True only when
        /// the door was actually opened.
        /// </summary>
        private static bool TryKick(Door door, Player player)
        {
            if (Recent(door))
            {
                return false;
            }
            Refusal refusal = Check(door, player);
            if (refusal == Refusal.Busy)
            {
                return false;
            }
            kicked[door] = Time.time;
            Vector3 away = (player.transform.position - door.transform.position).normalized;
            if (refusal != Refusal.None)
            {
                Rebuff(door, player, away, refusal);
                return false;
            }
            // The key is used the way Door.Interact uses it: named on screen, and gone if the
            // door eats its key. With LockedDoors on the kick needed no key, so none is spent.
            if (NeedsKey(door))
            {
                string key = door.m_keyItem.m_itemData.m_shared.m_name;
                if (door.m_consumeKey)
                {
                    player.GetInventory().RemoveItem(key, 1);
                }
                player.Message(MessageHud.MessageType.Center,
                    Localization.instance.Localize("$msg_door_usingkey", key));
            }
            // The show is everyone's; the door itself is opened by the game, through its own RPC,
            // so a client without the mod still sees it swing - just quietly and at walking pace.
            // Everybody includes us, and InvokeRoutedRPC runs it here and now, so the bang lands
            // on the kicker's own screen in the same frame as the kick.
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, KickRpc, door.m_nview.GetZDO().m_uid);
            }
            door.Open(away);
            if (Game.instance != null)
            {
                Game.instance.IncrementPlayerStat(PlayerStatType.DoorsOpened);
            }
            return true;
        }

        /// <summary>
        /// A warded or locked door does not move, the kicker does: the game's own locked rattle,
        /// the game's own reason on screen, and a stagger back from the door. The stagger faces
        /// the kicker at the door and rides the player's own sync, so everyone sees it.
        /// </summary>
        private static void Rebuff(Door door, Player player, Vector3 away, Refusal refusal)
        {
            door.m_lockedEffects.Create(door.transform.position, door.transform.rotation);
            player.Message(MessageHud.MessageType.Center, refusal == Refusal.Key
                ? Localization.instance.Localize("$msg_door_needkey", door.m_keyItem.m_itemData.m_shared.m_name)
                : Localization.instance.Localize("$piece_noaccess"));
            player.Stagger(away);
        }

        /// <summary>Whether this door has been kicked too recently to be kicked again.</summary>
        private static bool Recent(Door door)
        {
            float when;
            if (kicked.TryGetValue(door, out when) && Time.time - when < KickCooldown)
            {
                return true;
            }
            // Doors are kicked one at a time; with this many in here, everything but the last
            // one or two is a door the player walked away from or the world has unloaded.
            if (kicked.Count > 8)
            {
                kicked.Clear();
            }
            return false;
        }

        /// <summary>
        /// Tells a player looking at a shut door that it can be kicked - with anything in their
        /// hands, since <see cref="KickAtDoor"/> kicks regardless. Only a door the kick would
        /// actually open says so - a locked or warded one keeps the game's text.
        /// </summary>
        [HarmonyPatch(typeof(Door), nameof(Door.GetHoverText))]
        private static class ShowKickHint
        {
            private static void Postfix(Door __instance, ref string __result)
            {
                Player player = Player.m_localPlayer;
                if (!Instance.On || !showHint.Value || string.IsNullOrEmpty(__result) || player == null
                    || player.m_unarmedWeapon == null
                    || Check(__instance, player) != Refusal.None)
                {
                    return;
                }
                __result += Localization.instance.Localize(KickHint);
            }
        }

        /// <summary>
        /// The flourish, run the same way on every modded client: the bang at the door and the
        /// door's animator wound up for as long as the swing takes. Purely local to each client -
        /// nothing here is networked, so nobody hears the same kick twice.
        /// </summary>
        private static void RPC_Kick(long sender, ZDOID id)
        {
            // A dedicated server has nobody watching and no door instances to speak of.
            if (!Instance.On || (ZNet.instance != null && ZNet.instance.IsDedicated()))
            {
                return;
            }
            try
            {
                if (ZNetScene.instance == null)
                {
                    return;
                }
                GameObject go = ZNetScene.instance.FindInstance(id);
                Door door = go != null ? go.GetComponent<Door>() : null;
                // The door is out of this client's loaded zones - there is nothing to see here.
                if (door == null)
                {
                    return;
                }
                // The swing first: a bang that fails must not leave the door at walking pace.
                Swing(door);
                Bang(door.transform.position + Vector3.up * SoundHeight);
            }
            catch (Exception e)
            {
                TweakHost.Log.LogWarning("kick effects failed: " + e);
            }
        }

        // ---- The bang ------------------------------------------------------------------------

        /// <summary>Sets the configured effects off in the door's face.</summary>
        private static void Bang(Vector3 pos)
        {
            EffectList list = effects;
            if (list == null)
            {
                return;
            }
            // Every client spawns its own copy off the one RPC. m_forceDisableInit is the game's
            // own way of saying "no ZDO for this one" - the ZNetView, if the effect has one,
            // destroys itself in Awake, so this copy never leaves the client that made it.
            bool wasDisabled = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            GameObject[] spawned;
            try
            {
                spawned = list.Create(pos, Quaternion.identity);
            }
            finally
            {
                ZNetView.m_forceDisableInit = wasDisabled;
            }
            Sweep(spawned);
        }

        /// <summary>
        /// Puts a timer on anything that did not come with one. An effect prefab that is normally
        /// a child of some machine has no TimedDestruction of its own, because the machine is what
        /// eventually goes away - spawned loose like this it would sit at the door forever.
        /// </summary>
        private static void Sweep(GameObject[] spawned)
        {
            foreach (GameObject go in spawned)
            {
                if (go != null && go.GetComponentInChildren<TimedDestruction>(includeInactive: true) == null)
                {
                    UnityEngine.Object.Destroy(go, EffectLifetime);
                }
            }
        }

        /// <summary>
        /// Looks the configured effect prefabs up and keeps them in <see cref="effects"/>. Nothing
        /// is shipped with the mod: these are the game's own, so they are whatever this build of
        /// the game says they are, and a name that is not in the game at all simply drops out.
        /// An empty list leaves the kick deliberately silent.
        /// </summary>
        private static void ResolveEffects()
        {
            effects = null;
            if (ZNetScene.instance == null)
            {
                return; // not in a world - the next ZNetScene.Awake tries again
            }

            List<EffectList.EffectData> found = new List<EffectList.EffectData>();
            List<string> missing = new List<string>();
            foreach (string entry in effectPrefabs.Value.Split(','))
            {
                string name = entry.Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab != null)
                {
                    found.Add(new EffectList.EffectData { m_prefab = prefab, m_enabled = true });
                }
                else
                {
                    missing.Add(name);
                }
            }
            // Only the effects that carry a ZNetView are in ZNetScene; the rest live wherever the
            // prefab that uses them loaded them, so they have to be searched for. That is a walk
            // over every object Unity has in memory, which is why it happens once, for all of them
            // at once, and only for the names that were not found the cheap way. A root wins; a
            // child of the same name is only kept in reserve, because some effects are never
            // loaded loose at all - sfx_battering_ram_impact is its own prefab in the game's
            // bundles, but the ram only references fx_batteringram_fire, which carries the sound
            // as a child, so that child is the only copy ever in memory. Instantiating a child
            // clones it as a root of its own, without the smoke and flames of its parent.
            if (missing.Count > 0)
            {
                Dictionary<string, GameObject> children = new Dictionary<string, GameObject>();
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    int at = missing.IndexOf(go.name);
                    if (at < 0)
                    {
                        continue;
                    }
                    if (go.transform.parent != null)
                    {
                        // Only a prefab's own child, not one inside a ram standing in the world,
                        // which would take the effect with it when it is torn down.
                        if (!go.scene.IsValid() && !children.ContainsKey(go.name))
                        {
                            children.Add(go.name, go);
                        }
                        continue;
                    }
                    found.Add(new EffectList.EffectData { m_prefab = go, m_enabled = true });
                    missing.RemoveAt(at);
                    if (missing.Count == 0)
                    {
                        break;
                    }
                }
                for (int i = missing.Count - 1; i >= 0; i--)
                {
                    GameObject child;
                    if (children.TryGetValue(missing[i], out child))
                    {
                        found.Add(new EffectList.EffectData { m_prefab = child, m_enabled = true });
                        missing.RemoveAt(i);
                    }
                }
            }
            foreach (string name in missing)
            {
                TweakHost.Log.LogWarning("no effect prefab called " + name);
            }
            if (found.Count > 0)
            {
                effects = new EffectList { m_effectPrefabs = found.ToArray() };
            }
        }

        // ---- The swing -----------------------------------------------------------------------

        /// <summary>
        /// Winds the door's animator up and lets it down again once the swing is over. The state
        /// change itself is the game's: it arrives through UseDoor whenever the door's owner gets
        /// around to it, which is why the animator is sped up first and held there for a window
        /// rather than for the length of an animation that has not started yet.
        /// </summary>
        private static void Swing(Door door)
        {
            BaseUnityPlugin host = TweakHost.Plugin;
            if (host == null || door.m_animator == null)
            {
                return;
            }
            Coroutine running;
            if (swinging.TryGetValue(door, out running))
            {
                host.StopCoroutine(running);
            }
            swinging[door] = host.StartCoroutine(SwingRoutine(door, door.m_animator));
        }

        private static IEnumerator SwingRoutine(Door door, Animator animator)
        {
            animator.speed = Mathf.Max(1f, swingSpeed.Value);
            // Held until the door has swung open and come to rest, or is being shut again -
            // a door closed right after the kick must close at its normal pace. SwingWindow is
            // only the cap for a UseDoor that never comes back.
            bool opened = false;
            float until = Time.time + SwingWindow;
            while (animator != null && Time.time < until)
            {
                int state = animator.GetInteger("state");
                if (opened && state == 0)
                {
                    break; // shut again
                }
                if (state != 0)
                {
                    opened = true;
                    AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                    if (info.IsTag("open") && !animator.IsInTransition(0) && info.normalizedTime >= 1f)
                    {
                        break; // swung open and at rest
                    }
                }
                yield return null;
            }
            // The door may have been unloaded or torn down in the meantime; the animator is what
            // has to be put back, and only while it is still there.
            if (animator != null)
            {
                animator.speed = 1f;
            }
            swinging.Remove(door);
        }
    }
}
