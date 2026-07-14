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
	public void CapturedChunkCellsRemainImmutableAcrossLaterWorldEdits()
	{
		VoxelWorld world = new();
		ChunkCoordinate coordinate = new(0, 0, 0);
		int index = 1 + VoxelWorld.ChunkSize * (2 + VoxelWorld.ChunkSize * 3);
		world.SetVoxel(1, 2, 3, new VoxelCell(1));
		ReadOnlyMemory<VoxelCell> captured = world.CaptureChunkCells(coordinate);

		world.SetVoxel(1, 2, 3, new VoxelCell(2));
		ReadOnlyMemory<VoxelCell> current = world.CaptureChunkCells(coordinate);

		Assert.Equal(new VoxelCell(1), captured.Span[index]);
		Assert.Equal(new VoxelCell(2), current.Span[index]);
		Assert.Equal(VoxelWorld.ChunkVolume, captured.Length);
		Assert.Equal(VoxelWorld.ChunkVolume, current.Length);
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
}
