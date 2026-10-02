using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The hammer and the pickaxe stay in hand while swimming, and can be drawn there, so a dock,
    /// a pier or a rock under the surface can be worked on without wading back to shore. Every
    /// other item still goes back on the hip in deep water, and the off hand is emptied with it:
    /// this is for building, not for fighting or lighting the way.
    /// </summary>
    internal sealed class BuildInWater : Tweak
    {
        internal static readonly BuildInWater Instance = new BuildInWater();

        private BuildInWater() { }

        // True while Humanoid.UpdateEquipment runs for the local player holding a tool this tweak
        // keeps, so the HideHandItems prefix knows the hide it sees is the swim's.
        private static bool keepTool;

        internal override string Section => "Build In Water";

        protected override string Summary =>
            "Keep the hammer and the pickaxe in hand while swimming, and draw them in water, so you " +
            "can build and mine without going ashore. Other items are still put away.";

        /// <summary>
        /// A building tool (anything with build pieces - the hammer, and the hoe and cultivator
        /// along with it) or a pickaxe.
        /// </summary>
        private static bool IsAllowed(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null
                && (item.m_shared.m_buildPieces != null
                    || item.m_shared.m_skillType == Skills.SkillType.Pickaxes);
        }

        private static bool Applies(Humanoid humanoid, ItemDrop.ItemData item)
        {
            return Instance.On && humanoid != null && humanoid == Player.m_localPlayer && IsAllowed(item);
        }

        // UpdateEquipment hides both hands on every tick the player swims off the ground. Its other
        // callers (the hide key, a crafting station, a chair, eating) are left alone.

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
        private static class NoticeSwimHide
        {
            private static void Prefix(Humanoid __instance)
            {
                keepTool = Applies(__instance, __instance.m_rightItem);
            }

            private static void Postfix()
            {
                keepTool = false;
            }
        }

        /// <summary>
        /// Under the swim's hide the tool stays and only the off hand goes, remembered as hidden
        /// the way vanilla does, so the hide key brings it back on land.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems))]
        private static class KeepToolOut
        {
            private static bool Prefix(Humanoid __instance, ref bool __result)
            {
                if (!keepTool)
                {
                    return true;
                }
                ItemDrop.ItemData left = __instance.m_leftItem;
                if (left != null)
                {
                    __instance.UnequipItem(left);
                    __instance.m_hiddenLeftItem = left;
                    __instance.SetupVisEquipment(__instance.m_visEquipment, isRagdoll: false);
                }
                __result = left != null;
                return false;
            }
        }

        /// <summary>
        /// EquipItem refuses anything while the player swims off the ground. Its one IsSwimming
        /// call is swapped for <see cref="SwimmingBlocks"/>, which waives it for a tool; the
        /// other refusals (mid attack, mid dodge, broken) still apply.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class DrawToolInWater
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(code =>
                        code.Calls(AccessTools.Method(typeof(Character), nameof(Character.IsSwimming)))))
                    .ThrowIfInvalid("Humanoid.EquipItem no longer asks Character.IsSwimming")
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_1))
                    .SetInstruction(new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(BuildInWater), nameof(SwimmingBlocks))))
                    .InstructionEnumeration();
            }
        }

        private static bool SwimmingBlocks(Humanoid humanoid, ItemDrop.ItemData item)
        {
            return humanoid.IsSwimming() && !Applies(humanoid, item);
        }
    }
}
