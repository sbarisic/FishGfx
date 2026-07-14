using System;
using System.Runtime.InteropServices;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public class VoxelLightingTests
{
	[Fact]
	public void LightValuesValidateRoundTripAndUsePackedStorage()
	{
		VoxelBlockLight block = new(1, 2, 3);
		VoxelLight light = new(block, 4);

		Assert.Equal((byte)1, block.Red);
		Assert.Equal((byte)2, block.Green);
		Assert.Equal((byte)3, block.Blue);
		Assert.Equal(block, light.Block);
		Assert.Equal((byte)4, light.Sky);
		Assert.Equal((ushort)0x4321, light.Packed);
		Assert.Equal(2, Marshal.SizeOf<VoxelLight>());
		Assert.Equal(default, new VoxelLight(default, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelBlockLight(16, 0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelBlockLight(0, 16, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelBlockLight(0, 0, 16));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelLight(default, 16));
	}

	[Fact]
	public void MaterialLightSettingsInferDefaultsAndValidate()
	{
		VoxelMaterial opaque = new("Opaque", VoxelRenderMode.Opaque, new VoxelFaceTiles(0));
		VoxelMaterial transparent = new("Transparent", VoxelRenderMode.Transparent, new VoxelFaceTiles(0));
		VoxelMaterial nonOccludingOpaque = new(
			"Non-occluding",
			VoxelRenderMode.Opaque,
			new VoxelFaceTiles(0),
			occludesFaces: false
		);
		VoxelMaterialLightSettings emissive = new(1, new VoxelBlockLight(15, 10, 5));
		VoxelMaterial explicitLight = new(
			"Explicit",
			VoxelRenderMode.Transparent,
			new VoxelFaceTiles(0),
			light: emissive
		);

		Assert.Equal((byte)15, opaque.Light.Opacity);
		Assert.Equal((byte)0, transparent.Light.Opacity);
		Assert.Equal((byte)0, nonOccludingOpaque.Light.Opacity);
		Assert.Equal(default, opaque.Light.Emission);
		Assert.Equal(emissive, explicitLight.Light);
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelMaterialLightSettings(16));
	}

	[Fact]
	public void LightingOptionsDefaultAndRejectNonPositiveBudgets()
	{
		VoxelLightingOptions options = new();

		Assert.Equal(65_536, options.UpdateBudget);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.UpdateBudget = 0);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.UpdateBudget = -1);
	}

	[Fact]
	public void MeshingSchedulerRequiresLightingFromSameWorldAndPalette()
	{
		TestPalette materials = new();
		TestPalette otherMaterials = new();
		VoxelWorld world = new();
		VoxelWorld otherWorld = new();
		VoxelAtlasLayout atlas = new(1, 1, 1, 1);
		using VoxelLighting lighting = new(world, materials.Palette);

		using (VoxelMeshingScheduler scheduler = new(
			world,
			materials.Palette,
			atlas,
			maxWorkers: 1,
			lighting: lighting
		))
		{
			Assert.Equal(0, scheduler.PendingCount);
		}

		Assert.Throws<ArgumentException>(() =>
		{
			using VoxelMeshingScheduler scheduler = new(
				otherWorld,
				materials.Palette,
				atlas,
				maxWorkers: 1,
				lighting: lighting
			);
		});
		Assert.Throws<ArgumentException>(() =>
		{
			using VoxelMeshingScheduler scheduler = new(
				world,
				otherMaterials.Palette,
				atlas,
				maxWorkers: 1,
				lighting: lighting
			);
		});
	}

	[Fact]
	public void BlockLightFallsOffPerChannelAndOpaqueVoxelBlocksTunnel()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.FillChunk(chunk, new VoxelCell(materials.Stone));
		CarveHorizontalTunnel(world, 1, 6, y: 8, z: 8);
		world.SetVoxel(1, 8, 8, new VoxelCell(materials.WarmLamp));
		world.SetVoxel(4, 8, 8, new VoxelCell(materials.Stone));
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);

		Drain(lighting);

		AssertBlock(lighting.GetLight(1, 8, 8), 15, 12, 8);
		AssertBlock(lighting.GetLight(2, 8, 8), 14, 11, 7);
		AssertBlock(lighting.GetLight(3, 8, 8), 13, 10, 6);
		AssertBlock(lighting.GetLight(4, 8, 8), 0, 0, 0);
		AssertBlock(lighting.GetLight(5, 8, 8), 0, 0, 0);
	}

	[Fact]
	public void SkyShaftPreservesDirectLightAndAppliesTransparentAttenuation()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.FillChunk(chunk, new VoxelCell(materials.Stone));
		for (int y = 0; y < VoxelWorld.ChunkSize; y++)
			world.SetVoxel(8, y, 8, VoxelCell.Air);

		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk, skyExposedAbove: true);
		Drain(lighting);

		Assert.Equal((byte)15, lighting.GetLight(8, 15, 8).Sky);
		Assert.Equal((byte)15, lighting.GetLight(8, 0, 8).Sky);

		world.SetVoxel(8, 10, 8, new VoxelCell(materials.Filter));
		Drain(lighting);

		Assert.Equal((byte)14, lighting.GetLight(8, 10, 8).Sky);
		Assert.Equal((byte)14, lighting.GetLight(8, 0, 8).Sky);

		world.SetVoxel(8, 6, 8, new VoxelCell(materials.Stone));
		Drain(lighting);

		Assert.Equal((byte)0, lighting.GetLight(8, 5, 8).Sky);
	}

	[Fact]
	public void IndirectSkyAttenuatesAfterTurningSidewaysAndDownward()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.FillChunk(chunk, new VoxelCell(materials.Stone));
		for (int y = 11; y < VoxelWorld.ChunkSize; y++)
			world.SetVoxel(2, y, 8, VoxelCell.Air);
		for (int x = 2; x <= 5; x++)
			world.SetVoxel(x, 14, 8, VoxelCell.Air);
		for (int y = 11; y <= 14; y++)
			world.SetVoxel(5, y, 8, VoxelCell.Air);

		using VoxelLighting lighting = CreateLighting(
			world,
			materials.Palette,
			chunk,
			skyExposedAbove: true
		);
		Drain(lighting);

		Assert.Equal((byte)15, lighting.GetLight(2, 11, 8).Sky);
		Assert.Equal((byte)14, lighting.GetLight(3, 14, 8).Sky);
		Assert.Equal((byte)12, lighting.GetLight(5, 14, 8).Sky);
		Assert.Equal((byte)11, lighting.GetLight(5, 13, 8).Sky);
		Assert.Equal((byte)9, lighting.GetLight(5, 11, 8).Sky);
	}

	[Fact]
	public void RemovingOccluderAndSourceRecalculatesLight()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.FillChunk(chunk, new VoxelCell(materials.Stone));
		CarveHorizontalTunnel(world, 1, 6, y: 7, z: 7);
		world.SetVoxel(1, 7, 7, new VoxelCell(materials.RedLamp));
		world.SetVoxel(4, 7, 7, new VoxelCell(materials.Stone));
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		AssertBlock(lighting.GetLight(5, 7, 7), 0, 0, 0);

		world.SetVoxel(4, 7, 7, VoxelCell.Air);
		Drain(lighting);
		AssertBlock(lighting.GetLight(5, 7, 7), 11, 0, 0);

		world.SetVoxel(1, 7, 7, VoxelCell.Air);
		Drain(lighting);
		AssertBlock(lighting.GetLight(5, 7, 7), 0, 0, 0);
	}

	[Fact]
	public void RemovingOneOfOverlappingSourcesPreservesOtherChannels()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.FillChunk(chunk, new VoxelCell(materials.Stone));
		CarveHorizontalTunnel(world, 1, 5, y: 6, z: 6);
		world.SetVoxel(1, 6, 6, new VoxelCell(materials.RedLamp));
		world.SetVoxel(5, 6, 6, new VoxelCell(materials.BlueLamp));
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);

		AssertBlock(lighting.GetLight(3, 6, 6), 13, 0, 13);

		world.SetVoxel(1, 6, 6, VoxelCell.Air);
		Drain(lighting);

		AssertBlock(lighting.GetLight(3, 6, 6), 0, 0, 13);
	}

	[Fact]
	public void UnknownChunkBlocksLightButExplicitAllAirChunkPropagatesIt()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate sourceChunk = new(0, 0, 0);
		ChunkCoordinate airChunk = new(1, 0, 0);
		world.SetVoxel(15, 8, 8, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(sourceChunk, skyExposedAbove: false);
		Drain(lighting);

		Assert.Equal(1, lighting.ResidentChunkCount);
		Assert.Equal(default, lighting.GetLight(16, 8, 8));

		lighting.LoadChunk(airChunk, skyExposedAbove: false);
		Drain(lighting);

		Assert.Equal(2, lighting.ResidentChunkCount);
		AssertBlock(lighting.GetLight(16, 8, 8), 14, 0, 0);
		Assert.False(world.TryGetChunk(airChunk, out _));

		lighting.UnloadChunk(airChunk);
		Assert.Equal(default, lighting.GetLight(16, 8, 8));
		Assert.Equal(1, lighting.ResidentChunkCount);

		lighting.LoadChunk(airChunk, skyExposedAbove: false);
		Drain(lighting);
		AssertBlock(lighting.GetLight(16, 8, 8), 14, 0, 0);
	}

	[Fact]
	public void PublishingNewLitNeighborInvalidatesPublishedChunkHalo()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate sourceChunk = new(0, 0, 0);
		ChunkCoordinate neighborChunk = new(1, 0, 0);
		world.SetVoxel(15, 8, 8, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(sourceChunk, skyExposedAbove: false);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(sourceChunk, out long beforeNeighbor));

		lighting.LoadChunk(neighborChunk, skyExposedAbove: false);
		Drain(lighting);

		AssertBlock(lighting.GetLight(16, 8, 8), 14, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(sourceChunk, out long afterNeighbor));
		Assert.Equal(beforeNeighbor + 1, afterNeighbor);
	}

	[Fact]
	public void PaddedTopHaloUsesSkyExposureOfHorizontalAndDiagonalNeighbors()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate center = new(0, 0, 0);
		ChunkCoordinate west = new(-1, 0, 0);
		ChunkCoordinate northWest = new(-1, 0, -1);
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(center, skyExposedAbove: false);
		lighting.LoadChunk(west, skyExposedAbove: true);
		lighting.LoadChunk(northWest, skyExposedAbove: true);
		Drain(lighting);

		Assert.True(lighting.TryCreateSnapshot(center, out VoxelLightChunkSnapshot snapshot));
		Assert.Equal((byte)15, snapshot.GetLight(-1, VoxelWorld.ChunkSize, 8).Sky);
		Assert.Equal((byte)15, snapshot.GetLight(-1, VoxelWorld.ChunkSize, -1).Sky);
		Assert.Equal((byte)0, snapshot.GetLight(16, VoxelWorld.ChunkSize, 8).Sky);
	}

	[Fact]
	public void ExposureOnlyToggleInvalidatesEveryHorizontalSyntheticHaloConsumer()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate center = new(0, 0, 0);
		ChunkCoordinate east = new(1, 0, 0);
		ChunkCoordinate southEast = new(1, 0, 1);
		world.FillChunk(center, new VoxelCell(materials.Stone));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(center, skyExposedAbove: false);
		lighting.LoadChunk(east, skyExposedAbove: false);
		lighting.LoadChunk(southEast, skyExposedAbove: false);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(east, out long eastRevision));
		Assert.True(lighting.TryGetChunkRevision(southEast, out long diagonalRevision));
		Assert.Equal((byte)0, lighting.GetLight(8, 15, 8).Sky);

		lighting.SetSkyExposedAbove(center, true);
		Drain(lighting);

		Assert.True(lighting.TryGetChunkRevision(east, out long exposedEastRevision));
		Assert.True(lighting.TryGetChunkRevision(southEast, out long exposedDiagonalRevision));
		Assert.Equal(eastRevision + 1, exposedEastRevision);
		Assert.Equal(diagonalRevision + 1, exposedDiagonalRevision);
		Assert.Equal((byte)0, lighting.GetLight(8, 15, 8).Sky);
		Assert.True(lighting.TryCreateSnapshot(east, out VoxelLightChunkSnapshot snapshot));
		Assert.Equal((byte)15, snapshot.GetLight(-1, VoxelWorld.ChunkSize, 8).Sky);

		lighting.SetSkyExposedAbove(center, false);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(east, out long hiddenEastRevision));
		Assert.Equal(exposedEastRevision + 1, hiddenEastRevision);
		Assert.True(lighting.TryCreateSnapshot(east, out snapshot));
		Assert.Equal((byte)0, snapshot.GetLight(-1, VoxelWorld.ChunkSize, 8).Sky);
	}

	[Fact]
	public void UnloadRetainsPublishedHaloUntilOneAtomicRelightCommit()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate source = new(0, 0, 0);
		ChunkCoordinate neighbor = new(1, 0, 0);
		world.SetVoxel(15, 8, 8, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(source, skyExposedAbove: false);
		lighting.LoadChunk(neighbor, skyExposedAbove: false);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(neighbor, out long beforeUnload));
		Assert.True(lighting.TryCreateSnapshot(neighbor, out VoxelLightChunkSnapshot before));
		Assert.Equal((byte)15, before.GetLight(-1, 8, 8).Block.Red);

		Assert.True(lighting.UnloadChunk(source));
		Assert.Equal(default, lighting.GetLight(15, 8, 8));
		Assert.True(lighting.TryGetChunkRevision(neighbor, out long pendingRevision));
		Assert.Equal(beforeUnload, pendingRevision);
		Assert.True(lighting.TryCreateSnapshot(neighbor, out VoxelLightChunkSnapshot pending));
		Assert.Equal((byte)15, pending.GetLight(-1, 8, 8).Block.Red);
		lighting.Update(1);
		Assert.True(lighting.TryGetChunkRevision(neighbor, out pendingRevision));
		Assert.Equal(beforeUnload, pendingRevision);

		Drain(lighting);

		Assert.True(lighting.TryGetChunkRevision(neighbor, out long afterUnload));
		Assert.Equal(beforeUnload + 1, afterUnload);
		Assert.True(lighting.TryCreateSnapshot(neighbor, out VoxelLightChunkSnapshot after));
		Assert.Equal((byte)0, after.GetLight(-1, 8, 8).Block.Red);
	}

	[Fact]
	public void ReloadedCoordinateUsesNewMonotonicLightingGeneration()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate coordinate = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, coordinate);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkState(coordinate, out long firstGeneration, out _));

		Assert.True(lighting.UnloadChunk(coordinate));
		Drain(lighting);
		lighting.LoadChunk(coordinate, skyExposedAbove: false);
		Drain(lighting);

		Assert.True(lighting.TryGetChunkState(coordinate, out long secondGeneration, out _));
		Assert.True(secondGeneration > firstGeneration);
		Assert.True(lighting.TryCreateSnapshot(coordinate, out VoxelLightChunkSnapshot snapshot));
		Assert.Equal(secondGeneration, snapshot.Generation);
	}

	[Fact]
	public void PublishedSourceUnloadAndDarkReloadRemovesOldNeighborLight()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate sourceChunk = new(0, 0, 0);
		ChunkCoordinate neighborChunk = new(1, 0, 0);
		world.SetVoxel(15, 8, 8, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(sourceChunk, skyExposedAbove: false);
		lighting.LoadChunk(neighborChunk, skyExposedAbove: false);
		Drain(lighting);
		AssertBlock(lighting.GetLight(16, 8, 8), 14, 0, 0);

		Assert.True(lighting.UnloadChunk(sourceChunk));
		world.SetVoxel(15, 8, 8, VoxelCell.Air);
		lighting.LoadChunk(sourceChunk, skyExposedAbove: false);
		Drain(lighting);

		AssertBlock(lighting.GetLight(15, 8, 8), 0, 0, 0);
		AssertBlock(lighting.GetLight(16, 8, 8), 0, 0, 0);
	}

	[Fact]
	public void UnpublishedInFlightSourceUnloadCannotLeaveNeighborLight()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate neighborChunk = new(0, 0, 0);
		ChunkCoordinate sourceChunk = new(1, 0, 0);
		world.SetVoxel(16, 0, 0, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(neighborChunk, skyExposedAbove: false);
		Drain(lighting);
		lighting.LoadChunk(sourceChunk, skyExposedAbove: false);

		Assert.Equal(VoxelWorld.ChunkVolume + 2, lighting.Update(VoxelWorld.ChunkVolume + 2));
		Assert.False(lighting.TryGetChunkRevision(sourceChunk, out _));
		Assert.True(lighting.UnloadChunk(sourceChunk));
		Drain(lighting);

		Assert.Equal(1, lighting.ResidentChunkCount);
		AssertBlock(lighting.GetLight(15, 0, 0), 0, 0, 0);
		Assert.Equal(default, lighting.GetLight(16, 0, 0));
	}

	[Fact]
	public void LightCrossesNegativeWorldAndChunkCoordinates()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		world.SetVoxel(-1, 4, 4, new VoxelCell(materials.BlueLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(new ChunkCoordinate(-1, 0, 0), skyExposedAbove: false);
		lighting.LoadChunk(new ChunkCoordinate(0, 0, 0), skyExposedAbove: false);
		Drain(lighting);

		AssertBlock(lighting.GetLight(-1, 4, 4), 0, 0, 15);
		AssertBlock(lighting.GetLight(0, 4, 4), 0, 0, 14);
	}

	[Fact]
	public void DirectSkyCrossesVerticalChunkBoundary()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate lower = new(0, 0, 0);
		ChunkCoordinate upper = new(0, 1, 0);
		world.FillChunk(lower, new VoxelCell(materials.Stone));
		world.FillChunk(upper, new VoxelCell(materials.Stone));
		for (int y = 0; y < VoxelWorld.ChunkSize * 2; y++)
			world.SetVoxel(8, y, 8, VoxelCell.Air);

		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(lower, skyExposedAbove: false);
		lighting.LoadChunk(upper, skyExposedAbove: true);
		Drain(lighting);

		Assert.Equal((byte)15, lighting.GetLight(8, 16, 8).Sky);
		Assert.Equal((byte)15, lighting.GetLight(8, 15, 8).Sky);
		Assert.Equal((byte)15, lighting.GetLight(8, 0, 8).Sky);
	}

	[Fact]
	public void DeepFilterColumnAttenuatesContinuouslyAcrossThreeChunks()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate lower = new(0, 0, 0);
		ChunkCoordinate middle = new(0, 1, 0);
		ChunkCoordinate upper = new(0, 2, 0);
		world.FillChunk(lower, new VoxelCell(materials.Stone));
		world.FillChunk(middle, new VoxelCell(materials.Stone));
		world.FillChunk(upper, new VoxelCell(materials.Stone));
		for (int y = 0; y < VoxelWorld.ChunkSize * 3; y++)
			world.SetVoxel(8, y, 8, VoxelCell.Air);
		for (int y = 0; y <= 36; y++)
			world.SetVoxel(8, y, 8, new VoxelCell(materials.Filter));

		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(lower, skyExposedAbove: false);
		lighting.LoadChunk(middle, skyExposedAbove: false);
		lighting.LoadChunk(upper, skyExposedAbove: true);
		Drain(lighting);

		for (int y = 37; y < VoxelWorld.ChunkSize * 3; y++)
			Assert.Equal((byte)15, lighting.GetLight(8, y, 8).Sky);
		for (int y = 36; y >= 0; y--)
		{
			int waterDepth = 37 - y;
			byte expected = (byte)Math.Max(0, 15 - waterDepth);
			Assert.Equal(expected, lighting.GetLight(8, y, 8).Sky);
		}

		Assert.Equal((byte)10, lighting.GetLight(8, 32, 8).Sky);
		Assert.Equal((byte)9, lighting.GetLight(8, 31, 8).Sky);
		Assert.Equal((byte)0, lighting.GetLight(8, 16, 8).Sky);
		Assert.Equal((byte)0, lighting.GetLight(8, 15, 8).Sky);
		Assert.True(lighting.TryGetChunkRevision(lower, out long lowerRevision));
		Assert.True(lighting.TryGetChunkRevision(middle, out long middleRevision));
		Assert.True(lighting.TryGetChunkRevision(upper, out long upperRevision));

		for (int frame = 0; frame < 8; frame++)
		{
			Assert.Equal(0, lighting.Update(1));
			Assert.True(lighting.IsIdle);
		}

		Assert.True(lighting.TryGetChunkRevision(lower, out long lowerAfter));
		Assert.True(lighting.TryGetChunkRevision(middle, out long middleAfter));
		Assert.True(lighting.TryGetChunkRevision(upper, out long upperAfter));
		Assert.Equal(lowerRevision, lowerAfter);
		Assert.Equal(middleRevision, middleAfter);
		Assert.Equal(upperRevision, upperAfter);
	}

	[Fact]
	public void BudgetedUpdatesPublishOnlyAfterTransactionConverges()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long emptyRevision));
		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));

		Assert.False(lighting.IsIdle);
		Assert.True(lighting.PendingCount > 0);
		lighting.Update(1);

		Assert.False(lighting.IsIdle);
		Assert.Equal(default, lighting.GetLight(8, 8, 8));
		Assert.True(lighting.TryGetChunkRevision(chunk, out long partialRevision));
		Assert.Equal(emptyRevision, partialRevision);

		Drain(lighting, budget: 32);
		Assert.True(lighting.IsIdle);
		Assert.Equal(0, lighting.PendingCount);
		AssertBlock(lighting.GetLight(8, 8, 8), 15, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long litRevision));
		Assert.Equal(emptyRevision + 1, litRevision);

		world.SetVoxel(8, 8, 8, VoxelCell.Air);
		lighting.Update(1);
		AssertBlock(lighting.GetLight(8, 8, 8), 15, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(chunk, out partialRevision));
		Assert.Equal(litRevision, partialRevision);
		Drain(lighting, budget: 32);
		AssertBlock(lighting.GetLight(8, 8, 8), 0, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long darkRevision));
		Assert.Equal(litRevision + 1, darkRevision);
	}

	[Fact]
	public void UpdateOneBudgetsEveryInitialPublicationPhaseAtomically()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(chunk, skyExposedAbove: false);

		int updates = 0;
		while (!lighting.IsIdle && updates < 20_000)
		{
			Assert.True(lighting.PendingCount > 0);
			Assert.False(lighting.TryGetChunkRevision(chunk, out _));
			Assert.Equal(1, lighting.Update(1));
			updates++;
			if (!lighting.IsIdle)
				Assert.False(lighting.TryGetChunkRevision(chunk, out _));
		}

		Assert.True(lighting.IsIdle);
		Assert.Equal(4 * VoxelWorld.ChunkVolume, updates);
		Assert.Equal(0, lighting.PendingCount);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long revision));
		Assert.Equal(1, revision);
	}

	[Fact]
	public void UpdateOneBudgetsDirectSkyAndPublishesItAtomically()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long before));

		lighting.SetSkyExposedAbove(chunk, true);
		int updates = 0;
		while (!lighting.IsIdle && updates < 20_000)
		{
			Assert.True(lighting.PendingCount > 0);
			Assert.Equal((byte)0, lighting.GetLight(8, 8, 8).Sky);
			Assert.True(lighting.TryGetChunkRevision(chunk, out long partial));
			Assert.Equal(before, partial);
			Assert.Equal(1, lighting.Update(1));
			updates++;
		}

		Assert.True(lighting.IsIdle);
		Assert.Equal(0, lighting.PendingCount);
		Assert.Equal((byte)15, lighting.GetLight(8, 8, 8).Sky);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long after));
		Assert.Equal(before + 1, after);
	}

	[Fact]
	public void UpdateOneBudgetsMaterialDiffPropagationAndFinalComparison()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long before));

		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));
		Assert.Equal(1, lighting.PendingCount);
		Assert.Equal(1, lighting.Update(1));
		Assert.InRange(lighting.PendingCount, 1, VoxelWorld.ChunkVolume - 1);
		Assert.Equal(default, lighting.GetLight(8, 8, 8));
		int updates = 1;
		while (!lighting.IsIdle && updates < 100_000)
		{
			Assert.True(lighting.PendingCount > 0);
			Assert.Equal(default, lighting.GetLight(8, 8, 8));
			Assert.True(lighting.TryGetChunkRevision(chunk, out long partial));
			Assert.Equal(before, partial);
			Assert.Equal(1, lighting.Update(1));
			updates++;
		}

		Assert.True(lighting.IsIdle);
		Assert.Equal(0, lighting.PendingCount);
		AssertBlock(lighting.GetLight(8, 8, 8), 15, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long after));
		Assert.Equal(before + 1, after);
	}

	[Fact]
	public void UpdateOneBudgetsFullRebuildSeedingAndComparisonWithoutRepublishing()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long before));

		lighting.RequestFullRebuild();
		int updates = 0;
		while (!lighting.IsIdle && updates < 20_000)
		{
			Assert.True(lighting.PendingCount > 0);
			Assert.True(lighting.TryGetChunkRevision(chunk, out long partial));
			Assert.Equal(before, partial);
			Assert.Equal(1, lighting.Update(1));
			updates++;
		}

		Assert.True(lighting.IsIdle);
		Assert.Equal(3 * VoxelWorld.ChunkVolume, updates);
		Assert.Equal(0, lighting.PendingCount);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long after));
		Assert.Equal(before, after);
	}

	[Fact]
	public void EditArrivingAfterLazyChunkCaptureQueuesASecondAtomicTransaction()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(chunk, skyExposedAbove: false);
		Assert.Equal(1, lighting.Update(1));

		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));
		int updates = 1;
		while (!lighting.TryGetChunkRevision(chunk, out _) && updates < 20_000)
		{
			Assert.Equal(1, lighting.Update(1));
			updates++;
		}

		Assert.Equal(4 * VoxelWorld.ChunkVolume, updates);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long firstRevision));
		Assert.Equal(default, lighting.GetLight(8, 8, 8));
		Assert.False(lighting.IsIdle);

		Drain(lighting, budget: 32);
		AssertBlock(lighting.GetLight(8, 8, 8), 15, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long finalRevision));
		Assert.Equal(firstRevision + 1, finalRevision);
	}

	[Fact]
	public void LoadingChunkDuringBudgetedTransactionDoesNotRestartFirstPublication()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate first = new(0, 0, 0);
		ChunkCoordinate second = new(10, 0, 0);
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(first, skyExposedAbove: false);

		const int budget = 64;
		int totalProcessed = lighting.Update(budget);
		Assert.False(lighting.TryGetChunkRevision(first, out _));

		lighting.LoadChunk(second, skyExposedAbove: false);
		const int initialPublicationWork = 4 * VoxelWorld.ChunkVolume;
		for (int update = 0; update < initialPublicationWork / budget + 1; update++)
		{
			if (lighting.TryGetChunkRevision(first, out _))
				break;

			totalProcessed += lighting.Update(budget);
		}

		Assert.True(lighting.TryGetChunkRevision(first, out _));
		Assert.InRange(totalProcessed, 1, initialPublicationWork);
		Assert.False(lighting.TryGetChunkRevision(second, out _));
		Drain(lighting, budget);
		Assert.True(lighting.TryGetChunkRevision(second, out _));
	}

	[Fact]
	public void RepeatedExternalUnloadsDoNotRestartIncrementalPublication()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate[] external =
		{
			new ChunkCoordinate(0, 0, 0),
			new ChunkCoordinate(10, 0, 0),
			new ChunkCoordinate(20, 0, 0),
			new ChunkCoordinate(30, 0, 0),
		};
		ChunkCoordinate target = new(100, 0, 0);
		using VoxelLighting lighting = new(world, materials.Palette);
		foreach (ChunkCoordinate coordinate in external)
			lighting.LoadChunk(coordinate, skyExposedAbove: false);
		Drain(lighting);

		const int budget = 64;
		lighting.LoadChunk(target, skyExposedAbove: false);
		int processed = lighting.Update(budget);
		foreach (ChunkCoordinate coordinate in external)
		{
			Assert.True(lighting.UnloadChunk(coordinate));
			processed += lighting.Update(budget);
		}

		const int initialPublicationWork = 4 * VoxelWorld.ChunkVolume;
		for (int update = 0; update < initialPublicationWork / budget
			&& !lighting.TryGetChunkRevision(target, out _); update++)
			processed += lighting.Update(budget);

		Assert.True(lighting.TryGetChunkRevision(target, out _));
		Assert.InRange(processed, 1, initialPublicationWork);
		Drain(lighting, budget);
	}

	[Fact]
	public void BulkChunkReplacementAndFullRebuildReplacePublishedSources()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.SetVoxel(2, 2, 2, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		AssertBlock(lighting.GetLight(2, 2, 2), 15, 0, 0);

		VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
		replacement[13 + VoxelWorld.ChunkSize * (13 + VoxelWorld.ChunkSize * 13)] =
			new VoxelCell(materials.BlueLamp);
		Assert.True(world.SetChunk(chunk, replacement));
		lighting.Update(1);
		AssertBlock(lighting.GetLight(2, 2, 2), 15, 0, 0);

		Drain(lighting);
		AssertBlock(lighting.GetLight(2, 2, 2), 0, 0, 0);
		AssertBlock(lighting.GetLight(13, 13, 13), 0, 0, 15);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long beforeRebuild));

		lighting.RequestFullRebuild();
		Drain(lighting);
		AssertBlock(lighting.GetLight(13, 13, 13), 0, 0, 15);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long afterRebuild));
		Assert.Equal(beforeRebuild, afterRebuild);
	}

	[Fact]
	public void UnloadDuringBudgetedFullRebuildConverges()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate retained = new(0, 0, 0);
		ChunkCoordinate removed = new(1, 0, 0);
		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(retained, skyExposedAbove: false);
		lighting.LoadChunk(removed, skyExposedAbove: false);
		Drain(lighting);
		lighting.RequestFullRebuild();
		Assert.Equal(1, lighting.Update(1));

		Assert.True(lighting.UnloadChunk(removed));
		Drain(lighting);

		Assert.Equal(1, lighting.ResidentChunkCount);
		Assert.Equal(default, lighting.GetLight(16, 8, 8));
		AssertBlock(lighting.GetLight(8, 8, 8), 15, 0, 0);
	}

	[Fact]
	public void LightEquivalentMaterialEditDoesNotPublishNewRevision()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.SetVoxel(8, 8, 8, new VoxelCell(materials.Stone));
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long beforeEdit));

		world.SetVoxel(8, 8, 8, new VoxelCell(materials.StoneVariant));
		Assert.True(lighting.IsIdle);
		Assert.Equal(0, lighting.PendingCount);
		Assert.Equal(0, lighting.Update(1));

		Assert.True(lighting.TryGetChunkRevision(chunk, out long afterEdit));
		Assert.Equal(beforeEdit, afterEdit);
	}

	[Fact]
	public void ExactLightChangesCoalesceByCellAndPublishTheLatestMaterial()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long before));

		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));
		world.SetVoxel(8, 8, 8, new VoxelCell(materials.BlueLamp));
		Assert.Equal(1, lighting.PendingCount);
		Assert.Equal(1, lighting.Update(1));
		Assert.Equal(default, lighting.GetLight(8, 8, 8));
		Assert.True(lighting.TryGetChunkRevision(chunk, out long partial));
		Assert.Equal(before, partial);

		Drain(lighting, budget: 32);
		AssertBlock(lighting.GetLight(8, 8, 8), 0, 0, 15);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long after));
		Assert.Equal(before + 1, after);
	}

	[Fact]
	public void RedundantEmitterRemovalDoesNotIncrementRevision()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));
		world.SetVoxel(9, 8, 8, new VoxelCell(materials.DimRedLamp));
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long beforeRemoval));
		VoxelLight before = lighting.GetLight(10, 8, 8);

		world.SetVoxel(9, 8, 8, VoxelCell.Air);
		int processed = Drain(lighting, budget: 16);

		Assert.True(processed > 0);
		Assert.Equal(before, lighting.GetLight(10, 8, 8));
		Assert.True(lighting.TryGetChunkRevision(chunk, out long afterRemoval));
		Assert.Equal(beforeRemoval, afterRemoval);
	}

	[Fact]
	public void SkyExposureCanBeChangedAfterChunkLoad()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new(0, 0, 0);
		using VoxelLighting lighting = CreateLighting(world, materials.Palette, chunk);
		Drain(lighting);
		Assert.Equal((byte)0, lighting.GetLight(8, 8, 8).Sky);

		lighting.SetSkyExposedAbove(chunk, true);
		Drain(lighting);
		Assert.Equal((byte)15, lighting.GetLight(8, 8, 8).Sky);

		lighting.SetSkyExposedAbove(chunk, false);
		Drain(lighting);
		Assert.Equal((byte)0, lighting.GetLight(8, 8, 8).Sky);
	}

	private static VoxelLighting CreateLighting(
		VoxelWorld world,
		VoxelPalette palette,
		ChunkCoordinate coordinate,
		bool skyExposedAbove = false
	)
	{
		VoxelLighting lighting = new(world, palette);
		lighting.LoadChunk(coordinate, skyExposedAbove);
		return lighting;
	}

	private static int Drain(VoxelLighting lighting, int? budget = null)
	{
		int processed = 0;
		for (int iteration = 0; iteration < 20_000 && !lighting.IsIdle; iteration++)
			processed += lighting.Update(budget);

		Assert.True(lighting.IsIdle, $"Lighting did not converge; {lighting.PendingCount} cells remain pending.");
		Assert.Equal(0, lighting.PendingCount);
		return processed;
	}

	private static void AssertBlock(VoxelLight light, byte red, byte green, byte blue)
	{
		Assert.Equal(red, light.Block.Red);
		Assert.Equal(green, light.Block.Green);
		Assert.Equal(blue, light.Block.Blue);
	}

	private static void CarveHorizontalTunnel(VoxelWorld world, int minX, int maxX, int y, int z)
	{
		for (int x = minX; x <= maxX; x++)
			world.SetVoxel(x, y, z, VoxelCell.Air);
	}

	private sealed class TestPalette
	{
		internal TestPalette()
		{
			VoxelPaletteBuilder builder = new();
			Stone = builder.Add(new VoxelMaterial("Stone", VoxelRenderMode.Opaque, new VoxelFaceTiles(0)));
			StoneVariant = builder.Add(
				new VoxelMaterial("Stone variant", VoxelRenderMode.Opaque, new VoxelFaceTiles(1))
			);
			WarmLamp = builder.Add(
				new VoxelMaterial(
					"Warm lamp",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(0),
					light: new VoxelMaterialLightSettings(15, new VoxelBlockLight(15, 12, 8))
				)
			);
			RedLamp = builder.Add(
				new VoxelMaterial(
					"Red lamp",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(0),
					light: new VoxelMaterialLightSettings(15, new VoxelBlockLight(15, 0, 0))
				)
			);
			DimRedLamp = builder.Add(
				new VoxelMaterial(
					"Dim red lamp",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(0),
					light: new VoxelMaterialLightSettings(15, new VoxelBlockLight(14, 0, 0))
				)
			);
			BlueLamp = builder.Add(
				new VoxelMaterial(
					"Blue lamp",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(0),
					light: new VoxelMaterialLightSettings(15, new VoxelBlockLight(0, 0, 15))
				)
			);
			Filter = builder.Add(
				new VoxelMaterial(
					"Filter",
					VoxelRenderMode.Transparent,
					new VoxelFaceTiles(0),
					light: new VoxelMaterialLightSettings(1)
				)
			);
			Palette = builder.Build();
		}

		internal VoxelPalette Palette { get; }
		internal ushort Stone { get; }
		internal ushort StoneVariant { get; }
		internal ushort WarmLamp { get; }
		internal ushort RedLamp { get; }
		internal ushort DimRedLamp { get; }
		internal ushort BlueLamp { get; }
		internal ushort Filter { get; }
	}
}
