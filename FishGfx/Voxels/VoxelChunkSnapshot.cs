using System;

namespace FishGfx.Voxels;

public sealed class VoxelChunkSnapshot
{
	internal const int PaddedSize = VoxelWorld.ChunkSize + 2;
	private readonly ushort[] paddedMaterials;

	internal VoxelChunkSnapshot(
		ChunkCoordinate coordinate,
		long generation,
		long revision,
		ushort[] paddedMaterials
	)
	{
		Coordinate = coordinate;
		Generation = generation;
		Revision = revision;
		this.paddedMaterials = paddedMaterials;
	}

	public ChunkCoordinate Coordinate { get; }
	internal long Generation { get; }
	public long Revision { get; }

	public ushort GetMaterial(int localX, int localY, int localZ)
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

		return GetMaterialUnchecked(localX, localY, localZ);
	}

	internal ushort GetMaterialUnchecked(int localX, int localY, int localZ)
	{
		return paddedMaterials[
			(localX + 1) + PaddedSize * ((localY + 1) + PaddedSize * (localZ + 1))
		];
	}
}
