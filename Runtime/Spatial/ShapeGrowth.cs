namespace ShadowLib.Spatial
{
    using System;
    using System.Collections.Generic;
    using ShadowLib.RNG.Sources;

    internal static class ShapeGrowth
    {
        private const double Steepness = 32.0;

        private static readonly (int Col, int Row, int Layer)[] FaceSteps =
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
        };

        internal static (int Col, int Row, int Layer)[] Grow(
            IRandom rng, int cellCount, float compactness, int maxWidth, int maxHeight, int maxDepth)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (cellCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellCount), cellCount, "Cell count must be greater than zero.");
            if (!(compactness >= 0f && compactness <= 1f))
                throw new ArgumentOutOfRangeException(nameof(compactness), compactness, "Compactness must be between 0 and 1.");
            if (maxWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "Maximum width must be greater than zero.");
            if (maxHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHeight), maxHeight, "Maximum height must be greater than zero.");
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "Maximum depth must be greater than zero.");

            long capacity = Math.Min((long)maxWidth * maxHeight, int.MaxValue) * maxDepth;
            if (cellCount > capacity)
                throw new ArgumentOutOfRangeException(nameof(cellCount), cellCount, "The size limits cannot hold that many cells.");

            var growth = new Growth(WeightsByTouchCount(compactness), maxWidth, maxHeight, maxDepth, cellCount);
            growth.Add((0, 0, 0));
            while (growth.Count < cellCount)
                growth.Add(growth.TakeFromFrontier(rng.NextFloat()));

            return growth.Cells;
        }

        private static double[] WeightsByTouchCount(double compactness)
        {
            double perExtraTouch = (1.0 + (Steepness - 1.0) * compactness) / (Steepness - (Steepness - 1.0) * compactness);
            var weights = new double[FaceSteps.Length + 1];
            weights[1] = 1.0;
            for (int touching = 2; touching < weights.Length; touching++)
                weights[touching] = weights[touching - 1] * perExtraTouch;

            return weights;
        }

        private sealed class Growth
        {
            private readonly double[] _weights;
            private readonly (long Col, long Row, long Layer) _limit;
            private readonly HashSet<(int, int, int)> _shape = new HashSet<(int, int, int)>();
            private readonly Dictionary<(int, int, int), int> _touchCounts = new Dictionary<(int, int, int), int>();
            private readonly List<(int Col, int Row, int Layer)> _frontier = new List<(int Col, int Row, int Layer)>();
            private (int Col, int Row, int Layer) _min;
            private (int Col, int Row, int Layer) _max;

            internal Growth(double[] weights, int maxWidth, int maxHeight, int maxDepth, int cellCount)
            {
                _weights = weights;
                _limit = (maxWidth, maxHeight, maxDepth);
                Cells = new (int Col, int Row, int Layer)[cellCount];
            }

            internal (int Col, int Row, int Layer)[] Cells { get; }

            internal int Count { get; private set; }

            internal void Add((int Col, int Row, int Layer) cell)
            {
                Cells[Count++] = cell;
                _shape.Add(cell);
                _min = (Math.Min(_min.Col, cell.Col), Math.Min(_min.Row, cell.Row), Math.Min(_min.Layer, cell.Layer));
                _max = (Math.Max(_max.Col, cell.Col), Math.Max(_max.Row, cell.Row), Math.Max(_max.Layer, cell.Layer));

                foreach (var step in FaceSteps)
                {
                    var neighbour = (cell.Col + step.Col, cell.Row + step.Row, cell.Layer + step.Layer);
                    if (_shape.Contains(neighbour))
                        continue;

                    if (_touchCounts.TryGetValue(neighbour, out int touching))
                    {
                        _touchCounts[neighbour] = touching + 1;
                    }
                    else
                    {
                        _touchCounts[neighbour] = 1;
                        _frontier.Add(neighbour);
                    }
                }
            }

            // ponytail: two linear frontier walks per cell, O(cellCount^2) overall; a Fenwick tree if shapes reach thousands of cells
            internal (int Col, int Row, int Layer) TakeFromFrontier(float draw)
            {
                double total = 0.0;
                for (int i = 0; i < _frontier.Count; i++)
                {
                    if (FitsLimits(_frontier[i]))
                        total += _weights[_touchCounts[_frontier[i]]];
                }

                double target = draw * total;
                double cumulative = 0.0;
                int picked = -1;
                for (int i = 0; i < _frontier.Count; i++)
                {
                    if (!FitsLimits(_frontier[i]))
                        continue;

                    picked = i;
                    cumulative += _weights[_touchCounts[_frontier[i]]];
                    if (target < cumulative)
                        break;
                }

                var cell = _frontier[picked];
                _frontier.RemoveAt(picked);
                _touchCounts.Remove(cell);
                return cell;
            }

            private bool FitsLimits((int Col, int Row, int Layer) cell) =>
                (long)Math.Max(_max.Col, cell.Col) - Math.Min(_min.Col, cell.Col) < _limit.Col
                && (long)Math.Max(_max.Row, cell.Row) - Math.Min(_min.Row, cell.Row) < _limit.Row
                && (long)Math.Max(_max.Layer, cell.Layer) - Math.Min(_min.Layer, cell.Layer) < _limit.Layer;
        }
    }
}
