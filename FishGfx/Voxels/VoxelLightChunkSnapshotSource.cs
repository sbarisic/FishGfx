using System;

namespace FishGfx.Voxels;

internal readonly struct VoxelLightSnapshotContent
{
	internal VoxelLightSnapshotContent(
		ReadOnlyMemory<ushort> lights,
		bool skyExposedAbove
	)
	{
		Lights = lights;
		SkyExposedAbove = skyExposedAbove;
	}

	internal ReadOnlyMemory<ushort> Lights { get; }
	internal bool HasLights => !Lights.IsEmpty;
	internal bool SkyExposedAbove { get; }
}

internal sealed class VoxelLightChunkSnapshotSource
{
	private const int NeighborhoodSize = 3;
	private readonly VoxelLightSnapshotContent[] contents;

	internal VoxelLightChunkSnapshotSource(
		ChunkCoordinate coordinate,
		long generation,
		long revision,
		VoxelLightSnapshotContent[] contents
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

	internal VoxelLightChunkSnapshot Materialize()
	{
		ushort[] padded = new ushort[
			VoxelLightChunkSnapshot.PaddedSize
				* VoxelLightChunkSnapshot.PaddedSize
				* VoxelLightChunkSnapshot.PaddedSize
		];

		return Materialize(padded);
	}

	internal VoxelLightChunkSnapshot Materialize(ushort[] padded)
	{
		ArgumentNullException.ThrowIfNull(padded);

		if (padded.Length < PaddedCellCount)
		{
			throw new ArgumentException("The padded light buffer is too small.", nameof(padded));
		}

		padded.AsSpan(0, PaddedCellCount).Clear();

		for (int z = -1; z <= VoxelWorld.ChunkSize; z++)
		{
			ResolveComponent(z, out int offsetZ, out int sampleZ);

			for (int y = -1; y <= VoxelWorld.ChunkSize; y++)
			{
				ResolveComponent(y, out int offsetY, out int sampleY);

				for (int x = -1; x <= VoxelWorld.ChunkSize; x++)
				{
					ResolveComponent(x, out int offsetX, out int sampleX);
					VoxelLightSnapshotContent content = contents[
						NeighborhoodIndex(offsetX, offsetY, offsetZ)
					];

					if (content.HasLights)
					{
						padded[PaddedIndex(x, y, z)] = content.Lights.Span[
							CellIndex(sampleX, sampleY, sampleZ)
						];
					}
					else if (y == VoxelWorld.ChunkSize)
					{
						VoxelLightSnapshotContent below = contents[
							NeighborhoodIndex(offsetX, 0, offsetZ)
						];

						if (below.HasLights && below.SkyExposedAbove)
						{
							padded[PaddedIndex(x, y, z)] = VoxelLight.Pack(0, 0, 0, 15);
						}
					}
				}
			}
		}

		return new VoxelLightChunkSnapshot(Coordinate, Generation, Revision, padded);
	}

	internal static int PaddedCellCount =>
		VoxelLightChunkSnapshot.PaddedSize
			* VoxelLightChunkSnapshot.PaddedSize
			* VoxelLightChunkSnapshot.PaddedSize;

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
			+ VoxelLightChunkSnapshot.PaddedSize
				* (y + 1 + VoxelLightChunkSnapshot.PaddedSize * (z + 1));
	}
}
