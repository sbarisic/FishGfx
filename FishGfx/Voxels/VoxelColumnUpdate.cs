using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed class VoxelColumnUpdate : IDisposable
{
	private readonly Dictionary<ChunkCoordinate, PreparedVoxelChunk> chunks = new();
	private bool completed;

	internal VoxelColumnUpdate(VoxelWorld world, int chunkX, int chunkZ, long revision)
	{
		World = world;
		ChunkX = chunkX;
		ChunkZ = chunkZ;
		Revision = revision;
	}

	internal VoxelWorld World { get; }
	internal IReadOnlyDictionary<ChunkCoordinate, PreparedVoxelChunk> Chunks => chunks;
	public int ChunkX { get; }
	public int ChunkZ { get; }
	public long Revision { get; }
	public bool IsComplete => completed;

	internal void Add(ChunkCoordinate coordinate, PreparedVoxelChunk prepared)
	{
		ObjectDisposedException.ThrowIf(completed, this);
		if (coordinate.X != ChunkX || coordinate.Z != ChunkZ)
			throw new ArgumentException("A prepared chunk must belong to the update column.", nameof(coordinate));
		if (chunks.Remove(coordinate, out PreparedVoxelChunk previous))
			previous.Dispose();
		chunks.Add(coordinate, prepared);
	}

	internal void MarkComplete()
	{
		completed = true;
		chunks.Clear();
	}

	public void Dispose()
	{
		if (completed)
			return;
		completed = true;
		foreach (PreparedVoxelChunk chunk in chunks.Values)
			chunk.Dispose();
		chunks.Clear();
		World.CancelColumnUpdate(this);
	}
}
