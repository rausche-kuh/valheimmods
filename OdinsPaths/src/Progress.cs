using System;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// What a growth is doing, for the people waiting on it: a bar at the top of the screen for
    /// the host (the server's own player, who carries the frame cost), and a line in every
    /// player's message log when it starts, as each road is laid, and when it is done - sent as
    /// the game's own "ShowMessage", so players without the mod see them too.
    ///
    /// Also the frame watch: the slowest frame of each job and the stage it fell in, for the log,
    /// so a stutter can be put down to the search, the terrain writing or the clearing.
    ///
    /// A job is split into stages, each a share of it (<see cref="Stage"/>); the stage's own
    /// progress comes from a poll (a search's <see cref="PathSearch.Fraction"/>) or from
    /// <see cref="Set"/> (the writer's and the clearing's zones).
    /// </summary>
    internal static class Progress
    {
        /// <summary>Frames slower than this count as a hitch in the log.</summary>
        private const float HitchSeconds = 0.05f;

        /// <summary>Location prefab -> the game's name for what is there, as a translation token.</summary>
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "Eikthyrnir", "$enemy_eikthyr" },
            { "GDKing", "$enemy_gdking" },
            { "Bonemass", "$enemy_bonemass" },
            { "Dragonqueen", "$enemy_dragon" },
            { "GoblinKing", "$enemy_goblinking" },
            { "Mistlands_DvergrBossEntrance1", "$enemy_seekerqueen" },
            { "FaderLocation", "$enemy_fader" },
            { "DN_Bossroom", "$enemy_frozenking" },
            // Read out of the game's strings, 2026-09-25: Mörkhalla, the Winding Tunnels, the Forge of Potential.
            { "MorkBorg", "$location_morkhalla" },
            { "TheHole01", "$location_thehole" },
            { "AncientUpgradeStation", "$piece_upgradestation" },
            // No token found for these in the game's strings: the English name, shown as it is.
            { "Mistlands_DvergrTownEntrance1", "Infected mine" },
            { "Mistlands_DvergrTownEntrance2", "Infected mine" },
            { "CharredFortress", "Charred fortress" },
            { "NorthVillage", "Deep North village" },
            { "Vendor_BlackForest", "$npc_haldor" },
            { "Hildir_camp", "$npc_hildir" },
            { "BogWitch_Camp", "$npc_bogwitch" },
        };

        private static bool active;
        /// <summary>The world the growth runs in: one left mid-way leaves no bar behind in the next.</summary>
        private static ZDOMan activeFor;
        private static int job;
        private static int jobs;
        private static string title = "";
        private static string stage = "";
        private static float from;
        private static float to;
        private static float fraction;
        private static Func<float> poll;

        private static float worstFrame;
        private static string worstStage = "";
        private static int hitches;
        /// <summary>Garbage collections during the job, and how many of the hitches had one.</summary>
        private static int collections;
        private static int hitchesCollected;
        private static int collectionsSeen;
        /// <summary>Per stage of the job: wall time and frames spent in it, for the log; the side paths' stages summed.</summary>
        private static readonly Dictionary<string, float> stageSeconds = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> stageFrames = new Dictionary<string, int>();
        private static string timedStage = "";
        private static float stageStarted;

        private static GUIStyle label;
        private static Texture2D back;
        private static Texture2D fill;

        public static bool Active => active && activeFor == ZDOMan.instance;

        /// <summary>A growth (or a dev lay) of this many jobs starts.</summary>
        public static void Begin(int count)
        {
            active = true;
            activeFor = ZDOMan.instance;
            job = 0;
            jobs = Mathf.Max(count, 1);
            title = "";
            Stage("", 0f, 0f);
        }

        /// <summary>The job count as planned now; a growth plans afresh after each job.</summary>
        public static void Jobs(int count)
        {
            jobs = Mathf.Max(count, job + 1);
        }

        /// <summary>Job index (0 based) starts, laying a road to what.</summary>
        public static void Job(int index, string what)
        {
            job = index;
            title = what;
            worstFrame = 0f;
            worstStage = "";
            hitches = 0;
            collections = 0;
            hitchesCollected = 0;
            collectionsSeen = GC.CollectionCount(0);
            stageSeconds.Clear();
            stageFrames.Clear();
            timedStage = "";
            stageStarted = Time.realtimeSinceStartup;
            Stage("", 0f, 0f);
        }

        /// <summary>A stage of the job, taking it from one share to another; poll, if given, is its own 0 to 1.</summary>
        public static void Stage(string text, float start, float end, Func<float> fractionOf = null)
        {
            CloseStage();
            timedStage = text;
            stage = text;
            from = start;
            to = end;
            fraction = 0f;
            poll = fractionOf;
        }

        public static void Set(float value)
        {
            fraction = Mathf.Clamp01(value);
        }

        public static void End()
        {
            active = false;
            poll = null;
        }

        /// <summary>
        /// The slowest frame of the job so far, where it fell, and how many were over 50 ms; then
        /// the wall time and frames of each stage, longest first - for the log.
        /// </summary>
        public static string Frames()
        {
            CloseStage();
            List<string> order = new List<string>(stageSeconds.Keys);
            order.Sort((a, b) => stageSeconds[b].CompareTo(stageSeconds[a]));
            System.Text.StringBuilder times = new System.Text.StringBuilder();
            foreach (string name in order)
            {
                times.Append(times.Length > 0 ? ", " : "").Append(name.Length > 0 ? name : "between stages").Append(' ')
                    .Append(stageSeconds[name].ToString("F1")).Append(" s / ")
                    .Append(stageFrames.TryGetValue(name, out int frames) ? frames : 0).Append(" frames");
            }
            return "slowest frame " + (worstFrame * 1000f).ToString("F0") + " ms"
                + (worstStage.Length > 0 ? " (" + worstStage + ")" : "") + ", " + hitches + " over "
                + (HitchSeconds * 1000f).ToString("F0") + " ms (" + hitchesCollected + " with a garbage collection), "
                + collections + " collections; time: " + times;
        }

        /// <summary>Adds the wall time since the last stage began to that stage.</summary>
        private static void CloseStage()
        {
            float now = Time.realtimeSinceStartup;
            stageSeconds.TryGetValue(timedStage, out float seconds);
            stageSeconds[timedStage] = seconds + now - stageStarted;
            stageStarted = now;
        }

        /// <summary>A line in every player's message log, if the settings allow.</summary>
        public static void Announce(string text)
        {
            if (!OdinsPathsPlugin.Announce.Value || ZRoutedRpc.instance == null)
            {
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)MessageHud.MessageType.TopLeft, text);
        }

        /// <summary>What a location group is called in the game, in the server's language; its prefab name if unknown.</summary>
        public static string NameOf(string location)
        {
            if (Names.TryGetValue(location, out string token) && Localization.instance != null)
            {
                string name = Localization.instance.Localize(token);
                if (name.Length > 0 && !name.StartsWith("[") && !name.StartsWith("$"))
                {
                    return name;
                }
            }
            return location;
        }

        /// <summary>From the plugin's Update: the frame watch.</summary>
        public static void Tick()
        {
            if (!Active)
            {
                return;
            }
            float dt = Time.unscaledDeltaTime;
            int seen = GC.CollectionCount(0);
            bool collected = seen != collectionsSeen;
            collections += seen - collectionsSeen;
            collectionsSeen = seen;
            stageFrames.TryGetValue(timedStage, out int frames);
            stageFrames[timedStage] = frames + 1;
            if (dt > HitchSeconds)
            {
                hitches++;
                hitchesCollected += collected ? 1 : 0;
            }
            if (dt > worstFrame)
            {
                worstFrame = dt;
                worstStage = stage;
            }
        }

        /// <summary>From the plugin's OnGUI: the bar, for a player on the server.</summary>
        public static void Draw()
        {
            if (!Active || !OdinsPathsPlugin.ShowProgress.Value || Player.m_localPlayer == null || Hud.IsUserHidden())
            {
                return;
            }
            if (label == null || back == null || fill == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
                label.normal.textColor = new Color(1f, 0.93f, 0.8f);
                back = Solid(new Color(0f, 0f, 0f, 0.55f));
                fill = Solid(new Color(0.85f, 0.65f, 0.3f, 0.9f));
            }
            float inStage = poll != null ? Mathf.Clamp01(poll()) : fraction;
            float inJob = from + (to - from) * inStage;
            float total = Mathf.Clamp01((job + inJob) / jobs);

            Matrix4x4 saved = GUI.matrix;
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            const float width = 440f;
            float x = (Screen.width / scale - width) / 2f;
            const float y = 64f;
            GUI.DrawTexture(new Rect(x, y, width, 62f), back);
            GUI.Label(new Rect(x, y + 2f, width, 22f),
                "Odin's Paths - road " + (job + 1) + " of " + jobs + (title.Length > 0 ? ": " + title : ""), label);
            GUI.DrawTexture(new Rect(x + 12f, y + 27f, (width - 24f) * total, 8f), fill);
            GUI.Label(new Rect(x, y + 38f, width, 22f),
                (stage.Length > 0 ? stage + " " : "") + Mathf.FloorToInt(inStage * 100f) + "%", label);
            GUI.matrix = saved;
        }

        private static Texture2D Solid(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }

    public partial class OdinsPathsPlugin
    {
        void OnGUI()
        {
            Progress.Draw();
        }
    }
}
