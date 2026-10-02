using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valheim.UI;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A Forsaken powers category in the radial menu: opening it shows one element per boss power
    /// this character has unlocked, each with the power's own icon, and picking one makes it the
    /// power the power key casts - without the walk back to the sacrificial stones.
    ///
    /// Only the choice is moved. The power is still cast by the game's own key, the cooldown is
    /// still the one you were already on (switching does not reset it, exactly as switching at the
    /// stones does not), and a boss you have not beaten is not in the list - so the category is
    /// not there at all until the first one falls.
    /// </summary>
    internal sealed class PowerPicker : Tweak
    {
        internal static readonly PowerPicker Instance = new PowerPicker();

        private PowerPicker() { }

        internal override string Section => "Power Picker";

        protected override string Summary =>
            "A Forsaken powers category in the radial menu, holding every boss power you have " +
            "unlocked; picking one makes it yours, without the walk back to the stones.";

        // ---- What is unlocked ------------------------------------------------------------------

        /// <summary>
        /// The key each power's boss sets when it dies, read off the boss prefabs themselves -
        /// none of these follow from the power's name, and the game keeps the pairing nowhere a
        /// mod can reach it: the stone at the temple names the power, the boss names the key, and
        /// only the trophy in between ties the two together. The Deep North king sets
        /// <c>defeated_frozenking</c> but has neither stone nor power yet, so it is not here.
        /// </summary>
        private static readonly Dictionary<string, string> BossKeys = new Dictionary<string, string>
        {
            { "GP_Eikthyr", "defeated_eikthyr" },
            { "GP_TheElder", "defeated_gdking" },
            { "GP_Bonemass", "defeated_bonemass" },
            { "GP_Moder", "defeated_dragon" },
            { "GP_Yagluth", "defeated_goblinking" },
            { "GP_Queen", "defeated_queen" },
            { "GP_Fader", "defeated_fader" },
        };

        /// <summary>
        /// The boss powers this character may choose from, in the game's own order. A status
        /// effect with a cooldown is a guardian power - the one field only a guardian power fills
        /// in - and it is unlocked once its boss has fallen, which is the same moment the stone
        /// at the temple would start offering it.
        ///
        /// A dying boss writes its key twice: into the world, and into the unique keys of every
        /// character standing near enough to see it. Both are read, so the power is there whether
        /// you killed the boss in this world or brought the character from the world where you
        /// did. A power the character is already carrying, or has taken at a stone before, counts
        /// on its own - <c>Player.SetGuardianPower</c> leaves the power's name among those same
        /// unique keys - which is what keeps a power another mod handed out from going missing.
        /// </summary>
        private static List<StatusEffect> Unlocked()
        {
            List<StatusEffect> powers = new List<StatusEffect>();
            Player player = Player.m_localPlayer;
            ObjectDB db = ObjectDB.instance;
            if (player == null || db == null)
            {
                return powers;
            }
            string current = player.GetGuardianPowerName();
            foreach (StatusEffect effect in db.m_StatusEffects)
            {
                if (effect != null && effect.m_cooldown > 0f
                    && (effect.name == current || player.HaveUniqueKey(effect.name)
                        || BossDefeated(player, effect.name)))
                {
                    powers.Add(effect);
                }
            }
            return powers;
        }

        /// <summary>Whether a power's boss has fallen, to this world or to this character.</summary>
        private static bool BossDefeated(Player player, string power)
        {
            string key;
            if (!BossKeys.TryGetValue(power, out key))
            {
                return false;
            }
            ZoneSystem zones = ZoneSystem.instance;
            return (zones != null && zones.GetGlobalKey(key)) || player.HaveUniqueKey(key);
        }

        /// <summary>The power the character is carrying, or "" - the game's own answer.</summary>
        private static string Current()
        {
            Player player = Player.m_localPlayer;
            return player != null ? player.GetGuardianPowerName() : "";
        }

        /// <summary>
        /// Hands the power over, the same call the stone's own delayed activation makes. The
        /// cooldown is a timer on the player rather than on the power, so it is left alone and a
        /// switch mid cooldown buys nothing.
        /// </summary>
        private static void Pick(StatusEffect power)
        {
            Player player = Player.m_localPlayer;
            if (player == null || power == null || player.GetGuardianPowerName() == power.name)
            {
                return;
            }
            player.SetGuardianPower(power.name);
            player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(power.m_name));
        }

        // ---- The category ----------------------------------------------------------------------

        /// <summary>
        /// Adds the category to the top level of the radial menu. Every config builds its own
        /// list of elements and hands it to <c>ConstructRadial</c>, so a prefix there is where a
        /// list can still be added to; the main menu is told apart by the config the radial is
        /// currently opening. The list is built afresh on every open, so nothing is cached and a
        /// power unlocked since is simply there the next time.
        /// </summary>
        [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.ConstructRadial))]
        private static class TopCategory
        {
            private static void Prefix(RadialBase __instance, List<RadialMenuElement> elements)
            {
                if (!Instance.On || elements == null || !(__instance.CurrentConfig is ValheimRadialConfig))
                {
                    return;
                }
                // A character who has not beaten a boss yet has nothing to choose between, and an
                // empty category would only be a dead end.
                if (RadialData.SO == null || RadialData.SO.GroupElement == null || Unlocked().Count == 0)
                {
                    return;
                }
                GroupElement group = Object.Instantiate(RadialData.SO.GroupElement);
                // The main config is its own back config: picking Back from the powers returns
                // to the ring the category was opened from, as every other group does.
                group.Init(new PowerConfig(), __instance.CurrentConfig, __instance);
                elements.Add(group);
            }
        }

        /// <summary>
        /// The category itself and the sub menu behind it. A radial config is a plain
        /// <see cref="IRadialConfig"/> - the game's own are ScriptableObjects only because they
        /// are authored in the editor - and its <c>InitRadialConfig</c> is called by the radial
        /// each time the sub menu opens.
        /// </summary>
        private sealed class PowerConfig : IRadialConfig
        {
            /// <summary>Written straight onto the ring's label, so it is translated here.</summary>
            public string LocalizedName => Localization.instance.Localize("$omp_forsaken_powers");

            /// <summary>
            /// The icon of the power you are carrying, so the top level ring already says which
            /// one that is; the first unlocked power's icon while none is set.
            /// </summary>
            public Sprite Sprite
            {
                get
                {
                    List<StatusEffect> powers = Unlocked();
                    string current = Current();
                    foreach (StatusEffect power in powers)
                    {
                        if (power.name == current)
                        {
                            return power.m_icon;
                        }
                    }
                    return powers.Count > 0 ? powers[0].m_icon : null;
                }
            }

            public void InitRadialConfig(RadialBase radial)
            {
                List<RadialMenuElement> elements = new List<RadialMenuElement>();
                string current = Current();
                foreach (StatusEffect power in Unlocked())
                {
                    RadialMenuElement element = Element(power, power.name == current);
                    if (element != null)
                    {
                        elements.Add(element);
                    }
                }
                radial.ConstructRadial(elements);
            }

            /// <summary>
            /// One power, built from the emote element - the game's own icon-and-name element,
            /// which is what an emote in the emote sub menu is. Its Init is for an emote, so the
            /// three things an element is - what it is called, what it shows and what it does -
            /// are set here instead.
            ///
            /// The radial keeps whatever was last used as an element of its own and puts it back
            /// in the top level ring, so the element outlives the menu it was built for; picking
            /// marks it as the active one there and then, rather than leaving the ring showing a
            /// power that has since become yours as though it had not.
            /// </summary>
            private static RadialMenuElement Element(StatusEffect power, bool active)
            {
                EmoteElement element = RadialData.SO.EmoteElement != null
                    ? Object.Instantiate(RadialData.SO.EmoteElement) : null;
                if (element == null)
                {
                    return null;
                }
                element.SubTitle = Localization.instance.Localize(power.GetTooltipString());
                element.CloseOnInteract = () => true;
                element.Interact = delegate
                {
                    Pick(power);
                    Mark(element, power, true);
                    return true;
                };
                Mark(element, power, active);
                return element;
            }

            /// <summary>Names an element and tints its icon, gold while it is the power you carry.</summary>
            private static void Mark(RadialMenuElement element, StatusEffect power, bool active)
            {
                string name = Localization.instance.Localize(power.m_name);
                element.Name = active ? name + " (active)" : name;
                Image icon = element.Icon;
                if (icon != null)
                {
                    icon.gameObject.SetActive(power.m_icon != null);
                    icon.sprite = power.m_icon;
                    icon.color = active ? Palette.Gold : Color.white;
                }
            }
        }
    }
}
