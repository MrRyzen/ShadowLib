using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShadowLib.RNG.Sources;

namespace ShadowLib.Tests
{
    internal static class ShapeChecks
    {
        private static readonly (int Col, int Row, int Layer)[] FaceSteps =
        {
            (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
        };

        internal static void AssertDistinctAndConnected(ReadOnlySpan<(int Col, int Row, int Layer)> cells, int expectedCount, string label)
        {
            Assert.AreEqual(expectedCount, cells.Length, label);

            var all = new HashSet<(int, int, int)>();
            foreach (var cell in cells)
                Assert.IsTrue(all.Add(cell), $"{label}: {cell} repeats");

            var reached = new HashSet<(int, int, int)> { cells[0] };
            var open = new Stack<(int Col, int Row, int Layer)>();
            open.Push(cells[0]);
            while (open.Count > 0)
            {
                var cell = open.Pop();
                foreach (var step in FaceSteps)
                {
                    var next = (cell.Col + step.Col, cell.Row + step.Row, cell.Layer + step.Layer);
                    if (all.Contains(next) && reached.Add(next))
                        open.Push(next);
                }
            }

            Assert.AreEqual(expectedCount, reached.Count, $"{label}: not face-connected");
        }

        internal sealed class ConstantRandom : IRandom
        {
            private readonly float _value;
            public ConstantRandom(float value) => _value = value;
            public float NextFloat() => _value;

            public byte[] GetState() => throw new NotSupportedException();
            public void SetState(byte[] state) => throw new NotSupportedException();
            public void SetState(ReadOnlySpan<byte> state) => throw new NotSupportedException();
            public void Seed(ulong seed) => throw new NotSupportedException();
            public uint NextUInt() => throw new NotSupportedException();
            public int NextInt() => throw new NotSupportedException();
            public ulong NextULong() => throw new NotSupportedException();
            public double NextDouble() => throw new NotSupportedException();
            public int Range(int min, int max) => throw new NotSupportedException();
            public float Range(float min, float max) => throw new NotSupportedException();
            public double Range(double min, double max) => throw new NotSupportedException();
            public ulong Range(ulong min, ulong max) => throw new NotSupportedException();
        }
    }
}
