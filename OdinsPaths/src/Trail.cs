using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// A found route turned into something to lay: the grid's corners rounded off (Chaikin, two
    /// passes), a point every 2 m, each knowing its ground height as the game builds it (<see cref="Ground"/>), whether it is under
    /// water, and the height the path should run at there - the ground smoothed over about 10 m
    /// either way, within the stretch of dry land it belongs to, and on a main road cut and
    /// filled toward its kind's <see cref="RoadKind.MaxGrade"/> where the ground is steeper -, how
    /// deep the levelling may go to hold it there (<see cref="MaxCut"/>), and the kind of road it
    /// becomes. Where it turns sharply on a steep slope, the profile holds a flat landing through
    /// the bend (<see cref="Bends"/>); where two legs of it run close, both narrow
    /// (<see cref="LegRoom"/>). The Mistlands get neither: a dirt track that follows the ground,
    /// lit (<see cref="Lamps"/>).
    /// </summary>
    internal sealed class Trail
    {
        public const float Spacing = 2f;
        /// <summary>Ground this close above the water level is shore, left unpainted.</summary>
        public const float ShoreMargin = 0.3f;
        private const int ProfileReach = 5;
        /// <summary>
        /// In the Swamp, ground down to this far under the water is built up into a causeway, and
        /// the search counts it as ground: the swamp dips in and out of the water every few metres.
        /// </summary>
        public const float SwampFill = 1.5f;
        /// <summary>How far above the water a causeway runs.</summary>
        public const float CausewayHeight = 0.4f;
        /// <summary>Points further apart along the trail than this (20 m) are different legs of it.</summary>
        public const int OtherLeg = 10;

        /// <summary>A bend is a turn of more than this over <see cref="TurnReach"/> points either way.</summary>
        private const float TurnAngle = 60f;
        private const int TurnReach = 4;
        /// <summary>Ground at least this steep (rise over run, about 14 degrees) makes a bend a hairpin that gets a landing.</summary>
        private const float TurnSlope = 0.25f;
        /// <summary>A landing is flat this many points either side of its bend, and blends back over as many more.</summary>
        private const int LandingFlat = 2;
        private const int LandingBlend = 3;
        /// <summary>How much deeper than its kind's cut a landing may cut or fill.</summary>
        private const float LandingCut = 2.5f;
        /// <summary>Passes of the grade limit at most; it stops once nothing moves.</summary>
        private const int GradePasses = 300;
        /// <summary>Over this many points (8 m) either side of the Mistlands the road's own making fades to their dirt track.</summary>
        private const int MistlandsFade = 4;
        /// <summary>Two legs closer than their widths share the room between them, each narrowing to no less than this share of its width.</summary>
        private const float NarrowestShare = 0.3f;
        /// <summary>A leg narrows over this many points before the room gets tight, and widens again as far after.</summary>
        private const int NarrowTaper = 4;
        /// <summary>How many points inland of a main road's harbour shore are held at its quay height.</summary>
        private const int QuayPoints = 4;

        public readonly RoadKind Kind;
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Ground = new List<float>();
        /// <summary>Under water (or on the shore) and not a causeway: the sea, a river, a beach.</summary>
        public readonly List<bool> Water = new List<bool>();
        /// <summary>A stretch of swamp raised above the water; never Water.</summary>
        public readonly List<bool> Causeway = new List<bool>();
        public readonly List<float> Profile = new List<float>();
        /// <summary>How far the levelling may cut into or raise the ground at each point: 0 in the Mistlands.</summary>
        public readonly List<float> MaxCut = new List<float>();
        /// <summary>In the Mistlands: dirt, no levelling, and lamps beside it.</summary>
        public readonly List<bool> Mistlands = new List<bool>();
        /// <summary>How much of its kind's own making a point gets - paint and levelling -: 0 in the Mistlands, fading in beside them.</summary>
        public readonly List<float> Built = new List<float>();
        /// <summary>The most half the road's width may be at each point, so that two legs of a hairpin keep apart.</summary>
        public readonly List<float> LegRoom = new List<float>();
        /// <summary>The points the hairpin landings are centred on.</summary>
        public readonly List<int> Bends = new List<int>();
        /// <summary>Held at a harbour's quay height (<see cref="PinQuays"/>): the grade limit eases the road into it and leaves it.</summary>
        public readonly List<bool> Quay = new List<bool>();
        /// <summary>The terrain writer may level again what the same lay levelled before: a door path across the road's shoulder (<see cref="Ramp"/>).</summary>
        public bool OverOwn;
        /// <summary>A fan's half width at each point (<see cref="Fan"/>); null: the kind's.</summary>
        public List<float> Spread;
        /// <summary>A fan's paint strength at each point, fading out; null: full.</summary>
        public List<float> Strength;
        /// <summary>Its last this many points are dirt, after as many again fading from stone (<see cref="DirtTail"/>).</summary>
        private int dirtTail;
        private int dirtFade;
        /// <summary>Its first this many points are dirt, the same way (<see cref="DirtHead"/>).</summary>
        private int dirtHead;
        private int dirtHeadFade;

        /// <summary>Paint only, no levelling: a road's dirt fan (<see cref="Fan"/>).</summary>
        public bool PaintOnly => Spread != null;
        /// <summary>Half the widest the trail gets.</summary>
        public float MaxHalfWidth => Spread != null ? Mathf.Max(Spread[0], Spread[Spread.Count - 1]) : Kind.MaxHalfWidth;
        /// <summary>How far from the line the writer may change anything: edge and shoulder.</summary>
        public float Reach => Spread != null ? MaxHalfWidth * (1f + TerrainWriter.EdgeWobble) : Kind.Reach;

        public Trail(List<Vector2> route, RoadKind kind)
        {
            Kind = kind;
            List<Vector2> smooth = Chaikin(Chaikin(route));
            Resample(smooth);
            float waterLevel = ZoneSystem.instance.m_waterLevel;
            float causewayTop = waterLevel + CausewayHeight;
            List<float> surface = new List<float>(Points.Count);
            foreach (Vector2 p in Points)
            {
                float h = OdinsPaths.Ground.Height(p.x, p.y);
                bool low = h < causewayTop;
                Heightmap.Biome biome = WorldGenerator.instance.GetBiome(p.x, p.y);
                bool causeway = low && h >= waterLevel - SwampFill && biome == Heightmap.Biome.Swamp;
                Ground.Add(h);
                Causeway.Add(causeway);
                Water.Add(!causeway && h < waterLevel + ShoreMargin);
                Mistlands.Add(biome == Heightmap.Biome.Mistlands);
                surface.Add(causeway ? causewayTop : h);
            }
            for (int i = 0; i < Points.Count; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = i; k >= 0 && k >= i - ProfileReach && !Water[k]; k--)
                {
                    sum += surface[k];
                    count++;
                }
                for (int k = i + 1; k < Points.Count && k <= i + ProfileReach && !Water[k]; k++)
                {
                    sum += surface[k];
                    count++;
                }
                float profile = count > 0 ? sum / count : surface[i];
                Profile.Add(Causeway[i] ? Mathf.Max(profile, causewayTop) : profile);
            }
            PinQuays(surface);
            LimitGrade(surface);
            FindCuts(surface);
            FlattenBends();
            FadeIntoMistlands();
            FindLegRoom();
        }

        /// <summary>
        /// A main road's harbour is one site with one height, as the game's own harbours level the
        /// ground at their pier's land end to its deck: the last <see cref="QuayPoints"/> points
        /// before the shore are held at the quay height - the dock's deck height
        /// (<see cref="Docks.DeckHeight"/>) for the road just inland -, so the dock built there later
        /// runs on flush, and the grade limit brings the road down or up to it. Not at a harbour of
        /// the game's (its pier sets the height), nor where the levelling cannot reach the quay.
        /// </summary>
        private void PinQuays(List<float> surface)
        {
            for (int i = 0; i < Points.Count; i++)
            {
                Quay.Add(false);
            }
            if (Kind != RoadKind.Main || !Kind.Levelling)
            {
                return;
            }
            foreach (Landings.Landing landing in Landings.Find(this))
            {
                if (Ports.At(Points[landing.Shore]) != null)
                {
                    continue;
                }
                int inland = landing.Shore < landing.Toward ? -1 : 1;
                List<int> points = new List<int>();
                for (int i = landing.Shore; points.Count < QuayPoints && i >= 0 && i < Points.Count && !Water[i]; i += inland)
                {
                    points.Add(i);
                }
                if (points.Count < 2)
                {
                    continue;
                }
                float quay = Docks.DeckHeight(Profile[points[1]]);
                if (!points.TrueForAll(i => Mathf.Abs(quay - surface[i]) <= Kind.DeepestCut))
                {
                    continue;
                }
                foreach (int i in points)
                {
                    Profile[i] = quay;
                    Quay[i] = true;
                }
            }
        }

        /// <summary>
        /// Holds the profile at one height for its first flat metres - across the road it forks off
        /// - and then on a straight ramp to another at its last point: a path to a door, which has
        /// to arrive at the door's height whatever the ground does. The levelling may go as deep as
        /// that needs, up to deepest; vertices the same lay levelled before (the road's shoulder)
        /// it levels again (<see cref="OverOwn"/>).
        /// </summary>
        public void Ramp(float from, float to, float deepest, float flat)
        {
            OverOwn = true;
            int n = Points.Count;
            float[] along = new float[n];
            for (int i = 1; i < n; i++)
            {
                along[i] = along[i - 1] + Vector2.Distance(Points[i - 1], Points[i]);
            }
            for (int i = 0; i < n; i++)
            {
                float ramp = along[n - 1] - flat;
                Profile[i] = ramp > 0f ? Mathf.Lerp(from, to, (along[i] - flat) / ramp) : to;
                MaxCut[i] = Mathf.Clamp(Mathf.Abs(Profile[i] - Ground[i]) + 0.1f, Kind.MaxCut, Mathf.Max(Kind.MaxCut, deepest)) * Built[i];
            }
        }

        /// <summary>
        /// Where the smoothed profile still climbs steeper than the kind's <see cref="RoadKind.MaxGrade"/>,
        /// each too steep step gives on both ends - the upper one cut down, the lower one filled
        /// up -, no further than <see cref="RoadKind.SteepCut"/> from the ground, until every step
        /// keeps to the grade or has run into that limit. A slope too long for it is eased at both
        /// ends and followed in its middle. Water, causeways and the Mistlands stay as they are.
        /// </summary>
        private void LimitGrade(List<float> surface)
        {
            if (Kind.MaxGrade <= 0f || Kind.SteepCut <= Kind.MaxCut || !Kind.Levelling)
            {
                return;
            }
            int n = Points.Count;
            float step = Kind.MaxGrade * Spacing;
            float[] low = new float[n];
            float[] high = new float[n];
            float[] profile = Profile.ToArray();
            for (int i = 0; i < n; i++)
            {
                bool fixedHere = Water[i] || Causeway[i] || Mistlands[i] || Quay[i];
                float cut = fixedHere ? 0f : Kind.SteepCut;
                low[i] = Mathf.Min(surface[i] - cut, profile[i]);
                high[i] = Mathf.Max(surface[i] + cut, profile[i]);
                if (fixedHere)
                {
                    low[i] = high[i] = profile[i];
                }
            }
            for (int pass = 0; pass < GradePasses; pass++)
            {
                bool moved = false;
                // Both ways by turns, so neither end of a slope gives more than the other.
                bool forward = (pass & 1) == 0;
                for (int k = 0; k < n - 1; k++)
                {
                    int i = forward ? k : n - 2 - k;
                    if (Water[i] || Water[i + 1])
                    {
                        continue;
                    }
                    float rise = profile[i + 1] - profile[i];
                    float excess = Mathf.Abs(rise) - step;
                    if (excess <= 0.005f)
                    {
                        continue;
                    }
                    float give = Mathf.Sign(rise) * excess * 0.5f;
                    float a = Mathf.Clamp(profile[i] + give, low[i], high[i]);
                    float b = Mathf.Clamp(profile[i + 1] - give, low[i + 1], high[i + 1]);
                    // One end at its limit: the other gives the rest, as far as it may.
                    b = Mathf.Clamp(Mathf.Clamp(b, a - step, a + step), low[i + 1], high[i + 1]);
                    a = Mathf.Clamp(Mathf.Clamp(a, b - step, b + step), low[i], high[i]);
                    if (Mathf.Abs(a - profile[i]) + Mathf.Abs(b - profile[i + 1]) > 0.001f)
                    {
                        profile[i] = a;
                        profile[i + 1] = b;
                        moved = true;
                    }
                }
                if (!moved)
                {
                    break;
                }
            }
            // The kinks where a cut meets the smoothed ground, rounded over a point either way.
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || i == n - 1 || Water[i - 1] || Water[i] || Water[i + 1] || low[i] == high[i])
                {
                    Profile[i] = profile[i];
                    continue;
                }
                float rounded = (profile[i - 1] + profile[i] * 2f + profile[i + 1]) * 0.25f;
                Profile[i] = Mathf.Clamp(rounded, low[i], high[i]);
            }
        }

        /// <summary>
        /// How deep each point may cut or fill: its kind's MaxCut, and on steep ground as much more
        /// as holding the road level across the slope and on its profile needs, up to
        /// <see cref="RoadKind.SteepCut"/>. The deepest of a few points either way, so the edge of
        /// a cut does not jump from one segment to the next.
        /// </summary>
        private void FindCuts(List<float> surface)
        {
            int n = Points.Count;
            float[] need = new float[n];
            float across = Kind.MaxHalfWidth * Kind.FlatFactor;
            for (int i = 0; i < n; i++)
            {
                need[i] = Kind.MaxCut;
                if (Kind.SteepCut <= Kind.MaxCut || Water[i])
                {
                    continue;
                }
                int a = Mathf.Max(0, i - 2);
                int b = Mathf.Min(n - 1, i + 2);
                Vector2 dir = (Points[b] - Points[a]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * across;
                Vector2 left = Points[i] + side;
                Vector2 right = Points[i] - side;
                float cross = Mathf.Max(Mathf.Abs(OdinsPaths.Ground.Height(left.x, left.y) - surface[i]),
                    Mathf.Abs(OdinsPaths.Ground.Height(right.x, right.y) - surface[i]));
                need[i] = Mathf.Clamp(Mathf.Abs(Profile[i] - surface[i]) + cross + 0.25f, Kind.MaxCut, Kind.DeepestCut);
            }
            for (int i = 0; i < n; i++)
            {
                float deepest = need[i];
                for (int k = Mathf.Max(0, i - 2); k <= Mathf.Min(n - 1, i + 2); k++)
                {
                    deepest = Mathf.Max(deepest, need[k]);
                }
                MaxCut.Add(deepest);
            }
        }

        /// <summary>
        /// A hairpin on a steep slope climbs through its bend as the search found it, a leg
        /// running into the next at an angle: the turn itself came out tilted and lumpy (seen in
        /// game 2026-09-25). At each sharp bend on steep ground the profile is held level for a
        /// few metres and blended back into the legs, and the cut there may go deeper.
        /// </summary>
        private void FlattenBends()
        {
            float cosTurn = Mathf.Cos(TurnAngle * Mathf.Deg2Rad);
            int last = -100;
            for (int i = TurnReach; i < Points.Count - TurnReach; i++)
            {
                Vector2 before = Points[i] - Points[i - TurnReach];
                Vector2 after = Points[i + TurnReach] - Points[i];
                float cos = Vector2.Dot(before.normalized, after.normalized);
                if (cos > cosTurn || i - last <= LandingFlat + LandingBlend || Wet(i, LandingFlat + LandingBlend))
                {
                    continue;
                }
                // The sharpest point of the bend: go on while the turn sharpens.
                int centre = i;
                for (int k = i + 1; k < Points.Count - TurnReach && k <= i + TurnReach; k++)
                {
                    float next = Vector2.Dot((Points[k] - Points[k - TurnReach]).normalized, (Points[k + TurnReach] - Points[k]).normalized);
                    if (next < cos)
                    {
                        cos = next;
                        centre = k;
                    }
                }
                last = centre;
                i = centre;
                if (Mistlands[centre] || Slope(Points[centre]) < TurnSlope)
                {
                    continue;
                }
                float level = 0f;
                for (int k = centre - LandingFlat; k <= centre + LandingFlat; k++)
                {
                    level += Profile[k];
                }
                level /= LandingFlat * 2 + 1;
                int reach = LandingFlat + LandingBlend;
                for (int k = Mathf.Max(0, centre - reach); k <= Mathf.Min(Points.Count - 1, centre + reach); k++)
                {
                    int off = Mathf.Abs(k - centre);
                    float weight = off <= LandingFlat ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (float)(off - LandingFlat) / (LandingBlend + 1));
                    Profile[k] = Mathf.Lerp(Profile[k], level, weight);
                    MaxCut[k] = Mathf.Lerp(MaxCut[k], Mathf.Max(MaxCut[k], Kind.DeepestCut, Kind.MaxCut * LandingCut), weight);
                }
                Bends.Add(centre);
            }
        }

        /// <summary>
        /// The Mistlands' ground is too broken for a levelled road, and stone laid over it looked
        /// drawn onto the rocks (seen in game 2026-09-25): there the road is dirt that follows the
        /// ground. Its own paint and levelling fade out over <see cref="MistlandsFade"/> points
        /// either side.
        /// </summary>
        private void FadeIntoMistlands()
        {
            int n = Points.Count;
            for (int i = 0; i < n; i++)
            {
                float built = 1f;
                for (int k = Mathf.Max(0, i - MistlandsFade); k <= Mathf.Min(n - 1, i + MistlandsFade); k++)
                {
                    if (Mistlands[k])
                    {
                        built = Mathf.Min(built, Mathf.Abs(k - i) / (float)MistlandsFade);
                    }
                }
                Built.Add(built);
                MaxCut[i] *= built;
            }
        }

        /// <summary>
        /// Where another leg of the trail - a hairpin's leg above or below - comes closer than
        /// both legs' widths and shoulders need, each gets half the room between them: the most
        /// its half width may be there, no less than <see cref="NarrowestShare"/> of its width.
        /// Tapered over <see cref="NarrowTaper"/> points, so the road narrows into the turn and
        /// widens after it. Before, each leg's levelling ate into the other's (seen in game 2026-09-25).
        /// </summary>
        private void FindLegRoom()
        {
            int n = Points.Count;
            float full = Kind.MaxHalfWidth * Kind.FlatFactor + TerrainWriter.Shoulder;
            float look = full * 2f;
            float narrowest = Mathf.Max(1f, Kind.Width * NarrowestShare);
            Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                long key = CellKey(Mathf.FloorToInt(Points[i].x / look), Mathf.FloorToInt(Points[i].y / look));
                if (!cells.TryGetValue(key, out List<int> list))
                {
                    cells[key] = list = new List<int>();
                }
                list.Add(i);
            }
            float[] room = new float[n];
            for (int i = 0; i < n; i++)
            {
                room[i] = float.MaxValue;
                if (Water[i])
                {
                    continue;
                }
                int cx = Mathf.FloorToInt(Points[i].x / look);
                int cy = Mathf.FloorToInt(Points[i].y / look);
                float nearest = look;
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    for (int y = cy - 1; y <= cy + 1; y++)
                    {
                        if (!cells.TryGetValue(CellKey(x, y), out List<int> list))
                        {
                            continue;
                        }
                        foreach (int j in list)
                        {
                            if (Mathf.Abs(i - j) > OtherLeg && !Water[j])
                            {
                                nearest = Mathf.Min(nearest, Vector2.Distance(Points[i], Points[j]));
                            }
                        }
                    }
                }
                if (nearest < look)
                {
                    room[i] = Mathf.Max(narrowest, (nearest * 0.5f - TerrainWriter.Shoulder) / Kind.FlatFactor);
                }
            }
            for (int i = 0; i < n; i++)
            {
                float most = float.MaxValue;
                for (int k = Mathf.Max(0, i - NarrowTaper); k <= Mathf.Min(n - 1, i + NarrowTaper); k++)
                {
                    if (room[k] < float.MaxValue)
                    {
                        // Wider again with every point away from the tight spot.
                        most = Mathf.Min(most, room[k] + Mathf.Abs(k - i) * 0.25f);
                    }
                }
                LegRoom.Add(most);
            }
        }

        private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        /// <summary>Any point within reach of i under water or on a causeway.</summary>
        private bool Wet(int i, int reach)
        {
            for (int k = Mathf.Max(0, i - reach); k <= Mathf.Min(Points.Count - 1, i + reach); k++)
            {
                if (Water[k] || Causeway[k])
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The ground's slope at p, rise over run, over 2 m either way.</summary>
        private static float Slope(Vector2 p)
        {
            float dx = OdinsPaths.Ground.Height(p.x + 2f, p.y) - OdinsPaths.Ground.Height(p.x - 2f, p.y);
            float dz = OdinsPaths.Ground.Height(p.x, p.y + 2f) - OdinsPaths.Ground.Height(p.x, p.y - 2f);
            return Mathf.Sqrt(dx * dx + dz * dz) / 4f;
        }

        /// <summary>
        /// The dirt where a road stops short of its place (the user, 2026-10-02): a straight track
        /// from the road's end toward the way in, widening from the road's half width to wide and
        /// fading out, painted and never levelled. Where the door is not known it hides that.
        /// </summary>
        public static Trail Fan(Vector2 from, Vector2 toward, float length, float startHalf, float endHalf)
        {
            Vector2 to = from + (toward - from).normalized * length;
            Trail fan = new Trail(new List<Vector2> { from, to }, RoadKind.Spur);
            int n = fan.Points.Count;
            fan.Spread = new List<float>(n);
            fan.Strength = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? (float)i / (n - 1) : 1f;
                fan.Spread.Add(Mathf.Lerp(startHalf, endHalf, Mathf.Sqrt(t)));
                fan.Strength.Add(1f - Mathf.SmoothStep(0f, 1f, t));
            }
            return fan;
        }

        /// <summary>
        /// The last metres are dirt - inside an altar's ring, into a <see cref="Fan"/> that cannot
        /// paint over stone -, the stone fading into it over the fade before.
        /// </summary>
        public void DirtTail(float metres, float fade)
        {
            dirtTail = Mathf.Max(0, Mathf.RoundToInt(metres / Spacing));
            dirtFade = Mathf.Max(1, Mathf.RoundToInt(fade / Spacing));
        }

        /// <summary>The first metres are dirt - a road setting out between the sacrificial stones -, fading into stone after.</summary>
        public void DirtHead(float metres, float fade)
        {
            dirtHead = Mathf.Max(0, Mathf.RoundToInt(metres / Spacing));
            dirtHeadFade = Mathf.Max(1, Mathf.RoundToInt(fade / Spacing));
        }

        /// <summary>
        /// Half the road's width at a point of segment i..i+1: its kind's, drifting along the
        /// road, and no more than the room its legs leave it; a fan's own.
        /// </summary>
        public float HalfWidthAt(int segment, Vector2 at)
        {
            if (Spread != null)
            {
                return (Spread[segment] + Spread[Mathf.Min(segment + 1, Spread.Count - 1)]) * 0.5f;
            }
            float room = Mathf.Min(LegRoom[segment], LegRoom[Mathf.Min(segment + 1, LegRoom.Count - 1)]);
            return Mathf.Min(Kind.HalfWidthAt(at), room);
        }

        /// <summary>The paint at segment i..i+1: the kind's, turning to dirt into the Mistlands and over a dirt tail.</summary>
        public Color PaintAt(int segment)
        {
            if (!Kind.Paved)
            {
                return Kind.Paint;
            }
            float built = Mathf.Min(Built[segment], Built[Mathf.Min(segment + 1, Built.Count - 1)]);
            if (dirtFade > 0)
            {
                built = Mathf.Min(built, Mathf.Clamp01((float)(Points.Count - 1 - segment - dirtTail) / dirtFade));
            }
            if (dirtHeadFade > 0)
            {
                built = Mathf.Min(built, Mathf.Clamp01((float)(segment - dirtHead) / dirtHeadFade));
            }
            return Handover(Heightmap.m_paintMaskDirt, Kind.Paint, built);
        }

        /// <summary>
        /// From one paint to another, t of the way: the channels the second one fills rise over
        /// the first half, the ones it empties fall over the second. A straight lerp from dirt to
        /// stone is half of each midway, and the game clears grass only where a channel is over
        /// one half (<c>Heightmap.IsCleared</c>), so grass grew in the middle of a fade (found
        /// through Procedural Roads #32).
        /// </summary>
        public static Color Handover(Color from, Color to, float t)
        {
            float rise = Mathf.Clamp01(t * 2f);
            float fall = Mathf.Clamp01(t * 2f - 1f);
            return new Color(
                Mathf.Lerp(from.r, to.r, to.r > from.r ? rise : fall),
                Mathf.Lerp(from.g, to.g, to.g > from.g ? rise : fall),
                Mathf.Lerp(from.b, to.b, to.b > from.b ? rise : fall),
                Mathf.Lerp(from.a, to.a, to.a > from.a ? rise : fall));
        }

        /// <summary>How strongly segment i..i+1 is painted: a fan fades out.</summary>
        public float StrengthAt(int segment) => Strength != null ? Strength[segment] : 1f;

        /// <summary>The deeper cut of segment i..i+1's two ends.</summary>
        public float MaxCutAt(int segment) => Mathf.Max(MaxCut[segment], MaxCut[Mathf.Min(segment + 1, MaxCut.Count - 1)]);

        public float Length => Points.Count > 1 ? (Points.Count - 1) * Spacing : 0f;

        /// <summary>Metres of the trail under water, and the steepest grade on dry land.</summary>
        public void Stats(out float waterMetres, out float steepestGrade)
        {
            waterMetres = 0f;
            steepestGrade = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                if (Water[i])
                {
                    waterMetres += Spacing;
                }
                else if (!Water[i - 1])
                {
                    steepestGrade = Mathf.Max(steepestGrade, Mathf.Abs(Profile[i] - Profile[i - 1]) / Spacing);
                }
            }
        }

        /// <summary>Whether segment i..i+1 is built up out of the swamp: either end is.</summary>
        public bool CausewayAt(int segment) => Causeway[segment] || Causeway[segment + 1];

        /// <summary>
        /// The point of segment i..i+1 nearest to p: its distance, and the path height there (the
        /// dry end's profile if one end is under water).
        /// </summary>
        public float Nearest(Vector2 p, int segment, out float profile)
        {
            Vector2 a = Points[segment];
            Vector2 b = Points[segment + 1];
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float pa = Profile[segment];
            float pb = Profile[segment + 1];
            if (Water[segment])
            {
                pa = pb;
            }
            else if (Water[segment + 1])
            {
                pb = pa;
            }
            profile = Mathf.Lerp(pa, pb, t);
            return Vector2.Distance(p, a + ab * t);
        }

        private static List<Vector2> Chaikin(List<Vector2> points)
        {
            if (points.Count < 3)
            {
                return new List<Vector2>(points);
            }
            List<Vector2> result = new List<Vector2> { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                result.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
                result.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
            }
            result.Add(points[points.Count - 1]);
            return result;
        }

        private void Resample(List<Vector2> line)
        {
            Points.Add(line[0]);
            float carried = 0f;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                float length = Vector2.Distance(a, line[i + 1]);
                float at = Spacing - carried;
                while (at <= length)
                {
                    Points.Add(Vector2.Lerp(a, line[i + 1], at / length));
                    at += Spacing;
                }
                carried = length - (at - Spacing);
            }
            if (Points[Points.Count - 1] != line[line.Count - 1])
            {
                Points.Add(line[line.Count - 1]);
            }
        }
    }
}
