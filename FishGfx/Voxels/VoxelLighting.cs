using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting : IDisposable
{
	private readonly object sync = new object();
	private readonly VoxelWorld world;
	private readonly VoxelPalette palette;
	private readonly ushort[] materialSignatureLookup;
	private readonly Dictionary<ushort, ushort[]> uniformMaterialSignatures =
		new Dictionary<ushort, ushort[]>();
	private readonly int updateBudget;
	private readonly Dictionary<ChunkCoordinate, ResidentChunk> residents =
		new Dictionary<ChunkCoordinate, ResidentChunk>();
	private readonly HashSet<ChunkCoordinate> dirtyWorldChunks =
		new HashSet<ChunkCoordinate>();
	private readonly Dictionary<ChunkCoordinate, HashSet<int>> dirtyWorldCells =
		new Dictionary<ChunkCoordinate, HashSet<int>>();
	private readonly HashSet<ChunkCoordinate> addedChunks =
		new HashSet<ChunkCoordinate>();
	private readonly HashSet<ChunkCoordinate> removedChunks =
		new HashSet<ChunkCoordinate>();
	private readonly HashSet<ChunkCoordinate> skyChangedChunks =
		new HashSet<ChunkCoordinate>();
	private readonly Dictionary<ChunkCoordinate, RemovedChunkTombstone> removedTombstones =
		new Dictionary<ChunkCoordinate, RemovedChunkTombstone>();
	private RebuildTransaction transaction;
	private IncrementalTransaction incrementalTransaction;
	private bool fullRebuildRequested;
	private bool disposed;
	private long nextChunkGeneration;

	public VoxelLighting(
		VoxelWorld world,
		VoxelPalette palette,
		VoxelLightingOptions options = null
	)
	{
		this.world = world ?? throw new ArgumentNullException(nameof(world));
		this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
		materialSignatureLookup = CreateMaterialSignatureLookup(palette);
		updateBudget = (options ?? new VoxelLightingOptions()).UpdateBudget;
		world.ContentChanged += HandleWorldContentChanged;
	}

	internal event Action<ChunkCoordinate, long> ChunkInvalidated;

	public bool IsIdle
	{
		get
		{
			lock (sync)
			{
				ThrowIfDisposed();
				return transaction == null
					&& incrementalTransaction == null
					&& !fullRebuildRequested
					&& !HasPendingIncrementalChanges();
			}
		}
	}

	public int PendingCount
	{
		get
		{
			lock (sync)
			{
				ThrowIfDisposed();
				long count = dirtyWorldChunks.Count
					+ GetPendingExactCellCount()
					+ addedChunks.Count
					+ removedChunks.Count
					+ skyChangedChunks.Count
					+ (fullRebuildRequested ? 1 : 0);
				if (transaction != null)
				{
					count += transaction.PendingCount;
				}

				if (incrementalTransaction != null)
				{
					count += incrementalTransaction.PendingCount;
				}

				return count > int.MaxValue ? int.MaxValue : (int)count;
			}
		}
	}

	public int ResidentChunkCount
	{
		get
		{
			lock (sync)
			{
				ThrowIfDisposed();
				return residents.Count;
			}
		}
	}

	public void LoadChunk(ChunkCoordinate coordinate, bool skyExposedAbove = false)
	{
		lock (sync)
		{
			ThrowIfDisposed();

			if (residents.TryGetValue(coordinate, out ResidentChunk existing))
			{
				if (existing.SkyExposedAbove == skyExposedAbove)
				{
					return;
				}

				existing.SkyExposedAbove = skyExposedAbove;
				skyChangedChunks.Add(coordinate);
			}
			else
			{
				ResidentChunk resident = new ResidentChunk(
					coordinate,
					skyExposedAbove,
					++nextChunkGeneration
				);
				residents.Add(coordinate, resident);
				addedChunks.Add(coordinate);
			}
		}
	}

	public bool UnloadChunk(ChunkCoordinate coordinate)
	{
		lock (sync)
		{
			ThrowIfDisposed();

			if (!residents.TryGetValue(coordinate, out ResidentChunk removedChunk))
			{
				return false;
			}

			if (removedChunk.PublishedLights != null)
			{
				removedTombstones[coordinate] = new RemovedChunkTombstone(removedChunk);
			}

			bool wasPublished = removedChunk.PublishedLights != null;
			bool wasInFlight = transaction?.Lookup.ContainsKey(coordinate) == true
				|| incrementalTransaction?.Chunks.ContainsKey(coordinate) == true;
			bool restartFullRebuild = transaction != null;
			residents.Remove(coordinate);
			dirtyWorldChunks.Remove(coordinate);
			dirtyWorldCells.Remove(coordinate);
			skyChangedChunks.Remove(coordinate);
			bool wasPendingAddition = addedChunks.Remove(coordinate);
			if (wasPublished || wasInFlight || !wasPendingAddition)
			{
				removedChunks.Add(coordinate);
			}

			if (restartFullRebuild)
			{
				transaction = null;
				fullRebuildRequested = residents.Count != 0;
			}
			else if (incrementalTransaction != null)
			{
				incrementalTransaction.InvalidatedSourceCoordinates.Add(coordinate);
				if (incrementalTransaction.Chunks.ContainsKey(coordinate)
					|| incrementalTransaction.ReferencedSourceCoordinates.Contains(coordinate))
				{
					incrementalTransaction.DiscardAtCommit = true;
				}
			}

			return true;
		}
	}

	public void SetSkyExposedAbove(ChunkCoordinate coordinate, bool skyExposedAbove)
	{
		lock (sync)
		{
			ThrowIfDisposed();

			if (!residents.TryGetValue(coordinate, out ResidentChunk chunk))
			{
				throw new InvalidOperationException("Only resident chunks can be marked as sky-exposed.");
			}

			if (chunk.SkyExposedAbove == skyExposedAbove)
			{
				return;
			}

			chunk.SkyExposedAbove = skyExposedAbove;
			skyChangedChunks.Add(coordinate);
		}
	}

	public void RequestFullRebuild()
	{
		lock (sync)
		{
			ThrowIfDisposed();
			fullRebuildRequested = residents.Count != 0;
		}
	}

	public void MarkChunkDirty(ChunkCoordinate coordinate)
	{
		lock (sync)
		{
			ThrowIfDisposed();
			if (residents.ContainsKey(coordinate))
			{
				dirtyWorldChunks.Add(coordinate);
				dirtyWorldCells.Remove(coordinate);
			}
		}
	}

	public int Update(int? budget = null)
	{
		int actualBudget = budget ?? updateBudget;
		if (actualBudget <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(budget));
		}

		List<(ChunkCoordinate Coordinate, long Revision)> invalidated = null;
		int processed;

		lock (sync)
		{
			ThrowIfDisposed();
			processed = UpdateUnchecked(actualBudget, ref invalidated);
		}

		if (invalidated != null)
		{
			foreach ((ChunkCoordinate coordinate, long revision) in invalidated)
			{
				ChunkInvalidated?.Invoke(coordinate, revision);
			}
		}

		return processed;
	}

	public VoxelLight GetLight(int worldX, int worldY, int worldZ)
	{
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(
			worldX,
			worldY,
			worldZ,
			out int localX,
			out int localY,
			out int localZ
		);

		lock (sync)
		{
			ThrowIfDisposed();
			return residents.TryGetValue(coordinate, out ResidentChunk chunk)
				&& chunk.PublishedLights != null
				? new VoxelLight(chunk.PublishedLights[Index(localX, localY, localZ)])
				: default;
		}
	}

	internal bool TryCreateSnapshot(
		ChunkCoordinate coordinate,
		out VoxelLightChunkSnapshot snapshot
	)
	{
		if (!TryCaptureSnapshotSource(coordinate, out VoxelLightChunkSnapshotSource source))
		{
			snapshot = null;
			return false;
		}

		snapshot = source.Materialize();
		return true;
	}

	internal bool TryCaptureSnapshotSource(
		ChunkCoordinate coordinate,
		out VoxelLightChunkSnapshotSource source
	)
	{
		lock (sync)
		{
			ThrowIfDisposed();

			if (!residents.TryGetValue(coordinate, out ResidentChunk center)
				|| center.PublishedLights == null)
			{
				source = null;
				return false;
			}
			for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
			for (int offsetY = -1; offsetY <= 1; offsetY++)
			for (int offsetX = -1; offsetX <= 1; offsetX++)
			{
				ChunkCoordinate sample = coordinate + new ChunkCoordinate(
					offsetX,
					offsetY,
					offsetZ);
				if (IsLightingPending(sample))
				{
					source = null;
					return false;
				}
			}

			VoxelLightSnapshotContent[] contents = new VoxelLightSnapshotContent[27];

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
						contents[index] = CapturePublishedLightContent(sampleCoordinate);
					}
				}
			}

			source = new VoxelLightChunkSnapshotSource(
				coordinate,
				center.Generation,
				center.Revision,
				contents
			);
			return true;
		}
	}

	private bool IsLightingPending(ChunkCoordinate coordinate)
	{
		return dirtyWorldChunks.Contains(coordinate)
			|| dirtyWorldCells.ContainsKey(coordinate)
			|| addedChunks.Contains(coordinate)
			|| skyChangedChunks.Contains(coordinate)
			|| transaction?.Lookup.ContainsKey(coordinate) == true
			|| incrementalTransaction?.Chunks.ContainsKey(coordinate) == true;
	}

	private VoxelLightSnapshotContent CapturePublishedLightContent(
		ChunkCoordinate coordinate
	)
	{
		if (residents.TryGetValue(coordinate, out ResidentChunk resident)
			&& resident.PublishedLights != null)
		{
			return new VoxelLightSnapshotContent(
				resident.PublishedLights,
				resident.PublishedSkyExposedAbove
			);
		}

		return removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone tombstone)
			? new VoxelLightSnapshotContent(
				tombstone.PublishedLights,
				tombstone.PublishedSkyExposedAbove
			)
			: default;
	}

	internal bool TryGetChunkRevision(ChunkCoordinate coordinate, out long revision)
	{
		lock (sync)
		{
			ThrowIfDisposed();

			if (residents.TryGetValue(coordinate, out ResidentChunk chunk)
				&& chunk.PublishedLights != null)
			{
				revision = chunk.Revision;
				return true;
			}

			revision = 0;
			return false;
		}
	}

	internal bool IsResident(ChunkCoordinate coordinate)
	{
		lock (sync)
		{
			ThrowIfDisposed();
			return residents.ContainsKey(coordinate);
		}
	}

	internal bool TryGetChunkState(
		ChunkCoordinate coordinate,
		out long generation,
		out long revision
	)
	{
		lock (sync)
		{
			ThrowIfDisposed();
			if (residents.TryGetValue(coordinate, out ResidentChunk chunk)
				&& chunk.PublishedLights != null)
			{
				generation = chunk.Generation;
				revision = chunk.Revision;
				return true;
			}

			generation = 0;
			revision = 0;
			return false;
		}
	}

	private bool TryGetPublishedHaloLight(
		ChunkCoordinate coordinate,
		int index,
		out ushort light
	)
	{
		if (residents.TryGetValue(coordinate, out ResidentChunk resident)
			&& resident.PublishedLights != null)
		{
			light = resident.PublishedLights[index];
			return true;
		}
		if (removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone tombstone))
		{
			light = tombstone.PublishedLights[index];
			return true;
		}

		light = 0;
		return false;
	}

	private bool IsPublishedSkyExposed(ChunkCoordinate coordinate)
	{
		if (residents.TryGetValue(coordinate, out ResidentChunk resident)
			&& resident.PublishedLights != null)
		{
			return resident.PublishedSkyExposedAbove;
		}

		return removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone tombstone)
			&& tombstone.PublishedSkyExposedAbove;
	}

	internal bool IsCompatibleWith(VoxelWorld candidateWorld, VoxelPalette candidatePalette)
	{
		lock (sync)
		{
			ThrowIfDisposed();
			return ReferenceEquals(world, candidateWorld) && ReferenceEquals(palette, candidatePalette);
		}
	}

	public void Dispose()
	{
		lock (sync)
		{
			if (disposed)
			{
				return;
			}

			disposed = true;
			world.ContentChanged -= HandleWorldContentChanged;
			transaction = null;
			incrementalTransaction = null;
			residents.Clear();
			dirtyWorldChunks.Clear();
			dirtyWorldCells.Clear();
			addedChunks.Clear();
			removedChunks.Clear();
			skyChangedChunks.Clear();
			removedTombstones.Clear();
		}
	}

}
