using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal static class VoxelTestWorldGenerator
{
	internal const int OrientationShowcaseCount = 7;
	internal const int WorldMinimum = -640;
	internal const int WorldMaximum = 640;
	internal const int WorldSize = WorldMaximum - WorldMinimum;
	internal const int TerrainBottom = -80;
	internal const int WorldMinimumY = -80;
	internal const int WorldMaximumY = 192;
	internal const int MinimumChunkCoordinate = WorldMinimum / VoxelWorld.ChunkSize;
	internal const int MaximumChunkCoordinate = WorldMaximum / VoxelWorld.ChunkSize - 1;
	internal const int MinimumSurfaceHeight = -10;
	internal const int MaximumSurfaceHeight = 160;
	internal const int MinimumLakeArea = 24;
	internal const int BoundaryEditX = 15;
	internal const int BoundaryEditY = 180;
	internal const int BoundaryEditZ = 0;
	internal const int GlassMinimumX = 36;
	internal const int GlassMaximumX = 39;
	internal const int GlassZ = -12;
	internal const int GlassHeight = 20;

	internal static ushort GetOrientationShowcaseMaterial(VoxelTestMaterialIds materials, int index)
	{
		if (materials == null)
		{
			throw new ArgumentNullException(nameof(materials));
		}

		return index switch
		{
			0 => materials.Grass,
			1 => materials.Wood,
			2 => materials.CraftingTable,
			3 => materials.Barrel,
			4 => materials.Campfire,
			5 => materials.Torch,
			6 => materials.Foliage,
			_ => throw new ArgumentOutOfRangeException(nameof(index)),
		};
	}

	internal static VoxelPalette CreatePalette(VoxelTestModelAssets models, out VoxelTestMaterialIds ids)
	{
		if (models == null)
		{
			throw new ArgumentNullException(nameof(models));
		}

		VoxelPaletteBuilder builder = new VoxelPaletteBuilder();
		ids = new VoxelTestMaterialIds();
		ids.Stone = Add(builder, ids, "Stone", Opaque("Stone", 0));
		ids.Dirt = Add(builder, ids, "Dirt", Opaque("Dirt", 1));
		ids.StoneBrick = Add(builder, ids, "Stone Brick", Opaque("Stone Brick", 2));
		ids.Sand = Add(builder, ids, "Sand", Opaque("Sand", 3));
		ids.Bricks = Add(builder, ids, "Bricks", Opaque("Bricks", 4));
		ids.Plank = Add(builder, ids, "Plank", Opaque("Plank", 5));
		ids.EndStoneBrick = Add(builder, ids, "End Stone Brick", Opaque("End Stone Brick", 6));
		ids.Ice = Add(
			builder,
			ids,
			"Ice",
			new VoxelMaterial(
				"Ice",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(7),
				occludesFaces: false,
				doubleSided: true,
				light: new VoxelMaterialLightSettings(1)
			)
		);
		ids.Test = Add(builder, ids, "Test", Opaque("Test", 8));
		ids.Leaves = Add(
			builder,
			ids,
			"Leaf",
			new VoxelMaterial(
				"Leaf",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(9),
				occludesFaces: false,
				light: new VoxelMaterialLightSettings(1)
			)
		);
		ids.Water = Add(
			builder,
			ids,
			"Water",
			new VoxelMaterial(
				"Water",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(10),
				occludesFaces: false,
				doubleSided: true,
				wave: new VoxelWaveSettings(amplitude: 0.1f, wavelength: 6, speed: 0.2f),
				light: new VoxelMaterialLightSettings(1)
			)
		);
		ids.Glass = Add(
			builder,
			ids,
			"Glass",
			new VoxelMaterial(
				"Glass",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(11),
				occludesFaces: false,
				doubleSided: true,
				light: new VoxelMaterialLightSettings(0)
			)
		);
		ids.Glowstone = Add(
			builder,
			ids,
			"Glowstone",
			Opaque(
				"Glowstone",
				12,
				new VoxelMaterialLightSettings(15, new VoxelBlockLight(15, 12, 8))
			)
		);
		ids.Test2 = Add(builder, ids, "Test 2", Opaque("Test 2", 13));
		ids.Grass = Add(
			builder,
			ids,
			"Grass",
			new VoxelMaterial(
				"Grass",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(241, 241, 240, 1, 241, 241)
			)
		);
		ids.Wood = Add(
			builder,
			ids,
			"Wood",
			new VoxelMaterial(
				"Wood",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(242, 242, 243, 243, 242, 242)
			)
		);
		ids.CraftingTable = Add(
			builder,
			ids,
			"Crafting Table",
			new VoxelMaterial(
				"Crafting Table",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(245, 245, 244, 247, 246, 246)
			)
		);
		ids.Barrel = Add(builder, ids, "Barrel", Custom("Barrel", VoxelRenderMode.Opaque, models.Barrel, true));
		ids.Campfire = Add(
			builder,
			ids,
			"Campfire",
			Custom(
				"Campfire",
				VoxelRenderMode.Cutout,
				models.Campfire,
				false,
				new VoxelMaterialLightSettings(0, new VoxelBlockLight(15, 7, 2))
			)
		);
		ids.Torch = Add(
			builder,
			ids,
			"Torch",
			Custom(
				"Torch",
				VoxelRenderMode.Cutout,
				models.Torch,
				false,
				new VoxelMaterialLightSettings(0, new VoxelBlockLight(15, 10, 5))
			)
		);
		ids.Foliage = Add(
			builder,
			ids,
			"Foliage",
			new VoxelMaterial(
				"Foliage",
				VoxelRenderMode.Cutout,
				new VoxelFaceTiles(0),
				occludesFaces: false,
				models: models.Foliage,
				light: new VoxelMaterialLightSettings(1)
			)
		);
		ids.Gravel = Add(builder, ids, "Gravel", Opaque("Gravel", 21));

		return builder.Build();
	}

	private static VoxelMaterial Opaque(
		string name,
		int raylibTile,
		VoxelMaterialLightSettings? light = null
	)
	{
		return new VoxelMaterial(
			name,
			VoxelRenderMode.Opaque,
			new VoxelFaceTiles(raylibTile),
			light: light
		);
	}

	private static VoxelMaterial Custom(
		string name,
		VoxelRenderMode mode,
		VoxelModel model,
		bool occludesFaces,
		VoxelMaterialLightSettings? light = null
	)
	{
		return new VoxelMaterial(
			name,
			mode,
			new VoxelFaceTiles(0),
			occludesFaces: occludesFaces,
			models: new VoxelModelSet(model),
			light: light
		);
	}

	private static ushort Add(
		VoxelPaletteBuilder builder,
		VoxelTestMaterialIds ids,
		string name,
		VoxelMaterial material
	)
	{
		ushort id = builder.Add(material);
		ids.Add(id, name);
		return id;
	}

	internal static VoxelTestWorldData Generate(VoxelTestMaterialIds materials)
	{
		_ = materials;
		int[,] heights = CreateHeightField();
		VoxelLakeMap lakes = VoxelLakeAnalyzer.FindEnclosedBasins(heights, MinimumLakeArea);

		if (lakes.BasinCount < 2)
		{
			throw new InvalidOperationException("The deterministic validation terrain must produce at least two lakes.");
		}

		int[,] waterSurfaces = new int[WorldSize, WorldSize];

		for (int z = 0; z < WorldSize; z++)
		{
			for (int x = 0; x < WorldSize; x++)
			{
				waterSurfaces[x, z] = lakes.GetWaterSurface(x, z) ?? int.MinValue;
			}
		}

		VoxelTestWorldData result = new VoxelTestWorldData(
			heights,
			waterSurfaces,
			lakes.BasinCount,
			lakes.WaterColumnCount
		);
		ValidateWaterContainment(result);

		return result;
	}

	internal static int[,] CreateHeightField()
	{
		int[,] heights = new int[WorldSize, WorldSize];

		for (int z = 0; z < WorldSize; z++)
		{
			for (int x = 0; x < WorldSize; x++)
			{
				int worldX = x + WorldMinimum;
				int worldZ = z + WorldMinimum;
				float broad = MathF.Sin(worldX * 0.012f) * 48
					+ MathF.Cos(worldZ * 0.0105f) * 39
					+ MathF.Sin((worldX + worldZ) * 0.0065f) * 28;
				float detail = MathF.Sin(worldX * 0.038f + worldZ * 0.024f) * 12
					+ MathF.Cos(worldX * 0.021f - worldZ * 0.033f) * 9;
				float depressions = Depression(worldX, worldZ, -120, -90, 88, 62)
					+ Depression(worldX, worldZ, 145, 110, 76, 55);
				heights[x, z] = Math.Clamp(
					55 + (int)MathF.Round(broad + detail - depressions),
					MinimumSurfaceHeight,
					MaximumSurfaceHeight
				);
			}
		}

		return heights;
	}

	internal static void ValidateWaterContainment(VoxelTestWorldData data)
	{
		if (data == null)
		{
			throw new ArgumentNullException(nameof(data));
		}

		for (int z = WorldMinimum; z < WorldMaximum; z++)
		{
			for (int x = WorldMinimum; x < WorldMaximum; x++)
			{
				int? waterSurface = data.GetWaterSurface(x, z);

				if (!waterSurface.HasValue)
				{
					continue;
				}

				if (x == WorldMinimum || x == WorldMaximum - 1 || z == WorldMinimum || z == WorldMaximum - 1)
				{
					throw new InvalidOperationException("Lake water cannot occupy a world-boundary column.");
				}

				int terrainSurface = data.GetSurfaceHeight(x, z);

				for (int y = terrainSurface + 1; y <= waterSurface.Value; y++)
				{
					ValidateHorizontalNeighbor(x - 1, y, z);
					ValidateHorizontalNeighbor(x + 1, y, z);
					ValidateHorizontalNeighbor(x, y, z - 1);
					ValidateHorizontalNeighbor(x, y, z + 1);
				}
			}
		}

		void ValidateHorizontalNeighbor(int x, int y, int z)
		{
			int terrainSurface = data.GetSurfaceHeight(x, z);
			int? waterSurface = data.GetWaterSurface(x, z);

			if (terrainSurface < y && (!waterSurface.HasValue || waterSurface.Value < y))
			{
				throw new InvalidOperationException("Lake water has a horizontally exposed side.");
			}
		}
	}

	private static float Depression(
		int x,
		int z,
		float centerX,
		float centerZ,
		float radius,
		float depth
	)
	{
		float offsetX = x - centerX;
		float offsetZ = z - centerZ;
		float normalizedDistanceSquared = (offsetX * offsetX + offsetZ * offsetZ) / (radius * radius);

		if (normalizedDistanceSquared >= 1)
		{
			return 0;
		}

		float influence = 1 - normalizedDistanceSquared;
		return depth * influence * influence;
	}
}
