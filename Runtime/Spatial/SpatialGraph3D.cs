namespace ShadowLib.Spatial
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A spatial layer over <see cref="SparseGraph{TOccupant}"/> that maps (x, y, layer) coordinates
    /// to flat integer indices via <c>layer * Width * Height + y * Width + x</c>.
    /// <para>
    /// Only nodes that are explicitly added exist in the underlying graph — no dense allocation.
    /// Supports pocket generation from <see cref="Polyomino"/> shapes with auto-wiring of 4-adjacent
    /// neighbors, cross-layer connections, and polyomino-based occupancy placement.
    /// </para>
    /// </summary>
    /// <typeparam name="TOccupant">Type stored in each node.</typeparam>
    public sealed class SpatialGraph3D<TOccupant>
        where TOccupant : notnull
    {
        private readonly Dictionary<TOccupant, List<int>> _occupancyIndex;

        /// <summary>
        /// Gets the number of columns in the coordinate space.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the number of rows in the coordinate space.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the number of layers in the coordinate space.
        /// </summary>
        public int Layers { get; }

        /// <summary>
        /// Gets the underlying sparse graph. Use for direct topology access
        /// (neighbors, BFS algorithms, degradation, occupancy by index).
        /// </summary>
        public SparseGraph<TOccupant> Graph { get; }

        /// <summary>
        /// Creates a new spatial graph with the specified coordinate bounds.
        /// No nodes are created — use <see cref="AddPocket"/> or <see cref="AddNode"/> to populate.
        /// </summary>
        /// <param name="width">Number of columns. Must be greater than zero.</param>
        /// <param name="height">Number of rows. Must be greater than zero.</param>
        /// <param name="layers">Number of layers. Must be greater than zero.</param>
        /// <param name="initialCapacity">Initial capacity hint for the underlying graph.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any dimension is less than or equal to zero.
        /// </exception>
        public SpatialGraph3D(int width, int height, int layers = 1, int initialCapacity = 64)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
            if (layers <= 0)
                throw new ArgumentOutOfRangeException(nameof(layers), layers, "Layers must be greater than zero.");

            Width = width;
            Height = height;
            Layers = layers;
            Graph = new SparseGraph<TOccupant>(initialCapacity);
            _occupancyIndex = new Dictionary<TOccupant, List<int>>();
        }

        /// <summary>
        /// Converts (x, y, layer) coordinates to a flat node index.
        /// </summary>
        public int GetIndex(int x, int y, int layer = 0)
        {
            return layer * Width * Height + y * Width + x;
        }

        /// <summary>
        /// Converts a flat node index back to (x, y, layer) coordinates.
        /// </summary>
        public (int X, int Y, int Layer) GetPosition(int index)
        {
            int layerSize = Width * Height;
            int layer = index / layerSize;
            int remainder = index % layerSize;
            return (remainder % Width, remainder / Width, layer);
        }

        /// <summary>
        /// Returns whether the specified coordinates are within the coordinate bounds.
        /// Does not check whether a node actually exists at this position.
        /// </summary>
        public bool IsValidPosition(int x, int y, int layer = 0)
        {
            return (uint)x < (uint)Width
                && (uint)y < (uint)Height
                && (uint)layer < (uint)Layers;
        }

        /// <summary>
        /// Returns whether a node exists at the specified coordinates in the underlying graph.
        /// </summary>
        public bool NodeExists(int x, int y, int layer = 0)
        {
            if (!IsValidPosition(x, y, layer))
                return false;

            return Graph.ContainsNode(GetIndex(x, y, layer));
        }

        /// <summary>
        /// Stamps a polyomino shape as alive nodes at the specified anchor position.
        /// Automatically connects each new node to existing 4-adjacent neighbors.
        /// Positions that already have nodes are skipped.
        /// </summary>
        /// <param name="shape">The polyomino shape defining which cells to create.</param>
        /// <param name="anchorX">The column of the placement anchor.</param>
        /// <param name="anchorY">The row of the placement anchor.</param>
        /// <param name="layer">The layer to place the pocket on.</param>
        /// <returns>The number of new nodes created.</returns>
        public int AddPocket(Polyomino shape, int anchorX, int anchorY, int layer = 0)
        {
            var offsets = shape.Offsets;
            int created = 0;

            // First pass: create nodes
            for (int i = 0; i < offsets.Length; i++)
            {
                int x = anchorX + offsets[i].Col;
                int y = anchorY + offsets[i].Row;

                if (!IsValidPosition(x, y, layer))
                    continue;

                int index = GetIndex(x, y, layer);
                if (Graph.ContainsNode(index))
                    continue;

                Graph.AddNode(index);
                created++;
            }

            // Second pass: auto-connect 4-adjacent
            for (int i = 0; i < offsets.Length; i++)
            {
                int x = anchorX + offsets[i].Col;
                int y = anchorY + offsets[i].Row;

                if (!IsValidPosition(x, y, layer))
                    continue;

                ConnectAdjacent(x, y, layer);
            }

            return created;
        }

        /// <summary>
        /// Adds a single node at the specified coordinates.
        /// Does nothing if a node already exists there.
        /// </summary>
        /// <returns><see langword="true"/> if a new node was created; <see langword="false"/> if one already existed.</returns>
        public bool AddNode(int x, int y, int layer = 0)
        {
            if (!IsValidPosition(x, y, layer))
                throw new ArgumentOutOfRangeException(nameof(x), $"Position ({x}, {y}, {layer}) is outside bounds ({Width}x{Height}x{Layers}).");

            int index = GetIndex(x, y, layer);
            if (Graph.ContainsNode(index))
                return false;

            Graph.AddNode(index);
            return true;
        }

        /// <summary>
        /// Connects the node at the specified position to its existing 4-adjacent neighbors
        /// (left, right, up, down) on the same layer.
        /// </summary>
        public void ConnectAdjacent(int x, int y, int layer = 0)
        {
            int index = GetIndex(x, y, layer);
            if (!Graph.ContainsNode(index))
                return;

            TryConnect(index, x - 1, y, layer);
            TryConnect(index, x + 1, y, layer);
            TryConnect(index, x, y - 1, layer);
            TryConnect(index, x, y + 1, layer);
        }

        /// <summary>
        /// Creates a bidirectional edge between nodes at the same (x, y) position on two different layers.
        /// Both nodes must exist.
        /// </summary>
        public void ConnectLayers(int x, int y, int layer1, int layer2)
        {
            if (!IsValidPosition(x, y, layer1) || !IsValidPosition(x, y, layer2))
                throw new ArgumentOutOfRangeException(nameof(layer1), "One or both layer positions are out of bounds.");

            int a = GetIndex(x, y, layer1);
            int b = GetIndex(x, y, layer2);

            Graph.Connect(a, b);
        }

        /// <summary>
        /// Checks whether a polyomino shape can be placed at the specified anchor position.
        /// Each offset must map to an existing, alive, unoccupied node.
        /// </summary>
        public bool CanPlace(int anchorX, int anchorY, int layer, Polyomino shape)
        {
            var offsets = shape.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                int x = anchorX + offsets[i].Col;
                int y = anchorY + offsets[i].Row;

                if (!IsValidPosition(x, y, layer))
                    return false;

                int index = GetIndex(x, y, layer);
                if (!Graph.IsAlive(index) || Graph.IsOccupied(index))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Places an occupant on all nodes covered by the polyomino shape.
        /// All-or-nothing: if any cell cannot be occupied, no cells are modified.
        /// </summary>
        /// <returns><see langword="true"/> if placement succeeded.</returns>
        public bool Place(int anchorX, int anchorY, int layer, TOccupant occupant, Polyomino shape)
        {
            if (!CanPlace(anchorX, anchorY, layer, shape))
                return false;

            var offsets = shape.Offsets;
            if (!_occupancyIndex.TryGetValue(occupant, out var indices))
            {
                indices = new List<int>(offsets.Length);
                _occupancyIndex[occupant] = indices;
            }

            for (int i = 0; i < offsets.Length; i++)
            {
                int x = anchorX + offsets[i].Col;
                int y = anchorY + offsets[i].Row;
                int index = GetIndex(x, y, layer);

                Graph.Occupy(index, occupant);
                indices.Add(index);
            }

            return true;
        }

        /// <summary>
        /// Removes the specified occupant from all nodes it currently occupies.
        /// </summary>
        public void Free(TOccupant occupant)
        {
            if (!_occupancyIndex.TryGetValue(occupant, out var indices))
                return;

            for (int i = 0; i < indices.Count; i++)
                Graph.Clear(indices[i]);

            _occupancyIndex.Remove(occupant);
        }

        private void TryConnect(int fromIndex, int x, int y, int layer)
        {
            if (!IsValidPosition(x, y, layer))
                return;

            int toIndex = GetIndex(x, y, layer);
            if (Graph.ContainsNode(toIndex))
                Graph.Connect(fromIndex, toIndex);
        }
    }
}
