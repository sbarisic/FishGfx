using System;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelRendererOptimizationTests
{
	[Fact]
	public void IndirectBufferFlagIsAcceptedAndPreserved()
	{
		GraphicsBufferDescriptor descriptor = new(
			256,
			BufferBindFlags.Indirect,
			BufferUsage.Stream
		);

		Assert.Equal(BufferBindFlags.Indirect, descriptor.BindFlags);
		Assert.Equal(BufferUsage.Stream, descriptor.Usage);
	}

	[Fact]
	public void MeshingFocusSuppressesChunksOutsideTheInactiveMargin()
	{
		Camera camera = new()
		{
			Position = Vector3.Zero,
		};
		camera.SetPerspective(1280, 720, MathF.PI / 2, 0.1f, 512);
		VoxelMeshingFocus narrow = new(camera, 16, schedulingMargin: 0, cullingEnabled: true);
		VoxelMeshingFocus hysteretic = new(camera, 16, schedulingMargin: 32, cullingEnabled: true);
		ChunkCoordinate nearby = new(0, 0, 0);
		ChunkCoordinate marginOnly = new(1, 0, 0);

		Assert.True(narrow.ShouldSchedule(nearby));
		Assert.False(narrow.ShouldSchedule(marginOnly));
		Assert.True(hysteretic.ShouldSchedule(marginOnly));
	}

	[Theory]
	[InlineData(false, 4, 10, 0, 0, VoxelTransparentInvalidationReason.FirstFrame)]
	[InlineData(true, 5, 10, 0, 0, VoxelTransparentInvalidationReason.Geometry)]
	[InlineData(true, 4, 11, 0, 0, VoxelTransparentInvalidationReason.ActiveSet)]
	[InlineData(true, 4, 10, 0.25f, 0, VoxelTransparentInvalidationReason.Translation)]
	[InlineData(true, 4, 10, 0, 1.1f, VoxelTransparentInvalidationReason.Rotation)]
	public void TransparentCacheReportsDeterministicInvalidationReasons(
		bool hasCache,
		long geometry,
		long signature,
		float translation,
		float rotationDegrees,
		VoxelTransparentInvalidationReason expected
	)
	{
		VoxelTransparentCacheKey cached = new(4, 10, Vector3.Zero, -Vector3.UnitZ);
		float radians = rotationDegrees * MathF.PI / 180f;
		Vector3 forward = Vector3.Transform(
			-Vector3.UnitZ,
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians)
		);

		VoxelTransparentInvalidationReason actual = VoxelTransparentCachePolicy.Evaluate(
			hasCache,
			cached,
			geometry,
			signature,
			new Vector3(translation, 0, 0),
			forward,
			distanceThreshold: 0.25f,
			angleThresholdDegrees: 1f
		);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void ZeroTransparentThresholdInvalidatesOnAnyCameraChange()
	{
		VoxelTransparentCacheKey cached = new(4, 10, Vector3.Zero, -Vector3.UnitZ);
		Vector3 rotated = Vector3.Transform(
			-Vector3.UnitZ,
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.001f)
		);

		Assert.Equal(
			VoxelTransparentInvalidationReason.Translation,
			VoxelTransparentCachePolicy.Evaluate(
				true,
				cached,
				4,
				10,
				new Vector3(0.001f, 0, 0),
				-Vector3.UnitZ,
				0,
				0
			)
		);
		Assert.Equal(
			VoxelTransparentInvalidationReason.Rotation,
			VoxelTransparentCachePolicy.Evaluate(
				true,
				cached,
				4,
				10,
				Vector3.Zero,
				rotated,
				0,
				0
			)
		);
	}

	[Fact]
	public void TransparentCacheUsesCumulativeTranslationAndRotationThresholds()
	{
		VoxelTransparentCacheKey cached = new(4, 10, Vector3.Zero, -Vector3.UnitZ);
		Vector3 halfDegreeForward = Vector3.Transform(
			-Vector3.UnitZ,
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f * MathF.PI / 180f)
		);
		Vector3 overOneDegreeForward = Vector3.Transform(
			-Vector3.UnitZ,
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.1f * MathF.PI / 180f)
		);

		Assert.Equal(
			VoxelTransparentInvalidationReason.None,
			VoxelTransparentCachePolicy.Evaluate(
				true,
				cached,
				4,
				10,
				new Vector3(0.249f, 0, 0),
				halfDegreeForward,
				0.25f,
				1f
			)
		);
		Assert.Equal(
			VoxelTransparentInvalidationReason.Rotation,
			VoxelTransparentCachePolicy.Evaluate(
				true,
				cached,
				4,
				10,
				Vector3.Zero,
				overOneDegreeForward,
				0.25f,
				1f
			)
		);
	}
}
