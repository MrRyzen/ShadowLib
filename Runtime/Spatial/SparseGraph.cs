namespace ShadowLib.Spatial
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// A sparse, dictionary-backed graph that maps arbitrary integer indices to nodes.
    /// Only nodes that exist consume memory — no dense arrays, no wasted slots.
    /// <para>
    /// The index is caller-determined. SparseGraph does not assign or interpret indices;
    /// it simply maps <c>int → node</c>. Higher-level structures (e.g. SpatialGraph3D)
    /// can compute indices from coordinates and delegate to this graph.
    /// </para>
    /// <para>
    /// Supports degradation (mark nodes/edges dead and restore them), occupancy tracking,
    /// BFS-based graph algorithms (distance, range queries, connected components),
    /// and zero-allocation neighbor enumeration.
    /// </para>
    /// </summary>
    /// <typeparam name="TOccupant">Type stored in each node.</typeparam>
    public sealed class SparseGraph<TOccupant>
        where TOccupant : notnull
    {
        private struct NodeData
        {
            public TOccupant Occupant;
            public bool IsAlive;
            public bool IsOccupied;
        }

        private readonly Dictionary<int, NodeData> _nodes;
        private readonly Dictionary<int, List<int>> _adjacency;
        private readonly HashSet<long> _deadEdges;

        private int _aliveCount;

        // Reusable BFS buffers
        private readonly HashSet<int> _bfsVisited;
        private readonly Queue<int> _bfsQueue;

        /// <summary>
        /// Gets the total number of nodes in the graph (including dead).
        /// </summary>
        public int Count => _nodes.Count;

        /// <summary>
        /// Gets the number of alive nodes in the graph.
        /// </summary>
        public int AliveCount => _aliveCount;

        /// <summary>
        /// Creates a sparse graph with the specified initial capacity hint.
        /// </summary>
        /// <param name="initialCapacity">Initial capacity for internal collections.</param>
        public SparseGraph(int initialCapacity = 64)
        {
            _nodes = new Dictionary<int, NodeData>(initialCapacity);
            _adjacency = new Dictionary<int, List<int>>(initialCapacity);
            _deadEdges = new HashSet<long>();
            _bfsVisited = new HashSet<int>();
            _bfsQueue = new Queue<int>();
            _aliveCount = 0;
        }

        /// <summary>
        /// Adds an empty, alive node at the specified index.
        /// </summary>
        /// <param name="index">The caller-determined node index.</param>
        /// <exception cref="ArgumentException">Thrown if a node already exists at this index.</exception>
        public void AddNode(int index)
        {
            if (_nodes.ContainsKey(index))
                throw new ArgumentException($"Node {index} already exists.", nameof(index));

            _nodes[index] = new NodeData { IsAlive = true };
            _aliveCount++;
        }

        /// <summary>
        /// Adds an occupied, alive node at the specified index.
        /// </summary>
        /// <param name="index">The caller-determined node index.</param>
        /// <param name="occupant">The occupant to place in the node.</param>
        /// <exception cref="ArgumentException">Thrown if a node already exists at this index.</exception>
        public void AddNode(int index, TOccupant occupant)
        {
            if (_nodes.ContainsKey(index))
                throw new ArgumentException($"Node {index} already exists.", nameof(index));

            _nodes[index] = new NodeData
            {
                Occupant = occupant,
                IsAlive = true,
                IsOccupied = true
            };
            _aliveCount++;
        }

        /// <summary>
        /// Returns whether a node exists at the specified index (alive or dead).
        /// </summary>
        public bool ContainsNode(int index) => _nodes.ContainsKey(index);

        /// <summary>
        /// Marks a node as dead (degradation). The node remains in storage and can be restored.
        /// </summary>
        /// <param name="index">The node index.</param>
        /// <exception cref="KeyNotFoundException">Thrown if the node does not exist.</exception>
        public void RemoveNode(int index)
        {
            if (!_nodes.TryGetValue(index, out var data))
                throw new KeyNotFoundException($"Node {index} does not exist.");

            if (!data.IsAlive)
                return;

            data.IsAlive = false;
            _nodes[index] = data;
            _aliveCount--;
        }

        /// <summary>
        /// Restores a dead node to alive state.
        /// </summary>
        /// <param name="index">The node index.</param>
        /// <exception cref="KeyNotFoundException">Thrown if the node does not exist.</exception>
        public void RestoreNode(int index)
        {
            if (!_nodes.TryGetValue(index, out var data))
                throw new KeyNotFoundException($"Node {index} does not exist.");

            if (data.IsAlive)
                return;

            data.IsAlive = true;
            _nodes[index] = data;
            _aliveCount++;
        }

        /// <summary>
        /// Permanently removes a node from storage, including all its edges.
        /// </summary>
        /// <param name="index">The node index.</param>
        public void DeleteNode(int index)
        {
            if (!_nodes.TryGetValue(index, out var data))
                return;

            if (data.IsAlive)
                _aliveCount--;

            _nodes.Remove(index);

            // Remove all edges involving this node
            if (_adjacency.TryGetValue(index, out var neighbors))
            {
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int other = neighbors[i];
                    if (_adjacency.TryGetValue(other, out var otherNeighbors))
                        otherNeighbors.Remove(index);

                    // Clean dead-edge entries
                    _deadEdges.Remove(PackEdge(index, other));
                }

                _adjacency.Remove(index);
            }
        }

        /// <summary>
        /// Returns whether the node at the specified index is alive.
        /// Returns false if the node does not exist.
        /// </summary>
        public bool IsAlive(int index)
        {
            return _nodes.TryGetValue(index, out var data) && data.IsAlive;
        }

        /// <summary>
        /// Creates a bidirectional edge between two nodes.
        /// Both nodes must exist. Duplicate edges are ignored.
        /// </summary>
        public void Connect(int a, int b)
        {
            if (!_nodes.ContainsKey(a))
                throw new KeyNotFoundException($"Node {a} does not exist.");
            if (!_nodes.ContainsKey(b))
                throw new KeyNotFoundException($"Node {b} does not exist.");
            if (a == b)
                throw new ArgumentException("Cannot connect a node to itself.");

            AddEdge(a, b);
            AddEdge(b, a);

            // Ensure edge is not in dead set
            _deadEdges.Remove(PackEdge(a, b));
        }

        /// <summary>
        /// Marks an edge as dead (degradation). The edge remains in adjacency lists
        /// and can be restored with <see cref="RestoreEdge"/>.
        /// </summary>
        public void Disconnect(int a, int b)
        {
            _deadEdges.Add(PackEdge(a, b));
        }

        /// <summary>
        /// Restores a previously disconnected edge.
        /// </summary>
        public void RestoreEdge(int a, int b)
        {
            _deadEdges.Remove(PackEdge(a, b));
        }

        /// <summary>
        /// Returns whether two nodes are connected by an alive edge.
        /// Both nodes must exist and be alive, and the edge must not be dead.
        /// </summary>
        public bool AreConnected(int a, int b)
        {
            if (!_adjacency.TryGetValue(a, out var neighbors))
                return false;

            if (!neighbors.Contains(b))
                return false;

            if (_deadEdges.Contains(PackEdge(a, b)))
                return false;

            return IsAlive(a) && IsAlive(b);
        }

        /// <summary>
        /// Returns whether the node at the specified index is occupied.
        /// Returns false if the node does not exist.
        /// </summary>
        public bool IsOccupied(int index)
        {
            return _nodes.TryGetValue(index, out var data) && data.IsOccupied;
        }

        /// <summary>
        /// Gets the occupant at the specified node index.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the node does not exist.</exception>
        public TOccupant GetOccupant(int index)
        {
            if (!_nodes.TryGetValue(index, out var data))
                throw new KeyNotFoundException($"Node {index} does not exist.");

            return data.Occupant;
        }

        /// <summary>
        /// Sets the occupant at the specified node index.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the node does not exist.</exception>
        public void Occupy(int index, TOccupant occupant)
        {
            if (!_nodes.TryGetValue(index, out var data))
                throw new KeyNotFoundException($"Node {index} does not exist.");

            data.Occupant = occupant;
            data.IsOccupied = true;
            _nodes[index] = data;
        }

        /// <summary>
        /// Clears the occupant at the specified node index.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the node does not exist.</exception>
        public void Clear(int index)
        {
            if (!_nodes.TryGetValue(index, out var data))
                throw new KeyNotFoundException($"Node {index} does not exist.");

            data.Occupant = default!;
            data.IsOccupied = false;
            _nodes[index] = data;
        }

        /// <summary>
        /// Clears all occupants from all nodes.
        /// </summary>
        public void ClearAll()
        {
            var keys = new int[_nodes.Count];
            _nodes.Keys.CopyTo(keys, 0);

            for (int i = 0; i < keys.Length; i++)
            {
                var data = _nodes[keys[i]];
                data.Occupant = default!;
                data.IsOccupied = false;
                _nodes[keys[i]] = data;
            }
        }

        /// <summary>
        /// Writes alive, connected neighbors of the specified node into the buffer.
        /// Skips dead nodes and dead edges.
        /// </summary>
        /// <returns>Number of neighbors written.</returns>
        public int GetNeighbors(int index, Span<int> buffer)
        {
            if (!_adjacency.TryGetValue(index, out var neighbors))
                return 0;

            int count = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (count >= buffer.Length)
                    break;

                int neighbor = neighbors[i];
                if (IsAliveEdge(index, neighbor))
                    buffer[count++] = neighbor;
            }

            return count;
        }

        /// <summary>
        /// Returns the number of alive, connected neighbors of the specified node.
        /// </summary>
        public int GetNeighborCount(int index)
        {
            if (!_adjacency.TryGetValue(index, out var neighbors))
                return 0;

            int count = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (IsAliveEdge(index, neighbors[i]))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Returns a zero-allocation enumerator over alive, connected neighbors.
        /// </summary>
        public NeighborEnumerator GetNeighborEnumerator(int index)
        {
            if (!_adjacency.TryGetValue(index, out var neighbors))
                return new NeighborEnumerator(this, index, null);

            return new NeighborEnumerator(this, index, neighbors);
        }

        /// <summary>
        /// A struct enumerator that iterates alive, connected neighbors without allocation.
        /// </summary>
        public struct NeighborEnumerator
        {
            private readonly SparseGraph<TOccupant> _graph;
            private readonly int _source;
            private readonly List<int>? _neighbors;
            private int _index;

            internal NeighborEnumerator(SparseGraph<TOccupant> graph, int source, List<int>? neighbors)
            {
                _graph = graph;
                _source = source;
                _neighbors = neighbors;
                _index = -1;
            }

            /// <summary>Gets the current neighbor node index.</summary>
            public int Current => _neighbors![_index];

            /// <summary>Advances to the next alive, connected neighbor.</summary>
            public bool MoveNext()
            {
                if (_neighbors == null)
                    return false;

                while (++_index < _neighbors.Count)
                {
                    if (_graph.IsAliveEdge(_source, _neighbors[_index]))
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Computes the shortest path distance (hop count) between two nodes using BFS.
        /// Only traverses alive nodes and alive edges.
        /// </summary>
        /// <returns>Hop count, or -1 if unreachable.</returns>
        public int Distance(int from, int to)
        {
            if (from == to)
                return _nodes.ContainsKey(from) ? 0 : -1;

            if (!IsAlive(from) || !IsAlive(to))
                return -1;

            _bfsVisited.Clear();
            _bfsQueue.Clear();

            _bfsQueue.Enqueue(from);
            _bfsQueue.Enqueue(-1); // depth sentinel
            _bfsVisited.Add(from);
            int depth = 1;

            while (_bfsQueue.Count > 0)
            {
                int current = _bfsQueue.Dequeue();

                if (current == -1)
                {
                    if (_bfsQueue.Count == 0)
                        break;
                    depth++;
                    _bfsQueue.Enqueue(-1);
                    continue;
                }

                if (!_adjacency.TryGetValue(current, out var neighbors))
                    continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];

                    if (!_bfsVisited.Add(neighbor))
                        continue;

                    if (!IsAliveEdge(current, neighbor))
                        continue;

                    if (neighbor == to)
                        return depth;

                    _bfsQueue.Enqueue(neighbor);
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds all alive nodes reachable within the specified number of hops using BFS.
        /// The source node is excluded from the result.
        /// </summary>
        /// <param name="from">The starting node index.</param>
        /// <param name="steps">Maximum number of hops.</param>
        /// <param name="buffer">Buffer to receive reachable node indices.</param>
        /// <returns>Number of nodes written to buffer.</returns>
        public int GetNodesInRange(int from, int steps, Span<int> buffer)
        {
            if (steps < 0 || !IsAlive(from))
                return 0;

            _bfsVisited.Clear();
            _bfsQueue.Clear();

            _bfsQueue.Enqueue(from);
            _bfsQueue.Enqueue(-1); // depth sentinel
            _bfsVisited.Add(from);
            int depth = 1;
            int count = 0;

            while (_bfsQueue.Count > 0 && depth <= steps)
            {
                int current = _bfsQueue.Dequeue();

                if (current == -1)
                {
                    if (_bfsQueue.Count == 0)
                        break;
                    depth++;
                    _bfsQueue.Enqueue(-1);
                    continue;
                }

                if (!_adjacency.TryGetValue(current, out var neighbors))
                    continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];

                    if (!_bfsVisited.Add(neighbor))
                        continue;

                    if (!IsAliveEdge(current, neighbor))
                        continue;

                    if (count < buffer.Length)
                        buffer[count++] = neighbor;

                    if (depth < steps)
                        _bfsQueue.Enqueue(neighbor);
                }
            }

            return count;
        }

        /// <summary>
        /// Identifies all connected components among alive nodes.
        /// Each alive node is assigned a component ID (0-based). Dead/missing nodes get -1.
        /// </summary>
        /// <param name="componentIds">
        /// Caller-provided buffer. The caller is responsible for sizing and interpreting indices.
        /// Indices that correspond to alive nodes will be set to their component ID.
        /// Indices that correspond to dead or non-existent nodes are set to -1.
        /// </param>
        /// <returns>The number of connected components found.</returns>
        public int FindConnectedComponents(Span<int> componentIds)
        {
            componentIds.Fill(-1);

            _bfsVisited.Clear();
            int componentCount = 0;

            foreach (var kvp in _nodes)
            {
                int node = kvp.Key;
                if (!kvp.Value.IsAlive || _bfsVisited.Contains(node))
                    continue;
                if ((uint)node >= (uint)componentIds.Length)
                    continue;

                // BFS from this node to find all reachable alive nodes
                _bfsQueue.Clear();
                _bfsQueue.Enqueue(node);
                _bfsVisited.Add(node);

                while (_bfsQueue.Count > 0)
                {
                    int current = _bfsQueue.Dequeue();

                    if ((uint)current < (uint)componentIds.Length)
                        componentIds[current] = componentCount;

                    if (!_adjacency.TryGetValue(current, out var neighbors))
                        continue;

                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        int neighbor = neighbors[i];

                        if (!_bfsVisited.Add(neighbor))
                            continue;

                        if (!IsAliveEdge(current, neighbor))
                            continue;

                        _bfsQueue.Enqueue(neighbor);
                    }
                }

                componentCount++;
            }

            return componentCount;
        }

        /// <summary>
        /// Returns a struct enumerator over all alive node indices.
        /// </summary>
        public AliveEnumerator GetEnumerator() => new(this);

        /// <summary>
        /// A struct-based enumerator that yields alive node indices without allocation.
        /// Uses duck-typing for foreach support.
        /// </summary>
        public struct AliveEnumerator
        {
            private readonly SparseGraph<TOccupant> _graph;
            private IEnumerator<KeyValuePair<int, NodeData>>? _inner;
            private int _current;

            internal AliveEnumerator(SparseGraph<TOccupant> graph)
            {
                _graph = graph;
                _inner = null;
                _current = -1;
            }

            /// <summary>Gets the current alive node index.</summary>
            public int Current => _current;

            /// <summary>Advances to the next alive node.</summary>
            public bool MoveNext()
            {
                _inner ??= _graph._nodes.GetEnumerator();

                while (_inner.MoveNext())
                {
                    if (_inner.Current.Value.IsAlive)
                    {
                        _current = _inner.Current.Key;
                        return true;
                    }
                }

                return false;
            }
        }

        private void AddEdge(int from, int to)
        {
            if (!_adjacency.TryGetValue(from, out var neighbors))
            {
                neighbors = new List<int>(4);
                _adjacency[from] = neighbors;
            }

            if (!neighbors.Contains(to))
                neighbors.Add(to);
        }

        private static long PackEdge(int a, int b)
        {
            int min = a < b ? a : b;
            int max = a < b ? b : a;
            return ((long)min << 32) | (uint)max;
        }

        private bool IsAliveEdge(int from, int to)
        {
            if (_deadEdges.Contains(PackEdge(from, to)))
                return false;

            return _nodes.TryGetValue(to, out var data) && data.IsAlive;
        }
    }
}
