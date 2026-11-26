namespace ShadowLib.Spatial
{
    using System;
    using System.Collections.Generic;
    using ShadowLib.RNG.Sources;

    /// <summary>
    /// A three-dimensional shape as a set of cell offsets relative to an anchor at (0, 0, 0):
    /// the 3D counterpart of <see cref="Polyomino"/>. Offsets are normalized so the minimum
    /// column, row and layer are always zero. Shapes do not rotate; build the rotated shape instead.
    /// </summary>
    public readonly struct Polycube
    {
        private static readonly Polycube _single = FromBox(1, 1, 1);

        private readonly (int Col, int Row, int Layer)[] _offsets;

        private Polycube((int Col, int Row, int Layer)[] offsets, int width, int height, int depth)
        {
            _offsets = offsets;
            Width = width;
            Height = height;
            Depth = depth;
        }

        /// <summary>Gets the cell offsets of this shape. Empty for <c>default(Polycube)</c>.</summary>
        public ReadOnlySpan<(int Col, int Row, int Layer)> Offsets =>
            _offsets != null ? _offsets.AsSpan() : ReadOnlySpan<(int Col, int Row, int Layer)>.Empty;

        /// <summary>Gets the bounding-box width (columns).</summary>
        public int Width { get; }

        /// <summary>Gets the bounding-box height (rows).</summary>
        public int Height { get; }

        /// <summary>Gets the bounding-box depth (layers).</summary>
        public int Depth { get; }

        /// <summary>Gets the number of cells this shape occupies.</summary>
        public int CellCount => _offsets?.Length ?? 0;

        /// <summary>Gets a single-cell shape.</summary>
        public static ref readonly Polycube Single => ref _single;

        /// <summary>Creates a solid box.</summary>
        /// <param name="width">Columns. Must be greater than zero.</param>
        /// <param name="height">Rows. Must be greater than zero.</param>
        /// <param name="depth">Layers. Must be greater than zero.</param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, or the cell count does not fit in an int.</exception>
        public static Polycube FromBox(int width, int height, int depth)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be greater than zero.");
            if ((long)width * height * depth > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), "The box has too many cells.");

            var offsets = new (int Col, int Row, int Layer)[width * height * depth];
            int i = 0;
            for (int layer = 0; layer < depth; layer++)
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                        offsets[i++] = (col, row, layer);

            return new Polycube(offsets, width, height, depth);
        }

        /// <summary>Creates a shape from explicit offsets, shifted so each axis starts at zero. Input order is kept.</summary>
        /// <exception cref="ArgumentException">No offsets were given, or two offsets are the same cell.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The offsets are too far apart on an axis to normalize.</exception>
        public static Polycube FromOffsets(params (int Col, int Row, int Layer)[] offsets)
        {
            if (offsets == null || offsets.Length == 0)
                throw new ArgumentException("At least one offset is required.", nameof(offsets));

            RequireDistinct(offsets, nameof(offsets));

            var min = offsets[0];
            var max = offsets[0];
            for (int i = 1; i < offsets.Length; i++)
            {
                var offset = offsets[i];
                min = (Math.Min(min.Col, offset.Col), Math.Min(min.Row, offset.Row), Math.Min(min.Layer, offset.Layer));
                max = (Math.Max(max.Col, offset.Col), Math.Max(max.Row, offset.Row), Math.Max(max.Layer, offset.Layer));
            }

            if ((long)max.Col - min.Col >= int.MaxValue
                || (long)max.Row - min.Row >= int.MaxValue
                || (long)max.Layer - min.Layer >= int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(offsets), "The offsets span more than an int can hold.");

            var normalized = new (int Col, int Row, int Layer)[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
                normalized[i] = (offsets[i].Col - min.Col, offsets[i].Row - min.Row, offsets[i].Layer - min.Layer);

            return new Polycube(normalized, max.Col - min.Col + 1, max.Row - min.Row + 1, max.Layer - min.Layer + 1);
        }

        /// <summary>Creates a shape by repeating a flat shape through a number of layers.</summary>
        /// <param name="shape">The flat shape. Must have at least one cell.</param>
        /// <param name="depth">How many layers deep. Must be greater than zero.</param>
        /// <exception cref="ArgumentException">The flat shape has no cells, or lists the same cell twice.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Depth is not positive, or the cell count does not fit in an int.</exception>
        public static Polycube FromPolyomino(Polyomino shape, int depth = 1)
        {
            if (shape.CellCount == 0)
                throw new ArgumentException("The shape has no cells.", nameof(shape));
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be greater than zero.");
            if ((long)shape.CellCount * depth > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(depth), "The shape has too many cells.");

            var flat = shape.Offsets;
            var offsets = new (int Col, int Row, int Layer)[flat.Length * depth];
            int i = 0;
            for (int layer = 0; layer < depth; layer++)
                for (int cell = 0; cell < flat.Length; cell++)
                    offsets[i++] = (flat[cell].Col, flat[cell].Row, layer);

            RequireDistinct(offsets, nameof(shape));
            return new Polycube(offsets, shape.Width, shape.Height, depth);
        }

        /// <summary>
        /// Grows a random face-connected shape one cell at a time. Makes exactly
        /// <paramref name="cellCount"/> - 1 <see cref="IRandom.NextFloat"/> draws.
        /// </summary>
        /// <param name="rng">The random source.</param>
        /// <param name="cellCount">How many cells the shape has. Must be greater than zero.</param>
        /// <param name="compactness">0 grows thin, sprawling shapes; 1 grows dense, blocky ones.</param>
        /// <param name="maxWidth">Largest allowed bounding-box width.</param>
        /// <param name="maxHeight">Largest allowed bounding-box height.</param>
        /// <param name="maxDepth">Largest allowed bounding-box depth.</param>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The cell count or a limit is not positive, compactness is outside 0..1, or the limits cannot hold the cell count.
        /// </exception>
        public static Polycube Generate(
            IRandom rng, int cellCount, float compactness,
            int maxWidth = int.MaxValue, int maxHeight = int.MaxValue, int maxDepth = int.MaxValue) =>
            FromOffsets(ShapeGrowth.Grow(rng, cellCount, compactness, maxWidth, maxHeight, maxDepth));

        private static void RequireDistinct((int Col, int Row, int Layer)[] offsets, string paramName)
        {
            var seen = new HashSet<(int, int, int)>();
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!seen.Add(offsets[i]))
                    throw new ArgumentException($"Offset {offsets[i]} appears more than once.", paramName);
            }
        }
    }
}
