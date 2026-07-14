using System;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private IncrementalStepResult PrepareNextAddedMaterial(
		IncrementalTransaction incremental,
		bool canConsumeWork
	)
	{
		if (incremental.AddedCoordinateIndex >= incremental.AddedCoordinates.Count)
		{
			incremental.Phase = IncrementalPhase.PrepareSky;
			return IncrementalStepResult.Completed;
		}

		ChunkCoordinate coordinate =
			incremental.AddedCoordinates[incremental.AddedCoordinateIndex];
		if (!TryGetCurrentAddedSource(incremental, coordinate, out IncrementalSourceChunk source))
		{
			SkipAddedCoordinate(incremental, coordinate);
			return IncrementalStepResult.Completed;
		}

		if (!incremental.AddedMaterialContents.TryGetValue(
			coordinate,
			out VoxelChunkContent content
		))
		{
			content = world.CaptureChunkContent(coordinate);
			incremental.AddedMaterialContents.Add(coordinate, content);
		}

		if (!incremental.Chunks.TryGetValue(coordinate, out IncrementalWorkingChunk working))
		{
			working = CreateAddedWorkingChunk(source, content);
			incremental.Chunks.Add(coordinate, working);
			source.Working = working;
			AddDirectColumns(incremental, coordinate);
		}

		if (!canConsumeWork)
		{
			return IncrementalStepResult.NeedsBudget;
		}

		if (content.HasMaterialRuns)
		{
			PrepareNextAddedRun(incremental, source, working, content.MaterialRuns.Span);
		}
		else
		{
			PrepareNextAddedCell(incremental, source, working, content.Cells.Span);
		}
		incremental.RemainingAddedPreparationWork--;
		incremental.AddedCoordinateConsumedWork++;

		if (incremental.AddedCellIndex == VoxelWorld.ChunkVolume)
		{
			SeedPublishedBoundaryLight(incremental, source);
			AdvanceAddedCoordinate(incremental);
		}

		return IncrementalStepResult.Consumed;
	}

	private bool TryGetCurrentAddedSource(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate,
		out IncrementalSourceChunk source
	)
	{
		if (incremental.Sources.TryGetValue(coordinate, out source)
			&& source.PublishedLights == null
			&& residents.TryGetValue(coordinate, out ResidentChunk resident)
			&& ReferenceEquals(resident, source.Resident))
		{
			return true;
		}

		source = null;
		return false;
	}

	private IncrementalWorkingChunk CreateAddedWorkingChunk(
		IncrementalSourceChunk source,
		VoxelChunkContent content
	)
	{
		ushort[] signatures;
		if (content.MaterialRuns.Length == 1)
		{
			ushort signature = GetMaterialSignature(content.MaterialRuns.Span[0].MaterialId);
			signatures = GetUniformMaterialSignatures(signature);
		}
		else
		{
			signatures = new ushort[VoxelWorld.ChunkVolume];
		}

		return new IncrementalWorkingChunk(
			source.Resident,
			source.DesiredSkyExposedAbove,
			signatures,
			new ushort[VoxelWorld.ChunkVolume],
			new byte[VoxelWorld.ChunkVolume],
			isNew: true
		);
	}

	private int GetMaterialPreparationWork(VoxelChunkContent content)
	{
		if (!content.HasMaterialRuns)
		{
			return VoxelWorld.ChunkVolume;
		}

		int work = 0;
		foreach (VoxelMaterialRun run in content.MaterialRuns.Span)
		{
			ushort signature = GetMaterialSignature(run.MaterialId);
			work += (signature & 0xfff0) == 0 ? 1 : run.Length;
		}

		return work;
	}

	private void PrepareNextAddedRun(
		IncrementalTransaction incremental,
		IncrementalSourceChunk source,
		IncrementalWorkingChunk working,
		ReadOnlySpan<VoxelMaterialRun> runs
	)
	{
		VoxelMaterialRun run = runs[incremental.AddedRunIndex];
		ushort signature = GetMaterialSignature(run.MaterialId);
		if ((signature & 0xfff0) == 0)
		{
			if (runs.Length != 1)
			{
				Array.Fill(
					working.MaterialSignatures,
					signature,
					incremental.AddedCellIndex,
					run.Length
				);
			}

			incremental.AddedCellIndex += run.Length;
			incremental.AddedRunIndex++;
			incremental.AddedRunCellIndex = 0;
			return;
		}

		int index = incremental.AddedCellIndex;
		working.MaterialSignatures[index] = signature;
		EnqueueIncremental(incremental, source, index);
		incremental.AddedCellIndex++;
		incremental.AddedRunCellIndex++;
		if (incremental.AddedRunCellIndex == run.Length)
		{
			incremental.AddedRunIndex++;
			incremental.AddedRunCellIndex = 0;
		}
	}

	private void PrepareNextAddedCell(
		IncrementalTransaction incremental,
		IncrementalSourceChunk source,
		IncrementalWorkingChunk working,
		ReadOnlySpan<VoxelCell> cells
	)
	{
		int index = incremental.AddedCellIndex;
		ushort signature = GetMaterialSignature(cells[index].MaterialId);
		working.MaterialSignatures[index] = signature;
		if ((signature & 0xfff0) != 0)
		{
			EnqueueIncremental(incremental, source, index);
		}

		incremental.AddedCellIndex++;
	}

	private static void AdvanceAddedCoordinate(IncrementalTransaction incremental)
	{
		incremental.AddedCoordinateIndex++;
		incremental.AddedCellIndex = 0;
		incremental.AddedRunIndex = 0;
		incremental.AddedRunCellIndex = 0;
		incremental.AddedCoordinateConsumedWork = 0;
	}

	private static void SkipAddedCoordinate(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate
	)
	{
		if (incremental.AddedPreparationWork.TryGetValue(coordinate, out int work))
		{
			incremental.RemainingAddedPreparationWork -=
				work - incremental.AddedCoordinateConsumedWork;
		}

		AdvanceAddedCoordinate(incremental);
	}
}
