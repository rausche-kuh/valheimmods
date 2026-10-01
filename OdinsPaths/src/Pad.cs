using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The ground under a harbour building levelled to its floor, as the game's locations level
    /// theirs (<c>TerrainModifier</c>: level, then smooth): a box in the building's frame, cut down
    /// where the ground is higher than the floor and filled a little where it is lower - the piles
    /// carry the rest -, fading back into the ground over <see cref="Margin"/> around it. Written
    /// by <see cref="TerrainWriter.WritePad"/> after the path to the door; the builder reads the
    /// ground through it (<see cref="Planned"/>), so nothing the pad digs out counts as buried.
    /// </summary>
    internal sealed class Pad
    {
        /// <summary>The fade back into the ground around the box.</summary>
        public const float Margin = 1.5f;
        /// <summary>How far the box reaches past the building's own pieces.</summary>
        public const float Apron = 0.5f;
        /// <summary>How far under the floor's top the levelled ground lies.</summary>
        public const float UnderFloor = 0.1f;

        /// <summary>The building's frame, in which the box lies.</summary>
        public Builder.Frame Frame;
        /// <summary>The box, x and z in the frame, the apron included.</summary>
        public Vector2 Min;
        public Vector2 Max;
        /// <summary>The world height the ground is levelled to.</summary>
        public float Height;
        /// <summary>The most it cuts and the most it fills.</summary>
        public float MaxCut;
        public float MaxFill;

        /// <summary>How much of the levelling a point gets: 1 in the box, fading to 0 across the margin.</summary>
        public float Weight(float x, float z)
        {
            Vector3 local = Frame.Local(new Vector3(x, 0f, z));
            float dx = Mathf.Max(Min.x - local.x, 0f, local.x - Max.x);
            float dz = Mathf.Max(Min.y - local.z, 0f, local.z - Max.y);
            float outside = Mathf.Sqrt(dx * dx + dz * dz);
            return outside <= 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, outside / Margin);
        }

        /// <summary>The ground at a point once the pad is written, from the ground as the game builds it.</summary>
        public float Planned(float x, float z, float ground)
        {
            float weight = Weight(x, z);
            return weight <= 0f ? ground : ground + Delta(ground) * weight;
        }

        /// <summary>The full levelling at ground of this height, before the fade.</summary>
        public float Delta(float ground) => Mathf.Clamp(Height - ground, -MaxCut, MaxFill);

        /// <summary>The corners of the box with its margin, in the world, for the zones it touches.</summary>
        public void Bounds(out Vector2 min, out Vector2 max)
        {
            min = Vector2.one * float.MaxValue;
            max = Vector2.one * float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                Vector2 corner = Frame.Flat((k & 1) == 0 ? Min.x - Margin : Max.x + Margin, (k & 2) == 0 ? Min.y - Margin : Max.y + Margin);
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }
        }
    }
}
