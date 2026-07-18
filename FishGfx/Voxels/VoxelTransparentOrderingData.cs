using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal readonly record struct VoxelTransparentOrderingChunk(
	AxisAlignedBoundingBox Bounds,
	VoxelTransparentAllocation Allocation
);

internal sealed class VoxelTransparentOrderingSource
{
	private readonly VoxelTransparentOrderingChunk[] chunks;
	private int ownerReleased;
	private int retainedCount;
	private int released;

	internal VoxelTransparentOrderingSource(
		VoxelTransparentOrderingChunk[] chunks,
		long geometryRevision,
		long activeSetGeneration
	)
	{
		this.chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
		GeometryRevision = geometryRevision;
		ActiveSetGeneration = activeSetGeneration;
		int faceCount = 0;
		int indexCount = 0;
		int retained = 0;

		try
		{
			for (int index = 0; index < chunks.Length; index++)
			{
				VoxelTransparentAllocation allocation = chunks[index].Allocation;
				allocation.Retain();
				retained++;
				faceCount = checked(faceCount + allocation.FaceRecords.Length);
				indexCount = checked(indexCount + allocation.VertexCount);
			}
		}
		catch
		{
			for (int index = 0; index < retained; index++)
			{
				chunks[index].Allocation.ReleaseRetained();
			}

			throw;
		}

		FaceCapacity = faceCount;
		IndexCapacity = indexCount;
	}

	internal ReadOnlySpan<VoxelTransparentOrderingChunk> Chunks => chunks;

	internal long GeometryRevision { get; }

	internal long ActiveSetGeneration { get; }

	internal int FaceCapacity { get; }

	internal int IndexCapacity { get; }

	internal void Retain()
	{
		while (true)
		{
			if (Volatile.Read(ref released) != 0)
			{
				throw new ObjectDisposedException(nameof(VoxelTransparentOrderingSource));
			}

			int current = Volatile.Read(ref retainedCount);

			if (Interlocked.CompareExchange(ref retainedCount, current + 1, current) == current)
			{
				return;
			}
		}
	}

	internal void ReleaseRetained()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The transparent ordering source has no retained reference to release.");
		}

		TryRelease();
	}

	internal void ReleaseOwner()
	{
		if (Interlocked.Exchange(ref ownerReleased, 1) == 0)
		{
			TryRelease();
		}
	}

	private void TryRelease()
	{
		if (Volatile.Read(ref ownerReleased) == 0 || Volatile.Read(ref retainedCount) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref released, 1) != 0)
		{
			return;
		}

		for (int index = 0; index < chunks.Length; index++)
		{
			chunks[index].Allocation.ReleaseRetained();
		}
	}
}

internal readonly record struct VoxelTransparentOrderingRequest(
	VoxelTransparentOrderingSource Source,
	ViewFrustum Frustum,
	Vector3 CameraPosition,
	Vector3 CameraForward,
	float MaxRenderDistance,
	bool CullingEnabled,
	VoxelTransparentInvalidationReason Reason,
	long RequestSequence
);

internal sealed class VoxelTransparentOrderingResult : IDisposable
{
	private uint[] indices;
	private bool disposed;

	internal VoxelTransparentOrderingResult(
		VoxelTransparentOrderingSource source,
		uint[] indices,
		int indexCount,
		int faceCount,
		int visibleChunkCount,
		Vector3 cameraPosition,
		Vector3 cameraForward,
		VoxelTransparentInvalidationReason reason,
		long requestSequence,
		double sortMilliseconds,
		int workerAllocatedBytes
	)
	{
		Source = source ?? throw new ArgumentNullException(nameof(source));
		this.indices = indices ?? throw new ArgumentNullException(nameof(indices));
		Source.Retain();
		IndexCount = indexCount;
		FaceCount = faceCount;
		VisibleChunkCount = visibleChunkCount;
		CameraPosition = cameraPosition;
		CameraForward = cameraForward;
		Reason = reason;
		RequestSequence = requestSequence;
		SortMilliseconds = sortMilliseconds;
		WorkerAllocatedBytes = workerAllocatedBytes;
		CompletedTimestamp = Stopwatch.GetTimestamp();
	}

	internal VoxelTransparentOrderingSource Source { get; }

	internal ReadOnlySpan<uint> Indices => indices.AsSpan(0, IndexCount);

	internal int IndexCount { get; }

	internal int FaceCount { get; }

	internal int VisibleChunkCount { get; }

	internal Vector3 CameraPosition { get; }

	internal Vector3 CameraForward { get; }

	internal VoxelTransparentInvalidationReason Reason { get; }

	internal long RequestSequence { get; }

	internal double SortMilliseconds { get; }

	internal int WorkerAllocatedBytes { get; }

	internal long CompletedTimestamp { get; }

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		if (indices.Length > 0)
		{
			ArrayPool<uint>.Shared.Return(indices);
			indices = Array.Empty<uint>();
		}

		Source.ReleaseRetained();
	}
}
