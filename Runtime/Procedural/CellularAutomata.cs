namespace ShadowLib.Procedural
{
    using System;
    using ShadowLib.RNG.Sources;
    using ShadowLib.Spatial;

    /// <summary>
    /// Threshold cellular automaton over a <see cref="FieldGrid{T}"/> of alive/dead cells, the usual way to
    /// smooth random noise into caves. Counts the eight surrounding cells. Not thread-safe: an instance reuses a scratch buffer.
    /// </summary>
    public sealed class CellularAutomata
    {
        private const int NeighbourCount = 8;

        private readonly int _birthRule;
        private readonly int _surviveRule;
        private readonly bool _edgesAlive;
        private bool[] _previous = Array.Empty<bool>();

        /// <summary>Creates the rules.</summary>
        /// <param name="birthRule">A dead cell comes alive with at least this many living neighbours. 0 to 9; 9 means never.</param>
        /// <param name="surviveRule">A living cell stays alive with at least this many living neighbours. 0 to 9; 9 means never.</param>
        /// <param name="edgesAlive">Whether cells outside the grid count as alive, which closes caves at the border.</param>
        /// <exception cref="ArgumentOutOfRangeException">A rule is outside 0 to 9.</exception>
        public CellularAutomata(int birthRule, int surviveRule, bool edgesAlive = true)
        {
            if (birthRule < 0 || birthRule > NeighbourCount + 1)
                throw new ArgumentOutOfRangeException(nameof(birthRule), birthRule, "The birth rule must be from 0 to 9.");
            if (surviveRule < 0 || surviveRule > NeighbourCount + 1)
                throw new ArgumentOutOfRangeException(nameof(surviveRule), surviveRule, "The survive rule must be from 0 to 9.");

            _birthRule = birthRule;
            _surviveRule = surviveRule;
            _edgesAlive = edgesAlive;
        }

        /// <summary>Sets each cell alive with the given chance, row by row, one <see cref="IRandom.NextFloat"/> per cell.</summary>
        /// <param name="grid">The grid to fill.</param>
        /// <param name="rng">The random source.</param>
        /// <param name="aliveChance">The chance from 0 to 1 that a cell starts alive.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The chance is outside 0 to 1.</exception>
        public static void Randomize(FieldGrid<bool> grid, IRandom rng, float aliveChance)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (!(aliveChance >= 0f && aliveChance <= 1f))
                throw new ArgumentOutOfRangeException(nameof(aliveChance), aliveChance, "The chance must be between 0 and 1.");

            var cells = grid.AsSpan();
            for (int i = 0; i < cells.Length; i++)
                cells[i] = rng.NextFloat() < aliveChance;
        }

        /// <summary>Advances the grid in place. Every cell of a generation is decided from the generation before it.</summary>
        /// <param name="grid">The grid to advance.</param>
        /// <param name="iterations">How many generations to run.</param>
        /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is negative.</exception>
        public void Step(FieldGrid<bool> grid, int iterations = 1)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (iterations < 0)
                throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Iterations must not be negative.");

            int width = grid.Width;
            int height = grid.Height;
            var cells = grid.AsSpan();
            if (_previous.Length < cells.Length)
                _previous = new bool[cells.Length];

            var previous = _previous.AsSpan(0, cells.Length);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                cells.CopyTo(previous);
                for (int y = 0; y < height; y++)
                {
                    bool borderRow = y == 0 || y == height - 1;
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        int rule = previous[index] ? _surviveRule : _birthRule;
                        int living = borderRow || x == 0 || x == width - 1
                            ? LivingNeighbours(previous, width, height, x, y)
                            : InteriorLivingNeighbours(previous, width, index);
                        cells[index] = living >= rule;
                    }
                }
            }
        }

        private static int InteriorLivingNeighbours(ReadOnlySpan<bool> cells, int width, int index)
        {
            var above = cells.Slice(index - width - 1, 3);
            var below = cells.Slice(index + width - 1, 3);
            return (above[0] ? 1 : 0) + (above[1] ? 1 : 0) + (above[2] ? 1 : 0)
                + (cells[index - 1] ? 1 : 0) + (cells[index + 1] ? 1 : 0)
                + (below[0] ? 1 : 0) + (below[1] ? 1 : 0) + (below[2] ? 1 : 0);
        }

        private int LivingNeighbours(ReadOnlySpan<bool> cells, int width, int height, int x, int y)
        {
            int living = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = x + dx;
                    int ny = y + dy;
                    bool inside = nx >= 0 && nx < width && ny >= 0 && ny < height;
                    if (inside ? cells[ny * width + nx] : _edgesAlive)
                        living++;
                }
            }

            return living;
        }
    }
}
