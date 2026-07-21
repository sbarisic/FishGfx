using System;
using System.Diagnostics;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer
{
	private void UpdateTransparentOrdering(Camera camera, float renderDistance)
	{
		transparentGpuTimer.Poll();

		if (transparentOrdering.TryTakeFailure(out Exception failure))
		{
			throw new InvalidOperationException(
				"Transparent voxel ordering failed on its worker thread.",
				failure
			);
		}

		long allocatedStart = GC.GetAllocatedBytesForCurrentThread();

		if (transparentSourceDirty || transparentSource == null)
		{
			RebuildTransparentOrderingSource();
		}
		DiscardStalePendingTransparentOrdering();
		if (transparentIndexUploadJob != null
			&& !IsTransparentResultCurrent(transparentIndexUploadJob.OrderingResult))
		{
			transparentStaleResults++;
			transparentIndexUploadJob.Dispose();
			transparentIndexUploadJob = null;
		}

		while (transparentOrdering.TryTakeCompleted(out VoxelTransparentOrderingResult completed))
		{
			if (!ReferenceEquals(completed.Source, transparentSource)
				|| completed.Source.GeometryRevision != transparentGeometryRevision
				|| completed.Source.ActiveSetGeneration != transparentActiveSetGeneration)
			{
				transparentStaleResults++;
				completed.Dispose();
				continue;
			}

			QueueTransparentOrdering(completed);
		}

		Vector3 cameraForward = camera.WorldForwardNormal;
		VoxelTransparentInvalidationReason reason = GetTransparentInvalidationReason(
			transparentGeometryRevision,
			transparentActiveSetGeneration,
			camera.Position,
			cameraForward
		);

		if (transparentSnapshot == null && transparentIndexUploadJob == null)
		{
			VoxelTransparentOrderingRequest initial = CreateTransparentOrderingRequest(
				camera,
				renderDistance,
				VoxelTransparentInvalidationReason.FirstFrame
			);
			VoxelTransparentOrderingResult result = transparentOrdering.BuildSynchronously(initial);
			QueueTransparentOrdering(result);
			SetTransparentRequestKey(camera.Position, cameraForward);
			transparentLastRequestReason = VoxelTransparentInvalidationReason.FirstFrame;
		}
		else if (reason != VoxelTransparentInvalidationReason.None)
		{
			VoxelTransparentOrderingRequest request = CreateTransparentOrderingRequest(
				camera,
				renderDistance,
				reason
			);
			transparentOrdering.Request(request);
			SetTransparentRequestKey(camera.Position, cameraForward);
			transparentLastRequestReason = reason;
		}

		ProcessTransparentIndexUpload();

		transparentMainThreadAllocatedBytes = checked(
			transparentMainThreadAllocatedBytes
				+ (int)(GC.GetAllocatedBytesForCurrentThread() - allocatedStart)
		);
	}

	private void RebuildTransparentOrderingSource()
	{
		long start = Stopwatch.GetTimestamp();
		int count = 0;

		for (int index = 0; index < activeGpuChunks.Count; index++)
		{
			if (HasTransparentGeometry(activeGpuChunks[index].Transparent))
			{
				count++;
			}
		}

		VoxelTransparentOrderingChunk[] chunks = new VoxelTransparentOrderingChunk[count];
		int destination = 0;

		for (int index = 0; index < activeGpuChunks.Count; index++)
		{
			GpuChunk chunk = activeGpuChunks[index];
			VoxelTransparentAllocation allocation = chunk.Transparent;

			if (!HasTransparentGeometry(allocation))
			{
				continue;
			}

			chunks[destination++] = new VoxelTransparentOrderingChunk(
				chunk.Bounds.Translate(chunk.Coordinate.WorldOrigin),
				allocation
			);
		}

		VoxelTransparentOrderingSource replacement = new(
			chunks,
			transparentGeometryRevision,
			transparentActiveSetGeneration
		);
		VoxelTransparentOrderingSource previous = transparentSource;
		transparentSource = replacement;
		transparentSourceDirty = false;
		previous?.ReleaseOwner();
		transparentSourceBuildMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
	}

	internal static bool HasTransparentGeometry(VoxelTransparentAllocation allocation)
	{
		return allocation is { VertexCount: > 0 };
	}

	private VoxelTransparentOrderingRequest CreateTransparentOrderingRequest(
		Camera camera,
		float renderDistance,
		VoxelTransparentInvalidationReason reason
	)
	{
		return new VoxelTransparentOrderingRequest(
			transparentSource,
			ViewFrustum.FromCamera(camera),
			camera.Position,
			camera.WorldForwardNormal,
			renderDistance,
			cullingEnabled,
			reason,
			++transparentRequestSequence
		);
	}

	private void QueueTransparentOrdering(VoxelTransparentOrderingResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		if (transparentIndexUploadJob != null)
		{
			// Do not restart a partially uploaded index buffer when a moving camera
			// produces a newer ordering. Finish publishing the current snapshot and
			// retain only the newest replacement behind it.
			KeepLatestPendingTransparentOrdering(result);
			return;
		}

		long applyStart = Stopwatch.GetTimestamp();

		try
		{
			transparentIndexUploadJob = transparentIndexRing.BeginUpload(
				transparentGeometry.Generation,
				result
			);
			transparentSortMilliseconds = result.SortMilliseconds;
			transparentWorkerAllocatedBytes = checked(
				transparentWorkerAllocatedBytes + result.WorkerAllocatedBytes
			);
		}
		catch
		{
			result.Dispose();
			throw;
		}
		transparentResultApplyMilliseconds += Stopwatch.GetElapsedTime(
			applyStart
		).TotalMilliseconds;
	}

	private void KeepLatestPendingTransparentOrdering(
		VoxelTransparentOrderingResult result)
	{
		if (pendingTransparentOrderingResult != null
			&& pendingTransparentOrderingResult.RequestSequence >= result.RequestSequence)
		{
			result.Dispose();
			return;
		}

		pendingTransparentOrderingResult?.Dispose();
		pendingTransparentOrderingResult = result;
	}

	private void DiscardStalePendingTransparentOrdering()
	{
		if (pendingTransparentOrderingResult == null
			|| IsTransparentResultCurrent(pendingTransparentOrderingResult))
		{
			return;
		}

		transparentStaleResults++;
		pendingTransparentOrderingResult.Dispose();
		pendingTransparentOrderingResult = null;
	}

	private void QueuePendingTransparentOrdering()
	{
		DiscardStalePendingTransparentOrdering();
		if (pendingTransparentOrderingResult == null)
			return;

		VoxelTransparentOrderingResult pending = pendingTransparentOrderingResult;
		pendingTransparentOrderingResult = null;
		QueueTransparentOrdering(pending);
	}

	private void ProcessTransparentIndexUpload()
	{
		if (transparentIndexUploadJob == null)
			return;
		int remainingBudget = options.MeshUploadByteBudget
			- checked((int)Math.Min(meshUploadBytes, int.MaxValue));
		if (remainingBudget <= 0
			|| (meshUploadMilliseconds >= options.MeshUploadTimeBudgetMilliseconds
				&& meshUploadBytes > 0))
		{
			return;
		}

		long uploadStart = Stopwatch.GetTimestamp();
		int bytes = transparentIndexUploadJob.UploadNextSlice(
			Math.Min(options.MeshUploadSliceBytes, remainingBudget)
		);
		transparentIndexUploadMilliseconds += Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
		transparentIndexUploadBytes = checked(transparentIndexUploadBytes + bytes);
		if (!transparentIndexUploadJob.IsComplete)
			return;

		VoxelTransparentOrderingResult result = transparentIndexUploadJob.OrderingResult;
		VoxelTransparentDrawSnapshot replacement = transparentIndexUploadJob.Complete();
		VoxelTransparentDrawSnapshot previous = transparentSnapshot;
		transparentSnapshot = replacement;
		previous?.Dispose();
		visibleTransparentFaces = result.FaceCount;
		visibleTransparentVertices = result.IndexCount;
		transparentIndexUploadJob.Dispose();
		transparentIndexUploadJob = null;
		QueuePendingTransparentOrdering();
	}

	private bool IsTransparentResultCurrent(VoxelTransparentOrderingResult result)
	{
		return ReferenceEquals(result.Source, transparentSource)
			&& result.Source.GeometryRevision == transparentGeometryRevision
			&& result.Source.ActiveSetGeneration == transparentActiveSetGeneration;
	}

	private void ResetTransparentFrameWorkDiagnostics()
	{
		transparentSourceBuildMilliseconds = 0;
		transparentResultApplyMilliseconds = 0;
		transparentIndexUploadMilliseconds = 0;
		transparentIndexUploadBytes = 0;
		transparentMainThreadAllocatedBytes = 0;
		transparentWorkerAllocatedBytes = 0;
	}

	private void SetTransparentRequestKey(Vector3 cameraPosition, Vector3 cameraForward)
	{
		transparentRequestKey = new VoxelTransparentCacheKey(
			transparentGeometryRevision,
			transparentActiveSetGeneration,
			cameraPosition,
			cameraForward
		);
		hasTransparentRequestKey = true;
	}
}
