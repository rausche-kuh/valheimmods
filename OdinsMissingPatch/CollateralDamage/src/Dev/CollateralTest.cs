using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OdinsMissingPatch
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships.
    internal sealed partial class CollateralDamage
    {
        /// <summary>Test scenes: the attacker first, then the peers placed between it and the player.</summary>
        private static readonly Dictionary<string, string[]> Scenes = new Dictionary<string, string[]>
        {
            { "troll", new[] { "Troll", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman" } },
            { "trolls", new[] { "Troll", "Troll", "Greydwarf", "Greydwarf", "Greydwarf" } },
            { "boar", new[] { "Troll", "Boar", "Boar", "Boar", "Boar" } },
            { "eikthyr", new[] { "Eikthyr", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf" } },
            { "elder", new[] { "gd_king", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf", "Greydwarf" } },
            { "bonemass", new[] { "Bonemass", "Skeleton", "Skeleton", "Skeleton", "Draugr", "Draugr" } },
            { "yagluth", new[] { "GoblinKing", "Lox", "Lox", "Lox", "Goblin", "Goblin", "Goblin" } },
            { "queen", new[] { "SeekerQueen", "Seeker", "Seeker", "SeekerBrood", "SeekerBrood", "SeekerBrute" } },
            { "fader", new[] { "Fader", "Charred_Melee", "Charred_Melee", "Charred_Archer", "Charred_Archer" } },
        };

        /// <summary>
        /// omp_cd scene: spawns a test scene (attacker 18m ahead, its peers in a ring 6m ahead);
        /// omp_cd_info [radius] lists the creatures around with health, collateral damage, the boss
        /// spawn mark, alert state, target and whether they would drop loot; omp_cd_clear removes
        /// every creature within 50m without drops. Every collateral hit and every death of a
        /// creature that took one is shown top left and written to the BepInEx log.
        /// </summary>
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class Commands
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("omp_cd",
                    "[" + string.Join("|", new List<string>(Scenes.Keys).ToArray()) + "] spawns a collateral damage test scene in front of you",
                    args => Stage(args.Context, args.Length > 1 ? args[1].ToLowerInvariant() : ""));
                new Terminal.ConsoleCommand("omp_cd_info", "[radius] lists nearby creatures: health, collateral, boss spawn, target, loot",
                    args => Info(args.Context, args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 30f));
                new Terminal.ConsoleCommand("omp_cd_clear", "removes every creature within 50m, no drops",
                    args => Clear(args.Context));
            }
        }

        private static void Stage(Terminal context, string scene)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                context.AddString("no player");
                return;
            }
            if (!Scenes.TryGetValue(scene, out string[] prefabs))
            {
                context.AddString("scenes: " + string.Join(", ", new List<string>(Scenes.Keys).ToArray()));
                return;
            }
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 origin = player.transform.position;
            Place(prefabs[0], origin + forward * 18f, -forward);
            Vector3 ring = origin + forward * 6f;
            int peers = prefabs.Length - 1;
            for (int i = 0; i < peers; i++)
            {
                float angle = i * Mathf.PI * 2f / peers;
                Place(prefabs[i + 1], ring + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f, -forward);
            }
            context.AddString("spawned " + string.Join(", ", prefabs) + " - god mode recommended, omp_cd_info to inspect");
        }

        private static void Place(string name, Vector3 position, Vector3 facing)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null)
            {
                TweakHost.Log.LogWarning("omp_cd: no prefab " + name);
                return;
            }
            if (ZoneSystem.instance != null)
            {
                position.y = ZoneSystem.instance.GetGroundHeight(position) + 0.5f;
            }
            Object.Instantiate(prefab, position, Quaternion.LookRotation(facing));
        }

        private static void Info(Terminal context, float radius)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                context.AddString("no player");
                return;
            }
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character.IsPlayer() || Vector3.Distance(character.transform.position, player.transform.position) > radius)
                {
                    continue;
                }
                ZDO zdo = GetZDO(character);
                float collateral = zdo != null ? zdo.GetFloat(CollateralKey) : 0f;
                bool bossSpawn = zdo != null && zdo.GetBool(BossSpawnKey);
                MonsterAI ai = character.GetBaseAI() as MonsterAI;
                string target = ai != null && ai.GetTargetCreature() != null ? ai.GetTargetCreature().m_name : "-";
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0} hp {1:0}/{2:0} | collateral {3:0} ({4:0}%) | {5}{6}alerted {7} | target {8} | {9}",
                    Localization.instance.Localize(character.m_name), character.GetHealth(), character.GetMaxHealth(),
                    collateral, 100f * collateral / Mathf.Max(1f, character.GetMaxHealth()),
                    bossSpawn ? "BOSS SPAWN | " : "", Instance.IsAttacker(character) ? "ATTACKER | " : "",
                    ai != null && ai.IsAlerted(), Localization.instance.Localize(target),
                    WouldDrop(character, collateral, false) ? "loot" : "no loot unless a player finishes it");
                context.AddString(line);
                TweakHost.Log.LogInfo("omp_cd_info: " + line);
            }
        }

        private static void Clear(Terminal context)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                return;
            }
            int removed = 0;
            foreach (Character character in new List<Character>(Character.GetAllCharacters()))
            {
                if (character != null && !character.IsPlayer() &&
                    Vector3.Distance(character.transform.position, player.transform.position) <= 50f)
                {
                    ZNetScene.instance.Destroy(character.gameObject);
                    removed++;
                }
            }
            context.AddString("removed " + removed);
        }

        /// <summary>The Loot patch's rule, repeated for the report.</summary>
        private static bool WouldDrop(Character character, float collateral, bool playerFinished)
        {
            return collateral <= 0f || playerFinished || collateral <= Instance.lootLimit.Value * character.GetMaxHealth();
        }

        private static void Report(string text)
        {
            TweakHost.Log.LogInfo("collateral: " + text);
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text, 0, null, false, false);
            }
        }

        /// <summary>Reports each collateral hit on the victim's owner: attacker, damage, health left.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class ReportHit
        {
            private static void Prefix(Character __instance, HitData hit, out string __state)
            {
                __state = null;
                if (Instance.On && hit != null && __instance.m_nview != null && __instance.m_nview.IsValid() &&
                    __instance.m_nview.IsOwner() && hit.HaveAttacker() && Instance.IsCollateral(hit.GetAttacker(), __instance))
                {
                    __state = Localization.instance.Localize(hit.GetAttacker().m_name) + " hit " +
                        Localization.instance.Localize(__instance.m_name);
                }
            }

            private static void Postfix(Character __instance, string __state)
            {
                if (__state == null)
                {
                    return;
                }
                ZDO zdo = GetZDO(__instance);
                Report(string.Format(CultureInfo.InvariantCulture, "{0}: hp {1:0}/{2:0}, collateral total {3:0}",
                    __state, __instance.GetHealth(), __instance.GetMaxHealth(), zdo != null ? zdo.GetFloat(CollateralKey) : -1f));
            }
        }

        /// <summary>Reports the loot decision for every creature that took collateral damage.</summary>
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.OnDeath))]
        private static class ReportDeath
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(CharacterDrop __instance)
            {
                Character character = __instance.m_character;
                ZDO zdo = character != null ? GetZDO(character) : null;
                float collateral = zdo != null ? zdo.GetFloat(CollateralKey) : 0f;
                if (!Instance.On || collateral <= 0f)
                {
                    return;
                }
                Character killer = character.m_lastHit != null ? character.m_lastHit.GetAttacker() : null;
                bool drops = WouldDrop(character, collateral, killer is Player);
                Report(string.Format(CultureInfo.InvariantCulture, "{0} died (killed by {1}), collateral {2:0}/{3:0} -> {4}",
                    Localization.instance.Localize(character.m_name),
                    killer != null ? Localization.instance.Localize(killer.m_name) : "nobody (burn/poison?)",
                    collateral, character.GetMaxHealth(), drops ? "drops" : "NO drops"));
            }
        }
    }
}
