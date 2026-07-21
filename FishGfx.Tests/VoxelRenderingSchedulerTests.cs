using System;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelRenderingLightingTests
{
	[Fact]
	public void NewlyResidentChunksWaitForTheirFirstPublishedLightSnapshot()
	{
		(VoxelWorld world, VoxelPalette palette, _) = CreateCube(
			VoxelRenderMode.Opaque,
			1,
			1,
			1
		);
		using VoxelLighting lighting = new(world, palette);
		lighting.LoadChunk(default);
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			Atlas,
			maxWorkers: 1,
			lighting: lighting
		);

		Assert.Equal(0, scheduler.SchedulePending());
		Drain(lighting);
		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData result = WaitForResult(scheduler);

		Assert.True(lighting.TryGetChunkRevision(default, out long lightRevision));
		Assert.Equal(lightRevision, result.LightRevision);
	}

	[Fact]
	public void SchedulerWaitsForCompletedRelightingBeforePublishingChangedGeometry()
	{
		VoxelPaletteBuilder builder = new();
		ushort dark = builder.Add(
			new VoxelMaterial("Dark", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))
		);
		ushort emitting = builder.Add(
			new VoxelMaterial(
				"Emitter",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(0),
				light: new VoxelMaterialLightSettings(
					15,
					new VoxelBlockLight(15, 8, 2)
				)
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(dark));
		using VoxelLighting lighting = new(world, palette);
		lighting.LoadChunk(default);
		Drain(lighting);
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			Atlas,
			maxWorkers: 1,
			lighting: lighting
		);

		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData baseline = WaitForResult(scheduler);
		WaitForWorker(scheduler);
		world.SetVoxel(1, 1, 1, new VoxelCell(emitting));

		Assert.Equal(0, scheduler.SchedulePending());
		Drain(lighting);
		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData relit = WaitForResult(scheduler);

		Assert.True(relit.Revision > baseline.Revision);
		Assert.True(relit.LightRevision > baseline.LightRevision);
	}

	[Fact]
	public void WorldGenerationRejectsAnOldMeshAfterSameRevisionChunkReload()
	{
		(VoxelWorld world, VoxelPalette palette, ushort material) = CreateCube(
			VoxelRenderMode.Opaque,
			1,
			1,
			1
		);
		ChunkCoordinate coordinate = default;
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			Atlas,
			maxWorkers: 1
		);

		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData oldMesh = WaitForResult(scheduler);
		WaitForWorker(scheduler);
		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk oldChunk));
		Assert.True(VoxelRenderer.IsMeshCurrent(oldMesh, oldChunk, lighting: null));

		Assert.True(world.RemoveChunk(coordinate));
		Assert.True(world.SetVoxel(1, 1, 1, new VoxelCell(material)));
		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk reloadedChunk));

		Assert.Equal(oldMesh.Revision, reloadedChunk.Revision);
		Assert.NotEqual(oldMesh.WorldGeneration, reloadedChunk.Generation);
		Assert.False(VoxelRenderer.IsMeshCurrent(oldMesh, reloadedChunk, lighting: null));

		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData reloadedMesh = WaitForResult(scheduler);
		Assert.Equal(oldMesh.Revision, reloadedMesh.Revision);
		Assert.NotEqual(oldMesh.WorldGeneration, reloadedMesh.WorldGeneration);
		Assert.True(
			VoxelRenderer.IsMeshCurrent(reloadedMesh, reloadedChunk, lighting: null)
		);
	}

	[Fact]
	public void WorldAndLightGenerationsRejectAnOldMeshAfterSameRevisionResidentReload()
	{
		VoxelPaletteBuilder builder = new();
		ushort emitter = builder.Add(
			new VoxelMaterial(
				"Emitter",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(0),
				light: new VoxelMaterialLightSettings(
					15,
					new VoxelBlockLight(15, 8, 2)
				)
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		ChunkCoordinate coordinate = default;
		world.SetVoxel(1, 1, 1, new VoxelCell(emitter));
		using VoxelLighting lighting = new(world, palette);
		lighting.LoadChunk(coordinate);
		Drain(lighting);
		using VoxelMeshingScheduler scheduler = new(
			world,
			palette,
			Atlas,
			maxWorkers: 1,
			lighting: lighting
		);

		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData oldMesh = WaitForResult(scheduler);
		WaitForWorker(scheduler);
		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk oldChunk));
		Assert.True(VoxelRenderer.IsMeshCurrent(oldMesh, oldChunk, lighting));

		Assert.True(lighting.UnloadChunk(coordinate));
		Assert.True(world.RemoveChunk(coordinate));
		Assert.True(world.SetVoxel(1, 1, 1, new VoxelCell(emitter)));
		lighting.LoadChunk(coordinate);
		Drain(lighting);
		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk reloadedChunk));
		Assert.True(
			lighting.TryGetChunkState(
				coordinate,
				out long reloadedLightGeneration,
				out long reloadedLightRevision
			)
		);

		Assert.Equal(oldMesh.Revision, reloadedChunk.Revision);
		Assert.Equal(oldMesh.LightRevision, reloadedLightRevision);
		Assert.NotEqual(oldMesh.WorldGeneration, reloadedChunk.Generation);
		Assert.NotEqual(oldMesh.LightGeneration, reloadedLightGeneration);
		Assert.False(VoxelRenderer.IsMeshCurrent(oldMesh, reloadedChunk, lighting));

		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData reloadedMesh = WaitForResult(scheduler);
		Assert.Equal(oldMesh.Revision, reloadedMesh.Revision);
		Assert.Equal(oldMesh.LightRevision, reloadedMesh.LightRevision);
		Assert.NotEqual(oldMesh.WorldGeneration, reloadedMesh.WorldGeneration);
		Assert.NotEqual(oldMesh.LightGeneration, reloadedMesh.LightGeneration);
		Assert.True(VoxelRenderer.IsMeshCurrent(reloadedMesh, reloadedChunk, lighting));
	}

	[Fact]
	public void SchedulerRejectsLightingFromAnotherWorldOrPalette()
	{
		VoxelPaletteBuilder firstBuilder = new();
		firstBuilder.Add(
			new VoxelMaterial("First", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))
		);
		VoxelPalette firstPalette = firstBuilder.Build();
		VoxelPaletteBuilder secondBuilder = new();
		secondBuilder.Add(
			new VoxelMaterial("Second", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))
		);
		VoxelPalette secondPalette = secondBuilder.Build();
		VoxelWorld firstWorld = new();
		VoxelWorld secondWorld = new();
		using VoxelLighting lighting = new(firstWorld, firstPalette);

		Assert.Throws<ArgumentException>(
			() => new VoxelMeshingScheduler(
				secondWorld,
				firstPalette,
				Atlas,
				lighting: lighting
			)
		);
		Assert.Throws<ArgumentException>(
			() => new VoxelMeshingScheduler(
				firstWorld,
				secondPalette,
				Atlas,
				lighting: lighting
			)
		);
	}
}
