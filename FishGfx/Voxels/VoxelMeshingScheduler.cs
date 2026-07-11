using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FishGfx.Voxels
{
	/// <summary>
	/// Builds revision-tagged voxel mesh data on worker threads without accessing mutable world storage.
	/// </summary>
	public sealed class VoxelMeshingScheduler : IDisposable
	{
		private readonly VoxelWorld world;
		private readonly VoxelPalette palette;
		private readonly VoxelAtlasLayout atlas;
		private readonly VoxelMeshingOptions options;
		private readonly int maxWorkers;
		private readonly object sync = new object();
		private readonly HashSet<ChunkCoordinate> dirty = new HashSet<ChunkCoordinate>();
		private readonly Dictionary<ChunkCoordinate, long> inFlight = new Dictionary<ChunkCoordinate, long>();
		private readonly List<Task> tasks = new List<Task>();
		private readonly ConcurrentQueue<VoxelMeshData> completed = new ConcurrentQueue<VoxelMeshData>();
		private readonly ConcurrentQueue<Exception> failures = new ConcurrentQueue<Exception>();
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
		private bool disposed;

		public VoxelMeshingScheduler(
			VoxelWorld world,
			VoxelPalette palette,
			VoxelAtlasLayout atlas,
			VoxelMeshingOptions options = null,
			int? maxWorkers = null
		)
		{
			this.world = world ?? throw new ArgumentNullException(nameof(world));
			this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
			this.atlas = atlas;
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
				throw new ArgumentOutOfRangeException(nameof(maxWorkers));

			world.ChunkInvalidated += OnChunkInvalidated;

			foreach (VoxelChunk chunk in world.LoadedChunks)
				dirty.Add(chunk.Coordinate);
		}

		public int PendingCount
		{
			get
			{
				lock (sync)
					return dirty.Count + inFlight.Count + completed.Count + failures.Count;
			}
		}

		public int InFlightCount
		{
			get
			{
				lock (sync)
					return inFlight.Count;
			}
		}

		public void MarkDirty(ChunkCoordinate coordinate)
		{
			ThrowIfDisposed();

			lock (sync)
				dirty.Add(coordinate);
		}

		public int SchedulePending()
		{
			ThrowIfDisposed();
			List<(VoxelChunkSnapshot Snapshot, ChunkCoordinate Coordinate)> jobs =
				new List<(VoxelChunkSnapshot, ChunkCoordinate)>();

			lock (sync)
			{
				tasks.RemoveAll(task => task.IsCompleted);
				int available = maxWorkers - inFlight.Count;

				if (available <= 0)
					return 0;

				foreach (ChunkCoordinate coordinate in dirty.ToArray())
				{
					if (available == 0)
						break;
					if (inFlight.ContainsKey(coordinate))
						continue;

					VoxelChunkSnapshot snapshot = world.CreateSnapshot(coordinate);
					dirty.Remove(coordinate);

					if (snapshot == null)
						continue;

					inFlight.Add(coordinate, snapshot.Revision);
					jobs.Add((snapshot, coordinate));
					available--;
				}
			}

			foreach ((VoxelChunkSnapshot snapshot, ChunkCoordinate coordinate) in jobs)
			{
				Task task = Task.Run(
					() => Build(snapshot, coordinate, cancellation.Token),
					cancellation.Token
				);

				lock (sync)
					tasks.Add(task);
			}

			return jobs.Count;
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
				return;

			disposed = true;
			world.ChunkInvalidated -= OnChunkInvalidated;
			cancellation.Cancel();

			Task[] outstanding;

			lock (sync)
				outstanding = tasks.ToArray();

			try
			{
				Task.WaitAll(outstanding);
			}
			catch (AggregateException exception)
			{
				exception.Handle(inner => inner is OperationCanceledException);
			}

			cancellation.Dispose();
		}

		private void Build(VoxelChunkSnapshot snapshot, ChunkCoordinate coordinate, CancellationToken token)
		{
			try
			{
				token.ThrowIfCancellationRequested();
				VoxelMeshData result = VoxelMesher.Build(snapshot, palette, atlas, options);
				token.ThrowIfCancellationRequested();
				completed.Enqueue(result);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				failures.Enqueue(
					new InvalidOperationException($"Voxel meshing failed for chunk {coordinate} revision {snapshot.Revision}.", exception)
				);
			}
			finally
			{
				lock (sync)
				{
					inFlight.Remove(coordinate);
					tasks.RemoveAll(task => task.IsCompleted);
				}
			}
		}

		private void OnChunkInvalidated(ChunkCoordinate coordinate, long _)
		{
			lock (sync)
				dirty.Add(coordinate);
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(VoxelMeshingScheduler));
		}
	}
}
