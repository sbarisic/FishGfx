using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelColumnTransactionTests
{
	[Fact]
	public void EmptyPreparedChunkRemainsResidentForPresentationCompletion()
	{
		VoxelWorld world = new();
		ChunkCoordinate coordinate = new(0, 4, 0);
		using VoxelColumnUpdate update = world.BeginColumnUpdate(0, 0, 1);
		world.InstallPreparedChunk(
			update,
			coordinate,
			PreparedVoxelChunk.TakeOwnership(new VoxelCell[VoxelWorld.ChunkVolume]));

		world.CompleteColumnUpdate(update);

		Assert.True(world.TryGetChunk(coordinate, out VoxelChunk chunk));
		Assert.True(chunk.IsEmpty);
	}

	[Fact]
	public void FailedColumnCompletionPreservesPreviousChunkAndReleasesBarrierOnDispose()
	{
		VoxelWorld world = new();
		world.SetVoxel(0, 0, 0, new VoxelCell(1));
		VoxelColumnUpdate update = world.BeginColumnUpdate(0, 0, 7);
		VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
		replacement[0] = new VoxelCell(2);
		PreparedVoxelChunk invalid = PreparedVoxelChunk.TakeOwnership(replacement);
		invalid.Dispose();
		world.InstallPreparedChunk(update, new ChunkCoordinate(0, 0, 0), invalid);

		Assert.Throws<ObjectDisposedException>(() => world.CompleteColumnUpdate(update));
		Assert.Equal((ushort)1, world.GetVoxel(0, 0, 0).MaterialId);
		update.Dispose();

		using VoxelColumnUpdate retry = world.BeginColumnUpdate(0, 0, 8);
	}
}
