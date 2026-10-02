using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Reads a configured KeyboardShortcut through ZInput, the game's own input layer.
    /// KeyboardShortcut.IsDown itself refuses to fire while any key outside the combination is
    /// held, which in this game means "not while walking"; these only ask for the keys named.
    /// </summary>
    internal static class Hotkeys
    {
        /// <summary>True on the frame the main key goes down with every modifier held.</summary>
        internal static bool Pressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey, logWarning: false))
            {
                return false;
            }
            return ModifiersHeld(shortcut);
        }

        /// <summary>True while the main key and every modifier are held.</summary>
        internal static bool Held(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKey(shortcut.MainKey, logWarning: false))
            {
                return false;
            }
            return ModifiersHeld(shortcut);
        }

        /// <summary>
        /// The keys of a chord as the player's keyboard names them ("Left Shift + ."), each
        /// shortcut's modifiers before its main key, in the order given; empty when none is set.
        /// </summary>
        internal static string Describe(params KeyboardShortcut[] chord)
        {
            List<string> keys = new List<string>();
            foreach (KeyboardShortcut shortcut in chord)
            {
                if (shortcut.MainKey == KeyCode.None)
                {
                    continue;
                }
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    keys.Add(KeyName(modifier));
                }
                keys.Add(KeyName(shortcut.MainKey));
            }
            return string.Join(" + ", keys.ToArray());
        }

        /// <summary>The input system's name for a key, on the current layout; the enum name when it has none.</summary>
        private static string KeyName(KeyCode key)
        {
            string name = ZInput.KeyCodeToDisplayName(key);
            // A key without a control comes back as a "$KeyCode ..." complaint.
            return string.IsNullOrEmpty(name) || name.StartsWith("$") ? key.ToString() : name;
        }

        private static bool ModifiersHeld(KeyboardShortcut shortcut)
        {
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!ZInput.GetKey(modifier, logWarning: false))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
