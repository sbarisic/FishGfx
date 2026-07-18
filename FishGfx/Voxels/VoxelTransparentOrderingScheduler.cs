using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal sealed class VoxelTransparentOrderingScheduler : IDisposable
{
	private readonly object sync = new();
	private readonly AutoResetEvent signal = new(false);
	private readonly CancellationTokenSource cancellation = new();
	private readonly ConcurrentQueue<Exception> failures = new();
	private readonly Task worker;
	private PendingRequest pending;
	private VoxelTransparentOrderingResult completed;
	private bool running;
	private bool disposed;
	private int coalescedRequests;
	private int droppedResults;

	internal VoxelTransparentOrderingScheduler()
	{
		worker = Task.Factory.StartNew(
			WorkerLoop,
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default
		);
	}

	internal bool IsIdle
	{
		get
		{
			lock (sync)
			{
				return pending == null && !running && completed == null;
			}
		}
	}

	internal bool IsRunning
	{
		get
		{
			lock (sync)
			{
				return running;
			}
		}
	}

	internal bool HasPending
	{
		get
		{
			lock (sync)
			{
				return pending != null;
			}
		}
	}

	internal int CoalescedRequests => Volatile.Read(ref coalescedRequests);

	internal int DroppedResults => Volatile.Read(ref droppedResults);

	internal void Request(in VoxelTransparentOrderingRequest request)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(request.Source);
		request.Source.Retain();
		PendingRequest replacement = new(request);
		PendingRequest previous;

		lock (sync)
		{
			previous = pending;
			pending = replacement;
		}

		if (previous != null)
		{
			previous.Dispose();
			Interlocked.Increment(ref coalescedRequests);
		}

		signal.Set();
	}

	internal VoxelTransparentOrderingResult BuildSynchronously(
		in VoxelTransparentOrderingRequest request
	)
	{
		ThrowIfDisposed();
		return Build(request);
	}

	internal bool TryTakeCompleted(out VoxelTransparentOrderingResult result)
	{
		ThrowIfDisposed();

		lock (sync)
		{
			result = completed;
			completed = null;
			return result != null;
		}
	}

	internal bool TryTakeFailure(out Exception exception)
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
		cancellation.Cancel();
		signal.Set();

		try
		{
			worker.Wait();
		}
		catch (AggregateException exception)
			when (ContainsOnlyCancellationExceptions(exception))
		{
		}

		PendingRequest pendingToDispose;
		VoxelTransparentOrderingResult completedToDispose;

		lock (sync)
		{
			pendingToDispose = pending;
			pending = null;
			completedToDispose = completed;
			completed = null;
		}

		pendingToDispose?.Dispose();
		completedToDispose?.Dispose();
		signal.Dispose();
		cancellation.Dispose();
	}

	internal static VoxelTransparentOrderingResult Build(
		in VoxelTransparentOrderingRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request.Source);
		long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
		long sortStart = Stopwatch.GetTimestamp();
		int faceCapacity = Math.Max(1, request.Source.FaceCapacity);
		VoxelTransparentSortEntry[] entries = ArrayPool<VoxelTransparentSortEntry>.Shared.Rent(faceCapacity);
		int faceCount = 0;
		int indexCount = 0;
		int visibleChunkCount = 0;
		float distanceSquared = request.MaxRenderDistance * request.MaxRenderDistance;
		uint[] indices = null;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadOnlySpan<VoxelTransparentOrderingChunk> chunks = request.Source.Chunks;

			for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				VoxelTransparentOrderingChunk chunk = chunks[chunkIndex];
				Vector3 center = chunk.Bounds.Center;

				if (request.CullingEnabled
					&& (Vector3.DistanceSquared(request.CameraPosition, center) > distanceSquared
						|| !request.Frustum.Intersects(chunk.Bounds)))
				{
					continue;
				}

				visibleChunkCount++;
				VoxelTransparentFaceRecord[] faces = chunk.Allocation.FaceRecords;

				for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
				{
					if ((faceIndex & 4095) == 0)
					{
						cancellationToken.ThrowIfCancellationRequested();
					}

					VoxelTransparentFaceRecord face = faces[faceIndex];
					float depth = Vector3.Dot(
						face.WorldCenter - request.CameraPosition,
						request.CameraForward
					);
					entries[faceCount++] = new VoxelTransparentSortEntry(face, depth);
					indexCount = checked(indexCount + face.VertexCount);
				}
			}

			Array.Sort(entries, 0, faceCount, VoxelTransparentSortEntryComparer.Instance);
			cancellationToken.ThrowIfCancellationRequested();
			indices = indexCount == 0
				? Array.Empty<uint>()
				: ArrayPool<uint>.Shared.Rent(indexCount);
			int destination = 0;

			for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
			{
				VoxelTransparentFaceRecord face = entries[faceIndex].Face;

				for (int vertexIndex = 0; vertexIndex < face.VertexCount; vertexIndex++)
				{
					indices[destination++] = checked(face.FirstVertex + (uint)vertexIndex);
				}
			}

			double milliseconds = Stopwatch.GetElapsedTime(sortStart).TotalMilliseconds;
			int allocated = checked((int)(GC.GetAllocatedBytesForCurrentThread() - allocatedStart));

			VoxelTransparentOrderingResult result = new(
				request.Source,
				indices,
				indexCount,
				faceCount,
				visibleChunkCount,
				request.CameraPosition,
				request.CameraForward,
				request.Reason,
				request.RequestSequence,
				milliseconds,
				allocated
			);
			indices = null;
			return result;
		}
		finally
		{
			if (indices?.Length > 0)
			{
				ArrayPool<uint>.Shared.Return(indices);
			}

			ArrayPool<VoxelTransparentSortEntry>.Shared.Return(entries, clearArray: true);
		}
	}

	private static bool ContainsOnlyCancellationExceptions(AggregateException exception)
	{
		for (int index = 0; index < exception.InnerExceptions.Count; index++)
		{
			if (exception.InnerExceptions[index] is not OperationCanceledException)
			{
				return false;
			}
		}

		return true;
	}

	private void WorkerLoop()
	{
		WaitHandle[] handles = { signal, cancellation.Token.WaitHandle };

		while (WaitHandle.WaitAny(handles) == 0)
		{
			if (cancellation.IsCancellationRequested)
			{
				return;
			}

			PendingRequest work;

			lock (sync)
			{
				work = pending;
				pending = null;
				running = work != null;
			}

			if (work == null)
			{
				continue;
			}

			try
			{
				VoxelTransparentOrderingResult result = Build(
					work.Request,
					cancellation.Token
				);
				VoxelTransparentOrderingResult previous;

				lock (sync)
				{
					previous = completed;
					completed = result;
					running = false;
				}

				if (previous != null)
				{
					previous.Dispose();
					Interlocked.Increment(ref droppedResults);
				}
			}
			catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
			{
				lock (sync)
				{
					running = false;
				}
			}
			catch (Exception exception)
			{
				failures.Enqueue(exception);

				lock (sync)
				{
					running = false;
				}
			}
			finally
			{
				work.Dispose();
			}
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}

	private sealed class PendingRequest : IDisposable
	{
		internal PendingRequest(in VoxelTransparentOrderingRequest request)
		{
			Request = request;
		}

		internal VoxelTransparentOrderingRequest Request { get; }

		public void Dispose()
		{
			Request.Source.ReleaseRetained();
		}
	}

	private readonly record struct VoxelTransparentSortEntry(
		VoxelTransparentFaceRecord Face,
		float Depth
	);

	private sealed class VoxelTransparentSortEntryComparer
		: System.Collections.Generic.IComparer<VoxelTransparentSortEntry>
	{
		internal static readonly VoxelTransparentSortEntryComparer Instance = new();

		public int Compare(VoxelTransparentSortEntry left, VoxelTransparentSortEntry right)
		{
			int result = right.Depth.CompareTo(left.Depth);

			if (result == 0)
			{
				result = left.Face.Coordinate.X.CompareTo(right.Face.Coordinate.X);
			}

			if (result == 0)
			{
				result = left.Face.Coordinate.Y.CompareTo(right.Face.Coordinate.Y);
			}

			if (result == 0)
			{
				result = left.Face.Coordinate.Z.CompareTo(right.Face.Coordinate.Z);
			}

			return result != 0
				? result
				: left.Face.FaceIndex.CompareTo(right.Face.FaceIndex);
		}
	}
}
