namespace ShadowLib.Spatial
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A width × height × layers box for a 3D inventory that wears out. Only cells that were added exist
    /// (the pockets); items are <see cref="Polycube"/> shapes that occupy cells; cells can die and be repaired.
    /// </summary>
    /// <typeparam name="TOccupant">The item type. Items are told apart with the comparer given to the constructor.</typeparam>
    /// <remarks>
    /// Memory is proportional to <see cref="Volume"/>, not to the number of cells that exist.
    /// Shapes do not rotate. This is not an <see cref="ISpace{TOccupant}"/>. Not thread-safe.
    /// To rebuild saved state: add cells, place items, then kill the dead cells; no other order
    /// can recreate an item sitting on a dead cell.
    /// </remarks>
    public sealed class SpatialGraph3D<TOccupant>
        where TOccupant : notnull
    {
        [Flags]
        private enum CellState : byte
        {
            None = 0,
            Exists = 1,
            Alive = 2,
            Occupied = 4,
        }

        private const CellState Placeable = CellState.Exists | CellState.Alive;

        private readonly CellState[] _states;
        private readonly TOccupant[] _occupants;
        private readonly Dictionary<TOccupant, int[]> _cellsByOccupant;

        /// <summary>Creates an empty box. No cells exist until <see cref="AddCell"/> or <see cref="AddPocket"/>.</summary>
        /// <param name="width">Columns. Must be greater than zero.</param>
        /// <param name="height">Rows. Must be greater than zero.</param>
        /// <param name="layers">Layers. Must be greater than zero.</param>
        /// <param name="occupantComparer">How items are told apart; the default comparer when null.</param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, or the volume does not fit in an int.</exception>
        public SpatialGraph3D(int width, int height, int layers = 1, IEqualityComparer<TOccupant>? occupantComparer = null)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
            if (layers <= 0)
                throw new ArgumentOutOfRangeException(nameof(layers), layers, "Layers must be greater than zero.");
            if ((long)width * height * layers > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), "The box volume does not fit in an int.");

            Width = width;
            Height = height;
            Layers = layers;
            Volume = width * height * layers;
            _states = new CellState[Volume];
            _occupants = new TOccupant[Volume];
            _cellsByOccupant = new Dictionary<TOccupant, int[]>(occupantComparer);
        }

        /// <summary>Gets the number of columns.</summary>
        public int Width { get; }

        /// <summary>Gets the number of rows.</summary>
        public int Height { get; }

        /// <summary>Gets the number of layers.</summary>
        public int Layers { get; }

        /// <summary>Gets the size of the box: <see cref="Width"/> × <see cref="Height"/> × <see cref="Layers"/>.</summary>
        public int Volume { get; }

        /// <summary>Gets the number of cells that exist, living or dead.</summary>
        public int ExistingCount { get; private set; }

        /// <summary>Gets the number of living cells.</summary>
        public int AliveCount { get; private set; }

        /// <summary>Returns whether the position is inside the box, whether or not a cell exists there.</summary>
        public bool IsValidPosition(int x, int y, int layer = 0)
        {
            return (uint)x < (uint)Width
                && (uint)y < (uint)Height
                && (uint)layer < (uint)Layers;
        }

        /// <summary>
        /// Converts a position to its flat index, <c>layer * Width * Height + y * Width + x</c>.
        /// Only meaningful for positions inside the box.
        /// </summary>
        public int GetIndex(int x, int y, int layer = 0)
        {
            return layer * Width * Height + y * Width + x;
        }

        /// <summary>Converts a flat index back to its position.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside 0..Volume-1.</exception>
        public (int X, int Y, int Layer) GetPosition(int index)
        {
            if ((uint)index >= (uint)Volume)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index is outside the box.");

            int layerSize = Width * Height;
            int inLayer = index % layerSize;
            return (inLayer % Width, inLayer / Width, index / layerSize);
        }

        /// <summary>Adds a living, empty cell.</summary>
        /// <returns>False if a cell already exists there (a dead cell is not revived).</returns>
        /// <exception cref="ArgumentOutOfRangeException">The position is outside the box.</exception>
        public bool AddCell(int x, int y, int layer = 0)
        {
            if (!IsValidPosition(x, y, layer))
                throw new ArgumentOutOfRangeException(nameof(x), $"Position ({x}, {y}, {layer}) is outside the {Width}x{Height}x{Layers} box.");

            return AddCellAt(GetIndex(x, y, layer));
        }

        /// <summary>Adds a living, empty cell for every cell of the shape. Cells that already exist are left as they are.</summary>
        /// <returns>The number of cells created.</returns>
        /// <exception cref="ArgumentException">The shape has no cells.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Part of the shape is outside the box; nothing is created.</exception>
        public int AddPocket(Polycube shape, int x, int y, int layer = 0)
        {
            if (shape.CellCount == 0)
                throw new ArgumentException("The shape has no cells.", nameof(shape));

            var offsets = shape.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!IsValidPosition(x + offsets[i].Col, y + offsets[i].Row, layer + offsets[i].Layer))
                    throw new ArgumentOutOfRangeException(nameof(shape), $"The shape at ({x}, {y}, {layer}) leaves the {Width}x{Height}x{Layers} box.");
            }

            int created = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (AddCellAt(GetIndex(x + offsets[i].Col, y + offsets[i].Row, layer + offsets[i].Layer)))
                    created++;
            }
            return created;
        }

        /// <summary>Returns whether a cell exists at the position, living or dead.</summary>
        public bool Exists(int x, int y, int layer = 0) => Has(x, y, layer, CellState.Exists);

        /// <summary>Returns whether a living cell exists at the position.</summary>
        public bool IsAlive(int x, int y, int layer = 0) => Has(x, y, layer, CellState.Alive);

        /// <summary>Returns whether the cell at the position holds an item. A dead cell can still hold one.</summary>
        public bool IsOccupied(int x, int y, int layer = 0) => Has(x, y, layer, CellState.Occupied);

        /// <summary>Gets the item on the cell at the position. A dead cell still reports its item.</summary>
        /// <returns>False if there is no cell there or it is empty. Read the result, not <paramref name="occupant"/>, to tell.</returns>
        public bool TryGetOccupant(int x, int y, int layer, out TOccupant occupant)
        {
            if (!IsOccupied(x, y, layer))
            {
                occupant = default!;
                return false;
            }

            occupant = _occupants[GetIndex(x, y, layer)];
            return true;
        }

        /// <summary>Returns whether every cell of the shape lands on a cell that exists, is alive and is empty.</summary>
        public bool CanPlace(int x, int y, int layer, Polycube shape)
        {
            var offsets = shape.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                int cellX = x + offsets[i].Col;
                int cellY = y + offsets[i].Row;
                int cellLayer = layer + offsets[i].Layer;
                if (!IsValidPosition(cellX, cellY, cellLayer) || _states[GetIndex(cellX, cellY, cellLayer)] != Placeable)
                    return false;
            }
            return offsets.Length > 0;
        }

        /// <summary>Places an item on every cell of the shape, or on none.</summary>
        /// <returns>False, changing nothing, if <see cref="CanPlace"/> is false.</returns>
        /// <exception cref="ArgumentException">The item is already placed. Move it with <see cref="Free"/> then <see cref="Place"/>.</exception>
        public bool Place(int x, int y, int layer, TOccupant occupant, Polycube shape)
        {
            if (_cellsByOccupant.ContainsKey(occupant))
                throw new ArgumentException("The occupant is already placed.", nameof(occupant));
            if (!CanPlace(x, y, layer, shape))
                return false;

            var offsets = shape.Offsets;
            var cells = new int[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                int index = GetIndex(x + offsets[i].Col, y + offsets[i].Row, layer + offsets[i].Layer);
                cells[i] = index;
                _states[index] |= CellState.Occupied;
                _occupants[index] = occupant;
            }

            Array.Sort(cells);
            _cellsByOccupant.Add(occupant, cells);
            return true;
        }

        /// <summary>Removes an item from every cell it holds, including dead ones.</summary>
        /// <returns>False if the item was not placed.</returns>
        public bool Free(TOccupant occupant)
        {
            if (!_cellsByOccupant.Remove(occupant, out int[]? cells))
                return false;

            for (int i = 0; i < cells.Length; i++)
            {
                _states[cells[i]] &= ~CellState.Occupied;
                _occupants[cells[i]] = default!;
            }
            return true;
        }

        /// <summary>Returns whether the item is placed.</summary>
        public bool Contains(TOccupant occupant) => _cellsByOccupant.ContainsKey(occupant);

        /// <summary>Writes the positions an item holds, in ascending <see cref="GetIndex"/> order, dead cells included.</summary>
        /// <returns>The number of positions written: at most <c>buffer.Length</c>, and 0 for an item that is not placed.</returns>
        public int GetCells(TOccupant occupant, Span<(int X, int Y, int Layer)> buffer)
        {
            if (!_cellsByOccupant.TryGetValue(occupant, out int[]? cells))
                return 0;

            int count = Math.Min(cells.Length, buffer.Length);
            for (int i = 0; i < count; i++)
                buffer[i] = GetPosition(cells[i]);
            return count;
        }

        private bool Has(int x, int y, int layer, CellState flag)
        {
            return IsValidPosition(x, y, layer) && (_states[GetIndex(x, y, layer)] & flag) != 0;
        }

        private bool AddCellAt(int index)
        {
            if (_states[index] != CellState.None)
                return false;

            _states[index] = CellState.Exists | CellState.Alive;
            ExistingCount++;
            AliveCount++;
            return true;
        }
    }
}
