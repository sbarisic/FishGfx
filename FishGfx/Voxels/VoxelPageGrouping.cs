using System;
using System.Buffers;
using System.Collections.Generic;

namespace FishGfx.Voxels;

/// <summary>Stable linked ranges over borrowed input. The caller copies retained draw snapshots before disposal.</summary>
internal sealed class VoxelPageGrouping : IDisposable
{
    [ThreadStatic] private static Dictionary<VoxelGeometryPage, int> cachedLookup;
    private readonly Dictionary<VoxelGeometryPage, int> lookup;
    internal readonly VoxelGeometryPage[] Pages;
    internal readonly int[] Counts, First, Next;
    private readonly int[] last;
    internal int Count { get; private set; }

    internal VoxelPageGrouping(IReadOnlyList<VoxelPassEntry> entries)
    {
        lookup = cachedLookup ?? new(); cachedLookup = null;
        int capacity = Math.Max(1, entries.Count);
        Pages = ArrayPool<VoxelGeometryPage>.Shared.Rent(capacity);
        Counts = ArrayPool<int>.Shared.Rent(capacity);
        First = ArrayPool<int>.Shared.Rent(capacity);
        Next = ArrayPool<int>.Shared.Rent(capacity);
        last = ArrayPool<int>.Shared.Rent(capacity);
        for (int index = 0; index < entries.Count; index++)
        {
            var page = entries[index].Allocation.Page;
            if (!lookup.TryGetValue(page, out int group))
            {
                group = Count++; lookup.Add(page, group); Pages[group] = page;
                First[group] = index; Counts[group] = 0;
            }
            else Next[last[group]] = index;
            Next[index] = -1; last[group] = index; Counts[group]++;
        }
    }

    public void Dispose()
    {
        lookup.Clear(); cachedLookup ??= lookup;
        ArrayPool<VoxelGeometryPage>.Shared.Return(Pages, clearArray: true);
        ArrayPool<int>.Shared.Return(Counts); ArrayPool<int>.Shared.Return(First);
        ArrayPool<int>.Shared.Return(Next); ArrayPool<int>.Shared.Return(last);
    }
}
