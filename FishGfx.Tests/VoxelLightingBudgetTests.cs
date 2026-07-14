using System;
using System.Runtime.InteropServices;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelLightingTests
{
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
			{
				Assert.False(lighting.TryGetChunkRevision(chunk, out _));
			}
		}

		Assert.True(lighting.IsIdle);
		Assert.InRange(updates, 1, VoxelWorld.ChunkVolume / 2);
		Assert.Equal(0, lighting.PendingCount);
		Assert.True(lighting.TryGetChunkRevision(chunk, out long revision));
		Assert.Equal(1, revision);
	}

	[Fact]
	public void InitialSkyLitAirChunkAvoidsRedundantRelaxationPasses()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new ChunkCoordinate(0, 0, 0);
		using VoxelLighting lighting = new VoxelLighting(world, materials.Palette);
		lighting.LoadChunk(chunk, skyExposedAbove: true);

		int processed = Drain(lighting);

		Assert.InRange(processed, 1, 4 * VoxelWorld.ChunkVolume);
		Assert.Equal((byte)15, lighting.GetLight(8, 8, 8).Sky);
	}

	[Fact]
	public void InitialOpaqueChunkSkipsZeroSkylightTail()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		ChunkCoordinate chunk = new ChunkCoordinate(0, 0, 0);
		world.FillChunk(chunk, new VoxelCell(materials.Stone));
		using VoxelLighting lighting = new VoxelLighting(world, materials.Palette);
		lighting.LoadChunk(chunk, skyExposedAbove: false);

		int processed = Drain(lighting);

		Assert.InRange(processed, 1, 2 * VoxelWorld.ChunkVolume);
		Assert.Equal(default, lighting.GetLight(8, 8, 8));
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
		Assert.Equal(2 * VoxelWorld.ChunkVolume + 1, updates);
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

		Assert.InRange(updates, 1, VoxelWorld.ChunkVolume / 2);
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
		VoxelCell[] firstCells = new VoxelCell[VoxelWorld.ChunkVolume];
		for (int index = 0; index < firstCells.Length; index++)
		{
			firstCells[index] = new VoxelCell(
				index % 2 == 0 ? materials.Stone : materials.StoneVariant
			);
		}
		world.SetChunk(first, firstCells);
		using VoxelLighting lighting = new(world, materials.Palette);
		lighting.LoadChunk(first, skyExposedAbove: false);

		const int budget = 64;
		int totalProcessed = lighting.Update(budget);
		Assert.False(lighting.TryGetChunkRevision(first, out _));

		world.SetChunk(second, firstCells);
		lighting.LoadChunk(second, skyExposedAbove: false);
		const int initialPublicationWork = VoxelWorld.ChunkVolume + 1;
		for (int update = 0; update < initialPublicationWork / budget + 1; update++)
		{
			if (lighting.TryGetChunkRevision(first, out _))
			{
				break;
			}

			totalProcessed += lighting.Update(budget);
		}

		Assert.True(lighting.TryGetChunkRevision(first, out _));
		Assert.InRange(totalProcessed, 1, initialPublicationWork + budget - 1);
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
		{
			lighting.LoadChunk(coordinate, skyExposedAbove: false);
		}

		Drain(lighting);

		const int budget = 64;
		lighting.LoadChunk(target, skyExposedAbove: false);
		int processed = lighting.Update(budget);
		foreach (ChunkCoordinate coordinate in external)
		{
			Assert.True(lighting.UnloadChunk(coordinate));
			processed += lighting.Update(budget);
		}

		const int initialPublicationWork = VoxelWorld.ChunkVolume / 2;
		for (int update = 0; update < initialPublicationWork / budget
			&& !lighting.TryGetChunkRevision(target, out _); update++)
		{
			processed += lighting.Update(budget);
		}

		Assert.True(lighting.TryGetChunkRevision(target, out _));
		Assert.InRange(processed, 1, initialPublicationWork);
		Drain(lighting, budget);
	}

	[Fact]
	public void StreamedTerrainLightingStaysBelowInitialWorkCeiling()
	{
		TestPalette materials = new();
		VoxelWorld world = new();
		using VoxelLighting lighting = new(world, materials.Palette);
		VoxelCell stone = new VoxelCell(materials.Stone);
		const int side = 5;
		const int verticalChunks = 3;
		const int columnsPerFrame = 4;
		const int budget = 65_536;
		int nextColumn = 0;
		int processed = 0;
		int frames = 0;

		while (nextColumn < side * side || !lighting.IsIdle)
		{
			int frameEnd = Math.Min(side * side, nextColumn + columnsPerFrame);
			while (nextColumn < frameEnd)
			{
				int chunkX = nextColumn % side;
				int chunkZ = nextColumn / side;
				for (int chunkY = 0; chunkY < verticalChunks; chunkY++)
				{
					ChunkCoordinate coordinate = new ChunkCoordinate(chunkX, chunkY, chunkZ);
					lighting.LoadChunk(
						coordinate,
						skyExposedAbove: chunkY == verticalChunks - 1
					);
					if (chunkY < verticalChunks - 1)
					{
						world.FillChunk(coordinate, stone);
					}
				}

				nextColumn++;
			}

			processed += lighting.Update(budget);
			frames++;
		}

		Assert.InRange(processed, 1, 650_000);
		Assert.InRange(frames, 1, 10);
	}
}
