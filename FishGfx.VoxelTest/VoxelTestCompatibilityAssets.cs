using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using FishGfx.Graphics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal readonly struct VoxelTestMaterialEntry
{
	internal VoxelTestMaterialEntry(ushort id, string name)
	{
		Id = id;
		Name = name;
	}

	internal ushort Id { get; }
	internal string Name { get; }
}

internal sealed class VoxelTestModelAssets
{
	internal VoxelModel Barrel { get; init; }
	internal VoxelModel Campfire { get; init; }
	internal VoxelModel Torch { get; init; }
	internal VoxelModelSet Foliage { get; init; }
}

internal static class VoxelTestCompatibilityAssets
{
	internal const int AtlasSize = 512;
	internal const int CubeColumns = 16;
	internal const int CubeRows = 16;
	internal static readonly VoxelTextureRegion BarrelRegion =
		new VoxelTextureRegion(8, 72, 64, 64, AtlasSize, AtlasSize);
	internal static readonly VoxelTextureRegion CampfireRegion =
		new VoxelTextureRegion(88, 72, 64, 64, AtlasSize, AtlasSize);
	internal static readonly VoxelTextureRegion TorchRegion =
		new VoxelTextureRegion(168, 72, 16, 16, AtlasSize, AtlasSize);
	internal static readonly VoxelTextureRegion FoliageRegion =
		new VoxelTextureRegion(200, 72, 16, 16, AtlasSize, AtlasSize);

	internal static VoxelAtlasLayout AtlasLayout => new VoxelAtlasLayout(
		CubeColumns,
		CubeRows,
		AtlasSize,
		AtlasSize
	);

	internal static VoxelTestModelAssets LoadModels()
	{
		VoxelModel barrel = LoadModel("barrel", "barrel.json", "barrel_tex.png", BarrelRegion);
		VoxelModel campfire = LoadModel("campfire", "campfire.json", "campfire_tex.png", CampfireRegion);
		VoxelModel torch = LoadModel("torch", "torch.json", "torch_tex.png", TorchRegion);
		VoxelModel[] foliage =
		{
			LoadModel("grass", "grass1.json", "grass1_tex.png", FoliageRegion),
			LoadModel("grass", "grass2.json", "grass1_tex.png", FoliageRegion),
			LoadModel("grass", "grass3.json", "grass1_tex.png", FoliageRegion),
		};

		return new VoxelTestModelAssets
		{
			Barrel = barrel,
			Campfire = campfire,
			Torch = torch,
			Foliage = new VoxelModelSet(foliage),
		};
	}

	internal static Texture CreateTexture(GraphicsContext graphics)
	{
		if (graphics == null)
		{
			throw new ArgumentNullException(nameof(graphics));
		}

		using Bitmap composite = CreateBitmap();
		return graphics.CreateTextureFromImage(composite);
	}

	internal static Bitmap CreateBitmap()
	{
		Bitmap result = new Bitmap(AssetPath("atlas.png"));
		DrawAsset(result, ModelAssetPath("barrel", "barrel_tex.png"), BarrelRegion.X, BarrelRegion.Y, padded: true);
		DrawAsset(result, ModelAssetPath("campfire", "campfire_tex.png"), CampfireRegion.X, CampfireRegion.Y, padded: true);
		DrawAsset(result, ModelAssetPath("torch", "torch_tex.png"), TorchRegion.X, TorchRegion.Y, padded: true);
		DrawAsset(result, ModelAssetPath("grass", "grass1_tex.png"), FoliageRegion.X, FoliageRegion.Y, padded: true);

		return result;
	}

	internal static string AssetPath(params string[] parts)
	{
		string[] path = new string[parts.Length + 5];
		path[0] = AppContext.BaseDirectory;
		path[1] = "data";
		path[2] = "textures";
		path[3] = "voxels";
		path[4] = "raylibgame";
		Array.Copy(parts, 0, path, 5, parts.Length);

		return Path.Combine(path);
	}

	private static VoxelModel LoadModel(
		string directory,
		string fileName,
		string textureFileName,
		VoxelTextureRegion region
	)
	{
		using Bitmap texture = new Bitmap(ModelAssetPath(directory, textureFileName));

		if (texture.Width != region.Width || texture.Height != region.Height)
		{
			throw new InvalidDataException(
				$"Model texture '{textureFileName}' is {texture.Width}x{texture.Height}, "
				+ $"but its atlas region is {region.Width}x{region.Height}."
			);
		}

		Dictionary<string, VoxelTextureRegion> regions = new Dictionary<string, VoxelTextureRegion>
		{
			["0"] = region,
		};

		return MinecraftVoxelModelLoader.LoadFile(ModelAssetPath(directory, fileName), regions);
	}

	private static string ModelAssetPath(string directory, string fileName)
	{
		return AssetPath("models", directory, fileName);
	}

	private static void DrawAsset(Bitmap destination, string path, int x, int y, bool padded)
	{
		using Bitmap bitmap = new Bitmap(path);

		for (int sourceY = 0; sourceY < bitmap.Height; sourceY++)
		{
			for (int sourceX = 0; sourceX < bitmap.Width; sourceX++)
			{
				destination.SetPixel(x + sourceX, y + sourceY, bitmap.GetPixel(sourceX, sourceY));
			}
		}

		if (!padded)
		{
			return;
		}

		for (int sourceY = 0; sourceY < bitmap.Height; sourceY++)
		{
			destination.SetPixel(x - 1, y + sourceY, bitmap.GetPixel(0, sourceY));
			destination.SetPixel(x + bitmap.Width, y + sourceY, bitmap.GetPixel(bitmap.Width - 1, sourceY));
		}

		for (int sourceX = 0; sourceX < bitmap.Width; sourceX++)
		{
			destination.SetPixel(x + sourceX, y - 1, bitmap.GetPixel(sourceX, 0));
			destination.SetPixel(x + sourceX, y + bitmap.Height, bitmap.GetPixel(sourceX, bitmap.Height - 1));
		}

		destination.SetPixel(x - 1, y - 1, bitmap.GetPixel(0, 0));
		destination.SetPixel(x + bitmap.Width, y - 1, bitmap.GetPixel(bitmap.Width - 1, 0));
		destination.SetPixel(x - 1, y + bitmap.Height, bitmap.GetPixel(0, bitmap.Height - 1));
		destination.SetPixel(
			x + bitmap.Width,
			y + bitmap.Height,
			bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1)
		);
	}
}

internal sealed class VoxelTestMaterialIds
{
	private readonly List<VoxelTestMaterialEntry> placeable = new List<VoxelTestMaterialEntry>();
	private readonly ReadOnlyCollection<VoxelTestMaterialEntry> readOnlyPlaceable;

	internal VoxelTestMaterialIds()
	{
		readOnlyPlaceable = placeable.AsReadOnly();
	}

	internal ushort Stone;
	internal ushort Dirt;
	internal ushort StoneBrick;
	internal ushort Sand;
	internal ushort Bricks;
	internal ushort Plank;
	internal ushort EndStoneBrick;
	internal ushort Ice;
	internal ushort Test;
	internal ushort Leaves;
	internal ushort Water;
	internal ushort Glass;
	internal ushort Glowstone;
	internal ushort Test2;
	internal ushort Grass;
	internal ushort Wood;
	internal ushort CraftingTable;
	internal ushort Barrel;
	internal ushort Campfire;
	internal ushort Torch;
	internal ushort Foliage;
	internal ushort Gravel;

	internal IReadOnlyList<VoxelTestMaterialEntry> Placeable => readOnlyPlaceable;

	internal void Add(ushort id, string name)
	{
		placeable.Add(new VoxelTestMaterialEntry(id, name));
	}
}
