using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelRendererOptimizationTests
{
	[Fact]
	public void TransparentRangeCapacityCanBePreflightedWithoutAllocating()
	{
		VoxelVertexRangeAllocator ranges = new(capacity: 12, alignment: 6);

		Assert.True(ranges.CanAllocate(12));
		Assert.Equal(0, ranges.AllocationCount);
		Assert.True(ranges.TryAllocate(12, out int first));
		Assert.Equal(0, first);
		Assert.False(ranges.CanAllocate(6));
		ranges.Free(first, 12);
		Assert.True(ranges.CanAllocate(6));
	}

	[Fact]
	public void MeshBackpressureUsesHighAndLowWatermarks()
	{
		VoxelRendererOptions options = new()
		{
			MaximumReadyMeshJobs = 128,
			MaximumReadyMeshBytes = 32 * 1024 * 1024,
			ResumeReadyMeshJobs = 64,
			ResumeReadyMeshBytes = 16 * 1024 * 1024,
		};

		Assert.True(VoxelRenderer.EvaluateMeshBackpressure(
			false, 128, 0, options));
		Assert.True(VoxelRenderer.EvaluateMeshBackpressure(
			false, 0, 32 * 1024 * 1024, options));
		Assert.True(VoxelRenderer.EvaluateMeshBackpressure(
			true, 65, 16 * 1024 * 1024, options));
		Assert.True(VoxelRenderer.EvaluateMeshBackpressure(
			true, 64, 16 * 1024 * 1024 + 1, options));
		Assert.False(VoxelRenderer.EvaluateMeshBackpressure(
			true, 64, 16 * 1024 * 1024, options));
	}

	[Fact]
	public void ColumnUpdatePublishesAtomicallyAndDeduplicatesInvalidations()
	{
		VoxelWorld world = new();
		ChunkCoordinate lower = new(2, 0, -3);
		ChunkCoordinate upper = new(2, 1, -3);
		world.SetVoxel(32, 1, -48, new VoxelCell(1));
		world.SetVoxel(32, 17, -48, new VoxelCell(1));
		HashSet<ChunkCoordinate> invalidated = new();
		int invalidationEvents = 0;
		world.ChunkInvalidated += (coordinate, _) =>
		{
			invalidated.Add(coordinate);
			invalidationEvents++;
		};

		using VoxelColumnUpdate update = world.BeginColumnUpdate(2, -3, revision: 7);
		VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
		replacement[0] = new VoxelCell(2);
		world.InstallPreparedChunk(
			update,
			lower,
			PreparedVoxelChunk.TakeOwnership(replacement));

		Assert.Equal((ushort)1, world.GetVoxel(32, 1, -48).MaterialId);
		Assert.True(world.TryGetChunk(upper, out _));
		Assert.Empty(invalidated);

		world.CompleteColumnUpdate(update);

		Assert.Equal((ushort)2, world.GetVoxel(32, 0, -48).MaterialId);
		Assert.False(world.TryGetChunk(upper, out _));
		Assert.Equal(invalidated.Count, invalidationEvents);
	}

	[Fact]
	public void DisposedColumnUpdateLeavesTheWorldUnchanged()
	{
		VoxelWorld world = new();
		world.SetVoxel(0, 0, 0, new VoxelCell(1));
		VoxelColumnUpdate update = world.BeginColumnUpdate(0, 0, revision: 2);
		VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
		replacement[0] = new VoxelCell(2);
		world.InstallPreparedChunk(
			update,
			default,
			PreparedVoxelChunk.TakeOwnership(replacement));

		update.Dispose();

		Assert.Equal((ushort)1, world.GetVoxel(0, 0, 0).MaterialId);
		using VoxelColumnUpdate retry = world.BeginColumnUpdate(0, 0, revision: 3);
	}

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
	public void PreparedVoxelChunkTransfersCompleteStorageOnce()
	{
		VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
		cells[17] = new VoxelCell(3);
		PreparedVoxelChunk prepared = PreparedVoxelChunk.TakeOwnership(cells);
		VoxelWorld world = new();
		ChunkCoordinate coordinate = new(-2, 4, 3);

		Assert.True(world.SetPreparedChunk(coordinate, prepared));

		Assert.Equal(new VoxelCell(3), world.GetVoxel(-31, 65, 48));
		Assert.Throws<ObjectDisposedException>(() => world.SetPreparedChunk(coordinate, prepared));
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
