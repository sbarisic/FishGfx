using System.Collections;
using System.Collections.Generic;

namespace FishGfx.Voxels;

/// <summary>Owned by the scheduler lock. Empty columns are removed immediately.</summary>
internal sealed class DirtyChunkIndex : IEnumerable<ChunkCoordinate>
{
    private readonly Dictionary<(int X, int Z), HashSet<ChunkCoordinate>> columns = new();
    internal int Count { get; private set; }
    internal bool Add(ChunkCoordinate coordinate)
    {
        var key = (coordinate.X, coordinate.Z);
        if (!columns.TryGetValue(key, out var column)) columns.Add(key, column = new());
        if (!column.Add(coordinate)) return false;
        Count++; return true;
    }
    internal bool Remove(ChunkCoordinate coordinate)
    {
        var key = (coordinate.X, coordinate.Z);
        if (!columns.TryGetValue(key, out var column) || !column.Remove(coordinate)) return false;
        Count--;
        if (column.Count == 0) columns.Remove(key);
        return true;
    }
    internal IEnumerable<ChunkCoordinate> Candidates(VoxelMeshingFocus? focus)
    {
        if (focus.HasValue && focus.Value.TryGetColumnBounds(out int minX, out int maxX, out int minZ, out int maxZ)
            && ((double)maxX - minX + 1) * ((double)maxZ - minZ + 1) < columns.Count)
        {
            for (long x = minX; x <= maxX; x++) for (long z = minZ; z <= maxZ; z++)
                if (focus.Value.ShouldScheduleColumn((int)x, (int)z) && columns.TryGetValue(((int)x, (int)z), out var column))
                    foreach (var coordinate in column) yield return coordinate;
            yield break;
        }
        foreach (var pair in columns)
        {
            if (focus.HasValue && !focus.Value.ShouldScheduleColumn(pair.Key.X, pair.Key.Z)) continue;
            foreach (var coordinate in pair.Value) yield return coordinate;
        }
    }
    public IEnumerator<ChunkCoordinate> GetEnumerator() => Candidates(null).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
