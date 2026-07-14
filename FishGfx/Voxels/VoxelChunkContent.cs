using System;

namespace FishGfx.Voxels;

internal readonly struct VoxelMaterialRun
{
	internal VoxelMaterialRun(ushort materialId, ushort length)
	{
		MaterialId = materialId;
		Length = length;
	}

	internal ushort MaterialId { get; }
	internal ushort Length { get; }
}

internal readonly struct VoxelChunkContent
{
	internal VoxelChunkContent(
		ReadOnlyMemory<VoxelCell> cells,
		ReadOnlyMemory<VoxelMaterialRun> materialRuns,
		bool isImplicitAir
	)
	{
		Cells = cells;
		MaterialRuns = materialRuns;
		IsImplicitAir = isImplicitAir;
	}

	internal ReadOnlyMemory<VoxelCell> Cells { get; }
	internal ReadOnlyMemory<VoxelMaterialRun> MaterialRuns { get; }
	internal bool HasMaterialRuns => !MaterialRuns.IsEmpty;
	internal bool IsImplicitAir { get; }
}
