using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// UI pieces more than one mod draws with: the PNG icons shipped beside the DLL, and the
    /// tooltip window the inventory uses, for a control that was made without one.
    /// </summary>
    internal static class UiAssets
    {
        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        /// <summary>
        /// The icon named by a PNG shipped with the DLL, loaded once. Null when the file is
        /// missing or unreadable, which leaves the button blank but working.
        /// The zip carries the PNGs in icons/, but a mod manager may flatten that folder into the
        /// plugin directory on install (Gale does), so both places are tried.
        /// </summary>
        internal static Sprite Icon(string name)
        {
            if (icons.TryGetValue(name, out Sprite sprite))
            {
                return sprite;
            }
            sprite = null;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = IconPath(dir, name);
                if (path != null)
                {
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (LoadPng(texture, File.ReadAllBytes(path)))
                    {
                        texture.filterMode = FilterMode.Bilinear;
                        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f), 100f);
                    }
                }
                else
                {
                    TweakHost.Log.LogWarning("icon missing: " + name + ".png, looked in "
                        + Path.Combine(dir, "icons") + " and " + dir);
                }
            }
            catch (Exception e)
            {
                TweakHost.Log.LogWarning("could not load icon " + name + ": " + e.Message);
            }
            icons[name] = sprite;
            return sprite;
        }

        /// <summary>
        /// The first place the named PNG exists: icons/ under the DLL's directory, then the
        /// directory itself. Null when it is in neither.
        /// </summary>
        private static string IconPath(string dir, string name)
        {
            string file = name + ".png";
            string[] candidates = { Path.Combine(Path.Combine(dir, "icons"), file), Path.Combine(dir, file) };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static MethodInfo loadImage;

        /// <summary>
        /// The game's PNG decoder, ImageConversion.LoadImage(Texture2D, byte[]). Its module is
        /// built against netstandard 2.1, which a net472 build cannot reference, so it is
        /// found at runtime instead of at compile time.
        /// </summary>
        private static bool LoadPng(Texture2D texture, byte[] png)
        {
            if (loadImage == null)
            {
                Type conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                loadImage = conversion != null
                    ? conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) })
                    : null;
                if (loadImage == null)
                {
                    TweakHost.Log.LogWarning("ImageConversion.LoadImage not found; icons stay blank");
                    return false;
                }
            }
            return (bool)loadImage.Invoke(null, new object[] { texture, png });
        }

        /// <summary>
        /// The tooltip prefab the inventory slots use, for a button that was copied without one.
        /// </summary>
        internal static GameObject TooltipPrefab(InventoryGui gui)
        {
            InventoryGrid grid = gui.m_playerGrid;
            GameObject prefab = grid != null ? grid.m_elementPrefab : null;
            InventoryElement element = prefab != null ? prefab.GetComponent<InventoryElement>() : null;
            if (element != null && element.m_tooltip != null && element.m_tooltip.m_tooltipPrefab != null)
            {
                return element.m_tooltip.m_tooltipPrefab;
            }
            foreach (UITooltip tip in gui.GetComponentsInChildren<UITooltip>(true))
            {
                if (tip.m_tooltipPrefab != null)
                {
                    return tip.m_tooltipPrefab;
                }
            }
            return null;
        }
    }
}
