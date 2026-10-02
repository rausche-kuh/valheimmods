using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OdinsMissingPatch
{
    /// <summary>
    /// A small round mark over every other player you cannot easily see: behind a hill, inside a
    /// house, or simply far off. It sits over their head on screen, or on the screen's edge in their
    /// direction when they are off screen, and turns from its near colour to its far colour with
    /// distance. It is fully opaque, in the game's own UI colours - a yellow centre glowing out to
    /// orange - and a thin white ring inside a dark brown outline keep it readable on snow, sand, ash and night sky alike: subtle by its size
    /// alone, never by fading.
    /// </summary>
    internal sealed partial class PlayerMarks : Tweak
    {
        internal static readonly PlayerMarks Instance = new PlayerMarks();

        private PlayerMarks() { }

        private ConfigEntry<float> hideWithin;
        private ConfigEntry<float> showFrom;
        private ConfigEntry<float> farFrom;
        private ConfigEntry<float> hideBeyond;
        private ConfigEntry<float> size;
        private ConfigEntry<Color> centreColor;
        private ConfigEntry<Color> edgeColor;
        private ConfigEntry<Color> farEdgeColor;
        private ConfigEntry<bool> offScreen;
        private ConfigEntry<bool> sparks;

        internal override string Section => "Player Marks";

        protected override string Summary =>
            "A small round mark shows where every other player is, once they are out of sight or " +
            "far enough away.";

        protected override void Bind(ConfigFile config)
        {
            hideWithin = config.Bind(Section, "HideWithin", 10f,
                "Metres within which a player never gets a mark, seen or not.");
            showFrom = config.Bind(Section, "ShowFrom", 50f,
                "Metres from which a player always gets a mark. Between HideWithin and this, only a " +
                "player hidden behind terrain or a building gets one.");
            farFrom = config.Bind(Section, "FarFrom", 400f,
                "Metres at which the mark has turned FarEdgeColor and shrunk to its smallest size.");
            hideBeyond = config.Bind(Section, "HideBeyond", 0f,
                "Metres beyond which a player gets no mark at all. 0 shows every player, however far.");
            // The colour keys were renamed twice (NearColor / FarColor, then MarkColor /
            // FarMarkColor) as the look changed, so a config written earlier picks up the new look.
            size = config.Bind(Section, "MarkSize", 16f, new ConfigDescription(
                "Diameter of the whole mark, rings included, in HUD units (about pixels at 1080p). " +
                "Everything in it scales together.",
                new AcceptableValueRange<float>(6f, 64f)));
            centreColor = config.Bind(Section, "CentreColor", new Color(0.667f, 0.502f, 0.318f, 1f),
                "Colour at the centre of the mark, fading out to EdgeColor.");
            edgeColor = config.Bind(Section, "EdgeColor", new Color(1f, 0.557f, 0f, 1f),
                "Colour at the edge of the mark, inside the rings. For a far player it moves to the centre.");
            farEdgeColor = config.Bind(Section, "FarEdgeColor", new Color(0.553f, 0.776f, 0.894f, 1f),
                "Colour at the edge of the mark for a player at FarFrom and beyond.");
            offScreen = config.Bind(Section, "ShowOffScreen", true,
                "Keep the mark of a player off screen or behind you on the screen's edge, in their direction.");
            sparks = config.Bind(Section, "Sparks", true,
                "Little sparks drift outward from every mark, in its edge colour.");
        }

        // What blocks the view of a player: what blocks a monster's view in BaseAI, minus viewblock.
        private static int blockMask;

        // A hit this close to the target is the target (a ward's own collider), not a wall.
        private const float SelfHit = 0.75f;

        private readonly List<Vector3> targets = new List<Vector3>();
        private readonly List<ZNet.PlayerInfo> publicPlayers = new List<ZNet.PlayerInfo>();
        private readonly HashSet<ZDOID> loaded = new HashSet<ZDOID>();
        private readonly List<Mark> dots = new List<Mark>();
        private Sprite ringSprite;
        private Sprite nearSprite;
        private Sprite farSprite;
        private Sprite sparkSprite;
        private Color[] spriteColors;

        /// <summary>
        /// One mark on screen: the near disc, the far disc laid over it as the player moves away,
        /// and the rings drawn over both edges.
        /// </summary>
        private sealed class Mark
        {
            internal Image Fill;
            internal Image Far;
            internal Image Rings;
            internal readonly List<Image> Sparks = new List<Image>();
        }

        /// <summary>
        /// Adds Debug-only targets (the wards, so the marks can be tried alone). Implemented in
        /// src/Dev/; in a Release build the call is compiled away.
        /// </summary>
        static partial void AddDevTargets(List<Vector3> targets);

        /// <summary>
        /// The head of every other player: the loaded ones where they stand, and past the loaded
        /// area the ones who share their position on the map, where the server last saw them - the
        /// same players the map shows, so nobody who hid from the map shows up here.
        /// </summary>
        private void CollectTargets(Player local)
        {
            targets.Clear();
            loaded.Clear();
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null || player == local)
                {
                    continue;
                }
                loaded.Add(player.GetZDOID());
                if (!player.IsDead())
                {
                    targets.Add(player.GetHeadPoint() + Vector3.up * 0.6f);
                }
            }
            if (ZNet.instance != null)
            {
                publicPlayers.Clear();
                ZNet.instance.GetOtherPublicPlayers(publicPlayers);
                foreach (ZNet.PlayerInfo info in publicPlayers)
                {
                    if (!loaded.Contains(info.m_characterID))
                    {
                        targets.Add(info.m_position + Vector3.up * 2.4f);
                    }
                }
            }
            AddDevTargets(targets);
        }

        /// <summary>Runs after the enemy HUD has placed its bars, on its own canvas.</summary>
        private void Draw(EnemyHud hud)
        {
            int shown = 0;
            UpdateSprites();
            Player local = Player.m_localPlayer;
            Camera camera = Utils.GetMainCamera();
            bool hidden = !On || local == null || camera == null || hud.m_hudRoot == null ||
                Minimap.IsOpen() || InventoryGui.IsVisible();
            if (!hidden)
            {
                CollectTargets(local);
                Vector3 from = local.transform.position;
                foreach (Vector3 target in targets)
                {
                    if (Place(hud, camera, from, target, shown))
                    {
                        shown++;
                    }
                }
            }
            for (int i = shown; i < dots.Count; i++)
            {
                if (dots[i] != null && dots[i].Fill != null)
                {
                    dots[i].Fill.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Puts dot number <paramref name="index"/> over one target, or reports it needs none.</summary>
        private bool Place(EnemyHud hud, Camera camera, Vector3 from, Vector3 target, int index)
        {
            float distance = Vector3.Distance(from, target);
            if (distance < hideWithin.Value || (hideBeyond.Value > 0f && distance > hideBeyond.Value))
            {
                return false;
            }
            if (distance < showFrom.Value && !Blocked(camera.transform.position, target))
            {
                return false;
            }

            Vector3 screen = camera.WorldToScreenPointScaled(target);
            float far = Mathf.InverseLerp(Mathf.Max(showFrom.Value, hideWithin.Value), farFrom.Value, distance);
            float diameter = size.Value * Mathf.Lerp(1f, farScale, far);
            Vector2 centre = new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector2 offset = new Vector2(screen.x, screen.y) - centre;
            bool behind = screen.z < 0f;
            if (behind)
            {
                // Behind the camera the projection is mirrored through the centre.
                offset = -offset;
            }
            Vector2 half = centre - Vector2.one * diameter;
            bool outside = behind || Mathf.Abs(offset.x) > half.x || Mathf.Abs(offset.y) > half.y;
            if (outside)
            {
                if (!offScreen.Value)
                {
                    return false;
                }
                if (offset.sqrMagnitude < 1f)
                {
                    offset = Vector2.down;
                }
                // Slide along the line from the centre until the dot touches the screen's edge.
                float scale = Mathf.Min(
                    offset.x != 0f ? half.x / Mathf.Abs(offset.x) : float.MaxValue,
                    offset.y != 0f ? half.y / Mathf.Abs(offset.y) : float.MaxValue);
                offset *= scale;
            }

            Mark mark = Dot(hud, index);
            // Both discs are opaque, so fading the far one in keeps the mark at full brightness.
            mark.Far.color = new Color(1f, 1f, 1f, far);
            mark.Fill.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            mark.Fill.transform.position = centre + offset;
            mark.Fill.gameObject.SetActive(true);
            Spark(mark, index, diameter, Color.Lerp(edgeColor.Value, farEdgeColor.Value, far));
            return true;
        }

        // The sparks: how many a mark has, how many each one sends out per second, how far they
        // travel and how big they are, both in mark diameters. Tuned in game by omp_mark.
        private static int sparkCount = 5;
        private static float sparkRate = 0.7f;
        private static float sparkTravel = 0.9f;
        private static float sparkSize = 0.22f;

        /// <summary>
        /// Moves the sparks of a mark: each one leaves the outline at an angle of its own, drifts
        /// outward while it fades, and starts again at a new angle. The angle comes from a hash
        /// of the mark, the spark and the round, so no state is kept and no two marks pulse alike.
        /// </summary>
        private void Spark(Mark mark, int index, float diameter, Color color)
        {
            int count = sparks.Value ? sparkCount : 0;
            while (mark.Sparks.Count < count)
            {
                Image spark = NewImage("Spark", mark.Fill.transform, sparkSprite);
                // Under the far disc and the rings: a spark comes out from beneath the outline.
                spark.transform.SetAsFirstSibling();
                mark.Sparks.Add(spark);
            }
            for (int i = 0; i < mark.Sparks.Count; i++)
            {
                Image spark = mark.Sparks[i];
                spark.gameObject.SetActive(i < count);
                if (i >= count)
                {
                    continue;
                }
                float time = Time.time * sparkRate + Hash(index * 31 + i);
                float round = Mathf.Floor(time);
                float age = time - round;
                float angle = Hash(index * 7919 + i * 131 + round) * Mathf.PI * 2f;
                float radius = diameter * (OuterRadius * 0.5f + sparkTravel * age);
                spark.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                spark.rectTransform.sizeDelta = Vector2.one * diameter * sparkSize * (1f - 0.5f * age);
                spark.sprite = sparkSprite;
                color.a = Step01(age * 6f) * (1f - age);
                spark.color = color;
            }
        }

        /// <summary>A number in 0..1 that looks random but is always the same for the same input.</summary>
        private static float Hash(float n)
        {
            float x = Mathf.Sin(n * 12.9898f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        /// <summary>True when terrain, rock or a building stands between the camera and the target.</summary>
        private static bool Blocked(Vector3 eye, Vector3 target)
        {
            if (blockMask == 0)
            {
                blockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            }
            Vector3 line = target - eye;
            float length = line.magnitude;
            return length > SelfHit &&
                Physics.Raycast(eye, line / length, out RaycastHit hit, length, blockMask) &&
                hit.distance < length - SelfHit;
        }

        /// <summary>Mark number <paramref name="index"/>, made under the enemy HUD's root the first time.</summary>
        private Mark Dot(EnemyHud hud, int index)
        {
            while (dots.Count <= index)
            {
                dots.Add(null);
            }
            // The HUD is rebuilt with each world, taking its children with it.
            if (dots[index] == null || dots[index].Fill == null)
            {
                Image fill = NewImage("OdinsMissingPatch_PlayerMark", hud.m_hudRoot.transform, nearSprite);
                fill.transform.SetAsFirstSibling();
                // The far disc and the rings are children stretched over the near disc, so they
                // follow its size and draw on top of it, in that order.
                dots[index] = new Mark
                {
                    Fill = fill,
                    Far = Stretched(NewImage("Far", fill.transform, farSprite)),
                    Rings = Stretched(NewImage("Rings", fill.transform, ringSprite)),
                };
            }
            Mark mark = dots[index];
            mark.Fill.sprite = nearSprite;
            mark.Far.sprite = farSprite;
            mark.Rings.sprite = ringSprite;
            return mark;
        }

        private static Image Stretched(Image image)
        {
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.sizeDelta = Vector2.zero;
            return image;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        // The shape, in parts of the sprite's half width, so the whole mark scales with MarkSize:
        // the outline's outer edge, the outline and the ring inside it, and how far the disc reaches
        // under the ring, so no gap shows between them. Fixed rather than config, but not const:
        // the Debug build's omp_mark command tunes them in game.
        private const float OuterRadius = 0.97f;
        private const float DiscOverlap = 0.04f;
        private static float outlineWidth = 0.21f;
        private static float ringWidth = 0.07f;
        // Radius to the power of this before the step from centre to edge colour: above 1 the
        // centre colour spreads further out.
        private static float gradientCurve = 1f;
        // How big a mark is at FarFrom, as a part of MarkSize.
        private static float farScale = 0.75f;
        // A warm gold (#E1A766) rather than the white of the game's UI texts, which stood out of
        // the scene, and the dark brown of its panels (the scroll areas' #302114) rather than black.
        private static Color ringColor = new Color(0.882f, 0.655f, 0.4f, 1f);
        private static Color outlineColor = new Color(0.19f, 0.13f, 0.08f, 1f);
        private static bool shapeChanged;

        /// <summary>
        /// Draws the sprites of a mark, again whenever a colour in the config or the shape has
        /// changed: the near disc (CentreColor out to EdgeColor), the far disc (EdgeColor out to
        /// FarEdgeColor) and the rings - a light one inside a dark one, so one of the two stands
        /// out against whatever is behind. The colours are baked in rather than tinted, since a
        /// tint can only darken and the centre has to be brighter than the edge.
        /// </summary>
        private void UpdateSprites()
        {
            Color[] colors = { centreColor.Value, edgeColor.Value, farEdgeColor.Value };
            if (sparkSprite == null)
            {
                // A soft white dot, tinted per spark.
                sparkSprite = Draw((r, edge) => new Color(1f, 1f, 1f, 1f - Step01(r)));
            }
            if (!shapeChanged && nearSprite != null && farSprite != null && ringSprite != null &&
                spriteColors != null &&
                colors[0] == spriteColors[0] && colors[1] == spriteColors[1] && colors[2] == spriteColors[2])
            {
                return;
            }
            shapeChanged = false;
            float ringRadius = OuterRadius - outlineWidth;
            float hole = ringRadius - ringWidth;
            float disc = hole + DiscOverlap;
            Destroy(nearSprite);
            Destroy(farSprite);
            Destroy(ringSprite);
            nearSprite = Disc(colors[0], colors[1], disc);
            farSprite = Disc(colors[1], colors[2], disc);
            ringSprite = Draw((r, edge) =>
            {
                Color color = Color.Lerp(outlineColor, ringColor, Inside(r, ringRadius, edge));
                color.a = Inside(r, OuterRadius, edge) - Inside(r, hole, edge);
                return color;
            }, true);
            spriteColors = colors;
        }

        /// <summary>An opaque disc of <paramref name="radius"/>, fading from <paramref name="centre"/> to <paramref name="edge"/>.</summary>
        private static Sprite Disc(Color centre, Color edge, float radius)
        {
            return Draw((r, width) =>
            {
                Color color = Color.Lerp(centre, edge, Step01(Mathf.Pow(r / radius, gradientCurve)));
                color.a = Inside(r, radius, width);
                return color;
            }, true);
        }

        private static void Destroy(Sprite sprite)
        {
            if (sprite != null)
            {
                Object.Destroy(sprite.texture);
                Object.Destroy(sprite);
            }
        }

        /// <summary>How much of a pixel at radius <paramref name="r"/> lies inside <paramref name="radius"/>, anti-aliased.</summary>
        private static float Inside(float r, float radius, float edge)
        {
            return 1f - Step01((r - radius + edge) / (2f * edge));
        }

        /// <summary>
        /// The smooth step from 0 to 1 over <paramref name="t"/> in 0..1. Not Mathf.SmoothStep,
        /// which interpolates between its first two arguments instead of stepping at them.
        /// </summary>
        private static float Step01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // How rugged the mark's outline is: how far its edge swings in and out, as a part of the
        // radius, and how many bumps go round it. Tuned in game by omp_mark.
        private static float ruggedness = 0.2f;
        private static int bumps = 5;

        /// <summary>
        /// The radius of the mark's outline at <paramref name="angle"/>, as a part of a round one:
        /// three waves of different lengths laid over each other, so the edge is uneven like a
        /// hewn stone rather than a cog, and never above 1, so the widest bump still fits.
        /// </summary>
        private static float Outline(float angle)
        {
            float wave = 0.5f * Mathf.Sin(bumps * angle + 1.3f) +
                0.3f * Mathf.Sin((2 * bumps + 1) * angle + 0.4f) +
                0.2f * Mathf.Sin((3 * bumps + 2) * angle + 2.1f);
            return (1f + ruggedness * wave) / (1f + ruggedness);
        }

        /// <summary>A round sprite from a function of the radius (0 centre, 1 edge) and one pixel's width in it.</summary>
        private static Sprite Draw(System.Func<float, float, Color> pixel, bool rugged = false)
        {
            const int Side = 64;
            const float Edge = 1f / (Side / 2f);
            Texture2D texture = new Texture2D(Side, Side, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            for (int y = 0; y < Side; y++)
            {
                for (int x = 0; x < Side; x++)
                {
                    Vector2 at = new Vector2(x + 0.5f - Side / 2f, y + 0.5f - Side / 2f);
                    float r = at.magnitude / (Side / 2f);
                    if (rugged)
                    {
                        r /= Outline(Mathf.Atan2(at.y, at.x));
                    }
                    texture.SetPixel(x, y, pixel(r, Edge));
                }
            }
            texture.Apply(true);
            return Sprite.Create(texture, new Rect(0f, 0f, Side, Side), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// The enemy HUD draws the name plates over players' heads, straight in screen positions,
        /// and hides with the rest of the HUD - the right canvas and the right moment for the dots.
        /// </summary>
        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.LateUpdate))]
        private static class DrawMarks
        {
            private static void Postfix(EnemyHud __instance)
            {
                Instance.Draw(__instance);
            }
        }
    }
}
