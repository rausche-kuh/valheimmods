using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// The colours the mod draws with itself, so the same meaning always has the same colour.
    /// The game's own UI colours they sit beside are listed in docs/conventions.md.
    /// </summary>
    internal static class Palette
    {
        /// <summary>A favourite, the power you carry, a chest that just took something.</summary>
        internal static readonly Color Gold = new Color(1f, 0.8f, 0.3f, 1f);

        /// <summary>"A chest is involved": an amount the chests pay for, a chest's marked kinds.</summary>
        internal static readonly Color ChestYellow = new Color(1f, 0.84f, 0.3f, 1f);

        /// <summary><see cref="ChestYellow"/> as a text markup tag.</summary>
        internal const string ChestYellowTag = "<color=#ffd64d>";
    }
}
