using System;

namespace FishGfx.Voxels;

internal sealed class VoxelChunkSnapshotSource
{
	private const int NeighborhoodSize = 3;
	private readonly VoxelChunkContent[] contents;

	internal VoxelChunkSnapshotSource(
		ChunkCoordinate coordinate,
		long generation,
		long revision,
		VoxelChunkContent[] contents
	)
	{
		Coordinate = coordinate;
		Generation = generation;
		Revision = revision;
		this.contents = contents ?? throw new ArgumentNullException(nameof(contents));
	}

	internal ChunkCoordinate Coordinate { get; }
	internal long Generation { get; }
	internal long Revision { get; }
	internal VoxelChunkContent Center => contents[NeighborhoodIndex(0, 0, 0)];

	internal VoxelChunkContent GetNeighbor(int offsetX, int offsetY, int offsetZ)
	{
		return contents[NeighborhoodIndex(offsetX, offsetY, offsetZ)];
	}

	internal VoxelChunkSnapshot Materialize()
	{
		ushort[] padded = new ushort[
			VoxelChunkSnapshot.PaddedSize
				* VoxelChunkSnapshot.PaddedSize
				* VoxelChunkSnapshot.PaddedSize
		];

		return Materialize(padded);
	}

	internal VoxelChunkSnapshot Materialize(ushort[] padded)
	{
		ArgumentNullException.ThrowIfNull(padded);

		if (padded.Length < PaddedCellCount)
		{
			throw new ArgumentException("The padded material buffer is too small.", nameof(padded));
		}

		for (int z = -1; z <= VoxelWorld.ChunkSize; z++)
		{
			ResolveComponent(z, out int offsetZ, out int sampleZ);

			for (int y = -1; y <= VoxelWorld.ChunkSize; y++)
			{
				ResolveComponent(y, out int offsetY, out int sampleY);

				for (int x = -1; x <= VoxelWorld.ChunkSize; x++)
				{
					ResolveComponent(x, out int offsetX, out int sampleX);
					ReadOnlySpan<VoxelCell> cells = contents[
						NeighborhoodIndex(offsetX, offsetY, offsetZ)
					].Cells.Span;
					padded[PaddedIndex(x, y, z)] = cells[
						CellIndex(sampleX, sampleY, sampleZ)
					].MaterialId;
				}
			}
		}

		return new VoxelChunkSnapshot(Coordinate, Generation, Revision, padded);
	}

	internal static int PaddedCellCount =>
		VoxelChunkSnapshot.PaddedSize
			* VoxelChunkSnapshot.PaddedSize
			* VoxelChunkSnapshot.PaddedSize;

	private static void ResolveComponent(
		int value,
		out int offset,
		out int sample
	)
	{
		if (value < 0)
		{
			offset = -1;
			sample = VoxelWorld.ChunkSize - 1;
			return;
		}

		if (value >= VoxelWorld.ChunkSize)
		{
			offset = 1;
			sample = 0;
			return;
		}

		offset = 0;
		sample = value;
	}

	private static int NeighborhoodIndex(int x, int y, int z)
	{
		return x + 1 + NeighborhoodSize * (y + 1 + NeighborhoodSize * (z + 1));
	}

	private static int CellIndex(int x, int y, int z)
	{
		return x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);
	}

	private static int PaddedIndex(int x, int y, int z)
	{
		return x + 1
			+ VoxelChunkSnapshot.PaddedSize
				* (y + 1 + VoxelChunkSnapshot.PaddedSize * (z + 1));
	}
}
