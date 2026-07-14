using System;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private void InitializeNextMaterial(RebuildTransaction rebuild)
	{
		if (rebuild.InitializeChunkIndex >= rebuild.Chunks.Count)
		{
			rebuild.Phase = RebuildPhase.SeedDirectSky;
			return;
		}

		WorkingChunk chunk = rebuild.Chunks[rebuild.InitializeChunkIndex];
		if (!chunk.StorageInitialized)
		{
			chunk.InitializeStorage(CreateRebuildMaterialStorage(chunk));
		}

		if (!chunk.MaterialRuns.IsEmpty)
		{
			InitializeNextMaterialRun(rebuild, chunk, chunk.MaterialRuns.Span);
		}
		else
		{
			InitializeNextMaterialCell(rebuild, chunk);
		}

		if (rebuild.InitializeCellIndex == VoxelWorld.ChunkVolume)
		{
			AdvanceRebuildInitialization(rebuild);
		}
	}

	private ushort[] CreateRebuildMaterialStorage(WorkingChunk chunk)
	{
		if (chunk.MaterialRuns.Length == 1)
		{
			ushort signature = GetMaterialSignature(chunk.MaterialRuns.Span[0].MaterialId);
			return GetUniformMaterialSignatures(signature);
		}

		return new ushort[VoxelWorld.ChunkVolume];
	}

	private void InitializeNextMaterialRun(
		RebuildTransaction rebuild,
		WorkingChunk chunk,
		ReadOnlySpan<VoxelMaterialRun> runs
	)
	{
		VoxelMaterialRun run = runs[rebuild.InitializeRunIndex];
		ushort signature = GetMaterialSignature(run.MaterialId);
		if ((signature & 0xfff0) == 0)
		{
			if (runs.Length != 1)
			{
				Array.Fill(
					chunk.MaterialSignatures,
					signature,
					rebuild.InitializeCellIndex,
					run.Length
				);
			}

			rebuild.InitializeCellIndex += run.Length;
			rebuild.InitializeRunIndex++;
			return;
		}

		InitializeEmittingMaterialCell(
			rebuild,
			chunk,
			signature,
			run.Length
		);
	}

	private static void InitializeEmittingMaterialCell(
		RebuildTransaction rebuild,
		WorkingChunk chunk,
		ushort signature,
		int runLength
	)
	{
		int index = rebuild.InitializeCellIndex;
		chunk.MaterialSignatures[index] = signature;
		ushort light = VoxelLight.Pack(
			GetEmissionRed(signature),
			GetEmissionGreen(signature),
			GetEmissionBlue(signature),
			0
		);
		chunk.Lights[index] = light;
		Enqueue(rebuild, chunk, index);

		rebuild.InitializeCellIndex++;
		rebuild.InitializeRunCellIndex++;
		if (rebuild.InitializeRunCellIndex == runLength)
		{
			rebuild.InitializeRunIndex++;
			rebuild.InitializeRunCellIndex = 0;
		}
	}

	private void InitializeNextMaterialCell(
		RebuildTransaction rebuild,
		WorkingChunk chunk
	)
	{
		int index = rebuild.InitializeCellIndex;
		ushort signature = GetMaterialSignature(chunk.MaterialCells.Span[index].MaterialId);
		chunk.MaterialSignatures[index] = signature;
		ushort light = VoxelLight.Pack(
			GetEmissionRed(signature),
			GetEmissionGreen(signature),
			GetEmissionBlue(signature),
			0
		);
		chunk.Lights[index] = light;
		if (light != 0)
		{
			Enqueue(rebuild, chunk, index);
		}

		rebuild.InitializeCellIndex++;
	}

	private static void AdvanceRebuildInitialization(RebuildTransaction rebuild)
	{
		rebuild.InitializeCellIndex = 0;
		rebuild.InitializeRunIndex = 0;
		rebuild.InitializeRunCellIndex = 0;
		rebuild.InitializeChunkIndex++;
		if (rebuild.InitializeChunkIndex == rebuild.Chunks.Count)
		{
			rebuild.Phase = RebuildPhase.SeedDirectSky;
		}
	}
}
