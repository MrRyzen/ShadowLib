namespace ShadowLib.Procedural
{
    using System;
    using ShadowLib.RNG.Sources;

    /// <summary>Relative weights for the four step directions of a <see cref="RandomWalk"/>.</summary>
    public readonly struct DirectionBias
    {
        /// <summary>Creates a bias. Each weight must be finite and not negative, and at least one must be positive.</summary>
        /// <param name="right">Weight of a step to +X.</param>
        /// <param name="left">Weight of a step to -X.</param>
        /// <param name="up">Weight of a step to +Y.</param>
        /// <param name="down">Weight of a step to -Y.</param>
        /// <exception cref="ArgumentOutOfRangeException">A weight is negative, NaN or infinite, or all four are zero.</exception>
        public DirectionBias(float right, float left, float up, float down)
        {
            RequireWeight(right, nameof(right));
            RequireWeight(left, nameof(left));
            RequireWeight(up, nameof(up));
            RequireWeight(down, nameof(down));
            if (right == 0f && left == 0f && up == 0f && down == 0f)
                throw new ArgumentOutOfRangeException(nameof(right), "At least one direction needs a positive weight.");

            Right = right;
            Left = left;
            Up = up;
            Down = down;
        }

        /// <summary>Gets the weight of a step to +X.</summary>
        public float Right { get; }

        /// <summary>Gets the weight of a step to -X.</summary>
        public float Left { get; }

        /// <summary>Gets the weight of a step to +Y.</summary>
        public float Up { get; }

        /// <summary>Gets the weight of a step to -Y.</summary>
        public float Down { get; }

        /// <summary>Gets a bias where all four directions are equally likely.</summary>
        public static DirectionBias Uniform => new DirectionBias(1f, 1f, 1f, 1f);

        internal bool HasAnyWeight => Right > 0f || Left > 0f || Up > 0f || Down > 0f;

        private static void RequireWeight(float weight, string paramName)
        {
            if (!(weight >= 0f) || float.IsInfinity(weight))
                throw new ArgumentOutOfRangeException(paramName, weight, "A direction weight must be finite and not negative.");
        }
    }

    /// <summary>A seeded walk over a bounded grid, one orthogonal step at a time.</summary>
    public static class RandomWalk
    {
        /// <summary>
        /// Walks from <paramref name="start"/> and returns the visited positions in order, start first.
        /// The result is shorter than <paramref name="length"/> if the walk reaches a spot where no weighted direction stays in bounds.
        /// </summary>
        /// <param name="rng">The random source. One <see cref="IRandom.NextFloat"/> per step taken.</param>
        /// <param name="length">The most positions to return, including the start. Must be greater than zero.</param>
        /// <param name="width">Grid width; X stays in 0..width-1.</param>
        /// <param name="height">Grid height; Y stays in 0..height-1.</param>
        /// <param name="start">The first position. Must be inside the grid.</param>
        /// <param name="bias">The direction weights.</param>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Length, width or height is not positive, or the start is outside the grid.</exception>
        /// <exception cref="ArgumentException"><paramref name="bias"/> is <c>default</c> (all weights zero).</exception>
        public static (int X, int Y)[] Generate(
            IRandom rng, int length, int width, int height, (int X, int Y) start, DirectionBias bias)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be greater than zero.");

            var path = new (int X, int Y)[length];
            int written = Generate(rng, path.AsSpan(), width, height, start, bias);
            if (written < length)
                Array.Resize(ref path, written);

            return path;
        }

        /// <summary>
        /// Walks from <paramref name="start"/>, writing positions into <paramref name="path"/> without allocating.
        /// </summary>
        /// <returns>How many positions were written: the span length, or fewer if the walk had nowhere to go.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Width or height is not positive, or the start is outside the grid.</exception>
        /// <exception cref="ArgumentException"><paramref name="bias"/> is <c>default</c> (all weights zero).</exception>
        public static int Generate(
            IRandom rng, Span<(int X, int Y)> path, int width, int height, (int X, int Y) start, DirectionBias bias)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
            if (start.X < 0 || start.X >= width || start.Y < 0 || start.Y >= height)
                throw new ArgumentOutOfRangeException(nameof(start), start, "The start must be inside the grid.");
            if (!bias.HasAnyWeight)
                throw new ArgumentException("The bias has no weights; use the constructor or DirectionBias.Uniform.", nameof(bias));
            if (path.IsEmpty)
                return 0;

            var position = start;
            path[0] = position;
            for (int written = 1; written < path.Length; written++)
            {
                double right = position.X + 1 < width ? bias.Right : 0.0;
                double left = position.X > 0 ? bias.Left : 0.0;
                double up = position.Y + 1 < height ? bias.Up : 0.0;
                double down = position.Y > 0 ? bias.Down : 0.0;
                double total = right + left + up + down;
                if (total <= 0.0)
                    return written;

                double target = rng.NextFloat() * total;
                if (target < right)
                    position.X++;
                else if (target < right + left)
                    position.X--;
                else if (target < right + left + up)
                    position.Y++;
                else
                    position.Y--;

                path[written] = position;
            }

            return path.Length;
        }
    }
}
