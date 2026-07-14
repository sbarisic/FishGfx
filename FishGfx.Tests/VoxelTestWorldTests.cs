using System;
using System.Linq;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using FishGfx.VoxelTest;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTestWorldTests
{
	private static readonly Lazy<GeneratedWorld> Generated = new Lazy<GeneratedWorld>(CreateGeneratedWorld);

	[Fact]
	public void GeneratedWorldHasExpandedBoundsAndBoundedLakes()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestWorldData data = generated.Data;
		int minimumHeight = int.MaxValue;
		int maximumHeight = int.MinValue;

		Assert.Equal(-640, VoxelTestWorldGenerator.WorldMinimum);
		Assert.Equal(640, VoxelTestWorldGenerator.WorldMaximum);
		Assert.Equal(1280, VoxelTestWorldGenerator.WorldSize);
		Assert.Equal(-40, VoxelTestWorldGenerator.MinimumChunkCoordinate);
		Assert.Equal(39, VoxelTestWorldGenerator.MaximumChunkCoordinate);

		for (int z = VoxelTestWorldGenerator.WorldMinimum; z < VoxelTestWorldGenerator.WorldMaximum; z++)
		{
			for (int x = VoxelTestWorldGenerator.WorldMinimum; x < VoxelTestWorldGenerator.WorldMaximum; x++)
			{
				int height = data.GetSurfaceHeight(x, z);
				minimumHeight = Math.Min(minimumHeight, height);
				maximumHeight = Math.Max(maximumHeight, height);
			}
		}

		Assert.InRange(minimumHeight, VoxelTestWorldGenerator.MinimumSurfaceHeight, 10);
		Assert.InRange(maximumHeight, 140, VoxelTestWorldGenerator.MaximumSurfaceHeight);
		Assert.True(data.LakeCount >= 2);
		Assert.True(data.WaterColumnCount >= VoxelTestWorldGenerator.MinimumLakeArea * 2);
		VoxelTestWorldGenerator.ValidateWaterContainment(data);
	}

	[Fact]
	public void GeneratedChunksAndLakeLocationsAreDeterministic()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestWorldData data = generated.Data;
		ChunkCoordinate[] coordinates =
		{
			new ChunkCoordinate(-40, -5, -40),
			new ChunkCoordinate(-8, 2, -6),
			new ChunkCoordinate(0, 4, 0),
			new ChunkCoordinate(39, 6, 39),
		};

		foreach (ChunkCoordinate coordinate in coordinates)
		{
			VoxelCell[] first = data.GenerateChunk(coordinate, generated.Materials);
			VoxelCell[] second = data.GenerateChunk(coordinate, generated.Materials);

			Assert.Equal(first, second);
		}

		Vector3 water = data.UnderwaterCameraPosition;
		Assert.NotNull(data.GetWaterSurface((int)MathF.Floor(water.X), (int)MathF.Floor(water.Z)));
	}

	[Fact]
	public void DryTerrainUsesGrassThenShallowDirtThenStone()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestWorldData data = generated.Data;
		(int x, int z) = FindColumn(data, requireWater: false);
		int surface = data.GetSurfaceHeight(x, z);

		Assert.Equal(generated.Materials.Grass, data.GetTerrainMaterial(x, surface, z, generated.Materials));
		Assert.Equal(generated.Materials.Dirt, data.GetTerrainMaterial(x, surface - 1, z, generated.Materials));
		Assert.Equal(generated.Materials.Dirt, data.GetTerrainMaterial(x, surface - 3, z, generated.Materials));
		Assert.Equal(generated.Materials.Stone, data.GetTerrainMaterial(x, surface - 4, z, generated.Materials));
		Assert.Equal((ushort)0, data.GetTerrainMaterial(x, surface + 1, z, generated.Materials));
	}

	[Fact]
	public void LakeColumnsUseDirtBedsStoneSubsurfaceAndWaterAboveTerrain()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestWorldData data = generated.Data;
		Vector3 underwater = data.UnderwaterCameraPosition;
		int x = (int)MathF.Floor(underwater.X);
		int z = (int)MathF.Floor(underwater.Z);
		int surface = data.GetSurfaceHeight(x, z);
		int waterSurface = data.GetWaterSurface(x, z).Value;

		Assert.True(waterSurface > surface);
		Assert.Equal(generated.Materials.Dirt, data.GetTerrainMaterial(x, surface, z, generated.Materials));
		Assert.Equal(generated.Materials.Dirt, data.GetTerrainMaterial(x, surface - 3, z, generated.Materials));
		Assert.Equal(generated.Materials.Stone, data.GetTerrainMaterial(x, surface - 4, z, generated.Materials));

		for (int y = surface + 1; y <= waterSurface; y++)
		{
			Assert.Equal(generated.Materials.Water, data.GetTerrainMaterial(x, y, z, generated.Materials));
		}

		Assert.Equal((ushort)0, data.GetTerrainMaterial(x, waterSurface + 1, z, generated.Materials));
	}

	[Fact]
	public void VerticalChunkRangesCoverEveryGeneratedWaterSurface()
	{
		VoxelTestWorldData data = Generated.Value.Data;

		for (
			int chunkZ = VoxelTestWorldGenerator.MinimumChunkCoordinate;
			chunkZ <= VoxelTestWorldGenerator.MaximumChunkCoordinate;
			chunkZ++
		)
		{
			for (
				int chunkX = VoxelTestWorldGenerator.MinimumChunkCoordinate;
				chunkX <= VoxelTestWorldGenerator.MaximumChunkCoordinate;
				chunkX++
			)
			{
				(int minimumChunkY, int maximumChunkY) = data.GetVerticalChunkRange(chunkX, chunkZ);
				int originX = chunkX * VoxelWorld.ChunkSize;
				int originZ = chunkZ * VoxelWorld.ChunkSize;

				for (int z = originZ; z < originZ + VoxelWorld.ChunkSize; z++)
				{
					for (int x = originX; x < originX + VoxelWorld.ChunkSize; x++)
					{
						if (data.GetWaterSurface(x, z) is int waterSurface)
						{
							ChunkCoordinate waterChunk = ChunkCoordinate.FromWorld(x, waterSurface, z, out _, out _, out _);
							Assert.InRange(waterChunk.Y, minimumChunkY, maximumChunkY);
						}
					}
				}
			}
		}
	}

	[Fact]
	public void StreamerLoadsNearestChunksWithinBudgetAndClipsAtWorldBoundary()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 1,
			unloadRadius: 2,
			generationBudget: 2
		);

		Assert.Equal(2, streamer.Update(new Vector3(0.5f, 100, 0.5f)));
		Assert.Equal((0, 0), streamer.GeneratedThisFrame[0]);
		Assert.Equal(2, streamer.LoadedHorizontalCount);
		Assert.Equal(7, streamer.PendingHorizontalCount);

		while (!streamer.IsSettled)
		{
			streamer.Update(new Vector3(0.5f, 100, 0.5f));
		}

		Assert.Equal(9, streamer.LoadedHorizontalCount);

		VoxelTestChunkStreamer boundary = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 1,
			unloadRadius: 2,
			generationBudget: 8
		);
		boundary.Update(new Vector3(639.5f, 100, 639.5f));

		Assert.True(boundary.IsSettled);
		Assert.Equal(4, boundary.LoadedHorizontalCount);
		Assert.All(
			boundary.World.LoadedChunks,
			chunk =>
			{
				Assert.InRange(chunk.Coordinate.X, 38, 39);
				Assert.InRange(chunk.Coordinate.Z, 38, 39);
			}
		);
	}

	[Fact]
	public void StreamerPreservesEditsAcrossUnloadAndRegeneration()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 0,
			unloadRadius: 0,
			generationBudget: 1
		);
		int x = 0;
		int z = 0;
		int y = generated.Data.GetSurfaceHeight(x, z);
		streamer.Update(new Vector3(0.5f, y + 10, 0.5f));

		Assert.False(streamer.World.GetVoxel(x, y, z).IsAir);
		Assert.True(streamer.SetVoxel(x, y, z, VoxelCell.Air));
		Assert.True(streamer.SetVoxel(x, 185, z, new VoxelCell(generated.Materials.Stone)));
		Assert.True(streamer.World.GetVoxel(x, y, z).IsAir);
		Assert.Equal(2, streamer.OverrideCount);

		streamer.Update(new Vector3(64.5f, y + 10, 0.5f));
		Assert.True(streamer.World.GetVoxel(x, y, z).IsAir);
		streamer.Update(new Vector3(0.5f, y + 10, 0.5f));

		Assert.True(streamer.World.GetVoxel(x, y, z).IsAir);
		Assert.Equal(generated.Materials.Stone, streamer.World.GetVoxel(x, 185, z).MaterialId);
		Assert.Equal(1, streamer.LoadedHorizontalCount);
	}

	[Fact]
	public void CrossChunkTreeGeometryDoesNotForceNeighborLoading()
	{
		GeneratedWorld generated = Generated.Value;
		(int X, int Y, int Z) root = generated.Data
			.GetTreeRoots(
				VoxelTestWorldGenerator.WorldMinimum + 2,
				VoxelTestWorldGenerator.WorldMinimum + 2,
				VoxelTestWorldGenerator.WorldMaximum - 3,
				VoxelTestWorldGenerator.WorldMaximum - 3
			)
			.First(item => IsNearChunkEdge(item.X) || IsNearChunkEdge(item.Z));
		int targetX = root.X;
		int targetZ = root.Z;

		if (PositiveModulo(root.X, VoxelWorld.ChunkSize) <= 1)
		{
			targetX -= 2;
		}
		else if (PositiveModulo(root.X, VoxelWorld.ChunkSize) >= 14)
		{
			targetX += 2;
		}
		else if (PositiveModulo(root.Z, VoxelWorld.ChunkSize) <= 1)
		{
			targetZ -= 2;
		}
		else
		{
			targetZ += 2;
		}

		ChunkCoordinate rootChunk = ChunkCoordinate.FromWorld(root.X, root.Y, root.Z, out _, out _, out _);
		ChunkCoordinate targetChunk = ChunkCoordinate.FromWorld(targetX, root.Y + 4, targetZ, out _, out _, out _);
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 0,
			unloadRadius: 1,
			generationBudget: 1
		);
		streamer.Update(new Vector3(root.X + 0.5f, root.Y + 10, root.Z + 0.5f));

		Assert.Equal(1, streamer.LoadedHorizontalCount);
		Assert.DoesNotContain(
			streamer.World.LoadedChunks,
			chunk => chunk.Coordinate.X == targetChunk.X && chunk.Coordinate.Z == targetChunk.Z
		);

		streamer.Update(new Vector3(targetX + 0.5f, root.Y + 10, targetZ + 0.5f));

		Assert.Equal(generated.Materials.Leaves, streamer.World.GetVoxel(targetX, root.Y + 4, targetZ).MaterialId);
		Assert.NotEqual(rootChunk, targetChunk);
	}

	[Fact]
	public void RollingFrameRateUsesOnlyRecentSamples()
	{
		RollingFrameRateCounter counter = new RollingFrameRateCounter(0.5);

		for (int i = 1; i <= 30; i++)
		{
			counter.Update(i / 60.0, 1 / 60.0);
		}

		Assert.InRange(counter.FramesPerSecond, 59.9, 60.1);
		Assert.InRange(counter.FrameMilliseconds, 16.6, 16.7);
		Assert.Equal(30, counter.SampleCount);

		counter.Update(1.1, 0.1);

		Assert.True(counter.SampleCount < 30);
		Assert.True(counter.FramesPerSecond < 60);
		Assert.Throws<ArgumentOutOfRangeException>(() => counter.Update(2, 0));
	}

	[Fact]
	public void StreamerClampsCameraToExpandedWorldBounds()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(generated.Data, generated.Materials);
		Vector3 clamped = streamer.ClampPosition(new Vector3(-10000, 10000, 10000));

		Assert.InRange(clamped.X, -640, -639.9f);
		Assert.InRange(clamped.Y, 191.9f, 192);
		Assert.InRange(clamped.Z, 639.9f, 640);
	}

	[Fact]
	public void CameraAwareStreamingLoadsTheVisibleHaloBeforeBackgroundColumns()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 4,
			unloadRadius: 4,
			generationBudget: 1
		);
		Camera camera = new Camera();
		camera.Position = new Vector3(8, 8, 8);
		camera.SetPerspective(1920, 1080, MathF.PI / 2, 0.1f, 500);
		ChunkCoordinate center = ChunkCoordinate.FromWorld(
			(int)MathF.Floor(camera.Position.X),
			0,
			(int)MathF.Floor(camera.Position.Z),
			out _,
			out _,
			out _
		);
		List<(int X, int Z)> order = new List<(int, int)>();

		do
		{
			streamer.Update(camera, 108);
			order.AddRange(streamer.GeneratedThisFrame);
		}
		while (!streamer.IsSettled);

		int visibleIndex = order.IndexOf((center.X, center.Z - 3));
		int backgroundIndex = order.IndexOf((center.X, center.Z + 3));

		Assert.InRange(visibleIndex, 0, order.Count - 1);
		Assert.InRange(backgroundIndex, 0, order.Count - 1);
		Assert.True(
			visibleIndex < backgroundIndex,
			$"Visible index {visibleIndex}, background index {backgroundIndex}."
		);
	}

	[Fact]
	public void CameraAwareStreamingLightsOnlyVisibleAndHaloColumnsOnDemand()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new(
			generated.Data,
			generated.Materials,
			loadRadius: 4,
			unloadRadius: 4,
			generationBudget: 128
		);
		using VoxelLighting lighting = new(streamer.World, generated.Palette);
		streamer.AttachLighting(lighting);
		Camera camera = CreateStreamingCamera(Vector3.Zero, -Vector3.UnitZ);

		streamer.Update(camera, 108);
		Assert.Equal(new[] { (0, 0) }, streamer.PromotedLightingThisFrame);
		DrainFocusedLighting(lighting);
		SettleFocusedStreaming(streamer, lighting, camera);

		Assert.Equal(81, streamer.LoadedHorizontalCount);
		Assert.InRange(streamer.LitHorizontalCount, 1, 80);
		VoxelChunk[] background = streamer.World.LoadedChunks.Where(chunk =>
			chunk.Coordinate.X == 0 && chunk.Coordinate.Z == 3).ToArray();
		Assert.NotEmpty(background);
		Assert.All(
			background,
			chunk => Assert.False(lighting.IsResident(chunk.Coordinate))
		);
		Assert.True(lighting.IsIdle);
	}

	[Fact]
	public void FocusedLightingPromotesEditedColumnsAndCachesThemUntilWorldUnload()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new(
			generated.Data,
			generated.Materials,
			loadRadius: 4,
			unloadRadius: 4,
			generationBudget: 128
		);
		using VoxelLighting lighting = new(streamer.World, generated.Palette);
		streamer.AttachLighting(lighting);
		Camera camera = CreateStreamingCamera(Vector3.Zero, -Vector3.UnitZ);
		SettleFocusedStreaming(streamer, lighting, camera);
		ChunkCoordinate cached = streamer.World.LoadedChunks
			.Select(chunk => chunk.Coordinate)
			.First(coordinate => coordinate.X == 0 && coordinate.Z == -3);
		int editedX = 1;
		int editedY = 185;
		int editedZ = 3 * VoxelWorld.ChunkSize + 1;
		ChunkCoordinate edited = ChunkCoordinate.FromWorld(
			editedX,
			editedY,
			editedZ,
			out _,
			out _,
			out _
		);

		Assert.True(streamer.SetVoxel(
			editedX,
			editedY,
			editedZ,
			new VoxelCell(generated.Materials.Stone)
		));
		Assert.False(lighting.IsResident(edited));

		camera.LookAt(camera.Position + Vector3.UnitZ);
		SettleFocusedStreaming(streamer, lighting, camera);

		Assert.True(lighting.IsResident(edited));
		Assert.True(lighting.IsResident(cached));
		Assert.Equal(
			generated.Materials.Stone,
			streamer.World.GetVoxel(editedX, editedY, editedZ).MaterialId
		);

		camera.Position = new Vector3(10 * VoxelWorld.ChunkSize + 8, 96, 8);
		camera.LookAt(camera.Position - Vector3.UnitZ);
		streamer.Update(camera, 108);

		Assert.False(lighting.IsResident(cached));
		Assert.DoesNotContain(
			streamer.World.LoadedChunks,
			chunk => chunk.Coordinate.X == cached.X && chunk.Coordinate.Z == cached.Z
		);
	}

	private static Camera CreateStreamingCamera(Vector3 position, Vector3 direction)
	{
		Camera camera = new();
		camera.Position = position + new Vector3(8, 96, 8);
		camera.SetPerspective(1920, 1080, MathF.PI / 2, 0.1f, 500);
		camera.LookAt(camera.Position + direction);
		return camera;
	}

	private static void SettleFocusedStreaming(
		VoxelTestChunkStreamer streamer,
		VoxelLighting lighting,
		Camera camera
	)
	{
		int frames = 0;

		do
		{
			streamer.Update(camera, 108);
			Assert.InRange(streamer.PromotedLightingThisFrame.Count, 0, 4);
			DrainFocusedLighting(lighting);
			frames++;
		}
		while ((!streamer.IsSettled
		|| streamer.PendingLightingHorizontalCount > 0) && frames < 200);

		Assert.True(streamer.IsSettled);
		Assert.Equal(0, streamer.PendingLightingHorizontalCount);
	}

	private static void DrainFocusedLighting(VoxelLighting lighting)
	{
		int updates = 0;

		while (!lighting.IsIdle && updates < 20_000)
		{
			lighting.Update(65_536);
			updates++;
		}

		Assert.True(lighting.IsIdle);
	}

	private static GeneratedWorld CreateGeneratedWorld()
	{
		VoxelPalette palette = VoxelTestWorldGenerator.CreatePalette(
			VoxelTestCompatibilityAssets.LoadModels(),
			out VoxelTestMaterialIds materials
		);
		return new GeneratedWorld(palette, materials, VoxelTestWorldGenerator.Generate(materials));
	}

	private static (int X, int Z) FindColumn(VoxelTestWorldData data, bool requireWater)
	{
		for (int z = VoxelTestWorldGenerator.WorldMinimum + 1; z < VoxelTestWorldGenerator.WorldMaximum - 1; z++)
		{
			for (int x = VoxelTestWorldGenerator.WorldMinimum + 1; x < VoxelTestWorldGenerator.WorldMaximum - 1; x++)
			{
				if (data.GetWaterSurface(x, z).HasValue == requireWater)
				{
					return (x, z);
				}
			}
		}

		throw new InvalidOperationException("No matching terrain column was generated.");
	}

	private static bool IsNearChunkEdge(int coordinate)
	{
		int local = PositiveModulo(coordinate, VoxelWorld.ChunkSize);
		return local <= 1 || local >= 14;
	}

	private static int PositiveModulo(int value, int divisor)
	{
		int remainder = value % divisor;
		return remainder < 0 ? remainder + divisor : remainder;
	}

	private sealed class GeneratedWorld
	{
		internal GeneratedWorld(
			VoxelPalette palette,
			VoxelTestMaterialIds materials,
			VoxelTestWorldData data
		)
		{
			Palette = palette;
			Materials = materials;
			Data = data;
		}

		internal VoxelPalette Palette { get; }
		internal VoxelTestMaterialIds Materials { get; }
		internal VoxelTestWorldData Data { get; }
	}
}
