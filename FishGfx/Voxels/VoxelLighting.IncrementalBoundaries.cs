using System;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private static void AdvanceCellCursor(ref int coordinateIndex, ref int cellIndex)
	{
		cellIndex++;

		if (cellIndex == VoxelWorld.ChunkVolume)
		{
			cellIndex = 0;
			coordinateIndex++;
		}
	}

	private void EnqueueRemovedBoundaryNeighbor(
		IncrementalTransaction incremental,
		ChunkCoordinate removedCoordinate,
		int boundaryIndex
	)
	{
		int face = boundaryIndex / (VoxelWorld.ChunkSize * VoxelWorld.ChunkSize);
		int cell = boundaryIndex % (VoxelWorld.ChunkSize * VoxelWorld.ChunkSize);
		int first = cell % VoxelWorld.ChunkSize;
		int second = cell / VoxelWorld.ChunkSize;

		switch (face)
		{
			case 0:
				EnqueueAt(
					incremental,
					removedCoordinate + new ChunkCoordinate(-1, 0, 0),
					VoxelWorld.ChunkSize - 1,
					first,
					second
				);
				break;
			case 1:
				EnqueueAt(
					incremental,
					removedCoordinate + new ChunkCoordinate(1, 0, 0),
					0,
					first,
					second
				);
				break;
			case 2:
				EnqueueAt(
					incremental,
					removedCoordinate + new ChunkCoordinate(0, -1, 0),
					first,
					VoxelWorld.ChunkSize - 1,
					second
				);
				break;
			case 3:
				EnqueueAt(
					incremental,
					removedCoordinate + new ChunkCoordinate(0, 1, 0),
					first,
					0,
					second
				);
				break;
			case 4:
				EnqueueAt(
					incremental,
					removedCoordinate + new ChunkCoordinate(0, 0, -1),
					first,
					second,
					VoxelWorld.ChunkSize - 1
				);
				break;
			case 5:
				EnqueueAt(
					incremental,
					removedCoordinate + new ChunkCoordinate(0, 0, 1),
					first,
					second,
					0
				);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(boundaryIndex));
		}
	}
}
