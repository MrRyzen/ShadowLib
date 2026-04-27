namespace ShadowLib.Spatial
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// A flat-array-backed 2D grid of struct values, optimized for scalar fields such as
    /// density maps, influence grids, heatmaps, and cellular automata state.
    /// Unlike <see cref="Grid2D{TCell, TOccupant}"/>, this grid stores raw values without
    /// occupancy tracking or cell interfaces.
    /// </summary>
    /// <typeparam name="T">The value type stored in each cell. Must be a value type (struct).</typeparam>
    public class FieldGrid<T> : IEnumerable<T>
        where T : struct
    {
        private readonly T[] _values;

        /// <summary>
        /// Gets the number of columns in the grid.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the number of rows in the grid.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the world-space size of each cell, used for coordinate conversion.
        /// </summary>
        public float CellSize { get; private set; }

        /// <summary>
        /// Gets the world-space X origin of the grid. Cell positions are offset by this value.
        /// </summary>
        public float OriginX { get; private set; }

        /// <summary>
        /// Gets the world-space Y origin of the grid. Cell positions are offset by this value.
        /// </summary>
        public float OriginY { get; private set; }

        /// <summary>
        /// Gets the total number of cells in the grid (<see cref="Width"/> * <see cref="Height"/>).
        /// </summary>
        public int CellCount => _values.Length;

        /// <summary>
        /// Empty constructor for serialization purposes.
        /// </summary>
        public FieldGrid()
        {
            _values = Array.Empty<T>();
        }

        /// <summary>
        /// Initializes a new <see cref="FieldGrid{T}"/> with the specified dimensions.
        /// All cells are set to <paramref name="defaultValue"/>.
        /// </summary>
        /// <param name="width">Number of columns. Must be greater than zero.</param>
        /// <param name="height">Number of rows. Must be greater than zero.</param>
        /// <param name="cellSize">World-space size of each cell, used for coordinate conversion.</param>
        /// <param name="defaultValue">The initial value for every cell. Defaults to <c>default(T)</c>.</param>
        /// <param name="originX">World-space X origin of the grid. Defaults to 0.</param>
        /// <param name="originY">World-space Y origin of the grid. Defaults to 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="width"/> or <paramref name="height"/> is less than or equal to zero.
        /// </exception>
        public FieldGrid(int width, int height, float cellSize, T defaultValue = default, float originX = 0f, float originY = 0f)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

            Width = width;
            Height = height;
            CellSize = cellSize;
            OriginX = originX;
            OriginY = originY;
            _values = new T[width * height];

            if (!EqualityComparer<T>.Default.Equals(defaultValue, default))
                _values.AsSpan().Fill(defaultValue);
        }

        // ── Value Access ──────────────────────────────────────────────────

        /// <summary>
        /// Gets or sets the value at the specified grid coordinates by reference.
        /// Allows in-place mutation of struct fields without copying.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>A reference to the value at position (<paramref name="x"/>, <paramref name="y"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="x"/> or <paramref name="y"/> is outside grid bounds.
        /// </exception>
        public ref T this[int x, int y]
        {
            get
            {
                if (!TryGetIndex(x, y, out int index))
                    throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

                return ref _values[index];
            }
        }

        /// <summary>
        /// Gets the value at the specified grid coordinates as a copy.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>The value at position (<paramref name="x"/>, <paramref name="y"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="x"/> or <paramref name="y"/> is outside grid bounds.
        /// </exception>
        public T Get(int x, int y)
        {
            if (!TryGetIndex(x, y, out int index))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            return _values[index];
        }

        /// <summary>
        /// Sets the value at the specified grid coordinates.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <param name="value">The value to set.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="x"/> or <paramref name="y"/> is outside grid bounds.
        /// </exception>
        public void Set(int x, int y, T value)
        {
            if (!TryGetIndex(x, y, out int index))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            _values[index] = value;
        }

        /// <summary>
        /// Attempts to get the value at the specified coordinates without throwing on out-of-bounds input.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <param name="value">
        /// When this method returns <see langword="true"/>, contains the value at (<paramref name="x"/>, <paramref name="y"/>);
        /// otherwise <c>default(T)</c>.
        /// </param>
        /// <returns><see langword="true"/> if the coordinates are within bounds; otherwise <see langword="false"/>.</returns>
        public bool TryGet(int x, int y, out T value)
        {
            if (!TryGetIndex(x, y, out int index))
            {
                value = default;
                return false;
            }

            value = _values[index];
            return true;
        }

        /// <summary>
        /// Attempts to set the value at the specified coordinates without throwing on out-of-bounds input.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <param name="value">The value to set.</param>
        /// <returns><see langword="true"/> if the coordinates are within bounds and the value was set; otherwise <see langword="false"/>.</returns>
        public bool TrySet(int x, int y, T value)
        {
            if (!TryGetIndex(x, y, out int index))
                return false;

            _values[index] = value;
            return true;
        }

        /// <summary>
        /// Gets a reference to the value at the specified grid coordinates.
        /// Identical to the indexer; provided as an explicit method name for clarity.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>A reference to the value at position (<paramref name="x"/>, <paramref name="y"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="x"/> or <paramref name="y"/> is outside grid bounds.
        /// </exception>
        public ref T GetRef(int x, int y)
        {
            if (!TryGetIndex(x, y, out int index))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            return ref _values[index];
        }

        // ── Coordinate Helpers ────────────────────────────────────────────

        /// <summary>
        /// Converts grid coordinates to a flat array index.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>The flat index computed as <c>y * Width + x</c>.</returns>
        public int GetIndex(int x, int y)
        {
            return y * Width + x;
        }

        /// <summary>
        /// Converts a flat array index back to its (x, y) grid coordinates.
        /// </summary>
        /// <param name="index">The flat array index. Must be in range [0, <see cref="CellCount"/>).</param>
        /// <returns>A tuple containing the column (<c>x</c>) and row (<c>y</c>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="index"/> is negative or greater than or equal to <see cref="CellCount"/>.
        /// </exception>
        public (int x, int y) GetCoordinates(int index)
        {
            if (index < 0 || index >= _values.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index {index} is out of range. Grid has {_values.Length} cells.");

            return (index % Width, index / Width);
        }

        /// <summary>
        /// Determines whether the specified coordinates are within the grid bounds.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns><see langword="true"/> if the coordinates are valid; otherwise <see langword="false"/>.</returns>
        public bool IsValidCell(int x, int y)
        {
            return (uint)x < (uint)Width && (uint)y < (uint)Height;
        }

        /// <summary>
        /// Returns the world-space center position of the cell at the given grid coordinates.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>
        /// A tuple of <c>(worldX, worldY)</c> representing the center of the cell in world space,
        /// computed as <c>OriginX + (x + 0.5f) * CellSize</c> and <c>OriginY + (y + 0.5f) * CellSize</c>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public (float worldX, float worldY) GetWorldPosition(int x, int y)
        {
            if (!TryGetIndex(x, y, out _))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            return (OriginX + (x + 0.5f) * CellSize, OriginY + (y + 0.5f) * CellSize);
        }

        /// <summary>
        /// Converts a world-space position to grid coordinates.
        /// Returns coordinates even if they are outside the grid bounds; use <see cref="IsValidCell"/> to validate.
        /// </summary>
        /// <param name="worldX">The world-space X position.</param>
        /// <param name="worldY">The world-space Y position.</param>
        /// <returns>A tuple of <c>(x, y)</c> grid coordinates computed via floor division by <see cref="CellSize"/>.</returns>
        public (int x, int y) GetGridPosition(float worldX, float worldY)
        {
            return ((int)((worldX - OriginX) / CellSize), (int)((worldY - OriginY) / CellSize));
        }

        // ── Spatial Queries ───────────────────────────────────────────────

        /// <summary>
        /// Gets the values of the four cardinal neighbors (up, down, left, right) of the cell at the specified coordinates.
        /// Only includes neighbors that are within grid bounds.
        /// </summary>
        /// <param name="x">The column index of the source cell.</param>
        /// <param name="y">The row index of the source cell.</param>
        /// <param name="buffer">A span of at least 4 elements to receive neighbor values.</param>
        /// <returns>The number of neighbors written to <paramref name="buffer"/> (0 to 4).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public int GetNeighbors(int x, int y, Span<T> buffer)
        {
            if (!TryGetIndex(x, y, out _))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            int count = 0;
            TryAddNeighbor(buffer, ref count, x - 1, y);
            TryAddNeighbor(buffer, ref count, x + 1, y);
            TryAddNeighbor(buffer, ref count, x, y - 1);
            TryAddNeighbor(buffer, ref count, x, y + 1);
            return count;
        }

        /// <summary>
        /// Gets the values of all eight neighbors (cardinal and diagonal) of the cell at the specified coordinates.
        /// Only includes neighbors that are within grid bounds.
        /// </summary>
        /// <param name="x">The column index of the source cell.</param>
        /// <param name="y">The row index of the source cell.</param>
        /// <param name="buffer">A span of at least 8 elements to receive neighbor values.</param>
        /// <returns>The number of neighbors written to <paramref name="buffer"/> (0 to 8).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public int GetNeighbors8(int x, int y, Span<T> buffer)
        {
            if (!TryGetIndex(x, y, out _))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dy != 0)
                        TryAddNeighbor(buffer, ref count, x + dx, y + dy);
                }
            }

            return count;
        }

        /// <summary>
        /// Gets the flat indices of the four cardinal neighbors of the cell at the specified coordinates.
        /// Only includes neighbors that are within grid bounds.
        /// </summary>
        /// <param name="x">The column index of the source cell.</param>
        /// <param name="y">The row index of the source cell.</param>
        /// <param name="buffer">A span of at least 4 elements to receive neighbor indices.</param>
        /// <returns>The number of indices written to <paramref name="buffer"/> (0 to 4).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public int GetNeighborIndices(int x, int y, Span<int> buffer)
        {
            if (!TryGetIndex(x, y, out _))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            int count = 0;
            TryAddNeighborIndex(buffer, ref count, x - 1, y);
            TryAddNeighborIndex(buffer, ref count, x + 1, y);
            TryAddNeighborIndex(buffer, ref count, x, y - 1);
            TryAddNeighborIndex(buffer, ref count, x, y + 1);
            return count;
        }

        /// <summary>
        /// Gets the flat indices of all eight neighbors (cardinal and diagonal) of the cell at the specified coordinates.
        /// Only includes neighbors that are within grid bounds.
        /// </summary>
        /// <param name="x">The column index of the source cell.</param>
        /// <param name="y">The row index of the source cell.</param>
        /// <param name="buffer">A span of at least 8 elements to receive neighbor indices.</param>
        /// <returns>The number of indices written to <paramref name="buffer"/> (0 to 8).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public int GetNeighborIndices8(int x, int y, Span<int> buffer)
        {
            if (!TryGetIndex(x, y, out _))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dy != 0)
                        TryAddNeighborIndex(buffer, ref count, x + dx, y + dy);
                }
            }

            return count;
        }

        /// <summary>
        /// Gets all values within a given Manhattan distance of the specified coordinates.
        /// The source cell itself is excluded from the result.
        /// </summary>
        /// <param name="x">The column index of the origin cell.</param>
        /// <param name="y">The row index of the origin cell.</param>
        /// <param name="range">The maximum Manhattan distance. Must be greater than or equal to zero.</param>
        /// <param name="buffer">A span to receive values within range.</param>
        /// <returns>The number of values written to <paramref name="buffer"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds, or when <paramref name="range"/> is negative.
        /// </exception>
        public int GetCellsInRange(int x, int y, int range, Span<T> buffer)
        {
            if (!TryGetIndex(x, y, out _))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");
            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), range, "Range must be greater than or equal to zero.");

            int count = 0;
            int minX = Math.Max(0, x - range);
            int maxX = Math.Min(Width - 1, x + range);
            int minY = Math.Max(0, y - range);
            int maxY = Math.Min(Height - 1, y + range);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (count >= buffer.Length)
                        return count;
                    if (Math.Abs(cx - x) + Math.Abs(cy - y) <= range && (cx != x || cy != y))
                        buffer[count++] = _values[cy * Width + cx];
                }
            }

            return count;
        }

        /// <summary>
        /// Gets all values within a rectangular region.
        /// Clamps the region to grid bounds silently; out-of-bounds portions are excluded.
        /// </summary>
        /// <param name="x">The column index of the top-left corner of the region.</param>
        /// <param name="y">The row index of the top-left corner of the region.</param>
        /// <param name="width">The number of columns in the region. Must be greater than zero.</param>
        /// <param name="height">The number of rows in the region. Must be greater than zero.</param>
        /// <param name="buffer">A span to receive the region values.</param>
        /// <returns>The number of values written to <paramref name="buffer"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="width"/> or <paramref name="height"/> is less than or equal to zero.
        /// </exception>
        public int GetCellsInRect(int x, int y, int width, int height, Span<T> buffer)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

            int startX = Math.Max(0, x);
            int startY = Math.Max(0, y);
            int endX = Math.Min(Width, x + width);
            int endY = Math.Min(Height, y + height);

            int count = 0;
            for (int cy = startY; cy < endY; cy++)
            {
                for (int cx = startX; cx < endX; cx++)
                {
                    if (count >= buffer.Length)
                        return count;
                    buffer[count++] = _values[cy * Width + cx];
                }
            }

            return count;
        }

        /// <summary>
        /// Returns a read-only span over the specified row of the grid.
        /// Zero-allocation; the span points directly into the backing array.
        /// </summary>
        /// <param name="y">The row index. Must be in range [0, <see cref="Height"/>).</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> of length <see cref="Width"/> over the row.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="y"/> is outside [0, Height).</exception>
        public ReadOnlySpan<T> GetRow(int y)
        {
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y), y, $"Row index {y} is out of range. Height is {Height}.");

            return new ReadOnlySpan<T>(_values, y * Width, Width);
        }

        /// <summary>
        /// Returns a mutable span over the specified row of the grid.
        /// Zero-allocation; the span points directly into the backing array.
        /// Use for batch writes to an entire row.
        /// </summary>
        /// <param name="y">The row index. Must be in range [0, <see cref="Height"/>).</param>
        /// <returns>A <see cref="Span{T}"/> of length <see cref="Width"/> over the row.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="y"/> is outside [0, Height).</exception>
        public Span<T> GetRowMutable(int y)
        {
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y), y, $"Row index {y} is out of range. Height is {Height}.");

            return new Span<T>(_values, y * Width, Width);
        }

        /// <summary>
        /// Copies the specified column into the provided buffer.
        /// </summary>
        /// <param name="x">The column index. Must be in range [0, <see cref="Width"/>).</param>
        /// <param name="buffer">A span to receive the column values.</param>
        /// <returns>The number of values written.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="x"/> is outside [0, Width).</exception>
        public int GetColumn(int x, Span<T> buffer)
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Column index {x} is out of range. Width is {Width}.");

            int count = Math.Min(Height, buffer.Length);
            for (int y = 0; y < count; y++)
                buffer[y] = _values[y * Width + x];

            return count;
        }

        // ── Bulk Operations ───────────────────────────────────────────────

        /// <summary>
        /// Sets every cell in the grid to <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The value to fill with.</param>
        public void Fill(T value)
        {
            _values.AsSpan().Fill(value);
        }

        /// <summary>
        /// Sets all cells within a rectangular region to <paramref name="value"/>.
        /// Clamps the region to grid bounds silently.
        /// </summary>
        /// <param name="value">The value to fill with.</param>
        /// <param name="x">The column index of the top-left corner.</param>
        /// <param name="y">The row index of the top-left corner.</param>
        /// <param name="width">The number of columns in the region.</param>
        /// <param name="height">The number of rows in the region.</param>
        public void Fill(T value, int x, int y, int width, int height)
        {
            int startX = Math.Max(0, x);
            int startY = Math.Max(0, y);
            int endX = Math.Min(Width, x + width);
            int endY = Math.Min(Height, y + height);

            for (int cy = startY; cy < endY; cy++)
            {
                int rowStart = cy * Width + startX;
                int rowLength = endX - startX;
                _values.AsSpan(rowStart, rowLength).Fill(value);
            }
        }

        /// <summary>
        /// Resets every cell to <c>default(T)</c>.
        /// </summary>
        public void Clear()
        {
            _values.AsSpan().Clear();
        }

        /// <summary>
        /// Copies the entire grid contents into <paramref name="buffer"/>.
        /// Use for snapshotting grid state.
        /// </summary>
        /// <param name="buffer">A span of at least <see cref="CellCount"/> elements.</param>
        public void CopyTo(Span<T> buffer)
        {
            _values.AsSpan().CopyTo(buffer);
        }

        /// <summary>
        /// Overwrites the entire grid contents from <paramref name="source"/>.
        /// Use for restoring a previous snapshot.
        /// </summary>
        /// <param name="source">A span of exactly <see cref="CellCount"/> elements.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="source"/> length does not match <see cref="CellCount"/>.
        /// </exception>
        public void CopyFrom(ReadOnlySpan<T> source)
        {
            if (source.Length != _values.Length)
                throw new ArgumentException($"Source length {source.Length} does not match grid cell count {_values.Length}.", nameof(source));

            source.CopyTo(_values);
        }

        /// <summary>
        /// Returns a mutable span over the entire backing array.
        /// Use for direct bulk operations or interop with external algorithms.
        /// </summary>
        /// <returns>A <see cref="Span{T}"/> over all grid values in row-major order.</returns>
        public Span<T> AsSpan()
        {
            return _values.AsSpan();
        }

        /// <summary>
        /// Returns a read-only span over the entire backing array.
        /// </summary>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> over all grid values in row-major order.</returns>
        public ReadOnlySpan<T> AsReadOnlySpan()
        {
            return _values;
        }

        // ── Search & Iteration ────────────────────────────────────────────

        /// <summary>
        /// Finds the flat index of the first value that satisfies <paramref name="predicate"/>, scanning in row-major order.
        /// </summary>
        /// <param name="predicate">The condition to test against each value.</param>
        /// <returns>The flat index of the first match, or <c>-1</c> if no match is found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is null.</exception>
        public int FindIndex(Predicate<T> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _values.Length; i++)
            {
                if (predicate(_values[i]))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Finds all flat indices of values that satisfy <paramref name="predicate"/>, in row-major order.
        /// </summary>
        /// <param name="predicate">The condition to test against each value.</param>
        /// <param name="buffer">A span to receive the matching indices.</param>
        /// <returns>The number of indices written to <paramref name="buffer"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is null.</exception>
        public int FindAllIndices(Predicate<T> predicate, Span<int> buffer)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            int count = 0;
            for (int i = 0; i < _values.Length; i++)
            {
                if (count >= buffer.Length)
                    return count;
                if (predicate(_values[i]))
                    buffer[count++] = i;
            }

            return count;
        }

        /// <summary>
        /// Invokes <paramref name="action"/> for every value in the grid, in row-major order.
        /// </summary>
        /// <param name="action">The action to invoke on each value.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
        public void ForEach(Action<T> action)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < _values.Length; i++)
                action(_values[i]);
        }

        /// <summary>
        /// Returns a zero-allocation struct enumerator over all values.
        /// Enables <c>foreach (var value in grid)</c> without heap allocation.
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(_values);

        /// <inheritdoc/>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(_values);

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_values);

        /// <summary>
        /// A struct-based enumerator that avoids heap allocation when iterating with <c>foreach</c>.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] _array;
            private int _index;

            internal Enumerator(T[] array)
            {
                _array = array;
                _index = -1;
            }

            /// <inheritdoc/>
            public readonly T Current => _array[_index];

            /// <inheritdoc/>
            readonly object IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext() => ++_index < _array.Length;

            /// <inheritdoc/>
            public void Reset() => _index = -1;

            /// <inheritdoc/>
            public readonly void Dispose() { }
        }

        // ── Private Helpers ───────────────────────────────────────────────

        private bool TryGetIndex(int x, int y, out int index)
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            {
                index = y * Width + x;
                return true;
            }

            index = -1;
            return false;
        }

        private void TryAddNeighbor(Span<T> buffer, ref int count, int x, int y)
        {
            if (count < buffer.Length && TryGetIndex(x, y, out int index))
                buffer[count++] = _values[index];
        }

        private void TryAddNeighborIndex(Span<int> buffer, ref int count, int x, int y)
        {
            if (count < buffer.Length && TryGetIndex(x, y, out int index))
                buffer[count++] = index;
        }
    }
}
