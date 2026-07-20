using System;
using System.Collections.Generic;
using System.Threading;

namespace FishGfx.Voxels;

/// <summary>
/// Single-use chunk storage prepared away from the graphics thread. The input
/// array is owned by this object immediately and is transferred to a world by
/// <see cref="VoxelWorld.SetPreparedChunk"/>.
/// </summary>
public sealed class PreparedVoxelChunk : IDisposable
{
	private const int MaximumStoredMaterialRuns = 1_024;
	private VoxelCell[] cells;
	private VoxelMaterialRun[] materialRuns;

	private PreparedVoxelChunk(
		VoxelCell[] cells,
		VoxelMaterialRun[] materialRuns,
		int nonAirCount)
	{
		this.cells = cells;
		this.materialRuns = materialRuns;
		NonAirCount = nonAirCount;
	}

	public int NonAirCount { get; }

	public static PreparedVoxelChunk TakeOwnership(VoxelCell[] cells)
	{
		ArgumentNullException.ThrowIfNull(cells);
		if (cells.Length != VoxelWorld.ChunkVolume)
		{
			throw new ArgumentException(
				$"Chunk data must contain exactly {VoxelWorld.ChunkVolume} voxels.",
				nameof(cells)
			);
		}

		int nonAirCount = 0;
		List<VoxelMaterialRun> runs = new();
		ushort runMaterialId = cells[0].MaterialId;
		int runLength = 0;
		bool retainRuns = true;
		for (int index = 0; index < cells.Length; index++)
		{
			ushort materialId = cells[index].MaterialId;
			if (!cells[index].IsAir)
			{
				nonAirCount++;
			}

			if (!retainRuns)
			{
				continue;
			}
			if (materialId == runMaterialId)
			{
				runLength++;
				continue;
			}

			retainRuns = TryAddRun(runs, runMaterialId, runLength);
			runMaterialId = materialId;
			runLength = 1;
		}

		if (retainRuns)
		{
			retainRuns = TryAddRun(runs, runMaterialId, runLength);
		}

		return new PreparedVoxelChunk(
			cells,
			retainRuns ? runs.ToArray() : null,
			nonAirCount
		);
	}

	internal (VoxelCell[] Cells, VoxelMaterialRun[] Runs) Consume()
	{
		VoxelCell[] consumedCells = Interlocked.Exchange(ref cells, null)
			?? throw new ObjectDisposedException(nameof(PreparedVoxelChunk));
		VoxelMaterialRun[] consumedRuns = Interlocked.Exchange(ref materialRuns, null);
		return (consumedCells, consumedRuns);
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref cells, null);
		Interlocked.Exchange(ref materialRuns, null);
	}

	private static bool TryAddRun(
		List<VoxelMaterialRun> runs,
		ushort materialId,
		int length)
	{
		if (runs.Count + 1 >= MaximumStoredMaterialRuns)
		{
			runs.Clear();
			return false;
		}

		runs.Add(new VoxelMaterialRun(materialId, checked((ushort)length)));
		return true;
	}
}
