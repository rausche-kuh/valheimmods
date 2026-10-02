using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Names the tweaks a patch class outside any tweak works for: it is applied when one of them
    /// is on, and its failing to apply switches them off. Optional marks one whose failure only
    /// costs a nicety (a hover line, a tint), so it is logged and the tweaks carry on.
    /// <para>
    /// A shared helper in Common cannot name tweaks - each mod has its own - so it names itself
    /// instead (<c>[Serves(typeof(Danger))]</c>), and serves every tweak whose
    /// <see cref="Tweak.Uses"/> lists it. A mod none of whose tweaks uses it never applies it.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class ServesAttribute : Attribute
    {
        internal readonly Type[] Tweaks;

        public bool Optional { get; set; }

        public ServesAttribute(params Type[] tweaks)
        {
            Tweaks = tweaks;
        }
    }

    /// <summary>A patch class applied on every launch, whatever is switched on (the mod's words, dev commands).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class AlwaysAttribute : Attribute
    {
    }

    /// <summary>
    /// A patch whose target runs once per object as it loads (an Awake, a Start, a registration).
    /// Applied mid game it would miss everything already loaded, so a tweak that still lacks one
    /// only switches on at the next launch.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class LoadHookAttribute : Attribute
    {
    }

    /// <summary>
    /// Applies the patches tweak by tweak: only those of a tweak that is on at launch, each class
    /// on its own, so a game update that breaks one target switches off the tweaks that depend on
    /// it and nothing else. A tweak switched on mid game is patched then, unless it would need a
    /// <see cref="LoadHookAttribute"/> patch that is not in yet; one switched off stays patched
    /// and inert until the next launch.
    /// </summary>
    internal static class Patcher
    {
        private sealed class PatchClass
        {
            internal Type Type;

            /// <summary>The tweaks it works for; empty for one that is always applied.</summary>
            internal Tweak[] Serves;

            internal bool Optional;
            internal bool LoadHook;
            internal bool Applied;
            internal bool Failed;
        }

        private static readonly List<PatchClass> classes = new List<PatchClass>();
        private static Harmony harmony;
        private static ManualLogSource log;

        /// <summary>Finds every patch class in the mod and the tweaks it belongs to. Runs before the tweaks bind.</summary>
        internal static void Map(IList<Tweak> tweaks, ManualLogSource logSource)
        {
            log = logSource;
            Dictionary<Type, Tweak> byType = tweaks.ToDictionary(t => t.GetType());
            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (!type.IsDefined(typeof(HarmonyPatch), false))
                {
                    continue;
                }
                Tweak[] owners = Owners(type, byType);
                if (owners == null)
                {
                    continue;
                }
                classes.Add(new PatchClass
                {
                    Type = type,
                    Serves = owners,
                    Optional = type.GetCustomAttributes(typeof(ServesAttribute), false)
                        .Cast<ServesAttribute>().Any(s => s.Optional),
                    LoadHook = type.IsDefined(typeof(LoadHookAttribute), false),
                });
            }
        }

        /// <summary>
        /// A class nested in a tweak belongs to it, a shared one to the tweaks its Serves names.
        /// One with neither is applied always and reported, so a forgotten Serves shows in the log.
        /// Null for a shared one that serves no tweak of this mod: it is never applied.
        /// </summary>
        private static Tweak[] Owners(Type type, Dictionary<Type, Tweak> byType)
        {
            if (type.IsDefined(typeof(AlwaysAttribute), false))
            {
                return new Tweak[0];
            }
            ServesAttribute serves = (ServesAttribute)type.GetCustomAttributes(typeof(ServesAttribute), false).FirstOrDefault();
            if (serves != null)
            {
                var owners = new List<Tweak>();
                foreach (Type tweak in serves.Tweaks)
                {
                    if (byType.TryGetValue(tweak, out Tweak owner))
                    {
                        owners.Add(owner);
                    }
                    else if (typeof(Tweak).IsAssignableFrom(tweak))
                    {
                        log.LogWarning(type.FullName + " serves " + tweak.Name + ", which is not in the Tweaks list");
                    }
                    else
                    {
                        owners.AddRange(byType.Values.Where(t => t.Uses.Contains(tweak)));
                    }
                }
                return owners.Count > 0 ? owners.Distinct().ToArray() : null;
            }
            for (Type outer = type.DeclaringType; outer != null; outer = outer.DeclaringType)
            {
                if (byType.TryGetValue(outer, out Tweak owner))
                {
                    return new[] { owner };
                }
            }
            log.LogWarning(type.FullName + " belongs to no tweak and names none, so it is always applied");
            return new Tweak[0];
        }

        /// <summary>
        /// Whether any tweak the shared patch class works for is on right now: what a shared
        /// helper asks instead of listing its tweaks a second time beside its Serves.
        /// </summary>
        internal static bool AnyServedOn(Type patchClass)
        {
            foreach (PatchClass patch in classes)
            {
                if (patch.Type != patchClass)
                {
                    continue;
                }
                foreach (Tweak tweak in patch.Serves)
                {
                    if (tweak.On)
                    {
                        return true;
                    }
                }
                return false;
            }
            return false;
        }

        /// <summary>Whether switching the tweak on mid game can have to wait for a restart.</summary>
        internal static bool MayNeedRestart(Tweak tweak)
        {
            return classes.Any(c => c.LoadHook && c.Serves.Contains(tweak));
        }

        /// <summary>Patches what the tweaks switched on at launch need, and listens for the others being switched on.</summary>
        internal static void Apply(Harmony instance, IList<Tweak> tweaks)
        {
            harmony = instance;
            foreach (PatchClass patch in classes)
            {
                if (patch.Serves.Length == 0 || patch.Serves.Any(t => t.Wanted && !t.Broken))
                {
                    TryApply(patch);
                }
            }
            foreach (Tweak tweak in tweaks)
            {
                if (tweak.Wanted && !tweak.Broken)
                {
                    GoLive(tweak);
                }
                tweak.Enabled.SettingChanged += (sender, args) => SwitchedOn(tweak);
            }
        }

        private static void SwitchedOn(Tweak tweak)
        {
            if (!tweak.Wanted || tweak.Patched || tweak.Broken)
            {
                return;
            }
            List<PatchClass> missing = classes
                .Where(c => c.Serves.Contains(tweak) && !c.Applied && !c.Failed)
                .ToList();
            if (missing.Any(c => c.LoadHook))
            {
                log.LogInfo(tweak.Section + " was off at launch and takes effect at the next start of the game");
                return;
            }
            foreach (PatchClass patch in missing)
            {
                TryApply(patch);
            }
            if (!tweak.Broken)
            {
                GoLive(tweak);
                log.LogInfo(tweak.Section + " switched on");
            }
        }

        /// <summary>Marks the tweak patched and lets it catch up on what was loaded without it.</summary>
        private static void GoLive(Tweak tweak)
        {
            tweak.Patched = true;
            tweak.Refresh();
        }

        private static void TryApply(PatchClass patch)
        {
            try
            {
                harmony.CreateClassProcessor(patch.Type).Patch();
                patch.Applied = true;
            }
            catch (Exception e)
            {
                patch.Failed = true;
                Unpatch(patch);
                string cause = (e.InnerException ?? e).Message;
                if (patch.Serves.Length == 0 || patch.Optional)
                {
                    log.LogError(patch.Type.FullName + " could not be patched, carrying on without it: " + cause);
                    return;
                }
                foreach (Tweak tweak in patch.Serves)
                {
                    Break(tweak, patch.Type, cause);
                }
            }
        }

        /// <summary>
        /// Switches a tweak off for the session and takes out what was patched for it alone. A
        /// shared class stays in while another tweak it serves still uses it.
        /// </summary>
        private static void Break(Tweak tweak, Type failed, string cause)
        {
            if (tweak.Broken)
            {
                return;
            }
            tweak.Broken = true;
            tweak.Patched = false;
            log.LogError(tweak.Section + " is switched off: " + failed.FullName + " could not be patched, " +
                "most likely because a game update changed it. " + cause);
            foreach (PatchClass patch in classes)
            {
                if (patch.Applied && patch.Serves.Contains(tweak)
                    && !patch.Serves.Any(t => !t.Broken && (t.Patched || t.Wanted)))
                {
                    Unpatch(patch);
                    patch.Applied = false;
                }
            }
            tweak.Refresh();
        }

        /// <summary>Removes every patch method of the class this mod put on any game method.</summary>
        private static void Unpatch(PatchClass patch)
        {
            try
            {
                foreach (MethodBase original in harmony.GetPatchedMethods().ToList())
                {
                    Patches info = Harmony.GetPatchInfo(original);
                    if (info == null)
                    {
                        continue;
                    }
                    IEnumerable<Patch> all = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                        .Concat(info.Finalizers).Concat(info.ILManipulators);
                    foreach (Patch applied in all.ToList())
                    {
                        if (applied.owner == harmony.Id && applied.PatchMethod.DeclaringType == patch.Type)
                        {
                            harmony.Unpatch(original, applied.PatchMethod);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log.LogError("could not take out " + patch.Type.FullName + ": " + e.Message);
            }
        }
    }
}
