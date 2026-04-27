namespace ShadowLib.Spatial
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// A flat-array-backed 2D spatial grid that stores cells of type <typeparamref name="TCell"/>,
    /// each capable of holding an occupant of type <typeparamref name="TOccupant"/>.
    /// </summary>
    /// <typeparam name="TCell">The concrete cell type implementing <see cref="ICell{TOccupant}"/>.</typeparam>
    /// <typeparam name="TOccupant">The type of occupant data stored in each cell.</typeparam>
    public class Grid2D<TCell, TOccupant> : IEnumerable<TCell>, ISpace<TOccupant>
        where TCell : class, ICell<TOccupant>
        where TOccupant : notnull
    {
        private readonly TCell[] _cells;
        private readonly Dictionary<TOccupant, HashSet<int>> _occupancyIndex;

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
        public int CellCount => _cells.Length;

        /// <summary>
        /// Empty constructor for serialization purposes.
        /// </summary>
        public Grid2D()
        {
            _cells = Array.Empty<TCell>();
            _occupancyIndex = new Dictionary<TOccupant, HashSet<int>>();
        }

        /// <summary>
        /// Empty constructor for serialization purposes, with a custom equality comparer for occupant keys.
        /// </summary>
        /// <param name="occupantComparer">The equality comparer used for occupant identity in the internal index.</param>
        public Grid2D(IEqualityComparer<TOccupant> occupantComparer)
        {
            _cells = Array.Empty<TCell>();
            _occupancyIndex = new Dictionary<TOccupant, HashSet<int>>(occupantComparer);
        }

        /// <summary>
        /// Initializes a new <see cref="Grid2D{TCell, TOccupant}"/> and creates all cells using the factory.
        /// </summary>
        /// <param name="width">Number of columns. Must be greater than zero.</param>
        /// <param name="height">Number of rows. Must be greater than zero.</param>
        /// <param name="cellSize">World-space size of each cell, used for coordinate conversion.</param>
        /// <param name="cellFactory">
        /// A delegate invoked for each cell position to create the cell instance.
        /// Receives the (x, y) coordinates and must return a non-null <typeparamref name="TCell"/>.
        /// The grid will call <see cref="ICell{TOccupant}.Initialize"/> on the returned instance.
        /// </param>
        /// <param name="onCellCreated">
        /// Optional callback invoked for each cell after it is initialized.
        /// Use this for additional setup, such as assigning a Unity transform position.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="width"/> or <paramref name="height"/> is less than or equal to zero.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cellFactory"/> is null.</exception>
        /// <param name="originX">World-space X origin of the grid. Defaults to 0.</param>
        /// <param name="originY">World-space Y origin of the grid. Defaults to 0.</param>
        /// <param name="occupantComparer">
        /// Optional equality comparer for occupant keys in the internal index.
        /// Use this when <typeparamref name="TOccupant"/> needs custom equality semantics.
        /// </param>
        public Grid2D(int width, int height, float cellSize, Func<int, int, TCell> cellFactory, Action<TCell>? onCellCreated = null, float originX = 0f, float originY = 0f, IEqualityComparer<TOccupant>? occupantComparer = null)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

            if (cellFactory is null)
                throw new ArgumentNullException(nameof(cellFactory));

            Width = width;
            Height = height;
            CellSize = cellSize;
            OriginX = originX;
            OriginY = originY;
            _cells = new TCell[width * height];
            _occupancyIndex = occupantComparer != null
                ? new Dictionary<TOccupant, HashSet<int>>(occupantComparer)
                : new Dictionary<TOccupant, HashSet<int>>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var cell = cellFactory(x, y);
                    cell.Initialize(x, y);
                    int index = y * width + x;
                    _cells[index] = cell;
                    onCellCreated?.Invoke(cell);
                }
            }
        }

        /// <summary>
        /// Gets the cell at the specified grid coordinates.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>The <typeparamref name="TCell"/> at position (<paramref name="x"/>, <paramref name="y"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="x"/> or <paramref name="y"/> is outside grid bounds.
        /// </exception>
        public TCell this[int x, int y] => GetCell(x, y);

        /// <summary>
        /// Gets the cell at the specified grid coordinates.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <returns>The <typeparamref name="TCell"/> at position (<paramref name="x"/>, <paramref name="y"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="x"/> or <paramref name="y"/> is outside grid bounds.
        /// </exception>
        public TCell GetCell(int x, int y)
        {
            if (!TryGetIndex(x, y, out int index))
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Cell coordinates ({x}, {y}) are out of bounds. Grid is {Width}x{Height}.");

            return _cells[index];
        }

        /// <summary>
        /// Attempts to get the cell at the specified coordinates without throwing on out-of-bounds input.
        /// </summary>
        /// <param name="x">The column index.</param>
        /// <param name="y">The row index.</param>
        /// <param name="cell">
        /// When this method returns <see langword="true"/>, contains the cell at (<paramref name="x"/>, <paramref name="y"/>);
        /// otherwise <see langword="null"/>.
        /// </param>
        /// <returns><see langword="true"/> if the coordinates are within bounds; otherwise <see langword="false"/>.</returns>
        public bool TryGetCell(int x, int y, out TCell? cell)
        {
            if (!TryGetIndex(x, y, out int index))
            {
                cell = null;
                return false;
            }

            cell = _cells[index];
            return true;
        }

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
            if (index < 0 || index >= _cells.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index {index} is out of range. Grid has {_cells.Length} cells.");

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

        private bool CanOccupyCell(int x, int y)
        {
            if (!TryGetIndex(x, y, out int index))
                return false;

            var cell = _cells[index];
            return cell != null && !cell.IsOccupied;
        }


        /// <inheritdoc/>
        public bool CanOccupy(int x, int y, Polyomino shape)
        {
            var offsets = shape.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!CanOccupyCell(x + offsets[i].Col, y + offsets[i].Row))
                    return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public bool Occupy(int x, int y, TOccupant occupant, Polyomino shape)
        {
            if (!CanOccupy(x, y, shape))
                return false;

            var offsets = shape.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                int cx = x + offsets[i].Col;
                int cy = y + offsets[i].Row;
                int index = cy * Width + cx;
                _cells[index].Occupy(occupant);
                TrackOccupant(occupant, index);
            }

            return true;
        }

        /// <inheritdoc/>
        public void Free(TOccupant occupant)
        {
            if (!_occupancyIndex.TryGetValue(occupant, out var indices))
                return;

            Span<int> snapshot = stackalloc int[indices.Count];

            int n = 0;
            foreach (int i in indices)
                snapshot[n++] = i;

            for (int j = 0; j < n; j++)
            {
                _cells[snapshot[j]].Clear();
                UntrackOccupant(occupant, snapshot[j]);
            }
        }

        /// <inheritdoc/>
        public bool IsOccupied(int x, int y)
        {
            if (!TryGetIndex(x, y, out int index))
                return false;

            return _cells[index].IsOccupied;
        }

        /// <summary>
        /// Moves the occupant from one cell to another.
        /// The source cell must be occupied and the destination cell must be unoccupied.
        /// </summary>
        /// <param name="fromX">The column index of the source cell.</param>
        /// <param name="fromY">The row index of the source cell.</param>
        /// <param name="toX">The column index of the destination cell.</param>
        /// <param name="toY">The row index of the destination cell.</param>
        /// <returns>
        /// <see langword="true"/> if the move succeeded;
        /// <see langword="false"/> if the source is unoccupied or the destination is occupied.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when either pair of coordinates is outside grid bounds.
        /// </exception>
        public bool MoveOccupant(int fromX, int fromY, int toX, int toY)
        {
            if (!TryGetIndex(fromX, fromY, out int fromIndex))
                throw new ArgumentOutOfRangeException(nameof(fromX), fromX, $"Source coordinates ({fromX}, {fromY}) are out of bounds. Grid is {Width}x{Height}.");
            if (!TryGetIndex(toX, toY, out int toIndex))
                throw new ArgumentOutOfRangeException(nameof(toX), toX, $"Destination coordinates ({toX}, {toY}) are out of bounds. Grid is {Width}x{Height}.");

            var source = _cells[fromIndex];
            if (!source.IsOccupied)
                return false;

            var destination = _cells[toIndex];
            if (destination.IsOccupied)
                return false;

            var occupant = source.Occupant;
            destination.Occupy(occupant);
            source.Clear();

            if (_occupancyIndex.TryGetValue(occupant, out var indices))
            {
                indices.Remove(fromIndex);
                indices.Add(toIndex);
            }

            return true;
        }

        /// <summary>
        /// Gets the four cardinal neighbors (up, down, left, right) of the cell at the specified coordinates.
        /// Only includes neighbors that are within grid bounds.
        /// </summary>
        /// <param name="x">The column index of the source cell.</param>
        /// <param name="y">The row index of the source cell.</param>
        /// <returns>A list containing between 0 and 4 neighboring cells.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public int GetNeighbors(int x, int y, Span<TCell> buffer)
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
        /// Gets all eight neighbors (cardinal and diagonal) of the cell at the specified coordinates.
        /// Only includes neighbors that are within grid bounds.
        /// </summary>
        /// <param name="x">The column index of the source cell.</param>
        /// <param name="y">The row index of the source cell.</param>
        /// <returns>A list containing between 0 and 8 neighboring cells.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds.
        /// </exception>
        public int GetNeighbors8(int x, int y, Span<TCell> buffer)
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
        /// Gets all cells within a given Manhattan distance of the specified coordinates.
        /// The source cell itself is excluded from the result.
        /// </summary>
        /// <param name="x">The column index of the origin cell.</param>
        /// <param name="y">The row index of the origin cell.</param>
        /// <param name="range">The maximum Manhattan distance. Must be greater than or equal to zero.</param>
        /// <returns>A list of all cells within range, in row-major order.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the coordinates are outside grid bounds, or when <paramref name="range"/> is negative.
        /// </exception>
        public int GetCellsInRange(int x, int y, int range, Span<TCell> buffer)
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
                        buffer[count++] = _cells[cy * Width + cx];
                }
            }

            return count;
        }

        /// <summary>
        /// Gets all cells within a rectangular region.
        /// Clamps the region to grid bounds silently; out-of-bounds portions are excluded.
        /// </summary>
        /// <param name="x">The column index of the top-left corner of the region.</param>
        /// <param name="y">The row index of the top-left corner of the region.</param>
        /// <param name="width">The number of columns in the region. Must be greater than zero.</param>
        /// <param name="height">The number of rows in the region. Must be greater than zero.</param>
        /// <returns>A list of all cells within the (clamped) rectangular region.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="width"/> or <paramref name="height"/> is less than or equal to zero.
        /// </exception>
        public int GetCellsInRect(int x, int y, int width, int height, Span<TCell> buffer)
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
                    buffer[count++] = _cells[cy * Width + cx];
                }
            }

            return count;
        }

        /// <summary>
        /// Returns a read-only span over the specified row of the grid.
        /// Zero-allocation; the span points directly into the backing array.
        /// </summary>
        /// <param name="y">The row index. Must be in range [0, <see cref="Height"/>).</param>
        /// <returns>A <see cref="ReadOnlySpan{TCell}"/> of length <see cref="Width"/> over the row.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="y"/> is outside [0, Height).</exception>
        public ReadOnlySpan<TCell> GetRow(int y)
        {
            if (y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException(nameof(y), y, $"Row index {y} is out of range. Height is {Height}.");

            return new ReadOnlySpan<TCell>(_cells, y * Width, Width);
        }

        /// <summary>
        /// Copies the specified column into the provided buffer.
        /// </summary>
        /// <param name="x">The column index. Must be in range [0, <see cref="Width"/>).</param>
        /// <param name="buffer">A span of at least <see cref="Height"/> elements to receive the column cells.</param>
        /// <returns>The number of cells written (always <see cref="Height"/>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="x"/> is outside [0, Width).</exception>
        public int GetColumn(int x, Span<TCell> buffer)
        {
            if (x < 0 || x >= Width)
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Column index {x} is out of range. Width is {Width}.");

            int count = Math.Min(Height, buffer.Length);
            for (int y = 0; y < count; y++)
                buffer[y] = _cells[y * Width + x];

            return count;
        }

        /// <summary>
        /// Returns the internal occupancy index mapping each distinct occupant to the set of cell indices it occupies.
        /// The returned dictionary is a read-only view; do not cache across mutation calls.
        /// </summary>
        /// <returns>A read-only reference to the internal occupancy index.</returns>
        public IReadOnlyDictionary<TOccupant, HashSet<int>> GetOccupiedCells()
        {
            return _occupancyIndex;
        }

        /// <summary>
        /// Writes the flat cell indices that contain the specified occupant into <paramref name="buffer"/>.
        /// O(k) where k is the number of cells occupied by the occupant, backed by an internal index.
        /// </summary>
        /// <param name="occupant">The occupant to search for.</param>
        /// <param name="buffer">A span to receive the matching indices.</param>
        /// <returns>The number of indices written to <paramref name="buffer"/>.</returns>
        public int GetCellsForOccupant(TOccupant occupant, Span<int> buffer)
        {
            if (!_occupancyIndex.TryGetValue(occupant, out var indices))
                return 0;

            int count = 0;
            foreach (int i in indices)
            {
                if (count >= buffer.Length)
                    return count;
                buffer[count++] = i;
            }

            return count;
        }

        /// <summary>
        /// Finds the first cell that satisfies <paramref name="predicate"/>, scanning in row-major order.
        /// </summary>
        /// <param name="predicate">The condition to test against each cell.</param>
        /// <returns>
        /// The first matching <typeparamref name="TCell"/>, or <see langword="null"/> if no match is found.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is null.</exception>
        public TCell? FindCell(Predicate<TCell> predicate)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            for (int i = 0; i < _cells.Length; i++)
            {
                if (predicate(_cells[i]))
                    return _cells[i];
            }

            return null;
        }

        /// <summary>
        /// Finds all cells that satisfy <paramref name="predicate"/>, in row-major order.
        /// </summary>
        /// <param name="predicate">The condition to test against each cell.</param>
        /// <returns>A list of all matching cells. Empty list if no matches.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is null.</exception>
        public int FindAllCells(Predicate<TCell> predicate, Span<TCell> buffer)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));

            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (count >= buffer.Length)
                    return count;
                if (predicate(_cells[i]))
                    buffer[count++] = _cells[i];
            }

            return count;
        }

        /// <summary>
        /// Clears all cells in the grid, removing all occupants.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _cells.Length; i++)
                _cells[i].Clear();

            _occupancyIndex.Clear();
        }

        /// <summary>
        /// Invokes <paramref name="action"/> for every cell in the grid, in row-major order.
        /// </summary>
        /// <param name="action">The action to invoke on each cell.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
        public void ForEach(Action<TCell> action)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < _cells.Length; i++)
                action(_cells[i]);
        }

        /// <summary>
        /// Returns a zero-allocation struct enumerator over all cells.
        /// Enables <c>foreach (var cell in grid)</c> without heap allocation.
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(_cells);

        /// <inheritdoc/>
        IEnumerator<TCell> IEnumerable<TCell>.GetEnumerator() => new Enumerator(_cells);

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_cells);

        /// <summary>
        /// A struct-based enumerator that avoids heap allocation when iterating with <c>foreach</c>.
        /// </summary>
        public struct Enumerator : IEnumerator<TCell>
        {
            private readonly TCell[] _array;
            private int _index;

            internal Enumerator(TCell[] array)
            {
                _array = array;
                _index = -1;
            }

            /// <inheritdoc/>
            public readonly TCell Current => _array[_index];

            /// <inheritdoc/>
            readonly object IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext() => ++_index < _array.Length;

            /// <inheritdoc/>
            public void Reset() => _index = -1;

            /// <inheritdoc/>
            public readonly void Dispose() { }
        }

        private void TrackOccupant(TOccupant occupant, int index)
        {
            if (!_occupancyIndex.TryGetValue(occupant, out var indices))
            {
                indices = new HashSet<int>();
                _occupancyIndex[occupant] = indices;
            }

            indices.Add(index);
        }

        private void UntrackOccupant(TOccupant occupant, int index)
        {
            if (!_occupancyIndex.TryGetValue(occupant, out var indices))
                return;

            indices.Remove(index);
            if (indices.Count == 0)
                _occupancyIndex.Remove(occupant);
        }

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

        private void TryAddNeighbor(Span<TCell> buffer, ref int count, int x, int y)
        {
            if (count < buffer.Length && TryGetIndex(x, y, out int index))
                buffer[count++] = _cells[index];
        }
    }
}
