using System.Collections.Generic;
using FishGfx.VoxelTest;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public class VoxelTestWorldTests
{
	[Fact]
	public void GeneratedWorldHasExpectedExtentAndBoundedLakes()
	{
		VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(out VoxelTestMaterialIds materials);
		VoxelTestWorldData data = VoxelTestWorldGenerator.Generate(materials);
		HashSet<(int X, int Z)> horizontalChunks = new();

		foreach (VoxelChunk chunk in data.World.LoadedChunks)
			horizontalChunks.Add((chunk.Coordinate.X, chunk.Coordinate.Z));

		Assert.Equal(64, horizontalChunks.Count);

		for (int z = -4; z <= 3; z++)
			for (int x = -4; x <= 3; x++)
				Assert.Contains((x, z), horizontalChunks);

		Assert.True(data.LakeCount >= 2);
		Assert.True(data.WaterColumnCount >= VoxelTestWorldGenerator.MinimumLakeArea * 2);
		VoxelTestWorldGenerator.ValidateWaterContainment(data, materials.Water);

		HashSet<(int X, int Z)> waterChunks = new();

		for (int z = VoxelTestWorldGenerator.WorldMinimum; z < VoxelTestWorldGenerator.WorldMaximum; z++)
			for (int x = VoxelTestWorldGenerator.WorldMinimum; x < VoxelTestWorldGenerator.WorldMaximum; x++)
			{
				int? waterSurface = data.GetWaterSurface(x, z);

				if (!waterSurface.HasValue)
					continue;

				ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, waterSurface.Value, z, out _, out _, out _);
				waterChunks.Add((coordinate.X, coordinate.Z));
				Assert.Equal(materials.Dirt, data.World.GetVoxel(x, data.GetSurfaceHeight(x, z), z).MaterialId);
			}

		Assert.True(waterChunks.Count > 1);

		foreach ((int x, _, int z) in data.TreeBases)
			Assert.Null(data.GetWaterSurface(x, z));

		Assert.NotNull(palette);
	}

	[Fact]
	public void GeneratedHeightFieldAndLakeAnalysisAreDeterministic()
	{
		int[,] firstHeights = VoxelTestWorldGenerator.CreateHeightField();
		int[,] secondHeights = VoxelTestWorldGenerator.CreateHeightField();
		VoxelLakeMap firstLakes = VoxelLakeAnalyzer.FindEnclosedBasins(
			firstHeights,
			VoxelTestWorldGenerator.MinimumLakeArea
		);
		VoxelLakeMap secondLakes = VoxelLakeAnalyzer.FindEnclosedBasins(
			secondHeights,
			VoxelTestWorldGenerator.MinimumLakeArea
		);

		Assert.Equal(firstLakes.BasinCount, secondLakes.BasinCount);
		Assert.Equal(firstLakes.WaterColumnCount, secondLakes.WaterColumnCount);

		for (int z = 0; z < VoxelTestWorldGenerator.WorldSize; z++)
			for (int x = 0; x < VoxelTestWorldGenerator.WorldSize; x++)
			{
				Assert.Equal(firstHeights[x, z], secondHeights[x, z]);
				Assert.Equal(firstLakes.GetWaterSurface(x, z), secondLakes.GetWaterSurface(x, z));
			}
	}
}
