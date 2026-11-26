namespace ShadowLib.Spatial.Pathfinding
{
    using System;

    internal static class PathHeap
    {
        internal const int Closed = -1;

        internal static void Push(
            Span<int> heap, Span<int> positions, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar, ref int count, int cell)
        {
            SiftUp(heap, positions, estimates, costsSoFar, count++, cell);
        }

        internal static void Improve(
            Span<int> heap, Span<int> positions, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar, int cell)
        {
            SiftUp(heap, positions, estimates, costsSoFar, positions[cell], cell);
        }

        internal static int Pop(
            Span<int> heap, Span<int> positions, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar, ref int count)
        {
            int top = heap[0];
            positions[top] = Closed;
            count--;
            if (count > 0)
                SiftDown(heap, positions, estimates, costsSoFar, count, heap[count]);

            return top;
        }

        private static bool ComesBefore(int a, int b, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar)
        {
            if (estimates[a] != estimates[b])
                return estimates[a] < estimates[b];
            if (costsSoFar[a] != costsSoFar[b])
                return costsSoFar[a] > costsSoFar[b];

            return a < b;
        }

        private static void SiftUp(
            Span<int> heap, Span<int> positions, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar, int slot, int cell)
        {
            while (slot > 0)
            {
                int parentSlot = (slot - 1) / 2;
                int parent = heap[parentSlot];
                if (!ComesBefore(cell, parent, estimates, costsSoFar))
                    break;

                heap[slot] = parent;
                positions[parent] = slot;
                slot = parentSlot;
            }

            heap[slot] = cell;
            positions[cell] = slot;
        }

        private static void SiftDown(
            Span<int> heap, Span<int> positions, ReadOnlySpan<int> estimates, ReadOnlySpan<int> costsSoFar, int count, int cell)
        {
            int slot = 0;
            while (true)
            {
                int childSlot = slot * 2 + 1;
                if (childSlot >= count)
                    break;

                if (childSlot + 1 < count && ComesBefore(heap[childSlot + 1], heap[childSlot], estimates, costsSoFar))
                    childSlot++;

                int child = heap[childSlot];
                if (!ComesBefore(child, cell, estimates, costsSoFar))
                    break;

                heap[slot] = child;
                positions[child] = slot;
                slot = childSlot;
            }

            heap[slot] = cell;
            positions[cell] = slot;
        }
    }
}
