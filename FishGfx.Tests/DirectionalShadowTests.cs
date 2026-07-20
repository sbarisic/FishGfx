using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class DirectionalShadowTests
{
	[Fact]
	public void PracticalSplitsAreMonotonicAndEndAtMaximumDistance()
	{
		float[] splits = DirectionalShadowRenderer.CalculateSplits(0.05f, 128, 3, 0.65f);

		Assert.Equal(3, splits.Length);
		Assert.True(splits[0] > 0.05f);
		Assert.True(splits[1] > splits[0]);
		Assert.Equal(128, splits[2]);
	}

	[Fact]
	public void StableClipmapDoesNotChangeWhenViewCameraRotates()
	{
		Camera camera = new()
		{
			Position = new Vector3(123.25f, 64, -91.5f),
		};
		camera.SetPerspective(1920, 1080, MathF.PI / 3, 0.1f, 512);
		camera.LookAt(camera.Position - Vector3.UnitZ);
		DirectionalShadowCascade first = DirectionalShadowRenderer.BuildStableClipmap(
			camera,
			Vector3.Normalize(new Vector3(-0.4f, -1, -0.2f)),
			0.1f,
			128,
			0,
			2048,
			128
		);

		camera.LookAt(camera.Position + Vector3.UnitX);
		DirectionalShadowCascade rotated = DirectionalShadowRenderer.BuildStableClipmap(
			camera,
			Vector3.Normalize(new Vector3(-0.4f, -1, -0.2f)),
			0.1f,
			128,
			0,
			2048,
			128
		);

		Assert.Equal(first.ViewProjection, rotated.ViewProjection);
		Assert.Equal(first.TexelWorldSize, rotated.TexelWorldSize);
	}

	[Fact]
	public void TransparentAlphaCasterProducesIndependentShadowGeometry()
	{
		VoxelPaletteBuilder builder = new();
		ushort leaf = builder.Add(new VoxelMaterial(
			"Leaf",
			VoxelRenderMode.Transparent,
			new VoxelFaceTiles(0),
			occludesFaces: false,
			shadowCasterMode: VoxelShadowCasterMode.AlphaTest,
			shadowAlphaCutoff: 0.35f
		));
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(0, 0, 0, new VoxelCell(leaf));

		VoxelMeshData mesh = VoxelMesher.Build(
			world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
			palette,
			new VoxelAtlasLayout(1, 1, 16, 16)
		);

		Assert.Equal(6, mesh.TransparentFaces.Length);
		Assert.Equal(36, mesh.AlphaShadowVertices.Length);
		Assert.All(mesh.AlphaShadowVertices, vertex =>
			Assert.Equal(0.35f, vertex.WaveParameters.X));
	}

	[Fact]
	public void CutoutAlphaCasterReusesVisibleGeometryWithoutIndependentShadowGeometry()
	{
		VoxelPaletteBuilder builder = new();
		ushort leaf = builder.Add(new VoxelMaterial(
			"Leaf",
			VoxelRenderMode.Cutout,
			new VoxelFaceTiles(0),
			occludesFaces: false,
			shadowAlphaCutoff: VoxelRendererOptions.DefaultAlphaCutoff
		));
		VoxelWorld world = new();
		world.SetVoxel(0, 0, 0, new VoxelCell(leaf));

		VoxelMeshData mesh = VoxelMesher.Build(
			world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
			builder.Build(),
			new VoxelAtlasLayout(1, 1, 16, 16)
		);

		Assert.Equal(36, mesh.CutoutVertices.Length);
		Assert.Empty(mesh.TransparentFaces);
		Assert.Empty(mesh.AlphaShadowVertices);
	}

	[Fact]
	public void WavingAlphaTestCasterIsRejected()
	{
		Assert.Throws<ArgumentException>(() => new VoxelMaterial(
			"Invalid",
			VoxelRenderMode.Transparent,
			new VoxelFaceTiles(0),
			wave: new VoxelWaveSettings(0.1f, 6, 0.2f),
			shadowCasterMode: VoxelShadowCasterMode.AlphaTest
		));
	}

	[Fact]
	public void OrdinaryTransparentMaterialDoesNotProduceCasterGeometry()
	{
		VoxelPaletteBuilder builder = new();
		ushort water = builder.Add(new VoxelMaterial(
			"Water",
			VoxelRenderMode.Transparent,
			new VoxelFaceTiles(0),
			occludesFaces: false,
			doubleSided: true,
			wave: new VoxelWaveSettings(0.1f, 6, 0.2f)
		));
		VoxelWorld world = new();
		world.SetVoxel(0, 0, 0, new VoxelCell(water));

		VoxelMeshData mesh = VoxelMesher.Build(
			world.CreateSnapshot(new ChunkCoordinate(0, 0, 0)),
			builder.Build(),
			new VoxelAtlasLayout(1, 1, 16, 16)
		);

		Assert.NotEmpty(mesh.TransparentFaces);
		Assert.Empty(mesh.AlphaShadowVertices);
	}

	[Fact]
	public void AlphaTestCastersRetainTheConfiguredRasterBias()
	{
		RenderState baseState = RenderState.Default with
		{
			CullMode = CullMode.Front,
			DepthBiasSlope = 0.75f,
			DepthBiasConstant = 1f,
		};

		RenderState alphaState = DrawVoxelShadowPagesCommand.CreateAlphaTestState(baseState);

		Assert.Equal(CullMode.Back, alphaState.CullMode);
		Assert.Equal(baseState.DepthBiasSlope, alphaState.DepthBiasSlope);
		Assert.Equal(baseState.DepthBiasConstant, alphaState.DepthBiasConstant);
	}
}
