using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTests
{
	[Fact]
	public void MeshingSchedulerProducesRevisionedResultsAndReschedulesEdits()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		using VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);
		ChunkCoordinate coordinate = new(0, 0, 0);

		Assert.Equal(1, scheduler.SchedulePending());
		world.SetVoxel(2, 1, 1, new VoxelCell(opaque));
		VoxelMeshData stale = WaitForResult(scheduler);

		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk chunk));
		Assert.True(stale.Revision < chunk.Revision);
		SpinWait.SpinUntil(() => scheduler.InFlightCount == 0, 5000);
		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData current = WaitForResult(scheduler);

		Assert.Equal(chunk.Revision, current.Revision);
		Assert.Equal(60, current.OpaqueVertices.Length);
	}

	[Fact]
	public void SchedulerHonorsWorkerLimitAndRejectsUseAfterDisposal()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		world.SetVoxel(17, 1, 1, new VoxelCell(opaque));
		VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);

		Assert.Equal(1, scheduler.SchedulePending());
		Assert.InRange(scheduler.InFlightCount, 0, 1);
		scheduler.Dispose();
		Assert.Throws<ObjectDisposedException>(() => scheduler.SchedulePending());
	}

	[Fact]
	public void SchedulerReportsWorkerMeshingFailures()
	{
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(99));
		using VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			new VoxelPaletteBuilder().Build(),
			new VoxelAtlasLayout(1, 1, 16, 16),
			maxWorkers: 1
		);
		scheduler.SchedulePending();
		Exception failure = null;

		bool completed = SpinWait.SpinUntil(() => scheduler.TryDequeueFailure(out failure), 5000);

		Assert.True(completed);
		Assert.Contains("(0, 0, 0)", failure.Message);
		Assert.IsType<InvalidOperationException>(failure.InnerException);
	}

	[Fact]
	public void SchedulerExaminesOnlyTheBoundedPriorityWindow()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();

		for (int x = 0; x < 100; x++)
		{
			world.SetVoxel(x * VoxelWorld.ChunkSize, 1, 1, new VoxelCell(opaque));
		}

		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);

		Assert.Equal(1, scheduler.SchedulePending());
		Assert.InRange(scheduler.LastSelectionCount, 1, 64);
		Assert.Equal(1, scheduler.LastCaptureCount);
	}

	[Fact]
	public void SchedulerDropsDirtyCoordinatesAfterWorldRemoval()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		ChunkCoordinate coordinate = new(0, 0, 0);
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);

		Assert.True(world.RemoveChunk(coordinate));
		Assert.Equal(0, scheduler.SchedulePending());
		Assert.Equal(0, scheduler.PendingCount);
	}

	[Fact]
	public void SchedulerIgnoresUnlitChunksAndWakesAfterLightPublication()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		ChunkCoordinate coordinate = new(0, 0, 0);
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		using VoxelLighting lighting = new(world, palette);
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1,
			lighting: lighting
		);

		Assert.Equal(0, scheduler.SchedulePending());
		Assert.Equal(0, scheduler.PendingCount);

		lighting.LoadChunk(coordinate, skyExposedAbove: true);
		scheduler.MarkDirty(coordinate);
		Assert.Equal(0, scheduler.SchedulePending());
		Assert.True(scheduler.PendingCount > 0);

		while (!lighting.IsIdle)
		{
			lighting.Update();
		}

		Assert.Equal(1, scheduler.SchedulePending());
		Assert.Equal(coordinate, WaitForResult(scheduler).Coordinate);
	}

	[Fact]
	public void RendererOptionsKeepUnlimitedUploadTimeByDefault()
	{
		VoxelRendererOptions options = new();

		Assert.True(double.IsPositiveInfinity(options.MeshUploadTimeBudgetMilliseconds));
	}

	[Fact]
	public void SchedulerCompletesEmptyChunksWithoutMaterializingGeometry()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		world.SetVoxel(1, 1, 1, VoxelCell.Air);
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);

		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData result = WaitForResult(scheduler);
		Assert.Empty(result.OpaqueVertices);
		Assert.Empty(result.CutoutVertices);
		Assert.Empty(result.TransparentFaces);
		Assert.True(result.Bounds.IsEmpty);
	}

	[Fact]
	public void SchedulerPrioritizesAVisibleChunkBeforeAnEquallyDistantChunkBehindTheCamera()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		ChunkCoordinate visible = new(0, 0, -2);
		ChunkCoordinate behind = new(0, 0, 1);
		world.SetVoxel(0, 0, -17, new VoxelCell(opaque));
		world.SetVoxel(0, 0, 17, new VoxelCell(opaque));
		Camera camera = new();
		camera.Position = new Vector3(8, 8, 0);
		camera.SetPerspective(1920, 1080, MathF.PI / 2, 0.1f, 200);
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);
		VoxelMeshingFocus focus = new(camera, 100, cullingEnabled: true);

		Assert.Equal(1, scheduler.SchedulePending(focus));
		VoxelMeshData result = WaitForResult(scheduler);

		Assert.Equal(visible, result.Coordinate);
		Assert.NotEqual(behind, result.Coordinate);
	}

	[Fact]
	public void SchedulerSuppressesOnlyACompletelyOccludedCubeChunk()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		ChunkCoordinate center = default;
		ChunkCoordinate[] neighbors =
		{
			new ChunkCoordinate(1, 0, 0),
			new ChunkCoordinate(-1, 0, 0),
			new ChunkCoordinate(0, 1, 0),
			new ChunkCoordinate(0, -1, 0),
			new ChunkCoordinate(0, 0, 1),
			new ChunkCoordinate(0, 0, -1),
		};
		world.FillChunk(center, new VoxelCell(opaque));

		foreach (ChunkCoordinate neighbor in neighbors)
		{
			world.FillChunk(neighbor, new VoxelCell(opaque));
		}

		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);

		Assert.True(scheduler.IsProvablyOccluded(center));
		world.SetVoxel(VoxelWorld.ChunkSize, 1, 1, VoxelCell.Air);
		Assert.False(scheduler.IsProvablyOccluded(center));
		world.FillChunk(neighbors[0], new VoxelCell(opaque));
		Assert.True(scheduler.IsProvablyOccluded(center));
		world.RemoveChunk(neighbors[0]);
		Assert.False(scheduler.IsProvablyOccluded(center));
	}

	[Fact]
	public void SchedulerNeverSuppressesCustomModelChunks()
	{
		VoxelModel model = new VoxelModel(
			new[]
			{
				new VoxelVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector3.UnitY),
				new VoxelVertex(Vector3.UnitX, Color.White, Vector2.UnitX, Vector3.UnitY),
				new VoxelVertex(Vector3.UnitZ, Color.White, Vector2.UnitY, Vector3.UnitY),
			}
		);
		VoxelPaletteBuilder builder = new VoxelPaletteBuilder();
		ushort solid = builder.Add(
			new VoxelMaterial("Solid", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))
		);
		ushort modeled = builder.Add(
			new VoxelMaterial(
				"Modeled",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(0),
				models: new VoxelModelSet(model)
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new VoxelWorld();
		ChunkCoordinate center = default;
		world.FillChunk(center, new VoxelCell(modeled));
		world.FillChunk(new ChunkCoordinate(1, 0, 0), new VoxelCell(solid));
		world.FillChunk(new ChunkCoordinate(-1, 0, 0), new VoxelCell(solid));
		world.FillChunk(new ChunkCoordinate(0, 1, 0), new VoxelCell(solid));
		world.FillChunk(new ChunkCoordinate(0, -1, 0), new VoxelCell(solid));
		world.FillChunk(new ChunkCoordinate(0, 0, 1), new VoxelCell(solid));
		world.FillChunk(new ChunkCoordinate(0, 0, -1), new VoxelCell(solid));
		using VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			palette,
			new VoxelAtlasLayout(1, 1, 16, 16),
			maxWorkers: 1
		);

		Assert.False(scheduler.IsProvablyOccluded(center));
	}

	[Fact]
	public void TransparentStreamSortsBackToFrontAndAppliesChunkOrigins()
	{
		VoxelVertex[] nearVertices =
		{
			new VoxelVertex(new Vector3(1, 0, 0), Color.Red, Vector2.Zero, Vector3.UnitZ)
			{
				WaveParameters = new Vector4(0.1f, 2, 3, 1),
				PackedLightChannels = new Color(17, 34, 51, 68),
			},
		};
		VoxelVertex[] farVertices =
		{
			new VoxelVertex(new Vector3(2, 0, 0), Color.Blue, Vector2.Zero, Vector3.UnitZ),
		};
		VoxelTransparentFace near = new VoxelTransparentFace(new Vector3(0, 0, -2), nearVertices);
		VoxelTransparentFace far = new VoxelTransparentFace(new Vector3(0, 0, -10), farVertices);
		List<VoxelTransparentFaceInstance> faces = new()
		{
			new VoxelTransparentFaceInstance(new ChunkCoordinate(0, 0, 0), 0, new Vector3(10, 0, 0), near),
			new VoxelTransparentFaceInstance(new ChunkCoordinate(1, 0, 0), 0, new Vector3(20, 0, 0), far),
		};

		VoxelVertex[] stream = VoxelTransparentStreamBuilder.Build(Vector3.Zero, -Vector3.UnitZ, faces);

		Assert.Equal(Color.Blue, stream[0].Color);
		Assert.Equal(new Vector3(22, 0, 0), stream[0].Position);
		Assert.Equal(Color.Red, stream[1].Color);
		Assert.Equal(new Vector3(11, 0, 0), stream[1].Position);
		Assert.Equal(new Vector4(0.1f, 2, 3, 1), stream[1].WaveParameters);
		Assert.Equal(new Color(17, 34, 51, 68), stream[1].PackedLightChannels);
	}

	[Fact]
	public void TransparentStreamUsesPrecomputedDepthAndStableCoordinateTies()
	{
		VoxelTransparentFace face = new VoxelTransparentFace(
			Vector3.Zero,
			new[] { new VoxelVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector3.UnitY) }
		);
		List<VoxelTransparentFaceInstance> faces = new()
		{
			new VoxelTransparentFaceInstance(new ChunkCoordinate(1, 0, 0), 0, Vector3.Zero, face, 5),
			new VoxelTransparentFaceInstance(new ChunkCoordinate(0, 0, 0), 0, Vector3.Zero, face, 5),
			new VoxelTransparentFaceInstance(new ChunkCoordinate(2, 0, 0), 0, Vector3.Zero, face, 10),
		};
		VoxelVertex[] destination = new VoxelVertex[3];

		int count = VoxelTransparentStreamBuilder.BuildSorted(faces, destination);

		Assert.Equal(3, count);
		Assert.Equal(new ChunkCoordinate(2, 0, 0), faces[0].Coordinate);
		Assert.Equal(new ChunkCoordinate(0, 0, 0), faces[1].Coordinate);
		Assert.Equal(new ChunkCoordinate(1, 0, 0), faces[2].Coordinate);
	}

	[Fact]
	public void TransparentCacheKeyChangesForCameraVisibilityAndGeometry()
	{
		VoxelTransparentCacheKey baseline = new VoxelTransparentCacheKey(4, 10, Matrix4x4.Identity);

		Assert.Equal(baseline, new VoxelTransparentCacheKey(4, 10, Matrix4x4.Identity));
		Assert.NotEqual(baseline, new VoxelTransparentCacheKey(5, 10, Matrix4x4.Identity));
		Assert.NotEqual(baseline, new VoxelTransparentCacheKey(4, 11, Matrix4x4.Identity));
		Assert.NotEqual(
			baseline,
			new VoxelTransparentCacheKey(4, 10, Matrix4x4.CreateTranslation(1, 0, 0))
		);
	}

	[Theory]
	[InlineData(0, 1, 64)]
	[InlineData(64, 64, 64)]
	[InlineData(64, 65, 128)]
	[InlineData(128, 1000, 1024)]
	public void VoxelBufferCapacityGrowsByPowersOfTwo(int current, int required, int expected)
	{
		Assert.Equal(expected, VoxelMesh.CalculateCapacity(current, required));
	}

}
