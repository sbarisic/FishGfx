using System;
using System.Numerics;

namespace FishGfx.Voxels;

public sealed class VoxelRendererOptions
{
	public int WorkerCount { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);

	public int MeshUploadBudget { get; set; } = 4;

	public double MeshUploadTimeBudgetMilliseconds { get; set; } = double.PositiveInfinity;

	public float MaxRenderDistance { get; set; } = 256;

	public float AlphaCutoff { get; set; } = 0.5f;

	public VoxelMeshingOptions Meshing { get; set; } = new VoxelMeshingOptions();

	public VoxelSunSettings Sun { get; set; } = new VoxelSunSettings(
		new Vector3(-0.45f, -1, -0.3f),
		Color.White,
		1,
		0.35f
	);
}
