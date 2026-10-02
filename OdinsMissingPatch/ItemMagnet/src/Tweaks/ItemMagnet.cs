using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Hold a key to call Odin's pull: the player takes the Forsaken power pose, floats a hand
    /// above the ground with the activation fire around them, and every item lying within a ring
    /// that grows the longer the key is held flies to their feet. What fits in the backpack is
    /// then taken by the game's own auto pickup; the rest stays in a pile at the player's feet.
    ///
    /// Two rules keep it a cleanup and not a hauling tool or a combat trick, so it needs no
    /// cooldown: an item that has been moved once - pulled, or dropped by a player - is never
    /// pulled again (<see cref="MovedMarker"/>), and it only works while you are out of danger
    /// (<see cref="Danger"/>): it cannot be started then, and a channel stops when danger arrives.
    ///
    /// The game facts this rests on are in docs/item-magnet.md.
    /// </summary>
    internal sealed partial class ItemMagnet : Tweak
    {
        internal static readonly ItemMagnet Instance = new ItemMagnet();

        private ItemMagnet() { }

        // How fast a pulled item flies, in metres per second - the speed of the game's own auto
        // pickup pull, so the magnet reads as the same force at a larger reach.
        private const float PullSpeed = 15f;

        // Horizontal distance from the player at which an item counts as at their feet: inside
        // the game's auto pickup range, outside the player's own collider.
        private const float ArriveDistance = 1f;

        // Items still in flight when the key is let go are flown home for this long at most.
        private const float SettleTime = 4f;

        // The animator speed that holds the pose: not quite a freeze, so it still breathes.
        private const float HoldSpeed = 0.03f;

        // If the pose's GPower event has not come by then, the animation was cut short and the
        // next GPower event is a real guardian power again.
        private const float PoseTimeout = 3f;

        // The activation fire is replayed this often while the key is held.
        private const float FxInterval = 2f;

        // How high the player floats, how far the float bobs, and how fast either is reached.
        private const float Lift = 0.35f;
        private const float Bob = 0.06f;
        private const float LiftSpeed = 0.8f;

        // The pause screen's camera sway (Game.UpdatePause): degrees per second at its widest,
        // and how long it takes to fade in.
        private const float SwayDegrees = 5f;
        private const float SwayFadeTime = 1f;

        private ConfigEntry<KeyboardShortcut> hotkey;
        private ConfigEntry<float> baseRadius;
        private ConfigEntry<float> radiusPerSecond;
        private ConfigEntry<float> maxRadius;

        private Player channeler;
        private bool channeling;
        private float channelStart;
        private float swallowPowerUntil;
        private float settleUntil;
        private float lastFx;

        // Whether the pose has reached its peak and is being held.
        private bool posed;

        // Pullable items within MaxRadius that are not on their way yet, as of the last sweep:
        // outside the ring, or waiting for their owner to hand them over.
        private int waiting;

        // What is being flown in: the list to walk, the set to ask.
        private readonly List<ItemDrop> pulling = new List<ItemDrop>();
        private readonly HashSet<ItemDrop> pullingSet = new HashSet<ItemDrop>();

        // The float: how high the visual is now, and where it rests when it is not lifted.
        private Transform liftedVisual;
        private Vector3 visualRest;
        private float lift;

        private EffectList activationFx;
        private ObjectDB activationFxSource;

        internal override string Section => "Item Magnet";

        internal override Type[] Uses => new[] { typeof(Danger) };

        protected override string Summary =>
            "Hold a key to pull the items lying around you to your feet, in a ring that grows " +
            "the longer you hold it. Only out of combat, and an item that was moved once - pulled " +
            "or dropped by a player - is never pulled again.";

        protected override void Bind(ConfigFile config)
        {
            hotkey = config.Bind(Section, "Hotkey", new KeyboardShortcut(KeyCode.Y),
                "Hold to pull. Let go to stop.");
            baseRadius = config.Bind(Section, "BaseRadius", 10f, new ConfigDescription(
                "Metres. How far the pull reaches after holding the key for one second.",
                new AcceptableValueRange<float>(1f, 50f)));
            radiusPerSecond = config.Bind(Section, "RadiusPerSecond", 5f, new ConfigDescription(
                "Metres the reach grows by for every further second the key is held.",
                new AcceptableValueRange<float>(0f, 20f)));
            maxRadius = config.Bind(Section, "MaxRadius", 60f, new ConfigDescription(
                "Metres. The reach stops growing here. Items much further away than this are " +
                "often not loaded on your machine at all.",
                new AcceptableValueRange<float>(1f, 100f)));
        }

        /// <summary>How far the pull reaches after holding the key this long.</summary>
        private float RadiusAfter(float seconds)
        {
            float radius = baseRadius.Value + radiusPerSecond.Value * (seconds - 1f);
            return Mathf.Clamp(radius, 0f, maxRadius.Value);
        }

        private float Radius => channeling ? RadiusAfter(Time.time - channelStart) : 0f;

        // ---- Channelling ----------------------------------------------------------------------

        private void Tick(Player player)
        {
            if (channeler != null && channeler != player)
            {
                Forget();
            }
            if (channeling)
            {
                if (!On || !Hotkeys.Held(hotkey.Value) || !CanKeepChanneling(player))
                {
                    End(player);
                    return;
                }
                Sway(player);
                PlayFx(player);
                return;
            }
            if (On && player.TakeInput() && Hotkeys.Pressed(hotkey.Value))
            {
                TryStart(player);
            }
        }

        /// <summary>The checks StartGuardianPower makes before the same pose, plus being on the ground.</summary>
        private static bool CanPose(Player player)
        {
            return !player.IsDead() && !(player.InAttack() && !player.HaveQueuedChain()) && !player.InDodge()
                && player.CanMove() && !player.IsKnockedBack() && !player.IsStaggering()
                && !player.InMinorAction() && player.IsOnGround() && !player.IsSwimming()
                && !player.IsAttached() && !player.IsTeleporting() && !player.InPlaceMode();
        }

        private void TryStart(Player player)
        {
            if (!CanPose(player))
            {
                return;
            }
            if (Danger.Near(player))
            {
                player.Message(MessageHud.MessageType.Center, "$omp_magnet_danger");
                return;
            }
            channeler = player;
            channeling = true;
            channelStart = Time.time;
            swallowPowerUntil = Time.time + PoseTimeout;
            lastFx = float.NegativeInfinity;
            posed = false;
            player.m_zanim.SetTrigger("gpower");
        }

        private bool CanKeepChanneling(Player player)
        {
            if (player.IsDead() || player.IsStaggering() || player.IsKnockedBack() || player.IsSwimming()
                || player.IsAttached() || player.IsTeleporting() || !player.TakeInput())
            {
                return false;
            }
            if (Danger.Near(player))
            {
                player.Message(MessageHud.MessageType.Center, "$omp_magnet_danger");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Every way out of a channel ends here: the pose is let go and the animation plays out,
        /// what is in flight still lands, and the float settles in LateUpdate.
        /// </summary>
        private void End(Player player)
        {
            channeling = false;
            settleUntil = Time.time + SettleTime;
            if (player != null && player.m_zanim != null)
            {
                player.m_zanim.SetSpeed(1f);
            }
        }

        /// <summary>The player went away mid channel (logout, respawn): nothing of theirs is left to restore.</summary>
        private void Forget()
        {
            channeler = null;
            channeling = false;
            swallowPowerUntil = 0f;
            pulling.Clear();
            pullingSet.Clear();
            liftedVisual = null;
            lift = 0f;
        }

        /// <summary>
        /// The pose's GPower animation event, which would cast the player's real guardian power.
        /// The first one after a start is ours: it holds the pose instead.
        /// </summary>
        private bool TakeGPowerEvent(Player player)
        {
            if (player != channeler || Time.time > swallowPowerUntil)
            {
                return false;
            }
            swallowPowerUntil = 0f;
            if (channeling)
            {
                posed = true;
                player.m_zanim.SetSpeed(HoldSpeed);
            }
            return true;
        }

        // ---- The pull -------------------------------------------------------------------------

        /// <summary>
        /// Runs from the physics step, right after the game's own auto pickup. Items inside the
        /// ring are claimed (RequestOwn, the way auto pickup does it) and once owned marked and
        /// flown in; the owner's position is what the item's ZSyncTransform sends to everyone.
        /// Once the pose is held and the last item within MaxRadius has landed, the channel ends
        /// by itself - with nothing in reach, right after the pose.
        /// </summary>
        private void Pull(Player player, float dt)
        {
            if (player != channeler || (!channeling && pulling.Count == 0))
            {
                return;
            }
            Vector3 feet = player.transform.position;
            if (channeling)
            {
                Capture(player, feet, Radius);
            }
            for (int i = pulling.Count - 1; i >= 0; i--)
            {
                ItemDrop item = pulling[i];
                if (item == null || item.m_nview == null || !item.m_nview.IsValid() || !item.m_nview.IsOwner()
                    || FlyHome(item, feet, dt))
                {
                    pullingSet.Remove(item);
                    pulling.RemoveAt(i);
                }
            }
            if (channeling && waiting == 0 && pulling.Count == 0
                && (posed || Time.time - channelStart > PoseTimeout))
            {
                End(player);
            }
            if (!channeling && Time.time > settleUntil)
            {
                pulling.Clear();
                pullingSet.Clear();
            }
        }

        private void Capture(Player player, Vector3 feet, float radius)
        {
            waiting = 0;
            float sqrRadius = radius * radius;
            float sqrReach = maxRadius.Value * maxRadius.Value;
            foreach (ItemDrop item in ItemDrop.s_instances)
            {
                if (item == null || pullingSet.Contains(item))
                {
                    continue;
                }
                Vector3 offset = item.transform.position - feet;
                if (offset.sqrMagnitude > sqrReach || Horizontal(offset) <= ArriveDistance || !Pullable(player, item))
                {
                    continue;
                }
                if (offset.sqrMagnitude > sqrRadius)
                {
                    waiting++;
                    continue;
                }
                if (!item.m_nview.IsOwner())
                {
                    item.RequestOwn();
                    waiting++;
                    continue;
                }
                MovedMarker.Mark(item);
                pulling.Add(item);
                pullingSet.Add(item);
            }
        }

        /// <summary>What the game's auto pickup would consider, minus everything moved before.</summary>
        private static bool Pullable(Player player, ItemDrop item)
        {
            return item.m_nview != null && item.m_nview.IsValid() && item.m_autoPickup && !item.IsPiece()
                && !item.InTar() && !MovedMarker.IsMoved(item)
                && !player.HaveUniqueKey(item.m_itemData.m_shared.m_name);
        }

        /// <summary>Moves an item one step towards the player's feet; true once it is there.</summary>
        private static bool FlyHome(ItemDrop item, Vector3 feet, float dt)
        {
            Vector3 position = item.transform.position;
            Vector3 target = feet + Vector3.up * 0.5f;
            Vector3 toTarget = target - position;
            if (Horizontal(toTarget) <= ArriveDistance)
            {
                return true;
            }
            Vector3 next = position + Vector3.ClampMagnitude(toTarget, PullSpeed * dt);
            // Over a hill, not through it: the terrain under the item is the floor of its flight.
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(next) : next.y;
            next.y = Mathf.Max(next.y, ground + 0.3f);
            item.transform.position = next;
            Rigidbody body = item.m_body;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
            }
            return false;
        }

        private static float Horizontal(Vector3 offset)
        {
            return new Vector2(offset.x, offset.z).magnitude;
        }

        // ---- The look -------------------------------------------------------------------------

        /// <summary>
        /// The pause screen's slow camera sway while the key is held, faded in, flattened near
        /// straight up or down as the game does it. The game turns the eye while paused, when
        /// nothing else does; in play SetMouseLook rebuilds the eye from m_lookYaw every frame,
        /// so the sway turns the yaw and the mouse still adds to it.
        /// </summary>
        private void Sway(Player player)
        {
            if (player.m_eye == null)
            {
                return;
            }
            float fade = Mathf.Clamp01((Time.time - channelStart) / SwayFadeTime);
            float flat = Mathf.Max(0.05f, 1f - Mathf.Abs(Vector3.Dot(player.m_eye.forward, Vector3.up)));
            float degrees = Time.deltaTime * Mathf.Cos(Time.realtimeSinceStartup * 0.3f) * SwayDegrees * fade * flat;
            player.m_lookYaw *= Quaternion.Euler(0f, degrees, 0f);
            player.UpdateEyeRotation();
            player.m_lookDir = player.m_eye.forward;
        }

        /// <summary>
        /// The guardian powers' activation fire (fx_GP_Activation, every GP_ shares it), replayed
        /// while the key is held. Taken from the first guardian power in ObjectDB, so it works
        /// without the player holding one.
        /// </summary>
        private void PlayFx(Player player)
        {
            if (Time.time - lastFx < FxInterval)
            {
                return;
            }
            lastFx = Time.time;
            EffectList fx = ActivationFx();
            if (fx != null)
            {
                fx.Create(player.GetCenterPoint(), player.transform.rotation, player.transform);
            }
        }

        private EffectList ActivationFx()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                return null;
            }
            if (activationFxSource != db)
            {
                activationFxSource = db;
                activationFx = null;
                foreach (StatusEffect effect in db.m_StatusEffects)
                {
                    if (effect != null && effect.name.StartsWith("GP_") && effect.m_startEffects != null
                        && effect.m_startEffects.HasEffects())
                    {
                        activationFx = effect.m_startEffects;
                        break;
                    }
                }
            }
            return activationFx;
        }

        /// <summary>
        /// The float, eased in after the start and out after the end. Only the local player sees
        /// it: the pose itself is synced through the animator, the lift is the visual's offset.
        /// </summary>
        private void Float(Player player)
        {
            if (player != channeler)
            {
                return;
            }
            float target = channeling ? Lift + Mathf.Sin(Time.time * 2f) * Bob : 0f;
            if (lift == 0f && target == 0f)
            {
                return;
            }
            Transform visual = player.m_visual != null ? player.m_visual.transform : null;
            if (visual == null)
            {
                return;
            }
            if (liftedVisual != visual)
            {
                liftedVisual = visual;
                visualRest = visual.localPosition;
            }
            lift = Mathf.MoveTowards(lift, target, LiftSpeed * Time.deltaTime);
            visual.localPosition = visualRest + Vector3.up * lift;
            if (lift == 0f)
            {
                liftedVisual = null;
            }
        }

        // ---- Patches --------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Hotkey
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer && (Instance.On || Instance.channeling))
                {
                    Instance.Tick(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.GPower))]
        private static class HoldPose
        {
            private static bool Prefix(CharacterAnimEvent __instance)
            {
                Player player = __instance.m_character as Player;
                return player == null || player != Player.m_localPlayer || !Instance.TakeGPowerEvent(player);
            }
        }

        /// <summary>No walking, swinging, blocking or jumping out of the pose; looking around stays.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class StandStill
        {
            private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
                ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold,
                ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
            {
                if (!Instance.channeling || __instance != Instance.channeler)
                {
                    return;
                }
                movedir = Vector3.zero;
                attack = attackHold = secondaryAttack = secondaryAttackHold = false;
                block = blockHold = jump = crouch = run = autoRun = dodge = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        private static class PullItems
        {
            private static void Postfix(Player __instance, float dt)
            {
                if (__instance == Player.m_localPlayer)
                {
                    Instance.Pull(__instance, dt);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.LateUpdate))]
        private static class FloatPose
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                {
                    Instance.Float(__instance);
                }
            }
        }
    }
}
