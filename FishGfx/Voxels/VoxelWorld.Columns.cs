using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelWorld
{
	public VoxelColumnUpdate BeginColumnUpdate(int chunkX, int chunkZ, long revision)
	{
		lock (sync)
		{
			if (!activeColumnUpdates.Add((chunkX, chunkZ)))
				throw new InvalidOperationException($"Column ({chunkX}, {chunkZ}) already has an active update.");
		}
		return new VoxelColumnUpdate(this, chunkX, chunkZ, revision);
	}

	public void InstallPreparedChunk(
		VoxelColumnUpdate update,
		ChunkCoordinate coordinate,
		PreparedVoxelChunk prepared)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentNullException.ThrowIfNull(prepared);
		if (!ReferenceEquals(update.World, this))
			throw new ArgumentException("The column update belongs to another world.", nameof(update));
		update.Add(coordinate, prepared);
	}

	public void CompleteColumnUpdate(VoxelColumnUpdate update)
	{
		ArgumentNullException.ThrowIfNull(update);
		if (!ReferenceEquals(update.World, this))
			throw new ArgumentException("The column update belongs to another world.", nameof(update));

		List<(ChunkCoordinate Coordinate, VoxelChunk Chunk)> replacement = new();
		HashSet<ChunkCoordinate> replacementCoordinates = new();
		foreach ((ChunkCoordinate coordinate, PreparedVoxelChunk prepared) in update.Chunks)
		{
			if (coordinate.X != update.ChunkX || coordinate.Z != update.ChunkZ)
				throw new InvalidOperationException("A prepared chunk escaped its column update.");
			if (!replacementCoordinates.Add(coordinate))
				throw new InvalidOperationException($"Column ({update.ChunkX}, {update.ChunkZ}) contains duplicate chunk {coordinate}.");

			(VoxelCell[] cells, VoxelMaterialRun[] runs) = prepared.Consume();
			VoxelChunk chunk;
			lock (sync)
				chunk = CreateChunk(coordinate);
			chunk.AdoptPreparedUnchecked(cells, runs, prepared.NonAirCount);
			replacement.Add((coordinate, chunk));
		}

		List<ChunkCoordinate> removed = new();
		List<ChunkCoordinate> changed = new();
		List<(ChunkCoordinate Coordinate, long Revision)> invalidated = new();
		lock (sync)
		{
			if (update.IsComplete || !activeColumnUpdates.Contains((update.ChunkX, update.ChunkZ)))
				throw new InvalidOperationException("The column update is no longer active.");

			foreach (ChunkCoordinate coordinate in chunks.Keys)
			{
				if (coordinate.X == update.ChunkX && coordinate.Z == update.ChunkZ)
					changed.Add(coordinate);
			}
			foreach (ChunkCoordinate coordinate in replacementCoordinates)
			{
				if (!changed.Contains(coordinate))
					changed.Add(coordinate);
			}

			foreach (ChunkCoordinate coordinate in changed)
			{
				if (chunks.Remove(coordinate))
					removed.Add(coordinate);
			}
			foreach ((ChunkCoordinate coordinate, VoxelChunk chunk) in replacement)
			{
				chunks.Add(coordinate, chunk);
				removed.Remove(coordinate);
			}

			HashSet<ChunkCoordinate> invalidateCoordinates = new();
			foreach (ChunkCoordinate coordinate in changed)
			{
				for (int z = -1; z <= 1; z++)
					for (int y = -1; y <= 1; y++)
						for (int x = -1; x <= 1; x++)
						{
							ChunkCoordinate neighbor = coordinate + new ChunkCoordinate(x, y, z);
							if (chunks.ContainsKey(neighbor))
								invalidateCoordinates.Add(neighbor);
						}
			}
			foreach (ChunkCoordinate coordinate in invalidateCoordinates)
			{
				VoxelChunk chunk = chunks[coordinate];
				chunk.Revision++;
				invalidated.Add((coordinate, chunk.Revision));
			}
			activeColumnUpdates.Remove((update.ChunkX, update.ChunkZ));
			update.MarkComplete();
		}

		RaiseInvalidated(invalidated);
		foreach (ChunkCoordinate coordinate in removed)
			ChunkRemoved?.Invoke(coordinate);
		foreach (ChunkCoordinate coordinate in changed)
			ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
	}

	internal void CancelColumnUpdate(VoxelColumnUpdate update)
	{
		lock (sync)
			activeColumnUpdates.Remove((update.ChunkX, update.ChunkZ));
	}
}
