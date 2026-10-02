using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Carries settings over from the config file of Odin's Missing Patch from before it was split
    /// into a pack: every entry in a section the member's own file did not have yet takes the old
    /// value, if the old file has the same section and key. Per section, not per file, so a mod
    /// whose file existed before it took a tweak in (This Is Valheim) still gets that tweak's
    /// settings. The tweaks kept their section names through the split for this; a setting that
    /// was renamed or merged on the way simply starts at its default.
    /// </summary>
    internal static class LegacyConfig
    {
        private const string OldFile = "rauschekuh.odinsmissingpatch.cfg";

        /// <summary>The sections a config file has on disk; empty when there is no file yet.</summary>
        internal static HashSet<string> Sections(string path)
        {
            var sections = new HashSet<string>();
            try
            {
                if (File.Exists(path))
                {
                    foreach (ConfigDefinition definition in Read(path).Keys)
                    {
                        sections.Add(definition.Section);
                    }
                }
            }
            catch (Exception e)
            {
                TweakHost.Log.LogWarning("could not read " + path + ": " + e.Message);
            }
            return sections;
        }

        /// <param name="known">The sections the member's file had before anything was bound.</param>
        internal static void Import(ConfigFile config, HashSet<string> known)
        {
            try
            {
                string path = Path.Combine(Paths.ConfigPath, OldFile);
                if (!File.Exists(path))
                {
                    return;
                }
                Dictionary<ConfigDefinition, string> old = Read(path);
                int taken = 0;
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in config)
                {
                    if (!known.Contains(entry.Key.Section) && old.TryGetValue(entry.Key, out string value))
                    {
                        entry.Value.SetSerializedValue(value);
                        taken++;
                    }
                }
                if (taken > 0)
                {
                    config.Save();
                    TweakHost.Log.LogInfo("took " + taken + " setting(s) over from " + OldFile);
                }
            }
            catch (Exception e)
            {
                TweakHost.Log.LogWarning("could not read the settings of " + OldFile + ": " + e.Message);
            }
        }

        /// <summary>The section, key and raw value of every setting in a BepInEx config file.</summary>
        private static Dictionary<ConfigDefinition, string> Read(string path)
        {
            var values = new Dictionary<ConfigDefinition, string>();
            string section = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                int split = line.IndexOf('=');
                if (split > 0 && section.Length > 0)
                {
                    values[new ConfigDefinition(section, line.Substring(0, split).Trim())] = line.Substring(split + 1).Trim();
                }
            }
            return values;
        }
    }
}
