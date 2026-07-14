namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private static void AddDirectColumns(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate
	)
	{
		HorizontalChunkCoordinate horizontal = new HorizontalChunkCoordinate(
			coordinate.X,
			coordinate.Z
		);
		if (!incremental.AddedDirectGroups.Add(horizontal))
		{
			return;
		}

		int originX = coordinate.X * VoxelWorld.ChunkSize;
		int originZ = coordinate.Z * VoxelWorld.ChunkSize;
		for (int z = 0; z < VoxelWorld.ChunkSize; z++)
		{
			for (int x = 0; x < VoxelWorld.ChunkSize; x++)
			{
				incremental.DirectColumnSet.Add(new WorldColumn(originX + x, originZ + z));
			}
		}
	}

	private static void SeedPublishedBoundaryLight(
		IncrementalTransaction incremental,
		IncrementalSourceChunk target
	)
	{
		ChunkCoordinate coordinate = target.Resident.Coordinate;
		SeedPublishedFace(
			incremental,
			target,
			coordinate + new ChunkCoordinate(-1, 0, 0),
			axis: 0,
			positive: false
		);
		SeedPublishedFace(
			incremental,
			target,
			coordinate + new ChunkCoordinate(1, 0, 0),
			axis: 0,
			positive: true
		);
		SeedPublishedFace(
			incremental,
			target,
			coordinate + new ChunkCoordinate(0, -1, 0),
			axis: 1,
			positive: false
		);
		SeedPublishedFace(
			incremental,
			target,
			coordinate + new ChunkCoordinate(0, 1, 0),
			axis: 1,
			positive: true
		);
		SeedPublishedFace(
			incremental,
			target,
			coordinate + new ChunkCoordinate(0, 0, -1),
			axis: 2,
			positive: false
		);
		SeedPublishedFace(
			incremental,
			target,
			coordinate + new ChunkCoordinate(0, 0, 1),
			axis: 2,
			positive: true
		);
	}

	private static void SeedPublishedFace(
		IncrementalTransaction incremental,
		IncrementalSourceChunk target,
		ChunkCoordinate sourceCoordinate,
		int axis,
		bool positive
	)
	{
		if (!incremental.Sources.TryGetValue(
			sourceCoordinate,
			out IncrementalSourceChunk source
		) || source.PublishedLights == null)
		{
			return;
		}

		for (int second = 0; second < VoxelWorld.ChunkSize; second++)
		{
			for (int first = 0; first < VoxelWorld.ChunkSize; first++)
			{
				int sourceIndex;
				int targetIndex;
				switch (axis)
				{
					case 0:
						sourceIndex = Index(
							positive ? 0 : VoxelWorld.ChunkSize - 1,
							first,
							second
						);
						targetIndex = Index(
							positive ? VoxelWorld.ChunkSize - 1 : 0,
							first,
							second
						);
						break;
					case 1:
						sourceIndex = Index(
							first,
							positive ? 0 : VoxelWorld.ChunkSize - 1,
							second
						);
						targetIndex = Index(
							first,
							positive ? VoxelWorld.ChunkSize - 1 : 0,
							second
						);
						break;
					default:
						sourceIndex = Index(
							first,
							second,
							positive ? 0 : VoxelWorld.ChunkSize - 1
						);
						targetIndex = Index(
							first,
							second,
							positive ? VoxelWorld.ChunkSize - 1 : 0
						);
						break;
				}

				if (source.PublishedLights[sourceIndex] != 0)
				{
					EnqueueIncremental(incremental, target, targetIndex);
				}
			}
		}
	}
}
