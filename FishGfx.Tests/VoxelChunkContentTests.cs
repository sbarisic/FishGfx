using System.Linq;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTests
{
	[Fact]
	public void ChunkContentCapturesImplicitAndUniformMaterialRuns()
	{
		VoxelWorld world = new VoxelWorld();
		ChunkCoordinate missing = new ChunkCoordinate(1, 2, 3);
		VoxelChunkContent implicitAir = world.CaptureChunkContent(missing);

		Assert.True(implicitAir.IsImplicitAir);
		AssertSingleRun(implicitAir, 0, VoxelWorld.ChunkVolume);

		ChunkCoordinate filled = new ChunkCoordinate(0, 0, 0);
		world.FillChunk(filled, new VoxelCell(7));
		VoxelChunkContent uniform = world.CaptureChunkContent(filled);

		Assert.False(uniform.IsImplicitAir);
		AssertSingleRun(uniform, 7, VoxelWorld.ChunkVolume);
	}

	[Fact]
	public void ChunkContentCapturesLayeredRunsAndBoundsHighEntropyMetadata()
	{
		VoxelWorld world = new VoxelWorld();
		ChunkCoordinate layeredCoordinate = new ChunkCoordinate(0, 0, 0);
		VoxelCell[] layered = Enumerable.Repeat(
			new VoxelCell(1),
			VoxelWorld.ChunkVolume
		).ToArray();
		for (int index = VoxelWorld.ChunkVolume / 2; index < layered.Length; index++)
		{
			layered[index] = new VoxelCell(2);
		}

		world.SetChunk(layeredCoordinate, layered);
		ReadOnlySpan<VoxelMaterialRun> runs = world
			.CaptureChunkContent(layeredCoordinate)
			.MaterialRuns
			.Span;
		Assert.Equal(2, runs.Length);
		Assert.Equal((ushort)1, runs[0].MaterialId);
		Assert.Equal(VoxelWorld.ChunkVolume / 2, runs[0].Length);
		Assert.Equal((ushort)2, runs[1].MaterialId);
		Assert.Equal(VoxelWorld.ChunkVolume / 2, runs[1].Length);

		ChunkCoordinate noisyCoordinate = new ChunkCoordinate(1, 0, 0);
		VoxelCell[] noisy = new VoxelCell[VoxelWorld.ChunkVolume];
		for (int index = 0; index < noisy.Length; index++)
		{
			noisy[index] = new VoxelCell((ushort)(index % 2 + 1));
		}

		world.SetChunk(noisyCoordinate, noisy);
		Assert.False(world.CaptureChunkContent(noisyCoordinate).HasMaterialRuns);
	}

	[Fact]
	public void IndividualEditsInvalidateRunsButKnownEmptyChunksRetainAirRun()
	{
		VoxelWorld world = new VoxelWorld();
		ChunkCoordinate coordinate = new ChunkCoordinate(0, 0, 0);
		world.FillChunk(coordinate, new VoxelCell(3));

		world.SetVoxel(1, 2, 3, new VoxelCell(4));
		Assert.False(world.CaptureChunkContent(coordinate).HasMaterialRuns);

		ChunkCoordinate sparse = new ChunkCoordinate(1, 0, 0);
		world.SetVoxel(16, 0, 0, new VoxelCell(1));
		world.SetVoxel(16, 0, 0, VoxelCell.Air);
		VoxelChunkContent empty = world.CaptureChunkContent(sparse);

		AssertSingleRun(empty, 0, VoxelWorld.ChunkVolume);
	}

	private static void AssertSingleRun(
		VoxelChunkContent content,
		ushort materialId,
		int length
	)
	{
		Assert.True(content.HasMaterialRuns);
		Assert.Equal(1, content.MaterialRuns.Length);
		Assert.Equal(materialId, content.MaterialRuns.Span[0].MaterialId);
		Assert.Equal(length, content.MaterialRuns.Span[0].Length);
	}
}
