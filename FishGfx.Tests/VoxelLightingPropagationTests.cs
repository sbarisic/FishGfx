using System;
using System.Runtime.InteropServices;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelLightingTests
{
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
		{
			world.SetVoxel(8, y, 8, VoxelCell.Air);
		}

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
		{
			world.SetVoxel(2, y, 8, VoxelCell.Air);
		}

		for (int x = 2; x <= 5; x++)
		{
			world.SetVoxel(x, 14, 8, VoxelCell.Air);
		}

		for (int y = 11; y <= 14; y++)
		{
			world.SetVoxel(5, y, 8, VoxelCell.Air);
		}

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
	public void PublishingNewCornerLightInvalidatesDiagonalChunkHalo()
	{
		TestPalette materials = new TestPalette();
		VoxelWorld world = new VoxelWorld();
		ChunkCoordinate source = new ChunkCoordinate(0, 0, 0);
		ChunkCoordinate diagonal = new ChunkCoordinate(1, 1, 1);
		using VoxelLighting lighting = new VoxelLighting(world, materials.Palette);
		lighting.LoadChunk(diagonal, skyExposedAbove: false);
		Drain(lighting);
		Assert.True(lighting.TryGetChunkRevision(diagonal, out long beforeSource));

		world.SetVoxel(15, 15, 15, new VoxelCell(materials.RedLamp));
		lighting.LoadChunk(source, skyExposedAbove: false);
		Drain(lighting);

		AssertBlock(lighting.GetLight(15, 15, 15), 15, 0, 0);
		Assert.True(lighting.TryGetChunkRevision(diagonal, out long afterSource));
		Assert.Equal(beforeSource + 1, afterSource);
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
		{
			world.SetVoxel(8, y, 8, VoxelCell.Air);
		}

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
		{
			world.SetVoxel(8, y, 8, VoxelCell.Air);
		}

		for (int y = 0; y <= 36; y++)
		{
			world.SetVoxel(8, y, 8, new VoxelCell(materials.Filter));
		}

		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(lower, skyExposedAbove: false);
		lighting.LoadChunk(middle, skyExposedAbove: false);
		lighting.LoadChunk(upper, skyExposedAbove: true);
		Drain(lighting);

		for (int y = 37; y < VoxelWorld.ChunkSize * 3; y++)
		{
			Assert.Equal((byte)15, lighting.GetLight(8, y, 8).Sky);
		}

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
}
