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
		Assert.Equal(480, palette[ids.Grass].Tiles.PositiveY);
		Assert.Equal(481, palette[ids.Grass].Tiles.PositiveX);
		Assert.Equal(1, palette[ids.Grass].Tiles.NegativeY);
		Assert.Equal(482, palette[ids.Wood].Tiles.PositiveX);
		Assert.Equal(483, palette[ids.Wood].Tiles.PositiveY);
		Assert.Equal(484, palette[ids.CraftingTable].Tiles.PositiveY);
		Assert.Equal(487, palette[ids.CraftingTable].Tiles.NegativeY);
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

		Assert.Equal(1024, composite.Width);
		Assert.Equal(1024, composite.Height);
		Assert.Equal(source.GetPixel(0, 0), composite.GetPixel(0, 0));
		Assert.Equal(source.GetPixel(511, 511), composite.GetPixel(511, 511));
		Assert.NotEqual(0, composite.GetPixel(VoxelTestCompatibilityAssets.BarrelRegion.X, VoxelTestCompatibilityAssets.BarrelRegion.Y).A);
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
}
