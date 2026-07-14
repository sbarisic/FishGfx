using System;
using System.Runtime.InteropServices;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelLightingTests
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
}
