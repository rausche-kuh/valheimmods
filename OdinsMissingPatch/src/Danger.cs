using HarmonyLib;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Whether the local player is in danger: what makes CombatStamina charge again and keeps the
    /// item magnet from being used mid fight. The three settings live in <see cref="SharedSettings"/>
    /// so both ask the same question.
    ///
    /// Three things count: a hostile creature inside the threat radius, whether it has noticed you
    /// or not, an enraged enemy - one that is alerted and has you as its target - at any distance,
    /// and a boss health bar on screen. The second is what makes running from a troll count even
    /// once it is 30m behind you; the third covers a boss that is not coming for you right now -
    /// Moder circling, Bonemass lumbering, a boss after another player.
    /// </summary>
    internal static class Danger
    {
        // How often the loaded characters are searched for a threat. Every frame would be waste,
        // and a quarter second is well below anything a player notices.
        private const float CheckInterval = 0.25f;

        // How long a monster's "alerted, and you are my target" report keeps you in danger. The
        // game's own targeted indicator trusts the same reports for one second; the extra half
        // absorbs the network jitter of reports from monsters another player owns.
        private const float EnragedMemory = 1.5f;

        private static float lastCheck = float.NegativeInfinity;
        private static bool inDanger;

        // When an alerted monster last reported the local player as its target.
        private static float lastEnraged = float.NegativeInfinity;

        internal static bool Near(Player player)
        {
            if (Time.time - lastCheck < CheckInterval)
            {
                return inDanger;
            }
            lastCheck = Time.time;
            inDanger = Enraged() || BossBarShowing() || HostileWithin(player, SharedSettings.ThreatRadius.Value);
            return inDanger;
        }

        private static bool Enraged()
        {
            return SharedSettings.EnragedEnemies.Value && Time.time - lastEnraged < EnragedMemory;
        }

        /// <summary>
        /// The boss bar is the game's own "boss fight" verdict: an alerted boss within
        /// EnemyHud.m_maxShowDistanceBoss (100m) of the local player.
        /// </summary>
        private static bool BossBarShowing()
        {
            return SharedSettings.BossFights.Value && EnemyHud.instance != null && EnemyHud.instance.ShowingBossHud();
        }

        /// <summary>
        /// Hostility is the game's own verdict, not a list of prefab names: tamed animals and
        /// other players are ignored, a boar that has not noticed you still counts, an aggravated
        /// dvergr counts, and anything another mod adds is judged by the same rule.
        /// </summary>
        private static bool HostileWithin(Player player, float radius)
        {
            Vector3 here = player.transform.position;
            float sqrRadius = radius * radius;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character == player || character.IsDead())
                {
                    continue;
                }
                if (!BaseAI.IsEnemy(player, character))
                {
                    continue;
                }
                if ((character.transform.position - here).sqrMagnitude <= sqrRadius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A monster whose target is a player reports it to that player every half second or so,
        /// with whether it is alerted, and the report is an RPC to the player's own client - so
        /// it arrives whoever owns the monster, which the monster's target itself does not (it is
        /// never written to the ZDO). This is the enraged signal: alerted, and coming for you.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.RPC_OnTargeted))]
        [Serves(typeof(CombatStamina), typeof(ItemMagnet))]
        private static class NoticeEnragedEnemy
        {
            private static void Postfix(Player __instance, bool alerted)
            {
                if (alerted && __instance == Player.m_localPlayer)
                {
                    lastEnraged = Time.time;
                }
            }
        }
    }
}
