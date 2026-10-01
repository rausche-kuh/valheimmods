using System;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Notices that what a pin marks is gone, by asking the world rather than watching it go: a
    /// grave or a rock is destroyed on whichever machine owns it and merely vanishes on the
    /// others. Each sweep looks at the watched pins within range of the player whose area is
    /// loaded and asks whether the thing is still there. A pin that comes up missing on two sweeps
    /// in a row is handed on; one that is found, or whose area is not loaded, starts over. Each
    /// kind of pin keeps its own sweep, so their marks never mix.
    /// </summary>
    internal sealed class PinSweep
    {
        /// <summary>Pins found missing on the last sweep; a second sweep in a row decides.</summary>
        private readonly HashSet<Minimap.PinData> missing = new HashSet<Minimap.PinData>();

        /// <summary>
        /// One sweep over the map's pins, last first so <paramref name="gone"/> may remove the pin
        /// from the map. Range is measured in 3D from origin, so a pin up among the dungeon
        /// interiors is only ever checked from inside that dungeon.
        /// </summary>
        internal void Run(Minimap map, Vector3 origin, float range, Func<Minimap.PinData, bool> watched,
            Func<Vector3, bool> present, Action<Minimap.PinData> gone)
        {
            if (map == null || ZNetScene.instance == null)
            {
                return;
            }
            for (int i = map.m_pins.Count - 1; i >= 0; i--)
            {
                Minimap.PinData pin = map.m_pins[i];
                if (!watched(pin) || (pin.m_pos - origin).sqrMagnitude > range * range)
                {
                    continue;
                }
                if (!ZNetScene.instance.IsAreaReady(pin.m_pos) || present(pin.m_pos))
                {
                    missing.Remove(pin);
                    continue;
                }
                if (missing.Add(pin))
                {
                    continue;
                }
                missing.Remove(pin);
                gone(pin);
            }
        }
    }
}
