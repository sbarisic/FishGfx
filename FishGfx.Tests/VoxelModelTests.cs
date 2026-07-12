using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using FishGfx.Voxels;
using FishGfx.VoxelTest;
using Xunit;

namespace FishGfx.Tests;

public class VoxelModelTests
{
	private const string OneFaceJson = """
		{
		  "elements": [
		    {
		      "from": [0, 0, 0],
		      "to": [16, 16, 16],
		      "faces": {
		        "north": { "uv": [0, 0, 16, 16], "texture": "#0" }
		      }
		    }
		  ]
		}
		""";

	[Fact]
	public void TextureRegionMapsTopDownPixelsIntoBottomUpAtlasUvs()
	{
		VoxelTextureRegion region = new(512, 0, 64, 64, 1024, 1024);

		Assert.Equal(new Vector2(0.5f, 1), region.Map(Vector2.Zero));
		Assert.Equal(new Vector2(0.5625f, 0.9375f), region.Map(Vector2.One));
	}

	[Fact]
	public void ModelSnapshotsVerticesAndNormalizesNormals()
	{
		VoxelVertex[] vertices = CreateTriangle(VoxelRenderMode.Opaque);
		vertices[0].Normal = Vector3.UnitY * 4;
		VoxelModel model = new(vertices);
		vertices[0].Position = new Vector3(100);

		Assert.Equal(Vector3.Zero, model.Vertices[0].Position);
		Assert.Equal(Vector3.UnitY, model.Vertices[0].Normal);
		Assert.Throws<ArgumentException>(() => new VoxelModel(vertices.Take(2)));
	}

	[Fact]
	public void ModelSetSelectionIsDeterministicForNegativeCoordinates()
	{
		VoxelModel first = new(CreateTriangle(VoxelRenderMode.Opaque));
		VoxelVertex[] shifted = CreateTriangle(VoxelRenderMode.Opaque);
		shifted[0].Position.X = 0.25f;
		VoxelModel second = new(shifted);
		VoxelModelSet set = new(first, second);

		Assert.Same(set.Select(-12, 4, -9), set.Select(-12, 4, -9));
		Assert.Contains(set.Select(5, 7, 11), set.Models);
	}

	[Fact]
	public void MinecraftLoaderCreatesCorrectFaceGeometryAndUvs()
	{
		VoxelTextureRegion region = new(0, 0, 16, 16, 32, 32);
		VoxelModel model = MinecraftVoxelModelLoader.Load(
			OneFaceJson,
			new Dictionary<string, VoxelTextureRegion> { ["0"] = region }
		);

		Assert.Equal(6, model.Vertices.Count);
		Assert.All(model.Vertices, vertex => Assert.Equal(-Vector3.UnitZ, vertex.Normal));
		Assert.All(model.Vertices, vertex =>
		{
			Assert.InRange(vertex.UV.X, 0, 0.5f);
			Assert.InRange(vertex.UV.Y, 0.5f, 1);
		});
	}

	[Fact]
	public void MinecraftLoaderRejectsMalformedAndUnresolvedModels()
	{
		Assert.Throws<FormatException>(() => MinecraftVoxelModelLoader.Load("{}", new Dictionary<string, VoxelTextureRegion>()));
		Assert.Throws<FormatException>(() => MinecraftVoxelModelLoader.Load(OneFaceJson, new Dictionary<string, VoxelTextureRegion>()));
		Assert.Throws<FormatException>(() => MinecraftVoxelModelLoader.Load("{", new Dictionary<string, VoxelTextureRegion>()));
	}

	[Theory]
	[InlineData(VoxelRenderMode.Opaque, 3, 0, 0)]
	[InlineData(VoxelRenderMode.Cutout, 0, 3, 0)]
	[InlineData(VoxelRenderMode.Transparent, 0, 0, 3)]
	public void CustomModelsBakeIntoTheirMaterialRenderStream(
		VoxelRenderMode mode,
		int opaqueVertices,
		int cutoutVertices,
		int transparentVertices
	)
	{
		VoxelModel model = new(CreateTriangle(mode));
		VoxelPaletteBuilder builder = new();
		ushort id = builder.Add(
			new VoxelMaterial(
				"Custom",
				mode,
				new VoxelFaceTiles(0),
				models: new VoxelModelSet(model)
			)
		);
		VoxelWorld world = new();
		world.SetVoxel(3, 4, 5, new VoxelCell(id));

		VoxelMeshData result = VoxelMesher.Build(
			world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
			builder.Build(),
			new VoxelAtlasLayout(1, 1, 16, 16)
		);

		Assert.Equal(opaqueVertices, result.OpaqueVertices.Length);
		Assert.Equal(cutoutVertices, result.CutoutVertices.Length);
		Assert.Equal(transparentVertices, result.TransparentVertexCount);
		Assert.True(result.Bounds.IsInside(new Vector3(3, 4, 5)));
	}

	[Fact]
	public void CompatibilityAssetsAndPaletteMatchRaylibGameSnapshot()
	{
		string atlasPath = VoxelTestCompatibilityAssets.AssetPath("atlas.png");
		string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(atlasPath)));
		VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(
			VoxelTestCompatibilityAssets.LoadModels(),
			out VoxelTestMaterialIds ids
		);

		Assert.Equal("C28F6E91E1B98B28FD525708EFF13C1D901A3115D062AAB1741EBDBD4EE62F86", hash);
		Assert.Equal(23, palette.Count);
		Assert.Equal(240, palette[ids.Grass].Tiles.PositiveY);
		Assert.Equal(241, palette[ids.Grass].Tiles.PositiveX);
		Assert.Equal(1, palette[ids.Grass].Tiles.NegativeY);
		Assert.Equal(242, palette[ids.Wood].Tiles.PositiveX);
		Assert.Equal(243, palette[ids.Wood].Tiles.PositiveY);
		Assert.Equal(244, palette[ids.CraftingTable].Tiles.PositiveY);
		Assert.Equal(247, palette[ids.CraftingTable].Tiles.NegativeY);
		Assert.Equal(VoxelRenderMode.Transparent, palette[ids.Water].RenderMode);
		Assert.True(palette[ids.Glass].DoubleSided);
		Assert.NotNull(palette[ids.Barrel].Models);
		Assert.Equal(3, palette[ids.Foliage].Models.Models.Count);
	}

	[Fact]
	public void CompatibilityAtlasPreservesSourceAndPacksModelTextures()
	{
		using System.Drawing.Bitmap source = new(VoxelTestCompatibilityAssets.AssetPath("atlas.png"));
		using System.Drawing.Bitmap composite = VoxelTestCompatibilityAssets.CreateBitmap();

		Assert.Equal(512, composite.Width);
		Assert.Equal(512, composite.Height);
		Assert.Equal(source.GetPixel(0, 0), composite.GetPixel(0, 0));
		Assert.Equal(source.GetPixel(511, 511), composite.GetPixel(511, 511));
		Assert.NotEqual(0, composite.GetPixel(VoxelTestCompatibilityAssets.BarrelRegion.X, VoxelTestCompatibilityAssets.BarrelRegion.Y).A);
	}

	[Fact]
	public void CompatibilityCubeUvsResolveToNativeSourceAtlasPixels()
	{
		VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(
			VoxelTestCompatibilityAssets.LoadModels(),
			out VoxelTestMaterialIds ids
		);
		using System.Drawing.Bitmap source = new(VoxelTestCompatibilityAssets.AssetPath("atlas.png"));
		using System.Drawing.Bitmap composite = VoxelTestCompatibilityAssets.CreateBitmap();
		(ushort Material, int[] Tiles)[] cases =
		{
			(ids.Stone, new[] { 0, 0, 0, 0, 0, 0 }),
			(ids.Dirt, new[] { 1, 1, 1, 1, 1, 1 }),
			(ids.Water, new[] { 10, 10, 10, 10, 10, 10 }),
			(ids.Grass, new[] { 241, 241, 240, 1, 241, 241 }),
			(ids.Wood, new[] { 242, 242, 243, 243, 242, 242 }),
			(ids.CraftingTable, new[] { 245, 245, 244, 247, 246, 246 }),
		};

		foreach ((ushort material, int[] tiles) in cases)
		{
			VoxelMeshData mesh = BuildIsolatedCompatibilityBlock(material, palette);

			for (int face = 0; face < 6; face++)
			{
				IReadOnlyList<VoxelVertex> vertices = palette[material].RenderMode == VoxelRenderMode.Transparent
					? mesh.TransparentFaces[face].Vertices
					: mesh.OpaqueVertices.Skip(face * 6).Take(6).ToArray();
				Vector2 uv = Vector2.Zero;

				for (int i = 0; i < vertices.Count; i++)
					uv += vertices[i].UV;

				uv /= vertices.Count;
				System.Drawing.Color actual = SampleUv(composite, uv);
				int tile = tiles[face];
				AssertUvInsideTile(uv, tile);
				System.Drawing.Color expected = SampleUv(source, uv);
				Assert.Equal(expected, actual);
			}
		}
	}

	[Fact]
	public void TerrainGrassMeshUsesNativeGrassPixelsInsteadOfTestArrow()
	{
		VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(
			VoxelTestCompatibilityAssets.LoadModels(),
			out VoxelTestMaterialIds ids
		);
		VoxelTestWorldData data = VoxelTestWorldGenerator.Generate(ids);
		int x = VoxelTestWorldGenerator.WorldMinimum + 1;
		int z = VoxelTestWorldGenerator.WorldMinimum + 1;

		while (data.GetWaterSurface(x, z).HasValue)
			x++;

		int surface = data.GetSurfaceHeight(x, z);
		ushort generatedMaterial = data.GetTerrainMaterial(x, surface, z, ids);
		VoxelMeshData mesh = BuildIsolatedCompatibilityBlock(generatedMaterial, palette);
		Vector2 topUv = mesh.OpaqueVertices.Skip(12).Take(6).Aggregate(Vector2.Zero, (sum, vertex) => sum + vertex.UV) / 6;
		using System.Drawing.Bitmap composite = VoxelTestCompatibilityAssets.CreateBitmap();
		using System.Drawing.Bitmap source = new(VoxelTestCompatibilityAssets.AssetPath("atlas.png"));

		Assert.Equal(ids.Grass, generatedMaterial);
		AssertUvInsideTile(topUv, 240);
		Assert.Equal(SampleUv(source, topUv), SampleUv(composite, topUv));
		Assert.InRange(topUv.Y, 0, 1 / 16f);
	}

	[Fact]
	public void CustomModelUvsStayInsidePaddedNativeAtlasRegions()
	{
		VoxelTestModelAssets models = VoxelTestCompatibilityAssets.LoadModels();
		AssertModelInside(models.Barrel, VoxelTestCompatibilityAssets.BarrelRegion);
		AssertModelInside(models.Campfire, VoxelTestCompatibilityAssets.CampfireRegion);
		AssertModelInside(models.Torch, VoxelTestCompatibilityAssets.TorchRegion);

		foreach (VoxelModel foliage in models.Foliage.Models)
			AssertModelInside(foliage, VoxelTestCompatibilityAssets.FoliageRegion);

		using System.Drawing.Bitmap atlas = VoxelTestCompatibilityAssets.CreateBitmap();
		VoxelTextureRegion region = VoxelTestCompatibilityAssets.BarrelRegion;
		Assert.Equal(atlas.GetPixel(region.X, region.Y + region.Height / 2), atlas.GetPixel(region.X - 1, region.Y + region.Height / 2));
		Assert.Equal(atlas.GetPixel(region.X + region.Width - 1, region.Y + region.Height / 2), atlas.GetPixel(region.X + region.Width, region.Y + region.Height / 2));
	}

	[Fact]
	public void HotbarWrapsAndSelectsVisibleSlots()
	{
		VoxelTestMaterialEntry[] entries = Enumerable.Range(1, 12)
			.Select(index => new VoxelTestMaterialEntry((ushort)index, index.ToString()))
			.ToArray();
		VoxelHotbarSelection hotbar = new(entries);

		hotbar.Move(-1);
		Assert.Equal(12, hotbar.Selected.Id);
		hotbar.Move(1);
		Assert.Equal(1, hotbar.Selected.Id);
		hotbar.SelectVisibleSlot(8);
		Assert.Equal(9, hotbar.Selected.Id);
		Assert.True(hotbar.IsSelectedSlot(5));
	}

	private static VoxelVertex[] CreateTriangle(VoxelRenderMode mode)
	{
		_ = mode;
		return new[]
		{
			new VoxelVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector3.UnitY),
			new VoxelVertex(Vector3.UnitX, Color.White, Vector2.UnitX, Vector3.UnitY),
			new VoxelVertex(Vector3.UnitZ, Color.White, Vector2.UnitY, Vector3.UnitY),
		};
	}

	private static VoxelMeshData BuildIsolatedCompatibilityBlock(ushort material, VoxelPalette palette)
	{
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(material));
		return VoxelMesher.Build(
			world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
			palette,
			VoxelTestCompatibilityAssets.AtlasLayout
		);
	}

	private static System.Drawing.Color SampleUv(System.Drawing.Bitmap bitmap, Vector2 uv)
	{
		int x = Math.Clamp((int)(uv.X * bitmap.Width), 0, bitmap.Width - 1);
		int y = Math.Clamp((int)((1 - uv.Y) * bitmap.Height), 0, bitmap.Height - 1);
		return bitmap.GetPixel(x, y);
	}

	private static void AssertUvInsideTile(Vector2 uv, int tile)
	{
		int column = tile % 16;
		int row = tile / 16;
		float minimumU = column / 16f;
		float maximumU = (column + 1) / 16f;
		float minimumV = 1 - (row + 1) / 16f;
		float maximumV = 1 - row / 16f;
		Assert.InRange(uv.X, minimumU, maximumU);
		Assert.InRange(uv.Y, minimumV, maximumV);
	}

	private static void AssertModelInside(VoxelModel model, VoxelTextureRegion region)
	{
		float minimumU = region.X / (float)region.AtlasWidth;
		float maximumU = (region.X + region.Width) / (float)region.AtlasWidth;
		float minimumV = 1 - (region.Y + region.Height) / (float)region.AtlasHeight;
		float maximumV = 1 - region.Y / (float)region.AtlasHeight;

		Assert.All(model.Vertices, vertex =>
		{
			Assert.InRange(vertex.UV.X, minimumU, maximumU);
			Assert.InRange(vertex.UV.Y, minimumV, maximumV);
		});
	}
}
