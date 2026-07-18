using System;
using System.Numerics;

namespace FishGfx.Voxels;

public sealed class VoxelRendererOptions
{
	public const int DefaultGeometryPageSizeBytes = 64 * 1024 * 1024;

	public int WorkerCount { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);

	public int MeshUploadBudget { get; set; } = 4;

	public double MeshUploadTimeBudgetMilliseconds { get; set; } = double.PositiveInfinity;

	public float MaxRenderDistance { get; set; } = 256;

	public float AlphaCutoff { get; set; } = 0.5f;

	public int GeometryPageSizeBytes { get; set; } = DefaultGeometryPageSizeBytes;

	public float TransparentResortDistance { get; set; } = 0.25f;

	public float TransparentResortAngleDegrees { get; set; } = 1f;

	public float ActiveSetRefreshDistance { get; set; } = 8f;

	public float ActivationMargin { get; set; } = 16f;

	public float DeactivationMargin { get; set; } = 32f;

	public bool GpuProfilingEnabled { get; set; }

	public VoxelMeshingOptions Meshing { get; set; } = new VoxelMeshingOptions();

	public VoxelSunSettings Sun { get; set; } = new VoxelSunSettings(
		new Vector3(-0.45f, -1, -0.3f),
		Color.White,
		1,
		0.35f
	);
}
