using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using FishGfx.Graphics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest
{
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
		internal static readonly VoxelTextureRegion BarrelRegion = new VoxelTextureRegion(8, 72, 64, 64, AtlasSize, AtlasSize);
		internal static readonly VoxelTextureRegion CampfireRegion = new VoxelTextureRegion(88, 72, 64, 64, AtlasSize, AtlasSize);
		internal static readonly VoxelTextureRegion TorchRegion = new VoxelTextureRegion(168, 72, 16, 16, AtlasSize, AtlasSize);
		internal static readonly VoxelTextureRegion FoliageRegion = new VoxelTextureRegion(200, 72, 16, 16, AtlasSize, AtlasSize);

		internal static VoxelAtlasLayout AtlasLayout => new VoxelAtlasLayout(
			CubeColumns,
			CubeRows,
			AtlasSize,
			AtlasSize
		);

		internal static VoxelTestModelAssets LoadModels()
		{
			VoxelModel barrel = LoadModel("barrel", "barrel.json", BarrelRegion);
			VoxelModel campfire = LoadModel("campfire", "campfire.json", CampfireRegion);
			VoxelModel torch = LoadModel("torch", "torch.json", TorchRegion);
			VoxelModel[] foliage =
			{
				LoadModel("grass", "grass1.json", FoliageRegion),
				LoadModel("grass", "grass2.json", FoliageRegion),
				LoadModel("grass", "grass3.json", FoliageRegion),
			};

			return new VoxelTestModelAssets
			{
				Barrel = barrel,
				Campfire = campfire,
				Torch = torch,
				Foliage = new VoxelModelSet(foliage),
			};
		}

		internal static Texture CreateTexture()
		{
			using Bitmap composite = CreateBitmap();
			Texture texture = Texture.FromImage(composite);
			texture.SetFilter(TextureFilter.Nearest);
			texture.SetWrap(TextureWrap.ClampToEdge);

			return texture;
		}

		internal static Bitmap CreateBitmap()
		{
			Bitmap result = new Bitmap(AssetPath("atlas.png"));

			using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(result))
			{
				graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
				DrawAsset(graphics, ModelAssetPath("barrel", "barrel_tex.png"), BarrelRegion.X, BarrelRegion.Y, padded: true);
				DrawAsset(graphics, ModelAssetPath("campfire", "campfire_tex.png"), CampfireRegion.X, CampfireRegion.Y, padded: true);
				DrawAsset(graphics, ModelAssetPath("torch", "torch_tex.png"), TorchRegion.X, TorchRegion.Y, padded: true);
				DrawAsset(graphics, ModelAssetPath("grass", "grass1_tex.png"), FoliageRegion.X, FoliageRegion.Y, padded: true);
			}

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

		private static VoxelModel LoadModel(string directory, string fileName, VoxelTextureRegion region)
		{
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

		private static void DrawAsset(System.Drawing.Graphics graphics, string path, int x, int y, bool padded)
		{
			using Bitmap bitmap = new Bitmap(path);
			graphics.DrawImageUnscaled(bitmap, x, y);

			if (!padded)
				return;

			graphics.DrawImage(bitmap, new Rectangle(x - 1, y, 1, bitmap.Height), 0, 0, 1, bitmap.Height, GraphicsUnit.Pixel);
			graphics.DrawImage(bitmap, new Rectangle(x + bitmap.Width, y, 1, bitmap.Height), bitmap.Width - 1, 0, 1, bitmap.Height, GraphicsUnit.Pixel);
			graphics.DrawImage(bitmap, new Rectangle(x, y - 1, bitmap.Width, 1), 0, 0, bitmap.Width, 1, GraphicsUnit.Pixel);
			graphics.DrawImage(bitmap, new Rectangle(x, y + bitmap.Height, bitmap.Width, 1), 0, bitmap.Height - 1, bitmap.Width, 1, GraphicsUnit.Pixel);
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
}
