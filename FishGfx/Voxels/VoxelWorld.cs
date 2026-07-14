using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FishGfx.Voxels;

public sealed class VoxelWorld
{
	public const int ChunkSize = 16;
	public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;
	private static readonly ReadOnlyMemory<VoxelCell> EmptyChunkCells =
		new VoxelCell[ChunkVolume];

	private readonly object sync = new object();
	private readonly Dictionary<ChunkCoordinate, VoxelChunk> chunks =
		new Dictionary<ChunkCoordinate, VoxelChunk>();
	private long nextChunkGeneration;

	public event Action<ChunkCoordinate, long> ChunkInvalidated;
	public event Action<ChunkCoordinate> ChunkRemoved;
	internal event Action<VoxelWorldContentChange> ContentChanged;

	public int LoadedChunkCount
	{
		get
		{
			lock (sync)
			{
				return chunks.Count;
			}
		}
	}

	public IReadOnlyList<VoxelChunk> LoadedChunks
	{
		get
		{
			lock (sync)
			{
				return Array.AsReadOnly(chunks.Values.ToArray());
			}
		}
	}

	public bool TryGetChunk(ChunkCoordinate coordinate, out VoxelChunk chunk)
	{
		lock (sync)
		{
			return chunks.TryGetValue(coordinate, out chunk);
		}
	}

	public VoxelCell GetVoxel(int x, int y, int z)
	{
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, y, z, out int localX, out int localY, out int localZ);

		lock (sync)
		{
			return GetVoxelUnchecked(coordinate, localX, localY, localZ);
		}
	}

	public bool SetVoxel(int x, int y, int z, VoxelCell value)
	{
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, y, z, out int localX, out int localY, out int localZ);
		List<(ChunkCoordinate Coordinate, long Revision)> invalidated;
		ushort previousMaterialId;

		lock (sync)
		{
			if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
			{
				if (value.IsAir)
				{
					return false;
				}

				chunk = CreateChunk(coordinate);
				chunks.Add(coordinate, chunk);
			}

			VoxelCell previous = chunk.GetLocalUnchecked(localX, localY, localZ);
			if (!chunk.SetLocalUnchecked(localX, localY, localZ, value))
			{
				return false;
			}

			previousMaterialId = previous.MaterialId;

			invalidated = InvalidateBoundaryNeighborhood(coordinate, localX, localY, localZ);
		}

		RaiseInvalidated(invalidated);
		ContentChanged?.Invoke(VoxelWorldContentChange.Single(
			coordinate,
			localX + ChunkSize * (localY + ChunkSize * localZ),
			previousMaterialId,
			value.MaterialId
		));
		return true;
	}

	public void FillChunk(ChunkCoordinate coordinate, VoxelCell value)
	{
		List<(ChunkCoordinate Coordinate, long Revision)> invalidated;

		lock (sync)
		{
			if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
			{
				if (value.IsAir)
				{
					return;
				}

				chunk = CreateChunk(coordinate);
				chunks.Add(coordinate, chunk);
			}

			chunk.FillUnchecked(value);
			invalidated = InvalidateAllNeighbors(coordinate, includeCenter: true);
		}

		RaiseInvalidated(invalidated);
		ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
	}

	public bool SetChunk(ChunkCoordinate coordinate, ReadOnlySpan<VoxelCell> cells)
	{
		if (cells.Length != ChunkVolume)
		{
			throw new ArgumentException($"Chunk data must contain exactly {ChunkVolume} voxels.", nameof(cells));
		}

		bool hasNonAir = false;

		for (int i = 0; i < cells.Length; i++)
		{
			if (!cells[i].IsAir)
			{
				hasNonAir = true;
				break;
			}
		}

		if (!hasNonAir)
		{
			return RemoveChunk(coordinate);
		}

		List<(ChunkCoordinate Coordinate, long Revision)> invalidated;

		lock (sync)
		{
			if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
			{
				chunk = CreateChunk(coordinate);
				chunks.Add(coordinate, chunk);
			}

			if (!chunk.ReplaceUnchecked(cells))
			{
				return false;
			}

			invalidated = InvalidateAllNeighbors(coordinate, includeCenter: true);
		}

		RaiseInvalidated(invalidated);
		ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
		return true;
	}

	public bool RemoveChunk(ChunkCoordinate coordinate)
	{
		List<(ChunkCoordinate Coordinate, long Revision)> invalidated;

		lock (sync)
		{
			if (!chunks.Remove(coordinate))
			{
				return false;
			}

			invalidated = InvalidateAllNeighbors(coordinate, includeCenter: false);
		}

		RaiseInvalidated(invalidated);
		ChunkRemoved?.Invoke(coordinate);
		ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
		return true;
	}

	public int RemoveEmptyChunks()
	{
		ChunkCoordinate[] empty;

		lock (sync)
		{
			empty = chunks.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToArray();
		}

		foreach (ChunkCoordinate coordinate in empty)
		{
			RemoveChunk(coordinate);
		}

		return empty.Length;
	}

	public VoxelChunkSnapshot CreateSnapshot(ChunkCoordinate coordinate)
	{
		VoxelChunkSnapshotSource source = CaptureSnapshotSource(coordinate);
		return source?.Materialize();
	}

	internal VoxelChunkSnapshotSource CaptureSnapshotSource(
		ChunkCoordinate coordinate
	)
	{
		lock (sync)
		{
			if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
			{
				return null;
			}

			VoxelChunkContent[] contents = new VoxelChunkContent[27];

			for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
			{
				for (int offsetY = -1; offsetY <= 1; offsetY++)
				{
					for (int offsetX = -1; offsetX <= 1; offsetX++)
					{
						ChunkCoordinate sampleCoordinate = coordinate + new ChunkCoordinate(
							offsetX,
							offsetY,
							offsetZ
						);
						int index = offsetX + 1 + 3 * (offsetY + 1 + 3 * (offsetZ + 1));

						contents[index] = chunks.TryGetValue(sampleCoordinate, out VoxelChunk sample)
							? new VoxelChunkContent(
								sample.CaptureCellsUnchecked(),
								sample.CaptureMaterialRunsUnchecked(),
								isImplicitAir: false
							)
							: new VoxelChunkContent(
								EmptyChunkCells,
								VoxelChunk.EmptyMaterialRuns,
								isImplicitAir: true
							);
					}
				}
			}

			return new VoxelChunkSnapshotSource(
				coordinate,
				chunk.Generation,
				chunk.Revision,
				contents
			);
		}
	}

	private VoxelChunk CreateChunk(ChunkCoordinate coordinate)
	{
		return new VoxelChunk(coordinate, checked(++nextChunkGeneration));
	}

	internal ReadOnlyMemory<VoxelCell> CaptureChunkCells(ChunkCoordinate coordinate)
	{
		return CaptureChunkContent(coordinate).Cells;
	}

	internal ReadOnlyMemory<VoxelCell> CaptureChunkCells(
		ChunkCoordinate coordinate,
		out bool isImplicitAir
	)
	{
		VoxelChunkContent content = CaptureChunkContent(coordinate);
		isImplicitAir = content.IsImplicitAir;
		return content.Cells;
	}

	internal VoxelChunkContent CaptureChunkContent(ChunkCoordinate coordinate)
	{
		lock (sync)
		{
			if (chunks.TryGetValue(coordinate, out VoxelChunk chunk))
			{
				return new VoxelChunkContent(
					chunk.CaptureCellsUnchecked(),
					chunk.CaptureMaterialRunsUnchecked(),
					isImplicitAir: false
				);
			}

			return new VoxelChunkContent(
				EmptyChunkCells,
				VoxelChunk.EmptyMaterialRuns,
				isImplicitAir: true
			);
		}
	}

	private VoxelCell GetVoxelWorldUnchecked(int x, int y, int z)
	{
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, y, z, out int localX, out int localY, out int localZ);
		return GetVoxelUnchecked(coordinate, localX, localY, localZ);
	}

	private VoxelCell GetVoxelUnchecked(ChunkCoordinate coordinate, int localX, int localY, int localZ)
	{
		return chunks.TryGetValue(coordinate, out VoxelChunk chunk)
			? chunk.GetLocalUnchecked(localX, localY, localZ)
			: VoxelCell.Air;
	}

	private List<(ChunkCoordinate Coordinate, long Revision)> InvalidateBoundaryNeighborhood(
		ChunkCoordinate coordinate,
		int localX,
		int localY,
		int localZ
	)
	{
		int minX = localX == 0 ? -1 : 0;
		int maxX = localX == ChunkSize - 1 ? 1 : 0;
		int minY = localY == 0 ? -1 : 0;
		int maxY = localY == ChunkSize - 1 ? 1 : 0;
		int minZ = localZ == 0 ? -1 : 0;
		int maxZ = localZ == ChunkSize - 1 ? 1 : 0;
		List<(ChunkCoordinate Coordinate, long Revision)> result = new List<(ChunkCoordinate, long)>();

		for (int z = minZ; z <= maxZ; z++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				for (int x = minX; x <= maxX; x++)
				{
					InvalidateExisting(coordinate + new ChunkCoordinate(x, y, z), result);
				}
			}
		}

		return result;
	}

	private List<(ChunkCoordinate Coordinate, long Revision)> InvalidateAllNeighbors(
		ChunkCoordinate coordinate,
		bool includeCenter
	)
	{
		List<(ChunkCoordinate Coordinate, long Revision)> result = new List<(ChunkCoordinate, long)>();

		for (int z = -1; z <= 1; z++)
		{
			for (int y = -1; y <= 1; y++)
			{
				for (int x = -1; x <= 1; x++)
				{
					if (!includeCenter && x == 0 && y == 0 && z == 0)
					{
						continue;
					}

					InvalidateExisting(coordinate + new ChunkCoordinate(x, y, z), result);
				}
			}
		}

		return result;
	}

	private void InvalidateExisting(
		ChunkCoordinate coordinate,
		List<(ChunkCoordinate Coordinate, long Revision)> result
	)
	{
		if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
		{
			return;
		}

		chunk.Revision++;
		result.Add((coordinate, chunk.Revision));
	}

	private void RaiseInvalidated(List<(ChunkCoordinate Coordinate, long Revision)> invalidated)
	{
		foreach ((ChunkCoordinate coordinate, long revision) in invalidated)
		{
			ChunkInvalidated?.Invoke(coordinate, revision);
		}
	}

	private readonly struct Vector3Int
	{
		public Vector3Int(int x, int y, int z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public int X { get; }
		public int Y { get; }
		public int Z { get; }
	}
}
