using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

internal sealed class VoxelLightChunkSnapshot
{
	internal const int PaddedSize = VoxelWorld.ChunkSize + 2;
	private readonly ushort[] paddedLights;

	internal VoxelLightChunkSnapshot(
		ChunkCoordinate coordinate,
		long generation,
		long revision,
		ushort[] paddedLights
	)
	{
		Coordinate = coordinate;
		Generation = generation;
		Revision = revision;
		this.paddedLights = paddedLights;
	}

	internal VoxelLightChunkSnapshot(
		ChunkCoordinate coordinate,
		long revision,
		ushort[] paddedLights
	)
		: this(coordinate, 0, revision, paddedLights)
	{
	}

	internal ChunkCoordinate Coordinate { get; }
	internal long Generation { get; }
	internal long Revision { get; }

	internal VoxelLight GetLight(int localX, int localY, int localZ)
	{
		if (localX < -1 || localX > VoxelWorld.ChunkSize)
		{
			throw new ArgumentOutOfRangeException(nameof(localX));
		}

		if (localY < -1 || localY > VoxelWorld.ChunkSize)
		{
			throw new ArgumentOutOfRangeException(nameof(localY));
		}

		if (localZ < -1 || localZ > VoxelWorld.ChunkSize)
		{
			throw new ArgumentOutOfRangeException(nameof(localZ));
		}

		return GetLightUnchecked(localX, localY, localZ);
	}

	internal VoxelLight GetLightUnchecked(int localX, int localY, int localZ)
	{
		return new VoxelLight(paddedLights[
			(localX + 1) + PaddedSize * ((localY + 1) + PaddedSize * (localZ + 1))
		]);
	}
}
