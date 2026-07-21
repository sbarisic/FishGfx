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
	public void AxisAlignedBoundsUseThreeDimensionalSizeAndUnion()
	{
		AxisAlignedBoundingBox first = AxisAlignedBoundingBox.FromPositionAndSize(
			new Vector3(10, 20, 30),
			new Vector3(4, 6, 8)
		);
		AxisAlignedBoundingBox second = AxisAlignedBoundingBox.FromPositionAndSize(
			new Vector3(5, 24, 35),
			new Vector3(8, 10, 12)
		);
		AxisAlignedBoundingBox union = first.Union(second);

		Assert.Equal(new Vector3(4, 6, 8), first.Size);
		Assert.Equal(new Vector3(12, 23, 34), first.Center);
		Assert.Equal(new Vector3(14, 26, 38), first.Max);
		Assert.Equal(new Vector3(5, 20, 30), union.Min);
		Assert.Equal(new Vector3(9, 14, 17), union.Size);
		Assert.True(
			AxisAlignedBoundingBox.FromPositionAndSize(
				new Vector3(0, 4, 4),
				new Vector3(10, 2, 2)
			).Intersects(
				AxisAlignedBoundingBox.FromPositionAndSize(
					new Vector3(4, 0, 4),
					new Vector3(2, 10, 2)
				)
			)
		);
		Assert.False(
			AxisAlignedBoundingBox.FromPositionAndSize(
				Vector3.Zero,
				new Vector3(10)
			).Intersects(
				AxisAlignedBoundingBox.FromPositionAndSize(
					new Vector3(2, 2, 20),
					new Vector3(2)
				)
			)
		);
	}

	[Fact]
	public void FrustumRejectsDistantBounds()
	{
		Camera camera = new();
		camera.SetPerspective(1920, 1080, MathF.PI / 2, 0.1f, 100);
		ViewFrustum frustum = ViewFrustum.FromCamera(camera);

		Assert.True(
			frustum.Intersects(
				AxisAlignedBoundingBox.FromPositionAndSize(
					new Vector3(-1, -1, -6),
					new Vector3(2)
				)
			)
		);
		Assert.False(
			frustum.Intersects(
				AxisAlignedBoundingBox.FromPositionAndSize(
					new Vector3(100, 100, -6),
					new Vector3(2)
				)
			)
		);
		Assert.False(frustum.Intersects(AxisAlignedBoundingBox.Empty));
	}

	[Fact]
	public void VoxelRaycastFindsSurfaceAndAdjacentPlacementCell()
	{
		VoxelWorld world = new();
		world.SetVoxel(0, 0, 0, new VoxelCell(1));

		Assert.True(
			VoxelRaycast.Cast(
				world,
				new Vector3(0.5f, 0.5f, -2),
				Vector3.UnitZ,
				10,
				out VoxelRaycastHit hit
			)
		);
		Assert.Equal((0, 0, 0), (hit.X, hit.Y, hit.Z));
		Assert.Equal((0, 0, -1), (hit.NormalX, hit.NormalY, hit.NormalZ));
		Assert.Equal((0, 0, -1), (hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ));
		Assert.Equal(2, hit.Distance, 5);
		Assert.Equal(new VoxelCell(1), hit.Voxel);
	}

	[Fact]
	public void VoxelRaycastSupportsNegativeCoordinatesAndRangeLimits()
	{
		VoxelWorld world = new();
		world.SetVoxel(-1, 0, 0, new VoxelCell(2));

		Assert.False(
			VoxelRaycast.Cast(
				world,
				new Vector3(-0.5f, 0.5f, 3),
				-Vector3.UnitZ,
				1.99f,
				out _
			)
		);
		Assert.True(
			VoxelRaycast.Cast(
				world,
				new Vector3(-0.5f, 0.5f, 3),
				-Vector3.UnitZ,
				2,
				out VoxelRaycastHit hit
			)
		);
		Assert.Equal((-1, 0, 0), (hit.X, hit.Y, hit.Z));
		Assert.Equal((0, 0, 1), (hit.NormalX, hit.NormalY, hit.NormalZ));
	}

	[Fact]
	public void VoxelRaycastReportsOriginsInsideSolidVoxels()
	{
		VoxelWorld world = new();
		world.SetVoxel(2, 3, 4, new VoxelCell(1));

		Assert.True(
			VoxelRaycast.Cast(
				world,
				new Vector3(2.25f, 3.5f, 4.75f),
				Vector3.UnitX,
				0,
				out VoxelRaycastHit hit
			)
		);
		Assert.Equal(0, hit.Distance);
		Assert.False(hit.HasSurfaceNormal);
	}

	[Fact]
	public void PriorityFloodLeavesBoundaryConnectedDepressionsDry()
	{
		int[,] heights = CreateHeightField(5, 5, 5);

		for (int x = 0; x <= 2; x++)
		{
			heights[x, 2] = 0;
		}

		VoxelLakeMap lakes = VoxelLakeAnalyzer.FindEnclosedBasins(heights, minimumArea: 1);

		Assert.Equal(0, lakes.BasinCount);
		Assert.Equal(0, lakes.WaterColumnCount);
		Assert.Null(lakes.GetWaterSurface(2, 2));
	}

	[Fact]
	public void PriorityFloodFillsEnclosedDepressionsToTheirSpillElevation()
	{
		int[,] heights = CreateHeightField(5, 5, 5);

		for (int z = 1; z <= 3; z++)
		{
			for (int x = 1; x <= 3; x++)
			{
				heights[x, z] = 1;
			}
		}

		VoxelLakeMap lakes = VoxelLakeAnalyzer.FindEnclosedBasins(heights, minimumArea: 1);

		Assert.Equal(1, lakes.BasinCount);
		Assert.Equal(9, lakes.WaterColumnCount);
		Assert.Equal(5, lakes.GetWaterSurface(2, 2));
		Assert.Null(lakes.GetWaterSurface(0, 2));
	}

	[Fact]
	public void PriorityFloodKeepsSeparateBasinsAtIndependentLevels()
	{
		int[,] heights = CreateHeightField(11, 7, 0);
		SetEnclosedCell(heights, 3, 3, floor: 1, rim: 4);
		SetEnclosedCell(heights, 7, 3, floor: 2, rim: 7);

		VoxelLakeMap lakes = VoxelLakeAnalyzer.FindEnclosedBasins(heights, minimumArea: 1);

		Assert.Equal(2, lakes.BasinCount);
		Assert.Equal(4, lakes.GetWaterSurface(3, 3));
		Assert.Equal(7, lakes.GetWaterSurface(7, 3));
	}

	[Fact]
	public void PriorityFloodFiltersSmallPuddlesAndIsDeterministic()
	{
		int[,] heights = CreateHeightField(11, 7, 0);
		SetEnclosedCell(heights, 3, 3, floor: 1, rim: 4);
		SetEnclosedCell(heights, 7, 3, floor: 2, rim: 7);

		VoxelLakeMap first = VoxelLakeAnalyzer.FindEnclosedBasins(heights, minimumArea: 2);
		VoxelLakeMap second = VoxelLakeAnalyzer.FindEnclosedBasins(heights, minimumArea: 2);

		Assert.Equal(0, first.BasinCount);
		Assert.Equal(0, first.WaterColumnCount);

		for (int z = 0; z < heights.GetLength(1); z++)
		{
			for (int x = 0; x < heights.GetLength(0); x++)
			{
				Assert.Equal(first.GetWaterSurface(x, z), second.GetWaterSurface(x, z));
			}
		}
	}

	[Fact]
	public void VoxelFogSettingsValidateAndCalculateExponentialFog()
	{
		Assert.False(VoxelFogSettings.Disabled.Enabled);
		Assert.Equal(0, VoxelFogSettings.Disabled.CalculateFactor(100));

		VoxelFogSettings fog = new(new Color(30, 111, 145), 0.06f, 0.7f);
		VoxelFogSettings equivalent = new(new Color(30, 111, 145), 0.06f, 0.7f);

		Assert.True(fog.Enabled);
		Assert.Equal(equivalent, fog);
		Assert.Equal(0, fog.CalculateFactor(0));
		Assert.InRange(fog.CalculateFactor(10), 0.45f, 0.46f);
		Assert.True(fog.CalculateFactor(100) > fog.CalculateFactor(10));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelFogSettings(Color.Blue, float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelFogSettings(Color.Blue, -0.1f));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelFogSettings(Color.Blue, 0.1f, 1.1f));
		Assert.Throws<ArgumentOutOfRangeException>(() => fog.CalculateFactor(float.PositiveInfinity));
	}

	[Fact]
	public void VoxelMediumQueryUsesFlooredNegativeCoordinatesAndExactSurfaces()
	{
		VoxelWorld world = new();
		world.SetVoxel(-1, 2, -1, new VoxelCell(2));
		world.SetVoxel(0, 2, 0, new VoxelCell(3));

		Assert.True(VoxelMediumQuery.IsInsideMaterial(world, new Vector3(-0.5f, 2.5f, -0.5f), 2));
		Assert.True(VoxelMediumQuery.IsInsideMaterial(world, new Vector3(-0.001f, 2.999f, -0.001f), 2));
		Assert.False(VoxelMediumQuery.IsInsideMaterial(world, new Vector3(-0.5f, 3, -0.5f), 2));
		Assert.True(VoxelMediumQuery.IsInsideMaterial(world, new Vector3(-0.5f, 3, -0.5f), 0));
		Assert.False(VoxelMediumQuery.IsInsideMaterial(world, new Vector3(0.5f, 2.5f, 0.5f), 2));
		Assert.Equal(new VoxelCell(3), VoxelMediumQuery.GetVoxel(world, new Vector3(0.5f, 2.5f, 0.5f)));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => VoxelMediumQuery.GetVoxel(world, new Vector3(float.NaN, 0, 0))
		);
	}

	[Fact]
	public void GpuVoxelImplementationTypesAreNotPublicApi()
	{
		Assert.False(typeof(VoxelMesh).IsPublic);
		Assert.False(typeof(DrawVoxelIndexedCommand).IsPublic);
		Assert.False(typeof(VoxelTransparentGeometryStore).IsPublic);
		Assert.False(typeof(VoxelTransparentOrderingScheduler).IsPublic);
		Assert.False(typeof(VoxelTransparentIndexRing).IsPublic);
		Assert.False(typeof(DrawVoxelPagesCommand).IsPublic);
	}

	[Fact]
	public void VoxelRendererRequiresExplicitGraphicsAndLightingOwnership()
	{
		System.Reflection.ConstructorInfo constructor = Assert.Single(
			typeof(VoxelRenderer).GetConstructors()
		);
		Type[] parameterTypes = constructor
			.GetParameters()
			.Select(parameter => parameter.ParameterType)
			.ToArray();

		Assert.Equal(
			new[]
			{
				typeof(GraphicsContext),
				typeof(VoxelWorld),
				typeof(VoxelPalette),
				typeof(VoxelSurfaceTextureSet),
				typeof(VoxelAtlasLayout),
				typeof(VoxelLighting),
				typeof(VoxelRendererOptions),
			},
			parameterTypes
		);
		Assert.NotNull(
			typeof(VoxelRenderer).GetMethod(
				nameof(VoxelRenderer.UpdateMeshes),
				new[] { typeof(int?) }
			)
		);
		Assert.NotNull(
			typeof(VoxelRenderer).GetMethod(
				nameof(VoxelRenderer.UpdateMeshes),
				new[] { typeof(Camera), typeof(int?) }
			)
		);
		Assert.NotNull(typeof(VoxelRenderer).GetMethod(
			nameof(VoxelRenderer.EnqueueVisible),
			new[] { typeof(RenderQueue), typeof(Camera), typeof(float?) }
		));
		Assert.NotNull(typeof(VoxelRenderer).GetProperty(nameof(VoxelRenderer.IsCullingEnabled)));
		Assert.NotNull(typeof(VoxelRenderer).GetProperty(nameof(VoxelRenderer.SunSettings)));
		Assert.NotNull(typeof(VoxelRenderer).GetProperty(nameof(VoxelRenderer.FogSettings)));
		Assert.NotNull(typeof(VoxelRenderer).GetProperty(nameof(VoxelRenderer.AtlasTexture)));
		Assert.NotNull(typeof(VoxelRenderer).GetProperty(nameof(VoxelRenderer.SurfaceTextures)));
		Assert.NotNull(typeof(VoxelRenderer).GetMethod(nameof(VoxelRenderer.SetSurfaceTextures)));
		Assert.Null(typeof(VoxelRenderer).GetMethod("UpdateMeshing"));
		Assert.Null(typeof(VoxelRenderer).GetMethod("SubmitVisible"));
		Assert.Null(typeof(VoxelRenderer).GetProperty("CullingEnabled"));
		Assert.Null(typeof(VoxelRenderer).GetProperty("Sun"));
		Assert.Null(typeof(VoxelRenderer).GetProperty("Fog"));
	}

	[Fact]
	public void VoxelRendererOptionsExposeSchedulingAndInitialSunSettings()
	{
		VoxelRendererOptions options = new();

		Assert.True(options.WorkerCount > 0);
		Assert.Equal(4, options.MaximumMeshingWorkers);
		Assert.Equal(128, options.MaximumReadyMeshJobs);
		Assert.Equal(32L * 1024 * 1024, options.MaximumReadyMeshBytes);
		Assert.Equal(64, options.ResumeReadyMeshJobs);
		Assert.Equal(16L * 1024 * 1024, options.ResumeReadyMeshBytes);
		Assert.True(options.MeshUploadBudget >= 0);
		Assert.True(options.MaxRenderDistance > 0);
		Assert.Equal(64 * 1024 * 1024, options.GeometryPageSizeBytes);
		Assert.Equal(0.25f, options.TransparentResortDistance);
		Assert.Equal(1f, options.TransparentResortAngleDegrees);
		Assert.Equal(8f, options.ActiveSetRefreshDistance);
		Assert.Equal(16f, options.ActivationMargin);
		Assert.Equal(32f, options.DeactivationMargin);
		Assert.NotNull(options.Meshing);
		Assert.Equal(Color.White, options.Sun.Color);
		Assert.Null(typeof(VoxelRendererOptions).GetProperty("MaxWorkers"));
		Assert.Null(typeof(VoxelRendererOptions).GetProperty("UploadBudget"));
		Assert.Null(typeof(VoxelRendererOptions).GetProperty("RenderDistance"));
		Assert.Null(typeof(VoxelRendererOptions).GetProperty("Lighting"));
		Assert.Null(typeof(VoxelRendererOptions).GetProperty("LightDirection"));
		Assert.Null(typeof(VoxelRendererOptions).GetProperty("AmbientLight"));
	}
	private static void AssertWave(VoxelVertex vertex, float influence)
	{
		Assert.Equal(0.1f, vertex.WaveParameters.X, 6);
		Assert.Equal(MathF.Tau / 6, vertex.WaveParameters.Y, 6);
		Assert.Equal(MathF.Tau * 0.2f, vertex.WaveParameters.Z, 6);
		Assert.Equal(influence, vertex.WaveParameters.W);
	}

	private static (VoxelWorld World, VoxelPalette Palette, ushort Opaque, ushort Cutout, ushort Transparent)
		CreateWorldAndPalette()
	{
		VoxelPaletteBuilder builder = new();
		ushort opaque = builder.Add(new VoxelMaterial("Opaque", VoxelRenderMode.Opaque, new VoxelFaceTiles(0)));
		ushort cutout = builder.Add(
			new VoxelMaterial("Cutout", VoxelRenderMode.Cutout, new VoxelFaceTiles(0), occludesFaces: false)
		);
		ushort transparent = builder.Add(
			new VoxelMaterial("Transparent", VoxelRenderMode.Transparent, new VoxelFaceTiles(0), occludesFaces: false)
		);

		return (new VoxelWorld(), builder.Build(), opaque, cutout, transparent);
	}

	private static VoxelMeshData Build(VoxelWorld world, VoxelPalette palette, ChunkCoordinate coordinate)
	{
		return VoxelMesher.Build(
			world.CreateSnapshot(coordinate),
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32)
		);
	}

	private static VoxelMeshData WaitForResult(VoxelMeshingScheduler scheduler)
	{
		VoxelMeshData result = null;
		bool completed = SpinWait.SpinUntil(() => scheduler.TryDequeue(out result), 5000);

		Assert.True(completed, "Timed out waiting for voxel meshing worker.");
		return result;
	}

	private static int[,] CreateHeightField(int width, int height, int value)
	{
		int[,] result = new int[width, height];

		for (int z = 0; z < height; z++)
		{
			for (int x = 0; x < width; x++)
			{
				result[x, z] = value;
			}
		}

		return result;
	}

	private static void SetEnclosedCell(int[,] heights, int centerX, int centerZ, int floor, int rim)
	{
		for (int z = centerZ - 1; z <= centerZ + 1; z++)
		{
			for (int x = centerX - 1; x <= centerX + 1; x++)
			{
				heights[x, z] = rim;
			}
		}

		heights[centerX, centerZ] = floor;
	}

	private static VoxelCell[] CreateChunkData(params (int X, int Y, int Z, ushort Material)[] voxels)
	{
		VoxelCell[] result = new VoxelCell[VoxelWorld.ChunkVolume];

		foreach ((int x, int y, int z, ushort material) in voxels)
		{
			result[x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z)] = new VoxelCell(material);
		}

		return result;
	}
}
