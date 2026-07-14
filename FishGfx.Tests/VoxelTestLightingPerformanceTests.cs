using System;
using System.Linq;
using FishGfx.Graphics;
using FishGfx.Voxels;
using FishGfx.VoxelTest;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTestWorldTests
{
	[Fact]
	public void ShowcaseVisibleHaloLightingSettlesBeforeBackgroundStreaming()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 8,
			unloadRadius: 10,
			generationBudget: 4
		);
		using VoxelLighting lighting = new VoxelLighting(
			streamer.World,
			generated.Palette,
			new VoxelLightingOptions
			{
				UpdateBudget = 8_192,
			}
		);
		streamer.AttachLighting(lighting);
		Camera camera = new();
		camera.SetPerspective(1920, 1080, MathF.PI / 2.2f, 0.1f, 500);
		camera.Position = generated.Data.ShowcaseCameraPosition;
		camera.LookAt(generated.Data.ShowcaseTarget);
		ChunkCoordinate cameraChunk = ChunkCoordinate.FromWorld(
			(int)MathF.Floor(camera.Position.X),
			0,
			(int)MathF.Floor(camera.Position.Z),
			out _,
			out _,
			out _
		);
		int frames = 0;
		long processed = 0;
		bool visiblePublishedBeforeBackground = false;

		do
		{
			streamer.Update(camera, 108);
			processed += lighting.Update();

			if (!streamer.IsSettled
				&& streamer.World.LoadedChunks.Any(chunk =>
					chunk.Coordinate.X == cameraChunk.X
					&& chunk.Coordinate.Z == cameraChunk.Z
					&& lighting.TryGetChunkState(
						chunk.Coordinate,
						out _,
						out _
					)))
			{
				visiblePublishedBeforeBackground = true;
			}

			frames++;
		}
		while ((!streamer.IsSettled
			|| streamer.PendingLightingHorizontalCount > 0
			|| !lighting.IsIdle) && frames < 500);

		Assert.True(streamer.IsSettled);
		Assert.True(lighting.IsIdle);
		Assert.True(visiblePublishedBeforeBackground);
		Assert.InRange(streamer.LitHorizontalCount, 1, streamer.LoadedHorizontalCount - 1);
		Assert.InRange(processed, 1, 4_000_000);
		Assert.InRange(frames, 1, 500);
	}

	[Fact]
	public void ShowcaseInteriorTerrainAvoidsWorkerMeshing()
	{
		GeneratedWorld generated = Generated.Value;
		VoxelTestChunkStreamer streamer = new VoxelTestChunkStreamer(
			generated.Data,
			generated.Materials,
			loadRadius: 8,
			unloadRadius: 10,
			generationBudget: 289
		);
		streamer.Update(generated.Data.ShowcaseCameraPosition);
		using VoxelMeshingScheduler scheduler = new VoxelMeshingScheduler(
			streamer.World,
			generated.Palette,
			VoxelTestCompatibilityAssets.AtlasLayout,
			maxWorkers: 1
		);
		int suppressed = streamer.World.LoadedChunks.Count(
			chunk => scheduler.IsProvablyOccluded(chunk.Coordinate)
		);

		Assert.InRange(suppressed, 1_250, 1_400);
		Assert.True(streamer.World.LoadedChunkCount - suppressed <= 1_500);
	}
}
