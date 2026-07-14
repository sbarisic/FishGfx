using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTests
{
	[Fact]
	public void DeferredWorldSnapshotKeepsCapturedFacesEdgesAndCorners()
	{
		(VoxelWorld world, _, ushort opaque, ushort cutout, _) = CreateWorldAndPalette();
		ChunkCoordinate coordinate = new(-1, 0, -1);
		world.SetVoxel(-1, 4, -1, new VoxelCell(opaque));
		world.SetVoxel(-1, 16, -1, new VoxelCell(cutout));
		VoxelChunkSnapshotSource source = world.CaptureSnapshotSource(coordinate);

		world.SetVoxel(-1, 4, -1, VoxelCell.Air);
		world.SetVoxel(-1, 16, -1, VoxelCell.Air);
		VoxelChunkSnapshot captured = source.Materialize();
		VoxelChunkSnapshot current = world.CreateSnapshot(coordinate);

		Assert.Equal(opaque, captured.GetMaterial(15, 4, 15));
		Assert.Equal(cutout, captured.GetMaterial(15, 16, 15));
		Assert.Equal(0, current.GetMaterial(15, 4, 15));
		Assert.Equal(0, current.GetMaterial(15, 16, 15));
	}

	[Fact]
	public void DeferredLightSnapshotKeepsCapturedPublishedNeighborhood()
	{
		VoxelPaletteBuilder builder = new();
		ushort emitter = builder.Add(
			new VoxelMaterial(
				"Emitter",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(0),
				light: new VoxelMaterialLightSettings(
					15,
					new VoxelBlockLight(15, 4, 2)
				)
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		ChunkCoordinate coordinate = new(0, 0, 0);
		world.SetVoxel(15, 15, 15, new VoxelCell(emitter));
		using VoxelLighting lighting = new(world, palette);
		lighting.LoadChunk(coordinate);
		lighting.LoadChunk(new ChunkCoordinate(1, 1, 1));
		DrainCapturedLighting(lighting);
		Assert.True(lighting.TryCaptureSnapshotSource(coordinate, out VoxelLightChunkSnapshotSource source));

		world.SetVoxel(15, 15, 15, VoxelCell.Air);
		DrainCapturedLighting(lighting);
		VoxelLight captured = source.Materialize().GetLight(15, 15, 15);
		Assert.True(lighting.TryCreateSnapshot(coordinate, out VoxelLightChunkSnapshot current));

		Assert.Equal(15, captured.Block.Red);
		Assert.Equal(0, current.GetLight(15, 15, 15).Block.Red);
	}

	private static void DrainCapturedLighting(VoxelLighting lighting)
	{
		int updates = 0;

		while (!lighting.IsIdle && updates < 20_000)
		{
			lighting.Update();
			updates++;
		}

		Assert.True(lighting.IsIdle);
	}
}
