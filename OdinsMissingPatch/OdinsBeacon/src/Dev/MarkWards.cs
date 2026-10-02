using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OdinsMissingPatch
{
    // Dev only: src/Dev/ is compiled into Debug builds alone, so this never ships.
    internal sealed partial class PlayerMarks
    {
        /// <summary>
        /// Wards stand in for other players, so the marks can be tried in a world of one: every
        /// loaded ward gets a dot by the same rules a player does. On by default in a Debug build;
        /// omp_marks_wards switches it.
        /// </summary>
        private static bool markWards = true;

        static partial void AddDevTargets(List<Vector3> targets)
        {
            if (!markWards)
            {
                return;
            }
            foreach (PrivateArea ward in PrivateArea.m_allAreas)
            {
                if (ward != null)
                {
                    targets.Add(ward.transform.position + Vector3.up * 1.5f);
                }
            }
        }

        private const string MarkUsage =
            "omp_mark [name value] | reset - tunes the player marks live. Names: size, outline, ring, " +
            "curve, far, rugged, bumps, sparks, sparkrate, sparktravel, sparksize (numbers); centre, edge, faredge, ringcolor, outlinecolor (hex RRGGBB). " +
            "Without arguments it prints every value in the same form.";

        /// <summary>Sets one value of the mark's look, or returns false when the name or value is wrong.</summary>
        private static bool Tune(string name, string value)
        {
            PlayerMarks marks = Instance;
            float number;
            bool isNumber = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            Color color;
            bool isColor = ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out color);
            switch (name)
            {
                case "size" when isNumber: marks.size.Value = number; return true;
                case "outline" when isNumber: outlineWidth = number; break;
                case "ring" when isNumber: ringWidth = number; break;
                case "curve" when isNumber: gradientCurve = number; break;
                case "far" when isNumber: farScale = number; return true;
                case "rugged" when isNumber: ruggedness = Mathf.Clamp(number, 0f, 0.5f); break;
                case "bumps" when isNumber: bumps = Mathf.Clamp(Mathf.RoundToInt(number), 1, 24); break;
                case "sparks" when isNumber: sparkCount = Mathf.Clamp(Mathf.RoundToInt(number), 0, 32); return true;
                case "sparkrate" when isNumber: sparkRate = number; return true;
                case "sparktravel" when isNumber: sparkTravel = number; return true;
                case "sparksize" when isNumber: sparkSize = number; return true;
                case "centre" when isColor: marks.centreColor.Value = color; return true;
                case "edge" when isColor: marks.edgeColor.Value = color; return true;
                case "faredge" when isColor: marks.farEdgeColor.Value = color; return true;
                case "ringcolor" when isColor: ringColor = color; break;
                case "outlinecolor" when isColor: outlineColor = color; break;
                default: return false;
            }
            shapeChanged = true;
            return true;
        }

        private static void Reset()
        {
            PlayerMarks marks = Instance;
            foreach (ConfigEntryBase entry in new ConfigEntryBase[] { marks.size, marks.centreColor, marks.edgeColor, marks.farEdgeColor })
            {
                entry.BoxedValue = entry.DefaultValue;
            }
            outlineWidth = 0.21f;
            ringWidth = 0.07f;
            gradientCurve = 1f;
            farScale = 0.75f;
            ruggedness = 0.2f;
            bumps = 5;
            sparkCount = 5;
            sparkRate = 0.7f;
            sparkTravel = 0.9f;
            sparkSize = 0.22f;
            ringColor = new Color(0.882f, 0.655f, 0.4f, 1f);
            outlineColor = new Color(0.19f, 0.13f, 0.08f, 1f);
            shapeChanged = true;
        }

        /// <summary>Every tunable value, as the arguments that would set it.</summary>
        private static string Values()
        {
            PlayerMarks marks = Instance;
            return string.Format(CultureInfo.InvariantCulture,
                "size {0} outline {1} ring {2} curve {3} far {4} centre {5} edge {6} faredge {7} ringcolor {8} outlinecolor {9} " +
                "sparks {10} sparkrate {11} sparktravel {12} sparksize {13} rugged {14} bumps {15}",
                marks.size.Value, outlineWidth, ringWidth, gradientCurve, farScale,
                ColorUtility.ToHtmlStringRGB(marks.centreColor.Value),
                ColorUtility.ToHtmlStringRGB(marks.edgeColor.Value),
                ColorUtility.ToHtmlStringRGB(marks.farEdgeColor.Value),
                ColorUtility.ToHtmlStringRGB(ringColor),
                ColorUtility.ToHtmlStringRGB(outlineColor),
                sparkCount, sparkRate, sparkTravel, sparkSize, ruggedness, bumps);
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        [LoadHook]
        private static class Commands
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("omp_marks_wards",
                    "switches the player marks on wards, for trying them alone",
                    args =>
                    {
                        markWards = !markWards;
                        args.Context.AddString("player marks on wards: " + (markWards ? "on" : "off"));
                    });
                new Terminal.ConsoleCommand("omp_mark", MarkUsage,
                    args =>
                    {
                        bool reset = args.Length == 2 && args[1] == "reset";
                        if (reset)
                        {
                            Reset();
                        }
                        bool understood = args.Length == 1 || reset ||
                            (args.Length == 3 && Tune(args[1].ToLowerInvariant(), args[2]));
                        args.Context.AddString(understood ? Values() : MarkUsage);
                    });
            }
        }
    }
}
