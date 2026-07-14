using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FishGfx.Voxels
{
	/// <summary>
	/// Builds world- and light-revision-tagged voxel mesh data on worker threads from immutable snapshots.
	/// </summary>
	public sealed class VoxelMeshingScheduler : IDisposable
	{
		private readonly VoxelWorld world;
		private readonly VoxelPalette palette;
		private readonly VoxelAtlasLayout atlas;
		private readonly VoxelMeshingOptions options;
		private readonly VoxelLighting lighting;
		private readonly int maxWorkers;
		private readonly object sync = new object();
		private readonly HashSet<ChunkCoordinate> dirty = new HashSet<ChunkCoordinate>();
		private readonly Dictionary<ChunkCoordinate, MeshJobRevision> inFlight =
			new Dictionary<ChunkCoordinate, MeshJobRevision>();
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
			int? maxWorkers = null,
			VoxelLighting lighting = null
		)
		{
			this.world = world ?? throw new ArgumentNullException(nameof(world));
			this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
			this.atlas = atlas;
			this.lighting = lighting;
			if (lighting != null && !lighting.IsCompatibleWith(this.world, this.palette))
				throw new ArgumentException(
					"Voxel lighting must use the same world and palette as the meshing scheduler.",
					nameof(lighting)
				);
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
			if (lighting != null)
				lighting.ChunkInvalidated += OnLightChunkInvalidated;

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
			List<(
				VoxelChunkSnapshot Snapshot,
				VoxelLightChunkSnapshot LightSnapshot,
				ChunkCoordinate Coordinate
			)> jobs = new List<(VoxelChunkSnapshot, VoxelLightChunkSnapshot, ChunkCoordinate)>();

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

					if (snapshot == null)
					{
						dirty.Remove(coordinate);
						continue;
					}

					VoxelLightChunkSnapshot lightSnapshot = null;

					if (lighting != null && !lighting.TryCreateSnapshot(coordinate, out lightSnapshot))
						continue;

					dirty.Remove(coordinate);
					inFlight.Add(
						coordinate,
						new MeshJobRevision(
							snapshot.Generation,
							snapshot.Revision,
							lightSnapshot?.Generation ?? 0,
							lightSnapshot?.Revision ?? 0
						)
					);
					jobs.Add((snapshot, lightSnapshot, coordinate));
					available--;
				}
			}

			foreach ((
				VoxelChunkSnapshot snapshot,
				VoxelLightChunkSnapshot lightSnapshot,
				ChunkCoordinate coordinate
			) in jobs)
			{
				Task task = Task.Run(
					() => Build(snapshot, lightSnapshot, coordinate, cancellation.Token),
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
			if (lighting != null)
				lighting.ChunkInvalidated -= OnLightChunkInvalidated;
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

		private void Build(
			VoxelChunkSnapshot snapshot,
			VoxelLightChunkSnapshot lightSnapshot,
			ChunkCoordinate coordinate,
			CancellationToken token
		)
		{
			try
			{
				token.ThrowIfCancellationRequested();
				VoxelMeshData result = VoxelMesher.Build(
					snapshot,
					palette,
					atlas,
					options,
					lightSnapshot
				);
				token.ThrowIfCancellationRequested();
				completed.Enqueue(result);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				failures.Enqueue(
					new InvalidOperationException(
						$"Voxel meshing failed for chunk {coordinate} world revision {snapshot.Revision} and light revision {lightSnapshot?.Revision ?? 0}.",
						exception
					)
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

		private void OnLightChunkInvalidated(ChunkCoordinate coordinate, long _)
		{
			lock (sync)
				dirty.Add(coordinate);
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(VoxelMeshingScheduler));
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
}
