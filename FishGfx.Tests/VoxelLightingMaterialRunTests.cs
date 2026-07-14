using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelLightingTests
{
	[Fact]
	public void UniformAndLayeredOpaqueChunksConsumeRunBasedPreparationWork()
	{
		TestPalette materials = new TestPalette();
		ChunkCoordinate coordinate = new ChunkCoordinate(0, 0, 0);

		VoxelWorld uniformWorld = new VoxelWorld();
		uniformWorld.FillChunk(coordinate, new VoxelCell(materials.Stone));
		using (VoxelLighting lighting = CreateLighting(
			uniformWorld,
			materials.Palette,
			coordinate
		))
		{
			Assert.Equal(2, Drain(lighting, budget: 1));
		}

		VoxelCell[] layered = new VoxelCell[VoxelWorld.ChunkVolume];
		for (int index = 0; index < layered.Length; index++)
		{
			layered[index] = new VoxelCell(
				index < layered.Length / 2
					? materials.Stone
					: materials.StoneVariant
			);
		}

		VoxelWorld layeredWorld = new VoxelWorld();
		layeredWorld.SetChunk(coordinate, layered);
		using VoxelLighting layeredLighting = CreateLighting(
			layeredWorld,
			materials.Palette,
			coordinate
		);
		Assert.Equal(3, Drain(layeredLighting, budget: 1));
	}

	[Fact]
	public void HighEntropyAndEmittingChunksRetainPerCellBudgeting()
	{
		TestPalette materials = new TestPalette();
		ChunkCoordinate coordinate = new ChunkCoordinate(0, 0, 0);
		VoxelCell[] noisy = new VoxelCell[VoxelWorld.ChunkVolume];
		for (int index = 0; index < noisy.Length; index++)
		{
			noisy[index] = new VoxelCell(
				index % 2 == 0 ? materials.Stone : materials.StoneVariant
			);
		}

		VoxelWorld noisyWorld = new VoxelWorld();
		noisyWorld.SetChunk(coordinate, noisy);
		using (VoxelLighting lighting = CreateLighting(
			noisyWorld,
			materials.Palette,
			coordinate
		))
		{
			Assert.Equal(VoxelWorld.ChunkVolume + 1, Drain(lighting, budget: 64));
		}

		VoxelWorld emittingWorld = new VoxelWorld();
		emittingWorld.FillChunk(coordinate, new VoxelCell(materials.RedLamp));
		using VoxelLighting emitting = CreateLighting(
			emittingWorld,
			materials.Palette,
			coordinate
		);
		Assert.Equal(64, emitting.Update(64));
		Assert.False(emitting.TryGetChunkRevision(coordinate, out _));
		Drain(emitting, budget: 64);
		AssertBlock(emitting.GetLight(8, 8, 8), 15, 0, 0);
	}

	[Fact]
	public void EditingUniformChunkClonesSharedMaterialSignatures()
	{
		TestPalette materials = new TestPalette();
		VoxelWorld world = new VoxelWorld();
		ChunkCoordinate first = new ChunkCoordinate(0, 0, 0);
		ChunkCoordinate second = new ChunkCoordinate(2, 0, 0);
		world.FillChunk(first, new VoxelCell(materials.Stone));
		world.FillChunk(second, new VoxelCell(materials.Stone));
		using VoxelLighting lighting = new VoxelLighting(world, materials.Palette);
		lighting.LoadChunk(first, skyExposedAbove: false);
		lighting.LoadChunk(second, skyExposedAbove: false);
		Drain(lighting);

		world.SetVoxel(8, 8, 8, new VoxelCell(materials.RedLamp));
		Drain(lighting, budget: 32);

		AssertBlock(lighting.GetLight(8, 8, 8), 15, 0, 0);
		AssertBlock(lighting.GetLight(40, 8, 8), 0, 0, 0);
	}
}
