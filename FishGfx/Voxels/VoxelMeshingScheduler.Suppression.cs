using System;

namespace FishGfx.Voxels;

public sealed partial class VoxelMeshingScheduler
{
	private static readonly ChunkCoordinate[] FaceNeighbors =
	{
		new ChunkCoordinate(1, 0, 0),
		new ChunkCoordinate(-1, 0, 0),
		new ChunkCoordinate(0, 1, 0),
		new ChunkCoordinate(0, -1, 0),
		new ChunkCoordinate(0, 0, 1),
		new ChunkCoordinate(0, 0, -1),
	};

	internal bool IsProvablyOccluded(ChunkCoordinate coordinate)
	{
		VoxelChunkSnapshotSource source = world.CaptureSnapshotSource(coordinate);
		return source != null && IsProvablyOccluded(source);
	}

	private bool IsProvablyOccluded(VoxelChunkSnapshotSource source)
	{
		if (!IsFilledWithOccludingCubes(source.Center))
		{
			return false;
		}

		for (int face = 0; face < FaceNeighbors.Length; face++)
		{
			ChunkCoordinate neighbor = FaceNeighbors[face];
			VoxelChunkContent content = source.GetNeighbor(
				neighbor.X,
				neighbor.Y,
				neighbor.Z
			);

			if (!IsOccludingBoundary(content, face))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsEmpty(VoxelChunkContent content)
	{
		if (content.MaterialRuns.Length == 1)
		{
			VoxelMaterialRun run = content.MaterialRuns.Span[0];

			if (run.MaterialId == 0 && run.Length == VoxelWorld.ChunkVolume)
			{
				return true;
			}
		}

		foreach (VoxelCell cell in content.Cells.Span)
		{
			if (!cell.IsAir)
			{
				return false;
			}
		}

		return true;
	}

	private bool IsFilledWithOccludingCubes(VoxelChunkContent content)
	{
		if (content.IsImplicitAir || !content.HasMaterialRuns)
		{
			return false;
		}

		int cellCount = 0;

		foreach (VoxelMaterialRun run in content.MaterialRuns.Span)
		{
			if (!IsOccludingCube(run.MaterialId))
			{
				return false;
			}

			cellCount += run.Length;
		}

		return cellCount == VoxelWorld.ChunkVolume;
	}

	private bool IsOccludingBoundary(VoxelChunkContent content, int face)
	{
		if (content.IsImplicitAir)
		{
			return false;
		}

		if (content.MaterialRuns.Length == 1)
		{
			VoxelMaterialRun run = content.MaterialRuns.Span[0];

			if (run.Length == VoxelWorld.ChunkVolume)
			{
				return IsOccluding(run.MaterialId);
			}
		}

		ReadOnlySpan<VoxelCell> cells = content.Cells.Span;

		for (int second = 0; second < VoxelWorld.ChunkSize; second++)
		{
			for (int first = 0; first < VoxelWorld.ChunkSize; first++)
			{
				int index = GetBoundaryIndex(face, first, second);

				if (!IsOccluding(cells[index].MaterialId))
				{
					return false;
				}
			}
		}

		return true;
	}

	private bool IsOccludingCube(ushort materialId)
	{
		return IsOccluding(materialId) && palette[materialId].Models == null;
	}

	private bool IsOccluding(ushort materialId)
	{
		return materialId != 0
			&& palette.Contains(materialId)
			&& palette[materialId].OccludesFaces;
	}

	private static int GetBoundaryIndex(int face, int first, int second)
	{
		int maximum = VoxelWorld.ChunkSize - 1;

		return face switch
		{
			0 => Index(0, first, second),
			1 => Index(maximum, first, second),
			2 => Index(first, 0, second),
			3 => Index(first, maximum, second),
			4 => Index(first, second, 0),
			5 => Index(first, second, maximum),
			_ => throw new ArgumentOutOfRangeException(nameof(face)),
		};
	}

	private static int Index(int x, int y, int z)
	{
		return x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);
	}
}
