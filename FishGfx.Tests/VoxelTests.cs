using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public class VoxelTests
{
	[Theory]
	[InlineData(0, 0, 0)]
	[InlineData(15, 0, 15)]
	[InlineData(16, 1, 0)]
	[InlineData(-1, -1, 15)]
	[InlineData(-16, -1, 0)]
	[InlineData(-17, -2, 15)]
	public void ChunkCoordinatesUseFloorDivision(int worldX, int chunkX, int localX)
	{
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(worldX, 0, worldX, out int x, out int y, out int z);

		Assert.Equal(chunkX, coordinate.X);
		Assert.Equal(chunkX, coordinate.Z);
		Assert.Equal(localX, x);
		Assert.Equal(0, y);
		Assert.Equal(localX, z);
	}

	[Fact]
	public void WorldEditsAndRemovesEmptyChunks()
	{
		VoxelWorld world = new();
		VoxelCell stone = new(1);

		Assert.True(world.SetVoxel(-1, 3, -17, stone));
		Assert.Equal(stone, world.GetVoxel(-1, 3, -17));
		Assert.Equal(VoxelCell.Air, world.GetVoxel(100, 100, 100));
		Assert.Single(world.LoadedChunks);

		Assert.True(world.SetVoxel(-1, 3, -17, VoxelCell.Air));
		Assert.Equal(1, world.RemoveEmptyChunks());
		Assert.Empty(world.LoadedChunks);
	}

	[Fact]
	public void BoundaryEditInvalidatesOwningAndDiagonalNeighborChunks()
	{
		VoxelWorld world = new();
		VoxelCell stone = new(1);
		world.SetVoxel(15, 15, 15, stone);
		world.SetVoxel(16, 16, 16, stone);
		List<ChunkCoordinate> invalidated = new();
		world.ChunkInvalidated += (coordinate, _) => invalidated.Add(coordinate);

		world.SetVoxel(15, 15, 15, VoxelCell.Air);

		Assert.Contains(new ChunkCoordinate(0, 0, 0), invalidated);
		Assert.Contains(new ChunkCoordinate(1, 1, 1), invalidated);
	}

	[Fact]
	public void BulkChunkReplacementCopiesDataAndInvalidatesNeighborsOnce()
	{
		VoxelWorld world = new();
		ChunkCoordinate center = new(0, 0, 0);
		ChunkCoordinate neighbor = new(1, 0, 0);
		world.SetVoxel(16, 1, 1, new VoxelCell(1));
		long neighborRevision = world.LoadedChunks.Single(chunk => chunk.Coordinate == neighbor).Revision;
		List<ChunkCoordinate> invalidated = new();
		world.ChunkInvalidated += (coordinate, _) => invalidated.Add(coordinate);
		VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
		cells[1 + VoxelWorld.ChunkSize * (2 + VoxelWorld.ChunkSize * 3)] = new VoxelCell(2);

		Assert.True(world.SetChunk(center, cells));
		cells[1 + VoxelWorld.ChunkSize * (2 + VoxelWorld.ChunkSize * 3)] = VoxelCell.Air;

		Assert.Equal(new VoxelCell(2), world.GetVoxel(1, 2, 3));
		Assert.Equal(1, invalidated.Count(coordinate => coordinate == center));
		Assert.Equal(1, invalidated.Count(coordinate => coordinate == neighbor));
		Assert.True(world.TryGetChunk(neighbor, out VoxelChunk neighborChunk));
		Assert.Equal(neighborRevision + 1, neighborChunk.Revision);
		Assert.False(world.SetChunk(center, CreateChunkData((1, 2, 3, 2))));
	}

	[Fact]
	public void BulkAirChunkRemovesExistingChunkAndValidatesLength()
	{
		VoxelWorld world = new();
		ChunkCoordinate coordinate = new(-2, 3, 4);
		VoxelCell[] cells = CreateChunkData((0, 0, 0, 1));
		world.SetChunk(coordinate, cells);
		ChunkCoordinate? removed = null;
		world.ChunkRemoved += value => removed = value;

		Assert.True(world.SetChunk(coordinate, new VoxelCell[VoxelWorld.ChunkVolume]));
		Assert.Equal(coordinate, removed);
		Assert.False(world.TryGetChunk(coordinate, out _));
		Assert.False(world.SetChunk(coordinate, new VoxelCell[VoxelWorld.ChunkVolume]));
		Assert.Throws<ArgumentException>(() => world.SetChunk(coordinate, new VoxelCell[1]));
	}

	[Fact]
	public void PaletteReservesAirAndBecomesImmutable()
	{
		VoxelPaletteBuilder builder = new();
		ushort stone = builder.Add(new VoxelMaterial("Stone", VoxelRenderMode.Opaque, new VoxelFaceTiles(2)));
		VoxelPalette palette = builder.Build();

		Assert.Equal(1, stone);
		Assert.Null(palette[0]);
		Assert.Equal("Stone", palette[stone].Name);
		Assert.True(palette[stone].OccludesFaces);
		Assert.Throws<InvalidOperationException>(
			() => builder.Add(new VoxelMaterial("Later", VoxelRenderMode.Opaque, new VoxelFaceTiles(0)))
		);
		Assert.Throws<ArgumentException>(() => new VoxelMaterial("", VoxelRenderMode.Opaque, new VoxelFaceTiles(0)));
		Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelFaceTiles(-1));
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
		Assert.Equal(new Vector3(1), mesh.Bounds.Position);
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
	public void AtlasUvsUseSelectedTileAndHalfTexelInset()
	{
		VoxelPaletteBuilder builder = new();
		ushort material = builder.Add(new VoxelMaterial("Tile", VoxelRenderMode.Opaque, new VoxelFaceTiles(1)));
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(material));

		VoxelMeshData mesh = Build(world, palette, new ChunkCoordinate(0, 0, 0));
		float minU = mesh.OpaqueVertices.Min(vertex => vertex.UV.X);
		float maxU = mesh.OpaqueVertices.Max(vertex => vertex.UV.X);
		float minV = mesh.OpaqueVertices.Min(vertex => vertex.UV.Y);
		float maxV = mesh.OpaqueVertices.Max(vertex => vertex.UV.Y);

		Assert.Equal(0.515625f, minU, 6);
		Assert.Equal(0.984375f, maxU, 6);
		Assert.Equal(0.515625f, minV, 6);
		Assert.Equal(0.984375f, maxV, 6);
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

	[Fact]
	public void AabbUsesThreeDimensionalSizeAndUnion()
	{
		AABB first = new(new Vector3(10, 20, 30), new Vector3(4, 6, 8));
		AABB second = new(new Vector3(5, 24, 35), new Vector3(8, 10, 12));
		AABB union = first.Union(second);

		Assert.Equal(new Vector3(4, 6, 8), first.Bounds);
		Assert.Equal(new Vector3(12, 23, 34), first.Center);
		Assert.Equal(new Vector3(14, 26, 38), first.Maxs);
		Assert.Equal(new Vector3(5, 20, 30), union.Position);
		Assert.Equal(new Vector3(9, 14, 17), union.Size);
		Assert.True(
			new AABB(new Vector3(0, 4, 4), new Vector3(10, 2, 2)).Collide(
				new AABB(new Vector3(4, 0, 4), new Vector3(2, 10, 2))
			)
		);
		Assert.False(
			new AABB(new Vector3(0, 0, 0), new Vector3(10)).Collide(
				new AABB(new Vector3(2, 2, 20), new Vector3(2))
			)
		);
	}

	[Fact]
	public void FrustumRejectsDistantBounds()
	{
		Camera camera = new();
		camera.SetPerspective(1920, 1080, MathF.PI / 2, 0.1f, 100);
		ViewFrustum frustum = ViewFrustum.FromCamera(camera);

		Assert.True(frustum.Intersects(new AABB(new Vector3(-1, -1, -6), new Vector3(2))));
		Assert.False(frustum.Intersects(new AABB(new Vector3(100, 100, -6), new Vector3(2))));
		Assert.False(frustum.Intersects(AABB.Empty));
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
			heights[x, 2] = 0;

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
			for (int x = 1; x <= 3; x++)
				heights[x, z] = 1;

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
			for (int x = 0; x < heights.GetLength(0); x++)
				Assert.Equal(first.GetWaterSurface(x, z), second.GetWaterSurface(x, z));
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
	public void DrawVoxelMeshCommandRetainsCompatibilityAndFogOverloads()
	{
		Type[] compatibilityParameters =
		{
			typeof(VoxelMesh),
			typeof(Texture),
			typeof(ShaderProgram),
			typeof(Vector3),
			typeof(float),
			typeof(float),
		};
		Type[] fogParameters = compatibilityParameters.Append(typeof(VoxelFogSettings)).ToArray();

		Assert.NotNull(typeof(DrawVoxelMeshCommand).GetConstructor(compatibilityParameters));
		Assert.NotNull(typeof(DrawVoxelMeshCommand).GetConstructor(fogParameters));
		Assert.Equal(typeof(VoxelFogSettings), typeof(DrawVoxelMeshCommand).GetProperty(nameof(DrawVoxelMeshCommand.Fog))?.PropertyType);
	}

	[Fact]
	public void MeshingSchedulerProducesRevisionedResultsAndReschedulesEdits()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		using VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);
		ChunkCoordinate coordinate = new(0, 0, 0);

		Assert.Equal(1, scheduler.SchedulePending());
		world.SetVoxel(2, 1, 1, new VoxelCell(opaque));
		VoxelMeshData stale = WaitForResult(scheduler);

		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk chunk));
		Assert.True(stale.Revision < chunk.Revision);
		SpinWait.SpinUntil(() => scheduler.InFlightCount == 0, 5000);
		Assert.Equal(1, scheduler.SchedulePending());
		VoxelMeshData current = WaitForResult(scheduler);

		Assert.Equal(chunk.Revision, current.Revision);
		Assert.Equal(60, current.OpaqueVertices.Length);
	}

	[Fact]
	public void SchedulerHonorsWorkerLimitAndRejectsUseAfterDisposal()
	{
		(VoxelWorld world, VoxelPalette palette, ushort opaque, _, _) = CreateWorldAndPalette();
		world.SetVoxel(1, 1, 1, new VoxelCell(opaque));
		world.SetVoxel(17, 1, 1, new VoxelCell(opaque));
		VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			palette,
			new VoxelAtlasLayout(2, 2, 32, 32),
			maxWorkers: 1
		);

		Assert.Equal(1, scheduler.SchedulePending());
		Assert.InRange(scheduler.InFlightCount, 0, 1);
		scheduler.Dispose();
		Assert.Throws<ObjectDisposedException>(() => scheduler.SchedulePending());
	}

	[Fact]
	public void SchedulerReportsWorkerMeshingFailures()
	{
		VoxelWorld world = new();
		world.SetVoxel(1, 1, 1, new VoxelCell(99));
		using VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			world,
			new VoxelPaletteBuilder().Build(),
			new VoxelAtlasLayout(1, 1, 16, 16),
			maxWorkers: 1
		);
		scheduler.SchedulePending();
		Exception failure = null;

		bool completed = SpinWait.SpinUntil(() => scheduler.TryDequeueFailure(out failure), 5000);

		Assert.True(completed);
		Assert.Contains("(0, 0, 0)", failure.Message);
		Assert.IsType<InvalidOperationException>(failure.InnerException);
	}

	[Fact]
	public void TransparentStreamSortsBackToFrontAndAppliesChunkOrigins()
	{
		VoxelVertex[] nearVertices =
		{
			new VoxelVertex(new Vector3(1, 0, 0), Color.Red, Vector2.Zero, Vector3.UnitZ),
		};
		VoxelVertex[] farVertices =
		{
			new VoxelVertex(new Vector3(2, 0, 0), Color.Blue, Vector2.Zero, Vector3.UnitZ),
		};
		VoxelTransparentFace near = new VoxelTransparentFace(new Vector3(0, 0, -2), nearVertices);
		VoxelTransparentFace far = new VoxelTransparentFace(new Vector3(0, 0, -10), farVertices);
		List<VoxelTransparentFaceInstance> faces = new()
		{
			new VoxelTransparentFaceInstance(new ChunkCoordinate(0, 0, 0), 0, new Vector3(10, 0, 0), near),
			new VoxelTransparentFaceInstance(new ChunkCoordinate(1, 0, 0), 0, new Vector3(20, 0, 0), far),
		};

		VoxelVertex[] stream = VoxelTransparentStreamBuilder.Build(Vector3.Zero, -Vector3.UnitZ, faces);

		Assert.Equal(Color.Blue, stream[0].Color);
		Assert.Equal(new Vector3(22, 0, 0), stream[0].Position);
		Assert.Equal(Color.Red, stream[1].Color);
		Assert.Equal(new Vector3(11, 0, 0), stream[1].Position);
	}

	[Fact]
	public void TransparentStreamUsesPrecomputedDepthAndStableCoordinateTies()
	{
		VoxelTransparentFace face = new VoxelTransparentFace(
			Vector3.Zero,
			new[] { new VoxelVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector3.UnitY) }
		);
		List<VoxelTransparentFaceInstance> faces = new()
		{
			new VoxelTransparentFaceInstance(new ChunkCoordinate(1, 0, 0), 0, Vector3.Zero, face, 5),
			new VoxelTransparentFaceInstance(new ChunkCoordinate(0, 0, 0), 0, Vector3.Zero, face, 5),
			new VoxelTransparentFaceInstance(new ChunkCoordinate(2, 0, 0), 0, Vector3.Zero, face, 10),
		};
		VoxelVertex[] destination = new VoxelVertex[3];

		int count = VoxelTransparentStreamBuilder.BuildSorted(faces, destination);

		Assert.Equal(3, count);
		Assert.Equal(new ChunkCoordinate(2, 0, 0), faces[0].Coordinate);
		Assert.Equal(new ChunkCoordinate(0, 0, 0), faces[1].Coordinate);
		Assert.Equal(new ChunkCoordinate(1, 0, 0), faces[2].Coordinate);
	}

	[Fact]
	public void TransparentCacheKeyChangesForCameraVisibilityAndGeometry()
	{
		VoxelTransparentCacheKey baseline = new VoxelTransparentCacheKey(4, 10, Matrix4x4.Identity);

		Assert.Equal(baseline, new VoxelTransparentCacheKey(4, 10, Matrix4x4.Identity));
		Assert.NotEqual(baseline, new VoxelTransparentCacheKey(5, 10, Matrix4x4.Identity));
		Assert.NotEqual(baseline, new VoxelTransparentCacheKey(4, 11, Matrix4x4.Identity));
		Assert.NotEqual(
			baseline,
			new VoxelTransparentCacheKey(4, 10, Matrix4x4.CreateTranslation(1, 0, 0))
		);
	}

	[Theory]
	[InlineData(0, 1, 64)]
	[InlineData(64, 64, 64)]
	[InlineData(64, 65, 128)]
	[InlineData(128, 1000, 1024)]
	public void VoxelBufferCapacityGrowsByPowersOfTwo(int current, int required, int expected)
	{
		Assert.Equal(expected, VoxelMesh.CalculateCapacity(current, required));
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
			for (int x = 0; x < width; x++)
				result[x, z] = value;

		return result;
	}

	private static void SetEnclosedCell(int[,] heights, int centerX, int centerZ, int floor, int rim)
	{
		for (int z = centerZ - 1; z <= centerZ + 1; z++)
			for (int x = centerX - 1; x <= centerX + 1; x++)
				heights[x, z] = rim;

		heights[centerX, centerZ] = floor;
	}

	private static VoxelCell[] CreateChunkData(params (int X, int Y, int Z, ushort Material)[] voxels)
	{
		VoxelCell[] result = new VoxelCell[VoxelWorld.ChunkVolume];

		foreach ((int x, int y, int z, ushort material) in voxels)
			result[x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z)] = new VoxelCell(material);

		return result;
	}
}
