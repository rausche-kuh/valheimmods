using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OdinsMissingPatch
{
    /// <summary>
    /// What every mod of the family does on start, so its plugin class holds nothing but its name
    /// and its tweaks: bind the config, carry settings over from the old all-in-one mod on a first
    /// start, then patch the tweaks that are on.
    /// <para>
    /// Every member compiles its own copy of this (see the family's Directory.Build.props), so
    /// <see cref="Log"/> and <see cref="Plugin"/> are that member's own, never another's.
    /// </para>
    /// </summary>
    internal static class TweakHost
    {
        /// <summary>The mod's one log, for every tweak and helper.</summary>
        internal static ManualLogSource Log;

        /// <summary>The plugin itself, for anything that needs a MonoBehaviour (a coroutine).</summary>
        internal static BaseUnityPlugin Plugin;

        /// <param name="bindShared">Binds the settings several of the mod's tweaks read, before the tweaks bind theirs.</param>
        internal static void Start(BaseUnityPlugin plugin, ManualLogSource logger, ConfigFile config, string guid,
            Tweak[] tweaks, Action<ConfigFile> bindShared = null)
        {
            Plugin = plugin;
            Log = logger;
            // Read before anything is bound: binding adds the sections to the file.
            HashSet<string> known = LegacyConfig.Sections(config.ConfigFilePath);

            bindShared?.Invoke(config);
            Patcher.Map(tweaks, logger);
            foreach (Tweak tweak in tweaks)
            {
                tweak.Setup(config, Patcher.MayNeedRestart(tweak));
            }
            LegacyConfig.Import(config, known);

            // Only the tweaks switched on get their patches, each on its own, so a tweak switched
            // off leaves the game code it would touch to other mods, and a patch a game update
            // broke takes down only the tweaks that need it.
            Patcher.Apply(new Harmony(guid), tweaks);

            string on = string.Join(", ", tweaks.Where(t => t.On).Select(t => t.Section).ToArray());
            logger.LogInfo(on.Length > 0 ? "tweaks on: " + on : "every tweak is switched off");
        }
    }
}
