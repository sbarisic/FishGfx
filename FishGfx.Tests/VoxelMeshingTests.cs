using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTests
{
	[Fact]
	public void WaveSettingsValidateAndRequireTransparentCubeMaterials()
	{
		VoxelWaveSettings wave = new(amplitude: 0.1f, wavelength: 6, speed: 0.2f);
		VoxelMaterial water = new(
			"Water",
			VoxelRenderMode.Transparent,
			new VoxelFaceTiles(0),
			wave: wave
		);

		Assert.Equal(wave, water.Wave);
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelWaveSettings(float.NaN, 6, 0.2f));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelWaveSettings(-0.1f, 6, 0.2f));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelWaveSettings(0.1f, 0, 0.2f));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelWaveSettings(0.1f, 6, float.PositiveInfinity));
		Assert.Throws<ArgumentException>(
			() => new VoxelMaterial(
				"Stone",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(0),
				wave: wave
			)
		);
		Assert.Throws<ArgumentException>(
			() => new VoxelMaterial(
				"Default wave",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(0),
				wave: default(VoxelWaveSettings)
			)
		);

		VoxelModel model = new(
			new[]
			{
				new VoxelVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector3.UnitY),
				new VoxelVertex(Vector3.UnitX, Color.White, Vector2.UnitX, Vector3.UnitY),
				new VoxelVertex(Vector3.UnitZ, Color.White, Vector2.UnitY, Vector3.UnitY),
			}
		);
		Assert.Throws<ArgumentException>(
			() => new VoxelMaterial(
				"Model water",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(0),
				models: new VoxelModelSet(model),
				wave: wave
			)
		);
	}

	[Fact]
	public void IsolatedOpaqueCubeEmitsSixFaces()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(36, mesh.OpaqueVertices.Length);
		Assert.Empty(mesh.CutoutVertices);
		Assert.Empty(mesh.TransparentFaces);
		Assert.All(
			mesh.OpaqueVertices,
			vertex => Assert.Equal(new Color(0, 0, 0, 255), vertex.PackedLightChannels)
		);
		Assert.Equal(new Vector3(1), mesh.Bounds.Min);
		Assert.Equal(Vector3.One, mesh.Bounds.Size);
	}

	[Fact]
	public void AdjacentOpaqueCubesRemoveSharedFaces()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		world.SetVoxel(2, 1, 1, new VoxelCell(opaque));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(60, mesh.OpaqueVertices.Length);
	}

	[Fact]
	public void PaddedSnapshotsCullFacesAcrossChunkBoundaries()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(15, 1, 1, new VoxelCell(opaque));
		world.SetVoxel(16, 1, 1, new VoxelCell(opaque));

		VoxelMeshData left = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		VoxelMeshData right = Build(world, palette, new ChunkCoordinate(1, 0, 0));

		Assert.Equal(30, left.OpaqueVertices.Length);
		Assert.Equal(30, right.OpaqueVertices.Length);
	}

	[Fact]
	public void SeparatesCutoutAndTransparentGeometry()
	{
		(VoxelWorld world, VoxelPalette palette, _, ushort cutout, ushort transparent) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(cutout));
		world.SetVoxel(4, 1, 1, new VoxelCell(transparent));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Empty(mesh.OpaqueVertices);
		Assert.Equal(36, mesh.CutoutVertices.Length);
		Assert.Equal(6, mesh.TransparentFaces.Length);
		Assert.Equal(36, mesh.TransparentVertexCount);
		Assert.All(
			mesh.TransparentFaces.SelectMany(face => face.Vertices),
			vertex => Assert.Equal(Vector4.Zero, vertex.WaveParameters)
		);
	}

	[Fact]
	public void AdjacentMatchingTransparentBlocksRemoveInternalFaces()
	{
		(VoxelWorld world, VoxelPalette palette, _, _, ushort transparent) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(transparent));
		world.SetVoxel(2, 1, 1, new VoxelCell(transparent));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(10, mesh.TransparentFaces.Length);
	}

	[Fact]
	public void TransparentBlocksDoNotEmitCoplanarFacesAgainstOccludingNeighbors()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, ushort transparent) =
			CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(transparent));
		world.SetVoxel(2, 1, 1, new VoxelCell(opaque));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(5, mesh.TransparentFaces.Length);
		Assert.DoesNotContain(
			mesh.TransparentFaces,
			face => face.Center == new Vector3(2, 1.5f, 1.5f)
		);
		Assert.Empty(mesh.CutoutVertices);
		Assert.Equal(36, mesh.OpaqueVertices.Length);
	}

	[Fact]
	public void TransparentBlocksRetainFacesBehindNonOccludingCutouts()
	{
		(VoxelWorld world, VoxelPalette palette, _, ushort cutout, ushort transparent) =
			CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(transparent));
		world.SetVoxel(2, 1, 1, new VoxelCell(cutout));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(6, mesh.TransparentFaces.Length);
		Assert.Contains(
			mesh.TransparentFaces,
			face => face.Center == new Vector3(2, 1.5f, 1.5f)
		);
		Assert.Equal(36, mesh.CutoutVertices.Length);
		Assert.Empty(mesh.OpaqueVertices);
	}

	[Fact]
	public void DoubleSidedMaterialsEmitReversedTriangles()
	{
		VoxelPaletteBuilder builder = new();
		ushort glass = builder.Add(
			new VoxelMaterial(
				"Glass",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(0),
				doubleSided: true
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(glass));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(6, mesh.TransparentFaces.Length);
		Assert.All(mesh.TransparentFaces, face => Assert.Equal(12, face.Vertices.Count));
		Assert.Equal(-mesh.TransparentFaces[0].Vertices[0].Normal, mesh.TransparentFaces[0].Vertices[6].Normal);
		VoxelTransparentFace top = mesh.TransparentFaces[2];
		Assert.All(top.Vertices.Take(6), vertex => Assert.Equal(Vector3.UnitY, vertex.Normal));
		Assert.All(top.Vertices.Skip(6), vertex => Assert.Equal(-Vector3.UnitY, vertex.Normal));

		for (int triangle = 0; triangle < top.Vertices.Count; triangle += 3)
		{
			VoxelVertex a = top.Vertices[triangle];
			VoxelVertex b = top.Vertices[triangle + 1];
			VoxelVertex c = top.Vertices[triangle + 2];
			Vector3 geometricNormal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);

			Assert.True(Vector3.Dot(geometricNormal, a.Normal) > 0);
		}
	}

	[Fact]
	public void DoubleSidedWaterVolumeOnlyDoublesExposedBoundaryGeometry()
	{
		VoxelPaletteBuilder builder = new();
		ushort water = builder.Add(
			new VoxelMaterial(
				"Water",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(0),
				occludesFaces: false,
				doubleSided: true
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(water));
		world.SetVoxel(2, 1, 1, new VoxelCell(water));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		Assert.Equal(10, mesh.TransparentFaces.Length);
		Assert.Equal(120, mesh.TransparentVertexCount);
		Assert.All(mesh.TransparentFaces, face => Assert.Equal(12, face.Vertices.Count));
	}

	[Fact]
	public void WaterWavesAnimateTopAndSurfaceRimWithoutMovingBottom()
	{
		VoxelPaletteBuilder builder = new();
		ushort water = builder.Add(
			new VoxelMaterial(
				"Water",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(0),
				doubleSided: true,
				wave: new VoxelWaveSettings(0.1f, 6, 0.2f)
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(water));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		VoxelTransparentFace top = Assert.Single(
			mesh.TransparentFaces,
			face => face.Center == new Vector3(1.5f, 2, 1.5f)
		);
		VoxelTransparentFace bottom = Assert.Single(
			mesh.TransparentFaces,
			face => face.Center == new Vector3(1.5f, 1, 1.5f)
		);

		Assert.All(top.Vertices, vertex => AssertWave(vertex, 1));
		Assert.All(
			bottom.Vertices,
			vertex => Assert.Equal(Vector4.Zero, vertex.WaveParameters)
		);

		foreach (VoxelTransparentFace side in mesh.TransparentFaces.Where(face => face != top && face != bottom))
		{
			Assert.All(side.Vertices.Where(vertex => vertex.Position.Y == 2), vertex => AssertWave(vertex, 1));
			Assert.All(side.Vertices.Where(vertex => vertex.Position.Y == 1), vertex => AssertWave(vertex, 0));
		}
	}

	[Fact]
	public void DeepWaterOnlyAnimatesTheExposedSurfaceRim()
	{
		VoxelPaletteBuilder builder = new();
		ushort water = builder.Add(
			new VoxelMaterial(
				"Water",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(0),
				wave: new VoxelWaveSettings(0.1f, 6, 0.2f)
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(water));
		world.SetVoxel(1, 2, 1, new VoxelCell(water));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		VoxelTransparentFace[] lowerSides = mesh.TransparentFaces
			.Where(
				face => face.Center.Y == 1.5f
					&& (face.Center.X != 1.5f || face.Center.Z != 1.5f)
			)
			.ToArray();
		VoxelTransparentFace[] upperSides = mesh.TransparentFaces
			.Where(
				face => face.Center.Y == 2.5f
					&& (face.Center.X != 1.5f || face.Center.Z != 1.5f)
			)
			.ToArray();

		Assert.Equal(4, lowerSides.Length);
		Assert.Equal(4, upperSides.Length);
		Assert.All(
			lowerSides.SelectMany(face => face.Vertices),
			vertex => Assert.Equal(Vector4.Zero, vertex.WaveParameters)
		);
		Assert.All(
			upperSides.SelectMany(face => face.Vertices).Where(vertex => vertex.Position.Y == 3),
			vertex => AssertWave(vertex, 1)
		);
		Assert.All(
			upperSides.SelectMany(face => face.Vertices).Where(vertex => vertex.Position.Y == 2),
			vertex => AssertWave(vertex, 0)
		);
	}

	[Fact]
	public void FaceWindingMatchesOutwardNormals()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));

		for (int i = 0; i < mesh.OpaqueVertices.Length; i += 3)
		{
			VoxelVertex a = mesh.OpaqueVertices[i];
			VoxelVertex b = mesh.OpaqueVertices[i + 1];
			VoxelVertex c = mesh.OpaqueVertices[i + 2];
			Vector3 triangleNormal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);

			Assert.True(Vector3.Dot(triangleNormal, a.Normal) > 0);
		}
	}

	[Fact]
	public void CubeUvsUseLocalBoundariesAndSelectedArrayLayer()
	{
		VoxelPaletteBuilder builder = new();
		ushort material = builder.Add(new VoxelMaterial("Tile", VoxelRenderMode.Opaque, new VoxelFaceTiles(1)));
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(material));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		float minU = mesh.OpaqueVertices.Min(vertex => vertex.TextureCoordinates.X);
		float maxU = mesh.OpaqueVertices.Max(vertex => vertex.TextureCoordinates.X);
		float minV = mesh.OpaqueVertices.Min(vertex => vertex.TextureCoordinates.Y);
		float maxV = mesh.OpaqueVertices.Max(vertex => vertex.TextureCoordinates.Y);

		Assert.Equal(0, minU, 6);
		Assert.Equal(1, maxU, 6);
		Assert.Equal(0, minV, 6);
		Assert.Equal(1, maxV, 6);
		Assert.All(mesh.OpaqueVertices, vertex => Assert.Equal(1, vertex.TextureLayer));
	}

	[Fact]
	public void AtlasTileBoundsMapEveryNearestSampleToExactlyOneSourceTexel()
	{
		VoxelAtlasLayout atlas = new(16, 16, 512, 512);
		int[] tiles = { 0, 1, 15, 16, 127, 240, 255 };

		foreach (int tile in tiles)
		{
			VoxelAtlasUvBounds bounds = atlas.GetTileUvBounds(tile);
			int tileX = tile % atlas.Columns * atlas.TileWidth;
			int tileYFromBottom = (atlas.Rows - 1 - tile / atlas.Columns)
				* atlas.TileHeight;

			for (int texel = 0; texel < atlas.TileWidth; texel++)
			{
				float local = (texel + 0.5f) / atlas.TileWidth;
				float u = float.Lerp(bounds.MinimumU, bounds.MaximumU, local);
				int sampled = (int)MathF.Floor(u * atlas.TextureWidth);
				Assert.Equal(tileX + texel, sampled);
			}

			for (int texel = 0; texel < atlas.TileHeight; texel++)
			{
				float local = (texel + 0.5f) / atlas.TileHeight;
				float v = float.Lerp(bounds.MinimumV, bounds.MaximumV, local);
				int sampled = (int)MathF.Floor(v * atlas.TextureHeight);
				Assert.Equal(tileYFromBottom + texel, sampled);
			}
		}
	}

	[Fact]
	public void AtlasLayoutRequiresIntegralPixelSizedTiles()
	{
		Assert.Throws<ArgumentException>(() => new VoxelAtlasLayout(16, 16, 513, 512));
		Assert.Throws<ArgumentException>(() => new VoxelAtlasLayout(16, 16, 512, 513));
		VoxelAtlasLayout atlas = new(16, 16, 512, 512);

		Assert.Equal(32, atlas.TileWidth);
		Assert.Equal(32, atlas.TileHeight);
		Assert.Throws<ArgumentOutOfRangeException>(() => atlas.GetTileUvBounds(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => atlas.GetTileUvBounds(256));
	}

	[Fact]
	public void CubeFacesUseRaylibGameCornerOrientations()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		const float MinimumU = 0;
		const float MaximumU = 1;
		const float MinimumV = 0;
		const float MaximumV = 1;
		Vector2 topRight = new(MaximumU, MaximumV);
		Vector2 topLeft = new(MinimumU, MaximumV);
		Vector2 bottomLeft = new(MinimumU, MinimumV);
		Vector2 bottomRight = new(MaximumU, MinimumV);
		Vector2[][] expected =
		{
			new[] { topRight, topLeft, bottomLeft, bottomRight },
			new[] { topRight, topLeft, bottomLeft, bottomRight },
			new[] { topRight, topLeft, bottomLeft, bottomRight },
			new[] { bottomLeft, bottomRight, topRight, topLeft },
			new[] { bottomRight, topRight, topLeft, bottomLeft },
			new[] { topLeft, bottomLeft, bottomRight, topRight },
		};

		for (int face = 0; face < expected.Length; face++)
		{
			for (int corner = 0; corner < 4; corner++)
			{
				Assert.Equal(
					expected[face][corner],
					mesh.OpaqueVertices[face * 6 + corner].TextureCoordinates
				);
			}
		}
	}

	[Fact]
	public void AmbientOcclusionDarkensBlockedCorners()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		VoxelCell stone = new(opaque);
		world.SetVoxel(1, 1, 1, stone);
		world.SetVoxel(2, 2, 1, stone);
		world.SetVoxel(2, 1, 2, stone);
		world.SetVoxel(2, 2, 2, stone);

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		VoxelVertex[] corner = mesh.OpaqueVertices
			.Where(vertex => vertex.Normal == Vector3.UnitX && vertex.Position == new Vector3(2, 2, 2))
			.ToArray();

		Assert.NotEmpty(corner);
		Assert.All(corner, vertex => Assert.Equal(125, vertex.Color.R));
	}

	[Fact]
	public void RejectsUnknownMaterialsAndOutOfRangeTiles()
	{
		VoxelPaletteBuilder builder = new();
		ushort invalidTile = builder.Add(new VoxelMaterial("Bad tile", VoxelRenderMode.Opaque, new VoxelFaceTiles(4)));
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(invalidTile));

		Assert.Throws<InvalidOperationException>(
			() => VoxelMesher.Build(
				world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
				palette,
				new VoxelAtlasLayout(2, 2, 32, 32)
			)
		);

		VoxelPalette emptyPalette = new VoxelPaletteBuilder().Build();
		Assert.Throws<InvalidOperationException>(
			() => VoxelMesher.Build(
				world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
				emptyPalette,
				new VoxelAtlasLayout(2, 2, 32, 32)
			)
		);
	}
}
