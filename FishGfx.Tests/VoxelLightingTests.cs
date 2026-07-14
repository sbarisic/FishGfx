using System;
using System.Runtime.InteropServices;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelLightingTests
{
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
		{
			processed += lighting.Update(budget);
		}

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
		{
			world.SetVoxel(x, y, z, VoxelCell.Air);
		}
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
