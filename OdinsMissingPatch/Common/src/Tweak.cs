using BepInEx.Configuration;
using System;
using System.Collections.Generic;

namespace OdinsMissingPatch
{
    /// <summary>
    /// One change to the game: the config section it owns, plus the Harmony patches that carry
    /// it out, nested inside it. Only a tweak that is on gets its patches (see <see cref="Patcher"/>);
    /// they ask <see cref="On"/> before they do anything, so switching it off mid game leaves them
    /// inert until the next launch.
    /// </summary>
    internal abstract class Tweak
    {
        // A multiplier is applied straight to a game value, so keep a typo from breaking the game.
        private const float MinMultiplier = 0.1f;
        private const float MaxMultiplier = 20f;

        private const string RestartNote =
            " If it was off when the game started, switching it on may only take effect after a restart.";

        private ConfigEntry<bool> enabled;

        private readonly List<Action> settingHandlers = new List<Action>();

        /// <summary>The config section this tweak owns, e.g. "Station Range".</summary>
        internal abstract string Section { get; }

        /// <summary>One line on what switching it on does - the description of Enabled.</summary>
        protected abstract string Summary { get; }

        /// <summary>
        /// The shared helpers in Common whose patches this tweak needs, such as
        /// <see cref="Danger"/>: a patch class marked <c>[Serves(typeof(Danger))]</c> goes in with
        /// any tweak that lists Danger here (see <see cref="ServesAttribute"/>).
        /// </summary>
        internal virtual Type[] Uses => Type.EmptyTypes;

        /// <summary>Binds the tweak's own settings. Enabled is already bound when this runs.</summary>
        protected virtual void Bind(ConfigFile config)
        {
        }

        /// <summary>
        /// Switched on and patched. False until <see cref="Setup"/> has run and the patches are in,
        /// so an early patch is simply inert, and false for good once a patch of it has failed.
        /// </summary>
        internal bool On => Patched && Wanted;

        /// <summary>What the config asks for, patched or not.</summary>
        internal bool Wanted => enabled != null && enabled.Value;

        internal ConfigEntry<bool> Enabled => enabled;

        /// <summary>Whether its patches are in; set by <see cref="Patcher"/>.</summary>
        internal bool Patched { get; set; }

        /// <summary>A patch it needs could not be applied, so it stays off for the session.</summary>
        internal bool Broken { get; set; }

        internal void Setup(ConfigFile config, bool mayNeedRestart)
        {
            enabled = config.Bind(Section, "Enabled", true, Summary + (mayNeedRestart ? RestartNote : ""));
            Bind(config);
        }

        /// <summary>
        /// Runs every <see cref="OnSettingChanged"/> handler, for when <see cref="On"/> changed
        /// without a setting changing: the patches going in, or a broken tweak going out.
        /// </summary>
        internal void Refresh()
        {
            foreach (Action handler in settingHandlers)
            {
                handler();
            }
        }

        /// <summary>A multiplier on a vanilla value, clamped to something the game survives.</summary>
        protected ConfigEntry<float> BindMultiplier(ConfigFile config, string key, float value, string description)
        {
            return config.Bind(Section, key, value, new ConfigDescription(
                description, new AcceptableValueRange<float>(MinMultiplier, MaxMultiplier)));
        }

        /// <summary>
        /// A comma separated list setting. <paramref name="apply"/> gets the trimmed, non-empty
        /// items once now and again whenever the entry changes, so a caller parses into its own
        /// set there rather than on every use.
        /// </summary>
        protected ConfigEntry<string> BindList(ConfigFile config, string key, string value, string description,
            Action<List<string>> apply)
        {
            ConfigEntry<string> entry = config.Bind(Section, key, value, description);
            entry.SettingChanged += (sender, args) => apply(Items(entry.Value));
            apply(Items(entry.Value));
            return entry;
        }

        /// <summary>The trimmed, non-empty items of a comma separated list.</summary>
        internal static List<string> Items(string list)
        {
            List<string> items = new List<string>();
            foreach (string raw in (list ?? "").Split(','))
            {
                string item = raw.Trim();
                if (item.Length > 0)
                {
                    items.Add(item);
                }
            }
            return items;
        }

        /// <summary>
        /// Runs the handler whenever an entry in this tweak's section changes - Enabled included.
        /// Only tweaks that write game state on load need it; one that reads its setting where it
        /// is used follows the config file by itself. It also runs when the tweak's patches go in
        /// or come out, so it must bring what is already loaded up to date from any state.
        /// </summary>
        protected void OnSettingChanged(ConfigFile config, Action handler)
        {
            settingHandlers.Add(handler);
            config.SettingChanged += (sender, args) =>
            {
                if (args.ChangedSetting.Definition.Section == Section)
                {
                    handler();
                }
            };
        }
    }
}
