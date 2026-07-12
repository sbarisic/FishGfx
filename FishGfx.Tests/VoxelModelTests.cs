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
	public void MinecraftLoaderUsesBlockbenchUvOrientationForEveryDirection()
	{
		Dictionary<string, Vector2[]> expected = new()
		{
			["east"] = new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0) },
			["west"] = new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0) },
			["up"] = new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0) },
			["down"] = new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0) },
			["south"] = new[] { new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0) },
			["north"] = new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) },
		};

		foreach ((string direction, Vector2[] expectedCorners) in expected)
		{
			VoxelModel model = LoadSingleFace(direction, "[0, 0, 16, 16]");

			for (int corner = 0; corner < 4; corner++)
				Assert.Equal(expectedCorners[corner], model.Vertices[corner].UV);
		}
	}

	[Fact]
	public void MinecraftLoaderAppliesFaceRotationsAndPreservesReversedUvEndpoints()
	{
		Dictionary<int, Vector2[]> expected = new()
		{
			[0] = new[] { new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0) },
			[90] = new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) },
			[180] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
			[270] = new[] { new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0) },
		};

		foreach ((int rotation, Vector2[] expectedCorners) in expected)
		{
			VoxelModel model = LoadSingleFace("east", "[0, 0, 16, 16]", rotation);

			for (int corner = 0; corner < 4; corner++)
				Assert.Equal(expectedCorners[corner], model.Vertices[corner].UV);
		}

		VoxelModel reversed = LoadSingleFace("east", "[16, 16, 0, 0]");
		Vector2[] reversedExpected =
		{
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
		};

		for (int corner = 0; corner < 4; corner++)
			Assert.Equal(reversedExpected[corner], reversed.Vertices[corner].UV);
	}

	[Theory]
	[InlineData("[-1, 0, 16, 16]", null)]
	[InlineData("[0, 0, 17, 16]", null)]
	[InlineData("[0, 0, 16, 16]", 45)]
	[InlineData("[0, 0, 16, 16]", -90)]
	public void MinecraftLoaderRejectsUvsOutsideTheirRegionAndUnsupportedFaceRotations(
		string uv,
		int? rotation
	)
	{
		Assert.Throws<FormatException>(() => LoadSingleFace("north", uv, rotation));
	}

	[Fact]
	public void MinecraftLoaderValidatesDeclaredTextureSizeAgainstPackedRegion()
	{
		VoxelModel valid = LoadSingleFace(
			"north",
			"[0, 0, 16, 16]",
			declaredTextureSize: 64,
			regionSize: 64
		);

		Assert.Equal(6, valid.Vertices.Count);
		Assert.Throws<FormatException>(
			() => LoadSingleFace(
				"north",
				"[0, 0, 16, 16]",
				declaredTextureSize: 64,
				regionSize: 16
			)
		);
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
		Assert.True(palette[ids.Water].DoubleSided);
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
	public void PackedModelRegionsPreserveKnownSourcePixelsAndOrientation()
	{
		using System.Drawing.Bitmap atlas = VoxelTestCompatibilityAssets.CreateBitmap();
		(string Directory, string File, VoxelTextureRegion Region, int X, int Y)[] cases =
		{
			("barrel", "barrel_tex.png", VoxelTestCompatibilityAssets.BarrelRegion, 11, 19),
			("campfire", "campfire_tex.png", VoxelTestCompatibilityAssets.CampfireRegion, 17, 43),
			("torch", "torch_tex.png", VoxelTestCompatibilityAssets.TorchRegion, 6, 10),
			("grass", "grass1_tex.png", VoxelTestCompatibilityAssets.FoliageRegion, 5, 12),
		};

		foreach ((string directory, string file, VoxelTextureRegion region, int x, int y) in cases)
		{
			using System.Drawing.Bitmap source = new(
				VoxelTestCompatibilityAssets.AssetPath("models", directory, file)
			);
			Vector2 sourceUv = new((x + 0.5f) / source.Width, (y + 0.5f) / source.Height);
			System.Drawing.Color actual = SampleUv(atlas, region.Map(sourceUv));
			System.Drawing.Color packed = atlas.GetPixel(region.X + x, region.Y + y);
			System.Drawing.Color expected = source.GetPixel(x, y);

			Assert.Equal(packed, actual);
			Assert.Equal(expected, actual);
		}
	}

	[Fact]
	public void PackedModelRegionsPreserveCompleteAlphaMasksAndPadding()
	{
		using System.Drawing.Bitmap atlas = VoxelTestCompatibilityAssets.CreateBitmap();
		(string Directory, string File, VoxelTextureRegion Region)[] cases =
		{
			("barrel", "barrel_tex.png", VoxelTestCompatibilityAssets.BarrelRegion),
			("campfire", "campfire_tex.png", VoxelTestCompatibilityAssets.CampfireRegion),
			("torch", "torch_tex.png", VoxelTestCompatibilityAssets.TorchRegion),
			("grass", "grass1_tex.png", VoxelTestCompatibilityAssets.FoliageRegion),
		};

		foreach ((string directory, string file, VoxelTextureRegion region) in cases)
		{
			using System.Drawing.Bitmap source = new(
				VoxelTestCompatibilityAssets.AssetPath("models", directory, file)
			);

			for (int y = 0; y < source.Height; y++)
				for (int x = 0; x < source.Width; x++)
					Assert.Equal(source.GetPixel(x, y).A, atlas.GetPixel(region.X + x, region.Y + y).A);

			for (int y = 0; y < source.Height; y++)
			{
				Assert.Equal(source.GetPixel(0, y).A, atlas.GetPixel(region.X - 1, region.Y + y).A);
				Assert.Equal(
					source.GetPixel(source.Width - 1, y).A,
					atlas.GetPixel(region.X + region.Width, region.Y + y).A
				);
			}

			for (int x = 0; x < source.Width; x++)
			{
				Assert.Equal(source.GetPixel(x, 0).A, atlas.GetPixel(region.X + x, region.Y - 1).A);
				Assert.Equal(
					source.GetPixel(x, source.Height - 1).A,
					atlas.GetPixel(region.X + x, region.Y + region.Height).A
				);
			}

			Assert.Equal(source.GetPixel(0, 0).A, atlas.GetPixel(region.X - 1, region.Y - 1).A);
			Assert.Equal(
				source.GetPixel(source.Width - 1, 0).A,
				atlas.GetPixel(region.X + region.Width, region.Y - 1).A
			);
			Assert.Equal(
				source.GetPixel(0, source.Height - 1).A,
				atlas.GetPixel(region.X - 1, region.Y + region.Height).A
			);
			Assert.Equal(
				source.GetPixel(source.Width - 1, source.Height - 1).A,
				atlas.GetPixel(region.X + region.Width, region.Y + region.Height).A
			);
		}
	}

	[Fact]
	public void CompatibilityModelsRetainAuthoredBoundsAndWindingAfterElementRotations()
	{
		VoxelTestModelAssets models = VoxelTestCompatibilityAssets.LoadModels();
		(VoxelModel Model, Vector3 Position, Vector3 Size)[] cases =
		{
			(models.Barrel, Vector3.Zero, Vector3.One),
			(models.Campfire, Vector3.Zero, new Vector3(1, 0.375f, 1)),
			(models.Torch, new Vector3(0.375f, 0, 0.375f), new Vector3(0.25f, 0.8125f, 0.25f)),
			(models.Foliage.Models[0], new Vector3(0.1875f, 0, 0.125f), new Vector3(0.6875f, 0.6875f, 0.75f)),
			(models.Foliage.Models[1], new Vector3(0.1875f, 0, 0.125f), new Vector3(0.75f, 0.6875f, 0.75f)),
			(models.Foliage.Models[2], new Vector3(0.125f, 0, 0.0625f), new Vector3(0.75f, 0.5f, 0.75f)),
		};

		foreach ((VoxelModel model, Vector3 position, Vector3 size) in cases)
		{
			Assert.Equal(position, model.Bounds.Position);
			Assert.Equal(size, model.Bounds.Size);

			for (int triangle = 0; triangle < model.Vertices.Count; triangle += 3)
			{
				VoxelVertex a = model.Vertices[triangle];
				VoxelVertex b = model.Vertices[triangle + 1];
				VoxelVertex c = model.Vertices[triangle + 2];
				Vector3 geometricNormal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);

				Assert.True(Vector3.Dot(geometricNormal, a.Normal) > 0);
			}
		}
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

	private static VoxelModel LoadSingleFace(
		string direction,
		string uv,
		int? rotation = null,
		int? declaredTextureSize = null,
		int regionSize = 16
	)
	{
		string rotationProperty = rotation.HasValue ? $", \"rotation\": {rotation.Value}" : string.Empty;
		string textureSizeProperty = declaredTextureSize.HasValue
			? $"\"texture_size\": [{declaredTextureSize.Value}, {declaredTextureSize.Value}],"
			: string.Empty;
		string json = $$"""
			{
			  {{textureSizeProperty}}
			  "elements": [
			    {
			      "from": [0, 0, 0],
			      "to": [16, 16, 16],
			      "faces": {
			        "{{direction}}": { "uv": {{uv}}, "texture": "#0"{{rotationProperty}} }
			      }
			    }
			  ]
			}
			""";
		VoxelTextureRegion region = new(0, 0, regionSize, regionSize, regionSize, regionSize);

		return MinecraftVoxelModelLoader.Load(
			json,
			new Dictionary<string, VoxelTextureRegion> { ["0"] = region }
		);
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
