using HarmonyLib;
using System;
using System.IO;
using System.Reflection;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The mod's own words, in translations.csv beside the DLL: one row per token, one column per
    /// language, English first. The game's own CSV reader loads it, so an empty cell falls back to
    /// English by itself and a new language is a new column and nothing else.
    /// <para>
    /// Call sites never hold a sentence, only a $omp_ token: UITooltip and MessageHud both localize
    /// when they show, so the words follow a language change on their own. Anything that writes a
    /// label straight onto a component has to run it through <see cref="Localization.Localize(string)"/>
    /// itself. A count or an item name goes in as $1, $2 - the game substitutes those after the
    /// lookup, so word order stays the translator's to choose.
    /// </para>
    /// </summary>
    internal static class Translations
    {
        private const string FileName = "translations.csv";

        /// <summary>The column the game falls back to when a cell is empty, and this one when a language is missing.</summary>
        private const string Fallback = "English";

        private static string csv;
        private static string[] languages;

        /// <summary>
        /// Counts the loads, so anything holding a translated string it built itself can tell
        /// that the language has changed under it and build it again.
        /// </summary>
        internal static int Revision { get; private set; }

        /// <summary>
        /// Feeds the file to the game's loader every time a language is set up - on startup, from
        /// the Localization constructor, and again whenever the language is changed, which wipes
        /// every translation first. Nothing here can stop the game loading its own words: a
        /// missing or unreadable file only leaves the mod's tokens showing as [omp_...].
        /// </summary>
        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        [Always]
        private static class Load
        {
            private static void Postfix(Localization __instance, string language)
            {
                if (__instance == null || !Read() || !Reader())
                {
                    return;
                }
                object asset = null;
                try
                {
                    // A language the file has no column for would load nothing at all, tokens and
                    // all, so it reads the English column instead.
                    asset = textAsset.Invoke(new object[] { csv });
                    loadCsv.Invoke(__instance, new[] { asset, Has(language) ? language : Fallback });
                    Revision++;
                }
                catch (Exception e)
                {
                    OdinsMissingPatchPlugin.Log.LogWarning("could not load " + FileName + ": " + e.Message);
                }
                finally
                {
                    UnityEngine.Object leftover = asset as UnityEngine.Object;
                    if (leftover != null)
                    {
                        UnityEngine.Object.Destroy(leftover);
                    }
                }
            }
        }

        /// <summary>Reads the file once and keeps it, with its header split into the languages it carries.</summary>
        private static bool Read()
        {
            if (languages != null)
            {
                return csv != null;
            }
            languages = new string[0];
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(dir, FileName);
                if (!File.Exists(path))
                {
                    OdinsMissingPatchPlugin.Log.LogWarning(FileName + " missing: " + path);
                    return false;
                }
                csv = File.ReadAllText(path);
                using (StringReader reader = new StringReader(csv))
                {
                    string header = reader.ReadLine();
                    languages = header != null ? header.Split(',') : new string[0];
                }
                for (int i = 0; i < languages.Length; i++)
                {
                    languages[i] = languages[i].Trim().Trim('"');
                }
            }
            catch (Exception e)
            {
                csv = null;
                OdinsMissingPatchPlugin.Log.LogWarning("could not read " + FileName + ": " + e.Message);
            }
            return csv != null;
        }

        private static bool Has(string language)
        {
            foreach (string known in languages)
            {
                if (known == language)
                {
                    return true;
                }
            }
            return false;
        }

        private static ConstructorInfo textAsset;
        private static MethodInfo loadCsv;
        private static bool looked;

        /// <summary>
        /// The game's CSV reader, Localization.LoadCSV(TextAsset, string), and the TextAsset to
        /// hand it. Both are found at runtime: TextAsset lives in a module built against
        /// netstandard 2.1, which a net472 build cannot name without CS1705 - the same wall
        /// PanelButtons.LoadPng runs into.
        /// </summary>
        private static bool Reader()
        {
            if (looked)
            {
                return loadCsv != null && textAsset != null;
            }
            looked = true;
            Type asset = Type.GetType("UnityEngine.TextAsset, UnityEngine.CoreModule");
            if (asset != null)
            {
                textAsset = asset.GetConstructor(new[] { typeof(string) });
                loadCsv = typeof(Localization).GetMethod("LoadCSV", new[] { asset, typeof(string) });
            }
            if (loadCsv == null || textAsset == null)
            {
                OdinsMissingPatchPlugin.Log.LogWarning("Localization.LoadCSV not found; the mod's words stay untranslated");
                return false;
            }
            return true;
        }
    }
}
