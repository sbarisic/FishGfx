using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishGfx.Voxels;

/// <summary>
/// Builds world- and light-revision-tagged voxel mesh data on dedicated background workers.
/// </summary>
public sealed partial class VoxelMeshingScheduler : IDisposable
{
	private readonly VoxelWorld world;
	private readonly VoxelPalette palette;
	private readonly VoxelAtlasLayout atlas;
	private readonly VoxelMeshingOptions options;
	private readonly VoxelLighting lighting;
	private readonly int maxWorkers;
	private readonly bool poolMeshVertexBuffers;
	private readonly object sync = new object();
	private readonly HashSet<ChunkCoordinate> dirty = new HashSet<ChunkCoordinate>();
	private readonly Dictionary<ChunkCoordinate, MeshJobRevision> inFlight =
		new Dictionary<ChunkCoordinate, MeshJobRevision>();
	private readonly ConcurrentQueue<MeshJob> jobs = new ConcurrentQueue<MeshJob>();
	private readonly ConcurrentQueue<VoxelMeshData> completed = new ConcurrentQueue<VoxelMeshData>();
	private readonly ConcurrentQueue<Exception> failures = new ConcurrentQueue<Exception>();
	private readonly SemaphoreSlim jobSignal = new SemaphoreSlim(0);
	private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
	private readonly Task[] workers;
	private bool disposed;

	public VoxelMeshingScheduler(
		VoxelWorld world,
		VoxelPalette palette,
		VoxelAtlasLayout atlas,
		VoxelMeshingOptions options = null,
		int? maxWorkers = null,
		VoxelLighting lighting = null
	)
		: this(
			world,
			palette,
			atlas,
			options,
			maxWorkers,
			lighting,
			poolMeshVertexBuffers: false
		)
	{
	}

	internal VoxelMeshingScheduler(
		VoxelWorld world,
		VoxelPalette palette,
		VoxelAtlasLayout atlas,
		VoxelMeshingOptions options,
		int? maxWorkers,
		VoxelLighting lighting,
		bool poolMeshVertexBuffers
	)
	{
		this.world = world ?? throw new ArgumentNullException(nameof(world));
		this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
		this.atlas = atlas;
		this.lighting = lighting;
		this.poolMeshVertexBuffers = poolMeshVertexBuffers;

		if (lighting != null && !lighting.IsCompatibleWith(this.world, this.palette))
		{
			throw new ArgumentException(
				"Voxel lighting must use the same world and palette as the meshing scheduler.",
				nameof(lighting)
			);
		}

		VoxelMeshingOptions sourceOptions = options ?? new VoxelMeshingOptions();
		this.options = new VoxelMeshingOptions
		{
			AmbientOcclusion = sourceOptions.AmbientOcclusion,
			AoLevel1 = sourceOptions.AoLevel1,
			AoLevel2 = sourceOptions.AoLevel2,
			AoLevel3 = sourceOptions.AoLevel3,
		};
		this.maxWorkers = maxWorkers ?? Math.Max(1, Environment.ProcessorCount - 1);

		if (this.maxWorkers <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxWorkers));
		}

		if (poolMeshVertexBuffers)
		{
			PrewarmMeshVertexBuffers(this.maxWorkers);
		}

		world.ChunkInvalidated += OnChunkInvalidated;

		if (lighting != null)
		{
			lighting.ChunkInvalidated += OnLightChunkInvalidated;
		}

		foreach (VoxelChunk chunk in world.LoadedChunks)
		{
			dirty.Add(chunk.Coordinate);
		}

		workers = new Task[this.maxWorkers];

		for (int index = 0; index < workers.Length; index++)
		{
			workers[index] = Task.Factory.StartNew(
				WorkerLoop,
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default
			);
		}
	}

	public int PendingCount
	{
		get
		{
			lock (sync)
			{
				return dirty.Count + inFlight.Count + completed.Count + failures.Count;
			}
		}
	}

	public int InFlightCount
	{
		get
		{
			lock (sync)
			{
				return inFlight.Count;
			}
		}
	}

	internal int LastSelectionCount { get; private set; }
	internal int LastCaptureCount { get; private set; }

	public void MarkDirty(ChunkCoordinate coordinate)
	{
		ThrowIfDisposed();

		lock (sync)
		{
			dirty.Add(coordinate);
		}
	}

	public int SchedulePending()
	{
		return SchedulePending(focus: null);
	}

	internal int SchedulePending(VoxelMeshingFocus? focus)
	{
		ThrowIfDisposed();
		List<MeshJob> captured = new List<MeshJob>();

		lock (sync)
		{
			int available = maxWorkers - inFlight.Count;
			LastSelectionCount = 0;
			LastCaptureCount = 0;

			if (available <= 0)
			{
				return 0;
			}

			int selectionLimit = Math.Max(64, available * 4);
			ChunkCoordinate[] pending = SelectPending(dirty, focus, selectionLimit);
			LastSelectionCount = pending.Length;

			foreach (ChunkCoordinate coordinate in pending)
			{
				if (available == 0)
				{
					break;
				}

				if (inFlight.ContainsKey(coordinate))
				{
					continue;
				}

				if (!world.TryGetChunk(coordinate, out _))
				{
					dirty.Remove(coordinate);
					continue;
				}

				VoxelLightChunkSnapshotSource lightSource = null;

				if (lighting != null
					&& !lighting.TryCaptureSnapshotSource(coordinate, out lightSource))
				{
					if (!lighting.IsResident(coordinate))
					{
						dirty.Remove(coordinate);
					}

					continue;
				}

				VoxelChunkSnapshotSource worldSource = world.CaptureSnapshotSource(coordinate);

				if (worldSource == null)
				{
					dirty.Remove(coordinate);
					continue;
				}

				MeshJob job = new MeshJob(worldSource, lightSource);
				dirty.Remove(coordinate);
				inFlight.Add(coordinate, job.Revision);
				captured.Add(job);
				available--;
			}

			LastCaptureCount = captured.Count;
		}

		foreach (MeshJob job in captured)
		{
			jobs.Enqueue(job);
			jobSignal.Release();
		}

		return captured.Count;
	}

	public bool TryDequeue(out VoxelMeshData meshData)
	{
		ThrowIfDisposed();
		return completed.TryDequeue(out meshData);
	}

	public bool TryDequeueFailure(out Exception exception)
	{
		ThrowIfDisposed();
		return failures.TryDequeue(out exception);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		world.ChunkInvalidated -= OnChunkInvalidated;

		if (lighting != null)
		{
			lighting.ChunkInvalidated -= OnLightChunkInvalidated;
		}

		cancellation.Cancel();

		try
		{
			Task.WaitAll(workers);
		}
		catch (AggregateException exception)
		{
			exception.Handle(inner => inner is OperationCanceledException);
		}

		while (completed.TryDequeue(out VoxelMeshData result))
		{
			result.ReleasePooledVertexBuffers();
		}

		jobSignal.Dispose();
		cancellation.Dispose();
	}

	private void WorkerLoop()
	{
		Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
		CancellationToken token = cancellation.Token;

		while (true)
		{
			try
			{
				jobSignal.Wait(token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (jobs.TryDequeue(out MeshJob job))
			{
				Build(job, token);
			}
		}
	}

	private static void PrewarmMeshVertexBuffers(int workerCount)
	{
		const int firstLargeBuffer = 2_048;
		const int largestCommonBuffer = 32_768;
		List<VoxelVertex[]> rented = new List<VoxelVertex[]>();

		for (int size = firstLargeBuffer; size <= largestCommonBuffer; size *= 2)
		{
			for (int index = 0; index < workerCount; index++)
			{
				rented.Add(ArrayPool<VoxelVertex>.Shared.Rent(size));
			}
		}

		foreach (VoxelVertex[] buffer in rented)
		{
			ArrayPool<VoxelVertex>.Shared.Return(buffer);
		}
	}

	private void Build(MeshJob job, CancellationToken token)
	{
		ushort[] worldBuffer = null;
		ushort[] lightBuffer = null;
		VoxelMeshData result = null;

		try
		{
			token.ThrowIfCancellationRequested();

			if (IsEmpty(job.WorldSource.Center)
				|| IsProvablyOccluded(job.WorldSource))
			{
				result = CreateEmptyResult(job);
			}
			else
			{
				worldBuffer = ArrayPool<ushort>.Shared.Rent(
					VoxelChunkSnapshotSource.PaddedCellCount
				);
				VoxelChunkSnapshot snapshot = job.WorldSource.Materialize(worldBuffer);
				VoxelLightChunkSnapshot lightSnapshot = null;

				if (job.LightSource != null)
				{
					lightBuffer = ArrayPool<ushort>.Shared.Rent(
						VoxelLightChunkSnapshotSource.PaddedCellCount
					);
					lightSnapshot = job.LightSource.Materialize(lightBuffer);
				}

				token.ThrowIfCancellationRequested();
				result = VoxelMesher.Build(
					snapshot,
					palette,
					atlas,
					options,
					lightSnapshot,
					poolMeshVertexBuffers
				);
			}

			token.ThrowIfCancellationRequested();
			completed.Enqueue(result);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			result?.ReleasePooledVertexBuffers();
		}
		catch (Exception exception)
		{
			result?.ReleasePooledVertexBuffers();
			failures.Enqueue(
				new InvalidOperationException(
					$"Voxel meshing failed for chunk {job.Coordinate} world revision "
						+ $"{job.WorldSource.Revision} and light revision "
						+ $"{job.LightSource?.Revision ?? 0}.",
					exception
				)
			);
		}
		finally
		{
			if (worldBuffer != null)
			{
				ArrayPool<ushort>.Shared.Return(worldBuffer);
			}

			if (lightBuffer != null)
			{
				ArrayPool<ushort>.Shared.Return(lightBuffer);
			}

			lock (sync)
			{
				inFlight.Remove(job.Coordinate);
			}
		}
	}

	private static VoxelMeshData CreateEmptyResult(MeshJob job)
	{
		return new VoxelMeshData(
			job.Coordinate,
			job.WorldSource.Generation,
			job.WorldSource.Revision,
			job.LightSource?.Generation ?? 0,
			job.LightSource?.Revision ?? 0,
			Array.Empty<VoxelVertex>(),
			Array.Empty<VoxelVertex>(),
			Array.Empty<VoxelTransparentFace>(),
			AxisAlignedBoundingBox.Empty
		);
	}

	private void OnChunkInvalidated(ChunkCoordinate coordinate, long _)
	{
		lock (sync)
		{
			dirty.Add(coordinate);
		}
	}

	private void OnLightChunkInvalidated(ChunkCoordinate coordinate, long _)
	{
		lock (sync)
		{
			dirty.Add(coordinate);
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(VoxelMeshingScheduler));
		}
	}

	private sealed class MeshJob
	{
		internal MeshJob(
			VoxelChunkSnapshotSource worldSource,
			VoxelLightChunkSnapshotSource lightSource
		)
		{
			WorldSource = worldSource;
			LightSource = lightSource;
			Revision = new MeshJobRevision(
				worldSource.Generation,
				worldSource.Revision,
				lightSource?.Generation ?? 0,
				lightSource?.Revision ?? 0
			);
		}

		internal ChunkCoordinate Coordinate => WorldSource.Coordinate;
		internal VoxelChunkSnapshotSource WorldSource { get; }
		internal VoxelLightChunkSnapshotSource LightSource { get; }
		internal MeshJobRevision Revision { get; }
	}

	private readonly struct MeshJobRevision
	{
		internal MeshJobRevision(
			long worldGeneration,
			long worldRevision,
			long lightGeneration,
			long lightRevision
		)
		{
			WorldGeneration = worldGeneration;
			WorldRevision = worldRevision;
			LightGeneration = lightGeneration;
			LightRevision = lightRevision;
		}

		internal long WorldGeneration { get; }
		internal long WorldRevision { get; }
		internal long LightGeneration { get; }
		internal long LightRevision { get; }
	}
}
